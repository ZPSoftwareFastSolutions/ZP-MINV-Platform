using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Domain.Catalog;

namespace MINV.Application.Tech;

/// <summary>
/// V4.2 · Textos impresos de la edición Tecnología (ticket de la caja, rollo fiscal, PDF de la factura y proforma del
/// armado): las series o IMEI de cada línea y la garantía DERIVADA de la fecha de la venta y de los meses del producto
/// (regla T-04: nunca se guarda una fecha de vencimiento). Un solo lugar para que todos los comprobantes digan lo mismo.
/// </summary>
public static class TechPrint
{
    /// <summary>«S/N: A1, A2» o «IMEI: 352099001761481» (null si la línea no lleva series).</summary>
    public static string? Serials(SerialKind kind, IReadOnlyCollection<string>? serials) =>
        serials is null || serials.Count == 0 ? null : (kind == SerialKind.Imei ? "IMEI: " : "S/N: ") + string.Join(", ", serials);

    /// <summary>Series tal como las guarda la línea fiscal (texto separado por comas): primero el IMEI, si no el número de serie.</summary>
    public static string? Serials(string? serialNumbers, string? imeis)
    {
        static IReadOnlyList<string> Split(string? text) =>
            (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Split(imeis) is { Count: > 0 } imei ? Serials(SerialKind.Imei, imei) : Serials(SerialKind.Serial, Split(serialNumbers));
    }

    /// <summary>«Garantía hasta 26/09/2027».</summary>
    public static string Warranty(DateOnly until) => "Garantía hasta " + until.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>«Garantía 12 meses» (proforma: todavía no hay fecha de venta).</summary>
    public static string WarrantyMonths(int months) => months == 1 ? "Garantía 1 mes" : $"Garantía {months.ToString(CultureInfo.InvariantCulture)} meses";

    /// <summary>Fin de la garantía de una venta del día <paramref name="soldOn"/> (null si el producto no tiene garantía).</summary>
    public static DateOnly? WarrantyUntil(DateOnly soldOn, int months) => months > 0 ? soldOn.AddMonths(months) : null;

    /// <summary>Meses de garantía de cada producto (los que no tienen perfil técnico no aparecen).</summary>
    public static async Task<IReadOnlyDictionary<Guid, int>> WarrantyMonthsAsync(IMinvDbContext db, IReadOnlyCollection<Guid> productIds,
        CancellationToken ct) =>
        productIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await db.Set<ProductTechProfile>().AsNoTracking().Where(p => productIds.Contains(p.ProductId) && p.WarrantyMonths > 0)
                .ToDictionaryAsync(p => p.ProductId, p => p.WarrantyMonths, ct);
}
