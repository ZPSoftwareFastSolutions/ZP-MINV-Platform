using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MINV.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// V6 · SQL propio de PostgreSQL de la tienda web conectada (lo llama <see cref="V6Storefront"/>), según la regla B-15:
    /// <list type="number">
    /// <item>relleno de <c>pc_builds.channel</c> (los armados existentes son del escritorio) con verificación y retiro del valor
    /// provisional, y bitácora reconstruida de los armados existentes (una fila por hecho conocido: creado, cotizado, vendido o
    /// anulado) antes de crear el trigger append-only;</item>
    /// <item>append-only (trigger <c>trg_append_only</c> y privilegios revocados) en <c>sales.pc_build_events</c>;</item>
    /// <item>RLS por empresa en la tabla nueva (por descubrimiento) y política RESTRICTIVA por sucursal;</item>
    /// <item>datos V6 de las empresas existentes (las nuevas los reciben del aprovisionamiento): permisos <c>storefront.read</c> y
    /// <c>storefront.reserve</c> con su matriz, rol <c>TIENDA_WEB</c> y el usuario técnico <c>tienda-web@&lt;dominio&gt;</c> de cada
    /// empresa (rol TIENDA_WEB, casa matriz, credencial aleatoria inutilizable: el gateway lo autentica por configuración);</item>
    /// <item>privilegios de <c>minv_app</c> y <c>minv_server</c> (solo si existen).</item>
    /// </list>
    /// </summary>
    public partial class V6Storefront
    {
        /// <summary>Tablas nuevas de la V6 (privilegios de los roles de aplicación). Una prueba verifica que coinciden con el modelo.</summary>
        internal static readonly string[] NewTablesV6 = ["sales.pc_build_events"];

        /// <summary>Tablas de sucursal nuevas (<c>IBranchScoped</c>): se suman a las de la V4, la V4.1 y la V4.2.</summary>
        internal static readonly string[] BranchTablesV6 = ["sales.pc_build_events"];

        /// <summary>Bitácoras append-only nuevas (<c>IAppendOnly</c>, regla S-04).</summary>
        internal static readonly string[] AppendOnlyTablesV6 = ["sales.pc_build_events"];

        /// <summary>Esquemas donde se descubren las tablas con <c>tenant_id</c> sin política de empresa.</summary>
        internal static readonly string[] SchemasV6 =
            ["iam", "catalog", "warehouse", "inventory", "purchasing", "sales", "accounting", "integration", "billing", "service"];

        /// <summary>Permisos de la tienda web (copia fija de <c>PermissionCodes</c> al publicar la V6; una prueba lo compara).</summary>
        internal static readonly (string Code, string Description)[] StorefrontPermissions =
        [
            ("storefront.read", "Tienda web: leer el catálogo público (productos, precios, disponibilidad, imágenes y armados sugeridos)"),
            ("storefront.reserve", "Tienda web: reservar armados con reserva de stock y consultar o cancelar una reserva con su teléfono"),
        ];

        /// <summary>Matriz rol → permiso de la tienda web (copia fija de <c>PermissionCodes.ForRole</c>; una prueba la compara).
        /// TIENDA_WEB recibe además <c>inventory.stock.view</c> (permiso que ya existe).</summary>
        internal static readonly (string Role, string Permission)[] StorefrontRolePermissions =
        [
            ("ADMIN", "storefront.read"), ("ADMIN", "storefront.reserve"),
            ("GERENCIA", "storefront.read"), ("GERENCIA", "storefront.reserve"),
            ("TIENDA_WEB", "storefront.read"), ("TIENDA_WEB", "storefront.reserve"), ("TIENDA_WEB", "inventory.stock.view"),
        ];

        /// <summary>Rol del usuario técnico (copia fija de <c>RoleCodes</c>).</summary>
        internal const string StorefrontRoleCode = "TIENDA_WEB";

        internal const string StorefrontRoleName = "Tienda web";

        /// <summary>Usuario técnico (copia fija de <c>TenantProvisioner</c>).</summary>
        internal const string StorefrontUserLocalPart = "tienda-web";

        internal const string StorefrontUserName = "Tienda web";

        internal const string BackfillDetail = "Historial reconstruido al migrar a la V6";

        /// <summary>Relleno de <c>channel</c> y bitácora de los armados existentes (después de crear <c>pc_build_events</c> y antes de
        /// los CHECK y del trigger append-only).</summary>
        private static void V6Backfill(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql($"""
                UPDATE sales.pc_builds SET channel = 'Desktop' WHERE channel = '';
                ALTER TABLE sales.pc_builds ALTER COLUMN channel DROP DEFAULT;
                DO $$
                DECLARE n bigint;
                BEGIN
                    SELECT count(*) INTO n FROM sales.pc_builds WHERE channel NOT IN ('Desktop', 'Web');
                    IF n > 0 THEN
                        RAISE EXCEPTION 'M-INV V6: % armados quedaron sin canal', n;
                    END IF;
                END;
                $$;
                INSERT INTO sales.pc_build_events (id, tenant_id, branch_id, pc_build_id, action, status, user_id, occurred_at, detail, created_at, created_by)
                SELECT gen_random_uuid(), b.tenant_id, b.branch_id, b.id, 'Created', 'Draft', b.created_by_user_id, b.created_at, '{BackfillDetail}', now(),
                       b.created_by_user_id
                FROM sales.pc_builds b
                WHERE NOT EXISTS (SELECT 1 FROM sales.pc_build_events e WHERE e.pc_build_id = b.id);
                INSERT INTO sales.pc_build_events (id, tenant_id, branch_id, pc_build_id, action, status, user_id, occurred_at, detail, created_at, created_by)
                SELECT gen_random_uuid(), b.tenant_id, b.branch_id, b.id, 'Quoted', 'Quoted', b.created_by_user_id, b.quoted_at, '{BackfillDetail}', now(),
                       b.created_by_user_id
                FROM sales.pc_builds b
                WHERE b.quoted_at IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM sales.pc_build_events e WHERE e.pc_build_id = b.id AND e.action = 'Quoted');
                INSERT INTO sales.pc_build_events (id, tenant_id, branch_id, pc_build_id, action, status, user_id, occurred_at, detail, created_at, created_by)
                SELECT gen_random_uuid(), b.tenant_id, b.branch_id, b.id, b.status, b.status, b.created_by_user_id,
                       greatest(coalesce(b.updated_at, b.created_at), coalesce(b.quoted_at, b.created_at)), '{BackfillDetail}', now(), b.created_by_user_id
                FROM sales.pc_builds b
                WHERE b.status IN ('Sold', 'Cancelled')
                  AND NOT EXISTS (SELECT 1 FROM sales.pc_build_events e WHERE e.pc_build_id = b.id AND e.action = b.status);
                """);

        private static void V6Guards(MigrationBuilder migrationBuilder)
        {
            // 1. Append-only en la bitácora nueva -----------------------------------------------------------------------------
            foreach (var table in AppendOnlyTablesV6)
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON {table}
                        FOR EACH ROW EXECUTE FUNCTION iam.minv_append_only();
                    CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON {table}
                        FOR EACH STATEMENT EXECUTE FUNCTION iam.minv_append_only();
                    """);
            }

            // 2. RLS: empresa en toda tabla nueva con tenant_id; sucursal (RESTRICTIVA) en la de sucursal -------------------------
            migrationBuilder.Sql($"""
                DO $$
                DECLARE r record;
                BEGIN
                    FOR r IN
                        SELECT c.table_schema, c.table_name
                        FROM information_schema.columns c
                        JOIN information_schema.tables t
                          ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE'
                        WHERE c.column_name = 'tenant_id' AND c.table_schema IN ('{string.Join("', '", SchemasV6)}')
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
            foreach (var table in BranchTablesV6)
            {
                migrationBuilder.Sql($"""
                    CREATE POLICY branch_isolation ON {table} AS RESTRICTIVE
                        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
                    """);
            }

            // 3. Datos V6 de las empresas existentes: permisos, rol TIENDA_WEB, matriz y usuario técnico -----------------------------
            var permissions = string.Join(", ", StorefrontPermissions.Select(p => $"('{Sql(p.Code)}', '{Sql(p.Description)}')"));
            var matrix = string.Join(", ", StorefrontRolePermissions.Select(m => $"('{Sql(m.Role)}', '{Sql(m.Permission)}')"));
            migrationBuilder.Sql($"""
                INSERT INTO iam.permissions (id, tenant_id, code, description)
                SELECT gen_random_uuid(), t.id, p.code, p.description
                FROM iam.tenants t
                CROSS JOIN (VALUES {permissions}) AS p(code, description)
                WHERE NOT EXISTS (SELECT 1 FROM iam.permissions x WHERE x.tenant_id = t.id AND x.code = p.code);

                INSERT INTO iam.roles (id, tenant_id, code, name, is_system)
                SELECT gen_random_uuid(), t.id, '{StorefrontRoleCode}', '{Sql(StorefrontRoleName)}', true
                FROM iam.tenants t
                WHERE NOT EXISTS (SELECT 1 FROM iam.roles x WHERE x.tenant_id = t.id AND x.code = '{StorefrontRoleCode}');

                INSERT INTO iam.role_permissions (tenant_id, role_id, permission_id)
                SELECT r.tenant_id, r.id, p.id
                FROM iam.roles r
                JOIN (VALUES {matrix}) AS m(role_code, permission_code) ON m.role_code = r.code
                JOIN iam.permissions p ON p.tenant_id = r.tenant_id AND p.code = m.permission_code
                WHERE NOT EXISTS (SELECT 1 FROM iam.role_permissions x WHERE x.role_id = r.id AND x.permission_id = p.id);

                -- Usuario técnico tienda-web@<dominio del administrador> (o <empresa>.local) en cada empresa que aún no lo tiene
                INSERT INTO iam.users (id, tenant_id, email, display_name, is_active)
                SELECT gen_random_uuid(), t.id,
                       '{StorefrontUserLocalPart}@' || coalesce(
                           (SELECT split_part(u.email, '@', 2) FROM iam.users u
                            JOIN iam.user_roles ur ON ur.user_id = u.id AND ur.tenant_id = u.tenant_id
                            JOIN iam.roles r ON r.id = ur.role_id AND r.code = 'ADMIN'
                            WHERE u.tenant_id = t.id AND position('@' in u.email) > 0
                            ORDER BY u.created_at LIMIT 1),
                           lower(t.code) || '.local'),
                       '{Sql(StorefrontUserName)}', true
                FROM iam.tenants t
                WHERE NOT EXISTS (SELECT 1 FROM iam.users u JOIN iam.user_roles ur ON ur.user_id = u.id JOIN iam.roles r ON r.id = ur.role_id
                                  WHERE u.tenant_id = t.id AND r.code = '{StorefrontRoleCode}')
                  AND NOT EXISTS (SELECT 1 FROM iam.users u WHERE u.tenant_id = t.id AND u.email LIKE '{StorefrontUserLocalPart}@%');

                -- Credencial inutilizable (hash aleatorio: ninguna contraseña la produce), rol y casa matriz
                INSERT INTO iam.user_credentials (user_id, tenant_id, password_hash, algorithm, iterations, changed_at, must_change_password, failed_attempts)
                SELECT u.id, u.tenant_id,
                       encode(sha256(convert_to(gen_random_uuid()::text, 'UTF8')), 'base64') || ':' || encode(sha256(convert_to(gen_random_uuid()::text, 'UTF8')), 'base64'),
                       'PBKDF2-SHA256', 600000, now(), false, 0
                FROM iam.users u
                WHERE u.email LIKE '{StorefrontUserLocalPart}@%' AND u.display_name = '{Sql(StorefrontUserName)}'
                  AND NOT EXISTS (SELECT 1 FROM iam.user_credentials c WHERE c.user_id = u.id);

                INSERT INTO iam.user_roles (tenant_id, user_id, role_id)
                SELECT u.tenant_id, u.id, r.id
                FROM iam.users u
                JOIN iam.roles r ON r.tenant_id = u.tenant_id AND r.code = '{StorefrontRoleCode}'
                WHERE u.email LIKE '{StorefrontUserLocalPart}@%' AND u.display_name = '{Sql(StorefrontUserName)}'
                  AND NOT EXISTS (SELECT 1 FROM iam.user_roles x WHERE x.user_id = u.id);

                INSERT INTO warehouse.branch_users (tenant_id, branch_id, user_id)
                SELECT u.tenant_id,
                       coalesce((SELECT w.branch_id FROM iam.tenant_configs c JOIN warehouse.warehouses w ON w.id = c.default_warehouse_id
                                 WHERE c.tenant_id = u.tenant_id LIMIT 1),
                                (SELECT b.id FROM warehouse.branches b WHERE b.tenant_id = u.tenant_id ORDER BY b.code LIMIT 1)),
                       u.id
                FROM iam.users u
                JOIN iam.user_roles ur ON ur.user_id = u.id
                JOIN iam.roles r ON r.id = ur.role_id AND r.code = '{StorefrontRoleCode}'
                WHERE NOT EXISTS (SELECT 1 FROM warehouse.branch_users x WHERE x.user_id = u.id)
                  AND EXISTS (SELECT 1 FROM warehouse.branches b WHERE b.tenant_id = u.tenant_id);
                """);

            // 4. Privilegios: minv_app (escritorio directo) y minv_server (nube; NOBYPASSRLS), solo si existen ----------------------
            var tables = string.Join(", ", NewTablesV6);
            var ledgers = string.Join(", ", AppendOnlyTablesV6);
            migrationBuilder.Sql($"""
                DO $$
                DECLARE r text;
                BEGIN
                    FOREACH r IN ARRAY ARRAY['minv_app', 'minv_server'] LOOP
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) THEN
                            EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON {tables} TO %I', r);
                            EXECUTE format('REVOKE UPDATE, DELETE, TRUNCATE ON {ledgers} FROM %I', r);
                        END IF;
                    END LOOP;
                END;
                $$;
                """);
        }

        /// <summary>Reversa del SQL propio (antes de que la parte generada borre la tabla y las columnas): permisos, matriz, rol y
        /// usuario técnico (se desactiva; sus filas de auditoría lo referencian).</summary>
        private static void V6DropGuards(MigrationBuilder migrationBuilder)
        {
            var codes = string.Join(", ", StorefrontPermissions.Select(p => $"'{Sql(p.Code)}'"));
            migrationBuilder.Sql($"""
                DELETE FROM iam.role_permissions rp USING iam.permissions p WHERE p.id = rp.permission_id AND p.code IN ({codes});
                DELETE FROM iam.role_permissions rp USING iam.roles r WHERE r.id = rp.role_id AND r.code = '{StorefrontRoleCode}';
                UPDATE iam.users u SET is_active = false
                    FROM iam.user_roles ur JOIN iam.roles r ON r.id = ur.role_id
                    WHERE ur.user_id = u.id AND r.code = '{StorefrontRoleCode}';
                DELETE FROM iam.user_roles ur USING iam.roles r WHERE r.id = ur.role_id AND r.code = '{StorefrontRoleCode}';
                DELETE FROM iam.roles WHERE code = '{StorefrontRoleCode}';
                DELETE FROM iam.permissions WHERE code IN ({codes});
                UPDATE sales.pc_builds SET published_to_web = false;
                -- Las reservas de armados vuelven al stock y los armados reservados quedan anulados (el estado Reserved deja de existir)
                UPDATE inventory.stock_levels l SET quantity_reserved = l.quantity_reserved - r.q
                    FROM (SELECT stock_level_id, sum(quantity) AS q FROM inventory.stock_reservations
                          WHERE pc_build_line_id IS NOT NULL AND status = 'Active' GROUP BY stock_level_id) r
                    WHERE r.stock_level_id = l.id;
                UPDATE inventory.stock_reservations SET status = 'Released' WHERE pc_build_line_id IS NOT NULL AND status = 'Active';
                UPDATE sales.pc_builds SET status = 'Cancelled', cancel_reason = 'Reversa de la migración V6' WHERE status = 'Reserved';
                """);
        }

        private static string Sql(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    }
}
