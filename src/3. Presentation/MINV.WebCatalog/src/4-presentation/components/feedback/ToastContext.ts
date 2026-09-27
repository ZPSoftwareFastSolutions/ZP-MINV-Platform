import { createContext } from 'react';

export type ToastTone = 'success' | 'info' | 'warning' | 'danger';

export interface ToastOptions {
  title: string;
  description?: string;
  tone?: ToastTone;
  /** Botón de acción dentro del aviso («Ver armado»). */
  action?: { label: string; onClick: () => void };
  /** Milisegundos antes de cerrarse solo (4000 por defecto; 0 = no se cierra solo). */
  duration?: number;
}

export interface ToastApi {
  notify(options: ToastOptions): string;
  dismiss(id: string): void;
}

export const ToastContext = createContext<ToastApi | null>(null);
