# 05 — XML / XSD de documentos fiscales y validaciones de negocio (SIAT, modalidad Facturación Computarizada en Línea)

> Especificación de implementación para M-INV (.NET 8). Todo lo que sigue sale de los archivos del bloque 05; lo que no aparece en ellos se marca **NO DOCUMENTADO**. Las secciones marcadas **⚠ ELECTRÓNICA** aplican solo a la modalidad Electrónica en Línea (con firma digital) y **no** deben implementarse ahora.

---

## 0. Alcance y fuentes

### 0.1 Archivos leídos (bloque 05)

| Clave | Archivo | Contenido |
|---|---|---|
| **[XSD-CV]** | `adjuntos/xml/CompraVentaXML/facturaComputarizadaCompraVenta.xsd` (+ `.xml`) | XSD ver. 23/08/2021, sector 1 |
| **[XSD-NCD]** | `adjuntos/xml/CreditoDebitoXML/notaComputarizadaCreditoDebito.xsd` (+ `.xml`) | XSD ver. 23/08/2021, sector 24 |
| **[XSD-NCDD]** | `adjuntos/xml/CreditoDebitoDescuentoXML/notaComputarizadaCreditoDebitoDescuento.xsd` (+ `.xml`) | XSD ver. 23/08/2021, sector 47 |
| **[XSD-TC]** | `adjuntos/xml/TasaCeroXML/facturaComputarizadaTasaCero.xsd` (+ `.xml`) | XSD ver. 23/08/2021, sector 8 |
| **[XSD-ME]** | `adjuntos/xml/MonedaExtranjeraXML/facturaComputarizadaMonedaExtranjera.xsd` (+ `.xml`) | XSD ver. 23/08/2021, sector 9 |
| **[XSD-ALQ]** | `adjuntos/xml/AlquilerBienInmuebleXML/facturaComputarizadaAlquilerBienInmueble.xsd` (+ `.xml`) | XSD ver. 23/08/2021, sector 2 |
| **[XSD-BON]** | `adjuntos/xml/CompraVentaBonXML/facturaComputarizadaCompraVentaBon.xsd` (+ `.xml`) | XSD ver. **19/11/2021**, sector 35 |
| **[XSD-CONC]** | `adjuntos/xml/conciliacionXML/notaComputarizadaConciliacion.xsd` (+ `.xml`) | sin comentario de versión, sector 29 |
| (comparación) | los `*Electronica*.xsd/.xml` de las mismas carpetas | solo para marcar diferencias ⚠ ELECTRÓNICA |
| **[P-CV]** | `txt/facturacion-en-linea__archivos-xml-xsd-de-facturas-electronicas__factura-de-compra-y-venta.md` | tabla de campos sector 1 |
| **[P-NCD]** | `txt/…__nota-credito-debito.md` | tabla de campos sector 24 |
| **[P-NCDD]** | `txt/…__nota-credito-debito-descuento.md` | tabla de campos sector 47 |
| **[P-TC]** | `txt/…__factura-tasa-cero.md` | sector 8 |
| **[P-ME]** | `txt/…__factura-de-compra-venta-moneda-extranjera.md` | sector 9 |
| **[P-ALQ]** | `txt/…__recibo-alquiler-bienes-inmuebles.md` | sector 2 |
| **[P-BON]** | `txt/…__factura-compra-venta-bonificaciones.md` | sector 35 |
| **[P-CONC]** | `txt/…__nota-conciliacion.md` | sector 29 (leído porque `conciliacionXML` está en el bloque) |
| **[VAL1]** | `txt/…__validaciones-documentos-sector__validaciones.md` | fórmulas por sector (1–32) |
| **[VAL2]** | `txt/…__validaciones-documentos-sector__validaciones-cont.md` | fórmulas por sector (32–55) |
| **[FE]** | `txt/facturacion-en-linea__factura-electronica.md` | formato general del XML (el resto de la página es una imagen) |

(`…` = `facturacion-en-linea__archivos-xml-xsd-de-facturas-electronicas`)

### 0.2 Referencias cruzadas fuera del bloque (solo para contexto; su especificación detallada es de otros bloques)

| Clave | Archivo | Qué se tomó |
|---|---|---|
| [X-ERR] | `txt/facturacion-en-linea__implementacion-servicios-facturacion__codigos-error-siat.md` | códigos de error de validación de XML (sección 11) |
| [X-EMI] | `txt/facturacion-en-linea__emision-y-envio-de-facturas__emision-y-envio.md` | reglas de código de excepción, 99001/99002/99003, CI/NIT numérico |
| [X-RED] | `txt/facturacion-en-linea__algoritmos-utilizados__algoritmo-de-redondeo.md` | redondeo HALF-UP a 2 decimales |
| [X-VER] | `txt/versionamiento__versionamiento-2021.md`, `…-2022.md`, `…-2023.md` | cambios históricos de XSD |

### 0.3 Verificación empírica

Los 8 XML de ejemplo computarizados se validaron contra su XSD con `System.Xml.XmlReaderSettings` + `XmlSchemaSet` de .NET (PowerShell 5.1, .NET Framework): **los 8 son válidos**. En la sección 10 están los casos límite probados.

---

## 1. Formato general del XML (común a todos los documentos)

Fuente: [FE], [XSD-*], [P-*].

1. **Modalidad computarizada**: según [FE], para emitir en *Facturación Computarizada en Línea* se requiere "Token de acceso delegado (en el caso de sistema proveedor) o propio, y la **huella del archivo XML**". No lleva firma digital. El algoritmo de la huella **NO DOCUMENTADO en este bloque**. **[corregido por revisión]** Está documentado fuera del bloque: la "huella" es el **SHA-256 (hex en minúsculas, 64 caracteres) de los bytes GZIP del XML**, el mismo valor que viaja en `hashArchivo` (`requerimientos__sistema-informatico.md`: "…es utilizado también como Huella Digital en la modalidad computarizada en Línea"; `emision-y-envio.md`: "HASH (SHA 256) del archivo compreso… (también llamado Huella Digital)"). Ver `02-…` §5.
2. **Declaración XML** (todos los ejemplos): `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>`. Presencia/ausencia de BOM: **NO DOCUMENTADO** (recomendación: UTF-8 **sin** BOM).
3. **Sin namespace**: los XSD computarizados **no tienen `targetNamespace`** (`elementFormDefault="qualified"`, `attributeFormDefault="unqualified"`). Todos los elementos van **sin namespace**. El elemento raíz declara solo el namespace de instancia XSI y la ubicación del esquema:
   ```xml
   <facturaComputarizadaCompraVenta xsi:noNamespaceSchemaLocation="facturaComputarizadaCompraVenta.xsd"
                                    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
   ```
   El atributo `xsi:noNamespaceSchemaLocation` lleva el **nombre del archivo XSD** (sin ruta). Los XSD computarizados declaran `xmlns:ds="http://www.w3.org/2000/09/xmldsig#"` pero **no lo usan** (inofensivo).
4. **Estructura**: raíz → `cabecera` (exactamente 1) → `detalle` (1..N, según tipo). Nota de conciliación: `cabecera` → `detalleOriginal` (1..∞) → `detalleConciliacion` (1..∞).
5. **Orden estricto**: todo está definido con `xs:sequence`; los elementos deben emitirse **exactamente en el orden del XSD** (no en el orden de las tablas de las páginas, que a veces difiere — ver 2.4).
6. **Ningún elemento es opcional por ausencia**: en `cabecera` y `detalle` no hay ningún `minOccurs="0"`. Los campos "no obligatorios" son `nillable="true"` y **deben escribirse igual**, con valor nulo:
   ```xml
   <codigoPuntoVenta xsi:nil="true"/>
   ```
   Omitir el elemento hace fallar el XSD (probado, sección 10). [P-CV] lo dice explícitamente para `complemento`: "En otro caso enviar un valor nulo agregando en la Etiqueta xsi:nil="true"".
7. **Campos no nillable** no pueden llevar `xsi:nil` (probado con `leyenda`).
8. **Números**: `xs:integer` / `xs:decimal` → punto decimal, sin separador de miles, sin exponente (`1E2` y `100,50` fallan; probado). Se aceptan ceros finales (`775.00`, `99.10`) y enteros sin decimales (`99`).
9. **Fechas** (`fechaEmision`, `fechaEmisionFactura`): tipo `xs:dateTime`, "formato UTC Extendido, por ejemplo: "2020-02-15T08:40:12.215"" [P-CV]. Ejemplos: `2021-10-06T16:03:48.675` (sin zona horaria, milisegundos con 3 dígitos). El XSD aceptaría un sufijo `Z` u offset, pero los ejemplos no lo usan. Zona horaria que debe usarse: **NO DOCUMENTADO en este bloque**. Formato .NET recomendado: `yyyy-MM-dd'T'HH:mm:ss.fff`.
10. **Límite de líneas**: `detalle` maxOccurs = **500** en todos los documentos del bloque, salvo conciliación (`unbounded`). 501 líneas falla el XSD (probado).
11. **Servicios** [P-CV], [P-BON], [P-ALQ]: "En el caso de venta de servicios, se debe considerar en cantidad consignar **1**, en unidad de medida consignar **58 (unidad servicio)**, el precio del servicio consignarlo en precio unitario".
12. **⚠ ELECTRÓNICA** (no implementar ahora): la raíz se llama `factura**Electronica**…` / `nota…Electronica…`; el XSD importa `<xs:import namespace="http://www.w3.org/2000/09/xmldsig#" schemaLocation="../SignatureSchema.xsd"/>` y agrega `<xs:element ref="ds:Signature"/>` como **último** hijo de la raíz (después del último `detalle`). El ejemplo firmado usa: CanonicalizationMethod `http://www.w3.org/TR/2001/REC-xml-c14n-20010315`, SignatureMethod `http://www.w3.org/2001/04/xmldsig-more#rsa-sha256`, Reference `URI=""` con transforms `enveloped-signature` y `REC-xml-c14n-20010315#WithComments`, DigestMethod `http://www.w3.org/2001/04/xmlenc#sha256`, y `KeyInfo/X509Data/X509Certificate`. [P-CV]: "En caso de utilizar la modalidad electrónica en linea, no se olvide de incluir en el XSD la dirección donde se encuentra el SignatureSchema". Salvo el nombre de raíz y la firma, los XSD electrónicos son **idénticos** a los computarizados (verificado con `diff`).

---

## 2. Factura Compra Venta — `facturaComputarizadaCompraVenta` (codigoDocumentoSector = 1)

Fuente: [XSD-CV], [P-CV]. "Habilitada para transacciones por bienes o servicios en general, incluyen línea blanca, negra y cualquier actividad que involucre un intercambio de estos."

- Elemento raíz: `facturaComputarizadaCompraVenta`
- `xsi:noNamespaceSchemaLocation="facturaComputarizadaCompraVenta.xsd"`
- `cabecera` 1 vez; `detalle` minOccurs=1, maxOccurs=500.

Notación: `dec(T,F)` = `xs:decimal` con `totalDigits=T`, `fractionDigits=F` (parte entera máx. T−F dígitos). "Nil" = `nillable="true"`. "Oblig." = columna "Obligatorio" de [P-CV].

### 2.1 Cabecera (orden exacto del XSD)

| # | Elemento | Tipo XSD y restricción | Nil | Oblig. | Descripción / regla (fuente [P-CV] salvo indicación) |
|---|---|---|---|---|---|
| 1 | `nitEmisor` | `xs:integer` 1 … 9999999999999 (13 dígitos) | No | Sí | NIT del emisor registrado en el Padrón Nacional de Contribuyentes. |
| 2 | `razonSocialEmisor` | `xs:string` long. 1–200 | No | Sí | Razón social del emisor registrada en el Padrón. |
| 3 | `municipio` | `xs:string` long. 1–25 | No | Sí | "Nombre del departamento o municipio que se refleja en la Factura." |
| 4 | `telefono` | `xs:string` long. 1–25 | **Sí** | No | Teléfono que se refleja en la factura. Si no hay: `xsi:nil`. **Cadena vacía falla** (minLength 1). |
| 5 | `numeroFactura` | `xs:integer` 1 … 9999999999 (10 dígitos) | No | Sí | "Numeración propia que se le asigna a la Factura." |
| 6 | `cuf` | `xs:string` long. 1–100 | No | Sí | Código Único de Facturación, "debe ser generado por el emisor siguiendo el algoritmo indicado" (algoritmo NO DOCUMENTADO en este bloque). |
| 7 | `cufd` | `xs:string` long. 1–100 | No | Sí | Código Único de Facturación Diario, "se obtiene al consumir el servicio web correspondiente". |
| 8 | `codigoSucursal` | `xs:integer` 0 … 9999 | No | Sí | Sucursal registrada en el Padrón donde se emite. ([P-NCD]: "sucursal = 0 (casa matriz)"). |
| 9 | `direccion` | `xs:string` long. 1–500 | No | Sí | Dirección de la sucursal registrada en el Padrón. |
| 10 | `codigoPuntoVenta` | `xs:integer` 0 … 9999 | **Sí** | No | Punto de venta "creado mediante un servicio web". Ejemplo CV: `xsi:nil`; ejemplos de notas: `0`. **[corregido por revisión]** Recomendación consolidada: enviar siempre el **valor numérico** (0 si no hay punto de venta), para que coincida con el parámetro `codigoPuntoVenta` de la solicitud y con los 4 dígitos de POS del CUF (errores 1008 / 930). Qué prefiere el SIN (nil o 0) es NO DOCUMENTADO; ambos pasan el XSD. |
| 11 | `fechaEmision` | `xs:dateTime` | No | Sí | Fecha y hora de emisión, "UTC Extendido", ej. `2020-02-15T08:40:12.215`. |
| 12 | `nombreRazonSocial` | `xs:string` long. 1–500 | **Sí** | No | Nombre o razón social del cliente. |
| 13 | `codigoTipoDocumentoIdentidad` | `xs:integer` 1 … 5 | No | Sí | Paramétrica tipo de documento; "valores del 1 al 5". ([P-TC]: "Ejemplo 1 que representa al CI"). |
| 14 | `numeroDocumento` | `xs:string` long. 1–20 | No | Sí | Número del documento del cliente. |
| 15 | `complemento` | `xs:string` **maxLength 5, sin minLength** | **Sí** | No | "Valor que otorga el SEGIP en casos de cédulas de identidad con número duplicado, En otro caso enviar un valor nulo agregando en la Etiqueta xsi:nil="true"". (Una cadena vacía pasa el XSD, pero la regla dice nil.) |
| 16 | `codigoCliente` | `xs:string` long. 1–100 | No | Sí | "Código de identificación único del cliente, deberá ser asignado por el sistema de facturación del contribuyente." |
| 17 | `codigoMetodoPago` | `xs:integer` 1 … 308 | No | Sí | Paramétrica método de pago. "Por ejemplo 1 que representa a un pago en efectivo. (Utilizar el tipo de pago Otros (5) solo si el método utilizado no esta disponible)". |
| 18 | `numeroTarjeta` | `xs:integer` 0 … 9999999999999999 (16 dígitos) | **Sí** | No | "Cuando el método de pago es 2 (Tarjeta), debe enviarse este valor pero ofuscado con los primeros y últimos 4 dígitos en claro y ceros al medio. Ej: 4797000000007896, en otro caso, debe enviarse un valor nulo." |
| 19 | `montoTotal` | `dec(17,2)` **> 0** (minExclusive 0) | No | Sí | "Monto total por el cual se realiza el hecho generador." |
| 20 | `montoTotalSujetoIva` | `dec(17,2)` ≥ 0 | No | Sí | "Monto base para el cálculo del crédito fiscal." |
| 21 | `codigoMoneda` | `xs:integer` 1 … 154 | No | Sí | Paramétrica moneda de la transacción. |
| 22 | `tipoCambio` | `dec(17,2)` > 0 | No | Sí | "si el código de moneda es boliviano deberá ser igual a 1." |
| 23 | `montoTotalMoneda` | `dec(17,2)` > 0 | No | Sí | Monto total en la moneda de la transacción; "si el código de moneda es boliviano deberá ser igual al monto total." |
| 24 | `montoGiftCard` | `dec(17,2)` ≥ 0 | **Sí** | No | "Monto a ser cancelado con una Gift Card". |
| 25 | `descuentoAdicional` | `dec(17,2)` ≥ 0 | **Sí** | No | "Monto Adicional al descuento por item". |
| 26 | `codigoExcepcion` | `xs:integer` 0 … 1 | **Sí** | No | "Por defecto, enviar este campo con un valor de cero (0) o nulo. Solo cuando se desee autorizar al SIN el registro de una factura emitida a un NIT inválido se debe enviar el valor de uno (1)". |
| 27 | `cafc` | `xs:string` long. 1–50 | **Sí** | No | "Código de Autorización de Facturas por Contingencia". Nil en emisión normal. |
| 28 | `leyenda` | `xs:string` long. 1–200 | No | Sí | "Leyenda asociada a la actividad económica." (Texto de catálogo: NO DOCUMENTADO en este bloque.) |
| 29 | `usuario` | `xs:string` long. 1–100 | No | Sí | "Identifica al usuario que emite la factura, deberá ser descriptivo. Por ejemplo JPEREZ". |
| 30 | `codigoDocumentoSector` | `xs:integer` **fixed="1"** | No | Sí | "Para este tipo de factura este valor es 1." Cualquier otro valor falla el XSD. |

### 2.2 Detalle (1 a 500 repeticiones, orden exacto)

| # | Elemento | Tipo XSD y restricción | Nil | Oblig. | Descripción / regla [P-CV] |
|---|---|---|---|---|---|
| 1 | `actividadEconomica` | `xs:string` long. 1–10 | No | Sí | Actividad económica registrada en el Padrón relacionada al NIT (ej. `451010`). |
| 2 | `codigoProductoSin` | `xs:integer` 1 … 99999999 (8 dígitos) | No | Sí | "Homologado a los códigos de productos genéricos enviados por el SIN a través del servicio de sincronización." |
| 3 | `codigoProducto` | `xs:string` long. 1–50 | No | Sí | Código propio del contribuyente. |
| 4 | `descripcion` | `xs:string` long. 1–500 | No | Sí | Descripción propia. "Si corresponde, incluir en el campo Descripción de la factura el número de la Guía de Tránsito utilizada para el transporte de los productos." |
| 5 | `cantidad` | `dec(17,2)` > 0 | No | Sí | "En caso de servicio este valor debe ser 1." **Solo 2 decimales** en este XSD. |
| 6 | `unidadMedida` | `xs:integer` 1 … 200 | No | Sí | Paramétrica unidad de medida (58 = unidad servicio). |
| 7 | `precioUnitario` | `dec(17,2)` > 0 | No | Sí | Precio unitario. **Solo 2 decimales**. |
| 8 | `montoDescuento` | `dec(17,2)` ≥ 0 | **Sí** | No | "Monto de descuento sobre el producto o servicio específico, Si no aplica deberá ser nulo." (El ejemplo oficial envía `0`; ambas formas pasan el XSD.) |
| 9 | `subTotal` | `dec(17,2)` **> 0** | No | Sí | "El subtotal es igual a la (cantidad * precio unitario) – descuento." Un subtotal 0 falla (usar sector 35 para ítems 100 % bonificados). |
| 10 | `numeroSerie` | `xs:string` long. 0–1500 | **Sí** | No | "Número de serie correspondiente al producto vendido de línea blanca o negra. Nulo en otro caso." |
| 11 | `numeroImei` | `xs:string` long. 0–1500 | **Sí** | No | "Número de Imei del celular vendido. Nulo en otro caso." |

Nota [P-CV]: "Si los números de serie o Imei son pocos incluirlos en el detalle de la factura caso contrario enviar los mismos consumiendo el servicio **Recepción Archivos Anexos** correspondiente, el cual permite el registro simultaneo de varios numeros de Serie o Imei." (El servicio está documentado en otro bloque.)

### 2.3 XML de ejemplo oficial completo (computarizada compra-venta)

Copiado sin cambios de `adjuntos/xml/CompraVentaXML/facturaComputarizadaCompraVenta.xml` (valida contra el XSD):

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<facturaComputarizadaCompraVenta xsi:noNamespaceSchemaLocation="facturaComputarizadaCompraVenta.xsd" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
    <cabecera>
        <nitEmisor>1003579028</nitEmisor>
        <razonSocialEmisor>Carlos Loza</razonSocialEmisor>
        <municipio>La Paz</municipio>
        <telefono>78595684</telefono>
        <numeroFactura>1</numeroFactura>
        <cuf>44AAEC00DBD34C53C3E2CCE1A3FA7AF1E2A08606A667A75AC82F24C74</cuf>
        <cufd>BQUE+QytqQUDBKVUFOSVRPQkxVRFZNVFVJBMDAwMDAwM</cufd>
        <codigoSucursal>0</codigoSucursal>
        <direccion>AV. JORGE LOPEZ #123</direccion>
        <codigoPuntoVenta xsi:nil="true"/>
        <fechaEmision>2021-10-06T16:03:48.675</fechaEmision>
        <nombreRazonSocial>Mi razon social</nombreRazonSocial>
        <codigoTipoDocumentoIdentidad>1</codigoTipoDocumentoIdentidad>
        <numeroDocumento>5115889</numeroDocumento>
        <complemento xsi:nil="true"/>
        <codigoCliente>51158891</codigoCliente>
        <codigoMetodoPago>1</codigoMetodoPago>
        <numeroTarjeta xsi:nil="true"/>
        <montoTotal>99</montoTotal>
        <montoTotalSujetoIva>99</montoTotalSujetoIva>
        <codigoMoneda>1</codigoMoneda>
        <tipoCambio>1</tipoCambio>
        <montoTotalMoneda>99</montoTotalMoneda>
        <montoGiftCard xsi:nil="true"/>
        <descuentoAdicional>1</descuentoAdicional>
        <codigoExcepcion xsi:nil="true"/>
        <cafc xsi:nil="true"/>
        <leyenda>Ley N° 453: Tienes derecho a recibir información sobre las características y contenidos de los
            servicios que utilices.
        </leyenda>
        <usuario>pperez</usuario>
        <codigoDocumentoSector>1</codigoDocumentoSector>
    </cabecera>
    <detalle>
        <actividadEconomica>451010</actividadEconomica>
        <codigoProductoSin>49111</codigoProductoSin>
        <codigoProducto>JN-131231</codigoProducto>
        <descripcion>JUGO DE NARANJA EN VASO</descripcion>
        <cantidad>1</cantidad>
        <unidadMedida>1</unidadMedida>
        <precioUnitario>100</precioUnitario>
        <montoDescuento>0</montoDescuento>
        <subTotal>100</subTotal>
        <numeroSerie>124548</numeroSerie>
        <numeroImei>545454</numeroImei>
    </detalle>
</facturaComputarizadaCompraVenta>
```

Observaciones sobre el ejemplo:
- Cálculo: `subTotal = 1 × 100 − 0 = 100`; `montoTotal = 100 − 1 (descuentoAdicional) = 99`; método 1 (efectivo, sin gift card) → `montoTotalSujetoIva = 99`; `montoTotalMoneda = 99 / 1 = 99`.
- La `leyenda` del ejemplo contiene saltos de línea y espacios de indentación que `xs:string` conserva y que cuentan para `maxLength 200`. **Recomendación**: emitir el texto exacto de catálogo sin espacios ni saltos añadidos.
- [FE] reproduce el mismo ejemplo con `descripcion` = "MI PRODUCTO O SERVICIO" y `numeroImei xsi:nil="true"`.
- `codigoCliente` = `51158891` en los ejemplos; la regla de formación **NO está documentada** (solo "asignado por el sistema de facturación").

### 2.4 Diferencias entre la tabla de [P-CV] y el XSD (el XSD manda)

- **Orden**: [P-CV] lista `montoGiftCard`, `descuentoAdicional`, `codigoExcepcion`, `cafc` **antes** de `codigoMoneda`/`tipoCambio`/`montoTotalMoneda`. En el XSD van **después** de `montoTotalMoneda` (posiciones 24–27). Un orden distinto falla el XSD (probado).
- La tabla no indica longitudes ni rangos; están solo en el XSD (tablas 2.1/2.2).

---

## 3. Reglas de validación de negocio — Compra Venta (sector 1)

Fuente: [VAL1] sección "COMPRA VENTA", más las descripciones de [P-CV].

### 3.1 Fórmulas oficiales

| # | Fórmula | Condición |
|---|---|---|
| V1 | `subTotal = (cantidad × precioUnitario) − montoDescuento` | por línea |
| V2 | `montoTotal = Σ(subTotal) − descuentoAdicional` | siempre |
| V3 | `montoTotalMoneda = montoTotal / tipoCambio` | siempre |
| V4 | `montoTotalSujetoIva = montoTotal − montoGiftCard` | "Si metodoPago es con Gift Card" |
| V5 | `montoTotalSujetoIva = montoTotal` | "Si metodoPago no es con Gift Card" |

**Aclaración importante:** según [VAL1], la Gift Card **NO** se resta del `montoTotal`; se resta solo para obtener `montoTotalSujetoIva`. Es decir, `montoTotal = Σ subTotal − descuentoAdicional` (no "− montoGiftCard").

### 3.2 Reglas de campo derivadas de las descripciones

| # | Regla | Fuente |
|---|---|---|
| R1 | Si `codigoMoneda` = boliviano → `tipoCambio = 1` y `montoTotalMoneda = montoTotal`. | [P-CV] |
| R2 | `numeroTarjeta` solo si `codigoMetodoPago` = 2 (Tarjeta); enmascarado: primeros 4 y últimos 4 dígitos en claro, ceros al medio (ej. `4797000000007896`); si no, `xsi:nil`. | [P-CV] |
| R3 | `complemento` solo para cédula de identidad con número duplicado (valor SEGIP), máx. 5 caracteres; si no, `xsi:nil`. | [P-CV] |
| R4 | `codigoExcepcion` = 0 o nil por defecto; `1` solo para autorizar el registro de una factura a un NIT inválido. | [P-CV] |
| R5 | `montoDescuento` nil si no aplica. | [P-CV] |
| R6 | Servicios: `cantidad = 1`, `unidadMedida = 58`, precio del servicio en `precioUnitario`. | [P-CV] |
| R7 | `codigoMetodoPago = 5 (Otros)` solo si el método real no está en el catálogo. | [P-CV] |
| R8 | `usuario` descriptivo (ej. `JPEREZ`). | [P-CV] |
| R9 | `leyenda` asociada a la actividad económica. | [P-CV] |
| R10 | `numeroSerie`/`numeroImei` solo para línea blanca/negra o celulares; si son muchos, enviarlos por "Recepción Archivos Anexos". | [P-CV] |
| R11 | `montoTotal` > 0, `subTotal` > 0, `cantidad` > 0, `precioUnitario` > 0, `tipoCambio` > 0, `montoTotalMoneda` > 0 (minExclusive 0); `montoTotalSujetoIva`, `montoGiftCard`, `descuentoAdicional`, `montoDescuento` ≥ 0. | [XSD-CV] |
| R12 | Todos los montos y cantidades del sector 1 con **máximo 2 decimales** (`fractionDigits 2`), parte entera máx. 15 dígitos. | [XSD-CV] |

Reglas **no escritas en el bloque** pero implícitas en V2 y R11: `descuentoAdicional < Σ subTotal` (porque `montoTotal` > 0) y `montoDescuento < cantidad × precioUnitario` (porque `subTotal` > 0). Que `montoGiftCard ≤ montoTotal` se deduce de `montoTotalSujetoIva ≥ 0`.

### 3.3 Redondeo (referencia cruzada)

El bloque no define el redondeo para facturas. [X-RED] (fuera del bloque): "montos expresados con dos decimales y utiliza el redondeo tradicional o HALF-UP" (ej. 3.14159 → 3.14). En .NET: `Math.Round(x, 2, MidpointRounding.AwayFromZero)` sobre `decimal`. En qué paso se redondea (cada `subTotal` o solo totales) **NO DOCUMENTADO**; como el XSD solo admite 2 decimales en `subTotal`, hay que redondear cada línea antes de sumar.

### 3.4 Ejemplo numérico ilustrativo con Gift Card (calculado con las fórmulas V1–V4; no es un ejemplo oficial)

| Línea | cantidad | precioUnitario | montoDescuento | subTotal (V1) |
|---|---|---|---|---|
| 1 | 2.00 | 75.50 | 5.00 | 146.00 |
| 2 | 1.00 | 104.00 | nil (0) | 104.00 |

- Σ subTotal = 250.00; `descuentoAdicional` = 10.00 → `montoTotal` = 240.00 (V2)
- Pago con Gift Card de 40.00 → `montoTotalSujetoIva` = 240.00 − 40.00 = **200.00** (V4)
- BOB: `tipoCambio` = 1, `montoTotalMoneda` = 240.00 (V3/R1)
- El código de método de pago "Gift Card" **no está en este bloque** (catálogo de sincronización).

---

## 4. Nota Crédito-Débito — `notaFiscalComputarizadaCreditoDebito` (codigoDocumentoSector = 24)

Fuente: [XSD-NCD], [P-NCD]. "Habilitada para realizar ajustes en el Crédito y Débito Fiscal de los Sujetos Pasivos o compradores."

- **Elemento raíz**: `notaFiscalComputarizadaCreditoDebito` (con la palabra **"Fiscal"**, aunque el archivo se llama `notaComputarizadaCreditoDebito.xsd`).
- `xsi:noNamespaceSchemaLocation="notaComputarizadaCreditoDebito.xsd"`
- `cabecera` 1 vez; `detalle` **minOccurs = 2**, maxOccurs = 500.
- ⚠ ELECTRÓNICA: raíz `notaFiscalElectronicaCreditoDebito` + `ds:Signature`.

### 4.1 Cabecera (orden exacto)

| # | Elemento | Tipo XSD y restricción | Nil | Oblig. | Descripción / regla [P-NCD] |
|---|---|---|---|---|---|
| 1 | `nitEmisor` | integer 1 … 9999999999999 | No | Sí | NIT del emisor. |
| 2 | `razonSocialEmisor` | string 1–200 | No | Sí | |
| 3 | `municipio` | string 1–25 | No | Sí | "Lugar registrado en el Padrón Nacional de contribuyentes." |
| 4 | `telefono` | string 1–25 | Sí | No | |
| 5 | `numeroNotaCreditoDebito` | integer 1 … 9999999999 | No | Sí | "Numeración asignado a la Nota Crédito Débito." |
| 6 | `cuf` | string 1–100 | No | Sí | CUF de la nota. |
| 7 | `cufd` | string 1–100 | No | Sí | |
| 8 | `codigoSucursal` | integer 0 … 9999 | No | Sí | 0 = casa matriz. |
| 9 | `direccion` | string 1–500 | No | Sí | |
| 10 | `codigoPuntoVenta` | integer 0 … 9999 | Sí | No | |
| 11 | `fechaEmision` | dateTime | No | Sí | Fecha/hora de emisión de la nota. |
| 12 | `nombreRazonSocial` | string 1–500 | Sí | No | |
| 13 | `codigoTipoDocumentoIdentidad` | integer **1 … 5** | No | Sí | La página dice "valores que van del 1 al 9", pero **el XSD solo admite 1–5** (el valor 9 falla; probado). |
| 14 | `numeroDocumento` | string 1–20 | No | Sí | |
| 15 | `complemento` | string máx. 5 | Sí | No | Igual que en factura. |
| 16 | `codigoCliente` | string 1–100 | No | Sí | |
| 17 | `numeroFactura` | integer 1 … 9999999999 | No | Sí | Número de la **factura original**. |
| 18 | `numeroAutorizacionCuf` | string 1–100 | No | Sí | "Número de Cuf de la factura original o Código de Autorización si es una factura manual o computarizada SFV." |
| 19 | `fechaEmisionFactura` | dateTime | No | Sí | Fecha de emisión de la factura original, formato UTC Extendido (ej. `2019-02-13T08:32:12.215`). |
| 20 | `montoTotalOriginal` | `dec(17,2)` > 0 | No | Sí | "Monto total Sujeto a Crédito fiscal en la factura Original." (Ver fórmula N1: es Σ subTotal de líneas con transacción 1.) |
| 21 | `montoTotalDevuelto` | `dec(17,2)` > 0 | No | Sí | "Monto total que está siendo devuelto." |
| 22 | `montoDescuentoCreditoDebito` | `dec(17,2)` ≥ 0 | **Sí** | Sí (página) | "Monto prorrateado del descuento adicional efectuado en la factura original". La página dice obligatorio, el XSD lo permite nil y el ejemplo lo envía nil. |
| 23 | `montoEfectivoCreditoDebito` | `dec(17,2)` > 0 | No | Sí | "Trece por ciento (13%) del monto total devuelto." |
| 24 | `codigoExcepcion` | integer 0 … 1 | Sí | No | "Valor que permite el registro de NIT con error. Por defecto, enviar cero (0) o nulo y uno (1) cuando se autorice el NIT." |
| 25 | `leyenda` | string 1–200 | No | Sí | |
| 26 | `usuario` | string 1–100 | No | Sí | |
| 27 | `codigoDocumentoSector` | integer **fixed="24"** | No | Sí | "Para este tipo de factura este valor es 24." |

**No existen** en la nota: `codigoMetodoPago`, `numeroTarjeta`, `montoTotal`, `montoTotalSujetoIva`, `codigoMoneda`, `tipoCambio`, `montoTotalMoneda`, `montoGiftCard`, `descuentoAdicional`, **`cafc`**.

### 4.2 Detalle (2 a 500, orden exacto)

| # | Elemento | Tipo XSD y restricción | Nil | Oblig. | Descripción |
|---|---|---|---|---|---|
| 1 | `actividadEconomica` | string 1–10 | No | Sí | |
| 2 | `codigoProductoSin` | integer 1 … 99999999 | No | Sí | |
| 3 | `codigoProducto` | string 1–50 | No | Sí | |
| 4 | `descripcion` | string 1–500 | No | Sí | |
| 5 | `cantidad` | **`dec(25,10)`** > 0 | No | Sí | Hasta 10 decimales en el XSD. |
| 6 | `unidadMedida` | integer 1 … 200 | No | Sí | |
| 7 | `precioUnitario` | **`dec(25,10)`** > 0 | No | Sí | |
| 8 | `montoDescuento` | **`dec(25,10)`** ≥ 0 | Sí | No | "Si no aplica deberá ser nulo." |
| 9 | `subTotal` | **`dec(25,10)`** > 0 | No | Sí | `(cantidad × precioUnitario) − descuento`. |
| 10 | `codigoDetalleTransaccion` | integer 1 … 2 | No | Sí | "transacción original (enviar **1**) o del monto que está siendo devuelto (enviar **2**)." |

Notas de [P-NCD]:
- "Cuando se elabora una Nota Débito Crédito para una factura emitida en el SFV, se debe considerar que el redondeo en el detalle utiliza **5 decimales**, pero cuando se aplica a una factura electrónica se utilizan **2 decimales**, excepto en los documentos sector Exportación, libre consignación y demás sectores especiales."
- "El artículo 36 Inc. a) de la RND Nº102100000011 indica que la emisión de una Nota de Crédito – Débito debe realizarse bajo la modalidad de facturación vigente asociada al Contribuyente, no pudiendo emitirse documentos de este tipo utilizando otras modalidades." → M-INV (computarizada) emite notas computarizadas.

### 4.3 Reglas de validación (sector 24) — [VAL1] "NOTA CREDITO DEBITO"

| # | Fórmula | Condición / nota |
|---|---|---|
| N1 | `montoTotalOriginal = Σ(subTotal)` | de las líneas con `codigoDetalleTransaccion = 1`. "El monto de subtotales con transacción 1 debe ser igual al los subtotales de la factura original." |
| N2 | `montoTotalDevuelto = Σ(subTotal) − montoDescuentoCreditoDebito` | de las líneas con `codigoDetalleTransaccion = 2`. "El campo montoDescuentoCreditoDebito contiene el prorrateo del descuento realizado en la factura original". |
| N3 | `montoEfectivoCreditoDebito = montoTotalDevuelto × 0.13` | |
| N4 | `subTotal = (cantidad × precioUnitario) − montoDescuento` | por línea |

Consecuencias de diseño: la nota lleva **todas las líneas de la factura original** (transacción 1, con sus mismos subtotales) **más** las líneas devueltas (transacción 2). Por eso `detalle` minOccurs = 2.

### 4.4 XML de ejemplo oficial (computarizada, sector 24)

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<notaFiscalComputarizadaCreditoDebito xsi:noNamespaceSchemaLocation="notaComputarizadaCreditoDebito.xsd" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
    <cabecera>
        <nitEmisor>1003579028</nitEmisor>
        <razonSocialEmisor>ENTEL</razonSocialEmisor>
        <municipio>La Paz</municipio>
        <telefono>2846005</telefono>
        <numeroNotaCreditoDebito>1</numeroNotaCreditoDebito>
        <cuf>44AAEC00DBD34C53C3E5B135433591A5FA086F86A867A75AC82F24C74</cuf>
        <cufd>BQUE+QytqQUDBKVUFOSVRPQkxVRFZNVFVJBMDAwMDAwM</cufd>
        <codigoSucursal>0</codigoSucursal>
        <direccion>AV. JORGE LOPEZ #123</direccion>
        <codigoPuntoVenta>0</codigoPuntoVenta>
        <fechaEmision>2021-10-06T16:03:49.570</fechaEmision>
        <nombreRazonSocial>Juan Valdez</nombreRazonSocial>
        <codigoTipoDocumentoIdentidad>1</codigoTipoDocumentoIdentidad>
        <numeroDocumento>5115889</numeroDocumento>
        <complemento xsi:nil="true"/>
        <codigoCliente>51158891</codigoCliente>
        <numeroFactura>1</numeroFactura>
        <numeroAutorizacionCuf>dsa564dsa54d6as5</numeroAutorizacionCuf>
        <fechaEmisionFactura>3919-02-01T10:14:36.000</fechaEmisionFactura>
        <montoTotalOriginal>775.00</montoTotalOriginal>
        <montoTotalDevuelto>75.00</montoTotalDevuelto>
        <montoDescuentoCreditoDebito xsi:nil="true"/>
        <montoEfectivoCreditoDebito>9.75</montoEfectivoCreditoDebito>
        <codigoExcepcion xsi:nil="true"/>
        <leyenda>Ley N° 453: Los servicios deben suministrarse en condiciones de inocuidad, calidad y seguridad.
        </leyenda>
        <usuario>vjcm</usuario>
        <codigoDocumentoSector>24</codigoDocumentoSector>
    </cabecera>
    <detalle>
        <actividadEconomica>451010</actividadEconomica>
        <codigoProductoSin>49111</codigoProductoSin>
        <codigoProducto>123456</codigoProducto>
        <descripcion>Amortiguadores</descripcion>
        <cantidad>1</cantidad>
        <unidadMedida>1</unidadMedida>
        <precioUnitario>775.00</precioUnitario>
        <montoDescuento xsi:nil="true"/>
        <subTotal>775.00</subTotal>
        <codigoDetalleTransaccion>1</codigoDetalleTransaccion>
    </detalle>
    <detalle>
        <actividadEconomica>451010</actividadEconomica>
        <codigoProductoSin>49111</codigoProductoSin>
        <codigoProducto>123456</codigoProducto>
        <descripcion>Tornillos</descripcion>
        <cantidad>1</cantidad>
        <unidadMedida>1</unidadMedida>
        <precioUnitario>75.00</precioUnitario>
        <montoDescuento xsi:nil="true"/>
        <subTotal>75.00</subTotal>
        <codigoDetalleTransaccion>2</codigoDetalleTransaccion>
    </detalle>
</notaFiscalComputarizadaCreditoDebito>
```

Cálculo: N1 → 775.00 (única línea trans. 1); N2 → 75.00 − 0 (nil) = 75.00; N3 → 75.00 × 0.13 = **9.75**. (La `fechaEmisionFactura` 3919-… y el `numeroAutorizacionCuf` del ejemplo son valores ficticios.) Nótese que en el ejemplo la línea devuelta ("Tornillos") no figura como línea de la factura original; el ejemplo es ilustrativo y contradice la nota de N1 — ver Dudas.

---

## 5. Nota Crédito-Débito Descuento — `notaComputarizadaCreditoDebitoDescuento` (codigoDocumentoSector = 47)

Fuente: [XSD-NCDD], [P-NCDD], [VAL2] "NOTA CRÉDITO DÉBITO DESCUENTO", [X-VER] 2023.

- **Elemento raíz**: `notaComputarizadaCreditoDebitoDescuento` (**sin** "Fiscal").
- `xsi:noNamespaceSchemaLocation="notaComputarizadaCreditoDebitoDescuento.xsd"`
- `detalle` minOccurs = 2, maxOccurs = 500.
- ⚠ ELECTRÓNICA: raíz `notaElectronicaCreditoDebitoDescuento` + `ds:Signature`.

### 5.1 Diferencias exactas con la nota sector 24 (resultado de `diff` entre XSD)

| Cambio | Sector 24 | Sector 47 |
|---|---|---|
| Raíz | `notaFiscalComputarizadaCreditoDebito` | `notaComputarizadaCreditoDebitoDescuento` |
| Campo nuevo en cabecera | — | `descuentoAdicional`, `dec(17,2)` ≥ 0, **nillable**, ubicado **entre `montoTotalOriginal` y `montoTotalDevuelto`**. [P-NCDD]: "Descuento Adicional otorgado en la factura original", Oblig. No. |
| `codigoDocumentoSector` | fixed 24 | **fixed 47** |
| Campo nuevo en detalle | — | `nroItem`, integer 1 … 9999, **primer** elemento de cada `detalle`. [P-NCDD]: "Número correlativo del producto expresado en el detalle de la factura original y número de registro sujeto a devolución en la nota crédito débito". Oblig. Sí. |
| `detalle/subTotal` | > 0 (minExclusive) | **≥ 0** (minInclusive) — [X-VER] 2023: "Se ajusto el XSD de la nota Crédito Débito Descuento, para permitir la aplicación de este documento a facturas de Compra Venta Bonificaciones." |

Todo lo demás es idéntico (mismos tipos, `codigoTipoDocumentoIdentidad` 1–5 en XSD aunque la página diga 1–9, detalle con `dec(25,10)`, sin `cafc`).

Orden completo de la cabecera 47: `nitEmisor, razonSocialEmisor, municipio, telefono, numeroNotaCreditoDebito, cuf, cufd, codigoSucursal, direccion, codigoPuntoVenta, fechaEmision, nombreRazonSocial, codigoTipoDocumentoIdentidad, numeroDocumento, complemento, codigoCliente, numeroFactura, numeroAutorizacionCuf, fechaEmisionFactura, montoTotalOriginal, descuentoAdicional, montoTotalDevuelto, montoDescuentoCreditoDebito, montoEfectivoCreditoDebito, codigoExcepcion, leyenda, usuario, codigoDocumentoSector`.

Orden del detalle 47: `nroItem, actividadEconomica, codigoProductoSin, codigoProducto, descripcion, cantidad, unidadMedida, precioUnitario, montoDescuento, subTotal, codigoDetalleTransaccion`.

### 5.2 Reglas de validación (sector 47) — [VAL2]

| # | Fórmula | Condición |
|---|---|---|
| D1 | `montoTotalOriginal = Σ(subTotal)` | líneas con `codigoDetalleTransaccion = 1`; deben ser iguales a los subtotales de la factura original. |
| D2 | `subTotal = (cantidad × precioUnitario) − montoDescuento` | por línea |
| D3 | `MontoDescuentoDébitoCrédito = Σ descuentoItem` | líneas con `codigoDetalleTransaccion = 2` |
| D4 | `descuentoItem = (((subTotal × descuentoAdicional) / montoTotalOriginal) / cantidadOriginal) × cantidadDevuelta` | por línea devuelta |
| D5 | `MontoTotalDevuelto = Σ subTotalDevuelto − MontoDescuentoDebitoCredito` | líneas con transacción 2 |
| D6 | `MontoEfectivoDebitoCredito = MontoTotalDevuelto × 0.13` | |
| D7 | `subTotalDevuelto = (cantidad × precioUnitario) − montoDescuento` | líneas con transacción 2 |

Nota: `montoTotalOriginal` es la **suma de subtotales antes del descuento adicional** (en el ejemplo 850.00, aunque la factura original habría tenido `montoTotal` = 850 − 50 = 800).

### 5.3 XML de ejemplo oficial (computarizada, sector 47) y verificación numérica

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<notaComputarizadaCreditoDebitoDescuento xsi:noNamespaceSchemaLocation="notaComputarizadaCreditoDebitoDescuento.xsd" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
    <cabecera>
        <nitEmisor>1003579028</nitEmisor>
        <razonSocialEmisor>ENTEL</razonSocialEmisor>
        <municipio>La Paz</municipio>
        <telefono>2846005</telefono>
        <numeroNotaCreditoDebito>1</numeroNotaCreditoDebito>
        <cuf>44AAEC00DBD34C53C3E5B135433591A5FA086F86A867A75AC82F24C74</cuf>
        <cufd>BQUE+QytqQUDBKVUFOSVRPQkxVRFZNVFVJBMDAwMDAwM</cufd>
        <codigoSucursal>0</codigoSucursal>
        <direccion>AV. JORGE LOPEZ #123</direccion>
        <codigoPuntoVenta>0</codigoPuntoVenta>
        <fechaEmision>2021-10-06T16:03:49.570</fechaEmision>
        <nombreRazonSocial>Juan Valdez</nombreRazonSocial>
        <codigoTipoDocumentoIdentidad>1</codigoTipoDocumentoIdentidad>
        <numeroDocumento>5115889</numeroDocumento>
        <complemento xsi:nil="true"/>
        <codigoCliente>51158891</codigoCliente>
        <numeroFactura>1</numeroFactura>
        <numeroAutorizacionCuf>dsa564dsa54d6as5</numeroAutorizacionCuf>
        <fechaEmisionFactura>3919-02-01T10:14:36.000</fechaEmisionFactura>
        <montoTotalOriginal>850.00</montoTotalOriginal>
        <descuentoAdicional>50</descuentoAdicional>
        <montoTotalDevuelto>70.59</montoTotalDevuelto>
        <montoDescuentoCreditoDebito>4.41 </montoDescuentoCreditoDebito>
        <montoEfectivoCreditoDebito>9.18</montoEfectivoCreditoDebito>
        <codigoExcepcion xsi:nil="true"/>
        <leyenda>Ley N° 453: Los servicios deben suministrarse en condiciones de inocuidad, calidad y seguridad.
        </leyenda>
        <usuario>vjcm</usuario>
        <codigoDocumentoSector>47</codigoDocumentoSector>
    </cabecera>
    <detalle>
        <nroItem>1</nroItem>
        <actividadEconomica>451010</actividadEconomica>
        <codigoProductoSin>49111</codigoProductoSin>
        <codigoProducto>123456</codigoProducto>
        <descripcion>Amortiguadores</descripcion>
        <cantidad>1</cantidad>
        <unidadMedida>1</unidadMedida>
        <precioUnitario>775.00</precioUnitario>
        <montoDescuento xsi:nil="true"/>
        <subTotal>775.00</subTotal>
        <codigoDetalleTransaccion>1</codigoDetalleTransaccion>
    </detalle>
     <detalle>
        <nroItem>2</nroItem>
        <actividadEconomica>451010</actividadEconomica>
        <codigoProductoSin>49111</codigoProductoSin>
        <codigoProducto>123456</codigoProducto>
        <descripcion>Tornillos</descripcion>
        <cantidad>1</cantidad>
        <unidadMedida>1</unidadMedida>
        <precioUnitario>75.00</precioUnitario>
        <montoDescuento xsi:nil="true"/>
        <subTotal>75.00</subTotal>
        <codigoDetalleTransaccion>1</codigoDetalleTransaccion>
    </detalle>
    <detalle>
        <nroItem>2</nroItem>
        <actividadEconomica>451010</actividadEconomica>
        <codigoProductoSin>49111</codigoProductoSin>
        <codigoProducto>123456</codigoProducto>
        <descripcion>Tornillos</descripcion>
        <cantidad>1</cantidad>
        <unidadMedida>1</unidadMedida>
        <precioUnitario>75.00</precioUnitario>
        <montoDescuento xsi:nil="true"/>
        <subTotal>75.00</subTotal>
        <codigoDetalleTransaccion>2</codigoDetalleTransaccion>
    </detalle>
</notaComputarizadaCreditoDebitoDescuento>
```

Verificación con las fórmulas:
- D1: 775.00 + 75.00 = **850.00** ✓
- D4 (línea devuelta nroItem 2): ((75.00 × 50) / 850.00) / 1 × 1 = 4.4117647… → **4.41** ✓
- D5: 75.00 − 4.41 = **70.59** ✓
- D6: 70.59 × 0.13 = 9.1767 → **9.18** ✓ (redondeo HALF-UP a 2)
- El `nroItem` de la línea devuelta (2) repite el `nroItem` de la línea original a la que corresponde.
- El ejemplo tiene un espacio final en `4.41 ` (el XSD colapsa espacios en decimales; **recomendación**: no emitir espacios).

---

## 6. Variantes de factura del bloque — tabla de diferencias frente a Compra Venta (sector 1)

Fuentes: `diff` de cada XSD contra [XSD-CV]; páginas [P-TC], [P-ME], [P-ALQ], [P-BON]; fórmulas [VAL1]/[VAL2].

| Aspecto | **1 Compra Venta** | **8 Tasa Cero** | **9 Moneda Extranjera** | **2 Alquiler Bienes Inmuebles** | **35 Compra Venta Bonificaciones** |
|---|---|---|---|---|---|
| Uso | Bienes/servicios en general | "alcanzados por el Régimen Tasa Cero en el IVA. Para la venta de libros nacionales o importados y publicaciones oficiales. Por el transporte internacional de carga por carretera." | "transacciones de compra y venta de moneda extranjera" | "alquiler de bienes inmuebles propios" | Igual que sector 1; "Permite descuento total en algunos de los productos en el detalle." |
| Elemento raíz | `facturaComputarizadaCompraVenta` | `facturaComputarizadaTasaCero` | `facturaComputarizadaMonedaExtranjera` | `facturaComputarizadaAlquilerBienInmueble` | `facturaComputarizadaCompraVentaBon` |
| XSD / versión | ver. 23/08/2021 | ver. 23/08/2021 | ver. 23/08/2021 | ver. 23/08/2021 | **ver. 19/11/2021** |
| `codigoDocumentoSector` fixed | 1 | 8 | 9 | 2 | 35 |
| Campos extra en cabecera | — | — | `codigoTipoOperacion` (integer 1–2; 1 = venta, 2 = compra) **después de `codigoCliente`**; `ingresoDiferenciaCambio` (`dec(17,2)` ≥ 0) **después de `montoTotalSujetoIva`**; `tipoCambioOficial` (`dec(20,5)` > 0) **después de `usuario`** | `periodoFacturado` (string 1–50, ej. `MAYO 2019`) **después de `codigoCliente`** | — |
| `montoTotalSujetoIva` | `dec(17,2)` ≥ 0 | `xs:decimal` **fixed="0"** (página: "seteado a cero (0) por defecto"; página dice Oblig. "No" pero el elemento **debe estar**) | `xs:decimal` **fixed="0"** (idem) | `dec(17,2)` **> 0** (minExclusive) | `dec(17,2)` ≥ 0 |
| `montoGiftCard` | Sí (nillable) | Sí (nillable) | **No existe** | **No existe** | Sí (nillable) |
| `tipoCambio` | `dec(17,2)` | `dec(17,2)` | **`dec(20,5)`** | `dec(17,2)` | `dec(17,2)` |
| Detalle `cantidad`, `precioUnitario`, `montoDescuento`, `subTotal` | `dec(17,2)` | `dec(17,2)` | **`dec(20,5)`** | `dec(17,2)` | **`dec(20,5)`** |
| Detalle `subTotal` | > 0 | > 0 | > 0 | > 0 | **≥ 0** ("Permite Cero") |
| `numeroSerie`, `numeroImei` | Sí | **No existen** | **No existen** | **No existen** | Sí |
| Fórmula `montoTotal` | Σ subTotal − descuentoAdicional | idem | idem | idem | idem |
| Fórmula `montoTotalMoneda` | montoTotal / tipoCambio | idem | idem | idem | idem |
| Fórmula `montoTotalSujetoIva` | montoTotal − montoGiftCard (si gift card) / montoTotal | 0 fijo | 0 fijo | montoTotal ("No tiene monto gift card") | montoTotal − montoGiftCard (si gift card) / montoTotal |
| Fórmula extra | — | — | Venta (`codigoTipoOperacion = 1`): `ingresoDiferenciaCambio = tipoCambio − tipoCambioOficial`; Compra (`= 2`): `ingresoDiferenciaCambio = tipoCambioOficial − tipoCambio`. Página: "Expresada como valor absoluto." | — | — |
| Nota de uso | Servicios: cantidad 1, unidad 58 | — | [P-ME] `tipoCambioOficial`: "si el código de moneda es boliviano (688) deberá ser igual a 1. Debe ser mayor a 0 y puede contener hasta 20 dígitos en la parte entera y 2 decimales" (el XSD dice 5 decimales) | "En el caso de alquileres, se debe considerar en cantidad consignar 1, en unidad de medida consignar 58 (unidad servicio)" | Mismas notas que sector 1 (servicios, serie/IMEI, Guía de Tránsito) |

Todos los demás campos, tipos y el orden coinciden con el sector 1.

### 6.1 Ejemplos oficiales de las variantes (fragmentos relevantes; todos validan contra su XSD)

**Tasa Cero (8)** — `montoTotal 100`, `montoTotalSujetoIva 0`, `descuentoAdicional nil`, `montoGiftCard nil`, `codigoTipoDocumentoIdentidad 5`, `numeroDocumento 9112023019`, `nombreRazonSocial "Venta menores del dia"`; un detalle "Libro de Matemáticas" 1 × 100 = 100 sin `numeroSerie`/`numeroImei`.

**Moneda Extranjera (9)** — cabecera (orden relevante):
```xml
        <codigoCliente>51158891</codigoCliente>
        <codigoTipoOperacion>1</codigoTipoOperacion>
        <codigoMetodoPago>1</codigoMetodoPago>
        <numeroTarjeta xsi:nil="true"/>
        <montoTotal>837.60</montoTotal>
        <montoTotalSujetoIva>0</montoTotalSujetoIva>
        <ingresoDiferenciaCambio>0.02</ingresoDiferenciaCambio>
        <codigoMoneda>1</codigoMoneda>
        <tipoCambio>6.98</tipoCambio>
        <montoTotalMoneda>120</montoTotalMoneda>
        <descuentoAdicional xsi:nil="true"/>
        <codigoExcepcion xsi:nil="true"/>
        <cafc xsi:nil="true"/>
        <leyenda>Los servicios deben suministrarse en condiciones de inocuidad, calidad y seguridad</leyenda>
        <usuario>vjcm</usuario>
        <tipoCambioOficial>6.96</tipoCambioOficial>
        <codigoDocumentoSector>9</codigoDocumentoSector>
```
Detalle: "Venta de Moneda Dolares Americanos (USD)", `cantidad 120`, `precioUnitario 6.98`, `montoDescuento 0`, `subTotal 837.60`. Verificación: 120 × 6.98 = 837.60; `montoTotalMoneda` = 837.60 / 6.98 = 120; `ingresoDiferenciaCambio` = 6.98 − 6.96 = 0.02 (venta). **Ojo**: el ejemplo usa `codigoMoneda 1` con `tipoCambio 6.98`, mientras el ejemplo de Compra Venta usa `codigoMoneda 1` con `tipoCambio 1` (ver Dudas).

**Alquiler (2)** — `periodoFacturado MAYO 2019` tras `codigoCliente`; `montoTotal 100`, `montoTotalSujetoIva 100`, sin `montoGiftCard`, `codigoExcepcion 0`, `codigoTipoDocumentoIdentidad 5`, `nombreRazonSocial "Control Tributario"`, `leyenda "Una leyenda"`; detalle "Alquiler mes de Febrero" 1 × 100.

**Bonificaciones (35)** — dos líneas: "Producto 1" 1 × 100 con `montoDescuento 100` → `subTotal 0` (con `numeroSerie`/`numeroImei`), "Producto 2" 1 × 50 → `subTotal 50` (serie/IMEI nil). `descuentoAdicional 0` → `montoTotal = 0 + 50 − 0 = 50`, `montoTotalSujetoIva 50`, `montoTotalMoneda 50`.

---

## 7. Nota de Conciliación — `notaComputarizadaConciliacion` (codigoDocumentoSector = 29)

Fuente: [XSD-CONC], [P-CONC], [VAL1] "NOTA CONCILIACIÓN". Uso [P-CONC]: "ajustes en el Crédito y en el Débito Fiscal … por transacciones facturadas en periodos anteriores no mayores a doce (12) meses, por servicios de energía eléctrica, telecomunicaciones, agua potable e hidrocarburos." **No aplica al negocio de M-INV** (venta de bienes); se documenta porque está en el bloque.

- Raíz `notaComputarizadaConciliacion`; `xsi:noNamespaceSchemaLocation="notaComputarizadaConciliacion.xsd"`. ⚠ ELECTRÓNICA: `notaElectronicaConciliacion` + `ds:Signature`.
- Cabecera (orden): `nitEmisor` (int 1…9999999999999), `razonSocialEmisor` (1–200), `municipio` (1–25), `telefono` (nil; **minLength 0**, máx. 25), `numeroNotaConciliacion` (int 1…9999999999), `cuf` (1–100), `cufd` (1–100), `codigoSucursal` (0–9999), `direccion` (1–500), `codigoPuntoVenta` (nil, 0–9999), `fechaEmision` (dateTime), `nombreRazonSocial` (nil, 1–500), `codigoTipoDocumentoIdentidad` (int **1–9**), `numeroDocumento` (1–20), `complemento` (nil, máx. 5), `codigoCliente` (1–100), `numeroFactura` (int 1…9999999999), `numeroAutorizacionCuf` (1–100), `codigoControl` (nil, máx. 20), `fechaEmisionFactura` (dateTime), `montoTotalOriginal` (`dec(17,2)` > 0), `montoTotalConciliado` (`dec(17,2)` > 0), `creditoFiscalIva` (`dec(17,2)` ≥ 0), `debitoFiscalIva` (`dec(17,2)` ≥ 0), `codigoExcepcion` (nil, 0–1), `leyenda` (1–200), `usuario` (1–100), `codigoDocumentoSector` fixed **29**. (La página lista `debitoFiscalIva` antes de `creditoFiscalIva`; el XSD al revés.)
- `detalleOriginal` (1..∞): `actividadEconomica` (string **1–50**), `codigoProductoSin`, `codigoProducto` (1–50), `descripcion` (1–500), `cantidad` `dec(25,10)` > 0, `unidadMedida` (1–200), `precioUnitario` `dec(25,10)` > 0, `montoDescuento` (nil) `dec(25,10)` ≥ 0, `subTotal` `dec(25,10)` ≥ 0.
- `detalleConciliacion` (1..∞): `actividadEconomica` (1–50), `codigoProductoSin`, `codigoProducto`, `descripcion`, `montoOriginal` `dec(25,10)` ≥ 0, `montoFinal` ≥ 0, `montoConciliado` ≥ 0.
- Fórmulas [VAL1]: `montoTotalConciliado = Σ(montoConciliado)`; `montoConciliado = abs(montoOriginal − montoFinal)` "Actualizado con mantenimiento de valor … la UFV se actualiza a través del Banco Central entre el 5 y 10 de cada mes"; `debitoFiscalIva o creditoFiscalIva = montoTotalConciliado × 0.13`. [P-CONC]: si lo facturado fue **mayor** → Crédito Fiscal IVA; si fue **menor** → Débito Fiscal IVA; el otro se envía en 0.
- Ejemplo: `montoOriginal 775.00`, `montoFinal 725.00` → `montoConciliado 50`; `montoTotalConciliado 50.00`; `creditoFiscalIva 6.50`; `debitoFiscalIva 0`.

---

## 8. Catálogos y códigos que aparecen en el bloque

| Campo | Valores conocidos en el bloque | Rango XSD | Fuente del catálogo completo |
|---|---|---|---|
| `codigoDocumentoSector` | 1 Compra Venta, 2 Alquiler, 8 Tasa Cero, 9 Moneda Extranjera, 24 Nota Crédito-Débito, 29 Nota de Conciliación, 35 Compra Venta Bonificaciones, 47 Nota Crédito-Débito Descuento | fixed por XSD | páginas del bloque |
| `codigoTipoDocumentoIdentidad` | 1 = CI ("Ejemplo 1 que representa al CI"). Los ejemplos usan 5 con números tipo NIT (`9112023019`, `1020703023`), pero el significado de 5 **no está escrito** en el bloque. | facturas 1–5; notas 24/47 1–5; conciliación 1–9 | **NO DOCUMENTADO** (sincronización de catálogos) |
| `codigoMetodoPago` | 1 = efectivo; 2 = Tarjeta; 5 = Otros | 1–308 | **NO DOCUMENTADO** (sincronización). Código de Gift Card: NO DOCUMENTADO. |
| `unidadMedida` | 58 = unidad servicio | 1–200 | **NO DOCUMENTADO** (sincronización) |
| `codigoMoneda` | Ejemplos: 1. [P-ME] dice "boliviano (688)". | 1–154 | **NO DOCUMENTADO** (contradictorio, ver Dudas) |
| `codigoExcepcion` | 0 (o nil) = por defecto; 1 = autorizar NIT inválido | 0–1 | bloque |
| `codigoDetalleTransaccion` | 1 = transacción original; 2 = monto devuelto | 1–2 | bloque |
| `codigoTipoOperacion` (sector 9) | 1 = venta; 2 = compra | 1–2 | bloque |
| `codigoSucursal` | 0 = casa matriz | 0–9999 | bloque |
| `actividadEconomica`, `codigoProductoSin`, `leyenda` | ej. `451010`, `49111`; leyendas "Ley N° 453: …" | — | **NO DOCUMENTADO** (sincronización) |

Nota de [X-VER] 2021 (fuera del bloque): cuando el SIN agrega ítems a catálogos (métodos de pago, unidades de medida…), "se evitará realizar este tipo de ajustes a los XSD, debiendo el contribuyente realizar los mismos directamente en sus sistemas". → M-INV debe poder **ampliar los rangos `maxInclusive`** de sus copias de XSD si un código de catálogo nuevo excede el rango publicado.

---

## 9. Precisión decimal por documento (resumen para el motor de cálculo)

| Documento | Montos de cabecera | `tipoCambio` | Detalle (cantidad, precioUnitario, montoDescuento, subTotal) |
|---|---|---|---|
| 1 Compra Venta | 2 dec (17 dígitos) | 2 dec | **2 dec** (17 dígitos) |
| 2 Alquiler | 2 dec | 2 dec | 2 dec |
| 8 Tasa Cero | 2 dec | 2 dec | 2 dec |
| 9 Moneda Extranjera | 2 dec | **5 dec** (20 dígitos); `tipoCambioOficial` 5 dec | **5 dec** (20 dígitos) |
| 35 Bonificaciones | 2 dec | 2 dec | **5 dec** (20 dígitos) |
| 24 / 47 Notas C-D | 2 dec | — | **10 dec** (25 dígitos) |
| 29 Conciliación | 2 dec | — | **10 dec** (25 dígitos) |

- [X-VER] 2021 (fuera del bloque): "se amplio la cantidad de números decimales de 2 a 5 en las facturas de Compra y Venta de Moneda Extranjera, alcanzada por ICE e alcanzada Ice Zona Franca" — Compra Venta (1) **no** se amplió; su XSD publicado sigue con 2 decimales en cantidad/precio.
- Para notas de facturas **SFV** el detalle se redondea a 5 decimales; para facturas electrónicas/en línea a 2 [P-NCD].
- `fractionDigits` limita los decimales del **valor**: `99.10` es válido (valor 99.1); `99.123` no.

---

## 10. Resultados de validación con .NET `XmlSchemaSet` (casos límite probados)

Validación: `XmlReaderSettings { ValidationType = Schema }`, esquema cargado con `Schemas.Add(null, rutaXsd)`.

| Caso (sobre el XML oficial) | Resultado |
|---|---|
| Los 8 XML de ejemplo computarizados | Válidos |
| Omitir `<telefono>` (nillable) | **Error**: se esperaba `telefono` → los nillable deben estar presentes |
| Omitir `<codigoPuntoVenta>` | **Error** |
| `<telefono></telefono>` | **Error** (minLength 1) |
| `<complemento></complemento>` | Válido (sin minLength) — igual usar `xsi:nil` |
| `montoTotal 99.123` | **Error** FractionDigits |
| `montoTotal 99.10` | Válido |
| `cantidad 1.12345` en sector 1 | **Error** FractionDigits |
| `cantidad 1.12345` en sector 35 | Válido |
| `cantidad 1.1234567891` en nota 24 | Válido |
| `codigoDocumentoSector 2` en XSD sector 1 | **Error** valor fijo |
| `montoGiftCard` antes de `montoTotal` | **Error** de orden |
| `fechaEmision` con sufijo `Z` | Válido por XSD (los ejemplos no lo usan) |
| `numeroTarjeta 4797000000007896` | Válido |
| `subTotal 0` en sector 1 | **Error** MinExclusive |
| `montoDescuento xsi:nil` / `codigoExcepcion 0` / `nombreRazonSocial xsi:nil` | Válidos |
| `codigoTipoDocumentoIdentidad 6` en sector 1 | **Error** MaxInclusive |
| `leyenda xsi:nil` | **Error** (no nillable) |
| Sin ningún `detalle` | **Error** |
| 500 `detalle` / 501 `detalle` | Válido / **Error** |
| `precioUnitario 1E2` o `100,50` | **Error** (no es xs:decimal) |
| Tasa Cero `montoTotalSujetoIva 0.00` / `100` / omitido | Válido / **Error** valor fijo / **Error** |
| Tasa Cero con `numeroSerie` | **Error** (no existe) |
| Alquiler `montoTotalSujetoIva 0` | **Error** MinExclusive |
| Alquiler con `montoGiftCard` | **Error** (no existe) |
| Nota 24 con 1 solo `detalle` | **Error** (minOccurs 2) |
| Nota 24 `codigoTipoDocumentoIdentidad 9` | **Error** (máx. 5) |
| Nota 24 `montoDescuentoCreditoDebito 0` | Válido |
| Nota 24 `codigoDetalleTransaccion 3` | **Error** |
| Conciliación `codigoTipoDocumentoIdentidad 9` / `telefono` vacío | Válido / Válido |
| Raíz con `xmlns="http://x"` (namespace por defecto) | **Pasa "válido" en silencio** si no se activa `XmlSchemaValidationFlags.ReportValidationWarnings`; con el flag, aparecen advertencias "No se puede encontrar la información de esquema". → **Activar ese flag y tratar las advertencias como error.** |

---

## 11. Códigos de error SIAT relacionados con el contenido del XML (referencia cruzada [X-ERR], fuera del bloque)

Útiles para mapear rechazos a mensajes de usuario; la especificación de errores está en otro bloque.

| Código | Mensaje | Regla de este documento |
|---|---|---|
| 939 | La Factura o Nota De Crédito - Débito No Cumple Con El Formato Del Xsd Especificado | Secciones 1, 2, 4–7, 10 |
| 1001 | El NIT Enviado En El XML Es Inexistente O No Corresponde Al Cufd | `nitEmisor` |
| 1002 / 1003 | CUF / CUFD enviado en el XML inválido | `cuf`, `cufd` |
| 1004 | La Sucursal Enviada En El XML No Corresponde A Los Datos Del Cufd | `codigoSucursal` |
| 1007 | La Dirección Enviada En El XML No Corresponde A La Registrada En Padrón | `direccion` |
| 1008 | El Punto De Venta Enviado En El XML Es Inexistente O Invalido | `codigoPuntoVenta` |
| 1009 | La Fecha De Emisión Enviada En El XML No Es Valida Para Emisión En Linea | `fechaEmision` |
| 1011 | El Complemento Solo Puede Ser Enviado Cuando El Tipo De Documento Es Carnet De Identidad | R3 |
| 1012 | El Numero De Tarjeta Solo Puede Ser Enviado Cuando El Método De Pago Sea Con Tarjeta | R2 |
| 1013 / 1014 / 1018 | Cálculo erróneo de Monto Total / Monto Total Moneda / Subtotal | V2 / V3 / V1 |
| 1016 / 1017 | Actividad económica no habilitada / Código de producto no relacionado a actividad | `actividadEconomica`, `codigoProductoSin` |
| 1027 | El Monto De Diferencia De Cambios Es Erróneo | sector 9 |
| 1029 / 1030 / 1031 | Monto Total Devuelto / Monto Total Original / Monto Efectivo C-D erróneo | N1–N3, D1–D6 |
| 1033 | El Monto Devuelto Es Mayor Al Monto Original | notas |
| 1037 | El Numero Documento De Tipo NIT No Es Valido | R4 |
| 1043 / 1044 | Monto Conciliado / Monto Total Conciliado erróneo | sección 7 |
| 1045–1047 | CAFC no válido / fecha / número para el CAFC | `cafc` |
| 1048 / 1049 / 1061 | Factura de la nota no encontrada / Detalle de la nota diferente al de la factura original / Factura no válida para devolución | notas |
| 1050 | Monto Gift Card No Corresponde Al Método De Pago | V4 |
| 1054 | El Monto Descuento Crédito Débito Es Erróneo | D3/D4 |
| 1056 / 1057 / 1058 | Tipo de Cambio / Monto Total Moneda / Monto Total Sujeto Iva erróneo | R1, V3, V4/V5 |
| 2004 / 2005 / 2006 / 2007 / 2008 / 2012 | Advertencias equivalentes (complemento, NIT cliente no válido, tarjeta, monto total, monto total moneda, subtotal) | — |

Reglas de [X-EMI] (fuera del bloque) que completan R3/R4:
- "Los Códigos Especiales 99001 (Utilizado para consulados, embajadas, etc), el 99002 (Control Tributario) y el 99003 (Ventas Menores del Día) se deben enviar con el tipo de documento NIT y el código de Excepción en 1."
- "Si durante la emisión se utiliza como tipo de documento C.I. o NIT el sistema emisor debe validar que el valor que se envía sea numérico."
- "El código de excepción debe enviarse por defecto con un valor de 0 (cero). Se envía con un valor de 1 (uno) solo si el Tipo de documento es un NIT … si la emisión es en fuera de linea y el tipo de documento NIT siempre enviar el código de excepción con un valor de 1."

---

## 12. Resumen ⚠ ELECTRÓNICA (no implementar ahora)

- Raíces: `facturaElectronicaCompraVenta`, `notaFiscalElectronicaCreditoDebito`, `notaElectronicaCreditoDebitoDescuento`, `facturaElectronicaTasaCero`, `facturaElectronicaMonedaExtranjera`, `facturaElectronicaAlquilerBienInmueble`, `facturaElectronicaCompraVentaBon`, `notaElectronicaConciliacion`; `xsi:noNamespaceSchemaLocation` = `facturaElectronica….xsd` / `nota…Electronica….xsd`.
- XSD: `xs:import` de `../SignatureSchema.xsd` y `<xs:element ref="ds:Signature"/>` al final de la raíz.
- Firma XMLDSig enveloped (algoritmos en sección 1, punto 12).
- Campos de cabecera y detalle: **idénticos** a los computarizados. Para pasar a electrónica bastaría con cambiar el nombre de la raíz, el nombre del XSD y añadir la firma.

---

## 13. Qué debe implementar M-INV (derivado de este bloque)

**Generación del XML**
1. Un generador por tipo de documento (`IGeneradorXmlSiat` con implementaciones para sectores **1** y **24/47** como mínimo; **35** recomendado para promociones con ítems 100 % bonificados; **8/2/9** opcionales). Cada uno emite exactamente el orden de elementos de las tablas 2.1/2.2, 4.1/4.2, 5.1 y 6, con el nombre de raíz exacto (ojo: `notaFiscalComputarizadaCreditoDebito` vs `notaComputarizadaCreditoDebitoDescuento`).
2. Usar `XmlWriter` con UTF-8 sin BOM, `WriteStartDocument(true)` (`standalone="yes"`), raíz sin namespace, atributos `xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"` y `xsi:noNamespaceSchemaLocation="<archivo>.xsd"`.
3. Helper `EscribirNil(nombre)` que emite `<nombre xsi:nil="true"/>` para todo campo nillable sin valor (nunca omitir el elemento, nunca cadena vacía).
4. Formateo numérico con `CultureInfo.InvariantCulture`: montos de cabecera `0.00`; detalle con 2, 5 o 10 decimales según la tabla de la sección 9; enteros sin separadores. Fechas con `yyyy-MM-dd'T'HH:mm:ss.fff`.
5. Recortar/normalizar textos: sin saltos de línea ni espacios sobrantes en `leyenda`; validar longitudes máximas (razón social 200, municipio 25, dirección 500, nombreRazonSocial 500, numeroDocumento 20, complemento 5, codigoCliente 100, codigoProducto 50, descripcion 500, leyenda 200, usuario 100, cafc 50, numeroSerie/Imei 1500, periodoFacturado 50).

**Validación local antes de enviar**
6. Incluir los 8 XSD computarizados como recursos embebidos (`Infrastructure/Siat/Xsd/`) y validar **cada** XML generado con `XmlSchemaSet` con `ReportValidationWarnings` activo; tratar advertencias como error. Si falla, no enviar y mostrar el mensaje (evita el rechazo 939).
7. Permitir ampliar localmente los `maxInclusive` de catálogos (métodos de pago 308, unidades 200, monedas 154, tipo doc 5) si la sincronización trae códigos mayores (nota de [X-VER]).
8. Motor de cálculo `CalculadoraFacturaSiat` en `decimal` con redondeo `MidpointRounding.AwayFromZero` que implemente V1–V5, N1–N4, D1–D7 y las fórmulas de sector 9/29, y que **revalide** los totales antes de firmar/enviar (errores 1013, 1014, 1018, 1029–1031, 1054, 1057, 1058).
9. Pruebas unitarias que regeneren los 8 XML oficiales a partir de datos de dominio y comparen elemento por elemento, y que cubran todos los casos de la sección 10.

**Reglas de captura (pantalla de venta / facturación)**
10. Cliente: tipo de documento (catálogo), número (numérico si CI o NIT — [X-EMI]), `complemento` habilitado **solo** si tipo = CI (1), máx. 5; `nombreRazonSocial` opcional; `codigoCliente` generado por M-INV y persistente por cliente.
11. Método de pago: si es tarjeta (2) pedir número y guardar/emitir **solo enmascarado** (4 primeros + ceros + 4 últimos, 16 dígitos); en cualquier otro método `numeroTarjeta` = nil. Nunca almacenar el PAN completo.
12. Gift card: campo `montoGiftCard` habilitado solo con el método Gift Card (código por catálogo); `montoTotalSujetoIva = montoTotal − montoGiftCard`; validar `montoGiftCard ≤ montoTotal`.
13. Descuentos: por línea (`montoDescuento`, nil si 0 es opcional) y `descuentoAdicional` global; impedir que dejen `subTotal ≤ 0` o `montoTotal ≤ 0` (salvo sector 35, donde `subTotal` puede ser 0).
14. `codigoExcepcion`: 0/nil por defecto; opción explícita "Emitir igualmente a NIT no válido" que envía 1 (solo con tipo NIT), con registro de auditoría de quién lo autorizó.
15. Servicios (productos marcados "servicio"): forzar `cantidad = 1` y `unidadMedida = 58`.
16. Moneda: si BOB → `tipoCambio = 1`, `montoTotalMoneda = montoTotal`; si otra moneda → pedir tipo de cambio y calcular `montoTotalMoneda = montoTotal / tipoCambio`.
17. Productos con serie/IMEI: capturar por línea; si son muchos, marcar la factura para envío por "Recepción Archivos Anexos" (servicio de otro bloque).
18. Límite de 500 líneas por documento: bloquear o dividir la venta en varias facturas.
19. Cantidad y precio en sector 1 con máximo **2 decimales**: si el inventario maneja más decimales, redondear HALF-UP antes de facturar y advertir al usuario.

**Datos a guardar (normalizados)**
20. `ConfiguracionFiscal` por empresa: `nitEmisor`, `razonSocialEmisor`, modalidad, sectores habilitados.
21. `SucursalSiat`: `codigoSucursal` (0 = casa matriz), `municipio`, `direccion` (idéntica al Padrón — error 1007), `telefono`; `PuntoVentaSiat`: `codigoPuntoVenta`.
22. Homologación de producto: `codigoProductoSin`, `actividadEconomica`, `unidadMedida` SIAT, `codigoProducto` propio, flags `requiereSerie` / `requiereImei` / `esServicio`.
23. `DocumentoFiscal` (cabecera) con **todos** los campos de la tabla 2.1 (y los extra de notas/variantes), más: XML generado (texto exacto enviado), versión de XSD usada, huella/hash, estado, respuestas del SIN. `DocumentoFiscalDetalle` con todos los campos de 2.2 / 4.2 (+ `nroItem`, `codigoDetalleTransaccion`).
24. `usuario` del XML = login corto del usuario de M-INV que emite (descriptivo, p. ej. `JPEREZ`), guardado en el documento.
25. `leyenda` usada (texto exacto) guardada en el documento.

**Notas crédito-débito**
26. Pantalla "Nota de crédito/débito": buscar la factura original (número, CUF/código de autorización, fecha), copiar **todas** sus líneas como transacción 1 (subtotales idénticos), elegir líneas y cantidades devueltas (transacción 2, cantidad ≤ original), calcular `montoDescuentoCreditoDebito`, `montoTotalDevuelto`, `montoEfectivoCreditoDebito = 13 %`; impedir devolver más que el original (error 1033).
27. Selección automática de sector: **47** si la factura original tuvo `descuentoAdicional` > 0 o es de sector 35 (incluye `descuentoAdicional` y `nroItem`, con fórmula D4); **24** en otro caso.
28. Integración con inventario: una nota de crédito por devolución debe reingresar el stock de las líneas de transacción 2 (decisión de diseño de M-INV).

**Pantallas**
29. Emisión de factura (POS) con resumen de totales SIAT (`montoTotal`, `montoTotalSujetoIva`, `montoTotalMoneda`), vista previa del XML y resultado de validación XSD.
30. Configuración fiscal (empresa, sucursales SIAT, puntos de venta), homologación de productos y clientes con tipo de documento.
31. Consulta de documentos fiscales emitidos con descarga del XML.

---

## 14. Dudas / huecos de la documentación

1. **`codigoMoneda` de boliviano**: [P-ME] dice "boliviano (688)", pero el XSD limita `codigoMoneda` a 1–154 (688 fallaría) y los ejemplos usan `1` tanto con `tipoCambio 1` (sector 1) como con `tipoCambio 6.98` (sector 9). El código real debe tomarse del catálogo sincronizado; **NO DOCUMENTADO** en el bloque.
2. **`codigoTipoDocumentoIdentidad` en notas 24/47**: la página dice 1–9, el XSD 1–5. La conciliación sí admite 1–9. Qué significa cada valor (salvo 1 = CI) **NO DOCUMENTADO**.
3. **`montoDescuentoCreditoDebito`**: obligatorio según la página, nillable en el XSD; el ejemplo 24 lo envía nil. La nota 24 no tiene `descuentoAdicional` y la fórmula de prorrateo solo está en el sector 47. **No se documenta** cómo calcularlo en el sector 24.
4. **Contenido del detalle de la nota 24**: [VAL1] exige que los subtotales de transacción 1 sean iguales a los de la factura original, pero el ejemplo oficial devuelve "Tornillos" (75.00), que no está entre las líneas de transacción 1 (solo "Amortiguadores" 775.00). Queda la duda de si la línea devuelta debe existir en la original (lo lógico, y lo sugiere el error 1049).
5. **Redondeo en notas 47**: no se dice si `descuentoItem` se redondea por línea antes de sumar (D3) o si se redondea solo el total. El ejemplo tiene una sola línea devuelta.
6. **`ingresoDiferenciaCambio`**: la fórmula da la diferencia **por unidad** (0.02 en el ejemplo de 120 USD), no el total (2.40). No se aclara si es unitario o total. Además la página pide "valor absoluto".
7. **`tipoCambioOficial`**: la página dice 2 decimales, el XSD 5.
8. **`montoTotalSujetoIva` en tasa cero/moneda extranjera**: la página dice "Obligatorio: No", pero el XSD lo exige con valor fijo 0.
9. **Orden de campos**: las tablas de [P-CV] y [P-CONC] no siguen el orden del XSD. Se asume que manda el XSD, que es lo que se valida.
10. **Zona horaria y formato de `fechaEmision`**: "UTC Extendido" sin ejemplo de offset; no se dice si es hora local de Bolivia o UTC. **NO DOCUMENTADO en este bloque**.
11. **BOM / codificación**: solo se ve `encoding="UTF-8"`; la presencia de BOM **NO DOCUMENTADA**.
12. **Huella del XML** (requisito de la modalidad computarizada según [FE]) y **algoritmo del CUF**: **NO DOCUMENTADOS** en este bloque. **[corregido por revisión]** Ambos están documentados en otras páginas: huella = SHA-256 del GZIP (ver §1 punto 1) y CUF en `generacion-cuf.md` (especificado y verificado en `06-…` §1). Además, la fecha del CUF es **exactamente** la `fechaEmision` del XML con milisegundos (verificado decodificando el CUF del XML oficial de compra-venta: `2021-10-06T16:03:48.675` ↔ `20211006160348675`).
13. **Catálogos** (métodos de pago, incluido el código de Gift Card; unidades de medida; monedas; tipos de documento; leyendas por actividad; productos SIN): **NO DOCUMENTADOS** en este bloque; solo aparecen 1 = efectivo, 2 = tarjeta, 5 = otros, 58 = unidad servicio, tipo doc 1 = CI.
14. **`codigoCliente`**: la regla de formación no está documentada; los ejemplos usan `51158891`, que parece `numeroDocumento` (5115889) + `1`, pero es solo una observación.
15. **Decimales en Compra Venta**: el XSD publicado (ver. 23/08/2021) admite 2 decimales en `cantidad`/`precioUnitario`. No se documenta si el SIN acepta hoy más decimales para el sector 1. Solo las notas de facturas SFV mencionan 5 decimales.
16. **Notas en contingencia**: el XSD de las notas 24/47 no tiene `cafc`; cómo emitir una nota fuera de línea **NO DOCUMENTADO en este bloque**.
17. **Discrepancias menores entre XSD**: `telefono` admite cadena vacía solo en conciliación (minLength 0); `actividadEconomica` mide 1–50 en conciliación y 1–10 en el resto.
18. **Ejemplos con datos ficticios**: `fechaEmisionFactura 3919-02-01…` y `numeroAutorizacionCuf dsa564dsa54d6as5` no sirven como referencia de formato real para el CUF de la factura original.
