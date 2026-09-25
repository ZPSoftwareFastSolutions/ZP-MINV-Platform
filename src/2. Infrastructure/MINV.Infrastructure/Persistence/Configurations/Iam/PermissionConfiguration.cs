using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions", Schemas.Iam, t =>
        {
            t.HasCheckConstraint("ck_permissions_codigo_minusculas", "code = lower(code)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(80);
        builder.Property(x => x.Description).HasMaxLength(200);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}
