import { useContext } from 'react';
import {
  BuildActionsContext,
  BuildDrawerContext,
  BuildStateContext,
  type BuildActionsApi,
  type BuildDrawerApi,
  type BuildStateApi,
  type BuilderApi,
} from '@/4-presentation/state/BuilderContext';

function required<T>(value: T | null, hook: string): T {
  if (!value) throw new Error(`${hook} debe usarse dentro de <BuilderProvider>.`);
  return value;
}

/** Estado del armado (líneas, total, progreso, faltantes). Se vuelve a dibujar con cada pieza, no con el cajón. */
export function useBuildState(): BuildStateApi {
  return required(useContext(BuildStateContext), 'useBuildState()');
}

/** Acciones del armado (add, remove, setQuantity, clear, loadPreset): referencias estables. */
export function useBuildActions(): BuildActionsApi {
  return required(useContext(BuildActionsContext), 'useBuildActions()');
}

/** Cajón «Mi armado» (isDrawerOpen, openDrawer, closeDrawer). */
export function useBuildDrawer(): BuildDrawerApi {
  return required(useContext(BuildDrawerContext), 'useBuildDrawer()');
}

/**
 * Armado en memoria completo: `const { lines, count, total, progress, missing, add, remove, setQuantity, clear,
 * loadPreset, isInBuild, isDrawerOpen, openDrawer, closeDrawer } = useBuilder()`. Las tarjetas de la grilla usan los
 * hooks parciales para no volver a dibujarse al abrir el cajón.
 */
export function useBuilder(): BuilderApi {
  return { ...useBuildState(), ...useBuildActions(), ...useBuildDrawer() };
}
