// Estado del armado «Armá tu PC» en memoria (Context + useReducer con el reductor puro del dominio).
// No persiste en ningún almacenamiento: al recargar la página el armado vuelve a estar vacío; lo que sí persiste es la
// RESERVA en la tienda (V6), que se hace desde el armador y vacía el armado. Cada vez que llega una instantánea nueva
// del catálogo, las piezas del armado se sincronizan con su versión fresca (precio y disponibilidad).
// El estado, las acciones y el cajón viajan en contextos separados (BuilderContext.ts): abrir «Mi armado» no vuelve a
// dibujar las tarjetas de la grilla, que solo leen las acciones y el estado.
//
// V7 · W3b: vive arriba del enrutador aunque el catálogo todavía no haya llegado (o haya fallado): así el armado se
// conserva al navegar y las pantallas que no son de la tienda (ingresar, panel) se dibujan igual. Sin catálogo no
// sincroniza ni carga armados sugeridos; en cuanto llega la instantánea, sincroniza como siempre.

import { useCallback, useEffect, useMemo, useReducer, useState, type ReactNode } from 'react';
import { EMPTY_BUILD, buildReducer, isInBuild, summarizeBuild } from '@/1-domain/builder/build';
import { slotByKey, slotForProduct } from '@/1-domain/builder/slots';
import { useOptionalServices } from '@/4-presentation/hooks/useServices';
import { useToast } from '@/4-presentation/hooks/useToast';
import { BuildActionsContext, BuildDrawerContext, BuildStateContext, type BuildActionsApi, type BuildDrawerApi, type BuildStateApi } from './BuilderContext';

/** Grupo de los avisos de «agregado»: varias piezas seguidas muestran un solo aviso (el último), no una pila. */
const ADD_TOAST_GROUP = 'armado:agregar';

export function BuilderProvider({ children }: { children: ReactNode }) {
  const catalog = useOptionalServices()?.catalog ?? null;
  const toast = useToast();
  const [state, dispatch] = useReducer(buildReducer, EMPTY_BUILD);
  const [isDrawerOpen, setDrawerOpen] = useState(false);

  const openDrawer = useCallback(() => setDrawerOpen(true), []);
  const closeDrawer = useCallback(() => setDrawerOpen(false), []);

  // Instantánea nueva (refresco cada 60 s o al volver a la pestaña): las piezas elegidas toman su precio y stock frescos.
  useEffect(() => {
    if (catalog) dispatch({ type: 'sync', lookup: (sku) => catalog.getProductBySku(sku) });
  }, [catalog]);

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
      const detail = catalog?.getPreset(id);
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
