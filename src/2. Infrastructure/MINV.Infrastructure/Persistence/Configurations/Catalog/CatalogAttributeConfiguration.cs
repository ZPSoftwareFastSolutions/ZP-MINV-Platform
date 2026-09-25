using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class CatalogAttributeConfiguration : IEntityTypeConfiguration<CatalogAttribute>
{
    public void Configure(EntityTypeBuilder<CatalogAttribute> builder)
    {
        builder.ToTable("attributes", Schemas.Catalog);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(60);
        builder.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}
