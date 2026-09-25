using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> builder)
    {
        builder.ToTable("cash_movements", Schemas.Sales, t =>
        {
            t.HasCheckConstraint("ck_cash_movements_monto", "amount > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Direction).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Amount).HasPrecision(19, 4);
        builder.Property(x => x.Reason).HasMaxLength(200);
        builder.HasOne<PosSession>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.PosSessionId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RecordedByUserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
