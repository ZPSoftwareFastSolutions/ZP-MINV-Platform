using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Infrastructure.Persistence;

namespace MINV.Infrastructure.Billing.Soap;

/// <summary>
/// V4.1 · Una llamada al SIN para la bitácora técnica (<see cref="SiatServiceCall"/>). NUNCA lleva el token: la cabecera
/// <c>apikey</c> no forma parte de los cuerpos y el <c>archivo</c> se registra solo por su tamaño.
/// </summary>
public sealed record SiatCallRecord(int Environment, SiatResource Resource, string Operation, SiatPlace? Place, DateTimeOffset OccurredAt,
    int DurationMs, int? HttpStatus, int? SiatCode, bool Succeeded, string? RequestBody, string? ResponseBody, string? Error);

/// <summary>V4.1 · Bitácora técnica de las llamadas al SIN (append-only). Un fallo al registrar NUNCA interrumpe la llamada.</summary>
public interface ISiatCallLog
{
    Task RecordAsync(SiatCallRecord call, CancellationToken cancellationToken = default);
}

/// <summary>
/// V4.1 · Bitácora técnica en un contexto propio (como la auditoría): se guarda aunque el caso de uso que llamó al SIN
/// falle o deshaga su transacción, y un error al guardar solo se informa en el rastreo. La empresa sale del
/// <see cref="ITenantContext"/>; sin empresa (p. ej. herramientas) no se registra.
/// </summary>
public sealed class SiatCallLog(IDbContextFactory<MinvWriteDbContext> factory, ITenantContext tenant) : ISiatCallLog
{
    public async Task RecordAsync(SiatCallRecord call, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (!tenant.IsSet)
        {
            return;
        }
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            db.Set<SiatServiceCall>().Add(new SiatServiceCall(tenant.TenantId, call.Environment, SiatSoapContract.ResourcePath(call.Resource),
                call.Operation, call.Place?.BranchId, call.Place?.PointOfSaleCode, call.OccurredAt, call.DurationMs, call.HttpStatus, call.SiatCode,
                call.Succeeded, call.RequestBody, call.ResponseBody, call.Error));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("M-INV · no se pudo registrar la llamada al SIN {0}: {1}", call.Operation, ex.Message);
        }
    }
}

/// <summary>V4.1 · Errores comunes de los adaptadores del SIN.</summary>
public static class SiatGatewayErrors
{
    /// <summary>El SIN rechazó el token delegado (HTTP 401/403 o mensaje 989): NO es falta de comunicación. El mensaje
    /// nunca incluye el token.</summary>
    public static DomainException TokenRejected(string reason) =>
        new("siat.token_rejected",
            $"El SIN rechazó el token delegado ({reason}): genere uno nuevo en el Portal SIAT (Token Delegado) y cárguelo en Configuración › Facturación SIAT.");

    /// <summary>Las notas crédito-débito (Documentos de Ajuste) no tienen servicio de paquetes (investigación 02 §8.6).</summary>
    public static DomainException PackagesNotSupported() =>
        new("siat.package_unsupported", "El servicio de Documentos de Ajuste no recibe paquetes: las notas crédito-débito se envían solo en línea.");
}
