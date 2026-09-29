using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Iam;
using MINV.Domain.Sales;

namespace MINV.Infrastructure.Persistence.Configurations;

/// <summary>
/// V7 · <c>sales.customer_accounts</c> (regla P-04): vínculo 1 a 1 entre el usuario que ingresa a la tienda web y su cliente.
/// Las dos claves foráneas son compuestas con la empresa (una cuenta no puede unir filas de dos empresas) y cada lado es
/// único: un usuario tiene a lo sumo una cuenta y un cliente pertenece a lo sumo a un usuario. Tabla de la empresa (los
/// clientes y los usuarios no se filtran por sucursal, regla B-02).
/// </summary>
internal sealed class CustomerAccountConfiguration : IEntityTypeConfiguration<CustomerAccount>
{
    public void Configure(EntityTypeBuilder<CustomerAccount> builder)
    {
        builder.ToTable("customer_accounts", Schemas.Sales);
        builder.HasKey(x => x.Id);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.UserId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CustomerId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.CustomerId }).IsUnique();
    }
}
