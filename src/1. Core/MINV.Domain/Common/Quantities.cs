using System.Globalization;

namespace MINV.Domain.Common;

/// <summary>
/// Aritmética de cantidades de M-INV. Las cantidades se guardan con 6 decimales (<c>numeric(18,6)</c>) y todo
/// resultado se redondea con <see cref="Round6"/>: la misma regla <c>r6</c> de la V2.1 (Python y Office Scripts), así
/// la V3 reproduce exactamente los saldos de la V2.1 al migrar.
/// </summary>
public static class Quantities
{
    public const int Scale = 6;

    private const decimal Factor = 1_000_000m;

    /// <summary><c>floor(x · 10⁶ + 0,5) / 10⁶</c>: idéntico a <c>r6</c> de la V2.1 (redondeo de la mitad hacia +∞).</summary>
    public static decimal Round6(decimal value) => decimal.Floor(value * Factor + 0.5m) / Factor;

    public static bool IsWhole(decimal value) => value == decimal.Truncate(value);

    /// <summary>Exige cantidad entera cuando la unidad no admite decimales (UND, CAJA, PAQ…).</summary>
    public static void EnsureAllowed(decimal quantity, bool unitAllowsDecimals, string unitCode)
    {
        if (!unitAllowsDecimals && !IsWhole(quantity))
        {
            throw new DomainException("quantity.decimals", $"La unidad {unitCode} no admite decimales.");
        }
    }

    public static string Format(decimal value) =>
        Round6(value).ToString("0.######", CultureInfo.InvariantCulture);
}
