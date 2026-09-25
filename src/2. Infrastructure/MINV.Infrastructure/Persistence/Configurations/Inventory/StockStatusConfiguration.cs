using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Inventory;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class StockStatusConfiguration : IEntityTypeConfiguration<StockStatus>
{
    public void Configure(EntityTypeBuilder<StockStatus> builder)
    {
        builder.ToTable("stock_statuses", Schemas.Inventory);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(20);
        builder.Property(x => x.Name).HasMaxLength(40);
        builder.Property(x => x.SuggestedAction).HasMaxLength(200);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Priority }).IsUnique();
    }
}
