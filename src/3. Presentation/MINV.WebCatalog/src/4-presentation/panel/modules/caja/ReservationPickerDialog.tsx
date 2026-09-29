// Módulo «Caja» · «Vender una reserva»: escribir (o escanear) el número de la reserva o cotización, o elegirla de la
// lista de las VIGENTES (`GetPcBuildsQuery`, como «Desde armado» del escritorio): compras y armados, de la tienda web y
// del mostrador, con filtros, búsqueda y exportación. Al elegir, la caja la carga (`?reserva=<NÚMERO>` en la dirección).

import { Download, PackageCheck, PackageSearch } from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import {
  Button,
  DataTable,
  Dialog,
  FilterBar,
  Form,
  SearchField,
  SelectField,
  StatusBadge,
  TextField,
  Toolbar,
  statusOptions,
  useNotify,
  type DataTableColumn,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatDate, formatDateTime, formatMoney, formatNumber } from '@/4-presentation/panel/lib';
import {
  BUILD_CHANNELS,
  BUILD_CSV,
  BUILD_KINDS,
  BUILD_PICKER_FILTERS,
  buildCustomer,
  buildStatusLabel,
  filterBuilds,
  normalizeBuildNumber,
  sellableBuilds,
  type BuildPickerFilters,
} from './builds';
import type { BuildListData } from './types';

export interface ReservationPickerDialogProps {
  open: boolean;
  onClose: () => void;
  /** Cargar esa reserva o cotización en la caja. */
  onPick: (number: string) => void;
  /** Puede listar las reservas (sin este permiso, solo se escribe el número). */
  canList: boolean;
}

const STATE_OPTIONS = [
  { value: 'Reserved', label: 'Reservadas' },
  { value: 'Quoted', label: 'Cotizadas' },
];

const COLUMNS: DataTableColumn<BuildListData>[] = [
  {
    id: 'numero',
    header: 'Número',
    value: (row) => row.number,
    card: 'title',
    cell: (row) => (
      <span className="block min-w-36">
        <span className="block font-mono">{row.number}</span>
        <span className="block text-xs font-normal text-text-muted">{row.name}</span>
      </span>
    ),
  },
  {
    id: 'tipo',
    header: 'Tipo',
    value: (row) => `${row.kind}`,
    cell: (row) => (
      <span className="flex flex-wrap gap-1">
        <StatusBadge status={row.kind} statuses={BUILD_KINDS} />
        <StatusBadge status={row.channel} statuses={BUILD_CHANNELS} />
      </span>
    ),
  },
  { id: 'cliente', header: 'Cliente', value: (row) => buildCustomer(row) },
  {
    id: 'vence',
    header: 'Estado y vigencia',
    value: (row) => (row.status === 'Reserved' && row.reservedUntil ? new Date(row.reservedUntil) : new Date(`${row.validUntil}T23:59:59-04:00`)),
    cell: (row) => (
      <span className="block min-w-40">
        <span className="block font-medium">{buildStatusLabel(row)}</span>
        <span className="block text-xs text-text-muted">
          {row.status === 'Reserved' && row.reservedUntil ? `hasta el ${formatDateTime(row.reservedUntil)}` : `hasta el ${formatDate(row.validUntil)}`}
        </span>
      </span>
    ),
  },
  { id: 'total', header: 'Total', align: 'end', value: (row) => row.total, cell: (row) => formatMoney(row.total) },
];

export function ReservationPickerDialog({ open, onClose, onPick, canList }: ReservationPickerDialogProps) {
  const notify = useNotify();
  const formId = useId();
  const [number, setNumber] = useState('');
  const [touched, setTouched] = useState(false);
  const [filters, setFilters] = useState<BuildPickerFilters>(BUILD_PICKER_FILTERS);
  const builds = useRpcQuery('GetPcBuildsQuery', { status: null, channel: null, kind: null }, { enabled: open && canList });
  const sellable = useMemo(() => sellableBuilds(builds.data ?? []), [builds.data]);
  const rows = useMemo(() => filterBuilds(sellable, filters), [sellable, filters]);
  const active = Object.entries(filters).filter(([key, value]) => value !== BUILD_PICKER_FILTERS[key as keyof BuildPickerFilters]).length;

  const typed = normalizeBuildNumber(number);
  const numberError = touched && typed.length === 0 ? 'Escriba el número de la reserva o cotización, por ejemplo RES-CM-000012.' : undefined;

  const close = () => {
    setNumber('');
    setTouched(false);
    setFilters(BUILD_PICKER_FILTERS);
    onClose();
  };

  const pick = (value: string) => {
    setNumber('');
    setTouched(false);
    onPick(normalizeBuildNumber(value));
  };

  const setFilter = (key: keyof BuildPickerFilters, value: string) => setFilters((current) => ({ ...current, [key]: value }));

  const exportRows = () => {
    const file = exportCsv('reservas-vigentes', BUILD_CSV, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  return (
    <Dialog
      open={open}
      onClose={close}
      size="xl"
      title="Vender una reserva o cotización"
      description="Se cobra a sus precios congelados; la venta de una reserva consume su reserva (el stock reservado sale una sola vez)."
      footer={
        <Button variant="outline" onClick={close}>
          Cerrar
        </Button>
      }
    >
      <div className="space-y-4">
        <Form
          id={formId}
          onSubmit={() => {
            setTouched(true);
            if (typed.length > 0) pick(typed);
          }}
        >
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start">
            <TextField
              label="Número de la reserva o cotización"
              value={number}
              onChange={setNumber}
              error={numberError}
              placeholder="RES-CM-000012 o ARM-WEB-000001"
              leading={<PackageSearch />}
              autoComplete="off"
              spellCheck={false}
              className="flex-1"
              data-autofocus
            />
            <Button type="submit" leftIcon={<PackageCheck />} className="sm:mt-6">
              Cargar en la caja
            </Button>
          </div>
        </Form>

        {canList && (
          <>
            <FilterBar activeCount={active} onClear={() => setFilters(BUILD_PICKER_FILTERS)} title="Reservas y cotizaciones vigentes">
              <SearchField label="Buscar" placeholder="Número, nombre, cliente o teléfono" value={filters.q} onChange={(value) => setFilter('q', value)} />
              <SelectField label="Tipo" value={filters.tipo} onChange={(value) => setFilter('tipo', value)} options={statusOptions(BUILD_KINDS)} />
              <SelectField label="Estado" value={filters.estado} onChange={(value) => setFilter('estado', value)} options={STATE_OPTIONS} />
              <SelectField label="Canal" value={filters.canal} onChange={(value) => setFilter('canal', value)} options={statusOptions(BUILD_CHANNELS)} />
            </FilterBar>
            <Toolbar
              label="Acciones de la lista de reservas"
              end={
                <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
                  Exportar CSV
                </Button>
              }
            >
              {builds.data && (
                <span className="text-sm text-text-muted" data-testid="reservas-resumen">
                  {formatNumber(rows.length)} de {formatNumber(sellable.length)} vigentes
                </span>
              )}
            </Toolbar>
            <DataTable
              caption="Reservas y cotizaciones vigentes"
              columns={COLUMNS}
              rows={builds.data ? rows : undefined}
              rowKey={(row) => row.number}
              rowLabel={(row) => `la reserva ${row.number}`}
              loading={builds.loading}
              refreshing={builds.fetching && !builds.loading}
              error={builds.error}
              onRetry={builds.reload}
              operation="GetPcBuildsQuery"
              defaultSort={{ column: 'vence', direction: 'asc' }}
              pageSizes={[10, 25, 50]}
              onRowOpen={(row) => pick(row.number)}
              rowActions={(row) => [{ label: 'Cobrar en la caja', icon: <PackageCheck />, onSelect: () => pick(row.number) }]}
              maxHeightClass="sm:max-h-[45vh]"
              empty={{
                title: sellable.length === 0 ? 'No hay reservas ni cotizaciones vigentes' : 'Ninguna coincide con los filtros',
                description: sellable.length === 0 ? 'Las reservas de la tienda web y del mostrador, y las cotizaciones del Armador de PC, aparecen aquí.' : 'Pruebe con otros filtros.',
                icon: <PackageSearch />,
              }}
            />
          </>
        )}
      </div>
    </Dialog>
  );
}
