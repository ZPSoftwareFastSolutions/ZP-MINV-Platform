# Reglas de facturación SIAT · M-INV V4.1 (Facturación Computarizada en Línea)

> **Documento normativo** para personas y agentes que trabajen en la facturación (rama `Inventario-V4.1`:
> `MINV.Domain/Billing`, `MINV.Application/Billing`, `MINV.Infrastructure/Billing`, `MINV.SiatSimulator`, pantallas de
> Facturación). **DEBE** = obligatorio · **NO DEBE** = prohibido · **PUEDE** = permitido. Complementa las reglas de la V3
> (A-01…A-13) y de la V4 (B-01…B-17). Diseño: `docs/architecture/facturacion-siat-v4.1.md`. Normativa del SIN resumida
> y citada: `docs/billing/investigacion-siat/` (especificaciones 00 a 08).

## 1. Reglas

### F-01 · Modalidad y documentos
- M-INV emite en la modalidad **Computarizada en Línea** (`codigoModalidad = 2`): factura Compra Venta (sector 1, tipo 1)
  y nota Crédito-Débito (sector 24, tipo 3). Otros sectores o la modalidad electrónica NO DEBEN mezclarse con estos
  sin una especificación nueva.
- Sector 1 → recurso «Servicio Factura Compra Venta»; tipo 3 → «Documentos de Ajuste»; el resto → «Facturación
  Computarizada» (`SiatDocumentRef.Resource`).

### F-02 · El SIN solo se toca por el puerto
- Todo acceso al SIN DEBE pasar por `ISiatGateway`. NO DEBE haber SOAP, URL ni cabeceras del SIN fuera de
  `MINV.Infrastructure/Billing/Soap` y del simulador.
- La falta de comunicación DEBE lanzar `SiatUnavailableException`; un rechazo del SIN es una RESPUESTA (`SiatReply`), no
  una excepción.

### F-03 · Emisión dentro de la venta, envío después del COMMIT
- El documento fiscal (CUF, XML validado, líneas congeladas) DEBE crearse en la MISMA transacción que la venta.
- NO DEBE llamarse al SIN dentro de esa transacción: el envío lo hacen `DispatchFiscalDocumentsCommand` (la caja, al
  terminar de cobrar) y el trabajo en segundo plano (`ISiatWorker`).
- Las operaciones administrativas (CUIS, CUFD, sincronización, puntos de venta, eventos, anulación, reversión,
  verificación) SON casos de uso cuyo propósito es la llamada al SIN: llaman y guardan la respuesta.

### F-04 · CUF, hora fiscal y numeración
- El CUF DEBE generarse con `Cuf.Generate` y la MISMA marca de tiempo que `fechaEmision` (con milisegundos).
- La hora fiscal DEBE salir del reloj corregido con `sincronizarFechaHora` en la zona de la empresa, sin sufijo de zona.
- La numeración DEBE ser correlativa por (ambiente, punto de venta, documento sector) con índice único y reintento.

### F-05 · XML exacto y validado
- El XML DEBE seguir el orden del XSD oficial, con TODOS los elementos (los vacíos con `xsi:nil="true"`), UTF-8 sin
  BOM, sin namespace y números con punto; DEBE validarse contra el XSD embebido antes de guardarse. Un XML que no valida
  NO DEBE enviarse.
- `archivo` = GZIP del XML; paquetes = GZIP(TAR); `hashArchivo` = SHA-256 hexadecimal minúscula de los bytes GZIP.

### F-06 · Montos
- Todo cálculo fiscal DEBE hacerse en `decimal` con redondeo HALF-UP a 2 decimales por línea antes de sumar
  (`FiscalRules.Round2`); `double` está prohibido.
- El monto total fiscal de una venta DEBE ser igual al total cobrado por M-INV. Los totales se derivan de las líneas
  (no se guardan).
- La tarjeta DEBE guardarse, enviarse, auditarse e imprimirse SOLO enmascarada (`FiscalRules.MaskCard`).

### F-07 · Catálogos y homologación
- Los códigos de catálogos (métodos de pago, unidades, motivos de anulación, eventos, monedas, tipos de documento)
  DEBEN salir de la sincronización diaria; NO DEBEN fijarse en el código salvo las constantes de protocolo de
  `SiatCodes`. Los eventos significativos se eligen por DESCRIPCIÓN (su numeración cambió entre versiones).
- No se factura un producto, unidad o medio de pago sin homologar: la venta DEBE rechazarse con un mensaje claro.
- Lo que el SIN retira de un catálogo se marca `IsCurrent = false`; NO DEBE borrarse.

### F-08 · Comprador (nominatividad)
- Toda factura DEBE llevar número de documento del comprador; CI y NIT solo dígitos; complemento solo con CI.
- `codigoExcepcion = 1` con NIT especial (99001/99002/99003), cuando el cajero lo pide con NIT, y SIEMPRE con NIT fuera
  de línea. Con CI, CEX, pasaporte u otro documento DEBE ser 0.

### F-09 · Fuera de línea y recuperación
- Dos fallos de comunicación seguidos DEBEN pasar el punto de venta a fuera de línea con un evento abierto; la caja NO
  DEBE bloquearse: emite con tipo de emisión 2 y el último CUFD (≤ 72 h desde su obtención).
- La recuperación DEBE seguir el orden: CUFD nuevo → `registroEventoSignificativo` (`cufd` nuevo, `cufdEvento` el del
  evento) → verificación de los documentos sin respuesta (anular duplicados) → paquetes ≤ 500 del mismo sector →
  validación hasta 908/904.
- Un documento sin respuesta DEBE re-emitirse fuera de línea con número y CUF nuevos (la venta tiene UN documento
  activo) y verificarse después por CUF.
- Las notas crédito-débito NO DEBEN emitirse fuera de línea: quedan en cola hasta que vuelva la comunicación.

### F-10 · Anulación y reversión
- La anulación DEBE rechazarse localmente después del fin del día 9 del mes siguiente a la emisión
  (`FiscalRules.VoidDeadline`); la reversión, una sola vez y dentro del mismo plazo. 905/936 = anulado; 907/978 =
  revertido.
- Si la anulación no responde NO DEBE entrarse en contingencia: se verifica el estado antes de reintentar.
- La anulación fiscal NO devuelve mercadería por sí sola: «anular y devolver» es explícito (`ReturnGoods`).
- DEBE notificarse al comprador (correo) cada anulación y reversión, y quedar registrada la entrega.

### F-11 · Documento inmutable
- `fiscal_document_lines`, `fiscal_document_files`, `fiscal_document_events`, `fiscal_deliveries`, `siat_cuis`,
  `siat_cufds`, `siat_sync_runs`, `customer_nit_checks` y `siat_service_calls` son append-only.
- El estado del documento solo cambia por los métodos de `FiscalDocument`, y cada cambio DEBE dejar su fila en
  `fiscal_document_events`.

### F-12 · Secretos
- El token delegado y la contraseña SMTP DEBEN cifrarse con `ISecretProtector`; NO DEBEN aparecer en logs, auditoría,
  excepciones, `ToString()`, bitácora SOAP ni documentación. La bitácora SOAP guarda los cuerpos SIN la cabecera `apikey`.

### F-13 · Ambientes separados
- CUIS, CUFD, puntos de venta, eventos, documentos, tokens y URL DEBEN llevar su ambiente; nada del ambiente 2 (pruebas)
  se reutiliza en producción. La representación gráfica del ambiente 2 DEBE llevar «SIN VALOR LEGAL».

### F-14 · Sucursales
- Todo lo operativo de la facturación (puntos de venta, CUIS, CUFD, documentos, eventos, paquetes, CAFC) DEBE ser
  `IBranchScoped` con las mismas garantías de la V4 (B-02). La configuración de la empresa y los catálogos no se
  filtran por sucursal.

### F-15 · Quién habla con el SIN
- En modo nube solo `MINV.CloudServer` habla con el SIN (el escritorio NUNCA recibe el token). En modo local lo hace el
  escritorio si tiene la clave maestra; en la demostración, el simulador en memoria.

### F-16 · Simulador
- `MINV.SiatSimulator` y `InProcessSiatGateway` DEBEN implementar el mismo contrato que el cliente real y validar como el
  SIN (XSD, hash, CUF, vigencias, plazos). Un cambio del contrato SOAP DEBE hacerse en ambos lados a la vez.
- Los datos del simulador (catálogos, leyendas) son de prueba: en producción mandan los sincronizados.

### F-17 · Definición de terminado
Un cambio en la facturación está terminado solo si, además de B-17: pasan las pruebas del CUF con los vectores
oficiales, las de XML contra XSD, las del cliente SOAP contra el simulador HTTP y el flujo completo (en línea, fuera de
línea con recuperación, anulación, reversión y nota) en memoria y, con `MINV_TEST_PG`, en PostgreSQL.

## 2. Checklist para agentes

- [ ] ¿Se llama al SIN dentro de la transacción de una venta? → rechazar (F-03).
- [ ] ¿Un XML sin validar contra el XSD, o un elemento opcional omitido en vez de `xsi:nil`? → corregir (F-05).
- [ ] ¿`double` en un monto, o totales guardados en columnas? → `decimal` y derivar (F-06).
- [ ] ¿Un código de catálogo fijo en el código (salvo `SiatCodes`)? → catálogo sincronizado (F-07).
- [ ] ¿La caja se bloquea sin internet? → fuera de línea automático (F-09).
- [ ] ¿Anulación sin controlar el día 9 o reversión repetida? → `FiscalRules.VoidDeadline`, `IsReverted` (F-10).
- [ ] ¿Token o tarjeta completa en un log, auditoría o `ToString()`? → rechazar (F-06, F-12).
- [ ] ¿Cambio del contrato SOAP solo en el cliente o solo en el simulador? → ambos (F-16).
