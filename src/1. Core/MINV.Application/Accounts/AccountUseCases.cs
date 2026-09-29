using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Storefront;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Sales;

namespace MINV.Application.Accounts;

/// <summary>El usuario de la sesión y SU cliente.</summary>
internal sealed record AccountContext(User User, Customer Customer);

/// <summary>
/// V7 · Única puerta de los casos de uso <c>account.*</c> hacia el cliente (regla P-04): parte del usuario de la sesión, busca
/// SU cuenta en <c>sales.customer_accounts</c> y de ahí sale el cliente. Ningún caso de uso recibe un identificador de cliente
/// ni de usuario, así que no hay forma de pedir los datos de otro. Un usuario sin cuenta (el personal) recibe
/// <c>account.missing</c>.
/// </summary>
internal static class CustomerAccounts
{
    public static async Task<AccountContext> CurrentAsync(IMinvDbContext db, ICurrentUser user, CancellationToken ct, bool tracking = false)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para usar su cuenta.");
        var account = await db.Set<CustomerAccount>().AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId, ct);
        if (account is null || !account.BelongsTo(userId))
        {
            throw Missing();
        }
        var users = tracking ? db.Set<User>() : db.Set<User>().AsNoTracking();
        var customers = tracking ? db.Set<Customer>() : db.Set<Customer>().AsNoTracking();
        var owner = await users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw Missing();
        var customer = await customers.FirstOrDefaultAsync(c => c.Id == account.CustomerId, ct) ?? throw Missing();
        Guard.That(customer.IsActive, "account.inactive", "Su cuenta de cliente está desactivada: consulte en la tienda.");
        return new AccountContext(owner, customer);
    }

    public static MyAccountView View(AccountContext account) =>
        new(account.Customer.Name, account.User.Email, account.Customer.Phone, account.Customer.DocumentType,
            account.Customer.DocumentType is null ? null : account.Customer.TaxId, account.Customer.Complement, account.Customer.Code);

    private static DomainException Missing() =>
        new(AccountRules.Missing, "Su usuario no tiene una cuenta de cliente de la tienda web.");
}

public sealed class GetMyAccountHandler(IMinvDbContext db, ICurrentUser user) : IRequestHandler<GetMyAccountQuery, MyAccountView>
{
    public async Task<MyAccountView> Handle(GetMyAccountQuery request, CancellationToken ct) =>
        CustomerAccounts.View(await CustomerAccounts.CurrentAsync(db, user, ct));
}

public sealed class UpdateMyAccountHandler(IMinvDbContext db, ICurrentUser user) : IRequestHandler<UpdateMyAccountCommand, MyAccountView>
{
    public async Task<MyAccountView> Handle(UpdateMyAccountCommand request, CancellationToken ct)
    {
        var account = await CustomerAccounts.CurrentAsync(db, user, ct, tracking: true);
        var phone = PcBuild.NormalizePhone(request.Phone)
                    ?? throw new DomainException("pcbuild.contact_phone", "Indique un teléfono o WhatsApp.");
        var name = request.Name.Trim();
        account.User.Rename(name);
        // Primero los datos generales (con el documento que ya tenía) y después el documento nuevo: así el número nuevo se
        // valida contra SU tipo y no contra el anterior
        account.Customer.Update(name, account.Customer.TaxId, account.Customer.Email, phone, account.Customer.CustomerCategoryId);
        account.Customer.SetFiscalIdentity(request.DocumentType, request.DocumentType is null ? null : request.DocumentNumber,
            request.DocumentType is null ? null : request.Complement);
        await db.SaveChangesAsync(ct);
        return CustomerAccounts.View(account);
    }
}

public sealed class GetMyReservationsHandler(IMinvDbContext db, ICurrentUser user, IClock clock)
    : IRequestHandler<GetMyReservationsQuery, IReadOnlyList<StorefrontReservationView>>
{
    /// <summary>Tope de reservas que devuelve la consulta (las más nuevas).</summary>
    public const int MaxReservations = 200;

    public async Task<IReadOnlyList<StorefrontReservationView>> Handle(GetMyReservationsQuery request, CancellationToken ct)
    {
        var account = await CustomerAccounts.CurrentAsync(db, user, ct);
        var customerId = account.Customer.Id;
        var builds = await db.Set<PcBuild>().AsNoTracking().Include(b => b.Lines)
            .Where(b => b.CustomerId == customerId && b.ReservedAt != null)
            .OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Number).Take(MaxReservations).ToListAsync(ct);
        var now = clock.UtcNow;
        var views = new List<StorefrontReservationView>(builds.Count);
        foreach (var build in builds)
        {
            views.Add(await StorefrontReservationViews.ViewAsync(db, build, now, ct));
        }
        return views;
    }
}

public sealed class CancelMyReservationHandler(IMinvDbContext db, ICurrentUser user, IClock clock)
    : IRequestHandler<CancelMyReservationCommand, StorefrontReservationView>
{
    public const string Reason = "Cancelada por el cliente desde su cuenta";

    public async Task<StorefrontReservationView> Handle(CancelMyReservationCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para usar su cuenta.");
        var number = request.Number.Trim().ToUpperInvariant();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var account = await CustomerAccounts.CurrentAsync(db, user, ct);
                var customerId = account.Customer.Id;
                // La reserva de otro cliente «no existe»: mismo mensaje que un número que no existe (no se revela nada más)
                var build = await db.Set<PcBuild>().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Number == number && b.CustomerId == customerId, ct)
                            ?? throw new NotFoundException($"La reserva {number} no existe en su cuenta.");
                Guard.That(build.Status == PcBuildStatus.Reserved, "pcbuild.state",
                    $"La reserva {build.Number} está {PcBuild.Describe(build.Status)}: ya no se puede cancelar.");
                var now = clock.UtcNow;
                await PcBuildStock.ReleaseAsync(db, build, ct);
                build.ReleaseReservation(Reason, now, userId);
                await db.SaveChangesAsync(ct);
                return await StorefrontReservationViews.ViewAsync(db, build, now, ct);
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }
}

/// <summary>
/// Reserva de un cliente con cuenta: el mismo punto único de la tienda (<see cref="ReservationWriter"/>, canal Web) con el
/// contacto de SU cuenta y ligada a SU cliente. Los datos para la factura no se copian a la reserva: la caja los toma del
/// cliente al cobrar (no se guarda nada derivable). Reintento optimista ×3 (existencias y numeración).
/// </summary>
public sealed class CreateMyReservationHandler(IMinvDbContext db, ICurrentUser user, IClock clock, StorefrontOptions? options = null)
    : IRequestHandler<CreateMyReservationCommand, StorefrontReservationView>
{
    public async Task<StorefrontReservationView> Handle(CreateMyReservationCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para reservar.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var account = await CustomerAccounts.CurrentAsync(db, user, ct);
                var now = clock.UtcNow;
                var build = await ReservationWriter.CreateAsync(db, clock, options, new ReservationSpec(request.Kind, PcBuildChannel.Web, request.Lines,
                    account.Customer.Name, account.Customer.Phone, account.User.Email, request.Notes, request.Name, request.HoldDays,
                    Buyer: null, CustomerId: account.Customer.Id), userId, now, ct);
                // V7 · El correo de confirmación se encola aquí, en la misma transacción (regla P-06): hasta entonces, mailQueued = false
                await db.SaveChangesAsync(ct);
                return await StorefrontReservationViews.ViewAsync(db, build, now, ct, mailQueued: false);
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }
}
