// V7 · Reserva del carrito por HTTP con `fetch` simulado: el cuerpo de `POST /storefront/v1/reservations` con `kind`,
// `holdDays`, `buyer`, líneas sin ranura y notas en una línea; los plazos del catálogo; `kind` y `mailQueued` en la
// respuesta; y la reserva con la cuenta por RPC (`CreateMyReservationCommand`). Nada sale a la red.

import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest';
import { CATALOG_DTO, RESERVATION_DTO } from '@/2-application/storefront/fixtures';
import { toCatalogSnapshot, toReservation, toReservationRequestDto } from '@/2-application/storefront/mappers';
import { StorefrontApi } from './api';
import { HttpCatalogSource } from './HttpCatalogSource';
import { HttpReservationGateway } from './HttpReservationGateway';
import { HttpRpcGateway } from './HttpRpcGateway';
import { RpcAccountGateway } from './RpcAccountGateway';
import { WebApi } from './webApi';

const BASE = 'http://localhost:5291';

function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json; charset=utf-8', ...headers } });
}

let fetchMock: Mock;

beforeEach(() => {
  fetchMock = vi.fn();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function lastBody(): unknown {
  const [, init] = fetchMock.mock.calls.at(-1) as [string, RequestInit];
  return JSON.parse(String(init.body));
}

const CART_VIEW = {
  ...RESERVATION_DTO,
  number: 'RES-WEB-000001',
  kind: 'cart',
  mailQueued: true,
  lines: RESERVATION_DTO.lines.map((line) => ({ ...line, slot: null })),
};

describe('contrato V7 de la reserva', () => {
  it('un carrito viaja con kind, días, factura, líneas sin ranura y las notas en una línea', async () => {
    fetchMock.mockResolvedValue(json(CART_VIEW, 201));
    const reservation = await new HttpReservationGateway(new StorefrontApi(BASE)).create({
      lines: [{ sku: 'CPU-AMD-7600', quantity: 1 }, { sku: 'CASE-COR-4000D', quantity: 2, slot: null }],
      contact: { name: 'Valentina Aguirre', phone: '+591 71234567', email: 'v@correo.example' },
      notes: 'Paso el sábado\r\npor la mañana',
      idempotencyKey: '4f6a0c2e-8d1b-4c3e-9a7f-5b2d1e0c9a8b',
      kind: 'cart',
      holdDays: 3,
      buyer: { documentType: 1, documentNumber: ' 1234567 ', complement: '1a', name: 'Valentina\nAguirre' },
    });
    expect(lastBody()).toEqual({
      lines: [
        { sku: 'CPU-AMD-7600', quantity: 1 },
        { sku: 'CASE-COR-4000D', quantity: 2 },
      ],
      contact: { name: 'Valentina Aguirre', phone: '+591 71234567', email: 'v@correo.example' },
      notes: 'Paso el sábado por la mañana',
      kind: 'cart',
      holdDays: 3,
      buyer: { documentType: 1, documentNumber: '1234567', complement: '1A', name: 'Valentina Aguirre' },
    });
    expect(reservation).toMatchObject({ number: 'RES-WEB-000001', kind: 'cart', mailQueued: true });
    expect(reservation.lines.every((line) => line.slot === '')).toBe(true);
  });

  it('un armado sin los campos nuevos manda el mismo cuerpo de la V6 (sin kind ni días) y el complemento solo viaja con CI', () => {
    const body = toReservationRequestDto({
      lines: [{ sku: 'CPU-AMD-7600', quantity: 1, slot: 'cpu' }],
      contact: { name: 'Ana', phone: '71234567' },
      notes: 'Una\ndos',
      idempotencyKey: 'k',
      buyer: { documentType: 5, documentNumber: '1020304050', complement: '1A' },
    });
    expect(body).toEqual({
      lines: [{ sku: 'CPU-AMD-7600', quantity: 1, slot: 'cpu' }],
      contact: { name: 'Ana', phone: '71234567' },
      notes: 'Una dos',
      buyer: { documentType: 5, documentNumber: '1020304050' },
    });
    expect('kind' in body).toBe(false);
    expect('holdDays' in body).toBe(false);
  });

  it('la respuesta trae el tipo (o se deduce del número) y si se encoló el correo', () => {
    expect(toReservation(RESERVATION_DTO)).toMatchObject({ kind: 'build', mailQueued: false });
    expect(toReservation({ ...RESERVATION_DTO, number: 'RES-WEB-000009' })).toMatchObject({ kind: 'cart' });
    expect(toReservation({ ...RESERVATION_DTO, kind: 'cart', mailQueued: true })).toMatchObject({ kind: 'cart', mailQueued: true });
  });

  it('el catálogo trae las horas de la reserva y los días que se pueden pedir (48 h y 3 días si no vienen)', async () => {
    expect(toCatalogSnapshot(CATALOG_DTO, BASE).reservationPolicy).toEqual({ reservationHours: 48, maxHoldDays: 3 });
    fetchMock.mockResolvedValue(json({ ...CATALOG_DTO, reservationHours: 24, maxHoldDays: 1 }));
    const snapshot = await new HttpCatalogSource(new StorefrontApi(BASE)).load();
    expect(snapshot.reservationPolicy).toEqual({ reservationHours: 24, maxHoldDays: 1 });
  });
});

describe('reserva con la cuenta por RPC', () => {
  it('envía CreateMyReservationCommand con el id del pedido, kind «cart» y sin ranura; devuelve la reserva del dominio', async () => {
    fetchMock.mockResolvedValue(json({ ok: true, result: CART_VIEW, replayed: false }));
    const gateway = new RpcAccountGateway(new HttpRpcGateway(new WebApi()));
    const reservation = await gateway.createReservation({ lines: [{ sku: 'CPU-AMD-7600', quantity: 2 }], kind: 'cart', holdDays: 1, notes: 'Paso mañana' }, { requestId: 'r-1' });
    expect(lastBody()).toEqual({
      requestId: 'r-1',
      type: 'MINV.Application.Accounts.CreateMyReservationCommand',
      payload: { lines: [{ sku: 'CPU-AMD-7600', quantity: 2 }], kind: 'cart', holdDays: 1, notes: 'Paso mañana' },
    });
    expect(reservation).toMatchObject({ number: 'RES-WEB-000001', kind: 'cart', mailQueued: true });
  });

  it('el faltante de stock llega como error del dominio con el código de la tienda', async () => {
    fetchMock.mockResolvedValue(
      json(
        {
          ok: false,
          result: null,
          error: { kind: 'domain', message: 'No hay stock suficiente para 1 pieza(s): CPU-AMD-7600 (pedido 2, disponible 1)', errors: null, code: 'storefront.insufficient_stock' },
        },
        422,
      ),
    );
    const gateway = new RpcAccountGateway(new HttpRpcGateway(new WebApi()));
    await expect(gateway.createReservation({ lines: [{ sku: 'CPU-AMD-7600', quantity: 2 }], kind: 'cart' }, { requestId: 'r-2' })).rejects.toMatchObject({
      kind: 'domain',
      code: 'storefront.insufficient_stock',
    });
  });
});
