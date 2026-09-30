# Tienda y panel públicos en Docker · M-INV V7 (enlace para clientes y personal)

La plataforma web de la V7 (tienda con cuentas de cliente, carrito y reservas; panel del personal; correo de confirmación)
se publica desde este equipo con **Docker Desktop** y un **túnel de Cloudflare** (trycloudflare), igual que la tienda de la
V6 ([`tienda-publica-docker-v6.md`](tienda-publica-docker-v6.md)): un enlace `https://…trycloudflare.com` sirve la tienda
para los clientes y `…/panel` para el personal, sin abrir puertos del router ni contratar un servidor. Paso a paso completo
(base, usuarios, recorrido): [`inicio-rapido-v7.md`](inicio-rapido-v7.md).

```text
 Cliente o personal (internet) ──https──▶ Cloudflare ──túnel──▶ contenedor «tunel» (cloudflared)
                                                                   │ http://webcatalog:80 (red interna de Docker)
                                                                   ▼
                      contenedor «webcatalog» (nginx): la web (tienda + panel)
                         ├─ /storefront/   ──▶ contenedor «apigateway»  (tienda pública; despachador del correo) ──▶ «buzon» o Gmail
                         ├─ /api/v1/web/   ──▶ contenedor «cloudserver» (sesión web, registro de clientes, RPC del panel)
                         └─ otra /api/…    ──▶ 404
 Escritorio M-INV.exe ──http://localhost:5080──▶ «cloudserver» ──────────▶ PostgreSQL de este equipo (host.docker.internal:5432)
                                                  └─ «siat-simulador» (misma red, :5095)
```

## Servicios y puertos

Todos los puertos escuchan **solo en `127.0.0.1`** (este equipo); a internet sale únicamente lo que publica el túnel.

| Servicio (contenedor) | Imagen | Puerto en este equipo | Para qué |
|---|---|---|---|
| `cloudserver` | `minv-cloudserver:7.0.0-alpha.1` | `5080` | Servidor en la nube: el escritorio en modo «Nube» y, V7, la **sesión web y el panel** (`/api/v1/web/*`, `MINV_WEB_*`) |
| `apigateway` | `minv-apigateway:7.0.0-alpha.1` | `5090` | Tienda pública `/storefront/v1`, vencimiento de reservas y, V7, el **despachador del correo** (`MINV_MAIL_*`) |
| `siat-simulador` | `minv-siatsimulator:7.0.0-alpha.1` | `5095` | Simulador del SIN (perfil `siat-simulador`; datos de simulación, sin valor legal) |
| `webcatalog` | `minv-webcatalog:7.0.0-alpha.1` | `5173` | nginx con la web construida (tienda y panel) y el reenvío de sus dos API |
| `tunel` | `cloudflare/cloudflared` | — | Enlace público (perfil `publico`) hacia `webcatalog` |
| `buzon` | `axllent/mailpit` | `8025` | V7 · **Buzón de prueba** (perfil `correo-prueba`): recibe los correos sin enviarlos a nadie. SMTP interno `buzon:1025` (sin puerto publicado); interfaz en `http://127.0.0.1:8025`, **nunca** por el túnel |

Los contenedores se reinician solos (`restart: unless-stopped`) mientras Docker Desktop esté abierto. Llegan a la base
local por `host.docker.internal`, con el rol `minv_server` (sin BYPASSRLS).

## Qué publica el nginx del catálogo

`deploy/nginx.webcatalog.conf` es la única puerta hacia afuera (regla P-11):

| Ruta | Destino | Notas |
|---|---|---|
| `/` (y toda ruta de la página) | la web estática | Aplicación de una sola página: toda ruta sirve `index.html`, sin caché; `/assets/` con caché larga |
| `/storefront/` | API Gateway | Tienda pública: catálogo, imágenes, armados y reservas (límites por IP) |
| `/api/v1/web/` | Servidor en la nube | Sesión web por cookie, registro de clientes y RPC del panel. Cuerpo hasta 4 MB (una imagen de producto viaja dentro del RPC), 120 s de lectura, sin caché |
| cualquier otra `/api/…` | **404** | Las rutas del escritorio (`/api/v1/session/*`, `/api/v1/rpc`) viven solo en `127.0.0.1:5080` |

No se publican `/docs`, la API B2B `/v1` del gateway, las rutas del escritorio ni el buzón de prueba. La web se construye con
`VITE_API_URL=/` (mismo origen): un solo enlace sirve la web y sus dos API, sin CORS.

La **IP real** del visitante llega a los servidores en `X-Forwarded-For` (la de `Cf-Connecting-IP` que pone Cloudflare, o
la de la conexión en este equipo), y los servidores solo la aceptan desde la red interna de Docker
(`MINV_FORWARDED_HEADERS=true`): los límites por IP son **por visitante**. El esquema con que llegó (`X-Forwarded-Proto`,
https por el túnel) marca la cookie de sesión como `Secure`.

### Cabeceras de seguridad

nginx las agrega a toda respuesta (se declaran una sola vez, en el `server`):

| Cabecera | Valor |
|---|---|
| `Content-Security-Policy` | `default-src 'self'`; scripts, conexiones, formularios y `base-uri` solo del propio sitio; estilos del sitio (con `'unsafe-inline'`) y de `fonts.googleapis.com`; fuentes del sitio, de `fonts.gstatic.com` y `data:`; imágenes del sitio, `data:` y `blob:`; `object-src 'none'`; **`frame-ancestors 'none'`** |
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `Permissions-Policy` | cámara, micrófono, ubicación, pagos, USB e `interest-cohort` desactivados |
| `Cross-Origin-Opener-Policy` | `same-origin` |

La sesión, además, viaja en una cookie `HttpOnly`, `SameSite=Strict`, `Path=/api/v1/web`; toda petición que cambia algo exige
la cabecera `X-MINV-Client-Version` y rechaza otro origen (diseño: [`plataforma-web-v7.md`](../architecture/plataforma-web-v7.md) §3 y §9).

## El algoritmo

```text
 1. Requisitos: Docker Desktop encendido («Engine running») y la base local de la V7
      (powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear, si todavía no existe).
 2. Subir todo (la primera vez construye las imágenes: 5 a 10 minutos), con el buzón de prueba:
      powershell -ExecutionPolicy Bypass -File tools\docker_local.ps1 -Accion subir -Correo prueba
    (después de recrear la base agregue -ReiniciarSimulador: copia al contenedor el estado nuevo del simulador del SIN)
    Al final muestra «TIENDA PARA TUS CLIENTES: https://….trycloudflare.com» y «PANEL DEL PERSONAL: …/panel» y los guarda en
    %LOCALAPPDATA%\M-INV\enlace-publico.txt.
 3. Compartir el enlace con los clientes y el de /panel con el personal.
 4. El escritorio: M-INV.exe → «Nube» → http://localhost:5080 → empresa TECHZONE (o «Base local», igual que antes).
 5. Mantenerlo vivo: tools\docker_local.ps1 -Accion arranque (una vez) registra la tarea «M-INV Tienda publica».
 6. Ver el enlace vigente:  -Accion enlace      Estado:  -Accion estado      Apagar:  -Accion bajar
```

| Acción (`-Accion`) | Qué hace |
|---|---|
| `subir` | Detiene los servidores locales y lo que ocupe 5080/5090/5095/5173, apaga contenedores viejos de la V5, **escribe `deploy\.env`** (no se versiona) con el rol `minv_server` de la base local, las claves y el correo (sin mostrarlos), construye las imágenes, copia el estado del simulador si el volumen no tiene uno (o con `-ReiniciarSimulador`), levanta todo, espera, guarda el enlace y muestra el estado |
| `reanudar` | Lo que corre al iniciar sesión: enciende PostgreSQL, abre Docker Desktop si está cerrado y lo espera (hasta 10 min), levanta los contenedores **sin construir**, guarda el enlace nuevo y lo pone en los correos |
| `enlace` | Muestra el enlace vigente, lo guarda en `enlace-publico.txt` (con la dirección del panel) y actualiza «Ver mi reserva» de los correos |
| `estado` | Contenedores y salud: nube, gateway, simulador, tienda, panel, la tienda y la sesión web **a través de la web** (la sesión sin cookie debe responder 401), el buzón (si el correo es de prueba) y el enlace público |
| `arranque` | Registra la tarea programada «M-INV Tienda publica» (al iniciar sesión en Windows ejecuta `-Accion reanudar`); su registro queda en `%LOCALAPPDATA%\M-INV\docker-local.log` |
| `bajar` | Detiene y elimina los contenedores del proyecto `minv` (la base local, las imágenes y el volumen del simulador quedan) |

Otros parámetros: `-EmpresaTienda` (por defecto `TECHZONE`) y `-SucursalTienda` (`CM`) fijan la empresa y la sucursal de la
tienda y de la sesión web; `-PuertoBd` (5432); `-RemitentePrueba` y `-NombreRemitente` (remitente de los correos del buzón de
prueba).

## El correo: cuatro modos

`-Correo` decide cómo sale la confirmación de cada reserva. El despachador corre en el gateway, revisa la cola cada 15 s y
reintenta (1 min, 5 min, 30 min, 2 h; 5 intentos).

| `-Correo` | Qué pasa | Dónde se ven |
|---|---|---|
| `auto` (por defecto) | **real** si `correo.txt` trae `MINV_MAIL_PASSWORD`; si no, **prueba** | — |
| `prueba` | Todos los correos van al **buzón de prueba** (Mailpit), con remitente `zapasoftwarefastsolutions@gmail.com` («Tech Zone Gaming»). Nada sale a internet | `http://127.0.0.1:8025` |
| `real` | El servidor de `correo.txt` (Gmail: `smtp.gmail.com`, 587, STARTTLS y una **contraseña de aplicación** de la cuenta, con la verificación en dos pasos activa) | La casilla de cada cliente |
| `apagado` | No se envía nada: las confirmaciones quedan en la cola | Panel › Integraciones › Correos de reservas |

**`correo.txt`** vive en la carpeta de datos de M-INV (`%LOCALAPPDATA%\M-INV\correo.txt`; ver «Carpeta de datos»), **nunca**
en el repositorio. Líneas `CLAVE=valor`: `MINV_MAIL_HOST`, `MINV_MAIL_PORT` (587 si falta), `MINV_MAIL_STARTTLS` (`true` si
falta), `MINV_MAIL_USER`, `MINV_MAIL_PASSWORD`, `MINV_MAIL_FROM` y `MINV_MAIL_FROM_NAME`. El ejemplo completo para Gmail está
en [`inicio-rapido-v7.md`](inicio-rapido-v7.md) §9. `subir` copia esos valores a `deploy\.env` (tampoco se versiona); cambiar
`correo.txt` exige volver a `subir`.

Si la empresa tiene su propio correo **activo** (panel › Configuración › Correo de la empresa, o escritorio › Configuración ›
Facturación › Correo), el despachador usa ese servidor en lugar del de `correo.txt`. El enlace «Ver mi reserva» de cada correo
apunta a `MINV_PUBLIC_URL`, que `subir`, `enlace` y la tarea programada mantienen con el enlace vigente del túnel.

## Tarea programada

`tools\docker_local.ps1 -Accion arranque` registra **«M-INV Tienda publica»** para el usuario actual: al iniciar sesión en
Windows enciende PostgreSQL, abre Docker Desktop si está cerrado, levanta los contenedores (con el buzón si el correo es de
prueba), guarda el enlace nuevo en `enlace-publico.txt` y lo pone en los correos. La tarea ejecuta el script **de la carpeta
desde la que se registró**: si trabaja en otra carpeta del repositorio, vuelva a registrarla desde ahí. El equipo debe
quedar encendido y sin suspenderse (Configuración › Sistema › Energía: «Nunca» con corriente).

## Actualizar desde la V6

Si ya tenía la tienda de la V6 en Docker:

```powershell
# 1. Detener lo de la V6 (contenedores y servidores locales)
powershell -ExecutionPolicy Bypass -File tools\docker_local.ps1 -Accion bajar
powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion detener
# 2. Pasar a la V7
git switch Inventario-V7
# 3. Respaldar la base (copia en %LOCALAPPDATA%\M-INV\respaldos\)
powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion respaldar
```

4. **Migrar o recrear** la base:
   - **Recrear** (datos de prueba de la V7; respalda solo antes de borrar):
     `powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear`
   - **Migrar** una base con datos propios (no borra nada; guía `.claude/database-migration-guide.md` §11, con la consulta
     previa y la verificación):
     `dotnet run --project "src/4. Tools/MINV.Cli" -- migrate --conexion "<cadena del rol minv_owner de credenciales-bd-local.txt>"`
5. **Reconstruir las imágenes** 7.0.0-alpha.1 y levantar (`subir` reescribe `deploy\.env` con las variables nuevas de la V7:
   `MINV_WEB_*`, `MINV_MAIL_*`, `MINV_PUBLIC_URL`):

```powershell
powershell -ExecutionPolicy Bypass -File tools\docker_local.ps1 -Accion subir -Correo prueba -ReiniciarSimulador
```

6. Si la tarea programada se registró desde otra carpeta, vuelva a registrarla: `tools\docker_local.ps1 -Accion arranque`.

Una base de la V7 no vuelve a la V6 con la migración inversa si ya tiene actividad del canal web (la auditoría no se borra):
para volver, restaure el respaldo del paso 3 (`pg_restore`, la orden exacta la muestra `respaldar`).

## Carpeta de datos

La base local, las claves, `correo.txt`, `enlace-publico.txt` y los registros viven en `%LOCALAPPDATA%\M-INV`. Si Windows la
guardó en la carpeta privada de una aplicación empaquetada (`%LOCALAPPDATA%\Packages\<app>\LocalCache\Local\M-INV`), el
script usa esa; la variable `MINV_HOME` la fija a mano.

## El enlace cambia al reiniciar

El túnel rápido de trycloudflare no necesita cuenta, pero **su dirección cambia** cada vez que el contenedor `tunel` se
reinicia (reinicio del equipo, de Docker Desktop o `-Accion bajar`/`subir`). El enlace vigente siempre está en
`%LOCALAPPDATA%\M-INV\enlace-publico.txt` y con `-Accion enlace`. Para un enlace **fijo** hace falta un túnel con nombre de
una cuenta de Cloudflare con un dominio (el token va en `deploy/.env`, nunca en el repositorio), como explica la guía de la V6.

## Límites

- Es la base de datos **de este equipo**: si el equipo se apaga, la tienda y el panel dejan de responder.
- trycloudflare es para pruebas y demostraciones (sin garantía de disponibilidad); para operar de forma permanente conviene
  el túnel con nombre o el despliegue en la nube de `docs/deployment/despliegue-nube-v4.md` §14.
- El simulador del SIN es de **simulación**: las facturas no tienen valor legal. El buzón de prueba es solo para probar: en
  producción no se levanta.
- `tools\servidores_locales.ps1` (sin Docker) no enciende la sesión web del servidor en la nube: la tienda funciona, pero
  «Ingresar», «Mi cuenta» y el panel necesitan esta guía (o las variables `Minv__Web__*` puestas a mano).
