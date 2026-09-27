import { describe, expect, it } from 'vitest';
import type { Spec } from '@/1-domain/catalog/types';
import { compatibilitySpecs, warrantyLabel } from './productInfo';

function spec(key: string, text: string): Spec {
  return { key, label: key, value: text, text, unit: null, filterable: true };
}

describe('ficha de producto', () => {
  it('extrae solo las especificaciones de compatibilidad y las ordena (socket antes que potencia)', () => {
    const specs = [spec('nucleos', '8'), spec('tdp', '105 W'), spec('condicion', 'Nuevo'), spec('memoria_soportada', 'DDR5'), spec('socket', 'AM5')];
    expect(compatibilitySpecs({ specs }).map((item) => item.key)).toEqual(['socket', 'memoria_soportada', 'tdp']);
  });

  it('devuelve una lista vacía cuando el producto no trae datos de compatibilidad', () => {
    expect(compatibilitySpecs({ specs: [spec('condicion', 'Nuevo'), spec('color', 'Negro')] })).toEqual([]);
  });

  it('enuncia la garantía en meses, en singular y sin garantía', () => {
    expect(warrantyLabel(36)).toBe('36 meses de garantía oficial');
    expect(warrantyLabel(1)).toBe('1 mes de garantía oficial');
    expect(warrantyLabel(0)).toBe('Sin garantía del fabricante');
  });
});
