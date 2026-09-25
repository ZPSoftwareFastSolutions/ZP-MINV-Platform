using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;

namespace MINV.Infrastructure.Persistence;

/// <summary>Fila del modelo de lectura: existencias y valor por sucursal y variante (vista materializada).</summary>
public sealed class BranchStockView
{
    public Guid TenantId { get; set; }

    public Guid BranchId { get; set; }

    public Guid VariantId { get; set; }

    public decimal OnHand { get; set; }

    public decimal Reserved { get; set; }

    public decimal Value { get; set; }

    public DateTimeOffset RefreshedAt { get; set; }
}

/// <summary>Fila del modelo de lectura: ventas por sucursal y día (vista materializada).</summary>
public sealed class BranchDailySalesView
{
    public Guid TenantId { get; set; }

    public Guid BranchId { get; set; }

    public DateOnly Day { get; set; }

    public int Tickets { get; set; }

    public decimal Revenue { get; set; }

    public decimal Tax { get; set; }

    public DateTimeOffset RefreshedAt { get; set; }
}

/// <summary>
/// V4 · Contexto de LECTURA (OLAP) para la gerencia: consultas pesadas sobre vistas del esquema <c>reporting</c>
/// (vistas materializadas refrescadas cada pocos minutos), idealmente contra una réplica de lectura
/// (<c>ConnectionStrings:MinvRead</c>), sin competir con las cajas POS por la base principal. No rastrea cambios y no
/// guarda nada. Las vistas materializadas no admiten RLS: <c>minv_app</c> solo lee las vistas de seguridad
/// <c>v_branch_stock</c> / <c>v_branch_daily_sales</c>, que filtran por <c>iam.current_tenant_id()</c> e
/// <c>iam.branch_visible()</c>; además el contexto aplica los mismos filtros de empresa y sucursal que el de escritura.
/// </summary>
public sealed class MinvReadDbContext : DbContext
{
    private readonly ITenantContext _tenant;

    public MinvReadDbContext(DbContextOptions<MinvReadDbContext> options, ITenantContext tenant)
        : base(options)
    {
        _tenant = tenant;
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
    }

    public Guid CurrentTenantId => _tenant.IsSet ? _tenant.TenantId : Guid.Empty;

    public bool AllBranches => _tenant.Branches.AllBranches;

    public IReadOnlyCollection<Guid> BranchIds => _tenant.Branches.BranchIds;

    public DbSet<BranchStockView> BranchStock => Set<BranchStockView>();

    public DbSet<BranchDailySalesView> BranchDailySales => Set<BranchDailySalesView>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new InvalidOperationException("El contexto de lectura no guarda cambios: use MinvWriteDbContext.");

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("El contexto de lectura no guarda cambios: use MinvWriteDbContext.");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BranchStockView>(b =>
        {
            b.HasNoKey();
            b.ToView("v_branch_stock", Schemas.Reporting);
            b.Property(x => x.TenantId).HasColumnName("tenant_id");
            b.Property(x => x.BranchId).HasColumnName("branch_id");
            b.Property(x => x.VariantId).HasColumnName("variant_id");
            b.Property(x => x.OnHand).HasColumnName("on_hand");
            b.Property(x => x.Reserved).HasColumnName("reserved");
            b.Property(x => x.Value).HasColumnName("value");
            b.Property(x => x.RefreshedAt).HasColumnName("refreshed_at");
            b.HasQueryFilter(x => x.TenantId == CurrentTenantId && (AllBranches || BranchIds.Contains(x.BranchId)));
        });
        modelBuilder.Entity<BranchDailySalesView>(b =>
        {
            b.HasNoKey();
            b.ToView("v_branch_daily_sales", Schemas.Reporting);
            b.Property(x => x.TenantId).HasColumnName("tenant_id");
            b.Property(x => x.BranchId).HasColumnName("branch_id");
            b.Property(x => x.Day).HasColumnName("day");
            b.Property(x => x.Tickets).HasColumnName("tickets");
            b.Property(x => x.Revenue).HasColumnName("revenue");
            b.Property(x => x.Tax).HasColumnName("tax");
            b.Property(x => x.RefreshedAt).HasColumnName("refreshed_at");
            b.HasQueryFilter(x => x.TenantId == CurrentTenantId && (AllBranches || BranchIds.Contains(x.BranchId)));
        });
    }
}
