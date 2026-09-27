// PLACEHOLDER: la página de inicio completa (hero, banner, destacados, ofertas, categorías) la construye el agente
// de Inicio sobre este archivo. Aquí solo hay una vista mínima que ejercita el kit UI y las tarjetas.

import { Cpu, Search } from 'lucide-react';
import { ROUTES } from '@/4-presentation/app/routes';
import { ProductCard } from '@/4-presentation/components/product/ProductCard';
import { Button } from '@/4-presentation/components/ui/Button';
import { Container } from '@/4-presentation/components/ui/Container';
import { SectionHeading } from '@/4-presentation/components/ui/SectionHeading';
import { useDocumentTitle } from '@/4-presentation/hooks/useDocumentTitle';
import { useServices } from '@/4-presentation/hooks/useServices';
import { STORE } from '@/shared/constants';

export function HomePage() {
  useDocumentTitle();
  const { catalog } = useServices();
  const featured = catalog.getFeaturedProducts(8);

  return (
    <Container className="space-y-12 py-10">
      <section className="animate-fade-up">
        <p className="text-xs font-semibold uppercase tracking-[0.2em] text-accent">Tech Zone Gaming</p>
        <h1 className="mt-2 max-w-3xl font-display text-4xl font-bold tracking-tight text-text sm:text-5xl">
          Armá la PC que querés, <span className="text-gradient-brand">pieza por pieza</span>.
        </h1>
        <p className="mt-4 max-w-2xl text-lg text-text-muted">{STORE.tagline}</p>
        <div className="mt-6 flex flex-wrap gap-3">
          <Button to={ROUTES.builder} variant="brand" size="lg" leftIcon={<Cpu />}>
            Armá tu PC
          </Button>
          <Button to={ROUTES.catalog} variant="outline" size="lg" leftIcon={<Search />}>
            Explorar el catálogo
          </Button>
        </div>
      </section>

      <section aria-labelledby="destacados">
        <SectionHeading id="destacados" eyebrow="Lo más buscado" title="Productos destacados" subtitle="Una muestra del catálogo de prueba." action={{ label: 'Ver todo', to: ROUTES.catalog }} />
        <div className="mt-6 grid grid-cols-2 gap-3 sm:gap-4 md:grid-cols-3 xl:grid-cols-4">
          {featured.map((product, index) => (
            <ProductCard key={product.sku} product={product} priority={index < 4} />
          ))}
        </div>
      </section>
    </Container>
  );
}
