// Módulo «Documentos fiscales» · funciones puras (sin React): estados y textos del SIN (los del escritorio: `FiscalText`),
// hora fiscal, filtros de la lista, filas listas para mostrar, resumen del período, columnas del CSV, datos del comprador
// para re-emitir (`BuyerForm` del escritorio) y el motivo de anulación sugerido. La regla que manda es la del servidor:
// aquí solo se muestra, se filtra y se guía antes de enviar (regla P-01).

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses, statusOf } from '@/4-presentation/panel/kit';
import { addDays, formatDateTime, formatMoney, rangeError, type CsvColumn, type DateRange } from '@/4-presentation/panel/lib';

// ---------------------------------------------------------------------------------------------------- tipos del contrato

/** Una fila de `GetFiscalDocumentsQuery` tal como la manda el servidor. */
export type DocumentRecord = RpcResponseOf<'GetFiscalDocumentsQuery'>[number];
/** El detalle de `GetFiscalDocumentQuery`. */
export type DocumentDetailData = RpcResponseOf<'GetFiscalDocumentQuery'>;
export type DocumentEventData = DocumentDetailData['events'][number];
export type DocumentDeliveryData = DocumentDetailData['deliveries'][number];
export type DocumentLineData = DocumentDetailData['lines'][number];
/** Un valor de un catálogo del SIN (`GetSiatCatalogQuery`). */
export type CatalogItemData = RpcResponseOf<'GetSiatCatalogQuery'>[number];
/** La representación gráfica (`GetFiscalPrintModelQuery`). */
export type PrintModelData = RpcResponseOf<'GetFiscalPrintModelQuery'>;
/** Datos del comprador que viajan en `ReissueFiscalDocumentCommand`. */
export type BuyerPayload = NonNullable<RpcRequestOf<'ReissueFiscalDocumentCommand'>['buyer']>;

/** Catálogos del SIN que usa la pantalla (nombres de `SiatCatalogNames`). */
export const CATALOG_VOID_REASONS = 'MOTIVOS_ANULACION';
export const CATALOG_DOCUMENT_TYPES = 'TIPO_DOCUMENTO_IDENTIDAD';

// ---------------------------------------------------------------------------------------------------- estados y textos

/** Estado del documento fiscal (enumeración `FiscalDocumentStatus` del servidor). */
export const FISCAL_STATUSES = defineStatuses({
  Pending: { label: 'Pendiente', tone: 'warning' },
  Valid: { label: 'Válida', tone: 'success' },
  Rejected: { label: 'Rechazada', tone: 'danger' },
  NoResponse: { label: 'Sin respuesta', tone: 'warning' },
  Offline: { label: 'Fuera de línea', tone: 'info' },
  InPackage: { label: 'En paquete', tone: 'info' },
  PackageRejected: { label: 'Observada', tone: 'danger' },
  DuplicateToVoid: { label: 'Duplicada: anular', tone: 'danger' },
  Voided: { label: 'Anulada', tone: 'neutral' },
  Discarded: { label: 'Descartada', tone: 'neutral' },
});

/** Estado para mostrar (una anulación revertida lo dice). */
export function statusLabel(row: Pick<DocumentRecord, 'status' | 'isReverted'>): string {
  const label = statusOf(FISCAL_STATUSES, row.status).label;
  return row.status === 'Valid' && row.isReverted ? `${label} (anulación revertida)` : label;
}

const STATUS_HELP: Readonly<Record<string, string>> = {
  Pending: 'Emitida en línea; se está enviando al SIN.',
  Valid: 'El SIN la recibió y la validó.',
  Rejected: 'El SIN la rechazó: la venta recibe una factura nueva (vea los mensajes del SIN).',
  NoResponse: 'Se perdió la respuesta del SIN: la venta se re-emitió fuera de línea y esta se verifica al volver la conexión.',
  Offline: 'Emitida sin conexión con el SIN: se envía sola en un paquete al volver la comunicación.',
  InPackage: 'Enviada en un paquete de contingencia; falta la validación del SIN.',
  PackageRejected: 'El SIN la observó al validar el paquete: se re-emite.',
  DuplicateToVoid: 'El SIN la registró y la venta ya tiene otra factura: hay que anularla.',
  Voided: 'Anulada ante el SIN.',
  Discarded: 'Nunca llegó al SIN y se reemplazó por otra: queda solo como historia.',
};

/** Qué significa el estado (ayuda del detalle). */
export function statusHelp(status: string): string {
  return STATUS_HELP[status] ?? 'Consulte la bitácora del SIN del documento.';
}

/** Tono del aviso del estado en el detalle. */
export function statusAlertTone(status: string): 'success' | 'warning' | 'danger' | 'info' {
  const tone = statusOf(FISCAL_STATUSES, status).tone;
  if (tone === 'success' || tone === 'warning' || tone === 'danger') return tone;
  return 'info';
}

/** Tipo de documento (enumeración `FiscalDocumentKind`). */
export const KINDS = defineStatuses({
  Invoice: { label: 'Factura', tone: 'accent' },
  CreditDebitNote: { label: 'Nota crédito-débito', tone: 'info' },
});

export function kindLabel(kind: string): string {
  return statusOf(KINDS, kind).label;
}

/** «Factura N° 123» · «Nota crédito-débito N° 5». */
export function documentTitle(row: Pick<DocumentRecord, 'kind' | 'number'>): string {
  return `${kindLabel(row.kind)} N° ${row.number}`;
}

/** Tipo de emisión del SIN: 1 en línea; 2 fuera de línea (también las facturas manuales CAFC transcritas). */
export const EMISSION_ONLINE = 1;
export const EMISSION_OFFLINE = 2;
export const EMISSION_OPTIONS: readonly { value: string; label: string }[] = [
  { value: String(EMISSION_ONLINE), label: 'En línea' },
  { value: String(EMISSION_OFFLINE), label: 'Fuera de línea o manual (CAFC)' },
];

export function emissionText(emissionType: number, cafc: string | null): string {
  if (emissionType !== EMISSION_OFFLINE) return 'Emitida en línea (tipo de emisión 1).';
  return cafc ? `Factura manual transcrita · CAFC ${cafc}.` : 'Emitida fuera de línea (tipo de emisión 2).';
}

/** Pasos de la bitácora del SIN (enumeración `FiscalDocumentAction`). */
export const EVENT_ACTIONS = defineStatuses({
  Issued: { label: 'Emitido', tone: 'accent' },
  Sent: { label: 'Enviado al SIN', tone: 'accent' },
  Accepted: { label: 'Validado por el SIN', tone: 'success' },
  Rejected: { label: 'Rechazado por el SIN', tone: 'danger' },
  NoResponse: { label: 'Sin respuesta del SIN', tone: 'warning' },
  Reissued: { label: 'Re-emitido', tone: 'accent' },
  Packaged: { label: 'Incluido en un paquete', tone: 'accent' },
  PackageValidated: { label: 'Paquete validado', tone: 'success' },
  PackageRejected: { label: 'Observado en el paquete', tone: 'danger' },
  StatusChecked: { label: 'Estado verificado en el SIN', tone: 'accent' },
  VoidRequested: { label: 'Anulación solicitada', tone: 'accent' },
  Voided: { label: 'Anulado en el SIN', tone: 'warning' },
  VoidFailed: { label: 'Anulación no aceptada', tone: 'danger' },
  Reverted: { label: 'Anulación revertida', tone: 'success' },
  RevertFailed: { label: 'Reversión no aceptada', tone: 'danger' },
  Discarded: { label: 'Descartado', tone: 'warning' },
  Delivered: { label: 'Entregado al comprador', tone: 'info' },
  Printed: { label: 'Impreso', tone: 'info' },
});

/** «Validado por el SIN · código 908». */
export function eventTitle(event: Pick<DocumentEventData, 'action' | 'siatCode'>): string {
  const label = statusOf(EVENT_ACTIONS, event.action).label;
  return event.siatCode != null ? `${label} · código ${event.siatCode}` : label;
}

/** Canal de una entrega al comprador (`FiscalDeliveryChannel`). */
export function channelLabel(channel: string): string {
  if (channel === 'Email') return 'Correo';
  if (channel === 'Print') return 'Impresión';
  return 'PDF';
}

/** Quita el ✔ / ⚠ / ✖ del principio de un mensaje del servidor (la pantalla pone su propio ícono). */
export function plainMessage(text: string | null | undefined): string {
  return (text ?? '').replace(/^[\s✔⚠✖]+/u, '').trim();
}

/** «FACTURA MAL EMITIDA» → «Factura mal emitida» (los catálogos del SIN llegan en mayúsculas). */
export function sentenceCase(text: string): string {
  const clean = text.trim();
  if (clean.length === 0) return clean;
  const lower = clean.toLocaleLowerCase('es');
  return lower.charAt(0).toLocaleUpperCase('es') + lower.slice(1);
}

/** CUF abreviado para las tablas (el completo va en el detalle, con «Copiar»). */
export function shortCuf(cuf: string): string {
  return cuf.length > 16 ? `${cuf.slice(0, 8)}…${cuf.slice(-6)}` : cuf;
}

// ---------------------------------------------------------------------------------------------------- hora fiscal

const HAS_ZONE = /(?:Z|[+-]\d{2}:?\d{2})$/i;
const FISCAL = /^(\d{4})-(\d{2})-(\d{2})(?:T(\d{2}):(\d{2})(?::(\d{2}))?)?/;

/**
 * Fecha y hora fiscal: el SIN usa la hora de Bolivia y el servidor la manda SIN zona («2026-09-29T10:32:05.123»); se
 * muestra tal cual, sin correrla («29/09/2026 10:32:05»). Con zona, se muestra con la hora de La Paz.
 */
export function formatFiscalTime(value: string | null | undefined, seconds = true): string {
  const text = (value ?? '').trim();
  if (text.length === 0) return '—';
  if (HAS_ZONE.test(text)) return formatDateTime(text);
  const match = FISCAL.exec(text);
  if (!match) return text;
  const day = `${match[3]}/${match[2]}/${match[1]}`;
  if (!match[4]) return day;
  return `${day} ${match[4]}:${match[5]}${seconds && match[6] ? `:${match[6]}` : ''}`;
}

/** Solo el día de una fecha fiscal: «09/10/2026». */
export function formatFiscalDate(value: string | null | undefined): string {
  const text = (value ?? '').trim();
  const match = FISCAL.exec(text);
  return match ? `${match[3]}/${match[2]}/${match[1]}` : '—';
}

/** Día fiscal en formato ISO («2026-09-29»); null si no se puede leer. */
export function fiscalDay(value: string | null | undefined): string | null {
  const match = FISCAL.exec((value ?? '').trim());
  return match ? `${match[1]}-${match[2]}-${match[3]}` : null;
}

// ---------------------------------------------------------------------------------------------------- filtros

/** Filtros de la lista (en la dirección). Por defecto, los últimos 30 días (como el escritorio). */
export function documentFilters(today: string) {
  return { q: '', desde: addDays(today, -29), hasta: today, tipo: '', estado: '', sucursal: '', punto: '', emision: '', venta: '' };
}
export type DocumentFilters = ReturnType<typeof documentFilters>;

/** Parámetros de la dirección que NO son filtros: el documento abierto, empezar a anular y atajos del tablero. */
export const PARAMS = { document: 'documento', startVoid: 'anular', period: 'periodo' } as const;

/** Desde cuándo se piden los documentos con el filtro de fechas vacío («Todas»). */
export const ALL_DATES_FROM = '2000-01-01';

export interface DayRange {
  from: string;
  to: string;
}

/**
 * Rango para el servidor (`GetFiscalDocumentsQuery` exige las dos fechas): vacío = todo hasta hoy; solo «Desde» = hasta
 * hoy; solo «Hasta» = desde el principio. null si las fechas escritas no sirven (el campo ya lo avisa).
 */
export function serverRange(range: DateRange, today: string): DayRange | null {
  if (rangeError(range)) return null;
  const from = range.from ?? ALL_DATES_FROM;
  const to = range.to ?? (from > today ? from : today);
  return { from, to };
}

type ListPayload = RpcRequestOf<'GetFiscalDocumentsQuery'>;

/**
 * El pedido de la lista: fechas, tipo, estado y búsqueda (número, CUF, NIT/CI o nombre) los filtra el servidor. Un valor
 * raro de la dirección (un estado que no existe) no se envía.
 */
export function listPayload(filters: DocumentFilters, range: DayRange): ListPayload {
  const status = Object.prototype.hasOwnProperty.call(FISCAL_STATUSES, filters.estado) ? (filters.estado as NonNullable<ListPayload['status']>) : null;
  const kind = Object.prototype.hasOwnProperty.call(KINDS, filters.tipo) ? (filters.tipo as NonNullable<ListPayload['kind']>) : null;
  const search = filters.q.trim();
  return { from: range.from, to: range.to, status, kind, search: search.length > 0 ? search : null };
}

/** Número de venta escrito a mano → como lo guarda el servidor («f-cm-000123 » → «F-CM-000123»). */
export function normalizeSale(value: string): string {
  return value.trim().toUpperCase();
}

// ---------------------------------------------------------------------------------------------------- filas

/** Un documento listo para la tabla. */
export interface DocumentItem {
  key: string;
  row: DocumentRecord;
  title: string;
  kindText: string;
  statusText: string;
  /** «CM · PV 1». */
  placeText: string;
  /** Valor del filtro «Punto de venta» («CM-1»). */
  pointKey: string;
  isOffline: boolean;
}

export function toItems(rows: readonly DocumentRecord[]): DocumentItem[] {
  return rows.map((row) => ({
    key: row.id,
    row,
    title: documentTitle(row),
    kindText: kindLabel(row.kind),
    statusText: statusLabel(row),
    placeText: `${row.branchCode} · PV ${row.pointOfSaleCode}`,
    pointKey: `${row.branchCode}-${row.pointOfSaleCode}`,
    isOffline: row.emissionType === EMISSION_OFFLINE,
  }));
}

/** Lo que se filtra en la página (fechas, tipo, estado y búsqueda los filtra el servidor). */
export function filterItems(items: readonly DocumentItem[], filters: DocumentFilters): DocumentItem[] {
  const sale = normalizeSale(filters.venta);
  return items.filter(
    (item) =>
      (!filters.sucursal || item.row.branchCode === filters.sucursal) &&
      (!filters.punto || item.pointKey === filters.punto) &&
      (!filters.emision || String(item.row.emissionType) === filters.emision) &&
      (!sale || (item.row.saleNumber ?? '').toUpperCase() === sale),
  );
}

/** Sucursales de las filas (para la lista desplegable). */
export function branchOptions(items: readonly DocumentItem[]): { value: string; label: string }[] {
  return [...new Set(items.map((item) => item.row.branchCode))].sort().map((code) => ({ value: code, label: code }));
}

/** Puntos de venta de las filas (para la lista desplegable). */
export function pointOptions(items: readonly DocumentItem[], branch: string): { value: string; label: string }[] {
  const seen = new Map<string, string>();
  for (const item of items) if (!branch || item.row.branchCode === branch) seen.set(item.pointKey, `${item.row.branchCode} · punto de venta ${item.row.pointOfSaleCode}`);
  return [...seen.entries()].sort((a, b) => a[0].localeCompare(b[0], 'es', { numeric: true })).map(([value, label]) => ({ value, label }));
}

/** El documento VIGENTE de una venta (el último emitido: si se re-emitió, el nuevo). Las filas llegan de lo más nuevo a lo más viejo. */
export function currentDocumentOfSale(rows: readonly DocumentRecord[], sale: string): DocumentRecord | null {
  const wanted = normalizeSale(sale);
  const invoices = rows.filter((row) => row.kind === 'Invoice' && (row.saleNumber ?? '').toUpperCase() === wanted);
  const sorted = [...invoices].sort((a, b) => b.issuedAt.localeCompare(a.issuedAt) || b.number - a.number);
  return sorted.find((row) => row.status !== 'Discarded') ?? sorted[0] ?? null;
}

// ---------------------------------------------------------------------------------------------------- resumen del período

const WAITING = new Set(['Pending', 'Offline', 'InPackage', 'NoResponse']);
const BAD = new Set(['Voided', 'Rejected', 'PackageRejected', 'DuplicateToVoid']);

export interface PeriodSummary {
  documents: number;
  invoices: number;
  notes: number;
  validInvoices: number;
  validTotal: number;
  waiting: number;
  waitingOffline: number;
  bad: number;
  voided: number;
}

/** El resumen que el escritorio mostraba en tarjetas (aquí va plegado en «Ver resumen del período»). */
export function periodSummary(rows: readonly DocumentRecord[]): PeriodSummary {
  const valid = rows.filter((row) => row.status === 'Valid' && row.kind === 'Invoice');
  const waiting = rows.filter((row) => WAITING.has(row.status));
  const bad = rows.filter((row) => BAD.has(row.status));
  return {
    documents: rows.length,
    invoices: rows.filter((row) => row.kind === 'Invoice').length,
    notes: rows.filter((row) => row.kind === 'CreditDebitNote').length,
    validInvoices: valid.length,
    validTotal: valid.reduce((sum, row) => sum + row.total, 0),
    waiting: waiting.length,
    waitingOffline: waiting.filter((row) => row.emissionType === EMISSION_OFFLINE).length,
    bad: bad.length,
    voided: bad.filter((row) => row.status === 'Voided').length,
  };
}

/** Columnas del CSV de la lista. */
export const CSV_COLUMNS: readonly CsvColumn<DocumentItem>[] = [
  { header: 'Tipo', value: (item) => item.kindText },
  { header: 'Número', value: (item) => item.row.number },
  { header: 'Fecha y hora fiscal', value: (item) => formatFiscalTime(item.row.issuedAt) },
  { header: 'Sucursal', value: (item) => item.row.branchCode },
  { header: 'Punto de venta', value: (item) => item.row.pointOfSaleCode },
  { header: 'Comprador', value: (item) => item.row.buyerName },
  { header: 'Documento del comprador', value: (item) => item.row.buyerDocument },
  { header: 'Venta', value: (item) => item.row.saleNumber },
  { header: 'Estado', value: (item) => item.statusText },
  { header: 'Emisión', value: (item) => (item.isOffline ? 'Fuera de línea' : 'En línea') },
  { header: 'Total', value: (item) => item.row.total },
  { header: 'CUF', value: (item) => item.row.cuf },
  { header: 'Plazo de anulación', value: (item) => formatFiscalDate(item.row.voidDeadline) },
];

// ---------------------------------------------------------------------------------------------------- acciones

/** Estados que se re-emiten (tras un rechazo o una anulación por datos del comprador erróneos). */
const REISSUABLE = new Set(['Rejected', 'PackageRejected', 'Voided']);

export function isReissuable(row: Pick<DocumentRecord, 'status'>): boolean {
  return REISSUABLE.has(row.status);
}

/** Documentos que no se entregan al comprador (el servidor rechaza enviarlos por correo). */
const NOT_DELIVERABLE = new Set(['Rejected', 'Discarded', 'PackageRejected', 'DuplicateToVoid', 'NoResponse']);

export function isDeliverable(row: Pick<DocumentRecord, 'status'>): boolean {
  return !NOT_DELIVERABLE.has(row.status);
}

/** Solo las facturas de una venta de M-INV pueden devolver la mercadería al anular. */
export function canReturnGoods(row: Pick<DocumentRecord, 'kind' | 'saleNumber'>): boolean {
  return row.kind === 'Invoice' && row.saleNumber !== null;
}

/** Plazo de anulación y reversión (día 9 del mes siguiente), para los estados en que aplica. */
export function deadlineText(row: Pick<DocumentRecord, 'status' | 'voidDeadline'>): string | null {
  if (row.status !== 'Valid' && row.status !== 'DuplicateToVoid' && row.status !== 'Voided') return null;
  return `Plazo de anulación y reversión: hasta el ${formatFiscalDate(row.voidDeadline)} (día 9 del mes siguiente).`;
}

/** Los mensajes del SIN del último rechazo u observación (null si no está rechazado u observado). */
export function rejectionText(detail: Pick<DocumentDetailData, 'row' | 'events'>): string | null {
  if (detail.row.status !== 'Rejected' && detail.row.status !== 'PackageRejected') return null;
  const found = [...detail.events].filter((event) => event.messages && event.messages.trim().length > 0).sort((a, b) => b.occurredAt.localeCompare(a.occurredAt))[0];
  return found?.messages ?? null;
}

/** La bitácora del SIN, de lo más nuevo a lo más viejo. */
export function eventsNewestFirst(events: readonly DocumentEventData[]): DocumentEventData[] {
  return [...events].sort((a, b) => b.occurredAt.localeCompare(a.occurredAt));
}

/** Subtotal de las líneas (en una nota, solo lo devuelto) y el descuento que queda contra el total. */
export function linesTotals(detail: Pick<DocumentDetailData, 'row' | 'lines'>): { subtotal: number; discount: number } {
  const isNote = detail.row.kind === 'CreditDebitNote';
  const subtotal = detail.lines.filter((line) => !isNote || line.transactionCode !== 1).reduce((sum, line) => sum + line.subtotal, 0);
  const discount = isNote ? 0 : Math.max(0, Math.round((subtotal - detail.row.total) * 100) / 100);
  return { subtotal, discount };
}

/** Qué es una línea de una nota crédito-débito (1 = factura original, 2 = devuelto). */
export function lineTransactionText(code: number | null): string | null {
  if (code === 1) return 'Factura original';
  if (code === 2) return 'Devuelto';
  return null;
}

/** «2 u. × Bs 350,00 − desc. Bs 10,00 · SIN 83141 (act. 461000)». */
export function lineDetailText(line: DocumentLineData): string {
  const discount = line.discount > 0 ? ` − desc. ${formatMoney(line.discount)}` : '';
  return `${line.quantity} ${line.unit} × ${formatMoney(line.unitPrice)}${discount} · SIN ${line.sinProductCode} (act. ${line.activityCode})`;
}

// ---------------------------------------------------------------------------------------------------- anulación

/** Descripciones de los motivos que el escritorio propone (`FiscalIssuer.VoidReasonInvoice` / `VoidReasonNote`). */
const PREFERRED_REASON = { Invoice: 'FACTURA MAL EMITIDA', CreditDebitNote: 'NOTA DE CREDITO-DEBITO MAL EMITIDA' } as const;

function plain(text: string): string {
  return text
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toUpperCase()
    .trim();
}

/** Motivos vigentes del catálogo, para la lista desplegable. */
export function voidReasonOptions(catalog: readonly CatalogItemData[] | undefined): { value: string; label: string }[] {
  return (catalog ?? [])
    .filter((item) => item.isCurrent)
    .sort((a, b) => a.code - b.code)
    .map((item) => ({ value: String(item.code), label: `${item.code} · ${sentenceCase(item.description)}` }));
}

/** El motivo que se propone: «factura (o nota) mal emitida»; si no está, el primero. */
export function preferredReason(catalog: readonly CatalogItemData[] | undefined, kind: string): string {
  const current = (catalog ?? []).filter((item) => item.isCurrent).sort((a, b) => a.code - b.code);
  const key = kind === 'CreditDebitNote' ? PREFERRED_REASON.CreditDebitNote : PREFERRED_REASON.Invoice;
  const found = current.find((item) => plain(item.description) === key) ?? current.find((item) => plain(item.description).includes(key));
  return String((found ?? current[0])?.code ?? '');
}

// ---------------------------------------------------------------------------------------------------- comprador

export const DOC_CI = 1;
export const DOC_NIT = 5;

/** Datos del comprador que se escriben al re-emitir (en la página, mientras el diálogo está abierto). */
export interface BuyerDraft {
  documentType: number;
  documentNumber: string;
  complement: string;
  name: string;
  email: string;
  /** «Facturar aunque el NIT no sea válido» (código de excepción 1, solo con NIT). */
  exceptionRequested: boolean;
}

export const EMPTY_BUYER: BuyerDraft = { documentType: DOC_CI, documentNumber: '', complement: '', name: '', email: '', exceptionRequested: false };

const KNOWN_TYPES: Readonly<Record<number, string>> = {
  1: 'CI · Cédula de identidad',
  2: 'CEX · Cédula de extranjero',
  3: 'PAS · Pasaporte',
  4: 'OD · Otro documento',
  5: 'NIT · Número de identificación tributaria',
};

/** Tipos del catálogo sincronizado con el SIN (los vigentes) o, si todavía no se sincronizó, los 5 del SIN. */
export function documentTypeOptions(catalog: readonly CatalogItemData[] | undefined): { value: string; label: string }[] {
  const current = (catalog ?? []).filter((item) => item.isCurrent);
  const codes = current.length > 0 ? [...new Set(current.map((item) => item.code))].sort((a, b) => a - b) : [1, 2, 3, 4, 5];
  return codes.map((code) => ({ value: String(code), label: KNOWN_TYPES[code] ?? current.find((item) => item.code === code)?.description ?? String(code) }));
}

/** NIT especiales del SIN (van con tipo NIT). */
export const SPECIAL_NITS: readonly { code: string; label: string; defaultName: string | null }[] = [
  { code: '99003', label: '99003 · Ventas menores', defaultName: 'VENTAS MENORES DEL DIA' },
  { code: '99002', label: '99002 · Control tributario', defaultName: 'CONTROL TRIBUTARIO' },
  { code: '99001', label: '99001 · Consulados', defaultName: null },
];

export function applySpecialNit(buyer: BuyerDraft, code: string): BuyerDraft {
  const special = SPECIAL_NITS.find((item) => item.code === code);
  if (!special) return buyer;
  return { ...buyer, documentType: DOC_NIT, documentNumber: special.code, complement: '', exceptionRequested: false, name: special.defaultName ?? buyer.name };
}

/** Cambia el tipo de documento: el complemento es solo de la CI y la excepción solo del NIT. */
export function withDocumentType(buyer: BuyerDraft, type: number): BuyerDraft {
  return { ...buyer, documentType: type, complement: type === DOC_CI ? buyer.complement : '', exceptionRequested: type === DOC_NIT ? buyer.exceptionRequested : false };
}

export interface BuyerErrors {
  documentNumber?: string;
  complement?: string;
  email?: string;
}

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function isEmail(text: string): boolean {
  return EMAIL.test(text.trim());
}

/** Guía antes de enviar (la regla que manda es la del servidor). */
export function buyerErrors(buyer: BuyerDraft): BuyerErrors {
  const errors: BuyerErrors = {};
  const number = buyer.documentNumber.trim();
  if (number.length === 0) errors.documentNumber = 'Escriba el documento del comprador (CI, NIT, pasaporte…). Si no lo dio, use el NIT especial 99003 · Ventas menores.';
  else if ((buyer.documentType === DOC_CI || buyer.documentType === DOC_NIT) && !/^\d+$/.test(number))
    errors.documentNumber = `El ${buyer.documentType === DOC_NIT ? 'NIT' : 'CI'} lleva solo números (sin puntos, guiones ni espacios).`;
  else if (number.length > 20) errors.documentNumber = 'El número de documento tiene como máximo 20 caracteres.';
  const complement = buyer.complement.trim();
  if (complement.length > 0 && buyer.documentType !== DOC_CI) errors.complement = 'El complemento se usa solo con la cédula de identidad.';
  else if (complement.length > 5) errors.complement = 'El complemento tiene como máximo 5 caracteres.';
  if (buyer.email.trim().length > 0 && !isEmail(buyer.email)) errors.email = 'Escriba un correo válido, por ejemplo nombre@correo.com.';
  return errors;
}

/** Datos del comprador para el comando. */
export function buyerPayload(buyer: BuyerDraft): BuyerPayload {
  const complement = buyer.complement.trim().toUpperCase();
  const name = buyer.name.trim();
  const email = buyer.email.trim();
  return {
    documentType: buyer.documentType,
    documentNumber: buyer.documentNumber.trim(),
    complement: buyer.documentType === DOC_CI && complement.length > 0 ? complement : null,
    name: name.length > 0 ? name : null,
    email: email.length > 0 ? email : null,
    exceptionRequested: buyer.documentType === DOC_NIT && buyer.exceptionRequested,
  };
}

/** «5115889-1A» → { number: '5115889', complement: '1A' }. */
export function splitDocument(document: string): { number: string; complement: string } {
  const dash = document.indexOf('-');
  return dash > 0 ? { number: document.slice(0, dash), complement: document.slice(dash + 1) } : { number: document, complement: '' };
}

/** Tipo de documento del comprador tal como se envió al SIN (elemento `codigoTipoDocumentoIdentidad` del XML). */
export function documentTypeFromXml(xml: string | null | undefined): number | null {
  const match = /<codigoTipoDocumentoIdentidad>\s*(\d+)\s*<\/codigoTipoDocumentoIdentidad>/.exec(xml ?? '');
  return match ? Number(match[1]) : null;
}

/** El comprador del documento, para empezar a re-emitir con sus datos. */
export function buyerFromDetail(detail: Pick<DocumentDetailData, 'row' | 'xml' | 'buyerEmail'>): BuyerDraft {
  const { number, complement } = splitDocument(detail.row.buyerDocument);
  const type = documentTypeFromXml(detail.xml) ?? DOC_CI;
  return {
    documentType: type,
    documentNumber: number,
    complement: type === DOC_CI ? complement : '',
    name: detail.row.buyerName === 'S/N' ? '' : detail.row.buyerName,
    email: detail.buyerEmail ?? '',
    exceptionRequested: false,
  };
}

/** El XML con sangría para leerlo (sin tocar el contenido). */
export function prettyXml(xml: string): string {
  const text = xml.trim();
  if (text.length === 0) return '(Este documento no tiene XML guardado.)';
  const tokens = text.replace(/>\s*</g, '><').split(/(?=<)|(?<=>)/).filter((token) => token.length > 0);
  let depth = 0;
  const lines: string[] = [];
  for (const token of tokens) {
    if (token.startsWith('</')) {
      depth = Math.max(0, depth - 1);
      const last = lines[lines.length - 1];
      // «<a>texto</a>» queda en una sola línea.
      if (last !== undefined && !last.trimStart().startsWith('<') && lines.length >= 2) {
        const text = lines.pop() ?? '';
        const open = lines.pop() ?? '';
        lines.push(`${open}${text.trim()}${token}`);
        continue;
      }
      lines.push(`${'  '.repeat(depth)}${token}`);
    } else if (token.startsWith('<')) {
      lines.push(`${'  '.repeat(depth)}${token}`);
      if (!token.startsWith('<?') && !token.endsWith('/>')) depth += 1;
    } else {
      lines.push(`${'  '.repeat(depth)}${token}`);
    }
  }
  return lines.join('\n');
}

// ---------------------------------------------------------------------------------------------------- enlaces a otros módulos

/** La venta del documento en «Ventas» (ese día). */
export function saleLink(row: Pick<DocumentRecord, 'saleNumber' | 'issuedAt'>): string | null {
  if (!row.saleNumber) return null;
  const day = fiscalDay(row.issuedAt);
  const params = new URLSearchParams({ q: row.saleNumber });
  if (day) {
    params.set('desde', day);
    params.set('hasta', day);
  }
  return `ventas?${params.toString()}`;
}

/** La devolución (con nota crédito-débito) de la venta, en «Ventas». */
export function returnLink(saleNumber: string): string {
  return `ventas?devolver=${encodeURIComponent(saleNumber)}`;
}
