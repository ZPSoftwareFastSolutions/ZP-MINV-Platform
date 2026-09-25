using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Accounting;
using MINV.Domain.Catalog;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class TaxRuleConfiguration : IEntityTypeConfiguration<TaxRule>
{
    public void Configure(EntityTypeBuilder<TaxRule> builder)
    {
        builder.ToTable("tax_rules", Schemas.Accounting, t =>
        {
            t.HasCheckConstraint("ck_tax_rules_prioridad", "priority >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.HasOne<Tax>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.TaxId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CustomerCategory>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CustomerCategoryId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TaxId, x.CustomerCategoryId, x.BranchId }).IsUnique().AreNullsDistinct(false);
    }
}
