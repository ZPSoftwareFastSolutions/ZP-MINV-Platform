// Módulo «Ventas» · funciones puras: número y sucursal, rango de fechas del servidor, estado de cada venta, filtros,
// totales, estadísticas, acciones según el rol y el borrador de la devolución (reembolso estimado, validaciones y el pedido
// con la forma exacta del contrato).

import { describe, expect, it } from 'vitest';
import { base64ToBytes, safeFileName } from './download';
import {
  ALL_DATES_FROM,
  NO_INVOICE,
  activeFilters,
  branchOfNumber,
  buildReturnDraft,
  clearedFilters,
  customerOptions,
  distinctOptions,
  emailBlockedReason,
  estimatedRefund,
  filterReturns,
  filterSales,
  fiscalText,
  formatFiscalDateTime,
  lastDays,
  lineRefund,
  plainMessage,
  reasonProblem,
  reasonText,
  OTHER_REASON,
  returnProblems,
  returnsRange,
  saleActions,
  salesFilters,
  salesSummary,
  serverRange,
  shortCuf,
  toReturnItems,
  toReturnPayload,
  toSaleItems,
  totalsByDay,
  totalsByPaymentMethod,
  viewOf,
  voidsBeforeSin,
  type ReturnRecord,
  type ReturnableRecord,
  type SaleFiscalRecord,
  type SaleLineRecord,
  type SaleRecord,
} from './sales';

const TODAY = '2026-09-29';

function sale(overrides: Partial<SaleRecord>): SaleRecord {
  return {
    invoiceNumber: 'F-CM-000001',
    orderNumber: 'PV-CM-000001',
    issuedAt: '2026-09-29T15:00:00Z',
    date: TODAY,
    customerCode: 'CF',
    customer: 'Consumidor final',
    cashier: 'Diego Flores',
    paymentMethod: 'Efectivo',
    items: 1,
    total: 100,
    tax: 13,
    status: 'Issued',
    voidReason: null,
    ...overrides,
  };
}

function fiscal(overrides: Partial<SaleFiscalRecord>): SaleFiscalRecord {
  return {
    invoiceNumber: 'F-CM-000001',
    documentId: 'doc-1',
    number: 55,
    status: 'Valid',
    isReverted: false,
    emissionType: 1,
    cuf: 'ABCDEF1234567890ABCDEF',
    canVoid: true,
    canCreditNote: true,
    voidDeadline: '2026-10-09T23:59:59',
    ...overrides,
  };
}

function devolution(overrides: Partial<ReturnRecord>): ReturnRecord {
  return {
    number: 'DV-CM-000001',
    invoiceNumber: 'F-CM-000001',
    returnedAt: '2026-09-29T16:00:00Z',
    customer: 'Consumidor final',
    reason: 'Cambio de producto',
    refund: 40,
    creditNote: null,
    creditNoteStatus: null,
    ...overrides,
  };
}

const ALL = { reprint: true, email: true, returns: true, void: true };

describe('Ventas · números, fechas y textos', () => {
  it('la sucursal sale del número del documento (regla B-02)', () => {
    expect(branchOfNumber('F-CM-000123')).toBe('CM');
    expect(branchOfNumber('dv-cb-000001')).toBe('CB');
    expect(branchOfNumber('F-000123')).toBeNull();
  });

  it('el rango del servidor: hoy por defecto, «Todas» desde el principio hasta hoy, y nada si las fechas no sirven', () => {
    expect(serverRange({ from: TODAY, to: TODAY }, TODAY)).toEqual({ from: TODAY, to: TODAY });
    expect(serverRange({ from: null, to: null }, TODAY)).toEqual({ from: ALL_DATES_FROM, to: TODAY });
    expect(serverRange({ from: '2026-09-01', to: null }, TODAY)).toEqual({ from: '2026-09-01', to: TODAY });
    expect(serverRange({ from: null, to: '2026-09-10' }, TODAY)).toEqual({ from: ALL_DATES_FROM, to: '2026-09-10' });
    expect(serverRange({ from: '2026-09-10', to: '2026-09-01' }, TODAY)).toBeNull();
    // Las devoluciones se buscan hasta hoy (las de esas ventas pueden ser posteriores).
    expect(returnsRange({ from: '2026-09-01', to: '2026-09-10' }, TODAY)).toEqual({ from: '2026-09-01', to: TODAY });
  });

  it('la hora fiscal (sin zona) se escribe tal cual; con zona, en la hora de La Paz', () => {
    expect(formatFiscalDateTime('2026-10-09T23:59:59.9999999')).toBe('09/10/2026 23:59');
    expect(formatFiscalDateTime('2026-09-29T14:00:00Z')).toBe('29/09/2026 10:00');
    expect(formatFiscalDateTime(null)).toBe('—');
  });

  it('textos de la factura, mensajes y motivos', () => {
    expect(fiscalText(fiscal({}))).toBe('N° 55 · Válida');
    expect(fiscalText(fiscal({ isReverted: true }))).toBe('N° 55 · Válida (anulación revertida)');
    expect(fiscalText(null)).toBe('Sin factura del SIN');
    expect(shortCuf('ABCDEF1234567890ABCDEF')).toBe('ABCDEF12…ABCDEF');
    expect(plainMessage('✔ Factura F-CM-000001 anulada.')).toBe('Factura F-CM-000001 anulada.');
    expect(reasonText(OTHER_REASON, '  Cliente arrepentido ')).toBe('Cliente arrepentido');
    expect(reasonText('Venta duplicada', 'no se usa')).toBe('Venta duplicada');
    expect(reasonProblem('')).toBe('Indique el motivo.');
    expect(reasonProblem('x'.repeat(201))).toBe('Use como máximo 200 caracteres.');
    expect(viewOf('devoluciones')).toBe('devoluciones');
    expect(viewOf('otra')).toBe('ventas');
  });
});

describe('Ventas · lista', () => {
  const sales = [
    sale({ invoiceNumber: 'F-CM-000001', total: 100, tax: 13 }),
    sale({ invoiceNumber: 'F-CB-000002', customerCode: 'WEB-000003', customer: 'Luis Arce', cashier: 'Carla Rojas', paymentMethod: 'QR', total: 200, tax: 26, items: 3 }),
    sale({ invoiceNumber: 'F-CM-000003', status: 'Voided', voidReason: 'Venta duplicada', total: 500, tax: 65 }),
  ];
  const items = toSaleItems(sales, [fiscal({ invoiceNumber: 'F-CB-000002', documentId: 'doc-2', status: 'Offline', emissionType: 2 })], [devolution({ invoiceNumber: 'F-CB-000002', refund: 50 })]);

  it('arma cada venta: estado (vigente, con devolución, anulada), factura, sucursal y cliente web', () => {
    expect(items.map((item) => [item.key, item.state, item.fiscalState, item.branch, item.webCustomer, item.refunded])).toEqual([
      ['F-CM-000001', 'vigente', NO_INVOICE, 'CM', false, 0],
      ['F-CB-000002', 'devolucion', 'Offline', 'CB', true, 50],
      ['F-CM-000003', 'anulada', NO_INVOICE, 'CM', false, 0],
    ]);
  });

  it('filtra en la página por sucursal, cliente, cajero, pago, estado, factura y búsqueda sin acentos', () => {
    const base = salesFilters(TODAY);
    const keys = (filters: Partial<typeof base>) => filterSales(items, { ...base, ...filters }).map((item) => item.key);
    expect(keys({})).toHaveLength(3);
    expect(keys({ sucursal: 'CB' })).toEqual(['F-CB-000002']);
    expect(keys({ cliente: 'CF' })).toEqual(['F-CM-000001', 'F-CM-000003']);
    expect(keys({ cajero: 'Carla Rojas' })).toEqual(['F-CB-000002']);
    expect(keys({ pago: 'Efectivo', estado: 'anulada' })).toEqual(['F-CM-000003']);
    expect(keys({ fiscal: NO_INVOICE })).toEqual(['F-CM-000001', 'F-CM-000003']);
    expect(keys({ q: 'luís ÁRCE' })).toEqual(['F-CB-000002']);
    expect(keys({ q: 'f-cm' })).toEqual(['F-CM-000001', 'F-CM-000003']);
    // También por el número de la factura del SIN.
    expect(keys({ q: '55' })).toEqual(['F-CB-000002']);
  });

  it('cuenta los filtros de la pestaña (las fechas como uno) y los limpia volviendo a HOY', () => {
    const defaults = salesFilters(TODAY);
    const filters = { ...defaults, desde: '', hasta: '', cajero: 'Carla Rojas', nota: 'SinNota' };
    expect(activeFilters(filters, defaults, 'ventas')).toBe(2);
    expect(activeFilters(filters, defaults, 'devoluciones')).toBe(2);
    expect(clearedFilters(defaults, 'ventas')).toEqual({ desde: TODAY, hasta: TODAY, q: '', sucursal: '', cliente: '', cajero: '', pago: '', estado: '', fiscal: '' });
  });

  it('opciones de las listas y totales (sin las anuladas)', () => {
    expect(distinctOptions(['Efectivo', 'QR', 'Efectivo', null, ' '])).toEqual([
      { value: 'Efectivo', label: 'Efectivo' },
      { value: 'QR', label: 'QR' },
    ]);
    expect(customerOptions(items).map((option) => option.value)).toEqual(['CF', 'WEB-000003']);
    expect(salesSummary(items)).toMatchObject({ count: 3, valid: 2, voided: 1, total: 300, tax: 39, voidedTotal: 500, lines: 4, average: 150, largest: 200, refunded: 50, withReturns: 1 });
  });

  it('las acciones de una venta según el rol y su estado (el servidor decide igual)', () => {
    const [vigente, conDevolucion, anulada] = items;
    expect(saleActions(vigente, ALL, true)).toEqual({
      reprint: { visible: false, blocked: null },
      email: { visible: false, blocked: null },
      returns: { visible: true, blocked: null },
      void: { visible: true, blocked: null },
    });
    expect(saleActions(conDevolucion, ALL, true).void.blocked).toBe('La venta tiene devoluciones registradas: no se puede anular entera.');
    expect(saleActions(conDevolucion, ALL, true).reprint.visible).toBe(true);
    expect(saleActions(conDevolucion, { ...ALL, email: false }, true).email.visible).toBe(false);
    expect(saleActions(conDevolucion, ALL, false).reprint.visible).toBe(false);
    expect(saleActions(anulada, ALL, true).void.visible).toBe(false);
    expect(saleActions(anulada, ALL, true).returns.visible).toBe(false);
  });

  it('la factura activa se anula ante el SIN; solo se envía por correo lo que se entrega al comprador', () => {
    expect(voidsBeforeSin(null)).toBe(false);
    expect(voidsBeforeSin(fiscal({ status: 'Valid' }))).toBe(true);
    expect(voidsBeforeSin(fiscal({ status: 'Rejected' }))).toBe(false);
    expect(voidsBeforeSin(fiscal({ status: 'Voided' }))).toBe(false);
    expect(emailBlockedReason(fiscal({ status: 'Valid' }))).toBeNull();
    expect(emailBlockedReason(fiscal({ status: 'NoResponse' }))).toBe('Esta factura no se entrega al comprador (sin respuesta).');
  });

  it('devoluciones: filtra por las fechas elegidas, la nota y el cliente', () => {
    const returns = toReturnItems([
      devolution({ number: 'DV-CM-000001', returnedAt: '2026-09-29T16:00:00Z', creditNote: 'Nota N° 9', creditNoteStatus: 'Valid' }),
      devolution({ number: 'DV-CB-000002', invoiceNumber: 'F-CB-000002', customer: 'Luis Arce', returnedAt: '2026-09-20T16:00:00Z' }),
    ]);
    const base = salesFilters(TODAY);
    expect(filterReturns(returns, base, { from: TODAY, to: TODAY }, null).map((item) => item.key)).toEqual(['DV-CM-000001']);
    expect(filterReturns(returns, { ...base, nota: 'SinNota' }, { from: null, to: null }, null).map((item) => item.key)).toEqual(['DV-CB-000002']);
    const customer = { invoices: new Set(['F-CB-000002']), name: 'Luis Arce' };
    expect(filterReturns(returns, { ...base, cliente: 'WEB-000003' }, { from: null, to: null }, customer).map((item) => item.key)).toEqual(['DV-CB-000002']);
  });
});

describe('Ventas · estadísticas', () => {
  it('los últimos 7 días (hoy incluido) y lo vendido por día y por medio de pago, sin las anuladas', () => {
    const range = lastDays(7, new Date('2026-09-29T15:00:00Z'));
    expect(range).toEqual({ from: '2026-09-23', to: TODAY });
    const rows = [
      sale({ date: '2026-09-29', total: 100, paymentMethod: 'Efectivo' }),
      sale({ date: '2026-09-29', total: 50.5, paymentMethod: 'QR' }),
      sale({ date: '2026-09-25', total: 300, paymentMethod: 'Efectivo' }),
      sale({ date: '2026-09-25', total: 999, status: 'Voided', paymentMethod: 'Tarjeta' }),
    ];
    const days = totalsByDay(rows, range);
    expect(days).toHaveLength(7);
    expect(days.find((day) => day.day === '2026-09-29')).toEqual({ day: '2026-09-29', total: 150.5, count: 2 });
    expect(days.find((day) => day.day === '2026-09-25')).toEqual({ day: '2026-09-25', total: 300, count: 1 });
    expect(days.find((day) => day.day === '2026-09-24')).toEqual({ day: '2026-09-24', total: 0, count: 0 });
    expect(totalsByPaymentMethod(rows)).toEqual([
      { label: 'Efectivo', value: 400, count: 2 },
      { label: 'QR', value: 50.5, count: 1 },
    ]);
  });
});

describe('Ventas · devolución', () => {
  const returnable: ReturnableRecord[] = [
    { sku: 'GPU-RTX', name: 'Tarjeta de video', unit: 'u', sold: 2, returned: 0, unitPrice: 4000, discountPercent: 0 },
    { sku: 'CBL-HDMI', name: 'Cable HDMI', unit: 'u', sold: 3, returned: 1, unitPrice: 100, discountPercent: 10 },
    { sku: 'CBL-HDMI', name: 'Cable HDMI', unit: 'u', sold: 1, returned: 0, unitPrice: 120, discountPercent: 0 },
  ];
  const lines: SaleLineRecord[] = [
    { sku: 'GPU-RTX', name: 'Tarjeta de video', quantity: 2, unit: 'u', unitPrice: 4000, discountPercent: 0, amount: 8000, serials: ['SN-2', 'SN-1'] },
    { sku: 'CBL-HDMI', name: 'Cable HDMI', quantity: 3, unit: 'u', unitPrice: 100, discountPercent: 10, amount: 270, serials: null },
  ];
  const draft = buildReturnDraft(returnable, lines);

  it('junta las líneas del mismo producto y trae sus series', () => {
    expect(draft.map((line) => [line.sku, line.sold, line.returned, line.available, line.mixedPrices, line.serials])).toEqual([
      ['GPU-RTX', 2, 0, 2, false, ['SN-1', 'SN-2']],
      ['CBL-HDMI', 4, 1, 3, true, []],
    ]);
  });

  it('estima el reembolso tomando las líneas en orden y redondeando cada una', () => {
    const cable = draft[1];
    expect(lineRefund(cable, 2)).toBe(180);
    // 2 de la primera línea (90 c/u) y 1 de la segunda (120).
    expect(lineRefund(cable, 3)).toBe(300);
    expect(estimatedRefund(draft, { 'CBL-HDMI': 1 }, { 'GPU-RTX': ['SN-1'] })).toBe(4090);
  });

  it('valida como el servidor: algo que devolver, no más de lo que queda, motivo y medio de reembolso', () => {
    expect(returnProblems(draft, {}, {}, '', '')).toEqual({
      bySku: {},
      lines: 'Indique qué productos se devuelven: una cantidad o las series.',
      reason: 'Indique el motivo de la devolución.',
      method: 'Elija el medio con el que se reembolsa.',
    });
    expect(returnProblems(draft, { 'CBL-HDMI': 4 }, {}, 'Cambio', 'EFECTIVO')?.bySku).toEqual({ 'CBL-HDMI': 'Puede devolver hasta 3 u.' });
    expect(returnProblems(draft, { 'CBL-HDMI': 1 }, {}, 'Cambio', 'EFECTIVO')).toBeNull();
  });

  it('arma el pedido con la forma EXACTA del contrato (series solo en los productos que las llevan)', () => {
    expect(toReturnPayload('F-CM-000102', draft, { 'CBL-HDMI': 2, 'GPU-RTX': 5 }, { 'GPU-RTX': ['SN-2'] }, ' Producto con falla ', 'EFECTIVO', true)).toEqual({
      invoiceNumber: 'F-CM-000102',
      reason: 'Producto con falla',
      refundPaymentMethodCode: 'EFECTIVO',
      lines: [
        { sku: 'GPU-RTX', quantity: 1, serials: ['SN-2'] },
        { sku: 'CBL-HDMI', quantity: 2, serials: null },
      ],
      defective: true,
    });
  });
});

describe('Ventas · descarga del PDF', () => {
  it('lee el base64 y limpia el nombre del archivo', () => {
    expect(Array.from(base64ToBytes(btoa('%PDF')) ?? [])).toEqual([37, 80, 68, 70]);
    expect(base64ToBytes('no es base64 %%%')).toBeNull();
    expect(safeFileName('../facturas/factura 55.pdf', 'x.pdf')).toBe('factura-55.pdf');
    expect(safeFileName('', 'factura-55.pdf')).toBe('factura-55.pdf');
  });
});
