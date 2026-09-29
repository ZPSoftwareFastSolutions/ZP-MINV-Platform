// Módulo «Usuarios» · pestaña «Roles y permisos», de SOLO LECTURA (`GetRolesQuery`): explica en palabras qué puede hacer
// cada rol (sus funciones agrupadas por área, con los nombres del servidor) y qué no; filtros por rol, área y búsqueda
// («¿quién puede anular facturas?») en la dirección; «Ver sus usuarios» lleva a la lista filtrada por ese rol; exportar
// la matriz rol × función a CSV. Los roles y sus permisos los define el sistema: para que alguien haga otra cosa, se le
// cambia el rol.

import { Download, Filter, RefreshCw, ShieldCheck, Users } from 'lucide-react';
import { useMemo } from 'react';
import { useTableState, type RpcQuery } from '@/4-presentation/panel/hooks';
import {
  Alert,
  Button,
  Collapsible,
  EmptyState,
  ErrorState,
  FilterBar,
  LoadingState,
  SearchField,
  Section,
  SelectField,
  StatusBadge,
  Toolbar,
  useNotify,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatNumber } from '@/4-presentation/panel/lib';
import { PermissionList, RoleAbilities } from './RoleAbilities';
import { AREAS, ROLE_FILTERS, matchingPermissions, matrixCsvColumns, roleViews, type RoleView, type RolesData } from './users';

export interface RolesPanelProps {
  roles: RpcQuery<RolesData>;
  /** Abre la lista de usuarios filtrada por ese rol. */
  onShowUsers: (roleCode: string) => void;
}

export function RolesPanel({ roles, onShowUsers }: RolesPanelProps) {
  const notify = useNotify();
  const table = useTableState({ filters: ROLE_FILTERS });
  const filters = table.filters;
  const views = useMemo(() => (roles.data ? roleViews(roles.data, filters) : []), [roles.data, filters]);
  const filtered = filters.q !== '' || filters.area !== '';

  if (roles.error && !roles.data) return <ErrorState error={roles.error} operation="GetRolesQuery" onRetry={roles.reload} retrying={roles.fetching} />;

  const exportMatrix = () => {
    if (!roles.data) return;
    const shown = roles.data.roles.filter((role) => !filters.rol || role.code === filters.rol);
    const rows = matchingPermissions(roles.data, filters);
    const file = exportCsv('roles-y-permisos', matrixCsvColumns(shown), rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} funciones).`);
  };

  return (
    <div className="space-y-5">
      <Alert tone="info" title="Solo lectura">
        Cada rol trae su conjunto de funciones. Para que una persona haga otras tareas, cámbiele el rol en la pestaña «Usuarios».
      </Alert>

      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar una función" placeholder="Por ejemplo: anular facturas" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField
          label="Rol"
          allLabel="Todos los roles"
          value={filters.rol}
          onChange={(value) => table.setFilter('rol', value)}
          options={(roles.data?.roles ?? []).map((role) => ({ value: role.code, label: role.name }))}
        />
        <SelectField
          label="Área"
          allLabel="Todas las áreas"
          value={filters.area}
          onChange={(value) => table.setFilter('area', value)}
          options={AREAS.map((area) => ({ value: area.key, label: area.label }))}
        />
      </FilterBar>

      <Toolbar
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={roles.fetching && !roles.loading} onClick={roles.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={!roles.data} onClick={exportMatrix}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {roles.data && (
          <span className="text-sm text-text-muted" data-testid="roles-resumen">
            {formatNumber(views.length)} de {formatNumber(roles.data.roles.length)} roles · {formatNumber(roles.data.permissions.length)} funciones en el sistema
          </span>
        )}
      </Toolbar>

      {roles.loading || !roles.data ? (
        <LoadingState label="Cargando los roles…" rows={3} />
      ) : views.length === 0 ? (
        <EmptyState icon={<ShieldCheck />} title="No hay roles con estos filtros" description="Limpie los filtros para ver todos los roles.">
          <Button variant="outline" leftIcon={<Filter />} onClick={table.clearFilters}>
            Limpiar filtros
          </Button>
        </EmptyState>
      ) : (
        <div className="grid grid-cols-1 gap-4 xl:grid-cols-2" data-testid="roles">
          {views.map((view) => (
            <RoleCard key={view.code} view={view} filtered={filtered} onShowUsers={onShowUsers} />
          ))}
        </div>
      )}
    </div>
  );
}

function RoleCard({ view, filtered, onShowUsers }: { view: RoleView; filtered: boolean; onShowUsers: (code: string) => void }) {
  return (
    <Section
      title={view.name}
      description={view.description}
      actions={
        <Button variant="outline" leftIcon={<Users />} onClick={() => onShowUsers(view.code)}>
          Ver sus usuarios ({formatNumber(view.users)})
        </Button>
      }
    >
      <div className="space-y-4">
        <div className="flex flex-wrap items-center gap-2 text-sm text-text-muted">
          <StatusBadge tone="accent">
            {formatNumber(view.granted)} de {formatNumber(view.total)} funciones
          </StatusBadge>
          <span>{view.users === 1 ? '1 usuario con este rol' : `${formatNumber(view.users)} usuarios con este rol`}</span>
        </div>
        <RoleAbilities view={view} level={3} />
        {view.missing.length > 0 && (
          <Collapsible
            level={3}
            label={filtered ? `Ver lo que no puede (${formatNumber(view.missing.length)} de las buscadas)` : `Ver lo que no puede hacer (${formatNumber(view.missing.length)})`}
            openLabel="Ocultar lo que no puede hacer"
          >
            <PermissionList items={view.missing} granted={false} />
          </Collapsible>
        )}
      </div>
    </Section>
  );
}
