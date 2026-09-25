using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Catalog;
using MINV.Application.Common;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Purchasing;
using MINV.Domain.Sales;

namespace MINV.Application.Partners;

// ------------------------------------------------------------------------------------------------ proveedores
public sealed record SupplierRow(string Code, string Name, string? TaxId, int LeadTimeDays, string? Contact, string? Phone, string? Email,
    bool IsActive, int Products, int OpenOrders, decimal Purchased);

[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetSuppliersQuery : IRequest<IReadOnlyList<SupplierRow>>;

public sealed class GetSuppliersHandler(IMinvDbContext db) : IRequestHandler<GetSuppliersQuery, IReadOnlyList<SupplierRow>>
{
    public async Task<IReadOnlyList<SupplierRow>> Handle(GetSuppliersQuery request, CancellationToken ct)
    {
        var suppliers = await db.Set<Supplier>().OrderBy(s => s.LegalName).ToListAsync(ct);
        var contacts = (await db.Set<SupplierContact>().Where(c => c.IsPrimary).ToListAsync(ct)).GroupBy(c => c.SupplierId)
            .ToDictionary(g => g.Key, g => g.First());
        var products = await db.Set<ProductSupplier>().GroupBy(p => p.SupplierId).Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var orders = await db.Set<PurchaseOrder>().Include(o => o.Lines).ToListAsync(ct);
        return suppliers.Select(s =>
        {
            contacts.TryGetValue(s.Id, out var c);
            var own = orders.Where(o => o.SupplierId == s.Id).ToList();
            return new SupplierRow(s.Code, s.LegalName, s.TaxId, s.LeadTimeDays, c?.FullName, c?.Phone, c?.Email, s.IsActive,
                products.GetValueOrDefault(s.Id),
                own.Count(o => o.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived),
                own.Where(o => o.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.PartiallyReceived).Sum(o => o.Total));
        }).ToList();
    }
}

/// <summary>Crear (código automático P001…) o modificar un proveedor y su contacto principal.</summary>
[RequiresPermission(PermissionCodes.PurchasingManage)]
public sealed record SaveSupplierCommand(string? Code, string Name, string? TaxId, int LeadTimeDays, string? ContactName, string? Phone,
    string? Email, bool IsActive) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Code, Name, TaxId, LeadTimeDays, ContactName, Phone, Email, IsActive };
}

public sealed class SaveSupplierValidator : AbstractValidator<SaveSupplierCommand>
{
    public SaveSupplierValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Indique la razón social.").MaximumLength(150);
        RuleFor(x => x.LeadTimeDays).InclusiveBetween(0, 365).WithMessage("Los días de entrega van de 0 a 365.");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithMessage("El correo no es válido.");
    }
}

public sealed class SaveSupplierHandler(IMinvDbContext db, ITenantContext tenant) : IRequestHandler<SaveSupplierCommand, string>
{
    public async Task<string> Handle(SaveSupplierCommand r, CancellationToken ct)
    {
        Supplier supplier;
        if (string.IsNullOrWhiteSpace(r.Code))
        {
            var codes = await db.Set<Supplier>().Select(s => s.Code).ToListAsync(ct);
            var next = codes.Select(c => c.Length > 1 && c[0] == 'P' && int.TryParse(c[1..], out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
            supplier = new Supplier(tenant.TenantId, $"P{next:000}", r.Name.Trim(), r.TaxId, r.LeadTimeDays);
            db.Set<Supplier>().Add(supplier);
        }
        else
        {
            var code = r.Code.Trim().ToUpperInvariant();
            supplier = await db.Set<Supplier>().FirstOrDefaultAsync(s => s.Code == code, ct) ?? throw new NotFoundException($"El proveedor {code} no existe.");
            supplier.Update(r.Name.Trim(), r.TaxId, r.LeadTimeDays);
        }
        if (r.IsActive)
        {
            supplier.Activate();
        }
        else
        {
            supplier.Deactivate();
        }
        if (!string.IsNullOrWhiteSpace(r.ContactName) || !string.IsNullOrWhiteSpace(r.Phone) || !string.IsNullOrWhiteSpace(r.Email))
        {
            var contact = await db.Set<SupplierContact>().FirstOrDefaultAsync(c => c.SupplierId == supplier.Id && c.IsPrimary, ct);
            var name = string.IsNullOrWhiteSpace(r.ContactName) ? supplier.LegalName : r.ContactName.Trim();
            if (contact is null)
            {
                db.Set<SupplierContact>().Add(new SupplierContact(supplier.TenantId, supplier.Id, name, r.Phone, r.Email, isPrimary: true));
            }
            else
            {
                contact.Update(name, r.Phone, r.Email);
            }
        }
        await db.SaveChangesAsync(ct);
        return supplier.Code;
    }
}

// ------------------------------------------------------------------------------------------------ clientes
public sealed record CustomerRow(string Code, string Name, string? TaxId, string? Email, string? Phone, string CategoryCode, string Category,
    bool IsActive, int Purchases, decimal Total, DateOnly? LastPurchase);

public sealed record CustomersView(IReadOnlyList<CustomerRow> Customers, IReadOnlyList<OptionItem> Categories);

[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetCustomersQuery : IRequest<CustomersView>;

public sealed class GetCustomersHandler(IMinvDbContext db) : IRequestHandler<GetCustomersQuery, CustomersView>
{
    public async Task<CustomersView> Handle(GetCustomersQuery request, CancellationToken ct)
    {
        var categories = await db.Set<CustomerCategory>().OrderBy(c => c.Name).ToListAsync(ct);
        var customers = await db.Set<Customer>().OrderBy(c => c.Name).ToListAsync(ct);
        var sales = (await (from so in db.Set<SalesOrder>()
                            join i in db.Set<Invoice>() on so.Id equals i.SalesOrderId
                            where i.Status == InvoiceStatus.Issued
                            select new { so.CustomerId, so.OrderDate, Amount = so.Lines.Sum(l => l.Quantity * l.UnitPrice * (1 - l.DiscountPercent / 100m)) })
                .ToListAsync(ct))
            .GroupBy(x => x.CustomerId).ToDictionary(g => g.Key, g => (N: g.Count(), Total: g.Sum(x => x.Amount), Last: g.Max(x => x.OrderDate)));
        var byId = categories.ToDictionary(c => c.Id);
        return new CustomersView(customers.Select(c =>
        {
            var s = sales.GetValueOrDefault(c.Id);
            var cat = byId.GetValueOrDefault(c.CustomerCategoryId);
            return new CustomerRow(c.Code, c.Name, c.TaxId, c.Email, c.Phone, cat?.Code ?? "", cat?.Name ?? "", c.IsActive, s.N,
                JournalPoster.Money(s.Total), s.N > 0 ? s.Last : null);
        }).ToList(), categories.Select(c => new OptionItem(c.Code, c.Name)).ToList());
    }
}

/// <summary>Crear (código automático C0001…) o modificar un cliente.</summary>
[RequiresPermission(PermissionCodes.CustomersManage)]
public sealed record SaveCustomerCommand(string? Code, string Name, string? TaxId, string? Email, string? Phone, string CategoryCode, bool IsActive)
    : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Code, Name, TaxId, Email, Phone, CategoryCode, IsActive };
}

public sealed class SaveCustomerValidator : AbstractValidator<SaveCustomerCommand>
{
    public SaveCustomerValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Indique el nombre del cliente.").MaximumLength(150);
        RuleFor(x => x.CategoryCode).NotEmpty().WithMessage("Elija la categoría del cliente.");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithMessage("El correo no es válido.");
    }
}

public sealed class SaveCustomerHandler(IMinvDbContext db, ITenantContext tenant) : IRequestHandler<SaveCustomerCommand, string>
{
    public async Task<string> Handle(SaveCustomerCommand r, CancellationToken ct)
    {
        var category = await db.Set<CustomerCategory>().FirstOrDefaultAsync(c => c.Code == r.CategoryCode.Trim().ToUpperInvariant(), ct)
                       ?? throw new NotFoundException($"La categoría de cliente {r.CategoryCode} no existe.");
        Customer customer;
        if (string.IsNullOrWhiteSpace(r.Code))
        {
            var codes = await db.Set<Customer>().Select(c => c.Code).ToListAsync(ct);
            var next = codes.Select(c => c.Length > 1 && c[0] == 'C' && int.TryParse(c[1..], out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
            customer = new Customer(tenant.TenantId, $"C{next:0000}", r.Name.Trim(), r.TaxId, r.Email, r.Phone, category.Id);
            db.Set<Customer>().Add(customer);
        }
        else
        {
            var code = r.Code.Trim().ToUpperInvariant();
            customer = await db.Set<Customer>().FirstOrDefaultAsync(c => c.Code == code, ct) ?? throw new NotFoundException($"El cliente {code} no existe.");
            customer.Update(r.Name.Trim(), r.TaxId, r.Email, r.Phone, category.Id);
        }
        Guard.That(r.IsActive || customer.Code != "CF", "customer.cf", "El consumidor final no se puede desactivar.");
        if (r.IsActive)
        {
            customer.Activate();
        }
        else
        {
            customer.Deactivate();
        }
        await db.SaveChangesAsync(ct);
        return customer.Code;
    }
}
