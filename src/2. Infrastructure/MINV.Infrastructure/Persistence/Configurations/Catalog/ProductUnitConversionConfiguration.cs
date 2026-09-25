using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class ProductUnitConversionConfiguration : IEntityTypeConfiguration<ProductUnitConversion>
{
    public void Configure(EntityTypeBuilder<ProductUnitConversion> builder)
    {
        builder.ToTable("product_unit_conversions", Schemas.Catalog, t =>
        {
            t.HasCheckConstraint("ck_product_unit_conversions_factor", "factor_to_base > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FactorToBase).HasPrecision(18, 8);
        builder.HasOne<UnitOfMeasure>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UnitId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ProductId, x.UnitId }).IsUnique();
    }
}
