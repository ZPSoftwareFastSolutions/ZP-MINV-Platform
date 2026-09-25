using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class BrandModelConfiguration : IEntityTypeConfiguration<BrandModel>
{
    public void Configure(EntityTypeBuilder<BrandModel> builder)
    {
        builder.ToTable("models", Schemas.Catalog);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(80);
        builder.HasOne<Brand>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BrandId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.BrandId, x.Name }).IsUnique();
    }
}
