# Mapa V7 · Ventas, reservas de stock y tienda (storefront)

> Rama leída: `Inventario-V7` (idéntica a la V6). Solo lectura: nada se compiló ni se ejecutó; todo sale de leer el código.
> Raíz del repositorio: `D:\Proyectos Claude 2\Sistema Inventario\ZP-MINV-Platform`. Las rutas de abajo son relativas a esa raíz.
> «Verificado» = leído en el código. «Plausible» = deducido del código, sin prueba que lo confirme.

## 0. Archivos del subsistema

| Tema | Archivo |
|---|---|
| Agregado armado | `src/1. Core/MINV.Domain/Sales/PcBuild.cs` (`PcBuild`, `PcBuildLine`, `PcBuildEvent`, enums) |
| Compatibilidad y ranuras | `src/1. Core/MINV.Domain/Catalog/PcCompatibility.cs` (`PcSlot`, `PcComponent`, `PcIssue`, `PcCompatibilityReport`, `PcCompatibility`) |
| Reservas y existencias | `src/1. Core/MINV.Domain/Inventory/StockReservation.cs`, `StockLevel.cs`, `Enums.cs` (`ReservationStatus`) |
| Pedido, factura, pedido externo | `src/1. Core/MINV.Domain/Sales/SalesOrder.cs`, `SalesOrderLine.cs`, `Invoice.cs`, `ExternalOrder.cs`, `Customer.cs`, `PosSession.cs`, `Enums.cs` |
| Eventos de dominio | `src/1. Core/MINV.Domain/Events/DomainEvents.cs`; códigos en `src/1. Core/MINV.Domain/Integration/Webhooks.cs` |
| Tienda: contratos | `src/1. Core/MINV.Application/Storefront/StorefrontContracts.cs` |
| Tienda: catálogo | `src/1. Core/MINV.Application/Storefront/StorefrontCatalog.cs` |
| Tienda: reservas | `src/1. Core/MINV.Application/Storefront/StorefrontReservations.cs` (`PcBuildStock`, `StorefrontSlots`, manejadores) |
| Armador (escritorio) | `src/1. Core/MINV.Application/Tech/PcBuildUseCases.cs`, `TechContracts.cs`, `TechDashboard.cs` |
| Venta | `src/1. Core/MINV.Application/Sales/SalesUseCases.cs` (`CheckoutCommand`, `SaleWriter`, `SaleReverser`); `PosUseCases.cs` solo abre y cierra turno |
| Pedido B2B | `src/1. Core/MINV.Application/Integration/ExternalOrderUseCases.cs` |
| Rutas públicas | `src/3. Presentation/MINV.ApiGateway/Endpoints/StorefrontEndpoints.cs`, `V1Endpoints.cs` |
| Principal de tienda | `src/3. Presentation/MINV.ApiGateway/Security/StorefrontAuthenticationHandler.cs` |
| Vencimiento | `src/3. Presentation/MINV.ApiGateway/Background/BackgroundServices.cs` (`StorefrontReservationExpiryService`) |
| Armado del gateway | `src/3. Presentation/MINV.ApiGateway/ApiGatewayApp.cs` (CORS, límites, errores) |
| Persistencia | `src/2. Infrastructure/MINV.Infrastructure/Persistence/Configurations/Inventory/StockReservationConfiguration.cs`, `.../Sales/TechSalesConfigurations.cs`, `.../Sales/SalesOrderConfiguration.cs` |
| Migración V6 | `src/2. Infrastructure/MINV.Infrastructure/Persistence/Migrations/20260927173304_V6Storefront.cs` y `.Sql.cs` |
| Correo (existe) | `src/2. Infrastructure/MINV.Infrastructure/Billing/Mail/SmtpMailSender.cs`, `src/1. Core/MINV.Application/Abstractions/SiatPorts.cs`, `src/1. Core/MINV.Application/Billing/FiscalLifecycleUseCases.cs` (`BuyerMail`) |
| Web | `src/3. Presentation/MINV.WebCatalog/src/` (`1-domain/builder`, `1-domain/storefront`, `2-application/storefront`, `3-infrastructure/http`, `4-presentation/pages/builder`, `pages/product/PurchaseBox.tsx`, `pages/reservation`) |
| Documentos | `docs/integration/storefront-api-v1.md`, `docs/architecture/tienda-web-conectada-v6.md`, `.claude/v6-storefront-rules.md`, `docs/database/ERD-MINV-V3.md` §10 |

---

## 1. Agregado `PcBuild`

`public sealed partial class PcBuild : Entity, IBranchScoped, IConcurrencyAware, IAggregateRoot, IHasDomainEvents`. Tabla `sales.pc_builds`.

### 1.1 Constantes

| Constante | Valor |
|---|---|
| `PcBuild.MaxQuantity` | 16 unidades por línea |
| `PcBuild.MaxLines` | 20 líneas |
| `PcBuild.ExpiredReason` | `"Vencida"` |
| `PcBuild.WebNumberPrefix` | `"ARM-WEB"` |
| `PcBuild.MultiSlots` | `Ram, Storage, Gpu, Monitor, Peripheral, Software, Service` (las demás ranuras admiten UNA línea: `Cpu, Motherboard, Psu, Case, Cooler`) |

### 1.2 Campos (propiedad → columna)

| Propiedad | Columna | Tipo / límite | Desde |
|---|---|---|---|
| `BranchId` | `branch_id` | uuid NOT NULL | V4.2 |
| `Number` | `number` | varchar(40); único `(tenant_id, branch_id, number)` | V4.2 |
| `Name` | `name` | varchar(150) | V4.2 |
| `CustomerId` | `customer_id` | uuid NULL → `sales.customers` | V4.2 |
| `ValidUntil` | `valid_until` | date (último día de la cotización, inclusive) | V4.2 |
| `Status` | `status` | varchar(20) | V4.2 (+`Reserved` V6) |
| `QuotedWithErrors` | `quoted_with_errors` | boolean | V4.2 |
| `CreatedByUserId` | `created_by_user_id` | uuid → `iam.users` | V4.2 |
| `CreatedAt` / `QuotedAt` | `created_at` / `quoted_at` | timestamptz | V4.2 |
| `InvoiceId` | `invoice_id` | uuid NULL → `sales.invoices` (misma sucursal); único filtrado | V4.2 |
| `Channel` | `channel` | varchar(10): `Desktop` \| `Web` | V6 |
| `ContactName` | `contact_name` | varchar(120) | V6 |
| `ContactPhone` | `contact_phone` | varchar(30), normalizado | V6 |
| `ContactEmail` | `contact_email` | varchar(254) | V6 |
| `Notes` | `notes` | varchar(500) | V6 |
| `ReservedAt` / `ReservedUntil` | `reserved_at` / `reserved_until` | timestamptz NULL | V6 |
| `CancelReason` | `cancel_reason` | varchar(250) | V6 |
| `PublishedToWeb` | `published_to_web` | boolean | V6 |
| `RowVersion` | `xmin` | token de concurrencia | V4.2 |

No se guardan (derivados): `Total` (suma de `PcBuildLine.Subtotal`), `IsReservationActive`, `DomainEvents`.

### 1.3 Restricciones CHECK de `sales.pc_builds`

| Nombre | Regla |
|---|---|
| `ck_pc_builds_estado` | `status IN ('Draft','Quoted','Reserved','Sold','Cancelled')` |
| `ck_pc_builds_venta` | `(status = 'Sold') = (invoice_id IS NOT NULL)` |
| `ck_pc_builds_cotizacion` | `status NOT IN ('Quoted','Reserved') OR quoted_at IS NOT NULL` |
| `ck_pc_builds_marcado` | `NOT quoted_with_errors OR quoted_at IS NOT NULL` |
| `ck_pc_builds_canal` | `channel IN ('Desktop','Web')` |
| `ck_pc_builds_contacto` | `channel <> 'Web' OR (contact_name IS NOT NULL AND contact_phone IS NOT NULL)` |
| `ck_pc_builds_reserva` | `status <> 'Reserved' OR (reserved_at IS NOT NULL AND reserved_until IS NOT NULL)` |
| `ck_pc_builds_publicado` | `NOT published_to_web OR (channel = 'Desktop' AND status IN ('Quoted','Reserved','Sold'))` |

Índices: `(tenant_id, status)`; `(tenant_id, reserved_until) WHERE status = 'Reserved'`; `(tenant_id, published_to_web) WHERE published_to_web`.

### 1.4 Estados y transiciones

`enum PcBuildStatus { Draft, Quoted, Sold, Cancelled, Reserved }` · `enum PcBuildChannel { Desktop, Web }`.

| Método | Desde | Hacia | Acción en bitácora | Evento de dominio | Códigos de error |
|---|---|---|---|---|---|
| constructor / `CreateWeb(...)` | — | `Draft` | `Created` | — | `pcbuild.contact_name`, `pcbuild.contact_phone`, `guard.email` |
| `Rename`, `AddLine`, `RemoveLine` | `Draft` | `Draft` | — | — | `pcbuild.not_draft`, `pcbuild.quantity`, `pcbuild.slot`, `pcbuild.lines`, `pcbuild.line` |
| `Quote(validUntil, today, compatibility, acceptIncompatible, now, userId)` | `Draft` | `Quoted` | `Quoted` | — | `pcbuild.empty`, `pcbuild.valid_until`, `pcbuild.incompatible` |
| `Reserve(now, until, today, userId)` | `Quoted` | `Reserved` | `Reserved` | `PcBuildReservedEvent` | `pcbuild.state`, `pcbuild.expired`, `pcbuild.reserved_until`, `pcbuild.empty` |
| `ReleaseReservation(reason, now, userId, expired: false)` | `Reserved` | `Cancelled` | `Released` | `PcBuildReleasedEvent` (`Expired = false`) | `pcbuild.state` |
| `ReleaseReservation(..., expired: true)` | `Reserved` | `Cancelled` | `Expired` | `PcBuildReleasedEvent` (`Expired = true`) | `pcbuild.state` |
| `MarkSold(invoiceId, today, userId, now)` | `Draft`, `Quoted`, `Reserved` | `Sold` | `Sold` | `PcBuildSoldEvent` (`WasReserved`) | `pcbuild.state`, `pcbuild.expired` |
| `Cancel(userId, now, reason)` | `Draft`, `Quoted` | `Cancelled` (y `PublishedToWeb = false`) | `Cancelled` | — | `pcbuild.state` |
| `Publish(userId, now)` | `Quoted`/`Reserved`/`Sold` y canal `Desktop` | igual | `Published` | — | `pcbuild.publish_channel`, `pcbuild.publish_state`, `pcbuild.empty`, `pcbuild.published` |
| `Unpublish(userId, now)` | publicado | igual | `Unpublished` | — | `pcbuild.not_published` |

Notas verificadas:
- Un armado `Reserved` NO se anula con `Cancel`: el caso de uso llama a `ReleaseReservation`.
- El dominio permite `MarkSold` desde `Draft`, pero `SellPcBuildHandler` lo rechaza (`pcbuild.not_quoted`).
- `IsExpiredOn(today)`: `Quoted` o `Reserved` y `today > ValidUntil`. `IsReservationExpired(now)`: `Reserved` y `ReservedUntil <= now`.

### 1.5 Contacto y teléfono

- `SetContact(contactName, contactPhone, contactEmail, notes)`: en canal `Web` nombre y teléfono son obligatorios; correo con `Guard.OptionalEmail`; notas ≤ 500.
- `NormalizePhone`: quita espacios, guiones, puntos y paréntesis; expresión `^(?<cc>\+?591)?(?<n>[0-9]{7,8})$`; guarda `+591` + dígitos si vino con código de país, o solo los dígitos.
- `MatchesPhone(phone)`: compara los dígitos sin `+591`.

### 1.6 Numeración y vigencia

| Origen | Número | Cómo se genera |
|---|---|---|
| Escritorio | `ARM-<sucursal>-000001` (p. ej. `ARM-CM-000001`) | `Documents.NextForBranchAsync<PcBuild>(db, b => b.Number, "ARM", branchId, ct)` |
| Web | `ARM-WEB-000001` | `Documents.NextNumberAsync(db.Set<PcBuild>(), b => b.Number, PcBuild.WebNumberPrefix, ct)` |

- `Documents` está en `src/1. Core/MINV.Application/Common/BusinessServices.cs`: busca el mayor número que empieza con `prefijo-` (en la base y en lo local) y suma 1. La unicidad la da el índice; un choque se traduce en `ConcurrencyConflictException` y el caso de uso reintenta (máximo 3).
- Vigencia web: `until = now + StorefrontOptions.EffectiveHours` (48 por defecto; acotado 1…720). `ValidUntil` = fecha local de `until` en la zona de la empresa.
- Vigencia escritorio: `SavePcBuildCommand.ValidDays` (1…90, 7 por defecto); reserva con `ReservePcBuildCommand(Number, Hours = 48)` (1…720).

### 1.7 Líneas: `PcBuildLine` → `sales.pc_build_lines`

| Propiedad | Columna | Regla |
|---|---|---|
| `PcBuildId` | `pc_build_id` | FK `(tenant_id, branch_id, pc_build_id)`, borrado en cascada |
| `Slot` | `slot` varchar(20) | `ck_pc_build_lines_ranura`: `Cpu, Motherboard, Ram, Gpu, Storage, Psu, Case, Cooler, Monitor, Peripheral, Software, Service` |
| `VariantId` | `variant_id` | FK `(tenant_id, variant_id)` → `catalog.product_variants` |
| `Quantity` | `quantity` int | `ck_pc_build_lines_cantidad`: `BETWEEN 1 AND 16` |
| `QuotedUnitPrice` | `quoted_unit_price` numeric(19,4) | `ck_pc_build_lines_precio`: `>= 0`; redondeado a 2 decimales |

`Subtotal` se deriva (`Quantity * QuotedUnitPrice`). El precio cotizado es la redundancia comercial documentada (regla T-06).

### 1.8 Bitácora: `PcBuildEvent` → `sales.pc_build_events` (append-only)

| Columna | Contenido |
|---|---|
| `pc_build_id`, `branch_id`, `tenant_id` | FK `(tenant_id, branch_id, pc_build_id)`, `Restrict` |
| `action` | `Created, Quoted, Reserved, Released, Expired, Sold, Cancelled, Published, Unpublished` (`ck_pc_build_events_accion`) |
| `status` | estado DESPUÉS de la acción (`ck_pc_build_events_estado`) |
| `user_id`, `occurred_at`, `detail` | quién, cuándo, texto ≤ 250 |

La escribe solo `PcBuild.Log(...)`. Triggers `trg_append_only` y `trg_append_only_truncate`; políticas `tenant_isolation` y `branch_isolation` RESTRICTIVA.

### 1.9 Eventos de dominio (al outbox)

| Registro | Código (`IntegrationEvents`) | Campos |
|---|---|---|
| `PcBuildReservedEvent` | `pcbuild.reserved` | `PcBuildId, Number, BranchIdOfBuild, Channel, Total, ReservedUntil, Lines, OccurredAt` |
| `PcBuildReleasedEvent` | `pcbuild.released` | `..., Reason, Expired, Lines` |
| `PcBuildSoldEvent` | `pcbuild.sold` | `..., InvoiceId, WasReserved, Lines` |

`PcBuildEventLine(VariantId, Slot, Quantity, QuotedUnitPrice)`. Ningún evento lleva datos del contacto.

### 1.10 Compatibilidad

- Solo `PcCompatibility.Check(IReadOnlyList<PcComponent>)` (regla T-06). Piezas obligatorias: `Required = [Cpu, Motherboard, Ram, Storage, Psu, Case]`.
- Errores (`IsError = true`): `SOCKET_CPU_PLACA`, `TIPO_RAM`, `RANURAS_RAM`, `CAPACIDAD_RAM`, `RANURAS_M2`, `FORMATO_GABINETE`, `LARGO_GPU`, `SOCKET_REFRIGERACION`, `POTENCIA_FUENTE`.
- Avisos (`IsError = false`): `PIEZA_FALTANTE`, `SIN_GRAFICOS`, `POTENCIA_RECOMENDADA`.
- En la web la compatibilidad nunca bloquea: `CreateStorefrontReservationHandler` llama `Quote(..., acceptIncompatible: true, ...)` y la respuesta informa `hasCompatibilityWarnings` (= `QuotedWithErrors`).
- Consecuencia para un carrito: un monitor solo produce avisos `PIEZA_FALTANTE` (no errores), así que `IsCompatible` es verdadero.

---

## 2. `StockReservation` y `StockLevel`

### 2.1 `StockReservation` → `inventory.stock_reservations`

`public sealed class StockReservation : Entity, IConcurrencyAware, IBranchScoped`. El constructor es `internal`: solo la crea `StockLevel.Reserve`.

| Propiedad | Columna | Nota |
|---|---|---|
| `StockLevelId` | `stock_level_id` | FK `(tenant_id, branch_id, stock_level_id)` |
| `Quantity` | `quantity` numeric(18,6) | `ck_stock_reservations_cantidad`: `quantity > 0` |
| `Status` | `status` varchar(20) | `enum ReservationStatus { Active, Consumed, Released, Expired }` |
| `ExpiresAt` | `expires_at` | timestamptz |
| `PosSessionId` | `pos_session_id` | origen 1 → `sales.pos_sessions` |
| `SalesOrderLineId` | `sales_order_line_id` | origen 2 → `sales.sales_order_lines` |
| `PcBuildLineId` | `pc_build_line_id` | origen 3 (V6) → `sales.pc_build_lines` |
| `RowVersion` | `xmin` | |

Arco de origen:
- Base: `ck_stock_reservations_origen` = `num_nonnulls(pos_session_id, sales_order_line_id, pc_build_line_id) <= 1`.
- Dominio: mismo control con el código `reservation.origin`.
- Es `<= 1`, no `= 1`: una reserva sin origen es válida.
- Índices: `(stock_level_id, status)`; `(pc_build_line_id, status) WHERE pc_build_line_id IS NOT NULL`; `(tenant_id, branch_id, pc_build_line_id)`.

**Hecho clave (verificado con búsqueda en `src`):** el único origen que usa el código de aplicación es `PcBuildLineId`. Nadie llama a `StockLevel.Reserve` con `posSessionId` ni con `salesOrderLineId`, y nadie llama a `StockLevel.Consume`. La caja no reserva al armar el carrito: la venta descuenta directo.

### 2.2 Operaciones de `StockLevel`

| Método | Efecto en la reserva | Efecto en `QuantityReserved` | Efecto en `QuantityOnHand` | Movimiento |
|---|---|---|---|---|
| `Reserve(quantity, expiresAt, now, posSessionId?, salesOrderLineId?, pcBuildLineId?)` | nueva, `Active` | + cantidad | — | no |
| `Fulfill(reservation)` | `Consumed` | − cantidad | — | no (lo registra la venta) |
| `Release(reservation)` | `Released` | − cantidad | — | no |
| `Expire(reservation, now)` | `Expired` (exige `ExpiresAt <= now`, `reservation.not_expired`) | − cantidad | — | no |
| `Consume(reservation, issueType, unit, context)` | `Consumed` | − cantidad | − cantidad | sí (salida) |

- `Available => Quantities.Round6(QuantityOnHand - QuantityReserved)`.
- `Reserve` y `Register` lanzan `InsufficientStockException` si la cantidad supera `Available`.
- Otros códigos: `reservation.expiry`, `reservation.closed`, `reservation.other_level`, `reservation.consume_type`.

### 2.3 Cómo lo usa la aplicación: `PcBuildStock` (clase `internal static` en `StorefrontReservations.cs`)

| Método | Qué hace |
|---|---|
| `ReserveAsync(db, build, now, until, ct)` | Lee las existencias de la SUCURSAL del armado (`l.BranchId == build.BranchId`) de cada variante; reparte cada línea entre las posiciones con más disponible; si falta algo lanza `StorefrontStockException` con todos los faltantes y no reserva nada; si alcanza, crea una `StockReservation` por cada tramo con `pcBuildLineId: line.Id` |
| `ReleaseAsync` | `level.Release` de cada reserva activa del armado |
| `ExpireAsync` | `level.Expire` de cada reserva activa |
| `FulfillAsync` | `level.Fulfill` de cada reserva activa (al vender) |
| `ReservedAsync` | suma reservada activa del armado |
| `NamesAsync` | SKU y nombre por variante |

---

## 3. `SalesOrder`, `SalesOrderLine`, `ExternalOrder`, `Invoice`

### 3.1 Para qué se usan HOY

Un `SalesOrder` es una venta ya cobrada. No existe hoy un pedido pendiente: `SaleWriter.SellAsync` lo crea, confirma, despacha y factura dentro de la misma transacción.

| Entidad | Tabla | Uso real hoy |
|---|---|---|
| `SalesOrder` | `sales.sales_orders` | Cabecera de la venta. Número `PV-<sucursal>-000001`, único `(tenant_id, number)` |
| `SalesOrderLine` | `sales.sales_order_lines` | Línea con `VariantId, UnitId, Quantity, UnitPrice, DiscountPercent, StockMovementId` |
| `Invoice` / `InvoiceLine` | `sales.invoices` / `sales.invoice_lines` | Factura interna `F-<sucursal>-000001`; una por pedido (único `sales_order_id`) |
| `ExternalOrder` | `sales.external_orders` (append-only) | Registro de idempotencia del pedido B2B: `Channel, ExternalId, RequestHash, SalesOrderId, InvoiceNumber, ReceivedAt` |

### 3.2 Estados

| Enum | Valores | Transiciones |
|---|---|---|
| `SalesOrderStatus` | `Draft, Confirmed, Fulfilled, Invoiced, Cancelled` | `Confirm()` Draft→Confirmed (`sale.empty`); `MarkFulfilled()` Confirmed→Fulfilled (exige `StockMovementId` en todas las líneas, `sale.movements`); `MarkInvoiced()` Fulfilled→Invoiced; `Cancel()` desde Draft o Confirmed |
| `InvoiceStatus` | `Draft, Issued, Voided` | `Issue(now)`, `Void(reason, now)` |

### 3.3 Restricciones que condicionan el carrito

| Hecho | Dónde |
|---|---|
| `SalesOrder.CustomerId` es obligatorio (`Guard.NotEmpty`) | `SalesOrder.cs` |
| `SalesOrder.PriceListId` es obligatorio | `SalesOrder.cs` |
| `ck_sales_orders_origen`: `num_nonnulls(pos_session_id, warehouse_id) = 1` | `SalesOrderConfiguration.cs` |
| `sales_orders` NO tiene columnas de canal, contacto, vigencia de reserva ni motivo de cierre | `SalesOrder.cs` |
| No existe bitácora de pedidos (no hay `sales_order_events`) | búsqueda en `src` |
| `number` varchar(30) | `SalesOrderConfiguration.cs` |

### 3.4 Quién crea pedidos

| Llamador | `SaleOrigin` | Canal (`SaleChannels`) |
|---|---|---|
| `CheckoutHandler` (caja) | sucursal y almacén de la caja, `PosSessionId` | `pos` |
| `SellPcBuildHandler` (armado en caja) | igual que la caja | `pos` |
| `CreateExternalOrderHandler` (`POST /v1/orders`) | almacén pedido o el de la sucursal de la llave, sin turno | `api` |
| Transcripción CAFC | — | `cafc` (constante `SaleChannels.Cafc`) |

### 3.5 Pedido B2B (`CreateExternalOrderCommand`)

- Exige módulo `LicenseModuleCodes.ApiIntegrations` y permisos `sales.pos.operate` + `inventory.movements.register.sales`; ruta con alcance `ApiScopes.OrdersWrite`.
- Idempotencia: `(Channel, ExternalId)` con `Channel = "api-<id de la llave>"`; mismo contenido → `Replayed = true`; otro contenido → `IdempotencyConflictException` (422).
- NO reserva stock: vende y descuenta en el acto. Máximo 200 líneas.
- Resultado `ExternalOrderResult(ExternalId, OrderNumber, InvoiceNumber, BranchCode, Total, Tax, IssuedAt, Replayed, Cuf, FiscalNumber)`.

---

## 4. Casos de uso de la tienda

### 4.1 Tabla de casos de uso

| Ruta | Petición MediatR | Permiso | Límite | Caché |
|---|---|---|---|---|
| `GET /storefront/v1/catalog` | `GetStorefrontCatalogQuery` → `StorefrontCatalogView` | `storefront.read` | 300/min por IP | `public, max-age=30` |
| `GET /storefront/v1/products/{slug}` | `GetStorefrontProductQuery(Slug)` → `StorefrontProduct` | `storefront.read` | 300/min | `no-cache` |
| `GET /storefront/v1/products/{sku}/image` | `GetStorefrontProductImageQuery(Sku)` → `StorefrontImage` | `storefront.read` | 300/min | `public, max-age=3600` + `ETag` |
| `GET /storefront/v1/presets` | `GetStorefrontPresetsQuery` → lista de `StorefrontPreset` | `storefront.read` | 300/min | — |
| `POST /storefront/v1/reservations` | `CreateStorefrontReservationCommand` → `StorefrontReservationResult` | `storefront.reserve` | 10/min por IP | — |
| `GET /storefront/v1/reservations/{number}?phone=` | `GetStorefrontReservationQuery(Number, Phone)` | `storefront.read` | 300/min | — |
| `POST /storefront/v1/reservations/{number}/cancel` | `CancelStorefrontReservationCommand(Number, Phone)` | `storefront.reserve` | 10/min por IP | — |
| (trabajo interno) | `ExpirePcBuildReservationsCommand` → `int` | `storefront.reserve` | cada `ExpiryMinutes` (5) | — |

Todas las rutas: política de autorización `storefront` (esquema `Storefront`), CORS `storefront`, etiqueta «Tienda web». Sin configuración responden 503.

### 4.2 Principal técnico

- `StorefrontAuthenticator.AuthenticateAsync`: empresa `Minv:Storefront:TenantCode`; primer usuario activo con el rol `TIENDA_WEB` (`RoleCodes.Storefront`); sucursal `BranchCode` o la del almacén principal o la primera activa.
- Alcance: `new BranchScope(false, [branch.Id], branch.Id)` → UNA sucursal. Canal `RequestChannels.Storefront`.
- Rol `TIENDA_WEB`: `storefront.read`, `storefront.reserve`, `inventory.stock.view`. `ADMIN` y `GERENCIA` también tienen los dos permisos de tienda.
- `StorefrontSettings`: `Enabled, TenantCode, BranchCode, AllowedOrigins[], ReservationHours (48), ReadsPerMinute (300), ReservationsPerMinute (10), ExpiryMinutes (5)`.

### 4.3 Catálogo (`StorefrontCatalogReader`)

- Solo productos y variantes activos CON precio en la lista por defecto.
- Disponibilidad de la sucursal de la tienda: `available = max(0, onHand − reserved)`, `reserved`, `onHand`.
- `listPrice` solo si hay una lista vigente no predeterminada cuyo nombre contiene «web», «oferta» o «lista» + «tienda», y su precio es mayor.
- Popularidad 1…10 con ventas facturadas de 90 días; etiquetas `destacado` (≥ 8), `oferta`, `nuevo` (30 días).
- Armados publicados: `PcBuild` con `PublishedToWeb`, estado `Quoted`/`Reserved`/`Sold`; `available` si todas las piezas alcanzan.
- La instantánea se arma completa en cada petición (sin caché de servidor).

### 4.4 Crear reserva: pasos de `CreateStorefrontReservationHandler`

1. `requestId = IdempotencyId(key)`: primeros 16 bytes del SHA-256 de `"storefront-reservation:" + key`.
2. `hash = ContentHash(request)`: líneas ordenadas por SKU, contacto (teléfono solo dígitos, correo en minúsculas), notas y nombre.
3. Busca `ProcessedRequest` (`iam.processed_requests`) por `RequestId`. Si existe con el mismo hash devuelve la respuesta guardada (`Replayed = true`); con otro hash lanza `IdempotencyConflictException`.
4. `StorefrontSlots.ResolveAsync`: valida que cada SKU exista (`NotFoundException`) y esté activo (`product.inactive`); decide la ranura: la que manda la web, o la de la ficha (clave de compatibilidad de `PcParts.SlotKeys`), o la de la categoría (`MON`→Monitor, `SRV`→Service, `SOFT`/`LIC`→Software, `STO`/`ALMC`→Storage, **cualquier otra → `Peripheral`**).
5. `PcParts.ResolveAsync` trae precio de lista, stock y ficha. Sin precio: `price.missing`.
6. `PcBuild.CreateWeb(...)` → `AddLine` por pieza → `Quote(..., acceptIncompatible: true)` → `Reserve(now, until, today, userId)`.
7. `PcBuildStock.ReserveAsync` (todo o nada).
8. Guarda el armado y el `ProcessedRequest` en el MISMO `SaveChangesAsync`.
9. Ante `ConcurrencyConflictException` reintenta hasta 3 veces con `db.ClearTracking()`.

### 4.5 Contrato JSON exacto de `POST /storefront/v1/reservations`

Cabecera obligatoria: `Idempotency-Key` (texto ≤ 100). Alternativa: campo `idempotencyKey` en el cuerpo.

Cuerpo (`StorefrontReservationRequest`):

```json
{
  "lines": [
    { "sku": "CPU-AMD-7600", "quantity": 1, "slot": "cpu" }
  ],
  "contact": { "name": "Valentina Aguirre", "phone": "+591 71234567", "email": "valentina.aguirre@correo.example" },
  "notes": "Paso el sabado por la manana",
  "name": "Mi PC gamer",
  "idempotencyKey": null
}
```

| Campo | Obligatorio | Validación |
|---|---|---|
| `lines` | sí | 1 a 20 |
| `lines[].sku` | sí | ≤ 60 |
| `lines[].quantity` | no (1) | 1 a 16 |
| `lines[].slot` | no | `cpu, motherboard, ram, gpu, storage, psu, case, cooler, monitor, peripherals` (también `peripheral`), `software`, `service`; otro valor → 422 `pcbuild.slot` |
| `contact.name` | sí | ≤ 120 |
| `contact.phone` | sí | ≤ 30; boliviano 7 u 8 dígitos, con o sin `+591` |
| `contact.email` | no | ≤ 254, formato de correo |
| `notes` | no | ≤ 500 |
| `name` | no | ≤ 150; por defecto «Armado web de <nombre>» |

Respuesta (`StorefrontReservationView`):

```json
{
  "number": "ARM-WEB-000004",
  "status": "Reserved",
  "statusText": "Reservada",
  "createdAt": "2026-09-27T18:39:56.7982846+00:00",
  "reservedUntil": "2026-09-29T18:39:56.7982846+00:00",
  "total": 4698.0,
  "contactName": "Valentina Aguirre",
  "branch": "CM",
  "notes": "Paso el sabado por la manana",
  "hasCompatibilityWarnings": false,
  "lines": [
    { "slot": "cpu", "sku": "CPU-AMD-7600", "name": "Procesador AMD Ryzen 5 7600 (...)", "quantity": 1, "unitPrice": 2049.0, "subtotal": 2049.0 }
  ],
  "cancelReason": null
}
```

- `status`: `Reserved`, `Sold`, `Cancelled`, `Expired`. `Expired` se muestra también mientras la reserva vencida espera al trabajo que la cierra.
- En la respuesta `Service` y `Software` salen ambos como `"software"` (`StorefrontCatalogReader.WebSlots`).
- Nunca devuelve teléfono ni correo.

| HTTP | Cuándo |
|---|---|
| 201 + `Location: /storefront/v1/reservations/{number}` | creada |
| 200 + `Idempotent-Replayed: true` | misma llave, mismo contenido |
| 400 `validation` | validación (`errors[]`), sin llave de idempotencia |
| 404 `not_found` | SKU inexistente |
| 409 `insufficient_stock`, `code = storefront.insufficient_stock` | `shortages[] = { sku, name, requested, available }`; no se reservó nada |
| 422 `domain` | `pcbuild.contact_phone`, `pcbuild.slot`, `price.missing`, `product.inactive`, `pcbuild.state` |
| 422 `idempotency` | misma llave, otro contenido |
| 429 | límite por IP |
| 503 | tienda sin configurar |

### 4.6 Consultar, cancelar y vencer

| Caso | Regla |
|---|---|
| Consultar | `GetStorefrontReservationHandler.FindAsync`: `Number` en mayúsculas, `Channel == Web` y `MatchesPhone`; si no coincide, `NotFoundException` con el mismo texto (regla S-06) |
| Cancelar | Solo desde `Reserved`; `PcBuildStock.ReleaseAsync` + `ReleaseReservation("Cancelada por el cliente desde la tienda web", ...)` |
| Vencer | `ExpirePcBuildReservationsHandler`: armados `Reserved` con `ReservedUntil <= now` → `ExpireAsync` + `ReleaseReservation(PcBuild.ExpiredReason, ..., expired: true)`; un solo `SaveChangesAsync` para todos |
| Quién vence | Solo `StorefrontReservationExpiryService` del gateway, como el principal de la tienda. Solo se registra si `storefront.IsConfigured` |

### 4.7 Auditoría y privacidad

- `CreateStorefrontReservationCommand.AuditDetails` enmascara el teléfono (`Mask`: deja los 3 últimos dígitos) y reemplaza el correo por `***`.
- El escritorio muestra teléfono y correo solo con `sales.pcbuild.manage` (`PcBuildViews.RowsAsync`).

---

## 5. Venta en caja

### 5.1 Venta normal: `CheckoutCommand` → `CheckoutHandler` → `SaleWriter.SellAsync`

Permisos: `sales.pos.operate` + `inventory.movements.register.sales`. Archivo: `SalesUseCases.cs`.

| Paso | Qué hace |
|---|---|
| 1 | Turno abierto del usuario (`PosSession` `Open` con `OpenedByUserId`); si no hay, `pos.closed` |
| 2 | Cliente por código (`CF` = consumidor final) y medio de pago; `payment.reference` si el medio la exige |
| 3 | Números `PV-<suc>-…` y `F-<suc>-…` |
| 4 | `new SalesOrder(...)` con el arco: turno de caja O almacén |
| 5 | Por línea: producto activo, cantidad válida, precio de la lista por defecto (o el de `unitPrices`), series con `SerialLedger.ExpectAsync` y `OnHandAsync` (una línea por posición si lleva serie) |
| 6 | `order.Confirm()` |
| 7 | Salida de stock: `level.Register(saleType, ...)` en la posición con más disponible del almacén de la caja; `line.LinkMovement`; costo promedio; `unit.Sell(...)` y fila `SalesOrderLineSerial` por serie |
| 8 | `order.MarkFulfilled()`; `Invoice` con `VatRules.Allocate`; `invoice.Issue(now)`; `order.MarkInvoiced()` |
| 9 | `Payment` (append-only) y vuelto (`payment.insufficient`) |
| 10 | Asiento con `JournalPoster.PostAsync`: caja o banco / ventas / IVA débito / costo de ventas / inventario |
| 11 | `db.Publish(new SaleCompletedEvent(...))` |
| 12 | Si la empresa factura: `FiscalIssuer.IssueForSaleAsync` en la misma transacción (regla F-03); sin serializador, `fiscal.no_serializer` |
| 13 | `CheckoutResult` con `FiscalDocumentId, FiscalNumber, Cuf, FiscalStatus`; la caja envía después `DispatchFiscalDocumentsCommand` |

`SaleWriter.SellAsync` NO guarda: guarda el manejador, con reintento optimista ×3.

### 5.2 Armado reservado: `SellPcBuildCommand` → `SellPcBuildHandler`

Firma: `SellPcBuildCommand(string Number, string PaymentMethodCode, IReadOnlyList<SkuSerials>? Serials, decimal? CashReceived, string? PaymentReference, FiscalBuyerInput? Buyer, string? CardNumber, string? CustomerCode)`.

| Paso | Qué hace |
|---|---|
| 1 | Carga el armado por número; rechaza `Draft` (`pcbuild.not_quoted`), estados cerrados (`pcbuild.state`) y cotización vencida (`pcbuild.expired`) |
| 2 | Si está `Reserved`: `PcBuildStock.FulfillAsync` (libera lo reservado, sin movimiento) |
| 3 | Turno abierto del usuario; la caja debe ser de la sucursal del armado (`pcbuild.branch`) |
| 4 | Cliente: `request.CustomerCode`, o el del armado, o `"CF"` |
| 5 | Una `SaleLineInput` por línea del armado, con las series repartidas por SKU; series sobrantes → `SerialErrorCodes.Count` |
| 6 | `SaleWriter.SellAsync(..., prices)` con los precios COTIZADOS |
| 7 | `build.MarkSold(sale.Invoice.Id, today, userId, clock.UtcNow)` |

En el escritorio: `PosViewModel` recibe `PcBuildToSell(Number)`, carga el carrito con `GetPcBuildQuery`, bloquea las líneas y cobra con `SellPcBuildCommand`.

### 5.3 Datos del comprador para la factura

- `FiscalBuyerInput(int DocumentType, string DocumentNumber, string? Complement, string? Name, string? Email, bool ExceptionRequested = false)`.
- `FiscalIssuer.ResolveBuyerAsync`: usa lo capturado; si no hay, la identidad fiscal del cliente; si no hay ninguna, `fiscal.buyer_required`.
- La reserva web NO guarda documento ni razón social: el cajero los captura al cobrar.

---

## 6. La web hoy (lo que importa para el carrito)

| Hecho verificado | Archivo |
|---|---|
| No existe carrito: el único estado de compra es el armado (`BuilderProvider`, `buildReducer`) | `4-presentation/state/BuilderProvider.tsx`, `1-domain/builder/build.ts` |
| Rutas: `/`, `/catalogo`, `/catalogo/:categoria`, `/producto/:slug`, `/arma-tu-pc`, `/reserva`, `/reserva/:numero` | `4-presentation/app/router.tsx`, `routes.ts` |
| El formulario de reserva (`ReserveDialog`) solo se usa en `BuilderPage` | `4-presentation/pages/builder/BuilderPage.tsx` |
| Un producto sin ranura NO se puede agregar: `slotForProduct` devuelve `undefined` y `add` avisa «Este producto no forma parte de un armado de PC» | `1-domain/builder/slots.ts`, `BuilderProvider.tsx` |
| En la ficha de un producto sin ranura solo hay «Consultar por WhatsApp» | `4-presentation/pages/product/PurchaseBox.tsx` |
| Categorías con ranura: `CPU, MB, RAM, GPU, STO, PSU, CASE, COOL, MON, KEY, MOU, AUD, PAD, CAM, CHA, MAND, LIC, SRV` | `slots.ts` |
| Sin ranura (no se reservan desde la web): consolas, juegos, portátiles, equipos armados, redes, cables, cargadores | `slots.ts` (no aparecen) |
| Un monitor SÍ se puede reservar hoy, pero solo pasando por `/arma-tu-pc` | `slots.ts` (ranura `monitor`) |
| Tope web `MAX_QUANTITY = 10`; tope del servidor 16 | `build.ts`, `PcBuild.MaxQuantity` |
| La web marca `ram`, `gpu` y `monitor` como ranura única; el dominio las admite múltiples | `slots.ts`, `PcBuild.MultiSlots` |
| `RESERVATION_HOURS = 48` es una constante fija de la web | `1-domain/storefront/types.ts` |
| El armado vive en memoria; la regla S-07 prohíbe red y storage fuera de `3-infrastructure/http` | `src/architecture.test.ts` |
| Formulario actual: nombre, teléfono, correo (opcional), notas | `ReserveDialog.tsx`, `1-domain/storefront/contact.ts` |

**El servidor ya acepta cualquier producto.** Si la línea llega sin `slot`, `StorefrontSlots.ResolveAsync` la deja en `Peripheral`, que es ranura múltiple. El límite está en la web, no en la API. Excepción: dos productos distintos que caen en una ranura única (dos procesadores, dos placas, dos fuentes, dos gabinetes, dos refrigeraciones) dan 422 `pcbuild.slot`.

---

## 7. Correo: lo que ya existe

| Pieza | Detalle |
|---|---|
| Puerto | `IMailSender.SendAsync(MailMessageSpec message, CancellationToken)` |
| Tipos | `MailServer(Host, Port, UseSsl, UserName, Password, FromAddress, FromName)`, `MailAttachment(FileName, ContentType, Content)`, `MailMessageSpec(Server, To, Subject, HtmlBody, Attachments)` |
| Implementación | `SmtpMailSender` con `System.Net.Mail.SmtpClient`, `EnableSsl = server.UseSsl`, tiempo máximo 30 s |
| Configuración | `MailSettings` → `billing.mail_settings` (una fila por empresa, PK `tenant_id`): `Host, Port, UseSsl, UserName, PasswordCiphertext, PasswordKeyId, FromAddress, FromName, IsEnabled` |
| Contraseña | cifrada con `ISecretProtector` (clave maestra `MINV_INTEGRATION_KEYS`); se descifra solo en memoria |
| Lectura | `BuyerMail.ServerAsync(db, protector, ct)` (`internal static`, mismo ensamblado `MINV.Application`); devuelve `null` si `IsEnabled` es falso |
| Guardar configuración | `SaveMailSettingsCommand(Host, Port, UseSsl, UserName, NewPassword, FromAddress, FromName, Enabled)`: exige `billing.configure` y el módulo `LicenseModuleCodes.FiscalSiat` |
| Registro en el gateway | Con PostgreSQL, `AddMinvInfrastructure` registra `IMailSender` e `ISecretProtector`. Con `Minv:Storage=memoria` NO se registra `IMailSender` |
| Docker | `deploy/docker-compose.yml` exige `MINV_INTEGRATION_KEYS` en `cloudserver` y `apigateway` |
| Datos de prueba | `SiatSeeding` guarda el correo DESACTIVADO con servidor `smtp.<dominio>` de `.example` |
| Bitácora de entregas | `FiscalDelivery` es solo de documentos fiscales; no hay registro de entregas para reservas |

No existe hoy ningún envío de correo al reservar. No encontré plantilla, caso de uso ni tabla para ello.

---

## 8. Análisis para el carrito de la V7

Requisito: reservar cualquier producto (por ejemplo, un solo monitor) desde un carrito, sin pasar por «Armá tu PC», con datos del cliente y correo de confirmación.

### 8.1 Opción A · Reutilizar `PcBuild` con un tipo (Armado | Carrito)

| Aspecto | Detalle |
|---|---|
| Tablas y columnas | `sales.pc_builds`: columna nueva `kind` varchar(10) NOT NULL (`Build` \| `Cart`) con relleno `Build` y CHECK. `sales.pc_build_lines.slot`: pasa a admitir NULL (una línea de carrito no tiene ranura). Sin tablas nuevas |
| Numeración | Prefijo nuevo para el carrito (por ejemplo `RES-WEB-000001`); `Documents.NextNumberAsync` filtra por prefijo, no choca con `ARM-WEB-` |
| Dominio | `enum PcBuildKind`; fábrica nueva (por ejemplo `CreateWebCart`) para no tocar `CreateWeb`; `AddLine` no aplica la regla de ranura única si es carrito; `Quote` con informe vacío para carrito |
| `StockReservation` | Sin cambios: sigue el origen `PcBuildLineId` |
| Aplicación | `CreateStorefrontReservationCommand` gana un campo opcional de tipo (por defecto armado); `PcBuildStock`, cancelar, consultar y vencer se reutilizan tal cual |
| Escritorio | Las reservas de carrito aparecen en Armador de PC › Cotizaciones. Hace falta columna y filtro por tipo, y ocultar la compatibilidad en los carritos. `GetPcBuildsQuery` y `PcBuildRow` ganan el tipo |
| Caja | `SellPcBuildCommand` sirve sin cambios (líneas al precio congelado, series al cobrar) |
| Facturación | Sin cambios: el comprador se captura al cobrar (`FiscalBuyerInput`) |
| Normalización | `kind` es un atributo propio del agregado. `slot` NULL en carrito evita guardar un valor derivado de la categoría. La regla «armado exige ranura» cruza dos tablas: va en el dominio y, en la base, en un trigger (regla A-06) |
| Regla S-03 | Se cumple literalmente (la reserva sigue siendo `PcBuild` + una `StockReservation` por línea, en una transacción) |
| Regla S-04 | Se cumple: mismos estados y misma bitácora |
| Regla T-06 | Hay que aclararla: la compatibilidad se evalúa solo en armados; un carrito no es una cotización de PC |
| Eventos | `pcbuild.*` siguen iguales; se agrega el tipo como campo nuevo (regla B-08 permite agregar campos) |
| Contrato público | Compatible: solo se agregan campos opcionales a `/storefront/v1` |
| Pruebas que se romperían | Ninguna si el tipo por defecto es armado y no cambia la firma de `CreateWeb`. A revisar: `ModelTests` (exige migración nueva y listas), `V6StorefrontPostgresTests` (CHECK de líneas si `slot` admite NULL) |
| Costo | Bajo |
| Desventaja | El nombre `pc_builds` guarda cosas que no son PC |

### 8.2 Opción B · `SalesOrder` / `SalesOrderLine` con canal Web

| Aspecto | Detalle |
|---|---|
| Tablas y columnas | `sales.sales_orders`: faltan `channel`, `contact_name`, `contact_phone`, `contact_email`, `notes`, `reserved_at`, `reserved_until`, `cancel_reason`; estado nuevo `Reserved`. Tabla nueva de bitácora (la regla S-04 exige bitácora append-only). `ck_sales_orders_origen` hay que cambiarlo: hoy exige turno de caja O almacén |
| Cliente | `CustomerId` es obligatorio: habría que crear un `Customer` por cada visitante o usar `CF`. El principal `TIENDA_WEB` no tiene `sales.customers.manage` y la regla S-02 prohíbe exponer clientes |
| `StockReservation` | El origen `SalesOrderLineId` ya existe, pero ningún código lo usa hoy |
| Conversión a venta | `SaleWriter.SellAsync` siempre CREA un pedido nuevo. Habría que reescribirlo para facturar un pedido existente y usar `StockLevel.Consume` |
| Impacto | `SaleWriter` lo usan la caja, el armado, el pedido B2B, la transcripción CAFC y los datos de prueba: es el código más sensible del sistema |
| Escritorio | Pantalla nueva de pedidos web; el historial de ventas une con factura, así que un pedido sin factura no aparece (verificado en `GetSalesHandler`) |
| Caja | Comando nuevo para cobrar un pedido reservado |
| Facturación | El documento fiscal nace de la venta; hay que garantizar una sola factura por pedido (único `sales_order_id`) |
| Normalización | Correcta en teoría (un pedido es un pedido), pero mezcla en una tabla ventas cobradas y reservas sin cobrar |
| Regla S-03 | Hay que reescribirla (hoy dice «DEBE crear el `PcBuild`») |
| Regla S-04 | Hay que reescribirla para los estados del pedido |
| Regla T-06 | No cambia |
| Pruebas que se romperían | Todas las que pasan por `SaleWriter` si cambia su flujo: `LocalDataSeederTests`, pruebas de facturación (`tests/MINV.Infrastructure.Tests/Billing/`), `StorefrontFlowTests`, `StorefrontApiTests` (esperan `ARM-WEB-` y `PcBuild`), `StorefrontScreenTests`, `ModelTests` |
| Costo | Alto, con riesgo de regresión en ventas y facturación |

### 8.3 Opción C · Agregado nuevo

| Aspecto | Detalle |
|---|---|
| Tablas y columnas | Tres tablas nuevas en `sales` (cabecera, líneas, bitácora): 153 → 156. Cuarta columna de origen en `inventory.stock_reservations` y nuevo `ck_stock_reservations_origen` |
| Migración | Políticas `tenant_isolation` y `branch_isolation`, triggers append-only, privilegios de `minv_app` y `minv_server`, listas de tablas nuevas (regla B-15), ERD |
| Dominio y aplicación | Máquina de estados, reservar, liberar, vencer, cumplir, vender y eventos: todo duplicado de `PcBuild` y `PcBuildStock` |
| Escritorio | Pantalla nueva; la caja necesita otro comando de venta |
| Vencimiento | Segundo comando de vencimiento y segundo recorrido del trabajo en segundo plano |
| Normalización | La más limpia: cada concepto en su tabla, sin columnas que no aplican |
| Reglas S-03 / S-04 | Hay que ampliarlas a un segundo agregado |
| Regla T-06 | No cambia |
| Pruebas que se romperían | `ModelTests.El_modelo_tiene_153_tablas_en_10_esquemas`, `ModelTests.El_ERD_documenta_todas_las_tablas_del_modelo`, las de listas de tablas, `V6StorefrontPostgresTests` (texto del arco) |
| Costo | Alto; dos mecanismos de reserva conviviendo |

### 8.4 Comparación

| Criterio | A · `PcBuild` con tipo | B · `SalesOrder` | C · Agregado nuevo |
|---|---|---|---|
| Tablas nuevas | 0 | 1 | 3 |
| Columnas nuevas | 1 (+ `slot` admite NULL) | 8 o más | 1 en `stock_reservations` + las de las tablas nuevas |
| Toca `SaleWriter` | no | sí | no |
| Toca el arco de `StockReservation` | no | no | sí |
| Reutiliza vencimiento, cancelación y venta | sí | no | no |
| Cambia reglas S-03 / S-04 | no | sí | sí (amplía) |
| Riesgo de regresión | bajo | alto | medio |
| Limpieza semántica | media | media | alta |

### 8.5 Recomendación

**Opción A.** Motivos:
1. La API ya reserva cualquier producto; falta quitar la regla de ranura única para carritos y dar nombre y número propios.
2. Reutiliza todo lo probado de punta a punta: reserva todo o nada, idempotencia, vencimiento, cancelación, venta que consume la reserva, bitácora y eventos.
3. No toca `SaleWriter` ni el arco de reservas.
4. El contrato público sigue compatible.

Condiciones para que A quede bien:
- El tipo por defecto debe ser armado, para que las pruebas y la web actual sigan igual.
- En la web, el carrito debe ser un estado separado del armador (contexto propio), con su propia página y su propio formulario de reserva. Desde la ficha: «Agregar al carrito» y «Reservar ahora» (carrito de una línea).
- En el escritorio, separar visualmente «Reservas de la tienda» de «Cotizaciones de PC».

### 8.6 Datos del cliente

| Dato | Hoy en la reserva | Hace falta en la V7 |
|---|---|---|
| Nombre | obligatorio (`contact_name`, ≤ 120) | igual |
| Teléfono | obligatorio, boliviano (`contact_phone`) | igual |
| Correo | opcional (`contact_email`, ≤ 254) | opcional; si viene, dispara el correo de confirmación |
| Notas | opcional (`notes`, ≤ 500) | igual |
| Tipo de documento (1 CI, 2 CEX, 3 PAS, 4 OD, 5 NIT) | no se pide | opcional, para precargar la factura |
| Número de CI o NIT | no se pide | opcional; CI y NIT solo dígitos (`FiscalRules.EnsureBuyerDocument`) |
| Complemento del CI | no se pide | opcional, solo con CI, ≤ 5 |
| Nombre o razón social para la factura | no se pide | opcional |
| Dirección, ciudad | no se pide | no hace falta (retiro en tienda) |

Si se guardan los datos de factura en la reserva, son una instantánea del visitante anónimo (igual que el contacto). Deben seguir la regla S-06: enmascarados en la auditoría, nunca en la API pública, visibles solo con `sales.pcbuild.manage`.

### 8.7 Correo de confirmación: restricciones de diseño

| Restricción | Origen |
|---|---|
| El correo NO puede enviarse dentro del caso de uso ni antes del COMMIT | regla B-08 |
| Una petición repetida (`Replayed = true`) no debe reenviar el correo | regla S-05 |
| El evento `pcbuild.reserved` no lleva el correo: quien envía debe leer el armado | `DomainEvents.cs` |
| Hace falta registrar cada intento de envío (logrado o fallido) | no existe tabla ni acción de bitácora para esto |
| `SmtpClient` de .NET usa STARTTLS con `EnableSsl`; no admite TLS implícito | `SmtpMailSender.cs` |
| La contraseña del correo va cifrada en `billing.mail_settings`, nunca en el repositorio | regla F-12 |
| Guardar la configuración exige el módulo de facturación activo | `SaveMailSettingsCommand` |

---

## 9. Riesgos y hallazgos

| # | Hallazgo | Estado | Archivo |
|---|---|---|---|
| 1 | El vencimiento corre solo en el gateway y solo ve la sucursal de la tienda. Una reserva hecha desde el escritorio en otra sucursal no vence sola | Verificado en código | `BackgroundServices.cs`, `StorefrontAuthenticationHandler.cs`, `ModelConventions.cs` |
| 2 | Si la tienda no está configurada, el trabajo de vencimiento ni se registra: ninguna reserva vence | Verificado | `ApiGatewayApp.cs` |
| 3 | La reserva reparte entre posiciones de la SUCURSAL; la venta descuenta de UNA posición del ALMACÉN de la caja. Con stock repartido, la venta de una reserva puede fallar por falta de stock | Plausible | `PcBuildStock.ReserveAsync`, `SaleWriter.SellAsync` |
| 4 | La venta elige la posición ordenando por disponible leído de la base, antes de guardar el `Fulfill` | Plausible | `SaleWriter.SellAsync` |
| 5 | Una cotización puede quedar reservada más allá de su vigencia: no se puede vender (`pcbuild.expired`) y el stock sigue reservado hasta que venza la reserva | Verificado | `PcBuild.Reserve`, `PcBuild.IsExpiredOn` |
| 6 | La web muestra «48 horas» fijo; si cambia `ReservationHours`, el texto queda mal | Verificado | `1-domain/storefront/types.ts` |
| 7 | Tope de cantidad distinto: web 10, servidor 16 | Verificado | `build.ts`, `PcBuild.cs` |
| 8 | El número de reserva es correlativo y la clave es el teléfono: quien conoce el teléfono de alguien puede recorrer números y cancelar su reserva | Verificado | `GetStorefrontReservationHandler.FindAsync` |
| 9 | La consulta de reserva usa el límite de lecturas (300/min), no el de reservas (10/min) | Verificado | `StorefrontEndpoints.cs` |
| 10 | Un correo automático en una ruta pública permite hacer que la tienda envíe correos a direcciones ajenas | Riesgo de diseño V7 | — |
| 11 | La instantánea del catálogo se calcula entera en cada petición | Verificado (límite documentado) | `StorefrontCatalogReader.SnapshotAsync` |
| 12 | El vencimiento guarda todas las reservas vencidas en un solo guardado: un conflicto repite todo el lote | Verificado | `ExpirePcBuildReservationsHandler` |
| 13 | Los orígenes `pos_session_id` y `sales_order_line_id` de la reserva y `StockLevel.Consume` no tienen uso en la aplicación | Verificado | búsqueda en `src` |
| 14 | El ERD §3 todavía describe el arco con dos orígenes; el §10 lo corrige | Verificado | `docs/database/ERD-MINV-V3.md` |

---

## 10. Pruebas existentes del subsistema

| Archivo | Pruebas |
|---|---|
| `tests/MINV.Domain.Tests/Tech/PcBuildTests.cs` | 7 (líneas y ranura única, cotizar, incompatibles, vender, anular, redondeo) |
| `tests/MINV.Domain.Tests/Tech/PcBuildReservationTests.cs` | 6 (contacto y teléfono, reservar, liberar/vencer/vender, publicar, 20 líneas, arco de origen) |
| `tests/MINV.Domain.Tests/StockTests.cs` | reservas, liberar, vencer, consumir |
| `tests/MINV.Infrastructure.Tests/StorefrontFlowTests.cs` | 4 (instantánea, flujo completo, sin stock y contacto inválido, escritorio) |
| `tests/MINV.Infrastructure.Tests/V6StorefrontPostgresTests.cs` | 2 (aprovisionamiento y restricciones, bitácora por sucursal) |
| `tests/MINV.Infrastructure.Tests/ModelTests.cs` | `El_modelo_tiene_153_tablas_en_10_esquemas`, `El_ERD_documenta_todas_las_tablas_del_modelo`, FK con sucursal, `xmin` |
| `tests/MINV.Integration.Tests/StorefrontApiTests.cs` | 7 (catálogo y CORS, producto e imagen, reservar idempotente, 409, vencimiento, venta en caja, límites por IP) |
| `tests/MINV.DesktopClient.Tests/StorefrontScreenTests.cs` | 4 (armador, reservar/publicar/liberar, caja, reservado en stock) |
| Web | `ReserveDialog.test.tsx`, `ReservationPage.test.tsx`, `build.test.ts`, `contact.test.ts`, `storefront.test.ts`, `http.test.ts`, `architecture.test.ts` |

No hay pruebas de punta a punta de navegador dentro del repositorio (no encontré carpeta de Playwright en `MINV.WebCatalog`).

---

## 11. Lo que no pude confirmar

- Si las vistas `reporting.v_*` cuentan pedidos sin factura (importa solo para la opción B).
- Si alguna sucursal de los datos de prueba tiene más de un almacén o stock en varias posiciones (condición del riesgo 3). El aprovisionamiento crea un almacén `ALM01`.
- El comportamiento real del riesgo 4 en PostgreSQL: no ejecuté nada.
- Qué código HTTP recibe la web si se agotan los 3 reintentos por conflicto de numeración.
