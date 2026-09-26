using MINV.Domain.Billing;
using MINV.Domain.Common;

namespace MINV.Domain.Tests.Billing;

/// <summary>Eventos significativos (cierre, registro, plazos de 48 y 72 horas), paquetes de contingencia y CAFC.</summary>
public sealed class ContingencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Start = new(2026, 9, 25, 10, 0, 0, 123);

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _branch = Guid.NewGuid();
    private readonly Guid _pos = Guid.NewGuid();

    private SignificantEvent Event(SignificantEventKind kind = SignificantEventKind.Offline, Guid? cafc = null, int code = 2) =>
        new(_tenant, _branch, _pos, SiatCodes.EnvironmentTest, kind, code, "INACCESIBILIDAD AL SERVICIO WEB DE LA ADMINISTRACIÓN TRIBUTARIA",
            DateTime.SpecifyKind(Start, DateTimeKind.Local), Guid.NewGuid(), cafc, Now, Guid.NewGuid());

    [Fact]
    public void El_evento_nace_abierto_y_cubre_desde_su_inicio()
    {
        var e = Event();
        Assert.Equal(SignificantEventStatus.Open, e.Status);
        Assert.Equal(DateTimeKind.Unspecified, e.StartedAt.Kind);
        Assert.True(e.Covers(Start));
        Assert.True(e.Covers(Start.AddDays(3)));   // abierto: sin fin
        Assert.False(e.Covers(Start.AddMilliseconds(-1)));
        Assert.Null(e.EndedAt);
        Assert.Null(e.RegistrationDeadline);
        Assert.Null(e.TranscriptionDeadline);
    }

    [Fact]
    public void Al_cerrar_corre_el_plazo_de_48_horas_para_registrarlo()
    {
        var e = Event();
        var end = Start.AddHours(3);
        e.Close(end);
        Assert.Equal(SignificantEventStatus.Closed, e.Status);
        Assert.Equal(end, e.EndedAt);
        Assert.Equal(end.AddHours(48), e.RegistrationDeadline);
        Assert.Null(e.TranscriptionDeadline);   // solo la contingencia manual transcribe facturas
        Assert.True(e.Covers(end));
        Assert.False(e.Covers(end.AddMilliseconds(1)));
        Assert.Equal("siat.event_state", Assert.Throws<DomainException>(() => e.Close(end.AddHours(1))).Code);
    }

    [Fact]
    public void El_fin_debe_ser_posterior_al_inicio()
    {
        Assert.Equal("siat.event_range", Assert.Throws<DomainException>(() => Event().Close(Start)).Code);
        Assert.Equal("siat.event_range", Assert.Throws<DomainException>(() => Event().Close(Start.AddMinutes(-1))).Code);
    }

    [Fact]
    public void Contingencia_manual_con_CAFC_y_72_horas_para_transcribir()
    {
        var e = Event(SignificantEventKind.ManualCafc, cafc: Guid.NewGuid(), code: 5);
        var end = Start.AddHours(6);
        e.Close(end);
        Assert.Equal(end.AddHours(48), e.RegistrationDeadline);
        Assert.Equal(end.AddHours(72), e.TranscriptionDeadline);
        Assert.NotNull(e.ContingencyCodeId);
    }

    [Fact]
    public void Solo_la_contingencia_manual_usa_CAFC_y_el_codigo_sale_del_catalogo()
    {
        Assert.Equal("siat.event_cafc", Assert.Throws<DomainException>(() => Event(SignificantEventKind.Offline, cafc: Guid.NewGuid())).Code);
        Assert.Equal("siat.event_code", Assert.Throws<DomainException>(() => Event(code: 0)).Code);
        Assert.Equal("siat.event_code", Assert.Throws<DomainException>(() => Event(code: 100)).Code);
    }

    [Fact]
    public void Registro_con_el_CUFD_nuevo_y_luego_paquetes_y_conciliacion()
    {
        var e = Event();
        var sendCufd = Guid.NewGuid();
        Assert.Equal("siat.event_state", Assert.Throws<DomainException>(() => e.Register(sendCufd, "REC-1", Now)).Code);
        Assert.Equal("siat.event_state", Assert.Throws<DomainException>(() => e.MarkPackagesSent()).Code);
        e.Close(Start.AddHours(1));
        Assert.Equal("siat.event_state", Assert.Throws<DomainException>(() => e.Reconcile(false)).Code);

        e.Register(sendCufd, "1234567", Now.AddHours(1));
        Assert.Equal(SignificantEventStatus.Registered, e.Status);
        Assert.Equal((sendCufd, "1234567", Now.AddHours(1)), (e.SendCufdId!.Value, e.ReceptionCode, e.RegisteredAt!.Value));
        Assert.NotEqual(e.EventCufdId, e.SendCufdId);
        Assert.Equal("siat.event_state", Assert.Throws<DomainException>(() => e.Register(sendCufd, "otro", Now)).Code);

        e.MarkPackagesSent();
        e.MarkPackagesSent();   // varios paquetes del mismo evento
        Assert.Equal(SignificantEventStatus.PackagesSent, e.Status);
        e.Reconcile(withObservations: false);
        Assert.Equal(SignificantEventStatus.Reconciled, e.Status);

        var observed = Event();
        observed.Close(Start.AddHours(1));
        observed.Register(Guid.NewGuid(), "7654321", Now);
        observed.Reconcile(withObservations: true);
        Assert.Equal(SignificantEventStatus.WithObservations, observed.Status);
    }

    [Fact]
    public void Paquete_con_un_solo_resultado_de_validacion()
    {
        var package = new FiscalPackage(_tenant, _branch, Guid.NewGuid(), _pos, Guid.NewGuid(), SiatCodes.SectorPurchaseSale,
            SiatCodes.InvoiceWithTaxCredit, null, new string('F', 64), Now);
        Assert.Equal(new string('f', 64), package.Sha256);
        Assert.Equal(FiscalPackageStatus.Sent, package.Status);
        package.Accepted("REC-PAQ", SiatCodes.ReceptionPending);
        Assert.Equal(("REC-PAQ", SiatCodes.ReceptionPending), (package.ReceptionCode, package.LastSiatCode!.Value));
        package.Validated(SiatCodes.ReceptionValidated, Now, null);
        Assert.Equal(FiscalPackageStatus.Validated, package.Status);
        Assert.Equal(Now, package.ValidatedAt);
        Assert.Equal("siat.package_state", Assert.Throws<DomainException>(() => package.Observed(904, Now, "[]")).Code);
        Assert.Equal("siat.package_state", Assert.Throws<DomainException>(() => package.Rejected(902, Now, "[]")).Code);

        var observed = new FiscalPackage(_tenant, _branch, Guid.NewGuid(), _pos, Guid.NewGuid(), 1, 1, "1011917833B0D", new string('a', 64), Now);
        observed.Observed(SiatCodes.ReceptionObserved, Now, """[{"codigo":1013,"archivo":2}]""");
        Assert.Equal((FiscalPackageStatus.Observed, "1011917833B0D"), (observed.Status, observed.Cafc));
        Assert.Equal("guard.text", Assert.Throws<DomainException>(() =>
            new FiscalPackage(_tenant, _branch, Guid.NewGuid(), _pos, Guid.NewGuid(), 1, 1, null, "abc", Now)).Code);
    }

    [Fact]
    public void CAFC_con_rango_de_numeros()
    {
        var cafc = new ContingencyCode(_tenant, _branch, SiatCodes.SectorPurchaseSale, "1011917833B0D", 1, 50, new DateOnly(2027, 1, 1));
        Assert.True(cafc.IsActive);
        Assert.True(cafc.Contains(1));
        Assert.True(cafc.Contains(50));
        Assert.False(cafc.Contains(51));
        cafc.Deactivate();
        Assert.False(cafc.IsActive);
        Assert.Equal("siat.cafc_range", Assert.Throws<DomainException>(() =>
            new ContingencyCode(_tenant, _branch, 1, "X", 10, 9, null)).Code);
        Assert.Equal("siat.sector", Assert.Throws<DomainException>(() =>
            new ContingencyCode(_tenant, _branch, 100, "X", 1, 9, null)).Code);
    }
}
