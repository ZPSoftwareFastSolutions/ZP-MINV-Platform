// Módulo «Caja» · el cobro: valores por defecto del servidor (consumidor final, efectivo), efectivo y vuelto, referencia
// y tarjeta, la guía antes de cobrar (como el escritorio) y los pedidos EXACTOS de `CheckoutCommand` y
// `SellPcBuildCommand`.

import { describe, expect, it } from 'vitest';
import { EMPTY_CART, buildLines, cartReducer, type CajaCart } from './cart';
import { EMPTY_BUYER, type BuyerDraft } from './fiscal';
import {
  EMPTY_PAYMENT,
  afterSale,
  cardDigits,
  cashSuggestions,
  changeInfo,
  checkoutErrors,
  checkoutPayload,
  customerByName,
  errorCount,
  isCard,
  isCash,
  methodOf,
  sellBuildPayload,
  withDefaults,
  type PaymentDraft,
} from './payment';
import type { CajaProduct } from './products';
import type { BuildDetailData, PosStateData } from './types';

const state: PosStateData = {
  registers: [{ code: 'CAJA-CB-01', name: 'Caja 1', opensCashDrawer: false }],
  paymentMethods: [
    { code: 'EFECTIVO', name: 'Efectivo', opensCashDrawer: true },
    { code: 'QR', name: 'QR simple', opensCashDrawer: false },
    { code: 'TARJETA', name: 'Tarjeta de débito', opensCashDrawer: false },
  ],
  customers: [
    { code: 'CF', name: 'Consumidor final', opensCashDrawer: false },
    { code: 'CLI-0042', name: 'Ana Pérez', opensCashDrawer: false },
  ],
  session: null,
  taxRate: 13,
  companyName: 'Tech Zone Gaming S.R.L.',
  taxId: '1020304050',
  branchName: 'Sucursal Cochabamba',
  suggestedRegister: 'CAJA-CB-01',
  vatOnInvoicedAmount: true,
};

const mouse: CajaProduct = {
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
};
const phone: CajaProduct = { ...mouse, sku: 'CEL-SAM-A55', name: 'Celular Samsung A55', price: 3200, serialized: true, serialKind: 'Imei' };

const cart: CajaCart = [
  { type: 'add' as const, product: mouse },
  { type: 'add' as const, product: mouse },
  { type: 'setDiscount' as const, key: 'MOU-LOG-G502', discount: 10 },
  { type: 'setSerials' as const, product: phone, serials: ['356938035643809'] },
].reduce(cartReducer, EMPTY_CART);

const payment = (overrides: Partial<PaymentDraft> = {}) => ({ ...withDefaults(EMPTY_PAYMENT, state), ...overrides });
const nit = (overrides: Partial<BuyerDraft> = {}): BuyerDraft => ({ ...EMPTY_BUYER, documentType: 5, documentNumber: '1020304050', name: 'ACME SRL', ...overrides });

describe('caja · cobro', () => {
  it('por defecto consumidor final y efectivo; después de cobrar se limpia (el medio de pago queda)', () => {
    expect(withDefaults(EMPTY_PAYMENT, state)).toMatchObject({ customerCode: 'CF', methodCode: 'EFECTIVO' });
    expect(withDefaults({ ...EMPTY_PAYMENT, customerCode: 'CLI-0042', methodCode: 'QR' }, state)).toMatchObject({ customerCode: 'CLI-0042', methodCode: 'QR' });
    expect(withDefaults({ ...EMPTY_PAYMENT, customerCode: 'NO-EXISTE' }, state).customerCode).toBe('CF');
    expect(withDefaults(EMPTY_PAYMENT, undefined)).toBe(EMPTY_PAYMENT);
    expect(afterSale(payment({ methodCode: 'QR', reference: 'OP-1', cashReceived: 5, customerCode: 'CLI-0042' }))).toEqual({ ...EMPTY_PAYMENT, methodCode: 'QR' });
    expect(customerByName(state, ' ana pérez ')?.code).toBe('CLI-0042');
    expect(customerByName(state, null)).toBeNull();
  });

  it('efectivo, tarjeta (solo si la caja factura) y la tarjeta en dígitos', () => {
    expect(isCash(methodOf(state, 'EFECTIVO'))).toBe(true);
    expect(isCash(methodOf(state, 'QR'))).toBe(false);
    expect(isCash(null)).toBe(true);
    expect(isCard(methodOf(state, 'TARJETA'), true)).toBe(true);
    expect(isCard(methodOf(state, 'TARJETA'), false)).toBe(false);
    expect(isCard(methodOf(state, 'QR'), true)).toBe(false);
    expect(cardDigits('4111 1111-1111 1111')).toBe('4111111111111111');
  });

  it('vuelto y montos redondos para «recibe con…»', () => {
    expect(changeInfo(137.5, null).kind).toBe('none');
    expect(changeInfo(137.5, 100)).toMatchObject({ kind: 'short', amount: 37.5, text: 'Faltan Bs 37,50.' });
    expect(changeInfo(137.5, 137.5)).toMatchObject({ kind: 'exact', amount: 0 });
    expect(changeInfo(137.5, 200)).toMatchObject({ kind: 'change', amount: 62.5, text: 'Vuelto: Bs 62,50' });
    expect(cashSuggestions(137.5)).toEqual([140, 150, 200]);
    expect(cashSuggestions(100)).toEqual([120, 150, 200]);
    expect(cashSuggestions(0)).toEqual([]);
  });

  it('la guía antes de cobrar: caja, venta, efectivo insuficiente, referencia, documento del comprador y tarjeta', () => {
    const context = { cart, products: [mouse, phone], total: 4010, payment: payment(), buyer: EMPTY_BUYER, method: methodOf(state, 'EFECTIVO'), billing: false, sessionOpen: true };
    expect(errorCount(checkoutErrors(context))).toBe(0);
    expect(checkoutErrors({ ...context, sessionOpen: false, cart: EMPTY_CART }).general).toEqual(['Abra la caja para cobrar.', 'Agregue al menos un producto a la venta.']);
    expect(checkoutErrors({ ...context, payment: payment({ cashReceived: 4000 }) }).cash).toBe('Recibió Bs 4.000,00 y el total es Bs 4.010,00.');
    expect(checkoutErrors({ ...context, payment: payment({ methodCode: 'QR' }), method: methodOf(state, 'QR') }).reference).toBe(
      'El pago con QR simple exige el número de operación, voucher o referencia.',
    );
    // Con factura: a consumidor final hace falta el documento; con CI o NIT, solo dígitos; la tarjeta, de 8 a 19 dígitos.
    const billing = { ...context, billing: true };
    expect(checkoutErrors(billing).documentNumber).toMatch(/^Toda venta facturada lleva el documento del comprador/);
    expect(checkoutErrors({ ...billing, payment: payment({ customerCode: 'CLI-0042' }) }).documentNumber).toBeUndefined();
    expect(checkoutErrors({ ...billing, buyer: nit({ documentNumber: '1020-3040' }) }).documentNumber).toBe('El NIT lleva solo números (sin puntos, guiones ni espacios).');
    const card = { ...billing, buyer: nit(), payment: payment({ methodCode: 'TARJETA', reference: 'V-1', cardNumber: '4111' }), method: methodOf(state, 'TARJETA') };
    expect(checkoutErrors(card).card).toMatch(/^Escriba el número de la tarjeta/);
    expect(errorCount(checkoutErrors({ ...card, payment: payment({ methodCode: 'TARJETA', reference: 'V-1', cardNumber: '4111 1111 1111 1111' }) }))).toBe(0);
  });

  it('`CheckoutCommand` con la forma exacta del contrato (todos los parámetros)', () => {
    expect(checkoutPayload(cart, payment({ cashReceived: 5000 }), EMPTY_BUYER, methodOf(state, 'EFECTIVO'), false)).toEqual({
      customerCode: 'CF',
      paymentMethodCode: 'EFECTIVO',
      lines: [
        { sku: 'MOU-LOG-G502', quantity: 2, discountPercent: 10, serials: null },
        { sku: 'CEL-SAM-A55', quantity: 1, discountPercent: 0, serials: ['356938035643809'] },
      ],
      cashReceived: 5000,
      paymentReference: null,
      buyer: null,
      cardNumber: null,
    });
    expect(
      checkoutPayload(cart, payment({ methodCode: 'TARJETA', reference: ' V-778 ', cardNumber: '4111 1111 1111 1111', cashReceived: 99 }), nit({ email: ' acme@correo.example ' }), methodOf(state, 'TARJETA'), true),
    ).toMatchObject({
      paymentMethodCode: 'TARJETA',
      cashReceived: null,
      paymentReference: 'V-778',
      cardNumber: '4111111111111111',
      buyer: { documentType: 5, documentNumber: '1020304050', complement: null, name: 'ACME SRL', email: 'acme@correo.example', exceptionRequested: false },
    });
  });

  it('`SellPcBuildCommand` con la reserva, las series agrupadas por producto y el cobro', () => {
    const row = { number: 'RES-CB-000007', status: 'Reserved' } as BuildDetailData['build'];
    const detail = {
      build: row,
      quotedItems: [{ slot: null, sku: 'CEL-SAM-A55', name: 'Celular', quantity: 1, unitPrice: 3100, subtotal: 3100, stock: 1, imageId: null, keySpecs: [] }],
    } as unknown as BuildDetailData;
    const loaded = cartReducer(EMPTY_CART, { type: 'loadBuild', build: row, lines: buildLines(detail, () => ({ unit: 'UND', allowsDecimals: false, serialized: true, serialKind: 'Imei' })) });
    const withSerial = cartReducer(loaded, { type: 'setLineSerials', key: 'RES-CB-000007#1', serials: ['356938035643809'] });
    expect(sellBuildPayload('RES-CB-000007', withSerial, payment({ methodCode: 'QR', reference: 'QR-55', customerCode: 'CLI-0042' }), EMPTY_BUYER, methodOf(state, 'QR'), false)).toEqual({
      number: 'RES-CB-000007',
      paymentMethodCode: 'QR',
      serials: [{ sku: 'CEL-SAM-A55', serials: ['356938035643809'] }],
      cashReceived: null,
      paymentReference: 'QR-55',
      buyer: null,
      cardNumber: null,
      customerCode: 'CLI-0042',
    });
  });
});
