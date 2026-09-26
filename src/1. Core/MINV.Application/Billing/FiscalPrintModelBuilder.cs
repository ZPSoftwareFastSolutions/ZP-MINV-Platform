using MINV.Application.Abstractions;

namespace MINV.Application.Billing;

/// <summary>
/// V4.1 · Arma la representación gráfica (<see cref="FiscalPrintModel"/>) de un documento fiscal a partir de lo guardado
/// (documento, líneas, XML, configuración del ambiente para la URL del QR). La usan la consulta de impresión, el envío
/// por correo y la caja. Implementación: equipo de consultas (agente D).
/// </summary>
public static class FiscalPrintModelBuilder
{
    public static Task<FiscalPrintModel> BuildAsync(IMinvDbContext db, Guid documentId, CancellationToken ct) =>
        throw new NotImplementedException("FiscalPrintModelBuilder.BuildAsync: pendiente de implementar.");
}
