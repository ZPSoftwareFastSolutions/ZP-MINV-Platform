using System.Text.Json;
using MINV.Application.Common;
using MINV.Application.Integration;
using MINV.Application.Storefront;
using MINV.Application.Tech;
using MINV.Domain.Common;
using MINV.Domain.Sales;

namespace MINV.Application.Tests.Storefront;

/// <summary>V7 · Contrato de las reservas (regla P-05): plazo para recogerla, tipo, datos para la factura, idempotencia con los
/// campos nuevos, validación de los textos de una línea y auditoría enmascarada. Sin base de datos.</summary>
public sealed class ReservationContractTests
{
    private static readonly StorefrontContactInput Contact = new("Ana Quispe", "+591 71234567", "Ana@Correo.example");

    private static CreateStorefrontReservationCommand Command(PcBuildKind kind = PcBuildKind.Build, int? holdDays = null, ReservationBuyerInput? buyer = null,
        string? notes = "Paso el sábado", string name = "Ana Quispe", string? reservation = null) =>
        new([new StorefrontReservationLineInput("case-cor-4000d", 1, "Case")], Contact with { Name = name }, notes, "llave-1", reservation, kind, holdDays, buyer);

    [Fact]
    public void El_plazo_sale_de_los_dias_pedidos_o_de_las_horas_configuradas_y_nunca_pasa_del_tope()
    {
        var defaults = new StorefrontOptions();
        Assert.Equal((48, 72, 3), (defaults.EffectiveHours, defaults.EffectiveMaxHours, defaults.MaxHoldDays));
        Assert.Equal(48, defaults.HoursFor(null));
        Assert.Equal((24, 48, 72), (defaults.HoursFor(1), defaults.HoursFor(2), defaults.HoursFor(3)));
        Assert.Equal("storefront.hold_days", Assert.Throws<DomainException>(() => defaults.HoursFor(4)).Code);
        Assert.Equal("storefront.hold_days", Assert.Throws<DomainException>(() => defaults.HoursFor(0)).Code);

        // Un tope menor acota los días que se pueden pedir y las horas por defecto
        var oneDay = new StorefrontOptions(48, 24);
        Assert.Equal((24, 1), (oneDay.EffectiveHours, oneDay.MaxHoldDays));
        Assert.Equal(24, oneDay.HoursFor(1));
        Assert.Equal("storefront.hold_days", Assert.Throws<DomainException>(() => oneDay.HoursFor(2)).Code);
        var halfDay = new StorefrontOptions(6, 12);
        Assert.Equal((6, 1, 12), (halfDay.EffectiveHours, halfDay.MaxHoldDays, halfDay.HoursFor(1)));
        // Un tope mayor no da más de los 3 días del contrato; las horas configuradas nunca pasan del tope
        var wide = new StorefrontOptions(96, 240);
        Assert.Equal((96, 3, 72), (wide.EffectiveHours, wide.MaxHoldDays, wide.HoursFor(3)));
        Assert.Equal(72, new StorefrontOptions(96).EffectiveHours);
        Assert.Equal((1, 1), (new StorefrontOptions(0, 0).EffectiveHours, new StorefrontOptions(0, 0).MaxHoldDays));
    }

    [Fact]
    public void El_tipo_del_contrato_es_build_por_defecto_o_cart()
    {
        Assert.Equal(PcBuildKind.Build, StorefrontKinds.Parse(null));
        Assert.Equal(PcBuildKind.Build, StorefrontKinds.Parse("  "));
        Assert.Equal(PcBuildKind.Build, StorefrontKinds.Parse("build"));
        Assert.Equal(PcBuildKind.Cart, StorefrontKinds.Parse("cart"));
        Assert.Equal(PcBuildKind.Cart, StorefrontKinds.Parse(" CART "));
        var error = Assert.Throws<RequestValidationException>(() => StorefrontKinds.Parse("pedido"));
        Assert.Contains("pedido", Assert.Single(error.Errors), StringComparison.Ordinal);
        Assert.Equal(("build", "cart"), (StorefrontKinds.Text(PcBuildKind.Build), StorefrontKinds.Text(PcBuildKind.Cart)));
        // La vista de una reserva guardada antes de la V7 se lee como armado y sin correo encolado
        var view = new StorefrontReservationView("ARM-WEB-000001", "Reserved", "Reservada", DateTimeOffset.UtcNow, null, 10m, "Ana", "CM", null, false, []);
        Assert.Equal(("build", false), (view.Kind, view.MailQueued));
    }

    [Fact]
    public void El_hash_de_idempotencia_incluye_los_campos_nuevos_y_conserva_el_de_la_V6()
    {
        // Sin campos nuevos, el contenido normalizado es EXACTAMENTE el de la V6 (una repetición anterior sigue coincidiendo)
        var v6 = ApiKeyTokens.Hash("CASE-COR-4000D:1:case;|Ana Quispe|59171234567|ana@correo.example|Paso el sábado|");
        Assert.Equal(v6, CreateStorefrontReservationHandler.ContentHash(Command()));
        Assert.Equal(v6, CreateStorefrontReservationHandler.ContentHash(Command(kind: PcBuildKind.Build, holdDays: null, buyer: null)));

        var buyer = new ReservationBuyerInput(1, "4567890", "1a", "Ana Quispe");
        var hashes = new[]
        {
            CreateStorefrontReservationHandler.ContentHash(Command()),
            CreateStorefrontReservationHandler.ContentHash(Command(kind: PcBuildKind.Cart)),
            CreateStorefrontReservationHandler.ContentHash(Command(holdDays: 1)),
            CreateStorefrontReservationHandler.ContentHash(Command(holdDays: 2)),
            CreateStorefrontReservationHandler.ContentHash(Command(buyer: buyer)),
            CreateStorefrontReservationHandler.ContentHash(Command(buyer: buyer with { DocumentType = 3 })),
            CreateStorefrontReservationHandler.ContentHash(Command(buyer: buyer with { DocumentNumber = "4567891" })),
            CreateStorefrontReservationHandler.ContentHash(Command(buyer: buyer with { Complement = null })),
            CreateStorefrontReservationHandler.ContentHash(Command(buyer: buyer with { Name = "Otra razón social" })),
            CreateStorefrontReservationHandler.ContentHash(Command(PcBuildKind.Cart, 3, buyer)),
        };
        Assert.Equal(hashes.Length, hashes.Distinct(StringComparer.Ordinal).Count());
        // Normalizado: espacios de los extremos y mayúsculas del complemento no cambian el contenido
        Assert.Equal(CreateStorefrontReservationHandler.ContentHash(Command(buyer: buyer)),
            CreateStorefrontReservationHandler.ContentHash(Command(buyer: new ReservationBuyerInput(1, " 4567890 ", "1A ", " Ana Quispe "))));
    }

    [Fact]
    public void La_auditoria_enmascara_el_telefono_el_correo_y_los_datos_para_la_factura()
    {
        var command = Command(PcBuildKind.Cart, 2, new ReservationBuyerInput(5, "1023456029", null, "Comercial Andina S.R.L."));
        var audit = JsonSerializer.Serialize(command.AuditDetails);
        Assert.DoesNotContain("71234567", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("Correo.example", audit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1023456029", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("Andina", audit, StringComparison.Ordinal);
        Assert.Contains("*******029", audit, StringComparison.Ordinal);       // el documento, como el teléfono: solo los 3 últimos
        Assert.Contains("\"HoldDays\":2", audit, StringComparison.Ordinal);
        Assert.Contains("\"DocumentType\":5", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("1023456029", command.Buyer!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("1023456029", command.ToString(), StringComparison.Ordinal);

        var withComplement = JsonSerializer.Serialize(Command(buyer: new ReservationBuyerInput(1, "4567890", "1A", null)).AuditDetails);
        Assert.DoesNotContain("4567890", withComplement, StringComparison.Ordinal);
        Assert.DoesNotContain("1A", withComplement, StringComparison.Ordinal);
        Assert.Contains("\"Buyer\":null", JsonSerializer.Serialize(Command().AuditDetails), StringComparison.Ordinal);

        // El carrito de mostrador audita igual
        var counter = new ReserveCartCommand([new CartItemInput("MON-LG-24GS60F")], "Luis Rojas", "76543210", "luis@correo.example", null, 1,
            new ReservationBuyerInput(1, "7654321", null, "Luis Rojas Vaca"), "CF");
        var counterAudit = JsonSerializer.Serialize(counter.AuditDetails);
        Assert.DoesNotContain("76543210", counterAudit, StringComparison.Ordinal);
        Assert.DoesNotContain("luis@correo.example", counterAudit, StringComparison.Ordinal);
        Assert.DoesNotContain("7654321", counterAudit, StringComparison.Ordinal);
        Assert.DoesNotContain("Vaca", counterAudit, StringComparison.Ordinal);
        Assert.Contains("MON-LG-24GS60F", counterAudit, StringComparison.Ordinal);
        Assert.DoesNotContain("76543210", counter.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_respuesta_con_datos_personales_se_audita_enmascarada_y_viaja_completa()
    {
        var row = new PcBuildRow(Guid.NewGuid(), "RES-CM-000001", "Reserva de Luis Rojas", "CM", null, PcBuildStatus.Reserved, new DateOnly(2026, 9, 30), false,
            2499m, 2, true, DateTimeOffset.UtcNow, null, false, PcBuildChannel.Desktop, "Luis Rojas", "76543210", "luis@correo.example",
            DateTimeOffset.UtcNow.AddHours(48), false, null, "Pasa el lunes", 2, PcBuildKind.Cart, 1, "7654321", "1A", "Luis Rojas Vaca");
        var audit = new FakeAudit();
        var behavior = new MINV.Application.Behaviors.AuditBehavior<ReserveCartCommand, PcBuildRow>(audit);
        var command = new ReserveCartCommand([new CartItemInput("MON-LG-24GS60F")], "Luis Rojas", "76543210", "luis@correo.example", "Pasa el lunes", 2,
            new ReservationBuyerInput(1, "7654321", "1A", "Luis Rojas Vaca"));
        var response = await behavior.Handle(command, _ => Task.FromResult(row), CancellationToken.None);

        // Quien llamó recibe la fila completa (tiene sales.pcbuild.manage) y su JSON no lleva el campo de la auditoría
        Assert.Same(row, response);
        var wire = JsonSerializer.Serialize(response);
        Assert.Contains("76543210", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("AuditResult", wire, StringComparison.Ordinal);
        // La auditoría guarda la petición y la respuesta sin el teléfono, el correo ni los datos para la factura
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("ReserveCart", entry.Action);
        Assert.Contains("RES-CM-000001", entry.Details, StringComparison.Ordinal);
        Assert.Contains("*****210", entry.Details, StringComparison.Ordinal);
        Assert.Contains("****321", entry.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("76543210", entry.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("7654321", entry.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("luis@correo.example", entry.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("Vaca", entry.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("1A", entry.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void La_validacion_rechaza_dias_fuera_de_rango_datos_de_factura_mal_formados_y_textos_con_saltos()
    {
        var validator = new CreateStorefrontReservationValidator();
        Assert.True(validator.Validate(Command()).IsValid);
        Assert.True(validator.Validate(Command(PcBuildKind.Cart, 3, new ReservationBuyerInput(5, "1023456029", null, "Comercial Andina S.R.L."))).IsValid);
        Assert.True(validator.Validate(Command(notes: " Paso el sábado \r\n")).IsValid);   // los saltos de los extremos se recortan al guardar

        string[] Errors(CreateStorefrontReservationCommand c) => validator.Validate(c).Errors.Select(e => e.ErrorMessage).ToArray();
        Assert.Contains(ReservationRules.HoldDaysMessage, Errors(Command(holdDays: 0)));
        Assert.Contains(ReservationRules.HoldDaysMessage, Errors(Command(holdDays: 4)));
        Assert.Contains("El tipo de reserva no existe.", Errors(Command(kind: (PcBuildKind)7)));
        Assert.Contains("El tipo de documento para la factura va de 1 (CI) a 5 (NIT).", Errors(Command(buyer: new ReservationBuyerInput(6, "123"))));
        Assert.Contains("Indique el número de documento (CI o NIT) para la factura.", Errors(Command(buyer: new ReservationBuyerInput(1, " "))));
        Assert.Contains("El número de documento supera 20 caracteres.", Errors(Command(buyer: new ReservationBuyerInput(3, new string('7', 21)))));
        Assert.Contains("El complemento tiene como máximo 5 caracteres.", Errors(Command(buyer: new ReservationBuyerInput(1, "123", "ABCDEF"))));
        Assert.Contains("El nombre o razón social supera 150 caracteres.", Errors(Command(buyer: new ReservationBuyerInput(1, "123", null, new string('x', 151)))));
        Assert.Contains("El nombre o razón social " + ReservationRules.ControlCharacters,
            Errors(Command(buyer: new ReservationBuyerInput(5, "1023456029", null, "Andina\r\nBcc: otro@correo.example"))));
        Assert.Contains("El nombre de contacto " + ReservationRules.ControlCharacters, Errors(Command(name: "Ana\r\nBcc: otro@correo.example")));
        Assert.Contains("Las notas van en una sola línea: " + ReservationRules.ControlCharacters, Errors(Command(notes: "Paso el sábado\npor la mañana")));
        Assert.Contains("El nombre de la reserva " + ReservationRules.ControlCharacters, Errors(Command(reservation: "Mi\tcarrito")));

        var counter = new ReserveCartValidator();
        Assert.True(counter.Validate(new ReserveCartCommand([new CartItemInput("MON-LG-24GS60F")], "Luis Rojas", "76543210")).IsValid);
        string[] CounterErrors(ReserveCartCommand c) => counter.Validate(c).Errors.Select(e => e.ErrorMessage).ToArray();
        Assert.Contains("Agregue al menos un producto al carrito.", CounterErrors(new ReserveCartCommand([], "Luis Rojas", "76543210")));
        Assert.Contains("Indique el nombre de quien recoge la reserva.", CounterErrors(new ReserveCartCommand([new CartItemInput("X")], " ", "76543210")));
        Assert.Contains("Indique un teléfono o WhatsApp para avisar al cliente.", CounterErrors(new ReserveCartCommand([new CartItemInput("X")], "Luis", "")));
        Assert.Contains($"La cantidad de cada producto va de 1 a {PcBuild.MaxQuantity}.",
            CounterErrors(new ReserveCartCommand([new CartItemInput("X", 17)], "Luis", "76543210")));
        Assert.Contains(ReservationRules.HoldDaysMessage, CounterErrors(new ReserveCartCommand([new CartItemInput("X")], "Luis", "76543210", HoldDays: 5)));
        Assert.Contains("Las notas van en una sola línea: " + ReservationRules.ControlCharacters,
            CounterErrors(new ReserveCartCommand([new CartItemInput("X")], "Luis", "76543210", Notes: "uno\r\ndos")));
        Assert.Contains($"Una reserva admite como máximo {PcBuild.MaxLines} líneas.",
            CounterErrors(new ReserveCartCommand(Enumerable.Range(0, 21).Select(i => new CartItemInput("X" + i)).ToList(), "Luis", "76543210")));
    }
}
