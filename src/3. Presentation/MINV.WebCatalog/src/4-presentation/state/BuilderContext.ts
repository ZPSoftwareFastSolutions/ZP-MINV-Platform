import { createContext } from 'react';
import type { BuildProgress, BuildSummary } from '@/1-domain/builder/build';
import type { BuildLine, BuildSlot, SlotKey } from '@/1-domain/builder/types';
import type { Product } from '@/1-domain/catalog/types';

export interface BuilderApi {
  /** Líneas del armado en el orden de las ranuras. */
  lines: BuildLine[];
  /** Piezas (suma de cantidades). */
  count: number;
  total: number;
  /** Ahorro frente a los precios de lista. */
  savings: number;
  progress: BuildProgress;
  /** Ranuras obligatorias sin pieza. */
  missing: BuildSlot[];
  summary: BuildSummary;
  /** Agrega un producto (ranura inferida por su categoría si no se indica). Devuelve false si no se puede armar con él. */
  add(product: Product, slot?: SlotKey, quantity?: number): boolean;
  remove(sku: string): void;
  setQuantity(sku: string, quantity: number): void;
  clear(): void;
  /** Reemplaza el armado con un armado sugerido del catálogo. Devuelve false si el id no existe. */
  loadPreset(id: string): boolean;
  isInBuild(sku: string): boolean;
  /** Cajón lateral «Mi armado». */
  isDrawerOpen: boolean;
  openDrawer(): void;
  closeDrawer(): void;
}

export const BuilderContext = createContext<BuilderApi | null>(null);
