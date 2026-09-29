// V7 · Modo mock de la reserva del carrito: la pasarela en memoria respeta el contrato nuevo (RES-WEB-n, líneas sin
// ranura, días para recoger, factura con las reglas del SIN, texto libre sin caracteres de control y `mailQueued`) y la
// cuenta del cliente del modo mock reserva sobre el MISMO stock.

import { describe, expect, it } from 'vitest';
import type { ReservationRequest } from '@/1-domain/storefront/types';
import { InMemoryReservationGateway, MOCK_CATALOG } from './mockCatalog';
import { DEMO_USERS, InMemoryWebBackend } from './mockWeb';

const product = MOCK_CATALOG.products.find((item) => item.category === 'CPU' && item.stock >= 5)!;
const NOW = new Date('2026-09-28T12:00:00Z');

function cart(changes: Partial<ReservationRequest> = {}): ReservationRequest {
  return {
    lines: [{ sku: product.sku, quantity: 1 }],
    contact: { name: 'Ana Pérez', phone: '+591 71234567', email: 'ana@correo.example' },
    idempotencyKey: 'k1',
    kind: 'cart',
    holdDays: 1,
    ...changes,
  };
}

describe('InMemoryReservationGateway · carrito', () => {
  it('numera los carritos aparte (RES-WEB-n), sin ranura, con los días pedidos y el correo en cola', async () => {
    const gateway = new InMemoryReservationGateway(MOCK_CATALOG, () => NOW);
    const reservation = await gateway.create(cart());
    expect(reservation).toMatchObject({ number: 'RES-WEB-000001', kind: 'cart', mailQueued: true, status: 'Reserved' });
    expect(reservation.lines[0].slot).toBe('');
    expect(reservation.reservedUntil.toISOString()).toBe('2026-09-29T12:00:00.000Z');

    const noMail = await gateway.create(cart({ idempotencyKey: 'k2', contact: { name: 'Ana', phone: '71234567' }, holdDays: 3 }));
    expect(noMail).toMatchObject({ number: 'RES-WEB-000002', mailQueued: false });
    expect(noMail.reservedUntil.toISOString()).toBe('2026-10-01T12:00:00.000Z');
    // Sin días: las horas configuradas (48). Los armados siguen con su propia numeración.
    const build = await gateway.create({ ...cart({ idempotencyKey: 'k3', kind: undefined, holdDays: undefined }), lines: [{ sku: product.sku, quantity: 1, slot: 'cpu' }] });
    expect(build).toMatchObject({ number: 'ARM-WEB-000001', kind: 'build' });
    expect(build.reservedUntil.getTime() - build.createdAt.getTime()).toBe(48 * 3_600_000);
  });

  it('rechaza saltos de línea en medio de las notas o del nombre (400) y valida la factura como el servidor', async () => {
    const gateway = new InMemoryReservationGateway(MOCK_CATALOG, () => NOW);
    await expect(gateway.create(cart({ notes: 'Uno\ndos' }))).rejects.toMatchObject({ kind: 'validation', status: 400, errors: [expect.stringContaining('una sola línea')] });
    await expect(gateway.create(cart({ idempotencyKey: 'k2', notes: '  Paso mañana \n' }))).resolves.toMatchObject({ notes: 'Paso mañana' });
    await expect(gateway.create(cart({ idempotencyKey: 'k3', contact: { name: 'Ana\tPérez', phone: '71234567' } }))).rejects.toMatchObject({ kind: 'validation' });
    await expect(gateway.create(cart({ idempotencyKey: 'k4', holdDays: 4 }))).rejects.toMatchObject({ kind: 'validation' });
    await expect(gateway.create(cart({ idempotencyKey: 'k5', buyer: { documentType: 5, documentNumber: '12AB' } }))).rejects.toMatchObject({ kind: 'domain', code: 'buyer.doc_numeric' });
    await expect(gateway.create(cart({ idempotencyKey: 'k6', buyer: { documentType: 5, documentNumber: '1020304050', complement: '1A' } }))).rejects.toMatchObject({
      kind: 'domain',
      code: 'buyer.complement',
    });
    await expect(gateway.create(cart({ idempotencyKey: 'k7', buyer: { documentType: 1, documentNumber: '1234567', complement: '1A', name: 'Ana Pérez' } }))).resolves.toMatchObject({
      kind: 'cart',
    });
  });

  it('respeta el tope de días que publica la tienda', async () => {
    const gateway = new InMemoryReservationGateway({ ...MOCK_CATALOG, reservationPolicy: { reservationHours: 24, maxHoldDays: 1 } }, () => NOW);
    await expect(gateway.create(cart({ holdDays: 2 }))).rejects.toMatchObject({ kind: 'domain', code: 'storefront.hold_days' });
  });

  it('la misma llave con otro contenido (otros días) se rechaza; con el mismo contenido devuelve la misma reserva', async () => {
    const gateway = new InMemoryReservationGateway(MOCK_CATALOG, () => NOW);
    const first = await gateway.create(cart());
    expect((await gateway.create(cart())).number).toBe(first.number);
    await expect(gateway.create(cart({ holdDays: 2 }))).rejects.toMatchObject({ kind: 'idempotency' });
  });
});

describe('cuenta del cliente del modo mock sobre el mismo stock', () => {
  it('reserva con la cuenta descuenta el stock compartido, responde el faltante como el servidor y lo devuelve al liberar', async () => {
    const gateway = new InMemoryReservationGateway(MOCK_CATALOG, () => NOW);
    const backend = new InMemoryWebBackend({ products: MOCK_CATALOG.products, stock: gateway, now: () => NOW });
    const customer = DEMO_USERS.find((user) => user.kind === 'customer')!;
    await backend.session.login({ email: customer.email, password: customer.password });

    const reservation = await backend.rpc.send('CreateMyReservationCommand', { lines: [{ sku: product.sku, quantity: 2 }], kind: 'cart', holdDays: 1 });
    expect(reservation).toMatchObject({ kind: 'cart', mailQueued: true });
    expect(gateway.product(product.sku)).toMatchObject({ stock: product.stock - 2, reserved: 2 });

    await expect(backend.rpc.send('CreateMyReservationCommand', { lines: [{ sku: product.sku, quantity: product.stock }], kind: 'cart' })).rejects.toMatchObject({
      kind: 'domain',
      code: 'storefront.insufficient_stock',
      message: expect.stringContaining(`${product.sku} (pedido ${product.stock}, disponible ${product.stock - 2})`),
    });
    await expect(backend.rpc.send('CreateMyReservationCommand', { lines: [{ sku: product.sku, quantity: 1 }], kind: 'cart', notes: 'Uno\ndos' })).rejects.toMatchObject({
      kind: 'validation',
    });

    await backend.rpc.send('CancelMyReservationCommand', { number: reservation.number });
    expect(gateway.product(product.sku)).toMatchObject({ stock: product.stock, reserved: 0 });
  });
});
