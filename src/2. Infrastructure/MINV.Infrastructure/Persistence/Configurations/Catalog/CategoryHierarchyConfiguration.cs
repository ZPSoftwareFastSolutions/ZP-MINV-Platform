using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MINV.Domain.Catalog;

namespace MINV.Infrastructure.Persistence.Configurations;

internal sealed class CategoryHierarchyConfiguration : IEntityTypeConfiguration<CategoryHierarchy>
{
    public void Configure(EntityTypeBuilder<CategoryHierarchy> builder)
    {
        builder.ToTable("category_hierarchies", Schemas.Catalog, t =>
        {
            t.HasCheckConstraint("ck_category_hierarchies_profundidad", "depth >= 0");
            t.HasCheckConstraint("ck_category_hierarchies_reflexiva", "(depth = 0) = (ancestor_id = descendant_id)");
        });
        builder.HasKey(x => new { x.AncestorId, x.DescendantId });
        builder.HasOne<Category>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.AncestorId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DescendantId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.DescendantId);
    }
}
