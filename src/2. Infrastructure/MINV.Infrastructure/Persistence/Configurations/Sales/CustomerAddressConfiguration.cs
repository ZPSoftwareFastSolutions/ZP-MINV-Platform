using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Common;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> builder)
    {
        builder.ToTable("customer_addresses", Schemas.Sales);
        builder.HasKey(x => new { x.CustomerId, x.AddressId });
        builder.Property(x => x.AddressType).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CustomerId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Address>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.AddressId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.CustomerId, x.AddressType }).IsUnique().HasFilter("is_default");
    }
}
