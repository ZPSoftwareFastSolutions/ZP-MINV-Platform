// Dominio del armador de PC (V5). Solo estado en memoria: nada persiste ni se valida compatibilidad.

import type { CategoryCode, Product } from '@/1-domain/catalog/types';

export type SlotKey =
  | 'cpu'
  | 'motherboard'
  | 'ram'
  | 'gpu'
  | 'storage'
  | 'psu'
  | 'case'
  | 'cooler'
  | 'monitor'
  | 'peripherals'
  | 'software';

export interface BuildSlot {
  key: SlotKey;
  label: string;
  /** Categorías del catálogo cuyos productos pueden ocupar la ranura. */
  categories: CategoryCode[];
  required: boolean;
  /** Admite varias piezas (almacenamiento, periféricos). */
  multiple: boolean;
  order: number;
  /** Consejo corto que se muestra en el paso («El procesador define la potencia…»). */
  hint: string;
  icon: string;
}

export interface BuildLine {
  slot: SlotKey;
  product: Product;
  quantity: number;
}

export type PresetTier = 'entrada' | 'media' | 'alta' | 'entusiasta' | 'oficina' | 'creador';

export interface BuildPreset {
  id: string;
  name: string;
  tier: PresetTier;
  lines: { slot: SlotKey; sku: string; quantity: number }[];
}
