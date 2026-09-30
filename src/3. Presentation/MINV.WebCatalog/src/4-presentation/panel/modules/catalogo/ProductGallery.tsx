// Módulo «Catálogo» · vista de GALERÍA de la pestaña «Productos» (la galería con imágenes del escritorio): una tarjeta por
// producto con su imagen, precio, categoría, marca, disponible y distintivos (inactivo, serie o IMEI, garantía,
// plataformas), con el mismo menú «⋯» de la tabla. Ordena y pagina con el MISMO estado de la tabla (en la dirección);
// la pantalla pide las imágenes solo de la página a la vista.

import { ChevronLeft, ChevronRight, ImageOff, RotateCcw } from 'lucide-react';
import { Alert, Button, EmptyState, ErrorState, RowActions, SelectField, Skeleton, StatusBadge, type DataTableEmpty, type RowActionItem } from '@/4-presentation/panel/kit';
import { PAGE_SIZES, formatMoney, formatQuantity, rangeText, type PageSlice, type SortState } from '@/4-presentation/panel/lib';
import { GALLERY_SORTS, priceText, serialText, warrantyText, type ProductEntry } from './catalog';

export interface ProductGalleryProps {
  /** La página a la vista (null mientras carga). */
  slice: PageSlice<ProductEntry> | null;
  sort: SortState | null;
  onSortChange: (sort: SortState | null) => void;
  onPageChange: (page: number) => void;
  pageSize: number;
  onPageSizeChange: (size: number) => void;
  /** Imagen de cada variante (data:), de la página a la vista. */
  imageUrls: ReadonlyMap<string, string>;
  imagesLoading: boolean;
  error: unknown;
  onRetry: () => void;
  empty: DataTableEmpty;
  onOpen: (entry: ProductEntry) => void;
  actionsOf: (entry: ProductEntry) => RowActionItem[];
}

function sortValue(sort: SortState | null): string {
  return GALLERY_SORTS.find((option) => option.column === sort?.column && option.direction === sort?.direction)?.value ?? '';
}

export function ProductGallery({ slice, sort, onSortChange, onPageChange, pageSize, onPageSizeChange, imageUrls, imagesLoading, error, onRetry, empty, onOpen, actionsOf }: ProductGalleryProps) {
  const controls = (
    <div className="grid gap-3 sm:grid-cols-2 lg:max-w-2xl">
      <SelectField
        label="Ordenar por"
        allLabel="Como llegan"
        value={sortValue(sort)}
        onChange={(value) => {
          const option = GALLERY_SORTS.find((item) => item.value === value);
          onSortChange(option ? { column: option.column, direction: option.direction } : null);
        }}
        options={GALLERY_SORTS}
      />
      <SelectField
        label="Productos por página"
        allLabel={false}
        value={String(pageSize)}
        onChange={(value) => onPageSizeChange(Number(value))}
        options={PAGE_SIZES.map((size) => ({ value: String(size), label: String(size) }))}
      />
    </div>
  );

  if (!slice) {
    if (error) return <ErrorState error={error} operation="GetCatalogQuery" onRetry={onRetry} />;
    return (
      <div className="grid grid-cols-[repeat(auto-fill,minmax(13rem,1fr))] gap-3" aria-busy="true" data-testid="galeria">
        <p role="status" className="sr-only">
          Cargando productos…
        </p>
        {Array.from({ length: 8 }, (_, index) => (
          <Skeleton key={index} className="h-72 w-full rounded-card" />
        ))}
      </div>
    );
  }

  if (slice.total === 0) {
    return (
      <div className="rounded-card border border-border bg-surface shadow-card" data-testid="galeria">
        <EmptyState size="sm" icon={empty.icon} title={empty.title} description={empty.description}>
          {empty.action}
        </EmptyState>
      </div>
    );
  }

  return (
    <div className="space-y-4" data-testid="galeria">
      {controls}
      {error != null && (
        <Alert
          tone="danger"
          title="No se pudo actualizar la lista"
          actions={
            <Button variant="outline" leftIcon={<RotateCcw />} onClick={onRetry}>
              Reintentar
            </Button>
          }
        />
      )}
      <ul className="grid grid-cols-[repeat(auto-fill,minmax(13rem,1fr))] gap-3" aria-label="Productos del catálogo">
        {slice.rows.map((entry) => {
          const url = imageUrls.get(entry.row.variantId);
          return (
            <li key={entry.key} data-row-key={entry.key} className="flex min-w-0 flex-col overflow-hidden rounded-card border border-border bg-surface shadow-card">
              <div className="relative flex aspect-square items-center justify-center bg-surface-2">
                {url ? (
                  <img src={url} alt={`Imagen de ${entry.row.name}`} className="size-full object-contain" loading="lazy" />
                ) : entry.row.hasImage && imagesLoading ? (
                  <Skeleton className="size-full" />
                ) : (
                  <span className="flex flex-col items-center gap-1 text-xs text-text-muted">
                    <ImageOff aria-hidden="true" className="size-8" />
                    Sin imagen
                  </span>
                )}
                {!entry.row.isActive && (
                  <span className="absolute top-2 left-2">
                    <StatusBadge tone="neutral">Inactivo</StatusBadge>
                  </span>
                )}
              </div>
              <div className="flex min-w-0 flex-1 flex-col gap-2 p-3">
                <div className="flex items-start justify-between gap-1">
                  <button
                    type="button"
                    onClick={() => onOpen(entry)}
                    className="min-h-11 min-w-0 cursor-pointer text-left font-medium text-text underline-offset-2 hover:text-accent-hover hover:underline"
                  >
                    {entry.row.name}
                  </button>
                  <RowActions label={`Acciones de ${entry.row.name}`} actions={actionsOf(entry)} />
                </div>
                <p className="text-xs text-text-muted">
                  {entry.row.sku} · {entry.row.category}
                  {entry.brand ? ` · ${entry.brand}` : ''}
                </p>
                <p className="font-display text-lg font-semibold text-text tabular-nums">{priceText(entry.row.salePrice, formatMoney)}</p>
                <div className="flex flex-wrap gap-1.5">
                  {(entry.serial === 'serie' || entry.serial === 'imei') && <StatusBadge tone="info">{serialText(entry)}</StatusBadge>}
                  {entry.warrantyMonths > 0 && <StatusBadge tone="success">{warrantyText(entry.warrantyMonths)}</StatusBadge>}
                  {entry.platforms.map((platform) => (
                    <StatusBadge key={platform} tone="accent">
                      {platform}
                    </StatusBadge>
                  ))}
                </div>
                {entry.keySpecs && <p className="line-clamp-2 text-xs text-text-muted">{entry.keySpecs}</p>}
                <p className="mt-auto text-sm text-text-muted">{entry.available === null ? 'Disponible: —' : `Disponible: ${formatQuantity(entry.available, { unit: entry.row.unit })}`}</p>
              </div>
            </li>
          );
        })}
      </ul>
      <nav aria-label="Páginas de la galería" className="flex flex-wrap items-center justify-between gap-3">
        <span className="text-sm text-text-muted">{rangeText(slice)}</span>
        <div className="flex gap-2">
          <Button variant="outline" leftIcon={<ChevronLeft />} disabled={slice.page <= 1} onClick={() => onPageChange(slice.page - 1)}>
            Anterior
          </Button>
          <Button variant="outline" rightIcon={<ChevronRight />} disabled={slice.page >= slice.pageCount} onClick={() => onPageChange(slice.page + 1)}>
            Siguiente
          </Button>
        </div>
      </nav>
    </div>
  );
}
