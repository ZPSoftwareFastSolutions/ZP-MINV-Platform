using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MINV.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// V4.2 · Edición Tecnología: fichas técnicas tipadas (especificaciones, opciones, valores y perfil técnico del producto),
    /// series e IMEI por variante con su bitácora y las series de cada línea de venta, devolución y transferencia, armador de
    /// PC (cotizaciones) y garantías y RMA (esquema nuevo <c>service</c>). 140 → 152 tablas en 10 esquemas. El SQL propio de
    /// PostgreSQL (guardia, relleno de las series, RLS, append-only, vista de control, datos y privilegios) está en
    /// <c>V42TechRetail.Sql.cs</c>.
    /// </summary>
    public partial class V42TechRetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            V42Guard(migrationBuilder);

            migrationBuilder.DropForeignKey(
                name: "fk_serial_numbers_tenant_id_batch_id",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.DropForeignKey(
                name: "fk_serial_numbers_tenant_id_stock_level_id",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.DropIndex(
                name: "ix_stock_levels_tenant_id_batch_id",
                schema: "inventory",
                table: "stock_levels");

            migrationBuilder.DropIndex(
                name: "ix_serial_numbers_tenant_id_batch_id",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.DropIndex(
                name: "ix_serial_numbers_tenant_id_stock_level_id",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.DropIndex(
                name: "ux_serial_numbers_batch_id_serial",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.DropIndex(
                name: "ix_batches_tenant_id_variant_id",
                schema: "inventory",
                table: "batches");

            migrationBuilder.EnsureSchema(
                name: "service");

            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "inventory",
                table: "serial_numbers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "received_at",
                schema: "inventory",
                table: "serial_numbers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "variant_id",
                schema: "inventory",
                table: "serial_numbers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddUniqueConstraint(
                name: "ak_stock_levels_tenant_id_batch_id_id",
                schema: "inventory",
                table: "stock_levels",
                columns: new[] { "tenant_id", "batch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_batches_tenant_id_variant_id_id",
                schema: "inventory",
                table: "batches",
                columns: new[] { "tenant_id", "variant_id", "id" });

            V42Backfill(migrationBuilder);

            migrationBuilder.CreateTable(
                name: "pc_builds",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quoted_with_errors = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    quoted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pc_builds", x => x.id);
                    table.UniqueConstraint("ak_pc_builds_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_pc_builds_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_pc_builds_cotizacion", "status <> 'Quoted' OR quoted_at IS NOT NULL");
                    table.CheckConstraint("ck_pc_builds_estado", "status IN ('Draft', 'Quoted', 'Sold', 'Cancelled')");
                    table.CheckConstraint("ck_pc_builds_marcado", "NOT quoted_with_errors OR quoted_at IS NOT NULL");
                    table.CheckConstraint("ck_pc_builds_venta", "(status = 'Sold') = (invoice_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_pc_builds_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pc_builds_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "warehouse",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pc_builds_tenant_id_branch_id_invoice_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.invoice_id },
                        principalSchema: "sales",
                        principalTable: "invoices",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pc_builds_tenant_id_created_by_user_id",
                        columns: x => new { x.tenant_id, x.created_by_user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pc_builds_tenant_id_customer_id",
                        columns: x => new { x.tenant_id, x.customer_id },
                        principalSchema: "sales",
                        principalTable: "customers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_tech_profiles",
                schema: "catalog",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    warranty_months = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_tech_profiles", x => new { x.tenant_id, x.product_id });
                    table.CheckConstraint("ck_product_tech_profiles_garantia", "warranty_months BETWEEN 0 AND 120");
                    table.CheckConstraint("ck_product_tech_profiles_serie", "serial_kind IN ('Serial', 'Imei')");
                    table.ForeignKey(
                        name: "fk_product_tech_profiles_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_tech_profiles_tenant_id_product_id",
                        columns: x => new { x.tenant_id, x.product_id },
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_order_line_serials",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_number_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_order_line_serials", x => x.id);
                    table.UniqueConstraint("ak_sales_order_line_serials_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_sales_order_line_serials_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_sales_order_line_serials_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_order_line_serials_tenant_id_branch_id_sales__8cfea487",
                        columns: x => new { x.tenant_id, x.branch_id, x.sales_order_line_id },
                        principalSchema: "sales",
                        principalTable: "sales_order_lines",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_order_line_serials_tenant_id_serial_number_id",
                        columns: x => new { x.tenant_id, x.serial_number_id },
                        principalSchema: "inventory",
                        principalTable: "serial_numbers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_return_line_serials",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_return_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_number_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_return_line_serials", x => x.id);
                    table.UniqueConstraint("ak_sales_return_line_serials_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_sales_return_line_serials_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_sales_return_line_serials_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_return_line_serials_tenant_id_branch_id_sales_0a3fc4a1",
                        columns: x => new { x.tenant_id, x.branch_id, x.sales_return_line_id },
                        principalSchema: "sales",
                        principalTable: "sales_return_lines",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_return_line_serials_tenant_id_serial_number_id",
                        columns: x => new { x.tenant_id, x.serial_number_id },
                        principalSchema: "inventory",
                        principalTable: "serial_numbers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "serial_events",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_number_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_serial_events", x => x.id);
                    table.UniqueConstraint("ak_serial_events_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_serial_events_accion", "action IN ('Received', 'Sold', 'Returned', 'TransferDispatched', 'TransferReceived', 'RmaReceived', 'SentToSupplier', 'Repaired', 'Replaced', 'ReplacementIssued', 'ReturnedToSupplier', 'Scrapped', 'Adjusted', 'Restocked', 'ReturnedToCustomer')");
                    table.ForeignKey(
                        name: "fk_serial_events_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_serial_events_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "warehouse",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_serial_events_tenant_id_serial_number_id",
                        columns: x => new { x.tenant_id, x.serial_number_id },
                        principalSchema: "inventory",
                        principalTable: "serial_numbers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_serial_events_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "spec_definitions",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    data_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    is_multi_valued = table.Column<bool>(type: "boolean", nullable: false),
                    is_filterable = table.Column<bool>(type: "boolean", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    compatibility_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spec_definitions", x => x.id);
                    table.UniqueConstraint("ak_spec_definitions_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_spec_definitions_clave", "compatibility_key IS NULL OR compatibility_key ~ '^[a-z0-9_]+$'");
                    table.CheckConstraint("ck_spec_definitions_codigo", "code ~ '^[a-z0-9_]+$'");
                    table.CheckConstraint("ck_spec_definitions_multivalor", "NOT is_multi_valued OR data_type = 'Option'");
                    table.CheckConstraint("ck_spec_definitions_tipo", "data_type IN ('Text', 'Number', 'Option')");
                    table.ForeignKey(
                        name: "fk_spec_definitions_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_spec_definitions_tenant_id_category_id",
                        columns: x => new { x.tenant_id, x.category_id },
                        principalSchema: "catalog",
                        principalTable: "categories",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_line_serials",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_transfer_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_number_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer_line_serials", x => x.id);
                    table.UniqueConstraint("ak_stock_transfer_line_serials_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_stock_transfer_line_serials_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_line_serials_tenant_id_from_branch_i_661e4667",
                        columns: x => new { x.tenant_id, x.from_branch_id, x.to_branch_id, x.stock_transfer_line_id },
                        principalSchema: "inventory",
                        principalTable: "stock_transfer_lines",
                        principalColumns: new[] { "tenant_id", "from_branch_id", "to_branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_line_serials_tenant_id_serial_number_id",
                        columns: x => new { x.tenant_id, x.serial_number_id },
                        principalSchema: "inventory",
                        principalTable: "serial_numbers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "warranty_claims",
                schema: "service",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    serial_number_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_in_warranty = table.Column<bool>(type: "boolean", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resolution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    replacement_serial_id = table.Column<Guid>(type: "uuid", nullable: true),
                    opened_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warranty_claims", x => x.id);
                    table.UniqueConstraint("ak_warranty_claims_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_warranty_claims_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_warranty_claims_cierre", "(status = 'Delivered') = (closed_at IS NOT NULL)");
                    table.CheckConstraint("ck_warranty_claims_estado", "status IN ('Received', 'Diagnosing', 'SentToSupplier', 'Repaired', 'Replaced', 'Rejected', 'Delivered')");
                    table.CheckConstraint("ck_warranty_claims_otra_unidad", "replacement_serial_id IS NULL OR replacement_serial_id <> serial_number_id");
                    table.CheckConstraint("ck_warranty_claims_proveedor", "status <> 'SentToSupplier' OR supplier_id IS NOT NULL");
                    table.CheckConstraint("ck_warranty_claims_reemplazo", "status <> 'Replaced' OR replacement_serial_id IS NOT NULL");
                    table.CheckConstraint("ck_warranty_claims_resolucion", "status NOT IN ('Repaired', 'Replaced', 'Rejected', 'Delivered') OR resolution IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_warranty_claims_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_warranty_claims_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "warehouse",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_warranty_claims_tenant_id_branch_id_invoice_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.invoice_id },
                        principalSchema: "sales",
                        principalTable: "invoices",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_warranty_claims_tenant_id_customer_id",
                        columns: x => new { x.tenant_id, x.customer_id },
                        principalSchema: "sales",
                        principalTable: "customers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_warranty_claims_tenant_id_opened_by_user_id",
                        columns: x => new { x.tenant_id, x.opened_by_user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_warranty_claims_tenant_id_replacement_serial_id",
                        columns: x => new { x.tenant_id, x.replacement_serial_id },
                        principalSchema: "inventory",
                        principalTable: "serial_numbers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_warranty_claims_tenant_id_serial_number_id",
                        columns: x => new { x.tenant_id, x.serial_number_id },
                        principalSchema: "inventory",
                        principalTable: "serial_numbers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_warranty_claims_tenant_id_supplier_id",
                        columns: x => new { x.tenant_id, x.supplier_id },
                        principalSchema: "purchasing",
                        principalTable: "suppliers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pc_build_lines",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pc_build_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    quoted_unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pc_build_lines", x => x.id);
                    table.UniqueConstraint("ak_pc_build_lines_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_pc_build_lines_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_pc_build_lines_cantidad", "quantity BETWEEN 1 AND 16");
                    table.CheckConstraint("ck_pc_build_lines_precio", "quoted_unit_price >= 0");
                    table.CheckConstraint("ck_pc_build_lines_ranura", "slot IN ('Cpu', 'Motherboard', 'Ram', 'Gpu', 'Storage', 'Psu', 'Case', 'Cooler', 'Monitor', 'Peripheral', 'Software', 'Service')");
                    table.ForeignKey(
                        name: "fk_pc_build_lines_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pc_build_lines_tenant_id_branch_id_pc_build_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.pc_build_id },
                        principalSchema: "sales",
                        principalTable: "pc_builds",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_pc_build_lines_tenant_id_variant_id",
                        columns: x => new { x.tenant_id, x.variant_id },
                        principalSchema: "catalog",
                        principalTable: "product_variants",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "spec_options",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    spec_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spec_options", x => x.id);
                    table.UniqueConstraint("ak_spec_options_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.UniqueConstraint("ak_spec_options_tenant_id_spec_definition_id_id", x => new { x.tenant_id, x.spec_definition_id, x.id });
                    table.ForeignKey(
                        name: "fk_spec_options_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_spec_options_tenant_id_spec_definition_id",
                        columns: x => new { x.tenant_id, x.spec_definition_id },
                        principalSchema: "catalog",
                        principalTable: "spec_definitions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "warranty_claim_events",
                schema: "service",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warranty_claim_events", x => x.id);
                    table.UniqueConstraint("ak_warranty_claim_events_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_warranty_claim_events_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_warranty_claim_events_accion", "action IN ('Opened', 'StatusChanged', 'NoteAdded', 'ReplacementIssued', 'Closed')");
                    table.ForeignKey(
                        name: "fk_warranty_claim_events_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_warranty_claim_events_tenant_id_branch_id_claim_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.claim_id },
                        principalSchema: "service",
                        principalTable: "warranty_claims",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_warranty_claim_events_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_spec_values",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    spec_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    text_value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    option_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_spec_values", x => x.id);
                    table.UniqueConstraint("ak_product_spec_values_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_product_spec_values_arco", "num_nonnulls(number_value, text_value, option_id) = 1");
                    table.CheckConstraint("ck_product_spec_values_texto", "text_value IS NULL OR length(btrim(text_value)) > 0");
                    table.ForeignKey(
                        name: "fk_product_spec_values_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_spec_values_tenant_id_product_id",
                        columns: x => new { x.tenant_id, x.product_id },
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_product_spec_values_tenant_id_spec_definition_id",
                        columns: x => new { x.tenant_id, x.spec_definition_id },
                        principalSchema: "catalog",
                        principalTable: "spec_definitions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_spec_values_tenant_id_spec_definition_id_option_id",
                        columns: x => new { x.tenant_id, x.spec_definition_id, x.option_id },
                        principalSchema: "catalog",
                        principalTable: "spec_options",
                        principalColumns: new[] { "tenant_id", "spec_definition_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_tenant_id_batch_id_stock_level_id",
                schema: "inventory",
                table: "serial_numbers",
                columns: new[] { "tenant_id", "batch_id", "stock_level_id" });

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_tenant_id_serial",
                schema: "inventory",
                table: "serial_numbers",
                columns: new[] { "tenant_id", "serial" });

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_tenant_id_status",
                schema: "inventory",
                table: "serial_numbers",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_tenant_id_variant_id_batch_id",
                schema: "inventory",
                table: "serial_numbers",
                columns: new[] { "tenant_id", "variant_id", "batch_id" });

            migrationBuilder.CreateIndex(
                name: "ux_serial_numbers_tenant_id_variant_id_serial",
                schema: "inventory",
                table: "serial_numbers",
                columns: new[] { "tenant_id", "variant_id", "serial" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_serial_numbers_estado",
                schema: "inventory",
                table: "serial_numbers",
                sql: "status IN ('InStock', 'Reserved', 'Sold', 'Returned', 'Scrapped', 'InTransit', 'InRma', 'ReturnedToSupplier')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_serial_numbers_imei",
                schema: "inventory",
                table: "serial_numbers",
                sql: "kind <> 'Imei' OR serial ~ '^[0-9]{15}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_serial_numbers_serie",
                schema: "inventory",
                table: "serial_numbers",
                sql: "length(serial) > 0 AND serial !~ '[[:space:],;]'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_serial_numbers_tipo",
                schema: "inventory",
                table: "serial_numbers",
                sql: "kind IN ('Serial', 'Imei')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_serial_numbers_ubicacion",
                schema: "inventory",
                table: "serial_numbers",
                sql: "(status IN ('InStock', 'Reserved')) = (stock_level_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_pc_build_lines_tenant_id_branch_id_pc_build_id",
                schema: "sales",
                table: "pc_build_lines",
                columns: new[] { "tenant_id", "branch_id", "pc_build_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pc_build_lines_tenant_id_variant_id",
                schema: "sales",
                table: "pc_build_lines",
                columns: new[] { "tenant_id", "variant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pc_builds_tenant_id_branch_id_invoice_id",
                schema: "sales",
                table: "pc_builds",
                columns: new[] { "tenant_id", "branch_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pc_builds_tenant_id_created_by_user_id",
                schema: "sales",
                table: "pc_builds",
                columns: new[] { "tenant_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pc_builds_tenant_id_customer_id",
                schema: "sales",
                table: "pc_builds",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pc_builds_tenant_id_status",
                schema: "sales",
                table: "pc_builds",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_pc_builds_invoice_id",
                schema: "sales",
                table: "pc_builds",
                column: "invoice_id",
                unique: true,
                filter: "invoice_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_pc_builds_tenant_id_branch_id_number",
                schema: "sales",
                table: "pc_builds",
                columns: new[] { "tenant_id", "branch_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_spec_values_tenant_id_spec_definition_id_option_id",
                schema: "catalog",
                table: "product_spec_values",
                columns: new[] { "tenant_id", "spec_definition_id", "option_id" });

            migrationBuilder.CreateIndex(
                name: "ux_product_spec_values_tenant_id_product_id_spec_defin_2d2c8dd0",
                schema: "catalog",
                table: "product_spec_values",
                columns: new[] { "tenant_id", "product_id", "spec_definition_id", "option_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_line_serials_serial_number_id",
                schema: "sales",
                table: "sales_order_line_serials",
                column: "serial_number_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_line_serials_tenant_id_branch_id_sales__e7c7c1fa",
                schema: "sales",
                table: "sales_order_line_serials",
                columns: new[] { "tenant_id", "branch_id", "sales_order_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_line_serials_tenant_id_serial_number_id",
                schema: "sales",
                table: "sales_order_line_serials",
                columns: new[] { "tenant_id", "serial_number_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sales_order_line_serials_sales_order_line_id_serial_980878dd",
                schema: "sales",
                table: "sales_order_line_serials",
                columns: new[] { "sales_order_line_id", "serial_number_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_line_serials_serial_number_id",
                schema: "sales",
                table: "sales_return_line_serials",
                column: "serial_number_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_line_serials_tenant_id_branch_id_sales_0a8c1b00",
                schema: "sales",
                table: "sales_return_line_serials",
                columns: new[] { "tenant_id", "branch_id", "sales_return_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_line_serials_tenant_id_serial_number_id",
                schema: "sales",
                table: "sales_return_line_serials",
                columns: new[] { "tenant_id", "serial_number_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sales_return_line_serials_sales_return_line_id_seri_b31662bb",
                schema: "sales",
                table: "sales_return_line_serials",
                columns: new[] { "sales_return_line_id", "serial_number_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_serial_events_serial_number_id_occurred_at",
                schema: "inventory",
                table: "serial_events",
                columns: new[] { "serial_number_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_serial_events_tenant_id_branch_id",
                schema: "inventory",
                table: "serial_events",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_serial_events_tenant_id_document_number",
                schema: "inventory",
                table: "serial_events",
                columns: new[] { "tenant_id", "document_number" },
                filter: "document_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_serial_events_tenant_id_serial_number_id",
                schema: "inventory",
                table: "serial_events",
                columns: new[] { "tenant_id", "serial_number_id" });

            migrationBuilder.CreateIndex(
                name: "ix_serial_events_tenant_id_user_id",
                schema: "inventory",
                table: "serial_events",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_spec_definitions_tenant_id_compatibility_key",
                schema: "catalog",
                table: "spec_definitions",
                columns: new[] { "tenant_id", "compatibility_key" },
                filter: "compatibility_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_spec_definitions_tenant_id_category_id_code",
                schema: "catalog",
                table: "spec_definitions",
                columns: new[] { "tenant_id", "category_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_spec_options_tenant_id_spec_definition_id_value",
                schema: "catalog",
                table: "spec_options",
                columns: new[] { "tenant_id", "spec_definition_id", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_line_serials_serial_number_id",
                schema: "inventory",
                table: "stock_transfer_line_serials",
                column: "serial_number_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_line_serials_tenant_id_from_branch_i_79344d9f",
                schema: "inventory",
                table: "stock_transfer_line_serials",
                columns: new[] { "tenant_id", "from_branch_id", "to_branch_id", "stock_transfer_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_line_serials_tenant_id_serial_number_id",
                schema: "inventory",
                table: "stock_transfer_line_serials",
                columns: new[] { "tenant_id", "serial_number_id" });

            migrationBuilder.CreateIndex(
                name: "ux_stock_transfer_line_serials_stock_transfer_line_id__499197be",
                schema: "inventory",
                table: "stock_transfer_line_serials",
                columns: new[] { "stock_transfer_line_id", "serial_number_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_warranty_claim_events_claim_id_occurred_at",
                schema: "service",
                table: "warranty_claim_events",
                columns: new[] { "claim_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_warranty_claim_events_tenant_id_branch_id_claim_id",
                schema: "service",
                table: "warranty_claim_events",
                columns: new[] { "tenant_id", "branch_id", "claim_id" });

            migrationBuilder.CreateIndex(
                name: "ix_warranty_claim_events_tenant_id_user_id",
                schema: "service",
                table: "warranty_claim_events",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_warranty_claims_tenant_id_branch_id_invoice_id",
                schema: "service",
                table: "warranty_claims",
                columns: new[] { "tenant_id", "branch_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_warranty_claims_tenant_id_customer_id",
                schema: "service",
                table: "warranty_claims",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_warranty_claims_tenant_id_opened_by_user_id",
                schema: "service",
                table: "warranty_claims",
                columns: new[] { "tenant_id", "opened_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_warranty_claims_tenant_id_replacement_serial_id",
                schema: "service",
                table: "warranty_claims",
                columns: new[] { "tenant_id", "replacement_serial_id" });

            migrationBuilder.CreateIndex(
                name: "ix_warranty_claims_tenant_id_status",
                schema: "service",
                table: "warranty_claims",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_warranty_claims_tenant_id_supplier_id",
                schema: "service",
                table: "warranty_claims",
                columns: new[] { "tenant_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ux_warranty_claims_tenant_id_branch_id_number",
                schema: "service",
                table: "warranty_claims",
                columns: new[] { "tenant_id", "branch_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_warranty_claims_tenant_id_serial_number_id",
                schema: "service",
                table: "warranty_claims",
                columns: new[] { "tenant_id", "serial_number_id" },
                unique: true,
                filter: "status <> 'Delivered'");

            migrationBuilder.AddForeignKey(
                name: "fk_serial_numbers_tenant_id_batch_id_stock_level_id",
                schema: "inventory",
                table: "serial_numbers",
                columns: new[] { "tenant_id", "batch_id", "stock_level_id" },
                principalSchema: "inventory",
                principalTable: "stock_levels",
                principalColumns: new[] { "tenant_id", "batch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_serial_numbers_tenant_id_variant_id",
                schema: "inventory",
                table: "serial_numbers",
                columns: new[] { "tenant_id", "variant_id" },
                principalSchema: "catalog",
                principalTable: "product_variants",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_serial_numbers_tenant_id_variant_id_batch_id",
                schema: "inventory",
                table: "serial_numbers",
                columns: new[] { "tenant_id", "variant_id", "batch_id" },
                principalSchema: "inventory",
                principalTable: "batches",
                principalColumns: new[] { "tenant_id", "variant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            V42Guards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            V42DropGuards(migrationBuilder);

            migrationBuilder.DropForeignKey(
                name: "fk_serial_numbers_tenant_id_batch_id_stock_level_id",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.DropForeignKey(
                name: "fk_serial_numbers_tenant_id_variant_id",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.DropForeignKey(
                name: "fk_serial_numbers_tenant_id_variant_id_batch_id",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.DropTable(
                name: "pc_build_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "product_spec_values",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_tech_profiles",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "sales_order_line_serials",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "sales_return_line_serials",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "serial_events",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_transfer_line_serials",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "warranty_claim_events",
                schema: "service");

            migrationBuilder.DropTable(
                name: "pc_builds",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "spec_options",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "warranty_claims",
                schema: "service");

            migrationBuilder.DropTable(
                name: "spec_definitions",
                schema: "catalog");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_stock_levels_tenant_id_batch_id_id",
                schema: "inventory",
                table: "stock_levels");

            migrationBuilder.DropIndex(
                name: "ix_serial_numbers_tenant_id_batch_id_stock_level_id",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.DropIndex(
                name: "ix_serial_numbers_tenant_id_serial",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.DropIndex(
                name: "ix_serial_numbers_tenant_id_status",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.DropIndex(
                name: "ix_serial_numbers_tenant_id_variant_id_batch_id",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.DropIndex(
                name: "ux_serial_numbers_tenant_id_variant_id_serial",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_serial_numbers_estado",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_serial_numbers_imei",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_serial_numbers_serie",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_serial_numbers_tipo",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_serial_numbers_ubicacion",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_batches_tenant_id_variant_id_id",
                schema: "inventory",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.DropColumn(
                name: "received_at",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.DropColumn(
                name: "variant_id",
                schema: "inventory",
                table: "serial_numbers");

            migrationBuilder.CreateIndex(
                name: "ix_stock_levels_tenant_id_batch_id",
                schema: "inventory",
                table: "stock_levels",
                columns: new[] { "tenant_id", "batch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_tenant_id_batch_id",
                schema: "inventory",
                table: "serial_numbers",
                columns: new[] { "tenant_id", "batch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_tenant_id_stock_level_id",
                schema: "inventory",
                table: "serial_numbers",
                columns: new[] { "tenant_id", "stock_level_id" });

            migrationBuilder.CreateIndex(
                name: "ux_serial_numbers_batch_id_serial",
                schema: "inventory",
                table: "serial_numbers",
                columns: new[] { "batch_id", "serial" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_batches_tenant_id_variant_id",
                schema: "inventory",
                table: "batches",
                columns: new[] { "tenant_id", "variant_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_serial_numbers_tenant_id_batch_id",
                schema: "inventory",
                table: "serial_numbers",
                columns: new[] { "tenant_id", "batch_id" },
                principalSchema: "inventory",
                principalTable: "batches",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_serial_numbers_tenant_id_stock_level_id",
                schema: "inventory",
                table: "serial_numbers",
                columns: new[] { "tenant_id", "stock_level_id" },
                principalSchema: "inventory",
                principalTable: "stock_levels",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            V42DropSchema(migrationBuilder);
        }
    }
}
