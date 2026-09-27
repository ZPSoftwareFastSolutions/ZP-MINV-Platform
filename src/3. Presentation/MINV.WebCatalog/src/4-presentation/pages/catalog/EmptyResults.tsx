// Estado vacío del catálogo con sugerencias: limpiar filtros, ver todo y accesos a categorías populares.

import { RotateCcw, SearchX } from 'lucide-react';
import type { Category } from '@/1-domain/catalog/types';
import { ROUTES } from '@/4-presentation/app/routes';
import { Button } from '@/4-presentation/components/ui/Button';
import { CategoryIcon } from '@/4-presentation/components/ui/CategoryIcon';
import { EmptyState } from '@/4-presentation/components/ui/EmptyState';
import { LinkChip } from './LinkChip';

export interface EmptyResultsProps {
  q?: string;
  hasFilters: boolean;
  onClear: () => void;
  /** Categorías con más productos para seguir explorando. */
  suggestions: readonly Category[];
  className?: string;
}

export function EmptyResults({ q, hasFilters, onClear, suggestions, className }: EmptyResultsProps) {
  const title = q ? `No encontramos resultados para “${q}”` : 'No hay productos con estos filtros';
  const description = q
    ? 'Revisá la ortografía o probá con palabras más generales, como el modelo («rtx 5070»), la marca o el tipo de pieza («ddr5»).'
    : 'Probá con menos filtros, ampliá el rango de precio o recorré otra categoría.';

  return (
    <div className={className}>
      <EmptyState icon={<SearchX />} title={title} description={description}>
        {hasFilters && (
          <Button variant="brand" leftIcon={<RotateCcw />} onClick={onClear}>
            {q ? 'Quitar búsqueda y filtros' : 'Probá con menos filtros'}
          </Button>
        )}
        <Button to={ROUTES.catalog} variant="outline">
          Ver todo el catálogo
        </Button>
      </EmptyState>
      {suggestions.length > 0 && (
        <div className="mt-6 text-center">
          <p className="text-sm font-semibold text-text-muted">Categorías populares</p>
          <ul className="mt-3 flex flex-wrap justify-center gap-2">
            {suggestions.map((category) => (
              <li key={category.code}>
                <LinkChip to={ROUTES.category(category.slug)} icon={<CategoryIcon name={category.icon} />} count={category.productCount} size="sm">
                  {category.name}
                </LinkChip>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
