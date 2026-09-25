using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MINV.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "iam");

            migrationBuilder.EnsureSchema(
                name: "accounting");

            migrationBuilder.EnsureSchema(
                name: "sales");

            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.EnsureSchema(
                name: "warehouse");

            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.EnsureSchema(
                name: "purchasing");

            migrationBuilder.CreateTable(
                name: "modules",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    setup_price_bs = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    monthly_fee_bs = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_modules", x => x.id);
                    table.CheckConstraint("ck_modules_prices", "setup_price_bs >= 0 AND monthly_fee_bs >= 0");
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    tax_id = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    account_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    parent_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_postable = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.UniqueConstraint("ak_accounts_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_accounts_padre", "parent_account_id IS NULL OR parent_account_id <> id");
                    table.ForeignKey(
                        name: "fk_accounts_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_accounts_tenant_id_parent_account_id",
                        columns: x => new { x.tenant_id, x.parent_account_id },
                        principalSchema: "accounting",
                        principalTable: "accounts",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "adjustment_reasons",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adjustment_reasons", x => x.id);
                    table.UniqueConstraint("ak_adjustment_reasons_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_adjustment_reasons_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attributes",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attributes", x => x.id);
                    table.UniqueConstraint("ak_attributes_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_attributes_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "barcode_types",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    length = table.Column<int>(type: "integer", nullable: true),
                    has_check_digit = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_barcode_types", x => x.id);
                    table.UniqueConstraint("ak_barcode_types_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_barcode_types_longitud", "length IS NULL OR (length >= 1 AND length <= 64)");
                    table.ForeignKey(
                        name: "fk_barcode_types_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "brands",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brands", x => x.id);
                    table.UniqueConstraint("ak_brands_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_brands_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.UniqueConstraint("ak_categories_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_categories_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "countries",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    iso_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_countries", x => x.id);
                    table.UniqueConstraint("ak_countries_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_countries_iso", "iso_code ~ '^[A-Z]{2}$'");
                    table.ForeignKey(
                        name: "fk_countries_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "currencies",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    symbol = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    decimal_places = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currencies", x => x.id);
                    table.UniqueConstraint("ak_currencies_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_currencies_decimales", "decimal_places BETWEEN 0 AND 4");
                    table.CheckConstraint("ck_currencies_iso", "code ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "fk_currencies_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_periods",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<short>(type: "smallint", nullable: false),
                    month = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_periods", x => x.id);
                    table.UniqueConstraint("ak_fiscal_periods_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_fiscal_periods_anio", "year BETWEEN 2000 AND 2100");
                    table.CheckConstraint("ck_fiscal_periods_mes", "month BETWEEN 1 AND 12");
                    table.ForeignKey(
                        name: "fk_fiscal_periods_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "location_types",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    allows_picking = table.Column<bool>(type: "boolean", nullable: false),
                    allows_receiving = table.Column<bool>(type: "boolean", nullable: false),
                    allows_sales = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_location_types", x => x.id);
                    table.UniqueConstraint("ak_location_types_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_location_types_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "movement_types",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    stock_factor = table.Column<short>(type: "smallint", nullable: false),
                    domain = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requires_notes = table.Column<bool>(type: "boolean", nullable: false),
                    is_initial_balance = table.Column<bool>(type: "boolean", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movement_types", x => x.id);
                    table.UniqueConstraint("ak_movement_types_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_movement_types_factor", "stock_factor IN (-1, 1)");
                    table.ForeignKey(
                        name: "fk_movement_types_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_methods",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    requires_reference = table.Column<bool>(type: "boolean", nullable: false),
                    opens_cash_drawer = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_methods", x => x.id);
                    table.UniqueConstraint("ak_payment_methods_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_payment_methods_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.id);
                    table.UniqueConstraint("ak_permissions_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_permissions_codigo_minusculas", "code = lower(code)");
                    table.ForeignKey(
                        name: "fk_permissions_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                    table.UniqueConstraint("ak_roles_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_roles_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_statuses",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    suggested_action = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    requires_action = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_statuses", x => x.id);
                    table.UniqueConstraint("ak_stock_statuses_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_stock_statuses_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    tax_id = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    lead_time_days = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppliers", x => x.id);
                    table.UniqueConstraint("ak_suppliers_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_suppliers_dias", "lead_time_days >= 0");
                    table.ForeignKey(
                        name: "fk_suppliers_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "taxes",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_taxes", x => x.id);
                    table.UniqueConstraint("ak_taxes_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_taxes_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_modules",
                schema: "iam",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_modules", x => new { x.tenant_id, x.module_id });
                    table.CheckConstraint("ck_tenant_modules_vigencia", "expires_at IS NULL OR expires_at > activated_at");
                    table.ForeignKey(
                        name: "fk_tenant_modules_module_id",
                        column: x => x.module_id,
                        principalSchema: "iam",
                        principalTable: "modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_modules_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "units_of_measure",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    allows_decimals = table.Column<bool>(type: "boolean", nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_units_of_measure", x => x.id);
                    table.UniqueConstraint("ak_units_of_measure_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_units_of_measure_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.UniqueConstraint("ak_users_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_users_email_minusculas", "email = lower(email)");
                    table.ForeignKey(
                        name: "fk_users_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attribute_values",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attribute_values", x => x.id);
                    table.UniqueConstraint("ak_attribute_values_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_attribute_values_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attribute_values_tenant_id_attribute_id",
                        columns: x => new { x.tenant_id, x.attribute_id },
                        principalSchema: "catalog",
                        principalTable: "attributes",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "models",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_models", x => x.id);
                    table.UniqueConstraint("ak_models_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_models_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_models_tenant_id_brand_id",
                        columns: x => new { x.tenant_id, x.brand_id },
                        principalSchema: "catalog",
                        principalTable: "brands",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "category_hierarchies",
                schema: "catalog",
                columns: table => new
                {
                    ancestor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    descendant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category_hierarchies", x => new { x.ancestor_id, x.descendant_id });
                    table.CheckConstraint("ck_category_hierarchies_profundidad", "depth >= 0");
                    table.CheckConstraint("ck_category_hierarchies_reflexiva", "(depth = 0) = (ancestor_id = descendant_id)");
                    table.ForeignKey(
                        name: "fk_category_hierarchies_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_category_hierarchies_tenant_id_ancestor_id",
                        columns: x => new { x.tenant_id, x.ancestor_id },
                        principalSchema: "catalog",
                        principalTable: "categories",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_category_hierarchies_tenant_id_descendant_id",
                        columns: x => new { x.tenant_id, x.descendant_id },
                        principalSchema: "catalog",
                        principalTable: "categories",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "states",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    country_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_states", x => x.id);
                    table.UniqueConstraint("ak_states_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_states_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_states_tenant_id_country_id",
                        columns: x => new { x.tenant_id, x.country_id },
                        principalSchema: "sales",
                        principalTable: "countries",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exchange_rates",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_rates", x => x.id);
                    table.UniqueConstraint("ak_exchange_rates_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_exchange_rates_distintas", "from_currency_id <> to_currency_id");
                    table.CheckConstraint("ck_exchange_rates_tasa", "rate > 0");
                    table.ForeignKey(
                        name: "fk_exchange_rates_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_exchange_rates_tenant_id_from_currency_id",
                        columns: x => new { x.tenant_id, x.from_currency_id },
                        principalSchema: "accounting",
                        principalTable: "currencies",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_exchange_rates_tenant_id_to_currency_id",
                        columns: x => new { x.tenant_id, x.to_currency_id },
                        principalSchema: "accounting",
                        principalTable: "currencies",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "price_lists",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_lists", x => x.id);
                    table.UniqueConstraint("ak_price_lists_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_price_lists_vigencia", "valid_to IS NULL OR valid_to >= valid_from");
                    table.ForeignKey(
                        name: "fk_price_lists_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_price_lists_tenant_id_currency_id",
                        columns: x => new { x.tenant_id, x.currency_id },
                        principalSchema: "accounting",
                        principalTable: "currencies",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                schema: "iam",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_id, x.permission_id });
                    table.ForeignKey(
                        name: "fk_role_permissions_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_role_permissions_tenant_id_permission_id",
                        columns: x => new { x.tenant_id, x.permission_id },
                        principalSchema: "iam",
                        principalTable: "permissions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_role_permissions_tenant_id_role_id",
                        columns: x => new { x.tenant_id, x.role_id },
                        principalSchema: "iam",
                        principalTable: "roles",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_returns",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_returns", x => x.id);
                    table.UniqueConstraint("ak_purchase_returns_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_purchase_returns_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_returns_tenant_id_supplier_id",
                        columns: x => new { x.tenant_id, x.supplier_id },
                        principalSchema: "purchasing",
                        principalTable: "suppliers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_contacts",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_contacts", x => x.id);
                    table.UniqueConstraint("ak_supplier_contacts_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_supplier_contacts_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_contacts_tenant_id_supplier_id",
                        columns: x => new { x.tenant_id, x.supplier_id },
                        principalSchema: "purchasing",
                        principalTable: "suppliers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_invoices",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_invoices", x => x.id);
                    table.UniqueConstraint("ak_supplier_invoices_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_supplier_invoices_vencimiento", "due_date IS NULL OR due_date >= invoice_date");
                    table.ForeignKey(
                        name: "fk_supplier_invoices_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_invoices_tenant_id_currency_id",
                        columns: x => new { x.tenant_id, x.currency_id },
                        principalSchema: "accounting",
                        principalTable: "currencies",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_invoices_tenant_id_supplier_id",
                        columns: x => new { x.tenant_id, x.supplier_id },
                        principalSchema: "purchasing",
                        principalTable: "suppliers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tax_rates",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_rates", x => x.id);
                    table.UniqueConstraint("ak_tax_rates_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tax_rates_tasa", "rate >= 0 AND rate <= 100");
                    table.CheckConstraint("ck_tax_rates_vigencia", "valid_to IS NULL OR valid_to >= valid_from");
                    table.ForeignKey(
                        name: "fk_tax_rates_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_rates_tenant_id_tax_id",
                        columns: x => new { x.tenant_id, x.tax_id },
                        principalSchema: "catalog",
                        principalTable: "taxes",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "unit_conversions",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factor = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unit_conversions", x => x.id);
                    table.UniqueConstraint("ak_unit_conversions_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_unit_conversions_distintas", "from_unit_id <> to_unit_id");
                    table.CheckConstraint("ck_unit_conversions_factor", "factor > 0");
                    table.ForeignKey(
                        name: "fk_unit_conversions_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_unit_conversions_tenant_id_from_unit_id",
                        columns: x => new { x.tenant_id, x.from_unit_id },
                        principalSchema: "catalog",
                        principalTable: "units_of_measure",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_unit_conversions_tenant_id_to_unit_id",
                        columns: x => new { x.tenant_id, x.to_unit_id },
                        principalSchema: "catalog",
                        principalTable: "units_of_measure",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    details = table.Column<string>(type: "jsonb", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legacy_reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                    table.UniqueConstraint("ak_audit_logs_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_audit_logs_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_audit_logs_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_entries",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    fiscal_period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_entries", x => x.id);
                    table.UniqueConstraint("ak_journal_entries_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_journal_entries_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_entries_tenant_id_currency_id",
                        columns: x => new { x.tenant_id, x.currency_id },
                        principalSchema: "accounting",
                        principalTable: "currencies",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_entries_tenant_id_fiscal_period_id",
                        columns: x => new { x.tenant_id, x.fiscal_period_id },
                        principalSchema: "accounting",
                        principalTable: "fiscal_periods",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_entries_tenant_id_posted_by_user_id",
                        columns: x => new { x.tenant_id, x.posted_by_user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_credentials",
                schema: "iam",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    algorithm = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    iterations = table.Column<int>(type: "integer", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_credentials", x => x.user_id);
                    table.CheckConstraint("ck_user_credentials_intentos", "failed_attempts >= 0");
                    table.CheckConstraint("ck_user_credentials_iteraciones", "iterations >= 100000");
                    table.ForeignKey(
                        name: "fk_user_credentials_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_credentials_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                schema: "iam",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_roles_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_roles_tenant_id_role_id",
                        columns: x => new { x.tenant_id, x.role_id },
                        principalSchema: "iam",
                        principalTable: "roles",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_roles_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    base_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tracking_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.UniqueConstraint("ak_products_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_products_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_products_tenant_id_base_unit_id",
                        columns: x => new { x.tenant_id, x.base_unit_id },
                        principalSchema: "catalog",
                        principalTable: "units_of_measure",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_products_tenant_id_category_id",
                        columns: x => new { x.tenant_id, x.category_id },
                        principalSchema: "catalog",
                        principalTable: "categories",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_products_tenant_id_model_id",
                        columns: x => new { x.tenant_id, x.model_id },
                        principalSchema: "catalog",
                        principalTable: "models",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cities",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    state_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cities", x => x.id);
                    table.UniqueConstraint("ak_cities_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_cities_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cities_tenant_id_state_id",
                        columns: x => new { x.tenant_id, x.state_id },
                        principalSchema: "sales",
                        principalTable: "states",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_categories",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    default_price_list_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_categories", x => x.id);
                    table.UniqueConstraint("ak_customer_categories_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_customer_categories_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customer_categories_tenant_id_default_price_list_id",
                        columns: x => new { x.tenant_id, x.default_price_list_id },
                        principalSchema: "sales",
                        principalTable: "price_lists",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_suppliers",
                schema: "catalog",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_sku = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    lead_time_days = table.Column<int>(type: "integer", nullable: true),
                    is_preferred = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_suppliers", x => new { x.product_id, x.supplier_id });
                    table.CheckConstraint("ck_product_suppliers_dias", "lead_time_days IS NULL OR lead_time_days >= 0");
                    table.ForeignKey(
                        name: "fk_product_suppliers_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_suppliers_tenant_id_product_id",
                        columns: x => new { x.tenant_id, x.product_id },
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_suppliers_tenant_id_supplier_id",
                        columns: x => new { x.tenant_id, x.supplier_id },
                        principalSchema: "purchasing",
                        principalTable: "suppliers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_taxes",
                schema: "catalog",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_taxes", x => new { x.product_id, x.tax_id });
                    table.ForeignKey(
                        name: "fk_product_taxes_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_taxes_tenant_id_product_id",
                        columns: x => new { x.tenant_id, x.product_id },
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_product_taxes_tenant_id_tax_id",
                        columns: x => new { x.tenant_id, x.tax_id },
                        principalSchema: "catalog",
                        principalTable: "taxes",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_unit_conversions",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factor_to_base = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_unit_conversions", x => x.id);
                    table.UniqueConstraint("ak_product_unit_conversions_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_product_unit_conversions_factor", "factor_to_base > 0");
                    table.ForeignKey(
                        name: "fk_product_unit_conversions_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_unit_conversions_tenant_id_product_id",
                        columns: x => new { x.tenant_id, x.product_id },
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_product_unit_conversions_tenant_id_unit_id",
                        columns: x => new { x.tenant_id, x.unit_id },
                        principalSchema: "catalog",
                        principalTable: "units_of_measure",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_variants",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_variants", x => x.id);
                    table.UniqueConstraint("ak_product_variants_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_product_variants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_variants_tenant_id_product_id",
                        columns: x => new { x.tenant_id, x.product_id },
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "postal_codes",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    city_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_postal_codes", x => x.id);
                    table.UniqueConstraint("ak_postal_codes_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_postal_codes_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_postal_codes_tenant_id_city_id",
                        columns: x => new { x.tenant_id, x.city_id },
                        principalSchema: "sales",
                        principalTable: "cities",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    tax_id = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    customer_category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                    table.UniqueConstraint("ak_customers_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_customers_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customers_tenant_id_customer_category_id",
                        columns: x => new { x.tenant_id, x.customer_category_id },
                        principalSchema: "sales",
                        principalTable: "customer_categories",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "batches",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    manufactured_on = table.Column<DateOnly>(type: "date", nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batches", x => x.id);
                    table.UniqueConstraint("ak_batches_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_batches_caducidad", "expires_on IS NULL OR manufactured_on IS NULL OR expires_on >= manufactured_on");
                    table.ForeignKey(
                        name: "fk_batches_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batches_tenant_id_variant_id",
                        columns: x => new { x.tenant_id, x.variant_id },
                        principalSchema: "catalog",
                        principalTable: "product_variants",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "price_list_items",
                schema: "sales",
                columns: table => new
                {
                    price_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_list_items", x => new { x.price_list_id, x.variant_id });
                    table.CheckConstraint("ck_price_list_items_precio", "unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_price_list_items_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_price_list_items_tenant_id_price_list_id",
                        columns: x => new { x.tenant_id, x.price_list_id },
                        principalSchema: "sales",
                        principalTable: "price_lists",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_price_list_items_tenant_id_variant_id",
                        columns: x => new { x.tenant_id, x.variant_id },
                        principalSchema: "catalog",
                        principalTable: "product_variants",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_barcodes",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    barcode_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_barcodes", x => x.id);
                    table.UniqueConstraint("ak_product_barcodes_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_product_barcodes_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_barcodes_tenant_id_barcode_type_id",
                        columns: x => new { x.tenant_id, x.barcode_type_id },
                        principalSchema: "catalog",
                        principalTable: "barcode_types",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_barcodes_tenant_id_unit_id",
                        columns: x => new { x.tenant_id, x.unit_id },
                        principalSchema: "catalog",
                        principalTable: "units_of_measure",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_barcodes_tenant_id_variant_id",
                        columns: x => new { x.tenant_id, x.variant_id },
                        principalSchema: "catalog",
                        principalTable: "product_variants",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_variant_attributes",
                schema: "catalog",
                columns: table => new
                {
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_value_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_variant_attributes", x => new { x.variant_id, x.attribute_value_id });
                    table.ForeignKey(
                        name: "fk_product_variant_attributes_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_variant_attributes_tenant_id_attribute_value_id",
                        columns: x => new { x.tenant_id, x.attribute_value_id },
                        principalSchema: "catalog",
                        principalTable: "attribute_values",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_variant_attributes_tenant_id_variant_id",
                        columns: x => new { x.tenant_id, x.variant_id },
                        principalSchema: "catalog",
                        principalTable: "product_variants",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "addresses",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    postal_code_id = table.Column<Guid>(type: "uuid", nullable: false),
                    street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_addresses", x => x.id);
                    table.UniqueConstraint("ak_addresses_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_addresses_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_addresses_tenant_id_postal_code_id",
                        columns: x => new { x.tenant_id, x.postal_code_id },
                        principalSchema: "sales",
                        principalTable: "postal_codes",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "branches",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branches", x => x.id);
                    table.UniqueConstraint("ak_branches_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_branches_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_branches_tenant_id_address_id",
                        columns: x => new { x.tenant_id, x.address_id },
                        principalSchema: "sales",
                        principalTable: "addresses",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_addresses",
                schema: "sales",
                columns: table => new
                {
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_addresses", x => new { x.customer_id, x.address_id });
                    table.ForeignKey(
                        name: "fk_customer_addresses_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customer_addresses_tenant_id_address_id",
                        columns: x => new { x.tenant_id, x.address_id },
                        principalSchema: "sales",
                        principalTable: "addresses",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customer_addresses_tenant_id_customer_id",
                        columns: x => new { x.tenant_id, x.customer_id },
                        principalSchema: "sales",
                        principalTable: "customers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_addresses",
                schema: "purchasing",
                columns: table => new
                {
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_addresses", x => new { x.supplier_id, x.address_id });
                    table.ForeignKey(
                        name: "fk_supplier_addresses_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_addresses_tenant_id_address_id",
                        columns: x => new { x.tenant_id, x.address_id },
                        principalSchema: "sales",
                        principalTable: "addresses",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_addresses_tenant_id_supplier_id",
                        columns: x => new { x.tenant_id, x.supplier_id },
                        principalSchema: "purchasing",
                        principalTable: "suppliers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "branch_users",
                schema: "warehouse",
                columns: table => new
                {
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branch_users", x => new { x.branch_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_branch_users_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_branch_users_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "warehouse",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_branch_users_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cost_centers",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_centers", x => x.id);
                    table.UniqueConstraint("ak_cost_centers_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_cost_centers_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cost_centers_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "warehouse",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hardware_tokens",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hardware_tokens", x => x.id);
                    table.UniqueConstraint("ak_hardware_tokens_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_hardware_tokens_revocacion", "revoked_at IS NULL OR revoked_at >= registered_at");
                    table.ForeignKey(
                        name: "fk_hardware_tokens_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hardware_tokens_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "warehouse",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tax_rules",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_exempt = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_rules", x => x.id);
                    table.UniqueConstraint("ak_tax_rules_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_tax_rules_prioridad", "priority >= 0");
                    table.ForeignKey(
                        name: "fk_tax_rules_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_rules_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "warehouse",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_rules_tenant_id_customer_category_id",
                        columns: x => new { x.tenant_id, x.customer_category_id },
                        principalSchema: "sales",
                        principalTable: "customer_categories",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_rules_tenant_id_tax_id",
                        columns: x => new { x.tenant_id, x.tax_id },
                        principalSchema: "catalog",
                        principalTable: "taxes",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "warehouses",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warehouses", x => x.id);
                    table.UniqueConstraint("ak_warehouses_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_warehouses_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_warehouses_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "warehouse",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_lines",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_center_id = table.Column<Guid>(type: "uuid", nullable: true),
                    debit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    credit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    memo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_lines", x => x.id);
                    table.UniqueConstraint("ak_journal_lines_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_journal_lines_partida", "(debit > 0 AND credit = 0) OR (credit > 0 AND debit = 0)");
                    table.ForeignKey(
                        name: "fk_journal_lines_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_lines_tenant_id_account_id",
                        columns: x => new { x.tenant_id, x.account_id },
                        principalSchema: "accounting",
                        principalTable: "accounts",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_lines_tenant_id_cost_center_id",
                        columns: x => new { x.tenant_id, x.cost_center_id },
                        principalSchema: "accounting",
                        principalTable: "cost_centers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_lines_tenant_id_journal_entry_id",
                        columns: x => new { x.tenant_id, x.journal_entry_id },
                        principalSchema: "accounting",
                        principalTable: "journal_entries",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "access_logs",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attempted_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    machine_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    hardware_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_logs", x => x.id);
                    table.UniqueConstraint("ak_access_logs_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_access_logs_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_logs_tenant_id_hardware_token_id",
                        columns: x => new { x.tenant_id, x.hardware_token_id },
                        principalSchema: "iam",
                        principalTable: "hardware_tokens",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_logs_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hardware_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    machine_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    client_version = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                    table.UniqueConstraint("ak_sessions_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_sessions_fechas", "ended_at IS NULL OR ended_at >= started_at");
                    table.ForeignKey(
                        name: "fk_sessions_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sessions_tenant_id_hardware_token_id",
                        columns: x => new { x.tenant_id, x.hardware_token_id },
                        principalSchema: "iam",
                        principalTable: "hardware_tokens",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sessions_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "physical_counts",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    count_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_physical_counts", x => x.id);
                    table.UniqueConstraint("ak_physical_counts_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_physical_counts_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_physical_counts_tenant_id_posted_by_user_id",
                        columns: x => new { x.tenant_id, x.posted_by_user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_physical_counts_tenant_id_warehouse_id",
                        columns: x => new { x.tenant_id, x.warehouse_id },
                        principalSchema: "warehouse",
                        principalTable: "warehouses",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pos_registers",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    hardware_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pos_registers", x => x.id);
                    table.UniqueConstraint("ak_pos_registers_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_pos_registers_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pos_registers_tenant_id_hardware_token_id",
                        columns: x => new { x.tenant_id, x.hardware_token_id },
                        principalSchema: "iam",
                        principalTable: "hardware_tokens",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pos_registers_tenant_id_warehouse_id",
                        columns: x => new { x.tenant_id, x.warehouse_id },
                        principalSchema: "warehouse",
                        principalTable: "warehouses",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_stock_policies",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    min_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    max_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_stock_policies", x => x.id);
                    table.UniqueConstraint("ak_product_stock_policies_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_product_stock_policies_maximo", "max_quantity >= 0 AND (max_quantity = 0 OR max_quantity >= min_quantity)");
                    table.CheckConstraint("ck_product_stock_policies_minimo", "min_quantity >= 0");
                    table.ForeignKey(
                        name: "fk_product_stock_policies_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_stock_policies_tenant_id_variant_id",
                        columns: x => new { x.tenant_id, x.variant_id },
                        principalSchema: "catalog",
                        principalTable: "product_variants",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_stock_policies_tenant_id_warehouse_id",
                        columns: x => new { x.tenant_id, x.warehouse_id },
                        principalSchema: "warehouse",
                        principalTable: "warehouses",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expected_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_orders", x => x.id);
                    table.UniqueConstraint("ak_purchase_orders_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_purchase_orders_fechas", "expected_date IS NULL OR expected_date >= order_date");
                    table.ForeignKey(
                        name: "fk_purchase_orders_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_orders_tenant_id_currency_id",
                        columns: x => new { x.tenant_id, x.currency_id },
                        principalSchema: "accounting",
                        principalTable: "currencies",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_orders_tenant_id_supplier_id",
                        columns: x => new { x.tenant_id, x.supplier_id },
                        principalSchema: "purchasing",
                        principalTable: "suppliers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_orders_tenant_id_warehouse_id",
                        columns: x => new { x.tenant_id, x.warehouse_id },
                        principalSchema: "warehouse",
                        principalTable: "warehouses",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_adjustments",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adjustment_reason_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_adjustments", x => x.id);
                    table.UniqueConstraint("ak_stock_adjustments_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_stock_adjustments_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_adjustments_tenant_id_adjustment_reason_id",
                        columns: x => new { x.tenant_id, x.adjustment_reason_id },
                        principalSchema: "inventory",
                        principalTable: "adjustment_reasons",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_adjustments_tenant_id_posted_by_user_id",
                        columns: x => new { x.tenant_id, x.posted_by_user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_adjustments_tenant_id_warehouse_id",
                        columns: x => new { x.tenant_id, x.warehouse_id },
                        principalSchema: "warehouse",
                        principalTable: "warehouses",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfers",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    from_warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfers", x => x.id);
                    table.UniqueConstraint("ak_stock_transfers_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_stock_transfers_almacenes", "from_warehouse_id <> to_warehouse_id");
                    table.ForeignKey(
                        name: "fk_stock_transfers_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfers_tenant_id_from_warehouse_id",
                        columns: x => new { x.tenant_id, x.from_warehouse_id },
                        principalSchema: "warehouse",
                        principalTable: "warehouses",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfers_tenant_id_to_warehouse_id",
                        columns: x => new { x.tenant_id, x.to_warehouse_id },
                        principalSchema: "warehouse",
                        principalTable: "warehouses",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_configs",
                schema: "iam",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    default_currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    default_warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                    alert_margin = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    days_without_rotation = table.Column<int>(type: "integer", nullable: false),
                    min_business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_configs", x => x.tenant_id);
                    table.CheckConstraint("ck_tenant_configs_margen", "alert_margin >= 0 AND alert_margin <= 1");
                    table.CheckConstraint("ck_tenant_configs_rotacion", "days_without_rotation > 0");
                    table.ForeignKey(
                        name: "fk_tenant_configs_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_configs_tenant_id_default_currency_id",
                        columns: x => new { x.tenant_id, x.default_currency_id },
                        principalSchema: "accounting",
                        principalTable: "currencies",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_configs_tenant_id_default_warehouse_id",
                        columns: x => new { x.tenant_id, x.default_warehouse_id },
                        principalSchema: "warehouse",
                        principalTable: "warehouses",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "zones",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    location_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_zones", x => x.id);
                    table.UniqueConstraint("ak_zones_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_zones_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_zones_tenant_id_location_type_id",
                        columns: x => new { x.tenant_id, x.location_type_id },
                        principalSchema: "warehouse",
                        principalTable: "location_types",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_zones_tenant_id_warehouse_id",
                        columns: x => new { x.tenant_id, x.warehouse_id },
                        principalSchema: "warehouse",
                        principalTable: "warehouses",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pos_sessions",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pos_register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    opening_cash = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closing_cash_counted = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pos_sessions", x => x.id);
                    table.UniqueConstraint("ak_pos_sessions_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_pos_sessions_cierre", "(status = 'Closed') = (closed_at IS NOT NULL)");
                    table.CheckConstraint("ck_pos_sessions_fechas", "closed_at IS NULL OR closed_at >= opened_at");
                    table.CheckConstraint("ck_pos_sessions_fondo", "opening_cash >= 0");
                    table.ForeignKey(
                        name: "fk_pos_sessions_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pos_sessions_tenant_id_closed_by_user_id",
                        columns: x => new { x.tenant_id, x.closed_by_user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pos_sessions_tenant_id_opened_by_user_id",
                        columns: x => new { x.tenant_id, x.opened_by_user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pos_sessions_tenant_id_pos_register_id",
                        columns: x => new { x.tenant_id, x.pos_register_id },
                        principalSchema: "sales",
                        principalTable: "pos_registers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goods_receipts",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_document = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goods_receipts", x => x.id);
                    table.UniqueConstraint("ak_goods_receipts_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_goods_receipts_origen", "num_nonnulls(purchase_order_id, supplier_id) = 1");
                    table.ForeignKey(
                        name: "fk_goods_receipts_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goods_receipts_tenant_id_purchase_order_id",
                        columns: x => new { x.tenant_id, x.purchase_order_id },
                        principalSchema: "purchasing",
                        principalTable: "purchase_orders",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goods_receipts_tenant_id_received_by_user_id",
                        columns: x => new { x.tenant_id, x.received_by_user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goods_receipts_tenant_id_supplier_id",
                        columns: x => new { x.tenant_id, x.supplier_id },
                        principalSchema: "purchasing",
                        principalTable: "suppliers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goods_receipts_tenant_id_warehouse_id",
                        columns: x => new { x.tenant_id, x.warehouse_id },
                        principalSchema: "warehouse",
                        principalTable: "warehouses",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_lines", x => x.id);
                    table.UniqueConstraint("ak_purchase_order_lines_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_purchase_order_lines_cantidad", "quantity > 0");
                    table.CheckConstraint("ck_purchase_order_lines_costo", "unit_cost >= 0");
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_tenant_id_purchase_order_id",
                        columns: x => new { x.tenant_id, x.purchase_order_id },
                        principalSchema: "purchasing",
                        principalTable: "purchase_orders",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_tenant_id_unit_id",
                        columns: x => new { x.tenant_id, x.unit_id },
                        principalSchema: "catalog",
                        principalTable: "units_of_measure",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_tenant_id_variant_id",
                        columns: x => new { x.tenant_id, x.variant_id },
                        principalSchema: "catalog",
                        principalTable: "product_variants",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "aisles",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aisles", x => x.id);
                    table.UniqueConstraint("ak_aisles_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_aisles_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_aisles_tenant_id_zone_id",
                        columns: x => new { x.tenant_id, x.zone_id },
                        principalSchema: "warehouse",
                        principalTable: "zones",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_movements",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pos_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_movements", x => x.id);
                    table.UniqueConstraint("ak_cash_movements_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_cash_movements_monto", "amount > 0");
                    table.ForeignKey(
                        name: "fk_cash_movements_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cash_movements_tenant_id_pos_session_id",
                        columns: x => new { x.tenant_id, x.pos_session_id },
                        principalSchema: "sales",
                        principalTable: "pos_sessions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cash_movements_tenant_id_recorded_by_user_id",
                        columns: x => new { x.tenant_id, x.recorded_by_user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_orders",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pos_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                    price_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_orders", x => x.id);
                    table.UniqueConstraint("ak_sales_orders_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_sales_orders_origen", "num_nonnulls(pos_session_id, warehouse_id) = 1");
                    table.ForeignKey(
                        name: "fk_sales_orders_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_orders_tenant_id_customer_id",
                        columns: x => new { x.tenant_id, x.customer_id },
                        principalSchema: "sales",
                        principalTable: "customers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_orders_tenant_id_pos_session_id",
                        columns: x => new { x.tenant_id, x.pos_session_id },
                        principalSchema: "sales",
                        principalTable: "pos_sessions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_orders_tenant_id_price_list_id",
                        columns: x => new { x.tenant_id, x.price_list_id },
                        principalSchema: "sales",
                        principalTable: "price_lists",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_orders_tenant_id_warehouse_id",
                        columns: x => new { x.tenant_id, x.warehouse_id },
                        principalSchema: "warehouse",
                        principalTable: "warehouses",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "racks",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    aisle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_racks", x => x.id);
                    table.UniqueConstraint("ak_racks_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_racks_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_racks_tenant_id_aisle_id",
                        columns: x => new { x.tenant_id, x.aisle_id },
                        principalSchema: "warehouse",
                        principalTable: "aisles",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sales_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fiscal_authorization_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.UniqueConstraint("ak_invoices_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_invoices_anulacion", "(status = 'Voided') = (voided_at IS NOT NULL)");
                    table.CheckConstraint("ck_invoices_emision", "(status = 'Draft') = (issued_at IS NULL)");
                    table.ForeignKey(
                        name: "fk_invoices_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_tenant_id_sales_order_id",
                        columns: x => new { x.tenant_id, x.sales_order_id },
                        principalSchema: "sales",
                        principalTable: "sales_orders",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shelves",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rack_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shelves", x => x.id);
                    table.UniqueConstraint("ak_shelves_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_shelves_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shelves_tenant_id_rack_id",
                        columns: x => new { x.tenant_id, x.rack_id },
                        principalSchema: "warehouse",
                        principalTable: "racks",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_method_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pos_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.UniqueConstraint("ak_payments_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_payments_monto", "amount > 0");
                    table.ForeignKey(
                        name: "fk_payments_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_tenant_id_invoice_id",
                        columns: x => new { x.tenant_id, x.invoice_id },
                        principalSchema: "sales",
                        principalTable: "invoices",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_tenant_id_payment_method_id",
                        columns: x => new { x.tenant_id, x.payment_method_id },
                        principalSchema: "sales",
                        principalTable: "payment_methods",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_tenant_id_pos_session_id",
                        columns: x => new { x.tenant_id, x.pos_session_id },
                        principalSchema: "sales",
                        principalTable: "pos_sessions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_tenant_id_recorded_by_user_id",
                        columns: x => new { x.tenant_id, x.recorded_by_user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bins",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shelf_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    location_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pick_sequence = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bins", x => x.id);
                    table.UniqueConstraint("ak_bins_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_bins_secuencia", "pick_sequence >= 0");
                    table.ForeignKey(
                        name: "fk_bins_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bins_tenant_id_location_type_id",
                        columns: x => new { x.tenant_id, x.location_type_id },
                        principalSchema: "warehouse",
                        principalTable: "location_types",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bins_tenant_id_shelf_id",
                        columns: x => new { x.tenant_id, x.shelf_id },
                        principalSchema: "warehouse",
                        principalTable: "shelves",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bin_assignments",
                schema: "warehouse",
                columns: table => new
                {
                    bin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_primary_pick = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bin_assignments", x => new { x.bin_id, x.variant_id });
                    table.ForeignKey(
                        name: "fk_bin_assignments_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bin_assignments_tenant_id_bin_id",
                        columns: x => new { x.tenant_id, x.bin_id },
                        principalSchema: "warehouse",
                        principalTable: "bins",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bin_assignments_tenant_id_variant_id",
                        columns: x => new { x.tenant_id, x.variant_id },
                        principalSchema: "catalog",
                        principalTable: "product_variants",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_levels",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_on_hand = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    quantity_reserved = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_levels", x => x.id);
                    table.UniqueConstraint("ak_stock_levels_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_stock_levels_existencia", "quantity_on_hand >= 0");
                    table.CheckConstraint("ck_stock_levels_reserva", "quantity_reserved >= 0 AND quantity_reserved <= quantity_on_hand");
                    table.ForeignKey(
                        name: "fk_stock_levels_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_levels_tenant_id_batch_id",
                        columns: x => new { x.tenant_id, x.batch_id },
                        principalSchema: "inventory",
                        principalTable: "batches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_levels_tenant_id_bin_id",
                        columns: x => new { x.tenant_id, x.bin_id },
                        principalSchema: "warehouse",
                        principalTable: "bins",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "serial_numbers",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    stock_level_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_serial_numbers", x => x.id);
                    table.UniqueConstraint("ak_serial_numbers_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_serial_numbers_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_serial_numbers_tenant_id_batch_id",
                        columns: x => new { x.tenant_id, x.batch_id },
                        principalSchema: "inventory",
                        principalTable: "batches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_serial_numbers_tenant_id_stock_level_id",
                        columns: x => new { x.tenant_id, x.stock_level_id },
                        principalSchema: "inventory",
                        principalTable: "stock_levels",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_level_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_reference = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    notes = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    adjustment_reason_id = table.Column<Guid>(type: "uuid", nullable: true),
                    legacy_reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movements", x => x.id);
                    table.UniqueConstraint("ak_stock_movements_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_stock_movements_cantidad", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_stock_movements_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_tenant_id_adjustment_reason_id",
                        columns: x => new { x.tenant_id, x.adjustment_reason_id },
                        principalSchema: "inventory",
                        principalTable: "adjustment_reasons",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_tenant_id_movement_type_id",
                        columns: x => new { x.tenant_id, x.movement_type_id },
                        principalSchema: "inventory",
                        principalTable: "movement_types",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_tenant_id_recorded_by_user_id",
                        columns: x => new { x.tenant_id, x.recorded_by_user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_tenant_id_stock_level_id",
                        columns: x => new { x.tenant_id, x.stock_level_id },
                        principalSchema: "inventory",
                        principalTable: "stock_levels",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "average_cost_history",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    average_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    stock_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_average_cost_history", x => x.id);
                    table.UniqueConstraint("ak_average_cost_history_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_average_cost_history_costo", "average_cost >= 0");
                    table.ForeignKey(
                        name: "fk_average_cost_history_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_average_cost_history_tenant_id_stock_movement_id",
                        columns: x => new { x.tenant_id, x.stock_movement_id },
                        principalSchema: "inventory",
                        principalTable: "stock_movements",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_average_cost_history_tenant_id_variant_id",
                        columns: x => new { x.tenant_id, x.variant_id },
                        principalSchema: "catalog",
                        principalTable: "product_variants",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_average_cost_history_tenant_id_warehouse_id",
                        columns: x => new { x.tenant_id, x.warehouse_id },
                        principalSchema: "warehouse",
                        principalTable: "warehouses",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goods_receipt_lines",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    goods_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_level_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    stock_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goods_receipt_lines", x => x.id);
                    table.UniqueConstraint("ak_goods_receipt_lines_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_goods_receipt_lines_cantidad", "quantity > 0");
                    table.CheckConstraint("ck_goods_receipt_lines_costo", "unit_cost >= 0");
                    table.ForeignKey(
                        name: "fk_goods_receipt_lines_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goods_receipt_lines_tenant_id_goods_receipt_id",
                        columns: x => new { x.tenant_id, x.goods_receipt_id },
                        principalSchema: "purchasing",
                        principalTable: "goods_receipts",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_goods_receipt_lines_tenant_id_purchase_order_line_id",
                        columns: x => new { x.tenant_id, x.purchase_order_line_id },
                        principalSchema: "purchasing",
                        principalTable: "purchase_order_lines",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goods_receipt_lines_tenant_id_stock_level_id",
                        columns: x => new { x.tenant_id, x.stock_level_id },
                        principalSchema: "inventory",
                        principalTable: "stock_levels",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goods_receipt_lines_tenant_id_stock_movement_id",
                        columns: x => new { x.tenant_id, x.stock_movement_id },
                        principalSchema: "inventory",
                        principalTable: "stock_movements",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "physical_count_lines",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    physical_count_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_level_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counted_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    counted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    system_quantity_at_posting = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    stock_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_physical_count_lines", x => x.id);
                    table.UniqueConstraint("ak_physical_count_lines_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_physical_count_lines_conteo", "counted_quantity >= 0");
                    table.ForeignKey(
                        name: "fk_physical_count_lines_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_physical_count_lines_tenant_id_counted_by_user_id",
                        columns: x => new { x.tenant_id, x.counted_by_user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_physical_count_lines_tenant_id_physical_count_id",
                        columns: x => new { x.tenant_id, x.physical_count_id },
                        principalSchema: "inventory",
                        principalTable: "physical_counts",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_physical_count_lines_tenant_id_stock_level_id",
                        columns: x => new { x.tenant_id, x.stock_level_id },
                        principalSchema: "inventory",
                        principalTable: "stock_levels",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_physical_count_lines_tenant_id_stock_movement_id",
                        columns: x => new { x.tenant_id, x.stock_movement_id },
                        principalSchema: "inventory",
                        principalTable: "stock_movements",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_order_lines",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    discount_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    stock_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_order_lines", x => x.id);
                    table.UniqueConstraint("ak_sales_order_lines_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_sales_order_lines_cantidad", "quantity > 0");
                    table.CheckConstraint("ck_sales_order_lines_descuento", "discount_percent >= 0 AND discount_percent <= 100");
                    table.CheckConstraint("ck_sales_order_lines_precio", "unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_sales_order_lines_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_order_lines_tenant_id_sales_order_id",
                        columns: x => new { x.tenant_id, x.sales_order_id },
                        principalSchema: "sales",
                        principalTable: "sales_orders",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sales_order_lines_tenant_id_stock_movement_id",
                        columns: x => new { x.tenant_id, x.stock_movement_id },
                        principalSchema: "inventory",
                        principalTable: "stock_movements",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_order_lines_tenant_id_unit_id",
                        columns: x => new { x.tenant_id, x.unit_id },
                        principalSchema: "catalog",
                        principalTable: "units_of_measure",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_order_lines_tenant_id_variant_id",
                        columns: x => new { x.tenant_id, x.variant_id },
                        principalSchema: "catalog",
                        principalTable: "product_variants",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_adjustment_lines",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_adjustment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_level_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_type_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_stock_adjustment_lines", x => x.id);
                    table.UniqueConstraint("ak_stock_adjustment_lines_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_stock_adjustment_lines_cantidad", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_stock_adjustment_lines_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_adjustment_lines_tenant_id_movement_type_id",
                        columns: x => new { x.tenant_id, x.movement_type_id },
                        principalSchema: "inventory",
                        principalTable: "movement_types",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_adjustment_lines_tenant_id_stock_adjustment_id",
                        columns: x => new { x.tenant_id, x.stock_adjustment_id },
                        principalSchema: "inventory",
                        principalTable: "stock_adjustments",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_stock_adjustment_lines_tenant_id_stock_level_id",
                        columns: x => new { x.tenant_id, x.stock_level_id },
                        principalSchema: "inventory",
                        principalTable: "stock_levels",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_adjustment_lines_tenant_id_stock_movement_id",
                        columns: x => new { x.tenant_id, x.stock_movement_id },
                        principalSchema: "inventory",
                        principalTable: "stock_movements",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_lines",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_stock_level_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_stock_level_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    outbound_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inbound_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer_lines", x => x.id);
                    table.UniqueConstraint("ak_stock_transfer_lines_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_stock_transfer_lines_cantidad", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_stock_transfer_lines_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_lines_tenant_id_destination_stock_level_id",
                        columns: x => new { x.tenant_id, x.destination_stock_level_id },
                        principalSchema: "inventory",
                        principalTable: "stock_levels",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_lines_tenant_id_inbound_movement_id",
                        columns: x => new { x.tenant_id, x.inbound_movement_id },
                        principalSchema: "inventory",
                        principalTable: "stock_movements",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_lines_tenant_id_outbound_movement_id",
                        columns: x => new { x.tenant_id, x.outbound_movement_id },
                        principalSchema: "inventory",
                        principalTable: "stock_movements",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_lines_tenant_id_source_stock_level_id",
                        columns: x => new { x.tenant_id, x.source_stock_level_id },
                        principalSchema: "inventory",
                        principalTable: "stock_levels",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_lines_tenant_id_stock_transfer_id",
                        columns: x => new { x.tenant_id, x.stock_transfer_id },
                        principalSchema: "inventory",
                        principalTable: "stock_transfers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_return_lines",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_level_id = table.Column<Guid>(type: "uuid", nullable: false),
                    goods_receipt_line_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_purchase_return_lines", x => x.id);
                    table.UniqueConstraint("ak_purchase_return_lines_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_purchase_return_lines_cantidad", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_purchase_return_lines_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_return_lines_tenant_id_goods_receipt_line_id",
                        columns: x => new { x.tenant_id, x.goods_receipt_line_id },
                        principalSchema: "purchasing",
                        principalTable: "goods_receipt_lines",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_return_lines_tenant_id_purchase_return_id",
                        columns: x => new { x.tenant_id, x.purchase_return_id },
                        principalSchema: "purchasing",
                        principalTable: "purchase_returns",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_purchase_return_lines_tenant_id_stock_level_id",
                        columns: x => new { x.tenant_id, x.stock_level_id },
                        principalSchema: "inventory",
                        principalTable: "stock_levels",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_return_lines_tenant_id_stock_movement_id",
                        columns: x => new { x.tenant_id, x.stock_movement_id },
                        principalSchema: "inventory",
                        principalTable: "stock_movements",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_invoice_lines",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    goods_receipt_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_rate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_invoice_lines", x => x.id);
                    table.UniqueConstraint("ak_supplier_invoice_lines_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_supplier_invoice_lines_cantidad", "quantity > 0");
                    table.CheckConstraint("ck_supplier_invoice_lines_costo", "unit_cost >= 0");
                    table.CheckConstraint("ck_supplier_invoice_lines_origen", "num_nonnulls(goods_receipt_line_id, description) = 1");
                    table.ForeignKey(
                        name: "fk_supplier_invoice_lines_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_invoice_lines_tenant_id_goods_receipt_line_id",
                        columns: x => new { x.tenant_id, x.goods_receipt_line_id },
                        principalSchema: "purchasing",
                        principalTable: "goods_receipt_lines",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_invoice_lines_tenant_id_supplier_invoice_id",
                        columns: x => new { x.tenant_id, x.supplier_invoice_id },
                        principalSchema: "purchasing",
                        principalTable: "supplier_invoices",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_supplier_invoice_lines_tenant_id_tax_rate_id",
                        columns: x => new { x.tenant_id, x.tax_rate_id },
                        principalSchema: "accounting",
                        principalTable: "tax_rates",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_rate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tax_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_lines", x => x.id);
                    table.UniqueConstraint("ak_invoice_lines_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_invoice_lines_impuesto", "tax_amount >= 0");
                    table.ForeignKey(
                        name: "fk_invoice_lines_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_lines_tenant_id_invoice_id",
                        columns: x => new { x.tenant_id, x.invoice_id },
                        principalSchema: "sales",
                        principalTable: "invoices",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_invoice_lines_tenant_id_sales_order_line_id",
                        columns: x => new { x.tenant_id, x.sales_order_line_id },
                        principalSchema: "sales",
                        principalTable: "sales_order_lines",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_lines_tenant_id_tax_rate_id",
                        columns: x => new { x.tenant_id, x.tax_rate_id },
                        principalSchema: "accounting",
                        principalTable: "tax_rates",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_reservations",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_level_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pos_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sales_order_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_reservations", x => x.id);
                    table.UniqueConstraint("ak_stock_reservations_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_stock_reservations_cantidad", "quantity > 0");
                    table.CheckConstraint("ck_stock_reservations_origen", "num_nonnulls(pos_session_id, sales_order_line_id) <= 1");
                    table.ForeignKey(
                        name: "fk_stock_reservations_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_reservations_tenant_id_pos_session_id",
                        columns: x => new { x.tenant_id, x.pos_session_id },
                        principalSchema: "sales",
                        principalTable: "pos_sessions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_reservations_tenant_id_sales_order_line_id",
                        columns: x => new { x.tenant_id, x.sales_order_line_id },
                        principalSchema: "sales",
                        principalTable: "sales_order_lines",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_reservations_tenant_id_stock_level_id",
                        columns: x => new { x.tenant_id, x.stock_level_id },
                        principalSchema: "inventory",
                        principalTable: "stock_levels",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "iam",
                table: "modules",
                columns: new[] { "id", "code", "created_at", "created_by", "description", "monthly_fee_bs", "name", "setup_price_bs", "updated_at", "updated_by" },
                values: new object[,]
                {
                    { new Guid("01920000-0000-7000-8000-000000000001"), "DATA_ENGINE", new DateTimeOffset(new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Transición a PostgreSQL local: elimina la corrupción de archivos.", 0m, "Migración de motor de datos", 8500m, null, null },
                    { new Guid("01920000-0000-7000-8000-000000000002"), "DESKTOP_CLIENT", new DateTimeOffset(new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Interfaz compilada: 100.000+ productos sin latencia.", 0m, "Cliente de escritorio nativo", 6000m, null, null },
                    { new Guid("01920000-0000-7000-8000-000000000003"), "POS_HARDWARE", new DateTimeOffset(new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Cajas, escáneres e impresoras ESC/POS (COM/USB/red).", 0m, "Módulo POS y hardware", 4500m, null, null },
                    { new Guid("01920000-0000-7000-8000-000000000004"), "RBAC", new DateTimeOffset(new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Login cifrado y trazabilidad inmutable por operador.", 0m, "Control de acceso (RBAC)", 3000m, null, null },
                    { new Guid("01920000-0000-7000-8000-000000000005"), "SLA_SUPPORT", new DateTimeOffset(new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Respaldo, telemetría y actualizaciones de seguridad.", 800m, "SLA de soporte", 0m, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_logs_tenant_id_hardware_token_id",
                schema: "iam",
                table: "access_logs",
                columns: new[] { "tenant_id", "hardware_token_id" });

            migrationBuilder.CreateIndex(
                name: "ix_access_logs_tenant_id_occurred_at",
                schema: "iam",
                table: "access_logs",
                columns: new[] { "tenant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_access_logs_tenant_id_user_id",
                schema: "iam",
                table: "access_logs",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_tenant_id_parent_account_id",
                schema: "accounting",
                table: "accounts",
                columns: new[] { "tenant_id", "parent_account_id" });

            migrationBuilder.CreateIndex(
                name: "ux_accounts_tenant_id_code",
                schema: "accounting",
                table: "accounts",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_addresses_tenant_id_postal_code_id",
                schema: "sales",
                table: "addresses",
                columns: new[] { "tenant_id", "postal_code_id" });

            migrationBuilder.CreateIndex(
                name: "ux_adjustment_reasons_tenant_id_code",
                schema: "inventory",
                table: "adjustment_reasons",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_aisles_tenant_id_zone_id",
                schema: "warehouse",
                table: "aisles",
                columns: new[] { "tenant_id", "zone_id" });

            migrationBuilder.CreateIndex(
                name: "ux_aisles_zone_id_code",
                schema: "warehouse",
                table: "aisles",
                columns: new[] { "zone_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attribute_values_tenant_id_attribute_id",
                schema: "catalog",
                table: "attribute_values",
                columns: new[] { "tenant_id", "attribute_id" });

            migrationBuilder.CreateIndex(
                name: "ux_attribute_values_attribute_id_value",
                schema: "catalog",
                table: "attribute_values",
                columns: new[] { "attribute_id", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_attributes_tenant_id_name",
                schema: "catalog",
                table: "attributes",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_tenant_id_occurred_at",
                schema: "iam",
                table: "audit_logs",
                columns: new[] { "tenant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_tenant_id_user_id",
                schema: "iam",
                table: "audit_logs",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_audit_logs_tenant_id_legacy_reference",
                schema: "iam",
                table: "audit_logs",
                columns: new[] { "tenant_id", "legacy_reference" },
                unique: true,
                filter: "legacy_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_average_cost_history_tenant_id_stock_movement_id",
                schema: "accounting",
                table: "average_cost_history",
                columns: new[] { "tenant_id", "stock_movement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_average_cost_history_tenant_id_variant_id",
                schema: "accounting",
                table: "average_cost_history",
                columns: new[] { "tenant_id", "variant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_average_cost_history_tenant_id_warehouse_id",
                schema: "accounting",
                table: "average_cost_history",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_average_cost_history_variant_id_warehouse_id_effective_at",
                schema: "accounting",
                table: "average_cost_history",
                columns: new[] { "variant_id", "warehouse_id", "effective_at" });

            migrationBuilder.CreateIndex(
                name: "ux_barcode_types_tenant_id_code",
                schema: "catalog",
                table: "barcode_types",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_batches_tenant_id_variant_id",
                schema: "inventory",
                table: "batches",
                columns: new[] { "tenant_id", "variant_id" });

            migrationBuilder.CreateIndex(
                name: "ux_batches_variant_id",
                schema: "inventory",
                table: "batches",
                column: "variant_id",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ux_batches_variant_id_lot_number",
                schema: "inventory",
                table: "batches",
                columns: new[] { "variant_id", "lot_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bin_assignments_tenant_id_bin_id",
                schema: "warehouse",
                table: "bin_assignments",
                columns: new[] { "tenant_id", "bin_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bin_assignments_tenant_id_variant_id",
                schema: "warehouse",
                table: "bin_assignments",
                columns: new[] { "tenant_id", "variant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bins_tenant_id_location_type_id",
                schema: "warehouse",
                table: "bins",
                columns: new[] { "tenant_id", "location_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bins_tenant_id_shelf_id",
                schema: "warehouse",
                table: "bins",
                columns: new[] { "tenant_id", "shelf_id" });

            migrationBuilder.CreateIndex(
                name: "ux_bins_tenant_id_code",
                schema: "warehouse",
                table: "bins",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branch_users_tenant_id_branch_id",
                schema: "warehouse",
                table: "branch_users",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_branch_users_tenant_id_user_id",
                schema: "warehouse",
                table: "branch_users",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_branches_tenant_id_address_id",
                schema: "warehouse",
                table: "branches",
                columns: new[] { "tenant_id", "address_id" });

            migrationBuilder.CreateIndex(
                name: "ux_branches_tenant_id_code",
                schema: "warehouse",
                table: "branches",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_brands_tenant_id_name",
                schema: "catalog",
                table: "brands",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cash_movements_tenant_id_pos_session_id",
                schema: "sales",
                table: "cash_movements",
                columns: new[] { "tenant_id", "pos_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_movements_tenant_id_recorded_by_user_id",
                schema: "sales",
                table: "cash_movements",
                columns: new[] { "tenant_id", "recorded_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_categories_tenant_id_code",
                schema: "catalog",
                table: "categories",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_category_hierarchies_descendant_id",
                schema: "catalog",
                table: "category_hierarchies",
                column: "descendant_id");

            migrationBuilder.CreateIndex(
                name: "ix_category_hierarchies_tenant_id_ancestor_id",
                schema: "catalog",
                table: "category_hierarchies",
                columns: new[] { "tenant_id", "ancestor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_category_hierarchies_tenant_id_descendant_id",
                schema: "catalog",
                table: "category_hierarchies",
                columns: new[] { "tenant_id", "descendant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cities_tenant_id_state_id",
                schema: "sales",
                table: "cities",
                columns: new[] { "tenant_id", "state_id" });

            migrationBuilder.CreateIndex(
                name: "ux_cities_state_id_name",
                schema: "sales",
                table: "cities",
                columns: new[] { "state_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cost_centers_tenant_id_branch_id",
                schema: "accounting",
                table: "cost_centers",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ux_cost_centers_tenant_id_code",
                schema: "accounting",
                table: "cost_centers",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_countries_tenant_id_iso_code",
                schema: "sales",
                table: "countries",
                columns: new[] { "tenant_id", "iso_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_currencies_tenant_id_code",
                schema: "accounting",
                table: "currencies",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_addresses_tenant_id_address_id",
                schema: "sales",
                table: "customer_addresses",
                columns: new[] { "tenant_id", "address_id" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_addresses_tenant_id_customer_id",
                schema: "sales",
                table: "customer_addresses",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ux_customer_addresses_customer_id_address_type",
                schema: "sales",
                table: "customer_addresses",
                columns: new[] { "customer_id", "address_type" },
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ix_customer_categories_tenant_id_default_price_list_id",
                schema: "sales",
                table: "customer_categories",
                columns: new[] { "tenant_id", "default_price_list_id" });

            migrationBuilder.CreateIndex(
                name: "ux_customer_categories_tenant_id_code",
                schema: "sales",
                table: "customer_categories",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_tenant_id_customer_category_id",
                schema: "sales",
                table: "customers",
                columns: new[] { "tenant_id", "customer_category_id" });

            migrationBuilder.CreateIndex(
                name: "ux_customers_tenant_id_code",
                schema: "sales",
                table: "customers",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_tenant_id_from_currency_id",
                schema: "accounting",
                table: "exchange_rates",
                columns: new[] { "tenant_id", "from_currency_id" });

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_tenant_id_to_currency_id",
                schema: "accounting",
                table: "exchange_rates",
                columns: new[] { "tenant_id", "to_currency_id" });

            migrationBuilder.CreateIndex(
                name: "ux_exchange_rates_from_currency_id_to_currency_id_effe_b925ed63",
                schema: "accounting",
                table: "exchange_rates",
                columns: new[] { "from_currency_id", "to_currency_id", "effective_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_fiscal_periods_tenant_id_year_month",
                schema: "accounting",
                table: "fiscal_periods",
                columns: new[] { "tenant_id", "year", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_tenant_id_goods_receipt_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                columns: new[] { "tenant_id", "goods_receipt_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_tenant_id_purchase_order_line_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                columns: new[] { "tenant_id", "purchase_order_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_tenant_id_stock_level_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                columns: new[] { "tenant_id", "stock_level_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_tenant_id_stock_movement_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                columns: new[] { "tenant_id", "stock_movement_id" });

            migrationBuilder.CreateIndex(
                name: "ux_goods_receipt_lines_stock_movement_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                column: "stock_movement_id",
                unique: true,
                filter: "stock_movement_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_tenant_id_purchase_order_id",
                schema: "purchasing",
                table: "goods_receipts",
                columns: new[] { "tenant_id", "purchase_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_tenant_id_received_by_user_id",
                schema: "purchasing",
                table: "goods_receipts",
                columns: new[] { "tenant_id", "received_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_tenant_id_supplier_id",
                schema: "purchasing",
                table: "goods_receipts",
                columns: new[] { "tenant_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_tenant_id_warehouse_id",
                schema: "purchasing",
                table: "goods_receipts",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ux_goods_receipts_tenant_id_number",
                schema: "purchasing",
                table: "goods_receipts",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hardware_tokens_tenant_id_branch_id",
                schema: "iam",
                table: "hardware_tokens",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ux_hardware_tokens_tenant_id_fingerprint",
                schema: "iam",
                table: "hardware_tokens",
                columns: new[] { "tenant_id", "fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_tenant_id_invoice_id",
                schema: "sales",
                table: "invoice_lines",
                columns: new[] { "tenant_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_tenant_id_sales_order_line_id",
                schema: "sales",
                table: "invoice_lines",
                columns: new[] { "tenant_id", "sales_order_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_tenant_id_tax_rate_id",
                schema: "sales",
                table: "invoice_lines",
                columns: new[] { "tenant_id", "tax_rate_id" });

            migrationBuilder.CreateIndex(
                name: "ux_invoice_lines_sales_order_line_id",
                schema: "sales",
                table: "invoice_lines",
                column: "sales_order_line_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_id_sales_order_id",
                schema: "sales",
                table: "invoices",
                columns: new[] { "tenant_id", "sales_order_id" });

            migrationBuilder.CreateIndex(
                name: "ux_invoices_sales_order_id",
                schema: "sales",
                table: "invoices",
                column: "sales_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_invoices_tenant_id_number",
                schema: "sales",
                table: "invoices",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_tenant_id_currency_id",
                schema: "accounting",
                table: "journal_entries",
                columns: new[] { "tenant_id", "currency_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_tenant_id_fiscal_period_id",
                schema: "accounting",
                table: "journal_entries",
                columns: new[] { "tenant_id", "fiscal_period_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_tenant_id_posted_by_user_id",
                schema: "accounting",
                table: "journal_entries",
                columns: new[] { "tenant_id", "posted_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_journal_entries_tenant_id_number",
                schema: "accounting",
                table: "journal_entries",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_tenant_id_account_id",
                schema: "accounting",
                table: "journal_lines",
                columns: new[] { "tenant_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_tenant_id_cost_center_id",
                schema: "accounting",
                table: "journal_lines",
                columns: new[] { "tenant_id", "cost_center_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_tenant_id_journal_entry_id",
                schema: "accounting",
                table: "journal_lines",
                columns: new[] { "tenant_id", "journal_entry_id" });

            migrationBuilder.CreateIndex(
                name: "ux_location_types_tenant_id_code",
                schema: "warehouse",
                table: "location_types",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_models_tenant_id_brand_id",
                schema: "catalog",
                table: "models",
                columns: new[] { "tenant_id", "brand_id" });

            migrationBuilder.CreateIndex(
                name: "ux_models_brand_id_name",
                schema: "catalog",
                table: "models",
                columns: new[] { "brand_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_modules_code",
                schema: "iam",
                table: "modules",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_movement_types_tenant_id",
                schema: "inventory",
                table: "movement_types",
                column: "tenant_id",
                unique: true,
                filter: "is_initial_balance");

            migrationBuilder.CreateIndex(
                name: "ux_movement_types_tenant_id_code",
                schema: "inventory",
                table: "movement_types",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payment_methods_tenant_id_code",
                schema: "sales",
                table: "payment_methods",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_invoice_id",
                schema: "sales",
                table: "payments",
                columns: new[] { "tenant_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_payment_method_id",
                schema: "sales",
                table: "payments",
                columns: new[] { "tenant_id", "payment_method_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_pos_session_id",
                schema: "sales",
                table: "payments",
                columns: new[] { "tenant_id", "pos_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_recorded_by_user_id",
                schema: "sales",
                table: "payments",
                columns: new[] { "tenant_id", "recorded_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_permissions_tenant_id_code",
                schema: "iam",
                table: "permissions",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_physical_count_lines_tenant_id_counted_by_user_id",
                schema: "inventory",
                table: "physical_count_lines",
                columns: new[] { "tenant_id", "counted_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_physical_count_lines_tenant_id_physical_count_id",
                schema: "inventory",
                table: "physical_count_lines",
                columns: new[] { "tenant_id", "physical_count_id" });

            migrationBuilder.CreateIndex(
                name: "ix_physical_count_lines_tenant_id_stock_level_id",
                schema: "inventory",
                table: "physical_count_lines",
                columns: new[] { "tenant_id", "stock_level_id" });

            migrationBuilder.CreateIndex(
                name: "ix_physical_count_lines_tenant_id_stock_movement_id",
                schema: "inventory",
                table: "physical_count_lines",
                columns: new[] { "tenant_id", "stock_movement_id" });

            migrationBuilder.CreateIndex(
                name: "ux_physical_count_lines_physical_count_id_stock_level_id",
                schema: "inventory",
                table: "physical_count_lines",
                columns: new[] { "physical_count_id", "stock_level_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_physical_counts_tenant_id_posted_by_user_id",
                schema: "inventory",
                table: "physical_counts",
                columns: new[] { "tenant_id", "posted_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_physical_counts_tenant_id_warehouse_id",
                schema: "inventory",
                table: "physical_counts",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ux_physical_counts_tenant_id_number",
                schema: "inventory",
                table: "physical_counts",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_physical_counts_warehouse_id",
                schema: "inventory",
                table: "physical_counts",
                column: "warehouse_id",
                unique: true,
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_pos_registers_tenant_id_hardware_token_id",
                schema: "sales",
                table: "pos_registers",
                columns: new[] { "tenant_id", "hardware_token_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pos_registers_tenant_id_warehouse_id",
                schema: "sales",
                table: "pos_registers",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ux_pos_registers_hardware_token_id",
                schema: "sales",
                table: "pos_registers",
                column: "hardware_token_id",
                unique: true,
                filter: "hardware_token_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_pos_registers_tenant_id_code",
                schema: "sales",
                table: "pos_registers",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pos_sessions_tenant_id_closed_by_user_id",
                schema: "sales",
                table: "pos_sessions",
                columns: new[] { "tenant_id", "closed_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pos_sessions_tenant_id_opened_by_user_id",
                schema: "sales",
                table: "pos_sessions",
                columns: new[] { "tenant_id", "opened_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pos_sessions_tenant_id_pos_register_id",
                schema: "sales",
                table: "pos_sessions",
                columns: new[] { "tenant_id", "pos_register_id" });

            migrationBuilder.CreateIndex(
                name: "ux_pos_sessions_pos_register_id",
                schema: "sales",
                table: "pos_sessions",
                column: "pos_register_id",
                unique: true,
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_postal_codes_tenant_id_city_id",
                schema: "sales",
                table: "postal_codes",
                columns: new[] { "tenant_id", "city_id" });

            migrationBuilder.CreateIndex(
                name: "ux_postal_codes_city_id_code",
                schema: "sales",
                table: "postal_codes",
                columns: new[] { "city_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_price_list_items_tenant_id_price_list_id",
                schema: "sales",
                table: "price_list_items",
                columns: new[] { "tenant_id", "price_list_id" });

            migrationBuilder.CreateIndex(
                name: "ix_price_list_items_tenant_id_variant_id",
                schema: "sales",
                table: "price_list_items",
                columns: new[] { "tenant_id", "variant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_price_lists_tenant_id_currency_id",
                schema: "sales",
                table: "price_lists",
                columns: new[] { "tenant_id", "currency_id" });

            migrationBuilder.CreateIndex(
                name: "ux_price_lists_tenant_id",
                schema: "sales",
                table: "price_lists",
                column: "tenant_id",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ux_price_lists_tenant_id_name",
                schema: "sales",
                table: "price_lists",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_barcodes_tenant_id_barcode_type_id",
                schema: "catalog",
                table: "product_barcodes",
                columns: new[] { "tenant_id", "barcode_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_barcodes_tenant_id_unit_id",
                schema: "catalog",
                table: "product_barcodes",
                columns: new[] { "tenant_id", "unit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_barcodes_tenant_id_variant_id",
                schema: "catalog",
                table: "product_barcodes",
                columns: new[] { "tenant_id", "variant_id" });

            migrationBuilder.CreateIndex(
                name: "ux_product_barcodes_tenant_id_code",
                schema: "catalog",
                table: "product_barcodes",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_product_barcodes_variant_id",
                schema: "catalog",
                table: "product_barcodes",
                column: "variant_id",
                unique: true,
                filter: "is_primary");

            migrationBuilder.CreateIndex(
                name: "ix_product_stock_policies_tenant_id_variant_id",
                schema: "catalog",
                table: "product_stock_policies",
                columns: new[] { "tenant_id", "variant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_stock_policies_tenant_id_warehouse_id",
                schema: "catalog",
                table: "product_stock_policies",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ux_product_stock_policies_variant_id_warehouse_id",
                schema: "catalog",
                table: "product_stock_policies",
                columns: new[] { "variant_id", "warehouse_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_suppliers_tenant_id_product_id",
                schema: "catalog",
                table: "product_suppliers",
                columns: new[] { "tenant_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_suppliers_tenant_id_supplier_id",
                schema: "catalog",
                table: "product_suppliers",
                columns: new[] { "tenant_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ux_product_suppliers_product_id",
                schema: "catalog",
                table: "product_suppliers",
                column: "product_id",
                unique: true,
                filter: "is_preferred");

            migrationBuilder.CreateIndex(
                name: "ix_product_taxes_tenant_id_product_id",
                schema: "catalog",
                table: "product_taxes",
                columns: new[] { "tenant_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_taxes_tenant_id_tax_id",
                schema: "catalog",
                table: "product_taxes",
                columns: new[] { "tenant_id", "tax_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_unit_conversions_tenant_id_product_id",
                schema: "catalog",
                table: "product_unit_conversions",
                columns: new[] { "tenant_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_unit_conversions_tenant_id_unit_id",
                schema: "catalog",
                table: "product_unit_conversions",
                columns: new[] { "tenant_id", "unit_id" });

            migrationBuilder.CreateIndex(
                name: "ux_product_unit_conversions_product_id_unit_id",
                schema: "catalog",
                table: "product_unit_conversions",
                columns: new[] { "product_id", "unit_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_variant_attributes_tenant_id_attribute_value_id",
                schema: "catalog",
                table: "product_variant_attributes",
                columns: new[] { "tenant_id", "attribute_value_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_variant_attributes_tenant_id_variant_id",
                schema: "catalog",
                table: "product_variant_attributes",
                columns: new[] { "tenant_id", "variant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_variants_tenant_id_product_id",
                schema: "catalog",
                table: "product_variants",
                columns: new[] { "tenant_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ux_product_variants_product_id",
                schema: "catalog",
                table: "product_variants",
                column: "product_id",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ux_product_variants_tenant_id_sku",
                schema: "catalog",
                table: "product_variants",
                columns: new[] { "tenant_id", "sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id_base_unit_id",
                schema: "catalog",
                table: "products",
                columns: new[] { "tenant_id", "base_unit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id_category_id",
                schema: "catalog",
                table: "products",
                columns: new[] { "tenant_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id_model_id",
                schema: "catalog",
                table: "products",
                columns: new[] { "tenant_id", "model_id" });

            migrationBuilder.CreateIndex(
                name: "ux_products_tenant_id_code",
                schema: "catalog",
                table: "products",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_tenant_id_purchase_order_id",
                schema: "purchasing",
                table: "purchase_order_lines",
                columns: new[] { "tenant_id", "purchase_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_tenant_id_unit_id",
                schema: "purchasing",
                table: "purchase_order_lines",
                columns: new[] { "tenant_id", "unit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_tenant_id_variant_id",
                schema: "purchasing",
                table: "purchase_order_lines",
                columns: new[] { "tenant_id", "variant_id" });

            migrationBuilder.CreateIndex(
                name: "ux_purchase_order_lines_purchase_order_id_variant_id_unit_id",
                schema: "purchasing",
                table: "purchase_order_lines",
                columns: new[] { "purchase_order_id", "variant_id", "unit_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_tenant_id_currency_id",
                schema: "purchasing",
                table: "purchase_orders",
                columns: new[] { "tenant_id", "currency_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_tenant_id_supplier_id",
                schema: "purchasing",
                table: "purchase_orders",
                columns: new[] { "tenant_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_tenant_id_warehouse_id",
                schema: "purchasing",
                table: "purchase_orders",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ux_purchase_orders_tenant_id_number",
                schema: "purchasing",
                table: "purchase_orders",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_return_lines_tenant_id_goods_receipt_line_id",
                schema: "purchasing",
                table: "purchase_return_lines",
                columns: new[] { "tenant_id", "goods_receipt_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_return_lines_tenant_id_purchase_return_id",
                schema: "purchasing",
                table: "purchase_return_lines",
                columns: new[] { "tenant_id", "purchase_return_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_return_lines_tenant_id_stock_level_id",
                schema: "purchasing",
                table: "purchase_return_lines",
                columns: new[] { "tenant_id", "stock_level_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_return_lines_tenant_id_stock_movement_id",
                schema: "purchasing",
                table: "purchase_return_lines",
                columns: new[] { "tenant_id", "stock_movement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_returns_tenant_id_supplier_id",
                schema: "purchasing",
                table: "purchase_returns",
                columns: new[] { "tenant_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ux_purchase_returns_tenant_id_number",
                schema: "purchasing",
                table: "purchase_returns",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_racks_tenant_id_aisle_id",
                schema: "warehouse",
                table: "racks",
                columns: new[] { "tenant_id", "aisle_id" });

            migrationBuilder.CreateIndex(
                name: "ux_racks_aisle_id_code",
                schema: "warehouse",
                table: "racks",
                columns: new[] { "aisle_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_tenant_id_permission_id",
                schema: "iam",
                table: "role_permissions",
                columns: new[] { "tenant_id", "permission_id" });

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_tenant_id_role_id",
                schema: "iam",
                table: "role_permissions",
                columns: new[] { "tenant_id", "role_id" });

            migrationBuilder.CreateIndex(
                name: "ux_roles_tenant_id_code",
                schema: "iam",
                table: "roles",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_lines_tenant_id_sales_order_id",
                schema: "sales",
                table: "sales_order_lines",
                columns: new[] { "tenant_id", "sales_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_lines_tenant_id_stock_movement_id",
                schema: "sales",
                table: "sales_order_lines",
                columns: new[] { "tenant_id", "stock_movement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_lines_tenant_id_unit_id",
                schema: "sales",
                table: "sales_order_lines",
                columns: new[] { "tenant_id", "unit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_lines_tenant_id_variant_id",
                schema: "sales",
                table: "sales_order_lines",
                columns: new[] { "tenant_id", "variant_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sales_order_lines_stock_movement_id",
                schema: "sales",
                table: "sales_order_lines",
                column: "stock_movement_id",
                unique: true,
                filter: "stock_movement_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_tenant_id_customer_id",
                schema: "sales",
                table: "sales_orders",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_tenant_id_pos_session_id",
                schema: "sales",
                table: "sales_orders",
                columns: new[] { "tenant_id", "pos_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_tenant_id_price_list_id",
                schema: "sales",
                table: "sales_orders",
                columns: new[] { "tenant_id", "price_list_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_tenant_id_warehouse_id",
                schema: "sales",
                table: "sales_orders",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sales_orders_tenant_id_number",
                schema: "sales",
                table: "sales_orders",
                columns: new[] { "tenant_id", "number" },
                unique: true);

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
                name: "ix_sessions_tenant_id_hardware_token_id",
                schema: "iam",
                table: "sessions",
                columns: new[] { "tenant_id", "hardware_token_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_tenant_id_user_id",
                schema: "iam",
                table: "sessions",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_user_id_started_at",
                schema: "iam",
                table: "sessions",
                columns: new[] { "user_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shelves_tenant_id_rack_id",
                schema: "warehouse",
                table: "shelves",
                columns: new[] { "tenant_id", "rack_id" });

            migrationBuilder.CreateIndex(
                name: "ux_shelves_rack_id_code",
                schema: "warehouse",
                table: "shelves",
                columns: new[] { "rack_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_states_tenant_id_country_id",
                schema: "sales",
                table: "states",
                columns: new[] { "tenant_id", "country_id" });

            migrationBuilder.CreateIndex(
                name: "ux_states_country_id_code",
                schema: "sales",
                table: "states",
                columns: new[] { "country_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustment_lines_tenant_id_movement_type_id",
                schema: "inventory",
                table: "stock_adjustment_lines",
                columns: new[] { "tenant_id", "movement_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustment_lines_tenant_id_stock_adjustment_id",
                schema: "inventory",
                table: "stock_adjustment_lines",
                columns: new[] { "tenant_id", "stock_adjustment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustment_lines_tenant_id_stock_level_id",
                schema: "inventory",
                table: "stock_adjustment_lines",
                columns: new[] { "tenant_id", "stock_level_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustment_lines_tenant_id_stock_movement_id",
                schema: "inventory",
                table: "stock_adjustment_lines",
                columns: new[] { "tenant_id", "stock_movement_id" });

            migrationBuilder.CreateIndex(
                name: "ux_stock_adjustment_lines_stock_movement_id",
                schema: "inventory",
                table: "stock_adjustment_lines",
                column: "stock_movement_id",
                unique: true,
                filter: "stock_movement_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustments_tenant_id_adjustment_reason_id",
                schema: "inventory",
                table: "stock_adjustments",
                columns: new[] { "tenant_id", "adjustment_reason_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustments_tenant_id_posted_by_user_id",
                schema: "inventory",
                table: "stock_adjustments",
                columns: new[] { "tenant_id", "posted_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustments_tenant_id_warehouse_id",
                schema: "inventory",
                table: "stock_adjustments",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ux_stock_adjustments_tenant_id_number",
                schema: "inventory",
                table: "stock_adjustments",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_levels_tenant_id_batch_id",
                schema: "inventory",
                table: "stock_levels",
                columns: new[] { "tenant_id", "batch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_levels_tenant_id_bin_id",
                schema: "inventory",
                table: "stock_levels",
                columns: new[] { "tenant_id", "bin_id" });

            migrationBuilder.CreateIndex(
                name: "ux_stock_levels_bin_id_batch_id",
                schema: "inventory",
                table: "stock_levels",
                columns: new[] { "bin_id", "batch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_correlation_id",
                schema: "inventory",
                table: "stock_movements",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_stock_level_id_recorded_at",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "stock_level_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_id_adjustment_reason_id",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "tenant_id", "adjustment_reason_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_id_business_date",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "tenant_id", "business_date" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_id_movement_type_id",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "tenant_id", "movement_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_id_recorded_by_user_id",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "tenant_id", "recorded_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_id_stock_level_id",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "tenant_id", "stock_level_id" });

            migrationBuilder.CreateIndex(
                name: "ux_stock_movements_tenant_id_legacy_reference",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "tenant_id", "legacy_reference" },
                unique: true,
                filter: "legacy_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_stock_level_id_status",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "stock_level_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_tenant_id_pos_session_id",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "tenant_id", "pos_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_tenant_id_sales_order_line_id",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "tenant_id", "sales_order_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_tenant_id_stock_level_id",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "tenant_id", "stock_level_id" });

            migrationBuilder.CreateIndex(
                name: "ux_stock_statuses_tenant_id_code",
                schema: "inventory",
                table: "stock_statuses",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_stock_statuses_tenant_id_priority",
                schema: "inventory",
                table: "stock_statuses",
                columns: new[] { "tenant_id", "priority" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_tenant_id_destination_stock_level_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                columns: new[] { "tenant_id", "destination_stock_level_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_tenant_id_inbound_movement_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                columns: new[] { "tenant_id", "inbound_movement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_tenant_id_outbound_movement_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                columns: new[] { "tenant_id", "outbound_movement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_tenant_id_source_stock_level_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                columns: new[] { "tenant_id", "source_stock_level_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_tenant_id_stock_transfer_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                columns: new[] { "tenant_id", "stock_transfer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_tenant_id_from_warehouse_id",
                schema: "inventory",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "from_warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_tenant_id_to_warehouse_id",
                schema: "inventory",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "to_warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ux_stock_transfers_tenant_id_number",
                schema: "inventory",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_supplier_addresses_tenant_id_address_id",
                schema: "purchasing",
                table: "supplier_addresses",
                columns: new[] { "tenant_id", "address_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_addresses_tenant_id_supplier_id",
                schema: "purchasing",
                table: "supplier_addresses",
                columns: new[] { "tenant_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_contacts_tenant_id_supplier_id",
                schema: "purchasing",
                table: "supplier_contacts",
                columns: new[] { "tenant_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ux_supplier_contacts_supplier_id",
                schema: "purchasing",
                table: "supplier_contacts",
                column: "supplier_id",
                unique: true,
                filter: "is_primary");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoice_lines_tenant_id_goods_receipt_line_id",
                schema: "purchasing",
                table: "supplier_invoice_lines",
                columns: new[] { "tenant_id", "goods_receipt_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoice_lines_tenant_id_supplier_invoice_id",
                schema: "purchasing",
                table: "supplier_invoice_lines",
                columns: new[] { "tenant_id", "supplier_invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoice_lines_tenant_id_tax_rate_id",
                schema: "purchasing",
                table: "supplier_invoice_lines",
                columns: new[] { "tenant_id", "tax_rate_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoices_tenant_id_currency_id",
                schema: "purchasing",
                table: "supplier_invoices",
                columns: new[] { "tenant_id", "currency_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoices_tenant_id_supplier_id",
                schema: "purchasing",
                table: "supplier_invoices",
                columns: new[] { "tenant_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ux_supplier_invoices_supplier_id_number",
                schema: "purchasing",
                table: "supplier_invoices",
                columns: new[] { "supplier_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_suppliers_tenant_id_code",
                schema: "purchasing",
                table: "suppliers",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_suppliers_tenant_id_legal_name",
                schema: "purchasing",
                table: "suppliers",
                columns: new[] { "tenant_id", "legal_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_rates_tenant_id_tax_id",
                schema: "accounting",
                table: "tax_rates",
                columns: new[] { "tenant_id", "tax_id" });

            migrationBuilder.CreateIndex(
                name: "ux_tax_rates_tax_id_valid_from",
                schema: "accounting",
                table: "tax_rates",
                columns: new[] { "tax_id", "valid_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_rules_tenant_id_branch_id",
                schema: "accounting",
                table: "tax_rules",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_rules_tenant_id_customer_category_id",
                schema: "accounting",
                table: "tax_rules",
                columns: new[] { "tenant_id", "customer_category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_rules_tenant_id_tax_id",
                schema: "accounting",
                table: "tax_rules",
                columns: new[] { "tenant_id", "tax_id" });

            migrationBuilder.CreateIndex(
                name: "ux_tax_rules_tax_id_customer_category_id_branch_id",
                schema: "accounting",
                table: "tax_rules",
                columns: new[] { "tax_id", "customer_category_id", "branch_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ux_taxes_tenant_id_code",
                schema: "catalog",
                table: "taxes",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_configs_tenant_id_default_currency_id",
                schema: "iam",
                table: "tenant_configs",
                columns: new[] { "tenant_id", "default_currency_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_configs_tenant_id_default_warehouse_id",
                schema: "iam",
                table: "tenant_configs",
                columns: new[] { "tenant_id", "default_warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_modules_module_id",
                schema: "iam",
                table: "tenant_modules",
                column: "module_id");

            migrationBuilder.CreateIndex(
                name: "ux_tenants_code",
                schema: "iam",
                table: "tenants",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_unit_conversions_tenant_id_from_unit_id",
                schema: "catalog",
                table: "unit_conversions",
                columns: new[] { "tenant_id", "from_unit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_unit_conversions_tenant_id_to_unit_id",
                schema: "catalog",
                table: "unit_conversions",
                columns: new[] { "tenant_id", "to_unit_id" });

            migrationBuilder.CreateIndex(
                name: "ux_unit_conversions_from_unit_id_to_unit_id",
                schema: "catalog",
                table: "unit_conversions",
                columns: new[] { "from_unit_id", "to_unit_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_units_of_measure_tenant_id_code",
                schema: "catalog",
                table: "units_of_measure",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_credentials_tenant_id_user_id",
                schema: "iam",
                table: "user_credentials",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_tenant_id_role_id",
                schema: "iam",
                table: "user_roles",
                columns: new[] { "tenant_id", "role_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_tenant_id_user_id",
                schema: "iam",
                table: "user_roles",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_users_tenant_id_email",
                schema: "iam",
                table: "users",
                columns: new[] { "tenant_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_tenant_id_branch_id",
                schema: "warehouse",
                table: "warehouses",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ux_warehouses_tenant_id_code",
                schema: "warehouse",
                table: "warehouses",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_zones_tenant_id_location_type_id",
                schema: "warehouse",
                table: "zones",
                columns: new[] { "tenant_id", "location_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_zones_tenant_id_warehouse_id",
                schema: "warehouse",
                table: "zones",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ux_zones_warehouse_id_code",
                schema: "warehouse",
                table: "zones",
                columns: new[] { "warehouse_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_logs",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "audit_logs",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "average_cost_history",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "bin_assignments",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "branch_users",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "cash_movements",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "category_hierarchies",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "customer_addresses",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "exchange_rates",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "invoice_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "journal_lines",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "physical_count_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "price_list_items",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "product_barcodes",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_stock_policies",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_suppliers",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_taxes",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_unit_conversions",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_variant_attributes",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "purchase_return_lines",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "role_permissions",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "serial_numbers",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "sessions",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "stock_adjustment_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_reservations",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_statuses",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_transfer_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "supplier_addresses",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "supplier_contacts",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "supplier_invoice_lines",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "tax_rules",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "tenant_configs",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "tenant_modules",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "unit_conversions",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "user_credentials",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "user_roles",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "cost_centers",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "journal_entries",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "payment_methods",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "physical_counts",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "barcode_types",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "attribute_values",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "purchase_returns",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "permissions",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "stock_adjustments",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "sales_order_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "stock_transfers",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "goods_receipt_lines",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "supplier_invoices",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "tax_rates",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "modules",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "fiscal_periods",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "attributes",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "sales_orders",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "goods_receipts",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "purchase_order_lines",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "stock_movements",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "taxes",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "pos_sessions",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "purchase_orders",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "adjustment_reasons",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "movement_types",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_levels",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "customer_categories",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "users",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "pos_registers",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "suppliers",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "batches",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "bins",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "price_lists",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "hardware_tokens",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "product_variants",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "shelves",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "currencies",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "products",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "racks",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "units_of_measure",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "models",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "aisles",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "brands",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "zones",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "location_types",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "warehouses",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "branches",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "addresses",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "postal_codes",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "cities",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "states",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "countries",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "iam");
        }
    }
}
