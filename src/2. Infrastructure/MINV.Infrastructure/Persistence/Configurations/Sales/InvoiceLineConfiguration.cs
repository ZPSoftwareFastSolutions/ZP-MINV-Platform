using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Accounting;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("invoice_lines", Schemas.Sales, t =>
        {
            t.HasCheckConstraint("ck_invoice_lines_impuesto", "tax_amount >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TaxAmount).HasPrecision(19, 4);
        builder.HasOne<SalesOrderLine>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SalesOrderLineId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TaxRate>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.TaxRateId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.SalesOrderLineId).IsUnique();
    }
}
