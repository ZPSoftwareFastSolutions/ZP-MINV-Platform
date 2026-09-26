using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Warehousing;
using MINV.Domain.Accounting;
using MINV.Domain.Purchasing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class SupplierInvoiceConfiguration : IEntityTypeConfiguration<SupplierInvoice>
{
    public void Configure(EntityTypeBuilder<SupplierInvoice> builder)
    {
        builder.ToTable("supplier_invoices", Schemas.Purchasing, t =>
        {
            t.HasCheckConstraint("ck_supplier_invoices_vencimiento", "due_date IS NULL OR due_date >= invoice_date");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(40);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        // V4 · Cabecera sin almacén: su sucursal se valida con una FK directa (no hay padre que la herede)
        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Supplier>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SupplierId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Currency>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CurrencyId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne()
            .HasForeignKey(c => new { c.TenantId, c.BranchId, c.SupplierInvoiceId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.SupplierId, x.Number }).IsUnique();
    }
}

/// <summary>
/// V4.1 · Datos fiscales de la factura del proveedor (subtipo 1:1, clave supplier_invoice_id): lo que el proveedor
/// declaró en su documento para el libro de compras. De la misma sucursal que la factura (FK compuesta con la sucursal).
/// La base y el crédito fiscal se derivan (no se guardan).
/// </summary>
internal sealed class SupplierInvoiceFiscalConfiguration : IEntityTypeConfiguration<SupplierInvoiceFiscal>
{
    public void Configure(EntityTypeBuilder<SupplierInvoiceFiscal> builder)
    {
        builder.ToTable("supplier_invoice_fiscal", Schemas.Purchasing, t =>
        {
            t.HasCheckConstraint("ck_supplier_invoice_fiscal_importe", "total_amount > 0");
            t.HasCheckConstraint("ck_supplier_invoice_fiscal_deducciones",
                "discounts >= 0 AND not_subject_to_vat >= 0 AND total_amount - not_subject_to_vat - discounts >= 0");
            t.HasCheckConstraint("ck_supplier_invoice_fiscal_tipo", "purchase_type BETWEEN 1 AND 5");
        });
        builder.HasKey(x => x.SupplierInvoiceId);
        builder.Property(x => x.AuthorizationCode).HasMaxLength(100);
        builder.Property(x => x.ControlCode).HasMaxLength(17);
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.Discounts).HasPrecision(18, 2);
        builder.Property(x => x.NotSubjectToVat).HasPrecision(18, 2);
        builder.HasOne<SupplierInvoice>().WithOne()
            .HasForeignKey<SupplierInvoiceFiscal>(x => new { x.TenantId, x.BranchId, x.SupplierInvoiceId })
            .HasPrincipalKey<SupplierInvoice>(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
