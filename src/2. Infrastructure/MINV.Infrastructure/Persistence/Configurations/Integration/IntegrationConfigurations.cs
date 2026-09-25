using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;
using MINV.Domain.Integration;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

/// <summary>V4 · API Key del gateway B2B: prefijo único en toda la plataforma (se busca antes de conocer la empresa).</summary>
internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys", Schemas.Integration, t =>
        {
            t.HasCheckConstraint("ck_api_keys_prefijo", "prefix ~ '^[a-z0-9]{8}$'");
            t.HasCheckConstraint("ck_api_keys_hash", "token_hash ~ '^[0-9a-f]{64}$'");
            t.HasCheckConstraint("ck_api_keys_vencimiento", "expires_at IS NULL OR expires_at > created_at");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.Prefix).HasMaxLength(ApiKey.PrefixLength);
        builder.Property(x => x.TokenHash).HasMaxLength(64);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.OwnerUserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Scopes).WithOne()
            .HasForeignKey(s => new { s.TenantId, s.ApiKeyId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Scopes).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => x.Prefix).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Name });
    }
}

internal sealed class ApiKeyScopeConfiguration : IEntityTypeConfiguration<ApiKeyScope>
{
    public void Configure(EntityTypeBuilder<ApiKeyScope> builder)
    {
        builder.ToTable("api_key_scopes", Schemas.Integration);
        builder.HasKey(x => new { x.ApiKeyId, x.Scope });
        builder.Property(x => x.Scope).HasMaxLength(40);
    }
}

internal sealed class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.ToTable("webhook_endpoints", Schemas.Integration, t =>
        {
            t.HasCheckConstraint("ck_webhook_endpoints_url", "url ~ '^(https://|http://localhost|http://127\\.0\\.0\\.1|http://\\[::1\\])'");
            t.HasCheckConstraint("ck_webhook_endpoints_baja", "is_active = (disabled_at IS NULL)");
            t.HasCheckConstraint("ck_webhook_endpoints_version", "secret_version >= 1");
            t.HasCheckConstraint("ck_webhook_endpoints_rotacion",
                "(previous_secret_ciphertext IS NULL) = (previous_secret_expires_at IS NULL) AND (previous_secret_ciphertext IS NULL) = (previous_secret_key_id IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Url).HasMaxLength(500);
        builder.Property(x => x.Description).HasMaxLength(200);
        builder.Property(x => x.SecretCiphertext).HasMaxLength(500);
        builder.Property(x => x.SecretKeyId).HasMaxLength(40);
        builder.Property(x => x.PreviousSecretCiphertext).HasMaxLength(500);
        builder.Property(x => x.PreviousSecretKeyId).HasMaxLength(40);
        builder.HasOne<ApiKey>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ApiKeyId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CreatedByUserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Events).WithOne()
            .HasForeignKey(e => new { e.TenantId, e.EndpointId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Events).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class WebhookEndpointEventConfiguration : IEntityTypeConfiguration<WebhookEndpointEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEndpointEvent> builder)
    {
        builder.ToTable("webhook_endpoint_events", Schemas.Integration);
        builder.HasKey(x => new { x.EndpointId, x.EventType });
        builder.Property(x => x.EventType).HasMaxLength(60);
    }
}

/// <summary>Outbox transaccional (append-only): lo escribe SaveChanges junto con el cambio que produjo el evento.</summary>
internal sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("outbox_events", Schemas.Integration);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasMaxLength(60);
        builder.Property(x => x.Payload).HasColumnType("jsonb");
        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.OccurredAt });
    }
}

/// <summary>V4 · Cola de despacho (1:1 con el outbox): la única tabla mutable de la integración. El índice parcial sobre
/// los pendientes es el que usa <c>integration.claim_deliveries</c> (FOR UPDATE SKIP LOCKED).</summary>
internal sealed class OutboxDispatchConfiguration : IEntityTypeConfiguration<OutboxDispatch>
{
    public void Configure(EntityTypeBuilder<OutboxDispatch> builder)
    {
        builder.ToTable("outbox_dispatch", Schemas.Integration, t =>
        {
            t.HasCheckConstraint("ck_outbox_dispatch_estado", "status IN ('Pending', 'Completed', 'Exhausted')");
            t.HasCheckConstraint("ck_outbox_dispatch_fin", "(status = 'Pending') = (completed_at IS NULL)");
            t.HasCheckConstraint("ck_outbox_dispatch_rondas", $"rounds BETWEEN 0 AND {WebhookDelivery.MaxAttempts}");
        });
        builder.HasKey(x => x.OutboxEventId);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.LastError).HasMaxLength(500);
        builder.HasOne<OutboxEvent>().WithOne()
            .HasForeignKey<OutboxDispatch>(x => new { x.TenantId, x.OutboxEventId })
            .HasPrincipalKey<OutboxEvent>(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.NextAttemptAt).HasFilter("status = 'Pending'");
    }
}

/// <summary>Intentos de entrega (append-only): cada reintento es una fila; nunca se actualiza el estado.</summary>
internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("webhook_deliveries", Schemas.Integration, t =>
        {
            t.HasCheckConstraint("ck_webhook_deliveries_intento", $"attempt BETWEEN 1 AND {WebhookDelivery.MaxAttempts}");
            t.HasCheckConstraint("ck_webhook_deliveries_duracion", "duration_ms >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Error).HasMaxLength(500);
        builder.HasOne<OutboxEvent>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.OutboxEventId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WebhookEndpoint>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.EndpointId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OutboxEventId, x.EndpointId, x.Attempt }).IsUnique();
    }
}
