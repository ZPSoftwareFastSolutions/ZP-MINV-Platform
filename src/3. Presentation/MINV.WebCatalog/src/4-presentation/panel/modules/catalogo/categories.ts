// Módulo «Catálogo» · funciones puras de la pestaña «Categorías»: la fila (con cuántos productos y especificaciones
// propias tiene cada categoría), los filtros, el CSV y el formulario (nueva categoría, subcategoría o cambio de nombre).

import type { RpcRequestOf } from '@/4-presentation/app/contract';
import { matchesSearch, type CsvColumn } from '@/4-presentation/panel/lib';
import { suggestCategoryCode, type CatalogOptionsRecord, type CatalogRecord, type Option, type SpecDefinitionRecord } from './catalog';

export type SaveCategoryPayload = RpcRequestOf<'SaveCategoryCommand'>;

export interface CategoryEntry {
  key: string;
  code: string;
  name: string;
  /** Productos propios (sin contar los de sus subcategorías). */
  products: number;
  activeProducts: number;
  /** Especificaciones propias (sin las heredadas). */
  specs: number;
}

export function toCategoryEntries(options: CatalogOptionsRecord | undefined, catalog: readonly CatalogRecord[], definitions: readonly SpecDefinitionRecord[]): CategoryEntry[] {
  return (options?.categories ?? []).map((category) => {
    const own = catalog.filter((row) => row.categoryCode === category.code);
    return {
      key: category.code,
      code: category.code,
      name: category.name,
      products: own.length,
      activeProducts: own.filter((row) => row.isActive).length,
      specs: definitions.filter((definition) => definition.categoryCode === category.code && !definition.isInherited).length,
    };
  });
}

/** Filtros de la pestaña «Categorías» (con el prefijo `c_` en la dirección). */
export const CATEGORY_FILTERS = { q: '', productos: '', especificaciones: '' };
export type CategoryFilters = typeof CATEGORY_FILTERS;
export const CATEGORY_PREFIX = 'c_';

export const WITH_OPTIONS: readonly Option[] = [
  { value: 'con', label: 'Con alguno' },
  { value: 'sin', label: 'Sin ninguno' },
];

export function filterCategories(entries: readonly CategoryEntry[], filters: CategoryFilters): CategoryEntry[] {
  return entries.filter((entry) => {
    if (filters.productos === 'con' && entry.products === 0) return false;
    if (filters.productos === 'sin' && entry.products > 0) return false;
    if (filters.especificaciones === 'con' && entry.specs === 0) return false;
    if (filters.especificaciones === 'sin' && entry.specs > 0) return false;
    return matchesSearch(filters.q, [entry.name, entry.code]);
  });
}

export const CATEGORIES_CSV: readonly CsvColumn<CategoryEntry>[] = [
  { header: 'Código', value: (entry) => entry.code },
  { header: 'Categoría', value: (entry) => entry.name },
  { header: 'Productos', value: (entry) => entry.products },
  { header: 'Productos activos', value: (entry) => entry.activeProducts },
  { header: 'Especificaciones propias', value: (entry) => entry.specs },
];

// ---------------------------------------------------------------------------------------------------- formulario

/** Qué se hace: una categoría nueva (con madre opcional), una subcategoría de otra o cambiarle el nombre. */
export type CategoryMode = 'nueva' | 'subcategoria' | 'renombrar';

export interface CategoryDraft {
  name: string;
  code: string;
  /** El código se escribió a mano (ya no se sugiere desde el nombre). */
  codeTouched: boolean;
  parentCode: string;
}

export const CATEGORY_LIMITS = { code: 20, name: 80 } as const;

export function categoryDraftOf(mode: CategoryMode, target: { code: string; name: string } | null): CategoryDraft {
  if (mode === 'renombrar' && target) return { name: target.name, code: target.code, codeTouched: true, parentCode: '' };
  return { name: '', code: '', codeTouched: false, parentCode: mode === 'subcategoria' && target ? target.code : '' };
}

/** Al escribir el nombre de una categoría nueva, el código se sugiere solo (hasta que se escriba a mano). */
export function withCategoryName(draft: CategoryDraft, name: string, existing: readonly string[]): CategoryDraft {
  return { ...draft, name, code: draft.codeTouched ? draft.code : name.trim() ? suggestCategoryCode(name, existing) : '' };
}

export type CategoryProblems = Partial<Record<'name' | 'code', string>>;

const CODE_PATTERN = /^[A-Za-z0-9][A-Za-z0-9_-]*$/;

export function categoryProblems(draft: CategoryDraft, mode: CategoryMode, existing: readonly string[]): CategoryProblems {
  const problems: CategoryProblems = {};
  const name = draft.name.trim();
  if (!name) problems.name = 'Indique el nombre de la categoría.';
  else if (name.length > CATEGORY_LIMITS.name) problems.name = `Use como máximo ${CATEGORY_LIMITS.name} caracteres.`;
  if (mode !== 'renombrar') {
    const code = draft.code.trim().toUpperCase();
    if (!code) problems.code = 'Indique el código de la categoría.';
    else if (code.length > CATEGORY_LIMITS.code) problems.code = `Use como máximo ${CATEGORY_LIMITS.code} caracteres.`;
    else if (!CODE_PATTERN.test(code)) problems.code = 'Solo letras, números, guion y guion bajo (sin espacios).';
    else if (existing.some((item) => item.toUpperCase() === code)) problems.code = `Ya existe una categoría con el código ${code}.`;
  }
  return problems;
}

/** `SaveCategoryCommand`: una existente no cambia de madre (al renombrar va `parentCode: null`). */
export function toSaveCategory(draft: CategoryDraft, mode: CategoryMode): SaveCategoryPayload {
  return {
    code: draft.code.trim().toUpperCase(),
    name: draft.name.trim(),
    parentCode: mode === 'renombrar' || !draft.parentCode ? null : draft.parentCode,
  };
}
