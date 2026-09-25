using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.ToTable("brands", Schemas.Catalog);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(80);
        builder.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}
