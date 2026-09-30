// Análisis › Reportes › Movimientos (`GetMovementsReportQuery`, en el escritorio: ReportsView › Movimientos). Todos los
// movimientos del período (entradas, salidas y ajustes) para revisar o exportar: el tipo lo filtra el SERVIDOR (la lista
// sale de `GetMovementTypesQuery` si el rol puede verla); entradas o salidas, usuario y búsqueda se filtran en la página.
// Tabla ordenable, detalle lateral con las observaciones, exportar CSV e imprimir; gráfico plegado.

import { ArrowLeftRight, ChartColumn, Eye, ListFilter, PackageSearch } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import { Alert, BarList, Button, Collapsible, DataTable, DetailList, FilterBar, SearchField, SelectField, SidePanel, StatusBadge, useNotify } from '@/4-presentation/panel/kit';
import { exportCsv, formatDate, formatDateLong, formatNumber, formatTime } from '@/4-presentation/panel/lib';
import { inTableOrder, toCsvColumns, toTableColumns } from './columns';
import { PeriodFields } from './PeriodFields';
import { periodName, rangeText, resolvePeriod } from './period';
import { PrintSheet } from './PrintSheet';
import { ReportToolbar, ReportTotals } from './ReportParts';
import {
  FLOW_OPTIONS,
  MOVEMENTS_FILTERS,
  MOVEMENTS_LIMIT,
  MOVEMENT_COLUMNS,
  filterMovements,
  movementTypeOptions,
  movementUserOptions,
  movementsSummary,
  scopeText,
  signedText,
  toMovementEntries,
  type MovementEntry,
} from './reports';
import { usePrint } from './usePrint';
import { useRowMenuGuard } from './useRowMenuGuard';

const TABLE_COLUMNS = toTableColumns(MOVEMENT_COLUMNS);
const CSV_COLUMNS = toCsvColumns(MOVEMENT_COLUMNS);

export function MovementsReportView() {
  const notify = useNotify();
  const navigate = useNavigate();
  const { session, canRun } = usePermissions();
  const table = useTableState({ filters: MOVEMENTS_FILTERS, sort: { column: 'fecha', direction: 'desc' } });
  const filters = table.filters;
  const [now] = useState(() => new Date());
  const period = resolvePeriod(filters, now);

  const canTypes = canRun('GetMovementTypesQuery');
  const types = useRpcQuery('GetMovementTypesQuery', {}, { enabled: canTypes });
  const report = useRpcQuery('GetMovementsReportQuery', { from: period.from, to: period.to, typeCode: filters.tipo || null }, { enabled: period.problem === null });
  const entries = useMemo(() => toMovementEntries(report.data ?? []), [report.data]);
  const rows = useMemo(() => filterMovements(entries, filters), [entries, filters]);
  const users = useMemo(() => movementUserOptions(entries), [entries]);
  const typeOptions = useMemo(() => movementTypeOptions(canTypes ? types.data : undefined, entries), [canTypes, types.data, entries]);

  const printer = usePrint();
  const menu = useRowMenuGuard();
  const [detail, setDetail] = useState<{ entry: MovementEntry; open: boolean } | null>(null);
  const openDetail = (entry: MovementEntry) => setDetail({ entry, open: true });
  const closeDetail = () => setDetail((current) => (current ? { ...current, open: false } : current));
  const onlyProduct = (sku: string) => {
    table.setFilter('q', sku);
    closeDetail();
  };

  const scope = scopeText(session?.access);
  const periodLine = `${periodName(period)}: ${rangeText(period)}`;
  const typeName = typeOptions.find((option) => option.value === filters.tipo)?.label ?? filters.tipo;
  const filterLines = [
    ...(filters.tipo ? [`Tipo: ${typeName}`] : []),
    ...(filters.flujo ? [FLOW_OPTIONS.find((option) => option.value === filters.flujo)?.label ?? ''] : []),
    ...(filters.usuario ? [`Usuario: ${filters.usuario}`] : []),
    ...(filters.q ? [`Búsqueda: «${filters.q}»`] : []),
  ];

  const exportRows = () => {
    const file = exportCsv(`reporte de movimientos ${period.from} ${period.to}`, CSV_COLUMNS, inTableOrder(rows, MOVEMENT_COLUMNS, table.sort));
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };
  const printRows = () => {
    const printed = printer.print(
      <PrintSheet
        company={session?.company ?? ''}
        title="Reporte de movimientos"
        lines={[periodLine, scope, ...filterLines]}
        summary={movementsSummary(rows)}
        columns={MOVEMENT_COLUMNS.filter((column) => column.id !== 'notas' && column.id !== 'unidad' && column.id !== 'sku')}
        rows={inTableOrder(rows, MOVEMENT_COLUMNS, table.sort)}
        rowKey={(entry) => entry.key}
        printedBy={session?.displayName ?? ''}
        printedAt={new Date()}
      />,
    );
    if (!printed) notify.error('No se pudo imprimir', 'Este navegador no permite imprimir desde la página.');
  };

  const entry = detail?.entry;
  const capped = (report.data?.length ?? 0) >= MOVEMENTS_LIMIT;
  const byType = useMemo(() => {
    const counts = new Map<string, number>();
    for (const item of rows) counts.set(item.record.typeName, (counts.get(item.record.typeName) ?? 0) + 1);
    return [...counts.entries()].map(([label, value]) => ({ label, value })).sort((a, b) => b.value - a.value);
  }, [rows]);

  return (
    <div className="space-y-5">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <PeriodFields period={period} onChange={(values) => table.setFilters(values)} />
        <SelectField label="Tipo" allLabel="Todos los tipos" value={filters.tipo} onChange={(value) => table.setFilter('tipo', value)} options={typeOptions} />
        <SelectField label="Entradas o salidas" allLabel="Entradas y salidas" value={filters.flujo} onChange={(value) => table.setFilter('flujo', value)} options={FLOW_OPTIONS} />
        <SelectField label="Usuario" allLabel="Todos los usuarios" value={filters.usuario} onChange={(value) => table.setFilter('usuario', value)} options={users} />
        <SearchField label="Buscar" placeholder="Producto, SKU, documento, posición u observación" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
      </FilterBar>

      <ReportTotals description={`${periodLine} · ${scope}. Cuentan los movimientos con los filtros aplicados.`} items={report.data ? movementsSummary(rows) : null} testId="movimientos-totales" />

      <Collapsible label="Ver gráfico" openLabel="Ocultar gráfico" icon={<ChartColumn />} description="Cuántos movimientos hubo de cada tipo.">
        {report.data ? <BarList label="Movimientos por tipo" items={byType} tone="accent" /> : <p className="text-sm text-text-muted">Cargando el reporte…</p>}
      </Collapsible>

      <ReportToolbar
        summary={
          report.data
            ? `${formatNumber(rows.length)} de ${formatNumber(entries.length)} movimientos${capped ? ` · el servidor devuelve como máximo ${formatNumber(MOVEMENTS_LIMIT)}: acorte el período` : ''}`
            : undefined
        }
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
          caption="Movimientos del período"
          columns={TABLE_COLUMNS}
          rows={report.data ? rows : undefined}
          rowKey={(item) => item.key}
          rowLabel={(item) => `el movimiento ${item.record.typeName} de ${item.record.name}`}
          loading={report.loading}
          refreshing={report.fetching && !report.loading}
          error={report.error}
          onRetry={report.reload}
          operation="GetMovementsReportQuery"
          {...table.tableProps}
          onRowOpen={(item) => {
            if (!menu.busy()) openDetail(item);
          }}
          activeRowKey={detail?.open ? detail.entry.key : null}
          rowActions={(item) => [
            { label: 'Ver detalle', icon: <Eye />, onSelect: menu.guard(() => openDetail(item)) },
            { label: 'Ver solo este producto', icon: <ListFilter />, onSelect: menu.guard(() => onlyProduct(item.record.sku)), hidden: filters.q === item.record.sku },
            { label: 'Ver solo este tipo', icon: <ArrowLeftRight />, onSelect: menu.guard(() => table.setFilter('tipo', item.record.typeCode)), hidden: filters.tipo === item.record.typeCode },
            { label: 'Ver ficha y kardex del producto', icon: <PackageSearch />, onSelect: menu.guard(() => navigate(ROUTES.panelModule(`stock?ficha=${encodeURIComponent(item.record.sku)}`))) },
          ]}
          empty={{
            title: entries.length === 0 ? 'No hay movimientos en este período' : 'Ningún movimiento coincide con los filtros',
            description: entries.length === 0 ? 'Pruebe con otro período o con otro tipo.' : 'Pruebe con otros filtros o límpielos.',
            icon: <ArrowLeftRight />,
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
        title={entry ? entry.record.typeName : 'Movimiento'}
        description={entry ? `${entry.record.name} · ${formatDate(entry.record.recordedAt)} ${formatTime(entry.record.recordedAt)}` : undefined}
        headerExtra={entry && <StatusBadge tone={entry.isIn ? 'success' : 'warning'}>{entry.isIn ? 'Entrada' : 'Salida'}</StatusBadge>}
        footer={
          entry && (
            <div className="flex flex-col gap-2">
              <Button variant="outline" leftIcon={<PackageSearch />} fullWidth to={ROUTES.panelModule(`stock?ficha=${encodeURIComponent(entry.record.sku)}`)}>
                Ver ficha y kardex del producto
              </Button>
              {filters.q !== entry.record.sku && (
                <Button variant="ghost" leftIcon={<ListFilter />} fullWidth onClick={() => onlyProduct(entry.record.sku)}>
                  Ver solo los movimientos de este producto
                </Button>
              )}
            </div>
          )
        }
      >
        {entry && (
          <div data-testid="movimiento-detalle">
            <DetailList
              items={[
                { label: 'Cantidad', value: signedText(entry.record) },
                { label: 'Tipo', value: entry.record.typeName },
                { label: 'Producto', value: entry.record.name, wide: true },
                { label: 'SKU', value: entry.record.sku },
                { label: 'Posición', value: entry.record.binCode },
                { label: 'Fecha del movimiento', value: formatDateLong(entry.record.date) },
                { label: 'Registrado', value: `${formatDate(entry.record.recordedAt)} a las ${formatTime(entry.record.recordedAt)}` },
                { label: 'Usuario', value: entry.who },
                { label: 'Documento', value: entry.record.document },
                { label: 'Observaciones', value: entry.record.notes, wide: true },
              ]}
            />
          </div>
        )}
      </SidePanel>

      {printer.area}
    </div>
  );
}
