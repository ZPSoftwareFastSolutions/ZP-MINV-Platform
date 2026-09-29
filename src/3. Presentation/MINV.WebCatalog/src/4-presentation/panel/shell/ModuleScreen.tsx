// Una pantalla de un módulo: si la sesión no tiene los permisos del módulo (y de la pantalla), muestra «No tiene acceso
// a esta pantalla» con el permiso que falta en palabras; si los tiene, descarga la pantalla (carga diferida, con su
// esqueleto mientras llega) dentro de su propio límite de errores.

import { Suspense } from 'react';
import { LoadingState } from '@/4-presentation/components/feedback/AsyncState';
import { usePermissions } from '../hooks';
import { screenAccess, type ModuleRoute, type PanelModule } from '../registry';
import { NoAccess } from './PanelStates';
import { ScreenBoundary } from './ScreenBoundary';

export interface ModuleScreenProps {
  module: PanelModule;
  route: ModuleRoute;
}

export function ModuleScreen({ module, route }: ModuleScreenProps) {
  const { permissions } = usePermissions();
  const access = screenAccess(module, route, permissions);
  if (!access.allowed) return <NoAccess module={module} route={route} missing={access.missing} unlicensed={access.unlicensed} />;
  const Screen = route.element;
  return (
    <ScreenBoundary>
      <Suspense fallback={<LoadingState label={`Cargando ${route.title.toLowerCase()}…`} rows={3} />}>
        <Screen />
      </Suspense>
    </ScreenBoundary>
  );
}
