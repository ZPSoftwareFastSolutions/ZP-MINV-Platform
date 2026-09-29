// Avisos breves («Agregado al armado»). Región aria-live única (cada aviso no repite role="status": evita el doble
// anuncio), cierre automático que se pausa con el mouse Y con el foco de teclado, acción opcional y agrupación.
// Se montan en un portal sobre <body> (fuera de #root) para seguir siendo usables cuando un cajón deja #root inerte.
// Cuando hay un cajón o diálogo abierto, la región pasa arriba y al centro para no tapar sus botones ni su pie.

import { AlertTriangle, CheckCircle2, Info, X, XCircle } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import clsx from 'clsx';
import { useModalLayerCount } from '@/4-presentation/components/ui/modalLayer';
import { ToastContext, type ToastApi, type ToastOptions, type ToastTone } from './ToastContext';

interface ToastItem extends ToastOptions {
  id: string;
  duration: number;
}

const ICONS: Record<ToastTone, typeof CheckCircle2> = {
  success: CheckCircle2,
  info: Info,
  warning: AlertTriangle,
  danger: XCircle,
};

const TONE_CLASS: Record<ToastTone, string> = {
  success: 'text-success',
  info: 'text-accent',
  warning: 'text-warning',
  danger: 'text-danger',
};

const MAX_VISIBLE = 3;
const DEFAULT_MS = 4000;
const WITH_ACTION_MS = 8000;

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const timers = useRef(new Map<string, ReturnType<typeof setTimeout>>());
  const sequence = useRef(0);
  const layers = useModalLayerCount();

  // Al desmontar el proveedor no debe quedar ningún temporizador vivo.
  useEffect(
    () => () => {
      for (const timer of timers.current.values()) clearTimeout(timer);
      timers.current.clear();
    },
    [],
  );

  const dismiss = useCallback((id: string) => {
    const timer = timers.current.get(id);
    if (timer) clearTimeout(timer);
    timers.current.delete(id);
    setToasts((current) => current.filter((toast) => toast.id !== id));
  }, []);

  const dismissAll = useCallback(() => {
    for (const timer of timers.current.values()) clearTimeout(timer);
    timers.current.clear();
    setToasts([]);
  }, []);

  const schedule = useCallback(
    (id: string, duration: number) => {
      if (duration <= 0) return;
      const previous = timers.current.get(id);
      if (previous) clearTimeout(previous);
      timers.current.set(id, setTimeout(() => dismiss(id), duration));
    },
    [dismiss],
  );

  const pause = useCallback((id: string) => {
    const timer = timers.current.get(id);
    if (timer) clearTimeout(timer);
  }, []);

  const notify = useCallback(
    (options: ToastOptions) => {
      sequence.current += 1;
      const id = `toast-${sequence.current}`;
      const duration = options.duration ?? (options.action ? WITH_ACTION_MS : DEFAULT_MS);
      setToasts((current) => {
        const replaced = options.group ? current.find((toast) => toast.group === options.group) : undefined;
        if (replaced) {
          const timer = timers.current.get(replaced.id);
          if (timer) clearTimeout(timer);
          timers.current.delete(replaced.id);
        }
        const kept = replaced ? current.filter((toast) => toast.id !== replaced.id) : current;
        return [...kept.slice(-(MAX_VISIBLE - 1)), { ...options, id, duration }];
      });
      schedule(id, duration);
      return id;
    },
    [schedule],
  );

  const api = useMemo<ToastApi>(() => ({ notify, dismiss, dismissAll }), [notify, dismiss, dismissAll]);

  return (
    <ToastContext.Provider value={api}>
      {children}
      {createPortal(
        <div
          role="status"
          aria-live="polite"
          aria-relevant="additions"
          data-toasts
          className={clsx(
            'toast-region pointer-events-none fixed z-60 flex flex-col gap-2',
            layers > 0
              ? 'inset-x-4 top-4 items-center sm:inset-x-0 sm:top-6'
              : 'inset-x-4 bottom-4 items-end sm:inset-x-auto sm:right-6 sm:bottom-6',
          )}
        >
          {toasts.map((toast) => {
            const tone = toast.tone ?? 'success';
            const Icon = ICONS[tone];
            return (
              <div
                key={toast.id}
                className="pointer-events-auto flex w-full max-w-sm items-start gap-3 rounded-card border border-border bg-surface-2 p-4 shadow-card animate-fade-up"
                onMouseEnter={() => pause(toast.id)}
                onMouseLeave={() => schedule(toast.id, toast.duration)}
                onFocus={() => pause(toast.id)}
                onBlur={(event) => {
                  if (!event.currentTarget.contains(event.relatedTarget as Node | null)) schedule(toast.id, toast.duration);
                }}
              >
                <Icon aria-hidden="true" className={clsx('mt-0.5 size-5 shrink-0', TONE_CLASS[tone])} />
                <div className="min-w-0 flex-1">
                  <p className="text-sm font-semibold text-text">{toast.title}</p>
                  {/* Un error o una advertencia se leen completos (el panel explica ahí qué permiso falta); el resto, en dos líneas. */}
                  {toast.description && (
                    <p className={clsx('mt-0.5 text-sm text-text-muted', tone !== 'danger' && tone !== 'warning' && 'line-clamp-2')}>{toast.description}</p>
                  )}
                  {toast.action && (
                    <button
                      type="button"
                      className="mt-1 inline-flex min-h-9 items-center text-sm font-semibold text-accent transition-colors duration-200 hover:text-accent-hover"
                      onClick={() => {
                        toast.action?.onClick();
                        dismiss(toast.id);
                      }}
                    >
                      {toast.action.label}
                    </button>
                  )}
                </div>
                <button
                  type="button"
                  aria-label="Cerrar aviso"
                  className="-m-3 flex size-11 shrink-0 items-center justify-center rounded-lg text-text-muted transition-colors duration-200 hover:bg-surface-3 hover:text-text"
                  onClick={() => dismiss(toast.id)}
                >
                  <X aria-hidden="true" className="size-4" />
                </button>
              </div>
            );
          })}
        </div>,
        document.body,
      )}
    </ToastContext.Provider>
  );
}
