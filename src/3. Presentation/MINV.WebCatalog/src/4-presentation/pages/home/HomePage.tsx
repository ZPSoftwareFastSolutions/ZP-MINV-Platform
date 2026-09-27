// Portada de Tech Zone Gaming: hero con productos reales del mock, campañas, categorías, destacados, ofertas, armados
// sugeridos, consolas por plataforma, novedades, marcas y el cierre con ayuda y boletín. Todo sale de los casos de uso
// (síncronos, en memoria): las secciones se calculan una vez con useMemo.

import { BadgePercent } from 'lucide-react';
import { useMemo } from 'react';
import { Link } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { Container } from '@/4-presentation/components/ui/Container';
import { useDocumentTitle } from '@/4-presentation/hooks/useDocumentTitle';
import { useServices } from '@/4-presentation/hooks/useServices';
import { formatPercent, pluralize } from '@/shared/format';
import { BrandsSection } from './BrandsSection';
import { CampaignCarousel } from './CampaignCarousel';
import { CategoryGrid } from './CategoryGrid';
import { ConsolesSection } from './ConsolesSection';
import { HeroSection } from './HeroSection';
import { NewsletterSection } from './NewsletterSection';
import { PresetsSection } from './PresetsSection';
import { ProductRail } from './ProductRail';
import { maxSavingPercent, pickHeroProducts } from './homeSelectors';

const CONSOLE_CATEGORY_SLUGS = ['consolas', 'videojuegos', 'accesorios-de-consola'] as const;

export function HomePage() {
  useDocumentTitle();
  const { catalog } = useServices();

  const data = useMemo(() => {
    const allOffers = catalog.getOffers(200);
    const consoleProducts = CONSOLE_CATEGORY_SLUGS.flatMap(
      (slug) => catalog.searchCatalog({ category: slug, sort: 'relevancia', pageSize: 96 }).items,
    ).sort((a, b) => b.popularity - a.popularity);
    return {
      hero: pickHeroProducts(catalog),
      stats: {
        products: catalog.searchCatalog({ pageSize: 1 }).total,
        brands: catalog.getBrands().length,
        presets: catalog.getPresets().length,
      },
      tree: catalog.getCategoryTree(),
      featured: catalog.getFeaturedProducts(8),
      offers: allOffers.slice(0, 8),
      offersTotal: allOffers.length,
      maxSaving: maxSavingPercent(allOffers),
      newArrivals: catalog.getNewArrivals(8),
      presets: catalog.getPresets(),
      consoles: consoleProducts,
      brands: catalog.getBrands(),
    };
  }, [catalog]);

  return (
    <>
      <HeroSection products={data.hero} stats={data.stats} />

      <Container className="space-y-16 py-12 sm:space-y-20 sm:py-16">
        <CampaignCarousel />

        <CategoryGrid tree={data.tree} />

        <ProductRail
          id="destacados-titulo"
          eyebrow="Lo más buscado"
          title="Destacados de la semana"
          subtitle="Lo que más se lleva la comunidad gamer: procesadores, tarjetas de video, periféricos y consolas."
          action={{ label: 'Ver todo el catálogo', to: ROUTES.catalog }}
          products={data.featured}
          priority
        />

        <ProductRail
          id="ofertas-titulo"
          eyebrow="Precios rebajados"
          title="Ofertas de la semana"
          subtitle="Descuentos reales sobre el precio de lista, con la misma garantía oficial."
          action={{ label: 'Ver todas las ofertas', to: ROUTES.offers }}
          products={data.offers}
          emptyTitle="No hay ofertas activas"
          emptyDescription="Las ofertas cambian cada semana. Suscribite al boletín para enterarte primero."
          ribbon={
            data.offersTotal > 0 ? (
              <div className="flex flex-col gap-2 rounded-xl border border-cta/40 bg-cta/10 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                <p className="flex items-center gap-2 text-sm font-semibold text-cta-hover">
                  <BadgePercent aria-hidden="true" className="size-5 shrink-0" />
                  {data.maxSaving > 0 ? `Ahorrá hasta ${formatPercent(data.maxSaving)} frente al precio de lista` : 'Precios rebajados frente al precio de lista'}
                </p>
                <Link to={ROUTES.offers} className="inline-flex min-h-11 items-center text-sm text-text-muted transition-colors duration-200 hover:text-text sm:min-h-0">
                  {pluralize(data.offersTotal, 'producto en oferta', 'productos en oferta')} · válidas hasta agotar stock
                </Link>
              </div>
            ) : null
          }
        />

        <PresetsSection presets={data.presets} />

        <ProductRail
          id="novedades-titulo"
          eyebrow="Recién llegados"
          title="Novedades"
          subtitle="Lo último en llegar a las sucursales: consolas, juegos y componentes de nueva generación."
          action={{ label: 'Ver todas las novedades', to: ROUTES.newArrivals }}
          products={data.newArrivals}
          emptyTitle="Sin novedades por ahora"
          emptyDescription="Cada semana llegan productos nuevos. Volvé pronto o mirá el catálogo completo."
        />

        <ConsolesSection products={data.consoles} />

        <BrandsSection brands={data.brands} />

        <NewsletterSection />
      </Container>
    </>
  );
}
