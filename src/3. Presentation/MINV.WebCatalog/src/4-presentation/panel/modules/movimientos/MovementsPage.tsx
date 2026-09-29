// Inventario › Movimientos (en el escritorio: MovementView + MovementViewModel). Los últimos movimientos registrados con
// filtros en la dirección (tipo, entradas o salidas, usuario, producto, fechas, búsqueda y cuántos traer), tabla ordenable
// y paginada, detalle lateral y exportar CSV; el botón «Registrar movimiento» (y los atajos de entrada, salida y ajuste
// según el rol) abre el registro con poka-yoke. `?registrar=<tipo>&sku=<SKU>` lo abre desde otro módulo. La tendencia
// por día está PLEGADA detrás de «Ver tendencia».
//
// Operaciones: GetRecentMovementsQuery y GetMovementTypesQuery (`inventory.stock.view`); el registro, en
// RegisterMovementDialog. Solo se ofrecen los tipos que el rol puede registrar (el servidor decide igual).

import { ArrowLeftRight, ChartColumn, Download, Eye, History, ListFilter, PackageMinus, PackagePlus, PackageSearch, RefreshCw, Wrench } from 'lucide-react';
import { useMemo, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { usePermissions, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Button,
  Collapsible,
  ComboBox,
  DataTable,
  DateRangeField,
  DetailList,
  FilterBar,
  Page,
  SearchField,
  SelectField,
  SidePanel,
  StatusBadge,
  Toolbar,
  useNotify,
  type DataTableColumn,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatDate, formatDateLong, formatDateTime, formatNumber, formatTime, sortRows, toIsoDate, type SortState } from '@/4-presentation/panel/lib';
import { MovementTrend } from './MovementTrend';
import {
  FLOW_OPTIONS,
  MOVEMENT_CSV,
  MOVEMENT_FILTERS,
  REGISTER_PARAM,
  SKU_PARAM,
  TAKE_OPTIONS,
  allowedTypes,
  filterMovements,
  productCardLink,
  productOptions,
  signedQuantity,
  takeOf,
  toMovementEntries,
  typeOptions,
  userOptions,
  type MovementEntry,
} from './movements';
import { RegisterMovementDialog } from './RegisterMovementDialog';

const COLUMNS: DataTableColumn<MovementEntry>[] = [
  {
    id: 'registrado',
    header: 'Registrado',
    value: (entry) => new Date(entry.record.recordedAt),
    cell: (entry) => (
      <span className="block whitespace-nowrap">
        <span className="block">{formatDateTime(entry.record.recordedAt)}</span>
        {toIsoDate(entry.record.recordedAt) !== entry.record.businessDate && <span className="block text-xs text-text-muted">Fecha: {formatDate(entry.record.businessDate)}</span>}
      </span>
    ),
  },
  {
    id: 'tipo',
    header: 'Tipo',
    value: (entry) => entry.typeTitle,
    cell: (entry) => <StatusBadge tone={entry.isIn ? 'success' : 'warning'}>{entry.typeTitle}</StatusBadge>,
  },
  {
    id: 'producto',
    header: 'Producto',
    card: 'title',
    value: (entry) => entry.record.name,
    cell: (entry) => (
      <span className="block min-w-44">
        <span className="block">{entry.record.name}</span>
        <span className="block text-xs font-normal text-text-muted">{entry.record.sku}</span>
      </span>
    ),
  },
  {
    id: 'cantidad',
    header: 'Cantidad',
    align: 'end',
    value: (entry) => (entry.isIn ? entry.record.quantity : -entry.record.quantity),
    cell: (entry) => <span className={entry.isIn ? 'text-success-text' : 'text-warning-text'}>{signedQuantity(entry)}</span>,
  },
  { id: 'posicion', header: 'Posición', value: (entry) => entry.record.binCode },
  { id: 'documento', header: 'Documento', value: (entry) => entry.record.document },
  { id: 'usuario', header: 'Usuario', value: (entry) => entry.who },
];

function inTableOrder(list: readonly MovementEntry[], sort: SortState | null): readonly MovementEntry[] {
  const column = sort ? COLUMNS.find((item) => item.id === sort.column) : undefined;
  return sort && column?.value ? sortRows(list, column.value, sort.direction) : list;
}

export function MovementsPage() {
  const notify = useNotify();
  const navigate = useNavigate();
  const { can } = usePermissions();
  const table = useTableState({ filters: MOVEMENT_FILTERS, sort: { column: 'registrado', direction: 'desc' } });
  const filters = table.filters;
  const take = takeOf(filters.registros);

  const movements = useRpcQuery('GetRecentMovementsQuery', { take });
  const types = useRpcQuery('GetMovementTypesQuery', {});
  const entries = useMemo(() => toMovementEntries(movements.data ?? []), [movements.data]);
  const rows = useMemo(() => filterMovements(entries, filters), [entries, filters]);
  const users = useMemo(() => userOptions(entries), [entries]);
  const products = useMemo(() => productOptions(entries), [entries]);
  const allTypes = useMemo(() => typeOptions(types.data ?? []), [types.data]);

  const rights = { warehouse: can('inventory.movements.register.warehouse'), sales: can('inventory.movements.register.sales') };
  const registrable = allowedTypes(types.data ?? [], rights);
  const allowedCodes = registrable.map((type) => type.code);
  const canRegister = rights.warehouse || rights.sales;

  // Registro: se abre con `?registrar=` (también desde otros módulos). Cada apertura empieza un formulario nuevo.
  const [params, setParams] = useSearchParams();
  const requested = params.get(REGISTER_PARAM);
  const presetSku = params.get(SKU_PARAM);
  const registerOpen = requested !== null && canRegister;
  const [dialog, setDialog] = useState({ key: 0, open: false });
  if (registerOpen !== dialog.open) setDialog({ key: registerOpen ? dialog.key + 1 : dialog.key, open: registerOpen });
  const openRegister = (type: string, sku?: string) =>
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.set(REGISTER_PARAM, type);
        if (sku) next.set(SKU_PARAM, sku);
        else next.delete(SKU_PARAM);
        return next;
      },
      { replace: true },
    );
  const closeRegister = () =>
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.delete(REGISTER_PARAM);
        next.delete(SKU_PARAM);
        return next;
      },
      { replace: true },
    );

  // Detalle lateral (y la guarda del menú «⋯», que React propaga a la fila: ver «Pendientes» del informe M5).
  const [detail, setDetail] = useState<{ entry: MovementEntry; open: boolean } | null>(null);
  const pickingAction = useRef(false);
  const fromMenu = (action: () => void) => () => {
    pickingAction.current = true;
    action();
    setTimeout(() => {
      pickingAction.current = false;
    }, 0);
  };
  const openDetail = (entry: MovementEntry) => setDetail({ entry, open: true });
  const closeDetailPanel = () => setDetail((current) => (current ? { ...current, open: false } : current));
  const onlyProduct = (sku: string) => {
    table.setFilter('producto', sku);
    closeDetailPanel();
  };
  const registerAgain = (sku: string) => {
    closeDetailPanel();
    openRegister('1', sku);
  };

  const exportRows = () => {
    const file = exportCsv('movimientos', MOVEMENT_CSV, inTableOrder(rows, table.sort));
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const product = products.find((option) => option.value === filters.producto) ?? (filters.producto ? { value: filters.producto, label: filters.producto } : null);
  const entry = detail?.entry;
  const inCount = rows.filter((item) => item.isIn).length;

  return (
    <Page
      title="Movimientos"
      description="Entradas, salidas y ajustes del inventario. Para corregir un error se registra un ajuste: los movimientos no se borran."
      actions={
        canRegister && (
          <>
            {allowedCodes.includes('ENTRADA') && (
              <Button variant="outline" leftIcon={<PackagePlus />} onClick={() => openRegister('ENTRADA')}>
                Registrar entrada
              </Button>
            )}
            {allowedCodes.includes('SALIDA') && (
              <Button variant="outline" leftIcon={<PackageMinus />} onClick={() => openRegister('SALIDA')}>
                Registrar salida
              </Button>
            )}
            {(allowedCodes.includes('AJUSTE_NEG') || allowedCodes.includes('AJUSTE_POS')) && (
              <Button variant="outline" leftIcon={<Wrench />} onClick={() => openRegister('ajuste')}>
                Registrar ajuste
              </Button>
            )}
            <Button leftIcon={<ArrowLeftRight />} onClick={() => openRegister('1')}>
              Registrar movimiento
            </Button>
          </>
        )
      }
    >
      <Collapsible label="Ver tendencia de movimientos" openLabel="Ocultar tendencia de movimientos" icon={<ChartColumn />} description="Entradas y salidas por día.">
        <MovementTrend />
      </Collapsible>

      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Producto, SKU, documento, posición o usuario" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Tipo" allLabel="Todos los tipos" value={filters.tipo} onChange={(value) => table.setFilter('tipo', value)} options={allTypes} />
        <SelectField label="Entradas o salidas" allLabel="Entradas y salidas" value={filters.flujo} onChange={(value) => table.setFilter('flujo', value)} options={FLOW_OPTIONS} />
        <SelectField label="Usuario" allLabel="Todos los usuarios" value={filters.usuario} onChange={(value) => table.setFilter('usuario', value)} options={users} />
        <ComboBox label="Producto" placeholder="Nombre o SKU" value={product} onChange={(option) => table.setFilter('producto', option?.value ?? '')} options={products} />
        <SelectField
          label="Movimientos a revisar"
          allLabel={false}
          value={filters.registros}
          onChange={(value) => table.setFilter('registros', value || MOVEMENT_FILTERS.registros)}
          options={TAKE_OPTIONS}
          hint="Los más recientes; los filtros se aplican sobre ellos."
        />
        <DateRangeField label="Fechas" value={table.dateRange()} onChange={(range) => table.setDateRange(range)} />
      </FilterBar>

      <Toolbar
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={movements.fetching && !movements.loading} onClick={movements.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {movements.data && (
          <span className="text-sm text-text-muted" data-testid="movimientos-resumen">
            {formatNumber(rows.length)} de {formatNumber(entries.length)} movimientos · {formatNumber(inCount)} entradas y {formatNumber(rows.length - inCount)} salidas
          </span>
        )}
      </Toolbar>

      <DataTable
        caption="Últimos movimientos"
        columns={COLUMNS}
        rows={movements.data ? rows : undefined}
        rowKey={(item) => item.key}
        rowLabel={(item) => `el movimiento ${item.typeTitle} de ${item.record.name}`}
        loading={movements.loading}
        refreshing={movements.fetching && !movements.loading}
        error={movements.error}
        onRetry={movements.reload}
        operation="GetRecentMovementsQuery"
        {...table.tableProps}
        onRowOpen={(item) => {
          if (!pickingAction.current) openDetail(item);
        }}
        activeRowKey={detail?.open ? detail.entry.key : null}
        rowActions={(item) => [
          { label: 'Ver detalle', icon: <Eye />, onSelect: fromMenu(() => openDetail(item)) },
          { label: 'Ver ficha y kardex', icon: <PackageSearch />, onSelect: fromMenu(() => navigate(productCardLink(item.record.sku))) },
          { label: 'Ver solo este producto', icon: <ListFilter />, onSelect: fromMenu(() => onlyProduct(item.record.sku)), hidden: filters.producto === item.record.sku },
          { label: 'Registrar otro movimiento de este producto', icon: <ArrowLeftRight />, onSelect: fromMenu(() => registerAgain(item.record.sku)), hidden: !canRegister },
        ]}
        empty={{
          title: entries.length === 0 ? 'Todavía no hay movimientos' : 'No hay movimientos con estos filtros',
          description: entries.length === 0 ? undefined : 'Pruebe con otras fechas, revise más movimientos o limpie los filtros.',
          icon: <History />,
          action:
            entries.length === 0 ? undefined : (
              <Button variant="outline" onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
        }}
      />

      <SidePanel
        open={detail?.open ?? false}
        onClose={closeDetailPanel}
        title={entry ? entry.typeTitle : 'Movimiento'}
        description={entry ? `${entry.record.name} · ${formatDateTime(entry.record.recordedAt)}` : undefined}
        headerExtra={entry && <StatusBadge tone={entry.isIn ? 'success' : 'warning'}>{entry.isIn ? 'Entrada' : 'Salida'}</StatusBadge>}
        footer={
          entry && (
            <div className="flex flex-col gap-2">
              {canRegister && (
                <Button leftIcon={<ArrowLeftRight />} fullWidth onClick={() => registerAgain(entry.record.sku)}>
                  Registrar otro movimiento de este producto
                </Button>
              )}
              <Button variant="outline" leftIcon={<PackageSearch />} fullWidth to={productCardLink(entry.record.sku)}>
                Ver ficha y kardex
              </Button>
              {filters.producto !== entry.record.sku && (
                <Button variant="ghost" leftIcon={<ListFilter />} fullWidth onClick={() => onlyProduct(entry.record.sku)}>
                  Ver solo los movimientos de este producto
                </Button>
              )}
            </div>
          )
        }
      >
        {entry && (
          <div data-testid="detalle-movimiento">
            <DetailList
              items={[
                { label: 'Cantidad', value: signedQuantity(entry) },
                { label: 'Tipo', value: entry.typeTitle },
                { label: 'Producto', value: entry.record.name, wide: true },
                { label: 'SKU', value: entry.record.sku },
                { label: 'Posición', value: entry.record.binCode },
                { label: 'Fecha del movimiento', value: formatDateLong(entry.record.businessDate) },
                { label: 'Registrado', value: `${formatDate(entry.record.recordedAt)} a las ${formatTime(entry.record.recordedAt)}` },
                { label: 'Documento', value: entry.record.document },
                { label: 'Usuario', value: entry.who },
              ]}
            />
          </div>
        )}
      </SidePanel>

      <RegisterMovementDialog
        key={dialog.key}
        open={registerOpen}
        requestedType={requested}
        presetSku={presetSku}
        types={registrable}
        typesLoading={types.loading}
        onClose={closeRegister}
        onRegistered={movements.reload}
      />
    </Page>
  );
}
