import { createContext } from 'react';

/** Lo que cada TabPanel necesita saber de su grupo de pestañas. */
export interface TabsContextValue {
  /** Prefijo único de los ids del grupo. */
  prefix: string;
  /** Pestaña elegida. */
  value: string;
}

export const TabsContext = createContext<TabsContextValue | null>(null);

/** Id de una pestaña y de su panel (sin caracteres raros). */
export function tabIds(prefix: string, id: string): { tab: string; panel: string } {
  const safe = id.replace(/[^a-zA-Z0-9_-]/g, '_');
  return { tab: `${prefix}-pestana-${safe}`, panel: `${prefix}-panel-${safe}` };
}
