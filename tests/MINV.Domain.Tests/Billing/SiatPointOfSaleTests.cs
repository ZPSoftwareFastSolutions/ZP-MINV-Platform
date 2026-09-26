using MINV.Domain.Billing;
using MINV.Domain.Common;

namespace MINV.Domain.Tests.Billing;

/// <summary>Punto de venta del SIN: modo de operación (en línea, fuera de línea, contingencia manual, recuperando), fallos
/// de comunicación, espera entre verificaciones y vigencias de CUIS y CUFD.</summary>
public sealed class SiatPointOfSaleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 20, 0, 0, TimeSpan.Zero);

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _branch = Guid.NewGuid();

    private SiatPointOfSale Pos(int code = 1) =>
        new(_tenant, _branch, SiatCodes.EnvironmentTest, code, SiatCodes.PointOfSaleCashier, "Caja 1", "Caja principal", Guid.NewGuid(), Now);

    [Fact]
    public void Nace_en_linea_y_sin_fallos()
    {
        var pos = Pos();
        Assert.Equal(SiatConnectionMode.Online, pos.Mode);
        Assert.True(pos.IsOnline);
        Assert.False(pos.EmitsOffline);
        Assert.False(pos.IsClosed);
        Assert.Equal(Now, pos.ModeSince);
        Assert.Equal(0, pos.ConsecutiveFailures);
        Assert.Null(pos.RetryAt);
        Assert.Equal((_branch, SiatCodes.EnvironmentTest, 1), (pos.BranchId, pos.Environment, pos.Code));
    }

    [Fact]
    public void Dos_fallos_seguidos_piden_pasar_a_fuera_de_linea()
    {
        var pos = Pos();
        Assert.False(pos.RecordFailure("Tiempo de espera agotado", Now));
        Assert.Equal(1, pos.ConsecutiveFailures);
        Assert.Equal(SiatConnectionMode.Online, pos.Mode);
        Assert.True(pos.RecordFailure("Tiempo de espera agotado", Now.AddSeconds(5)));
        Assert.Equal(2, pos.ConsecutiveFailures);
        Assert.Equal("Tiempo de espera agotado", pos.LastError);

        pos.GoOffline(Now.AddSeconds(5));
        Assert.Equal(SiatConnectionMode.Offline, pos.Mode);
        Assert.True(pos.EmitsOffline);
        Assert.False(pos.IsOnline);
        Assert.Equal(Now.AddSeconds(5), pos.ModeSince);
        Assert.Equal(Now.AddSeconds(5).AddMinutes(1), pos.RetryAt);
    }

    [Fact]
    public void Fuera_de_linea_cada_fallo_alarga_la_espera_sin_volver_a_pedir_el_cambio()
    {
        var pos = Pos();
        pos.RecordFailure("sin red", Now);
        pos.RecordFailure("sin red", Now);
        pos.GoOffline(Now);
        Assert.False(pos.RecordFailure("sin red", Now.AddMinutes(1)));    // 3.er fallo: 4 minutos
        Assert.Equal(Now.AddMinutes(5), pos.RetryAt);
        Assert.False(pos.RecordFailure("sin red", Now.AddMinutes(5)));    // 4.º fallo: 8 minutos
        Assert.Equal(Now.AddMinutes(13), pos.RetryAt);
        pos.GoOffline(Now.AddMinutes(6));                                 // idempotente: no reinicia el modo
        Assert.Equal(Now, pos.ModeSince);
    }

    [Fact]
    public void Con_un_umbral_distinto_hacen_falta_mas_fallos()
    {
        var pos = Pos();
        Assert.False(pos.RecordFailure("x", Now, threshold: 3));
        Assert.False(pos.RecordFailure("x", Now, threshold: 3));
        Assert.True(pos.RecordFailure("x", Now, threshold: 3));
    }

    [Fact]
    public void Un_contacto_exitoso_reinicia_el_contador_de_fallos()
    {
        var pos = Pos();
        Assert.False(pos.RecordFailure("x", Now));
        pos.RecordContact(Now.AddSeconds(1));
        Assert.Equal(0, pos.ConsecutiveFailures);
        Assert.Null(pos.LastError);
        Assert.Equal(Now.AddSeconds(1), pos.LastContactAt);
        Assert.False(pos.RecordFailure("x", Now.AddSeconds(2)));   // vuelve a contar desde cero
    }

    [Fact]
    public void El_error_se_recorta_a_500_caracteres()
    {
        var pos = Pos();
        pos.RecordFailure(new string('e', 800), Now);
        Assert.Equal(500, pos.LastError!.Length);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(7, 64)]
    [InlineData(8, 120)]
    [InlineData(50, 120)]
    [InlineData(int.MaxValue, 120)]
    public void La_espera_entre_verificaciones_crece_y_nunca_supera_2_horas(int attempt, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), SiatPointOfSale.RetryDelay(attempt));
        Assert.True(SiatPointOfSale.RetryDelay(attempt) <= FiscalRules.MaxOfflineRetryInterval);
    }

    [Fact]
    public void Recuperacion_de_fuera_de_linea_a_en_linea()
    {
        var pos = Pos();
        pos.GoOffline(Now);
        pos.StartRecovery(Now.AddMinutes(30));
        Assert.Equal(SiatConnectionMode.Recovering, pos.Mode);
        Assert.True(pos.EmitsOffline);   // mientras se registran el evento y los paquetes se sigue emitiendo fuera de línea
        Assert.Null(pos.RetryAt);
        pos.StartRecovery(Now.AddMinutes(31));   // ya recuperando: no cambia
        Assert.Equal(Now.AddMinutes(30), pos.ModeSince);

        pos.BackOnline(Now.AddMinutes(35));
        Assert.Equal(SiatConnectionMode.Online, pos.Mode);
        Assert.Equal(0, pos.ConsecutiveFailures);
        Assert.Equal(Now.AddMinutes(35), pos.LastContactAt);
        Assert.Null(pos.LastError);

        pos.StartRecovery(Now.AddMinutes(40));   // en línea no hay nada que recuperar
        Assert.Equal(SiatConnectionMode.Online, pos.Mode);
    }

    [Fact]
    public void Contingencia_manual_solo_desde_en_linea()
    {
        var pos = Pos();
        pos.StartManualContingency(Now);
        Assert.Equal(SiatConnectionMode.ManualContingency, pos.Mode);
        Assert.True(pos.EmitsOffline);
        Assert.Equal("siat.mode", Assert.Throws<DomainException>(() => pos.StartManualContingency(Now)).Code);
        pos.StartRecovery(Now.AddHours(1));
        Assert.Equal(SiatConnectionMode.Recovering, pos.Mode);

        var offline = Pos();
        offline.GoOffline(Now);
        Assert.Equal("siat.mode", Assert.Throws<DomainException>(() => offline.StartManualContingency(Now)).Code);
    }

    [Fact]
    public void Cierre_definitivo_salvo_el_punto_0()
    {
        Assert.Equal("siat.pos_zero", Assert.Throws<DomainException>(() => Pos(code: 0).Close(Now)).Code);
        var pos = Pos();
        pos.Close(Now);
        Assert.True(pos.IsClosed);
        Assert.Equal("siat.pos_closed", Assert.Throws<DomainException>(() => pos.GoOffline(Now)).Code);
        Assert.Equal("siat.pos_closed", Assert.Throws<DomainException>(() => pos.StartManualContingency(Now)).Code);
    }

    [Fact]
    public void Valida_ambiente_codigo_y_tipo()
    {
        Assert.Equal("siat.environment", Assert.Throws<DomainException>(() =>
            new SiatPointOfSale(_tenant, _branch, 3, 1, 5, "Caja", null, null, Now)).Code);
        Assert.Equal("siat.pos_code", Assert.Throws<DomainException>(() =>
            new SiatPointOfSale(_tenant, _branch, 1, 10_000, 5, "Caja", null, null, Now)).Code);
        Assert.Equal("siat.pos_type", Assert.Throws<DomainException>(() =>
            new SiatPointOfSale(_tenant, _branch, 1, 1, 100, "Caja", null, null, Now)).Code);
        var zero = new SiatPointOfSale(_tenant, _branch, SiatCodes.EnvironmentProduction, 0, 0, "Sin punto de venta", null, null, Now);
        Assert.Equal(0, zero.Code);
    }

    [Fact]
    public void Renombrar_y_vincular_la_caja()
    {
        var pos = Pos();
        pos.Rename("Caja 2", null);
        pos.LinkRegister(null);
        Assert.Equal("Caja 2", pos.Name);
        Assert.Null(pos.Description);
        Assert.Null(pos.PosRegisterId);
    }

    [Fact]
    public void CUIS_vigente_365_dias_y_renovable_desde_5_dias_antes()
    {
        var cuis = new SiatCuis(_tenant, _branch, Guid.NewGuid(), "C2FC9CE3", Now.AddDays(365), Now);
        Assert.True(cuis.IsValidAt(Now.AddDays(364)));
        Assert.False(cuis.IsValidAt(Now.AddDays(365)));
        Assert.False(cuis.IsRenewableAt(Now.AddDays(359)));
        Assert.True(cuis.IsRenewableAt(Now.AddDays(360)));
        Assert.Equal("siat.cuis_validity", Assert.Throws<DomainException>(() =>
            new SiatCuis(_tenant, _branch, Guid.NewGuid(), "C2FC9CE3", Now, Now)).Code);
    }

    [Fact]
    public void CUFD_de_24_horas_usable_fuera_de_linea_hasta_72_horas_desde_su_obtencion()
    {
        var cufd = new SiatCufd(_tenant, _branch, Guid.NewGuid(), Guid.NewGuid(), "BQUE+QytqQUDBKVUFOSVRPQkxVRFZNVFVJBMDAwMDAwM",
            "67A75AC82F24C74", "AV. JORGE LOPEZ #123", Now.AddHours(24), Now);
        Assert.True(cufd.IsValidAt(Now.AddHours(23)));
        Assert.False(cufd.IsValidAt(Now.AddHours(24)));
        Assert.True(cufd.IsUsableOfflineAt(Now.AddHours(71)));
        Assert.False(cufd.IsUsableOfflineAt(Now.AddHours(72)));
        Assert.Equal("siat.cufd_validity", Assert.Throws<DomainException>(() =>
            new SiatCufd(_tenant, _branch, Guid.NewGuid(), Guid.NewGuid(), "X", "Y", "Z", Now.AddHours(-1), Now)).Code);
    }
}
