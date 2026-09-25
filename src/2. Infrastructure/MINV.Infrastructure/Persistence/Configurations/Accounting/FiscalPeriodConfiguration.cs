using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Accounting;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class FiscalPeriodConfiguration : IEntityTypeConfiguration<FiscalPeriod>
{
    public void Configure(EntityTypeBuilder<FiscalPeriod> builder)
    {
        builder.ToTable("fiscal_periods", Schemas.Accounting, t =>
        {
            t.HasCheckConstraint("ck_fiscal_periods_mes", "month BETWEEN 1 AND 12");
            t.HasCheckConstraint("ck_fiscal_periods_anio", "year BETWEEN 2000 AND 2100");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(x => new { x.TenantId, x.Year, x.Month }).IsUnique();
    }
}
