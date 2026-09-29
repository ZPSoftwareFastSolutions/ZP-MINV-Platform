// Formato del panel en español de Bolivia (es-BO) y con la hora de La Paz (America/La_Paz, UTC−4 todo el año, sin
// horario de verano). Funciones puras: las usan las tablas, las tarjetas, los detalles y la exportación a CSV.
//
//   formatMoney(1234.5)                       → 'Bs 1.234,50'
//   formatQuantity(1500)                      → '1.500'        formatQuantity(2.5, { unit: 'kg' }) → '2,5 kg'
//   formatDate('2026-09-29T01:30:00Z')        → '28/09/2026'   (en La Paz todavía es el 28)
//   formatDate('2026-09-28')                  → '28/09/2026'   (una fecha SIN hora nunca se corre de día)
//   formatDateTime('2026-09-29T01:30:00Z')    → '28/09/2026 21:30'
//   formatTime(new Date())                    → '21:54'
//   formatDateLong('2026-09-28')              → '28 de septiembre de 2026'
//
// Un valor vacío o inválido (null, undefined, '', NaN, una fecha mal escrita) se muestra como «—».

import { formatMoney as formatBolivianos, formatNumber, formatPercent } from '@/shared/format';

export { formatNumber, formatPercent };

export const PANEL_LOCALE = 'es-BO';
export const PANEL_TIME_ZONE = 'America/La_Paz';
/** Desfase de La Paz respecto de UTC (fijo: Bolivia no usa horario de verano). */
export const LA_PAZ_OFFSET = '-04:00';
/** Lo que se muestra cuando no hay valor. */
export const EMPTY_VALUE = '—';

/** Fecha u hora tal como llega del servidor (ISO 8601), un `Date`, milisegundos o nada. */
export type DateInput = Date | string | number | null | undefined;

const DATE_ONLY = /^(\d{4})-(\d{2})-(\d{2})$/;

/** Dinero en bolivianos: «Bs 1.234,50» (negativos «-Bs 120,00»). */
export function formatMoney(value: number | null | undefined, decimals = 2): string {
  if (value == null || !Number.isFinite(value)) return EMPTY_VALUE;
  return formatBolivianos(value, { decimals });
}

export interface QuantityFormatOptions {
  /** Unidad después del número («u.», «kg»). */
  unit?: string;
  /** Máximo de decimales (3 por defecto: las cantidades del inventario casi siempre son enteras). */
  maxDecimals?: number;
}

/** Cantidad: «1.500», «2,5», «12 u.». */
export function formatQuantity(value: number | null | undefined, options: QuantityFormatOptions = {}): string {
  if (value == null || !Number.isFinite(value)) return EMPTY_VALUE;
  const text = formatNumber(value, { maxDecimals: options.maxDecimals ?? 3 });
  return options.unit ? `${text} ${options.unit}` : text;
}

/**
 * Convierte lo que llega del servidor en un `Date` (o null si no sirve). Una fecha sin hora («2026-09-28», un DateOnly
 * del servidor) se toma al mediodía de La Paz: así cualquier formato la muestra en ese mismo día.
 */
export function toDate(value: DateInput): Date | null {
  if (value == null) return null;
  if (value instanceof Date) return Number.isNaN(value.getTime()) ? null : value;
  if (typeof value === 'number') {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? null : date;
  }
  const text = value.trim();
  if (text.length === 0) return null;
  const date = DATE_ONLY.test(text) ? new Date(`${text}T12:00:00${LA_PAZ_OFFSET}`) : new Date(text);
  return Number.isNaN(date.getTime()) ? null : date;
}

const PARTS = new Intl.DateTimeFormat('en-US', {
  timeZone: PANEL_TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hourCycle: 'h23',
});

interface LaPazParts {
  year: string;
  month: string;
  day: string;
  hour: string;
  minute: string;
}

/** Día y hora de ese instante en La Paz (año, mes, día, hora y minuto con dos cifras). */
function laPazParts(date: Date): LaPazParts {
  const parts = PARTS.formatToParts(date);
  const get = (type: Intl.DateTimeFormatPartTypes) => parts.find((part) => part.type === type)?.value ?? '00';
  const hour = get('hour');
  return { year: get('year'), month: get('month'), day: get('day'), hour: hour === '24' ? '00' : hour, minute: get('minute') };
}

/** «28/09/2026». */
export function formatDate(value: DateInput): string {
  const date = toDate(value);
  if (!date) return EMPTY_VALUE;
  const { day, month, year } = laPazParts(date);
  return `${day}/${month}/${year}`;
}

/** «21:54» (24 horas). */
export function formatTime(value: DateInput): string {
  const date = toDate(value);
  if (!date) return EMPTY_VALUE;
  const { hour, minute } = laPazParts(date);
  return `${hour}:${minute}`;
}

/** «28/09/2026 21:54». */
export function formatDateTime(value: DateInput): string {
  const date = toDate(value);
  if (!date) return EMPTY_VALUE;
  const { day, month, year, hour, minute } = laPazParts(date);
  return `${day}/${month}/${year} ${hour}:${minute}`;
}

const LONG_DATE = new Intl.DateTimeFormat(PANEL_LOCALE, { timeZone: PANEL_TIME_ZONE, day: 'numeric', month: 'long', year: 'numeric' });

/** «28 de septiembre de 2026». */
export function formatDateLong(value: DateInput): string {
  const date = toDate(value);
  return date ? LONG_DATE.format(date) : EMPTY_VALUE;
}

/** Fecha del calendario de La Paz en formato ISO («2026-09-28»): lo que usan los filtros de fechas. */
export function toIsoDate(value: DateInput): string | null {
  const date = toDate(value);
  if (!date) return null;
  const { year, month, day } = laPazParts(date);
  return `${year}-${month}-${day}`;
}

/** Hoy en La Paz («2026-09-28»). */
export function laPazToday(now: Date = new Date()): string {
  return toIsoDate(now)!;
}
