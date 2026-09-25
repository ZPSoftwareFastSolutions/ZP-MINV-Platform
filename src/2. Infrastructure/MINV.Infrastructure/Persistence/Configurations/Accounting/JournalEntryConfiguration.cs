using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Warehousing;
using MINV.Domain.Accounting;
using MINV.Domain.Iam;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.ToTable("journal_entries", Schemas.Accounting);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(30);
        builder.Property(x => x.Description).HasMaxLength(250);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        // V4 · Cabecera sin almacén: su sucursal se valida con una FK directa (no hay padre que la herede)
        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FiscalPeriod>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.FiscalPeriodId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Currency>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CurrencyId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.PostedByUserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne()
            .HasForeignKey(c => new { c.TenantId, c.BranchId, c.JournalEntryId })
            .HasPrincipalKey(p => new { p.TenantId, p.BranchId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
    }
}
