// Dominio puro del carrito: líneas por SKU, agregar, cambiar cantidad, quitar, vaciar, topes (16 por producto y 20
// productos distintos), lo disponible como límite y el dinero con aritmética exacta de centavos.

import { describe, expect, it } from 'vitest';
import { RESERVATION_LIMITS } from '@/1-domain/storefront/types';
import {
  CART_LIMITS,
  EMPTY_CART,
  addToCart,
  cartCount,
  cartOf,
  clearCart,
  isInCart,
  maxCartQuantity,
  normalizeQuantity,
  normalizeSku,
  quantityInCart,
  removeFromCart,
  sameCart,
  sanitizeCartItems,
  setCartQuantity,
} from './cart';
import { fromCents, lineCents, lineSubtotal, sumCents, toCents } from './totals';
import type { Cart } from './types';

const monitor = { sku: 'MON-LG-27', stock: 5 };
const game = { sku: 'JUE-PS5-GOW', stock: 40 };
const lastUnit = { sku: 'GPU-RTX-5090', stock: 1 };

function cartWith(...items: [string, number][]): Cart {
  return { items: items.map(([sku, quantity]) => ({ sku, quantity })) };
}

describe('topes del carrito', () => {
  it('son los mismos que la tienda exige al reservar', () => {
    expect(CART_LIMITS.maxQuantityPerLine).toBe(16);
    expect(CART_LIMITS.maxLines).toBe(20);
    expect(CART_LIMITS.maxQuantityPerLine).toBe(RESERVATION_LIMITS.maxQuantityPerLine);
    expect(CART_LIMITS.maxLines).toBe(RESERVATION_LIMITS.maxLines);
  });

  it('lo que admite un producto es lo disponible, con el tope de 16', () => {
    expect(maxCartQuantity({ stock: 5 })).toBe(5);
    expect(maxCartQuantity({ stock: 40 })).toBe(16);
    expect(maxCartQuantity({ stock: 0 })).toBe(0);
    expect(maxCartQuantity({ stock: -3 })).toBe(0);
    expect(maxCartQuantity({ stock: 2.9 })).toBe(2);
    expect(maxCartQuantity({ stock: Number.NaN })).toBe(0);
  });
});

describe('SKU y cantidad', () => {
  it('normaliza el SKU a mayúsculas y rechaza lo que no tiene forma de SKU', () => {
    expect(normalizeSku(' mon-lg-27 ')).toBe('MON-LG-27');
    expect(normalizeSku('CPU_AMD_7600')).toBe('CPU_AMD_7600');
    expect(normalizeSku('')).toBeNull();
    expect(normalizeSku('   ')).toBeNull();
    expect(normalizeSku('-EMPIEZA-MAL')).toBeNull();
    expect(normalizeSku('CON ESPACIO')).toBeNull();
    expect(normalizeSku('<script>')).toBeNull();
    expect(normalizeSku('A'.repeat(41))).toBeNull();
    expect(normalizeSku('A'.repeat(40))).toBe('A'.repeat(40));
    expect(normalizeSku(123)).toBeNull();
    expect(normalizeSku(null)).toBeNull();
  });

  it('la cantidad es un entero de 1 a 16 (0 si no sirve)', () => {
    expect(normalizeQuantity(3)).toBe(3);
    expect(normalizeQuantity(3.9)).toBe(3);
    expect(normalizeQuantity(99)).toBe(16);
    expect(normalizeQuantity(0)).toBe(0);
    expect(normalizeQuantity(-2)).toBe(0);
    expect(normalizeQuantity('3')).toBe(0);
    expect(normalizeQuantity(Number.POSITIVE_INFINITY)).toBe(0);
    expect(normalizeQuantity(Number.NaN)).toBe(0);
  });
});

describe('agregar al carrito', () => {
  it('agrega una línea nueva y no modifica el carrito que recibe', () => {
    const result = addToCart(EMPTY_CART, monitor);
    expect(result).toMatchObject({ added: 1, quantity: 1, limit: null });
    expect(result.cart.items).toEqual([{ sku: 'MON-LG-27', quantity: 1 }]);
    expect(EMPTY_CART.items).toEqual([]);
  });

  it('un mismo SKU suma a su línea y conserva el orden en que se agregó', () => {
    let cart = addToCart(EMPTY_CART, monitor, 2).cart;
    cart = addToCart(cart, game, 1).cart;
    const result = addToCart(cart, monitor, 2);
    expect(result).toMatchObject({ added: 2, quantity: 4, limit: null });
    expect(result.cart.items).toEqual([
      { sku: 'MON-LG-27', quantity: 4 },
      { sku: 'JUE-PS5-GOW', quantity: 1 },
    ]);
  });

  it('nunca supera lo disponible: entra lo que queda y avisa por qué', () => {
    const first = addToCart(EMPTY_CART, monitor, 4);
    const second = addToCart(first.cart, monitor, 3);
    expect(second).toMatchObject({ added: 1, quantity: 5, limit: 'stock' });
    const third = addToCart(second.cart, monitor, 1);
    expect(third).toMatchObject({ added: 0, quantity: 5, limit: 'stock' });
    expect(third.cart).toBe(second.cart);
  });

  it('nunca supera 16 unidades de un mismo producto aunque haya más disponibles', () => {
    const first = addToCart(EMPTY_CART, game, 15);
    expect(first).toMatchObject({ added: 15, quantity: 15, limit: null });
    const second = addToCart(first.cart, game, 5);
    expect(second).toMatchObject({ added: 1, quantity: 16, limit: 'line_limit' });
    expect(addToCart(second.cart, game)).toMatchObject({ added: 0, quantity: 16, limit: 'line_limit' });
    expect(addToCart(EMPTY_CART, game, 500)).toMatchObject({ added: 16, quantity: 16, limit: 'line_limit' });
  });

  it('un producto sin disponible no entra', () => {
    const result = addToCart(EMPTY_CART, { sku: 'CON-PS5-PRO', stock: 0 });
    expect(result).toMatchObject({ added: 0, quantity: 0, limit: 'unavailable' });
    expect(result.cart).toBe(EMPTY_CART);
  });

  it('no admite más de 20 productos distintos, pero sí sumar a uno que ya está', () => {
    let cart: Cart = EMPTY_CART;
    for (let index = 1; index <= 20; index += 1) cart = addToCart(cart, { sku: `SKU-${index}`, stock: 9 }).cart;
    expect(cart.items).toHaveLength(20);
    const rejected = addToCart(cart, { sku: 'SKU-21', stock: 9 });
    expect(rejected).toMatchObject({ added: 0, quantity: 0, limit: 'cart_full' });
    expect(rejected.cart).toBe(cart);
    expect(addToCart(cart, { sku: 'SKU-7', stock: 9 }, 2)).toMatchObject({ added: 2, quantity: 3, limit: null });
  });

  it('una cantidad que no sirve vale 1 y un SKU que no sirve no entra', () => {
    expect(addToCart(EMPTY_CART, monitor, Number.NaN)).toMatchObject({ added: 1, quantity: 1 });
    expect(addToCart(EMPTY_CART, monitor, -4)).toMatchObject({ added: 1, quantity: 1 });
    expect(addToCart(EMPTY_CART, monitor, 2.8)).toMatchObject({ added: 2, quantity: 2 });
    expect(addToCart(EMPTY_CART, { sku: 'con espacio', stock: 3 })).toMatchObject({ added: 0, limit: 'invalid' });
  });

  it('guarda el SKU normalizado', () => {
    expect(addToCart(EMPTY_CART, { sku: ' mon-lg-27 ', stock: 3 }).cart.items).toEqual([{ sku: 'MON-LG-27', quantity: 1 }]);
  });
});

describe('cambiar cantidad, quitar y vaciar', () => {
  const cart = cartWith(['MON-LG-27', 2], ['JUE-PS5-GOW', 1]);

  it('cambia la cantidad de una línea sin tocar las demás', () => {
    expect(setCartQuantity(cart, 'MON-LG-27', 4).items).toEqual([
      { sku: 'MON-LG-27', quantity: 4 },
      { sku: 'JUE-PS5-GOW', quantity: 1 },
    ]);
    expect(cart.items[0].quantity).toBe(2);
  });

  it('acota la cantidad a 16 y, con el producto, a lo disponible', () => {
    expect(quantityInCart(setCartQuantity(cart, 'JUE-PS5-GOW', 50), 'JUE-PS5-GOW')).toBe(16);
    expect(quantityInCart(setCartQuantity(cart, 'MON-LG-27', 9, monitor), 'MON-LG-27')).toBe(5);
    expect(quantityInCart(setCartQuantity(cart, 'MON-LG-27', 9, lastUnit), 'MON-LG-27')).toBe(1);
  });

  it('con 0, con un negativo o sin nada disponible quita la línea', () => {
    expect(setCartQuantity(cart, 'MON-LG-27', 0).items).toEqual([{ sku: 'JUE-PS5-GOW', quantity: 1 }]);
    expect(setCartQuantity(cart, 'MON-LG-27', -1).items).toEqual([{ sku: 'JUE-PS5-GOW', quantity: 1 }]);
    expect(setCartQuantity(cart, 'MON-LG-27', 2, { stock: 0 }).items).toEqual([{ sku: 'JUE-PS5-GOW', quantity: 1 }]);
  });

  it('si nada cambia devuelve el mismo carrito (y no agrega un SKU que no está)', () => {
    expect(setCartQuantity(cart, 'MON-LG-27', 2)).toBe(cart);
    expect(setCartQuantity(cart, 'NO-ESTA', 3)).toBe(cart);
    expect(setCartQuantity(cart, 'MON-LG-27', Number.NaN)).toBe(cart);
    expect(removeFromCart(cart, 'NO-ESTA')).toBe(cart);
    expect(clearCart(EMPTY_CART)).toBe(EMPTY_CART);
  });

  it('quita una línea y vacía el carrito', () => {
    expect(removeFromCart(cart, 'MON-LG-27').items).toEqual([{ sku: 'JUE-PS5-GOW', quantity: 1 }]);
    expect(removeFromCart(cartWith(['MON-LG-27', 2]), 'MON-LG-27')).toBe(EMPTY_CART);
    expect(clearCart(cart)).toBe(EMPTY_CART);
  });

  it('cuenta las unidades y sabe qué hay en el carrito', () => {
    expect(cartCount(cart)).toBe(3);
    expect(cartCount(EMPTY_CART)).toBe(0);
    expect(isInCart(cart, 'JUE-PS5-GOW')).toBe(true);
    expect(isInCart(cart, 'NO-ESTA')).toBe(false);
    expect(quantityInCart(cart, 'NO-ESTA')).toBe(0);
  });

  it('compara carritos por sus líneas, orden y cantidades', () => {
    expect(sameCart(cart, cartWith(['MON-LG-27', 2], ['JUE-PS5-GOW', 1]))).toBe(true);
    expect(sameCart(cart, cartWith(['JUE-PS5-GOW', 1], ['MON-LG-27', 2]))).toBe(false);
    expect(sameCart(cart, cartWith(['MON-LG-27', 3], ['JUE-PS5-GOW', 1]))).toBe(false);
    expect(sameCart(cart, cartWith(['MON-LG-27', 2]))).toBe(false);
  });
});

describe('líneas de origen dudoso', () => {
  it('descarta lo que no sirve, une los SKU repetidos y respeta los topes', () => {
    expect(
      sanitizeCartItems([
        { sku: 'mon-lg-27', quantity: 2 },
        { sku: 'MON-LG-27', quantity: 15 },
        { sku: 'JUE-PS5-GOW', quantity: '3' },
        { sku: '', quantity: 1 },
        { sku: 'CON ESPACIO', quantity: 1 },
        { sku: 'TEC-LOG-G915', quantity: 0 },
        { sku: 'RAT-RZR-V3', quantity: 2.7, price: 1, name: 'de más' },
        null,
        'texto',
        42,
        ['SKU', 1],
      ]),
    ).toEqual([
      { sku: 'MON-LG-27', quantity: 16 },
      { sku: 'RAT-RZR-V3', quantity: 2 },
    ]);
  });

  it('lo que no es una lista da un carrito vacío', () => {
    for (const raw of [null, undefined, 'texto', 7, {}, { items: [] }]) expect(sanitizeCartItems(raw)).toEqual([]);
    expect(cartOf(null)).toBe(EMPTY_CART);
  });

  it('se queda con los primeros 20 productos distintos', () => {
    const raw = Array.from({ length: 30 }, (_, index) => ({ sku: `SKU-${index + 1}`, quantity: 1 }));
    const items = sanitizeCartItems(raw);
    expect(items).toHaveLength(20);
    expect(items[19]).toEqual({ sku: 'SKU-20', quantity: 1 });
  });
});

describe('dinero con aritmética exacta de centavos', () => {
  it('pasa de bolivianos a centavos enteros y vuelve', () => {
    expect(toCents(1099.99)).toBe(109999);
    expect(toCents(0.1)).toBe(10);
    expect(toCents(2049)).toBe(204900);
    expect(toCents(19.999)).toBe(2000);
    expect(toCents(Number.NaN)).toBe(0);
    expect(fromCents(109999)).toBe(1099.99);
  });

  it('el subtotal de una línea no arrastra errores de coma flotante', () => {
    expect(0.1 * 3).not.toBe(0.3);
    expect(lineSubtotal(0.1, 3)).toBe(0.3);
    expect(lineSubtotal(1099.99, 16)).toBe(17599.84);
    expect(lineCents(33.33, 3)).toBe(9999);
    expect(lineSubtotal(79.9, 0)).toBe(0);
  });

  it('el total es la suma exacta de los subtotales', () => {
    const cents = [lineCents(0.1, 1), lineCents(0.2, 1), lineCents(1099.99, 2), lineCents(4.35, 3)];
    expect(0.1 + 0.2).not.toBe(0.3);
    expect(sumCents(cents)).toBe(10 + 20 + 219998 + 1305);
    expect(fromCents(sumCents(cents))).toBe(2213.33);
  });
});
