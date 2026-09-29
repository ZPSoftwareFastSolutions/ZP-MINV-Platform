// Funciones puras del módulo «Alertas»: tipo y acción sugerida, reposición, filtros, opciones, textos, enlaces y CSV.

import { describe, expect, it } from 'vitest';
import { buildCsv } from '@/4-presentation/panel/lib';
import {
  ALERT_CSV,
  ALERT_FILTERS,
  activeWarehouseLabel,
  adjustmentLink,
  alertCounts,
  alertTypeLabel,
  alertTypeOrder,
  categoryOptions,
  countText,
  entryLink,
  filterAlerts,
  isReplenishable,
  lastMovementText,
  orderLink,
  productCardLink,
  suggestedAction,
  supplierOptions,
  warehouseOptions,
  type AlertRecord,
  type BranchRecord,
} from './alerts';

function alert(position: number, status: string, sku: string, patch: Partial<AlertRecord> = {}): AlertRecord {
  return { position, status: status as AlertRecord['status'], sku, name: `Producto ${sku}`, category: 'Periféricos', supplier: 'Distribuidora Andina', stock: 1, minimum: 5, maximum: 20, unit: 'u.', shortfall: 4, suggestedQuantity: 19, lastMovement: '2026-09-20', ...patch };
}

const ALERTS = [
  alert(1, 'OutOfStock', 'TEC-01', { name: 'Teclado mecánico', category: 'Teclados', stock: 0 }),
  alert(2, 'Critical', 'MOU-01', { name: 'Mouse gamer' }),
  alert(3, 'Low', 'AUD-01', { name: 'Audífonos', supplier: '  ', lastMovement: null }),
  alert(4, 'Overstock', 'SSD-01', { name: 'SSD 1 TB', category: 'Almacenamiento', supplier: 'Importadora Sur', suggestedQuantity: 0, shortfall: 0 }),
];

describe('Alertas · tipos y acciones', () => {
  it('tipo en palabras, orden de urgencia y acción sugerida de la V2.1', () => {
    expect(ALERTS.map((item) => alertTypeLabel(item.status))).toEqual(['Sin stock', 'Crítico', 'Bajo', 'Exceso']);
    expect(alertTypeOrder('Inconsistent')).toBeLessThan(alertTypeOrder('OutOfStock'));
    expect(alertTypeOrder('Optimal')).toBe(99);
    expect(suggestedAction('OutOfStock')).toBe('Reabastecer de inmediato');
    expect(suggestedAction('Critical')).toBe('Emitir una orden de compra');
    expect(suggestedAction('Optimal')).toBe('Revisar');
    expect(['OutOfStock', 'Critical', 'Low', 'Overstock', 'Inconsistent'].map(isReplenishable)).toEqual([true, true, true, false, false]);
  });

  it('cuenta por tipo en el orden de prioridad, sin los tipos vacíos', () => {
    const counts = alertCounts(ALERTS);
    expect(counts.total).toBe(4);
    expect(counts.byType.map((item) => `${item.label} ${item.count}`)).toEqual(['Sin stock 1', 'Crítico 1', 'Bajo 1', 'Exceso 1']);
    expect(countText(1, 1)).toBe('1 de 1 alerta');
    expect(countText(2, 1500)).toBe('2 de 1.500 alertas');
  });
});

describe('Alertas · filtros y opciones', () => {
  it('filtra por tipo, categoría, proveedor («Sin proveedor») y búsqueda sin acentos (también por el tipo)', () => {
    const skus = (filters: Partial<typeof ALERT_FILTERS>) => filterAlerts(ALERTS, { ...ALERT_FILTERS, ...filters }).map((item) => item.sku);
    expect(skus({})).toEqual(['TEC-01', 'MOU-01', 'AUD-01', 'SSD-01']);
    expect(skus({ tipo: 'Critical' })).toEqual(['MOU-01']);
    expect(skus({ categoria: 'Almacenamiento' })).toEqual(['SSD-01']);
    expect(skus({ proveedor: 'Sin proveedor' })).toEqual(['AUD-01']);
    expect(skus({ q: 'teclado mecanico' })).toEqual(['TEC-01']);
    expect(skus({ q: 'exceso' })).toEqual(['SSD-01']);
  });

  it('opciones de categoría, proveedor y sucursales visibles', () => {
    expect(categoryOptions(ALERTS).map((option) => option.value)).toEqual(['Almacenamiento', 'Periféricos', 'Teclados']);
    expect(supplierOptions(ALERTS).map((option) => option.value)).toEqual(['Distribuidora Andina', 'Importadora Sur', 'Sin proveedor']);
    const branch = (code: string, isVisible = true): BranchRecord => ({ id: code, code, name: `Sucursal ${code}`, isActive: true, isVisible, warehouses: [`${code}-01`], users: 1, stockValue: null, transfersIn: 0, transfersOut: 0 });
    expect(warehouseOptions([branch('CM'), branch('SC', false)])).toEqual([{ value: 'CM-01', label: 'CM · Sucursal CM' }]);
    expect(activeWarehouseLabel({ code: 'CB' })).toBe('Sucursal activa (CB)');
    expect(activeWarehouseLabel(null)).toBe('Almacén principal de la empresa');
  });
});

describe('Alertas · textos, enlaces y CSV', () => {
  it('último movimiento y enlaces a los otros módulos', () => {
    expect(lastMovementText(ALERTS[0])).toBe('Último movimiento el 20/09/2026');
    expect(lastMovementText(ALERTS[2])).toBe('Sin movimientos');
    expect(productCardLink('MOU 01')).toBe('/panel/stock?ficha=MOU%2001');
    expect(entryLink('MOU-01')).toBe('/panel/movimientos?registrar=ENTRADA&sku=MOU-01');
    expect(adjustmentLink('SSD-01')).toBe('/panel/movimientos?registrar=ajuste&sku=SSD-01');
    expect(orderLink('MOU-01')).toBe('/panel/pedido?q=MOU-01');
    expect(orderLink()).toBe('/panel/pedido');
  });

  it('el CSV lleva el tipo en palabras, «Sin proveedor» y la acción sugerida', () => {
    const lines = buildCsv(ALERT_CSV, [ALERTS[2]], { bom: false }).trim().split('\r\n');
    expect(lines[0]).toBe('"Prioridad";"Tipo";"SKU";"Producto";"Categoría";"Proveedor";"Unidad";"Existencias";"Mínimo";"Máximo";"Faltan";"Sugerido";"Acción sugerida";"Último movimiento"');
    expect(lines[1]).toBe('3;"Bajo";"AUD-01";"Audífonos";"Periféricos";"Sin proveedor";"u.";1;5;20;4;19;"Programar la reposición";');
  });
});
