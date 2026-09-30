// Tecnología › Series: la pantalla «Series e IMEI» del escritorio (SerialsView) en la web. Cada unidad con serie o IMEI
// de la empresa (`SearchSerialsQuery`, las que ingresaron más recientemente): dónde está, a quién se vendió y su garantía
// (derivada de la venta, regla T-04). Detalle lateral con su trazabilidad (`GetSerialTraceQuery`), su garantía vigente
// (`GetWarrantyStatusQuery`) y sus casos de garantía. Comandos: registrar series de unidades que ya estaban en stock
// (`RegisterStockSerialsCommand`) y dar destino a una devuelta o en garantía (`DisposeSerialCommand`), solo para quien
// gestiona series. Los totales por estado van PLEGADOS en «Ver resumen de las unidades» (P-10).
//
// Filtros en la dirección: `q` (serie, IMEI o SKU), `estado`, `producto` (SKU) y `registros` van al SERVIDOR; `sucursal`,
// `tipo`, `garantia` y las fechas de ingreso (`desde`, `hasta`) se filtran en la página. Además: `?consultar=1` abre
// «Consultar una serie» (lo usa el tablero) y `?ver=<serie>` abre el detalle de esa unidad (lo usa Garantías; con
// `producto=<SKU>` si la serie puede repetirse en dos productos). Los dos se quitan de la dirección al abrirse.

import { BarChart3, Download, Eye, Filter, PackageX, RefreshCw, ScanBarcode, ScanSearch, ShieldPlus } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Button,
  Collapsible,
  ComboBox,
  DataTable,
  DateRangeField,
  FilterBar,
  Page,
  SearchField,
  SelectField,
  StatusBadge,
  Toolbar,
  useNotify,
  type DataTableColumn,
  type RowActionItem,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatDate, formatDateTime, formatNumber, laPazToday } from '@/4-presentation/panel/lib';
import { DisposeSerialDialog, type DisposeTarget } from './DisposeSerialDialog';
import { LookupSerialDialog } from './LookupSerialDialog';
import { RegisterSerialsDialog } from './RegisterSerialsDialog';
import { SerialDetailPanel, type SerialAbilities, type SerialTarget } from './SerialDetailPanel';
import { SerialsByStatusStat } from './SerialsByStatusStat';
import {
  KIND_OPTIONS,
  PRODUCTS_REQUEST,
  SERIALS_CSV,
  SERIAL_FILTERS,
  SERIAL_KINDS,
  SERIAL_STATES,
  STATUS_FILTER_OPTIONS,
  TAKE_OPTIONS,
  WARRANTY_OPTIONS,
  branchOptions,
  canDisposeOf,
  canOpenClaimFor,
  filterSerials,
  isTruncated,
  openClaimPath,
  productOptions,
  registerProductOptions,
  searchRequest,
  selectedProduct,
  statusLabel,
  toSerialItems,
  warrantyText,
  type SerialItem,
  type SerialRecord,
} from './serials';

const WARRANTY_TONES = { vigente: 'text-success-text', vencida: 'text-danger-text', sin: 'text-text-muted' } as const;

function serialColumns(today: string): DataTableColumn<SerialItem>[] {
  return [
    {
      id: 'serie',
      header: 'Serie o IMEI',
      value: (item) => item.row.serial,
      card: 'title',
      cell: (item) => (
        <span className="block">
          <span className="block font-mono break-all">{item.row.serial}</span>
          <StatusBadge status={item.row.kind} statuses={SERIAL_KINDS} />
        </span>
      ),
    },
    {
      id: 'producto',
      header: 'Producto',
      value: (item) => item.row.product,
      cell: (item) => (
        <span className="block min-w-44">
          <span className="block">{item.row.product}</span>
          <span className="block text-xs text-text-muted">{item.row.sku}</span>
        </span>
      ),
    },
    {
      id: 'estado',
      header: 'Estado',
      value: (item) => statusLabel(item.row.status),
      cell: (item) => <StatusBadge status={item.row.status} statuses={SERIAL_STATES} />,
    },
    { id: 'ubicacion', header: 'Dónde está', value: (item) => item.location || null, className: 'whitespace-nowrap' },
    {
      id: 'venta',
      header: 'Venta',
      value: (item) => (item.row.soldAt ? new Date(item.row.soldAt) : null),
      cell: (item) =>
        item.row.soldAt ? (
          <span className="block min-w-36">
            <span className="block whitespace-nowrap">
              {formatDate(item.row.soldAt)}
              {item.row.invoiceNumber && ` · ${item.row.invoiceNumber}`}
            </span>
            <span className="block text-xs text-text-muted">{item.row.customer ?? 'Cliente de otra sucursal'}</span>
          </span>
        ) : (
          <span className="text-text-muted">—</span>
        ),
    },
    {
      id: 'garantia',
      header: 'Garantía',
      value: (item) => item.row.warrantyUntil,
      cell: (item) => <span className={`whitespace-nowrap ${WARRANTY_TONES[item.warranty]}`}>{warrantyText(item.row.warrantyUntil, today)}</span>,
    },
    {
      id: 'ingreso',
      header: 'Ingresó',
      value: (item) => (item.row.receivedAt ? new Date(item.row.receivedAt) : null),
      cell: (item) => formatDateTime(item.row.receivedAt),
      className: 'whitespace-nowrap',
    },
  ];
}

export function SerialsPage() {
  const notify = useNotify();
  const navigate = useNavigate();
  const { can, canRun, session } = usePermissions();
  // «Hoy» se fija al abrir (la garantía vigente se compara con este día de La Paz).
  const [today] = useState(() => laPazToday());
  const table = useTableState({ filters: SERIAL_FILTERS, sort: { column: 'ingreso', direction: 'desc' } });
  const filters = table.filters;
  const [params, setParams] = useSearchParams();

  // Lo que filtra el SERVIDOR va en el pedido (cambiarlo vuelve a consultar); el resto se filtra en la página.
  const serials = useRpcQuery('SearchSerialsQuery', searchRequest(filters));
  const canListProducts = canRun('SearchTechProductsQuery');
  const products = useRpcQuery('SearchTechProductsQuery', PRODUCTS_REQUEST, { enabled: canListProducts });
  const items = useMemo(() => toSerialItems(serials.data ?? [], today), [serials.data, today]);
  const rows = useMemo(() => filterSerials(items, filters), [items, filters]);
  const columns = useMemo(() => serialColumns(today), [today]);

  // Listas desplegables.
  const branches = useMemo(() => session?.access.branches ?? [], [session]);
  const branchChoices = useMemo(() => branchOptions(items, branches, filters.sucursal), [items, branches, filters.sucursal]);
  const catalog = canListProducts && !products.error ? products.data : undefined;
  const productChoices = useMemo(() => productOptions(catalog, items), [catalog, items]);
  const productValue = selectedProduct(filters.producto, productChoices, items);
  const registerChoices = useMemo(() => (canListProducts && !products.error ? registerProductOptions(products.data ?? []) : null), [canListProducts, products.error, products.data]);

  const abilities: SerialAbilities = { openClaim: canRun('OpenWarrantyClaimCommand'), dispose: canRun('DisposeSerialCommand'), sales: can('sales.view') };
  const canRegister = canRun('RegisterStockSerialsCommand');

  // Detalle (se guarda la unidad y si está abierto: al cerrar, el panel se desliza con su contenido).
  const [detail, setDetail] = useState<{ target: SerialTarget; open: boolean } | null>(null);
  const detailOpen = detail?.open ?? false;
  const traceRequest = { serial: detail?.target.serial ?? '', sku: detail?.target.sku ?? null };
  const trace = useRpcQuery('GetSerialTraceQuery', traceRequest, { enabled: detailOpen, keepPreviousData: false });
  const warranty = useRpcQuery('GetWarrantyStatusQuery', traceRequest, { enabled: detailOpen, keepPreviousData: false });

  // Diálogos: cada uno se monta de nuevo en cada apertura (`key`), así su formulario empieza vacío.
  const [dialogSession, setDialogSession] = useState(0);
  const [lookupOpen, setLookupOpen] = useState(false);
  const [registerOpen, setRegisterOpen] = useState(false);
  const [disposing, setDisposing] = useState<DisposeTarget | null>(null);
  const nextDialog = () => setDialogSession((count) => count + 1);

  const openTarget = (target: SerialTarget) => setDetail({ target, open: true });
  const openDetail = (item: SerialItem) => openTarget({ serial: item.row.serial, sku: item.row.sku });
  const closeDetail = () => setDetail((current) => (current?.open ? { ...current, open: false } : current));
  const onlyProduct = (sku: string) => {
    table.setFilter('producto', sku);
    closeDetail();
  };
  const askLookup = () => {
    nextDialog();
    setLookupOpen(true);
  };
  const askRegister = () => {
    nextDialog();
    setRegisterOpen(true);
  };
  const askDispose = (row: SerialRecord) => {
    nextDialog();
    setDisposing({ serial: row.serial, sku: row.sku, product: row.product, status: row.status });
  };
  const afterDispose = () => {
    serials.reload();
    trace.reload();
    warranty.reload();
  };

  // `?ver=<serie>` (desde Garantías) y `?consultar=1` (desde el tablero): se abren una vez por pedido y se quitan de la
  // dirección.
  const verParam = params.get('ver');
  const lookupParam = params.get('consultar');
  const signature = `${verParam ?? ''}|${lookupParam ?? ''}`;
  const [handled, setHandled] = useState('|');
  if (signature !== handled) {
    setHandled(signature);
    if (verParam) setDetail({ target: { serial: verParam.trim(), sku: filters.producto || null }, open: true });
    if (lookupParam !== null) {
      setDialogSession((count) => count + 1);
      setLookupOpen(true);
    }
  }
  useEffect(() => {
    if (verParam === null && lookupParam === null) return;
    setParams(
      (previous) => {
        const updated = new URLSearchParams(previous);
        updated.delete('ver');
        updated.delete('consultar');
        return updated;
      },
      { replace: true },
    );
  }, [verParam, lookupParam, setParams]);

  const rowActions = (item: SerialItem): RowActionItem[] => [
    { label: 'Ver detalle', icon: <Eye />, onSelect: () => openDetail(item) },
    { label: 'Ver solo este producto', icon: <Filter />, onSelect: () => onlyProduct(item.row.sku), hidden: filters.producto === item.row.sku },
    {
      label: 'Abrir caso de garantía',
      icon: <ShieldPlus />,
      onSelect: () => navigate(ROUTES.panelModule(openClaimPath(item.row.serial, item.row.sku))),
      hidden: !abilities.openClaim || !canOpenClaimFor(item.row.status),
    },
    { label: 'Dar destino', icon: <PackageX />, tone: 'danger', onSelect: () => askDispose(item.row), hidden: !abilities.dispose || !canDisposeOf(item.row.status) },
  ];

  const exportRows = () => {
    const file = exportCsv('series', SERIALS_CSV, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const truncated = serials.data ? isTruncated(serials.data.length, filters.registros) : false;
  const detailTarget = detail?.target ?? null;

  return (
    <Page
      title="Series"
      description="Cada unidad con serie o IMEI: dónde está, a quién se vendió, su garantía, su trazabilidad y sus casos de garantía."
      actions={
        <>
          <Button variant="outline" leftIcon={<ScanSearch />} onClick={askLookup}>
            Consultar una serie
          </Button>
          {canRegister && (
            <Button leftIcon={<ScanBarcode />} onClick={askRegister}>
              Registrar series de stock
            </Button>
          )}
        </>
      }
    >
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Serie, IMEI o SKU" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Estado" allLabel="Todos los estados" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={STATUS_FILTER_OPTIONS} />
        <ComboBox
          label="Producto"
          placeholder="Nombre o SKU"
          value={productValue}
          onChange={(option) => table.setFilter('producto', option?.value ?? '')}
          options={productChoices}
          emptyText="Ningún producto con serie con ese nombre o SKU"
        />
        <SelectField label="Sucursal" allLabel="Todas las sucursales" value={filters.sucursal} onChange={(value) => table.setFilter('sucursal', value)} options={branchChoices} />
        <SelectField label="Tipo" allLabel="Serie e IMEI" value={filters.tipo} onChange={(value) => table.setFilter('tipo', value)} options={KIND_OPTIONS} />
        <SelectField label="Garantía" allLabel="Todas" value={filters.garantia} onChange={(value) => table.setFilter('garantia', value)} options={WARRANTY_OPTIONS} />
        <DateRangeField label="Fecha de ingreso" value={table.dateRange()} onChange={(range) => table.setDateRange(range)} />
        <SelectField
          label="Series a revisar"
          allLabel={false}
          value={filters.registros}
          onChange={(value) => table.setFilter('registros', value || SERIAL_FILTERS.registros)}
          options={TAKE_OPTIONS}
          hint="Las que ingresaron más recientemente; los demás filtros se aplican sobre ellas."
        />
      </FilterBar>

      <Toolbar
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={serials.fetching && !serials.loading} onClick={serials.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {serials.data && (
          <span className="text-sm text-text-muted" data-testid="series-resumen">
            {formatNumber(rows.length)} de {formatNumber(items.length)} series
            {truncated && ' · las más recientes: afine la búsqueda para ver otras'}
          </span>
        )}
      </Toolbar>

      <Collapsible
        label="Ver resumen de las unidades"
        openLabel="Ocultar resumen de las unidades"
        icon={<BarChart3 />}
        description="Unidades por estado de toda la empresa (no solo las de la lista) y cuántas vendidas siguen en garantía."
      >
        <SerialsByStatusStat />
      </Collapsible>

      <DataTable
        caption="Series e IMEI"
        columns={columns}
        rows={serials.data ? rows : undefined}
        rowKey={(item) => item.key}
        rowLabel={(item) => `la serie ${item.row.serial}`}
        loading={serials.loading}
        refreshing={serials.fetching && !serials.loading}
        error={serials.error}
        onRetry={serials.reload}
        operation="SearchSerialsQuery"
        {...table.tableProps}
        onRowOpen={openDetail}
        activeRowKey={detail?.open && detail.target.sku ? `${detail.target.sku}|${detail.target.serial}` : null}
        rowActions={rowActions}
        empty={{
          title: items.length === 0 && table.activeFilterCount === 0 ? 'Todavía no hay unidades con serie o IMEI' : 'No hay series con estos filtros',
          description:
            items.length === 0 && table.activeFilterCount === 0
              ? 'Las series entran al recibir compras, en el saldo inicial o con «Registrar series de stock».'
              : 'Pruebe con otra búsqueda u otros filtros, o limpie los filtros.',
          icon: <ScanBarcode />,
          action:
            table.activeFilterCount > 0 ? (
              <Button variant="outline" onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ) : canRegister ? (
              <Button variant="outline" leftIcon={<ScanBarcode />} onClick={askRegister}>
                Registrar series de stock
              </Button>
            ) : undefined,
        }}
      />

      <SerialDetailPanel
        target={detailTarget}
        open={detailOpen}
        onClose={closeDetail}
        trace={trace}
        warranty={warranty}
        abilities={abilities}
        onDispose={askDispose}
        onOnlyProduct={onlyProduct}
        productFiltered={(sku) => filters.producto === sku}
      />

      <LookupSerialDialog
        key={`consultar-${dialogSession}`}
        open={lookupOpen}
        onClose={() => setLookupOpen(false)}
        canOpenClaim={abilities.openClaim}
        onTrace={(serial, sku) => {
          setLookupOpen(false);
          openTarget({ serial, sku });
        }}
      />
      {canRegister && (
        <RegisterSerialsDialog
          key={`registrar-${dialogSession}`}
          open={registerOpen}
          onClose={() => setRegisterOpen(false)}
          onRegistered={serials.reload}
          products={registerChoices}
          productsLoading={canListProducts && !products.data && !products.error}
        />
      )}
      <DisposeSerialDialog key={`destino-${dialogSession}`} target={disposing} onClose={() => setDisposing(null)} onDisposed={afterDispose} />
    </Page>
  );
}
