// ÚNICO lugar de la web que usa el almacenamiento del navegador (regla P-08), y SOLO para el carrito.
//
// Qué se guarda: una entrada (`minv.carrito`) con la versión del formato y, por cada línea, el SKU y la cantidad:
//     {"v":1,"items":[{"sku":"MON-LG-27GS75Q","quantity":1}]}
// Nada más: ni precios, ni nombres, ni datos de la persona. El precio y lo disponible salen siempre del catálogo fresco.
//
// Tolerancia: el almacenamiento puede estar lleno, bloqueado por el navegador o traer cualquier cosa (otra versión del
// formato, texto roto, datos escritos a mano). Nada de eso rompe la página: leer devuelve un carrito vacío, guardar
// devuelve false, y el carrito sigue funcionando en memoria.
//
// Entre pestañas: el navegador avisa con el evento `storage` a las OTRAS pestañas del mismo sitio cuando cambia la
// entrada; quien se suscribe recibe las líneas nuevas.

import { sanitizeCartItems } from '@/1-domain/cart/cart';
import type { CartItem } from '@/1-domain/cart/types';
import type { ICartStore } from '@/1-domain/ports/ICartStore';
import { InMemoryCartStore } from './memoryCartStore';

/** Nombre de la entrada del carrito en el almacenamiento del navegador. */
export const CART_STORAGE_KEY = 'minv.carrito';

/** Versión del formato guardado. Si el formato cambia, sube: lo guardado con otra versión se descarta. */
export const CART_STORAGE_VERSION = 1;

/** Lo guardado nunca debería pasar de unos 2 kB (20 líneas); algo mucho más grande no lo escribió esta web. */
const MAX_STORED_LENGTH = 8_000;

/** Lo mínimo que se necesita del almacenamiento del navegador (las pruebas pasan uno propio). */
export type CartStorageArea = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;

/** De dónde llegan los avisos de otras pestañas (la ventana del navegador). */
export interface CartStorageEvents {
  addEventListener(type: 'storage', listener: (event: StorageEvent) => void): void;
  removeEventListener(type: 'storage', listener: (event: StorageEvent) => void): void;
}

/** Texto que se guarda: versión del formato + SKU y cantidad de cada línea. */
export function encodeCart(items: readonly CartItem[]): string {
  return JSON.stringify({ v: CART_STORAGE_VERSION, items: items.map((item) => ({ sku: item.sku, quantity: item.quantity })) });
}

/**
 * Líneas válidas a partir del texto guardado. Devuelve null cuando lo guardado NO sirve (texto roto, otra versión del
 * formato, otra forma) para que quien lee lo descarte; sin nada guardado devuelve un carrito vacío.
 */
export function decodeCart(text: string | null): CartItem[] | null {
  if (text === null || text === '') return [];
  if (text.length > MAX_STORED_LENGTH) return null;
  let stored: unknown;
  try {
    stored = JSON.parse(text);
  } catch {
    return null;
  }
  if (stored === null || typeof stored !== 'object' || Array.isArray(stored)) return null;
  const { v, items } = stored as { v?: unknown; items?: unknown };
  if (v !== CART_STORAGE_VERSION || !Array.isArray(items)) return null;
  return sanitizeCartItems(items);
}

/** El carrito en el almacenamiento del navegador. */
export class LocalCartStore implements ICartStore {
  readonly persistent = true;
  private readonly area: CartStorageArea;
  private readonly events: CartStorageEvents | null;

  constructor(area: CartStorageArea, events: CartStorageEvents | null = typeof window === 'undefined' ? null : window) {
    this.area = area;
    this.events = events;
  }

  load(): CartItem[] {
    let text: string | null;
    try {
      text = this.area.getItem(CART_STORAGE_KEY);
    } catch {
      return [];
    }
    const items = decodeCart(text);
    if (items !== null) return items;
    // Lo guardado no sirve: se borra para que no vuelva a estorbar (si tampoco se puede borrar, se ignora).
    try {
      this.area.removeItem(CART_STORAGE_KEY);
    } catch {
      /* almacenamiento bloqueado: el carrito queda vacío en memoria */
    }
    return [];
  }

  save(items: readonly CartItem[]): boolean {
    try {
      if (items.length === 0) this.area.removeItem(CART_STORAGE_KEY);
      else this.area.setItem(CART_STORAGE_KEY, encodeCart(items));
      return true;
    } catch {
      // Lleno (QuotaExceededError) o bloqueado (SecurityError): el carrito sigue en memoria.
      return false;
    }
  }

  subscribe(listener: (items: CartItem[]) => void): () => void {
    const events = this.events;
    if (!events) return () => undefined;
    const onStorage = (event: StorageEvent) => {
      // Solo la entrada del carrito (`key` nulo = otra pestaña vació todo el almacenamiento del sitio).
      if (event.key !== null && event.key !== CART_STORAGE_KEY) return;
      if (event.storageArea && event.storageArea !== this.area) return;
      listener(this.load());
    };
    events.addEventListener('storage', onStorage);
    return () => events.removeEventListener('storage', onStorage);
  }
}

/** El almacenamiento del navegador, o null si el navegador no lo ofrece o no deja usarlo (modo privado, bloqueo). */
export function browserStorageArea(): CartStorageArea | null {
  try {
    const area = globalThis.localStorage;
    if (!area) return null;
    // Algunos navegadores entregan el objeto pero lanzan al usarlo: se comprueba con una lectura.
    area.getItem(CART_STORAGE_KEY);
    return area;
  } catch {
    return null;
  }
}

/**
 * Almacén del carrito para el navegador: el almacenamiento local o, si no está disponible, la memoria (el carrito
 * funciona igual, pero se pierde al cerrar la pestaña).
 */
export function createCartStore(area: CartStorageArea | null = browserStorageArea()): ICartStore {
  return area ? new LocalCartStore(area) : new InMemoryCartStore();
}
