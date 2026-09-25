using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using MINV.Domain.Common;
using MINV.Infrastructure;
using MINV.Infrastructure.Persistence;

namespace MINV.Infrastructure.Tests;

/// <summary>Pruebas del modelo relacional (no necesitan PostgreSQL: EF Core arma el modelo y el DDL en memoria).</summary>
public sealed class ModelTests
{
    private static readonly MinvWriteDbContext Db = new MinvWriteDbContextDesignTimeFactory().CreateDbContext([]);
    private static readonly IModel Model = Db.GetService<IDesignTimeModel>().Model;
    private static readonly string Ddl = Db.Database.GenerateCreateScript();

    private static IEnumerable<IEntityType> Entities => Model.GetEntityTypes().Where(e => !e.IsOwned());

    [Fact]
    public void El_modelo_tiene_mas_de_80_tablas_en_8_esquemas()
    {
        var tables = Entities.Select(e => (e.GetSchema(), e.GetTableName())).Distinct().ToList();
        Assert.True(tables.Count >= 80, $"solo {tables.Count} tablas");
        Assert.Equal(110, tables.Count);   // 96 de la V3 + product_images (V3.1) + 13 de la V4 (sucursales, integración, idempotencia)
        Assert.Equal(Schemas.All.OrderBy(s => s), tables.Select(t => t.Item1!).Distinct().OrderBy(s => s));
    }

    [Fact]
    public void Toda_entidad_salvo_la_plataforma_tiene_TenantId_filtro_global_y_FK_al_tenant()
    {
        foreach (var e in Entities)
        {
            if (typeof(PlatformEntity).IsAssignableFrom(e.ClrType))
            {
                continue;
            }
            Assert.True(typeof(ITenantScoped).IsAssignableFrom(e.ClrType), $"{e.ClrType.Name} no tiene TenantId");
            Assert.NotNull(e.GetQueryFilter());
            Assert.Contains(e.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType.Name == "Tenant");
        }
        Assert.Equal(2, Entities.Count(e => typeof(PlatformEntity).IsAssignableFrom(e.ClrType)));
    }

    [Fact]
    public void Las_FK_entre_entidades_de_negocio_incluyen_el_tenant_para_no_mezclar_empresas()
    {
        foreach (var fk in Entities.SelectMany(e => e.GetForeignKeys()))
        {
            var principal = fk.PrincipalEntityType.ClrType;
            if (typeof(PlatformEntity).IsAssignableFrom(principal))
            {
                continue;
            }
            // V3: (tenant_id, x_id). V4: (tenant_id, branch_id, x_id) entre filas de una sucursal y
            // (tenant_id, from_branch_id, to_branch_id, x_id) entre las filas de una transferencia.
            var names = fk.Properties.Select(p => p.Name).ToList();
            // (la FK a la propia sucursal, p. ej. centro de costo → sucursal, es (tenant_id, branch_id): ahí branch_id es la referencia)
            var branchColumns = principal.Name == "Branch" ? 0 : names.Count(n => n is "BranchId" or "FromBranchId" or "ToBranchId");
            Assert.True(names.Contains(nameof(ITenantScoped.TenantId)) && fk.Properties.Count == 2 + branchColumns && branchColumns <= 2,
                $"{fk.DeclaringEntityType.ClrType.Name} → {principal.Name}: FK sin tenant_id");
        }
    }

    [Fact]
    public void Las_tablas_transaccionales_usan_xmin_como_token_de_concurrencia()
    {
        var conc = Entities.Where(e => typeof(IConcurrencyAware).IsAssignableFrom(e.ClrType)).ToList();
        Assert.Contains(conc, e => e.ClrType.Name == "StockLevel");
        Assert.Contains(conc, e => e.ClrType.Name == "PosSession");
        foreach (var e in conc)
        {
            var p = e.FindProperty(nameof(IConcurrencyAware.RowVersion))!;
            Assert.True(p.IsConcurrencyToken, e.ClrType.Name);
            Assert.Equal("xmin", p.GetColumnName());
        }
        Assert.DoesNotContain("row_version", Ddl, StringComparison.Ordinal);
    }

    [Fact]
    public void Los_libros_mayores_son_append_only()
    {
        var appendOnly = Entities.Where(e => typeof(IAppendOnly).IsAssignableFrom(e.ClrType)).Select(e => e.ClrType.Name).OrderBy(n => n);
        Assert.Equal(new[]
        {
            "AccessLog", "AuditLog", "AverageCostHistory", "CashMovement", "ExchangeRate", "ExternalOrder", "OutboxEvent", "Payment",
            "ProcessedRequest", "StockMovement", "StockTransferDiscrepancy", "StockTransferEvent", "StockTransferLineBatch",
            "StockTransferMovement", "WebhookDelivery",
        }, appendOnly);
    }

    [Fact]
    public void Los_identificadores_caben_en_PostgreSQL_y_estan_en_snake_case()
    {
        foreach (var e in Entities)
        {
            Assert.Matches("^[a-z][a-z0-9_]*$", e.GetTableName()!);
            foreach (var p in e.GetProperties())
            {
                Assert.Matches("^[a-z][a-z0-9_]*$", p.GetColumnName());
            }
            foreach (var name in e.GetKeys().Select(k => k.GetName()!)
                         .Concat(e.GetForeignKeys().Select(f => f.GetConstraintName()!))
                         .Concat(e.GetIndexes().Select(i => i.GetDatabaseName()!)))
            {
                Assert.True(name.Length <= 63, $"{name} supera 63 caracteres");
            }
        }
    }

    [Theory]
    [InlineData("CREATE SCHEMA inventory;")]
    [InlineData("CREATE TABLE inventory.stock_movements")]
    [InlineData("CONSTRAINT ck_stock_levels_existencia CHECK (quantity_on_hand >= 0)")]
    [InlineData("CONSTRAINT ck_movement_types_factor CHECK (stock_factor IN (-1, 1))")]
    [InlineData("CONSTRAINT ck_journal_lines_partida CHECK ((debit > 0 AND credit = 0) OR (credit > 0 AND debit = 0))")]
    [InlineData("WHERE status = 'Open'")]
    [InlineData("NULLS NOT DISTINCT")]
    [InlineData("quantity numeric(18,6) NOT NULL")]
    [InlineData("REFERENCES catalog.product_variants (tenant_id, id)")]
    public void El_DDL_generado_contiene_las_restricciones_clave(string fragment) =>
        Assert.Contains(fragment, Ddl, StringComparison.Ordinal);

    [Fact]
    public void El_ERD_documenta_todas_las_tablas_del_modelo()
    {
        var erd = File.ReadAllText(Path.Combine(V21MigrationTests.RepoRoot(), "docs", "database", "ERD-MINV-V3.md"));
        foreach (var e in Entities)
        {
            Assert.Contains($"`{e.GetSchema()}.{e.GetTableName()}`", erd, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Los_modulos_comerciales_se_siembran_con_la_migracion()
    {
        Assert.Contains("DATA_ENGINE", Ddl, StringComparison.Ordinal);
        Assert.Contains("SLA_SUPPORT", Ddl, StringComparison.Ordinal);
    }
}
