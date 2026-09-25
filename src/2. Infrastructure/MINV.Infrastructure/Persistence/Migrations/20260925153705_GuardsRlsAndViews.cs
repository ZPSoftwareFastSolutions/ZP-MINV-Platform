using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MINV.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Reglas que viven en PostgreSQL además del dominio (defensa en profundidad):
    /// 1) libros mayores append-only (UPDATE, DELETE y TRUNCATE prohibidos);
    /// 2) un solo valor por atributo en cada variante (la tabla 5FN no guarda attribute_id);
    /// 3) asientos contabilizados cuadrados e inmutables;
    /// 4) Row Level Security por tenant (variable de sesión minv.tenant_id);
    /// 5) vistas de lectura heredadas de la V2.1 y la verificación de conservación;
    /// 6) permisos del rol de la aplicación (minv_app) si existe.
    /// Requiere PostgreSQL 15 o superior (security_invoker y NULLS NOT DISTINCT).
    /// </summary>
    public partial class GuardsRlsAndViews : Migration
    {
        internal static readonly string[] AppendOnlyTables =
        [
            "inventory.stock_movements", "iam.audit_logs", "iam.access_logs", "sales.cash_movements", "sales.payments",
            "accounting.exchange_rates", "accounting.average_cost_history",
        ];

        private static readonly string[] Schemas = ["iam", "catalog", "warehouse", "inventory", "purchasing", "sales", "accounting"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Append-only ------------------------------------------------------------------------------------------
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION iam.minv_append_only() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'M-INV: %.% es append-only: % no permitido (corrija con un movimiento compensatorio)',
                        TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP USING ERRCODE = 'P0001';
                END;
                $$;
                """);
            foreach (var table in AppendOnlyTables)
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON {table}
                        FOR EACH ROW EXECUTE FUNCTION iam.minv_append_only();
                    CREATE TRIGGER trg_append_only_truncate BEFORE TRUNCATE ON {table}
                        FOR EACH STATEMENT EXECUTE FUNCTION iam.minv_append_only();
                    """);
            }

            // 2. Un valor por atributo en cada variante -----------------------------------------------------------------
            migrationBuilder.Sql("""
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
                """);

            // 3. Asientos: contabilizados = cuadrados e inmutables -------------------------------------------------------
            migrationBuilder.Sql("""
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
                """);

            // 4. Row Level Security por tenant ------------------------------------------------------------------------------
            migrationBuilder.Sql("""
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
                """);

            // 5. Vistas de lectura (heredadas de la V2.1) y verificación de conservación --------------------------------
            migrationBuilder.Sql("""
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
                """);

            // 6. Permisos del rol de la aplicación (si existe: lo crea scripts/db_init.sql) ---------------------------
            var schemas = string.Join(", ", Schemas);
            var ledgers = string.Join(", ", AppendOnlyTables);
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'minv_app') THEN
                        GRANT USAGE ON SCHEMA {schemas} TO minv_app;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {schemas} TO minv_app;
                        GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA {schemas} TO minv_app;
                        REVOKE UPDATE, DELETE, TRUNCATE ON {ledgers} FROM minv_app;
                        REVOKE INSERT, UPDATE, DELETE ON iam.modules, iam.__ef_migrations_history FROM minv_app;
                    END IF;
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP VIEW IF EXISTS iam.v_activity;
                DROP VIEW IF EXISTS inventory.v_conservation_breaches;
                DROP VIEW IF EXISTS inventory.v_stock_by_variant;
                DO $$
                DECLARE r record;
                BEGIN
                    FOR r IN
                        SELECT schemaname, tablename FROM pg_policies WHERE policyname = 'tenant_isolation'
                    LOOP
                        EXECUTE format('DROP POLICY tenant_isolation ON %I.%I', r.schemaname, r.tablename);
                        EXECUTE format('ALTER TABLE %I.%I DISABLE ROW LEVEL SECURITY', r.schemaname, r.tablename);
                    END LOOP;
                END;
                $$;
                DROP FUNCTION IF EXISTS iam.current_tenant_id();
                DROP TRIGGER IF EXISTS trg_journal_line_balanced ON accounting.journal_lines;
                DROP TRIGGER IF EXISTS trg_journal_entry_balanced ON accounting.journal_entries;
                DROP TRIGGER IF EXISTS trg_journal_line_immutable ON accounting.journal_lines;
                DROP TRIGGER IF EXISTS trg_journal_entry_immutable ON accounting.journal_entries;
                DROP FUNCTION IF EXISTS accounting.minv_journal_entry_balanced();
                DROP FUNCTION IF EXISTS accounting.minv_journal_line_balanced();
                DROP FUNCTION IF EXISTS accounting.minv_check_journal_balance(uuid);
                DROP FUNCTION IF EXISTS accounting.minv_journal_line_immutable();
                DROP FUNCTION IF EXISTS accounting.minv_journal_entry_immutable();
                DROP TRIGGER IF EXISTS trg_variant_single_value ON catalog.product_variant_attributes;
                DROP FUNCTION IF EXISTS catalog.minv_variant_single_value();
                """);
            foreach (var table in AppendOnlyTables)
            {
                migrationBuilder.Sql($"""
                    DROP TRIGGER IF EXISTS trg_append_only ON {table};
                    DROP TRIGGER IF EXISTS trg_append_only_truncate ON {table};
                    """);
            }
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS iam.minv_append_only();");
        }
    }
}
