// Secciones del panel, en el orden del menú y del tablero (diseño §7: las mismas que el escritorio). Cada módulo declara
// en qué sección va con su clave (`section: 'administracion'`). Una sección sin módulos visibles para la sesión no se
// muestra.

import { BarChart3, Building2, Cpu, LayoutDashboard, Package, Receipt, Settings, ShoppingCart, Truck, type LucideIcon } from 'lucide-react';

export interface PanelSection {
  key: string;
  title: string;
  icon: LucideIcon;
}

export const PANEL_SECTIONS = [
  { key: 'general', title: 'General', icon: LayoutDashboard },
  { key: 'ventas', title: 'Ventas', icon: ShoppingCart },
  { key: 'tecnologia', title: 'Tecnología', icon: Cpu },
  { key: 'inventario', title: 'Inventario', icon: Package },
  { key: 'compras', title: 'Compras', icon: Truck },
  { key: 'sucursales', title: 'Sucursales', icon: Building2 },
  { key: 'facturacion', title: 'Facturación', icon: Receipt },
  { key: 'analisis', title: 'Análisis', icon: BarChart3 },
  { key: 'administracion', title: 'Administración', icon: Settings },
] as const satisfies readonly PanelSection[];

/** Clave de una sección: 'general' | 'ventas' | 'tecnologia' | 'inventario' | 'compras' | 'sucursales' | 'facturacion' | 'analisis' | 'administracion'. */
export type PanelSectionKey = (typeof PANEL_SECTIONS)[number]['key'];

export function isSectionKey(value: unknown): value is PanelSectionKey {
  return typeof value === 'string' && PANEL_SECTIONS.some((section) => section.key === value);
}

/** La sección de una clave (o undefined si no existe). */
export function sectionOf(key: string): PanelSection | undefined {
  return PANEL_SECTIONS.find((section) => section.key === key);
}

/** Posición de la sección en el menú (las desconocidas, al final). */
export function sectionOrder(key: string): number {
  const index = PANEL_SECTIONS.findIndex((section) => section.key === key);
  return index < 0 ? PANEL_SECTIONS.length : index;
}
