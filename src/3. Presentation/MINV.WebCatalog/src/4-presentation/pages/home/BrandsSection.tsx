import { Link } from 'react-router-dom';
import type { Brand } from '@/1-domain/catalog/types';
import { ROUTES } from '@/4-presentation/app/routes';
import { SectionHeading } from '@/4-presentation/components/ui/SectionHeading';
import { pluralize } from '@/shared/format';

export interface BrandsSectionProps {
  brands: readonly Brand[];
  /** Cuántas marcas mostrar (las de más productos primero). */
  limit?: number;
}

/**
 * Marcas del catálogo como wordmarks tipográficos (sin logotipos de terceros). Cada una lleva a la búsqueda por su
 * nombre en el catálogo.
 */
export function BrandsSection({ brands, limit = 14 }: BrandsSectionProps) {
  const sorted = [...brands].sort((a, b) => b.productCount - a.productCount || a.name.localeCompare(b.name, 'es'));
  const visible = sorted.slice(0, limit);
  const remaining = Math.max(0, brands.length - visible.length);

  if (visible.length === 0) return null;

  return (
    <section aria-labelledby="marcas-titulo">
      <SectionHeading
        id="marcas-titulo"
        eyebrow="Distribución oficial"
        title="Las marcas que buscás"
        subtitle={`${pluralize(brands.length, 'marca', 'marcas')} con garantía del fabricante y servicio técnico propio.`}
        action={{ label: 'Ver todo el catálogo', to: ROUTES.catalog }}
      />
      <ul className="mt-6 flex flex-wrap gap-2 sm:gap-3">
        {visible.map((brand) => (
          <li key={brand.code}>
            <Link
              to={ROUTES.search(brand.name)}
              className="inline-flex h-12 items-center gap-2 rounded-xl border border-border bg-surface px-4 font-display text-base font-bold uppercase tracking-[0.14em] text-text-muted transition-[color,border-color,background-color] duration-200 hover:border-border-strong hover:bg-surface-2 hover:text-text sm:px-5"
              aria-label={`${brand.name}: ${pluralize(brand.productCount, 'producto', 'productos')}`}
            >
              {brand.name}
              <span aria-hidden="true" className="rounded-full bg-surface-3 px-1.5 py-0.5 font-sans text-xs font-medium normal-case tracking-normal text-text-faint tabular-nums">
                {brand.productCount}
              </span>
            </Link>
          </li>
        ))}
        {remaining > 0 && (
          <li>
            <Link
              to={ROUTES.catalog}
              className="inline-flex h-12 items-center rounded-xl border border-dashed border-border-strong px-4 text-sm font-semibold text-accent transition-colors duration-200 hover:text-accent-hover"
            >
              y {pluralize(remaining, 'marca más', 'marcas más')}
            </Link>
          </li>
        )}
      </ul>
    </section>
  );
}
