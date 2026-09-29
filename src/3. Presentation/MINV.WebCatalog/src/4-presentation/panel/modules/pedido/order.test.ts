// Funciones puras del módulo «Pedido sugerido»: cantidades revisadas, subtotales, grupos por proveedor (código y órdenes
// abiertas), por qué no se puede crear una orden, contenido de `CreatePurchaseOrderCommand`, texto para copiar y CSV.

import { describe, expect, it } from 'vitest';
import { buildCsv } from '@/4-presentation/panel/lib';
import {
  NO_SUPPLIER,
  ORDER_CSV,
  ORDER_FILTERS,
  busySuppliers,
  cannotOrderReason,
  categoryOptions,
  deliveryText,
  filterOrder,
  orderText,
  purchaseOrderPayload,
  purchaseOrdersLink,
  supplierGroups,
  supplierOptions,
  toOrderEntries,
  type OrderLineRecord,
  type SupplierRecord,
} from './order';

function line(sku: string, supplier: string, patch: Partial<OrderLineRecord> = {}): OrderLineRecord {
  return {
    supplier,
    sku,
    name: `Producto ${sku}`,
    unit: 'u.',
    status: 'Low',
    stock: 2,
    minimum: 5,
    maximum: 12,
    quantityToOrder: 10,
    unitCost: 12.5,
    subtotal: 125,
    leadTimeDays: 3,
    estimatedDelivery: '2026-10-02',
    contact: 'Ana Pérez',
    phone: '+591 70000000',
    email: 'ventas@andina.example',
    ...patch,
  };
}

const LINES = [
  line('A-1', 'Andina', { status: 'OutOfStock', quantityToOrder: 4, unitCost: 100 }),
  line('A-2', 'Andina', { quantityToOrder: 3, unitCost: 0.335 }),
  line('S-1', 'Sur', { status: 'Critical', contact: '', phone: '', email: '', leadTimeDays: null, estimatedDelivery: null }),
  line('X-1', NO_SUPPLIER),
];
const CATEGORIES = new Map([
  ['A-1', 'Teclados'],
  ['A-2', 'Periféricos'],
  ['S-1', 'Almacenamiento'],
]);
const SUPPLIERS: SupplierRecord[] = [
  { code: 'PROV-AND', name: 'Andina', taxId: null, leadTimeDays: 3, contact: 'Ana Pérez', phone: null, email: null, isActive: true, products: 5, openOrders: 0, purchased: 0 },
  { code: 'PROV-SUR', name: 'Sur', taxId: null, leadTimeDays: 8, contact: 'Luis Soto', phone: '+591 4 4000000', email: 'compras@sur.example', isActive: true, products: 2, openOrders: 2, purchased: 0 },
];

describe('Pedido · cantidades revisadas', () => {
  it('usa la sugerida hasta que se revisa; una vacía (null) no se pide; el subtotal se redondea a 2 decimales', () => {
    const entries = toOrderEntries(LINES, CATEGORIES, new Map<string, number | null>([['A-1', 6], ['A-2', 3], ['S-1', null]]));
    expect(entries.map((entry) => [entry.key, entry.quantity, entry.edited, entry.subtotal])).toEqual([
      ['A-1', 6, true, 600],
      ['A-2', 3, false, 1.01],
      ['S-1', 0, true, 0],
      ['X-1', 10, false, 125],
    ]);
    expect(entries[3].category).toBe('Sin categoría');
  });

  it('filtra por proveedor, categoría, semáforo y búsqueda; opciones sin repetir («Sin proveedor» en palabras)', () => {
    const entries = toOrderEntries(LINES, CATEGORIES, new Map());
    const skus = (filters: Partial<typeof ORDER_FILTERS>) => filterOrder(entries, { ...ORDER_FILTERS, ...filters }).map((entry) => entry.key);
    expect(skus({ proveedor: 'Andina' })).toEqual(['A-1', 'A-2']);
    expect(skus({ categoria: 'Almacenamiento' })).toEqual(['S-1']);
    expect(skus({ estado: 'OutOfStock' })).toEqual(['A-1']);
    expect(skus({ q: 'perifericos' })).toEqual(['A-2']);
    expect(supplierOptions(entries)).toEqual([
      { value: NO_SUPPLIER, label: 'Sin proveedor' },
      { value: 'Andina', label: 'Andina' },
      { value: 'Sur', label: 'Sur' },
    ]);
    expect(categoryOptions(entries).map((option) => option.value)).toEqual(['Almacenamiento', 'Periféricos', 'Sin categoría', 'Teclados']);
  });
});

describe('Pedido · proveedores', () => {
  const entries = toOrderEntries(LINES, CATEGORIES, new Map([['A-2', 0]]));
  const groups = supplierGroups(entries, SUPPLIERS);

  it('agrupa por proveedor con su código, sus órdenes abiertas, el total y el contacto (el de la lista si falta)', () => {
    expect(groups.map((group) => [group.name, group.code, group.openOrders, group.total, group.toOrder.map((entry) => entry.key)])).toEqual([
      ['Andina', 'PROV-AND', 0, 400, ['A-1']],
      ['Sur', 'PROV-SUR', 2, 125, ['S-1']],
      [NO_SUPPLIER, null, 0, 125, ['X-1']],
    ]);
    expect(groups[1]).toMatchObject({ contact: 'Luis Soto', phone: '+591 4 4000000', email: 'compras@sur.example', leadTimeDays: 8, estimatedDelivery: null });
    expect(busySuppliers(groups).map((group) => group.name)).toEqual(['Sur']);
  });

  it('explica por qué no se puede crear la orden', () => {
    expect(cannotOrderReason(groups[0])).toBeNull();
    expect(cannotOrderReason(groups[2])).toBe('Estos productos no tienen proveedor preferido: asígnelo en el catálogo.');
    expect(cannotOrderReason({ ...groups[0], code: null })).toBe('El proveedor no está en la lista de proveedores.');
    expect(cannotOrderReason({ ...groups[0], active: false })).toBe('El proveedor está inactivo.');
    expect(cannotOrderReason({ ...groups[0], toOrder: [] })).toBe('Todas las cantidades están en 0.');
  });

  it('arma `CreatePurchaseOrderCommand` con TODOS los campos y solo las cantidades mayores que 0', () => {
    expect(purchaseOrderPayload(groups[0], '2026-10-05', '  Revisado  ')).toEqual({
      supplierCode: 'PROV-AND',
      expectedDate: '2026-10-05',
      notes: 'Revisado',
      lines: [{ sku: 'A-1', quantity: 4, unitCost: 100 }],
    });
    expect(purchaseOrderPayload(groups[0], null, ' ')).toMatchObject({ expectedDate: null, notes: null });
  });

  it('entrega, texto para copiar y enlace a las órdenes de compra', () => {
    expect(deliveryText(groups[0])).toBe('Entrega en 3 días · llegaría el 02/10/2026');
    expect(deliveryText({ leadTimeDays: 1, estimatedDelivery: null })).toBe('Entrega en 1 día');
    expect(deliveryText({ leadTimeDays: null, estimatedDelivery: null })).toBe('Plazo de entrega sin definir');
    expect(orderText(groups[0], '2026-09-29')).toBe(
      ['Pedido sugerido · 29/09/2026', 'Proveedor: Andina · +591 70000000 · ventas@andina.example', '  A-1  Producto A-1  →  4 u.  (Bs 400,00)', 'Total: Bs 400,00'].join('\n'),
    );
    expect(purchaseOrdersLink()).toBe('/panel/compras');
    expect(purchaseOrdersLink('PROV-AND')).toBe('/panel/compras?proveedor=PROV-AND');
  });
});

describe('Pedido · CSV', () => {
  it('lleva la cantidad sugerida y la que se va a pedir (y neutraliza lo que parece una fórmula)', () => {
    const entries = toOrderEntries([LINES[0]], CATEGORIES, new Map([['A-1', 6]]));
    const lines = buildCsv(ORDER_CSV, entries, { bom: false }).trim().split('\r\n');
    expect(lines[0]).toBe(
      '"Proveedor";"SKU";"Producto";"Categoría";"Unidad";"Semáforo";"Existencias";"Mínimo";"Máximo";"Sugerido";"A pedir";"Costo unitario";"Subtotal";"Entrega estimada";"Contacto";"Teléfono";"Correo"',
    );
    expect(lines[1]).toBe(`"Andina";"A-1";"Producto A-1";"Teclados";"u.";"Sin stock";2;5;12;4;6;100;600;"02/10/2026";"Ana Pérez";"'+591 70000000";"ventas@andina.example"`);
    // El teléfono empieza con «+»: el CSV le antepone un apóstrofo para que Excel no lo tome como fórmula.
  });
});
