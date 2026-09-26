using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Billing;
using MINV.Domain.Iam;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

/// <summary>
/// V4.1 · Evento significativo de un punto de venta: CUFD del evento, CUFD de envío y CAFC de la MISMA sucursal. El
/// inicio y el fin son hora fiscal sin zona (timestamp without time zone), como los envía el SIN.
/// </summary>
internal sealed class SignificantEventConfiguration : IEntityTypeConfiguration<SignificantEvent>
{
    public void Configure(EntityTypeBuilder<SignificantEvent> builder)
    {
        builder.ToTable("significant_events", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_significant_events_ambiente", BillingChecks.Environment());
            t.HasCheckConstraint("ck_significant_events_clase", BillingChecks.In<SignificantEventKind>("kind"));
            t.HasCheckConstraint("ck_significant_events_estado", BillingChecks.In<SignificantEventStatus>("status"));
            t.HasCheckConstraint("ck_significant_events_codigo", "event_code BETWEEN 1 AND 99");
            t.HasCheckConstraint("ck_significant_events_rango", "ended_at IS NULL OR ended_at > started_at");
            t.HasCheckConstraint("ck_significant_events_fin", "(status = 'Open') = (ended_at IS NULL)");
            t.HasCheckConstraint("ck_significant_events_cafc", "kind = 'ManualCafc' OR contingency_code_id IS NULL");
            t.HasCheckConstraint("ck_significant_events_registro",
                "(status IN ('Open', 'Closed')) = (registered_at IS NULL) AND (registered_at IS NULL) = (reception_code IS NULL) " +
                "AND (registered_at IS NULL) = (send_cufd_id IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.StartedAt).HasColumnType("timestamp without time zone");
        builder.Property(x => x.EndedAt).HasColumnType("timestamp without time zone");
        builder.Property(x => x.ReceptionCode).HasMaxLength(100);
        builder.HasOne<SiatPointOfSale>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.PointOfSaleId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SiatCufd>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.EventCufdId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SiatCufd>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.SendCufdId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ContingencyCode>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.ContingencyCodeId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CreatedByUserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.PointOfSaleId, x.Status });
    }
}

/// <summary>V4.1 · Paquete de contingencia de un evento de la misma sucursal (≤ 500 documentos del mismo sector).</summary>
internal sealed class FiscalPackageConfiguration : IEntityTypeConfiguration<FiscalPackage>
{
    public void Configure(EntityTypeBuilder<FiscalPackage> builder)
    {
        builder.ToTable("fiscal_packages", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_fiscal_packages_estado", BillingChecks.In<FiscalPackageStatus>("status"));
            t.HasCheckConstraint("ck_fiscal_packages_sector", "document_sector BETWEEN 1 AND 99");
            t.HasCheckConstraint("ck_fiscal_packages_huella", BillingChecks.Sha256("sha256"));
            t.HasCheckConstraint("ck_fiscal_packages_validacion", "(status = 'Sent') = (validated_at IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Cafc).HasMaxLength(50);
        builder.Property(x => x.Sha256).HasMaxLength(64);
        builder.Property(x => x.ReceptionCode).HasMaxLength(100);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Messages).HasColumnType("text");
        builder.HasOne<SignificantEvent>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.SignificantEventId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SiatPointOfSale>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.PointOfSaleId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SiatCufd>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.SendCufdId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>V4.1 · CAFC (talonario de contingencia manual) de una sucursal y documento sector.</summary>
internal sealed class ContingencyCodeConfiguration : IEntityTypeConfiguration<ContingencyCode>
{
    public void Configure(EntityTypeBuilder<ContingencyCode> builder)
    {
        builder.ToTable("contingency_codes", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_contingency_codes_sector", "document_sector BETWEEN 1 AND 99");
            t.HasCheckConstraint("ck_contingency_codes_rango", "number_from > 0 AND number_to >= number_from");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(50);
        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.DocumentSector, x.Code }).IsUnique();
    }
}
