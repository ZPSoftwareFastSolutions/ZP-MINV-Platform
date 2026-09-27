// Estado del armado «Armá tu PC» en memoria (Context + useReducer con el reductor puro del dominio).
// No persiste en ningún almacenamiento: al recargar la página el armado vuelve a estar vacío (alcance de la V5).

import { useCallback, useMemo, useReducer, useState, type ReactNode } from 'react';
import { EMPTY_BUILD, buildReducer, summarizeBuild } from '@/1-domain/builder/build';
import { slotByKey, slotForProduct } from '@/1-domain/builder/slots';
import { useServices } from '@/4-presentation/hooks/useServices';
import { useToast } from '@/4-presentation/hooks/useToast';
import { BuilderContext, type BuilderApi } from './BuilderContext';

export function BuilderProvider({ children }: { children: ReactNode }) {
  const { catalog } = useServices();
  const toast = useToast();
  const [state, dispatch] = useReducer(buildReducer, EMPTY_BUILD);
  const [isDrawerOpen, setDrawerOpen] = useState(false);

  const openDrawer = useCallback(() => setDrawerOpen(true), []);
  const closeDrawer = useCallback(() => setDrawerOpen(false), []);

  const add = useCallback<BuilderApi['add']>(
    (product, slotKey, quantity) => {
      const slot = slotKey ? slotByKey(slotKey) : slotForProduct(product);
      if (!slot) {
        toast.notify({
          tone: 'info',
          title: 'Este producto no forma parte de un armado de PC',
          description: 'Podés verlo en el catálogo, pero no ocupa ninguna ranura del armador.',
        });
        return false;
      }
      dispatch({ type: 'add', product, slot: slot.key, quantity });
      toast.notify({
        tone: 'success',
        title: 'Agregado al armado',
        description: `${product.shortName} · ${slot.label}`,
        action: { label: 'Ver armado', onClick: openDrawer },
      });
      return true;
    },
    [toast, openDrawer],
  );

  const remove = useCallback((sku: string) => dispatch({ type: 'remove', sku }), []);
  const setQuantity = useCallback((sku: string, quantity: number) => dispatch({ type: 'setQuantity', sku, quantity }), []);
  const clear = useCallback(() => dispatch({ type: 'clear' }), []);

  const loadPreset = useCallback(
    (id: string) => {
      const detail = catalog.getPreset(id);
      if (!detail) return false;
      dispatch({ type: 'loadPreset', lines: detail.lines });
      toast.notify({
        tone: 'success',
        title: 'Armado cargado',
        description: detail.preset.name,
        action: { label: 'Ver armado', onClick: openDrawer },
      });
      return true;
    },
    [catalog, toast, openDrawer],
  );

  const summary = useMemo(() => summarizeBuild(state.lines), [state.lines]);

  const value = useMemo<BuilderApi>(
    () => ({
      lines: summary.lines,
      count: summary.count,
      total: summary.total,
      savings: summary.savings,
      progress: summary.progress,
      missing: summary.missing,
      summary,
      add,
      remove,
      setQuantity,
      clear,
      loadPreset,
      isInBuild: (sku) => summary.lines.some((line) => line.product.sku === sku),
      isDrawerOpen,
      openDrawer,
      closeDrawer,
    }),
    [summary, add, remove, setQuantity, clear, loadPreset, isDrawerOpen, openDrawer, closeDrawer],
  );

  return <BuilderContext.Provider value={value}>{children}</BuilderContext.Provider>;
}
