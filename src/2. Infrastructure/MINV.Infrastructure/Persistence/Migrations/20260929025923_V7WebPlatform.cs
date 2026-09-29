using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MINV.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// V7 · Plataforma web: carrito de compras sobre el armado (<c>sales.pc_builds.kind</c>, datos para la factura de la reserva
    /// <c>buyer_*</c> y ranura opcional en <c>sales.pc_build_lines</c>), cuentas de cliente (<c>sales.customer_accounts</c>),
    /// correo de la reserva (<c>integration.outgoing_mails</c>, <c>outgoing_mail_dispatch</c> y <c>outgoing_mail_attempts</c>)
    /// y el canal <c>web</c> en la auditoría. 153 → 157 tablas en 10 esquemas. El SQL propio de PostgreSQL (guardia, relleno del
    /// tipo, RLS, append-only, triggers del carrito, función del despachador del correo, rol CLIENTE y permisos, privilegios)
    /// está en <c>V7WebPlatform.Sql.cs</c>. Correcciones de la comprobación de normalización (paquete B4c,
    /// <c>docs/database/normalizacion-v7.md</c>): CHECK del dominio de 14 columnas de estado y de 10 listas cerradas de tablas
    /// anteriores a la V7 (sus valores salen de los enums, sin cambios desde la V3) y el índice completo de la FK de las
    /// garantías a la serie.
    /// </summary>
    public partial class V7WebPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            V7Guard(migrationBuilder);

            migrationBuilder.DropCheckConstraint(
                name: "ck_pc_build_lines_ranura",
                schema: "sales",
                table: "pc_build_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_logs_canal",
                schema: "iam",
                table: "audit_logs");

            migrationBuilder.AddColumn<string>(
                name: "buyer_complement",
                schema: "sales",
                table: "pc_builds",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "buyer_document_number",
                schema: "sales",
                table: "pc_builds",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "buyer_document_type",
                schema: "sales",
                table: "pc_builds",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "buyer_name",
                schema: "sales",
                table: "pc_builds",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "sales",
                table: "pc_builds",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "slot",
                schema: "sales",
                table: "pc_build_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.CreateTable(
                name: "customer_accounts",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_accounts", x => x.id);
                    table.UniqueConstraint("ak_customer_accounts_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_customer_accounts_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customer_accounts_tenant_id_customer_id",
                        columns: x => new { x.tenant_id, x.customer_id },
                        principalSchema: "sales",
                        principalTable: "customers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customer_accounts_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outgoing_mails",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    pc_build_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outgoing_mails", x => x.id);
                    table.UniqueConstraint("ak_outgoing_mails_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_outgoing_mails_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_outgoing_mails_destinatario", "recipient = lower(recipient) AND recipient ~ '^[^@[:space:]]+@[^@[:space:]]+\\.[^@[:space:]]+$'");
                    table.CheckConstraint("ck_outgoing_mails_tipo", "kind IN ('ReservationConfirmed')");
                    table.ForeignKey(
                        name: "fk_outgoing_mails_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outgoing_mails_tenant_id_branch_id_pc_build_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.pc_build_id },
                        principalSchema: "sales",
                        principalTable: "pc_builds",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outgoing_mails_tenant_id_requested_by_user_id",
                        columns: x => new { x.tenant_id, x.requested_by_user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outgoing_mail_attempts",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    outgoing_mail_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outgoing_mail_attempts", x => x.id);
                    table.UniqueConstraint("ak_outgoing_mail_attempts_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_outgoing_mail_attempts_duracion", "duration_ms >= 0");
                    table.CheckConstraint("ck_outgoing_mail_attempts_error", "succeeded = (error IS NULL)");
                    table.CheckConstraint("ck_outgoing_mail_attempts_intento", "attempt BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_outgoing_mail_attempts_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outgoing_mail_attempts_tenant_id_outgoing_mail_id",
                        columns: x => new { x.tenant_id, x.outgoing_mail_id },
                        principalSchema: "integration",
                        principalTable: "outgoing_mails",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outgoing_mail_dispatch",
                schema: "integration",
                columns: table => new
                {
                    outgoing_mail_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outgoing_mail_dispatch", x => x.outgoing_mail_id);
                    table.CheckConstraint("ck_outgoing_mail_dispatch_estado", "status IN ('Pending', 'Sent', 'Exhausted', 'Cancelled')");
                    table.CheckConstraint("ck_outgoing_mail_dispatch_fin", "(status = 'Pending') = (completed_at IS NULL)");
                    table.CheckConstraint("ck_outgoing_mail_dispatch_intentado", "status NOT IN ('Sent', 'Exhausted') OR attempts >= 1");
                    table.CheckConstraint("ck_outgoing_mail_dispatch_intentos", "attempts BETWEEN 0 AND 5");
                    table.ForeignKey(
                        name: "fk_outgoing_mail_dispatch_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outgoing_mail_dispatch_tenant_id_outgoing_mail_id",
                        columns: x => new { x.tenant_id, x.outgoing_mail_id },
                        principalSchema: "integration",
                        principalTable: "outgoing_mails",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            V7Backfill(migrationBuilder);

            migrationBuilder.AddCheckConstraint(
                name: "ck_pc_builds_factura_complemento",
                schema: "sales",
                table: "pc_builds",
                sql: "buyer_complement IS NULL OR buyer_document_type = 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pc_builds_factura_documento",
                schema: "sales",
                table: "pc_builds",
                sql: "(buyer_document_type IS NULL) = (buyer_document_number IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pc_builds_factura_nombre",
                schema: "sales",
                table: "pc_builds",
                sql: "buyer_name IS NULL OR buyer_document_type IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pc_builds_factura_tipo",
                schema: "sales",
                table: "pc_builds",
                sql: "buyer_document_type IS NULL OR buyer_document_type BETWEEN 1 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pc_builds_tipo",
                schema: "sales",
                table: "pc_builds",
                sql: "kind IN ('Build', 'Cart')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pc_builds_tipo_publicado",
                schema: "sales",
                table: "pc_builds",
                sql: "NOT published_to_web OR kind = 'Build'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pc_build_lines_ranura",
                schema: "sales",
                table: "pc_build_lines",
                sql: "slot IS NULL OR slot IN ('Cpu', 'Motherboard', 'Ram', 'Gpu', 'Storage', 'Psu', 'Case', 'Cooler', 'Monitor', 'Peripheral', 'Software', 'Service')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_logs_canal",
                schema: "iam",
                table: "audit_logs",
                sql: "channel IS NULL OR channel IN ('desktop', 'cloud', 'api', 'storefront', 'web')");

            // B4c · Comprobación de normalización (E13 y E14): dominio de los estados y de las listas cerradas de tablas anteriores
            // a la V7. Los datos existentes ya cumplen (los escribe el dominio con los mismos enums); si una fila no cumpliera, el
            // CHECK detiene la migración entera sin cambios (la migración corre en una transacción).
            migrationBuilder.AddCheckConstraint(
                name: "ck_warranty_claim_events_estado",
                schema: "service",
                table: "warranty_claim_events",
                sql: "status IN ('Received', 'Diagnosing', 'SentToSupplier', 'Repaired', 'Replaced', 'Rejected', 'Delivered')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_supplier_invoices_estado",
                schema: "purchasing",
                table: "supplier_invoices",
                sql: "status IN ('Draft', 'Posted', 'Paid', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_supplier_addresses_tipo",
                schema: "purchasing",
                table: "supplier_addresses",
                sql: "address_type IN ('Fiscal', 'Billing', 'Shipping', 'Pickup')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_transfer_events_estado",
                schema: "inventory",
                table: "stock_transfer_events",
                sql: "status IN ('Pending', 'Dispatched', 'Received', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_reservations_estado",
                schema: "inventory",
                table: "stock_reservations",
                sql: "status IN ('Active', 'Consumed', 'Released', 'Expired')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_adjustments_estado",
                schema: "inventory",
                table: "stock_adjustments",
                sql: "status IN ('Draft', 'Posted', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_siat_sync_runs_ambiente",
                schema: "billing",
                table: "siat_sync_runs",
                sql: "environment IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_siat_service_calls_ambiente",
                schema: "billing",
                table: "siat_service_calls",
                sql: "environment IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sales_orders_estado",
                schema: "sales",
                table: "sales_orders",
                sql: "status IN ('Draft', 'Confirmed', 'Fulfilled', 'Invoiced', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_purchase_returns_estado",
                schema: "purchasing",
                table: "purchase_returns",
                sql: "status IN ('Draft', 'Posted')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_purchase_orders_estado",
                schema: "purchasing",
                table: "purchase_orders",
                sql: "status IN ('Draft', 'Approved', 'PartiallyReceived', 'Received', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_products_trazabilidad",
                schema: "catalog",
                table: "products",
                sql: "tracking_mode IN ('None', 'Batch', 'Serial')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pos_sessions_estado",
                schema: "sales",
                table: "pos_sessions",
                sql: "status IN ('Open', 'Closed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_physical_counts_estado",
                schema: "inventory",
                table: "physical_counts",
                sql: "status IN ('Open', 'Posted', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_journal_entries_estado",
                schema: "accounting",
                table: "journal_entries",
                sql: "status IN ('Draft', 'Posted')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_estado",
                schema: "sales",
                table: "invoices",
                sql: "status IN ('Draft', 'Issued', 'Voided')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_hardware_tokens_tipo",
                schema: "iam",
                table: "hardware_tokens",
                sql: "kind IN ('Workstation', 'PosTerminal', 'Scanner', 'Printer')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_goods_receipts_estado",
                schema: "purchasing",
                table: "goods_receipts",
                sql: "status IN ('Draft', 'Posted')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fiscal_periods_estado",
                schema: "accounting",
                table: "fiscal_periods",
                sql: "status IN ('Open', 'Closed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fiscal_packages_tipo",
                schema: "billing",
                table: "fiscal_packages",
                sql: "document_type IN (1, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_addresses_tipo",
                schema: "sales",
                table: "customer_addresses",
                sql: "address_type IN ('Fiscal', 'Billing', 'Shipping', 'Pickup')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_movements_sentido",
                schema: "sales",
                table: "cash_movements",
                sql: "direction IN ('In', 'Out')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_logs_resultado",
                schema: "iam",
                table: "audit_logs",
                sql: "outcome IN ('Succeeded', 'Rejected', 'Failed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_tipo",
                schema: "accounting",
                table: "accounts",
                sql: "account_type IN ('Asset', 'Liability', 'Equity', 'Revenue', 'Expense')");

            migrationBuilder.CreateIndex(
                name: "ux_customer_accounts_tenant_id_customer_id",
                schema: "sales",
                table: "customer_accounts",
                columns: new[] { "tenant_id", "customer_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_customer_accounts_tenant_id_user_id",
                schema: "sales",
                table: "customer_accounts",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_mail_attempts_tenant_id_outgoing_mail_id",
                schema: "integration",
                table: "outgoing_mail_attempts",
                columns: new[] { "tenant_id", "outgoing_mail_id" });

            migrationBuilder.CreateIndex(
                name: "ux_outgoing_mail_attempts_outgoing_mail_id_attempt",
                schema: "integration",
                table: "outgoing_mail_attempts",
                columns: new[] { "outgoing_mail_id", "attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_mail_dispatch_next_attempt_at",
                schema: "integration",
                table: "outgoing_mail_dispatch",
                column: "next_attempt_at",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ux_outgoing_mail_dispatch_tenant_id_outgoing_mail_id",
                schema: "integration",
                table: "outgoing_mail_dispatch",
                columns: new[] { "tenant_id", "outgoing_mail_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_mails_tenant_id_branch_id_pc_build_id",
                schema: "integration",
                table: "outgoing_mails",
                columns: new[] { "tenant_id", "branch_id", "pc_build_id" });

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_mails_tenant_id_recipient_requested_at",
                schema: "integration",
                table: "outgoing_mails",
                columns: new[] { "tenant_id", "recipient", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_mails_tenant_id_requested_at",
                schema: "integration",
                table: "outgoing_mails",
                columns: new[] { "tenant_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_mails_tenant_id_requested_by_user_id",
                schema: "integration",
                table: "outgoing_mails",
                columns: new[] { "tenant_id", "requested_by_user_id" });

            // B4c · Comprobación de normalización (E06): la FK de las garantías a la serie solo tenía el índice único parcial de los
            // casos abiertos
            migrationBuilder.CreateIndex(
                name: "ix_warranty_claims_tenant_id_serial_number_id",
                schema: "service",
                table: "warranty_claims",
                columns: new[] { "tenant_id", "serial_number_id" });

            V7Guards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            V7DropGuards(migrationBuilder);

            // B4c · Comprobación de normalización: índice y CHECK de dominio agregados a tablas anteriores a la V7
            migrationBuilder.DropIndex(
                name: "ix_warranty_claims_tenant_id_serial_number_id",
                schema: "service",
                table: "warranty_claims");

            migrationBuilder.DropCheckConstraint(
                name: "ck_warranty_claim_events_estado",
                schema: "service",
                table: "warranty_claim_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_supplier_invoices_estado",
                schema: "purchasing",
                table: "supplier_invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_supplier_addresses_tipo",
                schema: "purchasing",
                table: "supplier_addresses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_transfer_events_estado",
                schema: "inventory",
                table: "stock_transfer_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_reservations_estado",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_adjustments_estado",
                schema: "inventory",
                table: "stock_adjustments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_siat_sync_runs_ambiente",
                schema: "billing",
                table: "siat_sync_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_siat_service_calls_ambiente",
                schema: "billing",
                table: "siat_service_calls");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sales_orders_estado",
                schema: "sales",
                table: "sales_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_purchase_returns_estado",
                schema: "purchasing",
                table: "purchase_returns");

            migrationBuilder.DropCheckConstraint(
                name: "ck_purchase_orders_estado",
                schema: "purchasing",
                table: "purchase_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_products_trazabilidad",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pos_sessions_estado",
                schema: "sales",
                table: "pos_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_physical_counts_estado",
                schema: "inventory",
                table: "physical_counts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_journal_entries_estado",
                schema: "accounting",
                table: "journal_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_estado",
                schema: "sales",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_hardware_tokens_tipo",
                schema: "iam",
                table: "hardware_tokens");

            migrationBuilder.DropCheckConstraint(
                name: "ck_goods_receipts_estado",
                schema: "purchasing",
                table: "goods_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fiscal_periods_estado",
                schema: "accounting",
                table: "fiscal_periods");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fiscal_packages_tipo",
                schema: "billing",
                table: "fiscal_packages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_addresses_tipo",
                schema: "sales",
                table: "customer_addresses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_movements_sentido",
                schema: "sales",
                table: "cash_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_logs_resultado",
                schema: "iam",
                table: "audit_logs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_tipo",
                schema: "accounting",
                table: "accounts");

            migrationBuilder.DropTable(
                name: "customer_accounts",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "outgoing_mail_attempts",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "outgoing_mail_dispatch",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "outgoing_mails",
                schema: "integration");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pc_builds_factura_complemento",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pc_builds_factura_documento",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pc_builds_factura_nombre",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pc_builds_factura_tipo",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pc_builds_tipo",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pc_builds_tipo_publicado",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pc_build_lines_ranura",
                schema: "sales",
                table: "pc_build_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_logs_canal",
                schema: "iam",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "buyer_complement",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropColumn(
                name: "buyer_document_number",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropColumn(
                name: "buyer_document_type",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropColumn(
                name: "buyer_name",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.AlterColumn<string>(
                name: "slot",
                schema: "sales",
                table: "pc_build_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_pc_build_lines_ranura",
                schema: "sales",
                table: "pc_build_lines",
                sql: "slot IN ('Cpu', 'Motherboard', 'Ram', 'Gpu', 'Storage', 'Psu', 'Case', 'Cooler', 'Monitor', 'Peripheral', 'Software', 'Service')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_logs_canal",
                schema: "iam",
                table: "audit_logs",
                sql: "channel IS NULL OR channel IN ('desktop', 'cloud', 'api', 'storefront')");

            V7DropSlotDefault(migrationBuilder);
        }
    }
}
