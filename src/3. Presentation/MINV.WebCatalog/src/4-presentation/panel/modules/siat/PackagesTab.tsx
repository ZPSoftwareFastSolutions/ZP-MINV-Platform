// Módulo «Estado del SIAT» · pestaña «Paquetes» (`GetFiscalPackagesQuery`): los paquetes de contingencia enviados al SIN
// al recuperar la conexión, con su validación, código de recepción y los mensajes del SIN (en la fila desplegable). Se
// pueden ver los de UN evento (`?p_evento=<id>`, desde «Eventos»: lo filtra el servidor); estado y sucursal los filtra la
// página. Filtros en la dirección con el prefijo `p_`.

import { Download, Package, RefreshCw } from 'lucide-react';
import { useMemo } from 'react';
import { useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import { Alert, Button, DataTable, FilterBar, SelectField, StatusBadge, Toolbar, statusOptions, useNotify, type DataTableColumn } from '@/4-presentation/panel/kit';
import { exportCsv, formatDateTime, formatNumber } from '@/4-presentation/panel/lib';
import { PACKAGE_CSV, PACKAGE_FILTERS, PACKAGE_STATUSES, branchOptions, filterPackages, placeText, sectorText, type PackageData } from './siat';

const COLUMNS: DataTableColumn<PackageData>[] = [
  { id: 'enviado', header: 'Enviado', value: (row) => new Date(row.sentAt), cell: (row) => formatDateTime(row.sentAt), className: 'whitespace-nowrap', card: 'title' },
  { id: 'lugar', header: 'Lugar', value: (row) => placeText(row), className: 'whitespace-nowrap' },
  { id: 'documento', header: 'Documento', value: (row) => sectorText(row.documentSector) },
  {
    id: 'documentos',
    header: 'Documentos',
    align: 'end',
    value: (row) => row.documents,
    cell: (row) => (
      <span className="block">
        {formatNumber(row.documents)}
        {row.cafc && <span className="block text-xs text-text-muted">CAFC {row.cafc}</span>}
      </span>
    ),
  },
  { id: 'validado', header: 'Validado', value: (row) => (row.validatedAt ? new Date(row.validatedAt) : null), cell: (row) => (row.validatedAt ? formatDateTime(row.validatedAt) : '—') },
  {
    id: 'estado',
    header: 'Estado',
    value: (row) => row.status,
    cell: (row) => (
      <span className="flex flex-col items-start gap-1">
        <StatusBadge status={row.status} statuses={PACKAGE_STATUSES} />
        {row.lastSiatCode != null && <span className="text-xs text-text-muted">código {row.lastSiatCode}</span>}
      </span>
    ),
  },
  { id: 'recepcion', header: 'Recepción del SIN', value: (row) => row.receptionCode },
];

export function PackagesTab() {
  const notify = useNotify();
  const table = useTableState({ prefix: 'p_', filters: PACKAGE_FILTERS, sort: { column: 'enviado', direction: 'desc' } });
  const filters = table.filters;
  const packages = useRpcQuery('GetFiscalPackagesQuery', { eventId: filters.evento || null });
  const rows = useMemo(() => filterPackages(packages.data ?? [], filters), [packages.data, filters]);
  const branches = useMemo(() => branchOptions(packages.data ?? []), [packages.data]);

  const exportRows = () => {
    const file = exportCsv('paquetes-contingencia', PACKAGE_CSV, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  return (
    <div className="space-y-4">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters} title="Filtros de los paquetes">
        <SelectField label="Estado del paquete" allLabel="Todos los estados" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(PACKAGE_STATUSES)} />
        <SelectField label="Sucursal del paquete" allLabel="Todas las sucursales" value={filters.sucursal} onChange={(value) => table.setFilter('sucursal', value)} options={branches} />
      </FilterBar>
      {filters.evento && (
        <Alert
          tone="info"
          title="Paquetes de un evento"
          actions={
            <Button variant="outline" onClick={() => table.setFilter('evento', '')}>
              Ver todos los paquetes
            </Button>
          }
        >
          Se muestran solo los paquetes del evento elegido en «Eventos».
        </Alert>
      )}
      <Toolbar
        label="Acciones de los paquetes"
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={packages.fetching && !packages.loading} onClick={packages.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {packages.data && (
          <span className="text-sm text-text-muted" data-testid="paquetes-resumen">
            {formatNumber(rows.length)} de {formatNumber(packages.data.length)} paquetes
          </span>
        )}
      </Toolbar>
      <DataTable
        caption="Paquetes de contingencia"
        columns={COLUMNS}
        rows={packages.data ? rows : undefined}
        rowKey={(row) => row.id}
        rowLabel={(row) => `el paquete de ${placeText(row)} enviado el ${formatDateTime(row.sentAt)}`}
        loading={packages.loading}
        refreshing={packages.fetching && !packages.loading}
        error={packages.error}
        onRetry={packages.reload}
        operation="GetFiscalPackagesQuery"
        {...table.tableProps}
        renderExpanded={(row) => (
          <p className="text-sm whitespace-pre-line break-words text-text-muted">{row.messages ? `Mensajes del SIN: ${row.messages}` : 'El SIN no dejó mensajes para este paquete.'}</p>
        )}
        empty={{
          title: filters.evento ? 'Este evento no tiene paquetes' : 'Todavía no se enviaron paquetes',
          description: 'Los paquetes se arman solos al recuperar la conexión con el SIN.',
          icon: <Package />,
        }}
      />
    </div>
  );
}
