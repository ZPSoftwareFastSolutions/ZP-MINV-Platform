// Catálogo: /catalogo y /catalogo/:categoria. Los filtros viven en la URL (q, marca, min, max, condicion, stock, tags,
// orden, pagina, vista); en escritorio el panel de filtros es fijo a la izquierda y en móvil se abre en un cajón.

import { RotateCcw } from 'lucide-react';
import { useMemo, useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { ProductCard } from '@/4-presentation/components/product/ProductCard';
import { Breadcrumbs } from '@/4-presentation/components/ui/Breadcrumbs';
import { Button } from '@/4-presentation/components/ui/Button';
import { CategoryIcon } from '@/4-presentation/components/ui/CategoryIcon';
import { Container } from '@/4-presentation/components/ui/Container';
import { Pagination } from '@/4-presentation/components/ui/Pagination';
import { SectionHeading } from '@/4-presentation/components/ui/SectionHeading';
import { useDocumentTitle } from '@/4-presentation/hooks/useDocumentTitle';
import { useServices } from '@/4-presentation/hooks/useServices';
import { ActiveFilters } from './ActiveFilters';
import { activeFilterCount, catalogHref, catalogQuery, clearedFilters, hasActiveFilters, type CatalogFilters } from './catalogFilters';
import { catalogHeading } from './catalogHeading';

import { EmptyResults } from './EmptyResults';
import { FilterPanel } from './FilterPanel';
import { FiltersDrawer } from './FiltersDrawer';
import { LinkChip } from './LinkChip';
import { ProductRow } from './ProductRow';
import { ResultsToolbar } from './ResultsToolbar';
import { useCatalogFilters } from './useCatalogFilters';

const SUGGESTED_CATEGORIES = 6;

export function CatalogPage() {
  const { catalog } = useServices();
  const { filters, apply, update, href } = useCatalogFilters();
  const [filtersOpen, setFiltersOpen] = useState(false);

  const result = useMemo(() => catalog.searchCatalog(catalogQuery(filters)), [catalog, filters]);
  const tree = useMemo(() => catalog.getCategoryFilterTree(filters), [catalog, filters]);
  const brandNames = useMemo(() => new Map(catalog.getBrands().map((brand) => [brand.code, brand.name])), [catalog]);
  const suggestions = useMemo(
    () => [...catalog.getRootCategories()].sort((a, b) => b.productCount - a.productCount).slice(0, SUGGESTED_CATEGORIES),
    [catalog],
  );

  const category = result.category;
  const root = result.breadcrumbs[0];
  const heading = catalogHeading(filters, { category, root, total: result.total });
  useDocumentTitle(heading.title);

  const activeCount = activeFilterCount(filters);
  const anyActive = hasActiveFilters(filters);
  const scopeLabel = category?.name ?? 'todo el catálogo';
  const brandName = (code: string) => brandNames.get(code) ?? code;

  /** Reemplaza todos los filtros; si cambia la categoría agrega una entrada al historial. */
  const applyAll = (next: CatalogFilters) => apply(next, { push: next.category !== filters.category });
  const clearAll = () => applyAll(clearedFilters(filters));

  const breadcrumbs = [
    { label: 'Catálogo', to: ROUTES.catalog },
    ...result.breadcrumbs.map((crumb, index) => ({
      label: crumb.name,
      to: index < result.breadcrumbs.length - 1 ? ROUTES.category(crumb.slug) : undefined,
    })),
  ];

  return (
    <Container className="py-6 sm:py-8">
      <Breadcrumbs items={breadcrumbs} />

      <header className="mt-4 animate-fade-up">
        <SectionHeading as="h1" eyebrow={heading.eyebrow} title={heading.title} subtitle={heading.subtitle} />
        {result.facets.categories.length > 0 && (
          <nav aria-label={category ? `Subcategorías de ${category.name}` : 'Categorías'} className="mt-5">
            <ul className="-mx-4 flex gap-2 overflow-x-auto px-4 pb-1 scrollbar-none sm:mx-0 sm:flex-wrap sm:px-0">
              {result.facets.categories.map((facet) => (
                <li key={facet.code} className="shrink-0">
                  <LinkChip to={href({ category: facet.slug, page: 1 })} icon={<CategoryIcon name={facet.icon} />} count={facet.count} size="sm">
                    {facet.name}
                  </LinkChip>
                </li>
              ))}
            </ul>
          </nav>
        )}
      </header>

      <div className="mt-6 lg:grid lg:grid-cols-[17rem_minmax(0,1fr)] lg:items-start lg:gap-8">
        <aside
          aria-labelledby="filtros-titulo"
          className="hidden lg:sticky lg:top-[7.5rem] lg:block lg:max-h-[calc(100dvh-8.5rem)] lg:overflow-y-auto lg:pr-3 lg:[scrollbar-width:thin]"
        >
          <div className="mb-2 flex min-h-11 items-center justify-between gap-2">
            <h2 id="filtros-titulo" className="font-display text-lg font-semibold text-text">
              Filtros
              {activeCount > 0 && (
                <span className="ml-2 rounded-full bg-accent-soft px-2 py-0.5 font-sans text-xs font-semibold text-accent-hover tabular-nums">
                  {activeCount}
                  <span className="sr-only"> activos</span>
                </span>
              )}
            </h2>
            {anyActive && (
              <Button size="sm" variant="ghost" leftIcon={<RotateCcw />} onClick={clearAll}>
                Limpiar
              </Button>
            )}
          </div>
          <FilterPanel filters={filters} facets={result.facets} tree={tree} onChange={update} />
        </aside>

        <section aria-label="Resultados" className="min-w-0">
          <ResultsToolbar
            total={result.total}
            page={result.page}
            pageCount={result.pageCount}
            sort={filters.sort}
            view={filters.view}
            q={filters.q}
            scopeLabel={scopeLabel}
            activeFilters={activeCount}
            onSortChange={(sort) => update({ sort })}
            onViewChange={(view) => update({ view, page: filters.page })}
            onSearch={(q) => update({ q })}
            onOpenFilters={() => setFiltersOpen(true)}
          />
          {/* La categoría es el contexto (no cuenta como filtro): su chip solo acompaña a los filtros o la búsqueda activos. */}
          <ActiveFilters className="mt-3" filters={filters} brandName={brandName} category={anyActive ? category : undefined} onChange={applyAll} />

          {result.items.length === 0 ? (
            <EmptyResults className="mt-8" q={filters.q} hasFilters={anyActive} onClear={clearAll} suggestions={suggestions} />
          ) : filters.view === 'lista' ? (
            <ul key={catalogHref(filters)} className="mt-5 flex flex-col gap-3 animate-fade-up">
              {result.items.map((product, index) => (
                <li key={product.sku}>
                  <ProductRow product={product} priority={index < 3} />
                </li>
              ))}
            </ul>
          ) : (
            <ul key={catalogHref(filters)} className="mt-5 grid grid-cols-2 gap-3 animate-fade-up sm:gap-4 md:grid-cols-3 xl:grid-cols-4">
              {result.items.map((product, index) => (
                <li key={product.sku} className="min-w-0">
                  <ProductCard product={product} priority={index < 4} />
                </li>
              ))}
            </ul>
          )}

          <Pagination className="mt-10" page={result.page} pageCount={result.pageCount} getHref={(page) => href({ page })} />
        </section>
      </div>

      <FiltersDrawer open={filtersOpen} onClose={() => setFiltersOpen(false)} filters={filters} onApply={applyAll} />
    </Container>
  );
}
