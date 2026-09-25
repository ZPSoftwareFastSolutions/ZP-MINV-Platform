using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("warehouses", Schemas.Warehousing);
        builder.HasKey(x => x.Id);
        // V4 · Destino de las FK (tenant_id, branch_id, warehouse_id): el contenido de un almacén es de su sucursal
        builder.HasAlternateKey(x => new { x.TenantId, x.BranchId, x.Id });
        builder.Property(x => x.Code).HasMaxLength(20);
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}
