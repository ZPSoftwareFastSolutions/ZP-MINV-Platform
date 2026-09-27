// Panel de filtros del catálogo: categorías (árbol con conteos), marcas, precio, condición, stock y etiquetas.
// Es un componente controlado: recibe los filtros y avisa cada cambio con `onChange`. En escritorio el cambio se
// aplica al instante; en el cajón móvil se acumula en un borrador y se aplica con «Ver N productos».

import clsx from 'clsx';
import { Check, ChevronDown, LayoutGrid } from 'lucide-react';
import { useId, useState, type ReactNode } from 'react';
import type { Condition, ProductTag } from '@/1-domain/catalog/types';
import type { CatalogFacets } from '@/2-application';
import { CategoryIcon } from '@/4-presentation/components/ui/CategoryIcon';
import { Chip } from '@/4-presentation/components/ui/Chip';
import type { CatalogFilters } from './catalogFilters';
import type { CategoryFilterNode, CategoryFilterTree } from '@/2-application/catalog/types';
import { PriceRangeFilter } from './PriceRangeFilter';

export interface FilterPanelProps {
  filters: CatalogFilters;
  facets: CatalogFacets;
  tree: CategoryFilterTree;
  onChange: (patch: Partial<CatalogFilters>) => void;
  className?: string;
}

const VISIBLE_BRANDS = 6;

const TAG_OPTIONS: readonly { tag: ProductTag; label: string }[] = [
  { tag: 'oferta', label: 'En oferta' },
  { tag: 'nuevo', label: 'Novedades' },
  { tag: 'destacado', label: 'Destacados' },
];

/** Sección plegable del panel (aria-expanded + aria-controls). El contenido queda montado con `hidden`. */
function FilterSection({ title, badge, defaultOpen = true, children }: { title: string; badge?: number; defaultOpen?: boolean; children: ReactNode }) {
  const [open, setOpen] = useState(defaultOpen);
  const panelId = useId();
  return (
    <section className="border-b border-border py-3 first:pt-0 last:border-b-0">
      <h3 className="font-sans text-sm">
        <button
          type="button"
          aria-expanded={open}
          aria-controls={panelId}
          onClick={() => setOpen((current) => !current)}
          className="flex min-h-11 w-full cursor-pointer items-center justify-between gap-2 rounded-lg text-left font-semibold text-text transition-colors duration-200 hover:text-accent-hover"
        >
          <span className="inline-flex items-center gap-2">
            {title}
            {badge != null && badge > 0 && (
              <span className="rounded-full bg-accent-soft px-1.5 py-0.5 text-xs font-semibold text-accent-hover tabular-nums">
                {badge}
                <span className="sr-only"> filtros activos</span>
              </span>
            )}
          </span>
          <ChevronDown aria-hidden="true" className={clsx('size-4 shrink-0 text-text-faint transition-transform duration-200', open && 'rotate-180')} />
        </button>
      </h3>
      <div id={panelId} hidden={!open} className="pb-1">
        {children}
      </div>
    </section>
  );
}

/** Fila de casilla accesible con conteo a la derecha. */
function CheckboxRow({ checked, onChange, label, count, disabled = false }: { checked: boolean; onChange: () => void; label: string; count?: number; disabled?: boolean }) {
  return (
    <label
      className={clsx(
        'flex min-h-11 cursor-pointer items-center gap-3 rounded-lg px-2 text-sm transition-colors duration-200 lg:min-h-10',
        disabled ? 'cursor-not-allowed opacity-50' : 'hover:bg-surface-2',
        checked ? 'text-text' : 'text-text-muted hover:text-text',
      )}
    >
      <input type="checkbox" className="peer sr-only" checked={checked} disabled={disabled} onChange={onChange} />
      <span
        aria-hidden="true"
        className="flex size-5 shrink-0 items-center justify-center rounded-md border border-border-strong bg-surface-2 transition-colors duration-200 peer-checked:border-accent peer-checked:bg-accent peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-accent [&_svg]:opacity-0 peer-checked:[&_svg]:opacity-100"
      >
        <Check className="size-3.5 text-bg" strokeWidth={3} />
      </span>
      <span className="flex-1 truncate">{label}</span>
      {count != null && <span className="text-xs text-text-faint tabular-nums">{count}</span>}
    </label>
  );
}

function CategoryButton({ node, depth = 0, onSelect }: { node: Pick<CategoryFilterNode, 'name' | 'count' | 'selected' | 'icon'>; depth?: number; onSelect: () => void }) {
  return (
    <button
      type="button"
      aria-current={node.selected ? 'true' : undefined}
      onClick={onSelect}
      className={clsx(
        'flex min-h-11 w-full cursor-pointer items-center gap-2.5 rounded-lg px-2 text-left text-sm transition-colors duration-200 lg:min-h-10',
        node.selected ? 'bg-surface-2 font-semibold text-accent-hover' : 'text-text-muted hover:bg-surface-2 hover:text-text',
      )}
    >
      {depth === 0 && <CategoryIcon name={node.icon} className={clsx('size-4 shrink-0', node.selected ? 'text-accent-hover' : 'text-text-faint')} />}
      <span className="flex-1 truncate">{node.name}</span>
      <span className="text-xs text-text-faint tabular-nums">{node.count}</span>
    </button>
  );
}

export function FilterPanel({ filters, facets, tree, onChange, className }: FilterPanelProps) {
  const [allBrands, setAllBrands] = useState(false);
  const priceActive = filters.minPrice != null || filters.maxPrice != null;
  const visibleBrands = allBrands ? facets.brands : facets.brands.filter((brand, index) => index < VISIBLE_BRANDS || filters.brands.includes(brand.code));
  const hiddenBrands = facets.brands.length - visibleBrands.length;
  const showCondition = facets.conditions.length > 1 || filters.condition != null;

  const toggleBrand = (code: string) =>
    onChange({ brands: filters.brands.includes(code) ? filters.brands.filter((brand) => brand !== code) : [...filters.brands, code] });
  const toggleTag = (tag: ProductTag) =>
    onChange({ tags: filters.tags.includes(tag) ? filters.tags.filter((value) => value !== tag) : [...filters.tags, tag] });
  const setCondition = (condition: Condition) => onChange({ condition: filters.condition === condition ? undefined : condition });

  return (
    <div className={className}>
      <FilterSection title="Categorías">
        <ul className="space-y-0.5">
          <li>
            <CategoryButton
              node={{ name: 'Todas las categorías', count: tree.total, selected: !filters.category, icon: 'LayoutGrid' }}
              onSelect={() => onChange({ category: undefined })}
            />
          </li>
          {tree.roots.map((root) => (
            <li key={root.code}>
              <CategoryButton node={root} onSelect={() => onChange({ category: root.slug })} />
              {root.children.length > 0 && (
                <ul className="mt-0.5 mb-1 ml-4 space-y-0.5 border-l border-border pl-2">
                  {root.children.map((child) => (
                    <li key={child.code}>
                      <CategoryButton node={child} depth={1} onSelect={() => onChange({ category: child.slug })} />
                    </li>
                  ))}
                </ul>
              )}
            </li>
          ))}
        </ul>
      </FilterSection>

      {facets.brands.length > 0 && (
        <FilterSection title="Marcas" badge={filters.brands.length}>
          <ul className="space-y-0.5">
            {visibleBrands.map((brand) => (
              <li key={brand.code}>
                <CheckboxRow checked={filters.brands.includes(brand.code)} onChange={() => toggleBrand(brand.code)} label={brand.name} count={brand.count} />
              </li>
            ))}
          </ul>
          {(hiddenBrands > 0 || allBrands) && facets.brands.length > VISIBLE_BRANDS && (
            <button
              type="button"
              aria-expanded={allBrands}
              onClick={() => setAllBrands((current) => !current)}
              className="mt-1 inline-flex min-h-10 cursor-pointer items-center gap-1 rounded-lg px-2 text-sm font-semibold text-accent transition-colors duration-200 hover:text-accent-hover"
            >
              {allBrands ? 'Ver menos marcas' : `Ver todas las marcas (${facets.brands.length})`}
              <ChevronDown aria-hidden="true" className={clsx('size-4 transition-transform duration-200', allBrands && 'rotate-180')} />
            </button>
          )}
        </FilterSection>
      )}

      <FilterSection title="Precio" badge={priceActive ? 1 : 0}>
        <PriceRangeFilter
          bounds={facets.priceRange}
          value={{ min: filters.minPrice, max: filters.maxPrice }}
          onCommit={(next) => onChange({ minPrice: next.min, maxPrice: next.max })}
        />
      </FilterSection>

      {showCondition && (
        <FilterSection title="Condición" badge={filters.condition ? 1 : 0}>
          <div className="flex flex-wrap gap-2 px-1 pt-1">
            {facets.conditions.map((facet) => (
              <Chip key={facet.value} size="sm" selected={filters.condition === facet.value} count={facet.count} onClick={() => setCondition(facet.value)}>
                {facet.value}
              </Chip>
            ))}
            {filters.condition && !facets.conditions.some((facet) => facet.value === filters.condition) && (
              <Chip size="sm" selected count={0} onClick={() => setCondition(filters.condition as Condition)}>
                {filters.condition}
              </Chip>
            )}
          </div>
        </FilterSection>
      )}

      <FilterSection title="Disponibilidad y etiquetas" badge={(filters.inStock ? 1 : 0) + filters.tags.length}>
        <ul className="space-y-0.5">
          <li>
            <CheckboxRow checked={filters.inStock} onChange={() => onChange({ inStock: !filters.inStock })} label="Solo con stock" />
          </li>
          {TAG_OPTIONS.map((option) => (
            <li key={option.tag}>
              <CheckboxRow checked={filters.tags.includes(option.tag)} onChange={() => toggleTag(option.tag)} label={option.label} />
            </li>
          ))}
        </ul>
      </FilterSection>
      <span className="sr-only">
        <LayoutGrid aria-hidden="true" />
      </span>
    </div>
  );
}
