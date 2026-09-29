using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Abstractions;
using MINV.Application.Accounts;
using MINV.Application.Storefront;
using MINV.Application.Tech;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Integration;
using MINV.Domain.Sales;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Seeding.Tecnologia;

namespace MINV.Infrastructure.Seeding;

/// <summary>
/// V7 · Plataforma web de la empresa de prueba (regla P-13): las cuentas de cliente (también están en
/// <see cref="SeedResult.Users"/>, con el rol CLIENTE) y las reservas que hicieron desde su cuenta, los carritos de la tienda sin
/// cuenta (uno vigente y uno vencido), el carrito de mostrador y los correos de confirmación que quedaron en la cola.
/// </summary>
/// <param name="Accounts">Cuentas de cliente registradas en la tienda web.</param>
/// <param name="AccountReservations">Reservas hechas desde esas cuentas (un carrito y un armado por cuenta).</param>
/// <param name="AccountBuilds">De esas reservas, las de armados de PC.</param>
/// <param name="ActiveCarts">Carritos de la tienda sin cuenta que siguen vigentes (el de un solo monitor).</param>
/// <param name="ExpiredCarts">Carritos de la tienda sin cuenta que vencieron (los cerró el vencimiento: el stock volvió).</param>
/// <param name="CounterCarts">Carritos reservados en el mostrador de la casa matriz.</param>
/// <param name="QueuedMails">Correos PENDIENTES en la cola al terminar la carga (todos: también los de las reservas web de la V6);
/// nadie los envía durante la carga, salen con el despachador del API Gateway.</param>
public sealed record SeedWeb(int Accounts, int AccountReservations, int AccountBuilds, int ActiveCarts, int ExpiredCarts, int CounterCarts, int QueuedMails);

public sealed partial class LocalDataSeeder
{
    /// <summary>V7 · Rol de las cuentas de cliente en la lista de usuarios de prueba (columna «Rol»).</summary>
    public const string CustomerRoleName = "Cliente";

    /// <summary>Cliente de la tienda web de la empresa de prueba: nombre, correo ficticio (.example), teléfono boliviano, las
    /// categorías de su carrito, el nombre de su armado y el armado del catálogo del que toma las piezas base, y sus notas.</summary>
    private sealed record WebCustomerPlan(string Name, string Email, string Phone, IReadOnlyList<string> CartCategories, string BuildName,
        string BaseBuild, string? Notes);

    private static readonly WebCustomerPlan[] WebCustomers =
    [
        new("Rocío Villca Choque", "rocio.villca@correo.example", "+591 71550321", ["MOU", "PAD"], "Mi PC gamer por partes", "ARM-CM-000002",
            "Paso a recogerlo el sábado por la mañana."),
        new("Marcelo Quisbert Loza", "marcelo.quisbert@correo.example", "76019482", ["AUD", "KEY"], "Actualización de mi PC de oficina", "ARM-SC-000002", null),
    ];

    /// <summary>Contactos de los carritos de la tienda sin cuenta: el vigente (un solo monitor) y el que venció.</summary>
    private static readonly (string Name, string Phone, string Email, string Notes)[] CartContacts =
    [
        ("Paola Rivera Gutiérrez", "+591 76120934", "paola.rivera@correo.example", "Lo recojo el viernes después del trabajo."),
        ("Jorge Salazar Mendoza", "70654213", "jorge.salazar@correo.example", "Es un regalo: ¿lo pueden envolver?"),
    ];

    /// <summary>
    /// V7 · Tienda web de la empresa de prueba (regla P-13) con los MISMOS casos de uso que el servidor en la nube, la tienda y el
    /// escritorio (regla A-13): dos clientes se registran (<see cref="RegisterCustomerAccountCommand"/>, canal web, sucursal de la
    /// tienda, antes de la sesión como en <c>/api/v1/web/account/register</c>) y reservan desde su cuenta
    /// (<see cref="CreateMyReservationCommand"/>) un carrito y un armado; la tienda recibe sin cuenta un carrito de un solo monitor
    /// (vigente, con los datos para la factura) y otro que venció hace días (lo cierra el vencimiento); y un vendedor de la casa
    /// matriz reserva un carrito en el mostrador para un cliente habitual (<see cref="ReserveCartCommand"/>). Todas llevan correo de
    /// contacto: sus confirmaciones quedan en la cola (regla P-06; las envía el despachador del API Gateway).
    /// <para>Las reservas vigentes son de hace unas horas y usan la vigencia configurada (48 h): así ninguna vence antes de un día y
    /// todas vencen si se adelanta el reloj dos días (las pruebas de vencimiento lo hacen). Solo se toman productos con stock de
    /// sobra en la casa matriz (siempre queda al menos una unidad disponible); si algo no alcanza, se informa y se sigue.</para>
    /// </summary>
    private async Task<SeedWeb> WebPlatformAsync(SeedOptions o, SignedIn admin, SignedIn counter, IReadOnlyDictionary<string, string> customerCodes,
        List<SeedUser> users, DateOnly today, DateTimeOffset realNow, Action<DateOnly, int, int> at, Action<string> log, CancellationToken ct)
    {
        var restore = clock.UtcNow;
        var taken = new Dictionary<string, int>(StringComparer.Ordinal);   // lo que apartan estas reservas (el disponible de la carga es de antes)
        var (accounts, accountReservations, accountBuilds, activeCarts, counterCarts) = (0, 0, 0, 0, 0);
        string? expiredNumber = null;
        // Lo «de hoy» ocurre desde las 09:00 o, si la carga corre antes de las 10:00, ayer desde las 18:00 (nunca en el futuro
        // respecto de la hora real, como las reservas web de la V6)
        var earlyToday = realNow.TimeOfDay < new TimeSpan(10, 0, 0);
        var recentDay = earlyToday ? today.AddDays(-1) : today;
        void Recent(int minutes) => at(recentDay, (earlyToday ? 18 : 9) + minutes / 60, minutes % 60);

        decimal Free(string sku) => _stock.Available(MainWarehouse, sku) - taken.GetValueOrDefault(sku);

        void Take(IEnumerable<StorefrontReservationLineInput> lines)
        {
            foreach (var line in lines)
            {
                taken[line.Sku] = taken.GetValueOrDefault(line.Sku) + line.Quantity;
            }
        }

        // Una unidad del producto con más disponible de cada categoría (queda al menos otra), sin ranura: es un carrito
        List<StorefrontReservationLineInput> Pick(IEnumerable<string> categories) =>
            categories.Select(category => _catalog.Products.Where(p => p.Category == category && !p.IsService && Free(p.Sku) >= 2)
                    .OrderByDescending(p => Free(p.Sku)).ThenBy(p => p.Sku, StringComparer.Ordinal).FirstOrDefault())
                .OfType<TechProduct>().Select(p => new StorefrontReservationLineInput(p.Sku)).ToList();

        // Piezas base (procesador, refrigeración, placa, memoria y almacenamiento) de un armado compatible del catálogo, empezando
        // por el preferido, con la ranura que manda el armador de la web; exige procesador y placa con stock
        List<StorefrontReservationLineInput> Kit(string preferred)
        {
            foreach (var build in _catalog.Builds.Where(b => !b.MarkedIncompatible).OrderBy(b => b.Number == preferred ? 0 : 1))
            {
                var parts = build.Lines.Select(l => (Line: l, Slot: _catalog.SlotOf(l)))
                    .Where(x => x.Slot is PcSlot.Cpu or PcSlot.Cooler or PcSlot.Motherboard or PcSlot.Ram or PcSlot.Storage)
                    .Where(x => Free(x.Line.Sku) >= x.Line.Quantity + 1).ToList();
                if (parts.Any(x => x.Slot == PcSlot.Cpu) && parts.Any(x => x.Slot == PcSlot.Motherboard))
                {
                    return parts.Select(x => new StorefrontReservationLineInput(x.Line.Sku, x.Line.Quantity, x.Slot!.Value.ToString().ToLowerInvariant()))
                        .ToList();
                }
            }
            return [];
        }

        // Reserva de la tienda sin cuenta (sesión del administrador en la casa matriz, como las reservas web de la V6)
        async Task<StorefrontReservationView?> StoreCartAsync((string Name, string Phone, string Email, string Notes) contact,
            List<StorefrontReservationLineInput> lines, string key, int? holdDays, ReservationBuyerInput? buyer)
        {
            if (lines.Count == 0)
            {
                log($"… el carrito de {contact.Name} no se registró: no hay stock de sobra en la casa matriz.");
                return null;
            }
            try
            {
                var result = await admin.Send(new CreateStorefrontReservationCommand(lines, new StorefrontContactInput(contact.Name, contact.Phone, contact.Email),
                    contact.Notes, key, null, PcBuildKind.Cart, holdDays, buyer), ct);
                Take(lines);
                return result.Reservation;
            }
            catch (DomainException ex)
            {
                log($"… el carrito de {contact.Name} no se registró ({ex.Message}).");
                return null;
            }
        }

        // Reserva desde la cuenta del cliente (su sesión: contacto, correo y cliente salen de SU cuenta, regla P-04)
        async Task<StorefrontReservationView?> MineAsync(SignedIn customer, string who, List<StorefrontReservationLineInput> lines, PcBuildKind kind,
            string? name, string? notes)
        {
            var what = kind == PcBuildKind.Cart ? "carrito" : "armado";
            if (lines.Count == 0)
            {
                log($"… el {what} de {who} no se registró: no hay stock de sobra en la casa matriz.");
                return null;
            }
            try
            {
                var view = await customer.Send(new CreateMyReservationCommand(lines, kind, null, notes, name), ct);
                Take(lines);
                return view;
            }
            catch (DomainException ex)
            {
                log($"… el {what} de {who} no se registró ({ex.Message}).");
                return null;
            }
        }

        try
        {
            // 1. Un carrito de la tienda que venció: hace tres días, con 1 día para recogerlo (lo cierra el vencimiento, abajo)
            at(today.AddDays(-3), 17, 30);
            expiredNumber = (await StoreCartAsync(CartContacts[1], Pick(["MAND", "CARG"]), "datos-prueba-carrito-vencido", 1, null))?.Number;

            // 2. Dos clientes se registran en la tienda web y reservan desde su cuenta un carrito y un armado
            var minute = 2;
            foreach (var plan in WebCustomers)
            {
                Recent(minute);
                var password = o.SharedPassword ?? NewPassword();
                using var customer = await RegisterAsync(o.TenantCode, plan, password, ct);
                accounts++;
                users.Add(new SeedUser(RoleCodes.Customer, CustomerRoleName, plan.Name, plan.Email, password, BranchMain));
                Recent(minute + 3);
                var cart = await MineAsync(customer, plan.Name, Pick(plan.CartCategories), PcBuildKind.Cart, null, plan.Notes);
                Recent(minute + 9);
                var build = await MineAsync(customer, plan.Name, Kit(plan.BaseBuild), PcBuildKind.Build, plan.BuildName, null);
                accountReservations += (cart is null ? 0 : 1) + (build is null ? 0 : 1);
                accountBuilds += build is null ? 0 : 1;
                log($"… {recentDay:dd/MM/yyyy}: {plan.Name} se registró en la tienda web ({plan.Email}) y reservó desde su cuenta " +
                    string.Join(" y ", new[] { cart is null ? null : $"el carrito {cart.Number}", build is null ? null : $"el armado {build.Number}" }.OfType<string>()) +
                    $" (Bs {(cart?.Total ?? 0) + (build?.Total ?? 0):N2}).");
                minute += 15;
            }

            // 3. Un carrito de la tienda sin cuenta, de un solo monitor, con el CI para la factura: vigente
            Recent(minute + 1);
            var single = await StoreCartAsync(CartContacts[0], Pick(["MON"]), "datos-prueba-carrito-activo", null,
                new ReservationBuyerInput(1, "6043317", null, CartContacts[0].Name));
            activeCarts += single is null ? 0 : 1;

            // 4. Un vendedor de la casa matriz reserva en el mostrador para un cliente habitual (con su CI para la factura)
            Recent(minute + 20);
            var regular = _catalog.Customers.First(c => c.Branch == BranchMain && !c.IsCompany && c.Phone is not null && c.Email is not null);
            var items = Pick(["RED", "CAB"]);
            PcBuildRow? atCounter = null;
            if (items.Count > 0)
            {
                try
                {
                    atCounter = await counter.Send(new ReserveCartCommand(items.Select(l => new CartItemInput(l.Sku, l.Quantity)).ToList(), regular.Name,
                        regular.Phone!, regular.Email, "Pasa a recogerlo el lunes; factura a su nombre.", null,
                        new ReservationBuyerInput(1, regular.DocumentNumber, null, regular.Name), customerCodes[regular.Code]), ct);
                    Take(items);
                    counterCarts++;
                }
                catch (DomainException ex)
                {
                    log($"… el carrito de mostrador de {regular.Name} no se registró ({ex.Message}).");
                }
            }
            log($"… {recentDay:dd/MM/yyyy}: carrito de la tienda de un solo monitor {single?.Number ?? "(sin stock)"} y carrito de mostrador " +
                $"{atCounter?.Number ?? "(sin stock)"} para {regular.Name}.");
        }
        finally
        {
            clock.StartAt(restore);
        }

        // El trabajo en segundo plano del gateway cierra las reservas vencidas: aquí lo hace la carga (el stock vuelve)
        var closed = await admin.Send(new ExpirePcBuildReservationsCommand(), ct);
        var db = admin.Scope.ServiceProvider.GetRequiredService<MinvWriteDbContext>();
        db.ChangeTracker.Clear();
        var expired = expiredNumber is null ? 0
            : await db.PcBuilds.AsNoTracking().CountAsync(b => b.Number == expiredNumber && b.Status == PcBuildStatus.Cancelled, ct);
        var queued = await db.OutgoingMailDispatches.AsNoTracking().CountAsync(d => d.Status == OutgoingMailStatus.Pending, ct);
        log($"Tienda web (V7): {accounts} cuentas de cliente con {accountReservations} reservas propias, {activeCarts} carrito vigente de un solo monitor, " +
            $"{expired} vencido (el vencimiento cerró {closed}) y {counterCarts} de mostrador; {queued} correos de confirmación en la cola.");
        return new SeedWeb(accounts, accountReservations, accountBuilds, activeCarts, expired, counterCarts, queued);
    }

    /// <summary>Como la tienda web (<c>POST /api/v1/web/account/register</c>): el registro corre ANTES de la sesión, en el canal web y
    /// sin ninguna sucursal visible hasta autenticar; al terminar, la misma sesión queda iniciada con la cuenta nueva (su rol CLIENTE,
    /// sus permisos <c>account.*</c> y la sucursal de la tienda).</summary>
    private async Task<SignedIn> RegisterAsync(string tenantCode, WebCustomerPlan plan, string password, CancellationToken ct)
    {
        var scope = services.CreateScope();
        try
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetBranches(new BranchScope(false, [Guid.Empty], null));
            scope.ServiceProvider.GetRequiredService<IRequestOrigin>().Set(RequestChannels.Web, null);
            await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RegisterCustomerAccountCommand(tenantCode, plan.Name, plan.Email, plan.Phone,
                password, BranchMain, "web datos-prueba", "datos-prueba"), ct);
            return new SignedIn(scope);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
