using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Billing;
using MINV.Domain.Warehousing;

namespace MINV.Infrastructure.Persistence.Configurations;

/// <summary>V4.1 · Configuración de facturación de la empresa: una fila por tenant (clave tenant_id, como tenant_configs).</summary>
internal sealed class SiatSettingsConfiguration : IEntityTypeConfiguration<SiatSettings>
{
    public void Configure(EntityTypeBuilder<SiatSettings> builder)
    {
        builder.ToTable("siat_settings", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_siat_settings_nit", "nit BETWEEN 1 AND 9999999999999");
            t.HasCheckConstraint("ck_siat_settings_ambiente", BillingChecks.Environment());
            t.HasCheckConstraint("ck_siat_settings_modalidad", $"modality = {SiatCodes.ModalityComputerized}");
        });
        builder.HasKey(x => x.TenantId);
        builder.Property(x => x.BusinessName).HasMaxLength(200);
        builder.Property(x => x.SystemCode).HasMaxLength(50);
        builder.Property(x => x.OnlineLegend).HasMaxLength(250);
        builder.Property(x => x.OfflineLegend).HasMaxLength(250);
    }
}

/// <summary>
/// V4.1 · Conexión por ambiente: URL de cada recurso SOAP, namespace, URL del QR y token delegado CIFRADO (nunca el texto
/// plano). Un perfil por (empresa, ambiente).
/// </summary>
internal sealed class SiatEnvironmentProfileConfiguration : IEntityTypeConfiguration<SiatEnvironmentProfile>
{
    public void Configure(EntityTypeBuilder<SiatEnvironmentProfile> builder)
    {
        builder.ToTable("siat_environment_profiles", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_siat_environment_profiles_ambiente", BillingChecks.Environment());
            t.HasCheckConstraint("ck_siat_environment_profiles_espera", "timeout_seconds BETWEEN 3 AND 120");
            t.HasCheckConstraint("ck_siat_environment_profiles_token", "(token_ciphertext IS NULL) = (token_key_id IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CodesUrl).HasMaxLength(400);
        builder.Property(x => x.SyncUrl).HasMaxLength(400);
        builder.Property(x => x.OperationsUrl).HasMaxLength(400);
        builder.Property(x => x.PurchaseSaleUrl).HasMaxLength(400);
        builder.Property(x => x.ComputerizedUrl).HasMaxLength(400);
        builder.Property(x => x.AdjustmentUrl).HasMaxLength(400);
        builder.Property(x => x.Namespace).HasMaxLength(200);
        builder.Property(x => x.QrBaseUrl).HasMaxLength(400);
        builder.Property(x => x.TokenCiphertext).HasMaxLength(8000);
        builder.Property(x => x.TokenKeyId).HasMaxLength(40);
        builder.Ignore(x => x.Endpoints);
        builder.HasIndex(x => new { x.TenantId, x.Environment }).IsUnique();
    }
}

/// <summary>V4.1 · Sucursal de M-INV ↔ codigoSucursal del Padrón (una por sucursal; el código es único en la empresa).</summary>
internal sealed class SiatBranchConfiguration : IEntityTypeConfiguration<SiatBranch>
{
    public void Configure(EntityTypeBuilder<SiatBranch> builder)
    {
        builder.ToTable("siat_branches", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_siat_branches_codigo", "siat_code BETWEEN 0 AND 9999");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Municipality).HasMaxLength(25);
        builder.Property(x => x.Phone).HasMaxLength(25);
        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.BranchId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.SiatCode }).IsUnique();
    }
}

/// <summary>V4.1 · Servidor SMTP de la empresa (una fila por tenant); la contraseña solo cifrada.</summary>
internal sealed class MailSettingsConfiguration : IEntityTypeConfiguration<MailSettings>
{
    public void Configure(EntityTypeBuilder<MailSettings> builder)
    {
        builder.ToTable("mail_settings", Schemas.Billing, t =>
        {
            t.HasCheckConstraint("ck_mail_settings_puerto", "port BETWEEN 1 AND 65535");
            t.HasCheckConstraint("ck_mail_settings_clave", "(password_ciphertext IS NULL) = (password_key_id IS NULL)");
        });
        builder.HasKey(x => x.TenantId);
        builder.Property(x => x.Host).HasMaxLength(200);
        builder.Property(x => x.UserName).HasMaxLength(200);
        builder.Property(x => x.PasswordCiphertext).HasMaxLength(4000);
        builder.Property(x => x.PasswordKeyId).HasMaxLength(40);
        builder.Property(x => x.FromAddress).HasMaxLength(254);
        builder.Property(x => x.FromName).HasMaxLength(100);
    }
}
