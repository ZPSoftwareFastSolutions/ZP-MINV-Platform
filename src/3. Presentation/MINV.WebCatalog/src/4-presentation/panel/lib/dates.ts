// Rangos de fechas de los filtros del panel. Un rango son dos días del calendario de La Paz en formato ISO
// («2026-09-28»), AMBOS incluidos; cualquiera de los dos puede faltar («desde el 1» o «hasta el 15»). Los atajos (hoy,
// ayer, últimos 7 días, este mes y mes anterior) se calculan con la fecha de La Paz, no con la del equipo.
//
//   shortcutRange('esteMes', ahora)       → { from: '2026-09-01', to: '2026-09-28' }
//   matchingShortcut(rango, ahora)        → 'esteMes' | null  (qué atajo coincide, para marcar su botón)
//   inRange(venta.fecha, rango)           → filtrar en la página
//   dayStart('2026-09-28')                → Date de las 00:00 de La Paz (para enviar al servidor)
//   dayEnd('2026-09-28')                  → Date de las 23:59:59.999 de La Paz

import { LA_PAZ_OFFSET, laPazToday, toIsoDate, type DateInput } from './format';

export interface DateRange {
  /** Primer día incluido («2026-09-01») o null (sin límite). */
  from: string | null;
  /** Último día incluido («2026-09-28») o null (sin límite). */
  to: string | null;
}

export const EMPTY_RANGE: DateRange = { from: null, to: null };

export type DateShortcutId = 'hoy' | 'ayer' | 'ultimos7' | 'esteMes' | 'mesAnterior';

/** Atajos de fechas, en el orden en que se muestran. */
export const DATE_SHORTCUTS: readonly { id: DateShortcutId; label: string }[] = [
  { id: 'hoy', label: 'Hoy' },
  { id: 'ayer', label: 'Ayer' },
  { id: 'ultimos7', label: 'Últimos 7 días' },
  { id: 'esteMes', label: 'Este mes' },
  { id: 'mesAnterior', label: 'Mes anterior' },
];

const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;

/** ¿Es un día válido en formato ISO («2026-02-30» no lo es)? */
export function isIsoDate(text: string | null | undefined): text is string {
  if (!text) return false;
  const match = ISO_DATE.exec(text);
  if (!match) return false;
  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const date = new Date(Date.UTC(year, month - 1, day));
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
}

function fromUtc(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function utcOf(iso: string): Date {
  const [year, month, day] = iso.split('-').map(Number);
  return new Date(Date.UTC(year, month - 1, day));
}

/** Suma (o resta) días a una fecha ISO: `addDays('2026-03-01', -1)` → '2026-02-28'. */
export function addDays(iso: string, days: number): string {
  const date = utcOf(iso);
  date.setUTCDate(date.getUTCDate() + days);
  return fromUtc(date);
}

/** Primer día del mes de una fecha ISO. */
export function monthStart(iso: string): string {
  return `${iso.slice(0, 7)}-01`;
}

/** El rango de un atajo, con la fecha de La Paz de `now`. */
export function shortcutRange(id: DateShortcutId, now: Date = new Date()): DateRange {
  const today = laPazToday(now);
  switch (id) {
    case 'hoy':
      return { from: today, to: today };
    case 'ayer': {
      const yesterday = addDays(today, -1);
      return { from: yesterday, to: yesterday };
    }
    case 'ultimos7':
      return { from: addDays(today, -6), to: today };
    case 'esteMes':
      return { from: monthStart(today), to: today };
    case 'mesAnterior': {
      const lastOfPrevious = addDays(monthStart(today), -1);
      return { from: monthStart(lastOfPrevious), to: lastOfPrevious };
    }
  }
}

/** ¿Qué atajo coincide exactamente con el rango? (null si ninguno). */
export function matchingShortcut(range: DateRange, now: Date = new Date()): DateShortcutId | null {
  if (!range.from || !range.to) return null;
  const found = DATE_SHORTCUTS.find(({ id }) => {
    const candidate = shortcutRange(id, now);
    return candidate.from === range.from && candidate.to === range.to;
  });
  return found?.id ?? null;
}

/** ¿El rango tiene alguna fecha? */
export function isRangeActive(range: DateRange): boolean {
  return Boolean(range.from || range.to);
}

/** Mensaje si el rango no sirve (fecha mal escrita o «desde» después de «hasta»); null si está bien. */
export function rangeError(range: DateRange): string | null {
  if ((range.from && !isIsoDate(range.from)) || (range.to && !isIsoDate(range.to))) return 'Escriba una fecha válida.';
  if (range.from && range.to && range.from > range.to) return 'La fecha «Desde» no puede ser posterior a «Hasta».';
  return null;
}

/** 00:00 de ese día en La Paz. */
export function dayStart(iso: string): Date {
  return new Date(`${iso}T00:00:00.000${LA_PAZ_OFFSET}`);
}

/** 23:59:59.999 de ese día en La Paz. */
export function dayEnd(iso: string): Date {
  return new Date(`${iso}T23:59:59.999${LA_PAZ_OFFSET}`);
}

/** ¿La fecha u hora cae dentro del rango (días de La Paz, ambos incluidos)? Sin fecha, solo si el rango está vacío. */
export function inRange(value: DateInput, range: DateRange): boolean {
  if (!isRangeActive(range)) return true;
  const day = toIsoDate(value);
  if (!day) return false;
  if (range.from && day < range.from) return false;
  if (range.to && day > range.to) return false;
  return true;
}
