using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;
using MINV.Domain.Integration;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Persistence.Configurations;

// =====================================================================================================================
// V7 · Correo saliente (regla P-06): el HECHO (outgoing_mails, append-only y de la sucursal de la reserva), su COLA
// (outgoing_mail_dispatch, la única tabla mutable, 1:1) y la BITÁCORA de intentos (outgoing_mail_attempts, append-only).
// No se guarda el asunto ni el cuerpo: se derivan al enviar desde la reserva (precios congelados).
// =====================================================================================================================

/// <summary>V7 · Correo pedido (append-only, de sucursal): la FK compuesta con la sucursal impide que el correo sea de otra
/// sucursal que su reserva. Los índices por destinatario y por fecha son los que cuentan los topes de 24 horas.</summary>
internal sealed class OutgoingMailConfiguration : IEntityTypeConfiguration<OutgoingMail>
{
    public void Configure(EntityTypeBuilder<OutgoingMail> builder)
    {
        builder.ToTable("outgoing_mails", Schemas.Integration, t =>
        {
            t.HasCheckConstraint("ck_outgoing_mails_tipo", BillingChecks.In<OutgoingMailKind>("kind"));
            // Una sola dirección, en minúsculas y sin espacios (así la cuentan los topes por destinatario)
            t.HasCheckConstraint("ck_outgoing_mails_destinatario",
                "recipient = lower(recipient) AND recipient ~ '^[^@[:space:]]+@[^@[:space:]]+\\.[^@[:space:]]+$'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.Recipient).HasMaxLength(OutgoingMail.MaxRecipientLength);
        builder.HasOne<PcBuild>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.PcBuildId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RequestedByUserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.Recipient, x.RequestedAt });
        builder.HasIndex(x => new { x.TenantId, x.RequestedAt });
    }
}

/// <summary>V7 · Cola de envío (1:1 con el correo, la única tabla mutable del correo). El índice parcial sobre los pendientes
/// es el que usará <c>integration.claim_outgoing_mails</c> (FOR UPDATE SKIP LOCKED).</summary>
internal sealed class OutgoingMailDispatchConfiguration : IEntityTypeConfiguration<OutgoingMailDispatch>
{
    public void Configure(EntityTypeBuilder<OutgoingMailDispatch> builder)
    {
        builder.ToTable("outgoing_mail_dispatch", Schemas.Integration, t =>
        {
            t.HasCheckConstraint("ck_outgoing_mail_dispatch_estado", BillingChecks.In<OutgoingMailStatus>("status"));
            t.HasCheckConstraint("ck_outgoing_mail_dispatch_fin", "(status = 'Pending') = (completed_at IS NULL)");
            t.HasCheckConstraint("ck_outgoing_mail_dispatch_intentos", $"attempts BETWEEN 0 AND {OutgoingMailAttempt.MaxAttempts}");
            // Enviado o agotado = hubo al menos un intento (pospuesto y cancelado no gastan intentos)
            t.HasCheckConstraint("ck_outgoing_mail_dispatch_intentado", "status NOT IN ('Sent', 'Exhausted') OR attempts >= 1");
        });
        builder.HasKey(x => x.OutgoingMailId);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.LastError).HasMaxLength(OutgoingMailDispatch.MaxErrorLength);
        builder.HasOne<OutgoingMail>().WithOne()
            .HasForeignKey<OutgoingMailDispatch>(x => new { x.TenantId, x.OutgoingMailId })
            .HasPrincipalKey<OutgoingMail>(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.NextAttemptAt).HasFilter("status = 'Pending'");
    }
}

/// <summary>V7 · Intentos de envío (append-only): cada reintento es una fila; un intento logrado no tiene error y uno fallido
/// siempre dice por qué.</summary>
internal sealed class OutgoingMailAttemptConfiguration : IEntityTypeConfiguration<OutgoingMailAttempt>
{
    public void Configure(EntityTypeBuilder<OutgoingMailAttempt> builder)
    {
        builder.ToTable("outgoing_mail_attempts", Schemas.Integration, t =>
        {
            t.HasCheckConstraint("ck_outgoing_mail_attempts_intento", $"attempt BETWEEN 1 AND {OutgoingMailAttempt.MaxAttempts}");
            t.HasCheckConstraint("ck_outgoing_mail_attempts_duracion", "duration_ms >= 0");
            t.HasCheckConstraint("ck_outgoing_mail_attempts_error", "succeeded = (error IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Error).HasMaxLength(OutgoingMailDispatch.MaxErrorLength);
        builder.HasOne<OutgoingMail>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.OutgoingMailId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OutgoingMailId, x.Attempt }).IsUnique();
    }
}
