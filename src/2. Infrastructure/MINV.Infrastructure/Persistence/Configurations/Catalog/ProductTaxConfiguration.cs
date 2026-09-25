using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class ProductTaxConfiguration : IEntityTypeConfiguration<ProductTax>
{
    public void Configure(EntityTypeBuilder<ProductTax> builder)
    {
        builder.ToTable("product_taxes", Schemas.Catalog);
        builder.HasKey(x => new { x.ProductId, x.TaxId });
        builder.HasOne<Tax>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.TaxId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
