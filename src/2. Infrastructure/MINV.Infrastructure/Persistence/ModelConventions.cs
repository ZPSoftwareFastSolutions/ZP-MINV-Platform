using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MINV.Domain.Common;
using MINV.Domain.Iam;

namespace MINV.Infrastructure.Persistence;

/// <summary>Esquemas de PostgreSQL: uno por contexto delimitado.</summary>
public static class Schemas
{
    public const string Iam = "iam";
    public const string Catalog = "catalog";
    public const string Warehousing = "warehouse";
    public const string Inventory = "inventory";
    public const string Purchasing = "purchasing";
    public const string Sales = "sales";
    public const string Accounting = "accounting";
    public const string Integration = "integration";

    /// <summary>Vistas materializadas y vistas de seguridad del modelo de lectura (no son tablas del modelo EF).</summary>
    public const string Reporting = "reporting";

    public static readonly IReadOnlyList<string> All = [Iam, Catalog, Warehousing, Inventory, Purchasing, Sales, Accounting, Integration];
}

/// <summary>Reglas transversales del modelo (se aplican después de las configuraciones de cada entidad).</summary>
internal static partial class ModelConventions
{
    public const string CreatedAt = "CreatedAt";
    public const string CreatedBy = "CreatedBy";
    public const string UpdatedAt = "UpdatedAt";
    public const string UpdatedBy = "UpdatedBy";
    public const int MaxIdentifierLength = 63;

    private static readonly MethodInfo FilterMethod =
        typeof(ModelConventions).GetMethod(nameof(SetTenantFilter), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo BranchFilterMethod =
        typeof(ModelConventions).GetMethod(nameof(SetBranchFilter), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo InterBranchFilterMethod =
        typeof(ModelConventions).GetMethod(nameof(SetInterBranchFilter), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static void Apply(ModelBuilder modelBuilder, MinvWriteDbContext context)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            var clr = entityType.ClrType;
            var builder = modelBuilder.Entity(clr);
            if (typeof(ITenantScoped).IsAssignableFrom(clr))
            {
                // Toda fila pertenece a un tenant existente y solo se ve dentro de su tenant.
                builder.HasOne(typeof(Tenant)).WithMany().HasForeignKey(nameof(ITenantScoped.TenantId)).OnDelete(DeleteBehavior.Restrict);
                // V4 · Un solo filtro por entidad (EF Core admite uno): empresa y, si corresponde, sucursal.
                var method = typeof(IBranchScoped).IsAssignableFrom(clr) ? BranchFilterMethod
                    : typeof(IInterBranch).IsAssignableFrom(clr) ? InterBranchFilterMethod
                    : FilterMethod;
                method.MakeGenericMethod(clr).Invoke(null, [modelBuilder, context]);
            }
            if (typeof(Entity).IsAssignableFrom(clr))
            {
                // Clave alterna (tenant_id, id): destino de las FK compuestas que impiden mezclar empresas.
                builder.HasAlternateKey(nameof(ITenantScoped.TenantId), nameof(Entity.Id));
                builder.Property(nameof(Entity.Id)).ValueGeneratedNever();
                if (typeof(IBranchScoped).IsAssignableFrom(clr))
                {
                    // V4 · Clave alterna (tenant_id, branch_id, id): destino de las FK compuestas con sucursal, que impiden
                    // que un hijo (línea, movimiento, posición) tenga otra sucursal que su padre.
                    builder.HasAlternateKey(nameof(ITenantScoped.TenantId), nameof(IBranchScoped.BranchId), nameof(Entity.Id));
                }
            }
            if (typeof(PlatformEntity).IsAssignableFrom(clr))
            {
                builder.Property(nameof(PlatformEntity.Id)).ValueGeneratedNever();
            }
            if (typeof(IConcurrencyAware).IsAssignableFrom(clr))
            {
                // Npgsql mapea un uint IsRowVersion a la columna de sistema xmin (no ocupa espacio ni se migra).
                builder.Property(nameof(IConcurrencyAware.RowVersion)).IsRowVersion();
            }
            // Auditoría técnica en propiedades sombra: el dominio no las ve; las llena el interceptor.
            builder.Property<DateTimeOffset>(CreatedAt).HasDefaultValueSql("now()");
            builder.Property<Guid?>(CreatedBy);
            if (!typeof(IAppendOnly).IsAssignableFrom(clr))
            {
                builder.Property<DateTimeOffset?>(UpdatedAt);
                builder.Property<Guid?>(UpdatedBy);
            }
        }
        ApplySnakeCaseNames(modelBuilder.Model);
    }

    private static void SetTenantFilter<T>(ModelBuilder modelBuilder, MinvWriteDbContext context) where T : class, ITenantScoped =>
        modelBuilder.Entity<T>().HasQueryFilter(e => e.TenantId == context.CurrentTenantId);

    /// <summary>V4 · Fila de una sucursal: visible si la sesión ve todas o si su sucursal está en el alcance.</summary>
    private static void SetBranchFilter<T>(ModelBuilder modelBuilder, MinvWriteDbContext context) where T : class, IBranchScoped =>
        modelBuilder.Entity<T>().HasQueryFilter(e => e.TenantId == context.CurrentTenantId
                                                     && (context.AllBranches || context.BranchIds.Contains(e.BranchId)));

    /// <summary>V4 · Documento entre sucursales: lo ven el origen y el destino.</summary>
    private static void SetInterBranchFilter<T>(ModelBuilder modelBuilder, MinvWriteDbContext context) where T : class, IInterBranch =>
        modelBuilder.Entity<T>().HasQueryFilter(e => e.TenantId == context.CurrentTenantId
                                                     && (context.AllBranches || context.BranchIds.Contains(e.FromBranchId)
                                                         || context.BranchIds.Contains(e.ToBranchId)));

    /// <summary>Nombres de PostgreSQL en snake_case y restricciones con prefijos estables (pk_, ak_, fk_, ux_, ix_).</summary>
    private static void ApplySnakeCaseNames(IMutableModel model)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            var table = entityType.GetTableName() ?? ToSnakeCase(entityType.ClrType.Name);
            foreach (var property in entityType.GetProperties())
            {
                if (property.Name == nameof(IConcurrencyAware.RowVersion) && typeof(IConcurrencyAware).IsAssignableFrom(entityType.ClrType))
                {
                    continue;   // xmin
                }
                property.SetColumnName(ToSnakeCase(property.Name));
            }
            foreach (var key in entityType.GetKeys())
            {
                key.SetName(key.IsPrimaryKey() ? Limit($"pk_{table}") : Limit($"ak_{table}_{Columns(key.Properties)}"));
            }
            foreach (var fk in entityType.GetForeignKeys())
            {
                fk.SetConstraintName(Limit($"fk_{table}_{Columns(fk.Properties)}"));
            }
            foreach (var index in entityType.GetIndexes())
            {
                index.SetDatabaseName(Limit($"{(index.IsUnique ? "ux" : "ix")}_{table}_{Columns(index.Properties)}"));
            }
        }
    }

    private static string Columns(IEnumerable<IReadOnlyProperty> properties) =>
        string.Join("_", properties.Select(p => ToSnakeCase(p.Name)));

    /// <summary>PostgreSQL trunca identificadores de más de 63 bytes: se acortan con un sufijo hash determinista.</summary>
    internal static string Limit(string name)
    {
        if (name.Length <= MaxIdentifierLength)
        {
            return name;
        }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..8].ToLowerInvariant();
        return name[..(MaxIdentifierLength - 9)] + "_" + hash;
    }

    internal static string ToSnakeCase(string name) => LowerUpper().Replace(UpperRun().Replace(name, "$1_$2"), "$1_$2").ToLowerInvariant();

    [GeneratedRegex("([A-Z]+)([A-Z][a-z])")]
    private static partial Regex UpperRun();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex LowerUpper();
}
