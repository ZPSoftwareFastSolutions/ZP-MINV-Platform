using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Iam;
using MINV.Domain.Accounting;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Application.Corporate;

// ================================================================================================ sucursales
public sealed record BranchRow(Guid Id, string Code, string Name, bool IsActive, IReadOnlyList<string> Warehouses, int Users, bool IsVisible,
    decimal? StockValue, int TransfersOut, int TransfersIn);

/// <summary>
/// V4 · Sucursales de la empresa (directorio corporativo). El valor del stock y las transferencias abiertas solo se
/// informan de las sucursales del alcance de la sesión.
/// </summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetBranchesQuery : IRequest<IReadOnlyList<BranchRow>>;

public sealed class GetBranchesHandler(IMinvDbContext db) : IRequestHandler<GetBranchesQuery, IReadOnlyList<BranchRow>>
{
    public async Task<IReadOnlyList<BranchRow>> Handle(GetBranchesQuery request, CancellationToken ct)
    {
        var branches = await db.Set<Branch>().OrderBy(b => b.Code).ToListAsync(ct);
        var warehouses = (await db.Set<Warehouse>().OrderBy(w => w.Code).Select(w => new { w.BranchId, w.Code }).ToListAsync(ct))
            .ToLookup(w => w.BranchId, w => w.Code);
        var users = await db.Set<BranchUser>().GroupBy(x => x.BranchId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var values = (await StockValuation.ByWarehouseAsync(db, ct)).GroupBy(x => x.BranchId).ToDictionary(g => g.Key, g => g.Sum(x => x.Value));
        var open = await db.Set<StockTransfer>().Where(t => t.Status == TransferStatus.Pending || t.Status == TransferStatus.Dispatched)
            .Select(t => new { t.FromBranchId, t.ToBranchId }).ToListAsync(ct);
        var scope = db.Branches;
        return branches.Select(b => new BranchRow(b.Id, b.Code, b.Name, b.IsActive, warehouses[b.Id].ToList(), users.GetValueOrDefault(b.Id),
            scope.Allows(b.Id), scope.Allows(b.Id) ? JournalPoster.Money(values.GetValueOrDefault(b.Id)) : null,
            open.Count(t => t.FromBranchId == b.Id), open.Count(t => t.ToBranchId == b.Id))).ToList();
    }
}

/// <summary>
/// V4 · Alta de una sucursal con su almacén y la topología mínima (zona / pasillo / estantería / nivel / posición
/// GENERAL) y, si se pide, su caja. Quien la crea queda asignado a ella.
/// </summary>
[RequiresModule(LicenseModuleCodes.MultiBranch)]
[RequiresPermission(PermissionCodes.BranchesManage)]
public sealed record CreateBranchCommand(string Code, string Name, string WarehouseCode, string WarehouseName, bool CreatePosRegister = true)
    : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Code, Name, WarehouseCode, WarehouseName, CreatePosRegister };
}

public sealed class CreateBranchValidator : AbstractValidator<CreateBranchCommand>
{
    public CreateBranchValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(12).Matches("^[A-Za-z0-9]+$").WithMessage("El código de la sucursal: letras y números (máx. 12).");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Indique el nombre de la sucursal.").MaximumLength(100);
        RuleFor(x => x.WarehouseCode).NotEmpty().MaximumLength(12).Matches("^[A-Za-z0-9]+$")
            .WithMessage("El código del almacén: letras y números (máx. 12).");
        RuleFor(x => x.WarehouseName).NotEmpty().WithMessage("Indique el nombre del almacén.").MaximumLength(100);
    }
}

public sealed class CreateBranchHandler(IMinvDbContext db, ICurrentUser user, ITenantContext tenant) : IRequestHandler<CreateBranchCommand, string>
{
    public async Task<string> Handle(CreateBranchCommand r, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para crear sucursales.");
        var code = r.Code.Trim().ToUpperInvariant();
        var warehouseCode = r.WarehouseCode.Trim().ToUpperInvariant();
        Guard.That(!await db.Set<Branch>().AnyAsync(b => b.Code == code, ct), "branch.duplicate", $"Ya existe la sucursal {code}.");
        Guard.That(!await db.Set<Warehouse>().AnyAsync(w => w.Code == warehouseCode, ct), "warehouse.duplicate", $"Ya existe el almacén {warehouseCode}.");
        var picking = await db.Set<LocationType>().FirstOrDefaultAsync(t => t.Code == "PICKING", ct)
                      ?? throw new NotFoundException("La empresa no tiene el tipo de ubicación PICKING.");
        var tenantId = tenant.TenantId;
        var branch = new Branch(tenantId, code, r.Name.Trim(), null);
        // La sucursal nueva entra al alcance de quien la crea (para poder registrar su topología ahora mismo)
        tenant.SetBranches(tenant.Branches.AllBranches ? tenant.Branches
            : tenant.Branches with { BranchIds = [.. tenant.Branches.BranchIds, branch.Id] });
        var warehouse = new Warehouse(tenantId, branch.Id, warehouseCode, r.WarehouseName.Trim());
        var zone = new Zone(tenantId, branch.Id, warehouse.Id, "GEN", "General", picking.Id);
        var aisle = new Aisle(tenantId, branch.Id, zone.Id, "00");
        var rack = new Rack(tenantId, branch.Id, aisle.Id, "00");
        var shelf = new Shelf(tenantId, branch.Id, rack.Id, "00");
        var bin = new Bin(tenantId, branch.Id, shelf.Id, $"{warehouseCode}-GENERAL", picking.Id, 0);
        db.Set<Branch>().Add(branch);
        db.Set<Warehouse>().Add(warehouse);
        db.Set<Zone>().Add(zone);
        db.Set<Aisle>().Add(aisle);
        db.Set<Rack>().Add(rack);
        db.Set<Shelf>().Add(shelf);
        db.Set<Bin>().Add(bin);
        if (r.CreatePosRegister)
        {
            db.Set<PosRegister>().Add(new PosRegister(tenantId, branch.Id, warehouse.Id, $"{code}-CAJA1", $"Caja 1 · {branch.Name}", null));
        }
        db.Set<BranchUser>().Add(new BranchUser(tenantId, branch.Id, userId));
        await db.SaveChangesAsync(ct);
        return $"✔ Sucursal {code} · {branch.Name} creada con el almacén {warehouseCode}" + (r.CreatePosRegister ? $" y la caja {code}-CAJA1." : ".");
    }
}

/// <summary>V4 · Cambia el nombre o activa/desactiva una sucursal (una inactiva no recibe transferencias).</summary>
[RequiresModule(LicenseModuleCodes.MultiBranch)]
[RequiresPermission(PermissionCodes.BranchesManage)]
public sealed record UpdateBranchCommand(string Code, string Name, bool IsActive) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Code, Name, IsActive };
}

public sealed class UpdateBranchHandler(IMinvDbContext db) : IRequestHandler<UpdateBranchCommand, string>
{
    public async Task<string> Handle(UpdateBranchCommand r, CancellationToken ct)
    {
        var code = r.Code.Trim().ToUpperInvariant();
        var branch = await db.Set<Branch>().FirstOrDefaultAsync(b => b.Code == code, ct) ?? throw new NotFoundException($"La sucursal {code} no existe.");
        branch.Rename(r.Name.Trim());
        if (r.IsActive)
        {
            branch.Activate();
        }
        else
        {
            Guard.That(await db.Set<Branch>().CountAsync(b => b.IsActive, ct) > 1, "branch.last", "No puede desactivar la única sucursal activa.");
            branch.Deactivate();
        }
        await db.SaveChangesAsync(ct);
        return $"✔ Sucursal {code} actualizada.";
    }
}

/// <summary>V4 · Asigna las sucursales en las que trabaja un usuario (reemplaza las anteriores).</summary>
[RequiresModule(LicenseModuleCodes.MultiBranch)]
[RequiresPermission(PermissionCodes.BranchesManage)]
public sealed record AssignUserBranchesCommand(string Email, IReadOnlyList<string> BranchCodes) : IRequest<string>, IAuditableRequest
{
    public object AuditDetails => new { Email, BranchCodes };
}

public sealed class AssignUserBranchesHandler(IMinvDbContext db) : IRequestHandler<AssignUserBranchesCommand, string>
{
    public async Task<string> Handle(AssignUserBranchesCommand r, CancellationToken ct)
    {
        var email = User.NormalizeEmail(r.Email);
        var user = await db.Set<User>().FirstOrDefaultAsync(u => u.Email == email, ct) ?? throw new NotFoundException($"El usuario {email} no existe.");
        await UserBranches.AssignAsync(db, user, r.BranchCodes, ct);
        await db.SaveChangesAsync(ct);
        return $"✔ {user.DisplayName} trabaja en: {string.Join(", ", r.BranchCodes.Select(c => c.Trim().ToUpperInvariant()))}.";
    }
}

// ================================================================================================ stock consolidado
public sealed record ConsolidatedStockRow(string Sku, string Name, string Category, string Unit, IReadOnlyList<decimal> ByBranch, decimal InTransit,
    decimal Total, decimal Value);

public sealed record ConsolidatedStock(IReadOnlyList<BranchInfo> Branches, IReadOnlyList<ConsolidatedStockRow> Rows, IReadOnlyList<decimal> ValueByBranch,
    decimal InTransitValue, decimal TotalValue);

/// <summary>
/// V4 · Stock consolidado por sucursal (gerencia global: todas; el resto: las suyas) más lo que está EN TRÁNSITO entre
/// sucursales (transferencias despachadas y aún no recibidas, contadas UNA sola vez: ya salieron del origen y todavía
/// no entraron al destino). Total = Σ sucursales + en tránsito. Valorizado al costo promedio de cada almacén.
/// </summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record ConsolidatedStockQuery(string? Search = null) : IRequest<ConsolidatedStock>;

public sealed class ConsolidatedStockHandler(IMinvDbContext db) : IRequestHandler<ConsolidatedStockQuery, ConsolidatedStock>
{
    public async Task<ConsolidatedStock> Handle(ConsolidatedStockQuery request, CancellationToken ct)
    {
        var scope = db.Branches;
        var branches = (await db.Set<Branch>().Where(b => b.IsActive).OrderBy(b => b.Code).Select(b => new BranchInfo(b.Id, b.Code, b.Name)).ToListAsync(ct))
            .Where(b => scope.Allows(b.Id)).ToList();
        var index = branches.Select((b, i) => (b.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var stock = await StockValuation.ByWarehouseAsync(db, ct);
        var transit = await (from l in db.Set<StockTransferLine>()
                             join t in db.Set<StockTransfer>() on l.StockTransferId equals t.Id
                             where t.Status == TransferStatus.Dispatched
                             select new { l.VariantId, l.Quantity, Cost = l.UnitCost ?? 0 }).ToListAsync(ct);
        var variantIds = stock.Select(s => s.VariantId).Concat(transit.Select(t => t.VariantId)).Distinct().ToList();
        var items = await (from v in db.Set<ProductVariant>()
                           join p in db.Set<Product>() on v.ProductId equals p.Id
                           join c in db.Set<Category>() on p.CategoryId equals c.Id
                           join u in db.Set<UnitOfMeasure>() on p.BaseUnitId equals u.Id
                           where variantIds.Contains(v.Id)
                           select new { v.Id, v.Sku, p.Name, Category = c.Name, Unit = u.Code }).ToListAsync(ct);
        var search = request.Search?.Trim();
        var rows = new List<ConsolidatedStockRow>();
        var valueByBranch = new decimal[branches.Count];
        foreach (var item in items.OrderBy(i => i.Sku))
        {
            if (!string.IsNullOrEmpty(search) && !item.Sku.Contains(search, StringComparison.OrdinalIgnoreCase)
                                              && !item.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var byBranch = new decimal[branches.Count];
            var value = 0m;
            foreach (var s in stock.Where(s => s.VariantId == item.Id && index.ContainsKey(s.BranchId)))
            {
                byBranch[index[s.BranchId]] += s.Quantity;
                valueByBranch[index[s.BranchId]] += s.Value;
                value += s.Value;
            }
            var inTransit = transit.Where(t => t.VariantId == item.Id).Sum(t => t.Quantity);
            value += transit.Where(t => t.VariantId == item.Id).Sum(t => t.Quantity * t.Cost);
            var total = Quantities.Round6(byBranch.Sum() + inTransit);
            if (total == 0)
            {
                continue;
            }
            rows.Add(new ConsolidatedStockRow(item.Sku, item.Name, item.Category, item.Unit, byBranch.Select(Quantities.Round6).ToList(),
                Quantities.Round6(inTransit), total, JournalPoster.Money(value)));
        }
        var transitValue = JournalPoster.Money(transit.Sum(t => t.Quantity * t.Cost));
        return new ConsolidatedStock(branches, rows, valueByBranch.Select(JournalPoster.Money).ToList(), transitValue,
            JournalPoster.Money(valueByBranch.Sum() + transitValue));
    }
}

/// <summary>Existencia y valor (costo promedio vigente) por variante, sucursal y almacén (solo lo visible).</summary>
internal static class StockValuation
{
    public sealed record Row(Guid VariantId, Guid BranchId, Guid WarehouseId, decimal Quantity, decimal Value);

    public static async Task<IReadOnlyList<Row>> ByWarehouseAsync(IMinvDbContext db, CancellationToken ct)
    {
        var stock = await (from l in db.Set<StockLevel>()
                           join b in db.Set<Batch>() on l.BatchId equals b.Id
                           join bin in db.Set<Bin>() on l.BinId equals bin.Id
                           join s in db.Set<Shelf>() on bin.ShelfId equals s.Id
                           join r in db.Set<Rack>() on s.RackId equals r.Id
                           join a in db.Set<Aisle>() on r.AisleId equals a.Id
                           join z in db.Set<Zone>() on a.ZoneId equals z.Id
                           where l.QuantityOnHand != 0
                           group l.QuantityOnHand by new { b.VariantId, l.BranchId, z.WarehouseId } into g
                           select new { g.Key.VariantId, g.Key.BranchId, g.Key.WarehouseId, Quantity = g.Sum() }).ToListAsync(ct);
        var costs = await (from h in db.Set<AverageCostHistory>()
                           where h.Sequence == db.Set<AverageCostHistory>().Where(x => x.VariantId == h.VariantId && x.WarehouseId == h.WarehouseId)
                               .Max(x => x.Sequence)
                           select new { h.VariantId, h.WarehouseId, h.AverageCost }).ToListAsync(ct);
        var cost = costs.ToDictionary(c => (c.VariantId, c.WarehouseId), c => c.AverageCost);
        return stock.Select(s => new Row(s.VariantId, s.BranchId, s.WarehouseId, s.Quantity,
            s.Quantity * cost.GetValueOrDefault((s.VariantId, s.WarehouseId)))).ToList();
    }
}
