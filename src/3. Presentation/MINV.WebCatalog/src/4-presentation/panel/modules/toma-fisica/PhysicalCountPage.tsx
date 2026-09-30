// Inventario › Toma física: la toma física del escritorio (PhysicalCountView + PhysicalCountViewModel) en la web.
//   - Sin toma abierta: «Iniciar toma física» (almacén, fecha y observación → `OpenPhysicalCountCommand`).
//   - Con la toma abierta (`GetOpenPhysicalCountQuery`, del almacén de trabajo o del elegido en `?almacen=`): progreso,
//     «Registrar un conteo» con el lector de códigos (`RecordCountCommand`), la planilla con las diferencias resaltadas
//     (sobrante, faltante, cuadra) y filtros por categoría, ubicación, diferencia, quién contó y búsqueda; la pestaña
//     «Pendientes» con lo que falta contar; corregir y quitar un conteo (`RemoveCountCommand`, con confirmación); imprimir
//     la planilla de conteo; exportar CSV; el resumen de diferencias PLEGADO; «Generar ajustes» con la confirmación del
//     resumen (`PostPhysicalCountCommand`) y «Anular toma» (`CancelPhysicalCountCommand`, con confirmación).
// Dirección: filtros (`?q=`, `?categoria=`, `?ubicacion=`, `?diferencia=`, `?contador=`), `?vista=pendientes` y `?almacen=`.
// Las filas no abren un detalle (todo está a la vista), así que el clic del menú «⋯» no tiene a dónde propagarse.

import { ArrowLeftRight, Ban, BarChart3, ClipboardCheck, ClipboardList, Download, Hourglass, PackageSearch, Pencil, Printer, RefreshCw, Trash2, Warehouse } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcCommand, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Alert,
  Button,
  Collapsible,
  ConfirmDialog,
  DataTable,
  ErrorState,
  FilterBar,
  Form,
  FormGrid,
  LoadingState,
  Page,
  SearchField,
  Section,
  SelectField,
  StatCard,
  StatusBadge,
  TabPanel,
  Tabs,
  TextField,
  Toolbar,
  useNotify,
  type DataTableColumn,
  type RowActionItem,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatDate, formatNumber, formatQuantity, formatTime, laPazToday, toDate } from '@/4-presentation/panel/lib';
import {
  COUNT_CSV,
  COUNT_FILTERS,
  DIFFERENCES,
  DIFFERENCE_OPTIONS,
  binOptions,
  categoryOptions,
  countProgress,
  countSummary,
  counterOptions,
  differenceText,
  filterEntries,
  pendingProducts,
  plainMessage,
  printRows,
  toCountEntries,
  toOpenCount,
  toRemoveCount,
  warehouseOptions,
  type CountEntry,
  type CountLineRecord,
  type LookupRecord,
  type PostResultRecord,
} from './count';
import { CountSheetPrint } from './CountSheetPrint';
import { PostCountDialog } from './PostCountDialog';
import { printCountSheet } from './printing';
import { RecordCountForm, type CountRequest } from './RecordCountForm';

const WAREHOUSE_PARAM = 'almacen';
const VIEW_PARAM = 'vista';

const COLUMNS: DataTableColumn<CountEntry>[] = [
  {
    id: 'producto',
    header: 'Producto',
    value: (entry) => entry.line.name,
    card: 'title',
    cell: (entry) => (
      <span className="block min-w-44">
        <span className="block">{entry.line.name}</span>
        <span className="block text-xs font-normal text-text-muted">
          {entry.line.sku}
          {entry.category ? ` · ${entry.category}` : ''}
        </span>
      </span>
    ),
  },
  { id: 'posicion', header: 'Posición', value: (entry) => entry.line.binCode },
  { id: 'sistema', header: 'Sistema', align: 'end', value: (entry) => entry.line.systemQuantity, cell: (entry) => formatQuantity(entry.line.systemQuantity, { unit: entry.line.unit }) },
  { id: 'contado', header: 'Contado', align: 'end', value: (entry) => entry.line.countedQuantity, cell: (entry) => formatQuantity(entry.line.countedQuantity, { unit: entry.line.unit }) },
  {
    id: 'diferencia',
    header: 'Diferencia',
    align: 'end',
    value: (entry) => entry.line.difference,
    cell: (entry) => <StatusBadge tone={DIFFERENCES[entry.kind].tone}>{differenceText(entry.line.difference, entry.line.unit)}</StatusBadge>,
  },
  {
    id: 'contador',
    header: 'Contó',
    value: (entry) => entry.line.countedBy,
    cell: (entry) => (
      <span className="block whitespace-nowrap">
        <span className="block">{entry.line.countedBy ?? '—'}</span>
        <span className="block text-xs text-text-muted">{formatTime(entry.line.countedAt)}</span>
      </span>
    ),
  },
  { id: 'hora', header: 'Hora', value: (entry) => toDate(entry.line.countedAt), cell: (entry) => formatTime(entry.line.countedAt), card: 'hidden' },
];

const PENDING_COLUMNS: DataTableColumn<LookupRecord>[] = [
  {
    id: 'producto',
    header: 'Producto',
    value: (product) => product.name,
    card: 'title',
    cell: (product) => (
      <span className="block min-w-44">
        <span className="block">{product.name}</span>
        <span className="block text-xs font-normal text-text-muted">{product.sku}</span>
      </span>
    ),
  },
  { id: 'categoria', header: 'Categoría', value: (product) => product.category },
  { id: 'posicion', header: 'Posición principal', value: (product) => product.primaryBin },
  { id: 'unidad', header: 'Unidad', value: (product) => product.unit },
];

function scopeText(filters: typeof COUNT_FILTERS): string {
  return [filters.categoria && `Categoría: ${filters.categoria}`, filters.ubicacion && `Posición: ${filters.ubicacion}`, filters.q && `Búsqueda: «${filters.q}»`].filter(Boolean).join(' · ');
}

export function PhysicalCountPage() {
  const notify = useNotify();
  const navigate = useNavigate();
  const { canRun, session } = usePermissions();
  const canOpen = canRun('OpenPhysicalCountCommand');
  const canRecord = canRun('RecordCountCommand');
  const canRemove = canRun('RemoveCountCommand');
  const canPost = canRun('PostPhysicalCountCommand');
  const canCancel = canRun('CancelPhysicalCountCommand');
  const table = useTableState({ filters: COUNT_FILTERS, sort: { column: 'hora', direction: 'desc' } });
  const filters = table.filters;
  const [params, setParams] = useSearchParams();
  const chosenWarehouse = params.get(WAREHOUSE_PARAM) ?? '';
  const view = params.get(VIEW_PARAM) === 'pendientes' ? 'pendientes' : 'contados';

  const workspace = useRpcQuery('GetWorkspaceQuery', {});
  const branches = useRpcQuery('GetBranchesQuery', {});
  const warehouses = useMemo(() => warehouseOptions(branches.data ?? [], session?.access.activeBranchId), [branches.data, session?.access.activeBranchId]);
  const sheetQuery = useRpcQuery('GetOpenPhysicalCountQuery', { warehouseCode: chosenWarehouse || null }, { keepPreviousData: false });
  const sheet = sheetQuery.data ?? null;
  const countWarehouse = sheet?.warehouseCode ?? (chosenWarehouse || workspace.data?.warehouseCode || null);
  const products = useRpcQuery('GetProductLookupQuery', { includeInactive: false });
  const bins = useRpcQuery('GetBinsQuery', { warehouseCode: countWarehouse }, { enabled: countWarehouse !== null });
  const preview = Boolean(workspace.data && countWarehouse && workspace.data.warehouseCode.toUpperCase() === countWarehouse.toUpperCase());

  const productList = useMemo(() => products.data ?? [], [products.data]);
  const entries = useMemo(() => toCountEntries(sheet, productList), [sheet, productList]);
  const rows = useMemo(() => filterEntries(entries, filters), [entries, filters]);
  const pending = useMemo(() => pendingProducts(productList, sheet, filters), [productList, sheet, filters]);
  const progress = countProgress(productList, sheet, filters);
  const summary = countSummary(sheet);
  const categories = useMemo(() => categoryOptions(productList), [productList]);
  const binChoices = useMemo(() => binOptions(bins.data ?? []), [bins.data]);
  const counters = useMemo(() => counterOptions(entries), [entries]);

  const setParam = (name: string, value: string) =>
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        if (value) next.set(name, value);
        else next.delete(name);
        return next;
      },
      { replace: true },
    );

  // ------------------------------------------------------------------------------------------------ iniciar
  const [countDate, setCountDate] = useState(() => laPazToday());
  const [notes, setNotes] = useState('');
  const [startTouched, setStartTouched] = useState(false);
  const open = useRpcCommand('OpenPhysicalCountCommand', { notifyError: false });
  const startWarehouse = chosenWarehouse || workspace.data?.warehouseCode || '';
  const today = laPazToday();
  const dateProblem = !countDate ? 'Indique la fecha del conteo.' : countDate > today ? 'La fecha no puede ser futura.' : undefined;
  const start = async () => {
    setStartTouched(true);
    if (!startWarehouse || dateProblem) return;
    const outcome = await open.run(toOpenCount(startWarehouse, countDate, notes));
    if (!outcome.ok) return;
    notify.success('Toma física abierta', `${outcome.result.number}: cuente cada producto y registre la cantidad.`);
    setNotes('');
    setStartTouched(false);
    setPosted(null);
    sheetQuery.reload();
  };

  // ------------------------------------------------------------------------------------------------ contar, quitar, contabilizar, anular
  const [request, setRequest] = useState<CountRequest | null>(null);
  const askCount = (sku: string, binCode: string | null) => {
    // El formulario carga el producto y lleva el foco a la cantidad (el navegador lo desplaza a la vista).
    setRequest((current) => ({ sku, binCode, nonce: (current?.nonce ?? 0) + 1 }));
  };

  const remove = useRpcCommand('RemoveCountCommand', { notifyError: false });
  const [removing, setRemoving] = useState<{ line: CountLineRecord; open: boolean } | null>(null);
  const confirmRemove = async () => {
    if (!sheet || !removing) return false;
    const line = removing.line;
    const outcome = await remove.run(toRemoveCount(sheet.id, line));
    if (outcome.ok) {
      notify.success('Conteo quitado', `${line.sku} en ${line.binCode}`);
      sheetQuery.reload();
    }
    return outcome;
  };

  const [posting, setPosting] = useState(false);
  const [posted, setPosted] = useState<PostResultRecord | null>(null);
  const onPosted = (result: PostResultRecord) => {
    setPosted(result);
    notify.success('Toma física contabilizada', plainMessage(result.message));
    sheetQuery.reload();
  };

  const cancel = useRpcCommand('CancelPhysicalCountCommand', { notifyError: false });
  const [cancelling, setCancelling] = useState(false);
  const confirmCancel = async () => {
    if (!sheet) return false;
    const outcome = await cancel.run({ physicalCountId: sheet.id });
    if (outcome.ok) {
      notify.info('Toma física anulada', plainMessage(outcome.result));
      sheetQuery.reload();
    }
    return outcome;
  };

  // ------------------------------------------------------------------------------------------------ imprimir y exportar
  const [print, setPrint] = useState<{ count: number; at: Date }>({ count: 0, at: new Date() });
  const notifyRef = useRef(notify);
  useEffect(() => {
    notifyRef.current = notify;
  });
  // Se imprime DESPUÉS de dibujar la planilla con la hora del pedido (una vez por pedido).
  useEffect(() => {
    if (print.count === 0) return;
    if (!printCountSheet()) notifyRef.current.warning('No se pudo imprimir', 'Este navegador no permite imprimir desde la página.');
  }, [print]);
  const toPrint = useMemo(() => printRows(productList, filters), [productList, filters]);

  const exportRows = () => {
    const file = exportCsv(sheet ? `toma fisica ${sheet.number}` : 'toma fisica', COUNT_CSV, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const reload = () => {
    sheetQuery.reload();
    products.reload();
    bins.reload();
  };

  const rowActions = (entry: CountEntry): RowActionItem[] => [
    { label: 'Corregir conteo', icon: <Pencil />, onSelect: () => askCount(entry.line.sku, entry.line.binCode), hidden: !canRecord },
    { label: 'Ver ficha y kardex', icon: <PackageSearch />, onSelect: () => navigate(ROUTES.panelModule(`stock?ficha=${encodeURIComponent(entry.line.sku)}`)) },
    {
      label: 'Quitar conteo',
      icon: <Trash2 />,
      tone: 'danger',
      onSelect: () => {
        remove.reset();
        setRemoving({ line: entry.line, open: true });
      },
      hidden: !canRemove,
    },
  ];

  const warehouseLabel = warehouses.find((option) => option.value.toUpperCase() === (countWarehouse ?? '').toUpperCase())?.label ?? countWarehouse ?? '—';

  const header = (
    <>
      {posted && (
        <Alert
          tone="success"
          title={`Toma ${posted.number} contabilizada`}
          actions={
            <>
              <Button variant="outline" leftIcon={<ArrowLeftRight />} to={ROUTES.panelModule('movimientos')}>
                Ver los movimientos
              </Button>
              <Button variant="ghost" onClick={() => setPosted(null)}>
                Cerrar aviso
              </Button>
            </>
          }
        >
          <span data-testid="resultado-toma">
            {plainMessage(posted.message)} ({formatNumber(posted.surpluses)} sobrantes, {formatNumber(posted.shortages)} faltantes, {formatNumber(posted.initialBalances)} saldos iniciales,{' '}
            {formatNumber(posted.matching)} cuadran).
          </span>
        </Alert>
      )}
      {warehouses.length > 1 && (
        <div className="max-w-md">
          <SelectField
            label="Almacén"
            allLabel={false}
            value={chosenWarehouse || workspace.data?.warehouseCode || ''}
            onChange={(value) => setParam(WAREHOUSE_PARAM, value === workspace.data?.warehouseCode ? '' : value)}
            options={warehouses}
            hint="Cada almacén tiene su propia toma."
          />
        </div>
      )}
    </>
  );

  const printSheet = (
    <CountSheetPrint rows={toPrint} number={sheet?.number ?? null} countDate={sheet?.countDate ?? null} warehouse={warehouseLabel} scope={scopeText(filters)} printedAt={print.at} />
  );
  const printButton = (
    <Button variant="outline" leftIcon={<Printer />} disabled={!products.data} onClick={() => setPrint((current) => ({ count: current.count + 1, at: new Date() }))}>
      Imprimir planilla
    </Button>
  );

  // ------------------------------------------------------------------------------------------------ sin datos todavía
  if (sheetQuery.data === undefined) {
    return (
      <Page title="Toma física" description="Compare lo que hay en los estantes con lo que dice el sistema y corrija las diferencias con ajustes.">
        {header}
        {sheetQuery.error ? (
          <ErrorState error={sheetQuery.error} operation="GetOpenPhysicalCountQuery" onRetry={sheetQuery.reload} retrying={sheetQuery.fetching} />
        ) : (
          <LoadingState label="Buscando la toma física en curso…" rows={3} />
        )}
      </Page>
    );
  }

  // ------------------------------------------------------------------------------------------------ sin toma abierta
  if (!sheet) {
    return (
      <Page
        title="Toma física"
        description="Compare lo que hay en los estantes con lo que dice el sistema y corrija las diferencias con ajustes."
        actions={printButton}
      >
        {header}
        <Section
          title="No hay una toma física en curso"
          description="Una toma física compara lo que hay en los estantes con lo que dice el sistema y corrige las diferencias con ajustes. Iníciela: queda abierta para todo el equipo, que cuenta a la vez."
        >
          {canOpen ? (
            <Form
              onSubmit={start}
              error={open.errorText}
              busy={open.sending}
              actions={
                <Button type="submit" leftIcon={<ClipboardCheck />} loading={open.sending} disabled={!startWarehouse}>
                  Iniciar toma física
                </Button>
              }
            >
              <FormGrid>
                <TextField label="Almacén" value={warehouseLabel} onChange={() => undefined} readOnly hint="El almacén de trabajo de la sucursal activa (o el elegido arriba)." />
                <TextField
                  label="Fecha del conteo"
                  type="date"
                  value={countDate}
                  onChange={(value) => {
                    setCountDate(value);
                    open.reset();
                  }}
                  max={today}
                  error={startTouched ? dateProblem : undefined}
                  required
                  hint="Hoy, o un día anterior si el conteo ya se hizo."
                />
              </FormGrid>
              <TextField label="Observación" value={notes} onChange={setNotes} optional maxLength={200} placeholder="Ej.: Conteo mensual de periféricos" />
            </Form>
          ) : (
            <p className="text-sm text-text-muted">Pida a quien administra el inventario que la inicie.</p>
          )}
        </Section>
        {printSheet}
      </Page>
    );
  }

  // ------------------------------------------------------------------------------------------------ toma abierta
  return (
    <Page
      title="Toma física"
      description={`Toma ${sheet.number} en curso: cuente cada producto y registre la cantidad (a mano o con el lector de códigos).`}
      actions={
        <>
          {printButton}
          {canCancel && (
            <Button
              variant="outline"
              leftIcon={<Ban />}
              onClick={() => {
                cancel.reset();
                setCancelling(true);
              }}
            >
              Anular toma
            </Button>
          )}
          {canPost && (
            <Button leftIcon={<ClipboardCheck />} disabled={sheet.lines.length === 0} onClick={() => setPosting(true)}>
              Generar ajustes
            </Button>
          )}
        </>
      }
    >
      {header}
      <section className="space-y-3 rounded-card border border-border bg-surface p-4 shadow-card" aria-label="Toma en curso" data-testid="toma-en-curso">
        <p className="flex flex-wrap items-center gap-2 text-sm text-text">
          <Warehouse aria-hidden="true" className="size-4 text-accent" />
          <strong>{sheet.number}</strong> · abierta el {formatDate(sheet.countDate)} · almacén {sheet.warehouseCode}
          {sheet.notes ? ` · ${sheet.notes}` : ''}
        </p>
        <div className="space-y-1">
          <p className="text-sm text-text-muted" data-testid="progreso-toma">
            Contados {formatNumber(progress.counted)} de {formatNumber(progress.expected)} productos{scopeText(filters) ? ` (${scopeText(filters)})` : ''} · {formatNumber(progress.percent)} %
          </p>
          <div
            role="progressbar"
            aria-label="Progreso de la toma"
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={progress.percent}
            className="h-2 overflow-hidden rounded-full bg-surface-3"
          >
            <div className="h-full rounded-full bg-accent transition-[width] duration-300" style={{ width: `${progress.percent}%` }} />
          </div>
        </div>
      </section>

      {canRecord && (
        <RecordCountForm
          sheet={sheet}
          products={productList}
          productsLoading={products.loading}
          bins={bins.data ?? []}
          preview={preview}
          request={request}
          onRecorded={sheetQuery.reload}
        />
      )}

      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Producto, SKU, posición o quién contó" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Categoría" allLabel="Todas las categorías" value={filters.categoria} onChange={(value) => table.setFilter('categoria', value)} options={categories} />
        <SelectField label="Ubicación" allLabel="Todas las posiciones" value={filters.ubicacion} onChange={(value) => table.setFilter('ubicacion', value)} options={binChoices} />
        <SelectField label="Diferencia" value={filters.diferencia} onChange={(value) => table.setFilter('diferencia', value)} options={DIFFERENCE_OPTIONS} />
        <SelectField label="Contó" allLabel="Todos" value={filters.contador} onChange={(value) => table.setFilter('contador', value)} options={counters} />
      </FilterBar>

      <Collapsible label="Ver resumen de diferencias" openLabel="Ocultar resumen de diferencias" icon={<BarChart3 />} description="Contados, sobrantes, faltantes y los que cuadran.">
        <div className="grid grid-cols-[repeat(auto-fit,minmax(10rem,1fr))] gap-3" data-testid="resumen-diferencias">
          <StatCard label="Conteos" value={formatNumber(summary.lines)} hint={`${formatNumber(summary.products)} productos`} icon={<ClipboardList />} />
          <StatCard label="Sobrantes" value={formatNumber(summary.surpluses)} hint="AJUSTE (+)" icon={<ClipboardCheck />} tone={summary.surpluses > 0 ? 'warning' : 'default'} />
          <StatCard label="Faltantes" value={formatNumber(summary.shortages)} hint="AJUSTE (−)" icon={<ClipboardCheck />} tone={summary.shortages > 0 ? 'danger' : 'default'} />
          <StatCard label="Cuadran" value={formatNumber(summary.matching)} hint="Sin movimiento" icon={<ClipboardCheck />} tone="success" />
        </div>
      </Collapsible>

      <Tabs
        label="Conteos y pendientes"
        value={view}
        onChange={(next) => setParam(VIEW_PARAM, next === 'pendientes' ? 'pendientes' : '')}
        tabs={[
          { id: 'contados', label: 'Contados', icon: <ClipboardList />, count: rows.length },
          { id: 'pendientes', label: 'Pendientes de contar', icon: <Hourglass />, count: pending.length },
        ]}
      >
        <TabPanel id="contados">
          <div className="space-y-4">
            <Toolbar
              label="Acciones de la lista de conteos"
              end={
                <>
                  <Button variant="outline" leftIcon={<RefreshCw />} loading={sheetQuery.fetching} onClick={reload}>
                    Actualizar
                  </Button>
                  <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
                    Exportar CSV
                  </Button>
                </>
              }
            >
              <span className="text-sm text-text-muted" data-testid="conteos-resumen">
                {formatNumber(rows.length)} de {formatNumber(entries.length)} conteos · {formatNumber(summary.surpluses)} sobrantes · {formatNumber(summary.shortages)} faltantes
              </span>
            </Toolbar>
            <DataTable
              caption="Conteos de la toma"
              columns={COLUMNS}
              rows={rows}
              rowKey={(entry) => entry.key}
              rowLabel={(entry) => `el conteo de ${entry.line.name} en ${entry.line.binCode}`}
              refreshing={sheetQuery.fetching}
              error={sheetQuery.error}
              onRetry={sheetQuery.reload}
              operation="GetOpenPhysicalCountQuery"
              {...table.tableProps}
              rowActions={rowActions}
              empty={{
                title: entries.length === 0 ? 'Todavía no hay conteos' : 'Ningún conteo coincide con los filtros',
                description: entries.length === 0 ? 'Registre lo contado arriba: aquí verá la diferencia contra el sistema.' : 'Pruebe con otra búsqueda o limpie los filtros.',
                icon: <ClipboardList />,
                action:
                  entries.length === 0 ? undefined : (
                    <Button variant="outline" onClick={table.clearFilters}>
                      Limpiar filtros
                    </Button>
                  ),
              }}
            />
          </div>
        </TabPanel>
        <TabPanel id="pendientes">
          <DataTable
            caption="Productos pendientes de contar"
            columns={PENDING_COLUMNS}
            rows={products.data ? pending : undefined}
            rowKey={(product) => product.sku}
            rowLabel={(product) => `el producto ${product.name}`}
            loading={products.loading}
            error={products.error}
            onRetry={products.reload}
            operation="GetProductLookupQuery"
            defaultSort={{ column: 'posicion', direction: 'asc' }}
            rowActions={(product) => [{ label: 'Contar', icon: <ClipboardCheck />, onSelect: () => askCount(product.sku, product.primaryBin), hidden: !canRecord }]}
            empty={{ title: 'No queda nada por contar', description: 'Todos los productos de esta selección ya tienen su conteo.', icon: <ClipboardCheck /> }}
          />
        </TabPanel>
      </Tabs>

      {printSheet}
      <PostCountDialog sheet={posting ? sheet : null} onClose={() => setPosting(false)} onPosted={onPosted} />
      <ConfirmDialog
        open={cancelling}
        onClose={() => setCancelling(false)}
        tone="danger"
        title={`¿Anular la toma ${sheet.number}?`}
        message={`Se descartan los ${formatNumber(sheet.lines.length)} conteos registrados y no se genera ningún ajuste. No se puede deshacer.`}
        confirmLabel="Anular toma"
        onConfirm={confirmCancel}
        error={cancel.errorText ?? undefined}
      />
      <ConfirmDialog
        open={removing?.open ?? false}
        onClose={() => setRemoving((shown) => (shown?.open ? { ...shown, open: false } : shown))}
        tone="danger"
        title={`¿Quitar el conteo de ${removing?.line.sku ?? ''} en ${removing?.line.binCode ?? ''}?`}
        message="Se borra lo contado de ese producto en esa posición; puede volver a contarlo después."
        confirmLabel="Quitar conteo"
        onConfirm={confirmRemove}
        error={remove.errorText ?? undefined}
      />
    </Page>
  );
}
