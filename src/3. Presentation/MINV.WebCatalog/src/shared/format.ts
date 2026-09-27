// Formato de números y dinero para Bolivia (es-BO): «Bs 2.049,00», «1.500», «11 %». Funciones puras, sin React.

const LOCALE = 'es-BO';
const cache = new Map<string, Intl.NumberFormat>();

function numberFormat(minDecimals: number, maxDecimals: number): Intl.NumberFormat {
  const key = `${minDecimals}:${maxDecimals}`;
  let format = cache.get(key);
  if (!format) {
    format = new Intl.NumberFormat(LOCALE, {
      minimumFractionDigits: minDecimals,
      maximumFractionDigits: maxDecimals,
      useGrouping: true,
    });
    cache.set(key, format);
  }
  return format;
}

export interface NumberFormatOptions {
  /** Decimales fijos (mínimo y máximo). */
  decimals?: number;
  /** Máximo de decimales cuando no se quieren fijos. */
  maxDecimals?: number;
}

/** «2.049,5» · «1.500» · «0,75». Separador de miles «.» y decimal «,». */
export function formatNumber(value: number, options: NumberFormatOptions = {}): string {
  if (!Number.isFinite(value)) return '—';
  const min = options.decimals ?? 0;
  const max = options.decimals ?? options.maxDecimals ?? 2;
  return numberFormat(min, Math.max(min, max)).format(value);
}

export interface MoneyFormatOptions {
  /** Decimales a mostrar (2 por defecto: «Bs 2.049,00»). */
  decimals?: number;
  /** Símbolo de la moneda («Bs»). */
  symbol?: string;
}

/** «Bs 2.049,00». Negativos: «-Bs 120,00». */
export function formatMoney(value: number, options: MoneyFormatOptions = {}): string {
  if (!Number.isFinite(value)) return '—';
  const symbol = options.symbol ?? 'Bs';
  const decimals = options.decimals ?? 2;
  const sign = value < 0 ? '-' : '';
  return `${sign}${symbol} ${formatNumber(Math.abs(value), { decimals })}`;
}

/** «11 %» (en español el porcentaje lleva un espacio). `value` es el porcentaje ya calculado (11 → «11 %»). */
export function formatPercent(value: number, decimals = 0): string {
  if (!Number.isFinite(value)) return '—';
  return `${formatNumber(value, { decimals })} %`;
}

/** «1 pieza» · «3 piezas». */
export function pluralize(count: number, singular: string, plural: string): string {
  return `${formatNumber(count)} ${count === 1 ? singular : plural}`;
}
