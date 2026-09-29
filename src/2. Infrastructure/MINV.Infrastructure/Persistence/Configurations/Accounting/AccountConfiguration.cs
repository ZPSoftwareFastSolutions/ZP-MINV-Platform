using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Accounting;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts", Schemas.Accounting, t =>
        {
            t.HasCheckConstraint("ck_accounts_padre", "parent_account_id IS NULL OR parent_account_id <> id");
            // V7 · Dominio del tipo de cuenta (comprobación de normalización E14, docs/database/normalizacion-v7.md)
            t.HasCheckConstraint("ck_accounts_tipo", BillingChecks.In<AccountType>("account_type"));
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(20);
        builder.Property(x => x.Name).HasMaxLength(120);
        builder.Property(x => x.AccountType).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ParentAccountId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}
