using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class UnitConversionConfiguration : IEntityTypeConfiguration<UnitConversion>
{
    public void Configure(EntityTypeBuilder<UnitConversion> builder)
    {
        builder.ToTable("unit_conversions", Schemas.Catalog, t =>
        {
            t.HasCheckConstraint("ck_unit_conversions_distintas", "from_unit_id <> to_unit_id");
            t.HasCheckConstraint("ck_unit_conversions_factor", "factor > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Factor).HasPrecision(18, 8);
        builder.HasOne<UnitOfMeasure>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.FromUnitId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UnitOfMeasure>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ToUnitId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.FromUnitId, x.ToUnitId }).IsUnique();
    }
}
