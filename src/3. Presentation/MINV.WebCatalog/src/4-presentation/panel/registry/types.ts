// Definición de un módulo del panel: lo que exporta por defecto `modules/<clave>/module.tsx`. El registro la descubre
// sola (no hay que anotarla en ninguna lista) y el esqueleto arma con ella el menú, las rutas, las migas, el buscador y
// el tablero de «Inicio», mostrando SOLO lo que los permisos de la sesión permiten.
//
//   export default defineModule({
//     key: 'actividad', section: 'administracion', title: 'Actividad', description: 'Quién hizo qué y cuándo.',
//     icon: History, order: 40, permissions: { all: ['iam.audit.view'] },
//     routes: [{ path: '', title: 'Actividad', element: lazyScreen(() => import('./ActivityPage'), 'ActivityPage') }],
//     actions: [{ key: 'ver', label: 'Ver la actividad', description: '…', icon: History, to: '' }],
//     stats: [{ key: 'hoy', title: 'Actividad de hoy', component: lazyScreen(() => import('./ActivityTodayStat'), 'ActivityTodayStat') }],
//   });
//
// `module.tsx` debe ser LIVIANO: solo esta definición, íconos y cargas diferidas. Las pantallas y las estadísticas se
// descargan recién cuando se abren (cada una en su propio fragmento).

import { lazy, type ComponentType, type LazyExoticComponent } from 'react';
import type { LucideIcon } from 'lucide-react';
import type { PermissionRule } from './access';
import type { PanelSectionKey } from './sections';

/** Una pantalla o una estadística: un componente sin props, casi siempre con carga diferida (`lazyScreen`). */
export type PanelComponent = ComponentType | LazyExoticComponent<ComponentType>;

export interface ModuleRoute {
  /**
   * Ruta DENTRO del módulo, sin «/» al inicio: '' es su pantalla principal (`/panel/<clave>`), 'nueva' es
   * `/panel/<clave>/nueva` y ':numero' es `/panel/<clave>/:numero` (el parámetro se lee con `useParams()`).
   */
  path: string;
  /** La pantalla: `lazyScreen(() => import('./MiPantalla'), 'MiPantalla')`. */
  element: PanelComponent;
  /** Título corto de la pantalla (migas de pan y buscador del menú). */
  title: string;
  /** Permisos EXTRA de esta pantalla, además de los del módulo. */
  permissions?: PermissionRule;
}

export interface ModuleAction {
  /** Identificador único dentro del módulo. */
  key: string;
  /** Texto del botón grande del tablero: un verbo y qué («Abrir caja», «Ver la actividad»). */
  label: string;
  /** Una línea: para qué sirve. */
  description: string;
  icon: LucideIcon;
  /** A dónde lleva, RELATIVO al módulo: '' (su pantalla principal), 'nueva', '?resultado=Rejected'. */
  to: string;
  /** Permisos EXTRA del botón, además de los del módulo. */
  permissions?: PermissionRule;
}

export interface ModuleStat {
  /** Identificador único dentro del módulo. */
  key: string;
  /** Título de la tarjeta dentro de «Ver estadísticas». */
  title: string;
  /** Permisos EXTRA, además de los del módulo. */
  permissions?: PermissionRule;
  /** El contenido (hace su propia consulta): se descarga y consulta recién al abrir «Ver estadísticas». */
  component: PanelComponent;
}

export interface PanelModuleDefinition {
  /** Clave del módulo = nombre de su carpeta = su dirección (`/panel/<clave>`). Minúsculas y guiones: 'ordenes-compra'. */
  key: string;
  /** Sección del menú y del tablero (ver `PANEL_SECTIONS`). */
  section: PanelSectionKey;
  /** Nombre en el menú («Actividad»). */
  title: string;
  /** Una línea: para qué sirve (buscador del menú). */
  description: string;
  icon: LucideIcon;
  /** Orden dentro de su sección (de menor a mayor; empates por título). Use decenas: 10, 20, 30… */
  order: number;
  /** Con qué permisos se ve el módulo (`{}` = cualquier sesión del personal). */
  permissions: PermissionRule;
  /** Módulos comerciales (licencia) que exige el servidor (`[RequiresModule]`), por ejemplo 'MULTI_BRANCH'. */
  licenseModules?: readonly string[];
  /** Sus pantallas (al menos la principal, con `path: ''`). */
  routes: readonly ModuleRoute[];
  /** Botones grandes que ofrece al tablero de «Inicio». */
  actions?: readonly ModuleAction[];
  /** Estadísticas que ofrece al tablero, plegadas detrás de «Ver estadísticas». */
  stats?: readonly ModuleStat[];
}

/** Declara un módulo (solo tipa el objeto: el registro lo valida al cargarlo). */
export function defineModule(definition: PanelModuleDefinition): PanelModuleDefinition {
  return definition;
}

/**
 * Carga diferida de un componente con exportación CON NOMBRE (la convención del proyecto):
 * `lazyScreen(() => import('./ActivityPage'), 'ActivityPage')`. El archivo se descarga recién al abrir la pantalla.
 */
export function lazyScreen<M extends Record<K, ComponentType>, K extends string>(load: () => Promise<M>, name: K): LazyExoticComponent<ComponentType> {
  return lazy(() => load().then((loaded) => ({ default: loaded[name] })));
}
