using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.ToTable("addresses", Schemas.Sales);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Street).HasMaxLength(200);
        builder.Property(x => x.Reference).HasMaxLength(200);
        builder.HasOne<PostalCode>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.PostalCodeId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
