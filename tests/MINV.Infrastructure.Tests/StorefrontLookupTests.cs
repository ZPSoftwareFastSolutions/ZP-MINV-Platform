using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Iam;
using MINV.Application.Remote;
using MINV.Application.Storefront;
using MINV.Application.Tech;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Sales;
using MINV.Infrastructure.Seeding;
using MINV.Infrastructure.Services;

namespace MINV.Infrastructure.Tests;

/// <summary>
/// V7 · Consulta pública de reservas con el código O el teléfono (regla S-06) sobre la empresa de prueba EN MEMORIA, con los
/// mismos manejadores y la misma tubería que el gateway: por código (contacto enmascarado), por teléfono (lista de la
/// empresa, las más nuevas primero, como máximo 10 y de los últimos 90 días), con los dos (la vista completa de siempre),
/// «no existe» sin revelar nada y la cancelación que sigue exigiendo el código Y el teléfono.
/// </summary>
public sealed class StorefrontLookupTests : IAsyncLifetime
{
    private const string Phone = "76100200";   // un teléfono que la carga de prueba no usa
    private ServiceProvider _services = null!;
    private SeedResult _seed = null!;

    public async Task InitializeAsync() => (_services, _seed) = await LocalDataSeederTests.SeedAsync(days: 3, tenant: "LOOK", seed: 31);

    public async Task DisposeAsync() => await _services.DisposeAsync();

    private async Task<IServiceScope> AdminAsync()
    {
        var scope = _services.CreateScope();
        var admin = _seed.Users.First(u => u.RoleCode == RoleCodes.Admin);
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new LoginCommand("LOOK", admin.Email, admin.Password, "pruebas", "7.0"));
        return scope;
    }

    private static Task<T> SendAsync<T>(IServiceScope scope, IRequest<T> request)
    {
        scope.ServiceProvider.GetRequiredService<IMinvDbContext>().ClearTracking();
        return scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
    }

    /// <summary>Un producto con stock de sobra para varias reservas de carrito.</summary>
    private static async Task<string> PlentyAsync(IServiceScope scope) =>
        (await SendAsync(scope, new GetStorefrontCatalogQuery())).Products.Where(p => p.Available >= 15).OrderBy(p => p.Sku, StringComparer.Ordinal).First().Sku;

    private static async Task<StorefrontReservationView> ReserveAsync(IServiceScope scope, string sku, string phone = "+591 " + Phone, string? email = "valentina@correo.example") =>
        (await SendAsync(scope, new CreateStorefrontReservationCommand([new StorefrontReservationLineInput(sku)],
            new StorefrontContactInput("Valentina Aguirre", phone, email), "Paso el sábado", Guid.NewGuid().ToString("N"), Kind: PcBuildKind.Cart))).Reservation;

    private static void AssertMasked(StorefrontReservationView view)
    {
        Assert.True(view.Masked);
        Assert.Equal(("V••• A•••", null, "•••••200"), (view.ContactName, view.Notes, view.MaskedPhone));
        var json = JsonSerializer.Serialize(view, RpcJson.Options);
        foreach (var secret in new[] { Phone, "valentina@", "Valentina", "Aguirre", "Paso el sábado" })
        {
            Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Con_el_codigo_solo_devuelve_la_reserva_con_el_contacto_enmascarado_y_con_los_dos_la_vista_completa()
    {
        using var scope = await AdminAsync();
        var sku = await PlentyAsync(scope);
        var created = await ReserveAsync(scope, sku);
        // Reservar devuelve la vista de siempre (quien reservó ya lo sabe todo): sin campos de consulta
        Assert.Equal(("Valentina Aguirre", "Paso el sábado", false, null), (created.ContactName, created.Notes, created.Masked, created.MaskedPhone));

        // Solo el código (en minúsculas y con espacios también): la misma reserva, con el contacto enmascarado
        var byCode = await SendAsync(scope, new GetStorefrontReservationQuery($" {created.Number.ToLowerInvariant()} "));
        Assert.Equal((created.Number, "Reserved", "cart", created.Total, created.ReservedUntil, "CM"),
            (byCode.Number, byCode.Status, byCode.Kind, byCode.Total, byCode.ReservedUntil, byCode.Branch));
        Assert.Equal(created.Lines, byCode.Lines);
        Assert.Equal("v•••@correo.example", byCode.MaskedEmail);
        AssertMasked(byCode);
        // Un teléfono en blanco cuenta como «sin teléfono»
        AssertMasked(await SendAsync(scope, new GetStorefrontReservationQuery(created.Number, "  ")));

        // Con el código Y el teléfono (con o sin +591): la vista completa de la V6 más el contacto enmascarado
        foreach (var phone in new[] { Phone, "+591 761-00200" })
        {
            var full = await SendAsync(scope, new GetStorefrontReservationQuery(created.Number, phone));
            Assert.Equal(("Valentina Aguirre", "Paso el sábado", false), (full.ContactName, full.Notes, full.Masked));
            Assert.Equal(("•••••200", "v•••@correo.example"), (full.MaskedPhone, full.MaskedEmail));
            Assert.DoesNotContain(Phone, JsonSerializer.Serialize(full, RpcJson.Options), StringComparison.Ordinal);
        }

        // Sin correo: no hay correo que enmascarar
        var noMail = await ReserveAsync(scope, sku, email: null);
        Assert.Null((await SendAsync(scope, new GetStorefrontReservationQuery(noMail.Number))).MaskedEmail);
    }

    [Fact]
    public async Task Con_el_telefono_solo_lista_las_reservas_web_de_ese_telefono_las_mas_nuevas_primero_y_enmascaradas()
    {
        using var scope = await AdminAsync();
        var sku = await PlentyAsync(scope);
        // Se guardan con y sin +591 según cómo lo escribió el visitante: las dos formas coinciden
        var first = await ReserveAsync(scope, sku, phone: "+591 " + Phone);
        var second = await ReserveAsync(scope, sku, phone: Phone);
        var other = await ReserveAsync(scope, sku, phone: "76100201");
        // Una reserva del MOSTRADOR con el mismo teléfono no es de la tienda: no sale
        var counter = await SendAsync(scope, new ReserveCartCommand([new CartItemInput(sku)], "Valentina Aguirre", Phone));

        var list = await SendAsync(scope, new GetStorefrontReservationsByPhoneQuery("761-00-200"));
        Assert.Equal([second.Number, first.Number], list.Select(r => r.Number));
        Assert.DoesNotContain(list, r => r.Number == other.Number || r.Number == counter.Number);
        Assert.All(list, AssertMasked);
        Assert.Equal(list.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Number, StringComparer.Ordinal).Select(r => r.Number),
            list.Select(r => r.Number));
        Assert.Equal(JsonSerializer.Serialize(list, RpcJson.Options),
            JsonSerializer.Serialize(await SendAsync(scope, new GetStorefrontReservationsByPhoneQuery("+591" + Phone)), RpcJson.Options));

        // El estado se ve igual que por el código (la cancelada también sale en la lista)
        await SendAsync(scope, new CancelStorefrontReservationCommand(first.Number, Phone));
        var after = await SendAsync(scope, new GetStorefrontReservationsByPhoneQuery(Phone));
        Assert.Equal(("Cancelled", "Reserved"), (after.Single(r => r.Number == first.Number).Status, after.Single(r => r.Number == second.Number).Status));
    }

    [Fact]
    public async Task La_lista_por_telefono_trae_como_maximo_10_y_solo_de_los_ultimos_90_dias()
    {
        using var scope = await AdminAsync();
        var sku = await PlentyAsync(scope);
        var clock = _services.GetRequiredService<DemoClock>();
        var realNow = clock.UtcNow;
        try
        {
            // Una reserva de hace 91 días ya no sale
            clock.StartAt(realNow.AddDays(-91));
            var old = await ReserveAsync(scope, sku);
            clock.StartAt(realNow);
            var numbers = new List<string>();
            for (var i = 0; i < StorefrontPrivacy.PhoneLookupLimit + 1; i++)
            {
                numbers.Add((await ReserveAsync(scope, sku)).Number);
            }
            var list = await SendAsync(scope, new GetStorefrontReservationsByPhoneQuery(Phone));
            Assert.Equal(StorefrontPrivacy.PhoneLookupLimit, list.Count);
            Assert.DoesNotContain(list, r => r.Number == old.Number);
            Assert.Equal(numbers.Skip(1).Reverse(), list.Select(r => r.Number));   // la más vieja de las 11 queda afuera
            // Por el código, la vieja sigue encontrándose
            Assert.Equal(old.Number, (await SendAsync(scope, new GetStorefrontReservationQuery(old.Number))).Number);
        }
        finally
        {
            clock.StartAt(realNow);
        }
    }

    [Fact]
    public async Task No_existe_no_revela_nada_y_cancelar_sigue_exigiendo_el_codigo_y_el_telefono()
    {
        using var scope = await AdminAsync();
        var sku = await PlentyAsync(scope);
        var created = await ReserveAsync(scope, sku);

        // El mismo «no existe» para un código que no existe y para un teléfono que no coincide
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => SendAsync(scope, new GetStorefrontReservationQuery("RES-WEB-999999")));
        Assert.Equal("La reserva RES-WEB-999999 no existe o el teléfono no coincide.", unknown.Message);
        var wrong = await Assert.ThrowsAsync<NotFoundException>(() => SendAsync(scope, new GetStorefrontReservationQuery(created.Number, "79999999")));
        Assert.Equal($"La reserva {created.Number} no existe o el teléfono no coincide.", wrong.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => SendAsync(scope, new GetStorefrontReservationQuery(created.Number, "no es un teléfono")));
        // Un teléfono sin reservas: lista vacía; uno mal formado: el mismo error que al reservar; vacío: validación
        Assert.Empty(await SendAsync(scope, new GetStorefrontReservationsByPhoneQuery("79999999")));
        Assert.Equal("pcbuild.contact_phone",
            (await Assert.ThrowsAsync<DomainException>(() => SendAsync(scope, new GetStorefrontReservationsByPhoneQuery("12")))).Code);
        await Assert.ThrowsAsync<RequestValidationException>(() => SendAsync(scope, new GetStorefrontReservationsByPhoneQuery(" ")));
        await Assert.ThrowsAsync<RequestValidationException>(() => SendAsync(scope, new GetStorefrontReservationQuery("")));

        // Cancelar: sin teléfono, con uno en blanco o con otro, «no existe»; la reserva sigue reservada
        foreach (var phone in new[] { "", "  ", "79999999", null! })
        {
            await Assert.ThrowsAsync<NotFoundException>(() => SendAsync(scope, new CancelStorefrontReservationCommand(created.Number, phone)));
        }
        Assert.Equal("Reserved", (await SendAsync(scope, new GetStorefrontReservationQuery(created.Number))).Status);
        var cancelled = await SendAsync(scope, new CancelStorefrontReservationCommand(created.Number, "+591 " + Phone));
        Assert.Equal(("Cancelled", "Valentina Aguirre", false), (cancelled.Status, cancelled.ContactName, cancelled.Masked));
    }
}
