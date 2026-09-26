# M-INV V3, V4 y V4.1 · Modelo relacional (ERD) · PostgreSQL 15+

Fuente de verdad: el modelo Code-First de `src/2. Infrastructure/MINV.Infrastructure` (entidades en
`src/1. Core/MINV.Domain`, configuraciones en `Persistence/Configurations`, migraciones en `Persistence/Migrations`).
Script equivalente: `scripts/db_init.sql`. La prueba `MINV.Infrastructure.Tests.ModelTests` verifica este documento
contra el modelo real: cada tabla del modelo debe aparecer aquí como `` `esquema.tabla` `` (V4.1: **140 tablas en 9
esquemas**, FK compuestas por tenant y por sucursal, xmin, 24 libros append-only). La V3.1 tenía 97 tablas en 7
esquemas y la V4 110 en 8; los cambios de la V4 (sucursal en las tablas transaccionales, transferencias rediseñadas,
integraciones, idempotencia y modelo de lectura) están resumidos en el §7 y ya incorporados en el §4; la facturación
SIAT de la V4.1 (esquema `billing`, devoluciones de venta, datos fiscales de compras y del cliente) está en el §8.
Arquitectura: `docs/architecture/arquitectura-v4.md` y `docs/architecture/facturacion-siat-v4.1.md`.

## 1. Resumen

| Contexto delimitado | Esquema | Tablas V3.1 | Tablas V4 | Tablas V4.1 | Nuevas en la V4 | Nuevas en la V4.1 |
|---|---|---:|---:|---:|---|---|
| IAM y tenants (identidad, RBAC, licencias, auditoría) | `iam` | 14 | 15 | 15 | `iam.processed_requests` | — |
| Catálogo y datos maestros | `catalog` | 19 | 19 | 19 | — | — |
| Topología de almacén | `warehouse` | 10 | 10 | 10 | — | — |
| Motor transaccional de stock | `inventory` | 14 | 18 | 18 | `inventory.stock_transfer_movements`, `inventory.stock_transfer_discrepancies`, `inventory.stock_transfer_events`, `inventory.stock_transfer_line_batches` | — |
| Compras y proveedores | `purchasing` | 11 | 11 | 12 | — | `purchasing.supplier_invoice_fiscal` |
| Ventas y POS | `sales` | 19 | 20 | 22 | `sales.external_orders` | `sales.sales_returns`, `sales.sales_return_lines` |
| Costos y contabilidad | `accounting` | 10 | 10 | 10 | — | — |
| Integraciones B2B (V4) | `integration` | — | 7 | 7 | las 7 del esquema | — |
| Facturación SIAT (V4.1) | `billing` | — | — | 27 | — | las 27 del esquema (§8) |
| **Total** | 7 → 8 → **9** | **97** | **110** | **140** | **13** | **30** |

Además, el esquema `reporting` (V4) contiene el modelo de lectura: vistas materializadas y vistas filtradas, no
tablas del modelo EF (§7.5).

## 2. Convenciones comunes a todas las tablas

| Regla | Implementación |
|---|---|
| Nombres | `snake_case`; restricciones `pk_`, `ak_`, `fk_`, `ux_` (únicos), `ix_`, `ck_`; ≤ 63 caracteres |
| Clave primaria | `id uuid` (UUID v7 generado en el cliente, creciente en el tiempo; sin secuencias ni contadores compartidos). Las tablas de relación usan clave compuesta natural |
| Multi-tenant | Toda tabla (salvo `iam.tenants` e `iam.modules`) tiene `tenant_id uuid NOT NULL` → `iam.tenants(id)`, filtro global en EF Core y **Row Level Security** (`tenant_id = iam.current_tenant_id()`) |
| Integridad entre empresas | Cada entidad expone la clave alterna `(tenant_id, id)` y **toda FK es compuesta** `(tenant_id, x_id) → (tenant_id, id)`: una fila no puede referenciar datos de otra empresa (lo garantiza PostgreSQL) |
| Concurrencia optimista | Las tablas transaccionales usan la columna de sistema `xmin` como token (`RowVersion` en C#); un conflicto aborta la transacción y el caso de uso reintenta con los valores actuales |
| Append-only | `inventory.stock_movements`, `iam.audit_logs`, `iam.access_logs`, `sales.cash_movements`, `sales.payments`, `accounting.exchange_rates`, `accounting.average_cost_history` y (V4) `inventory.stock_transfer_movements`, `inventory.stock_transfer_discrepancies`, `inventory.stock_transfer_events`, `inventory.stock_transfer_line_batches`, `integration.outbox_events`, `integration.webhook_deliveries`, `sales.external_orders`, `iam.processed_requests` y (V4.1) `billing.siat_cuis`, `billing.siat_cufds`, `billing.siat_sync_runs`, `billing.customer_nit_checks`, `billing.siat_service_calls`, `billing.fiscal_document_lines`, `billing.fiscal_document_files`, `billing.fiscal_document_events`, `billing.fiscal_deliveries`: triggers que rechazan UPDATE, DELETE y TRUNCATE; los roles `minv_app` y `minv_server` no tienen esos privilegios |
| Sucursal (V4) | 35 tablas «por sucursal» (`IBranchScoped`; 50 en la V4.1, §8.4) llevan `branch_id uuid NOT NULL`; 5 tablas «entre sucursales» (`IInterBranch`) llevan `from_branch_id` y `to_branch_id`. Filtro global de EF Core por el alcance de la sesión, guardas de escritura y **Row Level Security RESTRICTIVA** `branch_isolation` (`iam.branch_visible(…)`, variable `minv.branch_ids`). Lista completa en el §7.1 |
| Integridad entre sucursales (V4) | Las tablas por sucursal exponen la clave alterna `(tenant_id, branch_id, id)` y sus hijos la referencian con `(tenant_id, branch_id, padre_id)`: un hijo nunca tiene otra sucursal que su padre, y la cadena termina en el almacén (`warehouses.branch_id`). Las de transferencias usan `(tenant_id, from_branch_id, to_branch_id, id)` |
| Auditoría técnica | `created_at` (default `now()`), `created_by`; en las tablas no append-only también `updated_at`, `updated_by` |
| Tipos | cantidades `numeric(18,6)` (regla `r6` de la V2.1), dinero `numeric(19,4)`, tasas `numeric(18,8)`, porcentajes `numeric(9,4)`, fechas de negocio `date`, instantes `timestamptz` (UTC), estados como texto (`varchar(20)`). V4.1: montos fiscales `numeric(18,2)`, cantidades, precios y descuentos del detalle fiscal `numeric(20,10)` (notas crédito-débito con hasta 10 decimales), hora fiscal del SIN `timestamp without time zone` (la misma del CUF y del XML) |

## 3. Normalización hasta 5FN

- **Sin dependencias transitivas**: la variante se deriva del lote (`stock_levels.batch_id → batches.variant_id`), el
  almacén de la posición (`bins → shelves → racks → aisles → zones → warehouses`), la sucursal del almacén, el cliente y
  la sesión POS del pedido, la moneda de la lista de precios, la marca del modelo. `stock_movements` no guarda la
  cantidad con signo: el signo lo da `movement_types.stock_factor`.
- **Lote y código postal por defecto**: toda variante tiene el lote `SIN-LOTE` y toda ciudad el código postal `S/N`;
  así la existencia es siempre (posición, lote) y la dirección siempre apunta a un código postal, sin columnas nulas que
  romperían la normalización.
- **Relaciones N:M en tablas propias** con clave compuesta: `user_roles`, `role_permissions`, `product_taxes`,
  `product_suppliers`, `product_variant_attributes`, `branch_users`, `bin_assignments`, `supplier_addresses`,
  `customer_addresses`, `price_list_items`, `tenant_modules`. No hay dependencias de reunión que no se deriven de las
  claves (5FN).
- **Reglas que exigen más de una tabla** (y que en 5FN no se expresan con una FK) se garantizan con triggers: un solo
  valor por atributo en cada variante (`trg_variant_single_value`) y asientos contabilizados cuadrados e inmutables.
- **Arcos exclusivos** con `CHECK num_nonnulls(...)`: la recepción viene de una orden **o** de un proveedor; el pedido
  de venta sale de una sesión POS **o** de un almacén; la línea de factura de proveedor es de una recepción **o**
  libre; la reserva es de una sesión POS **o** de una línea de pedido.
- **Redundancia controlada (documentada)**: `tenant_id` en cada tabla (lo exige el aislamiento multi-tenant; su
  coherencia la garantizan las FK compuestas), (V4) `branch_id` / `from_branch_id` / `to_branch_id` en las tablas
  de sucursal (los necesitan los filtros y la RLS por sucursal; su coherencia la garantizan las FK compuestas con la
  sucursal), (V4) `stock_transfers.status` (estado materializado; la bitácora `stock_transfer_events` guarda cada
  transición) y `stock_levels.quantity_on_hand` / `quantity_reserved`, estado
  materializado del agregado para el control de concurrencia; la vista `inventory.v_conservation_breaches` verifica que
  Σ movimientos = existencia (invariante de conservación de la V1). V4: lo recibido de una transferencia NO se guarda
  (se deriva: despachado − faltantes) y `inventory.v_transfer_breaches` verifica la conservación de las transferencias.

## 4. Tablas por contexto

Además de las relaciones dibujadas, **todas** las tablas con `tenant_id` referencian `iam.tenants(id)` y las FK entre
contextos se listan en la columna «Referencias» de cada diccionario.

**V4**: los diagramas muestran `branch_id` en las tablas por sucursal. En esas tablas cada FK hacia su padre de la
misma sucursal incluye también `branch_id` (p. ej. `(tenant_id, branch_id, warehouse_id) → warehouse.warehouses
(tenant_id, branch_id, id)`); la columna «Referencias» la escribe entre paréntesis solo en las tablas nuevas o
rediseñadas de la V4.

### IAM y tenants · esquema `iam` (15 tablas)

```mermaid
erDiagram
    tenants {
        uuid id PK
        varchar code
        varchar legal_name
        varchar tax_id
        boolean is_active
    }
    modules {
        uuid id PK
        varchar code
        varchar name
        varchar description
        numeric setup_price_bs
        numeric monthly_fee_bs
    }
    tenant_modules {
        uuid tenant_id PK, FK
        uuid module_id PK, FK
        timestamptz activated_at
        timestamptz expires_at
        boolean is_active
    }
    tenant_configs {
        uuid tenant_id PK, FK
        uuid default_currency_id FK
        uuid default_warehouse_id FK
        numeric alert_margin
        integer days_without_rotation
        date min_business_date
        varchar time_zone_id
    }
    users {
        uuid id PK
        uuid tenant_id FK
        varchar email
        varchar display_name
        boolean is_active
    }
    user_credentials {
        uuid tenant_id FK
        uuid user_id PK, FK
        varchar password_hash
        varchar algorithm
        integer iterations
        timestamptz changed_at
        boolean must_change_password
        integer failed_attempts
        timestamptz locked_until
    }
    roles {
        uuid id PK
        uuid tenant_id FK
        varchar code
        varchar name
        boolean is_system
    }
    user_roles {
        uuid tenant_id FK
        uuid user_id PK, FK
        uuid role_id PK, FK
    }
    permissions {
        uuid id PK
        uuid tenant_id FK
        varchar code
        varchar description
    }
    role_permissions {
        uuid tenant_id FK
        uuid role_id PK, FK
        uuid permission_id PK, FK
    }
    sessions {
        uuid id PK
        uuid tenant_id FK
        uuid user_id FK
        uuid hardware_token_id FK
        timestamptz started_at
        timestamptz last_seen_at
        timestamptz ended_at
        varchar machine_name
        varchar client_version
        uuid active_branch_id FK
        varchar token_hash
        timestamptz expires_at
    }
    hardware_tokens {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        varchar name
        varchar kind
        varchar fingerprint
        timestamptz registered_at
        timestamptz revoked_at
    }
    access_logs {
        uuid id PK
        uuid tenant_id FK
        uuid user_id FK
        varchar attempted_email
        boolean succeeded
        varchar failure_reason
        timestamptz occurred_at
        varchar machine_name
        uuid hardware_token_id FK
    }
    audit_logs {
        uuid id PK
        uuid tenant_id FK
        uuid user_id FK
        timestamptz occurred_at
        varchar action
        varchar outcome
        varchar entity_type
        uuid entity_id
        jsonb details
        uuid correlation_id
        varchar legacy_reference
        varchar channel
        uuid api_key_id FK
        uuid branch_id FK
    }
    processed_requests {
        uuid id PK
        uuid tenant_id FK
        uuid request_id
        uuid user_id FK
        varchar request_type
        varchar request_hash
        text response
        timestamptz processed_at
    }
    modules ||--o{ tenant_modules : "module_id"
    users ||--o{ user_credentials : "user_id"
    users ||--o{ user_roles : "user_id"
    roles ||--o{ user_roles : "role_id"
    roles ||--o{ role_permissions : "role_id"
    permissions ||--o{ role_permissions : "permission_id"
    users ||--o{ sessions : "user_id"
    hardware_tokens |o--o{ sessions : "hardware_token_id"
    users |o--o{ access_logs : "user_id"
    hardware_tokens |o--o{ access_logs : "hardware_token_id"
    users |o--o{ audit_logs : "user_id"
    users ||--o{ processed_requests : "user_id"
```

| Tabla | Descripción | Clave | Referencias (FK) | Únicos / CHECK |
|---|---|---|---|---|
| `iam.tenants` | Inquilino (empresa cliente): raíz del aislamiento multi-tenant. Única tabla sin TenantId. | (id) | — | único (code) |
| `iam.modules` | Módulo comercial licenciable (matriz de valor B2B): precio de implantación y mensualidad en bolivianos. | (id) | — | único (code)<br>CHECK setup_price_bs >= 0 AND monthly_fee_bs >= 0 |
| `iam.tenant_modules` | Módulos que la empresa tiene licenciados (habilitan funciones: POS y hardware, RBAC…). | (tenant_id, module_id) | module_id → iam.modules | CHECK expires_at IS NULL OR expires_at > activated_at |
| `iam.tenant_configs` | Parámetros de la empresa (una fila por tenant). · OCC xmin | (tenant_id) | default_currency_id → accounting.currencies<br>default_warehouse_id → warehouse.warehouses | CHECK alert_margin >= 0 AND alert_margin <= 1<br>CHECK days_without_rotation > 0 |
| `iam.users` | Persona que opera el sistema (identidad = correo). · OCC xmin | (id) | — | único (tenant_id, email)<br>CHECK email = lower(email) |
| `iam.user_credentials` | Credencial de inicio de sesión (hash PBKDF2), separada del perfil del usuario. · OCC xmin | (user_id) | user_id → iam.users | CHECK iterations >= 100000<br>CHECK failed_attempts >= 0 |
| `iam.roles` | Rol de acceso (RBAC). | (id) | — | único (tenant_id, code) |
| `iam.user_roles` | Roles asignados a cada usuario. | (user_id, role_id) | user_id → iam.users<br>role_id → iam.roles | — |
| `iam.permissions` | Permiso granular (p. ej. inventory.movements.register). | (id) | — | único (tenant_id, code)<br>CHECK code = lower(code) |
| `iam.role_permissions` | Permisos de cada rol. | (role_id, permission_id) | role_id → iam.roles<br>permission_id → iam.permissions | — |
| `iam.sessions` | Sesión de trabajo en el cliente de escritorio. V4: sucursal activa (sin filtro: la sesión es de la empresa) y, en modo nube, SHA-256 del token `mses_…` con vencimiento deslizante de 12 h. · OCC xmin | (id) | user_id → iam.users<br>hardware_token_id → iam.hardware_tokens<br>active_branch_id → warehouse.branches | único (token_hash) WHERE token_hash IS NOT NULL<br>CHECK ended_at IS NULL OR ended_at >= started_at<br>CHECK (token_hash IS NULL) = (expires_at IS NULL) AND (token_hash IS NULL OR token_hash ~ '^[0-9a-f]{64}$') |
| `iam.hardware_tokens` | Equipo autorizado (estación o terminal POS) identificado por la huella de su hardware. | (id) | branch_id → warehouse.branches | único (tenant_id, fingerprint)<br>CHECK revoked_at IS NULL OR revoked_at >= registered_at |
| `iam.access_logs` | Intentos de inicio de sesión (append-only). · **append-only** | (id) | user_id → iam.users<br>hardware_token_id → iam.hardware_tokens | — |
| `iam.audit_logs` | Auditoría inmutable de cada comando (sucesora de 14_ACTIVIDAD de la V2.1). V4: canal (`desktop`, `cloud`, `api`), API Key usada y sucursal activa de quien ejecutó (contexto, sin filtro por sucursal). · **append-only** | (id) | user_id → iam.users<br>api_key_id → integration.api_keys<br>branch_id → warehouse.branches | único (tenant_id, legacy_reference) WHERE legacy_reference IS NOT NULL<br>CHECK channel IS NULL OR channel IN ('desktop', 'cloud', 'api')<br>índice (tenant_id, api_key_id, occurred_at) WHERE api_key_id IS NOT NULL |
| `iam.processed_requests` | V4 · Comando ya ejecutado por el servidor en la nube: si la red se corta durante el COMMIT, el reintento con el mismo `request_id` devuelve la respuesta guardada (idempotencia). Se escribe en la misma transacción que el comando. · **append-only** | (id) | user_id → iam.users | único (tenant_id, request_id)<br>CHECK request_hash ~ '^[0-9a-f]{64}$' |

### Catálogo y datos maestros · esquema `catalog` (19 tablas)

```mermaid
erDiagram
    categories {
        uuid id PK
        uuid tenant_id FK
        varchar code
        varchar name
    }
    category_hierarchies {
        uuid tenant_id FK
        uuid ancestor_id PK, FK
        uuid descendant_id PK, FK
        integer depth
    }
    brands {
        uuid id PK
        uuid tenant_id FK
        varchar name
    }
    models {
        uuid id PK
        uuid tenant_id FK
        uuid brand_id FK
        varchar name
    }
    units_of_measure {
        uuid id PK
        uuid tenant_id FK
        varchar code
        varchar name
        boolean allows_decimals
        varchar description
    }
    unit_conversions {
        uuid id PK
        uuid tenant_id FK
        uuid from_unit_id FK
        uuid to_unit_id FK
        numeric factor
    }
    products {
        uuid id PK
        uuid tenant_id FK
        varchar code
        varchar name
        varchar description
        uuid category_id FK
        uuid model_id FK
        uuid base_unit_id FK
        varchar tracking_mode
        boolean is_active
    }
    product_unit_conversions {
        uuid id PK
        uuid tenant_id FK
        uuid product_id FK
        uuid unit_id FK
        numeric factor_to_base
    }
    attributes {
        uuid id PK
        uuid tenant_id FK
        varchar name
    }
    attribute_values {
        uuid id PK
        uuid tenant_id FK
        uuid attribute_id FK
        varchar value
    }
    product_variants {
        uuid id PK
        uuid tenant_id FK
        uuid product_id FK
        varchar sku
        varchar name
        boolean is_default
        boolean is_active
    }
    product_variant_attributes {
        uuid tenant_id FK
        uuid variant_id PK, FK
        uuid attribute_value_id PK, FK
    }
    barcode_types {
        uuid id PK
        uuid tenant_id FK
        varchar code
        varchar name
        integer length
        boolean has_check_digit
    }
    product_barcodes {
        uuid id PK
        uuid tenant_id FK
        uuid variant_id FK
        uuid barcode_type_id FK
        varchar code
        uuid unit_id FK
        boolean is_primary
    }
    taxes {
        uuid id PK
        uuid tenant_id FK
        varchar code
        varchar name
    }
    product_taxes {
        uuid tenant_id FK
        uuid product_id PK, FK
        uuid tax_id PK, FK
    }
    product_suppliers {
        uuid tenant_id FK
        uuid product_id PK, FK
        uuid supplier_id PK, FK
        varchar supplier_sku
        integer lead_time_days
        boolean is_preferred
    }
    product_stock_policies {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid variant_id FK
        uuid warehouse_id FK
        numeric min_quantity
        numeric max_quantity
    }
    product_images {
        uuid id PK
        uuid tenant_id FK
        uuid variant_id FK
        bytea content
        varchar content_type
        varchar file_name
    }
    categories ||--o{ category_hierarchies : "ancestor_id"
    categories ||--o{ category_hierarchies : "descendant_id"
    brands ||--o{ models : "brand_id"
    units_of_measure ||--o{ unit_conversions : "from_unit_id"
    units_of_measure ||--o{ unit_conversions : "to_unit_id"
    categories ||--o{ products : "category_id"
    models |o--o{ products : "model_id"
    units_of_measure ||--o{ products : "base_unit_id"
    products ||--o{ product_unit_conversions : "product_id"
    units_of_measure ||--o{ product_unit_conversions : "unit_id"
    attributes ||--o{ attribute_values : "attribute_id"
    products ||--o{ product_variants : "product_id"
    product_variants ||--o{ product_variant_attributes : "variant_id"
    attribute_values ||--o{ product_variant_attributes : "attribute_value_id"
    product_variants ||--o{ product_barcodes : "variant_id"
    barcode_types ||--o{ product_barcodes : "barcode_type_id"
    units_of_measure |o--o{ product_barcodes : "unit_id"
    products ||--o{ product_taxes : "product_id"
    taxes ||--o{ product_taxes : "tax_id"
    products ||--o{ product_suppliers : "product_id"
    product_variants ||--o{ product_stock_policies : "variant_id"
    product_variants ||--o| product_images : "variant_id"
```

| Tabla | Descripción | Clave | Referencias (FK) | Únicos / CHECK |
|---|---|---|---|---|
| `catalog.categories` | Categoría del catálogo (árbol en CategoryHierarchies). | (id) | — | único (tenant_id, code) |
| `catalog.category_hierarchies` | Tabla de clausura del árbol de categorías (ancestro, descendiente, profundidad). | (ancestor_id, descendant_id) | ancestor_id → catalog.categories<br>descendant_id → catalog.categories | CHECK depth >= 0<br>CHECK (depth = 0) = (ancestor_id = descendant_id) |
| `catalog.brands` | Marca. | (id) | — | único (tenant_id, name) |
| `catalog.models` | Modelo de una marca. | (id) | brand_id → catalog.brands | único (brand_id, name) |
| `catalog.units_of_measure` | Unidad de medida. | (id) | — | único (tenant_id, code) |
| `catalog.unit_conversions` | Conversión genérica entre unidades. | (id) | from_unit_id → catalog.units_of_measure<br>to_unit_id → catalog.units_of_measure | único (from_unit_id, to_unit_id)<br>CHECK from_unit_id <> to_unit_id<br>CHECK factor > 0 |
| `catalog.products` | Producto del catálogo (agregado con variantes, impuestos y empaques). · OCC xmin | (id) | category_id → catalog.categories<br>model_id → catalog.models<br>base_unit_id → catalog.units_of_measure | único (tenant_id, code) |
| `catalog.product_unit_conversions` | Empaque propio de un producto (p. ej. CAJA = 100 UND). | (id) | product_id → catalog.products<br>unit_id → catalog.units_of_measure | único (product_id, unit_id)<br>CHECK factor_to_base > 0 |
| `catalog.attributes` | Atributo de variante (Color, Talla…). | (id) | — | único (tenant_id, name) |
| `catalog.attribute_values` | Valor de un atributo (Rojo, M…). | (id) | attribute_id → catalog.attributes | único (attribute_id, value) |
| `catalog.product_variants` | Variante vendible de un producto (lleva el SKU). Un producto simple tiene una variante por defecto. | (id) | product_id → catalog.products | único (tenant_id, sku)<br>único (product_id) WHERE is_default |
| `catalog.product_variant_attributes` | Valores de atributo de cada variante (un valor por atributo: lo exige un trigger). | (variant_id, attribute_value_id) | variant_id → catalog.product_variants<br>attribute_value_id → catalog.attribute_values | — |
| `catalog.barcode_types` | Simbología de código de barras (EAN-13, UPC-A…). | (id) | — | único (tenant_id, code)<br>CHECK length IS NULL OR (length >= 1 AND length <= 64) |
| `catalog.product_barcodes` | Códigos de barras de una variante (1:N). | (id) | variant_id → catalog.product_variants<br>barcode_type_id → catalog.barcode_types<br>unit_id → catalog.units_of_measure | único (tenant_id, code)<br>único (variant_id) WHERE is_primary |
| `catalog.taxes` | Impuesto (las tasas vigentes están en TaxRates). | (id) | — | único (tenant_id, code) |
| `catalog.product_taxes` | Impuestos que aplican a cada producto. | (product_id, tax_id) | product_id → catalog.products<br>tax_id → catalog.taxes | — |
| `catalog.product_suppliers` | Proveedores de cada producto (uno preferido). | (product_id, supplier_id) | product_id → catalog.products<br>supplier_id → purchasing.suppliers | único (product_id) WHERE is_preferred<br>CHECK lead_time_days IS NULL OR lead_time_days >= 0 |
| `catalog.product_stock_policies` | Mínimo y máximo de una variante en un almacén (semáforo y pedido sugerido). · OCC xmin | (id) | variant_id → catalog.product_variants<br>warehouse_id → warehouse.warehouses | único (variant_id, warehouse_id)<br>CHECK min_quantity >= 0<br>CHECK max_quantity >= 0 AND (max_quantity = 0 OR max_quantity >= min_quantity) |
| `catalog.product_images` | Imagen (foto o ilustración) de una variante: la galería del catálogo, del stock y del punto de venta. PNG o JPEG de hasta 1 MB guardado en la base (viaja con los respaldos y respeta RLS). | (id) | variant_id → catalog.product_variants | único (tenant_id, variant_id)<br>CHECK content_type IN ('image/png', 'image/jpeg')<br>CHECK octet_length(content) BETWEEN 1 AND 1048576 |

### Topología de almacén · esquema `warehouse` (10 tablas)

```mermaid
erDiagram
    branches {
        uuid id PK
        uuid tenant_id FK
        varchar code
        varchar name
        uuid address_id FK
        boolean is_active
    }
    warehouses {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        varchar code
        varchar name
        boolean is_active
    }
    location_types {
        uuid id PK
        uuid tenant_id FK
        varchar code
        varchar name
        boolean allows_picking
        boolean allows_receiving
        boolean allows_sales
    }
    zones {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid warehouse_id FK
        varchar code
        varchar name
        uuid location_type_id FK
    }
    aisles {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid zone_id FK
        varchar code
    }
    racks {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid aisle_id FK
        varchar code
    }
    shelves {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid rack_id FK
        varchar code
    }
    bins {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid shelf_id FK
        varchar code
        uuid location_type_id FK
        integer pick_sequence
        boolean is_active
    }
    branch_users {
        uuid tenant_id FK
        uuid branch_id PK, FK
        uuid user_id PK, FK
    }
    bin_assignments {
        uuid tenant_id FK
        uuid branch_id FK
        uuid bin_id PK, FK
        uuid variant_id PK, FK
        boolean is_primary_pick
    }
    branches ||--o{ warehouses : "branch_id"
    warehouses ||--o{ zones : "warehouse_id"
    location_types |o--o{ zones : "location_type_id"
    zones ||--o{ aisles : "zone_id"
    aisles ||--o{ racks : "aisle_id"
    racks ||--o{ shelves : "rack_id"
    shelves ||--o{ bins : "shelf_id"
    location_types ||--o{ bins : "location_type_id"
    branches ||--o{ branch_users : "branch_id"
    bins ||--o{ bin_assignments : "bin_id"
```

| Tabla | Descripción | Clave | Referencias (FK) | Únicos / CHECK |
|---|---|---|---|---|
| `warehouse.branches` | Sucursal. | (id) | address_id → sales.addresses | único (tenant_id, code) |
| `warehouse.warehouses` | Almacén de una sucursal. | (id) | branch_id → warehouse.branches | único (tenant_id, code) |
| `warehouse.location_types` | Tipo de posición (picking, reserva, recepción, despacho, góndola POS…). | (id) | — | único (tenant_id, code) |
| `warehouse.zones` | Zona de un almacén. | (id) | warehouse_id → warehouse.warehouses<br>location_type_id → warehouse.location_types | único (warehouse_id, code) |
| `warehouse.aisles` | Pasillo de una zona. | (id) | zone_id → warehouse.zones | único (zone_id, code) |
| `warehouse.racks` | Estantería de un pasillo. | (id) | aisle_id → warehouse.aisles | único (aisle_id, code) |
| `warehouse.shelves` | Nivel (estante) de una estantería. | (id) | rack_id → warehouse.racks | único (rack_id, code) |
| `warehouse.bins` | Posición física donde vive el stock (ruteo de picking por PickSequence). | (id) | shelf_id → warehouse.shelves<br>location_type_id → warehouse.location_types | único (tenant_id, code)<br>CHECK pick_sequence >= 0 |
| `warehouse.branch_users` | Usuarios habilitados en cada sucursal. | (branch_id, user_id) | branch_id → warehouse.branches<br>user_id → iam.users | — |
| `warehouse.bin_assignments` | Posición fija de picking de una variante. | (bin_id, variant_id) | bin_id → warehouse.bins<br>variant_id → catalog.product_variants | — |

### Motor transaccional de stock · esquema `inventory` (18 tablas)

```mermaid
erDiagram
    movement_types {
        uuid id PK
        uuid tenant_id FK
        varchar code
        varchar name
        varchar description
        smallint stock_factor
        varchar domain
        boolean requires_notes
        boolean is_initial_balance
        boolean is_system
    }
    stock_statuses {
        uuid id PK
        uuid tenant_id FK
        varchar code
        varchar name
        integer priority
        varchar suggested_action
        boolean requires_action
    }
    adjustment_reasons {
        uuid id PK
        uuid tenant_id FK
        varchar code
        varchar name
        boolean is_active
    }
    batches {
        uuid id PK
        uuid tenant_id FK
        uuid variant_id FK
        varchar lot_number
        date manufactured_on
        date expires_on
        boolean is_default
    }
    stock_levels {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid bin_id FK
        uuid batch_id FK
        numeric quantity_on_hand
        numeric quantity_reserved
    }
    stock_movements {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid stock_level_id FK
        uuid movement_type_id FK
        numeric quantity
        date business_date
        timestamptz recorded_at
        uuid recorded_by_user_id FK
        varchar document_reference
        varchar notes
        uuid adjustment_reason_id FK
        varchar legacy_reference
        uuid correlation_id
    }
    serial_numbers {
        uuid id PK
        uuid tenant_id FK
        uuid batch_id FK
        varchar serial
        uuid stock_level_id FK
        varchar status
    }
    stock_reservations {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid stock_level_id FK
        numeric quantity
        varchar status
        timestamptz expires_at
        uuid pos_session_id FK
        uuid sales_order_line_id FK
    }
    stock_adjustments {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        varchar number
        uuid warehouse_id FK
        uuid adjustment_reason_id FK
        varchar status
        varchar notes
        timestamptz posted_at
        uuid posted_by_user_id FK
    }
    stock_adjustment_lines {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid stock_adjustment_id FK
        uuid stock_level_id FK
        uuid movement_type_id FK
        numeric quantity
        uuid stock_movement_id FK
    }
    physical_counts {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        varchar number
        uuid warehouse_id FK
        date count_date
        varchar status
        timestamptz posted_at
        uuid posted_by_user_id FK
        varchar notes
    }
    physical_count_lines {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid physical_count_id FK
        uuid stock_level_id FK
        numeric counted_quantity
        uuid counted_by_user_id FK
        timestamptz counted_at
        numeric system_quantity_at_posting
        uuid stock_movement_id FK
    }
    stock_transfers {
        uuid id PK
        uuid tenant_id FK
        varchar number
        uuid from_branch_id FK
        uuid from_warehouse_id FK
        uuid to_branch_id FK
        uuid to_warehouse_id FK
        varchar status
        uuid requested_by_user_id FK
        timestamptz requested_at
        timestamptz dispatched_at
        timestamptz received_at
        varchar notes
    }
    stock_transfer_lines {
        uuid id PK
        uuid tenant_id FK
        uuid from_branch_id FK
        uuid to_branch_id FK
        uuid stock_transfer_id FK
        uuid variant_id FK
        numeric quantity
        numeric unit_cost
    }
    stock_transfer_line_batches {
        uuid tenant_id FK
        uuid from_branch_id FK
        uuid to_branch_id FK
        uuid transfer_line_id PK, FK
        uuid batch_id PK, FK
        numeric quantity
    }
    stock_transfer_movements {
        uuid tenant_id FK
        uuid branch_id FK
        uuid transfer_line_id PK, FK
        uuid stock_movement_id PK, FK
        varchar direction
    }
    stock_transfer_discrepancies {
        uuid id PK
        uuid tenant_id FK
        uuid from_branch_id FK
        uuid to_branch_id FK
        uuid transfer_line_id FK
        numeric quantity
        varchar reason
        uuid recorded_by_user_id FK
        timestamptz recorded_at
    }
    stock_transfer_events {
        uuid id PK
        uuid tenant_id FK
        uuid from_branch_id FK
        uuid to_branch_id FK
        uuid transfer_id FK
        varchar status
        uuid user_id FK
        timestamptz occurred_at
        varchar detail
    }
    batches ||--o{ stock_levels : "batch_id"
    stock_levels ||--o{ stock_movements : "stock_level_id"
    movement_types ||--o{ stock_movements : "movement_type_id"
    adjustment_reasons |o--o{ stock_movements : "adjustment_reason_id"
    batches ||--o{ serial_numbers : "batch_id"
    stock_levels |o--o{ serial_numbers : "stock_level_id"
    stock_levels ||--o{ stock_reservations : "stock_level_id"
    adjustment_reasons ||--o{ stock_adjustments : "adjustment_reason_id"
    stock_adjustments ||--o{ stock_adjustment_lines : "stock_adjustment_id"
    stock_levels ||--o{ stock_adjustment_lines : "stock_level_id"
    movement_types ||--o{ stock_adjustment_lines : "movement_type_id"
    stock_movements |o--o{ stock_adjustment_lines : "stock_movement_id"
    physical_counts ||--o{ physical_count_lines : "physical_count_id"
    stock_levels ||--o{ physical_count_lines : "stock_level_id"
    stock_movements |o--o{ physical_count_lines : "stock_movement_id"
    stock_transfers ||--o{ stock_transfer_lines : "stock_transfer_id"
    stock_transfers ||--o{ stock_transfer_events : "transfer_id"
    stock_transfer_lines ||--o{ stock_transfer_line_batches : "transfer_line_id"
    batches ||--o{ stock_transfer_line_batches : "batch_id"
    stock_transfer_lines ||--o{ stock_transfer_movements : "transfer_line_id"
    stock_movements ||--o| stock_transfer_movements : "stock_movement_id"
    stock_transfer_lines ||--o{ stock_transfer_discrepancies : "transfer_line_id"
```

| Tabla | Descripción | Clave | Referencias (FK) | Únicos / CHECK |
|---|---|---|---|---|
| `inventory.movement_types` | Tipo de movimiento con su factor de stock (+1/−1) y su dominio. | (id) | — | único (tenant_id, code)<br>único (tenant_id) WHERE is_initial_balance<br>CHECK stock_factor IN (-1, 1) |
| `inventory.stock_statuses` | Estados del semáforo de stock con su prioridad y acción sugerida. | (id) | — | único (tenant_id, code)<br>único (tenant_id, priority) |
| `inventory.adjustment_reasons` | Motivo de ajuste (merma, daño…). | (id) | — | único (tenant_id, code) |
| `inventory.batches` | Lote de una variante (con caducidad). Toda variante tiene un lote por defecto «SIN-LOTE». | (id) | variant_id → catalog.product_variants | único (variant_id, lot_number)<br>único (variant_id) WHERE is_default<br>CHECK expires_on IS NULL OR manufactured_on IS NULL OR expires_on >= manufactured_on |
| `inventory.stock_levels` | Existencia de un lote en una posición: estado materializado con control optimista (xmin). · OCC xmin | (id) | bin_id → warehouse.bins<br>batch_id → inventory.batches | único (bin_id, batch_id)<br>CHECK quantity_on_hand >= 0<br>CHECK quantity_reserved >= 0 AND quantity_reserved <= quantity_on_hand |
| `inventory.stock_movements` | Movimiento de inventario: event store inmutable (append-only). · **append-only** | (id) | stock_level_id → inventory.stock_levels<br>movement_type_id → inventory.movement_types<br>recorded_by_user_id → iam.users<br>adjustment_reason_id → inventory.adjustment_reasons | único (tenant_id, legacy_reference) WHERE legacy_reference IS NOT NULL<br>CHECK quantity > 0 |
| `inventory.serial_numbers` | Número de serie (trazabilidad 1 a 1). · OCC xmin | (id) | batch_id → inventory.batches<br>stock_level_id → inventory.stock_levels | único (batch_id, serial) |
| `inventory.stock_reservations` | Reserva temporal de stock (carrito del POS o pedido). · OCC xmin | (id) | stock_level_id → inventory.stock_levels<br>pos_session_id → sales.pos_sessions<br>sales_order_line_id → sales.sales_order_lines | CHECK quantity > 0<br>CHECK num_nonnulls(pos_session_id, sales_order_line_id) <= 1 |
| `inventory.stock_adjustments` | Documento de ajuste de inventario. · OCC xmin | (id) | warehouse_id → warehouse.warehouses<br>adjustment_reason_id → inventory.adjustment_reasons<br>posted_by_user_id → iam.users | único (tenant_id, number) |
| `inventory.stock_adjustment_lines` | Línea de un ajuste. | (id) | stock_adjustment_id → inventory.stock_adjustments<br>stock_level_id → inventory.stock_levels<br>movement_type_id → inventory.movement_types<br>stock_movement_id → inventory.stock_movements | único (stock_movement_id) WHERE stock_movement_id IS NOT NULL<br>CHECK quantity > 0 |
| `inventory.physical_counts` | Toma física (sucesora de 13_CONTEO de la V2.1). · OCC xmin | (id) | warehouse_id → warehouse.warehouses<br>posted_by_user_id → iam.users | único (tenant_id, number)<br>único (warehouse_id) WHERE status = 'Open' |
| `inventory.physical_count_lines` | Conteo de una existencia. · OCC xmin | (id) | physical_count_id → inventory.physical_counts<br>stock_level_id → inventory.stock_levels<br>counted_by_user_id → iam.users<br>stock_movement_id → inventory.stock_movements | único (physical_count_id, stock_level_id)<br>CHECK counted_quantity >= 0 |
| `inventory.stock_transfers` | V4 (rediseñada) · Transferencia entre almacenes de sucursales: `Pending` → `Dispatched` (en tránsito) → `Received`, o `Cancelled`. La ven el origen y el destino. · OCC xmin · **entre sucursales** | (id) | (from_branch_id, from_warehouse_id) → warehouse.warehouses<br>(to_branch_id, to_warehouse_id) → warehouse.warehouses<br>requested_by_user_id → iam.users | único (tenant_id, number)<br>CHECK from_warehouse_id <> to_warehouse_id<br>CHECK status IN ('Pending', 'Dispatched', 'Received', 'Cancelled')<br>CHECK (status IN ('Dispatched', 'Received')) = (dispatched_at IS NOT NULL)<br>CHECK (status = 'Received') = (received_at IS NOT NULL) |
| `inventory.stock_transfer_lines` | V4 (rediseñada) · Línea: variante, cantidad solicitada (= despachada: el despacho es completo) y costo promedio del origen al despachar. Lo recibido no se guarda: es cantidad − faltantes. · **entre sucursales** | (id) | (from_branch_id, to_branch_id, stock_transfer_id) → inventory.stock_transfers<br>variant_id → catalog.product_variants | único (stock_transfer_id, variant_id)<br>CHECK quantity > 0<br>CHECK unit_cost IS NULL OR unit_cost >= 0 |
| `inventory.stock_transfer_line_batches` | V4 · Manifiesto de despacho: lote y cantidad que viajan en cada línea (lo ven ambos lados; la recepción no puede ingresar de un lote más de lo que viajó). · **append-only** · **entre sucursales** | (transfer_line_id, batch_id) | (from_branch_id, to_branch_id, transfer_line_id) → inventory.stock_transfer_lines<br>batch_id → inventory.batches | CHECK quantity > 0 |
| `inventory.stock_transfer_movements` | V4 · Vínculo entre una línea y un movimiento de stock: salida TRASLADO (SALIDA) en el origen o entrada TRASLADO (ENTRADA) en el destino. Su sucursal es la del movimiento. · **append-only** · **por sucursal** | (transfer_line_id, stock_movement_id) | transfer_line_id → inventory.stock_transfer_lines<br>(branch_id, stock_movement_id) → inventory.stock_movements | único (stock_movement_id)<br>CHECK direction IN ('Out', 'In') |
| `inventory.stock_transfer_discrepancies` | V4 · Faltante al recibir (delta compensatorio con motivo); lo registra el destino y lo ven ambos. · **append-only** · **entre sucursales** | (id) | (from_branch_id, to_branch_id, transfer_line_id) → inventory.stock_transfer_lines<br>recorded_by_user_id → iam.users | CHECK quantity > 0 |
| `inventory.stock_transfer_events` | V4 · Bitácora de la máquina de estados (quién, cuándo, a qué estado y detalle). · **append-only** · **entre sucursales** | (id) | (from_branch_id, to_branch_id, transfer_id) → inventory.stock_transfers<br>user_id → iam.users | índice (transfer_id, occurred_at) |

### Compras y proveedores · esquema `purchasing` (11 tablas; 12 con `purchasing.supplier_invoice_fiscal` de la V4.1, §8.3)

```mermaid
erDiagram
    suppliers {
        uuid id PK
        uuid tenant_id FK
        varchar code
        varchar legal_name
        varchar tax_id
        integer lead_time_days
        boolean is_active
    }
    supplier_contacts {
        uuid id PK
        uuid tenant_id FK
        uuid supplier_id FK
        varchar full_name
        varchar phone
        varchar email
        boolean is_primary
    }
    supplier_addresses {
        uuid tenant_id FK
        uuid supplier_id PK, FK
        uuid address_id PK, FK
        varchar address_type
    }
    purchase_orders {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        varchar number
        uuid supplier_id FK
        uuid warehouse_id FK
        uuid currency_id FK
        date order_date
        date expected_date
        varchar status
        varchar notes
    }
    purchase_order_lines {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid purchase_order_id FK
        uuid variant_id FK
        uuid unit_id FK
        numeric quantity
        numeric unit_cost
    }
    goods_receipts {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        varchar number
        uuid purchase_order_id FK
        uuid supplier_id FK
        uuid warehouse_id FK
        timestamptz received_at
        uuid received_by_user_id FK
        varchar supplier_document
        varchar status
    }
    goods_receipt_lines {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid goods_receipt_id FK
        uuid purchase_order_line_id FK
        uuid stock_level_id FK
        numeric quantity
        numeric unit_cost
        uuid stock_movement_id FK
    }
    supplier_invoices {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid supplier_id FK
        varchar number
        date invoice_date
        date due_date
        uuid currency_id FK
        varchar status
    }
    supplier_invoice_lines {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid supplier_invoice_id FK
        uuid goods_receipt_line_id FK
        varchar description
        numeric quantity
        numeric unit_cost
        uuid tax_rate_id FK
    }
    purchase_returns {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        varchar number
        uuid supplier_id FK
        date return_date
        varchar reason
        varchar status
    }
    purchase_return_lines {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid purchase_return_id FK
        uuid stock_level_id FK
        uuid goods_receipt_line_id FK
        numeric quantity
        uuid stock_movement_id FK
    }
    suppliers ||--o{ supplier_contacts : "supplier_id"
    suppliers ||--o{ supplier_addresses : "supplier_id"
    suppliers ||--o{ purchase_orders : "supplier_id"
    purchase_orders ||--o{ purchase_order_lines : "purchase_order_id"
    purchase_orders |o--o{ goods_receipts : "purchase_order_id"
    suppliers |o--o{ goods_receipts : "supplier_id"
    goods_receipts ||--o{ goods_receipt_lines : "goods_receipt_id"
    purchase_order_lines |o--o{ goods_receipt_lines : "purchase_order_line_id"
    suppliers ||--o{ supplier_invoices : "supplier_id"
    supplier_invoices ||--o{ supplier_invoice_lines : "supplier_invoice_id"
    goods_receipt_lines |o--o{ supplier_invoice_lines : "goods_receipt_line_id"
    suppliers ||--o{ purchase_returns : "supplier_id"
    purchase_returns ||--o{ purchase_return_lines : "purchase_return_id"
    goods_receipt_lines |o--o{ purchase_return_lines : "goods_receipt_line_id"
```

| Tabla | Descripción | Clave | Referencias (FK) | Únicos / CHECK |
|---|---|---|---|---|
| `purchasing.suppliers` | Proveedor. | (id) | — | único (tenant_id, code)<br>único (tenant_id, legal_name)<br>CHECK lead_time_days >= 0 |
| `purchasing.supplier_contacts` | Contacto de un proveedor. | (id) | supplier_id → purchasing.suppliers | único (supplier_id) WHERE is_primary |
| `purchasing.supplier_addresses` | Direcciones de un proveedor. | (supplier_id, address_id) | supplier_id → purchasing.suppliers<br>address_id → sales.addresses | — |
| `purchasing.purchase_orders` | Orden de compra. · OCC xmin | (id) | supplier_id → purchasing.suppliers<br>warehouse_id → warehouse.warehouses<br>currency_id → accounting.currencies | único (tenant_id, number)<br>CHECK expected_date IS NULL OR expected_date >= order_date |
| `purchasing.purchase_order_lines` | Línea de una orden de compra. | (id) | purchase_order_id → purchasing.purchase_orders<br>variant_id → catalog.product_variants<br>unit_id → catalog.units_of_measure | único (purchase_order_id, variant_id, unit_id)<br>CHECK quantity > 0<br>CHECK unit_cost >= 0 |
| `purchasing.goods_receipts` | Recepción de mercancía. · OCC xmin | (id) | purchase_order_id → purchasing.purchase_orders<br>supplier_id → purchasing.suppliers<br>warehouse_id → warehouse.warehouses<br>received_by_user_id → iam.users | único (tenant_id, number)<br>CHECK num_nonnulls(purchase_order_id, supplier_id) = 1 |
| `purchasing.goods_receipt_lines` | Línea de una recepción. | (id) | goods_receipt_id → purchasing.goods_receipts<br>purchase_order_line_id → purchasing.purchase_order_lines<br>stock_level_id → inventory.stock_levels<br>stock_movement_id → inventory.stock_movements | único (stock_movement_id) WHERE stock_movement_id IS NOT NULL<br>CHECK quantity > 0<br>CHECK unit_cost >= 0 |
| `purchasing.supplier_invoices` | Factura de proveedor. · OCC xmin | (id) | supplier_id → purchasing.suppliers<br>currency_id → accounting.currencies | único (supplier_id, number)<br>CHECK due_date IS NULL OR due_date >= invoice_date |
| `purchasing.supplier_invoice_lines` | Línea de factura de proveedor. | (id) | supplier_invoice_id → purchasing.supplier_invoices<br>goods_receipt_line_id → purchasing.goods_receipt_lines<br>tax_rate_id → accounting.tax_rates | CHECK num_nonnulls(goods_receipt_line_id, description) = 1<br>CHECK quantity > 0<br>CHECK unit_cost >= 0 |
| `purchasing.purchase_returns` | Devolución a proveedor. · OCC xmin | (id) | supplier_id → purchasing.suppliers | único (tenant_id, number) |
| `purchasing.purchase_return_lines` | Línea de una devolución. | (id) | purchase_return_id → purchasing.purchase_returns<br>stock_level_id → inventory.stock_levels<br>goods_receipt_line_id → purchasing.goods_receipt_lines<br>stock_movement_id → inventory.stock_movements | CHECK quantity > 0 |

### Ventas y POS · esquema `sales` (20 tablas; 22 con las devoluciones de la V4.1, §8.3)

```mermaid
erDiagram
    countries {
        uuid id PK
        uuid tenant_id FK
        varchar iso_code
        varchar name
    }
    states {
        uuid id PK
        uuid tenant_id FK
        uuid country_id FK
        varchar code
        varchar name
    }
    cities {
        uuid id PK
        uuid tenant_id FK
        uuid state_id FK
        varchar name
    }
    postal_codes {
        uuid id PK
        uuid tenant_id FK
        uuid city_id FK
        varchar code
    }
    addresses {
        uuid id PK
        uuid tenant_id FK
        uuid postal_code_id FK
        varchar street
        varchar reference
    }
    customer_categories {
        uuid id PK
        uuid tenant_id FK
        varchar code
        varchar name
        uuid default_price_list_id FK
    }
    customers {
        uuid id PK
        uuid tenant_id FK
        varchar code
        varchar name
        varchar tax_id
        varchar email
        varchar phone
        uuid customer_category_id FK
        boolean is_active
        smallint document_type
        varchar complement
    }
    customer_addresses {
        uuid tenant_id FK
        uuid customer_id PK, FK
        uuid address_id PK, FK
        varchar address_type
        boolean is_default
    }
    price_lists {
        uuid id PK
        uuid tenant_id FK
        varchar name
        uuid currency_id FK
        date valid_from
        date valid_to
        boolean is_default
    }
    price_list_items {
        uuid tenant_id FK
        uuid price_list_id PK, FK
        uuid variant_id PK, FK
        numeric unit_price
    }
    pos_registers {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid warehouse_id FK
        varchar code
        varchar name
        uuid hardware_token_id FK
        boolean is_active
    }
    pos_sessions {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid pos_register_id FK
        uuid opened_by_user_id FK
        timestamptz opened_at
        numeric opening_cash
        uuid closed_by_user_id FK
        timestamptz closed_at
        numeric closing_cash_counted
        varchar status
    }
    cash_movements {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid pos_session_id FK
        varchar direction
        numeric amount
        varchar reason
        timestamptz occurred_at
        uuid recorded_by_user_id FK
    }
    sales_orders {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        varchar number
        uuid customer_id FK
        uuid pos_session_id FK
        uuid warehouse_id FK
        uuid price_list_id FK
        date order_date
        varchar status
    }
    sales_order_lines {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid sales_order_id FK
        uuid variant_id FK
        uuid unit_id FK
        numeric quantity
        numeric unit_price
        numeric discount_percent
        uuid stock_movement_id FK
    }
    invoices {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        varchar number
        uuid sales_order_id FK
        varchar status
        timestamptz issued_at
        varchar fiscal_authorization_code
        timestamptz voided_at
        varchar void_reason
    }
    invoice_lines {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid invoice_id FK
        uuid sales_order_line_id FK
        uuid tax_rate_id FK
        numeric tax_amount
    }
    payment_methods {
        uuid id PK
        uuid tenant_id FK
        varchar code
        varchar name
        boolean requires_reference
        boolean opens_cash_drawer
        boolean is_active
    }
    payments {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid invoice_id FK
        uuid payment_method_id FK
        numeric amount
        varchar reference
        timestamptz paid_at
        uuid pos_session_id FK
        uuid recorded_by_user_id FK
    }
    external_orders {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        varchar channel
        varchar external_id
        varchar request_hash
        uuid sales_order_id FK
        varchar invoice_number
        timestamptz received_at
    }
    countries ||--o{ states : "country_id"
    states ||--o{ cities : "state_id"
    cities ||--o{ postal_codes : "city_id"
    postal_codes ||--o{ addresses : "postal_code_id"
    price_lists |o--o{ customer_categories : "default_price_list_id"
    customer_categories ||--o{ customers : "customer_category_id"
    customers ||--o{ customer_addresses : "customer_id"
    addresses ||--o{ customer_addresses : "address_id"
    price_lists ||--o{ price_list_items : "price_list_id"
    pos_registers ||--o{ pos_sessions : "pos_register_id"
    pos_sessions ||--o{ cash_movements : "pos_session_id"
    customers ||--o{ sales_orders : "customer_id"
    pos_sessions |o--o{ sales_orders : "pos_session_id"
    price_lists ||--o{ sales_orders : "price_list_id"
    sales_orders ||--o{ sales_order_lines : "sales_order_id"
    sales_orders ||--o{ invoices : "sales_order_id"
    invoices ||--o{ invoice_lines : "invoice_id"
    sales_order_lines ||--o{ invoice_lines : "sales_order_line_id"
    invoices ||--o{ payments : "invoice_id"
    payment_methods ||--o{ payments : "payment_method_id"
    pos_sessions |o--o{ payments : "pos_session_id"
    sales_orders ||--o| external_orders : "sales_order_id"
```

| Tabla | Descripción | Clave | Referencias (FK) | Únicos / CHECK |
|---|---|---|---|---|
| `sales.countries` | País. | (id) | — | único (tenant_id, iso_code)<br>CHECK iso_code ~ '^[A-Z]{2}$' |
| `sales.states` | Departamento o estado. | (id) | country_id → sales.countries | único (country_id, code) |
| `sales.cities` | Ciudad. | (id) | state_id → sales.states | único (state_id, name) |
| `sales.postal_codes` | Código postal de una ciudad («S/N» para zonas sin código). | (id) | city_id → sales.cities | único (city_id, code) |
| `sales.addresses` | Dirección normalizada (calle → código postal → ciudad → estado → país). | (id) | postal_code_id → sales.postal_codes | — |
| `sales.customer_categories` | Categoría de cliente. | (id) | default_price_list_id → sales.price_lists | único (tenant_id, code) |
| `sales.customers` | Cliente. V4.1: datos de facturación del SIN, `document_type` (1 CI, 2 CEX, 3 PAS, 4 OD, 5 NIT; `tax_id` es el número) y `complement` (SEGIP, solo con CI). · OCC xmin | (id) | customer_category_id → sales.customer_categories | único (tenant_id, code)<br>CHECK document_type IS NULL OR document_type BETWEEN 1 AND 5<br>CHECK complement IS NULL OR document_type = 1 |
| `sales.customer_addresses` | Direcciones de un cliente. | (customer_id, address_id) | customer_id → sales.customers<br>address_id → sales.addresses | único (customer_id, address_type) WHERE is_default |
| `sales.price_lists` | Lista de precios. | (id) | currency_id → accounting.currencies | único (tenant_id, name)<br>único (tenant_id) WHERE is_default<br>CHECK valid_to IS NULL OR valid_to >= valid_from |
| `sales.price_list_items` | Precio de una variante en una lista. | (price_list_id, variant_id) | price_list_id → sales.price_lists<br>variant_id → catalog.product_variants | CHECK unit_price >= 0 |
| `sales.pos_registers` | Caja del punto de venta (toma stock de un almacén). | (id) | warehouse_id → warehouse.warehouses<br>hardware_token_id → iam.hardware_tokens | único (tenant_id, code)<br>único (hardware_token_id) WHERE hardware_token_id IS NOT NULL |
| `sales.pos_sessions` | Turno de caja (apertura y cierre con arqueo). · OCC xmin | (id) | pos_register_id → sales.pos_registers<br>opened_by_user_id → iam.users<br>closed_by_user_id → iam.users | único (pos_register_id) WHERE status = 'Open'<br>CHECK opening_cash >= 0<br>CHECK (status = 'Closed') = (closed_at IS NOT NULL)<br>CHECK closed_at IS NULL OR closed_at >= opened_at |
| `sales.cash_movements` | Ingreso o retiro de efectivo de la caja (append-only). · **append-only** | (id) | pos_session_id → sales.pos_sessions<br>recorded_by_user_id → iam.users | CHECK amount > 0 |
| `sales.sales_orders` | Pedido o venta (POS o back-office). · OCC xmin | (id) | customer_id → sales.customers<br>pos_session_id → sales.pos_sessions<br>warehouse_id → warehouse.warehouses<br>price_list_id → sales.price_lists | único (tenant_id, number)<br>CHECK num_nonnulls(pos_session_id, warehouse_id) = 1 |
| `sales.sales_order_lines` | Línea de un pedido de venta. | (id) | sales_order_id → sales.sales_orders<br>variant_id → catalog.product_variants<br>unit_id → catalog.units_of_measure<br>stock_movement_id → inventory.stock_movements | único (stock_movement_id) WHERE stock_movement_id IS NOT NULL<br>CHECK quantity > 0<br>CHECK unit_price >= 0<br>CHECK discount_percent >= 0 AND discount_percent <= 100 |
| `sales.invoices` | Factura de venta (una por pedido). · OCC xmin | (id) | sales_order_id → sales.sales_orders | único (tenant_id, number)<br>único (sales_order_id)<br>CHECK (status = 'Draft') = (issued_at IS NULL)<br>CHECK (status = 'Voided') = (voided_at IS NOT NULL) |
| `sales.invoice_lines` | Línea fiscal de una factura (impuesto aplicado). | (id) | invoice_id → sales.invoices<br>sales_order_line_id → sales.sales_order_lines<br>tax_rate_id → accounting.tax_rates | único (sales_order_line_id)<br>CHECK tax_amount >= 0 |
| `sales.payment_methods` | Medio de pago (efectivo, tarjeta, QR…). | (id) | — | único (tenant_id, code) |
| `sales.payments` | Pago de una factura (append-only). · **append-only** | (id) | invoice_id → sales.invoices<br>payment_method_id → sales.payment_methods<br>pos_session_id → sales.pos_sessions<br>recorded_by_user_id → iam.users | CHECK amount > 0 |
| `sales.external_orders` | V4 · Pedido de un canal externo (e-commerce, ERP) ya registrado como venta. El canal es la API Key que lo envió (`api-<id>`); repetir el mismo (canal, id externo) devuelve la venta original y con otro contenido se rechaza. · **append-only** · **por sucursal** | (id) | (branch_id, sales_order_id) → sales.sales_orders | único (tenant_id, channel, external_id)<br>único (sales_order_id)<br>CHECK request_hash ~ '^[0-9a-f]{64}$' |

### Costos y contabilidad · esquema `accounting` (10 tablas)

```mermaid
erDiagram
    currencies {
        uuid id PK
        uuid tenant_id FK
        varchar code
        varchar name
        varchar symbol
        smallint decimal_places
    }
    exchange_rates {
        uuid id PK
        uuid tenant_id FK
        uuid from_currency_id FK
        uuid to_currency_id FK
        date effective_date
        numeric rate
    }
    cost_centers {
        uuid id PK
        uuid tenant_id FK
        varchar code
        varchar name
        uuid branch_id FK
        boolean is_active
    }
    accounts {
        uuid id PK
        uuid tenant_id FK
        varchar code
        varchar name
        varchar account_type
        uuid parent_account_id FK
        boolean is_postable
    }
    fiscal_periods {
        uuid id PK
        uuid tenant_id FK
        smallint year
        smallint month
        varchar status
    }
    journal_entries {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        varchar number
        uuid fiscal_period_id FK
        date entry_date
        varchar description
        uuid currency_id FK
        varchar status
        timestamptz posted_at
        uuid posted_by_user_id FK
        uuid source_correlation_id
    }
    journal_lines {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid journal_entry_id FK
        uuid account_id FK
        uuid cost_center_id FK
        numeric debit
        numeric credit
        varchar memo
    }
    average_cost_history {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid variant_id FK
        uuid warehouse_id FK
        integer sequence
        timestamptz effective_at
        numeric average_cost
        uuid stock_movement_id FK
    }
    tax_rates {
        uuid id PK
        uuid tenant_id FK
        uuid tax_id FK
        numeric rate
        date valid_from
        date valid_to
    }
    tax_rules {
        uuid id PK
        uuid tenant_id FK
        uuid tax_id FK
        uuid customer_category_id FK
        uuid branch_id FK
        boolean is_exempt
        integer priority
    }
    currencies ||--o{ exchange_rates : "from_currency_id"
    currencies ||--o{ exchange_rates : "to_currency_id"
    accounts |o--o{ accounts : "parent_account_id"
    fiscal_periods ||--o{ journal_entries : "fiscal_period_id"
    currencies ||--o{ journal_entries : "currency_id"
    journal_entries ||--o{ journal_lines : "journal_entry_id"
    accounts ||--o{ journal_lines : "account_id"
    cost_centers |o--o{ journal_lines : "cost_center_id"
```

| Tabla | Descripción | Clave | Referencias (FK) | Únicos / CHECK |
|---|---|---|---|---|
| `accounting.currencies` | Moneda (ISO 4217). | (id) | — | único (tenant_id, code)<br>CHECK code ~ '^[A-Z]{3}$'<br>CHECK decimal_places BETWEEN 0 AND 4 |
| `accounting.exchange_rates` | Tipo de cambio diario (append-only). · **append-only** | (id) | from_currency_id → accounting.currencies<br>to_currency_id → accounting.currencies | único (from_currency_id, to_currency_id, effective_date)<br>CHECK from_currency_id <> to_currency_id<br>CHECK rate > 0 |
| `accounting.cost_centers` | Centro de costo. | (id) | branch_id → warehouse.branches | único (tenant_id, code) |
| `accounting.accounts` | Cuenta del plan contable. | (id) | parent_account_id → accounting.accounts | único (tenant_id, code)<br>CHECK parent_account_id IS NULL OR parent_account_id <> id |
| `accounting.fiscal_periods` | Período contable mensual. · OCC xmin | (id) | — | único (tenant_id, year, month)<br>CHECK month BETWEEN 1 AND 12<br>CHECK year BETWEEN 2000 AND 2100 |
| `accounting.journal_entries` | Asiento contable (partida doble). · OCC xmin | (id) | fiscal_period_id → accounting.fiscal_periods<br>currency_id → accounting.currencies<br>posted_by_user_id → iam.users | único (tenant_id, number) |
| `accounting.journal_lines` | Línea de un asiento (debe o haber). | (id) | journal_entry_id → accounting.journal_entries<br>account_id → accounting.accounts<br>cost_center_id → accounting.cost_centers | CHECK (debit > 0 AND credit = 0) OR (credit > 0 AND debit = 0) |
| `accounting.average_cost_history` | Historial del costo promedio ponderado por variante y almacén (append-only). V4: sucursal y `sequence` por (variante, almacén): el costo vigente es el de mayor secuencia y el índice único hace que dos cálculos concurrentes sobre el mismo estado choquen (el segundo reintenta). · **append-only** · **por sucursal** | (id) | variant_id → catalog.product_variants<br>(branch_id, warehouse_id) → warehouse.warehouses<br>(branch_id, stock_movement_id) → inventory.stock_movements | único (tenant_id, variant_id, warehouse_id, sequence)<br>CHECK average_cost >= 0<br>CHECK sequence >= 1 |
| `accounting.tax_rates` | Tasa de un impuesto con vigencia. | (id) | tax_id → catalog.taxes | único (tax_id, valid_from)<br>CHECK rate >= 0 AND rate <= 100<br>CHECK valid_to IS NULL OR valid_to >= valid_from |
| `accounting.tax_rules` | Regla de aplicación de un impuesto por categoría de cliente y sucursal. | (id) | tax_id → catalog.taxes<br>customer_category_id → sales.customer_categories<br>branch_id → warehouse.branches | único (tax_id, customer_category_id, branch_id) NULLS NOT DISTINCT<br>CHECK priority >= 0 |


### Integraciones B2B · esquema `integration` (7 tablas) · V4

```mermaid
erDiagram
    api_keys {
        uuid id PK
        uuid tenant_id FK
        varchar name
        varchar prefix
        varchar token_hash
        uuid owner_user_id FK
        uuid branch_id FK
        timestamptz created_at
        timestamptz expires_at
        timestamptz revoked_at
    }
    api_key_scopes {
        uuid tenant_id FK
        uuid api_key_id PK, FK
        varchar scope PK
    }
    webhook_endpoints {
        uuid id PK
        uuid tenant_id FK
        varchar url
        varchar description
        uuid created_by_user_id FK
        timestamptz created_at
        boolean is_active
        timestamptz disabled_at
        uuid api_key_id FK
        uuid branch_id FK
        varchar secret_ciphertext
        varchar secret_key_id
        integer secret_version
        varchar previous_secret_ciphertext
        varchar previous_secret_key_id
        timestamptz previous_secret_expires_at
    }
    webhook_endpoint_events {
        uuid tenant_id FK
        uuid endpoint_id PK, FK
        varchar event_type PK
    }
    outbox_events {
        uuid id PK
        uuid tenant_id FK
        varchar event_type
        uuid branch_id FK
        jsonb payload
        timestamptz occurred_at
    }
    outbox_dispatch {
        uuid tenant_id FK
        uuid outbox_event_id PK, FK
        varchar status
        integer rounds
        timestamptz next_attempt_at
        timestamptz completed_at
        varchar last_error
    }
    webhook_deliveries {
        uuid id PK
        uuid tenant_id FK
        uuid outbox_event_id FK
        uuid endpoint_id FK
        integer attempt
        integer status_code
        boolean succeeded
        varchar error
        timestamptz attempted_at
        integer duration_ms
    }
    api_keys ||--o{ api_key_scopes : "api_key_id"
    api_keys |o--o{ webhook_endpoints : "api_key_id"
    webhook_endpoints ||--o{ webhook_endpoint_events : "endpoint_id"
    outbox_events ||--|| outbox_dispatch : "outbox_event_id"
    outbox_events ||--o{ webhook_deliveries : "outbox_event_id"
    webhook_endpoints ||--o{ webhook_deliveries : "endpoint_id"
```

En estas tablas `branch_id` es un **atributo** (a qué sucursal está limitada la llave, qué eventos recibe el webhook, de
qué sucursal es el evento), no una partición: se filtran solo por empresa.

| Tabla | Descripción | Clave | Referencias (FK) | Únicos / CHECK |
|---|---|---|---|---|
| `integration.api_keys` | V4 · Llave del API Gateway: prefijo público (único en toda la plataforma: se busca antes de conocer la empresa, con `integration.resolve_api_key`) y SHA-256 del token completo (el token se muestra una sola vez). Actúa en nombre de su dueño; puede limitarse a una sucursal y vencer; se revoca, nunca se borra. · OCC xmin | (id) | owner_user_id → iam.users<br>branch_id → warehouse.branches | único (prefix)<br>CHECK prefix ~ '^[a-z0-9]{8}$'<br>CHECK token_hash ~ '^[0-9a-f]{64}$'<br>CHECK expires_at IS NULL OR expires_at > created_at |
| `integration.api_key_scopes` | V4 · Alcances de cada llave (`catalog:read`, `stock:read`, `orders:write`, `transfers:read`, `transfers:write`, `webhooks:manage`, `reports:read`). | (api_key_id, scope) | api_key_id → integration.api_keys | — |
| `integration.webhook_endpoints` | V4 · Destino de webhooks (URL https del tercero) con su secreto de firma **cifrado** (AES-256-GCM; `secret_key_id` = clave maestra usada) y, durante una rotación, el secreto anterior que sigue firmando hasta `previous_secret_expires_at`. · OCC xmin | (id) | api_key_id → integration.api_keys<br>branch_id → warehouse.branches<br>created_by_user_id → iam.users | CHECK la URL empieza por `https://` (o `http://localhost`, `http://127.0.0.1`, `http://[::1]`)<br>CHECK is_active = (disabled_at IS NULL)<br>CHECK secret_version >= 1<br>CHECK los tres campos `previous_secret_*` son todos nulos o todos no nulos |
| `integration.webhook_endpoint_events` | V4 · Eventos a los que se suscribe cada destino (una fila por evento). | (endpoint_id, event_type) | endpoint_id → integration.webhook_endpoints | — |
| `integration.outbox_events` | V4 · Outbox transaccional: cada evento de dominio (`sale.completed`, `transfer.dispatched`…) se guarda con su JSON en la MISMA transacción que el cambio que lo produjo. · **append-only** | (id) | branch_id → warehouse.branches | índice (tenant_id, occurred_at) |
| `integration.outbox_dispatch` | V4 · Cola de despacho 1:1 con el evento (la única tabla mutable de la integración): estado, rondas hechas, próximo intento, último error. La toma `integration.claim_deliveries` con FOR UPDATE SKIP LOCKED. · OCC xmin | (outbox_event_id) | outbox_event_id → integration.outbox_events | CHECK status IN ('Pending', 'Completed', 'Exhausted')<br>CHECK (status = 'Pending') = (completed_at IS NULL)<br>CHECK rounds BETWEEN 0 AND 8<br>índice parcial (next_attempt_at) WHERE status = 'Pending' |
| `integration.webhook_deliveries` | V4 · Cada intento de entrega de un evento a un destino (código HTTP, error, duración): un reintento es una fila nueva. · **append-only** | (id) | outbox_event_id → integration.outbox_events<br>endpoint_id → integration.webhook_endpoints | único (outbox_event_id, endpoint_id, attempt)<br>CHECK attempt BETWEEN 1 AND 8<br>CHECK duration_ms >= 0 |

## 5. Trazabilidad V2.1 → V3

La V3 se construyó a partir del modelo de la V2.1 (rama `Inventario-V2.1`). El importador
(`MINV.Infrastructure.Importing.V21`, comando `minv import-v21`) lleva cada tabla del libro colaborativo a su tabla
de la V3 y verifica la paridad (stock, semáforo, cobertura, ranking, alertas y pedido idénticos a la instantánea de la
V2.1).

| V2.1 (libro colaborativo) | V3 (PostgreSQL) |
|---|---|
| 01_CONFIG: cfgEmpresa, cfgNIT | `iam.tenants` |
| 01_CONFIG: cfgMargenAlerta, cfgDiasSinRotacion, cfgFechaMin, cfgMoneda, cfgBodega | `iam.tenant_configs` |
| 02_USUARIOS (tblUsuarios): Correo, Nombre, Activo | `iam.users` |
| 02_USUARIOS: Rol (ADMIN, BODEGA, VENTAS, CONSULTA) | `iam.roles` |
| 14_ACTIVIDAD (tblActividad): ID, Timestamp, Usuario_O365, Script, Resultado, Detalle | `iam.audit_logs` |
| 03_CATEGORIAS (tblCategorias): Código, Categoría | `catalog.categories` |
| 06_UNIDADES (tblUnidades): Código, Unidad, Decimales, Descripción | `catalog.units_of_measure` |
| 05_PRODUCTOS (tblProductos): SKU, Producto, Categoría, Unidad, Activo | `catalog.products` |
| tblProductos: SKU (la V2.1 no tenía variantes: cada producto migra con una variante por defecto) | `catalog.product_variants` |
| tblProductos: Proveedor | `catalog.product_suppliers` |
| tblProductos: StockMin, StockMax | `catalog.product_stock_policies` |
| 01_CONFIG: cfgBodega | `warehouse.branches` |
| tblProductos: Ubicación (A-01-01) | `warehouse.bins` |
| tblProductos: Ubicación | `warehouse.bin_assignments` |
| 01_CONFIG (tblTiposMov): Tipo, FactorStock, Dominio, Descripción | `inventory.movement_types` |
| 01_CONFIG (tblEstados) | `inventory.stock_statuses` |
| 15_STOCK (instantánea) → estado transaccional | `inventory.stock_levels` |
| 10A_ENTRADAS + 10B_SALIDAS (tblEntradas, tblSalidas) | `inventory.stock_movements` |
| 13_CONTEO: ctFecha, ctConfirmar | `inventory.physical_counts` |
| tblConteo: Conteo, Sistema, Diferencia | `inventory.physical_count_lines` |
| 04_PROVEEDORES (tblProveedores): Proveedor, NIT, DiasEntrega | `purchasing.suppliers` |
| tblProveedores: Contacto, Teléfono, Correo | `purchasing.supplier_contacts` |
| 18_PEDIDO (tblPedido) → orden de compra real | `purchasing.purchase_orders` |
| 10B_SALIDAS (tipo SALIDA) | `sales.sales_orders` |
| tblProductos: CostoUnitario | `accounting.average_cost_history` |
| 15_STOCK · 16_ALERTAS · 18_PEDIDO (instantáneas) | Proyección al consultar (`GetStockProjectionQuery`) y vista `inventory.v_stock_by_variant` |
| Estado «✖ Rechazado» de 10A/10B | Los rechazos no son movimientos: se migran a `iam.audit_logs` (resultado `Rejected`) |
| IDs `E-…`, `S-…`, `A-…` | `legacy_reference` (único por tenant) en `stock_movements` y `audit_logs` |

## 6. Objetos de PostgreSQL además de las tablas

| Objeto | Propósito |
|---|---|
| `iam.minv_append_only()` + `trg_append_only`, `trg_append_only_truncate` | Libros mayores inmutables |
| `catalog.minv_variant_single_value()` | Un valor por atributo en cada variante |
| `accounting.minv_journal_*` | Asiento contabilizado inmutable y cuadrado (constraint trigger diferido) |
| `iam.current_tenant_id()` + políticas `tenant_isolation` | Row Level Security por tenant (`SET minv.tenant_id`) |
| `inventory.v_stock_by_variant` | Stock por variante y almacén (entradas, salidas, stock, último movimiento) |
| `inventory.v_conservation_breaches` | Existencias cuya cantidad no coincide con la suma de sus movimientos (debe estar vacía) |
| `iam.v_activity` | Actividad con usuario (sucesora de 14_ACTIVIDAD) |
| (V4) `iam.branch_visible(uuid)` + políticas `branch_isolation` (RESTRICTIVAS) | Row Level Security por sucursal (`SET minv.branch_ids` = `*`, lista de UUID o vacío) en 35 tablas por sucursal y 5 entre sucursales; se combina con AND con `tenant_isolation` (108 tablas) |
| (V4) `trg_append_only` en 8 tablas nuevas | 15 libros inmutables en total |
| (V4) `integration.resolve_api_key(text)` | SECURITY DEFINER: busca una API Key por su prefijo antes de conocer la empresa (devuelve id, empresa, hash, dueño, sucursal y vigencia) |
| (V4) `iam.resolve_session(text)` | SECURITY DEFINER: busca la sesión del servidor en la nube por el hash de su token |
| (V4) `integration.claim_deliveries(integer, integer)` | SECURITY DEFINER: toma hasta N eventos vencidos de `outbox_dispatch` con FOR UPDATE SKIP LOCKED y los arrienda N segundos |
| (V4) `reporting.refresh_all()` | SECURITY DEFINER: refresca las vistas materializadas con `REFRESH … CONCURRENTLY` bajo un candado consultivo (una réplica a la vez) |
| (V4) `reporting.mv_branch_stock`, `reporting.mv_branch_daily_sales` | Vistas materializadas del modelo de lectura (índice único para el refresco concurrente); sin permisos para los roles de aplicación |
| (V4) `reporting.v_branch_stock`, `reporting.v_branch_daily_sales` | Vistas `security_barrier` filtradas por `iam.current_tenant_id()` e `iam.branch_visible()`: lo único del esquema `reporting` que leen `minv_app` y `minv_server` |
| (V4) `inventory.v_transfer_breaches` | Transferencias que violan la conservación (Σ salidas = cantidad = Σ manifiesto; recibido + faltantes = cantidad; sin movimientos si está pendiente o anulada). Debe estar vacía |
| (V4.1) `trg_append_only` en 9 tablas de `billing` | 24 libros inmutables en total (CUIS, CUFD, sincronizaciones, verificaciones de NIT, bitácora SOAP, detalle, XML, bitácora y entregas de los documentos fiscales) |
| (V4.1) políticas `tenant_isolation` y `branch_isolation` en las tablas nuevas | 138 tablas con `tenant_isolation`; 55 con `branch_isolation` RESTRICTIVA (50 por sucursal y 5 entre sucursales) |
| (V4.1) `billing.siat_active_tenants()` | SECURITY DEFINER (`search_path` fijo, sin EXECUTE para PUBLIC, solo `minv_server`): empresas con la facturación activa (`siat_settings.is_enabled`); la usa el despachador del servidor antes de conocer la empresa. Devuelve solo el id |
| (V4.1) `billing.v_fiscal_document_totals` | Vista `security_barrier` (y `security_invoker`: aplica la RLS del que consulta) con los totales DERIVADOS de cada documento fiscal con las fórmulas del SIN: subtotal de líneas (transacción nula o 1), total, base del IVA, débito o crédito del 13 % y devuelto de las notas. Los totales no se guardan (regla F-06) |

## 7. V4 · Multi-sucursal, transferencias, integraciones e idempotencia

Migración `V4MultiBranchCloud` (`Persistence/Migrations/20260925214056_V4MultiBranchCloud.cs` y su parcial `.Sql.cs`).
Verificada sobre una copia de la base local de la V3: 110 tablas, 108 políticas `tenant_isolation`, 40
`branch_isolation`, 15 triggers append-only y 0 descuadres de conservación.

### 7.1 Tablas por sucursal y entre sucursales

**Por sucursal** (`IBranchScoped`, columna `branch_id`, 35 tablas; política `branch_isolation` sobre `branch_id`):
`warehouse.zones`, `warehouse.aisles`, `warehouse.racks`, `warehouse.shelves`, `warehouse.bins`,
`warehouse.bin_assignments`, `catalog.product_stock_policies`, `inventory.stock_levels`, `inventory.stock_movements`,
`inventory.stock_reservations`, `inventory.stock_adjustments`, `inventory.stock_adjustment_lines`,
`inventory.physical_counts`, `inventory.physical_count_lines`, `inventory.stock_transfer_movements`,
`purchasing.purchase_orders`, `purchasing.purchase_order_lines`, `purchasing.goods_receipts`,
`purchasing.goods_receipt_lines`, `purchasing.purchase_returns`, `purchasing.purchase_return_lines`,
`purchasing.supplier_invoices`, `purchasing.supplier_invoice_lines`, `sales.pos_registers`, `sales.pos_sessions`,
`sales.cash_movements`, `sales.sales_orders`, `sales.sales_order_lines`, `sales.invoices`, `sales.invoice_lines`,
`sales.payments`, `sales.external_orders`, `accounting.journal_entries`, `accounting.journal_lines`,
`accounting.average_cost_history`.

**Entre sucursales** (`IInterBranch`, columnas `from_branch_id` y `to_branch_id`, 5 tablas; visibles si alguna de las
dos está en el alcance): `inventory.stock_transfers`, `inventory.stock_transfer_lines`,
`inventory.stock_transfer_line_batches`, `inventory.stock_transfer_discrepancies`, `inventory.stock_transfer_events`.

**De la empresa, sin filtro por sucursal** (directorio corporativo): `warehouse.branches`, `warehouse.warehouses`
(cuya `branch_id` es la raíz de la jerarquía), `warehouse.branch_users` (asignación de usuarios), catálogo, clientes,
proveedores, usuarios, roles, plan de cuentas. Las columnas `branch_id` de `iam.audit_logs`, `integration.api_keys`,
`integration.webhook_endpoints`, `integration.outbox_events` e `iam.sessions.active_branch_id` son atributos de
contexto, no particiones.

La migración rellena `branch_id` en los datos existentes bajando por la jerarquía (almacén → zona → pasillo → estantería
→ nivel → posición → existencia → movimiento; caja → turno → venta → factura → pago; orden → recepción → líneas), con
los triggers append-only y de asientos pausados solo durante el relleno; lo que no tiene camino (asientos, facturas de
proveedor sin recepción, devoluciones) va a la sucursal principal de la empresa (la V3 tenía una). Termina con una
comprobación: ninguna fila puede quedar con la sucursal `00000000-0000-0000-0000-000000000000`.

### 7.2 Las 13 tablas nuevas

| Esquema | Tabla | Para qué |
|---|---|---|
| `iam` | `iam.processed_requests` | idempotencia de los comandos del escritorio en modo nube |
| `sales` | `sales.external_orders` | idempotencia de los pedidos del e-commerce por API Key |
| `inventory` | `inventory.stock_transfer_movements` | vínculo línea ↔ movimiento de salida o entrada |
| `inventory` | `inventory.stock_transfer_discrepancies` | faltantes al recibir (deltas compensatorios) |
| `inventory` | `inventory.stock_transfer_events` | bitácora de la máquina de estados |
| `inventory` | `inventory.stock_transfer_line_batches` | manifiesto de despacho por lote |
| `integration` | `integration.api_keys` | llaves del API Gateway |
| `integration` | `integration.api_key_scopes` | alcances de cada llave |
| `integration` | `integration.webhook_endpoints` | destinos de webhooks con secreto cifrado |
| `integration` | `integration.webhook_endpoint_events` | eventos suscritos por destino |
| `integration` | `integration.outbox_events` | outbox transaccional (append-only) |
| `integration` | `integration.outbox_dispatch` | cola de despacho (mutable, SKIP LOCKED) |
| `integration` | `integration.webhook_deliveries` | intentos de entrega (append-only) |

### 7.3 Columnas nuevas o cambiadas en tablas existentes

| Tabla | Cambio |
|---|---|
| 33 tablas de la V3 (§7.1) | `branch_id uuid NOT NULL` + clave alterna `(tenant_id, branch_id, id)` + FK a su padre con `branch_id` |
| `iam.sessions` | `active_branch_id` (FK a `warehouse.branches`), `token_hash` (único, hex de 64), `expires_at` |
| `iam.audit_logs` | `channel` (`desktop`, `cloud`, `api`), `api_key_id` (FK a `integration.api_keys`), `branch_id` (sucursal activa) |
| `accounting.average_cost_history` | `branch_id`, `sequence` (≥ 1) + índice único `(tenant_id, variant_id, warehouse_id, sequence)` |
| `inventory.stock_transfers` | rediseño: `from_branch_id`, `to_branch_id`, `requested_by_user_id`, `requested_at`; `shipped_at` → `dispatched_at`; estados `Pending`/`Dispatched`/`Received`/`Cancelled` (antes `Draft`/`InTransit`…) |
| `inventory.stock_transfer_lines` | rediseño: `from_branch_id`, `to_branch_id`, `variant_id` (antes `source_stock_level_id`), `unit_cost`; se quitan `destination_stock_level_id`, `outbound_movement_id` e `inbound_movement_id` (los reemplazan el manifiesto y los vínculos) |
| `integration.webhook_endpoints` | secretos: `secret_ciphertext` + `secret_key_id` + `secret_version`; durante la rotación, `previous_secret_ciphertext`, `previous_secret_key_id`, `previous_secret_expires_at` |

### 7.4 Datos que agrega la migración a las empresas existentes

- Permisos `corporate.branches.all`, `corporate.branches.manage`, `inventory.transfers.manage`, `integration.manage` y la
  matriz: ADMIN (los cuatro), BODEGA (transferencias), GERENCIA (todas las sucursales y transferencias).
- Cuentas `1.1.06` Mercadería enviada a sucursales (activo) y `2.1.04` Mercadería recibida de sucursales (pasivo):
  en el consolidado, 1.1.06 − 2.1.04 = valor en tránsito.
- Módulos comerciales `CLOUD_HA`, `MULTI_BRANCH`, `API_INTEGRATIONS` y `GLOBAL_AUDIT` en `iam.modules`.

### 7.5 Esquema `reporting` (modelo de lectura)

| Objeto | Contenido |
|---|---|
| `reporting.mv_branch_stock` | por empresa, sucursal y variante: existencia, reservado, valor al costo vigente (mayor `sequence`) y `refreshed_at` |
| `reporting.mv_branch_daily_sales` | por empresa, sucursal y día (zona horaria de la empresa): tickets, ingresos (pagos) e IVA de las facturas emitidas, y `refreshed_at` |
| `reporting.v_branch_stock`, `reporting.v_branch_daily_sales` | vistas `security_barrier` sobre las anteriores, filtradas por empresa y sucursal visibles; las lee `MinvReadDbContext` (réplica `MINV_DB_READ` si existe) |

Se refrescan con `reporting.refresh_all()` cada 5 minutos desde el API Gateway.

### 7.6 Roles

| Rol | Tablas | Libros append-only | Funciones SECURITY DEFINER | RLS |
|---|---|---|---|---|
| `minv_owner` | dueño | dueño | dueño | la salta (solo migraciones) |
| `minv_server` | SELECT, INSERT, UPDATE, DELETE | solo SELECT e INSERT | EXECUTE en las 4 | sujeto (`NOBYPASSRLS`, no dueño) |
| `minv_app` | SELECT, INSERT, UPDATE, DELETE | solo SELECT e INSERT | — | sujeto |

## 8. V4.1 · Facturación SIAT (Computarizada en Línea)

Migración `V41SiatBilling` (`Persistence/Migrations/20260926003559_V41SiatBilling.cs` y su parcial `.Sql.cs`).
Reglas F-01 a F-17: `.claude/v41-billing-rules.md`; diseño: `docs/architecture/facturacion-siat-v4.1.md`. La V4.1
agrega el esquema `billing` (27 tablas), las devoluciones de venta (`sales`), los datos fiscales de las facturas de
proveedor (`purchasing`) y los datos de facturación del cliente. Resultado: **140 tablas en 9 esquemas**, 138 políticas
`tenant_isolation`, 55 `branch_isolation` RESTRICTIVAS y 24 triggers append-only.

Todas las tablas nuevas tienen `tenant_id` (RLS `tenant_isolation`, FK compuestas con la empresa); las de operación
(puntos de venta, códigos, documentos, contingencia, devoluciones) son **por sucursal** (`IBranchScoped`: RLS
`branch_isolation` y FK compuestas `(tenant_id, branch_id, x_id)`, regla F-14). La configuración de la empresa, los
catálogos sincronizados, la homologación y la bitácora SOAP no se filtran por sucursal.

### 8.1 Configuración, catálogos y homologación · esquema `billing` (15 tablas)

```mermaid
erDiagram
    siat_settings {
        uuid tenant_id PK, FK
        bigint nit
        varchar business_name
        varchar system_code
        integer environment
        integer modality
        boolean is_enabled
        varchar online_legend
        varchar offline_legend
        bigint clock_offset_ms
        timestamptz clock_synced_at
    }
    siat_environment_profiles {
        uuid id PK
        uuid tenant_id FK
        integer environment
        varchar codes_url
        varchar sync_url
        varchar operations_url
        varchar purchase_sale_url
        varchar computerized_url
        varchar adjustment_url
        varchar namespace
        varchar qr_base_url
        integer timeout_seconds
        varchar token_ciphertext
        varchar token_key_id
        date token_valid_until
        timestamptz token_updated_at
    }
    siat_branches {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        integer siat_code
        varchar municipality
        varchar phone
    }
    mail_settings {
        uuid tenant_id PK, FK
        varchar host
        integer port
        boolean use_ssl
        varchar user_name
        varchar password_ciphertext
        varchar password_key_id
        varchar from_address
        varchar from_name
        boolean is_enabled
    }
    siat_catalog_items {
        uuid id PK
        uuid tenant_id FK
        varchar catalog
        integer code
        varchar description
        boolean is_current
        timestamptz synced_at
    }
    siat_activities {
        uuid id PK
        uuid tenant_id FK
        varchar code
        varchar description
        varchar activity_type
        boolean is_current
        timestamptz synced_at
    }
    siat_activity_sectors {
        uuid id PK
        uuid tenant_id FK
        varchar activity_code
        integer document_sector
        varchar sector_type
        boolean is_current
        timestamptz synced_at
    }
    siat_legends {
        uuid id PK
        uuid tenant_id FK
        varchar activity_code
        varchar text
        boolean is_current
        timestamptz synced_at
    }
    siat_products {
        uuid id PK
        uuid tenant_id FK
        varchar activity_code
        integer product_code
        varchar description
        boolean is_current
        timestamptz synced_at
    }
    siat_sync_runs {
        uuid id PK
        uuid tenant_id FK
        integer environment
        varchar catalog
        integer items
        varchar error
        timestamptz occurred_at
        uuid user_id FK
    }
    product_siat_codes {
        uuid id PK
        uuid tenant_id FK
        uuid product_id FK
        varchar activity_code FK
        integer sin_product_code FK
    }
    unit_siat_codes {
        uuid id PK
        uuid tenant_id FK
        uuid unit_id FK
        integer sin_unit_code
    }
    payment_method_siat_codes {
        uuid id PK
        uuid tenant_id FK
        uuid payment_method_id FK
        integer sin_payment_method_code
    }
    customer_nit_checks {
        uuid id PK
        uuid tenant_id FK
        uuid customer_id FK
        bigint nit
        integer siat_code
        boolean is_valid
        varchar description
        timestamptz checked_at
        uuid user_id FK
    }
    siat_service_calls {
        uuid id PK
        uuid tenant_id FK
        integer environment
        varchar resource
        varchar operation
        uuid branch_id FK
        integer point_of_sale_code
        timestamptz occurred_at
        integer duration_ms
        integer http_status
        integer siat_code
        boolean succeeded
        varchar request_body
        varchar response_body
        varchar error
    }
    siat_settings ||--o{ siat_environment_profiles : "tenant_id"
    siat_activities ||--o{ siat_activity_sectors : "activity_code"
    siat_activities ||--o{ siat_legends : "activity_code"
    siat_activities ||--o{ siat_products : "activity_code"
    siat_products ||--o{ product_siat_codes : "(activity_code, product_code)"
```

Las relaciones actividad ↔ sector, leyenda y producto son lógicas (el SIN las sincroniza por código de actividad); la
única FK entre catálogos es la de la homologación de productos, que apunta a la clave alterna
`(tenant_id, activity_code, product_code)` de `billing.siat_products`: no se homologa contra un producto que el SIN no
publicó. Lo que el SIN retira queda con `is_current = false` (nunca se borra: hay documentos que lo usan).

| Tabla | Descripción | Clave | Referencias (FK) | Únicos / CHECK |
|---|---|---|---|---|
| `billing.siat_settings` | V4.1 · Configuración de facturación de la empresa (una fila por empresa, como `iam.tenant_configs`): NIT y razón social del Padrón, código del sistema autorizado, ambiente activo (1 producción, 2 pruebas y piloto), modalidad (siempre 2), activación, leyendas de modo en línea / fuera de línea y desfase del reloj con el SIN. · OCC xmin | (tenant_id) | tenant_id → iam.tenants | CHECK nit BETWEEN 1 AND 9999999999999<br>CHECK environment IN (1, 2)<br>CHECK modality = 2 |
| `billing.siat_environment_profiles` | V4.1 · Conexión por ambiente: URL de cada recurso SOAP (Códigos, Sincronización, Operaciones, Compra Venta, Computarizada, Documentos de Ajuste), namespace, URL base del QR, tiempo máximo de espera y token delegado **cifrado** (AES-256-GCM con `ISecretProtector`; `token_key_id` = clave maestra usada; el texto plano nunca se guarda) con su vigencia. · OCC xmin | (id) | — | único (tenant_id, environment)<br>CHECK environment IN (1, 2)<br>CHECK timeout_seconds BETWEEN 3 AND 120<br>CHECK (token_ciphertext IS NULL) = (token_key_id IS NULL) |
| `billing.siat_branches` | V4.1 · Sucursal de M-INV ↔ `codigoSucursal` del Padrón (0 = casa matriz), con el municipio y el teléfono que van en la factura. | (id) | branch_id → warehouse.branches | único (tenant_id, branch_id)<br>único (tenant_id, siat_code)<br>CHECK siat_code BETWEEN 0 AND 9999 |
| `billing.mail_settings` | V4.1 · Servidor SMTP de la empresa (una fila por empresa) para entregar el XML y la representación gráfica al comprador; contraseña solo **cifrada**. · OCC xmin | (tenant_id) | tenant_id → iam.tenants | CHECK port BETWEEN 1 AND 65535<br>CHECK (password_ciphertext IS NULL) = (password_key_id IS NULL) |
| `billing.siat_catalog_items` | V4.1 · Valor de una paramétrica sincronizada (métodos de pago, unidades, motivos de anulación, eventos significativos, monedas, tipos de documento…; `catalog` = nombre estable de `SiatCatalogNames`). | (id) | — | único (tenant_id, catalog, code)<br>CHECK code >= 0 |
| `billing.siat_activities` | V4.1 · Actividad económica del NIT (código CAEB como texto: conserva los ceros). | (id) | — | único (tenant_id, code) |
| `billing.siat_activity_sectors` | V4.1 · Documentos sector habilitados para cada actividad. | (id) | — | único (tenant_id, activity_code, document_sector)<br>CHECK document_sector BETWEEN 1 AND 99 |
| `billing.siat_legends` | V4.1 · Leyendas de la Ley N° 453 por actividad (la factura lleva una al azar, guardada en el documento). | (id) | — | único (tenant_id, activity_code, text) |
| `billing.siat_products` | V4.1 · Productos y servicios genéricos del SIN (destino de la homologación). | (id) | — | clave alterna (tenant_id, activity_code, product_code)<br>CHECK product_code BETWEEN 1 AND 99999999 |
| `billing.siat_sync_runs` | V4.1 · Cada sincronización de un catálogo (ambiente, filas recibidas, error). · **append-only** | (id) | user_id → iam.users | índice (tenant_id, catalog, occurred_at)<br>CHECK items >= 0 |
| `billing.product_siat_codes` | V4.1 · **Homologación** de un producto de M-INV con la actividad y el producto del SIN (tabla propia: el catálogo no gana columnas nulas). · OCC xmin | (id) | product_id → catalog.products<br>(activity_code, sin_product_code) → billing.siat_products (activity_code, product_code) | único (tenant_id, product_id) |
| `billing.unit_siat_codes` | V4.1 · Homologación de una unidad de medida con la paramétrica «Unidad de Medida» del SIN. | (id) | unit_id → catalog.units_of_measure | único (tenant_id, unit_id)<br>CHECK sin_unit_code BETWEEN 1 AND 999 |
| `billing.payment_method_siat_codes` | V4.1 · Homologación de un medio de pago con la paramétrica «Tipo Método Pago» del SIN. | (id) | payment_method_id → sales.payment_methods | único (tenant_id, payment_method_id)<br>CHECK sin_payment_method_code BETWEEN 1 AND 999 |
| `billing.customer_nit_checks` | V4.1 · Cada verificación de un NIT contra el Padrón (`verificarNit`: código 986, 994…). · **append-only** | (id) | customer_id → sales.customers<br>user_id → iam.users | índice (tenant_id, nit, checked_at)<br>CHECK nit > 0 |
| `billing.siat_service_calls` | V4.1 · Bitácora técnica de cada llamada SOAP al SIN (recurso, operación, duración, HTTP, código SIAT, cuerpos) **sin el token** (regla F-12). Es de la empresa: `branch_id` es un atributo de contexto, no una partición. · **append-only** | (id) | branch_id → warehouse.branches | índice (tenant_id, occurred_at)<br>CHECK duration_ms >= 0 |

### 8.2 Operación: puntos de venta, códigos, documentos fiscales y contingencia · esquema `billing` (12 tablas)

```mermaid
erDiagram
    siat_points_of_sale {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        integer environment
        integer code
        integer type_code
        varchar name
        varchar description
        uuid pos_register_id FK
        varchar mode
        timestamptz mode_since
        timestamptz last_contact_at
        integer consecutive_failures
        varchar last_error
        timestamptz retry_at
        timestamptz closed_at
    }
    siat_cuis {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid point_of_sale_id FK
        varchar code
        timestamptz valid_until
        timestamptz obtained_at
    }
    siat_cufds {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid point_of_sale_id FK
        uuid cuis_id FK
        varchar code
        varchar control_code
        varchar address
        timestamptz valid_until
        timestamptz obtained_at
    }
    fiscal_documents {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        integer environment
        varchar kind
        uuid point_of_sale_id FK
        uuid cuis_id FK
        uuid cufd_id FK
        integer document_sector
        integer document_type
        integer emission_type
        bigint number
        varchar cuf
        timestamp issued_at
        uuid invoice_id FK
        uuid sales_return_id FK
        uuid replaces_document_id FK
        uuid customer_id FK
        varchar customer_code
        integer buyer_document_type
        varchar buyer_document_number
        varchar buyer_complement
        varchar buyer_name
        varchar buyer_email
        integer payment_method_code
        varchar card_number_masked
        integer currency_code
        numeric exchange_rate
        numeric additional_discount
        numeric gift_card_amount
        integer exception_code
        varchar cafc
        varchar legend
        varchar user_code
        varchar status
        boolean is_reverted
        varchar reception_code
        integer last_siat_code
        uuid significant_event_id FK
        uuid package_id FK
        integer package_position
        integer void_reason_code
        timestamptz voided_at
        timestamptz reverted_at
        timestamptz created_at
    }
    fiscal_note_references {
        uuid document_id PK, FK
        uuid tenant_id FK
        uuid branch_id FK
        uuid original_document_id FK
        bigint original_number
        varchar original_cuf
        timestamp original_issued_at
        numeric discount_share
    }
    fiscal_document_lines {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid document_id FK
        integer line_number
        uuid variant_id FK
        varchar activity_code
        integer sin_product_code
        varchar product_code
        varchar description
        numeric quantity
        integer sin_unit_code
        numeric unit_price
        numeric discount
        integer transaction_code
        varchar serial_number
        varchar imei
    }
    fiscal_document_files {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid document_id FK
        text xml
        varchar gzip_sha256
        timestamptz created_at
    }
    fiscal_document_events {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid document_id FK
        varchar action
        timestamptz occurred_at
        integer siat_code
        varchar description
        varchar reception_code
        varchar messages
        uuid user_id FK
    }
    fiscal_deliveries {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid document_id FK
        varchar channel
        varchar recipient
        boolean succeeded
        varchar error
        timestamptz occurred_at
        uuid user_id FK
    }
    significant_events {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid point_of_sale_id FK
        integer environment
        varchar kind
        integer event_code
        varchar description
        timestamp started_at
        timestamp ended_at
        uuid event_cufd_id FK
        uuid send_cufd_id FK
        uuid contingency_code_id FK
        varchar reception_code
        varchar status
        timestamptz registered_at
        timestamptz created_at
        uuid created_by_user_id FK
    }
    fiscal_packages {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid significant_event_id FK
        uuid point_of_sale_id FK
        uuid send_cufd_id FK
        integer document_sector
        integer document_type
        varchar cafc
        varchar sha256
        varchar reception_code
        varchar status
        timestamptz sent_at
        timestamptz validated_at
        integer last_siat_code
        text messages
    }
    contingency_codes {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        integer document_sector
        varchar code
        bigint number_from
        bigint number_to
        date valid_until
        boolean is_active
    }
    siat_points_of_sale ||--o{ siat_cuis : "point_of_sale_id"
    siat_points_of_sale ||--o{ siat_cufds : "point_of_sale_id"
    siat_cuis ||--o{ siat_cufds : "cuis_id"
    siat_points_of_sale ||--o{ fiscal_documents : "point_of_sale_id"
    siat_cuis ||--o{ fiscal_documents : "cuis_id"
    siat_cufds ||--o{ fiscal_documents : "cufd_id"
    fiscal_documents ||--o{ fiscal_document_lines : "document_id"
    fiscal_documents ||--o| fiscal_note_references : "document_id"
    fiscal_documents |o--o{ fiscal_note_references : "original_document_id"
    fiscal_documents ||--o| fiscal_document_files : "document_id"
    fiscal_documents ||--o{ fiscal_document_events : "document_id"
    fiscal_documents ||--o{ fiscal_deliveries : "document_id"
    fiscal_documents |o--o{ fiscal_documents : "replaces_document_id"
    siat_points_of_sale ||--o{ significant_events : "point_of_sale_id"
    siat_cufds ||--o{ significant_events : "event_cufd_id"
    siat_cufds |o--o{ significant_events : "send_cufd_id"
    contingency_codes |o--o{ significant_events : "contingency_code_id"
    significant_events |o--o{ fiscal_documents : "significant_event_id"
    significant_events ||--o{ fiscal_packages : "significant_event_id"
    siat_points_of_sale ||--o{ fiscal_packages : "point_of_sale_id"
    siat_cufds ||--o{ fiscal_packages : "send_cufd_id"
    fiscal_packages |o--o{ fiscal_documents : "package_id"
```

Todas las FK de este diagrama son compuestas con la sucursal (`(tenant_id, branch_id, x_id)`): un CUFD, un documento,
una línea o un paquete no pueden ser de otra sucursal que su punto de venta.

| Tabla | Descripción | Clave | Referencias (FK) | Únicos / CHECK |
|---|---|---|---|---|
| `billing.siat_points_of_sale` | V4.1 · Punto de venta del SIN por sucursal y ambiente (código 0 = sin punto de venta; los demás los asigna `registroPuntoVenta`), caja de M-INV vinculada y **modo** de operación (`Online`, `Offline`, `ManualContingency`, `Recovering`: estado materializado; los hechos viven en sus tablas). · OCC xmin · **por sucursal** | (id) | branch_id → warehouse.branches<br>(branch_id, pos_register_id) → sales.pos_registers | único (tenant_id, environment, branch_id, code)<br>único (tenant_id, environment, pos_register_id) WHERE pos_register_id IS NOT NULL<br>CHECK environment IN (1, 2)<br>CHECK code BETWEEN 0 AND 9999<br>CHECK type_code BETWEEN 0 AND 99<br>CHECK mode IN (…)<br>CHECK consecutive_failures >= 0<br>CHECK closed_at IS NULL OR code > 0 |
| `billing.siat_cuis` | V4.1 · Historial de CUIS (365 días) de un punto de venta; el vigente es el último no vencido. · **append-only** · **por sucursal** | (id) | (branch_id, point_of_sale_id) → billing.siat_points_of_sale | índice (point_of_sale_id, valid_until)<br>CHECK valid_until > obtained_at |
| `billing.siat_cufds` | V4.1 · Historial de CUFD (24 h) con el **código de control** que se concatena al CUF y la **dirección** del XML; obtenido con un CUIS del mismo punto de venta. · **append-only** · **por sucursal** | (id) | (branch_id, point_of_sale_id) → billing.siat_points_of_sale<br>(branch_id, cuis_id) → billing.siat_cuis | índice (point_of_sale_id, obtained_at)<br>CHECK valid_until > obtained_at |
| `billing.fiscal_documents` | V4.1 · Documento fiscal digital (agregado): factura Compra Venta (sector 1, tipo 1) o nota Crédito-Débito (sector 24, tipo 3); ambiente, punto de venta, CUIS y CUFD usados, tipo de emisión, **número**, **CUF**, hora fiscal con milisegundos (`timestamp without time zone`: la misma del CUF y del XML), comprador **congelado**, método de pago y tarjeta **enmascarada**, estado, anulación y reversión, evento y paquete. Los totales NO se guardan (vista `billing.v_fiscal_document_totals`). · OCC xmin · **por sucursal** | (id) | (branch_id, point_of_sale_id) → billing.siat_points_of_sale<br>(branch_id, cuis_id) → billing.siat_cuis<br>(branch_id, cufd_id) → billing.siat_cufds<br>(branch_id, invoice_id) → sales.invoices<br>(branch_id, sales_return_id) → sales.sales_returns<br>(branch_id, significant_event_id) → billing.significant_events<br>(branch_id, package_id) → billing.fiscal_packages<br>(branch_id, replaces_document_id) → billing.fiscal_documents<br>customer_id → sales.customers | único (tenant_id, environment, point_of_sale_id, document_sector, number)<br>único (tenant_id, cuf)<br>único (tenant_id, invoice_id) WHERE invoice_id IS NOT NULL AND status IN ('Pending', 'Valid', 'Offline', 'InPackage'): una venta tiene un solo documento activo<br>CHECK document_sector IN (1, 24) y document_type IN (1, 3), coherentes con `kind`<br>CHECK emission_type IN (1, 2)<br>CHECK exception_code IN (0, 1)<br>CHECK number > 0 AND number <= 9999999999<br>CHECK status IN (…)<br>CHECK buyer_document_type BETWEEN 1 AND 5; complemento solo con CI<br>CHECK cafc solo con emisión 2; notas solo en línea<br>CHECK invoice_id solo en facturas; sales_return_id solo en notas<br>CHECK payment_method_code obligatorio en facturas (1 a 999)<br>CHECK montos >= 0 y exchange_rate > 0<br>CHECK paquete y posición (1 a 500) juntos; `InPackage` exige paquete<br>CHECK `Voided` exige voided_at; is_reverted = (reverted_at IS NOT NULL) |
| `billing.fiscal_note_references` | V4.1 · Subtipo 1:1 de las notas crédito-débito: factura original (documento de M-INV o transcrita: número, CUF o código de autorización, fecha) y descuento prorrateado (`montoDescuentoCreditoDebito`). · **por sucursal** | (document_id) | (branch_id, document_id) → billing.fiscal_documents<br>(branch_id, original_document_id) → billing.fiscal_documents | CHECK original_number > 0 AND original_number <= 9999999999<br>CHECK discount_share IS NULL OR discount_share >= 0 |
| `billing.fiscal_document_lines` | V4.1 · Detalle **congelado** del documento: actividad, código de producto SIN, código propio, descripción, cantidad, unidad SIN, precio, descuento, transacción (1 original / 2 devuelto, solo notas), serie e IMEI. Cantidades, precios y descuentos `numeric(20,10)`. · **append-only** · **por sucursal** | (id) | (branch_id, document_id) → billing.fiscal_documents<br>variant_id → catalog.product_variants | único (document_id, line_number)<br>CHECK line_number BETWEEN 1 AND 500<br>CHECK quantity > 0, unit_price > 0, discount >= 0<br>CHECK sin_product_code BETWEEN 1 AND 99999999<br>CHECK sin_unit_code BETWEEN 1 AND 999<br>CHECK transaction_code IS NULL OR transaction_code IN (1, 2) |
| `billing.fiscal_document_files` | V4.1 · XML exacto del documento (validado contra el XSD oficial) y SHA-256 del GZIP enviado (huella). · **append-only** · **por sucursal** | (id) | (branch_id, document_id) → billing.fiscal_documents | único (document_id)<br>CHECK gzip_sha256 ~ '^[0-9a-f]{64}$' |
| `billing.fiscal_document_events` | V4.1 · Bitácora del documento: cada envío y respuesta del SIN (códigos, código de recepción, mensajes en JSON) y cada acción del usuario (anulación, reversión, entrega…). · **append-only** · **por sucursal** | (id) | (branch_id, document_id) → billing.fiscal_documents<br>user_id → iam.users | índice (document_id, occurred_at)<br>CHECK action IN (…) |
| `billing.fiscal_deliveries` | V4.1 · Entregas del documento al comprador (correo con XML + PDF, impresión, PDF) con su resultado. · **append-only** · **por sucursal** | (id) | (branch_id, document_id) → billing.fiscal_documents<br>user_id → iam.users | índice (document_id, occurred_at)<br>CHECK channel IN ('Email', 'Print', 'Pdf') |
| `billing.significant_events` | V4.1 · Evento significativo (fuera de línea automático o contingencia manual con CAFC): código y descripción del catálogo, inicio y fin en hora fiscal (`timestamp without time zone`), CUFD del evento, CUFD nuevo de envío, CAFC, código de recepción y estado (`Open → Closed → Registered → PackagesSent → Reconciled / WithObservations`). · OCC xmin · **por sucursal** | (id) | (branch_id, point_of_sale_id) → billing.siat_points_of_sale<br>(branch_id, event_cufd_id) → billing.siat_cufds<br>(branch_id, send_cufd_id) → billing.siat_cufds<br>(branch_id, contingency_code_id) → billing.contingency_codes<br>created_by_user_id → iam.users | índice (tenant_id, point_of_sale_id, status)<br>CHECK environment IN (1, 2); kind y status IN (…)<br>CHECK event_code BETWEEN 1 AND 99<br>CHECK ended_at IS NULL OR ended_at > started_at<br>CHECK (status = 'Open') = (ended_at IS NULL)<br>CHECK CAFC solo en la contingencia manual<br>CHECK registro, código de recepción y CUFD de envío juntos desde `Registered` |
| `billing.fiscal_packages` | V4.1 · Paquete de contingencia GZIP(TAR) de hasta 500 documentos del mismo sector, evento y CAFC: huella, código de recepción y resultado de la validación (`Sent → Validated / Observed / Rejected`). · OCC xmin · **por sucursal** | (id) | (branch_id, significant_event_id) → billing.significant_events<br>(branch_id, point_of_sale_id) → billing.siat_points_of_sale<br>(branch_id, send_cufd_id) → billing.siat_cufds | CHECK status IN (…)<br>CHECK document_sector BETWEEN 1 AND 99<br>CHECK sha256 ~ '^[0-9a-f]{64}$'<br>CHECK (status = 'Sent') = (validated_at IS NULL) |
| `billing.contingency_codes` | V4.1 · CAFC: talonario de facturas de contingencia manual por sucursal y documento sector (rango y vigencia). · **por sucursal** | (id) | branch_id → warehouse.branches | único (tenant_id, branch_id, document_sector, code)<br>CHECK document_sector BETWEEN 1 AND 99<br>CHECK number_from > 0 AND number_to >= number_from |

### 8.3 Devoluciones de venta, datos fiscales de compras y datos de facturación del cliente

```mermaid
erDiagram
    sales_returns {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        varchar number
        uuid invoice_id FK
        uuid customer_id FK
        varchar reason
        uuid refund_payment_method_id FK
        uuid pos_session_id FK
        uuid user_id FK
        timestamptz returned_at
    }
    sales_return_lines {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid sales_return_id FK
        uuid sales_order_line_id FK
        uuid variant_id FK
        numeric quantity
        uuid stock_movement_id FK
    }
    supplier_invoice_fiscal {
        uuid supplier_invoice_id PK, FK
        uuid tenant_id FK
        uuid branch_id FK
        varchar authorization_code
        varchar control_code
        numeric total_amount
        numeric discounts
        numeric not_subject_to_vat
        integer purchase_type
    }
    sales_returns ||--o{ sales_return_lines : "sales_return_id"
```

| Tabla | Descripción | Clave | Referencias (FK) | Únicos / CHECK |
|---|---|---|---|---|
| `sales.sales_returns` | V4.1 · Devolución (total o parcial) de una venta, en la sucursal de la venta: el stock vuelve con movimientos de devolución, se reembolsa con el medio indicado y, si la venta estaba facturada, se emite una nota crédito-débito (`billing.fiscal_documents.sales_return_id`). El importe no se guarda: sale de las líneas del pedido. · OCC xmin · **por sucursal** | (id) | (branch_id, invoice_id) → sales.invoices<br>(branch_id, pos_session_id) → sales.pos_sessions<br>customer_id → sales.customers<br>refund_payment_method_id → sales.payment_methods<br>user_id → iam.users | único (tenant_id, number)<br>índice (tenant_id, returned_at) |
| `sales.sales_return_lines` | V4.1 · Línea devuelta: de una línea de la misma venta y sucursal, con el movimiento de stock que la repuso. · **por sucursal** | (id) | (branch_id, sales_return_id) → sales.sales_returns<br>(branch_id, sales_order_line_id) → sales.sales_order_lines<br>variant_id → catalog.product_variants<br>(branch_id, stock_movement_id) → inventory.stock_movements | único (sales_return_id, sales_order_line_id)<br>único (stock_movement_id) WHERE stock_movement_id IS NOT NULL<br>CHECK quantity > 0 |
| `purchasing.supplier_invoice_fiscal` | V4.1 · Datos fiscales de la factura del proveedor (subtipo 1:1 de `purchasing.supplier_invoices`) para el libro de compras: CUF o código de autorización, código de control, importe total, descuentos, importe no sujeto a crédito fiscal y tipo de compra del RCV. La base y el crédito fiscal (13 %) se derivan. · **por sucursal** | (supplier_invoice_id) | (branch_id, supplier_invoice_id) → purchasing.supplier_invoices | CHECK total_amount > 0<br>CHECK discounts >= 0 AND not_subject_to_vat >= 0 AND total_amount − not_subject_to_vat − discounts >= 0<br>CHECK purchase_type BETWEEN 1 AND 5 |

`sales.customers` gana `document_type smallint` (1 CI, 2 CEX, 3 PAS, 4 OD, 5 NIT; `tax_id` es el número) y
`complement varchar(5)` (complemento del SEGIP) con `CHECK (document_type IS NULL OR document_type BETWEEN 1 AND 5)` y
`CHECK (complement IS NULL OR document_type = 1)` (§4, «Ventas y POS»).

### 8.4 Sucursal, append-only y normalización

**Por sucursal** (15 tablas nuevas, 50 en total; política `branch_isolation` RESTRICTIVA sobre `branch_id`, lista
`BranchTablesV41` de la migración): `billing.siat_points_of_sale`, `billing.siat_cuis`, `billing.siat_cufds`,
`billing.fiscal_documents`, `billing.fiscal_note_references`, `billing.fiscal_document_lines`,
`billing.fiscal_document_files`, `billing.fiscal_document_events`, `billing.fiscal_deliveries`,
`billing.significant_events`, `billing.fiscal_packages`, `billing.contingency_codes`, `sales.sales_returns`,
`sales.sales_return_lines`, `purchasing.supplier_invoice_fiscal`.

**Append-only** (9 libros nuevos, 24 en total; `trg_append_only` y privilegios revocados, lista `AppendOnlyTablesV41`,
regla F-11): `billing.siat_cuis`, `billing.siat_cufds`, `billing.siat_sync_runs`, `billing.customer_nit_checks`,
`billing.siat_service_calls`, `billing.fiscal_document_lines`, `billing.fiscal_document_files`,
`billing.fiscal_document_events`, `billing.fiscal_deliveries`.

**Normalización y redundancia controlada.** Los totales fiscales NO se guardan: `subTotal = round2(cantidad × precio) −
descuento`, `montoTotal = Σ subTotal − descuentoAdicional`, `montoTotalSujetoIva = montoTotal − montoGiftCard`, en las
notas `montoTotalDevuelto = Σ subTotal (transacción 2) − descuento prorrateado`, y el IVA es el 13 % de la base; los
calculan el dominio (`FiscalDocument`) y la vista `billing.v_fiscal_document_totals`. Son **instantáneas legales**
documentadas (como `tenant_id`) los datos del comprador en `fiscal_documents` y el detalle en `fiscal_document_lines`: un
documento emitido no cambia si mañana cambian el cliente o el catálogo. Son estado materializado con su bitácora
`fiscal_documents.status` (cada cambio deja su fila en `fiscal_document_events`), `siat_points_of_sale.mode`,
`significant_events.status` y `fiscal_packages.status`. La homologación vive en tablas propias (`product_siat_codes`,
`unit_siat_codes`, `payment_method_siat_codes`) para no agregar columnas opcionales a los maestros.

### 8.5 Datos que agrega la migración

- Permisos `billing.view`, `billing.issue`, `billing.void`, `billing.contingency` y `billing.configure` en todas las
  empresas existentes, con la matriz de `PermissionCodes.ForRole`: ADMIN (los cinco), GERENCIA (view, void,
  contingency), VENTAS y CAJERO (view, issue), CONSULTA (view); BODEGA, ninguno. Las empresas nuevas los reciben del
  aprovisionamiento.
- Módulo comercial `FISCAL_SIAT` «Facturación SIAT (computarizada en línea)» en `iam.modules` (lo siembra la parte
  generada de la migración: `iam.modules` está en `HasData`).

### 8.6 Roles

`minv_server` y `minv_app` (solo si existen al migrar): `USAGE` en `billing`; SELECT, INSERT, UPDATE y DELETE en las 30
tablas nuevas, salvo UPDATE, DELETE y TRUNCATE en los 9 libros append-only; SELECT en `billing.v_fiscal_document_totals`.
`EXECUTE` en `billing.siat_active_tenants()` SOLO para `minv_server` (con las 4 de la V4, 5 funciones SECURITY DEFINER).
