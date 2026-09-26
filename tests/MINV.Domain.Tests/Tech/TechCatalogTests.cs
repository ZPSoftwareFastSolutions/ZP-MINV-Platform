using MINV.Domain.Catalog;
using MINV.Domain.Common;

namespace MINV.Domain.Tests.Tech;

/// <summary>V4.2 · Control por serie del producto (regla T-02) y orden de las opciones de una especificación.</summary>
public sealed class TechCatalogTests
{
    private static Product NewProduct(TrackingMode mode = TrackingMode.None) =>
        Product.Create(Guid.NewGuid(), "GPU-4060", "Tarjeta de video RTX 4060", Guid.NewGuid(), Guid.NewGuid(), mode);

    [Fact]
    public void Pasar_a_lleva_serie_exige_que_cada_unidad_en_stock_tenga_su_serie()
    {
        var product = NewProduct();
        Assert.Equal("tech.serials_pending", Assert.Throws<DomainException>(() => product.ChangeTracking(TrackingMode.Serial, 3, 0)).Code);
        Assert.Equal(TrackingMode.None, product.TrackingMode);
        product.ChangeTracking(TrackingMode.Serial, 0, 3);
        Assert.Equal(TrackingMode.Serial, product.TrackingMode);
    }

    [Fact]
    public void Dejar_la_serie_exige_que_no_queden_unidades_serializadas_en_stock_ni_en_transito()
    {
        var product = NewProduct(TrackingMode.Serial);
        Assert.Equal("tech.serials_on_hand", Assert.Throws<DomainException>(() => product.ChangeTracking(TrackingMode.None, 0, 2)).Code);
        product.ChangeTracking(TrackingMode.Serial, 5, 2);   // sin cambio: no valida nada
        product.ChangeTracking(TrackingMode.None, 0, 0);
        Assert.Equal(TrackingMode.None, product.TrackingMode);
    }

    [Fact]
    public void Una_opcion_cambia_de_orden_sin_cambiar_su_valor()
    {
        var option = new SpecOption(Guid.NewGuid(), Guid.NewGuid(), "DDR5", 2);
        option.Reorder(1);
        Assert.Equal(1, option.SortOrder);
        Assert.Equal("DDR5", option.Value);
    }
}
