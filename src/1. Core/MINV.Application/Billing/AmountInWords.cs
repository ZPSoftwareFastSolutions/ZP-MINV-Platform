using System.Globalization;
using MINV.Domain.Billing;

namespace MINV.Application.Billing;

/// <summary>
/// V4.1 · Monto literal de la representación gráfica: «Ciento veinte 50/100 Bolivianos» (la representación antepone
/// «Son: »). La parte entera va en palabras (español, formato oración) y los centavos SIEMPRE con dos dígitos sobre 100,
/// después de redondear HALF-UP a 2 decimales (<see cref="FiscalRules.Round2"/>). El número queda solo antes de la
/// fracción, por eso termina en «uno» (1 → «Uno», 21 → «Veintiuno»); delante de «mil» y «millón» se apocopa
/// («veintiún mil», «un millón»).
/// </summary>
public static class AmountInWords
{
    /// <summary>Mayor monto admitido: 999 999 999 999,99 (novecientos noventa y nueve mil millones…).</summary>
    public const decimal MaxAmount = 999_999_999_999.99m;

    private static readonly string[] Units =
    [
        "cero", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve", "diez", "once", "doce", "trece", "catorce",
        "quince", "dieciséis", "diecisiete", "dieciocho", "diecinueve", "veinte", "veintiuno", "veintidós", "veintitrés", "veinticuatro",
        "veinticinco", "veintiséis", "veintisiete", "veintiocho", "veintinueve",
    ];

    private static readonly string[] Tens = ["", "", "", "treinta", "cuarenta", "cincuenta", "sesenta", "setenta", "ochenta", "noventa"];

    private static readonly string[] Hundreds =
    [
        "", "ciento", "doscientos", "trescientos", "cuatrocientos", "quinientos", "seiscientos", "setecientos", "ochocientos", "novecientos",
    ];

    /// <summary>Texto en español del monto con centavos en fracción /100 y la moneda (por defecto «Bolivianos»).</summary>
    public static string Bolivianos(decimal amount, string currency = "Bolivianos")
    {
        if (amount < 0 || amount > MaxAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "El monto literal admite de 0 a 999 999 999 999,99.");
        }
        var rounded = FiscalRules.Round2(amount);
        var integer = decimal.Truncate(rounded);
        var cents = (int)((rounded - integer) * 100m);
        var words = Words((long)integer, apocopate: false);
        var text = $"{words} {cents.ToString("D2", CultureInfo.InvariantCulture)}/100";
        if (!string.IsNullOrWhiteSpace(currency))
        {
            text += " " + currency.Trim();
        }
        return char.ToUpper(text[0], CultureInfo.GetCultureInfo("es-BO")) + text[1..];
    }

    /// <summary>Número entero en palabras (en minúsculas). <paramref name="apocopate"/>: «un» en vez de «uno» (delante de
    /// un sustantivo: «veintiún mil», «un millón»).</summary>
    public static string Words(long number, bool apocopate = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(number);
        if (number == 0)
        {
            return Units[0];
        }
        var parts = new List<string>(4);
        var millions = number / 1_000_000;
        var rest = number % 1_000_000;
        if (millions > 0)
        {
            parts.Add(millions == 1 ? "un millón" : Words(millions, apocopate: true) + " millones");
        }
        var thousands = rest / 1000;
        var units = rest % 1000;
        if (thousands > 0)
        {
            parts.Add(thousands == 1 ? "mil" : BelowThousand((int)thousands, apocopate: true) + " mil");
        }
        if (units > 0)
        {
            parts.Add(BelowThousand((int)units, apocopate));
        }
        return string.Join(' ', parts);
    }

    private static string BelowThousand(int number, bool apocopate)
    {
        if (number == 100)
        {
            return "cien";
        }
        var hundreds = number / 100;
        var rest = number % 100;
        var tail = rest == 0 ? string.Empty : BelowHundred(rest, apocopate);
        if (hundreds == 0)
        {
            return tail;
        }
        return tail.Length == 0 ? Hundreds[hundreds] : Hundreds[hundreds] + " " + tail;
    }

    private static string BelowHundred(int number, bool apocopate)
    {
        if (number < 30)
        {
            return apocopate ? number switch { 1 => "un", 21 => "veintiún", _ => Units[number] } : Units[number];
        }
        var unit = number % 10;
        var tens = Tens[number / 10];
        if (unit == 0)
        {
            return tens;
        }
        return tens + " y " + (apocopate && unit == 1 ? "un" : Units[unit]);
    }
}
