// Descubrimiento de los módulos: TODA carpeta `panel/modules/<clave>/` con un `module.tsx` que exporta por defecto su
// definición entra sola al panel (no hay listas que editar). Solo lo importa `PanelRoot.tsx` (y las pruebas): los
// módulos importan de `registry/index.ts`, que NO incluye este archivo (así no hay importaciones circulares).
//
// La carga es inmediata (`eager`) porque `module.tsx` es liviano: las pantallas y las estadísticas son diferidas.

import { buildRegistry, type PanelRegistry } from './registry';

const found = import.meta.glob('../modules/*/module.tsx', { eager: true, import: 'default' });

/** El registro con todos los módulos del proyecto. */
export const PANEL_REGISTRY: PanelRegistry = buildRegistry(Object.entries(found).map(([source, definition]) => ({ source, definition })));
