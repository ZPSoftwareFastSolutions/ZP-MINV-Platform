using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Accounting;
using MINV.Domain.Inventory;

namespace MINV.Application.Inventory;

/// <summary>
/// V4.2 · Contabilidad de los ajustes de inventario: AJUSTE (+) y AJUSTE (−) de «Registrar movimiento» (mermas, daños,
/// pérdidas, sobrantes) y de la toma física. Hasta la V4.2 cambiaban el stock sin asiento y el mayor 1.1.05 Inventario se
/// alejaba del valor del stock (Σ existencias × costo promedio) justo en el valor de los ajustes: en la carga de 60 días de
/// Tech Zone Gaming, +2.646,02: diez AJUSTE (−) por 3.134,52 (nueve mermas y un faltante de la toma física) menos un
/// sobrante de la toma física por 488,50. Ahora cada ajuste se
/// contabiliza en la misma transacción al costo promedio vigente de la variante en el almacén (el mismo con el que el stock
/// se valúa): la merma o el faltante, Debe 5.1.09 Mermas y ajustes de inventario / Haber 1.1.05; el sobrante, Debe 1.1.05 /
/// Haber 4.1.02 Sobrantes de inventario (las cuentas del plan que ya existían para esto). El asiento va en la fecha del
/// movimiento o, si ese período está cerrado, en la de hoy. Los demás movimientos que se registran a mano (SALDO INICIAL,
/// ENTRADA, SALIDA, VENTA POS, DEVOLUCIÓN DE CLIENTE: el kardex de la V2.1, sin documento ni precio) no se contabilizan:
/// su contrapartida (apertura, proveedor, costo de ventas) la registra el contador con un asiento (ver el diseño de la V4.2,
/// límite L-05).
/// </summary>
public static class InventoryAdjustments
{
    /// <summary>¿El tipo de movimiento se contabiliza como ajuste de inventario?</summary>
    public static bool Posts(string movementTypeCode) =>
        movementTypeCode is MovementTypeCodes.AdjustmentIn or MovementTypeCodes.AdjustmentOut;

    /// <summary>Contabiliza los sobrantes y los faltantes (valores al costo promedio) de un documento; null si ambos son 0.</summary>
    public static async Task<JournalEntry?> PostAsync(IMinvDbContext db, Guid tenantId, Guid branchId, Guid userId, DateOnly businessDate, DateOnly today,
        string description, decimal surplus, decimal shortage, DateTimeOffset now, Guid correlationId, CancellationToken ct)
    {
        surplus = JournalPoster.Money(surplus);
        shortage = JournalPoster.Money(shortage);
        if (surplus <= 0 && shortage <= 0)
        {
            return null;
        }
        var closed = await db.Set<FiscalPeriod>().AnyAsync(p => p.Year == businessDate.Year && p.Month == businessDate.Month
                                                                && p.Status == FiscalPeriodStatus.Closed, ct);
        return await JournalPoster.PostAsync(db, tenantId, branchId, userId, closed ? today : businessDate, description,
        [
            new JournalLineSpec(AccountCodes.Inventory, surplus, 0, "Sobrante de inventario"),
            new JournalLineSpec(AccountCodes.InventorySurplus, 0, surplus, "Sobrante de inventario"),
            new JournalLineSpec(AccountCodes.InventoryShrinkage, shortage, 0, "Merma o faltante de inventario"),
            new JournalLineSpec(AccountCodes.Inventory, 0, shortage, "Merma o faltante de inventario"),
        ], now, correlationId, ct);
    }
}
