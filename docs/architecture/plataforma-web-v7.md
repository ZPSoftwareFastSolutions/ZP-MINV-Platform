# Plataforma web · M-INV V7 (diseño)

> Rama `Inventario-V7` (sobre `Inventario-V6`). Versión **7.0.0-alpha.1**. Reglas normativas: `.claude/v7-web-platform-rules.md`
> (P-01 a P-14). Plan y avance: `docs/product/plan-v7.md`.

## 1. Qué cambia

| Tema | V6 | V7 |
|---|---|---|
| Tienda | Catálogo + «Armá tu PC» + reservar un armado | + **carrito de compras**: reservar cualquier producto (un solo artículo o varios) |
| Reserva | Nombre, teléfono, correo opcional | + CI/NIT opcional para la factura, + días para recogerla (1 a 3), + **correo automático** con el código y el detalle |
| Acceso | La web no tiene cuentas | Botón **Ingresar**: inicio de sesión y registro; con credenciales incorrectas no se pasa |
| Personal | Solo el escritorio | **Panel web por rol** con las funciones del escritorio, separado por módulos |
| Clientes | Consultan su reserva con número + teléfono | + **cuenta de cliente**: mis reservas y mis datos |
| Escritorio | Armador de PC con reservas web | + pantalla **Reservas** (carritos y armados), reenviar correo, tablero simplificado, más filtros y exportaciones |

## 2. Arquitectura

```text
 Navegador ──https──▶ túnel ──▶ nginx del catálogo (un solo origen)
                                  ├─ /                      la web (SPA: tienda + panel)
                                  ├─ /storefront/        ─▶ MINV.ApiGateway  (tienda pública, principal técnico tienda-web)
                                  └─ /api/v1/web/        ─▶ MINV.CloudServer (sesión web por cookie + RPC)
 Escritorio M-INV.exe ──http://localhost:5080/api/v1/{session,rpc}──▶ MINV.CloudServer (token Bearer, sin cambios)

 MINV.ApiGateway  ── trabajos: webhooks, vistas de reportes, vencimiento de reservas, **correo saliente**
 MINV.CloudServer ── trabajos: facturación SIAT
 PostgreSQL (la misma base para todo): 157 tablas en 10 esquemas
```

- La web del panel es **otro cliente del servidor en la nube**: envía los MISMOS comandos y consultas de `MINV.Application`
  por RPC. Permisos, módulo, sucursal, validación y auditoría los decide el servidor en cada petición (B-01, B-10).
- El nginx publica SOLO `/storefront/` y `/api/v1/web/`. Las rutas del escritorio (`/api/v1/session/*`, `/api/v1/rpc`),
  `/docs` y la API B2B `/v1` no salen a internet.

## 3. Sesión web (MINV.CloudServer)

Sección de configuración `Minv:Web` (`WebSettings`): `Enabled` (falso por defecto), `TenantCode`, `BranchCode` (sucursal de
las cuentas de cliente; por defecto la del almacén principal), `RegistrationsPerHour` (5), `CookieName` (`minv_session`).

| Ruta | Cuerpo | Respuesta |
|---|---|---|
| `POST /api/v1/web/session/login` | `{ email, password }` | 200 `WebSession` + cookie · 401 `authentication` · 429 |
| `GET /api/v1/web/session` | — | 200 `WebSession` · 401 |
| `POST /api/v1/web/session/logout` | — | 204 (borra la cookie) |
| `POST /api/v1/web/account/register` | `{ name, email, phone, password }` | 201 `WebSession` + cookie · 400 · 422 `account.email_taken` · 429 |
| `POST /api/v1/web/rpc` | `RpcRequest` | `RpcResponse` (mismo contrato que `/api/v1/rpc`) |

`WebSession` = `{ displayName, email, roles[], permissions[], mustChangePassword, access (BranchAccess), kind ("staff" |
"customer"), expiresAt, serverVersion, company }`. **Nunca** lleva el token.

Reglas de la sesión:

1. El token de sesión (`mses_…`, 256 bits, en la base solo su SHA-256) viaja en una cookie `HttpOnly`, `SameSite=Strict`,
   `Path=/api/v1/web`, `Secure` cuando la petición llegó por https (`X-Forwarded-Proto`). El JavaScript de la página no
   puede leerlo. Vence a las 12 h sin actividad (igual que el escritorio).
2. Anti-CSRF: toda ruta `/api/v1/web/*` que no sea GET exige la cabecera `X-MINV-Client-Version` (misma versión mayor) y,
   si el navegador envía `Sec-Fetch-Site`, que sea `same-origin`; si envía `Origin`, que su host sea el de la petición.
   El servidor en la nube no publica CORS.
3. La empresa NO la escribe el usuario: sale de `Minv:Web:TenantCode`.
4. Mensaje único ante cualquier falla de inicio de sesión («Correo o contraseña incorrectos»); bloqueo de la cuenta a los
   5 intentos (15 min); 10 inicios por minuto por IP; 5 registros por hora por IP. La IP real sale de `X-Forwarded-For`
   solo desde redes privadas conocidas (`Minv:ForwardedHeaders`, igual que el gateway).
5. `kind = customer` cuando el ÚNICO rol del usuario es `CLIENTE`. Una sesión de cliente solo puede ejecutar por RPC
   (también por la ruta del escritorio) los casos de uso cuyos permisos empiezan con `account.` y los de su propia sesión
   (`ChangePasswordCommand`, `LogoutCommand`). Defensa en profundidad: la tubería de permisos sigue aplicando.
6. Canal de auditoría `web` (`RequestChannels.Web`).

## 4. Cuentas de cliente

- Rol nuevo `CLIENTE` («Cliente web») con los permisos `account.manage` y `account.reserve`.
- `sales.customer_accounts` (`CustomerAccount`): une un usuario (`iam.users`) con un cliente (`sales.customers`), 1 a 1.
- `RegisterCustomerAccountCommand` (previo a la sesión, como `LoginCommand`; no viaja por RPC): crea usuario, credencial
  (PBKDF2), rol `CLIENTE`, asignación a la sucursal de la tienda, cliente `WEB-000001` y la cuenta, en UNA transacción.
  Contraseña: 8 a 128 caracteres con letras y números. Correo ya registrado: `account.email_taken`.
- Casos de uso de la cuenta (namespace `MINV.Application.Accounts`):

| Caso de uso | Permiso | Qué hace |
|---|---|---|
| `GetMyAccountQuery` → `MyAccountView` | `account.manage` | Nombre, correo, teléfono y documento del cliente de la sesión |
| `UpdateMyAccountCommand(Name, Phone, DocumentType?, DocumentNumber?, Complement?)` | `account.manage` | Actualiza SUS datos |
| `GetMyReservationsQuery` → `IReadOnlyList<StorefrontReservationView>` | `account.manage` | Sus reservas (por `CustomerId`) |
| `CancelMyReservationCommand(Number)` | `account.manage` | Libera SU reserva |
| `CreateMyReservationCommand(Lines, Kind, HoldDays?, Notes?, Name?)` | `account.reserve` | Reserva con los datos de su cuenta; queda ligada a su cliente |

- El personal NO se registra solo: lo crea el Administrador (Usuarios). Un cliente que olvidó su contraseña la pide en la
  tienda (el Administrador la restablece). Límite conocido: sin verificación de correo ni recuperación por correo.

## 5. Carrito y reservas

Decisión: **reutilizar el agregado `PcBuild`** con un tipo (opción A del análisis), en vez de un agregado nuevo o de
`SalesOrder`: conserva reserva todo-o-nada, idempotencia, vencimiento, cancelación, venta que consume la reserva, bitácora
y eventos ya probados, sin tocar `SaleWriter` ni el arco de `StockReservation`.

| Cambio | Detalle |
|---|---|
| `PcBuild.Kind` | `enum PcBuildKind { Build, Cart }` → `sales.pc_builds.kind` (texto ≤ 10, `CHECK`), por defecto `Build` |
| Líneas de carrito | `PcBuildLine.Slot` admite nulo SOLO en un carrito; en un armado sigue siendo obligatorio (dominio + trigger) |
| Ranura única | No aplica a un carrito (se pueden reservar dos monitores distintos o dos fuentes) |
| Compatibilidad | Solo se evalúa en armados (T-06); un carrito se cotiza con el informe vacío |
| Numeración | Web: `RES-WEB-000001` · escritorio: `RES-<sucursal>-000001` · los armados conservan `ARM-…` |
| Datos para la factura | `BuyerDocumentType`, `BuyerDocumentNumber`, `BuyerComplement`, `BuyerName` (opcionales, instantánea del visitante; mismas reglas del SIN que `Customer.SetFiscalIdentity`); la caja los precarga al cobrar |
| Plazo para recoger | `HoldDays` 1 a 3 (24, 48 o 72 h); sin valor, `Minv:Storefront:ReservationHours` (48); tope `MaxReservationHours` (72) |
| Texto libre | Nombre, notas y razón social rechazan caracteres de control |

Contrato público (compatible: solo campos opcionales nuevos) de `POST /storefront/v1/reservations`:
`kind` (`"build"` por defecto | `"cart"`), `holdDays`, `buyer { documentType, documentNumber, complement, name }`. La
respuesta agrega `kind` y `mailQueued`. El catálogo agrega `reservationHours` y `maxHoldDays`.

Escritorio y panel: `SavePcBuildCommand`/`GetPcBuildsQuery`/`PcBuildRow` llevan el tipo; `SellPcBuildCommand` vende
también carritos (precio congelado, series al cobrar).

## 6. Correo de la reserva

Regla B-08: el correo **no** se envía dentro del caso de uso. Se encola en la misma transacción y sale después del COMMIT.

| Tabla (`integration`) | Entidad | Tipo | Contenido |
|---|---|---|---|
| `outgoing_mails` | `OutgoingMail` | hecho, append-only, de sucursal | `kind` (`ReservationConfirmed`), `pc_build_id`, `recipient`, `requested_at`, `requested_by_user_id` |
| `outgoing_mail_dispatch` | `OutgoingMailDispatch` | cola mutable 1:1 | `status` (`Pending`, `Sent`, `Exhausted`, `Cancelled`), `attempts` (0 a 5), `next_attempt_at`, `completed_at`, `last_error` |
| `outgoing_mail_attempts` | `OutgoingMailAttempt` | bitácora append-only | `attempt`, `succeeded`, `error`, `attempted_at`, `duration_ms` |

- No se guarda asunto ni cuerpo: se derivan al enviar desde la reserva (precios ya congelados).
- `ReservationMail.Enqueue(...)` se llama al reservar (tienda, cuenta de cliente y escritorio) si hay correo. Una repetición
  idempotente no vuelve a encolar. `ResendReservationMailCommand(Number, Email?)` (`sales.pcbuild.manage`) encola otro.
- `MailDispatcher.RunOnceAsync` (en `MINV.Infrastructure/Integration`) reclama con `integration.claim_outgoing_mails`
  (`SECURITY DEFINER`, `FOR UPDATE SKIP LOCKED`), envía, registra el intento y reprograma: inmediato, 1 min, 5 min, 30 min,
  2 h. Si la reserva ya no está `Reserved`, la cola queda `Cancelled`. Lo hospeda `MailDispatcherService` del gateway.
- Servidor SMTP: el de la empresa (`billing.mail_settings`, si está activo) o, si no, el del servidor (`Minv:Mail`:
  `Enabled`, `Host`, `Port`, `UseSsl`, `UserName`, `Password`, `FromAddress`, `FromName`, `PublicUrl`). Gmail:
  `smtp.gmail.com:587` con STARTTLS y una **contraseña de aplicación** que solo puede generar el dueño de la cuenta.
- Plantilla fija en el código, con alternativa de texto plano; todo valor variable va codificado (HTML) y el asunto solo
  lleva datos del servidor (número de reserva y nombre de la empresa). No incluye notas ni texto libre del cliente, ni el
  teléfono completo.
- Abuso: 10 reservas por minuto por IP (ya existe), máximo 3 correos por destinatario y 300 por empresa cada 24 h.
- Docker: perfil `correo-prueba` con un buzón de prueba (interfaz en `127.0.0.1:8025`, nunca por el túnel).

## 7. La web

```text
src/
  1-domain/        auth/ (sesión, roles, permisos) · cart/ (carrito puro) · ports/ (ISessionGateway, IRpcGateway, ICartStore)
  2-application/   auth/ · cart/ · panel/ (navegación por permiso, formato, exportar CSV)
  3-infrastructure/
    http/          api.ts (tienda) · webApi.ts (sesión y RPC, único otro lugar con fetch) · contract.generated.ts
    storage/       cartStorage.ts (único lugar con localStorage: solo el carrito, nunca datos de sesión)
  4-presentation/
    pages/auth/    LoginPage, RegisterPage            pages/cart/   CartPage, CheckoutPage (reserva)
    pages/account/ MiCuenta (reservas, datos, contraseña)
    panel/         shell/ (menú por rol, sucursal, usuario) · kit/ (tabla, filtros, formularios, plegables)
                   modules/<módulo>/module.tsx  ← cada módulo se registra solo (import.meta.glob)
```

- `contract.generated.ts` lo genera `minv contrato-web` por reflexión sobre `RpcCatalog`: tipos de cada petición y
  respuesta, nombre completo, si es comando y sus permisos. Una prueba falla si el archivo quedó desactualizado.
- El panel se descarga aparte (fragmento propio): quien solo visita la tienda no lo baja.
- Rutas: `/ingresar`, `/registrarse`, `/carrito`, `/reservar`, `/mi-cuenta/*`, `/panel/*`. Las rutas protegidas exigen
  sesión; sin sesión redirigen a `/ingresar`. Un cliente no entra a `/panel`; el personal no entra a `/mi-cuenta`.

### Módulos del panel (mismas secciones que el escritorio)

| Sección | Módulo | Permiso que lo muestra |
|---|---|---|
| General | Inicio (tablero) | sesión |
| Ventas | Caja · Ventas · Clientes · Reservas | `sales.pos.operate` · `sales.view` · `sales.customers.manage` · `sales.pcbuild.manage` |
| Tecnología | Armador de PC · Series · Garantías | `sales.view` + `inventory.stock.view` · `inventory.serials.view` |
| Inventario | Stock · Catálogo · Movimientos · Toma física · Alertas | `inventory.stock.view` · `catalog.manage` · `inventory.movements.*` · `inventory.counts.record` |
| Compras | Pedido sugerido · Órdenes de compra · Proveedores | `inventory.stock.view` · `purchasing.manage` |
| Sucursales | Sucursales · Transferencias | `reports.view`/`corporate.branches.*` · `inventory.transfers.manage` |
| Facturación | Documentos · Estado del SIAT · Homologación · Libros | `billing.view` |
| Análisis | Reportes · Contabilidad | `reports.view` · `accounting.manage` |
| Administración | Usuarios · Integraciones · Configuración · Actividad | `iam.users.manage` · `integration.manage` · `billing.configure` · `iam.audit.view` |

### Tablero y pantallas

- **Inicio**: saludo, sucursal activa y **botones grandes** con las funciones del rol. Las estadísticas están plegadas
  detrás de «Ver estadísticas» y se cargan solo al abrirlas. Nada de gráficos «de golpe».
- **Cada pantalla**: título, botones de acción, barra de filtros con **listas desplegables** (sucursal, categoría, marca,
  estado, canal, fechas) y búsqueda, tabla ordenable y paginada, acciones por fila, detalle en un panel lateral, exportar
  CSV, estados de carga, vacío y error con «Reintentar».
- Un botón que el rol no puede usar no se muestra; si el servidor rechaza, el aviso dice qué permiso falta.

## 8. Escritorio

- Pantalla **Reservas** (Ventas): carritos y armados reservados, filtros por tipo, canal, estado, fechas y texto; acciones
  Vender en caja, Liberar, Reenviar correo, Copiar teléfono, Nueva reserva (carrito en mostrador).
- **Inicio** simplificado: accesos como botones por rol; indicadores dentro de secciones plegables, cerradas al entrar.
- Filtros con listas desplegables y exportación a CSV en las listas principales; cola de correos en Administración.
- Usuarios: filtro Personal / Clientes web.

## 9. Seguridad

| Riesgo | Medida |
|---|---|
| Robo del token por XSS | Cookie `HttpOnly`; `Content-Security-Policy` estricta (solo `'self'`); React escapa el texto |
| CSRF | `SameSite=Strict` + cabecera propia obligatoria + `Sec-Fetch-Site`/`Origin` |
| Fuerza bruta | Bloqueo a los 5 intentos, 10 inicios/min por IP real, mensaje único |
| Escalada de privilegios | Permisos recalculados en el servidor en cada petición; lista de permitidos para clientes; registro siempre con rol `CLIENTE` |
| Datos de otros clientes | Los casos de uso `account.*` operan SOLO sobre el cliente de la sesión |
| Correo como medio de spam | Plantilla fija, topes por destinatario y por empresa, un correo por reserva |
| Inyección en correo | Asunto sin datos del cliente, valores codificados, sin caracteres de control |
| Clickjacking y fugas | `frame-ancestors 'none'`, `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy` |
| Secretos | Contraseña SMTP solo en `deploy/.env` (ignorado por Git) o cifrada en la base; nunca en registros ni auditoría |
| Superficie pública | El túnel publica solo la web, `/storefront/` y `/api/v1/web/` |

## 10. Base de datos

Migración `V7WebPlatform`: 153 → **157 tablas** (4 nuevas), mismos 10 esquemas.

| Cambio | Tabla |
|---|---|
| Nueva | `sales.customer_accounts` · `integration.outgoing_mails` · `integration.outgoing_mail_dispatch` · `integration.outgoing_mail_attempts` |
| Columnas | `sales.pc_builds`: `kind`, `buyer_document_type`, `buyer_document_number`, `buyer_complement`, `buyer_name` |
| Cambia | `sales.pc_build_lines.slot` admite nulo (solo carritos: trigger) · `iam.audit_logs.channel` admite `web` |
| Función | `integration.claim_outgoing_mails(integer, integer)` |
| Datos | Rol `CLIENTE`, permisos `account.manage` y `account.reserve` en las empresas existentes |

Comprobación de normalización: `docs/database/normalizacion-v7.md` (método, hallazgos y redundancias controladas) y
`scripts/verificar_normalizacion.sql` (consultas sobre `pg_catalog` para repetirla contra cualquier base).

## 11. Límites conocidos

- Sin pagos en línea: la reserva se cobra en la tienda. Sin envío a domicilio.
- Sin verificación de correo al registrarse ni recuperación de contraseña por correo.
- El enlace de trycloudflare cambia al reiniciar el túnel y depende de que el equipo esté encendido.
- La caja web imprime con el navegador; no maneja la impresora ESC/POS ni el cajón (eso sigue en el escritorio).
