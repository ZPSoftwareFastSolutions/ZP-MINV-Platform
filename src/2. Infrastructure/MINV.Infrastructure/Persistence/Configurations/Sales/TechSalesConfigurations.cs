using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

/// <summary>V4.2 · Series vendidas en una línea de venta (append-only, de la sucursal de la venta: FK compuesta con la
/// sucursal). Salen en el ticket, en la factura del SIN (numeroSerie/numeroImei, regla T-03) y en la garantía.</summary>
internal sealed class SalesOrderLineSerialConfiguration : IEntityTypeConfiguration<SalesOrderLineSerial>
{
    public void Configure(EntityTypeBuilder<SalesOrderLineSerial> builder)
    {
        builder.ToTable("sales_order_line_serials", Schemas.Sales);
        builder.HasKey(x => x.Id);
        builder.HasOne<SalesOrderLine>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.SalesOrderLineId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SerialNumber>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SerialNumberId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.SalesOrderLineId, x.SerialNumberId }).IsUnique();
        builder.HasIndex(x => x.SerialNumberId);
    }
}

/// <summary>V4.2 · Series devueltas en una línea de devolución (append-only, de la sucursal de la devolución).</summary>
internal sealed class SalesReturnLineSerialConfiguration : IEntityTypeConfiguration<SalesReturnLineSerial>
{
    public void Configure(EntityTypeBuilder<SalesReturnLineSerial> builder)
    {
        builder.ToTable("sales_return_line_serials", Schemas.Sales);
        builder.HasKey(x => x.Id);
        builder.HasOne<SalesReturnLine>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.SalesReturnLineId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SerialNumber>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SerialNumberId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.SalesReturnLineId, x.SerialNumberId }).IsUnique();
        builder.HasIndex(x => x.SerialNumberId);
    }
}

/// <summary>
/// V4.2 · Armado de PC (cotización) de una sucursal: número único por sucursal, xmin, venta que lo cobró de la MISMA
/// sucursal (FK compuesta) y una sola cotización por venta. El total no se guarda (sale de las líneas).
/// </summary>
internal sealed class PcBuildConfiguration : IEntityTypeConfiguration<PcBuild>
{
    public void Configure(EntityTypeBuilder<PcBuild> builder)
    {
        builder.ToTable("pc_builds", Schemas.Sales, t =>
        {
            t.HasCheckConstraint("ck_pc_builds_estado", "status IN ('Draft', 'Quoted', 'Sold', 'Cancelled')");
            t.HasCheckConstraint("ck_pc_builds_venta", "(status = 'Sold') = (invoice_id IS NOT NULL)");
            t.HasCheckConstraint("ck_pc_builds_cotizacion", "status <> 'Quoted' OR quoted_at IS NOT NULL");
            t.HasCheckConstraint("ck_pc_builds_marcado", "NOT quoted_with_errors OR quoted_at IS NOT NULL");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(40);
        builder.Property(x => x.Name).HasMaxLength(150);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CustomerId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CreatedByUserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Invoice>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.InvoiceId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne()
            .HasForeignKey(c => new { c.TenantId, c.BranchId, c.PcBuildId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.Total);
        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.Number }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => x.InvoiceId).IsUnique().HasFilter("invoice_id IS NOT NULL");
    }
}

/// <summary>V4.2 · Pieza de un armado con su precio cotizado (redundancia comercial documentada: la oferta hecha).</summary>
internal sealed class PcBuildLineConfiguration : IEntityTypeConfiguration<PcBuildLine>
{
    public void Configure(EntityTypeBuilder<PcBuildLine> builder)
    {
        builder.ToTable("pc_build_lines", Schemas.Sales, t =>
        {
            t.HasCheckConstraint("ck_pc_build_lines_ranura",
                "slot IN ('Cpu', 'Motherboard', 'Ram', 'Gpu', 'Storage', 'Psu', 'Case', 'Cooler', 'Monitor', 'Peripheral', 'Software', 'Service')");
            t.HasCheckConstraint("ck_pc_build_lines_cantidad", $"quantity BETWEEN 1 AND {PcBuild.MaxQuantity}");
            t.HasCheckConstraint("ck_pc_build_lines_precio", "quoted_unit_price >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Slot).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.QuotedUnitPrice).HasPrecision(19, 4);
        builder.HasOne<ProductVariant>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VariantId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(x => x.Subtotal);
    }
}
