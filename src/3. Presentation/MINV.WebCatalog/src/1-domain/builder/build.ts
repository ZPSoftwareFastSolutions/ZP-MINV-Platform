// Estado del armado de PC: reductor puro (sin React) y cálculos derivados (total, piezas, progreso, faltantes).
// Todo vive en memoria: no persiste ni valida compatibilidad (solo presentación).

import type { Product } from '@/1-domain/catalog/types';
import { roundMoney, savingAmount, sumMoney } from '@/1-domain/catalog/money';
import { BUILD_SLOTS, REQUIRED_SLOTS, slotByKey, slotForProduct } from './slots';
import type { BuildLine, BuildSlot, SlotKey } from './types';

/** Máximo de unidades de una misma pieza en un armado. */
export const MAX_QUANTITY = 10;

export interface BuildState {
  lines: BuildLine[];
}

export const EMPTY_BUILD: BuildState = { lines: [] };

export type BuildAction =
  | { type: 'add'; product: Product; slot?: SlotKey; quantity?: number }
  | { type: 'remove'; sku: string }
  | { type: 'setQuantity'; sku: string; quantity: number }
  | { type: 'clear' }
  | { type: 'loadPreset'; lines: readonly BuildLine[] };

/** Unidades máximas que admite una pieza en el armado: el tope general acotado por su stock (al menos 1). */
export function maxQuantityFor(product: Pick<Product, 'stock'>): number {
  return Math.max(1, Math.min(MAX_QUANTITY, Math.trunc(product.stock)));
}

/** Acota una cantidad a [0, máximo] (MAX_QUANTITY, o el tope por stock si se pasa el producto). */
export function clampQuantity(quantity: number, product?: Pick<Product, 'stock'>): number {
  if (!Number.isFinite(quantity)) return 1;
  const max = product ? maxQuantityFor(product) : MAX_QUANTITY;
  return Math.min(max, Math.max(0, Math.trunc(quantity)));
}

function sortLines(lines: readonly BuildLine[]): BuildLine[] {
  return [...lines].sort((a, b) => slotByKey(a.slot).order - slotByKey(b.slot).order);
}

/**
 * Reductor puro del armado.
 * - `add`: en ranuras de una sola pieza reemplaza la existente; en las múltiples suma cantidad si ya está el SKU.
 *   Si el producto no tiene ranura (una consola, un juego), el estado no cambia.
 * - `setQuantity` con 0 quita la línea.
 */
export function buildReducer(state: BuildState, action: BuildAction): BuildState {
  switch (action.type) {
    case 'add': {
      const slot = action.slot ? slotByKey(action.slot) : slotForProduct(action.product);
      if (!slot) return state;
      const quantity = Math.max(1, clampQuantity(action.quantity ?? 1, action.product));
      const existing = state.lines.find((line) => line.product.sku === action.product.sku);
      if (existing) {
        const merged = slot.multiple ? clampQuantity(existing.quantity + quantity, action.product) : quantity;
        return {
          lines: state.lines.map((line) =>
            line.product.sku === action.product.sku ? { ...line, slot: slot.key, quantity: merged } : line,
          ),
        };
      }
      const kept = slot.multiple ? state.lines : state.lines.filter((line) => line.slot !== slot.key);
      return { lines: sortLines([...kept, { slot: slot.key, product: action.product, quantity }]) };
    }
    case 'remove':
      return { lines: state.lines.filter((line) => line.product.sku !== action.sku) };
    case 'setQuantity': {
      const target = state.lines.find((line) => line.product.sku === action.sku);
      if (!target) return state;
      const quantity = clampQuantity(action.quantity, target.product);
      if (quantity === 0) return { lines: state.lines.filter((line) => line.product.sku !== action.sku) };
      return {
        lines: state.lines.map((line) => (line.product.sku === action.sku ? { ...line, quantity } : line)),
      };
    }
    case 'clear':
      return EMPTY_BUILD;
    case 'loadPreset':
      return { lines: sortLines(action.lines.map((line) => ({ ...line, quantity: Math.max(1, clampQuantity(line.quantity, line.product)) }))) };
    default:
      return state;
  }
}

/** Subtotal de una línea (precio × cantidad, redondeado a centavos). */
export function lineTotal(line: Pick<BuildLine, 'product' | 'quantity'>): number {
  return roundMoney(line.product.price * line.quantity);
}

/** Suma simple de precio × cantidad (sin envío ni descuentos: no hay checkout). */
export function buildTotal(lines: readonly BuildLine[]): number {
  return sumMoney(lines.map(lineTotal));
}

/** Ahorro total frente a los precios de lista de las piezas en oferta. */
export function buildSavings(lines: readonly BuildLine[]): number {
  return sumMoney(lines.map((line) => savingAmount(line.product.price, line.product.listPrice) * line.quantity));
}

/** Cantidad de piezas (suma de cantidades). */
export function buildCount(lines: readonly BuildLine[]): number {
  return lines.reduce((acc, line) => acc + line.quantity, 0);
}

export function linesForSlot(lines: readonly BuildLine[], slot: SlotKey): BuildLine[] {
  return lines.filter((line) => line.slot === slot);
}

export function isInBuild(lines: readonly BuildLine[], sku: string): boolean {
  return lines.some((line) => line.product.sku === sku);
}

/** Ranuras obligatorias que todavía no tienen pieza. */
export function missingSlots(lines: readonly BuildLine[]): BuildSlot[] {
  const covered = new Set(lines.map((line) => line.slot));
  return REQUIRED_SLOTS.filter((slot) => !covered.has(slot.key));
}

export interface BuildProgress {
  /** Ranuras obligatorias cubiertas. */
  covered: number;
  /** Total de ranuras obligatorias. */
  required: number;
  /** 0 a 1. */
  ratio: number;
  /** Verdadero cuando están todas las obligatorias. */
  complete: boolean;
}

export function buildProgress(lines: readonly BuildLine[]): BuildProgress {
  const required = REQUIRED_SLOTS.length;
  const covered = required - missingSlots(lines).length;
  return { covered, required, ratio: required === 0 ? 1 : covered / required, complete: covered === required };
}

export interface BuildSlotSummary {
  slot: BuildSlot;
  lines: BuildLine[];
  subtotal: number;
}

export interface BuildSummary {
  lines: BuildLine[];
  total: number;
  savings: number;
  count: number;
  progress: BuildProgress;
  missing: BuildSlot[];
  /** Todas las ranuras en orden, con sus líneas (vacías si no hay pieza). */
  slots: BuildSlotSummary[];
}

export function summarizeBuild(lines: readonly BuildLine[]): BuildSummary {
  const sorted = sortLines(lines);
  return {
    lines: sorted,
    total: buildTotal(sorted),
    savings: buildSavings(sorted),
    count: buildCount(sorted),
    progress: buildProgress(sorted),
    missing: missingSlots(sorted),
    slots: BUILD_SLOTS.map((slot) => {
      const slotLines = linesForSlot(sorted, slot.key);
      return { slot, lines: slotLines, subtotal: buildTotal(slotLines) };
    }),
  };
}
