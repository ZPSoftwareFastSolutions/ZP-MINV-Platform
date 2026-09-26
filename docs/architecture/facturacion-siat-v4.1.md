# Facturación SIAT · M-INV V4.1 (Facturación Computarizada en Línea)

> Rama `Inventario-V4.1` (sobre `Inventario-V4.-BaseDeDatosNube`). Documento de **diseño**: explica qué se construyó, por
> qué y cómo encajan las piezas. Las reglas obligatorias están en `.claude/v41-billing-rules.md` (F-01 … F-20). La
> investigación completa de la normativa del SIN (9 especificaciones, 630 KB, con cada dato citado a su página de
> `siatinfo.impuestos.gob.bo`) está en `docs/billing/investigacion-siat/`.

## 0. Qué es y qué no es

M-INV emite **documentos fiscales digitales** del SIAT (Servicio de Impuestos Nacionales de Bolivia) en la modalidad
**Facturación Computarizada en Línea** (`codigoModalidad = 2`, sin firma digital):

| Documento | Sector | Tipo | Servicio del SIN |
|---|---|---|---|
| Factura Compra Venta | 1 | 1 (con derecho a crédito fiscal) | Servicio Factura Compra Venta |
| Nota Crédito-Débito (devoluciones) | 24 | 3 (documento de ajuste) | Documentos de Ajuste |

Fuera de alcance en la V4.1 (preparado, no implementado): modalidad electrónica (firma digital), emisión masiva,
sectores especiales (47, 35, 41…), Registro de Compras por servicio web (el libro de compras sí se genera).

## 1. Flujo general

```text
 CAJA / API ──► CheckoutCommand ──► SaleWriter ──► FiscalIssuer ──► fiscal_documents (Pendiente o Fuera de línea)
                  (UNA transacción: venta, stock, pago, asiento, documento fiscal con CUF + XML validado contra el XSD)
                                                         │ después del COMMIT (regla F-03)
                                                         ▼
 DispatchFiscalDocumentsCommand ──► ISiatGateway.ReceiveDocumentAsync ──► SIN (recepcionFactura, gzip + SHA-256)
   (lo llama la caja al terminar de cobrar y el      │ 908 Válida · 902/904 Rechazada · sin respuesta → FUERA DE LÍNEA
    despachador en segundo plano cada pocos segundos) ▼
                                          fiscal_document_events (bitácora append-only de cada respuesta)
 FUERA DE LÍNEA: evento significativo abierto, facturas con tipo de emisión 2 y el último CUFD válido (≤ 72 h)
 RECUPERACIÓN (automática): verificarComunicacion → CUFD nuevo → registroEventoSignificativo → verificación de las
   facturas sin respuesta → paquetes GZIP(TAR) ≤ 500 → recepcionPaqueteFactura → validacionRecepcionPaqueteFactura
```

## 2. Modelo de datos (esquema `billing`, 5FN)

Todas las tablas llevan `tenant_id` (RLS `tenant_isolation`); las marcadas **S** llevan además `branch_id`
(`IBranchScoped`, RLS `branch_isolation` RESTRICTIVA y FK compuestas con la sucursal); las marcadas **A** son
append-only (`trg_append_only`).

| Tabla | Qué guarda | S | A |
|---|---|---|---|
| `siat_settings` | Una fila por empresa: NIT, razón social, código de sistema, ambiente activo (1 producción / 2 pruebas), leyendas de modo, desfase del reloj con el SIN | | |
| `siat_environment_profiles` | Por ambiente: token delegado **cifrado** (AES-256-GCM, `ISecretProtector`), vigencia del token, URL de cada recurso SOAP, namespace, URL base del QR, tiempo máximo de espera | | |
| `siat_branches` | Sucursal M-INV ↔ `codigoSucursal` del Padrón (0 = casa matriz), municipio y teléfono | | |
| `siat_points_of_sale` | Punto de venta del SIN por sucursal y ambiente (código 0 = sin punto de venta), tipo, caja vinculada, **modo** (en línea / fuera de línea / contingencia manual / recuperando) | S | |
| `siat_cuis` | Historial de CUIS (365 días) por punto de venta | S | A |
| `siat_cufds` | Historial de CUFD (24 h): código, **código de control**, dirección, vigencia | S | A |
| `siat_catalog_items` | Paramétricas sincronizadas (clave: catálogo + código) | | |
| `siat_activities` · `siat_activity_sectors` · `siat_legends` · `siat_products` | Actividades del NIT, actividad ↔ documento sector, leyendas Ley 453 por actividad, productos y servicios del SIN | | |
| `siat_sync_runs` | Cada sincronización (catálogo, ambiente, filas, error) | | A |
| `product_siat_codes` · `unit_siat_codes` · `payment_method_siat_codes` | **Homologación**: producto → (actividad, código de producto SIN); unidad → unidad SIN; medio de pago → método de pago SIN. Tablas propias: los atributos fiscales opcionales no ensucian los maestros con columnas nulas | | |
| `customer_nit_checks` | Cada verificación de NIT (código 986/994…) | | A |
| `fiscal_documents` | El documento fiscal (agregado): ambiente, punto de venta, CUIS y CUFD usados, sector, tipo, tipo de emisión, **número**, **CUF**, fecha y hora fiscal con milisegundos, datos del comprador **congelados**, medio de pago, estado, anulación/reversión, evento y paquete | S | |
| `fiscal_note_references` | Subtipo 1:1 de las notas: factura original (documento M-INV o transcrita: número, CUF, fecha) y descuento prorrateado | S | |
| `fiscal_document_lines` | Detalle **congelado** del documento (actividad, código SIN, código propio, descripción, cantidad, unidad SIN, precio, descuento, transacción 1/2 de las notas) | S | A |
| `fiscal_document_files` | XML exacto (validado contra el XSD) y SHA-256 del GZIP | S | A |
| `fiscal_document_events` | Bitácora de cada interacción con el SIN y de cada acción del usuario (enviado, válido, rechazado, anulado, revertido, entregado…) con códigos y mensajes | S | A |
| `fiscal_deliveries` | Entregas al comprador (correo con XML + PDF, impresión) | S | A |
| `significant_events` | Eventos significativos (fuera de línea o contingencia manual CAFC): catálogo, inicio/fin con milisegundos, CUFD del evento y CUFD de envío, código de recepción, estado | S | |
| `fiscal_packages` | Paquetes de contingencia (≤ 500, mismo sector y evento): hash, código de recepción, estado de validación | S | |
| `contingency_codes` | CAFC (talonarios de contingencia manual) por sucursal y sector | S | |
| `siat_service_calls` | Bitácora técnica de cada llamada SOAP **sin el token** (recurso, operación, duración, HTTP, código SIAT, cuerpos) | | A |
| `mail_settings` | Servidor SMTP de la empresa (contraseña cifrada) para entregar XML + PDF | | |

En `sales`: `customers` gana `document_type` (1 CI, 2 CEX, 3 PAS, 4 OD, 5 NIT) y `complement`; nuevas
`sales_returns` + `sales_return_lines` (devoluciones parciales con stock, reembolso y nota crédito-débito).

**Normalización.** Los totales NO se guardan: `montoTotal = Σ subTotal − descuentoAdicional`,
`subTotal = round2(cantidad × precio) − descuento`, `montoTotalSujetoIva = montoTotal − montoGiftCard`,
`montoTotalOriginal = Σ subTotal (tx 1)`, `montoTotalDevuelto = Σ subTotal (tx 2) − descuento`,
`montoEfectivoCreditoDebito = round2(devuelto × 0,13)` se calculan en el dominio (`FiscalDocument`) y en la vista
`billing.v_fiscal_document_totals` (libros). Los datos del comprador y el detalle del documento son **instantáneas
legales** (redundancia controlada documentada, como `tenant_id`): el documento emitido no puede cambiar si mañana
cambia el cliente o el catálogo.

## 3. Estados

**Documento fiscal** (`FiscalDocumentStatus`):

| Estado | Significado | Sale a |
|---|---|---|
| `Pending` | Emitido en línea (tipo de emisión 1), todavía no enviado | `Valid` · `Rejected` · `NoResponse` |
| `Valid` | 908 (o validado dentro de un paquete) | `Voided` (anulación) |
| `Rejected` | 902/904 en línea: el cliente recibe uno nuevo (número y CUF nuevos) | — |
| `NoResponse` | Se perdió la respuesta al enviarlo: la venta se re-emite fuera de línea | `Valid` (el SIN lo tenía y no se re-emitió) · `DuplicateToVoid` · `Discarded` |
| `Offline` | Emitido fuera de línea (tipo 2) durante un evento | `InPackage` |
| `InPackage` | Enviado en un paquete, esperando la validación | `Valid` · `PackageRejected` |
| `PackageRejected` | Error propio en la validación del paquete | re-emisión |
| `DuplicateToVoid` | Registrado en el SIN y además re-emitido fuera de línea | `Voided` |
| `Voided` | 905/936 | `Valid` con `IsReverted = true` (reversión, una sola vez) |
| `Discarded` | Nunca llegó al SIN y se re-emitió | — |

`IsReverted = true` impide volver a anular. `VoidUncertain`/`Voiding` no se guardan: si la anulación no responde, el
caso de uso verifica el estado en el SIN antes de reintentar (no se entra en contingencia).

**Punto de venta** (`SiatConnectionMode`): `Online → Offline` (dos fallos de comunicación) `→ Recovering → Online`;
`Online → ManualContingency` (el usuario declara corte de energía / falla de software o hardware) `→ Recovering`.

**Evento significativo**: `Open → Closed → Registered → PackagesSent → Reconciled | WithObservations`.
**Paquete**: `Sent → Validated | Observed | Rejected`.

## 4. Algoritmos (dominio, sin dependencias)

- **CUF** (`Cuf.Generate`): NIT(13) + `yyyyMMddHHmmssfff`(17) + sucursal(4) + modalidad(1) + tipo de emisión(1) + tipo
  de documento(1) + sector(2) + número(10) + punto de venta(4) → + dígito Módulo 11 → Base 16 en mayúsculas (sin el `0`
  inicial que agrega `BigInteger.ToString("X")`) → + código de control del CUFD. Verificado con el ejemplo oficial
  (`8727F63A15F8976591FDDE5B387C5D015A29E06A1A19E23EF34124CD`). La fecha del CUF **es** `fechaEmision`.
- **Redondeo**: `decimal` HALF-UP a 2 decimales (`MidpointRounding.AwayFromZero`) por línea antes de sumar.
- **Plazo de anulación y reversión**: hasta el fin del día 9 del mes siguiente a la emisión (hora del SIN).
- **Tarjeta**: se guarda y se envía SOLO enmascarada (4 primeros + ceros + 4 últimos).
- **Hora fiscal**: reloj del servidor + desfase medido con `sincronizarFechaHora`, en la zona de la empresa
  (`America/La_Paz`), sin sufijo de zona (`yyyy-MM-ddTHH:mm:ss.fff`).

## 5. Puertos y adaptadores

| Puerto (Application) | Implementación | Para qué |
|---|---|---|
| `ISiatGateway` | `SiatSoapGateway` (SOAP 1.1 sobre `HttpClient`, cabecera `apikey: TokenApi <token>`, análisis tolerante por nombre local); `InProcessSiatGateway` (simulador en memoria: demostración y pruebas) | Códigos, sincronización, operaciones, recepción, paquetes, anulación, reversión, verificación |
| `IFiscalDocumentSerializer` | `SiatXmlSerializer` (XML del sector 1 y 24 en el orden del XSD, `xsi:nil`, validación contra los XSD oficiales embebidos, GZIP, TAR, SHA-256) | XML y archivos a enviar |
| `IFiscalDocumentRenderer` | `FiscalPdfRenderer` (PDF media carta propio, sin dependencias: fuentes estándar Helvetica y QR vectorial) · `FiscalRollRenderer` (ESC/POS 80 mm con QR nativo, en `MINV.Hardware`) | Representación gráfica |
| `IMailSender` | `SmtpMailSender` | Entrega de XML + PDF al comprador |
| `ISiatCallLog` | escribe `siat_service_calls` con un contexto propio | Bitácora técnica (sin token) |

Las **URL, el namespace y los nombres de las operaciones** del SIN NO están publicados en la documentación (hueco H-01):
todo es configuración por ambiente (`siat_environment_profiles`) y los nombres de elementos SOAP están centralizados en
`SiatSoapContract`. El **simulador** (`src/4. Tools/MINV.SiatSimulator`, puerto 5095) implementa el mismo contrato,
valida el XML contra el XSD, el hash, el CUF y la vigencia del CUFD, y permite simular cortes: con él se prueba TODO el
flujo sin token real. Antes de producción se confirma el contrato con el WSDL del ambiente piloto del SIN.

## 6. Dónde corre el envío al SIN

| Modo del escritorio | Quién habla con el SIN |
|---|---|
| Demostración (memoria) | El propio escritorio con el simulador en memoria |
| Base local | El escritorio (despachador en segundo plano) con la clave maestra de este equipo |
| Nube | Solo `MINV.CloudServer` (el escritorio nunca ve el token) |

La caja, al terminar de cobrar, envía `DispatchFiscalDocumentsCommand` para su documento (respuesta en segundos) y
recién entonces imprime: así el comprador siempre recibe el documento definitivo (en línea validado o re-emitido fuera
de línea).

## 7. Interfaz (escritorio)

- **Punto de venta**: datos de facturación del comprador (tipo y número de documento, complemento, razón social,
  correo, verificación de NIT, NIT especiales 99001/99002/99003), estado fiscal al cobrar y ticket fiscal con QR.
- **Facturación › Documentos fiscales**: búsqueda, detalle (CUF, estado, mensajes del SIN, bitácora, XML), reimpresión
  en rollo o PDF, envío por correo, verificación de estado, anulación (motivo del catálogo, plazo), reversión, nota
  crédito-débito (devolución).
- **Facturación › Estado SIAT**: por sucursal y punto de venta: modo, CUIS, CUFD, token, catálogos, eventos, paquetes y
  plazos con cuenta regresiva; acciones de CUIS, CUFD, sincronizar, verificar comunicación, declarar/cerrar contingencia.
- **Facturación › Homologación**: productos, unidades y medios de pago contra los catálogos del SIN.
- **Facturación › Libros**: libro de ventas IVA y libro de compras del período (CSV y Excel) y resumen IVA/IT.
- **Configuración › Facturación SIAT**: NIT, razón social, código de sistema, ambiente, token (solo escritura), URL,
  sucursales del Padrón, puntos de venta, correo SMTP.

## 8. Módulo comercial y permisos

Módulo `FISCAL_SIAT` «Facturación SIAT (computarizada en línea)». Permisos: `billing.view`, `billing.issue`,
`billing.void` (anular, revertir, notas), `billing.contingency` (eventos, paquetes, CAFC), `billing.configure`
(configuración, CUIS/CUFD, puntos de venta, sincronización, homologación).

| Rol | Permisos |
|---|---|
| Administrador | todos |
| Gerencia | view, void, contingency |
| Ventas | view, issue |
| Cajero | view, issue |
| Bodega | — |
| Consulta | view |
