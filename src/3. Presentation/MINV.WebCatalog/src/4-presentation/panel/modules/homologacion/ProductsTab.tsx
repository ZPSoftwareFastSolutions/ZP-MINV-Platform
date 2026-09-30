// Módulo «Homologación» · pestaña «Productos»: cada producto con su actividad y su código del SIN, filtros en la dirección
// (búsqueda, homologación —«sin homologar»—, categoría y actividad), tabla con acciones por fila, detalle lateral, exportar
// CSV, «Asignar código del SIN» y «Sugerir homologación» (`SuggestProductHomologationQuery` + el catálogo del SIN de la
// actividad, `SearchSiatProductsQuery`) para aceptar en lote.

import { Download, Eye, Filter, PackageSearch, RefreshCw, Sparkles, Tag } from 'lucide-react';
import { useMemo, useRef, useState } from 'react';
import { usePermissions, useRpcCommand, useTableState } from '@/4-presentation/panel/hooks';
import {
  Button,
  DataTable,
  DetailList,
  FilterBar,
  SearchField,
  SelectField,
  SidePanel,
  StatusBadge,
  Toolbar,
  statusOptions,
  useNotify,
  type DataTableColumn,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatNumber } from '@/4-presentation/panel/lib';
import { AssignProductDialog } from './AssignProductDialog';
import {
  PRODUCT_CSV,
  PRODUCT_FILTERS,
  PRODUCT_STATUSES,
  activityOptions,
  categoryOptions,
  defaultActivity,
  filterProducts,
  productActivityOptions,
  productStatus,
  sinText,
  toSuggestions,
  type HomologationData,
  type ProductRowData,
  type SuggestionItem,
} from './homologation';
import { SuggestionsDialog } from './SuggestionsDialog';

const COLUMNS: DataTableColumn<ProductRowData>[] = [
  { id: 'sku', header: 'SKU', value: (row) => row.sku, className: 'whitespace-nowrap' },
  { id: 'producto', header: 'Producto', value: (row) => row.name, card: 'title', className: 'min-w-48' },
  { id: 'categoria', header: 'Categoría', value: (row) => row.category },
  { id: 'actividad', header: 'Actividad', value: (row) => row.activityCode },
  { id: 'sin', header: 'Código del SIN', value: (row) => row.sinProductCode, cell: (row) => <span className="block min-w-40">{sinText(row.sinProductCode, row.sinProductDescription)}</span> },
  { id: 'estado', header: 'Homologación', value: (row) => productStatus(row), cell: (row) => <StatusBadge status={productStatus(row)} statuses={PRODUCT_STATUSES} /> },
];

export interface ProductsTabProps {
  view: HomologationData | undefined;
  loading: boolean;
  fetching: boolean;
  error: unknown;
  reload: () => void;
}

export function ProductsTab({ view, loading, fetching, error, reload }: ProductsTabProps) {
  const notify = useNotify();
  const { canRun } = usePermissions();
  const table = useTableState({ filters: PRODUCT_FILTERS, sort: { column: 'producto', direction: 'asc' } });
  const filters = table.filters;
  const products = useMemo(() => view?.products ?? [], [view]);
  const rows = useMemo(() => filterProducts(products, filters), [products, filters]);
  const activities = useMemo(() => view?.activities ?? [], [view]);
  const canEdit = canRun('SaveProductHomologationCommand');
  const canSuggest = canRun('SuggestProductHomologationQuery') && canEdit;

  // Detalle lateral y guarda del menú «⋯» (el clic de una opción, en un portal, no debe llegar a la fila).
  const [detail, setDetail] = useState<{ row: ProductRowData; open: boolean } | null>(null);
  const pickingAction = useRef(false);
  const fromMenu = (action: () => void) => () => {
    pickingAction.current = true;
    action();
    setTimeout(() => {
      pickingAction.current = false;
    }, 0);
  };

  // Diálogos (cada uno se monta de nuevo en cada apertura).
  const [dialogSession, setDialogSession] = useState(0);
  const [assigning, setAssigning] = useState<ProductRowData | null>(null);
  const [suggestions, setSuggestions] = useState<{ activity: string; items: SuggestionItem[] } | null>(null);
  const askAssign = (row: ProductRowData) => {
    setDialogSession((count) => count + 1);
    setDetail((current) => (current ? { ...current, open: false } : current));
    setAssigning(row);
  };

  // «Sugerir homologación» con la actividad elegida.
  const [activity, setActivity] = useState('');
  const chosenActivity = activity || defaultActivity(activities);
  const suggest = useRpcCommand('SuggestProductHomologationQuery', { errorTitle: 'No se pudo sugerir la homologación' });
  const catalog = useRpcCommand('SearchSiatProductsQuery', { errorTitle: 'No se pudo leer el catálogo del SIN' });
  const runSuggest = async () => {
    if (!chosenActivity) return;
    const proposed = await suggest.run({ activityCode: chosenActivity });
    if (!proposed.ok) return;
    if (proposed.result.length === 0) {
      notify.info('Sin sugerencias', `Ningún producto pendiente coincide con el catálogo del SIN de la actividad ${chosenActivity}: asígnelos uno por uno.`);
      return;
    }
    const sinProducts = await catalog.run({ activityCode: chosenActivity, text: null, max: 1000 });
    setDialogSession((count) => count + 1);
    setSuggestions({ activity: chosenActivity, items: toSuggestions(proposed.result, products, sinProducts.ok ? sinProducts.result : []) });
  };

  const exportRows = () => {
    const file = exportCsv('homologacion-productos', PRODUCT_CSV, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const item = detail?.row;
  return (
    <div className="space-y-4">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="SKU, nombre, categoría o código del SIN" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Homologación" allLabel="Todos" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(PRODUCT_STATUSES)} />
        <SelectField label="Categoría" allLabel="Todas las categorías" value={filters.categoria} onChange={(value) => table.setFilter('categoria', value)} options={categoryOptions(products)} />
        <SelectField label="Actividad" allLabel="Todas las actividades" value={filters.actividad} onChange={(value) => table.setFilter('actividad', value)} options={productActivityOptions(products)} />
      </FilterBar>

      {canSuggest && (
        <section aria-label="Sugerir homologación" className="flex flex-col gap-3 rounded-card border border-border bg-surface p-4 sm:flex-row sm:items-end">
          <SelectField
            label="Actividad para sugerir"
            allLabel={false}
            placeholder="Elija la actividad económica"
            value={chosenActivity}
            onChange={setActivity}
            options={activityOptions(activities)}
            hint="Se buscan códigos del SIN por palabras del nombre y la categoría de los productos pendientes."
            className="min-w-0 flex-1"
          />
          <Button leftIcon={<Sparkles />} loading={suggest.sending || catalog.sending} disabled={!chosenActivity || (view?.pendingProducts ?? 0) === 0} onClick={() => void runSuggest()}>
            Sugerir homologación
          </Button>
        </section>
      )}

      <Toolbar
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={fetching && !loading} onClick={reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {view && (
          <span className="text-sm text-text-muted" data-testid="productos-resumen">
            {formatNumber(rows.length)} de {formatNumber(products.length)} productos · {formatNumber(view.pendingProducts)} sin homologar
          </span>
        )}
      </Toolbar>

      <DataTable
        caption="Homologación de productos"
        columns={COLUMNS}
        rows={view ? rows : undefined}
        rowKey={(row) => row.productId}
        rowLabel={(row) => `el producto ${row.sku}`}
        loading={loading}
        refreshing={fetching && !loading}
        error={error}
        onRetry={reload}
        operation="GetHomologationQuery"
        {...table.tableProps}
        onRowOpen={(row) => {
          if (!pickingAction.current) setDetail({ row, open: true });
        }}
        activeRowKey={detail?.open ? detail.row.productId : null}
        rowActions={(row) => [
          { label: 'Ver detalle', icon: <Eye />, onSelect: fromMenu(() => setDetail({ row, open: true })) },
          { label: 'Asignar código del SIN', icon: <Tag />, onSelect: fromMenu(() => askAssign(row)), hidden: !canEdit },
          { label: 'Ver solo esta categoría', icon: <Filter />, onSelect: fromMenu(() => table.setFilter('categoria', row.category)), hidden: filters.categoria === row.category },
        ]}
        empty={{
          title: products.length === 0 ? 'Todavía no hay productos' : 'No hay productos con estos filtros',
          description: products.length === 0 ? undefined : 'Pruebe con otros filtros o límpielos.',
          icon: <PackageSearch />,
          action:
            products.length === 0 ? undefined : (
              <Button variant="outline" onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
        }}
      />

      <SidePanel
        open={detail?.open ?? false}
        onClose={() => setDetail((current) => (current?.open ? { ...current, open: false } : current))}
        title={item ? `${item.sku} · ${item.name}` : 'Producto'}
        description={item?.category}
        headerExtra={item && <StatusBadge status={productStatus(item)} statuses={PRODUCT_STATUSES} />}
        footer={
          item &&
          canEdit && (
            <Button leftIcon={<Tag />} fullWidth onClick={() => askAssign(item)}>
              Asignar código del SIN
            </Button>
          )
        }
      >
        {item && (
          <DetailList
            items={[
              { label: 'SKU', value: item.sku },
              { label: 'Categoría', value: item.category },
              { label: 'Actividad económica', value: item.activityCode },
              { label: 'Código del SIN', value: sinText(item.sinProductCode, item.sinProductDescription), wide: true },
              { label: 'Producto activo', value: item.isActive ? 'Sí' : 'No (no se vende)' },
              {
                label: 'Qué significa',
                value: productStatus(item) === 'pendiente' ? 'Sin homologar no se factura: la caja rechaza la venta con un mensaje claro.' : 'La factura informa el producto al SIN con este código.',
                wide: true,
              },
            ]}
          />
        )}
      </SidePanel>

      <AssignProductDialog key={`asignar-${dialogSession}`} target={assigning} activities={activities} onClose={() => setAssigning(null)} onDone={reload} />
      <SuggestionsDialog key={`sugerencias-${dialogSession}`} target={suggestions} onClose={() => setSuggestions(null)} onDone={reload} />
    </div>
  );
}
