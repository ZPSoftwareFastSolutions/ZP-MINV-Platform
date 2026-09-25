using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class PosRegisterConfiguration : IEntityTypeConfiguration<PosRegister>
{
    public void Configure(EntityTypeBuilder<PosRegister> builder)
    {
        builder.ToTable("pos_registers", Schemas.Sales);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(20);
        builder.Property(x => x.Name).HasMaxLength(80);
        builder.HasOne<Warehouse>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.WarehouseId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HardwareToken>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HardwareTokenId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => x.HardwareTokenId).IsUnique().HasFilter("hardware_token_id IS NOT NULL");
    }
}
