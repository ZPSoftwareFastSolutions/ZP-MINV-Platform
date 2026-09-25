using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class TenantModuleConfiguration : IEntityTypeConfiguration<TenantModule>
{
    public void Configure(EntityTypeBuilder<TenantModule> builder)
    {
        builder.ToTable("tenant_modules", Schemas.Iam, t =>
        {
            t.HasCheckConstraint("ck_tenant_modules_vigencia", "expires_at IS NULL OR expires_at > activated_at");
        });
        builder.HasKey(x => new { x.TenantId, x.ModuleId });
        builder.HasOne<LicenseModule>().WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Restrict);
    }
}
