// Módulo «Series» · funciones puras: el pedido de la lista (siempre con todos los parámetros), los filtros de la página,
// la garantía a la vista, la lectura de las series escaneadas o pegadas (IMEI con Luhn), las opciones de las listas, los
// enlaces a Garantías y Ventas, el CSV y la estadística. Sin React ni red.

import { describe, expect, it } from 'vitest';
import {
  SERIALS_CSV,
  SERIAL_FILTERS,
  STATUS_FILTER_OPTIONS,
  branchOptions,
  canDisposeOf,
  canOpenClaimFor,
  claimPath,
  customerSalesPath,
  eventDetail,
  filterSerials,
  isTruncated,
  isValidImei,
  monthsText,
  openClaimOf,
  openClaimPath,
  originText,
  parseSerialList,
  plainMessage,
  productOptions,
  reasonProblem,
  reasonText,
  registerProductOptions,
  searchRequest,
  selectedProduct,
  summaryBars,
  takeOf,
  timeline,
  toSerialItems,
  traceWarranty,
  warrantyStateOf,
  warrantyText,
  type SerialRecord,
  type SerialTraceData,
  type TechProductRecord,
} from './serials';

function serial(overrides: Partial<SerialRecord>): SerialRecord {
  return {
    serial: 'SN-0001',
    kind: 'Serial',
    sku: 'NB-ASUS-01',
    product: 'Notebook ASUS TUF',
    status: 'InStock',
    branch: 'CM',
    warehouse: 'ALM-CM',
    receivedAt: '2026-09-01T14:00:00Z',
    soldAt: null,
    invoiceNumber: null,
    customer: null,
    warrantyUntil: null,
    ...overrides,
  };
}

function product(overrides: Partial<TechProductRecord>): TechProductRecord {
  return {
    sku: 'NB-ASUS-01',
    name: 'Notebook ASUS TUF',
    category: 'Notebooks',
    categoryCode: 'NOTEBOOKS',
    brand: 'ASUS',
    price: 9500,
    stock: 3,
    trackSerials: true,
    serialKind: 'Serial',
    warrantyMonths: 12,
    keySpecs: '',
    platforms: [],
    imageId: null,
    ...overrides,
  };
}

const TODAY = '2026-09-29';

describe('Series · pedido de la lista', () => {
  it('sin filtros envía todos los parámetros («sin filtro» = null) y las 1.000 más recientes', () => {
    expect(searchRequest(SERIAL_FILTERS)).toEqual({ text: null, status: null, sku: null, max: 1000 });
  });

  it('lleva la búsqueda, el estado, el producto y la cantidad; un estado o una cantidad rara no filtran', () => {
    expect(searchRequest({ q: '  sn-77 ', estado: 'InRma', producto: ' NB-ASUS-01 ', registros: '500' })).toEqual({
      text: 'sn-77',
      status: 'InRma',
      sku: 'NB-ASUS-01',
      max: 500,
    });
    expect(searchRequest({ q: '', estado: 'Robada', producto: '', registros: '9' })).toEqual({ text: null, status: null, sku: null, max: 1000 });
    expect(takeOf('5000')).toBe(5000);
    expect(isTruncated(1000, '1000')).toBe(true);
    expect(isTruncated(999, '1000')).toBe(false);
  });

  it('el filtro de estado no ofrece «Reservada» (como el escritorio)', () => {
    expect(STATUS_FILTER_OPTIONS.map((option) => option.value)).toEqual(['InStock', 'Sold', 'InTransit', 'Returned', 'InRma', 'ReturnedToSupplier', 'Scrapped']);
  });
});

describe('Series · garantía y filtros de la página', () => {
  it('la garantía se compara con hoy (la fecha de fin la calcula el servidor)', () => {
    expect(warrantyStateOf(null, TODAY)).toBe('sin');
    expect(warrantyStateOf('2026-09-29', TODAY)).toBe('vigente');
    expect(warrantyStateOf('2026-09-28', TODAY)).toBe('vencida');
    expect(warrantyText('2027-03-15', TODAY)).toBe('Hasta 15/03/2027');
    expect(warrantyText('2026-03-15', TODAY)).toBe('Venció el 15/03/2026');
    expect(warrantyText(null, TODAY)).toBe('—');
  });

  it('filtra por sucursal, tipo, garantía y fecha de ingreso (días de La Paz)', () => {
    const items = toSerialItems(
      [
        serial({ serial: 'A', branch: 'CM', receivedAt: '2026-09-02T03:00:00Z' }),
        serial({ serial: 'B', branch: 'CB', kind: 'Imei', status: 'Sold', warrantyUntil: '2027-01-01', receivedAt: '2026-08-01T14:00:00Z' }),
        serial({ serial: 'C', branch: 'CB', status: 'Sold', warrantyUntil: '2026-01-01', receivedAt: '2026-08-15T14:00:00Z' }),
      ],
      TODAY,
    );
    const keys = (filters: Partial<typeof SERIAL_FILTERS>) => filterSerials(items, { ...SERIAL_FILTERS, ...filters }).map((item) => item.row.serial);
    expect(keys({})).toEqual(['A', 'B', 'C']);
    expect(keys({ sucursal: 'CB' })).toEqual(['B', 'C']);
    expect(keys({ tipo: 'Imei' })).toEqual(['B']);
    expect(keys({ garantia: 'vigente' })).toEqual(['B']);
    expect(keys({ garantia: 'vencida' })).toEqual(['C']);
    expect(keys({ garantia: 'sin' })).toEqual(['A']);
    // 2026-09-02T03:00Z es el 1 de septiembre en La Paz.
    expect(keys({ desde: '2026-09-01', hasta: '2026-09-01' })).toEqual(['A']);
    expect(items[0].location).toBe('CM · ALM-CM');
  });

  it('las sucursales de la lista llevan el nombre que conoce la sesión', () => {
    const items = toSerialItems([serial({ branch: 'CM' }), serial({ serial: 'X', branch: 'SC' }), serial({ serial: 'Y', branch: null })], TODAY);
    expect(branchOptions(items, [{ code: 'CM', name: 'Casa Matriz' }])).toEqual([
      { value: 'CM', label: 'CM · Casa Matriz' },
      { value: 'SC', label: 'SC' },
    ]);
    expect(branchOptions([], [], 'CB')).toEqual([{ value: 'CB', label: 'CB' }]);
  });
});

describe('Series · productos', () => {
  const catalog = [
    product({ sku: 'NB-ASUS-01', name: 'Notebook ASUS TUF' }),
    product({ sku: 'CEL-01', name: 'Celular Galaxy', serialKind: 'Imei', stock: 0 }),
    product({ sku: 'MOUSE-01', name: 'Mouse gamer', trackSerials: false, stock: 10 }),
    product({ sku: 'CABLE-01', name: 'Cable HDMI', trackSerials: false, stock: 0 }),
  ];

  it('el filtro ofrece los productos que llevan serie; sin catálogo, los de la lista', () => {
    expect(productOptions(catalog, []).map((option) => [option.value, option.description])).toEqual([
      ['NB-ASUS-01', 'NB-ASUS-01 · lleva Serie · 3 en stock'],
      ['CEL-01', 'CEL-01 · lleva IMEI · 0 en stock'],
    ]);
    const items = toSerialItems([serial({ sku: 'B', product: 'Beta' }), serial({ sku: 'A', product: 'Alfa' }), serial({ serial: 'Z', sku: 'A', product: 'Alfa' })], TODAY);
    expect(productOptions(undefined, items).map((option) => option.value)).toEqual(['A', 'B']);
    expect(selectedProduct('A', [], items)).toEqual({ value: 'A', label: 'Alfa', description: 'A' });
    expect(selectedProduct('', [], items)).toBeNull();
  });

  it('para registrar series ofrece los que llevan serie o tienen stock (como el escritorio)', () => {
    expect(registerProductOptions(catalog).map((option) => option.value)).toEqual(['CEL-01', 'MOUSE-01', 'NB-ASUS-01']);
  });
});

describe('Series · registrar series (lo escaneado o pegado)', () => {
  it('una por línea o separadas por comas, en mayúsculas, sin repetidas ni con espacios', () => {
    expect(parseSerialList('sn-1\nSN-2, sn-3;\n\n  sn-1  \nSN 4', 'Serial')).toEqual({
      serials: ['SN-1', 'SN-2', 'SN-3'],
      problems: ['La serie SN-1 está repetida.', 'La serie «SN 4» no puede tener espacios, comas ni punto y coma.'],
    });
    expect(parseSerialList('A'.repeat(81), 'Serial').problems[0]).toMatch(/demasiado larga/);
  });

  it('el IMEI se guarda solo con dígitos y debe tener 15 con el dígito de Luhn correcto', () => {
    expect(isValidImei('490154203237518')).toBe(true);
    expect(isValidImei('49-015420-323751-8')).toBe(true);
    expect(isValidImei('490154203237519')).toBe(false);
    expect(isValidImei('49015420323751')).toBe(false);
    expect(parseSerialList('49-015420-323751-8\n490154203237519', 'Imei')).toEqual({
      serials: ['490154203237518'],
      problems: ['El IMEI «490154203237519» no es válido: debe tener 15 dígitos y el dígito verificador correcto.'],
    });
  });
});

describe('Series · detalle', () => {
  it('el recuadro de garantía usa los textos del escritorio', () => {
    expect(traceWarranty({ warrantyMonths: 0, inWarranty: false, serial: { warrantyUntil: null } }).title).toBe('El producto no tiene garantía');
    expect(traceWarranty({ warrantyMonths: 12, inWarranty: false, serial: { warrantyUntil: null } })).toMatchObject({ title: 'Garantía de 1 año (corre desde la venta)', tone: 'info' });
    expect(traceWarranty({ warrantyMonths: 6, inWarranty: true, serial: { warrantyUntil: '2027-03-15' } })).toEqual({
      title: 'En garantía hasta el 15/03/2027',
      detail: '6 meses desde la venta (se calcula al consultar: fecha de la venta + meses del producto).',
      tone: 'success',
    });
    expect(traceWarranty({ warrantyMonths: 24, inWarranty: false, serial: { warrantyUntil: '2026-01-10' } })).toMatchObject({ title: 'Garantía vencida el 10/01/2026', tone: 'danger' });
    expect([monthsText(1), monthsText(18), monthsText(36)]).toEqual(['1 mes', '18 meses', '3 años']);
  });

  it('la línea de tiempo va de lo más reciente a lo más antiguo y cada hecho dice dónde, con qué documento y quién', () => {
    const events = [
      { occurredAt: '2026-09-01T10:00:00Z', action: 'Received' as const, branch: 'CM', documentNumber: 'R-CM-000010', note: null, user: 'Bruno Mamani' },
      { occurredAt: '2026-09-20T10:00:00Z', action: 'Sold' as const, branch: 'CM', documentNumber: 'F-CM-000123', note: null, user: null },
    ];
    expect(timeline(events).map((event) => event.action)).toEqual(['Sold', 'Received']);
    expect(eventDetail(events[0])).toBe('CM · R-CM-000010 · Bruno Mamani');
    expect(eventDetail(events[1])).toBe('CM · F-CM-000123');
    expect(originText({ supplier: 'Distribuidora Andina', receiptNumber: 'R-CM-000010' })).toBe('Proveedor Distribuidora Andina · recepción R-CM-000010');
    expect(originText({ supplier: null, receiptNumber: null })).toBe('');
  });

  it('el caso abierto lo dice el servidor; sin esa consulta, la bitácora de la unidad', () => {
    const claims = [
      { serial: 'SN-1', status: 'Delivered', number: 'RMA-CM-000001' },
      { serial: 'SN-1', status: 'Diagnosing', number: 'RMA-CM-000002' },
    ] as unknown as SerialTraceData['claims'];
    expect(openClaimOf('SN-1', { openClaim: null }, { claims })).toBeNull();
    expect(openClaimOf('SN-1', undefined, { claims })).toBe('RMA-CM-000002');
    expect(openClaimOf('SN-1', undefined, undefined)).toBeNull();
  });

  it('abrir un caso: vendida o devuelta; dar destino: devuelta o en garantía', () => {
    expect(['InStock', 'Sold', 'Returned', 'InRma'].map(canOpenClaimFor)).toEqual([false, true, true, false]);
    expect(['InStock', 'Sold', 'Returned', 'InRma', 'Scrapped'].map(canDisposeOf)).toEqual([false, false, true, true, false]);
  });

  it('los enlaces a Garantías y Ventas llevan sus parámetros codificados', () => {
    expect(openClaimPath('SN 1/2', 'NB-01')).toBe('garantias?abrir=1&serie=SN+1%2F2&sku=NB-01');
    expect(openClaimPath('SN-1', null)).toBe('garantias?abrir=1&serie=SN-1');
    expect(claimPath('RMA-CM-000002')).toBe('garantias?ver=RMA-CM-000002');
    expect(customerSalesPath('C0002')).toBe('ventas?cliente=C0002');
  });
});

describe('Series · destino, mensajes, CSV y estadística', () => {
  it('el motivo sale de la lista o se escribe', () => {
    expect(reasonText('Equipo irreparable', 'x')).toBe('Equipo irreparable');
    expect(reasonText('otro', '  Se quemó  ')).toBe('Se quemó');
    expect(reasonProblem('ab')).toBe('Indique el motivo (al menos 3 caracteres).');
    expect(reasonProblem('abc')).toBeNull();
    expect(reasonProblem('x'.repeat(251))).toMatch(/hasta 250/);
    expect(plainMessage('✔ Serie SN-1: dada de baja.')).toBe('Serie SN-1: dada de baja.');
  });

  it('el CSV lleva las columnas en palabras', () => {
    expect(SERIALS_CSV.map((column) => column.header)).toEqual([
      'Serie o IMEI',
      'Tipo',
      'SKU',
      'Producto',
      'Estado',
      'Sucursal',
      'Almacén',
      'Ingresó',
      'Vendida',
      'Factura',
      'Cliente',
      'Garantía hasta',
      'Garantía',
    ]);
    const [item] = toSerialItems([serial({ status: 'Sold', kind: 'Imei', warrantyUntil: '2027-01-01', customer: 'Ana' })], TODAY);
    expect(SERIALS_CSV.map((column) => column.value(item)).filter((value) => typeof value === 'string')).toEqual([
      'SN-0001',
      'IMEI',
      'NB-ASUS-01',
      'Notebook ASUS TUF',
      'Vendida',
      'CM',
      'ALM-CM',
      'Ana',
      '01/01/2027',
      'Garantía vigente',
    ]);
  });

  it('las barras por estado agregan «En tránsito o reservadas» solo si hay', () => {
    expect(summaryBars({ total: 10, inStock: 4, inStockProducts: 2, sold: 3, soldInWarranty: 2, inRmaOrReturned: 2, out: 1 })).toEqual([
      { label: 'En stock', value: 4 },
      { label: 'Vendidas', value: 3 },
      { label: 'En garantía (RMA) o devueltas', value: 2 },
      { label: 'Bajas y devueltas al proveedor', value: 1 },
    ]);
    expect(summaryBars({ total: 12, inStock: 4, inStockProducts: 2, sold: 3, soldInWarranty: 2, inRmaOrReturned: 2, out: 1 }).slice(-1)).toEqual([{ label: 'En tránsito o reservadas', value: 2 }]);
  });
});
