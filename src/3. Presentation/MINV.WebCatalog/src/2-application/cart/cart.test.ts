// Casos de uso del carrito sobre un almacén simulado (el puerto ICartStore): guardar después de cada cambio, seguir
// funcionando si guardar falla, ponerse al día con otra pestaña, cruzar el carrito con el catálogo fresco (qué se agotó
// o bajó) y el contrato de la dirección de `/reservar`. Sin red y sin almacenamiento real.

import { describe, expect, it, vi } from 'vitest';
import type { CartItem } from '@/1-domain/cart/types';
import type { Product } from '@/1-domain/catalog/types';
import type { ICartStore } from '@/1-domain/ports/ICartStore';
import { MOCK_CATALOG } from '@/3-infrastructure/data/mockCatalog';
import { adjustCart, checkoutItemPath, checkoutSelection, createCartUseCases, isSingleItemCheckout, parseCheckoutItem, reviewCart } from './index';

const BASE = MOCK_CATALOG.products[0];

function product(sku: string, overrides: Partial<Product> = {}): Product {
  return { ...BASE, sku, slug: sku.toLowerCase(), name: `Producto ${sku}`, shortName: `Producto ${sku}`, price: 100, listPrice: null, stock: 10, reserved: 0, ...overrides };
}

function lookupOf(...products: Product[]) {
  const bySku = new Map(products.map((item) => [item.sku, item]));
  return (sku: string) => bySku.get(sku);
}

interface FakeStore extends ICartStore {
  saved: CartItem[][];
  /** Simula otra pestaña. */
  emit(items: unknown): void;
  listeners: number;
}

function fakeStore(initial: CartItem[] = [], options: { failSave?: boolean; persistent?: boolean } = {}): FakeStore {
  const listeners = new Set<(items: CartItem[]) => void>();
  const store: FakeStore = {
    persistent: options.persistent ?? true,
    saved: [],
    get listeners() {
      return listeners.size;
    },
    load: vi.fn(() => initial.map((item) => ({ ...item }))),
    save: vi.fn((items: readonly CartItem[]) => {
      if (options.failSave) return false;
      store.saved.push(items.map((item) => ({ ...item })));
      return true;
    }),
    subscribe: vi.fn((listener: (items: CartItem[]) => void) => {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    }),
    emit(items) {
      for (const listener of [...listeners]) listener(items as CartItem[]);
    },
  };
  return store;
}

describe('casos de uso del carrito', () => {
  it('empieza con lo guardado, ya validado', () => {
    const store = fakeStore([
      { sku: 'MON-1', quantity: 2 },
      { sku: 'con espacio', quantity: 1 },
      { sku: 'JUE-1', quantity: 99 },
    ]);
    const cart = createCartUseCases(store);
    expect(cart.current().items).toEqual([
      { sku: 'MON-1', quantity: 2 },
      { sku: 'JUE-1', quantity: 16 },
    ]);
    expect(cart.isPersistent()).toBe(true);
    expect(store.save).not.toHaveBeenCalled();
  });

  it('guarda SOLO el SKU y la cantidad después de cada cambio y avisa a quien escucha', () => {
    const store = fakeStore();
    const cart = createCartUseCases(store);
    const listener = vi.fn();
    cart.subscribe(listener);

    expect(cart.add(product('MON-1', { stock: 3 }), 2)).toMatchObject({ added: 2, quantity: 2, limit: null });
    cart.add(product('JUE-1'));
    cart.setQuantity('JUE-1', 4);
    cart.remove('MON-1');

    expect(store.saved).toEqual([
      [{ sku: 'MON-1', quantity: 2 }],
      [
        { sku: 'MON-1', quantity: 2 },
        { sku: 'JUE-1', quantity: 1 },
      ],
      [
        { sku: 'MON-1', quantity: 2 },
        { sku: 'JUE-1', quantity: 4 },
      ],
      [{ sku: 'JUE-1', quantity: 4 }],
    ]);
    expect(Object.keys(store.saved[0][0]).sort()).toEqual(['quantity', 'sku']);
    expect(listener).toHaveBeenCalledTimes(4);

    cart.clear();
    expect(store.saved.at(-1)).toEqual([]);
    expect(cart.current().items).toEqual([]);
  });

  it('un cambio que no cambia nada no guarda ni avisa', () => {
    const store = fakeStore([{ sku: 'MON-1', quantity: 3 }]);
    const cart = createCartUseCases(store);
    const listener = vi.fn();
    cart.subscribe(listener);
    const before = cart.current();

    expect(cart.add(product('MON-1', { stock: 3 }))).toMatchObject({ added: 0, quantity: 3, limit: 'stock' });
    cart.setQuantity('MON-1', 3);
    cart.remove('NO-ESTA');
    cart.add(product('AGOTADO', { stock: 0 }));

    expect(cart.current()).toBe(before);
    expect(store.save).not.toHaveBeenCalled();
    expect(listener).not.toHaveBeenCalled();
  });

  it('acota la cantidad a lo disponible cuando se pasa el producto', () => {
    const cart = createCartUseCases(fakeStore([{ sku: 'MON-1', quantity: 1 }]));
    cart.setQuantity('MON-1', 9, { stock: 4 });
    expect(cart.current().items).toEqual([{ sku: 'MON-1', quantity: 4 }]);
  });

  it('si guardar falla el carrito sigue funcionando en memoria y avisa que no se guarda', () => {
    const store = fakeStore([], { failSave: true });
    const cart = createCartUseCases(store);
    expect(cart.isPersistent()).toBe(true);
    cart.add(product('MON-1'), 2);
    expect(cart.current().items).toEqual([{ sku: 'MON-1', quantity: 2 }]);
    expect(cart.isPersistent()).toBe(false);
    cart.setQuantity('MON-1', 5);
    expect(cart.current().items).toEqual([{ sku: 'MON-1', quantity: 5 }]);
  });

  it('un almacén en memoria nunca cuenta como guardado', () => {
    const cart = createCartUseCases(fakeStore([], { persistent: false }));
    cart.add(product('MON-1'));
    expect(cart.isPersistent()).toBe(false);
  });

  it('se pone al día cuando otra pestaña cambia el carrito (sin volver a guardar)', () => {
    const store = fakeStore([{ sku: 'MON-1', quantity: 1 }]);
    const cart = createCartUseCases(store);
    const listener = vi.fn();
    const stop = cart.subscribe(listener);

    store.emit([
      { sku: 'MON-1', quantity: 3 },
      { sku: 'JUE-1', quantity: 1 },
    ]);
    expect(cart.current().items).toEqual([
      { sku: 'MON-1', quantity: 3 },
      { sku: 'JUE-1', quantity: 1 },
    ]);
    expect(listener).toHaveBeenCalledTimes(1);
    expect(store.save).not.toHaveBeenCalled();

    // Lo mismo otra vez no avisa; algo que no es una lista deja el carrito vacío.
    store.emit([
      { sku: 'MON-1', quantity: 3 },
      { sku: 'JUE-1', quantity: 1 },
    ]);
    expect(listener).toHaveBeenCalledTimes(1);
    store.emit('cualquier cosa');
    expect(cart.current().items).toEqual([]);

    stop();
    expect(store.listeners).toBe(0);
    store.emit([{ sku: 'MON-1', quantity: 9 }]);
    expect(cart.current().items).toEqual([]);
  });

  it('escucha al almacén una sola vez aunque haya varios oyentes y deja de escuchar con el último', () => {
    const store = fakeStore();
    const cart = createCartUseCases(store);
    const stopA = cart.subscribe(vi.fn());
    const stopB = cart.subscribe(vi.fn());
    expect(store.subscribe).toHaveBeenCalledTimes(1);
    stopA();
    expect(store.listeners).toBe(1);
    stopB();
    expect(store.listeners).toBe(0);
  });
});

describe('cruzar el carrito con el catálogo fresco', () => {
  const cart = {
    items: [
      { sku: 'MON-1', quantity: 2 },
      { sku: 'JUE-1', quantity: 5 },
      { sku: 'CON-1', quantity: 1 },
      { sku: 'RES-1', quantity: 1 },
      { sku: 'VIEJO-1', quantity: 3 },
    ],
  };
  const lookup = lookupOf(
    product('MON-1', { price: 1099.99, listPrice: 1299.99, stock: 8 }),
    product('JUE-1', { price: 0.1, stock: 2 }),
    product('CON-1', { price: 4500, stock: 0, reserved: 0 }),
    product('RES-1', { price: 300, stock: 0, reserved: 2 }),
  );

  it('marca lo que está bien, lo que bajó, lo que se agotó y lo que ya no está publicado', () => {
    const review = reviewCart(cart, lookup);
    expect(review.lines.map((line) => [line.sku, line.status, line.available, line.max, line.suggestedQuantity])).toEqual([
      ['MON-1', 'ok', 8, 8, 2],
      ['JUE-1', 'reduced', 2, 2, 2],
      ['CON-1', 'sold_out', 0, 0, 0],
      ['RES-1', 'sold_out', 0, 0, 0],
      ['VIEJO-1', 'unavailable', 0, 0, 0],
    ]);
    expect(review.issues.map((line) => line.sku)).toEqual(['JUE-1', 'CON-1', 'RES-1', 'VIEJO-1']);
    expect(review.reservable).toBe(false);
    expect(review.lines[4].product).toBeNull();
  });

  it('calcula el total con centavos exactos y deja afuera lo agotado y lo que ya no está', () => {
    const review = reviewCart(cart, lookup);
    expect(review.lines[0].subtotal).toBe(2199.98);
    expect(review.lines[1].subtotal).toBe(0.5);
    expect(review.lines.map((line) => line.counted)).toEqual([true, true, false, false, false]);
    expect(review.totalCents).toBe(219998 + 50);
    expect(review.total).toBe(2200.48);
    expect(review.savings).toBe(400);
    expect(review.count).toBe(12);
  });

  it('un carrito vacío no se puede reservar y uno sin cambios sí', () => {
    expect(reviewCart({ items: [] }, lookup)).toMatchObject({ lines: [], total: 0, count: 0, reservable: false });
    expect(reviewCart({ items: [{ sku: 'MON-1', quantity: 8 }] }, lookup)).toMatchObject({ reservable: true, issues: [] });
  });

  it('con 40 disponibles el tope de la línea sigue siendo 16', () => {
    const review = reviewCart({ items: [{ sku: 'JUE-2', quantity: 16 }] }, lookupOf(product('JUE-2', { stock: 40 })));
    expect(review.lines[0]).toMatchObject({ status: 'ok', available: 40, max: 16 });
  });

  it('ajustar baja lo que supera lo disponible y quita lo agotado y lo que ya no está', () => {
    const adjustment = adjustCart(cart, lookup);
    expect(adjustment.cart.items).toEqual([
      { sku: 'MON-1', quantity: 2 },
      { sku: 'JUE-1', quantity: 2 },
    ]);
    expect(adjustment.changes).toEqual([
      { sku: 'JUE-1', name: 'Producto JUE-1', from: 5, to: 2, reason: 'reduced' },
      { sku: 'CON-1', name: 'Producto CON-1', from: 1, to: 0, reason: 'sold_out' },
      { sku: 'RES-1', name: 'Producto RES-1', from: 1, to: 0, reason: 'sold_out' },
      { sku: 'VIEJO-1', name: 'VIEJO-1', from: 3, to: 0, reason: 'unavailable' },
    ]);
    expect(reviewCart(adjustment.cart, lookup).reservable).toBe(true);
    expect(cart.items).toHaveLength(5);
  });

  it('ajustar una sola línea no toca las demás, y sin nada por ajustar devuelve el mismo carrito', () => {
    const one = adjustCart(cart, lookup, 'JUE-1');
    expect(one.cart.items.map((item) => [item.sku, item.quantity])).toEqual([
      ['MON-1', 2],
      ['JUE-1', 2],
      ['CON-1', 1],
      ['RES-1', 1],
      ['VIEJO-1', 3],
    ]);
    expect(one.changes).toHaveLength(1);
    const clean = { items: [{ sku: 'MON-1', quantity: 2 }] };
    expect(adjustCart(clean, lookup)).toEqual({ cart: clean, changes: [] });
    expect(adjustCart(cart, lookup, 'MON-1').changes).toEqual([]);
  });

  it('los casos de uso ajustan, guardan el resultado y devuelven qué cambió', () => {
    const store = fakeStore(cart.items);
    const useCases = createCartUseCases(store);
    expect(useCases.review(lookup).issues).toHaveLength(4);
    const adjustment = useCases.adjust(lookup);
    expect(adjustment.changes).toHaveLength(4);
    expect(useCases.current().items).toEqual([
      { sku: 'MON-1', quantity: 2 },
      { sku: 'JUE-1', quantity: 2 },
    ]);
    expect(store.saved.at(-1)).toEqual(useCases.current().items);
    expect(useCases.review(lookup).reservable).toBe(true);
  });
});

describe('dirección de la reserva (`/reservar`)', () => {
  const lookup = lookupOf(product('MON-LG-27', { price: 2049, stock: 3 }));
  const cart = { items: [{ sku: 'JUE-1', quantity: 2 }] };

  it('arma la dirección de un artículo suelto', () => {
    expect(checkoutItemPath('MON-LG-27')).toBe('/reservar?sku=MON-LG-27&cantidad=1');
    expect(checkoutItemPath('MON-LG-27', 3)).toBe('/reservar?sku=MON-LG-27&cantidad=3');
    expect(checkoutItemPath('MON-LG-27', 99)).toBe('/reservar?sku=MON-LG-27&cantidad=16');
    expect(checkoutItemPath('MON-LG-27', 0)).toBe('/reservar?sku=MON-LG-27&cantidad=1');
  });

  it('lee el artículo de la dirección y desconfía de lo que trae', () => {
    expect(parseCheckoutItem('?sku=MON-LG-27&cantidad=2')).toEqual({ sku: 'MON-LG-27', quantity: 2 });
    expect(parseCheckoutItem('sku=mon-lg-27')).toEqual({ sku: 'MON-LG-27', quantity: 1 });
    expect(parseCheckoutItem(new URLSearchParams({ sku: 'MON-LG-27', cantidad: '999' }))).toEqual({ sku: 'MON-LG-27', quantity: 16 });
    expect(parseCheckoutItem('?sku=MON-LG-27&cantidad=99999999999999999999999')).toEqual({ sku: 'MON-LG-27', quantity: 16 });
    for (const quantity of ['0', '-3', '2.5', 'abc', '', '1e3']) {
      expect(parseCheckoutItem(`?sku=MON-LG-27&cantidad=${quantity}`)).toEqual({ sku: 'MON-LG-27', quantity: 1 });
    }
    expect(parseCheckoutItem('?sku=%3Cscript%3E&cantidad=1')).toBeNull();
    expect(parseCheckoutItem('?sku=&cantidad=1')).toBeNull();
    expect(parseCheckoutItem('?cantidad=2')).toBeNull();
    expect(parseCheckoutItem('')).toBeNull();
  });

  it('con `sku` reserva ESE solo artículo y no mira el carrito', () => {
    const selection = checkoutSelection('?sku=MON-LG-27&cantidad=2', cart, lookup);
    expect(selection.source).toBe('item');
    expect(selection.items).toEqual([{ sku: 'MON-LG-27', quantity: 2 }]);
    expect(selection.review).toMatchObject({ total: 4098, count: 2, reservable: true });
    expect(selection.review.lines[0].product?.sku).toBe('MON-LG-27');
  });

  it('un artículo que pide más de lo disponible o que no existe queda marcado', () => {
    expect(checkoutSelection('?sku=MON-LG-27&cantidad=5', cart, lookup).review.lines[0]).toMatchObject({ status: 'reduced', suggestedQuantity: 3 });
    expect(checkoutSelection('?sku=NO-EXISTE', cart, lookup).review).toMatchObject({ reservable: false });
    expect(checkoutSelection('?sku=%20', cart, lookup)).toMatchObject({ source: 'item', items: [] });
  });

  it('sin `sku` reserva el carrito', () => {
    expect(isSingleItemCheckout('?cantidad=2')).toBe(false);
    expect(isSingleItemCheckout('?sku=X')).toBe(true);
    const selection = checkoutSelection('', cart, lookupOf(product('JUE-1', { price: 350 })));
    expect(selection).toMatchObject({ source: 'cart', items: [{ sku: 'JUE-1', quantity: 2 }] });
    expect(selection.review.total).toBe(700);
  });
});
