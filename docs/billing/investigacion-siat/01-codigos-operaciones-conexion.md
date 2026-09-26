# Especificación 01: servicios de Códigos, servicio de Operaciones, conexión, autenticación y requisitos del sistema

**Proyecto:** M-INV, módulo de Facturación SIAT (Bolivia)
**Modalidad objetivo:** Facturación **Computarizada en Línea**, `codigoModalidad = 2`, sin firma digital.
**Bloque documental:** servicios de Códigos, servicio de Operaciones, Requerimientos, Conexión punto a punto, Códigos de autorización, Token delegado, Características SFVL y casos de prueba CUIS/CUFD.
**Convenciones:**
- Toda sección cita su archivo de origen en `scratchpad/siat/txt/`, o en `scratchpad/siat/adjuntos/` si es un adjunto.
- **NO DOCUMENTADO** indica que el dato no aparece en la documentación descargada.
- **⚠ ELECTRÓNICA** indica que el punto aplica solo a la modalidad Electrónica en Línea (`codigoModalidad = 1`) y no se implementa ahora.
- **[fuera de bloque]** indica un dato tomado de otra página del mismo corpus para completar el contexto. Esas páginas se detallan en otras especificaciones.

---

## 0. Resumen

| Concepto | Valor documentado | Fuente |
|---|---|---|
| Ambiente (`codigoAmbiente`) | `1` = Producción, `2` = Pruebas y Piloto | todas las páginas de `codigos__*` y `operaciones__*` |
| Modalidad (`codigoModalidad`) | `1` = Electrónica en Línea, `2` = Computarizada en Línea | `codigos__solicitud-cuis.md` y otras |
| Sucursal (`codigoSucursal`) | `0` = Casa Matriz, `1..n` = sucursales registradas en el Padrón | todas |
| Punto de venta (`codigoPuntoVenta`) | `0` = sin punto de venta, `1..n` = punto de venta registrado | todas |
| Vigencia del CUIS | **365 días calendario**; se renueva desde el **5.º día anterior** al vencimiento | `informacion__codigos-de-autorizacion.md`, `codigos__solicitud-cuis.md` |
| Vigencia del CUFD | **24 horas**; se solicita **a diario**. Si falla el servicio de CUFD, el último CUFD válido se amplía **hasta 72 h** [fuera de bloque] | `codigos__solicitud-cufd.md`; `emision-y-envio-de-facturas__ingreso-a-contingencia.md` |
| Autenticación | Token Delegado en la cabecera HTTP: **`apikey: TokenApi <token>`** | `emision-y-envio-de-facturas__solicitud-token.md` |
| Token piloto frente a producción | Son tokens distintos; producción requiere uno nuevo | `caracteristicas-sfvl__inicio-operaciones.md` |
| CUFD masivo | Máximo **1000** solicitudes por llamada | `codigos__solicitud-cufd-masivo.md` |
| Paquetes de contingencia | Máximo **500** facturas por paquete | `requerimientos__sistema-informatico.md` |
| Paquetes de emisión masiva | Hasta **1000** facturas por paquete | `requerimientos__sistema-informatico.md` |
| Sincronización de fecha y hora | **Obligatoria a diario**; se recomienda hacerla antes de pedir el CUFD | `requerimientos__sistema-informatico.md` |
| Sincronización de catálogos | Diaria | `requerimientos__sistema-informatico.md` |
| Ancho de banda mínimo | Urbano **1 Mbps**, rural **512 Kbps**; más de 1 Mbps para paquetes; HTTPS | `requerimientos__esquemas-de-conexion.md` |
| URLs y WSDL (piloto y producción) | **NO DOCUMENTADO** (ver §9) | ninguna |

---

## 1. Códigos de autorización: conceptos y vigencias
Fuente: `informacion__codigos-de-autorizacion.md`

| Código | Quién lo genera | Descripción y vigencia | Se usa en computarizada |
|---|---|---|---|
| **CUIS** (Código Único de Inicio de Sistemas) | Administración Tributaria (SIN) | Alfanumérico. Identifica la relación entre el Sistema de Facturación, las credenciales, el contribuyente, la sucursal y, opcionalmente, el punto de venta. **Vigencia de 365 días calendario.** Se obtiene con un Token que valida la autenticidad del contribuyente. | Sí |
| **CUFD** (Código Único de Facturación Diaria) | SIN | Alfanumérico. Permite emitir Documentos Fiscales Electrónicos **durante 24 horas**. Se obtiene con Token. | Sí |
| **CUF** (Código Único de Factura) | El Sistema Informático de Facturación, de forma automática al emitir | Individualiza cada factura en las modalidades en línea. El algoritmo está en `algoritmos-utilizados__generacion-cuf.md` [fuera de bloque]. Su paso 5 concatena el **código de control** devuelto por `solicitudCufd`. | Sí |
| **CAED** | SIN | Para las modalidades Manual y Prevalorada Preimpresa. | No |
| **CAFC** (Código Autorización Facturas Contingencia) | SIN | Se genera al solicitar la impresión de facturas **manuales de contingencia**. | Solo en contingencia manual (eventos 5, 6 y 7, ver §5.6) |
| **Número de Autorización** | Automático | Modalidad Computarizada SFV, la antigua. | No |

**Regla para registros obligatorios:** cuando un registro enviado a la Administración Tributaria pida el "Número de Autorización" de una factura emitida en modalidad Electrónica en Línea, Computarizada en Línea, Portal Web en Línea o Manual del sistema vigente, se consigna el valor **`99`**. Se exceptúan el Registro de Compras y Ventas y los aplicativos SIAT o Mis Facturas.

La nota sobre Prevaloradas en Línea, que exige solicitar una autorización con periodo, rangos y precios fijos, no aplica a M-INV.

---

## 2. Autenticación: Token Delegado
Fuentes: `emision-y-envio-de-facturas__solicitud-token.md` y `caracteristicas-sfvl__inicio-operaciones.md`. La primera página tenía 3 imágenes incrustadas en base64; las decodifiqué y revisé.

### 2.1 Obtención (manual, en el Portal SIAT)
1. Ingresar al Portal SIAT con las credenciales de **SIAT en Línea**.
2. Con credenciales **V2**, elegir **"Sistema de Facturación"**. Con V1 se omite este paso.
3. Elegir **"Gestión de Autorización de Sistemas Informáticos de Facturación"** para producción, o **"Gestión de Autorización de Sistemas Informáticos de Facturación (Piloto)"** para piloto.
4. Elegir **"Token Delegado en Producción"** o **"Token Delegado Piloto"**.
5. Elegir **"Generar Nuevo Ticket"** (así lo nombra el texto; el botón se llama "Generar Nuevo Token").
6. Completar el formulario **Token Delegado**. Esto se ve en la captura incrustada en la página.
   - Sección *Validez del Token*:
     - **Desde**: se autocompleta con fecha y hora, formato `dd/mm/aaaa HH:mm:ss`.
     - **NIT Delegado**: lista desplegable.
     - **Sistema**: lista desplegable.
     - **Código del Sistema**: se muestra al elegir el sistema. Este es el `codigoSistema`.
     - **Hasta**: fecha `dd/mm/aaaa` que elige el usuario. Es la **duración del token**, que es variable.
   - Sección *Token*: un campo **Token** de texto multilínea donde aparece el token generado.
   - Botones **Cancelar** y **Solicitar**.
7. Presionar **Solicitar**. Se muestra el token, que se copia al sistema.

### 2.2 Renovación
- Cuando el token caduca o está por caducar, se entra a la misma opción y se **inactiva** el token con el botón **X** (columna *Opciones*, que muestra 🔍 y ✖).
- Luego se presiona **"Generar Nuevo Token"**.
- La documentación no menciona ningún servicio web para renovar el token; la renovación es manual en el portal.

### 2.3 Uso en los servicios web: cabecera HTTP exacta
Cita textual de la documentación (Java):
```java
headers.put("apikey", Arrays.asList("TokenApi " + pToken));
```
- **Nombre de la cabecera:** `apikey`
- **Valor:** `TokenApi ` seguido del token, con **un espacio** después de `TokenApi`.
- En .NET, con `HttpClient` o un `IClientMessageInspector` de WCF: `request.Headers.Add("apikey", "TokenApi " + token);`
- La página también muestra una captura de SoapUI (`token-sopaui.png`) que no se descargó; el texto de la documentación basta.

### 2.4 Reglas
- **Todos** los servicios de Códigos y de Operaciones de este bloque exigen el Token Delegado. La única excepción es `verificarComunicacion` (§4.7), cuya página no lo menciona.
- El token de **producción es distinto** del token de piloto y se obtiene después del Inicio de Operaciones.
- Errores relacionados, tomados de `implementacion-servicios-facturacion__codigos-error-siat.md` [fuera de bloque]:
  - `989` Token inválido
  - `958` El usuario no se encuentra autorizado para consumir este servicio
  - `912` El sistema no está asociado al contribuyente
  - `975` El sistema no se encuentra autorizado o se encuentra observado

---

## 3. Parámetros comunes a todos los servicios

| Parámetro | Tipo documentado | Descripción literal | Valores |
|---|---|---|---|
| `codigoAmbiente` | Numérico | "Describe el tipo de ambiente utilizado" | `1` Producción; `2` Pruebas y Piloto |
| `codigoModalidad` | Numérico | "Modalidad utilizada por el Sistema Informático de Facturación para la emisión de facturas" | `1` Electrónica en Línea; `2` Computarizada en Línea. **M-INV envía siempre `2`.** |
| `codigoSistema` | Alfanumérico | "Código de Sistema que le fue asignado al momento de realizar la solicitud de autorización." | Se ve en el portal, en el formulario del Token (campo "Código del Sistema") |
| `nit` | Numérico | "NIT perteneciente al emisor de la Factura." En el registro de PV comisionista es el NIT del **Comitente**. | NIT de la empresa |
| `cuis` | Alfanumérico | "Valor único para una sucursal y/o punto de venta que se obtiene al realizar el inicio de uso de sistemas." | Resultado de `cuis` |
| `cufd` | Alfanumérico | "Valor diario otorgado por el SIN." | Resultado de `cufd` |
| `codigoSucursal` | Numérico | "Valor que identifica la sucursal donde se realiza la emisión de la Factura" | `0` Casa Matriz; `1,2,..,n` Sucursal |
| `codigoPuntoVenta` | Numérico | "Solo se envía cuando la transacción se realiza utilizando un punto de venta. Caso contrario enviar 0." | `0` o `1..n` |

**Campos de salida comunes:**
- `transaccion` (Boolean): indica si la operación fue exitosa.
- `mensajes`, `codigosRespuesta` o `codigosRespuestas` (Lista o DTO[codigosRespuesta]): lista de códigos y mensajes. **Su estructura interna NO ESTÁ DOCUMENTADA** en estas páginas. Los códigos posibles están en `codigos-error-siat.md` [fuera de bloque]; ver §7.

**Longitudes máximas de campos:** **NO DOCUMENTADO** en este bloque. Existen los errores `997` ("El nombre excede el límite de caracteres permitidos") y `998` ("La descripción excede el límite…"), pero no se indica el límite.

**Formatos de fecha documentados:**
- `fechaVigencia` (salida): "Fecha UTC extendida". No hay ejemplo del formato literal; **NO DOCUMENTADO**.
- `fechaInicioEvento` y `fechaFinEvento` (entrada, String): **`yyyy-MM-dd'T'HH:mm:ss.SSS`**. Ejemplo de construcción: `2026-09-25T08:15:00.000`.
- `fechaEvento` (consulta de eventos) y `fechaRevocacion`: tipo "Date", sin formato literal (**NO DOCUMENTADO**).
- Zona horaria: **NO DOCUMENTADO** en este bloque. La hora oficial se obtiene de la sincronización de Fecha y Hora; ver §8.4.

---

## 4. Servicio de CÓDIGOS
Las URLs y el WSDL están en la §9. Las operaciones se listan con el **nombre de método tal como aparece en la documentación**.

### 4.1 `cuis`: solicitud del CUIS
Fuente: `facturacion-en-linea__implementacion-servicios-facturacion__codigos__solicitud-cuis.md`
- **Nombre del método:** `cuis`. **Objeto de solicitud:** `SolicitudCuis`.
- **Propósito:** obtener el CUIS de una sucursal o punto de venta.

| Entrada | Tipo | Oblig. | Descripción |
|---|---|---|---|
| `codigoAmbiente` | Numérico | Sí | 1 / 2 |
| `codigoSistema` | Alfanumérico | Sí | Código del sistema autorizado |
| `nit` | Numérico | Sí | NIT del emisor |
| `codigoModalidad` | Numérico | Sí | 1 / 2 (M-INV: 2) |
| `codigoSucursal` | Numérico | Sí | 0 = Casa Matriz, 1..n |
| `codigoPuntoVenta` | Numérico | No | Solo si se usa un punto de venta; si no, `0` |

| Salida | Tipo |
|---|---|
| `codigoCUIS` | Alfanumérico |
| `fechaVigencia` | Fecha UTC extendida |
| `transaccion` | Boolean |
| `CodigosRespuestas` | DTO[CodigosRespuesta] |

**Reglas:**
- Requiere Token Delegado.
- El CUIS **se puede renovar a partir del quinto día anterior a su vencimiento**.
- El sistema avisa que el CUIS está por vencer **cuando se obtiene un CUFD** y la fecha de vigencia está cerca. Ese aviso es el código `3008` "Advertencia: El Cuis Esta A Punto De Caducar, Genere Un Nuevo Cuis Por Favor" [fuera de bloque].
- **Una vez renovado el CUIS se debe obtener también un nuevo CUFD** para seguir operando.
- Según `caracteristicas-sfvl__inicio-operaciones.md`, el CUIS se obtiene "solo 1 vez al inicio o cuando se haya vencido la duración del mismo".
- Errores probables [fuera de bloque]:
  - `980` Existe un CUIS vigente para la sucursal o punto de venta
  - `970` El CUIS en la base de datos se encuentra vigente, no puede solicitar otro
  - `3008` (aviso de caducidad próxima)
  - `910`, `911`, `912`, `917`, `918`, `919`, `933`, `937`, `938`, `961`–`964`
  - `988` CUIS fuera de tolerancia

### 4.2 CUIS masivo (`SolicitudCuisMasivo`)
Fuente: `…__codigos__cuis-masivo.md`
- **Nombre del método (según la documentación):** `SolicitudCuisMasivo`. El nombre real de la operación SOAP **NO ESTÁ DOCUMENTADO** (ver §9).
- **Propósito:** obtener varios CUIS a la vez sin pedirlos uno por uno.

| Entrada | Tipo | Oblig. | Descripción |
|---|---|---|---|
| `codigoAmbiente` | Numérico | Sí | 1 / 2 |
| `codigoModalidad` | Numérico | Sí | 1 / 2 |
| `codigoSistema` | Alfanumérico | Sí | |
| `nit` | Numérico | Sí | |
| `datosSolicitud` | Agrupador (lista) | Sí | "Agrupa a sucursal y codigoPuntoVenta para poder solicitar varios CUIS." |
| ↳ `codigoSucursal` | Numérico | Sí | 0, 1..n |
| ↳ `codigoPuntoVenta` | Numérico | No | `0` si no hay punto de venta |

| Salida | Tipo |
|---|---|
| `listaCodigosCuis` | Lista[alfanumérico] |
| `fechaVigencia` | Fecha UTC extendida |
| `transaccion` | Boolean |
| `codigosRespuesta` | DTO[codigosRespuesta] |

**Reglas:** Token Delegado; las mismas reglas de renovación que el CUIS individual.
**Límite de elementos por llamada:** **NO DOCUMENTADO** para el CUIS masivo; el límite de 1000 está documentado solo para el CUFD masivo.
**Duda:** la documentación no dice cómo se relaciona cada elemento de `listaCodigosCuis` con su par sucursal y punto de venta. Ver §11.

### 4.3 `solicitudCufd`: solicitud del CUFD
Fuente: `…__codigos__solicitud-cufd.md`
- **Nombre del método (según la documentación):** `solicitudCufd`.
- **Propósito:** habilita la emisión de facturas digitales **durante 24 horas**. Debe obtenerse **a diario**.

| Entrada | Tipo | Oblig. | Descripción |
|---|---|---|---|
| `codigoAmbiente` | Numérico | Sí | 1 / 2 |
| `codigoSistema` | Alfanumérico | Sí | |
| `nit` | Numérico | Sí | |
| `codigoModalidad` | Numérico | Sí | 1 / 2 (M-INV: 2) |
| `cuis` | Alfanumérico | Sí | CUIS vigente **de esa misma sucursal y punto de venta** |
| `codigoSucursal` | Numérico | Sí | 0, 1..n |
| `codigoPuntoVenta` | Numérico | No | "Solo se envía este valor cuando se desea obtener un CUFD para el punto de venta (1, 2,..,n). Caso contrario enviar 0." |

| Salida | Tipo | Uso |
|---|---|---|
| `codigoCUFD` | Alfanumérico | Va en el XML de cada factura (campo `cufd`) y en la cabecera de los servicios de emisión |
| `fechaVigencia` | Fecha UTC extendida | Fin de la vigencia |
| `transaccion` | Boolean | |
| `codigosRespuestas` | DTO[codigosRespuesta] | Puede traer el aviso `3008` de CUIS por vencer |
| **`codigoControl`** | Alfanumérico | Se **concatena al final del CUF** (paso 5 de `generacion-cuf.md` [fuera de bloque]; ejemplo de código de control en esa página: `"A19E23EF34124CD"`) |
| **`direccion`** | Alfanumérico | Dirección de la sucursal o casa matriz registrada en el **Padrón**. `informacion__modalidades-facturacion__facturacion-computarizada.md` [fuera de bloque]: "devuelve los códigos de Verificación y CUFD, además de la dirección de la sucursal o casa matriz". Debe ir en el campo `direccion` del XML; si no coincide, se produce el error `1007` o la advertencia `2014`. |

**Reglas:**
- Token Delegado.
- Al pedir el CUFD puede llegar la alerta de CUIS por vencer.
- Se recomienda **sincronizar fecha y hora antes de pedir el CUFD** (`requerimientos__sistema-informatico.md`).
- Si el servicio de CUFD falla (Time Out, -1, NullPointer o HTTP 500 tras varios reintentos), se entra **fuera de línea** y se sigue emitiendo con el **último CUFD válido**, cuya duración "se amplía hasta 72 horas". Se recomienda no pasar más de 2 horas antes de reintentar [fuera de bloque: `ingreso-a-contingencia.md`].
- Antes de registrar un evento significativo y enviar paquetes, **se debe obtener un nuevo CUFD** [fuera de bloque: `contingencia-y-eventos-significativos.md`].
- Errores probables [fuera de bloque]:
  - `913` CUIS inválido
  - `929` / `973` CUIS no vigente
  - `930` El CUIS no corresponde a la sucursal/punto de venta
  - `959` / `979` CUIS no asociado al sistema o a la sucursal
  - `953` CUFD no vigente
  - `123` CUFD fuera de tolerancia
  - `914` CUFD inválido

**Varios CUFD en el mismo día** (si se puede pedir un CUFD nuevo con uno vigente y qué pasa con el anterior): **NO DOCUMENTADO** en este bloque. La documentación solo indica pedir uno nuevo en contingencia y después de renovar el CUIS.

### 4.4 `solicitudCufdMasivo`: CUFD masivo
Fuente: `…__codigos__solicitud-cufd-masivo.md`
- **Nombre del método:** `solicitudCufdMasivo`.
- **Propósito:** el CUFD se obtiene por casa matriz, sucursal y punto de venta; este servicio los pide en lote.

| Entrada | Tipo | Oblig. | Descripción |
|---|---|---|---|
| `codigoAmbiente` | Numérico | Sí | |
| `codigoModalidad` | Numérico | Sí | |
| `codigoSistema` | Alfanumérico | Sí | |
| `nit` | Numérico | Sí | |
| `datosSolicitud` | Etiqueta contenedora (lista) | — | Un elemento por cada par sucursal y punto de venta |
| ↳ `codigoPuntoVenta` | Numérico | No | 1..n o `0`. La descripción de la página dice por error "sincronización de fecha y hora". |
| ↳ `codigoSucursal` | Numérico | Sí | 0, 1..n |
| ↳ `cuis` | Alfanumérico | Sí | CUIS del par sucursal y punto de venta |

| Salida | Tipo |
|---|---|
| `ListaCodigoCufd` | Lista [Alfanumérico] |
| `fechaVigencia` | Fecha UTC Extendida |
| `transaccion` | Boolean |
| `CodigosRespuestas` | DTO [CodigosRespuesta] |

**Reglas:**
- Token Delegado.
- **Máximo 1000 solicitudes por llamada.** Si se necesitan más, se parte la solicitud. Superar el límite produce el error `3002` "La Solicitud Excede El Limite De Cufd Masivo Permitido" [fuera de bloque].
- Puede llegar la alerta de CUIS por vencer.

**⚠ Hueco:** la documentación **no lista `codigoControl` ni `direccion` en la salida masiva**, y ambos son necesarios para el CUF y el XML. Hasta verificarlo con el WSDL real, M-INV usa el **CUFD individual** (§4.3). Con 3 sucursales y pocos puntos de venta basta.

### 4.5 `verificarNit`
Fuente: `…__codigos__verifica-nit.md`
- **Nombre del método:** `verificarNit`. **Objeto:** `SolicitudVerificarNit`.
- **Propósito:** verificar el NIT del **cliente** antes de enviar la factura.
- **Recomendación oficial:** verificar **con anticipación** a los clientes regulares o registrados, y a los eventuales **al momento de la emisión**.

| Entrada | Tipo | Oblig. | Descripción |
|---|---|---|---|
| `codigoAmbiente` | Numérico | Sí | |
| `codigoSistema` | Alfanumérico | Sí | |
| `nit` | Numérico | Sí | NIT del **emisor** |
| `codigoModalidad` | Numérico | Sí | |
| `codigoSucursal` | Numérico | Sí | |
| `nitParaVerificacion` | Numérico | Sí | NIT a verificar, el del cliente |

| Salida | Tipo |
|---|---|
| `mensajes` | Lista |
| `transaccion` | Boolean |

**Interpretación** con códigos de `codigos-error-siat.md` [fuera de bloque]:
- `986` NIT ACTIVO
- `987` NIT INACTIVO
- `994` NIT INEXISTENTE

**Relación con la emisión** [fuera de bloque: `facturacion-computarizada.md`, `contingencia-y-eventos-significativos.md`]:
- Si el tipo de documento es NIT y el número no es válido, o no se validó antes con este método, el emisor puede enviar el **código de excepción** para que la factura no sea rechazada.
- En emisión fuera de línea con tipo de documento NIT se envía **código de excepción = 1**.

**⚠ Hueco:** la tabla de la página **no incluye `cuis`** como parámetro de entrada, aunque todos los demás servicios lo piden. Hay que verificarlo con el WSDL (§11).

### 4.6 `notificaCertificadoRevocado` (⚠ ELECTRÓNICA)
Fuente: `…__codigos__notifica-certificado-revocado.md`
- **Nombre del método:** `solicitudNotificaRevocado`. **Objeto:** `notificaCertificadoRevocado`.
- **Propósito:** informar que una **firma digital** fue revocada o suspendida. **Inhabilita automáticamente el CUIS y el CUFD vigentes** hasta que haya una firma válida.
- **No aplica a la modalidad Computarizada**, que no usa firma. Se documenta para una futura modalidad electrónica.

| Entrada | Tipo | Oblig. | Descripción |
|---|---|---|---|
| `codigoAmbiente` | Numérico | Sí | |
| `codigoSistema` | Alfanumérico | Sí | |
| `nit` | Numérico | Sí | |
| `cuis` | Alfanumérico | Sí | |
| `codigoSucursal` | Numérico | Sí | |
| `fechaRevocacion` | Date | Sí | Fecha de revocación del certificado |
| `razonRevocacion` | Alfanumérico | Sí | Motivo |
| `certificado` | Alfanumérico | Sí | Certificado revocado |

| Salida | Tipo |
|---|---|
| `transaccion` | Boolean |
| `codigosRespuestas` | DTO[codigosRespuesta] |

Token Delegado.

### 4.7 `verificarComunicacion` [fuera de bloque]
Fuente: `…__facturacion-computarizada__verifica-comunicacion-fact-comp.md`. Existen páginas equivalentes para electrónica y para notas.
- **Entrada:** ninguna.
- **Salida:** `return = 926` ("Comunicación Exitosa"), numérico.
- **Uso:** consultarlo antes de emitir. Si responde con Time Out, -1, NullPointer o HTTP 500 tras varios reintentos, se pasa a fuera de línea [`ingreso-a-contingencia.md`].
- Existe en el servicio de facturación computarizada. Que también exista en los servicios de Códigos y Operaciones es **NO DOCUMENTADO** (ver §9).

---

## 5. Servicio de OPERACIONES

### 5.1 `registroPuntoVenta`
Fuente: `…__operaciones__registro-punto-de-venta.md`
- **Objeto:** `SolicitudRegistroPuntoVenta`.
- **Propósito:** los puntos de venta **deben registrarse** en el SIN antes de usarse, por este servicio o por el Portal Web.

| Entrada | Tipo | Oblig. | Descripción |
|---|---|---|---|
| `codigoAmbiente` | Numérico | Sí | |
| `codigoModalidad` | Numérico | Sí | |
| `codigoSistema` | Alfanumérico | Sí | |
| `codigoSucursal` | Numérico | Sí | Sucursal a la que pertenece el punto de venta |
| `codigoTipoPuntoVenta` | Numérico | Sí | Ver catálogo abajo |
| `cuis` | Alfanumérico | Sí | CUIS de la sucursal, con punto de venta 0 |
| `descripcion` | Alfanumérico | Sí | Descripción del punto de venta |
| `nit` | Numérico | Sí | NIT del emisor |
| `nombrePuntoVenta` | Alfanumérico | Sí | Nombre del punto de venta |

| Salida | Tipo |
|---|---|
| `codigoPuntoVenta` | Numérico, el correlativo que asigna el SIN |
| `transaccion` | Boolean |
| `mensajes` | Lista |

**Catálogo `codigoTipoPuntoVenta`** (documentado en esta página; también existe como paramétrica "Códigos de Tipo Punto de Venta" en la sincronización):

| Código | Tipo |
|---|---|
| 1 | Punto Venta Comisionista |
| 2 | Punto Venta Ventanilla de Cobranza |
| 3 | Punto de Venta Móviles |
| 4 | Punto de Venta YPFB |
| 5 | Punto de Venta Cajeros |
| 6 | Punto de Venta Conjunta |

Errores probables [fuera de bloque]:
- `947` Tipo de punto de venta inválido
- `948` Nombre de punto de venta vacío
- `949` Descripción de punto de venta vacía
- `997` / `998` (límite de caracteres)

### 5.2 `registroPuntoVentaComisionista`
Fuente: `…__operaciones__registro-punto-de-venta-comisionista.md`
- **Objeto:** `SolicitudPuntoVentaComisionista`.

| Entrada | Tipo | Oblig. | Descripción |
|---|---|---|---|
| `codigoAmbiente` | Numérico | Sí | |
| `codigoModalidad` | Numérico | Sí | |
| `codigoSistema` | Alfanumérico | Sí | |
| `codigoSucursal` | Numérico | Sí | |
| `cuis` | Alfanumérico | Sí | |
| `descripcion` | Alfanumérico | Sí | |
| `fechaFin` | *(tipo no indicado)* | Sí | Fecha de fin de contrato |
| `fechaInicio` | *(tipo no indicado)* | Sí | Fecha de inicio de contrato |
| `nit` | Numérico | Sí | NIT del **Comitente** |
| `nitComisionista` | Numérico | Sí | NIT del Comisionista |
| `nombrePuntoVenta` | Alfanumérico | Sí | |
| `numeroContrato` | Alfanumérico | Sí | Número del contrato firmado con el Comitente |

| Salida | Tipo |
|---|---|
| `mensajes` | Lista |
| `codigoPuntoVenta` | Numérico |
| `transaccion` | Boolean |

M-INV no lo necesita en la primera etapa; queda como opcional.

### 5.3 `ConsultaPuntoVenta`
Fuente: `…__operaciones__consulta-puntos-de-venta.md`
- **Propósito:** consultar los puntos de venta asociados al sujeto pasivo.

| Entrada | Tipo | Oblig. | Descripción |
|---|---|---|---|
| `codigoAmbiente` | Numérico | Sí | |
| `codigoSistema` | Alfanumérico | Sí | |
| `codigoSucursal` | Numérico | Sí | Sucursal que se consulta |
| `cuis` | Alfanumérico | Sí | |
| `nit` | Numérico | Sí | |

| Salida | Tipo |
|---|---|
| `transaccion` | Boolean |
| `mensajes` | Lista |
| `listaPuntosVentas` | Lista. **La estructura de cada elemento NO ESTÁ DOCUMENTADA.** |

Error probable [fuera de bloque]: `982` No existen puntos de venta asociados.
No lleva `codigoModalidad`.

### 5.4 `CierrePuntoVenta`
Fuente: `…__operaciones__cierre-punto-de-venta.md`
- **Propósito:** cierre **definitivo** de un punto de venta.
- **Precondición:** el punto de venta **no puede tener un CUIS ni un CUFD activo**.
- **Irreversible:** una vez cerrado, **no se puede volver a crear con el mismo correlativo**.

| Entrada | Tipo | Oblig. | Descripción |
|---|---|---|---|
| `codigoAmbiente` | Numérico | Sí | |
| `codigoPuntoVenta` | Numérico | Sí | El punto de venta a cerrar. La descripción copiada dice "caso contrario enviar 0", lo que no tiene sentido aquí. |
| `codigoSistema` | Alfanumérico | Sí | |
| `codigoSucursal` | Numérico | Sí | |
| `cuis` | Alfanumérico | Sí | |
| `nit` | Numérico | Sí | |

| Salida | Tipo |
|---|---|
| `transaccion` | Boolean |
| `mensajes` | Lista |

Sin `codigoModalidad`. Token Delegado.
Cómo se pone "inactivo" el CUIS o CUFD de un punto de venta antes del cierre: **NO DOCUMENTADO**. Probablemente se hace esperando su vencimiento o con `cierreOperacionesSistema` (§5.5) para ese punto de venta; ver §11.

### 5.5 `cierreOperacionesSistema`: cierre de operaciones
Fuente: `…__operaciones__cierre-de-operaciones.md`
- **Objeto:** `SolicitudOperaciones`.
- **Propósito:** cerrar las operaciones de una sucursal o punto de venta. **Inhabilita automáticamente el CUIS y el CUFD vigentes**; desde ese momento no se puede emitir.

| Entrada | Tipo | Oblig. | Descripción |
|---|---|---|---|
| `codigoAmbiente` | Numérico | Sí | |
| `codigoSistema` | Alfanumérico | Sí | |
| `nit` | Numérico | Sí | |
| `codigoModalidad` | Numérico | Sí | |
| `cuis` | Alfanumérico | Sí | |
| `codigoSucursal` | Numérico | Sí | |
| `codigoPuntoVenta` | Numérico | No | `0` si no aplica |

| Salida | Tipo |
|---|---|
| `codigoSistema` | Alfanumérico |
| `transaccion` | Boolean |
| `mensajes` | Lista |

Es una acción **destructiva**; en la interfaz debe exigir doble confirmación y un rol de administrador. Token Delegado.

### 5.6 `registroEventoSignificativo`
Fuente: `…__operaciones__registro-evento-significativo.md`
- **Objeto:** `SolicitudEventoSignificativo`.
- **Propósito:** informar al SIN de una contingencia. Devuelve un `codigoRecepcion`, que luego se usa al enviar el paquete de facturas fuera de línea (servicio de recepción de paquetes, en otra especificación).

| Entrada | Tipo | Oblig. | Descripción |
|---|---|---|---|
| `codigoAmbiente` | Numérico | Sí | |
| `codigoSistema` | Alfanumérico | Sí | |
| `nit` | Numérico | Sí | |
| `cuis` | Alfanumérico | Sí | |
| `cufd` | Alfanumérico | *(en blanco en la documentación)* | CUFD **actual**, el nuevo obtenido tras superar la contingencia |
| `codigoSucursal` | Numérico | No | |
| `codigoPuntoVenta` | Numérico | No | `0` si no aplica |
| `codigoEvento` | Numérico | Sí | "Paramétrica que identifica el tipo de evento" (ver catálogo abajo) |
| `descripcion` | Alfanumérico | Sí | Descripción del evento |
| `fechaInicioEvento` | String | Sí | Formato **`yyyy-MM-dd'T'HH:mm:ss.SSS`** |
| `fechaFinEvento` | String | Sí | Formato **`yyyy-MM-dd'T'HH:mm:ss.SSS`** |
| `cufdEvento` | Alfanumérico | Sí | "Valor del CUFD que se usó en la contingencia" |

| Salida | Tipo |
|---|---|
| `codigoRecepcion` | Alfanumérico |
| `transaccion` | Boolean |
| `mensajes` | Lista |

**Catálogo de eventos significativos** [fuera de bloque: `contingencia-y-eventos-significativos.md`]. Los códigos definitivos vienen de la sincronización "Códigos de Eventos Significativos".

> **[corregido por revisión]** La versión anterior decía que "la numeración coincide con la tabla oficial". **No es así.** La tabla de abajo sigue el **orden de la página de contingencia** (7 = energía). El Excel oficial `CasosDePruebaEventosSignificativos.xlsx` (Etapa V) usa **otros códigos para los eventos manuales**: 5 = corte de energía, 6 = virus o falla de software, 7 = cambio de infraestructura o falla de hardware. El Excel `CasosDePruebaEmisionPorPaquetes.xlsx` usa un tercer orden (1 = energía, 2 = internet, …). Los eventos 1 a 4 (offline) coinciden entre la página y el Excel de eventos; los 5 a 7 no. Hay que tomar **siempre** el código del catálogo sincronizado y decidir la acción (offline o CAFC) **por la descripción**. Ver `03-emision-contingencia-anulacion.md` §6.2 y `08-…` §2.5.

| # (orden de la página de contingencia, NO es el código del catálogo) | Evento | Acción |
|---|---|---|
| 1 | Corte del servicio de Internet | Emitir fuera de línea |
| 2 | Inaccesibilidad al Servicio Web de la Administración Tributaria | Emitir fuera de línea |
| 3 | Ingreso a zonas sin Internet por despliegue de puntos de venta | Emitir fuera de línea |
| 4 | Venta en lugares sin internet | Emitir fuera de línea |
| 5 | Virus informático o falla de software | Facturas de contingencia manuales (CAFC) o Portal Web en línea transitorio |
| 6 | Cambio de infraestructura de sistema o falla de hardware | Igual que el 5 |
| 7 | Corte de suministro de energía eléctrica | Facturas de contingencia manuales (CAFC) |

**Reglas** [fuera de bloque]:
- Registrar el evento **hasta 48 horas después** de terminada la contingencia.
- **Obtener un CUFD nuevo antes de registrar el evento** y enviar los paquetes.
- Las facturas fuera de línea se emiten con el **CUFD vigente hasta antes del corte**, que es el que va en `cufdEvento`.

Errores probables [fuera de bloque]:
- `950` / `951` código o descripción vacíos
- `960` fin de evento requerido
- `974` / `981` rango de fechas inválido
- `976` código de evento incorrecto
- `984` el evento no corresponde al CUFD del evento registrado

**Validaciones de M-INV:** `fechaFinEvento` > `fechaInicioEvento`; ambas dentro de la vigencia del `cufdEvento` (probable, **NO DOCUMENTADO**); registro dentro de las 48 h siguientes a `fechaFinEvento`.

### 5.7 `consultaEventoSignificativo`
Fuente: `…__operaciones__consulta-evento-significativo.md`
- **Objeto:** `SolicitudConsultaEvento`.
- **Propósito:** consultar los eventos significativos registrados.

| Entrada | Tipo | Oblig. | Descripción |
|---|---|---|---|
| `codigoAmbiente` | Numérico | Sí | |
| `codigoSistema` | Alfanumérico | Sí | |
| `nit` | Numérico | Sí | |
| `cuis` | Alfanumérico | Sí | |
| `cufd` | Alfanumérico | *(en blanco)* | |
| `codigoSucursal` | Numérico | No | |
| `codigoPuntoVenta` | Numérico | No | |
| `fechaEvento` | Date | Sí | Fecha del evento |

| Salida | Tipo |
|---|---|
| `listaEventos` | Array[`codigoEvento`, `descripción`, `fecha`] |
| `transaccion` | Boolean |
| `mensajes` | Lista |

Error probable: `957` No existe registro de evento significativo.

---

## 6. Sucursales, puntos de venta y esquemas de despliegue

### 6.1 Sucursales
Fuente: `requerimientos__sucursales-y-puntos-de-venta.md`
- Una sucursal es un establecimiento secundario **con dirección física y registrado en el Padrón Nacional de Contribuyentes**.
- La emisión se hace por sucursal o casa matriz, siendo la **sucursal 0 la Casa Matriz**.
- El número de sucursal **no lo inventa el sistema**: debe coincidir con el Padrón.

### 6.2 Puntos de venta
Fuente: la misma.
- Es un lugar, dispositivo o medio de venta asociado a una sucursal o casa matriz. Puede ser **fijo** (por ejemplo, ferias) o **móvil** (camiones de reparto).
- **No está en el Padrón**, pero **debe registrarse** mediante el servicio web (§5.1) o el Portal Web **antes de usarse**.
- Tipos: comisionistas, ventanilla de cobranza (ASFI), móviles, YPFB, **cajeros o similares**, venta conjunta.
- Las empresas que distribuyen en zonas **sin cobertura de Internet** registran una solicitud en el Portal Web. Esa solicitud es **declaración jurada** con justificación.
- ⚠ ELECTRÓNICA: se asume que los puntos de venta móviles pueden firmar, o que tienen un servicio de firma centralizado.

### 6.3 Esquemas de despliegue
Fuente: `requerimientos__esquemas-de-despliegue.md`
- **Monolítico:** el cliente es también el servidor. Ejemplo: una tienda pequeña sin cajeros ni sucursales. ⚠ ELECTRÓNICA: la firma la maneja el mismo servidor (HSM, token o software).
- **Cliente-servidor (centralizado):** las sucursales y puntos de venta envían la factura a la "central", que la remite al SIN. La contingencia ocurre **cuando la central no tiene comunicación** con el SIN.
  - **Para cada sucursal o punto de venta se genera un CUIS y un CUFD**, así se identifica el origen. El sistema **debe manejar múltiples CUIS y CUFD**; existe además el método masivo.
  - ⚠ ELECTRÓNICA: firma por token (en cada transacción), HSM o software.
- **Distribuido (descentralizado):** cada sucursal o punto de venta envía directamente al SIN, y la contingencia puede darse en cada uno por separado. ⚠ ELECTRÓNICA: cada uno maneja su propia firma.

**Para M-INV** (WPF, 3 sucursales CM/EA/SC, base de datos en la nube): conviene el **modelo centralizado**. Un componente único (servicio o *worker*) guarda los CUIS y CUFD por par sucursal y punto de venta, y habla con el SIN; las cajas usan los códigos de ese componente.

### 6.4 Esquemas de conexión
Fuente: `requerimientos__esquemas-de-conexion.md`

| Medio | Detalle |
|---|---|
| **Internet** | Velocidad mínima: **urbana 1 Mbps**, **rural 512 Kbps**. Se sugiere que la conexión sea exclusiva para el sistema de facturación. Para enviar **paquetes** se necesita **más de 1 Mbps**. Protocolo **HTTPS** con cifrado SSL. |
| **Fibra óptica punto a punto** | Recomendada para muchos puntos de venta o gran volumen (telecomunicaciones, banca, servicios básicos, hidrocarburos). Hay que verificar con el SIN la disponibilidad de puntos de acceso. Se puede usar la fibra de un tercero ya conectado si su proveedor lo declaró como cliente. |
| **MPLS** | Alternativa cuando la fibra no es viable; se contrata con empresas de telecomunicaciones. |

El uso de punto a punto o MPLS se **coordina con el SIN antes de la puesta en producción**. M-INV usa **Internet**.

### 6.5 Procedimientos de conexión punto a punto y MPLS
Fuentes: `conexion-punto-a-punto__procedimiento-conexion.md` y `…__procedimiento-conexion-proveedores.md`. No aplica a M-INV; se resume solo para referencia.

**Marco:** RND N° 102100000011. Las solicitudes se atienden por orden de llegada, porque los puntos de acceso son limitados.

**Contribuyente, alta:** formulario en el portal del SIN con:
- tipo de solicitud (alta);
- NIT y razón social;
- modalidad (Punto a Punto o MPLS);
- proveedor autorizado elegido de una lista (se asume un contrato previo; el SIN no intermedia);
- contactos técnicos, con celular y correo;
- aceptación de los términos de confidencialidad.

El sistema genera un número de "Formulario de solicitud" y avisa al DIT.

**Etapas de la VPN o MPLS:** "Pruebas de conectividad" y luego "Conexión final". Si no se cumplen los plazos, el puerto se libera.

**Plazos MPLS:**
- el DIT contacta en **≤ 2 días hábiles**;
- el inicio de las tareas es en **≤ 2 días hábiles**;
- el DID valida la recepción de datos de facturación y confirma en **≤ 2 días hábiles**.

**Baja:** formulario de baja. La baja se ejecuta en **≤ 3 días hábiles**; el proveedor retira sus equipos en **≤ 5 días hábiles**.

**Consideraciones:**
- Conexión con **ambos CPD** del SIN: primero el CPD Centro y luego el CPD Zona Sur.
- La "inteligencia de conexión" es del proveedor; la página de proveedores dice que es del contribuyente.
- El SIN puede dar de baja la conexión.

**Proveedores de telecomunicaciones:**
- Requisitos:
  - ser contribuyente activo con actividad 610000, 619030 o 619990;
  - no tener marcas de control;
  - indicar el tipo de enlace;
  - tener personal técnico.
- Formulario con el certificado ATT en PDF (≤ 5 MB) y aceptación de confidencialidad.
- Contacto del DIT en ≤ 2 días hábiles; visita guiada en ≤ 5 días hábiles.
- Espacio en rack:
  - punto a punto: 2 RU en CPD Zona Sur y 4 RU en CPD Centro;
  - MPLS: 2 RU en cada CPD.
- Etiquetado según TIA-606; etiquetas de equipo de ≥ 3×3 cm, en color.
- Punto a punto:
  - 24 conexiones iniciales, ampliables en 24 más con autorización de la GTIC;
  - instalación en 5 días hábiles, extensible 5 más;
  - ODF de ≤ 2 RU;
  - patch cord UTP Cat5e o superior, amarillo, hasta el switch de borde (puertos RJ-45 1GE);
  - energía AC.
- MPLS:
  - el DIT asigna un segmento de red;
  - pruebas de capa 4 y luego de capa 7;
  - el proveedor segmenta y aísla el tráfico por cliente, hace ingeniería de tráfico y aplica controles de seguridad (anti-DoS);
  - puede haber políticas de ancho de banda.

### 6.6 Contingencia desde el punto de vista del despliegue
Ver §5.6. En el esquema centralizado, la contingencia la declara la central.

---

## 7. Códigos de respuesta relevantes para este bloque
Fuente: `implementacion-servicios-facturacion__codigos-error-siat.md` [fuera de bloque]

| Código | Descripción | Dónde aplica |
|---|---|---|
| 123 | CUFD fuera de tolerancia | CUFD |
| 910 | El parámetro ambiente es inválido | todos |
| 911 | El parámetro código de sistema es inválido | todos |
| 912 | El sistema no está asociado al contribuyente | todos |
| 913 | CUIS inválido | CUFD, Operaciones |
| 914 | CUFD inválido | Operaciones y emisión |
| 917 | El parámetro modalidad es inválido | Códigos |
| 918 | El parámetro sucursal es inválido | todos |
| 919 | El parámetro NIT es inválido | todos |
| 926 | Comunicación exitosa | verificarComunicacion |
| 928 | Certificado revocado | ⚠ ELECTRÓNICA |
| 929 / 973 | El CUIS no está vigente | CUFD, Operaciones |
| 930 | El CUIS no corresponde a la sucursal o punto de venta | CUFD |
| 933 | El punto de venta es inexistente o inválido | CUIS, CUFD, cierre de PV |
| 937 | El NIT no tiene asociada la modalidad de facturación | Códigos |
| 938 | El NIT presenta marcas de control | Códigos |
| 942 | El código de recepción del evento significativo no existe | Paquetes |
| 947 | Tipo de punto de venta inválido | registroPuntoVenta |
| 948 | Nombre de punto de venta vacío | registroPuntoVenta |
| 949 | Descripción de punto de venta vacía | registroPuntoVenta |
| 950 | Código de evento significativo vacío | registroEvento |
| 951 | Descripción de evento significativo vacía | registroEvento |
| 953 | CUFD no vigente | emisión, eventos |
| 957 | No existe registro de evento significativo | consultaEvento |
| 958 | Usuario no autorizado para consumir el servicio | todos (token) |
| 959 / 979 | CUIS no asociado al sistema o a la sucursal | CUFD, Operaciones |
| 960 | Fin de evento requerido | registroEvento |
| 961–964 | Marcas del NIT: domicilio inexistente, bloqueo de dosificación por fiscalización o por jurídica, DDJJ | Códigos |
| 965 | El contribuyente no tiene firma vigente registrada | ⚠ ELECTRÓNICA |
| 966 / 967 / 991 / 992 / 995 / 999 | Errores del SIN (datos del contribuyente, tiempo de espera en base de datos, base de datos, padrón, servicio no disponible, ejecución) | todos: reintentar o pasar a contingencia |
| 970 | El CUIS de la base de datos está vigente; no se puede pedir otro | cuis |
| 974 / 981 | Rango de fechas del evento inválido | registroEvento |
| 975 | Sistema no autorizado u observado | todos |
| 976 | Código de evento incorrecto | registroEvento |
| 980 | Ya existe un CUIS vigente para la sucursal o punto de venta | cuis |
| 982 | No existen puntos de venta asociados | ConsultaPuntoVenta |
| 984 | El evento no corresponde al CUFD del evento registrado | eventos y paquetes |
| 986 / 987 / 994 | NIT ACTIVO / NIT INACTIVO / NIT INEXISTENTE | verificarNit |
| 988 | CUIS fuera de tolerancia | Códigos |
| 989 | Token inválido | todos |
| 996 | Rango de fechas inválido | eventos |
| 997 / 998 | Nombre o descripción exceden el límite de caracteres | registroPuntoVenta |
| 1004 | La sucursal del XML no corresponde a los datos del CUFD | emisión |
| 1007 / 2014 | La dirección del XML no corresponde a la del Padrón (error / advertencia) | emisión: usar la `direccion` del CUFD |
| 1008 | El punto de venta del XML es inexistente o inválido | emisión |
| 3002 | La solicitud excede el límite de CUFD masivo | solicitudCufdMasivo |
| 3008 | Advertencia: el CUIS está a punto de caducar; genere uno nuevo | CUFD |

---

## 8. Requisitos del Sistema Informático de Facturación
Fuente: `requerimientos__sistema-informatico.md`

El sistema debe estar **autorizado por el SIN** y tener como mínimo los componentes siguientes.

### 8.1 a) Emisor de facturas digitales
Genera el XML para la modalidad Computarizada en Línea. **Mínimo: emisión individual y emisión por contingencia.** La emisión masiva es opcional, según el giro del negocio.

**Emisión individual, en orden:**
1. Generar el XML según la actividad económica.
2. Firmar con XMLDSig. **⚠ ELECTRÓNICA solamente**; en computarizada se omite.
3. **Validar el XML contra el XSD.**
4. **Comprimir con Gzip.** El resultado va en la etiqueta `archivo`.
5. Calcular el **SHA256 del archivo comprimido**. Va en la etiqueta `hashArchivo` y **sirve también de Huella Digital en computarizada**.

**Paquetes por contingencia:**
- Fuera de línea, las facturas se agrupan en **paquetes de máximo 500**.
- Superada la contingencia: **registrar el evento** (§5.6) y **luego enviar los paquetes**.

**Paquetes por emisión masiva:** hasta **1000** facturas por paquete. Pensado para procesos automáticos en horarios extraordinarios (financieras, telecomunicaciones, luz, agua).

### 8.2 b) Gestor de facturas
Envía y valida las transacciones de registro, como la **anulación**. Ver las especificaciones de los servicios de facturación computarizada.

### 8.3 c) Sincronización de catálogos: diaria
Productos y servicios, países, eventos significativos, mensajes de servicios y otros. Detalle en `sincronizacion-codigos-catalogos.md` [fuera de bloque]: 18 paramétricas, con entradas `codigoAmbiente`, `codigoSistema`, `nit`, `cuis`, `codigoSucursal` y `codigoPuntoVenta` (opcional, 0), y Token Delegado.

### 8.4 d) Sincronización de fecha y hora: **obligatoria a diario**
- Alinea la hora del sistema con la del SIN. Esa hora se usa para controlar los plazos de envío y registro.
- Se puede hacer **varias veces al día**.
- **Recomendación: antes de pedir el CUFD.**
- Es la paramétrica "Fecha y Hora" del servicio de sincronización [fuera de bloque].

### 8.5 e) Registro de eventos significativos
Ver §5.6.

### 8.6 f) Gestor de envío de documentos e impresión
- Gestiona la impresión, el envío o la publicación de la **representación gráfica y del XML**.
- La impresión **no es obligatoria**. Pero si el sistema **no puede enviar** la representación gráfica y el XML, **debe poder imprimirlos** y luego enviarlos por algún medio tecnológico.
- Opcional: un portal web para que el cliente consulte y descargue sus facturas.

### 8.7 Homologación de productos y servicios
Fuente: `requerimientos__homologacion-de-productos-servicios.md`. Adjunto: `adjuntos/Catalogo Productos.xls`.
1. El sistema descarga el listado de productos y servicios del SIN con el servicio de sincronización. Cada código trae su **Código de Actividad Económica (CAEB)**.
2. Un "equipo de homologación" de la empresa **relaciona cada producto interno con un código SIN**. Varios productos internos pueden compartir un código SIN.
3. **El XML lleva el código SIN**; **la representación gráfica muestra el código interno**.
4. Se debe distinguir **origen importado o nacional**, porque el SIN los separa en códigos distintos.

**Ejemplo de la documentación:**

| Código SIN | Descripción SIN | CAEB |
|---|---|---|
| 1111 | Semillas de trigo, para siembra | 106110 |
| 1377 | Nueces del Brasil con cáscara | 463010 |

| Código interno | Descripción | Código SIN |
|---|---|---|
| 12384E1 | Nuez embolsada de 50gr | 1377 |
| 4654D3 | Nuez procesada con sal de 100gr | 1377 |

---

## 9. URLs, WSDL, namespaces y protocolo

**Qué está DOCUMENTADO en el corpus:**
- Los servicios son **SOAP**. `versionamiento-2021.md` menciona un "Menú de los Servicios SOAP" y `sincronizacion-codigos-catalogos.md` dice "Servicios SOAP" [fuera de bloque].
- Transporte **HTTPS** (`esquemas-de-conexion.md`).
- Autenticación con la cabecera `apikey: TokenApi <token>` (§2.3).
- Las **direcciones de los servicios productivos se entregan en el portal** al hacer "Inicio de Operaciones" (`caracteristicas-sfvl__inicio-operaciones.md`: "se habilita el sistema en producción y se brindan las direcciones de los servicios productivos").
- Portales: producción `https://siat.impuestos.gob.bo/` (launcher `https://siat.impuestos.gob.bo/v2/launcher`); dominio de piloto `https://pilotosiat.impuestos.gob.bo/`, usado para consultar QR [fuera de bloque: `codigo-respuesta-rapida-qr.md`].

**Qué NO está DOCUMENTADO en el corpus:**
- La URL o WSDL del servicio de **Códigos** y del servicio de **Operaciones**, en piloto y en producción.
- El **targetNamespace** SOAP, los nombres exactos de las operaciones WSDL, de los elementos *wrapper* de solicitud y respuesta, y de la lista de mensajes.
- La versión de SOAP (1.1 o 1.2).
- La estructura de `codigosRespuesta` y `mensajes`.
- La estructura de `listaPuntosVentas`.

> **Referencia externa NO verificada contra la documentación.** Es conocimiento general y **no sale de los archivos**; confírmese descargando el WSDL antes de codificar. Todo debe quedar **configurable** en la base de datos o en el `appsettings`, nunca fijo en el código.
> - Piloto: `https://pilotosiatservicios.impuestos.gob.bo/v2/FacturacionCodigos?wsdl` y `https://pilotosiatservicios.impuestos.gob.bo/v2/FacturacionOperaciones?wsdl`, y un patrón análogo para `FacturacionSincronizacion` y `ServicioFacturacionComputarizada`.
> - Producción: el mismo patrón con el host `https://siatrest.impuestos.gob.bo/v2/...`.
> - targetNamespace habitual: `https://siat.impuestos.gob.bo/`.
> - Nombres de operación habituales: Códigos → `cuis`, `cufd`, `cuisMasivo`, `cufdMasivo`, `verificarNit`, `verificarComunicacion`, `notificaCertificadoRevocado`. Operaciones → `registroPuntoVenta`, `consultaPuntoVenta`, `cierrePuntoVenta`, `registroPuntoVentaComisionista`, `registroEventoSignificativo`, `consultaEventoSignificativo`, `cierreOperacionesSistema`, `verificarComunicacion`.
> - En las respuestas reales es habitual que el código venga en un elemento `codigo`, no en `codigoCUIS` ni `codigoCUFD`, y los mensajes en una lista `mensajesList` de elementos `{codigo, descripcion}`.
>
> **Recomendación de implementación:** generar los *proxies* con `dotnet-svcutil` desde el WSDL real del ambiente piloto, y añadir la cabecera `apikey` con un `IClientMessageInspector` o un `HttpMessageHandler`. Así las diferencias de nombres entre la documentación y el WSDL se resuelven solas.

---

## 10. Puesta en marcha: asociación, inicio de operaciones y pruebas

### 10.1 Asociación de sistemas
Fuente: `caracteristicas-sfvl__asociacion-de-sistemas.md`

Aplica cuando **un proveedor** autoriza a un contribuyente a usar su sistema. Si la empresa usuaria de M-INV fuera distinta del titular de la autorización, se usaría este flujo.

**Asociación (la hace el proveedor)**, en PRODUCCIÓN del portal (`https://siat.impuestos.gob.bo/`):
- Ruta: "Gestión de Autorización de Sistemas (PILOTO)" → menú **Asociación de Sistemas (RND-102100000011)**.
- Datos:
  - NIT del contribuyente (válido y activo);
  - login del usuario;
  - nombre del sistema;
  - modalidad;
  - **tipo de servicio**:
    - **Licencia** (EULA, GNU, etc., con documento de respaldo);
    - **Alquiler** (sistema y hardware, temporal);
    - **Prestación de servicio** (WS SOAP o REST; hay que definir el plazo; el contribuyente cede un usuario de Oficina Virtual, y además su firma solo en ⚠ ELECTRÓNICA);
    - **Facturación por terceros** (el contribuyente cede un token delegado);
    - **Facturación Conjunta**;
    - **Comisionistas**;
  - sectores autorizados del contribuyente;
  - correo para confirmar.

**Confirmación (la hace el contribuyente):** Portal SIAT → "Sistema de Facturación Versión 2" → **Confirmación de la Asociación**. Primero solo se habilita "Pruebas Piloto", que genera un documento de especificaciones de prueba. Si el contribuyente está conforme, **acepta** y se emite la autorización de asociación; también puede rechazar.

### 10.2 Inicio de operaciones (paso de piloto a producción)
Fuente: `caracteristicas-sfvl__inicio-operaciones.md`
1. En el portal: menú **Inicio y Cierre de Operaciones** → sistemas asociados al NIT → botón **Inicio de Operaciones**.
2. Formulario con **fecha de inicio de operaciones**, **tipo de servicio de Internet** y **empresa proveedora de Internet**. Al aceptar, se habilita producción y **se entregan las direcciones de los servicios productivos**.
3. En producción:
   1. obtener un **nuevo Token**, distinto del de piloto y de duración variable;
   2. obtener el **CUIS**, una vez o cuando venza;
   3. obtener el **CUFD a diario**;
   4. **sincronizar catálogos a diario** (actividades, sectores, productos, fecha y hora, documento sector);
   5. **homologar productos**;
   6. definir **puntos de venta** si hace falta.

### 10.3 Casos de prueba oficiales: CUIS y CUFD
Fuentes: `adjuntos/CasosDePruebaCUIS.xlsx` y `adjuntos/CasosDePruebaCUFD.xlsx`. Contexto en `autorizacion-de-sistemas__…__fase-i-pruebas.md` [fuera de bloque]. La segunda hoja, "X", está vacía en ambos archivos.

**Etapa I, CUIS** (se ejecutan **2 pruebas por caso**):

| Nro | Ambiente | Código sistema | NIT | Modalidad | Sucursal | Punto de venta | Transacción | Resultado esperado |
|---|---|---|---|---|---|---|---|---|
| 1 | 2 | su código de sistema | su NIT | su modalidad (2) | 0 | **1** | TRUE | Código Único de Inicio de Sistemas |
| 2 | 2 | su código de sistema | su NIT | su modalidad (2) | 0 | **0** | TRUE | Código Único de Inicio de Sistemas |

**Etapa III, CUFD** (se ejecutan **100 pruebas por caso**):

| Nro | Ambiente | Código sistema | NIT | Modalidad | CUIS | Sucursal | Punto de venta | Transacción | Resultado esperado |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 2 | su código | su NIT | su modalidad | su CUIS | 0 | **1** | TRUE | Código Único de Facturación Diaria |
| 2 | 2 | su código | su NIT | su modalidad | su CUIS | 0 | **0** | TRUE | Código Único de Facturación Diaria |

La etapa II es la sincronización de catálogos, con 50 pruebas por caso; se trata en otra especificación.

**Consecuencias:**
- Para el caso 1 hace falta un **punto de venta 1 registrado** en la Casa Matriz. El orden es:
  1. `cuis` con (sucursal 0, punto de venta 0);
  2. `registroPuntoVenta` con ese CUIS;
  3. `cuis` con (sucursal 0, punto de venta 1);
  4. `cufd` para (0,0) y para (0,1), cada uno con **su propio CUIS**.
- M-INV necesita una pantalla o herramienta de **"Pruebas SIAT"** que repita N veces una solicitud (2 para CUIS, 100 para CUFD) y registre cada resultado.
- Qué devuelve el SIN al pedir el CUIS varias veces seguidas, con un CUIS ya vigente (error 980/970, o el mismo CUIS con `transaccion=true`), es **NO DOCUMENTADO**. Sin embargo, el caso espera `TRUE`.

---

## 11. Qué debe implementar M-INV
Requisitos derivados de este bloque. **R** = requisito funcional, **V** = validación, **D** = dato a persistir, **P** = pantalla.

### Configuración y conexión
1. **D/P: configuración SIAT por empresa.** Campos:
   - NIT emisor (numérico);
   - razón social;
   - `codigoSistema` (alfanumérico);
   - `codigoAmbiente` (1 o 2; por defecto 2);
   - `codigoModalidad` (fijo en **2**);
   - URLs de los WSDL de Códigos, Operaciones, Sincronización y Facturación Computarizada, **una por ambiente y editables**;
   - **token delegado cifrado** (DPAPI o cifrado en la base de datos, nunca en texto plano);
   - fechas "Desde" y "Hasta" del token.
2. **R: cabecera HTTP `apikey: TokenApi <token>`** en todas las llamadas SOAP, centralizada en un inspector o handler. **V:** no llamar si el token está vacío o vencido.
3. **R: alerta de token por vencer**, N días antes de la fecha "Hasta". Mostrar en la interfaz el procedimiento manual del portal (§2.1 y §2.2); no existe un servicio de renovación.
4. **R: `verificarComunicacion`.** Botón "Probar conexión" en la configuración, y verificación antes de cada emisión. Éxito = `926`. Tras 2 o 3 fallos (Time Out, -1, HTTP 500), el sistema pasa a **modo fuera de línea**.
5. **R: servicio central de facturación** (esquema cliente-servidor, §6.3): un único componente mantiene los CUIS y CUFD y habla con el SIN; los clientes WPF lo consumen. Evita que cada caja pida sus propios códigos sin control.
6. **D: bitácora de llamadas SIAT**: operación, ambiente, sucursal, punto de venta, fecha y hora, solicitud y respuesta (sin el token), `transaccion`, lista de códigos y mensajes, duración.
7. **D: catálogo local de códigos de mensaje** (tabla de la §7, o sincronizado desde "Códigos de Mensajes Servicios"), para mostrar descripciones legibles.

### Sucursales y puntos de venta
8. **D/P: mapear cada sucursal de M-INV** (CM, EA, SC) a su `codigoSucursal` del **Padrón** (0 = Casa Matriz, 1..n). **V:** entero ≥ 0, único por empresa. El número lo introduce el usuario según el Padrón; el sistema no lo inventa.
9. **D: `direccion` por sucursal**, tomada de la respuesta del CUFD. Se usa en el XML. **V:** avisar si cambia respecto de la guardada.
10. **R/P: registrar puntos de venta** con `registroPuntoVenta`:
    - selector de `codigoTipoPuntoVenta` con los tipos 1 a 6, **5 = Cajeros** recomendado para las cajas de M-INV;
    - nombre y descripción, ambos **obligatorios y no vacíos** (errores 948/949);
    - guardar el `codigoPuntoVenta` que devuelve el SIN.
    - **V:** exige el CUIS vigente de la sucursal, con punto de venta 0.
11. **R/P: consultar puntos de venta** (`ConsultaPuntoVenta`) para conciliar la lista local con la del SIN.
12. **R/P: cerrar punto de venta** (`CierrePuntoVenta`) con **doble confirmación** y la advertencia "definitivo; el correlativo no se puede reutilizar". **V:** bloquear si el punto de venta tiene CUIS o CUFD activo. Al cerrar, marcarlo *cerrado* y no borrarlo.
13. **D: vincular cada caja o usuario cajero de M-INV** a un par (sucursal, punto de venta) SIAT.
14. (Opcional) **R: `registroPuntoVentaComisionista`** con NIT del comitente, NIT del comisionista, número de contrato, fecha de inicio y fecha de fin.

### CUIS
15. **R: solicitar el CUIS** (`cuis`) por cada par (sucursal, punto de venta). **D:** código, `fechaVigencia`, fecha de obtención, sucursal, punto de venta, ambiente, estado (vigente, vencido o cerrado).
16. **V: vigencia del CUIS de 365 días.** Tarea diaria que alerta cuando faltan **≤ 5 días**; desde ese momento se permite renovar. Si llega el aviso **3008** al pedir el CUFD, mostrar una alerta destacada.
17. **R: después de renovar el CUIS, pedir inmediatamente un CUFD nuevo** para ese par.
18. **R: manejo de 980/970** (ya existe un CUIS vigente): no tratarlo como error fatal. Si la respuesta trae código y vigencia, guardarlos; si no, mantener el CUIS local.
19. (Opcional) **R: CUIS masivo** para dar de alta muchos puntos de venta a la vez.

### CUFD
20. **R: solicitar el CUFD a diario** por cada par (sucursal, punto de venta), con el CUIS propio de ese par. **D:**
    - `codigoCUFD`;
    - **`codigoControl`** (necesario para el CUF);
    - **`direccion`**;
    - `fechaVigencia`;
    - fecha de obtención;
    - CUIS usado;
    - sucursal y punto de venta.
21. **R: orden diario automático:**
    1. sincronización de **fecha y hora** (obligatoria a diario);
    2. sincronización de catálogos;
    3. solicitud de CUFD.

    Se ejecuta al iniciar la jornada, al abrir la caja o cuando `fechaVigencia` esté por vencer.
22. **V: no emitir en línea sin un CUFD vigente.** Cada factura usa el CUFD vigente de **su** sucursal y punto de venta.
23. **R: contingencia del servicio de CUFD.** Si falla, entrar en fuera de línea y seguir con el **último CUFD válido (hasta 72 h)**, reintentando cada ≤ 2 h.
24. **R: el CUFD masivo** solo si se verifica que devuelve `codigoControl` y `direccion` por elemento. **V:** máximo **1000** elementos por llamada; partir la solicitud si hay más.
25. **D: histórico de CUFD.** No sobrescribir, porque las facturas fuera de línea y los eventos referencian el CUFD usado (`cufdEvento`).

### NIT de clientes
26. **R/P: botón "Verificar NIT"** en la ficha del cliente, y verificación automática al facturar a un NIT no verificado. **D:** estado (ACTIVO 986, INACTIVO 987, INEXISTENTE 994), fecha de verificación, mensaje.
27. **V:** si el NIT es inválido o no se pudo verificar (sin conexión), la venta pide confirmación para emitir con **código de excepción = 1**. La regla detallada está en la especificación de emisión.

### Eventos significativos y cierre
28. **R/P: registrar un evento significativo** con:
    - selector de `codigoEvento` (catálogo sincronizado; referencia 1 a 7);
    - descripción obligatoria;
    - fecha de inicio y de fin en formato `yyyy-MM-dd'T'HH:mm:ss.SSS`;
    - `cufdEvento` = CUFD con el que se emitió fuera de línea;
    - `cufd` = CUFD **nuevo**, obtenido antes del registro.

    **D:** `codigoRecepcion` devuelto, vinculado al paquete de facturas.
29. **V: eventos:**
    - fin > inicio;
    - registro **≤ 48 h** después del fin;
    - pedir un CUFD nuevo antes de registrar;
    - no enviar paquetes sin un `codigoRecepcion` de evento.
30. **R: detección automática de contingencia.** Con `verificarComunicacion` fallido, el sistema abre un evento en curso con fecha de inicio. Al recuperar la conexión: cierra el evento con fecha de fin, pide un CUFD nuevo, registra el evento y encola los paquetes (≤ 500 facturas por paquete).
31. **R/P: consultar eventos** (`consultaEventoSignificativo`) por fecha, sucursal y punto de venta.
32. **R/P: cierre de operaciones** (`cierreOperacionesSistema`), solo para el administrador y con doble confirmación. Tras el cierre, marcar inactivos el CUIS y el CUFD de ese par e impedir la emisión.

### Homologación y otros
33. **D/P: homologación de productos.** Cada producto de M-INV tiene:
    - `codigoProductoSin` (código SIN del catálogo sincronizado);
    - `actividadEconomica` (CAEB);
    - indicador de **origen nacional o importado**.

    Pantalla con búsqueda sobre el catálogo SIN y asignación en lote. **V:** no facturar productos sin homologar. El XML usa el código SIN; la representación gráfica usa el código interno.
34. **R: tamaño de los paquetes** (se aplica en la especificación de emisión): contingencia ≤ 500 facturas; masivo ≤ 1000.
35. **R: flujo de emisión individual** (se aplica en la especificación de emisión): XML → validación con XSD → Gzip → SHA256 (`hashArchivo` y huella). **Sin firma**, por ser computarizada.
36. **R: representación gráfica y XML.** Imprimir, o enviar o poner a disposición (PDF y XML). Si no se pueden enviar, la impresión es obligatoria.
37. **P: tablero "Estado SIAT".** Por cada sucursal y punto de venta: CUIS (vigencia y días restantes), CUFD (vigencia), última sincronización de fecha y hora y de catálogos, modo en línea o fuera de línea, eventos abiertos, paquetes pendientes, vencimiento del token.
38. **P/R: herramienta "Pruebas de autorización"** para el ambiente 2. Ejecuta los casos oficiales: CUIS, 2 veces por cada caso (PV 1 y PV 0); CUFD, 100 veces por cada caso. Registra los resultados y los exporta.
39. **R: separación de ambientes.** Los códigos (CUIS, CUFD, puntos de venta, eventos) se guardan **por ambiente**; al pasar a producción no se reutiliza nada de piloto, ni siquiera el token.
40. **Documentación de usuario:** guía de obtención del token (§2.1), asociación e inicio de operaciones (§10.1 y §10.2), requisitos de Internet (§6.4).

---

## 12. Dudas y huecos de la documentación
1. **URLs y WSDL** de los servicios de Códigos y Operaciones, en piloto y en producción: **NO DOCUMENTADOS**. Solo se sabe que las de producción se entregan en el portal al hacer Inicio de Operaciones. Ver la referencia externa no verificada en la §9.
2. **Namespace SOAP, versión de SOAP y nombres de los *wrappers*:** no documentados. La documentación da "nombre de método" y "objeto", y a veces no coinciden: `SolicitudCuisMasivo` aparece como nombre de método y `solicitudNotificaRevocado` frente a `notificaCertificadoRevocado`.
3. **Nombres de los campos de salida:** la documentación dice `codigoCUIS` y `codigoCUFD`. Es posible que el WSDL use `codigo`; hay que verificarlo.
4. **Estructura de `codigosRespuesta` y `mensajes`:** no documentada. Tampoco se sabe si distinguen advertencias de errores; por ejemplo, 3008 es una "Advertencia" y quizá llega con `transaccion=true`.
5. **`verificarNit` no lista `cuis`** entre las entradas, cuando todos los demás servicios lo piden.
6. **CUFD masivo:** la salida documentada no incluye `codigoControl` ni `direccion` por elemento, ni dice cómo emparejar `ListaCodigoCufd` con cada solicitud. Lo mismo ocurre con `listaCodigosCuis` en el CUIS masivo. Tampoco se documenta el límite del CUIS masivo.
7. **Formato literal de "Fecha UTC extendida"** (`fechaVigencia`) y de los campos "Date" (`fechaEvento`, `fechaRevocacion`, `fechaInicio` y `fechaFin` del comisionista): no documentado. Tampoco la zona horaria. Solo el evento significativo tiene formato explícito: `yyyy-MM-dd'T'HH:mm:ss.SSS`.
8. **Longitudes máximas** de `nombrePuntoVenta`, `descripcion` y demás: no documentadas, aunque existen los errores 997/998.
9. **Pedir otro CUFD con uno vigente:** no se documenta si se permite ni si el anterior sigue siendo válido. Tampoco el significado exacto de "CUFD fuera de tolerancia" (123) ni de "CUIS fuera de tolerancia" (988).
10. **Pedir el CUIS con uno vigente:** los casos de prueba esperan `TRUE`, pero los errores 980/970 indican rechazo. No se documenta si devuelve el CUIS existente.
11. **Cierre de punto de venta:** exige que no haya CUIS ni CUFD activo, pero no se documenta cómo se inactivan antes del cierre (¿`cierreOperacionesSistema` con ese punto de venta?). Además, su `codigoPuntoVenta` figura como obligatorio con la nota "caso contrario enviar 0".
12. **Obligatoriedad de `cufd`** en `registroEventoSignificativo` y `consultaEventoSignificativo`: la celda está en blanco. En el registro de evento, `codigoSucursal` figura como "No" obligatorio, lo que es incoherente con el resto.
13. **Estructura de `listaPuntosVentas`** (salida de `ConsultaPuntoVenta`): no documentada. `listaEventos` solo indica `[codigoEvento, descripción, fecha]`.
14. **`verificarComunicacion` en los servicios de Códigos y Operaciones:** no documentado. Solo aparece en los servicios de facturación y de notas.
15. **Captura de SoapUI del token** (`/images/2021/token-sopaui.png`): no se descargó. El texto Java basta para la cabecera.
16. **Error textual:** la descripción de `codigoPuntoVenta` en el CUFD masivo dice "sincronización de fecha y hora"; parece copiada de otra página.
17. **Tiempo de vida del token:** lo elige el usuario en el campo "Hasta", sin máximo documentado. No hay un servicio para renovarlo ni para consultar su vigencia.
18. **Catálogo de eventos significativos:** esta página solo dice "paramétrica". La numeración 1 a 7 sale de `contingencia-y-eventos-significativos.md` y debe confirmarse con la sincronización.
