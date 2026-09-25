using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions", Schemas.Iam, t =>
        {
            t.HasCheckConstraint("ck_sessions_fechas", "ended_at IS NULL OR ended_at >= started_at");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MachineName).HasMaxLength(100);
        builder.Property(x => x.ClientVersion).HasMaxLength(30);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HardwareToken>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HardwareTokenId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.UserId, x.StartedAt });
    }
}
