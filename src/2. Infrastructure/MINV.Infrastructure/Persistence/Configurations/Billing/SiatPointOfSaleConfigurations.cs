using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Billing;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

/// <summary>
/// V4.1 · Punto de venta del SIN (por sucursal y ambiente). La caja vinculada es de la MISMA sucursal (FK compuesta con
/// la sucursal) y factura con un solo punto de venta por ambiente. Cabecera sin padre de sucursal: su sucursal se valida
/// con una FK directa, como las cabeceras de la V4.
/// </summary>
internal sealed class SiatPointOfSaleConfiguration : IEntityTypeConfiguration<SiatPointOfSale>
{
    public void Configure(EntityTypeBuilder<SiatPointOfSale> builder)
    {
        builder.ToTable("siat_points_of_sale", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_siat_points_of_sale_ambiente", BillingChecks.Environment());
            t.HasCheckConstraint("ck_siat_points_of_sale_codigo", "code BETWEEN 0 AND 9999");
            t.HasCheckConstraint("ck_siat_points_of_sale_tipo", "type_code BETWEEN 0 AND 99");
            t.HasCheckConstraint("ck_siat_points_of_sale_modo", BillingChecks.In<SiatConnectionMode>("mode"));
            t.HasCheckConstraint("ck_siat_points_of_sale_fallos", "consecutive_failures >= 0");
            t.HasCheckConstraint("ck_siat_points_of_sale_cierre", "closed_at IS NULL OR code > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(200);
        builder.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.LastError).HasMaxLength(500);
        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PosRegister>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.PosRegisterId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.Environment, x.BranchId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Environment, x.PosRegisterId }).IsUnique().HasFilter("pos_register_id IS NOT NULL");
    }
}

/// <summary>V4.1 · Historial de CUIS (append-only) de un punto de venta de la misma sucursal.</summary>
internal sealed class SiatCuisConfiguration : IEntityTypeConfiguration<SiatCuis>
{
    public void Configure(EntityTypeBuilder<SiatCuis> builder)
    {
        builder.ToTable("siat_cuis", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_siat_cuis_vigencia", "valid_until > obtained_at");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(100);
        builder.HasOne<SiatPointOfSale>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.PointOfSaleId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.PointOfSaleId, x.ValidUntil });
    }
}

/// <summary>V4.1 · Historial de CUFD (append-only): código, código de control y dirección; obtenido con un CUIS del mismo
/// punto de venta.</summary>
internal sealed class SiatCufdConfiguration : IEntityTypeConfiguration<SiatCufd>
{
    public void Configure(EntityTypeBuilder<SiatCufd> builder)
    {
        builder.ToTable("siat_cufds", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_siat_cufds_vigencia", "valid_until > obtained_at");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(100);
        builder.Property(x => x.ControlCode).HasMaxLength(50);
        builder.Property(x => x.Address).HasMaxLength(500);
        builder.HasOne<SiatPointOfSale>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.PointOfSaleId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SiatCuis>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.CuisId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.PointOfSaleId, x.ObtainedAt });
    }
}
