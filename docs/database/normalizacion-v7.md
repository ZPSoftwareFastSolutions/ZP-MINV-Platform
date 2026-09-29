# Comprobación de normalización de la base de datos · M-INV V7

> Pedido del cliente: «la base de datos tiene que estar normalizada, así que haz una comprobación de eso». Paquete B4c de la
> rama `Inventario-V7` (versión 7.0.0-alpha.1, migración `V7WebPlatform`: **157 tablas en 10 esquemas**). Consultas:
> `scripts/verificar_normalizacion.sql`. Prueba automática: `MINV.Infrastructure.Tests.NormalizationTests`. Modelo de datos:
> `docs/database/ERD-MINV-V3.md`.

## 1. En pocas palabras (para el dueño del negocio)

- **Sí, la base está normalizada.** Cada dato vive en un solo lugar: el stock sale de los movimientos, los totales de una venta
  o de una factura se calculan de sus líneas, los permisos de una persona salen de sus roles. Se revisaron las 157 tablas con
  19 reglas de estructura y 10 reglas sobre los datos de la empresa de prueba (Tech Zone Gaming, 20 días de operación).
- Donde un dato está guardado dos veces es **a propósito y está controlado**: por ejemplo el stock actual de cada producto se
  guarda para que dos cajas no vendan la última unidad a la vez, pero una consulta comprueba que siempre coincide con la
  suma de los movimientos; o el precio de una cotización queda congelado porque es la oferta que se le hizo al cliente. Las
  10 comprobaciones de datos dieron **cero diferencias**.
- Se encontraron y **se corrigieron 26 cosas** en esta versión: 25 reglas que ahora impiden guardar un estado o un tipo que no
  existe (por ejemplo, un pedido de compra solo puede estar «Borrador», «Aprobado», «Recibido parcialmente», «Recibido» o
  «Anulado») y un índice que faltaba para buscar las garantías de una serie.
- Quedan **7 detalles menores sin corregir** (sección 6), todos sin riesgo de perder o mezclar datos: se explican con su
  riesgo y con lo que haría falta para corregirlos.

## 2. Las formas normales, con ejemplos de esta base

Cada forma normal incluye a las anteriores: una tabla en 3FN también está en 1FN y 2FN.

| Forma | Qué exige | Ejemplo en M-INV |
|---|---|---|
| **1FN** | Cada celda guarda un solo valor y cada fila se identifica por su clave; nada de listas dentro de una columna ni columnas repetidas (`telefono1`, `telefono2`). | Los impuestos de un producto no van como «IVA, IT» en una columna: van en `catalog.product_taxes`, una fila por impuesto. Las series vendidas en una línea de venta van en `sales.sales_order_line_serials`, una fila por serie. |
| **2FN** | En una tabla con clave compuesta, cada dato depende de la clave COMPLETA, no de una parte. | En `sales.price_list_items` la clave es (lista de precios, variante) y el precio depende de las dos; el nombre del producto (que depende solo de la variante) no se repite ahí. |
| **3FN** | Ningún dato depende de otro dato que no sea la clave (sin dependencias «en cadena»). | Una existencia (`inventory.stock_levels`) guarda su lote y su posición, pero no la variante ni el almacén: la variante sale del lote y el almacén de la posición (`bins → shelves → racks → aisles → zones → warehouses`). |
| **FNBC** (Boyce-Codd) | Todo dato que determina a otros es una clave candidata (con su unicidad). | `sales.customer_accounts` tiene dos claves candidatas, (empresa, usuario) y (empresa, cliente), y las dos tienen índice único: un usuario tiene a lo sumo una cuenta y un cliente también. |
| **4FN** | No se mezclan en una tabla dos listas independientes de la misma cosa. | Los roles de un usuario (`iam.user_roles`) y las sucursales donde trabaja (`warehouse.branch_users`) van en tablas separadas; juntos obligarían a guardar todas las combinaciones rol × sucursal. |
| **5FN** | No se guarda lo que sale de reunir otras tablas. | No existe una tabla «permisos de un usuario»: se obtiene reuniendo `iam.user_roles` con `iam.role_permissions`. Guardarla repetiría información y podría contradecir a las otras dos. |

## 3. Qué se revisó y cómo repetirlo

**Qué.** El script `scripts/verificar_normalizacion.sql` tiene 32 consultas de solo lectura, cada una con un comentario que
explica su regla:

- **E01 a E19 (estructura, sobre `pg_catalog`)**: claves primarias, `tenant_id` y RLS por empresa, RLS por sucursal, libros
  append-only con sus triggers y privilegios, FK con índice, FK compuestas con la empresa y con la sucursal, textos sin
  límite, totales o contadores guardados, JSON, claves naturales sin unicidad, estados y listas cerradas sin CHECK, CHECK
  duplicados, columnas anulables dentro de una unicidad, grupos repetidos, copias de datos de la tabla referenciada y
  booleanas derivables.
- **D01 a D10 (datos)**: que cada redundancia controlada cuadre con lo que dice su fuente (existencias = movimientos,
  reservado = reservas activas, estado = bitácora, intentos de la cola = filas de intentos…).
- **I01 a I03 (informativas)**: resumen del esquema, sucursales como dato de contexto e instantáneas deliberadas.

Las de clase «problema» devuelven filas SOLO cuando algo no cumple; las que son decisiones de diseño justificadas están en la
tabla de excepciones de la sección 5.2.

**Dónde.** En un PostgreSQL 16.15 temporal y aislado (no se usó la base `minv` ni el servidor de localhost:5432): base nueva
migrada con `minv migrate` hasta `20260929025923_V7WebPlatform` y cargada con
`minv datos-prueba --codigo TECHZONE --dias 20 --dias-facturacion 10` (1638 movimientos, 235 documentos fiscales, 17 armados y
carritos, 17 transferencias, 7 casos de garantía, 1738 series, 26 reservas activas, 9 correos en la cola y 1906 filas de
auditoría). Dos corridas: **antes** (la migración V7 que dejó B4a) y **después** (con las correcciones de la sección 5.1).
El script tarda menos de medio segundo.

**Cómo repetirlo.** Con el dueño de la base (`minv_owner`) o un superusuario, porque un rol sujeto a RLS no ve las filas y las
comprobaciones de datos saldrían vacías sin serlo:

```powershell
# La conexión sale de variables de entorno: la contraseña no queda escrita en la orden ni en el historial
$env:PGHOST = 'localhost'; $env:PGPORT = '5432'; $env:PGUSER = 'minv_owner'; $env:PGPASSWORD = '<contraseña>'
psql -X -v ON_ERROR_STOP=1 -d minv -f scripts/verificar_normalizacion.sql
```

Prueba automática (sin PostgreSQL revisa el script, la tabla de excepciones y que todo enum tenga su CHECK en el modelo; con
`MINV_TEST_PG` ejecuta las consultas, comprueba que detectan una tabla mal diseñada y datos incoherentes, y que los datos de
prueba cuadran):

```powershell
$env:MINV_TEST_PG = '<cadena de un rol que puede crear bases>'
dotnet test tests/MINV.Infrastructure.Tests --filter "FullyQualifiedName~NormalizationTests"
```

## 4. Resultado de cada consulta

| Consulta | Regla | Antes | Después | Filas que quedan |
|---|---|---:|---:|---|
| E01 | Tablas sin clave primaria | 0 | 0 | — |
| E02 | Tablas de negocio sin `tenant_id` | 2 | 2 | `iam.modules`, `iam.tenants` (plataforma) |
| E03 | Tablas con `tenant_id` sin RLS de empresa | 0 | 0 | — (155 políticas `tenant_isolation`) |
| E04 | Tablas de sucursal sin `branch_isolation` | 3 | 3 | `billing.siat_branches`, `warehouse.branch_users`, `warehouse.warehouses` (directorio y configuración) |
| E05 | Libros append-only sin trigger o con privilegios de más | 0 | 0 | — (32 libros con sus dos triggers; `minv_app` y `minv_server` sin UPDATE/DELETE/TRUNCATE) |
| E06 | FK sin índice que la cubra | **1** | 0 | — (corregido: `service.warranty_claims (tenant_id, serial_number_id)`) |
| E07 | FK a tablas de negocio no compuestas con `tenant_id` | 0 | 0 | — (432 FK revisadas) |
| E08 | FK entre tablas de sucursal sin la sucursal | 0 | 0 | — |
| E09 | Columnas de texto sin límite | 3 | 3 | `fiscal_document_files.xml`, `fiscal_packages.messages`, `processed_requests.response` |
| E10 | Nombres que sugieren un total o un contador | 7 | 7 | ver 5.2 |
| E11 | Columnas JSON | 5 | 5 | ver 5.2 |
| E12 | Claves naturales sin unicidad | 4 | 4 | `siat_cufds.code`, `siat_cuis.code`, `supplier_contacts.email`, `customers.email` |
| E13 | Columnas de estado sin CHECK | **14** | 0 | — (corregido: 14 CHECK) |
| E14 | Listas cerradas sin CHECK | **14 + 1** | 4 | `fiscal_documents.kind`, `siat_activities.activity_type`, `siat_activity_sectors.sector_type`, `api_key_scopes.scope` |
| E15 | CHECK duplicados | 0 | 0 | — |
| E16 | Columnas anulables dentro de una unicidad | 0 | 0 | — (las 14 unicidades de tablas con una columna anulable son parciales «IS NOT NULL» o NULLS NOT DISTINCT) |
| E17 | Grupos repetidos (columnas numeradas o arreglos) | 0 | 0 | — |
| E18 | Copias de un dato de la tabla referenciada | 1 | 1 | `fiscal_documents.customer_code` |
| E19 | Booleanas que un CHECK iguala a otra columna | 3 | 3 | `fiscal_documents.is_reverted`, `outgoing_mail_attempts.succeeded`, `webhook_endpoints.is_active` |
| D01-D10 | Coherencia de las redundancias controladas con los datos | 0 | 0 | — (existencias, reservado, transferencias, series, estados de armados, transferencias y garantías, cola del correo, outbox, ranuras) |
| I01 | Resumen | — | — | 157 tablas, 10 esquemas, 432 FK, 219 → **244** CHECK, 480 índices únicos, 155 `tenant_isolation`, 64 `branch_isolation`, 32 libros, 6 SECURITY DEFINER, 3 vistas de control |

E14 «14 + 1»: la primera corrida dio 14 filas; la prueba de modelo (todo enum guardado como texto tiene su CHECK) encontró
además `inventory.movement_types.domain`, cuyo nombre no estaba en la lista de la consulta. Se corrigió y se agregó
`domain` al patrón de E14. Las 25 restricciones y el índice nuevos se probaron también sobre la base ANTES de corregir, ya
cargada con los 20 días de datos (en una transacción que se deshizo): se aplicaron sin ningún error, así que los datos
existentes ya las cumplen.

## 5. Hallazgos y su clasificación

Cada hallazgo se clasifica como:

- **Redundancia controlada ya documentada**: un dato guardado dos veces a propósito, con la sección del ERD que lo explica y,
  cuando se puede, una comprobación de datos que demuestra que cuadra.
- **Excepción de diseño ya documentada**: la regla de la consulta no aplica (no es una tabla de negocio, no es una clave, es
  una lista abierta o ya está acotada de otra forma); no es redundancia ni incumplimiento.
- **Incumplimiento real**: no cumple la regla. Se dice si se corrigió en esta versión o si queda pendiente y con qué riesgo.

### 5.1 Incumplimientos reales corregidos en esta versión

Todos en la migración `V7WebPlatform` (todavía no publicada), declarados en las configuraciones de EF Core, con su reversa en
`Down`, `scripts/db_init.sql` regenerado y el ERD al día (§11.7). Los valores de los CHECK salen de los enums del dominio
(`BillingChecks.In<TEnum>`), que no cambiaron desde la V3 (solo `TransferStatus` cambió en la V4, con su guardia).

| Consulta | Tabla y columna | Corrección |
|---|---|---|
| E06 | `service.warranty_claims (tenant_id, serial_number_id)` | Índice `ix_warranty_claims_tenant_id_serial_number_id`. Solo existía el índice único parcial de los casos abiertos: el historial de garantías de una serie (y la comprobación de la FK al borrar una serie) recorría la tabla. |
| E13 | `accounting.fiscal_periods.status` | `ck_fiscal_periods_estado`: Open, Closed |
| E13 | `accounting.journal_entries.status` | `ck_journal_entries_estado`: Draft, Posted |
| E13 | `inventory.physical_counts.status` | `ck_physical_counts_estado`: Open, Posted, Cancelled |
| E13 | `inventory.stock_adjustments.status` | `ck_stock_adjustments_estado`: Draft, Posted, Cancelled |
| E13 | `inventory.stock_reservations.status` | `ck_stock_reservations_estado`: Active, Consumed, Released, Expired |
| E13 | `inventory.stock_transfer_events.status` | `ck_stock_transfer_events_estado`: Pending, Dispatched, Received, Cancelled |
| E13 | `purchasing.goods_receipts.status` | `ck_goods_receipts_estado`: Draft, Posted |
| E13 | `purchasing.purchase_orders.status` | `ck_purchase_orders_estado`: Draft, Approved, PartiallyReceived, Received, Cancelled |
| E13 | `purchasing.purchase_returns.status` | `ck_purchase_returns_estado`: Draft, Posted |
| E13 | `purchasing.supplier_invoices.status` | `ck_supplier_invoices_estado`: Draft, Posted, Paid, Cancelled |
| E13 | `sales.invoices.status` | `ck_invoices_estado`: Draft, Issued, Voided |
| E13 | `sales.pos_sessions.status` | `ck_pos_sessions_estado`: Open, Closed |
| E13 | `sales.sales_orders.status` | `ck_sales_orders_estado`: Draft, Confirmed, Fulfilled, Invoiced, Cancelled |
| E13 | `service.warranty_claim_events.status` | `ck_warranty_claim_events_estado`: los 7 estados del caso |
| E14 | `accounting.accounts.account_type` | `ck_accounts_tipo`: Asset, Liability, Equity, Revenue, Expense |
| E14 | `billing.fiscal_packages.document_type` | `ck_fiscal_packages_tipo`: 1, 3 (como `fiscal_documents`) |
| E14 | `billing.siat_service_calls.environment` | `ck_siat_service_calls_ambiente`: 1, 2 |
| E14 | `billing.siat_sync_runs.environment` | `ck_siat_sync_runs_ambiente`: 1, 2 |
| E14 | `catalog.products.tracking_mode` | `ck_products_trazabilidad`: None, Batch, Serial |
| E14 | `iam.audit_logs.outcome` | `ck_audit_logs_resultado`: Succeeded, Rejected, Failed |
| E14 | `iam.hardware_tokens.kind` | `ck_hardware_tokens_tipo`: Workstation, PosTerminal, Scanner, Printer |
| E14 | `inventory.movement_types.domain` | `ck_movement_types_dominio`: Warehouse, Sales (lo encontró la prueba de modelo) |
| E14 | `purchasing.supplier_addresses.address_type` | `ck_supplier_addresses_tipo`: Fiscal, Billing, Shipping, Pickup |
| E14 | `sales.cash_movements.direction` | `ck_cash_movements_sentido`: In, Out |
| E14 | `sales.customer_addresses.address_type` | `ck_customer_addresses_tipo`: Fiscal, Billing, Shipping, Pickup |

Por qué es «sin riesgo»: los datos de estas columnas los escribe siempre el dominio con esos mismos enums (regla A-13: los
datos de prueba y los reales entran por los casos de uso). Si una base tuviera una fila fuera de la lista (solo posible
editándola a mano), el CHECK detiene la migración entera y la base queda en la V6 sin cambios (la migración corre en una
transacción); la guía de migraciones §11 trae la consulta para revisarlo antes.

### 5.2 Excepciones documentadas

Es la lista que usa la prueba automática: una fila de una consulta «problema» que no esté aquí hace fallar la prueba, y una
fila de aquí que deje de aparecer también (hay que quitarla). Las citas «ERD §…» son de `docs/database/ERD-MINV-V3.md`.

<!-- excepciones:inicio -->
| Consulta | Tabla | Columna | Clasificación | Motivo |
|---|---|---|---|---|
| E02 | `iam.modules` | — | Excepción de diseño ya documentada | Catálogo de módulos comerciales de la plataforma, común a todas las empresas: ERD §2 (toda tabla salvo `iam.tenants` e `iam.modules` lleva `tenant_id`) y regla A-04 (`PlatformEntity`). |
| E02 | `iam.tenants` | — | Excepción de diseño ya documentada | Es la tabla de las empresas: su `id` es el `tenant_id` de todas las demás. ERD §2 y regla A-04. |
| E04 | `billing.siat_branches` | `branch_id` | Excepción de diseño ya documentada | Configuración de la empresa (sucursal de M-INV ↔ código de sucursal del Padrón del SIN), no un hecho de la sucursal: ERD §8.1 y regla F-14 (la configuración y los catálogos no se filtran por sucursal). |
| E04 | `warehouse.branch_users` | `branch_id` | Excepción de diseño ya documentada | Directorio corporativo (qué usuario trabaja en qué sucursal): ERD §7.1 y regla B-02; es justamente lo que calcula el alcance de la sesión. |
| E04 | `warehouse.warehouses` | `branch_id` | Excepción de diseño ya documentada | Directorio corporativo: su sucursal es la raíz de la jerarquía de la que heredan todas las tablas de sucursal (ERD §7.1, regla B-02). |
| E09 | `billing.fiscal_document_files` | `xml` | Excepción de diseño ya documentada | XML exacto del documento fiscal validado contra el XSD (ERD §8.2, regla F-05): su largo depende de las líneas y no se puede recortar un documento legal. |
| E09 | `billing.fiscal_packages` | `messages` | Incumplimiento real (menor, pendiente) | Texto sin límite y sin documentar antes (ahora en ERD §11.7): mensajes de validación de un paquete de hasta 500 documentos. Ver 6.1. |
| E09 | `iam.processed_requests` | `response` | Excepción de diseño ya documentada | Respuesta guardada de un comando ya ejecutado para devolverla igual si se repite (ERD §4, regla B-09): su tamaño es el de la respuesta. |
| E10 | `billing.siat_points_of_sale` | `consecutive_failures` | Excepción de diseño ya documentada | Contador de la máquina de estados del punto de venta (dos fallos seguidos pasan a fuera de línea, regla F-09); estado materializado del modo según ERD §8.2 y §8.4; no es una copia de otra tabla. |
| E10 | `iam.user_credentials` | `failed_attempts` | Excepción de diseño ya documentada | Estado propio del bloqueo por intentos (ERD §4, CHECK `failed_attempts >= 0`; regla P-03): se reinicia al bloquear y al ingresar y cuenta también los fallos al cambiar la contraseña, que no dejan fila en `iam.access_logs`; no es derivable. |
| E10 | `integration.outbox_dispatch` | `rounds` | Redundancia controlada ya documentada | Estado de la cola del outbox, la única tabla mutable de la integración (ERD §4, regla B-06); la comprobación D09 verifica que ninguna entrega sea de una ronda no registrada. |
| E10 | `integration.outgoing_mail_dispatch` | `attempts` | Redundancia controlada ya documentada | Contador materializado de la cola del correo (ERD §11.3 y §11.4); la comprobación D08 verifica que es el número de filas de `outgoing_mail_attempts`. |
| E10 | `inventory.stock_levels` | `quantity_on_hand` | Redundancia controlada ya documentada | Estado materializado de la existencia para la concurrencia optimista (ERD §3, regla A-06); D01 (`v_conservation_breaches`) verifica que es la suma de los movimientos. |
| E10 | `inventory.stock_levels` | `quantity_reserved` | Redundancia controlada ya documentada | Estado materializado de lo reservado (ERD §3 y §10.2); D02 verifica que es la suma de las reservas activas. |
| E10 | `purchasing.supplier_invoice_fiscal` | `total_amount` | Excepción de diseño ya documentada | Importe impreso en la factura del proveedor para el libro de compras: dato del documento de un tercero, no un total calculado por M-INV (ERD §8.3; la base y el crédito fiscal sí se derivan). |
| E11 | `billing.fiscal_document_events` | `messages` | Excepción de diseño ya documentada | Copia literal de los mensajes del SIN en JSON (ERD §8.2), en una bitácora append-only; nunca se consulta por sus partes. |
| E11 | `billing.fiscal_packages` | `messages` | Incumplimiento real (menor, pendiente) | Mensajes de validación del SIN en JSON (1FN en sentido estricto), no documentados antes (ahora en ERD §11.7); no se consultan por sus partes. Ver 6.1. |
| E11 | `iam.audit_logs` | `details` | Excepción de diseño ya documentada | Detalle enmascarado del pedido auditado (ERD §4, diagrama de `iam`: `jsonb details`; regla A-05); documento opaco que no se consulta por sus partes. |
| E11 | `iam.processed_requests` | `response` | Redundancia controlada ya documentada | Instantánea de la respuesta de un comando para la idempotencia (ERD §4, regla B-09): repetir el pedido devuelve exactamente lo mismo. |
| E11 | `integration.outbox_events` | `payload` | Redundancia controlada ya documentada | Instantánea del evento de dominio que se entrega a terceros (ERD §4: «se guarda con su JSON»; regla B-08); append-only. |
| E12 | `billing.siat_cufds` | `code` | Excepción de diseño ya documentada | Historial append-only de los CUFD obtenidos (ERD §8.2): el código es un dato de cada obtención, no la clave de la fila. |
| E12 | `billing.siat_cuis` | `code` | Excepción de diseño ya documentada | Historial append-only de los CUIS (ERD §8.2): el SIN devuelve el mismo CUIS mientras está vigente, así que el código se repite a propósito. |
| E12 | `purchasing.supplier_contacts` | `email` | Excepción de diseño ya documentada | Dato de contacto, no una clave: la identidad del proveedor es su `code` (único por empresa, ERD §4). |
| E12 | `sales.customers` | `email` | Excepción de diseño ya documentada | Dato de contacto, no una clave (dos clientes pueden compartir un correo): la identidad del cliente es su `code` (único por empresa) y la de una cuenta web es `iam.users.email`, única por empresa (ERD §4 y §11.2). |
| E14 | `billing.fiscal_documents` | `kind` | Excepción de diseño ya documentada | Ya acotada: `ck_fiscal_documents_clase` solo admite `Invoice` (sector 1, tipo 1) o `CreditDebitNote` (sector 24, tipo 3) (ERD §8.2: CHECK coherentes con `kind`). |
| E14 | `billing.siat_activities` | `activity_type` | Excepción de diseño ya documentada | Valor de un catálogo del SIN que llega con la sincronización diaria (regla F-07): la lista la define el SIN, no M-INV. |
| E14 | `billing.siat_activity_sectors` | `sector_type` | Excepción de diseño ya documentada | Valor de un catálogo del SIN que llega con la sincronización diaria (regla F-07): la lista la define el SIN, no M-INV. |
| E14 | `integration.api_key_scopes` | `scope` | Incumplimiento real (menor, pendiente) | Lista cerrada en el código (`ApiScopes.All`) que valida el caso de uso, sin CHECK en la base. Ver 6.2. |
| E18 | `billing.fiscal_documents` | `customer_code` | Redundancia controlada ya documentada | Comprador congelado del documento fiscal: instantánea legal (ERD §8.4); una factura emitida no cambia si mañana cambia el cliente. |
| E19 | `billing.fiscal_documents` | `is_reverted` | Incumplimiento real (menor, pendiente) | Se deriva de `reverted_at IS NOT NULL` (`ck_fiscal_documents_reversion` impide que se contradigan). Ver 6.3. |
| E19 | `integration.outgoing_mail_attempts` | `succeeded` | Incumplimiento real (menor, pendiente) | Se deriva de `error IS NULL` (`ck_outgoing_mail_attempts_error` impide que se contradigan). Ver 6.3. |
| E19 | `integration.webhook_endpoints` | `is_active` | Incumplimiento real (menor, pendiente) | Se deriva de `disabled_at IS NULL` (`ck_webhook_endpoints_baja` impide que se contradigan). Ver 6.3. |
<!-- excepciones:fin -->

### 5.3 Otras redundancias controladas (revisión manual, sin consulta propia)

Además de lo que devuelven las consultas, la revisión a mano confirmó estas redundancias, todas documentadas y protegidas por
la propia base:

- `tenant_id` en cada tabla y `branch_id` en los hijos de una sucursal (ERD §3): los necesitan los filtros y la RLS; su
  coherencia la garantizan las FK compuestas (E07 y E08 sin filas).
- `inventory.serial_numbers.variant_id` (se deduce del lote) y `catalog.product_spec_values.spec_definition_id` (se deduce de la
  opción, cuando la hay): ERD §9.5; las FK compuestas `(variant_id, batch_id) → batches` y `(spec_definition_id, option_id) →
  spec_options` impiden que se contradigan.
- Estados materializados con su bitácora (ERD §3, §8.4, §9.5 y §10.2): `stock_transfers.status`, `fiscal_documents.status`,
  `siat_points_of_sale.mode`, `significant_events.status`, `fiscal_packages.status`, `serial_numbers.status`,
  `warranty_claims.status` y `pc_builds.status`. D05, D06 y D07 comprobaron con los datos que el estado de cada armado o
  carrito, transferencia y caso de garantía es el de su última fila de bitácora.
- Instantáneas deliberadas (consulta informativa I03): el precio cotizado de cada línea de un armado o carrito (ERD §9.3, regla
  T-06), el comprador y las líneas congeladas de un documento fiscal (ERD §8.4; también el texto `serial_number` de la línea,
  que es la copia literal de lo que viaja al SIN, regla T-03) y el contacto y los datos para la factura de una reserva (ERD §10.1
  y §11.4, ver la sección 7).

## 6. Incumplimientos reales que quedan pendientes, con su riesgo

Ninguno puede mezclar datos de dos empresas ni de dos sucursales, ni perder un hecho: son datos derivables protegidos por un
CHECK o textos sin tope. Se dejan sin tocar porque corregirlos cambia el dominio, contratos de otros paquetes o el tamaño
permitido de un dato legal.

1. **`billing.fiscal_packages.messages`** (E09 y E11): texto sin límite con los mensajes de validación del SIN en JSON. Riesgo
   bajo: lo escribe solo el despachador con la respuesta del SIN y nadie lo consulta por partes; un paquete observado de 500
   documentos podría guardar un texto grande. Corrección posible: tope como el de `fiscal_document_events.messages` (8000) o una
   tabla de mensajes por documento; hay que confirmar con el SIN el tamaño máximo de la respuesta antes de recortarla.
2. **`integration.api_key_scopes.scope`** (E14): la lista de alcances existe en el código (`ApiScopes.All`) y la valida el caso de
   uso, pero la base acepta cualquier texto de hasta 40 caracteres. Riesgo bajo: solo una edición manual de la base podría
   guardar un alcance inexistente, que el gateway no concedería (los alcances se intersectan con los permisos). Corrección
   posible: `CHECK (scope IN (…))` generado desde `ApiScopes.All` en una migración futura, después de verificar las llaves que
   ya existen.
3. **Booleanas derivables** (E19): `fiscal_documents.is_reverted` (= `reverted_at IS NOT NULL`),
   `outgoing_mail_attempts.succeeded` (= `error IS NULL`) y `webhook_endpoints.is_active` (= `disabled_at IS NULL`). Riesgo nulo
   de incoherencia (un CHECK obliga a que coincidan); solo ocupan un byte por fila. Quitarlas cambiaría entidades, consultas y
   contratos de la V4, la V4.1 y de los paquetes B3a y B3b del correo, y pruebas que las usan.
4. **Estados de dos valores derivables de una fecha** (revisión manual): `sales.pos_sessions.status` (Closed ⇔ `closed_at` no
   nulo, `ck_pos_sessions_cierre`) y `sales.invoices.status` (Draft ⇔ sin `issued_at`, Voided ⇔ con `voided_at`,
   `ck_invoices_emision` y `ck_invoices_anulacion`). Son estado materializado de la V3 no nombrado antes como redundancia
   (ahora en ERD §11.7); los CHECK impiden que se contradigan. Riesgo nulo; no se cambian por el mismo motivo que el punto 3.

## 7. Revisión de lo nuevo de la V7

- **`sales.customer_accounts`** (cuenta de cliente): une 1 a 1 un usuario con su cliente. Está en FNBC: sus dos claves candidatas
  (empresa, usuario) y (empresa, cliente) tienen índice único, y no copia ningún dato (nombre, correo, teléfono y documento viven
  en `iam.users` y `sales.customers`). Va en una tabla propia en vez de una columna anulable en `iam.users` o en
  `sales.customers`: así los maestros no ganan columnas opcionales y la identidad (`iam`) queda separada de las ventas (`sales`).
- **`integration.outgoing_mails`** (el hecho «se pidió un correo»): append-only y de la sucursal de su reserva. Su `branch_id`
  es la redundancia controlada de siempre (lo exige la RLS y la FK compuesta `(tenant_id, branch_id, pc_build_id)` garantiza
  que es la de la reserva). El destinatario NO se deriva de la reserva: un reenvío puede ir a otra dirección y los topes
  cuentan por destinatario. No guarda asunto ni cuerpo (se arman al enviar).
- **`integration.outgoing_mail_dispatch`** (la cola): 1 a 1 con el correo, separa lo que cambia del hecho inmutable (regla
  B-06). `attempts` es un contador materializado: D08 comprobó que coincide con las filas de intentos; `(status = 'Pending') =
  (completed_at IS NULL)` y `attempts BETWEEN 0 AND 5` lo protegen.
- **`integration.outgoing_mail_attempts`** (bitácora de intentos): append-only, un intento por número (`ux (outgoing_mail_id,
  attempt)`). Su única observación es `succeeded`, derivable de `error` (sección 6, punto 3).
- **`sales.pc_builds.kind`**: atributo propio del agregado (armado o carrito), con CHECK y un trigger que impide cambiarlo
  después de crearlo; no se deriva de nada.
- **`sales.pc_build_lines.slot` anulable**: la ranura solo significa algo en un armado; en un carrito sería un dato inventado.
  Que sea nula SOLO en un carrito cruza dos tablas y lo garantiza un trigger (regla A-06); D10 lo comprobó con los datos.
- **Contacto y datos para la factura de una reserva** (`contact_name`, `contact_phone`, `contact_email` de la V6 y
  `buyer_document_type`, `buyer_document_number`, `buyer_complement`, `buyer_name` de la V7): son una **instantánea de lo que
  dejó el visitante al reservar**, no una copia de `sales.customers`. Por qué no se toman del cliente:
  - el visitante de la tienda puede no ser un cliente registrado (`customer_id` nulo);
  - el teléfono de la reserva es con el que el visitante la consulta y la cancela en la tienda pública (regla S-06): si
    cambiara porque el cliente actualizó su teléfono, la reserva dejaría de encontrarse;
  - los datos para la factura son los que el visitante pidió para ESA compra (puede pedirla a nombre de su empresa); la caja
    los precarga al cobrar y el cajero puede cambiarlos.

  Tienen sus reglas de coherencia en la base (`ck_pc_builds_factura_*`: número solo con tipo, complemento solo con CI, razón
  social solo con documento; contacto obligatorio en el canal web) y se enmascaran en la auditoría (S-06). Están documentadas
  como redundancia comercial en el ERD §10.1 y §11.4, igual que el precio cotizado.

## 8. Cómo mantenerlo

- Cada tabla o columna nueva se revisa con el script (o con la prueba `NormalizationTests`, que corre con `MINV_TEST_PG`). Si
  aparece una fila nueva: se corrige o, si es una decisión de diseño, se agrega a la tabla de la sección 5.2 con su
  clasificación y su motivo (y, si es una redundancia, se documenta en el ERD).
- Sin PostgreSQL, `NormalizationTests.Toda_columna_de_un_enum_tiene_un_CHECK_con_sus_valores` ya impide agregar una columna de un
  enum sin su CHECK: los CHECK de los estados se declaran con `BillingChecks.In<TEnum>(columna)`, y un valor nuevo del enum
  genera solo la migración que actualiza el CHECK.
