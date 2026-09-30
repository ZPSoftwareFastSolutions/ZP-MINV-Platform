// Módulo «Catálogo» · pestaña «Categorías»: las categorías del catálogo (`GetCatalogOptionsQuery`) con cuántos productos y
// especificaciones propias tiene cada una, filtros (búsqueda, con o sin productos, con o sin especificaciones) con el
// prefijo `c_` en la dirección, detalle lateral y los comandos de `SaveCategoryCommand`: nueva categoría, subcategoría y
// cambio de nombre (con `catalog.manage`). «Ver sus productos» y «Ver sus especificaciones» llevan a las otras pestañas
// ya filtradas.

import { Download, Eye, FolderPlus, FolderTree, ListTree, Pencil, RefreshCw, SlidersHorizontal, Tags } from 'lucide-react';
import { useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { usePermissions, useTableState, type RpcQuery } from '@/4-presentation/panel/hooks';
import { Button, DataTable, DetailList, FilterBar, SearchField, SelectField, SidePanel, Toolbar, useNotify, type DataTableColumn, type RowActionItem } from '@/4-presentation/panel/kit';
import { exportCsv, formatNumber } from '@/4-presentation/panel/lib';
import type { CatalogOptionsRecord, CatalogRecord, SpecDefinitionRecord } from './catalog';
import { CATEGORIES_CSV, CATEGORY_FILTERS, CATEGORY_PREFIX, WITH_OPTIONS, filterCategories, toCategoryEntries, type CategoryEntry } from './categories';
import { CategoryDialog, type CategoryTarget } from './CategoryDialog';
import { NEW_PARAM, TAB_PARAM, newRequestOf, useOneShotParam } from './params';
import { SPEC_PREFIX } from './specs';

const COLUMNS: DataTableColumn<CategoryEntry>[] = [
  { id: 'nombre', header: 'Categoría', value: (entry) => entry.name, card: 'title' },
  { id: 'codigo', header: 'Código', value: (entry) => entry.code },
  { id: 'productos', header: 'Productos', align: 'end', value: (entry) => entry.products, footer: (rows) => formatNumber(rows.reduce((sum, entry) => sum + entry.products, 0)) },
  { id: 'activos', header: 'Activos', align: 'end', value: (entry) => entry.activeProducts },
  { id: 'especificaciones', header: 'Especificaciones propias', align: 'end', value: (entry) => entry.specs },
];

export interface CategoriesTabProps {
  options: RpcQuery<CatalogOptionsRecord>;
  catalog: RpcQuery<CatalogRecord[]>;
  definitions: RpcQuery<SpecDefinitionRecord[]>;
}

export function CategoriesTab({ options, catalog, definitions }: CategoriesTabProps) {
  const notify = useNotify();
  const { canRun } = usePermissions();
  const canEdit = canRun('SaveCategoryCommand');
  const table = useTableState({ filters: CATEGORY_FILTERS, sort: { column: 'nombre', direction: 'asc' }, prefix: CATEGORY_PREFIX });
  const [, setParams] = useSearchParams();

  const entries = useMemo(() => toCategoryEntries(options.data, catalog.data ?? [], definitions.data ?? []), [options.data, catalog.data, definitions.data]);
  const rows = useMemo(() => filterCategories(entries, table.filters), [entries, table.filters]);
  const choices = useMemo(() => entries.map((entry) => ({ value: entry.code, label: entry.name })), [entries]);

  const [detail, setDetail] = useState<{ entry: CategoryEntry; open: boolean } | null>(null);
  const current = detail ? (entries.find((entry) => entry.code === detail.entry.code) ?? detail.entry) : null;
  const ownSpecs = current ? (definitions.data ?? []).filter((definition) => definition.categoryCode === current.code && !definition.isInherited) : [];

  const [session, setSession] = useState(0);
  const [editing, setEditing] = useState<CategoryTarget | null>(null);
  const openDialog = (target: CategoryTarget) => {
    setSession((count) => count + 1);
    setEditing(target);
  };
  useOneShotParam(
    NEW_PARAM,
    (value) => newRequestOf(value) === 'categoria',
    () => {
      if (canEdit) openDialog({ mode: 'nueva', category: null });
    },
    { [TAB_PARAM]: 'categorias' },
  );

  // Guarda del menú «⋯» (su clic llega también a la fila).
  const pickingAction = useRef(false);
  const fromMenu = (action: () => void) => () => {
    pickingAction.current = true;
    action();
    setTimeout(() => {
      pickingAction.current = false;
    }, 0);
  };
  const openDetail = (entry: CategoryEntry) => setDetail({ entry, open: true });
  const openFromRow = (entry: CategoryEntry) => {
    if (!pickingAction.current) openDetail(entry);
  };

  /** Lleva a otra pestaña ya filtrada por la categoría. */
  const goTo = (tab: 'productos' | 'especificaciones', code: string) =>
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        if (tab === 'productos') {
          next.delete(TAB_PARAM);
          next.set('categoria', code);
          next.delete('espec');
          next.delete('pagina');
        } else {
          next.set(TAB_PARAM, 'especificaciones');
          next.set(`${SPEC_PREFIX}categoria`, code);
          next.delete(`${SPEC_PREFIX}pagina`);
        }
        return next;
      },
      { replace: true },
    );

  const reload = () => {
    options.reload();
    catalog.reload();
    definitions.reload();
  };

  const exportRows = () => {
    const file = exportCsv('categorias', CATEGORIES_CSV, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const rowActions = (entry: CategoryEntry): RowActionItem[] => [
    { label: 'Ver detalle', icon: <Eye />, onSelect: fromMenu(() => openDetail(entry)) },
    { label: 'Ver sus productos', icon: <Tags />, onSelect: fromMenu(() => goTo('productos', entry.code)) },
    { label: 'Ver sus especificaciones', icon: <SlidersHorizontal />, onSelect: fromMenu(() => goTo('especificaciones', entry.code)) },
    { label: 'Cambiar el nombre', icon: <Pencil />, onSelect: fromMenu(() => openDialog({ mode: 'renombrar', category: entry })), hidden: !canEdit },
    { label: 'Nueva subcategoría', icon: <FolderTree />, onSelect: fromMenu(() => openDialog({ mode: 'subcategoria', category: entry })), hidden: !canEdit },
  ];

  return (
    <div className="space-y-4">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Nombre o código" value={table.filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Productos" value={table.filters.productos} onChange={(value) => table.setFilter('productos', value)} options={WITH_OPTIONS} />
        <SelectField label="Especificaciones" value={table.filters.especificaciones} onChange={(value) => table.setFilter('especificaciones', value)} options={WITH_OPTIONS} />
      </FilterBar>

      <Toolbar
        label="Acciones de la lista de categorías"
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={options.fetching && !options.loading} onClick={reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {options.data && (
          <span className="text-sm text-text-muted" data-testid="categorias-resumen">
            {formatNumber(rows.length)} de {formatNumber(entries.length)} categorías · los productos de una subcategoría también se ven en su madre
          </span>
        )}
      </Toolbar>

      <DataTable
        caption="Categorías del catálogo"
        columns={COLUMNS}
        rows={options.data ? rows : undefined}
        rowKey={(entry) => entry.key}
        rowLabel={(entry) => `la categoría ${entry.name}`}
        loading={options.loading}
        refreshing={options.fetching && !options.loading}
        error={options.error}
        onRetry={options.reload}
        operation="GetCatalogOptionsQuery"
        {...table.tableProps}
        onRowOpen={openFromRow}
        activeRowKey={detail?.open ? detail.entry.key : null}
        rowActions={rowActions}
        empty={{
          title: entries.length === 0 ? 'Todavía no hay categorías' : 'Ninguna categoría coincide con los filtros',
          icon: <ListTree />,
          action:
            entries.length === 0 ? (
              canEdit && (
                <Button leftIcon={<FolderPlus />} onClick={() => openDialog({ mode: 'nueva', category: null })}>
                  Nueva categoría
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
        title={current ? current.name : 'Categoría'}
        description={current ? `Código ${current.code}` : undefined}
        footer={
          current && (
            <div className="flex flex-col gap-2">
              <Button leftIcon={<Tags />} fullWidth onClick={() => goTo('productos', current.code)}>
                Ver sus productos
              </Button>
              <Button variant="outline" leftIcon={<SlidersHorizontal />} fullWidth onClick={() => goTo('especificaciones', current.code)}>
                Ver sus especificaciones
              </Button>
              {canEdit && (
                <>
                  <Button variant="outline" leftIcon={<Pencil />} fullWidth onClick={() => openDialog({ mode: 'renombrar', category: current })}>
                    Cambiar el nombre
                  </Button>
                  <Button variant="outline" leftIcon={<FolderTree />} fullWidth onClick={() => openDialog({ mode: 'subcategoria', category: current })}>
                    Nueva subcategoría
                  </Button>
                </>
              )}
            </div>
          )
        }
      >
        {current && (
          <div className="space-y-5" data-testid="detalle-categoria">
            <DetailList
              items={[
                { label: 'Código', value: current.code },
                { label: 'Nombre', value: current.name },
                { label: 'Productos propios', value: formatNumber(current.products) },
                { label: 'Activos', value: formatNumber(current.activeProducts) },
              ]}
            />
            <section className="space-y-2">
              <h3 className="text-sm font-semibold text-text">Especificaciones propias</h3>
              {ownSpecs.length === 0 ? (
                <p className="text-sm text-text-muted">No tiene especificaciones propias (puede heredar las de su categoría madre).</p>
              ) : (
                <ul className="list-disc space-y-1 pl-5 text-sm text-text">
                  {ownSpecs.map((definition) => (
                    <li key={definition.code}>
                      {definition.name}
                      {definition.unit ? ` (${definition.unit})` : ''}
                    </li>
                  ))}
                </ul>
              )}
            </section>
          </div>
        )}
      </SidePanel>

      <CategoryDialog key={`categoria-${session}`} target={editing} categories={choices} onClose={() => setEditing(null)} onSaved={reload} />
    </div>
  );
}
