// Facturación › Documentos fiscales (FiscalDocumentsView del escritorio): facturas y notas crédito-débito del SIN.
//   - Lista (`GetFiscalDocumentsQuery`): fechas (últimos 30 días por defecto), tipo, estado y búsqueda (número, CUF, NIT/CI
//     o nombre) los filtra el SERVIDOR; sucursal, punto de venta, tipo de emisión y venta, la página. Tabla ordenable y
//     paginada con acciones por fila, exportar CSV y el resumen del período PLEGADO («Ver resumen del período», P-10).
//   - Detalle lateral (`GetFiscalDocumentQuery`) con la bitácora del SIN, las entregas y todas las acciones: ver e
//     imprimir / PDF (`GetFiscalPrintModelQuery`, `RenderFiscalDocumentQuery`, `RecordFiscalDeliveryCommand`), enviar por
//     correo (`SendFiscalDocumentEmailCommand`), verificar en el SIN (`CheckFiscalDocumentStatusCommand`), anular
//     (`VoidFiscalDocumentCommand`), revertir la anulación (`RevertFiscalVoidCommand`), re-emitir
//     (`ReissueFiscalDocumentCommand`), devolver con nota crédito-débito (en «Ventas») y el XML exacto.
//
// Dirección: `?documento=<id>` abre ese documento (así llegan la caja y ventas, y el detalle abierto se puede compartir);
// `?venta=<número>` muestra los documentos de esa venta (todas las fechas) y abre el vigente; `&anular=1` empieza a
// anularlo; `?periodo=hoy` (botón del tablero) muestra el día; `?q=<CUF>` busca (lo usa la caja).

import { BarChart3, Ban, CheckCircle2, Clock, Copy, Download, Eye, FilePlus, FileText, Mail, Printer, RadioTower, RefreshCw, Search, Undo2, XCircle } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcCommand, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Alert,
  Button,
  Collapsible,
  ConfirmDialog,
  DataTable,
  DateRangeField,
  FilterBar,
  Page,
  SearchField,
  SelectField,
  SidePanel,
  StatCard,
  StatusBadge,
  Toolbar,
  statusOptions,
  useNotify,
  type DataTableColumn,
  type RowActionItem,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatMoney, formatNumber, laPazToday } from '@/4-presentation/panel/lib';
import { DocumentDetail } from './DocumentDetail';
import { copyText } from './files';
import {
  CSV_COLUMNS,
  EMISSION_OPTIONS,
  FISCAL_STATUSES,
  KINDS,
  PARAMS,
  branchOptions,
  currentDocumentOfSale,
  documentFilters,
  documentTitle,
  filterItems,
  formatFiscalDate,
  formatFiscalTime,
  isDeliverable,
  isReissuable,
  listPayload,
  periodSummary,
  plainMessage,
  pointOptions,
  returnLink,
  serverRange,
  shortCuf,
  statusLabel,
  toItems,
  type DocumentItem,
  type DocumentRecord,
} from './fiscal';
import { PrintDialog } from './PrintDialog';
import { ReissueDialog } from './ReissueDialog';
import { SendEmailDialog } from './SendEmailDialog';
import { VoidDialog } from './VoidDialog';
import { XmlDialog } from './XmlDialog';

/** Columnas de la tabla (fuera del componente: no se vuelven a crear en cada dibujo). */
const COLUMNS: DataTableColumn<DocumentItem>[] = [
  {
    id: 'numero',
    header: 'Documento',
    value: (item) => item.row.number,
    card: 'title',
    cell: (item) => (
      <span className="block min-w-36">
        <span className="block">{item.title}</span>
        <span className="block font-mono text-xs font-normal text-text-muted">{shortCuf(item.row.cuf)}</span>
      </span>
    ),
  },
  {
    id: 'fecha',
    header: 'Fecha y hora',
    value: (item) => item.row.issuedAt,
    cell: (item) => formatFiscalTime(item.row.issuedAt),
    className: 'whitespace-nowrap',
  },
  { id: 'lugar', header: 'Sucursal · PV', value: (item) => item.placeText, className: 'whitespace-nowrap' },
  {
    id: 'comprador',
    header: 'Comprador',
    value: (item) => item.row.buyerName,
    cell: (item) => (
      <span className="block min-w-40">
        <span className="block">{item.row.buyerName}</span>
        <span className="block text-xs text-text-muted">{item.row.buyerDocument}</span>
      </span>
    ),
  },
  { id: 'venta', header: 'Venta', value: (item) => item.row.saleNumber, className: 'whitespace-nowrap' },
  {
    id: 'estado',
    header: 'Estado',
    value: (item) => item.statusText,
    cell: (item) => (
      <span className="flex flex-col items-start gap-1">
        <StatusBadge status={item.row.status} statuses={FISCAL_STATUSES} />
        {item.row.isReverted && <span className="text-xs text-text-muted">Anulación revertida</span>}
        {item.isOffline && item.row.status !== 'Offline' && <span className="text-xs text-text-muted">Emitida fuera de línea</span>}
      </span>
    ),
  },
  {
    id: 'total',
    header: 'Total',
    align: 'end',
    value: (item) => item.row.total,
    cell: (item) => formatMoney(item.row.total),
    footer: (rows) => formatMoney(rows.reduce((sum, item) => sum + item.row.total, 0)),
  },
];

export function FiscalDocumentsPage() {
  const notify = useNotify();
  const { can, canRun } = usePermissions();
  const [today] = useState(() => laPazToday());
  const table = useTableState({ filters: documentFilters(today), sort: { column: 'fecha', direction: 'desc' } });
  const filters = table.filters;
  const [params, setParams] = useSearchParams();

  // Atajos de la dirección: `?periodo=hoy` (tablero) y `?venta=` sin fechas (= todas las fechas). Mientras se corrige la
  // dirección no se consulta (así no se pide dos veces).
  const period = params.get(PARAMS.period);
  const saleWithoutDates = filters.venta !== '' && !params.has('desde') && !params.has('hasta');
  const redirecting = period === 'hoy' || saleWithoutDates;
  useEffect(() => {
    if (!redirecting) return;
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        if (next.get(PARAMS.period) === 'hoy') {
          next.delete(PARAMS.period);
          next.set('desde', today);
          next.set('hasta', today);
        } else if (next.get('venta') && !next.has('desde') && !next.has('hasta')) {
          next.set('desde', '');
          next.set('hasta', '');
        }
        return next;
      },
      { replace: true },
    );
  }, [redirecting, setParams, today]);

  // Lo que filtra el SERVIDOR va en el pedido; el resto se filtra en la página.
  const range = serverRange(table.dateRange(), today);
  const documents = useRpcQuery('GetFiscalDocumentsQuery', listPayload(filters, range ?? { from: today, to: today }), { enabled: range !== null && !redirecting });
  const items = useMemo(() => toItems(documents.data ?? []), [documents.data]);
  const rows = useMemo(() => filterItems(items, filters), [items, filters]);
  const branches = useMemo(() => branchOptions(items), [items]);
  const points = useMemo(() => pointOptions(items, filters.sucursal), [items, filters.sucursal]);

  // Detalle lateral (estado de la página). El panel lateral se cierra solo cuando cambia la dirección (al filtrar), así
  // que `?documento=` y `&anular=` se leen al LLEGAR: primero se quitan de la dirección y recién después se abre el panel.
  const requested = params.get(PARAMS.document);
  const requestedVoid = params.get(PARAMS.startVoid) === '1';
  const [arrival, setArrival] = useState<{ documentId: string; startVoid: boolean; opened: boolean } | null>(null);
  if (requested && (arrival?.documentId !== requested || arrival.opened)) setArrival({ documentId: requested, startVoid: requestedVoid, opened: false });
  useEffect(() => {
    if (!requested && !params.has(PARAMS.startVoid)) return;
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.delete(PARAMS.document);
        next.delete(PARAMS.startVoid);
        return next;
      },
      { replace: true },
    );
  }, [requested, params, setParams]);
  const [openId, setOpenId] = useState<string | null>(null);
  if (!requested && arrival && !arrival.opened) {
    setArrival({ ...arrival, opened: true });
    setOpenId(arrival.documentId);
  }
  // Se guarda el último documento abierto para que el panel se deslice con su contenido al cerrar.
  const [shownId, setShownId] = useState<string | null>(null);
  if (openId && openId !== shownId) setShownId(openId);
  const detail = useRpcQuery('GetFiscalDocumentQuery', { documentId: shownId ?? '' }, { enabled: openId !== null && shownId !== null, keepPreviousData: false });
  const openDocument = useCallback((documentId: string) => setOpenId(documentId), []);
  const closeDocument = () => {
    setOpenId(null);
    setArrival((current) => (current ? { ...current, startVoid: false } : current));
  };

  // Guarda del menú «⋯»: React propaga el clic de una opción (dibujada aparte, en un portal) a la fila, que abriría el
  // detalle. Mientras se elige una acción, la fila no se abre.
  const pickingAction = useRef(false);
  const fromMenu = (action: () => void) => () => {
    pickingAction.current = true;
    action();
    setTimeout(() => {
      pickingAction.current = false;
    }, 0);
  };

  // Diálogos (cada uno se monta de nuevo en cada apertura).
  const [dialogSession, setDialogSession] = useState(0);
  const [printing, setPrinting] = useState<DocumentRecord | null>(null);
  const [emailing, setEmailing] = useState<DocumentRecord | null>(null);
  const [voiding, setVoiding] = useState<DocumentRecord | null>(null);
  const [reissuing, setReissuing] = useState<DocumentRecord | null>(null);
  const [reverting, setReverting] = useState<DocumentRecord | null>(null);
  const [xml, setXml] = useState<{ title: string; xml: string } | null>(null);
  const openDialog = (setter: (row: DocumentRecord) => void) => (row: DocumentRecord) => {
    setDialogSession((count) => count + 1);
    setter(row);
  };
  const askPrint = openDialog(setPrinting);
  const askEmail = openDialog(setEmailing);
  const askVoid = openDialog(setVoiding);
  const askReissue = openDialog(setReissuing);

  const refresh = () => {
    documents.reload();
    if (openId) detail.reload();
  };

  const verify = useRpcCommand('CheckFiscalDocumentStatusCommand', { errorTitle: 'No se pudo verificar en el SIN' });
  const revert = useRpcCommand('RevertFiscalVoidCommand', { notifyError: false });

  const verifyNow = async (row: DocumentRecord) => {
    const outcome = await verify.run({ documentId: row.id });
    if (!outcome.ok) return;
    notify.info('Estado en el SIN', plainMessage(outcome.result));
    refresh();
  };

  const copyCuf = async (row: DocumentRecord) => {
    if (await copyText(row.cuf)) notify.success('CUF copiado', shortCuf(row.cuf));
    else notify.warning('No se pudo copiar', 'El navegador no permite copiar aquí: seleccione el CUF en el detalle y cópielo a mano.');
  };

  // `&anular=1`: con el documento abierto, el diálogo de anulación se abre solo; si no se puede anular, se explica por qué.
  const canVoidDocuments = canRun('VoidFiscalDocumentCommand');
  const voidRequested = arrival?.startVoid && openId === arrival.documentId && detail.data?.row.id === openId ? detail.data.row : null;
  const voidOnArrival = voidRequested && voidRequested.canVoid && canVoidDocuments ? voidRequested : null;
  const endArrivalVoid = useCallback(() => setArrival((current) => (current?.startVoid ? { ...current, startVoid: false } : current)), []);
  const warned = useRef<string | null>(null);
  useEffect(() => {
    if (!voidRequested || voidOnArrival || warned.current === voidRequested.id) return;
    warned.current = voidRequested.id;
    if (!canVoidDocuments) {
      notify.warning('No puede anular documentos', 'Pida al administrador el permiso para anular y revertir documentos fiscales.');
      return;
    }
    notify.warning(
      'No se puede anular',
      voidRequested.status === 'Voided'
        ? 'El documento ya está anulado.'
        : `El documento está ${statusLabel(voidRequested).toLocaleLowerCase('es')}: solo se anula un documento válido y dentro del plazo (hasta el ${formatFiscalDate(voidRequested.voidDeadline)}).`,
    );
  }, [voidRequested, voidOnArrival, canVoidDocuments, notify]);

  // `?venta=<número>`: al llegar la lista, abre el documento vigente de esa venta (una vez por venta); si no tiene, lo avisa.
  const [handledSale, setHandledSale] = useState<string | null>(null);
  const saleRows = filters.venta !== '' && !redirecting && !documents.fetching ? documents.data : undefined;
  const saleDocument = saleRows ? currentDocumentOfSale(saleRows, filters.venta) : null;
  if (saleRows && handledSale !== filters.venta) {
    setHandledSale(filters.venta);
    if (saleDocument && !openId) setOpenId(saleDocument.id);
  }
  const saleMissing = saleRows !== undefined && handledSale === filters.venta && saleDocument === null ? filters.venta : null;
  const notifiedSale = useRef<string | null>(null);
  useEffect(() => {
    if (!saleMissing || notifiedSale.current === saleMissing) return;
    notifiedSale.current = saleMissing;
    notify.info('Sin documento fiscal', `La venta ${saleMissing} no tiene facturas del SIN en estas fechas.`);
  }, [saleMissing, notify]);

  const canPrint = canRun('GetFiscalPrintModelQuery');
  const canEmail = canRun('SendFiscalDocumentEmailCommand');
  const canVerify = canRun('CheckFiscalDocumentStatusCommand');
  const canRevert = canRun('RevertFiscalVoidCommand');
  const canReissue = canRun('ReissueFiscalDocumentCommand');
  const canReturn = can('sales.view') && can('billing.void') && can('sales.pos.operate');
  const canSeeSale = can('sales.view');

  /** Acciones de un documento: las mismas en el menú «⋯» de la fila y al pie del detalle. */
  const actionsFor = (row: DocumentRecord, wrap: (action: () => void) => () => void): RowActionItem[] => [
    { label: 'Ver e imprimir', icon: <Printer />, onSelect: wrap(() => askPrint(row)), hidden: !canPrint },
    { label: 'Enviar por correo', icon: <Mail />, onSelect: wrap(() => askEmail(row)), hidden: !canEmail || !isDeliverable(row) },
    { label: 'Verificar en el SIN', icon: <RadioTower />, onSelect: wrap(() => void verifyNow(row)), hidden: !canVerify },
    { label: 'Re-emitir', icon: <FilePlus />, onSelect: wrap(() => askReissue(row)), hidden: !canReissue || !isReissuable(row) },
    { label: 'Copiar CUF', icon: <Copy />, onSelect: wrap(() => void copyCuf(row)) },
    { label: 'Revertir la anulación', icon: <Undo2 />, onSelect: wrap(() => setReverting(row)), hidden: !canRevert || !row.canRevert },
    { label: 'Anular ante el SIN', icon: <Ban />, tone: 'danger', onSelect: wrap(() => askVoid(row)), hidden: !canVoidDocuments || !row.canVoid },
  ];

  const exportRows = () => {
    const file = exportCsv('documentos-fiscales', CSV_COLUMNS, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const summary = useMemo(() => periodSummary(rows.map((item) => item.row)), [rows]);
  const shown = detail.data;
  const shownRow = shown?.row;

  return (
    <Page
      title="Documentos fiscales"
      description="Facturas y notas crédito-débito del SIN: consulta, impresión, correo, verificación, anulación y re-emisión."
      actions={
        <Button variant="outline" leftIcon={<RadioTower />} to={ROUTES.panelModule('siat')}>
          Estado del SIAT
        </Button>
      }
    >
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Número, CUF, NIT/CI o nombre" value={filters.q} onChange={(q) => table.setFilter('q', q)} hint="Se busca en el servidor." />
        <SelectField label="Tipo" allLabel="Facturas y notas" value={filters.tipo} onChange={(value) => table.setFilter('tipo', value)} options={statusOptions(KINDS)} />
        <SelectField label="Estado" allLabel="Todos los estados" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(FISCAL_STATUSES)} />
        <SelectField
          label="Tipo de emisión"
          allLabel="En línea y fuera de línea"
          value={filters.emision}
          onChange={(value) => table.setFilter('emision', value)}
          options={EMISSION_OPTIONS}
        />
        <SelectField
          label="Sucursal"
          allLabel="Todas las sucursales"
          value={filters.sucursal}
          onChange={(value) => table.setFilters({ sucursal: value, punto: '' })}
          options={branches}
        />
        <SelectField label="Punto de venta" allLabel="Todos los puntos" value={filters.punto} onChange={(value) => table.setFilter('punto', value)} options={points} />
        <DateRangeField
          label="Fechas de emisión"
          value={table.dateRange()}
          onChange={(value) => table.setDateRange(value)}
          hint="Por defecto, los últimos 30 días. «Todas» trae hasta 5.000 documentos, los más recientes."
        />
      </FilterBar>

      {filters.venta && (
        <Alert
          tone="info"
          title={`Documentos de la venta ${filters.venta}`}
          actions={
            <Button variant="outline" onClick={() => table.setFilter('venta', '')}>
              Ver todos los documentos
            </Button>
          }
        >
          Se muestran solo los documentos fiscales de esa venta (la factura vigente, las anteriores y sus notas).
        </Alert>
      )}

      <Toolbar
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={documents.fetching && !documents.loading} onClick={documents.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {documents.data && (
          <span className="text-sm text-text-muted" data-testid="documentos-resumen">
            {formatNumber(rows.length)} de {formatNumber(items.length)} documentos · {formatNumber(summary.invoices)} facturas · {formatNumber(summary.notes)} notas
          </span>
        )}
      </Toolbar>

      <DataTable
        caption="Documentos fiscales"
        columns={COLUMNS}
        rows={documents.data ? rows : undefined}
        rowKey={(item) => item.key}
        rowLabel={(item) => `la ${item.kindText.toLocaleLowerCase('es')} N° ${item.row.number}`}
        loading={documents.loading}
        refreshing={documents.fetching && !documents.loading}
        error={documents.error}
        onRetry={documents.reload}
        operation="GetFiscalDocumentsQuery"
        {...table.tableProps}
        onRowOpen={(item) => {
          if (!pickingAction.current) openDocument(item.row.id);
        }}
        activeRowKey={openId}
        rowActions={(item) => [{ label: 'Ver detalle', icon: <Eye />, onSelect: fromMenu(() => openDocument(item.row.id)) }, ...actionsFor(item.row, fromMenu)]}
        empty={{
          title: items.length === 0 ? 'No hay documentos fiscales en estas fechas' : 'No hay documentos con estos filtros',
          description: items.length === 0 ? 'Pruebe con otras fechas o con «Todas».' : 'Pruebe con otros filtros o límpielos.',
          icon: <Search />,
          action: (
            <Button variant="outline" onClick={table.clearFilters}>
              Limpiar filtros
            </Button>
          ),
        }}
      />

      {documents.data && (
        <Collapsible label="Ver resumen del período" openLabel="Ocultar resumen del período" icon={<BarChart3 />} description="Documentos, facturado válido, pendientes y anulados de la lista filtrada.">
          <div className="grid grid-cols-[repeat(auto-fit,minmax(11rem,1fr))] gap-3" data-testid="resumen-periodo">
            <StatCard label="Documentos" value={formatNumber(summary.documents)} hint={`${formatNumber(summary.invoices)} facturas · ${formatNumber(summary.notes)} notas`} icon={<FileText />} />
            <StatCard label="Facturado válido" value={formatMoney(summary.validTotal)} hint={`${formatNumber(summary.validInvoices)} facturas válidas en el SIN`} tone="success" icon={<CheckCircle2 />} />
            <StatCard
              label="Por enviar o validar"
              value={formatNumber(summary.waiting)}
              hint={summary.waiting === 0 ? 'Todo validado' : `${formatNumber(summary.waitingOffline)} emitidos fuera de línea`}
              tone={summary.waiting > 0 ? 'warning' : 'default'}
              icon={<Clock />}
            />
            <StatCard
              label="Anulados y rechazados"
              value={formatNumber(summary.bad)}
              hint={summary.bad === 0 ? 'Sin anulaciones ni rechazos' : `${formatNumber(summary.voided)} anulados`}
              tone={summary.bad > 0 ? 'danger' : 'default'}
              icon={<XCircle />}
            />
          </div>
        </Collapsible>
      )}

      <SidePanel
        open={openId !== null}
        onClose={closeDocument}
        title={shownRow ? documentTitle(shownRow) : 'Documento fiscal'}
        description={shownRow ? `${formatFiscalTime(shownRow.issuedAt)} · ${shownRow.branchCode} · punto de venta ${shownRow.pointOfSaleCode}` : undefined}
        headerExtra={shownRow && <StatusBadge status={shownRow.status} statuses={FISCAL_STATUSES} />}
        loading={detail.loading}
        error={detail.error}
        onRetry={detail.reload}
        footer={
          shownRow && (
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2" data-testid="acciones-documento">
              {actionsFor(shownRow, (action) => action)
                .filter((action) => !action.hidden && action.label !== 'Copiar CUF')
                .map((action) => (
                  <Button key={action.label} variant={action.tone === 'danger' ? 'danger' : 'outline'} leftIcon={action.icon} fullWidth onClick={action.onSelect}>
                    {action.label}
                  </Button>
                ))}
              {canReturn && shownRow.canCreditNote && shownRow.saleNumber && (
                <Button variant="outline" leftIcon={<Undo2 />} fullWidth to={ROUTES.panelModule(returnLink(shownRow.saleNumber))}>
                  Devolver productos (nota)
                </Button>
              )}
            </div>
          )
        }
      >
        {shown && (
          <DocumentDetail
            detail={shown}
            canSeeSale={canSeeSale}
            onOpenDocument={openDocument}
            onCopyCuf={() => void copyCuf(shown.row)}
            onShowXml={() => setXml({ title: documentTitle(shown.row), xml: shown.xml })}
          />
        )}
      </SidePanel>

      <PrintDialog key={`imprimir-${dialogSession}`} target={printing} onClose={() => setPrinting(null)} onDelivered={refresh} />
      <SendEmailDialog key={`correo-${dialogSession}`} target={emailing} onClose={() => setEmailing(null)} onDone={refresh} />
      <VoidDialog
        key={`anular-${dialogSession}`}
        target={voiding ?? voidOnArrival}
        onClose={() => {
          setVoiding(null);
          endArrivalVoid();
        }}
        onDone={refresh}
      />
      <ReissueDialog
        key={`reemitir-${dialogSession}`}
        target={reissuing}
        onClose={() => setReissuing(null)}
        onDone={(documentId) => {
          documents.reload();
          openDocument(documentId);
        }}
      />
      <XmlDialog target={xml} onClose={() => setXml(null)} />
      <ConfirmDialog
        open={reverting !== null}
        onClose={() => {
          setReverting(null);
          revert.reset();
        }}
        title={reverting ? `¿Revertir la anulación de N° ${reverting.number}?` : 'Revertir la anulación'}
        message={
          reverting
            ? `El documento vuelve a ser VÁLIDO ante el SIN. La reversión se hace UNA sola vez y dentro del plazo (hasta el ${formatFiscalDate(reverting.voidDeadline)}): después ya no se podrá anular. La mercadería devuelta (si la hubo) no vuelve a salir: revise el stock. El comprador recibe el aviso por correo.`
            : ''
        }
        confirmLabel="Revertir anulación"
        error={revert.errorText}
        onConfirm={async () => {
          if (!reverting) return false;
          const outcome = await revert.run({ documentId: reverting.id });
          if (outcome.ok) {
            notify.success('Anulación revertida', plainMessage(outcome.result));
            refresh();
          }
          return outcome;
        }}
      />
    </Page>
  );
}
