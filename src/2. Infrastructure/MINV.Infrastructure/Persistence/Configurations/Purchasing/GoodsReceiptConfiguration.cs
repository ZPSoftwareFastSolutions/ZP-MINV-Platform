using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;
using MINV.Domain.Purchasing;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> builder)
    {
        builder.ToTable("goods_receipts", Schemas.Purchasing, t =>
        {
            t.HasCheckConstraint("ck_goods_receipts_origen", "num_nonnulls(purchase_order_id, supplier_id) = 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(30);
        builder.Property(x => x.SupplierDocument).HasMaxLength(40);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<PurchaseOrder>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.PurchaseOrderId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Supplier>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SupplierId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.WarehouseId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ReceivedByUserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne()
            .HasForeignKey(c => new { c.TenantId, c.BranchId, c.GoodsReceiptId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
    }
}
