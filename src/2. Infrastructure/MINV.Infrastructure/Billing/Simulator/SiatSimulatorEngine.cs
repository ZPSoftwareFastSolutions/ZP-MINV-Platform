using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Infrastructure.Billing.Soap;

namespace MINV.Infrastructure.Billing.Simulator;

/// <summary>
/// V4.1 · Simulador del SIN en memoria (thread-safe) que se comporta como los servicios de la modalidad Computarizada en
/// Línea según la investigación (docs/billing/investigacion-siat): token, CUIS (365 días) y CUFD (24 h, código de control
/// y dirección), puntos de venta, eventos significativos, catálogos de SIMULACIÓN, fecha y hora de Bolivia, verificación
/// de NIT, recepción individual (XSD oficial, hash, CUF, fórmulas), paquetes fuera de línea, anulación, reversión y
/// verificación de estado, con los códigos de respuesta de la tabla oficial. Tiene un interruptor de disponibilidad para
/// simular cortes. Lo usan <see cref="InProcessSiatGateway"/> (demostración y pruebas) y el proyecto MINV.SiatSimulator
/// (HTTP con el mismo contrato SOAP que el cliente real, regla F-16).
/// </summary>
public sealed partial class SiatSimulatorEngine
{
    /// <summary>Zona de Bolivia (UTC−4, sin horario de verano).</summary>
    public static readonly TimeSpan BoliviaOffset = SiatSoapContract.BoliviaOffset;

    /// <summary>Tolerancia de los controles aritméticos (V1…V5, N1…N3).</summary>
    public const decimal Tolerance = 0.01m;

    /// <summary>Tolerancia de reloj para las fechas «en el futuro».</summary>
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(5);

    /// <summary>Antes del vencimiento del CUIS, desde cuándo se puede renovar (y se avisa con 3008).</summary>
    public static readonly TimeSpan CuisRenewalWindow = TimeSpan.FromDays(5);

    /// <summary>Plazo para registrar un evento significativo después de su fin.</summary>
    public static readonly TimeSpan EventRegistrationWindow = TimeSpan.FromHours(48);

    private static readonly JsonSerializerOptions StateJson = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly SiatSimulatorOptions _options;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, SimDocument> _documents = new(StringComparer.Ordinal);
    private readonly SimulatorState _state;
    private volatile bool _available;
    private long _latencyTicks;

    public SiatSimulatorEngine(SiatSimulatorOptions? options = null, TimeProvider? time = null)
    {
        _options = options ?? new SiatSimulatorOptions();
        _time = time ?? TimeProvider.System;
        _available = _options.StartAvailable;
        _state = LoadState(_options.StateFile);
        foreach (var document in _state.Documents)
        {
            _documents[DocumentKey(document.Environment, document.Nit, document.Cuf)] = document;
        }
    }

    public SiatSimulatorOptions Options => _options;

    /// <summary>Interruptor de disponibilidad: con false, el gateway en proceso lanza <see cref="SiatUnavailableException"/> y
    /// el host HTTP responde 503.</summary>
    public bool Available
    {
        get => _available;
        set => _available = value;
    }

    /// <summary>Demora artificial de cada respuesta del host HTTP (para probar el tiempo máximo de espera del cliente).</summary>
    public TimeSpan Latency
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _latencyTicks));
        set => Interlocked.Exchange(ref _latencyTicks, Math.Max(0, value.Ticks));
    }

    public DateTimeOffset UtcNow => _time.GetUtcNow();

    /// <summary>Hora actual de Bolivia, sin zona (la de <c>sincronizarFechaHora</c> y de los plazos).</summary>
    public DateTime BoliviaNow => DateTime.SpecifyKind(UtcNow.ToOffset(BoliviaOffset).DateTime, DateTimeKind.Unspecified);

    public bool IsTokenAccepted(string? token)
    {
        var value = token?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }
        return _options.AcceptedTokens.Count > 0
            ? _options.AcceptedTokens.Any(t => string.Equals(t?.Trim(), value, StringComparison.Ordinal))
            : value.Length >= _options.MinimumTokenLength;
    }

    public SiatSimulatorStatus Status()
    {
        lock (_gate)
        {
            return new SiatSimulatorStatus(Available, (int)Latency.TotalMilliseconds, BoliviaNow, _state.Cuis.Count, _state.Cufds.Count,
                _state.PointsOfSale.Count(p => !p.Closed), _state.Events.Count, _documents.Count, _documents.Values.Count(d => d.Voided),
                _state.Packages.Count, _options.StateFile);
        }
    }

    // ================================================================================================ comunicación
    public SiatReply CheckCommunication(string? token)
    {
        EnsureAvailable();
        return IsTokenAccepted(token)
            ? new SiatReply(true, SiatCodes.CommunicationOk, Text(SiatCodes.CommunicationOk), null, [Message(SiatCodes.CommunicationOk)])
            : new SiatReply(false, null, null, null, [Message(SiatCodes.InvalidToken)]);
    }

    // ================================================================================================ códigos
    /// <summary>CUIS por (ambiente, NIT, sucursal, punto de venta): 365 días; si hay uno vigente lo devuelve, salvo que ya esté
    /// dentro de los 5 días previos a su vencimiento (entonces entrega uno nuevo).</summary>
    public SiatCuisReply RequestCuis(string? token, SiatSimulatorCaller caller, SiatPlace place)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(place);
        EnsureAvailable();
        lock (_gate)
        {
            if ((CallerError(token, caller, true) ?? PlaceError(caller, place)) is { } error)
            {
                return new SiatCuisReply(false, null, null, [error]);
            }
            var now = UtcNow;
            var current = _state.Cuis.Where(c => c.IsAt(caller, place) && !c.Revoked && c.ValidUntil > now).MaxBy(c => c.ValidUntil);
            if (current is null || current.ValidUntil - now <= CuisRenewalWindow)
            {
                current = new SimCuis
                {
                    Code = NewCode(4),
                    SystemCode = caller.SystemCode!.Trim(),
                    IssuedAt = now,
                    ValidUntil = now.ToOffset(BoliviaOffset).Add(_options.CuisValidity),
                };
                current.Place(caller, place);
                _state.Cuis.Add(current);
                SaveState();
            }
            return new SiatCuisReply(true, current.Code, current.ValidUntil.ToOffset(BoliviaOffset), []);
        }
    }

    /// <summary>CUFD nuevo en cada solicitud (24 h) con código de control hexadecimal de 15 caracteres y la dirección del
    /// Padrón simulada. Avisa con 3008 si el CUIS está por vencer.</summary>
    public SiatCufdReply RequestCufd(string? token, SiatSimulatorCaller caller, SiatPlace place, string? cuis)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(place);
        EnsureAvailable();
        lock (_gate)
        {
            if ((CallerError(token, caller, true) ?? PlaceError(caller, place) ?? CuisError(caller, place, cuis, false)) is { } error)
            {
                return new SiatCufdReply(false, null, null, null, null, [error]);
            }
            var now = UtcNow;
            var cuisRecord = FindCuis(caller, cuis)!;
            string code;
            do
            {
                code = "BQ" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(30)).TrimEnd('=');
            }
            while (_state.Cufds.Any(c => c.Code == code));
            var cufd = new SimCufd
            {
                Code = code,
                ControlCode = NewCode(8)[..15],
                Address = string.Format(CultureInfo.InvariantCulture, _options.AddressFormat, place.BranchCode),
                CuisCode = cuisRecord.Code,
                IssuedAt = now,
                ValidUntil = now.ToOffset(BoliviaOffset).Add(_options.CufdValidity),
            };
            cufd.Place(caller, place);
            _state.Cufds.Add(cufd);
            SaveState();
            IReadOnlyList<SiatMessage> warnings = cuisRecord.ValidUntil - now <= CuisRenewalWindow ? [Message(SiatCodes.CuisAboutToExpire)] : [];
            return new SiatCufdReply(true, cufd.Code, cufd.ControlCode, cufd.Address, cufd.ValidUntil, warnings);
        }
    }

    /// <summary>verificarNit: 986 si tiene de 5 a 13 dígitos y no termina en 999; 987 si termina en 999; 994 si tiene menos
    /// de 5 dígitos (o más de 13).</summary>
    public SiatNitReply VerifyNit(string? token, SiatSimulatorCaller caller, int branchCode, string? cuis, long nitToVerify)
    {
        ArgumentNullException.ThrowIfNull(caller);
        EnsureAvailable();
        lock (_gate)
        {
            var place = new SiatPlace(branchCode, 0);
            if ((CallerError(token, caller, true) ?? BranchError(branchCode) ?? CuisError(caller, place, cuis, true)) is { } error)
            {
                return new SiatNitReply(false, false, error.Code, error.Description, [error]);
            }
        }
        var digits = nitToVerify.ToString(CultureInfo.InvariantCulture);
        var code = nitToVerify <= 0 || digits.Length is < 5 or > 13 ? SiatCodes.NitNotFound
            : digits.EndsWith("999", StringComparison.Ordinal) ? SiatCodes.NitInactive
            : SiatCodes.NitActive;
        var message = Message(code);
        return new SiatNitReply(code != SiatCodes.NitNotFound, code == SiatCodes.NitActive, code, message.Description, [message]);
    }

    // ================================================================================================ sincronización
    public SiatCatalogReply SyncCatalog(string? token, SiatSimulatorCaller caller, SiatPlace place, string? cuis, string catalog)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(place);
        var rows = SiatSimulatorCatalogs.Rows((catalog ?? string.Empty).Trim().ToUpperInvariant())
                   ?? throw new ArgumentOutOfRangeException(nameof(catalog), catalog, "El simulador no conoce ese catálogo.");
        EnsureAvailable();
        lock (_gate)
        {
            if ((CallerError(token, caller, false) ?? PlaceError(caller, place) ?? CuisError(caller, place, cuis, false)) is { } error)
            {
                return new SiatCatalogReply(false, [], [error]);
            }
        }
        return new SiatCatalogReply(true, rows, []);
    }

    /// <summary>sincronizarFechaHora: hora actual de Bolivia, sin zona.</summary>
    public SiatClockReply SyncClock(string? token, SiatSimulatorCaller caller, SiatPlace place, string? cuis)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(place);
        EnsureAvailable();
        lock (_gate)
        {
            if ((CallerError(token, caller, false) ?? PlaceError(caller, place) ?? CuisError(caller, place, cuis, false)) is { } error)
            {
                return new SiatClockReply(false, null, [error]);
            }
        }
        var now = BoliviaNow;
        return new SiatClockReply(true, now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond)), []);
    }

    // ================================================================================================ operaciones
    /// <summary>registroPuntoVenta: código correlativo por sucursal (1, 2, …). El CUIS es el de la sucursal.</summary>
    public SiatPointOfSaleReply RegisterPointOfSale(string? token, SiatSimulatorCaller caller, int branchCode, string? cuis, int typeCode, string? name,
        string? description)
    {
        ArgumentNullException.ThrowIfNull(caller);
        EnsureAvailable();
        lock (_gate)
        {
            var error = CallerError(token, caller, true) ?? BranchError(branchCode) ?? CuisError(caller, new SiatPlace(branchCode, 0), cuis, true)
                        ?? (SiatSimulatorCatalogs.Contains(SiatCatalogNames.PointOfSaleTypes, typeCode) ? null : Message(947))
                        ?? (string.IsNullOrWhiteSpace(name) ? Message(948) : null)
                        ?? (string.IsNullOrWhiteSpace(description) ? Message(949) : null);
            if (error is not null)
            {
                return new SiatPointOfSaleReply(false, null, [error]);
            }
            var code = _state.PointsOfSale.Where(p => p.Environment == caller.Environment && p.Nit == caller.Nit && p.Branch == branchCode)
                .Select(p => p.Code).DefaultIfEmpty(0).Max() + 1;
            _state.PointsOfSale.Add(new SimPointOfSale
            {
                Environment = caller.Environment,
                Nit = caller.Nit,
                Branch = branchCode,
                Code = code,
                Name = name!.Trim(),
                Description = description!.Trim(),
                TypeCode = typeCode,
            });
            SaveState();
            return new SiatPointOfSaleReply(true, code, []);
        }
    }

    public SiatPointOfSaleListReply ListPointsOfSale(string? token, SiatSimulatorCaller caller, int branchCode, string? cuis)
    {
        ArgumentNullException.ThrowIfNull(caller);
        EnsureAvailable();
        lock (_gate)
        {
            if ((CallerError(token, caller, false) ?? BranchError(branchCode) ?? CuisError(caller, new SiatPlace(branchCode, 0), cuis, true)) is { } error)
            {
                return new SiatPointOfSaleListReply(false, [], [error]);
            }
            var items = _state.PointsOfSale
                .Where(p => p.Environment == caller.Environment && p.Nit == caller.Nit && p.Branch == branchCode && !p.Closed)
                .OrderBy(p => p.Code)
                .Select(p => new SiatPointOfSaleInfo(p.Code, p.Name, p.TypeCode))
                .ToList();
            return items.Count == 0
                ? new SiatPointOfSaleListReply(false, [], [Message(982)])
                : new SiatPointOfSaleListReply(true, items, []);
        }
    }

    /// <summary>cierrePuntoVenta: cierre definitivo; el CUIS y los CUFD de ese punto dejan de valer.</summary>
    public SiatReply ClosePointOfSale(string? token, SiatSimulatorCaller caller, SiatPlace place, string? cuis)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(place);
        EnsureAvailable();
        lock (_gate)
        {
            var error = CallerError(token, caller, false) ?? BranchError(place.BranchCode) ?? CuisError(caller, place with { PointOfSaleCode = 0 }, cuis, true);
            var point = OpenPointOfSale(caller, place);
            error ??= point is null ? Message(933) : null;
            if (error is not null)
            {
                return new SiatReply(false, null, null, null, [error]);
            }
            point!.Closed = true;
            foreach (var code in _state.Cuis.Where(c => c.IsAt(caller, place)))
            {
                code.Revoked = true;
            }
            foreach (var code in _state.Cufds.Where(c => c.IsAt(caller, place)))
            {
                code.Revoked = true;
            }
            SaveState();
            return new SiatReply(true, null, null, null, []);
        }
    }

    /// <summary>
    /// registroEventoSignificativo: <paramref name="cufd"/> es el CUFD nuevo (vigente) y <paramref name="eventCufd"/> el que
    /// se usó durante el evento (debe haberlo emitido este simulador para ese lugar). Fin posterior al inicio, no en el
    /// futuro y dentro de las 48 h. Devuelve el código de recepción (numérico: viaja en <c>codigoEvento</c> del paquete).
    /// </summary>
    public SiatEventReply RegisterEvent(string? token, SiatSimulatorCaller caller, SiatPlace place, string? cuis, string? cufd, int eventCode,
        string? description, DateTime? startedAt, DateTime? endedAt, string? eventCufd)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(place);
        EnsureAvailable();
        lock (_gate)
        {
            var now = BoliviaNow;
            var error = CallerError(token, caller, false) ?? PlaceError(caller, place) ?? CuisError(caller, place, cuis, false)
                        ?? CufdError(caller, place, cufd, true)
                        ?? (eventCode == 0 ? Message(950) : null)
                        ?? (SiatSimulatorCatalogs.Contains(SiatCatalogNames.SignificantEvents, eventCode) ? null : Message(976))
                        ?? (string.IsNullOrWhiteSpace(description) ? Message(951) : null)
                        ?? (endedAt is null ? Message(960) : null)
                        ?? (startedAt is null || endedAt <= startedAt || endedAt > now + ClockTolerance
                            || now - endedAt > EventRegistrationWindow ? Message(974) : null)
                        ?? (FindCufd(caller, eventCufd) is { } used && used.Branch == place.BranchCode && used.PointOfSale == place.PointOfSaleCode
                            ? null
                            : Message(984));
            if (error is not null)
            {
                return new SiatEventReply(false, null, [error]);
            }
            var receptionCode = (_state.NextEventCode++).ToString(CultureInfo.InvariantCulture);
            var record = new SimEvent
            {
                ReceptionCode = receptionCode,
                ReasonCode = eventCode,
                Description = description!.Trim(),
                StartedAt = startedAt!.Value,
                EndedAt = endedAt!.Value,
                EventCufd = eventCufd!.Trim(),
                Cufd = cufd!.Trim(),
                RegisteredAt = UtcNow,
            };
            record.Place(caller, place);
            _state.Events.Add(record);
            SaveState();
            return new SiatEventReply(true, receptionCode, []);
        }
    }

    // ================================================================================================ validaciones comunes
    private void EnsureAvailable()
    {
        if (!Available)
        {
            throw new SiatUnavailableException("El SIN (simulador) no está disponible: el interruptor de disponibilidad está apagado.");
        }
    }

    private SiatMessage? CallerError(string? token, SiatSimulatorCaller caller, bool modality)
    {
        if (!IsTokenAccepted(token))
        {
            return Message(SiatCodes.InvalidToken);
        }
        if (caller.Environment is not (SiatCodes.EnvironmentProduction or SiatCodes.EnvironmentTest))
        {
            return Message(910);
        }
        if (modality && caller.Modality != SiatCodes.ModalityComputerized)
        {
            return Message(917);
        }
        if (string.IsNullOrWhiteSpace(caller.SystemCode))
        {
            return Message(911);
        }
        return caller.Nit is <= 0 or > 9_999_999_999_999 ? Message(919) : null;
    }

    private static SiatMessage? BranchError(int branchCode) => branchCode is < 0 or > 9999 ? Message(918) : null;

    /// <summary>Sucursal válida y, si el punto de venta no es 0, registrado y abierto.</summary>
    private SiatMessage? PlaceError(SiatSimulatorCaller caller, SiatPlace place) =>
        BranchError(place.BranchCode)
        ?? (place.PointOfSaleCode < 0 || (place.PointOfSaleCode > 0 && OpenPointOfSale(caller, place) is null) ? Message(933) : null);

    private SimPointOfSale? OpenPointOfSale(SiatSimulatorCaller caller, SiatPlace place) =>
        _state.PointsOfSale.FirstOrDefault(p => p.Environment == caller.Environment && p.Nit == caller.Nit && p.Branch == place.BranchCode
                                               && p.Code == place.PointOfSaleCode && !p.Closed);

    /// <summary>El CUIS existe (913), está vigente (929) y es de ese lugar (930); con <paramref name="anyPointOfSale"/> basta
    /// con que sea de la sucursal.</summary>
    private SiatMessage? CuisError(SiatSimulatorCaller caller, SiatPlace place, string? cuis, bool anyPointOfSale)
    {
        var record = FindCuis(caller, cuis);
        if (record is null)
        {
            return Message(913);
        }
        if (record.Revoked || record.ValidUntil <= UtcNow)
        {
            return Message(929);
        }
        return record.Branch != place.BranchCode || (!anyPointOfSale && record.PointOfSale != place.PointOfSaleCode) ? Message(930) : null;
    }

    /// <summary>El CUFD lo emitió este simulador para ese lugar (914) y, si se pide, sigue vigente (953).</summary>
    private SiatMessage? CufdError(SiatSimulatorCaller caller, SiatPlace place, string? cufd, bool requireValid)
    {
        var record = FindCufd(caller, cufd);
        if (record is null || record.Branch != place.BranchCode || record.PointOfSale != place.PointOfSaleCode)
        {
            return Message(914);
        }
        return requireValid && (record.Revoked || record.ValidUntil <= UtcNow) ? Message(953) : null;
    }

    private SimCuis? FindCuis(SiatSimulatorCaller caller, string? cuis) =>
        string.IsNullOrWhiteSpace(cuis) ? null : _state.Cuis.FirstOrDefault(c => c.Code == cuis.Trim() && c.IsOf(caller));

    private SimCufd? FindCufd(SiatSimulatorCaller caller, string? cufd) =>
        string.IsNullOrWhiteSpace(cufd) ? null : _state.Cufds.FirstOrDefault(c => c.Code == cufd.Trim() && c.IsOf(caller));

    // ================================================================================================ auxiliares
    /// <summary>Mensaje del SIN con el texto de la tabla oficial (y, en paquetes, número de archivo y de detalle).</summary>
    public static SiatMessage Message(int code, int? fileNumber = null, int? detailNumber = null, string? detail = null) =>
        new(code, detail is { Length: > 0 } ? $"{Text(code)}: {Trim(detail, 400)}" : Text(code), fileNumber, detailNumber);

    private static string Text(int code) => SiatSimulatorCatalogs.MessageText(code);

    private static string Trim(string text, int max) => text.Length > max ? text[..max] : text;

    private static string NewCode(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes));

    private static string DocumentKey(int environment, long nit, string cuf) =>
        string.Create(CultureInfo.InvariantCulture, $"{environment}|{nit}|{cuf}");

    // ================================================================================================ estado persistente
    private static SimulatorState LoadState(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new SimulatorState();
        }
        try
        {
            return JsonSerializer.Deserialize<SimulatorState>(File.ReadAllText(path, Encoding.UTF8), StateJson) ?? new SimulatorState();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("M-INV · simulador del SIN: no se pudo leer el estado {0} ({1}); se empieza de cero.", path, ex.Message);
            return new SimulatorState();
        }
    }

    /// <summary>Guarda el estado (si hay archivo configurado) de forma atómica: archivo temporal + reemplazo.</summary>
    private void SaveState()
    {
        var path = _options.StateFile;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        try
        {
            _state.Documents = [.. _documents.Values];
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            var temp = full + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_state, StateJson), new UTF8Encoding(false));
            File.Move(temp, full, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("M-INV · simulador del SIN: no se pudo guardar el estado en {0}: {1}", path, ex.Message);
        }
    }
}
