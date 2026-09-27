// Estado del armado «Armá tu PC» en memoria (Context + useReducer con el reductor puro del dominio).
// No persiste en ningún almacenamiento: al recargar la página el armado vuelve a estar vacío (alcance de la V5).
// El estado, las acciones y el cajón viajan en contextos separados (BuilderContext.ts): abrir «Mi armado» no vuelve a
// dibujar las tarjetas de la grilla, que solo leen las acciones y el estado.

import { useCallback, useMemo, useReducer, useState, type ReactNode } from 'react';
import { EMPTY_BUILD, buildReducer, isInBuild, summarizeBuild } from '@/1-domain/builder/build';
import { slotByKey, slotForProduct } from '@/1-domain/builder/slots';
import { useServices } from '@/4-presentation/hooks/useServices';
import { useToast } from '@/4-presentation/hooks/useToast';
import { BuildActionsContext, BuildDrawerContext, BuildStateContext, type BuildActionsApi, type BuildDrawerApi, type BuildStateApi } from './BuilderContext';

/** Grupo de los avisos de «agregado»: varias piezas seguidas muestran un solo aviso (el último), no una pila. */
const ADD_TOAST_GROUP = 'armado:agregar';

export function BuilderProvider({ children }: { children: ReactNode }) {
  const { catalog } = useServices();
  const toast = useToast();
  const [state, dispatch] = useReducer(buildReducer, EMPTY_BUILD);
  const [isDrawerOpen, setDrawerOpen] = useState(false);

  const openDrawer = useCallback(() => setDrawerOpen(true), []);
  const closeDrawer = useCallback(() => setDrawerOpen(false), []);

  const add = useCallback<BuildActionsApi['add']>(
    (product, slotKey, quantity, options) => {
      const slot = slotKey ? slotByKey(slotKey) : slotForProduct(product);
      if (!slot) {
        if (!options?.silent) {
          toast.notify({
            tone: 'info',
            title: 'Este producto no forma parte de un armado de PC',
            description: 'Podés verlo en el catálogo, pero no ocupa ninguna ranura del armador.',
          });
        }
        return false;
      }
      dispatch({ type: 'add', product, slot: slot.key, quantity });
      if (!options?.silent) {
        toast.notify({
          tone: 'success',
          group: ADD_TOAST_GROUP,
          title: 'Agregado al armado',
          description: `${product.shortName} · ${slot.label}`,
          action: { label: 'Ver armado', onClick: openDrawer },
        });
      }
      return true;
    },
    [toast, openDrawer],
  );

  const remove = useCallback((sku: string) => dispatch({ type: 'remove', sku }), []);
  const setQuantity = useCallback((sku: string, quantity: number) => dispatch({ type: 'setQuantity', sku, quantity }), []);
  const clear = useCallback(() => dispatch({ type: 'clear' }), []);

  const loadPreset = useCallback<BuildActionsApi['loadPreset']>(
    (id, options) => {
      const detail = catalog.getPreset(id);
      if (!detail) return false;
      dispatch({ type: 'loadPreset', lines: detail.lines });
      if (!options?.silent) {
        toast.notify({
          tone: 'success',
          group: ADD_TOAST_GROUP,
          title: 'Armado cargado',
          description: detail.preset.name,
          action: { label: 'Ver armado', onClick: openDrawer },
        });
      }
      return true;
    },
    [catalog, toast, openDrawer],
  );

  const summary = useMemo(() => summarizeBuild(state.lines), [state.lines]);

  const stateApi = useMemo<BuildStateApi>(
    () => ({
      lines: summary.lines,
      count: summary.count,
      total: summary.total,
      savings: summary.savings,
      progress: summary.progress,
      missing: summary.missing,
      summary,
      isInBuild: (sku) => isInBuild(summary.lines, sku),
    }),
    [summary],
  );

  const actionsApi = useMemo<BuildActionsApi>(() => ({ add, remove, setQuantity, clear, loadPreset }), [add, remove, setQuantity, clear, loadPreset]);

  const drawerApi = useMemo<BuildDrawerApi>(() => ({ isDrawerOpen, openDrawer, closeDrawer }), [isDrawerOpen, openDrawer, closeDrawer]);

  return (
    <BuildActionsContext.Provider value={actionsApi}>
      <BuildStateContext.Provider value={stateApi}>
        <BuildDrawerContext.Provider value={drawerApi}>{children}</BuildDrawerContext.Provider>
      </BuildStateContext.Provider>
    </BuildActionsContext.Provider>
  );
}
