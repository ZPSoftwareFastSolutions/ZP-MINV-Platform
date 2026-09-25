using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Accounting;
using MINV.Domain.Purchasing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class SupplierInvoiceLineConfiguration : IEntityTypeConfiguration<SupplierInvoiceLine>
{
    public void Configure(EntityTypeBuilder<SupplierInvoiceLine> builder)
    {
        builder.ToTable("supplier_invoice_lines", Schemas.Purchasing, t =>
        {
            t.HasCheckConstraint("ck_supplier_invoice_lines_origen", "num_nonnulls(goods_receipt_line_id, description) = 1");
            t.HasCheckConstraint("ck_supplier_invoice_lines_cantidad", "quantity > 0");
            t.HasCheckConstraint("ck_supplier_invoice_lines_costo", "unit_cost >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Description).HasMaxLength(200);
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.Property(x => x.UnitCost).HasPrecision(19, 4);
        builder.HasOne<GoodsReceiptLine>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.GoodsReceiptLineId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TaxRate>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.TaxRateId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
