using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Inventory;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class MovementTypeConfiguration : IEntityTypeConfiguration<MovementType>
{
    public void Configure(EntityTypeBuilder<MovementType> builder)
    {
        builder.ToTable("movement_types", Schemas.Inventory, t =>
        {
            t.HasCheckConstraint("ck_movement_types_factor", "stock_factor IN (-1, 1)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(30);
        builder.Property(x => x.Name).HasMaxLength(80);
        builder.Property(x => x.Description).HasMaxLength(200);
        builder.Property(x => x.Domain).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => x.TenantId).IsUnique().HasFilter("is_initial_balance");
    }
}
