// Módulo «Clientes» · funciones puras: cada cliente con sus datos de factura, filtros, resumen de la cartera y el
// formulario (reglas del SIN y pedidos con la forma exacta del contrato).

import { describe, expect, it } from 'vitest';
import {
  CUSTOMER_FILTERS,
  cartSummary,
  categoryOptions,
  customerProblems,
  draftOf,
  filterCustomers,
  hasProblems,
  toCustomerItems,
  toSaveCustomer,
  toSaveIdentity,
  toggleActivePayload,
  verifiableNit,
  type CustomerDraft,
  type CustomerRecord,
  type IdentityRecord,
} from './customers';

function customer(overrides: Partial<CustomerRecord>): CustomerRecord {
  return {
    code: 'C0001',
    name: 'Cliente',
    taxId: null,
    email: null,
    phone: null,
    categoryCode: 'GENERAL',
    category: 'General',
    isActive: true,
    purchases: 0,
    total: 0,
    lastPurchase: null,
    ...overrides,
  };
}

const CUSTOMERS = [
  customer({ code: 'CF', name: 'Consumidor final', purchases: 40, total: 12000 }),
  customer({ code: 'C0002', name: 'Mariana Céspedes', taxId: '4455667', email: 'mariana@correo.example', phone: '71234567', categoryCode: 'FRECUENTE', category: 'Frecuente', purchases: 5, total: 9500.5 }),
  customer({ code: 'WEB-000003', name: 'Luis Arce', taxId: '1020304050', email: 'luis@correo.example', purchases: 1, total: 120 }),
  customer({ code: 'C0004', name: 'Importadora Andina S.R.L.', taxId: '3344556', phone: '2-2445566', categoryCode: 'MAYORISTA', category: 'Mayorista', isActive: false }),
];

const IDENTITIES: IdentityRecord[] = [
  { code: 'C0002', documentType: 1, documentNumber: '4455667', complement: '1A' },
  { code: 'WEB-000003', documentType: 5, documentNumber: '1020304050', complement: null },
  { code: 'C0004', documentType: null, documentNumber: '3344556', complement: null },
];

const items = toCustomerItems(CUSTOMERS, IDENTITIES);

function draft(overrides: Partial<CustomerDraft>): CustomerDraft {
  return { name: 'Rosa Quispe', categoryCode: 'GENERAL', documentType: '', taxId: '', complement: '', email: '', phone: '', isActive: true, ...overrides };
}

describe('Clientes · lista', () => {
  it('une cada cliente con sus datos de factura y marca los clientes web', () => {
    expect(items.map((item) => [item.key, item.document, item.hasInvoiceData, item.webCustomer, item.state, item.contact])).toEqual([
      ['CF', null, false, false, 'activo', ''],
      ['C0002', 'CI 4455667-1A', true, false, 'activo', 'mariana@correo.example · 71234567'],
      ['WEB-000003', 'NIT 1020304050', true, true, 'activo', 'luis@correo.example'],
      ['C0004', '3344556', false, false, 'inactivo', '2-2445566'],
    ]);
  });

  it('filtra por categoría, estado, datos de factura, origen y búsqueda (nombre, código, NIT/CI, correo o teléfono)', () => {
    const keys = (filters: Partial<typeof CUSTOMER_FILTERS>) => filterCustomers(items, { ...CUSTOMER_FILTERS, ...filters }).map((item) => item.key);
    expect(keys({ categoria: 'FRECUENTE' })).toEqual(['C0002']);
    expect(keys({ estado: 'inactivo' })).toEqual(['C0004']);
    expect(keys({ factura: 'sin' })).toEqual(['CF', 'C0004']);
    expect(keys({ factura: 'con' })).toEqual(['C0002', 'WEB-000003']);
    expect(keys({ origen: 'web' })).toEqual(['WEB-000003']);
    expect(keys({ origen: 'tienda' })).toHaveLength(3);
    expect(keys({ q: 'cespedes' })).toEqual(['C0002']);
    expect(keys({ q: '1020304050' })).toEqual(['WEB-000003']);
    expect(keys({ q: 'luis@correo' })).toEqual(['WEB-000003']);
    expect(keys({ q: '2445566' })).toEqual(['C0004']);
  });

  it('opciones de categoría con cuántos clientes tiene y resumen de la cartera', () => {
    expect(categoryOptions([{ code: 'FRECUENTE', name: 'Frecuente' }, { code: 'GENERAL', name: 'General' }], items)).toEqual([
      { value: 'FRECUENTE', label: 'Frecuente (1)' },
      { value: 'GENERAL', label: 'General (2)' },
    ]);
    expect(cartSummary(items)).toEqual({
      active: 3,
      total: 4,
      buyers: 3,
      purchases: 46,
      soldToCustomers: 9620.5,
      finalConsumer: 12000,
      web: 1,
      withInvoiceData: 2,
      top: { name: 'Mariana Céspedes', total: 9500.5, purchases: 5 },
    });
  });
});

describe('Clientes · formulario', () => {
  it('el formulario vacío toma la categoría GENERAL; el de un cliente, sus datos', () => {
    const categories = [{ code: 'FRECUENTE', name: 'Frecuente' }, { code: 'GENERAL', name: 'General' }];
    expect(draftOf(null, categories)).toEqual(draft({ name: '', categoryCode: 'GENERAL' }));
    expect(draftOf(items[1], categories)).toEqual({
      name: 'Mariana Céspedes',
      categoryCode: 'FRECUENTE',
      documentType: '1',
      taxId: '4455667',
      complement: '1A',
      email: 'mariana@correo.example',
      phone: '71234567',
      isActive: true,
    });
  });

  it('valida con las reglas del servidor y del SIN', () => {
    expect(customerProblems(draft({ name: ' ', categoryCode: '' }), null, true)).toEqual({ name: 'Indique el nombre del cliente.', categoryCode: 'Elija la categoría del cliente.' });
    expect(customerProblems(draft({ email: 'no-es-correo' }), null, true).email).toBe('El correo no es válido (por ejemplo nombre@dominio.com).');
    expect(customerProblems(draft({ documentType: '1', taxId: '' }), null, true).taxId).toBe('Con tipo de documento, el número es obligatorio.');
    expect(customerProblems(draft({ documentType: '1', taxId: '44.556' }), null, true).taxId).toBe('El CI lleva solo números (sin puntos ni guiones).');
    expect(customerProblems(draft({ documentType: '5', taxId: '12A' }), null, true).taxId).toBe('El NIT lleva solo números (sin puntos ni guiones).');
    expect(customerProblems(draft({ documentType: '3', taxId: 'AB12345' }), null, true)).toEqual({});
    expect(customerProblems(draft({ documentType: '1', taxId: '123', complement: '123456' }), null, true).complement).toBe('El complemento tiene como máximo 5 caracteres.');
    expect(customerProblems(draft({ documentType: '1', taxId: '1'.repeat(21) }), null, true).taxId).toBe('El número de documento tiene como máximo 20 caracteres.');
    // Sin la facturación del SIN, el NIT/CI es libre (hasta 30 caracteres).
    expect(customerProblems(draft({ documentType: '1', taxId: '44.556' }), null, false)).toEqual({});
    expect(customerProblems(draft({ isActive: false }), 'CF', true).isActive).toBe('El consumidor final no se puede desactivar.');
    expect(hasProblems({})).toBe(false);
  });

  it('arma los pedidos con la forma EXACTA del contrato', () => {
    const form = draft({ documentType: '1', taxId: ' 7788990 ', complement: ' 2b ', email: ' rosa@correo.example ', phone: '' });
    expect(toSaveCustomer(null, form)).toEqual({ code: null, name: 'Rosa Quispe', taxId: '7788990', email: 'rosa@correo.example', phone: null, categoryCode: 'GENERAL', isActive: true });
    expect(toSaveIdentity('C0005', form)).toEqual({ code: 'C0005', documentType: 1, documentNumber: '7788990', complement: '2B' });
    // Complemento solo con CI; sin tipo, el número queda libre.
    expect(toSaveIdentity('C0005', { ...form, documentType: '5' })).toEqual({ code: 'C0005', documentType: 5, documentNumber: '7788990', complement: null });
    expect(toSaveIdentity('C0005', { ...form, documentType: '' })).toEqual({ code: 'C0005', documentType: null, documentNumber: '7788990', complement: null });
    expect(toggleActivePayload(items[1], false)).toEqual({
      code: 'C0002',
      name: 'Mariana Céspedes',
      taxId: '4455667',
      email: 'mariana@correo.example',
      phone: '71234567',
      categoryCode: 'FRECUENTE',
      isActive: false,
    });
  });

  it('solo se verifica un NIT escrito con dígitos', () => {
    expect(verifiableNit(draft({ documentType: '5', taxId: '1020304050' }))).toBe(1020304050);
    expect(verifiableNit(draft({ documentType: '5', taxId: '10-20' }))).toBeNull();
    expect(verifiableNit(draft({ documentType: '1', taxId: '1020304050' }))).toBeNull();
  });
});
