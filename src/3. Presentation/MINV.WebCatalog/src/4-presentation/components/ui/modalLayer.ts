// Pila ÚNICA de capas modales (cajones, hojas y diálogos): el bloqueo del fondo (`inert` en #root y overflow del body)
// se toma con la primera capa y se devuelve solo cuando la pila queda vacía, restaurando el overflow que había antes;
// Escape actúa únicamente sobre la capa superior. Cualquier superficie modal del sitio usa estos hooks, así dos capas
// abiertas a la vez (cajón «Mi armado» sobre el modal final) no se pisan los contadores ni el foco.

import { useCallback, useEffect, useId, useRef, useState, useSyncExternalStore, type KeyboardEvent, type RefObject } from 'react';

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

let stack: string[] = [];
let savedOverflow = '';
const listeners = new Set<() => void>();

function emit() {
  for (const listener of listeners) listener();
}

function push(id: string) {
  if (stack.includes(id)) return;
  if (stack.length === 0) {
    savedOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    document.getElementById('root')?.setAttribute('inert', '');
  }
  stack = [...stack, id];
  emit();
}

function pop(id: string) {
  if (!stack.includes(id)) return;
  stack = stack.filter((item) => item !== id);
  if (stack.length === 0) {
    document.body.style.overflow = savedOverflow;
    document.getElementById('root')?.removeAttribute('inert');
  }
  emit();
}

function isTop(id: string): boolean {
  return stack[stack.length - 1] === id;
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function getCount() {
  return stack.length;
}

/** Cuántas capas modales hay abiertas (0 = ninguna). Sirve para reubicar los avisos y no taparlas. */
export function useModalLayerCount(): number {
  return useSyncExternalStore(subscribe, getCount, () => 0);
}

/** Estado de una capa que se monta al abrir y sigue montada `closeMs` mientras dura la animación de salida. */
export function useOpenTransition(open: boolean, closeMs: number): { mounted: boolean; visible: boolean } {
  const [prevOpen, setPrevOpen] = useState(open);
  const [closing, setClosing] = useState(false);
  const [visible, setVisible] = useState(false);
  if (prevOpen !== open) {
    setPrevOpen(open);
    if (open) {
      setClosing(false);
    } else {
      setVisible(false);
      setClosing(true);
    }
  }
  const mounted = open || closing;

  useEffect(() => {
    if (!open) return;
    const frame = requestAnimationFrame(() => setVisible(true));
    return () => cancelAnimationFrame(frame);
  }, [open]);

  useEffect(() => {
    if (!closing) return;
    const timer = setTimeout(() => setClosing(false), closeMs);
    return () => clearTimeout(timer);
  }, [closing, closeMs]);

  return { mounted, visible };
}

export interface ModalDialogOptions {
  /** La capa está montada (abierta o cerrándose): toma el bloqueo del fondo. */
  mounted: boolean;
  /** La capa está abierta: recibe Escape y guarda el elemento al que devolver el foco. */
  open: boolean;
  onClose: () => void;
  panelRef: RefObject<HTMLElement | null>;
  /** Adónde devolver el foco si el disparador ya no existe al cerrar. */
  fallbackFocus?: () => HTMLElement | null | undefined;
}

/**
 * Mecánica común de toda superficie modal: registro en la pila (inert + overflow), foco inicial en `[data-autofocus]`
 * o el primer control, devolución del foco al cerrar (al disparador o al de reserva), Escape solo en la capa superior
 * y el manejador `onKeyDown` que atrapa el Tab dentro del panel.
 */
export function useModalDialog({ mounted, open, onClose, panelRef, fallbackFocus }: ModalDialogOptions): {
  layerId: string;
  trapFocus: (event: KeyboardEvent<HTMLElement>) => void;
} {
  const layerId = useId();
  const restoreFocusRef = useRef<HTMLElement | null>(null);
  const fallbackFocusRef = useRef(fallbackFocus);
  useEffect(() => {
    fallbackFocusRef.current = fallbackFocus;
  }, [fallbackFocus]);

  useEffect(() => {
    if (!open) return;
    restoreFocusRef.current = document.activeElement as HTMLElement | null;
  }, [open]);

  useEffect(() => {
    if (!mounted) return;
    push(layerId);
    return () => pop(layerId);
  }, [mounted, layerId]);

  useEffect(() => {
    if (!mounted) return;
    const panel = panelRef.current;
    const first = panel?.querySelector<HTMLElement>('[data-autofocus]') ?? panel?.querySelector<HTMLElement>(FOCUSABLE);
    const timer = setTimeout(() => first?.focus(), 30);
    return () => {
      clearTimeout(timer);
      const previous = restoreFocusRef.current;
      const usable = previous && previous !== document.body && previous.isConnected && !previous.closest('[inert]');
      const target = usable ? previous : fallbackFocusRef.current?.();
      target?.focus?.();
    };
  }, [mounted, panelRef]);

  useEffect(() => {
    if (!open) return;
    const onKeyDown = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape' && isTop(layerId)) {
        event.stopPropagation();
        onClose();
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [open, onClose, layerId]);

  const trapFocus = useCallback(
    (event: KeyboardEvent<HTMLElement>) => {
      if (event.key !== 'Tab' || !panelRef.current) return;
      const focusable = Array.from(panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE)).filter((element) => element.offsetParent !== null);
      if (focusable.length === 0) return;
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    },
    [panelRef],
  );

  return { layerId, trapFocus };
}

/** Solo para pruebas: cuántas capas hay en la pila y si el fondo está bloqueado. */
export function inspectModalLayers(): { count: number; inert: boolean } {
  return { count: stack.length, inert: document.getElementById('root')?.hasAttribute('inert') ?? false };
}
