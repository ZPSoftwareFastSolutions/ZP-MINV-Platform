using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", Schemas.Catalog);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(40);
        builder.Property(x => x.Name).HasMaxLength(150);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.TrackingMode).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Category>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CategoryId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BrandModel>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ModelId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UnitOfMeasure>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BaseUnitId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Variants).WithOne()
            .HasForeignKey(c => new { c.TenantId, c.ProductId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Variants).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(x => x.Taxes).WithOne()
            .HasForeignKey(c => new { c.TenantId, c.ProductId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Taxes).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(x => x.Packagings).WithOne()
            .HasForeignKey(c => new { c.TenantId, c.ProductId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Packagings).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}
