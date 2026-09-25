-- =====================================================================================================================
-- M-INV V4 · Inicialización de la base de datos PostgreSQL (110 tablas, 8 esquemas + modelo de lectura, 5FN)
-- Z&P Software Fast Solutions
--
-- ARCHIVO GENERADO por tools/build_v3.ps1 (cabecera + «dotnet ef migrations script --idempotent»). No lo edite a mano:
-- cambie el modelo (src/2. Infrastructure/MINV.Infrastructure) y regenere. Es idempotente: se puede volver a ejecutar.
--
-- Requisitos: PostgreSQL 15 o superior (se recomienda 16). En la nube: DigitalOcean, AWS RDS o Supabase (con PgBouncer
-- use el modo SESIÓN o la conexión directa: la seguridad por filas usa variables de sesión).
--
-- 1) Como superusuario o administrador (una sola vez):
--      CREATE ROLE minv_owner  LOGIN PASSWORD '<clave del dueño>';
--      CREATE ROLE minv_server LOGIN NOBYPASSRLS PASSWORD '<clave del servidor>';   -- V4: servidor en la nube y API Gateway
--      CREATE ROLE minv_app    LOGIN NOBYPASSRLS PASSWORD '<clave de la aplicación>'; -- escritorio con conexión directa
--      CREATE DATABASE minv OWNER minv_owner ENCODING 'UTF8' TEMPLATE template0;
--    (o bien: minv roles --clave-servidor … --clave-app … · tools\bd_nube.ps1 hace todo)
-- 2) Como dueño, conectado a la base «minv» (los roles deben existir: la migración les otorga los privilegios):
--      psql -U minv_owner -d minv -v ON_ERROR_STOP=1 -f scripts/db_init.sql
-- 3) Conexiones (siempre sujetas a Row Level Security por empresa y por sucursal):
--      servidores: MINV_DB = "Host=…;Database=minv;Username=minv_server;Password=…;SSL Mode=VerifyFull"
--      escritorio directo (base local): MINV_DB = "Host=localhost;Port=5432;Database=minv;Username=minv_app;Password=…"
--
-- Contenido: esquemas iam, catalog, warehouse, inventory, purchasing, sales, accounting e integration; tablas con PK, FK
-- compuestas (tenant_id, id) y (tenant_id, branch_id, id); restricciones CHECK e índices únicos; catálogo de módulos
-- comerciales; triggers append-only, un valor por atributo y asientos cuadrados; Row Level Security por empresa y
-- política restrictiva por sucursal; funciones SECURITY DEFINER para el servidor; esquema reporting (vistas
-- materializadas + vistas filtradas); vistas v_stock_by_variant, v_conservation_breaches, v_transfer_breaches y
-- v_activity; privilegios de minv_app y minv_server.
-- =====================================================================================================================

DO $$
BEGIN
    IF current_setting('server_version_num')::int < 150000 THEN
        RAISE EXCEPTION 'M-INV requiere PostgreSQL 15 o superior (servidor: %)', current_setting('server_version');
    END IF;
END;
$$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'iam') THEN
        CREATE SCHEMA iam;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS iam.__ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'iam') THEN
            CREATE SCHEMA iam;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'accounting') THEN
            CREATE SCHEMA accounting;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'sales') THEN
            CREATE SCHEMA sales;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'inventory') THEN
            CREATE SCHEMA inventory;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'warehouse') THEN
            CREATE SCHEMA warehouse;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'catalog') THEN
            CREATE SCHEMA catalog;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'purchasing') THEN
            CREATE SCHEMA purchasing;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE iam.modules (
        id uuid NOT NULL,
        code character varying(40) NOT NULL,
        name character varying(100) NOT NULL,
        description character varying(400),
        setup_price_bs numeric(19,4) NOT NULL,
        monthly_fee_bs numeric(19,4) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        CONSTRAINT pk_modules PRIMARY KEY (id),
        CONSTRAINT ck_modules_prices CHECK (setup_price_bs >= 0 AND monthly_fee_bs >= 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE iam.tenants (
        id uuid NOT NULL,
        code character varying(20) NOT NULL,
        legal_name character varying(150) NOT NULL,
        tax_id character varying(30),
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        CONSTRAINT pk_tenants PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE accounting.accounts (
        id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name character varying(120) NOT NULL,
        account_type character varying(20) NOT NULL,
        parent_account_id uuid,
        is_postable boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_accounts PRIMARY KEY (id),
        CONSTRAINT ak_accounts_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_accounts_padre CHECK (parent_account_id IS NULL OR parent_account_id <> id),
        CONSTRAINT fk_accounts_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_accounts_tenant_id_parent_account_id FOREIGN KEY (tenant_id, parent_account_id) REFERENCES accounting.accounts (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE inventory.adjustment_reasons (
        id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name character varying(80) NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_adjustment_reasons PRIMARY KEY (id),
        CONSTRAINT ak_adjustment_reasons_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_adjustment_reasons_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE catalog.attributes (
        id uuid NOT NULL,
        name character varying(60) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_attributes PRIMARY KEY (id),
        CONSTRAINT ak_attributes_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_attributes_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE catalog.barcode_types (
        id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name character varying(60) NOT NULL,
        length integer,
        has_check_digit boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_barcode_types PRIMARY KEY (id),
        CONSTRAINT ak_barcode_types_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_barcode_types_longitud CHECK (length IS NULL OR (length >= 1 AND length <= 64)),
        CONSTRAINT fk_barcode_types_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE catalog.brands (
        id uuid NOT NULL,
        name character varying(80) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_brands PRIMARY KEY (id),
        CONSTRAINT ak_brands_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_brands_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE catalog.categories (
        id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name character varying(80) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_categories PRIMARY KEY (id),
        CONSTRAINT ak_categories_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_categories_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE sales.countries (
        id uuid NOT NULL,
        iso_code character varying(2) NOT NULL,
        name character varying(80) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_countries PRIMARY KEY (id),
        CONSTRAINT ak_countries_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_countries_iso CHECK (iso_code ~ '^[A-Z]{2}$'),
        CONSTRAINT fk_countries_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE accounting.currencies (
        id uuid NOT NULL,
        code character varying(3) NOT NULL,
        name character varying(60) NOT NULL,
        symbol character varying(8) NOT NULL,
        decimal_places smallint NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_currencies PRIMARY KEY (id),
        CONSTRAINT ak_currencies_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_currencies_decimales CHECK (decimal_places BETWEEN 0 AND 4),
        CONSTRAINT ck_currencies_iso CHECK (code ~ '^[A-Z]{3}$'),
        CONSTRAINT fk_currencies_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE accounting.fiscal_periods (
        id uuid NOT NULL,
        year smallint NOT NULL,
        month smallint NOT NULL,
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_fiscal_periods PRIMARY KEY (id),
        CONSTRAINT ak_fiscal_periods_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_fiscal_periods_anio CHECK (year BETWEEN 2000 AND 2100),
        CONSTRAINT ck_fiscal_periods_mes CHECK (month BETWEEN 1 AND 12),
        CONSTRAINT fk_fiscal_periods_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE warehouse.location_types (
        id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name character varying(60) NOT NULL,
        allows_picking boolean NOT NULL,
        allows_receiving boolean NOT NULL,
        allows_sales boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_location_types PRIMARY KEY (id),
        CONSTRAINT ak_location_types_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_location_types_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE inventory.movement_types (
        id uuid NOT NULL,
        code character varying(30) NOT NULL,
        name character varying(80) NOT NULL,
        description character varying(200),
        stock_factor smallint NOT NULL,
        domain character varying(20) NOT NULL,
        requires_notes boolean NOT NULL,
        is_initial_balance boolean NOT NULL,
        is_system boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_movement_types PRIMARY KEY (id),
        CONSTRAINT ak_movement_types_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_movement_types_factor CHECK (stock_factor IN (-1, 1)),
        CONSTRAINT fk_movement_types_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE sales.payment_methods (
        id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name character varying(60) NOT NULL,
        requires_reference boolean NOT NULL,
        opens_cash_drawer boolean NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_payment_methods PRIMARY KEY (id),
        CONSTRAINT ak_payment_methods_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_payment_methods_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE iam.permissions (
        id uuid NOT NULL,
        code character varying(80) NOT NULL,
        description character varying(200) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_permissions PRIMARY KEY (id),
        CONSTRAINT ak_permissions_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_permissions_codigo_minusculas CHECK (code = lower(code)),
        CONSTRAINT fk_permissions_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE iam.roles (
        id uuid NOT NULL,
        code character varying(30) NOT NULL,
        name character varying(80) NOT NULL,
        is_system boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_roles PRIMARY KEY (id),
        CONSTRAINT ak_roles_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_roles_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE inventory.stock_statuses (
        id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name character varying(40) NOT NULL,
        priority integer NOT NULL,
        suggested_action character varying(200) NOT NULL,
        requires_action boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_stock_statuses PRIMARY KEY (id),
        CONSTRAINT ak_stock_statuses_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_stock_statuses_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE purchasing.suppliers (
        id uuid NOT NULL,
        code character varying(20) NOT NULL,
        legal_name character varying(150) NOT NULL,
        tax_id character varying(30),
        lead_time_days integer NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_suppliers PRIMARY KEY (id),
        CONSTRAINT ak_suppliers_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_suppliers_dias CHECK (lead_time_days >= 0),
        CONSTRAINT fk_suppliers_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE catalog.taxes (
        id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name character varying(80) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_taxes PRIMARY KEY (id),
        CONSTRAINT ak_taxes_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_taxes_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE iam.tenant_modules (
        tenant_id uuid NOT NULL,
        module_id uuid NOT NULL,
        activated_at timestamp with time zone NOT NULL,
        expires_at timestamp with time zone,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        CONSTRAINT pk_tenant_modules PRIMARY KEY (tenant_id, module_id),
        CONSTRAINT ck_tenant_modules_vigencia CHECK (expires_at IS NULL OR expires_at > activated_at),
        CONSTRAINT fk_tenant_modules_module_id FOREIGN KEY (module_id) REFERENCES iam.modules (id) ON DELETE RESTRICT,
        CONSTRAINT fk_tenant_modules_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE catalog.units_of_measure (
        id uuid NOT NULL,
        code character varying(10) NOT NULL,
        name character varying(40) NOT NULL,
        allows_decimals boolean NOT NULL,
        description character varying(200),
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_units_of_measure PRIMARY KEY (id),
        CONSTRAINT ak_units_of_measure_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_units_of_measure_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE iam.users (
        id uuid NOT NULL,
        email character varying(254) NOT NULL,
        display_name character varying(120) NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_users PRIMARY KEY (id),
        CONSTRAINT ak_users_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_users_email_minusculas CHECK (email = lower(email)),
        CONSTRAINT fk_users_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE catalog.attribute_values (
        id uuid NOT NULL,
        attribute_id uuid NOT NULL,
        value character varying(60) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_attribute_values PRIMARY KEY (id),
        CONSTRAINT ak_attribute_values_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_attribute_values_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_attribute_values_tenant_id_attribute_id FOREIGN KEY (tenant_id, attribute_id) REFERENCES catalog.attributes (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE catalog.models (
        id uuid NOT NULL,
        brand_id uuid NOT NULL,
        name character varying(80) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_models PRIMARY KEY (id),
        CONSTRAINT ak_models_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_models_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_models_tenant_id_brand_id FOREIGN KEY (tenant_id, brand_id) REFERENCES catalog.brands (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE catalog.category_hierarchies (
        ancestor_id uuid NOT NULL,
        descendant_id uuid NOT NULL,
        depth integer NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_category_hierarchies PRIMARY KEY (ancestor_id, descendant_id),
        CONSTRAINT ck_category_hierarchies_profundidad CHECK (depth >= 0),
        CONSTRAINT ck_category_hierarchies_reflexiva CHECK ((depth = 0) = (ancestor_id = descendant_id)),
        CONSTRAINT fk_category_hierarchies_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_category_hierarchies_tenant_id_ancestor_id FOREIGN KEY (tenant_id, ancestor_id) REFERENCES catalog.categories (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_category_hierarchies_tenant_id_descendant_id FOREIGN KEY (tenant_id, descendant_id) REFERENCES catalog.categories (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE sales.states (
        id uuid NOT NULL,
        country_id uuid NOT NULL,
        code character varying(10) NOT NULL,
        name character varying(80) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_states PRIMARY KEY (id),
        CONSTRAINT ak_states_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_states_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_states_tenant_id_country_id FOREIGN KEY (tenant_id, country_id) REFERENCES sales.countries (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE accounting.exchange_rates (
        id uuid NOT NULL,
        from_currency_id uuid NOT NULL,
        to_currency_id uuid NOT NULL,
        effective_date date NOT NULL,
        rate numeric(18,8) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_exchange_rates PRIMARY KEY (id),
        CONSTRAINT ak_exchange_rates_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_exchange_rates_distintas CHECK (from_currency_id <> to_currency_id),
        CONSTRAINT ck_exchange_rates_tasa CHECK (rate > 0),
        CONSTRAINT fk_exchange_rates_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_exchange_rates_tenant_id_from_currency_id FOREIGN KEY (tenant_id, from_currency_id) REFERENCES accounting.currencies (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_exchange_rates_tenant_id_to_currency_id FOREIGN KEY (tenant_id, to_currency_id) REFERENCES accounting.currencies (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE sales.price_lists (
        id uuid NOT NULL,
        name character varying(80) NOT NULL,
        currency_id uuid NOT NULL,
        valid_from date NOT NULL,
        valid_to date,
        is_default boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_price_lists PRIMARY KEY (id),
        CONSTRAINT ak_price_lists_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_price_lists_vigencia CHECK (valid_to IS NULL OR valid_to >= valid_from),
        CONSTRAINT fk_price_lists_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_price_lists_tenant_id_currency_id FOREIGN KEY (tenant_id, currency_id) REFERENCES accounting.currencies (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE iam.role_permissions (
        role_id uuid NOT NULL,
        permission_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_role_permissions PRIMARY KEY (role_id, permission_id),
        CONSTRAINT fk_role_permissions_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_role_permissions_tenant_id_permission_id FOREIGN KEY (tenant_id, permission_id) REFERENCES iam.permissions (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_role_permissions_tenant_id_role_id FOREIGN KEY (tenant_id, role_id) REFERENCES iam.roles (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE purchasing.purchase_returns (
        id uuid NOT NULL,
        number character varying(30) NOT NULL,
        supplier_id uuid NOT NULL,
        return_date date NOT NULL,
        reason character varying(200) NOT NULL,
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_purchase_returns PRIMARY KEY (id),
        CONSTRAINT ak_purchase_returns_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_purchase_returns_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_purchase_returns_tenant_id_supplier_id FOREIGN KEY (tenant_id, supplier_id) REFERENCES purchasing.suppliers (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE purchasing.supplier_contacts (
        id uuid NOT NULL,
        supplier_id uuid NOT NULL,
        full_name character varying(120) NOT NULL,
        phone character varying(40),
        email character varying(254),
        is_primary boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_supplier_contacts PRIMARY KEY (id),
        CONSTRAINT ak_supplier_contacts_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_supplier_contacts_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_supplier_contacts_tenant_id_supplier_id FOREIGN KEY (tenant_id, supplier_id) REFERENCES purchasing.suppliers (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE purchasing.supplier_invoices (
        id uuid NOT NULL,
        supplier_id uuid NOT NULL,
        number character varying(40) NOT NULL,
        invoice_date date NOT NULL,
        due_date date,
        currency_id uuid NOT NULL,
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_supplier_invoices PRIMARY KEY (id),
        CONSTRAINT ak_supplier_invoices_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_supplier_invoices_vencimiento CHECK (due_date IS NULL OR due_date >= invoice_date),
        CONSTRAINT fk_supplier_invoices_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_supplier_invoices_tenant_id_currency_id FOREIGN KEY (tenant_id, currency_id) REFERENCES accounting.currencies (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_supplier_invoices_tenant_id_supplier_id FOREIGN KEY (tenant_id, supplier_id) REFERENCES purchasing.suppliers (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE accounting.tax_rates (
        id uuid NOT NULL,
        tax_id uuid NOT NULL,
        rate numeric(9,4) NOT NULL,
        valid_from date NOT NULL,
        valid_to date,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_tax_rates PRIMARY KEY (id),
        CONSTRAINT ak_tax_rates_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_tax_rates_tasa CHECK (rate >= 0 AND rate <= 100),
        CONSTRAINT ck_tax_rates_vigencia CHECK (valid_to IS NULL OR valid_to >= valid_from),
        CONSTRAINT fk_tax_rates_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_tax_rates_tenant_id_tax_id FOREIGN KEY (tenant_id, tax_id) REFERENCES catalog.taxes (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE catalog.unit_conversions (
        id uuid NOT NULL,
        from_unit_id uuid NOT NULL,
        to_unit_id uuid NOT NULL,
        factor numeric(18,8) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_unit_conversions PRIMARY KEY (id),
        CONSTRAINT ak_unit_conversions_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_unit_conversions_distintas CHECK (from_unit_id <> to_unit_id),
        CONSTRAINT ck_unit_conversions_factor CHECK (factor > 0),
        CONSTRAINT fk_unit_conversions_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_unit_conversions_tenant_id_from_unit_id FOREIGN KEY (tenant_id, from_unit_id) REFERENCES catalog.units_of_measure (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_unit_conversions_tenant_id_to_unit_id FOREIGN KEY (tenant_id, to_unit_id) REFERENCES catalog.units_of_measure (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE iam.audit_logs (
        id uuid NOT NULL,
        user_id uuid,
        occurred_at timestamp with time zone NOT NULL,
        action character varying(100) NOT NULL,
        outcome character varying(20) NOT NULL,
        entity_type character varying(100),
        entity_id uuid,
        details jsonb,
        correlation_id uuid NOT NULL,
        legacy_reference character varying(40),
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_audit_logs PRIMARY KEY (id),
        CONSTRAINT ak_audit_logs_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_audit_logs_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_audit_logs_tenant_id_user_id FOREIGN KEY (tenant_id, user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE accounting.journal_entries (
        id uuid NOT NULL,
        number character varying(30) NOT NULL,
        fiscal_period_id uuid NOT NULL,
        entry_date date NOT NULL,
        description character varying(250) NOT NULL,
        currency_id uuid NOT NULL,
        status character varying(20) NOT NULL,
        posted_at timestamp with time zone,
        posted_by_user_id uuid,
        source_correlation_id uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_journal_entries PRIMARY KEY (id),
        CONSTRAINT ak_journal_entries_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_journal_entries_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_journal_entries_tenant_id_currency_id FOREIGN KEY (tenant_id, currency_id) REFERENCES accounting.currencies (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_journal_entries_tenant_id_fiscal_period_id FOREIGN KEY (tenant_id, fiscal_period_id) REFERENCES accounting.fiscal_periods (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_journal_entries_tenant_id_posted_by_user_id FOREIGN KEY (tenant_id, posted_by_user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE iam.user_credentials (
        user_id uuid NOT NULL,
        password_hash character varying(512) NOT NULL,
        algorithm character varying(30) NOT NULL,
        iterations integer NOT NULL,
        changed_at timestamp with time zone NOT NULL,
        must_change_password boolean NOT NULL,
        failed_attempts integer NOT NULL,
        locked_until timestamp with time zone,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_user_credentials PRIMARY KEY (user_id),
        CONSTRAINT ck_user_credentials_intentos CHECK (failed_attempts >= 0),
        CONSTRAINT ck_user_credentials_iteraciones CHECK (iterations >= 100000),
        CONSTRAINT fk_user_credentials_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_user_credentials_tenant_id_user_id FOREIGN KEY (tenant_id, user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE iam.user_roles (
        user_id uuid NOT NULL,
        role_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_user_roles PRIMARY KEY (user_id, role_id),
        CONSTRAINT fk_user_roles_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_user_roles_tenant_id_role_id FOREIGN KEY (tenant_id, role_id) REFERENCES iam.roles (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_user_roles_tenant_id_user_id FOREIGN KEY (tenant_id, user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE catalog.products (
        id uuid NOT NULL,
        code character varying(40) NOT NULL,
        name character varying(150) NOT NULL,
        description character varying(1000),
        category_id uuid NOT NULL,
        model_id uuid,
        base_unit_id uuid NOT NULL,
        tracking_mode character varying(20) NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_products PRIMARY KEY (id),
        CONSTRAINT ak_products_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_products_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_products_tenant_id_base_unit_id FOREIGN KEY (tenant_id, base_unit_id) REFERENCES catalog.units_of_measure (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_products_tenant_id_category_id FOREIGN KEY (tenant_id, category_id) REFERENCES catalog.categories (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_products_tenant_id_model_id FOREIGN KEY (tenant_id, model_id) REFERENCES catalog.models (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE sales.cities (
        id uuid NOT NULL,
        state_id uuid NOT NULL,
        name character varying(80) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_cities PRIMARY KEY (id),
        CONSTRAINT ak_cities_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_cities_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_cities_tenant_id_state_id FOREIGN KEY (tenant_id, state_id) REFERENCES sales.states (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE sales.customer_categories (
        id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name character varying(80) NOT NULL,
        default_price_list_id uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_customer_categories PRIMARY KEY (id),
        CONSTRAINT ak_customer_categories_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_customer_categories_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_customer_categories_tenant_id_default_price_list_id FOREIGN KEY (tenant_id, default_price_list_id) REFERENCES sales.price_lists (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE catalog.product_suppliers (
        product_id uuid NOT NULL,
        supplier_id uuid NOT NULL,
        supplier_sku character varying(60),
        lead_time_days integer,
        is_preferred boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_product_suppliers PRIMARY KEY (product_id, supplier_id),
        CONSTRAINT ck_product_suppliers_dias CHECK (lead_time_days IS NULL OR lead_time_days >= 0),
        CONSTRAINT fk_product_suppliers_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_product_suppliers_tenant_id_product_id FOREIGN KEY (tenant_id, product_id) REFERENCES catalog.products (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_product_suppliers_tenant_id_supplier_id FOREIGN KEY (tenant_id, supplier_id) REFERENCES purchasing.suppliers (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE catalog.product_taxes (
        product_id uuid NOT NULL,
        tax_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_product_taxes PRIMARY KEY (product_id, tax_id),
        CONSTRAINT fk_product_taxes_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_product_taxes_tenant_id_product_id FOREIGN KEY (tenant_id, product_id) REFERENCES catalog.products (tenant_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_product_taxes_tenant_id_tax_id FOREIGN KEY (tenant_id, tax_id) REFERENCES catalog.taxes (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE catalog.product_unit_conversions (
        id uuid NOT NULL,
        product_id uuid NOT NULL,
        unit_id uuid NOT NULL,
        factor_to_base numeric(18,8) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_product_unit_conversions PRIMARY KEY (id),
        CONSTRAINT ak_product_unit_conversions_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_product_unit_conversions_factor CHECK (factor_to_base > 0),
        CONSTRAINT fk_product_unit_conversions_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_product_unit_conversions_tenant_id_product_id FOREIGN KEY (tenant_id, product_id) REFERENCES catalog.products (tenant_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_product_unit_conversions_tenant_id_unit_id FOREIGN KEY (tenant_id, unit_id) REFERENCES catalog.units_of_measure (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE catalog.product_variants (
        id uuid NOT NULL,
        product_id uuid NOT NULL,
        sku character varying(40) NOT NULL,
        name character varying(150),
        is_default boolean NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_product_variants PRIMARY KEY (id),
        CONSTRAINT ak_product_variants_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_product_variants_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_product_variants_tenant_id_product_id FOREIGN KEY (tenant_id, product_id) REFERENCES catalog.products (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE sales.postal_codes (
        id uuid NOT NULL,
        city_id uuid NOT NULL,
        code character varying(12) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_postal_codes PRIMARY KEY (id),
        CONSTRAINT ak_postal_codes_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_postal_codes_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_postal_codes_tenant_id_city_id FOREIGN KEY (tenant_id, city_id) REFERENCES sales.cities (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE sales.customers (
        id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name character varying(150) NOT NULL,
        tax_id character varying(30),
        email character varying(254),
        phone character varying(40),
        customer_category_id uuid NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_customers PRIMARY KEY (id),
        CONSTRAINT ak_customers_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_customers_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_customers_tenant_id_customer_category_id FOREIGN KEY (tenant_id, customer_category_id) REFERENCES sales.customer_categories (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE inventory.batches (
        id uuid NOT NULL,
        variant_id uuid NOT NULL,
        lot_number character varying(40) NOT NULL,
        manufactured_on date,
        expires_on date,
        is_default boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_batches PRIMARY KEY (id),
        CONSTRAINT ak_batches_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_batches_caducidad CHECK (expires_on IS NULL OR manufactured_on IS NULL OR expires_on >= manufactured_on),
        CONSTRAINT fk_batches_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_batches_tenant_id_variant_id FOREIGN KEY (tenant_id, variant_id) REFERENCES catalog.product_variants (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE sales.price_list_items (
        price_list_id uuid NOT NULL,
        variant_id uuid NOT NULL,
        unit_price numeric(19,4) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_price_list_items PRIMARY KEY (price_list_id, variant_id),
        CONSTRAINT ck_price_list_items_precio CHECK (unit_price >= 0),
        CONSTRAINT fk_price_list_items_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_price_list_items_tenant_id_price_list_id FOREIGN KEY (tenant_id, price_list_id) REFERENCES sales.price_lists (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_price_list_items_tenant_id_variant_id FOREIGN KEY (tenant_id, variant_id) REFERENCES catalog.product_variants (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE catalog.product_barcodes (
        id uuid NOT NULL,
        variant_id uuid NOT NULL,
        barcode_type_id uuid NOT NULL,
        code character varying(64) NOT NULL,
        unit_id uuid,
        is_primary boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_product_barcodes PRIMARY KEY (id),
        CONSTRAINT ak_product_barcodes_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_product_barcodes_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_product_barcodes_tenant_id_barcode_type_id FOREIGN KEY (tenant_id, barcode_type_id) REFERENCES catalog.barcode_types (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_product_barcodes_tenant_id_unit_id FOREIGN KEY (tenant_id, unit_id) REFERENCES catalog.units_of_measure (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_product_barcodes_tenant_id_variant_id FOREIGN KEY (tenant_id, variant_id) REFERENCES catalog.product_variants (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE catalog.product_variant_attributes (
        variant_id uuid NOT NULL,
        attribute_value_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_product_variant_attributes PRIMARY KEY (variant_id, attribute_value_id),
        CONSTRAINT fk_product_variant_attributes_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_product_variant_attributes_tenant_id_attribute_value_id FOREIGN KEY (tenant_id, attribute_value_id) REFERENCES catalog.attribute_values (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_product_variant_attributes_tenant_id_variant_id FOREIGN KEY (tenant_id, variant_id) REFERENCES catalog.product_variants (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE sales.addresses (
        id uuid NOT NULL,
        postal_code_id uuid NOT NULL,
        street character varying(200) NOT NULL,
        reference character varying(200),
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_addresses PRIMARY KEY (id),
        CONSTRAINT ak_addresses_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_addresses_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_addresses_tenant_id_postal_code_id FOREIGN KEY (tenant_id, postal_code_id) REFERENCES sales.postal_codes (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE warehouse.branches (
        id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name character varying(100) NOT NULL,
        address_id uuid,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_branches PRIMARY KEY (id),
        CONSTRAINT ak_branches_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_branches_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_branches_tenant_id_address_id FOREIGN KEY (tenant_id, address_id) REFERENCES sales.addresses (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE sales.customer_addresses (
        customer_id uuid NOT NULL,
        address_id uuid NOT NULL,
        address_type character varying(20) NOT NULL,
        is_default boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_customer_addresses PRIMARY KEY (customer_id, address_id),
        CONSTRAINT fk_customer_addresses_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_customer_addresses_tenant_id_address_id FOREIGN KEY (tenant_id, address_id) REFERENCES sales.addresses (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_customer_addresses_tenant_id_customer_id FOREIGN KEY (tenant_id, customer_id) REFERENCES sales.customers (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE purchasing.supplier_addresses (
        supplier_id uuid NOT NULL,
        address_id uuid NOT NULL,
        address_type character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_supplier_addresses PRIMARY KEY (supplier_id, address_id),
        CONSTRAINT fk_supplier_addresses_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_supplier_addresses_tenant_id_address_id FOREIGN KEY (tenant_id, address_id) REFERENCES sales.addresses (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_supplier_addresses_tenant_id_supplier_id FOREIGN KEY (tenant_id, supplier_id) REFERENCES purchasing.suppliers (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE warehouse.branch_users (
        branch_id uuid NOT NULL,
        user_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_branch_users PRIMARY KEY (branch_id, user_id),
        CONSTRAINT fk_branch_users_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_branch_users_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES warehouse.branches (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_branch_users_tenant_id_user_id FOREIGN KEY (tenant_id, user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE accounting.cost_centers (
        id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name character varying(80) NOT NULL,
        branch_id uuid,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_cost_centers PRIMARY KEY (id),
        CONSTRAINT ak_cost_centers_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_cost_centers_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_cost_centers_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES warehouse.branches (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE iam.hardware_tokens (
        id uuid NOT NULL,
        branch_id uuid,
        name character varying(100) NOT NULL,
        kind character varying(20) NOT NULL,
        fingerprint character varying(128) NOT NULL,
        registered_at timestamp with time zone NOT NULL,
        revoked_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_hardware_tokens PRIMARY KEY (id),
        CONSTRAINT ak_hardware_tokens_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_hardware_tokens_revocacion CHECK (revoked_at IS NULL OR revoked_at >= registered_at),
        CONSTRAINT fk_hardware_tokens_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_hardware_tokens_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES warehouse.branches (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE accounting.tax_rules (
        id uuid NOT NULL,
        tax_id uuid NOT NULL,
        customer_category_id uuid,
        branch_id uuid,
        is_exempt boolean NOT NULL,
        priority integer NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_tax_rules PRIMARY KEY (id),
        CONSTRAINT ak_tax_rules_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_tax_rules_prioridad CHECK (priority >= 0),
        CONSTRAINT fk_tax_rules_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_tax_rules_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES warehouse.branches (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_tax_rules_tenant_id_customer_category_id FOREIGN KEY (tenant_id, customer_category_id) REFERENCES sales.customer_categories (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_tax_rules_tenant_id_tax_id FOREIGN KEY (tenant_id, tax_id) REFERENCES catalog.taxes (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE warehouse.warehouses (
        id uuid NOT NULL,
        branch_id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name character varying(100) NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_warehouses PRIMARY KEY (id),
        CONSTRAINT ak_warehouses_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_warehouses_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_warehouses_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES warehouse.branches (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE accounting.journal_lines (
        id uuid NOT NULL,
        journal_entry_id uuid NOT NULL,
        account_id uuid NOT NULL,
        cost_center_id uuid,
        debit numeric(19,4) NOT NULL,
        credit numeric(19,4) NOT NULL,
        memo character varying(200),
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_journal_lines PRIMARY KEY (id),
        CONSTRAINT ak_journal_lines_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_journal_lines_partida CHECK ((debit > 0 AND credit = 0) OR (credit > 0 AND debit = 0)),
        CONSTRAINT fk_journal_lines_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_journal_lines_tenant_id_account_id FOREIGN KEY (tenant_id, account_id) REFERENCES accounting.accounts (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_journal_lines_tenant_id_cost_center_id FOREIGN KEY (tenant_id, cost_center_id) REFERENCES accounting.cost_centers (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_journal_lines_tenant_id_journal_entry_id FOREIGN KEY (tenant_id, journal_entry_id) REFERENCES accounting.journal_entries (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE iam.access_logs (
        id uuid NOT NULL,
        user_id uuid,
        attempted_email character varying(254) NOT NULL,
        succeeded boolean NOT NULL,
        failure_reason character varying(200),
        occurred_at timestamp with time zone NOT NULL,
        machine_name character varying(100) NOT NULL,
        hardware_token_id uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_access_logs PRIMARY KEY (id),
        CONSTRAINT ak_access_logs_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_access_logs_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_access_logs_tenant_id_hardware_token_id FOREIGN KEY (tenant_id, hardware_token_id) REFERENCES iam.hardware_tokens (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_access_logs_tenant_id_user_id FOREIGN KEY (tenant_id, user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE iam.sessions (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        hardware_token_id uuid,
        started_at timestamp with time zone NOT NULL,
        last_seen_at timestamp with time zone NOT NULL,
        ended_at timestamp with time zone,
        machine_name character varying(100) NOT NULL,
        client_version character varying(30) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_sessions PRIMARY KEY (id),
        CONSTRAINT ak_sessions_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_sessions_fechas CHECK (ended_at IS NULL OR ended_at >= started_at),
        CONSTRAINT fk_sessions_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_sessions_tenant_id_hardware_token_id FOREIGN KEY (tenant_id, hardware_token_id) REFERENCES iam.hardware_tokens (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_sessions_tenant_id_user_id FOREIGN KEY (tenant_id, user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE inventory.physical_counts (
        id uuid NOT NULL,
        number character varying(30) NOT NULL,
        warehouse_id uuid NOT NULL,
        count_date date NOT NULL,
        status character varying(20) NOT NULL,
        posted_at timestamp with time zone,
        posted_by_user_id uuid,
        notes character varying(250),
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_physical_counts PRIMARY KEY (id),
        CONSTRAINT ak_physical_counts_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_physical_counts_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_physical_counts_tenant_id_posted_by_user_id FOREIGN KEY (tenant_id, posted_by_user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_physical_counts_tenant_id_warehouse_id FOREIGN KEY (tenant_id, warehouse_id) REFERENCES warehouse.warehouses (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE sales.pos_registers (
        id uuid NOT NULL,
        warehouse_id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name character varying(80) NOT NULL,
        hardware_token_id uuid,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_pos_registers PRIMARY KEY (id),
        CONSTRAINT ak_pos_registers_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_pos_registers_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_pos_registers_tenant_id_hardware_token_id FOREIGN KEY (tenant_id, hardware_token_id) REFERENCES iam.hardware_tokens (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_pos_registers_tenant_id_warehouse_id FOREIGN KEY (tenant_id, warehouse_id) REFERENCES warehouse.warehouses (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE catalog.product_stock_policies (
        id uuid NOT NULL,
        variant_id uuid NOT NULL,
        warehouse_id uuid NOT NULL,
        min_quantity numeric(18,6) NOT NULL,
        max_quantity numeric(18,6) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_product_stock_policies PRIMARY KEY (id),
        CONSTRAINT ak_product_stock_policies_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_product_stock_policies_maximo CHECK (max_quantity >= 0 AND (max_quantity = 0 OR max_quantity >= min_quantity)),
        CONSTRAINT ck_product_stock_policies_minimo CHECK (min_quantity >= 0),
        CONSTRAINT fk_product_stock_policies_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_product_stock_policies_tenant_id_variant_id FOREIGN KEY (tenant_id, variant_id) REFERENCES catalog.product_variants (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_product_stock_policies_tenant_id_warehouse_id FOREIGN KEY (tenant_id, warehouse_id) REFERENCES warehouse.warehouses (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE purchasing.purchase_orders (
        id uuid NOT NULL,
        number character varying(30) NOT NULL,
        supplier_id uuid NOT NULL,
        warehouse_id uuid NOT NULL,
        currency_id uuid NOT NULL,
        order_date date NOT NULL,
        expected_date date,
        status character varying(20) NOT NULL,
        notes character varying(250),
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_purchase_orders PRIMARY KEY (id),
        CONSTRAINT ak_purchase_orders_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_purchase_orders_fechas CHECK (expected_date IS NULL OR expected_date >= order_date),
        CONSTRAINT fk_purchase_orders_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_purchase_orders_tenant_id_currency_id FOREIGN KEY (tenant_id, currency_id) REFERENCES accounting.currencies (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_purchase_orders_tenant_id_supplier_id FOREIGN KEY (tenant_id, supplier_id) REFERENCES purchasing.suppliers (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_purchase_orders_tenant_id_warehouse_id FOREIGN KEY (tenant_id, warehouse_id) REFERENCES warehouse.warehouses (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE inventory.stock_adjustments (
        id uuid NOT NULL,
        number character varying(30) NOT NULL,
        warehouse_id uuid NOT NULL,
        adjustment_reason_id uuid NOT NULL,
        status character varying(20) NOT NULL,
        notes character varying(250),
        posted_at timestamp with time zone,
        posted_by_user_id uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_stock_adjustments PRIMARY KEY (id),
        CONSTRAINT ak_stock_adjustments_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_stock_adjustments_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_adjustments_tenant_id_adjustment_reason_id FOREIGN KEY (tenant_id, adjustment_reason_id) REFERENCES inventory.adjustment_reasons (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_adjustments_tenant_id_posted_by_user_id FOREIGN KEY (tenant_id, posted_by_user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_adjustments_tenant_id_warehouse_id FOREIGN KEY (tenant_id, warehouse_id) REFERENCES warehouse.warehouses (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE inventory.stock_transfers (
        id uuid NOT NULL,
        number character varying(30) NOT NULL,
        from_warehouse_id uuid NOT NULL,
        to_warehouse_id uuid NOT NULL,
        status character varying(20) NOT NULL,
        shipped_at timestamp with time zone,
        received_at timestamp with time zone,
        notes character varying(250),
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_stock_transfers PRIMARY KEY (id),
        CONSTRAINT ak_stock_transfers_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_stock_transfers_almacenes CHECK (from_warehouse_id <> to_warehouse_id),
        CONSTRAINT fk_stock_transfers_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_transfers_tenant_id_from_warehouse_id FOREIGN KEY (tenant_id, from_warehouse_id) REFERENCES warehouse.warehouses (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_transfers_tenant_id_to_warehouse_id FOREIGN KEY (tenant_id, to_warehouse_id) REFERENCES warehouse.warehouses (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE iam.tenant_configs (
        tenant_id uuid NOT NULL,
        default_currency_id uuid NOT NULL,
        default_warehouse_id uuid,
        alert_margin numeric(5,4) NOT NULL,
        days_without_rotation integer NOT NULL,
        min_business_date date NOT NULL,
        time_zone_id character varying(64) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        CONSTRAINT pk_tenant_configs PRIMARY KEY (tenant_id),
        CONSTRAINT ck_tenant_configs_margen CHECK (alert_margin >= 0 AND alert_margin <= 1),
        CONSTRAINT ck_tenant_configs_rotacion CHECK (days_without_rotation > 0),
        CONSTRAINT fk_tenant_configs_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_tenant_configs_tenant_id_default_currency_id FOREIGN KEY (tenant_id, default_currency_id) REFERENCES accounting.currencies (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_tenant_configs_tenant_id_default_warehouse_id FOREIGN KEY (tenant_id, default_warehouse_id) REFERENCES warehouse.warehouses (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE warehouse.zones (
        id uuid NOT NULL,
        warehouse_id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name character varying(80) NOT NULL,
        location_type_id uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_zones PRIMARY KEY (id),
        CONSTRAINT ak_zones_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_zones_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_zones_tenant_id_location_type_id FOREIGN KEY (tenant_id, location_type_id) REFERENCES warehouse.location_types (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_zones_tenant_id_warehouse_id FOREIGN KEY (tenant_id, warehouse_id) REFERENCES warehouse.warehouses (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE sales.pos_sessions (
        id uuid NOT NULL,
        pos_register_id uuid NOT NULL,
        opened_by_user_id uuid NOT NULL,
        opened_at timestamp with time zone NOT NULL,
        opening_cash numeric(19,4) NOT NULL,
        closed_by_user_id uuid,
        closed_at timestamp with time zone,
        closing_cash_counted numeric(19,4),
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_pos_sessions PRIMARY KEY (id),
        CONSTRAINT ak_pos_sessions_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_pos_sessions_cierre CHECK ((status = 'Closed') = (closed_at IS NOT NULL)),
        CONSTRAINT ck_pos_sessions_fechas CHECK (closed_at IS NULL OR closed_at >= opened_at),
        CONSTRAINT ck_pos_sessions_fondo CHECK (opening_cash >= 0),
        CONSTRAINT fk_pos_sessions_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_pos_sessions_tenant_id_closed_by_user_id FOREIGN KEY (tenant_id, closed_by_user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_pos_sessions_tenant_id_opened_by_user_id FOREIGN KEY (tenant_id, opened_by_user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_pos_sessions_tenant_id_pos_register_id FOREIGN KEY (tenant_id, pos_register_id) REFERENCES sales.pos_registers (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE purchasing.goods_receipts (
        id uuid NOT NULL,
        number character varying(30) NOT NULL,
        purchase_order_id uuid,
        supplier_id uuid,
        warehouse_id uuid NOT NULL,
        received_at timestamp with time zone NOT NULL,
        received_by_user_id uuid NOT NULL,
        supplier_document character varying(40),
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_goods_receipts PRIMARY KEY (id),
        CONSTRAINT ak_goods_receipts_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_goods_receipts_origen CHECK (num_nonnulls(purchase_order_id, supplier_id) = 1),
        CONSTRAINT fk_goods_receipts_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_goods_receipts_tenant_id_purchase_order_id FOREIGN KEY (tenant_id, purchase_order_id) REFERENCES purchasing.purchase_orders (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_goods_receipts_tenant_id_received_by_user_id FOREIGN KEY (tenant_id, received_by_user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_goods_receipts_tenant_id_supplier_id FOREIGN KEY (tenant_id, supplier_id) REFERENCES purchasing.suppliers (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_goods_receipts_tenant_id_warehouse_id FOREIGN KEY (tenant_id, warehouse_id) REFERENCES warehouse.warehouses (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE purchasing.purchase_order_lines (
        id uuid NOT NULL,
        purchase_order_id uuid NOT NULL,
        variant_id uuid NOT NULL,
        unit_id uuid NOT NULL,
        quantity numeric(18,6) NOT NULL,
        unit_cost numeric(19,4) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_purchase_order_lines PRIMARY KEY (id),
        CONSTRAINT ak_purchase_order_lines_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_purchase_order_lines_cantidad CHECK (quantity > 0),
        CONSTRAINT ck_purchase_order_lines_costo CHECK (unit_cost >= 0),
        CONSTRAINT fk_purchase_order_lines_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_purchase_order_lines_tenant_id_purchase_order_id FOREIGN KEY (tenant_id, purchase_order_id) REFERENCES purchasing.purchase_orders (tenant_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_purchase_order_lines_tenant_id_unit_id FOREIGN KEY (tenant_id, unit_id) REFERENCES catalog.units_of_measure (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_purchase_order_lines_tenant_id_variant_id FOREIGN KEY (tenant_id, variant_id) REFERENCES catalog.product_variants (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE warehouse.aisles (
        id uuid NOT NULL,
        zone_id uuid NOT NULL,
        code character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_aisles PRIMARY KEY (id),
        CONSTRAINT ak_aisles_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_aisles_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_aisles_tenant_id_zone_id FOREIGN KEY (tenant_id, zone_id) REFERENCES warehouse.zones (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE sales.cash_movements (
        id uuid NOT NULL,
        pos_session_id uuid NOT NULL,
        direction character varying(20) NOT NULL,
        amount numeric(19,4) NOT NULL,
        reason character varying(200) NOT NULL,
        occurred_at timestamp with time zone NOT NULL,
        recorded_by_user_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_cash_movements PRIMARY KEY (id),
        CONSTRAINT ak_cash_movements_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_cash_movements_monto CHECK (amount > 0),
        CONSTRAINT fk_cash_movements_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_cash_movements_tenant_id_pos_session_id FOREIGN KEY (tenant_id, pos_session_id) REFERENCES sales.pos_sessions (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_cash_movements_tenant_id_recorded_by_user_id FOREIGN KEY (tenant_id, recorded_by_user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE sales.sales_orders (
        id uuid NOT NULL,
        number character varying(30) NOT NULL,
        customer_id uuid NOT NULL,
        pos_session_id uuid,
        warehouse_id uuid,
        price_list_id uuid NOT NULL,
        order_date date NOT NULL,
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_sales_orders PRIMARY KEY (id),
        CONSTRAINT ak_sales_orders_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_sales_orders_origen CHECK (num_nonnulls(pos_session_id, warehouse_id) = 1),
        CONSTRAINT fk_sales_orders_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_sales_orders_tenant_id_customer_id FOREIGN KEY (tenant_id, customer_id) REFERENCES sales.customers (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_sales_orders_tenant_id_pos_session_id FOREIGN KEY (tenant_id, pos_session_id) REFERENCES sales.pos_sessions (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_sales_orders_tenant_id_price_list_id FOREIGN KEY (tenant_id, price_list_id) REFERENCES sales.price_lists (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_sales_orders_tenant_id_warehouse_id FOREIGN KEY (tenant_id, warehouse_id) REFERENCES warehouse.warehouses (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE warehouse.racks (
        id uuid NOT NULL,
        aisle_id uuid NOT NULL,
        code character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_racks PRIMARY KEY (id),
        CONSTRAINT ak_racks_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_racks_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_racks_tenant_id_aisle_id FOREIGN KEY (tenant_id, aisle_id) REFERENCES warehouse.aisles (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE sales.invoices (
        id uuid NOT NULL,
        number character varying(40) NOT NULL,
        sales_order_id uuid NOT NULL,
        status character varying(20) NOT NULL,
        issued_at timestamp with time zone,
        fiscal_authorization_code character varying(100),
        voided_at timestamp with time zone,
        void_reason character varying(200),
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_invoices PRIMARY KEY (id),
        CONSTRAINT ak_invoices_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_invoices_anulacion CHECK ((status = 'Voided') = (voided_at IS NOT NULL)),
        CONSTRAINT ck_invoices_emision CHECK ((status = 'Draft') = (issued_at IS NULL)),
        CONSTRAINT fk_invoices_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_invoices_tenant_id_sales_order_id FOREIGN KEY (tenant_id, sales_order_id) REFERENCES sales.sales_orders (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE warehouse.shelves (
        id uuid NOT NULL,
        rack_id uuid NOT NULL,
        code character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_shelves PRIMARY KEY (id),
        CONSTRAINT ak_shelves_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_shelves_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_shelves_tenant_id_rack_id FOREIGN KEY (tenant_id, rack_id) REFERENCES warehouse.racks (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE sales.payments (
        id uuid NOT NULL,
        invoice_id uuid NOT NULL,
        payment_method_id uuid NOT NULL,
        amount numeric(19,4) NOT NULL,
        reference character varying(60),
        paid_at timestamp with time zone NOT NULL,
        pos_session_id uuid,
        recorded_by_user_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_payments PRIMARY KEY (id),
        CONSTRAINT ak_payments_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_payments_monto CHECK (amount > 0),
        CONSTRAINT fk_payments_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_payments_tenant_id_invoice_id FOREIGN KEY (tenant_id, invoice_id) REFERENCES sales.invoices (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_payments_tenant_id_payment_method_id FOREIGN KEY (tenant_id, payment_method_id) REFERENCES sales.payment_methods (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_payments_tenant_id_pos_session_id FOREIGN KEY (tenant_id, pos_session_id) REFERENCES sales.pos_sessions (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_payments_tenant_id_recorded_by_user_id FOREIGN KEY (tenant_id, recorded_by_user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE warehouse.bins (
        id uuid NOT NULL,
        shelf_id uuid NOT NULL,
        code character varying(40) NOT NULL,
        location_type_id uuid NOT NULL,
        pick_sequence integer NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_bins PRIMARY KEY (id),
        CONSTRAINT ak_bins_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_bins_secuencia CHECK (pick_sequence >= 0),
        CONSTRAINT fk_bins_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_bins_tenant_id_location_type_id FOREIGN KEY (tenant_id, location_type_id) REFERENCES warehouse.location_types (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_bins_tenant_id_shelf_id FOREIGN KEY (tenant_id, shelf_id) REFERENCES warehouse.shelves (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE warehouse.bin_assignments (
        bin_id uuid NOT NULL,
        variant_id uuid NOT NULL,
        is_primary_pick boolean NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_bin_assignments PRIMARY KEY (bin_id, variant_id),
        CONSTRAINT fk_bin_assignments_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_bin_assignments_tenant_id_bin_id FOREIGN KEY (tenant_id, bin_id) REFERENCES warehouse.bins (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_bin_assignments_tenant_id_variant_id FOREIGN KEY (tenant_id, variant_id) REFERENCES catalog.product_variants (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE inventory.stock_levels (
        id uuid NOT NULL,
        bin_id uuid NOT NULL,
        batch_id uuid NOT NULL,
        quantity_on_hand numeric(18,6) NOT NULL,
        quantity_reserved numeric(18,6) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_stock_levels PRIMARY KEY (id),
        CONSTRAINT ak_stock_levels_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_stock_levels_existencia CHECK (quantity_on_hand >= 0),
        CONSTRAINT ck_stock_levels_reserva CHECK (quantity_reserved >= 0 AND quantity_reserved <= quantity_on_hand),
        CONSTRAINT fk_stock_levels_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_levels_tenant_id_batch_id FOREIGN KEY (tenant_id, batch_id) REFERENCES inventory.batches (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_levels_tenant_id_bin_id FOREIGN KEY (tenant_id, bin_id) REFERENCES warehouse.bins (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE inventory.serial_numbers (
        id uuid NOT NULL,
        batch_id uuid NOT NULL,
        serial character varying(80) NOT NULL,
        stock_level_id uuid,
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_serial_numbers PRIMARY KEY (id),
        CONSTRAINT ak_serial_numbers_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_serial_numbers_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_serial_numbers_tenant_id_batch_id FOREIGN KEY (tenant_id, batch_id) REFERENCES inventory.batches (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_serial_numbers_tenant_id_stock_level_id FOREIGN KEY (tenant_id, stock_level_id) REFERENCES inventory.stock_levels (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE inventory.stock_movements (
        id uuid NOT NULL,
        stock_level_id uuid NOT NULL,
        movement_type_id uuid NOT NULL,
        quantity numeric(18,6) NOT NULL,
        business_date date NOT NULL,
        recorded_at timestamp with time zone NOT NULL,
        recorded_by_user_id uuid NOT NULL,
        document_reference character varying(30),
        notes character varying(250),
        adjustment_reason_id uuid,
        legacy_reference character varying(40),
        correlation_id uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_stock_movements PRIMARY KEY (id),
        CONSTRAINT ak_stock_movements_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_stock_movements_cantidad CHECK (quantity > 0),
        CONSTRAINT fk_stock_movements_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_movements_tenant_id_adjustment_reason_id FOREIGN KEY (tenant_id, adjustment_reason_id) REFERENCES inventory.adjustment_reasons (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_movements_tenant_id_movement_type_id FOREIGN KEY (tenant_id, movement_type_id) REFERENCES inventory.movement_types (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_movements_tenant_id_recorded_by_user_id FOREIGN KEY (tenant_id, recorded_by_user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_movements_tenant_id_stock_level_id FOREIGN KEY (tenant_id, stock_level_id) REFERENCES inventory.stock_levels (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE accounting.average_cost_history (
        id uuid NOT NULL,
        variant_id uuid NOT NULL,
        warehouse_id uuid NOT NULL,
        effective_at timestamp with time zone NOT NULL,
        average_cost numeric(19,4) NOT NULL,
        stock_movement_id uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_average_cost_history PRIMARY KEY (id),
        CONSTRAINT ak_average_cost_history_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_average_cost_history_costo CHECK (average_cost >= 0),
        CONSTRAINT fk_average_cost_history_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_average_cost_history_tenant_id_stock_movement_id FOREIGN KEY (tenant_id, stock_movement_id) REFERENCES inventory.stock_movements (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_average_cost_history_tenant_id_variant_id FOREIGN KEY (tenant_id, variant_id) REFERENCES catalog.product_variants (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_average_cost_history_tenant_id_warehouse_id FOREIGN KEY (tenant_id, warehouse_id) REFERENCES warehouse.warehouses (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE purchasing.goods_receipt_lines (
        id uuid NOT NULL,
        goods_receipt_id uuid NOT NULL,
        purchase_order_line_id uuid,
        stock_level_id uuid NOT NULL,
        quantity numeric(18,6) NOT NULL,
        unit_cost numeric(19,4) NOT NULL,
        stock_movement_id uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_goods_receipt_lines PRIMARY KEY (id),
        CONSTRAINT ak_goods_receipt_lines_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_goods_receipt_lines_cantidad CHECK (quantity > 0),
        CONSTRAINT ck_goods_receipt_lines_costo CHECK (unit_cost >= 0),
        CONSTRAINT fk_goods_receipt_lines_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_goods_receipt_lines_tenant_id_goods_receipt_id FOREIGN KEY (tenant_id, goods_receipt_id) REFERENCES purchasing.goods_receipts (tenant_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_goods_receipt_lines_tenant_id_purchase_order_line_id FOREIGN KEY (tenant_id, purchase_order_line_id) REFERENCES purchasing.purchase_order_lines (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_goods_receipt_lines_tenant_id_stock_level_id FOREIGN KEY (tenant_id, stock_level_id) REFERENCES inventory.stock_levels (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_goods_receipt_lines_tenant_id_stock_movement_id FOREIGN KEY (tenant_id, stock_movement_id) REFERENCES inventory.stock_movements (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE inventory.physical_count_lines (
        id uuid NOT NULL,
        physical_count_id uuid NOT NULL,
        stock_level_id uuid NOT NULL,
        counted_quantity numeric(18,6) NOT NULL,
        counted_by_user_id uuid NOT NULL,
        counted_at timestamp with time zone NOT NULL,
        system_quantity_at_posting numeric(18,6),
        stock_movement_id uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_physical_count_lines PRIMARY KEY (id),
        CONSTRAINT ak_physical_count_lines_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_physical_count_lines_conteo CHECK (counted_quantity >= 0),
        CONSTRAINT fk_physical_count_lines_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_physical_count_lines_tenant_id_counted_by_user_id FOREIGN KEY (tenant_id, counted_by_user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_physical_count_lines_tenant_id_physical_count_id FOREIGN KEY (tenant_id, physical_count_id) REFERENCES inventory.physical_counts (tenant_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_physical_count_lines_tenant_id_stock_level_id FOREIGN KEY (tenant_id, stock_level_id) REFERENCES inventory.stock_levels (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_physical_count_lines_tenant_id_stock_movement_id FOREIGN KEY (tenant_id, stock_movement_id) REFERENCES inventory.stock_movements (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE sales.sales_order_lines (
        id uuid NOT NULL,
        sales_order_id uuid NOT NULL,
        variant_id uuid NOT NULL,
        unit_id uuid NOT NULL,
        quantity numeric(18,6) NOT NULL,
        unit_price numeric(19,4) NOT NULL,
        discount_percent numeric(9,4) NOT NULL,
        stock_movement_id uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_sales_order_lines PRIMARY KEY (id),
        CONSTRAINT ak_sales_order_lines_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_sales_order_lines_cantidad CHECK (quantity > 0),
        CONSTRAINT ck_sales_order_lines_descuento CHECK (discount_percent >= 0 AND discount_percent <= 100),
        CONSTRAINT ck_sales_order_lines_precio CHECK (unit_price >= 0),
        CONSTRAINT fk_sales_order_lines_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_sales_order_lines_tenant_id_sales_order_id FOREIGN KEY (tenant_id, sales_order_id) REFERENCES sales.sales_orders (tenant_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_sales_order_lines_tenant_id_stock_movement_id FOREIGN KEY (tenant_id, stock_movement_id) REFERENCES inventory.stock_movements (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_sales_order_lines_tenant_id_unit_id FOREIGN KEY (tenant_id, unit_id) REFERENCES catalog.units_of_measure (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_sales_order_lines_tenant_id_variant_id FOREIGN KEY (tenant_id, variant_id) REFERENCES catalog.product_variants (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE inventory.stock_adjustment_lines (
        id uuid NOT NULL,
        stock_adjustment_id uuid NOT NULL,
        stock_level_id uuid NOT NULL,
        movement_type_id uuid NOT NULL,
        quantity numeric(18,6) NOT NULL,
        stock_movement_id uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_stock_adjustment_lines PRIMARY KEY (id),
        CONSTRAINT ak_stock_adjustment_lines_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_stock_adjustment_lines_cantidad CHECK (quantity > 0),
        CONSTRAINT fk_stock_adjustment_lines_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_adjustment_lines_tenant_id_movement_type_id FOREIGN KEY (tenant_id, movement_type_id) REFERENCES inventory.movement_types (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_adjustment_lines_tenant_id_stock_adjustment_id FOREIGN KEY (tenant_id, stock_adjustment_id) REFERENCES inventory.stock_adjustments (tenant_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_stock_adjustment_lines_tenant_id_stock_level_id FOREIGN KEY (tenant_id, stock_level_id) REFERENCES inventory.stock_levels (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_adjustment_lines_tenant_id_stock_movement_id FOREIGN KEY (tenant_id, stock_movement_id) REFERENCES inventory.stock_movements (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE inventory.stock_transfer_lines (
        id uuid NOT NULL,
        stock_transfer_id uuid NOT NULL,
        source_stock_level_id uuid NOT NULL,
        destination_stock_level_id uuid,
        quantity numeric(18,6) NOT NULL,
        outbound_movement_id uuid,
        inbound_movement_id uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_stock_transfer_lines PRIMARY KEY (id),
        CONSTRAINT ak_stock_transfer_lines_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_stock_transfer_lines_cantidad CHECK (quantity > 0),
        CONSTRAINT fk_stock_transfer_lines_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_transfer_lines_tenant_id_destination_stock_level_id FOREIGN KEY (tenant_id, destination_stock_level_id) REFERENCES inventory.stock_levels (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_transfer_lines_tenant_id_inbound_movement_id FOREIGN KEY (tenant_id, inbound_movement_id) REFERENCES inventory.stock_movements (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_transfer_lines_tenant_id_outbound_movement_id FOREIGN KEY (tenant_id, outbound_movement_id) REFERENCES inventory.stock_movements (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_transfer_lines_tenant_id_source_stock_level_id FOREIGN KEY (tenant_id, source_stock_level_id) REFERENCES inventory.stock_levels (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_transfer_lines_tenant_id_stock_transfer_id FOREIGN KEY (tenant_id, stock_transfer_id) REFERENCES inventory.stock_transfers (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE purchasing.purchase_return_lines (
        id uuid NOT NULL,
        purchase_return_id uuid NOT NULL,
        stock_level_id uuid NOT NULL,
        goods_receipt_line_id uuid,
        quantity numeric(18,6) NOT NULL,
        stock_movement_id uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_purchase_return_lines PRIMARY KEY (id),
        CONSTRAINT ak_purchase_return_lines_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_purchase_return_lines_cantidad CHECK (quantity > 0),
        CONSTRAINT fk_purchase_return_lines_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_purchase_return_lines_tenant_id_goods_receipt_line_id FOREIGN KEY (tenant_id, goods_receipt_line_id) REFERENCES purchasing.goods_receipt_lines (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_purchase_return_lines_tenant_id_purchase_return_id FOREIGN KEY (tenant_id, purchase_return_id) REFERENCES purchasing.purchase_returns (tenant_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_purchase_return_lines_tenant_id_stock_level_id FOREIGN KEY (tenant_id, stock_level_id) REFERENCES inventory.stock_levels (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_purchase_return_lines_tenant_id_stock_movement_id FOREIGN KEY (tenant_id, stock_movement_id) REFERENCES inventory.stock_movements (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE purchasing.supplier_invoice_lines (
        id uuid NOT NULL,
        supplier_invoice_id uuid NOT NULL,
        goods_receipt_line_id uuid,
        description character varying(200),
        quantity numeric(18,6) NOT NULL,
        unit_cost numeric(19,4) NOT NULL,
        tax_rate_id uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_supplier_invoice_lines PRIMARY KEY (id),
        CONSTRAINT ak_supplier_invoice_lines_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_supplier_invoice_lines_cantidad CHECK (quantity > 0),
        CONSTRAINT ck_supplier_invoice_lines_costo CHECK (unit_cost >= 0),
        CONSTRAINT ck_supplier_invoice_lines_origen CHECK (num_nonnulls(goods_receipt_line_id, description) = 1),
        CONSTRAINT fk_supplier_invoice_lines_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_supplier_invoice_lines_tenant_id_goods_receipt_line_id FOREIGN KEY (tenant_id, goods_receipt_line_id) REFERENCES purchasing.goods_receipt_lines (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_supplier_invoice_lines_tenant_id_supplier_invoice_id FOREIGN KEY (tenant_id, supplier_invoice_id) REFERENCES purchasing.supplier_invoices (tenant_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_supplier_invoice_lines_tenant_id_tax_rate_id FOREIGN KEY (tenant_id, tax_rate_id) REFERENCES accounting.tax_rates (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE sales.invoice_lines (
        id uuid NOT NULL,
        invoice_id uuid NOT NULL,
        sales_order_line_id uuid NOT NULL,
        tax_rate_id uuid,
        tax_amount numeric(19,4) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_invoice_lines PRIMARY KEY (id),
        CONSTRAINT ak_invoice_lines_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_invoice_lines_impuesto CHECK (tax_amount >= 0),
        CONSTRAINT fk_invoice_lines_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_invoice_lines_tenant_id_invoice_id FOREIGN KEY (tenant_id, invoice_id) REFERENCES sales.invoices (tenant_id, id) ON DELETE CASCADE,
        CONSTRAINT fk_invoice_lines_tenant_id_sales_order_line_id FOREIGN KEY (tenant_id, sales_order_line_id) REFERENCES sales.sales_order_lines (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_invoice_lines_tenant_id_tax_rate_id FOREIGN KEY (tenant_id, tax_rate_id) REFERENCES accounting.tax_rates (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE TABLE inventory.stock_reservations (
        id uuid NOT NULL,
        stock_level_id uuid NOT NULL,
        quantity numeric(18,6) NOT NULL,
        status character varying(20) NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        pos_session_id uuid,
        sales_order_line_id uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_stock_reservations PRIMARY KEY (id),
        CONSTRAINT ak_stock_reservations_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_stock_reservations_cantidad CHECK (quantity > 0),
        CONSTRAINT ck_stock_reservations_origen CHECK (num_nonnulls(pos_session_id, sales_order_line_id) <= 1),
        CONSTRAINT fk_stock_reservations_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_reservations_tenant_id_pos_session_id FOREIGN KEY (tenant_id, pos_session_id) REFERENCES sales.pos_sessions (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_reservations_tenant_id_sales_order_line_id FOREIGN KEY (tenant_id, sales_order_line_id) REFERENCES sales.sales_order_lines (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_reservations_tenant_id_stock_level_id FOREIGN KEY (tenant_id, stock_level_id) REFERENCES inventory.stock_levels (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    INSERT INTO iam.modules (id, code, created_at, created_by, description, monthly_fee_bs, name, setup_price_bs, updated_at, updated_by)
    VALUES ('01920000-0000-7000-8000-000000000001', 'DATA_ENGINE', TIMESTAMPTZ '2026-09-25T00:00:00+00:00', NULL, 'Transición a PostgreSQL local: elimina la corrupción de archivos.', 0.0, 'Migración de motor de datos', 8500.0, NULL, NULL);
    INSERT INTO iam.modules (id, code, created_at, created_by, description, monthly_fee_bs, name, setup_price_bs, updated_at, updated_by)
    VALUES ('01920000-0000-7000-8000-000000000002', 'DESKTOP_CLIENT', TIMESTAMPTZ '2026-09-25T00:00:00+00:00', NULL, 'Interfaz compilada: 100.000+ productos sin latencia.', 0.0, 'Cliente de escritorio nativo', 6000.0, NULL, NULL);
    INSERT INTO iam.modules (id, code, created_at, created_by, description, monthly_fee_bs, name, setup_price_bs, updated_at, updated_by)
    VALUES ('01920000-0000-7000-8000-000000000003', 'POS_HARDWARE', TIMESTAMPTZ '2026-09-25T00:00:00+00:00', NULL, 'Cajas, escáneres e impresoras ESC/POS (COM/USB/red).', 0.0, 'Módulo POS y hardware', 4500.0, NULL, NULL);
    INSERT INTO iam.modules (id, code, created_at, created_by, description, monthly_fee_bs, name, setup_price_bs, updated_at, updated_by)
    VALUES ('01920000-0000-7000-8000-000000000004', 'RBAC', TIMESTAMPTZ '2026-09-25T00:00:00+00:00', NULL, 'Login cifrado y trazabilidad inmutable por operador.', 0.0, 'Control de acceso (RBAC)', 3000.0, NULL, NULL);
    INSERT INTO iam.modules (id, code, created_at, created_by, description, monthly_fee_bs, name, setup_price_bs, updated_at, updated_by)
    VALUES ('01920000-0000-7000-8000-000000000005', 'SLA_SUPPORT', TIMESTAMPTZ '2026-09-25T00:00:00+00:00', NULL, 'Respaldo, telemetría y actualizaciones de seguridad.', 800.0, 'SLA de soporte', 0.0, NULL, NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_access_logs_tenant_id_hardware_token_id ON iam.access_logs (tenant_id, hardware_token_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_access_logs_tenant_id_occurred_at ON iam.access_logs (tenant_id, occurred_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_access_logs_tenant_id_user_id ON iam.access_logs (tenant_id, user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_accounts_tenant_id_parent_account_id ON accounting.accounts (tenant_id, parent_account_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_accounts_tenant_id_code ON accounting.accounts (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_addresses_tenant_id_postal_code_id ON sales.addresses (tenant_id, postal_code_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_adjustment_reasons_tenant_id_code ON inventory.adjustment_reasons (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_aisles_tenant_id_zone_id ON warehouse.aisles (tenant_id, zone_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_aisles_zone_id_code ON warehouse.aisles (zone_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_attribute_values_tenant_id_attribute_id ON catalog.attribute_values (tenant_id, attribute_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_attribute_values_attribute_id_value ON catalog.attribute_values (attribute_id, value);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_attributes_tenant_id_name ON catalog.attributes (tenant_id, name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_audit_logs_tenant_id_occurred_at ON iam.audit_logs (tenant_id, occurred_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_audit_logs_tenant_id_user_id ON iam.audit_logs (tenant_id, user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_audit_logs_tenant_id_legacy_reference ON iam.audit_logs (tenant_id, legacy_reference) WHERE legacy_reference IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_average_cost_history_tenant_id_stock_movement_id ON accounting.average_cost_history (tenant_id, stock_movement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_average_cost_history_tenant_id_variant_id ON accounting.average_cost_history (tenant_id, variant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_average_cost_history_tenant_id_warehouse_id ON accounting.average_cost_history (tenant_id, warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_average_cost_history_variant_id_warehouse_id_effective_at ON accounting.average_cost_history (variant_id, warehouse_id, effective_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_barcode_types_tenant_id_code ON catalog.barcode_types (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_batches_tenant_id_variant_id ON inventory.batches (tenant_id, variant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_batches_variant_id ON inventory.batches (variant_id) WHERE is_default;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_batches_variant_id_lot_number ON inventory.batches (variant_id, lot_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_bin_assignments_tenant_id_bin_id ON warehouse.bin_assignments (tenant_id, bin_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_bin_assignments_tenant_id_variant_id ON warehouse.bin_assignments (tenant_id, variant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_bins_tenant_id_location_type_id ON warehouse.bins (tenant_id, location_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_bins_tenant_id_shelf_id ON warehouse.bins (tenant_id, shelf_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_bins_tenant_id_code ON warehouse.bins (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_branch_users_tenant_id_branch_id ON warehouse.branch_users (tenant_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_branch_users_tenant_id_user_id ON warehouse.branch_users (tenant_id, user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_branches_tenant_id_address_id ON warehouse.branches (tenant_id, address_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_branches_tenant_id_code ON warehouse.branches (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_brands_tenant_id_name ON catalog.brands (tenant_id, name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_cash_movements_tenant_id_pos_session_id ON sales.cash_movements (tenant_id, pos_session_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_cash_movements_tenant_id_recorded_by_user_id ON sales.cash_movements (tenant_id, recorded_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_categories_tenant_id_code ON catalog.categories (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_category_hierarchies_descendant_id ON catalog.category_hierarchies (descendant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_category_hierarchies_tenant_id_ancestor_id ON catalog.category_hierarchies (tenant_id, ancestor_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_category_hierarchies_tenant_id_descendant_id ON catalog.category_hierarchies (tenant_id, descendant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_cities_tenant_id_state_id ON sales.cities (tenant_id, state_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_cities_state_id_name ON sales.cities (state_id, name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_cost_centers_tenant_id_branch_id ON accounting.cost_centers (tenant_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_cost_centers_tenant_id_code ON accounting.cost_centers (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_countries_tenant_id_iso_code ON sales.countries (tenant_id, iso_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_currencies_tenant_id_code ON accounting.currencies (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_customer_addresses_tenant_id_address_id ON sales.customer_addresses (tenant_id, address_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_customer_addresses_tenant_id_customer_id ON sales.customer_addresses (tenant_id, customer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_customer_addresses_customer_id_address_type ON sales.customer_addresses (customer_id, address_type) WHERE is_default;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_customer_categories_tenant_id_default_price_list_id ON sales.customer_categories (tenant_id, default_price_list_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_customer_categories_tenant_id_code ON sales.customer_categories (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_customers_tenant_id_customer_category_id ON sales.customers (tenant_id, customer_category_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_customers_tenant_id_code ON sales.customers (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_exchange_rates_tenant_id_from_currency_id ON accounting.exchange_rates (tenant_id, from_currency_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_exchange_rates_tenant_id_to_currency_id ON accounting.exchange_rates (tenant_id, to_currency_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_exchange_rates_from_currency_id_to_currency_id_effe_b925ed63 ON accounting.exchange_rates (from_currency_id, to_currency_id, effective_date);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_fiscal_periods_tenant_id_year_month ON accounting.fiscal_periods (tenant_id, year, month);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_goods_receipt_lines_tenant_id_goods_receipt_id ON purchasing.goods_receipt_lines (tenant_id, goods_receipt_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_goods_receipt_lines_tenant_id_purchase_order_line_id ON purchasing.goods_receipt_lines (tenant_id, purchase_order_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_goods_receipt_lines_tenant_id_stock_level_id ON purchasing.goods_receipt_lines (tenant_id, stock_level_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_goods_receipt_lines_tenant_id_stock_movement_id ON purchasing.goods_receipt_lines (tenant_id, stock_movement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_goods_receipt_lines_stock_movement_id ON purchasing.goods_receipt_lines (stock_movement_id) WHERE stock_movement_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_goods_receipts_tenant_id_purchase_order_id ON purchasing.goods_receipts (tenant_id, purchase_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_goods_receipts_tenant_id_received_by_user_id ON purchasing.goods_receipts (tenant_id, received_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_goods_receipts_tenant_id_supplier_id ON purchasing.goods_receipts (tenant_id, supplier_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_goods_receipts_tenant_id_warehouse_id ON purchasing.goods_receipts (tenant_id, warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_goods_receipts_tenant_id_number ON purchasing.goods_receipts (tenant_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_hardware_tokens_tenant_id_branch_id ON iam.hardware_tokens (tenant_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_hardware_tokens_tenant_id_fingerprint ON iam.hardware_tokens (tenant_id, fingerprint);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_invoice_lines_tenant_id_invoice_id ON sales.invoice_lines (tenant_id, invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_invoice_lines_tenant_id_sales_order_line_id ON sales.invoice_lines (tenant_id, sales_order_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_invoice_lines_tenant_id_tax_rate_id ON sales.invoice_lines (tenant_id, tax_rate_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_invoice_lines_sales_order_line_id ON sales.invoice_lines (sales_order_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_invoices_tenant_id_sales_order_id ON sales.invoices (tenant_id, sales_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_invoices_sales_order_id ON sales.invoices (sales_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_invoices_tenant_id_number ON sales.invoices (tenant_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_journal_entries_tenant_id_currency_id ON accounting.journal_entries (tenant_id, currency_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_journal_entries_tenant_id_fiscal_period_id ON accounting.journal_entries (tenant_id, fiscal_period_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_journal_entries_tenant_id_posted_by_user_id ON accounting.journal_entries (tenant_id, posted_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_journal_entries_tenant_id_number ON accounting.journal_entries (tenant_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_journal_lines_tenant_id_account_id ON accounting.journal_lines (tenant_id, account_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_journal_lines_tenant_id_cost_center_id ON accounting.journal_lines (tenant_id, cost_center_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_journal_lines_tenant_id_journal_entry_id ON accounting.journal_lines (tenant_id, journal_entry_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_location_types_tenant_id_code ON warehouse.location_types (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_models_tenant_id_brand_id ON catalog.models (tenant_id, brand_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_models_brand_id_name ON catalog.models (brand_id, name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_modules_code ON iam.modules (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_movement_types_tenant_id ON inventory.movement_types (tenant_id) WHERE is_initial_balance;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_movement_types_tenant_id_code ON inventory.movement_types (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_payment_methods_tenant_id_code ON sales.payment_methods (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_payments_tenant_id_invoice_id ON sales.payments (tenant_id, invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_payments_tenant_id_payment_method_id ON sales.payments (tenant_id, payment_method_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_payments_tenant_id_pos_session_id ON sales.payments (tenant_id, pos_session_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_payments_tenant_id_recorded_by_user_id ON sales.payments (tenant_id, recorded_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_permissions_tenant_id_code ON iam.permissions (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_physical_count_lines_tenant_id_counted_by_user_id ON inventory.physical_count_lines (tenant_id, counted_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_physical_count_lines_tenant_id_physical_count_id ON inventory.physical_count_lines (tenant_id, physical_count_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_physical_count_lines_tenant_id_stock_level_id ON inventory.physical_count_lines (tenant_id, stock_level_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_physical_count_lines_tenant_id_stock_movement_id ON inventory.physical_count_lines (tenant_id, stock_movement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_physical_count_lines_physical_count_id_stock_level_id ON inventory.physical_count_lines (physical_count_id, stock_level_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_physical_counts_tenant_id_posted_by_user_id ON inventory.physical_counts (tenant_id, posted_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_physical_counts_tenant_id_warehouse_id ON inventory.physical_counts (tenant_id, warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_physical_counts_tenant_id_number ON inventory.physical_counts (tenant_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_physical_counts_warehouse_id ON inventory.physical_counts (warehouse_id) WHERE status = 'Open';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_pos_registers_tenant_id_hardware_token_id ON sales.pos_registers (tenant_id, hardware_token_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_pos_registers_tenant_id_warehouse_id ON sales.pos_registers (tenant_id, warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_pos_registers_hardware_token_id ON sales.pos_registers (hardware_token_id) WHERE hardware_token_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_pos_registers_tenant_id_code ON sales.pos_registers (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_pos_sessions_tenant_id_closed_by_user_id ON sales.pos_sessions (tenant_id, closed_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_pos_sessions_tenant_id_opened_by_user_id ON sales.pos_sessions (tenant_id, opened_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_pos_sessions_tenant_id_pos_register_id ON sales.pos_sessions (tenant_id, pos_register_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_pos_sessions_pos_register_id ON sales.pos_sessions (pos_register_id) WHERE status = 'Open';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_postal_codes_tenant_id_city_id ON sales.postal_codes (tenant_id, city_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_postal_codes_city_id_code ON sales.postal_codes (city_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_price_list_items_tenant_id_price_list_id ON sales.price_list_items (tenant_id, price_list_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_price_list_items_tenant_id_variant_id ON sales.price_list_items (tenant_id, variant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_price_lists_tenant_id_currency_id ON sales.price_lists (tenant_id, currency_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_price_lists_tenant_id ON sales.price_lists (tenant_id) WHERE is_default;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_price_lists_tenant_id_name ON sales.price_lists (tenant_id, name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_product_barcodes_tenant_id_barcode_type_id ON catalog.product_barcodes (tenant_id, barcode_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_product_barcodes_tenant_id_unit_id ON catalog.product_barcodes (tenant_id, unit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_product_barcodes_tenant_id_variant_id ON catalog.product_barcodes (tenant_id, variant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_product_barcodes_tenant_id_code ON catalog.product_barcodes (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_product_barcodes_variant_id ON catalog.product_barcodes (variant_id) WHERE is_primary;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_product_stock_policies_tenant_id_variant_id ON catalog.product_stock_policies (tenant_id, variant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_product_stock_policies_tenant_id_warehouse_id ON catalog.product_stock_policies (tenant_id, warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_product_stock_policies_variant_id_warehouse_id ON catalog.product_stock_policies (variant_id, warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_product_suppliers_tenant_id_product_id ON catalog.product_suppliers (tenant_id, product_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_product_suppliers_tenant_id_supplier_id ON catalog.product_suppliers (tenant_id, supplier_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_product_suppliers_product_id ON catalog.product_suppliers (product_id) WHERE is_preferred;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_product_taxes_tenant_id_product_id ON catalog.product_taxes (tenant_id, product_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_product_taxes_tenant_id_tax_id ON catalog.product_taxes (tenant_id, tax_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_product_unit_conversions_tenant_id_product_id ON catalog.product_unit_conversions (tenant_id, product_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_product_unit_conversions_tenant_id_unit_id ON catalog.product_unit_conversions (tenant_id, unit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_product_unit_conversions_product_id_unit_id ON catalog.product_unit_conversions (product_id, unit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_product_variant_attributes_tenant_id_attribute_value_id ON catalog.product_variant_attributes (tenant_id, attribute_value_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_product_variant_attributes_tenant_id_variant_id ON catalog.product_variant_attributes (tenant_id, variant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_product_variants_tenant_id_product_id ON catalog.product_variants (tenant_id, product_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_product_variants_product_id ON catalog.product_variants (product_id) WHERE is_default;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_product_variants_tenant_id_sku ON catalog.product_variants (tenant_id, sku);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_products_tenant_id_base_unit_id ON catalog.products (tenant_id, base_unit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_products_tenant_id_category_id ON catalog.products (tenant_id, category_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_products_tenant_id_model_id ON catalog.products (tenant_id, model_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_products_tenant_id_code ON catalog.products (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_purchase_order_lines_tenant_id_purchase_order_id ON purchasing.purchase_order_lines (tenant_id, purchase_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_purchase_order_lines_tenant_id_unit_id ON purchasing.purchase_order_lines (tenant_id, unit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_purchase_order_lines_tenant_id_variant_id ON purchasing.purchase_order_lines (tenant_id, variant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_purchase_order_lines_purchase_order_id_variant_id_unit_id ON purchasing.purchase_order_lines (purchase_order_id, variant_id, unit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_purchase_orders_tenant_id_currency_id ON purchasing.purchase_orders (tenant_id, currency_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_purchase_orders_tenant_id_supplier_id ON purchasing.purchase_orders (tenant_id, supplier_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_purchase_orders_tenant_id_warehouse_id ON purchasing.purchase_orders (tenant_id, warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_purchase_orders_tenant_id_number ON purchasing.purchase_orders (tenant_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_purchase_return_lines_tenant_id_goods_receipt_line_id ON purchasing.purchase_return_lines (tenant_id, goods_receipt_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_purchase_return_lines_tenant_id_purchase_return_id ON purchasing.purchase_return_lines (tenant_id, purchase_return_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_purchase_return_lines_tenant_id_stock_level_id ON purchasing.purchase_return_lines (tenant_id, stock_level_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_purchase_return_lines_tenant_id_stock_movement_id ON purchasing.purchase_return_lines (tenant_id, stock_movement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_purchase_returns_tenant_id_supplier_id ON purchasing.purchase_returns (tenant_id, supplier_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_purchase_returns_tenant_id_number ON purchasing.purchase_returns (tenant_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_racks_tenant_id_aisle_id ON warehouse.racks (tenant_id, aisle_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_racks_aisle_id_code ON warehouse.racks (aisle_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_role_permissions_tenant_id_permission_id ON iam.role_permissions (tenant_id, permission_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_role_permissions_tenant_id_role_id ON iam.role_permissions (tenant_id, role_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_roles_tenant_id_code ON iam.roles (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_sales_order_lines_tenant_id_sales_order_id ON sales.sales_order_lines (tenant_id, sales_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_sales_order_lines_tenant_id_stock_movement_id ON sales.sales_order_lines (tenant_id, stock_movement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_sales_order_lines_tenant_id_unit_id ON sales.sales_order_lines (tenant_id, unit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_sales_order_lines_tenant_id_variant_id ON sales.sales_order_lines (tenant_id, variant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_sales_order_lines_stock_movement_id ON sales.sales_order_lines (stock_movement_id) WHERE stock_movement_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_sales_orders_tenant_id_customer_id ON sales.sales_orders (tenant_id, customer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_sales_orders_tenant_id_pos_session_id ON sales.sales_orders (tenant_id, pos_session_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_sales_orders_tenant_id_price_list_id ON sales.sales_orders (tenant_id, price_list_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_sales_orders_tenant_id_warehouse_id ON sales.sales_orders (tenant_id, warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_sales_orders_tenant_id_number ON sales.sales_orders (tenant_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_serial_numbers_tenant_id_batch_id ON inventory.serial_numbers (tenant_id, batch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_serial_numbers_tenant_id_stock_level_id ON inventory.serial_numbers (tenant_id, stock_level_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_serial_numbers_batch_id_serial ON inventory.serial_numbers (batch_id, serial);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_sessions_tenant_id_hardware_token_id ON iam.sessions (tenant_id, hardware_token_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_sessions_tenant_id_user_id ON iam.sessions (tenant_id, user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_sessions_user_id_started_at ON iam.sessions (user_id, started_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_shelves_tenant_id_rack_id ON warehouse.shelves (tenant_id, rack_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_shelves_rack_id_code ON warehouse.shelves (rack_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_states_tenant_id_country_id ON sales.states (tenant_id, country_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_states_country_id_code ON sales.states (country_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_adjustment_lines_tenant_id_movement_type_id ON inventory.stock_adjustment_lines (tenant_id, movement_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_adjustment_lines_tenant_id_stock_adjustment_id ON inventory.stock_adjustment_lines (tenant_id, stock_adjustment_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_adjustment_lines_tenant_id_stock_level_id ON inventory.stock_adjustment_lines (tenant_id, stock_level_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_adjustment_lines_tenant_id_stock_movement_id ON inventory.stock_adjustment_lines (tenant_id, stock_movement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_stock_adjustment_lines_stock_movement_id ON inventory.stock_adjustment_lines (stock_movement_id) WHERE stock_movement_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_adjustments_tenant_id_adjustment_reason_id ON inventory.stock_adjustments (tenant_id, adjustment_reason_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_adjustments_tenant_id_posted_by_user_id ON inventory.stock_adjustments (tenant_id, posted_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_adjustments_tenant_id_warehouse_id ON inventory.stock_adjustments (tenant_id, warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_stock_adjustments_tenant_id_number ON inventory.stock_adjustments (tenant_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_levels_tenant_id_batch_id ON inventory.stock_levels (tenant_id, batch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_levels_tenant_id_bin_id ON inventory.stock_levels (tenant_id, bin_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_stock_levels_bin_id_batch_id ON inventory.stock_levels (bin_id, batch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_movements_correlation_id ON inventory.stock_movements (correlation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_movements_stock_level_id_recorded_at ON inventory.stock_movements (stock_level_id, recorded_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_movements_tenant_id_adjustment_reason_id ON inventory.stock_movements (tenant_id, adjustment_reason_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_movements_tenant_id_business_date ON inventory.stock_movements (tenant_id, business_date);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_movements_tenant_id_movement_type_id ON inventory.stock_movements (tenant_id, movement_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_movements_tenant_id_recorded_by_user_id ON inventory.stock_movements (tenant_id, recorded_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_movements_tenant_id_stock_level_id ON inventory.stock_movements (tenant_id, stock_level_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_stock_movements_tenant_id_legacy_reference ON inventory.stock_movements (tenant_id, legacy_reference) WHERE legacy_reference IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_reservations_stock_level_id_status ON inventory.stock_reservations (stock_level_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_reservations_tenant_id_pos_session_id ON inventory.stock_reservations (tenant_id, pos_session_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_reservations_tenant_id_sales_order_line_id ON inventory.stock_reservations (tenant_id, sales_order_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_reservations_tenant_id_stock_level_id ON inventory.stock_reservations (tenant_id, stock_level_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_stock_statuses_tenant_id_code ON inventory.stock_statuses (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_stock_statuses_tenant_id_priority ON inventory.stock_statuses (tenant_id, priority);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_transfer_lines_tenant_id_destination_stock_level_id ON inventory.stock_transfer_lines (tenant_id, destination_stock_level_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_transfer_lines_tenant_id_inbound_movement_id ON inventory.stock_transfer_lines (tenant_id, inbound_movement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_transfer_lines_tenant_id_outbound_movement_id ON inventory.stock_transfer_lines (tenant_id, outbound_movement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_transfer_lines_tenant_id_source_stock_level_id ON inventory.stock_transfer_lines (tenant_id, source_stock_level_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_transfer_lines_tenant_id_stock_transfer_id ON inventory.stock_transfer_lines (tenant_id, stock_transfer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_transfers_tenant_id_from_warehouse_id ON inventory.stock_transfers (tenant_id, from_warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_stock_transfers_tenant_id_to_warehouse_id ON inventory.stock_transfers (tenant_id, to_warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_stock_transfers_tenant_id_number ON inventory.stock_transfers (tenant_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_supplier_addresses_tenant_id_address_id ON purchasing.supplier_addresses (tenant_id, address_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_supplier_addresses_tenant_id_supplier_id ON purchasing.supplier_addresses (tenant_id, supplier_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_supplier_contacts_tenant_id_supplier_id ON purchasing.supplier_contacts (tenant_id, supplier_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_supplier_contacts_supplier_id ON purchasing.supplier_contacts (supplier_id) WHERE is_primary;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_supplier_invoice_lines_tenant_id_goods_receipt_line_id ON purchasing.supplier_invoice_lines (tenant_id, goods_receipt_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_supplier_invoice_lines_tenant_id_supplier_invoice_id ON purchasing.supplier_invoice_lines (tenant_id, supplier_invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_supplier_invoice_lines_tenant_id_tax_rate_id ON purchasing.supplier_invoice_lines (tenant_id, tax_rate_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_supplier_invoices_tenant_id_currency_id ON purchasing.supplier_invoices (tenant_id, currency_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_supplier_invoices_tenant_id_supplier_id ON purchasing.supplier_invoices (tenant_id, supplier_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_supplier_invoices_supplier_id_number ON purchasing.supplier_invoices (supplier_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_suppliers_tenant_id_code ON purchasing.suppliers (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_suppliers_tenant_id_legal_name ON purchasing.suppliers (tenant_id, legal_name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_tax_rates_tenant_id_tax_id ON accounting.tax_rates (tenant_id, tax_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_tax_rates_tax_id_valid_from ON accounting.tax_rates (tax_id, valid_from);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_tax_rules_tenant_id_branch_id ON accounting.tax_rules (tenant_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_tax_rules_tenant_id_customer_category_id ON accounting.tax_rules (tenant_id, customer_category_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_tax_rules_tenant_id_tax_id ON accounting.tax_rules (tenant_id, tax_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_tax_rules_tax_id_customer_category_id_branch_id ON accounting.tax_rules (tax_id, customer_category_id, branch_id) NULLS NOT DISTINCT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_taxes_tenant_id_code ON catalog.taxes (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_tenant_configs_tenant_id_default_currency_id ON iam.tenant_configs (tenant_id, default_currency_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_tenant_configs_tenant_id_default_warehouse_id ON iam.tenant_configs (tenant_id, default_warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_tenant_modules_module_id ON iam.tenant_modules (module_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_tenants_code ON iam.tenants (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_unit_conversions_tenant_id_from_unit_id ON catalog.unit_conversions (tenant_id, from_unit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_unit_conversions_tenant_id_to_unit_id ON catalog.unit_conversions (tenant_id, to_unit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_unit_conversions_from_unit_id_to_unit_id ON catalog.unit_conversions (from_unit_id, to_unit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_units_of_measure_tenant_id_code ON catalog.units_of_measure (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_user_credentials_tenant_id_user_id ON iam.user_credentials (tenant_id, user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_user_roles_tenant_id_role_id ON iam.user_roles (tenant_id, role_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_user_roles_tenant_id_user_id ON iam.user_roles (tenant_id, user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_users_tenant_id_email ON iam.users (tenant_id, email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_warehouses_tenant_id_branch_id ON warehouse.warehouses (tenant_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_warehouses_tenant_id_code ON warehouse.warehouses (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_zones_tenant_id_location_type_id ON warehouse.zones (tenant_id, location_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE INDEX ix_zones_tenant_id_warehouse_id ON warehouse.zones (tenant_id, warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    CREATE UNIQUE INDEX ux_zones_warehouse_id_code ON warehouse.zones (warehouse_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153521_InitialCreate') THEN
    INSERT INTO iam.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260925153521_InitialCreate', '8.0.31');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153705_GuardsRlsAndViews') THEN
    CREATE OR REPLACE FUNCTION iam.minv_append_only() RETURNS trigger
    LANGUAGE plpgsql AS $$
    BEGIN
        RAISE EXCEPTION 'M-INV: %.% es append-only: % no permitido (corrija con un movimiento compensatorio)',
            TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP USING ERRCODE = 'P0001';
    END;
    $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153705_GuardsRlsAndViews') THEN
    CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON inventory.stock_movements
        FOR EACH ROW EXECUTE FUNCTION iam.minv_append_only();
    CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON inventory.stock_movements
        FOR EACH STATEMENT EXECUTE FUNCTION iam.minv_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153705_GuardsRlsAndViews') THEN
    CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON iam.audit_logs
        FOR EACH ROW EXECUTE FUNCTION iam.minv_append_only();
    CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON iam.audit_logs
        FOR EACH STATEMENT EXECUTE FUNCTION iam.minv_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153705_GuardsRlsAndViews') THEN
    CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON iam.access_logs
        FOR EACH ROW EXECUTE FUNCTION iam.minv_append_only();
    CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON iam.access_logs
        FOR EACH STATEMENT EXECUTE FUNCTION iam.minv_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153705_GuardsRlsAndViews') THEN
    CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON sales.cash_movements
        FOR EACH ROW EXECUTE FUNCTION iam.minv_append_only();
    CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON sales.cash_movements
        FOR EACH STATEMENT EXECUTE FUNCTION iam.minv_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153705_GuardsRlsAndViews') THEN
    CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON sales.payments
        FOR EACH ROW EXECUTE FUNCTION iam.minv_append_only();
    CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON sales.payments
        FOR EACH STATEMENT EXECUTE FUNCTION iam.minv_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153705_GuardsRlsAndViews') THEN
    CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON accounting.exchange_rates
        FOR EACH ROW EXECUTE FUNCTION iam.minv_append_only();
    CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON accounting.exchange_rates
        FOR EACH STATEMENT EXECUTE FUNCTION iam.minv_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153705_GuardsRlsAndViews') THEN
    CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON accounting.average_cost_history
        FOR EACH ROW EXECUTE FUNCTION iam.minv_append_only();
    CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON accounting.average_cost_history
        FOR EACH STATEMENT EXECUTE FUNCTION iam.minv_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153705_GuardsRlsAndViews') THEN
    CREATE OR REPLACE FUNCTION catalog.minv_variant_single_value() RETURNS trigger
    LANGUAGE plpgsql AS $$
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM catalog.product_variant_attributes pva
            JOIN catalog.attribute_values av ON av.id = pva.attribute_value_id
            JOIN catalog.attribute_values nv ON nv.id = NEW.attribute_value_id
            WHERE pva.variant_id = NEW.variant_id
              AND pva.attribute_value_id <> NEW.attribute_value_id
              AND av.attribute_id = nv.attribute_id) THEN
            RAISE EXCEPTION 'M-INV: la variante ya tiene un valor para ese atributo' USING ERRCODE = 'P0001';
        END IF;
        RETURN NEW;
    END;
    $$;
    CREATE TRIGGER trg_variant_single_value BEFORE INSERT OR UPDATE ON catalog.product_variant_attributes
        FOR EACH ROW EXECUTE FUNCTION catalog.minv_variant_single_value();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153705_GuardsRlsAndViews') THEN
    CREATE OR REPLACE FUNCTION accounting.minv_journal_entry_immutable() RETURNS trigger
    LANGUAGE plpgsql AS $$
    BEGIN
        IF OLD.status = 'Posted' THEN
            RAISE EXCEPTION 'M-INV: el asiento % ya está contabilizado', OLD.number USING ERRCODE = 'P0001';
        END IF;
        RETURN NEW;
    END;
    $$;
    CREATE TRIGGER trg_journal_entry_immutable BEFORE UPDATE ON accounting.journal_entries
        FOR EACH ROW EXECUTE FUNCTION accounting.minv_journal_entry_immutable();

    CREATE OR REPLACE FUNCTION accounting.minv_journal_line_immutable() RETURNS trigger
    LANGUAGE plpgsql AS $$
    BEGIN
        IF EXISTS (SELECT 1 FROM accounting.journal_entries e WHERE e.id = OLD.journal_entry_id AND e.status = 'Posted') THEN
            RAISE EXCEPTION 'M-INV: las líneas de un asiento contabilizado no se modifican' USING ERRCODE = 'P0001';
        END IF;
        IF TG_OP = 'DELETE' THEN
            RETURN OLD;
        END IF;
        RETURN NEW;
    END;
    $$;
    CREATE TRIGGER trg_journal_line_immutable BEFORE UPDATE OR DELETE ON accounting.journal_lines
        FOR EACH ROW EXECUTE FUNCTION accounting.minv_journal_line_immutable();

    CREATE OR REPLACE FUNCTION accounting.minv_check_journal_balance(entry_id uuid) RETURNS void
    LANGUAGE plpgsql AS $$
    DECLARE
        total_debit numeric;
        total_credit numeric;
    BEGIN
        IF EXISTS (SELECT 1 FROM accounting.journal_entries e WHERE e.id = entry_id AND e.status = 'Posted') THEN
            SELECT coalesce(sum(debit), 0), coalesce(sum(credit), 0) INTO total_debit, total_credit
            FROM accounting.journal_lines WHERE journal_entry_id = entry_id;
            IF total_debit <> total_credit OR total_debit = 0 THEN
                RAISE EXCEPTION 'M-INV: el asiento no cuadra (debe %, haber %)', total_debit, total_credit
                    USING ERRCODE = 'P0001';
            END IF;
        END IF;
    END;
    $$;
    CREATE OR REPLACE FUNCTION accounting.minv_journal_entry_balanced() RETURNS trigger
    LANGUAGE plpgsql AS $$
    BEGIN
        PERFORM accounting.minv_check_journal_balance(NEW.id);
        RETURN NULL;
    END;
    $$;
    CREATE OR REPLACE FUNCTION accounting.minv_journal_line_balanced() RETURNS trigger
    LANGUAGE plpgsql AS $$
    BEGIN
        PERFORM accounting.minv_check_journal_balance(NEW.journal_entry_id);
        RETURN NULL;
    END;
    $$;
    CREATE CONSTRAINT TRIGGER trg_journal_entry_balanced AFTER INSERT OR UPDATE ON accounting.journal_entries
        DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION accounting.minv_journal_entry_balanced();
    CREATE CONSTRAINT TRIGGER trg_journal_line_balanced AFTER INSERT ON accounting.journal_lines
        DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION accounting.minv_journal_line_balanced();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153705_GuardsRlsAndViews') THEN
    CREATE OR REPLACE FUNCTION iam.current_tenant_id() RETURNS uuid
    LANGUAGE sql STABLE AS $$
        SELECT NULLIF(current_setting('minv.tenant_id', true), '')::uuid
    $$;
    DO $$
    DECLARE r record;
    BEGIN
        FOR r IN
            SELECT c.table_schema, c.table_name
            FROM information_schema.columns c
            JOIN information_schema.tables t
              ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE'
            WHERE c.column_name = 'tenant_id'
              AND c.table_schema IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting')
        LOOP
            EXECUTE format('ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY', r.table_schema, r.table_name);
            EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I.%I', r.table_schema, r.table_name);
            EXECUTE format('CREATE POLICY tenant_isolation ON %I.%I USING (tenant_id = iam.current_tenant_id()) '
                           'WITH CHECK (tenant_id = iam.current_tenant_id())', r.table_schema, r.table_name);
        END LOOP;
    END;
    $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153705_GuardsRlsAndViews') THEN
    CREATE OR REPLACE VIEW inventory.v_stock_by_variant WITH (security_invoker = true) AS
    SELECT l.tenant_id, z.warehouse_id, b.variant_id, v.sku,
           sum(CASE WHEN t.stock_factor > 0 THEN m.quantity ELSE 0 END) AS entries,
           sum(CASE WHEN t.stock_factor < 0 THEN m.quantity ELSE 0 END) AS issues,
           sum(m.quantity * t.stock_factor) AS stock,
           max(m.business_date) AS last_movement,
           count(*) AS movements
    FROM inventory.stock_movements m
    JOIN inventory.movement_types t ON t.id = m.movement_type_id
    JOIN inventory.stock_levels l ON l.id = m.stock_level_id
    JOIN inventory.batches b ON b.id = l.batch_id
    JOIN catalog.product_variants v ON v.id = b.variant_id
    JOIN warehouse.bins bi ON bi.id = l.bin_id
    JOIN warehouse.shelves sh ON sh.id = bi.shelf_id
    JOIN warehouse.racks r ON r.id = sh.rack_id
    JOIN warehouse.aisles a ON a.id = r.aisle_id
    JOIN warehouse.zones z ON z.id = a.zone_id
    GROUP BY l.tenant_id, z.warehouse_id, b.variant_id, v.sku;

    CREATE OR REPLACE VIEW inventory.v_conservation_breaches WITH (security_invoker = true) AS
    SELECT l.tenant_id, l.id AS stock_level_id, l.quantity_on_hand,
           coalesce(sum(m.quantity * t.stock_factor), 0) AS ledger_quantity
    FROM inventory.stock_levels l
    LEFT JOIN inventory.stock_movements m ON m.stock_level_id = l.id
    LEFT JOIN inventory.movement_types t ON t.id = m.movement_type_id
    GROUP BY l.tenant_id, l.id, l.quantity_on_hand
    HAVING l.quantity_on_hand <> coalesce(sum(m.quantity * t.stock_factor), 0);

    CREATE OR REPLACE VIEW iam.v_activity WITH (security_invoker = true) AS
    SELECT a.tenant_id, a.occurred_at, u.email, u.display_name, a.action, a.outcome, a.details, a.legacy_reference
    FROM iam.audit_logs a
    LEFT JOIN iam.users u ON u.id = a.user_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153705_GuardsRlsAndViews') THEN
    DO $$
    BEGIN
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'minv_app') THEN
            GRANT USAGE ON SCHEMA iam, catalog, warehouse, inventory, purchasing, sales, accounting TO minv_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA iam, catalog, warehouse, inventory, purchasing, sales, accounting TO minv_app;
            GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA iam, catalog, warehouse, inventory, purchasing, sales, accounting TO minv_app;
            REVOKE UPDATE, DELETE, TRUNCATE ON inventory.stock_movements, iam.audit_logs, iam.access_logs, sales.cash_movements, sales.payments, accounting.exchange_rates, accounting.average_cost_history FROM minv_app;
            REVOKE INSERT, UPDATE, DELETE ON iam.modules, iam.__ef_migrations_history FROM minv_app;
        END IF;
    END;
    $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925153705_GuardsRlsAndViews') THEN
    INSERT INTO iam.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260925153705_GuardsRlsAndViews', '8.0.31');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925184331_ProductImages') THEN
    CREATE TABLE catalog.product_images (
        id uuid NOT NULL,
        variant_id uuid NOT NULL,
        content bytea NOT NULL,
        content_type character varying(20) NOT NULL,
        file_name character varying(200),
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_product_images PRIMARY KEY (id),
        CONSTRAINT ak_product_images_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_product_images_tamano CHECK (octet_length(content) BETWEEN 1 AND 1048576),
        CONSTRAINT ck_product_images_tipo CHECK (content_type IN ('image/png', 'image/jpeg')),
        CONSTRAINT fk_product_images_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_product_images_tenant_id_variant_id FOREIGN KEY (tenant_id, variant_id) REFERENCES catalog.product_variants (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925184331_ProductImages') THEN
    CREATE UNIQUE INDEX ux_product_images_tenant_id_variant_id ON catalog.product_images (tenant_id, variant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925184331_ProductImages') THEN
    ALTER TABLE catalog.product_images ENABLE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS tenant_isolation ON catalog.product_images;
    CREATE POLICY tenant_isolation ON catalog.product_images
        USING (tenant_id = iam.current_tenant_id()) WITH CHECK (tenant_id = iam.current_tenant_id());
    DO $$
    BEGIN
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'minv_app') THEN
            GRANT SELECT, INSERT, UPDATE, DELETE ON catalog.product_images TO minv_app;
        END IF;
    END $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925184331_ProductImages') THEN
    INSERT INTO iam.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260925184331_ProductImages', '8.0.31');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DO $$
    BEGIN
        IF EXISTS (SELECT 1 FROM inventory.stock_transfers) THEN
            RAISE EXCEPTION 'M-INV V4: inventory.stock_transfers tiene filas de la V3 (no había caso de uso de transferencias): revíselas antes de migrar.';
        END IF;
    END;
    $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.aisles DROP CONSTRAINT fk_aisles_tenant_id_zone_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE accounting.average_cost_history DROP CONSTRAINT fk_average_cost_history_tenant_id_stock_movement_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE accounting.average_cost_history DROP CONSTRAINT fk_average_cost_history_tenant_id_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.bin_assignments DROP CONSTRAINT fk_bin_assignments_tenant_id_bin_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.bins DROP CONSTRAINT fk_bins_tenant_id_shelf_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.cash_movements DROP CONSTRAINT fk_cash_movements_tenant_id_pos_session_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.goods_receipt_lines DROP CONSTRAINT fk_goods_receipt_lines_tenant_id_goods_receipt_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.goods_receipt_lines DROP CONSTRAINT fk_goods_receipt_lines_tenant_id_purchase_order_line_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.goods_receipt_lines DROP CONSTRAINT fk_goods_receipt_lines_tenant_id_stock_level_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.goods_receipt_lines DROP CONSTRAINT fk_goods_receipt_lines_tenant_id_stock_movement_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.goods_receipts DROP CONSTRAINT fk_goods_receipts_tenant_id_purchase_order_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.goods_receipts DROP CONSTRAINT fk_goods_receipts_tenant_id_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.invoice_lines DROP CONSTRAINT fk_invoice_lines_tenant_id_invoice_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.invoice_lines DROP CONSTRAINT fk_invoice_lines_tenant_id_sales_order_line_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.invoices DROP CONSTRAINT fk_invoices_tenant_id_sales_order_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE accounting.journal_lines DROP CONSTRAINT fk_journal_lines_tenant_id_journal_entry_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.payments DROP CONSTRAINT fk_payments_tenant_id_invoice_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.payments DROP CONSTRAINT fk_payments_tenant_id_pos_session_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.physical_count_lines DROP CONSTRAINT fk_physical_count_lines_tenant_id_physical_count_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.physical_count_lines DROP CONSTRAINT fk_physical_count_lines_tenant_id_stock_level_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.physical_count_lines DROP CONSTRAINT fk_physical_count_lines_tenant_id_stock_movement_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.physical_counts DROP CONSTRAINT fk_physical_counts_tenant_id_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.pos_registers DROP CONSTRAINT fk_pos_registers_tenant_id_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.pos_sessions DROP CONSTRAINT fk_pos_sessions_tenant_id_pos_register_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE catalog.product_stock_policies DROP CONSTRAINT fk_product_stock_policies_tenant_id_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.purchase_order_lines DROP CONSTRAINT fk_purchase_order_lines_tenant_id_purchase_order_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.purchase_orders DROP CONSTRAINT fk_purchase_orders_tenant_id_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.purchase_return_lines DROP CONSTRAINT fk_purchase_return_lines_tenant_id_goods_receipt_line_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.purchase_return_lines DROP CONSTRAINT fk_purchase_return_lines_tenant_id_purchase_return_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.purchase_return_lines DROP CONSTRAINT fk_purchase_return_lines_tenant_id_stock_level_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.purchase_return_lines DROP CONSTRAINT fk_purchase_return_lines_tenant_id_stock_movement_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.racks DROP CONSTRAINT fk_racks_tenant_id_aisle_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.sales_order_lines DROP CONSTRAINT fk_sales_order_lines_tenant_id_sales_order_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.sales_order_lines DROP CONSTRAINT fk_sales_order_lines_tenant_id_stock_movement_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.sales_orders DROP CONSTRAINT fk_sales_orders_tenant_id_pos_session_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.sales_orders DROP CONSTRAINT fk_sales_orders_tenant_id_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.shelves DROP CONSTRAINT fk_shelves_tenant_id_rack_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_adjustment_lines DROP CONSTRAINT fk_stock_adjustment_lines_tenant_id_stock_adjustment_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_adjustment_lines DROP CONSTRAINT fk_stock_adjustment_lines_tenant_id_stock_level_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_adjustment_lines DROP CONSTRAINT fk_stock_adjustment_lines_tenant_id_stock_movement_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_adjustments DROP CONSTRAINT fk_stock_adjustments_tenant_id_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_levels DROP CONSTRAINT fk_stock_levels_tenant_id_bin_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_movements DROP CONSTRAINT fk_stock_movements_tenant_id_stock_level_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_reservations DROP CONSTRAINT fk_stock_reservations_tenant_id_pos_session_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_reservations DROP CONSTRAINT fk_stock_reservations_tenant_id_sales_order_line_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_reservations DROP CONSTRAINT fk_stock_reservations_tenant_id_stock_level_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfer_lines DROP CONSTRAINT fk_stock_transfer_lines_tenant_id_destination_stock_level_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfer_lines DROP CONSTRAINT fk_stock_transfer_lines_tenant_id_inbound_movement_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfer_lines DROP CONSTRAINT fk_stock_transfer_lines_tenant_id_outbound_movement_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfer_lines DROP CONSTRAINT fk_stock_transfer_lines_tenant_id_source_stock_level_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfer_lines DROP CONSTRAINT fk_stock_transfer_lines_tenant_id_stock_transfer_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfers DROP CONSTRAINT fk_stock_transfers_tenant_id_from_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfers DROP CONSTRAINT fk_stock_transfers_tenant_id_to_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.supplier_invoice_lines DROP CONSTRAINT fk_supplier_invoice_lines_tenant_id_goods_receipt_line_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.supplier_invoice_lines DROP CONSTRAINT fk_supplier_invoice_lines_tenant_id_supplier_invoice_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.zones DROP CONSTRAINT fk_zones_tenant_id_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX warehouse.ix_zones_tenant_id_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX warehouse.ix_warehouses_tenant_id_branch_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX purchasing.ix_supplier_invoice_lines_tenant_id_goods_receipt_line_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX purchasing.ix_supplier_invoice_lines_tenant_id_supplier_invoice_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX inventory.ix_stock_transfers_tenant_id_from_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX inventory.ix_stock_transfers_tenant_id_to_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX inventory.ix_stock_transfer_lines_tenant_id_destination_stock_level_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX inventory.ix_stock_transfer_lines_tenant_id_inbound_movement_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX inventory.ix_stock_transfer_lines_tenant_id_outbound_movement_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX inventory.ix_stock_transfer_lines_tenant_id_stock_transfer_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX inventory.ix_stock_reservations_tenant_id_pos_session_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX inventory.ix_stock_reservations_tenant_id_sales_order_line_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX inventory.ix_stock_reservations_tenant_id_stock_level_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX inventory.ix_stock_movements_tenant_id_stock_level_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX inventory.ix_stock_levels_tenant_id_bin_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX inventory.ix_stock_adjustments_tenant_id_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX inventory.ix_stock_adjustment_lines_tenant_id_stock_adjustment_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX inventory.ix_stock_adjustment_lines_tenant_id_stock_level_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX inventory.ix_stock_adjustment_lines_tenant_id_stock_movement_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX warehouse.ix_shelves_tenant_id_rack_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX sales.ix_sales_orders_tenant_id_pos_session_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX sales.ix_sales_orders_tenant_id_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX sales.ix_sales_order_lines_tenant_id_sales_order_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX sales.ix_sales_order_lines_tenant_id_stock_movement_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX warehouse.ix_racks_tenant_id_aisle_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX purchasing.ix_purchase_return_lines_tenant_id_goods_receipt_line_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX purchasing.ix_purchase_return_lines_tenant_id_purchase_return_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX purchasing.ix_purchase_return_lines_tenant_id_stock_level_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX purchasing.ix_purchase_return_lines_tenant_id_stock_movement_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX purchasing.ix_purchase_orders_tenant_id_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX purchasing.ix_purchase_order_lines_tenant_id_purchase_order_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX catalog.ix_product_stock_policies_tenant_id_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX sales.ix_pos_sessions_tenant_id_pos_register_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX sales.ix_pos_registers_tenant_id_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX inventory.ix_physical_counts_tenant_id_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX inventory.ix_physical_count_lines_tenant_id_physical_count_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX inventory.ix_physical_count_lines_tenant_id_stock_level_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX inventory.ix_physical_count_lines_tenant_id_stock_movement_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX sales.ix_payments_tenant_id_invoice_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX sales.ix_payments_tenant_id_pos_session_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX accounting.ix_journal_lines_tenant_id_journal_entry_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX sales.ix_invoices_tenant_id_sales_order_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX sales.ix_invoice_lines_tenant_id_invoice_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX sales.ix_invoice_lines_tenant_id_sales_order_line_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX purchasing.ix_goods_receipts_tenant_id_purchase_order_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX purchasing.ix_goods_receipts_tenant_id_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX purchasing.ix_goods_receipt_lines_tenant_id_goods_receipt_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX purchasing.ix_goods_receipt_lines_tenant_id_purchase_order_line_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX purchasing.ix_goods_receipt_lines_tenant_id_stock_level_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX purchasing.ix_goods_receipt_lines_tenant_id_stock_movement_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX sales.ix_cash_movements_tenant_id_pos_session_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX warehouse.ix_bins_tenant_id_shelf_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX warehouse.ix_bin_assignments_tenant_id_bin_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX accounting.ix_average_cost_history_tenant_id_stock_movement_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX accounting.ix_average_cost_history_tenant_id_variant_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX accounting.ix_average_cost_history_tenant_id_warehouse_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DROP INDEX warehouse.ix_aisles_tenant_id_zone_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfer_lines DROP COLUMN destination_stock_level_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfer_lines DROP COLUMN inbound_movement_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfer_lines DROP COLUMN outbound_movement_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'integration') THEN
            CREATE SCHEMA integration;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfers RENAME COLUMN shipped_at TO dispatched_at;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfer_lines RENAME COLUMN source_stock_level_id TO variant_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER INDEX inventory.ix_stock_transfer_lines_tenant_id_source_stock_level_id RENAME TO ix_stock_transfer_lines_tenant_id_variant_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.zones ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.supplier_invoices ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.supplier_invoice_lines ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfers ADD from_branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfers ADD requested_at timestamp with time zone NOT NULL DEFAULT TIMESTAMPTZ '-infinity';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfers ADD requested_by_user_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfers ADD to_branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfer_lines ADD from_branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfer_lines ADD to_branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfer_lines ADD unit_cost numeric(18,6);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_reservations ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_movements ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_levels ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_adjustments ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_adjustment_lines ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.shelves ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE iam.sessions ADD active_branch_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE iam.sessions ADD expires_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE iam.sessions ADD token_hash character varying(64);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.sales_orders ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.sales_order_lines ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.racks ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.purchase_returns ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.purchase_return_lines ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.purchase_orders ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.purchase_order_lines ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE catalog.product_stock_policies ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.pos_sessions ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.pos_registers ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.physical_counts ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.physical_count_lines ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.payments ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE accounting.journal_lines ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE accounting.journal_entries ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.invoices ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.invoice_lines ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.goods_receipts ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.goods_receipt_lines ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.cash_movements ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.bins ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.bin_assignments ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE accounting.average_cost_history ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE accounting.average_cost_history ADD sequence integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE iam.audit_logs ADD api_key_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE iam.audit_logs ADD branch_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE iam.audit_logs ADD channel character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.aisles ADD branch_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_movements DISABLE TRIGGER USER;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.cash_movements DISABLE TRIGGER USER;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.payments DISABLE TRIGGER USER;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE accounting.average_cost_history DISABLE TRIGGER USER;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE accounting.journal_entries DISABLE TRIGGER USER;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE accounting.journal_lines DISABLE TRIGGER USER;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    -- Topología: la sucursal baja por la jerarquía desde el almacén
    UPDATE warehouse.zones z SET branch_id = w.branch_id FROM warehouse.warehouses w WHERE w.id = z.warehouse_id;
    UPDATE warehouse.aisles a SET branch_id = z.branch_id FROM warehouse.zones z WHERE z.id = a.zone_id;
    UPDATE warehouse.racks r SET branch_id = a.branch_id FROM warehouse.aisles a WHERE a.id = r.aisle_id;
    UPDATE warehouse.shelves s SET branch_id = r.branch_id FROM warehouse.racks r WHERE r.id = s.rack_id;
    UPDATE warehouse.bins b SET branch_id = s.branch_id FROM warehouse.shelves s WHERE s.id = b.shelf_id;
    UPDATE warehouse.bin_assignments x SET branch_id = b.branch_id FROM warehouse.bins b WHERE b.id = x.bin_id;
    UPDATE catalog.product_stock_policies p SET branch_id = w.branch_id FROM warehouse.warehouses w WHERE w.id = p.warehouse_id;

    -- Existencias y libros de stock
    UPDATE inventory.stock_levels l SET branch_id = b.branch_id FROM warehouse.bins b WHERE b.id = l.bin_id;
    UPDATE inventory.stock_movements m SET branch_id = l.branch_id FROM inventory.stock_levels l WHERE l.id = m.stock_level_id;
    UPDATE inventory.stock_reservations r SET branch_id = l.branch_id FROM inventory.stock_levels l WHERE l.id = r.stock_level_id;
    UPDATE inventory.stock_adjustments a SET branch_id = w.branch_id FROM warehouse.warehouses w WHERE w.id = a.warehouse_id;
    UPDATE inventory.stock_adjustment_lines x SET branch_id = a.branch_id FROM inventory.stock_adjustments a WHERE a.id = x.stock_adjustment_id;
    UPDATE inventory.physical_counts c SET branch_id = w.branch_id FROM warehouse.warehouses w WHERE w.id = c.warehouse_id;
    UPDATE inventory.physical_count_lines x SET branch_id = c.branch_id FROM inventory.physical_counts c WHERE c.id = x.physical_count_id;

    -- Compras
    UPDATE purchasing.purchase_orders o SET branch_id = w.branch_id FROM warehouse.warehouses w WHERE w.id = o.warehouse_id;
    UPDATE purchasing.purchase_order_lines x SET branch_id = o.branch_id FROM purchasing.purchase_orders o WHERE o.id = x.purchase_order_id;
    UPDATE purchasing.goods_receipts g SET branch_id = w.branch_id FROM warehouse.warehouses w WHERE w.id = g.warehouse_id;
    UPDATE purchasing.goods_receipt_lines x SET branch_id = g.branch_id FROM purchasing.goods_receipts g WHERE g.id = x.goods_receipt_id;
    UPDATE purchasing.purchase_returns r SET branch_id = l.branch_id
        FROM purchasing.purchase_return_lines x JOIN inventory.stock_levels l ON l.id = x.stock_level_id
        WHERE x.purchase_return_id = r.id;
    UPDATE purchasing.purchase_return_lines x SET branch_id = r.branch_id FROM purchasing.purchase_returns r WHERE r.id = x.purchase_return_id;
    UPDATE purchasing.supplier_invoices i SET branch_id = g.branch_id
        FROM purchasing.supplier_invoice_lines x JOIN purchasing.goods_receipt_lines g ON g.id = x.goods_receipt_line_id
        WHERE x.supplier_invoice_id = i.id;

    -- Ventas y caja
    UPDATE sales.pos_registers r SET branch_id = w.branch_id FROM warehouse.warehouses w WHERE w.id = r.warehouse_id;
    UPDATE sales.pos_sessions s SET branch_id = r.branch_id FROM sales.pos_registers r WHERE r.id = s.pos_register_id;
    UPDATE sales.cash_movements c SET branch_id = s.branch_id FROM sales.pos_sessions s WHERE s.id = c.pos_session_id;
    UPDATE sales.sales_orders o SET branch_id = s.branch_id FROM sales.pos_sessions s WHERE s.id = o.pos_session_id;
    UPDATE sales.sales_orders o SET branch_id = w.branch_id FROM warehouse.warehouses w WHERE w.id = o.warehouse_id;
    UPDATE sales.sales_order_lines x SET branch_id = o.branch_id FROM sales.sales_orders o WHERE o.id = x.sales_order_id;
    UPDATE sales.invoices i SET branch_id = o.branch_id FROM sales.sales_orders o WHERE o.id = i.sales_order_id;
    UPDATE sales.invoice_lines x SET branch_id = i.branch_id FROM sales.invoices i WHERE i.id = x.invoice_id;
    UPDATE sales.payments p SET branch_id = i.branch_id FROM sales.invoices i WHERE i.id = p.invoice_id;

    -- Costos
    UPDATE accounting.average_cost_history h SET branch_id = w.branch_id FROM warehouse.warehouses w WHERE w.id = h.warehouse_id;
    UPDATE accounting.average_cost_history h SET sequence = s.n
    FROM (SELECT id, row_number() OVER (PARTITION BY tenant_id, variant_id, warehouse_id ORDER BY effective_at, id) AS n
          FROM accounting.average_cost_history) s
    WHERE s.id = h.id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DO $$
    DECLARE t text;
    BEGIN
        FOREACH t IN ARRAY ARRAY['purchasing.supplier_invoices', 'accounting.journal_entries', 'purchasing.purchase_returns'] LOOP
            EXECUTE format('UPDATE %s x SET branch_id = (SELECT b.id FROM warehouse.branches b WHERE b.tenant_id = x.tenant_id ORDER BY b.code LIMIT 1) '
                           'WHERE x.branch_id = ''00000000-0000-0000-0000-000000000000''', t);
        END LOOP;
    END;
    $$;
    UPDATE purchasing.supplier_invoice_lines x SET branch_id = i.branch_id FROM purchasing.supplier_invoices i WHERE i.id = x.supplier_invoice_id;
    UPDATE purchasing.purchase_return_lines x SET branch_id = r.branch_id FROM purchasing.purchase_returns r WHERE r.id = x.purchase_return_id;
    UPDATE accounting.journal_lines x SET branch_id = e.branch_id FROM accounting.journal_entries e WHERE e.id = x.journal_entry_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DO $$
    DECLARE t text; n bigint;
    BEGIN
        FOREACH t IN ARRAY ARRAY['warehouse.zones', 'warehouse.aisles', 'warehouse.racks', 'warehouse.shelves', 'warehouse.bins', 'warehouse.bin_assignments', 'catalog.product_stock_policies', 'inventory.stock_levels', 'inventory.stock_movements', 'inventory.stock_reservations', 'inventory.stock_adjustments', 'inventory.stock_adjustment_lines', 'inventory.physical_counts', 'inventory.physical_count_lines', 'inventory.stock_transfer_movements', 'purchasing.purchase_orders', 'purchasing.purchase_order_lines', 'purchasing.goods_receipts', 'purchasing.goods_receipt_lines', 'purchasing.purchase_returns', 'purchasing.purchase_return_lines', 'purchasing.supplier_invoices', 'purchasing.supplier_invoice_lines', 'sales.pos_registers', 'sales.pos_sessions', 'sales.cash_movements', 'sales.sales_orders', 'sales.sales_order_lines', 'sales.invoices', 'sales.invoice_lines', 'sales.payments', 'sales.external_orders', 'accounting.journal_entries', 'accounting.journal_lines', 'accounting.average_cost_history'] LOOP
            CONTINUE WHEN to_regclass(t) IS NULL;   -- tablas nuevas de la V4 (se crean después, vacías)
            EXECUTE format('SELECT count(*) FROM %s WHERE branch_id = ''00000000-0000-0000-0000-000000000000''', t) INTO n;
            IF n > 0 THEN
                RAISE EXCEPTION 'M-INV V4: % filas de % quedaron sin sucursal', n, t;
            END IF;
        END LOOP;
    END;
    $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_movements ENABLE TRIGGER USER;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.cash_movements ENABLE TRIGGER USER;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.payments ENABLE TRIGGER USER;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE accounting.average_cost_history ENABLE TRIGGER USER;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE accounting.journal_entries ENABLE TRIGGER USER;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE accounting.journal_lines ENABLE TRIGGER USER;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.zones ADD CONSTRAINT ak_zones_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.warehouses ADD CONSTRAINT ak_warehouses_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.supplier_invoices ADD CONSTRAINT ak_supplier_invoices_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.supplier_invoice_lines ADD CONSTRAINT ak_supplier_invoice_lines_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfers ADD CONSTRAINT ak_stock_transfers_tenant_id_from_branch_id_to_branch_id_id UNIQUE (tenant_id, from_branch_id, to_branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfer_lines ADD CONSTRAINT ak_stock_transfer_lines_tenant_id_from_branch_id_to_br_7655fbfd UNIQUE (tenant_id, from_branch_id, to_branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_reservations ADD CONSTRAINT ak_stock_reservations_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_movements ADD CONSTRAINT ak_stock_movements_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_levels ADD CONSTRAINT ak_stock_levels_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_adjustments ADD CONSTRAINT ak_stock_adjustments_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_adjustment_lines ADD CONSTRAINT ak_stock_adjustment_lines_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.shelves ADD CONSTRAINT ak_shelves_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.sales_orders ADD CONSTRAINT ak_sales_orders_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.sales_order_lines ADD CONSTRAINT ak_sales_order_lines_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.racks ADD CONSTRAINT ak_racks_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.purchase_returns ADD CONSTRAINT ak_purchase_returns_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.purchase_return_lines ADD CONSTRAINT ak_purchase_return_lines_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.purchase_orders ADD CONSTRAINT ak_purchase_orders_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.purchase_order_lines ADD CONSTRAINT ak_purchase_order_lines_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE catalog.product_stock_policies ADD CONSTRAINT ak_product_stock_policies_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.pos_sessions ADD CONSTRAINT ak_pos_sessions_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.pos_registers ADD CONSTRAINT ak_pos_registers_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.physical_counts ADD CONSTRAINT ak_physical_counts_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.physical_count_lines ADD CONSTRAINT ak_physical_count_lines_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.payments ADD CONSTRAINT ak_payments_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE accounting.journal_lines ADD CONSTRAINT ak_journal_lines_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE accounting.journal_entries ADD CONSTRAINT ak_journal_entries_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.invoices ADD CONSTRAINT ak_invoices_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.invoice_lines ADD CONSTRAINT ak_invoice_lines_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.goods_receipts ADD CONSTRAINT ak_goods_receipts_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.goods_receipt_lines ADD CONSTRAINT ak_goods_receipt_lines_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.cash_movements ADD CONSTRAINT ak_cash_movements_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.bins ADD CONSTRAINT ak_bins_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE accounting.average_cost_history ADD CONSTRAINT ak_average_cost_history_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.aisles ADD CONSTRAINT ak_aisles_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TABLE integration.api_keys (
        id uuid NOT NULL,
        name character varying(100) NOT NULL,
        prefix character varying(8) NOT NULL,
        token_hash character varying(64) NOT NULL,
        owner_user_id uuid NOT NULL,
        branch_id uuid,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        expires_at timestamp with time zone,
        revoked_at timestamp with time zone,
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_api_keys PRIMARY KEY (id),
        CONSTRAINT ak_api_keys_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_api_keys_hash CHECK (token_hash ~ '^[0-9a-f]{64}$'),
        CONSTRAINT ck_api_keys_prefijo CHECK (prefix ~ '^[a-z0-9]{8}$'),
        CONSTRAINT ck_api_keys_vencimiento CHECK (expires_at IS NULL OR expires_at > created_at),
        CONSTRAINT fk_api_keys_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_api_keys_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES warehouse.branches (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_api_keys_tenant_id_owner_user_id FOREIGN KEY (tenant_id, owner_user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TABLE sales.external_orders (
        id uuid NOT NULL,
        branch_id uuid NOT NULL,
        channel character varying(60) NOT NULL,
        external_id character varying(100) NOT NULL,
        request_hash character varying(64) NOT NULL,
        sales_order_id uuid NOT NULL,
        invoice_number character varying(30) NOT NULL,
        received_at timestamp with time zone NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_external_orders PRIMARY KEY (id),
        CONSTRAINT ak_external_orders_tenant_id_branch_id_id UNIQUE (tenant_id, branch_id, id),
        CONSTRAINT ak_external_orders_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_external_orders_hash CHECK (request_hash ~ '^[0-9a-f]{64}$'),
        CONSTRAINT fk_external_orders_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_external_orders_tenant_id_branch_id_sales_order_id FOREIGN KEY (tenant_id, branch_id, sales_order_id) REFERENCES sales.sales_orders (tenant_id, branch_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TABLE integration.outbox_events (
        id uuid NOT NULL,
        event_type character varying(60) NOT NULL,
        branch_id uuid,
        payload jsonb NOT NULL,
        occurred_at timestamp with time zone NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_outbox_events PRIMARY KEY (id),
        CONSTRAINT ak_outbox_events_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_outbox_events_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_outbox_events_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES warehouse.branches (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TABLE iam.processed_requests (
        id uuid NOT NULL,
        request_id uuid NOT NULL,
        user_id uuid NOT NULL,
        request_type character varying(200) NOT NULL,
        request_hash character varying(64) NOT NULL,
        response text NOT NULL,
        processed_at timestamp with time zone NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_processed_requests PRIMARY KEY (id),
        CONSTRAINT ak_processed_requests_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_processed_requests_hash CHECK (request_hash ~ '^[0-9a-f]{64}$'),
        CONSTRAINT fk_processed_requests_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_processed_requests_tenant_id_user_id FOREIGN KEY (tenant_id, user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TABLE inventory.stock_transfer_discrepancies (
        id uuid NOT NULL,
        from_branch_id uuid NOT NULL,
        to_branch_id uuid NOT NULL,
        transfer_line_id uuid NOT NULL,
        quantity numeric(18,6) NOT NULL,
        reason character varying(200) NOT NULL,
        recorded_by_user_id uuid NOT NULL,
        recorded_at timestamp with time zone NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_stock_transfer_discrepancies PRIMARY KEY (id),
        CONSTRAINT ak_stock_transfer_discrepancies_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_stock_transfer_discrepancies_cantidad CHECK (quantity > 0),
        CONSTRAINT fk_stock_transfer_discrepancies_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_transfer_discrepancies_tenant_id_from_branch__1bf1d075 FOREIGN KEY (tenant_id, from_branch_id, to_branch_id, transfer_line_id) REFERENCES inventory.stock_transfer_lines (tenant_id, from_branch_id, to_branch_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_transfer_discrepancies_tenant_id_recorded_by_user_id FOREIGN KEY (tenant_id, recorded_by_user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TABLE inventory.stock_transfer_events (
        id uuid NOT NULL,
        from_branch_id uuid NOT NULL,
        to_branch_id uuid NOT NULL,
        transfer_id uuid NOT NULL,
        status character varying(20) NOT NULL,
        user_id uuid NOT NULL,
        occurred_at timestamp with time zone NOT NULL,
        detail character varying(250) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_stock_transfer_events PRIMARY KEY (id),
        CONSTRAINT ak_stock_transfer_events_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT fk_stock_transfer_events_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_transfer_events_tenant_id_from_branch_id_to_b_7078530b FOREIGN KEY (tenant_id, from_branch_id, to_branch_id, transfer_id) REFERENCES inventory.stock_transfers (tenant_id, from_branch_id, to_branch_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_transfer_events_tenant_id_user_id FOREIGN KEY (tenant_id, user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TABLE inventory.stock_transfer_line_batches (
        transfer_line_id uuid NOT NULL,
        batch_id uuid NOT NULL,
        from_branch_id uuid NOT NULL,
        to_branch_id uuid NOT NULL,
        quantity numeric(18,6) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_stock_transfer_line_batches PRIMARY KEY (transfer_line_id, batch_id),
        CONSTRAINT ck_stock_transfer_line_batches_cantidad CHECK (quantity > 0),
        CONSTRAINT fk_stock_transfer_line_batches_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_transfer_line_batches_tenant_id_batch_id FOREIGN KEY (tenant_id, batch_id) REFERENCES inventory.batches (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_transfer_line_batches_tenant_id_from_branch_i_86ebd39b FOREIGN KEY (tenant_id, from_branch_id, to_branch_id, transfer_line_id) REFERENCES inventory.stock_transfer_lines (tenant_id, from_branch_id, to_branch_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TABLE inventory.stock_transfer_movements (
        transfer_line_id uuid NOT NULL,
        stock_movement_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        direction character varying(5) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_stock_transfer_movements PRIMARY KEY (transfer_line_id, stock_movement_id),
        CONSTRAINT ck_stock_transfer_movements_sentido CHECK (direction IN ('Out', 'In')),
        CONSTRAINT fk_stock_transfer_movements_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_transfer_movements_tenant_id_branch_id_stock__4b2e1eb5 FOREIGN KEY (tenant_id, branch_id, stock_movement_id) REFERENCES inventory.stock_movements (tenant_id, branch_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_stock_transfer_movements_tenant_id_transfer_line_id FOREIGN KEY (tenant_id, transfer_line_id) REFERENCES inventory.stock_transfer_lines (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TABLE integration.api_key_scopes (
        api_key_id uuid NOT NULL,
        scope character varying(40) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_api_key_scopes PRIMARY KEY (api_key_id, scope),
        CONSTRAINT fk_api_key_scopes_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_api_key_scopes_tenant_id_api_key_id FOREIGN KEY (tenant_id, api_key_id) REFERENCES integration.api_keys (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TABLE integration.webhook_endpoints (
        id uuid NOT NULL,
        url character varying(500) NOT NULL,
        description character varying(200),
        created_by_user_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        is_active boolean NOT NULL,
        disabled_at timestamp with time zone,
        api_key_id uuid,
        branch_id uuid,
        secret_ciphertext character varying(500) NOT NULL,
        secret_key_id character varying(40) NOT NULL,
        secret_version integer NOT NULL,
        previous_secret_ciphertext character varying(500),
        previous_secret_key_id character varying(40),
        previous_secret_expires_at timestamp with time zone,
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_webhook_endpoints PRIMARY KEY (id),
        CONSTRAINT ak_webhook_endpoints_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_webhook_endpoints_baja CHECK (is_active = (disabled_at IS NULL)),
        CONSTRAINT ck_webhook_endpoints_rotacion CHECK ((previous_secret_ciphertext IS NULL) = (previous_secret_expires_at IS NULL) AND (previous_secret_ciphertext IS NULL) = (previous_secret_key_id IS NULL)),
        CONSTRAINT ck_webhook_endpoints_url CHECK (url ~ '^(https://|http://localhost|http://127\.0\.0\.1|http://\[::1\])'),
        CONSTRAINT ck_webhook_endpoints_version CHECK (secret_version >= 1),
        CONSTRAINT fk_webhook_endpoints_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_webhook_endpoints_tenant_id_api_key_id FOREIGN KEY (tenant_id, api_key_id) REFERENCES integration.api_keys (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_webhook_endpoints_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES warehouse.branches (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_webhook_endpoints_tenant_id_created_by_user_id FOREIGN KEY (tenant_id, created_by_user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TABLE integration.outbox_dispatch (
        outbox_event_id uuid NOT NULL,
        status character varying(20) NOT NULL,
        rounds integer NOT NULL,
        next_attempt_at timestamp with time zone NOT NULL,
        completed_at timestamp with time zone,
        last_error character varying(500),
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_outbox_dispatch PRIMARY KEY (outbox_event_id),
        CONSTRAINT ck_outbox_dispatch_estado CHECK (status IN ('Pending', 'Completed', 'Exhausted')),
        CONSTRAINT ck_outbox_dispatch_fin CHECK ((status = 'Pending') = (completed_at IS NULL)),
        CONSTRAINT ck_outbox_dispatch_rondas CHECK (rounds BETWEEN 0 AND 8),
        CONSTRAINT fk_outbox_dispatch_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_outbox_dispatch_tenant_id_outbox_event_id FOREIGN KEY (tenant_id, outbox_event_id) REFERENCES integration.outbox_events (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TABLE integration.webhook_deliveries (
        id uuid NOT NULL,
        outbox_event_id uuid NOT NULL,
        endpoint_id uuid NOT NULL,
        attempt integer NOT NULL,
        status_code integer,
        succeeded boolean NOT NULL,
        error character varying(500),
        attempted_at timestamp with time zone NOT NULL,
        duration_ms integer NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_webhook_deliveries PRIMARY KEY (id),
        CONSTRAINT ak_webhook_deliveries_tenant_id_id UNIQUE (tenant_id, id),
        CONSTRAINT ck_webhook_deliveries_duracion CHECK (duration_ms >= 0),
        CONSTRAINT ck_webhook_deliveries_intento CHECK (attempt BETWEEN 1 AND 8),
        CONSTRAINT fk_webhook_deliveries_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_webhook_deliveries_tenant_id_endpoint_id FOREIGN KEY (tenant_id, endpoint_id) REFERENCES integration.webhook_endpoints (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_webhook_deliveries_tenant_id_outbox_event_id FOREIGN KEY (tenant_id, outbox_event_id) REFERENCES integration.outbox_events (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TABLE integration.webhook_endpoint_events (
        endpoint_id uuid NOT NULL,
        event_type character varying(60) NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        created_by uuid,
        updated_at timestamp with time zone,
        updated_by uuid,
        tenant_id uuid NOT NULL,
        CONSTRAINT pk_webhook_endpoint_events PRIMARY KEY (endpoint_id, event_type),
        CONSTRAINT fk_webhook_endpoint_events_tenant_id FOREIGN KEY (tenant_id) REFERENCES iam.tenants (id) ON DELETE RESTRICT,
        CONSTRAINT fk_webhook_endpoint_events_tenant_id_endpoint_id FOREIGN KEY (tenant_id, endpoint_id) REFERENCES integration.webhook_endpoints (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    INSERT INTO iam.modules (id, code, created_at, created_by, description, monthly_fee_bs, name, setup_price_bs, updated_at, updated_by)
    VALUES ('01920000-0000-7000-8000-000000000006', 'CLOUD_HA', TIMESTAMPTZ '2026-09-25T00:00:00+00:00', NULL, 'PostgreSQL gestionado en la nube (AWS/DigitalOcean) con respaldos PITR, réplica y SLA 99,9 %.', 1500.0, 'Infraestructura Cloud HA', 12000.0, NULL, NULL);
    INSERT INTO iam.modules (id, code, created_at, created_by, description, monthly_fee_bs, name, setup_price_bs, updated_at, updated_by)
    VALUES ('01920000-0000-7000-8000-000000000007', 'MULTI_BRANCH', TIMESTAMPTZ '2026-09-25T00:00:00+00:00', NULL, 'Inventario aislado por sucursal (BranchId) y transferencias con mercancía en tránsito.', 500.0, 'Topología multi-sucursal', 8000.0, NULL, NULL);
    INSERT INTO iam.modules (id, code, created_at, created_by, description, monthly_fee_bs, name, setup_price_bs, updated_at, updated_by)
    VALUES ('01920000-0000-7000-8000-000000000008', 'API_INTEGRATIONS', TIMESTAMPTZ '2026-09-25T00:00:00+00:00', NULL, 'API Gateway con API Keys y webhooks para e-commerce (Shopify) y ERP contable.', 400.0, 'Integraciones API (B2B)', 6000.0, NULL, NULL);
    INSERT INTO iam.modules (id, code, created_at, created_by, description, monthly_fee_bs, name, setup_price_bs, updated_at, updated_by)
    VALUES ('01920000-0000-7000-8000-000000000009', 'GLOBAL_AUDIT', TIMESTAMPTZ '2026-09-25T00:00:00+00:00', NULL, 'Modelo de lectura desnormalizado para gerencia sin cargar las cajas POS.', 300.0, 'Auditoría global (réplicas de lectura)', 5000.0, NULL, NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_zones_tenant_id_branch_id_warehouse_id ON warehouse.zones (tenant_id, branch_id, warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_supplier_invoice_lines_tenant_id_branch_id_goods_re_33e178bb ON purchasing.supplier_invoice_lines (tenant_id, branch_id, goods_receipt_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_supplier_invoice_lines_tenant_id_branch_id_supplier_4d17bc14 ON purchasing.supplier_invoice_lines (tenant_id, branch_id, supplier_invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_transfers_tenant_id_from_branch_id_from_warehouse_id ON inventory.stock_transfers (tenant_id, from_branch_id, from_warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_transfers_tenant_id_requested_by_user_id ON inventory.stock_transfers (tenant_id, requested_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_transfers_tenant_id_status ON inventory.stock_transfers (tenant_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_transfers_tenant_id_to_branch_id_to_warehouse_id ON inventory.stock_transfers (tenant_id, to_branch_id, to_warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfers ADD CONSTRAINT ck_stock_transfers_despacho CHECK ((status IN ('Dispatched', 'Received')) = (dispatched_at IS NOT NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfers ADD CONSTRAINT ck_stock_transfers_estado CHECK (status IN ('Pending', 'Dispatched', 'Received', 'Cancelled'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfers ADD CONSTRAINT ck_stock_transfers_recepcion CHECK ((status = 'Received') = (received_at IS NOT NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_transfer_lines_tenant_id_from_branch_id_to_br_b04b8098 ON inventory.stock_transfer_lines (tenant_id, from_branch_id, to_branch_id, stock_transfer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE UNIQUE INDEX ux_stock_transfer_lines_stock_transfer_id_variant_id ON inventory.stock_transfer_lines (stock_transfer_id, variant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfer_lines ADD CONSTRAINT ck_stock_transfer_lines_costo CHECK (unit_cost IS NULL OR unit_cost >= 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_reservations_tenant_id_branch_id_pos_session_id ON inventory.stock_reservations (tenant_id, branch_id, pos_session_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_reservations_tenant_id_branch_id_sales_order_line_id ON inventory.stock_reservations (tenant_id, branch_id, sales_order_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_reservations_tenant_id_branch_id_stock_level_id ON inventory.stock_reservations (tenant_id, branch_id, stock_level_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_movements_tenant_id_branch_id_stock_level_id ON inventory.stock_movements (tenant_id, branch_id, stock_level_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_levels_tenant_id_branch_id_bin_id ON inventory.stock_levels (tenant_id, branch_id, bin_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_adjustments_tenant_id_branch_id_warehouse_id ON inventory.stock_adjustments (tenant_id, branch_id, warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_adjustment_lines_tenant_id_branch_id_stock_ad_62ee571f ON inventory.stock_adjustment_lines (tenant_id, branch_id, stock_adjustment_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_adjustment_lines_tenant_id_branch_id_stock_level_id ON inventory.stock_adjustment_lines (tenant_id, branch_id, stock_level_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_adjustment_lines_tenant_id_branch_id_stock_movement_id ON inventory.stock_adjustment_lines (tenant_id, branch_id, stock_movement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_shelves_tenant_id_branch_id_rack_id ON warehouse.shelves (tenant_id, branch_id, rack_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_sessions_tenant_id_active_branch_id ON iam.sessions (tenant_id, active_branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE UNIQUE INDEX ux_sessions_token_hash ON iam.sessions (token_hash) WHERE token_hash IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE iam.sessions ADD CONSTRAINT ck_sessions_token CHECK ((token_hash IS NULL) = (expires_at IS NULL) AND (token_hash IS NULL OR token_hash ~ '^[0-9a-f]{64}$'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_sales_orders_tenant_id_branch_id_pos_session_id ON sales.sales_orders (tenant_id, branch_id, pos_session_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_sales_orders_tenant_id_branch_id_warehouse_id ON sales.sales_orders (tenant_id, branch_id, warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_sales_order_lines_tenant_id_branch_id_sales_order_id ON sales.sales_order_lines (tenant_id, branch_id, sales_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_sales_order_lines_tenant_id_branch_id_stock_movement_id ON sales.sales_order_lines (tenant_id, branch_id, stock_movement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_racks_tenant_id_branch_id_aisle_id ON warehouse.racks (tenant_id, branch_id, aisle_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_purchase_return_lines_tenant_id_branch_id_goods_rec_5503cf33 ON purchasing.purchase_return_lines (tenant_id, branch_id, goods_receipt_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_purchase_return_lines_tenant_id_branch_id_purchase_return_id ON purchasing.purchase_return_lines (tenant_id, branch_id, purchase_return_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_purchase_return_lines_tenant_id_branch_id_stock_level_id ON purchasing.purchase_return_lines (tenant_id, branch_id, stock_level_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_purchase_return_lines_tenant_id_branch_id_stock_movement_id ON purchasing.purchase_return_lines (tenant_id, branch_id, stock_movement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_purchase_orders_tenant_id_branch_id_warehouse_id ON purchasing.purchase_orders (tenant_id, branch_id, warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_purchase_order_lines_tenant_id_branch_id_purchase_order_id ON purchasing.purchase_order_lines (tenant_id, branch_id, purchase_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_product_stock_policies_tenant_id_branch_id_warehouse_id ON catalog.product_stock_policies (tenant_id, branch_id, warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_pos_sessions_tenant_id_branch_id_pos_register_id ON sales.pos_sessions (tenant_id, branch_id, pos_register_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_pos_registers_tenant_id_branch_id_warehouse_id ON sales.pos_registers (tenant_id, branch_id, warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_physical_counts_tenant_id_branch_id_warehouse_id ON inventory.physical_counts (tenant_id, branch_id, warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_physical_count_lines_tenant_id_branch_id_physical_count_id ON inventory.physical_count_lines (tenant_id, branch_id, physical_count_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_physical_count_lines_tenant_id_branch_id_stock_level_id ON inventory.physical_count_lines (tenant_id, branch_id, stock_level_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_physical_count_lines_tenant_id_branch_id_stock_movement_id ON inventory.physical_count_lines (tenant_id, branch_id, stock_movement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_payments_tenant_id_branch_id_invoice_id ON sales.payments (tenant_id, branch_id, invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_payments_tenant_id_branch_id_pos_session_id ON sales.payments (tenant_id, branch_id, pos_session_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_journal_lines_tenant_id_branch_id_journal_entry_id ON accounting.journal_lines (tenant_id, branch_id, journal_entry_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_invoices_tenant_id_branch_id_sales_order_id ON sales.invoices (tenant_id, branch_id, sales_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_invoice_lines_tenant_id_branch_id_invoice_id ON sales.invoice_lines (tenant_id, branch_id, invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_invoice_lines_tenant_id_branch_id_sales_order_line_id ON sales.invoice_lines (tenant_id, branch_id, sales_order_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_goods_receipts_tenant_id_branch_id_purchase_order_id ON purchasing.goods_receipts (tenant_id, branch_id, purchase_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_goods_receipts_tenant_id_branch_id_warehouse_id ON purchasing.goods_receipts (tenant_id, branch_id, warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_goods_receipt_lines_tenant_id_branch_id_goods_receipt_id ON purchasing.goods_receipt_lines (tenant_id, branch_id, goods_receipt_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_goods_receipt_lines_tenant_id_branch_id_purchase_or_92e74ade ON purchasing.goods_receipt_lines (tenant_id, branch_id, purchase_order_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_goods_receipt_lines_tenant_id_branch_id_stock_level_id ON purchasing.goods_receipt_lines (tenant_id, branch_id, stock_level_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_goods_receipt_lines_tenant_id_branch_id_stock_movement_id ON purchasing.goods_receipt_lines (tenant_id, branch_id, stock_movement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_cash_movements_tenant_id_branch_id_pos_session_id ON sales.cash_movements (tenant_id, branch_id, pos_session_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_bins_tenant_id_branch_id_shelf_id ON warehouse.bins (tenant_id, branch_id, shelf_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_bin_assignments_tenant_id_branch_id_bin_id ON warehouse.bin_assignments (tenant_id, branch_id, bin_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_average_cost_history_tenant_id_branch_id_stock_movement_id ON accounting.average_cost_history (tenant_id, branch_id, stock_movement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_average_cost_history_tenant_id_branch_id_warehouse_id ON accounting.average_cost_history (tenant_id, branch_id, warehouse_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE UNIQUE INDEX ux_average_cost_history_tenant_id_variant_id_warehouse_f2e7f7b5 ON accounting.average_cost_history (tenant_id, variant_id, warehouse_id, sequence);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE accounting.average_cost_history ADD CONSTRAINT ck_average_cost_history_secuencia CHECK (sequence >= 1);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_audit_logs_tenant_id_api_key_id_occurred_at ON iam.audit_logs (tenant_id, api_key_id, occurred_at) WHERE api_key_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_audit_logs_tenant_id_branch_id ON iam.audit_logs (tenant_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE iam.audit_logs ADD CONSTRAINT ck_audit_logs_canal CHECK (channel IS NULL OR channel IN ('desktop', 'cloud', 'api'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_aisles_tenant_id_branch_id_zone_id ON warehouse.aisles (tenant_id, branch_id, zone_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_api_key_scopes_tenant_id_api_key_id ON integration.api_key_scopes (tenant_id, api_key_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_api_keys_tenant_id_branch_id ON integration.api_keys (tenant_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_api_keys_tenant_id_name ON integration.api_keys (tenant_id, name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_api_keys_tenant_id_owner_user_id ON integration.api_keys (tenant_id, owner_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE UNIQUE INDEX ux_api_keys_prefix ON integration.api_keys (prefix);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_external_orders_tenant_id_branch_id_sales_order_id ON sales.external_orders (tenant_id, branch_id, sales_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE UNIQUE INDEX ux_external_orders_sales_order_id ON sales.external_orders (sales_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE UNIQUE INDEX ux_external_orders_tenant_id_channel_external_id ON sales.external_orders (tenant_id, channel, external_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_outbox_dispatch_next_attempt_at ON integration.outbox_dispatch (next_attempt_at) WHERE status = 'Pending';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE UNIQUE INDEX ux_outbox_dispatch_tenant_id_outbox_event_id ON integration.outbox_dispatch (tenant_id, outbox_event_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_outbox_events_tenant_id_branch_id ON integration.outbox_events (tenant_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_outbox_events_tenant_id_occurred_at ON integration.outbox_events (tenant_id, occurred_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_processed_requests_processed_at ON iam.processed_requests (processed_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_processed_requests_tenant_id_user_id ON iam.processed_requests (tenant_id, user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE UNIQUE INDEX ux_processed_requests_tenant_id_request_id ON iam.processed_requests (tenant_id, request_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_transfer_discrepancies_tenant_id_from_branch__ab9c7114 ON inventory.stock_transfer_discrepancies (tenant_id, from_branch_id, to_branch_id, transfer_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_transfer_discrepancies_tenant_id_recorded_by_user_id ON inventory.stock_transfer_discrepancies (tenant_id, recorded_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_transfer_events_tenant_id_from_branch_id_to_b_ed8553a1 ON inventory.stock_transfer_events (tenant_id, from_branch_id, to_branch_id, transfer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_transfer_events_tenant_id_user_id ON inventory.stock_transfer_events (tenant_id, user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_transfer_events_transfer_id_occurred_at ON inventory.stock_transfer_events (transfer_id, occurred_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_transfer_line_batches_tenant_id_batch_id ON inventory.stock_transfer_line_batches (tenant_id, batch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_transfer_line_batches_tenant_id_from_branch_i_d73f9652 ON inventory.stock_transfer_line_batches (tenant_id, from_branch_id, to_branch_id, transfer_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_transfer_movements_tenant_id_branch_id_stock__76276875 ON inventory.stock_transfer_movements (tenant_id, branch_id, stock_movement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_stock_transfer_movements_tenant_id_transfer_line_id ON inventory.stock_transfer_movements (tenant_id, transfer_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE UNIQUE INDEX ux_stock_transfer_movements_stock_movement_id ON inventory.stock_transfer_movements (stock_movement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_webhook_deliveries_tenant_id_endpoint_id ON integration.webhook_deliveries (tenant_id, endpoint_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_webhook_deliveries_tenant_id_outbox_event_id ON integration.webhook_deliveries (tenant_id, outbox_event_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE UNIQUE INDEX ux_webhook_deliveries_outbox_event_id_endpoint_id_attempt ON integration.webhook_deliveries (outbox_event_id, endpoint_id, attempt);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_webhook_endpoint_events_tenant_id_endpoint_id ON integration.webhook_endpoint_events (tenant_id, endpoint_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_webhook_endpoints_tenant_id_api_key_id ON integration.webhook_endpoints (tenant_id, api_key_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_webhook_endpoints_tenant_id_branch_id ON integration.webhook_endpoints (tenant_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE INDEX ix_webhook_endpoints_tenant_id_created_by_user_id ON integration.webhook_endpoints (tenant_id, created_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.aisles ADD CONSTRAINT fk_aisles_tenant_id_branch_id_zone_id FOREIGN KEY (tenant_id, branch_id, zone_id) REFERENCES warehouse.zones (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE iam.audit_logs ADD CONSTRAINT fk_audit_logs_tenant_id_api_key_id FOREIGN KEY (tenant_id, api_key_id) REFERENCES integration.api_keys (tenant_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE iam.audit_logs ADD CONSTRAINT fk_audit_logs_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES warehouse.branches (tenant_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE accounting.average_cost_history ADD CONSTRAINT fk_average_cost_history_tenant_id_branch_id_stock_movement_id FOREIGN KEY (tenant_id, branch_id, stock_movement_id) REFERENCES inventory.stock_movements (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE accounting.average_cost_history ADD CONSTRAINT fk_average_cost_history_tenant_id_branch_id_warehouse_id FOREIGN KEY (tenant_id, branch_id, warehouse_id) REFERENCES warehouse.warehouses (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.bin_assignments ADD CONSTRAINT fk_bin_assignments_tenant_id_branch_id_bin_id FOREIGN KEY (tenant_id, branch_id, bin_id) REFERENCES warehouse.bins (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.bins ADD CONSTRAINT fk_bins_tenant_id_branch_id_shelf_id FOREIGN KEY (tenant_id, branch_id, shelf_id) REFERENCES warehouse.shelves (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.cash_movements ADD CONSTRAINT fk_cash_movements_tenant_id_branch_id_pos_session_id FOREIGN KEY (tenant_id, branch_id, pos_session_id) REFERENCES sales.pos_sessions (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.goods_receipt_lines ADD CONSTRAINT fk_goods_receipt_lines_tenant_id_branch_id_goods_receipt_id FOREIGN KEY (tenant_id, branch_id, goods_receipt_id) REFERENCES purchasing.goods_receipts (tenant_id, branch_id, id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.goods_receipt_lines ADD CONSTRAINT fk_goods_receipt_lines_tenant_id_branch_id_purchase_or_a9a4baae FOREIGN KEY (tenant_id, branch_id, purchase_order_line_id) REFERENCES purchasing.purchase_order_lines (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.goods_receipt_lines ADD CONSTRAINT fk_goods_receipt_lines_tenant_id_branch_id_stock_level_id FOREIGN KEY (tenant_id, branch_id, stock_level_id) REFERENCES inventory.stock_levels (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.goods_receipt_lines ADD CONSTRAINT fk_goods_receipt_lines_tenant_id_branch_id_stock_movement_id FOREIGN KEY (tenant_id, branch_id, stock_movement_id) REFERENCES inventory.stock_movements (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.goods_receipts ADD CONSTRAINT fk_goods_receipts_tenant_id_branch_id_purchase_order_id FOREIGN KEY (tenant_id, branch_id, purchase_order_id) REFERENCES purchasing.purchase_orders (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.goods_receipts ADD CONSTRAINT fk_goods_receipts_tenant_id_branch_id_warehouse_id FOREIGN KEY (tenant_id, branch_id, warehouse_id) REFERENCES warehouse.warehouses (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.invoice_lines ADD CONSTRAINT fk_invoice_lines_tenant_id_branch_id_invoice_id FOREIGN KEY (tenant_id, branch_id, invoice_id) REFERENCES sales.invoices (tenant_id, branch_id, id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.invoice_lines ADD CONSTRAINT fk_invoice_lines_tenant_id_branch_id_sales_order_line_id FOREIGN KEY (tenant_id, branch_id, sales_order_line_id) REFERENCES sales.sales_order_lines (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.invoices ADD CONSTRAINT fk_invoices_tenant_id_branch_id_sales_order_id FOREIGN KEY (tenant_id, branch_id, sales_order_id) REFERENCES sales.sales_orders (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE accounting.journal_lines ADD CONSTRAINT fk_journal_lines_tenant_id_branch_id_journal_entry_id FOREIGN KEY (tenant_id, branch_id, journal_entry_id) REFERENCES accounting.journal_entries (tenant_id, branch_id, id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.payments ADD CONSTRAINT fk_payments_tenant_id_branch_id_invoice_id FOREIGN KEY (tenant_id, branch_id, invoice_id) REFERENCES sales.invoices (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.payments ADD CONSTRAINT fk_payments_tenant_id_branch_id_pos_session_id FOREIGN KEY (tenant_id, branch_id, pos_session_id) REFERENCES sales.pos_sessions (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.physical_count_lines ADD CONSTRAINT fk_physical_count_lines_tenant_id_branch_id_physical_count_id FOREIGN KEY (tenant_id, branch_id, physical_count_id) REFERENCES inventory.physical_counts (tenant_id, branch_id, id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.physical_count_lines ADD CONSTRAINT fk_physical_count_lines_tenant_id_branch_id_stock_level_id FOREIGN KEY (tenant_id, branch_id, stock_level_id) REFERENCES inventory.stock_levels (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.physical_count_lines ADD CONSTRAINT fk_physical_count_lines_tenant_id_branch_id_stock_movement_id FOREIGN KEY (tenant_id, branch_id, stock_movement_id) REFERENCES inventory.stock_movements (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.physical_counts ADD CONSTRAINT fk_physical_counts_tenant_id_branch_id_warehouse_id FOREIGN KEY (tenant_id, branch_id, warehouse_id) REFERENCES warehouse.warehouses (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.pos_registers ADD CONSTRAINT fk_pos_registers_tenant_id_branch_id_warehouse_id FOREIGN KEY (tenant_id, branch_id, warehouse_id) REFERENCES warehouse.warehouses (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.pos_sessions ADD CONSTRAINT fk_pos_sessions_tenant_id_branch_id_pos_register_id FOREIGN KEY (tenant_id, branch_id, pos_register_id) REFERENCES sales.pos_registers (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE catalog.product_stock_policies ADD CONSTRAINT fk_product_stock_policies_tenant_id_branch_id_warehouse_id FOREIGN KEY (tenant_id, branch_id, warehouse_id) REFERENCES warehouse.warehouses (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.purchase_order_lines ADD CONSTRAINT fk_purchase_order_lines_tenant_id_branch_id_purchase_order_id FOREIGN KEY (tenant_id, branch_id, purchase_order_id) REFERENCES purchasing.purchase_orders (tenant_id, branch_id, id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.purchase_orders ADD CONSTRAINT fk_purchase_orders_tenant_id_branch_id_warehouse_id FOREIGN KEY (tenant_id, branch_id, warehouse_id) REFERENCES warehouse.warehouses (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.purchase_return_lines ADD CONSTRAINT fk_purchase_return_lines_tenant_id_branch_id_goods_rec_79ef70b8 FOREIGN KEY (tenant_id, branch_id, goods_receipt_line_id) REFERENCES purchasing.goods_receipt_lines (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.purchase_return_lines ADD CONSTRAINT fk_purchase_return_lines_tenant_id_branch_id_purchase_return_id FOREIGN KEY (tenant_id, branch_id, purchase_return_id) REFERENCES purchasing.purchase_returns (tenant_id, branch_id, id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.purchase_return_lines ADD CONSTRAINT fk_purchase_return_lines_tenant_id_branch_id_stock_level_id FOREIGN KEY (tenant_id, branch_id, stock_level_id) REFERENCES inventory.stock_levels (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.purchase_return_lines ADD CONSTRAINT fk_purchase_return_lines_tenant_id_branch_id_stock_movement_id FOREIGN KEY (tenant_id, branch_id, stock_movement_id) REFERENCES inventory.stock_movements (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.racks ADD CONSTRAINT fk_racks_tenant_id_branch_id_aisle_id FOREIGN KEY (tenant_id, branch_id, aisle_id) REFERENCES warehouse.aisles (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.sales_order_lines ADD CONSTRAINT fk_sales_order_lines_tenant_id_branch_id_sales_order_id FOREIGN KEY (tenant_id, branch_id, sales_order_id) REFERENCES sales.sales_orders (tenant_id, branch_id, id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.sales_order_lines ADD CONSTRAINT fk_sales_order_lines_tenant_id_branch_id_stock_movement_id FOREIGN KEY (tenant_id, branch_id, stock_movement_id) REFERENCES inventory.stock_movements (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.sales_orders ADD CONSTRAINT fk_sales_orders_tenant_id_branch_id_pos_session_id FOREIGN KEY (tenant_id, branch_id, pos_session_id) REFERENCES sales.pos_sessions (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE sales.sales_orders ADD CONSTRAINT fk_sales_orders_tenant_id_branch_id_warehouse_id FOREIGN KEY (tenant_id, branch_id, warehouse_id) REFERENCES warehouse.warehouses (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE iam.sessions ADD CONSTRAINT fk_sessions_tenant_id_active_branch_id FOREIGN KEY (tenant_id, active_branch_id) REFERENCES warehouse.branches (tenant_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.shelves ADD CONSTRAINT fk_shelves_tenant_id_branch_id_rack_id FOREIGN KEY (tenant_id, branch_id, rack_id) REFERENCES warehouse.racks (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_adjustment_lines ADD CONSTRAINT fk_stock_adjustment_lines_tenant_id_branch_id_stock_ad_202e465f FOREIGN KEY (tenant_id, branch_id, stock_adjustment_id) REFERENCES inventory.stock_adjustments (tenant_id, branch_id, id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_adjustment_lines ADD CONSTRAINT fk_stock_adjustment_lines_tenant_id_branch_id_stock_level_id FOREIGN KEY (tenant_id, branch_id, stock_level_id) REFERENCES inventory.stock_levels (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_adjustment_lines ADD CONSTRAINT fk_stock_adjustment_lines_tenant_id_branch_id_stock_movement_id FOREIGN KEY (tenant_id, branch_id, stock_movement_id) REFERENCES inventory.stock_movements (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_adjustments ADD CONSTRAINT fk_stock_adjustments_tenant_id_branch_id_warehouse_id FOREIGN KEY (tenant_id, branch_id, warehouse_id) REFERENCES warehouse.warehouses (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_levels ADD CONSTRAINT fk_stock_levels_tenant_id_branch_id_bin_id FOREIGN KEY (tenant_id, branch_id, bin_id) REFERENCES warehouse.bins (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_movements ADD CONSTRAINT fk_stock_movements_tenant_id_branch_id_stock_level_id FOREIGN KEY (tenant_id, branch_id, stock_level_id) REFERENCES inventory.stock_levels (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_reservations ADD CONSTRAINT fk_stock_reservations_tenant_id_branch_id_pos_session_id FOREIGN KEY (tenant_id, branch_id, pos_session_id) REFERENCES sales.pos_sessions (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_reservations ADD CONSTRAINT fk_stock_reservations_tenant_id_branch_id_sales_order_line_id FOREIGN KEY (tenant_id, branch_id, sales_order_line_id) REFERENCES sales.sales_order_lines (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_reservations ADD CONSTRAINT fk_stock_reservations_tenant_id_branch_id_stock_level_id FOREIGN KEY (tenant_id, branch_id, stock_level_id) REFERENCES inventory.stock_levels (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfer_lines ADD CONSTRAINT fk_stock_transfer_lines_tenant_id_from_branch_id_to_br_8ab834cf FOREIGN KEY (tenant_id, from_branch_id, to_branch_id, stock_transfer_id) REFERENCES inventory.stock_transfers (tenant_id, from_branch_id, to_branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfer_lines ADD CONSTRAINT fk_stock_transfer_lines_tenant_id_variant_id FOREIGN KEY (tenant_id, variant_id) REFERENCES catalog.product_variants (tenant_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfers ADD CONSTRAINT fk_stock_transfers_tenant_id_from_branch_id_from_warehouse_id FOREIGN KEY (tenant_id, from_branch_id, from_warehouse_id) REFERENCES warehouse.warehouses (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfers ADD CONSTRAINT fk_stock_transfers_tenant_id_requested_by_user_id FOREIGN KEY (tenant_id, requested_by_user_id) REFERENCES iam.users (tenant_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE inventory.stock_transfers ADD CONSTRAINT fk_stock_transfers_tenant_id_to_branch_id_to_warehouse_id FOREIGN KEY (tenant_id, to_branch_id, to_warehouse_id) REFERENCES warehouse.warehouses (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.supplier_invoice_lines ADD CONSTRAINT fk_supplier_invoice_lines_tenant_id_branch_id_goods_re_507ecc30 FOREIGN KEY (tenant_id, branch_id, goods_receipt_line_id) REFERENCES purchasing.goods_receipt_lines (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE purchasing.supplier_invoice_lines ADD CONSTRAINT fk_supplier_invoice_lines_tenant_id_branch_id_supplier_47722964 FOREIGN KEY (tenant_id, branch_id, supplier_invoice_id) REFERENCES purchasing.supplier_invoices (tenant_id, branch_id, id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    ALTER TABLE warehouse.zones ADD CONSTRAINT fk_zones_tenant_id_branch_id_warehouse_id FOREIGN KEY (tenant_id, branch_id, warehouse_id) REFERENCES warehouse.warehouses (tenant_id, branch_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON inventory.stock_transfer_movements
        FOR EACH ROW EXECUTE FUNCTION iam.minv_append_only();
    CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON inventory.stock_transfer_movements
        FOR EACH STATEMENT EXECUTE FUNCTION iam.minv_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON inventory.stock_transfer_discrepancies
        FOR EACH ROW EXECUTE FUNCTION iam.minv_append_only();
    CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON inventory.stock_transfer_discrepancies
        FOR EACH STATEMENT EXECUTE FUNCTION iam.minv_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON inventory.stock_transfer_events
        FOR EACH ROW EXECUTE FUNCTION iam.minv_append_only();
    CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON inventory.stock_transfer_events
        FOR EACH STATEMENT EXECUTE FUNCTION iam.minv_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON inventory.stock_transfer_line_batches
        FOR EACH ROW EXECUTE FUNCTION iam.minv_append_only();
    CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON inventory.stock_transfer_line_batches
        FOR EACH STATEMENT EXECUTE FUNCTION iam.minv_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON integration.outbox_events
        FOR EACH ROW EXECUTE FUNCTION iam.minv_append_only();
    CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON integration.outbox_events
        FOR EACH STATEMENT EXECUTE FUNCTION iam.minv_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON integration.webhook_deliveries
        FOR EACH ROW EXECUTE FUNCTION iam.minv_append_only();
    CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON integration.webhook_deliveries
        FOR EACH STATEMENT EXECUTE FUNCTION iam.minv_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON sales.external_orders
        FOR EACH ROW EXECUTE FUNCTION iam.minv_append_only();
    CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON sales.external_orders
        FOR EACH STATEMENT EXECUTE FUNCTION iam.minv_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON iam.processed_requests
        FOR EACH ROW EXECUTE FUNCTION iam.minv_append_only();
    CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON iam.processed_requests
        FOR EACH STATEMENT EXECUTE FUNCTION iam.minv_append_only();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE OR REPLACE FUNCTION iam.branch_visible(p_branch uuid) RETURNS boolean
    LANGUAGE sql STABLE AS $$
        SELECT CASE coalesce(current_setting('minv.branch_ids', true), '')
                   WHEN '*' THEN true
                   WHEN '' THEN false
                   ELSE p_branch = ANY (string_to_array(current_setting('minv.branch_ids', true), ',')::uuid[])
               END
    $$;
    DO $$
    DECLARE r record;
    BEGIN
        FOR r IN
            SELECT c.table_schema, c.table_name
            FROM information_schema.columns c
            JOIN information_schema.tables t
              ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE'
            WHERE c.column_name = 'tenant_id' AND c.table_schema IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration')
        LOOP
            EXECUTE format('ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY', r.table_schema, r.table_name);
            EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I.%I', r.table_schema, r.table_name);
            EXECUTE format('CREATE POLICY tenant_isolation ON %I.%I USING (tenant_id = iam.current_tenant_id()) '
                           'WITH CHECK (tenant_id = iam.current_tenant_id())', r.table_schema, r.table_name);
        END LOOP;
    END;
    $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON warehouse.zones AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON warehouse.aisles AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON warehouse.racks AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON warehouse.shelves AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON warehouse.bins AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON warehouse.bin_assignments AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON catalog.product_stock_policies AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON inventory.stock_levels AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON inventory.stock_movements AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON inventory.stock_reservations AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON inventory.stock_adjustments AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON inventory.stock_adjustment_lines AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON inventory.physical_counts AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON inventory.physical_count_lines AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON inventory.stock_transfer_movements AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON purchasing.purchase_orders AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON purchasing.purchase_order_lines AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON purchasing.goods_receipts AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON purchasing.goods_receipt_lines AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON purchasing.purchase_returns AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON purchasing.purchase_return_lines AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON purchasing.supplier_invoices AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON purchasing.supplier_invoice_lines AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON sales.pos_registers AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON sales.pos_sessions AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON sales.cash_movements AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON sales.sales_orders AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON sales.sales_order_lines AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON sales.invoices AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON sales.invoice_lines AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON sales.payments AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON sales.external_orders AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON accounting.journal_entries AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON accounting.journal_lines AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON accounting.average_cost_history AS RESTRICTIVE
        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON inventory.stock_transfers AS RESTRICTIVE
        USING (iam.branch_visible(from_branch_id) OR iam.branch_visible(to_branch_id))
        WITH CHECK (iam.branch_visible(from_branch_id) OR iam.branch_visible(to_branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON inventory.stock_transfer_lines AS RESTRICTIVE
        USING (iam.branch_visible(from_branch_id) OR iam.branch_visible(to_branch_id))
        WITH CHECK (iam.branch_visible(from_branch_id) OR iam.branch_visible(to_branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON inventory.stock_transfer_line_batches AS RESTRICTIVE
        USING (iam.branch_visible(from_branch_id) OR iam.branch_visible(to_branch_id))
        WITH CHECK (iam.branch_visible(from_branch_id) OR iam.branch_visible(to_branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON inventory.stock_transfer_discrepancies AS RESTRICTIVE
        USING (iam.branch_visible(from_branch_id) OR iam.branch_visible(to_branch_id))
        WITH CHECK (iam.branch_visible(from_branch_id) OR iam.branch_visible(to_branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE POLICY branch_isolation ON inventory.stock_transfer_events AS RESTRICTIVE
        USING (iam.branch_visible(from_branch_id) OR iam.branch_visible(to_branch_id))
        WITH CHECK (iam.branch_visible(from_branch_id) OR iam.branch_visible(to_branch_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE OR REPLACE FUNCTION integration.resolve_api_key(p_prefix text)
    RETURNS TABLE (api_key_id uuid, tenant_id uuid, token_hash text, owner_user_id uuid, branch_id uuid,
                   expires_at timestamptz, revoked_at timestamptz)
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, integration AS $$
        SELECT k.id, k.tenant_id, k.token_hash, k.owner_user_id, k.branch_id, k.expires_at, k.revoked_at
        FROM integration.api_keys k
        WHERE k.prefix = p_prefix
    $$;

    CREATE OR REPLACE FUNCTION iam.resolve_session(p_token_hash text)
    RETURNS TABLE (session_id uuid, tenant_id uuid, user_id uuid, active_branch_id uuid, expires_at timestamptz, ended_at timestamptz)
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, iam AS $$
        SELECT s.id, s.tenant_id, s.user_id, s.active_branch_id, s.expires_at, s.ended_at
        FROM iam.sessions s
        WHERE s.token_hash = p_token_hash
    $$;

    CREATE OR REPLACE FUNCTION integration.claim_deliveries(p_limit integer, p_lease_seconds integer)
    RETURNS TABLE (tenant_id uuid, outbox_event_id uuid)
    LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, integration AS $$
    #variable_conflict use_column
    BEGIN
        RETURN QUERY
        WITH due AS (
            SELECT d.outbox_event_id AS id
            FROM integration.outbox_dispatch d
            WHERE d.status = 'Pending' AND d.next_attempt_at <= now()
            ORDER BY d.next_attempt_at
            LIMIT greatest(1, least(p_limit, 500))
            FOR UPDATE SKIP LOCKED)
        UPDATE integration.outbox_dispatch d
           SET next_attempt_at = now() + make_interval(secs => greatest(30, least(p_lease_seconds, 3600)))
        FROM due
        WHERE d.outbox_event_id = due.id
        RETURNING d.tenant_id, d.outbox_event_id;
    END;
    $$;

    REVOKE ALL ON FUNCTION integration.resolve_api_key(text) FROM PUBLIC;
    REVOKE ALL ON FUNCTION iam.resolve_session(text) FROM PUBLIC;
    REVOKE ALL ON FUNCTION integration.claim_deliveries(integer, integer) FROM PUBLIC;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    CREATE SCHEMA IF NOT EXISTS reporting;

    CREATE MATERIALIZED VIEW reporting.mv_branch_stock AS
    SELECT l.tenant_id, l.branch_id, b.variant_id,
           sum(l.quantity_on_hand) AS on_hand,
           sum(l.quantity_reserved) AS reserved,
           sum(l.quantity_on_hand * coalesce(c.average_cost, 0)) AS value,
           now() AS refreshed_at
    FROM inventory.stock_levels l
    JOIN inventory.batches b ON b.id = l.batch_id
    JOIN warehouse.bins bi ON bi.id = l.bin_id
    JOIN warehouse.shelves sh ON sh.id = bi.shelf_id
    JOIN warehouse.racks r ON r.id = sh.rack_id
    JOIN warehouse.aisles a ON a.id = r.aisle_id
    JOIN warehouse.zones z ON z.id = a.zone_id
    LEFT JOIN LATERAL (
        SELECT h.average_cost FROM accounting.average_cost_history h
        WHERE h.tenant_id = l.tenant_id AND h.variant_id = b.variant_id AND h.warehouse_id = z.warehouse_id
        ORDER BY h.sequence DESC LIMIT 1) c ON true
    GROUP BY l.tenant_id, l.branch_id, b.variant_id;
    CREATE UNIQUE INDEX ux_mv_branch_stock ON reporting.mv_branch_stock (tenant_id, branch_id, variant_id);

    CREATE MATERIALIZED VIEW reporting.mv_branch_daily_sales AS
    SELECT i.tenant_id, i.branch_id, (i.issued_at AT TIME ZONE c.time_zone_id)::date AS day,
           count(*)::int AS tickets,
           coalesce(sum(p.amount), 0) AS revenue,
           coalesce(sum(t.tax), 0) AS tax,
           now() AS refreshed_at
    FROM sales.invoices i
    JOIN iam.tenant_configs c ON c.tenant_id = i.tenant_id
    LEFT JOIN LATERAL (SELECT sum(x.amount) AS amount FROM sales.payments x WHERE x.invoice_id = i.id) p ON true
    LEFT JOIN LATERAL (SELECT sum(x.tax_amount) AS tax FROM sales.invoice_lines x WHERE x.invoice_id = i.id) t ON true
    WHERE i.status = 'Issued'
    GROUP BY i.tenant_id, i.branch_id, (i.issued_at AT TIME ZONE c.time_zone_id)::date;
    CREATE UNIQUE INDEX ux_mv_branch_daily_sales ON reporting.mv_branch_daily_sales (tenant_id, branch_id, day);

    -- Las vistas materializadas no admiten RLS: solo se exponen estas vistas filtradas (security_barrier evita que un
    -- predicado del usuario vea filas antes del filtro)
    CREATE VIEW reporting.v_branch_stock WITH (security_barrier = true) AS
    SELECT * FROM reporting.mv_branch_stock
    WHERE tenant_id = iam.current_tenant_id() AND iam.branch_visible(branch_id);

    CREATE VIEW reporting.v_branch_daily_sales WITH (security_barrier = true) AS
    SELECT * FROM reporting.mv_branch_daily_sales
    WHERE tenant_id = iam.current_tenant_id() AND iam.branch_visible(branch_id);

    CREATE OR REPLACE FUNCTION reporting.refresh_all() RETURNS boolean
    LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, reporting AS $$
    BEGIN
        IF NOT pg_try_advisory_xact_lock(hashtext('minv.reporting.refresh')) THEN
            RETURN false;   -- otra réplica ya está refrescando
        END IF;
        REFRESH MATERIALIZED VIEW CONCURRENTLY reporting.mv_branch_stock;
        REFRESH MATERIALIZED VIEW CONCURRENTLY reporting.mv_branch_daily_sales;
        RETURN true;
    END;
    $$;
    REVOKE ALL ON FUNCTION reporting.refresh_all() FROM PUBLIC;

    -- Conservación de las transferencias: despachado = cantidad; recibido + faltante = cantidad
    CREATE OR REPLACE VIEW inventory.v_transfer_breaches WITH (security_invoker = true) AS
    SELECT t.tenant_id, t.id AS transfer_id, t.number, t.status, l.id AS line_id, l.quantity,
           coalesce(o.sent, 0) AS sent, coalesce(m.shipped, 0) AS shipped, coalesce(i.received, 0) AS received,
           coalesce(d.shortage, 0) AS shortage
    FROM inventory.stock_transfers t
    JOIN inventory.stock_transfer_lines l ON l.stock_transfer_id = t.id
    LEFT JOIN LATERAL (SELECT sum(x.quantity) AS shipped FROM inventory.stock_transfer_line_batches x WHERE x.transfer_line_id = l.id) m ON true
    LEFT JOIN LATERAL (SELECT sum(x.quantity) AS shortage FROM inventory.stock_transfer_discrepancies x WHERE x.transfer_line_id = l.id) d ON true
    LEFT JOIN LATERAL (SELECT sum(sm.quantity) AS sent FROM inventory.stock_transfer_movements tm
                       JOIN inventory.stock_movements sm ON sm.id = tm.stock_movement_id
                       WHERE tm.transfer_line_id = l.id AND tm.direction = 'Out') o ON true
    LEFT JOIN LATERAL (SELECT sum(sm.quantity) AS received FROM inventory.stock_transfer_movements tm
                       JOIN inventory.stock_movements sm ON sm.id = tm.stock_movement_id
                       WHERE tm.transfer_line_id = l.id AND tm.direction = 'In') i ON true
    WHERE (t.status IN ('Dispatched', 'Received') AND (coalesce(o.sent, 0) <> l.quantity OR coalesce(m.shipped, 0) <> l.quantity))
       OR (t.status = 'Received' AND coalesce(i.received, 0) + coalesce(d.shortage, 0) <> l.quantity)
       OR (t.status IN ('Pending', 'Cancelled') AND (o.sent IS NOT NULL OR m.shipped IS NOT NULL OR i.received IS NOT NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    INSERT INTO iam.permissions (id, tenant_id, code, description)
    SELECT gen_random_uuid(), t.id, p.code, p.description
    FROM iam.tenants t
    CROSS JOIN (VALUES
        ('corporate.branches.all', 'Ver y operar todas las sucursales (gerencia global)'),
        ('corporate.branches.manage', 'Crear y modificar sucursales y asignar usuarios a sucursales'),
        ('inventory.transfers.manage', 'Crear, despachar y recibir transferencias entre sucursales'),
        ('integration.manage', 'Administrar API Keys y webhooks de integración B2B')) AS p(code, description)
    WHERE NOT EXISTS (SELECT 1 FROM iam.permissions x WHERE x.tenant_id = t.id AND x.code = p.code);

    INSERT INTO iam.role_permissions (tenant_id, role_id, permission_id)
    SELECT r.tenant_id, r.id, p.id
    FROM iam.roles r
    JOIN (VALUES ('ADMIN', 'corporate.branches.all'), ('ADMIN', 'corporate.branches.manage'), ('ADMIN', 'inventory.transfers.manage'),
                 ('ADMIN', 'integration.manage'), ('BODEGA', 'inventory.transfers.manage'), ('GERENCIA', 'corporate.branches.all'),
                 ('GERENCIA', 'inventory.transfers.manage')) AS m(role_code, permission_code) ON m.role_code = r.code
    JOIN iam.permissions p ON p.tenant_id = r.tenant_id AND p.code = m.permission_code
    WHERE NOT EXISTS (SELECT 1 FROM iam.role_permissions x WHERE x.role_id = r.id AND x.permission_id = p.id);

    INSERT INTO accounting.accounts (id, tenant_id, code, name, account_type, parent_account_id, is_postable)
    SELECT gen_random_uuid(), g.tenant_id, a.code, a.name, a.account_type, g.id, true
    FROM (VALUES ('1.1.06', 'Mercadería enviada a sucursales', 'Asset', '1.1'),
                 ('2.1.04', 'Mercadería recibida de sucursales', 'Liability', '2.1')) AS a(code, name, account_type, parent_code)
    JOIN accounting.accounts g ON g.code = a.parent_code
    WHERE NOT EXISTS (SELECT 1 FROM accounting.accounts x WHERE x.tenant_id = g.tenant_id AND x.code = a.code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DO $$
    DECLARE r text;
    BEGIN
        FOREACH r IN ARRAY ARRAY['minv_app', 'minv_server'] LOOP
            IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) THEN
                EXECUTE format('GRANT USAGE ON SCHEMA iam, catalog, warehouse, inventory, purchasing, sales, accounting, integration, reporting TO %I', r);
                EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA iam, catalog, warehouse, inventory, purchasing, sales, accounting, integration TO %I', r);
                EXECUTE format('REVOKE UPDATE, DELETE, TRUNCATE ON inventory.stock_movements, iam.audit_logs, iam.access_logs, sales.cash_movements, sales.payments, accounting.exchange_rates, accounting.average_cost_history, inventory.stock_transfer_movements, inventory.stock_transfer_discrepancies, inventory.stock_transfer_events, inventory.stock_transfer_line_batches, integration.outbox_events, integration.webhook_deliveries, sales.external_orders, iam.processed_requests FROM %I', r);
                EXECUTE format('REVOKE INSERT, UPDATE, DELETE ON iam.modules, iam.__ef_migrations_history FROM %I', r);
                EXECUTE format('GRANT SELECT ON reporting.v_branch_stock, reporting.v_branch_daily_sales TO %I', r);
                EXECUTE format('GRANT EXECUTE ON FUNCTION iam.current_tenant_id(), iam.branch_visible(uuid) TO %I', r);
            END IF;
        END LOOP;
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'minv_server') THEN
            GRANT EXECUTE ON FUNCTION integration.resolve_api_key(text), iam.resolve_session(text),
                integration.claim_deliveries(integer, integer), reporting.refresh_all() TO minv_server;
        END IF;
    END;
    $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    DO $$
    DECLARE r record;
    BEGIN
        FOR r IN SELECT schemaname, tablename FROM pg_tables
                 WHERE schemaname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration')
                   AND tableowner = current_user
        LOOP
            EXECUTE format('ANALYZE %I.%I', r.schemaname, r.tablename);
        END LOOP;
    END;
    $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925214056_V4MultiBranchCloud') THEN
    INSERT INTO iam.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260925214056_V4MultiBranchCloud', '8.0.31');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925225352_V4BranchHeaderKeys') THEN
    ALTER TABLE accounting.journal_entries ADD CONSTRAINT fk_journal_entries_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES warehouse.branches (tenant_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925225352_V4BranchHeaderKeys') THEN
    ALTER TABLE purchasing.purchase_returns ADD CONSTRAINT fk_purchase_returns_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES warehouse.branches (tenant_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925225352_V4BranchHeaderKeys') THEN
    ALTER TABLE purchasing.supplier_invoices ADD CONSTRAINT fk_supplier_invoices_tenant_id_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES warehouse.branches (tenant_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM iam.__ef_migrations_history WHERE "MigrationId" = '20260925225352_V4BranchHeaderKeys') THEN
    INSERT INTO iam.__ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260925225352_V4BranchHeaderKeys', '8.0.31');
    END IF;
END $EF$;
COMMIT;

