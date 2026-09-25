using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", Schemas.Iam, t =>
        {
            t.HasCheckConstraint("ck_users_email_minusculas", "email = lower(email)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Email).HasMaxLength(254);
        builder.Property(x => x.DisplayName).HasMaxLength(120);
        builder.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
    }
}
