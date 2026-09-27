import { useContext } from 'react';
import { BuilderContext, type BuilderApi } from '@/4-presentation/state/BuilderContext';

/**
 * Armado en memoria: `const { lines, count, total, progress, missing, add, remove, setQuantity, clear, loadPreset,
 * isInBuild, isDrawerOpen, openDrawer, closeDrawer } = useBuilder()`.
 */
export function useBuilder(): BuilderApi {
  const api = useContext(BuilderContext);
  if (!api) throw new Error('useBuilder() debe usarse dentro de <BuilderProvider>.');
  return api;
}
