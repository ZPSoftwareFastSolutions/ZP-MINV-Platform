// Análisis › Reportes › Compras (`GetPurchasesReportQuery`, en el escritorio: ReportsView › Compras). Compras RECIBIDAS
// en el período (costo de la mercadería que entró), por proveedor o por día, con las órdenes abiertas de hoy; tabla con
// pie de totales, detalle lateral con enlaces a Órdenes de compra y Proveedores, exportar CSV e imprimir. Gráficos
// plegados detrás de «Ver gráfico».

import { ChartColumn, ExternalLink, Eye, Truck } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { usePermissions, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import { Alert, BarList, Button, Collapsible, DataTable, DetailList, FilterBar, MiniBars, SearchField, SelectField, SidePanel, useNotify } from '@/4-presentation/panel/kit';
import { exportCsv, formatMoney, formatNumber, formatPercent, roundTo } from '@/4-presentation/panel/lib';
import { inTableOrder, toCsvColumns, toTableColumns } from './columns';
import { PeriodFields } from './PeriodFields';
import { periodName, rangeText, resolvePeriod } from './period';
import { PrintSheet } from './PrintSheet';
import { ReportToolbar, ReportTotals } from './ReportParts';
import { PURCHASES_FILTERS, PURCHASES_GROUPINGS, purchasesColumns, purchasesGroupingOf, purchasesRowLinks, purchasesRows, purchasesSummary, scopeText, searchRows, seriesPoints, type RankedRow } from './reports';
import { usePrint } from './usePrint';
import { useRowMenuGuard } from './useRowMenuGuard';

export function PurchasesReportView() {
  const notify = useNotify();
  const navigate = useNavigate();
  const { session } = usePermissions();
  const table = useTableState({ filters: PURCHASES_FILTERS, sort: null });
  const filters = table.filters;
  const [now] = useState(() => new Date());
  const period = resolvePeriod(filters, now);
  const grouping = purchasesGroupingOf(filters.agrupar);

  const report = useRpcQuery('GetPurchasesReportQuery', { from: period.from, to: period.to }, { enabled: period.problem === null });
  const allRows = useMemo(() => (report.data ? purchasesRows(report.data, grouping) : []), [report.data, grouping]);
  const rows = useMemo(() => searchRows(allRows, filters.q), [allRows, filters.q]);
  const columns = useMemo(() => purchasesColumns(grouping), [grouping]);
  const tableColumns = useMemo(() => toTableColumns(columns), [columns]);

  const printer = usePrint();
  const menu = useRowMenuGuard();
  const [detail, setDetail] = useState<{ row: RankedRow; open: boolean } | null>(null);
  const openDetail = (row: RankedRow) => setDetail({ row, open: true });
  const closeDetail = () => setDetail((current) => (current ? { ...current, open: false } : current));

  const scope = scopeText(session?.access);
  const periodLine = `${periodName(period)}: ${rangeText(period)}`;
  const groupingName = grouping === 'dia' ? 'por día' : 'por proveedor';

  const exportRows = () => {
    const file = exportCsv(`reporte de compras ${groupingName} ${period.from} ${period.to}`, toCsvColumns(columns), inTableOrder(rows, columns, table.sort));
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };
  const printRows = () => {
    if (!report.data) return;
    const printed = printer.print(
      <PrintSheet
        company={session?.company ?? ''}
        title={`Reporte de compras ${groupingName}`}
        lines={[periodLine, scope, ...(filters.q ? [`Búsqueda: «${filters.q}»`] : [])]}
        summary={purchasesSummary(report.data)}
        columns={columns}
        rows={inTableOrder(rows, columns, table.sort)}
        rowKey={(row) => row.key}
        printedBy={session?.displayName ?? ''}
        printedAt={new Date()}
      />,
    );
    if (!printed) notify.error('No se pudo imprimir', 'Este navegador no permite imprimir desde la página.');
  };

  const row = detail?.row;
  const links = row ? purchasesRowLinks(row, grouping) : [];
  const data = report.data;

  return (
    <div className="space-y-5">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <PeriodFields period={period} onChange={(values) => table.setFilters(values)} />
        <SelectField label="Agrupar" allLabel={false} value={grouping} onChange={(value) => table.setFilter('agrupar', value || PURCHASES_FILTERS.agrupar)} options={PURCHASES_GROUPINGS} />
        <SearchField label="Buscar" placeholder="Proveedor o código" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
      </FilterBar>

      <ReportTotals description={`${periodLine} · ${scope}. Las órdenes abiertas son las de hoy (no dependen del período).`} items={data ? purchasesSummary(data) : null} testId="compras-totales" />

      <Collapsible label="Ver gráfico" openLabel="Ocultar gráfico" icon={<ChartColumn />} description="Compras recibidas por día y por proveedor.">
        {data ? (
          <div className="grid gap-6 lg:grid-cols-2" data-testid="compras-grafico">
            <div>
              <p className="mb-2 text-sm font-semibold text-text">Compras recibidas por día</p>
              <MiniBars label="Compras recibidas por día del período" points={seriesPoints(data.byDay)} format={(value) => formatMoney(value)} height={96} tone="primary" showAxis />
            </div>
            <BarList label="Proveedores con más compras" items={data.bySupplier.slice(0, 10).map((item) => ({ label: item.name, value: item.amount, hint: `${formatNumber(item.count)} recepciones` }))} format={(value) => formatMoney(value)} />
          </div>
        ) : (
          <p className="text-sm text-text-muted">Cargando el reporte…</p>
        )}
      </Collapsible>

      <ReportToolbar
        summary={data ? `${formatNumber(rows.length)} de ${formatNumber(allRows.length)} filas · ${groupingName}` : undefined}
        onReload={report.reload}
        reloading={report.fetching && !report.loading}
        hasRows={rows.length > 0}
        onExport={exportRows}
        onPrint={printRows}
      />

      {period.problem ? (
        <Alert tone="warning" title="Revise las fechas">
          {period.problem} El reporte se actualiza al corregirlas.
        </Alert>
      ) : (
        <DataTable
          caption={`Compras ${groupingName}`}
          columns={tableColumns}
          rows={data ? rows : undefined}
          rowKey={(item) => item.key}
          rowLabel={(item) => item.name}
          loading={report.loading}
          refreshing={report.fetching && !report.loading}
          error={report.error}
          onRetry={report.reload}
          operation="GetPurchasesReportQuery"
          {...table.tableProps}
          onRowOpen={(item) => {
            if (!menu.busy()) openDetail(item);
          }}
          activeRowKey={detail?.open ? detail.row.key : null}
          rowActions={(item) => [
            { label: 'Ver detalle', icon: <Eye />, onSelect: menu.guard(() => openDetail(item)) },
            ...purchasesRowLinks(item, grouping).map((link) => ({ label: link.label, icon: <ExternalLink />, onSelect: menu.guard(() => navigate(link.to)) })),
          ]}
          empty={{
            title: allRows.length === 0 ? 'No se recibieron compras en este período' : 'Ninguna fila coincide con la búsqueda',
            description: allRows.length === 0 ? 'Pruebe con otro período.' : 'Pruebe con otro texto o limpie los filtros.',
            icon: <Truck />,
            action: (
              <Button variant="outline" onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
          }}
        />
      )}

      <SidePanel
        open={detail?.open ?? false}
        onClose={closeDetail}
        title={row ? row.name : 'Detalle'}
        description={row ? `Compras ${groupingName} · ${rangeText(period)}` : undefined}
        footer={
          links.length > 0 && (
            <div className="flex flex-col gap-2">
              {links.map((link) => (
                <Button key={link.to} variant="outline" leftIcon={<ExternalLink />} fullWidth to={link.to}>
                  {link.label}
                </Button>
              ))}
            </div>
          )
        }
      >
        {row && (
          <div data-testid="compras-detalle">
            <DetailList
              items={[
                { label: 'Puesto', value: `${row.rank} de ${allRows.length}` },
                { label: 'Código', value: row.day ? null : row.code },
                { label: 'Compras recibidas', value: formatMoney(row.amount) },
                { label: 'Participación', value: formatPercent(row.share * 100, 1) },
                { label: 'Recepciones', value: row.count === null ? null : formatNumber(row.count) },
                { label: 'Promedio por recepción', value: row.count ? formatMoney(roundTo(row.amount / row.count, 2)) : null },
              ]}
            />
          </div>
        )}
      </SidePanel>

      {printer.area}
    </div>
  );
}
