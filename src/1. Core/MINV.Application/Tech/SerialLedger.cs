using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Inventory;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Inventory;
using MINV.Domain.Service;

namespace MINV.Application.Tech;

/// <summary>
/// V4.2 · Códigos estables de los rechazos de series (regla T-02). Llegan al escritorio y al API con el mismo código
/// (<c>RpcError.Code</c>) para que la interfaz marque la línea o la serie exacta.
/// </summary>
public static class SerialErrorCodes
{
    /// <summary>Producto serializado sin series en la línea.</summary>
    public const string Required = "serial.required";

    /// <summary>Faltan o sobran series respecto de las unidades.</summary>
    public const string Count = "serial.count";

    /// <summary>La misma serie dos veces en el documento, o una serie nueva que ya existe.</summary>
    public const string Duplicate = "serial.duplicate";

    /// <summary>La serie existe pero no está en el estado que la operación necesita (vendida, en RMA, en tránsito…).</summary>
    public const string NotAvailable = "serial.not_available";

    /// <summary>La serie no está registrada para ese producto.</summary>
    public const string NotFound = "serial.not_found";

    /// <summary>La unidad está en otra sucursal.</summary>
    public const string WrongBranch = "serial.wrong_branch";

    /// <summary>La unidad está en otro almacén o en otra posición de la sucursal.</summary>
    public const string WrongLocation = "serial.wrong_location";

    /// <summary>IMEI con otro largo o con el dígito de Luhn incorrecto (lo lanza el dominio).</summary>
    public const string ImeiInvalid = "serial.imei_invalid";

    /// <summary>Se enviaron series para un producto que no lleva serie.</summary>
    public const string NotTracked = "serial.not_tracked";

    /// <summary>Cantidad con decimales en un producto serializado.</summary>
    public const string Quantity = "serial.quantity";

    /// <summary>La serie devuelta no es de esa venta, o la faltante no es de esa transferencia.</summary>
    public const string NotInDocument = "serial.not_in_document";

    /// <summary>La misma serie existe en dos productos: indique el SKU.</summary>
    public const string Ambiguous = "serial.ambiguous";

    /// <summary>La operación de un producto serializado tiene su propio documento (caja, devolución, transferencia, RMA).</summary>
    public const string UseDocument = "serial.use_document";

    /// <summary>La unidad está en un caso RMA abierto.</summary>
    public const string InClaim = "serial.in_claim";
}

/// <summary>Unidades de una misma existencia (posición + lote) que salen juntas con un solo movimiento.</summary>
internal sealed record SerialGroup(StockLevel Level, IReadOnlyList<SerialNumber> Units);

/// <summary>
/// V4.2 · Series de una operación (regla T-02): valida las de cada línea (producto serializado, cantidad entera, una serie
/// por unidad, sin repetir en todo el documento, IMEI válido), encuentra las unidades y comprueba que estén en el estado,
/// la sucursal y la posición que la operación necesita. Los cambios de estado los hacen SOLO los métodos de
/// <see cref="SerialNumber"/> (que dejan su fila de bitácora); el caso de uso registra el movimiento de stock y llama a este
/// servicio en la MISMA transacción. Una instancia por operación (recuerda las series ya usadas en el documento).
/// </summary>
internal sealed class SerialLedger(IMinvDbContext db)
{
    private readonly HashSet<(Guid VariantId, string Serial)> _claimed = new();
    private readonly Dictionary<Guid, SerialKind> _kinds = new();

    public static bool Tracks(Product product) => product.TrackingMode == TrackingMode.Serial;

    /// <summary>Tipo de identificador del producto (serie del fabricante o IMEI; sin perfil técnico: serie).</summary>
    public async Task<SerialKind> KindAsync(Guid productId, CancellationToken ct)
    {
        if (_kinds.TryGetValue(productId, out var known))
        {
            return known;
        }
        var kind = db.Set<ProductTechProfile>().Local.FirstOrDefault(p => p.ProductId == productId)?.SerialKind
                   ?? await db.Set<ProductTechProfile>().Where(p => p.ProductId == productId).Select(p => (SerialKind?)p.SerialKind)
                       .FirstOrDefaultAsync(ct)
                   ?? SerialKind.Serial;
        _kinds[productId] = kind;
        return kind;
    }

    /// <summary>
    /// Series normalizadas de una línea: vacío si el producto no lleva serie (y rechaza series que sobran); si lleva,
    /// exige cantidad entera y exactamente una serie por unidad, sin repetir en todo el documento.
    /// </summary>
    public async Task<IReadOnlyList<string>> ExpectAsync(VariantInfo item, decimal quantity, IReadOnlyList<string>? serials, CancellationToken ct)
    {
        var sku = item.Variant.Sku;
        if (!Tracks(item.Product))
        {
            Guard.That(serials is not { Count: > 0 }, SerialErrorCodes.NotTracked, $"{sku} no lleva serie: quite las series de la línea.");
            return [];
        }
        Guard.That(quantity > 0 && quantity == decimal.Truncate(quantity), SerialErrorCodes.Quantity,
            $"{sku} lleva serie: la cantidad debe ser un número entero de unidades.");
        var units = (int)quantity;
        Guard.That(serials is { Count: > 0 }, SerialErrorCodes.Required,
            $"{sku} lleva serie: indique la serie (o IMEI) de cada una de las {units} unidad(es).");
        var kind = await KindAsync(item.Product.Id, ct);
        var normalized = new List<string>(serials!.Count);
        foreach (var raw in serials)
        {
            var serial = SerialNumber.Normalize(kind, raw);
            Guard.That(!normalized.Contains(serial, StringComparer.Ordinal) && !_claimed.Contains((item.Variant.Id, serial)),
                SerialErrorCodes.Duplicate, $"La serie {serial} de {sku} está repetida en el documento.");
            normalized.Add(serial);
        }
        Guard.That(normalized.Count == units, SerialErrorCodes.Count,
            $"{sku}: son {units} unidad(es) y {normalized.Count} serie(s): indique exactamente una serie por unidad.");
        foreach (var serial in normalized)
        {
            _claimed.Add((item.Variant.Id, serial));
        }
        return normalized;
    }

    /// <summary>Unidades registradas de una variante por su serie (las del contexto primero).</summary>
    public async Task<Dictionary<string, SerialNumber>> FindAsync(Guid variantId, IReadOnlyCollection<string> serials, CancellationToken ct)
    {
        var found = db.Set<SerialNumber>().Local.Where(s => s.VariantId == variantId && serials.Contains(s.Serial))
            .ToDictionary(s => s.Serial, StringComparer.Ordinal);
        var missing = serials.Where(s => !found.ContainsKey(s)).ToList();
        if (missing.Count > 0)
        {
            foreach (var unit in await db.Set<SerialNumber>().Where(s => s.VariantId == variantId && missing.Contains(s.Serial)).ToListAsync(ct))
            {
                found[unit.Serial] = unit;
            }
        }
        return found;
    }

    /// <summary>
    /// Unidades EN STOCK que salen (venta, transferencia, reposición, ajuste negativo): cada serie existe, está en stock, en
    /// la sucursal <paramref name="branchId"/> y, si se indica, en una posición de <paramref name="bins"/>. Agrupadas por
    /// existencia (una salida por posición y lote).
    /// </summary>
    public async Task<IReadOnlyList<SerialGroup>> OnHandAsync(VariantInfo item, IReadOnlyList<string> serials, Guid branchId, IQueryable<Guid>? bins,
        CancellationToken ct)
    {
        var sku = item.Variant.Sku;
        var units = await FindAsync(item.Variant.Id, serials, ct);
        var allowedBins = bins is null ? null : (await bins.ToListAsync(ct)).ToHashSet();
        var groups = new Dictionary<Guid, (StockLevel Level, List<SerialNumber> Units)>();
        foreach (var serial in serials)
        {
            if (!units.TryGetValue(serial, out var unit))
            {
                throw new DomainException(SerialErrorCodes.NotFound, $"La serie {serial} no está registrada para {sku}.");
            }
            Guard.That(unit.IsAvailable, SerialErrorCodes.NotAvailable,
                $"La serie {serial} de {sku} no está disponible: está {SerialNumber.Describe(unit.Status)}.");
            var level = await LevelAsync(unit.StockLevelId!.Value, ct);
            Guard.That(level is not null && level.BranchId == branchId, SerialErrorCodes.WrongBranch,
                $"La serie {serial} de {sku} está en otra sucursal.");
            Guard.That(allowedBins is null || allowedBins.Contains(level!.BinId), SerialErrorCodes.WrongLocation,
                $"La serie {serial} de {sku} está en otro almacén de la sucursal.");
            if (!groups.TryGetValue(level!.Id, out var group))
            {
                group = (level, new List<SerialNumber>());
                groups[level.Id] = group;
            }
            group.Units.Add(unit);
        }
        return groups.Values.Select(g => new SerialGroup(g.Level, g.Units)).ToList();
    }

    /// <summary>Unidades en stock que salen de UNA existencia dada (ajuste negativo, devolución al proveedor por movimiento).</summary>
    public async Task<IReadOnlyList<SerialNumber>> OnHandAtAsync(VariantInfo item, IReadOnlyList<string> serials, StockLevel level, string binCode,
        CancellationToken ct)
    {
        var groups = await OnHandAsync(item, serials, level.BranchId, null, ct);
        foreach (var group in groups.Where(g => g.Level.Id != level.Id))
        {
            throw new DomainException(SerialErrorCodes.WrongLocation,
                $"La serie {group.Units[0].Serial} de {item.Variant.Sku} no está en la posición {binCode} (con ese lote).");
        }
        return groups.SelectMany(g => g.Units).ToList();
    }

    /// <summary>
    /// Unidades que ENTRAN al stock en <paramref name="level"/> (recepción de compra, saldo inicial, ajuste positivo): series
    /// nuevas o, con <paramref name="allowReentry"/>, unidades que vuelven (devueltas por el cliente, devueltas al proveedor
    /// o en garantía sin un caso abierto) y se reponen. Cualquier otra serie existente se rechaza por repetida.
    /// </summary>
    public async Task<IReadOnlyList<SerialNumber>> EnterAsync(VariantInfo item, IReadOnlyList<string> serials, Batch batch, StockLevel level,
        SerialContext context, bool allowReentry, CancellationToken ct)
    {
        var kind = await KindAsync(item.Product.Id, ct);
        var existing = await FindAsync(item.Variant.Id, serials, ct);
        var result = new List<SerialNumber>(serials.Count);
        foreach (var serial in serials)
        {
            if (existing.TryGetValue(serial, out var unit))
            {
                Guard.That(allowReentry && unit.Status is SerialNumberStatus.Returned or SerialNumberStatus.InRma or SerialNumberStatus.ReturnedToSupplier,
                    SerialErrorCodes.Duplicate, $"La serie {serial} de {item.Variant.Sku} ya existe (está {SerialNumber.Describe(unit.Status)}).");
                await EnsureNoOpenClaimAsync(unit, ct);
                unit.Restock(level, batch, context);
            }
            else
            {
                unit = SerialNumber.Receive(kind, serial, batch, level, context);
                db.Set<SerialNumber>().Add(unit);
            }
            result.Add(unit);
        }
        return result;
    }

    /// <summary>Una unidad en un caso RMA abierto (visible en las sucursales de la sesión) no se mueve fuera del caso; un caso
    /// ya reemplazado deja la unidad defectuosa libre para devolverla al proveedor o darla de baja.</summary>
    public async Task EnsureNoOpenClaimAsync(SerialNumber unit, CancellationToken ct)
    {
        if (unit.Status != SerialNumberStatus.InRma)
        {
            return;
        }
        var claim = await db.Set<WarrantyClaim>().Where(c => c.SerialNumberId == unit.Id && c.Status != WarrantyClaimStatus.Delivered
                                                             && c.Status != WarrantyClaimStatus.Replaced)
            .Select(c => c.Number).FirstOrDefaultAsync(ct);
        Guard.That(claim is null, SerialErrorCodes.InClaim, $"La serie {unit.Serial} está en el caso {claim}: resuélvala desde el caso RMA.");
    }

    /// <summary>Existencia de una unidad (null si es de una sucursal que la sesión no ve).</summary>
    public async Task<StockLevel?> LevelAsync(Guid stockLevelId, CancellationToken ct) =>
        db.Set<StockLevel>().Local.FirstOrDefault(l => l.Id == stockLevelId)
        ?? await db.Set<StockLevel>().FirstOrDefaultAsync(l => l.Id == stockLevelId, ct);

    /// <summary>Lote de una existencia.</summary>
    public async Task<Batch> BatchAsync(Guid batchId, CancellationToken ct) =>
        db.Set<Batch>().Local.FirstOrDefault(b => b.Id == batchId) ?? await db.Set<Batch>().FirstAsync(b => b.Id == batchId, ct);

    /// <summary>Una unidad por su serie (normalizada con el tipo del producto si se indica el SKU; si no, como serie o como
    /// IMEI). Ambigua si la misma serie existe en dos productos.</summary>
    public static async Task<SerialNumber> ResolveAsync(IMinvDbContext db, string serial, string? sku, CancellationToken ct)
    {
        var raw = (serial ?? string.Empty).Trim();
        Guard.That(raw.Length > 0, SerialErrorCodes.Required, "Indique la serie o el IMEI.");
        var candidates = new[] { raw.ToUpperInvariant(), Imei.Normalize(raw) }.Distinct(StringComparer.Ordinal).ToList();
        var query = db.Set<SerialNumber>().Where(s => candidates.Contains(s.Serial));
        if (!string.IsNullOrWhiteSpace(sku))
        {
            var variant = await new InventoryLookups(db).VariantBySkuAsync(sku, ct);
            query = query.Where(s => s.VariantId == variant.Variant.Id);
        }
        var units = await query.OrderBy(s => s.Id).Take(3).ToListAsync(ct);   // orden determinista (EF 10102): solo se cuentan 0, 1 o varias
        return units.Count switch
        {
            0 => throw new Common.NotFoundException($"La serie {raw} no está registrada{(sku is null ? string.Empty : " para " + sku.Trim().ToUpperInvariant())}."),
            1 => units[0],
            _ => throw new DomainException(SerialErrorCodes.Ambiguous, $"La serie {raw} existe en varios productos: indique el SKU."),
        };
    }
}
