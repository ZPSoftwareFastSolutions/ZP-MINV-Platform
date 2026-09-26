using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Billing;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Sales;

namespace MINV.Application.Billing;

// =====================================================================================================================
// V4.1 · Consultas y comandos NUEVOS que necesita la interfaz de escritorio de la facturación (agente G). No cambian los
// contratos existentes: amplían lo que muestran el menú, el historial de ventas y el editor de clientes.
// =====================================================================================================================

// --------------------------------------------------------------------------------------------------- acceso del menú
/// <summary>¿La empresa tiene licenciada la facturación SIAT y cómo está? (el escritorio arma el menú «Facturación» y el
/// trabajo automático con esto). Datos mínimos: no revela configuración ni secretos.</summary>
public sealed record BillingAccessView(bool ModuleActive, bool Configured, bool Enabled, int Environment);

/// <summary>Estado de la facturación para el menú del escritorio. Solo exige una sesión iniciada (cualquier rol: la caja
/// también lo necesita para saber si factura).</summary>
public sealed record GetBillingAccessQuery : IRequest<BillingAccessView>;

public sealed class GetBillingAccessHandler(IMinvDbContext db, ICurrentUser user, ILicenseService licenses)
    : IRequestHandler<GetBillingAccessQuery, BillingAccessView>
{
    public async Task<BillingAccessView> Handle(GetBillingAccessQuery request, CancellationToken ct)
    {
        if (!user.IsAuthenticated)
        {
            throw new AccessDeniedException("Inicie sesión para continuar.");
        }
        var module = await licenses.IsModuleActiveAsync(LicenseModuleCodes.FiscalSiat, ct);
        var settings = await db.Set<SiatSettings>().AsNoTracking().FirstOrDefaultAsync(ct);
        return new BillingAccessView(module, settings is not null, module && settings?.IsEnabled == true,
            settings?.Environment ?? SiatCodes.EnvironmentTest);
    }
}

// --------------------------------------------------------------------------------------------------- historial de ventas
/// <summary>Documento fiscal VIGENTE de una venta (el último emitido para su factura de M-INV: si se re-emitió, el nuevo).</summary>
public sealed record SaleFiscalStatusRow(string InvoiceNumber, Guid DocumentId, long Number, FiscalDocumentStatus Status, bool IsReverted,
    int EmissionType, string Cuf, bool CanVoid, bool CanCreditNote, DateTime VoidDeadline);

/// <summary>Estado fiscal de las ventas del período (mismo filtro de fechas que <c>GetSalesQuery</c>): lo usa el historial
/// de ventas para mostrar la factura del SIN de cada venta y llevar «Anular» a la anulación fiscal.</summary>
[RequiresPermission(PermissionCodes.SalesView)]
public sealed record GetSalesFiscalStatusQuery(DateOnly From, DateOnly To) : IRequest<IReadOnlyList<SaleFiscalStatusRow>>;

public sealed class GetSalesFiscalStatusHandler(IMinvDbContext db, IClock clock) : IRequestHandler<GetSalesFiscalStatusQuery, IReadOnlyList<SaleFiscalStatusRow>>
{
    public async Task<IReadOnlyList<SaleFiscalStatusRow>> Handle(GetSalesFiscalStatusQuery r, CancellationToken ct)
    {
        Guard.That(r.To >= r.From, "range.invalid", "La fecha final no puede ser anterior a la inicial.");
        var start = r.From;
        var end = r.To;
        var invoices = await (from i in db.Set<Invoice>().AsNoTracking()
                              join so in db.Set<SalesOrder>().AsNoTracking() on i.SalesOrderId equals so.Id
                              where so.OrderDate >= start && so.OrderDate <= end
                              select new { i.Id, i.Number }).ToListAsync(ct);
        if (invoices.Count == 0)
        {
            return [];
        }
        var ids = invoices.Select(i => i.Id).ToList();
        var documents = await db.Set<FiscalDocument>().AsNoTracking()
            .Where(d => d.Kind == FiscalDocumentKind.Invoice && d.InvoiceId != null && ids.Contains(d.InvoiceId.Value))
            .ToListAsync(ct);
        if (documents.Count == 0)
        {
            return [];
        }
        var fiscalNow = await FiscalIssuedStatus.FiscalNowAsync(db, clock, ct);
        var numbers = invoices.ToDictionary(i => i.Id, i => i.Number);
        return documents.GroupBy(d => d.InvoiceId!.Value)
            .Select(g => g.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Number).First())
            .Select(d => new SaleFiscalStatusRow(numbers[d.InvoiceId!.Value], d.Id, d.Number, d.Status, d.IsReverted, d.EmissionType, d.Cuf,
                d.CanVoidAt(fiscalNow), d.Status == FiscalDocumentStatus.Valid && fiscalNow <= FiscalRules.CreditNoteDeadline(d.IssuedAt),
                FiscalRules.VoidDeadline(d.IssuedAt)))
            .ToList();
    }
}

// --------------------------------------------------------------------------------------------------- clientes
/// <summary>Identidad fiscal de un cliente (tipo de documento del SIN, número y complemento).</summary>
public sealed record CustomerFiscalIdentityRow(string Code, int? DocumentType, string? DocumentNumber, string? Complement);

/// <summary>Tipo de documento y complemento de cada cliente (el editor de clientes los muestra; la lista general no los
/// trae).</summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetCustomerFiscalIdentitiesQuery : IRequest<IReadOnlyList<CustomerFiscalIdentityRow>>;

public sealed class GetCustomerFiscalIdentitiesHandler(IMinvDbContext db)
    : IRequestHandler<GetCustomerFiscalIdentitiesQuery, IReadOnlyList<CustomerFiscalIdentityRow>>
{
    public async Task<IReadOnlyList<CustomerFiscalIdentityRow>> Handle(GetCustomerFiscalIdentitiesQuery request, CancellationToken ct) =>
        await db.Set<Customer>().AsNoTracking().OrderBy(c => c.Code)
            .Select(c => new CustomerFiscalIdentityRow(c.Code, c.DocumentType, c.TaxId, c.Complement)).ToListAsync(ct);
}

/// <summary>Guarda el tipo de documento, el número y el complemento de un cliente con las reglas del SIN (CI y NIT solo
/// dígitos; complemento solo con CI). Sin tipo de documento el número queda como NIT/CI libre (como antes de la V4.1).</summary>
[RequiresPermission(PermissionCodes.CustomersManage)]
public sealed record SaveCustomerFiscalIdentityCommand(string Code, int? DocumentType, string? DocumentNumber, string? Complement)
    : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Code, DocumentType, DocumentNumber, Complement };
}

public sealed class SaveCustomerFiscalIdentityValidator : AbstractValidator<SaveCustomerFiscalIdentityCommand>
{
    public SaveCustomerFiscalIdentityValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Indique el cliente.");
        RuleFor(x => x.DocumentType).InclusiveBetween(SiatCodes.DocumentCi, SiatCodes.DocumentNit)
            .When(x => x.DocumentType is not null).WithMessage("El tipo de documento va de 1 (CI) a 5 (NIT).");
        RuleFor(x => x.DocumentNumber).NotEmpty().When(x => x.DocumentType is not null)
            .WithMessage("Con tipo de documento, el número es obligatorio.");
    }
}

public sealed class SaveCustomerFiscalIdentityHandler(IMinvDbContext db) : IRequestHandler<SaveCustomerFiscalIdentityCommand, string>
{
    public async Task<string> Handle(SaveCustomerFiscalIdentityCommand r, CancellationToken ct)
    {
        var code = r.Code.Trim().ToUpperInvariant();
        var customer = await db.Set<Customer>().FirstOrDefaultAsync(c => c.Code == code, ct)
                       ?? throw new NotFoundException($"El cliente {code} no existe.");
        customer.SetFiscalIdentity(r.DocumentType, string.IsNullOrWhiteSpace(r.DocumentNumber) ? null : r.DocumentNumber.Trim(),
            string.IsNullOrWhiteSpace(r.Complement) ? null : r.Complement.Trim());
        await db.SaveChangesAsync(ct);
        return customer.HasFiscalIdentity
            ? $"✔ Datos de facturación de {customer.Name}: documento {customer.TaxId}{(customer.Complement is null ? "" : "-" + customer.Complement)}."
            : $"✔ {customer.Name} queda sin tipo de documento para facturar.";
    }
}
