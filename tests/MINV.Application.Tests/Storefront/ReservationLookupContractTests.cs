using System.Text.Json;
using MINV.Application.Remote;
using MINV.Application.Storefront;

namespace MINV.Application.Tests.Storefront;

/// <summary>V7 · Contrato de la consulta pública de reservas con el código O el teléfono (regla S-06): cómo se enmascaran el
/// teléfono, el correo y el nombre, qué pide cada consulta y qué campos nuevos lleva la vista. Sin base de datos (los
/// manejadores se prueban sobre la empresa en memoria en <c>MINV.Infrastructure.Tests/StorefrontLookupTests</c>).</summary>
public sealed class ReservationLookupContractTests
{
    [Theory]
    [InlineData("71234567", "•••••567")]
    [InlineData("+59171234567", "•••••567")]
    [InlineData("+591 7123-4567", "•••••567")]
    [InlineData("2212345", "••••345")]
    [InlineData("12", "•••")]
    public void El_telefono_se_enmascara_con_sus_tres_ultimos_digitos_y_sin_el_codigo_de_pais(string phone, string masked) =>
        Assert.Equal(masked, StorefrontPrivacy.MaskPhone(phone));

    [Fact]
    public void Sin_telefono_ni_correo_no_hay_nada_que_enmascarar()
    {
        Assert.Null(StorefrontPrivacy.MaskPhone(null));
        Assert.Null(StorefrontPrivacy.MaskPhone("  "));
        Assert.Null(StorefrontPrivacy.MaskEmail(null));
        Assert.Null(StorefrontPrivacy.MaskEmail(" "));
    }

    [Theory]
    [InlineData("rosa@correo.example", "r•••@correo.example")]
    [InlineData(" Valentina.Aguirre@Correo.example ", "V•••@Correo.example")]
    [InlineData("a@b.example", "a•••@b.example")]
    [InlineData("sin-arroba", "•••")]
    [InlineData("@correo.example", "•••")]
    public void El_correo_se_enmascara_con_la_primera_letra_y_el_dominio(string email, string masked) =>
        Assert.Equal(masked, StorefrontPrivacy.MaskEmail(email));

    [Theory]
    [InlineData("Valentina Aguirre", "V••• A•••")]
    [InlineData("  ana   quispe  mamani ", "A••• Q••• M•••")]
    [InlineData("Ñandú", "Ñ•••")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void El_nombre_se_enmascara_con_la_inicial_de_cada_palabra_sin_revelar_su_largo(string? name, string masked) =>
        Assert.Equal(masked, StorefrontPrivacy.MaskName(name));

    [Fact]
    public void Nada_enmascarado_deja_ver_el_dato_completo()
    {
        const string phone = "+591 71234567", email = "valentina@correo.example", name = "Valentina Aguirre";
        Assert.DoesNotContain("71234567", StorefrontPrivacy.MaskPhone(phone)!, StringComparison.Ordinal);
        Assert.DoesNotContain("valentina", StorefrontPrivacy.MaskEmail(email)!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Valentina", StorefrontPrivacy.MaskName(name), StringComparison.Ordinal);
        Assert.DoesNotContain("Aguirre", StorefrontPrivacy.MaskName(name), StringComparison.Ordinal);
    }

    [Fact]
    public void La_consulta_por_codigo_admite_el_telefono_opcional_y_la_de_telefono_lo_exige()
    {
        var byCode = new GetStorefrontReservationValidator();
        Assert.True(byCode.Validate(new GetStorefrontReservationQuery("RES-WEB-000001")).IsValid);
        Assert.True(byCode.Validate(new GetStorefrontReservationQuery("RES-WEB-000001", "71234567")).IsValid);
        Assert.Contains(byCode.Validate(new GetStorefrontReservationQuery(" ")).Errors, e => e.ErrorMessage == "Indique el código de la reserva.");
        Assert.False(byCode.Validate(new GetStorefrontReservationQuery("RES-WEB-000001", new string('7', 31))).IsValid);
        Assert.Null(new GetStorefrontReservationQuery("ARM-WEB-000001").Phone);

        var byPhone = new GetStorefrontReservationsByPhoneValidator();
        Assert.True(byPhone.Validate(new GetStorefrontReservationsByPhoneQuery("+591 71234567")).IsValid);
        Assert.Contains(byPhone.Validate(new GetStorefrontReservationsByPhoneQuery("")).Errors,
            e => e.ErrorMessage == "Indique el código de la reserva o el teléfono con que la hizo.");
        Assert.False(byPhone.Validate(new GetStorefrontReservationsByPhoneQuery(new string('7', 31))).IsValid);
    }

    [Fact]
    public void Las_dos_consultas_son_lecturas_de_la_tienda_y_la_cancelacion_sigue_pidiendo_el_telefono()
    {
        Assert.Equal(["storefront.read"], RpcCatalog.PermissionsOf(typeof(GetStorefrontReservationQuery)));
        Assert.Equal(["storefront.read"], RpcCatalog.PermissionsOf(typeof(GetStorefrontReservationsByPhoneQuery)));
        Assert.False(RpcCatalog.IsCommand(typeof(GetStorefrontReservationsByPhoneQuery)));
        Assert.Equal(typeof(IReadOnlyList<StorefrontReservationView>), RpcCatalog.ResponseTypeOf(typeof(GetStorefrontReservationsByPhoneQuery)));
        // Cancelar no cambió: el número Y el teléfono (el teléfono enmascarado en la auditoría)
        var cancel = new CancelStorefrontReservationCommand("ARM-WEB-000001", "71234567");
        Assert.DoesNotContain("71234567", JsonSerializer.Serialize(cancel.AuditDetails), StringComparison.Ordinal);
        Assert.Equal((10, 90), (StorefrontPrivacy.PhoneLookupLimit, StorefrontPrivacy.PhoneLookupDays));
    }

    [Fact]
    public void La_vista_de_siempre_no_trae_los_campos_nuevos_de_la_consulta()
    {
        // Reservar, cancelar y la cuenta del cliente devuelven la vista de siempre: sin contacto enmascarado ni «masked»
        var view = new StorefrontReservationView("RES-WEB-000001", "Reserved", "Reservada", DateTimeOffset.UtcNow, null, 10m, "Ana Quispe", "CM", "Notas",
            false, []);
        Assert.Equal((null, null, false), (view.MaskedPhone, view.MaskedEmail, view.Masked));
        var json = JsonSerializer.Serialize(view, RpcJson.Options);
        Assert.Contains("\"masked\":false", json, StringComparison.Ordinal);
        Assert.Contains("\"maskedPhone\":null", json, StringComparison.Ordinal);
    }
}
