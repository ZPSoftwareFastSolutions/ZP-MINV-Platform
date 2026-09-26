# Inicio rápido · M-INV V4.1 (facturación SIAT) · paso a paso

La **V4.1** (4.1.0-alpha.1, rama `Inventario-V4.1`) agrega a M-INV la **facturación SIAT** de Bolivia en la modalidad
**Facturación Computarizada en Línea**: cada venta de caja o de la tienda en línea emite su **factura Compra Venta**, las
devoluciones emiten una **nota crédito-débito**, y M-INV se encarga solo de enviarlas al SIN, de seguir facturando
cuando se corta internet y de ponerse al día cuando vuelve. Esta guía lo prueba TODO en su propio equipo con un
**simulador del SIN** (datos de simulación, **sin valor legal**): no hace falta token real, NIT real ni internet.

Si todavía no instaló la base local, empiece por la guía de la V3 ([`inicio-rapido-v3.md`](inicio-rapido-v3.md),
sección 0). Lo multi-sucursal, la nube y el API siguen igual que en la V4 ([`inicio-rapido-v4.md`](inicio-rapido-v4.md)).
Para pasar al SIN de verdad: [`docs/billing/puesta-en-produccion-siat.md`](../billing/puesta-en-produccion-siat.md).

```text
EL ALGORITMO V4.1 · FACTURACIÓN EN ESTE EQUIPO (todo en PowerShell, dentro de la carpeta del repositorio)
 1. Base local con datos de prueba QUE FACTURAN (los últimos 25 días):
        powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear
 2. Servidores locales: simulador del SIN (:5095) + servidor en la nube (:5080) + API Gateway (:5090):
        powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion iniciar
 3. Abrir dist\M-INV-4.1.0-alpha.1-win-x64\M-INV.exe → «Nube» → servidor http://localhost:5080 → empresa MINV →
        un usuario de %LOCALAPPDATA%\M-INV\usuarios-prueba.txt
 4. Administrador: Facturación › Estado SIAT (8 puntos de venta EN LÍNEA, CUFD del día)
 5. Cajero CM: Punto de venta → vender con NIT o CI → la factura sale VÁLIDA con su QR
 6. Facturación › Documentos fiscales → abrir la factura → PDF (dice «SIN VALOR LEGAL»: es el ambiente de pruebas)
 7. Corte de internet simulado:  dotnet run --project "src/4. Tools/MINV.Cli" -- siat simulador-apagar
        vender (sale FUERA DE LÍNEA, la caja NO se bloquea) →  … siat simulador-encender  → en 1 o 2 minutos todo VÁLIDO
 8. Gerencia: anular una factura y revertir la anulación · devolución parcial con nota crédito-débito
 9. Bodega: registrar la factura de un proveedor (Facturación › Libros fiscales › Compras)
10. Contador (Administrador): Facturación › Libros fiscales → libro de ventas IVA, compras y resumen IVA/IT → Excel
 Al terminar:  powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion detener
```

## Requisitos

- Windows 10 u 11 y el **SDK de .NET 8 o superior** (`dotnet --list-sdks`).
- PostgreSQL portátil de M-INV ya instalado con `tools\bd_local.ps1 -Accion instalar -Zip <binarios.zip>` (una sola
  vez; ver la guía de la V3). No hace falta ser administrador del equipo.
- El escritorio publicado (`powershell -ExecutionPolicy Bypass -File tools\publicar_escritorio.ps1`, que deja
  `dist\M-INV-4.1.0-alpha.1-win-x64\M-INV.exe`).

## Paso 1 · Recrear la base con la facturación

```powershell
powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear
```

Tarda unos minutos (más que en la V4: ahora cada venta se factura y se envía al SIN simulado). Si los servidores locales
estaban encendidos, el script los detiene primero. Borra la base `minv`, la crea con las **140 tablas en 9 esquemas**
de la V4.1 (27 de ellas de facturación, en el esquema `billing`) y carga la empresa de prueba **MINV · Ferretería El
Constructor S.R.L.** con los mismos casos de uso que el sistema real. Lo nuevo de la V4.1:

- La empresa **factura desde hace 25 días** («ese día la ferretería empezó a facturar con M-INV»): NIT de simulación
  **1023456028**, razón social **FERRETERÍA EL CONSTRUCTOR S.R.L.**, **ambiente 2 (pruebas)** contra el simulador del
  SIN de este equipo (`http://localhost:5095`).
- Sucursales del Padrón: **CM = 0** (La Paz, casa matriz), **EA = 1** (El Alto) y **SC = 2** (Santa Cruz). Cada caja
  tiene su propio **punto de venta del SIN** (CAJA01, CAJA02 y CAJA03 en CM, EA-CAJA1 y SC-CAJA1), además del punto 0
  de cada sucursal: **8 puntos de venta**, cada uno con su CUIS y el CUFD de cada día.
- Catálogos del SIN sincronizados y **todo homologado**: los 61 productos (por categoría hacia productos SIN del
  catálogo de ferretería), las unidades (UND → unidad (bienes), KG → kilogramo, MT → metro…) y los medios de pago
  (efectivo, tarjeta, QR, transferencia).
- Desde ese día **cada venta de caja lleva comprador**: los clientes habituales con su NIT (empresas) o su CI
  (personas), algunos con correo `.example`, y compradores eventuales con CI; quien no da datos sale con el NIT
  especial **99003** (ventas menores del día). Los **pedidos de la tienda en línea** (API) también se facturan.
- **Escenarios reales** para explorar:

| Escenario | Qué pasó | Dónde verlo |
|---|---|---|
| Corte de internet en El Alto | Un día, de 10:00 a 13:00 el SIN «no respondió»: la caja siguió vendiendo con facturas **fuera de línea**; al volver internet, M-INV registró el **evento significativo**, envió el **paquete** y el SIN lo validó | Estado SIAT › Eventos y Paquetes (evento de EA **conciliado**) · Documentos fiscales (tipo de emisión «fuera de línea») |
| Corte de energía en Santa Cruz | De 15:00 a 17:00 la caja de SC facturó **a mano** con el talonario de contingencia (**CAFC**); a las 17:30 se transcribieron las **3 facturas manuales** y se enviaron en paquete | Estado SIAT › Eventos (contingencia manual de SC) · Documentos fiscales (N° 1001 a 1003 con CAFC) |
| NIT inválido | Una factura con un NIT mal escrito fue **rechazada** por el SIN (código 1037) y se **re-emitió con código de excepción** | Documentos fiscales (estado «rechazada» con el mensaje del SIN, y su reemplazo válido) |
| 3 anulaciones | Una con **devolución de la mercadería**, otra **re-emitida** con el CI corregido y otra **revertida** (se anuló por error) | Documentos fiscales (estados «anulada» y «revertida») |
| 2 devoluciones parciales | Con su **nota crédito-débito** válida | Documentos fiscales (tipo «nota crédito-débito») · Ventas › Devoluciones |
| 4 facturas de proveedores | Registradas sobre recepciones de mercadería | Libros fiscales › Compras |

Al terminar, todos los puntos quedan **en línea**, con el CUFD vigente para hoy, y nada queda pendiente de envío.

El script guarda **solo en este equipo** (nunca en el repositorio):

| Archivo en `%LOCALAPPDATA%\M-INV\` | Qué tiene |
|---|---|
| `usuarios-prueba.txt` | empresa, rol, sucursales, nombre, correo y contraseña de cada usuario de prueba, y el resumen de la facturación |
| `credenciales-bd-local.txt` | contraseñas de PostgreSQL (`postgres`, `minv_owner`, `minv_app`, `minv_server`) |
| `claves-integracion.txt` | la clave maestra de integraciones de este equipo, la API Key de la tienda y el **token de SIMULACIÓN del SIN** (`MINV_SIAT_TOKEN`) |
| `siat-simulador.json` | lo que «sabe» el simulador del SIN: CUIS, CUFD, facturas recibidas, eventos y paquetes de la carga |

Para verlos: `notepad $env:LOCALAPPDATA\M-INV\usuarios-prueba.txt`. Las contraseñas y el token son **aleatorios** y
cambian cada vez que se recrea la base. El token de simulación solo lo acepta el simulador de este equipo: **nunca** lo
use con el SIN real (en la base va cifrado con la clave maestra; en pantalla nunca se muestra).

¿Quiere la base de la V4 sin facturación? `tools\bd_local.ps1 -Accion recrear -SinFacturacion`.

## Paso 2 · Iniciar los servidores (con el simulador del SIN)

```powershell
powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion iniciar
```

Arranca, en este orden:

| Servidor | Dirección | Para qué |
|---|---|---|
| Simulador del SIN (`MINV.SiatSimulator`) | `http://localhost:5095` | responde como el SIN (CUIS, CUFD, catálogos, recepción de facturas, eventos, paquetes, anulaciones). Lee `siat-simulador.json` y acepta solo el `MINV_SIAT_TOKEN` |
| Servidor en la nube (`MINV.CloudServer`) | `http://localhost:5080` | el escritorio en modo Nube. Además hace el **trabajo automático de la facturación**: envía lo pendiente cada pocos segundos y, cada minuto, recupera los cortes y pide el CUFD del día |
| API Gateway (`MINV.ApiGateway`) | `http://localhost:5090` | la tienda en línea (sus pedidos también se facturan) |

`tools\servidores_locales.ps1 -Accion estado` dice si los tres responden y si el simulador «tiene internet».

## Paso 3 · Abrir el escritorio

1. Abra **`dist\M-INV-4.1.0-alpha.1-win-x64\M-INV.exe`**.
2. Elija **Nube**, servidor `http://localhost:5080`, **Probar** (debe decir «disponible»).
3. Empresa **MINV**, correo y contraseña de `usuarios-prueba.txt`.

(También funciona en **Base local**: el escritorio toma la clave maestra de `claves-integracion.txt` y envía él mismo
las facturas al simulador. La **Demostración** también factura, con un simulador en memoria y 4 facturas de ejemplo de hace una
semana, sin tocar el disco.)

En el menú aparece la sección **Facturación**: **Documentos fiscales**, **Estado SIAT**, **Homologación**, **Libros
fiscales** y, para el Administrador, **Facturación SIAT** (la configuración). La guía de cada pantalla está en
`docs/product/escritorio-v4.1.md`.

## Paso 4 · Mirar el estado de la facturación (Administrador)

**Facturación › Estado SIAT**: por sucursal y punto de venta, el **modo** (en línea / fuera de línea / contingencia
manual / recuperando), la vigencia del **CUIS** (un año) y del **CUFD** (el del día), lo pendiente de envío, los
**eventos significativos**, los **paquetes** y los plazos con cuenta regresiva. Deben verse los 8 puntos **en línea**.

En **Facturación SIAT** (configuración) están el NIT y la razón social del Padrón, el código de sistema, el ambiente
(2 = pruebas), la conexión (URL del simulador, token «cargado» con su vencimiento, nunca visible), las sucursales del
Padrón, los puntos de venta y el correo de la empresa (desactivado en la prueba: los correos `.example` no existen).

Lo mismo desde la línea de órdenes:

```powershell
dotnet run --project "src/4. Tools/MINV.Cli" -- siat estado
```

## Paso 5 · Facturar en la caja con NIT o CI (Cajero CM)

1. Entre como un **Cajero de CM** (CAJA01). Abra la caja si lo pide.
2. Agregue productos como siempre. En **Datos de facturación** elija el tipo de documento:
   - **CI** (persona): número (solo dígitos), complemento si tiene (p. ej. `1A`) y nombre.
   - **NIT** (empresa): número y razón social; **Verificar NIT** consulta el Padrón (en el simulador, un NIT de 5 a 13
     dígitos que termina en `999` sale **inactivo**).
   - Sin datos: **99003** (ventas menores del día). Correo opcional (ahí se le envían el PDF y el XML).
3. Cobre (con tarjeta, el número se guarda y se imprime **enmascarado**: 4 primeros, ceros y 4 últimos).
4. La caja envía la factura al SIN y en uno o dos segundos muestra **VÁLIDA**; el ticket sale con el **QR** de consulta.

Pruebe también con el NIT `3456789999` (inactivo en el Padrón simulado): el SIN **rechaza** la factura (código 1037)
y M-INV la **re-emite solo** con el **código de excepción** (el comprador confirma su NIT): en Documentos fiscales
quedan la rechazada, con el mensaje del SIN, y su reemplazo válido. (Si pide la verificación de NIT al capturarlo, la
caja le avisa antes.)

## Paso 6 · Ver el documento y su PDF

**Facturación › Documentos fiscales**: busque por fecha, sucursal, estado o número. En el detalle están el **CUF**, el
número, el punto de venta, el comprador, las líneas, los mensajes del SIN y la **bitácora** (emitida, enviada, válida,
anulada…). Desde ahí: **PDF** (media carta, con «SIN VALOR LEGAL» porque es el ambiente de pruebas), **reimprimir** en
rollo, **enviar por correo**, **verificar en el SIN** y ver el **XML** exacto que se envió.

## Paso 7 · Simular un corte de internet y ver la recuperación automática

```powershell
dotnet run --project "src/4. Tools/MINV.Cli" -- siat simulador-apagar       # el SIN deja de responder
```

1. Venda en la caja: el envío no obtiene respuesta y, tras dos fallos seguidos, el punto de venta pasa **fuera de
   línea** con un evento abierto. **La caja no se bloquea**: la factura sale **fuera de línea** (con el último CUFD) y
   se entrega igual. Las ventas siguientes ya salen fuera de línea desde el principio.
2. En **Estado SIAT** verá el punto «FUERA DE LÍNEA» y un **evento significativo** abierto (como el SIN no responde,
   M-INV lo registra como «Inaccesibilidad al servicio web de la Administración Tributaria», del catálogo del SIN).
3. Vuelva internet:

```powershell
dotnet run --project "src/4. Tools/MINV.Cli" -- siat simulador-encender
```

4. En uno o dos minutos el servidor en la nube lo resuelve **solo**: CUFD nuevo → registra el evento → arma el paquete →
   lo envía → lo valida. En Estado SIAT el punto vuelve **en línea**, el evento queda **conciliado** y el paquete
   **validado**; en Documentos fiscales esas facturas pasan a **válidas**. (Con prisa:
   `dotnet run --project "src/4. Tools/MINV.Cli" -- siat procesar`.)

`… siat simulador-estado` muestra si el simulador «tiene internet» y cuántos documentos recibió.

## Paso 8 · Anular, revertir y devolver (Gerencia)

- **Anular**: en Documentos fiscales, una factura válida › **Anular** › motivo (del catálogo del SIN). Elija si el
  cliente **devuelve la mercadería** (vuelve al stock) o no. Solo se puede hasta el **día 9 del mes siguiente**; M-INV
  bloquea después de esa fecha.
- **Revertir la anulación** (si se anuló por error): la misma factura › **Revertir anulación**. Una sola vez, en el
  mismo plazo.
- **Re-emitir** (datos del comprador equivocados): anule con el motivo de datos incorrectos y use **Re-emitir** con los
  datos corregidos: sale una factura nueva, con otro número y otro CUF.
- **Devolución parcial con nota crédito-débito**: en la factura › **Devolución** (o en Ventas), elija qué productos y
  cuántos devuelve y el motivo: la mercadería vuelve al stock, se registra el reembolso y se emite la **nota
  crédito-débito**, que se envía al SIN como la factura. (Sin internet, la nota espera en cola: el SIN no las acepta
  fuera de línea.)

Cada anulación, reversión y nota queda en la bitácora del documento y se avisa al comprador por correo (si la empresa
tiene el correo activo).

## Paso 9 · Registrar la factura de un proveedor (Bodega o Administrador)

**Facturación › Libros fiscales › Compras** muestra las **recepciones de mercadería sin factura**. Elija una e ingrese
el **número de factura** del proveedor, su **código de autorización**, la fecha y el importe: queda registrada con sus
líneas (las de la recepción), su crédito fiscal y su asiento contable, y entra al **libro de compras** del mes.

## Paso 10 · Sacar los libros del mes (el contador)

**Facturación › Libros fiscales**, elija el mes:

- **Libro de ventas IVA**: cada factura y nota del mes (válidas y anuladas con su marca), con el débito fiscal.
- **Libro de compras**: las facturas de proveedores, con el crédito fiscal.
- **Resumen IVA / IT**: débito, crédito y saldo del período.

**Exportar** a CSV o Excel. Los totales salen de los documentos (no se guardan aparte) y cuadran con las ventas cobradas.

## Dónde están las contraseñas y el token

| Qué | Dónde |
|---|---|
| Usuarios de prueba (correo y contraseña por rol y sucursal) | `%LOCALAPPDATA%\M-INV\usuarios-prueba.txt` |
| Contraseñas de PostgreSQL | `%LOCALAPPDATA%\M-INV\credenciales-bd-local.txt` |
| Clave maestra de integraciones (cifra el token del SIN, la contraseña SMTP y los secretos de webhooks) | `%LOCALAPPDATA%\M-INV\claves-integracion.txt` (`MINV_INTEGRATION_KEYS`) |
| Token de **simulación** del SIN | `%LOCALAPPDATA%\M-INV\claves-integracion.txt` (`MINV_SIAT_TOKEN`); en la base, cifrado |
| Lo que sabe el simulador del SIN | `%LOCALAPPDATA%\M-INV\siat-simulador.json` (el de la carga anterior queda como `siat-simulador.anterior.json`) |
| Registros de los servidores | `%LOCALAPPDATA%\M-INV\simulador-sin.log`, `servidor-nube.log`, `api-gateway.log` |

Nada de esto está en el repositorio ni en la documentación. Un token **real** del SIN se carga solo en la pantalla
**Facturación SIAT** (se guarda cifrado y no se vuelve a mostrar).

## Si algo no funciona

| Síntoma | Solución |
|---|---|
| `bd_local.ps1` termina con «Facturación SIAT omitida: falta la clave maestra…» | el archivo `claves-integracion.txt` se borró o está dañado: vuelva a ejecutar `tools\bd_local.ps1 -Accion recrear` (lo regenera) |
| La carga de datos tarda mucho | es normal la primera vez (compila y factura unas mil ventas); si pasa de 15 minutos, revise que PostgreSQL responda (`tools\bd_local.ps1 -Accion estado`) |
| La caja dice «La facturación no está lista en esta caja» | abra Estado SIAT: si el punto no tiene CUFD, pulse **Preparar** (o `dotnet run --project "src/4. Tools/MINV.Cli" -- siat preparar`); si no tiene punto de venta, créelo en Facturación SIAT › Puntos de venta |
| Todas las facturas quedan **fuera de línea** | el simulador no está encendido o está «apagado»: `tools\servidores_locales.ps1 -Accion estado`; si dice APAGADO, `… siat simulador-encender` |
| El simulador responde pero rechaza con «token inválido» | se recreó la base y el simulador arrancó con el token anterior: `tools\servidores_locales.ps1 -Accion iniciar` (lo reinicia con el nuevo) |
| Después de recrear la base el SIN «no conoce» los CUFD | el simulador se inició antes de la carga o con otro archivo: deténgalo e inícielo otra vez con `servidores_locales.ps1` (lee `siat-simulador.json`) |
| El punto de venta sigue fuera de línea después de encender el simulador | espere un minuto (el servidor en la nube lo recupera en su siguiente pasada) o `… siat procesar`; en Base local, el escritorio lo hace mientras está abierto |
| «Rechazada» con el código 1037 | NIT del comprador inactivo en el Padrón: M-INV re-emite con el código de excepción; si el NIT estaba mal escrito, anule esa factura con el motivo de datos incorrectos y re-emítala con el NIT correcto (o el CI) |
| No se puede anular: «fuera de plazo» | la anulación solo se permite hasta el día 9 del mes siguiente a la emisión (regla del SIN) |
| No aparece la sección Facturación | el usuario no tiene permiso `billing.*` (Bodega no lo tiene) o la empresa no tiene el módulo «Facturación SIAT» |
| «Este equipo no tiene la clave maestra de integraciones» (Base local) | falta `claves-integracion.txt`: use el modo **Nube** con los servidores locales, o recree la base |
| El libro de ventas no coincide con Ventas | el libro solo cuenta los días facturados (los últimos 25) y las facturas del mes elegido |
| El PDF dice «SIN VALOR LEGAL» | es correcto: el ambiente 2 (pruebas) siempre lo lleva. En producción (ambiente 1) desaparece |
