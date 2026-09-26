using System.Text.Json.Serialization;
using MINV.Application.Abstractions;

namespace MINV.Infrastructure.Billing.Simulator;

/// <summary>V4.1 · Configuración del simulador del SIN (demostración, pruebas y el proyecto MINV.SiatSimulator).</summary>
public sealed class SiatSimulatorOptions
{
    /// <summary>Tokens delegados aceptados. Vacío: cualquier token de al menos <see cref="MinimumTokenLength"/> caracteres.</summary>
    public IReadOnlyCollection<string> AcceptedTokens { get; set; } = [];

    public int MinimumTokenLength { get; set; } = 10;

    /// <summary>Dirección del Padrón que devuelve el CUFD (<c>{0}</c> = código de sucursal).</summary>
    public string AddressFormat { get; set; } = "AV. SIMULADA N° {0}";

    public TimeSpan CuisValidity { get; set; } = TimeSpan.FromDays(365);

    public TimeSpan CufdValidity { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Archivo JSON donde el simulador guarda su estado para sobrevivir reinicios (opcional).</summary>
    public string? StateFile { get; set; }

    /// <summary>Estado inicial del interruptor de disponibilidad.</summary>
    public bool StartAvailable { get; set; } = true;
}

/// <summary>V4.1 · Quién llama al simulador: ambiente, modalidad (0 si la operación no la envía), NIT y código de sistema.</summary>
public sealed record SiatSimulatorCaller(int Environment, int Modality, long Nit, string? SystemCode)
{
    public static SiatSimulatorCaller From(SiatConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return new(connection.Environment, connection.Modality, connection.Nit, connection.SystemCode);
    }
}

/// <summary>V4.1 · Parámetros comunes de los servicios de facturación (recepción, paquetes, anulación, reversión, estado).</summary>
public sealed record SiatSimulatorDocumentCall(SiatSimulatorCaller Caller, SiatPlace Place, SiatResource Resource, int DocumentSector,
    int DocumentType, int Emission, string? Cuis, string? Cufd);

/// <summary>V4.1 · Estado visible del simulador (<c>/control/estado</c>).</summary>
public sealed record SiatSimulatorStatus(bool Available, int LatencyMs, DateTime BoliviaNow, int Cuis, int Cufds, int PointsOfSale, int Events,
    int Documents, int VoidedDocuments, int Packages, string? StateFile);

// ------------------------------------------------------------------------------------------------ estado (serializable)
/// <summary>Estado completo del simulador (lo que se guarda en <see cref="SiatSimulatorOptions.StateFile"/>).</summary>
internal sealed class SimulatorState
{
    public int Version { get; set; } = 1;

    public long NextEventCode { get; set; } = 1_000_001;

    public List<SimCuis> Cuis { get; set; } = [];

    public List<SimCufd> Cufds { get; set; } = [];

    public List<SimPointOfSale> PointsOfSale { get; set; } = [];

    public List<SimEvent> Events { get; set; } = [];

    public List<SimDocument> Documents { get; set; } = [];

    public List<SimPackage> Packages { get; set; } = [];
}

/// <summary>Lugar del SIN: ambiente, NIT, sucursal del Padrón y punto de venta.</summary>
internal abstract class SimPlaced
{
    public int Environment { get; set; }

    public long Nit { get; set; }

    public int Branch { get; set; }

    public int PointOfSale { get; set; }

    public bool IsAt(SiatSimulatorCaller caller, SiatPlace place) =>
        Environment == caller.Environment && Nit == caller.Nit && Branch == place.BranchCode && PointOfSale == place.PointOfSaleCode;

    public bool IsOf(SiatSimulatorCaller caller) => Environment == caller.Environment && Nit == caller.Nit;

    public void Place(SiatSimulatorCaller caller, SiatPlace place)
    {
        Environment = caller.Environment;
        Nit = caller.Nit;
        Branch = place.BranchCode;
        PointOfSale = place.PointOfSaleCode;
    }
}

internal sealed class SimCuis : SimPlaced
{
    public string Code { get; set; } = string.Empty;

    public string SystemCode { get; set; } = string.Empty;

    public DateTimeOffset IssuedAt { get; set; }

    public DateTimeOffset ValidUntil { get; set; }

    public bool Revoked { get; set; }
}

internal sealed class SimCufd : SimPlaced
{
    public string Code { get; set; } = string.Empty;

    public string ControlCode { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string CuisCode { get; set; } = string.Empty;

    public DateTimeOffset IssuedAt { get; set; }

    public DateTimeOffset ValidUntil { get; set; }

    public bool Revoked { get; set; }
}

internal sealed class SimPointOfSale
{
    public int Environment { get; set; }

    public long Nit { get; set; }

    public int Branch { get; set; }

    public int Code { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int TypeCode { get; set; }

    public bool Closed { get; set; }
}

internal sealed class SimEvent : SimPlaced
{
    public string ReceptionCode { get; set; } = string.Empty;

    public int ReasonCode { get; set; }

    public string Description { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; }

    public DateTime EndedAt { get; set; }

    /// <summary>CUFD vigente durante el evento (con él se emitieron los documentos fuera de línea).</summary>
    public string EventCufd { get; set; } = string.Empty;

    /// <summary>CUFD nuevo con el que se registró el evento.</summary>
    public string Cufd { get; set; } = string.Empty;

    public DateTimeOffset RegisteredAt { get; set; }

    [JsonIgnore]
    public bool IsManual => ReasonCode >= SiatSimulatorCatalogs.FirstManualEvent;
}

internal sealed class SimDocument : SimPlaced
{
    public string Cuf { get; set; } = string.Empty;

    public int DocumentSector { get; set; }

    public int DocumentType { get; set; }

    public int Emission { get; set; }

    public long Number { get; set; }

    /// <summary>fechaEmision del XML (hora de Bolivia, sin zona).</summary>
    public DateTime IssuedAt { get; set; }

    public string ReceptionCode { get; set; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; set; }

    public bool Voided { get; set; }

    public int? VoidReason { get; set; }

    public bool Reverted { get; set; }
}

internal sealed class SimPackage : SimPlaced
{
    public string ReceptionCode { get; set; } = string.Empty;

    public int DocumentSector { get; set; }

    public string EventReceptionCode { get; set; } = string.Empty;

    public int Count { get; set; }

    public List<SimMessage> Errors { get; set; } = [];

    public DateTimeOffset ReceivedAt { get; set; }
}

internal sealed class SimMessage
{
    public int Code { get; set; }

    public string Description { get; set; } = string.Empty;

    public int? FileNumber { get; set; }

    public int? DetailNumber { get; set; }

    public static SimMessage From(SiatMessage message) => new()
    {
        Code = message.Code,
        Description = message.Description,
        FileNumber = message.FileNumber,
        DetailNumber = message.DetailNumber,
    };

    public SiatMessage ToMessage() => new(Code, Description, FileNumber, DetailNumber);
}
