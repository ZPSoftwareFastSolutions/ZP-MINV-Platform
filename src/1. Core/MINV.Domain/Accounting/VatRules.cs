namespace MINV.Domain.Accounting;

/// <summary>
/// V4.2 · IVA incluido en el precio (dominio puro). Dos convenciones, según el país de la empresa:
/// <list type="bullet">
/// <item><b>Bolivia</b> (Ley 843, arts. 5 y 15): el IVA forma parte del precio facturado y se calcula sobre el IMPORTE: el
/// débito fiscal de una venta es el 13 % de lo facturado, el crédito fiscal de una compra es el 13 % de la factura del
/// proveedor y el neto contable es el 87 % del importe (tasa efectiva del 14,94 % sobre el neto). Es la regla del libro de
/// ventas y del libro de compras del SIN (<c>FiscalRules.Vat</c>), así que el asiento de la venta, el costo neto de una
/// compra y el margen del catálogo la usan también.</item>
/// <item><b>Otros países</b> (empresas de la V3 con la tasa del 19 %): el precio es el neto más el impuesto, que se separa
/// como importe × tasa / (100 + tasa).</item>
/// </list>
/// Con la misma convención para el precio y para el costo, el margen sobre el neto es (precio − costo con IVA) / precio.
/// </summary>
public static class VatRules
{
    /// <summary>Código ISO de Bolivia: el IVA se calcula sobre el importe facturado.</summary>
    public const string BoliviaIso = "BO";

    /// <summary>¿El IVA se calcula sobre el importe facturado (Bolivia)? Sin país conocido, sí: M-INV nace en Bolivia (el
    /// aprovisionamiento usa «BO» por defecto).</summary>
    public static bool OnInvoicedAmount(string? countryIso) =>
        string.IsNullOrWhiteSpace(countryIso) || countryIso.Trim().Equals(BoliviaIso, StringComparison.OrdinalIgnoreCase);

    /// <summary>IVA contenido en un importe con el IVA incluido, redondeado a 2 decimales (mitad hacia arriba): en Bolivia
    /// el 13 % del importe; en los demás, importe × tasa / (100 + tasa).</summary>
    public static decimal IncludedTax(decimal amount, decimal ratePercent, bool onInvoicedAmount) =>
        ratePercent <= 0
            ? 0m
            : decimal.Round(onInvoicedAmount ? amount * ratePercent / 100m : amount * ratePercent / (100m + ratePercent), 2,
                MidpointRounding.AwayFromZero);

    /// <summary>
    /// IVA de una factura repartido entre sus líneas. El total es el IVA del importe TOTAL redondeado una sola vez
    /// (<see cref="IncludedTax"/> de la suma): en Bolivia, el débito fiscal que la factura lleva al libro de ventas (13 % de la
    /// base de la factura, <c>FiscalRules.Vat</c>). Redondear cada línea y sumar puede apartarse en centavos (729,50 + 15,50:
    /// 94,84 + 2,02 = 96,86 contra 96,85 del libro), y el asiento de la venta (2.1.02) debe ser el débito del libro. Cada
    /// línea recibe su IVA truncado al centavo y los centavos que faltan van a las líneas con la mayor fracción descartada
    /// (resto mayor): la suma es exactamente el IVA del total, ninguna línea queda negativa y, cuando el redondeo por línea ya
    /// cuadra con el total, el resultado es el mismo que redondear cada línea.
    /// </summary>
    public static decimal[] Allocate(IReadOnlyList<decimal> lineAmounts, decimal ratePercent, bool onInvoicedAmount)
    {
        var taxes = new decimal[lineAmounts.Count];
        if (ratePercent <= 0 || taxes.Length == 0)
        {
            return taxes;
        }
        var exact = lineAmounts.Select(a => onInvoicedAmount ? a * ratePercent / 100m : a * ratePercent / (100m + ratePercent)).ToArray();
        for (var i = 0; i < taxes.Length; i++)
        {
            taxes[i] = Math.Max(0m, decimal.Floor(exact[i] * 100m) / 100m);
        }
        var cents = (int)Math.Clamp((IncludedTax(lineAmounts.Sum(), ratePercent, onInvoicedAmount) - taxes.Sum()) * 100m, 0m, taxes.Length);
        foreach (var i in Enumerable.Range(0, taxes.Length).OrderByDescending(i => exact[i] - taxes[i]).ThenByDescending(i => lineAmounts[i]).Take(cents))
        {
            taxes[i] += 0.01m;
        }
        return taxes;
    }

    /// <summary>Neto (sin IVA) de un importe con el IVA incluido, sin redondear: en Bolivia el 87 % del importe.</summary>
    public static decimal NetOf(decimal amount, decimal ratePercent, bool onInvoicedAmount) =>
        ratePercent <= 0 ? amount : onInvoicedAmount ? amount * (100m - ratePercent) / 100m : amount * 100m / (100m + ratePercent);

    /// <summary>Importe con el IVA incluido que corresponde a un neto, sin redondear: en Bolivia neto / 0,87 (así el 13 % del
    /// importe es exactamente la diferencia con el neto).</summary>
    public static decimal GrossOf(decimal net, decimal ratePercent, bool onInvoicedAmount) =>
        ratePercent <= 0 ? net : onInvoicedAmount ? net * 100m / (100m - ratePercent) : net * (100m + ratePercent) / 100m;
}
