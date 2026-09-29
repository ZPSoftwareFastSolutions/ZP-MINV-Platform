// Módulo «Stock» · detalle lateral de un producto (en el escritorio: ProductDetailView, la ficha con kardex). Pestañas:
//   - Ficha: existencias, disponible, reservado, mínimo y máximo, costo promedio, valor, proveedor, códigos de barras y
//     existencias por posición y lote (`GetProductCardQuery`).
//   - Kardex: movimientos con su saldo acumulado, del más reciente al más antiguo, exportable a CSV.
//   - Reservas: lo reservado del producto (por posición y lote) y el acceso a «Reservas» para ver quién reservó.
//   - Por sucursal: existencias en cada sucursal visible y en tránsito (`ConsolidatedStockQuery`, recién al abrirla).
// La ficha es SIEMPRE del almacén de trabajo de la sesión (así la calcula el servidor): si la tabla muestra otro almacén,
// se avisa. Los botones de registro llevan a «Movimientos» con el producto elegido (solo a quien puede registrar).

import { ArrowLeftRight, ClipboardCopy, Download, Info, PackageMinus, PackagePlus, PencilLine, Users, Wrench } from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcQuery } from '@/4-presentation/panel/hooks';
import {
  Alert,
  Button,
  DataTable,
  DetailList,
  EmptyState,
  ErrorState,
  LoadingState,
  SidePanel,
  StatusBadge,
  TabPanel,
  Tabs,
  useNotify,
  type DataTableColumn,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatDate, formatMoney, formatNumber, formatQuantity, formatTime } from '@/4-presentation/panel/lib';
import {
  KARDEX_CSV,
  KARDEX_TAKE,
  STOCK_STATUSES,
  consolidatedRowOf,
  kardexCountText,
  kardexDetail,
  minMaxText,
  movementLink,
  quantityWithUnit,
  reservedBins,
  sentenceCase,
  signedText,
  toKardexEntries,
  type KardexEntry,
  type ProductCardData,
} from './stock';

export type DetailTab = 'ficha' | 'kardex' | 'reservas' | 'sucursales';

export interface StockDetailPanelProps {
  /** SKU mostrado (se conserva mientras el panel se cierra, para que se deslice con su contenido). */
  sku: string | null;
  open: boolean;
  onClose: () => void;
  /** Pestaña con la que se abre (desde el consolidado, «Por sucursal»). */
  initialTab?: DetailTab;
  /** Almacén de trabajo de la sesión (el de la ficha), si se conoce. */
  workWarehouse: string | null;
  /** Almacén que muestra la tabla si NO es el de trabajo (para avisar que la ficha es de otro almacén). */
  otherWarehouse: string | null;
}

const KARDEX_COLUMNS: DataTableColumn<KardexEntry>[] = [
  {
    id: 'fecha',
    header: 'Fecha',
    value: ({ line }) => new Date(line.recordedAt),
    cell: ({ line }) => (
      <span className="block whitespace-nowrap">
        <span className="block">{formatDate(line.businessDate)}</span>
        <span className="block text-xs text-text-muted">{formatTime(line.recordedAt)}</span>
      </span>
    ),
  },
  {
    id: 'movimiento',
    header: 'Movimiento',
    card: 'title',
    value: ({ line }) => sentenceCase(line.typeName),
    cell: ({ line }) => (
      <span className="block min-w-36">
        <span className="block font-medium">{sentenceCase(line.typeName)}</span>
        {kardexDetail(line) && <span className="block text-xs text-text-muted">{kardexDetail(line)}</span>}
        {line.notes && <span className="block text-xs text-text-muted italic">{line.notes}</span>}
      </span>
    ),
  },
  {
    id: 'cantidad',
    header: 'Cantidad',
    align: 'end',
    value: ({ line }) => line.signed,
    cell: ({ line }) => <span className={line.signed < 0 ? 'text-warning-text' : 'text-success-text'}>{signedText(line.signed)}</span>,
  },
  { id: 'saldo', header: 'Saldo', align: 'end', value: ({ line }) => line.balance, cell: ({ line }) => formatQuantity(line.balance) },
];

export function StockDetailPanel({ sku, open, onClose, initialTab = 'ficha', workWarehouse, otherWarehouse }: StockDetailPanelProps) {
  const { can } = usePermissions();
  const notify = useNotify();
  const [tab, setTab] = useState<DetailTab>(initialTab);
  // Otro producto (o el mismo abierto de nuevo desde otro lugar): vuelve a la pestaña con la que se pidió.
  const [opened, setOpened] = useState({ sku, initialTab });
  if (opened.sku !== sku || opened.initialTab !== initialTab) {
    setOpened({ sku, initialTab });
    setTab(initialTab);
  }

  const card = useRpcQuery('GetProductCardQuery', { skuOrBarcode: sku ?? '', take: KARDEX_TAKE }, { enabled: open && sku !== null, keepPreviousData: false });
  const data = card.data;
  const kardex = useMemo(() => toKardexEntries(data?.movements ?? []), [data]);
  const canWarehouse = can('inventory.movements.register.warehouse');
  const canSales = can('inventory.movements.register.sales');
  const canCatalog = can('catalog.manage');

  const copySku = () => {
    if (!data) return;
    void navigator.clipboard?.writeText(data.sku).catch(() => undefined);
    notify.info('SKU copiado', data.sku);
  };

  const exportKardex = () => {
    if (!data) return;
    const file = exportCsv(`kardex ${data.sku}`, KARDEX_CSV, data.movements);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(data.movements.length)} filas).`);
  };

  const footer =
    data && (canWarehouse || canSales || canCatalog) ? (
      <div className="flex flex-col gap-2">
        {canWarehouse && (
          <Button leftIcon={<PackagePlus />} fullWidth to={movementLink('ENTRADA', data.sku)}>
            Registrar entrada
          </Button>
        )}
        {canSales && (
          <Button variant="outline" leftIcon={<PackageMinus />} fullWidth to={movementLink('SALIDA', data.sku)}>
            Registrar salida
          </Button>
        )}
        {canWarehouse && (
          <Button variant="outline" leftIcon={<Wrench />} fullWidth to={movementLink('ajuste', data.sku)}>
            Registrar ajuste
          </Button>
        )}
        {canCatalog && (
          <Button variant="ghost" leftIcon={<PencilLine />} fullWidth to={ROUTES.panelModule(`catalogo?q=${encodeURIComponent(data.sku)}`)}>
            Editar en el catálogo
          </Button>
        )}
      </div>
    ) : undefined;

  return (
    <SidePanel
      open={open}
      onClose={onClose}
      title={data?.name ?? (sku ? `Producto ${sku}` : 'Producto')}
      description={data ? `${data.sku} · ${data.category} · unidad ${data.unit}` : undefined}
      headerExtra={data && <StatusBadge status={data.status} statuses={STOCK_STATUSES} />}
      loading={card.loading}
      error={card.error}
      onRetry={card.reload}
      footer={footer}
    >
      {data && sku && (
        <div className="space-y-4" data-testid="ficha-producto">
          {otherWarehouse && (
            <Alert tone="info" title="Ficha del almacén de trabajo">
              La ficha, el kardex y las reservas son de {workWarehouse ?? 'la sucursal activa'}; la lista muestra {otherWarehouse}. Para verlos de otra sucursal, cambie la
              sucursal activa en la barra superior.
            </Alert>
          )}
          <Tabs
            label="Secciones de la ficha"
            value={tab}
            onChange={setTab}
            tabs={[
              { id: 'ficha', label: 'Ficha' },
              { id: 'kardex', label: 'Kardex', count: data.totalMovements },
              { id: 'reservas', label: 'Reservas' },
              { id: 'sucursales', label: 'Por sucursal' },
            ]}
          >
            <TabPanel id="ficha">
              <CardSummary card={data} onCopySku={copySku} />
            </TabPanel>
            <TabPanel id="kardex">
              <div className="space-y-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <p className="text-sm text-text-muted" data-testid="kardex-resumen">
                    {kardexCountText(data)}
                  </p>
                  <Button variant="outline" leftIcon={<Download />} disabled={data.movements.length === 0} onClick={exportKardex}>
                    Exportar kardex
                  </Button>
                </div>
                <DataTable
                  caption={`Kardex de ${data.sku}`}
                  columns={KARDEX_COLUMNS}
                  rows={kardex}
                  rowKey={(entry) => entry.key}
                  rowLabel={({ line }) => `el movimiento ${sentenceCase(line.typeName)} del ${formatDate(line.businessDate)}`}
                  empty={{ title: 'Este producto todavía no tiene movimientos', icon: <ArrowLeftRight /> }}
                />
              </div>
            </TabPanel>
            <TabPanel id="reservas">
              <ReservationsView card={data} canSeeReservations={can('sales.pcbuild.manage')} />
            </TabPanel>
            <TabPanel id="sucursales">
              <BranchStock sku={data.sku} unit={data.unit} />
            </TabPanel>
          </Tabs>
        </div>
      )}
    </SidePanel>
  );
}

/** Pestaña «Ficha»: cifras, datos y existencias por posición y lote. */
function CardSummary({ card, onCopySku }: { card: ProductCardData; onCopySku: () => void }) {
  const binsTitleId = useId();
  return (
    <div className="space-y-5">
      <DetailList
        items={[
          { label: 'Existencias', value: quantityWithUnit(card.onHand, card.unit) },
          { label: 'Disponible', value: quantityWithUnit(card.available, card.unit) },
          { label: 'Reservado', value: quantityWithUnit(card.reserved, card.unit) },
          { label: 'Mínimo / máximo', value: minMaxText(card.minimum, card.maximum) },
          { label: 'Costo promedio', value: formatMoney(card.unitCost) },
          { label: 'Valor', value: formatMoney(Math.max(0, card.onHand) * card.unitCost) },
          { label: 'Proveedor preferido', value: card.supplier ?? 'Sin proveedor preferido' },
          { label: 'Posición principal', value: card.primaryBin ?? 'Sin posición asignada' },
          { label: 'Estado del producto', value: card.isActive ? 'Activo' : 'Inactivo' },
          { label: 'Códigos de barras', value: card.barcodes.length > 0 ? card.barcodes.join(' · ') : 'Sin código de barras', wide: true },
        ]}
      />
      <Button variant="subtle" leftIcon={<ClipboardCopy />} onClick={onCopySku}>
        Copiar SKU
      </Button>
      <section aria-labelledby={binsTitleId} className="space-y-2">
        <h3 id={binsTitleId} className="text-sm font-semibold text-text">
          Existencias por posición y lote
        </h3>
        {card.bins.length === 0 ? (
          <p className="text-sm text-text-muted">Sin existencias en las posiciones de este almacén.</p>
        ) : (
          <ul className="divide-y divide-border rounded-xl border border-border" data-testid="ficha-posiciones">
            {card.bins.map((bin) => (
              <li key={`${bin.binCode}|${bin.lotNumber}`} className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1 px-3 py-2 text-sm">
                <span className="min-w-0">
                  <span className="font-medium text-text">{bin.binCode}</span>
                  <span className="text-text-muted"> · lote {bin.lotNumber}</span>
                </span>
                <span className="text-text-muted tabular-nums">
                  <span className="font-semibold text-text">{formatQuantity(bin.onHand)}</span>
                  {bin.reserved > 0 && ` · reservado ${formatQuantity(bin.reserved)} · disponible ${formatQuantity(bin.available)}`}
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

/** Pestaña «Reservas»: lo reservado del producto y dónde; quién reservó se ve en «Reservas» (Ventas). */
function ReservationsView({ card, canSeeReservations }: { card: ProductCardData; canSeeReservations: boolean }) {
  const bins = reservedBins(card);
  if (card.reserved <= 0) {
    return <EmptyState size="sm" icon={<Info />} title="Sin reservas activas" description="Todas las existencias de este almacén están disponibles." />;
  }
  return (
    <div className="space-y-4" data-testid="ficha-reservas">
      <DetailList
        items={[
          { label: 'Reservado', value: quantityWithUnit(card.reserved, card.unit) },
          { label: 'Disponible', value: quantityWithUnit(card.available, card.unit) },
          { label: 'Existencias', value: quantityWithUnit(card.onHand, card.unit) },
        ]}
      />
      <ul className="divide-y divide-border rounded-xl border border-border">
        {bins.map((bin) => (
          <li key={`${bin.binCode}|${bin.lotNumber}`} className="flex flex-wrap justify-between gap-x-4 px-3 py-2 text-sm">
            <span>
              {bin.binCode} <span className="text-text-muted">· lote {bin.lotNumber}</span>
            </span>
            <span className="font-semibold tabular-nums">{quantityWithUnit(bin.reserved, card.unit)} reservadas</span>
          </li>
        ))}
      </ul>
      <p className="text-sm text-text-muted">
        Las unidades reservadas son de armados y carritos reservados (tienda web, mostrador o caja). Disponible = existencias − reservado.
      </p>
      {canSeeReservations && (
        <Button variant="outline" leftIcon={<Users />} to={ROUTES.panelModule(`reservas?q=${encodeURIComponent(card.sku)}`)}>
          Ver quién reservó
        </Button>
      )}
    </div>
  );
}

/** Pestaña «Por sucursal»: consulta el consolidado recién al abrirla. */
function BranchStock({ sku, unit }: { sku: string; unit: string }) {
  const consolidated = useRpcQuery('ConsolidatedStockQuery', { search: sku });
  if (consolidated.error) {
    return <ErrorState error={consolidated.error} operation="ConsolidatedStockQuery" onRetry={consolidated.reload} retrying={consolidated.fetching} />;
  }
  if (!consolidated.data) return <LoadingState label="Cargando las existencias por sucursal…" rows={2} />;
  const row = consolidatedRowOf(consolidated.data, sku);
  if (!row) {
    return <EmptyState size="sm" title="Sin existencias en sus sucursales" description="Ninguna de las sucursales que usted ve tiene unidades de este producto." />;
  }
  return (
    <div className="space-y-3" data-testid="ficha-sucursales">
      <ul className="divide-y divide-border rounded-xl border border-border">
        {consolidated.data.branches.map((branch, index) => (
          <li key={branch.id} className="flex flex-wrap justify-between gap-x-4 px-3 py-2 text-sm">
            <span>
              <span className="font-medium">{branch.code}</span> <span className="text-text-muted">· {branch.name}</span>
            </span>
            <span className="font-semibold tabular-nums">{quantityWithUnit(row.byBranch[index] ?? 0, unit)}</span>
          </li>
        ))}
        <li className="flex flex-wrap justify-between gap-x-4 px-3 py-2 text-sm">
          <span className="text-text-muted">En tránsito entre sucursales</span>
          <span className="font-semibold tabular-nums">{quantityWithUnit(row.inTransit, unit)}</span>
        </li>
      </ul>
      <DetailList
        items={[
          { label: 'Total de la empresa', value: quantityWithUnit(row.total, unit) },
          { label: 'Valor (costo promedio de cada almacén)', value: formatMoney(row.value) },
        ]}
      />
    </div>
  );
}
