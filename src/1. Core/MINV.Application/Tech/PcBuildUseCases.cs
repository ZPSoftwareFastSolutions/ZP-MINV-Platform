using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Application.Common;
using MINV.Application.Inventory;
using MINV.Application.Sales;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Application.Tech;

// =====================================================================================================================
// V4.2 · Armador de PC (regla T-06): la compatibilidad la decide SOLO PcCompatibility.Check con las especificaciones que
// tienen clave de compatibilidad; los casos de uso arman las piezas (PcComponent), cotizan con precios congelados y
// venden la cotización con los casos de uso normales de venta.
// =====================================================================================================================

/// <summary>V4.2 · Pieza del armado resuelta: producto, componente para las reglas, precio de lista, stock y ficha.</summary>
internal sealed record PcPart(PcSlot Slot, VariantInfo Item, PcComponent Component, decimal ListPrice, decimal Stock, Guid? ImageId,
    IReadOnlyList<string> KeySpecs, string? Brand);

/// <summary>V4.2 · Arma las piezas del armador desde el catálogo técnico.</summary>
internal sealed class PcParts(IMinvDbContext db)
{
    /// <summary>Especificación que identifica los productos de cada ranura (las demás ranuras se eligen por categoría).</summary>
    public static readonly IReadOnlyDictionary<PcSlot, string> SlotKeys = new Dictionary<PcSlot, string>
    {
        [PcSlot.Cpu] = CompatibilityKeys.CpuTdpW,
        [PcSlot.Motherboard] = CompatibilityKeys.RamSlots,
        [PcSlot.Ram] = CompatibilityKeys.RamCapacityGb,
        [PcSlot.Gpu] = CompatibilityKeys.GpuLengthMm,
        [PcSlot.Storage] = CompatibilityKeys.StorageInterface,
        [PcSlot.Psu] = CompatibilityKeys.PsuWatts,
        [PcSlot.Case] = CompatibilityKeys.CaseFormFactors,
        [PcSlot.Cooler] = CompatibilityKeys.CoolerSockets,
    };

    public async Task<IReadOnlyList<PcPart>> ResolveAsync(IReadOnlyList<PcBuildItemInput> inputs, CancellationToken ct)
    {
        var skus = inputs.Select(i => i.Sku.Trim().ToUpperInvariant()).Distinct().ToList();
        var rows = await (from v in db.Set<ProductVariant>()
                          join p in db.Set<Product>() on v.ProductId equals p.Id
                          join u in db.Set<UnitOfMeasure>() on p.BaseUnitId equals u.Id
                          where skus.Contains(v.Sku)
                          select new { v, p, u.Code, u.AllowsDecimals }).ToListAsync(ct);
        var missing = skus.Where(s => rows.All(r => r.v.Sku != s)).ToList();
        if (missing.Count > 0)
        {
            throw new NotFoundException($"El producto {string.Join(", ", missing)} no existe en el catálogo.");
        }
        var items = rows.Select(r => new VariantInfo(r.v, r.p, new UnitRule(r.Code, r.AllowsDecimals))).ToDictionary(i => i.Variant.Sku, StringComparer.Ordinal);
        return await PartsAsync(inputs.Select(i => (i.Slot, items[i.Sku.Trim().ToUpperInvariant()], i.Quantity)).ToList(), ct);
    }

    public async Task<IReadOnlyList<PcPart>> PartsAsync(IReadOnlyList<(PcSlot Slot, VariantInfo Item, int Quantity)> parts, CancellationToken ct)
    {
        var reader = new TechCatalogReader(db);
        var productIds = parts.Select(p => p.Item.Product.Id).Distinct().ToList();
        var variantIds = parts.Select(p => p.Item.Variant.Id).Distinct().ToList();
        var values = (await reader.ValuesAsync(productIds, null, ct)).ToLookup(v => v.ProductId);
        var prices = await reader.PricesAsync(variantIds, ct);
        var stock = await reader.StockAsync(variantIds, ct);
        var images = await reader.ImagesAsync(variantIds, ct);
        var brands = await reader.BrandsAsync(productIds, ct);
        return parts.Select(p =>
        {
            var mine = values[p.Item.Product.Id].ToList();
            var specs = mine.Where(v => v.Definition.CompatibilityKey is not null).GroupBy(v => v.Definition.CompatibilityKey!)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.OrderBy(v => v.OptionOrder).Select(v => v.Raw).ToList());
            var name = p.Item.Variant.Name is { Length: > 0 } n ? $"{p.Item.Product.Name} · {n}" : p.Item.Product.Name;
            return new PcPart(p.Slot, p.Item, new PcComponent(p.Slot, p.Item.Variant.Sku, name, p.Quantity, specs),
                prices.GetValueOrDefault(p.Item.Variant.Id), stock.GetValueOrDefault(p.Item.Variant.Id),
                images.TryGetValue(p.Item.Variant.Id, out var image) ? image : null, TechCatalogReader.KeySpecs(mine), brands.GetValueOrDefault(p.Item.Product.Id));
        }).ToList();
    }

    public static PcBuildCheckView Check(IReadOnlyList<PcPart> parts, IReadOnlyList<decimal>? prices = null)
    {
        var report = PcCompatibility.Check(parts.Select(p => p.Component).ToList());
        var items = parts.Select((p, i) => View(p, prices?[i] ?? p.ListPrice)).ToList();
        return new PcBuildCheckView(items, report.Issues, report.IsCompatible, report.EstimatedDrawW, report.RecommendedPsuW, report.PsuW,
            items.Sum(i => i.Subtotal));
    }

    public static PcBuildItemView View(PcPart part, decimal unitPrice) =>
        new(part.Slot, part.Item.Variant.Sku, part.Component.Name, part.Component.Quantity, unitPrice,
            decimal.Round(part.Component.Quantity * unitPrice, 2, MidpointRounding.AwayFromZero), part.Stock, part.ImageId, part.KeySpecs);

    /// <summary>Partes de un armado guardado (con su precio cotizado en el mismo orden).</summary>
    public async Task<(IReadOnlyList<PcPart> Parts, IReadOnlyList<decimal> Quoted)> OfBuildAsync(PcBuild build, CancellationToken ct)
    {
        var lines = build.Lines.OrderBy(l => l.Slot).ThenBy(l => l.Id).ToList();
        var variantIds = lines.Select(l => l.VariantId).Distinct().ToList();
        var rows = await (from v in db.Set<ProductVariant>()
                          join p in db.Set<Product>() on v.ProductId equals p.Id
                          join u in db.Set<UnitOfMeasure>() on p.BaseUnitId equals u.Id
                          where variantIds.Contains(v.Id)
                          select new { v, p, u.Code, u.AllowsDecimals }).ToDictionaryAsync(x => x.v.Id, ct);
        var parts = await PartsAsync(lines.Select(l =>
            (l.Slot, new VariantInfo(rows[l.VariantId].v, rows[l.VariantId].p, new UnitRule(rows[l.VariantId].Code, rows[l.VariantId].AllowsDecimals)), l.Quantity))
            .ToList(), ct);
        return (parts, lines.Select(l => l.QuotedUnitPrice).ToList());
    }
}

/// <summary>V4.2 · Filas de los armados (compatibilidad evaluada con las fichas vigentes).</summary>
internal sealed class PcBuildViews(IMinvDbContext db, IClock clock)
{
    public async Task<IReadOnlyList<PcBuildRow>> RowsAsync(IReadOnlyList<PcBuild> builds, CancellationToken ct)
    {
        var config = await new InventoryLookups(db).ConfigAsync(ct);
        var today = clock.TodayIn(config.TimeZoneId);
        var branches = await db.Set<Branch>().ToDictionaryAsync(b => b.Id, b => b.Code, ct);
        var customerIds = builds.Where(b => b.CustomerId is not null).Select(b => b.CustomerId!.Value).Distinct().ToList();
        var customers = await db.Set<Customer>().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var invoiceIds = builds.Where(b => b.InvoiceId is not null).Select(b => b.InvoiceId!.Value).ToList();
        var invoices = await db.Set<Invoice>().Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Number, ct);
        var parts = new PcParts(db);
        var result = new List<PcBuildRow>(builds.Count);
        foreach (var b in builds)
        {
            var (resolved, _) = await parts.OfBuildAsync(b, ct);
            var compatible = PcCompatibility.Check(resolved.Select(p => p.Component).ToList()).IsCompatible;
            result.Add(new PcBuildRow(b.Id, b.Number, b.Name, branches.GetValueOrDefault(b.BranchId, "?"),
                b.CustomerId is { } c ? customers.GetValueOrDefault(c) : null, b.Status, b.ValidUntil, b.IsExpiredOn(today), b.Total, b.Lines.Count,
                compatible, b.CreatedAt, b.InvoiceId is { } i ? invoices.GetValueOrDefault(i) : null, b.QuotedWithErrors));
        }
        return result;
    }

    public async Task<PcBuildRow> RowAsync(PcBuild build, CancellationToken ct) => (await RowsAsync([build], ct))[0];
}

public sealed class CheckPcBuildHandler(IMinvDbContext db) : IRequestHandler<CheckPcBuildQuery, PcBuildCheckView>
{
    public async Task<PcBuildCheckView> Handle(CheckPcBuildQuery request, CancellationToken ct) =>
        PcParts.Check(await new PcParts(db).ResolveAsync(request.Items, ct));
}

/// <summary>Candidatos de una ranura: compatibles con lo elegido si no agregan errores NUEVOS de compatibilidad.</summary>
public sealed class GetPcBuildCandidatesHandler(IMinvDbContext db) : IRequestHandler<GetPcBuildCandidatesQuery, IReadOnlyList<PcBuildCandidate>>
{
    public async Task<IReadOnlyList<PcBuildCandidate>> Handle(GetPcBuildCandidatesQuery request, CancellationToken ct)
    {
        var reader = new TechCatalogReader(db);
        var products = from v in db.Set<ProductVariant>()
                       join p in db.Set<Product>() on v.ProductId equals p.Id
                       join u in db.Set<UnitOfMeasure>() on p.BaseUnitId equals u.Id
                       where p.IsActive && v.IsActive
                       select new { v, p, u.Code, u.AllowsDecimals };
        var byKey = PcParts.SlotKeys.TryGetValue(request.Slot, out var key);
        Guard.That(byKey || !string.IsNullOrWhiteSpace(request.CategoryCode), "pcbuild.category",
            $"Para {PcCompatibility.SlotName(request.Slot)} indique la categoría de los productos.");
        if (byKey)
        {
            var withKey = from x in db.Set<ProductSpecValue>()
                          join d in db.Set<SpecDefinition>() on x.SpecDefinitionId equals d.Id
                          where d.CompatibilityKey == key
                          select x.ProductId;
            products = products.Where(x => withKey.Contains(x.p.Id));
        }
        if (!string.IsNullOrWhiteSpace(request.CategoryCode))
        {
            var subtree = await reader.SubtreeAsync((await reader.CategoryAsync(request.CategoryCode, ct)).Id, ct);
            products = products.Where(x => subtree.Contains(x.p.CategoryId));
        }
        if (!string.IsNullOrWhiteSpace(request.Text))
        {
            var text = request.Text.Trim().ToUpperInvariant();
            products = products.Where(x => x.v.Sku.ToUpper().Contains(text) || x.p.Name.ToUpper().Contains(text));
        }
        var candidates = (await products.ToListAsync(ct)).Select(x => new VariantInfo(x.v, x.p, new UnitRule(x.Code, x.AllowsDecimals))).ToList();
        var parts = new PcParts(db);
        var current = await parts.ResolveAsync(request.Current.Where(i => PcBuild.MultiSlots.Contains(request.Slot) || i.Slot != request.Slot).ToList(), ct);
        var baseline = PcCompatibility.Check(current.Select(p => p.Component).ToList()).Issues.Where(i => i.IsError)
            .Select(i => (i.Code, i.Message)).ToHashSet();
        var options = await parts.PartsAsync(candidates.Select(c => (request.Slot, c, 1)).ToList(), ct);
        return options.Select(option =>
            {
                var report = PcCompatibility.Check(current.Select(p => p.Component).Append(option.Component).ToList());
                var added = report.Issues.Where(i => i.IsError && !baseline.Contains((i.Code, i.Message))).ToList();
                return new PcBuildCandidate(option.Item.Variant.Sku, option.Component.Name, option.Brand, option.ListPrice, option.Stock, added.Count == 0,
                    added.FirstOrDefault()?.Message, option.ImageId, option.KeySpecs);
            })
            .Where(c => !request.OnlyInStock || c.Stock > 0)
            .OrderByDescending(c => c.IsCompatible).ThenBy(c => c.Price).ThenBy(c => c.Name, StringComparer.CurrentCulture).ToList();
    }
}

public sealed class SavePcBuildValidator : AbstractValidator<SavePcBuildCommand>
{
    public SavePcBuildValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Indique el nombre del armado.").MaximumLength(150);
        RuleFor(x => x.Items).NotEmpty().WithMessage("Agregue al menos una pieza.");
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.Sku).NotEmpty().WithMessage("Cada pieza necesita su producto.");
            i.RuleFor(x => x.Quantity).InclusiveBetween(1, PcBuild.MaxQuantity).WithMessage($"La cantidad de cada pieza va de 1 a {PcBuild.MaxQuantity}.");
        });
        RuleFor(x => x.ValidDays).InclusiveBetween(1, 90).WithMessage("La vigencia de la cotización va de 1 a 90 días.");
    }
}

/// <summary>Guarda (y cotiza) el armado con los precios de la lista vigente; numeración ARM-&lt;sucursal&gt;-000001 con
/// reintento.</summary>
public sealed class SavePcBuildHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<SavePcBuildCommand, PcBuildRow>
{
    public async Task<PcBuildRow> Handle(SavePcBuildCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var build = await SaveAsync(request, userId, ct);
                await db.SaveChangesAsync(ct);
                return await new PcBuildViews(db, clock).RowAsync(build, ct);
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }

    private async Task<PcBuild> SaveAsync(SavePcBuildCommand request, Guid userId, CancellationToken ct)
    {
        var config = await new InventoryLookups(db).ConfigAsync(ct);
        var today = clock.TodayIn(config.TimeZoneId);
        var now = clock.UtcNow;
        Guid? customerId = null;
        if (!string.IsNullOrWhiteSpace(request.CustomerCode))
        {
            var code = request.CustomerCode.Trim().ToUpperInvariant();
            customerId = await db.Set<Customer>().Where(c => c.Code == code).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct)
                         ?? throw new NotFoundException($"El cliente {code} no existe.");
        }
        var parts = await new PcParts(db).ResolveAsync(request.Items, ct);
        foreach (var part in parts.Where(p => p.ListPrice <= 0))
        {
            throw new DomainException("price.missing", $"{part.Item.Variant.Sku} no tiene precio en la lista de precios.");
        }
        PcBuild build;
        if (request.Id is { } id)
        {
            build = await db.Set<PcBuild>().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == id, ct)
                    ?? throw new NotFoundException("El armado no existe o es de otra sucursal.");
            build.Rename(request.Name.Trim(), customerId);
            foreach (var line in build.Lines.ToList())
            {
                build.RemoveLine(line.Id);
            }
        }
        else
        {
            var branchId = await BranchContext.ResolveAsync(db, null, ct);
            var number = await Documents.NextForBranchAsync<PcBuild>(db, b => b.Number, "ARM", branchId, ct);
            build = new PcBuild(config.TenantId, branchId, number, request.Name.Trim(), customerId, today.AddDays(request.ValidDays), userId, now);
            db.Set<PcBuild>().Add(build);
        }
        foreach (var part in parts)
        {
            build.AddLine(part.Slot, part.Item.Variant.Id, part.Component.Quantity, part.ListPrice);
        }
        if (request.Quote)
        {
            build.Quote(today.AddDays(request.ValidDays), today, PcCompatibility.Check(parts.Select(p => p.Component).ToList()), request.AcceptIncompatible, now);
        }
        return build;
    }
}

public sealed class GetPcBuildsHandler(IMinvDbContext db, IClock clock) : IRequestHandler<GetPcBuildsQuery, IReadOnlyList<PcBuildRow>>
{
    public async Task<IReadOnlyList<PcBuildRow>> Handle(GetPcBuildsQuery request, CancellationToken ct)
    {
        var query = db.Set<PcBuild>().AsNoTracking().Include(b => b.Lines).AsQueryable();
        if (request.Status is { } status)
        {
            query = query.Where(b => b.Status == status);
        }
        return await new PcBuildViews(db, clock).RowsAsync(await query.OrderByDescending(b => b.CreatedAt).Take(500).ToListAsync(ct), ct);
    }
}

public sealed class GetPcBuildHandler(IMinvDbContext db, IClock clock) : IRequestHandler<GetPcBuildQuery, PcBuildDetail>
{
    public async Task<PcBuildDetail> Handle(GetPcBuildQuery request, CancellationToken ct)
    {
        var number = request.Number.Trim().ToUpperInvariant();
        var build = await db.Set<PcBuild>().AsNoTracking().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Number == number, ct)
                    ?? throw new NotFoundException($"El armado {number} no existe o es de otra sucursal.");
        var (parts, quoted) = await new PcParts(db).OfBuildAsync(build, ct);
        return new PcBuildDetail(await new PcBuildViews(db, clock).RowAsync(build, ct), PcParts.Check(parts),
            parts.Select((p, i) => PcParts.View(p, quoted[i])).ToList());
    }
}

public sealed class CancelPcBuildHandler(IMinvDbContext db) : IRequestHandler<CancelPcBuildCommand, string>
{
    public async Task<string> Handle(CancelPcBuildCommand request, CancellationToken ct)
    {
        var number = request.Number.Trim().ToUpperInvariant();
        var build = await db.Set<PcBuild>().FirstOrDefaultAsync(b => b.Number == number, ct)
                    ?? throw new NotFoundException($"El armado {number} no existe o es de otra sucursal.");
        build.Cancel();
        await db.SaveChangesAsync(ct);
        return $"✔ Armado {build.Number} anulado.";
    }
}

public sealed class SellPcBuildValidator : AbstractValidator<SellPcBuildCommand>
{
    public SellPcBuildValidator()
    {
        RuleFor(x => x.Number).NotEmpty().WithMessage("Indique el armado.");
        RuleFor(x => x.PaymentMethodCode).NotEmpty().WithMessage("Elija el medio de pago.");
    }
}

/// <summary>
/// Venta de una cotización VIGENTE en la caja del usuario (sucursal del armado): una línea de venta por pieza al precio
/// cotizado, las series de las piezas serializadas, factura del SIN si la empresa factura, y el armado vendido con la venta
/// vinculada, todo en la MISMA transacción (reintento optimista ×3).
/// </summary>
public sealed class SellPcBuildHandler(IMinvDbContext db, ICurrentUser user, IClock clock, IFiscalDocumentSerializer? serializer = null)
    : IRequestHandler<SellPcBuildCommand, CheckoutResult>
{
    public async Task<CheckoutResult> Handle(SellPcBuildCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para vender.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var result = await SellAsync(request, userId, ct);
                await db.SaveChangesAsync(ct);
                return result;
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }

    private async Task<CheckoutResult> SellAsync(SellPcBuildCommand request, Guid userId, CancellationToken ct)
    {
        var number = request.Number.Trim().ToUpperInvariant();
        var build = await db.Set<PcBuild>().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Number == number, ct)
                    ?? throw new NotFoundException($"El armado {number} no existe o es de otra sucursal.");
        var config = await new InventoryLookups(db).ConfigAsync(ct);
        var today = clock.TodayIn(config.TimeZoneId);
        Guard.That(build.Status != PcBuildStatus.Draft, "pcbuild.not_quoted", $"El armado {build.Number} está en borrador: cotícelo antes de venderlo.");
        Guard.That(build.Status == PcBuildStatus.Quoted, "pcbuild.state", $"El armado {build.Number} está {PcBuild.Describe(build.Status)}: ya no se puede vender.");
        Guard.That(!build.IsExpiredOn(today), "pcbuild.expired", $"La cotización {build.Number} venció el {build.ValidUntil:dd/MM/yyyy}: cotícela de nuevo.");
        var session = await db.Set<PosSession>().Where(s => s.Status == PosSessionStatus.Open && s.OpenedByUserId == userId)
                          .OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync(ct)
                      ?? throw new DomainException("pos.closed", "Abra un turno de caja antes de cobrar.");
        Guard.That(session.BranchId == build.BranchId, "pcbuild.branch", $"El armado {build.Number} es de otra sucursal: cóbrelo en una caja de esa sucursal.");
        var register = await db.Set<PosRegister>().FirstAsync(r => r.Id == session.PosRegisterId, ct);
        var customerCode = request.CustomerCode
                           ?? (build.CustomerId is { } c ? await db.Set<Customer>().Where(x => x.Id == c).Select(x => x.Code).FirstAsync(ct) : "CF");

        // Una línea de venta por pieza, al precio cotizado; las series de cada producto se reparten entre sus piezas
        var variantIds = build.Lines.Select(l => l.VariantId).Distinct().ToList();
        var skus = await db.Set<ProductVariant>().Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Sku, ct);
        var serials = (request.Serials ?? []).GroupBy(s => s.Sku.Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => new Queue<string>(g.SelectMany(s => s.Serials)), StringComparer.Ordinal);
        var lines = new List<SaleLineInput>();
        var prices = new List<decimal?>();
        foreach (var line in build.Lines.OrderBy(l => l.Slot).ThenBy(l => l.Id))
        {
            var sku = skus[line.VariantId];
            List<string>? mine = null;
            if (serials.TryGetValue(sku, out var queue) && queue.Count > 0)
            {
                mine = [];
                while (queue.Count > 0 && mine.Count < line.Quantity)
                {
                    mine.Add(queue.Dequeue());
                }
            }
            lines.Add(new SaleLineInput(sku, line.Quantity, 0, mine));
            prices.Add(line.QuotedUnitPrice);
        }
        var extra = serials.Where(s => s.Value.Count > 0).Select(s => s.Key).ToList();
        Guard.That(extra.Count == 0, SerialErrorCodes.Count, $"Sobran series de {string.Join(", ", extra)}: no son piezas del armado o son más que las unidades.");
        var sale = await SaleWriter.SellAsync(db, clock, userId, new SaleOrigin(session.BranchId, register.WarehouseId, session.Id, SaleChannels.Pos, register.Id),
            customerCode, request.PaymentMethodCode, lines, request.CashReceived, request.PaymentReference,
            new SaleFiscal(serializer, BillingLookups.UserCode(user), request.Buyer, request.CardNumber), ct, prices);
        build.MarkSold(sale.Invoice.Id, today);
        return sale.Result;
    }
}
