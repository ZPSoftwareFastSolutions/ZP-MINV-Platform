using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;
using MINV.Domain.Purchasing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class ProductSupplierConfiguration : IEntityTypeConfiguration<ProductSupplier>
{
    public void Configure(EntityTypeBuilder<ProductSupplier> builder)
    {
        builder.ToTable("product_suppliers", Schemas.Catalog, t =>
        {
            t.HasCheckConstraint("ck_product_suppliers_dias", "lead_time_days IS NULL OR lead_time_days >= 0");
        });
        builder.HasKey(x => new { x.ProductId, x.SupplierId });
        builder.Property(x => x.SupplierSku).HasMaxLength(60);
        builder.HasOne<Product>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ProductId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Supplier>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SupplierId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ProductId).IsUnique().HasFilter("is_preferred");
    }
}
