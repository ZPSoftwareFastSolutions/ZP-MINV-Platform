# 02 — Servicios SOAP de envío de documentos fiscales (modalidad Computarizada en Línea)

Especificación de implementación para **M-INV** (.NET 8, Clean Architecture, EF Core + PostgreSQL, WPF).
Modalidad objetivo: **Facturación Computarizada en Línea** (`codigoModalidad = 2`, sin firma digital).
Alcance de este bloque: recursos **Servicio Factura Compra Venta**, **Facturación Computarizada en Línea** y **Documentos de Ajuste** (notas crédito-débito), comparados con dos páginas de **Facturación Electrónica**, más los casos de prueba de emisión individual, por paquetes, masiva y de anulación.

---

## 0. Convenciones y fuentes

**Marcas usadas en el documento**

- **NO DOCUMENTADO**: el dato no aparece en ningún archivo del corpus descargado.
- **⚠ ELECTRÓNICA**: aplica solo a la modalidad Electrónica en Línea (`codigoModalidad = 1`). M-INV no lo necesita ahora.
- **INFERIDO**: se deduce combinando varias fuentes, pero ninguna lo dice con una frase explícita.
- **FUERA DE DOC (verificar)**: es conocimiento externo al corpus. Se incluye solo como pista y debe confirmarse contra el WSDL en el ambiente piloto antes de usarse.

**Prefijo de archivos.** Todos los archivos citados están en `scratchpad/siat/txt/`. Se usa la abreviatura `P` para el prefijo común:
`P = facturacion-en-linea__implementacion-servicios-facturacion__`

### 0.1 Archivos del bloque (leídos completos)

| ID | Archivo |
|---|---|
| CV-REC | `P` + `servicio-factura-compra-venta__recepcion-factura-compra-venta.md` |
| CV-ANU | `P` + `servicio-factura-compra-venta__anulacion-factura-compra-venta.md` |
| CV-REV | `P` + `servicio-factura-compra-venta__reversion-anulacion-factura-compra-venta.md` |
| CV-PAQ | `P` + `servicio-factura-compra-venta__recepcion-paquete-compra-venta.md` |
| CV-VPAQ | `P` + `servicio-factura-compra-venta__validacion-recepcion-paquete-compra-venta.md` |
| CV-MAS | `P` + `servicio-factura-compra-venta__recepcion-masiva-compra-venta.md` |
| CV-VMAS | `P` + `servicio-factura-compra-venta__validacion-recepcion-masiva-factura-compra-venta.md` |
| CV-EST | `P` + `servicio-factura-compra-venta__verificacion-estado-factura-compra-venta.md` |
| CV-ANX | `P` + `servicio-factura-compra-venta__recepcion-archivos-anexos.md` |
| FC-REC | `P` + `facturacion-computarizada__recepcion-factura-computarizada.md` |
| FC-ANU | `P` + `facturacion-computarizada__anulacion-factura-computarizada.md` |
| FC-REV | `P` + `facturacion-computarizada__reversion-anulacion-factura-computarizada.md` |
| FC-PAQ | `P` + `facturacion-computarizada__recepcion-paquete-factura-computarizada.md` |
| FC-VPAQ | `P` + `facturacion-computarizada__validacion-recepcion-paquete-factura-computarizada.md` |
| FC-MAS | `P` + `facturacion-computarizada__recepcion-masiva-computarizada.md` |
| FC-VMAS | `P` + `facturacion-computarizada__validacion-recepcion-masiva-factura-computarizada.md` |
| FC-EST | `P` + `facturacion-computarizada__verificacion-estado-factura-computarizada.md` |
| FC-COM | `P` + `facturacion-computarizada__verifica-comunicacion-fact-comp.md` |
| FC-ELE | `P` + `facturacion-computarizada__recepcion-anexo-electrolineras.md` |
| DA-REC | `P` + `nota-credito-debito-comp__recepcion-nota-credito-debito-computarizada.md` |
| DA-ANU | `P` + `nota-credito-debito-comp__anulacion-nota-credito-debito.md` |
| DA-REV | `P` + `nota-credito-debito-comp__reversion-anulacion-documento-ajuste.md` |
| DA-EST | `P` + `nota-credito-debito-comp__verifica-estado-nota-fiscal-credito-debito-computarizada.md` |
| DA-COM | `P` + `nota-credito-debito-comp__verifica-comunicacion.md` |
| FE-REC | `P` + `facturacion-electronica__recepcion-factura-electronica.md` (comparación) |
| FE-ANU | `P` + `facturacion-electronica__anulacion-factura-electronica.md` (comparación) |
| CP-IND | `adjuntos/CasosDePruebaEmisionIndividual1.xlsx` (hoja "EMISIÓN INDIVIDUAL") |
| CP-PAQ | `adjuntos/CasosDePruebaEmisionPorPaquetes.xlsx` (hoja "EMISIÓN POR PAQUETES") |
| CP-MAS | `adjuntos/CasosDePruebaEmisionMasiva.xlsx` (hoja "EMISIÓN MASIVA") |
| CP-ANU | `adjuntos/CasosDePruebaAnulacionReversion.xlsx` (hoja "ANULACIÓN y REVERSIÓN") |

### 0.2 Archivos de contexto fuera del bloque

Estos archivos se consultaron solo para responder a las preguntas del enfoque: formato del archivo, hash, códigos de estado, plazos y homologación.

| ID | Archivo |
|---|---|
| EYE | `facturacion-en-linea__emision-y-envio-de-facturas__emision-y-envio.md` |
| GZIP | `facturacion-en-linea__algoritmos-utilizados__comprimir-gzip.md` |
| SHA | `facturacion-en-linea__algoritmos-utilizados__generacion-de-sha-256-md5-y-crc32.md` |
| ERR | `P` + `codigos-error-siat.md` |
| ANU | `facturacion-en-linea__emision-y-envio-de-facturas__anulacion-de-documentos-fiscales.md` |
| REV | `facturacion-en-linea__emision-y-envio-de-facturas__reversion-anulacion-documentos-fiscales.md` |
| CONT | `facturacion-en-linea__emision-y-envio-de-facturas__contingencia-y-eventos-significativos.md` |
| INGC | `facturacion-en-linea__emision-y-envio-de-facturas__ingreso-a-contingencia.md` |
| MANC | `facturacion-en-linea__casos-especiales__manuales-contingencia.md` |
| TOK | `facturacion-en-linea__emision-y-envio-de-facturas__solicitud-token.md` |
| TIPOS | `informacion__tipos-facturas.md` |
| MODC | `informacion__modalidades-facturacion__facturacion-computarizada.md` |
| CODA | `informacion__codigos-de-autorizacion.md` |
| F1 / F2 / F3 | `facturacion-en-linea__autorizacion-de-sistemas__pruebas-para-la-autorizacion-del-sistema-de-facturacion__fase-i-pruebas.md` / `…__fase-ii-inspeccion.md` / `…__fase-iii-pruebas-piloto.md` |
| RCP | `registro-de-compras-y-ventas__registro-de-compras-serv__recepcion-paquete-compras.md` |
| VER21 / VER23 | `versionamiento__versionamiento-2021.md` (menú del sitio) / `versionamiento__versionamiento-2023.md` |
| FEL | `facturacion-en-linea__factura-electronica.md` |
| XML-CV | `adjuntos/xml/CompraVentaXML/facturaComputarizadaCompraVenta.xml` |
| XML-NCD | `adjuntos/xml/CreditoDebitoXML/notaComputarizadaCreditoDebito.xml` |

**Diagramas.** Todas las páginas de servicios hacen referencia a un diagrama de proceso, por ejemplo `[imagen: /Anulación%20Factura%20Computarizada_archivos/recepcionAnulacion.jpg]`. Esas imágenes **no están descargadas** y no se pudieron leer. Las secuencias de la §12 se armaron con el texto de EYE, CONT, INGC y MANC.

---

## 1. Respuestas directas al enfoque

1. **La factura computarizada de Compra y Venta (`codigoDocumentoSector = 1`) se envía al recurso "Servicio Factura Compra Venta"**, no al recurso "Facturación Computarizada en Línea" (**INFERIDO con alta confianza**). La evidencia es la siguiente:
   - Todas las páginas repiten que los servicios "se hallan publicados de forma diferenciada por tipo de documentos sector" (CV-REC, FC-REC, CV-ANU, etc.).
   - El sitio tiene un grupo propio "Servicio Factura Compra Venta" (VER21) cuyas páginas aceptan `codigoModalidad` "Uno (1) Electrónica y dos (2) Computarizada en línea" (CV-REC, CV-ANU, CV-PAQ, CV-MAS).
   - El catálogo de errores tiene el código **932: "El Parámetro Código Documento Sector No Corresponde Al Servicio"** (ERR).
   - Ninguna página dice literalmente "el sector 1 va al servicio X". Por eso M-INV debe tener la regla configurable y confirmarla en piloto.
2. **Las Notas de Crédito-Débito (sector 24) y las Notas de Conciliación (sector 29) usan el recurso "Documentos de Ajuste"** (grupo `nota-credito-debito-comp`). Sus métodos son `recepcionDocumentoAjuste`, `anulacionDocumentoAjuste`, `verificacionEstadoDocumentoAjuste`, `reversionAnulacionDocumentoAjuste` y `verificarComunicacion` (DA-*). Ese recurso **no documenta métodos de paquete ni masivos**.
3. **Los demás sectores computarizados** (2–23, 28, 31, 33, …) usan el recurso **"Facturación Computarizada en Línea"** (FC-*).
4. **No están documentados** los nombres de WSDL, las URL, los namespaces SOAP ni las longitudes de los parámetros. En la §2.4 hay una pista FUERA DE DOC.
5. **Cómo se arma `archivo`**:
   - Factura individual: `GZIP(bytes UTF-8 del XML)`.
   - Paquete: `GZIP(TAR(n archivos XML))`. El TAR solo está documentado de forma explícita para el servicio de Registro de Compras (RCP). Para facturas, EYE dice "Formar paquetes de hasta 500 Facturas. Comprimir con Gzip".
   - **`hashArchivo` = SHA-256 de los bytes comprimidos** (EYE: "del archivo compreso"), en **hexadecimal minúscula** de 64 caracteres (SHA: `printHexBinary(...).toLowerCase()`).
   - El paso a Base64 lo hace la capa SOAP (**NO DOCUMENTADO**: la documentación declara el tipo como "Alfanumérico").
6. **Estados principales** (ERR y EYE):

   | Código | Significado |
   |---|---|
   | 901 | Recepción pendiente (respuesta al recibir un paquete o un envío masivo) |
   | 902 | Recepción rechazada |
   | 904 | Recepción observada |
   | 908 | Recepción validada |
   | 905 / 906 | Anulación confirmada / rechazada |
   | 907 / 909 | Reversión de anulación confirmada / rechazada |
   | 926 | Comunicación exitosa |

7. **Homologación** (§13), para los sectores que usará M-INV (1 y, si emite notas, 24):
   - Emisión individual: 2 casos por sector (con y sin punto de venta). La Fase I exige 500 emisiones individuales en total.
   - Paquetes: 16 casos por sector (7 eventos × 2 + 2 validaciones), con 10 pruebas por caso.
   - Masiva: 8 casos por sector, con 10 pruebas por caso.
   - Anulación: 2 casos por sector. La Fase I exige 250 anulaciones.
   - Reversión: 98 reversiones. Su planilla no está disponible localmente. **[corregido por revisión]** La planilla sí se obtuvo durante la especificación 08: `specs/_work08/CasosDePruebaReversionAnulacion.xls` y su volcado `.txt` (98 casos = 49 sectores × PV 0/1, resultado esperado 907). Casos de M-INV: sector 1 → casos 3 y 6; sector 24 → 67 y 70; sector 47 → 66 y 69 (ver `08-…` §2.3).

---

## 2. Recursos (servicios) y regla de enrutamiento

### 2.1 Recursos documentados y sus operaciones

Los nombres se escriben tal como los muestra la documentación. Las mayúsculas y minúsculas son inconsistentes; ver §15.

| Recurso (menú, VER21) | Operación ("Nombre Método") | Objeto de solicitud | Fuente |
|---|---|---|---|
| **Servicio Factura Compra Venta** | `RecepcionFactura` | `SolicitudServicioRecepcionFactura` | CV-REC |
| | `AnulacionFactura` | `SolicitudServicioAnulacionFactura` | CV-ANU |
| | `ReversionAnulacionFactura` | `SolicitudServicioReversionAnulacionFactura` | CV-REV |
| | `RecepcionPaqueteFactura` | `SolicitudServicioRecepcionPaquete` | CV-PAQ |
| | `validacionRecepcionPaqueteFactura` | `SolicitudServicioValidacionRecepcionPaquete` | CV-VPAQ |
| | `recepcionMasivaFactura` | `SolicitudServicioRecepcionMasiva` | CV-MAS |
| | `validacionRecepcionMasivaFactura` | `SolicitudServicioValidacionRecepcionMasiva` | CV-VMAS |
| | `verificacionEstadoFactura` | `SolicitudServicioVerificaEstadoFactura` | CV-EST |
| | "Recepción Anexos" (la página muestra por error "Nombre Método: RecepcionFactura") | `SolicitudRecepcionAnexos` | CV-ANX |
| | (sin página de Verifica Comunicación en este grupo; ver §15) | — | VER21 |
| **Facturación Computarizada en Línea** | `RecepcionFactura` | `SolicitudServicioRecepcionFactura` | FC-REC |
| | `AnulacionFactura` | `SolicitudServicioAnulacionFactura` | FC-ANU |
| | `ReversionAnulacionFactura` | `SolicitudServicioReversionAnulacionFactura` | FC-REV |
| | `RecepcionPaqueteFactura` | `SolicitudServicioRecepcionPaquete` | FC-PAQ |
| | `validacionRecepcionPaqueteFactura` | `SolicitudServicioValidacionRecepcionPaquete` | FC-VPAQ |
| | `recepcionMasivaFactura` | `SolicitudServicioRecepcionMasiva` | FC-MAS |
| | `validacionRecepcionMasivaFactura` | `SolicitudServicioValidacionRecepcionMasiva` | FC-VMAS |
| | `verificacionEstadoFactura` | `SolicitudServicioVerificaEstadoFactura` | FC-EST |
| | `verificarComunicacion` | (sin parámetros) | FC-COM |
| | `recepcionAnexosSuministroEnergia` | `SolicitudRecepcionSuministroAnexos` | FC-ELE |
| **Documentos de Ajuste** | `recepcionDocumentoAjuste` | `SolicitudServicioRecepcionDocumentoAjuste` | DA-REC |
| | `anulacionDocumentoAjuste` | `SolicitudServicioAnulacionDocumentoAjuste` | DA-ANU |
| | `verificacionEstadoDocumentoAjuste` | `SolicitudServicioVerificacionEstadoDocumentoAjuste` | DA-EST |
| | `reversionAnulacionDocumentoAjuste` | `SolicitudServicioReversionAnulacionDocumentoAjuste` | DA-REV |
| | `verificarComunicacion` | (sin parámetros) | DA-COM |
| ⚠ ELECTRÓNICA: **Facturación Electrónica en Línea** | `RecepcionFactura`, `AnulacionFactura`, … | los mismos objetos | FE-REC, FE-ANU |

Según EYE, `verificarComunicacion` "existe en cada recurso disponible, por lo que su implementación debe hacerse por recurso".

### 2.2 Regla de enrutamiento por documento sector (para modalidad 2)

| Documento sector (TIPOS) | tipoFacturaDocumento | Recurso a usar | Base |
|---|---|---|---|
| **1 Factura de Compra y Venta** | 1 (con derecho a crédito fiscal) | **Servicio Factura Compra Venta** | INFERIDO (§1.1) |
| 24 Nota de Crédito-Débito | 3 (documento de ajuste) | **Documentos de Ajuste** | DA-REC: "Documentos de Ajuste que incluyen las Notas Crédito - Débito y las Notas de Conciliación" |
| 29 Nota de Conciliación | 3 | **Documentos de Ajuste** | DA-REC |
| 47 NCD Descuentos, 48 NCD ICE | 3 (TIPOS: "Documento de Ajuste") | Documentos de Ajuste | INFERIDO: son variantes de NCD |
| Resto de sectores habilitados en computarizada | 1 o 2 según TIPOS | **Facturación Computarizada en Línea** | FC-* e INFERIDO |
| ⚠ ELECTRÓNICA: sector 1 en modalidad 1 | 1 | Servicio Factura Compra Venta (sus páginas admiten modalidad 1) | CV-REC |
| ⚠ ELECTRÓNICA: otros sectores en modalidad 1 | — | Facturación Electrónica en Línea | FE-REC |

**Implicación para M-INV.** M-INV vende materiales de construcción, así que en la práctica usará:

- El sector 1 con el recurso **Compra Venta**.
- Opcionalmente el sector 24 (devoluciones) con el recurso **Documentos de Ajuste**.

### 2.3 Valores de `tipoFacturaDocumento`

Los valores salen de TIPOS (columna "Tipo Factura/Documento") comparados con los casos de prueba (CP-IND, CP-ANU):

| Documento | tipoFacturaDocumento |
|---|---|
| Sector 1 (con derecho a crédito fiscal) | **1** |
| Sector 3 (sin derecho a crédito fiscal) | **2** |
| Sectores 24 y 29 (documento de ajuste) | **3** |

- El catálogo oficial se obtiene por sincronización ("tipos de factura", F1 Etapa II). Esta relación de valores es **INFERIDA** de los casos de prueba.
- El tipo "Documento Equivalente" (sector 30, boleto aéreo) no aparece en los casos de prueba: su código es **NO DOCUMENTADO** aquí.

### 2.4 Nombres técnicos del WSDL — NO DOCUMENTADO

El corpus no contiene URL de WSDL, nombres de servicio SOAP ni namespaces. EYE solo dice que "el servicio web correspondiente".

**FUERA DE DOC (verificar).** Estos son los nombres que usan habitualmente las integraciones con el SIAT. Se anotan solo para orientar la búsqueda en piloto y **no deben quedar fijos en el código**:

- Recursos:
  - `ServicioFacturacionCompraVenta` (sector 1)
  - `ServicioFacturacionComputarizada` (otros sectores computarizados)
  - `ServicioFacturacionDocumentoAjuste` (notas)
  - `ServicioFacturacionElectronica` (⚠ ELECTRÓNICA)
- Base de URL en piloto: `https://pilotosiatservicios.impuestos.gob.bo/v2/<Servicio>?wsdl`.
- Base de URL en producción: `https://siatrest.impuestos.gob.bo/v2/<Servicio>?wsdl`.
- Namespace: `https://siat.impuestos.gob.bo/`.
- En el WSDL las operaciones suelen estar en lowerCamelCase: `recepcionFactura`, `anulacionFactura`, `reversionAnulacionFactura`, `recepcionPaqueteFactura`, `validacionRecepcionPaqueteFactura`, `recepcionMasivaFactura`, `validacionRecepcionMasivaFactura`, `verificacionEstadoFactura`, `verificarComunicacion`, `recepcionAnexos`.
- La respuesta suele llamarse `RespuestaServicioFacturacion` y su lista de mensajes `mensajesList`.

**Recomendación.** Generar los clientes con `dotnet-svcutil` a partir del WSDL real de piloto y guardar las URL en la configuración, separadas por ambiente y por recurso.

---

## 3. Autenticación y parámetros comunes

### 3.1 Token

Fuente: TOK y FEL.

- Se usa el **Token Delegado** que se obtiene en el Portal SIAT. Hay un token para producción y otro para piloto: "Token Delegado en Producción" / "Token Delegado Piloto".
- Va en el **header HTTP** de cada solicitud:
  `apikey: TokenApi <token>` (Java: `headers.put("apikey", Arrays.asList("TokenApi " + pToken));`)
- Cuando caduca o está por caducar, se inactiva en el portal y se genera uno nuevo. La duración se elige al generarlo; valores concretos **NO DOCUMENTADOS** en este bloque.
- Error relacionado: **989 "Token Invalido"** (ERR).

### 3.2 Parámetros presentes en todas las operaciones del bloque

La documentación solo indica tipos genéricos: Numérico, Alfanumérico, TimeStamp, Boolean. **Longitudes y rangos: NO DOCUMENTADOS.**

| Parámetro | Tipo | Oblig. | Descripción (doc) | Valor en M-INV |
|---|---|---|---|---|
| `codigoAmbiente` | Numérico | Sí | Producción: 1; Pruebas y Piloto: 2 | Configuración por empresa |
| `codigoPuntoVenta` | Numérico | **No** | "Solo se envía cuando la transacción se realiza utilizando un punto de venta. Caso contrario enviar 0." | Código SIN de la caja; 0 si no hay |
| `codigoSistema` | Alfanumérico | Sí | Código de sistema asignado al solicitar la autorización | Configuración |
| `codigoSucursal` | Numérico | Sí | Casa Matriz: 0; Sucursal: 1, 2, …, n | Mapeo sucursal M-INV → código SIN |
| `nit` | Numérico | Sí | NIT del emisor | NIT de la empresa |
| `codigoDocumentoSector` | Numérico | Sí | Sector de la factura | 1 (factura) / 24 (NCD) |
| `codigoEmision` | Numérico | Sí | Depende de la operación (§3.3) | — |
| `codigoModalidad` | Numérico | Sí | "Uno (1) Electrónica y dos (2) Computarizada en línea" | **2** |
| `cufd` | Alfanumérico | Sí* | "Valor diario otorgado por el SIN" | CUFD vigente de la sucursal/punto de venta **que envía** |
| `cuis` | Alfanumérico | Sí | "Valor único para una sucursal y/o punto de venta …" | CUIS vigente de la sucursal/punto de venta |
| `tipoFacturaDocumento` | Numérico | Sí | Tipo de factura o documento de ajuste | 1 (sector 1) / 3 (sector 24) |

\* En CV-ANX y FC-ELE la columna "Obligatorio" de `cufd` está vacía.

### 3.3 `codigoEmision` por operación

| Operación | codigoEmision permitido | Fuente |
|---|---|---|
| RecepcionFactura, recepcionDocumentoAjuste | **1 (Online)** | CV-REC, FC-REC, DA-REC |
| RecepcionPaqueteFactura, validacionRecepcionPaqueteFactura | **2 (Offline)** | CV-PAQ, CV-VPAQ |
| recepcionMasivaFactura, validacionRecepcionMasivaFactura | **3 (Masiva)** | CV-MAS, CV-VMAS |
| AnulacionFactura, ReversionAnulacionFactura, verificacionEstadoFactura y sus equivalentes de Documento de Ajuste | **1 (Online)** | CV-ANU, CV-REV, CV-EST, DA-* |
| Recepción Anexos / Anexo Electrolineras | 1 (Online) | CV-ANX, FC-ELE |

- Según EYE, el tipo de emisión "CONTINGENCIA" que devuelve la sincronización es de **uso exclusivo del SIN**.
- Para la anulación, la reversión y la verificación **solo se documenta el valor 1**, incluso si la factura se emitió fuera de línea.

### 3.4 `codigoModalidad`

- Recepción, anulación, paquete y masiva: "Uno (1) Electrónica y dos (2) Computarizada en línea". Documentos de Ajuste: "Uno (1) Electrónica en Linea 0 dos (2) para Computarizada en línea".
- Reversión (CV-REV, FC-REV, DA-REV) y verificación de estado (CV-EST, FC-EST, DA-EST): solo "Computarizada en línea: 2".
- CV-ANX y FC-ELE dicen "Electrónica en línea: 1". Parece un error de copia; ver §15.
- **M-INV envía siempre 2.**

### 3.5 `fechaEnvio`

- Tipo **TimeStamp**, obligatorio: "Fecha y hora en la cual se envía la Factura" (CV-REC).
- Formato: los casos de prueba (CP-IND, CP-PAQ, CP-MAS) y F1 lo describen como **"la fecha actual en formato UTC extendido sin zona horaria"**. Por los ejemplos XML (XML-CV: `2021-10-06T16:03:48.675`), queda así: `yyyy-MM-dd'T'HH:mm:ss.SSS`, sin offset.
- **La hora de referencia (UTC o local de Bolivia, UTC-4) es NO DOCUMENTADA.** Se recomienda usar la hora del SIN obtenida con la sincronización "Fecha y Hora" (otra especificación).
- Errores relacionados (ERR):

  | Código | Descripción |
  |---|---|
  | 935 | Fecha de envío inválida |
  | 943 | Formato de fecha de envío incorrecto |
  | 983 | Fecha de envío del paquete fuera de plazo |
  | 993 | Fecha de envío fuera de plazo |

### 3.6 Coherencia entre los datos de la solicitud y el XML

El SIN compara los datos de la solicitud con el contenido del XML. Errores de ERR que lo muestran:

| Código | Descripción |
|---|---|
| 1001 | "El NIT Enviado En El XML Es Inexistente O No Corresponde Al Cufd" |
| 1003 | "El Cufd Enviado En El XML Es Invalido" |
| 1004 | "La Sucursal Enviada En El XML No Corresponde A Los Datos Del Cufd" |
| 1008 | "El Punto De Venta Enviado En El XML Es Inexistente O Invalido" |
| 931 / 932 | Documento sector inválido / que no corresponde al servicio |
| 930 | "El Cuis No Corresponde A La Sucursal/Punto Venta" |

**Regla para M-INV:** los valores `nitEmisor`, `cufd`, `codigoSucursal`, `codigoPuntoVenta` y `codigoDocumentoSector` del XML (ver XML-CV) deben coincidir con los de la solicitud. La excepción son los paquetes, en los que el XML lleva el CUFD del evento (§12.3).

---

## 4. Estructura de la respuesta

Fuentes: CV-*, FC-*, DA-* (columnas "Salida").

| Campo | Tipo | Presente en |
|---|---|---|
| `codigoEstado` | Numérico | Todas las operaciones |
| `codigoDescripcion` | Alfanumérico | Recepción individual, paquete, validación de paquete, masiva, validación masiva, anulación, reversión (CV/FC) |
| `codigoRecepcion` | Alfanumérico | Recepción individual, paquete, masiva, sus validaciones, verificación de estado (CV/FC/DA) |
| `codigosRespuestas` | DTO[codigosRespuesta] (lista) | Todas. Se escribe distinto según la página: `codigosRespuestas`, `codigosRespuesta`, `CodigosRespuestas` y `todigosRespuestas` (errata en DA-REC) |
| `transaccion` | Boolean | Todas |
| `descripcion` | Alfanumérico | Solo DA-REV (en lugar de `codigoDescripcion`) |
| `return` | Numérico (= 926) | `verificarComunicacion` (FC-COM, DA-COM) |

**Campos del DTO `codigosRespuesta`: NO DOCUMENTADOS** en las páginas de servicio. EYE los describe como "una lista de mensajes con códigos, descripciones, número de archivo y número de detalle de los errores y/o advertencias detectados en cada una de las facturas".

M-INV debe modelar la lista así:

| Campo | Tipo | Uso |
|---|---|---|
| código | int | — |
| descripción | string | — |
| número de archivo | int? | Posición de la factura dentro del paquete |
| número de detalle | int? | Línea del detalle |

Los nombres exactos se toman del WSDL (FUERA DE DOC: `mensajesList { codigo, descripcion, numeroArchivo, numeroDetalle, advertencia }`, a verificar).

---

## 5. Construcción de `archivo` y `hashArchivo`

### 5.1 Factura individual (RecepcionFactura, recepcionDocumentoAjuste)

Pasos, según EYE ("Emisión y envío Individual"):

1. Generar el XML del documento fiscal según su sector. Para el sector 1 la raíz es `facturaComputarizadaCompraVenta` (XML-CV). Para la NCD computarizada es `notaFiscalComputarizadaCreditoDebito` (XML-NCD). El detalle está en la especificación de XSD.
2. (⚠ ELECTRÓNICA: firmar con XMLDSig. **En computarizada no se firma.**)
3. **Validar contra el XSD** para comprobar que el XML está bien formado y respeta la estructura.
4. **Comprimir el XML con GZIP.** El resultado va en `archivo`.
5. **Calcular el SHA-256 del archivo comprimido del paso 4.** El resultado va en `hashArchivo`, "también llamado Huella Digital".
6. Llamar a "Recepción de Factura". Resultado: 908 (validado) si no hay observaciones, o 904 (observado) en caso contrario, junto con el código de recepción, la lista de errores y `transaccion` = true/false.

### 5.2 Paquete fuera de línea o de contingencia (RecepcionPaqueteFactura)

Fuentes: EYE, MANC, CV-PAQ y RCP.

- **Hasta 500 facturas** por paquete. CV-PAQ: "paquetes de hasta 500 facturas".
- **Todas del mismo documento sector** (MANC: "Formar paquetes de hasta 500 Facturas (Todas del mismo documento sector)").
- EYE: "Comprimir con Gzip, el archivo resultante debe ser enviado … en la etiqueta archivo", más el SHA-256 "del archivo compreso".
- **Contenedor TAR**: las páginas de facturas **no lo mencionan**. RCP (Registro de Compras, mismo SIN) sí lo detalla: "Formar paquetes de hasta 500 documentos y empaquetarlos en un contenedor TAR (paquete.tar). Comprimir con Gzip al archivo del contenedor TAR (ejemplo: paquete.tar.gz), mismo que debe ser enviado en la etiqueta archivo. Obtener el HASH (SHA256) del archivo compreso".
- **Formato que implementará M-INV:** `archivo = GZIP( TAR( f_1.xml, f_2.xml, …, f_n.xml ) )`, con un XML por factura, sin firmar (computarizada).
  - Los **nombres de archivo dentro del TAR son NO DOCUMENTADOS**. Propuesta: `<numeroFactura>.xml` o `<cuf>.xml`.
  - El **orden importa**, porque la validación devuelve "número de archivo" para identificar la factura observada. M-INV debe guardar qué factura ocupa cada posición.
  - Si ese número empieza en 0 o en 1 es **NO DOCUMENTADO**.
- `cantidadFacturas` debe ser igual a la cantidad real. Errores:

  | Código | Descripción |
  |---|---|
  | 985 | "La Cantidad De Facturas Es Diferente A La Declarada" |
  | 954 | Excede el máximo en contingencia |
  | 972 | Mayor a la definida en la normativa |

- Tamaño máximo: error **971 "El Tamaño Del Archivo Excede El Tamaño Permitido De 100 Mb"** y error **3009** (tamaño mayor al definido en la norma).

### 5.3 Paquete masivo (recepcionMasivaFactura)

- Mismo formato que el paquete, pero con **hasta 1000 facturas** (CV-MAS: "Tamaño de los paquetes: máximo 1000"; EYE: "Formar paquetes de hasta 1000 Facturas").
- F1 Etapa IX dice "bloques de hasta 2000 Facturas" (contradicción; ver §15).
- Error **956**: la cantidad de facturas del paquete masivo supera el máximo. Error **955**: "No Existe Registro Para Autorizar El Proceso Masivo".

### 5.4 Hash

Fuente: SHA.

- Algoritmo `SHA-256` aplicado a un `byte[]` (el archivo) con `MessageDigest`. El resultado se convierte con `DatatypeConverter.printHexBinary(...).toLowerCase()`. Es decir: **hexadecimal en minúscula, 64 caracteres**.
- Error: **969 "El Parámetro Hash Es Invalido"** (ERR).
- **Ambigüedad:** CP-IND y F1 dicen "SHA 256 a la cadena archivo", mientras EYE dice "del archivo compreso". M-INV calcula el hash **sobre los mismos bytes GZIP que envía en `archivo`, antes de la codificación Base64 de SOAP**. Hay que confirmarlo en piloto: si se recibe 969, probar como alternativa sobre la cadena Base64.
- Ejemplos numéricos:
  - Vector de prueba del formato: `SHA-256("abc")` = `ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad`.
  - Con el XML de ejemplo XML-CV (2353 bytes) comprimido en GZIP con mtime = 0 (Python `gzip.compress`) se obtienen 1001 bytes, que empiezan por `1f 8b 08 00 …`. Su SHA-256 = `04b9ebcdedb27915142629aeca2bfa3b31ac985ba6768c9c05dfe5471b8741f2`.
  - **Los bytes GZIP varían entre implementaciones** (cabecera, nivel de compresión). Por eso el hash siempre se calcula sobre los bytes que efectivamente se envían, nunca se recalcula por separado.
- GZIP: el ejemplo Java de GZIP usa `GZIPOutputStream` y nombra la salida con extensión `.zip`, aunque el formato es **GZIP (RFC 1952), no ZIP**.

### 5.5 Referencia de implementación en .NET 8

Esta es una **propuesta**; el código no viene de la documentación.

```csharp
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

public static class SiatEmpaquetador
{
    public static byte[] Gzip(byte[] datos)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(datos);
        return ms.ToArray();
    }

    // hashArchivo: SHA-256 de los bytes GZIP, en hexadecimal minúscula (64 caracteres)
    public static string Sha256Hex(byte[] datos) =>
        Convert.ToHexString(SHA256.HashData(datos)).ToLowerInvariant();

    // Individual: archivo = GZIP(XML en UTF-8, sin BOM)
    public static (byte[] archivo, string hash) Individual(string xml)
    {
        var gz = Gzip(new UTF8Encoding(false).GetBytes(xml));
        return (gz, Sha256Hex(gz));
    }

    // Paquete o masivo: archivo = GZIP(TAR(xml1..xmlN)); el orden se conserva (numeroArchivo)
    public static (byte[] archivo, string hash) Paquete(IReadOnlyList<(string nombre, string xml)> facturas)
    {
        using var tarMs = new MemoryStream();
        using (var tw = new TarWriter(tarMs, TarEntryFormat.Ustar, leaveOpen: true))
            foreach (var (nombre, xml) in facturas)
                tw.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, nombre)
                { DataStream = new MemoryStream(new UTF8Encoding(false).GetBytes(xml)) });
        var gz = Gzip(tarMs.ToArray());
        return (gz, Sha256Hex(gz));
    }
}
```

El proxy SOAP generado desde el WSDL expone `archivo` como `byte[]` si el tipo WSDL es `base64Binary` (**NO DOCUMENTADO**; a verificar). En ese caso se pasa el `byte[]` directamente y el serializador hace el Base64.

---

## 6. Operaciones del Servicio Factura Compra Venta (sector 1)

Las páginas de CV-* y FC-* son prácticamente iguales, salvo dos diferencias: Recepción Anexos existe solo en CV y Anexo Electrolineras existe solo en FC.

### 6.1 `RecepcionFactura` — envío individual en línea

Fuentes: CV-REC (igual a FC-REC). Objeto `SolicitudServicioRecepcionFactura`.

El servicio verifica los parámetros, analiza si el archivo es correcto y **valida el XML contra el XSD**. Si todo está bien devuelve el código de recepción; si no, los códigos de error y advertencia.

**Entrada**

| # | Parámetro | Tipo | Oblig. | Valor |
|---|---|---|---|---|
| 1 | codigoAmbiente | Numérico | Sí | 1 / 2 |
| 2 | codigoPuntoVenta | Numérico | No | código de punto de venta o 0 |
| 3 | codigoSistema | Alfanumérico | Sí | — |
| 4 | codigoSucursal | Numérico | Sí | 0..n |
| 5 | nit | Numérico | Sí | — |
| 6 | codigoDocumentoSector | Numérico | Sí | 1 |
| 7 | codigoEmision | Numérico | Sí | **1** |
| 8 | codigoModalidad | Numérico | Sí | **2** |
| 9 | cufd | Alfanumérico | Sí | CUFD vigente |
| 10 | cuis | Alfanumérico | Sí | CUIS vigente |
| 11 | tipoFacturaDocumento | Numérico | Sí | **1** |
| 12 | archivo | Alfanumérico | Sí | GZIP(XML) (§5.1) |
| 13 | fechaEnvio | TimeStamp | Sí | ahora, en el formato de §3.5 |
| 14 | hashArchivo | Alfanumérico | Sí | "Sha256 de la cadena Archivo que se envía" |

**Salida:** `codigoEstado`, `codigoRecepcion`, `codigosRespuestas`, `transaccion`, `codigoDescripcion`.

**Estados esperados**

| Estado | Significado | Fuente |
|---|---|---|
| **908** | Recepción validada | EYE; CP-IND espera 908 con `transaccion` = TRUE en todos sus casos |
| **904** | Observado, con la lista de errores (texto de EYE) | EYE |
| **902** | "Recepción Rechazada" | ERR |

M-INV trata 902 y 904 igual: la factura **no es válida** en el SIN. Hay que corregirla y volver a enviarla.

**Errores frecuentes a manejar** (ERR):

- Datos de parámetros inválidos: 910 (ambiente), 911 (código de sistema), 912 (sistema no asociado), 913/929/930/973 (CUIS), 914/953/123 (CUFD inválido, no vigente o fuera de tolerancia), 915, 916, 917, 918, 919, 920 (archivo).
- 931/932 (documento sector), 933 (punto de venta), 935/943 (fecha de envío), 937 (NIT sin la modalidad), 938 (marcas de control), **939 (XML no cumple el XSD)**, 940 (NIT sin el documento sector habilitado).
- 952/1000 (CUF ya registrado), 969 (hash), 975 (sistema no autorizado u observado), 989 (token), 995/999 (servicio).
- Validaciones de contenido 1001–1061, por ejemplo: 1013 (monto total), 1015 (importe base para crédito fiscal), 1018 (subtotal), 1024 (sumatoria de detalles), 1036 (nominatividad), 1037 (NIT no válido), 1016/1017 (actividad o producto no habilitado), 1009 (fecha de emisión no válida para emisión en línea).
- Advertencias: serie 2000–2019, por ejemplo **2005** (NIT del cliente no válido) y **2000** (error de correlatividad del número de factura).

### 6.2 `AnulacionFactura`

Fuentes: CV-ANU (igual a FC-ANU), ANU y F2. Objeto `SolicitudServicioAnulacionFactura`.

La factura "deberá estar previamente registrada y validad[a] por la Administración Tributaria".

**Entrada:** los 11 parámetros comunes de §3.2, con `codigoEmision` = **1**, `codigoModalidad` = 2 y `tipoFacturaDocumento` = 1, más:

| Parámetro | Tipo | Oblig. | Descripción |
|---|---|---|---|
| codigoMotivo | Numérico | Sí | "Paramétrica que indica el motivo por el cual la Factura está siendo anulada" (catálogo "Motivos Anulación" de la sincronización; **los valores no están en este bloque**) |
| cuf | Alfanumérico | Sí | CUF de la factura que se anula |

**Salida:** `codigosRespuesta`, `codigoEstado`, `transaccion`, `codigoDescripcion` (sin `codigoRecepcion`).

**Estados:** **905 "Anulación Confirmada"**, **906 "Anulación Rechazada"** (ERR). CP-ANU espera 905 con `transaccion` = TRUE.

**Reglas** (ANU, F2, ERR):

- Solo se anula de forma **individual**, **hasta el día nueve (9) del mes siguiente** a la emisión.
- El documento debe estar registrado como **válido** y **no haber sido usado en una Declaración Jurada**.
- Sectores de exportación: 180 días (F2, punto 11). No aplica a M-INV.
- La anulación "podrá ser realizada desde la misma sucursal en la cual se originó la transacción o desde otra sucursal habilitada". Los valores `codigoSucursal`, `codigoPuntoVenta`, `cuis` y `cufd` son los de **quien anula** (INFERIDO).
- Toda anulación **debe notificarse al comprador** por correo electrónico u otro medio electrónico privado, indicando como mínimo el **Código de Autorización, el número de factura y el motivo**.
- Errores:

  | Código | Descripción |
  |---|---|
  | 924 | No existe |
  | 925 | Motivo inválido |
  | 934 | Fuera de plazo |
  | 936 | Ya anulada |
  | 941 | No disponible para ser anulada |
  | 945 | Estado de recepción de anulación incorrecto |
  | 946 | CUF inexistente |

- Una factura con la **anulación revertida no puede volver a anularse** (REV).

### 6.3 `ReversionAnulacionFactura`

Fuentes: CV-REV (igual a FC-REV), REV y VER23. Objeto `SolicitudServicioReversionAnulacionFactura`. Se agregó en la versión 1.0.38 de la documentación (14/12/2023) por la RND 102300000034.

**Entrada:** los parámetros comunes, con `codigoEmision` = 1 y `codigoModalidad` = "Computarizada en línea: 2", más `cuf` (Alfanumérico, Sí: "Código único de factura que está siendo revertida"). **No tiene `codigoMotivo`.**

**Salida:** `codigoEstado`, `codigosRespuesta`, `transaccion`, `codigoDescripcion`.

**Reglas:**

- Se puede revertir **"por única vez"** una anulación errónea, y el documento vuelve a quedar en estado "VÁLIDO".
- El plazo va **hasta el día 9 del mes siguiente a la emisión de la factura original**.
- Los documentos revertidos **no podrán ser anulados** otra vez.
- Puede hacerse desde otra sucursal habilitada.
- Hay que **notificar al comprador**.

**Estados según REV:**

| Código | Significado según REV |
|---|---|
| **907** | "Reversión Anulada Conforme" |
| **981** | factura no disponible para reversión |
| **924** | factura no existe |
| **3011** | el sistema no superó las pruebas de autorización para usar la reversión |
| **3012** | solicitud de reversión fuera de plazo |

ERR además lista 909 "Reversión De Anulación Rechazada", 968 "… Ya Se Encuentra Revertida" y 978 "Reversión … Confirmada". Hay contradicciones en estos códigos; ver §15.

**Habilitación:**

- Los sistemas ya autorizados deben completar las pruebas de reversión en piloto y presionar "finalizar pruebas" para quedar habilitados en producción.
- Los sistemas que están en proceso de autorización deben completar este set "obligatoriamente" (FC-REV) o "adicionalmente" (CV-REV).

### 6.4 `RecepcionPaqueteFactura` — paquetes fuera de línea o de contingencia

Fuentes: CV-PAQ (igual a FC-PAQ), EYE, CONT, MANC. Objeto `SolicitudServicioRecepcionPaquete`.

**Entrada:** los parámetros comunes, con `codigoEmision` = **2 (Offline)**, `codigoModalidad` = 2 y `tipoFacturaDocumento` = 1, más:

| Parámetro | Tipo | Oblig. | Descripción |
|---|---|---|---|
| archivo | Alfanumérico | Sí | "Paquete de Facturas que son enviadas para su validación" (§5.2) |
| fechaEnvio | TimeStamp | Sí | — |
| hashArchivo | Alfanumérico | Sí | SHA-256 |
| cafc | Alfanumérico | **No** | "Código de autorización de emisión de facturas de contingencia. **Nulo si es una factura normal**" (se usa solo en la transcripción de facturas manuales de contingencia) |
| cantidadFacturas | Numérico | Sí | Cantidad de facturas del paquete |
| codigoEvento | Numérico | Sí | "**Código que devolvió el método de registro de evento**", es decir, el código de recepción del evento significativo (CP-PAQ lo llama "Código de recepción de evento") |

- `cufd` es el **CUFD nuevo, vigente al enviar**. EYE: "Consumir el servicio correspondiente para obtener un nuevo CUFD" antes de registrar el evento y enviar. Los XML del paquete llevan el CUFD que estaba vigente durante el evento.
- **Salida:** `codigoEstado`, `codigoRecepcion`, `CodigosRespuestas`, `transaccion`, `codigoDescripcion`.
- **Estado esperado:** **901 (Pendiente)** con `codigoRecepcion` y `transaccion` = true (EYE, CP-PAQ).
- **Errores específicos** (ERR):
  - 942: código de recepción de evento inexistente.
  - 957: no existe el evento.
  - 984: el evento no corresponde al CUFD del evento registrado.
  - **1006**: el CUFD no corresponde al evento asociado al paquete.
  - 1040 y la advertencia 2001: fecha de emisión fuera del rango de contingencia.
  - 954, 972, 985: cantidades.
  - 983: fecha de envío del paquete fuera de plazo.
  - 1045/1046/1047: CAFC inválido, o fecha o número de factura no válidos para ese CAFC.

### 6.5 `validacionRecepcionPaqueteFactura`

Fuentes: CV-VPAQ (igual a FC-VPAQ) y EYE. Objeto `SolicitudServicioValidacionRecepcionPaquete`.

**Entrada:** los parámetros comunes, con `codigoEmision` = **2**, más `codigoRecepcion` (Alfanumérico, Sí: "Código Recepción enviado por el SIN", el que devolvió `RecepcionPaqueteFactura`).

**Salida:** `codigoEstado`, `codigoDescripcion`, `codigoRecepcion`, `transaccion`, `codigosRespuestas`.

**Estados:** **901 (pendiente)**, **904 (observada)** o **908 (validado)** (EYE).

- Con 904 viene la lista de mensajes con código, descripción, número de archivo y número de detalle.
- MODC, punto 6b: "se observa el paquete, se registran las facturas correctas y se rechazan las que contengan los errores". Hay que corregir las rechazadas y volver a enviarlas (MODC, punto 8).
- Error 923/944: el código de recepción es inválido o no existe.

### 6.6 `recepcionMasivaFactura`

Fuentes: CV-MAS (igual a FC-MAS) y EYE. Objeto `SolicitudServicioRecepcionMasiva`.

**Requisito previo:** registrar en el portal web del SIN la **periodicidad (diaria, semanal o mensual)** y el **tamaño de paquete (máximo 1000)**. Error 955 si no hay registro. Según EYE, está pensado para empresas que facturan grandes volúmenes por lotes: financieras, telecomunicaciones, servicios básicos.

**Entrada:** los parámetros comunes, con `codigoEmision` = **3 (Masiva)**, más `archivo`, `fechaEnvio`, `hashArchivo` y `cantidadFacturas`. **No lleva** `codigoEvento` ni `cafc`. Las facturas se generan en "modalidad en línea" (EYE).

**Salida:** `codigoEstado`, `codigoRecepcion`, `codigosRespuestas`, `transaccion`, `codigoDescripcion`. Estado esperado: **901** con `transaccion` = true.

**Errores:** 955, 956, 1039 ("Fecha Emisión Para Envío Masivo Incorrecto") y la advertencia 2002.

### 6.7 `validacionRecepcionMasivaFactura`

Fuentes: CV-VMAS (igual a FC-VMAS). Objeto `SolicitudServicioValidacionRecepcionMasiva`.

**Entrada:** los parámetros comunes, con `codigoEmision` = **3**, más `codigoRecepcion` (Sí).

**Salida:** igual que la validación de paquete. Estados **901, 904 o 908**.

### 6.8 `verificacionEstadoFactura`

Fuentes: CV-EST (igual a FC-EST), EYE, CONT, INGC. Objeto `SolicitudServicioVerificaEstadoFactura`.

Verifica el estado de una factura (o nota) computarizada a partir de su **CUF**.

**Entrada:** los parámetros comunes, con `codigoEmision` = 1 y `codigoModalidad` = "dos(2) Computarizada en Línea", más `cuf` (Alfanumérico, Sí: "Código Único de Factura a ser validado").

**Salida:** `codigoEstado`, `codigoRecepcion`, `codigosRespuestas`, `transaccion`.

**Valores de `codigoEstado` para esta operación: NO DOCUMENTADOS.** La documentación solo dice "código de aceptación" o "de observación". M-INV guarda el código y su descripción tal como llegan, y los traduce con el catálogo de **mensajes de servicios** de la sincronización.

**Usos obligatorios de buena práctica** (EYE, CONT, INGC):

- Tras una contingencia, verificar las facturas que quedaron **sin código de respuesta**.
- Tras un timeout durante la emisión, verificar si la factura quedó registrada.
- Antes de reintentar una anulación que falló, verificar si la factura ya figura como anulada. Si figura anulada, se completa la anulación solo en el sistema local; si figura válida, se vuelve a anular.

### 6.9 `verificarComunicacion`

Fuentes: FC-COM y DA-COM (en el grupo CV no hay página; EYE dice que existe en cada recurso).

- **Entrada:** ninguna.
- **Salida:** `return = 926` (comunicación exitosa), Numérico.
- **Uso** (EYE, INGC):
  - Consumirlo periódicamente. Si responde con un error (Falso, -1, errores de la serie 400 o 500, timeout, "Java Null Point", HTTP 500) y la respuesta se repite tras "un par" de reintentos, **pasar automáticamente a fuera de línea**.
  - Permanecer fuera de línea "un tiempo prudencial … **no mayor a dos horas**" antes de volver a intentar.

### 6.10 Recepción Anexos (número de serie o IMEI) — solo en Compra Venta

Fuente: CV-ANX. Objeto `SolicitudRecepcionAnexos`. La página muestra "Nombre Método: RecepcionFactura", lo que es un error evidente; el nombre real debe tomarse del WSDL.

Sirve para "el envío de los números de Serie e Imei realizados en una compra". Por su parte, el XML de compra-venta (XML-CV) ya incluye `numeroSerie` y `numeroImei` en cada línea de detalle.

| Parámetro | Tipo | Oblig. | Descripción |
|---|---|---|---|
| codigoAmbiente, codigoDocumentoSector, codigoEmision (1), codigoModalidad ("Electrónica en línea: 1" [sic]), codigoPuntoVenta (No), codigoSistema, codigoSucursal, cufd (vacío), cuis, nit, tipoFacturaDocumento | como en §3.2 | | |
| codigo | Alfanumérico | Sí | Número de serie o IMEI |
| codigoProducto | Alfanumérico | Sí | Código de producto del contribuyente |
| codigoProductoSin | Numérico | Sí | Código de producto SIN |
| tipoCodigo | Numérico | Sí | **1 = Número de serie, 2 = IMEI** |
| cuf | "Numérico" [sic] | Sí | La descripción dice, por error, "Código que identifica el Tipo de Factura"; debe ser el CUF de la factura |

- **Salida:** `codigoEstado`, `codigosRespuestas`, `transaccion`.
- **Obligatoriedad, momento del envío y si acepta una lista de anexos: NO DOCUMENTADOS.** Para M-INV (materiales de construcción) es **opcional**. Se deja para una fase posterior, detrás de un feature flag.

---

## 7. Recurso Facturación Computarizada en Línea (otros sectores)

Fuente: FC-*.

- Las operaciones `RecepcionFactura`, `AnulacionFactura`, `ReversionAnulacionFactura`, `RecepcionPaqueteFactura`, `validacionRecepcionPaqueteFactura`, `recepcionMasivaFactura`, `validacionRecepcionMasivaFactura`, `verificacionEstadoFactura` y `verificarComunicacion` tienen **exactamente los mismos parámetros, tipos, obligatoriedad y salidas** que en la §6.
- Diferencias frente a la §6:
  - No tiene "Recepción Anexos".
  - Tiene **`recepcionAnexosSuministroEnergia`** (FC-ELE, objeto `SolicitudRecepcionSuministroAnexos`), que envía el detalle de recargas hechas con tarjeta en electrolineras. Corresponde al sector 31, Suministro de Energía (TIPOS). Sus parámetros son los comunes más:
    - `cufFactSuministro` ("Numérico" [sic], Sí)
    - `fechaRecarga` (Fecha, Sí)
    - `montoRecarga` (Numérico, Sí)
    - `giftCard` (Numérico, Sí: "Identifica a la tarjeta con la cual se realizó la recarga")

    Su salida es `codigoEstado`, `codigosRespuestas`, `transaccion`. **No aplica a M-INV.**
- M-INV no usará este recurso mientras solo emita el sector 1. El cliente SOAP debe quedar preparado igual, por si se habilitan otros sectores.

---

## 8. Recurso Documentos de Ajuste (Notas de Crédito-Débito y Conciliación)

Fuentes: DA-*, TIPOS y XML-NCD.

### 8.1 `recepcionDocumentoAjuste`

Fuente: DA-REC; la página lo escribe "Nombre Método; recepcionDocumentoAjuste". Objeto `SolicitudServicioRecepcionDocumentoAjuste`.

Recibe **de manera individual** las Notas de Crédito-Débito y las Notas de Conciliación, tanto electrónicas como computarizadas.

- **Entrada:** idéntica a `RecepcionFactura` (§6.1): los 11 parámetros comunes más `archivo`, `fechaEnvio`, `hashArchivo`. Se usa `codigoEmision` = 1, `codigoModalidad` = 2, `codigoDocumentoSector` = 24 (NCD) o 29 (Conciliación), y `tipoFacturaDocumento` = 3. El XML es `notaFiscalComputarizadaCreditoDebito` (XML-NCD).
- **Salida:** `codigoEstado`, `codigoRecepcion`, `todigosRespuestas` (sic), `transaccion`.
- **Estados:** los mismos que en la recepción individual (908 / 904 / 902). Esto es INFERIDO, porque la página solo dice "código de éxito".
- **Errores propios:**

  | Código | Descripción |
  |---|---|
  | 1048 | "Factura De La Nota Crédito Débito No Encontrada" |
  | 1049 | "Detalle De La Nota Diferente Al Detalle De La Factura Original" |
  | 1033 | "El Monto Devuelto Es Mayor Al Monto Original" |
  | 1029 / 1030 / 1031 | Montos devuelto, original y efectivo |
  | 1053 | Actividad no autorizada para ese plazo |
  | 1054 | Monto de descuento de crédito-débito |
  | 1061 | Factura no válida para realizar la devolución |

- **Plazo** (TIPOS): la NCD puede emitirse **hasta 18 meses** después de la factura original. La Nota de Conciliación, para transacciones de periodos anteriores **no mayores a 12 meses**.

### 8.2 `anulacionDocumentoAjuste`

Fuente: DA-ANU. Objeto `SolicitudServicioAnulacionDocumentoAjuste`.

- **Entrada:** idéntica a `AnulacionFactura`: los comunes con `codigoEmision` = 1, más `codigoMotivo` y `cuf`.
- **Salida:** `codigosRespuesta`, `codigoEstado`, `transaccion`. La descripción dice que devuelve "un código de recepción", pero ese campo no aparece en la tabla de salida.
- Estados 905 / 906. CP-ANU usa el motivo "NOTA DE CREDITO-DEBITO MAL EMITIDA" y espera 905.

### 8.3 `verificacionEstadoDocumentoAjuste`

Fuente: DA-EST. Objeto `SolicitudServicioVerificacionEstadoDocumentoAjuste`.

- **Entrada:** los comunes con `codigoEmision` = 1 y `codigoModalidad` = "Computarizada en línea: 2", más `cuf`.
- **Salida:** `codigoEstado`, `codigoRecepcion`, `codigosRespuestas`, `transaccion`.

### 8.4 `reversionAnulacionDocumentoAjuste`

Fuente: DA-REV. Objeto `SolicitudServicioReversionAnulacionDocumentoAjuste`.

- **Entrada:** los comunes con `codigoEmision` = 1 y `codigoModalidad` = 2, más `cuf`.
- **Salida:** `codigoEstado`, `codigosRespuesta`, `transaccion`, `descripcion`.
- Mismas reglas que la §6.3: una única vez, hasta el día 9 del mes siguiente, sin posibilidad de volver a anular, y habilitación por pruebas en piloto.

### 8.5 `verificarComunicacion`

Fuente: DA-COM. Igual a la §6.9: `return = 926`.

### 8.6 Lo que el recurso no tiene

- **No hay** métodos de paquete ni masivos para documentos de ajuste.
- **El tratamiento de una NCD que no se puede enviar por falta de conexión es NO DOCUMENTADO.**
- Recomendación para M-INV: **no emitir NCD fuera de línea**. Se dejan en cola en estado "pendiente de emisión" hasta que vuelva la comunicación. Ver también §15.18.

---

## 9. Comparación con la modalidad Electrónica (⚠ ELECTRÓNICA)

Fuentes: FE-REC, FE-ANU, EYE, ERR.

| Aspecto | Computarizada (2) | Electrónica (1) ⚠ |
|---|---|---|
| Recurso para el sector 1 | Servicio Factura Compra Venta | Servicio Factura Compra Venta (sus páginas admiten 1 y 2) |
| Recurso para otros sectores | Facturación Computarizada en Línea | Facturación Electrónica en Línea |
| Parámetros de `RecepcionFactura` | 14 (§6.1) | **Los mismos 14**, con `codigoModalidad` "Electrónica en línea: 1" |
| Firma del XML | **No** | **XMLDSig antes de comprimir.** El servicio valida "si la firma es válida" |
| Requisito de acceso (FEL) | Token más la "huella del archivo XML" | Token más firma digital |
| Errores exclusivos | — | 921 (firmado incorrecto), 922 (firma no corresponde al contribuyente), 927 (certificado inválido), 928 (certificado revocado), 965 (sin firma vigente registrada) |
| `AnulacionFactura` | Mismos parámetros, modalidad 2 | Mismos parámetros, modalidad 1 |

**Conclusión.** Para pasar M-INV a electrónica en el futuro, basta con:

- cambiar `codigoModalidad`;
- cambiar el recurso para los sectores distintos de 1;
- agregar la firma XMLDSig antes del GZIP;
- usar los XSD `facturaElectronica*`.

El formato de `archivo` y `hashArchivo` y las respuestas no cambian.

---

## 10. Códigos de estado y error relevantes

Fuente: ERR. Es una selección para las operaciones de este bloque.

**Estados de proceso**

| Código | Descripción | Cuándo |
|---|---|---|
| 901 | Recepción Pendiente | Recepción de paquete o masiva; validación aún en proceso |
| 902 | Recepción Rechazada | Recepción no aceptada |
| 903 | Recepción Procesada | **Uso NO DOCUMENTADO** en las páginas del bloque |
| 904 | Recepción Observada | Individual con observaciones (EYE); paquete o masivo con errores |
| 905 | Anulación Confirmada | Anulación correcta |
| 906 | Anulación Rechazada | Anulación fallida |
| 907 | Reversión De Anulación Confirmada | Reversión correcta |
| 908 | Recepción Validada | Documento válido |
| 909 | Reversión De Anulación Rechazada | Reversión fallida |
| 926 | Comunicación Exitosa | `verificarComunicacion` |
| 978 | Reversión De La Factura o Nota De Crédito/Débito Confirmada | Aparece en ERR (relación con 907: ver §15) |

**Errores de parámetros y del sistema** (no se envía o hay que corregir la configuración)

| Código | Descripción |
|---|---|
| 910 | Ambiente |
| 911 | Código de sistema |
| 912 | Sistema no asociado al contribuyente |
| 913 | CUIS inválido |
| 914 | CUFD inválido |
| 915 | Tipo factura documento |
| 916 | Tipo emisión |
| 917 | Modalidad |
| 918 | Sucursal |
| 919 | NIT |
| 920 | Archivo |
| 923 | Código de recepción inválido |
| 925 | Motivo de anulación inválido |
| 929 / 973 | CUIS no vigente |
| 930 / 979 | CUIS no corresponde a la sucursal o punto de venta |
| 931 | Documento sector inválido |
| **932** | **Documento sector no corresponde al servicio** (enrutamiento incorrecto) |
| 933 | Punto de venta inexistente |
| 935 / 943 | Fecha de envío |
| 937 | NIT sin la modalidad |
| 938 / 961–964 / 3003–3007 | Marcas del NIT |
| 940 | NIT sin el documento sector |
| 953 | CUFD no vigente |
| 958 | Usuario no autorizado para el servicio |
| 959 | CUIS no asociado al sistema |
| 966 / 967 / 991 / 995 / 999 | Errores del SIN (reintentar) |
| 969 | Hash inválido |
| 971 / 3009 | Tamaño del archivo |
| 975 | Sistema no autorizado u observado |
| 988 / 123 | CUIS / CUFD fuera de tolerancia |
| 989 | Token inválido |
| 3008 | Advertencia: CUIS a punto de caducar |

**Errores de anulación**

| Código | Descripción |
|---|---|
| 924 | No existe en el SIN |
| 934 | Fuera de plazo |
| 936 | Ya anulada |
| 941 | No disponible para anular |
| 945 | Estado de recepción de la anulación incorrecto |
| 946 | CUF no existe |
| 968 | Ya revertida |

**Errores de paquete y evento**

| Código | Descripción |
|---|---|
| 942 / 957 | Evento no existe |
| 944 | Código de recepción no existe |
| 954 / 956 / 972 | Exceso de facturas |
| 955 | Sin registro de proceso masivo |
| 983 | Envío del paquete fuera de plazo |
| 984 / 1006 | CUFD no corresponde al evento |
| 985 | Cantidad distinta a la declarada |
| 1039 | Fecha para masivo |
| 1040 | Fecha fuera del rango de contingencia |
| 1045–1047 | CAFC |

**Errores de contenido del XML (sector 1 y NCD)**

| Código | Descripción |
|---|---|
| 939 | XSD |
| 952 / 1000 | CUF duplicado |
| 1001–1009 | Coherencia del XML con los datos de la solicitud |
| 1010 | "La Factura No Puede Ser Enviada Con Numero De CI/NIT/CEX o Para Montos Mayores A 3000" |
| 1011 | Complemento solo con CI |
| 1012 | Número de tarjeta solo con método de pago tarjeta |
| 1013 / 1014 / 1015 / 1018 / 1024 | Cálculos |
| 1016 / 1017 | Actividad o producto |
| 1034 / 1035 / 1041 / 1051 | Fechas |
| 1036 / 1037 / 1059 | Nominatividad, NIT, razón social |
| 1048 / 1049 / 1033 / 1061 | Notas |
| 1050 | Monto gift card |
| 3010 | "La Factura Ya Se Encuentra Utilizada o Consolidada" |

**Advertencias (no invalidan el documento; se muestran y se registran):** 2000–2019. Las más relevantes son 2000 (correlatividad), 2005 (NIT del cliente no válido) y 2007–2012 (cálculos).

---

## 11. Plazos y límites

| Regla | Valor | Fuente |
|---|---|---|
| Anulación de factura o nota | Hasta el **día 9 del mes siguiente** a la emisión; no se puede si ya se usó en una DDJJ | ANU |
| Anulación en sectores de exportación | 180 días (no aplica a M-INV) | F2 |
| Reversión de anulación | **Una sola vez**, hasta el **día 9 del mes siguiente** a la emisión original; lo revertido no se puede volver a anular | REV, CV-REV |
| Registro de un evento significativo | Hasta **48 h** después de finalizada la contingencia | CONT |
| Envío de paquetes emitidos fuera de línea | Dentro de las **48 h** posteriores a la recuperación | F2 (punto 8) |
| Envío de facturas manuales de contingencia transcritas | Dentro de las **72 h** posteriores al restablecimiento | MANC, F2 (punto 9) |
| Vigencia del CUFD cuando falla el servicio de CUFD | Se **amplía hasta 72 h** usando el último CUFD válido | INGC |
| Tiempo máximo recomendado en fuera de línea antes de reintentar | **≤ 2 horas** | INGC |
| Tamaño de paquete (contingencia) | **≤ 500** facturas, **mismo documento sector** | CV-PAQ, EYE, MANC |
| Tamaño de paquete masivo | **≤ 1000** facturas (F1 Etapa IX dice 2000) | CV-MAS, EYE |
| Tamaño del archivo | ≤ **100 MB** (error 971) | ERR |
| Emisión de NCD | ≤ **18 meses** después de la factura original | TIPOS |
| Nota de Conciliación | Transacciones de periodos anteriores ≤ **12 meses** | TIPOS |
| CUIS | Vigencia de 365 días (otra especificación) | CODA |
| CUFD | 24 h (otra especificación) | CODA, MODC |

---

## 12. Secuencias de proceso

### 12.1 Antes de emitir

Fuente: EYE.

1. Tener el token delegado (§3.1).
2. Tener el CUIS vigente de cada sucursal o punto de venta. Se pide una sola vez, o cuando vence.
3. Tener el **CUFD diario**.
4. **Sincronizar los catálogos a diario**: actividades, sectores, productos, fecha y hora, documento sector, más los catálogos de F1 Etapa II, que incluyen **motivos de anulación**, **mensajes de servicios**, **eventos significativos**, tipos de factura y tipos de emisión.
5. Con despliegue centralizado, la sincronización puede hacerse una sola vez con la Casa Matriz.
6. Consumir `verificarComunicacion` del recurso de forma periódica.

### 12.2 Emisión individual en línea (sector 1)

1. Construir el XML (CUF, CUFD y demás datos) y validarlo contra `facturaComputarizadaCompraVenta.xsd`.
2. Aplicar GZIP y calcular el SHA-256 (§5.1).
3. Llamar a `RecepcionFactura` del recurso Compra Venta con `codigoEmision` = 1.
4. Resultado:
   - **908**: guardar `codigoRecepcion`, marcar la factura como VALIDADA y entregar al comprador el XML y la representación gráfica (F2, punto 13).
   - **902 o 904**: guardar los mensajes, marcar la factura como RECHAZADA u OBSERVADA, corregirla y volver a enviarla. **No se reutiliza el número ni el CUF**: una corrección es una nueva emisión (INFERIDO).
   - **Timeout, -1, HTTP 500 o 400/404**:
     1. Marcar la factura como **SIN_RESPUESTA** y pasar a fuera de línea.
     2. Cuando vuelva la comunicación, pedir un CUFD nuevo y llamar a `verificacionEstadoFactura` con el CUF.
     3. Si la factura figura registrada en el SIN, INGC indica "proceder a su anulación", y CONT agrega "de ser necesario, evitando duplicidades".
     4. Después, registrar el evento y enviar los paquetes (INGC, "Emisión de Facturas"). Ver §15.24.

### 12.3 Fuera de línea (eventos 1–4 del catálogo de CONT: el sistema sigue funcionando)

Fuentes: EYE, CONT, INGC.

1. **Detectar la caída.** `verificarComunicacion` o una llamada al servicio falla de forma repetida. El sistema **entra solo** en fuera de línea (F2, punto 14: "el ingreso y salida de fuera de línea … se realice de manera automática").
2. Registrar internamente el inicio del evento y su motivo.
3. Emitir las facturas con **tipo de emisión fuera de línea (2)** y con el **CUFD vigente antes del corte**.
4. Cambiar la tercera leyenda de la representación gráfica de "en línea" a "fuera de línea" (F2, punto 5).
5. Validar cada factura contra el XSD y guardarlas **una por una**.
6. **Al recuperar la comunicación** (reintentar en ≤ 2 h):
   1. Obtener un **CUFD nuevo**.
   2. Llamar a `registroEventoSignificativo` (otra especificación) con la fecha de inicio y fin, el CUFD del evento y el código del evento del catálogo. De ahí sale el **código de recepción del evento**.
   3. Armar paquetes de **≤ 500 facturas del mismo documento sector**, en formato TAR + GZIP, y calcular el hash.
   4. Llamar a `RecepcionPaqueteFactura` con `codigoEmision` = 2, `cufd` = CUFD nuevo, `codigoEvento` = código de recepción del evento, `cafc` = null y `cantidadFacturas` = n. Respuesta esperada: **901** con `codigoRecepcion`.
   5. Llamar a `validacionRecepcionPaqueteFactura(codigoRecepcion)` hasta dejar de recibir 901:
      - **908**: todas las facturas quedan validadas.
      - **904**: se aplica cada mensaje a la factura según su número de archivo. Las demás facturas quedan registradas; las observadas se corrigen y se vuelven a enviar.
7. **Plazos:** registrar el evento en ≤ 48 h y enviar los paquetes en ≤ 48 h (§11).
8. Como buena práctica, verificar por CUF las facturas que quedaron sin respuesta.
9. Si el tipo de documento del cliente es **NIT**, en fuera de línea se envía **`codigoExcepcion` = 1** en el XML (CONT, EYE).

### 12.4 Contingencia con facturas manuales (eventos 5–7 del catálogo de CONT: el sistema no funciona)

Fuentes: EYE, MANC, CONT, F2.

1. Se emiten **facturas manuales de contingencia** preimpresas con **CAFC**, solicitadas con anticipación a una imprenta autorizada. En piloto también hay que solicitar un CAFC por cada documento sector y sucursal que se pruebe (EYE, "Nota").
2. Superada la contingencia (≤ 72 h):
   1. Registrar el evento con el código 5, 6 o 7, la fecha de inicio y fin "hasta el minuto mínimamente", el CUFD del evento (el de la fecha del evento), el CUFD del envío y una descripción. **Un evento por cada CAFC utilizado** (MANC).
   2. **Transcribir** cada factura a XML con tipo de emisión "fuera de línea" (2), usando el CUFD vigente al entrar en contingencia.
   3. Armar paquetes de ≤ 500, del mismo sector, en TAR + GZIP, y calcular el hash.
   4. Pedir un CUFD nuevo.
   5. Llamar a `RecepcionPaqueteFactura` con **`cafc`** y el `codigoEvento` (código de recepción del evento). Respuesta esperada: 901.
   6. Llamar a la validación: 901, 904 o 908.
3. F2 (punto 12): el sistema debe gestionar los CAFC y permitir la transcripción.

### 12.5 Emisión masiva (opcional)

Fuente: EYE.

1. Registrarse en el portal: periodicidad y tamaño.
2. Emitir los XML "en línea" y guardarlos.
3. Armar paquetes de ≤ 1000 en TAR + GZIP y calcular el hash.
4. Llamar a `recepcionMasivaFactura` con `codigoEmision` = 3. Respuesta esperada: 901.
5. Llamar a `validacionRecepcionMasivaFactura`: 901, 904 o 908.

### 12.6 Anulación

Fuentes: ANU, INGC.

1. Validar en M-INV que la factura esté VALIDADA (908), que no esté anulada ni revertida y que esté **dentro del plazo** (día ≤ 9 del mes siguiente).
2. Elegir el `codigoMotivo` del catálogo sincronizado.
3. Llamar a `AnulacionFactura` (o `anulacionDocumentoAjuste`):
   - **905**: marcar como ANULADA, **notificar al comprador** (CUF, número y motivo) y revertir en inventario lo que corresponda (regla de negocio de M-INV).
   - **906** u otro error: mostrar los mensajes.
4. Si hay timeout, reintentar "un par" de veces. Si sigue fallando, esperar, **verificar el estado por CUF**:
   - Si ya figura anulada, completar la anulación solo en el sistema local.
   - Si figura válida, volver a anular.

### 12.7 Reversión de la anulación

Fuentes: REV, CV-REV.

1. Validar en M-INV que la factura esté ANULADA, que **nunca haya sido revertida** y que esté dentro del plazo (día ≤ 9 del mes siguiente a la emisión original).
2. Llamar a `ReversionAnulacionFactura`:
   - **907**: marcar como VALIDADA con la bandera `revertida` = true, lo que **bloquea una nueva anulación**, y notificar al comprador.
   - 3011: el sistema no está habilitado; completar las pruebas en piloto.
   - 3012: fuera de plazo.

---

## 13. Casos de prueba para la homologación

### 13.1 Planillas de este bloque (leídas completas)

Columnas en común: `codigoAmbiente` = 2, sucursal = 0, `transaccion` esperada = TRUE. Todos los datos de identidad dicen "su código de sistema / su NIT / su CUIS / su CUFD válido". `fechaEnvio` = "la fecha actual en formato UTC extendido sin zona horaria".

| Planilla | Filas | Estructura | Resultado esperado |
|---|---|---|---|
| **CP-IND** (Emisión individual, `codigoEmision` = 1) | **64** | 32 sectores × 2: punto de venta = 1 y punto de venta = 0. Sectores 1–24, 28–35 | Todas **908 "RECEPCION VALIDADA"** |
| **CP-PAQ** (Paquetes, `codigoEmision` = 2) | **432** | 27 sectores (1–23, 28–31; **sin 24** y sin 32–35) × 16 casos | 378 casos **901 "PENDIENTE"** y 54 casos **908** (validación) |
| **CP-MAS** (Masiva, `codigoEmision` = 3) | **216** | 27 sectores (los mismos que CP-PAQ) × 8 casos | 108 casos **901** y 108 casos **908** |
| **CP-ANU** (Anulación) | **56** | 28 sectores (1–24, 28–31) × 2: punto de venta = 1 y = 0 | Todas **905 "ANULACION CONFIRMADA"**. **No hay casos de reversión**, aunque la hoja se llame "ANULACIÓN y REVERSIÓN" |

**Estructura de los 16 casos por sector en CP-PAQ.** Hay 7 eventos. Para cada evento:

- 1 caso con "cantidad de facturas **igual a 500**" y punto de venta = 1, que espera 901;
- 1 caso con "**menor a 500**" y punto de venta = 0, que espera 901.

Después vienen 2 casos de validación (punto de venta = 1 y = 0) con el "código de recepción", que esperan 908. Los casos de recepción llevan "El código de recepción del evento registrado".

Eventos (código y descripción) tal como figuran en CP-PAQ:

| Código en CP-PAQ | Evento |
|---|---|
| 1 | Corte de suministro de energía eléctrica |
| 2 | Corte del servicio de internet |
| 3 | Virus informático o falla de software |
| 4 | Cambio de infraestructura del sistema o falla de hardware |
| 5 | Inaccesibilidad al servicio web de la Administración Tributaria |
| 6 | Ingreso a zonas sin internet por despliegue de punto de venta en vehículos automotores |
| 7 | Venta en lugares sin internet |

**Ojo:** esta numeración **no coincide** con la de CONT, que numera así: 1 corte de internet, 2 inaccesibilidad, 3 zonas sin internet, 4 lugares sin internet, 5 virus, 6 hardware, 7 energía. **Los códigos deben tomarse siempre del catálogo sincronizado.**

**Estructura de los 8 casos por sector en CP-MAS:**

| Caso | Punto de venta | Cantidad | Esperado |
|---|---|---|---|
| 1 | 0 | igual a 1000 | 901 |
| 2 | 0 | validación | 908 |
| 3 | 1 | igual a 1000 | 901 |
| 4 | 1 | validación | 908 |
| 5 | 0 | menor a 1000 | 901 |
| 6 | 0 | validación | 908 |
| 7 | 1 | menor a 1000 | 901 |
| 8 | 1 | validación | 908 |

### 13.2 Casos que corresponden a M-INV

**Sector 1**, con `tipoFacturaDocumento` = 1:

| Planilla | Casos | Detalle |
|---|---|---|
| CP-IND | 1 y 2 | Punto de venta = 1 y = 0; esperan 908 |
| CP-PAQ | 1 a 16 | 7 eventos × 2 + 2 validaciones |
| CP-MAS | 1 a 8 | Solo si se habilita la emisión masiva |
| CP-ANU | 1 y 2 | Motivo "FACTURA MAL EMITIDA"; esperan 905 |

**Sector 24** (NCD), con `tipoFacturaDocumento` = 3, solo si M-INV emite notas:

- CP-IND: casos 47 y 48, que esperan 908.
- CP-ANU: casos 47 y 48, con motivo "NOTA DE CREDITO-DEBITO MAL EMITIDA", que esperan 905.
- No hay casos de paquete ni masivos para el sector 24.

**Requisito de infraestructura:** en piloto hay que tener registrado un **punto de venta con código 1** en la sucursal 0, además de operar sin punto de venta (0).

### 13.3 Cantidades exigidas por la Fase I

Fuente: F1.

| Etapa | Planilla | Exigencia |
|---|---|---|
| I CUIS | CasosDePruebaCUIS | 2 pruebas por caso |
| II Sincronización | CasosDePruebaSincronizaciónCatálogos | 50 pruebas por caso |
| III CUFD | CasosDePruebaCUFD | 100 pruebas por caso |
| **IV Emisión individual** | CP-IND | **500 emisiones individuales** "de acuerdo al detalle en el apartado de seguimiento". Para **todos** los documentos sector asociados a la actividad económica |
| V Eventos significativos | CasosDePruebaEventosSignificativos | 5 pruebas por caso |
| **VI Paquetes** | CP-PAQ | **10 pruebas por caso**. Paquetes de hasta 500 facturas, por cada documento sector asociado |
| **VII Anulación** | CP-ANU | **250 anulaciones** |
| VIII Firma digital ⚠ ELECTRÓNICA | CasosDePruebaFirmaDigital | 250 emisiones firmadas (no aplica) |
| **IX Masiva** | CP-MAS | **10 pruebas por caso**. El texto dice "bloques de hasta 2000 facturas" |
| X Masiva IEHD | CasosDePruebaEmisionMasivaIEHD | Solo hidrocarburos (no aplica) |
| **XI Reversión de la anulación** | CasosDePruebaReversionAnulacion.xls (**no disponible localmente**) | **98 reversiones** |

El "apartado de seguimiento" que distribuye los totales entre casos está en el portal SIAT: **NO DOCUMENTADO** aquí.

### 13.4 Fases II y III

Fuentes: F2, F3.

**Fase III (piloto):**

- Pasos previos: asociación del sistema proveedor, confirmación del contribuyente, token delegado y configuración de clientes, productos, sucursales y puntos de venta.
- Pruebas: sincronización, emisión individual, anulación, reversión, eventos, fuera de línea con paquetes automáticos, transcripción manual (cuando corresponda) y masiva (cuando corresponda).
- Iniciar operaciones equivale a una **declaración jurada**.

**Fase II (inspección)** revisa, entre otros puntos:

- nominatividad;
- que no haya facturas en 0 salvo con gift card;
- combinación de medios de pago;
- rotación de leyendas;
- cambio de la leyenda en línea / fuera de línea;
- enmascarado de la tarjeta (`9999000000009999`);
- plazos de 48 h y 72 h;
- plazo de anulación;
- gestión de CAFC;
- envío del XML y la representación gráfica al comprador;
- **entrada y salida automática de fuera de línea**.

---

## 14. Qué debe implementar M-INV

Arquitectura sugerida: Domain / Application / Infrastructure / WPF, siguiendo la Clean Architecture ya existente.

1. **Configuración SIAT por empresa**:
   - `codigoAmbiente` (1 / 2), `codigoSistema`, NIT, `codigoModalidad` = 2.
   - **Token delegado** cifrado en reposo (por ejemplo, DPAPI o protección de columna), con su fecha de vencimiento y un aviso antes de que caduque (error 989).
   - Las **URL de cada recurso por ambiente** (Compra Venta, Computarizada, Documentos de Ajuste; y Códigos, Sincronización y Operaciones de otras especificaciones), **editables** porque no están documentadas (§2.4).
2. **Mapeo organizacional**:
   - Cada sucursal de M-INV a su `codigoSucursal` del SIN (Casa Matriz = 0).
   - Cada caja o punto de venta a su `codigoPuntoVenta` del SIN (0 = sin punto de venta).
   - Guardar el CUIS y el CUFD vigentes de cada combinación sucursal / punto de venta.
3. **Enrutador de recurso**: `ResolverRecurso(modalidad, codigoDocumentoSector, tipoFacturaDocumento)`:
   - sector 1 → Compra Venta;
   - tipo 3 (sectores 24, 29, 47, 48) → Documentos de Ajuste;
   - resto → Computarizada.

   La tabla debe ser configurable. Si llega el error **932**, se genera una alerta de "configuración de servicio incorrecta".
4. **Clientes SOAP** generados con `dotnet-svcutil` a partir del WSDL real, detrás de una interfaz `ISiatFacturacionGateway`:
   - Métodos: `RecepcionFactura`, `AnulacionFactura`, `ReversionAnulacionFactura`, `RecepcionPaquete`, `ValidacionPaquete`, `RecepcionMasiva`, `ValidacionMasiva`, `VerificacionEstado`, `VerificarComunicacion` y `RecepcionAnexos` (opcional). Más los equivalentes de Documento de Ajuste.
   - Un `IClientMessageInspector` o `HttpMessageHandler` agrega el header `apikey: TokenApi <token>`.
   - Timeouts configurables, por ejemplo 30 s.
5. **Empaquetador** (§5.5):
   - GZIP individual, TAR + GZIP para paquetes, SHA-256 en hexadecimal minúscula.
   - **Validación contra el XSD antes de enviar** (la evita el error 939).
   - Controles de límites: ≤ 500 facturas en contingencia, ≤ 1000 en masivo, ≤ 100 MB, un solo documento sector por paquete.
   - `cantidadFacturas` igual a la cantidad real.
6. **Emisión individual en línea**:
   - Llamar a `RecepcionFactura` al confirmar una venta facturada.
   - Estados internos: `BORRADOR → ENVIANDO → VALIDADA (908) | OBSERVADA (904) | RECHAZADA (902) | SIN_RESPUESTA (timeout) | PENDIENTE_PAQUETE (fuera de línea) | EN_PAQUETE (901) | ANULADA (905) | ANULACION_PENDIENTE`.
   - Una factura que no llegó a VALIDADA **no descuenta inventario de forma definitiva** (regla de negocio a definir con el usuario) o queda marcada para conciliar.
7. **Monitor de conectividad** (servicio en segundo plano):
   - `verificarComunicacion` **por recurso** cada N minutos (configurable) y antes de emitir si el último chequeo tiene más de X segundos.
   - Si falla repetidamente (falso, -1, 4xx, 5xx, timeout), pasar **automáticamente a FUERA_DE_LÍNEA**.
   - Reintentar a los ≤ 2 h (configurable).
   - Mostrar el estado en la barra de la ventana principal.
8. **Modo fuera de línea**:
   - Emitir con tipo de emisión 2 y el CUFD vigente antes del corte.
   - Cambiar la leyenda a "fuera de línea".
   - Con cliente identificado por NIT, enviar `codigoExcepcion` = 1.
   - Registrar el evento local: inicio, fin, código del catálogo, CUFD del evento y facturas asociadas.
9. **Recuperación automática**, al volver la comunicación:
   1. pedir un CUFD nuevo;
   2. registrar el evento significativo (otra especificación);
   3. armar paquetes de ≤ 500 del mismo sector;
   4. llamar a `RecepcionPaqueteFactura` con `codigoEvento` y `cafc` = null;
   5. esperar la respuesta 901 y guardar `codigoRecepcion`;
   6. consultar la validación hasta 908 o 904, con backoff (por ejemplo 10 s, 30 s, 60 s, …);
   7. con 904, aplicar los mensajes a cada factura por su **número de archivo** y reenviar las corregidas.

   Alertas de vencimiento a las 48 h (fuera de línea) y 72 h (manual).
10. **Contingencia manual con CAFC**:
    - Registro de los CAFC y de los rangos de facturas manuales.
    - Pantalla de **transcripción** de facturas manuales.
    - Un evento por cada CAFC.
    - Envío con `cafc`.
    - Control de los errores 1045–1047.
11. **Facturas sin respuesta**: una bandeja con las facturas que tuvieron timeout. Al recuperar la comunicación se llama a `verificacionEstadoFactura` por CUF:
    - si están registradas, se marcan VALIDADAS y **no se reenvían** (se evitan duplicados; si ya se habían reenviado, se anula el duplicado);
    - si no están registradas, pasan a un paquete.

    **[corregido por revisión]** Esta variante contradice el texto literal de INGC ("Emisión de Facturas": *"Si se encuentra registrada en los Servidores del SIN, proceder a su anulación luego registrar el evento significativo y enviar los paquetes"*) y además no es realizable tal cual: la factura original lleva en su CUF el tipo de emisión 1 (online), así que no puede viajar en un paquete `codigoEmision = 2` sin regenerar su CUF. **Flujo consolidado** (ver `00-indice-y-contradicciones.md`, C-07): ante un timeout en `RecepcionFactura`, la venta se **re-emite de inmediato fuera de línea** (nuevo número, tipo de emisión 2, CUF nuevo, CUFD vigente) y esa es la factura que recibe el cliente; la original queda `SIN_RESPUESTA`. Al recuperar la comunicación: CUFD nuevo → `verificacionEstadoFactura` de la original → si figura registrada, **se anula** (motivo del catálogo) → registrar evento → enviar paquetes. Si no figura registrada, la original se marca `DESCARTADA` sin enviarla.
12. **Anulación**:
    - Pantalla con el motivo, tomado del catálogo sincronizado.
    - Validación local del plazo (día ≤ 9 del mes siguiente), de que la factura esté en 908, no anulada y **no revertida**.
    - Permite anular desde otra sucursal.
    - Resultados 905 / 906.
    - **Notificación automática al comprador** por correo con CUF, número y motivo.
    - Reversión del movimiento de inventario, según la regla de M-INV.
    - Ante un timeout, verificar el estado antes de reintentar.
13. **Reversión de la anulación**:
    - Botón disponible solo si la factura está ANULADA, nunca fue revertida y está dentro del plazo.
    - Resultado 907: marcar `Revertida` = true (bloquea una nueva anulación) y notificar al comprador.
    - Manejo de 3011 y 3012.
    - Registro de si el sistema quedó habilitado para reversión, tras las pruebas en piloto.
14. **Notas de Crédito-Débito (sector 24)**:
    - Emisión desde una devolución de venta de M-INV, siempre ligada a la factura original: número, CUF (`numeroAutorizacionCuf`) y fecha.
    - Plazo de 18 meses.
    - Se envía al recurso Documentos de Ajuste.
    - Solo en línea: si no hay conexión, se deja en cola.
    - Anulación, reversión y verificación con los métodos `…DocumentoAjuste`.
15. **Verificación de estado** manual (pantalla "Consultar CUF en el SIN") y automática (conciliación diaria de las facturas del día).
16. **Emisión masiva** (opcional, desactivada por defecto): configuración de periodicidad y tamaño, lote de hasta 1000, `recepcionMasivaFactura` y su validación.
17. **Anexos serie / IMEI** (opcional, fase posterior): `RecepcionAnexos` con `tipoCodigo` 1 (serie) o 2 (IMEI).
18. **Persistencia** (EF Core + PostgreSQL). Tablas sugeridas:
    - `siat_documento_fiscal`:
      - identificadores: id, venta_id, cuf, numero, sector, tipo_factura_documento, modalidad, tipo_emision;
      - contexto de emisión: sucursal_sin, punto_venta_sin, cuis, cufd, fecha_emision;
      - contenido: xml (texto), archivo_gzip (bytea, opcional), hash;
      - último envío: fecha_envio, codigo_recepcion, codigo_estado, codigo_descripcion, transaccion;
      - estado interno y relaciones: estado_interno, paquete_id, evento_id, cafc, intentos;
      - anulación: anulada_en, motivo_anulacion, revertida, revertida_en;
      - notificación al comprador: notificado_comprador_en.
    - `siat_mensaje`: documento_id o paquete_id, codigo, descripcion, numero_archivo, numero_detalle, es_advertencia.
    - `siat_paquete`: id, tipo (contingencia / manual / masivo), sector, codigo_evento_recepcion, cafc, cantidad, archivo, hash, fecha_envio, codigo_recepcion, codigo_estado, intentos_validacion, y el orden de las facturas (`siat_paquete_item` con `posicion`).
    - `siat_evento`: inicio, fin, codigo_evento, cufd_evento, codigo_recepcion_evento, estado.
    - `siat_bitacora_soap`: operacion, recurso, request y response **sin token**, duracion, http_status, fecha. Sirve para auditoría y para la inspección.
19. **Catálogo de códigos de respuesta**:
    - Precargar la tabla de ERR (§10) como semilla.
    - Actualizarla con el catálogo "mensajes de servicios" de la sincronización.
    - Mostrar la descripción en español junto a cada código.
20. **Servicio en segundo plano** (IHostedService o un worker dentro de WPF):
    - Cola / outbox de envíos con reintentos idempotentes por CUF (para no duplicar: los errores 952 y 1000 se tratan como "ya registrada" y llevan a verificar el estado).
    - Consulta periódica de la validación de paquetes.
    - Alertas de plazos.
21. **Hora oficial**: calcular `fechaEnvio` y `fechaEmision` con la hora sincronizada con el SIN, en formato `yyyy-MM-ddTHH:mm:ss.fff` sin zona horaria.
22. **Pantallas WPF**:
    - Configuración SIAT.
    - Monitor SIAT: en línea o fuera de línea, CUIS y CUFD vigentes por sucursal y punto de venta, último 926.
    - Bandeja de documentos fiscales, con filtros por estado y sucursal.
    - Detalle con XML, mensajes y un botón para reintentar.
    - Anulación y reversión.
    - Paquetes y eventos (pendientes, en validación, observados).
    - Transcripción de contingencia manual.
    - Consulta por CUF.
    - Homologación (ver el punto 23).
23. **Asistente de homologación** (solo en ambiente 2):
    - Ejecuta en lote los casos de CP-IND, CP-PAQ (paquetes de exactamente 500 y de menos de 500, con cada evento), CP-MAS y CP-ANU para los sectores de la empresa.
    - Lleva contadores contra las exigencias de la Fase I (500 individuales, 10 por caso de paquete y masiva, 250 anulaciones, 98 reversiones).
    - Genera un reporte.
24. **Permisos por rol** (sobre los roles actuales de M-INV):
    - Cajero y Ventas: emitir.
    - Administrador y Gerencia: anular, revertir y configurar SIAT.
    - Consulta: solo ver.

---

## 15. Dudas y huecos de la documentación

1. **Los nombres de servicio del WSDL, las URL, los namespaces y el nombre exacto de las operaciones** (mayúsculas y minúsculas) no están en el corpus. Ver la pista FUERA DE DOC de §2.4; hay que confirmarlos en piloto.
2. **La regla sector 1 → Servicio Compra Venta es INFERIDA.** Ninguna página la dice de forma literal. Las páginas del grupo Computarizada también aceptan "1 y 2" en modalidad, lo que parece copiado. Confirmar con el WSDL y con el error 932.
3. **El TAR no se menciona** en las páginas de facturas; solo aparece en Registro de Compras (RCP). Tampoco están documentados los nombres de los archivos dentro del TAR, su orden ni si "número de archivo" empieza en 0 o en 1.
4. **El hash**: EYE dice "del archivo compreso" y CP-IND / F1 dicen "a la cadena archivo". No está claro si se calcula sobre los bytes GZIP o sobre su Base64.
5. **El tipo de `archivo`** figura como "Alfanumérico". No está documentado si el WSDL usa `base64Binary`.
6. **`fechaEnvio`**: el formato es "UTC extendido sin zona horaria", pero no se documenta si la hora es UTC o la local de Bolivia.
7. **`codigoEmision` en anulación, reversión y verificación** solo admite 1 (Online). No se documenta qué valor usar para facturas emitidas fuera de línea (se supone 1).
8. **`codigoModalidad`** en CV-ANX y FC-ELE dice "Electrónica en línea: 1": error de copia, o esos anexos solo existen para electrónica.
9. **Recepción Anexos (CV-ANX)**: el nombre del método que aparece es erróneo ("RecepcionFactura"); `cuf` figura como "Numérico" con una descripción equivocada; `cufd` no tiene obligatoriedad; no se sabe si acepta una lista ni cuándo es obligatorio.
10. **Los nombres de campos de la respuesta** son inconsistentes (`codigosRespuestas` / `codigosRespuesta` / `CodigosRespuestas` / `todigosRespuestas`; `descripcion` frente a `codigoDescripcion`). Los campos del DTO `codigosRespuesta` no están documentados.
11. **Los valores de `codigoEstado` de `verificacionEstadoFactura` y `verificacionEstadoDocumentoAjuste`** no están documentados.
12. **Estado de una recepción individual no válida**: EYE dice 904 (observada) y ERR tiene 902 (rechazada). No se sabe cuál devuelve realmente el servicio individual (se tratan igual).
13. **Códigos de reversión contradictorios**: REV usa 981 para "factura no disponible para reversión", pero en ERR 981 es "Rango De Fechas De Evento Significativo Invalido". Tampoco queda clara la relación entre 907 y 978.
14. **Los códigos 3011 y 3012** (REV) no figuran en ERR, que llega hasta 3010. ERR dice "Algunos de los códigos", así que la lista no está completa.
15. **Tamaño de la emisión masiva**: 1000 según CV-MAS, EYE y CP-MAS; 2000 según F1 Etapa IX.
16. **Numeración de los eventos significativos**: CP-PAQ asocia el código 1 a "corte de energía", mientras CONT asocia el 7 a energía y el 1 a internet. Además, EYE y MANC dicen que la contingencia manual usa los eventos 5, 6 y 7, pero CP-PAQ no tiene columna CAFC.
17. **Paquetes del sector 29 (Nota de Conciliación)**: aparecen en CP-PAQ y CP-MAS, pero el recurso Documentos de Ajuste no documenta métodos de paquete. No se sabe a qué recurso van.
18. **NCD (sector 24)**: no hay casos de paquete ni masivos, ni métodos para ellos. No se documenta qué hacer con una NCD si no hay conexión.
19. **La planilla de reversión** (`CasosDePruebaReversionAnulacion.xls`, 98 reversiones) está listada en `adjuntos.txt`, pero no está en la carpeta local. CP-ANU no contiene casos de reversión. **[corregido por revisión]** Resuelto: la especificación 08 la descargó de la URL oficial y la volcó en `specs/_work08/CasosDePruebaReversionAnulacion.txt`.
20. **Los diagramas de flujo** de cada servicio son imágenes no descargadas. Podrían contener detalles de secuencia que no están en el texto.
21. **El grupo Compra Venta no tiene página de `verificarComunicacion`**. EYE dice que existe en cada recurso; hay que confirmarlo en el WSDL.
22. **Longitudes y rangos** de todos los parámetros de las solicitudes: no documentados.
23. **Los valores de `codigoMotivo`** (catálogo de motivos de anulación) no están en este bloque. Los casos de prueba solo usan las descripciones "FACTURA MAL EMITIDA" y "NOTA DE CREDITO-DEBITO MAL EMITIDA".
24. **Factura con timeout que sí quedó registrada en el SIN**: INGC dice "proceder a su anulación" y CONT dice "de ser necesario … evitando duplicidades". No está claro si hay que anularla siempre o solo cuando además se reemitió fuera de línea.
25. **Datos de quien anula**: no se dice de forma explícita que `cufd`, `cuis`, `codigoSucursal` y `codigoPuntoVenta` en anulación y reversión sean los de la sucursal que anula y no los de la emisora (se infiere de "desde otra sucursal habilitada").
26. **Obligatoriedad de la emisión masiva en la homologación**: F1 Etapa IX parece exigirla para todos los sectores, mientras F3 dice "cuando corresponda". Hay que confirmarlo con el SIN si M-INV no la usará.
27. **Código de `tipoFacturaDocumento` para "Documento Equivalente"** (sector 30): no aparece en este bloque.
28. **El estado 903 "Recepción Procesada"** está en ERR, pero ninguna página del bloque explica cuándo se devuelve.
