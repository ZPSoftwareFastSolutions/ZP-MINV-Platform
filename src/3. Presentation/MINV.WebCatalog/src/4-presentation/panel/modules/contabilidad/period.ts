// Módulo «Contabilidad» · el PERÍODO del estado de resultados, del libro diario y de los saldos (funciones puras). Es
// copia de la del módulo «Reportes» (un módulo no importa a otro, regla P-09), con «Este mes» por defecto, como el
// escritorio. Toda consulta del período pide un rango de días
// (`from` y `to`, ambos incluidos, días de La Paz): la pantalla ofrece atajos en una lista desplegable («Últimos 30 días»,
// «Este mes», «Mes anterior»…) y, si se eligen fechas a mano, el período es «Personalizado». En la dirección:
// `?periodo=esteMes` o `?desde=2026-09-01&hasta=2026-09-15` (con fechas a mano, `periodo` no se escribe).

import { addDays, isIsoDate, laPazToday, monthStart, formatDate } from '@/4-presentation/panel/lib';

export type PeriodId = 'hoy' | 'ayer' | 'ultimos7' | 'ultimos30' | 'esteMes' | 'mesAnterior' | 'ultimos90' | 'esteAnio' | 'ultimos365';

/** Valor de la lista para las fechas elegidas a mano. */
export const CUSTOM_PERIOD = 'personalizado';

/** Atajos, en el orden en que se ofrecen. */
export const PERIOD_OPTIONS: readonly { value: PeriodId; label: string }[] = [
  { value: 'hoy', label: 'Hoy' },
  { value: 'ayer', label: 'Ayer' },
  { value: 'ultimos7', label: 'Últimos 7 días' },
  { value: 'ultimos30', label: 'Últimos 30 días' },
  { value: 'esteMes', label: 'Este mes' },
  { value: 'mesAnterior', label: 'Mes anterior' },
  { value: 'ultimos90', label: 'Últimos 90 días' },
  { value: 'esteAnio', label: 'Este año' },
  { value: 'ultimos365', label: 'Últimos 365 días' },
];

/** Opciones de la lista «Período» (los atajos y «Personalizado»). */
export const PERIOD_SELECT_OPTIONS: readonly { value: string; label: string }[] = [...PERIOD_OPTIONS, { value: CUSTOM_PERIOD, label: 'Personalizado (elija las fechas)' }];

/** Período con que se abre la contabilidad (el mismo del escritorio: «Este mes»). */
export const DEFAULT_PERIOD: PeriodId = 'esteMes';

/** Un período abarca como máximo un año (el servidor arma la serie día por día). */
export const MAX_PERIOD_DAYS = 366;

/** Filtros del período en la dirección (se suman a los de cada reporte). */
export const PERIOD_FILTERS = { periodo: DEFAULT_PERIOD as string, desde: '', hasta: '' };
export type PeriodFilters = typeof PERIOD_FILTERS;

export function isPeriodId(value: string): value is PeriodId {
  return PERIOD_OPTIONS.some((option) => option.value === value);
}

/** Días de un atajo, con la fecha de La Paz de `now`. */
export function periodRange(id: PeriodId, now: Date = new Date()): { from: string; to: string } {
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
    case 'ultimos30':
      return { from: addDays(today, -29), to: today };
    case 'esteMes':
      return { from: monthStart(today), to: today };
    case 'mesAnterior': {
      const last = addDays(monthStart(today), -1);
      return { from: monthStart(last), to: last };
    }
    case 'ultimos90':
      return { from: addDays(today, -89), to: today };
    case 'esteAnio':
      return { from: `${today.slice(0, 4)}-01-01`, to: today };
    case 'ultimos365':
      return { from: addDays(today, -364), to: today };
  }
}

/** Días entre dos fechas ISO, ambas incluidas. */
export function daysBetween(from: string, to: string): number {
  const start = Date.UTC(Number(from.slice(0, 4)), Number(from.slice(5, 7)) - 1, Number(from.slice(8, 10)));
  const end = Date.UTC(Number(to.slice(0, 4)), Number(to.slice(5, 7)) - 1, Number(to.slice(8, 10)));
  return Math.round((end - start) / 86_400_000) + 1;
}

export interface ResolvedPeriod {
  /** El atajo elegido o «personalizado». */
  id: PeriodId | typeof CUSTOM_PERIOD;
  from: string;
  to: string;
  /** Qué está mal (fecha inválida, al revés o más de un año); null si se puede pedir el reporte. */
  problem: string | null;
}

/**
 * El período que piden los filtros: con alguna fecha escrita a mano es «personalizado» (una fecha sola completa la otra:
 * «desde» sin «hasta» llega hasta hoy; «hasta» sola es ese día); si no, el atajo (uno desconocido vuelve al de siempre).
 */
export function resolvePeriod(filters: PeriodFilters, now: Date = new Date(), fallback: PeriodId = DEFAULT_PERIOD): ResolvedPeriod {
  if (filters.desde || filters.hasta) {
    const from = filters.desde || filters.hasta;
    const to = filters.hasta || laPazToday(now);
    let problem: string | null = null;
    if (!isIsoDate(from) || !isIsoDate(to)) problem = 'Escriba una fecha válida.';
    else if (from > to) problem = 'La fecha «Desde» no puede ser posterior a «Hasta».';
    else if (daysBetween(from, to) > MAX_PERIOD_DAYS) problem = 'Elija un período de hasta un año (366 días).';
    return { id: CUSTOM_PERIOD, from, to, problem };
  }
  const id = isPeriodId(filters.periodo) ? filters.periodo : fallback;
  return { id, ...periodRange(id, now), problem: null };
}

/** Nombre del período: «Últimos 30 días» o «Personalizado». */
export function periodName(period: ResolvedPeriod): string {
  return PERIOD_OPTIONS.find((option) => option.value === period.id)?.label ?? 'Personalizado';
}

/** «del 31/08/2026 al 29/09/2026» (o «el 29/09/2026» si es un solo día). */
export function rangeText(period: Pick<ResolvedPeriod, 'from' | 'to'>): string {
  return period.from === period.to ? `el ${formatDate(period.from)}` : `del ${formatDate(period.from)} al ${formatDate(period.to)}`;
}

/** Filtros al elegir un atajo de la lista (o «Personalizado»: deja escritas las fechas que se estaban viendo). */
export function filtersForPeriodChoice(value: string, current: ResolvedPeriod): Partial<PeriodFilters> {
  if (value === CUSTOM_PERIOD) return { periodo: DEFAULT_PERIOD, desde: current.from, hasta: current.to };
  return { periodo: isPeriodId(value) ? value : DEFAULT_PERIOD, desde: '', hasta: '' };
}

/** Filtros al escribir las fechas a mano (el atajo vuelve al de siempre: así el período cuenta como UN filtro). */
export function filtersForDates(range: { from: string | null; to: string | null }): Partial<PeriodFilters> {
  return { periodo: DEFAULT_PERIOD, desde: range.from ?? '', hasta: range.to ?? '' };
}
