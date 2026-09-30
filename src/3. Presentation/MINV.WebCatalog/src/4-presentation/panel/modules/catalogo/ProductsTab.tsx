// Módulo «Catálogo» · pestaña «Productos». El catálogo (`GetCatalogQuery`) cruzado con la ficha resumida de cada producto
// activo (`SearchTechProductsQuery`: marca, plataformas, serie o IMEI, garantía y disponible de la sucursal activa):
//   1. filtros en la dirección (`useTableState`): los que resuelve el SERVIDOR (categoría con sus subcategorías, condición
//      y especificaciones de la categoría → otro `GetCatalogQuery` con `categoryCode` y `specFilters`) y los de la página
//      (búsqueda, marca, estado, imagen, precio y margen, serie, plataforma);
//   2. lista (tabla ordenable y paginada) o galería con imágenes (`?vista=galeria`: pide las imágenes SOLO de la página);
//   3. detalle lateral con la ficha técnica, alta y edición por pestañas, activar y desactivar (con confirmación), CSV y
//      el resumen PLEGADO (regla P-10).
// El menú «⋯» de una fila se dibuja aparte (portal) y React le pasa el clic a la fila: mientras se atiende una acción del
// menú, el clic de la fila no abre el detalle (guarda `pickingAction`; ver «Pendientes» del informe M6).

import { BarChart3, CircleCheck, CircleOff, ClipboardList, Download, Eye, ImageOff, LayoutGrid, List, PackagePlus, PackageSearch, Pencil, RefreshCw, Tags } from 'lucide-react';
import { useMemo, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcCommand, useRpcQuery, useTableState, type RpcQuery } from '@/4-presentation/panel/hooks';
import {
  Alert,
  BarList,
  Button,
  Collapsible,
  ConfirmDialog,
  DataTable,
  FilterBar,
  SearchField,
  SelectField,
  StatCard,
  StatusBadge,
  Toolbar,
  statusOptions,
  useNotify,
  type DataTableColumn,
  type RowActionItem,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatMoney, formatNumber, formatQuantity, paginate, sortRows, type SortState } from '@/4-presentation/panel/lib';
import {
  CONDITION_CODE,
  IMAGE_OPTIONS,
  PLATFORM_CODES,
  PRICE_OPTIONS,
  PRODUCTS_CSV,
  PRODUCT_FILTERS,
  PRODUCT_STATES,
  SERIAL_OPTIONS,
  TECH_SEARCH,
  VIEW_PARAM,
  brandOptions,
  catalogSummary,
  categoryOptions,
  facetOptions,
  filterProducts,
  isLowMargin,
  marginText,
  needsServerFilter,
  parseSpecChoices,
  specFiltersOf,
  specOptions,
  toProductEntries,
  toggleActivePayload,
  viewOf,
  visibleFacets,
  withSpecChoice,
  type CatalogOptionsRecord,
  type CatalogRecord,
  type CatalogView,
  type ProductEntry,
  type SpecDefinitionRecord,
} from './catalog';
import { imageDataUrl } from './image';
import { DETAIL_PARAM, EDIT_PARAM, NEW_PARAM, newRequestOf, useOneShotParam } from './params';
import { ProductDetailPanel } from './ProductDetailPanel';
import { ProductDialog, type ProductTarget } from './ProductDialog';
import { ProductGallery } from './ProductGallery';

function ProductCell({ entry }: { entry: ProductEntry }) {
  return (
    <span className="block min-w-48">
      <span className="block">{entry.row.name}</span>
      <span className="mt-0.5 flex flex-wrap items-center gap-1.5 text-xs font-normal text-text-muted">
        {entry.row.sku}
        {!entry.row.hasImage && (
          <span className="inline-flex items-center gap-1" title="Sin imagen">
            <ImageOff aria-hidden="true" className="size-3.5" />
            <span className="sr-only">Sin imagen</span>
          </span>
        )}
        {(entry.serial === 'serie' || entry.serial === 'imei') && <StatusBadge tone="info">{entry.serial === 'imei' ? 'IMEI' : 'Serie'}</StatusBadge>}
      </span>
    </span>
  );
}

/** Columnas de la tabla (y el orden de la galería, que usa sus mismos valores). */
const COLUMNS: DataTableColumn<ProductEntry>[] = [
  { id: 'producto', header: 'Producto', value: (entry) => entry.row.name, card: 'title', cell: (entry) => <ProductCell entry={entry} /> },
  { id: 'categoria', header: 'Categoría', value: (entry) => entry.row.category },
  { id: 'marca', header: 'Marca', value: (entry) => entry.brand },
  {
    id: 'precio',
    header: 'Precio',
    align: 'end',
    value: (entry) => (entry.row.salePrice > 0 ? entry.row.salePrice : null),
    cell: (entry) => (entry.row.salePrice > 0 ? formatMoney(entry.row.salePrice) : <span className="text-text-muted">Sin precio</span>),
  },
  {
    id: 'margen',
    header: 'Margen',
    align: 'end',
    value: (entry) => entry.margin,
    cell: (entry) => <span className={isLowMargin(entry) ? 'font-semibold text-warning-text' : undefined}>{marginText(entry.margin)}</span>,
  },
  {
    id: 'disponible',
    header: 'Disponible',
    align: 'end',
    value: (entry) => entry.available,
    cell: (entry) => (entry.available === null ? '—' : formatQuantity(entry.available, { unit: entry.row.unit })),
  },
  { id: 'estado', header: 'Estado', value: (entry) => PRODUCT_STATES[entry.state].label, cell: (entry) => <StatusBadge status={entry.state} statuses={PRODUCT_STATES} /> },
];

/** Filas en el orden que muestra la tabla (para la galería y el CSV). */
function sortEntries(rows: readonly ProductEntry[], sort: SortState | null): ProductEntry[] {
  const column = sort ? COLUMNS.find((item) => item.id === sort.column) : undefined;
  return column?.value && sort ? sortRows(rows, column.value, sort.direction) : [...rows];
}

export interface ProductsTabProps {
  options: RpcQuery<CatalogOptionsRecord>;
  catalog: RpcQuery<CatalogRecord[]>;
  definitions: RpcQuery<SpecDefinitionRecord[]>;
}

export function ProductsTab({ options, catalog, definitions }: ProductsTabProps) {
  const notify = useNotify();
  const navigate = useNavigate();
  const { canRun } = usePermissions();
  const canProduct = canRun('SaveProductCommand');
  const canTech = canRun('SaveProductTechCommand');
  const table = useTableState({ filters: PRODUCT_FILTERS, sort: { column: 'producto', direction: 'asc' } });
  const filters = table.filters;
  const [params, setParams] = useSearchParams();
  const view = viewOf(params.get(VIEW_PARAM));

  const tech = useRpcQuery('SearchTechProductsQuery', TECH_SEARCH);
  const taxRate = options.data?.taxRate ?? 13;
  const onInvoiced = options.data?.vatOnInvoicedAmount ?? true;
  const tax = useMemo(() => ({ rate: taxRate, onInvoicedAmount: onInvoiced }), [taxRate, onInvoiced]);
  const entries = useMemo(() => toProductEntries(catalog.data ?? [], tech.data ?? [], tax), [catalog.data, tech.data, tax]);

  // Lo que filtra el SERVIDOR: categoría (con sus subcategorías), condición y especificaciones de la categoría.
  const specFilters = useMemo(() => specFiltersOf({ espec: filters.espec, condicion: filters.condicion }), [filters.espec, filters.condicion]);
  const serverFilter = needsServerFilter(filters);
  const matchedQuery = useRpcQuery(
    'GetCatalogQuery',
    { categoryCode: filters.categoria || null, specFilters: specFilters.length > 0 ? specFilters : null },
    { enabled: serverFilter, keepPreviousData: false },
  );
  const matched = useMemo(() => (serverFilter && matchedQuery.data ? new Set(matchedQuery.data.map((row) => row.sku.toUpperCase())) : null), [serverFilter, matchedQuery.data]);
  const facets = useRpcQuery('GetSpecFacetsQuery', { categoryCode: filters.categoria }, { enabled: Boolean(filters.categoria), keepPreviousData: false });
  const shownFacets = useMemo(() => visibleFacets(facets.data ?? []), [facets.data]);
  const choices = parseSpecChoices(filters.espec);

  const rows = useMemo(() => filterProducts(entries, filters, matched), [entries, filters, matched]);
  const ready = catalog.data !== undefined && (!serverFilter || matchedQuery.data !== undefined);
  const sorted = useMemo(() => sortEntries(rows, table.sort), [rows, table.sort]);

  const categoryChoices = useMemo(() => categoryOptions(options.data, catalog.data ?? []), [options.data, catalog.data]);
  const brands = useMemo(() => brandOptions(entries), [entries]);
  const platforms = useMemo(() => specOptions(definitions.data ?? [], PLATFORM_CODES), [definitions.data]);
  const conditions = useMemo(() => specOptions(definitions.data ?? [], [CONDITION_CODE]), [definitions.data]);
  const summary = useMemo(() => catalogSummary(entries), [entries]);

  // Galería: solo las imágenes de la página a la vista.
  const slice = paginate(sorted, table.page, table.pageSize);
  const variantIds = view === 'galeria' && ready ? slice.rows.filter((entry) => entry.row.hasImage).map((entry) => entry.row.variantId) : [];
  const galleryImages = useRpcQuery('GetProductImagesQuery', { variantIds }, { enabled: variantIds.length > 0 });
  const imageUrls = useMemo(() => new Map((galleryImages.data ?? []).map((image) => [image.variantId, imageDataUrl(image.contentType, image.content)])), [galleryImages.data]);

  // Detalle lateral: `?producto=SKU` (se guarda el último SKU para que el panel se deslice con su contenido al cerrarse).
  const detailSku = params.get(DETAIL_PARAM);
  const [shownSku, setShownSku] = useState<string | null>(detailSku);
  if (detailSku && detailSku !== shownSku) setShownSku(detailSku);
  const detailEntry = shownSku ? (entries.find((entry) => entry.row.sku.toUpperCase() === shownSku.toUpperCase()) ?? null) : null;
  const [detailVersion, setDetailVersion] = useState(0);
  const openDetail = (sku: string) => {
    setShownSku(sku);
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.set(DETAIL_PARAM, sku);
        return next;
      },
      { replace: true },
    );
  };
  const closeDetail = () =>
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.delete(DETAIL_PARAM);
        return next;
      },
      { replace: true },
    );

  // Guarda del menú «⋯»: su clic llega también a la fila (portal) y abriría el detalle encima de la acción elegida.
  const pickingAction = useRef(false);
  const fromMenu = (action: () => void) => () => {
    pickingAction.current = true;
    action();
    setTimeout(() => {
      pickingAction.current = false;
    }, 0);
  };
  const openFromRow = (entry: ProductEntry) => {
    if (!pickingAction.current) openDetail(entry.row.sku);
  };

  // Formulario del producto (se monta de nuevo en cada apertura).
  const [dialogSession, setDialogSession] = useState(0);
  const [editing, setEditing] = useState<ProductTarget | null>(null);
  const openEditor = (target: ProductTarget) => {
    setDialogSession((count) => count + 1);
    setEditing(target);
  };
  const editRow = (row: CatalogRecord) => {
    if (canProduct) openEditor({ mode: 'editar', row });
    else if (canTech) openEditor({ mode: 'ficha', row });
    else openDetail(row.sku);
  };

  // `?nuevo=producto` (tablero y botón de arriba) y `?editar=SKU` (desde otros módulos).
  useOneShotParam(
    NEW_PARAM,
    (value) => newRequestOf(value) === 'producto',
    () => {
      if (canProduct) openEditor({ mode: 'nuevo', row: null });
    },
  );
  useOneShotParam(
    EDIT_PARAM,
    () => catalog.data !== undefined,
    (sku) => {
      const row = catalog.data?.find((item) => item.sku.toUpperCase() === sku.toUpperCase());
      if (row) editRow(row);
      else notify.warning('No encontramos el producto', `No hay un producto con el SKU ${sku}.`);
    },
  );

  const reload = () => {
    catalog.reload();
    tech.reload();
    if (serverFilter) matchedQuery.reload();
    if (variantIds.length > 0) galleryImages.reload();
  };
  const afterSave = () => {
    reload();
    setDetailVersion((count) => count + 1);
  };

  // Activar (directo) y desactivar (con confirmación).
  const toggle = useRpcCommand('SaveProductCommand', { notifyError: false });
  const [deactivating, setDeactivating] = useState<{ row: CatalogRecord; open: boolean } | null>(null);
  const activate = async (row: CatalogRecord) => {
    const outcome = await toggle.run(toggleActivePayload(row, true));
    if (outcome.ok) {
      notify.success('Producto activado', `${row.sku} · ${row.name}`);
      afterSave();
    } else notify.error('No se pudo activar el producto', outcome.error);
  };
  const askDeactivate = (row: CatalogRecord) => {
    toggle.reset();
    setDeactivating({ row, open: true });
  };
  const confirmDeactivate = async () => {
    const row = deactivating?.row;
    if (!row) return false;
    const outcome = await toggle.run(toggleActivePayload(row, false));
    if (outcome.ok) {
      notify.success('Producto desactivado', `${row.sku} · ${row.name}`);
      afterSave();
    }
    return outcome;
  };

  const stockLink = (sku: string) => ROUTES.panelModule(`stock?ficha=${encodeURIComponent(sku)}`);
  const rowActions = (entry: ProductEntry): RowActionItem[] => [
    { label: 'Ver detalle', icon: <Eye />, onSelect: fromMenu(() => openDetail(entry.row.sku)) },
    { label: 'Editar', icon: <Pencil />, onSelect: fromMenu(() => openEditor({ mode: 'editar', row: entry.row })), hidden: !canProduct },
    { label: 'Editar ficha técnica', icon: <ClipboardList />, onSelect: fromMenu(() => openEditor({ mode: 'ficha', row: entry.row })), hidden: canProduct || !canTech },
    { label: 'Ver ficha y kardex', icon: <PackageSearch />, onSelect: fromMenu(() => navigate(stockLink(entry.row.sku))) },
    { label: 'Activar', icon: <CircleCheck />, onSelect: fromMenu(() => void activate(entry.row)), hidden: !canProduct || entry.row.isActive },
    { label: 'Desactivar', icon: <CircleOff />, tone: 'danger', onSelect: fromMenu(() => askDeactivate(entry.row)), hidden: !canProduct || !entry.row.isActive },
  ];

  const setView = (next: CatalogView) =>
    setParams(
      (previous) => {
        const updated = new URLSearchParams(previous);
        if (next === 'lista') updated.delete(VIEW_PARAM);
        else updated.set(VIEW_PARAM, next);
        return updated;
      },
      { replace: true },
    );

  const exportRows = () => {
    const file = exportCsv('catalogo', PRODUCTS_CSV, sorted);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(sorted.length)} filas).`);
  };

  const listError = catalog.error ?? (serverFilter ? matchedQuery.error : null);
  const retry = () => {
    catalog.reload();
    if (serverFilter) matchedQuery.reload();
  };
  const noProducts = entries.length === 0;
  const empty = {
    title: noProducts ? 'Todavía no hay productos' : 'Ningún producto coincide con los filtros',
    description: noProducts ? (canProduct ? 'Dé de alta el primero con «Nuevo producto».' : undefined) : 'Pruebe con otra búsqueda o limpie los filtros.',
    icon: <Tags />,
    action: noProducts ? (
      canProduct && (
        <Button leftIcon={<PackagePlus />} onClick={() => openEditor({ mode: 'nuevo', row: null })}>
          Nuevo producto
        </Button>
      )
    ) : (
      <Button variant="outline" onClick={table.clearFilters}>
        Limpiar filtros
      </Button>
    ),
  };

  return (
    <div className="space-y-4">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Nombre, SKU, código de barras, marca o proveedor" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField
          label="Categoría"
          allLabel="Todas las categorías"
          value={filters.categoria}
          onChange={(value) => table.setFilters({ categoria: value, espec: '' })}
          options={categoryChoices}
          hint={filters.categoria ? 'Incluye sus subcategorías.' : 'Elija una para filtrar por sus especificaciones.'}
        />
        <SelectField label="Marca" allLabel="Todas las marcas" value={filters.marca} onChange={(value) => table.setFilter('marca', value)} options={brands} />
        <SelectField label="Estado" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(PRODUCT_STATES)} />
        <SelectField label="Imagen" value={filters.imagen} onChange={(value) => table.setFilter('imagen', value)} options={IMAGE_OPTIONS} />
        <SelectField label="Precio" value={filters.precio} onChange={(value) => table.setFilter('precio', value)} options={PRICE_OPTIONS} />
        <SelectField label="Serie o IMEI" value={filters.serie} onChange={(value) => table.setFilter('serie', value)} options={SERIAL_OPTIONS} />
        {platforms.length > 0 && (
          <SelectField label="Plataforma" allLabel="Todas las plataformas" value={filters.plataforma} onChange={(value) => table.setFilter('plataforma', value)} options={platforms} />
        )}
        {conditions.length > 0 && <SelectField label="Condición" allLabel="Todas" value={filters.condicion} onChange={(value) => table.setFilter('condicion', value)} options={conditions} />}
        {shownFacets.map((facet) => (
          <SelectField
            key={facet.code}
            label={facet.name}
            allLabel="Cualquiera"
            value={choices.find((choice) => choice.code === facet.code)?.value ?? ''}
            onChange={(value) => table.setFilter('espec', withSpecChoice(filters.espec, facet.code, value))}
            options={facetOptions(facet)}
          />
        ))}
      </FilterBar>

      {facets.error && filters.categoria && (
        <Alert
          tone="warning"
          title="No se pudieron leer las especificaciones de la categoría"
          actions={
            <Button variant="outline" leftIcon={<RefreshCw />} onClick={facets.reload}>
              Reintentar
            </Button>
          }
        >
          La categoría se filtra igual; faltan solo los filtros por especificación.
        </Alert>
      )}
      {tech.error && (
        <Alert
          tone="warning"
          title="No se pudieron leer las fichas técnicas"
          actions={
            <Button variant="outline" leftIcon={<RefreshCw />} onClick={tech.reload}>
              Reintentar
            </Button>
          }
        >
          La lista se ve igual, pero sin marca, plataforma, serie ni disponible.
        </Alert>
      )}

      <Toolbar
        label="Acciones de la lista de productos"
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={(catalog.fetching || tech.fetching) && !catalog.loading} onClick={reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={!ready || sorted.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        <div role="group" aria-label="Vista" className="flex gap-1">
          <Button variant={view === 'lista' ? 'subtle' : 'ghost'} leftIcon={<List />} aria-pressed={view === 'lista'} onClick={() => setView('lista')}>
            Lista
          </Button>
          <Button variant={view === 'galeria' ? 'subtle' : 'ghost'} leftIcon={<LayoutGrid />} aria-pressed={view === 'galeria'} onClick={() => setView('galeria')}>
            Galería
          </Button>
        </div>
        {ready && (
          <span className="text-sm text-text-muted" data-testid="catalogo-resumen">
            {formatNumber(rows.length)} de {formatNumber(entries.length)} productos · {formatNumber(summary.active)} activos
          </span>
        )}
      </Toolbar>

      <Collapsible label="Ver resumen del catálogo" openLabel="Ocultar resumen del catálogo" icon={<BarChart3 />} description="Productos activos, imágenes, precio y margen promedio, y productos por categoría.">
        <div className="space-y-4" data-testid="resumen-catalogo">
          <div className="grid grid-cols-[repeat(auto-fit,minmax(11rem,1fr))] gap-3">
            <StatCard label="Productos activos" value={formatNumber(summary.active)} hint={`${formatNumber(summary.total)} en total · ${formatNumber(options.data?.categories.length ?? 0)} categorías`} icon={<Tags />} />
            <StatCard
              label="Con imagen"
              value={`${formatNumber(summary.withImage)} de ${formatNumber(summary.total)}`}
              hint={summary.withoutImage > 0 ? `${formatNumber(summary.withoutImage)} sin imagen` : 'Todos tienen imagen'}
              icon={<ImageOff />}
              tone={summary.withoutImage > 0 ? 'warning' : 'default'}
            />
            <StatCard
              label="Precio promedio"
              value={summary.averagePrice === null ? '—' : formatMoney(summary.averagePrice)}
              hint={`Lista ${options.data?.priceListName ?? '—'} · IVA ${formatNumber(taxRate)} % incluido`}
              icon={<Tags />}
            />
            <StatCard
              label="Margen promedio"
              value={marginText(summary.averageMargin)}
              hint={`${formatNumber(summary.lowMargin)} con margen bajo · ${formatNumber(summary.withoutPrice)} sin precio`}
              icon={<BarChart3 />}
              tone={summary.lowMargin > 0 ? 'warning' : 'default'}
            />
          </div>
          {summary.byCategory.length > 0 && <BarList label="Productos activos por categoría" items={summary.byCategory.slice(0, 8)} format={(value) => formatNumber(value)} />}
        </div>
      </Collapsible>

      {view === 'lista' ? (
        <DataTable
          caption="Productos del catálogo"
          columns={COLUMNS}
          rows={ready ? rows : undefined}
          rowKey={(entry) => entry.key}
          rowLabel={(entry) => `el producto ${entry.row.name} (${entry.row.sku})`}
          loading={catalog.loading || (serverFilter && matchedQuery.loading)}
          refreshing={(catalog.fetching || matchedQuery.fetching) && ready}
          error={listError}
          onRetry={retry}
          operation="GetCatalogQuery"
          {...table.tableProps}
          onRowOpen={openFromRow}
          activeRowKey={detailSku ? (detailEntry?.key ?? null) : null}
          rowActions={rowActions}
          empty={empty}
        />
      ) : (
        <ProductGallery
          slice={ready ? slice : null}
          sort={table.sort}
          onSortChange={table.setSort}
          onPageChange={table.setPage}
          pageSize={table.pageSize}
          onPageSizeChange={table.setPageSize}
          imageUrls={imageUrls}
          imagesLoading={galleryImages.loading}
          error={listError}
          onRetry={retry}
          empty={empty}
          onOpen={(entry) => openDetail(entry.row.sku)}
          actionsOf={rowActions}
        />
      )}

      <ProductDetailPanel
        sku={shownSku}
        entry={detailEntry}
        loaded={catalog.data !== undefined}
        open={detailSku !== null}
        onClose={closeDetail}
        tax={tax}
        version={detailVersion}
        canProduct={canProduct}
        canTech={canTech}
        onEdit={(mode) => detailEntry && openEditor({ mode, row: detailEntry.row })}
        onActivate={() => detailEntry && void activate(detailEntry.row)}
        onDeactivate={() => detailEntry && askDeactivate(detailEntry.row)}
        busy={toggle.sending}
        stockLink={stockLink}
      />

      <ProductDialog
        key={`producto-${dialogSession}`}
        target={editing}
        options={options.data}
        onClose={() => setEditing(null)}
        onSaved={afterSave}
        onCategoryCreated={options.reload}
      />
      <ConfirmDialog
        open={deactivating?.open ?? false}
        onClose={() => setDeactivating((shown) => (shown?.open ? { ...shown, open: false } : shown))}
        tone="danger"
        title={`¿Desactivar «${deactivating?.row.name ?? 'este producto'}»?`}
        message="Deja de venderse en la caja y de aparecer en el pedido sugerido y en la tienda. Sus movimientos y su historial se conservan, y se puede volver a activar."
        confirmLabel="Desactivar"
        onConfirm={confirmDeactivate}
        error={toggle.errorText ?? undefined}
      />
    </div>
  );
}
