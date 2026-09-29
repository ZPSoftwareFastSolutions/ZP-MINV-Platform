using System.Net;
using System.Text.Json;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Integration;
using MINV.Application.Remote;
using MINV.Domain.Iam;
using MINV.Domain.Integration;
using MINV.Domain.Sales;

namespace MINV.Application.Tests.Integration;

/// <summary>V7 · Correo de la reserva (regla P-06), sin base de datos: la plantilla es una función pura que nunca deja llegar
/// crudo un valor variable (etiquetas, comillas, saltos de línea), arma el asunto solo con datos del servidor, no muestra el
/// teléfono completo y arma el único enlace con la dirección pública que recibe; el puerto no imprime la contraseña; los casos
/// de uso de la cola exigen <c>sales.pcbuild.manage</c> y no dejan el correo del cliente en la auditoría.</summary>
public sealed class ReservationMailContractTests
{
    /// <summary>Sábado 3 de octubre de 2026, 22:30 UTC = 18:30 en Bolivia.</summary>
    private static readonly DateTimeOffset Until = new(2026, 10, 3, 22, 30, 0, TimeSpan.Zero);

    private static ReservationMailModel Model(string? contact = "Ana Quispe", string company = "Tech Zone Gaming S.R.L.", string? url = "https://tienda.example/",
        string product = "Monitor Gamer 27\" <b>QHD</b> & 165 Hz", string? phone = "+591 71234567", PcBuildKind kind = PcBuildKind.Cart) =>
        new("RES-WEB-000123", kind, company, contact,
            [new ReservationMailLine("MON-AOC-27G2", product, 1, 2499.9m, 2499.9m), new ReservationMailLine("JUE-PS5-EAFC", "EA Sports FC 26 (PS5)", 2, 499m, 998m)],
            3497.9m, Until, "Casa Matriz La Paz", "Av. 16 de Julio 1234", url, phone);

    // ------------------------------------------------------------------------------------------------ plantilla
    [Fact]
    public void El_correo_lleva_el_codigo_los_productos_el_total_en_Bs_el_vencimiento_en_hora_de_Bolivia_y_la_sucursal()
    {
        var mail = ReservationMailTemplate.Render(Model());
        Assert.Equal("Reserva RES-WEB-000123 · Tech Zone Gaming S.R.L.", mail.Subject);
        // En el HTML todo valor va codificado (también las letras con tilde: «s&#225;bado»); en el texto plano, tal cual
        foreach (var (body, encode) in new (string, Func<string, string>)[] { (mail.HtmlBody, WebUtility.HtmlEncode), (mail.TextBody, s => s) })
        {
            foreach (var expected in new[]
                     {
                         "RES-WEB-000123", "MON-AOC-27G2", "EA Sports FC 26 (PS5)", "Bs 2.499,90", "Bs 998,00", "Bs 3.497,90",
                         "sábado 3 de octubre de 2026, 18:30 (hora de Bolivia)", "Casa Matriz La Paz", "Av. 16 de Julio 1234",
                         "https://tienda.example/reserva/RES-WEB-000123", ReservationMailTemplate.LinkText, "Hola, Ana Quispe:", "Su reserva está confirmada.",
                     })
            {
                Assert.Contains(encode(expected), body, StringComparison.Ordinal);
            }
        }
        // Cantidad y precio unitario de cada producto
        Assert.Contains("- 2 x EA Sports FC 26 (PS5) (JUE-PS5-EAFC) · Bs 499,00 c/u · Bs 998,00", mail.TextBody, StringComparison.Ordinal);
        Assert.Contains("La reserva de su armado de PC está confirmada.", ReservationMailTemplate.Render(Model(kind: PcBuildKind.Build)).TextBody, StringComparison.Ordinal);
        // Es una función pura: el mismo modelo da siempre el mismo correo
        Assert.Equal(mail, ReservationMailTemplate.Render(Model()));
        // Formatos
        Assert.Equal(("Bs 0,00", "Bs 1.234.567,89", "Bs -5,50"),
            (ReservationMailTemplate.Money(0m), ReservationMailTemplate.Money(1234567.885m), ReservationMailTemplate.Money(-5.5m)));
        Assert.Equal("lunes 28 de septiembre de 2026, 00:05 (hora de Bolivia)",
            ReservationMailTemplate.Until(new DateTimeOffset(2026, 9, 28, 4, 5, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Un_nombre_con_script_comillas_o_saltos_de_linea_nunca_llega_crudo()
    {
        var hostile = "Ana \"<script>alert('x')</script>\"\r\nBcc: spam@correo.example";
        var mail = ReservationMailTemplate.Render(Model(contact: hostile, company: "Tech <i>Zone</i>\r\nBcc: spam@correo.example",
            product: "<img src=x onerror=alert(1)> \"Monitor\"\r\n<script>alert(2)</script>"));
        // El asunto es UNA línea, sin datos del cliente
        Assert.DoesNotContain('\r', mail.Subject);
        Assert.DoesNotContain('\n', mail.Subject);
        Assert.DoesNotContain("Ana", mail.Subject, StringComparison.Ordinal);
        Assert.StartsWith("Reserva RES-WEB-000123 · ", mail.Subject, StringComparison.Ordinal);
        // En el HTML nada llega crudo: ni etiquetas, ni comillas en atributos, ni saltos de línea dentro de un valor
        Assert.DoesNotContain("<script", mail.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", mail.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<i>", mail.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\r\nBcc", mail.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt; &quot;Monitor&quot; &lt;script&gt;alert(2)&lt;/script&gt;", mail.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Tech &lt;i&gt;Zone&lt;/i&gt; Bcc: spam@correo.example", mail.HtmlBody, StringComparison.Ordinal);
        // Un contacto que no parece un nombre no se usa en el saludo (ni en el HTML ni en el texto)
        Assert.Contains("Hola:", mail.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Hola:", mail.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("alert('x')", mail.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("alert('x')", mail.TextBody, StringComparison.Ordinal);
        // En el texto plano cada valor queda en su línea (sin saltos que lo partan)
        Assert.Contains("- 1 x <img src=x onerror=alert(1)> \"Monitor\" <script>alert(2)</script> (MON-AOC-27G2)", mail.TextBody, StringComparison.Ordinal);
        // Las comillas del nombre se codifican también cuando el nombre sí se usa
        Assert.Contains("Hola, Ana O&#39;Brien-Quispe:", ReservationMailTemplate.Render(Model(contact: "Ana O'Brien-Quispe")).HtmlBody, StringComparison.Ordinal);
        Assert.Null(ReservationMailTemplate.GreetingName("Visite https://estafa.example"));
        Assert.Null(ReservationMailTemplate.GreetingName("Ana 71234567"));
        Assert.Equal("María José Peñaranda", ReservationMailTemplate.GreetingName("  María\tJosé   Peñaranda "));
    }

    [Fact]
    public void El_telefono_nunca_sale_completo_y_el_unico_enlace_lo_arma_el_servidor()
    {
        var mail = ReservationMailTemplate.Render(Model());
        foreach (var body in new[] { mail.HtmlBody, mail.TextBody })
        {
            Assert.DoesNotContain("71234567", body, StringComparison.Ordinal);
            Assert.DoesNotContain("1234567", body, StringComparison.Ordinal);
            Assert.Contains("termina en 567", body, StringComparison.Ordinal);
        }
        Assert.Equal(("567", "12", (string?)null), (ReservationMailTemplate.PhoneEnding("+591 7123-4567"), ReservationMailTemplate.PhoneEnding("12"),
            ReservationMailTemplate.PhoneEnding(" - ")));
        Assert.DoesNotContain("termina en", ReservationMailTemplate.Render(Model(phone: null)).TextBody, StringComparison.Ordinal);

        // El enlace: la dirección pública (http o https, sin credenciales) + /reserva/ + el número; si no sirve, no hay enlace
        Assert.Equal("https://tienda.example/reserva/RES-WEB-000123", ReservationMailTemplate.Link(" https://tienda.example ", "res-web-000123"));
        Assert.Equal("https://tienda.example/tienda/reserva/RES-WEB-000123", ReservationMailTemplate.Link("https://tienda.example/tienda/?x=1#y", "RES-WEB-000123"));
        foreach (var bad in new[] { null, "", "javascript:alert(1)", "ftp://tienda.example", "https://usuario:clave@tienda.example", "tienda.example",
                     "https://tienda.example/\r\nBcc" })
        {
            Assert.Null(ReservationMailTemplate.Link(bad, "RES-WEB-000123"));
        }
        Assert.Null(ReservationMailTemplate.Link("https://tienda.example", "<>\""));
        var noLink = ReservationMailTemplate.Render(Model(url: null));
        Assert.DoesNotContain("<a ", noLink.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain(ReservationMailTemplate.LinkText, noLink.TextBody, StringComparison.Ordinal);
        // El único enlace del HTML es el de la reserva
        Assert.Single(mail.HtmlBody.Split("<a ", StringSplitOptions.None).Skip(1));
        Assert.Contains("href=\"https://tienda.example/reserva/RES-WEB-000123\"", mail.HtmlBody, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ puerto
    [Fact]
    public void El_puerto_de_correo_nunca_imprime_la_contrasena()
    {
        var server = new MailServer("smtp.gmail.com", 587, true, "reservas@techzone.example", "clave-de-aplicacion-secreta", "reservas@techzone.example", "Tech Zone");
        Assert.DoesNotContain("clave-de-aplicacion-secreta", server.ToString(), StringComparison.Ordinal);
        Assert.Contains("***", server.ToString(), StringComparison.Ordinal);
        Assert.Contains("smtp.gmail.com:587", server.ToString(), StringComparison.Ordinal);
        var message = new MailMessageSpec(server, "ana@correo.example", "Reserva RES-WEB-000123", "<p>cuerpo privado</p>", [], "cuerpo privado", "<minv-1@techzone.example>");
        Assert.DoesNotContain("clave-de-aplicacion-secreta", message.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("cuerpo privado", message.ToString(), StringComparison.Ordinal);
        Assert.Equal(("cuerpo privado", "<minv-1@techzone.example>"), (message.TextBody, message.MessageId));
        // Quien arma un mensaje como en la V6 (sin texto plano ni Message-ID) sigue compilando y obtiene los valores por defecto
        var legacy = new MailMessageSpec(server, "ana@correo.example", "Factura", "<p>x</p>", []);
        Assert.Equal(((string?)null, (string?)null), (legacy.TextBody, legacy.MessageId));
        // Lo que se guarda de un error del servidor no lleva la contraseña ni el usuario
        Assert.Equal("535 Auth failed for *** with ***", server.Redact("535 Auth failed for RESERVAS@techzone.example with clave-de-aplicacion-secreta"));
        Assert.Equal(string.Empty, server.Redact(null));
        Assert.Contains("contraseña (ninguna)", new MailServer("localhost", 1025, false, null, null, "a@b.example", "A").ToString(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ casos de uso
    [Fact]
    public void Reenviar_y_ver_la_cola_exigen_gestionar_reservas_y_no_dejan_el_correo_en_la_auditoria()
    {
        foreach (var (request, response, command) in new[]
                 {
                     (typeof(ResendReservationMailCommand), typeof(OutgoingMailRow), true),
                     (typeof(GetOutgoingMailsQuery), typeof(IReadOnlyList<OutgoingMailRow>), false),
                 })
        {
            Assert.True(RpcCatalog.TryResolve(RpcCatalog.NameOf(request), out var resolved, out var answer), request.Name);
            Assert.Equal((request, response), (resolved, answer));
            Assert.Equal([PermissionCodes.PcBuildManage], RpcCatalog.PermissionsOf(request));
            Assert.Equal(command, RpcCatalog.IsCommand(request));
            Assert.False(RpcCatalog.IsAllowedForCustomer(request));
        }

        var resend = new ResendReservationMailCommand("RES-WEB-000123", "ana.quispe@correo.example");
        var audit = JsonSerializer.Serialize(resend.AuditDetails);
        Assert.DoesNotContain("ana.quispe@correo.example", audit, StringComparison.Ordinal);
        Assert.Contains("a***@correo.example", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("ana.quispe", resend.ToString(), StringComparison.Ordinal);
        Assert.Null(JsonSerializer.SerializeToElement(new ResendReservationMailCommand("RES-WEB-000123").AuditDetails).GetProperty("Email").GetString());

        var row = new OutgoingMailRow(Guid.NewGuid(), "RES-WEB-000123", PcBuildKind.Cart, "CM", OutgoingMailKind.ReservationConfirmed, "Confirmación de reserva",
            "ana.quispe@correo.example", OutgoingMailStatus.Pending, "Pendiente", 0, OutgoingMailAttempt.MaxAttempts, null, Until, Until, null, null);
        var audited = JsonSerializer.Serialize(((IAuditableResponse)row).AuditResult);
        Assert.DoesNotContain("ana.quispe@correo.example", audited, StringComparison.Ordinal);
        Assert.Contains("RES-WEB-000123", audited, StringComparison.Ordinal);
        // El enmascarado explícito no viaja en el JSON de la respuesta
        Assert.DoesNotContain("AuditResult", JsonSerializer.Serialize(row, RpcJson.Options), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(("a***@correo.example", "***", "***"),
            (ReservationMail.MaskRecipient(" ana@correo.example "), ReservationMail.MaskRecipient("sin-arroba"), ReservationMail.MaskRecipient(null)));
    }

    [Fact]
    public void La_entrada_del_reenvio_y_de_la_cola_se_valida()
    {
        var resend = new ResendReservationMailValidator();
        Assert.True(resend.Validate(new ResendReservationMailCommand("RES-WEB-000123")).IsValid);
        Assert.True(resend.Validate(new ResendReservationMailCommand("RES-WEB-000123", " Ana@Correo.Example ")).IsValid);
        foreach (var bad in new[] { "ana@correo.example, otro@correo.example", "Ana <ana@correo.example>", "ana@correo.example\r\nBcc: x@y.example", "ana" })
        {
            Assert.False(resend.Validate(new ResendReservationMailCommand("RES-WEB-000123", bad)).IsValid, bad);
        }
        Assert.False(resend.Validate(new ResendReservationMailCommand(" ")).IsValid);

        var queue = new GetOutgoingMailsValidator();
        Assert.True(queue.Validate(new GetOutgoingMailsQuery()).IsValid);
        Assert.True(queue.Validate(new GetOutgoingMailsQuery(OutgoingMailStatus.Exhausted, "RES-WEB-000123", 500)).IsValid);
        Assert.False(queue.Validate(new GetOutgoingMailsQuery(Take: 0)).IsValid);
        Assert.False(queue.Validate(new GetOutgoingMailsQuery(Take: 501)).IsValid);
        Assert.False(queue.Validate(new GetOutgoingMailsQuery((OutgoingMailStatus)42)).IsValid);
        // Los topes que documenta el diseño
        Assert.Equal((3, 300, TimeSpan.FromHours(24)), (ReservationMail.MaxPerRecipient, ReservationMail.MaxPerCompany, ReservationMail.Window));
    }
}
