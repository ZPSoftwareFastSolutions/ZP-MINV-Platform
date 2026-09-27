import { ArrowRight } from 'lucide-react';
import { Link } from 'react-router-dom';
import type { CategoryNode } from '@/1-domain/catalog/categories';
import { ROUTES } from '@/4-presentation/app/routes';
import { CategoryIcon } from '@/4-presentation/components/ui/CategoryIcon';
import { EmptyState } from '@/4-presentation/components/ui/EmptyState';
import { SectionHeading } from '@/4-presentation/components/ui/SectionHeading';
import { pluralize } from '@/shared/format';

export interface CategoryGridProps {
  tree: CategoryNode[];
}

/** Grilla de categorías raíz con ícono, cantidad de productos y sus subcategorías principales. */
export function CategoryGrid({ tree }: CategoryGridProps) {
  return (
    <section aria-labelledby="categorias-titulo">
      <SectionHeading
        id="categorias-titulo"
        eyebrow="Explorá por categoría"
        title="Todo para tu setup"
        subtitle="Componentes, equipos, consolas y accesorios ordenados para que encuentres rápido lo que buscás."
        action={{ label: 'Ver el catálogo completo', to: ROUTES.catalog }}
      />
      {tree.length === 0 ? (
        <EmptyState className="mt-6" title="Todavía no hay categorías" description="El catálogo se está preparando. Volvé a intentarlo en un momento." />
      ) : (
        <ul className="mt-6 grid grid-cols-2 gap-3 sm:gap-4 md:grid-cols-3 lg:grid-cols-5">
          {tree.map((root) => (
            <li key={root.code} className="min-w-0">
              <Link
                to={ROUTES.category(root.slug)}
                className="group flex h-full flex-col gap-3 rounded-card border border-border bg-surface p-4 shadow-card transition-[border-color,box-shadow] duration-200 hover:border-border-strong hover:shadow-glow"
              >
                <span
                  aria-hidden="true"
                  className="flex size-11 items-center justify-center rounded-xl bg-primary-soft text-primary-text transition-colors duration-200 group-hover:bg-primary group-hover:text-white"
                >
                  <CategoryIcon name={root.icon} />
                </span>
                <span className="min-w-0">
                  <span className="block font-display text-base font-semibold text-text">{root.name}</span>
                  <span className="mt-0.5 block text-xs text-text-faint tabular-nums">{pluralize(root.productCount, 'producto', 'productos')}</span>
                </span>
                {root.children.length > 0 && (
                  <span className="line-clamp-2 text-xs leading-relaxed text-text-muted">{root.children.map((child) => child.name).join(' · ')}</span>
                )}
                <span className="mt-auto inline-flex items-center gap-1 pt-1 text-xs font-semibold text-accent transition-colors duration-200 group-hover:text-accent-hover">
                  Ver categoría
                  <ArrowRight aria-hidden="true" className="size-3.5" />
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
