using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MINV.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V41CafcNumbering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_fiscal_documents_tenant_id_environment_point_of_sal_c1b923c7",
                schema: "billing",
                table: "fiscal_documents");

            migrationBuilder.CreateIndex(
                name: "ux_fiscal_documents_tenant_id_environment_point_of_sal_c1b923c7",
                schema: "billing",
                table: "fiscal_documents",
                columns: new[] { "tenant_id", "environment", "point_of_sale_id", "document_sector", "number" },
                unique: true,
                filter: "cafc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_fiscal_documents_tenant_id_environment_point_of_sal_c5eda27d",
                schema: "billing",
                table: "fiscal_documents",
                columns: new[] { "tenant_id", "environment", "point_of_sale_id", "document_sector", "cafc", "number" },
                unique: true,
                filter: "cafc IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_fiscal_documents_tenant_id_environment_point_of_sal_c1b923c7",
                schema: "billing",
                table: "fiscal_documents");

            migrationBuilder.DropIndex(
                name: "ux_fiscal_documents_tenant_id_environment_point_of_sal_c5eda27d",
                schema: "billing",
                table: "fiscal_documents");

            migrationBuilder.CreateIndex(
                name: "ux_fiscal_documents_tenant_id_environment_point_of_sal_c1b923c7",
                schema: "billing",
                table: "fiscal_documents",
                columns: new[] { "tenant_id", "environment", "point_of_sale_id", "document_sector", "number" },
                unique: true);
        }
    }
}
