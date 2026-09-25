using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants", Schemas.Iam);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(20);
        builder.Property(x => x.LegalName).HasMaxLength(150);
        builder.Property(x => x.TaxId).HasMaxLength(30);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}
