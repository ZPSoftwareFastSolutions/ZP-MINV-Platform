# Inicio rápido · M-INV V6 (tienda web conectada) · paso a paso

La **V6** (6.0.0-alpha.1, rama `Inventario-V6`, sobre `Inventario-V5`) conecta el **catálogo web** de la V5 a la **misma base
de datos** que usa el escritorio. Hasta la V5 la web mostraba un catálogo «de mentira» (un archivo generado) y «Finalizar
armado» solo enseñaba un resumen. Ahora:

- lo que la web muestra (categorías, productos, fichas, precios, imágenes, armados sugeridos y **cuántas unidades hay**) sale
  de la base de datos de Tech Zone Gaming a través de una **API pública de tienda** del API Gateway (`/storefront/v1`);
- el visitante arma su PC y la **reserva** con su nombre y su teléfono: la reserva queda en la base como una cotización
  `ARM-WEB-000001` y el **stock de cada pieza queda reservado por 48 horas**;
- en el escritorio, el vendedor ve esa reserva en **Armador de PC › Cotizaciones**, la **vende en caja** (con factura) o la
  **libera**; también puede reservar sus propias cotizaciones y **publicar** armados sugeridos en la web;
- lo que se vende en el escritorio deja de estar disponible en la web al instante, y lo reservado se ve como reservado en
  los dos lados: **disponible = existencias − reservado**.

Todo se prueba en su propio equipo con la empresa **Tech Zone Gaming S.R.L.** (`TECHZONE`) y el simulador del SIN; no hace
falta contratar nada ni internet (salvo para instalar los paquetes de la web la primera vez). Diseño:
[`docs/architecture/tienda-web-conectada-v6.md`](../architecture/tienda-web-conectada-v6.md) · contrato de la API:
[`docs/integration/storefront-api-v1.md`](../integration/storefront-api-v1.md) · el catálogo web:
[`docs/product/catalogo-web-v5.md`](../product/catalogo-web-v5.md) · todo lo de la edición Tecnología (series, RMA,
facturación) sigue igual que en la V4.2: [`inicio-rapido-v4.2.md`](inicio-rapido-v4.2.md).

> **Importante: una sola base local por equipo.** `tools\bd_local.ps1 -Accion recrear` **borra la base `minv`** de este
> equipo (con lo que tuviera de la V4.2 o la V5) y la reemplaza por Tech Zone Gaming con los datos de la V6. También
> **cambian las contraseñas** de prueba de `usuarios-prueba.txt`. Una base de la V4.2 o la V5 que no quiera perder se
> actualiza sin recrearla (§2, «Base existente»).

```text
EL ALGORITMO V6 · TODO EN ESTE EQUIPO (PowerShell, dentro de la carpeta del repositorio)
 0. Esté en la rama de la V6:                          git switch Inventario-V6
 1. Detenga los servidores de la versión anterior (si estaban encendidos):
        powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion detener
 2. Recree la base con Tech Zone Gaming (153 tablas; BORRA la base anterior y cambia las contraseñas; unos minutos):
        powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear
 3. Publique el programa:  powershell -ExecutionPolicy Bypass -File tools\publicar_escritorio.ps1
 4. Encienda la «nube» local: simulador del SIN :5095 + servidor en la nube :5080 + API Gateway :5090 (con la tienda /storefront/v1)
        powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion iniciar
 5. Encienda la web (otra ventana de PowerShell; la primera vez «npm install» tarda unos minutos):
        cd "src\3. Presentation\MINV.WebCatalog"
        npm install
        npm run dev              → http://localhost:5173  (usa la API en http://localhost:5090: VITE_API_URL)
 6. EL RECORRIDO DE LA FUSIÓN
    a. En la web: Armá tu PC → elija las piezas → «Reservar armado» → nombre y teléfono → número ARM-WEB-… (vale 48 h)
    b. En el escritorio (Ventas o Administrador, dist\M-INV-6.0.0-alpha.1-win-x64\M-INV.exe, empresa TECHZONE, sucursal CM):
         Armador de PC › Cotizaciones › «Reservas web» → ahí está, con el contacto y «Reservado hasta»;
         Stock → la pieza muestra «Reservado: n» y disponible = existencias − reservado
    c. «Vender en caja» → series de las piezas que las llevan → CI o NIT → Cobrar → factura; la reserva se CONSUME
    d. En la web: la ficha de esa pieza ya no tiene esa unidad («Últimas n» o «Agotado»); «Consultar mi reserva» → Vendida
    e. Al revés: en el escritorio arme y guarde una cotización → «Reservar stock» → en la web esa pieza aparece con menos disponible
    f. «Liberar reserva» (escritorio) o «Liberar mi reserva» (web) → el stock vuelve; una reserva que vence la cierra sola
         el gateway (cada 5 minutos) y también devuelve el stock
 Al terminar:  Ctrl + C en la ventana de la web  y  powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion detener
 Quédese en la V6 mientras la prueba: no cambie de rama hasta decidir pasar a otra versión.
```

## Requisitos

- Windows 10 u 11 y el **SDK de .NET 8 o superior** (`dotnet --list-sdks`; con el 10 funciona). No hace falta ser
  administrador del equipo.
- PostgreSQL portátil de M-INV instalado **una sola vez** con
  `powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion instalar -Zip <postgresql-16.x-windows-x64-binaries.zip>`
  (guía de la V3: [`inicio-rapido-v3.md`](inicio-rapido-v3.md), sección 0). Si ya lo instaló para una versión anterior,
  sirve el mismo: solo hay que **recrear** la base (paso 2).
- **Node.js 22** (LTS) o superior para la web (`node --version`; con 20.19 o superior también funciona) y conexión a
  internet la primera vez (`npm install` descarga las bibliotecas).
- Un navegador moderno (Chrome, Edge o Firefox).

## 1. Pasar a la rama de la V6

```powershell
git switch Inventario-V6
powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion detener   # los de la versión anterior, si corrían
```

Los servidores locales se compilan desde la rama activa: si quedaron encendidos los de la V4.2 o la V5, el gateway no
tendría la tienda web. Deténgalos antes de seguir (el paso 2 también lo hace solo).

## 2. Recrear la base con Tech Zone Gaming

```powershell
powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear
```

Compila, borra la base `minv`, la crea con las **153 tablas en 10 esquemas** de la V6 (la tabla nueva es la bitácora de los
armados, `sales.pc_build_events`) y carga la empresa de prueba con los **mismos casos de uso** que el sistema real (unos
2 minutos de carga; más si es la primera compilación). Al final ejecuta `minv verify`. Todo lo que ya cargaba la V4.2
(159 productos con ficha e imagen, 60 días de operación con series, casos RMA, armados, facturas del simulador del SIN)
sigue igual; la V6 agrega:

| Qué | Detalle |
|---|---|
| **Usuario técnico de la tienda** | `tienda-web@techzone.example`, rol **Tienda web** (`TIENDA_WEB`), asignado a la casa matriz. **No tiene contraseña utilizable** y no sirve para entrar al escritorio: el API Gateway lo usa por configuración para ejecutar cada petición de `/storefront/v1` con solo tres permisos (leer el catálogo, reservar y ver stock). `usuarios-prueba.txt` lo menciona sin contraseña |
| **Armados publicados en la web** | Los armados sugeridos del catálogo que quedaron cotizados y compatibles (los dos de prueba de incompatibilidad no) se publican con «Publicar en la web»: son los **armados sugeridos** que muestra la web, a los precios cotizados |
| **Dos reservas web** | Hechas con el mismo caso de uso que usa la web, sobre el stock de la casa matriz y con contactos ficticios `.example`: una de **Valentina Aguirre**, reservada hace 4 días y ya **vencida** (la carga corre el trabajo de vencimiento: quedó **Anulada** con motivo «Vencida» y su stock volvió), y una de **Mateo Condori**, **activa**, reservada hoy a las 09:40 (vence 48 h después). Normalmente son `ARM-WEB-000001` y `ARM-WEB-000002`; la carga imprime cada número |

**Base existente.** Si tiene una base de la V4.2 o la V5 con datos propios, en vez de recrearla aplíquele la migración de
la V6 (no borra nada; agrega la tabla, las columnas, el rol `TIENDA_WEB` y el usuario técnico de cada empresa):

```powershell
dotnet run --project "src/4. Tools/MINV.Cli" -- migrate --conexion "<cadena del rol minv_owner de credenciales-bd-local.txt>"
```

## 3. Publicar el programa

```powershell
powershell -ExecutionPolicy Bypass -File tools\publicar_escritorio.ps1
```

Deja **`dist\M-INV-6.0.0-alpha.1-win-x64\M-INV.exe`** (los de las versiones anteriores quedan en su propia carpeta). Necesita
el runtime de escritorio de .NET 8 o superior; con `-Autocontenido` lo incluye.

## 4. Encender los servidores locales

```powershell
powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion iniciar
```

| Servidor | Dirección | Para qué |
|---|---|---|
| Simulador del SIN (`MINV.SiatSimulator`) | `http://localhost:5095` | responde como el SIN para las facturas (datos de simulación, sin valor legal) |
| Servidor en la nube (`MINV.CloudServer`) | `http://localhost:5080` | el escritorio en modo **Nube**; envía las facturas y recupera los cortes |
| API Gateway (`MINV.ApiGateway`) | `http://localhost:5090` | la API B2B de siempre (`/v1`, con API Key) y, **V6, la tienda web pública** `/storefront/v1` (sin llave). Documentación interactiva en `/docs` (etiqueta «Tienda web») |

El script pasa al gateway la empresa y la sucursal de la tienda y el origen de la web (por defecto `TECHZONE`, `CM` y
`http://localhost:5173`; se cambian con `-EmpresaTienda`, `-SucursalTienda` y `-OrigenTienda`). Al terminar imprime la
dirección de la tienda. Compruébela en el navegador: **`http://localhost:5090/storefront/v1/catalog`** debe mostrar un JSON
con `"code": "TECHZONE"`, la sucursal `CM`, 35 categorías, 40 marcas, 159 productos y los armados publicados. El gateway
además revisa **cada 5 minutos** las reservas vencidas y las cierra (`api-gateway.log` dice cuántas cerró).

`-Accion estado` dice si los tres responden; `-Accion detener` los apaga.

## 5. Encender la web

En **otra** ventana de PowerShell (la web queda corriendo en primer plano):

```powershell
cd "src\3. Presentation\MINV.WebCatalog"
npm install          # una sola vez (y cada vez que cambien las bibliotecas)
npm run dev          # http://localhost:5173
```

Abra **`http://localhost:5173`**. La web pide la **instantánea del catálogo** al gateway (`VITE_API_URL`, por defecto
`http://localhost:5090`) y la refresca sola al volver a la pestaña y cada 60 segundos; la ficha de cada producto consulta
la disponibilidad fresca al abrirse. Mientras carga muestra una pantalla de espera; si el gateway no responde, un aviso con
**«Reintentar»**.

| Variable | Dónde | Valor |
|---|---|---|
| `VITE_API_URL` | archivo `.env.local` en la carpeta de la web (no se versiona) o antes del comando: `$env:VITE_API_URL='http://localhost:5090'; npm run dev` | dirección del gateway que ve **el navegador**. Sin definirla: `http://localhost:5090` |
| `VITE_API_URL=mock` | igual | la web vuelve a usar el catálogo generado de la V5 (sin base de datos, sin reservas): solo para pruebas y demostraciones sin servidor |

Para publicar la web como sitio estático: `npm run build` incrusta la `VITE_API_URL` del momento (`.env.production`;
`.env.example` documenta las variables) y deja `dist/`; en Docker, `deploy/Dockerfile.webcatalog` la recibe
como `ARG VITE_API_URL` y `deploy/docker-compose.yml` la toma de `MINV_WEB_API_URL` (§ despliegue en la nube:
[`despliegue-nube-v4.md`](despliegue-nube-v4.md) §14).

## 6. El recorrido de la fusión

Abra el escritorio **`dist\M-INV-6.0.0-alpha.1-win-x64\M-INV.exe`** → «Nube» → servidor `http://localhost:5080` → **Probar** →
empresa **TECHZONE** → correo y contraseña de `%LOCALAPPDATA%\M-INV\usuarios-prueba.txt` («Base local» también funciona).
Use **Fernando Choque (Ventas, CM)** o el **Administrador** con la sucursal activa **CM**: la tienda web muestra y reserva el
stock de la **casa matriz** (la sucursal configurada en el gateway). Deje la web abierta en el navegador.

### 6.a En la web: armar y reservar

1. **Armá tu PC** (`/arma-tu-pc`): elija una pieza por ranura (procesador, placa, memoria, tarjeta de video, almacenamiento,
   fuente, gabinete…) o parta de un **armado sugerido** (son los publicados desde el escritorio). Cada tarjeta y cada ficha
   dicen **Disponible (n)**, **Últimas n**, **Reservado** (no queda disponible, pero hay unidades reservadas por otros) o
   **Agotado**; lo agotado no se puede agregar y la cantidad máxima es la disponible (hasta 10 por pieza).
2. **Reservar armado**: nombre, teléfono o WhatsApp de Bolivia (7 u 8 dígitos, con o sin `+591`), correo (opcional) y notas
   para la tienda. La web avisa que **le guardan el armado 48 horas y que se confirma y paga en la tienda**.
3. La respuesta trae el **número** (`ARM-WEB-000003` si la carga hizo dos), hasta cuándo vale, las líneas a los precios del
   momento y el total; el armado de la web queda vacío. Anote el número y el teléfono: son la «llave» para **Consultar mi
   reserva** (`/reserva/ARM-WEB-000003`). Si alguna pieza no alcanza, la web marca cuáles y cuánto hay
   (**no se reserva nada**: es todo o nada).

Por detrás, la web hizo `POST /storefront/v1/reservations` con una cabecera `Idempotency-Key` (un UUID por intento): si
la conexión se corta y reintenta, el servidor devuelve la **misma** reserva en vez de crear dos.

### 6.b En el escritorio: la reserva y el stock reservado

1. **Tecnología › Armador de PC › Cotizaciones** → filtro rápido **«Reservas web»**. La reserva aparece con
   **Canal Web**, el **contacto** (nombre y teléfono; el correo en el detalle), **«Reservado hasta»** (resaltado si vence en
   menos de 6 horas o ya venció), el total y las unidades reservadas. El detalle trae las notas del cliente, «Copiar
   teléfono» y cada línea con su disponibilidad. También puede filtrar por estado **Reservados**.
2. **Inventario › Stock** → busque una de las piezas: **«Reservado: n»** y el disponible = existencias − reservado. Lo
   mismo en la ficha del producto del **Catálogo**.
3. **Inicio › Tecnología** → tarjeta **«Reservas web activas»** (cantidad y Bs) con enlace al armador filtrado.
4. Quién ve qué: las cotizaciones (y las reservas web) las listan los roles con `sales.view` (Ventas, Cajero, Gerencia y
   Administrador), y el teléfono y el correo del cliente solo los que además tienen `sales.pcbuild.manage` (esos mismos
   cuatro roles). Bodega y Consulta no entran a Cotizaciones: ven el stock con lo reservado, sin el contacto.

### 6.c Vender en caja (consume la reserva)

1. En la cotización web: **«Vender en caja»** (Ventas, Cajero o Administrador: hace falta la caja). El aviso dice que la
   venta **consume la reserva**.
2. Cada pieza sale a su **precio cotizado** (congelado al reservar); las piezas serializadas piden sus **series**; CI o NIT
   del comprador → **Cobrar** → factura del SIN (VÁLIDA contra el simulador) y ticket.
3. El armado queda **Vendido** con la venta vinculada; las reservas de stock se **consumen**: la unidad se descuenta **una
   sola vez** (nunca «reservada» y además «vendida»). Si la pieza reservada era la última, la caja de otra sucursal nunca
   la vio disponible: la reserva la protegía.

### 6.d En la web: la unidad ya no está

1. Abra la ficha de una de las piezas vendidas (o recargue): el disponible bajó; si era la última unidad dice **Agotado**.
   La instantánea se refresca sola en menos de un minuto; la ficha, al abrirse.
2. **Consultar mi reserva** con el número y el teléfono: estado **Vendida**.

Para verlo «por dentro», en PowerShell (sin llave ni contraseña):

```powershell
Invoke-RestMethod http://localhost:5090/storefront/v1/products/case-cor-4000d | Select-Object sku, available, reserved, onHand
Invoke-RestMethod "http://localhost:5090/storefront/v1/reservations/ARM-WEB-000003?phone=71234567"   # el teléfono con que reservó
```

### 6.e Al revés: reservar desde el escritorio y mirar la web

1. **Armador de PC › Armar**: arme una PC, **Guardar cotización** (con vigencia) → en **Cotizaciones**, **«Reservar stock»**: pide las
   horas (48 por defecto; 24, 72 o 168 sugeridas) y reserva el stock de cada pieza en la sucursal del armado, todo o nada (si falta stock, dice
   qué piezas y cuánto hay).
2. En la web, esas piezas muestran menos disponible (o **Reservado** si no queda ninguna libre): la web y el escritorio ven
   el **mismo número**.
3. **«Publicar en la web»** / **«Quitar de la web»** sobre una cotización del escritorio la muestra o la esconde como
   **armado sugerido** en la web (solo armados del escritorio cotizados, reservados o vendidos; la web solo lista los que
   tienen todas sus piezas en el catálogo y marca si todas están disponibles).

### 6.f Liberar y vencer

- **Liberar reserva** en el escritorio (pide el motivo) o **«Liberar mi reserva»** en la web (`/reserva/:numero`, con el
  teléfono y una confirmación): el armado pasa a **Anulado** con el motivo y el stock vuelve a estar disponible en el acto.
- **Vencer**: una reserva que pasa de sus 48 h la cierra el **trabajo en segundo plano del gateway** (cada 5 minutos):
  Anulada con motivo «Vencida», stock devuelto. Nunca se cierra «al leer»: hasta que pase el trabajo, la web la muestra como
  **Vencida** y el escritorio la resalta. La reserva de **Valentina Aguirre** de la carga es exactamente eso (búsquela con el
  filtro Anulado o Reservas web).
- Anular una cotización reservada desde el escritorio también libera su stock.

Todo cambio deja su fila en la **bitácora del armado** (creado, cotizado, reservado, liberado, vencido, vendido,
publicado): el detalle de la cotización la muestra, y en **Administración › Actividad** cada reserva de la
web aparece con el canal `storefront` y el teléfono enmascarado.

## 7. Qué NO hace la V6

- **Sin pagos en línea**: la reserva se confirma y se cobra en la tienda física (efectivo, QR, tarjeta o transferencia en la
  caja). La web solo reserva.
- **Sin series desde la web**: la reserva es por cantidad; las series (o IMEI) se eligen al vender en caja.
- **Una sola sucursal**: la web muestra el stock de la sucursal configurada (casa matriz `CM`) y ahí se retira. No hay
  retiro en otra sucursal ni stock consolidado en la web.
- **Sin cuentas de cliente**: no hay registro ni inicio de sesión en la web; una reserva se consulta o se libera con su
  número **y** el teléfono con que se hizo (si no coinciden, «no existe»: no se revela nada).
- **Sin carrito persistente**: el armado vive en la memoria de la pestaña (a propósito); lo que se guarda es la reserva.
- **Límites por diseño**: 1 a 20 líneas y 1 a 16 unidades por línea por reserva; 300 lecturas y 10 reservas por minuto por
  dirección IP; la reserva vale 48 horas (configurable en el gateway).
- La compatibilidad de las piezas **se informa, no bloquea**: el vendedor la revisa en el escritorio (la cotización queda
  marcada si hay errores).

## 8. Usuarios de prueba

Empresa **TECHZONE** · Tech Zone Gaming S.R.L. Las **contraseñas** no están aquí: están en
`%LOCALAPPDATA%\M-INV\usuarios-prueba.txt` y cambian cada vez que se recrea la base.

| Rol | Nombre | Correo | Sucursales | En la V6 |
|---|---|---|---|---|
| Administrador | Administrador General | admin@techzone.example | Todas | Todo: reservar, liberar, vender, publicar; ve el contacto |
| Gerencia | Luis Gutiérrez | luis.gutierrez@techzone.example | Todas (gerencia global) | Reservar, liberar y publicar; ve el contacto; no cobra (no tiene caja) |
| Bodega | Mariana Suárez | mariana.suarez@techzone.example | CM | Ve el stock con lo reservado; no ve las cotizaciones ni el contacto (hace falta `sales.view`) |
| Bodega | Sergio Mamani | sergio.mamani@techzone.example | CB | Ídem |
| Bodega | Daniela Céspedes | daniela.cespedes@techzone.example | SC | Ídem |
| Ventas | Fernando Choque | fernando.choque@techzone.example | CM | Reservar, liberar, **vender en caja**, publicar; ve el contacto |
| Ventas | Sergio Rojas | sergio.rojas@techzone.example | SC | Ídem, en SC (la tienda web reserva en CM) |
| Cajero | Miguel Ortiz | miguel.ortiz@techzone.example | CM | Reservar, liberar, vender en caja, publicar; ve el contacto |
| Cajero | Luis Flores | luis.flores@techzone.example | CM | Ídem |
| Cajero | Camila Fernández | camila.fernandez@techzone.example | CB | Ídem, en CB |
| Cajero | Camila Morales | camila.morales@techzone.example | SC | Ídem, en SC |
| Consulta | María Villarroel | maria.villarroel@techzone.example | CM, CB, SC | Solo lectura: stock con lo reservado y reportes; sin cotizaciones ni contacto |
| **Tienda web** (técnico) | Tienda web | tienda-web@techzone.example | CM | **Sin contraseña**: lo usa el API Gateway para `/storefront/v1`; no entra al escritorio |

Los clientes de las reservas web de la carga (Valentina Aguirre, Mateo Condori) son ficticios, con correos `.example` y
teléfonos de prueba; no son usuarios del sistema.

## 9. Si algo no funciona

| Síntoma | Solución |
|---|---|
| La web muestra el aviso de error con «Reintentar» (o la consola del navegador dice `Failed to fetch`) | El gateway no responde o la web apunta a otra dirección: `tools\servidores_locales.ps1 -Accion estado`; abra `http://localhost:5090/storefront/v1/catalog` en el navegador; revise `VITE_API_URL` (sin barra final) y reinicie `npm run dev` después de cambiarla |
| La consola del navegador dice **CORS** («blocked by CORS policy») | El gateway solo admite el origen configurado (`http://localhost:5173`). Abra la web exactamente con esa dirección (no `127.0.0.1`, no otro puerto). Si Vite arrancó en otro puerto porque el 5173 estaba ocupado, cierre lo que lo ocupa o reinicie los servidores con `-OrigenTienda http://localhost:<puerto>` |
| `npm run dev` dice «Port 5173 is in use» | Otra web quedó corriendo (otra ventana o el `preview`): ciérrela; o use `npm run dev -- --port 5174` **y** reinicie los servidores con `-OrigenTienda http://localhost:5174` |
| `http://localhost:5090/storefront/v1/catalog` responde **503 «Tienda web no disponible»** | El gateway no encontró la empresa, el usuario técnico o la sucursal: la base es vieja (sin la migración V6, no tiene el usuario `tienda-web`) → `minv migrate` (§2) o recrear; o los servidores se compilaron desde otra rama → `git branch --show-current` debe decir `Inventario-V6`, detener e iniciar de nuevo |
| El servidor en la nube o el gateway no arrancan: «La base de datos no está al día: faltan …» | Aplique la migración V6 (§2, «Base existente») o recree la base |
| La web carga pero muestra el catálogo con todo «Disponible» y reservar «no guarda nada» | Está en modo **mock** (`VITE_API_URL=mock` en `.env.local` o en la ventana): quítelo y reinicie `npm run dev` |
| La web muestra **409 «No hay stock suficiente»** al reservar | Alguien vendió o reservó esas unidades antes: la web marca las piezas afectadas y cuánto hay; baje la cantidad o cambie la pieza. No se reservó nada |
| «El teléfono debe tener 7 u 8 dígitos (Bolivia)» | Escriba un celular o fijo boliviano: `71234567`, `+591 71234567` o `2-2345678`; sin letras |
| «Consultar mi reserva» dice que **no existe** | El número o el teléfono no coinciden con los de la reserva (a propósito no se dice cuál). Use exactamente el teléfono con que reservó, con o sin `+591` |
| **429** (la web dice que hay demasiadas peticiones) | Límite por IP: 300 lecturas o 10 reservas por minuto. Espere un minuto |
| «La llave de idempotencia ya se usó para otra reserva» | Se repitió una `Idempotency-Key` con otro contenido (solo pasa probando la API a mano): use una llave nueva |
| En el escritorio no aparece la reserva web | Sucursal activa: la tienda reserva en **CM**; con Gerencia o Administrador elija CM o «Todas las sucursales». Si la hizo hace más de 48 h, búsquela en **Anulado** (venció) |
| «Vender en caja» dice que la reserva venció o el armado está anulado | La reserva pasó sus 48 h y el trabajo la cerró: pida al cliente que vuelva a reservar, o guarde la cotización de nuevo (precios y vigencia nuevos) |
| La caja no deja vender más que el disponible aunque haya existencias | Están reservadas (web o escritorio): venda desde la reserva («Vender en caja» en la cotización) o libérela |
| La reserva vencida sigue apareciendo **Reservada** unos minutos | El trabajo de vencimiento corre cada 5 minutos en el gateway (`servidores_locales.ps1` debe estar iniciado); hasta entonces la web la muestra **Vencida** y el escritorio la resalta |
| `npm install` falla | Revise `node --version` (22 recomendado) y la conexión a internet; borre `node_modules` y repita |
| Facturas «fuera de línea», series, RMA, IMEI… | Igual que en la V4.2: [`inicio-rapido-v4.2.md`](inicio-rapido-v4.2.md) §9 |

Los registros de los servidores están en `%LOCALAPPDATA%\M-INV\api-gateway.log`, `servidor-nube.log` y `simulador-sin.log`;
el de la web, en la ventana de `npm run dev`.

## 10. Volver a la V5 o a la V4.2

Las versiones anteriores siguen en sus ramas, sin cambios. Para volver: detenga los servidores y la web,
`git switch Inventario-V5` (o `Inventario-V4.2`) y recree la base desde esa rama (`tools\bd_local.ps1 -Accion recrear`).
En la V5 la web funciona sola, con su catálogo generado (`npm run dev` en la misma carpeta), sin servidores.
