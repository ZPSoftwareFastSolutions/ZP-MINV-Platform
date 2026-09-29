// Módulo «Configuración» · sucursales del Padrón: cada sucursal de M-INV con su código de sucursal del Padrón (0 = casa
// matriz), el municipio y el teléfono que van en la factura (`SaveSiatBranchCommand`). Filtros en la dirección (con el
// prefijo `suc_`), exportar CSV y un diálogo por sucursal con validación por campo.

import { Building2, Download, Filter, MapPin, Save } from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import { useRpcCommand, useTableState } from '@/4-presentation/panel/hooks';
import { Button, DataTable, Dialog, FilterBar, Form, NumberField, SearchField, SelectField, StatusBadge, TextField, Toolbar, statusOptions, useNotify, type DataTableColumn } from '@/4-presentation/panel/kit';
import { exportCsv, formatNumber } from '@/4-presentation/panel/lib';
import {
  PADRON_CSV_COLUMNS,
  PADRON_FILTERS,
  PADRON_STATES,
  filterPadron,
  padronFormOf,
  padronPayload,
  padronProblems,
  padronStateOf,
  plainMessage,
  type PadronForm,
  type SiatBranchData,
} from './settings';

const COLUMNS: DataTableColumn<SiatBranchData>[] = [
  {
    id: 'sucursal',
    header: 'Sucursal',
    value: (branch) => branch.branchCode,
    card: 'title',
    cell: (branch) => (
      <span className="block min-w-40">
        <span className="block">{branch.branchCode}</span>
        <span className="block text-xs font-normal text-text-muted">{branch.branchName}</span>
      </span>
    ),
  },
  { id: 'codigo', header: 'Código del Padrón', align: 'end', value: (branch) => branch.siatCode },
  { id: 'municipio', header: 'Municipio', value: (branch) => branch.municipality },
  { id: 'telefono', header: 'Teléfono', value: (branch) => branch.phone },
  { id: 'estado', header: 'Estado', value: (branch) => PADRON_STATES[padronStateOf(branch)].label, cell: (branch) => <StatusBadge status={padronStateOf(branch)} statuses={PADRON_STATES} /> },
];

export interface PadronBranchesProps {
  branches: readonly SiatBranchData[];
  canSave: boolean;
  /** Sin la licencia de facturación: se ve, pero no se guarda. */
  locked: boolean;
  onSaved: () => void;
}

export function PadronBranches({ branches, canSave, locked, onSaved }: PadronBranchesProps) {
  const notify = useNotify();
  const table = useTableState({ prefix: 'suc_', filters: PADRON_FILTERS, sort: { column: 'sucursal', direction: 'asc' } });
  const filters = table.filters;
  const rows = useMemo(() => filterPadron(branches, filters), [branches, filters]);
  const [editing, setEditing] = useState<SiatBranchData | null>(null);

  const exportRows = () => {
    const file = exportCsv('sucursales-del-padron', PADRON_CSV_COLUMNS, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  return (
    <div className="space-y-4">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar sucursal" placeholder="Código, nombre o municipio" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Estado en el Padrón" allLabel="Todos" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(PADRON_STATES)} />
      </FilterBar>
      <Toolbar
        label="Acciones de las sucursales del Padrón"
        end={
          <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
            Exportar CSV
          </Button>
        }
      >
        <span className="text-sm text-text-muted" data-testid="padron-resumen">
          {formatNumber(branches.filter((branch) => branch.siatCode !== null).length)} de {formatNumber(branches.length)} sucursales con código del Padrón
        </span>
      </Toolbar>
      <DataTable
        caption="Sucursales del Padrón"
        columns={COLUMNS}
        rows={rows}
        rowKey={(branch) => branch.branchCode}
        rowLabel={(branch) => `la sucursal ${branch.branchCode}`}
        {...table.tableProps}
        paginate={false}
        rowActions={(branch) => [
          {
            label: branch.siatCode === null ? 'Asignar el código del Padrón' : 'Cambiar los datos del Padrón',
            icon: <MapPin />,
            onSelect: () => setEditing(branch),
            hidden: !canSave,
            disabled: locked,
            disabledReason: 'La empresa no tiene el módulo de facturación.',
          },
        ]}
        empty={{
          title: branches.length === 0 ? 'La empresa no tiene sucursales' : 'No hay sucursales con estos filtros',
          icon: <Building2 />,
          action:
            branches.length === 0 ? undefined : (
              <Button variant="outline" leftIcon={<Filter />} onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
        }}
      />
      <PadronDialog branch={editing} onClose={() => setEditing(null)} onSaved={onSaved} />
    </div>
  );
}

interface PadronDialogProps {
  branch: SiatBranchData | null;
  onClose: () => void;
  onSaved: () => void;
}

function PadronDialog({ branch, onClose, onSaved }: PadronDialogProps) {
  const formId = useId();
  const save = useRpcCommand('SaveSiatBranchCommand', { notifyError: false, success: (message) => plainMessage(message) || 'Sucursal guardada' });
  const [shown, setShown] = useState<SiatBranchData | null>(null);
  const [form, setForm] = useState<PadronForm>({ siatCode: null, municipality: '', phone: '' });
  const [touched, setTouched] = useState(false);
  const [wasOpen, setWasOpen] = useState(false);
  if ((branch !== null) !== wasOpen) {
    setWasOpen(branch !== null);
    if (branch) {
      setShown(branch);
      setForm(padronFormOf(branch));
      setTouched(false);
    }
  }
  const problems = padronProblems(form);

  const set = <K extends keyof PadronForm>(key: K, value: PadronForm[K]) => {
    setForm((current) => ({ ...current, [key]: value }));
    if (save.error) save.reset();
  };

  const close = () => {
    save.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (!shown || Object.keys(problems).length > 0) return;
    const outcome = await save.run(padronPayload(shown.branchCode, form));
    if (!outcome.ok) return;
    onSaved();
    close();
  };

  return (
    <Dialog
      open={branch !== null}
      onClose={close}
      dismissible={!save.sending}
      title="Sucursal del Padrón"
      description={shown ? `${shown.branchCode} · ${shown.branchName}` : undefined}
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={save.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Save />} loading={save.sending}>
            Guardar la sucursal
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={save.errorText} busy={save.sending}>
        <NumberField
          label="Código de sucursal en el Padrón"
          value={form.siatCode}
          onChange={(value) => set('siatCode', value)}
          error={touched ? problems.siatCode : undefined}
          hint="0 = casa matriz. Cada sucursal tiene su propio código."
          required
          data-autofocus
        />
        <TextField label="Municipio" value={form.municipality} onChange={(value) => set('municipality', value)} error={touched ? problems.municipality : undefined} hint="El que va en la factura." maxLength={25} required />
        <TextField label="Teléfono" type="tel" value={form.phone} onChange={(value) => set('phone', value)} error={touched ? problems.phone : undefined} maxLength={25} optional />
      </Form>
    </Dialog>
  );
}
