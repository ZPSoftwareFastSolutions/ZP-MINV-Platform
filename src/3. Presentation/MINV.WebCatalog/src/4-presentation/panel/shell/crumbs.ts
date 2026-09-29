// Migas de pan del panel a partir del registro (función pura): Inicio › Sección › Módulo › Pantalla. La sección no es
// un enlace (no tiene dirección propia); la última miga es la pantalla actual.
//
//   /panel                     → Inicio
//   /panel/actividad           → Inicio › Administración › Actividad
//   /panel/ventas/nueva        → Inicio › Ventas › Ventas › Nueva venta
//   /panel/no-existe           → Inicio › Pantalla no encontrada

import { HOME_MODULE_KEY, PANEL_BASE, locate, sectionOf, type PanelRegistry } from '../registry';

export interface PanelCrumb {
  label: string;
  /** Sin `to`: no es un enlace (la pantalla actual o una sección). */
  to?: string;
}

const HOME_LABEL = 'Inicio';

export function crumbsFor(registry: PanelRegistry, pathname: string): PanelCrumb[] {
  const path = pathname.length > 1 ? pathname.replace(/\/+$/, '') : pathname;
  if (path === PANEL_BASE) return [{ label: HOME_LABEL }];
  const home: PanelCrumb = { label: HOME_LABEL, to: PANEL_BASE };
  const found = locate(registry, path);
  if (!found) return [home, { label: 'Pantalla no encontrada' }];
  if (found.module.key === HOME_MODULE_KEY) return [{ label: HOME_LABEL }];
  const crumbs: PanelCrumb[] = [home];
  const section = sectionOf(found.module.section);
  if (section) crumbs.push({ label: section.title });
  if (found.route.path === '') crumbs.push({ label: found.module.title });
  else crumbs.push({ label: found.module.title, to: found.module.basePath }, { label: found.route.title });
  return crumbs;
}
