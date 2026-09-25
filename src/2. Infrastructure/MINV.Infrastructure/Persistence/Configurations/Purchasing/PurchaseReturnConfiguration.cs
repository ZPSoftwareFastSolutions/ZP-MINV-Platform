using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Warehousing;
using MINV.Domain.Purchasing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class PurchaseReturnConfiguration : IEntityTypeConfiguration<PurchaseReturn>
{
    public void Configure(EntityTypeBuilder<PurchaseReturn> builder)
    {
        builder.ToTable("purchase_returns", Schemas.Purchasing);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(30);
        builder.Property(x => x.Reason).HasMaxLength(200);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        // V4 · Cabecera sin almacén: su sucursal se valida con una FK directa (no hay padre que la herede)
        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Supplier>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SupplierId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne()
            .HasForeignKey(c => new { c.TenantId, c.BranchId, c.PurchaseReturnId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
    }
}
