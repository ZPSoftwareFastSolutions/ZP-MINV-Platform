# Puesta en producción de la facturación SIAT · del simulador al SIN real

M-INV 4.1 factura de punta a punta contra su **simulador del SIN** ([`inicio-rapido-v4.1.md`](../deployment/inicio-rapido-v4.1.md)).
Para emitir facturas **con valor legal** el SIN exige que el sistema esté **autorizado**: registro en el Portal SIAT,
pruebas en el ambiente **piloto** (Fase I), **inspección** (Fase II), **pruebas piloto** con el contribuyente
(Fase III) e **inicio de operaciones**. Esta guía explica cada paso y qué se hace en M-INV. La fuente de cada dato es
[`investigacion-siat/08-autorizacion-inspeccion-versionamiento.md`](investigacion-siat/08-autorizacion-inspeccion-versionamiento.md)
(§1, §2, §3, §6 y §10); los huecos de la documentación pública, en
[`00 §4`](investigacion-siat/00-indice-y-contradicciones.md).

```text
DE CERO A PRODUCCIÓN (resumen)
 0. Requisitos: NIT activo, con obligación IVA y sin marcas de control.
 1. Portal SIAT → «Gestión de Autorización de Sistemas (PILOTO)» → Nuevo Sistema: M-INV, versión 4.1, Computarizada en
    Línea, sectores 1 y 24, SIN proceso masivo → reporte con el CÓDIGO DE SISTEMA y las URL de PRUEBA.  (corren 90 días)
 2. Portal SIAT → Token Delegado Piloto.   Pedir un CAFC de prueba para la sucursal 0.
 3. M-INV (empresa de pruebas, ambiente 2): Facturación SIAT → NIT, código de sistema, URL del piloto, token →
    sucursal 0 y punto de venta 1 → Preparar → Homologación → activar.
 4. Confirmar el contrato SOAP con el WSDL del piloto (SiatSoapContract) y correr las pruebas.
 5. FASE I: CUIS, 18 catálogos, CUFD, 500 emisiones, eventos, paquetes, 250 anulaciones, 98 reversiones → 100 % en
    «Seguimiento» → Finalizar Pruebas.
 6. FASE II: inspección (física o virtual) con el checklist II-1 … II-15 (tabla del §5).
 7. Autorización (vale 3 años). Si M-INV se registró como PROVEEDOR: asociación y confirmación por cada cliente.
 8. FASE III: pruebas piloto con el token del contribuyente (a–h).
 9. Inicio de Operaciones → URL de PRODUCCIÓN → token de PRODUCCIÓN → M-INV en ambiente 1 → Preparar → activar.
```

> **Nunca** mezcle ambientes (regla F-13): los CUIS, CUFD, puntos de venta, eventos, documentos, tokens y URL del
> ambiente 2 no sirven en el 1. Y **nunca** apunte una empresa real al simulador: el simulador acepta cualquier cosa que
> «parezca» correcta y sus documentos no existen para el SIN.

## 1. Registrar el sistema en el Portal SIAT

1. **Requisitos del solicitante** (08 §1.1): NIT **activo**, con **obligación IVA** y **sin marcas de control** ni
   contravenciones.
2. Entre a `https://siat.impuestos.gob.bo/launcher/` con las credenciales de **producción** de *SIAT en Línea* (desde
   la versión 1.0.46 del anexo, las de piloto ya no sirven) → «Gestión de Autorización de Sistemas (PILOTO)» →
   «Autorización de Sistemas Informáticos de Facturación» → «Seguimiento de Sistemas» → **Nuevo Sistema**.
3. Datos del sistema (08 §1.3):

| Campo | Valor para M-INV |
|---|---|
| Nombre comercial | **M-INV** (el mismo que muestra la aplicación) |
| Tipo | **Propio** si una sola empresa factura con su propia instalación; **Proveedor** si Z&P Software instala M-INV para varios contribuyentes (M-INV es multiempresa: hueco **H-20**, decídalo antes de registrar; como proveedor hay que implementar todas las funcionalidades mínimas y hacer asociación + Fase III por cliente) |
| Versión | **4.1** (la que muestra el escritorio en «Acerca de» y el servidor en `/health`) |
| Marca de proceso masivo | **NO** (M-INV no emite masivamente: evita la etapa IX) |
| Modalidad | **Computarizada en Línea** |
| Documentos sector | **1** (Compra Venta) y **24** (Nota Crédito-Débito) |

4. Contactos (nombre, documento, correo y celular válidos). Al terminar, descargue el **reporte**: trae el **código de
   sistema** (`codigoSistema`), los parámetros constantes y las **URL de prueba**. Guárdelo: lo necesita en el §3.
5. **Registre las características** del sistema en «Seguimiento de Sistemas Informáticos» (08 §5.3). Declare
   exactamente lo que usa M-INV: sectores 1 y 24, emisión en línea, fuera de línea con paquetes, contingencia manual
   (CAFC), sin masiva. Las emisiones con características distintas «serán observadas».
6. Desde el registro corren **90 días** para terminar las pruebas (más una prórroga de 60 en «Solicitud de Prórroga»);
   si se vencen, la solicitud se cancela y hay que empezar de nuevo.

## 2. Token delegado del piloto y CAFC de prueba

- En el Portal SIAT: **Token Delegado Piloto** → «Generar Nuevo Token», con la fecha «Hasta» que elija. Cópielo una
  sola vez a M-INV (§3). Es distinto del de producción.
- Pida un **CAFC de prueba** para los documentos que autoriza (sector 1) y para las sucursales que probará (al menos la
  0): la inspección verifica la contingencia manual (II-12).
- En M-INV el token se guarda **cifrado** con la clave maestra (`MINV_INTEGRATION_KEYS`) y no se vuelve a mostrar;
  **no** lo pegue en archivos, correos ni en la documentación.

## 3. Configurar M-INV contra el piloto (ambiente 2)

Use una **empresa de pruebas** (no la de producción del cliente): el piloto exige `sucursal = 0` y los puntos de venta
**0 y 1** en todos los casos (08 §2.1). Con la empresa creada (`minv tenant create …`) y sus productos cargados, como
Administrador en el escritorio (modo **Nube**, contra el servidor que tendrá la salida a internet), **Facturación ›
Facturación SIAT**:

1. **Datos del Padrón**: NIT, razón social exacta, **código de sistema** del reporte, **ambiente 2 (pruebas)**.
2. **Conexión del ambiente de pruebas (2)**: la URL de cada servicio tal como figura en el reporte (Códigos,
   Sincronización, Operaciones, Servicio Factura Compra Venta, Facturación Computarizada y Documentos de Ajuste), el
   **namespace**, la URL base del **QR** del piloto (`https://pilotosiat.impuestos.gob.bo/consulta/QR`), el tiempo de
   espera y el **token delegado** con su vencimiento. Si el reporte solo da una raíz, el patrón habitual (FUERA DE DOC,
   hueco H-01) es `https://pilotosiatservicios.impuestos.gob.bo/v2/<Servicio>`, con los mismos nombres que usa el
   simulador (`FacturacionCodigos`, `FacturacionSincronizacion`, `FacturacionOperaciones`,
   `ServicioFacturacionCompraVenta`, `ServicioFacturacionComputarizada`, `ServicioFacturacionDocumentoAjuste`) y el
   namespace `https://siat.impuestos.gob.bo/`: **confírmelo con el WSDL** (§4).
3. **Sucursales del Padrón**: la casa matriz = **0**.
4. **Puntos de venta**: registre el **punto de venta 1** (registroPuntoVenta) y vincúlelo a una caja; el 0 ya existe.
5. **Preparar** (hora del SIN, CUIS y CUFD de cada punto, 18 catálogos): en Estado SIAT o con
   `minv siat preparar --codigo <empresa> --conexion "<cadena>"`.
6. **Homologación**: cada producto con su actividad y código de producto del SIN (con las sugerencias), las unidades y
   los medios de pago (los códigos salen de los catálogos sincronizados, nunca fijos).
7. **CAFC**: registre el talonario de prueba de la sucursal 0 (sector 1, rango y vigencia).
8. Active la facturación.

## 4. Confirmar el contrato SOAP con el WSDL del piloto (hueco H-01)

La documentación pública **no publica** las URL, el WSDL, el namespace ni las mayúsculas exactas de las operaciones.
M-INV los tiene en **un solo lugar**, `src/2. Infrastructure/MINV.Infrastructure/Billing/Soap/SiatSoapContract.cs`
(servicios, operaciones, nombres de solicitud y respuesta, elementos), y la lectura de las respuestas es tolerante
(por nombre local, `SiatSoapReplies.cs`). Antes de la Fase I:

1. Descargue el WSDL de cada servicio (`<URL>?wsdl`) y compárelo con `SiatSoapContract`:
   - `targetNamespace` (debe coincidir con el namespace de la conexión; si no, corríjalo en la pantalla);
   - nombres de las operaciones (`cuis`, `cufd`, `sincronizarFechaHora`, `sincronizarListaProductosServicios`…,
     `recepcionFactura`, `recepcionPaqueteFactura`, `validacionRecepcionPaqueteFactura`, `anulacionFactura`,
     `reversionAnulacionFactura`, `verificacionEstadoFactura`, `recepcionDocumentoAjuste`, `anulacionDocumentoAjuste`,
     `registroPuntoVenta`, `registroEventoSignificativo`, `verificarComunicacion`, `verificarNit`…);
   - nombre del parámetro de cada solicitud (`SolicitudServicioRecepcionFactura`, `SolicitudCuis`…) y de los campos;
   - tipo de `archivo` (Base64) y estructura de `mensajesList`/`codigosRespuestas` (huecos H-02 y H-03).
2. Si algo difiere, corríjalo en `SiatSoapContract` **y** en el simulador (regla F-16: el contrato cambia en los dos
   lados a la vez) y corra las pruebas: `dotnet test tests/MINV.Infrastructure.Tests --filter Siat` y
   `dotnet test tests/MINV.Integration.Tests --filter Siat` (cliente SOAP contra el simulador HTTP), luego
   `tools\build_v3.ps1`.
3. Prueba de humo contra el piloto: `minv siat sincronizar` y `minv siat preparar` deben terminar sin errores; en
   **Estado SIAT** revise las **llamadas al SIN** (bitácora técnica sin el token: servicio, operación, código HTTP,
   código SIAT, cuerpo). Un 989 es token inválido; un 932, que el sector no corresponde al servicio (URL cruzadas).

## 5. Fase I · Pruebas en el ambiente piloto

El SIN cuenta cada prueba en «Seguimiento» del portal (08 §2.2). Todas con **ambiente 2, sucursal 0 y los puntos de venta
0 y 1**:

| Etapa | Exigido | Cómo se hace con M-INV |
|---|---|---|
| I · CUIS | 2 por caso | Estado SIAT › Solicitar CUIS (o `minv siat preparar`) en los puntos 0 y 1 |
| II · Sincronización | 50 por caso (18 catálogos × 2 puntos) | `minv siat sincronizar` repetido (p. ej. `1..50 | % { dotnet run --project "src/4. Tools/MINV.Cli" -c Release -- siat sincronizar --codigo <empresa> --conexion "<cadena>" }`) |
| III · CUFD | 100 por caso | `minv siat preparar` pide un CUFD nuevo en cada punto cada vez (repítalo) o Estado SIAT › Solicitar CUFD |
| IV · Emisión individual | 500 (sector 1, casos 1-2; sector 24, casos 47-48) | Ventas en la caja del punto 1 y del 0 (o pedidos por el API Gateway, que se facturan y envían solos); notas: devoluciones de esas ventas |
| V · Eventos significativos | 5 por caso (7 eventos × 2 puntos) | Estado SIAT › Declarar contingencia (el evento se elige por **descripción** del catálogo sincronizado: el orden de los códigos cambió entre versiones, 08 §2.5) y cerrarla |
| VI · Paquetes | 10 por caso (sector 1, `codigoEmision = 2`, 500 y menos de 500) | Corte simulado de la salida a internet del servidor (o contingencia declarada), ventas fuera de línea y recuperación automática (evento → paquete → validación 908) |
| VII · Anulación | 250 (motivo «FACTURA MAL EMITIDA» / «NOTA DE CREDITO-DEBITO MAL EMITIDA») | Documentos fiscales › Anular |
| XI · Reversión | 98 | Documentos fiscales › Revertir anulación |
| VIII firma digital · IX y X masiva | — | No aplican (computarizada, sin masiva) |

M-INV 4.1 no trae todavía un asistente que ejecute la Fase I en lote: las cantidades se alcanzan con la operación normal
(caja, API y pantallas) de la empresa de pruebas. Cuando «Seguimiento» muestre el 100 %, pulse **Finalizar Pruebas** y
programe la inspección.

## 6. Fase II · Inspección (checklist II-1 … II-15)

La inspección (física o virtual, 08 §3) verifica la normativa y las funcionalidades mínimas «entre otras cosas». Dónde
está cada punto en M-INV:

| # | Qué verifica el SIN | Dónde está en M-INV | Estado |
|---|---|---|---|
| II-1 | **Nominatividad**: número de documento del comprador en toda transacción; 99001/99002/99003 con NIT y código de excepción 1 | Caja › Datos de facturación (documento obligatorio; «99003 ventas menores» cuando no da datos); regla F-08 en el dominio (`FiscalDocument`) | ✔ |
| II-2 | No emitir facturas con monto 0 (salvo gift card) | El dominio rechaza la factura con total 0 (`fiscal.total`); el monto de gift card se descuenta del sujeto a IVA | ✔ (más estricto: nunca total 0) |
| II-3 | Varios medios de pago **combinados** con los códigos de combinación | La V4.1 cobra con **un** medio de pago por venta (homologado al catálogo) | ⚠ **pendiente** antes de la inspección |
| II-4 | Segunda leyenda (Ley 453) **aleatoria** en cada emisión | `FiscalIssuer` elige al azar una leyenda de la actividad al emitir y la guarda en el documento (la reimpresión sale igual) | ✔ |
| II-5 | Tercera leyenda «en línea» / «fuera de línea» según el tipo de emisión | Facturación SIAT › Leyendas (textos configurables, hueco H-12); la representación gráfica usa la del tipo de emisión real | ✔ |
| II-6 | Tarjeta **enmascarada** (4 primeros, ceros, 4 últimos) | `FiscalRules.MaskCard`: se guarda, envía, audita e imprime solo enmascarada | ✔ |
| II-7 | Tipo de cambio en moneda extranjera | No aplica (sector 1 y 24) | — |
| II-8 | Paquetes fuera de línea dentro de **48 h** | Recuperación automática del despachador; Estado SIAT muestra el plazo con cuenta regresiva y alerta | ✔ |
| II-9 | Facturas manuales (CAFC) transcritas y enviadas dentro de **72 h** | Estado SIAT › Contingencia manual › Transcribir; paquete automático; plazo con cuenta regresiva | ✔ |
| II-10 | Plazo de **anulación** (hasta el día 9 del mes siguiente) | `FiscalRules.VoidDeadline`: Documentos fiscales bloquea la anulación y la reversión fuera de plazo | ✔ |
| II-11 | 180 días en exportación | No aplica | — |
| II-12 | **CAFC** para corte de energía, falla de hardware y falla de software, y su transcripción | Registro del talonario por sucursal y sector; declarar la contingencia manual en el punto de venta; transcribir las facturas manuales (número del talonario, fecha y hora, comprador, líneas); paquete con el CAFC | ✔ |
| II-13 | PDF y XML al **correo** del comprador (o medio electrónico privado) | Facturación SIAT › Correo (SMTP de la empresa, contraseña cifrada); envío automático al emitir y desde Documentos fiscales › Enviar por correo; cada entrega queda registrada | ✔ (configure el SMTP real) |
| II-14 | Entrada y salida del modo fuera de línea y envío de paquetes **automáticos** | Dos fallos seguidos → fuera de línea con evento; el despachador del servidor en la nube recupera solo (CUFD → evento → paquetes → validación) | ✔ |
| II-15 | Otros aplicables al giro | Verificación de estado en el SIN, reimpresión (rollo y PDF), bitácora de cada documento, bitácora técnica de llamadas al SIN, libros de ventas y compras, re-emisión de rechazados | ✔ |

Funcionalidades mínimas del anexo (08 §3.2): emisor (individual y contingencia, XML validado contra el XSD, GZIP,
SHA-256), gestor (anulación), sincronización diaria de catálogos y de fecha y hora, registro de eventos, envío e
impresión: todas implementadas.

Prepare para la inspección: la empresa de pruebas con documentos de cada tipo (en línea, fuera de línea con su paquete,
CAFC transcritas, anuladas, revertidas, notas), el correo SMTP funcionando y el escritorio en las pantallas
**Documentos fiscales** y **Estado SIAT**.

## 7. Autorización y Fase III · Pruebas piloto

- Aprobada la inspección, el sistema queda **autorizado por 3 años** (08 §7).
- **Proveedor**: por cada cliente, «Asociación de Sistemas» (NIT, login, sistema, modalidad, tipo de servicio —licencia,
  alquiler, prestación de servicio…—, sectores) y el cliente **confirma** la asociación (08 §5).
- **Fase III** (obligatoria; el SIN no la contabiliza, 08 §4): el contribuyente genera su **token delegado**, se
  configura su empresa en M-INV (clientes, productos, sucursales, puntos de venta, homologación) y se prueban
  a) sincronización, b) emisión en línea, c) anulación, d) reversión, e) eventos significativos, f) fuera de línea con
  envío automático de paquetes, g) facturas manuales CAFC. (h, masiva, no aplica.)

## 8. Inicio de operaciones (producción, ambiente 1)

1. Portal SIAT → «Inicio y Cierre de Operaciones» → **Inicio de Operaciones**: fecha de inicio, tipo y empresa de
   internet. Se entregan las **URL de producción** (y la ruta del QR de producción, hueco H-25).
2. Portal SIAT → **Token Delegado en Producción** (distinto del de piloto; duración variable).
3. En M-INV, con la empresa **real** del cliente, **Facturación › Facturación SIAT**:
   - Datos del Padrón con el NIT real y el código de sistema; **ambiente 1 (producción)**.
   - **Conexión del ambiente de producción (1)**: URL de producción de cada servicio, namespace, URL del QR de
     producción, token de producción y su vencimiento.
   - Sucursales del Padrón reales y **puntos de venta registrados en producción** (nada del piloto se reutiliza).
4. **Preparar**: CUIS (una vez al año o cuando vence), CUFD del día, hora y catálogos — desde ahí lo mantiene el
   despachador del servidor en la nube cada día (`Minv__Siat__Background=true`).
5. **Homologación** de todos los productos, unidades y medios de pago con los catálogos de producción.
6. Registre los **CAFC** reales de cada sucursal (para cortes de energía o fallas de equipo).
7. Active la facturación. La primera venta de prueba debe salir **VÁLIDA** y su PDF **sin** «SIN VALOR LEGAL»;
   verifíquela con el QR en la consulta pública del SIN.
8. Operación diaria: Estado SIAT (puntos en línea, pendientes, plazos), renovar el token antes de que venza, y los
   libros del mes en Libros fiscales.

## 9. Mantenerse autorizado

- **Nueva autorización** unos meses antes de cumplir los 3 años (con 3 oportunidades de inspección, 08 §7.1).
- Cada **nueva versión del anexo técnico** del SIN (08 §8) puede cambiar XSD, catálogos o servicios: revise el
  versionamiento, actualice los XSD embebidos y `SiatSoapContract` si corresponde, y vuelva a probar en piloto.
- Una **nueva versión de M-INV** que cambie la facturación debe pasar las pruebas de la regla F-17 (CUF con los
  vectores oficiales, XML contra XSD, cliente SOAP contra el simulador, flujo completo en memoria y en PostgreSQL) antes
  de instalarse en un cliente que factura.
