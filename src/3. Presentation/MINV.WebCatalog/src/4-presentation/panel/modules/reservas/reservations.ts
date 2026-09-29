// Módulo «Reservas» · funciones puras (sin React): qué es una reserva en la lista (un armado o un carrito que se reservó
// alguna vez), su estado en palabras, el plazo para recogerla (resaltado si vence en menos de 6 horas), el estado de su
// correo, los filtros de la lista (tipo, canal, estado, vence, sucursal, fechas y búsqueda), las columnas del CSV, el
// resumen de la estadística del tablero, el teléfono (copiar y WhatsApp) y el formulario de la reserva en mostrador.
//
// Los tipos del servidor SALEN DEL CONTRATO (regla P-07). Las reglas de verdad (stock, permisos, vencimiento, datos de
// factura) las aplica el servidor en cada pedido (regla P-01): aquí solo se muestra, se filtra y se valida por comodidad.

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses, statusOf, type SelectOption } from '@/4-presentation/panel/kit';
import { addDays, formatDateTime, formatMoney, formatNumber, inRange, laPazToday, matchesSearch, toDate, toIsoDate, type CsvColumn } from '@/4-presentation/panel/lib';

// ---------------------------------------------------------------------------------------------------- tipos del servidor

/** Una fila de `GetPcBuildsQuery` (armado de PC o carrito) tal como la manda el servidor. */
export type BuildRecord = RpcResponseOf<'GetPcBuildsQuery'>[number];
/** El detalle de `GetPcBuildQuery` (fila, líneas cotizadas, informe y bitácora). */
export type BuildDetailData = RpcResponseOf<'GetPcBuildQuery'>;
/** Una línea del detalle (producto, cantidad, precio congelado y disponibilidad). */
export type BuildLineData = BuildDetailData['quotedItems'][number];
/** Un hecho de la bitácora de la reserva. */
export type BuildEventData = NonNullable<BuildDetailData['history']>[number];
/** Una fila de la cola de correos (`GetOutgoingMailsQuery`). */
export type MailRecord = RpcResponseOf<'GetOutgoingMailsQuery'>[number];
/** Un producto que se puede vender, con su precio y lo disponible (existencias − reservado) en la sucursal activa. */
export type ProductRecord = RpcResponseOf<'GetSellableProductsQuery'>[number];
/** Un cliente registrado. */
export type CustomerRecord = RpcResponseOf<'GetCustomersQuery'>['customers'][number];

type BuildsRequest = RpcRequestOf<'GetPcBuildsQuery'>;
type CartRequest = RpcRequestOf<'ReserveCartCommand'>;

// ---------------------------------------------------------------------------------------------------- textos y estados

/** Tipo de la reserva (enumeración `PcBuildKind` del servidor): carrito de compra (RES-…) o armado de PC (ARM-…). */
export const KINDS = defineStatuses({
  Cart: { label: 'Compra', tone: 'info' },
  Build: { label: 'Armado', tone: 'accent' },
});

/** Canal (enumeración `PcBuildChannel`): la tienda web o el mostrador (el personal, desde el escritorio o el panel). */
export const CHANNELS = defineStatuses({
  Web: { label: 'Web', tone: 'info' },
  Desktop: { label: 'Mostrador', tone: 'neutral' },
});

/** Estado de la reserva en palabras (se deduce del estado del armado, de su plazo y del motivo del cierre). */
export const STATES = defineStatuses({
  reservada: { label: 'Reservada', tone: 'accent' },
  vencida: { label: 'Vencida', tone: 'warning' },
  vendida: { label: 'Vendida', tone: 'success' },
  liberada: { label: 'Liberada', tone: 'neutral' },
});
export type ReservationState = keyof typeof STATES;

/** Estado del correo de confirmación (enumeración `OutgoingMailStatus`) y los casos sin correo. */
export const MAIL_STATES = defineStatuses({
  Sent: { label: 'Enviado', tone: 'success' },
  Pending: { label: 'Pendiente', tone: 'info' },
  Exhausted: { label: 'No se pudo enviar', tone: 'danger' },
  Cancelled: { label: 'Cancelado', tone: 'neutral' },
  sinCorreo: { label: 'Sin correo', tone: 'neutral' },
  sinEnvio: { label: 'No se envió', tone: 'neutral' },
  sinDatos: { label: '—', tone: 'neutral' },
});

/** Motivo con que el trabajo automático cierra una reserva vencida (`PcBuild.ExpiredReason`). */
export const EXPIRED_REASON = 'Vencida';

/** Una reserva que vence dentro de este plazo se resalta (queda poco para que el cliente pase a cobrarla). */
export const EXPIRING_SOON_MS = 6 * 60 * 60 * 1000;

/** Nombre de cada ranura de un armado (enumeración `PcSlot`); una línea de carrito no tiene ranura. */
const SLOT_LABELS: Readonly<Record<string, string>> = {
  Cpu: 'Procesador',
  Motherboard: 'Placa madre',
  Ram: 'Memoria RAM',
  Gpu: 'Tarjeta de video',
  Storage: 'Almacenamiento',
  Psu: 'Fuente de poder',
  Case: 'Gabinete',
  Cooler: 'Refrigeración',
  Monitor: 'Monitor',
  Peripheral: 'Periféricos',
  Software: 'Software',
  Service: 'Servicios',
};

export function slotLabel(slot: string | null | undefined): string {
  return slot ? (SLOT_LABELS[slot] ?? slot) : 'Producto';
}

/** Qué pasó en cada hecho de la bitácora (enumeración `PcBuildEventAction`). */
const HISTORY_ACTIONS: Readonly<Record<string, string>> = {
  Created: 'Creada',
  Quoted: 'Cotizada',
  Reserved: 'Stock reservado',
  Released: 'Liberada',
  Expired: 'Vencida',
  Sold: 'Vendida en la caja',
  Cancelled: 'Anulada',
  Published: 'Publicada en la tienda web',
  Unpublished: 'Retirada de la tienda web',
};

export function historyAction(action: string): string {
  return HISTORY_ACTIONS[action] ?? action;
}

/** Tipos de documento del comprador para la factura (catálogo del SIN: 1 CI … 5 NIT, las reglas de `FiscalRules`). */
export const DOCUMENT_TYPES: readonly (SelectOption & { short: string; numeric: boolean; complement: boolean })[] = [
  { value: '1', label: 'Cédula de identidad (CI)', short: 'CI', numeric: true, complement: true },
  { value: '2', label: 'Cédula de extranjero (CEX)', short: 'CEX', numeric: false, complement: false },
  { value: '3', label: 'Pasaporte (PAS)', short: 'PAS', numeric: false, complement: false },
  { value: '4', label: 'Otro documento (OD)', short: 'OD', numeric: false, complement: false },
  { value: '5', label: 'NIT', short: 'NIT', numeric: true, complement: false },
];

export function documentTypeLabel(code: number | null | undefined): string | null {
  if (code == null) return null;
  return DOCUMENT_TYPES.find((type) => type.value === String(code))?.label ?? `Documento ${code}`;
}

/** Motivos frecuentes para liberar una reserva (los mismos que sugiere el escritorio). */
export const RELEASE_REASONS: readonly string[] = ['El cliente desistió', 'El cliente no pasó a recoger', 'Sin respuesta del cliente', 'Reservado por error'];
/** Valor de la opción «Otro motivo» (se escribe a mano). */
export const OTHER_REASON = '_otro';
export const REASON_MAX_LENGTH = 250;

/** El motivo que se envía al liberar (vacío si falta). */
export function releaseReason(choice: string, other: string): string {
  return choice === OTHER_REASON ? other.trim() : choice.trim();
}

// ---------------------------------------------------------------------------------------------------- teléfono

const COUNTRY_CODE = '591';

/**
 * Dígitos locales (7 u 8) de un teléfono boliviano, con o sin «+591», espacios o guiones; null si no es uno. El servidor
 * guarda el teléfono ya normalizado («+59171234567» o «71234567»).
 */
export function localPhoneDigits(phone: string | null | undefined): string | null {
  const digits = (phone ?? '').replace(/\D/g, '');
  const local = digits.length >= 10 && digits.startsWith(COUNTRY_CODE) ? digits.slice(COUNTRY_CODE.length) : digits;
  return local.length === 7 || local.length === 8 ? local : null;
}

/** «+591 71234567» (o el texto tal cual si no se reconoce). */
export function formatPhone(phone: string | null | undefined): string | null {
  if (!phone) return null;
  const local = localPhoneDigits(phone);
  return local ? `+${COUNTRY_CODE} ${local}` : phone;
}

/**
 * Enlace para escribirle por WhatsApp, armado SOLO con los dígitos del teléfono: `https://wa.me/591<número>`. La
 * dirección se compone por partes: es un enlace que el navegador abre en otra pestaña (no un recurso que cargue la
 * página), y la prueba de arquitectura vigila que el código del panel no nombre recursos de otros dominios (regla P-11).
 */
const WHATSAPP_BASE = ['https:', '', 'wa.me'].join('/');

export function whatsappUrl(phone: string | null | undefined): string | null {
  const local = localPhoneDigits(phone);
  return local ? `${WHATSAPP_BASE}/${COUNTRY_CODE}${local}` : null;
}

// ---------------------------------------------------------------------------------------------------- plazo

export interface HoldInfo {
  /** «29/09/2026 18:00» · «29/09/2026 12:30 · vence en 2 h 30 min» · «Venció el 28/09/2026 18:00». */
  text: string;
  /** normal · soon (vence en menos de 6 h) · expired (reservada con el plazo cumplido) · none (sin plazo vigente). */
  tone: 'normal' | 'soon' | 'expired' | 'none';
}

/** «2 h 30 min» · «45 min» · «menos de 1 min». */
export function remainingText(ms: number): string {
  const minutes = Math.floor(ms / 60_000);
  if (minutes < 1) return 'menos de 1 min';
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  if (hours === 0) return `${rest} min`;
  return rest === 0 ? `${hours} h` : `${hours} h ${rest} min`;
}

/** El plazo para recoger la reserva, con el resaltado de la tabla. */
export function holdInfo(row: Pick<BuildRecord, 'reservedUntil' | 'status'>, now: Date): HoldInfo {
  const until = toDate(row.reservedUntil);
  if (!until) return { text: '—', tone: 'none' };
  if (row.status !== 'Reserved') return { text: formatDateTime(until), tone: 'none' };
  const left = until.getTime() - now.getTime();
  if (left <= 0) return { text: `Venció el ${formatDateTime(until)}`, tone: 'expired' };
  if (left < EXPIRING_SOON_MS) return { text: `${formatDateTime(until)} · vence en ${remainingText(left)}`, tone: 'soon' };
  return { text: formatDateTime(until), tone: 'normal' };
}

// ---------------------------------------------------------------------------------------------------- estado

/** ¿Se reservó alguna vez? (tiene plazo). Las cotizaciones que nunca se reservaron son del armador, no de esta lista. */
export function wasReserved(row: Pick<BuildRecord, 'reservedUntil'>): boolean {
  return toDate(row.reservedUntil) !== null;
}

/** Estado de la reserva: reservada (vigente), vencida (plazo cumplido, aún sin cerrar o cerrada por vencimiento), vendida o liberada. */
export function reservationState(row: Pick<BuildRecord, 'status' | 'reservedUntil' | 'cancelReason'>, now: Date): ReservationState {
  if (row.status === 'Sold') return 'vendida';
  if (row.status === 'Reserved') {
    const until = toDate(row.reservedUntil);
    return until && until.getTime() > now.getTime() ? 'reservada' : 'vencida';
  }
  if (row.status === 'Cancelled' && row.cancelReason === EXPIRED_REASON) return 'vencida';
  return 'liberada';
}

export function stateLabel(state: string): string {
  return statusOf(STATES, state).label;
}

/** ¿Se puede cobrar en la caja? Reservada y con los precios todavía vigentes (el servidor decide igual). */
export function canBeSold(row: Pick<BuildRecord, 'status' | 'isExpired'>): boolean {
  return row.status === 'Reserved' && !row.isExpired;
}

/** Dirección de la caja con la reserva cargada (módulo «Caja»: `/panel/caja?reserva=<NÚMERO>`). */
export function sellPath(number: string): string {
  return `caja?reserva=${encodeURIComponent(number)}`;
}

// ---------------------------------------------------------------------------------------------------- correo

/** El correo más reciente de cada reserva (por número). */
export function latestMails(mails: readonly MailRecord[]): Map<string, MailRecord> {
  const byNumber = new Map<string, MailRecord>();
  for (const mail of mails) {
    const current = byNumber.get(mail.reservation);
    if (!current || mail.requestedAt > current.requestedAt) byNumber.set(mail.reservation, mail);
  }
  return byNumber;
}

/**
 * Estado del correo de una reserva (una clave de `MAIL_STATES`): el de su último correo; si no hay, si tenía o no correo de
 * contacto. Un estado nuevo del servidor se muestra tal cual (`statusOf`).
 */
export function mailStateOf(row: Pick<BuildRecord, 'contactEmail'>, mail: MailRecord | null, known: boolean): string {
  if (mail) return mail.status;
  if (!known) return 'sinDatos';
  return row.contactEmail ? 'sinEnvio' : 'sinCorreo';
}

/** «2 de 5 intentos». */
export function attemptsText(mail: Pick<MailRecord, 'attempts' | 'maxAttempts'>): string {
  return `${formatNumber(mail.attempts)} de ${formatNumber(mail.maxAttempts)} ${mail.maxAttempts === 1 ? 'intento' : 'intentos'}`;
}

// ---------------------------------------------------------------------------------------------------- lista

/** Una fila lista para la tabla: lo del servidor más cómo se muestra. */
export interface ReservationItem {
  row: BuildRecord;
  state: ReservationState;
  /** El cliente registrado o, si no, quien la recoge. */
  client: string;
  /** Teléfono como lo muestra la tabla («+591 71234567»). */
  phone: string | null;
  whatsapp: string | null;
  hold: HoldInfo;
  mail: MailRecord | null;
  /** Clave de `MAIL_STATES`. */
  mailState: string;
}

/**
 * Filas de la lista: solo lo que se reservó alguna vez (con plazo), con su estado, su plazo y su último correo.
 * `mails` null = la cola de correos no se pudo leer (el estado del correo queda «—»).
 */
export function toReservationItems(rows: readonly BuildRecord[], mails: readonly MailRecord[] | null, now: Date): ReservationItem[] {
  const latest = latestMails(mails ?? []);
  return rows.filter(wasReserved).map((row) => {
    const mail = latest.get(row.number) ?? null;
    return {
      row,
      state: reservationState(row, now),
      client: row.customer ?? row.contactName ?? '—',
      phone: formatPhone(row.contactPhone),
      whatsapp: whatsappUrl(row.contactPhone),
      hold: holdInfo(row, now),
      mail,
      mailState: mailStateOf(row, mail, mails !== null),
    };
  });
}

/** Filtros de la lista (en la dirección de la página). */
export const RESERVATION_FILTERS = { q: '', tipo: '', canal: '', estado: '', vence: '', sucursal: '', desde: '', hasta: '' };
export type ReservationFilters = typeof RESERVATION_FILTERS;

/** «Vence»: las que vencen hoy, mañana o ya vencieron (días de La Paz). */
export const DUE_OPTIONS: readonly SelectOption[] = [
  { value: 'hoy', label: 'Hoy' },
  { value: 'manana', label: 'Mañana' },
  { value: 'vencidas', label: 'Ya vencidas' },
];

/**
 * Lo que filtra el SERVIDOR (`GetPcBuildsQuery`: estado, canal y tipo). El estado del armado sale del filtro «Estado»
 * cuando es uno solo (reservada → Reserved, vendida → Sold, liberada → Cancelled) o de «Vence» (hoy o mañana: solo las
 * reservadas). «Vencida» junta reservadas con el plazo cumplido y cerradas por vencimiento: se pide todo y se filtra aquí.
 */
export function buildsRequest(filters: Pick<ReservationFilters, 'tipo' | 'canal' | 'estado' | 'vence'>): BuildsRequest {
  const byState: Record<string, BuildRecord['status']> = { reservada: 'Reserved', vendida: 'Sold', liberada: 'Cancelled' };
  const status = byState[filters.estado] ?? (filters.estado === '' && (filters.vence === 'hoy' || filters.vence === 'manana') ? 'Reserved' : null);
  return {
    status,
    channel: filters.canal === 'Web' || filters.canal === 'Desktop' ? filters.canal : null,
    kind: filters.tipo === 'Build' || filters.tipo === 'Cart' ? filters.tipo : null,
  };
}

/** ¿Vence en el día pedido (hoy o mañana, en La Paz) y sigue vigente? */
function dueOn(item: ReservationItem, due: string, now: Date): boolean {
  if (due === 'vencidas') return item.state === 'vencida';
  if (item.state !== 'reservada') return false;
  const today = laPazToday(now);
  const day = toIsoDate(item.row.reservedUntil);
  return due === 'hoy' ? day === today : due === 'manana' ? day === addDays(today, 1) : true;
}

/** Aplica los filtros de la página (los del servidor ya vienen aplicados; se repiten por si la respuesta es de antes). */
export function filterReservations(items: readonly ReservationItem[], filters: ReservationFilters, now: Date): ReservationItem[] {
  const range = { from: filters.desde || null, to: filters.hasta || null };
  return items.filter(
    (item) =>
      (!filters.tipo || item.row.kind === filters.tipo) &&
      (!filters.canal || item.row.channel === filters.canal) &&
      (!filters.estado || item.state === filters.estado) &&
      (!filters.vence || dueOn(item, filters.vence, now)) &&
      (!filters.sucursal || item.row.branchCode === filters.sucursal) &&
      inRange(item.row.createdAt, range) &&
      matchesSearch(filters.q, [
        item.row.number,
        item.row.name,
        item.client,
        item.row.contactName,
        item.row.customer,
        item.phone,
        item.row.contactPhone,
        localPhoneDigits(item.row.contactPhone),
        item.row.contactEmail,
      ]),
  );
}

/** Opciones del filtro «Sucursal»: las que aparecen en las reservas cargadas. */
export function branchOptions(items: readonly ReservationItem[]): SelectOption[] {
  return [...new Set(items.map((item) => item.row.branchCode))].sort((a, b) => a.localeCompare(b, 'es')).map((code) => ({ value: code, label: code }));
}

export interface ListSummary {
  count: number;
  total: number;
  /** Reservadas que vencen en menos de 6 horas. */
  soon: number;
  /** Reservadas con el plazo cumplido que todavía retienen stock. */
  expired: number;
}

export function listSummary(items: readonly ReservationItem[]): ListSummary {
  return {
    count: items.length,
    total: items.reduce((sum, item) => sum + item.row.total, 0),
    soon: items.filter((item) => item.hold.tone === 'soon').length,
    expired: items.filter((item) => item.hold.tone === 'expired').length,
  };
}

/** Columnas del CSV (más completas que la tabla: nombre, sucursal, correo de contacto, fechas y cierre). */
export const CSV_COLUMNS: readonly CsvColumn<ReservationItem>[] = [
  { header: 'Número', value: (item) => item.row.number },
  { header: 'Nombre', value: (item) => item.row.name },
  { header: 'Tipo', value: (item) => statusOf(KINDS, item.row.kind).label },
  { header: 'Canal', value: (item) => statusOf(CHANNELS, item.row.channel).label },
  { header: 'Sucursal', value: (item) => item.row.branchCode },
  { header: 'Cliente', value: (item) => item.client },
  // Los dígitos locales: un texto que empieza con «+» se exportaría como «'+591…» (protección contra fórmulas del CSV).
  { header: 'Teléfono', value: (item) => localPhoneDigits(item.row.contactPhone) ?? item.row.contactPhone },
  { header: 'Correo de contacto', value: (item) => item.row.contactEmail },
  { header: 'Creada', value: (item) => toDate(item.row.createdAt) },
  { header: 'Reservado hasta', value: (item) => toDate(item.row.reservedUntil) },
  { header: 'Estado', value: (item) => stateLabel(item.state) },
  { header: 'Correo', value: (item) => statusOf(MAIL_STATES, item.mailState).label },
  { header: 'Total', value: (item) => item.row.total },
  { header: 'Unidades reservadas', value: (item) => item.row.reserved },
  { header: 'Venta', value: (item) => item.row.invoiceNumber },
  { header: 'Motivo del cierre', value: (item) => item.row.cancelReason },
];

// ---------------------------------------------------------------------------------------------------- estadística

export interface ActiveReservationsSummary {
  /** Reservadas y vigentes. */
  active: number;
  /** Total en Bs de las vigentes. */
  value: number;
  /** Vigentes que vencen en menos de 6 horas. */
  soon: number;
  /** Reservadas con el plazo cumplido que todavía retienen stock (las cierra el trabajo automático). */
  expired: number;
  /** Vigentes por tipo y canal (cantidad, con el valor como pista). */
  groups: { label: string; value: number; hint: string }[];
}

const GROUP_LABELS: readonly { kind: BuildRecord['kind']; channel: BuildRecord['channel']; label: string }[] = [
  { kind: 'Cart', channel: 'Web', label: 'Compras de la tienda web' },
  { kind: 'Cart', channel: 'Desktop', label: 'Compras de mostrador' },
  { kind: 'Build', channel: 'Web', label: 'Armados de la tienda web' },
  { kind: 'Build', channel: 'Desktop', label: 'Armados de mostrador' },
];

/** Resumen de las reservas vigentes (de las filas reservadas que manda el servidor). */
export function activeReservationsSummary(rows: readonly BuildRecord[], now: Date): ActiveReservationsSummary {
  const reserved = rows.filter((row) => row.status === 'Reserved');
  const active = reserved.filter((row) => reservationState(row, now) === 'reservada');
  const groups = GROUP_LABELS.map(({ kind, channel, label }) => {
    const mine = active.filter((row) => row.kind === kind && row.channel === channel);
    return { label, value: mine.length, hint: formatMoney(mine.reduce((sum, row) => sum + row.total, 0)) };
  }).filter((group) => group.value > 0);
  return {
    active: active.length,
    value: active.reduce((sum, row) => sum + row.total, 0),
    soon: active.filter((row) => holdInfo(row, now).tone === 'soon').length,
    expired: reserved.length - active.length,
    groups,
  };
}

// ---------------------------------------------------------------------------------------------------- disponibilidad

/** Disponibilidad de una línea: reservada (lo que hay además de lo reservado) o, si no, si alcanza lo que hay. */
export function availabilityText(line: Pick<BuildLineData, 'stock' | 'quantity'>, reserved: boolean): { text: string; tone: 'accent' | 'success' | 'danger' } {
  const stock = formatNumber(line.stock, { maxDecimals: 3 });
  const plural = line.stock === 1 ? '' : 's';
  if (reserved) return { text: line.stock > 0 ? `Reservada · ${stock} más disponible${plural}` : 'Reservada · sin más unidades', tone: 'accent' };
  if (line.stock >= line.quantity) return { text: `Disponible ${stock}`, tone: 'success' };
  return { text: line.stock > 0 ? `Solo ${stock} disponible${plural}` : 'Sin stock', tone: 'danger' };
}

// ---------------------------------------------------------------------------------------------------- reserva en mostrador

/** Límites del servidor (`PcBuild.MaxLines`, `PcBuild.MaxQuantity`, validadores de `ReserveCartCommand`). */
export const CART_LIMITS = { lines: 20, quantity: 16, name: 120, phone: 30, email: 254, notes: 500, title: 150, document: 20, complement: 5, legalName: 150 } as const;

/** Días para recoger (1 a 3: 24, 48 o 72 horas). */
export const HOLD_DAY_OPTIONS: readonly SelectOption[] = [
  { value: '1', label: '1 día (24 horas)' },
  { value: '2', label: '2 días (48 horas)' },
  { value: '3', label: '3 días (72 horas)' },
];
export const DEFAULT_HOLD_DAYS = '2';

export interface CartLine {
  sku: string;
  name: string;
  price: number;
  /** Disponible (existencias − reservado) al abrir el formulario: solo informa, el servidor decide al reservar. */
  available: number;
  /** null si el campo está vacío o mal escrito. */
  quantity: number | null;
}

export interface CartForm {
  lines: CartLine[];
  customerCode: string;
  contactName: string;
  contactPhone: string;
  contactEmail: string;
  holdDays: string;
  notes: string;
  name: string;
  documentType: string;
  documentNumber: string;
  complement: string;
  legalName: string;
}

export const EMPTY_CART: CartForm = {
  lines: [],
  customerCode: '',
  contactName: '',
  contactPhone: '',
  contactEmail: '',
  holdDays: DEFAULT_HOLD_DAYS,
  notes: '',
  name: '',
  documentType: '',
  documentNumber: '',
  complement: '',
  legalName: '',
};

export type CartField = 'lines' | 'contactName' | 'contactPhone' | 'contactEmail' | 'notes' | 'name' | 'documentType' | 'documentNumber' | 'complement' | 'legalName';
export type CartProblems = Partial<Record<CartField, string>>;

/** Agrega un producto (o suma uno si ya estaba, hasta 16). */
export function addCartLine(lines: readonly CartLine[], product: ProductRecord): CartLine[] {
  const existing = lines.find((line) => line.sku === product.sku);
  if (existing) return lines.map((line) => (line.sku === product.sku ? { ...line, quantity: Math.min(CART_LIMITS.quantity, (line.quantity ?? 0) + 1) } : line));
  return [...lines, { sku: product.sku, name: product.name, price: product.price, available: product.available, quantity: 1 }];
}

/** Texto libre de una línea (el servidor rechaza saltos de línea, tabuladores y otros caracteres de control). */
function hasControlCharacters(text: string): boolean {
  return /\p{Cc}/u.test(text);
}

const PHONE_SEPARATORS = /[\s\-.()]/g;
const BOLIVIAN_PHONE = /^(\+?591)?[0-9]{7,8}$/;

/** ¿Teléfono boliviano? 7 u 8 dígitos, con o sin +591 (la regla de `PcBuild.NormalizePhone`). */
export function isBolivianPhone(phone: string): boolean {
  return BOLIVIAN_PHONE.test(phone.trim().replace(PHONE_SEPARATORS, ''));
}

/** Validación mínima de un correo (el servidor vuelve a validarlo). */
export function isEmail(value: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(value.trim());
}

/** Problemas del formulario (los mismos que rechazaría el servidor), campo por campo. Vacío = se puede enviar. */
export function cartProblems(form: CartForm): CartProblems {
  const problems: CartProblems = {};
  if (form.lines.length === 0) problems.lines = 'Agregue al menos un producto a la reserva.';
  else if (form.lines.length > CART_LIMITS.lines) problems.lines = `Una reserva admite como máximo ${CART_LIMITS.lines} productos distintos.`;
  else if (form.lines.some((line) => line.quantity === null || !Number.isInteger(line.quantity) || line.quantity < 1 || line.quantity > CART_LIMITS.quantity))
    problems.lines = `La cantidad de cada producto va de 1 a ${CART_LIMITS.quantity}.`;

  const name = form.contactName.trim();
  if (!name) problems.contactName = 'Indique el nombre de quien recoge la reserva.';
  else if (name.length > CART_LIMITS.name) problems.contactName = `Use como máximo ${CART_LIMITS.name} caracteres.`;
  else if (hasControlCharacters(name)) problems.contactName = 'El nombre va en una sola línea.';

  const phone = form.contactPhone.trim();
  if (!phone) problems.contactPhone = 'Indique un teléfono o WhatsApp para avisar al cliente.';
  else if (!isBolivianPhone(phone)) problems.contactPhone = 'El teléfono debe tener 7 u 8 dígitos (Bolivia), con o sin +591.';

  const email = form.contactEmail.trim();
  if (email && (email.length > CART_LIMITS.email || !isEmail(email))) problems.contactEmail = 'Escriba un correo válido, por ejemplo nombre@correo.com.';

  if (form.notes.trim().length > CART_LIMITS.notes) problems.notes = `Las notas tienen como máximo ${CART_LIMITS.notes} caracteres.`;
  else if (hasControlCharacters(form.notes.trim())) problems.notes = 'Las notas van en una sola línea.';
  if (form.name.trim().length > CART_LIMITS.title) problems.name = `Use como máximo ${CART_LIMITS.title} caracteres.`;
  else if (hasControlCharacters(form.name.trim())) problems.name = 'El nombre de la reserva va en una sola línea.';

  // Datos para la factura (opcionales): sin tipo no se guarda nada; con tipo, el número es obligatorio.
  const type = DOCUMENT_TYPES.find((option) => option.value === form.documentType);
  const number = form.documentNumber.trim();
  const complement = form.complement.trim();
  const legalName = form.legalName.trim();
  if (!type) {
    if (number || complement || legalName) problems.documentType = 'Elija el tipo de documento para guardar los datos de la factura.';
  } else {
    if (!number) problems.documentNumber = 'Indique el número de documento.';
    else if (number.length > CART_LIMITS.document) problems.documentNumber = `Use como máximo ${CART_LIMITS.document} caracteres.`;
    else if (type.numeric && !/^[0-9]+$/.test(number)) problems.documentNumber = `Con ${type.short} el número solo admite dígitos.`;
    else if (hasControlCharacters(number)) problems.documentNumber = 'El número tiene caracteres no permitidos.';
    if (complement) {
      if (!type.complement) problems.complement = 'El complemento solo se usa con la cédula de identidad.';
      else if (complement.length > CART_LIMITS.complement || !/^[0-9A-Za-z]+$/.test(complement)) problems.complement = 'Hasta 5 letras o números.';
    }
    if (legalName.length > CART_LIMITS.legalName) problems.legalName = `Use como máximo ${CART_LIMITS.legalName} caracteres.`;
    else if (hasControlCharacters(legalName)) problems.legalName = 'La razón social va en una sola línea.';
  }
  return problems;
}

/** El pedido de `ReserveCartCommand` con TODOS sus parámetros (lo vacío viaja como null). */
export function toReserveCartPayload(form: CartForm): CartRequest {
  const type = DOCUMENT_TYPES.find((option) => option.value === form.documentType);
  const complement = form.complement.trim();
  return {
    items: form.lines.map((line) => ({ sku: line.sku, quantity: line.quantity ?? 1 })),
    contactName: form.contactName.trim(),
    contactPhone: form.contactPhone.trim(),
    contactEmail: form.contactEmail.trim() || null,
    notes: form.notes.trim() || null,
    holdDays: Number(form.holdDays) || null,
    buyer: type
      ? {
          documentType: Number(type.value),
          documentNumber: form.documentNumber.trim(),
          complement: type.complement && complement ? complement.toUpperCase() : null,
          name: form.legalName.trim() || null,
        }
      : null,
    customerCode: form.customerCode || null,
    name: form.name.trim() || null,
  };
}

/** Total de la reserva a los precios vigentes (solo informa: el servidor congela los precios al reservar). */
export function cartTotal(lines: readonly CartLine[]): number {
  return lines.reduce((sum, line) => sum + line.price * (line.quantity ?? 0), 0);
}

/** Opciones de clientes registrados (activos, sin el consumidor final «CF»), por nombre. */
export function customerOptions(customers: readonly CustomerRecord[]): { value: string; label: string; description: string; data: CustomerRecord }[] {
  return customers
    .filter((customer) => customer.isActive && customer.code !== 'CF')
    .map((customer) => ({
      value: customer.code,
      label: customer.name,
      description: [customer.code, customer.taxId ? `NIT/CI ${customer.taxId}` : null, customer.phone].filter(Boolean).join(' · '),
      data: customer,
    }))
    .sort((a, b) => a.label.localeCompare(b.label, 'es'));
}
