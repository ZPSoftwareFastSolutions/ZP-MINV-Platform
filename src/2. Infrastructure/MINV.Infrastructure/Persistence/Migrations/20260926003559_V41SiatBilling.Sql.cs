using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MINV.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// V4.1 · SQL propio de PostgreSQL de la facturación SIAT (lo llama <see cref="V41SiatBilling"/>):
    /// <list type="number">
    /// <item>append-only (trigger <c>trg_append_only</c> y privilegios revocados) en los 9 libros fiscales nuevos;</item>
    /// <item>RLS por empresa en las 30 tablas nuevas (por descubrimiento: toda tabla con <c>tenant_id</c> sin política) y
    /// política RESTRICTIVA por sucursal en las 15 tablas de sucursal nuevas;</item>
    /// <item>función SECURITY DEFINER <c>billing.siat_active_tenants()</c> (empresas con la facturación activa, para el
    /// despachador del servidor, que corre antes de conocer la empresa);</item>
    /// <item>vista <c>billing.v_fiscal_document_totals</c>: totales DERIVADOS de las líneas con las fórmulas del SIN (los
    /// totales no se guardan);</item>
    /// <item>permisos <c>billing.*</c> y matriz rol-permiso de las empresas existentes (el módulo FISCAL_SIAT lo siembra
    /// la parte generada: <c>iam.modules</c> está en HasData);</item>
    /// <item>privilegios de <c>minv_app</c> y <c>minv_server</c> (solo si existen).</item>
    /// </list>
    /// </summary>
    public partial class V41SiatBilling
    {
        /// <summary>Tablas nuevas de la V4.1 (privilegios de los roles de aplicación). Una prueba verifica que coinciden con el
        /// modelo.</summary>
        internal static readonly string[] NewTablesV41 =
        [
            "billing.siat_settings", "billing.siat_environment_profiles", "billing.siat_branches", "billing.siat_points_of_sale",
            "billing.siat_cuis", "billing.siat_cufds", "billing.siat_catalog_items", "billing.siat_activities", "billing.siat_activity_sectors",
            "billing.siat_legends", "billing.siat_products", "billing.siat_sync_runs", "billing.product_siat_codes", "billing.unit_siat_codes",
            "billing.payment_method_siat_codes", "billing.customer_nit_checks", "billing.fiscal_documents", "billing.fiscal_note_references",
            "billing.fiscal_document_lines", "billing.fiscal_document_files", "billing.fiscal_document_events", "billing.fiscal_deliveries",
            "billing.significant_events", "billing.fiscal_packages", "billing.contingency_codes", "billing.siat_service_calls",
            "billing.mail_settings", "sales.sales_returns", "sales.sales_return_lines", "purchasing.supplier_invoice_fiscal",
        ];

        /// <summary>Tablas de sucursal nuevas (<c>IBranchScoped</c>): se suman a <see cref="V4MultiBranchCloud.BranchTables"/>.</summary>
        internal static readonly string[] BranchTablesV41 =
        [
            "billing.siat_points_of_sale", "billing.siat_cuis", "billing.siat_cufds", "billing.fiscal_documents", "billing.fiscal_note_references",
            "billing.fiscal_document_lines", "billing.fiscal_document_files", "billing.fiscal_document_events", "billing.fiscal_deliveries",
            "billing.significant_events", "billing.fiscal_packages", "billing.contingency_codes", "sales.sales_returns", "sales.sales_return_lines",
            "purchasing.supplier_invoice_fiscal",
        ];

        /// <summary>Libros append-only nuevos (<c>IAppendOnly</c>, regla F-11): se suman a los de la V3 y la V4.</summary>
        internal static readonly string[] AppendOnlyTablesV41 =
        [
            "billing.siat_cuis", "billing.siat_cufds", "billing.siat_sync_runs", "billing.customer_nit_checks", "billing.siat_service_calls",
            "billing.fiscal_document_lines", "billing.fiscal_document_files", "billing.fiscal_document_events", "billing.fiscal_deliveries",
        ];

        /// <summary>Esquemas donde se descubren las tablas con <c>tenant_id</c> sin política de empresa.</summary>
        internal static readonly string[] SchemasV41 =
            ["iam", "catalog", "warehouse", "inventory", "purchasing", "sales", "accounting", "integration", "billing"];

        /// <summary>Permisos de la facturación (copia fija de <c>PermissionCodes</c> al publicar la V4.1; una prueba lo compara).</summary>
        internal static readonly (string Code, string Description)[] BillingPermissions =
        [
            ("billing.view", "Consultar documentos fiscales, estado del SIAT y libros de ventas y compras"),
            ("billing.issue", "Emitir facturas (al vender) y reenviar documentos fiscales"),
            ("billing.void", "Anular y revertir documentos fiscales y emitir notas crédito-débito"),
            ("billing.contingency", "Gestionar eventos significativos, paquetes de contingencia y CAFC"),
            ("billing.configure", "Configurar la facturación SIAT: NIT, token, sucursales, puntos de venta, CUIS, CUFD, catálogos y homologación"),
        ];

        /// <summary>Matriz rol → permiso de la facturación (copia fija de <c>PermissionCodes.ForRole</c>; una prueba la compara).</summary>
        internal static readonly (string Role, string Permission)[] BillingRolePermissions =
        [
            ("ADMIN", "billing.view"), ("ADMIN", "billing.issue"), ("ADMIN", "billing.void"), ("ADMIN", "billing.contingency"),
            ("ADMIN", "billing.configure"),
            ("VENTAS", "billing.view"), ("VENTAS", "billing.issue"),
            ("CAJERO", "billing.view"), ("CAJERO", "billing.issue"),
            ("GERENCIA", "billing.view"), ("GERENCIA", "billing.void"), ("GERENCIA", "billing.contingency"),
            ("CONSULTA", "billing.view"),
        ];

        private static void V41Guards(MigrationBuilder migrationBuilder)
        {
            // 1. Append-only en los libros fiscales ---------------------------------------------------------------------
            foreach (var table in AppendOnlyTablesV41)
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON {table}
                        FOR EACH ROW EXECUTE FUNCTION iam.minv_append_only();
                    CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON {table}
                        FOR EACH STATEMENT EXECUTE FUNCTION iam.minv_append_only();
                    """);
            }

            // 2. RLS: empresa en toda tabla nueva con tenant_id y sucursal (RESTRICTIVA) en las de sucursal -------------------
            migrationBuilder.Sql($"""
                DO $$
                DECLARE r record;
                BEGIN
                    FOR r IN
                        SELECT c.table_schema, c.table_name
                        FROM information_schema.columns c
                        JOIN information_schema.tables t
                          ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE'
                        WHERE c.column_name = 'tenant_id' AND c.table_schema IN ('{string.Join("', '", SchemasV41)}')
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
            foreach (var table in BranchTablesV41)
            {
                migrationBuilder.Sql($"""
                    CREATE POLICY branch_isolation ON {table} AS RESTRICTIVE
                        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
                    """);
            }

            // 3. SECURITY DEFINER: empresas con la facturación activa (el despachador del servidor las recorre antes de fijar
            //    minv.tenant_id). Devuelve solo el id; search_path fijo; EXECUTE solo para minv_server (regla B-13).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION billing.siat_active_tenants() RETURNS SETOF uuid
                LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, billing AS $$
                    SELECT s.tenant_id FROM billing.siat_settings s WHERE s.is_enabled
                $$;
                REVOKE ALL ON FUNCTION billing.siat_active_tenants() FROM PUBLIC;
                """);

            // 4. Totales derivados por documento (regla F-06: no se guardan). Mismas fórmulas que FiscalDocument:
            //    subTotal = round2(cantidad × precio) − descuento; factura: total = Σ subTotal − descuento adicional,
            //    base = total − gift card; nota: devuelto = Σ subTotal (tx 2) − descuento prorrateado; IVA 13 % de la base.
            //    security_invoker: se aplican la RLS de empresa y de sucursal del que consulta.
            migrationBuilder.Sql("""
                CREATE VIEW billing.v_fiscal_document_totals WITH (security_invoker = true, security_barrier = true) AS
                SELECT t.*, round(t.total_subject_to_vat * 0.13, 2) AS vat_amount
                FROM (
                    SELECT d.tenant_id, d.branch_id, d.id AS document_id, d.kind, d.environment, d.document_sector, d.number, d.cuf,
                           d.issued_at, d.status, d.is_reverted,
                           l.lines_subtotal, d.additional_discount, d.gift_card_amount, l.original_total,
                           CASE WHEN d.kind = 'Invoice' THEN l.lines_subtotal - d.additional_discount
                                ELSE l.returned_subtotal - coalesce(n.discount_share, 0) END AS total_amount,
                           CASE WHEN d.kind = 'Invoice' THEN l.lines_subtotal - d.additional_discount - d.gift_card_amount
                                ELSE l.returned_subtotal - coalesce(n.discount_share, 0) END AS total_subject_to_vat,
                           CASE WHEN d.kind = 'CreditDebitNote' THEN l.returned_subtotal - coalesce(n.discount_share, 0) END AS returned_total
                    FROM billing.fiscal_documents d
                    LEFT JOIN billing.fiscal_note_references n ON n.document_id = d.id
                    CROSS JOIN LATERAL (
                        SELECT coalesce(sum(s.subtotal) FILTER (WHERE s.transaction_code IS NULL OR s.transaction_code = 1), 0) AS lines_subtotal,
                               coalesce(sum(s.subtotal) FILTER (WHERE s.transaction_code = 1), 0) AS original_total,
                               coalesce(sum(s.subtotal) FILTER (WHERE s.transaction_code = 2), 0) AS returned_subtotal
                        FROM (SELECT x.transaction_code, round(x.quantity * x.unit_price, 2) - coalesce(x.discount, 0) AS subtotal
                              FROM billing.fiscal_document_lines x
                              WHERE x.document_id = d.id) s) l
                ) t;
                """);

            // 5. Permisos de la facturación y matriz rol-permiso de las empresas existentes (las nuevas los reciben del
            //    aprovisionamiento con PermissionCodes).
            var permissions = string.Join(", ", BillingPermissions.Select(p => $"('{Sql(p.Code)}', '{Sql(p.Description)}')"));
            var matrix = string.Join(", ", BillingRolePermissions.Select(m => $"('{Sql(m.Role)}', '{Sql(m.Permission)}')"));
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
                """);

            // 6. Privilegios: minv_app (escritorio directo) y minv_server (nube; NOBYPASSRLS), solo si existen ---------
            var tables = string.Join(", ", NewTablesV41);
            var ledgers = string.Join(", ", AppendOnlyTablesV41);
            migrationBuilder.Sql($"""
                DO $$
                DECLARE r text;
                BEGIN
                    FOREACH r IN ARRAY ARRAY['minv_app', 'minv_server'] LOOP
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) THEN
                            EXECUTE format('GRANT USAGE ON SCHEMA billing TO %I', r);
                            EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON {tables} TO %I', r);
                            EXECUTE format('REVOKE UPDATE, DELETE, TRUNCATE ON {ledgers} FROM %I', r);
                            EXECUTE format('GRANT SELECT ON billing.v_fiscal_document_totals TO %I', r);
                        END IF;
                    END LOOP;
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'minv_server') THEN
                        GRANT EXECUTE ON FUNCTION billing.siat_active_tenants() TO minv_server;
                    END IF;
                END;
                $$;
                """);
        }

        /// <summary>Reversa del SQL propio (antes de que la parte generada borre las tablas): vista, función y permisos.</summary>
        private static void V41DropGuards(MigrationBuilder migrationBuilder)
        {
            var codes = string.Join(", ", BillingPermissions.Select(p => $"'{Sql(p.Code)}'"));
            migrationBuilder.Sql($"""
                DROP VIEW IF EXISTS billing.v_fiscal_document_totals;
                DROP FUNCTION IF EXISTS billing.siat_active_tenants();
                DELETE FROM iam.role_permissions rp USING iam.permissions p
                    WHERE p.id = rp.permission_id AND p.code IN ({codes});
                DELETE FROM iam.permissions WHERE code IN ({codes});
                """);
        }

        /// <summary>El esquema queda vacío cuando la parte generada ya borró las tablas.</summary>
        private static void V41DropSchema(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS billing;");

        private static string Sql(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    }
}
