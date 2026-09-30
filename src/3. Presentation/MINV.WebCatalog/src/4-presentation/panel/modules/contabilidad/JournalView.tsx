// Análisis › Contabilidad › Libro diario (`GetJournalQuery`; en el escritorio: AccountingView › Libro diario). Los
// asientos del período (las fechas las filtra el servidor) con filtros de cuenta, origen (venta, compra, anulación…),
// estado y búsqueda en la página; cada fila se despliega con su detalle Debe/Haber y abre el detalle lateral. Con una
// cuenta elegida, el resumen dice su Debe y su Haber en el período (el mayor de la cuenta). «Usar como plantilla» abre
// un asiento manual con las mismas cuentas. Exportar CSV (una fila por línea, como el escritorio).

import { CopyPlus, Download, Eye, NotebookText, RefreshCw } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Alert,
  Button,
  ComboBox,
  DataTable,
  DetailList,
  FilterBar,
  SearchField,
  SelectField,
  SidePanel,
  StatusBadge,
  Toolbar,
  statusOptions,
  useNotify,
  type ComboOption,
  type DataTableColumn,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatDate, formatDateLong, formatMoney, formatNumber, sortRows, type SortState } from '@/4-presentation/panel/lib';
import {
  ENTRY_STATUSES,
  JOURNAL_CSV,
  JOURNAL_FILTERS,
  JOURNAL_LIMIT,
  ORIGINS,
  accountLabel,
  accountMovement,
  entryPrefill,
  filterJournal,
  journalCsvRows,
  postableAccounts,
  toJournalItems,
  type AccountRecord,
  type EntryPrefill,
  type JournalItem,
} from './accounting';
import { EntryLinesTable } from './EntryLinesTable';
import { PeriodFields } from './PeriodFields';
import { periodName, rangeText, resolvePeriod } from './period';
import { useRowMenuGuard } from './useRowMenuGuard';

const COLUMNS: DataTableColumn<JournalItem>[] = [
  { id: 'asiento', header: 'Asiento', value: (item) => item.record.number, card: 'title', className: 'whitespace-nowrap' },
  { id: 'fecha', header: 'Fecha', value: (item) => item.record.date, cell: (item) => formatDate(item.record.date), className: 'whitespace-nowrap' },
  { id: 'origen', header: 'Origen', value: (item) => ORIGINS[item.origin].label, cell: (item) => <StatusBadge status={item.origin} statuses={ORIGINS} /> },
  { id: 'descripcion', header: 'Descripción', value: (item) => item.record.description, cell: (item) => <span className="line-clamp-2 block max-w-md min-w-48">{item.record.description}</span> },
  { id: 'debe', header: 'Debe', align: 'end', value: (item) => item.debit, cell: (item) => formatMoney(item.debit), footer: (rows) => formatMoney(rows.reduce((sum, item) => sum + Math.round(item.debit * 100), 0) / 100) },
  { id: 'haber', header: 'Haber', align: 'end', value: (item) => item.credit, cell: (item) => formatMoney(item.credit), footer: (rows) => formatMoney(rows.reduce((sum, item) => sum + Math.round(item.credit * 100), 0) / 100) },
  { id: 'estado', header: 'Estado', value: (item) => item.record.status, cell: (item) => <StatusBadge status={item.record.status} statuses={ENTRY_STATUSES} /> },
];

function inTableOrder(list: readonly JournalItem[], sort: SortState | null): readonly JournalItem[] {
  const column = sort ? COLUMNS.find((item) => item.id === sort.column) : undefined;
  return sort && column?.value ? sortRows(list, column.value, sort.direction) : list;
}

export interface JournalViewProps {
  /** El plan de cuentas (para la lista «Cuenta»). */
  accounts: readonly AccountRecord[];
  version: number;
  /** «Usar como plantilla»: abre el asiento manual con estas líneas (null si el rol no puede registrar asientos). */
  onUseAsTemplate: ((prefill: EntryPrefill) => void) | null;
}

export function JournalView({ accounts, version, onUseAsTemplate }: JournalViewProps) {
  const notify = useNotify();
  const table = useTableState({ filters: JOURNAL_FILTERS, sort: { column: 'asiento', direction: 'desc' } });
  const filters = table.filters;
  const [now] = useState(() => new Date());
  const period = resolvePeriod(filters, now);

  const journal = useRpcQuery('GetJournalQuery', { from: period.from, to: period.to }, { enabled: period.problem === null });
  const reload = journal.reload;
  // Después de registrar un asiento (la versión cambia) se vuelve a consultar; al montarse ya consulta sola.
  const [mountedVersion] = useState(version);
  useEffect(() => {
    if (version !== mountedVersion) reload();
  }, [version, mountedVersion, reload]);
  const items = useMemo(() => toJournalItems(journal.data ?? []), [journal.data]);
  const rows = useMemo(() => filterJournal(items, filters), [items, filters]);
  const accountOptions = useMemo<ComboOption[]>(() => postableAccounts(accounts).map((account) => ({ value: account.code, label: accountLabel(account) })), [accounts]);
  const account = accountOptions.find((option) => option.value === filters.cuenta) ?? (filters.cuenta ? { value: filters.cuenta, label: filters.cuenta } : null);
  const movement = filters.cuenta ? accountMovement(rows, filters.cuenta) : null;

  const menu = useRowMenuGuard();
  const [detail, setDetail] = useState<{ item: JournalItem; open: boolean } | null>(null);
  const openDetail = (item: JournalItem) => setDetail({ item, open: true });
  const closeDetail = () => setDetail((current) => (current ? { ...current, open: false } : current));
  const repeatEntry = (item: JournalItem) => {
    closeDetail();
    onUseAsTemplate?.(entryPrefill(item.record));
  };

  const exportRows = () => {
    const file = exportCsv(`libro diario ${period.from} ${period.to}`, JOURNAL_CSV, journalCsvRows(inTableOrder(rows, table.sort)));
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} asientos).`);
  };

  const item = detail?.item;
  const capped = (journal.data?.length ?? 0) >= JOURNAL_LIMIT;

  return (
    <div className="space-y-5">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <PeriodFields period={period} onChange={(values) => table.setFilters(values)} />
        <ComboBox label="Cuenta" placeholder="Código o nombre" value={account} onChange={(option) => table.setFilter('cuenta', option?.value ?? '')} options={accountOptions} />
        <SelectField label="Origen" allLabel="Todos los orígenes" value={filters.origen} onChange={(value) => table.setFilter('origen', value)} options={statusOptions(ORIGINS)} />
        <SelectField label="Estado" allLabel="Todos los estados" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(ENTRY_STATUSES)} />
        <SearchField label="Buscar" placeholder="Número, descripción, cuenta o glosa" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
      </FilterBar>

      <Toolbar
        label="Acciones del libro diario"
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={journal.fetching && !journal.loading} onClick={journal.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {journal.data && (
          <span className="text-sm text-text-muted" data-testid="diario-resumen">
            {formatNumber(rows.length)} de {formatNumber(items.length)} asientos · {periodName(period)}: {rangeText(period)}
            {capped ? ` · el servidor devuelve como máximo ${formatNumber(JOURNAL_LIMIT)}: acorte el período` : ''}
          </span>
        )}
      </Toolbar>

      {movement && (
        <div data-testid="diario-mayor" className="rounded-card border border-border bg-surface-2/60 px-4 py-3 text-sm">
          <span className="font-semibold text-text">{account?.label ?? filters.cuenta}</span>
          <span className="text-text-muted">
            {' '}
            · Debe {formatMoney(movement.debit)} · Haber {formatMoney(movement.credit)} · en {formatNumber(movement.entries)} asientos de estos filtros
          </span>
        </div>
      )}

      {period.problem ? (
        <Alert tone="warning" title="Revise las fechas">
          {period.problem} El libro se actualiza al corregirlas.
        </Alert>
      ) : (
        <DataTable
          caption="Libro diario"
          columns={COLUMNS}
          rows={journal.data ? rows : undefined}
          rowKey={(row) => row.key}
          rowLabel={(row) => `el asiento ${row.record.number}`}
          loading={journal.loading}
          refreshing={journal.fetching && !journal.loading}
          error={journal.error}
          onRetry={journal.reload}
          operation="GetJournalQuery"
          {...table.tableProps}
          renderExpanded={(row) => <EntryLinesTable number={row.record.number} lines={row.record.lines} highlight={filters.cuenta || undefined} />}
          onRowOpen={(row) => {
            if (!menu.busy()) openDetail(row);
          }}
          activeRowKey={detail?.open ? detail.item.key : null}
          rowActions={(row) => [
            { label: 'Ver detalle', icon: <Eye />, onSelect: menu.guard(() => openDetail(row)) },
            { label: 'Usar como plantilla de un asiento nuevo', icon: <CopyPlus />, onSelect: menu.guard(() => repeatEntry(row)), hidden: onUseAsTemplate === null },
          ]}
          empty={{
            title: items.length === 0 ? 'No hay asientos en este período' : 'Ningún asiento coincide con los filtros',
            description: items.length === 0 ? 'Pruebe con otro período.' : 'Pruebe con otros filtros o límpielos.',
            icon: <NotebookText />,
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
        title={item ? `Asiento ${item.record.number}` : 'Asiento'}
        description={item ? formatDateLong(item.record.date) : undefined}
        headerExtra={item && <StatusBadge status={item.record.status} statuses={ENTRY_STATUSES} />}
        footer={
          item &&
          onUseAsTemplate && (
            <Button variant="outline" leftIcon={<CopyPlus />} fullWidth onClick={() => repeatEntry(item)}>
              Usar como plantilla de un asiento nuevo
            </Button>
          )
        }
      >
        {item && (
          <div className="space-y-5" data-testid="asiento-detalle">
            <DetailList
              items={[
                { label: 'Descripción', value: item.record.description, wide: true },
                { label: 'Origen', value: <StatusBadge status={item.origin} statuses={ORIGINS} /> },
                { label: 'Total', value: formatMoney(item.record.total) },
              ]}
            />
            <EntryLinesTable number={item.record.number} lines={item.record.lines} highlight={filters.cuenta || undefined} />
            <p className="text-sm text-text-muted">Un asiento registrado no se borra ni se edita: para corregirlo se registra otro asiento que lo compense.</p>
          </div>
        )}
      </SidePanel>
    </div>
  );
}
