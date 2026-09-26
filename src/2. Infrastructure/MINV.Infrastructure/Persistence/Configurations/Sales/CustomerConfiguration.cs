using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers", Schemas.Sales, t =>
        {
            // V4.1 · Datos de facturación: tipo de documento del SIN (1 CI … 5 NIT); complemento solo con CI
            t.HasCheckConstraint("ck_customers_tipo_documento", "document_type IS NULL OR document_type BETWEEN 1 AND 5");
            t.HasCheckConstraint("ck_customers_complemento", "complement IS NULL OR document_type = 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(20);
        builder.Property(x => x.Name).HasMaxLength(150);
        builder.Property(x => x.TaxId).HasMaxLength(30);
        builder.Property(x => x.Email).HasMaxLength(254);
        builder.Property(x => x.Phone).HasMaxLength(40);
        builder.Property(x => x.DocumentType).HasConversion<short?>().HasColumnType("smallint");
        builder.Property(x => x.Complement).HasMaxLength(5);
        builder.HasOne<CustomerCategory>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CustomerCategoryId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}
