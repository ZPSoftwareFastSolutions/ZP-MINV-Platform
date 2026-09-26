using System.Globalization;
using MINV.Domain.Common;

namespace MINV.Domain.Billing;

/// <summary>V4.1 · Reglas numéricas y de plazos del SIAT (dominio puro; ver investigación 05, 06 y 03).</summary>
public static class FiscalRules
{
    /// <summary>Redondeo del SIN: HALF-UP a 2 decimales («redondeo tradicional»), SIEMPRE en decimal.</summary>
    public static decimal Round2(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Subtotal de una línea: round2(cantidad × precio) − descuento.</summary>
    public static decimal LineSubtotal(decimal quantity, decimal unitPrice, decimal? discount) =>
        Round2(quantity * unitPrice) - (discount ?? 0m);

    /// <summary>Crédito o débito fiscal (13 % del monto): montoEfectivoCreditoDebito de las notas y débito fiscal de las
    /// ventas.</summary>
    public static decimal Vat(decimal amount) => Round2(amount * SiatCodes.VatRate);

    /// <summary>¿El valor tiene como máximo <paramref name="decimals"/> decimales? (el sector 1 admite 2 en cantidades y
    /// precios).</summary>
    public static bool HasAtMostDecimals(decimal value, int decimals) =>
        decimal.Round(value, decimals, MidpointRounding.AwayFromZero) == value;

    /// <summary>
    /// Último instante para anular (o revertir la anulación de) un documento: fin del día 9 del mes siguiente al de la
    /// emisión, en hora fiscal (la del SIN). Se trata como inclusivo; si el SIN responde 934/3012, manda el SIN.
    /// </summary>
    public static DateTime VoidDeadline(DateTime issuedAt) =>
        new DateTime(issuedAt.Year, issuedAt.Month, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMonths(1).AddDays(9).AddTicks(-1);

    /// <summary>Plazo para emitir una nota crédito-débito sobre una factura: 18 meses.</summary>
    public static DateTime CreditNoteDeadline(DateTime invoiceIssuedAt) => invoiceIssuedAt.AddMonths(18);

    /// <summary>Plazo para registrar un evento significativo (48 h después de su fin) y enviar sus paquetes.</summary>
    public static readonly TimeSpan EventRegistrationWindow = TimeSpan.FromHours(48);

    /// <summary>Plazo para transcribir y enviar las facturas manuales de contingencia (CAFC).</summary>
    public static readonly TimeSpan CafcTranscriptionWindow = TimeSpan.FromHours(72);

    /// <summary>Vigencia ampliada del último CUFD cuando el servicio de CUFD no responde (contada desde su obtención).</summary>
    public static readonly TimeSpan CufdExtendedValidity = TimeSpan.FromHours(72);

    /// <summary>Espera máxima fuera de línea antes de volver a verificar la comunicación.</summary>
    public static readonly TimeSpan MaxOfflineRetryInterval = TimeSpan.FromHours(2);

    /// <summary>
    /// Número de tarjeta como lo exige el SIN: primeros 4 y últimos 4 dígitos en claro, ceros al medio
    /// (p. ej. 4797000000007896). Acepta el número completo o ya enmascarado (con asteriscos o equis).
    /// </summary>
    public static string MaskCard(string cardNumber)
    {
        var digits = new string((cardNumber ?? string.Empty).Where(c => char.IsAsciiDigit(c) || c is '*' or 'x' or 'X').ToArray());
        Guard.That(digits.Length is >= 12 and <= 16, "card.length", "El número de tarjeta debe tener entre 12 y 16 dígitos.");
        var first = digits[..4];
        var last = digits[^4..];
        Guard.That(first.All(char.IsAsciiDigit) && last.All(char.IsAsciiDigit), "card.digits",
            "Los primeros y los últimos 4 dígitos de la tarjeta son obligatorios.");
        return first + new string('0', digits.Length - 8) + last;
    }

    /// <summary>Formato de fecha y hora fiscal del SIN: «UTC extendido» sin zona, con milisegundos.</summary>
    public static string FormatDateTime(DateTime value) => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>Hora fiscal (sin zona) a partir de la hora UTC corregida con el reloj del SIN y la zona de la empresa.</summary>
    public static DateTime ToFiscalTime(DateTimeOffset utcNow, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        return DateTime.SpecifyKind(local.DateTime, DateTimeKind.Unspecified);
    }

    /// <summary>Valida el número de documento del comprador: CI y NIT solo dígitos; complemento solo con CI.</summary>
    public static void EnsureBuyerDocument(int documentType, string number, string? complement)
    {
        Guard.That(documentType is >= SiatCodes.DocumentCi and <= SiatCodes.DocumentNit, "buyer.doc_type",
            "El tipo de documento del comprador va de 1 (CI) a 5 (NIT).");
        var n = (number ?? string.Empty).Trim();
        Guard.That(n.Length is >= 1 and <= 20, "buyer.doc_number", "El número de documento del comprador es obligatorio (hasta 20 caracteres).");
        if (documentType is SiatCodes.DocumentCi or SiatCodes.DocumentNit)
        {
            Guard.That(n.All(char.IsAsciiDigit), "buyer.doc_numeric", "Con CI o NIT el número de documento solo admite dígitos.");
        }
        if (!string.IsNullOrWhiteSpace(complement))
        {
            Guard.That(documentType == SiatCodes.DocumentCi, "buyer.complement", "El complemento solo se usa con cédula de identidad.");
            Guard.That(complement.Trim().Length <= 5, "buyer.complement", "El complemento tiene como máximo 5 caracteres.");
        }
    }
}
