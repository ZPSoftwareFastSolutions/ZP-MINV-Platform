using System.Reflection;
using MediatR;
using MINV.Application.Billing;
using MINV.Application.Common;
using MINV.Application.Integration;
using MINV.Application.Sales;
using MINV.Application.Tech;
using MINV.Domain.Billing;
using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Inventory;

namespace MINV.Application.Tests.Tech;

/// <summary>V4.2 · Reglas de la aplicación de la edición Tecnología que no necesitan base de datos: series en la factura
/// del SIN (T-03), hash de idempotencia de los pedidos con series, números de las fichas y la tubería de los contratos.</summary>
public sealed class TechRulesTests
{
    private static FiscalLineInput Line(decimal quantity, decimal price, decimal? discount) =>
        new(Guid.NewGuid(), "474110", 12345, "SSD-1TB", "SSD NVMe 1 TB", quantity, 57, price, discount);

    [Fact]
    public void Las_series_que_caben_van_en_una_sola_linea_separadas_por_coma()
    {
        var lines = FiscalIssuer.WithSerials(Line(3, 879m, null), ["A1", "B2", "C3"], SerialKind.Serial);
        var line = Assert.Single(lines);
        Assert.Equal("A1, B2, C3", line.SerialNumber);
        Assert.Null(line.Imei);
        Assert.Equal(3m, line.Quantity);
    }

    [Theory]
    [InlineData(30, 60, 0)]
    [InlineData(30, 60, 10)]
    [InlineData(101, 80, 7.5)]
    [InlineData(7, 80, 33.33)]
    public void Si_las_series_no_caben_la_linea_se_divide_sin_cambiar_el_importe(int units, int length, decimal percent)
    {
        const decimal price = 879.90m;
        var amount = decimal.Round(units * price * (1 - percent / 100m), 2, MidpointRounding.AwayFromZero);
        var discount = FiscalRules.Round2(units * price) - amount;
        var serials = Enumerable.Range(1, units).Select(i => $"S{i:0000}".PadRight(length, 'X')).ToList();
        var lines = FiscalIssuer.WithSerials(Line(units, price, discount > 0 ? discount : null), serials, SerialKind.Serial);
        Assert.All(lines, l => Assert.True(l.SerialNumber!.Length <= FiscalIssuer.MaxSerialText));
        Assert.Equal(units, lines.Sum(l => l.Quantity));
        Assert.Equal(amount, lines.Sum(l => FiscalRules.LineSubtotal(l.Quantity, l.UnitPrice, l.Discount)));
        Assert.Equal(serials, lines.SelectMany(l => FiscalIssuer.SplitSerials(l.SerialNumber)));
        Assert.All(lines, l => Assert.Equal(price, l.UnitPrice));
        Assert.Equal(units * (length + 2) - 2 > FiscalIssuer.MaxSerialText, lines.Count > 1);
    }

    [Fact]
    public void El_IMEI_va_en_numeroImei()
    {
        var line = Assert.Single(FiscalIssuer.WithSerials(Line(1, 2999m, null), ["352099001761481"], SerialKind.Imei));
        Assert.Equal("352099001761481", line.Imei);
        Assert.Null(line.SerialNumber);
        Assert.Equal("fiscal.serials", Assert.Throws<DomainException>(() => FiscalIssuer.WithSerials(Line(2, 1m, null), ["A"], SerialKind.Serial)).Code);
    }

    [Fact]
    public void El_hash_de_un_pedido_sin_series_no_cambia_y_con_series_no_depende_del_orden()
    {
        CreateExternalOrderCommand Order(IReadOnlyList<string>? serials) => new("EXT-1", "CF", "EFECTIVO", [new SaleLineInput("GPU-4060", 2, 0, serials)]);
        var plain = CreateExternalOrderHandler.RequestHash(Order(null));
        Assert.Equal(plain, CreateExternalOrderHandler.RequestHash(new CreateExternalOrderCommand("EXT-1", "cf", "efectivo", [new SaleLineInput("gpu-4060", 2)])));
        var withSerials = CreateExternalOrderHandler.RequestHash(Order(["b-2", "A-1"]));
        Assert.NotEqual(plain, withSerials);
        Assert.Equal(withSerials, CreateExternalOrderHandler.RequestHash(Order(["A-1", "B-2"])));
        Assert.NotEqual(withSerials, CreateExternalOrderHandler.RequestHash(Order(["A-1", "B-3"])));
    }

    [Theory]
    [InlineData("3,8", 3.8)]
    [InlineData("12.5", 12.5)]
    [InlineData(" 650 ", 650)]
    [InlineData("-2", -2)]
    public void Los_numeros_de_una_ficha_admiten_coma_o_punto(string text, double expected) =>
        Assert.Equal((decimal)expected, TechCatalogReader.ParseNumber(text));

    [Theory]
    [InlineData("ocho")]
    [InlineData("")]
    [InlineData("1,000.5,3")]
    public void Un_texto_no_numerico_no_es_un_numero(string text) => Assert.Null(TechCatalogReader.ParseNumber(text));

    [Fact]
    public void El_IMEI_invalido_usa_el_codigo_estable_de_la_aplicacion() =>
        Assert.Equal(SerialErrorCodes.ImeiInvalid, Assert.Throws<DomainException>(() => SerialNumber.Normalize(SerialKind.Imei, "123")).Code);

    [Fact]
    public void Los_contratos_de_la_edicion_Tecnologia_exigen_permiso_y_los_comandos_se_auditan()
    {
        var requests = typeof(SkuSerials).Assembly.GetTypes()
            .Where(t => t.Namespace == "MINV.Application.Tech" && t.IsPublic && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
            .ToList();
        Assert.True(requests.Count >= 26, $"Solo {requests.Count} contratos");
        Assert.All(requests, r => Assert.NotEmpty(r.GetCustomAttributes<RequiresPermissionAttribute>()));
        var commands = requests.Where(r => r.Name.EndsWith("Command", StringComparison.Ordinal)).ToList();
        Assert.All(commands, c => Assert.True(typeof(IAuditableRequest).IsAssignableFrom(c), c.Name));
        Assert.Contains(typeof(SellPcBuildCommand), commands);
        Assert.Equal([Domain.Iam.PermissionCodes.PosOperate, Domain.Iam.PermissionCodes.MovementsRegisterSales],
            typeof(SellPcBuildCommand).GetCustomAttributes<RequiresPermissionAttribute>().Select(a => a.Permission));
    }
}
