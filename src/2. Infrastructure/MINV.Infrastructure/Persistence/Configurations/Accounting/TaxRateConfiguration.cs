using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Accounting;
using MINV.Domain.Catalog;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class TaxRateConfiguration : IEntityTypeConfiguration<TaxRate>
{
    public void Configure(EntityTypeBuilder<TaxRate> builder)
    {
        builder.ToTable("tax_rates", Schemas.Accounting, t =>
        {
            t.HasCheckConstraint("ck_tax_rates_tasa", "rate >= 0 AND rate <= 100");
            t.HasCheckConstraint("ck_tax_rates_vigencia", "valid_to IS NULL OR valid_to >= valid_from");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Rate).HasPrecision(9, 4);
        builder.HasOne<Tax>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.TaxId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TaxId, x.ValidFrom }).IsUnique();
    }
}
