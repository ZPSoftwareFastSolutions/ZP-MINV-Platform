// Módulo «Caja» · el cobro (funciones puras): cliente y medio de pago con los valores del servidor, efectivo recibido y
// vuelto, referencia y tarjeta, la guía antes de cobrar (como el escritorio) y los pedidos EXACTOS del contrato para
// `CheckoutCommand` (venta normal) y `SellPcBuildCommand` (reserva o cotización). La validación que manda es la del
// servidor: si rechaza, su mensaje se muestra en el diálogo del cobro.

import { formatMoney, roundTo } from '@/4-presentation/panel/lib';
import { cartProblems, serialsBySku, type CajaCart } from './cart';
import { buyerErrors, buyerPayload, type BuyerDraft, type BuyerErrors } from './fiscal';
import type { CajaProduct } from './products';
import type { CheckoutPayload, PosOptionData, PosStateData, SellBuildPayload } from './types';

/** Cliente «Consumidor final» de la empresa (el que la caja elige por defecto, como el escritorio). */
export const CONSUMER_CODE = 'CF';
/** Medio de pago que la caja elige por defecto. */
export const CASH_CODE = 'EFECTIVO';

/** Lo que el cajero elige y escribe para cobrar (vive solo en la página mientras se cobra). */
export interface PaymentDraft {
  customerCode: string;
  methodCode: string;
  /** Efectivo recibido (null = no se indicó: se cobra el total exacto). */
  cashReceived: number | null;
  /** Número de operación, voucher o referencia (medios que no son efectivo). */
  reference: string;
  /** Número de la tarjeta: viaja al cobrar y el servidor lo guarda solo enmascarado (regla F-06). */
  cardNumber: string;
}

export const EMPTY_PAYMENT: PaymentDraft = { customerCode: '', methodCode: '', cashReceived: null, reference: '', cardNumber: '' };

/** Completa el cliente (consumidor final) y el medio de pago (efectivo) si faltan o ya no existen. */
export function withDefaults(draft: PaymentDraft, state: PosStateData | undefined): PaymentDraft {
  if (!state) return draft;
  const customer = state.customers.some((option) => option.code === draft.customerCode)
    ? draft.customerCode
    : ((state.customers.find((option) => option.code === CONSUMER_CODE) ?? state.customers[0])?.code ?? '');
  const method = state.paymentMethods.some((option) => option.code === draft.methodCode)
    ? draft.methodCode
    : ((state.paymentMethods.find((option) => option.code === CASH_CODE) ?? state.paymentMethods[0])?.code ?? '');
  return customer === draft.customerCode && method === draft.methodCode ? draft : { ...draft, customerCode: customer, methodCode: method };
}

/** Después de cobrar: consumidor final y sin montos ni referencias (el medio de pago se conserva, como el escritorio). */
export function afterSale(draft: PaymentDraft): PaymentDraft {
  return { ...draft, customerCode: '', cashReceived: null, reference: '', cardNumber: '' };
}

export function methodOf(state: PosStateData | undefined, code: string): PosOptionData | null {
  return state?.paymentMethods.find((option) => option.code === code) ?? null;
}

/** El cliente de la caja con ese nombre (una reserva trae el nombre del cliente, no su código). */
export function customerByName(state: PosStateData | undefined, name: string | null | undefined): PosOptionData | null {
  const wanted = name?.trim().toLocaleLowerCase('es');
  if (!state || !wanted) return null;
  return state.customers.find((option) => option.name.trim().toLocaleLowerCase('es') === wanted) ?? null;
}

/** ¿Efectivo? El medio de pago abre el cajón (sin medio elegido, se asume efectivo, como el escritorio). */
export function isCash(method: PosOptionData | null): boolean {
  return method ? method.opensCashDrawer : true;
}

/** ¿Tarjeta? Solo importa si la caja factura: la factura lleva el número enmascarado. */
export function isCard(method: PosOptionData | null, billing: boolean): boolean {
  if (!billing || !method) return false;
  return /TARJ|CARD/i.test(method.code) || /tarjeta/i.test(method.name);
}

/** Solo los dígitos de la tarjeta. */
export function cardDigits(text: string): string {
  return text.replace(/\D/g, '');
}

/** Billetes redondos para «recibe con…»: el siguiente múltiplo de 20, 50, 100 y 200 por ENCIMA del total (hasta 3). */
export function cashSuggestions(total: number): number[] {
  if (!(total > 0)) return [];
  const values = [20, 50, 100, 200].map((step) => Math.floor(roundTo(total, 2) / step) * step + step);
  return [...new Set(values)].sort((a, b) => a - b).slice(0, 3);
}

export interface ChangeInfo {
  kind: 'none' | 'short' | 'exact' | 'change';
  amount: number;
  text: string;
}

/** Vuelto (o cuánto falta) con el efectivo recibido. */
export function changeInfo(total: number, cash: number | null): ChangeInfo {
  if (cash === null || !(cash > 0)) return { kind: 'none', amount: 0, text: 'Sin monto recibido: se cobra el total exacto.' };
  const difference = roundTo(cash - total, 2);
  if (difference < 0) return { kind: 'short', amount: -difference, text: `Faltan ${formatMoney(-difference)}.` };
  if (difference === 0) return { kind: 'exact', amount: 0, text: 'Pago exacto: sin vuelto.' };
  return { kind: 'change', amount: difference, text: `Vuelto: ${formatMoney(difference)}` };
}

// ---------------------------------------------------------------------------------------------------- guía antes de cobrar

export interface CheckoutErrors extends BuyerErrors {
  customer?: string;
  method?: string;
  cash?: string;
  reference?: string;
  card?: string;
  /** Lo que no es de un campo: caja cerrada, venta vacía, series o stock. */
  general: string[];
}

export interface CheckoutContext {
  cart: CajaCart;
  products: readonly CajaProduct[];
  total: number;
  payment: PaymentDraft;
  buyer: BuyerDraft;
  method: PosOptionData | null;
  /** La caja factura (datos del comprador y tarjeta). */
  billing: boolean;
  sessionOpen: boolean;
}

/** Lo que falta o no sirve para cobrar (la regla que manda es la del servidor). */
export function checkoutErrors(context: CheckoutContext): CheckoutErrors {
  const { cart, payment, method, total, billing } = context;
  const general: string[] = [];
  if (!context.sessionOpen) general.push('Abra la caja para cobrar.');
  if (cart.lines.length === 0) general.push('Agregue al menos un producto a la venta.');
  general.push(...cartProblems(cart, context.products));
  const errors: CheckoutErrors = { general };
  if (!payment.customerCode) errors.customer = 'Elija el cliente (o «Consumidor final»).';
  if (!method) {
    errors.method = 'Elija el medio de pago.';
  } else if (isCash(method)) {
    const cash = payment.cashReceived;
    if (cash !== null && cash > 0 && cash < total) errors.cash = `Recibió ${formatMoney(cash)} y el total es ${formatMoney(total)}.`;
  } else if (payment.reference.trim().length === 0) {
    errors.reference = `El pago con ${method.name} exige el número de operación, voucher o referencia.`;
  }
  if (billing) {
    Object.assign(errors, buyerErrors(context.buyer, payment.customerCode === CONSUMER_CODE));
    if (isCard(method, true)) {
      const digits = cardDigits(payment.cardNumber);
      if (digits.length < 8 || digits.length > 19) errors.card = 'Escriba el número de la tarjeta (de 8 a 19 dígitos): la factura lo lleva enmascarado.';
    }
  }
  return errors;
}

/** Cuántos problemas hay (de los campos y generales). */
export function errorCount(errors: CheckoutErrors): number {
  const fields = (Object.keys(errors) as (keyof CheckoutErrors)[]).filter((key) => key !== 'general' && errors[key]);
  return fields.length + errors.general.length;
}

// ---------------------------------------------------------------------------------------------------- pedidos

interface PaymentParts {
  cashReceived: number | null;
  paymentReference: string | null;
  cardNumber: string | null;
}

function paymentParts(payment: PaymentDraft, method: PosOptionData | null, billing: boolean): PaymentParts {
  const cash = isCash(method);
  const reference = payment.reference.trim();
  return {
    cashReceived: cash && payment.cashReceived !== null && payment.cashReceived > 0 ? payment.cashReceived : null,
    paymentReference: cash || reference.length === 0 ? null : reference,
    cardNumber: isCard(method, billing) ? cardDigits(payment.cardNumber) : null,
  };
}

/** `CheckoutCommand` con TODOS sus parámetros (lo que no aplica va en null). */
export function checkoutPayload(cart: CajaCart, payment: PaymentDraft, buyer: BuyerDraft, method: PosOptionData | null, billing: boolean): CheckoutPayload {
  const parts = paymentParts(payment, method, billing);
  return {
    customerCode: payment.customerCode,
    paymentMethodCode: payment.methodCode,
    lines: cart.lines.map((line) => ({ sku: line.sku, quantity: line.quantity, discountPercent: line.discountPercent, serials: line.serialized ? [...line.serials] : null })),
    cashReceived: parts.cashReceived,
    paymentReference: parts.paymentReference,
    buyer: billing ? buyerPayload(buyer) : null,
    cardNumber: parts.cardNumber,
  };
}

/** `SellPcBuildCommand` con TODOS sus parámetros: la reserva, las series de sus piezas y el cobro. */
export function sellBuildPayload(number: string, cart: CajaCart, payment: PaymentDraft, buyer: BuyerDraft, method: PosOptionData | null, billing: boolean): SellBuildPayload {
  const parts = paymentParts(payment, method, billing);
  const serials = serialsBySku(cart.lines);
  return {
    number,
    paymentMethodCode: payment.methodCode,
    serials: serials.length > 0 ? serials : null,
    cashReceived: parts.cashReceived,
    paymentReference: parts.paymentReference,
    buyer: billing ? buyerPayload(buyer) : null,
    cardNumber: parts.cardNumber,
    customerCode: payment.customerCode.length > 0 ? payment.customerCode : null,
  };
}
