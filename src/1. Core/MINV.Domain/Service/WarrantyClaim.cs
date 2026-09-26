using MINV.Domain.Common;

namespace MINV.Domain.Service;

/// <summary>V4.2 · Estado de un caso de garantía (RMA). Se guarda como texto.</summary>
public enum WarrantyClaimStatus
{
    /// <summary>El equipo entró a la tienda con la falla reportada.</summary>
    Received,

    /// <summary>Técnico revisando.</summary>
    Diagnosing,

    /// <summary>Enviado al proveedor o al servicio técnico de la marca.</summary>
    SentToSupplier,

    /// <summary>Reparado: listo para entregar.</summary>
    Repaired,

    /// <summary>Se entregó una unidad nueva en reemplazo (sale del stock).</summary>
    Replaced,

    /// <summary>Garantía rechazada (daño físico, vencida, sellos rotos…).</summary>
    Rejected,

    /// <summary>Equipo entregado al cliente: caso cerrado.</summary>
    Delivered,
}

/// <summary>V4.2 · Acción de la bitácora de un caso RMA. Se guarda como texto.</summary>
public enum WarrantyClaimAction
{
    Opened,
    StatusChanged,
    NoteAdded,
    ReplacementIssued,
    Closed,
}

/// <summary>
/// V4.2 · Caso de garantía (RMA) de una unidad vendida: número RMA-CM-000001, serie, cliente, venta original, falla,
/// estado y resolución (regla T-05). La vigencia de la garantía (fecha de la venta + meses del producto) se calcula al
/// consultar y NO se guarda (T-04); el caso guarda la DECISIÓN tomada al abrirlo: cubierto por la garantía o servicio con
/// cargo (<see cref="IsInWarranty"/> = false, marcado explícitamente). El estado cambia SOLO con <see cref="MoveTo"/> según
/// la tabla de transiciones y cada cambio (apertura, estado, reemplazo, nota, cierre) agrega su fila a la bitácora
/// append-only <see cref="History"/>.
/// </summary>
public sealed class WarrantyClaim : Entity, IBranchScoped, IConcurrencyAware, IAggregateRoot
{
    /// <summary>Tabla de transiciones: Recibido → Diagnóstico/Rechazado; Diagnóstico → Proveedor/Reparado/Reemplazado/
    /// Rechazado; Proveedor → Reparado/Reemplazado/Rechazado; Reparado/Reemplazado/Rechazado → Entregado (cierre).</summary>
    public static readonly IReadOnlyDictionary<WarrantyClaimStatus, IReadOnlyList<WarrantyClaimStatus>> Transitions =
        new Dictionary<WarrantyClaimStatus, IReadOnlyList<WarrantyClaimStatus>>
        {
            [WarrantyClaimStatus.Received] = [WarrantyClaimStatus.Diagnosing, WarrantyClaimStatus.Rejected],
            [WarrantyClaimStatus.Diagnosing] = [WarrantyClaimStatus.SentToSupplier, WarrantyClaimStatus.Repaired, WarrantyClaimStatus.Replaced,
                WarrantyClaimStatus.Rejected],
            [WarrantyClaimStatus.SentToSupplier] = [WarrantyClaimStatus.Repaired, WarrantyClaimStatus.Replaced, WarrantyClaimStatus.Rejected],
            [WarrantyClaimStatus.Repaired] = [WarrantyClaimStatus.Delivered],
            [WarrantyClaimStatus.Replaced] = [WarrantyClaimStatus.Delivered],
            [WarrantyClaimStatus.Rejected] = [WarrantyClaimStatus.Delivered],
            [WarrantyClaimStatus.Delivered] = [],
        };

    private readonly List<WarrantyClaimEvent> _history = new();

    private WarrantyClaim()
    {
    }

    public WarrantyClaim(Guid tenantId, Guid branchId, string number, Guid serialNumberId, Guid customerId, Guid? invoiceId, string issue,
        bool isInWarranty, Guid? supplierId, Guid userId, DateTimeOffset receivedAt)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        Number = Guard.Text(number, "El número del caso", 40);
        SerialNumberId = Guard.NotEmpty(serialNumberId, nameof(serialNumberId));
        CustomerId = Guard.NotEmpty(customerId, nameof(customerId));
        InvoiceId = Guard.NotEmptyIfPresent(invoiceId, nameof(invoiceId));
        Issue = Guard.Text(issue, "La falla reportada", 500, minLength: 5);
        IsInWarranty = isInWarranty;
        SupplierId = Guard.NotEmptyIfPresent(supplierId, nameof(supplierId));
        OpenedByUserId = Guard.NotEmpty(userId, nameof(userId));
        ReceivedAt = receivedAt.ToUniversalTime();
        Status = WarrantyClaimStatus.Received;
        Log(WarrantyClaimAction.Opened, isInWarranty ? "Recibido en garantía" : "Recibido FUERA de garantía (servicio con cargo)", userId,
            receivedAt);
    }

    public Guid BranchId { get; private set; }

    public string Number { get; private set; } = string.Empty;

    /// <summary>Unidad del caso (el producto sale de la serie: no se repite aquí, regla A-06).</summary>
    public Guid SerialNumberId { get; private set; }

    public Guid CustomerId { get; private set; }

    /// <summary>Venta original, de la MISMA sucursal (FK compuesta con la sucursal, regla B-02). Si la unidad se vendió en
    /// otra sucursal, el vínculo con la venta es la serie y su bitácora.</summary>
    public Guid? InvoiceId { get; private set; }

    public string Issue { get; private set; } = string.Empty;

    /// <summary>Cubierto por la garantía al abrirlo (false = servicio o reparación con cargo, marcado explícitamente).</summary>
    public bool IsInWarranty { get; private set; }

    public Guid? SupplierId { get; private set; }

    public WarrantyClaimStatus Status { get; private set; }

    public string? Resolution { get; private set; }

    /// <summary>Unidad entregada en reemplazo (si hubo).</summary>
    public Guid? ReplacementSerialId { get; private set; }

    public Guid OpenedByUserId { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    /// <summary>Bitácora del caso (solo las filas agregadas en esta sesión si no se cargó).</summary>
    public IReadOnlyCollection<WarrantyClaimEvent> History => _history;

    public bool IsOpen => Status != WarrantyClaimStatus.Delivered;

    public bool CanMoveTo(WarrantyClaimStatus next) => Transitions[Status].Contains(next);

    /// <summary>Avanza el caso según la tabla de transiciones. La resolución es obligatoria al reparar, reemplazar o
    /// rechazar; el proveedor, al enviarlo al proveedor; el reemplazo exige registrar antes la unidad nueva. Entregado
    /// cierra el caso.</summary>
    public WarrantyClaimEvent MoveTo(WarrantyClaimStatus next, string? resolution, Guid? supplierId, Guid userId, DateTimeOffset now,
        string? note = null)
    {
        Guard.That(CanMoveTo(next), "rma.transition", $"Un caso {Describe(Status)} no puede pasar a {Describe(next)}.");
        if (next is WarrantyClaimStatus.Repaired or WarrantyClaimStatus.Replaced or WarrantyClaimStatus.Rejected)
        {
            Resolution = Guard.Text(resolution, "La resolución", 500);
        }
        if (next == WarrantyClaimStatus.SentToSupplier)
        {
            SupplierId = Guard.NotEmptyIfPresent(supplierId, nameof(supplierId)) ?? SupplierId;
            Guard.That(SupplierId is not null, "rma.supplier", "Indique a qué proveedor se envía el equipo.");
        }
        Guard.That(next != WarrantyClaimStatus.Replaced || ReplacementSerialId is not null, "rma.replacement",
            "Registre primero la unidad de reemplazo.");
        Status = next;
        if (next == WarrantyClaimStatus.Delivered)
        {
            ClosedAt = now.ToUniversalTime();
            return Log(WarrantyClaimAction.Closed, note ?? "Entregado al cliente", userId, now);
        }
        var detail = next is WarrantyClaimStatus.Repaired or WarrantyClaimStatus.Replaced or WarrantyClaimStatus.Rejected
            ? $"{Describe(next)}: {Resolution}"
            : Describe(next);
        return Log(WarrantyClaimAction.StatusChanged, note is null ? detail : $"{detail} · {note}", userId, now);
    }

    /// <summary>Registra la unidad nueva entregada en reemplazo (sale del stock con su movimiento de reposición).</summary>
    public WarrantyClaimEvent SetReplacement(Guid replacementSerialId, Guid userId, DateTimeOffset now, string? note = null)
    {
        Guard.That(Status is WarrantyClaimStatus.Diagnosing or WarrantyClaimStatus.SentToSupplier, "rma.replacement_state",
            "El reemplazo se registra durante el diagnóstico o con el equipo en el proveedor.");
        Guard.That(ReplacementSerialId is null, "rma.replacement_twice", "El caso ya tiene una unidad de reemplazo.");
        Guard.That(replacementSerialId != SerialNumberId, "rma.replacement_same", "El reemplazo debe ser otra unidad.");
        ReplacementSerialId = Guard.NotEmpty(replacementSerialId, nameof(replacementSerialId));
        return Log(WarrantyClaimAction.ReplacementIssued, note ?? "Unidad de reemplazo entregada", userId, now);
    }

    /// <summary>Nota del técnico o de la atención (solo con el caso abierto).</summary>
    public WarrantyClaimEvent AddNote(string note, Guid userId, DateTimeOffset now)
    {
        Guard.That(IsOpen, "rma.closed", $"El caso {Number} ya está cerrado.");
        return Log(WarrantyClaimAction.NoteAdded, Guard.Text(note, "La nota", 500), userId, now);
    }

    public static string Describe(WarrantyClaimStatus status) => status switch
    {
        WarrantyClaimStatus.Received => "recibido",
        WarrantyClaimStatus.Diagnosing => "en diagnóstico",
        WarrantyClaimStatus.SentToSupplier => "enviado al proveedor",
        WarrantyClaimStatus.Repaired => "reparado",
        WarrantyClaimStatus.Replaced => "reemplazado",
        WarrantyClaimStatus.Rejected => "rechazado",
        WarrantyClaimStatus.Delivered => "entregado",
        _ => status.ToString(),
    };

    private WarrantyClaimEvent Log(WarrantyClaimAction action, string? note, Guid userId, DateTimeOffset at)
    {
        var row = new WarrantyClaimEvent(TenantId, BranchId, Id, action, Status, note, userId, at);
        _history.Add(row);
        return row;
    }
}

/// <summary>V4.2 · Bitácora inmutable de un caso RMA.</summary>
public sealed class WarrantyClaimEvent : Entity, IBranchScoped, IAppendOnly
{
    private WarrantyClaimEvent()
    {
    }

    public WarrantyClaimEvent(Guid tenantId, Guid branchId, Guid claimId, WarrantyClaimAction action, WarrantyClaimStatus status, string? note,
        Guid userId, DateTimeOffset occurredAt)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        ClaimId = Guard.NotEmpty(claimId, nameof(claimId));
        Action = Guard.Defined(action, "La acción");
        Status = Guard.Defined(status, "El estado");
        Note = Guard.OptionalText(note is { Length: > 500 } ? note[..500] : note, "La nota", 500);
        UserId = Guard.NotEmpty(userId, nameof(userId));
        OccurredAt = occurredAt.ToUniversalTime();
    }

    public Guid BranchId { get; private set; }

    public Guid ClaimId { get; private set; }

    public WarrantyClaimAction Action { get; private set; }

    /// <summary>Estado del caso después del hecho.</summary>
    public WarrantyClaimStatus Status { get; private set; }

    public string? Note { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}
