using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Domain.Common;
using MINV.Domain.Iam;
using MINV.Domain.Inventory;
using MINV.Infrastructure.Persistence;
using MINV.Infrastructure.Persistence.Interceptors;
using MINV.Infrastructure.Services;

namespace MINV.Infrastructure.Tests;

/// <summary>Guardas de escritura y servicios (sin PostgreSQL: el interceptor se ejecuta sobre el ChangeTracker).</summary>
public sealed class InterceptorTests
{
    private static (MINVDbContext Db, MinvSaveChangesInterceptor Interceptor, TenantContext Tenant) Build()
    {
        var tenant = new TenantContext();
        var options = new DbContextOptionsBuilder<MINVDbContext>();
        DependencyInjection.Configure(options, DependencyInjection.DefaultConnectionString);
        var db = new MINVDbContext(options.Options, tenant);
        var user = new CurrentUser();
        user.SignIn(Guid.NewGuid(), "ana@demo.example", "Ana", []);
        return (db, new MinvSaveChangesInterceptor(tenant, user, new SystemClock()), tenant);
    }

    private static StockMovement Movement(Guid tenant)
    {
        var type = MovementType.CreateDefaults(tenant).First(t => t.Code == MovementTypeCodes.Receipt);
        var level = StockLevel.Open(tenant, Guid.NewGuid(), Guid.NewGuid());
        return level.Register(type, 5, new UnitRule("UND", false), new MovementContext(Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.UtcNow), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Modificar_o_borrar_un_movimiento_del_libro_mayor_esta_prohibido()
    {
        var (db, interceptor, tenant) = Build();
        var id = Guid.NewGuid();
        tenant.Set(id);
        var movement = Movement(id);
        db.Attach(movement);
        db.Entry(movement).State = EntityState.Modified;
        Assert.Throws<AppendOnlyViolationException>(() => interceptor.Apply(db));
        db.Entry(movement).State = EntityState.Deleted;
        Assert.Throws<AppendOnlyViolationException>(() => interceptor.Apply(db));
    }

    [Fact]
    public void No_se_puede_escribir_una_fila_de_otra_empresa()
    {
        var (db, interceptor, tenant) = Build();
        tenant.Set(Guid.NewGuid());
        db.Add(new Role(Guid.NewGuid(), "BODEGA", "Bodega", true));
        Assert.Equal("tenant.mismatch", Assert.Throws<DomainException>(() => interceptor.Apply(db)).Code);
    }

    [Fact]
    public void Las_filas_nuevas_reciben_la_auditoria_tecnica()
    {
        var (db, interceptor, tenant) = Build();
        var id = Guid.NewGuid();
        tenant.Set(id);
        var role = new Role(id, "BODEGA", "Bodega", true);
        db.Add(role);
        interceptor.Apply(db);
        Assert.NotEqual(default, db.Entry(role).Property<DateTimeOffset>(ModelConventions.CreatedAt).CurrentValue);
        Assert.NotNull(db.Entry(role).Property<Guid?>(ModelConventions.CreatedBy).CurrentValue);
    }
}

public sealed class ServicesTests
{
    [Fact]
    public void El_hash_PBKDF2_verifica_la_contrasena_correcta_y_rechaza_otras()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var hash = hasher.Hash("Clave-Segura-2026");
        Assert.True(hasher.Verify("Clave-Segura-2026", hash, hasher.Iterations));
        Assert.False(hasher.Verify("clave-segura-2026", hash, hasher.Iterations));
        Assert.NotEqual(hash, hasher.Hash("Clave-Segura-2026"));   // sal aleatoria
        Assert.Equal("password.length", Assert.Throws<DomainException>(() => hasher.Hash("corta")).Code);
    }

    [Theory]
    [InlineData("StockMovement", "stock_movement")]
    [InlineData("POSSessions", "pos_sessions")]
    [InlineData("UnitsOfMeasure", "units_of_measure")]
    [InlineData("Sales30Days", "sales30_days")]
    public void Nombres_en_snake_case(string name, string expected) => Assert.Equal(expected, ModelConventions.ToSnakeCase(name));

    [Fact]
    public void Los_identificadores_largos_se_acortan_sin_colisiones()
    {
        var a = ModelConventions.Limit("fk_supplier_invoice_lines_tenant_id_goods_receipt_line_id_extra_largo_1");
        var b = ModelConventions.Limit("fk_supplier_invoice_lines_tenant_id_goods_receipt_line_id_extra_largo_2");
        Assert.True(a.Length <= 63 && b.Length <= 63);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void La_fecha_de_hoy_depende_de_la_zona_horaria_de_la_empresa()
    {
        var clock = new SystemClock();
        var laPaz = clock.TodayIn("America/La_Paz");
        var tokyo = clock.TodayIn("Asia/Tokyo");
        Assert.InRange(tokyo.DayNumber - laPaz.DayNumber, 0, 1);
    }
}
