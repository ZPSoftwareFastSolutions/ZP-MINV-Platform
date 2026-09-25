using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class LicenseModuleConfiguration : IEntityTypeConfiguration<LicenseModule>
{
    public void Configure(EntityTypeBuilder<LicenseModule> builder)
    {
        builder.ToTable("modules", Schemas.Iam, t =>
        {
            t.HasCheckConstraint("ck_modules_prices", "setup_price_bs >= 0 AND monthly_fee_bs >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(40);
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(400);
        builder.Property(x => x.SetupPriceBs).HasPrecision(19, 4);
        builder.Property(x => x.MonthlyFeeBs).HasPrecision(19, 4);
        builder.HasIndex(x => x.Code).IsUnique();
        // Catálogo comercial sembrado por la migración (fecha fija: las migraciones deben ser deterministas).
        var seededAt = new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
        builder.HasData(LicenseModule.Catalog().Select(m => new
        {
            m.Id, m.Code, m.Name, m.Description, m.SetupPriceBs, m.MonthlyFeeBs, CreatedAt = seededAt,
        }));
    }
}
