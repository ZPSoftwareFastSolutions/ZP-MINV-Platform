using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MINV.Application.Abstractions;
using MINV.Domain.Common;

namespace MINV.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Guardas de escritura (se ejecutan en cada SaveChanges, antes de enviar SQL):
/// 1) append-only: ninguna fila de un libro mayor se modifica ni se borra;
/// 2) aislamiento: toda fila nueva lleva el TenantId de la sesión y el TenantId nunca cambia;
/// 3) auditoría técnica: created_at/by y updated_at/by.
/// </summary>
public sealed class MinvSaveChangesInterceptor(ITenantContext tenant, ICurrentUser user, IClock clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    internal void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }
        var now = clock.UtcNow;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is IAppendOnly && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new AppendOnlyViolationException(entry.Metadata.ClrType.Name);
            }
            if (entry.Entity is ITenantScoped scoped)
            {
                EnsureTenant(entry, scoped);
            }
            switch (entry.State)
            {
                case EntityState.Added:
                    SetIfPresent(entry, ModelConventions.CreatedAt, now);
                    SetIfPresent(entry, ModelConventions.CreatedBy, user.UserId);
                    break;
                case EntityState.Modified:
                    SetIfPresent(entry, ModelConventions.UpdatedAt, now);
                    SetIfPresent(entry, ModelConventions.UpdatedBy, user.UserId);
                    break;
            }
        }
    }

    private void EnsureTenant(EntityEntry entry, ITenantScoped scoped)
    {
        if (entry.State == EntityState.Added)
        {
            if (!tenant.IsSet || scoped.TenantId != tenant.TenantId)
            {
                throw new DomainException("tenant.mismatch",
                    $"{entry.Metadata.ClrType.Name}: no se puede escribir fuera de la empresa de la sesión.");
            }
        }
        else if (entry.State == EntityState.Modified && entry.Property(nameof(ITenantScoped.TenantId)).IsModified)
        {
            throw new DomainException("tenant.immutable", "El TenantId de una fila no puede cambiar.");
        }
    }

    private static void SetIfPresent(EntityEntry entry, string property, object? value)
    {
        if (entry.Metadata.FindProperty(property) is not null)
        {
            entry.Property(property).CurrentValue = value;
        }
    }
}

/// <summary>
/// Defensa en profundidad: al abrir cada conexión fija <c>minv.tenant_id</c>, que usan las políticas de Row Level
/// Security de PostgreSQL. Aunque una consulta olvidara el filtro, la base de datos no devolvería filas de otra empresa
/// (si la aplicación se conecta con el rol <c>minv_app</c>, que no es dueño de las tablas).
/// </summary>
public sealed class TenantSessionInterceptor(ITenantContext tenant) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = Build(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = Build(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand Build(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('minv.tenant_id', @tenant, false)";
        var p = command.CreateParameter();
        p.ParameterName = "tenant";
        p.Value = tenant.IsSet ? tenant.TenantId.ToString() : string.Empty;
        command.Parameters.Add(p);
        return command;
    }
}
