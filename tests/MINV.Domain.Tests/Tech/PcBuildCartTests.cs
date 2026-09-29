using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Events;
using MINV.Domain.Sales;

namespace MINV.Domain.Tests.Tech;

/// <summary>V7 · Carrito (regla P-05): un <see cref="PcBuild"/> de tipo <c>Cart</c> reserva cualquier producto (líneas sin ranura,
/// sin ranura única y sin compatibilidad), nunca se publica, lleva los datos para la factura con las reglas del SIN y sus textos
/// libres son de una línea. El armado de PC sigue exactamente igual.</summary>
public sealed class PcBuildCartTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Branch = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 28);
    private static readonly PcCompatibilityReport Compatible = new([], 255, 350, 650);

    private static readonly PcCompatibilityReport Incompatible =
        new([new PcIssue("SOCKET_CPU_PLACA", true, "El socket no coincide.")], 140, 200, 750);

    private static PcBuild WebCart(string number = "RES-WEB-000001") =>
        PcBuild.CreateWebCart(Tenant, Branch, number, "Reserva de Ana Quispe", "Ana Quispe", "+591 71234567", "ana@correo.example", "Paso el sábado",
            Today.AddDays(1), User, Now);

    private static PcBuild DesktopCart(Guid? customerId = null) =>
        PcBuild.CreateDesktopCart(Tenant, Branch, "RES-CM-000001", "Reserva de mostrador", customerId, Today.AddDays(2), User, Now);

    private static PcBuild Build() => new(Tenant, Branch, "ARM-CM-000001", "PC Gamer", null, Today.AddDays(7), User, Now);

    [Fact]
    public void Un_carrito_reserva_un_solo_monitor_sin_ranura()
    {
        var cart = WebCart();
        Assert.Equal((PcBuildKind.Cart, PcBuildChannel.Web, PcBuildStatus.Draft, true), (cart.Kind, cart.Channel, cart.Status, cart.IsCart));
        var monitor = Guid.NewGuid();
        var line = cart.AddLine(null, monitor, 1, 1699m);
        Assert.Null(line.Slot);
        Assert.Equal((monitor, 1, 1699m, 1699m), (line.VariantId, line.Quantity, line.QuotedUnitPrice, line.Subtotal));

        cart.QuoteCart(Today.AddDays(1), Today, Now, User);
        Assert.Equal((PcBuildStatus.Quoted, false), (cart.Status, cart.QuotedWithErrors));
        var until = Now.AddHours(24);
        cart.Reserve(Now, until, Today, User);
        Assert.Equal((PcBuildStatus.Reserved, (DateTimeOffset?)until, 1699m), (cart.Status, cart.ReservedUntil, cart.Total));
        // Mismos estados y misma bitácora que un armado (regla S-04)
        Assert.Equal([PcBuildEventAction.Created, PcBuildEventAction.Quoted, PcBuildEventAction.Reserved], cart.History.Select(h => h.Action));
        Assert.Contains("Carrito creado desde la tienda web", cart.History.First().Detail, StringComparison.Ordinal);
        // El evento dice que es un carrito y su línea va sin ranura; nunca lleva el contacto
        var reserved = Assert.IsType<PcBuildReservedEvent>(Assert.Single(cart.DomainEvents));
        Assert.Equal(("RES-WEB-000001", "Web", PcBuildEventKinds.Cart, 1699m), (reserved.Number, reserved.Channel, reserved.Kind, reserved.Total));
        Assert.Null(Assert.Single(reserved.Lines).Slot);

        // Vender consume la reserva; liberar y vencer anulan con motivo, igual que en un armado
        cart.ClearDomainEvents();
        cart.MarkSold(Guid.NewGuid(), Today, User, Now.AddHours(2));
        var sold = Assert.IsType<PcBuildSoldEvent>(Assert.Single(cart.DomainEvents));
        Assert.Equal((PcBuildStatus.Sold, true, PcBuildEventKinds.Cart), (cart.Status, sold.WasReserved, sold.Kind));
        var released = WebCart("RES-WEB-000002");
        released.AddLine(null, monitor, 1, 1699m);
        released.QuoteCart(Today.AddDays(1), Today, Now, User);
        released.Reserve(Now, until, Today, User);
        released.ReleaseReservation(PcBuild.ExpiredReason, until.AddMinutes(1), User, expired: true);
        Assert.Equal((PcBuildStatus.Cancelled, PcBuild.ExpiredReason, PcBuildEventAction.Expired),
            (released.Status, released.CancelReason, released.History.Last().Action));
        Assert.Equal(PcBuildEventKinds.Cart, Assert.IsType<PcBuildReleasedEvent>(released.DomainEvents.Last()).Kind);
    }

    [Fact]
    public void Un_carrito_admite_dos_productos_de_la_misma_ranura_unica_y_un_armado_no()
    {
        var cart = DesktopCart();
        cart.AddLine(PcSlot.Psu, Guid.NewGuid(), 1, 649m);
        cart.AddLine(PcSlot.Psu, Guid.NewGuid(), 1, 1299m);       // dos fuentes distintas
        cart.AddLine(PcSlot.Case, Guid.NewGuid(), 2, 1299m);
        cart.AddLine(PcSlot.Case, Guid.NewGuid(), 1, 1199m);      // dos gabinetes distintos
        cart.AddLine(null, Guid.NewGuid(), 1, 829m);              // y dos productos sueltos (un juego y un cable)
        cart.AddLine(null, Guid.NewGuid(), 3, 169m);
        Assert.Equal(6, cart.Lines.Count);
        Assert.Equal(649m + 1299m + 2598m + 1199m + 829m + 507m, cart.Total);
        Assert.Equal(2, cart.Lines.Count(l => l.Slot is null));
        // Los topes de cantidad, de líneas y de ranura válida siguen valiendo en el carrito
        Assert.Equal("pcbuild.quantity", Assert.Throws<DomainException>(() => cart.AddLine(null, Guid.NewGuid(), 17, 1m)).Code);
        Assert.Equal("guard.enum", Assert.Throws<DomainException>(() => cart.AddLine((PcSlot)99, Guid.NewGuid(), 1, 1m)).Code);
        for (var i = cart.Lines.Count; i < PcBuild.MaxLines; i++)
        {
            cart.AddLine(null, Guid.NewGuid(), 1, 10m);
        }
        Assert.Equal("pcbuild.lines", Assert.Throws<DomainException>(() => cart.AddLine(null, Guid.NewGuid(), 1, 10m)).Code);

        // En un armado todo sigue igual: ranura obligatoria y una sola pieza en las ranuras únicas
        var build = Build();
        Assert.Equal((PcBuildKind.Build, false), (build.Kind, build.IsCart));
        build.AddLine(PcSlot.Psu, Guid.NewGuid(), 1, 649m);
        Assert.Equal("pcbuild.slot", Assert.Throws<DomainException>(() => build.AddLine(PcSlot.Psu, Guid.NewGuid(), 1, 1299m)).Code);
        Assert.Equal("pcbuild.slot", Assert.Throws<DomainException>(() => build.AddLine(null, Guid.NewGuid(), 1, 829m)).Code);
        build.AddLine(PcSlot.Monitor, Guid.NewGuid(), 1, 1699m);
        build.AddLine(PcSlot.Monitor, Guid.NewGuid(), 1, 2649m);  // las ranuras múltiples siguen admitiendo varias
        Assert.Equal(3, build.Lines.Count);
        Assert.All(build.Lines, l => Assert.NotNull(l.Slot));
    }

    [Fact]
    public void Cotizar_un_carrito_no_evalua_la_compatibilidad()
    {
        var cart = DesktopCart();
        cart.AddLine(PcSlot.Cpu, Guid.NewGuid(), 1, 1399m);
        cart.AddLine(PcSlot.Motherboard, Guid.NewGuid(), 1, 1249m);
        // Aunque llegue un informe con errores y sin confirmación, el carrito se cotiza y no queda marcado
        cart.Quote(Today.AddDays(2), Today, Incompatible, acceptIncompatible: false, Now, User);
        Assert.Equal((PcBuildStatus.Quoted, false), (cart.Status, cart.QuotedWithErrors));
        Assert.DoesNotContain("compatibilidad", cart.History.Last().Detail, StringComparison.OrdinalIgnoreCase);
        Assert.True(PcCompatibilityReport.Empty.IsCompatible);
        Assert.Empty(PcCompatibilityReport.Empty.Issues);

        // Vacío o con la vigencia pasada tampoco se cotiza
        Assert.Equal("pcbuild.empty", Assert.Throws<DomainException>(() => DesktopCart().QuoteCart(Today, Today, Now, User)).Code);
        var late = DesktopCart();
        late.AddLine(null, Guid.NewGuid(), 1, 10m);
        Assert.Equal("pcbuild.valid_until", Assert.Throws<DomainException>(() => late.QuoteCart(Today.AddDays(-1), Today, Now, User)).Code);

        // El armado sigue exigiendo la confirmación y QuoteCart es solo para carritos
        var build = Build();
        build.AddLine(PcSlot.Cpu, Guid.NewGuid(), 1, 1399m);
        Assert.Equal("pcbuild.incompatible", Assert.Throws<DomainException>(() => build.Quote(Today.AddDays(7), Today, Incompatible, false, Now)).Code);
        Assert.Equal("pcbuild.kind", Assert.Throws<DomainException>(() => build.QuoteCart(Today.AddDays(7), Today, Now)).Code);
        build.Quote(Today.AddDays(7), Today, Incompatible, acceptIncompatible: true, Now);
        Assert.True(build.QuotedWithErrors);
    }

    [Fact]
    public void Un_carrito_no_se_publica_como_armado_sugerido()
    {
        var desktop = DesktopCart();
        desktop.AddLine(null, Guid.NewGuid(), 1, 829m);
        desktop.QuoteCart(Today.AddDays(2), Today, Now, User);
        var error = Assert.Throws<DomainException>(() => desktop.Publish(User, Now));
        Assert.Equal("pcbuild.publish_kind", error.Code);
        Assert.False(desktop.PublishedToWeb);
        Assert.DoesNotContain(desktop.History, h => h.Action == PcBuildEventAction.Published);

        var web = WebCart();
        web.AddLine(null, Guid.NewGuid(), 1, 829m);
        web.QuoteCart(Today.AddDays(1), Today, Now, User);
        web.Reserve(Now, Now.AddHours(48), Today, User);
        Assert.Equal("pcbuild.publish_kind", Assert.Throws<DomainException>(() => web.Publish(User, Now)).Code);

        // Un armado del escritorio cotizado se sigue publicando
        var build = Build();
        build.AddLine(PcSlot.Cpu, Guid.NewGuid(), 1, 1399m);
        build.Quote(Today.AddDays(7), Today, Compatible, false, Now);
        build.Publish(User, Now);
        Assert.True(build.PublishedToWeb);
    }

    [Fact]
    public void Los_datos_para_la_factura_siguen_las_reglas_del_SIN()
    {
        var cart = WebCart();
        Assert.False(cart.HasBuyer);
        cart.SetBuyer(1, " 4567890 ", "1a", "  Ana Quispe Mamani ");
        Assert.True(cart.HasBuyer);
        Assert.Equal((1, "4567890", "1A", "Ana Quispe Mamani"), (cart.BuyerDocumentType, cart.BuyerDocumentNumber, cart.BuyerComplement, cart.BuyerName));
        cart.SetBuyer(5, "1023456029", null, "Comercial Andina S.R.L.");
        Assert.Equal((5, "1023456029", (string?)null, "Comercial Andina S.R.L."),
            (cart.BuyerDocumentType, cart.BuyerDocumentNumber, cart.BuyerComplement, cart.BuyerName));
        cart.SetBuyer(3, "AB-123456", null, null);                 // pasaporte: admite letras
        Assert.Equal((3, "AB-123456", (string?)null), (cart.BuyerDocumentType, cart.BuyerDocumentNumber, cart.BuyerName));

        // Las mismas reglas que Customer.SetFiscalIdentity (FiscalRules.EnsureBuyerDocument)
        Assert.Equal("buyer.doc_numeric", Assert.Throws<DomainException>(() => cart.SetBuyer(1, "45678-LP", null, null)).Code);
        Assert.Equal("buyer.doc_numeric", Assert.Throws<DomainException>(() => cart.SetBuyer(5, "10234 56029", null, null)).Code);
        Assert.Equal("buyer.doc_type", Assert.Throws<DomainException>(() => cart.SetBuyer(9, "4567890", null, null)).Code);
        Assert.Equal("buyer.doc_type", Assert.Throws<DomainException>(() => cart.SetBuyer(0, "4567890", null, null)).Code);
        Assert.Equal("buyer.doc_number", Assert.Throws<DomainException>(() => cart.SetBuyer(1, "  ", null, null)).Code);
        Assert.Equal("buyer.doc_number", Assert.Throws<DomainException>(() => cart.SetBuyer(3, new string('7', 21), null, null)).Code);
        Assert.Equal("buyer.complement", Assert.Throws<DomainException>(() => cart.SetBuyer(5, "1023456029", "1A", null)).Code);
        Assert.Equal("buyer.complement", Assert.Throws<DomainException>(() => cart.SetBuyer(1, "4567890", "ABCDEF", null)).Code);
        // Sin tipo de documento no se guarda nada suelto (ni el número, ni el complemento, ni la razón social)
        Assert.Equal("pcbuild.buyer", Assert.Throws<DomainException>(() => cart.SetBuyer(null, "4567890", null, null)).Code);
        Assert.Equal("pcbuild.buyer", Assert.Throws<DomainException>(() => cart.SetBuyer(null, null, "1A", null)).Code);
        Assert.Equal("pcbuild.buyer", Assert.Throws<DomainException>(() => cart.SetBuyer(null, null, null, "Comercial Andina S.R.L.")).Code);
        // La razón social, el número y el complemento son textos de una línea
        Assert.Equal("guard.control_chars", Assert.Throws<DomainException>(() => cart.SetBuyer(5, "1023456029", null, "Andina\r\nBcc: otro@correo.example")).Code);
        Assert.Equal("guard.control_chars", Assert.Throws<DomainException>(() => cart.SetBuyer(3, "AB\t123", null, null)).Code);
        Assert.Equal("guard.text", Assert.Throws<DomainException>(() => cart.SetBuyer(5, "1023456029", null, new string('x', 151))).Code);
        // Un rechazo no deja los datos a medias
        Assert.Equal((3, "AB-123456", (string?)null), (cart.BuyerDocumentType, cart.BuyerDocumentNumber, cart.BuyerName));

        // Se pueden quitar mientras la reserva sigue abierta; vendida o anulada ya no cambian
        cart.SetBuyer(null, null, null, null);
        Assert.False(cart.HasBuyer);
        Assert.Equal(((int?)null, (string?)null, (string?)null, (string?)null),
            (cart.BuyerDocumentType, cart.BuyerDocumentNumber, cart.BuyerComplement, cart.BuyerName));
        cart.SetBuyer(1, "4567890", null, "Ana Quispe");
        cart.AddLine(null, Guid.NewGuid(), 1, 829m);
        cart.QuoteCart(Today.AddDays(1), Today, Now, User);
        cart.Reserve(Now, Now.AddHours(24), Today, User);
        cart.SetBuyer(1, "4567891", null, "Ana Quispe");           // reservada: todavía se corrige
        cart.MarkSold(Guid.NewGuid(), Today, User, Now);
        Assert.Equal("pcbuild.state", Assert.Throws<DomainException>(() => cart.SetBuyer(1, "4567892", null, null)).Code);
        Assert.Equal("4567891", cart.BuyerDocumentNumber);

        // También un armado de PC puede llevar los datos para la factura
        var build = Build();
        build.SetBuyer(5, "1023456029", null, "Comercial Andina S.R.L.");
        Assert.True(build.HasBuyer);
    }

    [Theory]
    [InlineData("Ana\r\nBcc: otro@correo.example")]
    [InlineData("Ana\nQuispe")]
    [InlineData("Ana\rQuispe")]
    [InlineData("Ana\tQuispe")]
    [InlineData("Ana\0Quispe")]
    [InlineData("Ana\u001bQuispe")]
    [InlineData("Ana\u0085Quispe")]
    [InlineData("Ana\u2028Quispe")]
    [InlineData("Ana\u2029Quispe")]
    public void Los_textos_libres_rechazan_caracteres_de_control(string text)
    {
        Assert.True(Guard.HasControlCharacters(text));
        // Nombre de contacto y notas
        Assert.Equal("guard.control_chars", Assert.Throws<DomainException>(() =>
            PcBuild.CreateWebCart(Tenant, Branch, "RES-WEB-000009", "Reserva", text, "71234567", null, null, Today, User, Now)).Code);
        Assert.Equal("guard.control_chars", Assert.Throws<DomainException>(() =>
            PcBuild.CreateWebCart(Tenant, Branch, "RES-WEB-000009", "Reserva", "Ana", "71234567", null, text, Today, User, Now)).Code);
        Assert.Equal("guard.control_chars", Assert.Throws<DomainException>(() =>
            PcBuild.CreateWeb(Tenant, Branch, "ARM-WEB-000009", "Armado web", text, "71234567", null, null, Today, User, Now)).Code);
        var desktop = Build();
        Assert.Equal("guard.control_chars", Assert.Throws<DomainException>(() => desktop.SetContact("Ana", null, null, text)).Code);
        Assert.Null(desktop.Notes);
        // Nombre del armado o de la reserva (al crear y al renombrar)
        Assert.Equal("guard.control_chars", Assert.Throws<DomainException>(() =>
            new PcBuild(Tenant, Branch, "ARM-CM-000009", text, null, Today, User, Now)).Code);
        Assert.Equal("guard.control_chars", Assert.Throws<DomainException>(() =>
            PcBuild.CreateDesktopCart(Tenant, Branch, "RES-CM-000009", text, null, Today, User, Now)).Code);
        Assert.Equal("guard.control_chars", Assert.Throws<DomainException>(() => desktop.Rename(text, null)).Code);
        // Razón social
        Assert.Equal("guard.control_chars", Assert.Throws<DomainException>(() => desktop.SetBuyer(5, "1023456029", null, text)).Code);
    }

    [Fact]
    public void Los_saltos_de_los_extremos_se_recortan_y_Guard_Text_no_cambia()
    {
        // Lo que se guarda queda limpio: el salto final de un campo de texto no es un error
        var cart = PcBuild.CreateWebCart(Tenant, Branch, "RES-WEB-000003", " Reserva de Ana \r\n", "\tAna Quispe\n", "71234567", null, " Paso el sábado \r\n",
            Today, User, Now);
        Assert.Equal(("Reserva de Ana", "Ana Quispe", "Paso el sábado"), (cart.Name, cart.ContactName, cart.Notes));
        Assert.Equal("Tildes, eñes y «comillas»: ¡sí! 😀", Guard.PlainText("Tildes, eñes y «comillas»: ¡sí! 😀", "El texto", 50));
        Assert.Null(Guard.OptionalPlainText("  \r\n ", "El texto", 50));
        Assert.Equal("guard.text", Assert.Throws<DomainException>(() => Guard.PlainText(" ", "El texto", 50)).Code);
        Assert.Equal("guard.text", Assert.Throws<DomainException>(() => Guard.OptionalPlainText(new string('x', 51), "El texto", 50)).Code);
        Assert.False(Guard.HasControlCharacters(null));
        Assert.False(Guard.HasControlCharacters(string.Empty));
        // La regla es nueva: quienes ya usaban Guard.Text y Guard.OptionalText siguen como estaban
        Assert.Equal("uno\ndos", Guard.Text("uno\ndos", "El texto", 50));
        Assert.Equal("uno\tdos", Guard.OptionalText("uno\tdos", "El texto", 50));
    }

    [Fact]
    public void La_numeracion_distingue_carritos_y_armados_y_el_tipo_por_defecto_es_armado()
    {
        Assert.Equal("RES-WEB", PcBuild.NumberPrefixOf(PcBuildKind.Cart, PcBuildChannel.Web));
        Assert.Equal("RES", PcBuild.NumberPrefixOf(PcBuildKind.Cart, PcBuildChannel.Desktop));
        Assert.Equal("ARM-WEB", PcBuild.NumberPrefixOf(PcBuildKind.Build, PcBuildChannel.Web));
        Assert.Equal("ARM", PcBuild.NumberPrefixOf(PcBuildKind.Build, PcBuildChannel.Desktop));
        Assert.Equal((PcBuild.WebNumberPrefix, PcBuild.WebCartNumberPrefix), ("ARM-WEB", "RES-WEB"));

        // El constructor y CreateWeb de la V6 siguen creando armados, con su mismo texto de bitácora y su evento «Build»
        var web = PcBuild.CreateWeb(Tenant, Branch, "ARM-WEB-000001", "Armado web de Ana", "Ana Quispe", "71234567", null, null, Today.AddDays(2), User, Now);
        Assert.Equal((PcBuildKind.Build, PcBuildChannel.Web, "Armado creado desde la tienda web"), (web.Kind, web.Channel, web.History.Single().Detail));
        var desktop = Build();
        Assert.Equal((PcBuildKind.Build, PcBuildChannel.Desktop, "Armado creado"), (desktop.Kind, desktop.Channel, desktop.History.Single().Detail));
        desktop.AddLine(PcSlot.Cpu, Guid.NewGuid(), 1, 1399m);
        desktop.Quote(Today.AddDays(7), Today, Compatible, false, Now, User);
        desktop.Reserve(Now, Now.AddHours(48), Today, User);
        var reserved = Assert.IsType<PcBuildReservedEvent>(Assert.Single(desktop.DomainEvents));
        Assert.Equal((PcBuildEventKinds.Build, "Cpu"), (reserved.Kind, Assert.Single(reserved.Lines).Slot));
        Assert.Equal((nameof(PcBuildKind.Build), nameof(PcBuildKind.Cart)), (PcBuildEventKinds.Build, PcBuildEventKinds.Cart));

        // Carrito de mostrador: canal Desktop, contacto opcional y cliente registrado
        var customer = Guid.NewGuid();
        var counter = DesktopCart(customer);
        Assert.Equal((PcBuildKind.Cart, PcBuildChannel.Desktop, (Guid?)customer, "Carrito creado en el mostrador"),
            (counter.Kind, counter.Channel, counter.CustomerId, counter.History.Single().Detail));
        counter.SetContact("Luis Rojas", "(2) 221-2345", null, null);
        Assert.Equal(("Luis Rojas", "22212345"), (counter.ContactName, counter.ContactPhone));
        // Carrito web de un cliente con cuenta: queda ligado a él; el contacto sigue siendo obligatorio
        var account = PcBuild.CreateWebCart(Tenant, Branch, "RES-WEB-000004", "Reserva de Ana", "Ana Quispe", "71234567", null, null, Today, User, Now, customer);
        Assert.Equal((Guid?)customer, account.CustomerId);
        Assert.Equal("pcbuild.contact_phone", Assert.Throws<DomainException>(() =>
            PcBuild.CreateWebCart(Tenant, Branch, "RES-WEB-000005", "Reserva", "Ana", " ", null, null, Today, User, Now)).Code);
    }
}
