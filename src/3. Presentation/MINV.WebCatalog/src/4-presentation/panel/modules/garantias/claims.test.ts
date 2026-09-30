// Módulo «Garantías» · funciones puras: el pedido de la lista, los filtros de la página, los pasos siguientes (solo los
// que manda el servidor), los textos de la garantía, de la venta y de la bitácora, el resumen, el CSV, las opciones de
// clientes y de reemplazo, y los enlaces. Sin React ni red.

import { describe, expect, it } from 'vitest';
import {
  CLAIMS_CSV,
  CLAIM_FILTERS,
  STATE_FILTER_OPTIONS,
  branchOptions,
  chosenText,
  claimTimeline,
  claimsRequest,
  claimsSummary,
  customerOptions,
  customerSalesPath,
  daysText,
  eventTitle,
  filterClaims,
  issueProblem,
  lookupWarning,
  movedText,
  needsCustomer,
  needsResolution,
  needsSupplier,
  nextSteps,
  noteProblem,
  replacementOptions,
  saleLine,
  seriesPath,
  statusBars,
  stepQuestion,
  toClaimItems,
  usesReplacement,
  warrantyLine,
  type ClaimRecord,
  type CustomerRecord,
} from './claims';

function claim(overrides: Partial<ClaimRecord>): ClaimRecord {
  return {
    id: '00000000-0000-0000-0000-000000000001',
    number: 'RMA-CM-000001',
    branchCode: 'CM',
    serial: 'SN-0001',
    sku: 'NB-ASUS-01',
    product: 'Notebook ASUS TUF',
    customer: 'Mariana Céspedes',
    issue: 'No enciende',
    status: 'Received',
    isInWarranty: true,
    warrantyUntil: '2027-03-15',
    receivedAt: '2026-09-20T14:00:00Z',
    closedAt: null,
    supplier: null,
    resolution: null,
    replacementSerial: null,
    daysOpen: 9,
    ...overrides,
  };
}

describe('Garantías · pedido y filtros', () => {
  it('«Abiertos» (al entrar), todos o un estado: siempre con los dos parámetros', () => {
    expect(CLAIM_FILTERS.estado).toBe('abiertos');
    expect(claimsRequest('abiertos')).toEqual({ status: null, onlyOpen: true });
    expect(claimsRequest('')).toEqual({ status: null, onlyOpen: false });
    expect(claimsRequest('SentToSupplier')).toEqual({ status: 'SentToSupplier', onlyOpen: false });
    expect(claimsRequest('Perdido')).toEqual({ status: null, onlyOpen: false });
    expect(STATE_FILTER_OPTIONS[0]).toEqual({ value: 'abiertos', label: 'Abiertos (sin entregar)' });
    expect(STATE_FILTER_OPTIONS.map((option) => option.value)).toContain('Delivered');
  });

  it('filtra por búsqueda (sin acentos), sucursal, cobertura, días y fecha de recepción', () => {
    const items = toClaimItems([
      claim({ number: 'RMA-CM-000001', customer: 'Mariana Céspedes', daysOpen: 3 }),
      claim({ number: 'RMA-CB-000002', branchCode: 'CB', serial: 'IMEI-77', isInWarranty: false, daysOpen: 20, receivedAt: '2026-09-01T02:00:00Z' }),
      claim({ number: 'RMA-CB-000003', branchCode: 'CB', status: 'Delivered', supplier: 'Distribuidora Andina', daysOpen: 40 }),
    ]);
    const numbers = (filters: Partial<typeof CLAIM_FILTERS>) => filterClaims(items, { ...CLAIM_FILTERS, ...filters }).map((item) => item.key);
    expect(numbers({})).toEqual(['RMA-CM-000001', 'RMA-CB-000002', 'RMA-CB-000003']);
    expect(numbers({ q: 'cespedes' })).toEqual(['RMA-CM-000001', 'RMA-CB-000002', 'RMA-CB-000003']);
    expect(numbers({ q: 'imei-77' })).toEqual(['RMA-CB-000002']);
    expect(numbers({ q: 'andina' })).toEqual(['RMA-CB-000003']);
    expect(numbers({ sucursal: 'CB' })).toEqual(['RMA-CB-000002', 'RMA-CB-000003']);
    expect(numbers({ cobertura: 'cargo' })).toEqual(['RMA-CB-000002']);
    expect(numbers({ dias: '15' })).toEqual(['RMA-CB-000002', 'RMA-CB-000003']);
    expect(numbers({ dias: '30' })).toEqual(['RMA-CB-000003']);
    // 2026-09-01T02:00Z es el 31 de agosto en La Paz.
    expect(numbers({ desde: '2026-08-31', hasta: '2026-08-31' })).toEqual(['RMA-CB-000002']);
    expect(items.map((item) => item.open)).toEqual([true, true, false]);
    expect(branchOptions(items, [{ code: 'CM', name: 'Casa Matriz' }])).toEqual([
      { value: 'CB', label: 'CB' },
      { value: 'CM', label: 'CM · Casa Matriz' },
    ]);
  });
});

describe('Garantías · pasos siguientes (los decide el servidor)', () => {
  it('ofrece SOLO los estados de `nextStatuses`, en su orden, con los textos del escritorio', () => {
    expect(nextSteps({ nextStatuses: ['SentToSupplier', 'Repaired', 'Replaced', 'Rejected'] })).toEqual([
      { next: 'SentToSupplier', label: 'Enviar al proveedor', kind: 'outline' },
      { next: 'Repaired', label: 'Marcar reparado', kind: 'primary' },
      { next: 'Replaced', label: 'Reemplazar con otra unidad', kind: 'primary' },
      { next: 'Rejected', label: 'Rechazar la garantía', kind: 'danger' },
    ]);
    expect(nextSteps({ nextStatuses: [] })).toEqual([]);
    expect(stepQuestion('Delivered', 'RMA-CM-000001')).toBe('¿Entregar el equipo del caso RMA-CM-000001 al cliente?');
  });

  it('qué pide cada paso: resolución, proveedor o la entrega del reemplazo', () => {
    expect((['Diagnosing', 'SentToSupplier', 'Repaired', 'Replaced', 'Rejected', 'Delivered'] as const).map(needsResolution)).toEqual([false, false, true, true, true, false]);
    expect(needsSupplier('SentToSupplier')).toBe(true);
    expect(needsSupplier('Repaired')).toBe(false);
    expect(usesReplacement('Replaced', { replacementSerial: null })).toBe(true);
    expect(usesReplacement('Replaced', { replacementSerial: 'SN-9' })).toBe(false);
    expect(usesReplacement('Repaired', { replacementSerial: null })).toBe(false);
    expect(chosenText('otro', '  Cambio de placa ')).toBe('Cambio de placa');
    expect(chosenText('Sello de garantía roto', 'x')).toBe('Sello de garantía roto');
    expect(movedText(claim({ status: 'SentToSupplier' }))).toBe('Caso RMA-CM-000001: en el proveedor');
  });
});

describe('Garantías · textos', () => {
  it('la bitácora nombra el hecho y el estado al que pasó, lo más reciente primero', () => {
    const events = [
      { occurredAt: '2026-09-20T14:00:00Z', action: 'Opened' as const, status: 'Received' as const, note: 'Recibido en garantía', user: 'Carla Rojas' },
      { occurredAt: '2026-09-21T14:00:00Z', action: 'StatusChanged' as const, status: 'Diagnosing' as const, note: null, user: 'Bruno Mamani' },
      { occurredAt: '2026-09-22T14:00:00Z', action: 'Closed' as const, status: 'Delivered' as const, note: null, user: 'Bruno Mamani' },
    ];
    expect(claimTimeline(events).map(eventTitle)).toEqual(['Caso cerrado · entregado', 'Cambio de estado · en diagnóstico', 'Caso abierto']);
  });

  it('la garantía y la venta en una línea (las calcula el servidor)', () => {
    expect(warrantyLine({ warrantyMonths: 12, warrantyUntil: '2027-03-15', inWarranty: true })).toEqual({ text: 'En garantía hasta el 15/03/2027', tone: 'success' });
    expect(warrantyLine({ warrantyMonths: 12, warrantyUntil: '2026-03-15', inWarranty: false })).toEqual({ text: 'Garantía vencida el 15/03/2026', tone: 'danger' });
    expect(warrantyLine({ warrantyMonths: 0, warrantyUntil: null, inWarranty: false }).text).toBe('El producto no tiene garantía');
    expect(
      saleLine({
        invoiceNumber: null,
        warranty: { soldOn: '2026-03-15', invoiceNumber: 'F-CM-000123', customer: 'Mariana Céspedes' } as Parameters<typeof saleLine>[0]['warranty'],
      }),
    ).toBe('vendido el 15/03/2026 · F-CM-000123 · Mariana Céspedes');
    expect(daysText({ closedAt: null, daysOpen: 1 })).toBe('1 día abierto');
    expect(daysText({ closedAt: null, daysOpen: 12 })).toBe('12 días abierto');
    expect(daysText({ closedAt: '2026-09-29T15:00:00Z', daysOpen: 3 })).toBe('Cerrado el 29/09/2026');
  });

  it('al abrir un caso avisa si ya hay uno abierto o si la unidad no está vendida', () => {
    expect(lookupWarning({ status: 'Sold', openClaim: null })).toBeNull();
    expect(lookupWarning({ status: 'Returned', openClaim: null })).toBeNull();
    expect(lookupWarning({ status: 'Sold', openClaim: 'RMA-CM-000004' })).toBe('La serie ya tiene el caso RMA-CM-000004 abierto.');
    expect(lookupWarning({ status: 'InStock', openClaim: null })).toBe('Solo se abre un caso de garantía de una unidad vendida: la serie está en stock.');
    expect(needsCustomer({ customerCode: null })).toBe(true);
    expect(needsCustomer({ customerCode: 'C0002' })).toBe(false);
    expect(issueProblem('abc')).toBe('Describa la falla reportada (al menos 5 caracteres).');
    expect(issueProblem('No enciende')).toBeNull();
    expect(noteProblem('  ')).toBe('Escriba la nota.');
    expect(noteProblem('x'.repeat(501))).toMatch(/hasta 500/);
  });
});

describe('Garantías · resumen, CSV, opciones y enlaces', () => {
  it('los indicadores del escritorio sobre todos los casos', () => {
    const rows = [
      claim({ status: 'Received', daysOpen: 2 }),
      claim({ status: 'Diagnosing', daysOpen: 5, isInWarranty: false }),
      claim({ status: 'SentToSupplier', daysOpen: 18 }),
      claim({ status: 'Delivered', daysOpen: 30, replacementSerial: 'SN-9' }),
    ];
    expect(claimsSummary(rows)).toEqual({ total: 4, open: 3, oldest: 18, workshop: 2, atSupplier: 1, chargeable: 1, delivered: 1, replaced: 1 });
    expect(statusBars(rows)).toEqual([
      { label: 'Recibido', value: 1 },
      { label: 'En diagnóstico', value: 1 },
      { label: 'En el proveedor', value: 1 },
      { label: 'Entregado', value: 1 },
    ]);
  });

  it('el CSV lleva las columnas en palabras', () => {
    expect(CLAIMS_CSV.map((column) => column.header)).toEqual([
      'Caso',
      'Sucursal',
      'Recibido',
      'Estado',
      'Cobertura',
      'Serie o IMEI',
      'SKU',
      'Producto',
      'Cliente',
      'Falla reportada',
      'Garantía hasta',
      'Proveedor',
      'Resolución',
      'Reemplazo',
      'Días',
      'Cerrado',
    ]);
    const [item] = toClaimItems([claim({ isInWarranty: false, status: 'Diagnosing' })]);
    expect(CLAIMS_CSV.find((column) => column.header === 'Cobertura')?.value(item)).toBe('Con cargo');
    expect(CLAIMS_CSV.find((column) => column.header === 'Estado')?.value(item)).toBe('En diagnóstico');
  });

  it('clientes activos por nombre; unidades de reemplazo sin la del caso', () => {
    const customers = [
      { code: 'C2', name: 'Zoe', taxId: '123', isActive: true },
      { code: 'C1', name: 'Ana', taxId: null, isActive: true },
      { code: 'C3', name: 'Inactivo', taxId: null, isActive: false },
    ] as CustomerRecord[];
    expect(customerOptions(customers).map((option) => [option.value, option.label, option.description])).toEqual([
      ['C1', 'Ana', 'C1'],
      ['C2', 'Zoe', 'C2 · NIT/CI 123'],
    ]);
    const available = [
      { serial: 'SN-0001', sku: 'NB', branch: 'CM', warehouse: 'ALM-CM' },
      { serial: 'SN-0002', sku: 'NB', branch: 'CM', warehouse: 'ALM-CM' },
    ] as unknown as Parameters<typeof replacementOptions>[0];
    expect(replacementOptions(available, 'SN-0001').map((option) => [option.value, option.description])).toEqual([['SN-0002', 'CM · ALM-CM']]);
  });

  it('los enlaces a Series y Ventas llevan sus parámetros codificados', () => {
    expect(seriesPath('SN 1', 'NB-01')).toBe('series?q=SN+1&producto=NB-01&ver=SN+1');
    expect(customerSalesPath('C0002')).toBe('ventas?cliente=C0002');
  });
});
