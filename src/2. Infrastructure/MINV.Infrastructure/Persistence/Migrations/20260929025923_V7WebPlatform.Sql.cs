using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MINV.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// V7 · SQL propio de PostgreSQL de la plataforma web (lo llama <see cref="V7WebPlatform"/>), según la regla B-15:
    /// <list type="number">
    /// <item>guardia (antes de cualquier cambio): si una empresa ya tuviera un rol <c>CLIENTE</c> que no es el del sistema o que
    /// tiene permisos del personal, la migración se detiene (el registro de la tienda web asigna ese rol a cualquier visitante,
    /// regla P-03);</item>
    /// <item>relleno de <c>pc_builds.kind</c> (los armados existentes son armados: <c>Build</c>) con verificación y retiro del
    /// valor provisional; <c>pc_builds</c> no tiene triggers: no hay nada que pausar;</item>
    /// <item>append-only (trigger <c>trg_append_only</c> y privilegios revocados) en <c>integration.outgoing_mails</c> y
    /// <c>integration.outgoing_mail_attempts</c>;</item>
    /// <item>RLS por empresa en las 4 tablas nuevas (por descubrimiento) y política RESTRICTIVA por sucursal en el correo pedido
    /// (<c>outgoing_mails</c>, de la sucursal de su reserva);</item>
    /// <item>reglas del carrito que cruzan dos filas (regla P-05): la ranura nula solo en líneas de un carrito
    /// (<c>trg_pc_build_line_slot</c>) y el tipo del armado no cambia después de insertarlo (<c>trg_pc_build_kind_immutable</c>);</item>
    /// <item>función SECURITY DEFINER <c>integration.claim_outgoing_mails(integer, integer)</c> del despachador del correo
    /// (<c>FOR UPDATE SKIP LOCKED</c>, <c>search_path</c> fijo, sin EXECUTE para PUBLIC, regla B-13);</item>
    /// <item>datos V7 de las empresas existentes (las nuevas los reciben del aprovisionamiento): permisos <c>account.manage</c> y
    /// <c>account.reserve</c>, rol <c>CLIENTE</c> («Cliente web», de sistema) y su matriz rol-permiso;</item>
    /// <item>privilegios de <c>minv_app</c> y <c>minv_server</c> (solo si existen) y EXECUTE de la función solo para
    /// <c>minv_server</c>.</item>
    /// </list>
    /// El canal <c>web</c> de <c>iam.audit_logs</c> (<c>ck_audit_logs_canal</c>) lo cambia la parte generada.
    /// </summary>
    public partial class V7WebPlatform
    {
        /// <summary>Tablas nuevas de la V7 (privilegios de los roles de aplicación). Una prueba verifica que coinciden con el modelo.</summary>
        internal static readonly string[] NewTablesV7 =
        [
            "sales.customer_accounts", "integration.outgoing_mails", "integration.outgoing_mail_dispatch", "integration.outgoing_mail_attempts",
        ];

        /// <summary>Tablas de sucursal nuevas (<c>IBranchScoped</c>): se suman a las de la V4, la V4.1, la V4.2 y la V6. La cola
        /// (<c>outgoing_mail_dispatch</c>), los intentos y las cuentas de cliente son de la empresa.</summary>
        internal static readonly string[] BranchTablesV7 = ["integration.outgoing_mails"];

        /// <summary>Libros append-only nuevos (<c>IAppendOnly</c>, regla P-06): el correo pedido y sus intentos de envío.</summary>
        internal static readonly string[] AppendOnlyTablesV7 = ["integration.outgoing_mails", "integration.outgoing_mail_attempts"];

        /// <summary>Esquemas donde se descubren las tablas con <c>tenant_id</c> sin política de empresa.</summary>
        internal static readonly string[] SchemasV7 =
            ["iam", "catalog", "warehouse", "inventory", "purchasing", "sales", "accounting", "integration", "billing", "service"];

        /// <summary>Permisos de la cuenta de cliente (copia fija de <c>PermissionCodes</c> al publicar la V7; una prueba lo compara).</summary>
        internal static readonly (string Code, string Description)[] AccountPermissions =
        [
            ("account.manage", "Cuenta de cliente: ver y actualizar sus datos y ver o cancelar sus propias reservas"),
            ("account.reserve", "Cuenta de cliente: reservar productos con los datos de su cuenta"),
        ];

        /// <summary>Matriz rol → permiso de la cuenta de cliente (copia fija de <c>PermissionCodes.ForRole</c>; una prueba la
        /// compara): la administración los recibe por tener todos y CLIENTE solo esos dos.</summary>
        internal static readonly (string Role, string Permission)[] AccountRolePermissions =
        [
            ("ADMIN", "account.manage"), ("ADMIN", "account.reserve"),
            ("CLIENTE", "account.manage"), ("CLIENTE", "account.reserve"),
        ];

        /// <summary>Rol de las cuentas de cliente (copia fija de <c>RoleCodes</c>).</summary>
        internal const string CustomerRoleCode = "CLIENTE";

        internal const string CustomerRoleName = "Cliente web";

        /// <summary>Ranura que recibe en la reversa una pieza sin ranura (solo de un carrito): la V6 exige ranura y el escritorio
        /// ya muestra esas piezas en Periféricos.</summary>
        internal const string DowngradeSlot = "Peripheral";

        /// <summary>Guardia (antes de cualquier cambio): el rol CLIENTE, si ya existiera, debe ser el del sistema y sin permisos
        /// del personal.</summary>
        private static void V7Guard(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql($"""
                DO $$
                DECLARE n bigint;
                BEGIN
                    SELECT count(*) INTO n FROM iam.roles r
                    WHERE r.code = '{CustomerRoleCode}'
                      AND (NOT r.is_system OR EXISTS (
                          SELECT 1 FROM iam.role_permissions rp JOIN iam.permissions p ON p.id = rp.permission_id
                          WHERE rp.role_id = r.id AND p.code NOT LIKE 'account.%'));
                    IF n > 0 THEN
                        RAISE EXCEPTION 'M-INV V7: % empresas ya tienen un rol {CustomerRoleCode} que no es el del sistema o que tiene permisos del personal (el registro de la tienda web asigna ese rol a cualquier visitante): cámbiele el código o quítele esos permisos antes de migrar.', n;
                    END IF;
                END;
                $$;
                """);

        /// <summary>Relleno de <c>kind</c> (después de agregar la columna con el valor provisional y antes de sus CHECK), verificación
        /// y retiro del valor provisional.</summary>
        private static void V7Backfill(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                UPDATE sales.pc_builds SET kind = 'Build' WHERE kind = '';
                ALTER TABLE sales.pc_builds ALTER COLUMN kind DROP DEFAULT;
                DO $$
                DECLARE n bigint;
                BEGIN
                    SELECT count(*) INTO n FROM sales.pc_builds WHERE kind NOT IN ('Build', 'Cart');
                    IF n > 0 THEN
                        RAISE EXCEPTION 'M-INV V7: % armados quedaron sin tipo', n;
                    END IF;
                END;
                $$;
                """);

        private static void V7Guards(MigrationBuilder migrationBuilder)
        {
            // 1. Append-only en el correo pedido y en sus intentos ----------------------------------------------------------
            foreach (var table in AppendOnlyTablesV7)
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
                        WHERE c.column_name = 'tenant_id' AND c.table_schema IN ('{string.Join("', '", SchemasV7)}')
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
            foreach (var table in BranchTablesV7)
            {
                migrationBuilder.Sql($"""
                    CREATE POLICY branch_isolation ON {table} AS RESTRICTIVE
                        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
                    """);
            }

            // 3. Carrito (regla P-05): la ranura nula solo en las líneas de un carrito (cruza la línea y su armado: trigger, regla
            //    A-06) y el tipo no cambia después de insertar (una línea sin ranura nunca queda colgando de un armado). Sin
            //    armado visible, la FK o la RLS rechazan la fila.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION sales.minv_pc_build_line_slot() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE b record;
                BEGIN
                    SELECT p.number, p.kind INTO b FROM sales.pc_builds p WHERE p.id = NEW.pc_build_id;
                    IF FOUND AND b.kind <> 'Cart' THEN
                        RAISE EXCEPTION 'M-INV: cada pieza del armado % debe tener su ranura (solo un carrito admite productos sin ranura)',
                            b.number USING ERRCODE = 'P0001';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER trg_pc_build_line_slot BEFORE INSERT OR UPDATE OF slot, pc_build_id ON sales.pc_build_lines
                    FOR EACH ROW WHEN (NEW.slot IS NULL) EXECUTE FUNCTION sales.minv_pc_build_line_slot();

                CREATE OR REPLACE FUNCTION sales.minv_pc_build_kind_immutable() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'M-INV: el tipo de % (armado o carrito) no cambia después de crearlo', OLD.number
                        USING ERRCODE = 'P0001';
                END;
                $$;
                CREATE TRIGGER trg_pc_build_kind_immutable BEFORE UPDATE OF kind ON sales.pc_builds
                    FOR EACH ROW WHEN (OLD.kind IS DISTINCT FROM NEW.kind) EXECUTE FUNCTION sales.minv_pc_build_kind_immutable();
                """);

            // 4. SECURITY DEFINER del despachador del correo (regla B-13): reclama los pendientes vencidos de TODAS las empresas
            //    antes de fijar minv.tenant_id; devuelve solo (empresa, correo, arrendamiento); search_path fijo; EXECUTE solo para
            //    minv_server. B6: el arrendamiento (leased_until) es la marca de dueño de ESTE reclamo: el despachador lo renueva
            //    justo antes de enviar solo si sigue siendo el suyo (otra réplica que lo reclamó al vencer tiene otro valor).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION integration.claim_outgoing_mails(p_limit integer, p_lease_seconds integer)
                RETURNS TABLE (tenant_id uuid, outgoing_mail_id uuid, leased_until timestamptz)
                LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, integration AS $$
                #variable_conflict use_column
                BEGIN
                    RETURN QUERY
                    WITH due AS (
                        SELECT d.outgoing_mail_id AS id
                        FROM integration.outgoing_mail_dispatch d
                        WHERE d.status = 'Pending' AND d.next_attempt_at <= now()
                        ORDER BY d.next_attempt_at
                        LIMIT greatest(1, least(p_limit, 500))
                        FOR UPDATE SKIP LOCKED)
                    UPDATE integration.outgoing_mail_dispatch d
                       SET next_attempt_at = now() + make_interval(secs => greatest(30, least(p_lease_seconds, 3600))),
                           leased_until = now() + make_interval(secs => greatest(30, least(p_lease_seconds, 3600)))
                    FROM due
                    WHERE d.outgoing_mail_id = due.id
                    RETURNING d.tenant_id, d.outgoing_mail_id, d.leased_until;
                END;
                $$;
                REVOKE ALL ON FUNCTION integration.claim_outgoing_mails(integer, integer) FROM PUBLIC;
                """);

            // 5. Datos V7 de las empresas existentes: permisos de la cuenta de cliente, rol CLIENTE y matriz --------------------------
            var permissions = string.Join(", ", AccountPermissions.Select(p => $"('{Sql(p.Code)}', '{Sql(p.Description)}')"));
            var matrix = string.Join(", ", AccountRolePermissions.Select(m => $"('{Sql(m.Role)}', '{Sql(m.Permission)}')"));
            migrationBuilder.Sql($"""
                INSERT INTO iam.permissions (id, tenant_id, code, description)
                SELECT gen_random_uuid(), t.id, p.code, p.description
                FROM iam.tenants t
                CROSS JOIN (VALUES {permissions}) AS p(code, description)
                WHERE NOT EXISTS (SELECT 1 FROM iam.permissions x WHERE x.tenant_id = t.id AND x.code = p.code);

                INSERT INTO iam.roles (id, tenant_id, code, name, is_system)
                SELECT gen_random_uuid(), t.id, '{CustomerRoleCode}', '{Sql(CustomerRoleName)}', true
                FROM iam.tenants t
                WHERE NOT EXISTS (SELECT 1 FROM iam.roles x WHERE x.tenant_id = t.id AND x.code = '{CustomerRoleCode}');

                INSERT INTO iam.role_permissions (tenant_id, role_id, permission_id)
                SELECT r.tenant_id, r.id, p.id
                FROM iam.roles r
                JOIN (VALUES {matrix}) AS m(role_code, permission_code) ON m.role_code = r.code
                JOIN iam.permissions p ON p.tenant_id = r.tenant_id AND p.code = m.permission_code
                WHERE NOT EXISTS (SELECT 1 FROM iam.role_permissions x WHERE x.role_id = r.id AND x.permission_id = p.id);
                """);

            // 6. Privilegios: minv_app (escritorio directo) y minv_server (nube y gateway; NOBYPASSRLS), solo si existen ---------------
            var tables = string.Join(", ", NewTablesV7);
            var ledgers = string.Join(", ", AppendOnlyTablesV7);
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
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'minv_server') THEN
                        GRANT EXECUTE ON FUNCTION integration.claim_outgoing_mails(integer, integer) TO minv_server;
                    END IF;
                END;
                $$;
                """);
        }

        /// <summary>Reversa del SQL propio (antes de que la parte generada borre las tablas nuevas, las columnas y la ranura nula).
        /// Guardia: la auditoría es append-only, así que con filas del canal <c>web</c> el CHECK de la V6 no se puede restaurar (se
        /// detiene con un mensaje claro en vez de fallar a mitad de camino). Después: función del correo, triggers del carrito,
        /// piezas sin ranura a Periféricos, permisos, matriz y rol CLIENTE (las cuentas de cliente se desactivan: sus reservas,
        /// sesiones y auditoría las referencian; el personal no se toca).</summary>
        private static void V7DropGuards(MigrationBuilder migrationBuilder)
        {
            var codes = string.Join(", ", AccountPermissions.Select(p => $"'{Sql(p.Code)}'"));
            migrationBuilder.Sql("""
                DO $$
                DECLARE n bigint;
                BEGIN
                    SELECT count(*) INTO n FROM iam.audit_logs WHERE channel = 'web';
                    IF n > 0 THEN
                        RAISE EXCEPTION 'M-INV V7: no se puede volver a la V6: % registros de la auditoría son del canal web y la auditoría es append-only. Restaure el respaldo previo a la migración.', n;
                    END IF;
                END;
                $$;
                """);
            migrationBuilder.Sql($"""
                DROP FUNCTION IF EXISTS integration.claim_outgoing_mails(integer, integer);
                DROP TRIGGER IF EXISTS trg_pc_build_kind_immutable ON sales.pc_builds;
                DROP FUNCTION IF EXISTS sales.minv_pc_build_kind_immutable();
                DROP TRIGGER IF EXISTS trg_pc_build_line_slot ON sales.pc_build_lines;
                DROP FUNCTION IF EXISTS sales.minv_pc_build_line_slot();
                UPDATE sales.pc_build_lines SET slot = '{DowngradeSlot}' WHERE slot IS NULL;
                DELETE FROM iam.role_permissions rp USING iam.permissions p WHERE p.id = rp.permission_id AND p.code IN ({codes});
                DELETE FROM iam.role_permissions rp USING iam.roles r WHERE r.id = rp.role_id AND r.code = '{CustomerRoleCode}';
                UPDATE iam.users u SET is_active = false
                    WHERE EXISTS (SELECT 1 FROM iam.user_roles ur JOIN iam.roles r ON r.id = ur.role_id
                                  WHERE ur.user_id = u.id AND r.code = '{CustomerRoleCode}')
                      AND NOT EXISTS (SELECT 1 FROM iam.user_roles ur JOIN iam.roles r ON r.id = ur.role_id
                                      WHERE ur.user_id = u.id AND r.code <> '{CustomerRoleCode}');
                DELETE FROM iam.user_roles ur USING iam.roles r WHERE r.id = ur.role_id AND r.code = '{CustomerRoleCode}';
                DELETE FROM iam.roles WHERE code = '{CustomerRoleCode}';
                DELETE FROM iam.permissions WHERE code IN ({codes});
                """);
        }

        /// <summary>Al final de la reversa: la parte generada vuelve a exigir la ranura dejándole el valor por defecto '' (con el que
        /// rellenaría las nulas, que ya no hay); en la V6 la columna no tenía valor por defecto.</summary>
        private static void V7DropSlotDefault(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("ALTER TABLE sales.pc_build_lines ALTER COLUMN slot DROP DEFAULT;");

        private static string Sql(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    }
}
