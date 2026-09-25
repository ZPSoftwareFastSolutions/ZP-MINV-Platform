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
            if (entry.Entity is IBranchScoped branchScoped)
            {
                EnsureBranch(entry, branchScoped.BranchId);
            }
            else if (entry.Entity is IInterBranch interBranch)
            {
                // Una fila entre sucursales (transferencia, manifiesto, faltante, bitácora) la escribe una de sus dos
                // sucursales: qué lado puede hacer qué (el origen crea y despacha, el destino recibe) lo decide el caso de uso.
                EnsureInterBranch(entry, interBranch);
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

    /// <summary>V4 · Toda fila nueva de una sucursal cae dentro del alcance de la sesión y su sucursal nunca cambia.</summary>
    private void EnsureBranch(EntityEntry entry, Guid branchId)
    {
        if (entry.State == EntityState.Added)
        {
            if (branchId == Guid.Empty || !tenant.Branches.Allows(branchId))
            {
                throw new DomainException("branch.outside_scope",
                    $"{entry.Metadata.ClrType.Name}: no puede registrar operaciones en una sucursal que no es suya.");
            }
        }
        else if (entry.State == EntityState.Modified && entry.Metadata.FindProperty(nameof(IBranchScoped.BranchId)) is not null
                 && entry.Property(nameof(IBranchScoped.BranchId)).IsModified)
        {
            throw new DomainException("branch.immutable", "La sucursal de una fila no puede cambiar.");
        }
    }

    private void EnsureInterBranch(EntityEntry entry, IInterBranch row)
    {
        if (entry.State == EntityState.Added)
        {
            if (row.FromBranchId == Guid.Empty || row.ToBranchId == Guid.Empty
                || !(tenant.Branches.Allows(row.FromBranchId) || tenant.Branches.Allows(row.ToBranchId)))
            {
                throw new DomainException("branch.outside_scope",
                    $"{entry.Metadata.ClrType.Name}: la operación no es de ninguna de sus sucursales.");
            }
        }
        else if (entry.State == EntityState.Modified
                 && (entry.Property(nameof(IInterBranch.FromBranchId)).IsModified || entry.Property(nameof(IInterBranch.ToBranchId)).IsModified))
        {
            throw new DomainException("branch.immutable", "Las sucursales de una transferencia no pueden cambiar.");
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
/// Defensa en profundidad: al abrir cada conexión fija <c>minv.tenant_id</c> y (V4) <c>minv.branch_ids</c>, que usan
/// las políticas de Row Level Security de PostgreSQL. Aunque una consulta olvidara el filtro, la base de datos no
/// devolvería filas de otra empresa ni de otra sucursal (si la aplicación se conecta con el rol <c>minv_app</c>, que no
/// es dueño de las tablas). Son valores de SESIÓN: con un pool externo (PgBouncer) debe usarse el modo sesión.
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
        command.CommandText = "SELECT set_config('minv.tenant_id', @tenant, false), set_config('minv.branch_ids', @branches, false)";
        var p = command.CreateParameter();
        p.ParameterName = "tenant";
        p.Value = tenant.IsSet ? tenant.TenantId.ToString() : string.Empty;
        command.Parameters.Add(p);
        var b = command.CreateParameter();
        b.ParameterName = "branches";
        b.Value = tenant.Branches.ToSessionSetting();
        command.Parameters.Add(b);
        return command;
    }
}
