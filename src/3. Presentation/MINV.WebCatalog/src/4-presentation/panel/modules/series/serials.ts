// Módulo «Series» · funciones puras (sin React): estados de una unidad y de los hechos de su bitácora, la garantía a la
// vista (la calcula el servidor: fecha de la venta + meses del producto, regla T-04; aquí solo se compara con hoy para
// mostrarla), el pedido de la lista (lo que filtra el servidor) y lo que se filtra en la página, columnas del CSV,
// opciones de las listas desplegables, enlaces a Garantías y la lectura de las series que se escanean o pegan al
// registrar. Se prueban sin React en `serials.test.ts`.

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses, statusOptions, type ComboOption, type SelectOption } from '@/4-presentation/panel/kit';
import { formatDate, formatNumber, formatQuantity, inRange, type CsvColumn } from '@/4-presentation/panel/lib';

// ---------------------------------------------------------------------------------------------------- tipos del contrato

/** Una fila de `SearchSerialsQuery` tal como la manda el servidor. */
export type SerialRecord = RpcResponseOf<'SearchSerialsQuery'>[number];
/** Trazabilidad de una unidad (`GetSerialTraceQuery`). */
export type SerialTraceData = RpcResponseOf<'GetSerialTraceQuery'>;
export type SerialEventRecord = SerialTraceData['events'][number];
export type SerialClaimRecord = SerialTraceData['claims'][number];
/** Garantía y venta vigente de una unidad (`GetWarrantyStatusQuery`). */
export type WarrantyInfo = RpcResponseOf<'GetWarrantyStatusQuery'>;
/** Totales por estado de TODAS las series de la empresa (`GetSerialSummaryQuery`). */
export type SerialSummaryData = RpcResponseOf<'GetSerialSummaryQuery'>;
/** Producto del catálogo técnico (`SearchTechProductsQuery`): si lleva serie, de qué tipo y su stock. */
export type TechProductRecord = RpcResponseOf<'SearchTechProductsQuery'>[number];
export type SerialStatusCode = SerialRecord['status'];
export type SerialKindCode = SerialRecord['kind'];
export type EventActionCode = SerialEventRecord['action'];
export type DisposalCode = RpcRequestOf<'DisposeSerialCommand'>['disposal'];

// ---------------------------------------------------------------------------------------------------- estados

/** Estado de una unidad (enumeración `SerialNumberStatus` del servidor). */
export const SERIAL_STATES = defineStatuses<SerialStatusCode>({
  InStock: { label: 'En stock', tone: 'success' },
  Sold: { label: 'Vendida', tone: 'info' },
  InTransit: { label: 'En tránsito', tone: 'accent' },
  Returned: { label: 'Devuelta por el cliente', tone: 'warning' },
  InRma: { label: 'En garantía (RMA)', tone: 'warning' },
  ReturnedToSupplier: { label: 'Devuelta al proveedor', tone: 'neutral' },
  Scrapped: { label: 'Dada de baja', tone: 'neutral' },
  Reserved: { label: 'Reservada', tone: 'accent' },
});

/** Estados que ofrece el filtro (una unidad «reservada» se ve con su estado, pero no se filtra por él, como el escritorio). */
export const STATUS_FILTER_OPTIONS: readonly SelectOption[] = statusOptions(SERIAL_STATES).filter((option) => option.value !== 'Reserved');

/** Tipo de identificación de la unidad. */
export const SERIAL_KINDS = defineStatuses<SerialKindCode>({
  Serial: { label: 'Serie', tone: 'neutral' },
  Imei: { label: 'IMEI', tone: 'accent' },
});

/** Hechos de la bitácora de una unidad (enumeración `SerialEventAction`), con los textos del escritorio. */
export const EVENT_ACTIONS = defineStatuses<EventActionCode>({
  Received: { label: 'Ingresó al stock', tone: 'success' },
  Sold: { label: 'Vendida', tone: 'info' },
  Returned: { label: 'Devuelta por el cliente', tone: 'warning' },
  TransferDispatched: { label: 'Despachada en una transferencia', tone: 'accent' },
  TransferReceived: { label: 'Recibida por transferencia', tone: 'success' },
  RmaReceived: { label: 'Recibida en garantía (RMA)', tone: 'warning' },
  SentToSupplier: { label: 'Enviada al proveedor o al servicio técnico', tone: 'warning' },
  Repaired: { label: 'Reparada', tone: 'success' },
  Replaced: { label: 'Reemplazada por otra unidad', tone: 'warning' },
  ReplacementIssued: { label: 'Entregada como reposición de garantía', tone: 'info' },
  ReturnedToSupplier: { label: 'Devuelta al proveedor', tone: 'danger' },
  Scrapped: { label: 'Dada de baja', tone: 'danger' },
  Adjusted: { label: 'Ajuste de inventario', tone: 'accent' },
  Restocked: { label: 'Volvió al stock', tone: 'success' },
  ReturnedToCustomer: { label: 'Entregada a su dueño', tone: 'info' },
});

/** Estado de un caso de garantía (para la lista de casos de la unidad). */
export const CLAIM_STATES = defineStatuses<SerialClaimRecord['status']>({
  Received: { label: 'Recibido', tone: 'info' },
  Diagnosing: { label: 'En diagnóstico', tone: 'warning' },
  SentToSupplier: { label: 'En el proveedor', tone: 'warning' },
  Repaired: { label: 'Reparado', tone: 'success' },
  Replaced: { label: 'Reemplazado', tone: 'success' },
  Rejected: { label: 'Rechazado', tone: 'danger' },
  Delivered: { label: 'Entregado', tone: 'neutral' },
});

/** La garantía de una unidad, a la vista: vigente, vencida o sin garantía en curso (todavía no se vendió). */
export type WarrantyState = 'vigente' | 'vencida' | 'sin';

export const WARRANTY_STATES = defineStatuses<WarrantyState>({
  vigente: { label: 'Garantía vigente', tone: 'success' },
  vencida: { label: 'Garantía vencida', tone: 'danger' },
  sin: { label: 'Sin garantía en curso', tone: 'neutral' },
});

function hasKey<K extends string>(map: Readonly<Record<K, unknown>>, value: string): value is K {
  return Object.prototype.hasOwnProperty.call(map, value);
}

/** `?estado=` de la dirección → estado para el servidor (un valor raro no filtra). */
export function statusCodeOf(value: string): SerialStatusCode | null {
  return value && hasKey(SERIAL_STATES, value) ? value : null;
}

export function kindLabel(kind: string): string {
  return hasKey(SERIAL_KINDS, kind) ? SERIAL_KINDS[kind].label : kind;
}

export function statusLabel(status: string): string {
  return hasKey(SERIAL_STATES, status) ? SERIAL_STATES[status].label : status;
}

/** Garantía de una fila frente a hoy (fechas de La Paz «2026-09-29»). La fecha de fin la calcula el servidor. */
export function warrantyStateOf(until: string | null, today: string): WarrantyState {
  if (!until) return 'sin';
  return until >= today ? 'vigente' : 'vencida';
}

/** «Hasta 12/03/2027», «Venció el 12/03/2025» o «—». */
export function warrantyText(until: string | null, today: string): string {
  if (!until) return '—';
  return until >= today ? `Hasta ${formatDate(until)}` : `Venció el ${formatDate(until)}`;
}

/** «1 mes», «6 meses», «1 año», «2 años», «18 meses». */
export function monthsText(months: number): string {
  if (months >= 12 && months % 12 === 0) return months === 12 ? '1 año' : `${months / 12} años`;
  return months === 1 ? '1 mes' : `${formatNumber(months)} meses`;
}

export interface WarrantySummary {
  title: string;
  detail: string;
  tone: 'success' | 'danger' | 'info' | 'neutral';
}

/** El recuadro de garantía del detalle (los mismos textos del escritorio). */
export function traceWarranty(trace: Pick<SerialTraceData, 'warrantyMonths' | 'inWarranty'> & { serial: Pick<SerialRecord, 'warrantyUntil'> }): WarrantySummary {
  const months = trace.warrantyMonths;
  if (months <= 0) return { title: 'El producto no tiene garantía', detail: 'Su ficha no tiene meses de garantía.', tone: 'neutral' };
  const detail = `${monthsText(months)} desde la venta (se calcula al consultar: fecha de la venta + meses del producto).`;
  const until = trace.serial.warrantyUntil;
  if (!until) return { title: `Garantía de ${monthsText(months)} (corre desde la venta)`, detail, tone: 'info' };
  if (trace.inWarranty) return { title: `En garantía hasta el ${formatDate(until)}`, detail, tone: 'success' };
  return { title: `Garantía vencida el ${formatDate(until)}`, detail, tone: 'danger' };
}

// ---------------------------------------------------------------------------------------------------- filas

/** Una fila lista para mostrar. */
export interface SerialItem {
  /** SKU + serie (la serie es única por producto). */
  key: string;
  row: SerialRecord;
  warranty: WarrantyState;
  /** «CM · ALM-CM» (sucursal y almacén). */
  location: string;
}

export function serialKey(row: Pick<SerialRecord, 'sku' | 'serial'>): string {
  return `${row.sku}|${row.serial}`;
}

export function locationOf(row: Pick<SerialRecord, 'branch' | 'warehouse'>): string {
  return [row.branch, row.warehouse].filter((part): part is string => Boolean(part && part.trim())).join(' · ');
}

export function toSerialItems(rows: readonly SerialRecord[], today: string): SerialItem[] {
  return rows.map((row) => ({ key: serialKey(row), row, warranty: warrantyStateOf(row.warrantyUntil, today), location: locationOf(row) }));
}

// ---------------------------------------------------------------------------------------------------- filtros

/** Cuántas series se piden al servidor (las que ingresaron más recientemente; el servidor acepta de 1 a 5000). */
export const TAKE_OPTIONS: readonly SelectOption[] = [
  { value: '500', label: 'Las 500 más recientes' },
  { value: '1000', label: 'Las 1.000 más recientes' },
  { value: '2000', label: 'Las 2.000 más recientes' },
  { value: '5000', label: 'Las 5.000 más recientes' },
];
export const DEFAULT_TAKE = '1000';

/** `?registros=` → cantidad para el servidor (un valor raro vuelve al de siempre). */
export function takeOf(value: string): number {
  return Number(TAKE_OPTIONS.some((option) => option.value === value) ? value : DEFAULT_TAKE);
}

/**
 * Filtros de la lista (en la dirección). Van al SERVIDOR: `q` (serie, IMEI o SKU), `estado`, `producto` (SKU) y
 * `registros`. Se filtran en la PÁGINA: `sucursal`, `tipo`, `garantia` y las fechas de ingreso (`desde`, `hasta`).
 */
export const SERIAL_FILTERS = { q: '', estado: '', producto: '', sucursal: '', tipo: '', garantia: '', desde: '', hasta: '', registros: DEFAULT_TAKE };
export type SerialFilters = typeof SERIAL_FILTERS;

/** El pedido de la lista: SIEMPRE con todos los parámetros («sin filtro» = null). */
export function searchRequest(filters: Pick<SerialFilters, 'q' | 'estado' | 'producto' | 'registros'>): RpcRequestOf<'SearchSerialsQuery'> {
  const text = filters.q.trim();
  const sku = filters.producto.trim();
  return { text: text || null, status: statusCodeOf(filters.estado), sku: sku || null, max: takeOf(filters.registros) };
}

/** Lo que se filtra en la página (el resto ya lo filtró el servidor). */
export function filterSerials(items: readonly SerialItem[], filters: SerialFilters): SerialItem[] {
  const range = { from: filters.desde || null, to: filters.hasta || null };
  return items.filter(
    (item) =>
      (!filters.sucursal || item.row.branch === filters.sucursal) &&
      (!filters.tipo || item.row.kind === filters.tipo) &&
      (!filters.garantia || item.warranty === filters.garantia) &&
      inRange(item.row.receivedAt, range),
  );
}

/** ¿La lista llegó al tope pedido? (hay más series: conviene afinar la búsqueda). */
export function isTruncated(count: number, registros: string): boolean {
  return count >= takeOf(registros);
}

export const KIND_OPTIONS: readonly SelectOption[] = statusOptions(SERIAL_KINDS);
export const WARRANTY_OPTIONS: readonly SelectOption[] = statusOptions(WARRANTY_STATES);

/** Sucursales que aparecen en la lista, con su nombre si la sesión la conoce («CM · Casa Matriz»). */
export function branchOptions(items: readonly SerialItem[], branches: readonly { code: string; name: string }[], selected = ''): SelectOption[] {
  const codes = new Set(items.map((item) => item.row.branch).filter((code): code is string => Boolean(code)));
  if (selected) codes.add(selected);
  return [...codes]
    .sort((a, b) => a.localeCompare(b, 'es'))
    .map((code) => {
      const branch = branches.find((item) => item.code === code);
      return { value: code, label: branch ? `${code} · ${branch.name}` : code };
    });
}

/** El pedido del catálogo técnico para la lista de productos (todos, también los que no tienen stock). */
export const PRODUCTS_REQUEST: RpcRequestOf<'SearchTechProductsQuery'> = { text: null, categoryCode: null, platform: null, filters: null, onlyInStock: false, max: 2000 };

function productOption(product: TechProductRecord): ComboOption<TechProductRecord> {
  const kind = product.trackSerials ? `lleva ${kindLabel(product.serialKind)}` : 'todavía sin serie';
  return { value: product.sku, label: product.name, description: `${product.sku} · ${kind} · ${formatQuantity(product.stock)} en stock`, data: product };
}

/**
 * Productos para el filtro «Producto»: los que llevan serie o IMEI del catálogo (si la sesión puede leerlo); si no, los
 * que aparecen en la lista.
 */
export function productOptions(products: readonly TechProductRecord[] | undefined, items: readonly SerialItem[]): ComboOption<TechProductRecord>[] {
  if (products) return products.filter((product) => product.trackSerials).map(productOption);
  const seen = new Map<string, ComboOption<TechProductRecord>>();
  for (const item of items) {
    if (!seen.has(item.row.sku)) seen.set(item.row.sku, { value: item.row.sku, label: item.row.product, description: item.row.sku });
  }
  return [...seen.values()].sort((a, b) => a.label.localeCompare(b.label, 'es'));
}

/** Productos para «Registrar series de stock»: los que llevan serie y los que tienen stock (como el escritorio). */
export function registerProductOptions(products: readonly TechProductRecord[]): ComboOption<TechProductRecord>[] {
  return products
    .filter((product) => product.trackSerials || product.stock > 0)
    .map(productOption)
    .sort((a, b) => a.label.localeCompare(b.label, 'es'));
}

/** La opción elegida en el filtro «Producto» (aunque el catálogo todavía no haya llegado). */
export function selectedProduct(sku: string, options: readonly ComboOption<TechProductRecord>[], items: readonly SerialItem[]): ComboOption<TechProductRecord> | null {
  if (!sku) return null;
  const option = options.find((item) => item.value === sku);
  if (option) return option;
  const row = items.find((item) => item.row.sku === sku)?.row;
  return { value: sku, label: row ? row.product : sku, description: sku };
}

// ---------------------------------------------------------------------------------------------------- CSV

export const SERIALS_CSV: readonly CsvColumn<SerialItem>[] = [
  { header: 'Serie o IMEI', value: (item) => item.row.serial },
  { header: 'Tipo', value: (item) => kindLabel(item.row.kind) },
  { header: 'SKU', value: (item) => item.row.sku },
  { header: 'Producto', value: (item) => item.row.product },
  { header: 'Estado', value: (item) => statusLabel(item.row.status) },
  { header: 'Sucursal', value: (item) => item.row.branch },
  { header: 'Almacén', value: (item) => item.row.warehouse },
  { header: 'Ingresó', value: (item) => (item.row.receivedAt ? new Date(item.row.receivedAt) : null) },
  { header: 'Vendida', value: (item) => (item.row.soldAt ? new Date(item.row.soldAt) : null) },
  { header: 'Factura', value: (item) => item.row.invoiceNumber },
  { header: 'Cliente', value: (item) => item.row.customer },
  { header: 'Garantía hasta', value: (item) => (item.row.warrantyUntil ? formatDate(item.row.warrantyUntil) : null) },
  { header: 'Garantía', value: (item) => WARRANTY_STATES[item.warranty].label },
];

// ---------------------------------------------------------------------------------------------------- detalle

/** La bitácora de la unidad, lo más reciente primero. */
export function timeline(events: readonly SerialEventRecord[]): SerialEventRecord[] {
  return [...events].sort((a, b) => new Date(b.occurredAt).getTime() - new Date(a.occurredAt).getTime());
}

/** «CM · F-CM-000123 · Diego Flores». */
export function eventDetail(event: Pick<SerialEventRecord, 'branch' | 'documentNumber' | 'user'>): string {
  return [event.branch, event.documentNumber, event.user].filter((part): part is string => Boolean(part && part.trim())).join(' · ');
}

/** «Proveedor X · recepción R-CM-000010». */
export function originText(trace: Pick<SerialTraceData, 'supplier' | 'receiptNumber'>): string {
  return [trace.supplier ? `Proveedor ${trace.supplier}` : null, trace.receiptNumber ? `recepción ${trace.receiptNumber}` : null]
    .filter((part): part is string => part !== null)
    .join(' · ');
}

/** Caso de garantía abierto de la unidad: lo dice el servidor (`openClaim`); si esa consulta no llegó, su bitácora. */
export function openClaimOf(serial: string, warranty: Pick<WarrantyInfo, 'openClaim'> | undefined, trace: Pick<SerialTraceData, 'claims'> | undefined): string | null {
  if (warranty) return warranty.openClaim;
  return trace?.claims.find((claim) => claim.serial === serial && claim.status !== 'Delivered')?.number ?? null;
}

/** Solo se abre un caso de garantía de una unidad vendida (o devuelta por el cliente); el servidor lo vuelve a decidir. */
export function canOpenClaimFor(status: string): boolean {
  return status === 'Sold' || status === 'Returned';
}

/** Solo se da destino a una unidad devuelta o en garantía (las que están en stock salen con un ajuste). */
export function canDisposeOf(status: string): boolean {
  return status === 'Returned' || status === 'InRma';
}

// ---------------------------------------------------------------------------------------------------- enlaces

/** Garantías › «Abrir un caso» con la serie ya escrita (y su SKU, por si la serie existe en dos productos). */
export function openClaimPath(serial: string, sku: string | null): string {
  const params = new URLSearchParams({ abrir: '1', serie: serial });
  if (sku) params.set('sku', sku);
  return `garantias?${params.toString()}`;
}

/** Garantías con el detalle de un caso abierto. */
export function claimPath(number: string): string {
  return `garantias?${new URLSearchParams({ ver: number }).toString()}`;
}

/** Ventas › las ventas de un cliente (parámetro del módulo «Ventas»). */
export function customerSalesPath(code: string): string {
  return `ventas?${new URLSearchParams({ cliente: code }).toString()}`;
}

// ---------------------------------------------------------------------------------------------------- registrar series

/** Largo máximo de una serie (el del servidor). */
export const SERIAL_MAX = 80;

/** IMEI sin espacios ni guiones («35-209900-176148-1» → «352099001761481»). */
export function normalizeImei(value: string): string {
  return value.replace(/[\s-]/g, '');
}

/** 15 dígitos y el dígito verificador de Luhn correcto (comodidad: el servidor lo valida igual). */
export function isValidImei(value: string): boolean {
  const digits = normalizeImei(value);
  if (!/^\d{15}$/.test(digits)) return false;
  let sum = 0;
  for (let index = 0; index < 14; index += 1) {
    let digit = Number(digits[index]);
    if (index % 2 === 1) {
      digit *= 2;
      if (digit > 9) digit -= 9;
    }
    sum += digit;
  }
  return (10 - (sum % 10)) % 10 === Number(digits[14]);
}

export interface SerialListParse {
  /** Series listas para enviar (normalizadas: mayúsculas; el IMEI solo con dígitos). */
  serials: string[];
  /** Problemas en palabras (repetidas, mal escritas). */
  problems: string[];
}

/**
 * Lee las series escritas, escaneadas (el lector agrega una por línea) o pegadas: una por línea o separadas por comas,
 * punto y coma o tabulaciones. Es comodidad: el servidor vuelve a validar todo.
 */
export function parseSerialList(text: string, kind: SerialKindCode): SerialListParse {
  const serials: string[] = [];
  const problems: string[] = [];
  for (const raw of text.split(/[\r\n,;\t]+/)) {
    const token = raw.trim();
    if (!token) continue;
    let serial: string;
    if (kind === 'Imei') {
      serial = normalizeImei(token);
      if (!isValidImei(serial)) {
        problems.push(`El IMEI «${token}» no es válido: debe tener 15 dígitos y el dígito verificador correcto.`);
        continue;
      }
    } else {
      serial = token.toUpperCase();
      if (/\s/.test(serial)) {
        problems.push(`La serie «${token}» no puede tener espacios, comas ni punto y coma.`);
        continue;
      }
      if (serial.length > SERIAL_MAX) {
        problems.push(`La serie «${token.slice(0, 20)}…» es demasiado larga (máximo ${SERIAL_MAX} caracteres).`);
        continue;
      }
    }
    if (serials.includes(serial)) {
      problems.push(`La serie ${serial} está repetida.`);
      continue;
    }
    serials.push(serial);
  }
  return { serials, problems };
}

export function seriesCountText(count: number): string {
  return count === 1 ? '1 serie' : `${formatNumber(count)} series`;
}

/** Nota por defecto del registro (como el escritorio). */
export const REGISTER_NOTE = 'Inventario inicial de series';
export const NOTE_MAX = 200;

// ---------------------------------------------------------------------------------------------------- dar destino

export const DISPOSALS: readonly { value: DisposalCode; label: string; description: string }[] = [
  { value: 'ReturnToSupplier', label: 'Devolver al proveedor', description: 'Reemplazo o nota de crédito del proveedor (lo que devuelva entra aparte).' },
  { value: 'Scrap', label: 'Dar de baja', description: 'Equipo irreparable o destruido.' },
];

export const DISPOSE_REASONS: readonly string[] = ['Falla de fábrica confirmada', 'Reemplazo del proveedor', 'Equipo irreparable', 'Daño físico'];
export const OTHER_REASON = 'otro';
export const REASON_MAX = 250;

export function reasonOptions(reasons: readonly string[]): SelectOption[] {
  return [...reasons.map((reason) => ({ value: reason, label: reason })), { value: OTHER_REASON, label: 'Otro motivo (escribirlo)' }];
}

export function reasonText(choice: string, other: string): string {
  return (choice === OTHER_REASON ? other : choice).trim();
}

export function reasonProblem(reason: string): string | null {
  if (reason.length < 3) return 'Indique el motivo (al menos 3 caracteres).';
  if (reason.length > REASON_MAX) return `El motivo admite hasta ${REASON_MAX} caracteres.`;
  return null;
}

/** El mensaje del servidor sin la marca «✔» del escritorio. */
export function plainMessage(text: string): string {
  return text.replace(/^[✔✓]\s*/u, '').trim();
}

// ---------------------------------------------------------------------------------------------------- estadística

/** Barras de «Unidades por estado» (todas las series de la empresa). */
export function summaryBars(summary: SerialSummaryData): { label: string; value: number }[] {
  const others = Math.max(0, summary.total - summary.inStock - summary.sold - summary.inRmaOrReturned - summary.out);
  const bars = [
    { label: 'En stock', value: summary.inStock },
    { label: 'Vendidas', value: summary.sold },
    { label: 'En garantía (RMA) o devueltas', value: summary.inRmaOrReturned },
    { label: 'Bajas y devueltas al proveedor', value: summary.out },
  ];
  if (others > 0) bars.push({ label: 'En tránsito o reservadas', value: others });
  return bars;
}

export function productsText(count: number): string {
  return count === 1 ? 'de 1 producto' : `de ${formatNumber(count)} productos`;
}
