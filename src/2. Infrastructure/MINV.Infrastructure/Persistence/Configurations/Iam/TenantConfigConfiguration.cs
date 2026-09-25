using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Accounting;
using MINV.Domain.Iam;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class TenantConfigConfiguration : IEntityTypeConfiguration<TenantConfig>
{
    public void Configure(EntityTypeBuilder<TenantConfig> builder)
    {
        builder.ToTable("tenant_configs", Schemas.Iam, t =>
        {
            t.HasCheckConstraint("ck_tenant_configs_margen", "alert_margin >= 0 AND alert_margin <= 1");
            t.HasCheckConstraint("ck_tenant_configs_rotacion", "days_without_rotation > 0");
        });
        builder.HasKey(x => x.TenantId);
        builder.Property(x => x.AlertMargin).HasPrecision(5, 4);
        builder.Property(x => x.TimeZoneId).HasMaxLength(64);
        builder.HasOne<Currency>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DefaultCurrencyId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DefaultWarehouseId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
