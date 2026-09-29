using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Purchasing;
using MINV.Domain.Sales;
using MINV.Domain.Service;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

/// <summary>
/// V4.2 · Caso de garantía (RMA) de una sucursal (esquema <c>service</c>, regla T-05): número único por sucursal, xmin,
/// venta original de la MISMA sucursal (FK compuesta con la sucursal), un solo caso abierto por serie y los requisitos de
/// cada estado (resolución, proveedor, reemplazo, cierre) también como CHECK. La vigencia de la garantía no se guarda (T-04).
/// </summary>
internal sealed class WarrantyClaimConfiguration : IEntityTypeConfiguration<WarrantyClaim>
{
    public void Configure(EntityTypeBuilder<WarrantyClaim> builder)
    {
        builder.ToTable("warranty_claims", Schemas.Service, t =>
        {
            t.HasCheckConstraint("ck_warranty_claims_estado",
                "status IN ('Received', 'Diagnosing', 'SentToSupplier', 'Repaired', 'Replaced', 'Rejected', 'Delivered')");
            t.HasCheckConstraint("ck_warranty_claims_resolucion",
                "status NOT IN ('Repaired', 'Replaced', 'Rejected', 'Delivered') OR resolution IS NOT NULL");
            t.HasCheckConstraint("ck_warranty_claims_proveedor", "status <> 'SentToSupplier' OR supplier_id IS NOT NULL");
            t.HasCheckConstraint("ck_warranty_claims_reemplazo", "status <> 'Replaced' OR replacement_serial_id IS NOT NULL");
            t.HasCheckConstraint("ck_warranty_claims_otra_unidad", "replacement_serial_id IS NULL OR replacement_serial_id <> serial_number_id");
            t.HasCheckConstraint("ck_warranty_claims_cierre", "(status = 'Delivered') = (closed_at IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(40);
        builder.Property(x => x.Issue).HasMaxLength(500);
        builder.Property(x => x.Resolution).HasMaxLength(500);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SerialNumber>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SerialNumberId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SerialNumber>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ReplacementSerialId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CustomerId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Invoice>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.InvoiceId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Supplier>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SupplierId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.OpenedByUserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.History).WithOne()
            .HasForeignKey(e => new { e.TenantId, e.BranchId, e.ClaimId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.IsOpen);
        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.Number }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.SerialNumberId }).IsUnique().HasFilter("status <> 'Delivered'");
        // V7 · Índice completo de la FK a la serie (el único parcial solo cubre los casos abiertos): el historial de garantías de
        // una serie y la comprobación de la FK no recorren la tabla (comprobación de normalización E06,
        // docs/database/normalizacion-v7.md)
        builder.HasIndex(x => new { x.TenantId, x.SerialNumberId }, "ix_warranty_claims_tenant_id_serial_number_id");
        builder.HasIndex(x => new { x.TenantId, x.Status });
    }
}

/// <summary>V4.2 · Bitácora append-only de un caso RMA (de la sucursal del caso: FK compuesta con la sucursal).</summary>
internal sealed class WarrantyClaimEventConfiguration : IEntityTypeConfiguration<WarrantyClaimEvent>
{
    public void Configure(EntityTypeBuilder<WarrantyClaimEvent> builder)
    {
        builder.ToTable("warranty_claim_events", Schemas.Service, t =>
        {
            t.HasCheckConstraint("ck_warranty_claim_events_accion", "action IN ('Opened', 'StatusChanged', 'NoteAdded', 'ReplacementIssued', 'Closed')");
            // V7 · Dominio del estado resultante (comprobación de normalización E13, docs/database/normalizacion-v7.md)
            t.HasCheckConstraint("ck_warranty_claim_events_estado", BillingChecks.In<WarrantyClaimStatus>("status"));
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ClaimId, x.OccurredAt });
    }
}
