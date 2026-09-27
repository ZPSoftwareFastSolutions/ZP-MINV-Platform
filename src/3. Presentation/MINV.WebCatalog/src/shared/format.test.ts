import { describe, expect, it } from 'vitest';
import { formatMoney, formatNumber, formatPercent, pluralize } from './format';
import { normalizeText, tokenize } from './text';

describe('formato es-BO', () => {
  it('formatea dinero como «Bs 2.049,00» con miles y dos decimales', () => {
    expect(formatMoney(2049)).toBe('Bs 2.049,00');
    expect(formatMoney(36099.5)).toBe('Bs 36.099,50');
    expect(formatMoney(79)).toBe('Bs 79,00');
  });

  it('admite negativos, cero y decimales personalizados', () => {
    expect(formatMoney(-120)).toBe('-Bs 120,00');
    expect(formatMoney(0)).toBe('Bs 0,00');
    expect(formatMoney(1500, { decimals: 0 })).toBe('Bs 1.500');
  });

  it('formatea números y porcentajes con espacio antes del símbolo', () => {
    expect(formatNumber(1500)).toBe('1.500');
    expect(formatNumber(0.756, { decimals: 1 })).toBe('0,8');
    expect(formatPercent(13)).toBe('13 %');
    expect(formatNumber(Number.NaN)).toBe('—');
  });

  it('pluraliza en español', () => {
    expect(pluralize(1, 'pieza', 'piezas')).toBe('1 pieza');
    expect(pluralize(3, 'pieza', 'piezas')).toBe('3 piezas');
  });
});

describe('texto', () => {
  it('normaliza sin acentos ni mayúsculas', () => {
    expect(normalizeText('  Refrigeración   Líquida ')).toBe('refrigeracion liquida');
    expect(tokenize('RTX  5070 Ti')).toEqual(['rtx', '5070', 'ti']);
  });
});
