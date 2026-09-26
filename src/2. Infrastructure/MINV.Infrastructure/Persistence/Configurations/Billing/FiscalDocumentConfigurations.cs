using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Billing;
using MINV.Domain.Catalog;
using MINV.Domain.Iam;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Persistence.Configurations;

/// <summary>
/// V4.1 · Documento fiscal (factura sector 1 o nota crédito-débito sector 24). Todo lo que cuelga de él o lo que usa
/// (punto de venta, CUIS, CUFD, venta, devolución, evento, paquete, documento reemplazado) es de la MISMA sucursal: FK
/// compuestas (tenant_id, branch_id, x_id). Numeración única por (ambiente, punto de venta, sector); CUF único; una venta
/// tiene un solo documento ACTIVO (índice único parcial). La hora fiscal va sin zona (timestamp without time zone): es
/// la misma que el CUF y el XML.
/// </summary>
internal sealed class FiscalDocumentConfiguration : IEntityTypeConfiguration<FiscalDocument>
{
    /// <summary>Estados en los que una venta ya tiene su documento (no admite otro activo).</summary>
    internal const string ActiveStatusFilter = "invoice_id IS NOT NULL AND status IN ('Pending', 'Valid', 'Offline', 'InPackage')";

    public void Configure(EntityTypeBuilder<FiscalDocument> builder)
    {
        builder.ToTable("fiscal_documents", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_fiscal_documents_ambiente", BillingChecks.Environment());
            t.HasCheckConstraint("ck_fiscal_documents_sector", "document_sector IN (1, 24)");
            t.HasCheckConstraint("ck_fiscal_documents_tipo", "document_type IN (1, 3)");
            t.HasCheckConstraint("ck_fiscal_documents_clase",
                "(kind = 'Invoice' AND document_sector = 1 AND document_type = 1) " +
                "OR (kind = 'CreditDebitNote' AND document_sector = 24 AND document_type = 3)");
            t.HasCheckConstraint("ck_fiscal_documents_emision", "emission_type IN (1, 2)");
            t.HasCheckConstraint("ck_fiscal_documents_excepcion", "exception_code IN (0, 1)");
            t.HasCheckConstraint("ck_fiscal_documents_numero", "number > 0 AND number <= 9999999999");
            t.HasCheckConstraint("ck_fiscal_documents_estado", BillingChecks.In<FiscalDocumentStatus>("status"));
            t.HasCheckConstraint("ck_fiscal_documents_comprador",
                "buyer_document_type BETWEEN 1 AND 5 AND (buyer_complement IS NULL OR buyer_document_type = 1)");
            t.HasCheckConstraint("ck_fiscal_documents_cafc", "cafc IS NULL OR emission_type = 2");
            t.HasCheckConstraint("ck_fiscal_documents_notas_en_linea", "kind = 'Invoice' OR emission_type = 1");
            t.HasCheckConstraint("ck_fiscal_documents_origen",
                "(invoice_id IS NULL OR kind = 'Invoice') AND (sales_return_id IS NULL OR kind = 'CreditDebitNote')");
            t.HasCheckConstraint("ck_fiscal_documents_pago",
                "(kind = 'Invoice') = (payment_method_code IS NOT NULL) AND (payment_method_code IS NULL OR payment_method_code BETWEEN 1 AND 999)");
            t.HasCheckConstraint("ck_fiscal_documents_montos", "additional_discount >= 0 AND gift_card_amount >= 0 AND exchange_rate > 0");
            t.HasCheckConstraint("ck_fiscal_documents_paquete",
                "(package_id IS NULL) = (package_position IS NULL) AND (package_position IS NULL OR package_position BETWEEN 1 AND 500) " +
                "AND (status <> 'InPackage' OR package_id IS NOT NULL)");
            t.HasCheckConstraint("ck_fiscal_documents_anulacion", "status <> 'Voided' OR voided_at IS NOT NULL");
            t.HasCheckConstraint("ck_fiscal_documents_reversion", "is_reverted = (reverted_at IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Cuf).HasMaxLength(100);
        builder.Property(x => x.IssuedAt).HasColumnType("timestamp without time zone");
        builder.Property(x => x.CustomerCode).HasMaxLength(100);
        builder.Property(x => x.BuyerDocumentNumber).HasMaxLength(20);
        builder.Property(x => x.BuyerComplement).HasMaxLength(5);
        builder.Property(x => x.BuyerName).HasMaxLength(500);
        builder.Property(x => x.BuyerEmail).HasMaxLength(254);
        builder.Property(x => x.CardNumberMasked).HasMaxLength(16);
        builder.Property(x => x.ExchangeRate).HasPrecision(18, 8);
        builder.Property(x => x.AdditionalDiscount).HasPrecision(18, 2);
        builder.Property(x => x.GiftCardAmount).HasPrecision(18, 2);
        builder.Property(x => x.Cafc).HasMaxLength(50);
        builder.Property(x => x.Legend).HasMaxLength(200);
        builder.Property(x => x.UserCode).HasMaxLength(100);
        builder.Property(x => x.ReceptionCode).HasMaxLength(100);
        builder.HasOne<SiatPointOfSale>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.PointOfSaleId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SiatCuis>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.CuisId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SiatCufd>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.CufdId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Invoice>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.InvoiceId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesReturn>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.SalesReturnId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SignificantEvent>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.SignificantEventId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FiscalPackage>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.PackageId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FiscalDocument>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.ReplacesDocumentId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CustomerId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne()
            .HasForeignKey(c => new { c.TenantId, c.BranchId, c.DocumentId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.NoteReference).WithOne()
            .HasForeignKey<FiscalNoteReference>(c => new { c.TenantId, c.BranchId, c.DocumentId })
            .HasPrincipalKey<FiscalDocument>(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);
        builder.HasIndex(x => new { x.TenantId, x.Environment, x.PointOfSaleId, x.DocumentSector, x.Number }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Cuf }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.InvoiceId }).IsUnique().HasFilter(ActiveStatusFilter);
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.IssuedAt });
    }
}

/// <summary>V4.1 · Subtipo 1:1 de las notas: factura original (de M-INV, de la misma sucursal, o transcrita).</summary>
internal sealed class FiscalNoteReferenceConfiguration : IEntityTypeConfiguration<FiscalNoteReference>
{
    public void Configure(EntityTypeBuilder<FiscalNoteReference> builder)
    {
        builder.ToTable("fiscal_note_references", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_fiscal_note_references_numero", "original_number > 0 AND original_number <= 9999999999");
            t.HasCheckConstraint("ck_fiscal_note_references_descuento", "discount_share IS NULL OR discount_share >= 0");
        });
        builder.HasKey(x => x.DocumentId);
        builder.Property(x => x.OriginalCuf).HasMaxLength(100);
        builder.Property(x => x.OriginalIssuedAt).HasColumnType("timestamp without time zone");
        builder.Property(x => x.DiscountShare).HasPrecision(18, 2);
        builder.HasOne<FiscalDocument>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.OriginalDocumentId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>V4.1 · Detalle congelado (append-only). Cantidades, precios y descuentos con 10 decimales (notas).</summary>
internal sealed class FiscalDocumentLineConfiguration : IEntityTypeConfiguration<FiscalDocumentLine>
{
    public void Configure(EntityTypeBuilder<FiscalDocumentLine> builder)
    {
        builder.ToTable("fiscal_document_lines", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_fiscal_document_lines_numero", $"line_number BETWEEN 1 AND {SiatCodes.MaxLinesPerDocument}");
            t.HasCheckConstraint("ck_fiscal_document_lines_cantidad", "quantity > 0");
            t.HasCheckConstraint("ck_fiscal_document_lines_precio", "unit_price > 0");
            t.HasCheckConstraint("ck_fiscal_document_lines_descuento", "discount IS NULL OR discount >= 0");
            t.HasCheckConstraint("ck_fiscal_document_lines_producto_sin", "sin_product_code BETWEEN 1 AND 99999999");
            t.HasCheckConstraint("ck_fiscal_document_lines_unidad_sin", "sin_unit_code BETWEEN 1 AND 999");
            t.HasCheckConstraint("ck_fiscal_document_lines_transaccion", "transaction_code IS NULL OR transaction_code IN (1, 2)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActivityCode).HasMaxLength(10);
        builder.Property(x => x.ProductCode).HasMaxLength(50);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.Quantity).HasPrecision(20, 10);
        builder.Property(x => x.UnitPrice).HasPrecision(20, 10);
        builder.Property(x => x.Discount).HasPrecision(20, 10);
        builder.Property(x => x.SerialNumber).HasMaxLength(1500);
        builder.Property(x => x.Imei).HasMaxLength(1500);
        builder.HasOne<ProductVariant>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VariantId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.DocumentId, x.LineNumber }).IsUnique();
    }
}

/// <summary>V4.1 · XML exacto validado y huella SHA-256 del GZIP (append-only; uno por documento).</summary>
internal sealed class FiscalDocumentFileConfiguration : IEntityTypeConfiguration<FiscalDocumentFile>
{
    public void Configure(EntityTypeBuilder<FiscalDocumentFile> builder)
    {
        builder.ToTable("fiscal_document_files", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_fiscal_document_files_huella", BillingChecks.Sha256("gzip_sha256"));
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Xml).HasColumnType("text");
        builder.Property(x => x.GzipSha256).HasMaxLength(64);
        builder.HasOne<FiscalDocument>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.DocumentId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.DocumentId).IsUnique();
    }
}

/// <summary>V4.1 · Bitácora del documento (append-only): envíos, respuestas del SIN y acciones del usuario.</summary>
internal sealed class FiscalDocumentEventConfiguration : IEntityTypeConfiguration<FiscalDocumentEvent>
{
    public void Configure(EntityTypeBuilder<FiscalDocumentEvent> builder)
    {
        builder.ToTable("fiscal_document_events", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_fiscal_document_events_accion", BillingChecks.In<FiscalDocumentAction>("action"));
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.ReceptionCode).HasMaxLength(100);
        builder.Property(x => x.Messages).HasMaxLength(8000);
        builder.HasOne<FiscalDocument>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.DocumentId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.DocumentId, x.OccurredAt });
    }
}

/// <summary>V4.1 · Entregas del documento al comprador (append-only).</summary>
internal sealed class FiscalDeliveryConfiguration : IEntityTypeConfiguration<FiscalDelivery>
{
    public void Configure(EntityTypeBuilder<FiscalDelivery> builder)
    {
        builder.ToTable("fiscal_deliveries", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_fiscal_deliveries_canal", BillingChecks.In<FiscalDeliveryChannel>("channel"));
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Recipient).HasMaxLength(254);
        builder.Property(x => x.Error).HasMaxLength(500);
        builder.HasOne<FiscalDocument>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.DocumentId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.DocumentId, x.OccurredAt });
    }
}
