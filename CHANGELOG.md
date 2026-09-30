# Historial de cambios · M-INV

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/). Versionado semántico.

## [7.0.0-alpha.1 · Plataforma web] · 2026-09-29 · rama `Inventario-V7`

Tema: **todo el sistema en la web**. La tienda de la V6 gana **inicio de sesión y registro de clientes**, un **carrito de
compras** (se reserva cualquier producto, también uno solo, sin pasar por «Armá tu PC») y el **correo automático** con el
código y el detalle de cada reserva; el personal trabaja en un **panel web por rol** (`/panel`) que envía los MISMOS casos de
uso que el escritorio al servidor en la nube; el escritorio gana la pantalla **Reservas**, la cola de **Correos**, un inicio
simplificado y filtros con exportación en sus listas. Construida sobre `Inventario-V6`. **157 tablas en 10 esquemas** (4
nuevas). Reglas P-01 a P-14: `.claude/v7-web-platform-rules.md` · diseño: `docs/architecture/plataforma-web-v7.md` · plan y
avance: `docs/product/plan-v7.md` · normalización: `docs/database/normalizacion-v7.md` · tablas:
`docs/database/ERD-MINV-V3.md` §11 · paso a paso: `docs/deployment/inicio-rapido-v7.md` · Docker:
`docs/deployment/tienda-publica-docker-v7.md` · la web para todos: `docs/product/plataforma-web-v7.md` · escritorio:
`docs/product/escritorio-v7.md` · guía para todos: `GUIA-DE-INICIO.md` §8. Informes de cada paquete:
`docs/architecture/v7-notas/`.

### Agregado · tienda web: cuentas, carrito y reserva

- **Ingresar y registrarse** (`/ingresar`, `/registrarse`, `/cambiar-contrasena`): botón «Ingresar» en la cabecera (también en
  el teléfono), mensaje único ante cualquier falla («Correo o contraseña incorrectos»), aviso de cuenta bloqueada (código
  estable `auth.locked`), registro con nombre, correo, teléfono boliviano y contraseña (8 a 128 caracteres con letras y
  números). **Registrarse crea SIEMPRE una cuenta de Cliente**: el personal lo crea el Administrador. Después de ingresar, el
  personal va a `/panel` y el cliente a `/mi-cuenta` (o a la dirección interna de `volver`, validada contra redirecciones
  abiertas). Con credenciales incorrectas no se sale de la pantalla.
- **Mi cuenta** (`/mi-cuenta`, solo clientes): «Mis reservas» (filtro por estado, detalle, horas reales que se guarda cada
  una y «Liberar mi reserva»), «Mis datos» (nombre, teléfono y documento para la factura) y «Cambiar contraseña». El menú de la
  cabecera ofrece «Mi cuenta» y «Mis reservas» al cliente e «Ir al panel» al personal.
- **Carrito de compras**: «Agregar al carrito» y **«Reservar ahora»** en cada tarjeta, fila y ficha con disponibilidad
  (también consolas, juegos y portátiles); «Reservar ahora» lleva a `/reservar?sku=…&cantidad=…` con ESE solo artículo, sin
  tocar el carrito ni el armado. `/carrito` vuelve a consultar el catálogo, marca lo que se agotó, bajó o ya no está
  publicado y no deja reservar hasta ajustar. Topes: 16 unidades por producto y 20 productos (los de la reserva). El carrito es
  lo ÚNICO que la web guarda en el navegador (`minv.carrito`: SKU y cantidad), con sincronización entre pestañas.
- **`/reservar`** (carrito o artículo suelto): sin cuenta, nombre, teléfono o WhatsApp, correo («Te enviamos el código y el
  detalle de tu reserva»), **«¿Cuándo pasás a recogerlo?»** (24, 48 o 72 h, acotado por el catálogo) y la sección plegable
  **«Datos para tu factura (opcional)»** (CI, CEX, PAS, OD o NIT; complemento solo con CI; nombre o razón social); con
  cuenta de cliente, los datos salen de la cuenta y la reserva va por RPC (`CreateMyReservationCommand`). Idempotente por
  intento (misma llave al reintentar por red, otra si cambia algo). Sin stock suficiente (409 de la tienda o
  `storefront.insufficient_stock` por RPC): marca cada producto y ofrece «Ajustar a lo disponible» sin perder lo escrito. Confirmación con el número y «Copiar», tipo (Compra), hasta cuándo, dónde se retira, el
  detalle y «Te enviamos un correo a …» solo si el servidor lo encoló (`mailQueued`).
- «Mi reserva» (`/reserva`) acepta `RES-WEB-…` y `ARM-WEB-…`; las horas de la tienda salen del catálogo (`reservationHours`,
  `maxHoldDays`): la web ya no tiene una constante de 48 h. Las notas se unen en una línea antes de enviarse.
- **«Mi reserva» con el código O el celular** (regla S-06 actualizada): «Código de reserva» y «Número de celular» con «Complete
  al menos uno». Solo el código → `GET /storefront/v1/reservations/{number}` (el `phone` pasa a opcional) con el contacto
  enmascarado (`masked`, `maskedPhone` `•••••567`, `maskedEmail` `v•••@correo.example`, nombre con iniciales y sin notas);
  solo el celular → `GET /storefront/v1/reservations?phone=` (`GetStorefrontReservationsByPhoneQuery`: canal Web, últimos 90
  días, las 10 más nuevas, enmascaradas); los dos → la vista completa de siempre. Cancelar sigue exigiendo código Y teléfono
  (la web pide el celular si se buscó solo con el código). Límite propio de consultas por IP
  (`Minv:Storefront:LookupsPerMinute`, 20). El 404 no cambia.
- La tienda caída ya no bloquea el sitio: `/ingresar`, `/registrarse`, `/mi-cuenta` y `/panel` funcionan aunque el catálogo
  no cargue (`CatalogStateProvider` + `CatalogGate`).

### Agregado · servidor: sesión web, cuentas de cliente y carrito

- **Sesión web** en `MINV.CloudServer` (`Minv:Web`: `Enabled`, `TenantCode`, `BranchCode`, `RegistrationsPerHour`,
  `CookieName`): `POST /api/v1/web/session/login`, `GET /api/v1/web/session`, `POST /api/v1/web/session/logout`,
  `POST /api/v1/web/account/register` y `POST /api/v1/web/rpc` (mismo contrato que el RPC del escritorio). `WebSession` nunca
  lleva el token; `kind = customer` cuando el ÚNICO rol es `CLIENTE`. Apagada, las rutas no existen (404); encendida sin
  empresa, 503. La empresa sale siempre de la configuración, nunca de la petición.
- **Cuentas de cliente**: rol `CLIENTE` («Cliente web», permisos `account.manage` y `account.reserve`), tabla
  `sales.customer_accounts` (usuario ↔ cliente, 1 a 1) y casos de uso `MINV.Application.Accounts`:
  `RegisterCustomerAccountCommand` (previo a la sesión, no viaja por RPC; crea usuario, credencial, rol, sucursal de la
  tienda, cliente `WEB-000001` y la cuenta en UNA transacción), `GetMyAccountQuery`, `UpdateMyAccountCommand`,
  `GetMyReservationsQuery`, `CancelMyReservationCommand` y `CreateMyReservationCommand`, que operan SOLO sobre el cliente de la
  sesión (regla P-04).
- **Carrito = `PcBuild` de tipo `Cart`** (`PcBuildKind { Build, Cart }`): líneas sin ranura, sin ranura única y sin
  compatibilidad (solo en armados); un carrito no se publica. Numeración **`RES-WEB-000001`** (web) y **`RES-<sucursal>-000001`**
  (mostrador); los armados conservan `ARM-…`. **Datos para la factura** del comprador (`buyer_*`, mismas reglas del SIN que el
  cliente; enmascarados en la auditoría; la caja los precarga al cobrar). **Plazo para recoger** `holdDays` 1 a 3 (24, 48 o
  72 h); sin valor, `Minv:Storefront:ReservationHours` (48); tope `MaxReservationHours` (72). Nombre, notas y razón social
  rechazan caracteres de control. `ReserveCartCommand` (carrito de mostrador, `sales.pcbuild.manage`); `SellPcBuildCommand`
  vende también carritos (precio congelado, series al cobrar).
- Contrato público compatible (solo campos opcionales) de `POST /storefront/v1/reservations`: `kind` (`"build"` por defecto |
  `"cart"`), `holdDays` y `buyer`; la respuesta agrega `kind` y `mailQueued` y el catálogo `reservationHours` y `maxHoldDays`.
  Los eventos `pcbuild.*` ganan el campo `Kind`.

### Agregado · correo de la reserva

- Cada reserva con correo (tienda, cuenta de cliente, carrito de mostrador y reserva de una cotización) **encola** su
  confirmación en la MISMA transacción (`ReservationMail.EnqueueAsync`, regla P-06): `integration.outgoing_mails` (hecho,
  append-only, de sucursal), `integration.outgoing_mail_dispatch` (cola: `Pending`, `Sent`, `Exhausted`, `Cancelled`) e
  `integration.outgoing_mail_attempts` (bitácora de cada intento). No se guarda asunto ni cuerpo: se arman al enviar.
- **Despachador** `MailDispatcher` (en el API Gateway, `MailDispatcherService`, `Minv:Mail`): reclama la cola con
  `integration.claim_outgoing_mails` (SECURITY DEFINER, `FOR UPDATE SKIP LOCKED`), envía y reintenta (inmediato, 1 min, 5 min,
  30 min, 2 h; 5 intentos). Una falla del servidor de correo pospone 5 min sin gastar intento; sin servidor configurado, 15 min.
  Si la reserva ya no está reservada, el correo se cancela.
- El correo lleva **el código de la reserva y su detalle**: productos con cantidad, precio y subtotal, total («se paga al
  recoger»), hasta cuándo se guarda, dónde se recoge y el enlace «Ver mi reserva» (`Minv:Mail:PublicUrl`). Asunto «Reserva
  `<número>` · `<empresa>`», solo con datos del servidor; versión HTML y de texto; todo valor codificado; sin las notas del
  cliente. Topes: 3 correos por destinatario y 300 por empresa cada 24 h.
- Servidor de correo: el de la empresa (Configuración › Correo, si está activo) o el de `Minv:Mail`. Remitente de Tech Zone
  Gaming en Docker: **`zapasoftwarefastsolutions@gmail.com`** (remitente por defecto de `tools\docker_local.ps1` para el buzón
  de prueba; con Gmail real, el `MINV_MAIL_FROM` de `correo.txt`), por `smtp.gmail.com:587` con STARTTLS y una contraseña de
  aplicación que solo puede generar el dueño de la cuenta.
- `ResendReservationMailCommand` (reenviar, también a otro correo; cancela los pendientes de esa reserva) y
  `GetOutgoingMailsQuery` (la cola), con `sales.pcbuild.manage`. `SmtpMailSender`: tiempo máximo real, texto + HTML,
  `Message-ID` y fallas clasificadas con mensajes en español.

### Agregado · panel web del personal (`/panel`)

- **Esqueleto por rol**: menú lateral por secciones plegables (General, Ventas, Tecnología, Inventario, Compras, Sucursales,
  Facturación, Análisis, Administración) con **buscador de pantallas**, migas de pan, lista de **sucursal activa**
  (`SelectBranchCommand`) y el menú del usuario. Cada rol ve SOLO lo que sus permisos permiten; una pantalla sin permiso dice
  qué permiso falta. Se descarga aparte: quien solo visita la tienda no lo baja.
- **Inicio**: saludo, rol y sucursal activa; «¿Qué quiere hacer?» con **botones grandes** de cada módulo; **«Ver estadísticas
  ^» plegado**: al entrar no se ve ni se consulta nada, y cada estadística se carga por separado al abrirlo (regla P-10).
- **Registro de módulos**: cada módulo es `modules/<clave>/module.tsx` (`defineModule`) y se registra solo
  (`import.meta.glob`); la prueba de arquitectura impide que un módulo importe de otro o declare a mano tipos del servidor.
  Guía para escribir uno: `src/3. Presentation/MINV.WebCatalog/src/4-presentation/panel/README.md`.
- **Conjunto de componentes** (`kit/`, 36 componentes) y ayudas: tabla ordenable y paginada (tarjetas en el teléfono),
  **filtros con listas desplegables y `ComboBox` con búsqueda**, rango de fechas con atajos, filtros en la dirección
  (`useTableState`), detalle lateral, diálogos y confirmaciones, plegables «Ver …», estados de carga, vacío y error con
  «Reintentar», y **exportar CSV** (`exportCsv`: UTF-8 con BOM, «;», CRLF y celdas que parecen fórmulas neutralizadas).
- **Módulos**: Inicio; Ventas › Caja, Ventas, Clientes, Reservas; Tecnología › Armador de PC, Series, Garantías; Inventario ›
  Stock, Catálogo, Movimientos, Toma física, Alertas; Compras › Pedido sugerido, Órdenes de compra, Proveedores; Sucursales ›
  Sucursales, Transferencias; Facturación › Documentos fiscales, Estado del SIAT; Análisis › Reportes, Contabilidad;
  Administración › Usuarios, Integraciones (con la cola «Correos de reservas»), Configuración y Actividad. Detalle de
  botones, filtros y permisos: `docs/product/plataforma-web-v7.md`.
  <!-- V7-MODULOS: completar con los módulos que falten -->

### Agregado · contrato TypeScript generado

- `minv contrato-web [--salida <archivo>]` (sin base de datos) genera por reflexión sobre `RpcCatalog`
  `src/3. Presentation/MINV.WebCatalog/src/3-infrastructure/http/contract.generated.ts`: **193 operaciones y 408 tipos**, con
  `RpcOperations`, `RPC_META` (nombre completo, si es comando, permisos, módulos y si la puede usar un cliente), `WebSession`,
  el sobre del RPC y las listas `PERMISSIONS` y `ROLES`, con la serialización exacta de `RpcJson.Options` (regla P-07).
  `WebContractTests` falla si el archivo quedó desactualizado. La web lo usa solo a través de `contract.ts`.

### Agregado · seguridad web

- Token de sesión (`mses_…`, 256 bits; en la base solo su SHA-256) SOLO en una cookie **`HttpOnly`, `SameSite=Strict`,
  `Path=/api/v1/web`**, `Secure` cuando la petición llegó por https (`X-Forwarded-Proto` desde redes de confianza); vence a las
  12 h sin actividad. Nada de la sesión se guarda en el navegador.
- **Anti-CSRF**: toda ruta `/api/v1/web/*` que no es GET exige la cabecera `X-MINV-Client-Version` (misma versión mayor) y
  rechaza `Sec-Fetch-Site` de otro sitio y `Origin` de otro host; el servidor en la nube no publica CORS.
- **Límites**: bloqueo de la cuenta a los 5 intentos (15 min, `auth.locked`), 10 inicios de sesión por minuto y 5 registros por
  hora por IP real (`X-Forwarded-For` solo desde redes privadas conocidas), 10 reservas por minuto por IP en la tienda, topes
  del correo.
- Una sesión de cliente solo ejecuta los casos de uso `account.*`, `ChangePasswordCommand` y `LogoutCommand`, en las DOS rutas
  de RPC. `SelectBranchCommand` y `LogoutCommand` actúan siempre sobre la sesión de la cookie. Canal de auditoría `web`. La
  auditoría enmascara la respuesta de los comandos que devuelven contacto o datos de factura (`IAuditableResponse`; corrige
  también las reservas de la V6).
- **nginx del catálogo**: publica solo la web, `/storefront/` y `/api/v1/web/`; cualquier otra ruta `/api/` responde 404.
  Cabeceras `Content-Security-Policy` (`'self'`, más las fuentes de Google Fonts; `frame-ancestors 'none'`),
  `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy` y `Cross-Origin-Opener-Policy`.

### Agregado · base de datos

- Migración **`V7WebPlatform`**: 153 → **157 tablas en 10 esquemas** (`sales.customer_accounts`, `integration.outgoing_mails`,
  `integration.outgoing_mail_dispatch`, `integration.outgoing_mail_attempts`); columnas `kind` y `buyer_*` en `sales.pc_builds`;
  `sales.pc_build_lines.slot` admite nulo (solo carritos: trigger `trg_pc_build_line_slot`); el tipo no cambia
  (`trg_pc_build_kind_immutable`); canal `web` en la auditoría; función `integration.claim_outgoing_mails`; rol `CLIENTE` y
  permisos `account.*` en las empresas existentes (guardia: un rol `CLIENTE` previo que no sea de sistema detiene la
  migración); relleno `kind = 'Build'`; reversa que se niega si la auditoría ya tiene filas del canal `web`. Base migrada: 155
  `tenant_isolation`, 64 `branch_isolation`, 32 libros append-only, 6 funciones SECURITY DEFINER, 244 CHECK.
- **Comprobación de normalización**: `scripts/verificar_normalizacion.sql` (32 consultas de solo lectura: E01-E19 de
  estructura, D01-D10 de coherencia de los datos, I01-I03 informativas), informe `docs/database/normalizacion-v7.md` y prueba
  `NormalizationTests`. Se corrigieron 26 hallazgos dentro de `V7WebPlatform` (25 CHECK de estados y listas cerradas en tablas
  anteriores y el índice `ix_warranty_claims_tenant_id_serial_number_id`); quedan 7 detalles menores documentados con su riesgo.
- `minv verify` exige los mínimos de la V7 (157 tablas, 32 libros, 155 RLS, 64 por sucursal) y agrega la comprobación
  «Reservas» (lo reservado = la suma de las reservas activas de armados y carritos) y el resumen de la tienda web.

### Agregado · escritorio

- **Ventas › Reservas** (carritos `RES-…` y armados `ARM-…` reservados, de la web y del mostrador: vencimiento resaltado,
  estado del correo, «Vender en caja», «Liberar», «Reenviar correo», «Copiar teléfono» y «Nueva reserva en mostrador»),
  **Administración › Correos** (la cola de confirmaciones con «Reenviar»), **inicio simplificado** (botones por rol y los
  indicadores en secciones plegables cerradas), **filtros en listas desplegables con «Limpiar filtros» y «Exportar CSV»** en
  las listas de trabajo (servicio compartido `Services/CsvExport.cs`), **Usuarios** con el filtro Personal / Clientes web,
  la caja que precarga los datos de factura de la reserva, **Armador de PC › Cotizaciones** solo con armados (los carritos
  van a Reservas; el tablero Tecnología cuenta como cotizaciones solo los armados) y un aviso claro si una cuenta de cliente
  intenta entrar al escritorio. Arreglos de botones sin efecto, detalles que actuaban sobre la fila anterior, filtros que no
  filtraban, horas en UTC y textos técnicos. Guía y capturas 103 a 110: [`docs/product/escritorio-v7.md`](docs/product/escritorio-v7.md)
  · informe `docs/architecture/v7-notas/informe-D1-escritorio.md`.

### Agregado · Docker y datos de prueba

- Mismo stack de la V6 en Docker Desktop (imágenes **7.0.0-alpha.1**): el nginx del catálogo reenvía `/api/v1/web/` al servidor
  en la nube (sesión web y panel); `cloudserver` con `MINV_WEB_*` y `MINV_FORWARDED_HEADERS`; `apigateway` con el despachador de
  correo (`MINV_MAIL_*`, `MINV_PUBLIC_URL`). **Buzón de prueba Mailpit** (servicio `buzon`, perfil `correo-prueba`): recibe
  los correos sin enviarlos a nadie y los muestra en `http://127.0.0.1:8025`, solo en este equipo.
- `tools\docker_local.ps1 -Correo auto|prueba|real|apagado` (`auto`: real si `correo.txt` trae la contraseña, si no de prueba) y
  `correo.txt` en la carpeta de datos de M-INV (`MINV_MAIL_HOST`, `MINV_MAIL_PORT`, `MINV_MAIL_STARTTLS`, `MINV_MAIL_USER`,
  `MINV_MAIL_PASSWORD`, `MINV_MAIL_FROM`, `MINV_MAIL_FROM_NAME`; no se versiona). `enlace` y la tarea programada ponen el
  enlace vigente del túnel en «Ver mi reserva» de los correos; `estado` comprueba también el panel, la sesión web y el buzón.
  Guía: `docs/deployment/tienda-publica-docker-v7.md`.
- `tools\bd_local.ps1 -Accion respaldar` (copia `pg_dump` de la base `minv` en `%LOCALAPPDATA%\M-INV\respaldos`) y `recrear`
  **respalda antes de borrar** (si el respaldo falla, no borra nada).
- **Datos de prueba** (reglas P-13 y A-13, con los casos de uso): 2 cuentas de cliente registradas que reservaron desde su
  cuenta un carrito y un armado cada una, un carrito de la tienda de UN solo monitor vigente con datos para la factura, un
  carrito vencido, un carrito de mostrador y 9 correos de confirmación en la cola. `usuarios-prueba.txt` gana la sección
  **«Clientes de la tienda web»** (ingresan en la web, no en el escritorio).

### Documentación

- `docs/deployment/inicio-rapido-v7.md` (el algoritmo: base con respaldo, usuarios, Docker, el recorrido cliente → correo →
  panel → caja → «Vendida», cada rol, Gmail real, problemas frecuentes), `docs/deployment/tienda-publica-docker-v7.md`
  (servicios, qué publica nginx, cabeceras, modos de correo, tarea programada, actualizar desde la V6),
  `docs/product/plataforma-web-v7.md` (páginas de la tienda, carrito y reserva, «Mi cuenta», cada módulo del panel con sus
  botones y filtros, matriz de roles y permisos), `GUIA-DE-INICIO.md` §8, `docs/product/escritorio-v7.md`,
  `docs/database/normalizacion-v7.md`, `docs/database/ERD-MINV-V3.md` §11, `.claude/database-migration-guide.md` §11, la guía
  del panel y el `README.md` de la web (`src/3. Presentation/MINV.WebCatalog`), `CLAUDE.md` y este historial.

### Cambiado

- `Directory.Build.props`, imágenes de `deploy/docker-compose.yml` y `package.json` de la web: **7.0.0-alpha.1** (la web envía
  `X-MINV-Client-Version: 7.0.0`).
- `GetPcBuildsQuery` filtra por tipo (sin tipo devuelve armados y carritos); `PcBuildRow` trae el tipo y los datos para la
  factura (solo con `sales.pcbuild.manage`). La vigencia de la tienda se acota a `MaxReservationHours` (72).
- La tienda rechaza (400) nombre, notas y razón social con caracteres de control o saltos de línea en medio; la web ya los une.
- `AuthenticationFailedException` lleva un código opcional (`auth.locked` también en el inicio de sesión del escritorio).
- La web separa `CatalogProvider` en `CatalogStateProvider` y `CatalogGate`; los avisos de error del sitio muestran la
  descripción completa.

### Verificado

<!-- V7-PRUEBAS: resultados de la batería final -->

### Límites conocidos

- Sin pagos en línea ni envío a domicilio: la reserva se cobra en la tienda. Sin verificación del correo al registrarse ni
  recuperación de contraseña por correo (la restablece el Administrador).
- El enlace de trycloudflare cambia al reiniciar el túnel y depende de que el equipo esté encendido.
- La caja web imprime con el navegador: no maneja la impresora ESC/POS ni el cajón (eso sigue en el escritorio).
- Cambiar la contraseña no cierra las otras sesiones abiertas; una cuenta de cliente desactivada puede iniciar sesión, pero sus
  casos de uso responden `account.inactive`; cambiar `Minv:Web:BranchCode` no mueve las cuentas ya creadas.
- El vencimiento automático corre en el gateway y solo ve la sucursal de la tienda: un carrito de mostrador de otra sucursal no
  vence solo. Las reservas hechas en modo «Base local» encolan su correo en esa base, que solo sale si un gateway con
  `Minv:Mail` apunta a ella.
- La sesión web no informa los módulos comerciales activos (`licenseModules` del panel es informativo: el servidor rechaza y el
  aviso lo explica). Algunas listas del panel filtran en la página porque la consulta del servidor solo recibe fechas o un tope.

## [6.0.0-alpha.1 · Tienda web conectada] · 2026-09-27 · rama `Inventario-V6`

Tema: el catálogo web de la V5 deja el mock y se conecta a **la misma base de datos en la nube que el escritorio** (Tech
Zone Gaming, TECHZONE) a través de una **API pública de tienda** en el API Gateway (`/storefront/v1`, sin API Key). Lo que
se vende en el escritorio deja de estar disponible en la web; lo que se **reserva** desde el armador de la web (o desde el
escritorio) queda **EN RESERVA** para ambos hasta que se vende en caja, se libera o vence. Construida sobre `Inventario-V5`.
**153 tablas en 10 esquemas** (1 nueva). Reglas S-01 a S-10: `.claude/v6-storefront-rules.md` · diseño:
`docs/architecture/tienda-web-conectada-v6.md` · contrato de la API: `docs/integration/storefront-api-v1.md` · tablas:
`docs/database/ERD-MINV-V3.md` §10 · paso a paso: `docs/deployment/inicio-rapido-v6.md` · para todos: `GUIA-DE-INICIO.md` §7.
Estado: las **fases A** (dominio, aplicación, migración, API pública, datos de prueba, pruebas) **y B** (pantallas de la web
y del escritorio) están fusionadas en la rama, y la **fase C** las verificó de punta a punta sobre una base temporal (ver
«Verificado» más abajo).

### Agregado (fase A · backend)

- **Dominio**: `PcBuild` con canal (`Desktop`/`Web`), contacto (nombre y teléfono boliviano obligatorios en la web, correo y
  notas), estado `Reserved` (`Quoted → Reserved → Sold | Cancelled`), `Reserve`, `ReleaseReservation` (con motivo; «Vencida» al
  vencer), `MarkSold` desde reservado, `Publish`/`Unpublish` (armados sugeridos de la web), bitácora append-only
  `PcBuildEvent` (`sales.pc_build_events`) de cada cambio y eventos de dominio `pcbuild.reserved`, `pcbuild.released`,
  `pcbuild.sold` al outbox (webhooks). `StockReservation` con el tercer origen `PcBuildLineId` (arco caja | pedido | armado) y
  `StockLevel.Fulfill` (vender un armado reservado consume la reserva: nunca se descuenta dos veces). Rol `TIENDA_WEB` y permisos
  `storefront.read` / `storefront.reserve` (también Administrador y Gerencia).
- **Aplicación** (`MINV.Application/Storefront`): `GetStorefrontCatalogQuery` (instantánea con la forma del mock de la V5:
  categorías con ícono y conteo, marcas, productos con slug, ficha, precio, disponibilidad = existencias − reservado en la
  sucursal de la tienda, popularidad por ventas de 90 días, etiquetas, descripción generada, armados publicados),
  `GetStorefrontProductQuery`, `GetStorefrontProductImageQuery`, `GetStorefrontPresetsQuery`, `GetStorefrontReservationQuery`
  (número + teléfono), `CreateStorefrontReservationCommand` (todo o nada, reintento optimista, `ARM-WEB-000001`, idempotente por
  `Idempotency-Key` en `processed_requests`, 409 con el detalle de lo que falta), `CancelStorefrontReservationCommand`,
  `ExpirePcBuildReservationsCommand` (sistema) y, para el escritorio, `ReservePcBuildCommand`, `ReleasePcBuildReservationCommand`,
  `PublishPcBuildCommand`; `SellPcBuildCommand` vende armados reservados consumiendo la reserva; `CancelPcBuildCommand` libera;
  `PcBuildRow`/`PcBuildDetail` con canal, contacto (teléfono y correo solo con `sales.pcbuild.manage`), reserva, publicación y
  bitácora; tablero Tecnología con «reservas web activas» (cantidad y Bs).
- **API Gateway**: esquema de autenticación `Storefront` (principal técnico `tienda-web` de `Minv:Storefront:TenantCode`, sucursal
  `BranchCode`), grupo `/storefront/v1` con 7 rutas, límites por IP (300 lecturas/min, 10 reservas/min), CORS para
  `AllowedOrigins`, caché HTTP de la imagen (ETag, 1 h) y de la instantánea (30 s), OpenAPI «Tienda web» y
  `StorefrontReservationExpiryService` (cada 5 min cierra las reservas vencidas).
- **Infraestructura**: migración `V6Storefront` (+ `.Sql.cs`: relleno del canal y de la bitácora, RLS y append-only de
  `pc_build_events`, permisos, rol y usuario técnico por empresa, privilegios), `TenantProvisioner` crea el usuario técnico,
  `scripts/db_init.sql` regenerado.
- **Datos de prueba**: los 6 armados sugeridos del catálogo se publican en la web (la tienda muestra los de su sucursal: 3 en
  CM); dos reservas web (una vencida y liberada por el trabajo de vencimiento, otra activa de hace unas horas) con contactos
  ficticios `.example`; `usuarios-prueba.txt` menciona el usuario técnico (sin contraseña utilizable).
- **Scripts**: `tools/servidores_locales.ps1` pasa la empresa y la sucursal de la tienda al gateway y muestra
  `http://localhost:5090/storefront/v1/catalog`; `deploy/docker-compose.yml` con las variables `MINV_STOREFRONT_*` y
  `webcatalog` construido con `VITE_API_URL` (`deploy/Dockerfile.webcatalog`).
- **Pruebas**: dominio (reservas, contacto, publicación, bitácora, arco), infraestructura en memoria (flujo completo
  reservar → stock reservado → vender consume → liberar/vencer devuelve, idempotencia, insuficiente, permisos) y PostgreSQL
  (usuario técnico, CHECK y arco, append-only, RLS por sucursal), integración del gateway (catálogo, producto, imagen, CORS,
  reservar, 409, consultar, cancelar, vencimiento, venta en caja, límites por IP).

### Agregado (fase B · web)

- Puertos `ICatalogSource` e `IReservationGateway` en `1-domain/ports`; tipos del contrato en `2-application/storefront`;
  `3-infrastructure/http` (`HttpCatalogSource`, `HttpReservationGateway`, `api.ts`): el único lugar con `fetch`
  (`src/architecture.test.ts`), base `VITE_API_URL` (por defecto `http://localhost:5090`; `VITE_API_URL=mock` usa los
  `*.data.ts` de la V5); `InMemoryCatalogRepository` hidratado con la instantánea (imágenes absolutas hacia la API).
- `CatalogProvider` con estados cargando (skeleton y logotipo), error («Reintentar») y listo; refresco al volver a la pestaña
  y cada 60 s; la ficha consulta `GET /products/{slug}` al abrirse. Disponibilidad `disponible (n)`, `ultimas`, `reservado`,
  `agotado` en tarjetas, filas, ficha, candidatos del armador y resumen; «Agregar al armado» solo con disponible; cantidad
  máxima = disponible.
- «Finalizar armado» → **«Reservar armado»**: formulario accesible (nombre, teléfono/WhatsApp con validación boliviana, correo
  opcional, notas), aviso de vigencia, envío con `Idempotency-Key`, errores del contrato (409 marca las piezas y cuánto hay),
  éxito con número, vencimiento, líneas y total, enlaces «Consultar mi reserva» y WhatsApp; página `/reserva/:numero`
  (teléfono → estado; «Liberar mi reserva»). Armados sugeridos desde la API. Textos: la reserva se guarda en la tienda y se
  confirma en persona. `.env.example`, `npm run build` con `.env.production`; `deploy/Dockerfile.webcatalog` con `ARG VITE_API_URL`.
- Pruebas (vitest): mapeo DTO → dominio, fuentes HTTP con `fetch` simulado (éxito, 409, 429, red caída), `CatalogProvider`,
  formulario de reserva, página de consulta; las de la V5 siguen con `VITE_API_URL=mock`.

### Agregado (fase B · escritorio)

- **Armador de PC › Cotizaciones**: columnas **Canal** (insignia «Web» con ícono), **Contacto** (nombre y teléfono; correo en el
  detalle; solo con `sales.pcbuild.manage`, regla S-06), **Reservado hasta** (resaltado en ámbar si vence en menos de 6 h y en
  rojo si ya venció) y **Web** (publicado); estado **Reservados**, filtro rápido **Reservas web** y tarjeta **Reservas web
  activas** (cantidad, Bs y cuántas vencen pronto). Acciones según el estado y el permiso: **Reservar stock** (pide las horas,
  48 por defecto; todo o nada), **Liberar reserva** (pide el motivo), **Vender en caja** (avisa que la venta consume la
  reserva), **Publicar en la web** / **Quitar de la web**. Detalle de una reserva web: vigencia, contacto con **Copiar
  teléfono**, notas del cliente y cada pieza con su disponibilidad en la sucursal.
- **Stock, catálogo y caja**: «Reservado: n» y disponible = existencias − reservado (chip «Con reservas» y columna
  **Reservado** en Stock, con la exportación a Excel; «n UND · reservado m» en el catálogo); la caja muestra «Disponible n
  (reservado m)», marca la línea que supera lo disponible con lo reservado y avisa **Stock insuficiente** antes de cobrar; carga
  también los armados **reservados** (web o escritorio) y la venta los cobra consumiendo la reserva (regla S-04).
- **Inicio › Tecnología**: tarjeta **Reservas web activas** (cantidad y Bs) y enlace «Reservas web» que abren el armador con el
  filtro puesto.
- **Capturas y guía**: `docs/product/escritorio-v6.md` con las capturas 99 a 102 (`docs/product/capturas/v6`) que genera
  `M-INV.exe --capturas` (`tools/build_v3.ps1 -Capturas`).
- **Pruebas** (`tests/MINV.DesktopClient.Tests/StorefrontScreenTests`): la reserva web de la demostración en la lista, el
  detalle y el tablero; reservar, publicar y liberar una cotización propia desde el ViewModel; la caja vende la reserva web
  consumiéndola; stock, catálogo y caja con lo reservado y la caja que no cobra más que lo disponible.
- **Aplicación** (mínimo, fuera del alcance del escritorio): `GetStockReservationsQuery` (`inventory.stock.view`): unidades
  reservadas por SKU en el almacén de trabajo, para «Reservado: n» en stock, catálogo y caja.

### Documentación

- `docs/deployment/inicio-rapido-v6.md` (el algoritmo: base, escritorio, servidores, web y el recorrido web → escritorio →
  web; qué no hace; problemas frecuentes), `GUIA-DE-INICIO.md` (ediciones 6 «Catálogo Web» y 7 «Tienda web conectada»,
  cuenta técnica «Tienda web», usuarios y problemas), `docs/product/catalogo-web-v5.md` (el catálogo web de la V5 y su
  conexión en la V6), `docs/architecture/tienda-web-conectada-v6.md` ajustado al código (configuración, principal técnico,
  idempotencia en `iam.processed_requests`, `Expired`, casos de uso del escritorio, límites, pruebas),
  `docs/deployment/despliegue-nube-v4.md` §14 (gateway con la tienda, `webcatalog`, CORS, TLS, variables),
  `deploy/.env.example` (variables `MINV_STOREFRONT_*` y `MINV_WEB_API_URL` documentadas), `README.md`, `CLAUDE.md` y este
  historial. Quién ve las cotizaciones en el escritorio: los roles con `sales.view` (Bodega y Consulta no; ven el stock con
  lo reservado).

### Verificado (fase C · punta a punta)

- Sobre una base temporal (`minv migrate` + `minv datos-prueba` de 60 días con facturación + `minv verify`), con el simulador
  del SIN, el servidor en la nube y el gateway en puertos propios y la web de Vite contra ese gateway: la web carga el catálogo
  real (159 productos, imágenes desde la API); reservar un armado sugerido desde la web crea `ARM-WEB-…` y cada pieza queda
  con `reserved` +1 y `available` −1 (disponible = existencias − reservado); el escritorio (RPC del servidor en la nube, como
  un cliente de escritorio) la lista con `GetPcBuildsQuery(Reserved, Web)` con canal Web, contacto y «reservado hasta», y la
  ficha del producto y `GetStockReservationsQuery` muestran lo reservado; `SellPcBuildCommand` desde una caja abierta factura
  (`F-CM-…`) y **consume** la reserva (existencias −1, reservado −1, disponible igual: una sola salida); la web muestra la
  pieza como «Última unidad» / «Agotado» y «Mi reserva» como **Vendida**; `ReservePcBuildCommand` sobre una cotización del
  escritorio deja la pieza en «Reservado» en la web y `ReleasePcBuildReservationCommand` la devuelve; reservar más de lo
  disponible responde `409 storefront.insufficient_stock` con `shortages[]` sin reservar nada (en la web marca la pieza y
  ofrece «Ajustar a lo disponible»); una reserva vencida se muestra `Expired` sin cerrarse al leer y el trabajo del gateway
  la cierra («Vencida», stock devuelto, fila `Expired` en la bitácora); repetir el `POST` con la misma `Idempotency-Key`
  devuelve la misma reserva con `Idempotent-Replayed: true` (y `422 idempotency` con otro contenido); CORS solo para el
  origen configurado y `429` al superar 10 reservas o 300 lecturas por minuto por IP.

### Agregado (tienda pública en Docker)

- `tools/docker_local.ps1` (`subir`, `reanudar`, `enlace`, `estado`, `arranque`, `bajar`): levanta la V6 en **Docker Desktop**
  de este equipo sobre el PostgreSQL local (`host.docker.internal`), con el estado del simulador del SIN copiado al volumen,
  y la publica con un **túnel rápido de Cloudflare** (perfil `publico`, servicio `tunel`): un enlace
  `https://….trycloudflare.com` para todo público que se guarda en `%LOCALAPPDATA%\M-INV\enlace-publico.txt`. La tarea
  programada «M-INV Tienda publica» (al iniciar sesión) enciende la base, abre Docker Desktop, levanta los contenedores
  (`restart: unless-stopped`) y guarda el enlace nuevo. Guía: `docs/deployment/tienda-publica-docker-v6.md`.
- `deploy/nginx.webcatalog.conf`: el nginx del catálogo sirve la SPA y reenvía `/storefront/` al gateway (un solo origen,
  sin CORS); solo la tienda sale a internet (ni `/docs`, ni la API B2B, ni el servidor del escritorio). Pasa la IP real del
  cliente (`Cf-Connecting-IP`) en `X-Forwarded-For`.
- API Gateway: `Minv:ForwardedHeaders` (apagado por defecto; `true` en Docker) acepta `X-Forwarded-For` solo desde redes
  privadas conocidas, para que el límite por IP de la tienda sea por cliente y no uno compartido por todos.
- La web admite `VITE_API_URL=/` (mismo origen, rutas relativas `/storefront/v1/…`); es el valor por defecto de la imagen.

### Corregido (tienda pública en Docker)

- `createSources` resolvía dos veces la URL de la API: con `VITE_API_URL=/` la base quedaba vacía y el constructor la volvía
  a `http://localhost:5090`, así que la tienda publicada mostraba «No pudimos cargar el catálogo». Ahora se resuelve una vez.
- El healthcheck de `Dockerfile.cloudserver` ejecutaba `dotnet MINV.CloudServer.dll --version`, que intentaba levantar un
  segundo servidor y dejaba el contenedor «unhealthy»; ahora consulta `GET /api/v1/health`.
- `deploy/docker-compose.yml` exigía `POSTGRES_PASSWORD` aun sin el perfil `local-db`.

### Cambiado

- `Directory.Build.props`, imágenes de `deploy/docker-compose.yml` y `package.json` de la web: **6.0.0-alpha.1**.
- `SellPcBuildCommand` vende también armados **reservados** (consume la reserva); `CancelPcBuildCommand` libera la reserva de
  un armado reservado; `GetPcBuildsQuery` filtra por canal; `PcBuildRow`, `PcBuildDetail` y `TechDashboardView` traen los
  campos de la V6 (el teléfono y el correo solo con `sales.pcbuild.manage`).
- `inventory.stock_reservations`: el arco de origen pasa de dos a tres (`pos_session_id`, `sales_order_line_id`,
  `pc_build_line_id`); `iam.audit_logs.channel` admite `storefront`; ADMIN y GERENCIA reciben `storefront.read` y
  `storefront.reserve`.
- API Gateway: el limitador global por IP separa el presupuesto de `/storefront/v1` (300/min) del resto (120/min); OpenAPI
  describe la tienda web; `tools/servidores_locales.ps1` recibe `-EmpresaTienda`, `-SucursalTienda` y `-OrigenTienda`.
- `docs/database/ERD-MINV-V3.md` §10, `docs/integration/api-gateway-v1.md` §7.2 (eventos `pcbuild.*`) y las pruebas de
  PostgreSQL (153 tablas, 151/63 políticas, 30 libros).

### Límites conocidos

- Sin pagos en línea ni cuentas de cliente: la reserva se confirma y cobra en la tienda; se consulta o libera con el número y
  el teléfono.
- La reserva es por cantidad (las series se eligen al vender en caja) y muestra el stock de **una** sucursal (la configurada).
- La compatibilidad de las piezas se informa (`hasCompatibilityWarnings`), no bloquea.
- Una reserva vencida se cierra en la pasada siguiente del trabajo del gateway (cada 5 min); hasta entonces la API la muestra
  `Expired` y el stock sigue reservado.
- La instantánea se calcula en cada petición (caché HTTP de 30 s): con catálogos muy grandes convendría materializarla.

## [5.0.0-alpha.1 · Catálogo Web] · 2026-09-27 · rama `Inventario-V5`

Tema: **Catálogo Web M-INV (MINV.WebCatalog)**. El catálogo web ahora funciona de punta a punta, con un diseño renovado y responsivo, sin errores de consola.

### Agregado

- **Página de Inicio**: Hero con titular en dos tonos, tres productos protagonistas flotando en losetas con brillo, cifras en vivo (ej. 159 productos, 40 marcas, 6 armados), cinta de confianza, carrusel de campañas, y secciones dedicadas a categorías, destacados, ofertas, novedades, PC armadas, consolas, marcas y boletín.
- **Catálogo Principal**: Navegación con migas de pan, chips de categorías con conteos en tiempo real, panel de filtros a la izquierda (en móvil bajo un botón «Filtros»), buscador interno del catálogo, opciones de orden, selector de vista (grilla o lista) y paginación (12 por página).
- **Ficha de Producto**: Ilustración grande del producto, insignias dinámicas (oferta, destacado, serie/IMEI), precio con formato «Antes» tachado y monto de ahorro, indicador de stock y garantía, recomendaciones de compatibilidad («Va en: Tarjeta de video»), y botones de acción rápida («Agregar al armado» y «Ver mi armado»).
- **Correcciones de Código**: Resolución de errores de TypeScript (`tsc`), alineación de importaciones del dominio y aplicación (ej. `getCategoryFilterTree`), limpieza de componentes obsoletos (`RefButton`) y estabilización de la suite de pruebas unitarias con Vitest (todas en verde).


## [4.2.0-alpha.1 · edición Tecnología] · 2026-09-26 · rama `Inventario-V4.2`

Tema: M-INV **exclusivo para tiendas de tecnología y gaming** (componentes de PC, computadoras, monitores, periféricos,
consolas PS4 y PS5, Xbox Series X y Series S, Nintendo Switch y Switch 2, videojuegos, accesorios, redes, cables y
software), **con datos y no con código a medida**: fichas técnicas, plataformas y reglas de compatibilidad son
especificaciones por categoría; el motor (CQRS y append-only, multi-sucursal, nube, API y facturación SIAT) es el de la
V4.1. Cada unidad serializada es un hecho trazable de punta a punta: entra, viaja, se vende (la factura del SIN lleva su
serie o IMEI) y vuelve por garantía con su serie. Construida sobre `Inventario-V4.1`. **152 tablas en 10 esquemas** (12
nuevas; esquema `service`). Diseño: `docs/architecture/edicion-tecnologia-v4.2.md` · reglas T-01 a T-10:
`.claude/v42-tech-rules.md` · paso a paso: `docs/deployment/inicio-rapido-v4.2.md` · interfaz:
`docs/product/escritorio-v4.2.md` · para todos: `GUIA-DE-INICIO.md` §5.

### Agregado

- **Dominio** (`MINV.Domain`):
  - fichas técnicas (`Catalog/TechCatalog.cs`): `SpecDefinition` por categoría (texto, número con unidad u opción;
    multivalor, filtrable, obligatoria, clave de compatibilidad), `SpecOption`, `ProductSpecValue` (arco exclusivo
    número/texto/opción), `ProductTechProfile` (serie o IMEI y meses de garantía) y las 17 claves de `CompatibilityKeys`;
  - series (`Inventory/SerialNumber.cs`, `SerialTracking.cs`): la serie es de una variante, estados nuevos `InTransit`,
    `InRma` y `ReturnedToSupplier`, cambios SOLO por métodos de negocio (recibir, vender, reponer, devolver, RMA,
    transferir, reingresar, devolver al proveedor, dar de baja) con su fila de bitácora (15 acciones) e IMEI de 15 dígitos
    con dígito de Luhn; códigos estables `serial.*`;
  - garantías (`Service/WarrantyClaim.cs`): caso RMA con tabla de transiciones (recibido → diagnóstico → proveedor →
    reparado, reemplazado o rechazado → entregado), reparación con cargo fuera de garantía y bitácora del caso;
  - armador (`Catalog/PcCompatibility.cs`, `Sales/PcBuild.cs`): 12 ranuras, 12 reglas (socket, tipo y ranuras de RAM,
    capacidad, M.2, formato del gabinete, largo de la GPU, socket del enfriador, potencia de la fuente y recomendada,
    gráficos, piezas faltantes), consumo estimado (TDP + GPU + 75 W) y fuente recomendada (× 1,3); armado Borrador →
    Cotizado (precios y vigencia congelados) → Vendido o Anulado, con la confirmación explícita de los errores;
  - 6 permisos (`catalog.specs.manage`, `inventory.serials.view`, `inventory.serials.manage`, `service.rma.open`,
    `service.rma.manage`, `sales.pcbuild.manage`) y su matriz por rol, tipo de movimiento **REPOSICIÓN POR GARANTÍA**
    (`REPOSICION_GARANTIA`) y cuenta **5.1.10 Costo de garantías**.
- **Casos de uso** (`MINV.Application/Tech`, contratos en `TechContracts.cs`): especificaciones con herencia y ficha
  técnica completa, búsqueda y facetas por especificación y plataforma, consultas de series (disponibles, búsqueda,
  trazabilidad, garantía derivada), registrar series de unidades en stock, dar destino (proveedor o baja), casos RMA
  (abrir, avanzar, notas, reposición con otra unidad con su movimiento y su asiento 5.1.10 / 1.1.05), armador (revisión,
  candidatos con los errores nuevos de cada pieza, guardar y cotizar, anular, vender la cotización en la caja en la misma
  transacción) y `GetTechDashboardQuery`.
- **Series en las operaciones de siempre**: venta de caja, pedidos del API y facturas CAFC (`SaleLineInput.Serials`),
  anulación, devoluciones (`ReturnLineInput.Serials`) y **devolución por falla** (`CreateSalesReturnCommand(…, Defective)`:
  reembolso sin reingreso, la unidad queda en garantía), transferencias (despacho en tránsito y faltantes con las series
  que no llegaron), recepción de compras (`ReceivePurchaseOrderCommand(…, Serials)`), saldo inicial, ajustes y devolución
  a proveedor (`RegisterMovementCommand.Serials`); `GetSaleLinesQuery` devuelve las series de cada línea; la toma física
  rechaza la diferencia de un serializado (`serial.count_adjustment`).
- **Factura del SIN** (T-03): `numeroSerie` o `numeroImei` por línea (series unidas con «, »); si pasan de 1500
  caracteres la línea se divide por unidades con el mismo precio y el descuento prorrateado (el total fiscal no cambia).
  Actividades 4741100, 4741200 y 4742100 con los productos SIN del rubro.
- **Impresión**: ticket, rollo fiscal y PDF de la factura con `S/N:` o `IMEI:` y «Garantía hasta dd/mm/aaaa» (fecha de la
  venta + meses, derivada al imprimir); proforma del armado (texto de ticket, rollo y PDF) con la garantía de cada pieza y
  «Documento sin valor fiscal».
- **Base de datos**: migración `V42TechRetail` (guardia, relleno de `serial_numbers.variant_id` con verificación, RLS por
  empresa en las 12 tablas nuevas y RESTRICTIVA por sucursal en 7, append-only en 5 bitácoras, trigger
  `catalog.minv_spec_value_matches`, vista `inventory.v_serial_breaches`, datos V4.2 de las empresas existentes y
  privilegios de `minv_app` y `minv_server`); `scripts/db_init.sql` regenerado; `minv verify` controla también las series
  en stock; ERD §9 y guía de migraciones §9.
- **API Gateway**: `GET /v1/products/{sku}/specs` (alcance `catalog:read`) y `serials` por línea en `POST /v1/orders`.
- **Escritorio**: sección **Tecnología** del menú con **Armador de PC** (ranuras, candidatos compatibles e incompatibles
  atenuados con el motivo, revisión en vivo, consumo y fuente recomendada, cotizaciones, proforma y «Vender en caja»),
  **Series e IMEI** (búsqueda, garantía derivada, línea de tiempo de cada unidad, registrar series de stock, dar destino,
  abrir RMA) y **Garantías y RMA** (tarjetas, chips por estado, bitácora y acciones según el estado, reemplazo con otra
  unidad); catálogo con la pestaña **Ficha técnica**, insignias Serie / IMEI / Garantía, chips de plataforma, facetas y
  administración de **Especificaciones** y subcategorías; punto de venta con chips de categoría y plataforma, elección de
  la unidad al agregar un equipo y **Desde armado**; formulario de series (escáner en modo teclado, lista pegada,
  contador, IMEI inválido y repetidas en vivo) en compras, movimientos, transferencias y devoluciones; sección
  **Tecnología** del tablero (ventas por categoría y plataforma, tarjetas de video y consolas más vendidas, RMA y armados)
  y guías de ayuda de la edición.
- **Tema gaming** (T-08): paletas `Palette.Dark.xaml` (predeterminada) y `Palette.Light.xaml` con las mismas claves y
  contraste WCAG AA, degradado de marca violeta → cian (botón principal, menú activo, líneas de acento, progreso), foco
  del teclado con anillo neón, `KpiCardButton` con acento, insignias `Badge` y `PlatformBadge`, títulos y cifras en
  Bahnschrift, ícono y logotipo propios (`tools/tecnologia/generar_icono_gaming.py`) y textos de marca en
  `Services/Brand.cs`. Guía: `docs/product/ux-ui-guidelines.md` §13.
- **Empresa de prueba Tech Zone Gaming S.R.L.** (`TECHZONE`, sucursales CM La Paz, CB Cochabamba y SC Santa Cruz, NIT de
  simulación 1023456029) desde `Seeding/Tecnologia/catalogo-tecnologia.json` embebido (generado y validado con
  `tools/tecnologia/generar_catalogo.py`): 35 categorías, 176 especificaciones, 40 marcas, 159 productos con ficha técnica,
  producto SIN e ilustración propia sin logotipos (`tools/tecnologia/generar_imagenes_tecnologia.py`), 8 proveedores, 30
  clientes y 8 armados. La base de prueba (60 días, unos 122 s de carga) deja unas 2.722 series e IMEI, 7 casos RMA en
  todos sus estados, los 8 armados (dos cobrados en la caja, uno incompatible en borrador y otro cotizado con errores
  confirmados), una devolución por falla, transferencias con faltantes, tomas físicas y unos 518 documentos fiscales con
  los escenarios de contingencia de la V4.1.
- **Pruebas**: dominio (IMEI, estados de la serie, RMA, armado, compatibilidad con los armados del catálogo), aplicación
  de punta a punta en memoria (ciclo de vida de una serie, rechazos, transferencias, armador, fichas y facetas, división
  de la línea fiscal, devolución por falla), PostgreSQL con `MINV_TEST_PG` (RLS, append-only, arco de las fichas, unicidad
  de series, `v_serial_breaches` vacía, relleno y guardia de la migración, ciclo de vida, transferencia con series,
  armador vendido, RLS con la sucursal Cochabamba), escritorio (páginas nuevas, caja con IMEI y factura válida, armador
  cotizado y vendido, RMA con reposición, catálogo técnico, series en movimientos, compras y transferencias, devolución
  por falla, tema y alcance de los recursos), RPC de la edición en el servidor en la nube y `ModelTests` de las tablas
  nuevas.
- **Documentación**: diseño, reglas T-01 a T-10, inicio rápido de la V4.2, guía de la interfaz, sección 5 de la
  `GUIA-DE-INICIO.md`, ERD §9, guía de migraciones §9, API (`/v1/products/{sku}/specs` y ejemplos con productos y
  sucursales de Tech Zone Gaming), despliegue en la nube (152 tablas) y guía UX/UI (§13); las 83 capturas de la
  demostración en `docs/product/capturas/v4.2` (01 a 98, tema claro y sus variantes oscuras).

### Cambiado

- La empresa de prueba de `minv datos-prueba`, `tools\bd_local.ps1` y `tools\bd_nube.ps1 -DatosPrueba` pasa de la
  ferretería (MINV, sucursales CM, EA y SC) a **Tech Zone Gaming** (`TECHZONE`, CM, CB y SC); los nombres de los usuarios de
  prueba son los mismos (misma semilla) con correos `@techzone.example`. Las ramas anteriores conservan la ferretería.
- La **demostración** ya no importa el libro de la V2.1: genera Tech Zone Gaming en memoria con el MISMO `LocalDataSeeder`
  (8 días, 6 facturados, 40 % del volumen, sin los escenarios de contingencia ni el pedido sugerido completo, altas en
  paralelo) y abre en unos **17 s en frío** (unos 2 s son la primera distribución a Cochabamba y Santa Cruz: 90 productos
  con pocas unidades); T-09 prevalece sobre lo que A-12 decía de la V2.1 (`minv import-v21` sigue disponible).
- Arranque en frío (`Directory.Build.props`): `TieredPGO=false` en el escritorio, la CLI y las pruebas y
  `TieredCompilation.CallCountingDelayMs=0` en todos los proyectos (~25 % menos de tiempo en la demostración; los
  servidores conservan la PGO dinámica).
- `inventory.serial_numbers`: la serie es única por (empresa, **variante**, serie) —antes por lote— y guarda su tipo y su
  fecha de ingreso.
- Tema: el **oscuro** es el predeterminado; las preferencias guardadas hasta la V4.1 con «sistema» (el valor por defecto)
  pasan una sola vez al oscuro; «claro» u «oscuro» elegidos antes se respetan.
- Los parámetros nuevos de los contratos existentes (`Serials`, `Defective`, `MissingSerials`, `SpecFilters`,
  `ParentCode`, `SuggestedRegister`) son opcionales y van al final: los clientes de la V4.1 siguen funcionando con
  productos sin serie.
- **Caja**: `GetPosStateQuery` sugiere la caja (`SuggestedRegister`: la del turno propio o una libre, primero las de la
  sucursal activa) y la pantalla la preselecciona; el nombre de sucursal de la caja es el de la sucursal activa.
- **Simulador del SIN**: padrón simulado de actividades por NIT; Tech Zone Gaming (1023456029) solo sincroniza sus tres
  actividades de tecnología y sus productos SIN; cualquier otro NIT conserva además las de ferretería de la V4.1 (las usan
  sus pruebas de homologación).
- **Demostración**: el saldo inicial de la casa matriz va del 60 al 105 % del máximo (la base local sigue del 90 al
  160 %) para que su tablero no arranque en sobrestock; ya no copia el libro de la V2.1 junto al programa y se quitó
  `DesktopDemoBilling` (la facturación de la demostración la prepara el mismo `LocalDataSeeder`).
- **Series e IMEI**: las tarjetas cuentan todas las series de la empresa, la grilla muestra las 1.000 más recientes y la
  búsqueda acepta también el SKU; el detalle de **Documentos fiscales** muestra las series o IMEI de cada línea.
- **Actividad**: los comandos auditados de la V3 a la V4.2 y sus datos se muestran en español (sí/no en lugar de
  True/False); textos de ayuda, alertas, toma física y permisos sin referencias a la V2.1 ni a la ferretería
  («Registrar entradas, saldo inicial y ajustes», «Registrar salidas»). La empresa por defecto de `appsettings.json` es
  `TECHZONE`.
- **Migraciones**: `V4MultiBranchCloud` deja congelado su `ANALYZE` de los 8 esquemas de la V4 (usaba
  `PostgresMaintenance.AnalyzeSql`, que en la V4.2 sumó `billing` y `service` y así cambiaba el SQL de una migración
  publicada, reglas A-07 y B-15); la prueba `ModelTests.Db_init_sql_coincide_con_el_script_de_las_migraciones` compara
  `scripts/db_init.sql` con el script idempotente de las migraciones.
- **Costo neto de IVA en los datos de prueba**: `catalogo-tecnologia.json` trae el costo con el IVA incluido (como el
  precio); el lector (`TechSeedCatalog.NetCost`) lo carga **neto**, el **87 %** del costo con IVA (2 decimales, mitad hacia
  arriba; en Bolivia el crédito fiscal es el 13 % de la factura), para el producto, el saldo inicial y las compras, porque
  el catálogo mide el margen con el costo promedio contra el precio neto (el 87 % del precio) y el costo de ventas y los
  reportes usan ese costo. El catálogo muestra el margen del JSON (15,2 a 34,9 %, 22,1 % en promedio) y ningún «margen
  bajo» (< 15 %); con el costo con IVA eran del 4,2 al 26,4 % (12,0 %) y 108 productos quedaban bajo ese umbral. El lector
  valida que costo, precio y `margen_pct` sean coherentes. (Durante el pulido el costo neto fue costo / 1,13: el cierre lo
  cambió al 87 %, ver abajo.)
- **Factura del proveedor sobre una compra al costo neto** (`RegisterSupplierInvoiceHandler`): la factura es el importe
  definitivo de la compra; su asiento lleva el crédito fiscal a 1.1.04, **suma a 2.1.01 Proveedores** la diferencia entre
  el importe de la factura (menos descuentos) y el valor de la recepción, y saca de 1.1.05 solo el resto (recepción neta
  de Bs 1.000 y factura de Bs 1.130: Debe 1.1.04 146,90 / Haber 2.1.01 130,00 y Haber 1.1.05 16,90). Con la recepción al
  costo con IVA (factura = recepción) sigue siendo Debe 1.1.04 / Haber 1.1.05, como en la V4.1; un descuento del proveedor
  baja la deuda. Antes, con las compras netas, el asiento sacaba del inventario un IVA que nunca había entrado y la deuda
  quedaba sin el IVA. Los datos de prueba registran las 4 facturas de proveedores con el IVA encima (el costo con IVA
  del catálogo): en la demostración, el crédito fiscal de compras del mes pasa de Bs 41.590,13 a Bs 46.996,84 (capturas 13
  Actividad, 73 y 77 Libros fiscales).
- **Cierre · inventario del mayor = valor del stock**. En la carga de 60 días, 1.1.05 Inventario de la casa matriz quedaba
  **+452,79** sobre el valor del stock (Σ existencias × costo promedio; Cochabamba y Santa Cruz en 0,00). Descompuesto por
  tipo de movimiento contra los asientos de cada documento (saldo inicial, compras, ventas, transferencias, devoluciones y
  anulación, reposición por garantía: todo cuadra al centavo, sin redondeos) quedaron dos causas de lógica, corregidas en
  su origen:
  - **+2.646,02: los AJUSTE (±) no se contabilizaban** (9 mermas y 1 faltante de la toma física por 3.134,52 menos 1
    sobrante por 488,50). `RegisterMovementCommand` y `PostPhysicalCountCommand` contabilizan ahora cada ajuste en la misma
    transacción, al costo promedio del almacén (`InventoryAdjustments`): merma o faltante Debe **5.1.09** / Haber 1.1.05;
    sobrante Debe 1.1.05 / Haber **4.1.02** Sobrantes de inventario (la cuenta existía y no se usaba); el mensaje dice el
    asiento. La toma física ganó el reintento optimista (×3). ENTRADA, SALIDA y SALDO INICIAL registrados a mano siguen sin
    asiento (límite L-05).
  - **−2.193,23: el crédito fiscal de las 4 facturas de proveedores** (13 % de recepción × 1,13) superaba en un 1,69 % de
    la recepción el IVA sumado a la deuda, porque el costo neto era «importe / 1,13» y el SIN da crédito por el 13 % del
    importe. Con el costo neto al **87 %** (`FiscalRules.NetCost`) y la factura = **recepción / 0,87**
    (`FiscalRules.InvoiceForNetCost`, datos de prueba y diálogo), el crédito es exactamente el IVA sumado a la deuda (también
    con el redondeo, probado con 20.000 importes) y el asiento no toca 1.1.05.
  Después de las dos correcciones la misma carga deja **0,00** en las tres sucursales; lo vigilan `LocalDataSeederTests`
  (memoria) y la prueba de los datos de prueba en PostgreSQL. Diseño: `edicion-tecnologia-v4.2.md` §12 (tabla y consulta
  de control).
- **Cierre · IVA boliviano sobre el importe facturado** (`VatRules`, decisión D-10): el asiento de la venta y el de la
  devolución separan el IVA como el **13 % de lo cobrado** (Ventas = 87 %), como el débito fiscal del libro de ventas (antes
  importe × 13/113, la convención de la V3); el catálogo mide el margen contra el 87 % del precio y su editor calcula el
  precio de un margen con la misma regla; la caja muestra «IVA incluido» con esa regla. El IVA de la venta es el del
  **total** de la factura, redondeado una vez y repartido entre las líneas (`VatRules.Allocate`, resto mayor): así 2.1.02
  es al centavo el débito fiscal del libro de ventas (13 % de la base de cada factura); redondear línea por línea podía
  apartarse en centavos (729,50 + 15,50: 94,84 + 2,02 = 96,86 contra 96,85 del libro). Una empresa de otro
  país (tasa del 19 %, según el país de la dirección de sus sucursales: `Pricing.VatOnInvoicedAmountAsync`) conserva la
  convención de la V3. `CatalogOptions` y `PosState` llevan `VatOnInvoicedAmount` (parámetro opcional al final).
- **Cierre · Registrar factura del proveedor** (Libros fiscales): propone el importe con IVA de la recepción al costo neto
  (recepción ÷ 0,87), lo explica y muestra el asiento que dejará lo que se escriba (crédito fiscal, deuda con el proveedor
  y si el inventario cambia).
- **Cierre · Catálogo**: en la tarjeta de la galería el precio se ve siempre completo («Bs 6.899,00» ya no queda «Bs 6.89»
  tapado por el SKU); el SKU usa lo que queda y se recorta con «…» (entero en la ayuda).
- **Cierre · Actividad**: los importes del detalle (importe total, descuentos, costo, precio, fondo y arqueo de caja) se
  muestran con el formato de dinero del escritorio («Bs 35.212,54» y no «35212.54»).
- **Cierre · capturas** (`docs/product/capturas/v4.2`): 42 regeneradas donde cambiaron por el cierre (costo neto al 87 %
  en tableros, stock, fichas, catálogo, compras, reportes, contabilidad con 4.1.02 Sobrantes y 5.1.09 Mermas, sucursales y
  transferencias; IVA del 13 % de lo cobrado en la caja y en Ventas; precio completo en la galería; importes de la
  Actividad; crédito fiscal de compras en Libros fiscales); las otras 41 solo cambiaban horas y se conservan.
- **Tablero**: el gráfico «Entradas y salidas» ya no cuenta el **saldo inicial** como entrada (es la apertura del
  inventario, no operación, y aplastaba el resto de los días); se informa aparte en el resumen y en la ayuda de la columna
  (`MovementTrendDay.Opening`).
- **Actividad**: las fechas del detalle se leen con el formato del escritorio (23/09/2026, 23/09/2026 14:30) y no en ISO.
- **Datos de prueba**: el mensaje «N días de operación simulados» da el mismo total de ventas en caja que el resumen final
  y lo desglosa (día a día, facturas manuales CAFC transcritas y armados de PC cobrados en la caja).
- **EF Core**: orden determinista en las consultas que generaban los avisos 10102, 10103 y 10114 (API Key y token de sesión
  en el servidor y el gateway, búsqueda de una serie, notas crédito-débito pendientes del trabajo de la facturación,
  leyendas y actividades del SIN); los resultados no cambian.
- **Capturas** (`docs/product/capturas/v4.2`): 42 regeneradas con el costo neto, el gráfico sin el saldo inicial y las
  fechas de la Actividad (tableros, catálogo, stock, fichas, pedido, compras, reportes, contabilidad, sucursales,
  transferencias, documentos y libros fiscales, series); las otras 41 solo cambiaban horas mostradas y se conservan.

### Decisiones y límites conocidos

- Los **servicios** (unidad `SERV`: ensamblado, instalación de Windows, mantenimiento) llevan un **cupo** de existencias:
  el dominio no tiene artículos sin stock.
- **Excepción parcial a A-13** en los datos de prueba, documentada en el código: marcas y modelos, la unidad `SERV`, las
  categorías de cliente, las cajas extra y la topología del almacén se escriben directo (no tienen casos de uso todavía);
  todo lo demás pasa por la tubería completa.
- La garantía se **deriva** (venta + meses); la de una unidad entregada como **reposición** cuenta desde la fecha de la
  reposición.
- La **nota crédito-débito** no lleva `numeroSerie` en el XML: el XSD del sector 24 no tiene el campo; las series
  devueltas quedan en el documento y en `sales_return_line_serials`.
- La **devolución por falla** deja la unidad en garantía, sin reingreso; lo que el proveedor reponga o acredite entra como
  un hecho nuevo.
- Las **ventas anteriores** a volver serializado un producto no piden series retroactivas (esas unidades vendidas no
  tienen trazabilidad por serie).
- Los movimientos que se registran **a mano** sin documento ni precio (SALDO INICIAL, ENTRADA, SALIDA, VENTA POS y
  DEVOLUCIÓN DE CLIENTE) **no generan asiento**: su contrapartida la registra el contador; los AJUSTE (±) sí (L-05).
- El **costo promedio no se recalcula** con la factura del proveedor: si su importe no es el que corresponde a la recepción
  (recepción / 0,87 al costo neto), 1.1.05 se aparta del valor del stock en la diferencia; el diálogo lo advierte (L-06).
- Con los roles predeterminados, las **devoluciones** exigen `sales.pos.operate` y `billing.void`: solo el Administrador.
- El tablero Tecnología lee el modelo de escritura (pocas filas por empresa y ventana de 30 días).
- En la **demostración** (8 días), Cochabamba y Santa Cruz reciben una primera distribución de unos 90 productos con
  pocas unidades y pocas reposiciones: unos 60 a 70 productos aparecen agotados en esas sucursales (antes, con 12
  productos, eran 138). Una distribución completa llevaba la apertura a más de 20 s; la base local de 60 días abastece las
  tres sucursales.

### Pendiente

- Lo propio del SIN real sigue como en la V4.1 (token, NIT y confirmar el contrato con el WSDL del piloto):
  `docs/billing/puesta-en-produccion-siat.md`.

## [4.1.0-alpha.1 · facturación SIAT] · 2026-09-26 · rama `Inventario-V4.1`

Tema: **facturación SIAT de Bolivia** en la modalidad **Facturación Computarizada en Línea**: cada venta (caja, tienda en
línea por el API y facturas manuales de contingencia transcritas) emite su **factura Compra Venta** (sector 1) y cada
devolución su **nota Crédito-Débito** (sector 24); M-INV las envía al SIN, sigue facturando sin internet, se recupera
solo y lleva los libros de ventas y compras. Construida sobre `Inventario-V4.-BaseDeDatosNube`. **140 tablas en 9
esquemas** (27 nuevas en `billing`). Diseño: `docs/architecture/facturacion-siat-v4.1.md` · reglas F-01 a F-17:
`.claude/v41-billing-rules.md` · qué es y qué falta confirmar: `docs/billing/README.md` · paso a paso:
`docs/deployment/inicio-rapido-v4.1.md` · al SIN real: `docs/billing/puesta-en-produccion-siat.md`.

### Agregado

- **Investigación de la normativa del SIN** (`docs/billing/investigacion-siat/`, especificaciones 00 a 08 con cada dato
  citado a su página): códigos y operaciones, servicios de facturación, emisión, contingencia y anulación, 18 catálogos y
  193 códigos de respuesta, XML/XSD, CUF y representación gráfica, notas crédito-débito, autorización e inspección;
  contradicciones resueltas y huecos abiertos H-01 … H-29.
- **Dominio** (`MINV.Domain/Billing`): `FiscalDocument` (agregado con estados `Pending`, `Valid`, `Rejected`,
  `NoResponse`, `Offline`, `InPackage`, `PackageRejected`, `DuplicateToVoid`, `Voided` con reversión única, `Discarded`),
  líneas y comprador **congelados**, totales DERIVADOS de las líneas; **CUF** (`Cuf.Generate`: Módulo 11, Base 16 y
  código de control, verificado con los vectores oficiales); `FiscalRules` (redondeo HALF-UP a 2 decimales por línea,
  plazo de anulación y reversión hasta el día 9 del mes siguiente, 48 h y 72 h de contingencia, tarjeta enmascarada);
  `SiatPointOfSale` (en línea → fuera de línea tras dos fallos → recuperando → en línea; contingencia manual), CUIS,
  CUFD, eventos significativos, paquetes (≤ 500), CAFC, configuración por empresa y ambiente (token cifrado), sucursales
  del Padrón, homologación y devoluciones de venta (`SalesReturn`).
- **Casos de uso** (`MINV.Application/Billing`, contratos en `BillingContracts.cs`, módulo `FISCAL_SIAT`, permisos
  `billing.view`, `billing.issue`, `billing.void`, `billing.contingency`, `billing.configure`):
  - emisión dentro de la venta (`CheckoutCommand` con `FiscalBuyerInput` y tarjeta; pedidos externos del API con
    comprador) con `FiscalIssuer`: comprador nominativo (NIT/CI/CEX/pasaporte/otro, complemento, NIT especiales 99001,
    99002 y 99003 con código de excepción), homologación obligatoria, total fiscal = total cobrado, leyenda Ley 453 al
    azar, fuera de línea automático con el último CUFD;
  - envío después del COMMIT (`DispatchFiscalDocumentsCommand`) y trabajo automático (`RunSiatWorkCommand`,
    `ISiatWorker`): 908/902/904, sin respuesta → re-emisión fuera de línea, recuperación (CUFD nuevo → evento → verificación
    de los sin respuesta y duplicados → paquetes → validación), notas en cola y correos;
  - anulación (motivo del catálogo, plazo, «anular y devolver mercadería»), reversión única, re-emisión, verificación de
    estado, devoluciones parciales con nota crédito-débito (`CreateSalesReturnCommand`), contingencia manual con CAFC
    (registro del talonario, transcripción con su numeración propia, paquete), fin de contingencia y recuperación forzada;
  - administración: configuración, conexión por ambiente, sucursales del Padrón, puntos de venta (registro, vínculo con
    la caja, cierre), CUIS/CUFD, «Preparar SIAT», 18 catálogos, hora del SIN, verificación de comunicación y de NIT,
    estado SIAT con alertas y plazos, homologación con sugerencias, correo SMTP de la empresa;
  - consultas: documentos fiscales (búsqueda, detalle, bitácora, XML), representación gráfica (PDF media carta y rollo
    ESC/POS 80 mm con QR, «SIN VALOR LEGAL» en pruebas), libros de ventas IVA y de compras (CSV y Excel), resumen IVA/IT,
    facturas de proveedores con crédito fiscal y asiento, bitácora técnica de llamadas al SIN.
- **Persistencia**: esquema `billing` (27 tablas) + `sales.sales_returns`, `sales.sales_return_lines` y
  `purchasing.supplier_invoice_fiscal`; migraciones `V41SiatBilling` (RLS por empresa y por sucursal, 9 libros
  append-only más, `billing.siat_active_tenants`, vista `billing.v_fiscal_document_totals` con los totales derivados,
  permisos y privilegios) y `V41CafcNumbering` (las facturas CAFC llevan la numeración de su talonario).
- **XML y archivos**: `SiatXmlSerializer` (orden del XSD, `xsi:nil`, UTF-8 sin BOM, validación contra los XSD oficiales
  embebidos, GZIP, paquetes GZIP(TAR) y SHA-256), `FiscalPdfRenderer` (PDF propio sin dependencias, QR vectorial),
  `FiscalRollRenderer` (ESC/POS con QR nativo) y monto literal.
- **Cliente SOAP del SIN** (`SiatSoapGateway`, contrato centralizado en `SiatSoapContract`, cabecera
  `apikey: TokenApi <token>`, lectura tolerante, bitácora técnica **sin el token**) y **simulador del SIN**
  (`SiatSimulatorEngine`: CUIS, CUFD, puntos de venta, eventos, catálogos de ferretería, verificarNit, recepción con
  XSD/hash/CUF/fórmulas, NIT del comprador inactivo → 1037, paquetes, anulación y reversión con plazo, estado en JSON):
  en proceso (`InProcessSiatGateway`, demostración y pruebas) y por HTTP (`src/4. Tools/MINV.SiatSimulator`, puerto
  5095, `/control/offline` para simular cortes).
- **Servidor en la nube**: despachador fiscal en segundo plano (`SiatBackgroundService`, `Minv:Siat:Background`): envío
  cada 10 s y mantenimiento cada 60 s de cada empresa con la facturación activa. El escritorio en modo nube nunca ve el
  token.
- **Escritorio**: sección **Facturación** (Documentos fiscales, Estado SIAT, Homologación, Libros fiscales,
  Facturación SIAT), datos de facturación y estado fiscal en el punto de venta; guía en `docs/product/escritorio-v4.1.md`.
- **Datos de prueba que facturan** (`LocalDataSeeder` + `SiatSeeding`/`SiatSeedSetup`, con los casos de uso y el reloj
  simulado): la empresa MINV factura los últimos 25 días contra el simulador del SIN EN PROCESO (NIT de simulación
  1023456028, «FERRETERÍA EL CONSTRUCTOR S.R.L.», ambiente 2, sucursales del Padrón CM = 0, EA = 1, SC = 2, 8 puntos de
  venta, catálogos, homologación completa por categoría, token de simulación aleatorio cifrado), con comprador en cada
  venta (clientes habituales con NIT o CI y compradores eventuales), un corte de internet de 3 horas en El Alto
  recuperado con evento y paquete validado, una contingencia manual CAFC en Santa Cruz con 3 facturas transcritas, 3
  anulaciones (con devolución, re-emitida y revertida), 2 devoluciones con nota crédito-débito, un NIT rechazado
  re-emitido con excepción, pedidos web facturados y 4 facturas de proveedores; al final todo en línea con el CUFD de
  hoy. El estado del simulador queda en `%LOCALAPPDATA%\M-INV\siat-simulador.json` y el token en
  `claves-integracion.txt` (`MINV_SIAT_TOKEN`) para que el simulador HTTP conozca lo emitido. `SeedResult.Billing`
  resume la facturación.
- **Demostración**: la empresa de demostración también factura con el simulador en memoria (configuración mínima,
  punto 0 y el de la caja 1, homologación y 4 facturas de ejemplo de hace una semana), sin escribir en disco
  (`DemoSession.Billing`).
- **Línea de órdenes** (`minv`): `datos-prueba` con la facturación (`--sin-facturacion`, `--dias-facturacion`,
  `--siat-estado`, `--simulador`); nuevo `siat` con `estado`, `preparar`, `sincronizar`, `procesar [--forzar]`
  (proceso de plataforma contra la conexión de la empresa) y `simulador-estado`, `simulador-apagar`,
  `simulador-encender` (control HTTP del simulador); `verify` cuenta las 27 tablas de `billing` y comprueba que el
  total de cada factura válida sea el cobrado y que ninguna venta tenga dos documentos vigentes.
- **Scripts**: `tools\bd_local.ps1` (recrear detiene los servidores locales, carga la facturación y guarda
  `MINV_SIAT_TOKEN`; `-SinFacturacion`), `tools\servidores_locales.ps1` (inicia, detiene y consulta también el
  simulador del SIN, ANTES del servidor en la nube, con el estado y el token por variables de entorno;
  `-SinSimulador`), `tools\build_v3.ps1` (140 tablas, capturas en `docs/product/capturas/v4.1`, paso que arranca el
  simulador y prueba su WSDL y el corte simulado).
- **Despliegue**: `deploy/Dockerfile.siatsimulator` y perfil opcional `siat-simulador` de Docker Compose (comparte la
  red del servidor en la nube); variables `MINV_SIAT_BACKGROUND` y `MINV_SIAT_TOKEN` (solo ensayos) en
  `deploy/.env.example`; imágenes 4.1.0-alpha.1.
- **Documentación**: `docs/deployment/inicio-rapido-v4.1.md` (el algoritmo paso a paso con el simulador y la tabla «Si
  algo no funciona»), `docs/billing/README.md`, `docs/billing/puesta-en-produccion-siat.md` (registro en el Portal SIAT,
  token del piloto, WSDL, Fases I-III con el checklist II-1 … II-15, inicio de operaciones), sección de facturación en
  `docs/architecture/arquitectura-v4.md` y `docs/deployment/despliegue-nube-v4.md` (§12: despachador fiscal y SIN real).

### Cambiado

- `SeedOptions`: el NIT por defecto es el de simulación (`1023456028`) y hay opciones de facturación (`Billing`,
  `BillingDays`, `SiatToken`, `SiatSimulatorUrl`).
- `SupplierInvoiceLine`: una línea sale de una recepción **o** tiene descripción propia (arco exclusivo
  `ck_supplier_invoice_lines_origen`, que PostgreSQL ya exigía); la factura registrada sobre una recepción no repite la
  descripción.
- El simulador valida el NIT del comprador en la factura (1037 sin código de excepción) con el mismo Padrón simulado que
  `verificarNit`.

### Pendiente (V4.1)

- Confirmar el contrato SOAP con el WSDL real del piloto (hueco H-01) y los demás huecos de `docs/billing/README.md`.
- Pago combinado con varios medios en una venta (II-3 del checklist de inspección), asistente para ejecutar la Fase I en
  lote, modalidad electrónica, emisión masiva y Registro de Compras por servicio web.

## [4.0.0-alpha.1 · base de datos en la nube] · 2026-09-25 · rama `Inventario-V4.-BaseDeDatosNube`

Tema: **M-INV multi-sucursal en la nube**. Cada sucursal ve y opera solo lo suyo, la mercadería viaja entre
sucursales con **transferencias en tránsito**, el escritorio trabaja **por internet contra un servidor M-INV** (sin
credenciales de la base) y terceros se integran por un **API Gateway B2B** con API Keys y **webhooks firmados**. 110
tablas en 8 esquemas. Construida sobre `Inventario-V3.-BaseDeDatosLocal`. Arquitectura:
`docs/architecture/arquitectura-v4.md` · reglas B-01 a B-17: `.claude/v4-architecture-rules.md`.

### Agregado

- **Multi-sucursal**: `IBranchScoped` (`branch_id`, 35 tablas) e `IInterBranch` (`from_branch_id`/`to_branch_id`, 5
  tablas); `BranchScope` (todas, asignadas y activa) calculado en el servidor al iniciar sesión (`UserAccess`): el permiso
  `corporate.branches.all` da la vista de gerencia global y `warehouse.branch_users` las sucursales de cada usuario;
  `SelectBranchCommand` cambia la sucursal activa. Cuatro barreras: filtros globales de EF Core, guardas de escritura
  (`branch.outside_scope`, `branch.immutable`), FK compuestas `(tenant_id, branch_id, padre)` y RLS **RESTRICTIVA**
  `branch_isolation` con `iam.branch_visible()` y la variable de sesión `minv.branch_ids`. Directorio corporativo
  (sucursales, almacenes, catálogo) sin filtro. Numeración por sucursal (`F-CM-000001`, `TR-EA-000003`).
- **Transferencias entre sucursales** (`StockTransfer`): `Pending → Dispatched (en tránsito) → Received`, o `Cancelled`;
  el origen crea, despacha y anula, el destino recibe. Despacho completo por lote con manifiesto
  (`stock_transfer_line_batches`), recepción con los mismos lotes y **faltantes con motivo** como deltas compensatorios
  (`stock_transfer_discrepancies`), vínculos a los movimientos (`stock_transfer_movements`) y bitácora append-only
  (`stock_transfer_events`). Una transacción explícita con reintento optimista ×3. Contabilidad: despacho Debe 1.1.06
  «Mercadería enviada a sucursales» / Haber 1.1.05; recepción Debe 1.1.05 + 5.1.09 (faltante) / Haber 2.1.04
  «Mercadería recibida de sucursales»; en el consolidado 1.1.06 − 2.1.04 = valor en tránsito. Casos de uso
  `Get/CreateTransfer…`, `DispatchTransferCommand`, `ReceiveTransferCommand`, `CancelTransferCommand`; vista de
  conservación `inventory.v_transfer_breaches`.
- **Sucursales** (`Application/Corporate`): `GetBranchesQuery`, `CreateBranchCommand` (almacén, topología mínima y
  caja), `UpdateBranchCommand`, `AssignUserBranchesCommand`, `ConsolidatedStockQuery` (stock por sucursal + en tránsito,
  contado una vez) y `GetBranchReportQuery` (tablero gerencial desde el modelo de lectura).
- **Servidor en la nube `MINV.CloudServer`**: `POST /api/v1/session/login` (token `mses_…` guardado como hash, 12 h de
  vencimiento deslizante), `POST /api/v1/rpc` (los mismos comandos y consultas de MediatR, catálogo acotado a
  `MINV.Application`, permisos y alcance recalculados en cada petición), `POST /api/v1/session/logout`,
  `GET /api/v1/health`; exige la misma versión mayor del cliente (`X-MINV-Client-Version`); límites de tasa por IP y por
  sesión; contrato y mapeo de errores en `Application/Remote/RpcContract.cs`.
- **API Gateway `MINV.ApiGateway`** (solo B2B): API Keys `minv_<prefijo>_<secreto>` (SHA-256, alcances
  `catalog:read`, `stock:read`, `orders:write`, `transfers:read`, `transfers:write`, `webhooks:manage`, `reports:read`
  intersectados con los permisos del dueño, sucursal opcional); rutas `/v1` de catálogo, sucursales, stock, stock
  consolidado, pedidos, transferencias, webhooks, entregas y reportes; ProblemDetails; límites (120/min por IP y cubeta
  por llave); OpenAPI en `/docs`; servicios en segundo plano (webhooks y refresco del modelo de lectura).
- **Outbox transaccional y webhooks**: eventos de dominio (`sale.completed`, `sale.voided`, `purchase.received`,
  `transfer.dispatched`, `transfer.received`, `transfer.discrepancy`) guardados en `integration.outbox_events` en la
  MISMA transacción; despachador con `FOR UPDATE SKIP LOCKED` (`integration.claim_deliveries`), firma
  `X-MINV-Signature: t=<unix>,v1=<hex>` (HMAC-SHA256, doble firma 24 h al rotar), cabeceras `X-MINV-Event` y
  `X-MINV-Delivery`, cliente HTTP protegido contra SSRF, 8 intentos (1 min, 5 min, 30 min, 2 h, 6 h, 12 h, 24 h) y
  cada intento en `webhook_deliveries`. Secretos aleatorios cifrados con AES-256-GCM y claves maestras de
  `MINV_INTEGRATION_KEYS`; verificación de referencia `ApiKeyTokens.Verify`.
- **Idempotencia**: pedidos externos por (API Key, `externalId`) con hash del contenido (`sales.external_orders`:
  repetir devuelve la venta original, otro contenido → 422); comandos del escritorio en la nube por `requestId`
  (`iam.processed_requests`, misma transacción que el comando).
- **Modelo de lectura** (OLAP): `MinvReadDbContext`, `IReportingReader`, esquema `reporting` con vistas materializadas
  (`mv_branch_stock`, `mv_branch_daily_sales`) y vistas `security_barrier` filtradas; réplica opcional `MINV_DB_READ`;
  `reporting.refresh_all()` cada 5 minutos.
- **Base de datos**: migración `V4MultiBranchCloud` (97 → 110 tablas, esquema `integration`): relleno de `branch_id` por
  la jerarquía con triggers pausados solo durante el relleno, políticas por empresa (108) y por sucursal (40) desde
  listas explícitas, 15 libros append-only, funciones SECURITY DEFINER con `search_path` fijo y EXECUTE solo para
  `minv_server` (`integration.resolve_api_key`, `iam.resolve_session`, `integration.claim_deliveries`,
  `reporting.refresh_all`), permisos y cuentas V4 para las empresas existentes. Rol **`minv_server`** (NOBYPASSRLS, no
  dueño); los servidores se niegan a arrancar con un rol que pueda saltarse RLS (`MINV_ALLOW_PRIVILEGED_ROLE=1` solo en
  desarrollo).
- **Módulos comerciales**: `CLOUD_HA` Infraestructura Cloud HA (Bs 12.000 + 1.500/mes), `MULTI_BRANCH` Topología
  multi-sucursal (8.000 + 500), `API_INTEGRATIONS` Integraciones API B2B (6.000 + 400), `GLOBAL_AUDIT` Auditoría global
  con réplicas de lectura (5.000 + 300).
- **Datos de prueba multi-sucursal** (`minv datos-prueba`): sucursales CM (Casa matriz, `ALM01`), EA (El Alto, `ALMEA`,
  caja `EA-CAJA1`) y SC (Santa Cruz, `ALMSC`, caja `SC-CAJA1`); 12 usuarios asignados por sucursal; reposición semanal
  con faltantes ocasionales; pedidos del e-commerce por la API Key «Tienda en línea»; al final, una transferencia en
  tránsito a SC y una pendiente a EA.
- **Escritorio**: selector de conexión en el inicio de sesión («Base local», «Nube» (servidor M-INV) con dirección
  https —http solo para `localhost`— y la demostración), selector de sucursal en la barra superior («Todas las
  sucursales» para la gerencia global) y pantallas **Sucursales**, **Transferencias** e **Integraciones** (API Keys,
  webhooks, entregas). En modo nube el token vive solo en memoria y los comandos se reintentan con el mismo id.
- **Herramientas**: `tools\bd_local.ps1 -Accion recrear` (V4: rol `minv_server`, claves maestras y archivos
  `usuarios-prueba.txt`, `credenciales-bd-local.txt`, `claves-integracion.txt`), `tools\servidores_locales.ps1`
  (`iniciar`, `detener`, `estado`: servidor en `http://localhost:5080` y gateway en `http://localhost:5090`),
  `tools\bd_nube.ps1` (`preparar`, `estado`: PostgreSQL gestionado de DigitalOcean, AWS RDS o Supabase) y `deploy/`
  (Docker Compose y Dockerfiles de ambos servidores).
- **Pruebas** `tests/MINV.Integration.Tests`: servidor en la nube y gateway reales (Kestrel en loopback) sobre la base en
  memoria con la empresa multi-sucursal: versión del cliente, alcance decidido por el servidor, idempotencia de
  comandos y pedidos, errores con su código, serialización del catálogo RPC completo, 401/403 por llave y alcance,
  OpenAPI y webhooks firmados.
- **Documentación**: `docs/architecture/arquitectura-v4.md`, `docs/deployment/despliegue-nube-v4.md`,
  `docs/deployment/inicio-rapido-v4.md`, `docs/integration/api-gateway-v1.md`, `docs/product/escritorio-v4.md` (con
  capturas en `docs/product/capturas/v4`), `.claude/v4-architecture-rules.md`; ERD y guía de migraciones actualizados.
- **Herramientas**: `tools\bd_local.ps1` (rol `minv_server`, claves de integración, datos multi-sucursal),
  `tools\servidores_locales.ps1` (nube simulada en el equipo), `tools\bd_nube.ps1` (PostgreSQL gestionado),
  `minv roles` y `minv verify` con aislamiento por sucursal y conservación de transferencias, `deploy\` (Dockerfiles y
  docker compose).
- **Migración adicional `V4BranchHeaderKeys`**: FK (tenant_id, branch_id) a sucursales en asientos, facturas de
  proveedor y devoluciones a proveedor.
- **Mantenimiento**: `ANALYZE` de las tablas de M-INV al final de la carga de datos de prueba y de la migración V4 (sin
  estadísticas, el reporte de movimientos tardaba 61 s en una base recién cargada; con ellas, 0,17 s).

### Cambiado

- `MINVDbContext` pasa a llamarse **`MinvWriteDbContext`** (y su fábrica `MinvWriteDbContextDesignTimeFactory`); con dos
  contextos, toda orden `dotnet ef` lleva `--context MinvWriteDbContext`.
- `inventory.stock_transfers` y `inventory.stock_transfer_lines` rediseñadas (sucursales, solicitante, costo, variante;
  estados `Pending`/`Dispatched`/`Received`/`Cancelled`).
- `accounting.average_cost_history`: sucursal y `sequence` con índice único (el costo vigente es el de mayor secuencia;
  dos recepciones concurrentes chocan y reintentan).
- `iam.sessions`: sucursal activa, hash del token y vencimiento; `iam.audit_logs`: canal (`desktop`, `cloud`, `api`),
  API Key y sucursal activa.
- `LoginCommand` calcula permisos y alcance por sucursal antes de abrir la sesión (`LoginResult.Access`); un usuario
  sin sucursales asignadas no puede entrar.
- Matriz de permisos: nuevos `corporate.branches.all`, `corporate.branches.manage`, `inventory.transfers.manage` e
  `integration.manage`; GERENCIA gana todas las sucursales y transferencias, BODEGA transferencias, ADMIN todo.
- Plan de cuentas: `1.1.06` Mercadería enviada a sucursales y `2.1.04` Mercadería recibida de sucursales.
- La venta (caja o API) comparte `SaleWriter` y publica `sale.completed`; la recepción de compras publica
  `purchase.received`; la anulación, `sale.voided`.
- Errores de PostgreSQL traducidos: unicidad, serialización e interbloqueo → conflicto (409); FK, CHECK y triggers →
  regla de negocio (422); RLS → acceso denegado (403).
- Versión 4.0.0-alpha.1.

### Verificado

- Migración `V4MultiBranchCloud` aplicada sobre una copia de la base local de la V3: 110 tablas, 108 políticas por
  empresa, 40 por sucursal, 15 triggers append-only y 0 descuadres de conservación.

## [3.1.0-alpha.1 · base de datos local] · 2026-09-25 · rama `Inventario-V3.-BaseDeDatosLocal`

Tema: **todo funcionando con una base de datos PostgreSQL LOCAL** (97 tablas en 5FN), **datos de prueba** con usuarios
de cada rol, **muchas más funciones por rol** (punto de venta, ventas, clientes, compras, proveedores, reportes,
contabilidad, usuarios) e **imágenes de cada producto**. Misma versión 3.1.0-alpha.1, construida sobre `Inventario-V3.1`.

### Agregado

- **`tools/bd_local.ps1`**: PostgreSQL 16 portátil en `%LOCALAPPDATA%\M-INV` sin permisos de administrador (`instalar`,
  `iniciar`, `detener`, `estado`, `recrear`, `-Autoiniciar`, `-SinDatos`): clúster UTF-8 con `scram-sha-256`, roles
  `minv_owner` (clave aleatoria) y `minv_app`, base `minv`, migraciones y datos de prueba. Claves solo en el equipo.
- **`minv datos-prueba`** (`LocalDataSeeder`): empresa **MINV · Ferretería El Constructor S.R.L.**, 9 usuarios de los 6
  roles con contraseñas aleatorias (archivo `usuarios-prueba.txt`, nunca versionado), 8 categorías con 24 posiciones,
  8 proveedores, 29 clientes, 61 productos con imagen, precio, costo, mínimo, máximo y código EAN-13, y 60 días de
  operación simulada con los casos de uso reales: dos cajas (apertura, ventas en 4 medios de pago, arqueo y cierre),
  anulaciones, mermas, pedido sugerido → aprobación → recepción, depósitos, gastos del mes y pagos a proveedores.
- **Imágenes de productos**: tabla `catalog.product_images` (bytea PNG/JPEG ≤ 1 MB, RLS, única por variante; migración
  `ProductImages`), 38 ilustraciones propias (`tools/generar_imagenes_productos.py`, recursos incrustados) asignadas por
  nombre a los datos de prueba y a la demostración; `SetProductImageCommand`, `RemoveProductImageCommand`,
  `GetProductImagesQuery`.
- **Casos de uso nuevos** (con permisos, validación y auditoría): catálogo (`GetCatalogQuery`, `GetCatalogOptionsQuery`,
  `SaveProductCommand` con posición, `SaveCategoryCommand`); clientes y proveedores (`Get/Save…`); compras
  (`CreatePurchaseOrderCommand`, `CreateSuggestedPurchaseOrdersCommand`, `Approve…`, `Cancel…`, `ReceivePurchaseOrderCommand`
  con costo promedio ponderado y asiento); ventas (`GetPosStateQuery`, `GetSellableProductsQuery`, `CheckoutCommand` con
  factura, IVA incluido, pago y asiento, `GetSalesQuery`, `GetSaleLinesQuery`, `VoidSaleCommand` con devolución y asiento
  inverso); reportes (`GetSalesReportQuery`, `GetPurchasesReportQuery`, `GetMovementsReportQuery`); contabilidad
  automática con plan de cuentas jerárquico (`ChartOfAccounts`, `JournalPoster`, `GetChartOfAccountsQuery`,
  `GetJournalQuery`, `GetIncomeStatementQuery`, `CreateJournalEntryCommand`, `CreateAccountCommand`); administración
  (`GetUsersQuery`, `SaveUserCommand`, `ResetUserPasswordCommand`, `GetRolesQuery`, `Get/UpdateCompanySettings…`).
- **Pantallas nuevas del escritorio** (todo lo que viene de una lista, en combos): **Catálogo** en galería con imágenes o
  lista y editor lateral (imagen, categoría + nueva, unidad, posición, proveedor, costo, precio con combo de margen,
  mínimo, máximo, código de barras); **Punto de venta** (caja, tarjetas con imagen, carrito con descuentos, cliente y
  medio de pago, vuelto, referencia, ticket en pantalla o ESC/POS, arqueo); **Ventas** (período, filtros, detalle y
  anulación con motivo); **Clientes**; **Órdenes de compra** (desde el pedido sugerido, aprobar, recibir, anular);
  **Proveedores**; **Reportes** (ventas, compras, movimientos, inventario, con gráficos y ranking agrupable);
  **Contabilidad** (estado de resultados, libro diario, plan de cuentas, asientos con plantillas); **Usuarios y roles**
  (alta, rol, contraseña temporal, restablecer, matriz de funciones, parámetros de la empresa).
- **Stock en galería** (tarjeta con la imagen de cada producto) además de la tabla; la ficha del producto muestra la
  imagen y lleva al editor del catálogo.
- Menú por secciones (General, Ventas, Inventario, Compras y reposición, Análisis, Administración) según el rol; cuadros
  con dato a completar (motivo, efectivo contado, documento) y con contraseña para copiar.
- Pruebas: datos de prueba en memoria (coherencia contable, stock, ventas, compras, permisos por rol), pantallas de
  negocio con la demostración (POS → cobro → anulación, compras sugeridas → aprobar → recibir, catálogo, reportes,
  contabilidad, usuarios) y datos de prueba contra PostgreSQL real.

### Cambiado

- Matriz de permisos: nuevos `reports.view`, `sales.customers.manage` y `sales.view`; Gerencia gana contabilidad y
  compras, Bodega compras y reportes, Ventas y Cajero el punto de venta, clientes y ventas, Consulta los reportes.
- Cada empresa nueva recibe el plan de cuentas completo (activo, pasivo, patrimonio, ingresos, costos y gastos).
- La venta de caja se asocia a la sesión y la recepción a su orden (arcos exclusivos `ck_sales_orders_origen` y
  `ck_goods_receipts_origen` que PostgreSQL exige y la memoria no validaba).
- La demostración completa el catálogo de módulos licenciados y agrega precios de venta: el punto de venta funciona
  también sin base de datos.
- `appsettings.json` propone la empresa `MINV` (la de la base local).

### Corregido

- **Clientes** no cargaba con PostgreSQL («Numeric value does not fit in a System.Decimal»): el total por cliente se
  calculaba en SQL como `cantidad × precio × (1 − descuento / 100)` y la división dejaba numéricos de 34 cifras (el
  máximo de .NET es 28). Ahora suma los pagos de las facturas emitidas. La prueba contra PostgreSQL recorre todas las
  consultas de las pantallas con 20 días de datos para que no vuelva a pasar.

### Verificado

- `tools/build_v3.ps1 -Capturas -Publicar` con `MINV_TEST_PG`: 135 pruebas (7 contra PostgreSQL real), migraciones al
  día, `scripts/db_init.sql` con 97 tablas, 46 capturas (claro y oscuro) y `M-INV.exe` publicado.
- `minv verify --codigo MINV`: 97 tablas, 7 libros append-only, RLS en 95 tablas, conservación sin descuadres; partida
  doble cuadrada en los 900+ asientos de los datos de prueba.

## [3.1.0-alpha.1] · 2026-09-25 · rama `Inventario-V3.1`

Tema de la versión: **cliente de escritorio completo, bonito e intuitivo**. La interfaz de la V3 (tablas tipo Excel)
se reemplaza por una aplicación de escritorio con sistema visual propio, pensada para el trabajo diario de bodega,
ventas y gerencia. El modelo de datos, las reglas y la paridad con la V2.1 no cambian.

### Agregado

- **`M-INV.exe`** con ícono propio, **pantalla de carga** (preferencias, tema y comprobación de PostgreSQL en segundos)
  e **inicio de sesión** rediseñado (estado de la base con «Reintentar», Bloq Mayús, recordar empresa y correo).
- **Modo demostración** sin base de datos: el libro de la V2.1 se migra a una base en memoria con el mismo importador
  (reglas, poka-yoke y paridad) y se entra con cualquiera de sus usuarios y roles. Contraseña aleatoria por ejecución;
  el reloj de la demostración se ubica en el día de los datos de la V2.1.
- **Ventana principal**: menú lateral por secciones según los permisos del rol (contraíble), búsqueda global de
  productos con autocompletado sin tildes (`Ctrl+K`), campana de alertas, avisos flotantes, confirmaciones dentro de la
  ventana, menú de la cuenta, tema **claro/oscuro/según Windows** y atajos de teclado.
- **Pantallas**: Inicio (indicadores, próximo paso según el rol, entradas y salidas de 14 días, semáforo en dona, alertas
  urgentes, más vendidos, últimos movimientos, actividad); Stock (chips por estado, categoría, orden, barra de nivel,
  exportación a Excel); Registrar movimiento (tipo → producto → cantidad, vista previa y **poka-yoke rojo sangre** de la
  V2.1, Enter para registrar, historial de la sesión); **Toma física** (iniciar, contar, quitar, anular y generar ajustes
  con confirmación); Alertas; Pedido sugerido por proveedor (copiar para correo o WhatsApp, exportar); **Ficha del
  producto** con gráfico del saldo y kardex; Actividad con filtros; Configuración (tema, impresora ESC/POS con **página
  de prueba**, prueba del escáner, sesión, permisos, conexión) y Ayuda (guías, atajos, semáforo).
- **Cambio de contraseña** (obligatorio si el administrador la asignó) y **cierre de sesión** que vuelve al inicio.
- Lector de códigos en modo teclado en todas las pantallas (elige el producto o abre su ficha) sin ensuciar los campos.
- Casos de uso nuevos: `GetWorkspaceQuery`, `GetProductLookupQuery`, `GetBinsQuery`, `GetMovementTypesQuery`,
  `GetProductCardQuery` (ficha y kardex con saldo acumulado), `GetRecentMovementsQuery`, `GetMovementTrendQuery`,
  `GetOpenPhysicalCountQuery`, `RemoveCountCommand`, `CancelPhysicalCountCommand`, `ChangePasswordCommand` y
  `LogoutCommand` (auditados los que escriben).
- `DatabaseProbe` (comprobación rápida de PostgreSQL sin mostrar la contraseña), `DemoWorkspace` / `DemoClock` y
  `AddMinvDemoInfrastructure` (EF Core InMemory 8.0.31), `ReceiptPrinters` y `ReceiptRenderer.RenderTestPage`.
- **`M-INV.exe --capturas <carpeta>`**: recorre todas las pantallas (claro, oscuro y dos roles) y guarda imágenes;
  `tools/build_v3.ps1 -Capturas` las deja en `docs/product/capturas/v3.1` y `-Publicar` genera el ejecutable con
  `tools/publicar_escritorio.ps1` (`dist/`, dependiente del runtime o `-Autocontenido`).
- Guía de la interfaz `docs/product/escritorio-v3.1.md`; generador del ícono `tools/generar_icono_escritorio.py`.

### Cambiado

- El ejecutable se llama `M-INV.exe` (antes `MINV.DesktopClient.exe`); versión 3.1.0-alpha.1.
- Cada acción de la interfaz es una unidad de trabajo: el contexto de datos descarta lo rastreado y las consultas se
  envían de a una (`SerialMediator`), así nunca se decide con existencias leídas antes de que otra caja las cambiara.
- La auditoría clasifica como *Rechazado* (no *Falló*) una credencial incorrecta.
- `docs/deployment/inicio-rapido-v3.md`: algoritmo rápido de 3 pasos con la demostración y distribución del ejecutable.

### Verificado

- `tools/build_v3.ps1 -Capturas -Publicar` sin fallas: compilación Release sin advertencias; **128 pruebas** (dominio
  47, aplicación 10, hardware 10, infraestructura 44 con 5 que requieren PostgreSQL, **cliente de escritorio 17** con la
  demostración real); migraciones al día; `db_init.sql` sin cambios; 24 capturas revisadas; `M-INV.exe` publicado
  (11,3 MB) y probado hasta el inicio de sesión.
- Pendiente: probar contra un PostgreSQL real (triggers, RLS y vistas) con `MINV_TEST_PG`.

## [3.0.0-alpha.1] · 2026-09-25 · rama `Inventario-V3`

Tema de la versión: **fundación de M-INV V3** (escritorio + PostgreSQL). Transición desde Excel (V2.1) a una
arquitectura cliente-servidor para punto de venta y bodegas de alta concurrencia. Es una versión *alpha*: la base de
datos, el dominio, los casos de uso de inventario, la migración desde la V2.1 y la infraestructura están completos y
probados; las pantallas de compras, ventas POS completas y contabilidad llegan en las siguientes iteraciones.

### Agregado

- **Solución .NET 8** `MINV.sln` en Clean Architecture: `MINV.Domain` (sin dependencias), `MINV.Application`
  (MediatR 12.5, FluentValidation), `MINV.Infrastructure` (EF Core 8 + Npgsql 8), `MINV.Hardware`,
  `MINV.DesktopClient` (WPF/MVVM) y `MINV.Cli` (`minv`). Versiones centralizadas (`Directory.Packages.props`) y
  advertencias como errores.
- **Base de datos PostgreSQL de 96 tablas en 7 esquemas** (iam 14, catalog 18, warehouse 10, inventory 14,
  purchasing 11, sales 19, accounting 10), normalizada hasta 5FN: árbol de categorías con tabla de clausura, variantes
  y atributos, códigos de barras múltiples, conversiones de unidades, topología sucursal › almacén › zona › pasillo ›
  estantería › nivel › posición, lotes con caducidad, series, reservas, tomas físicas, traslados, compras, ventas/POS
  con direcciones normalizadas (país › estado › ciudad › código postal), pagos, costos promedio, impuestos con vigencia
  y contabilidad de partida doble.
- **Multi-tenant**: `TenantId` en toda entidad, filtros globales, FK compuestas `(tenant_id, x_id)` y Row Level
  Security. **Concurrencia optimista** con `xmin`. **Append-only** en movimientos, auditoría, accesos, caja, pagos,
  tipos de cambio y costo promedio (EF Core + triggers). **Auditoría** de cada comando con su resultado.
- **Dominio rico**: `StockLevel` (movimientos, poka-yoke, reservas, ajuste por conteo), `Product`/`ProductVariant`
  (variantes, EAN con dígito de control, empaques, impuestos), `PhysicalCount` (CF-AAAAMMDD, todo o nada),
  `PosSession`, `JournalEntry` (cuadre), `UserCredential` (bloqueo por intentos), `StockRules` y `StockProjection`
  (semáforo, alertas, cobertura, ranking y pedido: tercera implementación de las reglas de la V2.1).
- **Importador V2.1 → V3** (`minv import-v21`): lector .xlsx sin dependencias, plan de migración a través del
  dominio, rechazos y actividad a la auditoría, toma física en curso, y **verificación de paridad** con la instantánea
  de la V2.1 (idéntica en el libro de demostración).
- **Licencias por módulo comercial** (`iam.modules` sembrado con la matriz de valor: motor de datos Bs 8.500, cliente
  de escritorio Bs 6.000, POS y hardware Bs 4.500, RBAC Bs 3.000, SLA Bs 800/mes) y `[RequiresModule]`.
- **Login cifrado** (PBKDF2-SHA256, 600.000 iteraciones), sesiones, equipos autorizados y registro de accesos.
- **Hardware POS**: documentos ESC/POS, comprobante de venta, impresoras serie, red y USB (cola de Windows en RAW),
  lector serie y detector de escáner en modo teclado.
- **Migraciones EF Core** (`InitialCreate`, `GuardsRlsAndViews`) y `scripts/db_init.sql` generado (idempotente).
- **Pruebas**: 102 (dominio 47, aplicación 10, hardware 8, infraestructura 37 de las cuales 5 requieren PostgreSQL
  real vía `MINV_TEST_PG`).
- Documentación: `docs/database/ERD-MINV-V3.md`, `.claude/v3-architecture-rules.md`,
  `.claude/database-migration-guide.md`, `docs/deployment/inicio-rapido-v3.md`, `tools/build_v3.ps1`.

### Decisiones

- **Marco .NET 8** como pidió la especificación. Aviso: .NET 8 deja de tener soporte el 10 de noviembre de 2026; el
  marco está centralizado en `Directory.Build.props` para pasar a .NET 10 LTS cambiando una línea (y las versiones de
  EF Core/Npgsql).
- **MediatR 12.5.0** (última versión con licencia Apache 2.0; la 13 exige licencia comercial).
- **`RowVersion` = `xmin`** de PostgreSQL (equivalente al `rowversion` de SQL Server, sin columna extra).
- **Redundancias controladas y documentadas**: `tenant_id` (aislamiento) y el estado materializado de
  `stock_levels` (control de concurrencia), verificado por la vista `v_conservation_breaches`.

## [2.1.0] · 2026-09-25 · rama `Inventario-V2.1`

Tema de la versión: **M-INV colaborativo completo**. Sobre la base de la 2.0 (captura por usuario, bitácoras
fragmentadas, instantánea a demanda) se agregan las funciones que faltaban para operar el día a día en Microsoft 365
sin salir del libro, todas con la misma regla de oro: nadie escribe en la celda de otro.

### Agregado

- **Portada de Gerencia** (`00_PORTADA_GERENCIA`): próximo paso, accesos (stock y cobertura, alertas, pedido sugerido,
  actividad), 8 indicadores (valor, requieren acción, pedido, movimientos del mes, unidades vendidas en 30 días,
  cobertura mediana, bloqueos del mes, usuarios), gráficos nativos (unidades despachadas por mes, valor por categoría),
  los 10 más vendidos en 30 días y la actividad de cada usuario en el mes.
- **Consulta por usuario** (`17_CONSULTA`): una fila por persona (ADMIN, BODEGA, VENTAS) con su propio selector de
  producto: disponible exacto en vivo, semáforo, mínimo/máximo, sugerido, proveedor, ubicación, último movimiento
  (fecha, tipo y quién), entradas y salidas de 30 días, cobertura y valor. Nuevo `OrdenConsulta` en `02_USUARIOS`.
- **Toma física colaborativa** (`13_CONTEO`): cada producto tiene su celda de conteo; vista previa de la diferencia y
  del ajuste; fecha y confirmación `SI`. Nuevo script **`GenerarAjustesConteo.ts`**: valida todo, compara contra el
  stock exacto, registra AJUSTE (±) o SALDO INICIAL en una sola inserción (`addRows`), vuelve a verificar los ajustes
  negativos y limpia solo los conteos procesados.
- **Pedido sugerido por proveedor** (`18_PEDIDO`): instantánea que escribe `RecalcularStock` con cantidad a pedir,
  subtotal, días de entrega, fecha estimada y contacto del proveedor, lista para filtrar e imprimir.
- **Registro de actividad** (`14_ACTIVIDAD`): cada ejecución de un script (registros, bloqueos, rechazos, recálculos,
  conteos, diagnósticos) con ID, correo, nombre, script, resultado y detalle. Indicadores `kpiEjecuciones`,
  `kpiBloqMes` y `txtUltimaEjecucion`.
- **Resumen diario** `ResumenDiario.ts` (solo lectura) para un flujo programado de Power Automate: asunto, HTML, texto e
  indicadores del día calculados con el stock exacto.
- **15_STOCK**: columnas `Salidas30d`, `CoberturaDias` y `RankSalidas30d` (misma regla en Python y TypeScript).
- **Portadas**: Bodega con *Toma física*; Ventas con *Consultar producto* y *Stock completo*; barra de navegación de 10
  destinos en todas las hojas; próximo paso de Bodega con el conteo en curso.
- **Guía de acceso paso a paso** `docs/deployment/inicio-rapido.md` (demo local, demo en la nube, producción, uso diario
  por rol, Power Automate y solución de problemas) y `99_AYUDA` ampliada (cómo entrar, consulta, toma física, pedido,
  actividad, gerencia, resumen diario).
- **Verificación de tipos** opcional en `tools/build_v2.ps1`: cada script se compila con TypeScript estricto contra
  `tests/office-scripts/excelscript-tipos.d.ts` si hay `tsc` (PATH o `MINV_TSC`).
- **Pruebas**: de 12 a 22 (actividad, toma física, resumen diario, paridad del pedido y de las columnas nuevas);
  el simulador de `ExcelScript` admite `Table.addRows`.

### Cambiado

- `lib/comun.ts`: `registrarActividad`, `agregarFilas` (lote atómico), `saldos` (stock exacto de todos los productos en
  una pasada), `estadoDe` (compartido), `fechaTexto`/`fechaCompacta`, `nombreVisible`; `registrar` audita también los
  intentos sin fila de captura.
- `RecalcularStock.ts` escribe tres instantáneas (stock de 20 columnas, alertas y pedido) y deja su ejecución en
  `14_ACTIVIDAD`; `DiagnosticoInstalacion.ts` revisa las 16 hojas, 16 tablas y 9 nombres de la 2.1, la fila de consulta
  y la contraseña de cada hoja que escriben los scripts.
- `tools/verify_minv_v2.ps1`: 21 hojas; nuevas comprobaciones independientes de cobertura, ranking, pedido, conteo,
  consulta, actividad y Gerencia, con pruebas en vivo (escribir y recalcular).
- Anchos de columna revisados para que los encabezados con filtro no se corten.

### Corregido

- Verificador: `[math]::Max(0, $x)` de PowerShell redondeaba a entero un stock decimal (usaba la sobrecarga `Int32`);
  ahora `[math]::Max(0.0, $x)`.

## [2.0.0] · 2026-09-25 · rama `Inventario-V2`

Tema de la versión: **M-INV colaborativo en Microsoft 365**. Un solo libro en SharePoint/OneDrive que Bodega y Ventas
usan a la vez desde Excel para la web, sin colisiones de coautoría, con auditoría por correo y Office Scripts.

### Agregado

- **Libro colaborativo** `src/M-INV_V2_Colaborativo.xlsx` (Core con demo) y
  `releases/M-INV_V2_Colaborativo_Produccion.xlsx` (Release), generados por `tools/build_minv_v2.py` (`tools/minv2/`).
- **Escritura fragmentada**: `10A_ENTRADAS` (Bodega: entrada, saldo inicial, ajustes) y `10B_SALIDAS` (Ventas). En cada
  una, **captura por usuario** (una fila por persona según `02_USUARIOS`, con validación y disponible exacto en vivo) y
  **bitácora oficial** que solo escriben los scripts (`Table.addRow`, ID sin contador compartido, `Usuario_O365` y
  `Timestamp` en columnas ocultas).
- **Poka-yoke**: una salida (o ajuste negativo) mayor que el disponible se tiñe de rojo sangre con texto blanco tachado
  y el script bloquea la consolidación; si otra persona se adelanta, el registro propio queda «✖ Rechazado» y no suma.
- **Lectura a demanda**: `15_STOCK` y `16_ALERTAS` son instantáneas de valores que reconstruye `RecalcularStock.ts`;
  las portadas muestran quién y cuándo calculó y cuántos movimientos hay después.
- **Portadas por rol**: `00_PORTADA_BODEGA` (registrar entrada, registrar ajuste, alertas de stock crítico) y
  `00_PORTADA_VENTAS` (registrar salida, consultar disponibilidad), con indicadores, gráficos y últimos movimientos.
- **Control de acceso**: `02_USUARIOS` (correo de Microsoft 365 y rol ADMIN/BODEGA/VENTAS/CONSULTA) validado por los
  scripts; «Permitir editar rangos» en las capturas (contraseña opcional por rol); guía de SharePoint.
- **Office Scripts** (`src/office-scripts/`): `RegistrarEntrada`, `RegistrarSalida`, `RecalcularStock` y
  `DiagnosticoInstalacion`, con bloque común (`lib/comun.ts`) sincronizado por `tools/office_scripts.py`, que también
  genera las versiones instalables con la contraseña (`build/office-scripts/`).
- **Pruebas sin Excel**: simulador de la API `ExcelScript` y 12 pruebas que ejecutan los scripts reales
  (`tests/office-scripts/`), incluida la paridad de `RecalcularStock` con el generador.
- **Verificación en Excel** `tools/verify_minv_v2.ps1` y ciclo completo `tools/build_v2.ps1`.
- Documentación: `.claude/v2-concurrency-rules.md`, `docs/deployment/sharepoint-rbac-policies.md`,
  `docs/architecture/data-dictionary-v2.md`, `src/office-scripts/README.md`.

### Cambiado

- `tools/minv/master.py`: la tabla de catálogo y proveedores se separa de su decoración para reutilizarla en la V2.
- `tools/minv/postprocess.py`: nueva guarda que aborta el build si una fórmula tiene paréntesis o llaves desbalanceados
  (Excel rechazaba el libro completo).

### Decisiones

- La especificación pedía cálculo **Manual** para 15_STOCK: en Excel el modo de cálculo es por libro y por sesión (no por
  hoja) y congelaría el disponible y el poka-yoke. Se implementó la instantánea a demanda, que cumple el objetivo sin ese
  costo; `RecalcularStock` además dispara un recálculo completo.
- «Permitir editar rangos» no admite correos en Excel para la web: el correo lo valida el script contra `02_USUARIOS` y
  los rangos admiten contraseña por rol.

## [1.2.0] · 2026-09-25 · rama `Inventario-V1.2`

Tema de la versión: **más funciones y un uso más intuitivo para todos los perfiles** (bodega, compras, gerencia y
administración), sin romper el modelo CQRS.

### Agregado

- **Dos ediciones** generadas desde el mismo código:
  - **Estándar** (`.xlsx`, sin macros): todo lo nuevo de lectura y consulta.
  - **Plus** (`.xlsm`): formulario guiado, sellado de la bitácora, ajustes automáticos del conteo y atajos de doble clic.
- **Portada renovada**: asistente **«Próximo paso»** que indica qué hacer ahora (errores, catálogo vacío, saldo
  inicial, conteo en curso, agotados, reposición, inventario sin rotación) con un botón que lleva al lugar exacto;
  8 mosaicos con iconos (Registrar, Consultar, Stock, Alertas, Pedido, Conteo, Catálogo, Guía); tarjeta
  **Sin rotación**; indicador de integridad de la bitácora.
- **Barra de navegación** con iconos en todas las hojas visibles (la hoja activa resaltada).
- **`04_PROVEEDORES`** (maestro nuevo): contacto, teléfono, correo, días de entrega; productos y alertas por proveedor.
  `05_PRODUCTOS` gana la columna `Proveedor` (lista validada).
- **`12_REGISTRO`** (Plus): formulario guiado con búsqueda por texto, vista previa del stock antes → después, semáforo
  antes → después y 7 validaciones en vivo. El botón **REGISTRAR** escribe en la bitácora, comprueba el `Estado` y
  sella la fila; conserva tipo y responsable para registrar varios movimientos seguidos.
- **`13_CONTEO`** (toma física): conteo contra sistema, diferencia en unidades y en dinero, resultado
  (cuadra / sobrante / faltante) y ajuste sugerido. En Plus, **Generar ajustes** registra y sella los `AJUSTE (+/-)`.
- **`17_KARDEX`** (consulta de producto): búsqueda por texto, ficha (stock, estado, valor, último movimiento, días sin
  movimiento), gráfico de evolución del saldo e historial de los últimos 100 movimientos.
- **`18_PEDIDO`** (pedido sugerido de compra): agrupado por proveedor y priorizado, cantidad a pedir hasta el máximo,
  costo y subtotal; filtro por proveedor con contacto y fecha estimada de entrega; listo para imprimir.
- **`99_AYUDA`**: «¿Qué necesita hacer hoy?» por perfil y lista de **primeros pasos** que se marca sola.
- `15_STOCK`: columnas `Proveedor` y `DiasSinMov` (inventario inmovilizado en ámbar); `16_ALERTAS`: columna `Proveedor`.
- Parámetros `cfgDiasSinRotacion` (60 días), `cfgEdicion` y `cfgSelladoHasta`.
- Edición Plus: **sellado** de la bitácora (las filas registradas quedan bloqueadas y en gris al registrar desde el
  formulario, al generar ajustes y al guardar), **doble clic** (alerta o pedido → formulario de reposición con la
  cantidad sugerida; stock o bitácora → consulta del producto) y aviso visible cuando las macros están deshabilitadas.
- Herramientas: generador modular (`tools/minv/`), `tools/build_xlsm.ps1` generalizado (Core y Release, con 20+
  pruebas automáticas del VBA en Excel), `tools/build_all.ps1` (ciclo completo) y guarda del post-proceso que impide
  referencias estructuradas en formatos condicionales y validaciones.

### Cambiado

- `src/M-INV_V1_Core.xlsm` pasa de variante opcional (V1.1, solo modo app) a **edición Plus** completa; se agrega
  `releases/M-INV_V1_Produccion_Bloqueado.xlsm`.
- Los fuentes VBA (`src/macros/`) pasan a UTF-8; el script los instala con `AddFromString`.
- Datos demo: 6 proveedores ficticios y 2 productos de baja rotación.

### Corregido

- Excel rechazaba el libro completo cuando un formato condicional usaba referencias estructuradas (`tblStock[...]`):
  se reemplazaron por nombres definidos y el generador ahora lo impide (regla R-08).

## [1.1.0] · rama `Inventario-V1`

- Variante `.xlsm` con modo app: la barra de fórmulas se oculta en la portada y se restaura al salir (probado en Excel).

## [1.0.0] · rama `Inventario-V1`

- Primera versión: libro CQRS (bitácora append-only → stock → alertas → tablero), Core con datos demo y Release
  blindado, generador, verificador en Excel real y documentación.
