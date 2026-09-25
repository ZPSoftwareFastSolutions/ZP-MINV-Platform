using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class CityConfiguration : IEntityTypeConfiguration<City>
{
    public void Configure(EntityTypeBuilder<City> builder)
    {
        builder.ToTable("cities", Schemas.Sales);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(80);
        builder.HasOne<State>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StateId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.StateId, x.Name }).IsUnique();
    }
}
