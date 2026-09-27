// Tres contextos para que cada consumidor se vuelva a dibujar solo con lo que usa: el estado del armado (cambia con
// cada pieza), las acciones (estables) y el cajón «Mi armado» (cambia al abrir o cerrar). `useBuilder()` los combina.

import { createContext } from 'react';
import type { BuildProgress, BuildSummary } from '@/1-domain/builder/build';
import type { BuildLine, BuildSlot, SlotKey } from '@/1-domain/builder/types';
import type { Product } from '@/1-domain/catalog/types';

export interface BuildStateApi {
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
  isInBuild(sku: string): boolean;
}

export interface AddOptions {
  /** Sin aviso (toast): el armador ya anuncia el cambio en su propia región viva. */
  silent?: boolean;
}

export interface BuildActionsApi {
  /** Agrega un producto (ranura inferida por su categoría si no se indica). Devuelve false si no se puede armar con él. */
  add(product: Product, slot?: SlotKey, quantity?: number, options?: AddOptions): boolean;
  remove(sku: string): void;
  setQuantity(sku: string, quantity: number): void;
  clear(): void;
  /** Reemplaza el armado con un armado sugerido del catálogo. Devuelve false si el id no existe. */
  loadPreset(id: string, options?: AddOptions): boolean;
}

export interface BuildDrawerApi {
  /** Cajón lateral «Mi armado». */
  isDrawerOpen: boolean;
  openDrawer(): void;
  closeDrawer(): void;
}

export type BuilderApi = BuildStateApi & BuildActionsApi & BuildDrawerApi;

export const BuildStateContext = createContext<BuildStateApi | null>(null);
export const BuildActionsContext = createContext<BuildActionsApi | null>(null);
export const BuildDrawerContext = createContext<BuildDrawerApi | null>(null);
