// Muestra de una pantalla de lista completa con datos de ejemplo: filtros en la dirección (useTableState), búsqueda,
// listas desplegables, lista con búsqueda, rango de fechas, barra de acciones, tabla con orden, páginas, selección,
// fila desplegable, totales y acciones por fila, detalle lateral con pestañas, confirmación peligrosa y exportar CSV.
// Es el modelo a copiar por los módulos del panel.

import { Ban, Copy, Download, Eye, Printer, RefreshCw } from 'lucide-react';
import { useMemo, useState } from 'react';
import { usePermissions } from '../hooks/usePermissions';
import { useTableState } from '../hooks/useTableState';
import { csvColumnsOf, exportCsv, formatDateTime, formatMoney, formatNumber, formatQuantity, inRange, matchesSearch } from '../lib';
import {
  Button,
  ComboBox,
  ConfirmDialog,
  DataTable,
  DateRangeField,
  DetailList,
  FilterBar,
  SearchField,
  SelectField,
  SidePanel,
  StatusBadge,
  TabPanel,
  Tabs,
  TextArea,
  Toolbar,
  statusOptions,
  useNotify,
  type DataTableColumn,
} from '../kit';
import { BRANCHES, CUSTOMER_OPTIONS, SALE_STATUSES, branchName, buildSales, type SaleStatus, type SampleSale } from './sampleData';

const COLUMNS: DataTableColumn<SampleSale>[] = [
  { id: 'numero', header: 'Número', value: (sale) => sale.number, card: 'title', className: 'whitespace-nowrap' },
  { id: 'fecha', header: 'Fecha', value: (sale) => new Date(sale.date), cell: (sale) => formatDateTime(sale.date), className: 'whitespace-nowrap' },
  {
    id: 'cliente',
    header: 'Cliente',
    value: (sale) => sale.customer.name,
    cell: (sale) => (
      <span className="block min-w-36">
        <span className="block">{sale.customer.name}</span>
        <span className="block text-xs text-text-muted">{sale.customer.nit ? `NIT ${sale.customer.nit}` : 'Sin NIT'}</span>
      </span>
    ),
  },
  { id: 'sucursal', header: 'Sucursal', value: (sale) => branchName(sale.branch) },
  { id: 'canal', header: 'Canal', value: (sale) => (sale.channel === 'Web' ? 'Tienda web' : 'Caja') },
  { id: 'estado', header: 'Estado', value: (sale) => SALE_STATUSES[sale.status].label, cell: (sale) => <StatusBadge status={sale.status} statuses={SALE_STATUSES} /> },
  {
    id: 'articulos',
    header: 'Artículos',
    align: 'end',
    value: (sale) => sale.items,
    footer: (rows) => formatQuantity(rows.reduce((sum, sale) => sum + sale.items, 0)),
  },
  {
    id: 'total',
    header: 'Total',
    align: 'end',
    value: (sale) => sale.total,
    cell: (sale) => formatMoney(sale.total),
    footer: (rows) => formatMoney(rows.filter((sale) => sale.status !== 'Voided').reduce((sum, sale) => sum + sale.total, 0)),
  },
];

/** Lo que va al CSV: las columnas de la tabla más las notas (que no se ven en la tabla). */
const CSV_COLUMNS = [...csvColumnsOf(COLUMNS), { header: 'NIT', value: (sale: SampleSale) => sale.customer.nit }, { header: 'Notas', value: (sale: SampleSale) => sale.notes }];

const CHANNELS = [
  { value: 'Caja', label: 'Caja' },
  { value: 'Web', label: 'Tienda web' },
];

const FILTERS = { q: '', estado: '', sucursal: '', canal: '', cliente: '', desde: '', hasta: '' };

interface Shown<T> {
  item: T;
  open: boolean;
}

export function SalesDemo() {
  const notify = useNotify();
  const { can } = usePermissions();
  const table = useTableState({ filters: FILTERS, sort: { column: 'fecha', direction: 'desc' } });
  const filters = table.filters;
  const [sales, setSales] = useState(() => buildSales());
  const [selection, setSelection] = useState<Set<string>>(() => new Set());
  const [detail, setDetail] = useState<Shown<SampleSale> | null>(null);
  const [detailTab, setDetailTab] = useState<'resumen' | 'productos'>('resumen');
  const [voiding, setVoiding] = useState<Shown<SampleSale> | null>(null);
  const [reason, setReason] = useState('');
  const [refreshing, setRefreshing] = useState(false);

  const rows = useMemo(() => {
    const range = { from: filters.desde || null, to: filters.hasta || null };
    return sales.filter(
      (sale) =>
        (!filters.estado || sale.status === filters.estado) &&
        (!filters.sucursal || sale.branch === filters.sucursal) &&
        (!filters.canal || sale.channel === filters.canal) &&
        (!filters.cliente || sale.customer.id === filters.cliente) &&
        inRange(sale.date, range) &&
        matchesSearch(filters.q, [sale.number, sale.customer.name, sale.customer.nit]),
    );
  }, [sales, filters]);

  const customer = CUSTOMER_OPTIONS.find((option) => option.value === filters.cliente) ?? null;
  const openSale = (sale: SampleSale) => {
    setDetailTab('resumen');
    setDetail({ item: sale, open: true });
  };
  const askVoid = (sale: SampleSale) => {
    setReason('');
    setVoiding({ item: sale, open: true });
  };

  const exportRows = (list: readonly SampleSale[], name: string) => {
    const file = exportCsv(name, CSV_COLUMNS, list);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(list.length)} filas).`);
  };

  const refresh = () => {
    setRefreshing(true);
    setTimeout(() => setRefreshing(false), 900);
  };

  const confirmVoid = () =>
    new Promise<boolean>((resolve) => {
      const target = voiding?.item;
      setTimeout(() => {
        if (target) {
          setSales((list) => list.map((sale) => (sale.number === target.number ? { ...sale, status: 'Voided' } : sale)));
          setDetail((current) => (current && current.item.number === target.number ? { ...current, item: { ...current.item, status: 'Voided' } } : current));
          notify.success('Venta anulada', `${target.number} · motivo: ${reason.trim()}`);
        }
        resolve(true);
      }, 700);
    });

  const sale = detail?.item;

  return (
    <div className="space-y-4">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Número, cliente o NIT" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Estado" value={filters.estado as SaleStatus | ''} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(SALE_STATUSES)} />
        <SelectField label="Sucursal" allLabel="Todas las sucursales" value={filters.sucursal} onChange={(value) => table.setFilter('sucursal', value)} options={BRANCHES} />
        <SelectField label="Canal" value={filters.canal} onChange={(value) => table.setFilter('canal', value)} options={CHANNELS} />
        <ComboBox label="Cliente" placeholder="Nombre o NIT" value={customer} onChange={(option) => table.setFilter('cliente', option?.value ?? '')} options={CUSTOMER_OPTIONS} />
        <DateRangeField label="Fechas" value={table.dateRange()} onChange={(range) => table.setDateRange(range)} />
      </FilterBar>

      <Toolbar
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={refreshing} onClick={refresh}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={() => exportRows(rows, 'ventas')}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {selection.size > 0 ? (
          <>
            <span className="text-sm font-medium text-text">{selection.size === 1 ? '1 venta seleccionada' : `${formatNumber(selection.size)} ventas seleccionadas`}</span>
            <Button variant="subtle" onClick={() => exportRows(sales.filter((item) => selection.has(item.number)), 'ventas-seleccionadas')}>
              Exportar selección
            </Button>
          </>
        ) : (
          <span className="text-sm text-text-muted">
            {formatNumber(rows.length)} ventas · {formatMoney(rows.filter((item) => item.status !== 'Voided').reduce((sum, item) => sum + item.total, 0))}
          </span>
        )}
      </Toolbar>

      <DataTable
        caption="Ventas de ejemplo"
        columns={COLUMNS}
        rows={rows}
        rowKey={(item) => item.number}
        rowLabel={(item) => `la venta ${item.number}`}
        refreshing={refreshing}
        {...table.tableProps}
        selection={selection}
        onSelectionChange={setSelection}
        renderExpanded={(item) => (
          <ul className="space-y-1 text-sm">
            {item.lines.map((line, index) => (
              <li key={`${line.sku}-${index}`} className="flex flex-wrap justify-between gap-x-4">
                <span>
                  {line.quantity} × {line.name} <span className="text-text-faint">{line.sku}</span>
                </span>
                <span className="tabular-nums">{formatMoney(line.quantity * line.price)}</span>
              </li>
            ))}
          </ul>
        )}
        rowActions={(item) => [
          { label: 'Ver detalle', icon: <Eye />, onSelect: () => openSale(item) },
          { label: 'Reimprimir comprobante', icon: <Printer />, onSelect: () => notify.info('Comprobante enviado a la impresora', item.number) },
          {
            label: 'Copiar número',
            icon: <Copy />,
            onSelect: () => {
              void navigator.clipboard?.writeText(item.number).catch(() => undefined);
              notify.success('Número copiado', item.number);
            },
          },
          {
            label: 'Anular venta',
            icon: <Ban />,
            tone: 'danger',
            onSelect: () => askVoid(item),
            disabled: item.status === 'Voided',
            disabledReason: item.status === 'Voided' ? 'La venta ya está anulada.' : undefined,
            hidden: !can('sales.pos.operate'),
          },
        ]}
        onRowOpen={openSale}
        activeRowKey={detail?.open ? detail.item.number : null}
        empty={{
          title: 'No hay ventas con estos filtros',
          description: 'Pruebe con otras fechas o limpie los filtros.',
          action: (
            <Button variant="outline" onClick={table.clearFilters}>
              Limpiar filtros
            </Button>
          ),
        }}
      />

      <SidePanel
        open={detail?.open ?? false}
        onClose={() => setDetail((current) => (current?.open ? { ...current, open: false } : current))}
        title={sale ? `Venta ${sale.number}` : 'Venta'}
        description={sale ? formatDateTime(sale.date) : undefined}
        headerExtra={sale && <StatusBadge status={sale.status} statuses={SALE_STATUSES} />}
        footer={
          sale &&
          sale.status !== 'Voided' &&
          can('sales.pos.operate') && (
            <Button variant="danger" leftIcon={<Ban />} fullWidth onClick={() => askVoid(sale)}>
              Anular venta
            </Button>
          )
        }
      >
        {sale && (
          <Tabs
            label="Secciones de la venta"
            value={detailTab}
            onChange={setDetailTab}
            tabs={[
              { id: 'resumen', label: 'Resumen' },
              { id: 'productos', label: 'Productos', count: sale.lines.length },
            ]}
          >
            <TabPanel id="resumen">
              <DetailList
                items={[
                  { label: 'Cliente', value: sale.customer.name },
                  { label: 'NIT', value: sale.customer.nit },
                  { label: 'Sucursal', value: branchName(sale.branch) },
                  { label: 'Canal', value: sale.channel === 'Web' ? 'Tienda web' : 'Caja' },
                  { label: 'Artículos', value: formatQuantity(sale.items) },
                  { label: 'Total', value: formatMoney(sale.total) },
                  { label: 'Notas', value: sale.notes, wide: true },
                ]}
              />
            </TabPanel>
            <TabPanel id="productos">
              <ul className="divide-y divide-border text-sm">
                {sale.lines.map((line, index) => (
                  <li key={`${line.sku}-${index}`} className="flex justify-between gap-3 py-2">
                    <span className="min-w-0">
                      <span className="block text-text">{line.name}</span>
                      <span className="block text-xs text-text-muted">
                        {line.sku} · {line.quantity} × {formatMoney(line.price)}
                      </span>
                    </span>
                    <span className="shrink-0 font-semibold tabular-nums">{formatMoney(line.quantity * line.price)}</span>
                  </li>
                ))}
              </ul>
            </TabPanel>
          </Tabs>
        )}
      </SidePanel>

      <ConfirmDialog
        open={voiding?.open ?? false}
        onClose={() => setVoiding((current) => (current?.open ? { ...current, open: false } : current))}
        tone="danger"
        title={`¿Anular la venta ${voiding?.item.number ?? ''}?`}
        message="Se devuelve el stock y la factura queda anulada. La anulación queda en la bitácora con su motivo."
        confirmLabel="Anular venta"
        confirmDisabled={reason.trim().length < 5}
        onConfirm={confirmVoid}
      >
        <TextArea label="Motivo" hint="Al menos 5 caracteres." value={reason} onChange={setReason} maxLength={200} rows={3} required />
      </ConfirmDialog>
    </div>
  );
}
