// Tecnología › Garantías: la pantalla «Garantías y RMA» del escritorio (WarrantyClaimsView) en la web. Los casos de
// garantía (`GetWarrantyClaimsQuery`, los ABIERTOS al entrar) con filtros, tabla, exportar CSV y el detalle lateral
// (`GetWarrantyClaimQuery`) con la bitácora y SOLO los pasos que el servidor permite (regla T-05). Comandos: abrir un caso
// (`OpenWarrantyClaimCommand`, con la garantía de la serie de `GetWarrantyStatusQuery`), avanzar el estado
// (`MoveWarrantyClaimCommand`), entregar la unidad de reemplazo (`IssueWarrantyReplacementCommand`) y agregar notas
// (`AddWarrantyClaimNoteCommand`); más la orden de servicio imprimible. Los indicadores del escritorio van PLEGADOS en
// «Ver resumen de los casos» (P-10).
//
// Filtros en la dirección: `estado` va al SERVIDOR (`abiertos` por defecto, vacío = todos, o un estado); `q`, `sucursal`,
// `cobertura`, `dias` y las fechas de recepción (`desde`, `hasta`) se filtran en la página. Además: `?abrir=1` abre
// «Abrir un caso» (lo usa el tablero), con `&serie=<serie>&sku=<SKU>` ya escrita (lo usa Series), y `?ver=<número>` abre el
// detalle de ese caso (lo usa Series). `abrir` se quita de la dirección al abrirse; `ver`, al cerrar el detalle.

import { BarChart3, Download, Eye, NotebookPen, Printer, RefreshCw, ScanBarcode, ShieldCheck, ShieldPlus } from 'lucide-react';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Button,
  Collapsible,
  DataTable,
  DateRangeField,
  FilterBar,
  Page,
  SearchField,
  SelectField,
  StatusBadge,
  Toolbar,
  useNotify,
  type DataTableColumn,
  type RowActionItem,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatDateTime, formatNumber } from '@/4-presentation/panel/lib';
import { AddNoteDialog } from './AddNoteDialog';
import { ClaimDetailPanel, type ClaimAbilities } from './ClaimDetailPanel';
import { ClaimsSummary } from './ClaimsSummary';
import {
  CLAIMS_CSV,
  CLAIM_FILTERS,
  CLAIM_STATES,
  COVERAGES,
  COVERAGE_OPTIONS,
  DAYS_OPTIONS,
  OPEN_FILTER,
  STATE_FILTER_OPTIONS,
  branchLabel as labelOfBranch,
  branchOptions,
  claimStatusLabel,
  claimsRequest,
  daysText,
  filterClaims,
  seriesPath,
  toClaimItems,
  usesReplacement,
  type ClaimItem,
  type ClaimRecord,
  type NextStep,
} from './claims';
import { MoveClaimDialog, type MoveTarget } from './MoveClaimDialog';
import { OpenClaimDialog, type OpenClaimTarget } from './OpenClaimDialog';
import { ReplaceClaimDialog } from './ReplaceClaimDialog';
import { ServiceOrderDialog } from './ServiceOrderDialog';

const COLUMNS: DataTableColumn<ClaimItem>[] = [
  {
    id: 'caso',
    header: 'Caso',
    value: (item) => item.row.number,
    card: 'title',
    className: 'whitespace-nowrap',
    cell: (item) => (
      <span className="block">
        <span className="block">{item.row.number}</span>
        <span className="block text-xs font-normal text-text-muted">Sucursal {item.row.branchCode}</span>
      </span>
    ),
  },
  { id: 'recibido', header: 'Recibido', value: (item) => new Date(item.row.receivedAt), cell: (item) => formatDateTime(item.row.receivedAt), className: 'whitespace-nowrap' },
  {
    id: 'equipo',
    header: 'Equipo y cliente',
    value: (item) => item.row.product,
    cell: (item) => (
      <span className="block min-w-48">
        <span className="block">{item.row.product}</span>
        <span className="block text-xs break-all text-text-muted">
          {item.row.sku} · {item.row.serial}
        </span>
        <span className="block text-xs text-text-muted">{item.row.customer}</span>
      </span>
    ),
  },
  {
    id: 'falla',
    header: 'Falla',
    value: (item) => item.row.issue,
    sortable: false,
    cell: (item) => <span className="line-clamp-2 block max-w-xs min-w-40 text-text-muted">{item.row.issue}</span>,
  },
  {
    id: 'cobertura',
    header: 'Cobertura',
    value: (item) => COVERAGES[item.coverage].label,
    cell: (item) => <StatusBadge status={item.coverage} statuses={COVERAGES} />,
  },
  {
    id: 'estado',
    header: 'Estado',
    value: (item) => claimStatusLabel(item.row.status),
    cell: (item) => <StatusBadge status={item.row.status} statuses={CLAIM_STATES} />,
  },
  { id: 'dias', header: 'Tiempo', align: 'end', value: (item) => item.row.daysOpen, cell: (item) => <span className="whitespace-nowrap">{daysText(item.row)}</span> },
];

export function ClaimsPage() {
  const notify = useNotify();
  const navigate = useNavigate();
  const { can, canRun, session } = usePermissions();
  const table = useTableState({ filters: CLAIM_FILTERS, sort: { column: 'recibido', direction: 'desc' } });
  const filters = table.filters;
  const [params, setParams] = useSearchParams();

  // Lo que filtra el SERVIDOR (el estado) va en el pedido; el resto se filtra en la página.
  const claims = useRpcQuery('GetWarrantyClaimsQuery', claimsRequest(filters.estado));
  const items = useMemo(() => toClaimItems(claims.data ?? []), [claims.data]);
  const rows = useMemo(() => filterClaims(items, filters), [items, filters]);
  const branches = useMemo(() => session?.access.branches ?? [], [session]);
  const branchChoices = useMemo(() => branchOptions(items, branches, filters.sucursal), [items, branches, filters.sucursal]);
  const branchLabel = useCallback((code: string) => labelOfBranch(code, branches), [branches]);

  const canOpen = canRun('OpenWarrantyClaimCommand');
  const abilities: ClaimAbilities = {
    move: canRun('MoveWarrantyClaimCommand'),
    replace: canRun('IssueWarrantyReplacementCommand'),
    note: canRun('AddWarrantyClaimNoteCommand'),
    sales: can('sales.view'),
  };

  // Detalle (se guarda el número y si está abierto: al cerrar, el panel se desliza con su contenido).
  const [detail, setDetail] = useState<{ number: string; open: boolean } | null>(null);
  const detailOpen = detail?.open ?? false;
  const claimDetail = useRpcQuery('GetWarrantyClaimQuery', { number: detail?.number ?? '' }, { enabled: detailOpen, keepPreviousData: false });
  const detailItem = detail ? (items.find((item) => item.key === detail.number)?.row ?? null) : null;

  // Diálogos: cada uno se monta de nuevo en cada apertura (`key`), así su formulario empieza vacío.
  const [dialogSession, setDialogSession] = useState(0);
  const [opening, setOpening] = useState<OpenClaimTarget | null>(null);
  const [moving, setMoving] = useState<MoveTarget | null>(null);
  const [replacing, setReplacing] = useState<ClaimRecord | null>(null);
  const [noting, setNoting] = useState<ClaimRecord | null>(null);
  const [printing, setPrinting] = useState<string | null>(null);
  const nextDialog = () => setDialogSession((count) => count + 1);

  const openDetail = (number: string) => setDetail({ number, open: true });
  const closeDetail = () => {
    setDetail((current) => (current?.open ? { ...current, open: false } : current));
    // `?ver=` queda en la dirección mientras el detalle está abierto (quitarlo antes cerraría el panel, que se cierra al
    // navegar); al cerrarlo se quita.
    if (params.has('ver')) {
      setParams(
        (previous) => {
          const updated = new URLSearchParams(previous);
          updated.delete('ver');
          return updated;
        },
        { replace: true },
      );
    }
  };
  const reloadAll = () => {
    claims.reload();
    claimDetail.reload();
  };
  const askOpen = (target: OpenClaimTarget) => {
    nextDialog();
    setOpening(target);
  };
  const askNote = (claim: ClaimRecord) => {
    nextDialog();
    setNoting(claim);
  };
  const askPrint = (number: string) => {
    nextDialog();
    setPrinting(number);
  };
  const askStep = (claim: ClaimRecord, step: NextStep) => {
    nextDialog();
    if (usesReplacement(step.next, claim)) setReplacing(claim);
    else setMoving({ claim, next: step.next });
  };
  const afterOpened = (row: ClaimRecord) => {
    // El caso nuevo se ve con su detalle (y en la lista de abiertos, la que se ve al entrar).
    claims.reload();
    openDetail(row.number);
  };

  // `?abrir=1[&serie=&sku=]` (tablero y Series) y `?ver=<número>` (Series): se abren una vez por pedido. `abrir`, `serie` y
  // `sku` se quitan enseguida de la dirección; `ver`, al cerrar el detalle (quitarlo antes cerraría el panel).
  const openParam = params.get('abrir');
  const serialParam = params.get('serie');
  const skuParam = params.get('sku');
  const viewParam = params.get('ver');
  const signature = `${openParam ?? ''}|${serialParam ?? ''}|${skuParam ?? ''}|${viewParam ?? ''}`;
  const [handled, setHandled] = useState('|||');
  if (signature !== handled) {
    setHandled(signature);
    if (viewParam) setDetail({ number: viewParam.trim().toUpperCase(), open: true });
    if (openParam !== null && canOpen) {
      setDialogSession((count) => count + 1);
      setOpening({ serial: serialParam?.trim() ?? '', sku: skuParam?.trim() || null });
    }
  }
  useEffect(() => {
    if (openParam === null && serialParam === null && skuParam === null) return;
    setParams(
      (previous) => {
        const updated = new URLSearchParams(previous);
        for (const key of ['abrir', 'serie', 'sku']) updated.delete(key);
        return updated;
      },
      { replace: true },
    );
  }, [openParam, serialParam, skuParam, setParams]);

  const rowActions = (item: ClaimItem): RowActionItem[] => [
    { label: 'Ver detalle', icon: <Eye />, onSelect: () => openDetail(item.row.number) },
    { label: 'Imprimir la orden de servicio', icon: <Printer />, onSelect: () => askPrint(item.row.number) },
    { label: 'Ver la serie', icon: <ScanBarcode />, onSelect: () => navigate(ROUTES.panelModule(seriesPath(item.row.serial, item.row.sku))) },
    { label: 'Agregar nota', icon: <NotebookPen />, onSelect: () => askNote(item.row), hidden: !abilities.note || !item.open },
  ];

  const exportRows = () => {
    const file = exportCsv('garantias', CLAIMS_CSV, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const openCount = rows.filter((item) => item.open).length;
  const onlyOpenView = filters.estado === OPEN_FILTER;

  return (
    <Page
      title="Garantías"
      description="Casos de garantía (RMA): recibir el equipo, diagnosticarlo, enviarlo al proveedor, repararlo o reemplazarlo y entregarlo, con su orden de servicio."
      actions={
        canOpen && (
          <Button leftIcon={<ShieldPlus />} onClick={() => askOpen({ serial: '', sku: null })}>
            Abrir un caso
          </Button>
        )
      }
    >
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Número de caso, serie, cliente o producto" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Estado" allLabel="Todos los casos" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={STATE_FILTER_OPTIONS} />
        <SelectField label="Sucursal" allLabel="Todas las sucursales" value={filters.sucursal} onChange={(value) => table.setFilter('sucursal', value)} options={branchChoices} />
        <SelectField label="Cobertura" allLabel="En garantía y con cargo" value={filters.cobertura} onChange={(value) => table.setFilter('cobertura', value)} options={COVERAGE_OPTIONS} />
        <SelectField label="Tiempo en el taller" allLabel="Cualquiera" value={filters.dias} onChange={(value) => table.setFilter('dias', value)} options={DAYS_OPTIONS} />
        <DateRangeField label="Fecha de recepción" value={table.dateRange()} onChange={(range) => table.setDateRange(range)} />
      </FilterBar>

      <Toolbar
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={claims.fetching && !claims.loading} onClick={claims.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {claims.data && (
          <span className="text-sm text-text-muted" data-testid="casos-resumen">
            {formatNumber(rows.length)} de {formatNumber(items.length)} casos{onlyOpenView ? ' abiertos' : ` · ${formatNumber(openCount)} abiertos`}
          </span>
        )}
      </Toolbar>

      <Collapsible
        label="Ver resumen de los casos"
        openLabel="Ocultar resumen de los casos"
        icon={<BarChart3 />}
        description="Casos abiertos, en el taller o en el proveedor, con cargo y entregados (todos los casos, no solo los de la lista)."
      >
        <ClaimsSummary />
      </Collapsible>

      <DataTable
        caption="Casos de garantía"
        columns={COLUMNS}
        rows={claims.data ? rows : undefined}
        rowKey={(item) => item.key}
        rowLabel={(item) => `el caso ${item.row.number}`}
        loading={claims.loading}
        refreshing={claims.fetching && !claims.loading}
        error={claims.error}
        onRetry={claims.reload}
        operation="GetWarrantyClaimsQuery"
        {...table.tableProps}
        onRowOpen={(item) => openDetail(item.row.number)}
        activeRowKey={detail?.open ? detail.number : null}
        rowActions={rowActions}
        empty={{
          title: items.length === 0 ? (onlyOpenView ? 'No hay casos abiertos' : 'Todavía no hay casos de garantía') : 'No hay casos con estos filtros',
          description:
            items.length === 0
              ? 'Reciba un equipo en garantía con «Abrir un caso» o desde Series.'
              : 'Pruebe con otra búsqueda u otros filtros, o limpie los filtros.',
          icon: <ShieldCheck />,
          action:
            items.length > 0 || table.activeFilterCount > 0 ? (
              <Button variant="outline" onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ) : canOpen ? (
              <Button variant="outline" leftIcon={<ShieldPlus />} onClick={() => askOpen({ serial: '', sku: null })}>
                Abrir un caso
              </Button>
            ) : undefined,
        }}
      />

      <ClaimDetailPanel
        number={detail?.number ?? null}
        open={detailOpen}
        onClose={closeDetail}
        item={detailItem}
        detail={claimDetail}
        abilities={abilities}
        branchLabel={branchLabel}
        onStep={askStep}
        onNote={askNote}
        onPrint={askPrint}
      />

      {canOpen && <OpenClaimDialog key={`abrir-${dialogSession}`} target={opening} onClose={() => setOpening(null)} onOpened={afterOpened} />}
      <MoveClaimDialog key={`mover-${dialogSession}`} target={moving} onClose={() => setMoving(null)} onMoved={reloadAll} />
      <ReplaceClaimDialog key={`reemplazo-${dialogSession}`} target={replacing} onClose={() => setReplacing(null)} onReplaced={reloadAll} />
      <AddNoteDialog key={`nota-${dialogSession}`} target={noting} onClose={() => setNoting(null)} onAdded={() => claimDetail.reload()} />
      <ServiceOrderDialog key={`orden-${dialogSession}`} number={printing} onClose={() => setPrinting(null)} />
    </Page>
  );
}
