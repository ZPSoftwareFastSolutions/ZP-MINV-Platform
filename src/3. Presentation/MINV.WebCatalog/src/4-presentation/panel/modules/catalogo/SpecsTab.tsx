// Módulo «Catálogo» · pestaña «Especificaciones» (la administración de especificaciones por categoría del escritorio):
// todas las especificaciones o las de una categoría CON las heredadas de sus madres (`GetSpecDefinitionsQuery`), filtros
// (categoría, tipo, uso y búsqueda, con el prefijo `e_` en la dirección), detalle lateral y crear o modificar con
// `SaveSpecDefinitionCommand` (permiso `catalog.specs.manage`). Regla T-01: las reglas de la ficha las valida el servidor.

import { Download, Eye, FilePlus2, Pencil, RefreshCw, SlidersHorizontal } from 'lucide-react';
import { useMemo, useRef, useState } from 'react';
import { usePermissions, useRpcQuery, useTableState, type RpcQuery } from '@/4-presentation/panel/hooks';
import {
  Button,
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
  type RowActionItem,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatNumber } from '@/4-presentation/panel/lib';
import type { CatalogOptionsRecord, SpecDefinitionRecord } from './catalog';
import { NEW_PARAM, TAB_PARAM, newRequestOf, useOneShotParam } from './params';
import { SpecDialog, type SpecTarget } from './SpecDialog';
import {
  SPECS_CSV,
  SPEC_FILTERS,
  SPEC_PREFIX,
  SPEC_TYPES,
  SPEC_USE_OPTIONS,
  compatibilityLabel,
  compatibilityOptions,
  filterSpecs,
  nextSortOrder,
  toSpecEntries,
  type SpecEntry,
} from './specs';

const COLUMNS: DataTableColumn<SpecEntry>[] = [
  {
    id: 'nombre',
    header: 'Especificación',
    value: (entry) => entry.definition.name,
    card: 'title',
    cell: (entry) => (
      <span className="block min-w-40">
        <span className="block">
          {entry.definition.name}
          {entry.definition.unit ? ` (${entry.definition.unit})` : ''}
        </span>
        <span className="block text-xs font-normal text-text-muted">{entry.definition.code}</span>
      </span>
    ),
  },
  {
    id: 'categoria',
    header: 'Categoría',
    value: (entry) => entry.definition.categoryName,
    cell: (entry) => (
      <span className="flex flex-wrap items-center gap-1.5">
        {entry.definition.categoryName}
        {entry.definition.isInherited && <StatusBadge tone="neutral">Heredada</StatusBadge>}
      </span>
    ),
  },
  { id: 'tipo', header: 'Tipo', value: (entry) => entry.typeText },
  { id: 'opciones', header: 'Opciones', value: (entry) => entry.optionsText, sortable: false, cell: (entry) => <span className="line-clamp-2 block max-w-xs min-w-32 text-text-muted">{entry.optionsText || '—'}</span> },
  {
    id: 'uso',
    header: 'Uso',
    value: (entry) => entry.flags.join(' · '),
    sortable: false,
    cell: (entry) => (
      <span className="flex max-w-xs flex-wrap gap-1">
        {entry.definition.isRequired && <StatusBadge tone="warning">Obligatoria</StatusBadge>}
        {entry.definition.isFilterable && <StatusBadge tone="info">Filtrable</StatusBadge>}
        {entry.definition.compatibilityKey && <StatusBadge tone="accent">Armador</StatusBadge>}
        {!entry.definition.isRequired && !entry.definition.isFilterable && !entry.definition.compatibilityKey && <span className="text-text-muted">—</span>}
      </span>
    ),
  },
  { id: 'orden', header: 'Orden', align: 'end', value: (entry) => entry.definition.sortOrder },
];

export interface SpecsTabProps {
  options: RpcQuery<CatalogOptionsRecord>;
  /** Todas las especificaciones (para las claves del armador y el orden sugerido). */
  definitions: RpcQuery<SpecDefinitionRecord[]>;
}

export function SpecsTab({ options, definitions }: SpecsTabProps) {
  const notify = useNotify();
  const { canRun } = usePermissions();
  const canEdit = canRun('SaveSpecDefinitionCommand');
  const table = useTableState({ filters: SPEC_FILTERS, sort: { column: 'categoria', direction: 'asc' }, prefix: SPEC_PREFIX });
  const filters = table.filters;

  // Con una categoría elegida, el servidor trae también las heredadas de sus madres.
  const byCategory = useRpcQuery('GetSpecDefinitionsQuery', { categoryCode: filters.categoria || null }, { enabled: Boolean(filters.categoria), keepPreviousData: false });
  const source = filters.categoria ? byCategory : definitions;
  const entries = useMemo(() => toSpecEntries(source.data ?? []), [source.data]);
  const rows = useMemo(() => filterSpecs(entries, filters), [entries, filters]);
  const categories = useMemo(() => (options.data?.categories ?? []).map((category) => ({ value: category.code, label: category.name })), [options.data]);
  const keys = (current: string | null) => compatibilityOptions(definitions.data ?? [], current);

  const [detail, setDetail] = useState<{ entry: SpecEntry; open: boolean } | null>(null);
  const [session, setSession] = useState(0);
  const [editing, setEditing] = useState<SpecTarget | null>(null);
  const openDialog = (definition: SpecDefinitionRecord | null) => {
    const categoryCode = definition?.categoryCode ?? filters.categoria;
    setSession((count) => count + 1);
    setEditing({ definition, categoryCode, sortOrder: nextSortOrder(definitions.data ?? [], categoryCode) });
  };
  useOneShotParam(
    NEW_PARAM,
    (value) => newRequestOf(value) === 'especificacion',
    () => {
      if (canEdit) openDialog(null);
    },
    { [TAB_PARAM]: 'especificaciones' },
  );

  const pickingAction = useRef(false);
  const fromMenu = (action: () => void) => () => {
    pickingAction.current = true;
    action();
    setTimeout(() => {
      pickingAction.current = false;
    }, 0);
  };
  const openDetail = (entry: SpecEntry) => setDetail({ entry, open: true });
  const openFromRow = (entry: SpecEntry) => {
    if (!pickingAction.current) openDetail(entry);
  };

  const reload = () => {
    definitions.reload();
    if (filters.categoria) byCategory.reload();
  };

  const exportRows = () => {
    const file = exportCsv(filters.categoria ? `especificaciones ${filters.categoria}` : 'especificaciones', SPECS_CSV, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const rowActions = (entry: SpecEntry): RowActionItem[] => [
    { label: 'Ver detalle', icon: <Eye />, onSelect: fromMenu(() => openDetail(entry)) },
    {
      label: entry.definition.isInherited ? `Modificar en ${entry.definition.categoryName}` : 'Modificar',
      icon: <Pencil />,
      onSelect: fromMenu(() => openDialog(entry.definition)),
      hidden: !canEdit,
    },
  ];

  const current = detail?.entry.definition ?? null;
  const categoryName = categories.find((category) => category.value === filters.categoria)?.label;

  return (
    <div className="space-y-4">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SelectField
          label="Categoría"
          allLabel="Todas las categorías"
          value={filters.categoria}
          onChange={(value) => table.setFilter('categoria', value)}
          options={categories}
          hint={filters.categoria ? 'Con las heredadas de sus categorías madre.' : 'Cada especificación en su propia categoría.'}
        />
        <SearchField label="Buscar" placeholder="Nombre, código, unidad u opción" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Tipo" value={filters.tipo} onChange={(value) => table.setFilter('tipo', value)} options={statusOptions(SPEC_TYPES)} />
        <SelectField label="Uso" value={filters.uso} onChange={(value) => table.setFilter('uso', value)} options={SPEC_USE_OPTIONS} />
      </FilterBar>

      <Toolbar
        label="Acciones de la lista de especificaciones"
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={source.fetching && !source.loading} onClick={reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {source.data && (
          <span className="text-sm text-text-muted" data-testid="especificaciones-resumen">
            {formatNumber(rows.length)} de {formatNumber(entries.length)} especificaciones{categoryName ? ` de ${categoryName}` : ''}
          </span>
        )}
      </Toolbar>

      <DataTable
        caption="Especificaciones por categoría"
        columns={COLUMNS}
        rows={source.data ? rows : undefined}
        rowKey={(entry) => entry.key}
        rowLabel={(entry) => `la especificación ${entry.definition.name} de ${entry.definition.categoryName}`}
        loading={source.loading}
        refreshing={source.fetching && !source.loading}
        error={source.error}
        onRetry={source.reload}
        operation="GetSpecDefinitionsQuery"
        {...table.tableProps}
        onRowOpen={openFromRow}
        activeRowKey={detail?.open ? detail.entry.key : null}
        rowActions={rowActions}
        empty={{
          title: entries.length === 0 ? (categoryName ? `${categoryName} todavía no tiene especificaciones` : 'Todavía no hay especificaciones') : 'Ninguna especificación coincide con los filtros',
          description: entries.length === 0 && canEdit ? 'Cree la primera con «Nueva especificación».' : undefined,
          icon: <SlidersHorizontal />,
          action:
            entries.length === 0 ? (
              canEdit && (
                <Button leftIcon={<FilePlus2 />} onClick={() => openDialog(null)}>
                  Nueva especificación
                </Button>
              )
            ) : (
              <Button variant="outline" onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
        }}
      />

      <SidePanel
        open={detail?.open ?? false}
        onClose={() => setDetail((shown) => (shown?.open ? { ...shown, open: false } : shown))}
        title={current ? current.name : 'Especificación'}
        description={current ? `${current.code} · ${current.categoryName}` : undefined}
        footer={
          current &&
          canEdit && (
            <Button leftIcon={<Pencil />} fullWidth onClick={() => openDialog(current)}>
              {current.isInherited ? `Modificar en ${current.categoryName}` : 'Modificar'}
            </Button>
          )
        }
      >
        {current && detail && (
          <div className="space-y-5" data-testid="detalle-especificacion">
            <DetailList
              items={[
                { label: 'Código', value: current.code },
                { label: 'Categoría', value: current.isInherited ? `${current.categoryName} (heredada)` : current.categoryName },
                { label: 'Tipo', value: detail.entry.typeText },
                { label: 'Unidad', value: current.unit },
                { label: 'Obligatoria', value: current.isRequired ? 'Sí' : 'No' },
                { label: 'Filtra el catálogo', value: current.isFilterable ? 'Sí' : 'No' },
                { label: 'Armador de PC', value: current.compatibilityKey ? compatibilityLabel(current.compatibilityKey) : 'No participa' },
                { label: 'Orden', value: formatNumber(current.sortOrder) },
                { label: 'Opciones', value: current.options.length > 0 ? current.options.join(', ') : null, wide: true },
              ]}
            />
          </div>
        )}
      </SidePanel>

      <SpecDialog
        key={`especificacion-${session}`}
        target={editing}
        categories={categories}
        compatibilityKeys={keys(editing?.definition?.compatibilityKey ?? null)}
        onClose={() => setEditing(null)}
        onSaved={reload}
      />
    </div>
  );
}
