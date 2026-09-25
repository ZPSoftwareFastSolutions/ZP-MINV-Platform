using MINV.Domain.Common;
using MINV.Domain.Inventory;

namespace MINV.Domain.Tests;

public sealed class StockLevelTests
{
    private readonly TestData _t = new();

    [Fact]
    public void Una_entrada_suma_y_una_salida_resta_segun_el_factor_del_tipo()
    {
        var level = _t.Level();
        var m1 = level.Register(_t.Type(MovementTypeCodes.Receipt), 20, TestData.Und, _t.Context(doc: "FC-10290"));
        var m2 = level.Register(_t.Type(MovementTypeCodes.Issue), 8, TestData.Und, _t.Context());
        Assert.Equal(12, level.QuantityOnHand);
        Assert.Equal(20, m1.Quantity);
        Assert.Equal(8, m2.Quantity);   // la cantidad siempre es positiva: el signo lo da el tipo
        Assert.Equal("FC-10290", m1.DocumentReference);
        Assert.Equal(level.Id, m2.StockLevelId);
    }

    [Fact]
    public void Poka_yoke_una_salida_mayor_que_el_disponible_se_bloquea_y_no_cambia_nada()
    {
        var level = _t.Level(5);
        var ex = Assert.Throws<InsufficientStockException>(() =>
            level.Register(_t.Type(MovementTypeCodes.Issue), 6, TestData.Und, _t.Context()));
        Assert.Equal(5, ex.Available);
        Assert.Equal(5, level.QuantityOnHand);
        Assert.Contains("Stock insuficiente: disponible 5", ex.Message);
    }

    [Fact]
    public void Las_reservas_protegen_el_stock_frente_a_otras_salidas()
    {
        var level = _t.Level(10);
        var reservation = level.Reserve(7, _t.Now.AddMinutes(15), _t.Now);
        Assert.Equal(3, level.Available);
        Assert.Throws<InsufficientStockException>(() =>
            level.Register(_t.Type(MovementTypeCodes.Issue), 4, TestData.Und, _t.Context()));
        var sale = level.Consume(reservation, _t.Type(MovementTypeCodes.Sale), TestData.Und, _t.Context());
        Assert.Equal(7, sale.Quantity);
        Assert.Equal(3, level.QuantityOnHand);
        Assert.Equal(0, level.QuantityReserved);
        Assert.Equal(ReservationStatus.Consumed, reservation.Status);
    }

    [Fact]
    public void Liberar_o_vencer_una_reserva_devuelve_el_disponible()
    {
        var level = _t.Level(10);
        var r1 = level.Reserve(4, _t.Now.AddMinutes(5), _t.Now);
        var r2 = level.Reserve(3, _t.Now.AddMinutes(5), _t.Now);
        level.Release(r1);
        level.Expire(r2, _t.Now.AddMinutes(6));
        Assert.Equal(10, level.Available);
        Assert.Throws<DomainException>(() => level.Release(r1));
    }

    [Fact]
    public void Las_unidades_sin_decimales_rechazan_cantidades_fraccionarias()
    {
        var level = _t.Level();
        var ex = Assert.Throws<DomainException>(() =>
            level.Register(_t.Type(MovementTypeCodes.Receipt), 1.5m, TestData.Und, _t.Context()));
        Assert.Equal("quantity.decimals", ex.Code);
        level.Register(_t.Type(MovementTypeCodes.Receipt), 1.5m, TestData.Kg, _t.Context());
        Assert.Equal(1.5m, level.QuantityOnHand);
    }

    [Fact]
    public void Los_ajustes_exigen_observacion_como_en_la_V21()
    {
        var level = _t.Level(10);
        var ex = Assert.Throws<DomainException>(() =>
            level.Register(_t.Type(MovementTypeCodes.AdjustmentOut), 1, TestData.Und, _t.Context()));
        Assert.Equal("movement.notes_required", ex.Code);
        level.Register(_t.Type(MovementTypeCodes.AdjustmentOut), 1, TestData.Und, _t.Context("Merma"));
        Assert.Equal(9, level.QuantityOnHand);
    }

    [Fact]
    public void El_saldo_inicial_solo_se_admite_en_una_existencia_sin_movimientos()
    {
        var type = _t.Type(MovementTypeCodes.InitialBalance);
        StockLevel.EnsureInitialBalanceAllowed(type, hasMovements: false);
        var ex = Assert.Throws<DomainException>(() => StockLevel.EnsureInitialBalanceAllowed(type, hasMovements: true));
        Assert.Equal("movement.initial_balance_repeated", ex.Code);
    }

    [Fact]
    public void Un_tipo_de_otra_empresa_no_se_puede_usar()
    {
        var level = _t.Level();
        var other = MovementType.CreateDefaults(Guid.NewGuid()).First(x => x.Code == MovementTypeCodes.Receipt);
        Assert.Equal("tenant.mismatch", Assert.Throws<DomainException>(() =>
            level.Register(other, 1, TestData.Und, _t.Context())).Code);
    }

    [Theory]
    [InlineData(false, 0, 0, null)]
    [InlineData(false, 0, 7, MovementTypeCodes.InitialBalance)]
    [InlineData(true, 26, 28, MovementTypeCodes.AdjustmentIn)]
    [InlineData(true, 26, 25, MovementTypeCodes.AdjustmentOut)]
    [InlineData(true, 26, 26, null)]
    public void AdjustToCount_lleva_la_existencia_a_lo_contado(bool hasMovements, int system, int counted, string? expectedType)
    {
        var level = _t.Level(system);
        var movement = level.AdjustToCount(counted, _t.CountTypes, hasMovements, TestData.Und, _t.Context("Toma física"));
        Assert.Equal(counted, level.QuantityOnHand);
        if (expectedType is null)
        {
            Assert.Null(movement);
        }
        else
        {
            Assert.Equal(_t.Type(expectedType).Id, movement!.MovementTypeId);
            Assert.Equal(Math.Abs(counted - system), movement.Quantity);
        }
    }

    [Fact]
    public void El_movimiento_guarda_la_auditoria_y_su_referencia_V21()
    {
        var level = _t.Level();
        var context = new MovementContext(_t.User, _t.Today, _t.Now, "FC-1", "nota", LegacyReference: "E-20260925-101500-AB12");
        var m = level.Register(_t.Type(MovementTypeCodes.Receipt), 3, TestData.Und, context);
        Assert.Equal(_t.User, m.RecordedByUserId);
        Assert.Equal(_t.Now, m.RecordedAt);
        Assert.Equal("E-20260925-101500-AB12", m.LegacyReference);
        Assert.Equal(_t.Now.ToUnixTimeMilliseconds(), UuidV7.GetTimestamp(m.Id).ToUnixTimeMilliseconds());
    }

    [Fact]
    public void Una_fecha_de_negocio_futura_se_rechaza()
    {
        var level = _t.Level();
        var context = new MovementContext(_t.User, _t.Today.AddDays(2), _t.Now);
        Assert.Equal("movement.future_date", Assert.Throws<DomainException>(() =>
            level.Register(_t.Type(MovementTypeCodes.Receipt), 1, TestData.Und, context)).Code);
    }
}

public sealed class StockRulesTests
{
    // Casos del semáforo de la V2.1 (tblEstados): mínimo 20, máximo 120, margen 20 %
    [Theory]
    [InlineData(-1, StockStatusCode.Inconsistent)]
    [InlineData(0, StockStatusCode.OutOfStock)]
    [InlineData(5, StockStatusCode.Critical)]
    [InlineData(20, StockStatusCode.Critical)]
    [InlineData(21, StockStatusCode.Low)]
    [InlineData(24, StockStatusCode.Low)]
    [InlineData(25, StockStatusCode.Optimal)]
    [InlineData(120, StockStatusCode.Optimal)]
    [InlineData(121, StockStatusCode.Overstock)]
    public void Semaforo_igual_al_de_la_V21(decimal stock, StockStatusCode expected) =>
        Assert.Equal(expected, StockRules.Evaluate(stock, 20, 120, isActive: true, alertMargin: 0.20m));

    [Fact]
    public void Un_producto_inactivo_siempre_es_INACTIVO_y_sin_maximo_no_hay_sobrestock()
    {
        Assert.Equal(StockStatusCode.Inactive, StockRules.Evaluate(0, 20, 120, false, 0.2m));
        Assert.Equal(StockStatusCode.Optimal, StockRules.Evaluate(10_000, 20, 0, true, 0.2m));
    }

    [Fact]
    public void Cantidad_sugerida_y_tope_de_reposicion_como_en_16_ALERTAS_y_18_PEDIDO()
    {
        Assert.Equal(98, StockRules.SuggestedQuantity(StockStatusCode.Low, 22, 20, 120));
        Assert.Equal(40, StockRules.SuggestedQuantity(StockStatusCode.OutOfStock, 0, 20, 0));   // sin máximo: 2 × mínimo
        Assert.Equal(0, StockRules.SuggestedQuantity(StockStatusCode.Overstock, 150, 20, 120));
        Assert.Equal(0, StockRules.SuggestedQuantity(StockStatusCode.Inconsistent, -3, 20, 120));
    }

    [Fact]
    public void Cobertura_en_dias_al_ritmo_de_30_dias()
    {
        Assert.Equal(246, StockRules.CoverageDays(53.5m, 6.5m));   // PIN-001 de la demo de la V2.1
        Assert.Null(StockRules.CoverageDays(10, 0));
        Assert.Equal(0, StockRules.CoverageDays(-2, 5));
    }

    [Fact]
    public void Etiquetas_y_codigos_del_semaforo()
    {
        foreach (var status in Enum.GetValues<StockStatusCode>())
        {
            Assert.Equal(status, StockRules.FromLabel(StockRules.Label(status)));
            Assert.Matches("^[A-Z]+$", StockRules.Code(status));
        }
        Assert.Equal("CRÍTICO", StockRules.Label(StockStatusCode.Critical));
    }
}

public sealed class QuantitiesAndIdsTests
{
    [Theory]
    [InlineData("0.1234565", "0.123457")]
    [InlineData("2.5000004", "2.5")]
    [InlineData("-1.0000005", "-1")]
    public void Round6_es_el_r6_de_la_V21(string value, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            Quantities.Round6(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void UuidV7_tiene_version_7_y_es_monotono()
    {
        var ids = Enumerable.Range(0, 2000).Select(_ => UuidV7.NewGuid()).ToList();
        Assert.All(ids, id => Assert.Equal('7', id.ToString()[14]));
        var bytes = ids.Select(id => id.ToByteArray(bigEndian: true)).ToList();
        for (var i = 1; i < bytes.Count; i++)
        {
            Assert.True(bytes[i - 1].AsSpan().SequenceCompareTo(bytes[i]) < 0, "los UUID v7 deben crecer (orden de PostgreSQL)");
        }
    }
}
