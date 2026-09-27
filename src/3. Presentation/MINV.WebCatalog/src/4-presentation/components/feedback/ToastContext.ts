import { createContext } from 'react';

export type ToastTone = 'success' | 'info' | 'warning' | 'danger';

export interface ToastOptions {
  title: string;
  description?: string;
  tone?: ToastTone;
  /** Botón de acción dentro del aviso («Ver armado»). */
  action?: { label: string; onClick: () => void };
  /**
   * Milisegundos antes de cerrarse solo (0 = no se cierra solo). Por defecto 4000; con `action`, 8000 para que un
   * usuario de teclado o lento alcance a usarla.
   */
  duration?: number;
  /**
   * Avisos del mismo grupo se reemplazan entre sí (un solo aviso «Agregado al armado» aunque se agreguen tres piezas
   * seguidas) en vez de apilarse.
   */
  group?: string;
}

export interface ToastApi {
  notify(options: ToastOptions): string;
  dismiss(id: string): void;
  /** Cierra todos los avisos (al abrir un modal que no debe quedar tapado). */
  dismissAll(): void;
}

export const ToastContext = createContext<ToastApi | null>(null);
