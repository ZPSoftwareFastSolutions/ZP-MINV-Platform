// El registro de módulos del panel (funciones puras). `discovery.ts` le pasa lo que exporta cada
// `modules/<carpeta>/module.tsx`; aquí se valida cada definición, se ordena y se responde qué ve cada sesión:
//
//   buildRegistry(entradas)                     → { modules (válidos y ordenados), problems (en palabras), home }
//   menuSections(registro, permisos)            → secciones del menú con sus módulos visibles
//   dashboardActions(registro, permisos)        → botones del tablero agrupados por sección
//   dashboardStats(registro, permisos)          → estadísticas plegadas del tablero
//   panelScreens / searchScreens                → buscador de pantallas del menú
//   locate(registro, '/panel/x/y')              → módulo y pantalla de una dirección (migas de pan, acceso)
//
// Un módulo con errores NO se carga (el resto sigue funcionando) y el problema queda en `problems`: la prueba del registro
// falla y, en desarrollo, el panel lo muestra arriba de la pantalla.

import { matchPath } from 'react-router-dom';
import { PERMISSION_LIST } from '@/4-presentation/app/contract';
import { matchesSearch } from '../lib/table';
import { allows, missingFor, type MissingPermissions, type PermissionRule } from './access';
import { isSectionKey, PANEL_SECTIONS, sectionOf, sectionOrder, type PanelSection } from './sections';
import type { ModuleAction, ModuleRoute, ModuleStat, PanelModuleDefinition } from './types';

/** Dirección del panel. */
export const PANEL_BASE = '/panel';
/** El módulo del tablero: su pantalla es `/panel` (y no `/panel/inicio`). */
export const HOME_MODULE_KEY = 'inicio';

const KEY_PATTERN = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/;
const FOLDER_PATTERN = /modules\/([^/]+)\/module\.tsx$/;

/** Lo que descubre `import.meta.glob`: el archivo y lo que exporta por defecto. */
export interface RegistryEntry {
  /** Ruta del archivo (`../modules/<carpeta>/module.tsx`): la carpeta debe ser la clave del módulo. */
  source: string;
  definition: unknown;
}

/** Un módulo ya validado, con su dirección. */
export interface PanelModule extends PanelModuleDefinition {
  /** `/panel` (el tablero) o `/panel/<clave>`. */
  basePath: string;
}

export interface PanelRegistry {
  /** Módulos válidos, en el orden del menú (sección, `order` y título). */
  modules: readonly PanelModule[];
  /** Por qué no se cargó algún módulo (vacío si todo está bien). */
  problems: readonly string[];
  /** El módulo del tablero (`inicio`), si existe. */
  home: PanelModule | null;
}

/**
 * Módulos comerciales activos de la empresa, o null si la sesión no los informa (hoy la sesión web no los trae: el
 * servidor rechaza si falta la licencia y el aviso lo explica). Con null no se filtra por licencia.
 */
export type LicensedModules = readonly string[] | null;

// ---------------------------------------------------------------------------------------------------- validación

const KNOWN_PERMISSIONS = new Set(PERMISSION_LIST.map((permission) => permission.code));

function isComponent(value: unknown): boolean {
  return typeof value === 'function' || (typeof value === 'object' && value !== null && '$$typeof' in value);
}

function isText(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isStringList(value: unknown): value is readonly string[] {
  return Array.isArray(value) && value.every((item) => typeof item === 'string');
}

/** Problemas de una regla de permisos (listas de texto y permisos que existan en el contrato). */
function ruleProblems(rule: unknown, where: string): string[] {
  if (rule === undefined) return [];
  if (typeof rule !== 'object' || rule === null) return [`${where}: «permissions» debe ser un objeto { any?, all? }.`];
  const { any, all } = rule as PermissionRule;
  const problems: string[] = [];
  for (const [name, list] of [
    ['any', any],
    ['all', all],
  ] as const) {
    if (list === undefined) continue;
    if (!isStringList(list)) {
      problems.push(`${where}: «permissions.${name}» debe ser una lista de códigos de permiso.`);
      continue;
    }
    for (const code of list) if (!KNOWN_PERMISSIONS.has(code)) problems.push(`${where}: el permiso «${code}» no existe en el contrato.`);
  }
  return problems;
}

/** ¿Una dirección relativa al módulo? ('' , 'nueva', '?estado=x', ':numero'): sin «/» inicial, sin «..» ni otro sitio. */
function isRelativePath(value: unknown): value is string {
  return typeof value === 'string' && !value.startsWith('/') && !value.includes('..') && !value.includes('://') && !value.includes('\\');
}

function folderOf(source: string): string | null {
  return FOLDER_PATTERN.exec(source.replace(/\\/g, '/'))?.[1] ?? null;
}

/** Problemas de una definición (vacío = válida). */
function definitionProblems(entry: RegistryEntry): string[] {
  const folder = folderOf(entry.source) ?? entry.source;
  const where = `Módulo «${folder}» (modules/${folder}/module.tsx)`;
  const definition = entry.definition as Partial<PanelModuleDefinition> | undefined;
  if (!definition || typeof definition !== 'object') return [`${where}: no exporta por defecto su definición (export default defineModule({ … })).`];

  const problems: string[] = [];
  const { key, section, title, description, icon, order, permissions, licenseModules, routes, actions, stats } = definition;
  if (!isText(key) || !KEY_PATTERN.test(key)) problems.push(`${where}: «key» debe ir en minúsculas y con guiones (por ejemplo «ordenes-compra»).`);
  else if (key !== folder) problems.push(`${where}: la clave «${key}» debe ser igual al nombre de su carpeta («${folder}»).`);
  if (!isSectionKey(section)) problems.push(`${where}: la sección «${String(section)}» no existe (use una de: ${PANEL_SECTIONS.map((item) => item.key).join(', ')}).`);
  if (!isText(title)) problems.push(`${where}: falta «title».`);
  if (!isText(description)) problems.push(`${where}: falta «description».`);
  if (!isComponent(icon)) problems.push(`${where}: «icon» debe ser un ícono de lucide-react (por ejemplo History).`);
  if (typeof order !== 'number' || !Number.isFinite(order)) problems.push(`${where}: «order» debe ser un número.`);
  if (permissions === undefined) problems.push(`${where}: falta «permissions» (use {} si basta con tener sesión).`);
  problems.push(...ruleProblems(permissions, where));
  if (licenseModules !== undefined && !isStringList(licenseModules)) problems.push(`${where}: «licenseModules» debe ser una lista de códigos.`);

  if (!Array.isArray(routes) || routes.length === 0) {
    problems.push(`${where}: «routes» debe tener al menos la pantalla principal ({ path: '', … }).`);
  } else {
    const paths = new Set<string>();
    (routes as readonly Partial<ModuleRoute>[]).forEach((route, index) => {
      const at = `${where}, pantalla ${index + 1}`;
      if (!isRelativePath(route.path)) problems.push(`${at}: «path» es relativa al módulo y va sin «/» al inicio ('' es la principal).`);
      else if (paths.has(route.path)) problems.push(`${at}: la ruta «${route.path}» está repetida.`);
      else paths.add(route.path);
      if (!isComponent(route.element)) problems.push(`${at}: «element» debe ser un componente (lazyScreen(() => import('./Pantalla'), 'Pantalla')).`);
      if (!isText(route.title)) problems.push(`${at}: falta «title».`);
      problems.push(...ruleProblems(route.permissions, at));
    });
    if (!paths.has('')) problems.push(`${where}: falta la pantalla principal ({ path: '', … }).`);
    if (key === HOME_MODULE_KEY && routes.length !== 1) problems.push(`${where}: el tablero tiene una sola pantalla (la de /panel).`);
  }

  if (actions !== undefined) {
    if (!Array.isArray(actions)) problems.push(`${where}: «actions» debe ser una lista.`);
    else {
      const keys = new Set<string>();
      (actions as readonly Partial<ModuleAction>[]).forEach((action, index) => {
        const at = `${where}, botón ${index + 1}`;
        if (!isText(action.key)) problems.push(`${at}: falta «key».`);
        else if (keys.has(action.key)) problems.push(`${at}: la clave «${action.key}» está repetida.`);
        else keys.add(action.key);
        if (!isText(action.label)) problems.push(`${at}: falta «label».`);
        if (!isText(action.description)) problems.push(`${at}: falta «description».`);
        if (!isComponent(action.icon)) problems.push(`${at}: «icon» debe ser un ícono de lucide-react.`);
        if (!isRelativePath(action.to)) problems.push(`${at}: «to» es relativa al módulo ('' , 'nueva' o '?filtro=x'), sin «/» al inicio.`);
        problems.push(...ruleProblems(action.permissions, at));
      });
    }
  }

  if (stats !== undefined) {
    if (!Array.isArray(stats)) problems.push(`${where}: «stats» debe ser una lista.`);
    else {
      const keys = new Set<string>();
      (stats as readonly Partial<ModuleStat>[]).forEach((stat, index) => {
        const at = `${where}, estadística ${index + 1}`;
        if (!isText(stat.key)) problems.push(`${at}: falta «key».`);
        else if (keys.has(stat.key)) problems.push(`${at}: la clave «${stat.key}» está repetida.`);
        else keys.add(stat.key);
        if (!isText(stat.title)) problems.push(`${at}: falta «title».`);
        if (!isComponent(stat.component)) problems.push(`${at}: «component» debe ser un componente (lazyScreen(…)).`);
        problems.push(...ruleProblems(stat.permissions, at));
      });
    }
  }
  return problems;
}

/** Dirección base de un módulo: `/panel` para el tablero y `/panel/<clave>` para el resto. */
export function basePathOf(key: string): string {
  return key === HOME_MODULE_KEY ? PANEL_BASE : `${PANEL_BASE}/${key}`;
}

const collator = new Intl.Collator('es', { sensitivity: 'base' });

function compareModules(a: PanelModuleDefinition, b: PanelModuleDefinition): number {
  return sectionOrder(a.section) - sectionOrder(b.section) || a.order - b.order || collator.compare(a.title, b.title) || a.key.localeCompare(b.key);
}

/** Valida y ordena los módulos descubiertos. Uno con problemas (o con la clave repetida) queda afuera. */
export function buildRegistry(entries: readonly RegistryEntry[]): PanelRegistry {
  const problems: string[] = [];
  const valid: PanelModuleDefinition[] = [];
  const seen = new Set<string>();
  for (const entry of [...entries].sort((a, b) => a.source.localeCompare(b.source))) {
    const found = definitionProblems(entry);
    if (found.length > 0) {
      problems.push(...found);
      continue;
    }
    const definition = entry.definition as PanelModuleDefinition;
    if (seen.has(definition.key)) {
      problems.push(`Módulo «${definition.key}»: hay dos módulos con la misma clave; se cargó solo el primero.`);
      continue;
    }
    seen.add(definition.key);
    valid.push(definition);
  }
  const modules = valid.sort(compareModules).map((definition) => ({ ...definition, basePath: basePathOf(definition.key) }));
  return { modules, problems, home: modules.find((module) => module.key === HOME_MODULE_KEY) ?? null };
}

// ---------------------------------------------------------------------------------------------------- qué ve la sesión

/** ¿La licencia de la empresa incluye los módulos comerciales que exige el módulo? (sin datos de licencia, sí). */
function licensedFor(module: PanelModuleDefinition, licensed: LicensedModules): boolean {
  const required = module.licenseModules ?? [];
  return licensed === null || required.every((code) => licensed.includes(code));
}

/** ¿La sesión ve el módulo en el menú? */
export function canSeeModule(module: PanelModuleDefinition, granted: readonly string[], licensed: LicensedModules = null): boolean {
  return allows(module.permissions, granted) && licensedFor(module, licensed);
}

/** Dirección de algo del módulo: `to` es relativa ('' , 'nueva', '?estado=x'). */
export function modulePath(module: Pick<PanelModule, 'basePath'>, to = ''): string {
  if (to === '') return module.basePath;
  if (to.startsWith('?') || to.startsWith('#')) return `${module.basePath}${to}`;
  return `${module.basePath}/${to}`;
}

/** Módulos que ve la sesión, en el orden del menú. */
export function visibleModules(registry: PanelRegistry, granted: readonly string[], licensed: LicensedModules = null): PanelModule[] {
  return registry.modules.filter((module) => canSeeModule(module, granted, licensed));
}

export interface MenuSection {
  section: PanelSection;
  modules: PanelModule[];
}

/** Secciones del menú con sus módulos visibles (las secciones vacías no aparecen). */
export function menuSections(registry: PanelRegistry, granted: readonly string[], licensed: LicensedModules = null): MenuSection[] {
  const visible = visibleModules(registry, granted, licensed);
  return PANEL_SECTIONS.map((section) => ({ section, modules: visible.filter((module) => module.section === section.key) })).filter(
    (group) => group.modules.length > 0,
  );
}

export interface DashboardAction {
  module: PanelModule;
  action: ModuleAction;
  /** Dirección completa del botón. */
  to: string;
}

export interface DashboardActionGroup {
  section: PanelSection;
  actions: DashboardAction[];
}

/** Botones del tablero que la sesión puede usar, agrupados por sección (en el orden del menú). */
export function dashboardActions(registry: PanelRegistry, granted: readonly string[], licensed: LicensedModules = null): DashboardActionGroup[] {
  const visible = visibleModules(registry, granted, licensed);
  return PANEL_SECTIONS.map((section) => ({
    section,
    actions: visible
      .filter((module) => module.section === section.key)
      .flatMap((module) =>
        (module.actions ?? []).filter((action) => allows(action.permissions, granted)).map((action) => ({ module, action, to: modulePath(module, action.to) })),
      ),
  })).filter((group) => group.actions.length > 0);
}

export interface DashboardStat {
  module: PanelModule;
  stat: ModuleStat;
}

/** Estadísticas que la sesión puede ver (plegadas detrás de «Ver estadísticas»), en el orden del menú. */
export function dashboardStats(registry: PanelRegistry, granted: readonly string[], licensed: LicensedModules = null): DashboardStat[] {
  return visibleModules(registry, granted, licensed).flatMap((module) =>
    (module.stats ?? []).filter((stat) => allows(stat.permissions, granted)).map((stat) => ({ module, stat })),
  );
}

// ---------------------------------------------------------------------------------------------------- pantallas

/** Una pantalla a la que se puede ir desde el buscador del menú. */
export interface PanelScreen {
  /** Identificador estable (`actividad` o `ventas/nueva`). */
  id: string;
  title: string;
  /** Módulo y sección, para mostrar debajo del título. */
  context: string;
  to: string;
  module: PanelModule;
}

/** Pantallas que ve la sesión: cada módulo y sus pantallas con título propio que no piden un dato (sin «:»). */
export function panelScreens(registry: PanelRegistry, granted: readonly string[], licensed: LicensedModules = null): PanelScreen[] {
  return visibleModules(registry, granted, licensed).flatMap((module) => {
    const section = sectionOf(module.section)?.title ?? '';
    const main: PanelScreen = { id: module.key, title: module.title, context: section, to: module.basePath, module };
    const extra = module.routes
      .filter((route) => route.path !== '' && !route.path.includes(':') && allows(route.permissions, granted))
      .map((route) => ({ id: `${module.key}/${route.path}`, title: route.title, context: `${section} › ${module.title}`, to: modulePath(module, route.path), module }));
    return [main, ...extra];
  });
}

/** Busca por título, módulo, sección y descripción (sin acentos; todas las palabras). */
export function searchScreens(screens: readonly PanelScreen[], query: string): PanelScreen[] {
  if (query.trim().length === 0) return [];
  return screens.filter((screen) => matchesSearch(query, [screen.title, screen.context, screen.module.title, screen.module.description]));
}

/** Dónde está una dirección del panel: el módulo, su pantalla y los parámetros. null si no es de ningún módulo. */
export interface PanelLocation {
  module: PanelModule;
  route: ModuleRoute;
  params: Record<string, string | undefined>;
}

export function locate(registry: PanelRegistry, pathname: string): PanelLocation | null {
  for (const module of registry.modules) {
    for (const route of module.routes) {
      const pattern = route.path === '' ? module.basePath : `${module.basePath}/${route.path}`;
      const match = matchPath({ path: pattern, end: true }, pathname);
      if (match) return { module, route, params: match.params };
    }
  }
  return null;
}

/** ¿La sesión puede abrir esa pantalla? Si no, qué le falta (en códigos; `missingText` lo dice en palabras). */
export function screenAccess(
  module: PanelModuleDefinition,
  route: ModuleRoute | null,
  granted: readonly string[],
  licensed: LicensedModules = null,
): { allowed: boolean; missing: MissingPermissions; unlicensed: readonly string[] } {
  const rules = [module.permissions, route?.permissions];
  const missing = missingFor(rules, granted);
  const unlicensed = licensed === null ? [] : (module.licenseModules ?? []).filter((code) => !licensed.includes(code));
  const allowed = rules.every((rule) => allows(rule, granted)) && unlicensed.length === 0;
  return { allowed, missing, unlicensed };
}
