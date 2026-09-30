// Módulo «Órdenes de compra» · pruebas de las funciones puras: estados y acciones por estado, sucursal del número,
// filtros, resumen, recepción (series) y los pedidos exactos de la orden nueva, la recepción y la factura del proveedor.

import { describe, expect, it } from 'vitest';
import {
  addProduct,
  branchOfNumber,
  createOrderPayload,
  defaultDelivery,
  draftTotal,
  emptyDraft,
  filterInvoices,
  filterOrders,
  hasOrderProblems,
  invoiceDraftFor,
  invoicePayload,
  invoiceProblems,
  invoiceRange,
  matchesState,
  orderProblems,
  parseSerials,
  pendingLines,
  pendingValue,
  plainMessage,
  productChoices,
  proposedInvoiceTotal,
  purchaseSummary,
  receivePayload,
  serialsProblem,
  suggestedQuantity,
  supplierOptions,
  toOrderItems,
  ORDER_FILTERS,
  type CatalogRecord,
  type InvoiceRecord,
  type OrderDetailData,
  type OrderRecord,
  type SupplierRecord,
} from './purchasing';

const TODAY = '2026-09-29';

function order(overrides: Partial<OrderRecord>): OrderRecord {
  return {
    id: 'o1',
    number: 'OC-CM-000001',
    supplierCode: 'P001',
    supplier: 'Distribuidora Andina',
    orderDate: '2026-09-20',
    expectedDate: '2026-09-25',
    status: 'Draft',
    lines: 2,
    total: 1000,
    receivedPercent: 0,
    notes: null,
    ...overrides,
  };
}

function product(overrides: Partial<CatalogRecord>): CatalogRecord {
  return {
    barcode: null,
    binCode: null,
    category: 'Periféricos',
    categoryCode: 'PER',
    description: null,
    hasImage: false,
    isActive: true,
    maximum: 20,
    minimum: 5,
    name: 'Mouse',
    salePrice: 150,
    sku: 'MOU-01',
    supplier: 'Distribuidora Andina',
    supplierCode: 'P001',
    unit: 'UND',
    unitCost: 80,
    variantId: 'v1',
    ...overrides,
  };
}

describe('Órdenes de compra · lista', () => {
  it('saca la sucursal del número y dice qué se puede hacer según el estado', () => {
    expect(branchOfNumber('OC-CM-000012')).toBe('CM');
    expect(branchOfNumber('RC-cb-000001')).toBe('CB');
    expect(branchOfNumber('sin número')).toBeNull();
    const [draft, approved, partial, done] = toOrderItems(
      [
        order({ id: 'a', status: 'Draft' }),
        order({ id: 'b', status: 'Approved', expectedDate: '2026-09-28' }),
        order({ id: 'c', status: 'PartiallyReceived', expectedDate: '2026-10-05' }),
        order({ id: 'd', status: 'Received', expectedDate: '2026-09-01' }),
      ],
      TODAY,
    );
    expect(draft.abilities).toEqual({ approve: true, receive: false, cancel: true });
    expect(approved.abilities).toEqual({ approve: false, receive: true, cancel: true });
    expect(partial.abilities).toEqual({ approve: false, receive: true, cancel: false });
    expect(done.abilities).toEqual({ approve: false, receive: false, cancel: false });
    expect([draft.overdue, approved.overdue, partial.overdue, done.overdue]).toEqual([false, true, false, false]);
  });

  it('filtra por estado (también «por recibir» y «abiertas»), proveedor, sucursal, fechas y búsqueda', () => {
    expect(matchesState('Approved', 'por-recibir')).toBe(true);
    expect(matchesState('Draft', 'por-recibir')).toBe(false);
    expect(matchesState('Draft', 'abiertas')).toBe(true);
    expect(matchesState('Received', 'abiertas')).toBe(false);
    const items = toOrderItems(
      [
        order({ id: 'a', number: 'OC-CM-000001', status: 'Draft', supplierCode: 'P001', orderDate: '2026-09-01', notes: 'Urgente' }),
        order({ id: 'b', number: 'OC-CB-000002', status: 'Approved', supplierCode: 'P002', supplier: 'Tecno Import', orderDate: '2026-09-20' }),
      ],
      TODAY,
    );
    const keys = (filters: Partial<typeof ORDER_FILTERS>) => filterOrders(items, { ...ORDER_FILTERS, ...filters }).map((item) => item.key);
    expect(keys({ estado: 'por-recibir' })).toEqual(['b']);
    expect(keys({ proveedor: 'p001' })).toEqual(['a']);
    expect(keys({ sucursal: 'CB' })).toEqual(['b']);
    expect(keys({ desde: '2026-09-10' })).toEqual(['b']);
    expect(keys({ q: 'urgente' })).toEqual(['a']);
    expect(keys({ q: 'tecno' })).toEqual(['b']);
  });

  it('la lista de proveedores une el directorio y los de las órdenes', () => {
    const suppliers = [{ code: 'P002', name: 'Tecno Import' } as SupplierRecord];
    const items = toOrderItems([order({ supplierCode: 'P009', supplier: 'Viejo proveedor' })], TODAY);
    expect(supplierOptions(suppliers, items)).toEqual([
      { value: 'P002', label: 'Tecno Import (P002)' },
      { value: 'P009', label: 'Viejo proveedor (P009)' },
    ]);
  });

  it('el resumen cuenta borradores, por recibir (y los vencidos), lo recibido del mes y los proveedores', () => {
    const summary = purchaseSummary(
      [
        order({ status: 'Draft', total: 100 }),
        order({ status: 'Approved', total: 200, expectedDate: '2026-09-29', supplierCode: 'P002' }),
        order({ status: 'PartiallyReceived', total: 300, expectedDate: '2026-10-10' }),
        order({ status: 'Received', total: 400, orderDate: '2026-09-02' }),
        order({ status: 'Received', total: 999, orderDate: '2026-08-30' }),
      ],
      TODAY,
    );
    expect(summary).toEqual({
      drafts: { count: 1, total: 100 },
      pending: { count: 2, total: 500, overdue: 1 },
      receivedThisMonth: { count: 1, total: 400 },
      suppliers: 2,
      orders: 5,
    });
  });

  it('quita la marca del mensaje del servidor', () => {
    expect(plainMessage('✔ Factura 12 registrada.')).toBe('Factura 12 registrada.');
  });
});

describe('Órdenes de compra · recepción', () => {
  const detail: OrderDetailData = {
    order: order({ status: 'PartiallyReceived' }),
    receipts: ['RC-CM-000001'],
    lines: [
      { lineId: 'l1', sku: 'MOU-01', name: 'Mouse', unit: 'UND', quantity: 10, unitCost: 80, subtotal: 800, received: 10 },
      { lineId: 'l2', sku: 'CEL-01', name: 'Celular', unit: 'UND', quantity: 3, unitCost: 1000, subtotal: 3000, received: 1 },
    ],
  };

  it('muestra solo lo pendiente y su valor', () => {
    expect(pendingLines(detail).map((line) => line.sku)).toEqual(['CEL-01']);
    expect(pendingValue(pendingLines(detail))).toBe(2000);
  });

  it('lee las series (una por renglón o con comas) y valida la cantidad, las repetidas y el IMEI', () => {
    expect(parseSerials(' SN1\nSN2 , SN3;\n\n')).toEqual(['SN1', 'SN2', 'SN3']);
    expect(serialsProblem(['A'], 2, 'Serial')).toBe('Escriba 2 series (una por unidad): hay 1.');
    expect(serialsProblem(['A', 'a'], 2, 'Serial')).toBe('La serie a está repetida.');
    expect(serialsProblem(['12345', '356938035643809'], 2, 'Imei')).toBe('El IMEI 12345 no es válido: son 15 dígitos.');
    expect(serialsProblem(['356938035643809', '356938035643817'], 2, 'Imei')).toBeNull();
  });

  it('arma el pedido exacto de la recepción', () => {
    expect(receivePayload('o1', '  FAC-99 ', new Map([['CEL-01', ['S1', 'S2']]]))).toEqual({ id: 'o1', supplierDocument: 'FAC-99', serials: [{ sku: 'CEL-01', serials: ['S1', 'S2'] }] });
    expect(receivePayload('o1', '', new Map())).toEqual({ id: 'o1', supplierDocument: null, serials: null });
  });
});

describe('Órdenes de compra · orden nueva', () => {
  it('propone la cantidad del mínimo al máximo y el costo del catálogo; si se repite suma 1', () => {
    expect(suggestedQuantity({ minimum: 5, maximum: 20 })).toBe(15);
    expect(suggestedQuantity({ minimum: 0, maximum: 0 })).toBe(1);
    const units = [{ code: 'UND', name: 'Unidad', allowsDecimals: false }];
    let draft = addProduct(emptyDraft('P001'), product({}), units);
    expect(draft.lines).toEqual([{ sku: 'MOU-01', name: 'Mouse', unit: 'UND', allowsDecimals: false, quantity: 15, unitCost: 80 }]);
    draft = addProduct(draft, product({}), units);
    expect(draft.lines[0].quantity).toBe(16);
    expect(draftTotal(draft)).toBe(1280);
  });

  it('ofrece los productos activos del proveedor (o todos) que todavía no están en la orden', () => {
    const catalog = [product({}), product({ sku: 'TEC-01', name: 'Teclado', supplierCode: 'P002' }), product({ sku: 'X', name: 'Inactivo', isActive: false })];
    expect(productChoices(catalog, { supplierCode: 'P001', onlySupplier: true, lines: [] }).map((option) => option.value)).toEqual(['MOU-01']);
    expect(productChoices(catalog, { supplierCode: 'P001', onlySupplier: false, lines: [] }).map((option) => option.value)).toEqual(['MOU-01', 'TEC-01']);
    expect(
      productChoices(catalog, { supplierCode: 'P001', onlySupplier: false, lines: [{ sku: 'MOU-01', name: '', unit: '', allowsDecimals: true, quantity: 1, unitCost: 1 }] }).map(
        (option) => option.value,
      ),
    ).toEqual(['TEC-01']);
  });

  it('valida proveedor, productos, cantidades enteras, costos y la fecha', () => {
    const empty = orderProblems(emptyDraft(), TODAY);
    expect(empty.supplierCode).toBe('Elija el proveedor.');
    expect(empty.lines).toBe('Agregue al menos un producto a la orden.');
    const draft = {
      ...emptyDraft('P001'),
      expectedDate: '2026-09-01',
      lines: [
        { sku: 'A', name: 'A', unit: 'UND', allowsDecimals: false, quantity: 1.5, unitCost: -1 },
        { sku: 'B', name: 'B', unit: 'KG', allowsDecimals: true, quantity: null, unitCost: null },
      ],
    };
    const problems = orderProblems(draft, TODAY);
    expect(problems.expectedDate).toBe('La entrega esperada no puede ser anterior a hoy.');
    expect(problems.byLine).toEqual({
      A: { quantity: 'En UND la cantidad es entera.', unitCost: 'El costo no puede ser negativo.' },
      B: { quantity: 'Indique la cantidad.', unitCost: 'Indique el costo unitario.' },
    });
    expect(hasOrderProblems(problems)).toBe(true);
  });

  it('arma el pedido exacto de `CreatePurchaseOrderCommand`', () => {
    const draft = { ...emptyDraft('P001'), notes: '  Para la campaña ', lines: [{ sku: 'MOU-01', name: 'Mouse', unit: 'UND', allowsDecimals: false, quantity: 10, unitCost: 80 }] };
    expect(createOrderPayload(draft)).toEqual({ supplierCode: 'P001', expectedDate: null, notes: 'Para la campaña', lines: [{ sku: 'MOU-01', quantity: 10, unitCost: 80 }] });
    expect(createOrderPayload({ ...draft, expectedDate: '2026-10-05', notes: '' })).toMatchObject({ expectedDate: '2026-10-05', notes: null });
    expect(defaultDelivery(TODAY, 3)).toBe('2026-10-02');
    expect(defaultDelivery(TODAY, 0)).toBe('2026-09-30');
  });
});

describe('Órdenes de compra · facturas de proveedores', () => {
  const receipt = { receiptNumber: 'RC-CM-000001', receivedOn: '2026-09-20', supplierCode: 'P001', supplier: 'Distribuidora Andina', total: 870 };

  it('propone el importe con IVA de una recepción al costo neto y valida el formulario', () => {
    expect(proposedInvoiceTotal(870)).toBe(1000);
    const draft = invoiceDraftFor(receipt);
    expect(draft).toMatchObject({ invoiceDate: '2026-09-20', totalAmount: 1000, discounts: 0, notSubjectToVat: 0, purchaseType: '1' });
    expect(invoiceProblems(draft, TODAY)).toEqual({
      invoiceNumber: 'Indique el número de la factura del proveedor.',
      authorizationCode: 'Indique el CUF (o el código de autorización) de la factura.',
    });
    expect(invoiceProblems({ ...draft, invoiceNumber: '1', authorizationCode: 'X', invoiceDate: '2026-10-01', totalAmount: 0 }, TODAY)).toEqual({
      invoiceDate: 'La fecha de la factura no puede ser posterior a hoy.',
      totalAmount: 'El importe total debe ser mayor que 0.',
    });
  });

  it('arma el pedido exacto de `RegisterSupplierInvoiceCommand`', () => {
    const draft = { ...invoiceDraftFor(receipt), invoiceNumber: ' 4521 ', authorizationCode: ' ABC123 ', controlCode: '' };
    expect(invoicePayload('RC-CM-000001', draft)).toEqual({
      receiptNumber: 'RC-CM-000001',
      invoiceNumber: '4521',
      authorizationCode: 'ABC123',
      invoiceDate: '2026-09-20',
      totalAmount: 1000,
      discounts: 0,
      notSubjectToVat: 0,
      purchaseType: 1,
      controlCode: null,
    });
  });

  it('el rango del servidor lleva siempre las dos fechas; filtra por proveedor y búsqueda', () => {
    expect(invoiceRange({ desde: '', hasta: '' }, TODAY)).toEqual({ from: '2000-01-01', to: TODAY });
    expect(invoiceRange({ desde: '2026-09-01', hasta: '2026-09-15' }, TODAY)).toEqual({ from: '2026-09-01', to: '2026-09-15' });
    const invoices = [
      { id: '1', number: '4521', supplierCode: 'P001', supplier: 'Andina', supplierNit: '1020', receiptNumber: 'RC-CM-000001', authorizationCode: 'X' },
      { id: '2', number: '77', supplierCode: 'P002', supplier: 'Tecno', supplierNit: null, receiptNumber: null, authorizationCode: 'Y' },
    ] as InvoiceRecord[];
    const filters = { q: '', proveedor: '', desde: '', hasta: '' };
    expect(filterInvoices(invoices, { ...filters, proveedor: 'P002' }).map((row) => row.id)).toEqual(['2']);
    expect(filterInvoices(invoices, { ...filters, q: 'rc-cm' }).map((row) => row.id)).toEqual(['1']);
  });
});
