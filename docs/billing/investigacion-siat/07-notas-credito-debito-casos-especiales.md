# Spec 07: Notas de Crédito-Débito, Tasa Cero, casos especiales, modalidades y Registro de Compras y Ventas (RCV)

Especificación de implementación para M-INV (rama `Inventario-V4.1`), modalidad **Computarizada en Línea** (`codigoModalidad = 2`, sin firma digital).

## Convenciones

- **⚠ ELECTRÓNICA**: aplica solo a la modalidad Electrónica en Línea (`codigoModalidad = 1`). M-INV no lo implementa ahora.
- **NO DOCUMENTADO**: el dato no aparece en las fuentes leídas. No se inventa.
- **[RECOMENDACIÓN M-INV]**: decisión de diseño propia. No es un requisito del SIN.
- **[DEDUCCIÓN]**: conclusión que se saca de juntar dos o más fuentes. No hay una frase literal que la diga.
- Todas las fechas XML siguen el "formato UTC extendido" sin zona horaria, por ejemplo `2021-10-06T16:03:49.570`.

## 0. Fuentes

Los archivos de páginas están en `scratchpad/siat/txt/` y los adjuntos en `scratchpad/siat/adjuntos/`.

### Bloque asignado

| Id | Archivo | Contenido real |
|---|---|---|
| B1 | `facturacion-en-linea__casos-especiales__notas-de-credito-debito.md` | Página de 92 KB, de la que unos 91 KB son **una sola imagen PNG en base64**. El texto útil son 2 párrafos más un cuadro de modalidades, decodificado a mano en §3.2. No trae los campos de la nota (esos vienen de A1–A9). |
| B2 | `facturacion-en-linea__casos-especiales__facturacion-tasa-cero-iva-ley-n-1613.md` | Tasa Cero de la Ley 1613 |
| B3 | `facturacion-en-linea__casos-especiales__facturacion-conjunta.md` | Facturación conjunta |
| B4 | `facturacion-en-linea__casos-especiales__facturacion-por-terceros.md` | Facturación por terceros |
| B5 | `facturacion-en-linea__casos-especiales__facturacion-por-terceros-ypfb.md` | Facturación por terceros de YPFB |
| B6 | `facturacion-en-linea__casos-especiales__facturacion-comisionistas.md` | Comisionistas |
| B7 | `facturacion-en-linea__casos-especiales__sap-businessone.md` | SAP Business One |
| B8 | `informacion__modalidades-facturacion__facturacion-electronica.md` | Modalidad electrónica, como contraste |
| B9 | `informacion__modalidades-facturacion__facturacion-portal-web.md` | Portal Web, como contraste |
| B10 | `informacion__modalidades-de-facturacion-manual__computarizada-sfv.md` | Computarizada SFV |
| B11 | `registro-de-compras-y-ventas__registro-de-ventas__ventas-estandar.md` | Registro de Ventas Estándar |
| B12 | `registro-de-compras-y-ventas__confirmacion-y-registro-de-compras.md` | Registro de Compras |
| B13 | `registro-de-compras-y-ventas__registro-de-compras-serv__introduccion-registro.md` | Habilitar el servicio |
| B14 | `registro-de-compras-y-ventas__registro-de-compras-serv__consulta-de-compras.md` | `consultaCompras` |
| B15 | `registro-de-compras-y-ventas__registro-de-compras-serv__confirmacioncompras.md` | `confirmacionCompras` |
| B16 | `registro-de-compras-y-ventas__registro-de-compras-serv__anulacion-compras.md` | `AnulacionCompra` |
| B17 | `registro-de-compras-y-ventas__registro-de-compras-serv__recepcion-paquete-compras.md` | `recepcionPaqueteCompras` |
| B18 | `registro-de-compras-y-ventas__registro-de-compras-serv__validacion-recepcion-paquete-de-compras.md` | `validacionRecepcionPaqueteCompras` |
| B19 | `registro-de-compras-y-ventas__registro-de-ventas__reintegro.md` | Reintegros |
| B20 | `registro-de-compras-y-ventas__descarga-de-formatos.md` | Enlaces a las plantillas |
| B21 | `registro-de-compras-y-ventas__registro-de-ventas__registro-prevaloradas.md`, `...__prevaloradas-telecomunicaciones.md`, `...__ventas-de-combustible__ventas-combustible.md`, `...__ventas-de-combustible__codigos-de-paises.md` | Otros registros de ventas, fuera de alcance |
| X1 | `adjuntos/xml/registroCompra/registroCompra.xsd` + `F0_RegistroCompra` (XML de ejemplo) | XSD del registro de compras |
| X2 | `adjuntos/xml/confirmacionCompra/confirmacionCompra.xsd` + `F0_Confirmacion` | XSD de la confirmación de compras |
| X3 | `adjuntos/xml/PComprasEstandar/PlantillaRegistro_ComprasEstandar.xlsx` | Plantilla de compras |
| X4 | `adjuntos/xml/Pventasestandar/PlantillaRegistro_ventas estandar.xlsx` | Plantilla de ventas |

### Fuentes de apoyo, fuera del bloque

Las leí porque B1 no trae el detalle de la Nota C/D.

| Id | Archivo |
|---|---|
| A1 | `facturacion-en-linea__archivos-xml-xsd-de-facturas-electronicas__nota-credito-debito.md` (tabla de campos del sector 24) |
| A2 | `facturacion-en-linea__archivos-xml-xsd-de-facturas-electronicas__nota-credito-debito-descuento.md` (sector 47) |
| A3 | `facturacion-en-linea__archivos-xml-xsd-de-facturas-electronicas__nota-credito-debito-ice.md` (sector 48) |
| A4 | `facturacion-en-linea__archivos-xml-xsd-de-facturas-electronicas__validaciones-documentos-sector__validaciones.md` |
| A5 | `facturacion-en-linea__archivos-xml-xsd-de-facturas-electronicas__validaciones-documentos-sector__validaciones-cont.md` |
| A6 | `adjuntos/xml/CreditoDebitoXML/notaComputarizadaCreditoDebito.xsd` y `.xml`, más `notaElectronicaCreditoDebito.*` |
| A7 | `adjuntos/xml/CreditoDebitoDescuentoXML/notaComputarizadaCreditoDebitoDescuento.xsd` y `.xml`, más su versión electrónica |
| A8 | `adjuntos/Nota CreditoDebito.pdf` (representación gráfica del sector 24) |
| A9 | `adjuntos/Nota CreditoDebitoDescuento.pdf` (representación gráfica del sector 47) |
| A10 | `facturacion-en-linea__implementacion-servicios-facturacion__nota-credito-debito-comp__recepcion-nota-credito-debito-computarizada.md` |
| A11 | `...__nota-credito-debito-comp__anulacion-nota-credito-debito.md` |
| A12 | `...__nota-credito-debito-comp__reversion-anulacion-documento-ajuste.md` |
| A13 | `...__nota-credito-debito-comp__verifica-estado-nota-fiscal-credito-debito-computarizada.md` |
| A14 | `...__nota-credito-debito-comp__verifica-comunicacion.md` |
| A15 | `facturacion-en-linea__implementacion-servicios-facturacion__codigos-error-siat.md` |
| A16 | `informacion__tipos-facturas.md` |
| A17 | `facturacion-en-linea__algoritmos-utilizados__generacion-cuf.md` |
| A18 | `facturacion-en-linea__algoritmos-utilizados__codigo-respuesta-rapida-qr.md` |
| A19 | `facturacion-en-linea__algoritmos-utilizados__algoritmo-de-redondeo.md` |
| A20 | `facturacion-en-linea__emision-y-envio-de-facturas__anulacion-de-documentos-fiscales.md` |
| A21 | `facturacion-en-linea__emision-y-envio-de-facturas__reversion-anulacion-documentos-fiscales.md` |
| A22 | `adjuntos/CasosDePruebaEmisionIndividual1.xlsx`, `adjuntos/CasosDePruebaAnulacionReversion.xlsx` |
| A23 | `versionamiento__versionamiento-2021.md`, `-2023.md`, `-2024.md` |
| A24 | `informacion__modalidades-facturacion__facturacion-computarizada.md`, `informacion__modalidades-de-facturacion-manual__manual.md`, `...__prevalorada.md` |
| A25 | `adjuntos/xml/TasaCeroXML/facturaComputarizadaTasaCero.xsd` y `.xml`, más `facturacion-en-linea__archivos-xml-xsd-de-facturas-electronicas__factura-tasa-cero.md` |
| A26 | `facturacion-en-linea__implementacion-servicios-facturacion__facturacion-computarizada__recepcion-factura-computarizada.md` |

---

## 1. Resumen ejecutivo

1. La **Nota de Crédito-Débito** es un *documento de ajuste* del crédito y débito fiscal. Se emite por **devolución parcial o total, o por rescisión de contrato**, hasta **18 meses** después de la factura original (A16).
2. En el CUF va `tipoFacturaDocumento = 3` y el documento sector es **24** (A17, A22). Hay variantes: **47** cuando la factura original tuvo *descuento adicional* y **48** cuando tuvo *ICE* (A16).
3. El XML de la nota lleva **las líneas de la factura original** (`codigoDetalleTransaccion = 1`) y **las líneas devueltas** (`codigoDetalleTransaccion = 2`). Tiene como mínimo 2 detalles y como máximo 500 (A6).
4. Fórmulas (A4):
   - `montoTotalOriginal = Σ subTotal(tx=1)`, que debe ser igual a los subtotales de la factura original.
   - `montoTotalDevuelto = Σ subTotal(tx=2) − montoDescuentoCreditoDebito`.
   - `montoEfectivoCreditoDebito = montoTotalDevuelto × 0.13`.
5. La nota se emite en la **modalidad vigente** del contribuyente, según el art. 36 inc. a) de la RND 102100000011. Si la factura original es manual, SFV o de Portal Web, M-INV debe permitir **transcribir a mano** los datos de esa factura (B1).
6. Servicios SOAP de la nota computarizada (A10–A14): `recepcionDocumentoAjuste`, `anulacionDocumentoAjuste`, `reversionAnulacionDocumentoAjuste`, `verificacionEstadoDocumentoAjuste` y `verificarComunicacion`. El nombre y la URL del servicio (WSDL) son **NO DOCUMENTADO** en estas fuentes.
7. **RCV de ventas.** El Registro de Ventas Estándar es para facturas **manuales y SFV**. Las facturas computarizadas en línea se registran en el SIN al enviarlas [DEDUCCIÓN, ver §5.1].
8. **RCV de compras.** M-INV sí debe gestionar las compras. Hay dos caminos:
   - Consultar y **confirmar** las compras que los proveedores reportaron a nuestro NIT (`consultaCompras` → `confirmacionCompras`, que se revierte con `AnulacionCompra`).
   - **Registrar** las compras que el proveedor no declaró (`recepcionPaqueteCompras` → `validacionRecepcionPaqueteCompras`).

   En ambos casos se envían paquetes de hasta **500** XML, empaquetados en TAR, comprimidos con Gzip y firmados con un hash SHA-256. **[corregido por revisión]** El SHA-256 no es una firma: es el hash del `.tar.gz` que se envía en el parámetro `hash`. Estos servicios de compras no llevan firma digital.
9. **Tasa Cero de la Ley 1613** (bienes de capital para agro, industria, **construcción** y minería): se emite **solo desde el módulo del SIAT**, con la actividad 465993. M-INV no la emite, solo la puede registrar (B2).
10. La relación con los formularios 200 (IVA) y 400 (IT) es **NO DOCUMENTADO**. La única mención es el código de error 3003 ("No Tiene Formularios 200 Y 210 Vigentes").

---

## 2. Modalidades de facturación (contraste)

### 2.1 Computarizada en Línea: la modalidad de M-INV

Fuente: A24, `informacion__modalidades-facturacion__facturacion-computarizada.md`.

- Emite Facturas Digitales con **token propio o delegado**, desde un sistema autorizado. Luego las envía al SIN, que las registra y valida.
- Características:
  - Imprimir la factura es **opcional**.
  - Envío **individual** del XML.
  - Envío en **paquete por contingencia**.
  - Envío **masivo** en paquete.
- Flujo:
  1. El sistema, que debe estar autorizado y con **CUIS vigente**, solicita el **CUFD**. El CUFD habilita a emitir durante **24 horas**.
  2. El SIN verifica al emisor y devuelve el código de verificación, el CUFD y la **dirección** de la sucursal o casa matriz.
  3. El sistema arma el XML, obtiene el **hash del archivo** y lo envía.
  4. El SIN valida la cabecera de recepción:
     - a) Si la emisión es individual y la validación es correcta, devuelve el **código de recepción** y el estado queda en *recibido*.
     - b) Si es un paquete de contingencia o masivo y la validación es correcta, devuelve el código de recepción.
     - c) Si hay errores, devuelve una lista de códigos y mensajes.
  5. El emisor imprime la factura solo si el cliente quiere un respaldo.
  6. En paquetes, el SIN valida cada factura por separado. Registra las correctas y rechaza las erróneas. Si el tipo de documento es NIT y el número no es válido, se puede enviar el **código de excepción** para que la factura no sea rechazada.
  7. El **código de recepción** se usa para validar la recepción **solo** en paquetes de contingencia o emisión masiva.
  8. Si el SIN observa algo, se subsana y se reenvía.

### 2.2 Electrónica en Línea (⚠ ELECTRÓNICA)

Fuente: B8. Se diferencia de la computarizada en lo siguiente:

- Usa **firma digital**.
- El XML se envía **firmado digitalmente**, tanto en envío individual como en paquetes.
- El sistema envía al cliente la representación gráfica y el XML por correo u otro medio.
- El paso 7 de la computarizada (usar el código de recepción para validar paquetes) no aparece igual. En su lugar dice: "el SIN retorna los resultados".
- En los XSD electrónicos se agrega `xmlns:ds="http://www.w3.org/2000/09/xmldsig#"`, el `xs:import` de `../SignatureSchema.xsd` y el elemento `<ds:Signature/>` al final del elemento raíz (A6, A7). En la computarizada **no van**.

### 2.3 Portal Web en Línea

Fuente: B9.

- Es la modalidad del propio SIN. Se emite desde su página web con credenciales.
- Se puede usar **excepcionalmente como contingencia** de la Computarizada y la Electrónica en línea. Para eso hay que solicitar la opción "Autorización de Portal Web como Contingencia" y elegir el sistema autorizado al que respalda.
- Luego el contribuyente registra productos y clientes, sincroniza catálogos y emite facturas **o Notas Crédito-Débito**. El SIN genera el XML y lo envía al comprador junto con un PDF, cuya impresión es opcional.

### 2.4 Computarizada SFV, Manual y Prevalorada (modalidades anteriores)

Fuentes: B10, A24.

- **Computarizada SFV**:
  - Es **transitoria** y ya no se asigna a usuarios nuevos.
  - La autorización se pide cada **180 días**.
  - Usa **código de control** y **código QR**.
  - Es **obligatorio registrar las facturas emitidas en el RCV** (Registro de Ventas).
- **Manual**: facturas preimpresas en imprenta autorizada. Registro obligatorio en el RCV.
- **Prevalorada**: sin nombre del comprador, con el precio preimpreso. Registro obligatorio en el RCV.

### 2.5 Cuadro comparativo

| Aspecto | Computarizada en Línea (M-INV) | Electrónica en Línea ⚠ | Portal Web | SFV / Manual |
|---|---|---|---|---|
| Firma digital | No | Sí | No aplica (la hace el SIN) | No |
| CUIS/CUFD | Sí | Sí | No aplica | No (usa dosificación o código de control) |
| Envío al SIN | XML con hash (individual, contingencia o masivo) | XML firmado | Lo genera el SIN | No se envía; se carga en el **RCV** |
| Notas C/D | Desde el propio sistema (§3.2) | Desde el propio sistema | Desde el Portal | Aplicación SIAT o Portal (§3.2) |
| Registro en RCV | **Automático** [DEDUCCIÓN] | Automático | Automático | **Obligatorio y manual** |

---

## 3. Notas de Crédito-Débito

### 3.1 Concepto, cuándo se emiten y plazo

- Es un **documento de ajuste** del crédito y débito fiscal para devoluciones parciales o totales y para **rescisiones de contrato**. Se puede emitir **hasta 18 meses después** de la factura original (A16).
- En el catálogo de documentos sector (A16):
  - **24**: Nota de Crédito-Débito. Tipo: Documento de Ajuste.
  - **47**: Nota Crédito Débito Descuentos. Para facturas "afectadas con un Descuento Adicional".
  - **48**: Nota Crédito Débito ICE. Para facturas emitidas con ICE.
  - **29**: Nota de Conciliación. Solo para energía, telecomunicaciones, agua e hidrocarburos. Fuera de alcance.
- En el CUF (A17), el campo "TIPO FACTURA / DOCUMENTO AJUSTE" vale **3 = Documento de Ajuste** y el sector va con 2 dígitos (`24`).
- Hay un error que controla el plazo: **1053**, "La Actividad De La Nota De Crédito Débito No Se Encuentra Autorizada Para Este Plazo" (A15).
- **Quién toma el crédito y quién el débito: NO DOCUMENTADO.** Las fuentes solo dicen que "ajusta crédito y débito fiscal" y la representación gráfica rotula "MONTO EFECTIVO DÉBITO-CRÉDITO". [RECOMENDACIÓN M-INV] Tratarlo contablemente así, y validarlo con el contador:
  - La nota emitida por M-INV al aceptar una devolución de venta **reduce el débito fiscal** de la empresa.
  - Una nota recibida de un proveedor por una devolución de compra **reduce el crédito fiscal**.
- Las fuentes solo describen devoluciones y rescisiones. Las **notas por aumento de precio: NO DOCUMENTADO.** El XSD solo tiene `montoTotalDevuelto`.

### 3.2 Regla de modalidad y notas sobre facturas de modalidades anteriores

Fuente: B1, texto más imagen.

- Según el art. 36 inc. a) de la **RND 102100000011**, la nota se emite **en la modalidad vigente** del contribuyente y no con otras.
- Si la factura original salió en otra modalidad (manual, computarizada SFV, Portal Web), todo sistema, propio o de proveedor, **debe permitir transcribir a mano los campos de la factura original** al emitir la nota.
- Cuadro de la imagen de B1, decodificado:

| Modalidad anterior (de la factura) | Modalidad actual (del emisor) | Forma de emisión de la nota |
|---|---|---|
| (misma) | Manual / Computarizada SFV | Aplicación SIAT |
| (misma) | Portal Web | Portal Web |
| (misma) | **Computarizada en Línea** | **Sistema propio / Facturador de escritorio** |
| (misma) | Electrónica en Línea | Sistema propio / Facturador de escritorio |
| Manual / Computarizada SFV | Portal Web | Portal Web o Aplicación SIAT |
| Manual / Comp. SFV / Portal Web | **Computarizada en Línea** | **Sistema propio / Facturador de escritorio** |
| Manual / Comp. SFV / Portal Web | Electrónica en Línea | Sistema propio / Facturador de escritorio |

- **Consecuencia para M-INV:** todas las notas se emiten desde M-INV. Hay dos casos:
  - La factura original es de M-INV: los datos se toman de la base de datos.
  - La factura original es manual, SFV o de Portal Web: los datos se **ingresan a mano**, incluidos el número, el código de autorización (en lugar del CUF), la fecha, los montos y las líneas.
- A1 lo confirma: `numeroAutorizacionCuf` es el "Número de Cuf de la factura original **o Código de Autorización** si es una factura manual o computarizada SFV".

### 3.3 Qué documento sector usar

| Situación de la factura original | Sector | Raíz XML (computarizada) |
|---|---|---|
| Compra-venta normal sin descuento adicional | **24** | `notaFiscalComputarizadaCreditoDebito` |
| Tiene `descuentoAdicional > 0` (también Compra Venta Bonificaciones, según A23 2023 v1.0.31) | **47** | `notaComputarizadaCreditoDebitoDescuento` |
| Productos con ICE | **48** | Raíz: NO DOCUMENTADO; el zip `CreditoDebitoIceXML.zip` no está en los adjuntos |

- Cada sector debe estar **habilitado para el NIT**. Si no, el SIN responde el error 940, "El NIT No Tiene Habilitado El Documento Sector" (A15).
- ⚠ Hay ambigüedad: el sector 24 también tiene `montoDescuentoCreditoDebito` ("monto prorrateado del descuento adicional"). Ver "Dudas".

### 3.4 XML de la Nota Crédito-Débito computarizada (sector 24): cabecera

Fuentes: A6 (XSD, `<!-- XSD ver.23/08/2021 -->`) y A1 (descripciones).

- **Raíz:** `notaFiscalComputarizadaCreditoDebito`.
- **Sin targetNamespace.** El ejemplo usa `xsi:noNamespaceSchemaLocation="notaComputarizadaCreditoDebito.xsd"` y `xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"`.
- Estructura: `<cabecera>` seguido de 2 a 500 `<detalle>`.
- El orden es **estricto** (`xs:sequence`).
- Los nulos se envían como `<campo xsi:nil="true"/>`.

| # | Campo | Tipo y restricción XSD | Oblig. (A1) | Nillable (XSD) | Significado y origen del valor |
|---|---|---|---|---|---|
| 1 | `nitEmisor` | integer 1..9999999999999 | Sí | – | NIT del emisor según el Padrón |
| 2 | `razonSocialEmisor` | string 1..200 | Sí | – | Razón social según el Padrón |
| 3 | `municipio` | string 1..25 | Sí | – | Municipio según el Padrón |
| 4 | `telefono` | string 1..25 | No | sí | Teléfono según el Padrón |
| 5 | `numeroNotaCreditoDebito` | integer 1..9999999999 | Sí | – | Número correlativo de la **nota** |
| 6 | `cuf` | string 1..100 | Sí | – | CUF de la nota (§3.10) |
| 7 | `cufd` | string 1..100 | Sí | – | CUFD vigente |
| 8 | `codigoSucursal` | integer 0..9999 | Sí | – | 0 = casa matriz |
| 9 | `direccion` | string 1..500 | Sí | – | Dirección de la sucursal según el Padrón. Debe coincidir con la registrada; si no, error 1007 o advertencia 2014 |
| 10 | `codigoPuntoVenta` | integer 0..9999 | No | sí | Punto de venta |
| 11 | `fechaEmision` | xs:dateTime | Sí | – | Fecha y hora de la **nota** |
| 12 | `nombreRazonSocial` | string 1..500 | No | sí | Cliente, es decir el comprador de la factura original |
| 13 | `codigoTipoDocumentoIdentidad` | integer **1..5** (XSD) | Sí | – | Catálogo de tipo de documento. A1 dice "1 al 9" y el XSD dice 1..5; ver Dudas. 1 = CI |
| 14 | `numeroDocumento` | string 1..20 | Sí | – | Documento del cliente |
| 15 | `complemento` | string 0..5 | No | sí | Complemento de CI del SEGIP. Solo va con CI (error 1011) |
| 16 | `codigoCliente` | string 1..100 | Sí | – | Código interno del cliente en M-INV |
| 17 | `numeroFactura` | integer 1..9999999999 | Sí | – | **Número de la factura original** |
| 18 | `numeroAutorizacionCuf` | string 1..100 | Sí | – | **CUF de la factura original**, o su código de autorización si es manual o SFV |
| 19 | `fechaEmisionFactura` | xs:dateTime | Sí | – | **Fecha y hora de emisión de la factura original** |
| 20 | `montoTotalOriginal` | decimal(17,2) > 0 | Sí | – | "Monto total sujeto a crédito fiscal en la factura original". Se valida contra Σ subTotal(tx=1), ver §3.8 |
| 21 | `montoTotalDevuelto` | decimal(17,2) > 0 | Sí | – | Monto total devuelto |
| 22 | `montoDescuentoCreditoDebito` | decimal(17,2) ≥ 0 | Sí según A1 | **sí** | Parte prorrateada del descuento adicional de la factura original. El ejemplo lo envía `xsi:nil="true"` |
| 23 | `montoEfectivoCreditoDebito` | decimal(17,2) > 0 | Sí | – | 13 % del monto total devuelto |
| 24 | `codigoExcepcion` | integer 0..1 | No | sí | 0 o nulo por defecto; 1 para autorizar un NIT con error |
| 25 | `leyenda` | string 1..200 | Sí | – | Leyenda de la actividad económica, del catálogo de leyendas |
| 26 | `usuario` | string 1..100 | Sí | – | Usuario emisor, en forma descriptiva (por ejemplo `JPEREZ`) |
| 27 | `codigoDocumentoSector` | integer **fixed = 24** | Sí | – | Siempre 24 |

### 3.5 XML de la nota (sector 24): detalle

Se repite de 2 a 500 veces, en este orden (A6, A1):

| # | Campo | Tipo y restricción XSD | Oblig. | Nillable | Significado |
|---|---|---|---|---|---|
| 1 | `actividadEconomica` | string 1..10 | Sí | – | Actividad del Padrón |
| 2 | `codigoProductoSin` | integer 1..99999999 | Sí | – | Código de producto SIN, homologado por sincronización |
| 3 | `codigoProducto` | string 1..50 | Sí | – | Código interno (SKU en M-INV) |
| 4 | `descripcion` | string 1..500 | Sí | – | Descripción |
| 5 | `cantidad` | decimal(25,10) > 0 | Sí | – | En servicios vale 1 |
| 6 | `unidadMedida` | integer 1..200 | Sí | – | Catálogo de unidades de medida |
| 7 | `precioUnitario` | decimal(25,10) > 0 | Sí | – | Precio unitario |
| 8 | `montoDescuento` | decimal(25,10) ≥ 0 | No | sí | Descuento del ítem; nulo si no aplica |
| 9 | `subTotal` | decimal(25,10) **> 0** | Sí | – | `(cantidad × precioUnitario) − montoDescuento` |
| 10 | `codigoDetalleTransaccion` | integer 1..2 | Sí | – | **1 = transacción original**, **2 = monto que se devuelve** |

Reglas de armado que se deducen de A4 y A15:

- Las líneas con `codigoDetalleTransaccion = 1` reproducen el detalle de la factura original. Su suma debe **igualar los subtotales de la factura original**. Si el detalle difiere, el SIN responde el error 1049, "Detalle De La Nota Diferente Al Detalle De La Factura Original".
  - [RECOMENDACIÓN M-INV] Copiar **todas** las líneas de la factura original, con sus mismas cantidades, precios y descuentos.
- Las líneas con `codigoDetalleTransaccion = 2` son los ítems devueltos, con la **cantidad devuelta**.
- El XSD exige **al menos 2 detalles**, es decir, al menos una línea de tipo 1 y una de tipo 2.

### 3.6 Variantes: sector 47 (Descuento) y sector 48 (ICE)

**Sector 47** (A2, A7; el XSD difiere del de sector 24 solo en esto):

- Raíz `notaComputarizadaCreditoDebitoDescuento`. ⚠ ELECTRÓNICA: `notaElectronicaCreditoDebitoDescuento`.
- En la cabecera, **después de `montoTotalOriginal`**, va `descuentoAdicional`: decimal(17,2) ≥ 0, nillable, obligatorio = No. Es el descuento adicional de la factura original.
- `codigoDocumentoSector` es fijo en **47**.
- El detalle empieza con **`nroItem`**: integer 1..9999, obligatorio. Es el número de línea en la factura original, y el mismo número identifica la línea devuelta.
- En el detalle, `subTotal` es **≥ 0**, no > 0. Esto permite ítems bonificados (A23 2023 v1.0.31).
- En el ejemplo, el ítem devuelto (tx=2) repite el `nroItem` de su línea original (tx=1).

**Sector 48** (A3): igual que el 47 (`descuentoAdicional`, `nroItem`), más estos campos por detalle:

- `marcaIce`: 1 = con ICE, 2 = sin ICE.
- `alicuotaIva`, `precioNetoVentaIce`, `alicuotaEspecifica`, `alicuotaPorcentual`, `montoIceEspecifico`, `montoIcePorcentual`, `cantidadIce`.

Para el 48 **no hay XSD en los adjuntos**; los tipos y longitudes son NO DOCUMENTADO. Queda fuera de alcance salvo que la empresa venda productos con ICE.

### 3.7 ⚠ ELECTRÓNICA: diferencias del XML

- Raíz `notaFiscalElectronicaCreditoDebito`.
- Se agrega `xmlns:ds` más `xs:import namespace="http://www.w3.org/2000/09/xmldsig#" schemaLocation="../SignatureSchema.xsd"`.
- Se agrega `<xs:element ref="ds:Signature"/>` como último hijo de la raíz.
- El resto es idéntico (A6, verificado con `diff`).

### 3.8 Fórmulas, validaciones y redondeo

**Sector 24** (A4, bloque "NOTA CREDITO DEBITO"):

```
subTotal (cada línea)       = (cantidad × precioUnitario) − montoDescuento
montoTotalOriginal          = Σ subTotal  de las líneas con codigoDetalleTransaccion = 1
                              (debe igualar los subtotales de la factura original)
montoTotalDevuelto          = Σ subTotal  de las líneas con codigoDetalleTransaccion = 2
                              − montoDescuentoCreditoDebito
montoEfectivoCreditoDebito  = montoTotalDevuelto × 0.13
```

**Sector 47** (A5, bloque "NOTA CRÉDITO DÉBITO DESCUENTO"):

```
montoTotalOriginal          = Σ subTotal(tx=1)                         (subtotales antes del descuento adicional)
descuentoItem (por línea tx=2) = (((subTotal × descuentoAdicional) / montoTotalOriginal) / cantidadOriginal) × cantidadDevuelta
montoDescuentoCreditoDebito = Σ descuentoItem
subTotalDevuelto            = (cantidad × precioUnitario) − montoDescuento   (línea tx=2)
montoTotalDevuelto          = Σ subTotalDevuelto − montoDescuentoCreditoDebito
montoEfectivoCreditoDebito  = montoTotalDevuelto × 0.13
```

En `descuentoItem`, el `subTotal` es el de la **línea original**. Así se deduce del ejemplo de §3.9.

**Sector 48** (A5): igual que el 47, con una diferencia en el descuento por ítem:

```
descuentoItem = ((((subTotal − Ice% − IceEsp) × descuentoAdicional) / montoTotalOriginalSinIce) / cantidadOriginal) × cantidadDevuelta
```

Además, el subtotal lleva el ICE sumado: `subTotal = (cantidad × precioUnitario − montoDescuento) + montoIcePorcentual + montoIceEspecifico`.

**Redondeo** (A1, A2, A3 y A19):

- Método **HALF-UP** con **2 decimales** para facturas en línea.
- Si la nota es sobre una factura **emitida en SFV**, el **detalle** se redondea a **5 decimales**. Sobre una factura en línea se usan 2 decimales, salvo en exportación, libre consignación y otros sectores especiales.

**Validaciones del SIN relacionadas** (A15):

| Código | Significado |
|---|---|
| 1029 | Monto total devuelto erróneo |
| 1030 | Monto total original erróneo |
| 1031 | Monto efectivo de crédito o débito erróneo |
| 1033 | El monto devuelto es mayor al monto original |
| 1048 | No se encontró la factura de la nota |
| 1049 | El detalle de la nota difiere del de la factura original |
| 1054 | Monto descuento crédito débito erróneo |
| 1061 | La factura de la nota no es válida para hacer la devolución |
| 1018 / 2012 | Subtotal erróneo |
| 1005 / 2013 | La nota no puede emitirse al mismo emisor |
| 1053 | Plazo |
| 1016 / 1017 | Actividad o producto no habilitado |

[RECOMENDACIÓN M-INV] Validar localmente **antes de enviar**:

- Que las fórmulas cuadren.
- Que la cantidad devuelta por ítem no supere lo vendido **menos lo ya devuelto en notas anteriores**. Si el SIN controla el acumulado entre varias notas es NO DOCUMENTADO.
- Que la fecha esté dentro de los 18 meses.
- Que la factura original no esté anulada.
- Que el total devuelto no supere `montoTotalOriginal`.

### 3.9 Ejemplos numéricos

**Ejemplo 1: XML oficial del sector 24** (A6, `notaComputarizadaCreditoDebito.xml`).

- Línea tx=1: "Amortiguadores", 1 × 775.00, subtotal 775.00. Da `montoTotalOriginal = 775.00`.
- Línea tx=2: "Tornillos", 1 × 75.00, subtotal 75.00. Da `montoTotalDevuelto = 75.00`, con `montoDescuentoCreditoDebito` nulo.
- `montoEfectivoCreditoDebito = 75.00 × 0.13 = 9.75`.
- ⚠ El ejemplo es incoherente: devuelve un ítem que no estaba en la original, y `fechaEmisionFactura` es `3919-02-01…`. Sirve solo como plantilla de estructura.

**Ejemplo 2: PDF oficial del sector 24** (A8).

- Original: Amortiguadores 1 × 775.00 = 775.00.
- Devolución total del mismo ítem: 775.00.
- **MONTO EFECTIVO DÉBITO-CRÉDITO = 775.00 × 0.13 = 100.75.**

**Ejemplo 3: sector 47 con descuento adicional** (A7 y A9).

- Factura original: Amortiguadores 775.00 y Tornillos 75.00. `montoTotalOriginal = 850.00`, `descuentoAdicional = 50.00`, y la factura cobró 800.00.
- Se devuelven los Tornillos: 1 de 1.
- Cálculo:
  - `descuentoItem = ((75.00 × 50.00) / 850.00) / 1 × 1 = 4.4117…`, que redondea a **4.41**.
  - `montoTotalDevuelto = 75.00 − 4.41 =` **70.59**. El literal impreso es "Setenta 59/100 Bolivianos".
  - `montoEfectivoCreditoDebito = 70.59 × 0.13 = 9.1767`, que redondea a **9.18**.

**Ejemplo 4: devolución parcial de cantidad** (ejemplo propio de M-INV, no oficial).

- Factura original n.º 1520: línea A, 10 bolsas de cemento × 62.00 = 620.00; línea B, 5 varillas × 45.50 = 227.50. Da `montoTotalOriginal = 847.50`.
- El cliente devuelve 3 bolsas. La nota lleva:
  - Línea A (tx=1): 10 × 62.00 = 620.00.
  - Línea B (tx=1): 5 × 45.50 = 227.50.
  - Línea A (tx=2): 3 × 62.00 = 186.00.
- Resultado: `montoTotalDevuelto = 186.00` y `montoEfectivoCreditoDebito = 24.18`.
- Si la línea A original tenía `montoDescuento = 20.00`, la línea tx=2 lleva un descuento prorrateado de 20.00 / 10 × 3 = 6.00, con subtotal 180.00 y efectivo 23.40. El prorrateo del descuento **por ítem** es NO DOCUMENTADO; es una [RECOMENDACIÓN M-INV] por analogía con la fórmula del 47.

### 3.10 CUF, numeración y QR de la nota

- **CUF** (A17). Algoritmo general, a detallar en la spec de CUF:
  - NIT (13) + fecha y hora `yyyyMMddHHmmssSSS` (17) + sucursal (4) + modalidad (1) = **2** + tipo de emisión (1) = **1** (online) + tipo factura/documento de ajuste (1) = **3** + documento sector (2) = **24** + número (10) + punto de venta (4).
  - A esa cadena se le agrega el dígito Módulo 11, se pasa a Base 16 y se concatena el código de control del CUFD.
  - En el número va el **`numeroNotaCreditoDebito`** [DEDUCCIÓN: el CUF identifica la nota y el QR usa "el número correlativo de la Factura o Nota" (A18)].
- **Numeración.** La nota tiene su **propio correlativo** (`numeroNotaCreditoDebito`), separado del de las facturas.
  - Si el correlativo va por sucursal, por punto de venta o por NIT es NO DOCUMENTADO en este bloque. [RECOMENDACIÓN M-INV] Usar una serie por sucursal y punto de venta, igual que las facturas.
- **QR** (A18). Formato: `…/consulta/QR?nit=<NIT emisor>&cuf=<CUF de la nota>&numero=<n.º de nota>&t=<1 rollo | 2 media hoja>`.
  - La URL de piloto es `https://pilotosiat.impuestos.gob.bo/consulta/QR`. La de producción se informa al terminar la autorización.
  - El tamaño mínimo recomendado es 3 × 3 cm.

### 3.11 Servicios SOAP de la nota computarizada (Documentos de Ajuste)

- Son los mismos servicios para Notas C/D y Notas de Conciliación, y para las modalidades electrónica y computarizada.
- El **nombre del servicio o WSDL y su URL: NO DOCUMENTADO** en estas páginas; ver la spec de endpoints.
- El formato de `archivo` (compresión y codificación) no se detalla aquí. Ver `facturacion-en-linea__emision-y-envio-de-facturas__emision-y-envio.md` y `facturacion-en-linea__algoritmos-utilizados__comprimir-gzip.md` (fuera del bloque).

**`recepcionDocumentoAjuste`** (A10). Objeto `SolicitudServicioRecepcionDocumentoAjuste`. Recibe **un documento a la vez**.

| Entrada | Tipo | Oblig. | Valor para M-INV |
|---|---|---|---|
| `codigoAmbiente` | Num | Sí | 1 = producción; 2 = pruebas y piloto |
| `codigoPuntoVenta` | Num | No | 0 si no hay punto de venta |
| `codigoSistema` | Alfa | Sí | Código de sistema asignado |
| `codigoSucursal` | Num | Sí | 0 = casa matriz |
| `nit` | Num | Sí | NIT del emisor |
| `codigoDocumentoSector` | Num | Sí | **24** (o 47 / 48) |
| `codigoEmision` | Num | Sí | **1** (online; es el único valor permitido) |
| `codigoModalidad` | Num | Sí | **2** (computarizada). ⚠ 1 = electrónica |
| `cufd` | Alfa | Sí | CUFD vigente |
| `cuis` | Alfa | Sí | CUIS de la sucursal o punto de venta |
| `tipoFacturaDocumento` | Num | Sí | **3** (documento de ajuste) |
| `archivo` | Alfa | Sí | La nota (XML). **[corregido por revisión]** No es el XML plano: es el **XML de la nota comprimido en GZIP** (mismo procedimiento de envío individual de `emision-y-envio.md`: validar XSD → GZIP → SHA-256). La codificación de transporte (Base64 / `byte[]`) la resuelve el WSDL |
| `fechaEnvio` | TimeStamp | Sí | Fecha y hora de envío |
| `hashArchivo` | Alfa | Sí | SHA-256 de la cadena `archivo`. **[corregido por revisión]** = SHA-256 de los **bytes GZIP**, en hexadecimal minúscula (64 caracteres) |

- Salidas: `codigoEstado` (Num), `codigoRecepcion` (Alfa), `codigosRespuestas` (DTO[codigosRespuesta]; la página lo escribe mal como "todigosRespuestas") y `transaccion` (Boolean).
- Resultado esperado en el caso de prueba oficial: **908 = RECEPCIÓN VALIDADA** (A22, filas 48 y 49 de `CasosDePruebaEmisionIndividual1.xlsx`, que son los NRO 47 y 48, con sector 24, tipo 3 y punto de venta 1 o 0).

**`anulacionDocumentoAjuste`** (A11). Objeto `SolicitudServicioAnulacionDocumentoAjuste`.

- Entradas: las mismas de contexto (`codigoAmbiente`, `codigoPuntoVenta`, `codigoSistema`, `codigoSucursal`, `nit`, `codigoDocumentoSector`, `codigoEmision=1`, `codigoModalidad=2`, `cufd`, `cuis`, `tipoFacturaDocumento=3`), más estas dos:
  - `codigoMotivo` (Num, obligatorio): catálogo "Motivos Anulación", que se obtiene por sincronización.
  - `cuf` (Alfa, obligatorio): CUF de **la nota** a anular.
- Salidas: `codigosRespuesta` (DTO), `codigoEstado` y `transaccion`.
- Esperado: **905 = ANULACIÓN CONFIRMADA** (A22, `CasosDePruebaAnulacionReversion.xlsx` filas 48 y 49, con motivo "NOTA DE CREDITO-DEBITO MAL EMITIDA").
- Otros códigos:
  - 906: anulación rechazada.
  - 934: la solicitud está fuera de plazo.
  - 936: ya estaba anulada.
  - 941: no disponible para anular.
  - 925: motivo inválido.
  - 924 / 946: no existe.

**`reversionAnulacionDocumentoAjuste`** (A12). Objeto `SolicitudServicioReversionAnulacionDocumentoAjuste`.

- Entradas: las mismas de contexto (`codigoModalidad = 2`) más `cuf`.
- Salidas: `codigoEstado`, `codigosRespuesta`, `transaccion` y `descripcion`.
- Revierte la anulación **una sola vez**, según la RND 102300000034. Un documento revertido **no puede volver a anularse**.
- Para usarlo, el sistema debe completar sus pruebas en piloto y pulsar "finalizar pruebas".
- Códigos:
  - 907: reversión de anulación confirmada.
  - 909: reversión rechazada.
  - 968: la anulación ya estaba revertida.
  - 978: reversión de la factura o nota C/D confirmada.
  - A21 menciona además 981 (no disponible para reversión), 924, **3011** (el sistema no superó las pruebas de reversión) y **3012** (fuera de plazo). 3011 y 3012 **no figuran** en la tabla de A15, y ahí el 981 tiene otro significado; ver Dudas.

**`verificacionEstadoDocumentoAjuste`** (A13). Objeto `SolicitudServicioVerificacionEstadoDocumentoAjuste`.

- Entradas: las mismas de contexto más `cuf`.
- Salidas: `codigoEstado`, `codigoRecepcion`, `codigosRespuestas` y `transaccion`.
- Si todo es correcto devuelve un código de aceptación; si no, uno de observación con la lista de errores. Los estados posibles son 901–908 (A15).
  **[corregido por revisión]** A15 (`codigos-error-siat.md`) lista los estados 901–909 en general, pero **ninguna página dice qué `codigoEstado` devuelve la verificación de estado** para un documento válido, anulado o revertido: es NO DOCUMENTADO (coincide con `02-…` §6.8 y §15.11 y `03-…` §10). M-INV debe guardar el código tal como llega y confirmar en piloto el valor para "válida" (probablemente 908), "anulada" (probablemente 905) y "revertida".

**`verificarComunicacion`** (A14). No tiene entrada. Devuelve **926 = Comunicación Exitosa**.

### 3.12 Plazos de anulación y reversión

Fuentes: A20, A21. Las páginas hablan de "documentos fiscales" en general, así que aplica también a notas [DEDUCCIÓN].

- **Anulación:**
  - Es individual, por servicio, **hasta el día 9 del mes siguiente** a la emisión.
  - Requisitos: el documento debe estar válido en la base del SIN y **no haberse usado en una declaración jurada**.
  - Se puede anular desde la sucursal de origen u otra habilitada.
  - Hay que **notificar al comprador** por correo u otro medio privado. La notificación incluye como mínimo el código de autorización, el número y el motivo.
- **Reversión de la anulación:**
  - Una sola vez, hasta el mismo día 9 del mes siguiente a la emisión de la factura original.
  - También hay que notificar al comprador.
  - Tras revertir, el documento ya no se puede anular.

### 3.13 Representación gráfica de la nota

Fuentes: A8, A9.

- **Encabezado izquierdo:** razón social del emisor, "CASA MATRIZ" o la sucursal, "No. Punto de Venta N", dirección, teléfono y municipio.
- **Encabezado derecho:** NIT, **Nota N°** y **CÓD. AUTORIZACIÓN**, que es el CUF partido en varias líneas.
- **Título:** "NOTA CRÉDITO - DÉBITO".
- **Datos del cliente y de la factura original:**
  - Fecha (de la nota, `dd/MM/yyyy hh:mm a. m./p. m.`).
  - Nombre/Razón Social.
  - N° Factura (la original).
  - NIT/CI/CEX.
  - Cod. Cliente.
  - Fecha Factura (la original).
  - N° Autorización/CUF (el de la original).
- **Sección "DATOS FACTURA ORIGINAL":**
  - Tabla con Código producto, Cantidad, Unidad de medida (su descripción, por ejemplo "OTRO"), Descripción, Precio unitario, Descuento y Subtotal.
  - Total: **MONTO TOTAL ORIGINAL Bs**.
  - En el sector 47 se agregan **DESCUENTO ADICIONAL** y **MONTO TOTAL A PAGAR Bs**.
- **Sección "DATOS DE LA DEVOLUCIÓN O RESCISIÓN":**
  - Las mismas columnas.
  - Totales: **MONTO TOTAL DEVUELTO Bs** y **MONTO EFECTIVO DÉBITO-CRÉDITO Bs**.
  - En el sector 47 los totales son: SUB TOTAL, MONTO DESCUENTO DEBITO CREDITO, MONTO TOTAL DEVUELTO y MONTO EFECTIVO DÉBITO-CRÉDITO.
- **"Son: … Bolivianos"**: el literal del **monto total devuelto**, con los centavos en la forma `xx/100`.
- **Pie:**
  - La leyenda fija de "contribuye al desarrollo del país / uso ilícito sancionado".
  - La leyenda de la actividad (Ley 453).
  - La leyenda de "Representación Gráfica de un Documento Fiscal Digital emitido en una modalidad de facturación en línea".
  - El **QR**.
  - El rótulo inferior "(NOTA CRÉDITO DÉBITO)" o "(NOTA CRÉDITO DÉBITO DESCUENTO)".
  - El texto exacto de las leyendas está en A8 y A9 y se comparte con la spec de representación gráfica.

### 3.14 Relación con el inventario de M-INV

La documentación del SIN **no dice nada del inventario**. Todo lo que sigue es [RECOMENDACIÓN M-INV].

1. **Devolución de venta** (cliente → empresa). Hoy no existe en el dominio: hay `Sales/Invoice` pero no `SalesReturn`.
   - Crear la entidad `SalesReturn` (y sus líneas), ligada a la `Invoice` original y a la `CreditDebitNote` que se emite.
   - Al confirmar la devolución:
     - a) Se registra un `StockMovement` de **entrada** con un tipo de movimiento nuevo, "Devolución de venta", hacia el almacén y la ubicación que se elijan. Hay que indicar el estado de la mercadería (apta, dañada o cuarentena). Si el producto maneja lote o serie, se reincorpora el mismo lote o serie; en `SerialNumberStatus` ya existe `Returned`.
     - b) Se emite la Nota C/D (sector 24 o 47).
     - c) Se genera la devolución de dinero o el saldo a favor del cliente.
     - d) Se genera el asiento contable. El costo de reingreso será el costo promedio vigente o el costo de la venta original; hay que decidirlo.
   - Si el SIN **rechaza** la nota, el movimiento de stock debe quedar pendiente o revertirse. [RECOMENDACIÓN] Mover el stock solo cuando la nota llegue a 908, o usar un estado "pendiente de nota".
   - La **rescisión** sin retorno físico (un servicio) genera nota sin movimiento de stock.
2. **Devolución de compra** (empresa → proveedor). Ya existe `Purchasing/PurchaseReturn`.
   - El proveedor emite la Nota C/D. M-INV debe **registrar la nota recibida** con estos datos: NIT del proveedor, número de nota, CUF de la nota, número y CUF de la factura original, fecha, monto devuelto y monto efectivo C/D.
   - La nota se vincula a la `PurchaseReturn` y a la `SupplierInvoice`.
   - El **tratamiento en el RCV** de las notas recibidas es NO DOCUMENTADO en el bloque; ver Dudas.
3. **Anulación de la nota** (dentro del plazo). Revierte el movimiento de stock de la devolución y el asiento contable.

---

## 4. Tasa Cero

### 4.1 Ley N° 1613: bienes de capital (B2; A16 sector 33)

- Es el art. 8 de la Ley 1613 del Presupuesto General del Estado **para la gestión 2025**, publicada el 01-01-2025.
- Da incentivo a la importación y comercialización de **bienes de capital y plantas industriales** para los sectores agropecuario, industrial, **de construcción** y minería.
- Tiene un listado de **códigos NANDINA** (`/images/archivos_tecnicos/archivos_apoyo/ADS5302.pdf`, que no está en los adjuntos).
- **Emisión:** solo en el **módulo del SIAT** (`https://siat.impuestos.gob.bo/v2/launcher`), con las credenciales del SIAT en Línea. El camino es opción "Emisión Factura Tasa Cero Ley 1613 Bienes Capital" → "Emitir Factura Bienes Capital".
- Requiere estar registrado con la **actividad económica 465993**. El SIAT ofrece hacer el registro y agregar la actividad.
- En A16 es el documento sector **33**, "Factura Tasa Cero IVA Ley N° 1613", **sin derecho a crédito fiscal**.
- **Implicaciones para M-INV:**
  - No se emite por servicio web desde un sistema propio, al menos según la documentación.
  - [RECOMENDACIÓN] Permitir registrar en M-INV una **"venta facturada externamente (SIAT Tasa Cero)"**: número, CUF, fecha y monto. Así se descarga el inventario y se contabiliza sin emitir factura desde M-INV.
  - Al **comprar** con una factura Tasa Cero, el importe va al 100 % en la columna "compras gravadas a Tasa Cero" y no genera crédito fiscal (§5.3).
- Vigencia después de 2025: NO DOCUMENTADO. A23 2024 v1.0.40 menciona una "Tasa Cero IVA Ley N° 1546" anterior, lo que sugiere que se renueva cada año con la ley de presupuesto.

### 4.2 Tasa Cero, sector 8: libros y transporte internacional de carga (A25, contraste)

- Es para libros y para transporte internacional de carga por carretera.
- Raíz `facturaComputarizadaTasaCero`.
- `montoTotalSujetoIva` es **fijo en 0** en el XSD.
- `codigoDocumentoSector` es fijo en **8**.
- Probablemente no aplica a M-INV.

---

## 5. Registro de Compras y Ventas (RCV)

### 5.1 Panorama y qué le toca a M-INV

- El **Registro de Ventas Estándar** (B11) sirve para facturas **manuales y computarizadas SFV** que no sean de combustible, prevaloradas, telecomunicaciones ni reintegros.
- En su columna 5, el código de autorización admite **15 caracteres**, así que no cabe un CUF.
- Las facturas **computarizadas en línea** "se envían, registran y validan en los servidores del SIN" (A24).
- [DEDUCCIÓN] De lo anterior sale que M-INV **no necesita cargar sus ventas en línea al RCV**. En cambio, sí debe **generar un Libro de Ventas interno** con la misma estructura para conciliar.
- El registro de **compras** sí es tarea de M-INV (§5.3 y §5.7).
- Códigos "ESPECIFICACIÓN", que van en la columna 2 **solo cuando se importa un archivo** (B11, B12, B19, B21):

| Especificación | Registro |
|---|---|
| 1 | Compras estándar |
| 2 | Ventas estándar |
| 3 | Venta de combustible |
| 4 | Prevaloradas preimpresas |
| 5 | Prevaloradas de telecomunicaciones |
| 6 | Reintegros |

- En todas las plantillas los miles se separan con "coma" y los decimales con "punto".

### 5.2 Registro de Ventas Estándar: 24 columnas

Fuentes: B11, X4.

| Col | Nombre | Tipo | Long. | Se registra | Regla |
|---|---|---|---|---|---|
| 1 | N° | entero | 8 | Sí | Correlativo de la fila |
| 2 | ESPECIFICACION | entero | 1 | No | Valor 2. Solo va, en la 2.ª posición, si se importa |
| 3 | FECHA DE LA FACTURA | fecha DD/MM/AAAA | 10 | Sí | Emisión |
| 4 | N° DE LA FACTURA | entero | 15 | Sí | |
| 5 | CÓDIGO DE AUTORIZACIÓN | alfanumérico | 15 | Sí | Distinto de 0 |
| 6 | NIT / CI CLIENTE | alfanumérico | 15 | Sí | 0 si corresponde. Especiales: **99001** consulados y embajadas; **99002** Control Tributario; **99003** ventas menores del día |
| 7 | COMPLEMENTO | alfanumérico | 5 | Sí | En blanco si no hay |
| 8 | NOMBRE O RAZÓN SOCIAL | alfanumérico | 240 | Sí | "Sin Nombre" o "S/N"; "Control Tributario" / "Ventas menores del día" según el caso |
| 9 | IMPORTE TOTAL DE LA VENTA | 14.2 | | Sí | Sin descontar ICE, IEHD, IPJ, tasas, no sujetos, exentos, tasa cero, descuentos ni gift card |
| 10 | IMPORTE ICE | 14.2 | | Sí | 0 si no hay |
| 11 | IMPORTE IEHD | 14.2 | | Sí | 0 si no hay |
| 12 | IMPORTE IPJ | 14.2 | | Sí | 0 si no hay |
| 13 | TASAS | 14.2 | | Sí | 0 si no hay |
| 14 | OTROS NO SUJETOS AL IVA | 14.2 | | Sí | 0 si no hay |
| 15 | EXPORTACIONES Y OPERACIONES EXENTAS | 14.2 | | Sí | Turismo receptivo, zonas francas, Ley 2206, seguridad alimentaria, fideicomisos… |
| 16 | VENTAS GRAVADAS A TASA CERO | 14.2 | | Sí | Libros, transporte internacional de carga… |
| 17 | SUBTOTAL | 14.2 | | Sí | `= 9 − 10 − 11 − 12 − 13 − 14 − 15 − 16` |
| 18 | DESCUENTOS, BONIFICACIONES Y REBAJAS SUJETAS AL IVA | 14.2 | | Sí | Otorgados |
| 19 | IMPORTE GIFT CARD | 14.2 | | Sí | |
| 20 | IMPORTE BASE PARA DÉBITO FISCAL | 14.2 | | Sí | `= 17 − 18 − 19` |
| 21 | DÉBITO FISCAL | 14.2 | | Sí | `= 20 × 13 %` |
| 22 | ESTADO | carácter | 1 | Sí | **A** anulada, **V** válida, **C** emitida en contingencia, **L** libre consignación |
| 23 | CÓDIGO DE CONTROL | alfanumérico | 17 | Sí | Pares hexadecimales separados por "-" (sin la letra "O"). 0 si no hay |
| 24 | TIPO DE VENTA | entero | 1 | Sí | **0** otros; **1** gift card (venta de gift card) |

- La plantilla X4 tiene una sola hoja, "Hoja1":
  - Fila 1: encabezados. Fila 2: descripciones. Fila 3: nota de separadores. **Desde la fila 4**: datos, con N° y ESPECIFICACIÓN = 2 ya puestos.
  - Fórmulas: `Q = I−J−K−L−M−N−O−P`, `T = Q−R−S`, `U = T*0.13`.
  - Formatos: fecha `dd/mm/yyyy`, importes `0.00`.
- Ejemplo de ventas (propio): total 1 000.00, descuento 50.00, sin otros conceptos. SUBTOTAL 1 000.00, BASE 950.00, **DÉBITO FISCAL 123.50**.

### 5.3 Registro de Compras: 23 columnas

Fuentes: B12, X3.

| Col | Nombre | Tipo | Long. | Se registra | Regla |
|---|---|---|---|---|---|
| 1 | N° | entero | 6 | Sí | Correlativo |
| 2 | ESPECIFICACIÓN | entero | 1 | No | Valor 1. Solo va si se importa |
| 3 | NIT PROVEEDOR | entero | 12 | Sí | Para DUI/DIM: NIT de la agencia despachante, o el propio si la empresa importa directamente |
| 4 | RAZÓN SOCIAL PROVEEDOR | alfanumérico | 240 | Sí | Para DUI/DIM: la agencia, o la propia empresa |
| 5 | CÓDIGO DE AUTORIZACIÓN | alfanumérico | **100** | Sí | Distinto de 0. Aquí **cabe el CUF**. Excepciones: **1** = boleto aéreo; **3** = DUI/DIM |
| 6 | NÚMERO FACTURA | entero | 20 | Sí | Boleto aéreo: el e-ticket sin guiones. **DUI/DIM: 0** |
| 7 | NÚMERO DUI/DIM | alfanumérico | 15 | Sí | Solo en importación. DUI `AAAADDDCNNNNNNNN` (ej. `2020211C30695`); DIM `AAAADDDNNNNNNNN` (ej. `20215212014770`). **0** si es factura |
| 8 | FECHA DE FACTURA/DUI/DIM | DD/MM/AAAA | 10 | Sí | Para DUI/DIM: la fecha de validación o aceptación de Aduana |
| 9 | IMPORTE TOTAL COMPRA | 14.2 | | Sí | Bruto, sin deducir nada |
| 10 | IMPORTE ICE | 14.2 | | Sí | |
| 11 | IMPORTE IEHD | 14.2 | | Sí | |
| 12 | IMPORTE IPJ | 14.2 | | Sí | |
| 13 | TASAS | 14.2 | | Sí | Tasas no sujetas al IVA |
| 14 | OTRO NO SUJETO A CRÉDITO FISCAL | 14.2 | | Sí | Ej.: para gasolina o diésel comprados en estación de servicio, va el **30 %** del total |
| 15 | IMPORTES EXENTOS | 14.2 | | Sí | |
| 16 | IMPORTE COMPRAS GRAVADAS A TASA CERO | 14.2 | | Sí | Va el **100 %** del total |
| 17 | SUBTOTAL | 14.2 | | Sí | `= 9 − 10 − 11 − 12 − 13 − 14 − 15 − 16` |
| 18 | DESCUENTOS/BONIFICACIONES/REBAJAS SUJETAS AL IVA | 14.2 | | Sí | Obtenidos |
| 19 | IMPORTE GIFT CARD | 14.2 | | Sí | |
| 20 | IMPORTE BASE CF | 14.2 | | Sí | `= 17 − 18 − 19` |
| 21 | CRÉDITO FISCAL | 14.2 | | Sí | `= 20 × 13 %` |
| 22 | TIPO COMPRA | entero | 1 | Sí | 1 = mercado interno con destino a actividades gravadas; 2 = mercado interno con destino a actividades no gravadas; 3 = sujetas a proporcionalidad; 4 = para exportaciones; 5 = mercado interno y exportaciones |
| 23 | CÓDIGO DE CONTROL | alfanumérico | 17 | Sí | Igual que en ventas; 0 si no hay |

Ejemplos:

- **Oficiales** (B12):
  - Luz: servicio 100.00 + tasa de alumbrado 20.00 + tasa de aseo 10.00. Total 130.00; tasas 30.00; **subtotal 100.00**; CF 13.00.
  - Cerveza: 80.00 + ICE 10.00. Total 90.00; ICE 10.00; **subtotal 80.00**; CF 10.40.
- **Propio**, sobre la regla del 30 % para combustible: diésel por 200.00. Otro no sujeto 60.00; subtotal 140.00; CF 18.20.
- La plantilla X3 ("Hoja1") tiene la misma disposición que la de ventas: datos desde la fila 4, ESPECIFICACIÓN = 1 y las mismas fórmulas en Q, T y U.

### 5.4 Reintegros: 7 columnas (B19)

- Se usan cuando en el período hubo bienes u obras gravados **destinados a donaciones o entregas a título gratuito** (art. 8 del DS 21530). El crédito fiscal que generaron se debe **reintegrar**.

| Col | Nombre | Tipo | Long. | Regla |
|---|---|---|---|---|
| 1 | N° | entero | 6 | Correlativo |
| 2 | ESPECIFICACIÓN | entero | 1 | Valor 6. Solo si se importa |
| 3 | FECHA DE REINTEGRO | DD/MM/AAAA | 10 | |
| 4 | IMPORTE TOTAL DEL REINTEGRO | 14.2 | | Total donado o entregado |
| 5 | DÉBITO FISCAL | 14.2 | | `= 4 × 13 %` |
| 6 | NIT / CI BENEFICIARIO | alfanumérico | 15 | Si no hay: personería jurídica o documento equivalente |
| 7 | COMPLEMENTO | alfanumérico | 5 | |

- Ejemplo propio: una donación de materiales por 500.00 da un débito fiscal de 65.00.
- **Relevante para inventario:** una salida de stock por "donación / entrega gratuita" alimenta este registro.

### 5.5 Otros registros de ventas: fuera de alcance (B21)

| Registro | Especif. | Resumen |
|---|---|---|
| Venta de combustible | 3 | 19 columnas. Incluye placa o B-SISA, país de la placa (catálogo de códigos de países: 1 Afganistán … 22 Bolivia …), tipo de envase (B / T / O), tipo de producto (1–7), autorización de venta y estado A / V / C |
| Prevaloradas | 4 | Rangos de facturas preimpresas vendidas. Base DF = total − ICE − exentas − tasa cero; DF 13 %; estado A / V |
| Prevaloradas de telecomunicaciones | 5 | Rangos de tarjetas prepago. DF = total × 13 %; columna mayorista (1) o comisionista (2); estado A / V |

### 5.6 Servicio Registro de Compras (SOAP)

Fuentes: B13–B18, X1, X2.

#### 5.6.1 Habilitación (B13)

- El servicio **se agrega** al sistema autorizado desde el ambiente **Piloto**. El camino es: *Autorización de Sistemas* → *Seguimiento de Sistemas* → botón **"Registro de compras"** → **Aceptar**. Con eso se habilitan las pruebas.
- Al superar las pruebas y pulsar **"Finalizar pruebas"**, el servicio queda habilitado en producción.
- Los sistemas que están en etapa inicial o en autorización pueden agregarlo ya.
- Los que están **en inspección** deben terminar ese proceso y pedir el servicio cuando lleguen a producción.
- En los adjuntos **no hay** casos de prueba en .xlsx para compras.

#### 5.6.2 Parámetros comunes de todos los métodos

| Entrada | Tipo | Oblig. | Valor |
|---|---|---|---|
| `codigoAmbiente` | Num | Sí | 1 = producción; 2 = pruebas y piloto |
| `codigoPuntoVenta` | Num | No | 0 si no hay |
| `codigoSistema` | Alfa | Sí | Código del sistema |
| `codigoSucursal` | Num | Sí | 0 = casa matriz |
| `cufd` | Alfa | Sí | CUFD vigente |
| `cuis` | Alfa | Sí | CUIS |
| `nit` | Num | Sí | **NIT del comprador**, es decir el de la empresa que usa M-INV |

- El **nombre del servicio o WSDL y su URL: NO DOCUMENTADO.**
- `codigosRespuestas` es un DTO[codigosRespuesta] cuya estructura no se detalla aquí.

#### 5.6.3 Métodos

| Método (exacto) | Objeto de solicitud | Entradas propias | Salidas | Para qué |
|---|---|---|---|---|
| `consultaCompras` | `SolicitudConsultaCompras` | `Fecha` (Timestamp, obligatorio) | `Archivo` (Alfa), `transaccion` (Bool), `codigosRespuestas` | Devuelve un archivo con **todas las facturas que los emisores reportaron a nuestro NIT** a esa fecha. El formato de `Archivo` es NO DOCUMENTADO |
| `confirmacionCompras` | `SolicitudConfirmacionCompras` | `archivo`, `cantidadFacturas` (Num), `fechaEnvio` (Timestamp), `gestion` (Num), `hash` (SHA-256), `periodo` (Num); todos obligatorios | `codigoEstado`, `codigoRecepcion`, `codigosRespuestas`, `transaccion`, `codigoDescripcion` | **Confirma** las compras reportadas. Los XML se arman con lo que devolvió `consultaCompras` |
| `AnulacionCompra` (con **A mayúscula**) | `SolicitudAnulacionCompra` | `nitProveedor` (Num, Sí), `NroFactura` (Num, Sí), `codigoAutorizacion` (Alfa, No: "de la factura manual"), `nroDuiDIM` (Num, No) | `codigoEstado`, `codigoDescripcion`, `codigosRespuestas`, `transaccion` | **Revierte la confirmación** de una compra ya registrada |
| `recepcionPaqueteCompras` | `SolicitudRecepcionCompras` | `archivo`, `cantidadFacturas`, `fechaEnvio`, `gestion`, `hash`, `periodo`; todos obligatorios | `codigoEstado`, `codigoRecepcion`, `codigosRespuestas`, `transaccion`, `codigoDescripcion` | **Registra en masa** las compras que el vendedor no declaró |
| `validacionRecepcionPaqueteCompras` | `SolicitudValidacionRecepcionCompras` | `codigoRecepcion` (Alfa, Sí) | `codigoEstado`, `codigoRecepcion`, `codigosRespuestas`, `transaccion`, `codigoDescripcion` | Valida el paquete enviado. El texto dice que "devuelve un archivo con las facturas registradas", pero la tabla **no lista** esa salida |

- `gestion` y `periodo` se describen como "gestión / periodo de las facturas enviadas". [DEDUCCIÓN] Gestión es el año (AAAA) y período es el mes (1–12). Los rangos exactos son NO DOCUMENTADO.

#### 5.6.4 Armado del paquete (B15, B17)

1. Generar un XML por factura.
2. **Validarlo contra el XSD.**
3. Guardarlo temporalmente de forma individual.
4. Agrupar **hasta 500** XML en un contenedor **TAR** (`paquete.tar`).
5. Comprimir el TAR con **Gzip** (`paquete.tar.gz`) y enviarlo en `archivo`.
6. Calcular **SHA-256** del archivo **comprimido** y enviarlo en `hash`.
7. `cantidadFacturas` es el número de XML del paquete.

- Cómo se codifica `archivo` en SOAP (base64 u otra) es NO DOCUMENTADO en el bloque; ver la spec de envío.
- Posibles errores: 969 (hash inválido), 985 (la cantidad de facturas difiere de la declarada), 972 / 3009 (el paquete excede el máximo), 971 (tamaño mayor a 100 MB) (A15).

#### 5.6.5 XSD `registroCompra` (X1): para `recepcionPaqueteCompras`

- Raíz **`registroCompra`**.
- Sin namespace destino, `elementFormDefault="qualified"`. Declara `xmlns:ds` pero **no lo usa** (no hay firma).
- Orden estricto:

| # | Elemento | Tipo y restricción | Columna de la plantilla |
|---|---|---|---|
| 1 | `nro` | integer 1..9999 | N° |
| 2 | `nitEmisor` | integer 1..9999999999999 | NIT PROVEEDOR |
| 3 | `razonSocialEmisor` | string 1..240 | RAZÓN SOCIAL |
| 4 | `codigoAutorizacion` | string 1..100 | CÓDIGO DE AUTORIZACIÓN (CUF o código) |
| 5 | `numeroFactura` | integer **1**..99999999999999999999 | NÚMERO FACTURA (ver Dudas: DUI/DIM usa 0) |
| 6 | `numeroDuiDim` | string 1..15 | NÚMERO DUI/DIM (`0` si no hay) |
| 7 | `fechaEmision` | xs:dateTime | FECHA (ej. `2021-05-09T00:00:00`) |
| 8 | `montoTotalCompra` | decimal(14,2) **> 0** | IMPORTE TOTAL COMPRA |
| 9 | `importeIce` | decimal(14,2) ≥ 0 | ICE |
| 10 | `importeIehd` | decimal(14,2) ≥ 0 | IEHD |
| 11 | `importeIpj` | decimal(14,2) ≥ 0 | IPJ |
| 12 | `tasas` | decimal(14,2) ≥ 0 | TASAS |
| 13 | `otroNoSujetoCredito` | decimal(14,2) ≥ 0 | OTRO NO SUJETO A CF |
| 14 | `importesExentos` | decimal(14,2) ≥ 0 | EXENTOS |
| 15 | `importeTasaCero` | decimal(14,2) ≥ 0 | TASA CERO |
| 16 | `subTotal` | decimal(14,2) ≥ 0 | SUBTOTAL |
| 17 | `descuento` | decimal(14,2) ≥ 0 | DESCUENTOS |
| 18 | `montoGiftCard` | decimal(14,2) ≥ 0 | GIFT CARD |
| 19 | `montoTotalSujetoIva` | decimal(14,2) ≥ 0 | IMPORTE BASE CF |
| 20 | `creditoFiscal` | decimal(14,2) ≥ 0 | CRÉDITO FISCAL |
| 21 | `tipoCompra` | integer 1..5 | TIPO COMPRA |
| 22 | `codigoControl` | string 0..17, **nillable** | CÓDIGO DE CONTROL (`0` si no hay) |

Ejemplo oficial (`F0_RegistroCompra`):

- `nro=1`, `nitEmisor=1111111010`, `codigoAutorizacion=1`, `numeroFactura=11110`, `numeroDuiDim=0`, `fechaEmision=2021-05-09T00:00:00`.
- `montoTotalCompra=100`, todos los importes deducibles en 0, `subTotal=100`, `montoTotalSujetoIva=100`, `creditoFiscal=13`.
- `tipoCompra=1`, `codigoControl=0`.

#### 5.6.6 XSD `confirmacionCompra` (X2): para `confirmacionCompras`

- Raíz **`confirmacionCompra`**.
- Orden estricto:

| # | Elemento | Tipo y restricción |
|---|---|---|
| 1 | `nro` | integer 1..9999 |
| 2 | `nitEmisor` | integer 1..9999999999999 |
| 3 | `codigoAutorizacion` | string 1..100 |
| 4 | `numeroFactura` | integer 1..99999999999999999999 |
| 5 | `tipoCompra` | integer **0..9**, no nillable. La plantilla y la página documentan solo 1..5 |

- Ejemplo (`F0_Confirmacion`): `nro=1`, `nitEmisor=154422029`, `codigoAutorizacion=1`, `numeroFactura=11110`, `tipoCompra=1`.

#### 5.6.7 Flujos para M-INV

- **Confirmación mensual de compras.**
  1. Llamar `consultaCompras(Fecha)`.
  2. Mostrar lo que devuelve, cruzado con las `SupplierInvoice` de M-INV por NIT del proveedor, código de autorización o CUF y número.
  3. El usuario marca qué confirma y asigna el `tipoCompra`.
  4. Generar un `confirmacionCompra` por factura y armar paquetes de 500 o menos.
  5. Llamar `confirmacionCompras`.
  6. Guardar el `codigoRecepcion` y el estado.
  - Si hace falta validar después la confirmación con `validacionRecepcionPaqueteCompras` es NO DOCUMENTADO; esa página solo habla de la recepción.
- **Compras no declaradas por el proveedor.**
  1. Tomar las `SupplierInvoice` que faltan en `consultaCompras`.
  2. Generar un `registroCompra` por factura, con los importes calculados según §5.3.
  3. Armar el paquete y llamar `recepcionPaqueteCompras`, que devuelve un `codigoRecepcion`.
  4. Llamar `validacionRecepcionPaqueteCompras(codigoRecepcion)` hasta obtener el estado final (ver la tabla 901–908 de A15).
- **Deshacer una confirmación.** Llamar `AnulacionCompra(nitProveedor, NroFactura[, codigoAutorizacion, nroDuiDIM])`.

### 5.7 Formularios 200 (IVA) y 400 (IT)

- **NO DOCUMENTADO.** Ninguna página del bloque ni de apoyo explica cómo el RCV alimenta el Form. 200 ni el Form. 400, ni menciona "libros" de compras y ventas.
- La única referencia es el código **3003**, "Marca: No Tiene Formularios 200 Y 210 Vigentes" (A15). Es una marca de control que bloquea al NIT.
- Lo documentado es solo el cálculo por fila: **débito fiscal = base × 13 %** en ventas y reintegros, y **crédito fiscal = base × 13 %** en compras.
- También está documentado que **no se puede anular** un documento ya usado en una declaración jurada (A20).
- [RECOMENDACIÓN M-INV] Emitir un **resumen fiscal mensual** que el contador traslada a los formularios:
  - Σ débito fiscal de ventas.
  - Σ efectivo de las notas C/D emitidas y recibidas.
  - Σ crédito fiscal de compras por tipo de compra.
  - Σ reintegros.
  - Ventas brutas y exentas.
  - El mapeo exacto a casillas queda **pendiente de normativa**.

---

## 6. Casos especiales: resumen (probablemente fuera de alcance)

| Caso (fuente) | Qué es | Cómo funciona | ¿Aplica a M-INV? |
|---|---|---|---|
| **Facturación conjunta** (B3) | Dos o más documentos fiscales de **distintos contribuyentes** en una misma representación gráfica, emitidos desde el sistema de uno de ellos por contrato | **Caso 1**, ambos en línea: A asocia a B como proveedor; B crea un **punto de venta de tipo conjunto** y genera un **token delegado**; A emite. **Caso 2**, A en línea y B en SFV, y emite B: A pide una **dosificación especial** y registra esas facturas en el **RCV**; si quien emite está en línea, debe manejar un SFV aparte solo para esto. **Caso 3**, emite A: B pide en el portal su habilitación de "emisión por terceros", asocia el sistema de A en modalidad computarizada y entrega el token; A crea un punto de venta para B y pide el CUFD por B. **Los proveedores de sistemas deben usar la electrónica en línea para sus propias facturas.** ⚠ ELECTRÓNICA: si A es electrónica, debe delegar la firma | No |
| **Facturación por terceros** (B4) | Un tercero autorizado emite por el sujeto pasivo (ejemplo: un banco que factura por un colegio) | **Caso 1**, ambos en línea: el tercero asocia su sistema; el titular acepta, pide un **token** con vigencia y lo entrega; el tercero pide el CUFD por el titular. **Caso 2**, el titular no está en línea: pide la habilitación, asocia el sistema del tercero en modalidad **computarizada** y recibe el token; el tercero crea un punto de venta y pide el CUFD. En el portal: "Generar Nueva Solicitud" → NIT del emisor → "Solicitar" (imprime una constancia) → "Generar TOKEN". El sistema emisor debe ser **de tipo proveedor** | No, salvo que M-INV se venda como sistema "proveedor" |
| **Terceros YPFB** (B5) | Asociación que hacen los funcionarios de YPFB con estaciones de servicio | En el portal: Sistema de Facturación → "Solicitud de Asociación por Terceros" → "…YPFB" → nueva solicitud con NIT, sistema, modalidad, sector, sucursal, correo y punto de venta | No |
| **Comisionistas** (B6) | Venta por cuenta de terceros con comisión (RND 101700000014, modificada por la RND 102100000011, art. 5): el comitente entrega facturas prevaloradas o usa la electrónica o computarizada en línea | **Caso 1**, el comitente presta su sistema: crea un **punto de venta comisionista** con NIT del comisionista, n.º de contrato y fechas de inicio y fin, y le entrega el sistema con CUIS y token; el comisionista pide el CUFD y emite. **Caso 2**, el comisionista usa su propio sistema (autorizado y de tipo proveedor): lo asocia, el comitente acepta, pide el token y envía sucursal, punto de venta y token; el comisionista pide el **CUFD a diario**. ⚠ ELECTRÓNICA: firma del comitente o firma delegada | No (posible a futuro si hay consignación) |
| **SAP Business One** (B7) | Modelo de implementación para ERPs | Las ventas se **replican de inmediato a una estructura específica de facturación**. Una aplicación lee, genera el XML, **valida contra el XSD**, ⚠ firma (electrónica), genera el archivo, calcula el **HASH** y lo envía. Esa aplicación es la que el SIN certifica | Sí, como **patrón de arquitectura**: tener una tabla o bandeja de documentos fiscales separada de la venta |

---

## 7. Qué debe implementar M-INV

Las entidades existentes que se citan son `Sales/Invoice`, `Purchasing/SupplierInvoice`, `Purchasing/PurchaseReturn`, `Inventory/StockMovement` y `MovementType`.

### A. Notas de Crédito-Débito (emisión)

1. **Entidad `CreditDebitNote`** (agregado raíz, con alcance de sucursal). Campos:
   - Tipo de documento sector: 24, 47 o 48.
   - `numeroNotaCreditoDebito`, que es un correlativo propio por sucursal y punto de venta.
   - CUF, CUFD usado, CUIS, fecha de emisión y usuario.
   - Datos del cliente (tipo y número de documento, complemento, código de cliente, razón social).
   - **Referencia a la factura original:**
     - `InvoiceId` si es de M-INV, o bien los datos transcritos si es externa.
     - `numeroFactura`, `numeroAutorizacionCuf` (100), `fechaEmisionFactura`.
     - Modalidad de origen: en línea, SFV, manual o Portal Web. Define el redondeo a 2 o 5 decimales.
   - Montos: `montoTotalOriginal`, `descuentoAdicional` (solo 47), `montoTotalDevuelto`, `montoDescuentoCreditoDebito`, `montoEfectivoCreditoDebito`.
   - `codigoExcepcion` y leyenda.
   - Estado: Borrador / Enviada / Validada (908) / Observada / Rechazada / Anulada / Reversión.
   - Datos de la respuesta: `codigoRecepcion`, `codigoEstado` y la lista de `codigosRespuestas`.
   - XML generado y su hash SHA-256.
2. **Entidad `CreditDebitNoteLine`** con estos campos:
   - `codigoDetalleTransaccion` (1 = original, 2 = devuelto) y `nroItem` (sector 47 y 48).
   - Actividad económica, `codigoProductoSin`, código de producto (SKU), descripción.
   - Cantidad (25,10), unidad de medida SIN, precio unitario (25,10), `montoDescuento` y `subTotal`.
   - En las líneas tipo 2, un enlace a la línea original.
3. **Pantalla "Devolución / Nota C-D":**
   - a) Buscar la factura original por número, CUF, cliente o fecha. También debe existir la opción **"Factura externa / modalidad anterior"**, con **captura manual** de número, código de autorización, fecha y líneas (B1).
   - b) Se precargan todas las líneas originales como tipo 1.
   - c) El usuario marca los ítems y las **cantidades devueltas**, que se convierten en líneas tipo 2. Si es una rescisión, marca el servicio entero.
   - d) Se elige el almacén de reingreso y el estado de la mercadería.
   - e) Se muestra en vivo el `montoTotalOriginal`, el devuelto y el efectivo al 13 %.
4. **Selector automático de sector:**
   - 47 si la factura original tiene `descuentoAdicional > 0`.
   - 48 si tiene ICE.
   - 24 en los demás casos.
   - Se verifica que el sector esté habilitado para el NIT; si no, se muestra el error 940.
5. **Motor de cálculo** según §3.8, con HALF-UP, 2 decimales en cabecera y 2 o 5 decimales en el detalle según la modalidad de origen. Reglas:
   - `montoTotalOriginal = Σ subTotal(tx=1)`, que debe igualar la factura original.
   - `montoTotalDevuelto = Σ subTotal(tx=2) − montoDescuentoCreditoDebito`.
   - `efectivo = devuelto × 0.13`.
   - En el sector 47: `descuentoItem = (((subTotalOrig × descAdic) / montoTotalOriginal) / cantOrig) × cantDevuelta`.
   - [RECOMENDACIÓN] El descuento por ítem de las líneas devueltas se prorratea por cantidad.
6. **Validaciones locales antes de enviar:**
   - Al menos una línea tipo 1 y una tipo 2, y como máximo 500 líneas.
   - La cantidad devuelta acumulada (sumando todas las notas no anuladas de esa factura) no supera la vendida.
   - El devuelto no supera el original (error 1033).
   - La factura original está vigente, es decir no anulada.
   - La fecha de la nota es a lo sumo **18 meses** después de la factura original.
   - El cliente no es el mismo emisor (error 1005).
   - `complemento` solo va con CI.
   - Todos los campos cumplen longitud y tipo del XSD.
7. **Generar el XML** con la raíz `notaFiscalComputarizadaCreditoDebito` (o `notaComputarizadaCreditoDebitoDescuento`):
   - Orden exacto del XSD.
   - `xsi:nil="true"` para los nulos.
   - `codigoDocumentoSector` fijo.
   - **Validación contra una copia local del XSD** (ver. 23/08/2021) antes de enviar.
   - Los XSD se guardan versionados y se pueden actualizar. El SIN avisa que, cuando crecen los catálogos, cada contribuyente debe ampliar él mismo los límites del XSD (A23).
8. **CUF de la nota** con `tipoFacturaDocumento = 3`, documento sector 24, 47 o 48 y el número de nota. Se reutiliza el generador de CUF de las facturas.
9. **Cliente SOAP de Documentos de Ajuste** con los métodos `recepcionDocumentoAjuste`, `anulacionDocumentoAjuste`, `reversionAnulacionDocumentoAjuste`, `verificacionEstadoDocumentoAjuste` y `verificarComunicacion`.
   - Parámetros de §3.11: `codigoModalidad = 2`, `codigoEmision = 1` y `tipoFacturaDocumento = 3`.
   - Se guardan todas las respuestas en un registro de transmisiones.
   - Si falla la comunicación, se reintenta y se consulta el estado por CUF. `verificarComunicacion` debe devolver 926.
10. **Representación gráfica** (PDF o impresión, en rollo o media hoja) según §3.13, con QR `nit/cuf/numero/t` y el literal del monto devuelto. Se envía al cliente por correo.
11. **Anulación de la nota** hasta el **día 9 del mes siguiente** a la emisión:
    - Se elige el motivo del catálogo sincronizado "Motivos Anulación".
    - Al anular se revierte el movimiento de stock y el asiento contable.
    - Se envía una **notificación al comprador** con código de autorización, número y motivo.
    - **Reversión de la anulación**: una sola vez, dentro del mismo plazo, y se bloquea una nueva anulación.
12. **Permisos y roles:**
    - Emitir notas: Administrador y Gerencia, o Ventas con aprobación.
    - Anular: Administrador y Gerencia.
    - Consultar: todos los roles de lectura.
    - Todo se registra en `AuditLog`.

### B. Inventario ligado a devoluciones

13. **Nuevo `MovementType` "Devolución de venta"** (entrada). El `StockMovement` referencia a `SalesReturn` o `CreditDebitNote`. Se aplica cuando la nota llega a 908, o se registra como "pendiente de nota" y se confirma al validarse.
    - Tratamiento de **lote y serie**: la serie vuelve a `Returned` o `Available` según inspección.
    - Opción de reingresar a un almacén o ubicación de **cuarentena o mercadería dañada**.
14. **Devolución de compra** (`PurchaseReturn`). Se registra la **Nota C/D recibida del proveedor**: NIT, n.º de nota, CUF, factura original (n.º y CUF), fecha, monto devuelto y monto efectivo.
    - Se vincula a la `SupplierInvoice` y ajusta el crédito fiscal en el resumen fiscal.
    - La salida de stock ya la hace `PurchaseReturn`.
15. **Salida por donación o entrega gratuita**: un `MovementType` nuevo que alimenta el **Registro de Reintegros** (§5.4) con fecha, importe al costo o valor, débito fiscal del 13 % y NIT/CI del beneficiario.

### C. Registro de Compras (RCV) y datos fiscales de compras

16. **Ampliar `SupplierInvoice`** con los datos fiscales de §5.3:
    - NIT del proveedor (hasta 13 dígitos) y razón social (240).
    - Código de autorización o CUF (100) y número de factura (hasta 20 dígitos).
    - N.º DUI/DIM (15), con validación de formato DUI `AAAADDDCNNNNNNNN` y DIM `AAAADDDNNNNNNNN`, y fecha de factura, DUI o DIM.
    - Importe total, ICE, IEHD, IPJ, tasas, otro no sujeto a CF, exentos, tasa cero, descuentos y gift card, más los calculados **subtotal, base CF y CF 13 %**.
    - `tipoCompra` (1–5) y código de control (17, `0` por defecto).
    - Tipo de documento: factura / boleto aéreo (código 1) / DUI-DIM (código 3).
    - Estado RCV: Sin reportar / Reportada por el proveedor / Confirmada / Registrada por nosotros / Anulada.
17. **Asistente de regla del 30 %:** para gasolina o diésel en estación de servicio, propone "otro no sujeto a CF" = 30 % del total. Para compras Tasa Cero, pone el 100 % en tasa cero, con CF 0.
18. **Pantalla "Compras SIN (RCV)":**
    - a) Botón **Consultar compras reportadas** (`consultaCompras`), por fecha o período.
    - b) Cruce automático con las `SupplierInvoice` por NIT, código de autorización y número. Muestra coincidencias, faltantes en M-INV y faltantes en el SIN.
    - c) **Confirmar** en lote (`confirmacionCompras`), asignando el `tipoCompra`.
    - d) **Registrar** las no declaradas (`recepcionPaqueteCompras` y luego `validacionRecepcionPaqueteCompras`).
    - e) **Anular la confirmación** (`AnulacionCompra`).
    - f) Historial de lotes con `codigoRecepcion`, estado, códigos de respuesta, fecha de envío, gestión y período.
19. **Generador de paquetes:**
    - Un XML por factura (`registroCompra` o `confirmacionCompra`), validado contra el XSD local.
    - Hasta 500 por paquete, con `nro` correlativo 1..n dentro del paquete [RECOMENDACIÓN].
    - Empaquetado TAR, compresión Gzip y SHA-256 del archivo `.tar.gz`, con `cantidadFacturas` exacta.
20. **Habilitación:** un checklist en la configuración de la empresa que indica si el servicio "Registro de compras" está habilitado para el sistema (paso manual en el SIAT, B13). Mientras no lo esté, las opciones se ocultan o desactivan.

### D. Libros, reportes y exportaciones

21. **Libro de Ventas interno**, en la estructura exacta de 24 columnas de §5.2. Sirve para conciliar y para facturas manuales o de contingencia fuera de línea que el contador deba cargar.
    - Exporta a .xlsx con la estructura de la plantilla oficial X4: ESPECIFICACIÓN = 2, fechas `dd/mm/yyyy` y el mismo orden de columnas.
    - ESTADO se toma del estado de la factura: V / A / C.
    - Aplica los NIT especiales 99001, 99002 y 99003.
22. **Libro de Compras** exportable con la estructura de la plantilla X3 (ESPECIFICACIÓN = 1).
23. **Registro de Reintegros** exportable (ESPECIFICACIÓN = 6).
24. **Resumen fiscal mensual:**
    - Débito fiscal de ventas, menos el efectivo de las notas emitidas.
    - Crédito fiscal de compras por tipo, menos el efectivo de las notas recibidas.
    - Reintegros.
    - Totales de exentos y tasa cero.
    - Aviso: "el mapeo a Form. 200 / 400 es responsabilidad del contador" (NO DOCUMENTADO).
    - Bloqueo: un documento marcado como "usado en DDJJ" no se puede anular (A20).

### E. Tasa Cero, modalidades y casos especiales

25. **Registrar una venta facturada externamente** (Tasa Cero de la Ley 1613, emitida en el módulo del SIAT, sector 33, o una factura del Portal Web usado como contingencia). Campos: n.º, CUF, fecha, monto y sector. Descarga el stock y contabiliza **sin** llamar servicios de emisión.
26. **Opción de contingencia "Portal Web"** documentada en la guía de uso: si el sistema falla, la empresa puede emitir en el Portal Web, siempre que haya pedido antes la autorización. Después, esas ventas se registran en M-INV como en el punto 25.
27. **Arquitectura tipo "bandeja fiscal"**, siguiendo el patrón SAP B1 (B7):
    - La venta o devolución se replica de inmediato a una tabla de documentos fiscales.
    - Un proceso genera el XML, lo valida contra el XSD, calcula el hash, lo envía y guarda la respuesta.
    - Esto desacopla el POS del SIN.
28. **Fuera de alcance por ahora**: facturación conjunta, por terceros y comisionistas, nota de conciliación (29), nota ICE (48, salvo que haya productos con ICE) y registros de combustible y prevaloradas. Se dejan preparados el catálogo de documentos sector y el tipo de punto de venta.

### F. Pruebas de certificación (ambiente piloto)

29. Reproducir los casos oficiales (A22):
    - `CasosDePruebaEmisionIndividual1.xlsx` filas 48 y 49: tipo 3, sector 24, sucursal 0, con punto de venta 1 y 0. Esperado **908**.
    - `CasosDePruebaAnulacionReversion.xlsx` filas 48 y 49: anulación de la nota, motivo "NOTA DE CREDITO-DEBITO MAL EMITIDA". Esperado **905**.
    - Agregar pruebas unitarias de las fórmulas con los ejemplos 1 a 4 de §3.9, de la validación XSD con los XML oficiales (A6, A7, X1, X2) y del empaquetado TAR, Gzip y SHA-256.

---

## 8. Dudas y huecos de la documentación

1. **B1 casi no tiene texto.** La página "Notas de Crédito-Débito" de casos especiales es sobre todo una imagen. No trae campos, plazos ni fórmulas: todo eso sale de A1–A16. Si otro agente revisa el tema, no debe esperar más datos en B1.
2. **Nombre y URL de los servicios o WSDL** de Documentos de Ajuste y Registro de Compras: NO DOCUMENTADO en estas páginas.
3. **Formato del parámetro `archivo`** (bytes, base64, Gzip) en `recepcionDocumentoAjuste` y en los servicios de compras: aquí no se detalla, solo el TAR y Gzip de los paquetes de compras. El formato de la salida `Archivo` de `consultaCompras` también es NO DOCUMENTADO; no se sabe si es XML, TAR o CSV, ni con qué campos.
4. **`codigoTipoDocumentoIdentidad`**: A1 dice valores 1–9 y el XSD del sector 24 restringe a 1..5. [RECOMENDACIÓN] Validar con el catálogo sincronizado y ampliar el XSD local si hace falta.
5. **`montoDescuentoCreditoDebito`** es "Obligatorio: Sí" en A1, pero es **nillable** en el XSD y el ejemplo lo envía nulo. [RECOMENDACIÓN] Enviar `xsi:nil` cuando sea 0 en el sector 24; verificarlo en piloto.
6. **Sector 24 frente a 47:** no está claro si una factura con descuento adicional **debe** usar el 47 o si el 24 con `montoDescuentoCreditoDebito` es aceptable.
7. **`montoTotalOriginal`**: A1 lo define como "monto sujeto a crédito fiscal" y A4 como "Σ subTotal(tx=1)". Si la factura original tuvo **gift card** o descuento adicional, los dos valores difieren. En el ejemplo del 47 se usa la suma de subtotales (850), no lo cobrado (800).
8. **Prorrateo del `montoDescuento` por ítem** en devoluciones parciales: NO DOCUMENTADO.
9. **Si el SIN controla el acumulado de varias notas** sobre la misma factura: NO DOCUMENTADO. El error 1033 solo habla de "devuelto mayor al original".
10. **Quién toma el débito y quién el crédito** con la nota, y cómo se registra una nota **recibida** en el RCV de compras: NO DOCUMENTADO. La plantilla de compras habla de "Factura o Nota Fiscal" sin más detalle.
11. **Notas de "débito" por aumento de precio:** NO DOCUMENTADO. El XSD solo modela devoluciones.
12. **Contingencia para notas:** `recepcionDocumentoAjuste` solo acepta `codigoEmision = 1` (online) y es individual. **No hay** envío por paquete ni masivo documentado para notas. Qué hacer si el SIN no está disponible al momento de la devolución es NO DOCUMENTADO; ¿se puede usar el Portal Web como contingencia?
13. **Códigos de reversión inconsistentes:** A21 cita 981 como "factura no disponible para reversión", 3011 y 3012. En A15, 981 es "Rango De Fechas De Evento Significativo Invalido" y 3011 y 3012 no existen; la tabla llega hasta 3010.
14. **Numeración de notas** (por sucursal, punto de venta o NIT) y si el aviso de correlatividad 2000 aplica a notas: NO DOCUMENTADO en el bloque.
15. **XSD de compras con incoherencias:**
    - `registroCompra.numeroFactura` tiene mínimo 1, pero en DUI/DIM la plantilla exige **0**.
    - `NIT PROVEEDOR` mide 12 dígitos en la plantilla y 13 en el XSD.
    - `confirmacionCompra.tipoCompra` acepta **0..9** en el XSD y la documentación solo 1..5.
    - `validacionRecepcionPaqueteCompras` dice que devuelve un archivo que su tabla de salidas no lista.
16. **`gestion` y `periodo`** en compras: los rangos y el significado exactos (mes o bimestre) son NO DOCUMENTADO; se asume año y mes 1–12. El **plazo** para confirmar o registrar compras de un período también es NO DOCUMENTADO.
17. **`AnulacionCompra`** no recibe CUF; `codigoAutorizacion` se describe "de la factura manual". Para anular la confirmación de una compra con factura en línea, ¿se envía el CUF en `codigoAutorizacion`? No está claro.
18. **Form. 200 / 400 y libros:** NO DOCUMENTADO; solo existe el error 3003 sobre los formularios 200 y 210.
19. **Tasa Cero de la Ley 1613:** vigencia después de la gestión 2025, lista NANDINA (ADS5302.pdf, no descargado) y si existe un servicio web para sistemas propios: NO DOCUMENTADO. La página indica solo el módulo del SIAT.
20. **Sector 48 (ICE):** su XSD (`CreditoDebitoIceXML.zip`) y su PDF no están en los adjuntos.
21. **Ejemplos oficiales con datos incoherentes:** el XML del sector 24 devuelve un ítem que no estaba en la factura original, y la fecha de la factura es 3919. No deben usarse como casos de validación de negocio, solo de estructura.
