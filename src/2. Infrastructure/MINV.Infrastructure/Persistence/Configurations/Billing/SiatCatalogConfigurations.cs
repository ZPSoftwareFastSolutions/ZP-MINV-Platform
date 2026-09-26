using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Billing;
using MINV.Domain.Catalog;
using MINV.Domain.Iam;
using MINV.Domain.Sales;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

/// <summary>V4.1 · Paramétricas sincronizadas del SIN (clave natural: catálogo + código). Lo retirado no se borra.</summary>
internal sealed class SiatCatalogItemConfiguration : IEntityTypeConfiguration<SiatCatalogItem>
{
    public void Configure(EntityTypeBuilder<SiatCatalogItem> builder)
    {
        builder.ToTable("siat_catalog_items", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_siat_catalog_items_codigo", "code >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Catalog).HasMaxLength(40);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.HasIndex(x => new { x.TenantId, x.Catalog, x.Code }).IsUnique();
    }
}

/// <summary>V4.1 · Actividades económicas del NIT (código CAEB como texto).</summary>
internal sealed class SiatActivityConfiguration : IEntityTypeConfiguration<SiatActivity>
{
    public void Configure(EntityTypeBuilder<SiatActivity> builder)
    {
        builder.ToTable("siat_activities", Schemas.Billing);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(10);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.ActivityType).HasMaxLength(10);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}

/// <summary>V4.1 · Documentos sector habilitados por actividad.</summary>
internal sealed class SiatActivitySectorConfiguration : IEntityTypeConfiguration<SiatActivitySector>
{
    public void Configure(EntityTypeBuilder<SiatActivitySector> builder)
    {
        builder.ToTable("siat_activity_sectors", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_siat_activity_sectors_sector", "document_sector BETWEEN 1 AND 99");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActivityCode).HasMaxLength(10);
        builder.Property(x => x.SectorType).HasMaxLength(20);
        builder.HasIndex(x => new { x.TenantId, x.ActivityCode, x.DocumentSector }).IsUnique();
    }
}

/// <summary>V4.1 · Leyendas de la Ley 453 por actividad.</summary>
internal sealed class SiatLegendConfiguration : IEntityTypeConfiguration<SiatLegend>
{
    public void Configure(EntityTypeBuilder<SiatLegend> builder)
    {
        builder.ToTable("siat_legends", Schemas.Billing);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActivityCode).HasMaxLength(10);
        builder.Property(x => x.Text).HasMaxLength(200);
        builder.HasIndex(x => new { x.TenantId, x.ActivityCode, x.Text }).IsUnique();
    }
}

/// <summary>V4.1 · Productos y servicios del SIN. Clave alterna (tenant_id, activity_code, product_code): destino de la
/// homologación de los productos propios.</summary>
internal sealed class SiatProductConfiguration : IEntityTypeConfiguration<SiatProduct>
{
    public void Configure(EntityTypeBuilder<SiatProduct> builder)
    {
        builder.ToTable("siat_products", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_siat_products_codigo", "product_code BETWEEN 1 AND 99999999");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.TenantId, x.ActivityCode, x.ProductCode });
        builder.Property(x => x.ActivityCode).HasMaxLength(10);
        builder.Property(x => x.Description).HasMaxLength(1000);
    }
}

/// <summary>V4.1 · Cada sincronización de un catálogo (append-only).</summary>
internal sealed class SiatSyncRunConfiguration : IEntityTypeConfiguration<SiatSyncRun>
{
    public void Configure(EntityTypeBuilder<SiatSyncRun> builder)
    {
        builder.ToTable("siat_sync_runs", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_siat_sync_runs_filas", "items >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Catalog).HasMaxLength(40);
        builder.Property(x => x.Error).HasMaxLength(1000);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.Catalog, x.OccurredAt });
    }
}

/// <summary>V4.1 · Homologación producto → (actividad, producto SIN): un código por producto; el par debe existir en el
/// catálogo sincronizado (FK a la clave alterna de siat_products).</summary>
internal sealed class ProductSiatCodeConfiguration : IEntityTypeConfiguration<ProductSiatCode>
{
    public void Configure(EntityTypeBuilder<ProductSiatCode> builder)
    {
        builder.ToTable("product_siat_codes", Schemas.Billing);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActivityCode).HasMaxLength(10);
        builder.HasOne<Product>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ProductId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SiatProduct>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ActivityCode, x.SinProductCode })
            .HasPrincipalKey(p => new { p.TenantId, p.ActivityCode, p.ProductCode })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.ProductId }).IsUnique();
    }
}

/// <summary>V4.1 · Homologación unidad de medida → unidad SIN.</summary>
internal sealed class UnitSiatCodeConfiguration : IEntityTypeConfiguration<UnitSiatCode>
{
    public void Configure(EntityTypeBuilder<UnitSiatCode> builder)
    {
        builder.ToTable("unit_siat_codes", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_unit_siat_codes_codigo", "sin_unit_code BETWEEN 1 AND 999");
        });
        builder.HasKey(x => x.Id);
        builder.HasOne<UnitOfMeasure>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UnitId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.UnitId }).IsUnique();
    }
}

/// <summary>V4.1 · Homologación medio de pago → método de pago SIN.</summary>
internal sealed class PaymentMethodSiatCodeConfiguration : IEntityTypeConfiguration<PaymentMethodSiatCode>
{
    public void Configure(EntityTypeBuilder<PaymentMethodSiatCode> builder)
    {
        builder.ToTable("payment_method_siat_codes", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_payment_method_siat_codes_codigo", "sin_payment_method_code BETWEEN 1 AND 999");
        });
        builder.HasKey(x => x.Id);
        builder.HasOne<PaymentMethod>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.PaymentMethodId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.PaymentMethodId }).IsUnique();
    }
}

/// <summary>V4.1 · Verificaciones de NIT contra el Padrón (append-only).</summary>
internal sealed class CustomerNitCheckConfiguration : IEntityTypeConfiguration<CustomerNitCheck>
{
    public void Configure(EntityTypeBuilder<CustomerNitCheck> builder)
    {
        builder.ToTable("customer_nit_checks", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_customer_nit_checks_nit", "nit > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Description).HasMaxLength(300);
        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CustomerId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.Nit, x.CheckedAt });
    }
}

/// <summary>
/// V4.1 · Bitácora técnica de las llamadas SOAP (append-only, sin el token). Es de la empresa, no de una sucursal:
/// <c>branch_id</c> es un atributo de contexto (qué sucursal llamó), no una partición.
/// </summary>
internal sealed class SiatServiceCallConfiguration : IEntityTypeConfiguration<SiatServiceCall>
{
    public void Configure(EntityTypeBuilder<SiatServiceCall> builder)
    {
        builder.ToTable("siat_service_calls", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_siat_service_calls_duracion", "duration_ms >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Resource).HasMaxLength(60);
        builder.Property(x => x.Operation).HasMaxLength(80);
        builder.Property(x => x.RequestBody).HasMaxLength(20000);
        builder.Property(x => x.ResponseBody).HasMaxLength(20000);
        builder.Property(x => x.Error).HasMaxLength(1000);
        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.OccurredAt });
    }
}
