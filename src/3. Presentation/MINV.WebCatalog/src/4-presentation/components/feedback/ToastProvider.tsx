// Avisos breves («Agregado al armado»). Región aria-live, cierre automático (se pausa al pasar el mouse) y acción opcional.

import { AlertTriangle, CheckCircle2, Info, X, XCircle } from 'lucide-react';
import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';
import clsx from 'clsx';
import { ToastContext, type ToastApi, type ToastOptions, type ToastTone } from './ToastContext';

interface ToastItem extends ToastOptions {
  id: string;
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

let sequence = 0;

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const timers = useRef(new Map<string, ReturnType<typeof setTimeout>>());

  const dismiss = useCallback((id: string) => {
    const timer = timers.current.get(id);
    if (timer) clearTimeout(timer);
    timers.current.delete(id);
    setToasts((current) => current.filter((toast) => toast.id !== id));
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
      sequence += 1;
      const id = `toast-${sequence}`;
      const duration = options.duration ?? 4000;
      setToasts((current) => [...current.slice(-2), { ...options, id, duration }]);
      schedule(id, duration);
      return id;
    },
    [schedule],
  );

  const api = useMemo<ToastApi>(() => ({ notify, dismiss }), [notify, dismiss]);

  return (
    <ToastContext.Provider value={api}>
      {children}
      <div
        aria-live="polite"
        aria-relevant="additions"
        className="pointer-events-none fixed inset-x-4 bottom-4 z-60 flex flex-col items-end gap-2 sm:inset-x-auto sm:right-6 sm:bottom-6"
      >
        {toasts.map((toast) => {
          const tone = toast.tone ?? 'success';
          const Icon = ICONS[tone];
          return (
            <div
              key={toast.id}
              role="status"
              className="pointer-events-auto flex w-full max-w-sm items-start gap-3 rounded-card border border-border bg-surface-2 p-4 shadow-card animate-fade-up"
              onMouseEnter={() => pause(toast.id)}
              onMouseLeave={() => schedule(toast.id, toast.duration ?? 4000)}
            >
              <Icon aria-hidden="true" className={clsx('mt-0.5 size-5 shrink-0', TONE_CLASS[tone])} />
              <div className="min-w-0 flex-1">
                <p className="text-sm font-semibold text-text">{toast.title}</p>
                {toast.description && <p className="mt-0.5 line-clamp-2 text-sm text-text-muted">{toast.description}</p>}
                {toast.action && (
                  <button
                    type="button"
                    className="mt-2 text-sm font-semibold text-accent transition-colors duration-200 hover:text-accent-hover"
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
                className="-m-2 flex size-9 shrink-0 items-center justify-center rounded-lg text-text-muted transition-colors duration-200 hover:bg-surface-3 hover:text-text"
                onClick={() => dismiss(toast.id)}
              >
                <X aria-hidden="true" className="size-4" />
              </button>
            </div>
          );
        })}
      </div>
    </ToastContext.Provider>
  );
}
