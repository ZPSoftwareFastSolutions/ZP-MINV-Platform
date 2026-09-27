import { Gamepad2 } from 'lucide-react';
import { useMemo, useState } from 'react';
import type { Product } from '@/1-domain/catalog/types';
import { ROUTES } from '@/4-presentation/app/routes';
import { ProductCard } from '@/4-presentation/components/product/ProductCard';
import { Button } from '@/4-presentation/components/ui/Button';
import { Chip } from '@/4-presentation/components/ui/Chip';
import { EmptyState } from '@/4-presentation/components/ui/EmptyState';
import { SectionHeading } from '@/4-presentation/components/ui/SectionHeading';
import { PLATFORM_FILTERS, matchesPlatform } from './homeSelectors';

export interface ConsolesSectionProps {
  /** Consolas, juegos y accesorios (ya ordenados por relevancia). */
  products: Product[];
  limit?: number;
}

const ALL = 'todas';

/** «Consolas y juegos»: chips de plataforma (PS5, Xbox Series X|S, Switch y Switch 2) que filtran productos reales. */
export function ConsolesSection({ products, limit = 8 }: ConsolesSectionProps) {
  const [platformId, setPlatformId] = useState<string>(ALL);
  const platform = PLATFORM_FILTERS.find((candidate) => candidate.id === platformId);

  const counts = useMemo(
    () => new Map(PLATFORM_FILTERS.map((filter) => [filter.id, products.filter((product) => matchesPlatform(product, filter)).length])),
    [products],
  );
  const visible = useMemo(() => products.filter((product) => matchesPlatform(product, platform)).slice(0, limit), [products, platform, limit]);
  const activeLabel = platform ? platform.label : 'todas las plataformas';

  return (
    <section aria-labelledby="consolas-titulo">
      <SectionHeading
        id="consolas-titulo"
        eyebrow="Consolas y juegos"
        title="PS5, Xbox Series X|S, Switch y Switch 2"
        subtitle="Consolas nuevas con garantía oficial y los juegos del momento en formato físico."
        action={{ label: 'Ver todas las consolas', to: ROUTES.category('consolas') }}
      />

      <div role="group" aria-label="Filtrar por plataforma" className="-mx-4 mt-6 flex gap-2 overflow-x-auto px-4 pb-1 scrollbar-none sm:mx-0 sm:flex-wrap sm:px-0">
        <Chip selected={platformId === ALL} icon={<Gamepad2 />} count={products.length} onClick={() => setPlatformId(ALL)}>
          Todas
        </Chip>
        {PLATFORM_FILTERS.map((filter) => (
          <Chip key={filter.id} selected={platformId === filter.id} count={counts.get(filter.id) ?? 0} onClick={() => setPlatformId(filter.id)}>
            {filter.label}
          </Chip>
        ))}
      </div>
      <p className="sr-only" aria-live="polite">
        Mostrando {visible.length} productos de {activeLabel}.
      </p>

      {visible.length === 0 ? (
        <EmptyState className="mt-6" icon={<Gamepad2 />} title={`Sin productos de ${activeLabel} por ahora`} description="Probá con otra plataforma o mirá todas las consolas del catálogo.">
          <Button type="button" variant="outline" onClick={() => setPlatformId(ALL)}>
            Ver todas las plataformas
          </Button>
        </EmptyState>
      ) : (
        <ul className="mt-6 grid grid-cols-2 gap-3 sm:gap-4 md:grid-cols-3 xl:grid-cols-4">
          {visible.map((product) => (
            <li key={product.sku} className="min-w-0">
              <ProductCard product={product} />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
