using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Integration;
using MINV.Application.Inventory;
using MINV.Application.Remote;
using MINV.Application.Tech;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Application.Storefront;

/// <summary>
/// V6 · Reservas de stock de un armado (regla S-03): una <see cref="StockReservation"/> por línea (origen
/// <c>PcBuildLineId</c>) sobre las existencias de la sucursal del armado, todo o nada; liberar, vencer y cumplir (al vender)
/// devuelven la cantidad reservada por los métodos de <see cref="StockLevel"/>. Lo comparten la tienda web y el escritorio.
/// </summary>
internal static class PcBuildStock
{
    /// <summary>Reserva el stock de cada línea hasta <paramref name="until"/>: de las posiciones de la sucursal con más
    /// disponible (una línea puede repartirse en varias). Si falta stock de cualquier pieza lanza
    /// <see cref="StorefrontStockException"/> con el detalle y no reserva nada.</summary>
    public static async Task ReserveAsync(IMinvDbContext db, PcBuild build, DateTimeOffset now, DateTimeOffset until, CancellationToken ct)
    {
        var variantIds = build.Lines.Select(l => l.VariantId).Distinct().ToList();
        var levels = await (from l in db.Set<StockLevel>()
                            join b in db.Set<Batch>() on l.BatchId equals b.Id
                            where l.BranchId == build.BranchId && variantIds.Contains(b.VariantId)
                            select new { Level = l, b.VariantId }).ToListAsync(ct);
        var names = await NamesAsync(db, variantIds, ct);
        var remaining = levels.ToDictionary(x => x.Level.Id, x => x.Level.Available);
        var plan = new List<(StockLevel Level, PcBuildLine Line, decimal Quantity)>();
        var shortages = new List<StorefrontShortage>();
        foreach (var line in build.Lines.OrderBy(l => l.Slot).ThenBy(l => l.Id))
        {
            var mine = levels.Where(x => x.VariantId == line.VariantId).ToList();
            var available = Quantities.Round6(mine.Sum(x => remaining[x.Level.Id]));
            var need = (decimal)line.Quantity;
            foreach (var x in mine.OrderByDescending(x => remaining[x.Level.Id]))
            {
                if (need <= 0)
                {
                    break;
                }
                var take = Math.Min(need, remaining[x.Level.Id]);
                if (take <= 0)
                {
                    continue;
                }
                plan.Add((x.Level, line, take));
                remaining[x.Level.Id] = Quantities.Round6(remaining[x.Level.Id] - take);
                need = Quantities.Round6(need - take);
            }
            if (need > 0)
            {
                var (sku, name) = names.GetValueOrDefault(line.VariantId, ("?", "?"));
                shortages.Add(new StorefrontShortage(sku, name, line.Quantity, Math.Max(0, available)));
            }
        }
        if (shortages.Count > 0)
        {
            throw new StorefrontStockException(shortages);
        }
        foreach (var (level, line, quantity) in plan)
        {
            db.Set<StockReservation>().Add(level.Reserve(quantity, until, now, pcBuildLineId: line.Id));
        }
    }

    /// <summary>Libera las reservas activas del armado (el stock vuelve a disponible).</summary>
    public static async Task ReleaseAsync(IMinvDbContext db, PcBuild build, CancellationToken ct)
    {
        foreach (var (level, reservation) in await ActiveAsync(db, build, ct))
        {
            level.Release(reservation);
        }
    }

    /// <summary>Vence las reservas activas del armado (trabajo en segundo plano).</summary>
    public static async Task ExpireAsync(IMinvDbContext db, PcBuild build, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var (level, reservation) in await ActiveAsync(db, build, ct))
        {
            level.Expire(reservation, now);
        }
    }

    /// <summary>Consume las reservas activas al vender (la venta registra la salida una sola vez, regla S-04).</summary>
    public static async Task FulfillAsync(IMinvDbContext db, PcBuild build, CancellationToken ct)
    {
        foreach (var (level, reservation) in await ActiveAsync(db, build, ct))
        {
            level.Fulfill(reservation);
        }
    }

    /// <summary>Cantidad reservada activa por variante (para las vistas del escritorio).</summary>
    public static async Task<decimal> ReservedAsync(IMinvDbContext db, PcBuild build, CancellationToken ct)
    {
        var lineIds = build.Lines.Select(l => l.Id).ToList();
        return await db.Set<StockReservation>().Where(r => r.PcBuildLineId != null && lineIds.Contains(r.PcBuildLineId.Value) && r.Status == ReservationStatus.Active)
            .SumAsync(r => r.Quantity, ct);
    }

    private static async Task<IReadOnlyList<(StockLevel Level, StockReservation Reservation)>> ActiveAsync(IMinvDbContext db, PcBuild build, CancellationToken ct)
    {
        var lineIds = build.Lines.Select(l => l.Id).ToList();
        var reservations = await db.Set<StockReservation>()
            .Where(r => r.PcBuildLineId != null && lineIds.Contains(r.PcBuildLineId.Value) && r.Status == ReservationStatus.Active).ToListAsync(ct);
        var levelIds = reservations.Select(r => r.StockLevelId).Distinct().ToList();
        var levels = await db.Set<StockLevel>().Where(l => levelIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        return reservations.Select(r => (levels[r.StockLevelId], r)).ToList();
    }

    public static async Task<IReadOnlyDictionary<Guid, (string Sku, string Name)>> NamesAsync(IMinvDbContext db, IReadOnlyCollection<Guid> variantIds,
        CancellationToken ct) =>
        await (from v in db.Set<ProductVariant>()
               join p in db.Set<Product>() on v.ProductId equals p.Id
               where variantIds.Contains(v.Id)
               select new { v.Id, v.Sku, Name = v.Name == null || v.Name == "" ? p.Name : p.Name + " · " + v.Name })
            .ToDictionaryAsync(x => x.Id, x => (x.Sku, x.Name), ct);
}

/// <summary>V6 · Vista de una reserva para el cliente web (sin teléfono ni correo).</summary>
internal static class StorefrontReservationViews
{
    public static string StatusOf(PcBuild build, DateTimeOffset now) => build.Status switch
    {
        PcBuildStatus.Reserved when build.IsReservationExpired(now) => "Expired",
        PcBuildStatus.Reserved => "Reserved",
        PcBuildStatus.Sold => "Sold",
        PcBuildStatus.Cancelled when build.CancelReason == PcBuild.ExpiredReason => "Expired",
        PcBuildStatus.Cancelled => "Cancelled",
        _ => build.Status.ToString(),
    };

    public static string StatusText(string status) => status switch
    {
        "Reserved" => "Reservada",
        "Sold" => "Vendida",
        "Cancelled" => "Cancelada",
        "Expired" => "Vencida",
        "Quoted" => "Cotizada",
        "Draft" => "En borrador",
        _ => status,
    };

    /// <summary>V7: <paramref name="mailQueued"/> lo decide quien encola el correo de confirmación (al reservar); las
    /// consultas posteriores no lo informan. Los datos para la factura NUNCA salen en esta vista (regla P-05).</summary>
    public static async Task<StorefrontReservationView> ViewAsync(IMinvDbContext db, PcBuild build, DateTimeOffset now, CancellationToken ct,
        bool mailQueued = false)
    {
        var names = await PcBuildStock.NamesAsync(db, build.Lines.Select(l => l.VariantId).Distinct().ToList(), ct);
        var branch = db.Set<Branch>().Local.FirstOrDefault(b => b.Id == build.BranchId)?.Code
                     ?? await db.Set<Branch>().Where(b => b.Id == build.BranchId).Select(b => b.Code).FirstAsync(ct);
        var status = StatusOf(build, now);
        return new StorefrontReservationView(build.Number, status, StatusText(status), build.CreatedAt, build.ReservedUntil, build.Total,
            build.ContactName ?? string.Empty, branch, build.Notes, build.QuotedWithErrors,
            build.Lines.OrderBy(l => l.Slot).ThenBy(l => l.Id).Select(l =>
            {
                var (sku, name) = names.GetValueOrDefault(l.VariantId, ("?", "?"));
                return new StorefrontReservationLine(l.Slot is { } slot ? StorefrontCatalogReader.WebSlots[slot] : null, sku, name, l.Quantity,
                    l.QuotedUnitPrice, l.Subtotal);
            }).ToList(), build.CancelReason, StorefrontKinds.Text(build.Kind), mailQueued);
    }
}

/// <summary>V6 · Ranura del dominio para cada pieza de la web: la indicada por la web o, si no viene, la que dice la ficha
/// técnica (clave de compatibilidad que identifica la ranura) o la categoría (monitor, software, servicio, periférico).
/// V7: en un carrito (<c>deduce = false</c>) la ranura NO se deduce: la línea queda sin ranura salvo que la web la mande.</summary>
internal static class StorefrontSlots
{
    private static readonly IReadOnlyDictionary<string, PcSlot> FromWeb = new Dictionary<string, PcSlot>(StringComparer.OrdinalIgnoreCase)
    {
        ["cpu"] = PcSlot.Cpu, ["motherboard"] = PcSlot.Motherboard, ["ram"] = PcSlot.Ram, ["gpu"] = PcSlot.Gpu, ["storage"] = PcSlot.Storage,
        ["psu"] = PcSlot.Psu, ["case"] = PcSlot.Case, ["cooler"] = PcSlot.Cooler, ["monitor"] = PcSlot.Monitor, ["peripherals"] = PcSlot.Peripheral,
        ["peripheral"] = PcSlot.Peripheral, ["software"] = PcSlot.Software, ["service"] = PcSlot.Service,
    };

    public static async Task<IReadOnlyList<PcBuildItemInput>> ResolveAsync(IMinvDbContext db, IReadOnlyList<StorefrontReservationLineInput> lines,
        CancellationToken ct, bool deduce = true)
    {
        var skus = lines.Select(l => l.Sku.Trim().ToUpperInvariant()).Distinct().ToList();
        var products = await (from v in db.Set<ProductVariant>()
                              join p in db.Set<Product>() on v.ProductId equals p.Id
                              where skus.Contains(v.Sku)
                              select new { v.Sku, ProductId = p.Id, p.CategoryId, p.IsActive, VariantActive = v.IsActive }).ToListAsync(ct);
        var missing = skus.Where(s => products.All(p => p.Sku != s)).ToList();
        if (missing.Count > 0)
        {
            throw new NotFoundException($"El producto {string.Join(", ", missing)} no está en el catálogo.");
        }
        var inactive = products.Where(p => !p.IsActive || !p.VariantActive).Select(p => p.Sku).ToList();
        Guard.That(inactive.Count == 0, "product.inactive", $"El producto {string.Join(", ", inactive)} ya no se vende.");
        var reader = new TechCatalogReader(db);
        var keys = (await reader.ValuesAsync(products.Select(p => p.ProductId).Distinct().ToList(), null, ct))
            .Where(v => v.Definition.CompatibilityKey is not null).GroupBy(v => v.ProductId)
            .ToDictionary(g => g.Key, g => g.Select(v => v.Definition.CompatibilityKey!).ToHashSet(StringComparer.Ordinal));
        var lineage = new Dictionary<Guid, IReadOnlyList<string>>();
        var result = new List<PcBuildItemInput>(lines.Count);
        foreach (var line in lines)
        {
            var sku = line.Sku.Trim().ToUpperInvariant();
            var product = products.First(p => p.Sku == sku);
            PcSlot? slot;
            if (line.Slot is { Length: > 0 } web)
            {
                Guard.That(FromWeb.TryGetValue(web.Trim(), out var given), "pcbuild.slot", $"La ranura «{web}» no existe.");
                slot = given == PcSlot.Software && await IsServiceAsync(product.CategoryId) ? PcSlot.Service : given;
            }
            else if (!deduce)
            {
                slot = null;
            }
            else
            {
                slot = PcParts.SlotKeys.FirstOrDefault(k => keys.GetValueOrDefault(product.ProductId)?.Contains(k.Value) == true) is { Key: var bySpec, Value: not null }
                    ? bySpec
                    : await ByCategoryAsync(product.CategoryId);
            }
            result.Add(new PcBuildItemInput(slot, sku, line.Quantity));
        }
        return result;

        async Task<IReadOnlyList<string>> LineageAsync(Guid categoryId)
        {
            if (!lineage.TryGetValue(categoryId, out var codes))
            {
                var ids = (await reader.AncestorsAsync(categoryId, ct)).OrderBy(a => a.Value).Select(a => a.Key).ToList();
                codes = await db.Set<Category>().Where(c => ids.Contains(c.Id)).Select(c => c.Code).ToListAsync(ct);
                lineage[categoryId] = codes;
            }
            return codes;
        }

        async Task<bool> IsServiceAsync(Guid categoryId) => (await LineageAsync(categoryId)).Contains("SRV");

        async Task<PcSlot> ByCategoryAsync(Guid categoryId)
        {
            var codes = await LineageAsync(categoryId);
            return codes.Contains("MON") ? PcSlot.Monitor
                : codes.Contains("SRV") ? PcSlot.Service
                : codes.Contains("SOFT") || codes.Contains("LIC") ? PcSlot.Software
                : codes.Contains("STO") || codes.Contains("ALMC") ? PcSlot.Storage
                : PcSlot.Peripheral;
        }
    }
}

/// <summary>V7 · Lo que hace falta para crear una reserva (armado o carrito) desde cualquier canal: la tienda web, el mostrador
/// del escritorio o la cuenta de un cliente. <paramref name="CustomerId"/> liga la reserva a un cliente registrado.</summary>
internal sealed record ReservationSpec(PcBuildKind Kind, PcBuildChannel Channel, IReadOnlyList<StorefrontReservationLineInput> Lines,
    string? ContactName, string? ContactPhone, string? ContactEmail, string? Notes, string? Name, int? HoldDays = null,
    ReservationBuyerInput? Buyer = null, Guid? CustomerId = null);

/// <summary>
/// V7 · Crea una reserva (reglas S-03 y P-05): el armado o el carrito cotizado a los precios vigentes y RESERVADO, con una
/// reserva de stock por línea en la sucursal activa (todo o nada). No guarda: quien llama agrega lo suyo (idempotencia,
/// correo) y guarda todo en UN <c>SaveChanges</c>, con su reintento optimista. Punto único de las reservas nuevas de la
/// tienda (<see cref="CreateStorefrontReservationHandler"/>) y del mostrador (<c>ReserveCartCommand</c>).
/// </summary>
internal static class ReservationWriter
{
    public static async Task<PcBuild> CreateAsync(IMinvDbContext db, IClock clock, StorefrontOptions? options, ReservationSpec spec, Guid userId,
        DateTimeOffset now, CancellationToken ct)
    {
        var config = await new InventoryLookups(db).ConfigAsync(ct);
        var today = clock.TodayIn(config.TimeZoneId);
        var branchId = await BranchContext.ResolveAsync(db, null, ct);
        var until = now.AddHours((options ?? new StorefrontOptions()).HoursFor(spec.HoldDays));
        var zone = TimeZoneInfo.FindSystemTimeZoneById(config.TimeZoneId);
        var validUntil = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(until, zone).DateTime);
        if (validUntil < today)
        {
            validUntil = today;
        }
        var cart = spec.Kind == PcBuildKind.Cart;
        // En un carrito la ranura no se deduce: la línea queda sin ranura salvo que la web la mande (regla P-05)
        var items = await StorefrontSlots.ResolveAsync(db, spec.Lines, ct, deduce: !cart);
        var parts = await new PcParts(db).ResolveAsync(items, ct);
        var unpriced = parts.Where(p => p.ListPrice <= 0).Select(p => p.Item.Variant.Sku).ToList();
        Guard.That(unpriced.Count == 0, "price.missing", $"{string.Join(", ", unpriced)} no tiene precio en la lista de precios.");
        var prefix = PcBuild.NumberPrefixOf(spec.Kind, spec.Channel);
        var number = spec.Channel == PcBuildChannel.Web
            ? await Documents.NextNumberAsync(db.Set<PcBuild>(), b => b.Number, prefix, ct)
            : await Documents.NextForBranchAsync<PcBuild>(db, b => b.Number, prefix, branchId, ct);
        var name = NameOf(spec);
        PcBuild build;
        if (spec.Channel == PcBuildChannel.Web)
        {
            build = cart
                ? PcBuild.CreateWebCart(config.TenantId, branchId, number, name, spec.ContactName ?? string.Empty, spec.ContactPhone ?? string.Empty,
                    spec.ContactEmail, spec.Notes, validUntil, userId, now, spec.CustomerId)
                : PcBuild.CreateWeb(config.TenantId, branchId, number, name, spec.ContactName ?? string.Empty, spec.ContactPhone ?? string.Empty,
                    spec.ContactEmail, spec.Notes, validUntil, userId, now);
            if (!cart && spec.CustomerId is { } customerId)
            {
                build.Rename(name, customerId);
            }
        }
        else
        {
            build = cart
                ? PcBuild.CreateDesktopCart(config.TenantId, branchId, number, name, spec.CustomerId, validUntil, userId, now)
                : new PcBuild(config.TenantId, branchId, number, name, spec.CustomerId, validUntil, userId, now);
            build.SetContact(spec.ContactName, spec.ContactPhone, spec.ContactEmail, spec.Notes);
        }
        if (spec.Buyer is { } buyer)
        {
            build.SetBuyer(buyer.DocumentType, buyer.DocumentNumber, buyer.Complement, buyer.Name);
        }
        foreach (var part in parts)
        {
            build.AddLine(part.Slot, part.Item.Variant.Id, part.Component.Quantity, part.ListPrice);
        }
        if (cart)
        {
            build.QuoteCart(validUntil, today, now, userId);
        }
        else
        {
            // Compatibilidad: se calcula y queda marcada si hay errores, nunca bloquea una reserva (el vendedor la revisa)
            build.Quote(validUntil, today, PcCompatibility.Check(parts.Select(p => p.Component).ToList()), acceptIncompatible: true, now, userId);
        }
        build.Reserve(now, until, today, userId);
        await PcBuildStock.ReserveAsync(db, build, now, until, ct);
        db.Set<PcBuild>().Add(build);
        return build;
    }

    /// <summary>Nombre de la reserva: el que llega o «Armado web de …» / «Reserva de …» con el nombre de contacto (≤ 150).</summary>
    private static string NameOf(ReservationSpec spec)
    {
        var contact = spec.ContactName?.Trim();
        var name = spec.Name?.Trim() is { Length: > 0 } custom ? custom
            : (spec.Kind, spec.Channel) switch
            {
                (PcBuildKind.Cart, _) => string.IsNullOrEmpty(contact) ? "Reserva" : $"Reserva de {contact}",
                (_, PcBuildChannel.Web) => $"Armado web de {contact}",
                _ => string.IsNullOrEmpty(contact) ? "Armado" : $"Armado de {contact}",
            };
        return name.Length > 150 ? name[..150] : name;
    }
}

/// <summary>
/// Reserva web (regla S-03): armado del canal Web cotizado a los precios vigentes (compatibilidad informada, nunca bloquea),
/// reservado por <see cref="StorefrontOptions.ReservationHours"/> horas con una reserva de stock por línea en la sucursal de
/// la tienda, numerado ARM-WEB-000001, todo en UNA transacción con reintento optimista e idempotente por la llave
/// (<c>processed_requests</c>, como el RPC del servidor en la nube). V7: con <c>Kind = Cart</c> crea un carrito
/// (RES-WEB-000001, líneas sin ranura salvo que la web la mande, sin compatibilidad), el vencimiento sale de
/// <c>HoldDays</c> y la reserva guarda los datos para la factura (<see cref="ReservationWriter"/>).
/// </summary>
public sealed class CreateStorefrontReservationHandler(IMinvDbContext db, ICurrentUser user, ITenantContext tenant, IClock clock,
    StorefrontOptions? options = null)
    : IRequestHandler<CreateStorefrontReservationCommand, StorefrontReservationResult>
{
    public async Task<StorefrontReservationResult> Handle(CreateStorefrontReservationCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("La petición no está autenticada.");
        var requestId = IdempotencyId(request.IdempotencyKey);
        var hash = ContentHash(request);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var existing = await db.Set<ProcessedRequest>().AsNoTracking().FirstOrDefaultAsync(r => r.RequestId == requestId, ct);
                if (existing is not null)
                {
                    if (existing.RequestHash != hash)
                    {
                        throw new IdempotencyConflictException(
                            $"La llave de idempotencia ya se usó para otra reserva: use una llave nueva.");
                    }
                    var replayed = JsonSerializer.Deserialize<StorefrontReservationView>(existing.Response, RpcJson.Options)
                                   ?? throw new InvalidOperationException("La respuesta guardada de la reserva no se pudo leer.");
                    // Una respuesta guardada antes de la V7 no trae el tipo: era un armado
                    return new StorefrontReservationResult(replayed.Kind is null ? replayed with { Kind = StorefrontKinds.Build } : replayed, true);
                }
                var now = clock.UtcNow;
                var view = await CreateAsync(request, userId, now, ct);
                db.Set<ProcessedRequest>().Add(new ProcessedRequest(tenant.TenantId, requestId, userId, RpcCatalog.NameOf(request.GetType()), hash,
                    JsonSerializer.Serialize(view, RpcJson.Options), now));
                await db.SaveChangesAsync(ct);
                return new StorefrontReservationResult(view, false);
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                // Otra caja o reserva tomó las mismas existencias, o dos reintentos simultáneos con la misma llave chocaron en
                // el índice único: se vuelve a leer el estado real (y la llave repetida encuentra la respuesta guardada)
                db.ClearTracking();
            }
        }
    }

    private async Task<StorefrontReservationView> CreateAsync(CreateStorefrontReservationCommand request, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var build = await ReservationWriter.CreateAsync(db, clock, options, new ReservationSpec(request.Kind, PcBuildChannel.Web, request.Lines,
            request.Contact.Name, request.Contact.Phone, request.Contact.Email, request.Notes, request.Name, request.HoldDays, request.Buyer), userId, now, ct);
        // V7 · El correo de confirmación se encola aquí, en la misma transacción (regla P-06): hasta entonces, mailQueued = false
        return await StorefrontReservationViews.ViewAsync(db, build, now, ct, mailQueued: false);
    }

    /// <summary>Id determinista de la llave de idempotencia (los 16 primeros bytes de su SHA-256), único por empresa en
    /// <c>processed_requests</c>.</summary>
    public static Guid IdempotencyId(string key) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("storefront-reservation:" + key.Trim())).AsSpan(0, 16));

    /// <summary>SHA-256 del contenido normalizado (líneas ordenadas, contacto y notas) para detectar la misma llave con otro
    /// contenido. V7: también el tipo, los días para recogerla y los datos para la factura; se agregan SOLO cuando vienen, así
    /// una reserva sin los campos nuevos conserva el hash de la V6 (una repetición de antes de actualizar sigue coincidiendo).</summary>
    public static string ContentHash(CreateStorefrontReservationCommand r)
    {
        var text = new StringBuilder();
        foreach (var line in r.Lines.OrderBy(l => l.Sku.Trim().ToUpperInvariant(), StringComparer.Ordinal).ThenBy(l => l.Slot ?? string.Empty, StringComparer.Ordinal))
        {
            text.Append(line.Sku.Trim().ToUpperInvariant()).Append(':').Append(line.Quantity.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(line.Slot?.Trim().ToLowerInvariant()).Append(';');
        }
        text.Append('|').Append(r.Contact.Name.Trim()).Append('|').Append(new string(r.Contact.Phone.Where(char.IsDigit).ToArray()))
            .Append('|').Append(r.Contact.Email?.Trim().ToLowerInvariant()).Append('|').Append(r.Notes?.Trim()).Append('|').Append(r.Name?.Trim());
        if (r.Kind != PcBuildKind.Build)
        {
            text.Append("|kind:").Append(StorefrontKinds.Text(r.Kind));
        }
        if (r.HoldDays is { } days)
        {
            text.Append("|hold:").Append(days.ToString(CultureInfo.InvariantCulture));
        }
        if (r.Buyer is { } buyer)
        {
            text.Append("|buyer:").Append(buyer.DocumentType.ToString(CultureInfo.InvariantCulture)).Append(':').Append(buyer.DocumentNumber?.Trim())
                .Append(':').Append(buyer.Complement?.Trim().ToUpperInvariant()).Append(':').Append(buyer.Name?.Trim());
        }
        return ApiKeyTokens.Hash(text.ToString());
    }
}

public sealed class GetStorefrontReservationHandler(IMinvDbContext db, IClock clock) : IRequestHandler<GetStorefrontReservationQuery, StorefrontReservationView>
{
    public async Task<StorefrontReservationView> Handle(GetStorefrontReservationQuery request, CancellationToken ct)
    {
        var build = await FindAsync(db, request.Number, request.Phone, tracking: false, ct);
        return await StorefrontReservationViews.ViewAsync(db, build, clock.UtcNow, ct);
    }

    /// <summary>La reserva por su número y el teléfono con que se hizo; si no coinciden, «no existe» (no se revela más, regla S-06).</summary>
    internal static async Task<PcBuild> FindAsync(IMinvDbContext db, string number, string phone, bool tracking, CancellationToken ct)
    {
        var code = number.Trim().ToUpperInvariant();
        var query = tracking ? db.Set<PcBuild>() : db.Set<PcBuild>().AsNoTracking();
        var build = await query.Include(b => b.Lines).FirstOrDefaultAsync(b => b.Number == code && b.Channel == PcBuildChannel.Web, ct);
        if (build is null || !build.MatchesPhone(phone))
        {
            throw new NotFoundException($"La reserva {code} no existe o el teléfono no coincide.");
        }
        return build;
    }
}

public sealed class CancelStorefrontReservationHandler(IMinvDbContext db, ICurrentUser user, IClock clock)
    : IRequestHandler<CancelStorefrontReservationCommand, StorefrontReservationView>
{
    public async Task<StorefrontReservationView> Handle(CancelStorefrontReservationCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("La petición no está autenticada.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var build = await GetStorefrontReservationHandler.FindAsync(db, request.Number, request.Phone, tracking: true, ct);
                Guard.That(build.Status == PcBuildStatus.Reserved, "pcbuild.state",
                    $"El armado {build.Number} está {PcBuild.Describe(build.Status)}: la reserva ya no se puede cancelar.");
                var now = clock.UtcNow;
                await PcBuildStock.ReleaseAsync(db, build, ct);
                build.ReleaseReservation("Cancelada por el cliente desde la tienda web", now, userId);
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

/// <summary>Cierra las reservas vencidas de la empresa (armados <c>Reserved</c> con <c>ReservedUntil</c> pasado): el stock
/// vuelve y el armado queda anulado con motivo «Vencida» y su fila en la bitácora.</summary>
public sealed class ExpirePcBuildReservationsHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<ExpirePcBuildReservationsCommand, int>
{
    public async Task<int> Handle(ExpirePcBuildReservationsCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("La petición no está autenticada.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var now = clock.UtcNow;
                var expired = await db.Set<PcBuild>().Include(b => b.Lines)
                    .Where(b => b.Status == PcBuildStatus.Reserved && b.ReservedUntil != null && b.ReservedUntil <= now).ToListAsync(ct);
                foreach (var build in expired)
                {
                    await PcBuildStock.ExpireAsync(db, build, now, ct);
                    build.ReleaseReservation(PcBuild.ExpiredReason, now, userId, expired: true);
                }
                if (expired.Count > 0)
                {
                    await db.SaveChangesAsync(ct);
                }
                return expired.Count;
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }
}
