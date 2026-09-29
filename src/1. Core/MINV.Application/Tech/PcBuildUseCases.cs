using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Billing;
using MINV.Application.Common;
using MINV.Application.Inventory;
using MINV.Application.Sales;
using MINV.Application.Storefront;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Application.Tech;

// =====================================================================================================================
// V4.2 · Armador de PC (regla T-06): la compatibilidad la decide SOLO PcCompatibility.Check con las especificaciones que
// tienen clave de compatibilidad; los casos de uso arman las piezas (PcComponent), cotizan con precios congelados y
// venden la cotización con los casos de uso normales de venta.
// =====================================================================================================================

/// <summary>V4.2 · Pieza del armado resuelta: producto, componente para las reglas, precio de lista, stock y ficha. V7: el
/// producto sin ranura de un carrito lleva <see cref="Slot"/> nulo; su <see cref="Component"/> (nombre, cantidad y ficha) se
/// arma como periférico, que no tiene reglas: en un carrito la compatibilidad no se evalúa (regla P-05).</summary>
internal sealed record PcPart(PcSlot? Slot, VariantInfo Item, PcComponent Component, decimal ListPrice, decimal Stock, Guid? ImageId,
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

    public async Task<IReadOnlyList<PcPart>> PartsAsync(IReadOnlyList<(PcSlot? Slot, VariantInfo Item, int Quantity)> parts, CancellationToken ct)
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
            return new PcPart(p.Slot, p.Item, new PcComponent(p.Slot ?? PcSlot.Peripheral, p.Item.Variant.Sku, name, p.Quantity, specs),
                prices.GetValueOrDefault(p.Item.Variant.Id), stock.GetValueOrDefault(p.Item.Variant.Id),
                images.TryGetValue(p.Item.Variant.Id, out var image) ? image : null, TechCatalogReader.KeySpecs(mine), brands.GetValueOrDefault(p.Item.Product.Id));
        }).ToList();
    }

    /// <summary>Informe del armado con sus piezas. V7: en un carrito (<paramref name="kind"/>) la compatibilidad no se evalúa:
    /// el informe sale vacío (sin avisos de piezas faltantes ni consumo).</summary>
    public static PcBuildCheckView Check(IReadOnlyList<PcPart> parts, IReadOnlyList<decimal>? prices = null, PcBuildKind kind = PcBuildKind.Build)
    {
        var report = kind == PcBuildKind.Cart ? PcCompatibilityReport.Empty : PcCompatibility.Check(parts.Select(p => p.Component).ToList());
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

/// <summary>V4.2 · Filas de los armados (compatibilidad evaluada con las fichas vigentes). V6: canal, contacto (teléfono y correo
/// solo con <c>sales.pcbuild.manage</c>, regla S-06), reserva, publicación y unidades reservadas.</summary>
internal sealed class PcBuildViews(IMinvDbContext db, IClock clock, ICurrentUser? user = null)
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
        var lineIds = builds.Where(b => b.Status == PcBuildStatus.Reserved).SelectMany(b => b.Lines.Select(l => l.Id)).ToList();
        var reserved = lineIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : (await db.Set<StockReservation>().AsNoTracking()
                .Where(r => r.PcBuildLineId != null && lineIds.Contains(r.PcBuildLineId.Value) && r.Status == ReservationStatus.Active)
                .Select(r => new { LineId = r.PcBuildLineId!.Value, r.Quantity }).ToListAsync(ct))
            .GroupBy(r => r.LineId).ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
        // El contacto y los datos para la factura de quien reserva: solo para quien gestiona las reservas (reglas S-06 y P-05)
        var showContact = user?.HasPermission(PermissionCodes.PcBuildManage) == true;
        var parts = new PcParts(db);
        var result = new List<PcBuildRow>(builds.Count);
        foreach (var b in builds)
        {
            // V7 · En un carrito la compatibilidad no se evalúa (regla P-05)
            var compatible = b.IsCart || PcCompatibility.Check((await parts.OfBuildAsync(b, ct)).Parts.Select(p => p.Component).ToList()).IsCompatible;
            result.Add(new PcBuildRow(b.Id, b.Number, b.Name, branches.GetValueOrDefault(b.BranchId, "?"),
                b.CustomerId is { } c ? customers.GetValueOrDefault(c) : null, b.Status, b.ValidUntil, b.IsExpiredOn(today), b.Total, b.Lines.Count,
                compatible, b.CreatedAt, b.InvoiceId is { } i ? invoices.GetValueOrDefault(i) : null, b.QuotedWithErrors,
                b.Channel, b.ContactName, showContact ? b.ContactPhone : null, showContact ? b.ContactEmail : null, b.ReservedUntil, b.PublishedToWeb,
                b.CancelReason, b.Notes, b.Lines.Sum(l => reserved.GetValueOrDefault(l.Id)), b.Kind,
                showContact ? b.BuyerDocumentType : null, showContact ? b.BuyerDocumentNumber : null, showContact ? b.BuyerComplement : null,
                showContact ? b.BuyerName : null));
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
        var options = await parts.PartsAsync(candidates.Select(c => ((PcSlot?)request.Slot, c, 1)).ToList(), ct);
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
        RuleFor(x => x.Kind).IsInEnum().WithMessage("El tipo (armado o carrito) no existe.");
    }
}

/// <summary>Guarda (y cotiza) el armado con los precios de la lista vigente; numeración ARM-&lt;sucursal&gt;-000001 con
/// reintento. V7: un carrito se numera RES-&lt;sucursal&gt;-000001, sus productos van sin ranura y se cotiza sin evaluar la
/// compatibilidad (regla P-05).</summary>
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
                return await new PcBuildViews(db, clock, user).RowAsync(build, ct);
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
            // V7 · El tipo se fija al crear: un armado no se convierte en carrito ni al revés
            Guard.That(build.Kind == request.Kind, "pcbuild.kind",
                $"{build.Number} es {(build.IsCart ? "un carrito" : "un armado de PC")}: el tipo no cambia después de crearlo.");
            build.Rename(request.Name.Trim(), customerId);
            foreach (var line in build.Lines.ToList())
            {
                build.RemoveLine(line.Id);
            }
        }
        else
        {
            var branchId = await BranchContext.ResolveAsync(db, null, ct);
            var number = await Documents.NextForBranchAsync<PcBuild>(db, b => b.Number, PcBuild.NumberPrefixOf(request.Kind, PcBuildChannel.Desktop),
                branchId, ct);
            build = request.Kind == PcBuildKind.Cart
                ? PcBuild.CreateDesktopCart(config.TenantId, branchId, number, request.Name.Trim(), customerId, today.AddDays(request.ValidDays), userId, now)
                : new PcBuild(config.TenantId, branchId, number, request.Name.Trim(), customerId, today.AddDays(request.ValidDays), userId, now);
            db.Set<PcBuild>().Add(build);
        }
        foreach (var part in parts)
        {
            build.AddLine(part.Slot, part.Item.Variant.Id, part.Component.Quantity, part.ListPrice);
        }
        if (request.Quote)
        {
            // V7 · Un carrito se cotiza con el informe vacío: la compatibilidad solo se evalúa en armados (regla P-05)
            var report = build.IsCart ? PcCompatibilityReport.Empty : PcCompatibility.Check(parts.Select(p => p.Component).ToList());
            build.Quote(today.AddDays(request.ValidDays), today, report, request.AcceptIncompatible, now, userId);
        }
        return build;
    }
}

public sealed class GetPcBuildsHandler(IMinvDbContext db, IClock clock, ICurrentUser user) : IRequestHandler<GetPcBuildsQuery, IReadOnlyList<PcBuildRow>>
{
    public async Task<IReadOnlyList<PcBuildRow>> Handle(GetPcBuildsQuery request, CancellationToken ct)
    {
        var query = db.Set<PcBuild>().AsNoTracking().Include(b => b.Lines).AsQueryable();
        if (request.Status is { } status)
        {
            query = query.Where(b => b.Status == status);
        }
        if (request.Channel is { } channel)
        {
            query = query.Where(b => b.Channel == channel);
        }
        if (request.Kind is { } kind)
        {
            query = query.Where(b => b.Kind == kind);
        }
        return await new PcBuildViews(db, clock, user).RowsAsync(await query.OrderByDescending(b => b.CreatedAt).Take(500).ToListAsync(ct), ct);
    }
}

public sealed class GetPcBuildHandler(IMinvDbContext db, IClock clock, ICurrentUser user) : IRequestHandler<GetPcBuildQuery, PcBuildDetail>
{
    public async Task<PcBuildDetail> Handle(GetPcBuildQuery request, CancellationToken ct)
    {
        var number = request.Number.Trim().ToUpperInvariant();
        var build = await db.Set<PcBuild>().AsNoTracking().Include(b => b.Lines).Include(b => b.History).FirstOrDefaultAsync(b => b.Number == number, ct)
                    ?? throw new NotFoundException($"El armado {number} no existe o es de otra sucursal.");
        var (parts, quoted) = await new PcParts(db).OfBuildAsync(build, ct);
        var userIds = build.History.Select(e => e.UserId).Distinct().ToList();
        var users = await db.Set<User>().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var history = build.History.OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .Select(e => new PcBuildEventView(e.OccurredAt, e.Action, e.Status, e.Detail, users.GetValueOrDefault(e.UserId, "?"))).ToList();
        return new PcBuildDetail(await new PcBuildViews(db, clock, user).RowAsync(build, ct), PcParts.Check(parts, kind: build.Kind),
            parts.Select((p, i) => PcParts.View(p, quoted[i])).ToList(), history);
    }
}

/// <summary>Anula el armado. V6: si estaba reservado libera la reserva (el stock vuelve) en la misma transacción.</summary>
public sealed class CancelPcBuildHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<CancelPcBuildCommand, string>
{
    public async Task<string> Handle(CancelPcBuildCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var number = request.Number.Trim().ToUpperInvariant();
                var build = await db.Set<PcBuild>().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Number == number, ct)
                            ?? throw new NotFoundException($"El armado {number} no existe o es de otra sucursal.");
                var now = clock.UtcNow;
                if (build.Status == PcBuildStatus.Reserved)
                {
                    await PcBuildStock.ReleaseAsync(db, build, ct);
                    build.ReleaseReservation(request.Reason?.Trim() is { Length: > 0 } reason ? reason : "Anulado por el vendedor", now, userId);
                }
                else
                {
                    build.Cancel(userId, now, request.Reason);
                }
                await db.SaveChangesAsync(ct);
                return $"✔ Armado {build.Number} anulado.";
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }
}

/// <summary>V6 · Reserva el stock de una cotización del escritorio (todo o nada, misma transacción, regla S-03).</summary>
public sealed class ReservePcBuildValidator : AbstractValidator<ReservePcBuildCommand>
{
    public ReservePcBuildValidator()
    {
        RuleFor(x => x.Number).NotEmpty().WithMessage("Indique el armado.");
        RuleFor(x => x.Hours).InclusiveBetween(1, 24 * 30).WithMessage("La reserva dura de 1 hora a 30 días.");
    }
}

public sealed class ReservePcBuildHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<ReservePcBuildCommand, PcBuildRow>
{
    public async Task<PcBuildRow> Handle(ReservePcBuildCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var number = request.Number.Trim().ToUpperInvariant();
                var build = await db.Set<PcBuild>().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Number == number, ct)
                            ?? throw new NotFoundException($"El armado {number} no existe o es de otra sucursal.");
                var config = await new InventoryLookups(db).ConfigAsync(ct);
                var today = clock.TodayIn(config.TimeZoneId);
                var now = clock.UtcNow;
                var until = now.AddHours(request.Hours);
                build.Reserve(now, until, today, userId);
                await PcBuildStock.ReserveAsync(db, build, now, until, ct);
                await db.SaveChangesAsync(ct);
                return await new PcBuildViews(db, clock, user).RowAsync(build, ct);
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }
}

public sealed class ReserveCartValidator : AbstractValidator<ReserveCartCommand>
{
    public ReserveCartValidator()
    {
        RuleFor(x => x.Items).NotEmpty().WithMessage("Agregue al menos un producto al carrito.")
            .Must(i => i is null || i.Count <= PcBuild.MaxLines).WithMessage($"Una reserva admite como máximo {PcBuild.MaxLines} líneas.");
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.Sku).NotEmpty().WithMessage("Cada línea necesita su producto.").MaximumLength(60);
            i.RuleFor(x => x.Quantity).InclusiveBetween(1, PcBuild.MaxQuantity).WithMessage($"La cantidad de cada producto va de 1 a {PcBuild.MaxQuantity}.");
        });
        RuleFor(x => x.ContactName).NotEmpty().WithMessage("Indique el nombre de quien recoge la reserva.").MaximumLength(120)
            .Must(ReservationRules.IsPlain).WithMessage("El nombre de contacto " + ReservationRules.ControlCharacters);
        RuleFor(x => x.ContactPhone).NotEmpty().WithMessage("Indique un teléfono o WhatsApp para avisar al cliente.").MaximumLength(30);
        RuleFor(x => x.ContactEmail).MaximumLength(254);
        RuleFor(x => x.Notes).MaximumLength(500).WithMessage("Las notas superan 500 caracteres.")
            .Must(ReservationRules.IsPlain).WithMessage("Las notas van en una sola línea: " + ReservationRules.ControlCharacters);
        RuleFor(x => x.Name).MaximumLength(150)
            .Must(ReservationRules.IsPlain).WithMessage("El nombre de la reserva " + ReservationRules.ControlCharacters);
        RuleFor(x => x.HoldDays).InclusiveBetween(1, StorefrontOptions.HoldDaysLimit).WithMessage(ReservationRules.HoldDaysMessage);
        RuleFor(x => x.Buyer!).SetValidator(new ReservationBuyerValidator()).When(x => x.Buyer is not null);
    }
}

/// <summary>
/// V7 · Carrito de mostrador (regla P-05): el mismo camino que la reserva de la tienda (<see cref="ReservationWriter"/>) en
/// el canal Desktop y en la sucursal activa: carrito cotizado a los precios vigentes y reservado con una reserva de stock
/// por línea, todo o nada, en UNA transacción con reintento optimista (existencias y numeración).
/// </summary>
public sealed class ReserveCartHandler(IMinvDbContext db, ICurrentUser user, IClock clock, StorefrontOptions? options = null)
    : IRequestHandler<ReserveCartCommand, PcBuildRow>
{
    public async Task<PcBuildRow> Handle(ReserveCartCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Guid? customerId = null;
                if (!string.IsNullOrWhiteSpace(request.CustomerCode))
                {
                    var code = request.CustomerCode.Trim().ToUpperInvariant();
                    customerId = await db.Set<Customer>().Where(c => c.Code == code).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct)
                                 ?? throw new NotFoundException($"El cliente {code} no existe.");
                }
                var lines = request.Items.Select(i => new StorefrontReservationLineInput(i.Sku, i.Quantity)).ToList();
                var build = await ReservationWriter.CreateAsync(db, clock, options, new ReservationSpec(PcBuildKind.Cart, PcBuildChannel.Desktop, lines,
                    request.ContactName, request.ContactPhone, request.ContactEmail, request.Notes, request.Name, request.HoldDays, request.Buyer, customerId),
                    userId, clock.UtcNow, ct);
                // V7 · El correo de confirmación se encola aquí, en la misma transacción (regla P-06)
                await db.SaveChangesAsync(ct);
                return await new PcBuildViews(db, clock, user).RowAsync(build, ct);
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }
}

public sealed class ReleasePcBuildReservationValidator : AbstractValidator<ReleasePcBuildReservationCommand>
{
    public ReleasePcBuildReservationValidator()
    {
        RuleFor(x => x.Number).NotEmpty().WithMessage("Indique el armado.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Indique el motivo de la liberación.").MaximumLength(250);
    }
}

/// <summary>V6 · Libera la reserva (el stock vuelve; el armado queda anulado con su motivo).</summary>
public sealed class ReleasePcBuildReservationHandler(IMinvDbContext db, ICurrentUser user, IClock clock)
    : IRequestHandler<ReleasePcBuildReservationCommand, PcBuildRow>
{
    public async Task<PcBuildRow> Handle(ReleasePcBuildReservationCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var number = request.Number.Trim().ToUpperInvariant();
                var build = await db.Set<PcBuild>().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Number == number, ct)
                            ?? throw new NotFoundException($"El armado {number} no existe o es de otra sucursal.");
                await PcBuildStock.ReleaseAsync(db, build, ct);
                build.ReleaseReservation(request.Reason, clock.UtcNow, userId);
                await db.SaveChangesAsync(ct);
                return await new PcBuildViews(db, clock, user).RowAsync(build, ct);
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }
}

/// <summary>V6 · Publica o retira un armado sugerido de la tienda web.</summary>
public sealed class PublishPcBuildHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<PublishPcBuildCommand, PcBuildRow>
{
    public async Task<PcBuildRow> Handle(PublishPcBuildCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión.");
        var number = request.Number.Trim().ToUpperInvariant();
        var build = await db.Set<PcBuild>().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Number == number, ct)
                    ?? throw new NotFoundException($"El armado {number} no existe o es de otra sucursal.");
        if (request.Published)
        {
            build.Publish(userId, clock.UtcNow);
        }
        else
        {
            build.Unpublish(userId, clock.UtcNow);
        }
        await db.SaveChangesAsync(ct);
        return await new PcBuildViews(db, clock, user).RowAsync(build, ct);
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
        Guard.That(build.Status is PcBuildStatus.Quoted or PcBuildStatus.Reserved, "pcbuild.state",
            $"El armado {build.Number} está {PcBuild.Describe(build.Status)}: ya no se puede vender.");
        Guard.That(!build.IsExpiredOn(today), "pcbuild.expired", $"La cotización {build.Number} venció el {build.ValidUntil:dd/MM/yyyy}: cotícela de nuevo.");
        // V6 · Un armado reservado se vende CONSUMIENDO sus reservas (regla S-04): la venta registra la salida una sola vez
        if (build.Status == PcBuildStatus.Reserved)
        {
            await PcBuildStock.FulfillAsync(db, build, ct);
        }
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
        // V7 · Si el cajero no capturó comprador, la factura sale con los datos que dejó quien reservó (regla P-05)
        var buyer = request.Buyer ?? (build.HasBuyer
            ? new FiscalBuyerInput(build.BuyerDocumentType!.Value, build.BuyerDocumentNumber!, build.BuyerComplement, build.BuyerName, build.ContactEmail)
            : null);
        var sale = await SaleWriter.SellAsync(db, clock, userId, new SaleOrigin(session.BranchId, register.WarehouseId, session.Id, SaleChannels.Pos, register.Id),
            customerCode, request.PaymentMethodCode, lines, request.CashReceived, request.PaymentReference,
            new SaleFiscal(serializer, BillingLookups.UserCode(user), buyer, request.CardNumber), ct, prices);
        build.MarkSold(sale.Invoice.Id, today, userId, clock.UtcNow);
        return sale.Result;
    }
}
