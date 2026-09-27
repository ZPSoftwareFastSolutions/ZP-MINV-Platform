// Chips de los filtros aplicados (con «x» para quitar cada uno) y «Limpiar todo».

import clsx from 'clsx';
import { RotateCcw } from 'lucide-react';
import type { Category } from '@/1-domain/catalog/types';
import { Button } from '@/4-presentation/components/ui/Button';
import { Chip } from '@/4-presentation/components/ui/Chip';
import { formatMoney } from '@/shared/format';
import { clearedFilters, hasActiveFilters, type CatalogFilters } from './catalogFilters';

export interface ActiveFiltersProps {
  filters: CatalogFilters;
  /** Nombre visible de una marca a partir de su código. */
  brandName: (code: string) => string;
  category?: Category;
  onChange: (next: CatalogFilters) => void;
  className?: string;
}

const TAG_LABELS: Record<string, string> = { oferta: 'En oferta', nuevo: 'Novedades', destacado: 'Destacados' };

function priceLabel(filters: CatalogFilters): string {
  const min = filters.minPrice != null ? formatMoney(filters.minPrice, { decimals: 0 }) : null;
  const max = filters.maxPrice != null ? formatMoney(filters.maxPrice, { decimals: 0 }) : null;
  if (min && max) return `${min} – ${max}`;
  if (min) return `Desde ${min}`;
  return `Hasta ${max}`;
}

export function ActiveFilters({ filters, brandName, category, onChange, className }: ActiveFiltersProps) {
  if (!hasActiveFilters(filters) && !category) return null;
  const chips: { key: string; label: string; remove: () => void }[] = [];
  const patch = (next: Partial<CatalogFilters>) => onChange({ ...filters, ...next, page: 1 });

  if (category) chips.push({ key: 'categoria', label: `Categoría: ${category.name}`, remove: () => patch({ category: undefined }) });
  if (filters.q) chips.push({ key: 'q', label: `Búsqueda: “${filters.q}”`, remove: () => patch({ q: undefined }) });
  for (const code of filters.brands) {
    chips.push({ key: `marca-${code}`, label: brandName(code), remove: () => patch({ brands: filters.brands.filter((brand) => brand !== code) }) });
  }
  if (filters.minPrice != null || filters.maxPrice != null) {
    chips.push({ key: 'precio', label: priceLabel(filters), remove: () => patch({ minPrice: undefined, maxPrice: undefined }) });
  }
  if (filters.condition) chips.push({ key: 'condicion', label: filters.condition, remove: () => patch({ condition: undefined }) });
  if (filters.inStock) chips.push({ key: 'stock', label: 'Solo con stock', remove: () => patch({ inStock: false }) });
  for (const tag of filters.tags) {
    chips.push({ key: `tag-${tag}`, label: TAG_LABELS[tag] ?? tag, remove: () => patch({ tags: filters.tags.filter((value) => value !== tag) }) });
  }

  return (
    <div className={clsx('flex flex-wrap items-center gap-2', className)} aria-label="Filtros aplicados">
      {chips.map((chip) => (
        <Chip key={chip.key} size="sm" selected aria-label={`Quitar filtro: ${chip.label}`} onClick={chip.remove} onRemove={chip.remove}>
          {chip.label}
        </Chip>
      ))}
      {hasActiveFilters(filters) && (
        <Button size="sm" variant="ghost" leftIcon={<RotateCcw />} onClick={() => onChange(clearedFilters(filters))}>
          Limpiar todo
        </Button>
      )}
    </div>
  );
}
