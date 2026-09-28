// La pasarela de reservas en memoria (modo mock y pruebas) respeta el contrato: todo o nada, número ARM-WEB-n, disponible
// que baja y vuelve, idempotencia por llave y consulta/cancelación con el teléfono.

import { describe, expect, it } from 'vitest';
import type { ReservationRequest } from '@/1-domain/storefront/types';
import { InMemoryReservationGateway, MOCK_CATALOG, MockCatalogSource } from './mockCatalog';

/** Un procesador con varias unidades disponibles (el mock deja algunos con 1). */
const cpu = MOCK_CATALOG.products.find((product) => product.category === 'CPU' && product.stock >= 5)!;

function request(quantity = 1, key = 'k1'): ReservationRequest {
  return { lines: [{ sku: cpu.sku, quantity, slot: 'cpu' }], contact: { name: 'Ana Pérez', phone: '+591 71234567' }, idempotencyKey: key };
}

describe('InMemoryReservationGateway', () => {
  it('reserva, numera, descuenta lo disponible y suma lo reservado; la misma llave devuelve la misma reserva', async () => {
    const gateway = new InMemoryReservationGateway(MOCK_CATALOG, () => new Date('2026-09-27T12:00:00Z'));
    const reservation = await gateway.create(request(2));
    expect(reservation.number).toBe('ARM-WEB-000001');
    expect(reservation.status).toBe('Reserved');
    expect(reservation.reservedUntil.toISOString()).toBe('2026-09-29T12:00:00.000Z');
    expect(reservation.total).toBe(cpu.price * 2);
    expect(reservation.branch).toBe('CM');
    expect(gateway.product(cpu.sku)).toMatchObject({ stock: cpu.stock - 2, reserved: 2 });

    const replayed = await gateway.create(request(2));
    expect(replayed.number).toBe('ARM-WEB-000001');
    expect(replayed.replayed).toBe(true);
    await expect(gateway.create({ ...request(3), idempotencyKey: 'k1' })).rejects.toMatchObject({ kind: 'idempotency', status: 422 });
    const second = await gateway.create(request(1, 'k2'));
    expect(second.number).toBe('ARM-WEB-000002');
  });

  it('sin stock suficiente no reserva nada (409 con faltantes) y rechaza teléfonos no bolivianos y datos incompletos', async () => {
    const gateway = new InMemoryReservationGateway(MOCK_CATALOG);
    await expect(gateway.create(request(cpu.stock + 1))).rejects.toMatchObject({
      kind: 'insufficient_stock',
      status: 409,
      shortages: [{ sku: cpu.sku, requested: cpu.stock + 1, available: cpu.stock }],
    });
    expect(gateway.product(cpu.sku)?.stock).toBe(cpu.stock);
    await expect(gateway.create({ ...request(1, 'k3'), contact: { name: 'Ana', phone: '+1 555 1234567' } })).rejects.toMatchObject({ kind: 'domain', code: 'pcbuild.contact_phone' });
    await expect(gateway.create({ ...request(1, 'k4'), lines: [], contact: { name: ' ', phone: '71234567' } })).rejects.toMatchObject({
      kind: 'validation',
      errors: ['Agregue al menos una pieza al armado.', 'Indique el nombre de quien reserva.'],
    });
    await expect(gateway.create({ ...request(1, 'k5'), lines: [{ sku: 'NO-EXISTE', quantity: 1, slot: 'cpu' }] })).rejects.toMatchObject({ kind: 'domain', code: 'product.inactive' });
  });

  it('consulta solo con el teléfono correcto; cancelar devuelve el stock y no se puede cancelar dos veces', async () => {
    const gateway = new InMemoryReservationGateway(MOCK_CATALOG);
    const reservation = await gateway.create(request(1));
    await expect(gateway.get(reservation.number, '70000000')).rejects.toMatchObject({ kind: 'not_found', status: 404 });
    await expect(gateway.get('ARM-WEB-999999', '71234567')).rejects.toMatchObject({ kind: 'not_found' });
    expect((await gateway.get(' arm-web-000001 ', '591 7123 4567')).status).toBe('Reserved');

    const cancelled = await gateway.cancel(reservation.number, '71234567');
    expect(cancelled).toMatchObject({ status: 'Cancelled', statusText: 'Cancelada', cancelReason: 'Cancelada por el cliente desde la tienda web' });
    expect(gateway.product(cpu.sku)).toMatchObject({ stock: cpu.stock, reserved: 0 });
    await expect(gateway.cancel(reservation.number, '71234567')).rejects.toMatchObject({ kind: 'domain', code: 'pcbuild.state' });
  });

  it('una reserva vencida se consulta como «Vencida»', async () => {
    let now = new Date('2026-09-27T12:00:00Z');
    const gateway = new InMemoryReservationGateway(MOCK_CATALOG, () => now);
    const reservation = await gateway.create(request(1));
    now = new Date('2026-09-30T12:00:00Z');
    const expired = await gateway.get(reservation.number, '71234567');
    expect(expired).toMatchObject({ status: 'Expired', statusText: 'Vencida', cancelReason: 'Vencida' });
  });

  it('la fuente del mock responde la instantánea completa y la ficha por slug o SKU', async () => {
    const source = new MockCatalogSource();
    const snapshot = await source.load();
    expect(snapshot.products).toHaveLength(MOCK_CATALOG.products.length);
    expect(snapshot.store.company.code).toBe('TECHZONE');
    expect(snapshot.presets).toHaveLength(6);
    expect((await source.product('cpu-amd-7600'))?.sku).toBe('CPU-AMD-7600');
    expect((await source.product('CPU-AMD-7600'))?.sku).toBe('CPU-AMD-7600');
    expect(await source.product('nada')).toBeUndefined();
    expect(await source.presets()).toHaveLength(6);
  });
});
