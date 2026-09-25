using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Accounting;
using MINV.Domain.Purchasing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class SupplierInvoiceConfiguration : IEntityTypeConfiguration<SupplierInvoice>
{
    public void Configure(EntityTypeBuilder<SupplierInvoice> builder)
    {
        builder.ToTable("supplier_invoices", Schemas.Purchasing, t =>
        {
            t.HasCheckConstraint("ck_supplier_invoices_vencimiento", "due_date IS NULL OR due_date >= invoice_date");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(40);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Supplier>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SupplierId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Currency>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CurrencyId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne()
            .HasForeignKey(c => new { c.TenantId, c.SupplierInvoiceId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.SupplierId, x.Number }).IsUnique();
    }
}
