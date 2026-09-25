using Microsoft.EntityFrameworkCore;
using MINV.Application.Common;
using MINV.Domain.Common;
using Npgsql;

namespace MINV.Infrastructure.Persistence;

/// <summary>Traducción de los errores de PostgreSQL a las excepciones que entiende la aplicación (y que la tubería de
/// auditoría registra como «rechazado» en lugar de «falla»).</summary>
internal static class PostgresErrors
{
    public const string UniqueViolation = "23505";
    public const string ForeignKeyViolation = "23503";
    public const string CheckViolation = "23514";
    public const string SerializationFailure = "40001";
    public const string DeadlockDetected = "40P01";
    public const string InsufficientPrivilege = "42501";
    public const string RaiseException = "P0001";

    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolation };

    public static string ConstraintName(DbUpdateException ex) =>
        (ex.InnerException as PostgresException)?.ConstraintName ?? "restricción única";

    /// <summary>
    /// 23505 (unicidad), 40001 (serialización) y 40P01 (interbloqueo) → conflicto de concurrencia (el caso de uso
    /// reintenta); 23503 (FK), 23514 (CHECK) y P0001 (regla de un trigger) → regla de negocio; 42501 (RLS) → acceso
    /// denegado. Lo demás se deja pasar como falla técnica.
    /// </summary>
    public static Exception? Translate(DbUpdateException ex) => ex.InnerException switch
    {
        PostgresException { SqlState: UniqueViolation } pg =>
            new ConcurrencyConflictException($"Otra sesión registró el mismo dato al mismo tiempo ({pg.ConstraintName ?? "restricción única"}).", ex),
        PostgresException { SqlState: SerializationFailure or DeadlockDetected } =>
            new ConcurrencyConflictException("La base de datos detectó operaciones simultáneas sobre los mismos datos.", ex),
        PostgresException { SqlState: ForeignKeyViolation } pg =>
            new DomainException("db.foreign_key", $"La operación hace referencia a un dato que no existe o no es de su empresa o sucursal ({pg.ConstraintName})."),
        PostgresException { SqlState: CheckViolation } pg =>
            new DomainException("db.check", $"La base de datos rechazó un valor fuera de regla ({pg.ConstraintName})."),
        PostgresException { SqlState: RaiseException } pg => new DomainException("db.rule", pg.MessageText),
        PostgresException { SqlState: InsufficientPrivilege } =>
            new AccessDeniedException("La base de datos rechazó la operación: la fila no es de su empresa o de sus sucursales."),
        _ => null,
    };
}
