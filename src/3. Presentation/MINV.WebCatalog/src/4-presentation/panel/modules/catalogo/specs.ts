// Módulo «Catálogo» · funciones puras de las FICHAS TÉCNICAS (regla T-01): la pestaña «Ficha técnica» del producto (cada
// especificación con el control de su tipo: número con unidad, texto, una opción o varias), la lista de la pestaña
// «Especificaciones» y el formulario de una especificación. Las reglas de verdad (tipo del valor, opción de ESA
// especificación, obligatorias, herencia, garantía, control por serie) las valida el servidor; aquí solo se avisa antes.

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses } from '@/4-presentation/panel/kit';
import { matchesSearch, type CsvColumn } from '@/4-presentation/panel/lib';
import { showNumber, type Option, type SpecDefinitionRecord } from './catalog';

/** Ficha técnica de un producto (`GetProductTechQuery`). */
export type ProductTechRecord = RpcResponseOf<'GetProductTechQuery'>;
export type SaveTechPayload = RpcRequestOf<'SaveProductTechCommand'>;
export type SaveSpecPayload = RpcRequestOf<'SaveSpecDefinitionCommand'>;
export type SerialKindValue = SaveTechPayload['serialKind'];
export type SpecDataTypeValue = SaveSpecPayload['dataType'];

// ---------------------------------------------------------------------------------------------------- textos

export const SPEC_TYPES = defineStatuses({
  Text: { label: 'Texto', tone: 'neutral' },
  Number: { label: 'Número', tone: 'info' },
  Option: { label: 'Opción', tone: 'accent' },
});

export const SPEC_TYPE_OPTIONS: readonly { value: SpecDataTypeValue; label: string; description: string }[] = [
  { value: 'Text', label: 'Texto', description: 'Un texto libre (modelo, chipset).' },
  { value: 'Number', label: 'Número (con unidad)', description: 'Una medida: GB, W, mm, Hz.' },
  { value: 'Option', label: 'Opción (lista de valores)', description: 'Se elige de una lista: socket, tipo de RAM, plataforma.' },
];

export function specTypeText(dataType: string, multi: boolean): string {
  const base = dataType === 'Number' ? 'Número' : dataType === 'Option' ? 'Opción' : 'Texto';
  return multi ? `${base} (varios valores)` : base;
}

export const SERIAL_KIND_OPTIONS: readonly { value: SerialKindValue; label: string; description: string }[] = [
  { value: 'Serial', label: 'Número de serie del fabricante', description: 'Computadoras, componentes, consolas, periféricos.' },
  { value: 'Imei', label: 'IMEI (15 dígitos)', description: 'Celulares y equipos con SIM.' },
];

/**
 * Nombre en español de una clave de compatibilidad del armador (las claves son constantes del servidor,
 * `CompatibilityKeys`; aquí solo se traducen para mostrarlas, como el escritorio). Una clave nueva se muestra tal cual.
 */
const COMPATIBILITY_LABELS: Readonly<Record<string, string>> = {
  cpu_socket: 'Socket del procesador',
  ram_type: 'Tipo de RAM',
  ram_slots: 'Ranuras de RAM de la placa',
  ram_max_gb: 'RAM máxima de la placa (GB)',
  form_factor: 'Formato de la placa',
  case_form_factors: 'Formatos de placa del gabinete',
  gpu_length_mm: 'Largo de la tarjeta de video (mm)',
  case_max_gpu_mm: 'Largo máximo de GPU del gabinete (mm)',
  power_draw_w: 'Consumo (W)',
  cpu_tdp_w: 'TDP del procesador (W)',
  psu_watts: 'Potencia de la fuente (W)',
  m2_slots: 'Ranuras M.2 de la placa',
  cooler_sockets: 'Sockets del disipador',
  igpu: 'Gráficos integrados',
  storage_interface: 'Interfaz de almacenamiento',
  ram_modules: 'Módulos del kit de RAM',
  ram_capacity_gb: 'Capacidad del kit de RAM (GB)',
};

export function compatibilityLabel(key: string): string {
  return COMPATIBILITY_LABELS[key] ?? key;
}

/**
 * Claves del armador que se pueden elegir: las que ya usan las especificaciones del servidor (no hay una operación que
 * liste `CompatibilityKeys.All`; ver «Pendientes» del informe) más la de la especificación que se edita.
 */
export function compatibilityOptions(definitions: readonly SpecDefinitionRecord[], current: string | null): Option[] {
  const keys = new Set(definitions.map((definition) => definition.compatibilityKey).filter((key): key is string => Boolean(key)));
  if (current) keys.add(current);
  return [...keys].map((key) => ({ value: key, label: compatibilityLabel(key) })).sort((a, b) => a.label.localeCompare(b.label, 'es'));
}

/** Marcas de una especificación: obligatoria, filtrable, armador y herencia. */
export function specFlags(definition: SpecDefinitionRecord): string[] {
  return [
    definition.isRequired ? 'Obligatoria' : null,
    definition.isFilterable ? 'Filtra el catálogo' : null,
    definition.compatibilityKey ? `Armador: ${compatibilityLabel(definition.compatibilityKey)}` : null,
    definition.isInherited ? `Heredada de ${definition.categoryName}` : null,
  ].filter((flag): flag is string => flag !== null);
}

// ---------------------------------------------------------------------------------------------------- ficha técnica (producto)

export type SpecControl = 'number' | 'text' | 'option-one' | 'option-many';

export function controlOf(definition: SpecDefinitionRecord): SpecControl {
  if (definition.dataType === 'Option') return definition.isMultiValued ? 'option-many' : 'option-one';
  return definition.dataType === 'Number' ? 'number' : 'text';
}

/** Lo que se escribió o eligió en una especificación. */
export interface SpecValueDraft {
  /** Texto o número (varios valores separados por comas). */
  text: string;
  /** Opciones elegidas. */
  picked: string[];
}

export interface TechDraft {
  trackSerials: boolean;
  serialKind: SerialKindValue;
  warrantyMonths: number | null;
  /** Por código de especificación. */
  values: Record<string, SpecValueDraft>;
}

/** La ficha guardada del producto (o vacía, si es nuevo) lista para editar. */
export function techDraftOf(tech: ProductTechRecord | null | undefined): TechDraft {
  const values: Record<string, SpecValueDraft> = {};
  for (const spec of tech?.specs ?? []) {
    const shown = spec.dataType === 'Number' ? spec.values.map(showNumber) : spec.values;
    values[spec.code] = { text: shown.join(', '), picked: [...spec.values] };
  }
  return { trackSerials: tech?.trackSerials ?? false, serialKind: tech?.serialKind ?? 'Serial', warrantyMonths: tech?.warrantyMonths ?? 0, values };
}

const NUMBER = /^-?\d+(?:[.,]\d+)?$/;

/** Valores de una especificación para el servidor: las opciones elegidas (de ESA especificación) o lo escrito. */
export function specValues(definition: SpecDefinitionRecord, entry: SpecValueDraft | undefined): string[] {
  if (!entry) return [];
  const control = controlOf(definition);
  if (control === 'option-one' || control === 'option-many') {
    const picked = definition.options.filter((option) => entry.picked.some((value) => value.toLowerCase() === option.toLowerCase()));
    return control === 'option-one' ? picked.slice(0, 1) : picked;
  }
  const parts = definition.isMultiValued ? entry.text.split(/[,;]/) : [entry.text];
  return parts
    .map((part) => part.trim())
    .filter((part) => part.length > 0)
    .map((part) => (control === 'number' && NUMBER.test(part) ? part.replace(',', '.') : part));
}

export interface TechProblems {
  warranty?: string;
  /** Por código de especificación. */
  specs: Record<string, string>;
}

export function hasTechProblems(problems: TechProblems): boolean {
  return problems.warranty !== undefined || Object.keys(problems.specs).length > 0;
}

/** Guía antes de enviar: garantía de 0 a 120 meses, números bien escritos y las obligatorias con valor. */
export function techProblems(definitions: readonly SpecDefinitionRecord[], draft: TechDraft): TechProblems {
  const problems: TechProblems = { specs: {} };
  const months = draft.warrantyMonths;
  if (months === null || !Number.isInteger(months) || months < 0 || months > 120) problems.warranty = 'La garantía va de 0 a 120 meses.';
  for (const definition of definitions) {
    const values = specValues(definition, draft.values[definition.code]);
    if (controlOf(definition) === 'number') {
      const wrong = values.find((value) => !NUMBER.test(value));
      if (wrong !== undefined) {
        problems.specs[definition.code] = definition.isMultiValued ? `«${wrong}» no es un número (separe los valores con comas y use punto decimal).` : `«${wrong}» no es un número.`;
        continue;
      }
    }
    if (definition.isRequired && values.length === 0) problems.specs[definition.code] = `Indique ${definition.name.toLowerCase()}: es obligatoria.`;
  }
  return problems;
}

/** `SaveProductTechCommand` (reemplaza la ficha anterior): solo las especificaciones de la categoría que tienen valor. */
export function toSaveTech(sku: string, definitions: readonly SpecDefinitionRecord[], draft: TechDraft): SaveTechPayload {
  return {
    sku,
    trackSerials: draft.trackSerials,
    serialKind: draft.serialKind,
    warrantyMonths: draft.warrantyMonths ?? 0,
    specs: definitions.map((definition) => ({ code: definition.code, values: specValues(definition, draft.values[definition.code]) })).filter((spec) => spec.values.length > 0),
  };
}

export function serialHelp(draft: TechDraft, saved: ProductTechRecord | null | undefined): string {
  if (!draft.trackSerials) return 'El producto se vende por cantidad, sin serie.';
  if (saved?.trackSerials) {
    const units = saved.serialsInStock === 1 ? '1 unidad' : `${saved.serialsInStock} unidades`;
    return `Cada unidad entra, se vende y vuelve por garantía con su serie o IMEI (${units} con serie en stock).`;
  }
  return 'Cada unidad entra, se vende y vuelve por garantía con su serie o IMEI. Para activarlo en un producto con existencias, cada unidad en stock debe tener su serie (Series › Registrar series de stock), y el cambio lo hace una sesión con todas las sucursales.';
}

// ---------------------------------------------------------------------------------------------------- lista de especificaciones

export interface SpecEntry {
  key: string;
  definition: SpecDefinitionRecord;
  typeText: string;
  flags: string[];
  optionsText: string;
}

export function toSpecEntries(definitions: readonly SpecDefinitionRecord[]): SpecEntry[] {
  return definitions.map((definition) => ({
    key: `${definition.categoryCode}|${definition.code}`,
    definition,
    typeText: specTypeText(definition.dataType, definition.isMultiValued),
    flags: specFlags(definition),
    optionsText: definition.options.join(', '),
  }));
}

/** Filtros de la pestaña «Especificaciones» (con el prefijo `e_` en la dirección). */
export const SPEC_FILTERS = { categoria: '', q: '', tipo: '', uso: '' };
export type SpecFilters = typeof SPEC_FILTERS;
export const SPEC_PREFIX = 'e_';

export const SPEC_USE_OPTIONS: readonly Option[] = [
  { value: 'obligatoria', label: 'Obligatorias' },
  { value: 'filtrable', label: 'Filtran el catálogo' },
  { value: 'armador', label: 'Las usa el armador de PC' },
  { value: 'heredada', label: 'Heredadas de otra categoría' },
];

export function filterSpecs(entries: readonly SpecEntry[], filters: SpecFilters): SpecEntry[] {
  return entries.filter(({ definition, optionsText }) => {
    if (filters.tipo && definition.dataType !== filters.tipo) return false;
    if (filters.uso === 'obligatoria' && !definition.isRequired) return false;
    if (filters.uso === 'filtrable' && !definition.isFilterable) return false;
    if (filters.uso === 'armador' && !definition.compatibilityKey) return false;
    if (filters.uso === 'heredada' && !definition.isInherited) return false;
    return matchesSearch(filters.q, [definition.name, definition.code, definition.categoryName, definition.unit, optionsText]);
  });
}

export const SPECS_CSV: readonly CsvColumn<SpecEntry>[] = [
  { header: 'Categoría', value: (entry) => entry.definition.categoryName },
  { header: 'Código', value: (entry) => entry.definition.code },
  { header: 'Especificación', value: (entry) => entry.definition.name },
  { header: 'Unidad', value: (entry) => entry.definition.unit },
  { header: 'Tipo', value: (entry) => entry.typeText },
  { header: 'Opciones', value: (entry) => entry.optionsText },
  { header: 'Obligatoria', value: (entry) => entry.definition.isRequired },
  { header: 'Filtrable', value: (entry) => entry.definition.isFilterable },
  { header: 'Clave del armador', value: (entry) => (entry.definition.compatibilityKey ? compatibilityLabel(entry.definition.compatibilityKey) : null) },
  { header: 'Heredada', value: (entry) => entry.definition.isInherited },
  { header: 'Orden', value: (entry) => entry.definition.sortOrder },
];

// ---------------------------------------------------------------------------------------------------- formulario de una especificación

export interface SpecDraft {
  categoryCode: string;
  code: string;
  /** El código se escribió a mano (ya no se sugiere desde el nombre). */
  codeTouched: boolean;
  name: string;
  unit: string;
  dataType: SpecDataTypeValue;
  isMultiValued: boolean;
  isFilterable: boolean;
  isRequired: boolean;
  compatibilityKey: string;
  sortOrder: number | null;
  /** Una opción por renglón (en ese orden). */
  options: string;
}

export const SPEC_LIMITS = { code: 40, name: 80, unit: 20, option: 60 } as const;

/** Orden sugerido para una especificación nueva: la última de la categoría + 10. */
export function nextSortOrder(definitions: readonly SpecDefinitionRecord[], categoryCode: string): number {
  const own = definitions.filter((definition) => definition.categoryCode === categoryCode && !definition.isInherited).map((definition) => definition.sortOrder);
  return (own.length > 0 ? Math.max(...own) : 0) + 10;
}

export function specDraftOf(definition: SpecDefinitionRecord | null, categoryCode: string, sortOrder: number): SpecDraft {
  if (definition) {
    return {
      categoryCode: definition.categoryCode,
      code: definition.code,
      codeTouched: true,
      name: definition.name,
      unit: definition.unit ?? '',
      dataType: definition.dataType,
      isMultiValued: definition.isMultiValued,
      isFilterable: definition.isFilterable,
      isRequired: definition.isRequired,
      compatibilityKey: definition.compatibilityKey ?? '',
      sortOrder: definition.sortOrder,
      options: definition.options.join('\n'),
    };
  }
  return {
    categoryCode,
    code: '',
    codeTouched: false,
    name: '',
    unit: '',
    dataType: 'Text',
    isMultiValued: false,
    isFilterable: true,
    isRequired: false,
    compatibilityKey: '',
    sortOrder,
    options: '',
  };
}

/** Código sugerido a partir del nombre: «Tipo de RAM» → «tipo_de_ram». */
export function suggestSpecCode(name: string): string {
  return name
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '')
    .slice(0, SPEC_LIMITS.code);
}

/** Opciones escritas (una por renglón, sin vacías). */
export function optionLines(text: string): string[] {
  return text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

export type SpecField = 'categoryCode' | 'code' | 'name' | 'unit' | 'sortOrder' | 'options';
export type SpecProblems = Partial<Record<SpecField, string>>;

const SPEC_CODE = /^[a-z0-9][a-z0-9_-]*$/;

export function specProblems(draft: SpecDraft, isNew: boolean): SpecProblems {
  const problems: SpecProblems = {};
  if (!draft.categoryCode) problems.categoryCode = 'Elija la categoría.';
  if (isNew) {
    const code = draft.code.trim().toLowerCase();
    if (!code) problems.code = 'Indique el código de la especificación.';
    else if (code.length > SPEC_LIMITS.code) problems.code = `Use como máximo ${SPEC_LIMITS.code} caracteres.`;
    else if (!SPEC_CODE.test(code)) problems.code = 'Minúsculas, números, guion y guion bajo (sin espacios).';
  }
  const name = draft.name.trim();
  if (!name) problems.name = 'Indique el nombre de la especificación.';
  else if (name.length > SPEC_LIMITS.name) problems.name = `Use como máximo ${SPEC_LIMITS.name} caracteres.`;
  if (draft.unit.trim().length > SPEC_LIMITS.unit) problems.unit = `Use como máximo ${SPEC_LIMITS.unit} caracteres.`;
  if (draft.sortOrder === null || !Number.isInteger(draft.sortOrder)) problems.sortOrder = 'El orden es un número entero (10, 20, 30…).';
  if (draft.dataType === 'Option') {
    const options = optionLines(draft.options);
    const seen = new Set<string>();
    const repeated = options.find((option) => {
      const key = option.toLowerCase();
      if (seen.has(key)) return true;
      seen.add(key);
      return false;
    });
    if (options.length === 0) problems.options = 'Escriba al menos una opción (una por renglón).';
    else if (options.some((option) => option.length > SPEC_LIMITS.option)) problems.options = `Cada opción admite hasta ${SPEC_LIMITS.option} caracteres.`;
    else if (repeated) problems.options = `La opción «${repeated}» está repetida.`;
  }
  return problems;
}

/** `SaveSpecDefinitionCommand` con TODOS sus parámetros (las opciones solo en las de tipo opción). */
export function toSaveSpec(draft: SpecDraft): SaveSpecPayload {
  const unit = draft.unit.trim();
  return {
    categoryCode: draft.categoryCode,
    code: draft.code.trim().toLowerCase(),
    name: draft.name.trim(),
    unit: unit.length > 0 ? unit : null,
    dataType: draft.dataType,
    isMultiValued: draft.isMultiValued,
    isFilterable: draft.isFilterable,
    isRequired: draft.isRequired,
    compatibilityKey: draft.compatibilityKey || null,
    sortOrder: draft.sortOrder ?? 0,
    options: draft.dataType === 'Option' ? optionLines(draft.options) : [],
  };
}
