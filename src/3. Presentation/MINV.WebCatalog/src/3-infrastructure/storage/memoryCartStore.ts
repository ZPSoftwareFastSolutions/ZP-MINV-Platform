// Almacén del carrito EN MEMORIA: el mismo puerto que el del navegador, sin tocar ningún almacenamiento. Se usa cuando
// el navegador no deja guardar (el carrito funciona, pero se pierde al cerrar la pestaña) y en las pruebas.

import { sanitizeCartItems } from '@/1-domain/cart/cart';
import type { CartItem } from '@/1-domain/cart/types';
import type { ICartStore } from '@/1-domain/ports/ICartStore';

export interface InMemoryCartStoreOptions {
  /** Las pruebas de la presentación lo marcan como guardado para no ver el aviso de «carrito sin guardar». */
  persistent?: boolean;
}

export class InMemoryCartStore implements ICartStore {
  readonly persistent: boolean;
  private items: CartItem[];
  private readonly listeners = new Set<(items: CartItem[]) => void>();

  constructor(initial: readonly CartItem[] = [], options: InMemoryCartStoreOptions = {}) {
    this.items = sanitizeCartItems([...initial]);
    this.persistent = options.persistent ?? false;
  }

  load(): CartItem[] {
    return this.items.map((item) => ({ ...item }));
  }

  save(items: readonly CartItem[]): boolean {
    this.items = sanitizeCartItems([...items]);
    return true;
  }

  subscribe(listener: (items: CartItem[]) => void): () => void {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  }

  /** Simula que OTRA pestaña cambió el carrito: lo guarda y avisa a quien escucha. */
  changeFromElsewhere(items: readonly CartItem[]): void {
    this.items = sanitizeCartItems([...items]);
    for (const listener of [...this.listeners]) listener(this.load());
  }
}
