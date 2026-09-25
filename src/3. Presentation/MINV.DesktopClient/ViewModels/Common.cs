using System.Globalization;
using System.Security.Cryptography;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;

namespace MINV.DesktopClient.ViewModels;

/// <summary>Opción de un combo: texto visible y valor (el combo muestra <see cref="Label"/>).</summary>
public sealed record Choice<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

/// <summary>Período de consulta (reportes, ventas, contabilidad) elegido de un combo.</summary>
public sealed record PeriodOption(string Label, DateOnly From, DateOnly To)
{
    public string RangeText => From == To ? Fmt.Date(From) : $"{Fmt.Date(From)} al {Fmt.Date(To)}";

    public override string ToString() => Label;

    /// <summary>Períodos habituales a partir de «hoy» (el de la empresa: en la demostración, el día de sus datos).</summary>
    public static IReadOnlyList<PeriodOption> Presets(DateOnly today)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var previousMonth = monthStart.AddMonths(-1);
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return
        [
            new("Hoy", today, today),
            new("Ayer", today.AddDays(-1), today.AddDays(-1)),
            new("Esta semana", weekStart, today),
            new("Últimos 7 días", today.AddDays(-6), today),
            new("Este mes", monthStart, today),
            new("Mes anterior", previousMonth, monthStart.AddDays(-1)),
            new("Últimos 30 días", today.AddDays(-29), today),
            new("Últimos 60 días", today.AddDays(-59), today),
            new("Últimos 90 días", today.AddDays(-89), today),
            new("Este año", new DateOnly(today.Year, 1, 1), today),
        ];
    }
}

/// <summary>Fecha elegida de un combo («Hoy», «Mañana», «En 7 días (02/10/2026)»…).</summary>
public sealed record DateOption(string Label, DateOnly Date)
{
    public override string ToString() => Label;

    /// <summary>Hoy y los días anteriores (asientos, registros con fecha de negocio).</summary>
    public static IReadOnlyList<DateOption> Past(DateOnly today, int days)
    {
        var list = new List<DateOption> { new($"Hoy · {Fmt.Date(today)}", today), new($"Ayer · {Fmt.Date(today.AddDays(-1))}", today.AddDays(-1)) };
        for (var i = 2; i < days; i++)
        {
            var d = today.AddDays(-i);
            list.Add(new DateOption($"{Fmt.Culture.TextInfo.ToTitleCase(d.ToString("dddd", Fmt.Culture))} · {Fmt.Date(d)}", d));
        }
        return list;
    }

    /// <summary>Fechas futuras (entrega esperada de una orden de compra).</summary>
    public static IReadOnlyList<DateOption> Future(DateOnly today) =>
    [
        new($"Mañana · {Fmt.Date(today.AddDays(1))}", today.AddDays(1)),
        new($"En 2 días · {Fmt.Date(today.AddDays(2))}", today.AddDays(2)),
        new($"En 3 días · {Fmt.Date(today.AddDays(3))}", today.AddDays(3)),
        new($"En 5 días · {Fmt.Date(today.AddDays(5))}", today.AddDays(5)),
        new($"En 1 semana · {Fmt.Date(today.AddDays(7))}", today.AddDays(7)),
        new($"En 2 semanas · {Fmt.Date(today.AddDays(14))}", today.AddDays(14)),
        new($"En 1 mes · {Fmt.Date(today.AddDays(30))}", today.AddDays(30)),
    ];
}

/// <summary>Tarjeta de indicador (KPI) de los encabezados: título, valor, detalle, ícono y color de la paleta.</summary>
public sealed class KpiCard(string title, string glyph, string brushKey = "Brand", string softKey = "BrandSoft") : ObservableObject
{
    private string _value = "—";
    private string? _detail;

    public string Title { get; } = title;

    public string Glyph { get; } = glyph;

    public string BrushKey { get; } = brushKey;

    public string SoftKey { get; } = softKey;

    public string Value
    {
        get => _value;
        set => Set(ref _value, value);
    }

    public string? Detail
    {
        get => _detail;
        set => Set(ref _detail, value);
    }
}

/// <summary>Fila de un ranking con barra proporcional (reportes).</summary>
public sealed record RankRow(int Rank, string Key, string Name, string QuantityText, string AmountText, string ProfitText, string CountText,
    double Share, string ShareText);

public static class Numbers
{
    /// <summary>Lee un importe o cantidad escrito por una persona (coma o punto decimal). Vacío = 0 si se permite.</summary>
    public static bool TryParse(string? text, out decimal value, bool emptyIsZero = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0;
            return emptyIsZero;
        }
        return Fmt.TryParseQuantity(text.Replace(Fmt.CurrencySymbol, "", StringComparison.OrdinalIgnoreCase), out value);
    }

    public static string Plain(decimal value) => value.ToString(decimal.Truncate(value) == value ? "0" : "0.##", CultureInfo.InvariantCulture);

    /// <summary>Contraseña temporal legible («Rápida-Nube-4821»): la persona la cambia al ingresar.</summary>
    public static string NewPassword()
    {
        string[] words = ["Condor", "Illimani", "Titicaca", "Sajama", "Uyuni", "Llama", "Vicuna", "Quinua", "Mirador", "Tunari", "Andes", "Salar"];
        return $"{words[RandomNumberGenerator.GetInt32(words.Length)]}-{words[RandomNumberGenerator.GetInt32(words.Length)]}-" +
               RandomNumberGenerator.GetInt32(1000, 10000).ToString(CultureInfo.InvariantCulture);
    }
}
