using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MINV.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// V6 · Tienda web conectada: canal, contacto, reserva (estado <c>Reserved</c>), motivo del cierre y publicación en la web
    /// de los armados; bitácora append-only <c>sales.pc_build_events</c>; tercer origen de las reservas de stock (línea de
    /// armado); canal <c>storefront</c> en la auditoría. 152 → 153 tablas en 10 esquemas. El SQL propio de PostgreSQL (relleno
    /// del canal y de la bitácora, RLS, append-only, permisos, rol y usuario técnico, privilegios) está en <c>V6Storefront.Sql.cs</c>.
    /// </summary>
    public partial class V6Storefront : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_reservations_origen",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pc_builds_cotizacion",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pc_builds_estado",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_logs_canal",
                schema: "iam",
                table: "audit_logs");

            migrationBuilder.AddColumn<Guid>(
                name: "pc_build_line_id",
                schema: "inventory",
                table: "stock_reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancel_reason",
                schema: "sales",
                table: "pc_builds",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "channel",
                schema: "sales",
                table: "pc_builds",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "contact_email",
                schema: "sales",
                table: "pc_builds",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_name",
                schema: "sales",
                table: "pc_builds",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_phone",
                schema: "sales",
                table: "pc_builds",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                schema: "sales",
                table: "pc_builds",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "published_to_web",
                schema: "sales",
                table: "pc_builds",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reserved_at",
                schema: "sales",
                table: "pc_builds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reserved_until",
                schema: "sales",
                table: "pc_builds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pc_build_events",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pc_build_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    detail = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pc_build_events", x => x.id);
                    table.UniqueConstraint("ak_pc_build_events_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_pc_build_events_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_pc_build_events_accion", "action IN ('Created', 'Quoted', 'Reserved', 'Released', 'Expired', 'Sold', 'Cancelled', 'Published', 'Unpublished')");
                    table.CheckConstraint("ck_pc_build_events_estado", "status IN ('Draft', 'Quoted', 'Reserved', 'Sold', 'Cancelled')");
                    table.ForeignKey(
                        name: "fk_pc_build_events_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pc_build_events_tenant_id_branch_id_pc_build_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.pc_build_id },
                        principalSchema: "sales",
                        principalTable: "pc_builds",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pc_build_events_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_pc_build_line_id_status",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "pc_build_line_id", "status" },
                filter: "pc_build_line_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_tenant_id_branch_id_pc_build_line_id",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "tenant_id", "branch_id", "pc_build_line_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_reservations_origen",
                schema: "inventory",
                table: "stock_reservations",
                sql: "num_nonnulls(pos_session_id, sales_order_line_id, pc_build_line_id) <= 1");

            migrationBuilder.CreateIndex(
                name: "ix_pc_builds_tenant_id_published_to_web",
                schema: "sales",
                table: "pc_builds",
                columns: new[] { "tenant_id", "published_to_web" },
                filter: "published_to_web");

            migrationBuilder.CreateIndex(
                name: "ix_pc_builds_tenant_id_reserved_until",
                schema: "sales",
                table: "pc_builds",
                columns: new[] { "tenant_id", "reserved_until" },
                filter: "status = 'Reserved'");

            V6Backfill(migrationBuilder);

            migrationBuilder.AddCheckConstraint(
                name: "ck_pc_builds_canal",
                schema: "sales",
                table: "pc_builds",
                sql: "channel IN ('Desktop', 'Web')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pc_builds_contacto",
                schema: "sales",
                table: "pc_builds",
                sql: "channel <> 'Web' OR (contact_name IS NOT NULL AND contact_phone IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pc_builds_cotizacion",
                schema: "sales",
                table: "pc_builds",
                sql: "status NOT IN ('Quoted', 'Reserved') OR quoted_at IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pc_builds_estado",
                schema: "sales",
                table: "pc_builds",
                sql: "status IN ('Draft', 'Quoted', 'Reserved', 'Sold', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pc_builds_publicado",
                schema: "sales",
                table: "pc_builds",
                sql: "NOT published_to_web OR (channel = 'Desktop' AND status IN ('Quoted', 'Reserved', 'Sold'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pc_builds_reserva",
                schema: "sales",
                table: "pc_builds",
                sql: "status <> 'Reserved' OR (reserved_at IS NOT NULL AND reserved_until IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_logs_canal",
                schema: "iam",
                table: "audit_logs",
                sql: "channel IS NULL OR channel IN ('desktop', 'cloud', 'api', 'storefront')");

            migrationBuilder.CreateIndex(
                name: "ix_pc_build_events_pc_build_id_occurred_at",
                schema: "sales",
                table: "pc_build_events",
                columns: new[] { "pc_build_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pc_build_events_tenant_id_branch_id_pc_build_id",
                schema: "sales",
                table: "pc_build_events",
                columns: new[] { "tenant_id", "branch_id", "pc_build_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pc_build_events_tenant_id_user_id",
                schema: "sales",
                table: "pc_build_events",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_stock_reservations_tenant_id_branch_id_pc_build_line_id",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "tenant_id", "branch_id", "pc_build_line_id" },
                principalSchema: "sales",
                principalTable: "pc_build_lines",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            V6Guards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            V6DropGuards(migrationBuilder);

            migrationBuilder.DropForeignKey(
                name: "fk_stock_reservations_tenant_id_branch_id_pc_build_line_id",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropTable(
                name: "pc_build_events",
                schema: "sales");

            migrationBuilder.DropIndex(
                name: "ix_stock_reservations_pc_build_line_id_status",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropIndex(
                name: "ix_stock_reservations_tenant_id_branch_id_pc_build_line_id",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_reservations_origen",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropIndex(
                name: "ix_pc_builds_tenant_id_published_to_web",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropIndex(
                name: "ix_pc_builds_tenant_id_reserved_until",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pc_builds_canal",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pc_builds_contacto",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pc_builds_cotizacion",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pc_builds_estado",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pc_builds_publicado",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pc_builds_reserva",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_logs_canal",
                schema: "iam",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "pc_build_line_id",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropColumn(
                name: "cancel_reason",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropColumn(
                name: "channel",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropColumn(
                name: "contact_email",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropColumn(
                name: "contact_name",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropColumn(
                name: "contact_phone",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropColumn(
                name: "notes",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropColumn(
                name: "published_to_web",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropColumn(
                name: "reserved_at",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.DropColumn(
                name: "reserved_until",
                schema: "sales",
                table: "pc_builds");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_reservations_origen",
                schema: "inventory",
                table: "stock_reservations",
                sql: "num_nonnulls(pos_session_id, sales_order_line_id) <= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pc_builds_cotizacion",
                schema: "sales",
                table: "pc_builds",
                sql: "status <> 'Quoted' OR quoted_at IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pc_builds_estado",
                schema: "sales",
                table: "pc_builds",
                sql: "status IN ('Draft', 'Quoted', 'Sold', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_logs_canal",
                schema: "iam",
                table: "audit_logs",
                sql: "channel IS NULL OR channel IN ('desktop', 'cloud', 'api')");
        }
    }
}
