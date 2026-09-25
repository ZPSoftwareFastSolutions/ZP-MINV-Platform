using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Inventory;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class AdjustmentReasonConfiguration : IEntityTypeConfiguration<AdjustmentReason>
{
    public void Configure(EntityTypeBuilder<AdjustmentReason> builder)
    {
        builder.ToTable("adjustment_reasons", Schemas.Inventory);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(20);
        builder.Property(x => x.Name).HasMaxLength(80);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}
