using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", Schemas.Sales, t =>
        {
            t.HasCheckConstraint("ck_payments_monto", "amount > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(19, 4);
        builder.Property(x => x.Reference).HasMaxLength(60);
        builder.HasOne<Invoice>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId, x.InvoiceId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PaymentMethod>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.PaymentMethodId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
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
