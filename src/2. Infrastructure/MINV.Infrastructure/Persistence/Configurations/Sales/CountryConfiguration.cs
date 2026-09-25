using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable("countries", Schemas.Sales, t =>
        {
            t.HasCheckConstraint("ck_countries_iso", "iso_code ~ '^[A-Z]{2}$'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IsoCode).HasMaxLength(2);
        builder.Property(x => x.Name).HasMaxLength(80);
        builder.HasIndex(x => new { x.TenantId, x.IsoCode }).IsUnique();
    }
}
