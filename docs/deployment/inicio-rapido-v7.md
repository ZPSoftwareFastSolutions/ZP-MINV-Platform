# Inicio rápido · M-INV V7 (plataforma web) · paso a paso

La **V7** (7.0.0-alpha.1, rama `Inventario-V7`, sobre `Inventario-V6`) lleva **todo el sistema a la web**. La tienda de la V6
(catálogo real, disponibilidad y reservas de armados) gana:

- **cuentas de cliente**: el botón **«Ingresar»** abre el inicio de sesión y el registro; el cliente tiene **«Mi cuenta»**
  con sus reservas y sus datos;
- un **carrito de compras**: se reserva cualquier producto, también **uno solo** («Reservar ahora»), sin pasar por «Armá tu
  PC» (que sigue igual); la reserva pide los datos del cliente, los datos para la factura (opcionales) y cuándo pasa a
  recogerla (1, 2 o 3 días) y queda con un número **`RES-WEB-000001`**;
- el **correo automático** con el código y el detalle de la reserva, que sale desde la cuenta de la empresa
  (`zapasoftwarefastsolutions@gmail.com`) por una cola con reintentos;
- el **panel web del personal** (`/panel`): cada rol ve sus módulos (caja, ventas, reservas, stock, compras, sucursales,
  facturación, reportes, contabilidad, usuarios…) con botones, listas desplegables, filtros y exportación a CSV; las
  estadísticas quedan plegadas detrás de **«Ver estadísticas»**;
- en el escritorio, la pantalla **Ventas › Reservas**, la cola de **Correos**, un inicio simplificado y filtros con
  «Exportar CSV» ([`docs/product/escritorio-v7.md`](../product/escritorio-v7.md)).

Todo se prueba en su propio equipo con la empresa **Tech Zone Gaming S.R.L.** (`TECHZONE`), el simulador del SIN y un
**buzón de correo de prueba** que no envía nada a internet. La tienda y el panel corren en **Docker Desktop**, con un enlace
público para los clientes. Guías relacionadas: la web para todos [`docs/product/plataforma-web-v7.md`](../product/plataforma-web-v7.md) ·
Docker en detalle [`tienda-publica-docker-v7.md`](tienda-publica-docker-v7.md) · diseño
[`docs/architecture/plataforma-web-v7.md`](../architecture/plataforma-web-v7.md) · lo de la V6 (armados, API de tienda)
sigue igual: [`inicio-rapido-v6.md`](inicio-rapido-v6.md).

> **Importante: una sola base local por equipo.** `tools\bd_local.ps1 -Accion recrear` reemplaza la base `minv` de este
> equipo por Tech Zone Gaming con los datos de la V7 y **cambia las contraseñas** de prueba. En la V7 ya no se pierde nada
> por accidente: **antes de borrar, guarda una copia** en `%LOCALAPPDATA%\M-INV\respaldos\` (si la copia falla, no borra
> nada). Una base de la V6 con datos propios también se puede actualizar sin recrearla (§2, «Base existente»).

```text
EL ALGORITMO V7 · TODO EN ESTE EQUIPO (PowerShell, dentro de la carpeta del repositorio)
 0. Esté en la rama de la V7:                          git switch Inventario-V7
 1. Apague lo de la versión anterior (lo que tenga encendido):
        powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion detener
        powershell -ExecutionPolicy Bypass -File tools\docker_local.ps1 -Accion bajar
 2. Recree la base con Tech Zone Gaming (157 tablas; RESPALDA la anterior y cambia las contraseñas; unos minutos):
        powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear
 3. Mire los usuarios de prueba (personal y «Clientes de la tienda web»):
        notepad $env:LOCALAPPDATA\M-INV\usuarios-prueba.txt
 4. Publique el programa:  powershell -ExecutionPolicy Bypass -File tools\publicar_escritorio.ps1
 5. Suba la tienda y el panel en Docker, con el buzón de prueba (la primera vez construye las imágenes: 5 a 10 minutos):
        powershell -ExecutionPolicy Bypass -File tools\docker_local.ps1 -Accion subir -Correo prueba -ReiniciarSimulador
    → muestra TIENDA PARA TUS CLIENTES (https://….trycloudflare.com) y PANEL DEL PERSONAL (…/panel)
    → en este equipo: tienda http://localhost:5173 · panel http://localhost:5173/panel · correos http://127.0.0.1:8025
 6. EL RECORRIDO
    a. Tienda: «Ingresar» → «Crear cuenta» (o una cuenta de «Clientes de la tienda web») → un producto → «Reservar ahora»
       (o «Agregar al carrito» → Carrito → «Reservar») → días para recoger → «Confirmar reserva» → número RES-WEB-…
    b. Buzón de prueba http://127.0.0.1:8025 → llega «Reserva RES-WEB-… · <empresa>» con el código y el detalle
    c. Panel http://localhost:5173/panel → ingrese con cada rol → Ventas › Reservas → la reserva, con su correo «Enviado»
    d. Vender: en el panel, Reservas → «Vender en caja» (Caja web) o en el escritorio Ventas › Reservas → «Vender en caja»
       → series si las lleva → CI o NIT → Cobrar → factura; la reserva se CONSUME
    e. Tienda: «Mi cuenta» → «Mis reservas» → la reserva dice «Vendida»
 7. Correo de verdad (Gmail): correo.txt en %LOCALAPPDATA%\M-INV con la contraseña de aplicación y
        powershell -ExecutionPolicy Bypass -File tools\docker_local.ps1 -Accion subir -Correo real
 Al terminar (si no quiere dejar la tienda publicada):  powershell -ExecutionPolicy Bypass -File tools\docker_local.ps1 -Accion bajar
 Quédese en la V7 mientras la prueba: no cambie de rama hasta decidir pasar a otra versión.
```

## Requisitos

- Windows 10 u 11 y el **SDK de .NET 8 o superior** (`dotnet --list-sdks`; con el 10 funciona). No hace falta ser
  administrador del equipo.
- PostgreSQL portátil de M-INV instalado **una sola vez** con
  `powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion instalar -Zip <postgresql-16.x-windows-x64-binaries.zip>`
  (guía de la V3: [`inicio-rapido-v3.md`](inicio-rapido-v3.md), sección 0). Si ya lo instaló para otra versión, sirve el
  mismo.
- **Docker Desktop** abierto y con «Engine running» (la tienda, el panel, los servidores, el buzón de prueba y el túnel
  corren ahí). La web se construye dentro de Docker: **no** hace falta Node.js para seguir esta guía (solo para programar la
  web).
- Conexión a internet la primera vez (Docker descarga las imágenes base) y para el enlace público del túnel.
- Un navegador moderno (Chrome, Edge o Firefox).

## 1. Pasar a la rama de la V7

```powershell
git switch Inventario-V7
powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion detener
powershell -ExecutionPolicy Bypass -File tools\docker_local.ps1 -Accion bajar
```

Los servidores y las imágenes se construyen desde la rama activa: si quedaron encendidos los de la V6, la tienda no tendría
cuentas, carrito ni panel. `bajar` detiene los contenedores (la base local, las imágenes y el estado del simulador quedan);
si Docker Desktop no estaba abierto, ignore su aviso.

## 2. Recrear la base (con respaldo)

```powershell
powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear
```

1. **Respalda** la base `minv` que haya (copia completa con `pg_dump`) en
   `%LOCALAPPDATA%\M-INV\respaldos\minv-AAAAMMDD-HHMMSS.dump` y muestra la orden `pg_restore` para volver a ella. Si la copia
   falla, se detiene sin borrar nada.
2. Borra la base, la crea con las **157 tablas en 10 esquemas** de la V7 (las nuevas: cuentas de cliente y la cola de correos
   con sus envíos y sus intentos) y carga Tech Zone Gaming con los mismos casos de uso del sistema real (unos minutos; más si
   es la primera compilación). Al final ejecuta `minv verify`, que ahora también comprueba las reservas.

Todo lo de la V6 sigue (159 productos con ficha e imagen, 60 días de operación, series, garantías, armados publicados en la
web, facturas del simulador del SIN). La V7 agrega:

| Qué | Detalle |
|---|---|
| **2 cuentas de cliente** | Registradas como lo hace la web; cada una reservó desde su cuenta un carrito y un armado. Están en la sección «Clientes de la tienda web» de `usuarios-prueba.txt` |
| **Carritos de la tienda** | Uno vigente de **un solo monitor** con datos para la factura (el caso «quiero solo un monitor») y uno vencido, cerrado por el trabajo de vencimiento |
| **Carrito de mostrador** | Hecho por un vendedor de la casa matriz para un cliente registrado (`RES-CM-…`) |
| **Correos en la cola** | 9 confirmaciones pendientes (las reservas con correo). Al subir Docker, el despachador envía al buzón de prueba las que siguen vigentes y cancela las de reservas vencidas |

Para copiar la base sin recrearla (por ejemplo antes de probar algo):

```powershell
powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion respaldar
```

**Base existente.** Una base de la V6 con datos propios se actualiza sin recrearla con la migración de la V7 (agrega las 4
tablas, las columnas del carrito, el rol «Cliente web» y la función de la cola; no borra nada). Respáldela antes y siga la
guía de migraciones (`.claude/database-migration-guide.md` §11, que trae una consulta previa y la verificación):

```powershell
powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion respaldar
dotnet run --project "src/4. Tools/MINV.Cli" -- migrate --conexion "<cadena del rol minv_owner de credenciales-bd-local.txt>"
```

## 3. Los usuarios de prueba

Las contraseñas **no** están en el repositorio ni en esta guía: están en `%LOCALAPPDATA%\M-INV\usuarios-prueba.txt` y
**cambian cada vez que se recrea la base**.

```powershell
notepad $env:LOCALAPPDATA\M-INV\usuarios-prueba.txt
```

| Sección del archivo | Para qué sirve |
|---|---|
| La tabla del **personal** (rol, nombre, correo, contraseña y sucursales) | Entrar al **escritorio** (M-INV.exe, empresa `TECHZONE`) y al **panel web** (`/panel`, con el mismo correo y contraseña). Hay una persona por rol: Administrador, Gerencia, Bodega, Ventas, Cajero y Consulta, repartidas en las sucursales CM (casa matriz, La Paz), CB y SC |
| **«Clientes de la tienda web»** | Entrar a la **tienda** con «Ingresar» (Mi cuenta: sus datos y sus reservas). **No** sirven para el escritorio ni para el panel |

La cuenta técnica «Tienda web» que menciona el archivo no tiene contraseña: la usa el API Gateway y no es una persona.

## 4. Publicar y abrir el escritorio

```powershell
powershell -ExecutionPolicy Bypass -File tools\publicar_escritorio.ps1
```

Deja **`dist\M-INV-7.0.0-alpha.1-win-x64\M-INV.exe`**. Ábralo cuando Docker esté arriba (§5): «Nube» → servidor
`http://localhost:5080` → **Probar** → empresa **TECHZONE** → correo y contraseña de una persona del personal («Base local»
también funciona). Para el recorrido use una persona de **Ventas o Cajero de la casa matriz (CM)**, o el Administrador con la
sucursal activa **CM**: la tienda web reserva el stock de la casa matriz.

## 5. Encender la tienda y el panel en Docker

```powershell
powershell -ExecutionPolicy Bypass -File tools\docker_local.ps1 -Accion subir -Correo prueba -ReiniciarSimulador
```

`subir` apaga lo que ocupe los puertos, escribe `deploy\.env` con las claves de este equipo (no las muestra ni se versiona),
construye las imágenes **7.0.0-alpha.1** y levanta: servidor en la nube (`:5080`, también la sesión web y el panel), API
Gateway (`:5090`, la tienda y el correo), simulador del SIN (`:5095`), la web (`:5173`), el túnel público y el **buzón de
prueba** (`:8025`). `-ReiniciarSimulador` copia al contenedor el estado del simulador de la carga recién hecha: úselo
siempre después de `recrear`. Al final:

```text
================================================================================
  TIENDA PARA TUS CLIENTES:  https://….trycloudflare.com
  PANEL DEL PERSONAL:        https://….trycloudflare.com/panel
================================================================================
  En este equipo:           http://localhost:5173  (panel: http://localhost:5173/panel)
  Correos de prueba:        http://127.0.0.1:8025
```

| Orden | Qué hace |
|---|---|
| `tools\docker_local.ps1 -Accion estado` | Dice si responde cada parte: nube, gateway, simulador, tienda, panel, la tienda y la sesión web a través de la web, el buzón y el enlace público |
| `tools\docker_local.ps1 -Accion enlace` | Muestra el enlace vigente (cambia cada vez que el túnel se reinicia), lo guarda en `%LOCALAPPDATA%\M-INV\enlace-publico.txt` y lo pone en «Ver mi reserva» de los correos |
| `tools\docker_local.ps1 -Accion reanudar` | Enciende la base y los contenedores sin construir (lo que hace la tarea programada al iniciar sesión) |
| `tools\docker_local.ps1 -Accion arranque` | Registra una vez la tarea «M-INV Tienda publica»: al iniciar sesión en Windows todo se enciende solo |
| `tools\docker_local.ps1 -Accion bajar` | Detiene y quita los contenedores (la base, las imágenes y el estado del simulador quedan) |

`-Correo prueba` deja **todos** los correos en el buzón de prueba (`http://127.0.0.1:8025`, solo este equipo): ninguno sale a
internet, aunque el cliente escriba un correo real. Más detalle (puertos, qué se publica, cabeceras de seguridad):
[`tienda-publica-docker-v7.md`](tienda-publica-docker-v7.md).

## 6. El recorrido del cliente: cuenta, carrito, reserva y correo

Abra la tienda: **`http://localhost:5173`** en este equipo o el enlace público desde un celular.

### 6.a Crear una cuenta o ingresar

1. **«Ingresar»** (arriba a la derecha, también en el teléfono). Para una cuenta nueva: **«Crear cuenta»** → nombre, correo,
   teléfono de Bolivia y contraseña (de 8 a 128 caracteres, con letras y números) dos veces. Registrarse crea **siempre una
   cuenta de cliente**.
2. O ingrese con una cuenta de la sección «Clientes de la tienda web» de `usuarios-prueba.txt`.
3. Con credenciales incorrectas la página dice «Correo o contraseña incorrectos» y **no deja pasar**. A los 5 intentos fallidos
   la cuenta se bloquea 15 minutos y la página lo avisa.

Sin cuenta también se puede reservar (con nombre, teléfono y correo en el formulario); la cuenta sirve para ver y liberar las
reservas en «Mi cuenta».

### 6.b Un solo producto o el carrito

1. En el **Catálogo**, abra un producto con disponibilidad (por ejemplo un monitor).
2. **«Reservar ahora»**: va directo a la reserva de ESE producto (cambie la cantidad ahí mismo). No toca el carrito ni «Armá tu
   PC».
3. O **«Agregar al carrito»** en varios productos → ícono del carrito → **`/carrito`**: cantidades, «Quitar», total. Si algo se
   agotó o bajó mientras tanto, la página lo marca y pide ajustar antes de reservar. → **«Reservar»**.

### 6.c Reservar

1. **Con cuenta**: sus datos aparecen solo para leer («Cambiar mis datos» lleva a Mi cuenta). **Sin cuenta**: nombre y
   apellido, teléfono o WhatsApp y **correo** (ahí llega la confirmación).
2. **«¿Cuándo pasás a recogerlo?»**: mañana (24 h), en 2 días (48 h) o en 3 días (72 h).
3. Opcional, **«Datos para tu factura»**: tipo de documento (CI, CEX, PAS, OD o NIT), número, complemento (solo con CI) y
   nombre o razón social. La caja los propone al cobrar. Con cuenta se usa el documento de la cuenta.
4. **«Confirmar reserva»**. La confirmación muestra el **número** (con la carga de prueba, normalmente `RES-WEB-000005`) con
   «Copiar», hasta cuándo se guarda, dónde se retira (la casa matriz), el detalle y el total, y **«Te enviamos un correo a
   …»** si dejó correo. El stock de cada producto queda reservado: la web y el escritorio muestran menos disponible.

Si alguien se llevó las unidades antes, la página marca cuánto hay y ofrece **«Ajustar a lo disponible»** sin perder lo
escrito (la reserva es todo o nada: no se reservó nada).

### 6.d El correo en el buzón de prueba

Abra **`http://127.0.0.1:8025`**. En unos segundos (el despachador revisa la cola cada 15 s) llega **«Reserva RES-WEB-… ·
<empresa>»**, de la empresa, con el saludo, **el código de la reserva** para presentar al recogerla, cada producto con
cantidad, precio y subtotal, el total («se paga al recoger»), hasta cuándo se guarda, dónde se recoge y el botón **«Ver mi
reserva»** (lleva al enlace público vigente). En el buzón también aparecen las confirmaciones de las reservas de la carga de
prueba que seguían en la cola.

## 7. El personal en el panel (cada rol)

Abra **`http://localhost:5173/panel`** (o `…trycloudflare.com/panel`). Sin sesión, la web pide ingresar y después vuelve al
panel. Use el correo y la contraseña de una persona del personal de `usuarios-prueba.txt`.

- **Inicio**: «Hola, …», el rol y la **sucursal activa**; debajo, **«¿Qué quiere hacer?»** con un **botón grande** por cada
  función del rol, agrupados por sección. **«Ver estadísticas ^»** está **cerrado**: al abrirlo, cada estadística se carga
  por separado.
- **Menú lateral** por secciones, con un **buscador de pantallas**; arriba, las migas de pan, la lista **«Sucursal activa»**
  (si la persona tiene más de una) y su nombre (cambiar contraseña, ir a la tienda, cerrar sesión).
- **Cada lista**: filtros en listas desplegables y búsqueda, «Limpiar filtros», tabla ordenable y paginada, menú «⋯» por
  fila, detalle al costado y **«Exportar CSV»**. Los filtros quedan en la dirección: se puede guardar o compartir la lista
  filtrada.

| Rol | Qué ve en el panel |
|---|---|
| **Administrador** | Todo: Caja, Ventas, Clientes, Reservas, Armador de PC, Series, Garantías, Stock, Catálogo, Movimientos, Toma física, Alertas, Pedido sugerido, Órdenes de compra, Proveedores, Sucursales, Transferencias, Documentos fiscales, Estado del SIAT, Reportes, Contabilidad, Usuarios, Integraciones (con «Correos de reservas»), Configuración y Actividad |
| **Gerencia** | Todas las sucursales. Ventas, Clientes, Reservas (sin Caja), Armador de PC, Series, Garantías, Stock, Catálogo, Alertas, Pedido sugerido, Órdenes de compra, Proveedores, Sucursales, Transferencias, Documentos fiscales, Estado del SIAT, Reportes, Contabilidad y Actividad |
| **Ventas** | Su sucursal. **Caja**, Ventas, Clientes, Reservas, Armador de PC, Series, Garantías, Stock, Catálogo, Movimientos (salidas), Alertas, Pedido sugerido (solo mirar), Sucursales, Documentos fiscales, Estado del SIAT y Reportes |
| **Cajero** | Su sucursal. Lo mismo que Ventas, sin Sucursales ni Reportes |
| **Bodega** | Su sucursal. Series, Garantías, Stock, Catálogo, Movimientos (entradas y ajustes), Toma física, Alertas, Pedido sugerido (genera las órdenes), Órdenes de compra, Proveedores, Sucursales, Transferencias y Reportes. Nada de ventas |
| **Consulta** | Solo lectura: Series, Garantías, Stock, Catálogo, Alertas, Pedido sugerido, Sucursales, Documentos fiscales, Estado del SIAT y Reportes |
| **Cliente web** | **No entra al panel**: su página es «Mi cuenta» |

<!-- V7-MODULOS: completar con los módulos que falten -->

Un botón que el rol no puede usar no aparece; si alguien abre por dirección una pantalla que no le corresponde, el panel dice
**qué permiso falta**. Igual el servidor decide en cada pedido. La lista completa de módulos, botones y filtros:
[`docs/product/plataforma-web-v7.md`](../product/plataforma-web-v7.md).

**Ventas › Reservas** (Ventas, Cajero, Gerencia y Administrador): la reserva del recorrido aparece arriba, con tipo **Compra**,
canal **Web**, el cliente, el teléfono, el total, **«Reservado hasta»** (en ámbar si vence en menos de 6 h, en rojo si ya
venció) y el estado del correo (**Enviado**). El detalle muestra los productos, los datos para la factura, la bitácora y los
correos, con **Vender en caja**, **Liberar**, **Reenviar correo**, **Copiar teléfono** y **Abrir WhatsApp**. **«Nueva reserva
en mostrador»** reserva productos para un cliente que llama o pasa por la tienda.

## 8. Vender la reserva y verla «Vendida»

La venta **consume** la reserva: la unidad se descuenta una sola vez. Hágalo por UNO de los dos caminos, con una persona de
**Ventas o Cajero de la casa matriz (CM)** o el Administrador con la sucursal activa CM:

**En el panel (Caja web)**

1. **Ventas › Reservas** → la reserva → **«Vender en caja»** (o **Caja › «Vender una reserva»** y elegirla de la lista).
2. Si la caja está cerrada: **«Abrir caja»** → la caja y el fondo inicial.
3. La reserva se carga con sus **precios congelados** y los **datos para la factura** que dejó el cliente. Si un producto lleva
   serie o IMEI, elija sus unidades.
4. **«Cobrar»** (F4) → medio de pago (y efectivo recibido) → **«Cobrar Bs …»**. La factura va al simulador del SIN y se puede
   imprimir con el navegador, descargar en PDF o enviar por correo.

**En el escritorio**

1. M-INV.exe (§4) → **Ventas › Reservas** → la reserva → **«Vender en caja»**.
2. Series si las lleva → CI o NIT (precargado desde la reserva) → **Cobrar** → factura y ticket.

**El cliente la ve «Vendida»**: en la tienda, **«Mi cuenta» › «Mis reservas»** (filtro «Vendidas») o **«Consultar mi
reserva»** (`/reserva`) con el número y el teléfono.

Otras pruebas: **«Liberar mi reserva»** (Mi cuenta) o **«Liberar»** (panel o escritorio) devuelve el stock al instante; una
reserva que pasa su plazo la cierra sola el gateway (cada 5 minutos) y su correo pendiente se cancela. **«Reenviar correo»**
manda otra confirmación (también a otro correo, si el cliente escribió mal el suyo).

## 9. Correo de verdad con Gmail

El envío real sale desde **`zapasoftwarefastsolutions@gmail.com`**. Google no deja usar la contraseña normal de la cuenta:
hace falta una **contraseña de aplicación**, que **solo puede crear el dueño de la cuenta**.

1. En la cuenta de Google de la empresa: **Seguridad › Verificación en dos pasos** → actívela.
2. **Seguridad › Contraseñas de aplicaciones** → cree una (por ejemplo «M-INV») y cópiela: son 16 letras.
3. Cree el archivo `correo.txt` en la carpeta de datos de M-INV (fuera del repositorio; nunca lo suba a Git):

   ```powershell
   notepad $env:LOCALAPPDATA\M-INV\correo.txt
   ```

   con estas líneas (la contraseña de aplicación sin espacios, en lugar del texto entre `<>`):

   ```text
   MINV_MAIL_HOST=smtp.gmail.com
   MINV_MAIL_PORT=587
   MINV_MAIL_STARTTLS=true
   MINV_MAIL_USER=zapasoftwarefastsolutions@gmail.com
   MINV_MAIL_PASSWORD=<contraseña de aplicación>
   MINV_MAIL_FROM=zapasoftwarefastsolutions@gmail.com
   MINV_MAIL_FROM_NAME=Tech Zone Gaming
   ```

4. Vuelva a subir con el correo real:

   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\docker_local.ps1 -Accion subir -Correo real
   ```

   Con `-Correo auto` (lo que usa `subir` si no se indica nada) basta con que `correo.txt` tenga la contraseña para usar el
   correo real. `-Correo apagado` no envía nada (las confirmaciones quedan en la cola).
5. Pruebe con una reserva a su propio correo, o en el panel (Administrador): **Integraciones › Correos de reservas** →
   «Reenviar una confirmación».

Otra forma, desde el panel: **Administración › Configuración › Correo de la empresa** → «Configurar con Gmail» (guarda la
contraseña cifrada en la base). Si ese correo está **activo**, el sistema lo usa en lugar de `correo.txt`.

## 10. Qué NO hace la V7

- **Sin pagos en línea ni envío a domicilio**: la reserva se confirma y se cobra en la tienda.
- **Sin verificación del correo** al registrarse ni recuperación de la contraseña por correo: si un cliente la olvida, el
  Administrador le asigna una temporal (panel › Usuarios › «Restablecer una contraseña»).
- **Una sola sucursal en la web**: la tienda muestra y reserva el stock de la casa matriz (CM).
- La **caja web** imprime con la impresora del navegador: no maneja la impresora de tickets ESC/POS ni el cajón (eso sigue en
  el escritorio).
- El **enlace de trycloudflare cambia** al reiniciar el túnel y solo funciona con el equipo encendido.
- Límites por diseño: 16 unidades por producto y 20 productos por reserva; de 1 a 3 días para recoger; 10 reservas por minuto,
  10 inicios de sesión por minuto y 5 registros por hora por dirección IP; 3 correos por destinatario y 300 por empresa cada
  24 h.

## 11. Si algo no funciona

| Síntoma | Solución |
|---|---|
| `subir` dice «Docker Desktop no responde» | Abra Docker Desktop y espere «Engine running»; repita |
| `subir` dice «Falta la base local (rol minv_server o claves de integracion)» | Recree la base (§2) antes de subir |
| `subir -Correo real` dice que falta `correo.txt` | Créelo como en §9 (con `MINV_MAIL_HOST`, `MINV_MAIL_USER`, `MINV_MAIL_PASSWORD` y `MINV_MAIL_FROM`) |
| `recrear` falla porque la base está en uso | Detenga antes los contenedores: `tools\docker_local.ps1 -Accion bajar` |
| Algo sale «NO» en `-Accion estado` | Mire el registro del servicio: `docker compose -f deploy\docker-compose.yml --env-file deploy\.env logs --tail 60 apigateway` (o `cloudserver`, `webcatalog`, `buzon`) |
| La web se ve como la V6 (sin «Ingresar» ni carrito) | Las imágenes son de otra rama: `git branch --show-current` debe decir `Inventario-V7`; vuelva a `subir` (reconstruye) |
| «Correo o contraseña incorrectos» con una cuenta de prueba | Las contraseñas cambian al recrear la base: copie la del `usuarios-prueba.txt` actual. Las cuentas de «Clientes de la tienda web» entran a la tienda, no al panel |
| «Cuenta bloqueada por intentos fallidos» | 5 intentos fallidos: espere 15 minutos, o el Administrador le asigna una contraseña temporal (panel › Usuarios) |
| «Ese correo ya tiene una cuenta» al registrarse | Use «Ingresar» con ese correo |
| Aviso de demasiados intentos (429) | Límite por IP: 10 inicios de sesión por minuto, 5 registros por hora, 10 reservas por minuto. Espere |
| Un cliente abre `/panel` o el personal abre `/mi-cuenta` | No corresponde: el cliente va a «Mi cuenta» y el personal al panel |
| «No tiene acceso a esta pantalla» | El rol no tiene ese permiso; el aviso dice cuál. Use otro rol (§7) |
| No llega el correo al buzón | ¿Dejó correo al reservar? (si no, la confirmación dice «Guardá este número»). `-Accion estado` debe mostrar el buzón «OK». Mire la cola en el panel (Integraciones › Correos de reservas) o en el escritorio (Administración › Correos): estado, intentos y el último error. Tope: 3 correos por destinatario cada 24 h |
| Con Gmail, los correos quedan «Pendiente» con un error de credenciales | La contraseña de aplicación está mal o la verificación en dos pasos no está activa: corrija `correo.txt` y vuelva a `subir -Correo real`. Mientras tanto se reintenta cada 5 minutos sin gastar intentos |
| Con Gmail y la base de prueba, llegan avisos de «no entregado» | Los correos en cola de la carga de prueba van a direcciones ficticias `.example`: es esperable. Pruebe con reservas propias |
| «Ver mi reserva» del correo no abre | El enlace del túnel cambió: `tools\docker_local.ps1 -Accion enlace` lo actualiza para los correos siguientes |
| No aparece «Vender en caja» | El rol no tiene caja (Gerencia, Consulta, Bodega), la reserva ya no está vigente o la sucursal activa no es la de la reserva (CM) |
| La caja no deja cobrar: «la caja está cerrada» | «Abrir caja» con la caja y el fondo inicial |
| Las facturas quedan fuera de línea o rechazadas después de recrear la base | El simulador tiene el estado de la carga anterior: `tools\docker_local.ps1 -Accion subir -Correo prueba -ReiniciarSimulador` |
| `npm run dev` con `tools\servidores_locales.ps1`: la tienda carga pero «Ingresar» no funciona | Ese camino no enciende la sesión web del servidor en la nube: use Docker (§5) |
| Facturas, series, RMA, armados, API de tienda… | Igual que en la V6 y la V4.2: [`inicio-rapido-v6.md`](inicio-rapido-v6.md) §9 y [`inicio-rapido-v4.2.md`](inicio-rapido-v4.2.md) §9 |

El registro de la tarea programada está en `%LOCALAPPDATA%\M-INV\docker-local.log`; el enlace vigente, en
`%LOCALAPPDATA%\M-INV\enlace-publico.txt`.

## 12. Volver a la V6

Las versiones anteriores siguen en sus ramas, sin cambios. Para volver: `tools\docker_local.ps1 -Accion bajar`,
`git switch Inventario-V6` y recree la base desde esa rama (`tools\bd_local.ps1 -Accion recrear`; en la V6 no respalda sola:
antes haga `-Accion respaldar` desde la V7 si quiere conservar lo probado). Después, `tools\docker_local.ps1 -Accion subir`
desde la V6.
