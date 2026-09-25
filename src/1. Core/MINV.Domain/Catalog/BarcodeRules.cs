using MINV.Domain.Common;

namespace MINV.Domain.Catalog;

/// <summary>Validación de códigos de barras: longitud fija, solo dígitos y dígito de control GTIN (EAN-8, UPC-A,
/// EAN-13, GTIN-14) cuando la simbología lo exige.</summary>
public static class BarcodeRules
{
    public static string Validate(BarcodeType type, string code)
    {
        ArgumentNullException.ThrowIfNull(type);
        var c = Guard.Text(code, "El código de barras", 64);
        if (type.Length is { } length)
        {
            Guard.That(c.Length == length, "barcode.length", $"Un código {type.Code} tiene {length} caracteres.");
        }
        if (type.HasCheckDigit)
        {
            Guard.That(c.All(char.IsAsciiDigit), "barcode.digits", $"Un código {type.Code} solo admite dígitos.");
            Guard.That(IsValidGtin(c), "barcode.check_digit",
                $"El dígito de control de {c} no es válido (debería ser {ComputeGtinCheckDigit(c[..^1])}).");
        }
        return c;
    }

    /// <summary>Dígito de control GTIN (módulo 10 con pesos 3 y 1 desde la derecha).</summary>
    public static char ComputeGtinCheckDigit(string digitsWithoutCheck)
    {
        var sum = 0;
        for (var i = 0; i < digitsWithoutCheck.Length; i++)
        {
            var d = digitsWithoutCheck[digitsWithoutCheck.Length - 1 - i] - '0';
            sum += i % 2 == 0 ? d * 3 : d;
        }
        return (char)('0' + (10 - sum % 10) % 10);
    }

    public static bool IsValidGtin(string digits) =>
        digits.Length >= 8 && digits.All(char.IsAsciiDigit) && ComputeGtinCheckDigit(digits[..^1]) == digits[^1];
}
