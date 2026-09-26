# 06 · Algoritmos (CUF, Módulo 11, Base 16, SHA-256, GZIP, redondeo), QR y representación gráfica

Especificación de implementación para M-INV, módulo de **Facturación Computarizada en Línea** del SIAT (modalidad 2).

**Convenciones de este documento**
- **Fuente:** es el archivo de `scratchpad/siat/txt/` (o de `scratchpad/siat/adjuntos/`) del que sale cada dato.
- **NO DOCUMENTADO:** el dato no aparece en los archivos de este bloque. No se inventó nada; donde hago una **propuesta** o una **inferencia**, lo digo expresamente.
- **VERIFICADO:** lo comprobé con un script. El script es `specs/06-anexos/verificar_cuf.py` y se ejecuta con `python verificar_cuf.py`.
- **⚠ ELECTRÓNICA:** aplica solo a la modalidad Electrónica en Línea (modalidad 1). Todo lo demás aplica a la Computarizada.
- **Ref. cruzada:** dato tomado de un archivo **fuera de mi bloque**. Se incluye como apoyo y debe contrastarse con la spec que cubre ese archivo.

Archivos de mi bloque (todos leídos completos):

| # | Archivo | Contenido real |
|---|---|---|
| 1 | `facturacion-en-linea__titulos-y-subtitulos-fel.md` | Tabla de títulos y subtítulos por tipo de documento |
| 2 | `facturacion-en-linea__algoritmos-utilizados__generacion-cuf.md` | Campos del CUF, pasos y ejemplo |
| 3 | `facturacion-en-linea__algoritmos-utilizados__algoritmo-modulo-11.md` | Código Java del Módulo 11. La versión C# es un enlace (`?id=344`) que **no está en el archivo** |
| 4 | `facturacion-en-linea__algoritmos-utilizados__base-16.md` | C#: `CompleteCero`, `Base16`, `Base10` |
| 5 | `facturacion-en-linea__algoritmos-utilizados__codigo-respuesta-rapida-qr.md` | URL del QR, parámetros y dimensión |
| 6 | `facturacion-en-linea__algoritmos-utilizados__comprimir-gzip.md` | Código Java de GZIP |
| 7 | `facturacion-en-linea__algoritmos-utilizados__generacion-de-sha-256-md5-y-crc32.md` | Solo SHA-256. **MD5 y CRC32 figuran en el título pero no tienen contenido** |
| 8 | `facturacion-en-linea__algoritmos-utilizados__algoritmo-de-redondeo.md` | Unas 6 líneas de texto más una imagen PNG en base64 (≈108 KB) que dice "REDONDEAR SIEMPRE HACIA ARRIBA". La extraje a `specs/06-anexos/redondeo-imagen-pagina.png` |
| 9 | `facturacion-manual__algoritmos__qr-sfv.md` | Solo una frase y una imagen (`/images/2021/qrsfv.png`). Como la página estaba vacía en texto, descargué la imagen del sitio oficial y la guardé en `specs/06-anexos/qr-sfv-campos.png` |
| 10 | `facturacion-manual__algoritmos__codigo-de-control.md` | Descripción del Código de Control del SFV antiguo. El PDF de la especificación (`356aea02e.pdf`) **no está disponible**: el adjunto `rnd_356aea02e.pdf` resultó ser una página HTML y no un PDF |
| 11–14 | `adjuntos/factura COMPRA VENTA.pdf`, `Nota CreditoDebito.pdf`, `Nota CreditoDebitoDescuento.pdf`, `factura TasaCero.pdf` | Ejemplos de representación gráfica. Los cuatro son de **1 página tamaño Carta (612×792 pt)** |

---

## 1. Código Único de Factura (CUF)

Fuente: `facturacion-en-linea__algoritmos-utilizados__generacion-cuf.md`

### 1.1 Campos (en este orden exacto, todos numéricos, rellenados con ceros a la izquierda)

| Orden | Campo | Descripción / valores documentados | Tipo | Longitud |
|---|---|---|---|---|
| 1 | NIT (Emisor) | NIT del contribuyente | Numérico | 13 |
| 2 | FECHA/HORA (Emisión) | "Fecha y Hora del Emisor", formato `yyyyMMddHHmmssSSS` (SSS = milisegundos) | Numérico | 17 |
| 3 | SUCURSAL | 0 = Casa Matriz; 1 = Sucursal 1; 2 = Sucursal 2; N = Sucursal N | Numérico | 4 |
| 4 | MODALIDAD | 1 = Electrónica en Línea ⚠ ELECTRÓNICA; **2 = Computarizada en Línea (la de M-INV)**; 3 = Portal Web en Línea | Numérico | 1 |
| 5 | TIPO DE EMISIÓN | 1 = Online; 2 = Offline; 3 = Masiva | Numérico | 1 |
| 6 | TIPO FACTURA / DOCUMENTO AJUSTE | 1 = Factura con Derecho a Crédito Fiscal; 2 = Factura sin Derecho a Crédito Fiscal; 3 = Documento de Ajuste | Numérico | 1 |
| 7 | TIPO DOCUMENTO SECTOR | 1 = Factura Compra Venta; 2 = Recibo de Alquiler de Bienes Inmuebles; "……."; 24 = Nota Crédito - Débito. La lista completa **NO está en esta página** (ref. cruzada: catálogo de tipos de documento sector) | Numérico | 2 |
| 8 | NÚMERO DE FACTURA | Número de factura | Numérico | 10 |
| 9 | PUNTO DE VENTA (POS) | 0 = No corresponde; 1, 2, 3, 4, …, n | Numérico | 4 |
| 10 | CÓDIGO AUTOVERIFICADOR | Módulo 11 | Numérico | 1 |
| | **TOTAL** | | | **54** |

La página dice textualmente: "Todos los campos deben completarse conforme a la longitud indicada."

### 1.2 Algoritmo (pasos de la página)

1. Se rellena cada campo con ceros a la izquierda hasta su longitud.
2. Se concatenan los campos 1 a 9. El resultado es una cadena de **53 dígitos**.
3. Se calcula el dígito Módulo 11 de esa cadena y se agrega al final. Quedan **54 dígitos**.
4. La cadena de 54 dígitos se interpreta como un número entero decimal grande y se convierte a **Base 16** (hexadecimal en MAYÚSCULAS y sin ceros a la izquierda; ver §3).
5. Al resultado se le concatena el **código de control** que devuelve el servicio web `solicitudCufd`: **CUF = HEX(paso 4) + códigoControl(CUFD)**.

### 1.3 Ejemplo oficial, VERIFICADO paso a paso

Datos de la página: NIT 123456789, FECHA/HORA 20190113163721231, SUCURSAL 0, MODALIDAD 1, TIPO EMISIÓN 1, TIPO FACTURA 1, TIPO DOC. SECTOR 1, NÚMERO 1, POS 0. Código de control de ejemplo: `A19E23EF34124CD`.

| Paso | Valor | ¿Cuadra con la página? |
|---|---|---|
| Relleno | `0000123456789` · `20190113163721231` · `0000` · `1` · `1` · `1` · `01` · `0000000001` · `0000` | Sí |
| Cadena de 53 dígitos | `00001234567892019011316372123100001110100000000010000` | **Sí** |
| Módulo 11 | suma ponderada = **472**; 472 mod 11 = **10**. Cuando el resto es 10, el algoritmo agrega **"1"** | Sí |
| Cadena de 54 dígitos | `000012345678920190113163721231000011101000000000100001` | **Sí** |
| Base 16 | `8727F63A15F8976591FDDE5B387C5D015A29E06A1` (41 caracteres) | **Sí** |
| CUF | `8727F63A15F8976591FDDE5B387C5D015A29E06A1A19E23EF34124CD` (56 caracteres) | **Sí** |

Detalle de la suma del Módulo 11. Se recorre de derecha a izquierda con pesos 2,3,…,9 que vuelven a 2; solo aportan los dígitos distintos de cero:
`1×6 + 1×8 + 1×2 + 1×3 + 1×4 + 1×9 + 3×2 + 2×3 + 1×4 + 2×5 + 7×6 + 3×7 + 6×8 + 1×9 + 3×2 + 1×3 + 1×4 + 9×6 + 1×7 + 2×9 + 9×2 + 8×3 + 7×4 + 6×5 + 5×6 + 4×7 + 3×8 + 2×9 + 1×2 = 472`.

Con los mismos datos pero **MODALIDAD = 2 (computarizada)**, la cadena de 53 dígitos es `00001234567892019011316372123100002110100000000010000`, el dígito verificador es 3, el hex es `8727F63A15F8976591FDDE5B4128CF31A2C8606A3` y el CUF queda `8727F63A15F8976591FDDE5B4128CF31A2C8606A3A19E23EF34124CD`. Este vector es de cálculo propio y no viene en la página.

### 1.4 Vectores de prueba adicionales (VERIFICADOS con ida y vuelta)

Decodifiqué los CUF que aparecen en la documentación: hex → decimal de 54 dígitos → campos → Módulo 11 correcto. Después los regeneré con el algoritmo y todos coinciden carácter por carácter. Sirven como pruebas unitarias.

| Origen | NIT dentro del CUF | Fecha/hora | Suc | Mod | Emis | TipoFac | Sector | Nº | POS | DV | Código de control (15) | CUF |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| URL de ejemplo del QR (pág. QR) | 1003579028 | 20200824162245482 | 0 | **2** | 1 | 1 | 01 | 137 | 0 | 4 | `67A75AC82F24C74` | `44AAEC00DBCBA35880091FEE05E92DC65368558BA467A75AC82F24C74` |
| XML `facturaComputarizadaCompraVenta.xml` (ref. cruzada) | 1003579028 | 20211006160348675 | 0 | **2** | 1 | 1 | 01 | 1 | 0 | 6 | `67A75AC82F24C74` | `44AAEC00DBD34C53C3E2CCE1A3FA7AF1E2A08606A667A75AC82F24C74` |
| PDF `Nota CreditoDebito.pdf` | 1003579028 | 20211006160349570 | 0 | **2** | 1 | **3** | **24** | 1 | 0 | 8 | `67A75AC82F24C74` | `44AAEC00DBD34C53C3E5B135433591A5FA086F86A867A75AC82F24C74` |
| PDF `factura COMPRA VENTA.pdf` | 142591020 | 20220506091942957 | 5 | 1 ⚠ | 1 | 1 | 01 | 2377 | 0 | 6 | `7A84B0CA3176D74` | `9C1A83F996B702B8497F0555BFF4C27CB7E1783A67A84B0CA3176D74` |
| PDF `factura TasaCero.pdf` | 374803027 | 20220330152721578 | 0 | 3 | 1 | 2 | 08 | 24 | 0 | 4 | `159318EB7846D74` | `19A522E68ED20E82F3FADFE11D8545827010889F04159318EB7846D74` |

Conclusiones que salen de estos vectores:
- **La fecha del CUF es exactamente la `fechaEmision` del XML, con milisegundos.** En el XML computarizado, `<fechaEmision>2021-10-06T16:03:48.675</fechaEmision>` corresponde a `20211006160348675` en el CUF. La página no lo dice explícitamente; queda VERIFICADO con el ejemplo oficial.
- El número de factura del CUF coincide con el parámetro `numero` del QR (137) y con el "FACTURA N°" del PDF (2377, 24).
- En los PDF de ejemplo, el NIT que se imprime **no coincide** con el NIT que va dentro del CUF (por ejemplo, 123451020 impreso frente a 142591020 en el CUF). Son datos de ejemplo retocados. No hay que usarlos para validar el NIT.
- En todos los ejemplos, el código de control del CUFD tiene **15 caracteres hexadecimales**. La longitud **NO está DOCUMENTADA** en esta página, así que no conviene asumirla fija.
- Longitud del CUF: la parte hex mide **41 o 42** caracteres en los ejemplos (depende del número de dígitos del NIT), con un máximo teórico de 45 porque 10^54 < 16^45. Con el código de control, los ejemplos miden **56 o 57**. En BD conviene reservar `varchar(100)`.

### 1.5 Implementación de referencia en C# (.NET 8)

Es una traducción propia de los algoritmos Java/C# de la página y está VERIFICADA con los 6 vectores anteriores.

> **[corregido por revisión]** El comentario "(hora de Bolivia)" del parámetro `fechaEmision` en el código siguiente es una **recomendación de diseño**, no un dato de la documentación: la zona horaria es NO DOCUMENTADA en todo el corpus (solo se dice "formato UTC extendido sin zona horaria"). Regla consolidada: usar la hora obtenida de la sincronización "Fecha y Hora" del SIN (reloj corregido), formateada **sin offset**, y confirmarlo en piloto. Lo que sí está verificado es que el valor debe ser **idéntico** al de `<fechaEmision>` del XML. Verificación independiente de la revisión: los 6 vectores y el ejemplo oficial se recalcularon con un script separado en Python y coinciden.

```csharp
using System.Globalization;
using System.Numerics;

public static class SiatCuf
{
    /// <param name="fechaEmision">MISMA fecha/hora que se escribe en <fechaEmision> del XML (hora de Bolivia).</param>
    /// <param name="codigoControlCufd">Campo codigoControl devuelto por solicitudCufd.</param>
    public static string Generar(long nitEmisor, DateTime fechaEmision, int codigoSucursal, int modalidad,
        int tipoEmision, int tipoFacturaDocumento, int codigoDocumentoSector, long numeroFactura,
        int codigoPuntoVenta, string codigoControlCufd)
    {
        var ci = CultureInfo.InvariantCulture;
        string cadena =
              nitEmisor.ToString("D13", ci)
            + fechaEmision.ToString("yyyyMMddHHmmssfff", ci)   // HH = 24 h, fff = milisegundos
            + codigoSucursal.ToString("D4", ci)
            + modalidad.ToString("D1", ci)                      // 2 = Computarizada en Línea
            + tipoEmision.ToString("D1", ci)                    // 1 online, 2 offline, 3 masiva
            + tipoFacturaDocumento.ToString("D1", ci)           // 1 con CF, 2 sin CF, 3 doc. ajuste
            + codigoDocumentoSector.ToString("D2", ci)          // 1 compra-venta, 24 nota crédito-débito
            + numeroFactura.ToString("D10", ci)
            + codigoPuntoVenta.ToString("D4", ci);              // 0 = no corresponde
        if (cadena.Length != 53) throw new ArgumentException("Algún campo excede su longitud (cadena != 53 dígitos).");

        cadena += Modulo11(cadena);                              // 54 dígitos
        return Base16(cadena) + codigoControlCufd;
    }

    // Equivale a calculaDigitoMod11(cadena, 1, 9, false) de la página.
    public static string Modulo11(string cadena)
    {
        int suma = 0, mult = 2;
        for (int i = cadena.Length - 1; i >= 0; i--)
        {
            suma += mult * (cadena[i] - '0');
            if (++mult > 9) mult = 2;
        }
        int dig = suma % 11;          // 0..10 (el caso 11 del código Java es inalcanzable con x10=false)
        return dig == 10 ? "1" : dig.ToString(CultureInfo.InvariantCulture);
    }

    // ¡OJO! BigInteger.ToString("X") de .NET antepone un "0" si el primer nibble es >= 8
    // (VERIFICADO: devuelve "08727F63A15F..." para el ejemplo oficial). Hay que quitarlo.
    public static string Base16(string decimales) =>
        BigInteger.Parse(decimales, NumberStyles.None, CultureInfo.InvariantCulture).ToString("X").TrimStart('0');
}
```

Reglas de validación antes de generar:
- NIT ≤ 13 dígitos; sucursal 0..9999; POS 0..9999; número de factura 1..9 999 999 999 (10 dígitos); sector 1..99; modalidad, tipo de emisión y tipo de factura con 1 dígito.
- Si la cadena no mide exactamente 53 caracteres, hay que abortar.
- El código de control debe ser el del **CUFD vigente** con el que se emite. Qué CUFD usar, cuánto dura y cómo se obtiene está NO DOCUMENTADO en esta página (ref. cruzada: `…codigos__solicitud-cufd.md`).

### 1.6 Discrepancias de la documentación

- La página del Módulo 11 dice que la cadena de entrada es "nit, sucursal, fecha, modalidad, tipo emisión, tipo documento, número factura". **Ese orden es incorrecto e incompleto** (le faltan el tipo de factura y el POS, y la sucursal está antes que la fecha). El orden válido es el de la página del CUF: NIT, FECHA, SUCURSAL, MODALIDAD, TIPO EMISIÓN, TIPO FACTURA, SECTOR, NÚMERO, POS. Así lo confirman el ejemplo oficial y los 5 vectores.
- La página del CUF llama al sector 2 "Recibo de Alquiler de Bienes Inmuebles", mientras que la tabla de títulos lo titula "FACTURA DE ALQUILER".

---

## 2. Algoritmo Módulo 11

Fuente: `facturacion-en-linea__algoritmos-utilizados__algoritmo-modulo-11.md`

La firma Java publicada es `calculaDigitoMod11(String cadena, int numDig, int limMult, boolean x10)`, y el SIAT la invoca como `calculaDigitoMod11(pCadena, 1, 9, false)` a través de `obtenerModulo11`.

Semántica exacta con los parámetros del SIAT (numDig = 1, limMult = 9, x10 = false):
1. `suma = 0`, `mult = 2`.
2. Se recorre la cadena **de derecha a izquierda**: `suma += mult * dígito`. Luego `mult++`, y si pasa de 9 vuelve a 2.
3. Como `x10 = false`, `dig = suma % 11`.
4. Si `dig == 10`, se agrega **"1"**. Si `dig == 11`, se agrega "0" (no puede pasar con `% 11`). Si `dig < 10`, se agrega el dígito.
5. El método devuelve el **último carácter**, que es el dígito verificador.

Casos límite: con resto 0 el DV es "0"; con resto 10 el DV es "1" (lo muestra el ejemplo oficial). La rama `x10 = true` (`((suma*10) % 11) % 10`) **no la usa el SIAT**.

La versión C# de la página está enlazada (`?id=344`) pero **no está en el archivo** (NO DOCUMENTADO en el texto). La traducción del §1.5 es equivalente y está VERIFICADA.

---

## 3. Base 16 y relleno de ceros

Fuente: `facturacion-en-linea__algoritmos-utilizados__base-16.md`

Código C# publicado:
- `StringTools.CompleteCero(string pString, short pMaxChar, bool pRigth = false)`: antepone "0" hasta llegar a `pMaxChar`. El parámetro `pRigth` **no se usa** en el código publicado; siempre rellena a la izquierda. Si la cadena ya es más larga, **no la trunca**: la devuelve tal cual. M-INV debe validar la longitud por su cuenta.
- `Base16(string pString)`: `BigInteger.Parse(pString).ToString("X")`, que da hex en MAYÚSCULAS.
- `Base10(string pString)`: `BigInteger.Parse(pString, NumberStyles.HexNumber).ToString()`, la operación inversa. Sirve para decodificar y auditar un CUF.

**Trampa .NET (VERIFICADA en .NET Framework 4.x y en .NET SDK 10):** el código tal como lo publica el SIN produce `08727F63A15F8976591FDDE5B387C5D015A29E06A1`, con un **0 extra**, cuando el primer dígito hex es ≥ 8, porque .NET reserva ese nibble para el signo. El resultado esperado por el SIAT **no lleva** ese 0 (`8727F63A…`). Hay que usar `.TrimStart('0')`.

Lo mismo aplica a `Base10`. Al decodificar, `BigInteger.Parse("8727…", HexNumber)` interpreta la cadena como **negativa** si el primer carácter es ≥ 8. Para decodificar hay que anteponer "0": `BigInteger.Parse("0" + hex, NumberStyles.HexNumber)`. VERIFICADO: sin el "0" devuelve `-11038347277104333527537726323449427314494920587615`; con el "0" devuelve `12345678920190113163721231000011101000000000100001`, que rellenado a 54 dígitos es la cadena original. Luego hay que aplicar `.PadLeft(54, '0')`.

---

## 4. SHA-256 (y MD5/CRC32)

Fuente: `facturacion-en-linea__algoritmos-utilizados__generacion-de-sha-256-md5-y-crc32.md`

- Java: `MessageDigest.getInstance("SHA-256")` sobre `byte[] pArchivo`, y luego `DatatypeConverter.printHexBinary(digest).toLowerCase()`. **Resultado: hex en minúsculas, 64 caracteres.**
- La página recomienda "Java JDK1.8.0_172". En .NET no aplica.
- Equivalente .NET: `Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()`.
- **Qué bytes se hashean** (XML crudo, XML comprimido o paquete TAR.GZ) y **en qué campo va** (p. ej. `hashArchivo`): NO DOCUMENTADO en esta página. Ref. cruzada: specs de recepción de facturas y paquetes.
  **[corregido por revisión]** Resuelto en `emision-y-envio.md` y `requerimientos__sistema-informatico.md`: se hashean los **bytes comprimidos** que viajan en `archivo` (GZIP del XML en envío individual; GZIP del TAR en paquetes) y el resultado va en `hashArchivo` (en los servicios de compras, en `hash`). Ver `02-…` §5.
- **MD5 y CRC32:** aparecen en el título de la página pero **no hay ningún contenido**. NO DOCUMENTADO.

---

## 5. Compresión GZIP

Fuente: `facturacion-en-linea__algoritmos-utilizados__comprimir-gzip.md`

- Java: `GZIPOutputStream` sobre `FileOutputStream(archivo + ".zip")`, con buffer de 1024 bytes. Devuelve `true` o `false`.
- Aunque la extensión del ejemplo es **`.zip`, el formato es GZIP** (RFC 1952), no ZIP.
- Equivalente .NET: `using var gz = new GZipStream(destino, CompressionLevel.Optimal); origen.CopyTo(gz);`. Se puede trabajar en memoria con `MemoryStream`; no hace falta escribir en disco.
- Qué se comprime (un XML individual o un TAR con varios XML para paquetes o masiva) y cómo se envía (base64 o `byte[]` en SOAP): NO DOCUMENTADO en esta página. Ref. cruzada: specs de recepción.

---

## 6. Redondeo

Fuente: `facturacion-en-linea__algoritmos-utilizados__algoritmo-de-redondeo.md`

### 6.1 Lo que dice la página (texto completo relevante)

> "El Servicio de Impuestos Nacionales utiliza en la emisión de facturas electrónicas en linea montos expresados con **dos decimales** y utiliza el redondeo tradicional o **HALF-UP**. En este caso, el redondeo se realiza al número superior cuando el decimal sea igual o superior a 5 y al número inferior cuando el decimal sea igual o inferior a 5."

Ejemplos de la página: **3.14159 → 3.14** y **3.14559 → 3.15**.

Notas de interpretación:
- El texto se contradice ("igual o inferior a 5" también baja). **Los ejemplos resuelven la duda: el 5 sube.** Es HALF_UP estándar: se mira el tercer decimal, y si es ≥ 5 se sube.
- La página tiene una imagen que dice "REDONDEAR SIEMPRE HACIA ARRIBA". **No debe interpretarse como techo (ceiling)**: el ejemplo 3.14159 → 3.14 baja. Es HALF_UP.
- El redondeo es de **un solo paso** sobre el valor exacto. 3.14559 → 3.15 al mirar el tercer decimal; no se redondea en cascada.
- La página habla de "facturas electrónicas". Los XSD computarizados (ref. cruzada) también limitan los montos a 2 decimales, así que **aplica igual a la computarizada**.

### 6.2 Cuándo se redondea cada campo

La página **NO DOCUMENTA** en qué momento se redondea cada campo.

Ref. cruzada (no es de mi bloque), `adjuntos/xml/CompraVentaXML/facturaComputarizadaCompraVenta.xsd`: `cantidad`, `precioUnitario`, `montoDescuento`, `subTotal`, `montoTotal`, `montoTotalSujetoIva`, `tipoCambio`, `montoTotalMoneda`, `montoGiftCard` y `descuentoAdicional` tienen `fractionDigits = 2` y `totalDigits = 17`.

En cambio, en `notaComputarizadaCreditoDebito(.Descuento).xsd` el **detalle** (`cantidad`, `precioUnitario`, `montoDescuento`, `subTotal`) admite **10 decimales**, y los totales solo 2. Ref. cruzada: en `…nota-credito-debito.md` se lee: "Cuando se elabora una Nota Débito Crédito para una factura emitida en el SFV, se debe considerar que el redondeo en el detalle utiliza 5 decimales, pero cuando se aplica a una factura electrónica se utilizan 2 decimales…".

**Regla propuesta para M-INV (factura compra-venta computarizada).** Es una inferencia que respetan los XSD y que cuadra con todos los PDF de ejemplo:
1. `cantidad`, `precioUnitario` y `montoDescuento` se capturan o redondean a 2 decimales con HALF_UP **antes** de calcular.
2. `subTotal = R2(cantidad × precioUnitario − montoDescuento)`.
3. `SUBTOTAL (cabecera) = Σ subTotal`. Es una suma de valores que ya tienen 2 decimales, así que no hace falta volver a redondear.
4. `TOTAL (montoTotal) = SUBTOTAL − descuentoAdicional`.
5. `MONTO A PAGAR = TOTAL − montoGiftCard`, e `IMPORTE BASE CRÉDITO FISCAL (montoTotalSujetoIva)` según las reglas del XSD (ref. cruzada). Estas fórmulas se infieren de las etiquetas de los PDF y del XML de ejemplo; **no están en mi bloque**.
6. Cualquier multiplicación o división (porcentajes, prorrateos, tipo de cambio, 13 %) se hace con `decimal` exacto y se redondea **una sola vez** al asignar el campo.

### 6.3 Ejemplos numéricos VERIFICADOS con los PDF de mi bloque

**`factura COMPRA VENTA.pdf`**

| Código | Cantidad | P. unit. | Desc. | Cálculo | SubTotal en el PDF |
|---|---|---|---|---|---|
| GA-CL-013 | 200.00 | 0.63 | 7.52 | 200 × 0.63 = 126.00 − 7.52 | **118.48** ✔ |
| GA-CL-015 | 100.00 | 0.72 | 4.11 | 100 × 0.72 = 72.00 − 4.11 | **67.89** ✔ |
| RP-FR-01 | 1.00 | 0.98 | 0.00 | 0.98 | **0.98** ✔ |
| HI-CO-004 | 3.00 | 40.72 | 0.00 | 3 × 40.72 | **122.16** ✔ |

SUBTOTAL 309.51 ✔ (suma); DESCUENTO 0.00; TOTAL 309.51; GIFT CARD 0.00; MONTO A PAGAR 309.51; IMPORTE BASE CF 309.51.

**`Nota CreditoDebito.pdf`**: devuelve 1 × 775.00, así que MONTO TOTAL DEVUELTO = 775.00. MONTO EFECTIVO DÉBITO-CRÉDITO = 775.00 × 13 % = **100.75** ✔. El 13 % se infiere del número; la fórmula no está documentada en mi bloque.

**`Nota CreditoDebitoDescuento.pdf`** (caso con descuento adicional en la factura original):
- Factura original: 775.00 + 75.00 = **850.00** (MONTO TOTAL ORIGINAL). DESCUENTO ADICIONAL 50.00. MONTO TOTAL A PAGAR **800.00**.
- Devolución: SUB TOTAL 75.00.
- MONTO DESCUENTO DÉBITO CRÉDITO = 50.00 × 75.00 / 850.00 = 4.41176… → **4.41** ✔ (HALF_UP). Es un prorrateo del descuento adicional en proporción al monto original; la fórmula es INFERIDA del ejemplo.
- MONTO TOTAL DEVUELTO = 75.00 − 4.41 = **70.59** ✔.
- MONTO EFECTIVO DÉBITO-CRÉDITO = 70.59 × 0.13 = 9.1767 → **9.18** ✔.
- Literal: "Son: Setenta 59/100 Bolivianos", es decir, el literal corresponde al **monto total devuelto**.

### 6.4 Implementación .NET (VERIFICADA)

```csharp
public static class SiatRedondeo
{
    public static decimal R2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
```

- **No hay que usar `Math.Round(x, 2)` sin modo**: redondea al par (banker's rounding). Por ejemplo, `2.345m → 2.34`, cuando lo correcto es 2.35. VERIFICADO.
- **No hay que usar `double`**. `Math.Round(1.005, 2, AwayFromZero)` devuelve **1.00** en `double` y **1.01** en `decimal`. VERIFICADO en .NET SDK 10. En PostgreSQL, usar `numeric(19,2)` para montos.
- `AwayFromZero` equivale a HALF_UP para valores positivos. Los XSD exigen montos > 0 o ≥ 0, así que no hay casos negativos.
- Al serializar al XML o al QR: `v.ToString("0.00", CultureInfo.InvariantCulture)`, con punto decimal. VERIFICADO que con la cultura `es` del equipo .NET imprime coma ("2,68"), y eso rompería el XML. Si el XML exige o no exactamente 2 decimales (p. ej. "99" frente a "99.00"), eso es NO DOCUMENTADO aquí. El XML de ejemplo usa `<montoTotal>99</montoTotal>`, sin decimales.

---

## 7. Código QR de la factura (facturación en línea)

Fuente: `facturacion-en-linea__algoritmos-utilizados__codigo-respuesta-rapida-qr.md`

### 7.1 Finalidad

Es una "medida de seguridad en la impresión de la representación gráfica". Permite acceder a un enlace del portal tributario para validar que la Factura o la Nota de Crédito-Débito está registrada en la base de datos de la Administración Tributaria. Es obligatorio en la representación gráfica ("se debe incluir").

### 7.2 Contenido: una URL

```
https://pilotosiat.impuestos.gob.bo/consulta/QR?nit=valorNit&cuf=valorCuf&numero=valorNroFactura&t=valorTamaño
```

| Parámetro | Valor |
|---|---|
| `nit` | NIT del **emisor** de la Factura o Nota de Crédito-Débito |
| `cuf` | CUF de la Factura o Nota |
| `numero` | Número correlativo de la Factura o Nota |
| `t` | "tamaño para la pre visualización": **1 = rollo**, **2 = media hoja**. Si se omite, vale **1** |

- Ejemplo de la página, sin `t`: `https://pilotosiat.impuestos.gob.bo/consulta/QR?nit=1003579028&cuf=44AAEC00DBCBA35880091FEE05E92DC65368558BA467A75AC82F24C74&numero=137`. Ese CUF es de modalidad **2 computarizada** (§1.4) y su número interno es 137, igual que el parámetro `numero`. VERIFICADO.
- **Ambiente de pruebas (piloto):** `https://pilotosiat.impuestos.gob.bo/consulta/QR`.
- **Ambiente productivo:** la página dice "La ruta para ambiente productivo se la hará conocer al finalizar el proceso de autorización". Es **NO DOCUMENTADO**, así que M-INV debe tenerla **configurable**. No invento la URL.
- Codificación: los valores son numéricos o hex en mayúsculas, así que no requieren escape URL. Aun así conviene usar un constructor de query estándar.
- Nivel de corrección de errores, versión del QR, margen y color: **NO DOCUMENTADO**.

### 7.3 Dimensión

La página dice: "Se recomienda no utilizar códigos QR de menos de **3 x 3 cm**. En caso de utilizarlos deben ser visibles y permitir la lectura correcta con dispositivos de capacidades y calidad media (dispositivos móviles, tabletas u otros)."

En los 4 PDF de ejemplo, el QR es una imagen JPEG en escala de grises de 328×328 px, dibujada a **78.9 pt ≈ 2.79 × 2.79 cm**. Es decir, los propios ejemplos quedan un poco por debajo de la recomendación. Lo medí con la matriz `cm` de cada PDF. **M-INV debe usar al menos 3 cm (≥ 85 pt)**.

### 7.4 Contenido de la imagen de la página (`/images/2021/qrfactura.png`)

Es un QR de 347×346 px. No pude decodificarlo porque no hay ningún decodificador QR instalado y no descargué librerías. Qué URL exacta contiene: NO VERIFICADO.

---

## 8. QR del SFV antiguo (modalidad computarizada transitoria) — **no aplica a M-INV**

Fuente: `facturacion-manual__algoritmos__qr-sfv.md`. La tabla está en la imagen oficial, que descargué a `specs/06-anexos/qr-sfv-campos.png`.

Aplica a la modalidad manual o "Computarizada SFV" (con código de control y número de autorización de dosificación), **no a la Computarizada en Línea**. La documento para distinguirla y para cuando se emitan Notas de Crédito-Débito que referencien facturas antiguas del SFV.

| Pos. | Campo | Tipo | Descripción | Obligatoriedad | Long. máx. |
|---|---|---|---|---|---|
| 1 | NIT emisor | Numérico | NIT del emisor | SI | 12 |
| 2 | Número de Factura | Numérico | Número correlativo de Factura o Nota Fiscal | SI | 10 |
| 3 | Número de Autorización | Numérico | Número otorgado por la AT para identificar la dosificación | SI | 15 |
| 4 | Fecha de emisión | Fecha | Formato DD/MM/AAAA | SI | 10 |
| 5 | Total | Numérico | Monto total de la Factura o Nota Fiscal (punto "." como separador decimal) | SI | 11 |
| 6 | Importe base para el Crédito Fiscal | Numérico | Monto válido para el cálculo del CF (punto decimal) | SI | 11 |
| 7 | Código de Control | Alfanumérico | Identifica la transacción | SI | 17 |
| 8 | NIT / CI / CEX Comprador | Alfanumérico | NIT del comprador; si no tiene, el CI o Carnet de Extranjería, o el carácter cero (0) | SI | 12 |
| 9 | Importe ICE / IEHD / TASAS | Numérico | Si no corresponde, "0" (punto decimal) | CUANDO CORRESPONDA | 11 |
| 10 | Importe por ventas no Gravadas o Gravadas a Tasa Cero | Numérico | Si no corresponde, "0" | CUANDO CORRESPONDA | 11 |
| 11 | Importe no Sujeto a Crédito Fiscal | Numérico | Si no corresponde, "0" | CUANDO CORRESPONDA | 11 |
| 12 | Descuentos, Bonificaciones y Rebajas Obtenidas | Numérico | Si no corresponde, "0" | CUANDO CORRESPONDA | 11 |
| | **Total de caracteres** | | | | **142** |

El **separador** entre campos es NO DOCUMENTADO: no aparece en la imagen ni en el texto.

---

## 9. Código de Control (SFV antiguo) — **no aplica a M-INV**

Fuente: `facturacion-manual__algoritmos__codigo-de-control.md`

- Es un dato alfanumérico que el sistema computarizado SFV genera al emitir. Ejemplo: `CB-5E-CF-8B-05`.
- Son pares hexadecimales (A–F) separados por guiones, **sin la letra "O"**: solo el número cero.
- Se genera a partir de la información de la dosificación y de la llave asignada, con **Alleged RC4, Verhoeff y Base 64**. El detalle está en la "Especificación Técnica para la generación del Código de Control" (`…/356aea02e.pdf`), que **no está disponible** en los adjuntos (se descargó un HTML). El algoritmo detallado es NO DOCUMENTADO en mi bloque.
- En la Computarizada en Línea, el CUF reemplaza al código de control. **M-INV no lo necesita**, salvo para mostrar o validar datos de facturas antiguas en Notas de Crédito-Débito.

---

## 10. Títulos y subtítulos de los Documentos Fiscales Digitales

Fuente: `facturacion-en-linea__titulos-y-subtitulos-fel.md`. Tabla transcrita completa. Las erratas del original están marcadas con *(sic)*.

| Descripción | Título | Subtítulo |
|---|---|---|
| FACTURA DE COMPRA Y VENTA | **FACTURA** | **CON DERECHO A CRÉDITO FISCAL** |
| FACTURA DE ALQUILER | FACTURA DE ALQUILER | CON DERECHO A CRÉDITO FISCAL |
| FACTURA COMERCIAL DE EXPORTACIÓN | FACTURA COMERCIAL DE EXPORTACIÓN | SIN DERECHO A CRÉDITO FISCAL |
| FACTURA COMERCIAL DE EXPORTACIÓN EN LIBRE CONSIGNACIÓN | FACTURA COMERCIAL DE EXPORTACIÓN EN LIBRE CONSIGNACIÓN | SIN DERECHO A CRÉDITO FISCAL |
| FACTURA DE VENTA DE ZONA FRANCA | FACTURA DE VENTA DE ZONA FRANCA | SIN DERECHO A CRÉDITO FISCAL |
| FACTURA DE SERVICIO TURÍSTICO Y HOSPEDAJE | FACTURA DE SERVICIO TURÍSTICO Y HOSPEDAJE | SIN DERECHO A CRÉDITO FISCAL |
| FACTURA DE SEGURIDAD ALIMENTARIA Y ABASTECIMIENTO | FACTURA - SEGURIDAD ALIMENTARIA Y ABASTECIMIENTO | SIN DERECHO A CRÉDITO FISCAL |
| FACTURA TASA CERO VENTA DE LIBROS | FACTURA TASA CERO – VENTA DE LIBROS | SIN DERECHO A CRÉDITO FISCAL |
| FACTURA TASA CERO DE TRANSPORTE DE CARGA INTERNACIONAL | FACTURA TASA CERO – TRANSPORTE DE CARGA INTERNACIONAL | SIN DERECHO A CRÉDITO FISCAL |
| FACTURA DE COMPRA Y VENTA DE MONEDA EXTRANJERA | FACTURA DE COMPRA Y VENTA DE MONEDA EXTRANJERA | SIN DERECHO A CRÉDITO FISCAL |
| FACTURA DUTTY FREE *(sic)* | FACTURA DUTTY FREE *(sic)* | SIN DERECHO A CRÉDITO FISCAL |
| FACTURA SECTORES EDUCATIVOS | FACTURA | CON DERECHO A CRÉDITO FISCAL |
| FACTURA CLÍNICAS/HOSPITALES | FACTURA | CON DERECHO A CRÉDITO FISCAL |
| FACTURA HOTELES | FACTURA | CON DERECHO A CRÉDITO FISCAL |
| FACTURA COMERCIALIZACIÓN DE COMBUSTIBLE | FACTURA | CON DERECHO A CRÉDITO FISCAL |
| FACTURA COMERCIALIZACIÓN DE GNV | FACTURA | "CON DERECHO A CRÉFACTURADITO FISCAL" *(sic, errata de "CRÉDITO")* |
| FACTURA COMERCIALIZACIÓN DE GN/GLP | FACTURA | CON DERECHO A CRÉDITO FISCAL |
| FACTURA DE HIDROCARBUROS ALCANZADA IEHD | FACTURA | CON DERECHO A CRÉDITO FISCAL |
| FACTURA DE HIDROCARBUROS NO ALCANZADA IEHD | FACTURA | CON DERECHO A CRÉDITO FISCAL |
| FACTURA ALCANZADA POR ICE | FACTURA | CON DERECHO A CRÉDITO FISCAL |
| FACTURA COMERCIAL EXPORTACIÓN DE SERVICIOS | FACTURA COMERCIAL DE EXPORTACIÓN | SIN DERECHO A CRÉDITO FISCAL |
| FACTURA DE SERVICIOS BÁSICOS | FACTURA | CON DERECHO A CRÉDITO FISCAL |
| FACTURA DE SERVICIOS BÁSICOS ZONA FRANCA | FACTURA | SIN DERECHO A CRÉDITO FISCAL |
| FACTURA DE JUEGOS DE AZAR | FACTURA | CON DERECHO A CRÉDITO FISCAL |
| FACTURA DE ENTIDADES FINANCIERAS | FACTURA | CON DERECHO A CRÉDITO FISCAL |
| FACTURA COMERCIAL DE EXPORTACIÓN DE MINERALES | FACTURA COMERCIAL DE EXPORTACIÓN | SIN DERECHO A CRÉDITO FISCAL |
| FACTURA COMERCIAL DE EXPORTACIÓN HIDROCARBUROS | FACTURA COMERCIAL DE EXPORTACIÓN | SIN DERECHO A CRÉDITO FISCAL |
| FACTURA VENTA INTERNA DE MINERALES | FACTURA | CON DERECHO A CRÉDITO FISCAL |
| FACTURA DE TELECOMUNICACIONES | FACTURA | CON DERECHO A CRÉDITO FISCAL |
| FACTURA SUMINISTRO DE ENERGÍA | FACTURA | CON DERECHO A CRÉDITO FISCAL |
| FACTURA IMPORTACIÓN Y COMERCIALIZACIÓN DE LUBRICANTES | FACTURA | CON DERECHO A CRÉDITO FISCAL |
| FACTURA SECTOR EDUCATIVO ZONA FRANCA | FACTURA | SIN DERECHO A CRÉDITO FISCAL |
| FACTURA COMERCIAL DE EXPORTACIÓN PRECIO VENTA | FACTURA COMERCIAL DE EXPORTACIÓN | SIN DERECHO A CRÉDITO FISCAL |
| FACTURA PREVALORADA | FACTURA | CON DERECHO A CRÉDITO FISCAL |
| *(fila huérfana, sin descripción ni título)* | — | SIN DERECHO A CRÉDITO FISCAL *(probablemente la Prevalorada sin derecho a CF: NO DOCUMENTADO)* |
| FACTURA DE TELECOMUNICACIONES ZONA FRANCA | FACTURA | SIN DERECHO A CRÉDITO FISCAL |
| FACTURA HOSPITALES/CLINICAS ZONA FRANCA | FACTURA | SIN DERECHO A CRÉDITO FISCAL |
| FACTURA ENGARRAFADORAS | FACTURA | CON DERECHO A CRÉDITO FISCAL |
| FACTURA VENTA MINERALES BANCO CENTRAL | FACTURA | SIN DERECHO A CRÉDITO FISCAL |
| FACTURA IMPORTACIÓN Y COMERCIALIZACIÓN DE LUBRICANTES IEHD | FACTURA | CON DERECHO A CRÉDITO FISCAL |
| FACTURA COMPRA-VENTA DE INSUMOS PARA LA PRODUCCIÓN DE BIODIÉSEL Y/O DIÉSEL ECOLÓGICO | FACTURA COMPRA-VENTA DE INSUMOS PARA LA PRODUCCIÓN DE BIODIÉSEL Y/O DIÉSEL ECOLÓGICO | SIN DERECHO A CRÉDITO FISCAL |

Cómo se imprime (según los PDF):
- El **título** va en MAYÚSCULAS, en negrita.
- El **subtítulo** va **en mayúsculas y minúsculas y entre paréntesis**: "(Con Derecho a Crédito Fiscal)" o "(Sin Derecho a Crédito Fiscal)". La tabla lo muestra en mayúsculas y sin paréntesis. En el PDF de tasa cero aparece con doble espacio, "(Sin  Derecho a Crédito Fiscal)", lo que parece una errata.
- En el PDF de tasa cero, el título usa guion simple: "FACTURA TASA CERO - TRANSPORTE DE CARGA INTERNACIONAL". La tabla usa guion largo "–".
- La **Nota de Crédito-Débito** (sector 24) **no figura en la tabla**. Los PDF la titulan **"NOTA CRÉDITO - DÉBITO"** y **sin subtítulo**.
- Para M-INV (compra-venta, sector 1): título **"FACTURA"** y subtítulo **"(Con Derecho a Crédito Fiscal)"**. Para notas (sector 24): **"NOTA CRÉDITO - DÉBITO"**, sin subtítulo.
- La tabla **no trae** los códigos de documento sector. Solo están documentados en mi bloque 1 = compra-venta, 2 = alquiler y 24 = nota (página del CUF), y 08 = tasa cero (deducido de un CUF). Para el resto, ref. cruzada: catálogo `tipoDocumentoSector`.

---

## 11. Representación gráfica en formato hoja (PDF de ejemplo)

Fuentes: `adjuntos/factura COMPRA VENTA.pdf`, `adjuntos/Nota CreditoDebito.pdf`, `adjuntos/Nota CreditoDebitoDescuento.pdf`, `adjuntos/factura TasaCero.pdf`.

Las medidas salen del flujo de contenido de cada PDF (posiciones `Td`, tamaños `Tf`, matriz `cm` del QR). Las coordenadas están en **puntos (pt) medidos desde la esquina superior izquierda** (1 pt = 0.3528 mm).

Ninguna página de mi bloque define normativamente la disposición: los PDF son "ejemplos". Por eso lo que sigue es la **disposición de referencia a replicar**.

### 11.1 Página y tipografía

- **Tamaño: Carta, 612 × 792 pt (21.59 × 27.94 cm), vertical, 1 página.** El contenido ocupa aproximadamente los 510 pt superiores (unos 18 cm) y el resto queda en blanco.
- El QR del SIAT llama "media hoja" (`t=2`) a este formato. Qué dimensiones exactas tiene "media hoja" es NO DOCUMENTADO: los ejemplos son hoja Carta completa.
- Márgenes: izquierdo ≈ 30 pt (1.06 cm); el borde derecho de la tabla y del QR llega a ≈ 584 pt, lo que deja un margen derecho de ≈ 28 pt.
- Fuentes: **Nimbus Sans / Nimbus Sans Bold**, métricamente equivalentes a **Helvetica/Arial**. Los ejemplos de notas usan DejaVu Sans para el pie. Texto en negro sobre blanco.
- Tamaños: razón social 9 pt negrita; resto del emisor 8 pt; título **14 pt negrita**; subtítulo 9 pt; datos del cliente 9 pt (etiqueta en negrita y valor normal); tabla 8 pt (cabecera en negrita); etiquetas de totales 7 pt y valores de totales 8 pt; literal "Son:" 8 pt negrita; leyendas de 7 pt a 7.5 pt; número de página 8 pt.

### 11.2 Factura compra-venta (`factura COMPRA VENTA.pdf`): disposición de arriba abajo

**Bloque 1: Emisor (arriba a la izquierda)**
Líneas centradas en una columna de unos 30 a 210 pt, con separación de ≈ 11 pt. La primera línea está a y ≈ 33 pt:
1. Razón social del emisor (9 pt, negrita). Ej.: "Metales Totai S.R.L."
2. **"CASA MATRIZ"** si la sucursal es 0, o **"SUCURSAL N. 5"** si la sucursal es 5 (8 pt, negrita).
3. **"No. Punto de Venta 0"**.
4. Dirección. Ej.: "Av. Juan XXIII". Puede ocupar 2 líneas; en tasa cero se ve "CALLE LA PAZ NRO. SN ZONA/BARRIO:" / "CENTRAL SACABA".
5. **"Teléfono: 2824512"**. No aparece en el ejemplo de tasa cero.
6. Municipio. Ej.: "Yacuiba".

**Bloque 2: Datos fiscales (arriba a la derecha)**
Etiquetas en negrita de 8 pt en x = 393 pt y valores de 8 pt en x = 500 pt. La primera fila está a y ≈ 33 pt, con 11 pt de separación:
- **NIT** → NIT del emisor.
- **FACTURA N°** → número de factura.
- **CÓD. AUTORIZACIÓN** → **el CUF**. Ojo: la etiqueta impresa es "CÓD. AUTORIZACIÓN", no "CUF". El CUF se parte en varias líneas según el ancho de la columna (≈ 75 pt, líneas de 16 o 17 caracteres, interlineado de 9.3 pt). Ej.: `9C1A83F996B702B84` / `97F0555BFF4C27CB7` / `E1783A67A84B0CA31` / `76D74`.

**Marca de agua** (solo en este ejemplo): "SIN VALOR LEGAL", 55 pt, negrita cursiva, gris claro (RGB ≈ 0.86), cruzando el ancho a y ≈ 122 pt. No hay ninguna regla que la exija. **Propuesta:** imprimirla en el ambiente piloto o de pruebas.

**Bloque 3: Título**, centrado. "**FACTURA**" a 14 pt negrita, en y ≈ 134 pt. Debajo, "(Con Derecho a Crédito Fiscal)" a 9 pt, en y ≈ 149 pt.

**Bloque 4: Datos del cliente** (9 pt, 2 filas en y ≈ 181 y 196 pt)
| Izquierda (etiqueta en x = 30, valor en x = 147) | Derecha (etiqueta alineada a la derecha hasta ≈ 497, valor en x = 505) |
|---|---|
| **Fecha:** `06/05/2022 09:19 AM` | **NIT/CI/CEX:** `987654` |
| **Nombre/Razón Social:** `DAVID ZELADA` | **Cod. Cliente:** `1864` |

- Formato de fecha del ejemplo: `dd/MM/yyyy hh:mm AM/PM`. En las notas aparece `06/10/2021 04:03 p. m.`, con la cultura es-BO. La fecha impresa no lleva segundos aunque la de la emisión sí los tiene.
- Cómo mostrar el **complemento** del CI y el tipo de documento: NO DOCUMENTADO, porque ningún ejemplo lo usa.

**Bloque 5: Tabla de detalle**
Tiene bordes finos, de x = 30 a 584 pt. La cabecera va a 8 pt negrita centrada, en dos o tres renglones, en y ≈ 222–248 pt.

| Columna | Ancho aprox. (pt) | Alineación del valor | Ejemplo |
|---|---|---|---|
| CÓDIGO PRODUCTO / SERVICIO | 79 | izquierda | GA-CL-013 |
| CANTIDAD | 59 | derecha, 2 decimales | 200.00 |
| UNIDAD DE MEDIDA | 56 | izquierda (**descripción** de la unidad, no el código) | PIEZAS · UNIDAD (BIENES) · TUBOS |
| DESCRIPCIÓN | 146 | izquierda, con salto de línea automático | GANCHO P/ CALAMINA M6*J 60 |
| PRECIO UNITARIO | 70 | derecha, 2 decimales | 0.63 |
| DESCUENTO | 69 | derecha | 7.52 |
| SUBTOTAL | 75 | derecha | 118.48 |

La fila mide unos 22 pt y crece si la descripción ocupa varias líneas; en tasa cero ocupa 8 líneas.

Ref. cruzada (`…homologacion-de-productos-servicios.md`): el código visible es el **código interno** de la empresa (`codigoProducto`); el `codigoProductoSin` va solo en el XML.

**Bloque 6: Totales** (caja de 2 columnas, de x ≈ 370 a 584 pt, alineada a la derecha, justo debajo de la tabla). Etiquetas de 7 pt alineadas a la derecha y valores de 8 pt alineados a la derecha, con filas de 12 pt:
1. SUBTOTAL Bs → 309.51
2. DESCUENTO Bs → 0.00
3. TOTAL Bs → 309.51
4. MONTO GIFT CARD Bs → 0.00
5. **MONTO A PAGAR Bs** (negrita) → **309.51**
6. **IMPORTE BASE CRÉDITO FISCAL** (negrita) → **309.51**

A la izquierda de la caja, en x = 38 y alineado verticalmente con la mitad de los totales: **"Son: Trescientos nueve 51/100 Bolivianos"** (8 pt negrita).

**Bloque 7: Leyendas y QR.** Empieza unos 20–30 pt debajo de los totales.
- Hay tres leyendas **centradas** en el ancho x ≈ 30–490 pt, con unos 16–20 pt entre líneas:
  1. `ESTA FACTURA CONTRIBUYE AL DESARROLLO DEL PAÍS, EL USO ILÍCITO SERÁ SANCIONADO PENALMENTE DE ACUERDO A LEY` (7 pt, mayúsculas).
  2. `Ley N° 453: <texto de la leyenda de la factura>` (7.5 pt). Ej.: "Ley N° 453: El proveedor deberá entregar el producto en las modalidades y términos ofertados o convenidos."
  3. `“Este documento es la Representación Gráfica de un Documento Fiscal Digital emitido en una modalidad de facturación en línea”` (7.5 pt, **con comillas tipográficas**).
- **QR** a la derecha: de x = 505 a 584 pt, con el borde superior alineado a la primera leyenda (y ≈ 431 pt) y un tamaño de 78.9 × 78.9 pt (2.79 cm).

**Pie:** número de página "1/1" a 8 pt, abajo a la derecha (x ≈ 543, y ≈ 757 pt).

El texto "(Factura Compra Venta)" en serif de 12 pt, cerca del pie, **es solo el rótulo del ejemplo**. No forma parte de la factura y no se debe imprimir.

### 11.3 Factura Tasa Cero (`factura TasaCero.pdf`): diferencias

- Título: "**FACTURA TASA CERO - TRANSPORTE DE CARGA INTERNACIONAL**" (14 pt, centrado, y ≈ 146 pt). Subtítulo "(Sin Derecho a Crédito Fiscal)" en y ≈ 169 pt. Datos del cliente en y ≈ 221 y 236 pt.
- Los encabezados de la tabla son iguales, pero la primera columna se titula "CÓDIGO PRODUCTO", sin "/ SERVICIO".
- Totales: SUBTOTAL Bs, DESCUENTO Bs, TOTAL Bs, MONTO GIFT CARD Bs y **MONTO A PAGAR Bs** (negrita). **No lleva la fila "IMPORTE BASE CRÉDITO FISCAL"**, porque es sin derecho a CF.
- Montos con separador de miles: **"3,480.00"**, es decir, coma para los miles y punto para los decimales.
- Literal: "Son: Tres mil cuatrocientos ochenta 00/100 Bolivianos".
- Ley 453 de este ejemplo: "Ley N° 453: El proveedor debe brindar atención sin discriminación, con respeto, calidez y cordialidad a los usuarios y consumidores."
- Emisor sin línea de teléfono. Según el CUF, el ejemplo se emitió en modalidad 3 (portal web) y la disposición es la misma.

### 11.4 Nota de Crédito-Débito (`Nota CreditoDebito.pdf`)

- Los bloques de emisor y de datos fiscales son iguales, pero en la derecha dicen **"Nota N°"** en lugar de "FACTURA N°". La etiqueta sigue siendo "CÓD. AUTORIZACIÓN" con el CUF de la nota.
- Título: "**NOTA CRÉDITO - DÉBITO**", 14 pt, **sin subtítulo**, en y ≈ 138 pt.
- Datos (9 pt, 4 filas en y ≈ 185, 200, 215 y 230 pt):

| Izquierda (etiqueta en x = 30, valor en x = 150) | Derecha (etiqueta alineada a la derecha, valor en x = 474) |
|---|---|
| **Fecha:** 06/10/2021 04:03 p. m. | **NIT/CI/CEX:** 5115889 |
| **Nombre/Razón Social:** Juan Perez | **Cod. Cliente:** 51158891 |
| **N° Factura:** 1 (número de la factura original) | **Fecha Factura:** 01/02/3919 10:14 a. m. *(fecha de ejemplo absurda, tal cual en el PDF)* |
| | **N° Autorización/CUF:** dsa564dsa54d6as5 (el CUF de la factura original o, si era del SFV, su N° de autorización) |

- Sección "**DATOS FACTURA ORIGINAL**" (9 pt negrita, y ≈ 251 pt): tabla con las columnas CÓDIGO PRODUCTO, CANTIDAD, UNIDAD DE MEDIDA, DESCRIPCIÓN, PRECIO UNITARIO, DESCUENTO y SUBTOTAL. Debajo, la caja **MONTO TOTAL ORIGINAL Bs** 775.00.
- Sección "**DATOS DE LA DEVOLUCIÓN O RESCISIÓN**": tabla con las mismas columnas y, debajo, la caja **MONTO TOTAL DEVUELTO Bs** 775.00 y **MONTO EFECTIVO DÉBITO-CRÉDITO Bs** 100.75.
- Literal a la izquierda de la caja: "Son: Setecientos Setenta y cinco 00/100 Bolivianos". Corresponde al **monto total devuelto**. Nótese la capitalización inconsistente del ejemplo.
- Leyendas y QR como en la factura. La primera leyenda **sigue diciendo "ESTA FACTURA…"** aunque el documento sea una nota. Ley 453 del ejemplo: "Ley N° 453: Los servicios deben suministrarse en condiciones de inocuidad, calidad y seguridad."

### 11.5 Nota de Crédito-Débito con descuento (`Nota CreditoDebitoDescuento.pdf`)

Es igual que la §11.4, con estas cajas:
- Bajo la tabla original: **MONTO TOTAL ORIGINAL Bs** 850.00 / **DESCUENTO ADICIONAL** 50.00 / **MONTO TOTAL A PAGAR Bs** 800.00.
- Bajo la tabla de devolución: **SUB TOTAL** 75.00 / **MONTO DESCUENTO DEBITO CREDITO** 4.41 / **MONTO TOTAL DEVUELTO** 70.59 / **MONTO EFECTIVO DÉBITO-CRÉDITO** 9.18.
- Literal: "Son: Setenta 59/100 Bolivianos", que corresponde a 70.59.

Los cálculos están en la §6.3.

### 11.6 Monto literal

NO DOCUMENTADO como regla. Lo que sigue se deduce de los 4 ejemplos:
- Formato: **`Son: <parte entera en palabras> <centavos con 2 dígitos>/100 Bolivianos`**.
  - Ej.: 309.51 → "Son: Trescientos nueve 51/100 Bolivianos"; 3480.00 → "Son: Tres mil cuatrocientos ochenta 00/100 Bolivianos"; 70.59 → "Son: Setenta 59/100 Bolivianos"; 775.00 → "Son: Setecientos Setenta y cinco 00/100 Bolivianos".
- La primera letra va en mayúscula y el resto en minúsculas (hay una excepción inconsistente en la nota). **Propuesta:** usar formato oración.
- ¿De qué monto es el literal en la factura: TOTAL o MONTO A PAGAR? En los ejemplos ambos valen lo mismo (gift card 0), así que es NO DOCUMENTADO. En las notas es el MONTO TOTAL DEVUELTO.
- La moneda siempre aparece como "Bolivianos". El literal para otras monedas es NO DOCUMENTADO en mi bloque.

---

## 12. Representación gráfica en formato **rollo** (ticket)

**NO DOCUMENTADO.** Ningún archivo de mi bloque trae un ejemplo en rollo ni sus medidas. Lo único documentado es el parámetro del QR **`t=1` = rollo**, que además es el valor por defecto.

**Propuesta M-INV (no normativa).** Mantener exactamente el mismo contenido y el mismo orden que la hoja, pero en una sola columna para papel de 80 mm (o 58 mm):
1. Emisor centrado: razón social, CASA MATRIZ o SUCURSAL N. x, No. Punto de Venta, dirección, teléfono y municipio.
2. Título "FACTURA" y "(Con Derecho a Crédito Fiscal)", centrados.
3. NIT, FACTURA N°, CÓD. AUTORIZACIÓN (CUF partido en líneas).
4. Fecha, Nombre/Razón Social, NIT/CI/CEX y Cod. Cliente, uno por línea.
5. Detalle por ítem: código y descripción en una línea; debajo, "cantidad × precio − descuento = subtotal".
6. Totales en el mismo orden que la hoja.
7. Literal "Son: …".
8. Las tres leyendas, centradas y en letra pequeña.
9. QR centrado de **al menos 3 × 3 cm**, con la URL construida con `t=1`.

---

## 13. Leyendas

Fuentes: los PDF de mi bloque, más estas referencias cruzadas marcadas.

| # | Leyenda | Regla |
|---|---|---|
| 1 | `ESTA FACTURA CONTRIBUYE AL DESARROLLO DEL PAÍS, EL USO ILÍCITO SERÁ SANCIONADO PENALMENTE DE ACUERDO A LEY` | Texto fijo, igual en los 4 PDF (también en las notas) |
| 2 | `Ley N° 453: …` | Varía por documento. En los ejemplos aparecen 4 textos distintos (los 3 de los PDF y, en el XML de ejemplo, "Ley N° 453: Tienes derecho a recibir información sobre las características y contenidos de los servicios que utilices."). Ref. cruzada `…fase-ii-inspeccion.md` punto 4: "Se cambie **aleatoriamente con cada emisión** la segunda leyenda al pie de la representación grafica en cumplimiento a la ley del consumidor N° 453". Ref. cruzada: el catálogo "Códigos de Leyendas Facturas" (sincronización) aporta los textos por actividad económica, y el XML lleva el campo `<leyenda>` |
| 3 | `“Este documento es la Representación Gráfica de un Documento Fiscal Digital emitido en una modalidad de facturación en línea”` | Ref. cruzada `…fase-ii-inspeccion.md` punto 5: "Se cambie la tercera leyenda vinculada a la forma de operación de 'en linea' a 'fuera de linea' y viceversa". **El texto exacto de la versión "fuera de línea" es NO DOCUMENTADO en los archivos descargados** |

Sobre la versión "fuera de línea": en la práctica suele usarse "…emitido fuera de línea, verifique su envío con su proveedor o en la página web www.impuestos.gob.bo". Ese texto **no aparece en la documentación descargada** y hay que confirmarlo con la normativa (RND 102100000011 y sus anexos) antes de usarlo.

---

## 14. Qué debe implementar M-INV

1. **`SiatCuf.Generar(...)`** en la capa de dominio o aplicación, con la firma y el código del §1.5: 9 campos rellenados, Módulo 11, Base 16 con `TrimStart('0')` y el código de control del CUFD. Debe usar **modalidad = 2**, tipo de emisión 1 (online) o 2 (offline), tipo de factura 1 (compra-venta con CF) o 3 (nota) y sector 1 o 24.
2. **Pruebas unitarias del CUF** con los **6 vectores** de §1.3 y §1.4 (el script `specs/06-anexos/verificar_cuf.py` los reproduce). Además, un test que confirme que `BigInteger.ToString("X")` sin `TrimStart` da un resultado distinto, para documentar la trampa.
3. **`SiatCuf.Decodificar(cuf, largoCodigoControl)`** usando Base10 con el "0" antepuesto: extrae los campos y verifica el Módulo 11. Sirve para auditoría y soporte, y para un botón "Validar CUF" en la pantalla de detalle de la factura.
4. **Una sola marca de tiempo por emisión.** La `fechaEmision` (hora de Bolivia, con milisegundos) se genera una vez y se usa idéntica en el CUF (`yyyyMMddHHmmssfff`) y en el XML. Se persiste con precisión de milisegundos (`timestamp(3)`). La zona horaria es NO DOCUMENTADO en mi bloque.
5. **Persistir en la tabla de facturas:** NIT del emisor, código de sucursal, código de POS, modalidad, tipo de emisión, tipo de factura/documento, código de documento sector, número de factura, fecha de emisión, CUFD usado, código de control del CUFD, CUF (`varchar(100)`), leyenda Ley 453 elegida, formato de impresión usado y URL del QR generada.
6. **`SiatRedondeo.R2`** = `Math.Round(decimal, 2, MidpointRounding.AwayFromZero)`. Queda **prohibido `double`** para montos, y en BD se usa `numeric(19,2)` (o `numeric(25,10)` para el detalle de notas). Se serializa con `CultureInfo.InvariantCulture`. Hay que añadir un analizador o test que falle si se usa `Math.Round` sin `MidpointRounding`.
7. **Cálculo de líneas y totales** según §6.2: `subTotal = R2(cantidad × precio − descuento)`, SUBTOTAL = Σ, TOTAL = SUBTOTAL − descuento adicional, MONTO A PAGAR = TOTAL − gift card. Pruebas con los números de `factura COMPRA VENTA.pdf` (118.48, 67.89, 0.98, 122.16 → 309.51).
8. **Cálculos de la nota de crédito-débito** (inferidos y pendientes de confirmar con la spec de notas). **[corregido por revisión]** No son inferencias: están documentados en `validaciones-documentos-sector__validaciones.md` (sector 24: `montoEfectivoCreditoDebito = montoTotalDevuelto × 0.13`) y `…__validaciones-cont.md` / `versionamiento-2022` 1.0.26 (sector 47: `descuentoItem = (((subTotal × descuentoAdicional) / montoTotalOriginal) / cantidadOriginal) × cantidadDevuelta`). Ver `05-…` §4.3 y §5.2 y `07-…` §3.8. prorrateo del descuento adicional `R2(descAdic × subtotalDevuelto / montoTotalOriginal)`, `montoTotalDevuelto = subtotalDevuelto − descuentoProrrateado` y `montoEfectivo = R2(montoTotalDevuelto × 0.13)`. Pruebas: 775 → 100.75; y 75 con descuento de 50 sobre 850 → 4.41 / 70.59 / 9.18.
9. **`MontoLiteral.Convertir(decimal)`**, que devuelve "Son: {Palabras} {cc:00}/100 Bolivianos" en español correcto: cien/ciento, veintiuno, "y" entre decenas y unidades a partir de 31, mil/millón/millones, y formato oración. Pruebas: 309.51, 3480.00, 70.59 y 775.00 con los textos de §11.6, más los casos límite 0.50, 1.00, 100.00, 101.00, 1 000 000.00 y 21.00.
10. **`SiatQr.ConstruirUrl(nit, cuf, numero, formato)`** con la base configurable por ambiente. Piloto: `https://pilotosiat.impuestos.gob.bo/consulta/QR`. Producción: se configura cuando el SIN la comunique, por lo que la opción debe quedar vacía u obligatoria antes de pasar a producción. El parámetro `t` vale 1 para rollo y 2 para hoja.
11. **Generador de imagen QR** (por ejemplo, con la librería QRCoder, que es una elección nuestra) impreso a **≥ 3 × 3 cm**: ≥ 85 pt en PDF, o ≥ 354 px a 300 dpi o 240 px a 203 dpi en ticket.
12. **Plantilla "Hoja" (Carta)** con la disposición exacta del §11: bloques emisor y datos fiscales, título y subtítulo, cliente, tabla de 7 columnas, caja de totales, literal, 3 leyendas con el QR a la derecha y número de página "n/N". Variantes: compra-venta con fila de Importe Base CF, sin CF (sin esa fila), nota crédito-débito y nota con descuento. Fuente Helvetica/Arial; montos con formato `#,##0.00` en InvariantCulture.
13. **Plantilla "Rollo"** (80 mm y 58 mm) según la propuesta del §12, con el mismo contenido.
14. **Tabla de títulos y subtítulos** como catálogo en BD (§10), indexada por código de documento sector. Se imprime el título en mayúsculas y el subtítulo entre paréntesis en formato título.
15. **Leyendas:** la primera es fija. La segunda sale del catálogo de leyendas sincronizado (ref. cruzada) y se elige **al azar en cada emisión** entre las de la actividad económica; se guarda la elegida, que es la misma que va en `<leyenda>` del XML y en el impreso, y en una reimpresión se usa la guardada. La tercera depende del tipo de emisión: "en línea" si es 1 y la variante "fuera de línea" si es 2 (el texto está pendiente de confirmar; debe ser configurable).
16. **Marca de agua "SIN VALOR LEGAL"** cuando el ambiente sea piloto o pruebas. Es una propuesta basada en el ejemplo.
17. **Utilidades de envío:** `Gzip.Comprimir(byte[])` y `Sha256.HexMinusculas(byte[])`, que las usan los servicios de recepción (ref. cruzada).
18. **Pantallas:** (a) vista previa e impresión de factura o nota, con selector Hoja o Rollo y botón de reimpresión; (b) detalle de la factura, que muestra el CUF, la URL del QR (con botón "Verificar en SIAT", que la abre en el navegador) y el botón "Validar CUF"; (c) configuración de facturación: URL base del QR por ambiente, formato de impresión por defecto por punto de venta, impresora, tamaño del QR y texto de la leyenda fuera de línea.
19. **Registro de reimpresiones** (quién, cuándo y en qué formato). Es una propuesta de trazabilidad, no un requisito documentado.

---

## 15. Dudas / huecos de la documentación

1. **URL del QR en producción:** NO DOCUMENTADA ("se la hará conocer al finalizar el proceso de autorización").
2. **Texto exacto de la tercera leyenda en modo "fuera de línea":** NO DOCUMENTADO en los archivos descargados (solo se sabe que debe cambiar).
3. **Formato "rollo":** no hay ejemplo, medidas ni disposición. Tampoco se definen las dimensiones de "media hoja": los ejemplos son hoja Carta completa.
4. **Monto literal:** no hay regla. No se sabe si refleja el TOTAL o el MONTO A PAGAR cuando hay gift card, ni cómo se escribe en moneda extranjera.
5. **Redondeo:** la página no dice en qué momento se redondea cada campo; el texto es contradictorio ("igual o inferior a 5") y la imagen dice "siempre hacia arriba". Los ejemplos confirman HALF_UP.
6. **MD5 y CRC32:** anunciados en el título de la página pero sin contenido.
7. **SHA-256:** no se indica sobre qué bytes se calcula ni en qué campo va (está en otras páginas).
8. **GZIP:** el ejemplo nombra la salida ".zip" aunque es GZIP; no se indica qué se comprime (XML individual o TAR).
9. **Código de control del CUFD:** no se documenta su longitud (en todos los ejemplos es de 15 caracteres hex).
10. **Módulo 11:** la versión C# enlazada (`?id=344`) no está en el archivo, y la página describe un orden de campos incorrecto que contradice al CUF.
11. **Base 16 en .NET:** el código publicado por el SIN tiene el error del "0" inicial de `BigInteger.ToString("X")`. `CompleteCero` no trunca y su parámetro `pRigth` no hace nada.
12. **Código de Control del SFV:** la especificación en PDF no está disponible (el adjunto es HTML). No aplica a M-INV.
13. **QR SFV:** no se documenta el separador entre campos.
14. **Zona horaria de la fecha del CUF:** no se indica si es la hora local de Bolivia (UTC-4) o la del servidor, ni si hay que sincronizarla con el servicio de fecha y hora del SIAT (eso está en otras páginas).
15. **Tabla de títulos:** erratas ("CRÉFACTURADITO", "DUTTY"), una fila huérfana "SIN DERECHO A CRÉDITO FISCAL" sin descripción y ningún código de documento sector. La Nota de Crédito-Débito no figura en la tabla.
16. **PDF de ejemplo:** el NIT impreso no coincide con el NIT contenido en el CUF; la nota tiene una fecha de factura original en el año 3919 y un "CUF" original de relleno ("dsa564dsa54d6as5"). Son maquetas y no deben usarse para validar datos.
17. **Tamaño del QR en los ejemplos:** los propios ejemplos del SIN usan 2.79 cm, por debajo de los 3 cm recomendados. M-INV usará ≥ 3 cm.
18. **Nivel de corrección de errores y versión del QR:** NO DOCUMENTADOS.
