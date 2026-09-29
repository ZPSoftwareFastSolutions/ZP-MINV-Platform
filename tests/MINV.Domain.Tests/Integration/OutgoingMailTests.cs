using MINV.Domain.Common;
using MINV.Domain.Integration;

namespace MINV.Domain.Tests.Integration;

/// <summary>V7 · Correo saliente (regla P-06): el hecho append-only con un destinatario que no admite cabeceras ni listas, su
/// <c>Message-ID</c> determinista, la máquina de estados de la cola (pendiente → enviado | agotado | cancelado, cinco intentos con
/// espera inmediata, 1 min, 5 min, 30 min y 2 h) y la bitácora de intentos.</summary>
public sealed class OutgoingMailTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Branch = Guid.NewGuid();
    private static readonly Guid Build = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 14, 0, 0, TimeSpan.Zero);

    private static OutgoingMail Mail(string recipient = "ana@correo.example") =>
        new(Tenant, Branch, OutgoingMailKind.ReservationConfirmed, Build, recipient, User, Now);

    private static string Code(Action action) => Assert.Throws<DomainException>(action).Code;

    [Fact]
    public void El_correo_guarda_la_reserva_y_un_solo_destinatario_en_minusculas()
    {
        var mail = Mail("  Ana.Quispe@Correo.Example ");
        Assert.Equal((Tenant, Branch, OutgoingMailKind.ReservationConfirmed, Build, "ana.quispe@correo.example", User, Now),
            (mail.TenantId, mail.BranchId, mail.Kind, mail.PcBuildId, mail.Recipient, mail.RequestedByUserId, mail.RequestedAt));
        Assert.IsAssignableFrom<IAppendOnly>(mail);
        Assert.IsAssignableFrom<IBranchScoped>(mail);
        // La hora se guarda en UTC aunque llegue con otra zona
        Assert.Equal(TimeSpan.Zero, new OutgoingMail(Tenant, Branch, OutgoingMailKind.ReservationConfirmed, Build, "a@b.example", User,
            Now.ToOffset(TimeSpan.FromHours(-4))).RequestedAt.Offset);

        // Nada con que armar otra cabecera, un nombre visible o una lista de destinatarios
        foreach (var bad in new[]
                 {
                     "", "   ", "ana", "ana@correo", "@correo.example", "ana@@correo.example", "ana quispe@correo.example",
                     "ana@correo.example\r\nBcc: otro@correo.example", "ana@correo.example\nX: y", "ana\t@correo.example",
                     "Ana <ana@correo.example>", "\"Ana\"@correo.example", "ana@correo.example, otro@correo.example",
                     "ana@correo.example;otro@correo.example", "ana(comentario)@correo.example", "ana@[127.0.0.1]",
                     new string('a', 250) + "@correo.example",
                 })
        {
            Assert.False(OutgoingMail.IsValidRecipient(bad), bad);
            Assert.Equal("mail.recipient", Code(() => Mail(bad)));
        }
        Assert.False(OutgoingMail.IsValidRecipient(null));
        Assert.True(OutgoingMail.IsValidRecipient("maria-jose.flores+tienda@sub.correo.example"));
        Assert.Equal("mail.recipient", Code(() => OutgoingMail.NormalizeRecipient(null)));
        Assert.Equal("guard.empty", Code(() => new OutgoingMail(Tenant, Branch, OutgoingMailKind.ReservationConfirmed, Guid.Empty, "a@b.example", User, Now)));
    }

    [Fact]
    public void El_Message_ID_es_el_mismo_en_cada_reintento_y_usa_el_dominio_del_remitente()
    {
        var mail = Mail();
        var id = mail.MessageId("reservas@TechZone.example");
        Assert.Equal($"<minv-{mail.Id:N}@techzone.example>", id);
        Assert.Equal(id, mail.MessageId(" reservas@techzone.example "));
        Assert.Equal($"<minv-{mail.Id:N}@techzone.example>", mail.MessageId("techzone.example"));
        // Un remitente sin dominio válido (o con algo que rompería la cabecera) usa el dominio de reserva
        foreach (var sender in new[] { null, "", "sin-dominio", "a@b", "a@dominio con espacio.example", "a@x.example>", "a@x.example\r\nBcc" })
        {
            Assert.Equal($"<minv-{mail.Id:N}@{OutgoingMail.FallbackMessageDomain}>", mail.MessageId(sender));
        }
        Assert.NotEqual(id, Mail().MessageId("reservas@techzone.example"));
    }

    [Fact]
    public void La_cola_nace_pendiente_y_reintenta_con_espera_hasta_agotarse_al_quinto_intento()
    {
        var mail = Mail();
        var dispatch = new OutgoingMailDispatch(Tenant, mail.Id, Now);
        Assert.Equal((mail.Id, OutgoingMailStatus.Pending, 0, Now, (DateTimeOffset?)null, (string?)null, 1),
            (dispatch.OutgoingMailId, dispatch.Status, dispatch.Attempts, dispatch.NextAttemptAt, dispatch.CompletedAt, dispatch.LastError, dispatch.NextAttempt));
        Assert.True(dispatch.IsDueAt(Now));
        Assert.False(dispatch.IsDueAt(Now.AddSeconds(-1)));

        // Esperas antes de cada intento: inmediato, 1 min, 5 min, 30 min y 2 h
        Assert.Equal([TimeSpan.Zero, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2)],
            Enumerable.Range(1, OutgoingMailAttempt.MaxAttempts).Select(OutgoingMailAttempt.BackoffBefore));
        var at = Now;
        foreach (var wait in new[] { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2) })
        {
            dispatch.RecordFailure("El servidor de correo no respondió.", at);
            Assert.Equal((OutgoingMailStatus.Pending, at + wait, (DateTimeOffset?)null, "El servidor de correo no respondió."),
                (dispatch.Status, dispatch.NextAttemptAt, dispatch.CompletedAt, dispatch.LastError));
            Assert.False(dispatch.IsDueAt(at + wait - TimeSpan.FromSeconds(1)));
            Assert.True(dispatch.IsDueAt(at + wait));
            at += wait;
        }
        Assert.Equal((4, 5), (dispatch.Attempts, dispatch.NextAttempt));
        dispatch.RecordFailure(null, at);
        Assert.Equal((OutgoingMailStatus.Exhausted, 5, at, "El envío falló sin detalle."), (dispatch.Status, dispatch.Attempts, dispatch.CompletedAt!.Value, dispatch.LastError));
        Assert.False(dispatch.IsDueAt(at.AddDays(1)));

        // Terminada, la cola no cambia más
        Assert.Equal("mail.done", Code(() => dispatch.MarkSent(at)));
        Assert.Equal("mail.done", Code(() => dispatch.RecordFailure("otra", at)));
        Assert.Equal("mail.done", Code(() => dispatch.Postpone("Sin configurar", at, TimeSpan.FromMinutes(5))));
        Assert.Equal("mail.done", Code(() => dispatch.Cancel("Vendida", at)));
    }

    [Fact]
    public void Enviar_rechazo_permanente_posponer_y_cancelar()
    {
        // Enviado en el segundo intento: cuenta el intento y borra el error anterior
        var sent = new OutgoingMailDispatch(Tenant, Guid.NewGuid(), Now);
        sent.RecordFailure("Tiempo agotado", Now);
        sent.MarkSent(Now.AddMinutes(1));
        Assert.Equal((OutgoingMailStatus.Sent, 2, Now.AddMinutes(1), (string?)null), (sent.Status, sent.Attempts, sent.CompletedAt!.Value, sent.LastError));
        Assert.Equal("mail.done", Code(() => sent.MarkSent(Now)));

        // El buzón no existe: agotado de inmediato, sin gastar los otros cuatro intentos
        var rejected = new OutgoingMailDispatch(Tenant, Guid.NewGuid(), Now);
        rejected.RecordFailure("550 5.1.1 El buzón no existe", Now, permanent: true);
        Assert.Equal((OutgoingMailStatus.Exhausted, 1, Now), (rejected.Status, rejected.Attempts, rejected.CompletedAt!.Value));

        // Pospuesto (la empresa no tiene el correo configurado): sigue pendiente y NO gasta un intento
        var postponed = new OutgoingMailDispatch(Tenant, Guid.NewGuid(), Now);
        postponed.Postpone("La empresa no tiene configurado el correo.", Now, TimeSpan.FromMinutes(10));
        Assert.Equal((OutgoingMailStatus.Pending, 0, Now.AddMinutes(10), "La empresa no tiene configurado el correo."),
            (postponed.Status, postponed.Attempts, postponed.NextAttemptAt, postponed.LastError));
        Assert.Equal("mail.postpone", Code(() => postponed.Postpone("x", Now, TimeSpan.FromSeconds(30))));
        Assert.Equal("mail.postpone", Code(() => postponed.Postpone("x", Now, TimeSpan.FromHours(25))));
        Assert.Equal("guard.text", Code(() => postponed.Postpone("  ", Now, TimeSpan.FromMinutes(5))));

        // Cancelado (la reserva se vendió o un reenvío lo reemplazó): no gasta un intento y guarda el motivo
        postponed.Cancel("La reserva ya se vendió.", Now.AddMinutes(3));
        Assert.Equal((OutgoingMailStatus.Cancelled, 0, Now.AddMinutes(3), "La reserva ya se vendió."),
            (postponed.Status, postponed.Attempts, postponed.CompletedAt!.Value, postponed.LastError));
        Assert.Equal("mail.done", Code(() => postponed.RecordFailure("x", Now)));
    }

    [Fact]
    public void El_error_guardado_es_una_sola_linea_de_500_caracteres_como_maximo()
    {
        var lineSeparator = ((char)0x2028).ToString();
        Assert.Equal("Fallo al conectar: 421 servicio no disponible",
            OutgoingMailDispatch.Clean("  Fallo al conectar:\r\n  421\tservicio" + lineSeparator + "no disponible  "));
        Assert.Null(OutgoingMailDispatch.Clean(" \r\n "));
        Assert.Equal(OutgoingMailDispatch.MaxErrorLength, OutgoingMailDispatch.Clean(new string('x', 900))!.Length);
        var dispatch = new OutgoingMailDispatch(Tenant, Guid.NewGuid(), Now);
        dispatch.RecordFailure("Línea 1\r\nLínea 2 " + new string('y', 700), Now);
        Assert.DoesNotContain('\n', dispatch.LastError!);
        Assert.Equal(OutgoingMailDispatch.MaxErrorLength, dispatch.LastError!.Length);
    }

    [Fact]
    public void Cada_intento_es_una_fila_de_la_bitacora()
    {
        var mailId = Guid.NewGuid();
        var ok = new OutgoingMailAttempt(Tenant, mailId, 1, true, "se ignora", Now, 850);
        Assert.Equal((mailId, 1, true, (string?)null, Now, 850), (ok.OutgoingMailId, ok.Attempt, ok.Succeeded, ok.Error, ok.AttemptedAt, ok.DurationMs));
        Assert.IsAssignableFrom<IAppendOnly>(ok);
        var failed = new OutgoingMailAttempt(Tenant, mailId, 5, false, null, Now, 0);
        Assert.Equal((false, "El envío falló sin detalle."), (failed.Succeeded, failed.Error));
        Assert.Equal("Tiempo agotado", new OutgoingMailAttempt(Tenant, mailId, 2, false, " Tiempo\r\nagotado ", Now, 30000).Error);
        Assert.Equal("mail.attempt", Code(() => new OutgoingMailAttempt(Tenant, mailId, 0, true, null, Now, 1)));
        Assert.Equal("mail.attempt", Code(() => new OutgoingMailAttempt(Tenant, mailId, OutgoingMailAttempt.MaxAttempts + 1, true, null, Now, 1)));
        Assert.Equal("guard.non_negative", Code(() => new OutgoingMailAttempt(Tenant, mailId, 1, true, null, Now, -1)));
    }
}
