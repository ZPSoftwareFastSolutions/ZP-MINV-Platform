# 00 · Índice, contradicciones resueltas, huecos abiertos y requisitos consolidados

**Proyecto:** M-INV, rama `Inventario-V4.1`, módulo de **Facturación SIAT** (Bolivia).
**Modalidad:** Facturación **Computarizada en Línea** (`codigoModalidad = 2`, sin firma digital). Lo que solo sirve para la Electrónica en Línea (modalidad 1) se marca **⚠ ELECTRÓNICA**.
**Rol de este documento:** es el resultado de la revisión de completitud y consistencia de las especificaciones 01 a 08. La revisión volvió a leer la documentación original (`scratchpad/siat/txt` y `scratchpad/siat/adjuntos`) en los seis puntos críticos, corrigió los errores directamente en las especificaciones (cada corrección lleva la marca **[corregido por revisión]**) y consolida aquí las decisiones.

**Convenciones**
- **NO DOCUMENTADO**: el dato no está en el corpus descargado. No se completó de memoria.
- **INFERIDO**: se deduce combinando fuentes, sin frase literal.
- **FUERA DE DOC**: conocimiento externo al corpus. Solo sirve como pista y hay que confirmarlo en piloto.
- **DECISIÓN M-INV**: elección de diseño para implementar, no exigencia del SIN.
- Los archivos fuente se citan por su nombre en `scratchpad/siat/txt/`. El prefijo largo `facturacion-en-linea__implementacion-servicios-facturacion__` se abrevia `IS__`.

---

## 1. Índice de especificaciones

| # | Archivo | Alcance | Secciones clave |
|---|---|---|---|
| 01 | `01-codigos-operaciones-conexion.md` | Servicio de **Códigos** (CUIS, CUFD, masivos, verificarNit), servicio de **Operaciones** (puntos de venta, eventos significativos, cierres), token delegado, requisitos del sistema, esquemas de despliegue y conexión, casos de prueba CUIS/CUFD | §2 token · §4 Códigos · §5 Operaciones · §6 despliegue · §8 requisitos mínimos · §9 URLs (NO DOCUMENTADO) · §10 puesta en marcha · §11 requisitos (40) |
| 02 | `02-servicios-facturacion-computarizada.md` | Recursos SOAP de envío: **Servicio Factura Compra Venta**, **Facturación Computarizada en Línea**, **Documentos de Ajuste**; formato de `archivo` y `hashArchivo`; estados; casos de prueba de emisión, paquetes, masiva y anulación | §1 respuestas directas · §2 enrutamiento · §5 GZIP/TAR/SHA-256 con código .NET · §6 operaciones · §8 notas · §10 códigos · §11 plazos · §12 secuencias · §13 homologación · §14 requisitos (24) |
| 03 | `03-emision-contingencia-anulacion.md` | Emisión individual, **ingreso a contingencia**, eventos significativos, paquetes fuera de línea, **CAFC**, masiva, verificación de estado, **anulación**, **reversión**, comprobante de transacción | §5 cinco casos de fallo · §6 eventos · §7 paquetes · §8 CAFC · §11 anulación · §12 reversión · §14 plazos con ejemplos · §16 máquinas de estado · §17 requisitos (32) |
| 04 | `04-catalogos-errores-homologacion.md` | **Sincronización** (18 catálogos), tabla completa de **193 códigos** de respuesta, documentos sector (51), **homologación de productos** y análisis del catálogo SIN | §2 sectores · §3 sincronización · §4 códigos · §5–6 homologación y catálogo ferretería · §7 requisitos (15) |
| 04a | `04-catalogo-productos-sin-completo.csv` | Catálogo de productos SIN completo (21 555 filas, `;`, UTF-8) | — |
| 04b | `04-catalogo-productos-sin-ferreteria.csv` | Subconjunto ferretería y construcción (276 filas) para precarga | — |
| 05 | `05-xml-xsd-validaciones.md` | **XML/XSD** campo por campo (sectores 1, 24, 47; variantes 2, 8, 9, 35, 29), fórmulas de validación, precisión decimal, pruebas de XSD con .NET | §1 formato general · §2 Compra Venta · §3 fórmulas V1–V5 · §4 Nota 24 · §5 Nota 47 · §9 decimales · §10 casos límite · §13 requisitos (31) |
| 06 | `06-representacion-grafica-qr-redondeo.md` | **CUF** (verificado con 6 vectores), Módulo 11, Base 16, SHA-256, GZIP, **redondeo HALF-UP**, **QR**, títulos, **representación gráfica** hoja y rollo, leyendas, monto literal | §1 CUF + C# · §6 redondeo · §7 QR · §10 títulos · §11–12 plantillas · §13 leyendas · §14 requisitos (19) |
| 06a | `06-anexos/verificar_cuf.py`, `qr-sfv-campos.png`, `redondeo-imagen-pagina.png` | Script que reproduce los vectores del CUF; imágenes extraídas | — |
| 07 | `07-notas-credito-debito-casos-especiales.md` | **Notas Crédito-Débito** (24, 47, 48), modalidades, Tasa Cero, casos especiales, **Registro de Compras y Ventas (RCV)** y servicio de Registro de Compras | §3 notas · §4 tasa cero · §5 RCV · §6 casos especiales · §7 requisitos (29) |
| 08 | `08-autorizacion-inspeccion-versionamiento.md` | **Autorización** del sistema, **Fases I–III**, checklist de inspección, asociación, inicio de operaciones, renovación, **versionamiento 2021–2026** | §2 Fase I con casos por sector · §2.5 códigos de evento · §3 inspección II-1…II-15 · §6 inicio de operaciones · §8 versiones · §10 de cero a producción · §11 requisitos M-INV-1…28 |
| — | `_work02/`, `_work08/`, `_tools/` | Volcados de los Excel de casos de prueba (incluida la planilla de reversión, que no estaba en `adjuntos/`), páginas de versionamiento 2025–2026 completas y scripts de lectura | — |

**Mapa rápido por tema**

| Tema | Dónde |
|---|---|
| Token y cabecera HTTP | 01 §2 · 02 §3.1 · 03 §2.1 |
| CUIS / CUFD | 01 §4 · 08 M-INV-5/6 |
| Sincronización y catálogos | 04 §3 · 08 §2.2 |
| XML sector 1 | 05 §2–3 |
| CUF | 06 §1–3 |
| `archivo` / `hashArchivo` / paquetes | 02 §5 |
| Emisión en línea | 02 §6.1 · 03 §4 |
| Contingencia, eventos, paquetes, CAFC | 03 §5–8, §16 · 02 §12 · 01 §5.6 |
| Anulación y reversión | 02 §6.2–6.3 · 03 §11–12 |
| Notas crédito-débito | 05 §4–5 · 07 §3 · 02 §8 |
| Representación gráfica, QR, leyendas | 06 §7–13 · 07 §3.13 |
| Códigos de respuesta | 04 §4 (tabla completa) |
| Homologación de productos | 04 §5–6 · 01 §8.7 |
| Autorización y pruebas | 08 · 02 §13 · 01 §10 |
| RCV y compras | 07 §5 |

---

## 2. Verificación de los seis puntos críticos contra la fuente

### 2.1 Servicio SOAP y operación para factura compra-venta y nota crédito-débito

**Fuentes verificadas:** `IS__servicio-factura-compra-venta__recepcion-factura-compra-venta.md`, `IS__facturacion-computarizada__recepcion-factura-computarizada.md` (idénticas salvo el contador de visitas), `IS__nota-credito-debito-comp__recepcion-nota-credito-debito-computarizada.md`, menú del sitio en `versionamiento__versionamiento-2021.md` (líneas 682–757), `IS__codigos-error-siat.md` (error 932).

| Documento | Sector | `tipoFacturaDocumento` | Recurso SOAP (menú del sitio) | Operación ("Nombre Método") | Objeto de solicitud | `codigoEmision` | `codigoModalidad` |
|---|---|---|---|---|---|---|---|
| Factura Compra Venta (individual) | 1 | 1 | **Servicio Factura Compra Venta** (INFERIDO, ver abajo) | `RecepcionFactura` | `SolicitudServicioRecepcionFactura` | 1 | 2 |
| Paquete fuera de línea / CAFC | 1 | 1 | Servicio Factura Compra Venta | `RecepcionPaqueteFactura` → `validacionRecepcionPaqueteFactura` | `SolicitudServicioRecepcionPaquete` / `…ValidacionRecepcionPaquete` | 2 | 2 |
| Anulación / reversión / verificación | 1 | 1 | Servicio Factura Compra Venta | `AnulacionFactura` / `ReversionAnulacionFactura` / `verificacionEstadoFactura` | `SolicitudServicioAnulacionFactura` / `…ReversionAnulacionFactura` / `…VerificaEstadoFactura` | 1 | 2 |
| Nota Crédito-Débito | 24 (47, 48) | 3 | **Documentos de Ajuste** (documentado: "Documentos de Ajuste que incluyen las Notas Crédito - Débito y las Notas de Conciliación") | `recepcionDocumentoAjuste` | `SolicitudServicioRecepcionDocumentoAjuste` | 1 (único valor) | 2 |
| Anulación / verificación / reversión de nota | 24 | 3 | Documentos de Ajuste | `anulacionDocumentoAjuste` / `verificacionEstadoDocumentoAjuste` / `reversionAnulacionDocumentoAjuste` | `SolicitudServicio…DocumentoAjuste` | 1 | 2 |

- **Resultado: CORRECTO en 02 y 07.** El enrutamiento "sector 1 → Servicio Factura Compra Venta" es **INFERIDO con alta confianza**: todas las páginas dicen que los servicios "se hallan publicados de forma diferenciada por tipo de documentos sector", el menú tiene un grupo propio "Servicio Factura Compra Venta" cuyas páginas admiten modalidad 1 y 2, y existe el error **932 "El Parámetro Código Documento Sector No Corresponde Al Servicio"**. Ninguna página lo dice literalmente.
- **Corregido:** 03 y 08 citaban las páginas del recurso "Facturación Computarizada en Línea" sin decir que el sector 1 va al recurso Compra Venta. Se añadió la nota en 03 §4.1 y en 08 M-INV-14.
- Las **notas no tienen** operaciones de paquete ni masivas: solo se envían en línea, una por una.
- Todas las operaciones de envío devuelven `codigoEstado`, `codigoRecepcion` (salvo anulación y reversión), `codigosRespuestas` y `transaccion`; la estructura interna del DTO de mensajes es NO DOCUMENTADA (texto: "códigos, descripciones, número de archivo y número de detalle").

### 2.2 Formato exacto del archivo enviado

**Fuentes verificadas:** `facturacion-en-linea__emision-y-envio-de-facturas__emision-y-envio.md`, `facturacion-en-linea__requerimientos__sistema-informatico.md`, `…__algoritmos-utilizados__comprimir-gzip.md`, `…__generacion-de-sha-256-md5-y-crc32.md`, `registro-de-compras-y-ventas__registro-de-compras-serv__recepcion-paquete-compras.md` y `…__confirmacioncompras.md`, `…fase-i-pruebas.md`.

| Caso | `archivo` | `hashArchivo` | Límites |
|---|---|---|---|
| Individual (factura o nota) | `GZIP( bytes UTF-8 del XML validado )` | `SHA-256(bytes GZIP)` en **hexadecimal minúscula, 64 caracteres** | 1 documento; detalle ≤ 500 líneas (XSD) |
| Paquete fuera de línea o CAFC | `GZIP( TAR( xml_1 … xml_n ) )` (**DECISIÓN M-INV**, ver C-02) | `SHA-256(bytes .tar.gz)` | ≤ **500** facturas, **mismo documento sector**, un solo evento (y un solo CAFC); `cantidadFacturas` exacta (error 985); ≤ 100 MB (971) |
| Masiva (no la usará M-INV) | igual que el paquete | igual | ≤ **1000** (F1 Etapa IX dice 2000, ver C-05) |

- Texto literal: "Comprimir el archivo XML en formato Gzip, mismo que debe ser enviado en la etiqueta archivo" y "Obtener el HASH (SHA 256) del archivo compreso obtenido en el paso anterior … (también llamado Huella Digital)". En computarizada ese hash **es la huella**; no hay firma.
- SHA-256 publicado: `DatatypeConverter.printHexBinary(digest).toLowerCase()`.
- La codificación de transporte (Base64 / `base64Binary` / `byte[]`) es **NO DOCUMENTADA**: el tipo figura como "Alfanumérico". Con proxies generados desde el WSDL se pasa el `byte[]` y el serializador hace el Base64. El hash se calcula **sobre los bytes GZIP**, nunca sobre el Base64 (ver C-03).
- **Verificado por la revisión:** con el XML oficial `facturaComputarizadaCompraVenta.xml` (2 353 bytes) comprimido con `gzip.compress(mtime=0)` se obtienen 1 001 bytes que empiezan por `1f 8b 08 00` y cuyo SHA-256 es `04b9ebcdedb27915142629aeca2bfa3b31ac985ba6768c9c05dfe5471b8741f2`; `SHA-256("abc")` = `ba7816bf…15ad`. Los bytes GZIP varían entre librerías, así que el hash siempre se calcula sobre los bytes que efectivamente se envían.
- El ejemplo Java de GZIP nombra la salida `.zip`, pero el formato es **GZIP (RFC 1952)**.

### 2.3 XML computarizado de compra-venta contra el XSD

**Fuentes verificadas:** `adjuntos/xml/CompraVentaXML/facturaComputarizadaCompraVenta.xsd` (ver. 23/08/2021) y `.xml`, `…archivos-xml-xsd-de-facturas-electronicas__factura-de-compra-y-venta.md`.

- **Resultado: 05 §2.1–2.2 es CORRECTO campo por campo.** Cabecera de **30** elementos y detalle de **11**, en el orden exacto del XSD, con los mismos tipos, longitudes, rangos, `nillable` y `fixed="1"` en `codigoDocumentoSector`.
- **Verificado por la revisión con `XmlSchemaSet` de .NET:** el XML oficial valida; al quitar `<telefono>` (nillable) falla ("Lista esperada de elementos posibles: 'telefono'"); con `subTotal = 0` falla por `MinExclusive`; la nota oficial sector 24 valida contra su XSD.
- Reglas que el programador no debe olvidar:
  1. Raíz `facturaComputarizadaCompraVenta` **sin namespace**, con `xmlns:xsi` y `xsi:noNamespaceSchemaLocation="facturaComputarizadaCompraVenta.xsd"`; `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>`.
  2. **Ningún elemento se omite**: los opcionales van con `xsi:nil="true"`.
  3. Orden del XSD, **no** el de la tabla de la página (la página lista `montoGiftCard … cafc` antes de `codigoMoneda`; el XSD los pone después de `montoTotalMoneda`).
  4. Montos, `cantidad` y `precioUnitario` del sector 1 con **2 decimales** (`dec(17,2)`); `montoTotal`, `subTotal`, `cantidad`, `precioUnitario`, `tipoCambio`, `montoTotalMoneda` > 0.
  5. `detalle` de 1 a 500.
  6. Fórmulas (`validaciones-documentos-sector__validaciones.md`): `subTotal = cantidad × precioUnitario − montoDescuento`; `montoTotal = Σ subTotal − descuentoAdicional`; `montoTotalMoneda = montoTotal / tipoCambio`; `montoTotalSujetoIva = montoTotal − montoGiftCard` (con gift card) o `= montoTotal`.
  7. `fechaEmision` = la **misma** marca de tiempo que va en el CUF (ver 2.4).

### 2.4 Algoritmo del CUF

**Fuentes verificadas:** `…algoritmos-utilizados__generacion-cuf.md`, `…__algoritmo-modulo-11.md`, `…__base-16.md`.

- Cadena de **53 dígitos**: NIT (13) + fecha `yyyyMMddHHmmssSSS` (17) + sucursal (4) + modalidad (1) + tipo de emisión (1) + tipo factura/documento de ajuste (1) + documento sector (2) + número (10) + punto de venta (4), todos con ceros a la izquierda.
- Se agrega el dígito **Módulo 11** (`calculaDigitoMod11(cadena, 1, 9, false)`: pesos 2…9 de derecha a izquierda, resto 10 → "1") → 54 dígitos → **Base 16 en mayúsculas** → se concatena el **`codigoControl`** que devolvió `solicitudCufd`.
- **Verificado independientemente por la revisión** (script Python propio, además del de 06): ejemplo oficial `8727F63A15F8976591FDDE5B387C5D015A29E06A1A19E23EF34124CD` ✓; con modalidad 2, `…4128CF31A2C8606A3A19E23EF34124CD` ✓; los CUF del XML computarizado, de la URL del QR y de la nota decodifican a cadenas con Módulo 11 correcto y modalidad 2. El CUF del XML confirma que la fecha es **exactamente `fechaEmision` con milisegundos**.
- Trampas documentadas en 06: el orden de campos de la página del Módulo 11 es incorrecto (manda la del CUF); `BigInteger.ToString("X")` de .NET antepone un `0` cuando el primer nibble es ≥ 8 y hay que quitarlo (`TrimStart('0')`); para decodificar hay que anteponer `"0"` al hex.
- En notas: tipo 3, sector 24, número = `numeroNotaCreditoDebito` (INFERIDO; el QR usa "número correlativo de la Factura o Nota").
- **Resultado: 06 CORRECTO.** Solo se corrigió que el comentario "(hora de Bolivia)" del código es una recomendación, no un dato documentado.

### 2.5 Plazos de anulación y de contingencia

Todos verificados literalmente en las fuentes indicadas.

| Regla | Plazo | Fuente literal |
|---|---|---|
| Anulación (factura o nota) | Individual, **hasta el día 9 del mes siguiente** a la emisión; documento válido y **no usado en una DDJJ**; desde la misma u otra sucursal habilitada; **notificar al comprador** (código de autorización, número, motivo) | `…emision-y-envio-de-facturas__anulacion-de-documentos-fiscales.md` |
| Anulación en sectores de exportación | 180 días (no aplica) | `…fase-ii-inspeccion.md` punto 11 |
| Reversión de anulación | **Por única vez**, hasta el **día 9 del mes siguiente a la emisión de la factura original**; lo revertido no se puede volver a anular; notificar al comprador | `…reversion-anulacion-documentos-fiscales.md` |
| Registro del evento significativo | Hasta **48 h** después de finalizada la contingencia | `…contingencia-y-eventos-significativos.md` |
| Envío de paquetes fuera de línea | Dentro de las **48 h** posteriores a la recuperación | `…fase-ii-inspeccion.md` punto 8 |
| Transcripción y envío de facturas manuales (CAFC) | Máximo **72 h** después del restablecimiento | `…casos-especiales__manuales-contingencia.md`; `…fase-ii-inspeccion.md` punto 9 |
| Vigencia del CUFD si falla `solicitudCufd` | "se amplía hasta a **72 horas**" (punto de partida NO DOCUMENTADO, ver C-19) | `…ingreso-a-contingencia.md` |
| Espera fuera de línea antes de reintentar | "no mayor a **dos horas**" | `…ingreso-a-contingencia.md` |
| CUFD | 24 h, a diario | `informacion__codigos-de-autorizacion.md`; `IS__codigos__solicitud-cufd.md` |
| CUIS | 365 días; renovable desde el **5.º día anterior** al vencimiento | mismas páginas; `IS__codigos__solicitud-cuis.md` |
| Nota crédito-débito | Hasta **18 meses** después de la factura original | `informacion__tipos-facturas.md` |
| Autorización del sistema | 90 días (+ 60 de prórroga); vigencia 3 años | `…guia-de-usuario-prorroga.md`; `…renovacion-autorizacion.md` |

**Resultado:** los plazos son **consistentes** entre las ocho especificaciones. Se añadió a 03 que el punto de partida de las 72 h del CUFD no está documentado.

### 2.6 URLs, WSDL y autenticación por token

- **Token (VERIFICADO):** `…emision-y-envio-de-facturas__solicitud-token.md` dice textualmente `headers.put("apikey", Arrays.asList("TokenApi " + pToken));`. En .NET: cabecera HTTP **`apikey: TokenApi <token>`** (un espacio después de `TokenApi`) en **todas** las llamadas. Se genera en el Portal SIAT ("Token Delegado Piloto" / "Token Delegado en Producción", campo "Hasta" elegido por el usuario) y se renueva a mano (inactivar con X → "Generar Nuevo Token"). El token de producción **es distinto** del de piloto (`…caracteristicas-sfvl__inicio-operaciones.md`). Error 989 = token inválido. **Coherente en 01, 02, 03 y 04.**
- **URLs y WSDL: NO DOCUMENTADOS.** Se buscó `wsdl` y todas las URL del corpus: no hay ninguna de servicios. Lo documentado es que las URL de prueba vienen en el **reporte de registro del sistema** (`…proceso-de-autorizacion.md`) y las de producción se entregan al hacer **Inicio de Operaciones** (`…inicio-operaciones.md`). También son NO DOCUMENTADOS el `targetNamespace`, la versión de SOAP, las mayúsculas exactas de las operaciones y la estructura del DTO de mensajes.
- **FUERA DE DOC (pista consolidada de 01 §9 y 02 §2.4; confirmar en piloto y dejar todo configurable):**

  | Recurso | Nombre habitual | Piloto (patrón) | Producción (patrón) |
  |---|---|---|---|
  | Códigos | `FacturacionCodigos` | `https://pilotosiatservicios.impuestos.gob.bo/v2/FacturacionCodigos?wsdl` | `https://siatrest.impuestos.gob.bo/v2/FacturacionCodigos?wsdl` |
  | Sincronización | `FacturacionSincronizacion` | mismo patrón | mismo patrón |
  | Operaciones | `FacturacionOperaciones` | mismo patrón | mismo patrón |
  | Compra Venta (sector 1) | `ServicioFacturacionCompraVenta` | mismo patrón | mismo patrón |
  | Computarizada (otros sectores) | `ServicioFacturacionComputarizada` | mismo patrón | mismo patrón |
  | Documentos de Ajuste (notas) | `ServicioFacturacionDocumentoAjuste` | mismo patrón | mismo patrón |

  Namespace habitual `https://siat.impuestos.gob.bo/`; operaciones en lowerCamelCase (`cuis`, `cufd`, `recepcionFactura`, …). **Nada de esto debe quedar fijo en el código.**
- **Documentado:** QR de piloto `https://pilotosiat.impuestos.gob.bo/consulta/QR?nit=…&cuf=…&numero=…&t=…` (`t`: 1 rollo, 2 media hoja; por defecto 1). La ruta de producción "se la hará conocer al finalizar el proceso de autorización".
- **Recomendación:** generar los proxies con `dotnet-svcutil` desde el WSDL real de piloto y añadir la cabecera con un `IClientMessageInspector` o un `HttpMessageHandler`.

---

## 3. Contradicciones encontradas y cómo se resolvieron

| Id | Contradicción | Resolución | Corrección aplicada |
|---|---|---|---|
| **C-01** | 02 enruta el sector 1 al recurso **Compra Venta**; 03 y 08 describen `RecepcionFactura` y `RecepcionPaqueteFactura` con las páginas del recurso **Computarizada** sin distinguir. | Sector 1 → **Servicio Factura Compra Venta**; tipo 3 (24, 47, 48, 29) → **Documentos de Ajuste**; resto → Computarizada. Tabla de enrutamiento **configurable**; ante el error 932, alerta de configuración. | 03 §4.1; 08 M-INV-14 |
| **C-02** | 02 adopta `GZIP(TAR)` para paquetes; 03 dice que el contenedor es NO DOCUMENTADO; 07 documenta el TAR solo para compras. | **`GZIP(TAR(xml…))`**, por analogía literal con el Registro de Compras del mismo SIN. Nombres de archivo dentro del TAR (propuesta `<numeroFactura>.xml`) y base del "número de archivo" (0 o 1): NO DOCUMENTADOS; M-INV guarda la posición de cada factura. Confirmar con la Etapa VI. | 03 §7.2 |
| **C-03** | `emision-y-envio.md` y `sistema-informatico.md`: hash "del archivo compreso"; la tabla del servicio y F1: "Sha256 de la cadena Archivo". | Hash sobre los **bytes GZIP** (antes del Base64 de SOAP). Si el piloto devuelve **969**, probar sobre la cadena Base64 y documentar el resultado. | 06 §4 (nota); 07 §3.11 |
| **C-04** | 01 afirmaba que la numeración de eventos "coincide con la tabla oficial" (página: 7 = energía). El Excel de la Etapa V usa 5 = energía, 6 = virus/software, 7 = hardware; el Excel de paquetes usa un tercer orden; `versionamiento-2022` 1.0.25 "reordenó las causales 5, 6 y 7". | **Nunca fijar códigos.** Tomar el código del catálogo sincronizado "Eventos Significativos" y decidir la acción **por la descripción**: internet / inaccesibilidad / zonas sin internet / lugares sin internet → fuera de línea automático; energía / virus-software / hardware → CAFC (o Portal Web para virus y hardware). | 01 §5.6 |
| **C-05** | Masiva: 1000 facturas (`emision-y-envio.md`, página del servicio, Excel, F3) frente a 2000 (F1 Etapa IX). | 1000. M-INV **no** marca "Proceso Masivo" ni implementa masiva en V4.1. | — |
| **C-06** | Recepción individual no válida: `emision-y-envio.md` dice 904 (observada); la tabla de códigos tiene 902 (rechazada). | Tratar **902 y 904 igual**: la factura no es válida; se corrige y se emite de nuevo con número y CUF nuevos. | — |
| **C-07** | Timeout al emitir: 02 §14.11 proponía no re-emitir y mandar la original a un paquete; 03 y `ingreso-a-contingencia.md` dicen anular la original si quedó registrada. Además, la original lleva tipo de emisión 1 en su CUF y no puede ir en un paquete `codigoEmision = 2`. | **Flujo consolidado (literal de INGC):** timeout → la factura queda `SIN_RESPUESTA` → la venta se **re-emite fuera de línea** (nuevo número, tipo de emisión 2, CUF nuevo) y esa es la que recibe el cliente → al recuperar: CUFD nuevo → `verificacionEstadoFactura` de la original → si está registrada, **se anula** → evento → paquetes. Si no está registrada, se marca `DESCARTADA`. El salto de numeración puede dar la advertencia 2000 (no bloquea). | 02 §14.11 |
| **C-08** | Reversión: la página usa **907**, 981, 924, 3011 y 3012; la tabla de códigos tiene 907, **978** (también "confirmada"), 909, 968, y en ella el **981** es "rango de fechas de evento inválido"; 3011 y 3012 no figuran. | Éxito = **907 o 978**. En el contexto de reversión, 981 = "no disponible para reversión". Manejar 3011 (sistema no habilitado: terminar pruebas de reversión en piloto), 3012 (fuera de plazo), 968 (ya revertida) y 909. | — |
| **C-09** | `codigoTipoDocumentoIdentidad` en notas: página 1–9; XSD 1–5. | Manda el XSD (1–5) y el catálogo sincronizado; si el catálogo trae códigos mayores, ampliar la copia local del XSD (nota de versionamiento: "el contribuyente debe hacer esos ajustes"). | — |
| **C-10** | 04 proponía **bloquear la emisión** si no se sincronizó en el día; 02, 03 y 08 exigen seguir emitiendo **fuera de línea automáticamente** cuando el SIN no responde. | No bloquear fuera de línea. En línea: alerta y reintento, se emite con la última copia local. Bloqueo duro solo sin CUIS vigente, sin CUFD utilizable o con producto sin homologar. | 04 §7.5 |
| **C-11** | `codigoMoneda` de boliviano: la página de moneda extranjera dice "688"; el XSD limita a 1–154; los ejemplos usan 1. | Tomar el código del catálogo "Tipo Moneda"; con boliviano `tipoCambio = 1` y `montoTotalMoneda = montoTotal`. | — |
| **C-12** | `montoDescuentoCreditoDebito` (nota 24): "Obligatorio: Sí" en la página, `nillable` en el XSD, nil en el ejemplo. | Enviar el valor si hay descuento adicional prorrateado; `xsi:nil` (o 0) si no hay. Confirmar en piloto. | — |
| **C-13** | El XML oficial de la nota 24 devuelve "Tornillos", que no está entre las líneas de la transacción original; la regla dice que las líneas tx = 1 deben igualar la factura original (error 1049). | Manda la regla: la nota copia **todas** las líneas originales como tx = 1 y las devueltas como tx = 2. El ejemplo solo sirve como plantilla de estructura. | — |
| **C-14** | Orden de campos de la tabla de la página frente al XSD (sector 1 y conciliación). | Manda el **XSD** (lo que se valida). | — |
| **C-15** | La página del Módulo 11 da un orden de campos distinto e incompleto. | Manda la página del CUF; verificado con 6 vectores. | — |
| **C-16** | El código C# Base 16 publicado por el SIN produce un `0` inicial de más en .NET. | `ToString("X").TrimStart('0')`; decodificar con `"0" + hex`. | — |
| **C-17** | Redondeo: el texto dice "igual o inferior a 5" baja, una imagen dice "siempre hacia arriba"; los ejemplos (3.14159 → 3.14; 3.14559 → 3.15) muestran HALF-UP. | **HALF-UP** a 2 decimales en `decimal` (`MidpointRounding.AwayFromZero`), redondeando cada línea antes de sumar. Prohibido `double`. | — |
| **C-18** | Contingencia manual: `emision-y-envio.md` pone el registro del evento como "paso 0"; la página de contingencia pide CUFD nuevo **antes** del evento. | Orden único: **CUFD nuevo → registrar evento → enviar paquetes → validar**. | — |
| **C-19** | CUFD ampliado "hasta 72 horas": no se dice desde cuándo. | Contar desde la **obtención** del CUFD (interpretación conservadora) y reintentar cada ≤ 2 h. | 03 §14 ej. 4 |
| **C-20** | 07 decía que la verificación de estado devuelve "901–908"; 02 y 03 dicen NO DOCUMENTADO. | NO DOCUMENTADO: guardar el código tal como llega y confirmar en piloto los valores para válida, anulada y revertida. | 07 §3.11 |
| **C-21** | 02 decía que la planilla de reversión no estaba disponible; 08 la descargó (98 casos). | Disponible en `specs/_work08/CasosDePruebaReversionAnulacion.txt`. Casos de M-INV: sector 1 → 3 y 6; sector 24 → 67 y 70; sector 47 → 66 y 69. | 02 §1 y §15.19 |
| **C-22** | `manuales-contingencia.md` dice que el evento indica el CAFC; `registroEventoSignificativo` no tiene campo CAFC. | **1 evento por cada CAFC** en el modelo; el CAFC viaja en `RecepcionPaqueteFactura.cafc` y en el campo `<cafc>` de cada XML transcrito. | — |
| **C-23** | `codigoEvento` de `RecepcionPaqueteFactura` es "Numérico", pero recibe el `codigoRecepcion` del evento, que es "Alfanumérico". | Enviar el **código de recepción del evento** (no el código de catálogo) con el tipo que declare el WSDL. | — |
| **C-24** | `codigoPuntoVenta` en el XML: el ejemplo de factura usa `xsi:nil`, los de notas usan `0`. | Enviar el **valor numérico** (0 si no hay PV), coherente con la solicitud y el CUF. | 05 §2.1 |
| **C-25** | `verificarComunicacion`: 03 lo pedía en "cada servicio"; solo hay página para Computarizada, Documentos de Ajuste y Electrónica. | Implementarlo por recurso cuando el WSDL lo publique; si no, sondear con otra operación liviana. | 03 §17.6 |
| **C-26** | 05 y 06 marcaban como NO DOCUMENTADOS la huella, el algoritmo del CUF y las fórmulas de la nota, que sí están en otras páginas. | Referencias cruzadas añadidas. | 05 §1 y §14.12; 06 §4 y §14.8 |
| **C-27** | Máquinas de estado distintas en 02 §14.6 y 03 §16.4. | Canónica: **03 §16.4**, más el estado `RECHAZADA` (902 u 904 en línea) de 02. | — |
| **C-28** | CUFD masivo: la salida documentada no trae `codigoControl` ni `direccion`, imprescindibles para el CUF y el XML. | Usar `solicitudCufd` individual por (sucursal, PV). | — |
| **C-29** | Anulación desde otra sucursal: no se dice qué `codigoSucursal`, CUIS y CUFD enviar. | Los de la sucursal y PV **que anula** (INFERIDO); confirmar en piloto. | — |
| **C-30** | `montoTotalOriginal`: "monto sujeto a crédito fiscal en la factura original" (página) frente a `Σ subTotal (tx = 1)` (fórmula y ejemplo del sector 47: 850, no 800). | Usar la **fórmula**: `Σ subTotal` de las líneas tx = 1. | — |
| **C-31** | QR: se recomienda ≥ 3 × 3 cm, pero los PDF de ejemplo lo imprimen a 2.79 cm. | ≥ 3 × 3 cm (≥ 85 pt). | — |

---

## 4. Huecos que siguen abiertos

Todos requieren confirmación en el **ambiente piloto** (WSDL real y pruebas) o una consulta al SIN. Ninguno bloquea el diseño si se implementa como **configuración**.

**Técnicos del SOAP**
- H-01 URL/WSDL de cada recurso en piloto y producción, `targetNamespace`, versión de SOAP y mayúsculas exactas de las operaciones (la documentación usa `RecepcionFactura`, `AnulacionFactura`, `solicitudCufd`, `cuis`…). Vienen en el reporte de registro y en el inicio de operaciones.
- H-02 Estructura de `codigosRespuestas` / `mensajes` (nombres de campos, si distinguen advertencia de error, base del "número de archivo") y de `listaPuntosVentas`, `listaEventos` y de las listas de cada catálogo sincronizado.
- H-03 Tipo WSDL de `archivo` (Base64 / `byte[]`) y confirmación del hash sobre los bytes GZIP (C-03).
- H-04 Nombres de los archivos dentro del TAR y orden esperado.
- H-05 Nombres exactos de las 18 operaciones de sincronización (04 §3.2 usa los del enunciado: NO DOCUMENTADOS) y formato de la respuesta de "Fecha y Hora".
- H-06 `verificarNit` no lista `cuis` como entrada; `verificarComunicacion` en Compra Venta, Códigos, Operaciones y Sincronización.

**Fechas y horas**
- H-07 **Zona horaria** de `fechaEmision`, `fechaEnvio`, `fechaInicioEvento` y `fechaFinEvento`: solo se dice "UTC extendido sin zona horaria" (formato `yyyy-MM-dd'T'HH:mm:ss.SSS`). DECISIÓN M-INV: reloj corregido con la sincronización de Fecha y Hora del SIN, sin offset.
- H-08 Formato literal de `fechaVigencia` ("Fecha UTC extendida") y de los campos tipo Date.
- H-09 Si "hasta el día 9" incluye todo el día 9 (M-INV lo trata como inclusivo y acepta el 934 como definitivo); qué pasa al vencer los plazos de 48 h y 72 h.

**Catálogos y valores**
- H-10 Códigos de: motivos de anulación (solo se conocen las descripciones "FACTURA MAL EMITIDA" y "NOTA DE CREDITO-DEBITO MAL EMITIDA"), tipos de documento de identidad 2–5 (1 = CI documentado; 5 = NIT deducido), métodos de pago (1 efectivo, 2 tarjeta, 5 otros; **gift card y combinaciones NO DOCUMENTADOS**), monedas, unidades de medida (58 = unidad servicio), eventos significativos (C-04) y leyendas por actividad.
- H-11 `codigoEstado` que devuelven `verificacionEstadoFactura` y `verificacionEstadoDocumentoAjuste`.
- H-12 Texto exacto de la **tercera leyenda "fuera de línea"** (solo está documentado el texto "en línea"). Debe ser configurable.
- H-13 Si una factura con solo advertencias (2000–2019) queda válida (se asume que sí).

**Contingencia y CAFC**
- H-14 Rango de numeración, vigencia y forma de pedir el **CAFC** (la guía de facturas de contingencia no está en el corpus).
- H-15 Hora que se usa al transcribir facturas manuales que no la registran.
- H-16 Qué CUFD usar si la contingencia dura más que la vigencia del CUFD (fuera del caso de fallo de `solicitudCufd`); si se puede pedir un segundo CUFD el mismo día y qué pasa con el anterior.
- H-17 **Notas crédito-débito sin conexión**: no hay servicio de paquetes ni masivo para notas. DECISIÓN M-INV: no emitir notas fuera de línea; dejarlas en cola "pendiente de emisión" hasta que vuelva la comunicación.

**Notas crédito-débito**
- H-18 Si una factura con descuento adicional **debe** usar el sector 47 o puede usar el 24 con `montoDescuentoCreditoDebito`.
- H-19 Prorrateo del `montoDescuento` por ítem en devoluciones parciales; si el SIN controla el acumulado de varias notas sobre la misma factura; numeración de las notas (serie propia por sucursal y PV, DECISIÓN M-INV); notas por aumento de precio.

**Autorización**
- H-20 ¿Sistema **propio** o **proveedor**? M-INV es multiempresa: si emitirá para más de un NIT debe registrarse como proveedor, con asociación y Fase III por cliente.
- H-21 "Los sistemas Web y API se autorizan por separado" (1.0.47): cómo aplica a un cliente WPF con servicio en la nube. DECISIÓN M-INV: un solo componente de facturación, que es lo que se registra.
- H-22 Reparto de las 500 emisiones, 250 anulaciones y 98 reversiones; significado de "50 pruebas por caso" en la sincronización (se ve en "Seguimiento" del portal).
- H-23 Lista de características del formulario de registro (capturas no descargadas).
- H-24 Texto de las **RND** (102100000011, 102300000034 y otras): los `adjuntos/rnd_*.pdf` son la portada HTML del SIN, no los PDF.

**Otros**
- H-25 Ruta del QR en producción; formato "rollo" y medidas de "media hoja"; nivel de corrección de errores del QR.
- H-26 Monto literal cuando hay gift card (¿TOTAL o MONTO A PAGAR?).
- H-27 Regla "importado / nacional" de la homologación: el catálogo vigente no distingue; material eléctrico sin código específico en ferretería minorista.
- H-28 Registro de Compras: formato de la salida `Archivo` de `consultaCompras`, significado de `gestion` y `periodo`, plazos, y cómo anular la confirmación de una compra con CUF.
- H-29 Número exacto de reintentos ("un par de veces") y timeout HTTP antes de declarar fuera de línea (DECISIÓN M-INV: 2 reintentos, timeout configurable).

---

## 5. Decisiones consolidadas de implementación

1. **Arquitectura:** un único **servicio de facturación SIAT** (capa Infrastructure + worker/`IHostedService`) custodia token, CUIS, CUFD, colas y paquetes, y habla con el SIN. Las cajas WPF escriben la venta y el documento fiscal en una **bandeja fiscal** (patrón SAP B1 de 07 §6) y no llaman al SIN directamente. Despliegue centralizado (01 §6.3).
2. **Enrutador de recursos** configurable por `(modalidad, sector, tipoFacturaDocumento)` (C-01).
3. **Formato de envío:** individual `GZIP(XML UTF-8 sin BOM)`; paquete `GZIP(TAR)`; `hashArchivo = hex minúscula(SHA-256(bytes GZIP))` (C-02, C-03).
4. **Fechas:** una sola marca de tiempo por emisión, con milisegundos, del reloj corregido con el SIN; XML y solicitudes `yyyy-MM-dd'T'HH:mm:ss.fff` sin offset; CUF `yyyyMMddHHmmssfff` (H-07).
5. **Montos:** `decimal` + HALF-UP (`AwayFromZero`); sector 1 a 2 decimales en todo; notas 24/47 hasta 10 decimales en el detalle; serialización `InvariantCulture`; PostgreSQL `numeric`.
6. **Estados del documento fiscal** (C-27): `BORRADOR → EMITIENDO → VALIDADA (908) | OBSERVADA/RECHAZADA (904/902) | SIN_RESPUESTA → …`; `EMITIDA_OFFLINE`, `TRANSCRITA_CAFC → EN_PAQUETE → VALIDADA | RECHAZADA_EN_PAQUETE`; `ANULANDO → ANULADA | ANULACION_INCIERTA`; `REVIRTIENDO → VALIDADA(revertida=true)`; `DUPLICADA_A_ANULAR`; `DESCARTADA`. Invariante: `revertida = true` impide volver a anular.
7. **Eventos y paquetes:** eventos por descripción de catálogo (C-04); **CUFD nuevo → evento → paquetes → validación** (C-18); un evento por CAFC (C-22); `codigoEvento` = código de recepción del evento (C-23); paquetes por (sucursal, PV, evento, sector, tipo factura, CAFC), ≤ 500.
8. **Timeout al emitir:** flujo C-07.
9. **Notas crédito-débito:** solo en línea; cola si no hay conexión (H-17).
10. **Inventario (DECISIÓN M-INV, el SIN no lo regula):** la anulación **no** repone stock sola (se ofrece "anular y devolver mercadería" o "anular y reemitir"); la nota crédito-débito repone el stock de las líneas tx = 2 cuando llega a 908 (o queda "pendiente de nota").
11. **Seguridad:** token cifrado en reposo (DPAPI o columna cifrada), nunca en la bitácora; tarjeta guardada **solo enmascarada**; bitácora SOAP sin token.
12. **Sin valores fijos en el código:** URLs, nombres de operaciones, códigos de catálogo, textos de leyendas, URL base del QR y límites `maxInclusive` de los XSD son configuración o datos sincronizados.

---

## 6. Requisitos consolidados y priorizados para M-INV

**Criterios de prioridad**
- **P1, imprescindible para facturar legalmente:** sin esto no se emite un documento fiscal válido o se incumple la norma en la operación diaria (RND 102100000011 y Anexo Técnico, incluidas las funciones mínimas FM-a a FM-f).
- **P2, homologación:** necesario para aprobar la autorización (Fases I a III) e iniciar operaciones.
- **P3, mejoras:** servicios opcionales, reportes y comodidad.

La columna "Fuente" remite a las especificaciones (número § sección / ítem).

### P1 · Imprescindible para facturar legalmente

| Id | Requisito | Fuente |
|---|---|---|
| P1-01 | **Configuración SIAT por empresa**: NIT, razón social, `codigoSistema`, ambiente (1/2), modalidad fija 2, URLs/WSDL **por recurso y por ambiente** editables, token delegado **cifrado** por ambiente con su fecha "Hasta" y alerta de vencimiento. Pantalla solo para Administrador. | 01 §11.1–3 · 02 §14.1 · 08 M-INV-1 |
| P1-02 | **Cabecera `apikey: TokenApi <token>`** en todas las llamadas, centralizada en un inspector/handler; no llamar con token vacío o vencido; alerta ante 989. | 01 §2.3 · 02 §3.1 |
| P1-03 | **Servicio central de facturación** (bandeja fiscal + worker) que custodia CUIS/CUFD y habla con el SIN; es el único componente que se autoriza. | 01 §6.3 · 07 §7.27 · 08 M-INV-26 |
| P1-04 | **Clientes SOAP** generados desde el WSDL (`dotnet-svcutil`) detrás de interfaces: Códigos, Sincronización, Operaciones, Compra Venta, Documentos de Ajuste (Computarizada preparado); **enrutador por sector configurable** con alerta ante 932; timeouts configurables. | 02 §2, §14.3–4 · C-01 |
| P1-05 | **Mapeo sucursal M-INV ↔ `codigoSucursal` del Padrón** (0 = casa matriz; lo ingresa el usuario) con `direccion`, `municipio` y `telefono` del Padrón; la `direccion` se toma y actualiza de la respuesta del CUFD (errores 1007/2014). | 01 §6.1, §11.8–9 · 05 §13.21 |
| P1-06 | **Puntos de venta**: registro con `registroPuntoVenta` (tipo 5 = cajeros), nombre y descripción no vacíos, guardar el `codigoPuntoVenta` del SIN; consulta con `ConsultaPuntoVenta`; vincular cada caja o cajero a un par (sucursal, PV). | 01 §5.1, §5.3, §11.10–13 |
| P1-07 | **CUIS por (sucursal, PV)**: solicitud, vigencia 365 días, alerta desde 5 días antes y ante la advertencia 3008, manejo de 980/970; tras renovar, **CUFD nuevo inmediato**; sin CUIS vigente no se emite. | 01 §4.1, §11.15–18 · 08 M-INV-5 |
| P1-08 | **CUFD diario por (sucursal, PV)** con el CUIS propio del par: guardar `codigoCUFD`, **`codigoControl`**, **`direccion`**, `fechaVigencia`, fecha de obtención, en un **histórico inmutable**; secuencia diaria fecha/hora → catálogos → CUFD; si `solicitudCufd` falla, fuera de línea con el último CUFD (≤ 72 h, C-19) y reintento cada ≤ 2 h; **CUFD nuevo antes de registrar un evento y enviar paquetes**. Usar el CUFD individual, no el masivo (C-28). | 01 §4.3, §11.20–25 · 03 §6.5 · 08 M-INV-6 |
| P1-09 | **Sincronización diaria** de los **18 catálogos** y de **Fecha y Hora** (reloj SIN con diferencia guardada); tabla genérica + tablas de actividades, productos, leyendas y actividad-documento sector; no borrar lo retirado; validar siempre contra lo sincronizado y no contra los enums del XSD; sin bloqueo fuera de línea (C-10). | 04 §3, §7.3–6 · 08 M-INV-7/8 |
| P1-10 | **Homologación de productos**: por producto, actividad económica (7 caracteres, texto), `codigoProductoSin` y unidad de medida SIN; el XML lleva el código SIN y la representación gráfica el código interno; pantalla con búsqueda y asignación en lote; precarga con el CSV de ferretería; **no se factura un producto sin homologar**. | 04 §5–6, §7.8 · 01 §8.7 · 08 M-INV-9 |
| P1-11 | **Homologación de paramétricas**: métodos de pago (incluidos gift card y combinaciones), moneda, tipo de documento de identidad y unidad de medida, contra el catálogo sincronizado. | 04 §7.9 · 08 II-3 |
| P1-12 | **Generador XML sector 1** `facturaComputarizadaCompraVenta`: orden exacto del XSD, `xsi:nil` en todo campo opcional vacío, UTF-8 sin BOM, sin namespace, `InvariantCulture`, 2 decimales, ≤ 500 líneas (dividir la venta si hay más), `codigoPuntoVenta` numérico (C-24), `usuario` descriptivo, longitudes máximas. | 05 §1–2, §13.1–5 |
| P1-13 | **Validación local contra el XSD embebido** con `ReportValidationWarnings` y advertencias tratadas como error; no enviar si falla (evita 939); permitir ampliar `maxInclusive` locales. | 05 §10, §13.6–7 |
| P1-14 | **Motor de cálculo** en `decimal` HALF-UP: subtotales por línea redondeados antes de sumar, `montoTotal`, `montoTotalSujetoIva` (gift card), `montoTotalMoneda`, tipo de cambio; revalidación antes de enviar (1013, 1014, 1018, 1057, 1058). | 05 §3 · 06 §6 · C-17 |
| P1-15 | **CUF**: algoritmo del §2.4 con Módulo 11, Base 16 sin el `0` de .NET y `codigoControl` del CUFD; misma marca de tiempo que `fechaEmision`; pruebas unitarias con los 6 vectores. | 06 §1–3, §14.1–2 |
| P1-16 | **Numeración correlativa** de facturas (1…9 999 999 999) y de notas (serie propia), por (sucursal, PV) y documento (DECISIÓN M-INV); advertencia 2000 como aviso. | 05 §2.1 · 07 §3.10 |
| P1-17 | **Empaquetador**: individual GZIP; paquete GZIP(TAR) con orden guardado; SHA-256 hex minúscula; control de ≤ 500, mismo sector, ≤ 100 MB y `cantidadFacturas` exacta. | 02 §5 · C-02/C-03 |
| P1-18 | **Emisión individual en línea**: `RecepcionFactura` (Compra Venta) con `codigoEmision = 1`, `codigoModalidad = 2`, `tipoFacturaDocumento = 1`, `codigoDocumentoSector = 1`, `fechaEnvio` sin offset; 908 = válida; 902/904 = no válida (C-06); advertencias 2xxx se registran y no bloquean; guardar `codigoRecepcion` y todos los mensajes. | 02 §6.1 · 03 §4 |
| P1-19 | **Reglas de captura de la venta** (Fase II): número de documento **siempre** (nominatividad); CI y NIT solo numéricos; `complemento` solo con CI; `codigoExcepcion` 0 por defecto, 1 si es NIT y se pide no validarlo, **siempre 1 fuera de línea con NIT**; clientes especiales 99001/99002/99003 con tipo NIT y excepción 1; total 0 prohibido salvo gift card; pago mixto con el código de combinación; **tarjeta enmascarada** 4+ceros+4 (nunca el PAN completo); servicios con cantidad 1 y unidad 58; no usar actividades de importación ni el tipo de emisión "CONTINGENCIA". | 05 §3, §13.10–19 · 08 II-1…II-6 · 03 §17.29–30 |
| P1-20 | **`verificarNit`** para clientes con NIT (clientes habituales con anticipación, eventuales al emitir); guardar 986/987/994; si no se puede verificar, confirmar con excepción 1. | 01 §4.5, §11.26–27 |
| P1-21 | **Monitor de comunicación y fuera de línea automático**: `verificarComunicacion` (926) por recurso (C-25); tras 2 reintentos fallidos (timeout, -1, NPE, 4xx, 5xx) pasa solo a FUERA_DE_LINEA; emisión con tipo de emisión 2, CUFD vigente antes del corte, excepción 1 con NIT y tercera leyenda "fuera de línea"; reintento cada ≤ 2 h; **recuperación automática** sin intervención (CUFD nuevo → evento → paquetes → validación con backoff hasta 908/904 → aplicar mensajes por número de archivo → reenviar corregidas). | 02 §12.3, §14.7–9 · 03 §5–7, §16 · 08 M-INV-14, II-14 |
| P1-22 | **Eventos significativos**: entidad (sucursal, PV, código y descripción de catálogo, inicio y fin `yyyy-MM-dd'T'HH:mm:ss.SSS`, `cufdEvento`, `cufd` de envío, `codigoRecepcion`, estado, CAFC); registro con `registroEventoSignificativo` validando fin > inicio y plazo ≤ 48 h; consulta con `consultaEventoSignificativo`; códigos por descripción (C-04). | 01 §5.6–5.7 · 03 §6 · 08 M-INV-15 |
| P1-23 | **Facturas sin respuesta** (timeout al emitir): flujo C-07 con verificación por CUF y anulación del duplicado. | 03 §5 · 02 §14.11 |
| P1-24 | **Contingencia manual CAFC**: alta de CAFC por sucursal y sector (rango y vigencia los ingresa el usuario, H-14); botón "Declarar contingencia manual"; **pantalla de transcripción** (tipo de emisión 2, CUFD del evento, campo `cafc`, fecha dentro del evento, número dentro del talonario); 1 evento por CAFC; paquete con `cafc` dentro de **72 h**; errores 1045–1047, 1040/2001. | 03 §8, §17.15–17 · 08 M-INV-18, II-12 |
| P1-25 | **Alertas de plazos** con cuenta regresiva: evento sin registrar (48 h), paquetes sin enviar (48 h), transcripción CAFC (72 h), CUFD ampliado (72 h), CUIS (5 días), token, autorización (3 años). | 03 §17.14 · 08 M-INV-2, M-INV-16 |
| P1-26 | **Anulación**: motivo del catálogo; bloqueo después del **día 9 del mes siguiente** (hora SIN; regla configurable por sector); solo documentos 908 no anulados ni revertidos; desde otra sucursal (C-29); 905 y 936 = éxito; timeout → verificar el estado antes de reintentar, sin entrar en contingencia; **notificación obligatoria al comprador** (CUF, número, motivo) registrada; roles Administrador/Gerencia. | 02 §6.2, §12.6 · 03 §11, §17.19–21 · 08 II-10 |
| P1-27 | **Verificación de estado por CUF** (manual y automática en recuperación y conciliación); guardar el código tal como llegue (H-11). | 02 §6.8 · 03 §10 |
| P1-28 | **Representación gráfica** hoja Carta y rollo: título "FACTURA" / "(Con Derecho a Crédito Fiscal)", "CÓD. AUTORIZACIÓN" = CUF, código interno del producto, descripción de la unidad, totales (incluido "IMPORTE BASE CRÉDITO FISCAL"), literal "Son: … xx/100 Bolivianos", **tres leyendas** (1 fija; 2 Ley 453 **al azar por emisión** del catálogo de la actividad y guardada; 3 "en línea"/"fuera de línea" configurable) y **QR ≥ 3 × 3 cm** con `nit`, `cuf`, `numero`, `t` y base configurable por ambiente; reimpresión idéntica. | 06 §7, §10–14 · 08 II-4/II-5 |
| P1-29 | **Entrega al comprador** (art. 26): correo con **XML + PDF** o medio privado; si no se puede enviar, imprimir y enviar luego; registro de envío. | 03 §13 · 08 II-13 · 01 §8.6 |
| P1-30 | **Nota crédito-débito sector 24** (devoluciones): entidad y líneas (tx 1 = todas las originales, tx 2 = devueltas); pantalla de devolución desde la factura de M-INV o **transcripción manual** de una factura de otra modalidad; fórmulas `montoTotalOriginal = Σ tx1`, `montoTotalDevuelto = Σ tx2 − montoDescuentoCreditoDebito`, `efectivo = devuelto × 0.13`; validaciones (18 meses, factura no anulada, acumulado devuelto ≤ vendido, 1033, no al mismo emisor 1005); XML `notaFiscalComputarizadaCreditoDebito` (2–500 detalles, `codigoDetalleTransaccion`); CUF tipo 3 sector 24; `recepcionDocumentoAjuste` **solo en línea** (cola si no hay conexión); anulación y verificación por Documentos de Ajuste; representación gráfica "NOTA CRÉDITO - DÉBITO"; reingreso de stock al validarse. | 05 §4 · 07 §3, §7.A–B · 02 §8 · C-12/C-13/C-30 |
| P1-31 | **Persistencia fiscal completa**: XML exacto enviado, GZIP, hash, CUF, CUFD usado (FK al histórico), tipo de emisión, excepción, CAFC, evento, paquete y posición, `codigoRecepcion`, estado y **todos** los mensajes; anulación y reversión con usuario y fecha; leyenda elegida. | 02 §14.18 · 03 §17.28 · 05 §13.20–25 · 06 §14.5 |
| P1-32 | **Bitácora SOAP** inmutable (operación, recurso, ambiente, sucursal, PV, request y response **sin token**, duración, HTTP, estado) y **catálogo local de códigos** (193 semilla + "Mensajes Servicios") con acciones: 3008 renovar CUIS; 953/914/123 CUFD nuevo; 989 alerta de token; 995/967/991/999 candidatos a fuera de línea; 2xxx advertencia. | 01 §11.6–7 · 04 §4, §7.12–13 · 03 §17.27 |
| P1-33 | **Tablero "Estado SIAT"** por (sucursal, PV): modo, CUIS, CUFD, token, última comunicación, eventos, paquetes y plazos. | 01 §11.37 · 03 §17.25 |
| P1-34 | **Separación por ambiente**: CUIS, CUFD, PV, eventos, tokens y URLs guardados por ambiente; nada de piloto se reutiliza en producción. | 01 §11.39 · 08 §6 |
| P1-35 | **Permisos por rol** sobre los roles de M-INV: Cajero y Ventas emiten; Administrador y Gerencia anulan, revierten, emiten notas y configuran; Consulta solo ve; todo en `AuditLog`. | 02 §14.24 · 07 §7.12 |

### P2 · Homologación (autorización y puesta en producción)

| Id | Requisito | Fuente |
|---|---|---|
| P2-01 | **Datos de registro del sistema**: nombre comercial y versión mostrados igual que lo registrado (pantalla "Acerca de / Datos SIAT"); tipo propio o proveedor (H-20); sin "Proceso Masivo"; sectores 1 y 24 (47 opcional); características declaradas iguales a lo que se usa. | 08 §1.3, §5.3, M-INV-3 |
| P2-02 | **Preparación del piloto**: PV **1** registrado en la casa matriz además del PV 0 (orden: CUIS (0,0) → `registroPuntoVenta` → CUIS (0,1) → CUFD de cada par); CAFC de prueba por sector y sucursal. | 01 §10.3 · 08 §10.6 |
| P2-03 | **Asistente "Pruebas de autorización (Fase I)"** solo en ambiente 2, con contadores por etapa y reporte exportable: CUIS 2 por caso; sincronización 36 casos × 50; CUFD 2 casos × 100; **500 emisiones** individuales (sector 1: casos 1–2; sector 24: casos 47–48); eventos 14 casos × 5; paquetes sector 1: 16 casos × 10 (7 eventos × "igual a 500" con PV 1 y "menor a 500" con PV 0 + 2 validaciones); **250 anulaciones** (casos 1–2 y 47–48); **98 reversiones** (casos 3/6, 67/70, 66/69). | 02 §13 · 08 §2 · 04 §3.5 · 01 §10.3 |
| P2-04 | **Reversión de la anulación** (Etapa XI, obligatoria para los sistemas en autorización): `ReversionAnulacionFactura` y `reversionAnulacionDocumentoAjuste`, una sola vez, hasta el día 9, `revertida = true` bloquea nuevas anulaciones, éxito 907/978, manejo de 3011/3012/981/968/909, notificación al comprador. | 02 §6.3 · 03 §12 · C-08 |
| P2-05 | **Guion y evidencia de inspección (Fase II)**: demostración de II-1 a II-14 y FM-a a FM-f, con la bitácora como evidencia; marca de agua "SIN VALOR LEGAL" en ambiente 2. | 08 §3, M-INV-25 · 06 §14.16 |
| P2-06 | **Fase III y paso a producción**: checklist de pruebas a–h; captura de las URL de producción del inicio de operaciones; token de producción nuevo; CUIS, CUFD, sincronización y homologación en producción; control del plazo 90 + 60 días. | 08 §4, §6, §1.7, §10 |
| P2-07 | **Renovación de la autorización** a los 3 años (alertas; 3 oportunidades de inspección). | 08 §7.1 |

### P3 · Mejoras

| Id | Requisito | Fuente |
|---|---|---|
| P3-01 | **Registro de Compras (RCV)**: `consultaCompras`, `confirmacionCompras`, `recepcionPaqueteCompras` + validación y `AnulacionCompra` (paquetes TAR+GZIP ≤ 500); ampliación de `SupplierInvoice` con los datos fiscales; habilitación del servicio en el portal. | 07 §5.6, §7.C |
| P3-02 | **Libros** de ventas (24 columnas), compras (23) y reintegros exportables en el formato de las plantillas; **resumen fiscal mensual** para el contador (el mapeo a Form. 200/400 es NO DOCUMENTADO). | 07 §5.2–5.4, §7.D |
| P3-03 | **Sectores opcionales**: 47 (nota con descuento adicional: `nroItem`, `descuentoItem`), 35 (bonificaciones) y 41 (tasas), si el NIT los tiene habilitados. | 05 §5–6 · 07 §3.6 · 04 §7.11 |
| P3-04 | **Portal o endpoint público** con comprobante de transacción (enlace o QR ≥ 3 × 3 cm con token opaco, sin redirecciones) para descargar XML y PDF. | 03 §13, §17.24 · 08 1.0.53 |
| P3-05 | **Recepción de anexos** de serie/IMEI (`tipoCodigo` 1/2). | 02 §6.10 |
| P3-06 | **Emisión masiva** (interfaz preparada, desactivada). | 02 §6.6 · 03 §9 |
| P3-07 | **Ventas facturadas externamente** (Portal Web como contingencia, Tasa Cero Ley 1613 sector 33): registro de número, CUF, fecha y monto para descargar stock sin emitir. | 07 §4.1, §7.25–26 |
| P3-08 | **Operaciones administrativas**: CUIS/CUFD masivos, `registroPuntoVentaComisionista`, `CierrePuntoVenta` y `cierreOperacionesSistema` con doble confirmación. | 01 §4.2, §4.4, §5.2, §5.4–5.5 |
| P3-09 | **Utilidades**: "Validar CUF" (decodificador), consulta por CUF con enlace "Verificar en SIAT", conciliación diaria automática, registro de reimpresiones, monto literal para otras monedas. | 06 §14.3, §14.18–19 · 02 §14.15 |
| P3-10 | **Salida por donación** que alimente el registro de reintegros. | 07 §5.4, §7.15 |
| P3-11 | **Modo proveedor multi-NIT** (asociación por empresa, token delegado por cliente, Fase III por cliente), si M-INV se ofrece a terceros. | 08 M-INV-28 · H-20 |

---

## 7. Qué se corrigió en cada especificación (resumen)

| Spec | Correcciones **[corregido por revisión]** |
|---|---|
| 01 | §5.6: la numeración de eventos de la página **no** coincide con los códigos de los Excel (C-04). |
| 02 | §1 y §15.19: planilla de reversión disponible (C-21). §14.11: flujo de timeout alineado con INGC (C-07). |
| 03 | §4.1: sector 1 al recurso Compra Venta (C-01). §7.2: contenedor TAR (C-02). §14 ej. 4: origen de las 72 h (C-19). §17.6: `verificarComunicacion` por recurso (C-25). |
| 04 | §7.5: sin bloqueo de emisión fuera de línea (C-10). |
| 05 | §1: huella = SHA-256 del GZIP. §2.1: `codigoPuntoVenta` numérico (C-24). §14.12: CUF y huella sí documentados (C-26). |
| 06 | §1.5: la zona horaria es recomendación, no dato. §4: bytes que se hashean. §14.8: fórmulas de la nota documentadas (C-26). |
| 07 | §1: el SHA-256 no es una firma. §3.11: `archivo` = GZIP del XML y hash sobre GZIP; estados de verificación NO DOCUMENTADOS (C-20). |
| 08 | M-INV-14: recurso Compra Venta, `codigoEvento` = código de recepción, formato del paquete (C-01, C-23). |

Las demás afirmaciones revisadas de las ocho especificaciones (parámetros de CUIS, CUFD, eventos y servicios de envío, tablas XSD de los sectores 1 y 24, CUF, plazos y token) **coinciden con la fuente**.
