// Módulo «Configuración» · actividades económicas de la empresa según el SIN (`GetSiatActivitiesQuery`, llegan con la
// sincronización de catálogos): código, descripción, tipo, documentos sector y si siguen vigentes. Sirven para revisar
// la sincronización y la homologación. Solo lectura, con filtros en la dirección (prefijo `act_`) y exportar CSV.

import { Download, Filter, RefreshCw, ScrollText } from 'lucide-react';
import { useMemo } from 'react';
import { useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import { Button, DataTable, FilterBar, SearchField, SelectField, StatusBadge, Toolbar, useNotify, type DataTableColumn } from '@/4-presentation/panel/kit';
import { exportCsv, formatNumber } from '@/4-presentation/panel/lib';
import { ACTIVITY_CSV_COLUMNS, ACTIVITY_FILTERS, CURRENT_OPTIONS, filterActivities, sectorsText, type EconomicActivity } from './settings';

const COLUMNS: DataTableColumn<EconomicActivity>[] = [
  { id: 'codigo', header: 'Código', value: (activity) => activity.code, className: 'whitespace-nowrap font-mono' },
  { id: 'descripcion', header: 'Descripción', value: (activity) => activity.description, card: 'title', className: 'min-w-56' },
  { id: 'tipo', header: 'Tipo', value: (activity) => activity.activityType },
  { id: 'sectores', header: 'Documentos sector', value: (activity) => sectorsText(activity), sortable: false },
  {
    id: 'vigente',
    header: 'Vigente',
    value: (activity) => activity.isCurrent,
    cell: (activity) => <StatusBadge tone={activity.isCurrent ? 'success' : 'neutral'}>{activity.isCurrent ? 'Vigente' : 'Retirada'}</StatusBadge>,
  },
];

export function ActivitiesList() {
  const notify = useNotify();
  const activities = useRpcQuery('GetSiatActivitiesQuery', {});
  const table = useTableState({ prefix: 'act_', filters: ACTIVITY_FILTERS, sort: { column: 'codigo', direction: 'asc' } });
  const filters = table.filters;
  const all = useMemo(() => activities.data ?? [], [activities.data]);
  const rows = useMemo(() => filterActivities(all, filters), [all, filters]);

  const exportRows = () => {
    const file = exportCsv('actividades-economicas-siat', ACTIVITY_CSV_COLUMNS, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  return (
    <div className="space-y-4">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar actividad" placeholder="Código o descripción" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Vigencia" allLabel="Todas" value={filters.vigente} onChange={(value) => table.setFilter('vigente', value)} options={CURRENT_OPTIONS} />
      </FilterBar>
      <Toolbar
        label="Acciones de las actividades"
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={activities.fetching && !activities.loading} onClick={activities.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {activities.data && (
          <span className="text-sm text-text-muted" data-testid="actividades-resumen">
            {formatNumber(rows.length)} de {formatNumber(all.length)} actividades
          </span>
        )}
      </Toolbar>
      <DataTable
        caption="Actividades económicas del SIN"
        columns={COLUMNS}
        rows={activities.data ? rows : undefined}
        rowKey={(activity) => activity.code}
        loading={activities.loading}
        refreshing={activities.fetching && !activities.loading}
        error={activities.error}
        onRetry={activities.reload}
        operation="GetSiatActivitiesQuery"
        {...table.tableProps}
        empty={{
          title: all.length === 0 ? 'Todavía no hay actividades' : 'No hay actividades con estos filtros',
          description: all.length === 0 ? 'Llegan al sincronizar los catálogos del SIN.' : undefined,
          icon: <ScrollText />,
          action:
            all.length === 0 ? undefined : (
              <Button variant="outline" leftIcon={<Filter />} onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
        }}
      />
    </div>
  );
}
