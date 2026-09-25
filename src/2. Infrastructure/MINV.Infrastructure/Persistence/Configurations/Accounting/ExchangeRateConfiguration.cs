using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Accounting;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("exchange_rates", Schemas.Accounting, t =>
        {
            t.HasCheckConstraint("ck_exchange_rates_distintas", "from_currency_id <> to_currency_id");
            t.HasCheckConstraint("ck_exchange_rates_tasa", "rate > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Rate).HasPrecision(18, 8);
        builder.HasOne<Currency>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.FromCurrencyId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Currency>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ToCurrencyId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.FromCurrencyId, x.ToCurrencyId, x.EffectiveDate }).IsUnique();
    }
}
