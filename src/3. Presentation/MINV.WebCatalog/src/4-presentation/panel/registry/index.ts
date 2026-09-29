// REGISTRO DE MÓDULOS DEL PANEL (paquete W3b). Un módulo importa de aquí lo necesario para declararse:
//
//   import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';
//
// y el tablero («Inicio») lo que necesita para mostrar lo que ofrecen los demás módulos. La guía completa para escribir
// un módulo está en `panel/README.md`. El descubrimiento de los módulos (`discovery.ts`) NO se exporta aquí.

export { defineModule, lazyScreen, type ModuleAction, type ModuleRoute, type ModuleStat, type PanelComponent, type PanelModuleDefinition } from './types';
export { PANEL_SECTIONS, isSectionKey, sectionOf, sectionOrder, type PanelSection, type PanelSectionKey } from './sections';
export { allows, hasMissing, missingFor, missingText, type MissingPermissions, type PermissionRule } from './access';
export {
  HOME_MODULE_KEY,
  PANEL_BASE,
  basePathOf,
  buildRegistry,
  canSeeModule,
  dashboardActions,
  dashboardStats,
  locate,
  menuSections,
  modulePath,
  panelScreens,
  screenAccess,
  searchScreens,
  visibleModules,
  type DashboardAction,
  type DashboardActionGroup,
  type DashboardStat,
  type LicensedModules,
  type MenuSection,
  type PanelLocation,
  type PanelModule,
  type PanelRegistry,
  type PanelScreen,
  type RegistryEntry,
} from './registry';
export { PanelRegistryContext, usePanelRegistry } from './context';
