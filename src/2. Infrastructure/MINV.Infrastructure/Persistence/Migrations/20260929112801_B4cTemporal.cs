using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MINV.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B4cTemporal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_warranty_claims_tenant_id_serial_number_id",
                schema: "service",
                table: "warranty_claims",
                columns: new[] { "tenant_id", "serial_number_id" });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
