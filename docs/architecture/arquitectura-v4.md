# Arquitectura · M-INV V4 (multi-sucursal en la nube) · 4.0.0-alpha.1

La **V4** lleva M-INV de una base local de una sola sucursal a una **plataforma multi-sucursal en la nube**: cada
sucursal ve y opera solo lo suyo, la mercadería viaja entre sucursales con **transferencias en tránsito**, el
escritorio puede trabajar **contra un servidor en internet** sin tener credenciales de la base, y terceros (e-commerce,
ERP) se integran por un **API Gateway B2B** con API Keys y **webhooks firmados**. Rama `Inventario-V4.-BaseDeDatosNube`,
construida sobre `Inventario-V3.-BaseDeDatosLocal`.

Documentos relacionados: reglas normativas [`.claude/v4-architecture-rules.md`](../../.claude/v4-architecture-rules.md) ·
despliegue [`docs/deployment/despliegue-nube-v4.md`](../deployment/despliegue-nube-v4.md) · guía del integrador
[`docs/integration/api-gateway-v1.md`](../integration/api-gateway-v1.md) · modelo de datos
[`docs/database/ERD-MINV-V3.md`](../database/ERD-MINV-V3.md) (V3 y V4) · paso a paso
[`docs/deployment/inicio-rapido-v4.md`](../deployment/inicio-rapido-v4.md).

## 0. La V4 en una imagen

```text
  ESCRITORIO M-INV.exe (WPF)                                  TERCEROS B2B (tienda en línea, ERP contable)
  ┌──────────────────────────────┐                            ┌──────────────────────────────┐
  │ modo «Base local»  ──────────┼── Npgsql (rol minv_app) ─┐ │ API Key minv_<prefijo>_<…>   │
  │ modo «Nube» (servidor M-INV) │                          │ └──────────────┬───────────────┘
  │ modo «Demostración» (memoria)│                          │                │ HTTPS /v1/…  (JSON, ProblemDetails)
  └──────────────┬───────────────┘                          │                ▼
                 │ HTTPS  POST /api/v1/rpc                   │ ┌──────────────────────────────────────────────┐
                 │ (token de sesión mses_…)                  │ │ MINV.ApiGateway (ASP.NET Core) · SOLO B2B     │
                 ▼                                           │ │ ApiKey → alcances → casos de uso              │
  ┌──────────────────────────────────────────┐              │ │ + despachador de webhooks (outbox, SKIP LOCKED)│
  │ MINV.CloudServer (ASP.NET Core)          │              │ │ + refresco del modelo de lectura (5 min)      │
  │ login · RPC de MediatR · idempotencia    │              │ └──────────────────────┬────────────────────────┘
  └──────────────────┬───────────────────────┘              │                        │
                     │   los MISMOS casos de uso (MINV.Application): validación → permisos/licencia → auditoría
                     ▼                                       ▼                        ▼
  ┌──────────────────────────────────────────────────────────────────────────────────────────────────────────┐
  │ MINV.Infrastructure · MinvWriteDbContext (OLTP)  ·  MinvReadDbContext (OLAP: esquema reporting)           │
  │ filtros globales por empresa y SUCURSAL · guardas de escritura · outbox en SaveChanges · xmin (OCC)       │
  └──────────────────┬───────────────────────────────────────────────────────────────┬───────────────────────┘
                     │ rol minv_server (NOBYPASSRLS, no dueño)                        │ MINV_DB_READ (opcional)
                     ▼                                                                ▼
  ┌──────────────────────────────────────────────────────────┐          ┌──────────────────────────────────┐
  │ PostgreSQL 15+ gestionado (DigitalOcean · AWS RDS ·      │ ───────► │ réplica de lectura (gerencia,    │
  │ Supabase): 110 tablas · 8 esquemas · RLS empresa+sucursal│ réplica  │ reportes; vistas materializadas) │
  │ SET minv.tenant_id / minv.branch_ids por conexión        │          └──────────────────────────────────┘
  └──────────────────────────────────────────────────────────┘
                     ▲ webhooks firmados (X-MINV-Signature) salen del gateway hacia las URL https de los terceros
```

El principio rector de la V1 sigue intacto: **CQRS y append-only**. El inventario cambia solo registrando hechos
inmutables; las correcciones son registros compensatorios; todo lo demás es proyección. La V4 extiende ese principio a
varias sucursales, a la red y a terceros.

## 1. Proyectos de la solución (`MINV.sln`)

| Capa | Proyecto | Qué cambia en la V4 |
|---|---|---|
| 1. Core | `MINV.Domain` | `IBranchScoped`, `IInterBranch`, `IDomainEvent`; transferencias rediseñadas (`StockTransfer`, líneas, manifiesto por lote, faltantes, bitácora); `ApiKey`, `WebhookEndpoint`, `OutboxEvent`, `OutboxDispatch`, `WebhookDelivery`; `ExternalOrder`, `ProcessedRequest`; eventos de dominio; cuentas 1.1.06 y 2.1.04; 4 módulos comerciales nuevos |
| 1. Core | `MINV.Application` | `BranchScope` y alcance en el login (`UserAccess`, `SelectBranchCommand`); casos de uso de sucursales (`Corporate/`), transferencias (`Inventory/Transfers/`), integraciones (`Integration/`); contrato RPC (`Remote/RpcContract.cs`); `IReportingReader`, `ISecretProtector`, `IRequestOrigin` |
| 2. Infrastructure | `MINV.Infrastructure` | `MINVDbContext` pasa a llamarse **`MinvWriteDbContext`**; nuevo **`MinvReadDbContext`**; filtros por sucursal, guardas, outbox, traducción de errores de PostgreSQL; autenticadores de API Key y de sesión en la nube; despachador de webhooks con protección SSRF; cifrado AES-256-GCM de secretos; arranque de servidores (`Hosting/ServerHosting.cs`); migración `V4MultiBranchCloud` |
| 3. Presentation | `MINV.DesktopClient` | Selector de conexión (base local, nube, demostración), selector de sucursal en la barra superior, pantallas Sucursales, Transferencias e Integraciones |
| 3. Presentation | **`MINV.CloudServer`** (nuevo) | Servidor HTTP del escritorio en modo nube: login, RPC de MediatR, logout, estado |
| 3. Presentation | **`MINV.ApiGateway`** (nuevo) | API REST B2B `/v1` con API Keys, alcances, límites, ProblemDetails, OpenAPI en `/docs`, servicios en segundo plano |
| 4. Tools | `MINV.Cli` | `minv datos-prueba` genera la empresa de prueba multi-sucursal |
| tests | **`MINV.Integration.Tests`** (nuevo) | Servidor en la nube y gateway REALES (Kestrel en loopback) sobre la base en memoria con la empresa de prueba |

## 2. Conceptos

### 2.1 Multi-sucursal: alcance, filtros y aislamiento

**Dos tipos de filas por sucursal** (`src/1. Core/MINV.Domain/Common/Entity.cs`):

| Interfaz | Columnas | Quién la ve | Ejemplos |
|---|---|---|---|
| `IBranchScoped` | `branch_id` | solo las sesiones cuyo alcance incluye esa sucursal | existencias, movimientos, posiciones, cajas, ventas, facturas, pagos, compras, asientos, costo promedio (35 tablas) |
| `IInterBranch` | `from_branch_id`, `to_branch_id` | el origen **y** el destino | transferencias, sus líneas, manifiesto por lote, faltantes y bitácora (5 tablas) |

Lo que **no** es de una sucursal sigue siendo de la empresa: el **directorio corporativo** (`warehouse.branches`,
`warehouse.warehouses`, `warehouse.branch_users`), el catálogo, clientes, proveedores, usuarios, roles, plan de
cuentas. Así cualquier sucursal puede elegir el almacén destino de una transferencia o vender cualquier producto del
catálogo, pero nunca ve el stock ni los documentos de otra.

**Alcance de la sesión** (`BranchScope` en `src/1. Core/MINV.Application/Abstractions/Ports.cs`): `AllBranches` (todas),
`BranchIds` (las visibles) y `ActiveBranchId` (la sucursal donde se registran las operaciones que no indican otra:
caja, movimientos, compras, asientos manuales). Lo calcula SIEMPRE el servidor (`Application/Iam/UserAccess.cs`):

1. Los roles dan los permisos (`iam.user_roles` → `iam.role_permissions`).
2. Con el permiso **`corporate.branches.all`** (roles ADMIN y GERENCIA) el usuario es **gerencia global**: ve todas las
   sucursales activas y puede elegir «Todas las sucursales» (vista consolidada, sin sucursal activa).
3. Sin ese permiso ve solo las sucursales asignadas en **`warehouse.branch_users`**; si no tiene ninguna, el inicio de
   sesión se rechaza («Su usuario no tiene sucursales asignadas») y queda registrado en `iam.access_logs`.
4. La sucursal activa de la sesión anterior se respeta si sigue permitida; si no, la primera asignada.
5. `SelectBranchCommand` cambia la sucursal activa (validada contra el alcance) y la guarda en `iam.sessions.active_branch_id`.

**Cuatro barreras independientes** aplican el alcance:

| Barrera | Dónde | Qué hace |
|---|---|---|
| Filtros globales de EF Core | `Persistence/ModelConventions.cs` | Un solo filtro por entidad: empresa y, según la interfaz, `AllBranches ∨ branch_id ∈ alcance` o `AllBranches ∨ from ∈ alcance ∨ to ∈ alcance` |
| Guardas de escritura | `Persistence/Interceptors/Interceptors.cs` (`MinvSaveChangesInterceptor`) | Toda fila nueva de sucursal cae dentro del alcance (`branch.outside_scope`); `branch_id`, `from_branch_id` y `to_branch_id` nunca cambian (`branch.immutable`) |
| FK compuestas con sucursal | `Persistence/Configurations/**` | Clave alterna `(tenant_id, branch_id, id)`; el hijo referencia `(tenant_id, branch_id, padre_id)`: una línea, un movimiento o una posición no pueden tener otra sucursal que su padre, y todo termina en el almacén (`warehouses.branch_id`) |
| Row Level Security | migración `V4MultiBranchCloud` | Política **RESTRICTIVA** `branch_isolation` (se suma, con AND, a `tenant_isolation`) en las 35 + 5 tablas, con `iam.branch_visible(uuid)` sobre la variable `minv.branch_ids` |

**Variables de sesión de PostgreSQL**: `TenantSessionInterceptor` ejecuta al ABRIR cada conexión
`set_config('minv.tenant_id', …, false)` y `set_config('minv.branch_ids', …, false)` (`*` = todas, lista de UUID
separada por comas, o vacío = ninguna). Son valores de **sesión** (no de transacción): con un pool externo como
PgBouncer o Supavisor **solo funciona el modo sesión** (en modo transacción, la siguiente sentencia podría ir a otra
conexión del servidor con otra empresa o sin ninguna). La conexión directa también sirve.

**Numeración por sucursal**: los documentos llevan el código de la sucursal (`F-CM-000001`, `F-EA-000001`,
`TR-CM-000012`, `PV-SC-000040`, `AS-EA-000003`). Una sucursal solo ve sus documentos, así que su numeración nunca
choca con la de otra; si dos cajas de la misma sucursal numeran a la vez, el índice único rechaza a una y el caso de uso
reintenta.

### 2.2 Transferencias entre sucursales (mercadería en tránsito)

Máquina de estados atómica pero **diferida** (`Domain/Inventory/StockTransfer.cs`):

```text
                  DispatchTransferCommand (ORIGEN)             ReceiveTransferCommand (DESTINO)
  ┌───────────┐   salidas TRASLADO (SALIDA) + manifiesto ┌─────────────┐  entradas TRASLADO (ENTRADA)   ┌───────────┐
  │ Pending   │ ───────────────────────────────────────► │ Dispatched  │ ─────────────────────────────► │ Received  │
  │ pendiente │   asiento del origen                     │ EN TRÁNSITO │  faltantes con motivo +        │ recibida  │
  └─────┬─────┘                                          └─────────────┘  asiento del destino           └───────────┘
        │ CancelTransferCommand (ORIGEN, con motivo; no movió stock)
        ▼
  ┌───────────┐     Cada transición: fila en inventory.stock_transfer_events (bitácora append-only)
  │ Cancelled │     y evento de dominio (transfer.dispatched / transfer.received / transfer.discrepancy)
  └───────────┘
```

| Paso | Lo hace | Reglas |
|---|---|---|
| Crear (`CreateTransferCommand`) | el **origen** (almacén de la sucursal activa, o el indicado si es del alcance) | una línea por producto, cantidad > 0 y según la unidad (decimales solo si la unidad los admite); almacén destino distinto del de origen, activo y de una sucursal activa; no mueve stock |
| Despachar (`DispatchTransferCommand`) | el **origen** | cada línea sale **completa** (una o varias salidas: primero los lotes que vencen antes, luego las posiciones con más disponible); el poka-yoke del dominio impide el negativo; costo = promedio del almacén de origen; manifiesto por lote (`stock_transfer_line_batches`) |
| Recibir (`ReceiveTransferCommand`) | el **destino** | de una vez, todas las líneas; sin líneas = todo llegó; entra con los MISMOS lotes del manifiesto, nunca más de lo despachado (ni por lote); la diferencia exige motivo y queda como **faltante** (`stock_transfer_discrepancies`, delta compensatorio); recalcula el costo promedio del destino |
| Anular (`CancelTransferCommand`) | el **origen** | solo pendiente (una despachada ya movió stock y debe recibirse) |

La gerencia global ve ambos lados y puede hacer los dos papeles. El despacho y la recepción corren en **una transacción
explícita** (`IMinvDbContext.BeginTransactionAsync`) con **reintento optimista ×3**: si otra sesión vendió el mismo
stock o despachó/anuló la misma transferencia, la más lenta relee y reintenta; la tercera falla se informa.

**Conservación** (vista `inventory.v_transfer_breaches`, debe estar vacía): por línea, Σ salidas = cantidad = Σ
manifiesto; si está recibida, Σ entradas + Σ faltantes = cantidad; si está pendiente o anulada, sin movimientos. En el
consolidado: **stock total = Σ sucursales + en tránsito**, contando lo que viaja UNA sola vez (ya salió del origen y
todavía no entró al destino; `ConsolidatedStockQuery` en `Application/Corporate/BranchUseCases.cs`).

**Contabilidad** (`Domain/Accounting/ChartOfAccounts.cs`): dos cuentas recíprocas nuevas.

| Momento | Sucursal | Debe | Haber |
|---|---|---|---|
| Despacho | origen | **1.1.06** Mercadería enviada a sucursales (valor despachado) | **1.1.05** Inventario de mercaderías |
| Recepción | destino | **1.1.05** Inventario (valor recibido) + **5.1.09** Mermas y ajustes de inventario (valor del faltante) | **2.1.04** Mercadería recibida de sucursales (valor despachado) |

En el consolidado, **1.1.06 − 2.1.04 = valor de la mercadería en tránsito** (cero cuando todo se recibió). Ejemplo
numérico completo en el §4.3.

### 2.3 Concurrencia optimista (OCC)

- Toda tabla mutable importante implementa `IConcurrencyAware`: `RowVersion` se mapea a la columna de sistema **`xmin`**
  de PostgreSQL (no ocupa espacio ni se migra). Transferencias, existencias, sesiones, API Keys, webhooks y la cola de
  despacho la usan: dos sucursales que despachan o reciben la misma transferencia a la vez no pueden hacerlo dos veces.
- **Costo promedio con secuencia**: `accounting.average_cost_history` es append-only y el costo vigente es la fila de
  mayor `sequence` por (variante, almacén), con índice único `(tenant_id, variant_id, warehouse_id, sequence)`. Si una
  compra y una transferencia llegan al mismo almacén a la vez y calculan el promedio sobre el mismo estado, las dos
  intentan escribir la misma secuencia: la segunda choca con el índice, se deshace y reintenta con el promedio real.
- **Base en memoria** (demostración y pruebas): no existe `xmin`, así que `MinvWriteDbContext` incrementa la versión de
  cada fila modificada antes de guardar; las pruebas de OCC corren sin PostgreSQL.
- **Traducción de errores** (`Persistence/PostgresErrors.cs`):

| SQLSTATE | Significado | Se convierte en | HTTP |
|---|---|---|---|
| 23505 | unicidad | `ConcurrencyConflictException` (el caso de uso reintenta) | 409 |
| 40001 / 40P01 | serialización / interbloqueo | `ConcurrencyConflictException` | 409 |
| 23503 | FK (dato inexistente o de otra empresa/sucursal) | `DomainException("db.foreign_key")` | 422 |
| 23514 | CHECK | `DomainException("db.check")` | 422 |
| P0001 | regla de un trigger | `DomainException("db.rule")` | 422 |
| 42501 | RLS / privilegio | `AccessDeniedException` | 403 |

### 2.4 Append-only y deltas compensatorios

Los libros mayores nunca se actualizan ni se borran (interceptor de EF Core + triggers `trg_append_only` + privilegios
revocados a `minv_app` y `minv_server`). La V4 suma 8 a los 7 de la V3 (**15 en total**):

| V3 | V4 (nuevos) |
|---|---|
| `inventory.stock_movements`, `iam.audit_logs`, `iam.access_logs`, `sales.cash_movements`, `sales.payments`, `accounting.exchange_rates`, `accounting.average_cost_history` | `inventory.stock_transfer_movements`, `inventory.stock_transfer_discrepancies`, `inventory.stock_transfer_events`, `inventory.stock_transfer_line_batches`, `integration.outbox_events`, `integration.webhook_deliveries`, `sales.external_orders`, `iam.processed_requests` |

Un faltante no «edita» la transferencia: es un **delta compensatorio** (lo recibido se deriva: despachado − faltantes,
por eso no hay columna «recibido»). Cada reintento de un webhook es una fila nueva. La única tabla mutable de la
integración es la cola `integration.outbox_dispatch`.

### 2.5 Outbox transaccional y webhooks

```text
 caso de uso ──► agregado.DomainEvents / db.Publish(evento)
                      │  SaveChanges (MISMA transacción que el cambio)
                      ▼
      integration.outbox_events (append-only, payload jsonb)  +  integration.outbox_dispatch (Pending, next_attempt_at)
                      │
   API Gateway · WebhookDispatcherService (cada 5 s si no hay trabajo; lotes de 50)
                      │  integration.claim_deliveries(lote, 120 s)  ← FOR UPDATE SKIP LOCKED (varias réplicas, sin duplicar)
                      ▼
   por evento, en el contexto de SU empresa: webhooks activos suscritos (y de su sucursal) que aún no confirmaron
                      │  POST JSON firmado · SafeWebhookHttp (solo IP públicas, sin redirecciones, 10 s)
                      ▼
   integration.webhook_deliveries (un intento = una fila)  →  outbox_dispatch: Completed | reprogramado | Exhausted
```

- **Eventos** (`Domain/Integration/Webhooks.cs`, `IntegrationEvents`): `sale.completed`, `sale.voided`,
  `purchase.received`, `transfer.dispatched`, `transfer.received`, `transfer.discrepancy`.
- **Sobre** del cuerpo: `{ id, type, occurredAt, tenantId, branchId, data }` (`id` = id del evento, estable entre
  reintentos: el receptor deduplica con él).
- **Cabeceras**: `X-MINV-Signature: t=<unix>,v1=<hex>` (HMAC-SHA256 del texto `t + "." + cuerpo` con el secreto del
  destino; durante la rotación del secreto van **dos** `v1=`), `X-MINV-Event`, `X-MINV-Delivery` (`<id sin guiones>-<intento>`),
  `User-Agent: M-INV-Webhooks/4.0`.
- **Reintentos**: 8 rondas por evento; espera antes de cada una: inmediata, 1 min, 5 min, 30 min, 2 h, 6 h, 12 h y
  24 h. Éxito = cualquier 2xx; una redirección cuenta como falla. Tras 8 intentos el evento queda `Exhausted`.
- **Secretos**: 32 bytes aleatorios (`whsec_…`), se muestran UNA vez y se guardan **cifrados con AES-256-GCM**
  (`Infrastructure/Services/IntegrationServices.cs`) con una clave maestra que NO está en la base: variable
  `MINV_INTEGRATION_KEYS` = `id1:base64(32 bytes);id2:…` (la primera cifra; todas descifran; el id de la clave va como
  dato asociado del cifrado). Si la base se filtra, no se pueden falsificar webhooks.
- **Rotación**: `RotateWebhookSecretCommand` genera otro secreto; el anterior sigue firmando 24 h (doble firma).
- Un webhook solo recibe eventos ocurridos **después** de registrarlo (no se reenvía la historia) y, si se registró
  para una sucursal, solo los de esa sucursal. Máximo 20 webhooks activos por empresa.

### 2.6 API Keys y alcances

- Formato `minv_<prefijo 8>_<secreto 43>`: 32 bytes aleatorios en base64url (256 bits). En la base solo quedan el
  **prefijo** (público, único en toda la plataforma, para encontrar la llave) y el **SHA-256** del token completo (con
  256 bits de azar no hace falta sal ni KDF lento). La comparación es en tiempo constante. El token se muestra una vez.
- Una llave **actúa en nombre de su dueño** (quien la creó): permisos efectivos = permisos del dueño **∩** permisos
  que conceden sus alcances (`ApiScopes.EffectivePermissions`). Si el dueño pierde un permiso o se desactiva, la llave
  pierde lo mismo en la siguiente petición.
- Alcances (`Domain/Integration/ApiKey.cs`):

| Alcance | Permite | Permisos que concede |
|---|---|---|
| `catalog:read` | productos, precios, códigos de barras, sucursales | `inventory.stock.view` |
| `stock:read` | existencias por sucursal y consolidadas | `inventory.stock.view` |
| `orders:write` | registrar pedidos de e-commerce como ventas | `sales.pos.operate`, `inventory.movements.register.sales`, `sales.view` |
| `transfers:read` | consultar transferencias | `inventory.stock.view` |
| `transfers:write` | crear, despachar, recibir y anular transferencias | `inventory.transfers.manage`, `inventory.stock.view` |
| `webhooks:manage` | registrar, rotar y desactivar webhooks | `integration.manage` |
| `reports:read` | reportes gerenciales por sucursal | `reports.view` |

- **Restricción por sucursal** opcional: la llave solo ve y opera esa sucursal (y deja de servir si el dueño ya no
  trabaja en ella). Sin restricción, hereda el alcance del dueño.
- Se crean, listan y revocan en el escritorio (pantalla **Integraciones**; `CreateApiKeyCommand`,
  `RevokeApiKeyCommand`); la revocación es definitiva y no borra la fila (auditoría).

### 2.7 Idempotencia

| Dónde | Clave | Tabla | Repetición igual | Mismo id, otro contenido |
|---|---|---|---|---|
| Pedidos del API (`POST /v1/orders`) | (canal = API Key, `externalId`) único por empresa | `sales.external_orders` (+ SHA-256 del contenido normalizado) | devuelve la venta original (`200`, `Idempotent-Replayed: true`) | `422` `idempotency` |
| Comandos del escritorio en modo nube (`POST /api/v1/rpc`) | `requestId` único por empresa | `iam.processed_requests` (+ SHA-256 de tipo + payload y la respuesta) | devuelve la respuesta guardada (`replayed: true`) | `422` `idempotency` |

El registro de idempotencia se escribe **en la misma transacción** que el comando: si el COMMIT falló, no quedó
ninguno de los dos; si se confirmó, quedaron ambos. Así un corte de red durante el COMMIT nunca vende ni mueve stock dos
veces. Dos reintentos simultáneos chocan con el índice único: el segundo recibe un conflicto y, al reintentar,
encuentra al primero.

### 2.8 Servidor en la nube (`MINV.CloudServer`)

El escritorio en modo nube no tiene credenciales de la base: envía el **mismo comando o consulta de MediatR** que
usaría en conexión directa, serializado en JSON, y el servidor lo ejecuta con SU tubería.

| Punto de entrada | Qué hace |
|---|---|
| `GET /api/v1/health` | `{ status, product, version }` |
| `POST /api/v1/session/login` | `LoginCommand` con la tubería (bloqueo tras 5 intentos, `access_logs`); emite un token `mses_…` (256 bits): en `iam.sessions` solo queda su hash; vence a las **12 h sin actividad** (se renueva, a lo sumo una vez por minuto). Límite: 10 intentos por minuto por IP (`Minv:LoginsPerMinute`) |
| `POST /api/v1/rpc` | `{ requestId, type, payload }` → `{ ok, result, error, replayed }`. Cubeta de 120 peticiones con reposición de 10 por segundo por sesión |
| `POST /api/v1/session/logout` | cierra la sesión (`LogoutCommand`) |

- **Contrato** (`Application/Remote/RpcContract.cs`): `type` es el nombre completo de la clase; solo se aceptan los
  `IRequest<>` públicos de `MINV.Application` (salvo `LoginCommand`): nada fuera de ese ensamblado se puede instanciar
  desde la red. Un comando (`IAuditableRequest`) corre en UNA transacción con su `ProcessedRequest`; una consulta, sin
  registro de idempotencia.
- **Versión**: la cabecera `X-MINV-Client-Version` debe tener la **misma versión mayor** que el servidor (4); si no,
  `400 unsupported` («actualice el escritorio»).
- **En cada petición** el servidor resuelve el token con `iam.resolve_session` (SECURITY DEFINER), exige que la sesión
  esté abierta y vigente, y **recalcula permisos y alcance por sucursal**: el escritorio no decide qué ve.
- Errores: `validation` 400 · `authentication` 401 · `access_denied` 403 · `not_found` 404 · `concurrency` 409 ·
  `domain`/`idempotency` 422 · `unsupported` 400 · `server` 500 (sin detalles internos; se registra en el log). El
  escritorio convierte cada error en la MISMA excepción que lanzaría el caso de uso local: las pantallas no notan la
  diferencia.
- Tamaño máximo de una petición: 4 MB (imágenes de producto ≤ 1 MB en base64).

### 2.9 API Gateway B2B (`MINV.ApiGateway`)

**Solo para integraciones de terceros.** El escritorio NUNCA habla con el gateway (ni en modo nube): usa el servidor
en la nube, cuya sesión es de una persona con su rol; el gateway usa llaves de sistema con alcances. Detalle de cada
endpoint en la [guía del integrador](../integration/api-gateway-v1.md).

- **Autenticación** (`Security/ApiKeyAuthenticationHandler.cs`): `Authorization: Bearer minv_…` o `X-Api-Key: minv_…`.
  El prefijo se busca con `integration.resolve_api_key` (SECURITY DEFINER: antes de conocer la empresa, RLS no dejaría
  ver la fila); una llave inválida, vencida o revocada da `401` sin detalle (no revela si el prefijo existe).
- **Autorización en dos niveles**: cada ruta exige un alcance (política por alcance → `403`) y, dentro, el caso de uso
  vuelve a comprobar permisos, módulo licenciado y sucursal (defensa en profundidad).
- **Errores** como ProblemDetails (RFC 7807) con el mismo criterio que el servidor en la nube: `title` = tipo de error,
  `detail` = mensaje, extensiones `errors` (validación) y `code` (código estable del dominio).
- **Límites**: 120 peticiones por minuto por IP (ventana fija, se aplica a todo y antes de autenticar) y, por llave
  (partición = prefijo público del token, sin tocar la base), una cubeta de 100 con reposición de 10 por segundo en las
  rutas `/v1` → `429`.
- **OpenAPI**: interfaz en `/docs`, documento en `/docs/v1/openapi.json`; `GET /health` público.
- **Servicios en segundo plano** (`Background/BackgroundServices.cs`): despachador de webhooks
  (`Minv:Webhooks:Enabled`, `Minv:Webhooks:IntervalSeconds` = 5, `Minv:Webhooks:AllowPrivateTargets` solo en
  pruebas) y refresco del modelo de lectura cada `Minv:Reporting:RefreshMinutes` = 5 (`Minv:Reporting:Enabled`; solo con
  PostgreSQL). Ambos son seguros con varias réplicas (SKIP LOCKED y candado consultivo).

### 2.10 Modelo de escritura (OLTP) y de lectura (OLAP)

| | `MinvWriteDbContext` | `MinvReadDbContext` |
|---|---|---|
| Para | casos de uso, cajas, transferencias (transaccional) | tablero gerencial por sucursal (Auditoría global) |
| Lee | las 110 tablas | `reporting.v_branch_stock`, `reporting.v_branch_daily_sales` |
| Escribe | sí (outbox, guardas, OCC) | nunca (`SaveChanges` lanza excepción) |
| Conexión | `MINV_DB` | `MINV_DB_READ` (réplica de lectura) o, si falta, `MINV_DB` |

- Las vistas materializadas `reporting.mv_branch_stock` (existencia y valor por sucursal y variante al costo vigente) y
  `reporting.mv_branch_daily_sales` (tickets, ingresos e IVA por sucursal y día en la zona horaria de la empresa) se
  refrescan con `reporting.refresh_all()` (`REFRESH … CONCURRENTLY`, candado consultivo: si otra réplica ya refresca,
  no hace nada).
- Las vistas materializadas **no admiten RLS**: `minv_app` y `minv_server` NO tienen permiso sobre ellas; solo leen las
  vistas `security_barrier` que filtran por `iam.current_tenant_id()` e `iam.branch_visible()`, y además el contexto
  aplica los mismos filtros de empresa y sucursal.
- `IReportingReader` (`Persistence/ReportingReader.cs`): en PostgreSQL lee esas vistas (datos con hasta 5 minutos de
  antigüedad; informa `RefreshedAt`); en la demostración en memoria calcula lo mismo en vivo.
- `GetBranchReportQuery` (`Application/Corporate/BranchReportUseCases.cs`, módulo GLOBAL_AUDIT): ventas, ticket
  promedio, participación y valor del stock por sucursal visible, más el valor en tránsito.

### 2.11 Base de datos, roles y funciones

**Migración `V4MultiBranchCloud`** (`Persistence/Migrations/20260925214056_V4MultiBranchCloud*.cs`): 110 tablas en 8
esquemas (se suma `integration`) y el esquema `reporting` (vistas, no tablas del modelo). Orden:

1. **Guardia**: `inventory.stock_transfers` debe estar vacía (la V3 no tenía caso de uso de transferencias).
2. Columnas nuevas (`branch_id` con valor provisional `00000000-…`), tablas nuevas, claves.
3. **Relleno** de `branch_id` bajando por la jerarquía (almacén → zona → pasillo → estantería → nivel → posición →
   existencia → movimiento; caja → turno → venta → factura → pago; orden → recepción → líneas…), con los triggers
   append-only y de asientos **pausados solo durante el relleno** y solo en las tablas que lo necesitan; lo que no tiene
   camino (asientos, facturas de proveedor sin recepción, devoluciones) va a la sucursal principal (la V3 tenía una por
   empresa); la secuencia del costo promedio se numera por (variante, almacén). Una comprobación final aborta si queda
   alguna fila sin sucursal.
4. **Defensas**: append-only en las 8 tablas nuevas; RLS por empresa en todas las tablas con `tenant_id` (108) y
   RESTRICTIVA por sucursal generada desde listas explícitas (`BranchTables`, `InterBranchTables`): 40 políticas.
5. Funciones **SECURITY DEFINER** con `search_path` fijo y `EXECUTE` revocado a `PUBLIC` y concedido solo a
   `minv_server`: `integration.resolve_api_key(text)`, `iam.resolve_session(text)`,
   `integration.claim_deliveries(integer, integer)`, `reporting.refresh_all()`. Hacen UNA cosa cada una y no aceptan
   SQL ni nombres de tabla.
6. Modelo de lectura, vista `inventory.v_transfer_breaches`, datos V4 de las empresas existentes (4 permisos, matriz
   rol-permiso, cuentas 1.1.06 y 2.1.04) y privilegios.

Verificada sobre una copia de la base local de la V3: 110 tablas, 108 políticas por empresa, 40 por sucursal, 15
triggers append-only y 0 descuadres de conservación.

**Roles**:

| Rol | Quién lo usa | Propiedades |
|---|---|---|
| `minv_owner` | migraciones (`minv migrate`, `scripts/db_init.sql`), `tools\bd_nube.ps1` | dueño de las tablas (salta RLS: nunca lo usa una aplicación) |
| `minv_server` | `MINV.CloudServer` y `MINV.ApiGateway` | `NOSUPERUSER NOBYPASSRLS`, **no** dueño; CRUD en las tablas salvo UPDATE/DELETE/TRUNCATE en los libros; EXECUTE en las 4 funciones |
| `minv_app` | escritorio con conexión directa (base local, una empresa) | igual que `minv_server` pero sin las funciones SECURITY DEFINER |

Al arrancar, ambos servidores comprueban (`Infrastructure/Hosting/ServerHosting.cs`) que la base está al día y que el
rol de conexión **no** es superusuario, no tiene BYPASSRLS y no es dueño de ninguna tabla; si lo fuera, un error de
filtro en el código dejaría ver datos de otra empresa o sucursal, así que el servidor **se niega a arrancar**
(`MINV_ALLOW_PRIVILEGED_ROLE=1` lo permite, solo en desarrollo). También exige poder ejecutar `iam.resolve_session`.

### 2.12 Módulos comerciales de la V4

Se suman al catálogo `iam.modules` (precios en bolivianos) y habilitan casos de uso con `[RequiresModule]`:

| Código | Módulo | Implantación | Mensualidad | Habilita |
|---|---|---:|---:|---|
| `CLOUD_HA` | Infraestructura Cloud HA | 12.000 | 1.500 | PostgreSQL gestionado con PITR, réplica y SLA 99,9 % |
| `MULTI_BRANCH` | Topología multi-sucursal | 8.000 | 500 | sucursales, asignación de usuarios, transferencias |
| `API_INTEGRATIONS` | Integraciones API (B2B) | 6.000 | 400 | API Keys, webhooks, pedidos externos |
| `GLOBAL_AUDIT` | Auditoría global (réplicas de lectura) | 5.000 | 300 | tablero por sucursal desde el modelo de lectura |

### 2.13 Datos de prueba multi-sucursal

`minv datos-prueba` (`Infrastructure/Seeding/LocalDataSeeder.cs`) arma la empresa **MINV** con los casos de uso reales:

- 3 sucursales: **CM** Casa matriz (almacén `ALM01`, cajas `CAJA01`, `CAJA02`, `CAJA03`), **EA** El Alto (`ALMEA`,
  caja `EA-CAJA1`) y **SC** Santa Cruz (`ALMSC`, caja `SC-CAJA1`).
- 12 usuarios: Administrador (todas), Gerencia (todas, gerencia global), Bodega en CM, EA y SC, Ventas en CM y SC,
  Cajeros en CM (2), EA y SC, Consulta en las tres.
- Reposición semanal: la casa matriz despacha a El Alto los lunes y a Santa Cruz los miércoles; la sucursal recibe al
  día siguiente, a veces con un faltante («Caja dañada en el camión»…).
- Pedidos del e-commerce martes y viernes por la API Key **«Tienda en línea»** (alcances `catalog:read`, `stock:read`,
  `orders:write`, restringida a CM).
- Al final quedan una transferencia **en tránsito** hacia SC y una **pendiente** hacia EA, y (si el equipo tiene
  `MINV_INTEGRATION_KEYS`) un webhook de prueba hacia `https://tienda.elconstructor.example/webhooks/minv`
  (dominio ficticio: sus entregas fallan y sirven para ver los reintentos en «Entregas»).

## 3. Decisiones de diseño y su porqué

| Decisión | Alternativa descartada | Por qué |
|---|---|---|
| `branch_id` redundante en cada tabla de sucursal + FK compuestas `(tenant_id, branch_id, padre)` | derivar la sucursal con joins hasta el almacén | Los filtros de EF y las políticas RLS necesitan una columna directa para ser simples y rápidos; las FK compuestas garantizan que la redundancia nunca se contradiga (mismo patrón que `tenant_id` en la V3) |
| Directorio corporativo (sucursales, almacenes, catálogo) sin filtro por sucursal | filtrar todo por sucursal | Una sucursal necesita ver los destinos posibles y el catálogo común; lo sensible (stock, ventas, costos) sí se filtra |
| Dos interfaces (`IBranchScoped`, `IInterBranch`) | una transferencia por sucursal (dos documentos) | Un solo documento con dos lados evita documentos espejo que pueden divergir; ambas sucursales ven la misma verdad |
| RLS RESTRICTIVA por sucursal además de la de empresa | solo filtros en el código | Defensa en profundidad: un filtro olvidado en una consulta nueva no filtra datos; las políticas restrictivas se combinan con AND |
| Variables de sesión fijadas al abrir la conexión | `SET LOCAL` por transacción | Las consultas de EF abren y cierran conexiones fuera de transacciones explícitas; la variable de sesión cubre todas. Costo: con PgBouncer solo sirve el modo sesión (documentado) |
| Transferencia en dos pasos con estado «en tránsito» | mover el stock de un almacén a otro en una sola operación | Entre sucursales la mercadería viaja horas o días; el inventario de ambas debe ser real mientras tanto y el faltante debe detectarlo quien recibe |
| Despacho completo, recepción única con faltantes | despachos y recepciones parciales | Estados simples y conservación verificable con una vista; lo que no llegó es un hecho con motivo, no un saldo pendiente eterno |
| Manifiesto por lote visible para ambos lados | que el destino lea los movimientos del origen | Los movimientos del origen NO son visibles para el destino (RLS); el manifiesto es el dato mínimo compartido para entrar los mismos lotes |
| Cuentas recíprocas 1.1.06 / 2.1.04 | asiento único que cruza sucursales | Cada sucursal registra solo sus asientos (su alcance); el consolidado elimina recíprocas y el saldo neto es exactamente lo que viaja |
| Secuencia + índice único en el costo promedio | bloqueo pesimista | OCC como en todo M-INV (regla A-03): sin bloqueos, el conflicto se detecta y se reintenta |
| Outbox en la misma transacción | publicar el webhook después del COMMIT | Nunca se pierde un evento confirmado ni se publica uno de una transacción deshecha |
| Cola `outbox_dispatch` mutable separada del evento append-only | marcar el evento como enviado | El evento es un hecho inmutable; el estado de entrega cambia y vive aparte (y cada intento es otra fila) |
| `FOR UPDATE SKIP LOCKED` + arriendo de 120 s | un único despachador | Varias réplicas del gateway sin entregas simultáneas del mismo evento; si una réplica cae, otra retoma al vencer el arriendo |
| HMAC con marca de tiempo y doble firma al rotar | firma sin tiempo / cambio brusco de secreto | Evita repeticiones (tolerancia de 5 min en el receptor) y permite rotar sin cortar la integración |
| Secretos de webhook cifrados con clave maestra fuera de la base | guardar el secreto en claro o solo su hash | El emisor necesita el secreto para firmar (no sirve un hash); cifrado, una copia de la base no permite falsificar webhooks |
| API Key = prefijo + SHA-256 | PBKDF2/bcrypt como las contraseñas | Con 256 bits de azar no hay diccionario posible; un hash rápido permite autenticar cada petición sin costo |
| Llave que actúa en nombre de un usuario, recortada por alcances | cuentas de servicio con permisos propios | Reutiliza RBAC, auditoría y alcance por sucursal; revocar o degradar al dueño limita la llave |
| Escritorio en la nube por RPC de los MISMOS comandos | una API REST paralela para el escritorio | Una sola lógica (casos de uso y tubería) para local y nube; las pantallas no cambian; el catálogo RPC está acotado al ensamblado de aplicación |
| Gateway solo B2B | que el escritorio use el gateway | Separación de riesgos: las llaves de sistema tienen alcances mínimos y límites propios; las sesiones de personas tienen su rol completo y vencen |
| Idempotencia registrada en la transacción del comando | confiar en que el cliente no repita | Sobre internet los cortes durante el COMMIT existen; el reintento con el mismo id es seguro por construcción |
| Funciones SECURITY DEFINER mínimas | dar BYPASSRLS al servidor | Antes de conocer la empresa hay que resolver una llave o un token; las funciones exponen solo esa búsqueda, con `search_path` fijo y EXECUTE solo para `minv_server` |
| El servidor se niega a arrancar con un rol privilegiado | confiar en la configuración | Un superusuario o dueño salta RLS en silencio; el arranque lo detecta antes de aceptar tráfico |
| Vistas materializadas + réplica de lectura | reportes pesados sobre la base transaccional | El tablero gerencial no compite con las cajas; datos de hasta 5 minutos son suficientes para la gerencia |
| Listas explícitas de tablas en la migración | descubrir las tablas por columnas en SQL | Una migración publicada es inmutable: su comportamiento no debe cambiar si mañana aparece otra columna `branch_id` |

## 4. Flujos

### 4.1 Escritorio en modo nube

```text
 M-INV.exe (modo «Nube»)                   MINV.CloudServer                              PostgreSQL (minv_server)
 ───────────────────────                   ────────────────                              ────────────────────────
 1  POST /api/v1/session/login  ────────►  ¿X-MINV-Client-Version mayor = 4?  (si no: 400 unsupported)
    {tenantCode, email, password,           LoginCommand por la tubería ───────────────► iam.tenants, users, credentials
     machineName, clientVersion}            (5 fallos → bloqueo 15 min)                   iam.access_logs (éxito o falla)
                                            permisos + alcance por sucursal               iam.sessions (+ active_branch_id)
                                            token mses_… (256 bits) ─────────────────────► sessions.token_hash, expires_at
    ◄──────  200 {token, expiresAt, login{roles, permissions, access{allBranches, branches, activeBranchId}}, serverVersion}

 2  POST /api/v1/rpc                ────►  iam.resolve_session(sha256(token)) ──────────► (SECURITY DEFINER)
    Authorization: Bearer mses_…            sesión abierta y vigente; renueva 12 h
    {requestId, type, payload}              RECALCULA permisos y sucursales ─────────────► set_config(minv.tenant_id,
                                                                                                     minv.branch_ids)
                                            ¿consulta? → MediatR → 200 {ok, result}
                                            ¿comando?  → BEGIN
                                                         ¿requestId ya procesado? → misma respuesta (replayed: true)
                                                         validación → permisos y licencia → caso de uso
                                                         → auditoría (canal «cloud») → outbox
                                                         INSERT iam.processed_requests (hash + respuesta)
                                                         COMMIT
    ◄──────  200 {ok: true, result, replayed: false}   ·   4xx {ok: false, error{kind, message, errors, code}}

 3  corte de red sin respuesta → el escritorio reenvía con el MISMO requestId (hasta 3 intentos)
    → si el primero se confirmó: {ok: true, result, replayed: true}; si no: se ejecuta por primera vez
```

El token vive solo en la memoria del escritorio (nunca en disco). Si la sesión vence o se cierra en el servidor, la
ventana principal vuelve al inicio de sesión.

### 4.2 Pedido del e-commerce por la API

```text
 Tienda en línea                         MINV.ApiGateway                                   PostgreSQL
 ───────────────                         ───────────────                                   ──────────
 POST /v1/orders ────────────────────►   límite por IP (120/min) → ApiKeyAuthenticationHandler
 Authorization: Bearer minv_…            integration.resolve_api_key(prefijo) ─────────────► (SECURITY DEFINER)
 {externalId: "PED-1001", lines…}        hash en tiempo constante; no revocada ni vencida
                                         dueño activo → permisos ∩ alcances; sucursal de la llave
                                         política «orders:write» (si falta: 403)
                                         CreateExternalOrderCommand por la tubería:
                                           validación → permisos + módulo API_INTEGRATIONS
                                           ¿(api-<llave>, PED-1001) ya existe?
                                              mismo hash  → venta original ─────────────► 200 + Idempotent-Replayed: true
                                              otro hash   → 422 idempotency
                                           SaleWriter (lo mismo que la caja, sin turno):
                                              pedido → salida de stock (poka-yoke) → factura F-CM-… con IVA
                                              → pago → asiento (ventas, IVA, costo) → evento sale.completed
                                           INSERT sales.external_orders ──── una transacción ──► COMMIT
                                           auditoría: canal «api», api_key_id
 ◄──── 201 Created {externalId, orderNumber, invoiceNumber, branchCode, total, tax, issuedAt, replayed: false}
```

Si dos reintentos del mismo pedido llegan a la vez, el segundo choca con el índice único
`(tenant_id, channel, external_id)`, se deshace, reintenta y encuentra el primero: una sola venta.

### 4.3 Ciclo de vida de una transferencia (con números)

Casa matriz (**CM**, almacén `ALM01`) repone a El Alto (**EA**, almacén `ALMEA`) con 10 unidades de `FER-004`
(Martillo carpintero 16 oz). Costo promedio en `ALM01`: **Bs 48,00**. En `ALMEA` había 5 unidades a Bs 46,00.

| # | Paso | Quién | Stock | Asiento | Bitácora y eventos |
|---|---|---|---|---|---|
| 1 | Crear `TR-CM-000031` | Bodega CM | sin cambios | — | `Pending` «Solicitada» |
| 2 | Despachar | Bodega CM | `ALM01` −10 (TRASLADO (SALIDA)); manifiesto: SIN-LOTE × 10; costo de la línea 48,00 | **CM**: Debe 1.1.06 **480,00** / Haber 1.1.05 **480,00** | `Dispatched` «Despachada: mercadería en tránsito»; `transfer.dispatched` (totalCost 480,00) |
| — | En tránsito | — | CM 0 + EA 0 de este envío; **en tránsito 10** | consolidado: 1.1.06 (480) − 2.1.04 (0) = **480 en tránsito** | EA la ve «despachada (en tránsito)» y solo EA puede recibirla |
| 3 | Recibir: llegaron 9, falta 1 («Caja dañada en el camión») | Bodega EA | `ALMEA` +9 (TRASLADO (ENTRADA), mismo lote); faltante 1 (delta compensatorio) | **EA**: Debe 1.1.05 **432,00** + Debe 5.1.09 **48,00** / Haber 2.1.04 **480,00** | `Received` «Recibida con faltantes (1)»; `transfer.discrepancy` y `transfer.received` |

Resultado:

- **Costo promedio en `ALMEA`** (nueva fila de `average_cost_history` con la secuencia siguiente):
  (5 × 46,00 + 9 × 48,00) / 14 = 662,00 / 14 = **Bs 47,285714**.
- **Consolidado contable**: 1.1.06 (480) − 2.1.04 (480) = **0 en tránsito**; inventario: −480 en CM y +432 en EA =
  −48, que es exactamente el gasto 5.1.09 por la merma en tránsito.
- **Conservación**: Σ salidas (10) = Σ entradas (9) + Σ faltantes (1) + en tránsito (0). `inventory.v_transfer_breaches`
  vacía.
- **Qué ve cada uno**: Bodega CM ve la transferencia, su salida y su asiento; Bodega EA ve la transferencia, su entrada,
  el faltante y su asiento; ninguno ve el stock ni los asientos del otro. La gerencia global ve todo.

### 4.4 Entrega de un webhook

```text
 1  La venta, recepción o transferencia guarda su evento en integration.outbox_events + outbox_dispatch (misma transacción)
 2  WebhookDispatcherService (gateway) cada 5 s: integration.claim_deliveries(50, 120)
       → toma hasta 50 eventos vencidos con FOR UPDATE SKIP LOCKED y los «arrienda» 120 s (otra réplica no los toma)
 3  Por evento, con el tenant del evento: webhooks activos, suscritos al tipo, creados antes del evento y de su sucursal
       (o sin filtro), que todavía no confirmaron y tienen intentos disponibles
 4  Cuerpo {id, type, occurredAt, tenantId, branchId, data}; secreto(s) descifrados con MINV_INTEGRATION_KEYS
       X-MINV-Signature: t=1790000000,v1=<hmac del secreto vigente>[,v1=<hmac del anterior, si está en gracia>]
 5  POST con SafeWebhookHttp: resuelve el DNS y conecta SOLO a IP públicas (rechaza loopback, 10/8, 172.16/12,
       192.168/16, 169.254/16 —metadatos de la nube—, 100.64/10, multicast…), sin redirecciones, 10 s, respuesta ≤ 64 KB
 6  Cada intento → fila en integration.webhook_deliveries (código, error, duración)
 7  outbox_dispatch.RecordRound: todos confirmaron → Completed · si no → próxima ronda (1 min, 5 min, 30 min, 2 h, 6 h,
       12 h, 24 h) · tras la ronda 8 → Exhausted (queda visible en Integraciones › Entregas)
```

La entrega es **al menos una vez**: si el gateway cae después de entregar y antes de guardar, otra réplica reintenta al
vencer el arriendo. El receptor deduplica por `id` del sobre.

## 5. Seguridad por capas (resumen)

| Amenaza | Defensa |
|---|---|
| Ver datos de otra empresa o sucursal | filtros de EF + guardas de escritura + FK compuestas + RLS empresa y sucursal + rol sin BYPASSRLS verificado al arrancar |
| Robo de la base de datos | contraseñas PBKDF2; API Keys y tokens de sesión solo como SHA-256; secretos de webhook cifrados AES-256-GCM con clave fuera de la base |
| Credenciales en los escritorios | modo nube: el escritorio no tiene cadena de conexión; token en memoria; https obligatorio salvo `localhost` |
| Fuerza bruta | bloqueo de la cuenta tras 5 fallos; 10 logins por minuto por IP; 120 peticiones por minuto por IP en el gateway; 401 sin detalle |
| Repetición o doble registro | idempotencia por `externalId` y `requestId`; OCC; firma con marca de tiempo |
| SSRF desde los webhooks | URL https sin credenciales; conexión solo a IP públicas resueltas en el momento del envío (también si el DNS cambia después); sin redirecciones |
| Escalada por funciones privilegiadas | SECURITY DEFINER mínimas, `search_path` fijo, EXECUTE solo `minv_server` |
| Pérdida de eventos | outbox transaccional; reintentos con espera exponencial; historial de entregas |

## 6. Pruebas

- `tests/MINV.Integration.Tests`: levanta `MINV.CloudServer` y `MINV.ApiGateway` **reales** (Kestrel en un puerto de
  loopback) con `--Minv:Storage memoria` y la empresa de prueba multi-sucursal; prueba login, versión del cliente,
  alcance por sucursal decidido por el servidor, idempotencia de comandos RPC y de pedidos, errores con su código,
  serialización de todo el catálogo RPC, 401/403 por llave y alcance, OpenAPI público y webhooks firmados verificados
  con `ApiKeyTokens.Verify`.
- `tests/MINV.Infrastructure.Tests/ModelTests`: 110 tablas en 8 esquemas, FK con tenant (y sucursal), xmin,
  15 libros append-only, nombres ≤ 63 caracteres y que el ERD documente todas las tablas.
- Las suites de la V3 siguen vigentes (dominio, aplicación, datos de prueba en memoria y contra PostgreSQL con
  `MINV_TEST_PG`, pantallas del escritorio).

## 7. Límites conocidos de la alfa

- El despacho es completo por línea y la recepción es única (sin recepciones parciales en varias entregas).
- Un webhook no recibe eventos anteriores a su registro; no hay reenvío manual de un evento agotado.
- Las tablas `integration.outbox_events`, `integration.webhook_deliveries` e `iam.processed_requests` crecen sin
  depuración automática (planifique un archivo periódico con el rol dueño).
- Los límites por IP ven la IP del proxy si los servidores están detrás de uno: habilite los encabezados reenviados
  (`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, ver la guía de despliegue).
