using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class PriceListItemConfiguration : IEntityTypeConfiguration<PriceListItem>
{
    public void Configure(EntityTypeBuilder<PriceListItem> builder)
    {
        builder.ToTable("price_list_items", Schemas.Sales, t =>
        {
            t.HasCheckConstraint("ck_price_list_items_precio", "unit_price >= 0");
        });
        builder.HasKey(x => new { x.PriceListId, x.VariantId });
        builder.Property(x => x.UnitPrice).HasPrecision(19, 4);
        builder.HasOne<PriceList>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.PriceListId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VariantId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
