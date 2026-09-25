using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MINV.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// V4 · FK directa (tenant_id, branch_id) → warehouse.branches en las cabeceras de sucursal que no cuelgan de un almacén
    /// (asientos, facturas de proveedor y devoluciones a proveedor): la sucursal de todo documento existe y es de la empresa.
    /// </summary>
    public partial class V4BranchHeaderKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "fk_journal_entries_tenant_id_branch_id",
                schema: "accounting",
                table: "journal_entries",
                columns: new[] { "tenant_id", "branch_id" },
                principalSchema: "warehouse",
                principalTable: "branches",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_returns_tenant_id_branch_id",
                schema: "purchasing",
                table: "purchase_returns",
                columns: new[] { "tenant_id", "branch_id" },
                principalSchema: "warehouse",
                principalTable: "branches",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_supplier_invoices_tenant_id_branch_id",
                schema: "purchasing",
                table: "supplier_invoices",
                columns: new[] { "tenant_id", "branch_id" },
                principalSchema: "warehouse",
                principalTable: "branches",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_journal_entries_tenant_id_branch_id",
                schema: "accounting",
                table: "journal_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_returns_tenant_id_branch_id",
                schema: "purchasing",
                table: "purchase_returns");

            migrationBuilder.DropForeignKey(
                name: "fk_supplier_invoices_tenant_id_branch_id",
                schema: "purchasing",
                table: "supplier_invoices");
        }
    }
}
