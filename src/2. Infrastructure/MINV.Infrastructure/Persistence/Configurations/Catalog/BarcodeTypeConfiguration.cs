using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class BarcodeTypeConfiguration : IEntityTypeConfiguration<BarcodeType>
{
    public void Configure(EntityTypeBuilder<BarcodeType> builder)
    {
        builder.ToTable("barcode_types", Schemas.Catalog, t =>
        {
            t.HasCheckConstraint("ck_barcode_types_longitud", "length IS NULL OR (length >= 1 AND length <= 64)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(20);
        builder.Property(x => x.Name).HasMaxLength(60);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}
