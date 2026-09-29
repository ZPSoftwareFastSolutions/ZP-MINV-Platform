// Módulo «Usuarios» · pestaña «Usuarios»: la lista de `GetUsersQuery` con filtros en la dirección (tipo —Personal o
// Clientes web—, rol, sucursal y estado con listas desplegables, y búsqueda), tabla ordenable y paginada, acciones por
// fila, detalle lateral (con lo que puede hacer su rol), exportar CSV y el resumen PLEGADO «Ver resumen de los usuarios».
// Los comandos los abre la pantalla (UsersPage): aquí solo se ofrecen, a quien puede ejecutarlos.

import { BarChart3, Building2, Download, Eye, Filter, History, KeyRound, Pencil, RefreshCw, UserCheck, UserPlus, Users, UserX } from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import type { RpcResponseOf } from '@/4-presentation/app/contract';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useTableState, type RpcQuery } from '@/4-presentation/panel/hooks';
import {
  Button,
  Collapsible,
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
  type DataTableColumn,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatNumber } from '@/4-presentation/panel/lib';
import { RoleAbilities } from './RoleAbilities';
import { UsersSummary } from './UsersSummary';
import {
  KIND_LABELS,
  USER_CSV_COLUMNS,
  USER_FILTERS,
  USER_STATUSES,
  branchFilterOptions,
  filterUsers,
  kindOptions,
  lastAccessText,
  passwordText,
  roleFilterOptions,
  roleViewOf,
  statusLabel,
  type BranchChoice,
  type RoleChoice,
  type RolesData,
  type UserItem,
} from './users';

/** Columnas de la tabla: fuera del componente (no se vuelven a crear en cada dibujo). */
const COLUMNS: DataTableColumn<UserItem>[] = [
  {
    id: 'usuario',
    header: 'Usuario',
    value: (item) => item.record.name,
    card: 'title',
    cell: (item) => (
      <span className="block min-w-44">
        <span className="block">
          {item.record.name}
          {item.isSelf && <span className="font-normal text-text-muted"> (usted)</span>}
        </span>
        <span className="block text-xs font-normal text-text-muted">{item.record.email}</span>
      </span>
    ),
  },
  {
    id: 'rol',
    header: 'Rol',
    value: (item) => item.roleText,
    cell: (item) => <StatusBadge tone={item.kind === 'clientes' ? 'info' : 'accent'}>{item.roleText || 'Sin rol'}</StatusBadge>,
  },
  { id: 'sucursales', header: 'Sucursales', value: (item) => item.branchesText, className: 'min-w-32' },
  { id: 'estado', header: 'Estado', value: (item) => statusLabel(item.status), cell: (item) => <StatusBadge status={item.status} statuses={USER_STATUSES} /> },
  {
    id: 'ingreso',
    header: 'Último ingreso',
    value: (item) => (item.record.lastAccess ? new Date(item.record.lastAccess) : null),
    cell: (item) => lastAccessText(item.record),
    className: 'whitespace-nowrap',
  },
];

export interface UsersListProps {
  users: RpcQuery<RpcResponseOf<'GetUsersQuery'>>;
  items: readonly UserItem[];
  roles: RolesData | undefined;
  roleChoices: readonly RoleChoice[];
  branches: readonly BranchChoice[];
  canSave: boolean;
  canReset: boolean;
  canAssign: boolean;
  onNew: () => void;
  onEdit: (item: UserItem) => void;
  onReset: (item: UserItem) => void;
  onAssign: (item: UserItem) => void;
  onToggle: (item: UserItem) => void;
}

export function UsersList({ users, items, roles, roleChoices, branches, canSave, canReset, canAssign, onNew, onEdit, onReset, onAssign, onToggle }: UsersListProps) {
  const notify = useNotify();
  const navigate = useNavigate();
  const { can } = usePermissions();
  const canAudit = can('iam.audit.view');
  const abilitiesTitleId = useId();
  const table = useTableState({ filters: USER_FILTERS, sort: { column: 'usuario', direction: 'asc' } });
  const filters = table.filters;
  const rows = useMemo(() => filterUsers(items, filters), [items, filters]);

  // Detalle abierto: se guarda la clave y se lee la fila de la lista (se actualiza sola después de un comando).
  const [detail, setDetail] = useState<{ item: UserItem; open: boolean } | null>(null);
  const current = detail ? (items.find((item) => item.key === detail.item.key) ?? detail.item) : null;
  const abilities = current ? roleViewOf(roles, current.roleCode) : null;

  const openDetail = (item: UserItem) => setDetail({ item, open: true });
  const closeDetail = () => setDetail((value) => (value?.open ? { ...value, open: false } : value));
  const activityOf = (item: UserItem) => ROUTES.panelModule(`actividad?usuario=${encodeURIComponent(item.record.email)}`);
  const canToggle = (item: UserItem) => canSave && !item.isSelf && item.roleCode !== '';
  const run = (action: (item: UserItem) => void, item: UserItem) => {
    closeDetail();
    action(item);
  };

  const exportRows = () => {
    const file = exportCsv('usuarios', USER_CSV_COLUMNS, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const locked = rows.filter((row) => row.status === 'bloqueado').length;

  return (
    <div className="space-y-5">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Nombre, correo o rol" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Tipo" allLabel="Todos los tipos" value={filters.tipo} onChange={(value) => table.setFilter('tipo', value)} options={kindOptions(items)} />
        <SelectField label="Rol" allLabel="Todos los roles" value={filters.rol} onChange={(value) => table.setFilter('rol', value)} options={roleFilterOptions(roleChoices)} />
        <SelectField
          label="Sucursal"
          allLabel="Todas las sucursales"
          value={filters.sucursal}
          onChange={(value) => table.setFilter('sucursal', value)}
          options={branchFilterOptions(branches)}
        />
        <SelectField label="Estado" allLabel="Todos los estados" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(USER_STATUSES)} />
      </FilterBar>

      <Toolbar
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={users.fetching && !users.loading} onClick={users.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {users.data && (
          <span className="text-sm text-text-muted" data-testid="usuarios-resumen">
            {formatNumber(rows.length)} de {formatNumber(items.length)} usuarios · {formatNumber(locked)} bloqueados
          </span>
        )}
      </Toolbar>

      <DataTable
        caption="Usuarios"
        columns={COLUMNS}
        rows={users.data ? rows : undefined}
        rowKey={(row) => row.key}
        rowLabel={(row) => `${row.record.name} (${row.record.email})`}
        loading={users.loading}
        refreshing={users.fetching && !users.loading}
        error={users.error}
        onRetry={users.reload}
        operation="GetUsersQuery"
        {...table.tableProps}
        onRowOpen={openDetail}
        activeRowKey={detail?.open ? detail.item.key : null}
        rowActions={(row) => [
          { label: 'Ver detalle', icon: <Eye />, onSelect: () => openDetail(row) },
          { label: 'Editar', icon: <Pencil />, onSelect: () => onEdit(row), hidden: !canSave },
          { label: 'Asignar sucursales', icon: <Building2 />, onSelect: () => onAssign(row), hidden: !canAssign || row.kind !== 'personal' },
          { label: 'Restablecer la contraseña', icon: <KeyRound />, onSelect: () => onReset(row), hidden: !canReset },
          { label: 'Ver su actividad', icon: <History />, onSelect: () => navigate(activityOf(row)), hidden: !canAudit },
          {
            label: row.record.isActive ? 'Desactivar' : 'Activar',
            icon: row.record.isActive ? <UserX /> : <UserCheck />,
            tone: row.record.isActive ? 'danger' : 'default',
            onSelect: () => onToggle(row),
            hidden: !canToggle(row),
          },
        ]}
        empty={{
          title: items.length === 0 ? 'Todavía no hay usuarios' : 'No hay usuarios con estos filtros',
          description: items.length === 0 ? undefined : 'Pruebe con otro tipo, rol o estado, o limpie los filtros.',
          icon: <Users />,
          action:
            items.length === 0 ? (
              canSave && (
                <Button leftIcon={<UserPlus />} onClick={onNew}>
                  Nuevo usuario
                </Button>
              )
            ) : (
              <Button variant="outline" leftIcon={<Filter />} onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
        }}
      />

      <Collapsible
        label="Ver resumen de los usuarios"
        openLabel="Ocultar el resumen"
        icon={<BarChart3 />}
        description="Personal activo, bloqueados, contraseñas por cambiar, ingresos de hoy y personal por rol."
      >
        <UsersSummary users={users.data} />
      </Collapsible>

      <SidePanel
        open={detail?.open ?? false}
        onClose={closeDetail}
        title={current?.record.name ?? 'Usuario'}
        description={current?.record.email}
        headerExtra={current && <StatusBadge status={current.status} statuses={USER_STATUSES} />}
        footer={
          current && (
            <div className="flex flex-col gap-2">
              {canSave && (
                <Button leftIcon={<Pencil />} fullWidth onClick={() => run(onEdit, current)}>
                  Editar
                </Button>
              )}
              {canAssign && current.kind === 'personal' && (
                <Button variant="outline" leftIcon={<Building2 />} fullWidth onClick={() => run(onAssign, current)}>
                  Asignar sucursales
                </Button>
              )}
              {canReset && (
                <Button variant="outline" leftIcon={<KeyRound />} fullWidth onClick={() => run(onReset, current)}>
                  Restablecer la contraseña
                </Button>
              )}
              {canAudit && (
                <Button variant="outline" leftIcon={<History />} fullWidth to={activityOf(current)}>
                  Ver su actividad
                </Button>
              )}
              {canToggle(current) && (
                <Button
                  variant={current.record.isActive ? 'danger' : 'outline'}
                  leftIcon={current.record.isActive ? <UserX /> : <UserCheck />}
                  fullWidth
                  onClick={() => run(onToggle, current)}
                >
                  {current.record.isActive ? 'Desactivar' : 'Activar'}
                </Button>
              )}
            </div>
          )
        }
      >
        {current && (
          <div className="space-y-5">
            <DetailList
              items={[
                { label: 'Tipo', value: KIND_LABELS[current.kind] },
                { label: 'Rol', value: current.roleText || 'Sin rol' },
                { label: 'Sucursales', value: current.branchesText },
                { label: 'Último ingreso', value: lastAccessText(current.record) },
                { label: 'Contraseña', value: passwordText(current.record) },
                { label: 'Intentos fallidos', value: formatNumber(current.record.failedAttempts) },
              ]}
            />
            {abilities && (
              <section aria-labelledby={abilitiesTitleId} className="space-y-3">
                <h3 id={abilitiesTitleId} className="text-sm font-semibold text-text">
                  Lo que puede hacer ({formatNumber(abilities.granted)} de {formatNumber(abilities.total)} funciones)
                </h3>
                <p className="text-sm text-text-muted">{abilities.description}</p>
                <RoleAbilities view={abilities} level={4} />
              </section>
            )}
          </div>
        )}
      </SidePanel>
    </div>
  );
}
