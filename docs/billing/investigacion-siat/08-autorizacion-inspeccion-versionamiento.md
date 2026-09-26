# 08 — Autorización del sistema, pruebas (Fases I, II y III), asociación, inicio de operaciones y versionamiento

Especificación de implementación para el módulo de facturación de M-INV (modalidad **Facturación Computarizada en Línea**, código de modalidad 2, sin firma digital).

**Convenciones**

- `⚠ ELECTRÓNICA`: aplica solo a la modalidad Electrónica en Línea. No hay que implementarlo ahora.
- `NO DOCUMENTADO`: el dato no aparece en las fuentes leídas. No se inventó.
- `[x-ref]`: dato tomado de una página que no está en este bloque. Se leyó solo para comprobar lo que dice el bloque; el detalle completo está en la especificación que cubre esa página.
- Todas las páginas están en `scratchpad/siat/txt/`. Los nombres de archivo se citan sin la ruta.

## 0. Fuentes leídas

| Alias | Archivo | Estado |
|---|---|---|
| PA | `facturacion-en-linea__autorizacion-de-sistemas__proceso-de-autorizacion.md` | Completo |
| GS | `facturacion-en-linea__autorizacion-de-sistemas__gestion-de-solicitudes-de-autorizacion.md` | Completo (trata la inhabilitación) |
| RC | `facturacion-en-linea__autorizacion-de-sistemas__registro-caracteristicas.md` | Completo, pero el contenido real son capturas que no se descargaron |
| RN | `facturacion-en-linea__autorizacion-de-sistemas__renovacion-autorizacion.md` | Completo |
| PR | `facturacion-en-linea__autorizacion-de-sistemas__guia-de-usuario-prorroga.md` | Completo |
| F1 | `...__pruebas-para-la-autorizacion-del-sistema-de-facturacion__fase-i-pruebas.md` | Completo (Etapas I a XI) |
| F2 | `...__fase-ii-inspeccion.md` | Completo. Tiene unos 1,9 KB de texto; el resto del archivo es una imagen decorativa en base64 (se comprobó contra la página en vivo) |
| F3 | `...__fase-iii-pruebas-piloto.md` | Completo. Tiene unos 1,8 KB de texto; el resto es una imagen decorativa |
| AS | `facturacion-en-linea__caracteristicas-sfvl__asociacion-de-sistemas.md` | Completo |
| IO | `facturacion-en-linea__caracteristicas-sfvl__inicio-operaciones.md` | Completo |
| SF | `sistema-facturacion.md` | Completo |
| V21…V24 | `versionamiento__versionamiento-2021.md` … `-2024.md` | Completos (versiones 1.0.0 a 1.0.49) |
| V25, V26 | `versionamiento__versionamiento-2025.md`, `-2026.md` | **Truncados** en el scratchpad: solo tenían la primera versión de cada año. Se completaron con `curl` a la URL oficial; las versiones 1.0.50 a 1.0.58 salen de esa descarga (`specs/_work08/v2025.html`, `v2026.html`). |
| XLS | `adjuntos/CasosDePrueba*.xlsx` | Leídos con `specs/_tools/xlsx_dump.py`; los volcados están en `specs/_work08/*.txt` |
| XLS-R | `CasosDePruebaReversionAnulacion.xls` | **No estaba descargado**. Se bajó de la URL oficial y se leyó con un lector BIFF propio: `specs/_work08/CasosDePruebaReversionAnulacion.txt` |
| RND | `adjuntos/rnd_356aea02e.pdf` | **NO ES UN PDF.** Es el HTML de la portada de impuestos.gob.bo (147 724 bytes, idéntico en tamaño a los otros 6 `rnd_*.pdf`). La URL de origen (`.../uploads/356aea02e.pdf`, enlazada desde `facturacion-manual__algoritmos__codigo-de-control.md`) corresponde a la *"Especificación Técnica para la generación del Código de Control"*, que es de la facturación **manual/SFV antigua** y no del SIAT en línea. Al reintentar con curl, esa URL y la de la RND 102100000011 (`491335f008.pdf`) redirigen a la portada. **Contenido de la RND: NO DISPONIBLE.** Solo se usan los artículos que citan otras páginas (sección 9). |

---

## 1. Requisitos y solicitud de autorización (PA, V24 1.0.46/1.0.47)

### 1.1 Requisitos del solicitante (PA)

1. El NIT del solicitante debe estar **activo**.
2. Debe tener **obligación tributaria IVA**.
3. No debe tener **marcas de control ni contravenciones tributarias**.

### 1.2 Dónde se solicita (PA, V24 1.0.46)

- Desde la versión **1.0.46 (25/09/2024)** el inicio de sesión de PILOTO y PRODUCCIÓN es el mismo. Siempre se entra por PRODUCCIÓN, en `https://siat.impuestos.gob.bo/launcher/`, con las credenciales de *SIAT en Línea*:
  - Con autenticación **v1**, se busca la opción "Gestión de Autorización de Sistemas (PILOTO)".
  - Con autenticación **v2**, se entra a "Sistemas de Facturación" y luego a "Gestión de Autorización de Sistemas (PILOTO)".
  - **Nota (V24 1.0.46):** las credenciales que se usaban en PILOTO **dejan de surtir efecto**. Solo valen las de PRODUCCIÓN.
- Ruta dentro del portal: "Autorización de Sistemas Informáticos de Facturación", luego "Seguimiento de Sistemas" y luego **"Nuevo Sistema"**.

### 1.3 Datos del sistema que se registran (PA)

| Campo | Descripción según la documentación | Valor sugerido para M-INV |
|---|---|---|
| Nombre Comercial | Nombre con el que se conocerá el sistema | "M-INV" (debe coincidir con lo que muestra la aplicación) |
| Tipo | Uso **propio** o **proveedor** (lo usan otros contribuyentes) | Ver la duda D1: M-INV es multiempresa |
| Versión | Versión del sistema | La versión que muestra la aplicación, por ejemplo 4.1 (ver 4.4.12) |
| Marca de Proceso Masivo | Obliga al propietario o proveedor a hacer **pruebas adicionales** de envío de paquetes por emisión masiva | **No marcar** si M-INV no hará emisión masiva: evita la Etapa IX |
| Modalidad de Facturación | Modalidad con la que operará | Computarizada en Línea |
| Tipo Documento Sector | Documentos que manejará, según la actividad (propio) o la oferta (proveedor) | 1 = Compra-Venta y 24 = Nota Crédito-Débito. Opcionalmente 47 = Nota Crédito-Débito Descuento (códigos confirmados en los XML oficiales `adjuntos/xml/CompraVentaXML`, `CreditoDebitoXML` y `CreditoDebitoDescuentoXML`) |

**Datos de las personas de contacto (PA):** Nombre completo, Tipo de documento, Número de documento, Complemento (si tiene), Correo electrónico válido y Celular válido.

### 1.4 Qué devuelve el registro (PA)

Al terminar se genera un **reporte** que incluye:
- el **código de sistema** asignado (se envía como parámetro `codigoSistema` en todos los servicios);
- los **parámetros constantes** para consumir los servicios;
- las **direcciones (URL)** que se usan en las pruebas de las Fases.

> Las URL/WSDL, los namespaces y el valor exacto de los "parámetros constantes" **NO están documentados en este bloque**: los entrega ese reporte. M-INV debe permitir configurarlos sin recompilar (ver M-INV-1).

### 1.5 Notas obligatorias (PA; agregadas en V24 1.0.47, 07/11/2024)

- **Los sistemas WEB y las API se autorizan por separado**: por un lado el sistema web y por otro la API.
- **Los sistemas de tipo proveedor deben implementar todas las funcionalidades mínimas.**

### 1.6 ⚠ ELECTRÓNICA: firma digital de prueba (PA)

Para la modalidad electrónica: se genera un CSR con los campos de AGETIC, se envía al SIN por correo y el SIN devuelve el certificado firmado por el SIN y AGETIC. **No aplica a computarizada.**

### 1.7 Plazos del proceso (PR)

| Plazo | Valor |
|---|---|
| Plazo para autorizar un sistema (propio o proveedor) | **90 días** |
| Prórroga si el desarrollo no terminó o no se superaron las pruebas | **60 días más**, una sola prórroga según el texto |
| Si al día 90 + 60 no se completaron las pruebas | La solicitud se **cancela automáticamente** y hay que empezar una nueva |

Cómo se pide la prórroga: portal, luego "Autorización de Sistemas", luego **"Solicitud de Prórroga"**. Se elige el sistema, se escribe el **motivo** y se **confirma**.

### 1.8 Etapas (PA)

| Fase | Contenido | Quién la controla |
|---|---|---|
| **Fase 1: Pruebas** | Pruebas mínimas de emisión y envío al SIN (sección 2) | El SIN las contabiliza (porcentaje por caso) |
| **Fase 2: Funcionalidad e Inspección** | El SIN verifica las funcionalidades mínimas. Se coordina entre el SIN y el contribuyente y puede ser **física o virtual** (sección 3) | El SIN |
| **Fase 3: Pruebas Piloto** | Pruebas funcionales y **de carga** de la implementación ya autorizada (sección 4) | **El SIN no las contabiliza.** Son responsabilidad del contribuyente, con sistema propio o proveedor |

> Nota histórica (V21 1.0.2): antes la "Fase 2" eran las Pruebas Piloto y la "Fase 3" la Inspección. El orden vigente es el de la tabla.

---

## 2. FASE I: Pruebas en ambiente piloto (F1 y los Excel de casos)

### 2.1 Reglas comunes

- Cada caso de prueba del Excel tiene estas columnas: Nro, Código Ambiente, Código Sistema, NIT, Modalidad, CUIS, CUFD, Sucursal, Código Punto Venta, **Transacción** (si se procesó bien, TRUE/FALSE) y **Resultado esperado** (lista de mensajes).
- **En todos los casos** de todos los Excel: `codigoAmbiente = 2` (ambiente de pruebas), `sucursal = 0`, y cada caso se repite para **`codigoPuntoVenta = 1` y `codigoPuntoVenta = 0`**. **Consecuencia:** en PILOTO M-INV tiene que haber registrado al menos un punto de venta (código 1), además del PV 0.
- **Fecha Envío, Fecha Inicio Evento y Fecha Fin Evento** van "en formato UTC extendido sin zona horaria" (F1). El patrón exacto de fecha **NO está en este bloque**; ver la especificación de servicios.
- **Hash Archivo** es el SHA-256 de la cadena `archivo` (F1). [x-ref] `requerimientos__sistema-informatico.md`: es el SHA-256 del XML **ya comprimido en Gzip**, y en computarizada ese valor es también la *Huella Digital*.
- Cada caso da un **porcentaje** que permite cumplir el mínimo de la etapa. Las cantidades se cuentan "en el apartado de **seguimiento**" del portal (F1). Cómo se reparten por caso o por sector: **NO DOCUMENTADO** (solo se ve en el portal).

### 2.2 Etapas, cantidades y si aplican a M-INV

| Etapa | Qué se prueba | Excel | Cantidad exigida (F1) | Resultado esperado (Excel) | ¿Aplica a M-INV computarizada? |
|---|---|---|---|---|---|
| **I. Obtención de CUIS** | Servicio de CUIS | `CasosDePruebaCUIS.xlsx` (2 casos: PV 1 y PV 0) | **2 pruebas por caso** | "CÓDIGO ÚNICO DE INICIO DE SISTEMAS", Transacción TRUE | **Sí** |
| **II. Sincronización de catálogos** | Servicios de sincronización | `CasosDePruebaSincronizaciónCatálogos.xlsx` (18 hojas × 2 casos = 36 casos) | **50 pruebas por caso** | "LISTADO TOTAL DE …", Transacción TRUE | **Sí** |
| **III. Obtención de CUFD** | Servicio de CUFD | `CasosDePruebaCUFD.xlsx` (2 casos) | **100 pruebas por caso** | "Código Único de Facturación Diaria", TRUE | **Sí** |
| **IV. Emisión individual** | Facturas y notas C/D individuales, por cada documento sector de la actividad | `CasosDePruebaEmisionIndividual1.xlsx` (64 casos: sectores 1–24 y 28–35, × PV 0/1) | **500 emisiones individuales** en total | **908 = RECEPCION VALIDADA** | **Sí** |
| **V. Eventos significativos** | Registro de inicio y fin de evento | `CasosDePruebaEventosSignificativos.xlsx` (14 casos: 7 eventos × PV 0/1) | **5 pruebas por caso** | "Código de recepción del evento", TRUE | **Sí** |
| **VI. Emisión de paquetes (contingencia)** | Paquetes de **hasta 500** facturas generados por un evento | `CasosDePruebaEmisionPorPaquetes.xlsx` (432 casos, 16 por sector; **no incluye el sector 24**) | **10 pruebas por caso** | Envío: **901 = PENDIENTE**. Validación: **908 = RECEPCION VALIDADA** | **Sí** (sector 1) |
| **VII. Anulación** | Anulación de facturas y notas C/D, por cada sector | `CasosDePruebaAnulacionReversion.xlsx` (56 casos) | **250 anulaciones** | **905 = ANULACION CONFIRMADA** | **Sí** |
| VIII. Firma digital ⚠ ELECTRÓNICA | Facturas y notas firmadas | `CasosDePruebaFirmaDigital.xlsx` (no descargado; no aplica) | 250 emisiones firmadas | — | **No** |
| IX. Emisión masiva | Paquetes masivos (F1 dice "hasta 2000"; el Excel dice "igual a 1000" o "menor a 1000") | `CasosDePruebaEmisionMasiva.xlsx` (216 casos, 8 por sector) | **10 pruebas por caso** | 901, luego 908 | Solo si se marca "Proceso Masivo". **Recomendado: no** |
| X. Emisión masiva IEHD | Hidrocarburos, bloques de hasta 1000, métodos `recepcionMasivaContratosYPFB` y `validacionRecepcionMasivaFacturaYPFB` | `CasosDePruebaEmisionMasivaIEHD.xls` | — | — | **No** (sector hidrocarburos) |
| **XI. Reversión de la anulación** | Revertir la anulación de documentos fiscales | `CasosDePruebaReversionAnulacion.xls` (98 casos: 49 sectores × PV 0/1) | **98 reversiones** | **907 = REVERSIÓN CONFIRMADA** | **Sí** |

**Catálogos que prueba la Etapa II.** Son las hojas del Excel, cada una con 2 casos (PV 1 y PV 0) y el resultado "LISTADO TOTAL DE …":
Actividades · Fecha y Hora ("FECHA Y HORA ACTUAL") · Actividades Documento Sector · Leyendas Factura · Mensajes Servicios · Productos Servicios · Eventos Significativos · Motivo Anulación · País Origen · Tipo Documento Identidad · Tipo Documento Sector · Tipo Emisión · Tipo Habitación · Tipo Método Pago · Tipo Moneda · Tipo Punto Venta · Tipo Factura · Unidad Medida.
El texto de F1 también menciona "**modalidad**" como catálogo, pero no hay una hoja para él (ver la duda D6).

### 2.3 Casos concretos que M-INV debe cubrir (sectores 1, 24 y, si se usa, 47)

Nro = número de caso en el Excel. Todos con `sucursal = 0`.

| Etapa | Casos | Datos del caso |
|---|---|---|
| I CUIS | 1, 2 | PV 1 y PV 0 |
| II Sincronización | 1, 2 de cada una de las 18 hojas | PV 1 y PV 0 |
| III CUFD | 1, 2 | PV 1 y PV 0 |
| IV Individual | **1, 2** | Sector 1, tipoFactura 1, `codigoEmision = 1`, PV 1 y PV 0 |
| IV Individual | **47, 48** | Sector 24, tipoFactura 3, `codigoEmision = 1`, PV 1 y PV 0 |
| IV Individual | — | El sector 47 **no aparece** en el Excel de emisión individual (duda D7) |
| V Eventos | 1 a 14 | Eventos 1 a 7 × PV 1 y PV 0. Datos: CUIS, CUFD vigente, fecha de inicio y fin en UTC, **CUFD del evento** (el CUFD con que se generó el evento) |
| VI Paquetes | 1 a 14 | Sector 1, tipoFactura 1, **`codigoEmision = 2`**. Cada evento de 1 a 7 con PV 1 y **cantidad "igual a 500"**, y con PV 0 y **cantidad "menor a 500"**. Requiere el **código de recepción del evento**. Resultado 901 |
| VI Paquetes | 15, 16 | Validación del paquete con "su código de recepción". Resultado 908 |
| VII Anulación | 1, 2 | Sector 1, tipoFactura 1, motivo "FACTURA MAL EMITIDA". Resultado 905 |
| VII Anulación | 47, 48 | Sector 24, tipoFactura 3, motivo "NOTA DE CREDITO-DEBITO MAL EMITIDA". Resultado 905 |
| XI Reversión | 3 y 6 | Sector 1, tipoFactura 1. Resultado 907 |
| XI Reversión | 67 y 70 | Sector 24, tipoFactura 3 |
| XI Reversión | 66 y 69 | Sector 47, tipoFactura 3 |
| XI Reversión | todos | Motivo "FACTURA MAL EMITIDA" |
| IX Masiva (solo si se marca) | 1 a 8 | Sector 1, `codigoEmision = 3`, PV 0 y 1, "igual a 1000" y "menor a 1000". 901, luego 908 |

**Valores de catálogo que se deducen de los Excel** (no hay una tabla oficial de nombres en este bloque):

- `codigoEmision`: **1** en la emisión individual (en línea), **2** en los paquetes (fuera de línea) y **3** en la masiva.
- `codigoTipoFactura`: **1** en compra-venta (sector 1), **2** en varios sectores "sin crédito fiscal" (por ejemplo 3–10) y **3** en las notas (24, 29, 47).
- La descripción oficial de cada código se obtiene de la sincronización `Tipo Emisión` y `Tipo Factura`.

### 2.4 Estados que aparecen en este bloque

| Código | Texto (Excel o página) | Uso |
|---|---|---|
| 901 | PENDIENTE | Respuesta al enviar un paquete o una emisión masiva |
| 904 | observada | [x-ref] `emision-y-envio.md`: estado posible al validar un paquete |
| 905 | ANULACION CONFIRMADA | Anulación |
| 907 | REVERSIÓN CONFIRMADA (Excel) o "Reversión Anulada Conforme" ([x-ref] `reversion-anulacion-documentos-fiscales.md`) | Reversión |
| 908 | RECEPCION VALIDADA | Emisión individual o validación de paquete |
| 981, 924, 3011, 3012 | [x-ref] `reversion-anulacion-documentos-fiscales.md` | 981: factura no disponible para reversión. 924: factura no existe. 3011: el sistema no superó las pruebas de autorización para usar la reversión. 3012: reversión fuera de plazo |

### 2.5 Eventos significativos: el orden de los códigos NO es el mismo en todas las fuentes (crítico)

| Código | Excel de Eventos (Etapa V) | [x-ref] Página de Contingencia | Excel de Paquetes (Etapa VI) |
|---|---|---|---|
| 1 | Corte del servicio de Internet | Corte del servicio de Internet | **Corte de suministro de energía eléctrica** |
| 2 | Inaccesibilidad al servicio web de la AT | Inaccesibilidad al servicio web de la AT | **Corte del servicio de Internet** |
| 3 | Ingreso a zonas sin internet por despliegue de punto de venta en vehículos automotores | Ingreso a zonas sin Internet por despliegue de puntos de venta | **Virus informático o falla de software** |
| 4 | Venta en lugares sin internet | Venta en lugares sin internet | **Cambio de infraestructura o falla de hardware** |
| 5 | **Corte de suministro de energía eléctrica** | **Virus informático o falla de software** | Inaccesibilidad al servicio web |
| 6 | **Virus informático o falla de software** | **Cambio de infraestructura o falla de hardware** | Ingreso a zonas sin internet |
| 7 | **Cambio de infraestructura o falla de hardware** | **Corte de suministro de energía eléctrica** | Venta en lugares sin internet |

Además, V22 1.0.25 (26/09/2022) dice: *"Se reordenaron las causales 5, 6 y 7 de emisión de facturas de contingencia"*.

**Regla para M-INV:** nunca fijar códigos de evento en el código fuente. Hay que usar **siempre** el catálogo que devuelve la sincronización de Eventos Significativos y relacionar cada evento con su comportamiento (fuera de línea o CAFC) **por la descripción sincronizada** (ver M-INV-15).

---

## 3. FASE II: Inspección. Checklist de verificación (F2)

F2 dice textualmente que la inspección verifica que el sistema (propio o proveedor) *"cumpla la Normativa vigente y tenga implementada las funcionalidades mínimas establecidas en este Anexo"*, **"entre otras cosas"**. Es decir, la lista no es cerrada. La fase se hace bajo supervisión del SIN, **física o virtualmente** (PA).

### 3.1 Qué se verifica, textualmente (F2, numeración original)

| # | Qué verifica el SIN | Cómo debe cumplirlo M-INV |
|---|---|---|
| **II-1** | **Nominatividad**: toda transacción, sin importar el monto, lleva **número de documento** del comprador. Nominatividad es el número de documento, no el nombre o la razón social | Número de documento **obligatorio** en toda venta facturada, sin "venta sin nombre". Para los casos especiales, [x-ref] `emision-y-envio.md`: los códigos 99001 (consulados, embajadas), 99002 (Control Tributario) y 99003 (Ventas Menores del Día) se envían con tipo de documento **NIT** y **código de excepción = 1** |
| **II-2** | **No emitir facturas con monto 0**, salvo que el medio de pago sea **"Gift Card"** | Validación que bloquea la emisión: si el total a pagar es 0 y no hay un método de pago Gift Card, se rechaza. El código de Gift Card sale del catálogo de métodos de pago |
| **II-3** | Poder usar **varios medios de pago y combinarlos**, enviando los códigos definidos para eso | Pago mixto en la pantalla de venta, que se traduce al **código de combinación** del catálogo de métodos de pago. No se calcula localmente |
| **II-4** | La **segunda leyenda** del pie de la representación gráfica **cambia aleatoriamente en cada emisión** (Ley del consumidor N° 453) | Elegir al azar una leyenda del catálogo de Leyendas, filtrada por la actividad económica, **en cada factura**, y guardarla en la factura para poder reimprimirla igual |
| **II-5** | La **tercera leyenda**, la de la forma de operación, cambia de **"en línea" a "fuera de línea"** y viceversa según corresponda | La leyenda depende del tipo de emisión real de cada factura. El texto exacto **NO está en este bloque** (ver la especificación de representación gráfica) |
| **II-6** | Con tarjeta de crédito o débito, el número se **enmascara con ceros en las posiciones centrales**. Ejemplo: `9999000000009999`, donde los 9 son los dígitos reales de la tarjeta | Se guardan y envían los **4 primeros y los 4 últimos dígitos** y **ceros en el medio**. Nunca se guarda el número completo |
| II-7 | En la "Factura de Compra y Venta de Moneda Extranjera", el **tipo de cambio de venta** va en el precio unitario del detalle | **No aplica** (M-INV no emite ese sector) |
| **II-8** | Los paquetes de facturas emitidas **fuera de línea** se envían **dentro de las 48 horas** posteriores a la recuperación de la contingencia | Cola de paquetes con plazo límite = fin del evento + 48 h, con alertas y **envío automático** (II-14) |
| **II-9** | Los paquetes de **facturas manuales de contingencia transcritas** se envían **dentro de las 72 horas** posteriores a la recuperación | Plazo límite = fin del evento + 72 h para los paquetes con CAFC |
| **II-10** | El **plazo de anulación** de facturas cumple la normativa | [x-ref] `anulacion-de-documentos-fiscales.md`: **hasta el día 9 del mes siguiente** a la emisión, de forma individual. El sistema debe **bloquear** la anulación fuera de ese plazo |
| II-11 | El plazo de anulación de los sectores de **exportación** es de **180 días** | **No aplica** a los sectores 1 y 24, pero la regla debe poder configurarse por sector |
| **II-12** | Control o gestión de **CAFC** para los eventos "**corte de suministro de energía eléctrica**", "**cambio de infraestructura o falla de hardware**" y "**virus informático o falla de software**", y su **transcripción** en el sistema cuando corresponda | Módulo CAFC: registro de los códigos o rangos autorizados, la pantalla de transcripción de facturas manuales y el envío en paquete con el CAFC (M-INV-18) |
| **II-13** | La **representación gráfica y el XML** llegan al **correo del comprador** o quedan disponibles por un medio electrónico que garantice la privacidad | Envío por correo del PDF y el XML, o portal o enlace. [x-ref] `comprobante-de-transaccion.md` (V25 1.0.53) |
| **II-14** | La **entrada y salida del modo fuera de línea** y el **envío de paquetes** son **automáticos** | Máquina de estados de conectividad automática, sin depender del cajero (M-INV-14) |
| II-15 | "Otros aplicables al sector o giro de negocio" | Abierto |

### 3.2 Funcionalidades mínimas del Anexo que la inspección da por implementadas

F2 remite a *"funcionalidades mínimas establecidas en este Anexo"*. Las enumera [x-ref] `requerimientos__sistema-informatico.md`:

| Id | Componente mínimo | Detalle |
|---|---|---|
| FM-a | **Emisor de facturas digitales** | Emisión **individual** y **por contingencia** obligatorias; **masiva** según el giro. Pasos de la emisión individual: 1) generar el XML; 2) ⚠ firmar con XMLDSig, solo en electrónica; 3) **validar contra el XSD**; 4) **comprimir en Gzip**, que va en la etiqueta `archivo`; 5) **SHA-256** del comprimido, que va en `hashArchivo` y en computarizada es la *Huella Digital*. Contingencia: paquetes de **máximo 500** facturas; al recuperarse se registra el evento y se envían los paquetes. Masiva: paquetes de **hasta 1000** |
| FM-b | **Gestor de facturas digitales** | Envía y valida transacciones de registro, **como la anulación** |
| FM-c | **Sincronización de catálogos** | **Diaria** |
| FM-d | **Sincronización de fecha y hora** | "**Debe obligatoriamente efectuarse a diario**". Se usa para controlar plazos. Se recomienda hacerla **antes de obtener el CUFD** |
| FM-e | **Registro de eventos significativos** | — |
| FM-f | **Gestor de envío de documentos digitales e impresión** | Imprime, envía o publica la representación gráfica y el XML. La impresión **no es obligatoria**, salvo que el sistema no pueda enviarlos por medios electrónicos. Puede haber un **portal** de consulta para el cliente |

### 3.3 Funciones que pidió el orquestador: qué dice la documentación

| Función | ¿Está documentada como requisito? | Fuente o comentario |
|---|---|---|
| Registro de clientes | **Implícito, no como pantalla obligatoria.** II-1 exige el número de documento en toda transacción | F2 #1 |
| Catálogos sincronizados | **Sí**: FM-c, Etapa II y sincronización diaria en IO | F1, IO, [x-ref] |
| Homologación de productos | **Sí**: paso del inicio de operaciones | IO |
| Reimpresión | **NO DOCUMENTADO** como verificación. FM-f cubre la impresión y el envío | Recomendado |
| Anulación | **Sí**: Etapa VII, II-10 y FM-b | F1, F2 |
| Reversión de anulación | **Sí**: Etapa XI | F1 |
| Consulta o verificación de estado | **Sí como buena práctica**: [x-ref] contingencia, *"mantener un registro de facturas sin código de respuesta"* y verificarlas con el servicio de verificación de estado | [x-ref] |
| Bitácora o auditoría | **NO DOCUMENTADO** como requisito de inspección. Se recomienda como evidencia para la inspección y para el registro de facturas sin respuesta | — |
| Gestión de eventos significativos | **Sí**: FM-e, Etapa V, II-12 y II-14 | F1, F2 |
| Envío de paquetes | **Sí**: Etapa VI, II-8, II-9 y II-14 | F1, F2 |
| Reportes | **NO DOCUMENTADO** en este bloque | — |
| Control de CUFD vencido | **Parcial.** IO: el CUFD se obtiene **a diario** "para poder emitir". [x-ref] contingencia: si falla el servicio de CUFD se emite fuera de línea con el último CUFD válido, que en ese caso **se amplía hasta 72 horas**. V22 1.0.19: pedir un **CUFD nuevo antes** de registrar el evento y enviar paquetes | IO, V22, [x-ref] |
| Bloqueo si no hay CUIS | **Implícito.** IO: el CUIS se obtiene al inicio **o cuando venció**. V23 1.0.30: al **renovar el CUIS** hay que **obtener un CUFD nuevo** para seguir operando | IO, V23 |

---

## 4. FASE III: Pruebas piloto (F3)

**Objetivo:** garantizar la integración del sistema del **proveedor** con el ecosistema de facturación de sus **clientes** (contribuyentes), con pruebas **funcionales y de carga** en PILOTO, antes de iniciar operaciones en la nueva modalidad.

**Pasos (F3, textuales):**

1. El proveedor **asocia** su sistema con el contribuyente (sección 5).
2. El contribuyente **confirma** la asociación.
3. El contribuyente **genera un token delegado** y se lo entrega al proveedor.
4. El proveedor y el contribuyente **configuran** el ecosistema de emisión: clientes, productos, sucursales, puntos de venta y otros.
5. El contribuyente hace pruebas de:
   - a) sincronización de catálogos;
   - b) emisión individual en línea;
   - c) anulación;
   - d) reversión de anulación;
   - e) gestión de eventos significativos;
   - f) emisión fuera de línea y **envío automático** de paquetes de contingencia;
   - g) transcripción y envío de **facturas manuales de contingencia**, cuando corresponda;
   - h) emisión **masiva** y envío de paquetes **de hasta 1000** facturas, cuando corresponda.

**Obligatoriedad:** la fase es **obligatoria**. *Iniciar operaciones* equivale a una **declaración jurada** de que el contribuyente y el proveedor hicieron todo lo anterior. Cantidades mínimas: **NO DOCUMENTADO**; el SIN no las contabiliza (PA).

---

## 5. Asociación de sistemas (AS) y registro de características (RC)

### 5.1 Asociación: la hace el proveedor, en PILOTO

Ruta: portal (sección 1.2), menú **"Asociación de Sistemas (RND-102100000011)"**, opción **"Asociación de Sistemas"**. Datos que se ingresan:

| Campo | Regla |
|---|---|
| NIT del contribuyente | Válido y **activo** |
| Login del usuario | Usuario con el que el contribuyente entra al sistema |
| Nombre del sistema | El que se asocia |
| Modalidad | Modalidad a la que se asocia |
| Tipo de servicio | Uno de los de la tabla siguiente |
| Sectores | **Solo** los sectores en los que el contribuyente está autorizado |
| Correo del contribuyente | Ahí le llega el mensaje para confirmar |

**Tipos de servicio (AS):**

| Tipo | Definición resumida |
|---|---|
| **Licencia** | Se cede el sistema mediante licencias (EULA, ALUF, CLUF, GNU, CDDL u otras), con un acuerdo respaldado por un documento electrónico o físico |
| **Alquiler** | Se cede el sistema **y el hardware**, de forma temporal |
| **Prestación de servicio** | El sistema del contribuyente envía solo los datos a un servicio web del proveedor (SOAP o REST), que genera y emite la factura. Hay que definir el tiempo del servicio. El contribuyente cede un **usuario de oficina virtual** y, ⚠ ELECTRÓNICA, su firma digital |
| **Facturación por terceros** | Solo el proveedor genera y emite; el contribuyente cede un **token delegado** (ejemplo: colegios) |
| Facturación conjunta | Sin descripción en la página: **NO DOCUMENTADO** |
| Comisionistas | Sin descripción en la página: **NO DOCUMENTADO** |

### 5.2 Confirmación: la hace el contribuyente (AS)

Ruta: portal, **"Sistema de Facturación Versión 2"**, **"Confirmación de la Asociación"**. Se muestran los sistemas que asociaron el NIT. Al principio solo está habilitada la opción **"Pruebas Piloto"**, que genera un documento con las especificaciones de las pruebas. Si el contribuyente queda conforme, **acepta** (con confirmación) y se emite la **autorización de asociación**. También puede **rechazar**.

### 5.3 Registro de características o funcionalidades (RC)

- **Sistema propio:** "Seguimiento de Sistemas Informáticos" y el botón para **registrar las características** del sistema.
- **Contribuyentes asociados:** "Confirmación de Asociación de Sistemas".
- La lista de características que pide el formulario está en capturas (`reno7.png`, `reno10.png`, `reno11.png`) que **no se descargaron**: **NO DOCUMENTADO**.
- Según RN, las emisiones con **características distintas a las elegidas "serán observadas"**. Hay que declarar exactamente lo que M-INV usará: sectores 1 y 24, en línea, fuera de línea, CAFC, y masiva sí o no.

---

## 6. Inicio de operaciones (IO)

**Ruta:** menú **"Inicio y Cierre de Operaciones"**, que muestra los sistemas asociados al NIT, y el botón **"Inicio de Operaciones"**.

**Formulario:**
- **fecha de inicio de operaciones**;
- **tipo de servicio de Internet**;
- **empresa proveedora de Internet**.

Al aceptar, el sistema queda **habilitado en producción** y se entregan las **URL de los servicios productivos**.

**Pasos en producción (IO), en orden:**

1. Obtener un **token nuevo en producción** en el Portal SIAT. Es **distinto del de PILOTO** y su duración es **variable**.
2. Obtener el **CUIS**, **una sola vez al inicio** o **cuando haya vencido**.
3. Obtener el **CUFD a diario** para poder emitir.
4. **Sincronizar catálogos a diario**: actividades, sectores, productos, fecha y hora, documento sector.
5. **Homologar** los productos propios con el catálogo del SIN.
6. Definir **puntos de venta** si hace falta.

---

## 7. Renovación, nueva autorización, prórroga e inhabilitación

### 7.1 Vigencia y nueva autorización (RN, V24 1.0.48/1.0.49)

- RND 102100000011, **Art. 20 inciso B** (citado en RN): la autorización vale **3 años desde su emisión**. **Antes** de que venza, el propietario o proveedor debe pedir una **nueva autorización** y pasar las pruebas **vigentes** en ese momento.
- Ruta: portal, "Sistema de Facturación" (solo v2), "Gestión de Autorización de Sistemas (PILOTO)", "Seguimiento de Sistemas Informáticos" y el botón para **solicitar la nueva autorización**.
- **Sistema propio:** se completa la información **especificando todas las funcionalidades** que usará y se pulsa **"Solicitar"**.
- En ambos casos se **reajustan las pruebas en PILOTO** y hay que **completarlas de nuevo**. Luego se pulsa **"Finalizar Pruebas"**, que permite **programar la fecha y hora de la inspección**.
- Si se supera la inspección, la autorización se extiende **3 años más**.
- Hay **3 oportunidades** para completar la inspección. Si no se supera, **el código de sistema no se podrá volver a usar**.
- **Solo** pueden renovar los sistemas **autorizados y en producción**.
- Si no se pidió la renovación y llegó la fecha de fin de la autorización, **no se puede volver a usar** el código de sistema.

### 7.2 Inhabilitación, o baja del sistema autorizado (GS)

**Condiciones:**
1. El sistema "Proveedor" **no está asociado** a otro contribuyente.
2. **No está habilitado** en ninguna sucursal ni punto de venta.
3. **No está observado** por la AT.

**Flujo:** el contribuyente la solicita, el SIN verifica los requisitos y el SIN confirma o rechaza.

---

## 8. Versionamiento del Anexo Técnico, de 2021 a 2026 (V21 a V26)

Todas las versiones de 2021-10 a 2024 repiten esta **nota general**:

> Cuando el SIN agrega elementos a un catálogo (métodos de pago, unidades de medida y otros), habría que ajustar los XSD para aceptar más valores. El SIN **no publicará** esos ajustes. **El contribuyente debe hacerlos directamente en su sistema.**

**Consecuencia para M-INV:** no dar por buenos los `enumeration` ni los `maxInclusive` publicados de los XSD para códigos de catálogo. Hay que validar contra el **catálogo sincronizado**.

### 8.1 2024 a 2026, en detalle, con su impacto en Computarizada, Compra-Venta (sector 1) y Notas C/D (24 y 47)

| Versión (fecha) | Cambio (textual o resumido) | Impacto en M-INV |
|---|---|---|
| **1.0.58** (10/06/2026) | En Servicios Básicos y Servicios Básicos Zona Franca, la leyenda "Tarifa Dignidad" pasa a ser "Descuento Patria" | Ninguno |
| **1.0.57** (13/05/2026) | El sector 55 se llama ahora "Factura de Comercialización de Combustible". El XML y el XSD **no cambian** | Ninguno |
| **1.0.56** (11/04/2026) | Sector 55: el importe válido para crédito fiscal se calcula al **100%** | Ninguno |
| 1.0.55 (31/12/2025) | Sector 55: crédito fiscal solo al 70% | Ninguno |
| 1.0.54 (18/12/2025) | Sector 55: cálculo según el DS 5503 | Ninguno |
| **1.0.53** (20/10/2025) | Se incorporan los **Requisitos Mínimos para la Impresión del Comprobante de Transacción** | **Sí.** Si M-INV entrega un ticket o comprobante en lugar del PDF o XML, debe tener un **enlace web o QR de al menos 3 × 3 cm** hacia el portal del sistema, donde el comprador consulte el XML y la representación gráfica (Art. 26 de la RND, [x-ref] `comprobante-de-transaccion.md`) |
| 1.0.52 (25/08/2025) | Sector 55: 70% | Ninguno |
| **1.0.51** (08/02/2025) | Se ajusta la **descripción de los métodos de pago** | **Sí.** Mostrar las descripciones que llegan de la sincronización; no fijarlas en el código |
| 1.0.50 (16/01/2025) | Nuevos sectores **54** (insumos para biodiésel) y **55** (combustible), y la **Facturación Tasa Cero IVA Ley 1613** | Solo si la empresa vende bienes de tasa cero. Por defecto, ninguno |
| **1.0.49** (06/12/2024; nota del 29/11/2024) | Se ajusta la redacción del procedimiento de **Nueva Autorización** | **Sí**: proceso de la sección 7.1 |
| **1.0.48** (20/11/2024) | Procedimiento de **reautorización**. Nota sobre la **emisión con tipo de documento CI y el uso del código de excepción** | **Sí.** [x-ref] `emision-y-envio.md`: con CI o NIT hay que validar que el valor sea **numérico**. `codigoExcepcion` es **0 por defecto** y **1 solo** si el tipo es NIT y se pide no validarlo. **Fuera de línea con NIT, siempre 1**. Los códigos especiales 99001, 99002 y 99003 van con NIT y excepción 1 |
| **1.0.47** (07/11/2024) | Los sistemas **Web y API** se autorizan por separado | **Sí** (duda D2) |
| **1.0.46** (25/09/2024) | **Login único**: a PILOTO se entra por PRODUCCIÓN y las credenciales de PILOTO **dejan de funcionar** | **Sí**: configuración de tokens y ambientes |
| 1.0.45 (13/08/2024) | Imágenes del login y nota sobre ajustes de XSD a cargo del contribuyente | **Sí** (ver la consecuencia de la nota general) |
| **1.0.44** (12/06/2024) | Nota: incluir el **número de Guía de Tránsito en la descripción** de la factura, y **nota aclaratoria sobre notas de Crédito y Débito** | **Sí** para las notas C/D. [x-ref] `casos-especiales__notas-de-credito-debito.md`, Art. 36 inc. a de la RND: la nota se emite en la **modalidad vigente**, y para notas sobre facturas de **modalidades anteriores** (manual, SFV u otra) todos los sistemas deben permitir **transcribir a mano los datos de la factura original**. A qué sectores aplica lo de la Guía de Tránsito: **NO DOCUMENTADO** (no aparece en las páginas descargadas) |
| **1.0.43** (29/05/2024) | Nueva redacción de las **pruebas de autorización** (las 3 fases), habilitación del registro de compras y **nueva funcionalidad de anulación y de reversión de anulación** | **Sí**: plazos de anulación y reversión (sección 3.1, II-10) |
| **1.0.42** (18/04/2024) | Información sobre los **errores devueltos por los servicios del SIN** | **Sí**: tabla de mensajes (página `codigos-error-siat`, de otro bloque) |
| **1.0.41** (12/04/2024) | Funcionalidad de **Registro de Compras** y documento de **Ingreso en Contingencia** | **Sí** para contingencia: [x-ref] `ingreso-a-contingencia.md` |
| 1.0.40 (15/01/2024) | Facturación Tasa Cero IVA Ley 1546 | Por defecto, ninguno |

### 8.2 2021 a 2023: cambios que todavía afectan a Computarizada, Compra-Venta y Notas

| Versión (fecha) | Cambio | Impacto |
|---|---|---|
| 1.0.39 (27/12/2023) | Se completa la documentación de la Reversión de la Anulación | Reversión |
| **1.0.38** (14/12/2023) | Servicios SOAP de **Reversión de Anulación** (facturas, documentos fiscales, boletos) según la **RND 102300000034** | Servicio que hay que implementar (Etapa XI) |
| 1.0.33 (10/07/2023) | Aclaración sobre la anulación de documentos fiscales | Anulación |
| 1.0.32 (13/03/2023) | Aclaración sobre el **envío de códigos especiales** | Nominatividad (II-1) |
| 1.0.31 (16/01/2023) | El XSD de la **Nota C/D Descuento** (sector 47) admite facturas de Compra Venta Bonificaciones | Solo si se usa el 47 |
| **1.0.30** (04/01/2023) | Al **renovar el CUIS** hay que **obtener un CUFD nuevo** para seguir operando | Regla en el ciclo de vida del CUIS y del CUFD |
| 1.0.28 (16/11/2022) | Nueva redacción de las **facturas manuales de contingencia** | CAFC |
| **1.0.27** (24/10/2022) | Nota C/D Descuento ICE: `descuentoItem=((((subTotal-Ice%-IceEsp)*descuentoAdicional)/(montoTotalOriginalsinIce))/cantidadOriginal)*cantidadDevuelta` | No aplica (ICE) |
| **1.0.26** (14/10/2022) | Notas C/D Descuento e ICE: **`descuentoItem=(((subTotal*descuentoAdicional)/(montoTotalOriginal))/cantidadOriginal)*cantidadDevuelta`** | **Sí**, si se usa el 47 (ver el ejemplo más abajo) |
| **1.0.25** (26/09/2022) | Se **reordenan las causales 5, 6 y 7 de contingencia**; se aclaran la asociación y el inicio de operaciones; nuevas unidades de medida | Ver 2.5 |
| **1.0.24** (17/08/2022) | Nuevos sectores **Nota C/D Descuento** (47) y **Nota C/D ICE** | Nota Descuento |
| **1.0.19** (15/06/2022) | Obtener un **CUFD nuevo antes** de registrar el evento y enviar paquetes (antes era una recomendación) | Obligatorio en el flujo de contingencia |
| 1.0.9 (26/11/2021) | Sector Compra Venta Bonificaciones | No aplica por defecto |
| **1.0.8** (15/11/2021) | Unidades de medida nuevas (Rollos, Horas): XSD con **71 unidades**. Se ajusta la **validación del monto total devuelto** en la Nota C/D | Notas C/D. Unidades desde el catálogo |
| **1.0.7** (04/11/2021) | Se amplían el uso del **CAFC**, los **métodos de pago y sus combinaciones** (se abren los XSD) y el inicio de operaciones | II-3, II-12 |
| 1.0.6 (26/10/2021) | Conexión punto a punto para proveedores; uso del **CUFD al emitir con CAFC** | CAFC |
| 1.0.5 (22/10/2021) | Descarga del catálogo de productos (homologación) | Homologación |
| 1.0.4 (05/10/2021) | Validaciones de los XML por sector; títulos y subtítulos | — |
| **1.0.2** (2021, sin fecha) | Computarizada: **token propio o delegado** en lugar de credenciales; **hash del archivo** en el esquema; el sistema debe emitir **individual, por contingencia y, según el giro, masivo**; se agrega el **inciso f** (gestor de envío e impresión); cambia la obligatoriedad de `nombreRazonSocial` en todos los XSD; en la Nota C/D los montos pasan de 12+5 a **15 enteros + 5 decimales** | Base del diseño |
| 1.0.1 / 1.0.0 (2021) | Anexo Técnico de la **RND 102100000011**; servicios SOAP v1 y v2; procedimiento de token; Excel de pruebas | — |

**Ejemplo ilustrativo de la fórmula de V22 1.0.26** (números elegidos para el ejemplo, no son del SIN): con `subTotal = 100.00`, `descuentoAdicional = 10.00`, `montoTotalOriginal = 500.00`, `cantidadOriginal = 4` y `cantidadDevuelta = 2`, el cálculo es `((100 × 10) / 500) / 4 × 2`, es decir `2 / 4 × 2` = **1.00**. El redondeo que corresponde a este cálculo **no está en este bloque**; ver la especificación del algoritmo de redondeo.

**`sistema-facturacion.md` (SF):** el Anexo Técnico documenta las modalidades según la **RND 102100000011**. Última actualización publicada: **10/06/2026** (el cambio "Descuento Patria" de la 1.0.58).

---

## 9. Normativa citada (lo único disponible de la RND)

| Norma | Contenido citado | Fuente |
|---|---|---|
| RND 102100000011, Art. 20 inc. B | Autorización válida 3 años. Nueva autorización antes de que venza | RN |
| RND 102100000011, Art. 26 | Envío del XML y la representación gráfica al correo del comprador o por medios que garanticen la privacidad (WhatsApp, Instagram) | [x-ref] `comprobante-de-transaccion.md` |
| RND 102100000011, Art. 36 inc. a | La Nota C/D se emite en la modalidad vigente; transcripción manual de facturas de otras modalidades | [x-ref] `notas-de-credito-debito.md` |
| RND 102300000034 | Reversión de la anulación | V23 1.0.38 |
| Texto completo de la RND | **NO DISPONIBLE** (sección 0, fila RND) | — |

---

## 10. Secuencia completa, de cero a producción, para M-INV

1. **Requisitos:** NIT activo, obligación IVA y sin marcas de control (1.1).
2. Entrar a `siat.impuestos.gob.bo/launcher/` con credenciales de producción, luego "Gestión de Autorización de Sistemas (PILOTO)", luego "Nuevo Sistema" (1.2).
3. Registrar el sistema: nombre, tipo, versión, masivo (no), modalidad **Computarizada en Línea** y sectores **1 y 24** (y 47 si se usa). Registrar los contactos. Descargar el reporte con el **código de sistema**, los parámetros y las **URL de prueba** (1.3, 1.4).
4. Registrar las **características o funcionalidades** en "Seguimiento de Sistemas Informáticos" (5.3).
5. **Empieza a correr el plazo de 90 días** (más 60 de prórroga si se pide) (1.7).
6. Configurar M-INV en **PILOTO**: token, código de sistema, NIT, `codigoAmbiente = 2`, sucursal 0. Registrar el **punto de venta 1** y pedir un **CAFC de prueba** para los sectores y sucursales que se probarán ([x-ref] `emision-y-envio.md`: *"Para el ambiente de pruebas (PILOTO) deberá solicitar CAFC para los documentos que está autorizando, así como para las sucursales que probarán"*).
7. Ejecutar la **Fase I**, etapas I a VII y XI, con las cantidades de 2.2, hasta tener el 100% en "Seguimiento".
8. Pulsar **"Finalizar Pruebas"** y **programar la inspección** (RN; se entiende igual para la primera autorización).
9. **Fase II, inspección** física o virtual: demostrar cada punto de 3.1 y 3.2.
10. **Autorización** del sistema, que vale 3 años.
11. Si es proveedor: **asociación** de cada NIT cliente y **confirmación** del cliente (5.1, 5.2). Si es propio: ver la duda D3.
12. **Fase III, pruebas piloto:** token delegado; configuración de clientes, productos, sucursales y PV; pruebas a–h (sección 4).
13. **Inicio de operaciones**: fecha, tipo de Internet y proveedor de Internet. Se reciben las URL de producción (6).
14. En **producción**: token de producción, CUIS, CUFD diario, sincronización diaria de catálogos y de fecha y hora, homologación de productos y puntos de venta (6).
15. **Unos meses antes de que se cumplan los 3 años**: nueva autorización, con 3 oportunidades de inspección (7.1).

---

## 11. Qué debe implementar M-INV

Los requisitos llevan el prefijo **M-INV-n**. Donde el requisito es una recomendación propia y no una exigencia de la documentación, se indica.

### A. Configuración y datos de autorización

1. **M-INV-1. Configuración SIAT por empresa (NIT):**
   - NIT, **código de sistema**, modalidad (2), tipo de sistema (propio o proveedor), **ambiente** (piloto o producción), nombre comercial y **versión registrada**, sectores autorizados (1, 24 y, si se usa, 47), marca de proceso masivo (sí o no), fecha de autorización y fecha de vencimiento (**fecha de autorización + 3 años**);
   - **URL de los servicios de piloto y de producción** por separado, ingresadas desde el reporte de registro y desde el inicio de operaciones (sin valores fijos en el código);
   - **token delegado** por ambiente, con su fecha de vencimiento. El token de piloto y el de producción **son distintos** (IO);
   - datos del inicio de operaciones: fecha, tipo de Internet y proveedor de Internet.
2. **M-INV-2. Alerta de vencimiento de la autorización**, con aviso configurable a los 180, 90 y 30 días (propio), y **alerta de fin del plazo de 90 + 60 días** mientras el sistema está en proceso de autorización.
3. **M-INV-3. Pantalla "Acerca de / Datos SIAT"** que muestre el nombre comercial y la versión **idénticos a los registrados**. Emitir con características distintas a las declaradas se "observa" (RN).
4. **M-INV-4. Sucursales y puntos de venta:** la sucursal 0 (casa matriz) y los PV del SIN (PV 0 y PV registrados), relacionados con las sucursales y almacenes de M-INV. En piloto hace falta el **PV 1** para los casos de prueba.

### B. Códigos y sincronización

5. **M-INV-5. CUIS por sucursal y punto de venta:** se pide una vez y se renueva **al vencer**. Se guardan el código y su fecha de vigencia. **Bloqueo:** sin un CUIS vigente no hay emisión, ni en línea ni fuera de línea. **Al renovar el CUIS se pide un CUFD nuevo de inmediato** (V23 1.0.30).
6. **M-INV-6. CUFD diario por sucursal y PV:**
   - se guardan el código, la fecha de vigencia, la fecha de obtención y los demás campos de la respuesta (los campos exactos no están en este bloque; ver la especificación de servicios);
   - **se renueva automáticamente** antes de la primera venta del día, después de sincronizar la fecha y hora (FM-d);
   - **si el servicio de CUFD falla**: se pasa a fuera de línea con el **último CUFD válido**, que en ese caso se extiende **hasta 72 horas** ([x-ref] `ingreso-a-contingencia.md`);
   - **siempre se pide un CUFD nuevo antes** de registrar un evento y de enviar paquetes (V22 1.0.19);
   - todo CUFD usado queda guardado de forma **permanente**, porque las facturas fuera de línea y el evento hacen referencia al "CUFD del evento".
7. **M-INV-7. Sincronización diaria automática** de los **18 catálogos** de la Etapa II, más la fecha y hora (**obligatoria a diario**). Cada catálogo se guarda con su fecha de sincronización. Si la sincronización del día falló, se muestra una alerta. Hay un botón para sincronizar a mano.
8. **M-INV-8. Validar contra el catálogo sincronizado**, no contra los `enumeration` del XSD: métodos de pago, unidades de medida, países, tipos de documento, motivos de anulación y eventos (nota de V21 a V24).
9. **M-INV-9. Homologación de productos:** cada producto de M-INV queda relacionado con la **actividad económica**, el **código de producto SIN** y la **unidad de medida SIN**. No se puede facturar un producto que no esté homologado (IO).

### C. Emisión (computarizada en línea, sectores 1 y 24)

10. **M-INV-10. Emisión individual en línea**, con `codigoEmision = 1`:
    1. armar el XML según el XSD del sector;
    2. validarlo contra el XSD;
    3. comprimirlo en Gzip;
    4. calcular el SHA-256 del comprimido, que va en `hashArchivo` y es la huella digital;
    5. enviarlo;
    6. guardar el `codigoRecepcion` y el estado (908 = válida).
    - Se guardan **el XML original, el comprimido y el hash** de cada factura.
11. **M-INV-11. Validaciones de la venta, bloqueantes (checklist de la Fase II):**
    - a) **número de documento del comprador obligatorio** siempre (II-1). Si el tipo es CI o NIT, **solo números**. `codigoExcepcion` es 0 por defecto, 1 si es NIT y el cajero pide no validar, y **siempre 1 fuera de línea con NIT**. También están disponibles los códigos especiales 99001, 99002 y 99003, que van con NIT y excepción 1;
    - b) **total 0 prohibido** salvo que el pago sea con Gift Card (II-2);
    - c) **pago mixto** con el código de combinación del catálogo (II-3);
    - d) **tarjeta enmascarada**: 4 primeros dígitos, ceros en el medio y 4 últimos (II-6; ejemplo de la documentación: `9999000000009999`). Nunca se guarda el número completo. Para tarjetas que no tienen 16 dígitos, cómo enmascarar **NO está documentado**; se aplica la misma regla de conservar los 4 primeros y los 4 últimos.
12. **M-INV-12. Leyendas:** la **segunda leyenda se elige al azar** del catálogo de leyendas de la actividad, **en cada emisión**, y se guarda en la factura (II-4). La **tercera leyenda** depende de si la factura se emitió en línea o fuera de línea (II-5).
13. **M-INV-13. Nota Crédito-Débito, sector 24:**
    - se emite **en la modalidad vigente**, haciendo referencia a la factura original de M-INV;
    - también se permite la **transcripción manual** de una factura original de otra modalidad (manual o SFV) ([x-ref] 1.0.44, Art. 36 inc. a);
    - se valida el **monto total devuelto** (V21 1.0.8; la regla exacta está en la especificación de Notas C/D);
    - si se usa el sector 47: `descuentoItem` según V22 1.0.26.

### D. Contingencia, eventos, paquetes y CAFC

14. **M-INV-14. Contingencia automática (II-14):** una máquina de estados con estas reglas.
    - **Entrada a fuera de línea**: la verificación de comunicación responde *timeout*, -1, NullPointer o HTTP 500 (o 400/404 al consumir el servicio) y el error se repite en 2 reintentos más. Entonces se pasa **automáticamente** a fuera de línea ([x-ref] `ingreso-a-contingencia.md`).
    - **Reintentos**: se reintenta periódicamente dentro de un plazo prudente, **no mayor a 2 horas**.
    - **Salida de fuera de línea**, al volver la comunicación y **sin que intervenga el usuario**:
      1. pedir un CUFD nuevo;
      2. registrar el evento significativo (`registroEventoSignificativo`) con el CUFD del evento y las fechas de inicio y fin;
      3. armar los paquetes de **hasta 500 facturas** (`codigoEmision = 2`);
      4. enviarlos (`recepcionPaqueteFactura`), que devuelve 901;
      5. validarlos, que devuelve 908, o 904 con la lista de errores por archivo y detalle.
    - **[corregido por revisión]** Para facturas del sector 1, `RecepcionPaqueteFactura` y `validacionRecepcionPaqueteFactura` se invocan en el recurso **"Servicio Factura Compra Venta"** (no en "Facturación Computarizada en Línea"), con `codigoEmision = 2`, `cufd` = CUFD nuevo, `codigoEvento` = **código de recepción** devuelto por `registroEventoSignificativo` (no el código de catálogo), `cafc` nulo salvo transcripción manual y `archivo = GZIP(TAR(XML…))`. Ver `02-…` §2.2 y §6.4 y `00-indice-y-contradicciones.md`. Las notas crédito-débito no tienen servicio de paquetes.
    - Todo esto de forma **automática**, con una pantalla de seguimiento.
15. **M-INV-15. Eventos significativos:**
    - el código de evento se toma **del catálogo sincronizado**, nunca de un valor fijo (sección 2.5);
    - se relaciona por descripción: **fuera de línea** para Internet, inaccesibilidad al servicio web, zonas sin Internet y lugares sin Internet; **CAFC y transcripción** para corte de energía, cambio de infraestructura o hardware, y virus o falla de software (II-12);
    - se registra **hasta 48 horas después** de que termina la contingencia ([x-ref] `contingencia-y-eventos-significativos.md`);
    - se guardan el código de recepción del evento, el CUFD del evento, las fechas y la sucursal y PV.
16. **M-INV-16. Plazos de los paquetes con alerta y bloqueo:**
    - paquetes fuera de línea: **48 h** desde que se recuperó la conexión (II-8);
    - paquetes de facturas manuales transcritas: **72 h** (II-9);
    - panel de "paquetes pendientes" con cuenta regresiva.
17. **M-INV-17. Registro de facturas sin código de respuesta:** cuando hubo timeout al emitir, al recuperar la conexión se consulta el estado de la factura. Si el SIN la registró, se evita la duplicidad (y se anula si hace falta) antes de enviar los paquetes ([x-ref] contingencia).
18. **M-INV-18. Módulo CAFC:**
    - registro de los CAFC autorizados por sucursal y sector, con rango o número inicial y final si corresponde;
    - pantalla de **transcripción** de facturas manuales de contingencia, que usan el **CUFD vigente al entrar en contingencia**;
    - envío en paquete con el CAFC dentro de 72 h;
    - en piloto hace falta un **CAFC de prueba**.

### E. Anulación, reversión, envío y consulta

19. **M-INV-19. Anulación individual:**
    - motivo tomado del catálogo;
    - **bloqueo después del día 9 del mes siguiente** a la emisión (II-10), con una regla configurable por sector (180 días para exportación, II-11);
    - solo si el SIN tiene la factura como válida y **no se usó en ninguna Declaración Jurada** ([x-ref]);
    - se puede anular desde otra sucursal habilitada;
    - **notificación al comprador** por correo con el **código de autorización, el número de factura y el motivo** ([x-ref] `anulacion-de-documentos-fiscales.md`);
    - resultado 905;
    - si el servicio de anulación da timeout, se verifica el estado antes de reintentar.
20. **M-INV-20. Reversión de anulación:**
    - **una sola vez por documento**, hasta el día 9 del mes siguiente a la emisión de la factura original;
    - resultado 907;
    - se manejan los códigos 981, 924, 3011 y 3012 ([x-ref]).
21. **M-INV-21. Envío al comprador (II-13):** correo con el **PDF de la representación gráfica y el XML**, reenvío a pedido y, opcionalmente, un **enlace o QR de al menos 3 × 3 cm** en el ticket, hacia un portal de consulta (V25 1.0.53).
22. **M-INV-22. Consulta de facturas:** filtros por fecha, cliente, estado (válida, anulada, pendiente o observada), tipo de emisión y CUF, con **reimpresión** y **reenvío** del documento. La reimpresión no figura como exigencia en la documentación, pero se recomienda.

### F. Pruebas, auditoría y evidencia para la inspección

23. **M-INV-23. Bitácora SIAT** (recomendado; la documentación no la exige): cada llamada SOAP (operación, fecha, sucursal, PV, CUIS, CUFD, `codigoRecepcion`, estado, mensajes y duración) y cada cambio de modo en línea o fuera de línea. Sirve de evidencia en la inspección y para corregir fallas.
24. **M-INV-24. Asistente "Pruebas de autorización (Fase I)"**, solo para `codigoAmbiente = 2`. Ejecuta y cuenta los casos de la sección 2.3 con las cantidades de 2.2:
    - CUIS: 2 por caso;
    - sincronización: 50 por caso;
    - CUFD: 100 por caso;
    - emisión individual: 500 en total;
    - eventos: 5 por caso;
    - paquetes: 10 por caso;
    - anulaciones: 250;
    - reversiones: 98.

    Muestra el avance por etapa. Genera facturas de prueba en paquetes de "igual a 500" y "menor a 500".
25. **M-INV-25. Guion de inspección** (documento o pantalla) con la demostración de cada punto de II-1 a II-14 y de FM-a a FM-f, para la Fase II.

### G. Arquitectura y proceso

26. **M-INV-26. Un solo componente autorizado:** si la emisión SIAT se hace desde el cliente WPF y también desde una API o servicio en la nube, **cada uno necesita su propia autorización** (V24 1.0.47). Se recomienda concentrar todo el SIAT en **un solo componente**, por ejemplo un servicio de facturación de la capa de infraestructura, y registrar solo ese.
27. **M-INV-27. Sin masiva:** no marcar "Proceso Masivo" y no implementar la Etapa IX, salvo que se decida emitir en lote.
28. **M-INV-28. Multiempresa:** si M-INV va a emitir para **más de un NIT**, registrarlo como **"Proveedor"** y guardar por empresa la **asociación** (tipo de servicio, sectores, token delegado y estado de la confirmación). En ese caso hay que implementar **todas** las funcionalidades mínimas (PA).

---

## 12. Dudas y huecos de la documentación

- **D1.** **¿Propio o proveedor?** M-INV es multiempresa. Si más de un NIT emitirá con él, tiene que ser "Proveedor", con asociación, token delegado y Fase III con cada cliente. La documentación no dice si un sistema *propio* puede usarse para NIT de un mismo grupo empresarial.
- **D2.** **Web y API por separado (1.0.47):** no se define qué es "API" en el caso de un sistema de escritorio con un servicio en la nube (M-INV V4 tiene una base de datos en la nube). Hay que confirmarlo con el SIN o concentrar la emisión en un solo componente.
- **D3.** **Asociación e inicio de operaciones de un sistema propio:** AS e IO describen el caso del proveedor ("los sistemas asociados al NIT"). **NO DOCUMENTADO** si un sistema propio pasa por la asociación, la confirmación y la Fase III, o si inicia operaciones directamente.
- **D4.** **Tope de la emisión masiva:** F1 Etapa IX dice "hasta **2000**"; `requerimientos__sistema-informatico.md`, F3 y el Excel dicen **1000**. No afecta a M-INV si no marca masiva.
- **D5.** **Códigos de eventos significativos:** hay tres órdenes distintos (sección 2.5) y además hubo un reordenamiento de las causales 5, 6 y 7 (V22 1.0.25). Solución: usar solo el catálogo sincronizado.
- **D6.** El texto de F1 incluye "modalidad" entre los catálogos, pero el Excel no tiene hoja de modalidad. Al revés, el Excel incluye Actividades, Fecha y Hora, Actividades Documento Sector, Tipo Habitación y Tipo Punto Venta, que el texto no menciona.
- **D7.** El **sector 47** (Nota C/D Descuento) aparece en el Excel de **reversión**, pero **no** en los de emisión individual ni anulación (que llegan al sector 35 y al 31). No está claro cómo se prueba su emisión en la Fase I.
- **D8.** El **sector 24 no está** en los Excel de paquetes ni de masiva. **NO DOCUMENTADO** si las notas C/D pueden emitirse fuera de línea o en paquetes.
- **D9.** Cómo se reparten las **500 emisiones, 250 anulaciones y 98 reversiones** entre casos o sectores: **NO DOCUMENTADO**. Solo se ve en "seguimiento" del portal.
- **D10.** Lista de **características o funcionalidades** que pide el formulario de registro (RC): **NO DOCUMENTADO**, está en capturas.
- **D11.** Número de **oportunidades de inspección** en la **primera** autorización: **NO DOCUMENTADO**. Solo se dice que son 3 para la renovación.
- **D12.** **Texto de la RND 102100000011:** **no disponible**. El archivo `rnd_356aea02e.pdf` es HTML, y la URL de origen corresponde a otra norma (Código de Control SFV). Las URL `.../uploads/*.pdf` redirigen a la portada del SIN.
- **D13.** Tipos de servicio **"Facturación Conjunta"** y **"Comisionistas"** de la asociación: no tienen descripción en AS.
- **D14.** **Guía de Tránsito en la descripción** (1.0.44): no se dice a qué sectores o productos aplica, ni en qué formato.
- **D15.** **Formato exacto de "UTC extendido sin zona horaria"**, las URL, los namespaces y los parámetros SOAP **no están en este bloque**. Ver la especificación de servicios.
- **D16.** La prórroga se describe como "una prórroga de 60 días más". **NO DOCUMENTADO** si se puede pedir más de una vez; se asume que no.
- **D17.** La Fase III no fija cantidades mínimas y el SIN no la contabiliza, pero iniciar operaciones equivale a una **declaración jurada** de que se hizo. Conviene guardar la evidencia (bitácora, M-INV-23).

---

### Archivos de trabajo generados (solo dentro de `specs/`)

- `specs/_work08/CasosDePrueba*.txt`: volcado completo de cada Excel de casos de prueba.
- `specs/_work08/CasosDePruebaReversionAnulacion.xls` y `.txt`: Excel de reversión que faltaba, bajado de `https://siatinfo.impuestos.gob.bo/images/archivos_tecnicos/archivos_apoyo/CasosDePruebaReversionAnulacion.xls`.
- `specs/_work08/xls_dump.py`: lector mínimo de archivos .xls (BIFF8).
- `specs/_work08/v2025.html` y `v2026.html`: páginas de versionamiento completas, porque las del scratchpad estaban truncadas.
