// Cierre con identidad estable para las capas modales del panel (SidePanel y Dialog).
//
// El cajón de la tienda (components/ui/Drawer) vuelve a ejecutar su «cerrar al navegar» cada vez que cambia `onClose`, y
// la pila de capas vuelve a suscribir Escape. Si la pantalla pasa una función nueva en cada dibujo (lo normal:
// `onClose={() => setAbierta(null)}`) y ese cierre crea un estado nuevo, se arma un ciclo sin fin. Con este cierre la
// identidad no cambia, se usa siempre la última función y solo se avisa si la capa está abierta (y se puede cerrar).

import { useCallback, useEffect, useRef } from 'react';

export function useStableClose(open: boolean, onClose: () => void, enabled = true): () => void {
  const latest = useRef({ open, onClose, enabled });
  useEffect(() => {
    latest.current = { open, onClose, enabled };
  });
  return useCallback(() => {
    if (latest.current.open && latest.current.enabled) latest.current.onClose();
  }, []);
}
