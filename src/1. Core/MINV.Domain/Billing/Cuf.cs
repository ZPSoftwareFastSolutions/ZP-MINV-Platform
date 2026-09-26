using System.Globalization;
using System.Numerics;
using MINV.Domain.Common;

namespace MINV.Domain.Billing;

/// <summary>
/// V4.1 · Código Único de Factura (algoritmo publicado por el SIN: «Generación del Código Único de Factura CUF»,
/// «Algoritmo Módulo 11» y «Base 16»). Verificado con el ejemplo oficial:
/// NIT 123456789, 2019-01-13 16:37:21.231, sucursal 0, modalidad 1, emisión 1, tipo 1, sector 1, número 1, PV 0 y
/// código de control A19E23EF34124CD → <c>8727F63A15F8976591FDDE5B387C5D015A29E06A1A19E23EF34124CD</c>.
/// </summary>
public static class Cuf
{
    /// <summary>Datos que forman el CUF (todos van con ceros a la izquierda).</summary>
    public sealed record Parts(long Nit, DateTime IssuedAt, int BranchCode, int Modality, int EmissionType, int DocumentType,
        int DocumentSector, long Number, int PointOfSaleCode);

    /// <summary>Genera el CUF: cadena de 53 dígitos + Módulo 11 → Base 16 (mayúsculas) + código de control del CUFD.</summary>
    public static string Generate(Parts parts, string controlCode)
    {
        ArgumentNullException.ThrowIfNull(parts);
        Guard.That(!string.IsNullOrWhiteSpace(controlCode), "cuf.control", "Falta el código de control del CUFD.");
        var digits = Digits(parts);
        var withCheck = digits + Mod11(digits);
        return Base16(withCheck) + controlCode.Trim();
    }

    /// <summary>Cadena de 53 dígitos antes del dígito verificador.</summary>
    public static string Digits(Parts parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        Guard.That(parts.Nit is > 0 and <= 9_999_999_999_999, "cuf.nit", "El NIT debe tener entre 1 y 13 dígitos.");
        Guard.That(parts.BranchCode is >= 0 and <= 9999, "cuf.branch", "El código de sucursal va de 0 a 9999.");
        Guard.That(parts.PointOfSaleCode is >= 0 and <= 9999, "cuf.pos", "El código de punto de venta va de 0 a 9999.");
        Guard.That(parts.Number is > 0 and <= 9_999_999_999, "cuf.number", "El número de documento va de 1 a 9999999999.");
        Guard.That(parts.Modality is >= 1 and <= 9 && parts.EmissionType is >= 1 and <= 9 && parts.DocumentType is >= 1 and <= 9,
            "cuf.codes", "Modalidad, tipo de emisión y tipo de documento son de un dígito.");
        Guard.That(parts.DocumentSector is >= 1 and <= 99, "cuf.sector", "El documento sector va de 1 a 99.");
        return string.Concat(
            parts.Nit.ToString("D13", CultureInfo.InvariantCulture),
            parts.IssuedAt.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture),
            parts.BranchCode.ToString("D4", CultureInfo.InvariantCulture),
            parts.Modality.ToString(CultureInfo.InvariantCulture),
            parts.EmissionType.ToString(CultureInfo.InvariantCulture),
            parts.DocumentType.ToString(CultureInfo.InvariantCulture),
            parts.DocumentSector.ToString("D2", CultureInfo.InvariantCulture),
            parts.Number.ToString("D10", CultureInfo.InvariantCulture),
            parts.PointOfSaleCode.ToString("D4", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Dígito autoverificador Módulo 11 del SIN (<c>calculaDigitoMod11(cadena, 1, 9, false)</c>): pesos 2…9 de derecha a
    /// izquierda; resto 10 → «1», resto 11 → «0».
    /// </summary>
    public static string Mod11(string digits)
    {
        Guard.That(!string.IsNullOrEmpty(digits) && digits.All(char.IsAsciiDigit), "cuf.mod11", "El Módulo 11 se calcula sobre dígitos.");
        var sum = 0;
        var weight = 2;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            sum += weight * (digits[i] - '0');
            if (++weight > 9)
            {
                weight = 2;
            }
        }
        var digit = sum % 11;
        return digit switch
        {
            10 => "1",
            11 => "0",
            _ => digit.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Base 16 en mayúsculas de un número decimal grande (sin el «0» inicial que agrega .NET cuando el primer
    /// nibble es ≥ 8).</summary>
    public static string Base16(string decimalDigits)
    {
        var value = BigInteger.Parse(decimalDigits, NumberStyles.None, CultureInfo.InvariantCulture);
        var hex = value.ToString("X", CultureInfo.InvariantCulture).TrimStart('0');
        return hex.Length == 0 ? "0" : hex;
    }

    /// <summary>Decodifica la parte hexadecimal de un CUF (sin el código de control) a sus datos. Devuelve null si el
    /// dígito verificador no coincide (CUF alterado).</summary>
    public static Parts? Decode(string hexWithoutControl)
    {
        if (string.IsNullOrWhiteSpace(hexWithoutControl))
        {
            return null;
        }
        BigInteger value;
        try
        {
            value = BigInteger.Parse("0" + hexWithoutControl.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return null;
        }
        var digits = value.ToString(CultureInfo.InvariantCulture).PadLeft(54, '0');
        if (digits.Length != 54 || Mod11(digits[..53]) != digits[53..])
        {
            return null;
        }
        if (!DateTime.TryParseExact(digits.Substring(13, 17), "yyyyMMddHHmmssfff", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var issuedAt))
        {
            return null;
        }
        var parts = new Parts(
            long.Parse(digits[..13], CultureInfo.InvariantCulture), issuedAt,
            int.Parse(digits.Substring(30, 4), CultureInfo.InvariantCulture),
            digits[34] - '0', digits[35] - '0', digits[36] - '0',
            int.Parse(digits.Substring(37, 2), CultureInfo.InvariantCulture),
            long.Parse(digits.Substring(39, 10), CultureInfo.InvariantCulture),
            int.Parse(digits.Substring(49, 4), CultureInfo.InvariantCulture));
        return parts is { Nit: > 0, Modality: >= 1 and <= 3, EmissionType: >= 1 and <= 3, DocumentType: >= 1 and <= 4, Number: > 0 }
            ? parts
            : null;
    }

    /// <summary>Decodifica un CUF completo. Con el código de control se corta exacto; sin él se prueban los cortes
    /// posibles (la parte hexadecimal de 54 dígitos mide de 34 a 45 caracteres) y se acepta el primero con Módulo 11,
    /// fecha y códigos válidos.</summary>
    public static Parts? DecodeFull(string cuf, string? controlCode = null)
    {
        if (string.IsNullOrWhiteSpace(cuf))
        {
            return null;
        }
        if (!string.IsNullOrEmpty(controlCode) && cuf.EndsWith(controlCode, StringComparison.Ordinal))
        {
            return Decode(cuf[..^controlCode.Length]);
        }
        for (var length = Math.Min(cuf.Length, 45); length >= 34; length--)
        {
            if (Decode(cuf[..length]) is { } parts)
            {
                return parts;
            }
        }
        return null;
    }
}
