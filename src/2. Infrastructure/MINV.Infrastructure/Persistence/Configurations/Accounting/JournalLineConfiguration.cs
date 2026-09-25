using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Accounting;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class JournalLineConfiguration : IEntityTypeConfiguration<JournalLine>
{
    public void Configure(EntityTypeBuilder<JournalLine> builder)
    {
        builder.ToTable("journal_lines", Schemas.Accounting, t =>
        {
            t.HasCheckConstraint("ck_journal_lines_partida", "(debit > 0 AND credit = 0) OR (credit > 0 AND debit = 0)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Debit).HasPrecision(19, 4);
        builder.Property(x => x.Credit).HasPrecision(19, 4);
        builder.Property(x => x.Memo).HasMaxLength(200);
        builder.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.AccountId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CostCenter>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CostCenterId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
