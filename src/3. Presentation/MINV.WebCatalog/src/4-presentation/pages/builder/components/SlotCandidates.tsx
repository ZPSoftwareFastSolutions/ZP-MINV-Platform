// Candidatos de una ranura: búsqueda, chips de marca, «solo en stock», orden y la lista de tarjetas compactas.
// Todo el filtrado ocurre en el cliente sobre el mock (getSlotCandidates); el estado de los filtros vive en el paso.

import { ArrowUpDown, PackageSearch, Search, X } from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import type { BuildLine, BuildSlot } from '@/1-domain/builder/types';
import { isProductSort, PRODUCT_SORTS, type ProductSort } from '@/1-domain/catalog/products';
import type { Product } from '@/1-domain/catalog/types';
import { Button } from '@/4-presentation/components/ui/Button';
import { Chip } from '@/4-presentation/components/ui/Chip';
import { EmptyState } from '@/4-presentation/components/ui/EmptyState';
import { useServices } from '@/4-presentation/hooks/useServices';
import { pluralize } from '@/shared/format';
import { CandidateCard } from './CandidateCard';

export interface SlotCandidatesProps {
  slot: BuildSlot;
  /** Líneas del armado que ya ocupan esta ranura. */
  lines: readonly BuildLine[];
  onChoose: (product: Product) => void;
  onRemove: (sku: string) => void;
  onQuantity: (sku: string, quantity: number) => void;
}

const PAGE = 8;

export function SlotCandidates({ slot, lines, onChoose, onRemove, onQuantity }: SlotCandidatesProps) {
  const { catalog } = useServices();
  const searchId = useId();
  const sortId = useId();
  const [q, setQ] = useState('');
  const [brands, setBrands] = useState<string[]>([]);
  const [inStock, setInStock] = useState(false);
  const [sort, setSort] = useState<ProductSort>('relevancia');
  const [visible, setVisible] = useState(PAGE);

  const result = useMemo(
    () => catalog.getSlotCandidates(slot.key, { q: q.trim() || undefined, brands: brands.length ? brands : undefined, inStock: inStock || undefined, sort }),
    [catalog, slot.key, q, brands, inStock, sort],
  );
  const quantities = useMemo(() => new Map(lines.map((line) => [line.product.sku, line.quantity])), [lines]);
  const hasFilters = q.trim() !== '' || brands.length > 0 || inStock;

  const toggleBrand = (code: string) => {
    setBrands((current) => (current.includes(code) ? current.filter((item) => item !== code) : [...current, code]));
    setVisible(PAGE);
  };
  const resetFilters = () => {
    setQ('');
    setBrands([]);
    setInStock(false);
    setVisible(PAGE);
  };

  const items = result.items.slice(0, visible);

  return (
    <div className="space-y-4">
      <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
        <div className="relative flex w-full items-center md:max-w-xs">
          <label htmlFor={searchId} className="sr-only">
            Buscar en {slot.label}
          </label>
          <Search aria-hidden="true" className="pointer-events-none absolute left-3 size-4 text-text-faint" />
          <input
            id={searchId}
            type="search"
            value={q}
            onChange={(event) => {
              setQ(event.target.value);
              setVisible(PAGE);
            }}
            autoComplete="off"
            placeholder={`Buscar en ${slot.label.toLowerCase()}…`}
            className="h-11 w-full rounded-xl border border-border bg-surface pl-10 pr-12 text-sm text-text placeholder:text-text-faint transition-colors duration-200 hover:border-border-strong focus:border-accent focus:outline-none [&::-webkit-search-cancel-button]:hidden"
          />
          {q && (
            <button
              type="button"
              aria-label="Borrar búsqueda"
              onClick={() => setQ('')}
              className="absolute inset-y-0 right-0 flex w-11 cursor-pointer items-center justify-center rounded-r-xl text-text-muted transition-colors duration-200 hover:bg-surface-3 hover:text-text"
            >
              <X aria-hidden="true" className="size-4" />
            </button>
          )}
        </div>
        <div className="flex items-center gap-2">
          <Chip selected={inStock} onClick={() => setInStock((value) => !value)}>
            Solo en stock
          </Chip>
          <label htmlFor={sortId} className="sr-only">
            Ordenar por
          </label>
          <div className="relative">
            <ArrowUpDown aria-hidden="true" className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-text-faint" />
            <select
              id={sortId}
              value={sort}
              onChange={(event) => {
                if (isProductSort(event.target.value)) setSort(event.target.value);
              }}
              className="h-11 cursor-pointer appearance-none rounded-xl border border-border bg-surface pl-9 pr-8 text-sm font-medium text-text transition-colors duration-200 hover:border-border-strong focus:border-accent focus:outline-none"
            >
              {PRODUCT_SORTS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </div>
        </div>
      </div>

      {result.facets.brands.length > 1 && (
        <div className="-mx-1 flex gap-2 overflow-x-auto px-1 pb-1 scrollbar-none" role="group" aria-label={`Filtrar ${slot.label} por marca`}>
          {result.facets.brands.map((brand) => (
            <Chip key={brand.code} selected={brands.includes(brand.code)} count={brand.count} onClick={() => toggleBrand(brand.code)}>
              {brand.name}
            </Chip>
          ))}
        </div>
      )}

      <p className="text-sm text-text-muted" role="status">
        {pluralize(result.total, 'opción', 'opciones')}
        {hasFilters && ' con estos filtros'}
      </p>

      {items.length === 0 ? (
        <EmptyState size="sm" icon={<PackageSearch />} title="Sin resultados" description="Probá con otra palabra o quitá algún filtro.">
          <Button variant="outline" onClick={resetFilters}>
            Quitar filtros
          </Button>
        </EmptyState>
      ) : (
        <ul className="space-y-2" aria-label={`Opciones para ${slot.label}`}>
          {items.map((product) => (
            <CandidateCard
              key={product.sku}
              product={product}
              slot={slot}
              quantity={quantities.get(product.sku) ?? 0}
              onChoose={onChoose}
              onRemove={onRemove}
              onQuantity={onQuantity}
            />
          ))}
        </ul>
      )}

      {result.total > visible && (
        <div className="flex justify-center">
          <Button variant="subtle" onClick={() => setVisible((value) => value + PAGE)}>
            Mostrar {pluralize(Math.min(PAGE, result.total - visible), 'opción más', 'opciones más')}
          </Button>
        </div>
      )}
    </div>
  );
}
