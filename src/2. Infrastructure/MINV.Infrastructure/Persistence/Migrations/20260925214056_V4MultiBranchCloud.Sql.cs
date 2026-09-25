using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MINV.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// V4 · SQL propio de PostgreSQL de la migración multi-sucursal (lo llama <see cref="V4MultiBranchCloud"/>):
    /// <list type="number">
    /// <item>guardia: las transferencias de la V3 no existían (no había caso de uso): la tabla debe estar vacía;</item>
    /// <item>relleno de <c>branch_id</c> en los datos existentes siguiendo la jerarquía (almacén → zona → … → posición →
    /// existencia → movimiento; turno → caja; factura → pedido…), con los triggers append-only e inmutables pausados SOLO
    /// durante el relleno, y la secuencia del costo promedio;</item>
    /// <item>defensa en profundidad: append-only en las tablas nuevas, RLS por empresa en las tablas nuevas y política
    /// RESTRICTIVA por sucursal (<c>iam.branch_visible</c>, variable <c>minv.branch_ids</c>);</item>
    /// <item>funciones SECURITY DEFINER mínimas para el servidor en la nube y el gateway (resolver una API Key o una
    /// sesión antes de conocer la empresa, reclamar entregas de webhooks con SKIP LOCKED, refrescar reportes);</item>
    /// <item>modelo de lectura (<c>reporting</c>): vistas materializadas + vistas security_barrier filtradas;</item>
    /// <item>datos V4 de las empresas existentes (permisos, matriz rol-permiso y cuentas entre sucursales) y privilegios de
    /// los roles <c>minv_app</c> (escritorio con conexión directa) y <c>minv_server</c> (nube, sin BYPASSRLS).</item>
    /// </list>
    /// </summary>
    public partial class V4MultiBranchCloud
    {
        /// <summary>Tablas por sucursal (<c>IBranchScoped</c>). Una prueba verifica que coinciden con el modelo.</summary>
        internal static readonly string[] BranchTables =
        [
            "warehouse.zones", "warehouse.aisles", "warehouse.racks", "warehouse.shelves", "warehouse.bins", "warehouse.bin_assignments",
            "catalog.product_stock_policies",
            "inventory.stock_levels", "inventory.stock_movements", "inventory.stock_reservations", "inventory.stock_adjustments",
            "inventory.stock_adjustment_lines", "inventory.physical_counts", "inventory.physical_count_lines", "inventory.stock_transfer_movements",
            "purchasing.purchase_orders", "purchasing.purchase_order_lines", "purchasing.goods_receipts", "purchasing.goods_receipt_lines",
            "purchasing.purchase_returns", "purchasing.purchase_return_lines", "purchasing.supplier_invoices", "purchasing.supplier_invoice_lines",
            "sales.pos_registers", "sales.pos_sessions", "sales.cash_movements", "sales.sales_orders", "sales.sales_order_lines", "sales.invoices",
            "sales.invoice_lines", "sales.payments", "sales.external_orders",
            "accounting.journal_entries", "accounting.journal_lines", "accounting.average_cost_history",
        ];

        /// <summary>Tablas entre dos sucursales (<c>IInterBranch</c>): las ven el origen y el destino.</summary>
        internal static readonly string[] InterBranchTables =
        [
            "inventory.stock_transfers", "inventory.stock_transfer_lines", "inventory.stock_transfer_line_batches",
            "inventory.stock_transfer_discrepancies", "inventory.stock_transfer_events",
        ];

        /// <summary>Libros append-only nuevos de la V4 (se suman a los de <see cref="GuardsRlsAndViews.AppendOnlyTables"/>).</summary>
        internal static readonly string[] AppendOnlyTablesV4 =
        [
            "inventory.stock_transfer_movements", "inventory.stock_transfer_discrepancies", "inventory.stock_transfer_events",
            "inventory.stock_transfer_line_batches", "integration.outbox_events", "integration.webhook_deliveries", "sales.external_orders",
            "iam.processed_requests",
        ];

        internal static readonly string[] SchemasV4 = ["iam", "catalog", "warehouse", "inventory", "purchasing", "sales", "accounting", "integration"];

        /// <summary>Tablas cuyo relleno necesita pausar sus triggers (append-only o asientos inmutables).</summary>
        private static readonly string[] GuardedDuringBackfill =
        [
            "inventory.stock_movements", "sales.cash_movements", "sales.payments", "accounting.average_cost_history",
            "accounting.journal_entries", "accounting.journal_lines",
        ];

        private static void V4Guard(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM inventory.stock_transfers) THEN
                        RAISE EXCEPTION 'M-INV V4: inventory.stock_transfers tiene filas de la V3 (no había caso de uso de transferencias): revíselas antes de migrar.';
                    END IF;
                END;
                $$;
                """);

        private static void V4Backfill(MigrationBuilder migrationBuilder)
        {
            foreach (var table in GuardedDuringBackfill)
            {
                migrationBuilder.Sql($"ALTER TABLE {table} DISABLE TRIGGER USER;");
            }
            migrationBuilder.Sql("""
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
                """);
            // Lo que no tiene camino (asientos, facturas de proveedor sin recepción…): la sucursal principal de la empresa
            // (la V3 tenía una sola sucursal por empresa). Las líneas heredan de su cabecera.
            migrationBuilder.Sql("""
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
                """);
            migrationBuilder.Sql($"""
                DO $$
                DECLARE t text; n bigint;
                BEGIN
                    FOREACH t IN ARRAY ARRAY['{string.Join("', '", BranchTables)}'] LOOP
                        CONTINUE WHEN to_regclass(t) IS NULL;   -- tablas nuevas de la V4 (se crean después, vacías)
                        EXECUTE format('SELECT count(*) FROM %s WHERE branch_id = ''00000000-0000-0000-0000-000000000000''', t) INTO n;
                        IF n > 0 THEN
                            RAISE EXCEPTION 'M-INV V4: % filas de % quedaron sin sucursal', n, t;
                        END IF;
                    END LOOP;
                END;
                $$;
                """);
            foreach (var table in GuardedDuringBackfill)
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE TRIGGER USER;");
            }
        }

        private static void V4Guards(MigrationBuilder migrationBuilder)
        {
            // 1. Append-only en los libros nuevos ------------------------------------------------------------------------
            foreach (var table in AppendOnlyTablesV4)
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON {table}
                        FOR EACH ROW EXECUTE FUNCTION iam.minv_append_only();
                    CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON {table}
                        FOR EACH STATEMENT EXECUTE FUNCTION iam.minv_append_only();
                    """);
            }

            // 2. RLS: empresa en las tablas nuevas y sucursal (RESTRICTIVA: se suma a la de empresa) ----------------------
            migrationBuilder.Sql($"""
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
                        WHERE c.column_name = 'tenant_id' AND c.table_schema IN ('{string.Join("', '", SchemasV4)}')
                    LOOP
                        EXECUTE format('ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY', r.table_schema, r.table_name);
                        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I.%I', r.table_schema, r.table_name);
                        EXECUTE format('CREATE POLICY tenant_isolation ON %I.%I USING (tenant_id = iam.current_tenant_id()) '
                                       'WITH CHECK (tenant_id = iam.current_tenant_id())', r.table_schema, r.table_name);
                    END LOOP;
                END;
                $$;
                """);
            foreach (var table in BranchTables)
            {
                migrationBuilder.Sql($"""
                    CREATE POLICY branch_isolation ON {table} AS RESTRICTIVE
                        USING (iam.branch_visible(branch_id)) WITH CHECK (iam.branch_visible(branch_id));
                    """);
            }
            foreach (var table in InterBranchTables)
            {
                migrationBuilder.Sql($"""
                    CREATE POLICY branch_isolation ON {table} AS RESTRICTIVE
                        USING (iam.branch_visible(from_branch_id) OR iam.branch_visible(to_branch_id))
                        WITH CHECK (iam.branch_visible(from_branch_id) OR iam.branch_visible(to_branch_id));
                    """);
            }

            // 3. Funciones SECURITY DEFINER (search_path fijo; EXECUTE solo para minv_server) --------------------------
            migrationBuilder.Sql("""
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
                """);

            // 4. Modelo de lectura (OLAP): vistas materializadas + vistas filtradas ------------------------------------
            migrationBuilder.Sql("""
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
                """);

            // 5. Datos V4 de las empresas existentes (las nuevas los reciben del aprovisionamiento) --------------------
            migrationBuilder.Sql("""
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
                """);

            // 6. Privilegios: minv_app (escritorio directo) y minv_server (nube y gateway; NOBYPASSRLS) --------------
            var schemas = string.Join(", ", SchemasV4);
            var ledgers = string.Join(", ", GuardsRlsAndViews.AppendOnlyTables.Concat(AppendOnlyTablesV4));
            migrationBuilder.Sql($"""
                DO $$
                DECLARE r text;
                BEGIN
                    FOREACH r IN ARRAY ARRAY['minv_app', 'minv_server'] LOOP
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) THEN
                            EXECUTE format('GRANT USAGE ON SCHEMA {schemas}, reporting TO %I', r);
                            EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {schemas} TO %I', r);
                            EXECUTE format('REVOKE UPDATE, DELETE, TRUNCATE ON {ledgers} FROM %I', r);
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
                """);
        }

        /// <summary>Estadísticas del planificador después del relleno (ANALYZE sí se admite dentro de la transacción).</summary>
        private static void V4Analyze(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(PostgresMaintenance.AnalyzeSql);

        private static void V4DropGuards(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP VIEW IF EXISTS inventory.v_transfer_breaches;
                DROP FUNCTION IF EXISTS reporting.refresh_all();
                DROP SCHEMA IF EXISTS reporting CASCADE;
                DROP FUNCTION IF EXISTS integration.claim_deliveries(integer, integer);
                DROP FUNCTION IF EXISTS iam.resolve_session(text);
                DROP FUNCTION IF EXISTS integration.resolve_api_key(text);
                DO $$
                DECLARE r record;
                BEGIN
                    FOR r IN SELECT schemaname, tablename FROM pg_policies WHERE policyname = 'branch_isolation' LOOP
                        EXECUTE format('DROP POLICY branch_isolation ON %I.%I', r.schemaname, r.tablename);
                    END LOOP;
                END;
                $$;
                DROP FUNCTION IF EXISTS iam.branch_visible(uuid);
                DELETE FROM accounting.accounts a WHERE a.code IN ('1.1.06', '2.1.04')
                    AND NOT EXISTS (SELECT 1 FROM accounting.journal_lines l WHERE l.account_id = a.id);
                """);
        }
    }
}
