// Módulo «Caja» · la venta en curso: agregar, cantidades (enteras o con decimales), descuentos, quitar, series de los
// productos serializados, cargar una reserva (líneas fijas a precio congelado), importes con la aritmética del servidor
// (redondeo a 2 decimales, IVA incluido de Bolivia) y los avisos que impiden cobrar (series, lo disponible).

import { describe, expect, it } from 'vitest';
import {
  EMPTY_CART,
  buildLines,
  cartProblems,
  cartReducer,
  cartTotals,
  includedTax,
  itemsText,
  lineAmount,
  lineInfoFrom,
  lineStock,
  missingSerials,
  serialsBySku,
  serialsInOtherLines,
  stockWarning,
  type CajaCart,
  type CartAction,
} from './cart';
import type { CajaProduct } from './products';
import type { BuildDetailData, BuildRowData } from './types';

function product(overrides: Partial<CajaProduct>): CajaProduct {
  return {
    sku: 'MOU-LOG-G502',
    name: 'Mouse Logitech G502',
    category: 'Mouse',
    categoryCode: 'MOU',
    unit: 'UND',
    allowsDecimals: false,
    price: 450,
    available: 5,
    reserved: 0,
    barcodes: [],
    serialized: false,
    serialKind: 'Serial',
    warrantyMonths: 0,
    platforms: [],
    brand: null,
    keySpecs: '',
    ...overrides,
  };
}

const mouse = product({});
const cable = product({ sku: 'CAB-HDMI', name: 'Cable HDMI', unit: 'M', allowsDecimals: true, price: 35.5, available: 10 });
const phone = product({ sku: 'CEL-SAM-A55', name: 'Celular Samsung A55', price: 3200, available: 3, reserved: 1, serialized: true, serialKind: 'Imei' });
const products = [mouse, cable, phone];

const run = (actions: CartAction[], start: CajaCart = EMPTY_CART) => actions.reduce(cartReducer, start);

function buildRow(overrides: Partial<BuildRowData> = {}): BuildRowData {
  return {
    id: 'b1',
    number: 'RES-CB-000007',
    name: 'Compra de Ana',
    branchCode: 'CB',
    customer: null,
    status: 'Reserved',
    validUntil: '2026-10-05',
    isExpired: false,
    total: 3650,
    items: 2,
    isCompatible: true,
    createdAt: '2026-09-28T14:00:00Z',
    invoiceNumber: null,
    quotedWithErrors: false,
    channel: 'Web',
    contactName: 'Ana Pérez',
    contactPhone: '71234567',
    contactEmail: 'ana@correo.example',
    reservedUntil: '2026-09-30T14:00:00Z',
    publishedToWeb: false,
    cancelReason: null,
    notes: null,
    reserved: 2,
    kind: 'Cart',
    buyerDocumentType: 5,
    buyerDocumentNumber: '1020304050',
    buyerComplement: null,
    buyerName: 'ANA PEREZ SRL',
    ...overrides,
  };
}

function buildDetail(row: BuildRowData): BuildDetailData {
  const item = (sku: string, name: string, quantity: number, unitPrice: number) => ({ slot: null, sku, name, quantity, unitPrice, subtotal: quantity * unitPrice, stock: 5, imageId: null, keySpecs: [] });
  return {
    build: row,
    check: { items: [], issues: [], isCompatible: true, estimatedDrawW: 0, recommendedPsuW: 0, psuW: null, total: row.total },
    quotedItems: [item('CEL-SAM-A55', 'Celular Samsung A55', 1, 3200), item('MOU-LOG-G502', 'Mouse Logitech G502', 1, 450)],
    history: null,
  };
}

describe('caja · venta en curso', () => {
  it('agregar suma una unidad (media, si la unidad admite decimales); − en la última unidad quita la línea', () => {
    let cart = run([{ type: 'add', product: mouse }, { type: 'add', product: mouse }, { type: 'add', product: cable }]);
    expect(cart.lines.map((line) => [line.sku, line.quantity])).toEqual([
      ['MOU-LOG-G502', 2],
      ['CAB-HDMI', 1],
    ]);
    cart = run([{ type: 'increase', key: 'CAB-HDMI' }, { type: 'decrease', key: 'MOU-LOG-G502' }, { type: 'decrease', key: 'MOU-LOG-G502' }], cart);
    expect(cart.lines.map((line) => [line.sku, line.quantity])).toEqual([['CAB-HDMI', 1.5]]);
  });

  it('la cantidad escrita se ajusta a la unidad y un cero o un valor raro no cambia nada', () => {
    let cart = run([{ type: 'add', product: mouse }, { type: 'add', product: cable }]);
    cart = run(
      [
        { type: 'setQuantity', key: 'MOU-LOG-G502', quantity: 3.4 },
        { type: 'setQuantity', key: 'CAB-HDMI', quantity: 2.3456 },
      ],
      cart,
    );
    expect(cart.lines.map((line) => line.quantity)).toEqual([3, 2.346]);
    expect(run([{ type: 'setQuantity', key: 'MOU-LOG-G502', quantity: 0 }], cart)).toBe(cart);
    expect(run([{ type: 'setQuantity', key: 'MOU-LOG-G502', quantity: Number.NaN }], cart)).toBe(cart);
  });

  it('un producto con serie no se agrega «a secas»: la cantidad es la de las series elegidas (sin repetir)', () => {
    expect(run([{ type: 'add', product: phone }]).lines).toHaveLength(0);
    let cart = run([{ type: 'setSerials', product: phone, serials: ['35000000000001', ' 35000000000001 ', '35000000000002'] }]);
    expect(cart.lines[0]).toMatchObject({ sku: 'CEL-SAM-A55', quantity: 2, serials: ['35000000000001', '35000000000002'] });
    cart = run([{ type: 'increase', key: 'CEL-SAM-A55' }, { type: 'setQuantity', key: 'CEL-SAM-A55', quantity: 5 }], cart);
    expect(cart.lines[0].quantity).toBe(2);
    expect(run([{ type: 'setSerials', product: phone, serials: [] }], cart).lines).toHaveLength(0);
  });

  it('descuentos por línea y quitar; con una reserva cargada sus líneas no se tocan ni se mezclan con otros productos', () => {
    let cart = run([{ type: 'add', product: mouse }, { type: 'setDiscount', key: 'MOU-LOG-G502', discount: 10 }]);
    expect(cart.lines[0].discountPercent).toBe(10);
    expect(run([{ type: 'remove', key: 'MOU-LOG-G502' }], cart).lines).toHaveLength(0);

    const detail = buildDetail(buildRow());
    cart = run([{ type: 'loadBuild', build: detail.build, lines: buildLines(detail, lineInfoFrom(products)) }]);
    expect(cart.build?.number).toBe('RES-CB-000007');
    expect(cart.lines.map((line) => [line.key, line.sku, line.locked, line.reserved, line.serialized, line.unitPrice])).toEqual([
      ['RES-CB-000007#1', 'CEL-SAM-A55', true, true, true, 3200],
      ['RES-CB-000007#2', 'MOU-LOG-G502', true, true, false, 450],
    ]);
    const same = run(
      [
        { type: 'add', product: cable },
        { type: 'remove', key: 'RES-CB-000007#2' },
        { type: 'setDiscount', key: 'RES-CB-000007#2', discount: 25 },
        { type: 'increase', key: 'RES-CB-000007#2' },
      ],
      cart,
    );
    expect(same).toBe(cart);
    // Las series de una pieza: como mucho su cantidad.
    const withSerials = run([{ type: 'setLineSerials', key: 'RES-CB-000007#1', serials: ['A1', 'A2'] }], cart);
    expect(withSerials.lines[0].serials).toEqual(['A1']);
    expect(run([{ type: 'clear' }], withSerials)).toEqual(EMPTY_CART);
  });

  it('una pieza que ya no está a la venta toma de la ficha técnica si lleva serie', () => {
    const info = lineInfoFrom([], [{ sku: 'GPU-X', name: 'GPU', categoryCode: 'GPU', category: 'GPU', brand: null, price: 1, stock: 1, trackSerials: true, warrantyMonths: 36, keySpecs: '', platforms: [], imageId: null, serialKind: 'Serial' }]);
    expect(info('gpu-x')).toEqual({ unit: 'UND', allowsDecimals: false, serialized: true, serialKind: 'Serial' });
    expect(info('NADA')).toBeNull();
  });

  it('importes con la aritmética del servidor: línea redondeada a 2 decimales, descuentos y el IVA incluido', () => {
    expect(lineAmount({ quantity: 3, unitPrice: 33.33, discountPercent: 10 })).toBe(89.99);
    expect(lineAmount({ quantity: 3, unitPrice: 0.35, discountPercent: 0 })).toBe(1.05);
    const cart = run([{ type: 'add', product: mouse }, { type: 'add', product: mouse }, { type: 'setDiscount', key: 'MOU-LOG-G502', discount: 10 }, { type: 'add', product: cable }]);
    expect(cartTotals(cart.lines)).toEqual({ lines: 2, units: 3, gross: 935.5, discount: 90, total: 845.5 });
    // Bolivia: el 13 % del importe facturado; en otros países, el IVA contenido.
    expect(includedTax(845.5, 13, true)).toBe(109.92);
    expect(includedTax(113, 13, false)).toBe(13);
    expect(includedTax(0, 13, true)).toBe(0);
    expect(itemsText(cartTotals(cart.lines))).toBe('2 productos · 3 unidades');
    expect(itemsText(cartTotals([]))).toBe('Venta vacía');
  });

  it('avisa lo que impide cobrar: series que faltan y más de lo disponible (lo reservado no se vende)', () => {
    const cart = run([{ type: 'add', product: mouse }, { type: 'setQuantity', key: 'MOU-LOG-G502', quantity: 6 }]);
    const stock = lineStock(cart.lines[0], cart, products);
    expect(stock).toEqual({ available: 5, reserved: 0, requested: 6, exceeds: true });
    expect(stockWarning({ available: 2, reserved: 1, requested: 3, exceeds: true })).toBe('Supera lo disponible: hay 2 (1 reservada para otras ventas).');

    const detail = buildDetail(buildRow({ status: 'Quoted', kind: 'Build', number: 'ARM-CB-000003' }));
    const quoted = run([{ type: 'loadBuild', build: detail.build, lines: buildLines(detail, lineInfoFrom(products)) }]);
    expect(missingSerials(quoted.lines[0])).toBe(1);
    expect(cartProblems(quoted, products)).toEqual(['Celular Samsung A55: Elija 1 IMEI (tiene 0).']);
    // Una reserva: sus unidades ya están apartadas para esta venta (no cuentan contra lo disponible).
    const reserved = run([{ type: 'loadBuild', build: buildRow(), lines: buildLines(buildDetail(buildRow()), lineInfoFrom(products)) }]);
    expect(lineStock(reserved.lines[1], reserved, [{ ...mouse, available: 0 }]).exceeds).toBe(false);
    expect(cartProblems(cart, products)).toEqual(['Mouse Logitech G502: pide 6 y hay 5. Baje la cantidad o libere la reserva.']);
  });

  it('series de otras líneas y series agrupadas por producto para vender una reserva', () => {
    const row = buildRow();
    const detail: BuildDetailData = { ...buildDetail(row), quotedItems: [...buildDetail(row).quotedItems, { ...buildDetail(row).quotedItems[0] }] };
    let cart = run([{ type: 'loadBuild', build: row, lines: buildLines(detail, lineInfoFrom(products)) }]);
    cart = run(
      [
        { type: 'setLineSerials', key: 'RES-CB-000007#1', serials: ['S1'] },
        { type: 'setLineSerials', key: 'RES-CB-000007#3', serials: ['S2'] },
      ],
      cart,
    );
    expect(serialsInOtherLines(cart, 'cel-sam-a55', 'RES-CB-000007#1')).toEqual(['S2']);
    expect(serialsBySku(cart.lines)).toEqual([{ sku: 'CEL-SAM-A55', serials: ['S1', 'S2'] }]);
  });
});
