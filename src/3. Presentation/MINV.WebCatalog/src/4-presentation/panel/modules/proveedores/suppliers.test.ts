// Módulo «Proveedores» · pruebas de las funciones puras: presentación, filtros, resumen, enlaces a las órdenes y el
// formulario (validaciones y el pedido exacto de `SaveSupplierCommand`).

import { describe, expect, it } from 'vitest';
import {
  SUPPLIER_FILTERS,
  daysText,
  draftOf,
  filterSuppliers,
  newOrderLink,
  ordersLink,
  supplierProblems,
  suppliersSummary,
  toSavePayload,
  toSupplierItems,
  toggleActivePayload,
  type SupplierRecord,
} from './suppliers';

function supplier(overrides: Partial<SupplierRecord>): SupplierRecord {
  return {
    code: 'P001',
    name: 'Distribuidora Andina',
    taxId: '1020304',
    leadTimeDays: 3,
    contact: 'Rosa Quispe',
    phone: '71234567',
    email: 'ventas@andina.example',
    isActive: true,
    products: 4,
    openOrders: 1,
    purchased: 5000,
    ...overrides,
  };
}

const ROWS = [
  supplier({}),
  supplier({ code: 'P002', name: 'Tecno Import', taxId: null, contact: null, phone: null, email: null, leadTimeDays: 10, openOrders: 0, purchased: 12000, products: 2 }),
  supplier({ code: 'P003', name: 'Viejo proveedor', isActive: false, leadTimeDays: 5, openOrders: 0, purchased: 0, products: 0 }),
];

describe('Proveedores · lista', () => {
  it('presenta el contacto y filtra por estado, órdenes abiertas, plazo y búsqueda (también por código)', () => {
    const items = toSupplierItems(ROWS);
    expect(items[0].contact).toBe('Rosa Quispe · 71234567 · ventas@andina.example');
    expect(items[1].contact).toBe('');
    const keys = (filters: Partial<typeof SUPPLIER_FILTERS>) => filterSuppliers(items, { ...SUPPLIER_FILTERS, ...filters }).map((item) => item.key);
    expect(keys({ estado: 'inactivo' })).toEqual(['P003']);
    expect(keys({ ordenes: 'con' })).toEqual(['P001']);
    expect(keys({ ordenes: 'sin' })).toEqual(['P002', 'P003']);
    expect(keys({ entrega: 'rapido' })).toEqual(['P001']);
    expect(keys({ entrega: 'semana' })).toEqual(['P003']);
    expect(keys({ entrega: 'mas' })).toEqual(['P002']);
    expect(keys({ q: 'p002' })).toEqual(['P002']);
    expect(keys({ q: 'rosa' })).toEqual(['P001', 'P003']);
  });

  it('resume activos, órdenes abiertas, lo comprado (con el principal) y la entrega promedio de los activos', () => {
    expect(suppliersSummary(ROWS)).toEqual({
      active: 2,
      total: 3,
      products: 6,
      openOrders: 1,
      withOpenOrders: 1,
      purchased: 17000,
      top: ROWS[1],
      averageLead: 6.5,
    });
    expect(suppliersSummary([]).averageLead).toBeNull();
  });

  it('lleva a sus órdenes y a una orden nueva del módulo «Órdenes de compra»', () => {
    expect(ordersLink('P 1')).toBe('/panel/compras?proveedor=P%201');
    expect(newOrderLink('P002')).toBe('/panel/compras?nueva=1&proveedor=P002');
    expect(daysText(0)).toBe('El mismo día');
    expect(daysText(1)).toBe('1 día');
  });
});

describe('Proveedores · formulario', () => {
  it('valida la razón social, los días de entrega, los largos y el correo', () => {
    expect(supplierProblems({ ...draftOf(null), name: ' ', leadTimeDays: null, email: 'rosa@' })).toEqual({
      name: 'Indique la razón social.',
      leadTimeDays: 'Indique los días de entrega.',
      email: 'El correo no es válido (por ejemplo nombre@dominio.com).',
    });
    expect(supplierProblems({ ...draftOf(null), name: 'X', leadTimeDays: 400, taxId: '1'.repeat(31) })).toEqual({
      leadTimeDays: 'Los días de entrega van de 0 a 365.',
      taxId: 'Hasta 30 caracteres.',
    });
    expect(supplierProblems({ ...draftOf(null), name: 'X' })).toEqual({});
  });

  it('arma el pedido exacto (vacíos como null) y el de activar o desactivar', () => {
    expect(toSavePayload(null, { ...draftOf(null), name: '  Nuevo SRL ', taxId: ' ', leadTimeDays: 7, email: ' a@b.example ' })).toEqual({
      code: null,
      name: 'Nuevo SRL',
      taxId: null,
      leadTimeDays: 7,
      contactName: null,
      phone: null,
      email: 'a@b.example',
      isActive: true,
    });
    expect(toggleActivePayload(ROWS[0], false)).toEqual({
      code: 'P001',
      name: 'Distribuidora Andina',
      taxId: '1020304',
      leadTimeDays: 3,
      contactName: 'Rosa Quispe',
      phone: '71234567',
      email: 'ventas@andina.example',
      isActive: false,
    });
  });
});
