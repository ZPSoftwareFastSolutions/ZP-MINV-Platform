using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Iam;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Application.Accounts;

/// <summary>V7 · Sucursal de las cuentas de cliente: la configurada (<c>Minv:Web:BranchCode</c>) o, sin valor, la del almacén
/// principal de la empresa (la casa matriz); si no tiene, la primera activa. Es la misma regla que usa la tienda pública.</summary>
internal static class AccountBranch
{
    public static async Task<Branch> ResolveAsync(IMinvDbContext db, string? branchCode, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(branchCode))
        {
            var code = branchCode.Trim().ToUpperInvariant();
            return await db.Set<Branch>().AsNoTracking().FirstOrDefaultAsync(b => b.Code == code && b.IsActive, ct)
                   ?? throw new DomainException("account.branch", $"La sucursal {code} de la tienda no existe o está inactiva.");
        }
        return await (from c in db.Set<TenantConfig>()
                      join w in db.Set<Warehouse>() on c.DefaultWarehouseId equals w.Id
                      join b in db.Set<Branch>() on w.BranchId equals b.Id
                      where b.IsActive
                      select b).AsNoTracking().FirstOrDefaultAsync(ct)
               ?? await db.Set<Branch>().AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Code).FirstOrDefaultAsync(ct)
               ?? throw new DomainException("account.branch", "La empresa no tiene una sucursal activa para las cuentas de cliente.");
    }
}

/// <summary>
/// Registro de una cuenta de cliente (regla P-03). Todo lo que crea (usuario, credencial, rol CLIENTE, sucursal de la tienda,
/// cliente WEB-000001, cuenta, sesión y registro de acceso) se guarda en UN <c>SaveChanges</c> (una transacción), con reintento
/// optimista: si dos registros simultáneos toman el mismo código de cliente o el mismo correo, el índice único rechaza al
/// segundo y se vuelve a leer (el correo repetido sale entonces como <c>account.email_taken</c>). Los permisos y el alcance
/// de la sesión se calculan después, desde la base, igual que en el inicio de sesión (regla B-01).
/// </summary>
public sealed class RegisterCustomerAccountHandler(IMinvDbContext db, ITenantContext tenant, ICurrentUser currentUser, IPasswordHasher hasher, IClock clock)
    : IRequestHandler<RegisterCustomerAccountCommand, CustomerAccountSession>
{
    public const string EmailTakenMessage = "Ya existe una cuenta con ese correo: ingrese con su contraseña o use otro correo.";

    public async Task<CustomerAccountSession> Handle(RegisterCustomerAccountCommand request, CancellationToken ct)
    {
        var code = request.TenantCode.Trim().ToUpperInvariant();
        var company = await db.Set<Tenant>().AsNoTracking().FirstOrDefaultAsync(t => t.Code == code && t.IsActive, ct)
                      ?? throw new DomainException("account.unavailable", "La tienda no admite registros en este momento.");
        tenant.Set(company.Id);
        var email = User.NormalizeEmail(request.Email);
        var name = request.Name.Trim();
        var phone = PcBuild.NormalizePhone(request.Phone)
                    ?? throw new DomainException("pcbuild.contact_phone", "Indique un teléfono o WhatsApp.");
        // El hash (PBKDF2) se calcula una sola vez, fuera del reintento; la contraseña no se guarda ni se registra
        var hash = hasher.Hash(request.Password);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await CreateAsync(company.Id, request, email, name, phone, hash, ct);
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }

    private async Task<CustomerAccountSession> CreateAsync(Guid tenantId, RegisterCustomerAccountCommand request, string email, string name, string phone,
        string hash, CancellationToken ct)
    {
        var now = clock.UtcNow;
        Guard.That(!await db.Set<User>().AnyAsync(u => u.Email == email, ct), AccountRules.EmailTaken, EmailTakenMessage);
        var role = await db.Set<Role>().AsNoTracking().FirstOrDefaultAsync(r => r.Code == RoleCodes.Customer, ct)
                   ?? throw new DomainException("account.role_missing", "La empresa todavía no tiene el rol de cliente web: actualice la base de datos.");
        var branch = await AccountBranch.ResolveAsync(db, request.BranchCode, ct);
        var categoryId = await CustomerCategoryAsync(ct);
        var customerCode = await Documents.NextNumberAsync(db.Set<Customer>(), c => c.Code, AccountRules.CustomerCodePrefix, ct);

        var user = new User(tenantId, email, name);
        var customer = new Customer(tenantId, customerCode, name, null, email, phone, categoryId);
        var session = new Session(tenantId, user.Id, null, now, request.MachineName, request.ClientVersion);
        session.SelectBranch(branch.Id);
        db.Set<User>().Add(user);
        db.Set<UserCredential>().Add(new UserCredential(tenantId, user.Id, hash, hasher.Algorithm, hasher.Iterations, now, mustChangePassword: false));
        // SIEMPRE el rol CLIENTE y la sucursal de la tienda: el registro no recibe rol, sucursal ni permisos (regla P-03)
        db.Set<UserRole>().Add(new UserRole(tenantId, user.Id, role.Id));
        db.Set<BranchUser>().Add(new BranchUser(tenantId, branch.Id, user.Id));
        db.Set<Customer>().Add(customer);
        db.Set<CustomerAccount>().Add(new CustomerAccount(tenantId, user.Id, customer.Id));
        db.Set<Session>().Add(session);
        db.Set<AccessLog>().Add(new AccessLog(tenantId, user.Id, email, true, null, now, request.MachineName, null));
        await db.SaveChangesAsync(ct);

        var (roles, permissions) = await UserAccess.PermissionsAsync(db, user.Id, ct);
        var access = await UserAccess.AccessAsync(db, user.Id, permissions, branch.Id, ct);
        currentUser.SignIn(user.Id, user.Email, user.DisplayName, permissions);
        tenant.SetBranches(access.ToScope());
        return new CustomerAccountSession(
            new LoginResult(tenantId, user.Id, session.Id, user.DisplayName, roles, permissions, MustChangePassword: false, access), customer.Code);
    }

    /// <summary>Categoría del cliente nuevo: la del consumidor final (la categoría general de la empresa) o, si no existe, la
    /// primera por código.</summary>
    private async Task<Guid> CustomerCategoryAsync(CancellationToken ct) =>
        await db.Set<Customer>().Where(c => c.Code == "CF").Select(c => (Guid?)c.CustomerCategoryId).FirstOrDefaultAsync(ct)
        ?? await db.Set<CustomerCategory>().OrderBy(c => c.Code).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct)
        ?? throw new DomainException("account.category", "La empresa no tiene categorías de cliente.");
}
