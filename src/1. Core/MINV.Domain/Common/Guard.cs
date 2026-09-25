using System.Text.RegularExpressions;

namespace MINV.Domain.Common;

/// <summary>Validaciones de invariantes. Lanzan <see cref="DomainException"/> con un código estable.</summary>
public static partial class Guard
{
    public static Guid NotEmpty(Guid value, string name) =>
        value == Guid.Empty ? throw new DomainException("guard.empty", $"{name} es obligatorio.") : value;

    public static Guid? NotEmptyIfPresent(Guid? value, string name) =>
        value == Guid.Empty ? throw new DomainException("guard.empty", $"{name} no puede ser un identificador vacío.") : value;

    /// <summary>Texto obligatorio, recortado, de 1 a <paramref name="maxLength"/> caracteres.</summary>
    public static string Text(string? value, string name, int maxLength, int minLength = 1)
    {
        var t = value?.Trim() ?? string.Empty;
        if (t.Length < minLength)
        {
            throw new DomainException("guard.text", minLength <= 1
                ? $"{name} es obligatorio."
                : $"{name} debe tener al menos {minLength} caracteres.");
        }
        if (t.Length > maxLength)
        {
            throw new DomainException("guard.text", $"{name} supera {maxLength} caracteres.");
        }
        return t;
    }

    /// <summary>Texto opcional: vacío se guarda como <c>null</c>.</summary>
    public static string? OptionalText(string? value, string name, int maxLength)
    {
        var t = value?.Trim();
        if (string.IsNullOrEmpty(t))
        {
            return null;
        }
        return t.Length > maxLength ? throw new DomainException("guard.text", $"{name} supera {maxLength} caracteres.") : t;
    }

    /// <summary>Código en mayúsculas sin espacios (A-Z, 0-9, guion y guion bajo).</summary>
    public static string Code(string? value, string name, int maxLength)
    {
        var t = Text(value, name, maxLength).ToUpperInvariant();
        return CodePattern().IsMatch(t)
            ? t
            : throw new DomainException("guard.code", $"{name} solo admite letras, números, guion y guion bajo (sin espacios).");
    }

    public static decimal Positive(decimal value, string name) =>
        value > 0 ? value : throw new DomainException("guard.positive", $"{name} debe ser mayor que 0.");

    public static decimal NonNegative(decimal value, string name) =>
        value >= 0 ? value : throw new DomainException("guard.non_negative", $"{name} no puede ser negativo.");

    public static int NonNegative(int value, string name) =>
        value >= 0 ? value : throw new DomainException("guard.non_negative", $"{name} no puede ser negativo.");

    public static decimal Percent(decimal value, string name) =>
        value is >= 0 and <= 100 ? value : throw new DomainException("guard.percent", $"{name} debe estar entre 0 y 100.");

    public static decimal Fraction(decimal value, string name) =>
        value is >= 0 and <= 1 ? value : throw new DomainException("guard.fraction", $"{name} debe estar entre 0 y 1.");

    public static string Email(string? value, string name)
    {
        var t = Text(value, name, 254).ToLowerInvariant();
        return EmailPattern().IsMatch(t) ? t : throw new DomainException("guard.email", $"{name} no es un correo válido.");
    }

    public static string? OptionalEmail(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? null : Email(value, name);

    public static TEnum Defined<TEnum>(TEnum value, string name) where TEnum : struct, Enum =>
        Enum.IsDefined(value) ? value : throw new DomainException("guard.enum", $"{name} no es un valor válido.");

    public static void That(bool condition, string code, string message)
    {
        if (!condition)
        {
            throw new DomainException(code, message);
        }
    }

    [GeneratedRegex("^[A-Z0-9][A-Z0-9_-]*$")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
