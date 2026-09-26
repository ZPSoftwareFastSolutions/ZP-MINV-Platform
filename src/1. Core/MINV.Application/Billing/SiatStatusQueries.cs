using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Domain.Catalog;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Application.Billing;

// =====================================================================================================================
// V4.1 · Tablero «Estado SIAT» (investigación 01 §11.37, 03 §17.25, 08 P1-25/P1-33): por sucursal y punto de venta, modo,
// CUIS, CUFD, documentos pendientes y fuera de línea, eventos con sus plazos, y ALERTAS con cuenta regresiva.
// =====================================================================================================================

/// <summary>V4.1 · Severidades de las alertas del tablero.</summary>
public static class SiatAlertSeverity
{
    public const string Danger = "danger";
    public const string Warning = "warning";
    public const string Info = "info";

    internal static int Rank(string severity) => severity switch
    {
        Danger => 0,
        Warning => 1,
        _ => 2,
    };
}

/// <summary>V4.1 · Estado de los puntos de venta del SIN (lo usan el tablero y el alta de un punto de venta).</summary>
internal static class SiatStatusBuilder
{
    /// <summary>Instante (con zona) de una hora fiscal sin zona.</summary>
    public static DateTimeOffset At(DateTime fiscal, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(fiscal, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }

    public static async Task<IReadOnlyList<SiatPointOfSaleStatus>> PointsAsync(IMinvDbContext db, int environment, TimeZoneInfo zone, Guid? onlyPointId,
        DateTimeOffset now, CancellationToken ct)
    {
        var points = await db.Set<SiatPointOfSale>().AsNoTracking()
            .Where(p => p.Environment == environment && (onlyPointId == null || p.Id == onlyPointId)).ToListAsync(ct);
        if (points.Count == 0)
        {
            return [];
        }
        var ids = points.Select(p => p.Id).ToList();
        var branches = await db.Set<Branch>().AsNoTracking().ToDictionaryAsync(b => b.Id, ct);
        var mappings = await db.Set<SiatBranch>().AsNoTracking().ToDictionaryAsync(b => b.BranchId, b => b.SiatCode, ct);
        var registers = await db.Set<PosRegister>().AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Code, ct);
        var cuis = (await db.Set<SiatCuis>().AsNoTracking().Where(c => ids.Contains(c.PointOfSaleId)).ToListAsync(ct))
            .GroupBy(c => c.PointOfSaleId).ToDictionary(g => g.Key, g => g.MaxBy(c => c.ObtainedAt)!);
        // Solo los CUFD recientes: uno más viejo ya no sirve ni fuera de línea (72 h)
        var since = now.AddDays(-7);
        var cufds = (await db.Set<SiatCufd>().AsNoTracking().Where(c => ids.Contains(c.PointOfSaleId) && c.ObtainedAt >= since).ToListAsync(ct))
            .GroupBy(c => c.PointOfSaleId).ToDictionary(g => g.Key, g => g.MaxBy(c => c.ObtainedAt)!);
        var documents = await db.Set<FiscalDocument>().AsNoTracking()
            .Where(d => ids.Contains(d.PointOfSaleId) && (d.Status == FiscalDocumentStatus.Pending || d.Status == FiscalDocumentStatus.NoResponse
                                                          || d.Status == FiscalDocumentStatus.Offline))
            .GroupBy(d => new { d.PointOfSaleId, d.Status }).Select(g => new { g.Key.PointOfSaleId, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        var events = await db.Set<SignificantEvent>().AsNoTracking()
            .Where(e => ids.Contains(e.PointOfSaleId) && e.Status != SignificantEventStatus.Reconciled && e.Status != SignificantEventStatus.WithObservations)
            .ToListAsync(ct);
        var eventRows = (await SiatEventRows.BuildAsync(db, events, ct)).ToDictionary(e => e.Id);
        return points
            .OrderBy(p => mappings.GetValueOrDefault(p.BranchId)).ThenBy(p => branches.TryGetValue(p.BranchId, out var b) ? b.Code : string.Empty)
            .ThenBy(p => p.Code)
            .Select(p =>
            {
                var branch = branches.GetValueOrDefault(p.BranchId);
                var open = events.Where(e => e.PointOfSaleId == p.Id).OrderByDescending(e => e.StartedAt).FirstOrDefault();
                return new SiatPointOfSaleStatus(p.Id, p.BranchId, branch?.Code ?? "?", branch?.Name ?? "?", mappings.GetValueOrDefault(p.BranchId),
                    p.Environment, p.Code, p.Name, p.PosRegisterId is { } r ? registers.GetValueOrDefault(r) : null, p.Mode, p.ModeSince, p.LastContactAt,
                    p.LastError, p.RetryAt, cuis.GetValueOrDefault(p.Id)?.ValidUntil, cufds.GetValueOrDefault(p.Id)?.ValidUntil,
                    cufds.GetValueOrDefault(p.Id)?.ObtainedAt,
                    documents.Where(d => d.PointOfSaleId == p.Id && d.Status != FiscalDocumentStatus.Offline).Sum(d => d.Count),
                    documents.Where(d => d.PointOfSaleId == p.Id && d.Status == FiscalDocumentStatus.Offline).Sum(d => d.Count),
                    p.IsClosed, open is null ? null : eventRows.GetValueOrDefault(open.Id));
            }).ToList();
    }
}

public sealed class GetSiatStatusHandler(IMinvDbContext db, IClock clock) : IRequestHandler<GetSiatStatusQuery, SiatStatusView>
{
    /// <summary>Días antes del vencimiento del token delegado en que se avisa.</summary>
    public const int TokenWarningDays = 7;

    public async Task<SiatStatusView> Handle(GetSiatStatusQuery request, CancellationToken ct)
    {
        var lookups = new BillingLookups(db, null, clock);
        var settings = await db.Set<SiatSettings>().AsNoTracking().FirstOrDefaultAsync(ct);
        var zone = await lookups.ZoneAsync(ct);
        var now = clock.UtcNow;
        var environment = settings?.Environment ?? SiatCodes.EnvironmentTest;
        var fiscalNow = settings is null ? FiscalRules.ToFiscalTime(now, zone) : lookups.FiscalNow(settings, zone);
        var today = fiscalNow.Date;
        var profile = await db.Set<SiatEnvironmentProfile>().AsNoTracking().FirstOrDefaultAsync(p => p.Environment == environment, ct);
        var lastCatalogSync = await db.Set<SiatSyncRun>().AsNoTracking()
            .Where(r => r.Environment == environment && r.Error == null && r.Catalog != SiatCatalogNames.DateTime)
            .OrderByDescending(r => r.OccurredAt).Select(r => (DateTimeOffset?)r.OccurredAt).FirstOrDefaultAsync(ct);
        var points = await SiatStatusBuilder.PointsAsync(db, environment, zone, null, now, ct);
        var active = points.Where(p => !p.IsClosed).ToList();
        var alerts = new List<SiatAlert>();

        // --- configuración, token y ambiente
        if (settings is null)
        {
            alerts.Add(new(SiatAlertSeverity.Danger, "Falta configurar la facturación SIAT",
                "Cargue el NIT, la razón social y el código de sistema en Configuración › Facturación SIAT."));
        }
        else if (!settings.IsEnabled)
        {
            alerts.Add(new(SiatAlertSeverity.Info, "La facturación SIAT está desactivada",
                "Las ventas se registran sin documento fiscal hasta que la active en Configuración › Facturación SIAT."));
        }
        if (profile is null)
        {
            alerts.Add(new(SiatAlertSeverity.Danger, $"Falta la conexión del ambiente {environment}",
                "Cargue las URL de los servicios del SIN, la URL del QR y el token delegado."));
        }
        else if (!profile.HasToken)
        {
            alerts.Add(new(SiatAlertSeverity.Danger, "Falta el token delegado del SIN",
                "Genérelo en el Portal SIAT (Token Delegado) y cárguelo en Configuración › Facturación SIAT."));
        }
        else if (profile.TokenValidUntil is { } until)
        {
            var expires = SiatStatusBuilder.At(until.ToDateTime(new TimeOnly(23, 59, 59)), zone);
            var days = until.DayNumber - DateOnly.FromDateTime(today).DayNumber;
            if (days < 0)
            {
                alerts.Add(new(SiatAlertSeverity.Danger, "El token delegado VENCIÓ",
                    $"Venció el {until:dd/MM/yyyy}: genere uno nuevo en el Portal SIAT (inactivar con X › Generar Nuevo Token) y cárguelo.", expires));
            }
            else if (days <= TokenWarningDays)
            {
                alerts.Add(new(SiatAlertSeverity.Warning, $"El token delegado vence en {days} día(s)",
                    $"Vence el {until:dd/MM/yyyy}: genere el nuevo en el Portal SIAT antes de esa fecha.", expires));
            }
        }
        if (environment == SiatCodes.EnvironmentTest)
        {
            alerts.Add(new(SiatAlertSeverity.Info, "Ambiente de pruebas (piloto)",
                "Los documentos emitidos en el ambiente 2 no tienen valor legal: se imprimen con la marca «SIN VALOR LEGAL»."));
        }
        if (settings is not null && active.Count == 0)
        {
            alerts.Add(new(SiatAlertSeverity.Danger, "No hay puntos de venta del SIN",
                "Mapee las sucursales con su código del Padrón y ejecute «Preparar SIAT» (crea el punto 0 y pide CUIS y CUFD)."));
        }

        // --- por punto de venta: CUIS, CUFD, modo y eventos
        foreach (var p in active)
        {
            var label = $"{p.BranchCode} · punto {p.Code}";
            if (p.CuisValidUntil is not { } cuisUntil || cuisUntil <= now)
            {
                alerts.Add(new(SiatAlertSeverity.Danger, $"{label}: sin CUIS vigente", "Sin CUIS no se emite: solicítelo (Estado SIAT › CUIS).", p.CuisValidUntil));
            }
            else if (cuisUntil.AddDays(-5) <= now)
            {
                alerts.Add(new(SiatAlertSeverity.Warning, $"{label}: el CUIS vence pronto",
                    $"Vence el {SiatAdminSupport.Local(cuisUntil, zone)}: ya se puede renovar (y luego pedir un CUFD nuevo).", cuisUntil));
            }
            if (p.CufdValidUntil is not { } cufdUntil || cufdUntil <= now)
            {
                var offlineUsable = p.CufdObtainedAt is { } obtained && obtained.Add(FiscalRules.CufdExtendedValidity) > now;
                alerts.Add(new(p.Mode == SiatConnectionMode.Online && !offlineUsable ? SiatAlertSeverity.Danger : SiatAlertSeverity.Warning,
                    $"{label}: CUFD vencido",
                    p.CufdValidUntil is null
                        ? "El punto no tiene CUFD: pídalo (Estado SIAT › CUFD o «Preparar SIAT»)."
                        : $"Venció el {SiatAdminSupport.Local(p.CufdValidUntil.Value, zone)}: pida el CUFD del día" +
                          (offlineUsable ? " (fuera de línea se puede seguir usando hasta 72 h desde su obtención)." : "."),
                    p.CufdObtainedAt?.Add(FiscalRules.CufdExtendedValidity)));
            }
            if (p.Mode != SiatConnectionMode.Online)
            {
                var mode = p.Mode switch
                {
                    SiatConnectionMode.Offline => "fuera de línea",
                    SiatConnectionMode.ManualContingency => "en contingencia manual",
                    _ => "recuperando la comunicación",
                };
                alerts.Add(new(SiatAlertSeverity.Warning, $"{label}: {mode} desde {SiatAdminSupport.Local(p.ModeSince, zone)}",
                    (p.RetryAt is { } retry ? $"Próximo intento de comunicación: {SiatAdminSupport.Local(retry, zone)}. " : string.Empty) +
                    (p.LastError is { Length: > 0 } error ? "Último error: " + error : "La caja sigue facturando fuera de línea."), p.RetryAt));
            }
            if (p.OpenEvent is { } ev)
            {
                AddEventAlerts(alerts, label, ev, fiscalNow, zone);
            }
        }

        // --- documentos y paquetes
        var packagesSent = await db.Set<FiscalPackage>().AsNoTracking().CountAsync(x => x.Status == FiscalPackageStatus.Sent, ct);
        if (packagesSent > 0)
        {
            alerts.Add(new(SiatAlertSeverity.Warning, $"{packagesSent} paquete(s) de contingencia sin validar",
                "El SIN todavía no confirmó su validación: el trabajo automático lo consulta hasta obtener el resultado."));
        }
        var statusCounts = await db.Set<FiscalDocument>().AsNoTracking().Where(d => d.Environment == environment)
            .GroupBy(d => d.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var noResponse = statusCounts.GetValueOrDefault(FiscalDocumentStatus.NoResponse);
        if (noResponse > 0)
        {
            alerts.Add(new(SiatAlertSeverity.Warning, $"{noResponse} documento(s) sin respuesta del SIN",
                "Se verifican por CUF al recuperar la comunicación (si el SIN los registró y se re-emitieron, se anulan los duplicados)."));
        }
        var duplicates = statusCounts.GetValueOrDefault(FiscalDocumentStatus.DuplicateToVoid);
        if (duplicates > 0)
        {
            alerts.Add(new(SiatAlertSeverity.Danger, $"{duplicates} documento(s) duplicado(s) por anular",
                "El SIN los registró y la venta ya tiene otro documento: anúlelos (Documentos fiscales › Anular)."));
        }
        var weekAgo = fiscalNow.AddDays(-7);
        var rejected = await db.Set<FiscalDocument>().AsNoTracking()
            .CountAsync(d => d.Environment == environment && d.IssuedAt >= weekAgo
                             && (d.Status == FiscalDocumentStatus.Rejected || d.Status == FiscalDocumentStatus.PackageRejected), ct);
        if (rejected > 0)
        {
            alerts.Add(new(SiatAlertSeverity.Warning, $"{rejected} documento(s) rechazado(s) en los últimos 7 días",
                "Revise los mensajes del SIN en Documentos fiscales: la venta recibe un documento nuevo."));
        }

        // --- sincronización del día y homologación
        if (settings is not null)
        {
            if (settings.ClockSyncedAt is not { } clockSynced || FiscalRules.ToFiscalTime(clockSynced, zone).Date != today)
            {
                alerts.Add(new(SiatAlertSeverity.Warning, "La hora no se sincronizó hoy con el SIN",
                    "La sincronización de fecha y hora es obligatoria a diario: ejecute «Preparar SIAT»."));
            }
            if (lastCatalogSync is not { } catalogs || FiscalRules.ToFiscalTime(catalogs, zone).Date != today)
            {
                alerts.Add(new(SiatAlertSeverity.Warning, "Los catálogos no se sincronizaron hoy",
                    "Se emite con la última copia local; la sincronización diaria es obligatoria («Preparar SIAT» o Sincronizar catálogos)."));
            }
        }
        var pendingProducts = await HomologationCounts.PendingProductsAsync(db, ct);
        if (pendingProducts > 0)
        {
            alerts.Add(new(SiatAlertSeverity.Warning, $"{pendingProducts} producto(s) activo(s) sin homologar",
                "No se facturan hasta tener actividad económica y código de producto del SIN (Facturación › Homologación)."));
        }

        // --- hoy (hora fiscal)
        var start = today;
        var end = today.AddDays(1);
        var todayDocuments = await db.Set<FiscalDocument>().AsNoTracking().Include(d => d.Lines).Include(d => d.NoteReference)
            .Where(d => d.Environment == environment && d.IssuedAt >= start && d.IssuedAt < end).ToListAsync(ct);
        var issuedToday = todayDocuments.Where(d => FiscalIssuedStatus.IsIssued(d.Status)).ToList();
        return new SiatStatusView(settings is not null, settings?.IsEnabled == true, environment, settings?.Nit, settings?.BusinessName,
            settings?.ClockSyncedAt, lastCatalogSync, profile?.TokenValidUntil, profile?.HasToken == true, points,
            alerts.OrderBy(a => SiatAlertSeverity.Rank(a.Severity)).ToList(),
            active.Sum(p => p.PendingDocuments), active.Sum(p => p.OfflineDocuments),
            points.Count(p => p.OpenEvent?.Status == SignificantEventStatus.Open), issuedToday.Count,
            issuedToday.Where(d => d.Kind == FiscalDocumentKind.Invoice).Sum(d => d.TotalAmount));
    }

    private static void AddEventAlerts(List<SiatAlert> alerts, string label, SignificantEventRow ev, DateTime fiscalNow, TimeZoneInfo zone)
    {
        var (kind, opened, registered) = ev.Kind == SignificantEventKind.ManualCafc
            ? ("Contingencia manual", "abierta", "registrada")
            : ("Evento fuera de línea", "abierto", "registrado");
        switch (ev.Status)
        {
            case SignificantEventStatus.Open:
                alerts.Add(new(ev.Kind == SignificantEventKind.ManualCafc ? SiatAlertSeverity.Warning : SiatAlertSeverity.Info,
                    $"{label}: {kind} {opened} desde {ev.StartedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}",
                    ev.Kind == SignificantEventKind.ManualCafc
                        ? $"«{ev.Description}» con el CAFC {ev.Cafc}: al terminar, transcriba las facturas manuales (72 h desde el fin)."
                        : $"«{ev.Description}»: {ev.Documents} documento(s) emitidos fuera de línea, se envían en paquetes al recuperar."));
                break;
            case SignificantEventStatus.Closed when ev.RegistrationDeadline is { } deadline:
            {
                var hours = (deadline - fiscalNow).TotalHours;
                alerts.Add(new(hours <= 12 ? SiatAlertSeverity.Danger : SiatAlertSeverity.Warning,
                    $"{label}: {kind} sin registrar en el SIN",
                    hours > 0
                        ? $"Quedan {Math.Floor(hours).ToString(CultureInfo.InvariantCulture)} h para registrarlo y enviar sus paquetes (48 h desde el fin, hasta el " +
                          $"{deadline.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)})."
                        : $"Venció el plazo de 48 h ({deadline.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}): regístrelo cuanto antes.",
                    SiatStatusBuilder.At(deadline, zone)));
                break;
            }
            case SignificantEventStatus.Registered or SignificantEventStatus.PackagesSent:
                alerts.Add(new(SiatAlertSeverity.Info, $"{label}: {kind} {registered} ({ev.ReceptionCode})",
                    "Sus paquetes se envían y validan automáticamente.",
                    ev.RegistrationDeadline is { } d ? SiatStatusBuilder.At(d, zone) : null));
                break;
        }
        if (ev.TranscriptionDeadline is { } transcription && ev.Status is SignificantEventStatus.Closed or SignificantEventStatus.Registered)
        {
            var hours = (transcription - fiscalNow).TotalHours;
            alerts.Add(new(hours <= 12 ? SiatAlertSeverity.Danger : SiatAlertSeverity.Warning,
                $"{label}: transcripción de facturas manuales (CAFC {ev.Cafc})",
                hours > 0
                    ? $"Quedan {Math.Floor(hours).ToString(CultureInfo.InvariantCulture)} h para transcribirlas y enviarlas (72 h desde el fin de la contingencia)."
                    : "Venció el plazo de 72 h para transcribir y enviar las facturas manuales.",
                SiatStatusBuilder.At(transcription, zone)));
        }
    }
}

/// <summary>V4.1 · Conteos de homologación (tablero, caja y homologación).</summary>
internal static class HomologationCounts
{
    /// <summary>Productos activos sin actividad económica y código de producto del SIN.</summary>
    public static Task<int> PendingProductsAsync(IMinvDbContext db, CancellationToken ct) =>
        db.Set<Product>().CountAsync(p => p.IsActive && !db.Set<ProductSiatCode>().Any(h => h.ProductId == p.Id), ct);
}
