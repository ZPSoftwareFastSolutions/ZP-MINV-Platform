using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MINV.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// V4.2 · SQL propio de PostgreSQL de la edición Tecnología (lo llama <see cref="V42TechRetail"/>), según la regla B-15:
    /// <list type="number">
    /// <item>guardia: las series existentes deben poder cumplir las reglas nuevas (una serie por variante y no por lote, sin
    /// espacios ni separadores, existencia solo si está en stock y del mismo lote);</item>
    /// <item>relleno de <c>serial_numbers.variant_id</c> desde su lote (y <c>kind</c> = Serial, <c>received_at</c> = alta de la
    /// fila) con verificación de que no quedó ninguna fila sin valor; <c>serial_numbers</c> no tiene triggers, no hay nada que
    /// pausar; al final se quitan los valores provisionales;</item>
    /// <item>append-only (trigger <c>trg_append_only</c> y privilegios revocados) en las 5 bitácoras nuevas;</item>
    /// <item>RLS por empresa en las 12 tablas nuevas (por descubrimiento, incluido el esquema nuevo <c>service</c>), política
    /// RESTRICTIVA por sucursal en las 6 tablas de sucursal nuevas y por origen o destino en la tabla entre sucursales;</item>
    /// <item>trigger <c>catalog.minv_spec_value_matches</c>: el valor usa la columna del tipo de su especificación y una
    /// especificación de un solo valor tiene una sola fila por producto (regla T-01);</item>
    /// <item>vista de control <c>inventory.v_serial_breaches</c> (regla T-02): series en stock ≠ stock, por sucursal y
    /// variante serializada;</item>
    /// <item>datos V4.2 de las empresas existentes (las nuevas los reciben del aprovisionamiento): permisos y matriz rol-permiso,
    /// tipo de movimiento REPOSICIÓN POR GARANTÍA y cuenta 5.1.10 Costo de garantías;</item>
    /// <item>privilegios de <c>minv_app</c> y <c>minv_server</c> (solo si existen).</item>
    /// </list>
    /// </summary>
    public partial class V42TechRetail
    {
        /// <summary>Tablas nuevas de la V4.2 (privilegios de los roles de aplicación). Una prueba verifica que coinciden con el
        /// modelo.</summary>
        internal static readonly string[] NewTablesV42 =
        [
            "catalog.spec_definitions", "catalog.spec_options", "catalog.product_spec_values", "catalog.product_tech_profiles",
            "inventory.serial_events", "inventory.stock_transfer_line_serials", "sales.sales_order_line_serials",
            "sales.sales_return_line_serials", "sales.pc_builds", "sales.pc_build_lines", "service.warranty_claims",
            "service.warranty_claim_events",
        ];

        /// <summary>Tablas de sucursal nuevas (<c>IBranchScoped</c>): se suman a las de la V4 y la V4.1.</summary>
        internal static readonly string[] BranchTablesV42 =
        [
            "sales.sales_order_line_serials", "sales.sales_return_line_serials", "sales.pc_builds", "sales.pc_build_lines",
            "service.warranty_claims", "service.warranty_claim_events",
        ];

        /// <summary>Tablas entre sucursales nuevas (<c>IInterBranch</c>): las ven el origen y el destino.</summary>
        internal static readonly string[] InterBranchTablesV42 = ["inventory.stock_transfer_line_serials"];

        /// <summary>Bitácoras append-only nuevas (<c>IAppendOnly</c>, reglas T-02 y T-05).</summary>
        internal static readonly string[] AppendOnlyTablesV42 =
        [
            "inventory.serial_events", "inventory.stock_transfer_line_serials", "sales.sales_order_line_serials",
            "sales.sales_return_line_serials", "service.warranty_claim_events",
        ];

        /// <summary>Esquemas donde se descubren las tablas con <c>tenant_id</c> sin política de empresa.</summary>
        internal static readonly string[] SchemasV42 =
            ["iam", "catalog", "warehouse", "inventory", "purchasing", "sales", "accounting", "integration", "billing", "service"];

        /// <summary>Permisos de la edición Tecnología (copia fija de <c>PermissionCodes</c> al publicar la V4.2; una prueba lo
        /// compara).</summary>
        internal static readonly (string Code, string Description)[] TechPermissions =
        [
            ("catalog.specs.manage", "Fichas técnicas: especificaciones por categoría, valores de cada producto, garantía y control por serie o IMEI"),
            ("inventory.serials.view", "Consultar series e IMEI, su trazabilidad, la garantía de una unidad y los casos RMA"),
            ("inventory.serials.manage", "Registrar series e IMEI de unidades en stock (inventario inicial) y dar de baja unidades serializadas"),
            ("service.rma.open", "Abrir casos de garantía (RMA) al recibir un equipo del cliente"),
            ("service.rma.manage", "Garantías y RMA: diagnosticar, enviar al proveedor, reponer con otra unidad y entregar equipos"),
            ("sales.pcbuild.manage", "Armador de PC: armar, cotizar y anular armados (cotizaciones con precio congelado)"),
        ];

        /// <summary>Matriz rol → permiso de la edición Tecnología (copia fija de <c>PermissionCodes.ForRole</c>; una prueba la
        /// compara).</summary>
        internal static readonly (string Role, string Permission)[] TechRolePermissions =
        [
            ("ADMIN", "catalog.specs.manage"), ("ADMIN", "inventory.serials.view"), ("ADMIN", "inventory.serials.manage"),
            ("ADMIN", "service.rma.open"), ("ADMIN", "service.rma.manage"), ("ADMIN", "sales.pcbuild.manage"),
            ("BODEGA", "catalog.specs.manage"), ("BODEGA", "inventory.serials.view"), ("BODEGA", "inventory.serials.manage"),
            ("BODEGA", "service.rma.open"), ("BODEGA", "service.rma.manage"),
            ("VENTAS", "inventory.serials.view"), ("VENTAS", "service.rma.open"), ("VENTAS", "sales.pcbuild.manage"),
            ("CAJERO", "inventory.serials.view"), ("CAJERO", "service.rma.open"), ("CAJERO", "sales.pcbuild.manage"),
            ("GERENCIA", "catalog.specs.manage"), ("GERENCIA", "inventory.serials.view"), ("GERENCIA", "inventory.serials.manage"),
            ("GERENCIA", "service.rma.open"), ("GERENCIA", "service.rma.manage"), ("GERENCIA", "sales.pcbuild.manage"),
            ("CONSULTA", "inventory.serials.view"),
        ];

        /// <summary>Tipo de movimiento de la reposición por garantía (copia fija de <c>MovementType.CreateDefaults</c>).</summary>
        internal const string WarrantyMovementCode = "REPOSICION_GARANTIA";

        internal const string WarrantyMovementName = "REPOSICIÓN POR GARANTÍA";

        internal const string WarrantyMovementDescription =
            "Salida de una unidad nueva entregada al cliente en reemplazo de otra en garantía (RMA). Asiento: 5.1.10 Costo de garantías.";

        /// <summary>Cuenta del costo de las reposiciones por garantía (copia fija de <c>ChartOfAccounts.Defaults</c>).</summary>
        internal const string WarrantyAccountCode = "5.1.10";

        internal const string WarrantyAccountName = "Costo de garantías";

        /// <summary>Guardia (antes de cualquier cambio): las series existentes deben poder cumplir las reglas nuevas.</summary>
        private static void V42Guard(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                DO $$
                DECLARE n bigint;
                BEGIN
                    SELECT count(*) INTO n FROM (
                        SELECT s.tenant_id, b.variant_id, s.serial
                        FROM inventory.serial_numbers s JOIN inventory.batches b ON b.id = s.batch_id
                        GROUP BY s.tenant_id, b.variant_id, s.serial
                        HAVING count(*) > 1) d;
                    IF n > 0 THEN
                        RAISE EXCEPTION 'M-INV V4.2: % series se repiten en lotes distintos de la misma variante (la serie pasa a ser única por variante): resuélvalas antes de migrar.', n;
                    END IF;
                    IF EXISTS (SELECT 1 FROM inventory.serial_numbers WHERE length(serial) = 0 OR serial ~ '[[:space:],;]') THEN
                        RAISE EXCEPTION 'M-INV V4.2: hay series vacías o con espacios, comas o punto y coma: corríjalas antes de migrar.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM inventory.serial_numbers WHERE (status IN ('InStock', 'Reserved')) <> (stock_level_id IS NOT NULL)) THEN
                        RAISE EXCEPTION 'M-INV V4.2: hay series en stock sin existencia o fuera de stock con existencia: corríjalas antes de migrar.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM inventory.serial_numbers s JOIN inventory.stock_levels l ON l.id = s.stock_level_id
                               WHERE l.batch_id <> s.batch_id) THEN
                        RAISE EXCEPTION 'M-INV V4.2: hay series en una existencia de otro lote: corríjalas antes de migrar.';
                    END IF;
                END;
                $$;
                """);

        /// <summary>Relleno de las columnas nuevas de las series (después de agregarlas con valor provisional y antes de crear
        /// sus índices, restricciones y FK), verificación y retiro de los valores provisionales.</summary>
        private static void V42Backfill(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE inventory.serial_numbers s SET variant_id = b.variant_id FROM inventory.batches b WHERE b.id = s.batch_id;
                UPDATE inventory.serial_numbers SET kind = 'Serial', received_at = created_at;
                DO $$
                DECLARE n bigint;
                BEGIN
                    SELECT count(*) INTO n FROM inventory.serial_numbers
                    WHERE variant_id = '00000000-0000-0000-0000-000000000000' OR kind = '' OR received_at < timestamptz '0002-01-01 00:00:00+00';
                    IF n > 0 THEN
                        RAISE EXCEPTION 'M-INV V4.2: % series quedaron sin variante, tipo o fecha de ingreso', n;
                    END IF;
                END;
                $$;
                ALTER TABLE inventory.serial_numbers
                    ALTER COLUMN variant_id DROP DEFAULT,
                    ALTER COLUMN kind DROP DEFAULT,
                    ALTER COLUMN received_at DROP DEFAULT;
                """);
        }

        private static void V42Guards(MigrationBuilder migrationBuilder)
        {
            // 1. Append-only en las bitácoras nuevas ---------------------------------------------------------------------
            foreach (var table in AppendOnlyTablesV42)
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON {table}
                        FOR EACH ROW EXECUTE FUNCTION iam.minv_append_only();
                    CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON {table}
                        FOR EACH STATEMENT EXECUTE FUNCTION iam.minv_append_only();
                    """);
            }

            // 2. RLS: empresa en toda tabla nueva con tenant_id; sucursal (RESTRICTIVA) en las de sucursal y entre sucursales
            migrationBuilder.Sql($"""
                DO $$
                DECLARE r record;
                BEGIN
                    FOR r IN
                        SELECT c.table_schema, c.table_name
                        FROM information_schema.columns c
                        JOIN information_schema.tables t
                          ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE'
                        WHERE c.column_name = 'tenant_id' AND c.table_schema IN ('{string.Join("', '", SchemasV42)}')
                          AND NOT EXISTS (SELECT 1 FROM pg_policies p
                                          WHERE p.schemaname = c.table_schema AND p.tablename = c.table_name AND p.policyname = 'tenant_isolation')
                    LOOP
                        EXECUTE format('ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY', r.table_schema, r.table_name);
                        EXECUTE format('CREATE POLICY tenant_isolation ON %I.%I USING (tenant_id = iam.current_tenant_id()) '
                                       'WITH CHECK (tenant_id = iam.current_tenant_id())', r.table_schema, r.table_name);
                    END LOOP;
                END;
                $$;
                """);
            foreach (var table in BranchTablesV42)
            {
                migrationBuilder.Sql($"""
                    CREATE POLICY branch_isolation ON {table} AS RESTRICTIVE
                        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
                    """);
            }
            foreach (var table in InterBranchTablesV42)
            {
                migrationBuilder.Sql($"""
                    CREATE POLICY branch_isolation ON {table} AS RESTRICTIVE
                        USING (iam.branch_visible(from_branch_id) OR iam.branch_visible(to_branch_id))
                        WITH CHECK (iam.branch_visible(from_branch_id) OR iam.branch_visible(to_branch_id));
                    """);
            }

            // 3. Fichas técnicas (T-01): el valor usa la columna del tipo de su especificación (el CHECK de arco garantiza que
            //    es la única) y una especificación de un solo valor tiene una sola fila por producto.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION catalog.minv_spec_value_matches() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE d record;
                BEGIN
                    IF num_nonnulls(NEW.number_value, NEW.text_value, NEW.option_id) <> 1 THEN
                        RETURN NEW;   -- el CHECK del arco (ck_product_spec_values_arco) lo rechaza
                    END IF;
                    SELECT s.name, s.data_type, s.is_multi_valued INTO d FROM catalog.spec_definitions s WHERE s.id = NEW.spec_definition_id;
                    IF NOT FOUND THEN
                        RETURN NEW;   -- la FK lo rechaza
                    END IF;
                    IF (d.data_type = 'Number' AND NEW.number_value IS NULL) OR (d.data_type = 'Text' AND NEW.text_value IS NULL)
                       OR (d.data_type = 'Option' AND NEW.option_id IS NULL) THEN
                        RAISE EXCEPTION 'M-INV: el valor de «%» no es del tipo de la especificación (%)', d.name, d.data_type
                            USING ERRCODE = 'P0001';
                    END IF;
                    IF NOT d.is_multi_valued AND EXISTS (
                        SELECT 1 FROM catalog.product_spec_values v
                        WHERE v.product_id = NEW.product_id AND v.spec_definition_id = NEW.spec_definition_id AND v.id <> NEW.id) THEN
                        RAISE EXCEPTION 'M-INV: «%» admite un solo valor por producto', d.name USING ERRCODE = 'P0001';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_spec_value_matches BEFORE INSERT OR UPDATE ON catalog.product_spec_values
                    FOR EACH ROW EXECUTE FUNCTION catalog.minv_spec_value_matches();
                """);

            // 4. Vista de control (T-02): en cada sucursal, las series en stock de una variante serializada = su stock. Debe estar
            //    VACÍA. security_invoker: se aplican la RLS de empresa y de sucursal del que consulta.
            migrationBuilder.Sql("""
                CREATE OR REPLACE VIEW inventory.v_serial_breaches WITH (security_invoker = true) AS
                WITH stock AS (
                    SELECT l.tenant_id, l.branch_id, b.variant_id, sum(l.quantity_on_hand) AS on_hand
                    FROM inventory.stock_levels l
                    JOIN inventory.batches b ON b.id = l.batch_id
                    GROUP BY l.tenant_id, l.branch_id, b.variant_id),
                serials AS (
                    SELECT s.tenant_id, l.branch_id, s.variant_id, count(*) AS in_stock
                    FROM inventory.serial_numbers s
                    JOIN inventory.stock_levels l ON l.id = s.stock_level_id
                    WHERE s.status IN ('InStock', 'Reserved')
                    GROUP BY s.tenant_id, l.branch_id, s.variant_id)
                SELECT v.tenant_id, coalesce(st.branch_id, se.branch_id) AS branch_id, v.id AS variant_id, v.sku,
                       coalesce(st.on_hand, 0) AS stock, coalesce(se.in_stock, 0) AS serials_in_stock,
                       coalesce(se.in_stock, 0) - coalesce(st.on_hand, 0) AS difference
                FROM stock st
                FULL JOIN serials se ON se.tenant_id = st.tenant_id AND se.branch_id = st.branch_id AND se.variant_id = st.variant_id
                JOIN catalog.product_variants v ON v.id = coalesce(st.variant_id, se.variant_id)
                JOIN catalog.products p ON p.id = v.product_id
                WHERE p.tracking_mode = 'Serial' AND coalesce(st.on_hand, 0) <> coalesce(se.in_stock, 0);
                """);

            // 5. Datos V4.2 de las empresas existentes (las nuevas los reciben del aprovisionamiento) --------------------------
            var permissions = string.Join(", ", TechPermissions.Select(p => $"('{Sql(p.Code)}', '{Sql(p.Description)}')"));
            var matrix = string.Join(", ", TechRolePermissions.Select(m => $"('{Sql(m.Role)}', '{Sql(m.Permission)}')"));
            migrationBuilder.Sql($"""
                INSERT INTO iam.permissions (id, tenant_id, code, description)
                SELECT gen_random_uuid(), t.id, p.code, p.description
                FROM iam.tenants t
                CROSS JOIN (VALUES {permissions}) AS p(code, description)
                WHERE NOT EXISTS (SELECT 1 FROM iam.permissions x WHERE x.tenant_id = t.id AND x.code = p.code);

                INSERT INTO iam.role_permissions (tenant_id, role_id, permission_id)
                SELECT r.tenant_id, r.id, p.id
                FROM iam.roles r
                JOIN (VALUES {matrix}) AS m(role_code, permission_code) ON m.role_code = r.code
                JOIN iam.permissions p ON p.tenant_id = r.tenant_id AND p.code = m.permission_code
                WHERE NOT EXISTS (SELECT 1 FROM iam.role_permissions x WHERE x.role_id = r.id AND x.permission_id = p.id);

                INSERT INTO inventory.movement_types (id, tenant_id, code, name, description, stock_factor, domain, requires_notes,
                                                     is_initial_balance, is_system)
                SELECT gen_random_uuid(), t.id, '{WarrantyMovementCode}', '{Sql(WarrantyMovementName)}', '{Sql(WarrantyMovementDescription)}',
                       -1, 'Warehouse', false, false, true
                FROM iam.tenants t
                WHERE NOT EXISTS (SELECT 1 FROM inventory.movement_types x WHERE x.tenant_id = t.id AND x.code = '{WarrantyMovementCode}');

                INSERT INTO accounting.accounts (id, tenant_id, code, name, account_type, parent_account_id, is_postable)
                SELECT gen_random_uuid(), g.tenant_id, '{WarrantyAccountCode}', '{Sql(WarrantyAccountName)}', 'Expense', g.id, true
                FROM accounting.accounts g
                WHERE g.code = '5.1'
                  AND NOT EXISTS (SELECT 1 FROM accounting.accounts x WHERE x.tenant_id = g.tenant_id AND x.code = '{WarrantyAccountCode}');
                """);

            // 6. Privilegios: minv_app (escritorio directo) y minv_server (nube; NOBYPASSRLS), solo si existen ---------------
            var tables = string.Join(", ", NewTablesV42);
            var ledgers = string.Join(", ", AppendOnlyTablesV42);
            migrationBuilder.Sql($"""
                DO $$
                DECLARE r text;
                BEGIN
                    FOREACH r IN ARRAY ARRAY['minv_app', 'minv_server'] LOOP
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) THEN
                            EXECUTE format('GRANT USAGE ON SCHEMA service TO %I', r);
                            EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON {tables} TO %I', r);
                            EXECUTE format('REVOKE UPDATE, DELETE, TRUNCATE ON {ledgers} FROM %I', r);
                            EXECUTE format('GRANT SELECT ON inventory.v_serial_breaches TO %I', r);
                        END IF;
                    END LOOP;
                END;
                $$;
                """);
        }

        /// <summary>Reversa del SQL propio (antes de que la parte generada borre las tablas nuevas): vista, trigger de las
        /// fichas técnicas, permisos, tipo de movimiento y cuenta (si no se usaron).</summary>
        private static void V42DropGuards(MigrationBuilder migrationBuilder)
        {
            var codes = string.Join(", ", TechPermissions.Select(p => $"'{Sql(p.Code)}'"));
            migrationBuilder.Sql($"""
                DROP VIEW IF EXISTS inventory.v_serial_breaches;
                DROP TRIGGER IF EXISTS trg_spec_value_matches ON catalog.product_spec_values;
                DROP FUNCTION IF EXISTS catalog.minv_spec_value_matches();
                DELETE FROM iam.role_permissions rp USING iam.permissions p
                    WHERE p.id = rp.permission_id AND p.code IN ({codes});
                DELETE FROM iam.permissions WHERE code IN ({codes});
                DELETE FROM inventory.movement_types t WHERE t.code = '{WarrantyMovementCode}'
                    AND NOT EXISTS (SELECT 1 FROM inventory.stock_movements m WHERE m.movement_type_id = t.id);
                DELETE FROM accounting.accounts a WHERE a.code = '{WarrantyAccountCode}'
                    AND NOT EXISTS (SELECT 1 FROM accounting.journal_lines l WHERE l.account_id = a.id);
                """);
        }

        /// <summary>El esquema queda vacío cuando la parte generada ya borró las tablas.</summary>
        private static void V42DropSchema(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS service;");

        private static string Sql(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    }
}
