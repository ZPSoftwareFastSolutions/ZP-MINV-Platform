using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class CatalogAttributeValueConfiguration : IEntityTypeConfiguration<CatalogAttributeValue>
{
    public void Configure(EntityTypeBuilder<CatalogAttributeValue> builder)
    {
        builder.ToTable("attribute_values", Schemas.Catalog);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Value).HasMaxLength(60);
        builder.HasOne<CatalogAttribute>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.AttributeId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.AttributeId, x.Value }).IsUnique();
    }
}
