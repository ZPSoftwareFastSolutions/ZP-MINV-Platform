using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;
using MINV.Domain.Inventory;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> builder)
    {
        builder.ToTable("batches", Schemas.Inventory, t =>
        {
            t.HasCheckConstraint("ck_batches_caducidad", "expires_on IS NULL OR manufactured_on IS NULL OR expires_on >= manufactured_on");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.LotNumber).HasMaxLength(40);
        builder.HasOne<ProductVariant>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VariantId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.VariantId, x.LotNumber }).IsUnique();
        builder.HasIndex(x => x.VariantId).IsUnique().HasFilter("is_default");
    }
}
