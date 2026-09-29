using System.Globalization;
using System.Net;
using System.Text;
using MINV.Domain.Sales;

namespace MINV.Application.Integration;

/// <summary>V7 · Producto de la reserva en el correo: nombre, SKU, cantidad y precio congelado al reservar.</summary>
public sealed record ReservationMailLine(string Sku, string Name, int Quantity, decimal UnitPrice, decimal Subtotal);

/// <summary>
/// V7 · Todo lo que el correo de una reserva puede decir (regla P-06). NO hay dónde poner las notas, el nombre libre de la
/// reserva, el correo ni el teléfono del cliente: <paramref name="PhoneEnding"/> son, como mucho, los 3 últimos dígitos.
/// <paramref name="PublicUrl"/> es la dirección pública de la tienda (la fija el servidor, nunca el cliente); sin ella el
/// correo sale sin enlace.
/// </summary>
public sealed record ReservationMailModel(string Number, PcBuildKind Kind, string CompanyName, string? ContactName,
    IReadOnlyList<ReservationMailLine> Lines, decimal Total, DateTimeOffset ReservedUntil, string BranchName, string? BranchAddress = null,
    string? PublicUrl = null, string? PhoneEnding = null);

/// <summary>V7 · El correo ya armado: asunto, cuerpo HTML y su alternativa de texto plano.</summary>
public sealed record ReservationMailContent(string Subject, string HtmlBody, string TextBody);

/// <summary>
/// V7 · Plantilla del correo de confirmación de una reserva (regla P-06). Es una función PURA: el mismo modelo da siempre el
/// mismo correo, sin leer la base, el reloj ni la configuración. Reglas que cumple:
/// <list type="bullet">
/// <item>El asunto lleva SOLO datos del servidor: el número de la reserva y el nombre de la empresa, sin saltos de línea.</item>
/// <item>Todo valor variable del HTML va codificado; ninguno llega crudo aunque traiga etiquetas, comillas o saltos de línea.</item>
/// <item>El texto es fijo: no incluye notas ni texto libre del cliente. El saludo usa el nombre de contacto solo si parece un
/// nombre (letras, espacios, punto, apóstrofo y guion); si no, saluda sin nombre.</item>
/// <item>El teléfono nunca sale completo: como mucho, sus 3 últimos dígitos.</item>
/// <item>El único enlace lo arma el servidor con la dirección pública que recibe y el número de la reserva.</item>
/// </list>
/// </summary>
public static class ReservationMailTemplate
{
    /// <summary>Hora de Bolivia (UTC−4, sin horario de verano): la del vencimiento que lee el cliente.</summary>
    public static readonly TimeSpan BoliviaOffset = TimeSpan.FromHours(-4);

    /// <summary>Ruta de la página «Mi reserva» de la tienda web.</summary>
    public const string ReservationPath = "/reserva/";

    public const string LinkText = "Ver mi reserva";

    private const int MaxGreetingName = 80;
    private const int MaxLineLength = 200;

    private static readonly string[] Days = ["domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"];

    private static readonly string[] Months =
    [
        "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre",
    ];

    public static ReservationMailContent Render(ReservationMailModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var number = Code(model.Number);
        var company = Line(model.CompanyName, 150);
        var greeting = GreetingName(model.ContactName) is { } name ? $"Hola, {name}:" : "Hola:";
        var what = model.Kind == PcBuildKind.Build ? "La reserva de su armado de PC está confirmada." : "Su reserva está confirmada.";
        var until = Until(model.ReservedUntil);
        var branch = Line(model.BranchName, 100);
        var address = Line(model.BranchAddress, MaxLineLength);
        var link = Link(model.PublicUrl, number);
        var phone = PhoneEnding(model.PhoneEnding);
        var lines = model.Lines.Select(l => (Sku: Line(l.Sku, 60), Name: Line(l.Name, MaxLineLength), l.Quantity, Unit: Money(l.UnitPrice), Subtotal: Money(l.Subtotal)))
            .ToList();
        var total = Money(model.Total);
        var lookup = phone is null
            ? "Para verla o cancelarla en la tienda web le pediremos el número de la reserva y el teléfono con el que reservó."
            : $"Para verla o cancelarla en la tienda web le pediremos el número de la reserva y el teléfono con el que reservó (termina en {phone}).";

        // ------------------------------------------------------------------------------------------------ texto plano
        var text = new StringBuilder()
            .Append(company).Append("\r\n\r\n")
            .Append(greeting).Append("\r\n\r\n")
            .Append(what).Append(" Presente este código al recogerla:\r\n\r\n")
            .Append("    ").Append(number).Append("\r\n\r\n")
            .Append("Productos\r\n");
        foreach (var l in lines)
        {
            text.Append("- ").Append(l.Quantity.ToString(CultureInfo.InvariantCulture)).Append(" x ").Append(l.Name).Append(" (").Append(l.Sku).Append(") · ")
                .Append(l.Unit).Append(" c/u · ").Append(l.Subtotal).Append("\r\n");
        }
        text.Append("Total: ").Append(total).Append(" (se paga al recoger)\r\n\r\n")
            .Append("Se guarda hasta: ").Append(until).Append("\r\n")
            .Append("Dónde se recoge: ").Append(branch);
        if (address.Length > 0)
        {
            text.Append(" · ").Append(address);
        }
        text.Append("\r\n\r\n");
        if (link is not null)
        {
            text.Append(LinkText).Append(": ").Append(link).Append("\r\n");
        }
        text.Append(lookup).Append("\r\n\r\n")
            .Append("Si usted no hizo esta reserva, no tiene que hacer nada: vence sola y no tiene costo.\r\n")
            .Append("Este es un mensaje automático de ").Append(company).Append(".\r\n");

        // ------------------------------------------------------------------------------------------------ HTML
        var html = new StringBuilder()
            .Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>").Append(Html("Reserva " + number)).Append("</title></head>")
            .Append("<body style=\"margin:0;padding:0;background:#f3f4f6;font-family:Arial,Helvetica,sans-serif;color:#111827;\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f3f4f6;\"><tr><td align=\"center\" style=\"padding:24px 12px;\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:600px;background:#ffffff;border-radius:8px;\">")
            .Append("<tr><td style=\"padding:24px 24px 8px 24px;font-size:18px;font-weight:bold;\">").Append(Html(company)).Append("</td></tr>")
            .Append("<tr><td style=\"padding:8px 24px;font-size:15px;line-height:22px;\"><p style=\"margin:0 0 12px 0;\">").Append(Html(greeting)).Append("</p>")
            .Append("<p style=\"margin:0;\">").Append(Html(what)).Append(" Presente este código al recogerla:</p></td></tr>")
            .Append("<tr><td align=\"center\" style=\"padding:16px 24px;\"><div style=\"display:inline-block;padding:14px 24px;border:2px dashed #2563eb;border-radius:8px;")
            .Append("font-family:Consolas,'Courier New',monospace;font-size:26px;font-weight:bold;letter-spacing:2px;color:#1d4ed8;\">").Append(Html(number))
            .Append("</div></td></tr>")
            .Append("<tr><td style=\"padding:8px 24px;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;font-size:14px;\">")
            .Append("<tr><th align=\"left\" style=\"padding:8px 4px;border-bottom:2px solid #e5e7eb;\">Producto</th>")
            .Append("<th align=\"right\" style=\"padding:8px 4px;border-bottom:2px solid #e5e7eb;\">Cantidad</th>")
            .Append("<th align=\"right\" style=\"padding:8px 4px;border-bottom:2px solid #e5e7eb;\">Precio</th>")
            .Append("<th align=\"right\" style=\"padding:8px 4px;border-bottom:2px solid #e5e7eb;\">Subtotal</th></tr>");
        foreach (var l in lines)
        {
            html.Append("<tr><td style=\"padding:8px 4px;border-bottom:1px solid #e5e7eb;\">").Append(Html(l.Name))
                .Append("<br><span style=\"color:#6b7280;font-size:12px;\">").Append(Html(l.Sku)).Append("</span></td>")
                .Append("<td align=\"right\" style=\"padding:8px 4px;border-bottom:1px solid #e5e7eb;\">").Append(l.Quantity.ToString(CultureInfo.InvariantCulture)).Append("</td>")
                .Append("<td align=\"right\" style=\"padding:8px 4px;border-bottom:1px solid #e5e7eb;white-space:nowrap;\">").Append(Html(l.Unit)).Append("</td>")
                .Append("<td align=\"right\" style=\"padding:8px 4px;border-bottom:1px solid #e5e7eb;white-space:nowrap;\">").Append(Html(l.Subtotal)).Append("</td></tr>");
        }
        html.Append("<tr><td colspan=\"3\" align=\"right\" style=\"padding:10px 4px;font-weight:bold;\">Total</td>")
            .Append("<td align=\"right\" style=\"padding:10px 4px;font-weight:bold;white-space:nowrap;\">").Append(Html(total)).Append("</td></tr></table>")
            .Append("<p style=\"margin:4px 0 0 0;font-size:12px;color:#6b7280;\">Se paga al recoger.</p></td></tr>")
            .Append("<tr><td style=\"padding:12px 24px;font-size:15px;line-height:22px;\">")
            .Append("<p style=\"margin:0 0 8px 0;\"><strong>Se guarda hasta:</strong> ").Append(Html(until)).Append("</p>")
            .Append("<p style=\"margin:0;\"><strong>Dónde se recoge:</strong> ").Append(Html(branch));
        if (address.Length > 0)
        {
            html.Append(" · ").Append(Html(address));
        }
        html.Append("</p></td></tr>");
        if (link is not null)
        {
            html.Append("<tr><td align=\"center\" style=\"padding:12px 24px;\"><a href=\"").Append(Html(link))
                .Append("\" style=\"display:inline-block;padding:12px 24px;background:#2563eb;color:#ffffff;text-decoration:none;border-radius:6px;font-weight:bold;\">")
                .Append(Html(LinkText)).Append("</a></td></tr>");
        }
        html.Append("<tr><td style=\"padding:8px 24px 24px 24px;font-size:13px;line-height:20px;color:#4b5563;\">")
            .Append("<p style=\"margin:0 0 8px 0;\">").Append(Html(lookup)).Append("</p>")
            .Append("<p style=\"margin:0 0 8px 0;\">Si usted no hizo esta reserva, no tiene que hacer nada: vence sola y no tiene costo.</p>")
            .Append("<p style=\"margin:0;\">Este es un mensaje automático de ").Append(Html(company)).Append(".</p></td></tr>")
            .Append("</table></td></tr></table></body></html>");

        return new ReservationMailContent(Subject(number, company), html.ToString(), text.ToString());
    }

    /// <summary>Asunto: solo el número de la reserva y el nombre de la empresa (datos del servidor), en una línea.</summary>
    public static string Subject(string number, string companyName)
    {
        var company = Line(companyName, 150);
        var code = Code(number);
        return company.Length == 0 ? $"Reserva {code}" : $"Reserva {code} · {company}";
    }

    /// <summary>Enlace «Ver mi reserva»: la dirección pública (http o https, sin usuario ni contraseña) + la página de la
    /// reserva + su número. Null si la dirección no está configurada o no sirve.</summary>
    public static string? Link(string? publicUrl, string number)
    {
        var text = publicUrl?.Trim();
        if (string.IsNullOrEmpty(text) || HasControl(text) || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }
        var code = Code(number);
        if (code.Length == 0)
        {
            return null;
        }
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + ReservationPath + Uri.EscapeDataString(code);
    }

    /// <summary>Vencimiento en la hora de Bolivia: «sábado 3 de octubre de 2026, 18:30 (hora de Bolivia)».</summary>
    public static string Until(DateTimeOffset value)
    {
        var local = value.ToOffset(BoliviaOffset);
        return string.Create(CultureInfo.InvariantCulture,
            $"{Days[(int)local.DayOfWeek]} {local.Day} de {Months[local.Month - 1]} de {local.Year}, {local:HH:mm} (hora de Bolivia)");
    }

    /// <summary>Importe en bolivianos: «Bs 1.234,50».</summary>
    public static string Money(decimal value)
    {
        var rounded = decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        var text = Math.Abs(rounded).ToString("#,##0.00", CultureInfo.InvariantCulture).Replace(',', ' ').Replace('.', ',').Replace(' ', '.');
        return rounded < 0 ? "Bs -" + text : "Bs " + text;
    }

    /// <summary>Nombre para el saludo, o null si no parece el nombre de una persona o de una empresa: solo letras, espacios,
    /// punto, apóstrofo y guion, hasta 80 caracteres. Así el saludo no puede llevar un enlace, un número ni un mensaje.</summary>
    public static string? GreetingName(string? value)
    {
        var name = Line(value, int.MaxValue);
        if (name.Length == 0 || name.Length > MaxGreetingName)
        {
            return null;
        }
        foreach (var c in name)
        {
            if (!(char.IsLetter(c) || c is ' ' or '.' or '\'' or '-' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark))
            {
                return null;
            }
        }
        return name.Any(char.IsLetter) ? name : null;
    }

    /// <summary>Como mucho los 3 últimos dígitos del teléfono (nunca el teléfono completo), o null.</summary>
    public static string? PhoneEnding(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits.Length > 3 ? digits[^3..] : digits;
    }

    /// <summary>Número de la reserva: solo letras, números y guion (lo genera el servidor; cualquier otra cosa se descarta).</summary>
    private static string Code(string? value) =>
        new string((value ?? string.Empty).Trim().ToUpperInvariant().Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').ToArray()) is { Length: <= 40 } code
            ? code
            : string.Empty;

    /// <summary>Texto de UNA línea: los caracteres de control (CR, LF, tabulador…) pasan a ser un espacio, los espacios
    /// repetidos se juntan y se recorta al máximo indicado.</summary>
    private static string Line(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }
        var text = new StringBuilder(value.Length);
        var space = false;
        foreach (var c in value.Trim())
        {
            var blank = char.IsControl(c) || char.IsWhiteSpace(c) || c is '\u2028' or '\u2029';
            if (blank)
            {
                space = text.Length > 0;
                continue;
            }
            if (space)
            {
                text.Append(' ');
                space = false;
            }
            text.Append(c);
        }
        return text.Length > maxLength ? text.ToString(0, maxLength).TrimEnd() : text.ToString();
    }

    private static bool HasControl(string value) => value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c));

    private static string Html(string value) => WebUtility.HtmlEncode(value);
}
