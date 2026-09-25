using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Accounting;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("currencies", Schemas.Accounting, t =>
        {
            t.HasCheckConstraint("ck_currencies_iso", "code ~ '^[A-Z]{3}$'");
            t.HasCheckConstraint("ck_currencies_decimales", "decimal_places BETWEEN 0 AND 4");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(3);
        builder.Property(x => x.Name).HasMaxLength(60);
        builder.Property(x => x.Symbol).HasMaxLength(8);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}
