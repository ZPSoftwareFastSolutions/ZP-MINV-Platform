using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

/// <summary>
/// V4 · Transferencia entre sucursales. Clave alterna (tenant_id, from_branch_id, to_branch_id, id): destino de las FK de
/// líneas y bitácora, que así no pueden tener otras sucursales que la cabecera. Cada almacén se referencia con su
/// sucursal (tenant_id, branch_id, warehouse_id): el almacén de origen es de la sucursal de origen y el de destino de la de
/// destino.
/// </summary>
internal sealed class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> builder)
    {
        builder.ToTable("stock_transfers", Schemas.Inventory, t =>
        {
            t.HasCheckConstraint("ck_stock_transfers_almacenes", "from_warehouse_id <> to_warehouse_id");
            t.HasCheckConstraint("ck_stock_transfers_estado",
                "status IN ('Pending', 'Dispatched', 'Received', 'Cancelled')");
            t.HasCheckConstraint("ck_stock_transfers_despacho", "(status IN ('Dispatched', 'Received')) = (dispatched_at IS NOT NULL)");
            t.HasCheckConstraint("ck_stock_transfers_recepcion", "(status = 'Received') = (received_at IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.TenantId, x.FromBranchId, x.ToBranchId, x.Id });
        builder.Property(x => x.Number).HasMaxLength(30);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Notes).HasMaxLength(250);
        builder.HasOne<Warehouse>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.FromBranchId, x.FromWarehouseId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ToBranchId, x.ToWarehouseId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RequestedByUserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne()
            .HasForeignKey(c => new { c.TenantId, c.FromBranchId, c.ToBranchId, c.StockTransferId })
            .HasPrincipalKey(p => new { p.TenantId, p.FromBranchId, p.ToBranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.History).WithOne()
            .HasForeignKey(c => new { c.TenantId, c.FromBranchId, c.ToBranchId, c.TransferId })
            .HasPrincipalKey(p => new { p.TenantId, p.FromBranchId, p.ToBranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);
        builder.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status });
    }
}

internal sealed class StockTransferLineConfiguration : IEntityTypeConfiguration<StockTransferLine>
{
    public void Configure(EntityTypeBuilder<StockTransferLine> builder)
    {
        builder.ToTable("stock_transfer_lines", Schemas.Inventory, t =>
        {
            t.HasCheckConstraint("ck_stock_transfer_lines_cantidad", "quantity > 0");
            t.HasCheckConstraint("ck_stock_transfer_lines_costo", "unit_cost IS NULL OR unit_cost >= 0");
        });
        builder.HasKey(x => x.Id);
        // Destino del manifiesto y de los faltantes: siempre con las mismas sucursales que la línea
        builder.HasAlternateKey(x => new { x.TenantId, x.FromBranchId, x.ToBranchId, x.Id });
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.Property(x => x.UnitCost).HasPrecision(18, 6);
        builder.HasOne<ProductVariant>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VariantId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Movements).WithOne()
            .HasForeignKey(c => new { c.TenantId, c.TransferLineId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Discrepancies).WithOne()
            .HasForeignKey(c => new { c.TenantId, c.FromBranchId, c.ToBranchId, c.TransferLineId })
            .HasPrincipalKey(p => new { p.TenantId, p.FromBranchId, p.ToBranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Batches).WithOne()
            .HasForeignKey(c => new { c.TenantId, c.FromBranchId, c.ToBranchId, c.TransferLineId })
            .HasPrincipalKey(p => new { p.TenantId, p.FromBranchId, p.ToBranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Movements).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Discrepancies).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Batches).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.Shortage);
        builder.Ignore(x => x.ReceivedQuantity);
        builder.HasIndex(x => new { x.StockTransferId, x.VariantId }).IsUnique();
    }
}

/// <summary>Vínculo línea ↔ movimiento (append-only). La FK compuesta con la sucursal del movimiento impide vincular un
/// movimiento de otra sucursal; un movimiento pertenece a una sola línea.</summary>
internal sealed class StockTransferMovementConfiguration : IEntityTypeConfiguration<StockTransferMovement>
{
    public void Configure(EntityTypeBuilder<StockTransferMovement> builder)
    {
        builder.ToTable("stock_transfer_movements", Schemas.Inventory, t =>
        {
            t.HasCheckConstraint("ck_stock_transfer_movements_sentido", "direction IN ('Out', 'In')");
        });
        builder.HasKey(x => new { x.TransferLineId, x.StockMovementId });
        builder.Property(x => x.Direction).HasConversion<string>().HasMaxLength(5);
        builder.HasOne<StockMovement>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.StockMovementId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.StockMovementId).IsUnique();
    }
}

internal sealed class StockTransferLineBatchConfiguration : IEntityTypeConfiguration<StockTransferLineBatch>
{
    public void Configure(EntityTypeBuilder<StockTransferLineBatch> builder)
    {
        builder.ToTable("stock_transfer_line_batches", Schemas.Inventory, t =>
        {
            t.HasCheckConstraint("ck_stock_transfer_line_batches_cantidad", "quantity > 0");
        });
        builder.HasKey(x => new { x.TransferLineId, x.BatchId });
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.HasOne<Batch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BatchId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockTransferDiscrepancyConfiguration : IEntityTypeConfiguration<StockTransferDiscrepancy>
{
    public void Configure(EntityTypeBuilder<StockTransferDiscrepancy> builder)
    {
        builder.ToTable("stock_transfer_discrepancies", Schemas.Inventory, t =>
        {
            t.HasCheckConstraint("ck_stock_transfer_discrepancies_cantidad", "quantity > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.Property(x => x.Reason).HasMaxLength(200);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RecordedByUserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockTransferEventConfiguration : IEntityTypeConfiguration<StockTransferEvent>
{
    public void Configure(EntityTypeBuilder<StockTransferEvent> builder)
    {
        builder.ToTable("stock_transfer_events", Schemas.Inventory);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Detail).HasMaxLength(250);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TransferId, x.OccurredAt });
    }
}
