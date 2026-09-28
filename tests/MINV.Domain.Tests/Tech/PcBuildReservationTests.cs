using MINV.Domain.Catalog;
using MINV.Domain.Common;
using MINV.Domain.Events;
using MINV.Domain.Inventory;
using MINV.Domain.Sales;

namespace MINV.Domain.Tests.Tech;

/// <summary>V6 · Reservas de armados (reglas S-03 a S-06): canal web con contacto obligatorio y teléfono boliviano, estados
/// Quoted → Reserved → Sold | Cancelled, vencimiento, publicación, bitácora append-only de cada cambio y eventos de dominio; el
/// arco de origen de las reservas de stock y el consumo al vender.</summary>
public sealed class PcBuildReservationTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Branch = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 27);
    private static readonly PcCompatibilityReport Compatible = new([], 255, 350, 650);

    private static PcBuild Quoted(PcBuildChannel channel = PcBuildChannel.Desktop)
    {
        var build = channel == PcBuildChannel.Web
            ? PcBuild.CreateWeb(Tenant, Branch, "ARM-WEB-000001", "Armado web de Ana", "Ana Quispe", "+591 71234567", "ana@correo.example", "Paso el sábado",
                Today.AddDays(2), User, Now)
            : new PcBuild(Tenant, Branch, "ARM-CM-000001", "PC Gamer", null, Today.AddDays(7), User, Now);
        build.AddLine(PcSlot.Cpu, Guid.NewGuid(), 1, 1399m);
        build.AddLine(PcSlot.Ram, Guid.NewGuid(), 2, 749.50m);
        build.Quote(Today.AddDays(7), Today, Compatible, false, Now, User);
        return build;
    }

    [Fact]
    public void Un_armado_web_exige_nombre_y_telefono_boliviano_y_lo_normaliza()
    {
        var build = Quoted(PcBuildChannel.Web);
        Assert.Equal((PcBuildChannel.Web, "Ana Quispe", "+59171234567", "ana@correo.example"), (build.Channel, build.ContactName, build.ContactPhone, build.ContactEmail));
        Assert.True(build.MatchesPhone("71234567"));
        Assert.True(build.MatchesPhone("+591 7123-4567"));
        Assert.False(build.MatchesPhone("71234568"));
        Assert.False(build.MatchesPhone("no es un teléfono"));
        Assert.Equal("pcbuild.contact_name", Assert.Throws<DomainException>(() =>
            PcBuild.CreateWeb(Tenant, Branch, "ARM-WEB-000002", "X", " ", "71234567", null, null, Today, User, Now)).Code);
        Assert.Equal("pcbuild.contact_phone", Assert.Throws<DomainException>(() =>
            PcBuild.CreateWeb(Tenant, Branch, "ARM-WEB-000002", "X", "Ana", "12345", null, null, Today, User, Now)).Code);
        Assert.Equal("pcbuild.contact_phone", Assert.Throws<DomainException>(() =>
            PcBuild.CreateWeb(Tenant, Branch, "ARM-WEB-000002", "X", "Ana", "+1 5551234567", null, null, Today, User, Now)).Code);
        Assert.Equal("guard.email", Assert.Throws<DomainException>(() =>
            PcBuild.CreateWeb(Tenant, Branch, "ARM-WEB-000002", "X", "Ana", "71234567", "sin-arroba", null, Today, User, Now)).Code);
        Assert.Equal("22212345", PcBuild.NormalizePhone("(2) 221-2345"));   // fijo con código de área: 8 dígitos
        Assert.Null(PcBuild.NormalizePhone("  "));
        // En el escritorio el contacto es opcional
        var desktop = new PcBuild(Tenant, Branch, "ARM-CM-000009", "PC", null, Today, User, Now);
        desktop.SetContact(null, null, null, null);
        Assert.Null(desktop.ContactPhone);
    }

    [Fact]
    public void Reservar_solo_desde_una_cotizacion_vigente_y_deja_bitacora_y_evento()
    {
        var build = Quoted();
        var until = Now.AddHours(48);
        build.Reserve(Now, until, Today, User);
        Assert.Equal((PcBuildStatus.Reserved, (DateTimeOffset?)Now, (DateTimeOffset?)until), (build.Status, build.ReservedAt, build.ReservedUntil));
        Assert.True(build.IsReservationActive);
        Assert.False(build.IsReservationExpired(Now.AddHours(47)));
        Assert.True(build.IsReservationExpired(until));
        Assert.Equal([PcBuildEventAction.Created, PcBuildEventAction.Quoted, PcBuildEventAction.Reserved], build.History.Select(h => h.Action));
        Assert.All(build.History, h => Assert.Equal((Tenant, Branch, build.Id, User), (h.TenantId, h.BranchId, h.PcBuildId, h.UserId)));
        var reserved = Assert.IsType<PcBuildReservedEvent>(Assert.Single(build.DomainEvents));
        Assert.Equal((build.Number, "Desktop", 1399m + 1499m, 2), (reserved.Number, reserved.Channel, reserved.Total, reserved.Lines.Count));
        // No se reserva dos veces, ni un borrador, ni una cotización vencida, ni con vigencia pasada
        Assert.Equal("pcbuild.state", Assert.Throws<DomainException>(() => build.Reserve(Now, until, Today, User)).Code);
        var draft = new PcBuild(Tenant, Branch, "ARM-CM-000002", "Borrador", null, Today, User, Now);
        Assert.Equal("pcbuild.state", Assert.Throws<DomainException>(() => draft.Reserve(Now, until, Today, User)).Code);
        var expired = Quoted();
        Assert.Equal("pcbuild.expired", Assert.Throws<DomainException>(() => expired.Reserve(Now, until, Today.AddDays(8), User)).Code);
        Assert.Equal("pcbuild.reserved_until", Assert.Throws<DomainException>(() => Quoted().Reserve(Now, Now, Today, User)).Code);
    }

    [Fact]
    public void Liberar_o_vencer_anula_con_motivo_y_vender_consume_la_reserva()
    {
        var released = Quoted(PcBuildChannel.Web);
        released.Reserve(Now, Now.AddHours(48), Today, User);
        released.ClearDomainEvents();
        released.ReleaseReservation("Cancelada por el cliente", Now.AddHours(1), User);
        Assert.Equal((PcBuildStatus.Cancelled, "Cancelada por el cliente"), (released.Status, released.CancelReason));
        Assert.Equal(PcBuildEventAction.Released, released.History.Last().Action);
        var releasedEvent = Assert.IsType<PcBuildReleasedEvent>(Assert.Single(released.DomainEvents));
        Assert.False(releasedEvent.Expired);
        Assert.Equal("pcbuild.state", Assert.Throws<DomainException>(() => released.ReleaseReservation("otra vez", Now, User)).Code);
        Assert.Equal("pcbuild.state", Assert.Throws<DomainException>(() => released.MarkSold(Guid.NewGuid(), Today, User, Now)).Code);

        var expired = Quoted();
        expired.Reserve(Now, Now.AddHours(48), Today, User);
        expired.ReleaseReservation(PcBuild.ExpiredReason, Now.AddHours(49), User, expired: true);
        Assert.Equal((PcBuildStatus.Cancelled, PcBuild.ExpiredReason, PcBuildEventAction.Expired), (expired.Status, expired.CancelReason, expired.History.Last().Action));
        Assert.True(Assert.IsType<PcBuildReleasedEvent>(expired.DomainEvents.Last()).Expired);

        var sold = Quoted(PcBuildChannel.Web);
        sold.Reserve(Now, Now.AddHours(48), Today, User);
        sold.ClearDomainEvents();
        var invoice = Guid.NewGuid();
        sold.MarkSold(invoice, Today.AddDays(1), User, Now.AddDays(1));
        Assert.Equal((PcBuildStatus.Sold, (Guid?)invoice), (sold.Status, sold.InvoiceId));
        var soldEvent = Assert.IsType<PcBuildSoldEvent>(Assert.Single(sold.DomainEvents));
        Assert.True(soldEvent.WasReserved);
        Assert.Equal("Web", soldEvent.Channel);
        Assert.Contains("reserva consumida", sold.History.Last().Detail, StringComparison.Ordinal);
        // Un armado reservado no se anula con Cancel: se libera (lo decide el caso de uso)
        var reserved = Quoted();
        reserved.Reserve(Now, Now.AddHours(48), Today, User);
        Assert.Equal("pcbuild.state", Assert.Throws<DomainException>(() => reserved.Cancel(User, Now)).Code);
        Assert.False(reserved.IsReservationExpired(Now.AddHours(47)));
        Assert.True(reserved.IsExpiredOn(Today.AddDays(8)));   // la cotización sigue teniendo su vigencia
    }

    [Fact]
    public void Publicar_solo_armados_del_escritorio_cotizados_y_la_bitacora_lo_registra()
    {
        var build = Quoted();
        build.Publish(User, Now);
        Assert.True(build.PublishedToWeb);
        Assert.Equal("pcbuild.published", Assert.Throws<DomainException>(() => build.Publish(User, Now)).Code);
        build.Unpublish(User, Now.AddMinutes(1));
        Assert.False(build.PublishedToWeb);
        Assert.Equal("pcbuild.not_published", Assert.Throws<DomainException>(() => build.Unpublish(User, Now)).Code);
        Assert.Equal([PcBuildEventAction.Published, PcBuildEventAction.Unpublished], build.History.TakeLast(2).Select(h => h.Action));
        Assert.Equal("pcbuild.publish_channel", Assert.Throws<DomainException>(() => Quoted(PcBuildChannel.Web).Publish(User, Now)).Code);
        var draft = new PcBuild(Tenant, Branch, "ARM-CM-000003", "Borrador", null, Today, User, Now);
        Assert.Equal("pcbuild.publish_state", Assert.Throws<DomainException>(() => draft.Publish(User, Now)).Code);
        // Un armado vendido sigue pudiendo publicarse como sugerido; uno anulado se despublica solo
        var sold = Quoted();
        sold.MarkSold(Guid.NewGuid(), Today, User, Now);
        sold.Publish(User, Now);
        Assert.True(sold.PublishedToWeb);
        var cancelled = Quoted();
        cancelled.Publish(User, Now);
        cancelled.Cancel(User, Now, "Cambió de idea");
        Assert.False(cancelled.PublishedToWeb);
        Assert.Equal("Cambió de idea", cancelled.CancelReason);
    }

    [Fact]
    public void Una_reserva_admite_como_maximo_20_lineas()
    {
        var build = new PcBuild(Tenant, Branch, "ARM-CM-000004", "Muchas piezas", null, Today, User, Now);
        for (var i = 0; i < PcBuild.MaxLines; i++)
        {
            build.AddLine(PcSlot.Peripheral, Guid.NewGuid(), 1, 10m);
        }
        Assert.Equal("pcbuild.lines", Assert.Throws<DomainException>(() => build.AddLine(PcSlot.Peripheral, Guid.NewGuid(), 1, 10m)).Code);
    }

    [Fact]
    public void La_reserva_de_stock_tiene_un_solo_origen_y_cumplirla_no_mueve_la_existencia()
    {
        var t = new TestData();
        var level = t.Level(10);
        var lineId = Guid.NewGuid();
        var reservation = level.Reserve(3, t.Now.AddHours(48), t.Now, pcBuildLineId: lineId);
        Assert.Equal((lineId, (Guid?)null, (Guid?)null, 7m), (reservation.PcBuildLineId, reservation.PosSessionId, reservation.SalesOrderLineId, level.Available));
        Assert.Equal("reservation.origin", Assert.Throws<DomainException>(() =>
            level.Reserve(1, t.Now.AddHours(1), t.Now, posSessionId: Guid.NewGuid(), pcBuildLineId: Guid.NewGuid())).Code);
        Assert.Equal("reservation.origin", Assert.Throws<DomainException>(() =>
            level.Reserve(1, t.Now.AddHours(1), t.Now, salesOrderLineId: Guid.NewGuid(), pcBuildLineId: Guid.NewGuid())).Code);
        // Cumplir la reserva al vender: la cantidad reservada vuelve y la existencia la baja la venta (una sola salida)
        level.Fulfill(reservation);
        Assert.Equal((ReservationStatus.Consumed, 10m, 10m), (reservation.Status, level.QuantityOnHand, level.Available));
        Assert.Equal("reservation.closed", Assert.Throws<DomainException>(() => level.Fulfill(reservation)).Code);
        Assert.Equal("reservation.closed", Assert.Throws<DomainException>(() => level.Release(reservation)).Code);
    }
}
