// Sucursales › Sucursales (paquete M7): lo mismo que «Sucursales» del escritorio (BranchesView + BranchesViewModel) y
// más, en dos pestañas para no mezclar:
//   · «Sucursales»: el directorio (`GetBranchesQuery`) con filtros en la dirección (búsqueda, estado, mis sucursales u
//     otras), tabla, detalle lateral con los enlaces a sus transferencias, y —para quien administra sucursales— nueva
//     sucursal (`CreateBranchCommand`), editar y activar/desactivar (`UpdateBranchCommand`, con confirmación al
//     desactivar) y los usuarios de cada sucursal con casillas (`AssignUserBranchesCommand`). El comparativo por
//     sucursal (`GetBranchReportQuery`) va PLEGADO en «Ver comparativo por sucursal» (regla P-10).
//   · «Stock consolidado» (`ConsolidatedStockQuery`, en `ConsolidatedStockTab`).
// Cada acción solo se ofrece a quien puede ejecutar su comando (`canRun`); el servidor decide igual (regla P-01).

import { ArrowDownToLine, ArrowUpFromLine, BarChart3, Boxes, Building2, Download, Eye, Pencil, Plus, Power, PowerOff, RefreshCw, Users } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { usePermissions, useRpcCommand, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Alert,
  Button,
  Collapsible,
  ConfirmDialog,
  DataTable,
  DetailList,
  FilterBar,
  Page,
  SearchField,
  SelectField,
  SidePanel,
  StatusBadge,
  TabPanel,
  Tabs,
  Toolbar,
  statusOptions,
  useNotify,
  type DataTableColumn,
  type TabItem,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatMoney, formatNumber, sortRows, type SortState } from '@/4-presentation/panel/lib';
import { BranchReport } from './BranchReport';
import { EditBranchDialog, NewBranchDialog } from './BranchDialogs';
import { ConsolidatedStockTab } from './ConsolidatedStockTab';
import { BranchUsersDialog, UserBranchesDialog } from './UsersDialogs';
import { useTabParam } from './params';
import { useRowMenuGuard } from './rowGuard';
import {
  ACCESS_OPTIONS,
  BRANCHES_CSV,
  BRANCH_FILTERS,
  BRANCH_STATES,
  editProblem,
  filterBranches,
  plainMessage,
  toBranchItems,
  transfersLink,
  updateBranchPayload,
  type BranchItem,
  type BranchRecord,
} from './branches';

type BranchesTab = 'sucursales' | 'stock';

const COLUMNS: DataTableColumn<BranchItem>[] = [
  {
    id: 'sucursal',
    header: 'Sucursal',
    value: (item) => item.row.code,
    card: 'title',
    cell: (item) => (
      <span className="block min-w-44">
        <span className="block">
          {item.row.code} · {item.row.name}
        </span>
        {!item.row.isVisible && <span className="block text-xs font-normal text-text-muted">No es una de sus sucursales</span>}
      </span>
    ),
  },
  { id: 'almacenes', header: 'Almacenes', value: (item) => item.row.warehouses.join(', '), sortable: false },
  { id: 'usuarios', header: 'Usuarios', align: 'end', value: (item) => item.row.users },
  {
    id: 'stock',
    header: 'Stock valorizado',
    align: 'end',
    value: (item) => item.row.stockValue,
    cell: (item) => (item.row.stockValue === null ? <span className="text-text-muted">Sin acceso</span> : formatMoney(item.row.stockValue)),
    footer: (rows) => formatMoney(rows.reduce((sum, item) => sum + (item.row.stockValue ?? 0), 0)),
  },
  { id: 'salen', header: 'Salen', align: 'end', value: (item) => item.row.transfersOut },
  { id: 'llegan', header: 'Llegan', align: 'end', value: (item) => item.row.transfersIn },
  { id: 'estado', header: 'Estado', value: (item) => BRANCH_STATES[item.state].label, cell: (item) => <StatusBadge status={item.state} statuses={BRANCH_STATES} /> },
];

function inTableOrder(list: readonly BranchItem[], sort: SortState | null): readonly BranchItem[] {
  const column = sort ? COLUMNS.find((item) => item.id === sort.column) : undefined;
  return sort && column?.value ? sortRows(list, column.value, sort.direction) : list;
}

interface Shown<T> {
  item: T;
  open: boolean;
}

export function BranchesPage() {
  const notify = useNotify();
  const navigate = useNavigate();
  const { can, canRun, session } = usePermissions();
  const canStock = canRun('ConsolidatedStockQuery');
  const [tab, setTab] = useTabParam<BranchesTab>(canStock ? ['sucursales', 'stock'] : ['sucursales'], 'sucursales');
  const table = useTableState({ filters: BRANCH_FILTERS, sort: { column: 'sucursal', direction: 'asc' } });
  const filters = table.filters;

  const branches = useRpcQuery('GetBranchesQuery', {});
  const all = useMemo(() => branches.data ?? [], [branches.data]);
  const items = useMemo(() => toBranchItems(all), [all]);
  const rows = useMemo(() => filterBranches(items, filters), [items, filters]);

  const canCreate = canRun('CreateBranchCommand');
  const canUpdate = canRun('UpdateBranchCommand');
  const canAssign = canRun('AssignUserBranchesCommand') && canRun('GetUsersQuery');
  const canReport = canRun('GetBranchReportQuery');
  const canTransfers = can('inventory.transfers.manage') && can('inventory.stock.view');
  const { fromMenu, guardOpen } = useRowMenuGuard();

  const [detail, setDetail] = useState<Shown<BranchItem> | null>(null);
  const [dialogSession, setDialogSession] = useState(0);
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<BranchRecord | null>(null);
  const [members, setMembers] = useState<BranchRecord | null>(null);
  const [assigning, setAssigning] = useState(false);
  const [deactivating, setDeactivating] = useState<Shown<BranchItem> | null>(null);
  const toggle = useRpcCommand('UpdateBranchCommand', { notifyError: false });

  const detailItem = detail ? (items.find((item) => item.key === detail.item.key) ?? detail.item) : null;
  const next = () => setDialogSession((count) => count + 1);
  const openDetail = (item: BranchItem) => setDetail({ item, open: true });
  const closeDetail = () => setDetail((current) => (current?.open ? { ...current, open: false } : current));
  const askEdit = (item: BranchItem) => {
    next();
    setEditing(item.row);
  };
  const askMembers = (item: BranchItem) => {
    next();
    setMembers(item.row);
  };
  const askDeactivate = (item: BranchItem) => {
    toggle.reset();
    setDeactivating({ item, open: true });
  };

  const activate = async (item: BranchItem) => {
    const outcome = await toggle.run(updateBranchPayload(item.row, item.row.name, true));
    if (outcome.ok) {
      notify.success('Sucursal activada', plainMessage(outcome.result));
      branches.reload();
    } else {
      notify.error('No se pudo activar la sucursal', outcome.error);
    }
  };

  const confirmDeactivate = async () => {
    const target = deactivating?.item;
    if (!target) return false;
    const outcome = await toggle.run(updateBranchPayload(target.row, target.row.name, false));
    if (outcome.ok) {
      notify.success('Sucursal desactivada', plainMessage(outcome.result));
      branches.reload();
    }
    return outcome;
  };

  const exportRows = () => {
    const file = exportCsv('sucursales', BRANCHES_CSV, inTableOrder(rows, table.sort));
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const rowActions = (item: BranchItem) => {
    const lastActive = editProblem(item.row.name, false, item.row, all).isActive;
    return [
      { label: 'Ver detalle', icon: <Eye />, onSelect: fromMenu(() => openDetail(item)) },
      { label: 'Transferencias que salen', icon: <ArrowUpFromLine />, onSelect: fromMenu(() => navigate(transfersLink('origen', item.row.code))), hidden: !canTransfers },
      { label: 'Transferencias que llegan', icon: <ArrowDownToLine />, onSelect: fromMenu(() => navigate(transfersLink('destino', item.row.code))), hidden: !canTransfers },
      { label: 'Usuarios de la sucursal', icon: <Users />, onSelect: fromMenu(() => askMembers(item)), hidden: !canAssign || !item.row.isActive },
      { label: 'Editar', icon: <Pencil />, onSelect: fromMenu(() => askEdit(item)), hidden: !canUpdate },
      { label: 'Activar', icon: <Power />, onSelect: fromMenu(() => void activate(item)), hidden: !canUpdate || item.row.isActive },
      {
        label: 'Desactivar',
        icon: <PowerOff />,
        tone: 'danger' as const,
        onSelect: fromMenu(() => askDeactivate(item)),
        hidden: !canUpdate || !item.row.isActive,
        disabled: Boolean(lastActive),
        disabledReason: lastActive,
      },
    ];
  };

  const tabItems: TabItem<BranchesTab>[] = [
    { id: 'sucursales', label: 'Sucursales', icon: <Building2 /> },
    ...(canStock ? [{ id: 'stock' as const, label: 'Stock consolidado', icon: <Boxes /> }] : []),
  ];
  const current = detailItem;
  const scopeText = session?.access.allBranches ? 'Gerencia global: ve todas las sucursales.' : 'Ve los datos de las sucursales asignadas a su usuario.';

  return (
    <Page
      title="Sucursales"
      description="Ventas y stock por sucursal, el stock consolidado con lo que está en tránsito y quién trabaja en cada sucursal."
      actions={
        <>
          {canAssign && (
            <Button
              variant="outline"
              leftIcon={<Users />}
              onClick={() => {
                next();
                setAssigning(true);
              }}
            >
              Asignar usuarios
            </Button>
          )}
          {canCreate && (
            <Button
              leftIcon={<Plus />}
              onClick={() => {
                next();
                setCreating(true);
              }}
            >
              Nueva sucursal
            </Button>
          )}
        </>
      }
    >
      <Tabs label="Secciones de las sucursales" value={tab} onChange={(value) => setTab(value)} tabs={tabItems}>
        <TabPanel id="sucursales" className="space-y-5">
          <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
            <SearchField label="Buscar" placeholder="Código, nombre o almacén" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
            <SelectField label="Estado" allLabel="Todas" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(BRANCH_STATES)} />
            <SelectField label="Acceso" allLabel="Todas" value={filters.acceso} onChange={(value) => table.setFilter('acceso', value)} options={ACCESS_OPTIONS} />
          </FilterBar>

          <Toolbar
            end={
              <>
                <Button variant="outline" leftIcon={<RefreshCw />} loading={branches.fetching && !branches.loading} onClick={branches.reload}>
                  Actualizar
                </Button>
                <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
                  Exportar CSV
                </Button>
              </>
            }
          >
            {branches.data && (
              <span className="text-sm text-text-muted" data-testid="sucursales-resumen">
                {formatNumber(rows.length)} de {formatNumber(items.length)} sucursales · {scopeText}
              </span>
            )}
          </Toolbar>

          {canReport && (
            <Collapsible
              label="Ver comparativo por sucursal"
              openLabel="Ocultar comparativo por sucursal"
              icon={<BarChart3 />}
              description="Ventas, tickets, participación y stock de cada sucursal en un período, y lo que está en tránsito."
            >
              <BranchReport />
            </Collapsible>
          )}

          <DataTable
            caption="Sucursales de la empresa"
            columns={COLUMNS}
            rows={branches.data ? rows : undefined}
            rowKey={(item) => item.key}
            rowLabel={(item) => `la sucursal ${item.row.code} · ${item.row.name}`}
            loading={branches.loading}
            refreshing={branches.fetching && !branches.loading}
            error={branches.error}
            onRetry={branches.reload}
            operation="GetBranchesQuery"
            {...table.tableProps}
            onRowOpen={guardOpen(openDetail)}
            activeRowKey={detail?.open ? detail.item.key : null}
            rowActions={rowActions}
            empty={{
              title: items.length === 0 ? 'Todavía no hay sucursales' : 'Ninguna sucursal coincide con los filtros',
              icon: <Building2 />,
              action: (
                <Button variant="outline" onClick={table.clearFilters}>
                  Limpiar filtros
                </Button>
              ),
            }}
          />
        </TabPanel>
        <TabPanel id="stock">
          <ConsolidatedStockTab />
        </TabPanel>
      </Tabs>

      <SidePanel
        open={detail?.open ?? false}
        onClose={closeDetail}
        title={current ? `${current.row.code} · ${current.row.name}` : 'Sucursal'}
        description={current ? (current.row.isVisible ? 'Una de sus sucursales' : 'No es una de sus sucursales: sin datos de stock') : undefined}
        headerExtra={current && <StatusBadge status={current.state} statuses={BRANCH_STATES} />}
        footer={
          current && (
            <div className="flex flex-col gap-2">
              {canTransfers && (
                <Button leftIcon={<ArrowUpFromLine />} fullWidth to={transfersLink('origen', current.row.code)}>
                  Transferencias que salen
                </Button>
              )}
              {canTransfers && (
                <Button variant="outline" leftIcon={<ArrowDownToLine />} fullWidth to={transfersLink('destino', current.row.code)}>
                  Transferencias que llegan
                </Button>
              )}
              {canAssign && current.row.isActive && (
                <Button variant="outline" leftIcon={<Users />} fullWidth onClick={() => askMembers(current)}>
                  Usuarios de la sucursal
                </Button>
              )}
              {canUpdate && (
                <Button variant="outline" leftIcon={<Pencil />} fullWidth onClick={() => askEdit(current)}>
                  Editar
                </Button>
              )}
            </div>
          )
        }
      >
        {current && (
          <div className="space-y-4">
            {!current.row.isActive && <Alert tone="warning">Sucursal inactiva: no recibe transferencias ni se elige para operar.</Alert>}
            <DetailList
              items={[
                { label: 'Código', value: current.row.code },
                { label: 'Nombre', value: current.row.name },
                { label: 'Almacenes', value: current.row.warehouses.join(', ') || 'Sin almacén' },
                { label: 'Usuarios', value: current.row.users === 1 ? '1 usuario' : `${formatNumber(current.row.users)} usuarios` },
                { label: 'Stock valorizado', value: current.row.stockValue === null ? 'Sin acceso' : formatMoney(current.row.stockValue) },
                { label: 'Transferencias abiertas', value: `${formatNumber(current.row.transfersOut)} salen · ${formatNumber(current.row.transfersIn)} llegan` },
              ]}
            />
          </div>
        )}
      </SidePanel>

      <NewBranchDialog key={`nueva-${dialogSession}`} open={creating} existing={all} onClose={() => setCreating(false)} onCreated={branches.reload} />
      <EditBranchDialog key={`editar-${dialogSession}`} target={editing} all={all} onClose={() => setEditing(null)} onSaved={branches.reload} />
      <BranchUsersDialog key={`usuarios-${dialogSession}`} target={members} branches={all} onClose={() => setMembers(null)} onSaved={branches.reload} />
      <UserBranchesDialog key={`asignar-${dialogSession}`} open={assigning} branches={all} onClose={() => setAssigning(false)} onSaved={branches.reload} />
      <ConfirmDialog
        open={deactivating?.open ?? false}
        onClose={() => setDeactivating((shown) => (shown?.open ? { ...shown, open: false } : shown))}
        tone="danger"
        title={`¿Desactivar la sucursal ${deactivating?.item.row.code ?? ''}?`}
        message="Deja de recibir transferencias y no se elige para operar. Sus documentos, stock y movimientos se conservan, y se puede volver a activar."
        confirmLabel="Desactivar"
        onConfirm={confirmDeactivate}
        error={toggle.errorText ? plainMessage(toggle.errorText) : undefined}
      />
    </Page>
  );
}
