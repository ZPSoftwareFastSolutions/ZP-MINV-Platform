using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;

namespace MINV.Infrastructure.Persistence.Configurations;

/// <summary>
/// V4.2 · Especificación técnica de una categoría (regla T-01): código único por categoría, tipo (texto, número u opción),
/// multivalor solo para opciones y clave de compatibilidad del armador. La heredan las subcategorías.
/// </summary>
internal sealed class SpecDefinitionConfiguration : IEntityTypeConfiguration<SpecDefinition>
{
    public void Configure(EntityTypeBuilder<SpecDefinition> builder)
    {
        builder.ToTable("spec_definitions", Schemas.Catalog, t =>
        {
            t.HasCheckConstraint("ck_spec_definitions_tipo", "data_type IN ('Text', 'Number', 'Option')");
            t.HasCheckConstraint("ck_spec_definitions_multivalor", "NOT is_multi_valued OR data_type = 'Option'");
            t.HasCheckConstraint("ck_spec_definitions_codigo", "code ~ '^[a-z0-9_]+$'");
            t.HasCheckConstraint("ck_spec_definitions_clave", "compatibility_key IS NULL OR compatibility_key ~ '^[a-z0-9_]+$'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(40);
        builder.Property(x => x.Name).HasMaxLength(80);
        builder.Property(x => x.Unit).HasMaxLength(20);
        builder.Property(x => x.DataType).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.CompatibilityKey).HasMaxLength(40);
        builder.HasOne<Category>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CategoryId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.CategoryId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.CompatibilityKey }).HasFilter("compatibility_key IS NOT NULL");
    }
}

/// <summary>V4.2 · Opción de una especificación de tipo opción. Clave alterna (tenant_id, spec_definition_id, id): destino
/// de la FK de los valores, que así solo pueden usar una opción de SU especificación.</summary>
internal sealed class SpecOptionConfiguration : IEntityTypeConfiguration<SpecOption>
{
    public void Configure(EntityTypeBuilder<SpecOption> builder)
    {
        builder.ToTable("spec_options", Schemas.Catalog);
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.TenantId, x.SpecDefinitionId, x.Id });
        builder.Property(x => x.Value).HasMaxLength(60);
        builder.HasOne<SpecDefinition>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SpecDefinitionId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TenantId, x.SpecDefinitionId, x.Value }).IsUnique();
    }
}

/// <summary>
/// V4.2 · Valor de una especificación para un producto. Arco exclusivo (<c>num_nonnulls</c> = 1): número, texto u opción;
/// la opción es de ESA especificación (FK compuesta); que la columna usada sea la del tipo de la especificación y que una
/// especificación de un solo valor tenga una sola fila lo vigila el trigger <c>catalog.minv_spec_value_matches</c>
/// (migración V42TechRetail). Único por (producto, especificación, opción) con NULLS NOT DISTINCT: un número o texto por
/// especificación y cada opción una vez.
/// </summary>
internal sealed class ProductSpecValueConfiguration : IEntityTypeConfiguration<ProductSpecValue>
{
    public void Configure(EntityTypeBuilder<ProductSpecValue> builder)
    {
        builder.ToTable("product_spec_values", Schemas.Catalog, t =>
        {
            t.HasCheckConstraint("ck_product_spec_values_arco", "num_nonnulls(number_value, text_value, option_id) = 1");
            t.HasCheckConstraint("ck_product_spec_values_texto", "text_value IS NULL OR length(btrim(text_value)) > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.NumberValue).HasPrecision(18, 4);
        builder.Property(x => x.TextValue).HasMaxLength(200);
        builder.HasOne<Product>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ProductId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SpecDefinition>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SpecDefinitionId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SpecOption>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SpecDefinitionId, x.OptionId })
            .HasPrincipalKey(p => new { p.TenantId, p.SpecDefinitionId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.ProductId, x.SpecDefinitionId, x.OptionId }).IsUnique().AreNullsDistinct(false);
        builder.HasIndex(x => new { x.TenantId, x.SpecDefinitionId, x.OptionId });
    }
}

/// <summary>V4.2 · Perfil técnico 1:1 del producto (clave = empresa + producto): tipo de serie y meses de garantía.</summary>
internal sealed class ProductTechProfileConfiguration : IEntityTypeConfiguration<ProductTechProfile>
{
    public void Configure(EntityTypeBuilder<ProductTechProfile> builder)
    {
        builder.ToTable("product_tech_profiles", Schemas.Catalog, t =>
        {
            t.HasCheckConstraint("ck_product_tech_profiles_serie", "serial_kind IN ('Serial', 'Imei')");
            t.HasCheckConstraint("ck_product_tech_profiles_garantia", "warranty_months BETWEEN 0 AND 120");
        });
        builder.HasKey(x => new { x.TenantId, x.ProductId });
        builder.Property(x => x.SerialKind).HasConversion<string>().HasMaxLength(10);
        builder.HasOne<Product>().WithOne()
            .HasForeignKey<ProductTechProfile>(x => new { x.TenantId, x.ProductId })
            .HasPrincipalKey<Product>(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
