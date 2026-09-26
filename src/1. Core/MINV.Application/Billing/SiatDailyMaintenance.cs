namespace MINV.Application.Billing;

/// <summary>
/// V4.1 · Mantenimiento diario de los códigos del SIN de la empresa: por cada punto de venta activo, CUIS vigente
/// (renovación desde 5 días antes), hora del SIN, CUFD del día (renovado antes de vencer) y catálogos si no se
/// sincronizaron hoy. Lo usan <see cref="PrepareSiatCommand"/> (forzado) y el trabajo automático
/// (<see cref="ISiatWorker.MaintainAsync"/>). NO guarda (guarda quien llama). Implementación: agente D.
/// </summary>
public static class SiatDailyMaintenance
{
    public static Task<SiatMaintenanceResult> RunAsync(BillingLookups lookups, SiatCodeManager codes, FiscalContext context, bool force,
        Guid? userId, CancellationToken ct) =>
        throw new NotImplementedException("SiatDailyMaintenance.RunAsync: pendiente de implementar.");
}
