// Módulo «Libros fiscales» · pestaña «Libro de ventas» (`GetSalesBookQuery`): las facturas y notas del SIN del mes con sus
// importes, estado y sucursal; los montos que el SIN no usa a diario (ICE, IEHD, tasas, exportaciones…) van en la fila
// desplegable. Totales al pie (de lo filtrado) y los del libro completo arriba. Descarga del libro (CSV del SIN o Excel)
// y «Exportar lo filtrado».

import { BookOpen, Download, RefreshCw } from 'lucide-react';
import { useMemo } from 'react';
import { useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import { Button, DataTable, DetailList, StatusBadge, Toolbar, useNotify, type DataTableColumn } from '@/4-presentation/panel/kit';
import { exportCsv, formatMoney, formatNumber } from '@/4-presentation/panel/lib';
import {
  SALES_CSV,
  SALES_STATUSES,
  buyerDocumentText,
  filterSales,
  fiscalDate,
  kindText,
  monthLabel,
  shortCode,
  sum,
  type BookFilters,
  type MonthValue,
  type SalesBookRowData,
} from './books';
import { ExportButtons } from './ExportButtons';

const COLUMNS: DataTableColumn<SalesBookRowData>[] = [
  { id: 'fila', header: 'N°', value: (row) => row.row, align: 'end' },
  { id: 'fecha', header: 'Fecha', value: (row) => row.date, cell: (row) => fiscalDate(row.date), className: 'whitespace-nowrap' },
  {
    id: 'documento',
    header: 'Documento',
    value: (row) => row.number,
    card: 'title',
    cell: (row) => (
      <span className="block whitespace-nowrap">
        {kindText(row.documentSector)} N° {row.number}
        <span className="block font-mono text-xs font-normal text-text-muted">{shortCode(row.cuf)}</span>
      </span>
    ),
  },
  {
    id: 'comprador',
    header: 'Comprador',
    value: (row) => row.buyerName,
    cell: (row) => (
      <span className="block min-w-40">
        {row.buyerName}
        <span className="block text-xs text-text-muted">{buyerDocumentText(row)}</span>
      </span>
    ),
  },
  { id: 'total', header: 'Importe total', align: 'end', value: (row) => row.total, cell: (row) => formatMoney(row.total), footer: (rows) => formatMoney(sum(rows, (row) => row.total)) },
  { id: 'base', header: 'Base débito', align: 'end', value: (row) => row.taxBase, cell: (row) => formatMoney(row.taxBase), footer: (rows) => formatMoney(sum(rows, (row) => row.taxBase)) },
  { id: 'debito', header: 'Débito fiscal', align: 'end', value: (row) => row.taxDebit, cell: (row) => formatMoney(row.taxDebit), footer: (rows) => formatMoney(sum(rows, (row) => row.taxDebit)) },
  { id: 'estado', header: 'Estado', value: (row) => row.status, cell: (row) => <StatusBadge status={row.status} statuses={SALES_STATUSES} /> },
  { id: 'sucursal', header: 'Sucursal', value: (row) => row.branchCode },
];

export interface SalesBookTabProps {
  month: MonthValue;
  filters: BookFilters;
}

export function SalesBookTab({ month, filters }: SalesBookTabProps) {
  const notify = useNotify();
  const table = useTableState({ prefix: 'v_', sort: { column: 'fila', direction: 'asc' } });
  const book = useRpcQuery('GetSalesBookQuery', { year: month.year, month: month.month });
  const rows = useMemo(() => filterSales(book.data?.rows ?? [], filters), [book.data, filters]);

  const exportRows = () => {
    const file = exportCsv(`libro-ventas-filtrado-${month.year}-${String(month.month).padStart(2, '0')}`, SALES_CSV, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const data = book.data;
  return (
    <div className="space-y-4">
      <Toolbar
        label="Acciones del libro de ventas"
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={book.fetching && !book.loading} onClick={book.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar lo filtrado
            </Button>
            <ExportButtons month={month} purchases={false} disabled={!data || data.rows.length === 0} />
          </>
        }
      >
        {data && (
          <span className="text-sm text-text-muted" data-testid="libro-ventas-totales">
            {monthLabel(month)}: {formatNumber(data.rows.length)} registros · {formatNumber(data.valid)} válidas · {formatNumber(data.voided)} anuladas · total {formatMoney(data.total)} · base{' '}
            {formatMoney(data.taxBase)} · débito fiscal {formatMoney(data.taxDebit)}
          </span>
        )}
      </Toolbar>
      <DataTable
        caption={`Libro de ventas de ${monthLabel(month)}`}
        columns={COLUMNS}
        rows={data ? rows : undefined}
        rowKey={(row) => `${row.row}-${row.cuf}`}
        rowLabel={(row) => `el registro ${row.row} (${kindText(row.documentSector).toLocaleLowerCase('es')} N° ${row.number})`}
        loading={book.loading}
        refreshing={book.fetching && !book.loading}
        error={book.error}
        onRetry={book.reload}
        operation="GetSalesBookQuery"
        {...table.tableProps}
        footerLabel="Totales de lo filtrado"
        renderExpanded={(row) => (
          <DetailList
            items={[
              { label: 'CUF', value: <span className="font-mono text-xs break-all">{row.cuf}</span>, wide: true },
              { label: 'Subtotal', value: formatMoney(row.subtotal) },
              { label: 'Descuentos', value: formatMoney(row.discounts) },
              { label: 'Gift card', value: formatMoney(row.giftCard) },
              { label: 'ICE', value: formatMoney(row.ice) },
              { label: 'IEHD', value: formatMoney(row.iehd) },
              { label: 'IPJ', value: formatMoney(row.ipj) },
              { label: 'Tasas', value: formatMoney(row.fees) },
              { label: 'Otros no sujetos a IVA', value: formatMoney(row.otherNotSubject) },
              { label: 'Exportaciones y exentas', value: formatMoney(row.exports) },
              { label: 'Tasa cero', value: formatMoney(row.zeroRate) },
              { label: 'Código de control', value: row.controlCode },
            ]}
          />
        )}
        empty={{
          title: (data?.rows.length ?? 0) === 0 ? `No hay facturas ni notas en ${monthLabel(month)}` : 'No hay registros con estos filtros',
          description: 'Elija otro mes o limpie los filtros.',
          icon: <BookOpen />,
        }}
      />
    </div>
  );
}
