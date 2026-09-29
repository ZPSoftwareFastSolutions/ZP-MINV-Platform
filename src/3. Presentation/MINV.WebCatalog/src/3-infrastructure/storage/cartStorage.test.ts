// Almacén del carrito: guarda SOLO el SKU y la cantidad con la versión del formato; tolera datos rotos, otra versión del
// formato y un almacenamiento que lanza (lleno o bloqueado) sin romper nada; y avisa cuando otra pestaña cambia el
// carrito. Se prueba con un almacenamiento simulado y con el de jsdom (que se limpia en cada prueba).

import { afterEach, describe, expect, it, vi } from 'vitest';
import type { CartItem } from '@/1-domain/cart/types';
import {
  CART_STORAGE_KEY,
  CART_STORAGE_VERSION,
  LocalCartStore,
  browserStorageArea,
  createCartStore,
  decodeCart,
  encodeCart,
  type CartStorageArea,
  type CartStorageEvents,
} from './cartStorage';
import { InMemoryCartStore } from './memoryCartStore';

const ITEMS: CartItem[] = [
  { sku: 'MON-LG-27', quantity: 2 },
  { sku: 'JUE-PS5-GOW', quantity: 1 },
];

/** Almacenamiento simulado: un mapa, con la opción de lanzar al leer, al escribir o al borrar. */
class FakeArea implements CartStorageArea {
  readonly entries = new Map<string, string>();
  failOn: Partial<Record<'getItem' | 'setItem' | 'removeItem', Error>> = {};

  getItem(key: string): string | null {
    if (this.failOn.getItem) throw this.failOn.getItem;
    return this.entries.get(key) ?? null;
  }

  setItem(key: string, value: string): void {
    if (this.failOn.setItem) throw this.failOn.setItem;
    this.entries.set(key, value);
  }

  removeItem(key: string): void {
    if (this.failOn.removeItem) throw this.failOn.removeItem;
    this.entries.delete(key);
  }
}

/** Ventana simulada: permite disparar el aviso `storage` de otra pestaña. */
class FakeEvents implements CartStorageEvents {
  readonly listeners = new Set<(event: StorageEvent) => void>();

  addEventListener(_type: 'storage', listener: (event: StorageEvent) => void): void {
    this.listeners.add(listener);
  }

  removeEventListener(_type: 'storage', listener: (event: StorageEvent) => void): void {
    this.listeners.delete(listener);
  }

  fire(key: string | null): void {
    const event = new StorageEvent('storage', { key });
    for (const listener of [...this.listeners]) listener(event);
  }
}

function storeWith(text?: string) {
  const area = new FakeArea();
  if (text !== undefined) area.entries.set(CART_STORAGE_KEY, text);
  const events = new FakeEvents();
  return { area, events, store: new LocalCartStore(area, events) };
}

afterEach(() => {
  // Primero se devuelve el almacenamiento real (una prueba pudo reemplazarlo) y después se limpia.
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  window.localStorage.clear();
});

describe('formato guardado', () => {
  it('lleva la versión y, de cada línea, solo el SKU y la cantidad', () => {
    const withExtras = [{ sku: 'MON-LG-27', quantity: 2, price: 2049, name: 'Monitor', phone: '70000000' } as CartItem];
    const stored = JSON.parse(encodeCart(withExtras)) as Record<string, unknown>;
    expect(stored).toEqual({ v: 1, items: [{ sku: 'MON-LG-27', quantity: 2 }] });
    expect(CART_STORAGE_VERSION).toBe(1);
    expect(CART_STORAGE_KEY).toBe('minv.carrito');
  });

  it('lee lo que escribió', () => {
    expect(decodeCart(encodeCart(ITEMS))).toEqual(ITEMS);
    expect(decodeCart(null)).toEqual([]);
    expect(decodeCart('')).toEqual([]);
  });

  it.each([
    ['texto que no es JSON', '{"v":1,"items":[{"sku"'],
    ['texto suelto', 'hola'],
    ['un número', '42'],
    ['null', 'null'],
    ['una lista suelta (sin versión)', '[{"sku":"MON-LG-27","quantity":1}]'],
    ['un objeto sin versión', '{"items":[{"sku":"MON-LG-27","quantity":1}]}'],
    ['una versión vieja', '{"v":0,"items":[{"sku":"MON-LG-27","quantity":1}]}'],
    ['una versión futura', '{"v":2,"items":[{"sku":"MON-LG-27","quantity":1}]}'],
    ['la versión como texto', '{"v":"1","items":[]}'],
    ['líneas que no son una lista', '{"v":1,"items":{"sku":"MON-LG-27"}}'],
    ['algo enorme', JSON.stringify({ v: 1, items: [], relleno: 'x'.repeat(9_000) })],
  ])('descarta %s', (_name, text) => {
    expect(decodeCart(text)).toBeNull();
  });

  it('dentro de un formato válido, limpia las líneas que no sirven', () => {
    const text = JSON.stringify({
      v: 1,
      items: [{ sku: 'mon-lg-27', quantity: 2 }, { sku: 'CON ESPACIO', quantity: 1 }, { sku: 'JUE-1', quantity: 400 }, { sku: 'TEC-1', quantity: -1 }, null, 'x', { sku: 'MON-LG-27', quantity: 1 }],
    });
    expect(decodeCart(text)).toEqual([
      { sku: 'MON-LG-27', quantity: 3 },
      { sku: 'JUE-1', quantity: 16 },
    ]);
  });
});

describe('LocalCartStore', () => {
  it('sin nada guardado devuelve un carrito vacío', () => {
    const { store } = storeWith();
    expect(store.load()).toEqual([]);
    expect(store.persistent).toBe(true);
  });

  it('guarda y vuelve a leer; un carrito vacío borra la entrada', () => {
    const { store, area } = storeWith();
    expect(store.save(ITEMS)).toBe(true);
    expect(JSON.parse(area.entries.get(CART_STORAGE_KEY) ?? '')).toEqual({ v: 1, items: ITEMS });
    expect(store.load()).toEqual(ITEMS);
    expect(store.save([])).toBe(true);
    expect(area.entries.has(CART_STORAGE_KEY)).toBe(false);
    expect(store.load()).toEqual([]);
  });

  it('no toca otras entradas del almacenamiento', () => {
    const { store, area } = storeWith();
    area.entries.set('otra-cosa', 'intacta');
    store.save(ITEMS);
    store.save([]);
    store.load();
    expect([...area.entries]).toEqual([['otra-cosa', 'intacta']]);
  });

  it.each([
    ['datos rotos', '{"v":1,"items":[{"sku"'],
    ['una versión vieja del formato', '{"v":0,"items":[{"sku":"MON-LG-27","quantity":1}]}'],
    ['otra forma', '["MON-LG-27"]'],
  ])('con %s devuelve un carrito vacío y borra lo guardado', (_name, text) => {
    const { store, area } = storeWith(text);
    expect(store.load()).toEqual([]);
    expect(area.entries.has(CART_STORAGE_KEY)).toBe(false);
    // Y después se puede guardar con normalidad.
    expect(store.save(ITEMS)).toBe(true);
    expect(store.load()).toEqual(ITEMS);
  });

  it('si el almacenamiento lanza al leer, devuelve un carrito vacío', () => {
    const { store, area } = storeWith(encodeCart(ITEMS));
    area.failOn.getItem = new DOMException('bloqueado', 'SecurityError');
    expect(() => store.load()).not.toThrow();
    expect(store.load()).toEqual([]);
  });

  it('si el almacenamiento está lleno o bloqueado, guardar devuelve false sin lanzar', () => {
    const { store, area } = storeWith();
    area.failOn.setItem = new DOMException('lleno', 'QuotaExceededError');
    expect(() => store.save(ITEMS)).not.toThrow();
    expect(store.save(ITEMS)).toBe(false);
    area.failOn.removeItem = new DOMException('bloqueado', 'SecurityError');
    expect(store.save([])).toBe(false);
  });

  it('si lo guardado no sirve y tampoco se puede borrar, igual devuelve un carrito vacío', () => {
    const { store, area } = storeWith('roto');
    area.failOn.removeItem = new DOMException('bloqueado', 'SecurityError');
    expect(() => store.load()).not.toThrow();
    expect(store.load()).toEqual([]);
  });

  it('avisa cuando OTRA pestaña cambia el carrito, y deja de avisar al darse de baja', () => {
    const { store, area, events } = storeWith();
    const listener = vi.fn();
    const stop = store.subscribe(listener);

    area.entries.set(CART_STORAGE_KEY, encodeCart(ITEMS));
    events.fire(CART_STORAGE_KEY);
    expect(listener).toHaveBeenLastCalledWith(ITEMS);

    // Otra entrada del sitio no es asunto del carrito.
    events.fire('otra-cosa');
    expect(listener).toHaveBeenCalledTimes(1);

    // `key` nulo: la otra pestaña vació todo el almacenamiento.
    area.entries.clear();
    events.fire(null);
    expect(listener).toHaveBeenLastCalledWith([]);
    expect(listener).toHaveBeenCalledTimes(2);

    stop();
    expect(events.listeners.size).toBe(0);
    events.fire(CART_STORAGE_KEY);
    expect(listener).toHaveBeenCalledTimes(2);
  });

  it('si otra pestaña deja datos rotos, avisa con un carrito vacío', () => {
    const { store, area, events } = storeWith(encodeCart(ITEMS));
    const listener = vi.fn();
    store.subscribe(listener);
    area.entries.set(CART_STORAGE_KEY, '{{{');
    events.fire(CART_STORAGE_KEY);
    expect(listener).toHaveBeenLastCalledWith([]);
  });

  it('sin ventana (fuera del navegador) suscribirse no hace nada', () => {
    const store = new LocalCartStore(new FakeArea(), null);
    expect(() => store.subscribe(vi.fn())()).not.toThrow();
  });
});

describe('con el almacenamiento del navegador (jsdom)', () => {
  it('guarda en la entrada del carrito y la lee otra instancia', () => {
    const first = new LocalCartStore(window.localStorage);
    expect(first.save(ITEMS)).toBe(true);
    expect(window.localStorage.getItem(CART_STORAGE_KEY)).toBe('{"v":1,"items":[{"sku":"MON-LG-27","quantity":2},{"sku":"JUE-PS5-GOW","quantity":1}]}');
    expect(window.localStorage.length).toBe(1);
    expect(new LocalCartStore(window.localStorage).load()).toEqual(ITEMS);
  });

  it('recibe el aviso `storage` de la ventana', () => {
    const store = new LocalCartStore(window.localStorage);
    const listener = vi.fn();
    const stop = store.subscribe(listener);
    window.localStorage.setItem(CART_STORAGE_KEY, encodeCart([{ sku: 'TEC-LOG-G915', quantity: 3 }]));
    window.dispatchEvent(new StorageEvent('storage', { key: CART_STORAGE_KEY, storageArea: window.localStorage }));
    expect(listener).toHaveBeenLastCalledWith([{ sku: 'TEC-LOG-G915', quantity: 3 }]);
    stop();
    window.dispatchEvent(new StorageEvent('storage', { key: CART_STORAGE_KEY, storageArea: window.localStorage }));
    expect(listener).toHaveBeenCalledTimes(1);
  });

  it('`createCartStore` usa el almacenamiento del navegador cuando está disponible', () => {
    expect(browserStorageArea()).toBe(window.localStorage);
    const store = createCartStore();
    expect(store).toBeInstanceOf(LocalCartStore);
    expect(store.persistent).toBe(true);
  });

  it('si el navegador no ofrece almacenamiento, el carrito vive en memoria', () => {
    vi.stubGlobal('localStorage', undefined);
    expect(browserStorageArea()).toBeNull();
    const store = createCartStore();
    expect(store).toBeInstanceOf(InMemoryCartStore);
    expect(store.persistent).toBe(false);
    expect(store.save(ITEMS)).toBe(true);
    expect(store.load()).toEqual(ITEMS);
  });

  it('si el navegador bloquea el almacenamiento (lanza al usarlo), el carrito vive en memoria', () => {
    const blocked = new FakeArea();
    blocked.failOn.getItem = new DOMException('bloqueado', 'SecurityError');
    vi.stubGlobal('localStorage', blocked);
    expect(browserStorageArea()).toBeNull();
    expect(createCartStore()).toBeInstanceOf(InMemoryCartStore);
  });

  it('si el navegador lanza al pedir el almacenamiento, el carrito vive en memoria', () => {
    const original = Object.getOwnPropertyDescriptor(globalThis, 'localStorage');
    Object.defineProperty(globalThis, 'localStorage', {
      configurable: true,
      get() {
        throw new DOMException('bloqueado', 'SecurityError');
      },
    });
    try {
      expect(browserStorageArea()).toBeNull();
      expect(createCartStore()).toBeInstanceOf(InMemoryCartStore);
    } finally {
      if (original) Object.defineProperty(globalThis, 'localStorage', original);
      else Reflect.deleteProperty(globalThis, 'localStorage');
    }
  });
});

describe('InMemoryCartStore', () => {
  it('guarda y lee en memoria, sin tocar el almacenamiento del navegador', () => {
    const store = new InMemoryCartStore();
    expect(store.persistent).toBe(false);
    expect(store.load()).toEqual([]);
    expect(store.save(ITEMS)).toBe(true);
    expect(store.load()).toEqual(ITEMS);
    expect(window.localStorage.length).toBe(0);
  });

  it('entrega copias: cambiar lo leído no cambia lo guardado', () => {
    const store = new InMemoryCartStore(ITEMS);
    const loaded = store.load();
    loaded[0].quantity = 9;
    loaded.pop();
    expect(store.load()).toEqual(ITEMS);
  });

  it('valida lo que recibe y puede simular a otra pestaña', () => {
    const store = new InMemoryCartStore([{ sku: 'con espacio', quantity: 1 }, ...ITEMS], { persistent: true });
    expect(store.persistent).toBe(true);
    expect(store.load()).toEqual(ITEMS);
    const listener = vi.fn();
    const stop = store.subscribe(listener);
    store.changeFromElsewhere([{ sku: 'TEC-LOG-G915', quantity: 1 }]);
    expect(listener).toHaveBeenLastCalledWith([{ sku: 'TEC-LOG-G915', quantity: 1 }]);
    expect(store.load()).toEqual([{ sku: 'TEC-LOG-G915', quantity: 1 }]);
    stop();
    store.changeFromElsewhere([]);
    expect(listener).toHaveBeenCalledTimes(1);
  });
});
