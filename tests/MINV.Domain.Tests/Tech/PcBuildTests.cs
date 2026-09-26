using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Sales;

namespace MINV.Domain.Tests.Tech;

/// <summary>V4.2 · Armado de PC (regla T-06): líneas por ranura, cotizar (precios congelados, vigencia y confirmación de
/// un armado incompatible), vender, anular y vencimiento.</summary>
public sealed class PcBuildTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Branch = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 25);

    private static readonly PcCompatibilityReport Compatible = new([], 255, 350, 650);

    private static readonly PcCompatibilityReport Incompatible =
        new([new PcIssue("SOCKET_CPU_PLACA", true, "El socket no coincide."), new PcIssue("SIN_GRAFICOS", false, "Sin gráficos.")], 140, 200, 750);

    private static PcBuild Draft()
    {
        var build = new PcBuild(Tenant, Branch, "ARM-CM-000001", "PC Gamer Entrada", Guid.NewGuid(), Today.AddDays(7), User, Now);
        build.AddLine(PcSlot.Cpu, Guid.NewGuid(), 1, 1399m);
        build.AddLine(PcSlot.Motherboard, Guid.NewGuid(), 1, 1249m);
        build.AddLine(PcSlot.Ram, Guid.NewGuid(), 2, 749.50m);
        return build;
    }

    [Fact]
    public void Las_lineas_calculan_el_total_y_respetan_una_pieza_por_ranura_unica()
    {
        var build = Draft();
        Assert.Equal(PcBuildStatus.Draft, build.Status);
        Assert.Equal(1399m + 1249m + 1499m, build.Total);
        Assert.All(build.Lines, l => Assert.Equal((Tenant, Branch, build.Id), (l.TenantId, l.BranchId, l.PcBuildId)));
        Assert.Equal("pcbuild.slot", Assert.Throws<DomainException>(() => build.AddLine(PcSlot.Cpu, Guid.NewGuid(), 1, 1m)).Code);
        build.AddLine(PcSlot.Ram, Guid.NewGuid(), 1, 100m);            // varias memorias sí
        build.AddLine(PcSlot.Storage, Guid.NewGuid(), 2, 879m);
        Assert.Equal("pcbuild.quantity", Assert.Throws<DomainException>(() => build.AddLine(PcSlot.Storage, Guid.NewGuid(), 17, 1m)).Code);
        Assert.Equal("pcbuild.quantity", Assert.Throws<DomainException>(() => build.AddLine(PcSlot.Gpu, Guid.NewGuid(), 0, 1m)).Code);
        Assert.Throws<DomainException>(() => build.AddLine(PcSlot.Gpu, Guid.NewGuid(), 1, -1m));
        var ram = build.Lines.First(l => l.Slot == PcSlot.Ram);
        build.RemoveLine(ram.Id);
        Assert.DoesNotContain(build.Lines, l => l.Id == ram.Id);
        Assert.Equal("pcbuild.line", Assert.Throws<DomainException>(() => build.RemoveLine(ram.Id)).Code);
    }

    [Fact]
    public void Cotizar_congela_precios_y_vigencia_y_ya_no_se_edita()
    {
        var build = Draft();
        build.Quote(Today.AddDays(10), Today, Compatible, acceptIncompatible: false, Now);
        Assert.Equal(PcBuildStatus.Quoted, build.Status);
        Assert.Equal(Today.AddDays(10), build.ValidUntil);
        Assert.False(build.QuotedWithErrors);
        Assert.Equal(Now, build.QuotedAt);
        Assert.Equal("pcbuild.not_draft", Assert.Throws<DomainException>(() => build.AddLine(PcSlot.Gpu, Guid.NewGuid(), 1, 3199m)).Code);
        Assert.Equal("pcbuild.not_draft", Assert.Throws<DomainException>(() => build.RemoveLine(build.Lines.First().Id)).Code);
        Assert.Equal("pcbuild.not_draft", Assert.Throws<DomainException>(() => build.Rename("Otro", null)).Code);
        Assert.Equal("pcbuild.not_draft", Assert.Throws<DomainException>(() =>
            build.Quote(Today.AddDays(10), Today, Compatible, false, Now)).Code);
    }

    [Fact]
    public void Un_armado_vacio_o_con_vigencia_pasada_no_se_cotiza()
    {
        var empty = new PcBuild(Tenant, Branch, "ARM-CM-000002", "Vacío", null, Today, User, Now);
        Assert.Equal("pcbuild.empty", Assert.Throws<DomainException>(() => empty.Quote(Today, Today, Compatible, false, Now)).Code);
        Assert.Equal("pcbuild.valid_until", Assert.Throws<DomainException>(() =>
            Draft().Quote(Today.AddDays(-1), Today, Compatible, false, Now)).Code);
    }

    [Fact]
    public void Un_armado_incompatible_solo_se_cotiza_con_confirmacion_y_queda_marcado()
    {
        var build = Draft();
        var error = Assert.Throws<DomainException>(() => build.Quote(Today.AddDays(7), Today, Incompatible, acceptIncompatible: false, Now));
        Assert.Equal("pcbuild.incompatible", error.Code);
        Assert.Contains("1 error", error.Message, StringComparison.Ordinal);
        Assert.Equal(PcBuildStatus.Draft, build.Status);
        build.Quote(Today.AddDays(7), Today, Incompatible, acceptIncompatible: true, Now);
        Assert.True(build.QuotedWithErrors);
        Assert.Equal(PcBuildStatus.Quoted, build.Status);
    }

    [Fact]
    public void Vender_vincula_la_venta_y_una_cotizacion_vencida_no_se_vende()
    {
        var build = Draft();
        build.Quote(Today.AddDays(7), Today, Compatible, false, Now);
        Assert.False(build.IsExpiredOn(Today.AddDays(7)));             // el último día todavía vale
        Assert.True(build.IsExpiredOn(Today.AddDays(8)));
        Assert.Equal("pcbuild.expired", Assert.Throws<DomainException>(() => build.MarkSold(Guid.NewGuid(), Today.AddDays(8))).Code);
        var invoice = Guid.NewGuid();
        build.MarkSold(invoice, Today.AddDays(7));
        Assert.Equal((PcBuildStatus.Sold, (Guid?)invoice), (build.Status, build.InvoiceId));
        Assert.False(build.IsExpiredOn(Today.AddDays(30)));            // vendido: ya no vence
        Assert.Equal("pcbuild.state", Assert.Throws<DomainException>(() => build.MarkSold(Guid.NewGuid(), Today)).Code);
        Assert.Equal("pcbuild.state", Assert.Throws<DomainException>(() => build.Cancel()).Code);
    }

    [Fact]
    public void Un_borrador_se_puede_vender_y_anular_solo_antes_de_venderse()
    {
        var sold = Draft();
        sold.MarkSold(Guid.NewGuid(), Today);
        Assert.Equal(PcBuildStatus.Sold, sold.Status);

        var cancelled = Draft();
        cancelled.Quote(Today, Today, Compatible, false, Now);
        cancelled.Cancel();
        Assert.Equal(PcBuildStatus.Cancelled, cancelled.Status);
        Assert.Equal("pcbuild.state", Assert.Throws<DomainException>(() => cancelled.MarkSold(Guid.NewGuid(), Today)).Code);
        Assert.Equal("pcbuild.state", Assert.Throws<DomainException>(() => cancelled.Cancel()).Code);
        Assert.False(cancelled.IsExpiredOn(Today.AddDays(1)));
    }

    [Fact]
    public void El_precio_cotizado_se_redondea_a_centavos_y_el_subtotal_es_cantidad_por_precio()
    {
        var build = new PcBuild(Tenant, Branch, "ARM-CM-000003", "Oficina", null, Today, User, Now);
        var line = build.AddLine(PcSlot.Storage, Guid.NewGuid(), 3, 333.335m);
        Assert.Equal(333.34m, line.QuotedUnitPrice);
        Assert.Equal(1000.02m, line.Subtotal);
    }
}
