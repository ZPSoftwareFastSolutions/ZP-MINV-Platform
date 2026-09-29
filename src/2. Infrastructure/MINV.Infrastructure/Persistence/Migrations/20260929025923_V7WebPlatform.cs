using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MINV.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V7WebPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
