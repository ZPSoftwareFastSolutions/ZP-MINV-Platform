using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MINV.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// V3.1 · imágenes de productos (<c>catalog.product_images</c>): una por variante, en bytea, con la misma seguridad
    /// por empresa (Row Level Security) y los mismos permisos de <c>minv_app</c> que el resto del catálogo.
    /// </summary>
    public partial class ProductImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_images",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_images", x => x.id);
                    table.UniqueConstraint("ak_product_images_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_product_images_tamano", "octet_length(content) BETWEEN 1 AND 1048576");
                    table.CheckConstraint("ck_product_images_tipo", "content_type IN ('image/png', 'image/jpeg')");
                    table.ForeignKey(
                        name: "fk_product_images_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_images_tenant_id_variant_id",
                        columns: x => new { x.tenant_id, x.variant_id },
                        principalSchema: "catalog",
                        principalTable: "product_variants",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_product_images_tenant_id_variant_id",
                schema: "catalog",
                table: "product_images",
                columns: new[] { "tenant_id", "variant_id" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE catalog.product_images ENABLE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON catalog.product_images;
                CREATE POLICY tenant_isolation ON catalog.product_images
                    USING (tenant_id = iam.current_tenant_id()) WITH CHECK (tenant_id = iam.current_tenant_id());
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'minv_app') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON catalog.product_images TO minv_app;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON catalog.product_images;");
            migrationBuilder.DropTable(
                name: "product_images",
                schema: "catalog");
        }
    }
}
