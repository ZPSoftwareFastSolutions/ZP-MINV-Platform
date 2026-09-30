// Módulo «Órdenes de compra» · pestaña «Facturas de proveedores» (el libro de compras del escritorio, del lado de las
// compras): las recepciones contabilizadas que todavía no tienen factura (`GetReceiptsWithoutInvoiceQuery`) con
// «Registrar factura» (`RegisterSupplierInvoiceCommand`), y las facturas registradas en un rango de fechas
// (`GetSupplierInvoicesQuery`: el servidor exige las dos fechas; por defecto, el mes en curso). Filtros en la dirección
// con el prefijo `f_` (`?pestana=facturas&f_q=RC-CM-000012` desde el detalle de una orden). Las dos listas exportan CSV.

import { Download, FileCheck2, FileClock, FileText, RefreshCw } from 'lucide-react';
import { useMemo, useState } from 'react';
import { usePermissions, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Button,
  DataTable,
  DateRangeField,
  FilterBar,
  SearchField,
  Section,
  SelectField,
  StatusBadge,
  Toolbar,
  useNotify,
  type DataTableColumn,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatDate, formatMoney, formatNumber, laPazToday, toDate } from '@/4-presentation/panel/lib';
import {
  INVOICES_CSV,
  PENDING_RECEIPTS_CSV,
  filterInvoices,
  filterPendingReceipts,
  invoiceFilters,
  invoiceRange,
  invoiceSupplierOptions,
  type InvoiceRecord,
  type PendingReceiptRecord,
} from './purchasing';
import { useRowMenuGuard } from './rowGuard';
import { SupplierInvoiceDialog } from './SupplierInvoiceDialog';

const PENDING_COLUMNS: DataTableColumn<PendingReceiptRecord>[] = [
  { id: 'recepcion', header: 'Recepción', value: (row) => row.receiptNumber, card: 'title', className: 'whitespace-nowrap font-mono' },
  { id: 'fecha', header: 'Recibida el', value: (row) => toDate(row.receivedOn), cell: (row) => formatDate(row.receivedOn), className: 'whitespace-nowrap' },
  {
    id: 'proveedor',
    header: 'Proveedor',
    value: (row) => row.supplier,
    cell: (row) => (
      <span className="block min-w-40">
        <span className="block">{row.supplier}</span>
        <span className="block text-xs text-text-muted">{row.supplierCode}</span>
      </span>
    ),
  },
  {
    id: 'total',
    header: 'Total recibido',
    align: 'end',
    value: (row) => row.total,
    cell: (row) => formatMoney(row.total),
    footer: (rows) => formatMoney(rows.reduce((sum, row) => sum + row.total, 0)),
  },
];

const INVOICE_COLUMNS: DataTableColumn<InvoiceRecord>[] = [
  { id: 'fecha', header: 'Fecha', value: (row) => toDate(row.invoiceDate), cell: (row) => formatDate(row.invoiceDate), className: 'whitespace-nowrap' },
  { id: 'numero', header: 'Factura', value: (row) => row.number, card: 'title', className: 'whitespace-nowrap' },
  {
    id: 'proveedor',
    header: 'Proveedor',
    value: (row) => row.supplier,
    cell: (row) => (
      <span className="block min-w-40">
        <span className="block">{row.supplier}</span>
        <span className="block text-xs text-text-muted">{row.supplierNit ? `NIT ${row.supplierNit}` : row.supplierCode}</span>
      </span>
    ),
  },
  { id: 'recepcion', header: 'Recepción', value: (row) => row.receiptNumber, className: 'whitespace-nowrap font-mono' },
  {
    id: 'autorizacion',
    header: 'CUF o autorización',
    value: (row) => row.authorizationCode,
    sortable: false,
    cell: (row) => <span className="block max-w-48 truncate font-mono text-xs" title={row.authorizationCode}>{row.authorizationCode || '—'}</span>,
  },
  {
    id: 'importe',
    header: 'Importe',
    align: 'end',
    value: (row) => row.totalAmount,
    cell: (row) => formatMoney(row.totalAmount),
    footer: (rows) => formatMoney(rows.reduce((sum, row) => sum + row.totalAmount, 0)),
  },
  {
    id: 'credito',
    header: 'Crédito fiscal',
    align: 'end',
    value: (row) => row.taxCredit,
    cell: (row) => formatMoney(row.taxCredit),
    footer: (rows) => formatMoney(rows.reduce((sum, row) => sum + row.taxCredit, 0)),
  },
  { id: 'estado', header: 'Estado', value: (row) => row.status, cell: (row) => <StatusBadge tone={row.status === 'Anulada' ? 'danger' : 'success'}>{row.status}</StatusBadge> },
];

export function InvoicesTab() {
  const notify = useNotify();
  const { canRun } = usePermissions();
  const [today] = useState(() => laPazToday());
  const defaults = useMemo(() => invoiceFilters(today), [today]);
  const table = useTableState({ prefix: 'f_', filters: defaults, sort: { column: 'fecha', direction: 'desc' } });
  const pendingTable = useTableState({ prefix: 'r_', sort: { column: 'fecha', direction: 'desc' } });
  const filters = table.filters;
  const range = invoiceRange(filters, today);

  const pending = useRpcQuery('GetReceiptsWithoutInvoiceQuery', {});
  const invoices = useRpcQuery('GetSupplierInvoicesQuery', range);
  const pendingRows = useMemo(() => filterPendingReceipts(pending.data ?? [], filters), [pending.data, filters]);
  const invoiceRows = useMemo(() => filterInvoices(invoices.data ?? [], filters), [invoices.data, filters]);
  const suppliers = useMemo(() => invoiceSupplierOptions(pending.data ?? [], invoices.data ?? []), [pending.data, invoices.data]);
  const canRegister = canRun('RegisterSupplierInvoiceCommand');
  const { fromMenu, guardOpen } = useRowMenuGuard();

  const [dialogSession, setDialogSession] = useState(0);
  const [registering, setRegistering] = useState<PendingReceiptRecord | null>(null);
  const askRegister = (row: PendingReceiptRecord) => {
    setDialogSession((count) => count + 1);
    setRegistering(row);
  };

  const reload = () => {
    pending.reload();
    invoices.reload();
  };

  const exportPending = () => {
    const file = exportCsv('recepciones-sin-factura', PENDING_RECEIPTS_CSV, pendingRows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(pendingRows.length)} filas).`);
  };
  const exportInvoices = () => {
    const file = exportCsv('facturas-de-proveedores', INVOICES_CSV, invoiceRows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(invoiceRows.length)} filas).`);
  };

  return (
    <div className="space-y-5">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Factura, recepción, proveedor, NIT o CUF" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Proveedor" allLabel="Todos los proveedores" value={filters.proveedor} onChange={(value) => table.setFilter('proveedor', value)} options={suppliers} />
        <DateRangeField label="Fechas de las facturas" value={table.dateRange()} onChange={(value) => table.setDateRange(value)} hint="Las recepciones sin factura se ven todas." />
      </FilterBar>

      <Section
        title="Recepciones sin factura"
        description="Mercadería recibida cuya factura del proveedor todavía no se registró (para completar el libro de compras)."
        actions={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={(pending.fetching || invoices.fetching) && !pending.loading} onClick={reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={pendingRows.length === 0} onClick={exportPending}>
              Exportar CSV
            </Button>
          </>
        }
      >
        <DataTable
          caption="Recepciones sin factura"
          columns={PENDING_COLUMNS}
          rows={pending.data ? pendingRows : undefined}
          rowKey={(row) => row.receiptNumber}
          rowLabel={(row) => `la recepción ${row.receiptNumber} de ${row.supplier}`}
          loading={pending.loading}
          refreshing={pending.fetching && !pending.loading}
          error={pending.error}
          onRetry={pending.reload}
          operation="GetReceiptsWithoutInvoiceQuery"
          {...pendingTable.tableProps}
          onRowOpen={canRegister ? guardOpen(askRegister) : undefined}
          rowActions={(row) => [{ label: 'Registrar factura', icon: <FileCheck2 />, onSelect: fromMenu(() => askRegister(row)), hidden: !canRegister }]}
          empty={{
            title: (pending.data ?? []).length === 0 ? 'Todas las recepciones tienen su factura' : 'Ninguna recepción coincide con los filtros',
            icon: <FileClock />,
          }}
        />
      </Section>

      <Section title="Facturas registradas" description="Facturas de proveedores en el libro de compras, con su crédito fiscal.">
        <Toolbar
          label="Acciones de las facturas"
          end={
            <Button variant="outline" leftIcon={<Download />} disabled={invoiceRows.length === 0} onClick={exportInvoices}>
              Exportar CSV
            </Button>
          }
        >
          {invoices.data && (
            <span className="text-sm text-text-muted" data-testid="facturas-resumen">
              {formatNumber(invoiceRows.length)} facturas del {formatDate(range.from)} al {formatDate(range.to)}
            </span>
          )}
        </Toolbar>
        <DataTable
          className="mt-3"
          caption="Facturas de proveedores registradas"
          columns={INVOICE_COLUMNS}
          rows={invoices.data ? invoiceRows : undefined}
          rowKey={(row) => row.id}
          rowLabel={(row) => `la factura ${row.number} de ${row.supplier}`}
          loading={invoices.loading}
          refreshing={invoices.fetching && !invoices.loading}
          error={invoices.error}
          onRetry={invoices.reload}
          operation="GetSupplierInvoicesQuery"
          {...table.tableProps}
          empty={{
            title: 'No hay facturas en estas fechas',
            description: 'Pruebe con otro rango o limpie los filtros.',
            icon: <FileText />,
            action: (
              <Button variant="outline" onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
          }}
        />
      </Section>

      <SupplierInvoiceDialog key={`factura-${dialogSession}`} target={registering} onClose={() => setRegistering(null)} onRegistered={reload} />
    </div>
  );
}
