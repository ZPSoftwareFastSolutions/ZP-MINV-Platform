// Formato del panel en español de Bolivia con la hora de La Paz (UTC−4 todo el año), rangos de fechas con atajos y
// lectura de números escritos a mano. Funciones puras: sin red ni almacenamiento.

import { describe, expect, it } from 'vitest';
import { DATE_SHORTCUTS, addDays, dayEnd, dayStart, inRange, isIsoDate, matchingShortcut, rangeError, shortcutRange } from './dates';
import { EMPTY_VALUE, formatDate, formatDateLong, formatDateTime, formatMoney, formatQuantity, formatTime, laPazToday, toDate, toIsoDate } from './format';
import { parseLocaleNumber, roundTo } from './numbers';

describe('formato de moneda y cantidades', () => {
  it('muestra bolivianos como «Bs 1.234,50»', () => {
    expect(formatMoney(1234.5)).toBe('Bs 1.234,50');
    expect(formatMoney(1234567.891)).toBe('Bs 1.234.567,89');
    expect(formatMoney(0)).toBe('Bs 0,00');
    expect(formatMoney(-120)).toBe('-Bs 120,00');
    expect(formatMoney(1500, 0)).toBe('Bs 1.500');
  });

  it('un valor vacío o inválido se muestra como «—»', () => {
    expect(formatMoney(null)).toBe(EMPTY_VALUE);
    expect(formatMoney(undefined)).toBe('—');
    expect(formatMoney(Number.NaN)).toBe('—');
    expect(formatQuantity(null)).toBe('—');
    expect(formatDate('')).toBe('—');
    expect(formatDateTime('no es una fecha')).toBe('—');
  });

  it('las cantidades llevan separador de miles, hasta 3 decimales y la unidad opcional', () => {
    expect(formatQuantity(1500)).toBe('1.500');
    expect(formatQuantity(2.5, { unit: 'kg' })).toBe('2,5 kg');
    expect(formatQuantity(0.1234)).toBe('0,123');
    expect(formatQuantity(12, { unit: 'u.' })).toBe('12 u.');
  });
});

describe('fechas y horas de La Paz', () => {
  it('una hora UTC de madrugada todavía es el día anterior en La Paz', () => {
    expect(formatDate('2026-09-29T01:30:00Z')).toBe('28/09/2026');
    expect(formatDateTime('2026-09-29T01:30:00Z')).toBe('28/09/2026 21:30');
    expect(formatTime('2026-09-29T13:05:00Z')).toBe('09:05');
    expect(toIsoDate('2026-09-29T03:59:59Z')).toBe('2026-09-28');
    expect(toIsoDate('2026-09-29T04:00:00Z')).toBe('2026-09-29');
  });

  it('una fecha SIN hora no se corre de día', () => {
    expect(formatDate('2026-09-28')).toBe('28/09/2026');
    expect(toIsoDate('2026-01-01')).toBe('2026-01-01');
    expect(toDate('   ')).toBeNull();
  });

  it('acepta Date y milisegundos, y escribe la fecha larga en español', () => {
    expect(formatDate(new Date('2026-12-31T23:00:00-04:00'))).toBe('31/12/2026');
    expect(formatDate(Date.UTC(2026, 0, 1, 12))).toBe('01/01/2026');
    expect(formatDateLong('2026-09-28')).toBe('28 de septiembre de 2026');
    expect(laPazToday(new Date('2026-09-29T02:00:00Z'))).toBe('2026-09-28');
  });
});

describe('rangos de fechas con atajos', () => {
  // 28/09/2026 a las 21:30 en La Paz (29/09 a las 01:30 en UTC).
  const now = new Date('2026-09-29T01:30:00Z');

  it('calcula cada atajo con el día de La Paz', () => {
    expect(shortcutRange('hoy', now)).toEqual({ from: '2026-09-28', to: '2026-09-28' });
    expect(shortcutRange('ayer', now)).toEqual({ from: '2026-09-27', to: '2026-09-27' });
    expect(shortcutRange('ultimos7', now)).toEqual({ from: '2026-09-22', to: '2026-09-28' });
    expect(shortcutRange('esteMes', now)).toEqual({ from: '2026-09-01', to: '2026-09-28' });
    expect(shortcutRange('mesAnterior', now)).toEqual({ from: '2026-08-01', to: '2026-08-31' });
    expect(DATE_SHORTCUTS.map((shortcut) => shortcut.label)).toEqual(['Hoy', 'Ayer', 'Últimos 7 días', 'Este mes', 'Mes anterior']);
  });

  it('cruza el cambio de año y los años bisiestos', () => {
    const newYear = new Date('2026-01-01T02:00:00Z'); // 31/12/2025 en La Paz
    expect(shortcutRange('hoy', newYear)).toEqual({ from: '2025-12-31', to: '2025-12-31' });
    expect(shortcutRange('mesAnterior', newYear)).toEqual({ from: '2025-11-01', to: '2025-11-30' });
    expect(shortcutRange('mesAnterior', new Date('2028-03-10T15:00:00Z'))).toEqual({ from: '2028-02-01', to: '2028-02-29' });
    expect(addDays('2028-03-01', -1)).toBe('2028-02-29');
  });

  it('reconoce qué atajo coincide con un rango', () => {
    expect(matchingShortcut({ from: '2026-09-01', to: '2026-09-28' }, now)).toBe('esteMes');
    expect(matchingShortcut({ from: '2026-09-02', to: '2026-09-28' }, now)).toBeNull();
    expect(matchingShortcut({ from: null, to: null }, now)).toBeNull();
  });

  it('valida el rango y filtra por días de La Paz con los extremos incluidos', () => {
    expect(rangeError({ from: '2026-09-10', to: '2026-09-01' })).toMatch(/posterior/);
    expect(rangeError({ from: '2026-02-30', to: null })).toMatch(/válida/);
    expect(rangeError({ from: '2026-09-01', to: '2026-09-30' })).toBeNull();
    expect(isIsoDate('2026-02-28')).toBe(true);
    expect(isIsoDate('2026-2-28')).toBe(false);
    const range = { from: '2026-09-01', to: '2026-09-28' };
    expect(inRange('2026-09-29T03:00:00Z', range)).toBe(true); // 28/09 a las 23:00 en La Paz
    expect(inRange('2026-09-29T04:00:00Z', range)).toBe(false);
    expect(inRange('2026-09-01T04:00:00Z', range)).toBe(true);
    expect(inRange(null, range)).toBe(false);
    expect(inRange(null, { from: null, to: null })).toBe(true);
    expect(dayStart('2026-09-28').toISOString()).toBe('2026-09-28T04:00:00.000Z');
    expect(dayEnd('2026-09-28').toISOString()).toBe('2026-09-29T03:59:59.999Z');
  });
});

describe('números escritos a mano', () => {
  it('lee el formato de Bolivia y el punto decimal', () => {
    expect(parseLocaleNumber('1.234,5')).toBe(1234.5);
    expect(parseLocaleNumber('1234.5')).toBe(1234.5);
    expect(parseLocaleNumber('1234,50')).toBe(1234.5);
    expect(parseLocaleNumber('1.234.567,89')).toBe(1234567.89);
    expect(parseLocaleNumber('1,234.56')).toBe(1234.56);
    expect(parseLocaleNumber('1.500')).toBe(1500);
    expect(parseLocaleNumber('0.125')).toBe(0.125);
    expect(parseLocaleNumber('Bs 1.500,00')).toBe(1500);
    expect(parseLocaleNumber('-12,5')).toBe(-12.5);
  });

  it('vacío es null y lo que no es un número es NaN', () => {
    expect(parseLocaleNumber('')).toBeNull();
    expect(parseLocaleNumber('   ')).toBeNull();
    expect(parseLocaleNumber('abc')).toBeNaN();
    expect(parseLocaleNumber('12,5.3')).toBeNaN();
    expect(parseLocaleNumber('1.2.3')).toBeNaN();
    expect(parseLocaleNumber(',')).toBeNaN();
  });

  it('redondea mitad hacia arriba sin errores del binario', () => {
    expect(roundTo(1.005, 2)).toBe(1.01);
    expect(roundTo(2.345, 2)).toBe(2.35);
    expect(roundTo(-2.345, 2)).toBe(-2.35);
    expect(roundTo(10, 0)).toBe(10);
    expect(roundTo(1.23, 2)).toBe(1.23);
  });
});
