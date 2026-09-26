using MINV.Domain.Common;

namespace MINV.Domain.Billing;

/// <summary>
/// V4.1 · Evento significativo (contingencia): corte de internet o del servicio del SIN (fuera de línea automático) o
/// corte de energía / falla de software o hardware (contingencia manual con facturas CAFC). Orden de la recuperación:
/// CUFD nuevo → registroEventoSignificativo (cufd = nuevo, cufdEvento = el vigente al empezar) → paquetes.
/// </summary>
public sealed class SignificantEvent : Entity, IBranchScoped, IConcurrencyAware
{
    private SignificantEvent()
    {
    }

    public SignificantEvent(Guid tenantId, Guid branchId, Guid pointOfSaleId, int environment, SignificantEventKind kind, int eventCode,
        string description, DateTime startedAt, Guid eventCufdId, Guid? contingencyCodeId, DateTimeOffset createdAt, Guid? userId)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        PointOfSaleId = Guard.NotEmpty(pointOfSaleId, nameof(pointOfSaleId));
        Environment = environment;
        Kind = kind;
        Guard.That(eventCode is >= 1 and <= 99, "siat.event_code", "El código de evento sale del catálogo de eventos significativos.");
        EventCode = eventCode;
        Description = Guard.Text(description, "La descripción del evento", 500);
        StartedAt = DateTime.SpecifyKind(startedAt, DateTimeKind.Unspecified);
        EventCufdId = Guard.NotEmpty(eventCufdId, nameof(eventCufdId));
        ContingencyCodeId = Guard.NotEmptyIfPresent(contingencyCodeId, nameof(contingencyCodeId));
        Guard.That(kind == SignificantEventKind.ManualCafc || contingencyCodeId is null, "siat.event_cafc",
            "Solo la contingencia manual usa un CAFC.");
        Status = SignificantEventStatus.Open;
        CreatedAt = createdAt;
        CreatedByUserId = Guard.NotEmptyIfPresent(userId, nameof(userId));
    }

    public Guid BranchId { get; private set; }

    public Guid PointOfSaleId { get; private set; }

    public int Environment { get; private set; }

    public SignificantEventKind Kind { get; private set; }

    /// <summary>Código del catálogo «Eventos Significativos» (se elige por descripción: la numeración cambió entre versiones).</summary>
    public int EventCode { get; private set; }

    public string Description { get; private set; } = string.Empty;

    /// <summary>Inicio en hora fiscal (fechaHoraInicioEvento).</summary>
    public DateTime StartedAt { get; private set; }

    public DateTime? EndedAt { get; private set; }

    /// <summary>CUFD vigente al empezar el evento (cufdEvento): con él se emiten las facturas fuera de línea.</summary>
    public Guid EventCufdId { get; private set; }

    /// <summary>CUFD NUEVO pedido después del evento (cufd del registro y de los paquetes).</summary>
    public Guid? SendCufdId { get; private set; }

    public Guid? ContingencyCodeId { get; private set; }

    /// <summary>codigoRecepcionEventoSignificativo: va como codigoEvento en los paquetes.</summary>
    public string? ReceptionCode { get; private set; }

    public SignificantEventStatus Status { get; private set; }

    public DateTimeOffset? RegisteredAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedByUserId { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    /// <summary>Hasta cuándo se puede registrar el evento (48 h desde el fin).</summary>
    public DateTime? RegistrationDeadline => EndedAt?.Add(FiscalRules.EventRegistrationWindow);

    /// <summary>Hasta cuándo se transcriben y envían las facturas manuales (72 h desde el fin).</summary>
    public DateTime? TranscriptionDeadline => Kind == SignificantEventKind.ManualCafc ? EndedAt?.Add(FiscalRules.CafcTranscriptionWindow) : null;

    /// <summary>¿La fecha fiscal cae dentro del evento? (las facturas fuera de línea deben estarlo: errores 1040/2001).</summary>
    public bool Covers(DateTime fiscalTime) => fiscalTime >= StartedAt && (EndedAt is null || fiscalTime <= EndedAt);

    /// <summary>Fin del evento (la comunicación volvió o el usuario declara el fin de la contingencia).</summary>
    public void Close(DateTime endedAt)
    {
        Guard.That(Status == SignificantEventStatus.Open, "siat.event_state", "El evento ya estaba cerrado.");
        var end = DateTime.SpecifyKind(endedAt, DateTimeKind.Unspecified);
        Guard.That(end > StartedAt, "siat.event_range", "El fin del evento debe ser posterior a su inicio.");
        EndedAt = end;
        Status = SignificantEventStatus.Closed;
    }

    /// <summary>El SIN registró el evento (<c>registroEventoSignificativo</c>).</summary>
    public void Register(Guid sendCufdId, string receptionCode, DateTimeOffset now)
    {
        Guard.That(Status == SignificantEventStatus.Closed, "siat.event_state", "Solo se registra un evento cerrado.");
        SendCufdId = Guard.NotEmpty(sendCufdId, nameof(sendCufdId));
        ReceptionCode = Guard.Text(receptionCode, "El código de recepción del evento", 100);
        RegisteredAt = now;
        Status = SignificantEventStatus.Registered;
    }

    public void MarkPackagesSent()
    {
        Guard.That(Status is SignificantEventStatus.Registered or SignificantEventStatus.PackagesSent, "siat.event_state",
            "El evento debe estar registrado para enviar sus paquetes.");
        Status = SignificantEventStatus.PackagesSent;
    }

    /// <summary>Todos los paquetes validados (o con observaciones).</summary>
    public void Reconcile(bool withObservations)
    {
        Guard.That(Status is SignificantEventStatus.PackagesSent or SignificantEventStatus.Registered, "siat.event_state",
            "El evento no tiene paquetes enviados.");
        Status = withObservations ? SignificantEventStatus.WithObservations : SignificantEventStatus.Reconciled;
    }
}

/// <summary>V4.1 · Paquete de contingencia: GZIP(TAR) de hasta 500 documentos del mismo sector, evento y CAFC.</summary>
public sealed class FiscalPackage : Entity, IBranchScoped, IConcurrencyAware
{
    private FiscalPackage()
    {
    }

    public FiscalPackage(Guid tenantId, Guid branchId, Guid significantEventId, Guid pointOfSaleId, Guid sendCufdId, int documentSector,
        int documentType, string? cafc, string sha256, DateTimeOffset sentAt)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        SignificantEventId = Guard.NotEmpty(significantEventId, nameof(significantEventId));
        PointOfSaleId = Guard.NotEmpty(pointOfSaleId, nameof(pointOfSaleId));
        SendCufdId = Guard.NotEmpty(sendCufdId, nameof(sendCufdId));
        DocumentSector = documentSector;
        DocumentType = documentType;
        Cafc = Guard.OptionalText(cafc, "El CAFC", 50);
        Sha256 = Guard.Text(sha256, "La huella del paquete", 64, 64).ToLowerInvariant();
        SentAt = sentAt;
        Status = FiscalPackageStatus.Sent;
    }

    public Guid BranchId { get; private set; }

    public Guid SignificantEventId { get; private set; }

    public Guid PointOfSaleId { get; private set; }

    public Guid SendCufdId { get; private set; }

    public int DocumentSector { get; private set; }

    public int DocumentType { get; private set; }

    public string? Cafc { get; private set; }

    public string Sha256 { get; private set; } = string.Empty;

    public string? ReceptionCode { get; private set; }

    public FiscalPackageStatus Status { get; private set; }

    public DateTimeOffset SentAt { get; private set; }

    public DateTimeOffset? ValidatedAt { get; private set; }

    public int? LastSiatCode { get; private set; }

    /// <summary>Mensajes de la validación (JSON).</summary>
    public string? Messages { get; private set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint RowVersion { get; private set; }

    public void Accepted(string receptionCode, int siatCode)
    {
        ReceptionCode = Guard.Text(receptionCode, "El código de recepción del paquete", 100);
        LastSiatCode = siatCode;
    }

    public void Validated(int siatCode, DateTimeOffset now, string? messages)
    {
        Guard.That(Status == FiscalPackageStatus.Sent, "siat.package_state", "El paquete ya tiene resultado.");
        Status = FiscalPackageStatus.Validated;
        LastSiatCode = siatCode;
        ValidatedAt = now;
        Messages = messages;
    }

    public void Observed(int siatCode, DateTimeOffset now, string? messages)
    {
        Guard.That(Status == FiscalPackageStatus.Sent, "siat.package_state", "El paquete ya tiene resultado.");
        Status = FiscalPackageStatus.Observed;
        LastSiatCode = siatCode;
        ValidatedAt = now;
        Messages = messages;
    }

    public void Rejected(int siatCode, DateTimeOffset now, string? messages)
    {
        Guard.That(Status == FiscalPackageStatus.Sent, "siat.package_state", "El paquete ya tiene resultado.");
        Status = FiscalPackageStatus.Rejected;
        LastSiatCode = siatCode;
        ValidatedAt = now;
        Messages = messages;
    }
}

/// <summary>V4.1 · Código de Autorización de Facturas por Contingencia (talonario de facturas manuales) de una sucursal.</summary>
public sealed class ContingencyCode : Entity, IBranchScoped
{
    private ContingencyCode()
    {
    }

    public ContingencyCode(Guid tenantId, Guid branchId, int documentSector, string code, long numberFrom, long numberTo, DateOnly? validUntil)
        : base(tenantId)
    {
        BranchId = Guard.NotEmpty(branchId, nameof(branchId));
        Guard.That(documentSector is >= 1 and <= 99, "siat.sector", "El documento sector va de 1 a 99.");
        DocumentSector = documentSector;
        Code = Guard.Text(code, "El CAFC", 50);
        Guard.That(numberFrom > 0 && numberTo >= numberFrom, "siat.cafc_range", "El rango del talonario no es válido.");
        NumberFrom = numberFrom;
        NumberTo = numberTo;
        ValidUntil = validUntil;
        IsActive = true;
    }

    public Guid BranchId { get; private set; }

    public int DocumentSector { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public long NumberFrom { get; private set; }

    public long NumberTo { get; private set; }

    public DateOnly? ValidUntil { get; private set; }

    public bool IsActive { get; private set; }

    public bool Contains(long number) => number >= NumberFrom && number <= NumberTo;

    public void Deactivate() => IsActive = false;
}
