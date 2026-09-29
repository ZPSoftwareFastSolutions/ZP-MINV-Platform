// Módulo «Armador de PC» · funciones puras: piezas por ranura (reemplazar, sumar, cantidades, límites), el pedido de
// `SavePcBuildCommand`, candidatos (marca, precio, solo compatibles), la categoría propuesta de los extras, el resumen de la
// compatibilidad del servidor, estados, vigencia y acciones de cada estado, y la lista de cotizaciones (servidor, página,
// CSV y resumen).

import { describe, expect, it } from 'vitest';
import { buildCsv } from '@/4-presentation/panel/lib';
import {
  BUILD_FILTERS,
  CSV_COLUMNS,
  EMPTY_CANDIDATE_FILTERS,
  MAX_LINES,
  NO_BRAND,
  SLOTS,
  addPart,
  autoName,
  availabilityText,
  brandOptions,
  buildNote,
  buildState,
  builderPath,
  buildsOverview,
  buildsRequest,
  canAddLine,
  canCancelBuild,
  canPrintBuild,
  canPublishBuild,
  canReleaseBuild,
  canReserveBuild,
  canSellBuild,
  canUnpublishBuild,
  changeQuantity,
  checkSummary,
  compatibilityText,
  customerCodeByName,
  customerOptions,
  defaultCategory,
  filterBuilds,
  filterCandidates,
  isEditable,
  nextRequiredSlot,
  partsFromItems,
  partsTotal,
  psuLoad,
  releaseReason,
  removePart,
  reserveHours,
  sellPath,
  slotSummary,
  sortedIssues,
  toBuildItems,
  toInputs,
  toSavePayload,
  validityText,
  withLivePrices,
  type BuildRecord,
  type CandidateRecord,
  type CheckData,
  type CustomerRecord,
  type PartLine,
} from './builder';

const NOW = new Date('2026-09-29T14:00:00Z');
const hours = (value: number) => new Date(NOW.getTime() + value * 3_600_000).toISOString();

function candidate(overrides: Partial<CandidateRecord>): CandidateRecord {
  return { sku: 'SKU', name: 'Pieza', brand: 'AMD', price: 100, stock: 5, isCompatible: true, reason: null, imageId: null, keySpecs: [], ...overrides };
}

function build(overrides: Partial<BuildRecord>): BuildRecord {
  return {
    id: 'b-1',
    number: 'ARM-CM-000010',
    name: 'PC gamer',
    branchCode: 'CM',
    customer: null,
    status: 'Quoted',
    validUntil: '2026-10-06',
    isExpired: false,
    total: 7000,
    items: 6,
    isCompatible: true,
    createdAt: hours(-24),
    invoiceNumber: null,
    quotedWithErrors: false,
    channel: 'Desktop',
    contactName: null,
    contactPhone: null,
    contactEmail: null,
    reservedUntil: null,
    publishedToWeb: false,
    cancelReason: null,
    notes: null,
    reserved: 0,
    kind: 'Build',
    buyerDocumentType: null,
    buyerDocumentNumber: null,
    buyerComplement: null,
    buyerName: null,
    ...overrides,
  };
}

function check(overrides: Partial<CheckData>): CheckData {
  return { items: [], issues: [], isCompatible: true, estimatedDrawW: 300, recommendedPsuW: 390, psuW: 650, total: 0, ...overrides };
}

describe('armador · piezas por ranura', () => {
  it('una ranura de una pieza reemplaza; una de varias suma unidades (hasta 16) o agrega una línea', () => {
    let parts = addPart([], 'Cpu', candidate({ sku: 'CPU-1', name: 'Ryzen 5 7600', price: 1650 }));
    parts = addPart(parts, 'Cpu', candidate({ sku: 'CPU-2', name: 'Core i5 14400', price: 1800 }));
    expect(parts.map((part) => part.sku)).toEqual(['CPU-2']);
    parts = addPart(parts, 'Ram', candidate({ sku: 'RAM-16', price: 600 }));
    parts = addPart(parts, 'Ram', candidate({ sku: 'RAM-16', price: 600 }));
    parts = addPart(parts, 'Ram', candidate({ sku: 'RAM-8', price: 300 }));
    expect(parts.map((part) => [part.slot, part.sku, part.quantity])).toEqual([
      ['Cpu', 'CPU-2', 1],
      ['Ram', 'RAM-16', 2],
      ['Ram', 'RAM-8', 1],
    ]);
    expect(partsTotal(parts)).toBe(1800 + 1200 + 300);
    expect(changeQuantity(parts, 'Ram', 'RAM-16', 50).find((part) => part.sku === 'RAM-16')?.quantity).toBe(16);
    expect(changeQuantity(parts, 'Ram', 'RAM-8', -5).find((part) => part.sku === 'RAM-8')?.quantity).toBe(1);
    expect(removePart(parts, 'Ram', 'RAM-8').map((part) => part.sku)).toEqual(['CPU-2', 'RAM-16']);
  });

  it('no pasa de 20 piezas distintas (sí suma unidades de una que ya está)', () => {
    const full: PartLine[] = Array.from({ length: MAX_LINES }, (_, index) => ({ slot: 'Peripheral', sku: `P-${index}`, name: 'x', unitPrice: 1, stock: 1, keySpecs: [], quantity: 1 }));
    expect(canAddLine(full, 'Peripheral', 'NUEVO')).toBe(false);
    expect(canAddLine(full, 'Peripheral', 'P-3')).toBe(true);
    expect(canAddLine(full, 'Cpu', 'CPU-1')).toBe(true);
    expect(addPart(full, 'Peripheral', candidate({ sku: 'NUEVO' }))).toHaveLength(MAX_LINES);
  });

  it('las piezas viajan en el orden de las ranuras, con ranura, SKU y cantidad', () => {
    const parts = addPart(addPart(addPart([], 'Case', candidate({ sku: 'CASE' })), 'Cpu', candidate({ sku: 'CPU' })), 'Ram', candidate({ sku: 'RAM' }));
    expect(toInputs(parts)).toEqual([
      { slot: 'Cpu', sku: 'CPU', quantity: 1 },
      { slot: 'Ram', sku: 'RAM', quantity: 1 },
      { slot: 'Case', sku: 'CASE', quantity: 1 },
    ]);
  });

  it('un armado guardado se carga con sus piezas (una sin ranura, entre los periféricos) y el informe actualiza precio y stock', () => {
    const parts = partsFromItems([
      { slot: 'Cpu', sku: 'CPU', name: 'Ryzen', quantity: 1, unitPrice: 1650, subtotal: 1650, stock: 3, imageId: null, keySpecs: ['AM5'] },
      { slot: null, sku: 'MOU', name: 'Mouse', quantity: 2, unitPrice: 350, subtotal: 700, stock: 9, imageId: null, keySpecs: [] },
    ]);
    expect(parts.map((part) => [part.slot, part.sku, part.quantity])).toEqual([
      ['Cpu', 'CPU', 1],
      ['Peripheral', 'MOU', 2],
    ]);
    const live = withLivePrices(parts, check({ items: [{ slot: 'Cpu', sku: 'CPU', name: 'Ryzen', quantity: 1, unitPrice: 1700, subtotal: 1700, stock: 1, imageId: null, keySpecs: [] }] }));
    expect(live[0]).toMatchObject({ unitPrice: 1700, stock: 1 });
    expect(live[1]).toMatchObject({ unitPrice: 350, stock: 9 });
  });

  it('propone el nombre, dice qué falta en cada ranura y la siguiente obligatoria vacía', () => {
    expect(autoName([])).toBe('PC a medida');
    const parts = addPart(addPart([], 'Cpu', candidate({ sku: 'CPU', name: 'Ryzen 5 7600 AM5' })), 'Gpu', candidate({ sku: 'GPU', name: 'GeForce RTX 4060 8 GB' }));
    expect(autoName(parts)).toBe('PC Ryzen 5 7600 + GeForce RTX 4060');
    expect(nextRequiredSlot(parts)).toBe('Motherboard');
    expect(slotSummary(parts, SLOTS[0])).toBe('1 pieza');
    expect(slotSummary(parts, SLOTS[1])).toBe('Obligatoria · elija una pieza');
    expect(slotSummary(parts, SLOTS[7])).toBe('Opcional');
  });
});

describe('armador · candidatos y compatibilidad', () => {
  const list = [
    candidate({ sku: 'A', brand: 'AMD', price: 1650 }),
    candidate({ sku: 'B', brand: 'Intel', price: 1800, isCompatible: false, reason: 'Otro socket' }),
    candidate({ sku: 'C', brand: null, price: 900 }),
  ];

  it('filtra en la página por marca (también «sin marca»), precio y solo compatibles', () => {
    expect(brandOptions(list)).toEqual([
      { value: 'AMD', label: 'AMD' },
      { value: 'Intel', label: 'Intel' },
      { value: NO_BRAND, label: 'Sin marca' },
    ]);
    const skus = (filters: Partial<typeof EMPTY_CANDIDATE_FILTERS>) => filterCandidates(list, { ...EMPTY_CANDIDATE_FILTERS, ...filters }).map((item) => item.sku);
    expect(skus({ brand: 'Intel' })).toEqual(['B']);
    expect(skus({ brand: NO_BRAND })).toEqual(['C']);
    expect(skus({ minPrice: 1000, maxPrice: 1700 })).toEqual(['A']);
    expect(skus({ onlyCompatible: true })).toEqual(['A', 'C']);
  });

  it('propone la categoría de los extras por el nombre de la ranura', () => {
    const categories = [
      { code: 'CPU', name: 'Procesadores' },
      { code: 'MON', name: 'Monitores' },
      { code: 'PER', name: 'Periféricos gamer' },
    ];
    expect(defaultCategory(SLOTS.find((slot) => slot.code === 'Monitor')!, categories)).toBe('MON');
    expect(defaultCategory(SLOTS.find((slot) => slot.code === 'Peripheral')!, categories)).toBe('PER');
    expect(defaultCategory(SLOTS.find((slot) => slot.code === 'Software')!, categories)).toBe('');
    expect(defaultCategory(SLOTS[0], categories)).toBe('');
  });

  it('el resumen muestra lo que dijo el servidor: errores, avisos o compatible, y la carga de la fuente', () => {
    expect(checkSummary(undefined, false)).toMatchObject({ title: 'Elija las piezas', tone: 'info' });
    expect(checkSummary(undefined, true)).toMatchObject({ title: 'Revisando la compatibilidad…', tone: 'info' });
    const issues = [
      { code: 'psu.low', isError: false, message: 'La fuente queda justa.' },
      { code: 'cpu.socket', isError: true, message: 'Otro socket.' },
    ];
    expect(checkSummary(check({ issues }), true)).toMatchObject({ title: '1 error de compatibilidad', tone: 'danger', errors: 1, warnings: 1 });
    expect(checkSummary(check({ issues: [issues[0]] }), true)).toMatchObject({ title: 'Compatible · 1 aviso', tone: 'warning' });
    expect(checkSummary(check({}), true)).toMatchObject({ title: 'Compatible', tone: 'success' });
    expect(sortedIssues(check({ issues })).map((issue) => issue.code)).toEqual(['cpu.socket', 'psu.low']);
    expect(psuLoad(check({ estimatedDrawW: 325, psuW: 650 }))).toBe(50);
    expect(psuLoad(check({ psuW: null }))).toBeNull();
  });

  it('la disponibilidad de una pieza cotizada', () => {
    expect(availabilityText({ stock: 2, quantity: 1 }, true).text).toBe('Reservada · 2 más disponibles');
    expect(availabilityText({ stock: 3, quantity: 1 }, false)).toEqual({ text: 'Disponible 3', tone: 'success' });
    expect(availabilityText({ stock: 0, quantity: 1 }, false)).toEqual({ text: 'Sin stock en la sucursal', tone: 'danger' });
  });
});

describe('armador · estados, vigencia y acciones', () => {
  it('el estado en palabras incluye la vigencia vencida y la reserva cumplida', () => {
    expect(buildState(build({ status: 'Draft' }), NOW)).toBe('Draft');
    expect(buildState(build({}), NOW)).toBe('Quoted');
    expect(buildState(build({ isExpired: true }), NOW)).toBe('QuotedExpired');
    expect(buildState(build({ status: 'Reserved', reservedUntil: hours(5) }), NOW)).toBe('Reserved');
    expect(buildState(build({ status: 'Reserved', reservedUntil: hours(-1) }), NOW)).toBe('ReservedExpired');
    expect(validityText(build({}))).toBe('Vigente hasta 06/10/2026');
    expect(validityText(build({ isExpired: true }))).toBe('Venció el 06/10/2026');
    expect(validityText(build({ status: 'Sold', invoiceNumber: 'F-CM-000120' }))).toBe('Venta F-CM-000120');
    expect(validityText(build({ status: 'Draft' }))).toBe('—');
    expect(compatibilityText(build({ isCompatible: false, quotedWithErrors: true }))).toEqual({ text: 'Con errores aceptados', tone: 'warning' });
    expect(compatibilityText(build({ isCompatible: false }))).toEqual({ text: 'Con errores', tone: 'danger' });
  });

  it('cada estado ofrece sus acciones (el servidor decide igual)', () => {
    const quoted = build({});
    const reserved = build({ status: 'Reserved', channel: 'Web', reservedUntil: hours(10) });
    const sold = build({ status: 'Sold', publishedToWeb: true });
    const draft = build({ status: 'Draft' });
    expect([canSellBuild(quoted), canReserveBuild(quoted), canPublishBuild(quoted), canCancelBuild(quoted), canPrintBuild(quoted)]).toEqual([true, true, true, true, true]);
    expect([canSellBuild(reserved), canReleaseBuild(reserved), canPublishBuild(reserved), canCancelBuild(reserved)]).toEqual([true, true, false, false]);
    expect([canSellBuild(sold), canPublishBuild(sold), canUnpublishBuild(sold), canPrintBuild(sold)]).toEqual([false, false, true, true]);
    expect([isEditable(draft), isEditable(quoted), isEditable(null), canPrintBuild(draft), canCancelBuild(draft)]).toEqual([true, false, true, false, true]);
    expect(canSellBuild(build({ isExpired: true }))).toBe(false);
    expect(canPublishBuild(build({ kind: 'Cart' }))).toBe(false);
    expect(sellPath('ARM-CM-000010')).toBe('caja?reserva=ARM-CM-000010');
    expect(builderPath('ARM-CM-000010')).toBe('armador/ARM-CM-000010');
    expect(builderPath(null)).toBe('armador/nuevo');
  });

  it('la nota del armado abierto explica qué pasa en cada estado', () => {
    expect(buildNote(null, NOW)).toMatch(/Elija las piezas por ranura/);
    expect(buildNote(build({ quotedWithErrors: true, publishedToWeb: true }), NOW)).toBe(
      'Precios congelados hasta el 06/10/2026 · cotizado con errores aceptados · publicado en la tienda web.',
    );
    expect(buildNote(build({ status: 'Reserved', channel: 'Web', reservedUntil: hours(10) }), NOW)).toMatch(/^Stock reservado hasta el 29\/09\/2026 20:00 .* tienda web\. Al cobrarla/);
    expect(buildNote(build({ status: 'Cancelled', cancelReason: 'El cliente desistió' }), NOW)).toBe('Armado anulado: El cliente desistió.');
  });

  it('las horas de la reserva (de la lista o escritas, de 1 a 720) y el motivo de la liberación', () => {
    expect(reserveHours('48', null)).toBe(48);
    expect(reserveHours('otra', 36)).toBe(36);
    expect(reserveHours('otra', 0)).toBeNull();
    expect(reserveHours('otra', 721)).toBeNull();
    expect(reserveHours('otra', null)).toBeNull();
    expect(releaseReason('_otro', '  Se arrepintió ')).toBe('Se arrepintió');
  });
});

describe('armador · guardar y clientes', () => {
  const parts = addPart(addPart([], 'Cpu', candidate({ sku: 'CPU-R5', name: 'Ryzen 5 7600' })), 'Ram', candidate({ sku: 'RAM-16' }));

  it('arma el pedido de SavePcBuildCommand con TODOS los parámetros', () => {
    expect(toSavePayload({ current: null, name: ' ', customerCode: '', parts, quote: false, validDays: '7', acceptIncompatible: false })).toEqual({
      id: null,
      name: 'PC Ryzen 5 7600',
      customerCode: null,
      items: [
        { slot: 'Cpu', sku: 'CPU-R5', quantity: 1 },
        { slot: 'Ram', sku: 'RAM-16', quantity: 1 },
      ],
      quote: false,
      validDays: 7,
      acceptIncompatible: false,
      kind: 'Build',
    });
    expect(toSavePayload({ current: { id: 'b-9' }, name: ' PC de Ana ', customerCode: 'CLI-0002', parts, quote: true, validDays: '15', acceptIncompatible: true })).toMatchObject({
      id: 'b-9',
      name: 'PC de Ana',
      customerCode: 'CLI-0002',
      quote: true,
      validDays: 15,
      acceptIncompatible: true,
    });
  });

  it('los clientes se ofrecen activos y sin el consumidor final; el de un armado guardado se busca por nombre', () => {
    const customer = (overrides: Partial<CustomerRecord>): CustomerRecord => ({
      code: 'CLI-0001',
      name: 'Zoe',
      category: 'General',
      categoryCode: 'GEN',
      email: null,
      phone: null,
      taxId: null,
      isActive: true,
      lastPurchase: null,
      purchases: 0,
      total: 0,
      ...overrides,
    });
    const list = [customer({ code: 'CF', name: 'Consumidor final' }), customer({ code: 'CLI-0002', name: 'Ana Rojas', taxId: '4455667' }), customer({ code: 'CLI-0003', name: 'Baja', isActive: false })];
    expect(customerOptions(list).map((option) => [option.value, option.description])).toEqual([['CLI-0002', 'CLI-0002 · NIT/CI 4455667']]);
    expect(customerCodeByName(list, 'Ana Rojas')).toBe('CLI-0002');
    expect(customerCodeByName(list, 'Consumidor final')).toBe('');
    expect(customerCodeByName(list, null)).toBe('');
  });
});

describe('armador · lista de cotizaciones', () => {
  const rows = [
    build({ number: 'ARM-CM-000010', name: 'PC gamer', customer: 'Ana Rojas' }),
    build({ number: 'ARM-CM-000011', status: 'Draft', name: 'Borrador oficina', total: 3000 }),
    build({ number: 'ARM-WEB-000012', status: 'Reserved', channel: 'Web', contactName: 'Luis Mamani', reservedUntil: hours(20), total: 9800 }),
    build({ number: 'ARM-CM-000013', status: 'Sold', invoiceNumber: 'F-CM-000120', publishedToWeb: true, total: 12500, createdAt: '2026-09-20T15:00:00Z' }),
    build({ number: 'ARM-CM-000014', isExpired: true, name: 'Cotización vieja' }),
    build({ number: 'RES-CM-000020', kind: 'Cart' }),
  ];
  const items = toBuildItems(rows, NOW);
  const numbers = (filters: Partial<typeof BUILD_FILTERS>) => filterBuilds(items, { ...BUILD_FILTERS, ...filters }).map((item) => item.row.number);

  it('solo armados de PC (los carritos son de Reservas), con cliente o contacto', () => {
    expect(items.map((item) => item.row.number)).toEqual(['ARM-CM-000010', 'ARM-CM-000011', 'ARM-WEB-000012', 'ARM-CM-000013', 'ARM-CM-000014']);
    expect(items.map((item) => item.client)).toEqual(['Ana Rojas', 'Sin cliente', 'Luis Mamani', 'Sin cliente', 'Sin cliente']);
  });

  it('pide al servidor solo armados, con el estado y el canal', () => {
    expect(buildsRequest({ estado: '', canal: '' })).toEqual({ status: null, channel: null, kind: 'Build' });
    expect(buildsRequest({ estado: 'Quoted', canal: 'Web' })).toEqual({ status: 'Quoted', channel: 'Web', kind: 'Build' });
    expect(buildsRequest({ estado: 'raro', canal: 'otro' })).toEqual({ status: null, channel: null, kind: 'Build' });
  });

  it('filtra por estado, vigencia, publicado, canal, fechas y búsqueda', () => {
    expect(numbers({ estado: 'Quoted' })).toEqual(['ARM-CM-000010', 'ARM-CM-000014']);
    expect(numbers({ vigencia: 'vigente' })).toEqual(['ARM-CM-000010', 'ARM-WEB-000012']);
    expect(numbers({ vigencia: 'vencida' })).toEqual(['ARM-CM-000014']);
    expect(numbers({ publicada: 'si' })).toEqual(['ARM-CM-000013']);
    expect(numbers({ publicada: 'no' })).toHaveLength(4);
    expect(numbers({ canal: 'Web' })).toEqual(['ARM-WEB-000012']);
    expect(numbers({ desde: '2026-09-20', hasta: '2026-09-20' })).toEqual(['ARM-CM-000013']);
    expect(numbers({ q: 'luis' })).toEqual(['ARM-WEB-000012']);
    expect(numbers({ q: 'f-cm-000120' })).toEqual(['ARM-CM-000013']);
  });

  it('el CSV y el resumen de los armados', () => {
    const [header, first] = buildCsv(CSV_COLUMNS, items.slice(0, 1), { bom: false }).split('\r\n');
    expect(header).toBe('"Número";"Armado";"Cliente";"Canal";"Sucursal";"Piezas";"Compatibilidad";"Estado";"Vigencia o venta";"Publicado en la web";"Creado";"Total"');
    expect(first).toBe('"ARM-CM-000010";"PC gamer";"Ana Rojas";"Mostrador";"CM";6;"Compatible";"Cotizado";"Vigente hasta 06/10/2026";"No";"28/09/2026 10:00";7000');
    expect(buildsOverview(rows, NOW)).toEqual({ open: 2, openValue: 16800, webActive: 1, webValue: 9800, sold: 1, soldValue: 12500, drafts: 1 });
  });
});
