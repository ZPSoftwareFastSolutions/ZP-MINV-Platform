using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MINV.Infrastructure.Persistence;

/// <summary>Interpretación de los errores de PostgreSQL que la aplicación trata de forma especial.</summary>
internal static class PostgresErrors
{
    public const string UniqueViolation = "23505";
    public const string RaiseException = "P0001";

    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolation };

    public static string ConstraintName(DbUpdateException ex) =>
        (ex.InnerException as PostgresException)?.ConstraintName ?? "restricción única";
}
