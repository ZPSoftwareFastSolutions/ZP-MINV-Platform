using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Persistence.Configurations;

/// <summary>V4 · Pedidos de canales externos (idempotencia): (canal, id externo) único por empresa; la venta es de la
/// misma sucursal (FK compuesta con la sucursal).</summary>
internal sealed class ExternalOrderConfiguration : IEntityTypeConfiguration<ExternalOrder>
{
    public void Configure(EntityTypeBuilder<ExternalOrder> builder)
    {
        builder.ToTable("external_orders", Schemas.Sales, t =>
        {
            t.HasCheckConstraint("ck_external_orders_hash", "request_hash ~ '^[0-9a-f]{64}$'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Channel).HasMaxLength(60);
        builder.Property(x => x.ExternalId).HasMaxLength(100);
        builder.Property(x => x.RequestHash).HasMaxLength(64);
        builder.Property(x => x.InvoiceNumber).HasMaxLength(30);
        builder.HasOne<SalesOrder>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.SalesOrderId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.Channel, x.ExternalId }).IsUnique();
        builder.HasIndex(x => x.SalesOrderId).IsUnique();
    }
}
