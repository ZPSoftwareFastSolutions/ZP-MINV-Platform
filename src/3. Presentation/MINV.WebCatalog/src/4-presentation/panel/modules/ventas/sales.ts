// Módulo «Ventas» · funciones puras (sin React): cómo se presenta cada venta y cada devolución, los filtros de la lista
// (en la dirección de la página), el rango de fechas que se pide al servidor, las columnas del CSV, los totales, las
// estadísticas plegadas del tablero y el borrador de una devolución (líneas, cantidades, series y reembolso estimado).
// Los textos de los estados son los del escritorio (SalesViewModels.cs y FiscalText de BillingCommon.cs).
//
// Los tipos del servidor SALEN DEL CONTRATO (regla P-07): se derivan del nombre de la operación. Las reglas que aquí se
// repiten (qué venta se anula ante el SIN, qué documento se puede enviar por correo) son COMODIDAD para ocultar o explicar
// un botón: el servidor vuelve a decidir en cada pedido (regla P-01).

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses, statusOf } from '@/4-presentation/panel/kit';
import {
  addDays,
  formatDateTime,
  formatQuantity,
  inRange,
  laPazToday,
  matchesSearch,
  rangeError,
  roundTo,
  toDate,
  toIsoDate,
  type CsvColumn,
  type DateRange,
} from '@/4-presentation/panel/lib';

// ---------------------------------------------------------------------------------------------------- tipos del contrato

/** Una venta de `GetSalesQuery` tal como la manda el servidor. */
export type SaleRecord = RpcResponseOf<'GetSalesQuery'>[number];
/** Documento fiscal vigente de una venta (`GetSalesFiscalStatusQuery`). */
export type SaleFiscalRecord = RpcResponseOf<'GetSalesFiscalStatusQuery'>[number];
/** Una devolución de `GetSalesReturnsQuery`. */
export type ReturnRecord = RpcResponseOf<'GetSalesReturnsQuery'>[number];
/** Una línea de la venta (`GetSaleLinesQuery`), con sus series. */
export type SaleLineRecord = RpcResponseOf<'GetSaleLinesQuery'>[number];
/** Lo que todavía se puede devolver de una línea (`GetReturnableLinesQuery`). */
export type ReturnableRecord = RpcResponseOf<'GetReturnableLinesQuery'>[number];
/** El pedido de `CreateSalesReturnCommand`. */
export type ReturnPayload = RpcRequestOf<'CreateSalesReturnCommand'>;
/** Resultado de la devolución. */
export type ReturnOutcome = RpcResponseOf<'CreateSalesReturnCommand'>;
/** Representación gráfica de la factura (`GetFiscalPrintModelQuery`). */
export type InvoicePrint = RpcResponseOf<'GetFiscalPrintModelQuery'>;
/** Un medio de pago de la caja (`GetPosStateQuery`). */
export type RefundMethod = RpcResponseOf<'GetPosStateQuery'>['paymentMethods'][number];

// ---------------------------------------------------------------------------------------------------- estados

/** Estado de la venta en la lista: vigente, con devolución (parcial o total) o anulada. */
export const SALE_STATES = defineStatuses({
  vigente: { label: 'Vigente', tone: 'success' },
  devolucion: { label: 'Con devolución', tone: 'warning' },
  anulada: { label: 'Anulada', tone: 'danger' },
});
export type SaleState = keyof typeof SALE_STATES;

/** Valor del filtro «Factura del SIN» para las ventas que no tienen documento fiscal. */
export const NO_INVOICE = 'SinFactura';
/** Valor del filtro «Nota crédito-débito» para las devoluciones sin nota (venta sin factura del SIN). */
export const NO_NOTE = 'SinNota';

/** Estado del documento fiscal ante el SIN (enumeración `FiscalDocumentStatus`), como el escritorio. */
const FISCAL_BASE = {
  Valid: { label: 'Válida', tone: 'success' },
  Pending: { label: 'Pendiente', tone: 'warning' },
  Offline: { label: 'Fuera de línea', tone: 'info' },
  InPackage: { label: 'En paquete', tone: 'info' },
  NoResponse: { label: 'Sin respuesta', tone: 'warning' },
  Rejected: { label: 'Rechazada', tone: 'danger' },
  PackageRejected: { label: 'Observada', tone: 'danger' },
  DuplicateToVoid: { label: 'Duplicada: anular', tone: 'danger' },
  Voided: { label: 'Anulada', tone: 'neutral' },
  Discarded: { label: 'Descartada', tone: 'neutral' },
} as const;

export const FISCAL_STATES = defineStatuses({ ...FISCAL_BASE, [NO_INVOICE]: { label: 'Sin factura del SIN', tone: 'neutral' } });
export const NOTE_STATES = defineStatuses({ ...FISCAL_BASE, [NO_NOTE]: { label: 'Sin nota (venta sin factura del SIN)', tone: 'neutral' } });

/** Qué significa cada estado ante el SIN (ayuda del detalle), como el escritorio (FiscalText.StatusHelp). */
const FISCAL_HELP: Readonly<Record<string, string>> = {
  Pending: 'Emitida en línea; se está enviando al SIN.',
  Valid: 'El SIN la recibió y la validó.',
  Rejected: 'El SIN la rechazó: la venta recibe una factura nueva (vea los mensajes del SIN en Documentos).',
  NoResponse: 'Se perdió la respuesta del SIN: la venta se re-emitió fuera de línea y esta se verifica al volver la conexión.',
  Offline: 'Emitida sin conexión con el SIN: se envía sola en un paquete al volver la comunicación.',
  InPackage: 'Enviada en un paquete de contingencia; falta la validación del SIN.',
  PackageRejected: 'El SIN la observó al validar el paquete: se re-emite.',
  DuplicateToVoid: 'El SIN la registró y la venta ya tiene otra factura: hay que anularla.',
  Voided: 'Anulada ante el SIN.',
  Discarded: 'Nunca llegó al SIN y se reemplazó por otra: queda solo como historia.',
};

export function fiscalHelp(status: string): string {
  return FISCAL_HELP[status] ?? '';
}

/** Tipo de emisión «fuera de línea» del SIN (el resto es en línea). */
export const OFFLINE_EMISSION = 2;

/** «Válida» · «Válida (anulación revertida)» · «Sin factura del SIN». */
export function fiscalStatusLabel(fiscal: SaleFiscalRecord | null): string {
  if (!fiscal) return FISCAL_STATES[NO_INVOICE].label;
  const label = statusOf(FISCAL_STATES, fiscal.status).label;
  return fiscal.status === 'Valid' && fiscal.isReverted ? `${label} (anulación revertida)` : label;
}

/** «N° 1234 · Válida» · «Sin factura del SIN». */
export function fiscalText(fiscal: SaleFiscalRecord | null): string {
  return fiscal ? `N° ${fiscal.number} · ${fiscalStatusLabel(fiscal)}` : fiscalStatusLabel(null);
}

/** Estados del documento que NO bloquean la anulación interna (el servidor exige anular ante el SIN los demás). */
const INACTIVE_FISCAL = new Set(['Voided', 'Rejected', 'Discarded', 'PackageRejected']);

/** ¿La venta tiene un documento fiscal activo? Entonces se anula ante el SIN (Facturación › Documentos), no aquí. */
export function voidsBeforeSin(fiscal: SaleFiscalRecord | null): boolean {
  return fiscal !== null && !INACTIVE_FISCAL.has(fiscal.status);
}

/** Documentos que no se entregan al comprador (el servidor rechaza enviarlos por correo). */
const NOT_DELIVERABLE = new Set(['Rejected', 'Discarded', 'PackageRejected', 'DuplicateToVoid', 'NoResponse']);

/** ¿Se puede enviar la factura por correo? null si se puede; si no, por qué. */
export function emailBlockedReason(fiscal: SaleFiscalRecord | null): string | null {
  if (!fiscal) return 'La venta no tiene factura del SIN.';
  return NOT_DELIVERABLE.has(fiscal.status) ? `Esta factura no se entrega al comprador (${fiscalStatusLabel(fiscal).toLowerCase()}).` : null;
}

/** CUF abreviado para las listas (el completo va en el detalle). */
export function shortCuf(cuf: string): string {
  return cuf.length > 16 ? `${cuf.slice(0, 8)}…${cuf.slice(-6)}` : cuf;
}

const FISCAL_TIME = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/;

/**
 * Fecha y hora FISCAL (la del SIN, hora de Bolivia, sin zona: `2026-10-09T23:59:59`) → «09/10/2026 23:59». Se escribe
 * tal cual, sin convertir de zona. Si el texto trae zona (`Z` o `-04:00`), se muestra con la hora de La Paz.
 */
export function formatFiscalDateTime(value: string | null | undefined): string {
  if (!value) return '—';
  if (/(Z|[+-]\d{2}:\d{2})$/.test(value)) return formatDateTime(value);
  const match = FISCAL_TIME.exec(value);
  return match ? `${match[3]}/${match[2]}/${match[1]} ${match[4]}:${match[5]}` : value;
}

// ---------------------------------------------------------------------------------------------------- números y sucursal

/** Sucursal de un documento por su número (`F-CM-000123` → `CM`, regla B-02). null si el número no la lleva. */
export function branchOfNumber(number: string): string | null {
  const parts = number.trim().toUpperCase().split('-');
  return parts.length >= 3 ? parts.slice(1, -1).join('-') || null : null;
}

/** ¿Es un cliente que se registró en la tienda web? (código `WEB-…`). */
export function isWebCustomer(code: string | null | undefined): boolean {
  return (code ?? '').trim().toUpperCase().startsWith('WEB-');
}

/** Quita el ✔ / ⚠ / ✖ del principio de un mensaje del servidor (la pantalla pone su propio ícono). */
export function plainMessage(message: string): string {
  return message.replace(/^[✔⚠✖\s]+/u, '').trim();
}

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Por qué no sirve un correo escrito (vacío sirve: se usa el del comprador), o null. */
export function emailProblem(text: string): string | null {
  const email = text.trim();
  if (email.length === 0) return null;
  return EMAIL.test(email) && email.length <= 150 ? null : 'Escriba un correo válido, por ejemplo nombre@dominio.com.';
}

// ---------------------------------------------------------------------------------------------------- filtros y fechas

/** Pestañas de la pantalla (`?vista=` en la dirección). */
export const VIEWS = { sales: 'ventas', returns: 'devoluciones' } as const;
export type SalesView = (typeof VIEWS)[keyof typeof VIEWS];

export function viewOf(value: string | null): SalesView {
  return value === VIEWS.returns ? VIEWS.returns : VIEWS.sales;
}

/** Filtros de la pantalla (en la dirección). Las fechas son de HOY por defecto. */
export function salesFilters(today: string) {
  return { q: '', desde: today, hasta: today, sucursal: '', cliente: '', cajero: '', pago: '', estado: '', fiscal: '', nota: '' };
}
export type SalesFilters = ReturnType<typeof salesFilters>;
type FilterKey = keyof SalesFilters;

/** Filtros que valen para las dos pestañas (además de las fechas). */
const SHARED_KEYS: readonly FilterKey[] = ['q', 'sucursal', 'cliente'];
const SALES_KEYS: readonly FilterKey[] = ['cajero', 'pago', 'estado', 'fiscal'];
const RETURNS_KEYS: readonly FilterKey[] = ['nota'];

function keysOf(view: SalesView): FilterKey[] {
  return [...SHARED_KEYS, ...(view === VIEWS.returns ? RETURNS_KEYS : SALES_KEYS)];
}

/** Filtros activos de la pestaña (el rango de fechas cuenta como uno). */
export function activeFilters(filters: SalesFilters, defaults: SalesFilters, view: SalesView): number {
  const range = filters.desde !== defaults.desde || filters.hasta !== defaults.hasta ? 1 : 0;
  return range + keysOf(view).filter((key) => filters[key] !== defaults[key]).length;
}

/** «Limpiar filtros» de la pestaña: sus filtros y las fechas vuelven a su valor por defecto (hoy). */
export function clearedFilters(defaults: SalesFilters, view: SalesView): Partial<SalesFilters> {
  const cleared: Partial<SalesFilters> = { desde: defaults.desde, hasta: defaults.hasta };
  for (const key of keysOf(view)) cleared[key] = defaults[key];
  return cleared;
}

/** Desde cuándo se piden las ventas con el filtro de fechas vacío («Todas»). */
export const ALL_DATES_FROM = '2000-01-01';

/** Rango de días (DateOnly, ambos incluidos) que se envía al servidor. */
export interface DayRange {
  from: string;
  to: string;
}

/**
 * Rango para el servidor a partir del filtro de fechas: vacío = todas las ventas hasta hoy; solo «Desde» = hasta hoy;
 * solo «Hasta» = desde el principio. null si las fechas escritas no sirven (el campo ya lo avisa).
 */
export function serverRange(range: DateRange, today: string): DayRange | null {
  if (rangeError(range)) return null;
  const from = range.from ?? ALL_DATES_FROM;
  const to = range.to ?? (from > today ? from : today);
  return { from, to };
}

/**
 * Rango de las devoluciones: desde el inicio de las ventas hasta HOY (o hasta el final del rango, si es posterior), así se
 * encuentran también las devoluciones de esas ventas hechas después. La pestaña «Devoluciones» filtra luego por las
 * fechas elegidas.
 */
export function returnsRange(sales: DayRange, today: string): DayRange {
  return { from: sales.from, to: sales.to > today ? sales.to : today };
}

// ---------------------------------------------------------------------------------------------------- ventas

/** Una venta lista para la tabla: lo del servidor más cómo se muestra. */
export interface SaleItem {
  /** Identificador estable: el número de la venta (factura de M-INV). */
  key: string;
  row: SaleRecord;
  /** Código de la sucursal (del número) o null. */
  branch: string | null;
  state: SaleState;
  /** Documento fiscal vigente (null si la venta no se facturó ante el SIN). */
  fiscal: SaleFiscalRecord | null;
  /** Estado del documento fiscal o `NO_INVOICE`. */
  fiscalState: string;
  /** Devoluciones de la venta (las encontradas en el rango consultado). */
  returns: ReturnRecord[];
  /** Total reembolsado en esas devoluciones. */
  refunded: number;
  webCustomer: boolean;
}

export function toSaleItems(sales: readonly SaleRecord[], fiscal: readonly SaleFiscalRecord[], returns: readonly ReturnRecord[]): SaleItem[] {
  const fiscalBySale = new Map(fiscal.map((row) => [row.invoiceNumber.toUpperCase(), row]));
  const returnsBySale = new Map<string, ReturnRecord[]>();
  for (const row of returns) {
    const key = row.invoiceNumber.toUpperCase();
    returnsBySale.set(key, [...(returnsBySale.get(key) ?? []), row]);
  }
  return sales.map((row) => {
    const upper = row.invoiceNumber.toUpperCase();
    const document = fiscalBySale.get(upper) ?? null;
    const saleReturns = returnsBySale.get(upper) ?? [];
    return {
      key: row.invoiceNumber,
      row,
      branch: branchOfNumber(row.invoiceNumber),
      state: row.status === 'Voided' ? 'anulada' : saleReturns.length > 0 ? 'devolucion' : 'vigente',
      fiscal: document,
      fiscalState: document?.status ?? NO_INVOICE,
      returns: saleReturns,
      refunded: roundTo(
        saleReturns.reduce((sum, item) => sum + item.refund, 0),
        2,
      ),
      webCustomer: isWebCustomer(row.customerCode),
    };
  });
}

/** Aplica los filtros de la página a las ventas (el servidor ya filtró las fechas). */
export function filterSales(items: readonly SaleItem[], filters: SalesFilters): SaleItem[] {
  return items.filter(
    (item) =>
      (!filters.sucursal || item.branch === filters.sucursal) &&
      (!filters.cliente || item.row.customerCode === filters.cliente) &&
      (!filters.cajero || item.row.cashier === filters.cajero) &&
      (!filters.pago || item.row.paymentMethod === filters.pago) &&
      (!filters.estado || item.state === filters.estado) &&
      (!filters.fiscal || item.fiscalState === filters.fiscal) &&
      matchesSearch(filters.q, [item.row.invoiceNumber, item.row.orderNumber, item.row.customer, item.row.customerCode, item.fiscal ? String(item.fiscal.number) : null]),
  );
}

export interface Option {
  value: string;
  label: string;
}

const collator = new Intl.Collator('es', { sensitivity: 'base' });

/** Opciones de una lista desplegable con los valores distintos que aparecen (ordenados). */
export function distinctOptions(values: readonly (string | null | undefined)[]): Option[] {
  const unique = [...new Set(values.filter((value): value is string => Boolean(value && value.trim())))];
  return unique.sort((a, b) => collator.compare(a, b)).map((value) => ({ value, label: value }));
}

/** Clientes que aparecen en las ventas cargadas (para el filtro «Cliente»). */
export function customerOptions(items: readonly SaleItem[]): { value: string; label: string; description: string }[] {
  const byCode = new Map<string, string>();
  for (const item of items) if (!byCode.has(item.row.customerCode)) byCode.set(item.row.customerCode, item.row.customer);
  return [...byCode.entries()]
    .map(([value, label]) => ({ value, label, description: value }))
    .sort((a, b) => collator.compare(a.label, b.label));
}

/** Sucursales de la sesión para el filtro «Sucursal» («CM · Casa matriz La Paz»). */
export function branchOptions(branches: readonly { code: string; name: string }[]): Option[] {
  return branches.map((branch) => ({ value: branch.code, label: `${branch.code} · ${branch.name}` }));
}

export interface SalesSummary {
  /** Ventas en la lista. */
  count: number;
  /** Ventas no anuladas. */
  valid: number;
  voided: number;
  /** Total vendido (sin las anuladas). */
  total: number;
  /** IVA incluido en lo vendido. */
  tax: number;
  /** Importe de las anuladas. */
  voidedTotal: number;
  /** Líneas vendidas (sin las anuladas). */
  lines: number;
  /** Ticket promedio y el mayor (sin las anuladas); 0 si no hay. */
  average: number;
  largest: number;
  /** Reembolsado en devoluciones de estas ventas. */
  refunded: number;
  withReturns: number;
}

/** Totales de las ventas de la lista (lo que el escritorio mostraba en sus tarjetas). */
export function salesSummary(items: readonly SaleItem[]): SalesSummary {
  const valid = items.filter((item) => item.state !== 'anulada');
  const voided = items.filter((item) => item.state === 'anulada');
  const total = roundTo(
    valid.reduce((sum, item) => sum + item.row.total, 0),
    2,
  );
  return {
    count: items.length,
    valid: valid.length,
    voided: voided.length,
    total,
    tax: roundTo(
      valid.reduce((sum, item) => sum + item.row.tax, 0),
      2,
    ),
    voidedTotal: roundTo(
      voided.reduce((sum, item) => sum + item.row.total, 0),
      2,
    ),
    lines: valid.reduce((sum, item) => sum + item.row.items, 0),
    average: valid.length > 0 ? roundTo(total / valid.length, 2) : 0,
    largest: valid.reduce((max, item) => Math.max(max, item.row.total), 0),
    refunded: roundTo(
      items.reduce((sum, item) => sum + item.refunded, 0),
      2,
    ),
    withReturns: items.filter((item) => item.returns.length > 0).length,
  };
}

export function saleStateLabel(state: string): string {
  return statusOf(SALE_STATES, state).label;
}

/** Qué operaciones puede ejecutar la sesión (permisos que el contrato declara para cada una). */
export interface SaleAbilities {
  reprint: boolean;
  email: boolean;
  returns: boolean;
  void: boolean;
}

/** Una acción sobre la venta: si se ofrece y, si no se puede hacer ahora, por qué. */
export interface SaleAction {
  visible: boolean;
  blocked: string | null;
}

export interface SaleActions {
  reprint: SaleAction;
  email: SaleAction;
  returns: SaleAction;
  void: SaleAction;
}

/**
 * Acciones de una venta para la sesión: se ofrecen solo si el rol puede ejecutarlas; las que el servidor rechazaría por el
 * estado de la venta se muestran con el motivo (comodidad: el servidor decide igual).
 */
export function saleActions(item: SaleItem, abilities: SaleAbilities, fiscalEnabled: boolean): SaleActions {
  const hasInvoice = fiscalEnabled && item.fiscal !== null;
  const voided = item.state === 'anulada';
  return {
    reprint: { visible: hasInvoice && abilities.reprint, blocked: null },
    email: { visible: hasInvoice && abilities.email, blocked: hasInvoice ? emailBlockedReason(item.fiscal) : null },
    returns: {
      visible: abilities.returns && !voided,
      blocked: item.fiscal?.status === 'Voided' ? 'La factura del SIN de esta venta está anulada: no admite devoluciones.' : null,
    },
    void: {
      visible: abilities.void && !voided,
      blocked: item.returns.length > 0 ? 'La venta tiene devoluciones registradas: no se puede anular entera.' : null,
    },
  };
}

/** Columnas del CSV de ventas (más completas que la tabla). */
export const SALES_CSV: readonly CsvColumn<SaleItem>[] = [
  { header: 'Venta', value: (item) => item.row.invoiceNumber },
  { header: 'Pedido', value: (item) => item.row.orderNumber },
  { header: 'Fecha y hora', value: (item) => toDate(item.row.issuedAt) },
  { header: 'Sucursal', value: (item) => item.branch },
  { header: 'Código del cliente', value: (item) => item.row.customerCode },
  { header: 'Cliente', value: (item) => item.row.customer },
  { header: 'Cajero', value: (item) => item.row.cashier },
  { header: 'Medio de pago', value: (item) => item.row.paymentMethod },
  { header: 'Líneas', value: (item) => item.row.items },
  { header: 'Total', value: (item) => item.row.total },
  { header: 'IVA', value: (item) => item.row.tax },
  { header: 'Estado', value: (item) => saleStateLabel(item.state) },
  { header: 'Motivo de anulación', value: (item) => item.row.voidReason },
  { header: 'Reembolsado', value: (item) => item.refunded },
  { header: 'Factura del SIN', value: (item) => item.fiscal?.number ?? null },
  { header: 'Estado de la factura', value: (item) => fiscalStatusLabel(item.fiscal) },
  { header: 'CUF', value: (item) => item.fiscal?.cuf ?? null },
];

// ---------------------------------------------------------------------------------------------------- devoluciones

/** Una devolución lista para la tabla. */
export interface ReturnItem {
  key: string;
  row: ReturnRecord;
  branch: string | null;
  /** Estado de la nota crédito-débito o `NO_NOTE`. */
  noteState: string;
}

export function toReturnItems(returns: readonly ReturnRecord[]): ReturnItem[] {
  return returns.map((row) => ({ key: row.number, row, branch: branchOfNumber(row.number), noteState: row.creditNoteStatus ?? NO_NOTE }));
}

export function noteStateLabel(state: string): string {
  return statusOf(NOTE_STATES, state).label;
}

/**
 * Filtros de la pestaña «Devoluciones»: las fechas elegidas (día de La Paz de la devolución), la sucursal, la nota, la
 * búsqueda y el cliente (por sus ventas cargadas o por su nombre).
 */
export function filterReturns(
  items: readonly ReturnItem[],
  filters: SalesFilters,
  range: DateRange,
  customer: { invoices: ReadonlySet<string>; name: string | null } | null,
): ReturnItem[] {
  return items.filter(
    (item) =>
      inRange(item.row.returnedAt, range) &&
      (!filters.sucursal || item.branch === filters.sucursal) &&
      (!filters.nota || item.noteState === filters.nota) &&
      (!filters.cliente || (customer !== null && (customer.invoices.has(item.row.invoiceNumber.toUpperCase()) || (customer.name !== null && item.row.customer === customer.name)))) &&
      matchesSearch(filters.q, [item.row.number, item.row.invoiceNumber, item.row.customer, item.row.reason, item.row.creditNote]),
  );
}

export const RETURNS_CSV: readonly CsvColumn<ReturnItem>[] = [
  { header: 'Devolución', value: (item) => item.row.number },
  { header: 'Fecha y hora', value: (item) => toDate(item.row.returnedAt) },
  { header: 'Sucursal', value: (item) => item.branch },
  { header: 'Venta', value: (item) => item.row.invoiceNumber },
  { header: 'Cliente', value: (item) => item.row.customer },
  { header: 'Motivo', value: (item) => item.row.reason },
  { header: 'Reembolso', value: (item) => item.row.refund },
  { header: 'Nota crédito-débito', value: (item) => item.row.creditNote },
  { header: 'Estado de la nota', value: (item) => noteStateLabel(item.noteState) },
];

// ---------------------------------------------------------------------------------------------------- estadísticas

/** Los últimos `days` días de La Paz, hoy incluido. */
export function lastDays(days: number, now: Date = new Date()): DayRange {
  const today = laPazToday(now);
  return { from: addDays(today, -(Math.max(1, days) - 1)), to: today };
}

export interface DayTotal {
  /** Día de La Paz («2026-09-29»). */
  day: string;
  total: number;
  count: number;
}

/** Lo vendido por día (fecha de la venta), sin las anuladas; cada día del rango aparece aunque no tenga ventas. */
export function totalsByDay(rows: readonly SaleRecord[], range: DayRange): DayTotal[] {
  const days: DayTotal[] = [];
  for (let day = range.from; day <= range.to; day = addDays(day, 1)) days.push({ day, total: 0, count: 0 });
  const byDay = new Map(days.map((item) => [item.day, item]));
  for (const row of rows) {
    if (row.status === 'Voided') continue;
    const day = byDay.get(row.date.slice(0, 10)) ?? byDay.get(toIsoDate(row.issuedAt) ?? '');
    if (!day) continue;
    day.total = roundTo(day.total + row.total, 2);
    day.count += 1;
  }
  return days;
}

export interface MethodTotal {
  label: string;
  value: number;
  count: number;
}

/** Lo cobrado por medio de pago, sin las anuladas (de mayor a menor). */
export function totalsByPaymentMethod(rows: readonly SaleRecord[]): MethodTotal[] {
  const byMethod = new Map<string, MethodTotal>();
  for (const row of rows) {
    if (row.status === 'Voided') continue;
    const current = byMethod.get(row.paymentMethod) ?? { label: row.paymentMethod, value: 0, count: 0 };
    current.value = roundTo(current.value + row.total, 2);
    current.count += 1;
    byMethod.set(row.paymentMethod, current);
  }
  return [...byMethod.values()].sort((a, b) => b.value - a.value || collator.compare(a.label, b.label));
}

// ---------------------------------------------------------------------------------------------------- motivos

/** Motivos sugeridos al anular (los del escritorio); «Otro motivo» deja escribirlo. */
export const VOID_REASONS: readonly string[] = [
  'Error de cobro: el cliente cambió de producto',
  'Devolución del cliente',
  'Producto defectuoso',
  'Venta duplicada',
  'Error en el precio',
];

/** Motivos sugeridos para una devolución (los del escritorio). */
export const RETURN_REASONS: readonly string[] = ['Producto con falla', 'Cambio de producto', 'Error en el despacho', 'El cliente desistió de la compra'];

/** Motivo que sugiere una devolución POR FALLA. */
export const DEFECT_REASON = 'Producto con falla';

/** Valor de la lista de motivos para escribir uno propio. */
export const OTHER_REASON = '_otro';

/** Largo máximo del motivo (el del servidor). */
export const REASON_MAX = 200;

export function reasonOptions(reasons: readonly string[]): Option[] {
  return [...reasons.map((reason) => ({ value: reason, label: reason })), { value: OTHER_REASON, label: 'Otro motivo (escribirlo)' }];
}

/** El motivo que se envía: el elegido o el escrito. */
export function reasonText(choice: string, other: string): string {
  return choice === OTHER_REASON ? other.trim() : choice.trim();
}

/** Por qué no sirve un motivo (las reglas del servidor), o null. */
export function reasonProblem(reason: string): string | null {
  if (reason.trim().length === 0) return 'Indique el motivo.';
  if (reason.trim().length > REASON_MAX) return `Use como máximo ${REASON_MAX} caracteres.`;
  return null;
}

// ---------------------------------------------------------------------------------------------------- devolución

/** Parte de un producto en la venta (una línea): cuánto queda y a qué precio (para estimar el reembolso). */
export interface ReturnPart {
  available: number;
  unitPrice: number;
  discountPercent: number;
}

/** Un producto de la venta en el formulario de devolución (las líneas del mismo SKU van juntas). */
export interface ReturnDraftLine {
  sku: string;
  name: string;
  unit: string;
  sold: number;
  returned: number;
  /** Lo que todavía se puede devolver (vendido − devuelto). */
  available: number;
  /** Precio y descuento de la primera línea (para mostrar). */
  unitPrice: number;
  discountPercent: number;
  /** Hay líneas del mismo producto con distinto precio o descuento. */
  mixedPrices: boolean;
  parts: ReturnPart[];
  /** Series (o IMEI) vendidas del producto en esta venta: si hay, se devuelve marcando las series (regla T-02). */
  serials: string[];
}

function round6(value: number): number {
  return roundTo(value, 6);
}

/** Arma el formulario de la devolución: una fila por producto, con lo que queda por devolver y sus series. */
export function buildReturnDraft(returnable: readonly ReturnableRecord[], saleLines: readonly SaleLineRecord[] | undefined): ReturnDraftLine[] {
  const serialsBySku = new Map<string, Set<string>>();
  for (const line of saleLines ?? []) {
    if (!line.serials || line.serials.length === 0) continue;
    const key = line.sku.toUpperCase();
    const set = serialsBySku.get(key) ?? new Set<string>();
    for (const serial of line.serials) set.add(serial);
    serialsBySku.set(key, set);
  }
  const bySku = new Map<string, ReturnDraftLine>();
  for (const line of returnable) {
    const key = line.sku.toUpperCase();
    const available = Math.max(0, round6(line.sold - line.returned));
    const part: ReturnPart = { available, unitPrice: line.unitPrice, discountPercent: line.discountPercent };
    const current = bySku.get(key);
    if (current) {
      current.sold = round6(current.sold + line.sold);
      current.returned = round6(current.returned + line.returned);
      current.available = round6(current.available + available);
      current.mixedPrices ||= line.unitPrice !== current.unitPrice || line.discountPercent !== current.discountPercent;
      current.parts.push(part);
      continue;
    }
    bySku.set(key, {
      sku: line.sku,
      name: line.name,
      unit: line.unit,
      sold: line.sold,
      returned: line.returned,
      available,
      unitPrice: line.unitPrice,
      discountPercent: line.discountPercent,
      mixedPrices: false,
      parts: [part],
      serials: [...(serialsBySku.get(key) ?? [])].sort(),
    });
  }
  return [...bySku.values()];
}

export function isSerialized(line: ReturnDraftLine): boolean {
  return line.serials.length > 0;
}

/**
 * Reembolso estimado de devolver `quantity` de un producto: se toma de sus líneas en orden (como el servidor) y cada una
 * se redondea a 2 decimales (cantidad × precio × (1 − descuento / 100)). El importe que vale es el que responde el servidor.
 */
export function lineRefund(line: ReturnDraftLine, quantity: number): number {
  let pending = Math.max(0, quantity);
  let refund = 0;
  for (const part of line.parts) {
    if (pending <= 0) break;
    const take = Math.min(pending, part.available);
    if (take <= 0) continue;
    refund += roundTo(take * part.unitPrice * (1 - part.discountPercent / 100), 2);
    pending = round6(pending - take);
  }
  return roundTo(refund, 2);
}

/** Cantidad a devolver de un producto: las series marcadas (serializado) o la cantidad escrita. */
export function returnQuantity(line: ReturnDraftLine, quantities: Readonly<Record<string, number | null>>, serials: Readonly<Record<string, readonly string[]>>): number {
  if (isSerialized(line)) return serials[line.sku]?.length ?? 0;
  return quantities[line.sku] ?? 0;
}

/** Reembolso estimado de toda la devolución. */
export function estimatedRefund(
  lines: readonly ReturnDraftLine[],
  quantities: Readonly<Record<string, number | null>>,
  serials: Readonly<Record<string, readonly string[]>>,
): number {
  return roundTo(
    lines.reduce((sum, line) => sum + lineRefund(line, returnQuantity(line, quantities, serials)), 0),
    2,
  );
}

export interface ReturnErrors {
  /** Error general de las líneas (nada para devolver). */
  lines?: string;
  /** Error de cada producto (por SKU). */
  bySku: Record<string, string>;
  reason?: string;
  method?: string;
}

/** Revisa el formulario antes de enviarlo (las mismas reglas del servidor). null si está bien. */
export function returnProblems(
  lines: readonly ReturnDraftLine[],
  quantities: Readonly<Record<string, number | null>>,
  serials: Readonly<Record<string, readonly string[]>>,
  reason: string,
  methodCode: string,
): ReturnErrors | null {
  const errors: ReturnErrors = { bySku: {} };
  let something = false;
  for (const line of lines) {
    const quantity = returnQuantity(line, quantities, serials);
    if (quantity > 0) something = true;
    if (quantity > line.available) {
      errors.bySku[line.sku] = line.available > 0 ? `Puede devolver hasta ${formatQuantity(line.available)} ${line.unit}.` : 'Ya se devolvió todo lo vendido de este producto.';
    }
  }
  if (!something) errors.lines = 'Indique qué productos se devuelven: una cantidad o las series.';
  const problem = reasonProblem(reason);
  if (problem) errors.reason = problem === 'Indique el motivo.' ? 'Indique el motivo de la devolución.' : problem;
  if (!methodCode) errors.method = 'Elija el medio con el que se reembolsa.';
  return errors.lines || errors.reason || errors.method || Object.keys(errors.bySku).length > 0 ? errors : null;
}

/** El pedido de la devolución con la forma EXACTA del contrato (todos los parámetros, series solo si el producto las lleva). */
export function toReturnPayload(
  invoiceNumber: string,
  lines: readonly ReturnDraftLine[],
  quantities: Readonly<Record<string, number | null>>,
  serials: Readonly<Record<string, readonly string[]>>,
  reason: string,
  methodCode: string,
  defective: boolean,
): ReturnPayload {
  return {
    invoiceNumber,
    reason: reason.trim(),
    refundPaymentMethodCode: methodCode,
    lines: lines
      .filter((line) => returnQuantity(line, quantities, serials) > 0)
      .map((line) =>
        isSerialized(line)
          ? { sku: line.sku, quantity: serials[line.sku]?.length ?? 0, serials: [...(serials[line.sku] ?? [])] }
          : { sku: line.sku, quantity: quantities[line.sku] ?? 0, serials: null },
      ),
    defective,
  };
}

/** Medio de reembolso por defecto: efectivo (como el escritorio) o el primero. */
export function defaultRefundMethod(methods: readonly RefundMethod[]): string {
  return (methods.find((method) => method.code.toUpperCase() === 'EFECTIVO') ?? methods[0])?.code ?? '';
}

/** Número de venta escrito por la persona («f-cm-000123 » → «F-CM-000123»). */
export function normalizeSaleNumber(text: string): string {
  return text.trim().toUpperCase();
}
