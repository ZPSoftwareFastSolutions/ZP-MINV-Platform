using MINV.Domain.Common;

namespace MINV.Domain.Purchasing;

/// <summary>Factura de proveedor.</summary>
public sealed class SupplierInvoice : Entity, IConcurrencyAware, IAggregateRoot, IBranchScoped
{
    private readonly List<SupplierInvoiceLine> _lines = new();

    private SupplierInvoice()
    {
    }

    /// <summary>V4 · Sucursal dueña de la fila (redundancia controlada; la FK compuesta con el padre la mantiene coherente).</summary>
    public Guid BranchId { get; private set; }

    public SupplierInvoice(Guid tenantId, Guid branchId, Guid supplierId, string number, DateOnly invoiceDate, DateOnly? dueDate, Guid currencyId)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        SupplierId = Guard.NotEmpty(supplierId, nameof(supplierId));
        Number = Guard.Text(number, "El número", 40);
        InvoiceDate = invoiceDate;
        DueDate = dueDate;
        CurrencyId = Guard.NotEmpty(currencyId, nameof(currencyId));
        Status = SupplierInvoiceStatus.Draft;
    }

    public Guid SupplierId { get; private set; }

    public string Number { get; private set; } = string.Empty;

    public DateOnly InvoiceDate { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public Guid CurrencyId { get; private set; }

    public SupplierInvoiceStatus Status { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<SupplierInvoiceLine> Lines => _lines;

    /// <summary>Agrega una línea (solo en borrador).</summary>
    public SupplierInvoiceLine AddLine(Guid? goodsReceiptLineId, string? description, decimal quantity, decimal unitCost, Guid? taxRateId)
    {
        EnsureDraft();
        var line = new SupplierInvoiceLine(TenantId, BranchId, Id, goodsReceiptLineId, description, quantity, unitCost, taxRateId);
        _lines.Add(line);
        return line;
    }

    /// <summary>V4.1 · Contabiliza la factura (deja de ser borrador): entra al libro de compras.</summary>
    public void Post()
    {
        EnsureDraft();
        Guard.That(_lines.Count > 0, "invoice.empty", "Una factura necesita al menos una línea.");
        Status = SupplierInvoiceStatus.Posted;
    }

    /// <summary>V4.1 · Anula la factura del proveedor (queda el registro; sale del libro de compras con estado A).</summary>
    public void Cancel()
    {
        Guard.That(Status == SupplierInvoiceStatus.Posted, "invoice.not_posted", "Solo se anula una factura contabilizada.");
        Status = SupplierInvoiceStatus.Cancelled;
    }

    private void EnsureDraft() => Guard.That(Status == SupplierInvoiceStatus.Draft, "document.not_draft",
        "Solo se pueden modificar documentos en borrador.");
}

/// <summary>
/// V4.1 · Datos fiscales de la factura de un proveedor (subtipo 1:1 de <see cref="SupplierInvoice"/>): lo que el
/// proveedor declaró en SU documento y que va al libro de compras (RCV). El importe es un hecho del documento del
/// proveedor (puede diferir por redondeo de Σ líneas); el crédito fiscal (13 % de la base) se deriva.
/// </summary>
public sealed class SupplierInvoiceFiscal : BaseEntity, IBranchScoped
{
    private SupplierInvoiceFiscal()
    {
    }

    public SupplierInvoiceFiscal(Guid tenantId, Guid branchId, Guid supplierInvoiceId, string authorizationCode, string? controlCode,
        decimal totalAmount, decimal discounts, decimal notSubjectToVat, int purchaseType)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        SupplierInvoiceId = Guard.NotEmpty(supplierInvoiceId, nameof(supplierInvoiceId));
        AuthorizationCode = Guard.Text(authorizationCode, "El CUF o código de autorización", 100);
        ControlCode = Guard.OptionalText(controlCode, "El código de control", 17);
        TotalAmount = Billing.FiscalRules.Round2(Guard.Positive(totalAmount, "El importe total de la compra"));
        Discounts = Billing.FiscalRules.Round2(Guard.NonNegative(discounts, "Los descuentos"));
        NotSubjectToVat = Billing.FiscalRules.Round2(Guard.NonNegative(notSubjectToVat, "El importe no sujeto a crédito fiscal"));
        Guard.That(purchaseType is >= 1 and <= 5, "purchase.type", "El tipo de compra va de 1 a 5.");
        PurchaseType = purchaseType;
        Guard.That(TaxBase >= 0, "purchase.base", "Los descuentos y lo no sujeto superan el importe de la compra.");
    }

    public Guid SupplierInvoiceId { get; private set; }

    public Guid BranchId { get; private set; }

    /// <summary>CUF (facturación en línea) o código de autorización (facturas antiguas).</summary>
    public string AuthorizationCode { get; private set; } = string.Empty;

    /// <summary>Código de control de facturas antiguas (en línea: «0»).</summary>
    public string? ControlCode { get; private set; }

    public decimal TotalAmount { get; private set; }

    public decimal Discounts { get; private set; }

    /// <summary>ICE, IEHD, IPJ, tasas y otros no sujetos a IVA.</summary>
    public decimal NotSubjectToVat { get; private set; }

    /// <summary>Tipo de compra del RCV (1 = mercado interno con destino a actividades gravadas…).</summary>
    public int PurchaseType { get; private set; }

    /// <summary>Importe base para crédito fiscal.</summary>
    public decimal TaxBase => TotalAmount - NotSubjectToVat - Discounts;

    /// <summary>Crédito fiscal (13 % de la base).</summary>
    public decimal TaxCredit => Billing.FiscalRules.Vat(TaxBase);
}
