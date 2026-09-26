using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Application.Tech;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;

namespace MINV.Application.Inventory.Movements;

/// <summary>
/// Registrar un movimiento de inventario (sucesor de la captura + RegistrarEntrada/RegistrarSalida de la V2.1).
/// El tipo decide el dominio: los de bodega exigen el permiso de bodega y los de ventas el de ventas. V4.2 · Un producto
/// serializado lleva sus series (<see cref="Serials"/>, una por unidad, regla T-02) en la MISMA transacción: las entradas
/// (saldo inicial, entrada, ajuste positivo, recepción) registran series nuevas (o reponen unidades devueltas); el ajuste
/// negativo las da de baja, la devolución a proveedor las devuelve y la salida las vende, siempre desde esa posición y
/// lote. Ventas en caja, devoluciones de clientes, transferencias y reposiciones de garantía tienen su propio documento.
/// </summary>
public sealed record RegisterMovementCommand(
    string Sku,
    string BinCode,
    string MovementTypeCode,
    decimal Quantity,
    DateOnly? BusinessDate = null,
    string? DocumentReference = null,
    string? Notes = null,
    string? LotNumber = null,
    string? AdjustmentReasonCode = null,
    IReadOnlyList<string>? Serials = null) : IRequest<RegisterMovementResult>, IAuditableRequest
{
    public object AuditDetails => new { Sku, BinCode, MovementTypeCode, Quantity, BusinessDate, DocumentReference, LotNumber, Serials };
}

public sealed record RegisterMovementResult(Guid MovementId, Guid StockLevelId, decimal QuantityOnHand, decimal Available, string Message);

public sealed class RegisterMovementValidator : AbstractValidator<RegisterMovementCommand>
{
    public RegisterMovementValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().WithMessage("Elija el producto.").MaximumLength(40);
        RuleFor(x => x.BinCode).NotEmpty().WithMessage("Indique la posición.").MaximumLength(40);
        RuleFor(x => x.MovementTypeCode).NotEmpty().WithMessage("Elija el tipo de movimiento.").MaximumLength(30);
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("La cantidad debe ser un número mayor que 0.");
        RuleFor(x => x.DocumentReference).MaximumLength(30).WithMessage("El documento supera 30 caracteres.");
        RuleFor(x => x.Notes).MaximumLength(250).WithMessage("Las observaciones superan 250 caracteres.");
        RuleFor(x => x.LotNumber).MaximumLength(40);
    }
}

public sealed class RegisterMovementHandler(IMinvDbContext db, ICurrentUser user, IClock clock)
    : IRequestHandler<RegisterMovementCommand, RegisterMovementResult>
{
    private const int MaxAttempts = 3;

    public async Task<RegisterMovementResult> Handle(RegisterMovementCommand request, CancellationToken cancellationToken)
    {
        var userId = user.UserId ?? throw new AccessDeniedException("Inicie sesión para registrar movimientos.");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await TryRegister(request, userId, cancellationToken);
            }
            catch (ConcurrencyConflictException) when (attempt < MaxAttempts)
            {
                // Otra caja o bodeguero cambió la misma existencia: se vuelve a leer el stock real y se reintenta.
                db.ClearTracking();
            }
        }
    }

    private async Task<RegisterMovementResult> TryRegister(RegisterMovementCommand request, Guid userId, CancellationToken ct)
    {
        var lookups = new InventoryLookups(db);
        var type = await lookups.MovementTypeAsync(request.MovementTypeCode, ct);
        var permission = type.Domain == MovementDomain.Sales
            ? PermissionCodes.MovementsRegisterSales
            : PermissionCodes.MovementsRegisterWarehouse;
        if (!user.HasPermission(permission))
        {
            throw new AccessDeniedException($"Su rol no permite registrar movimientos de tipo {type.Name}.");
        }
        var item = await lookups.VariantBySkuAsync(request.Sku, ct);
        Guard.That(item.Product.IsActive && item.Variant.IsActive, "product.inactive",
            $"El producto {item.Variant.Sku} está inactivo.");
        var bin = await lookups.BinByCodeAsync(request.BinCode, ct);
        var config = await lookups.ConfigAsync(ct);
        var today = clock.TodayIn(config.TimeZoneId);
        var businessDate = request.BusinessDate ?? today;
        Guard.That(businessDate <= today && businessDate >= config.MinBusinessDate, "movement.date",
            "La fecha no es válida (futura o anterior a la fecha mínima).");
        Guid? reasonId = null;
        if (!string.IsNullOrWhiteSpace(request.AdjustmentReasonCode))
        {
            var code = request.AdjustmentReasonCode.Trim().ToUpperInvariant();
            reasonId = (await db.Set<AdjustmentReason>().FirstOrDefaultAsync(r => r.Code == code && r.IsActive, ct)
                        ?? throw new NotFoundException($"El motivo de ajuste {code} no existe.")).Id;
        }
        var batch = await lookups.BatchAsync(item.Variant, request.LotNumber, ct);
        var (level, isNew) = await lookups.StockLevelAsync(item.Variant.TenantId, bin.Id, batch.Id, ct);
        if (type.IsInitialBalance)
        {
            StockLevel.EnsureInitialBalanceAllowed(type, !isNew && await lookups.HasMovementsAsync(level.Id, ct));
        }
        var ledger = new SerialLedger(db);
        var serials = await ledger.ExpectAsync(item, request.Quantity, request.Serials, ct);
        var leaving = serials.Count > 0 && !type.Increases ? await ledger.OnHandAtAsync(item, serials, level, bin.Code, ct) : [];
        var now = clock.UtcNow;
        var context = new MovementContext(userId, businessDate, now, request.DocumentReference, request.Notes, reasonId);
        var movement = level.Register(type, request.Quantity, item.Unit, context);
        db.Set<StockMovement>().Add(movement);
        if (serials.Count > 0)
        {
            await MoveSerialsAsync(ledger, type, item, serials, leaving, batch, level, userId, request, ct);
        }
        // V4.2 · Un AJUSTE (±) se contabiliza al costo promedio del almacén (5.1.09 / 4.1.02 contra 1.1.05): el mayor sigue al
        // valor del stock (ver InventoryAdjustments)
        string? entryNumber = null;
        if (InventoryAdjustments.Posts(type.Code))
        {
            var unitCost = await AverageCosts.CurrentAsync(db, item.Variant.Id, await lookups.WarehouseOfBinAsync(bin.Id, ct), ct);
            var value = movement.Quantity * unitCost;
            var entry = await InventoryAdjustments.PostAsync(db, level.TenantId, level.BranchId, userId, businessDate, today,
                $"{type.Name} {Quantities.Format(movement.Quantity)} {item.Unit.UnitCode} · {item.Variant.Sku}" +
                (string.IsNullOrWhiteSpace(request.Notes) ? string.Empty : $": {request.Notes.Trim()}"),
                type.Increases ? value : 0, type.Increases ? 0 : value, now, movement.Id, ct);
            entryNumber = entry?.Number;
        }
        await db.SaveChangesAsync(ct);
        var message = $"✔ Registrado · {type.Name} {Quantities.Format(movement.Quantity)} {item.Unit.UnitCode} · " +
                      $"{item.Variant.Sku} · stock {Quantities.Format(level.QuantityOnHand)} {item.Unit.UnitCode}" +
                      (entryNumber is null ? string.Empty : $" · asiento {entryNumber}");
        return new RegisterMovementResult(movement.Id, level.Id, level.QuantityOnHand, level.Available, message);
    }

    /// <summary>V4.2 · Lo que el movimiento le hace a cada serie (misma transacción que el movimiento).</summary>
    private async Task MoveSerialsAsync(SerialLedger ledger, MovementType type, VariantInfo item, IReadOnlyList<string> serials,
        IReadOnlyList<SerialNumber> leaving, Batch batch, StockLevel level, Guid userId, RegisterMovementCommand request, CancellationToken ct)
    {
        var own = type.Code switch
        {
            MovementTypeCodes.Sale => "la caja (Cobrar)",
            MovementTypeCodes.SaleReturn => "la devolución de la venta",
            MovementTypeCodes.TransferOut or MovementTypeCodes.TransferIn => "las transferencias entre sucursales",
            MovementTypeCodes.WarrantyReplacement => "la reposición del caso RMA",
            _ => null,
        };
        Guard.That(own is null, SerialErrorCodes.UseDocument, $"{item.Variant.Sku} lleva serie: registre {type.Name} desde {own}.");
        var note = request.Notes ?? type.Name;
        var context = new SerialContext(level.BranchId, userId, clock.UtcNow, request.DocumentReference, $"{type.Name}: {note}");
        if (type.Increases)
        {
            await ledger.EnterAsync(item, serials, batch, level, context, allowReentry: true, ct);
            return;
        }
        foreach (var unit in leaving)
        {
            _ = type.Code switch
            {
                MovementTypeCodes.AdjustmentOut => unit.Scrap(level, context),
                MovementTypeCodes.PurchaseReturn => unit.ReturnToSupplier(level, context),
                _ => unit.Sell(level, context),
            };
        }
    }
}
