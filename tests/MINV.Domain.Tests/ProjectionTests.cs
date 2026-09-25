using MINV.Domain.Inventory;

namespace MINV.Domain.Tests;

/// <summary>Proyección de lectura (15_STOCK, 16_ALERTAS, 18_PEDIDO). La paridad completa con los datos de la V2.1 se
/// prueba en MINV.Infrastructure.Tests con el libro colaborativo real.</summary>
public sealed class ProjectionTests
{
    private static readonly DateOnly Today = new(2026, 9, 25);
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    private static readonly ProjectionItem[] Items =
    [
        new(A, 0, "A-1", "Producto A", "CAT", "Proveedor 2", "UND", true, 10, 50, 100),
        new(B, 1, "B-1", "Producto B", "CAT", "Proveedor 1", "UND", true, 10, 0, 50),
        new(C, 2, "C-1", "Producto C", "CAT", "", "UND", true, 5, 20, 10),
    ];

    private static readonly ProjectionSupplier[] Suppliers =
    [
        new("Proveedor 1", 0, 3, "Ana", "600", "a@x.example"),
        new("Proveedor 2", 1, 7, "Luis", "700", "l@x.example"),
    ];

    private static StockProjectionResult Project() => StockProjection.Project(Items,
    [
        new(A, 20, Today.AddDays(-40), false), new(A, -12, Today.AddDays(-5), true),   // A: stock 8 → crítico
        new(B, 5, Today.AddDays(-2), false),                                         // B: stock 5 → crítico, sin ventas
        new(C, 30, Today.AddDays(-60), false), new(C, -30, Today.AddDays(-1), true),  // C: agotado
    ], 0.2m, Today, Suppliers);

    [Fact]
    public void Stock_ventas_de_30_dias_cobertura_y_ranking()
    {
        var r = Project();
        var a = r.Stock.Single(x => x.Sku == "A-1");
        Assert.Equal(8, a.Stock);
        Assert.Equal(12, a.Sales30Days);
        Assert.Equal(20, a.CoverageDays);           // 8 / (12 / 30)
        Assert.Equal(StockStatusCode.Critical, a.Status);
        Assert.Equal(800, a.InventoryValue);
        Assert.Equal(5, a.DaysWithoutMovement);
        Assert.Equal(1, r.Stock.Single(x => x.Sku == "C-1").SalesRank);   // más vendido
        Assert.Equal(2, a.SalesRank);
        Assert.Null(r.Stock.Single(x => x.Sku == "B-1").SalesRank);
        Assert.Equal(5, r.Movements);
    }

    [Fact]
    public void Alertas_priorizadas_agotado_antes_que_critico()
    {
        var r = Project();
        Assert.Equal(["C-1", "B-1", "A-1"], r.Alerts.Select(x => x.Sku));
        Assert.Equal([1, 2, 3], r.Alerts.Select(x => x.Position));
        Assert.Equal(20, r.Alerts[0].SuggestedQuantity);
    }

    [Fact]
    public void Pedido_agrupado_por_proveedor_en_su_orden_y_sin_proveedor_al_final()
    {
        var r = Project();
        Assert.Equal(["B-1", "A-1", "C-1"], r.Order.Select(x => x.Sku));
        Assert.Equal(StockProjection.NoSupplier, r.Order[^1].Supplier);
        var b = r.Order[0];
        Assert.Equal(15, b.QuantityToOrder);         // sin máximo: 2 × mínimo − stock
        Assert.Equal(750, b.Subtotal);
        Assert.Equal(Today.AddDays(3), b.EstimatedDelivery);
        Assert.Equal("Ana", b.Contact);
    }
}
