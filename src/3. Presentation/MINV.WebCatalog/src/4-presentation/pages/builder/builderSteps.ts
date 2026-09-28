// Reglas de presentación del armador «Armá tu PC» (sin React): qué especificaciones resumir por ranura, cuál es el
// siguiente paso y los datos de referencia del catálogo. Desde la V6 el número del armado lo da la tienda al reservar
// (`ARM-WEB-000001`): aquí ya no se inventa ninguno. Nada de aquí valida compatibilidad: solo elige qué mostrar y en qué orden.

import { BUILD_SLOTS } from '@/1-domain/builder/slots';
import type { BuildLine, BuildSlot, PresetTier, SlotKey } from '@/1-domain/builder/types';
import type { Product, Spec } from '@/1-domain/catalog/types';

/** Especificaciones clave que resume la tarjeta de candidato de cada ranura (2-3, con sus unidades en `text`). */
const KEY_SPECS: Partial<Record<SlotKey, readonly string[]>> = {
  cpu: ['nucleos', 'frecuencia_turbo', 'socket'],
  motherboard: ['socket', 'chipset', 'tipo_ram'],
  ram: ['capacidad_total', 'velocidad', 'tipo_ram'],
  gpu: ['vram', 'fuente_recomendada', 'largo'],
  storage: ['capacidad', 'tecnologia', 'lectura'],
  psu: ['potencia', 'certificacion', 'modularidad'],
  case: ['formatos_placa', 'largo_max_gpu', 'tipo_gabinete'],
  cooler: ['tipo_refrigeracion', 'tdp_soportado', 'sockets_compatibles'],
  monitor: ['tamano', 'resolucion', 'frecuencia'],
};

/** Especificaciones que no aportan en un resumen de 3 líneas (ya se ven en insignias o en el nombre). */
const GENERIC_SKIP = new Set(['condicion', 'color']);

/**
 * Hasta `limit` especificaciones clave de un producto para su ranura. En las ranuras mixtas (periféricos, software)
 * se toman las filtrables del propio producto, sin la condición ni el color.
 */
export function keySpecsFor(product: Pick<Product, 'specs'>, slot: SlotKey, limit = 3): Spec[] {
  const preferred = KEY_SPECS[slot];
  if (preferred) {
    return preferred
      .map((key) => product.specs.find((spec) => spec.key === key))
      .filter((spec): spec is Spec => spec !== undefined)
      .slice(0, limit);
  }
  return product.specs.filter((spec) => spec.filterable && !GENERIC_SKIP.has(spec.key)).slice(0, limit);
}

/** Ranura que sigue en el orden de armado, o undefined en la última. */
export function slotAfter(slot: SlotKey): BuildSlot | undefined {
  const index = BUILD_SLOTS.findIndex((candidate) => candidate.key === slot);
  return index >= 0 ? BUILD_SLOTS[index + 1] : undefined;
}

/**
 * Siguiente paso que conviene abrir después de elegir en `current`: la primera ranura posterior sin pieza y no
 * omitida; si no hay, la primera anterior en esas condiciones; undefined cuando todo está resuelto.
 */
export function nextOpenSlot(current: SlotKey, lines: readonly BuildLine[], skipped: ReadonlySet<SlotKey>): BuildSlot | undefined {
  const covered = new Set(lines.map((line) => line.slot));
  const pending = (slot: BuildSlot) => slot.key !== current && !covered.has(slot.key) && !skipped.has(slot.key);
  const index = BUILD_SLOTS.findIndex((slot) => slot.key === current);
  return BUILD_SLOTS.slice(index + 1).find(pending) ?? BUILD_SLOTS.slice(0, index).find(pending);
}

/** Paso con el que conviene empezar: la primera ranura obligatoria sin pieza (o la primera de todas si están cubiertas). */
export function initialOpenSlot(lines: readonly BuildLine[]): SlotKey | null {
  const covered = new Set(lines.map((line) => line.slot));
  const firstMissing = BUILD_SLOTS.find((slot) => slot.required && !covered.has(slot.key));
  if (firstMissing) return firstMissing.key;
  return lines.length === 0 ? BUILD_SLOTS[0].key : null;
}

export type StepStatus = 'elegido' | 'omitido' | 'pendiente';

export function stepStatus(slot: BuildSlot, lines: readonly BuildLine[], skipped: ReadonlySet<SlotKey>): StepStatus {
  if (lines.some((line) => line.slot === slot.key)) return 'elegido';
  if (skipped.has(slot.key)) return 'omitido';
  return 'pendiente';
}

export interface ReferenceItem {
  key: string;
  /** Pieza de la que sale el dato («Procesador»). */
  source: string;
  label: string;
  value: string;
}

/** Dato de referencia: (ranura, clave de especificación, etiqueta corta). */
const REFERENCE_SPECS: readonly [SlotKey, string, string][] = [
  ['cpu', 'tdp', 'TDP del procesador'],
  ['gpu', 'consumo', 'Consumo de la tarjeta de video'],
  ['gpu', 'fuente_recomendada', 'Fuente que recomienda el fabricante de la GPU'],
  ['gpu', 'largo', 'Largo de la tarjeta de video'],
  ['psu', 'potencia', 'Potencia de la fuente elegida'],
  ['cooler', 'tdp_soportado', 'TDP que soporta la refrigeración'],
  ['case', 'largo_max_gpu', 'Largo máximo de GPU del gabinete'],
  ['motherboard', 'tipo_ram', 'Memoria que admite la placa madre'],
  ['ram', 'tipo_ram', 'Tipo de la memoria elegida'],
];

/**
 * Datos informativos que trae el catálogo sobre las piezas elegidas (potencia de la fuente, TDP del CPU…).
 * Se muestran etiquetados como referencia: el sitio NO valida compatibilidad.
 */
export function referenceData(lines: readonly BuildLine[]): ReferenceItem[] {
  const items: ReferenceItem[] = [];
  for (const [slotKey, specKey, label] of REFERENCE_SPECS) {
    const line = lines.find((candidate) => candidate.slot === slotKey);
    const spec = line?.product.specs.find((candidate) => candidate.key === specKey);
    if (!line || !spec) continue;
    const slot = BUILD_SLOTS.find((candidate) => candidate.key === slotKey);
    items.push({ key: `${slotKey}:${specKey}`, source: slot?.label ?? slotKey, label, value: spec.text });
  }
  return items;
}

export const TIER_LABELS: Record<PresetTier, string> = {
  entrada: 'Gama de entrada',
  media: 'Gama media',
  alta: 'Gama alta',
  entusiasta: 'Entusiasta',
  oficina: 'Oficina y estudio',
  creador: 'Creadores y streaming',
};

/** Ranuras cuyas piezas se destacan al presentar un armado sugerido (las que definen su rendimiento). */
export const PRESET_KEY_SLOTS: readonly SlotKey[] = ['cpu', 'gpu', 'ram', 'storage'];

/** Piezas clave de un armado sugerido (hasta `limit`), en el orden de PRESET_KEY_SLOTS. */
export function presetKeyLines(lines: readonly BuildLine[], limit = 3): BuildLine[] {
  const picked: BuildLine[] = [];
  for (const slot of PRESET_KEY_SLOTS) {
    const line = lines.find((candidate) => candidate.slot === slot);
    if (line) picked.push(line);
    if (picked.length === limit) break;
  }
  return picked;
}

/** Fecha larga en español de Bolivia («26 de septiembre de 2026»). */
export function formatLongDate(date: Date): string {
  return new Intl.DateTimeFormat('es-BO', { dateStyle: 'long' }).format(date);
}
