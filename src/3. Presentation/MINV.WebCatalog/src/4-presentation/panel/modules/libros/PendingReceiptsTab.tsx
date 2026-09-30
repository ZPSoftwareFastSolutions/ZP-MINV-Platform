// Módulo «Libros fiscales» · pestaña «Recepciones sin factura» (`GetReceiptsWithoutInvoiceQuery`, permiso «gestionar
// compras»): la mercadería recibida cuya factura del proveedor todavía no se registró (no entra al libro de compras ni da
// crédito fiscal). «Registrar factura» en cada una (`RegisterSupplierInvoiceCommand`) y «Exportar CSV».

import { Download, FileCheck, PackageCheck, RefreshCw } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import { Button, DataTable, Toolbar, useNotify, type DataTableColumn } from '@/4-presentation/panel/kit';
import { exportCsv, formatDate, formatMoney, formatNumber, matchesSearch } from '@/4-presentation/panel/lib';
import { PENDING_CSV, sum, type BookFilters, type PendingReceiptData } from './books';
import { SupplierInvoiceDialog } from './SupplierInvoiceDialog';

const COLUMNS: DataTableColumn<PendingReceiptData>[] = [
  { id: 'recepcion', header: 'Recepción', value: (row) => row.receiptNumber, card: 'title', className: 'whitespace-nowrap' },
  { id: 'fecha', header: 'Recibida', value: (row) => row.receivedOn, cell: (row) => formatDate(row.receivedOn), className: 'whitespace-nowrap' },
  {
    id: 'proveedor',
    header: 'Proveedor',
    value: (row) => row.supplier,
    cell: (row) => (
      <span className="block min-w-40">
        {row.supplier}
        <span className="block text-xs text-text-muted">{row.supplierCode}</span>
      </span>
    ),
  },
  {
    id: 'total',
    header: 'Total al costo neto',
    align: 'end',
    value: (row) => row.total,
    cell: (row) => formatMoney(row.total),
    footer: (rows) => formatMoney(sum(rows, (row) => row.total)),
  },
];

export interface PendingReceiptsTabProps {
  filters: BookFilters;
  /** Se registró una factura (el libro de compras cambió). */
  onRegistered?: () => void;
}

export function PendingReceiptsTab({ filters, onRegistered }: PendingReceiptsTabProps) {
  const notify = useNotify();
  const table = useTableState({ prefix: 'r_', sort: { column: 'fecha', direction: 'asc' } });
  const pending = useRpcQuery('GetReceiptsWithoutInvoiceQuery', {});
  const rows = useMemo(() => (pending.data ?? []).filter((row) => matchesSearch(filters.q, [row.receiptNumber, row.supplier, row.supplierCode])), [pending.data, filters.q]);
  const [session, setSession] = useState(0);
  const [registering, setRegistering] = useState<PendingReceiptData | null>(null);
  const register = (row: PendingReceiptData) => {
    setSession((count) => count + 1);
    setRegistering(row);
  };

  return (
    <div className="space-y-4">
      <Toolbar
        label="Acciones de las recepciones"
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={pending.fetching && !pending.loading} onClick={pending.reload}>
              Actualizar
            </Button>
            <Button
              variant="outline"
              leftIcon={<Download />}
              disabled={rows.length === 0}
              onClick={() => {
                const file = exportCsv('recepciones-sin-factura', PENDING_CSV, rows);
                notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
              }}
            >
              Exportar CSV
            </Button>
          </>
        }
      >
        {pending.data && (
          <span className="text-sm text-text-muted" data-testid="recepciones-resumen">
            {formatNumber(rows.length)} recepciones sin la factura del proveedor (de todos los meses)
          </span>
        )}
      </Toolbar>
      <DataTable
        caption="Recepciones sin factura del proveedor"
        columns={COLUMNS}
        rows={pending.data ? rows : undefined}
        rowKey={(row) => row.receiptNumber}
        rowLabel={(row) => `la recepción ${row.receiptNumber}`}
        loading={pending.loading}
        refreshing={pending.fetching && !pending.loading}
        error={pending.error}
        onRetry={pending.reload}
        operation="GetReceiptsWithoutInvoiceQuery"
        {...table.tableProps}
        rowActions={(row) => [{ label: 'Registrar factura', icon: <FileCheck />, onSelect: () => register(row) }]}
        empty={{ title: 'Todas las recepciones tienen su factura', description: 'El libro de compras está completo.', icon: <PackageCheck /> }}
      />
      <SupplierInvoiceDialog
        key={`factura-${session}`}
        target={registering}
        onClose={() => setRegistering(null)}
        onDone={() => {
          pending.reload();
          onRegistered?.();
        }}
      />
    </div>
  );
}
