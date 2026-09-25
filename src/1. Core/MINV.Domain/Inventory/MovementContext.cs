namespace MINV.Domain.Inventory;

/// <summary>Datos de auditoría y de negocio de un movimiento (quién, cuándo, con qué documento y por qué).</summary>
/// <param name="UserId">Usuario que registra (en la V2.1: Usuario_O365).</param>
/// <param name="BusinessDate">Fecha de negocio (en la V2.1: Fecha).</param>
/// <param name="RecordedAt">Momento exacto del registro (en la V2.1: Timestamp). Se guarda en UTC.</param>
/// <param name="DocumentReference">Documento de soporte (≤ 30 caracteres).</param>
/// <param name="Notes">Observaciones (≤ 250; obligatorias en los tipos que las exigen).</param>
/// <param name="AdjustmentReasonId">Motivo de ajuste codificado.</param>
/// <param name="LegacyReference">ID del registro en la V2.1 (E-… / S-…) al migrar.</param>
/// <param name="CorrelationId">Agrupa los movimientos de una misma operación (traslado, toma física, venta).</param>
public sealed record MovementContext(
    Guid UserId,
    DateOnly BusinessDate,
    DateTimeOffset RecordedAt,
    string? DocumentReference = null,
    string? Notes = null,
    Guid? AdjustmentReasonId = null,
    string? LegacyReference = null,
    Guid? CorrelationId = null);

/// <summary>Tipos que usa una toma física para llevar la existencia al conteo.</summary>
public sealed record CountMovementTypes(MovementType InitialBalance, MovementType AdjustmentIn, MovementType AdjustmentOut);

/// <summary>Unidad de la variante de una existencia (decide si admite decimales).</summary>
public sealed record UnitRule(string UnitCode, bool AllowsDecimals);
