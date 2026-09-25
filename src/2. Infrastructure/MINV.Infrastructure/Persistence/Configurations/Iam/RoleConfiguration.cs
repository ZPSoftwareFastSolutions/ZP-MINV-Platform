using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles", Schemas.Iam);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(30);
        builder.Property(x => x.Name).HasMaxLength(80);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}
