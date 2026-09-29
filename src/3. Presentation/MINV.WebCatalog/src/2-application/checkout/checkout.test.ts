// Aplicación de la reserva V7: la llave por intento (la misma al reintentar por red, otra si cambia algo), las fallas de
// las dos vías en una sola forma, el ajuste a lo disponible, la reserva del carrito por la tienda pública y la reserva
// con la cuenta. Puertos simulados: nada toca la red.

import { describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { IAccountGateway } from '@/1-domain/ports/IAccountGateway';
import type { IReservationGateway } from '@/1-domain/ports/IReservationGateway';
import { StorefrontError } from '@/1-domain/storefront/errors';
import type { Reservation } from '@/1-domain/storefront/types';
import { MOCK_CATALOG } from '@/3-infrastructure/data/mockCatalog';
import { createAccountUseCases } from '../auth/account';
import { reviewCart } from '../cart/review';
import { createReservationUseCases } from '../storefront/reservations';
import { checkoutAdjustments, contentFingerprint, IdempotentAttempt, isNetworkFailure, parseShortages, restrictToFields, toCheckoutFailure } from './index';

const lookup = (sku: string) => MOCK_CATALOG.products.find((product) => product.sku === sku);
const CPU = MOCK_CATALOG.products.find((product) => product.category === 'CPU' && product.stock >= 5)!;

const RESERVATION: Reservation = {
  number: 'RES-WEB-000001',
  kind: 'cart',
  status: 'Reserved',
  statusText: 'Reservada',
  createdAt: new Date('2026-09-28T10:00:00Z'),
  reservedUntil: new Date('2026-09-29T10:00:00Z'),
  total: 100,
  contactName: 'Valentina Aguirre',
  branch: 'CM',
  notes: null,
  hasCompatibilityWarnings: false,
  lines: [],
  cancelReason: null,
  replayed: false,
  mailQueued: true,
};

describe('llave de idempotencia por intento', () => {
  it('conserva la llave si la falla fue de red y estrena otra si cambió el contenido o el servidor respondió', async () => {
    let next = 0;
    const attempt = new IdempotentAttempt(() => `llave-${++next}`);
    const network = new StorefrontError({ kind: 'network', status: 0, detail: 'sin red' });
    const keys: string[] = [];
    const failing = (key: string) => {
      keys.push(key);
      return Promise.reject(network);
    };

    await expect(attempt.run('A', failing)).rejects.toBe(network);
    await expect(attempt.run('A', failing)).rejects.toBe(network);
    expect(keys).toEqual(['llave-1', 'llave-1']);

    // Cambió algo: otra llave (la misma con otro contenido daría 422).
    await expect(attempt.run('B', failing)).rejects.toBe(network);
    expect(keys[2]).toBe('llave-2');

    // El servidor respondió (409): el intento se cierra y el próximo envío estrena llave aunque el contenido sea igual.
    await expect(attempt.run('B', (key) => (keys.push(key), Promise.reject(new StorefrontError({ kind: 'insufficient_stock', status: 409, detail: 'sin stock' }))))).rejects.toBeInstanceOf(StorefrontError);
    expect(keys[3]).toBe('llave-2');
    expect(await attempt.run('B', (key) => Promise.resolve(key))).toBe('llave-3');
    expect(attempt.current()).toBeNull();
  });

  it('reconoce la red caída en las dos vías y la huella no depende del orden de las claves', () => {
    expect(isNetworkFailure(new WebApiError({ kind: 'network', message: 'x' }))).toBe(true);
    expect(isNetworkFailure(new WebApiError({ kind: 'validation', message: 'x' }))).toBe(false);
    expect(isNetworkFailure(new Error('x'))).toBe(false);
    expect(contentFingerprint({ b: 1, a: { d: [1, 2], c: undefined } })).toBe(contentFingerprint({ a: { d: [1, 2] }, b: 1 }));
    expect(contentFingerprint({ a: 1 })).not.toBe(contentFingerprint({ a: 2 }));
  });
});

describe('fallas de la reserva', () => {
  it('409 de la tienda: faltantes con lo que hay', () => {
    const failure = toCheckoutFailure(
      new StorefrontError({ kind: 'insufficient_stock', status: 409, detail: 'x', shortages: [{ sku: 'MON-1', name: 'Monitor', requested: 3, available: 1 }] }),
    );
    expect(failure).toMatchObject({ kind: 'insufficient_stock', shortages: [{ sku: 'MON-1', requested: 3, available: 1 }] });
    expect(failure.title).toContain('stock');
  });

  it('por el RPC el faltante llega en el texto y se lee igual', () => {
    const message = 'No hay stock suficiente para 2 pieza(s): MON-LG-27 (pedido 3, disponible 1); SSD-X_1 (pedido 2, disponible 0)';
    expect(parseShortages(message)).toEqual([
      { sku: 'MON-LG-27', name: 'MON-LG-27', requested: 3, available: 1 },
      { sku: 'SSD-X_1', name: 'SSD-X_1', requested: 2, available: 0 },
    ]);
    const failure = toCheckoutFailure(new WebApiError({ kind: 'domain', status: 422, code: 'storefront.insufficient_stock', message }));
    expect(failure.kind).toBe('insufficient_stock');
    expect(failure.shortages).toHaveLength(2);
  });

  it('400/422: ubica cada mensaje del servidor en su campo cuando puede', () => {
    const validation = toCheckoutFailure(
      new StorefrontError({
        kind: 'validation',
        status: 400,
        detail: 'Datos no válidos',
        errors: ['Las notas van en una sola línea: no admite saltos de línea.', 'Indique el número de documento (CI o NIT) para la factura.', 'Agregue al menos una pieza al armado.'],
      }),
    );
    expect(validation.kind).toBe('invalid');
    expect(validation.fieldErrors).toEqual({
      notes: 'Las notas van en una sola línea: no admite saltos de línea.',
      documentNumber: 'Indique el número de documento (CI o NIT) para la factura.',
    });
    expect(validation.message).toBe('Agregue al menos una pieza al armado.');

    const phone = toCheckoutFailure(new StorefrontError({ kind: 'domain', status: 422, code: 'pcbuild.contact_phone', detail: 'El teléfono debe tener 7 u 8 dígitos.' }));
    expect(phone.fieldErrors).toEqual({ phone: 'El teléfono debe tener 7 u 8 dígitos.' });
    const numeric = toCheckoutFailure(new WebApiError({ kind: 'domain', status: 422, code: 'buyer.doc_numeric', message: 'Con CI o NIT el número solo admite dígitos.' }));
    expect(numeric.fieldErrors).toEqual({ documentNumber: 'Con CI o NIT el número solo admite dígitos.' });
    const general = toCheckoutFailure(new StorefrontError({ kind: 'domain', status: 422, code: 'product.inactive', detail: 'El producto X ya no se vende.' }));
    expect(general).toMatchObject({ kind: 'invalid', fieldErrors: {}, message: 'El producto X ya no se vende.' });
  });

  it('con una cuenta, los errores de campos que no se muestran pasan a los mensajes generales', () => {
    const failure = toCheckoutFailure(
      new WebApiError({ kind: 'validation', status: 400, message: 'Datos no válidos', errors: ['El nombre de contacto no admite saltos de línea.', 'Las notas superan 500 caracteres.'] }),
    );
    expect(Object.keys(failure.fieldErrors).sort()).toEqual(['name', 'notes']);
    const shown = restrictToFields(failure, ['holdDays', 'notes']);
    expect(shown.fieldErrors).toEqual({ notes: 'Las notas superan 500 caracteres.' });
    expect(shown.messages).toContain('El nombre de contacto no admite saltos de línea.');
    const onlyHidden = restrictToFields(toCheckoutFailure(new WebApiError({ kind: 'domain', status: 422, code: 'pcbuild.contact_phone', message: 'Teléfono inválido.' })), ['holdDays', 'notes']);
    expect(onlyHidden).toMatchObject({ fieldErrors: {}, message: 'Teléfono inválido.', messages: [] });
  });

  it('429, red caída, servidor caído, sesión vencida y llave repetida', () => {
    expect(toCheckoutFailure(new StorefrontError({ kind: 'rate_limited', status: 429, detail: 'x' })).kind).toBe('rate_limited');
    expect(toCheckoutFailure(new WebApiError({ kind: 'rate_limited', message: 'x' })).kind).toBe('rate_limited');
    expect(toCheckoutFailure(new StorefrontError({ kind: 'network', status: 0, detail: 'x' })).message).toContain('no se va a duplicar');
    expect(toCheckoutFailure(new StorefrontError({ kind: 'unavailable', status: 503, detail: 'x' })).kind).toBe('unavailable');
    expect(toCheckoutFailure(new WebApiError({ kind: 'server', status: 500, message: 'x' })).kind).toBe('unavailable');
    expect(toCheckoutFailure(new WebApiError({ kind: 'authentication', status: 401, message: 'x' })).kind).toBe('session');
    expect(toCheckoutFailure(new StorefrontError({ kind: 'idempotency', status: 422, detail: 'x' })).kind).toBe('retry');
    expect(toCheckoutFailure(new Error('raro')).kind).toBe('unknown');
  });
});

describe('ajustar a lo disponible', () => {
  it('lo que dijo la tienda manda sobre el catálogo; nunca sube una cantidad', () => {
    const review = reviewCart({ items: [{ sku: CPU.sku, quantity: 3 }] }, lookup);
    expect(review.lines[0].status).toBe('ok');
    expect(checkoutAdjustments(review, [])).toEqual([]);
    expect(checkoutAdjustments(review, [{ sku: CPU.sku, name: CPU.name, requested: 3, available: 1 }])).toEqual([{ sku: CPU.sku, name: CPU.shortName, from: 3, to: 1 }]);
    expect(checkoutAdjustments(review, [{ sku: CPU.sku, name: CPU.name, requested: 3, available: 0 }])[0].to).toBe(0);
    // Sin faltante del servidor, lo sugerido por el catálogo (bajó).
    const reduced = reviewCart({ items: [{ sku: CPU.sku, quantity: CPU.stock + 2 }] }, lookup);
    expect(checkoutAdjustments(reduced, [])).toEqual([{ sku: CPU.sku, name: CPU.shortName, from: CPU.stock + 2, to: CPU.stock }]);
  });
});

describe('reservar un carrito', () => {
  it('por la tienda pública: kind «cart», líneas sin ranura, días, factura y notas en una línea', async () => {
    const gateway: IReservationGateway = { create: vi.fn().mockResolvedValue(RESERVATION), get: vi.fn(), cancel: vi.fn() };
    const useCases = createReservationUseCases(gateway);
    await useCases.reserveCart({
      items: [{ sku: CPU.sku, quantity: 2 }],
      contact: { name: 'Valentina Aguirre', phone: '+591 71234567', email: 'v@correo.example' },
      holdDays: 1,
      buyer: { documentType: 5, documentNumber: '1020304050', name: 'Tech SRL' },
      notes: 'Paso el sábado\npor la mañana',
      idempotencyKey: 'llave-1',
    });
    expect(gateway.create).toHaveBeenCalledWith({
      lines: [{ sku: CPU.sku, quantity: 2 }],
      contact: { name: 'Valentina Aguirre', phone: '+591 71234567', email: 'v@correo.example' },
      notes: 'Paso el sábado por la mañana',
      idempotencyKey: 'llave-1',
      kind: 'cart',
      holdDays: 1,
      buyer: { documentType: 5, documentNumber: '1020304050', name: 'Tech SRL' },
    });
    await expect(useCases.reserveCart({ items: [], contact: { name: 'A', phone: '71234567' }, idempotencyKey: 'k' })).rejects.toMatchObject({ kind: 'validation' });
    await expect(useCases.reserveCart({ items: [{ sku: CPU.sku, quantity: 1 }], contact: { name: 'A', phone: '71234567' }, holdDays: 4, idempotencyKey: 'k' })).rejects.toMatchObject({
      kind: 'validation',
    });
    expect(gateway.create).toHaveBeenCalledTimes(1);
  });

  it('con la cuenta: CreateMyReservation con kind «cart», sin datos de contacto y con el id del pedido', async () => {
    const gateway = {
      account: vi.fn(),
      updateAccount: vi.fn(),
      reservations: vi.fn(),
      cancelReservation: vi.fn(),
      changePassword: vi.fn(),
      createReservation: vi.fn().mockResolvedValue(RESERVATION),
    } satisfies IAccountGateway;
    const account = createAccountUseCases(gateway);
    expect(await account.reserve({ items: [{ sku: CPU.sku, quantity: 1 }], holdDays: 3, notes: 'Una\ndos' }, { requestId: 'r-1' })).toBe(RESERVATION);
    expect(gateway.createReservation).toHaveBeenCalledWith({ lines: [{ sku: CPU.sku, quantity: 1 }], kind: 'cart', holdDays: 3, notes: 'Una dos' }, { requestId: 'r-1' });
    await expect(account.reserve({ items: [{ sku: CPU.sku, quantity: 17 }] })).rejects.toMatchObject({ kind: 'validation' });
  });
});
