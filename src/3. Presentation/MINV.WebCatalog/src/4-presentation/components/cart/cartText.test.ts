// Textos del carrito: el aviso al agregar (según cuánto entró y por qué no entró más), lo que le pasó a una línea que
// cambió y el resumen de un ajuste.

import { describe, expect, it } from 'vitest';
import type { Product } from '@/1-domain/catalog/types';
import { MOCK_CATALOG } from '@/3-infrastructure/data/mockCatalog';
import { addToCartNotice, adjustmentText, cartCountLabel, lineIssueText, unitsLabel } from './cartText';

const product = { shortName: 'Monitor LG 27"' };
const catalogProduct: Product = MOCK_CATALOG.products[0];

describe('aviso al agregar al carrito', () => {
  it('entró todo: «Agregado al carrito» con cuántas hay y «Ver carrito»', () => {
    expect(addToCartNotice(product, { added: 1, quantity: 1, limit: null })).toEqual({
      tone: 'success',
      title: 'Agregado al carrito',
      description: 'Monitor LG 27" · 1 unidad en tu carrito',
      showCart: true,
    });
    expect(addToCartNotice(product, { added: 2, quantity: 5, limit: null }).description).toBe('Monitor LG 27" · 5 unidades en tu carrito');
  });

  it('entró una parte: dice cuánto y por qué', () => {
    expect(addToCartNotice(product, { added: 1, quantity: 3, limit: 'stock' })).toMatchObject({
      tone: 'info',
      title: 'Agregado al carrito',
      description: 'Monitor LG 27" · Agregamos 1 unidad: es todo lo disponible.',
    });
    expect(addToCartNotice(product, { added: 2, quantity: 16, limit: 'line_limit' }).description).toBe('Monitor LG 27" · Agregamos 2 unidades: el máximo es 16 por producto.');
  });

  it('no entró nada: explica el motivo en palabras simples', () => {
    expect(addToCartNotice(product, { added: 0, quantity: 3, limit: 'stock' })).toMatchObject({ tone: 'info', title: 'Ya tenés todo lo disponible', showCart: true });
    expect(addToCartNotice(product, { added: 0, quantity: 16, limit: 'line_limit' })).toMatchObject({ title: 'Llegaste al máximo por producto' });
    expect(addToCartNotice(product, { added: 0, quantity: 0, limit: 'cart_full' })).toMatchObject({
      tone: 'warning',
      title: 'Tu carrito está lleno',
      description: 'Admite hasta 20 productos distintos. Quitá alguno para agregar otro.',
      showCart: true,
    });
    expect(addToCartNotice(product, { added: 0, quantity: 0, limit: 'unavailable' })).toMatchObject({ tone: 'warning', title: 'Sin unidades disponibles', showCart: false });
    expect(addToCartNotice(product, { added: 0, quantity: 0, limit: 'invalid' })).toMatchObject({ tone: 'danger', showCart: false });
  });
});

describe('líneas que cambiaron', () => {
  it('dice qué pasó con cada una', () => {
    expect(lineIssueText({ status: 'ok', available: 5, quantity: 2, product: catalogProduct })).toBe('');
    expect(lineIssueText({ status: 'reduced', available: 2, quantity: 5, product: catalogProduct })).toBe('Pediste 5 y quedan 2 unidades.');
    expect(lineIssueText({ status: 'reduced', available: 1, quantity: 3, product: catalogProduct })).toBe('Pediste 3 y queda 1 sola unidad.');
    expect(lineIssueText({ status: 'sold_out', available: 0, quantity: 1, product: { ...catalogProduct, stock: 0, reserved: 0 } })).toBe('Se agotó desde que lo agregaste.');
    expect(lineIssueText({ status: 'sold_out', available: 0, quantity: 1, product: { ...catalogProduct, stock: 0, reserved: 3 } })).toBe(
      'Las unidades que quedan están reservadas por otros clientes.',
    );
    expect(lineIssueText({ status: 'unavailable', available: 0, quantity: 1, product: null })).toBe('Este producto ya no está publicado en el catálogo.');
  });

  it('resume un ajuste', () => {
    expect(
      adjustmentText([
        { sku: 'A', name: 'Monitor LG', from: 3, to: 1, reason: 'reduced' },
        { sku: 'B', name: 'SSD Kingston', from: 1, to: 0, reason: 'sold_out' },
      ]),
    ).toBe('Monitor LG: de 3 a 1 · SSD Kingston: lo quitamos');
  });

  it('cuenta en singular y en plural', () => {
    expect(unitsLabel(1)).toBe('1 unidad');
    expect(unitsLabel(4)).toBe('4 unidades');
    expect(cartCountLabel(1)).toBe('1 producto');
    expect(cartCountLabel(12)).toBe('12 productos');
  });
});
