using MINV.Domain.Accounting;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;

namespace MINV.Domain.Tests;

public sealed class PhysicalCountTests
{
    private readonly TestData _t = new();

    [Fact]
    public void El_numero_del_documento_es_CF_AAAAMMDD_como_en_la_V21()
    {
        Assert.Equal("CF-20260925", PhysicalCount.NumberFor(new DateOnly(2026, 9, 25), 1));
        Assert.Equal("CF-20260925-2", PhysicalCount.NumberFor(new DateOnly(2026, 9, 25), 2));
    }

    [Fact]
    public void Contabilizar_genera_sobrante_faltante_y_saldo_inicial_contra_el_stock_exacto()
    {
        var surplus = _t.Level(26);
        var shortage = _t.Level(26);
        var fresh = _t.Level();
        var same = _t.Level(10);
        var count = PhysicalCount.Open(_t.Tenant, Guid.NewGuid(), _t.Today);
        count.RecordCount(surplus.Id, 28, _t.User, _t.Now);
        count.RecordCount(shortage.Id, 20, _t.User, _t.Now);
        count.RecordCount(shortage.Id, 25, _t.User, _t.Now);   // recuento: corrige la línea
        count.RecordCount(fresh.Id, 7, _t.User, _t.Now);
        count.RecordCount(same.Id, 10, _t.User, _t.Now);
        var levels = new[] { surplus, shortage, fresh, same }.ToDictionary(l => l.Id);
        var items = new Dictionary<Guid, CountItemInfo>
        {
            [surplus.Id] = new("FER-003", TestData.Und, true),
            [shortage.Id] = new("FER-004", TestData.Und, true),
            [fresh.Id] = new("NEW-001", TestData.Und, false),
            [same.Id] = new("FER-005", TestData.Und, true),
        };
        var movements = count.Post(levels, items, _t.CountTypes, _t.User, _t.Now);
        Assert.Equal(3, movements.Count);
        Assert.Equal(PhysicalCountStatus.Posted, count.Status);
        Assert.Equal(28, surplus.QuantityOnHand);
        Assert.Equal(25, shortage.QuantityOnHand);
        Assert.Equal(7, fresh.QuantityOnHand);
        Assert.All(movements, m => Assert.Equal("CF-20260925", m.DocumentReference));
        Assert.All(movements, m => Assert.Equal(count.Id, m.CorrelationId));
        var shortageMovement = movements.Single(m => m.StockLevelId == shortage.Id);
        Assert.Equal("Toma física del 25/09/2026: sistema 26, contado 25 UND (diferencia -1)", shortageMovement.Notes);
        Assert.StartsWith("Toma física del 25/09/2026: saldo inicial contado 7 UND", movements.Single(m => m.StockLevelId == fresh.Id).Notes);
        Assert.Equal(-1, count.Lines.Single(l => l.StockLevelId == shortage.Id).Difference);
        Assert.Throws<DomainException>(() => count.RecordCount(same.Id, 1, _t.User, _t.Now));
    }

    [Fact]
    public void Un_conteo_invalido_bloquea_todo_y_no_registra_nada()
    {
        var a = _t.Level(10);
        var b = _t.Level(10);
        var count = PhysicalCount.Open(_t.Tenant, Guid.NewGuid(), _t.Today);
        count.RecordCount(a.Id, 12, _t.User, _t.Now);
        count.RecordCount(b.Id, 2.5m, _t.User, _t.Now);
        var ex = Assert.Throws<DomainException>(() => count.Post(new[] { a, b }.ToDictionary(l => l.Id),
            new Dictionary<Guid, CountItemInfo> { [a.Id] = new("A", TestData.Und, true), [b.Id] = new("B", TestData.Und, true) },
            _t.CountTypes, _t.User, _t.Now));
        Assert.Equal("count.invalid", ex.Code);
        Assert.Contains("B (UND no admite decimales)", ex.Message);
        Assert.Equal(10, a.QuantityOnHand);
        Assert.Equal(PhysicalCountStatus.Open, count.Status);
    }
}

public sealed class CatalogTests
{
    private readonly TestData _t = new();

    [Fact]
    public void Un_producto_nace_con_su_variante_por_defecto_con_el_SKU_de_la_V21()
    {
        var product = _t.Product("fer-001");
        Assert.Equal("FER-001", product.Code);
        var variant = Assert.Single(product.Variants);
        Assert.True(variant.IsDefault);
        Assert.Equal("FER-001", variant.Sku);
        Assert.Same(variant, product.DefaultVariant);
    }

    [Fact]
    public void Las_variantes_tienen_SKU_unico_y_desactivar_el_producto_las_desactiva()
    {
        var product = _t.Product();
        product.AddVariant("FER-001-R", "Rojo");
        Assert.Equal("product.duplicate_sku", Assert.Throws<DomainException>(() => product.AddVariant("fer-001-r", null)).Code);
        product.Deactivate();
        Assert.All(product.Variants, v => Assert.False(v.IsActive));
        Assert.Equal(StockStatusCode.Inactive, product.EvaluateStockStatus(50, 10, 100, 0.2m));
    }

    [Fact]
    public void Empaques_e_impuestos_sin_duplicados()
    {
        var product = _t.Product();
        var box = new UnitOfMeasure(_t.Tenant, "CAJA", "Caja", false, null);
        product.DefinePackaging(box, 100);
        product.DefinePackaging(box, 50);
        Assert.Equal(50, Assert.Single(product.Packagings).FactorToBase);
        var iva = new Tax(_t.Tenant, "IVA", "IVA");
        product.AssignTax(iva);
        product.AssignTax(iva);
        Assert.Single(product.Taxes);
    }

    [Fact]
    public void Codigos_de_barras_EAN13_con_digito_de_control()
    {
        var ean = new BarcodeType(_t.Tenant, "EAN13", "EAN-13", 13, true);
        var variant = _t.Product().DefaultVariant;
        var b = variant.AddBarcode(ean, "7501031311309");
        Assert.True(b.IsPrimary);
        Assert.Equal("barcode.check_digit", Assert.Throws<DomainException>(() => variant.AddBarcode(ean, "7501031311308")).Code);
        Assert.Equal("barcode.length", Assert.Throws<DomainException>(() => variant.AddBarcode(ean, "123")).Code);
        Assert.Equal('9', BarcodeRules.ComputeGtinCheckDigit("750103131130"));
    }

    [Fact]
    public void Un_valor_por_atributo_en_cada_variante()
    {
        var variant = _t.Product().DefaultVariant;
        var color = new CatalogAttribute(_t.Tenant, "Color");
        var red = new CatalogAttributeValue(_t.Tenant, color.Id, "Rojo");
        var blue = new CatalogAttributeValue(_t.Tenant, color.Id, "Azul");
        variant.AssignAttributeValue(red, []);
        Assert.Equal("variant.attribute_duplicate", Assert.Throws<DomainException>(() => variant.AssignAttributeValue(blue, [red])).Code);
    }

    [Fact]
    public void Politica_de_stock_minimo_maximo_y_cantidad_a_pedir()
    {
        var policy = new ProductStockPolicy(_t.Tenant, Guid.NewGuid(), Guid.NewGuid(), 20, 120);
        Assert.Equal(98, policy.SuggestedOrderQuantity(22));
        Assert.Equal(120, policy.SuggestedOrderQuantity(-5));
        Assert.Equal("policy.range", Assert.Throws<DomainException>(() => policy.Define(30, 10)).Code);
    }

    [Fact]
    public void El_arbol_de_categorias_mantiene_la_clausura()
    {
        var root = new Category(_t.Tenant, "FER", "Ferretería");
        var child = new Category(_t.Tenant, "FER-TOR", "Tornillería");
        var rootRows = CategoryTree.ForNewCategory(root, []);
        var childRows = CategoryTree.ForNewCategory(child, rootRows);
        Assert.Single(rootRows);
        Assert.Equal(2, childRows.Count);
        Assert.Contains(childRows, r => r.AncestorId == root.Id && r.DescendantId == child.Id && r.Depth == 1);
    }
}

public sealed class SalesAndAccountingTests
{
    private readonly TestData _t = new();

    [Fact]
    public void Turno_de_caja_abre_registra_efectivo_y_cierra_con_arqueo()
    {
        var session = PosSession.Open(_t.Tenant, Guid.NewGuid(), _t.User, 500, _t.Now);
        var cashIn = session.RegisterCash(CashDirection.In, 100, "Cambio", _t.User, _t.Now);
        Assert.Equal(100, cashIn.Amount);
        session.Close(_t.User, 1_850, _t.Now.AddHours(8));
        Assert.Equal(PosSessionStatus.Closed, session.Status);
        Assert.Equal(-50, session.CashDifference(session.ExpectedCash(cashSales: 1_300, cashIn: 100, cashOut: 0)));
        Assert.Throws<DomainException>(() => session.RegisterCash(CashDirection.Out, 1, "x", _t.User, _t.Now));
    }

    [Fact]
    public void Un_asiento_solo_se_contabiliza_si_cuadra_y_luego_es_inmutable()
    {
        var entry = new JournalEntry(_t.Tenant, "AS-0001", Guid.NewGuid(), _t.Today, "Ajuste de inventario", Guid.NewGuid());
        entry.Debit(Guid.NewGuid(), 100);
        entry.Credit(Guid.NewGuid(), 90);
        Assert.Equal("journal.unbalanced", Assert.Throws<DomainException>(() => entry.Post(_t.User, _t.Now)).Code);
        entry.Credit(Guid.NewGuid(), 10);
        entry.Post(_t.User, _t.Now);
        Assert.Equal(JournalEntryStatus.Posted, entry.Status);
        Assert.Throws<DomainException>(() => entry.Debit(Guid.NewGuid(), 1));
    }
}
