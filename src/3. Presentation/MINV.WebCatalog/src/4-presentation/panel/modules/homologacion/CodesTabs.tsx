// Módulo «Homologación» · pestañas «Unidades» (`SaveUnitHomologationCommand`) y «Medios de pago»
// (`SavePaymentMethodHomologationCommand`): cada unidad de medida y cada medio de pago de M-INV con su código del catálogo
// sincronizado del SIN, filtro «sin homologar» y búsqueda (en la dirección, prefijos `u_` y `m_`), exportar CSV y
// «Cambiar el código del SIN» en un diálogo (solo para quien configura la facturación).

import { CreditCard, Download, Pencil, Ruler } from 'lucide-react';
import { useMemo, useState } from 'react';
import { usePermissions, useRpcCommand, useTableState } from '@/4-presentation/panel/hooks';
import { DataTable, FilterBar, SearchField, SelectField, StatusBadge, Toolbar, Button, statusOptions, useNotify, type DataTableColumn } from '@/4-presentation/panel/kit';
import { exportCsv, formatNumber } from '@/4-presentation/panel/lib';
import { CodeDialog, type CodeDialogTarget } from './CodeDialog';
import {
  CODE_FILTERS,
  CODE_STATUSES,
  METHOD_CSV,
  UNIT_CSV,
  codeStatus,
  filterMethods,
  filterUnits,
  plainMessage,
  sinOptions,
  sinText,
  type HomologationData,
  type MethodRowData,
  type UnitRowData,
} from './homologation';

export interface CodesTabProps {
  view: HomologationData | undefined;
  loading: boolean;
  fetching: boolean;
  error: unknown;
  reload: () => void;
}

const UNIT_COLUMNS: DataTableColumn<UnitRowData>[] = [
  { id: 'codigo', header: 'Código', value: (row) => row.code },
  { id: 'nombre', header: 'Unidad', value: (row) => row.name, card: 'title' },
  { id: 'sin', header: 'Unidad del SIN', value: (row) => row.sinUnitCode, cell: (row) => sinText(row.sinUnitCode, row.sinUnitDescription) },
  { id: 'estado', header: 'Homologación', value: (row) => codeStatus(row.sinUnitCode), cell: (row) => <StatusBadge status={codeStatus(row.sinUnitCode)} statuses={CODE_STATUSES} /> },
];

const METHOD_COLUMNS: DataTableColumn<MethodRowData>[] = [
  { id: 'codigo', header: 'Código', value: (row) => row.code },
  { id: 'nombre', header: 'Medio de pago', value: (row) => row.name, card: 'title' },
  { id: 'sin', header: 'Método de pago del SIN', value: (row) => row.sinCode, cell: (row) => sinText(row.sinCode, row.sinDescription) },
  { id: 'estado', header: 'Homologación', value: (row) => codeStatus(row.sinCode), cell: (row) => <StatusBadge status={codeStatus(row.sinCode)} statuses={CODE_STATUSES} /> },
];

export function UnitsTab({ view, loading, fetching, error, reload }: CodesTabProps) {
  const notify = useNotify();
  const { canRun } = usePermissions();
  const table = useTableState({ prefix: 'u_', filters: CODE_FILTERS, sort: { column: 'codigo', direction: 'asc' } });
  const rows = useMemo(() => filterUnits(view?.units ?? [], table.filters), [view, table.filters]);
  const canEdit = canRun('SaveUnitHomologationCommand');
  const save = useRpcCommand('SaveUnitHomologationCommand', { notifyError: false });
  const [session, setSession] = useState(0);
  const [editing, setEditing] = useState<{ row: UnitRowData; target: CodeDialogTarget } | null>(null);

  const edit = (row: UnitRowData) => {
    save.reset();
    setSession((count) => count + 1);
    setEditing({
      row,
      target: {
        title: `Unidad del SIN · ${row.code}`,
        description: `${row.name}: la unidad con que se informa al SIN (catálogo UNIDAD_MEDIDA).`,
        label: 'Unidad del SIN',
        currentCode: row.sinUnitCode,
        options: sinOptions(view?.sinUnits ?? []),
      },
    });
  };

  return (
    <div className="space-y-4">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters} title="Filtros de las unidades">
        <SearchField label="Buscar unidad" placeholder="Código, nombre o código del SIN" value={table.filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Homologación de la unidad" allLabel="Todas" value={table.filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(CODE_STATUSES)} />
      </FilterBar>
      <Toolbar
        label="Acciones de las unidades"
        end={
          <Button
            variant="outline"
            leftIcon={<Download />}
            disabled={rows.length === 0}
            onClick={() => {
              const file = exportCsv('homologacion-unidades', UNIT_CSV, rows);
              notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
            }}
          >
            Exportar CSV
          </Button>
        }
      />
      <DataTable
        caption="Homologación de unidades"
        columns={UNIT_COLUMNS}
        rows={view ? rows : undefined}
        rowKey={(row) => row.unitId}
        rowLabel={(row) => `la unidad ${row.code}`}
        loading={loading}
        refreshing={fetching && !loading}
        error={error}
        onRetry={reload}
        operation="GetHomologationQuery"
        {...table.tableProps}
        rowActions={(row) => [{ label: 'Cambiar la unidad del SIN', icon: <Pencil />, onSelect: () => edit(row), hidden: !canEdit }]}
        empty={{ title: 'No hay unidades con estos filtros', icon: <Ruler /> }}
      />
      <CodeDialog
        key={`unidad-${session}`}
        target={editing?.target ?? null}
        sending={save.sending}
        errorText={save.errorText}
        onClose={() => setEditing(null)}
        onSubmit={async (code) => {
          if (!editing) return { ok: false };
          const outcome = await save.run({ unitCode: editing.row.code, sinUnitCode: code });
          if (outcome.ok) {
            notify.success('Unidad homologada', `${editing.row.code} → ${plainMessage(outcome.result) || code}`);
            reload();
          }
          return outcome;
        }}
      />
    </div>
  );
}

export function MethodsTab({ view, loading, fetching, error, reload }: CodesTabProps) {
  const notify = useNotify();
  const { canRun } = usePermissions();
  const table = useTableState({ prefix: 'm_', filters: CODE_FILTERS, sort: { column: 'nombre', direction: 'asc' } });
  const rows = useMemo(() => filterMethods(view?.paymentMethods ?? [], table.filters), [view, table.filters]);
  const canEdit = canRun('SavePaymentMethodHomologationCommand');
  const save = useRpcCommand('SavePaymentMethodHomologationCommand', { notifyError: false });
  const [session, setSession] = useState(0);
  const [editing, setEditing] = useState<{ row: MethodRowData; target: CodeDialogTarget } | null>(null);

  const edit = (row: MethodRowData) => {
    save.reset();
    setSession((count) => count + 1);
    setEditing({
      row,
      target: {
        title: `Método de pago del SIN · ${row.name}`,
        description: `${row.code}: el método con que se informa al SIN (catálogo TIPO_METODO_PAGO).`,
        label: 'Método de pago del SIN',
        currentCode: row.sinCode,
        options: sinOptions(view?.sinPaymentMethods ?? []),
      },
    });
  };

  return (
    <div className="space-y-4">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters} title="Filtros de los medios de pago">
        <SearchField label="Buscar medio de pago" placeholder="Código, nombre o código del SIN" value={table.filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Homologación del medio de pago" allLabel="Todos" value={table.filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(CODE_STATUSES)} />
      </FilterBar>
      <Toolbar
        label="Acciones de los medios de pago"
        end={
          <Button
            variant="outline"
            leftIcon={<Download />}
            disabled={rows.length === 0}
            onClick={() => {
              const file = exportCsv('homologacion-medios-de-pago', METHOD_CSV, rows);
              notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
            }}
          >
            Exportar CSV
          </Button>
        }
      />
      <DataTable
        caption="Homologación de medios de pago"
        columns={METHOD_COLUMNS}
        rows={view ? rows : undefined}
        rowKey={(row) => row.paymentMethodId}
        rowLabel={(row) => `el medio de pago ${row.name}`}
        loading={loading}
        refreshing={fetching && !loading}
        error={error}
        onRetry={reload}
        operation="GetHomologationQuery"
        {...table.tableProps}
        rowActions={(row) => [{ label: 'Cambiar el método del SIN', icon: <Pencil />, onSelect: () => edit(row), hidden: !canEdit }]}
        empty={{ title: 'No hay medios de pago con estos filtros', icon: <CreditCard /> }}
      />
      <CodeDialog
        key={`medio-${session}`}
        target={editing?.target ?? null}
        sending={save.sending}
        errorText={save.errorText}
        onClose={() => setEditing(null)}
        onSubmit={async (code) => {
          if (!editing) return { ok: false };
          const outcome = await save.run({ paymentMethodCode: editing.row.code, sinCode: code });
          if (outcome.ok) {
            notify.success('Medio de pago homologado', `${editing.row.name} → ${plainMessage(outcome.result) || code}`);
            reload();
          }
          return outcome;
        }}
      />
    </div>
  );
}
