// Módulo «Libros fiscales» · pestaña «Libro de compras» (`GetPurchasesBookQuery`): las facturas de los proveedores del mes
// con su crédito fiscal; los montos que no se usan a diario van en la fila desplegable. Totales al pie (de lo filtrado) y
// los del libro completo arriba. Descarga del libro (CSV del SIN o Excel) y «Exportar lo filtrado».

import { BookOpen, Download, RefreshCw } from 'lucide-react';
import { useMemo } from 'react';
import { useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import { Button, DataTable, DetailList, Toolbar, useNotify, type DataTableColumn } from '@/4-presentation/panel/kit';
import { exportCsv, formatDate, formatMoney, formatNumber } from '@/4-presentation/panel/lib';
import { PURCHASES_CSV, filterPurchases, monthLabel, shortCode, sum, type BookFilters, type MonthValue, type PurchasesBookRowData } from './books';
import { ExportButtons } from './ExportButtons';

const COLUMNS: DataTableColumn<PurchasesBookRowData>[] = [
  { id: 'fila', header: 'N°', value: (row) => row.row, align: 'end' },
  { id: 'fecha', header: 'Fecha', value: (row) => row.date, cell: (row) => formatDate(row.date), className: 'whitespace-nowrap' },
  {
    id: 'proveedor',
    header: 'Proveedor',
    value: (row) => row.supplierName,
    card: 'title',
    cell: (row) => (
      <span className="block min-w-40">
        {row.supplierName}
        <span className="block text-xs font-normal text-text-muted">NIT {row.supplierNit}</span>
      </span>
    ),
  },
  {
    id: 'factura',
    header: 'Factura',
    value: (row) => row.invoiceNumber,
    cell: (row) => (
      <span className="block whitespace-nowrap">
        N° {row.invoiceNumber}
        <span className="block font-mono text-xs text-text-muted">{shortCode(row.authorizationCode)}</span>
      </span>
    ),
  },
  { id: 'total', header: 'Importe total', align: 'end', value: (row) => row.total, cell: (row) => formatMoney(row.total), footer: (rows) => formatMoney(sum(rows, (row) => row.total)) },
  { id: 'base', header: 'Base crédito', align: 'end', value: (row) => row.taxBase, cell: (row) => formatMoney(row.taxBase), footer: (rows) => formatMoney(sum(rows, (row) => row.taxBase)) },
  { id: 'credito', header: 'Crédito fiscal', align: 'end', value: (row) => row.taxCredit, cell: (row) => formatMoney(row.taxCredit), footer: (rows) => formatMoney(sum(rows, (row) => row.taxCredit)) },
  { id: 'tipo', header: 'Tipo de compra', value: (row) => row.purchaseType },
  { id: 'sucursal', header: 'Sucursal', value: (row) => row.branchCode },
];

export interface PurchasesBookTabProps {
  month: MonthValue;
  filters: BookFilters;
}

export function PurchasesBookTab({ month, filters }: PurchasesBookTabProps) {
  const notify = useNotify();
  const table = useTableState({ prefix: 'c_', sort: { column: 'fila', direction: 'asc' } });
  const book = useRpcQuery('GetPurchasesBookQuery', { year: month.year, month: month.month });
  const rows = useMemo(() => filterPurchases(book.data?.rows ?? [], filters), [book.data, filters]);

  const exportRows = () => {
    const file = exportCsv(`libro-compras-filtrado-${month.year}-${String(month.month).padStart(2, '0')}`, PURCHASES_CSV, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const data = book.data;
  return (
    <div className="space-y-4">
      <Toolbar
        label="Acciones del libro de compras"
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={book.fetching && !book.loading} onClick={book.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar lo filtrado
            </Button>
            <ExportButtons month={month} purchases disabled={!data || data.rows.length === 0} />
          </>
        }
      >
        {data && (
          <span className="text-sm text-text-muted" data-testid="libro-compras-totales">
            {monthLabel(month)}: {formatNumber(data.rows.length)} facturas · total {formatMoney(data.total)} · base {formatMoney(data.taxBase)} · crédito fiscal {formatMoney(data.taxCredit)}
          </span>
        )}
      </Toolbar>
      <DataTable
        caption={`Libro de compras de ${monthLabel(month)}`}
        columns={COLUMNS}
        rows={data ? rows : undefined}
        rowKey={(row) => `${row.row}-${row.invoiceNumber}-${row.supplierNit}`}
        rowLabel={(row) => `la factura N° ${row.invoiceNumber} de ${row.supplierName}`}
        loading={book.loading}
        refreshing={book.fetching && !book.loading}
        error={book.error}
        onRetry={book.reload}
        operation="GetPurchasesBookQuery"
        {...table.tableProps}
        footerLabel="Totales de lo filtrado"
        renderExpanded={(row) => (
          <DetailList
            items={[
              { label: 'Código de autorización', value: <span className="font-mono text-xs break-all">{row.authorizationCode}</span>, wide: true },
              { label: 'Subtotal', value: formatMoney(row.subtotal) },
              { label: 'Descuentos', value: formatMoney(row.discounts) },
              { label: 'Gift card', value: formatMoney(row.giftCard) },
              { label: 'ICE', value: formatMoney(row.ice) },
              { label: 'IEHD', value: formatMoney(row.iehd) },
              { label: 'IPJ', value: formatMoney(row.ipj) },
              { label: 'Tasas', value: formatMoney(row.fees) },
              { label: 'Otros no sujetos a IVA', value: formatMoney(row.otherNotSubject) },
              { label: 'Exentas', value: formatMoney(row.exempt) },
              { label: 'Tasa cero', value: formatMoney(row.zeroRate) },
              { label: 'Código de control', value: row.controlCode },
            ]}
          />
        )}
        empty={{
          title: (data?.rows.length ?? 0) === 0 ? `No hay facturas de proveedores en ${monthLabel(month)}` : 'No hay facturas con estos filtros',
          description: 'Registre las facturas de las recepciones en la pestaña «Recepciones sin factura».',
          icon: <BookOpen />,
        }}
      />
    </div>
  );
}
