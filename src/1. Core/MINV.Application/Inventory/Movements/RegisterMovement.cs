using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;

namespace MINV.Application.Inventory.Movements;

/// <summary>
/// Registrar un movimiento de inventario (sucesor de la captura + RegistrarEntrada/RegistrarSalida de la V2.1).
/// El tipo decide el dominio: los de bodega exigen el permiso de bodega y los de ventas el de ventas.
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
    string? AdjustmentReasonCode = null) : IRequest<RegisterMovementResult>, IAuditableRequest
{
    public object AuditDetails => new { Sku, BinCode, MovementTypeCode, Quantity, BusinessDate, DocumentReference, LotNumber };
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
        var context = new MovementContext(userId, businessDate, clock.UtcNow, request.DocumentReference, request.Notes, reasonId);
        var movement = level.Register(type, request.Quantity, item.Unit, context);
        db.Set<StockMovement>().Add(movement);
        await db.SaveChangesAsync(ct);
        var message = $"✔ Registrado · {type.Name} {Quantities.Format(movement.Quantity)} {item.Unit.UnitCode} · " +
                      $"{item.Variant.Sku} · stock {Quantities.Format(level.QuantityOnHand)} {item.Unit.UnitCode}";
        return new RegisterMovementResult(movement.Id, level.Id, level.QuantityOnHand, level.Available, message);
    }
}
