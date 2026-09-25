using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class PosSessionConfiguration : IEntityTypeConfiguration<PosSession>
{
    public void Configure(EntityTypeBuilder<PosSession> builder)
    {
        builder.ToTable("pos_sessions", Schemas.Sales, t =>
        {
            t.HasCheckConstraint("ck_pos_sessions_fondo", "opening_cash >= 0");
            t.HasCheckConstraint("ck_pos_sessions_cierre", "(status = 'Closed') = (closed_at IS NOT NULL)");
            t.HasCheckConstraint("ck_pos_sessions_fechas", "closed_at IS NULL OR closed_at >= opened_at");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OpeningCash).HasPrecision(19, 4);
        builder.Property(x => x.ClosingCashCounted).HasPrecision(19, 4);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<PosRegister>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.PosRegisterId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.OpenedByUserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ClosedByUserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.PosRegisterId).IsUnique().HasFilter("status = 'Open'");
    }
}
