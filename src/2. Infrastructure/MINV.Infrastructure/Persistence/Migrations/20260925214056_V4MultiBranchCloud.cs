using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MINV.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// V4 · Multi-sucursal en la nube: jerarquía <c>branch_id</c> en las tablas transaccionales, transferencias con
    /// mercancía en tránsito (manifiesto por lote, faltantes, bitácora), integraciones B2B (API Keys, webhooks, outbox),
    /// idempotencia (pedidos externos, peticiones procesadas), sesiones con token para el servidor en la nube y costo
    /// promedio con secuencia. El SQL propio de PostgreSQL está en <c>V4MultiBranchCloud.Sql.cs</c>.
    /// </summary>
    public partial class V4MultiBranchCloud : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            V4Guard(migrationBuilder);

            migrationBuilder.DropForeignKey(
                name: "fk_aisles_tenant_id_zone_id",
                schema: "warehouse",
                table: "aisles");

            migrationBuilder.DropForeignKey(
                name: "fk_average_cost_history_tenant_id_stock_movement_id",
                schema: "accounting",
                table: "average_cost_history");

            migrationBuilder.DropForeignKey(
                name: "fk_average_cost_history_tenant_id_warehouse_id",
                schema: "accounting",
                table: "average_cost_history");

            migrationBuilder.DropForeignKey(
                name: "fk_bin_assignments_tenant_id_bin_id",
                schema: "warehouse",
                table: "bin_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_bins_tenant_id_shelf_id",
                schema: "warehouse",
                table: "bins");

            migrationBuilder.DropForeignKey(
                name: "fk_cash_movements_tenant_id_pos_session_id",
                schema: "sales",
                table: "cash_movements");

            migrationBuilder.DropForeignKey(
                name: "fk_goods_receipt_lines_tenant_id_goods_receipt_id",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_goods_receipt_lines_tenant_id_purchase_order_line_id",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_goods_receipt_lines_tenant_id_stock_level_id",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_goods_receipt_lines_tenant_id_stock_movement_id",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_goods_receipts_tenant_id_purchase_order_id",
                schema: "purchasing",
                table: "goods_receipts");

            migrationBuilder.DropForeignKey(
                name: "fk_goods_receipts_tenant_id_warehouse_id",
                schema: "purchasing",
                table: "goods_receipts");

            migrationBuilder.DropForeignKey(
                name: "fk_invoice_lines_tenant_id_invoice_id",
                schema: "sales",
                table: "invoice_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_invoice_lines_tenant_id_sales_order_line_id",
                schema: "sales",
                table: "invoice_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_invoices_tenant_id_sales_order_id",
                schema: "sales",
                table: "invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_journal_lines_tenant_id_journal_entry_id",
                schema: "accounting",
                table: "journal_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_payments_tenant_id_invoice_id",
                schema: "sales",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "fk_payments_tenant_id_pos_session_id",
                schema: "sales",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "fk_physical_count_lines_tenant_id_physical_count_id",
                schema: "inventory",
                table: "physical_count_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_physical_count_lines_tenant_id_stock_level_id",
                schema: "inventory",
                table: "physical_count_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_physical_count_lines_tenant_id_stock_movement_id",
                schema: "inventory",
                table: "physical_count_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_physical_counts_tenant_id_warehouse_id",
                schema: "inventory",
                table: "physical_counts");

            migrationBuilder.DropForeignKey(
                name: "fk_pos_registers_tenant_id_warehouse_id",
                schema: "sales",
                table: "pos_registers");

            migrationBuilder.DropForeignKey(
                name: "fk_pos_sessions_tenant_id_pos_register_id",
                schema: "sales",
                table: "pos_sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_product_stock_policies_tenant_id_warehouse_id",
                schema: "catalog",
                table: "product_stock_policies");

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_order_lines_tenant_id_purchase_order_id",
                schema: "purchasing",
                table: "purchase_order_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_orders_tenant_id_warehouse_id",
                schema: "purchasing",
                table: "purchase_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_return_lines_tenant_id_goods_receipt_line_id",
                schema: "purchasing",
                table: "purchase_return_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_return_lines_tenant_id_purchase_return_id",
                schema: "purchasing",
                table: "purchase_return_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_return_lines_tenant_id_stock_level_id",
                schema: "purchasing",
                table: "purchase_return_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_return_lines_tenant_id_stock_movement_id",
                schema: "purchasing",
                table: "purchase_return_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_racks_tenant_id_aisle_id",
                schema: "warehouse",
                table: "racks");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_order_lines_tenant_id_sales_order_id",
                schema: "sales",
                table: "sales_order_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_order_lines_tenant_id_stock_movement_id",
                schema: "sales",
                table: "sales_order_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_orders_tenant_id_pos_session_id",
                schema: "sales",
                table: "sales_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_orders_tenant_id_warehouse_id",
                schema: "sales",
                table: "sales_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_shelves_tenant_id_rack_id",
                schema: "warehouse",
                table: "shelves");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_adjustment_lines_tenant_id_stock_adjustment_id",
                schema: "inventory",
                table: "stock_adjustment_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_adjustment_lines_tenant_id_stock_level_id",
                schema: "inventory",
                table: "stock_adjustment_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_adjustment_lines_tenant_id_stock_movement_id",
                schema: "inventory",
                table: "stock_adjustment_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_adjustments_tenant_id_warehouse_id",
                schema: "inventory",
                table: "stock_adjustments");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_levels_tenant_id_bin_id",
                schema: "inventory",
                table: "stock_levels");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_movements_tenant_id_stock_level_id",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_reservations_tenant_id_pos_session_id",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_reservations_tenant_id_sales_order_line_id",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_reservations_tenant_id_stock_level_id",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_transfer_lines_tenant_id_destination_stock_level_id",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_transfer_lines_tenant_id_inbound_movement_id",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_transfer_lines_tenant_id_outbound_movement_id",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_transfer_lines_tenant_id_source_stock_level_id",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_transfer_lines_tenant_id_stock_transfer_id",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_transfers_tenant_id_from_warehouse_id",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_transfers_tenant_id_to_warehouse_id",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropForeignKey(
                name: "fk_supplier_invoice_lines_tenant_id_goods_receipt_line_id",
                schema: "purchasing",
                table: "supplier_invoice_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_supplier_invoice_lines_tenant_id_supplier_invoice_id",
                schema: "purchasing",
                table: "supplier_invoice_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_zones_tenant_id_warehouse_id",
                schema: "warehouse",
                table: "zones");

            migrationBuilder.DropIndex(
                name: "ix_zones_tenant_id_warehouse_id",
                schema: "warehouse",
                table: "zones");

            migrationBuilder.DropIndex(
                name: "ix_warehouses_tenant_id_branch_id",
                schema: "warehouse",
                table: "warehouses");

            migrationBuilder.DropIndex(
                name: "ix_supplier_invoice_lines_tenant_id_goods_receipt_line_id",
                schema: "purchasing",
                table: "supplier_invoice_lines");

            migrationBuilder.DropIndex(
                name: "ix_supplier_invoice_lines_tenant_id_supplier_invoice_id",
                schema: "purchasing",
                table: "supplier_invoice_lines");

            migrationBuilder.DropIndex(
                name: "ix_stock_transfers_tenant_id_from_warehouse_id",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "ix_stock_transfers_tenant_id_to_warehouse_id",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "ix_stock_transfer_lines_tenant_id_destination_stock_level_id",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.DropIndex(
                name: "ix_stock_transfer_lines_tenant_id_inbound_movement_id",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.DropIndex(
                name: "ix_stock_transfer_lines_tenant_id_outbound_movement_id",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.DropIndex(
                name: "ix_stock_transfer_lines_tenant_id_stock_transfer_id",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.DropIndex(
                name: "ix_stock_reservations_tenant_id_pos_session_id",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropIndex(
                name: "ix_stock_reservations_tenant_id_sales_order_line_id",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropIndex(
                name: "ix_stock_reservations_tenant_id_stock_level_id",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropIndex(
                name: "ix_stock_movements_tenant_id_stock_level_id",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "ix_stock_levels_tenant_id_bin_id",
                schema: "inventory",
                table: "stock_levels");

            migrationBuilder.DropIndex(
                name: "ix_stock_adjustments_tenant_id_warehouse_id",
                schema: "inventory",
                table: "stock_adjustments");

            migrationBuilder.DropIndex(
                name: "ix_stock_adjustment_lines_tenant_id_stock_adjustment_id",
                schema: "inventory",
                table: "stock_adjustment_lines");

            migrationBuilder.DropIndex(
                name: "ix_stock_adjustment_lines_tenant_id_stock_level_id",
                schema: "inventory",
                table: "stock_adjustment_lines");

            migrationBuilder.DropIndex(
                name: "ix_stock_adjustment_lines_tenant_id_stock_movement_id",
                schema: "inventory",
                table: "stock_adjustment_lines");

            migrationBuilder.DropIndex(
                name: "ix_shelves_tenant_id_rack_id",
                schema: "warehouse",
                table: "shelves");

            migrationBuilder.DropIndex(
                name: "ix_sales_orders_tenant_id_pos_session_id",
                schema: "sales",
                table: "sales_orders");

            migrationBuilder.DropIndex(
                name: "ix_sales_orders_tenant_id_warehouse_id",
                schema: "sales",
                table: "sales_orders");

            migrationBuilder.DropIndex(
                name: "ix_sales_order_lines_tenant_id_sales_order_id",
                schema: "sales",
                table: "sales_order_lines");

            migrationBuilder.DropIndex(
                name: "ix_sales_order_lines_tenant_id_stock_movement_id",
                schema: "sales",
                table: "sales_order_lines");

            migrationBuilder.DropIndex(
                name: "ix_racks_tenant_id_aisle_id",
                schema: "warehouse",
                table: "racks");

            migrationBuilder.DropIndex(
                name: "ix_purchase_return_lines_tenant_id_goods_receipt_line_id",
                schema: "purchasing",
                table: "purchase_return_lines");

            migrationBuilder.DropIndex(
                name: "ix_purchase_return_lines_tenant_id_purchase_return_id",
                schema: "purchasing",
                table: "purchase_return_lines");

            migrationBuilder.DropIndex(
                name: "ix_purchase_return_lines_tenant_id_stock_level_id",
                schema: "purchasing",
                table: "purchase_return_lines");

            migrationBuilder.DropIndex(
                name: "ix_purchase_return_lines_tenant_id_stock_movement_id",
                schema: "purchasing",
                table: "purchase_return_lines");

            migrationBuilder.DropIndex(
                name: "ix_purchase_orders_tenant_id_warehouse_id",
                schema: "purchasing",
                table: "purchase_orders");

            migrationBuilder.DropIndex(
                name: "ix_purchase_order_lines_tenant_id_purchase_order_id",
                schema: "purchasing",
                table: "purchase_order_lines");

            migrationBuilder.DropIndex(
                name: "ix_product_stock_policies_tenant_id_warehouse_id",
                schema: "catalog",
                table: "product_stock_policies");

            migrationBuilder.DropIndex(
                name: "ix_pos_sessions_tenant_id_pos_register_id",
                schema: "sales",
                table: "pos_sessions");

            migrationBuilder.DropIndex(
                name: "ix_pos_registers_tenant_id_warehouse_id",
                schema: "sales",
                table: "pos_registers");

            migrationBuilder.DropIndex(
                name: "ix_physical_counts_tenant_id_warehouse_id",
                schema: "inventory",
                table: "physical_counts");

            migrationBuilder.DropIndex(
                name: "ix_physical_count_lines_tenant_id_physical_count_id",
                schema: "inventory",
                table: "physical_count_lines");

            migrationBuilder.DropIndex(
                name: "ix_physical_count_lines_tenant_id_stock_level_id",
                schema: "inventory",
                table: "physical_count_lines");

            migrationBuilder.DropIndex(
                name: "ix_physical_count_lines_tenant_id_stock_movement_id",
                schema: "inventory",
                table: "physical_count_lines");

            migrationBuilder.DropIndex(
                name: "ix_payments_tenant_id_invoice_id",
                schema: "sales",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_payments_tenant_id_pos_session_id",
                schema: "sales",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_journal_lines_tenant_id_journal_entry_id",
                schema: "accounting",
                table: "journal_lines");

            migrationBuilder.DropIndex(
                name: "ix_invoices_tenant_id_sales_order_id",
                schema: "sales",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "ix_invoice_lines_tenant_id_invoice_id",
                schema: "sales",
                table: "invoice_lines");

            migrationBuilder.DropIndex(
                name: "ix_invoice_lines_tenant_id_sales_order_line_id",
                schema: "sales",
                table: "invoice_lines");

            migrationBuilder.DropIndex(
                name: "ix_goods_receipts_tenant_id_purchase_order_id",
                schema: "purchasing",
                table: "goods_receipts");

            migrationBuilder.DropIndex(
                name: "ix_goods_receipts_tenant_id_warehouse_id",
                schema: "purchasing",
                table: "goods_receipts");

            migrationBuilder.DropIndex(
                name: "ix_goods_receipt_lines_tenant_id_goods_receipt_id",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropIndex(
                name: "ix_goods_receipt_lines_tenant_id_purchase_order_line_id",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropIndex(
                name: "ix_goods_receipt_lines_tenant_id_stock_level_id",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropIndex(
                name: "ix_goods_receipt_lines_tenant_id_stock_movement_id",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropIndex(
                name: "ix_cash_movements_tenant_id_pos_session_id",
                schema: "sales",
                table: "cash_movements");

            migrationBuilder.DropIndex(
                name: "ix_bins_tenant_id_shelf_id",
                schema: "warehouse",
                table: "bins");

            migrationBuilder.DropIndex(
                name: "ix_bin_assignments_tenant_id_bin_id",
                schema: "warehouse",
                table: "bin_assignments");

            migrationBuilder.DropIndex(
                name: "ix_average_cost_history_tenant_id_stock_movement_id",
                schema: "accounting",
                table: "average_cost_history");

            migrationBuilder.DropIndex(
                name: "ix_average_cost_history_tenant_id_variant_id",
                schema: "accounting",
                table: "average_cost_history");

            migrationBuilder.DropIndex(
                name: "ix_average_cost_history_tenant_id_warehouse_id",
                schema: "accounting",
                table: "average_cost_history");

            migrationBuilder.DropIndex(
                name: "ix_aisles_tenant_id_zone_id",
                schema: "warehouse",
                table: "aisles");

            migrationBuilder.DropColumn(
                name: "destination_stock_level_id",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.DropColumn(
                name: "inbound_movement_id",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.DropColumn(
                name: "outbound_movement_id",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.EnsureSchema(
                name: "integration");

            migrationBuilder.RenameColumn(
                name: "shipped_at",
                schema: "inventory",
                table: "stock_transfers",
                newName: "dispatched_at");

            migrationBuilder.RenameColumn(
                name: "source_stock_level_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                newName: "variant_id");

            migrationBuilder.RenameIndex(
                name: "ix_stock_transfer_lines_tenant_id_source_stock_level_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                newName: "ix_stock_transfer_lines_tenant_id_variant_id");

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "warehouse",
                table: "zones",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "purchasing",
                table: "supplier_invoices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "purchasing",
                table: "supplier_invoice_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "from_branch_id",
                schema: "inventory",
                table: "stock_transfers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "requested_at",
                schema: "inventory",
                table: "stock_transfers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "requested_by_user_id",
                schema: "inventory",
                table: "stock_transfers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "to_branch_id",
                schema: "inventory",
                table: "stock_transfers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "from_branch_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "to_branch_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "unit_cost",
                schema: "inventory",
                table: "stock_transfer_lines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "inventory",
                table: "stock_reservations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "inventory",
                table: "stock_movements",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "inventory",
                table: "stock_levels",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "inventory",
                table: "stock_adjustments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "inventory",
                table: "stock_adjustment_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "warehouse",
                table: "shelves",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "active_branch_id",
                schema: "iam",
                table: "sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                schema: "iam",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "token_hash",
                schema: "iam",
                table: "sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "sales",
                table: "sales_orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "sales",
                table: "sales_order_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "warehouse",
                table: "racks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "purchasing",
                table: "purchase_returns",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "purchasing",
                table: "purchase_return_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "purchasing",
                table: "purchase_orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "purchasing",
                table: "purchase_order_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "catalog",
                table: "product_stock_policies",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "sales",
                table: "pos_sessions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "sales",
                table: "pos_registers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "inventory",
                table: "physical_counts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "inventory",
                table: "physical_count_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "sales",
                table: "payments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "accounting",
                table: "journal_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "accounting",
                table: "journal_entries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "sales",
                table: "invoices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "sales",
                table: "invoice_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "purchasing",
                table: "goods_receipts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "sales",
                table: "cash_movements",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "warehouse",
                table: "bins",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "warehouse",
                table: "bin_assignments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "accounting",
                table: "average_cost_history",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "sequence",
                schema: "accounting",
                table: "average_cost_history",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "api_key_id",
                schema: "iam",
                table: "audit_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "iam",
                table: "audit_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "channel",
                schema: "iam",
                table: "audit_logs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "warehouse",
                table: "aisles",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // V4 · Las columnas branch_id nuevas se llenan ANTES de crear las claves alternas y las FK compuestas
            V4Backfill(migrationBuilder);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_zones_tenant_id_branch_id_id",
                schema: "warehouse",
                table: "zones",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_warehouses_tenant_id_branch_id_id",
                schema: "warehouse",
                table: "warehouses",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_supplier_invoices_tenant_id_branch_id_id",
                schema: "purchasing",
                table: "supplier_invoices",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_supplier_invoice_lines_tenant_id_branch_id_id",
                schema: "purchasing",
                table: "supplier_invoice_lines",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_stock_transfers_tenant_id_from_branch_id_to_branch_id_id",
                schema: "inventory",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "from_branch_id", "to_branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_stock_transfer_lines_tenant_id_from_branch_id_to_br_7655fbfd",
                schema: "inventory",
                table: "stock_transfer_lines",
                columns: new[] { "tenant_id", "from_branch_id", "to_branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_stock_reservations_tenant_id_branch_id_id",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_stock_movements_tenant_id_branch_id_id",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_stock_levels_tenant_id_branch_id_id",
                schema: "inventory",
                table: "stock_levels",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_stock_adjustments_tenant_id_branch_id_id",
                schema: "inventory",
                table: "stock_adjustments",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_stock_adjustment_lines_tenant_id_branch_id_id",
                schema: "inventory",
                table: "stock_adjustment_lines",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_shelves_tenant_id_branch_id_id",
                schema: "warehouse",
                table: "shelves",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_sales_orders_tenant_id_branch_id_id",
                schema: "sales",
                table: "sales_orders",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_sales_order_lines_tenant_id_branch_id_id",
                schema: "sales",
                table: "sales_order_lines",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_racks_tenant_id_branch_id_id",
                schema: "warehouse",
                table: "racks",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_purchase_returns_tenant_id_branch_id_id",
                schema: "purchasing",
                table: "purchase_returns",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_purchase_return_lines_tenant_id_branch_id_id",
                schema: "purchasing",
                table: "purchase_return_lines",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_purchase_orders_tenant_id_branch_id_id",
                schema: "purchasing",
                table: "purchase_orders",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_purchase_order_lines_tenant_id_branch_id_id",
                schema: "purchasing",
                table: "purchase_order_lines",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_product_stock_policies_tenant_id_branch_id_id",
                schema: "catalog",
                table: "product_stock_policies",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_pos_sessions_tenant_id_branch_id_id",
                schema: "sales",
                table: "pos_sessions",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_pos_registers_tenant_id_branch_id_id",
                schema: "sales",
                table: "pos_registers",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_physical_counts_tenant_id_branch_id_id",
                schema: "inventory",
                table: "physical_counts",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_physical_count_lines_tenant_id_branch_id_id",
                schema: "inventory",
                table: "physical_count_lines",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_payments_tenant_id_branch_id_id",
                schema: "sales",
                table: "payments",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_journal_lines_tenant_id_branch_id_id",
                schema: "accounting",
                table: "journal_lines",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_journal_entries_tenant_id_branch_id_id",
                schema: "accounting",
                table: "journal_entries",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_invoices_tenant_id_branch_id_id",
                schema: "sales",
                table: "invoices",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_invoice_lines_tenant_id_branch_id_id",
                schema: "sales",
                table: "invoice_lines",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_goods_receipts_tenant_id_branch_id_id",
                schema: "purchasing",
                table: "goods_receipts",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_goods_receipt_lines_tenant_id_branch_id_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_cash_movements_tenant_id_branch_id_id",
                schema: "sales",
                table: "cash_movements",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_bins_tenant_id_branch_id_id",
                schema: "warehouse",
                table: "bins",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_average_cost_history_tenant_id_branch_id_id",
                schema: "accounting",
                table: "average_cost_history",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_aisles_tenant_id_branch_id_id",
                schema: "warehouse",
                table: "aisles",
                columns: new[] { "tenant_id", "branch_id", "id" });

            migrationBuilder.CreateTable(
                name: "api_keys",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prefix = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_keys", x => x.id);
                    table.UniqueConstraint("ak_api_keys_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_api_keys_hash", "token_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_api_keys_prefijo", "prefix ~ '^[a-z0-9]{8}$'");
                    table.CheckConstraint("ck_api_keys_vencimiento", "expires_at IS NULL OR expires_at > created_at");
                    table.ForeignKey(
                        name: "fk_api_keys_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_api_keys_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "warehouse",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_api_keys_tenant_id_owner_user_id",
                        columns: x => new { x.tenant_id, x.owner_user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "external_orders",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sales_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_orders", x => x.id);
                    table.UniqueConstraint("ak_external_orders_tenant_id_branch_id_id", x => new { x.tenant_id, x.branch_id, x.id });
                    table.UniqueConstraint("ak_external_orders_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_external_orders_hash", "request_hash ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_external_orders_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_external_orders_tenant_id_branch_id_sales_order_id",
                        columns: x => new { x.tenant_id, x.branch_id, x.sales_order_id },
                        principalSchema: "sales",
                        principalTable: "sales_orders",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbox_events",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_events", x => x.id);
                    table.UniqueConstraint("ak_outbox_events_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_outbox_events_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outbox_events_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "warehouse",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "processed_requests",
                schema: "iam",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    response = table.Column<string>(type: "text", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_requests", x => x.id);
                    table.UniqueConstraint("ak_processed_requests_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_processed_requests_hash", "request_hash ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_processed_requests_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_processed_requests_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_discrepancies",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer_discrepancies", x => x.id);
                    table.UniqueConstraint("ak_stock_transfer_discrepancies_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_stock_transfer_discrepancies_cantidad", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_stock_transfer_discrepancies_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_discrepancies_tenant_id_from_branch__1bf1d075",
                        columns: x => new { x.tenant_id, x.from_branch_id, x.to_branch_id, x.transfer_line_id },
                        principalSchema: "inventory",
                        principalTable: "stock_transfer_lines",
                        principalColumns: new[] { "tenant_id", "from_branch_id", "to_branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_discrepancies_tenant_id_recorded_by_user_id",
                        columns: x => new { x.tenant_id, x.recorded_by_user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_events",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_stock_transfer_events", x => x.id);
                    table.UniqueConstraint("ak_stock_transfer_events_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_stock_transfer_events_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_events_tenant_id_from_branch_id_to_b_7078530b",
                        columns: x => new { x.tenant_id, x.from_branch_id, x.to_branch_id, x.transfer_id },
                        principalSchema: "inventory",
                        principalTable: "stock_transfers",
                        principalColumns: new[] { "tenant_id", "from_branch_id", "to_branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_events_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_line_batches",
                schema: "inventory",
                columns: table => new
                {
                    transfer_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer_line_batches", x => new { x.transfer_line_id, x.batch_id });
                    table.CheckConstraint("ck_stock_transfer_line_batches_cantidad", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_stock_transfer_line_batches_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_line_batches_tenant_id_batch_id",
                        columns: x => new { x.tenant_id, x.batch_id },
                        principalSchema: "inventory",
                        principalTable: "batches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_line_batches_tenant_id_from_branch_i_86ebd39b",
                        columns: x => new { x.tenant_id, x.from_branch_id, x.to_branch_id, x.transfer_line_id },
                        principalSchema: "inventory",
                        principalTable: "stock_transfer_lines",
                        principalColumns: new[] { "tenant_id", "from_branch_id", "to_branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_movements",
                schema: "inventory",
                columns: table => new
                {
                    transfer_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_movement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer_movements", x => new { x.transfer_line_id, x.stock_movement_id });
                    table.CheckConstraint("ck_stock_transfer_movements_sentido", "direction IN ('Out', 'In')");
                    table.ForeignKey(
                        name: "fk_stock_transfer_movements_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_movements_tenant_id_branch_id_stock__4b2e1eb5",
                        columns: x => new { x.tenant_id, x.branch_id, x.stock_movement_id },
                        principalSchema: "inventory",
                        principalTable: "stock_movements",
                        principalColumns: new[] { "tenant_id", "branch_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_movements_tenant_id_transfer_line_id",
                        columns: x => new { x.tenant_id, x.transfer_line_id },
                        principalSchema: "inventory",
                        principalTable: "stock_transfer_lines",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "api_key_scopes",
                schema: "integration",
                columns: table => new
                {
                    api_key_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_key_scopes", x => new { x.api_key_id, x.scope });
                    table.ForeignKey(
                        name: "fk_api_key_scopes_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_api_key_scopes_tenant_id_api_key_id",
                        columns: x => new { x.tenant_id, x.api_key_id },
                        principalSchema: "integration",
                        principalTable: "api_keys",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "webhook_endpoints",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    api_key_id = table.Column<Guid>(type: "uuid", nullable: true),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    secret_ciphertext = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    secret_key_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    secret_version = table.Column<int>(type: "integer", nullable: false),
                    previous_secret_ciphertext = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    previous_secret_key_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    previous_secret_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_endpoints", x => x.id);
                    table.UniqueConstraint("ak_webhook_endpoints_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_webhook_endpoints_baja", "is_active = (disabled_at IS NULL)");
                    table.CheckConstraint("ck_webhook_endpoints_rotacion", "(previous_secret_ciphertext IS NULL) = (previous_secret_expires_at IS NULL) AND (previous_secret_ciphertext IS NULL) = (previous_secret_key_id IS NULL)");
                    table.CheckConstraint("ck_webhook_endpoints_url", "url ~ '^(https://|http://localhost|http://127\\.0\\.0\\.1|http://\\[::1\\])'");
                    table.CheckConstraint("ck_webhook_endpoints_version", "secret_version >= 1");
                    table.ForeignKey(
                        name: "fk_webhook_endpoints_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_webhook_endpoints_tenant_id_api_key_id",
                        columns: x => new { x.tenant_id, x.api_key_id },
                        principalSchema: "integration",
                        principalTable: "api_keys",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_webhook_endpoints_tenant_id_branch_id",
                        columns: x => new { x.tenant_id, x.branch_id },
                        principalSchema: "warehouse",
                        principalTable: "branches",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_webhook_endpoints_tenant_id_created_by_user_id",
                        columns: x => new { x.tenant_id, x.created_by_user_id },
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbox_dispatch",
                schema: "integration",
                columns: table => new
                {
                    outbox_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rounds = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_outbox_dispatch", x => x.outbox_event_id);
                    table.CheckConstraint("ck_outbox_dispatch_estado", "status IN ('Pending', 'Completed', 'Exhausted')");
                    table.CheckConstraint("ck_outbox_dispatch_fin", "(status = 'Pending') = (completed_at IS NULL)");
                    table.CheckConstraint("ck_outbox_dispatch_rondas", "rounds BETWEEN 0 AND 8");
                    table.ForeignKey(
                        name: "fk_outbox_dispatch_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outbox_dispatch_tenant_id_outbox_event_id",
                        columns: x => new { x.tenant_id, x.outbox_event_id },
                        principalSchema: "integration",
                        principalTable: "outbox_events",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "webhook_deliveries",
                schema: "integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    outbox_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("pk_webhook_deliveries", x => x.id);
                    table.UniqueConstraint("ak_webhook_deliveries_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_webhook_deliveries_duracion", "duration_ms >= 0");
                    table.CheckConstraint("ck_webhook_deliveries_intento", "attempt BETWEEN 1 AND 8");
                    table.ForeignKey(
                        name: "fk_webhook_deliveries_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_webhook_deliveries_tenant_id_endpoint_id",
                        columns: x => new { x.tenant_id, x.endpoint_id },
                        principalSchema: "integration",
                        principalTable: "webhook_endpoints",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_webhook_deliveries_tenant_id_outbox_event_id",
                        columns: x => new { x.tenant_id, x.outbox_event_id },
                        principalSchema: "integration",
                        principalTable: "outbox_events",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "webhook_endpoint_events",
                schema: "integration",
                columns: table => new
                {
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_endpoint_events", x => new { x.endpoint_id, x.event_type });
                    table.ForeignKey(
                        name: "fk_webhook_endpoint_events_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "iam",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_webhook_endpoint_events_tenant_id_endpoint_id",
                        columns: x => new { x.tenant_id, x.endpoint_id },
                        principalSchema: "integration",
                        principalTable: "webhook_endpoints",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "iam",
                table: "modules",
                columns: new[] { "id", "code", "created_at", "created_by", "description", "monthly_fee_bs", "name", "setup_price_bs", "updated_at", "updated_by" },
                values: new object[,]
                {
                    { new Guid("01920000-0000-7000-8000-000000000006"), "CLOUD_HA", new DateTimeOffset(new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "PostgreSQL gestionado en la nube (AWS/DigitalOcean) con respaldos PITR, réplica y SLA 99,9 %.", 1500m, "Infraestructura Cloud HA", 12000m, null, null },
                    { new Guid("01920000-0000-7000-8000-000000000007"), "MULTI_BRANCH", new DateTimeOffset(new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Inventario aislado por sucursal (BranchId) y transferencias con mercancía en tránsito.", 500m, "Topología multi-sucursal", 8000m, null, null },
                    { new Guid("01920000-0000-7000-8000-000000000008"), "API_INTEGRATIONS", new DateTimeOffset(new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "API Gateway con API Keys y webhooks para e-commerce (Shopify) y ERP contable.", 400m, "Integraciones API (B2B)", 6000m, null, null },
                    { new Guid("01920000-0000-7000-8000-000000000009"), "GLOBAL_AUDIT", new DateTimeOffset(new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Modelo de lectura desnormalizado para gerencia sin cargar las cajas POS.", 300m, "Auditoría global (réplicas de lectura)", 5000m, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "ix_zones_tenant_id_branch_id_warehouse_id",
                schema: "warehouse",
                table: "zones",
                columns: new[] { "tenant_id", "branch_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoice_lines_tenant_id_branch_id_goods_re_33e178bb",
                schema: "purchasing",
                table: "supplier_invoice_lines",
                columns: new[] { "tenant_id", "branch_id", "goods_receipt_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoice_lines_tenant_id_branch_id_supplier_4d17bc14",
                schema: "purchasing",
                table: "supplier_invoice_lines",
                columns: new[] { "tenant_id", "branch_id", "supplier_invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_tenant_id_from_branch_id_from_warehouse_id",
                schema: "inventory",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "from_branch_id", "from_warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_tenant_id_requested_by_user_id",
                schema: "inventory",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "requested_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_tenant_id_status",
                schema: "inventory",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_tenant_id_to_branch_id_to_warehouse_id",
                schema: "inventory",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "to_branch_id", "to_warehouse_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_transfers_despacho",
                schema: "inventory",
                table: "stock_transfers",
                sql: "(status IN ('Dispatched', 'Received')) = (dispatched_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_transfers_estado",
                schema: "inventory",
                table: "stock_transfers",
                sql: "status IN ('Pending', 'Dispatched', 'Received', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_transfers_recepcion",
                schema: "inventory",
                table: "stock_transfers",
                sql: "(status = 'Received') = (received_at IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_tenant_id_from_branch_id_to_br_b04b8098",
                schema: "inventory",
                table: "stock_transfer_lines",
                columns: new[] { "tenant_id", "from_branch_id", "to_branch_id", "stock_transfer_id" });

            migrationBuilder.CreateIndex(
                name: "ux_stock_transfer_lines_stock_transfer_id_variant_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                columns: new[] { "stock_transfer_id", "variant_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_transfer_lines_costo",
                schema: "inventory",
                table: "stock_transfer_lines",
                sql: "unit_cost IS NULL OR unit_cost >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_tenant_id_branch_id_pos_session_id",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "tenant_id", "branch_id", "pos_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_tenant_id_branch_id_sales_order_line_id",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "tenant_id", "branch_id", "sales_order_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_tenant_id_branch_id_stock_level_id",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "tenant_id", "branch_id", "stock_level_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_id_branch_id_stock_level_id",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "tenant_id", "branch_id", "stock_level_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_levels_tenant_id_branch_id_bin_id",
                schema: "inventory",
                table: "stock_levels",
                columns: new[] { "tenant_id", "branch_id", "bin_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustments_tenant_id_branch_id_warehouse_id",
                schema: "inventory",
                table: "stock_adjustments",
                columns: new[] { "tenant_id", "branch_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustment_lines_tenant_id_branch_id_stock_ad_62ee571f",
                schema: "inventory",
                table: "stock_adjustment_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_adjustment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustment_lines_tenant_id_branch_id_stock_level_id",
                schema: "inventory",
                table: "stock_adjustment_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_level_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustment_lines_tenant_id_branch_id_stock_movement_id",
                schema: "inventory",
                table: "stock_adjustment_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_movement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_shelves_tenant_id_branch_id_rack_id",
                schema: "warehouse",
                table: "shelves",
                columns: new[] { "tenant_id", "branch_id", "rack_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_tenant_id_active_branch_id",
                schema: "iam",
                table: "sessions",
                columns: new[] { "tenant_id", "active_branch_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sessions_token_hash",
                schema: "iam",
                table: "sessions",
                column: "token_hash",
                unique: true,
                filter: "token_hash IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sessions_token",
                schema: "iam",
                table: "sessions",
                sql: "(token_hash IS NULL) = (expires_at IS NULL) AND (token_hash IS NULL OR token_hash ~ '^[0-9a-f]{64}$')");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_tenant_id_branch_id_pos_session_id",
                schema: "sales",
                table: "sales_orders",
                columns: new[] { "tenant_id", "branch_id", "pos_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_tenant_id_branch_id_warehouse_id",
                schema: "sales",
                table: "sales_orders",
                columns: new[] { "tenant_id", "branch_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_lines_tenant_id_branch_id_sales_order_id",
                schema: "sales",
                table: "sales_order_lines",
                columns: new[] { "tenant_id", "branch_id", "sales_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_lines_tenant_id_branch_id_stock_movement_id",
                schema: "sales",
                table: "sales_order_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_movement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_racks_tenant_id_branch_id_aisle_id",
                schema: "warehouse",
                table: "racks",
                columns: new[] { "tenant_id", "branch_id", "aisle_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_return_lines_tenant_id_branch_id_goods_rec_5503cf33",
                schema: "purchasing",
                table: "purchase_return_lines",
                columns: new[] { "tenant_id", "branch_id", "goods_receipt_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_return_lines_tenant_id_branch_id_purchase_return_id",
                schema: "purchasing",
                table: "purchase_return_lines",
                columns: new[] { "tenant_id", "branch_id", "purchase_return_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_return_lines_tenant_id_branch_id_stock_level_id",
                schema: "purchasing",
                table: "purchase_return_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_level_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_return_lines_tenant_id_branch_id_stock_movement_id",
                schema: "purchasing",
                table: "purchase_return_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_movement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_tenant_id_branch_id_warehouse_id",
                schema: "purchasing",
                table: "purchase_orders",
                columns: new[] { "tenant_id", "branch_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_tenant_id_branch_id_purchase_order_id",
                schema: "purchasing",
                table: "purchase_order_lines",
                columns: new[] { "tenant_id", "branch_id", "purchase_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_stock_policies_tenant_id_branch_id_warehouse_id",
                schema: "catalog",
                table: "product_stock_policies",
                columns: new[] { "tenant_id", "branch_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pos_sessions_tenant_id_branch_id_pos_register_id",
                schema: "sales",
                table: "pos_sessions",
                columns: new[] { "tenant_id", "branch_id", "pos_register_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pos_registers_tenant_id_branch_id_warehouse_id",
                schema: "sales",
                table: "pos_registers",
                columns: new[] { "tenant_id", "branch_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_physical_counts_tenant_id_branch_id_warehouse_id",
                schema: "inventory",
                table: "physical_counts",
                columns: new[] { "tenant_id", "branch_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_physical_count_lines_tenant_id_branch_id_physical_count_id",
                schema: "inventory",
                table: "physical_count_lines",
                columns: new[] { "tenant_id", "branch_id", "physical_count_id" });

            migrationBuilder.CreateIndex(
                name: "ix_physical_count_lines_tenant_id_branch_id_stock_level_id",
                schema: "inventory",
                table: "physical_count_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_level_id" });

            migrationBuilder.CreateIndex(
                name: "ix_physical_count_lines_tenant_id_branch_id_stock_movement_id",
                schema: "inventory",
                table: "physical_count_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_movement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_branch_id_invoice_id",
                schema: "sales",
                table: "payments",
                columns: new[] { "tenant_id", "branch_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_branch_id_pos_session_id",
                schema: "sales",
                table: "payments",
                columns: new[] { "tenant_id", "branch_id", "pos_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_tenant_id_branch_id_journal_entry_id",
                schema: "accounting",
                table: "journal_lines",
                columns: new[] { "tenant_id", "branch_id", "journal_entry_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_id_branch_id_sales_order_id",
                schema: "sales",
                table: "invoices",
                columns: new[] { "tenant_id", "branch_id", "sales_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_tenant_id_branch_id_invoice_id",
                schema: "sales",
                table: "invoice_lines",
                columns: new[] { "tenant_id", "branch_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_tenant_id_branch_id_sales_order_line_id",
                schema: "sales",
                table: "invoice_lines",
                columns: new[] { "tenant_id", "branch_id", "sales_order_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_tenant_id_branch_id_purchase_order_id",
                schema: "purchasing",
                table: "goods_receipts",
                columns: new[] { "tenant_id", "branch_id", "purchase_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_tenant_id_branch_id_warehouse_id",
                schema: "purchasing",
                table: "goods_receipts",
                columns: new[] { "tenant_id", "branch_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_tenant_id_branch_id_goods_receipt_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                columns: new[] { "tenant_id", "branch_id", "goods_receipt_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_tenant_id_branch_id_purchase_or_92e74ade",
                schema: "purchasing",
                table: "goods_receipt_lines",
                columns: new[] { "tenant_id", "branch_id", "purchase_order_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_tenant_id_branch_id_stock_level_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_level_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_tenant_id_branch_id_stock_movement_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_movement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_movements_tenant_id_branch_id_pos_session_id",
                schema: "sales",
                table: "cash_movements",
                columns: new[] { "tenant_id", "branch_id", "pos_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bins_tenant_id_branch_id_shelf_id",
                schema: "warehouse",
                table: "bins",
                columns: new[] { "tenant_id", "branch_id", "shelf_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bin_assignments_tenant_id_branch_id_bin_id",
                schema: "warehouse",
                table: "bin_assignments",
                columns: new[] { "tenant_id", "branch_id", "bin_id" });

            migrationBuilder.CreateIndex(
                name: "ix_average_cost_history_tenant_id_branch_id_stock_movement_id",
                schema: "accounting",
                table: "average_cost_history",
                columns: new[] { "tenant_id", "branch_id", "stock_movement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_average_cost_history_tenant_id_branch_id_warehouse_id",
                schema: "accounting",
                table: "average_cost_history",
                columns: new[] { "tenant_id", "branch_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ux_average_cost_history_tenant_id_variant_id_warehouse_f2e7f7b5",
                schema: "accounting",
                table: "average_cost_history",
                columns: new[] { "tenant_id", "variant_id", "warehouse_id", "sequence" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_average_cost_history_secuencia",
                schema: "accounting",
                table: "average_cost_history",
                sql: "sequence >= 1");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_tenant_id_api_key_id_occurred_at",
                schema: "iam",
                table: "audit_logs",
                columns: new[] { "tenant_id", "api_key_id", "occurred_at" },
                filter: "api_key_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_tenant_id_branch_id",
                schema: "iam",
                table: "audit_logs",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_logs_canal",
                schema: "iam",
                table: "audit_logs",
                sql: "channel IS NULL OR channel IN ('desktop', 'cloud', 'api')");

            migrationBuilder.CreateIndex(
                name: "ix_aisles_tenant_id_branch_id_zone_id",
                schema: "warehouse",
                table: "aisles",
                columns: new[] { "tenant_id", "branch_id", "zone_id" });

            migrationBuilder.CreateIndex(
                name: "ix_api_key_scopes_tenant_id_api_key_id",
                schema: "integration",
                table: "api_key_scopes",
                columns: new[] { "tenant_id", "api_key_id" });

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_tenant_id_branch_id",
                schema: "integration",
                table: "api_keys",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_tenant_id_name",
                schema: "integration",
                table: "api_keys",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_tenant_id_owner_user_id",
                schema: "integration",
                table: "api_keys",
                columns: new[] { "tenant_id", "owner_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_api_keys_prefix",
                schema: "integration",
                table: "api_keys",
                column: "prefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_orders_tenant_id_branch_id_sales_order_id",
                schema: "sales",
                table: "external_orders",
                columns: new[] { "tenant_id", "branch_id", "sales_order_id" });

            migrationBuilder.CreateIndex(
                name: "ux_external_orders_sales_order_id",
                schema: "sales",
                table: "external_orders",
                column: "sales_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_external_orders_tenant_id_channel_external_id",
                schema: "sales",
                table: "external_orders",
                columns: new[] { "tenant_id", "channel", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_dispatch_next_attempt_at",
                schema: "integration",
                table: "outbox_dispatch",
                column: "next_attempt_at",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ux_outbox_dispatch_tenant_id_outbox_event_id",
                schema: "integration",
                table: "outbox_dispatch",
                columns: new[] { "tenant_id", "outbox_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_events_tenant_id_branch_id",
                schema: "integration",
                table: "outbox_events",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_events_tenant_id_occurred_at",
                schema: "integration",
                table: "outbox_events",
                columns: new[] { "tenant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_processed_requests_processed_at",
                schema: "iam",
                table: "processed_requests",
                column: "processed_at");

            migrationBuilder.CreateIndex(
                name: "ix_processed_requests_tenant_id_user_id",
                schema: "iam",
                table: "processed_requests",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_processed_requests_tenant_id_request_id",
                schema: "iam",
                table: "processed_requests",
                columns: new[] { "tenant_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_discrepancies_tenant_id_from_branch__ab9c7114",
                schema: "inventory",
                table: "stock_transfer_discrepancies",
                columns: new[] { "tenant_id", "from_branch_id", "to_branch_id", "transfer_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_discrepancies_tenant_id_recorded_by_user_id",
                schema: "inventory",
                table: "stock_transfer_discrepancies",
                columns: new[] { "tenant_id", "recorded_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_events_tenant_id_from_branch_id_to_b_ed8553a1",
                schema: "inventory",
                table: "stock_transfer_events",
                columns: new[] { "tenant_id", "from_branch_id", "to_branch_id", "transfer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_events_tenant_id_user_id",
                schema: "inventory",
                table: "stock_transfer_events",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_events_transfer_id_occurred_at",
                schema: "inventory",
                table: "stock_transfer_events",
                columns: new[] { "transfer_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_line_batches_tenant_id_batch_id",
                schema: "inventory",
                table: "stock_transfer_line_batches",
                columns: new[] { "tenant_id", "batch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_line_batches_tenant_id_from_branch_i_d73f9652",
                schema: "inventory",
                table: "stock_transfer_line_batches",
                columns: new[] { "tenant_id", "from_branch_id", "to_branch_id", "transfer_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_movements_tenant_id_branch_id_stock__76276875",
                schema: "inventory",
                table: "stock_transfer_movements",
                columns: new[] { "tenant_id", "branch_id", "stock_movement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_movements_tenant_id_transfer_line_id",
                schema: "inventory",
                table: "stock_transfer_movements",
                columns: new[] { "tenant_id", "transfer_line_id" });

            migrationBuilder.CreateIndex(
                name: "ux_stock_transfer_movements_stock_movement_id",
                schema: "inventory",
                table: "stock_transfer_movements",
                column: "stock_movement_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_tenant_id_endpoint_id",
                schema: "integration",
                table: "webhook_deliveries",
                columns: new[] { "tenant_id", "endpoint_id" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_tenant_id_outbox_event_id",
                schema: "integration",
                table: "webhook_deliveries",
                columns: new[] { "tenant_id", "outbox_event_id" });

            migrationBuilder.CreateIndex(
                name: "ux_webhook_deliveries_outbox_event_id_endpoint_id_attempt",
                schema: "integration",
                table: "webhook_deliveries",
                columns: new[] { "outbox_event_id", "endpoint_id", "attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoint_events_tenant_id_endpoint_id",
                schema: "integration",
                table: "webhook_endpoint_events",
                columns: new[] { "tenant_id", "endpoint_id" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoints_tenant_id_api_key_id",
                schema: "integration",
                table: "webhook_endpoints",
                columns: new[] { "tenant_id", "api_key_id" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoints_tenant_id_branch_id",
                schema: "integration",
                table: "webhook_endpoints",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoints_tenant_id_created_by_user_id",
                schema: "integration",
                table: "webhook_endpoints",
                columns: new[] { "tenant_id", "created_by_user_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_aisles_tenant_id_branch_id_zone_id",
                schema: "warehouse",
                table: "aisles",
                columns: new[] { "tenant_id", "branch_id", "zone_id" },
                principalSchema: "warehouse",
                principalTable: "zones",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_audit_logs_tenant_id_api_key_id",
                schema: "iam",
                table: "audit_logs",
                columns: new[] { "tenant_id", "api_key_id" },
                principalSchema: "integration",
                principalTable: "api_keys",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_audit_logs_tenant_id_branch_id",
                schema: "iam",
                table: "audit_logs",
                columns: new[] { "tenant_id", "branch_id" },
                principalSchema: "warehouse",
                principalTable: "branches",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_average_cost_history_tenant_id_branch_id_stock_movement_id",
                schema: "accounting",
                table: "average_cost_history",
                columns: new[] { "tenant_id", "branch_id", "stock_movement_id" },
                principalSchema: "inventory",
                principalTable: "stock_movements",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_average_cost_history_tenant_id_branch_id_warehouse_id",
                schema: "accounting",
                table: "average_cost_history",
                columns: new[] { "tenant_id", "branch_id", "warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_bin_assignments_tenant_id_branch_id_bin_id",
                schema: "warehouse",
                table: "bin_assignments",
                columns: new[] { "tenant_id", "branch_id", "bin_id" },
                principalSchema: "warehouse",
                principalTable: "bins",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_bins_tenant_id_branch_id_shelf_id",
                schema: "warehouse",
                table: "bins",
                columns: new[] { "tenant_id", "branch_id", "shelf_id" },
                principalSchema: "warehouse",
                principalTable: "shelves",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_cash_movements_tenant_id_branch_id_pos_session_id",
                schema: "sales",
                table: "cash_movements",
                columns: new[] { "tenant_id", "branch_id", "pos_session_id" },
                principalSchema: "sales",
                principalTable: "pos_sessions",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipt_lines_tenant_id_branch_id_goods_receipt_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                columns: new[] { "tenant_id", "branch_id", "goods_receipt_id" },
                principalSchema: "purchasing",
                principalTable: "goods_receipts",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipt_lines_tenant_id_branch_id_purchase_or_a9a4baae",
                schema: "purchasing",
                table: "goods_receipt_lines",
                columns: new[] { "tenant_id", "branch_id", "purchase_order_line_id" },
                principalSchema: "purchasing",
                principalTable: "purchase_order_lines",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipt_lines_tenant_id_branch_id_stock_level_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_level_id" },
                principalSchema: "inventory",
                principalTable: "stock_levels",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipt_lines_tenant_id_branch_id_stock_movement_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_movement_id" },
                principalSchema: "inventory",
                principalTable: "stock_movements",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipts_tenant_id_branch_id_purchase_order_id",
                schema: "purchasing",
                table: "goods_receipts",
                columns: new[] { "tenant_id", "branch_id", "purchase_order_id" },
                principalSchema: "purchasing",
                principalTable: "purchase_orders",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipts_tenant_id_branch_id_warehouse_id",
                schema: "purchasing",
                table: "goods_receipts",
                columns: new[] { "tenant_id", "branch_id", "warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_invoice_lines_tenant_id_branch_id_invoice_id",
                schema: "sales",
                table: "invoice_lines",
                columns: new[] { "tenant_id", "branch_id", "invoice_id" },
                principalSchema: "sales",
                principalTable: "invoices",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_invoice_lines_tenant_id_branch_id_sales_order_line_id",
                schema: "sales",
                table: "invoice_lines",
                columns: new[] { "tenant_id", "branch_id", "sales_order_line_id" },
                principalSchema: "sales",
                principalTable: "sales_order_lines",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_invoices_tenant_id_branch_id_sales_order_id",
                schema: "sales",
                table: "invoices",
                columns: new[] { "tenant_id", "branch_id", "sales_order_id" },
                principalSchema: "sales",
                principalTable: "sales_orders",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_journal_lines_tenant_id_branch_id_journal_entry_id",
                schema: "accounting",
                table: "journal_lines",
                columns: new[] { "tenant_id", "branch_id", "journal_entry_id" },
                principalSchema: "accounting",
                principalTable: "journal_entries",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_payments_tenant_id_branch_id_invoice_id",
                schema: "sales",
                table: "payments",
                columns: new[] { "tenant_id", "branch_id", "invoice_id" },
                principalSchema: "sales",
                principalTable: "invoices",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_payments_tenant_id_branch_id_pos_session_id",
                schema: "sales",
                table: "payments",
                columns: new[] { "tenant_id", "branch_id", "pos_session_id" },
                principalSchema: "sales",
                principalTable: "pos_sessions",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_physical_count_lines_tenant_id_branch_id_physical_count_id",
                schema: "inventory",
                table: "physical_count_lines",
                columns: new[] { "tenant_id", "branch_id", "physical_count_id" },
                principalSchema: "inventory",
                principalTable: "physical_counts",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_physical_count_lines_tenant_id_branch_id_stock_level_id",
                schema: "inventory",
                table: "physical_count_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_level_id" },
                principalSchema: "inventory",
                principalTable: "stock_levels",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_physical_count_lines_tenant_id_branch_id_stock_movement_id",
                schema: "inventory",
                table: "physical_count_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_movement_id" },
                principalSchema: "inventory",
                principalTable: "stock_movements",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_physical_counts_tenant_id_branch_id_warehouse_id",
                schema: "inventory",
                table: "physical_counts",
                columns: new[] { "tenant_id", "branch_id", "warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_pos_registers_tenant_id_branch_id_warehouse_id",
                schema: "sales",
                table: "pos_registers",
                columns: new[] { "tenant_id", "branch_id", "warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_pos_sessions_tenant_id_branch_id_pos_register_id",
                schema: "sales",
                table: "pos_sessions",
                columns: new[] { "tenant_id", "branch_id", "pos_register_id" },
                principalSchema: "sales",
                principalTable: "pos_registers",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_product_stock_policies_tenant_id_branch_id_warehouse_id",
                schema: "catalog",
                table: "product_stock_policies",
                columns: new[] { "tenant_id", "branch_id", "warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_order_lines_tenant_id_branch_id_purchase_order_id",
                schema: "purchasing",
                table: "purchase_order_lines",
                columns: new[] { "tenant_id", "branch_id", "purchase_order_id" },
                principalSchema: "purchasing",
                principalTable: "purchase_orders",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_orders_tenant_id_branch_id_warehouse_id",
                schema: "purchasing",
                table: "purchase_orders",
                columns: new[] { "tenant_id", "branch_id", "warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_return_lines_tenant_id_branch_id_goods_rec_79ef70b8",
                schema: "purchasing",
                table: "purchase_return_lines",
                columns: new[] { "tenant_id", "branch_id", "goods_receipt_line_id" },
                principalSchema: "purchasing",
                principalTable: "goods_receipt_lines",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_return_lines_tenant_id_branch_id_purchase_return_id",
                schema: "purchasing",
                table: "purchase_return_lines",
                columns: new[] { "tenant_id", "branch_id", "purchase_return_id" },
                principalSchema: "purchasing",
                principalTable: "purchase_returns",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_return_lines_tenant_id_branch_id_stock_level_id",
                schema: "purchasing",
                table: "purchase_return_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_level_id" },
                principalSchema: "inventory",
                principalTable: "stock_levels",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_return_lines_tenant_id_branch_id_stock_movement_id",
                schema: "purchasing",
                table: "purchase_return_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_movement_id" },
                principalSchema: "inventory",
                principalTable: "stock_movements",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_racks_tenant_id_branch_id_aisle_id",
                schema: "warehouse",
                table: "racks",
                columns: new[] { "tenant_id", "branch_id", "aisle_id" },
                principalSchema: "warehouse",
                principalTable: "aisles",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_order_lines_tenant_id_branch_id_sales_order_id",
                schema: "sales",
                table: "sales_order_lines",
                columns: new[] { "tenant_id", "branch_id", "sales_order_id" },
                principalSchema: "sales",
                principalTable: "sales_orders",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_order_lines_tenant_id_branch_id_stock_movement_id",
                schema: "sales",
                table: "sales_order_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_movement_id" },
                principalSchema: "inventory",
                principalTable: "stock_movements",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_orders_tenant_id_branch_id_pos_session_id",
                schema: "sales",
                table: "sales_orders",
                columns: new[] { "tenant_id", "branch_id", "pos_session_id" },
                principalSchema: "sales",
                principalTable: "pos_sessions",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_orders_tenant_id_branch_id_warehouse_id",
                schema: "sales",
                table: "sales_orders",
                columns: new[] { "tenant_id", "branch_id", "warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sessions_tenant_id_active_branch_id",
                schema: "iam",
                table: "sessions",
                columns: new[] { "tenant_id", "active_branch_id" },
                principalSchema: "warehouse",
                principalTable: "branches",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_shelves_tenant_id_branch_id_rack_id",
                schema: "warehouse",
                table: "shelves",
                columns: new[] { "tenant_id", "branch_id", "rack_id" },
                principalSchema: "warehouse",
                principalTable: "racks",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_adjustment_lines_tenant_id_branch_id_stock_ad_202e465f",
                schema: "inventory",
                table: "stock_adjustment_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_adjustment_id" },
                principalSchema: "inventory",
                principalTable: "stock_adjustments",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_adjustment_lines_tenant_id_branch_id_stock_level_id",
                schema: "inventory",
                table: "stock_adjustment_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_level_id" },
                principalSchema: "inventory",
                principalTable: "stock_levels",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_adjustment_lines_tenant_id_branch_id_stock_movement_id",
                schema: "inventory",
                table: "stock_adjustment_lines",
                columns: new[] { "tenant_id", "branch_id", "stock_movement_id" },
                principalSchema: "inventory",
                principalTable: "stock_movements",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_adjustments_tenant_id_branch_id_warehouse_id",
                schema: "inventory",
                table: "stock_adjustments",
                columns: new[] { "tenant_id", "branch_id", "warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_levels_tenant_id_branch_id_bin_id",
                schema: "inventory",
                table: "stock_levels",
                columns: new[] { "tenant_id", "branch_id", "bin_id" },
                principalSchema: "warehouse",
                principalTable: "bins",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_movements_tenant_id_branch_id_stock_level_id",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "tenant_id", "branch_id", "stock_level_id" },
                principalSchema: "inventory",
                principalTable: "stock_levels",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_reservations_tenant_id_branch_id_pos_session_id",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "tenant_id", "branch_id", "pos_session_id" },
                principalSchema: "sales",
                principalTable: "pos_sessions",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_reservations_tenant_id_branch_id_sales_order_line_id",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "tenant_id", "branch_id", "sales_order_line_id" },
                principalSchema: "sales",
                principalTable: "sales_order_lines",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_reservations_tenant_id_branch_id_stock_level_id",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "tenant_id", "branch_id", "stock_level_id" },
                principalSchema: "inventory",
                principalTable: "stock_levels",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_transfer_lines_tenant_id_from_branch_id_to_br_8ab834cf",
                schema: "inventory",
                table: "stock_transfer_lines",
                columns: new[] { "tenant_id", "from_branch_id", "to_branch_id", "stock_transfer_id" },
                principalSchema: "inventory",
                principalTable: "stock_transfers",
                principalColumns: new[] { "tenant_id", "from_branch_id", "to_branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_transfer_lines_tenant_id_variant_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                columns: new[] { "tenant_id", "variant_id" },
                principalSchema: "catalog",
                principalTable: "product_variants",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_transfers_tenant_id_from_branch_id_from_warehouse_id",
                schema: "inventory",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "from_branch_id", "from_warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_transfers_tenant_id_requested_by_user_id",
                schema: "inventory",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "requested_by_user_id" },
                principalSchema: "iam",
                principalTable: "users",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_transfers_tenant_id_to_branch_id_to_warehouse_id",
                schema: "inventory",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "to_branch_id", "to_warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_supplier_invoice_lines_tenant_id_branch_id_goods_re_507ecc30",
                schema: "purchasing",
                table: "supplier_invoice_lines",
                columns: new[] { "tenant_id", "branch_id", "goods_receipt_line_id" },
                principalSchema: "purchasing",
                principalTable: "goods_receipt_lines",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_supplier_invoice_lines_tenant_id_branch_id_supplier_47722964",
                schema: "purchasing",
                table: "supplier_invoice_lines",
                columns: new[] { "tenant_id", "branch_id", "supplier_invoice_id" },
                principalSchema: "purchasing",
                principalTable: "supplier_invoices",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_zones_tenant_id_branch_id_warehouse_id",
                schema: "warehouse",
                table: "zones",
                columns: new[] { "tenant_id", "branch_id", "warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "branch_id", "id" },
                onDelete: ReferentialAction.Restrict);

            V4Guards(migrationBuilder);
            V4Analyze(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            V4DropGuards(migrationBuilder);

            migrationBuilder.DropForeignKey(
                name: "fk_aisles_tenant_id_branch_id_zone_id",
                schema: "warehouse",
                table: "aisles");

            migrationBuilder.DropForeignKey(
                name: "fk_audit_logs_tenant_id_api_key_id",
                schema: "iam",
                table: "audit_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_audit_logs_tenant_id_branch_id",
                schema: "iam",
                table: "audit_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_average_cost_history_tenant_id_branch_id_stock_movement_id",
                schema: "accounting",
                table: "average_cost_history");

            migrationBuilder.DropForeignKey(
                name: "fk_average_cost_history_tenant_id_branch_id_warehouse_id",
                schema: "accounting",
                table: "average_cost_history");

            migrationBuilder.DropForeignKey(
                name: "fk_bin_assignments_tenant_id_branch_id_bin_id",
                schema: "warehouse",
                table: "bin_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_bins_tenant_id_branch_id_shelf_id",
                schema: "warehouse",
                table: "bins");

            migrationBuilder.DropForeignKey(
                name: "fk_cash_movements_tenant_id_branch_id_pos_session_id",
                schema: "sales",
                table: "cash_movements");

            migrationBuilder.DropForeignKey(
                name: "fk_goods_receipt_lines_tenant_id_branch_id_goods_receipt_id",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_goods_receipt_lines_tenant_id_branch_id_purchase_or_a9a4baae",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_goods_receipt_lines_tenant_id_branch_id_stock_level_id",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_goods_receipt_lines_tenant_id_branch_id_stock_movement_id",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_goods_receipts_tenant_id_branch_id_purchase_order_id",
                schema: "purchasing",
                table: "goods_receipts");

            migrationBuilder.DropForeignKey(
                name: "fk_goods_receipts_tenant_id_branch_id_warehouse_id",
                schema: "purchasing",
                table: "goods_receipts");

            migrationBuilder.DropForeignKey(
                name: "fk_invoice_lines_tenant_id_branch_id_invoice_id",
                schema: "sales",
                table: "invoice_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_invoice_lines_tenant_id_branch_id_sales_order_line_id",
                schema: "sales",
                table: "invoice_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_invoices_tenant_id_branch_id_sales_order_id",
                schema: "sales",
                table: "invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_journal_lines_tenant_id_branch_id_journal_entry_id",
                schema: "accounting",
                table: "journal_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_payments_tenant_id_branch_id_invoice_id",
                schema: "sales",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "fk_payments_tenant_id_branch_id_pos_session_id",
                schema: "sales",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "fk_physical_count_lines_tenant_id_branch_id_physical_count_id",
                schema: "inventory",
                table: "physical_count_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_physical_count_lines_tenant_id_branch_id_stock_level_id",
                schema: "inventory",
                table: "physical_count_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_physical_count_lines_tenant_id_branch_id_stock_movement_id",
                schema: "inventory",
                table: "physical_count_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_physical_counts_tenant_id_branch_id_warehouse_id",
                schema: "inventory",
                table: "physical_counts");

            migrationBuilder.DropForeignKey(
                name: "fk_pos_registers_tenant_id_branch_id_warehouse_id",
                schema: "sales",
                table: "pos_registers");

            migrationBuilder.DropForeignKey(
                name: "fk_pos_sessions_tenant_id_branch_id_pos_register_id",
                schema: "sales",
                table: "pos_sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_product_stock_policies_tenant_id_branch_id_warehouse_id",
                schema: "catalog",
                table: "product_stock_policies");

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_order_lines_tenant_id_branch_id_purchase_order_id",
                schema: "purchasing",
                table: "purchase_order_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_orders_tenant_id_branch_id_warehouse_id",
                schema: "purchasing",
                table: "purchase_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_return_lines_tenant_id_branch_id_goods_rec_79ef70b8",
                schema: "purchasing",
                table: "purchase_return_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_return_lines_tenant_id_branch_id_purchase_return_id",
                schema: "purchasing",
                table: "purchase_return_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_return_lines_tenant_id_branch_id_stock_level_id",
                schema: "purchasing",
                table: "purchase_return_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_return_lines_tenant_id_branch_id_stock_movement_id",
                schema: "purchasing",
                table: "purchase_return_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_racks_tenant_id_branch_id_aisle_id",
                schema: "warehouse",
                table: "racks");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_order_lines_tenant_id_branch_id_sales_order_id",
                schema: "sales",
                table: "sales_order_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_order_lines_tenant_id_branch_id_stock_movement_id",
                schema: "sales",
                table: "sales_order_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_orders_tenant_id_branch_id_pos_session_id",
                schema: "sales",
                table: "sales_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_orders_tenant_id_branch_id_warehouse_id",
                schema: "sales",
                table: "sales_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_sessions_tenant_id_active_branch_id",
                schema: "iam",
                table: "sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_shelves_tenant_id_branch_id_rack_id",
                schema: "warehouse",
                table: "shelves");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_adjustment_lines_tenant_id_branch_id_stock_ad_202e465f",
                schema: "inventory",
                table: "stock_adjustment_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_adjustment_lines_tenant_id_branch_id_stock_level_id",
                schema: "inventory",
                table: "stock_adjustment_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_adjustment_lines_tenant_id_branch_id_stock_movement_id",
                schema: "inventory",
                table: "stock_adjustment_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_adjustments_tenant_id_branch_id_warehouse_id",
                schema: "inventory",
                table: "stock_adjustments");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_levels_tenant_id_branch_id_bin_id",
                schema: "inventory",
                table: "stock_levels");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_movements_tenant_id_branch_id_stock_level_id",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_reservations_tenant_id_branch_id_pos_session_id",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_reservations_tenant_id_branch_id_sales_order_line_id",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_reservations_tenant_id_branch_id_stock_level_id",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_transfer_lines_tenant_id_from_branch_id_to_br_8ab834cf",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_transfer_lines_tenant_id_variant_id",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_transfers_tenant_id_from_branch_id_from_warehouse_id",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_transfers_tenant_id_requested_by_user_id",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_transfers_tenant_id_to_branch_id_to_warehouse_id",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropForeignKey(
                name: "fk_supplier_invoice_lines_tenant_id_branch_id_goods_re_507ecc30",
                schema: "purchasing",
                table: "supplier_invoice_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_supplier_invoice_lines_tenant_id_branch_id_supplier_47722964",
                schema: "purchasing",
                table: "supplier_invoice_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_zones_tenant_id_branch_id_warehouse_id",
                schema: "warehouse",
                table: "zones");

            migrationBuilder.DropTable(
                name: "api_key_scopes",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "external_orders",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "outbox_dispatch",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "processed_requests",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "stock_transfer_discrepancies",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_transfer_events",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_transfer_line_batches",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_transfer_movements",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "webhook_deliveries",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "webhook_endpoint_events",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "outbox_events",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "webhook_endpoints",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "api_keys",
                schema: "integration");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_zones_tenant_id_branch_id_id",
                schema: "warehouse",
                table: "zones");

            migrationBuilder.DropIndex(
                name: "ix_zones_tenant_id_branch_id_warehouse_id",
                schema: "warehouse",
                table: "zones");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_warehouses_tenant_id_branch_id_id",
                schema: "warehouse",
                table: "warehouses");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_supplier_invoices_tenant_id_branch_id_id",
                schema: "purchasing",
                table: "supplier_invoices");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_supplier_invoice_lines_tenant_id_branch_id_id",
                schema: "purchasing",
                table: "supplier_invoice_lines");

            migrationBuilder.DropIndex(
                name: "ix_supplier_invoice_lines_tenant_id_branch_id_goods_re_33e178bb",
                schema: "purchasing",
                table: "supplier_invoice_lines");

            migrationBuilder.DropIndex(
                name: "ix_supplier_invoice_lines_tenant_id_branch_id_supplier_4d17bc14",
                schema: "purchasing",
                table: "supplier_invoice_lines");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_stock_transfers_tenant_id_from_branch_id_to_branch_id_id",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "ix_stock_transfers_tenant_id_from_branch_id_from_warehouse_id",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "ix_stock_transfers_tenant_id_requested_by_user_id",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "ix_stock_transfers_tenant_id_status",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "ix_stock_transfers_tenant_id_to_branch_id_to_warehouse_id",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_transfers_despacho",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_transfers_estado",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_transfers_recepcion",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_stock_transfer_lines_tenant_id_from_branch_id_to_br_7655fbfd",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.DropIndex(
                name: "ix_stock_transfer_lines_tenant_id_from_branch_id_to_br_b04b8098",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.DropIndex(
                name: "ux_stock_transfer_lines_stock_transfer_id_variant_id",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_transfer_lines_costo",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_stock_reservations_tenant_id_branch_id_id",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropIndex(
                name: "ix_stock_reservations_tenant_id_branch_id_pos_session_id",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropIndex(
                name: "ix_stock_reservations_tenant_id_branch_id_sales_order_line_id",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropIndex(
                name: "ix_stock_reservations_tenant_id_branch_id_stock_level_id",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_stock_movements_tenant_id_branch_id_id",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "ix_stock_movements_tenant_id_branch_id_stock_level_id",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_stock_levels_tenant_id_branch_id_id",
                schema: "inventory",
                table: "stock_levels");

            migrationBuilder.DropIndex(
                name: "ix_stock_levels_tenant_id_branch_id_bin_id",
                schema: "inventory",
                table: "stock_levels");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_stock_adjustments_tenant_id_branch_id_id",
                schema: "inventory",
                table: "stock_adjustments");

            migrationBuilder.DropIndex(
                name: "ix_stock_adjustments_tenant_id_branch_id_warehouse_id",
                schema: "inventory",
                table: "stock_adjustments");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_stock_adjustment_lines_tenant_id_branch_id_id",
                schema: "inventory",
                table: "stock_adjustment_lines");

            migrationBuilder.DropIndex(
                name: "ix_stock_adjustment_lines_tenant_id_branch_id_stock_ad_62ee571f",
                schema: "inventory",
                table: "stock_adjustment_lines");

            migrationBuilder.DropIndex(
                name: "ix_stock_adjustment_lines_tenant_id_branch_id_stock_level_id",
                schema: "inventory",
                table: "stock_adjustment_lines");

            migrationBuilder.DropIndex(
                name: "ix_stock_adjustment_lines_tenant_id_branch_id_stock_movement_id",
                schema: "inventory",
                table: "stock_adjustment_lines");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_shelves_tenant_id_branch_id_id",
                schema: "warehouse",
                table: "shelves");

            migrationBuilder.DropIndex(
                name: "ix_shelves_tenant_id_branch_id_rack_id",
                schema: "warehouse",
                table: "shelves");

            migrationBuilder.DropIndex(
                name: "ix_sessions_tenant_id_active_branch_id",
                schema: "iam",
                table: "sessions");

            migrationBuilder.DropIndex(
                name: "ux_sessions_token_hash",
                schema: "iam",
                table: "sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sessions_token",
                schema: "iam",
                table: "sessions");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_sales_orders_tenant_id_branch_id_id",
                schema: "sales",
                table: "sales_orders");

            migrationBuilder.DropIndex(
                name: "ix_sales_orders_tenant_id_branch_id_pos_session_id",
                schema: "sales",
                table: "sales_orders");

            migrationBuilder.DropIndex(
                name: "ix_sales_orders_tenant_id_branch_id_warehouse_id",
                schema: "sales",
                table: "sales_orders");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_sales_order_lines_tenant_id_branch_id_id",
                schema: "sales",
                table: "sales_order_lines");

            migrationBuilder.DropIndex(
                name: "ix_sales_order_lines_tenant_id_branch_id_sales_order_id",
                schema: "sales",
                table: "sales_order_lines");

            migrationBuilder.DropIndex(
                name: "ix_sales_order_lines_tenant_id_branch_id_stock_movement_id",
                schema: "sales",
                table: "sales_order_lines");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_racks_tenant_id_branch_id_id",
                schema: "warehouse",
                table: "racks");

            migrationBuilder.DropIndex(
                name: "ix_racks_tenant_id_branch_id_aisle_id",
                schema: "warehouse",
                table: "racks");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_purchase_returns_tenant_id_branch_id_id",
                schema: "purchasing",
                table: "purchase_returns");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_purchase_return_lines_tenant_id_branch_id_id",
                schema: "purchasing",
                table: "purchase_return_lines");

            migrationBuilder.DropIndex(
                name: "ix_purchase_return_lines_tenant_id_branch_id_goods_rec_5503cf33",
                schema: "purchasing",
                table: "purchase_return_lines");

            migrationBuilder.DropIndex(
                name: "ix_purchase_return_lines_tenant_id_branch_id_purchase_return_id",
                schema: "purchasing",
                table: "purchase_return_lines");

            migrationBuilder.DropIndex(
                name: "ix_purchase_return_lines_tenant_id_branch_id_stock_level_id",
                schema: "purchasing",
                table: "purchase_return_lines");

            migrationBuilder.DropIndex(
                name: "ix_purchase_return_lines_tenant_id_branch_id_stock_movement_id",
                schema: "purchasing",
                table: "purchase_return_lines");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_purchase_orders_tenant_id_branch_id_id",
                schema: "purchasing",
                table: "purchase_orders");

            migrationBuilder.DropIndex(
                name: "ix_purchase_orders_tenant_id_branch_id_warehouse_id",
                schema: "purchasing",
                table: "purchase_orders");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_purchase_order_lines_tenant_id_branch_id_id",
                schema: "purchasing",
                table: "purchase_order_lines");

            migrationBuilder.DropIndex(
                name: "ix_purchase_order_lines_tenant_id_branch_id_purchase_order_id",
                schema: "purchasing",
                table: "purchase_order_lines");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_product_stock_policies_tenant_id_branch_id_id",
                schema: "catalog",
                table: "product_stock_policies");

            migrationBuilder.DropIndex(
                name: "ix_product_stock_policies_tenant_id_branch_id_warehouse_id",
                schema: "catalog",
                table: "product_stock_policies");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_pos_sessions_tenant_id_branch_id_id",
                schema: "sales",
                table: "pos_sessions");

            migrationBuilder.DropIndex(
                name: "ix_pos_sessions_tenant_id_branch_id_pos_register_id",
                schema: "sales",
                table: "pos_sessions");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_pos_registers_tenant_id_branch_id_id",
                schema: "sales",
                table: "pos_registers");

            migrationBuilder.DropIndex(
                name: "ix_pos_registers_tenant_id_branch_id_warehouse_id",
                schema: "sales",
                table: "pos_registers");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_physical_counts_tenant_id_branch_id_id",
                schema: "inventory",
                table: "physical_counts");

            migrationBuilder.DropIndex(
                name: "ix_physical_counts_tenant_id_branch_id_warehouse_id",
                schema: "inventory",
                table: "physical_counts");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_physical_count_lines_tenant_id_branch_id_id",
                schema: "inventory",
                table: "physical_count_lines");

            migrationBuilder.DropIndex(
                name: "ix_physical_count_lines_tenant_id_branch_id_physical_count_id",
                schema: "inventory",
                table: "physical_count_lines");

            migrationBuilder.DropIndex(
                name: "ix_physical_count_lines_tenant_id_branch_id_stock_level_id",
                schema: "inventory",
                table: "physical_count_lines");

            migrationBuilder.DropIndex(
                name: "ix_physical_count_lines_tenant_id_branch_id_stock_movement_id",
                schema: "inventory",
                table: "physical_count_lines");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_payments_tenant_id_branch_id_id",
                schema: "sales",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_payments_tenant_id_branch_id_invoice_id",
                schema: "sales",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_payments_tenant_id_branch_id_pos_session_id",
                schema: "sales",
                table: "payments");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_journal_lines_tenant_id_branch_id_id",
                schema: "accounting",
                table: "journal_lines");

            migrationBuilder.DropIndex(
                name: "ix_journal_lines_tenant_id_branch_id_journal_entry_id",
                schema: "accounting",
                table: "journal_lines");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_journal_entries_tenant_id_branch_id_id",
                schema: "accounting",
                table: "journal_entries");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_invoices_tenant_id_branch_id_id",
                schema: "sales",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "ix_invoices_tenant_id_branch_id_sales_order_id",
                schema: "sales",
                table: "invoices");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_invoice_lines_tenant_id_branch_id_id",
                schema: "sales",
                table: "invoice_lines");

            migrationBuilder.DropIndex(
                name: "ix_invoice_lines_tenant_id_branch_id_invoice_id",
                schema: "sales",
                table: "invoice_lines");

            migrationBuilder.DropIndex(
                name: "ix_invoice_lines_tenant_id_branch_id_sales_order_line_id",
                schema: "sales",
                table: "invoice_lines");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_goods_receipts_tenant_id_branch_id_id",
                schema: "purchasing",
                table: "goods_receipts");

            migrationBuilder.DropIndex(
                name: "ix_goods_receipts_tenant_id_branch_id_purchase_order_id",
                schema: "purchasing",
                table: "goods_receipts");

            migrationBuilder.DropIndex(
                name: "ix_goods_receipts_tenant_id_branch_id_warehouse_id",
                schema: "purchasing",
                table: "goods_receipts");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_goods_receipt_lines_tenant_id_branch_id_id",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropIndex(
                name: "ix_goods_receipt_lines_tenant_id_branch_id_goods_receipt_id",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropIndex(
                name: "ix_goods_receipt_lines_tenant_id_branch_id_purchase_or_92e74ade",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropIndex(
                name: "ix_goods_receipt_lines_tenant_id_branch_id_stock_level_id",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropIndex(
                name: "ix_goods_receipt_lines_tenant_id_branch_id_stock_movement_id",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_cash_movements_tenant_id_branch_id_id",
                schema: "sales",
                table: "cash_movements");

            migrationBuilder.DropIndex(
                name: "ix_cash_movements_tenant_id_branch_id_pos_session_id",
                schema: "sales",
                table: "cash_movements");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_bins_tenant_id_branch_id_id",
                schema: "warehouse",
                table: "bins");

            migrationBuilder.DropIndex(
                name: "ix_bins_tenant_id_branch_id_shelf_id",
                schema: "warehouse",
                table: "bins");

            migrationBuilder.DropIndex(
                name: "ix_bin_assignments_tenant_id_branch_id_bin_id",
                schema: "warehouse",
                table: "bin_assignments");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_average_cost_history_tenant_id_branch_id_id",
                schema: "accounting",
                table: "average_cost_history");

            migrationBuilder.DropIndex(
                name: "ix_average_cost_history_tenant_id_branch_id_stock_movement_id",
                schema: "accounting",
                table: "average_cost_history");

            migrationBuilder.DropIndex(
                name: "ix_average_cost_history_tenant_id_branch_id_warehouse_id",
                schema: "accounting",
                table: "average_cost_history");

            migrationBuilder.DropIndex(
                name: "ux_average_cost_history_tenant_id_variant_id_warehouse_f2e7f7b5",
                schema: "accounting",
                table: "average_cost_history");

            migrationBuilder.DropCheckConstraint(
                name: "ck_average_cost_history_secuencia",
                schema: "accounting",
                table: "average_cost_history");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_tenant_id_api_key_id_occurred_at",
                schema: "iam",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_tenant_id_branch_id",
                schema: "iam",
                table: "audit_logs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_logs_canal",
                schema: "iam",
                table: "audit_logs");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_aisles_tenant_id_branch_id_id",
                schema: "warehouse",
                table: "aisles");

            migrationBuilder.DropIndex(
                name: "ix_aisles_tenant_id_branch_id_zone_id",
                schema: "warehouse",
                table: "aisles");

            migrationBuilder.DeleteData(
                schema: "iam",
                table: "modules",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000006"));

            migrationBuilder.DeleteData(
                schema: "iam",
                table: "modules",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000007"));

            migrationBuilder.DeleteData(
                schema: "iam",
                table: "modules",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000008"));

            migrationBuilder.DeleteData(
                schema: "iam",
                table: "modules",
                keyColumn: "id",
                keyValue: new Guid("01920000-0000-7000-8000-000000000009"));

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "warehouse",
                table: "zones");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "purchasing",
                table: "supplier_invoices");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "purchasing",
                table: "supplier_invoice_lines");

            migrationBuilder.DropColumn(
                name: "from_branch_id",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "requested_at",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "requested_by_user_id",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "to_branch_id",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "from_branch_id",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.DropColumn(
                name: "to_branch_id",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.DropColumn(
                name: "unit_cost",
                schema: "inventory",
                table: "stock_transfer_lines");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "inventory",
                table: "stock_levels");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "inventory",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "inventory",
                table: "stock_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "warehouse",
                table: "shelves");

            migrationBuilder.DropColumn(
                name: "active_branch_id",
                schema: "iam",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "expires_at",
                schema: "iam",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "token_hash",
                schema: "iam",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "sales",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "sales",
                table: "sales_order_lines");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "warehouse",
                table: "racks");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "purchasing",
                table: "purchase_returns");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "purchasing",
                table: "purchase_return_lines");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "purchasing",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "purchasing",
                table: "purchase_order_lines");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "catalog",
                table: "product_stock_policies");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "sales",
                table: "pos_sessions");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "sales",
                table: "pos_registers");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "inventory",
                table: "physical_counts");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "inventory",
                table: "physical_count_lines");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "sales",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "accounting",
                table: "journal_lines");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "accounting",
                table: "journal_entries");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "sales",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "sales",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "purchasing",
                table: "goods_receipts");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "purchasing",
                table: "goods_receipt_lines");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "sales",
                table: "cash_movements");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "warehouse",
                table: "bins");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "warehouse",
                table: "bin_assignments");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "accounting",
                table: "average_cost_history");

            migrationBuilder.DropColumn(
                name: "sequence",
                schema: "accounting",
                table: "average_cost_history");

            migrationBuilder.DropColumn(
                name: "api_key_id",
                schema: "iam",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "iam",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "channel",
                schema: "iam",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "warehouse",
                table: "aisles");

            migrationBuilder.RenameColumn(
                name: "dispatched_at",
                schema: "inventory",
                table: "stock_transfers",
                newName: "shipped_at");

            migrationBuilder.RenameColumn(
                name: "variant_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                newName: "source_stock_level_id");

            migrationBuilder.RenameIndex(
                name: "ix_stock_transfer_lines_tenant_id_variant_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                newName: "ix_stock_transfer_lines_tenant_id_source_stock_level_id");

            migrationBuilder.AddColumn<Guid>(
                name: "destination_stock_level_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "inbound_movement_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "outbound_movement_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_zones_tenant_id_warehouse_id",
                schema: "warehouse",
                table: "zones",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_tenant_id_branch_id",
                schema: "warehouse",
                table: "warehouses",
                columns: new[] { "tenant_id", "branch_id" });

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
                name: "ix_stock_transfer_lines_tenant_id_stock_transfer_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                columns: new[] { "tenant_id", "stock_transfer_id" });

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
                name: "ix_stock_movements_tenant_id_stock_level_id",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "tenant_id", "stock_level_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_levels_tenant_id_bin_id",
                schema: "inventory",
                table: "stock_levels",
                columns: new[] { "tenant_id", "bin_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustments_tenant_id_warehouse_id",
                schema: "inventory",
                table: "stock_adjustments",
                columns: new[] { "tenant_id", "warehouse_id" });

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
                name: "ix_shelves_tenant_id_rack_id",
                schema: "warehouse",
                table: "shelves",
                columns: new[] { "tenant_id", "rack_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_tenant_id_pos_session_id",
                schema: "sales",
                table: "sales_orders",
                columns: new[] { "tenant_id", "pos_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_tenant_id_warehouse_id",
                schema: "sales",
                table: "sales_orders",
                columns: new[] { "tenant_id", "warehouse_id" });

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
                name: "ix_racks_tenant_id_aisle_id",
                schema: "warehouse",
                table: "racks",
                columns: new[] { "tenant_id", "aisle_id" });

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
                name: "ix_purchase_orders_tenant_id_warehouse_id",
                schema: "purchasing",
                table: "purchase_orders",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_tenant_id_purchase_order_id",
                schema: "purchasing",
                table: "purchase_order_lines",
                columns: new[] { "tenant_id", "purchase_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_stock_policies_tenant_id_warehouse_id",
                schema: "catalog",
                table: "product_stock_policies",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pos_sessions_tenant_id_pos_register_id",
                schema: "sales",
                table: "pos_sessions",
                columns: new[] { "tenant_id", "pos_register_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pos_registers_tenant_id_warehouse_id",
                schema: "sales",
                table: "pos_registers",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_physical_counts_tenant_id_warehouse_id",
                schema: "inventory",
                table: "physical_counts",
                columns: new[] { "tenant_id", "warehouse_id" });

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
                name: "ix_payments_tenant_id_invoice_id",
                schema: "sales",
                table: "payments",
                columns: new[] { "tenant_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_pos_session_id",
                schema: "sales",
                table: "payments",
                columns: new[] { "tenant_id", "pos_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_tenant_id_journal_entry_id",
                schema: "accounting",
                table: "journal_lines",
                columns: new[] { "tenant_id", "journal_entry_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_id_sales_order_id",
                schema: "sales",
                table: "invoices",
                columns: new[] { "tenant_id", "sales_order_id" });

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
                name: "ix_goods_receipts_tenant_id_purchase_order_id",
                schema: "purchasing",
                table: "goods_receipts",
                columns: new[] { "tenant_id", "purchase_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_tenant_id_warehouse_id",
                schema: "purchasing",
                table: "goods_receipts",
                columns: new[] { "tenant_id", "warehouse_id" });

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
                name: "ix_cash_movements_tenant_id_pos_session_id",
                schema: "sales",
                table: "cash_movements",
                columns: new[] { "tenant_id", "pos_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bins_tenant_id_shelf_id",
                schema: "warehouse",
                table: "bins",
                columns: new[] { "tenant_id", "shelf_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bin_assignments_tenant_id_bin_id",
                schema: "warehouse",
                table: "bin_assignments",
                columns: new[] { "tenant_id", "bin_id" });

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
                name: "ix_aisles_tenant_id_zone_id",
                schema: "warehouse",
                table: "aisles",
                columns: new[] { "tenant_id", "zone_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_aisles_tenant_id_zone_id",
                schema: "warehouse",
                table: "aisles",
                columns: new[] { "tenant_id", "zone_id" },
                principalSchema: "warehouse",
                principalTable: "zones",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_average_cost_history_tenant_id_stock_movement_id",
                schema: "accounting",
                table: "average_cost_history",
                columns: new[] { "tenant_id", "stock_movement_id" },
                principalSchema: "inventory",
                principalTable: "stock_movements",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_average_cost_history_tenant_id_warehouse_id",
                schema: "accounting",
                table: "average_cost_history",
                columns: new[] { "tenant_id", "warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_bin_assignments_tenant_id_bin_id",
                schema: "warehouse",
                table: "bin_assignments",
                columns: new[] { "tenant_id", "bin_id" },
                principalSchema: "warehouse",
                principalTable: "bins",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_bins_tenant_id_shelf_id",
                schema: "warehouse",
                table: "bins",
                columns: new[] { "tenant_id", "shelf_id" },
                principalSchema: "warehouse",
                principalTable: "shelves",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_cash_movements_tenant_id_pos_session_id",
                schema: "sales",
                table: "cash_movements",
                columns: new[] { "tenant_id", "pos_session_id" },
                principalSchema: "sales",
                principalTable: "pos_sessions",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipt_lines_tenant_id_goods_receipt_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                columns: new[] { "tenant_id", "goods_receipt_id" },
                principalSchema: "purchasing",
                principalTable: "goods_receipts",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipt_lines_tenant_id_purchase_order_line_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                columns: new[] { "tenant_id", "purchase_order_line_id" },
                principalSchema: "purchasing",
                principalTable: "purchase_order_lines",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipt_lines_tenant_id_stock_level_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                columns: new[] { "tenant_id", "stock_level_id" },
                principalSchema: "inventory",
                principalTable: "stock_levels",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipt_lines_tenant_id_stock_movement_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                columns: new[] { "tenant_id", "stock_movement_id" },
                principalSchema: "inventory",
                principalTable: "stock_movements",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipts_tenant_id_purchase_order_id",
                schema: "purchasing",
                table: "goods_receipts",
                columns: new[] { "tenant_id", "purchase_order_id" },
                principalSchema: "purchasing",
                principalTable: "purchase_orders",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipts_tenant_id_warehouse_id",
                schema: "purchasing",
                table: "goods_receipts",
                columns: new[] { "tenant_id", "warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_invoice_lines_tenant_id_invoice_id",
                schema: "sales",
                table: "invoice_lines",
                columns: new[] { "tenant_id", "invoice_id" },
                principalSchema: "sales",
                principalTable: "invoices",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_invoice_lines_tenant_id_sales_order_line_id",
                schema: "sales",
                table: "invoice_lines",
                columns: new[] { "tenant_id", "sales_order_line_id" },
                principalSchema: "sales",
                principalTable: "sales_order_lines",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_invoices_tenant_id_sales_order_id",
                schema: "sales",
                table: "invoices",
                columns: new[] { "tenant_id", "sales_order_id" },
                principalSchema: "sales",
                principalTable: "sales_orders",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_journal_lines_tenant_id_journal_entry_id",
                schema: "accounting",
                table: "journal_lines",
                columns: new[] { "tenant_id", "journal_entry_id" },
                principalSchema: "accounting",
                principalTable: "journal_entries",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_payments_tenant_id_invoice_id",
                schema: "sales",
                table: "payments",
                columns: new[] { "tenant_id", "invoice_id" },
                principalSchema: "sales",
                principalTable: "invoices",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_payments_tenant_id_pos_session_id",
                schema: "sales",
                table: "payments",
                columns: new[] { "tenant_id", "pos_session_id" },
                principalSchema: "sales",
                principalTable: "pos_sessions",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_physical_count_lines_tenant_id_physical_count_id",
                schema: "inventory",
                table: "physical_count_lines",
                columns: new[] { "tenant_id", "physical_count_id" },
                principalSchema: "inventory",
                principalTable: "physical_counts",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_physical_count_lines_tenant_id_stock_level_id",
                schema: "inventory",
                table: "physical_count_lines",
                columns: new[] { "tenant_id", "stock_level_id" },
                principalSchema: "inventory",
                principalTable: "stock_levels",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_physical_count_lines_tenant_id_stock_movement_id",
                schema: "inventory",
                table: "physical_count_lines",
                columns: new[] { "tenant_id", "stock_movement_id" },
                principalSchema: "inventory",
                principalTable: "stock_movements",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_physical_counts_tenant_id_warehouse_id",
                schema: "inventory",
                table: "physical_counts",
                columns: new[] { "tenant_id", "warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_pos_registers_tenant_id_warehouse_id",
                schema: "sales",
                table: "pos_registers",
                columns: new[] { "tenant_id", "warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_pos_sessions_tenant_id_pos_register_id",
                schema: "sales",
                table: "pos_sessions",
                columns: new[] { "tenant_id", "pos_register_id" },
                principalSchema: "sales",
                principalTable: "pos_registers",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_product_stock_policies_tenant_id_warehouse_id",
                schema: "catalog",
                table: "product_stock_policies",
                columns: new[] { "tenant_id", "warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_order_lines_tenant_id_purchase_order_id",
                schema: "purchasing",
                table: "purchase_order_lines",
                columns: new[] { "tenant_id", "purchase_order_id" },
                principalSchema: "purchasing",
                principalTable: "purchase_orders",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_orders_tenant_id_warehouse_id",
                schema: "purchasing",
                table: "purchase_orders",
                columns: new[] { "tenant_id", "warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_return_lines_tenant_id_goods_receipt_line_id",
                schema: "purchasing",
                table: "purchase_return_lines",
                columns: new[] { "tenant_id", "goods_receipt_line_id" },
                principalSchema: "purchasing",
                principalTable: "goods_receipt_lines",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_return_lines_tenant_id_purchase_return_id",
                schema: "purchasing",
                table: "purchase_return_lines",
                columns: new[] { "tenant_id", "purchase_return_id" },
                principalSchema: "purchasing",
                principalTable: "purchase_returns",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_return_lines_tenant_id_stock_level_id",
                schema: "purchasing",
                table: "purchase_return_lines",
                columns: new[] { "tenant_id", "stock_level_id" },
                principalSchema: "inventory",
                principalTable: "stock_levels",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_return_lines_tenant_id_stock_movement_id",
                schema: "purchasing",
                table: "purchase_return_lines",
                columns: new[] { "tenant_id", "stock_movement_id" },
                principalSchema: "inventory",
                principalTable: "stock_movements",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_racks_tenant_id_aisle_id",
                schema: "warehouse",
                table: "racks",
                columns: new[] { "tenant_id", "aisle_id" },
                principalSchema: "warehouse",
                principalTable: "aisles",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_order_lines_tenant_id_sales_order_id",
                schema: "sales",
                table: "sales_order_lines",
                columns: new[] { "tenant_id", "sales_order_id" },
                principalSchema: "sales",
                principalTable: "sales_orders",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_order_lines_tenant_id_stock_movement_id",
                schema: "sales",
                table: "sales_order_lines",
                columns: new[] { "tenant_id", "stock_movement_id" },
                principalSchema: "inventory",
                principalTable: "stock_movements",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_orders_tenant_id_pos_session_id",
                schema: "sales",
                table: "sales_orders",
                columns: new[] { "tenant_id", "pos_session_id" },
                principalSchema: "sales",
                principalTable: "pos_sessions",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_orders_tenant_id_warehouse_id",
                schema: "sales",
                table: "sales_orders",
                columns: new[] { "tenant_id", "warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_shelves_tenant_id_rack_id",
                schema: "warehouse",
                table: "shelves",
                columns: new[] { "tenant_id", "rack_id" },
                principalSchema: "warehouse",
                principalTable: "racks",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_adjustment_lines_tenant_id_stock_adjustment_id",
                schema: "inventory",
                table: "stock_adjustment_lines",
                columns: new[] { "tenant_id", "stock_adjustment_id" },
                principalSchema: "inventory",
                principalTable: "stock_adjustments",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_adjustment_lines_tenant_id_stock_level_id",
                schema: "inventory",
                table: "stock_adjustment_lines",
                columns: new[] { "tenant_id", "stock_level_id" },
                principalSchema: "inventory",
                principalTable: "stock_levels",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_adjustment_lines_tenant_id_stock_movement_id",
                schema: "inventory",
                table: "stock_adjustment_lines",
                columns: new[] { "tenant_id", "stock_movement_id" },
                principalSchema: "inventory",
                principalTable: "stock_movements",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_adjustments_tenant_id_warehouse_id",
                schema: "inventory",
                table: "stock_adjustments",
                columns: new[] { "tenant_id", "warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_levels_tenant_id_bin_id",
                schema: "inventory",
                table: "stock_levels",
                columns: new[] { "tenant_id", "bin_id" },
                principalSchema: "warehouse",
                principalTable: "bins",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_movements_tenant_id_stock_level_id",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "tenant_id", "stock_level_id" },
                principalSchema: "inventory",
                principalTable: "stock_levels",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_reservations_tenant_id_pos_session_id",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "tenant_id", "pos_session_id" },
                principalSchema: "sales",
                principalTable: "pos_sessions",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_reservations_tenant_id_sales_order_line_id",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "tenant_id", "sales_order_line_id" },
                principalSchema: "sales",
                principalTable: "sales_order_lines",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_reservations_tenant_id_stock_level_id",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "tenant_id", "stock_level_id" },
                principalSchema: "inventory",
                principalTable: "stock_levels",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_transfer_lines_tenant_id_destination_stock_level_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                columns: new[] { "tenant_id", "destination_stock_level_id" },
                principalSchema: "inventory",
                principalTable: "stock_levels",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_transfer_lines_tenant_id_inbound_movement_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                columns: new[] { "tenant_id", "inbound_movement_id" },
                principalSchema: "inventory",
                principalTable: "stock_movements",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_transfer_lines_tenant_id_outbound_movement_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                columns: new[] { "tenant_id", "outbound_movement_id" },
                principalSchema: "inventory",
                principalTable: "stock_movements",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_transfer_lines_tenant_id_source_stock_level_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                columns: new[] { "tenant_id", "source_stock_level_id" },
                principalSchema: "inventory",
                principalTable: "stock_levels",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_transfer_lines_tenant_id_stock_transfer_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                columns: new[] { "tenant_id", "stock_transfer_id" },
                principalSchema: "inventory",
                principalTable: "stock_transfers",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_transfers_tenant_id_from_warehouse_id",
                schema: "inventory",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "from_warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_transfers_tenant_id_to_warehouse_id",
                schema: "inventory",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "to_warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_supplier_invoice_lines_tenant_id_goods_receipt_line_id",
                schema: "purchasing",
                table: "supplier_invoice_lines",
                columns: new[] { "tenant_id", "goods_receipt_line_id" },
                principalSchema: "purchasing",
                principalTable: "goods_receipt_lines",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_supplier_invoice_lines_tenant_id_supplier_invoice_id",
                schema: "purchasing",
                table: "supplier_invoice_lines",
                columns: new[] { "tenant_id", "supplier_invoice_id" },
                principalSchema: "purchasing",
                principalTable: "supplier_invoices",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_zones_tenant_id_warehouse_id",
                schema: "warehouse",
                table: "zones",
                columns: new[] { "tenant_id", "warehouse_id" },
                principalSchema: "warehouse",
                principalTable: "warehouses",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
