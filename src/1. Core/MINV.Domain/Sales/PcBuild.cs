using MINV.Domain.Catalog;
using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>V4.2 · Estado de un armado de PC (cotización). Se guarda como texto.</summary>
public enum PcBuildStatus
{
    Draft,
    Quoted,
    Sold,
    Cancelled,
}

/// <summary>
/// V4.2 · Armado de PC (regla T-06): cotización con las piezas por ranura y el PRECIO COTIZADO de cada una (instantánea
/// comercial documentada: el cliente tiene ese precio mientras la cotización esté vigente, aunque cambie la lista de
/// precios). Número ARM-CM-000001. Borrador → Cotizado (congela precios y vigencia) → Vendido (con la venta que lo cobró)
/// o Anulado. La compatibilidad la decide SOLO <see cref="PcCompatibility.Check"/>: un armado con errores no se cotiza sin
/// la confirmación explícita del usuario y queda marcado (<see cref="QuotedWithErrors"/>). Pasa a la caja como carrito y
/// se vende con los casos de uso normales de venta.
/// </summary>
public sealed class PcBuild : Entity, IBranchScoped, IConcurrencyAware, IAggregateRoot
{
    public const int MaxQuantity = 16;

    /// <summary>Ranuras que admiten varias líneas (el resto, una sola pieza por armado).</summary>
    public static readonly IReadOnlyList<PcSlot> MultiSlots =
        [PcSlot.Ram, PcSlot.Storage, PcSlot.Gpu, PcSlot.Monitor, PcSlot.Peripheral, PcSlot.Software, PcSlot.Service];

    private readonly List<PcBuildLine> _lines = new();

    private PcBuild()
    {
    }

    public PcBuild(Guid tenantId, Guid branchId, string number, string name, Guid? customerId, DateOnly validUntil, Guid userId,
        DateTimeOffset createdAt)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        Number = Guard.Text(number, "El número del armado", 40);
        Name = Guard.Text(name, "El nombre del armado", 150);
        CustomerId = Guard.NotEmptyIfPresent(customerId, nameof(customerId));
        ValidUntil = validUntil;
        CreatedByUserId = Guard.NotEmpty(userId, nameof(userId));
        CreatedAt = createdAt.ToUniversalTime();
        Status = PcBuildStatus.Draft;
    }

    public Guid BranchId { get; private set; }

    public string Number { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public Guid? CustomerId { get; private set; }

    /// <summary>Último día en que vale la cotización (inclusive).</summary>
    public DateOnly ValidUntil { get; private set; }

    public PcBuildStatus Status { get; private set; }

    /// <summary>Se cotizó con errores de compatibilidad porque el usuario lo confirmó explícitamente (regla T-06).</summary>
    public bool QuotedWithErrors { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? QuotedAt { get; private set; }

    /// <summary>Venta de M-INV que lo cobró (de la misma sucursal).</summary>
    public Guid? InvoiceId { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<PcBuildLine> Lines => _lines;

    /// <summary>Total derivado de las líneas (no se guarda).</summary>
    public decimal Total => _lines.Sum(l => l.Subtotal);

    public void Rename(string name, Guid? customerId)
    {
        EnsureEditable();
        Name = Guard.Text(name, "El nombre del armado", 150);
        CustomerId = Guard.NotEmptyIfPresent(customerId, nameof(customerId));
    }

    public PcBuildLine AddLine(PcSlot slot, Guid variantId, int quantity, decimal quotedUnitPrice)
    {
        EnsureEditable();
        Guard.Defined(slot, "La ranura");
        Guard.That(quantity is >= 1 and <= MaxQuantity, "pcbuild.quantity", $"La cantidad va de 1 a {MaxQuantity}.");
        Guard.That(MultiSlots.Contains(slot) || _lines.All(l => l.Slot != slot), "pcbuild.slot",
            $"El armado ya tiene {PcCompatibility.SlotName(slot)}.");
        var line = new PcBuildLine(TenantId, BranchId, Id, slot, variantId, quantity, quotedUnitPrice);
        _lines.Add(line);
        return line;
    }

    public void RemoveLine(Guid lineId)
    {
        EnsureEditable();
        Guard.That(_lines.RemoveAll(l => l.Id == lineId) == 1, "pcbuild.line", "La pieza no está en el armado.");
    }

    /// <summary>
    /// Emite la cotización: congela precios y vigencia (<paramref name="validUntil"/> no puede ser anterior a hoy). Con
    /// errores de compatibilidad solo si el usuario lo confirma (<paramref name="acceptIncompatible"/>), y queda marcado.
    /// </summary>
    public void Quote(DateOnly validUntil, DateOnly today, PcCompatibilityReport compatibility, bool acceptIncompatible, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(compatibility);
        EnsureEditable();
        Guard.That(_lines.Count > 0, "pcbuild.empty", "El armado no tiene piezas.");
        Guard.That(validUntil >= today, "pcbuild.valid_until", "La vigencia de la cotización no puede terminar antes de hoy.");
        Guard.That(compatibility.IsCompatible || acceptIncompatible, "pcbuild.incompatible",
            $"El armado tiene {compatibility.Errors} error(es) de compatibilidad: confírmelo explícitamente para cotizarlo igual.");
        ValidUntil = validUntil;
        QuotedWithErrors = !compatibility.IsCompatible;
        QuotedAt = now.ToUniversalTime();
        Status = PcBuildStatus.Quoted;
    }

    /// <summary>Cobrado en la caja: una cotización vencida ya no se vende a su precio (se vuelve a armar y cotizar).</summary>
    public void MarkSold(Guid invoiceId, DateOnly today)
    {
        Guard.That(Status is PcBuildStatus.Draft or PcBuildStatus.Quoted, "pcbuild.state",
            $"El armado {Number} está {Describe(Status)}: ya no se puede vender.");
        Guard.That(!IsExpiredOn(today), "pcbuild.expired", $"La cotización {Number} venció el {ValidUntil:dd/MM/yyyy}: cotícela de nuevo.");
        InvoiceId = Guard.NotEmpty(invoiceId, nameof(invoiceId));
        Status = PcBuildStatus.Sold;
    }

    public void Cancel()
    {
        Guard.That(Status is PcBuildStatus.Draft or PcBuildStatus.Quoted, "pcbuild.state",
            $"El armado {Number} está {Describe(Status)}: ya no se puede anular.");
        Status = PcBuildStatus.Cancelled;
    }

    /// <summary>Una cotización está vencida desde el día siguiente a su vigencia.</summary>
    public bool IsExpiredOn(DateOnly today) => Status == PcBuildStatus.Quoted && today > ValidUntil;

    public static string Describe(PcBuildStatus status) => status switch
    {
        PcBuildStatus.Draft => "en borrador",
        PcBuildStatus.Quoted => "cotizado",
        PcBuildStatus.Sold => "vendido",
        PcBuildStatus.Cancelled => "anulado",
        _ => status.ToString(),
    };

    private void EnsureEditable() =>
        Guard.That(Status == PcBuildStatus.Draft, "pcbuild.not_draft", "Solo se modifica un armado en borrador.");
}

/// <summary>V4.2 · Pieza de un armado con su precio cotizado.</summary>
public sealed class PcBuildLine : Entity, IBranchScoped
{
    private PcBuildLine()
    {
    }

    internal PcBuildLine(Guid tenantId, Guid branchId, Guid pcBuildId, PcSlot slot, Guid variantId, int quantity, decimal quotedUnitPrice)
        : base(tenantId)
    {
        BranchId = branchId;
        PcBuildId = pcBuildId;
        Slot = Guard.Defined(slot, "La ranura");
        VariantId = Guard.NotEmpty(variantId, nameof(variantId));
        Quantity = quantity;
        QuotedUnitPrice = decimal.Round(Guard.NonNegative(quotedUnitPrice, "El precio cotizado"), 2, MidpointRounding.AwayFromZero);
    }

    public Guid BranchId { get; private set; }

    public Guid PcBuildId { get; private set; }

    public PcSlot Slot { get; private set; }

    public Guid VariantId { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>Precio unitario congelado al cotizar (con impuestos, como la lista de precios).</summary>
    public decimal QuotedUnitPrice { get; private set; }

    public decimal Subtotal => decimal.Round(Quantity * QuotedUnitPrice, 2, MidpointRounding.AwayFromZero);
}
