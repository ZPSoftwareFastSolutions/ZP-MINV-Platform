using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

/// <summary>
/// Número de serie o IMEI de una unidad. V4.2 (regla T-02): única por (empresa, variante, serie); el lote es de SU
/// variante (FK compuesta (tenant_id, variant_id, batch_id) a <c>batches</c>) y la existencia donde está es de SU lote (FK
/// compuesta (tenant_id, batch_id, stock_level_id) a <c>stock_levels</c>): la base garantiza que una serie en stock ocupa
/// una existencia de su propio producto. Solo las series en stock (o reservadas, estado de la V3) tienen existencia.
/// </summary>
internal sealed class SerialNumberConfiguration : IEntityTypeConfiguration<SerialNumber>
{
    public void Configure(EntityTypeBuilder<SerialNumber> builder)
    {
        builder.ToTable("serial_numbers", Schemas.Inventory, t =>
        {
            t.HasCheckConstraint("ck_serial_numbers_estado",
                "status IN ('InStock', 'Reserved', 'Sold', 'Returned', 'Scrapped', 'InTransit', 'InRma', 'ReturnedToSupplier')");
            t.HasCheckConstraint("ck_serial_numbers_tipo", "kind IN ('Serial', 'Imei')");
            t.HasCheckConstraint("ck_serial_numbers_ubicacion", "(status IN ('InStock', 'Reserved')) = (stock_level_id IS NOT NULL)");
            t.HasCheckConstraint("ck_serial_numbers_imei", "kind <> 'Imei' OR serial ~ '^[0-9]{15}$'");
            t.HasCheckConstraint("ck_serial_numbers_serie", "length(serial) > 0 AND serial !~ '[[:space:],;]'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Serial).HasMaxLength(SerialNumber.MaxLength);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        builder.HasOne<ProductVariant>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VariantId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VariantId, x.BatchId })
            .HasPrincipalKey(p => new { p.TenantId, p.VariantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockLevel>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BatchId, x.StockLevelId })
            .HasPrincipalKey(p => new { p.TenantId, p.BatchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.History).WithOne()
            .HasForeignKey(e => new { e.TenantId, e.SerialNumberId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.IsOnHand);
        builder.Ignore(x => x.IsAvailable);
        builder.HasIndex(x => new { x.TenantId, x.VariantId, x.Serial }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Serial });
        builder.HasIndex(x => new { x.TenantId, x.Status });
    }
}

/// <summary>V4.2 · Bitácora append-only de cada serie (trazabilidad de punta a punta). La sucursal es un dato del hecho: la
/// serie es de la empresa y viaja entre sucursales.</summary>
internal sealed class SerialEventConfiguration : IEntityTypeConfiguration<SerialEvent>
{
    public void Configure(EntityTypeBuilder<SerialEvent> builder)
    {
        builder.ToTable("serial_events", Schemas.Inventory, t =>
        {
            t.HasCheckConstraint("ck_serial_events_accion",
                "action IN ('Received', 'Sold', 'Returned', 'TransferDispatched', 'TransferReceived', 'RmaReceived', 'SentToSupplier', " +
                "'Repaired', 'Replaced', 'ReplacementIssued', 'ReturnedToSupplier', 'Scrapped', 'Adjusted', 'Restocked', 'ReturnedToCustomer')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.DocumentNumber).HasMaxLength(40);
        builder.Property(x => x.Note).HasMaxLength(300);
        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.SerialNumberId, x.OccurredAt });
        builder.HasIndex(x => new { x.TenantId, x.DocumentNumber }).HasFilter("document_number IS NOT NULL");
    }
}

/// <summary>V4.2 · Series que viajan en una línea de transferencia (append-only; las ven origen y destino). FK compuesta con
/// las dos sucursales de la línea.</summary>
internal sealed class StockTransferLineSerialConfiguration : IEntityTypeConfiguration<StockTransferLineSerial>
{
    public void Configure(EntityTypeBuilder<StockTransferLineSerial> builder)
    {
        builder.ToTable("stock_transfer_line_serials", Schemas.Inventory);
        builder.HasKey(x => x.Id);
        builder.HasOne<StockTransferLine>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.FromBranchId, x.ToBranchId, x.StockTransferLineId })
            .HasPrincipalKey(p => new { p.TenantId, p.FromBranchId, p.ToBranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SerialNumber>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SerialNumberId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.StockTransferLineId, x.SerialNumberId }).IsUnique();
        builder.HasIndex(x => x.SerialNumberId);
    }
}
