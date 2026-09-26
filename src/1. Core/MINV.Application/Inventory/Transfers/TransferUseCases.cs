using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Purchasing;
using MINV.Application.Tech;
using MINV.Domain.Accounting;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Domain.Warehousing;

namespace MINV.Application.Inventory.Transfers;

// ================================================================================================ consultas
public sealed record TransferRow(Guid Id, string Number, string FromBranch, string FromWarehouse, string ToBranch, string ToWarehouse,
    TransferStatus Status, string StatusLabel, DateTimeOffset RequestedAt, DateTimeOffset? DispatchedAt, DateTimeOffset? ReceivedAt, int Lines,
    decimal Quantity, decimal Value, decimal Shortage, string? Notes, bool CanDispatch, bool CanReceive, bool CanCancel, string FromBranchCode = "",
    string ToBranchCode = "");

public sealed record TransferLineRow(Guid LineId, string Sku, string Name, string Unit, decimal Quantity, decimal? UnitCost, decimal Received,
    decimal Shortage, string? ShortageReason, IReadOnlyList<string> Lots, IReadOnlyList<string>? Serials = null);

public sealed record TransferHistoryRow(DateTimeOffset OccurredAt, string StatusLabel, string User, string Detail);

public sealed record TransferDetail(TransferRow Header, IReadOnlyList<TransferLineRow> Lines, IReadOnlyList<TransferHistoryRow> History);

/// <summary>
/// V4 · Transferencias visibles (las que salen de o llegan a una sucursal del alcance). Indica qué puede hacer la sesión
/// con cada una: el ORIGEN despacha o anula; el DESTINO recibe (la gerencia global, ambas).
/// </summary>
[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetTransfersQuery(TransferStatus? Status = null, int Take = 300) : IRequest<IReadOnlyList<TransferRow>>;

public sealed class GetTransfersHandler(IMinvDbContext db) : IRequestHandler<GetTransfersQuery, IReadOnlyList<TransferRow>>
{
    public async Task<IReadOnlyList<TransferRow>> Handle(GetTransfersQuery request, CancellationToken ct)
    {
        var query = db.Set<StockTransfer>().AsNoTracking();
        if (request.Status is { } status)
        {
            query = query.Where(t => t.Status == status);
        }
        var transfers = await query.OrderByDescending(t => t.RequestedAt).Take(Math.Clamp(request.Take, 1, 2000)).ToListAsync(ct);
        return await TransferViews.RowsAsync(db, transfers, ct);
    }
}

[RequiresPermission(PermissionCodes.StockView)]
public sealed record GetTransferQuery(Guid Id) : IRequest<TransferDetail>;

public sealed class GetTransferHandler(IMinvDbContext db) : IRequestHandler<GetTransferQuery, TransferDetail>
{
    public async Task<TransferDetail> Handle(GetTransferQuery request, CancellationToken ct)
    {
        var transfer = await db.Set<StockTransfer>().AsNoTracking()
                           .Include(t => t.Lines).ThenInclude(l => l.Discrepancies)
                           .Include(t => t.Lines).ThenInclude(l => l.Batches)
                           .Include(t => t.History)
                           .FirstOrDefaultAsync(t => t.Id == request.Id, ct)
                       ?? throw new NotFoundException("La transferencia no existe o no es de sus sucursales.");
        var header = (await TransferViews.RowsAsync(db, [transfer], ct))[0];
        var variantIds = transfer.Lines.Select(l => l.VariantId).ToList();
        var items = await (from v in db.Set<ProductVariant>()
                           join p in db.Set<Product>() on v.ProductId equals p.Id
                           join u in db.Set<UnitOfMeasure>() on p.BaseUnitId equals u.Id
                           where variantIds.Contains(v.Id)
                           select new { v.Id, v.Sku, p.Name, Unit = u.Code }).ToDictionaryAsync(x => x.Id, ct);
        var batchIds = transfer.Lines.SelectMany(l => l.Batches).Select(b => b.BatchId).Distinct().ToList();
        var lots = await db.Set<Batch>().Where(b => batchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.LotNumber, ct);
        var lines = transfer.Lines.OrderBy(l => items[l.VariantId].Sku).Select(l => new TransferLineRow(l.Id, items[l.VariantId].Sku, items[l.VariantId].Name,
            items[l.VariantId].Unit, l.Quantity, l.UnitCost, transfer.Status == TransferStatus.Received ? l.ReceivedQuantity : 0, l.Shortage,
            l.Discrepancies.Select(d => d.Reason).FirstOrDefault(),
            l.Batches.Select(b => $"{lots.GetValueOrDefault(b.BatchId, "?")} × {b.Quantity:0.######}").ToList())).ToList();
        var userIds = transfer.History.Select(h => h.UserId).Distinct().ToList();
        var users = await db.Set<User>().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var history = transfer.History.OrderBy(h => h.OccurredAt).Select(h => new TransferHistoryRow(h.OccurredAt, TransferStatuses.Label(h.Status),
            users.GetValueOrDefault(h.UserId, "?"), h.Detail)).ToList();
        // V4.2 · Series que viajan en cada línea
        var serials = await TransferRules.SerialsAsync(db, transfer.Lines.Select(l => l.Id).ToList(), ct);
        lines = lines.Select(l => serials[l.LineId].Any() ? l with { Serials = serials[l.LineId].Select(s => s.Serial).Order(StringComparer.Ordinal).ToList() } : l)
            .ToList();
        return new TransferDetail(header, lines, history);
    }
}

internal static class TransferViews
{
    public static async Task<IReadOnlyList<TransferRow>> RowsAsync(IMinvDbContext db, IReadOnlyList<StockTransfer> transfers, CancellationToken ct)
    {
        var ids = transfers.Select(t => t.Id).ToList();
        var totals = await (from l in db.Set<StockTransferLine>()
                            where ids.Contains(l.StockTransferId)
                            group l by l.StockTransferId into g
                            select new { g.Key, Lines = g.Count(), Quantity = g.Sum(x => x.Quantity), Value = g.Sum(x => x.Quantity * (x.UnitCost ?? 0)) })
            .ToDictionaryAsync(x => x.Key, ct);
        var shortages = await (from d in db.Set<StockTransferDiscrepancy>()
                               join l in db.Set<StockTransferLine>() on d.TransferLineId equals l.Id
                               where ids.Contains(l.StockTransferId)
                               group d.Quantity by l.StockTransferId into g
                               select new { g.Key, Total = g.Sum() }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        var branches = await db.Set<Branch>().ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var codes = await db.Set<Branch>().ToDictionaryAsync(b => b.Id, b => b.Code, ct);
        var warehouses = await db.Set<Warehouse>().ToDictionaryAsync(w => w.Id, w => w.Code, ct);
        var scope = db.Branches;
        return transfers.Select(t =>
        {
            totals.TryGetValue(t.Id, out var sum);
            return new TransferRow(t.Id, t.Number, branches.GetValueOrDefault(t.FromBranchId, "?"), warehouses.GetValueOrDefault(t.FromWarehouseId, "?"),
                branches.GetValueOrDefault(t.ToBranchId, "?"), warehouses.GetValueOrDefault(t.ToWarehouseId, "?"), t.Status, TransferStatuses.Label(t.Status),
                t.RequestedAt, t.DispatchedAt, t.ReceivedAt, sum?.Lines ?? 0, sum?.Quantity ?? 0, JournalPoster.Money(sum?.Value ?? 0),
                shortages.GetValueOrDefault(t.Id), t.Notes,
                t.Status == TransferStatus.Pending && scope.Allows(t.FromBranchId),
                t.Status == TransferStatus.Dispatched && scope.Allows(t.ToBranchId),
                t.Status == TransferStatus.Pending && scope.Allows(t.FromBranchId),
                codes.GetValueOrDefault(t.FromBranchId, "?"), codes.GetValueOrDefault(t.ToBranchId, "?"));
        }).ToList();
    }
}

// ================================================================================================ comandos
/// <summary>Línea de una transferencia. V4.2 · <see cref="Serials"/>: series (o IMEI) de las unidades que viajan de un
/// producto serializado, una por unidad, en stock en el almacén de origen (regla T-02).</summary>
public sealed record TransferLineInput(string Sku, decimal Quantity, IReadOnlyList<string>? Serials = null);

public sealed record TransferRef(Guid Id, string Number, string Message);

/// <summary>V4 · Solicita una transferencia desde un almacén de una sucursal del alcance (por defecto, el de la sucursal
/// activa) hacia el almacén de otra sucursal. Queda PENDIENTE: todavía no mueve stock.</summary>
[RequiresModule(LicenseModuleCodes.MultiBranch)]
[RequiresPermission(PermissionCodes.TransfersManage)]
public sealed record CreateTransferCommand(string ToWarehouseCode, IReadOnlyList<TransferLineInput> Lines, string? Notes = null,
    string? FromWarehouseCode = null) : IRequest<TransferRef>, IAuditableRequest
{
    public object AuditDetails => new { FromWarehouseCode, ToWarehouseCode, Lines, Notes };
}

public sealed class CreateTransferValidator : AbstractValidator<CreateTransferCommand>
{
    public CreateTransferValidator()
    {
        RuleFor(x => x.ToWarehouseCode).NotEmpty().WithMessage("Elija el almacén de destino.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Agregue al menos un producto.");
        RuleFor(x => x.Lines).Must(l => l.Select(x => x.Sku.Trim().ToUpperInvariant()).Distinct().Count() == l.Count)
            .WithMessage("Cada producto va una sola vez en la transferencia.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.Sku).NotEmpty().WithMessage("Indique el producto de cada línea.");
            l.RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Cada cantidad debe ser mayor que 0.");
        });
        RuleFor(x => x.Notes).MaximumLength(250);
    }
}

public sealed class CreateTransferHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<CreateTransferCommand, TransferRef>
{
    public async Task<TransferRef> Handle(CreateTransferCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para transferir.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var lookups = new InventoryLookups(db);
                var config = await lookups.ConfigAsync(ct);
                var from = await lookups.WarehouseAsync(request.FromWarehouseCode, config, ct);
                TransferRules.EnsureAllowed(db, from.BranchId, "despachar desde");
                var to = await lookups.WarehouseByCodeAsync(request.ToWarehouseCode, ct);
                Guard.That(to.IsActive, "warehouse.inactive", $"El almacén {to.Code} está inactivo.");
                Guard.That(await db.Set<Branch>().AnyAsync(b => b.Id == to.BranchId && b.IsActive, ct), "branch.inactive",
                    "La sucursal de destino está inactiva.");
                var number = await Documents.NextForBranchAsync<StockTransfer>(db, t => t.Number, "TR", from.BranchId, ct);
                var transfer = new StockTransfer(from.TenantId, number, from.BranchId, from.Id, to.BranchId, to.Id, userId, clock.UtcNow,
                    request.Notes?.Trim());
                var ledger = new SerialLedger(db);
                var bins = lookups.BinIdsOf(from.Id);
                foreach (var input in request.Lines)
                {
                    var item = await lookups.VariantBySkuAsync(input.Sku, ct);
                    Guard.That(item.Product.IsActive && item.Variant.IsActive, "product.inactive", $"El producto {item.Variant.Sku} está inactivo.");
                    Quantities.EnsureAllowed(input.Quantity, item.Unit.AllowsDecimals, item.Unit.UnitCode);
                    var line = transfer.AddLine(item.Variant.Id, input.Quantity);
                    // V4.2 · Las series que viajarán: en stock en el almacén de origen (se vuelven a verificar al despachar)
                    var serials = await ledger.ExpectAsync(item, input.Quantity, input.Serials, ct);
                    var units = serials.Count == 0 ? [] : (await ledger.OnHandAsync(item, serials, from.BranchId, bins, ct)).SelectMany(g => g.Units);
                    foreach (var unit in units)
                    {
                        db.Set<StockTransferLineSerial>().Add(new StockTransferLineSerial(from.TenantId, from.BranchId, to.BranchId, line.Id, unit.Id));
                    }
                }
                db.Set<StockTransfer>().Add(transfer);
                await db.SaveChangesAsync(ct);
                var products = transfer.Lines.Count == 1 ? "1 producto" : $"{transfer.Lines.Count} productos";
                return new TransferRef(transfer.Id, number, $"✔ Transferencia {number} solicitada de {from.Code} a {to.Code} ({products}).");
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }
}

/// <summary>
/// V4 · Despacha una transferencia pendiente (lo hace el ORIGEN) en UNA transacción: salidas TRASLADO (SALIDA) de las
/// posiciones con disponible (primero los lotes que vencen antes), manifiesto por lote, costo promedio del origen y el
/// asiento del origen (Mercadería enviada a sucursales / Inventario). La mercadería queda EN TRÁNSITO. Si otra sesión
/// vendió o despachó el mismo stock, o despachó/anuló la misma transferencia, la más lenta reintenta con el estado real.
/// </summary>
[RequiresModule(LicenseModuleCodes.MultiBranch)]
[RequiresPermission(PermissionCodes.TransfersManage)]
public sealed record DispatchTransferCommand(Guid Id) : IRequest<TransferRef>, IAuditableRequest
{
    public object AuditDetails => new { Id };
}

public sealed class DispatchTransferHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<DispatchTransferCommand, TransferRef>
{
    public async Task<TransferRef> Handle(DispatchTransferCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para despachar.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var transaction = await db.BeginTransactionAsync(ct);
                var result = await DispatchAsync(request.Id, userId, ct);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }

    private async Task<TransferRef> DispatchAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var transfer = await TransferRules.LoadAsync(db, id, ct);
        TransferRules.EnsureAllowed(db, transfer.FromBranchId, "despachar");
        var lookups = new InventoryLookups(db);
        var config = await lookups.ConfigAsync(ct);
        var today = clock.TodayIn(config.TimeZoneId);
        var now = clock.UtcNow;
        var outType = await lookups.MovementTypeAsync(MovementTypeCodes.TransferOut, ct);
        var bins = lookups.BinIdsOf(transfer.FromWarehouseId);
        var toBranch = await db.Set<Branch>().Where(b => b.Id == transfer.ToBranchId).Select(b => b.Name).FirstAsync(ct);
        var dispatches = new List<LineDispatch>();
        var value = 0m;
        var travelling = await TransferRules.SerialsAsync(db, transfer.Lines.Select(l => l.Id).ToList(), ct);
        var ledger = new SerialLedger(db);
        foreach (var line in transfer.Lines)
        {
            var item = await TransferRules.ItemAsync(db, line.VariantId, ct);
            var unitCost = await AverageCosts.CurrentAsync(db, line.VariantId, transfer.FromWarehouseId, ct);
            if (SerialLedger.Tracks(item.Product))
            {
                // V4.2 · Salen las series de la línea (siguen en stock en el origen) y quedan EN TRÁNSITO
                Guard.That(travelling[line.Id].Any(), SerialErrorCodes.Required,
                    $"{item.Variant.Sku} lleva serie y la transferencia {transfer.Number} no indica sus series: anúlela y solicítela de nuevo con las series.");
                var serials = await ledger.ExpectAsync(item, line.Quantity, travelling[line.Id].Select(s => s.Serial).ToList(), ct);
                var serialContext = new SerialContext(transfer.FromBranchId, userId, now, transfer.Number, $"Transferencia {transfer.Number} → {toBranch}");
                foreach (var group in await ledger.OnHandAsync(item, serials, transfer.FromBranchId, bins, ct))
                {
                    var context = new MovementContext(userId, today, now, transfer.Number, $"Transferencia {transfer.Number} → {toBranch}",
                        CorrelationId: transfer.Id);
                    var movement = group.Level.Register(outType, group.Units.Count, item.Unit, context);
                    db.Set<StockMovement>().Add(movement);
                    dispatches.Add(new LineDispatch(line.Id, new TransferMovement(movement.Id, transfer.FromBranchId, group.Level.BatchId, group.Units.Count),
                        unitCost));
                    foreach (var unit in group.Units)
                    {
                        unit.TransferOut(group.Level, serialContext);
                    }
                }
                value += JournalPoster.Money(line.Quantity * unitCost);
                continue;
            }
            var levels = await (from l in db.Set<StockLevel>()
                                join b in db.Set<Batch>() on l.BatchId equals b.Id
                                where b.VariantId == line.VariantId && bins.Contains(l.BinId) && l.QuantityOnHand - l.QuantityReserved > 0
                                orderby b.ExpiresOn == null, b.ExpiresOn, l.QuantityOnHand - l.QuantityReserved descending
                                select l).ToListAsync(ct);
            var remaining = line.Quantity;
            foreach (var level in levels)
            {
                if (remaining <= 0)
                {
                    break;
                }
                var take = Math.Min(level.Available, remaining);
                var context = new MovementContext(userId, today, now, transfer.Number, $"Transferencia {transfer.Number} → {toBranch}",
                    CorrelationId: transfer.Id);
                var movement = level.Register(outType, take, item.Unit, context);
                db.Set<StockMovement>().Add(movement);
                dispatches.Add(new LineDispatch(line.Id, new TransferMovement(movement.Id, transfer.FromBranchId, level.BatchId, take), unitCost));
                remaining = Quantities.Round6(remaining - take);
            }
            if (remaining > 0)
            {
                throw new DomainException("transfer.insufficient_stock",
                    $"No hay stock suficiente de {item.Variant.Sku} para despachar {line.Quantity}: faltan {remaining} {item.Unit.UnitCode}.");
            }
            value += JournalPoster.Money(line.Quantity * unitCost);
        }
        transfer.Dispatch(dispatches, userId, now);
        var message = $"✔ Transferencia {transfer.Number} despachada: la mercadería está en tránsito hacia {toBranch}.";
        if (value > 0)
        {
            var entry = await JournalPoster.PostAsync(db, transfer.TenantId, transfer.FromBranchId, userId, today,
                $"Transferencia {transfer.Number} despachada a {toBranch}",
                [new JournalLineSpec(AccountCodes.InterBranchSent, value, 0), new JournalLineSpec(AccountCodes.Inventory, 0, value)], now, transfer.Id, ct);
            message += $" Asiento {entry.Number} por {value:0.00}.";
        }
        return new TransferRef(transfer.Id, transfer.Number, message);
    }
}

/// <summary>Lo recibido de una línea. V4.2 · En un producto serializado, un faltante DEBE indicar qué series no llegaron
/// (<see cref="MissingSerials"/>): esas se dan de baja como faltante en tránsito y las demás entran al stock del destino.</summary>
public sealed record TransferReceiptInput(string Sku, decimal ReceivedQuantity, string? ShortageReason = null, IReadOnlyList<string>? MissingSerials = null);

/// <summary>
/// V4 · Recibe una transferencia despachada (lo hace el DESTINO) en UNA transacción: entradas TRASLADO (ENTRADA) con los
/// mismos lotes del manifiesto, costo promedio del destino, faltantes como registros compensatorios con motivo y el
/// asiento del destino (Inventario + Mermas / Mercadería recibida de sucursales). Sin líneas = todo llegó completo.
/// </summary>
[RequiresModule(LicenseModuleCodes.MultiBranch)]
[RequiresPermission(PermissionCodes.TransfersManage)]
public sealed record ReceiveTransferCommand(Guid Id, IReadOnlyList<TransferReceiptInput>? Lines = null) : IRequest<TransferRef>, IAuditableRequest
{
    public object AuditDetails => new { Id, Lines };
}

public sealed class ReceiveTransferValidator : AbstractValidator<ReceiveTransferCommand>
{
    public ReceiveTransferValidator()
    {
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.ReceivedQuantity).GreaterThanOrEqualTo(0).WithMessage("Lo recibido no puede ser negativo.");
            l.RuleFor(x => x.ShortageReason).MaximumLength(200);
        });
    }
}

public sealed class ReceiveTransferHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<ReceiveTransferCommand, TransferRef>
{
    public async Task<TransferRef> Handle(ReceiveTransferCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para recibir.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var transaction = await db.BeginTransactionAsync(ct);
                var result = await ReceiveAsync(request, userId, ct);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
                db.ClearTracking();
            }
        }
    }

    private async Task<TransferRef> ReceiveAsync(ReceiveTransferCommand request, Guid userId, CancellationToken ct)
    {
        var transfer = await TransferRules.LoadAsync(db, request.Id, ct);
        TransferRules.EnsureAllowed(db, transfer.ToBranchId, "recibir en");
        var lookups = new InventoryLookups(db);
        var config = await lookups.ConfigAsync(ct);
        var today = clock.TodayIn(config.TimeZoneId);
        var now = clock.UtcNow;
        var inType = await lookups.MovementTypeAsync(MovementTypeCodes.TransferIn, ct);
        var bins = lookups.BinIdsOf(transfer.ToWarehouseId);
        var fromBranch = await db.Set<Branch>().Where(b => b.Id == transfer.FromBranchId).Select(b => b.Name).FirstAsync(ct);
        var inputs = (request.Lines ?? []).ToDictionary(l => l.Sku.Trim().ToUpperInvariant(), StringComparer.Ordinal);
        var receipts = new List<LineReceipt>();
        decimal received = 0m, shortage = 0m, total = 0m;
        var travelling = await TransferRules.SerialsAsync(db, transfer.Lines.Select(l => l.Id).ToList(), ct);
        var ledger = new SerialLedger(db);
        foreach (var line in transfer.Lines)
        {
            var item = await TransferRules.ItemAsync(db, line.VariantId, ct);
            var input = inputs.GetValueOrDefault(item.Variant.Sku);
            inputs.Remove(item.Variant.Sku);
            var quantity = input?.ReceivedQuantity ?? line.Quantity;
            Guard.That(quantity <= line.Quantity, "transfer.over_receipt",
                $"Se recibirían {quantity} de {item.Variant.Sku} y solo se despacharon {line.Quantity}: no se puede recibir de más.");
            if (quantity > 0)
            {
                Quantities.EnsureAllowed(quantity, item.Unit.AllowsDecimals, item.Unit.UnitCode);
            }
            var unitCost = line.UnitCost ?? 0;
            var onHandBefore = await AverageCosts.OnHandAsync(db, line.VariantId, bins, ct);
            var averageBefore = await AverageCosts.CurrentAsync(db, line.VariantId, transfer.ToWarehouseId, ct);
            var binId = await Receipts.BinForAsync(db, line.VariantId, transfer.ToWarehouseId, bins, ct);
            var movements = new List<TransferMovement>();

            // Lo que entra por lote: en el orden del manifiesto o, si el producto lleva serie, el lote de cada serie recibida
            var takes = new List<(Guid BatchId, decimal Quantity, IReadOnlyList<SerialNumber> Units)>();
            var units = travelling[line.Id].ToList();
            if (units.Count > 0)
            {
                var missing = TransferRules.Missing(item, line, units, quantity, input?.MissingSerials);
                var lost = new SerialContext(transfer.ToBranchId, userId, now, transfer.Number,
                    $"Faltante en la transferencia {transfer.Number} de {fromBranch}: {input?.ShortageReason ?? "sin motivo"}");
                foreach (var unit in missing)
                {
                    unit.Scrap(null, lost);
                }
                takes.AddRange(units.Except(missing).GroupBy(u => u.BatchId)
                    .Select(g => (g.Key, (decimal)g.Count(), (IReadOnlyList<SerialNumber>)g.ToList())));
            }
            else
            {
                var pending = quantity;
                foreach (var shipped in line.Batches.OrderBy(b => b.BatchId))
                {
                    if (pending <= 0)
                    {
                        break;
                    }
                    var take = Math.Min(shipped.Quantity, pending);
                    takes.Add((shipped.BatchId, take, []));
                    pending = Quantities.Round6(pending - take);
                }
            }
            foreach (var (batchId, take, arrived) in takes)
            {
                var (level, _) = await lookups.StockLevelAsync(transfer.TenantId, binId, batchId, ct);
                var context = new MovementContext(userId, today, now, transfer.Number, $"Transferencia {transfer.Number} ← {fromBranch}",
                    CorrelationId: transfer.Id);
                var movement = level.Register(inType, take, item.Unit, context);
                db.Set<StockMovement>().Add(movement);
                movements.Add(new TransferMovement(movement.Id, transfer.ToBranchId, batchId, take));
                if (arrived.Count > 0)
                {
                    var batch = await ledger.BatchAsync(batchId, ct);
                    var came = new SerialContext(transfer.ToBranchId, userId, now, transfer.Number, $"Transferencia {transfer.Number} ← {fromBranch}");
                    foreach (var unit in arrived)
                    {
                        unit.TransferIn(level, batch, came);
                    }
                }
            }
            receipts.Add(new LineReceipt(line.Id, movements, input?.ShortageReason));
            var lineValue = JournalPoster.Money(line.Quantity * unitCost);
            var receivedValue = JournalPoster.Money(Math.Min(quantity, line.Quantity) * unitCost);
            total += lineValue;
            received += receivedValue;
            shortage += lineValue - receivedValue;
            if (movements.Count > 0)
            {
                var average = AverageCosts.Weighted(onHandBefore, averageBefore, movements.Sum(m => m.Quantity), unitCost);
                await AverageCosts.RecordAsync(db, transfer.TenantId, transfer.ToBranchId, line.VariantId, transfer.ToWarehouseId, now, average,
                    movements[0].Id, ct);
            }
        }
        if (inputs.Count > 0)
        {
            throw new DomainException("transfer.unknown_line", $"{string.Join(", ", inputs.Keys)} no está en la transferencia {transfer.Number}.");
        }
        var discrepancies = transfer.Receive(receipts, userId, now);
        var message = discrepancies.Count == 0
            ? $"✔ Transferencia {transfer.Number} recibida completa."
            : $"✔ Transferencia {transfer.Number} recibida con {discrepancies.Count} faltante(s) registrados como merma en tránsito.";
        if (total > 0)
        {
            var entry = await JournalPoster.PostAsync(db, transfer.TenantId, transfer.ToBranchId, userId, today,
                $"Transferencia {transfer.Number} recibida de {fromBranch}",
            [
                new JournalLineSpec(AccountCodes.Inventory, received, 0),
                new JournalLineSpec(AccountCodes.InventoryShrinkage, shortage, 0, "Faltante en tránsito"),
                new JournalLineSpec(AccountCodes.InterBranchReceived, 0, total),
            ], now, transfer.Id, ct);
            message += $" Asiento {entry.Number}.";
        }
        return new TransferRef(transfer.Id, transfer.Number, message);
    }
}

/// <summary>V4 · Anula una transferencia pendiente (lo hace el ORIGEN; no movió stock).</summary>
[RequiresModule(LicenseModuleCodes.MultiBranch)]
[RequiresPermission(PermissionCodes.TransfersManage)]
public sealed record CancelTransferCommand(Guid Id, string Reason) : IRequest<TransferRef>, IAuditableRequest
{
    public object AuditDetails => new { Id, Reason };
}

public sealed class CancelTransferValidator : AbstractValidator<CancelTransferCommand>
{
    public CancelTransferValidator() => RuleFor(x => x.Reason).NotEmpty().WithMessage("Indique el motivo de la anulación.").MaximumLength(200);
}

public sealed class CancelTransferHandler(IMinvDbContext db, ICurrentUser user, IClock clock) : IRequestHandler<CancelTransferCommand, TransferRef>
{
    public async Task<TransferRef> Handle(CancelTransferCommand request, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para anular.");
        var transfer = await TransferRules.LoadAsync(db, request.Id, ct);
        TransferRules.EnsureAllowed(db, transfer.FromBranchId, "anular desde");
        transfer.Cancel(userId, clock.UtcNow, request.Reason);
        await db.SaveChangesAsync(ct);
        return new TransferRef(transfer.Id, transfer.Number, $"✔ Transferencia {transfer.Number} anulada.");
    }
}

internal static class TransferRules
{
    public static async Task<StockTransfer> LoadAsync(IMinvDbContext db, Guid id, CancellationToken ct) =>
        await db.Set<StockTransfer>()
            .Include(t => t.Lines).ThenInclude(l => l.Batches)
            .Include(t => t.Lines).ThenInclude(l => l.Discrepancies)
            .Include(t => t.Lines).ThenInclude(l => l.Movements)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
        ?? throw new NotFoundException("La transferencia no existe o no es de sus sucursales.");

    /// <summary>El lado que actúa debe ser una sucursal del alcance: el origen crea, despacha y anula; el destino recibe.</summary>
    public static void EnsureAllowed(IMinvDbContext db, Guid branchId, string action)
    {
        if (!db.Branches.Allows(branchId))
        {
            throw new AccessDeniedException($"No puede {action} esa sucursal: no está entre las suyas.");
        }
    }

    /// <summary>V4.2 · Unidades que viajan en cada línea (las ven origen y destino).</summary>
    public static async Task<ILookup<Guid, SerialNumber>> SerialsAsync(IMinvDbContext db, IReadOnlyCollection<Guid> lineIds, CancellationToken ct) =>
        (await (from x in db.Set<StockTransferLineSerial>()
                join s in db.Set<SerialNumber>() on x.SerialNumberId equals s.Id
                where lineIds.Contains(x.StockTransferLineId)
                select new { x.StockTransferLineId, Unit = s }).ToListAsync(ct))
        .ToLookup(x => x.StockTransferLineId, x => x.Unit);

    /// <summary>
    /// V4.2 · Series que NO llegaron de una línea serializada: si se recibe menos de lo despachado, el faltante DEBE decir qué
    /// series faltan (tantas como unidades faltantes, todas de la línea); si llegó completa, no se indican.
    /// </summary>
    public static IReadOnlyList<SerialNumber> Missing(VariantInfo item, StockTransferLine line, IReadOnlyList<SerialNumber> units, decimal received,
        IReadOnlyList<string>? missingSerials)
    {
        var sku = item.Variant.Sku;
        Guard.That(received == decimal.Truncate(received), SerialErrorCodes.Quantity, $"{sku} lleva serie: lo recibido debe ser un número entero.");
        var shortage = (int)(line.Quantity - received);
        var listed = (missingSerials ?? []).Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0).ToList();
        Guard.That(listed.Count == listed.Distinct(StringComparer.Ordinal).Count(), SerialErrorCodes.Duplicate, $"{sku}: hay series faltantes repetidas.");
        Guard.That(listed.Count == shortage, shortage > 0 && listed.Count == 0 ? SerialErrorCodes.Required : SerialErrorCodes.Count,
            shortage == 0
                ? $"{sku} llegó completo: no indique series faltantes."
                : $"{sku}: faltan {shortage} unidad(es): indique exactamente qué series no llegaron.");
        var result = new List<SerialNumber>();
        foreach (var serial in listed)
        {
            var unit = units.FirstOrDefault(u => u.Serial == serial || u.Serial == Imei.Normalize(serial))
                       ?? throw new DomainException(SerialErrorCodes.NotInDocument, $"La serie {serial} no viaja en esta transferencia ({sku}).");
            result.Add(unit);
        }
        return result;
    }

    public static async Task<VariantInfo> ItemAsync(IMinvDbContext db, Guid variantId, CancellationToken ct)
    {
        var row = await (from v in db.Set<ProductVariant>()
                         join p in db.Set<Product>() on v.ProductId equals p.Id
                         join u in db.Set<UnitOfMeasure>() on p.BaseUnitId equals u.Id
                         where v.Id == variantId
                         select new { v, p, u.Code, u.AllowsDecimals }).FirstAsync(ct);
        return new VariantInfo(row.v, row.p, new UnitRule(row.Code, row.AllowsDecimals));
    }
}
