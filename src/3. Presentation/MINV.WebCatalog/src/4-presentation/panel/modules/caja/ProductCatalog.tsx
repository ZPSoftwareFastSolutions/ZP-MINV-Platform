// Módulo «Caja» · los productos a la venta: buscador que también recibe el LECTOR DE CÓDIGOS (funciona como teclado:
// escribe el código y pulsa Enter, y el producto se agrega), filtros con listas desplegables (categoría, plataforma y
// condición de las opciones del servidor, disponibilidad) en la dirección de la página, tabla ordenable y paginada con
// «Agregar» en cada fila, detalle lateral y exportación a CSV. Un código que no está a la venta se explica
// (`GetProductLookupQuery`: inactivo o sin precio).

import { Barcode, Download, Eye, Filter, PackageSearch, Plus, RefreshCw, ScanBarcode, X } from 'lucide-react';
import { useEffect, useMemo, useState, type KeyboardEvent, type RefObject } from 'react';
import { useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Alert,
  Button,
  DataTable,
  DetailList,
  FilterBar,
  SelectField,
  SidePanel,
  StatusBadge,
  TextField,
  Toolbar,
  useNotify,
  type DataTableColumn,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatMoney, formatNumber, formatQuantity } from '@/4-presentation/panel/lib';
import {
  CONDITION_SPEC,
  PRODUCT_CSV,
  PRODUCT_FILTERS,
  STOCK_OPTIONS,
  availabilityText,
  categoryOptions,
  conditionOptions,
  filterProducts,
  findByCode,
  findLookup,
  platformOptions,
  serialLabel,
  skuSet,
  unknownCodeText,
  warrantyText,
  type CajaProduct,
} from './products';
import type { SpecDefinitionData } from './types';

export interface ProductCatalogProps {
  /** Productos a la venta (undefined mientras llegan). */
  products: readonly CajaProduct[] | undefined;
  loading: boolean;
  refreshing: boolean;
  error: unknown;
  onRetry: () => void;
  specs: readonly SpecDefinitionData[] | undefined;
  /** No se pudo leer la ficha técnica (qué productos llevan serie). */
  techError: unknown;
  onRetryTech: () => void;
  canLookup: boolean;
  /** Por qué no se puede agregar a la venta ahora (null = se puede). */
  blocked: string | null;
  /** Cantidad de cada SKU (en mayúsculas) que ya está en la venta. */
  inCart: ReadonlyMap<string, number>;
  onAdd: (product: CajaProduct) => void;
  searchRef: RefObject<HTMLInputElement | null>;
}

const LOOKUP_REQUEST = { includeInactive: true };

/** Texto del botón: «Agregar» o, con serie, «Elegir serie» / «Elegir IMEI» (se elige la unidad). */
function addLabel(product: CajaProduct): string {
  return product.serialized ? `Elegir ${product.serialKind === 'Imei' ? 'IMEI' : 'serie'}` : 'Agregar';
}

/** Por qué no se puede agregar el producto (null = se puede). */
function addBlocked(product: CajaProduct, blocked: string | null): string | null {
  return blocked ?? (product.available <= 0 ? 'Producto agotado: no hay unidades disponibles.' : null);
}

export function ProductCatalog({ products, loading, refreshing, error, onRetry, specs, techError, onRetryTech, canLookup, blocked, inCart, onAdd, searchRef }: ProductCatalogProps) {
  const notify = useNotify();
  const table = useTableState({ filters: PRODUCT_FILTERS, sort: null });
  const { filters, setFilter } = table;

  // La búsqueda: el texto se aplica al dejar de escribir (en la dirección) y Enter agrega el producto del código.
  const [text, setText] = useState(filters.q);
  const [applied, setApplied] = useState(filters.q);
  if (applied !== filters.q) {
    setApplied(filters.q);
    if (text.trim() !== filters.q) setText(filters.q);
  }
  useEffect(() => {
    const next = text.trim();
    if (next === filters.q) return;
    const timer = setTimeout(() => setFilter('q', next), 300);
    return () => clearTimeout(timer);
  }, [text, filters.q, setFilter]);

  // Un código que no está a la venta: se explica con el catálogo completo (se pide recién la primera vez).
  const [missed, setMissed] = useState<string | null>(null);
  const lookup = useRpcQuery('GetProductLookupQuery', LOOKUP_REQUEST, { enabled: missed !== null && canLookup });

  // «Condición» la filtra el servidor (es una especificación del producto).
  const condition = useRpcQuery(
    'SearchTechProductsQuery',
    { text: null, categoryCode: null, platform: null, filters: [{ code: CONDITION_SPEC, values: [filters.condicion], min: null, max: null }], onlyInStock: false, max: 2000 },
    { enabled: filters.condicion !== '' },
  );
  const conditionSkus = useMemo(() => (filters.condicion !== '' && condition.data ? skuSet(condition.data) : null), [filters.condicion, condition.data]);
  const waitingCondition = filters.condicion !== '' && condition.data === undefined;

  const all = useMemo(() => products ?? [], [products]);
  const rows = useMemo(() => filterProducts(all, filters, conditionSkus), [all, filters, conditionSkus]);
  const categories = useMemo(() => categoryOptions(all), [all]);
  const platforms = useMemo(() => platformOptions(specs, all), [specs, all]);
  const conditions = useMemo(() => conditionOptions(specs), [specs]);
  const inStock = rows.filter((product) => product.available > 0).length;

  const [detail, setDetail] = useState<{ product: CajaProduct; open: boolean } | null>(null);
  const openDetail = (product: CajaProduct) => setDetail({ product, open: true });

  const clearSearch = () => {
    setText('');
    setMissed(null);
    if (filters.q) setFilter('q', '');
  };

  const onSearchKey = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Escape') {
      if (text.length > 0) {
        event.preventDefault();
        clearSearch();
      }
      return;
    }
    if (event.key !== 'Enter') return;
    event.preventDefault();
    const code = text.trim();
    if (code.length === 0 || !products) return;
    const exact = findByCode(products, code);
    const matches = exact ? [exact] : filterProducts(products, { ...filters, q: code }, conditionSkus);
    if (matches.length === 1) {
      onAdd(matches[0]);
      clearSearch();
      return;
    }
    setFilter('q', code);
    setMissed(matches.length === 0 ? code : null);
  };

  const columns = useMemo<DataTableColumn<CajaProduct>[]>(
    () => [
      {
        id: 'producto',
        header: 'Producto',
        value: (product) => product.name,
        card: 'title',
        className: 'min-w-56',
        cell: (product) => {
          const quantity = inCart.get(product.sku.toUpperCase()) ?? 0;
          return (
            <span className="block">
              <span className="block">{product.name}</span>
              <span className="mt-0.5 flex flex-wrap items-center gap-1 text-xs font-normal text-text-muted">
                <span className="font-mono">{product.sku}</span>
                {product.serialized && (
                  <StatusBadge tone="accent" icon={<Barcode />}>
                    {serialLabel(product.serialKind)}
                  </StatusBadge>
                )}
                {product.platforms.map((platform) => (
                  <StatusBadge key={platform} tone="neutral">
                    {platform}
                  </StatusBadge>
                ))}
                {quantity > 0 && <StatusBadge tone="success">En la venta: {formatQuantity(quantity)}</StatusBadge>}
              </span>
            </span>
          );
        },
      },
      { id: 'categoria', header: 'Categoría', value: (product) => product.category },
      { id: 'precio', header: 'Precio', align: 'end', value: (product) => product.price, cell: (product) => formatMoney(product.price) },
      {
        id: 'disponible',
        header: 'Disponible',
        align: 'end',
        value: (product) => product.available,
        cell: (product) => (
          <span className={product.available <= 0 ? 'font-medium text-danger-text' : product.available <= 3 ? 'font-medium text-warning-text' : undefined}>
            {availabilityText(product)}
          </span>
        ),
      },
      {
        id: 'agregar',
        header: 'Agregar a la venta',
        hideHeader: true,
        sortable: false,
        csv: false,
        cell: (product) => {
          const reason = addBlocked(product, blocked);
          return (
            <Button
              variant={reason ? 'outline' : 'primary'}
              leftIcon={product.serialized ? <Barcode /> : <Plus />}
              disabled={reason !== null}
              title={reason ?? undefined}
              aria-label={`${addLabel(product)}: ${product.name}`}
              onClick={() => onAdd(product)}
            >
              {addLabel(product)}
            </Button>
          );
        },
      },
    ],
    [inCart, blocked, onAdd],
  );

  const exportRows = () => {
    const file = exportCsv('productos-caja', PRODUCT_CSV, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  let missedText: string | null = null;
  if (missed !== null) {
    if (!canLookup) missedText = unknownCodeText(missed, null);
    else if (lookup.data) missedText = unknownCodeText(missed, findLookup(lookup.data, missed));
    else if (lookup.error) missedText = unknownCodeText(missed, null);
    else missedText = `Buscando el código «${missed}»…`;
  }

  const product = detail?.product;
  const productBlocked = product ? addBlocked(product, blocked) : null;

  return (
    <section aria-label="Productos a la venta" className="min-w-0 space-y-4">
      <TextField
        ref={searchRef}
        label="Buscar o escanear (F2)"
        type="search"
        value={text}
        onChange={(value) => {
          setText(value);
          setMissed(null);
        }}
        onKeyDown={onSearchKey}
        placeholder="Nombre, SKU o código de barras"
        hint="El lector de códigos funciona como teclado: al leer el código (o pulsar Enter) el producto se agrega a la venta."
        leading={<ScanBarcode />}
        trailing={
          text ? (
            <button
              type="button"
              aria-label="Borrar la búsqueda"
              title="Borrar la búsqueda"
              onClick={clearSearch}
              className="flex size-11 cursor-pointer items-center justify-center rounded-xl text-text-muted hover:text-text [&_svg]:size-4"
            >
              <X aria-hidden="true" />
            </button>
          ) : undefined
        }
        autoComplete="off"
        spellCheck={false}
        enterKeyHint="enter"
        inputClassName="[&::-webkit-search-cancel-button]:appearance-none"
        data-testid="buscar-producto"
      />
      {missedText && (
        <Alert
          tone="warning"
          actions={
            <Button variant="ghost" leftIcon={<X />} onClick={() => setMissed(null)}>
              Entendido
            </Button>
          }
        >
          <span data-testid="codigo-no-encontrado">{missedText}</span>
        </Alert>
      )}
      {techError != null && (
        <Alert
          tone="warning"
          title="No se pudo leer qué productos llevan serie o IMEI"
          actions={
            <Button variant="outline" leftIcon={<RefreshCw />} onClick={onRetryTech}>
              Reintentar
            </Button>
          }
        >
          Si al cobrar el servidor pide series, pulse «Reintentar» y vuelva a agregar el producto.
        </Alert>
      )}

      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SelectField label="Categoría" allLabel="Todas las categorías" value={filters.categoria} onChange={(value) => setFilter('categoria', value)} options={categories} />
        {platforms.length > 0 && (
          <SelectField label="Plataforma" allLabel="Todas las plataformas" value={filters.plataforma} onChange={(value) => setFilter('plataforma', value)} options={platforms} />
        )}
        {conditions.length > 0 && (
          <SelectField label="Condición" allLabel="Todas" value={filters.condicion} onChange={(value) => setFilter('condicion', value)} options={conditions} />
        )}
        <SelectField label="Disponibilidad" value={filters.stock} onChange={(value) => setFilter('stock', value)} options={STOCK_OPTIONS} />
      </FilterBar>

      <Toolbar
        label="Acciones de la lista de productos"
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={refreshing} onClick={onRetry}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {products && (
          <span className="text-sm text-text-muted" data-testid="productos-resumen">
            {formatNumber(rows.length)} de {formatNumber(all.length)} productos · {formatNumber(inStock)} con stock
          </span>
        )}
      </Toolbar>

      <DataTable
        caption="Productos a la venta"
        columns={columns}
        rows={products && !waitingCondition ? rows : undefined}
        rowKey={(row) => row.sku}
        rowLabel={(row) => `el producto ${row.name}`}
        loading={loading || waitingCondition}
        refreshing={refreshing || (condition.fetching && !waitingCondition)}
        error={error ?? condition.error}
        onRetry={error ? onRetry : condition.reload}
        operation={error ? 'GetSellableProductsQuery' : 'SearchTechProductsQuery'}
        {...table.tableProps}
        onRowOpen={openDetail}
        activeRowKey={detail?.open ? detail.product.sku : null}
        rowActions={(row) => [
          {
            label: row.serialized ? 'Elegir la unidad y agregar' : 'Agregar a la venta',
            icon: <Plus />,
            onSelect: () => onAdd(row),
            disabled: addBlocked(row, blocked) !== null,
            disabledReason: addBlocked(row, blocked) ?? undefined,
          },
          { label: 'Ver detalle', icon: <Eye />, onSelect: () => openDetail(row) },
          { label: 'Ver solo esta categoría', icon: <Filter />, onSelect: () => setFilter('categoria', row.categoryCode), hidden: filters.categoria === row.categoryCode },
        ]}
        empty={{
          title: all.length === 0 ? 'No hay productos a la venta' : 'Ningún producto coincide',
          description: all.length === 0 ? 'Los productos activos con precio en la lista vigente aparecen aquí.' : 'Pruebe con otro texto u otros filtros.',
          icon: <PackageSearch />,
          action:
            all.length === 0 ? undefined : (
              <Button variant="outline" onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
        }}
      />

      <SidePanel
        open={detail?.open ?? false}
        onClose={() => setDetail((current) => (current?.open ? { ...current, open: false } : current))}
        title={product?.name ?? 'Producto'}
        description={product ? `${product.sku} · ${product.category}` : undefined}
        headerExtra={product?.serialized ? <StatusBadge tone="accent">{serialLabel(product.serialKind)}</StatusBadge> : undefined}
        footer={
          product && (
            <Button
              fullWidth
              leftIcon={product.serialized ? <Barcode /> : <Plus />}
              disabled={productBlocked !== null}
              onClick={() => {
                setDetail((current) => (current ? { ...current, open: false } : current));
                onAdd(product);
              }}
            >
              {productBlocked ?? (product.serialized ? 'Elegir la unidad y agregar a la venta' : 'Agregar a la venta')}
            </Button>
          )
        }
      >
        {product && (
          <DetailList
            items={[
              { label: 'Precio', value: formatMoney(product.price) },
              { label: 'Disponible', value: availabilityText(product) },
              { label: 'Reservado para otras ventas', value: product.reserved > 0 ? formatQuantity(product.reserved, { unit: product.unit }) : 'Nada' },
              { label: 'Marca', value: product.brand },
              { label: 'Lleva serie', value: product.serialized ? `Sí · ${serialLabel(product.serialKind)} (se elige la unidad al vender)` : 'No' },
              { label: 'Garantía', value: warrantyText(product.warrantyMonths) ?? 'Sin garantía registrada' },
              { label: 'Plataformas', value: product.platforms.join(', ') },
              { label: 'Códigos de barras', value: product.barcodes.join(', ') },
              { label: 'Especificaciones', value: product.keySpecs, wide: true },
            ]}
          />
        )}
      </SidePanel>
    </section>
  );
}
