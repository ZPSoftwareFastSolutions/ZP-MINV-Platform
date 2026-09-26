using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MINV.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// V4.1 · Facturación SIAT (Computarizada en Línea): esquema <c>billing</c> (configuración, puntos de venta, CUIS,
    /// CUFD, catálogos sincronizados, homologación, documentos fiscales con sus líneas, XML y bitácora, entregas,
    /// contingencia y bitácora SOAP), devoluciones de venta (<c>sales.sales_returns</c>), datos fiscales de las facturas de
    /// proveedor, datos de facturación del cliente y el módulo comercial FISCAL_SIAT. El SQL propio de PostgreSQL está en
    /// <c>V41SiatBilling.Sql.cs</c>.
    /// </summary>
    public partial class V41SiatBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "billing");

            migrationBuilder.AddColumn<string>(
                name: "complement",
                schema: "sales",
                table: "customers",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "document_type",
                schema: "sales",
                table: "customers",
                type: "smallint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "contingency_codes",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_sector = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    number_from = table.Column<long>(type: "bigint", nullable: false),
                    number_to = table.Column<long>(type: "bigint", nullable: false),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contingency_codes", x => x.id);
                    table.UniqueConstraint("ak_contingency_codes_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_contingency_codes_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_contingency_codes_rango", "number_from > 0 AND number_to >= number_from");
                    table.CheckConstraint("ck_contingency_codes_sector", "document_sector BETWEEN 1 AND 99");
                    table.ForeignKey(
                        name: "fk_contingency_codes_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contingency_codes_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "warehouse",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_nit_checks",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nit = table.Column<long>(type: "bigint", nullable: false),
                    siat_code = table.Column<int>(type: "integer", nullable: false),
                    is_valid = table.Column<bool>(type: "boolean", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_nit_checks", x => x.id);
                    table.UniqueConstraint("ak_customer_nit_checks_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_customer_nit_checks_nit", "nit > 0");
                    table.ForeignKey(
                        name: "fk_customer_nit_checks_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customer_nit_checks_tenant_id_customer_id",
                        columns: x => new { x.tenant_id, x.customer_id },
                        principalSchema: "sales",
                        principalTable: "customers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customer_nit_checks_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mail_settings",
                schema: "billing",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false),
                    use_ssl = table.Column<bool>(type: "boolean", nullable: false),
                    user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    password_ciphertext = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    password_key_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    from_address = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    from_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mail_settings", x => x.tenant_id);
                    table.CheckConstraint("ck_mail_settings_clave", "(password_ciphertext IS NULL) = (password_key_id IS NULL)");
                    table.CheckConstraint("ck_mail_settings_puerto", "port BETWEEN 1 AND 65535");
                    table.ForeignKey(
                        name: "fk_mail_settings_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_method_siat_codes",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_method_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sin_payment_method_code = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_method_siat_codes", x => x.id);
                    table.UniqueConstraint("ak_payment_method_siat_codes_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_payment_method_siat_codes_codigo", "sin_payment_method_code BETWEEN 1 AND 999");
                    table.ForeignKey(
                        name: "fk_payment_method_siat_codes_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_method_siat_codes_tenant_id_payment_method_id",
                        columns: x => new { x.tenant_id, x.payment_method_id },
                        principalSchema: "sales",
                        principalTable: "payment_methods",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_returns",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    refund_payment_method_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pos_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    returned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_returns", x => x.id);
                    table.UniqueConstraint("ak_sales_returns_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_sales_returns_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_sales_returns_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_returns_tenant_id_branch_id_invoice_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.invoice_id },
                        principalSchema: "sales",
                        principalTable: "invoices",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_returns_tenant_id_branch_id_pos_session_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.pos_session_id },
                        principalSchema: "sales",
                        principalTable: "pos_sessions",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_returns_tenant_id_customer_id",
                        columns: x => new { x.tenant_id, x.customer_id },
                        principalSchema: "sales",
                        principalTable: "customers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_returns_tenant_id_refund_payment_method_id",
                        columns: x => new { x.tenant_id, x.refund_payment_method_id },
                        principalSchema: "sales",
                        principalTable: "payment_methods",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_returns_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "siat_activities",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    activity_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_siat_activities", x => x.id);
                    table.UniqueConstraint("ak_siat_activities_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_siat_activities_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "siat_activity_sectors",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    document_sector = table.Column<int>(type: "integer", nullable: false),
                    sector_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_siat_activity_sectors", x => x.id);
                    table.UniqueConstraint("ak_siat_activity_sectors_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_siat_activity_sectors_sector", "document_sector BETWEEN 1 AND 99");
                    table.ForeignKey(
                        name: "fk_siat_activity_sectors_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "siat_branches",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    siat_code = table.Column<int>(type: "integer", nullable: false),
                    municipality = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    phone = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_siat_branches", x => x.id);
                    table.UniqueConstraint("ak_siat_branches_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_siat_branches_codigo", "siat_code BETWEEN 0 AND 9999");
                    table.ForeignKey(
                        name: "fk_siat_branches_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_siat_branches_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "warehouse",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "siat_catalog_items",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    code = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_siat_catalog_items", x => x.id);
                    table.UniqueConstraint("ak_siat_catalog_items_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_siat_catalog_items_codigo", "code >= 0");
                    table.ForeignKey(
                        name: "fk_siat_catalog_items_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "siat_environment_profiles",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment = table.Column<int>(type: "integer", nullable: false),
                    codes_url = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    sync_url = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    operations_url = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    purchase_sale_url = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    computerized_url = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    adjustment_url = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    @namespace = table.Column<string>(name: "namespace", type: "character varying(200)", maxLength: 200, nullable: false),
                    qr_base_url = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    token_ciphertext = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    token_key_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    token_valid_until = table.Column<DateOnly>(type: "date", nullable: true),
                    token_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_siat_environment_profiles", x => x.id);
                    table.UniqueConstraint("ak_siat_environment_profiles_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_siat_environment_profiles_ambiente", "environment IN (1, 2)");
                    table.CheckConstraint("ck_siat_environment_profiles_espera", "timeout_seconds BETWEEN 3 AND 120");
                    table.CheckConstraint("ck_siat_environment_profiles_token", "(token_ciphertext IS NULL) = (token_key_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_siat_environment_profiles_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "siat_legends",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_siat_legends", x => x.id);
                    table.UniqueConstraint("ak_siat_legends_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_siat_legends_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "siat_points_of_sale",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<int>(type: "integer", nullable: false),
                    type_code = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    pos_register_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mode_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_contact_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    retry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_siat_points_of_sale", x => x.id);
                    table.UniqueConstraint("ak_siat_points_of_sale_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_siat_points_of_sale_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_siat_points_of_sale_ambiente", "environment IN (1, 2)");
                    table.CheckConstraint("ck_siat_points_of_sale_cierre", "closed_at IS NULL OR code > 0");
                    table.CheckConstraint("ck_siat_points_of_sale_codigo", "code BETWEEN 0 AND 9999");
                    table.CheckConstraint("ck_siat_points_of_sale_fallos", "consecutive_failures >= 0");
                    table.CheckConstraint("ck_siat_points_of_sale_modo", "mode IN ('Online', 'Offline', 'ManualContingency', 'Recovering')");
                    table.CheckConstraint("ck_siat_points_of_sale_tipo", "type_code BETWEEN 0 AND 99");
                    table.ForeignKey(
                        name: "fk_siat_points_of_sale_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_siat_points_of_sale_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "warehouse",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_siat_points_of_sale_tenant_id_branch_id_pos_register_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.pos_register_id },
                        principalSchema: "sales",
                        principalTable: "pos_registers",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "siat_products",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    product_code = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_siat_products", x => x.id);
                    table.UniqueConstraint("ak_siat_products_tenant_id_activity_code_product_code", x => new { x.tenant_id, x.activity_code, x.product_code });
                    table.UniqueConstraint("ak_siat_products_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_siat_products_codigo", "product_code BETWEEN 1 AND 99999999");
                    table.ForeignKey(
                        name: "fk_siat_products_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "siat_service_calls",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment = table.Column<int>(type: "integer", nullable: false),
                    resource = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    operation = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    point_of_sale_code = table.Column<int>(type: "integer", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    http_status = table.Column<int>(type: "integer", nullable: true),
                    siat_code = table.Column<int>(type: "integer", nullable: true),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    request_body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    response_body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_siat_service_calls", x => x.id);
                    table.UniqueConstraint("ak_siat_service_calls_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_siat_service_calls_duracion", "duration_ms >= 0");
                    table.ForeignKey(
                        name: "fk_siat_service_calls_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_siat_service_calls_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "warehouse",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "siat_settings",
                schema: "billing",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nit = table.Column<long>(type: "bigint", nullable: false),
                    business_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    system_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    environment = table.Column<int>(type: "integer", nullable: false),
                    modality = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    online_legend = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    offline_legend = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    clock_offset_ms = table.Column<long>(type: "bigint", nullable: false),
                    clock_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_siat_settings", x => x.tenant_id);
                    table.CheckConstraint("ck_siat_settings_ambiente", "environment IN (1, 2)");
                    table.CheckConstraint("ck_siat_settings_modalidad", "modality = 2");
                    table.CheckConstraint("ck_siat_settings_nit", "nit BETWEEN 1 AND 9999999999999");
                    table.ForeignKey(
                        name: "fk_siat_settings_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "siat_sync_runs",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment = table.Column<int>(type: "integer", nullable: false),
                    catalog = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    items = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_siat_sync_runs", x => x.id);
                    table.UniqueConstraint("ak_siat_sync_runs_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_siat_sync_runs_filas", "items >= 0");
                    table.ForeignKey(
                        name: "fk_siat_sync_runs_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_siat_sync_runs_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_invoice_fiscal",
                schema: "purchasing",
                columns: table => new
                {
                    supplier_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    authorization_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    control_code = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discounts = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    not_subject_to_vat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    purchase_type = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_invoice_fiscal", x => x.supplier_invoice_id);
                    table.CheckConstraint("ck_supplier_invoice_fiscal_deducciones", "discounts >= 0 AND not_subject_to_vat >= 0 AND total_amount - not_subject_to_vat - discounts >= 0");
                    table.CheckConstraint("ck_supplier_invoice_fiscal_importe", "total_amount > 0");
                    table.CheckConstraint("ck_supplier_invoice_fiscal_tipo", "purchase_type BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_supplier_invoice_fiscal_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_invoice_fiscal_tenant_id_branch_id_supplie_d42241a5",
                        columns: x => new { x.tenant_id, x.branch_id, x.supplier_invoice_id },
                        principalSchema: "purchasing",
                        principalTable: "supplier_invoices",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "unit_siat_codes",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sin_unit_code = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unit_siat_codes", x => x.id);
                    table.UniqueConstraint("ak_unit_siat_codes_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_unit_siat_codes_codigo", "sin_unit_code BETWEEN 1 AND 999");
                    table.ForeignKey(
                        name: "fk_unit_siat_codes_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_unit_siat_codes_tenant_id_unit_id",
                        columns: x => new { x.tenant_id, x.unit_id },
                        principalSchema: "catalog",
                        principalTable: "units_of_measure",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_return_lines",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    stock_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_return_lines", x => x.id);
                    table.UniqueConstraint("ak_sales_return_lines_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_sales_return_lines_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_sales_return_lines_cantidad", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_sales_return_lines_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_return_lines_tenant_id_branch_id_sales_order_line_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.sales_order_line_id },
                        principalSchema: "sales",
                        principalTable: "sales_order_lines",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_return_lines_tenant_id_branch_id_sales_return_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.sales_return_id },
                        principalSchema: "sales",
                        principalTable: "sales_returns",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sales_return_lines_tenant_id_branch_id_stock_movement_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.stock_movement_id },
                        principalSchema: "inventory",
                        principalTable: "stock_movements",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_return_lines_tenant_id_variant_id",
                        columns: x => new { x.tenant_id, x.variant_id },
                        principalSchema: "catalog",
                        principalTable: "product_variants",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "siat_cuis",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    point_of_sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    obtained_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_siat_cuis", x => x.id);
                    table.UniqueConstraint("ak_siat_cuis_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_siat_cuis_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_siat_cuis_vigencia", "valid_until > obtained_at");
                    table.ForeignKey(
                        name: "fk_siat_cuis_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_siat_cuis_tenant_id_branch_id_point_of_sale_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.point_of_sale_id },
                        principalSchema: "billing",
                        principalTable: "siat_points_of_sale",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_siat_codes",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    sin_product_code = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_siat_codes", x => x.id);
                    table.UniqueConstraint("ak_product_siat_codes_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_product_siat_codes_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_siat_codes_tenant_id_activity_code_sin_product_code",
                        columns: x => new { x.tenant_id, x.activity_code, x.sin_product_code },
                        principalSchema: "billing",
                        principalTable: "siat_products",
                        principalColumns: new[] { "tenant_id", "activity_code", "product_code" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_siat_codes_tenant_id_product_id",
                        columns: x => new { x.tenant_id, x.product_id },
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "siat_cufds",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    point_of_sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuis_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    control_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    obtained_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_siat_cufds", x => x.id);
                    table.UniqueConstraint("ak_siat_cufds_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_siat_cufds_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_siat_cufds_vigencia", "valid_until > obtained_at");
                    table.ForeignKey(
                        name: "fk_siat_cufds_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_siat_cufds_tenant_id_branch_id_cuis_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.cuis_id },
                        principalSchema: "billing",
                        principalTable: "siat_cuis",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_siat_cufds_tenant_id_branch_id_point_of_sale_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.point_of_sale_id },
                        principalSchema: "billing",
                        principalTable: "siat_points_of_sale",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "significant_events",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    point_of_sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    event_code = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    event_cufd_id = table.Column<Guid>(type: "uuid", nullable: false),
                    send_cufd_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contingency_code_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reception_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_significant_events", x => x.id);
                    table.UniqueConstraint("ak_significant_events_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_significant_events_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_significant_events_ambiente", "environment IN (1, 2)");
                    table.CheckConstraint("ck_significant_events_cafc", "kind = 'ManualCafc' OR contingency_code_id IS NULL");
                    table.CheckConstraint("ck_significant_events_clase", "kind IN ('Offline', 'ManualCafc')");
                    table.CheckConstraint("ck_significant_events_codigo", "event_code BETWEEN 1 AND 99");
                    table.CheckConstraint("ck_significant_events_estado", "status IN ('Open', 'Closed', 'Registered', 'PackagesSent', 'Reconciled', 'WithObservations')");
                    table.CheckConstraint("ck_significant_events_fin", "(status = 'Open') = (ended_at IS NULL)");
                    table.CheckConstraint("ck_significant_events_rango", "ended_at IS NULL OR ended_at > started_at");
                    table.CheckConstraint("ck_significant_events_registro", "(status IN ('Open', 'Closed')) = (registered_at IS NULL) AND (registered_at IS NULL) = (reception_code IS NULL) AND (registered_at IS NULL) = (send_cufd_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_significant_events_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_significant_events_tenant_id_branch_id_contingency_code_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.contingency_code_id },
                        principalSchema: "billing",
                        principalTable: "contingency_codes",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_significant_events_tenant_id_branch_id_event_cufd_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.event_cufd_id },
                        principalSchema: "billing",
                        principalTable: "siat_cufds",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_significant_events_tenant_id_branch_id_point_of_sale_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.point_of_sale_id },
                        principalSchema: "billing",
                        principalTable: "siat_points_of_sale",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_significant_events_tenant_id_branch_id_send_cufd_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.send_cufd_id },
                        principalSchema: "billing",
                        principalTable: "siat_cufds",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_significant_events_tenant_id_created_by_user_id",
                        columns: x => new { x.tenant_id, x.created_by_user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_packages",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    significant_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    point_of_sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    send_cufd_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_sector = table.Column<int>(type: "integer", nullable: false),
                    document_type = table.Column<int>(type: "integer", nullable: false),
                    cafc = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reception_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    validated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_siat_code = table.Column<int>(type: "integer", nullable: true),
                    messages = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_packages", x => x.id);
                    table.UniqueConstraint("ak_fiscal_packages_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_fiscal_packages_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_fiscal_packages_estado", "status IN ('Sent', 'Validated', 'Observed', 'Rejected')");
                    table.CheckConstraint("ck_fiscal_packages_huella", "sha256 ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_fiscal_packages_sector", "document_sector BETWEEN 1 AND 99");
                    table.CheckConstraint("ck_fiscal_packages_validacion", "(status = 'Sent') = (validated_at IS NULL)");
                    table.ForeignKey(
                        name: "fk_fiscal_packages_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_packages_tenant_id_branch_id_point_of_sale_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.point_of_sale_id },
                        principalSchema: "billing",
                        principalTable: "siat_points_of_sale",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_packages_tenant_id_branch_id_send_cufd_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.send_cufd_id },
                        principalSchema: "billing",
                        principalTable: "siat_cufds",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_packages_tenant_id_branch_id_significant_event_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.significant_event_id },
                        principalSchema: "billing",
                        principalTable: "significant_events",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_documents",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    point_of_sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuis_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cufd_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_sector = table.Column<int>(type: "integer", nullable: false),
                    document_type = table.Column<int>(type: "integer", nullable: false),
                    emission_type = table.Column<int>(type: "integer", nullable: false),
                    number = table.Column<long>(type: "bigint", nullable: false),
                    cuf = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    issued_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sales_return_id = table.Column<Guid>(type: "uuid", nullable: true),
                    replaces_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    buyer_document_type = table.Column<int>(type: "integer", nullable: false),
                    buyer_document_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    buyer_complement = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    buyer_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    buyer_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    payment_method_code = table.Column<int>(type: "integer", nullable: true),
                    card_number_masked = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    currency_code = table.Column<int>(type: "integer", nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    additional_discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    gift_card_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    exception_code = table.Column<int>(type: "integer", nullable: false),
                    cafc = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    legend = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    user_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_reverted = table.Column<bool>(type: "boolean", nullable: false),
                    reception_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_siat_code = table.Column<int>(type: "integer", nullable: true),
                    significant_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    package_position = table.Column<int>(type: "integer", nullable: true),
                    void_reason_code = table.Column<int>(type: "integer", nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reverted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_documents", x => x.id);
                    table.UniqueConstraint("ak_fiscal_documents_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_fiscal_documents_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_fiscal_documents_ambiente", "environment IN (1, 2)");
                    table.CheckConstraint("ck_fiscal_documents_anulacion", "status <> 'Voided' OR voided_at IS NOT NULL");
                    table.CheckConstraint("ck_fiscal_documents_cafc", "cafc IS NULL OR emission_type = 2");
                    table.CheckConstraint("ck_fiscal_documents_clase", "(kind = 'Invoice' AND document_sector = 1 AND document_type = 1) OR (kind = 'CreditDebitNote' AND document_sector = 24 AND document_type = 3)");
                    table.CheckConstraint("ck_fiscal_documents_comprador", "buyer_document_type BETWEEN 1 AND 5 AND (buyer_complement IS NULL OR buyer_document_type = 1)");
                    table.CheckConstraint("ck_fiscal_documents_emision", "emission_type IN (1, 2)");
                    table.CheckConstraint("ck_fiscal_documents_estado", "status IN ('Pending', 'Valid', 'Rejected', 'NoResponse', 'Offline', 'InPackage', 'PackageRejected', 'DuplicateToVoid', 'Voided', 'Discarded')");
                    table.CheckConstraint("ck_fiscal_documents_excepcion", "exception_code IN (0, 1)");
                    table.CheckConstraint("ck_fiscal_documents_montos", "additional_discount >= 0 AND gift_card_amount >= 0 AND exchange_rate > 0");
                    table.CheckConstraint("ck_fiscal_documents_notas_en_linea", "kind = 'Invoice' OR emission_type = 1");
                    table.CheckConstraint("ck_fiscal_documents_numero", "number > 0 AND number <= 9999999999");
                    table.CheckConstraint("ck_fiscal_documents_origen", "(invoice_id IS NULL OR kind = 'Invoice') AND (sales_return_id IS NULL OR kind = 'CreditDebitNote')");
                    table.CheckConstraint("ck_fiscal_documents_pago", "(kind = 'Invoice') = (payment_method_code IS NOT NULL) AND (payment_method_code IS NULL OR payment_method_code BETWEEN 1 AND 999)");
                    table.CheckConstraint("ck_fiscal_documents_paquete", "(package_id IS NULL) = (package_position IS NULL) AND (package_position IS NULL OR package_position BETWEEN 1 AND 500) AND (status <> 'InPackage' OR package_id IS NOT NULL)");
                    table.CheckConstraint("ck_fiscal_documents_reversion", "is_reverted = (reverted_at IS NOT NULL)");
                    table.CheckConstraint("ck_fiscal_documents_sector", "document_sector IN (1, 24)");
                    table.CheckConstraint("ck_fiscal_documents_tipo", "document_type IN (1, 3)");
                    table.ForeignKey(
                        name: "fk_fiscal_documents_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_documents_tenant_id_branch_id_cufd_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.cufd_id },
                        principalSchema: "billing",
                        principalTable: "siat_cufds",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_documents_tenant_id_branch_id_cuis_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.cuis_id },
                        principalSchema: "billing",
                        principalTable: "siat_cuis",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_documents_tenant_id_branch_id_invoice_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.invoice_id },
                        principalSchema: "sales",
                        principalTable: "invoices",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_documents_tenant_id_branch_id_package_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.package_id },
                        principalSchema: "billing",
                        principalTable: "fiscal_packages",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_documents_tenant_id_branch_id_point_of_sale_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.point_of_sale_id },
                        principalSchema: "billing",
                        principalTable: "siat_points_of_sale",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_documents_tenant_id_branch_id_replaces_document_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.replaces_document_id },
                        principalSchema: "billing",
                        principalTable: "fiscal_documents",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_documents_tenant_id_branch_id_sales_return_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.sales_return_id },
                        principalSchema: "sales",
                        principalTable: "sales_returns",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_documents_tenant_id_branch_id_significant_event_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.significant_event_id },
                        principalSchema: "billing",
                        principalTable: "significant_events",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_documents_tenant_id_customer_id",
                        columns: x => new { x.tenant_id, x.customer_id },
                        principalSchema: "sales",
                        principalTable: "customers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_deliveries",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recipient = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_deliveries", x => x.id);
                    table.UniqueConstraint("ak_fiscal_deliveries_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_fiscal_deliveries_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_fiscal_deliveries_canal", "channel IN ('Email', 'Print', 'Pdf')");
                    table.ForeignKey(
                        name: "fk_fiscal_deliveries_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_deliveries_tenant_id_branch_id_document_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.document_id },
                        principalSchema: "billing",
                        principalTable: "fiscal_documents",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_deliveries_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_document_events",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    siat_code = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reception_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    messages = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_document_events", x => x.id);
                    table.UniqueConstraint("ak_fiscal_document_events_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_fiscal_document_events_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_fiscal_document_events_accion", "action IN ('Issued', 'Sent', 'Accepted', 'Rejected', 'NoResponse', 'Reissued', 'Packaged', 'PackageValidated', 'PackageRejected', 'StatusChecked', 'VoidRequested', 'Voided', 'VoidFailed', 'Reverted', 'RevertFailed', 'Discarded', 'Delivered', 'Printed')");
                    table.ForeignKey(
                        name: "fk_fiscal_document_events_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_document_events_tenant_id_branch_id_document_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.document_id },
                        principalSchema: "billing",
                        principalTable: "fiscal_documents",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_document_events_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_document_files",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xml = table.Column<string>(type: "text", nullable: false),
                    gzip_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_document_files", x => x.id);
                    table.UniqueConstraint("ak_fiscal_document_files_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_fiscal_document_files_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_fiscal_document_files_huella", "gzip_sha256 ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_fiscal_document_files_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_document_files_tenant_id_branch_id_document_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.document_id },
                        principalSchema: "billing",
                        principalTable: "fiscal_documents",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_document_lines",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    activity_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    sin_product_code = table.Column<int>(type: "integer", nullable: false),
                    product_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(20,10)", precision: 20, scale: 10, nullable: false),
                    sin_unit_code = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(20,10)", precision: 20, scale: 10, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(20,10)", precision: 20, scale: 10, nullable: true),
                    transaction_code = table.Column<int>(type: "integer", nullable: true),
                    serial_number = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: true),
                    imei = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_document_lines", x => x.id);
                    table.UniqueConstraint("ak_fiscal_document_lines_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_fiscal_document_lines_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_fiscal_document_lines_cantidad", "quantity > 0");
                    table.CheckConstraint("ck_fiscal_document_lines_descuento", "discount IS NULL OR discount >= 0");
                    table.CheckConstraint("ck_fiscal_document_lines_numero", "line_number BETWEEN 1 AND 500");
                    table.CheckConstraint("ck_fiscal_document_lines_precio", "unit_price > 0");
                    table.CheckConstraint("ck_fiscal_document_lines_producto_sin", "sin_product_code BETWEEN 1 AND 99999999");
                    table.CheckConstraint("ck_fiscal_document_lines_transaccion", "transaction_code IS NULL OR transaction_code IN (1, 2)");
                    table.CheckConstraint("ck_fiscal_document_lines_unidad_sin", "sin_unit_code BETWEEN 1 AND 999");
                    table.ForeignKey(
                        name: "fk_fiscal_document_lines_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_document_lines_tenant_id_branch_id_document_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.document_id },
                        principalSchema: "billing",
                        principalTable: "fiscal_documents",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_document_lines_tenant_id_variant_id",
                        columns: x => new { x.tenant_id, x.variant_id },
                        principalSchema: "catalog",
                        principalTable: "product_variants",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_note_references",
                schema: "billing",
                columns: table => new
                {
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_number = table.Column<long>(type: "bigint", nullable: false),
                    original_cuf = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    original_issued_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    discount_share = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_note_references", x => x.document_id);
                    table.CheckConstraint("ck_fiscal_note_references_descuento", "discount_share IS NULL OR discount_share >= 0");
                    table.CheckConstraint("ck_fiscal_note_references_numero", "original_number > 0 AND original_number <= 9999999999");
                    table.ForeignKey(
                        name: "fk_fiscal_note_references_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_note_references_tenant_id_branch_id_document_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.document_id },
                        principalSchema: "billing",
                        principalTable: "fiscal_documents",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fiscal_note_references_tenant_id_branch_id_original_14003d04",
                        columns: x => new { x.tenant_id, x.branch_id, x.original_document_id },
                        principalSchema: "billing",
                        principalTable: "fiscal_documents",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "iam",
                table: "modules",
                columns: new[] { "id", "code", "created_at", "created_by", "description", "monthly_fee_bs", "name", "setup_price_bs", "updated_at", "updated_by" },
                values: new object[] { new Guid("01920000-0000-7000-8000-00000000000a"), "FISCAL_SIAT", new DateTimeOffset(new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Facturas y notas crédito-débito del SIN con CUF, contingencia automática, anulación, libros de ventas y compras.", 450m, "Facturación SIAT (computarizada en línea)", 7000m, null, null });

            migrationBuilder.AddCheckConstraint(
                name: "ck_customers_complemento",
                schema: "sales",
                table: "customers",
                sql: "complement IS NULL OR document_type = 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customers_tipo_documento",
                schema: "sales",
                table: "customers",
                sql: "document_type IS NULL OR document_type BETWEEN 1 AND 5");

            migrationBuilder.CreateIndex(
                name: "ux_contingency_codes_tenant_id_branch_id_document_sector_code",
                schema: "billing",
                table: "contingency_codes",
                columns: new[] { "tenant_id", "branch_id", "document_sector", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_nit_checks_tenant_id_customer_id",
                schema: "billing",
                table: "customer_nit_checks",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_nit_checks_tenant_id_nit_checked_at",
                schema: "billing",
                table: "customer_nit_checks",
                columns: new[] { "tenant_id", "nit", "checked_at" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_nit_checks_tenant_id_user_id",
                schema: "billing",
                table: "customer_nit_checks",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_deliveries_document_id_occurred_at",
                schema: "billing",
                table: "fiscal_deliveries",
                columns: new[] { "document_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_deliveries_tenant_id_branch_id_document_id",
                schema: "billing",
                table: "fiscal_deliveries",
                columns: new[] { "tenant_id", "branch_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_deliveries_tenant_id_user_id",
                schema: "billing",
                table: "fiscal_deliveries",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_document_events_document_id_occurred_at",
                schema: "billing",
                table: "fiscal_document_events",
                columns: new[] { "document_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_document_events_tenant_id_branch_id_document_id",
                schema: "billing",
                table: "fiscal_document_events",
                columns: new[] { "tenant_id", "branch_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_document_events_tenant_id_user_id",
                schema: "billing",
                table: "fiscal_document_events",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_document_files_tenant_id_branch_id_document_id",
                schema: "billing",
                table: "fiscal_document_files",
                columns: new[] { "tenant_id", "branch_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ux_fiscal_document_files_document_id",
                schema: "billing",
                table: "fiscal_document_files",
                column: "document_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_document_lines_tenant_id_branch_id_document_id",
                schema: "billing",
                table: "fiscal_document_lines",
                columns: new[] { "tenant_id", "branch_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_document_lines_tenant_id_variant_id",
                schema: "billing",
                table: "fiscal_document_lines",
                columns: new[] { "tenant_id", "variant_id" });

            migrationBuilder.CreateIndex(
                name: "ux_fiscal_document_lines_document_id_line_number",
                schema: "billing",
                table: "fiscal_document_lines",
                columns: new[] { "document_id", "line_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_documents_tenant_id_branch_id_cufd_id",
                schema: "billing",
                table: "fiscal_documents",
                columns: new[] { "tenant_id", "branch_id", "cufd_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_documents_tenant_id_branch_id_cuis_id",
                schema: "billing",
                table: "fiscal_documents",
                columns: new[] { "tenant_id", "branch_id", "cuis_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_documents_tenant_id_branch_id_invoice_id",
                schema: "billing",
                table: "fiscal_documents",
                columns: new[] { "tenant_id", "branch_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_documents_tenant_id_branch_id_package_id",
                schema: "billing",
                table: "fiscal_documents",
                columns: new[] { "tenant_id", "branch_id", "package_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_documents_tenant_id_branch_id_point_of_sale_id",
                schema: "billing",
                table: "fiscal_documents",
                columns: new[] { "tenant_id", "branch_id", "point_of_sale_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_documents_tenant_id_branch_id_replaces_document_id",
                schema: "billing",
                table: "fiscal_documents",
                columns: new[] { "tenant_id", "branch_id", "replaces_document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_documents_tenant_id_branch_id_sales_return_id",
                schema: "billing",
                table: "fiscal_documents",
                columns: new[] { "tenant_id", "branch_id", "sales_return_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_documents_tenant_id_branch_id_significant_event_id",
                schema: "billing",
                table: "fiscal_documents",
                columns: new[] { "tenant_id", "branch_id", "significant_event_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_documents_tenant_id_customer_id",
                schema: "billing",
                table: "fiscal_documents",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_documents_tenant_id_issued_at",
                schema: "billing",
                table: "fiscal_documents",
                columns: new[] { "tenant_id", "issued_at" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_documents_tenant_id_status",
                schema: "billing",
                table: "fiscal_documents",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_fiscal_documents_tenant_id_cuf",
                schema: "billing",
                table: "fiscal_documents",
                columns: new[] { "tenant_id", "cuf" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_fiscal_documents_tenant_id_environment_point_of_sal_c1b923c7",
                schema: "billing",
                table: "fiscal_documents",
                columns: new[] { "tenant_id", "environment", "point_of_sale_id", "document_sector", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_fiscal_documents_tenant_id_invoice_id",
                schema: "billing",
                table: "fiscal_documents",
                columns: new[] { "tenant_id", "invoice_id" },
                unique: true,
                filter: "invoice_id IS NOT NULL AND status IN ('Pending', 'Valid', 'Offline', 'InPackage')");

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_note_references_tenant_id_branch_id_original_149ead42",
                schema: "billing",
                table: "fiscal_note_references",
                columns: new[] { "tenant_id", "branch_id", "original_document_id" });

            migrationBuilder.CreateIndex(
                name: "ux_fiscal_note_references_tenant_id_branch_id_document_id",
                schema: "billing",
                table: "fiscal_note_references",
                columns: new[] { "tenant_id", "branch_id", "document_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_packages_tenant_id_branch_id_point_of_sale_id",
                schema: "billing",
                table: "fiscal_packages",
                columns: new[] { "tenant_id", "branch_id", "point_of_sale_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_packages_tenant_id_branch_id_send_cufd_id",
                schema: "billing",
                table: "fiscal_packages",
                columns: new[] { "tenant_id", "branch_id", "send_cufd_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_packages_tenant_id_branch_id_significant_event_id",
                schema: "billing",
                table: "fiscal_packages",
                columns: new[] { "tenant_id", "branch_id", "significant_event_id" });

            migrationBuilder.CreateIndex(
                name: "ux_payment_method_siat_codes_tenant_id_payment_method_id",
                schema: "billing",
                table: "payment_method_siat_codes",
                columns: new[] { "tenant_id", "payment_method_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_siat_codes_tenant_id_activity_code_sin_product_code",
                schema: "billing",
                table: "product_siat_codes",
                columns: new[] { "tenant_id", "activity_code", "sin_product_code" });

            migrationBuilder.CreateIndex(
                name: "ux_product_siat_codes_tenant_id_product_id",
                schema: "billing",
                table: "product_siat_codes",
                columns: new[] { "tenant_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_lines_tenant_id_branch_id_sales_order_line_id",
                schema: "sales",
                table: "sales_return_lines",
                columns: new[] { "tenant_id", "branch_id", "sales_order_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_lines_tenant_id_branch_id_sales_return_id",
                schema: "sales",
                table: "sales_return_lines",
                columns: new[] { "tenant_id", "branch_id", "sales_return_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_lines_tenant_id_branch_id_stock_movement_id",
                schema: "sales",
                table: "sales_return_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_movement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_lines_tenant_id_variant_id",
                schema: "sales",
                table: "sales_return_lines",
                columns: new[] { "tenant_id", "variant_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sales_return_lines_sales_return_id_sales_order_line_id",
                schema: "sales",
                table: "sales_return_lines",
                columns: new[] { "sales_return_id", "sales_order_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sales_return_lines_stock_movement_id",
                schema: "sales",
                table: "sales_return_lines",
                column: "stock_movement_id",
                unique: true,
                filter: "stock_movement_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sales_returns_tenant_id_branch_id_invoice_id",
                schema: "sales",
                table: "sales_returns",
                columns: new[] { "tenant_id", "branch_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_returns_tenant_id_branch_id_pos_session_id",
                schema: "sales",
                table: "sales_returns",
                columns: new[] { "tenant_id", "branch_id", "pos_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_returns_tenant_id_customer_id",
                schema: "sales",
                table: "sales_returns",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_returns_tenant_id_refund_payment_method_id",
                schema: "sales",
                table: "sales_returns",
                columns: new[] { "tenant_id", "refund_payment_method_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_returns_tenant_id_returned_at",
                schema: "sales",
                table: "sales_returns",
                columns: new[] { "tenant_id", "returned_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_returns_tenant_id_user_id",
                schema: "sales",
                table: "sales_returns",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sales_returns_tenant_id_number",
                schema: "sales",
                table: "sales_returns",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_siat_activities_tenant_id_code",
                schema: "billing",
                table: "siat_activities",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_siat_activity_sectors_tenant_id_activity_code_docum_52e808dc",
                schema: "billing",
                table: "siat_activity_sectors",
                columns: new[] { "tenant_id", "activity_code", "document_sector" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_siat_branches_tenant_id_branch_id",
                schema: "billing",
                table: "siat_branches",
                columns: new[] { "tenant_id", "branch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_siat_branches_tenant_id_siat_code",
                schema: "billing",
                table: "siat_branches",
                columns: new[] { "tenant_id", "siat_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_siat_catalog_items_tenant_id_catalog_code",
                schema: "billing",
                table: "siat_catalog_items",
                columns: new[] { "tenant_id", "catalog", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_siat_cufds_point_of_sale_id_obtained_at",
                schema: "billing",
                table: "siat_cufds",
                columns: new[] { "point_of_sale_id", "obtained_at" });

            migrationBuilder.CreateIndex(
                name: "ix_siat_cufds_tenant_id_branch_id_cuis_id",
                schema: "billing",
                table: "siat_cufds",
                columns: new[] { "tenant_id", "branch_id", "cuis_id" });

            migrationBuilder.CreateIndex(
                name: "ix_siat_cufds_tenant_id_branch_id_point_of_sale_id",
                schema: "billing",
                table: "siat_cufds",
                columns: new[] { "tenant_id", "branch_id", "point_of_sale_id" });

            migrationBuilder.CreateIndex(
                name: "ix_siat_cuis_point_of_sale_id_valid_until",
                schema: "billing",
                table: "siat_cuis",
                columns: new[] { "point_of_sale_id", "valid_until" });

            migrationBuilder.CreateIndex(
                name: "ix_siat_cuis_tenant_id_branch_id_point_of_sale_id",
                schema: "billing",
                table: "siat_cuis",
                columns: new[] { "tenant_id", "branch_id", "point_of_sale_id" });

            migrationBuilder.CreateIndex(
                name: "ux_siat_environment_profiles_tenant_id_environment",
                schema: "billing",
                table: "siat_environment_profiles",
                columns: new[] { "tenant_id", "environment" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_siat_legends_tenant_id_activity_code_text",
                schema: "billing",
                table: "siat_legends",
                columns: new[] { "tenant_id", "activity_code", "text" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_siat_points_of_sale_tenant_id_branch_id_pos_register_id",
                schema: "billing",
                table: "siat_points_of_sale",
                columns: new[] { "tenant_id", "branch_id", "pos_register_id" });

            migrationBuilder.CreateIndex(
                name: "ux_siat_points_of_sale_tenant_id_environment_branch_id_code",
                schema: "billing",
                table: "siat_points_of_sale",
                columns: new[] { "tenant_id", "environment", "branch_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_siat_points_of_sale_tenant_id_environment_pos_register_id",
                schema: "billing",
                table: "siat_points_of_sale",
                columns: new[] { "tenant_id", "environment", "pos_register_id" },
                unique: true,
                filter: "pos_register_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_siat_service_calls_tenant_id_branch_id",
                schema: "billing",
                table: "siat_service_calls",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_siat_service_calls_tenant_id_occurred_at",
                schema: "billing",
                table: "siat_service_calls",
                columns: new[] { "tenant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_siat_sync_runs_tenant_id_catalog_occurred_at",
                schema: "billing",
                table: "siat_sync_runs",
                columns: new[] { "tenant_id", "catalog", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_siat_sync_runs_tenant_id_user_id",
                schema: "billing",
                table: "siat_sync_runs",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_significant_events_tenant_id_branch_id_contingency_code_id",
                schema: "billing",
                table: "significant_events",
                columns: new[] { "tenant_id", "branch_id", "contingency_code_id" });

            migrationBuilder.CreateIndex(
                name: "ix_significant_events_tenant_id_branch_id_event_cufd_id",
                schema: "billing",
                table: "significant_events",
                columns: new[] { "tenant_id", "branch_id", "event_cufd_id" });

            migrationBuilder.CreateIndex(
                name: "ix_significant_events_tenant_id_branch_id_point_of_sale_id",
                schema: "billing",
                table: "significant_events",
                columns: new[] { "tenant_id", "branch_id", "point_of_sale_id" });

            migrationBuilder.CreateIndex(
                name: "ix_significant_events_tenant_id_branch_id_send_cufd_id",
                schema: "billing",
                table: "significant_events",
                columns: new[] { "tenant_id", "branch_id", "send_cufd_id" });

            migrationBuilder.CreateIndex(
                name: "ix_significant_events_tenant_id_created_by_user_id",
                schema: "billing",
                table: "significant_events",
                columns: new[] { "tenant_id", "created_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_significant_events_tenant_id_point_of_sale_id_status",
                schema: "billing",
                table: "significant_events",
                columns: new[] { "tenant_id", "point_of_sale_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_supplier_invoice_fiscal_tenant_id_branch_id_supplie_88deba65",
                schema: "purchasing",
                table: "supplier_invoice_fiscal",
                columns: new[] { "tenant_id", "branch_id", "supplier_invoice_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_unit_siat_codes_tenant_id_unit_id",
                schema: "billing",
                table: "unit_siat_codes",
                columns: new[] { "tenant_id", "unit_id" },
                unique: true);

            V41Guards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            V41DropGuards(migrationBuilder);

            migrationBuilder.DropTable(
                name: "customer_nit_checks",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "fiscal_deliveries",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "fiscal_document_events",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "fiscal_document_files",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "fiscal_document_lines",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "fiscal_note_references",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "mail_settings",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "payment_method_siat_codes",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "product_siat_codes",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "sales_return_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "siat_activities",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "siat_activity_sectors",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "siat_branches",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "siat_catalog_items",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "siat_environment_profiles",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "siat_legends",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "siat_service_calls",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "siat_settings",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "siat_sync_runs",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "supplier_invoice_fiscal",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "unit_siat_codes",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "fiscal_documents",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "siat_products",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "fiscal_packages",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "sales_returns",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "significant_events",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "contingency_codes",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "siat_cufds",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "siat_cuis",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "siat_points_of_sale",
                schema: "billing");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customers_complemento",
                schema: "sales",
                table: "customers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customers_tipo_documento",
                schema: "sales",
                table: "customers");

            migrationBuilder.DeleteData(
                schema: "iam",
                table: "modules",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-00000000000a"));

            migrationBuilder.DropColumn(
                name: "complement",
                schema: "sales",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "document_type",
                schema: "sales",
                table: "customers");

            V41DropSchema(migrationBuilder);
        }
    }
}
