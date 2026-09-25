using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class UserCredentialConfiguration : IEntityTypeConfiguration<UserCredential>
{
    public void Configure(EntityTypeBuilder<UserCredential> builder)
    {
        builder.ToTable("user_credentials", Schemas.Iam, t =>
        {
            t.HasCheckConstraint("ck_user_credentials_iteraciones", "iterations >= 100000");
            t.HasCheckConstraint("ck_user_credentials_intentos", "failed_attempts >= 0");
        });
        builder.HasKey(x => x.UserId);
        builder.Property(x => x.PasswordHash).HasMaxLength(512);
        builder.Property(x => x.Algorithm).HasMaxLength(30);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
