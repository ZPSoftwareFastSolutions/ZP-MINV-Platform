// El panel completo sobre un registro de módulos: la estructura (menú, barra superior) y una ruta por cada pantalla de
// cada módulo, relativas a `/panel` (el enrutador monta el panel en `/panel/*`). Se registran TODAS las pantallas, no
// solo las visibles: abrir por dirección una pantalla sin permiso muestra «No tiene acceso a esta pantalla» (y no «no
// encontrada»). `PanelRoot` le pasa el registro real; las pruebas, uno con módulos de muestra.

import { Route, Routes } from 'react-router-dom';
import { HOME_MODULE_KEY, PanelRegistryContext, type PanelRegistry } from '../registry';
import { ModuleScreen } from './ModuleScreen';
import { PanelLayout } from './PanelLayout';
import { PanelNotFound } from './PanelStates';

export interface PanelAppProps {
  registry: PanelRegistry;
}

export function PanelApp({ registry }: PanelAppProps) {
  const home = registry.home;
  return (
    <PanelRegistryContext.Provider value={registry}>
      <Routes>
        <Route element={<PanelLayout />}>
          {home && <Route index element={<ModuleScreen key={HOME_MODULE_KEY} module={home} route={home.routes[0]} />} />}
          {registry.modules
            .filter((module) => module.key !== HOME_MODULE_KEY)
            .map((module) => (
              <Route key={module.key} path={module.key}>
                {module.routes.map((route) =>
                  route.path === '' ? (
                    <Route key="(principal)" index element={<ModuleScreen key={`${module.key}/`} module={module} route={route} />} />
                  ) : (
                    <Route key={route.path} path={route.path} element={<ModuleScreen key={`${module.key}/${route.path}`} module={module} route={route} />} />
                  ),
                )}
              </Route>
            ))}
          <Route path="*" element={<PanelNotFound />} />
        </Route>
      </Routes>
    </PanelRegistryContext.Provider>
  );
}
