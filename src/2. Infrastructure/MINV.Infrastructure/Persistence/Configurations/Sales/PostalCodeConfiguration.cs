using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class PostalCodeConfiguration : IEntityTypeConfiguration<PostalCode>
{
    public void Configure(EntityTypeBuilder<PostalCode> builder)
    {
        builder.ToTable("postal_codes", Schemas.Sales);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(12);
        builder.HasOne<City>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CityId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.CityId, x.Code }).IsUnique();
    }
}
