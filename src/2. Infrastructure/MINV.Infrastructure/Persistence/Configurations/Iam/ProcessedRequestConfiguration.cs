using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;

namespace MINV.Infrastructure.Persistence.Configurations;

/// <summary>V4 · Comandos ya ejecutados por el servidor en la nube (idempotencia de los reintentos del escritorio).</summary>
internal sealed class ProcessedRequestConfiguration : IEntityTypeConfiguration<ProcessedRequest>
{
    public void Configure(EntityTypeBuilder<ProcessedRequest> builder)
    {
        builder.ToTable("processed_requests", Schemas.Iam, t =>
        {
            t.HasCheckConstraint("ck_processed_requests_hash", "request_hash ~ '^[0-9a-f]{64}$'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RequestType).HasMaxLength(200);
        builder.Property(x => x.RequestHash).HasMaxLength(64);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.RequestId }).IsUnique();
        builder.HasIndex(x => x.ProcessedAt);
    }
}
