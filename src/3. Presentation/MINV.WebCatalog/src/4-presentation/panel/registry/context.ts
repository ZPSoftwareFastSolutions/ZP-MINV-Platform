// El registro que usa el panel abierto. Lo entrega el esqueleto (`PanelApp`); las pruebas pueden entregar uno propio con
// módulos de muestra. El tablero de «Inicio» lo lee para ofrecer los botones y las estadísticas de los demás módulos
// (sin importarlos: regla P-09).
//
//   const registry = usePanelRegistry();
//   const grupos = dashboardActions(registry, usePermissions().permissions);

import { createContext, useContext } from 'react';
import type { PanelRegistry } from './registry';

export const PanelRegistryContext = createContext<PanelRegistry | null>(null);

export function usePanelRegistry(): PanelRegistry {
  const registry = useContext(PanelRegistryContext);
  if (!registry) throw new Error('usePanelRegistry() debe usarse dentro del panel (PanelApp).');
  return registry;
}
