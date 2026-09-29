// Pantallas propias del esqueleto: una ruta de un módulo sin permiso («No tiene acceso a esta pantalla», con el permiso
// que falta en palabras) y una dirección del panel que no corresponde a ninguna pantalla.

import { Compass, House, ShieldAlert } from 'lucide-react';
import { Button } from '@/4-presentation/components/ui/Button';
import { useDocumentMeta } from '@/4-presentation/hooks/useDocumentMeta';
import { PANEL_BASE, missingText, sectionOf, type MissingPermissions, type ModuleRoute, type PanelModule } from '../registry';

export interface NoAccessProps {
  module: PanelModule;
  route: ModuleRoute;
  missing: MissingPermissions;
  /** Módulos comerciales que la empresa no tiene (si la sesión los informa). */
  unlicensed?: readonly string[];
}

export function NoAccess({ module, route, missing, unlicensed = [] }: NoAccessProps) {
  useDocumentMeta({ title: 'Sin acceso · Panel' });
  const section = sectionOf(module.section)?.title;
  const where = [section, module.title, route.path === '' ? null : route.title].filter(Boolean).join(' › ');
  return (
    <div
      role="alert"
      className="mx-auto flex max-w-xl flex-col items-center gap-3 rounded-card border border-warning/40 bg-warning-soft/40 px-6 py-12 text-center"
      data-testid="sin-acceso"
    >
      <span aria-hidden="true" className="flex size-14 items-center justify-center rounded-2xl bg-warning-soft text-warning-text">
        <ShieldAlert className="size-7" />
      </span>
      <h1 className="text-2xl">No tiene acceso a esta pantalla</h1>
      <p className="text-sm font-medium text-text">{where}</p>
      <p className="max-w-md text-sm text-text-muted">
        {unlicensed.length > 0 ? `La empresa no tiene habilitado el módulo ${unlicensed.join(', ')}. Consulte con el administrador.` : missingText(missing)}
      </p>
      <Button to={PANEL_BASE} variant="outline" leftIcon={<House />}>
        Volver al inicio del panel
      </Button>
    </div>
  );
}

export function PanelNotFound() {
  useDocumentMeta({ title: 'Pantalla no encontrada · Panel' });
  return (
    <div className="mx-auto flex max-w-xl flex-col items-center gap-3 rounded-card border border-border bg-surface px-6 py-12 text-center" data-testid="pantalla-no-encontrada">
      <span aria-hidden="true" className="flex size-14 items-center justify-center rounded-2xl bg-surface-2 text-accent">
        <Compass className="size-7" />
      </span>
      <h1 className="text-2xl">No encontramos esta pantalla</h1>
      <p className="max-w-md text-sm text-text-muted">
        La dirección no corresponde a ninguna pantalla del panel, o esa pantalla todavía no está disponible. Use el menú o el buscador de
        pantallas.
      </p>
      <Button to={PANEL_BASE} variant="outline" leftIcon={<House />}>
        Ir al inicio del panel
      </Button>
    </div>
  );
}
