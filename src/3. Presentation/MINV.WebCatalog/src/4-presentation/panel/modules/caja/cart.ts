// Módulo «Caja» · la venta en curso (el carrito del cajero), funciones puras: agregar, cambiar cantidad y descuento,
// quitar, elegir las series de los productos serializados y cargar una reserva o cotización (líneas fijas a precio
// congelado). Los importes son una VISTA PREVIA con la misma aritmética del servidor (importe de la línea redondeado a 2
// decimales, IVA incluido de Bolivia); el total que vale es el que devuelve el cobro (regla P-01). La venta vive solo en
// la memoria de la página: nada se guarda en el navegador.

import { formatQuantity, roundTo } from '@/4-presentation/panel/lib';
import { productBySku, serialNoun, type CajaProduct } from './products';
import type { BuildDetailData, BuildRowData, SerialKindData } from './types';

/** Una línea de la venta. */
export interface CajaLine {
  /** Identificador de la línea: el SKU en una venta normal; «RES-CM-000012#2» en una reserva. */
  key: string;
  sku: string;
  name: string;
  unit: string;
  allowsDecimals: boolean;
  /** Precio unitario con IVA: el de la lista vigente o el congelado de la reserva. */
  unitPrice: number;
  quantity: number;
  /** Descuento de la línea (0 a 100 %). */
  discountPercent: number;
  serialized: boolean;
  serialKind: SerialKindData;
  /** Series o IMEI de las unidades (una por unidad). */
  serials: readonly string[];
  /** Línea de una reserva o cotización: precio, cantidad y descuento fijos. */
  locked: boolean;
  /** Sus unidades ya están reservadas para esta venta (no cuentan contra lo disponible). */
  reserved: boolean;
}

export interface CajaCart {
  lines: readonly CajaLine[];
  /** La reserva o cotización que se está cobrando (null = venta normal). */
  build: BuildRowData | null;
}

export const EMPTY_CART: CajaCart = { lines: [], build: null };

/** Descuentos que ofrece la caja por línea (los mismos del escritorio). */
export const DISCOUNT_OPTIONS: readonly number[] = [0, 5, 10, 15, 20, 25];

/** Paso de los botones − y +: una unidad (o media, si la unidad admite decimales). */
export function quantityStep(line: Pick<CajaLine, 'allowsDecimals'>): number {
  return line.allowsDecimals ? 0.5 : 1;
}

/** Cantidad válida para la unidad: entera, o con hasta 3 decimales. */
export function normalizeQuantity(value: number, allowsDecimals: boolean): number {
  return allowsDecimals ? roundTo(value, 3) : Math.round(value);
}

function lineOf(product: CajaProduct, quantity: number, serials: readonly string[] = []): CajaLine {
  return {
    key: product.sku,
    sku: product.sku,
    name: product.name,
    unit: product.unit,
    allowsDecimals: product.allowsDecimals,
    unitPrice: product.price,
    quantity,
    discountPercent: 0,
    serialized: product.serialized,
    serialKind: product.serialKind,
    serials,
    locked: false,
    reserved: false,
  };
}

/** Lo que la caja sabe de un producto al cargar una reserva (unidad y si lleva serie). */
export interface LineInfo {
  unit: string;
  allowsDecimals: boolean;
  serialized: boolean;
  serialKind: SerialKindData;
}

/** Líneas fijas de una reserva o cotización: una por pieza, a su precio congelado, con sus series por elegir. */
export function buildLines(detail: BuildDetailData, infoOf: (sku: string) => LineInfo | null): CajaLine[] {
  const reserved = detail.build.status === 'Reserved';
  return detail.quotedItems.map((item, index) => {
    const info = infoOf(item.sku);
    return {
      key: `${detail.build.number}#${index + 1}`,
      sku: item.sku,
      name: item.name,
      unit: info?.unit ?? 'UND',
      allowsDecimals: info?.allowsDecimals ?? false,
      unitPrice: item.unitPrice,
      quantity: item.quantity,
      discountPercent: 0,
      serialized: info?.serialized ?? false,
      serialKind: info?.serialKind ?? 'Serial',
      serials: [],
      locked: true,
      reserved,
    };
  });
}

/** Qué sabe la caja de un SKU con la lista de productos (null si no está a la venta). */
export function lineInfoFrom(products: readonly CajaProduct[]): (sku: string) => LineInfo | null {
  return (sku) => {
    const product = productBySku(products, sku);
    return product ? { unit: product.unit, allowsDecimals: product.allowsDecimals, serialized: product.serialized, serialKind: product.serialKind } : null;
  };
}

export type CartAction =
  | { type: 'add'; product: CajaProduct }
  | { type: 'setQuantity'; key: string; quantity: number }
  | { type: 'increase'; key: string }
  | { type: 'decrease'; key: string }
  | { type: 'setDiscount'; key: string; discount: number }
  | { type: 'remove'; key: string }
  /** Series de un producto serializado de la venta normal: la cantidad es la de las series (vacío = se quita). */
  | { type: 'setSerials'; product: CajaProduct; serials: readonly string[] }
  /** Series de una línea fija de una reserva (tantas como su cantidad). */
  | { type: 'setLineSerials'; key: string; serials: readonly string[] }
  | { type: 'loadBuild'; build: BuildRowData; lines: readonly CajaLine[] }
  | { type: 'clear' };

function unique(values: readonly string[]): string[] {
  const seen = new Set<string>();
  const result: string[] = [];
  for (const value of values) {
    const text = value.trim();
    const key = text.toUpperCase();
    if (text.length === 0 || seen.has(key)) continue;
    seen.add(key);
    result.push(text);
  }
  return result;
}

function updateLine(cart: CajaCart, key: string, change: (line: CajaLine) => CajaLine | null): CajaCart {
  const index = cart.lines.findIndex((line) => line.key === key);
  if (index < 0) return cart;
  const next = change(cart.lines[index]);
  if (next === cart.lines[index]) return cart;
  const lines = [...cart.lines];
  if (next === null) lines.splice(index, 1);
  else lines[index] = next;
  return { ...cart, lines };
}

/** Líneas que el cajero puede cambiar (no las de una reserva ni la cantidad de una serializada). */
function editable(line: CajaLine): boolean {
  return !line.locked && !line.serialized;
}

export function cartReducer(cart: CajaCart, action: CartAction): CajaCart {
  switch (action.type) {
    case 'add': {
      // Con una reserva cargada no se mezclan otros productos; un serializado se agrega eligiendo su serie.
      if (cart.build || action.product.serialized) return cart;
      const existing = cart.lines.find((line) => line.sku === action.product.sku);
      if (!existing) return { ...cart, lines: [...cart.lines, lineOf(action.product, 1)] };
      return updateLine(cart, existing.key, (line) => ({ ...line, quantity: normalizeQuantity(line.quantity + quantityStep(line), line.allowsDecimals) }));
    }
    case 'setQuantity':
      return updateLine(cart, action.key, (line) => {
        if (!editable(line) || !Number.isFinite(action.quantity) || action.quantity <= 0) return line;
        const quantity = normalizeQuantity(action.quantity, line.allowsDecimals);
        return quantity > 0 && quantity !== line.quantity ? { ...line, quantity } : line;
      });
    case 'increase':
      return updateLine(cart, action.key, (line) => (editable(line) ? { ...line, quantity: normalizeQuantity(line.quantity + quantityStep(line), line.allowsDecimals) } : line));
    case 'decrease':
      return updateLine(cart, action.key, (line) => {
        if (!editable(line)) return line;
        const quantity = normalizeQuantity(line.quantity - quantityStep(line), line.allowsDecimals);
        return quantity > 0 ? { ...line, quantity } : null;
      });
    case 'setDiscount':
      return updateLine(cart, action.key, (line) => {
        const discount = Math.min(100, Math.max(0, action.discount));
        return line.locked || !Number.isFinite(discount) || discount === line.discountPercent ? line : { ...line, discountPercent: discount };
      });
    case 'remove':
      return updateLine(cart, action.key, (line) => (line.locked ? line : null));
    case 'setSerials': {
      const serials = unique(action.serials);
      const existing = cart.lines.find((line) => !line.locked && line.sku === action.product.sku);
      if (existing) return updateLine(cart, existing.key, (line) => (serials.length === 0 ? null : { ...line, serials, quantity: serials.length }));
      if (cart.build || serials.length === 0 || !action.product.serialized) return cart;
      return { ...cart, lines: [...cart.lines, lineOf(action.product, serials.length, serials)] };
    }
    case 'setLineSerials':
      return updateLine(cart, action.key, (line) => (line.locked && line.serialized ? { ...line, serials: unique(action.serials).slice(0, Math.round(line.quantity)) } : line));
    case 'loadBuild':
      return { build: action.build, lines: [...action.lines] };
    case 'clear':
      return EMPTY_CART;
  }
}

// ---------------------------------------------------------------------------------------------------- importes

/** Importe de la línea con su descuento, redondeado a 2 decimales (mitad hacia arriba), como el servidor. */
export function lineAmount(line: Pick<CajaLine, 'quantity' | 'unitPrice' | 'discountPercent'>): number {
  return roundTo(line.quantity * line.unitPrice * (1 - line.discountPercent / 100), 2);
}

/** Importe de la línea sin descuento. */
export function lineGross(line: Pick<CajaLine, 'quantity' | 'unitPrice'>): number {
  return roundTo(line.quantity * line.unitPrice, 2);
}

export interface CartTotals {
  /** Líneas (productos distintos). */
  lines: number;
  units: number;
  /** Sin descuentos. */
  gross: number;
  discount: number;
  /** A cobrar (con IVA incluido). */
  total: number;
}

export function cartTotals(lines: readonly CajaLine[]): CartTotals {
  const total = roundTo(lines.reduce((sum, line) => sum + lineAmount(line), 0), 2);
  const gross = roundTo(lines.reduce((sum, line) => sum + lineGross(line), 0), 2);
  return { lines: lines.length, units: roundTo(lines.reduce((sum, line) => sum + line.quantity, 0), 3), gross, discount: roundTo(gross - total, 2), total };
}

/**
 * IVA contenido en un importe con el IVA incluido (redondeado a 2 decimales): en Bolivia el 13 % del importe facturado
 * (`onInvoicedAmount`); en los demás, importe × tasa / (100 + tasa). Misma regla que el servidor (`VatRules`).
 */
export function includedTax(amount: number, ratePercent: number, onInvoicedAmount: boolean): number {
  if (!(ratePercent > 0) || !(amount > 0)) return 0;
  return roundTo(onInvoicedAmount ? (amount * ratePercent) / 100 : (amount * ratePercent) / (100 + ratePercent), 2);
}

/** «2 productos · 3 unidades» · «Venta vacía». */
export function itemsText(totals: CartTotals): string {
  if (totals.lines === 0) return 'Venta vacía';
  return `${totals.lines === 1 ? '1 producto' : `${totals.lines} productos`} · ${formatQuantity(totals.units)} ${totals.units === 1 ? 'unidad' : 'unidades'}`;
}

// ---------------------------------------------------------------------------------------------------- avisos de la venta

/** Cuántas series faltan elegir en la línea (0 si no lleva serie o ya están todas). */
export function missingSerials(line: CajaLine): number {
  return line.serialized ? Math.max(0, Math.round(line.quantity) - line.serials.length) : 0;
}

export interface LineStock {
  /** Disponible del producto (null si no se sabe: el producto no está en la lista). */
  available: number | null;
  reserved: number;
  /** Cuánto pide la venta de ese producto (todas sus líneas). */
  requested: number;
  /** La venta pide más de lo disponible (guía visual: el que decide es el servidor). */
  exceeds: boolean;
}

export function lineStock(line: CajaLine, cart: CajaCart, products: readonly CajaProduct[]): LineStock {
  if (line.reserved) return { available: null, reserved: 0, requested: line.quantity, exceeds: false };
  const product = productBySku(products, line.sku);
  const requested = roundTo(
    cart.lines.filter((other) => !other.reserved && other.sku.toUpperCase() === line.sku.toUpperCase()).reduce((sum, other) => sum + other.quantity, 0),
    3,
  );
  if (!product) return { available: null, reserved: 0, requested, exceeds: false };
  return { available: product.available, reserved: product.reserved, requested, exceeds: requested > product.available };
}

/** «Supera lo disponible: hay 2 (1 reservada para otras ventas).» */
export function stockWarning(stock: LineStock): string {
  const available = formatQuantity(Math.max(0, stock.available ?? 0));
  if (stock.reserved > 0) {
    const held = formatQuantity(stock.reserved);
    return `Supera lo disponible: hay ${available} (${held} ${stock.reserved === 1 ? 'reservada' : 'reservadas'} para otras ventas).`;
  }
  return `Supera lo disponible: hay ${available}.`;
}

/** «Elija 2 series (tiene 1)». */
export function serialsNeededText(line: CajaLine): string {
  const required = Math.round(line.quantity);
  return `Elija ${required} ${serialNoun(line.serialKind, required)} (tiene ${line.serials.length}).`;
}

/** Lo que impide cobrar en la venta misma (series que faltan, más de lo disponible), en palabras. */
export function cartProblems(cart: CajaCart, products: readonly CajaProduct[]): string[] {
  const problems: string[] = [];
  for (const line of cart.lines) {
    if (missingSerials(line) > 0) problems.push(`${line.name}: ${serialsNeededText(line)}`);
  }
  const reported = new Set<string>();
  for (const line of cart.lines) {
    const stock = lineStock(line, cart, products);
    const key = line.sku.toUpperCase();
    if (!stock.exceeds || reported.has(key)) continue;
    reported.add(key);
    problems.push(`${line.name}: pide ${formatQuantity(stock.requested)} y ${stockWarning(stock).replace(/^Supera lo disponible: /, '')} Baje la cantidad o libere la reserva.`);
  }
  return problems;
}

/** Series que ya usan OTRAS líneas del mismo producto (para no elegirlas dos veces). */
export function serialsInOtherLines(cart: CajaCart, sku: string, exceptKey: string | null): string[] {
  return cart.lines.filter((line) => line.key !== exceptKey && line.sku.toUpperCase() === sku.toUpperCase()).flatMap((line) => [...line.serials]);
}

/** Series de la venta agrupadas por producto (para vender una reserva). */
export function serialsBySku(lines: readonly CajaLine[]): { sku: string; serials: string[] }[] {
  const groups = new Map<string, { sku: string; serials: string[] }>();
  for (const line of lines) {
    if (line.serials.length === 0) continue;
    const key = line.sku.toUpperCase();
    const group = groups.get(key) ?? { sku: line.sku, serials: [] };
    group.serials.push(...line.serials);
    groups.set(key, group);
  }
  return [...groups.values()];
}
