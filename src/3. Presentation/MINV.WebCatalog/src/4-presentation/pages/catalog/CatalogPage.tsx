// PLACEHOLDER: el catálogo completo (filtros, facetas, orden, paginación) lo construye el agente de Catálogo.
// Esta vista mínima lee la categoría y la búsqueda de la URL y muestra los resultados con ProductCard.

import { useParams, useSearchParams } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { ProductCard } from '@/4-presentation/components/product/ProductCard';
import { Breadcrumbs } from '@/4-presentation/components/ui/Breadcrumbs';
import { Button } from '@/4-presentation/components/ui/Button';
import { Container } from '@/4-presentation/components/ui/Container';
import { EmptyState } from '@/4-presentation/components/ui/EmptyState';
import { SectionHeading } from '@/4-presentation/components/ui/SectionHeading';
import { useDocumentTitle } from '@/4-presentation/hooks/useDocumentTitle';
import { useServices } from '@/4-presentation/hooks/useServices';
import { pluralize } from '@/shared/format';

export function CatalogPage() {
  const { catalog } = useServices();
  const { categoria } = useParams();
  const [params] = useSearchParams();
  const q = params.get('q') ?? undefined;
  const tags = params.get('tags') === 'oferta' ? (['oferta'] as const) : params.get('tags') === 'nuevo' ? (['nuevo'] as const) : undefined;
  const result = catalog.searchCatalog({ category: categoria, q, tags, pageSize: 24 });
  const title = result.category?.name ?? (tags?.[0] === 'oferta' ? 'Ofertas' : q ? `Resultados para «${q}»` : 'Catálogo');
  useDocumentTitle(title);

  return (
    <Container className="py-8">
      <Breadcrumbs
        items={[
          { label: 'Catálogo', to: ROUTES.catalog },
          ...result.breadcrumbs.map((category, index) => ({
            label: category.name,
            to: index < result.breadcrumbs.length - 1 ? ROUTES.category(category.slug) : undefined,
          })),
        ]}
      />
      <SectionHeading as="h1" className="mt-4" title={title} subtitle={pluralize(result.total, 'producto', 'productos')} />
      {result.items.length === 0 ? (
        <EmptyState className="mt-8" title="No encontramos productos" description="Probá con otra palabra o recorré las categorías.">
          <Button to={ROUTES.catalog} variant="outline">
            Ver todo el catálogo
          </Button>
        </EmptyState>
      ) : (
        <div className="mt-6 grid grid-cols-2 gap-3 sm:gap-4 md:grid-cols-3 xl:grid-cols-4">
          {result.items.map((product, index) => (
            <ProductCard key={product.sku} product={product} priority={index < 4} />
          ))}
        </div>
      )}
    </Container>
  );
}
