// Módulo «Sucursales» · el comparativo por sucursal (`GetBranchReportQuery`, del MODELO DE LECTURA: no carga la base de
// las cajas; regla B-14) en un rango de fechas: tickets, ventas, IVA, ticket promedio, participación y stock valorizado
// de cada sucursal visible, más lo que está en tránsito y DE CUÁNDO son los datos. Vive dentro de un «Ver …» plegado
// (regla P-10): se monta y consulta recién al abrirlo. Exporta CSV.

import { Download, Truck } from 'lucide-react';
import { useState } from 'react';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { BarList, Button, DataTable, DateRangeField, ErrorState, StatCard, useNotify, type DataTableColumn } from '@/4-presentation/panel/kit';
import { exportCsv, formatDateTime, formatMoney, formatNumber, laPazToday, rangeError, type DateRange } from '@/4-presentation/panel/lib';
import { IN_TRANSIT_LINK, REPORT_CSV, defaultReportRange, reportRequest, type BranchReportLine } from './branches';

const COLUMNS: DataTableColumn<BranchReportLine>[] = [
  {
    id: 'sucursal',
    header: 'Sucursal',
    value: (row) => row.name,
    card: 'title',
    cell: (row) => (
      <span className="block min-w-40">
        <span className="block">{row.name}</span>
        <span className="block text-xs text-text-muted">{row.code}</span>
      </span>
    ),
  },
  { id: 'tickets', header: 'Tickets', align: 'end', value: (row) => row.tickets, footer: (rows) => formatNumber(rows.reduce((sum, row) => sum + row.tickets, 0)) },
  { id: 'ventas', header: 'Ventas', align: 'end', value: (row) => row.revenue, cell: (row) => formatMoney(row.revenue), footer: (rows) => formatMoney(rows.reduce((sum, row) => sum + row.revenue, 0)) },
  { id: 'iva', header: 'IVA', align: 'end', value: (row) => row.tax, cell: (row) => formatMoney(row.tax) },
  { id: 'promedio', header: 'Ticket promedio', align: 'end', value: (row) => row.averageTicket, cell: (row) => formatMoney(row.averageTicket) },
  { id: 'participacion', header: 'Participación', align: 'end', value: (row) => row.sharePercent, cell: (row) => `${formatNumber(row.sharePercent, { maxDecimals: 1 })} %` },
  { id: 'stock', header: 'Stock valorizado', align: 'end', value: (row) => row.stockValue, cell: (row) => formatMoney(row.stockValue) },
];

export function BranchReport() {
  const notify = useNotify();
  const [today] = useState(() => laPazToday());
  const [range, setRange] = useState<DateRange>(() => defaultReportRange(today));
  const request = rangeError(range) ? null : reportRequest(range, today);
  const report = useRpcQuery('GetBranchReportQuery', request ?? { from: today, to: today }, { enabled: request !== null });

  const exportRows = () => {
    if (!report.data) return;
    const file = exportCsv(`comparativo-sucursales-${report.data.from}-${report.data.to}`, REPORT_CSV, report.data.branches);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(report.data.branches.length)} filas).`);
  };

  const data = report.data;
  return (
    <div className="space-y-4" data-testid="comparativo-sucursales">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-end lg:justify-between">
        <DateRangeField label="Período de ventas" value={range} onChange={setRange} shortcuts={['ultimos7', 'esteMes', 'mesAnterior']} className="lg:max-w-xl" />
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" leftIcon={<Truck />} to={IN_TRANSIT_LINK}>
            Ver lo que está en tránsito
          </Button>
          <Button variant="outline" leftIcon={<Download />} disabled={!data || data.branches.length === 0} onClick={exportRows}>
            Exportar CSV
          </Button>
        </div>
      </div>
      {report.error ? (
        <ErrorState error={report.error} operation="GetBranchReportQuery" title="No se pudo cargar el comparativo" onRetry={report.reload} retrying={report.fetching} />
      ) : (
        <>
          <div className="grid grid-cols-[repeat(auto-fit,minmax(11rem,1fr))] gap-3">
            <StatCard label="Ventas del período" value={formatMoney(data?.totalRevenue ?? 0)} loading={!data} tone="success" />
            <StatCard label="Stock valorizado" value={formatMoney(data?.totalStockValue ?? 0)} hint="Al costo promedio de cada almacén" loading={!data} />
            <StatCard label="En tránsito" value={formatMoney(data?.inTransitValue ?? 0)} hint="Despachado y aún no recibido" loading={!data} />
          </div>
          <p className="text-sm text-text-muted" data-testid="comparativo-frescura">
            {data ? (data.refreshedAt ? `Datos del modelo de lectura actualizados el ${formatDateTime(data.refreshedAt)} (se refrescan cada pocos minutos).` : 'Datos en vivo.') : 'Cargando…'}
          </p>
          {data && data.branches.length > 0 && <BarList label="Ventas por sucursal" items={data.branches.map((row) => ({ label: `${row.code} · ${row.name}`, value: row.revenue }))} format={formatMoney} tone="accent" />}
          <DataTable
            caption="Comparativo por sucursal"
            columns={COLUMNS}
            rows={data?.branches}
            rowKey={(row) => row.code}
            loading={report.loading}
            refreshing={report.fetching && !report.loading}
            paginate={false}
            empty={{ title: 'No hay sucursales visibles en el comparativo' }}
          />
        </>
      )}
    </div>
  );
}
