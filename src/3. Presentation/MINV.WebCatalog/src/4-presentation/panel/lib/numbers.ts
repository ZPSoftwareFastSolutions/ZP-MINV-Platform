// Lectura de números escritos a mano en el formato de Bolivia (o con punto decimal): la usan NumberField y MoneyField.
//
//   parseLocaleNumber('1.234,5')   → 1234.5        parseLocaleNumber('1234.5') → 1234.5
//   parseLocaleNumber('Bs 1.500')  → 1500          parseLocaleNumber('12,5.3') → NaN (no es un número)
//   parseLocaleNumber('')          → null (vacío)

/** Número escrito por la persona: null si está vacío, NaN si no es un número. */
export function parseLocaleNumber(text: string): number | null {
  let clean = text.replace(/\s| /g, '').replace(/^bs\.?/i, '');
  if (clean.length === 0) return null;
  const negative = clean.startsWith('-');
  if (negative || clean.startsWith('+')) clean = clean.slice(1);
  if (!/^[\d.,]+$/.test(clean) || !/\d/.test(clean)) return Number.NaN;

  const lastComma = clean.lastIndexOf(',');
  const lastDot = clean.lastIndexOf('.');
  let normalized: string;
  if (lastComma >= 0 && lastDot >= 0) {
    // Los dos separadores: el ÚLTIMO es el decimal y el otro agrupa miles.
    const decimal = lastComma > lastDot ? ',' : '.';
    const group = decimal === ',' ? '.' : ',';
    const [whole, fraction, ...rest] = clean.split(decimal);
    if (rest.length > 0 || fraction === undefined || fraction.includes(group) || !isGrouped(whole, group)) return Number.NaN;
    normalized = `${whole.split(group).join('')}.${fraction}`;
  } else if (lastComma >= 0) {
    // Solo comas: una sola es decimal («12,5»); varias con grupos de tres son miles («1,234,567»).
    const parts = clean.split(',');
    if (parts.length === 2) normalized = `${parts[0]}.${parts[1]}`;
    else if (isGrouped(clean, ',')) normalized = parts.join('');
    else return Number.NaN;
  } else if (lastDot >= 0) {
    // Solo puntos: con grupos exactos de tres son miles («1.234», «1.234.567»); un solo punto suelto es decimal («12.5»).
    const parts = clean.split('.');
    if (isGrouped(clean, '.') && parts.length >= 2 && parts[parts.length - 1].length === 3) normalized = parts.join('');
    else if (parts.length === 2) normalized = `${parts[0]}.${parts[1]}`;
    else return Number.NaN;
  } else {
    normalized = clean;
  }
  if (!/^\d*\.?\d*$/.test(normalized) || normalized === '.') return Number.NaN;
  const value = Number(normalized);
  return negative ? -value : value;
}

/** ¿El texto es un entero con grupos de tres cifras separados por `group` («1.234.567»; «0.125» no lo es)? */
function isGrouped(text: string, group: string): boolean {
  const parts = text.split(group);
  if (parts.length === 1) return /^\d+$/.test(text);
  return /^[1-9]\d{0,2}$/.test(parts[0]) && parts.slice(1).every((part) => /^\d{3}$/.test(part));
}

/**
 * Redondea a `decimals` cifras, la mitad hacia arriba (en valor absoluto), sin los errores del binario: 1,005 → 1,01 y
 * 2,345 → 2,35 (multiplicar por 100 daría 234,4999…). Corre la coma en el texto del número en vez de multiplicar.
 */
export function roundTo(value: number, decimals: number): number {
  if (!Number.isFinite(value)) return value;
  const places = Math.max(0, Math.trunc(decimals));
  const sign = value < 0 ? -1 : 1;
  const absolute = Math.abs(value);
  if (String(absolute).includes('e')) return (sign * Math.round(absolute * 10 ** places)) / 10 ** places;
  const shifted = Math.round(Number(`${absolute}e${places}`));
  return sign * Number(`${shifted}e-${places}`);
}
