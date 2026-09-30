// Análisis › Contabilidad › Plan de cuentas (`GetChartOfAccountsQuery`; en el escritorio: AccountingView › Plan de
// cuentas). El plan en ÁRBOL (sangría por nivel, los grupos en negrita) con Debe, Haber y saldo de cada cuenta: de todo
// el historial o del período elegido (`?saldos=periodo`). Filtros de tipo, clase, movimiento y búsqueda; detalle lateral;
// «Crear subcuenta» bajo un grupo (`CreateAccountCommand`, en su diálogo) y «Ver asientos» de una cuenta imputable.

import { Download, Eye, FolderPlus, ListTree, NotebookText, RefreshCw } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useRpcQuery, useTableState, type RpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, DataTable, DetailList, FilterBar, SearchField, SelectField, SidePanel, StatusBadge, Toolbar, statusOptions, useNotify, type DataTableColumn } from '@/4-presentation/panel/kit';
import { exportCsv, formatMoney, formatNumber, sortRows, type SortState } from '@/4-presentation/panel/lib';
import {
  ACCOUNT_CSV,
  ACCOUNT_TYPES,
  ACTIVITY_OPTIONS,
  BALANCE_OPTIONS,
  CHART_FILTERS,
  CLASS_OPTIONS,
  accountTypeLabel,
  childrenOf,
  filterAccounts,
  type AccountRecord,
} from './accounting';
import { PeriodFields } from './PeriodFields';
import { rangeText, resolvePeriod } from './period';
import { useRowMenuGuard } from './useRowMenuGuard';

/** Sangría de cada nivel del árbol (clases fijas: Tailwind las necesita escritas). */
const INDENT = ['pl-0', 'pl-5', 'pl-10', 'pl-14', 'pl-16'] as const;

const moneyOrBlank = (value: number) => (value !== 0 ? formatMoney(value) : '');

const COLUMNS: DataTableColumn<AccountRecord>[] = [
  {
    id: 'cuenta',
    header: 'Cuenta',
    value: (account) => account.code,
    card: 'title',
    className: 'min-w-64',
    cell: (account) => (
      <span className={`flex items-baseline gap-2 ${INDENT[Math.min(account.level, INDENT.length - 1)]}`}>
        <span className="w-16 shrink-0 font-mono text-xs text-text-muted">{account.code}</span>
        <span className={account.isPostable ? undefined : 'font-semibold'}>{account.name}</span>
      </span>
    ),
  },
  { id: 'tipo', header: 'Tipo', value: (account) => accountTypeLabel(account.type), cell: (account) => <StatusBadge status={account.type} statuses={ACCOUNT_TYPES} /> },
  { id: 'clase', header: 'Clase', value: (account) => (account.isPostable ? 'Imputable' : 'Grupo') },
  { id: 'debe', header: 'Debe', align: 'end', value: (account) => account.debit, cell: (account) => moneyOrBlank(account.debit), footer: (rows) => formatMoney(rows.filter((row) => row.isPostable).reduce((sum, row) => sum + Math.round(row.debit * 100), 0) / 100) },
  { id: 'haber', header: 'Haber', align: 'end', value: (account) => account.credit, cell: (account) => moneyOrBlank(account.credit), footer: (rows) => formatMoney(rows.filter((row) => row.isPostable).reduce((sum, row) => sum + Math.round(row.credit * 100), 0) / 100) },
  {
    id: 'saldo',
    header: 'Saldo',
    align: 'end',
    value: (account) => account.balance,
    cell: (account) => (account.balance !== 0 ? <span className={account.balance < 0 ? 'text-danger-text' : undefined}>{formatMoney(account.balance)}</span> : '—'),
  },
];

function inTableOrder(list: readonly AccountRecord[], sort: SortState | null): readonly AccountRecord[] {
  const column = sort ? COLUMNS.find((item) => item.id === sort.column) : undefined;
  return sort && column?.value ? sortRows(list, column.value, sort.direction) : list;
}

export interface ChartViewProps {
  /** El plan con los saldos de todo el historial (lo carga la pantalla: también lo usan los diálogos y el libro). */
  chart: RpcQuery<AccountRecord[]>;
  /** Cambia después de registrar un asiento: los saldos del período se vuelven a consultar. */
  version: number;
  /** Abre el libro diario con los asientos de una cuenta. */
  onShowAccount: (code: string) => void;
  /** Abre «Nueva cuenta» bajo un grupo (null si el rol no puede crear cuentas). */
  onCreateUnder: ((parentCode: string) => void) | null;
}

export function ChartView({ chart, version, onShowAccount, onCreateUnder }: ChartViewProps) {
  const notify = useNotify();
  const table = useTableState({ filters: CHART_FILTERS, sort: { column: 'cuenta', direction: 'asc' } });
  const filters = table.filters;
  const [now] = useState(() => new Date());
  const period = resolvePeriod(filters, now);
  const byPeriod = filters.saldos === 'periodo';

  // Con «saldos del período» se consulta aparte; si no, se usa el plan que ya cargó la pantalla.
  const inPeriod = useRpcQuery('GetChartOfAccountsQuery', { from: period.from, to: period.to }, { enabled: byPeriod && period.problem === null });
  const reloadPeriod = inPeriod.reload;
  const [mountedVersion] = useState(version);
  useEffect(() => {
    if (version !== mountedVersion && byPeriod) reloadPeriod();
  }, [version, mountedVersion, byPeriod, reloadPeriod]);
  const source = byPeriod ? inPeriod : chart;
  const accounts = useMemo(() => source.data ?? [], [source.data]);
  const rows = useMemo(() => filterAccounts(accounts, filters), [accounts, filters]);

  const menu = useRowMenuGuard();
  const [detail, setDetail] = useState<{ account: AccountRecord; open: boolean } | null>(null);
  const openDetail = (account: AccountRecord) => setDetail({ account, open: true });
  const closeDetail = () => setDetail((current) => (current ? { ...current, open: false } : current));

  const exportRows = () => {
    const file = exportCsv(byPeriod ? `plan de cuentas ${period.from} ${period.to}` : 'plan de cuentas', ACCOUNT_CSV, inTableOrder(rows, table.sort));
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} cuentas).`);
  };

  const account = detail?.account;
  const children = account ? childrenOf(accounts, account.code) : [];
  const parent = account?.parentCode ? accounts.find((item) => item.code === account.parentCode) : undefined;

  return (
    <div className="space-y-5">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Código o nombre de la cuenta" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Tipo" allLabel="Todos los tipos" value={filters.tipo} onChange={(value) => table.setFilter('tipo', value)} options={statusOptions(ACCOUNT_TYPES)} />
        <SelectField label="Clase" allLabel="Grupos e imputables" value={filters.clase} onChange={(value) => table.setFilter('clase', value)} options={CLASS_OPTIONS} />
        <SelectField label="Movimiento" allLabel="Con y sin movimiento" value={filters.movimiento} onChange={(value) => table.setFilter('movimiento', value)} options={ACTIVITY_OPTIONS} />
        <SelectField label="Saldos" allLabel="De todo el historial" value={filters.saldos} onChange={(value) => table.setFilter('saldos', value)} options={BALANCE_OPTIONS} />
        {byPeriod && <PeriodFields period={period} onChange={(values) => table.setFilters(values)} />}
      </FilterBar>

      <Toolbar
        label="Acciones del plan de cuentas"
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={source.fetching && !source.loading} onClick={source.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {source.data && (
          <span className="text-sm text-text-muted" data-testid="cuentas-resumen">
            {formatNumber(rows.length)} de {formatNumber(accounts.length)} cuentas · saldos {byPeriod ? rangeText(period) : 'de todo el historial'}
          </span>
        )}
      </Toolbar>

      {byPeriod && period.problem ? (
        <Alert tone="warning" title="Revise las fechas">
          {period.problem} Los saldos se actualizan al corregirlas.
        </Alert>
      ) : (
        <DataTable
          caption="Plan de cuentas"
          columns={COLUMNS}
          rows={source.data ? rows : undefined}
          rowKey={(row) => row.code}
          rowLabel={(row) => `la cuenta ${row.code} ${row.name}`}
          loading={source.loading}
          refreshing={source.fetching && !source.loading}
          error={source.error}
          onRetry={source.reload}
          operation="GetChartOfAccountsQuery"
          {...table.tableProps}
          pageSizes={[25, 50, 100]}
          onRowOpen={(row) => {
            if (!menu.busy()) openDetail(row);
          }}
          activeRowKey={detail?.open ? detail.account.code : null}
          rowActions={(row) => [
            { label: 'Ver detalle', icon: <Eye />, onSelect: menu.guard(() => openDetail(row)) },
            { label: 'Ver los asientos de la cuenta', icon: <NotebookText />, onSelect: menu.guard(() => onShowAccount(row.code)), hidden: !row.isPostable },
            { label: 'Crear una subcuenta', icon: <FolderPlus />, onSelect: menu.guard(() => onCreateUnder?.(row.code)), hidden: row.isPostable || onCreateUnder === null },
          ]}
          empty={{
            title: accounts.length === 0 ? 'El plan de cuentas está vacío' : 'Ninguna cuenta coincide con los filtros',
            description: accounts.length === 0 ? undefined : 'Pruebe con otros filtros o límpielos.',
            icon: <ListTree />,
            action:
              accounts.length === 0 ? undefined : (
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
        title={account ? `${account.code} · ${account.name}` : 'Cuenta'}
        description={account ? (account.isPostable ? 'Cuenta imputable (lleva asientos)' : 'Grupo (suma sus subcuentas)') : undefined}
        headerExtra={account && <StatusBadge status={account.type} statuses={ACCOUNT_TYPES} />}
        footer={
          account && (
            <div className="flex flex-col gap-2">
              {account.isPostable && (
                <Button variant="outline" leftIcon={<NotebookText />} fullWidth onClick={() => onShowAccount(account.code)}>
                  Ver los asientos de la cuenta
                </Button>
              )}
              {!account.isPostable && onCreateUnder && (
                <Button leftIcon={<FolderPlus />} fullWidth onClick={() => onCreateUnder(account.code)}>
                  Crear una subcuenta
                </Button>
              )}
            </div>
          )
        }
      >
        {account && (
          <div data-testid="cuenta-detalle">
            <DetailList
              items={[
                { label: 'Código', value: account.code },
                { label: 'Tipo', value: accountTypeLabel(account.type) },
                { label: 'Grupo', value: parent ? `${parent.code} · ${parent.name}` : account.parentCode ? account.parentCode : 'Es una cuenta principal' },
                { label: 'Nivel', value: formatNumber(account.level + 1) },
                { label: 'Debe', value: formatMoney(account.debit) },
                { label: 'Haber', value: formatMoney(account.credit) },
                { label: `Saldo (${byPeriod ? rangeText(period) : 'todo el historial'})`, value: formatMoney(account.balance), wide: true },
                { label: 'Subcuentas', value: children.length > 0 ? children.map((child) => `${child.code} ${child.name}`).join(' · ') : null, wide: true },
              ]}
            />
          </div>
        )}
      </SidePanel>
    </div>
  );
}
