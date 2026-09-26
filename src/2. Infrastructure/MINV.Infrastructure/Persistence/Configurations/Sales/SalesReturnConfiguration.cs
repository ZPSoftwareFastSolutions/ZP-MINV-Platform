using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Persistence.Configurations;

/// <summary>
/// V4.1 · Devolución de una venta. Se registra en la sucursal de la venta (FK compuesta con la sucursal a la factura y al
/// turno de caja); el importe a reembolsar no se guarda: sale de las líneas del pedido original.
/// </summary>
internal sealed class SalesReturnConfiguration : IEntityTypeConfiguration<SalesReturn>
{
    public void Configure(EntityTypeBuilder<SalesReturn> builder)
    {
        builder.ToTable("sales_returns", Schemas.Sales);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(40);
        builder.Property(x => x.Reason).HasMaxLength(200);
        builder.HasOne<Invoice>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.InvoiceId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PosSession>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.PosSessionId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CustomerId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PaymentMethod>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RefundPaymentMethodId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne()
            .HasForeignKey(c => new { c.TenantId, c.BranchId, c.SalesReturnId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.ReturnedAt });
    }
}

/// <summary>V4.1 · Línea devuelta: de una línea de la misma venta y sucursal, con el movimiento de stock que la repuso.</summary>
internal sealed class SalesReturnLineConfiguration : IEntityTypeConfiguration<SalesReturnLine>
{
    public void Configure(EntityTypeBuilder<SalesReturnLine> builder)
    {
        builder.ToTable("sales_return_lines", Schemas.Sales, t =>
        {
            t.HasCheckConstraint("ck_sales_return_lines_cantidad", "quantity > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.HasOne<SalesOrderLine>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.SalesOrderLineId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VariantId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockMovement>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.StockMovementId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.SalesReturnId, x.SalesOrderLineId }).IsUnique();
        builder.HasIndex(x => x.StockMovementId).IsUnique().HasFilter("stock_movement_id IS NOT NULL");
    }
}
