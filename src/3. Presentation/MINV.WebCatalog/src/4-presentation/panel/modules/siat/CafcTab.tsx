// Módulo «Estado del SIAT» · pestaña «Talonarios CAFC» (`GetContingencyCodesQuery`): los talonarios de facturas de
// contingencia de cada sucursal, con su rango, cuántos números se usaron, vencimiento y estado; «Registrar talonario»
// (`RegisterContingencyCodeCommand`) para quien gestiona las contingencias. Filtros en la dirección con el prefijo `t_`.

import { BookOpen, Download, Plus, RefreshCw } from 'lucide-react';
import { useMemo } from 'react';
import { useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import { Button, DataTable, FilterBar, SelectField, StatusBadge, Toolbar, statusOptions, useNotify, type DataTableColumn } from '@/4-presentation/panel/kit';
import { exportCsv, formatNumber } from '@/4-presentation/panel/lib';
import { CAFC_CSV, CAFC_FILTERS, CAFC_STATUSES, SECTOR_OPTIONS, SECTOR_PURCHASE_SALE, branchOptions, cafcUsage, filterCafcs, formatFiscalTime, type CafcData } from './siat';

const COLUMNS: DataTableColumn<CafcData>[] = [
  { id: 'codigo', header: 'Código CAFC', value: (row) => row.code, card: 'title' },
  { id: 'sucursal', header: 'Sucursal', value: (row) => row.branchCode },
  { id: 'documento', header: 'Documento', value: (row) => (row.documentSector === SECTOR_PURCHASE_SALE ? 'Factura compra venta' : 'Nota crédito-débito') },
  { id: 'rango', header: 'Números', value: (row) => row.numberFrom, cell: (row) => `N° ${row.numberFrom} a ${row.numberTo}` },
  { id: 'usados', header: 'Usados', value: (row) => row.used, cell: (row) => cafcUsage(row) },
  { id: 'vence', header: 'Vence', value: (row) => row.validUntil, cell: (row) => (row.validUntil ? formatFiscalTime(row.validUntil) : 'Sin vencimiento') },
  {
    id: 'estado',
    header: 'Estado',
    value: (row) => (row.isActive ? 'Activo' : 'Inactivo'),
    cell: (row) => <StatusBadge status={row.isActive ? 'active' : 'inactive'} statuses={CAFC_STATUSES} />,
  },
];

export interface CafcTabProps {
  canRegister: boolean;
  onRegister: () => void;
}

export function CafcTab({ canRegister, onRegister }: CafcTabProps) {
  const notify = useNotify();
  const table = useTableState({ prefix: 't_', filters: CAFC_FILTERS, sort: { column: 'sucursal', direction: 'asc' } });
  const filters = table.filters;
  // Al registrar un talonario, la pantalla vuelve a montar la pestaña (le cambia la `key`) y la lista se vuelve a pedir.
  const cafcs = useRpcQuery('GetContingencyCodesQuery', {});
  const rows = useMemo(() => filterCafcs(cafcs.data ?? [], filters), [cafcs.data, filters]);
  const branches = useMemo(() => branchOptions(cafcs.data ?? []), [cafcs.data]);

  const exportRows = () => {
    const file = exportCsv('talonarios-cafc', CAFC_CSV, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  return (
    <div className="space-y-4">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters} title="Filtros de los talonarios">
        <SelectField label="Estado del talonario" allLabel="Activos e inactivos" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(CAFC_STATUSES)} />
        <SelectField label="Sucursal del talonario" allLabel="Todas las sucursales" value={filters.sucursal} onChange={(value) => table.setFilter('sucursal', value)} options={branches} />
        <SelectField label="Documento del talonario" allLabel="Facturas y notas" value={filters.sector} onChange={(value) => table.setFilter('sector', value)} options={SECTOR_OPTIONS} />
      </FilterBar>
      <Toolbar
        label="Acciones de los talonarios"
        end={
          <>
            {canRegister && (
              <Button leftIcon={<Plus />} onClick={onRegister}>
                Registrar talonario
              </Button>
            )}
            <Button variant="outline" leftIcon={<RefreshCw />} loading={cafcs.fetching && !cafcs.loading} onClick={cafcs.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {cafcs.data && (
          <span className="text-sm text-text-muted" data-testid="talonarios-resumen">
            {formatNumber(rows.length)} de {formatNumber(cafcs.data.length)} talonarios
          </span>
        )}
      </Toolbar>
      <DataTable
        caption="Talonarios CAFC"
        columns={COLUMNS}
        rows={cafcs.data ? rows : undefined}
        rowKey={(row) => row.id}
        rowLabel={(row) => `el talonario ${row.code}`}
        loading={cafcs.loading}
        refreshing={cafcs.fetching && !cafcs.loading}
        error={cafcs.error}
        onRetry={cafcs.reload}
        operation="GetContingencyCodesQuery"
        {...table.tableProps}
        empty={{
          title: (cafcs.data ?? []).length === 0 ? 'Todavía no hay talonarios CAFC' : 'No hay talonarios con estos filtros',
          description: 'El SIN entrega el CAFC para facturar a mano durante una contingencia manual.',
          icon: <BookOpen />,
          action: canRegister ? (
            <Button leftIcon={<Plus />} onClick={onRegister}>
              Registrar talonario
            </Button>
          ) : undefined,
        }}
      />
    </div>
  );
}
