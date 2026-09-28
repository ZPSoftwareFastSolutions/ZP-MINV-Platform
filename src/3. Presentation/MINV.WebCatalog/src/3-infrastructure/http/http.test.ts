// Adaptador HTTP de la tienda con `fetch` simulado: éxito, 404, 409 con faltantes, 429 sin cuerpo, red caída, cabeceras
// de idempotencia y codificación del teléfono en la URL.

import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest';
import { CATALOG_DTO, CASE_DTO, INSUFFICIENT_STOCK_PROBLEM, RESERVATION_DTO } from '@/2-application/storefront/fixtures';
import { isMockApiUrl, resolveApiUrl, StorefrontApi } from './api';
import { HttpCatalogSource } from './HttpCatalogSource';
import { HttpReservationGateway } from './HttpReservationGateway';

const BASE = 'http://localhost:5291';

function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json; charset=utf-8', ...headers } });
}

function problem(body: unknown, status: number): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/problem+json' } });
}

let fetchMock: Mock;

beforeEach(() => {
  fetchMock = vi.fn();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function lastRequest(): { url: string; init: RequestInit } {
  const [url, init] = fetchMock.mock.calls.at(-1) as [string, RequestInit];
  return { url, init };
}

describe('StorefrontApi', () => {
  it('resuelve la base: vacía → gateway local; «mock» es el modo sin red; sin barra final', () => {
    expect(resolveApiUrl(undefined)).toBe('http://localhost:5090');
    expect(resolveApiUrl('  ')).toBe('http://localhost:5090');
    expect(resolveApiUrl('http://api.example/')).toBe('http://api.example');
    expect(isMockApiUrl('mock')).toBe(true);
    expect(isMockApiUrl(' MOCK ')).toBe(true);
    expect(isMockApiUrl('http://localhost:5090')).toBe(false);
    expect(new StorefrontApi(BASE).url('/catalog')).toBe(`${BASE}/storefront/v1/catalog`);
  });

  it('«/» es el mismo origen: rutas relativas (tienda pública detrás del nginx del catálogo)', () => {
    expect(resolveApiUrl('/')).toBe('');
    expect(new StorefrontApi('/').url('/catalog')).toBe('/storefront/v1/catalog');
    expect(new StorefrontApi('/').url('products/gpu/image')).toBe('/storefront/v1/products/gpu/image');
  });

  it('sin respuesta del servidor lanza un error de red', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    await expect(new StorefrontApi(BASE).get('/catalog')).rejects.toMatchObject({ kind: 'network', status: 0 });
  });

  it('un 429 sin cuerpo se traduce a «límite superado»', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 429 }));
    await expect(new StorefrontApi(BASE).get('/catalog')).rejects.toMatchObject({ kind: 'rate_limited', status: 429 });
  });

  it('un 503 con problem+json se traduce a «tienda no disponible»', async () => {
    fetchMock.mockResolvedValue(problem({ title: 'Tienda web no disponible', status: 503, detail: 'La tienda no está configurada.' }, 503));
    await expect(new StorefrontApi(BASE).get('/catalog')).rejects.toMatchObject({ kind: 'unavailable', detail: 'La tienda no está configurada.' });
  });
});

describe('HttpCatalogSource', () => {
  it('carga la instantánea desde /storefront/v1/catalog con imágenes absolutas hacia la API', async () => {
    fetchMock.mockResolvedValue(json(CATALOG_DTO));
    const snapshot = await new HttpCatalogSource(new StorefrontApi(BASE)).load();
    expect(lastRequest().url).toBe(`${BASE}/storefront/v1/catalog`);
    expect(lastRequest().init.method).toBe('GET');
    expect(snapshot.products[0].image).toBe(`${BASE}/storefront/v1/products/CASE-COR-4000D/image`);
    expect(snapshot.store.branch.code).toBe('CM');
    expect(snapshot.presets[0].lines).toHaveLength(2);
  });

  it('consulta la ficha fresca por slug (codificado) y devuelve undefined si ya no está publicada', async () => {
    const source = new HttpCatalogSource(new StorefrontApi(BASE));
    fetchMock.mockResolvedValueOnce(json({ ...CASE_DTO, available: 3, reserved: 1 }));
    const fresh = await source.product('case-cor-4000d');
    expect(lastRequest().url).toBe(`${BASE}/storefront/v1/products/case-cor-4000d`);
    expect(fresh).toMatchObject({ sku: 'CASE-COR-4000D', stock: 3, reserved: 1 });

    fetchMock.mockResolvedValueOnce(problem({ title: 'not_found', status: 404, detail: 'El producto no-existe no está en el catálogo.' }, 404));
    expect(await source.product('no existe')).toBeUndefined();
    expect(lastRequest().url).toBe(`${BASE}/storefront/v1/products/no%20existe`);

    fetchMock.mockResolvedValueOnce(new Response(null, { status: 429 }));
    await expect(source.product('case-cor-4000d')).rejects.toMatchObject({ kind: 'rate_limited' });
  });

  it('trae los armados publicados', async () => {
    fetchMock.mockResolvedValue(json(CATALOG_DTO.presets));
    const presets = await new HttpCatalogSource(new StorefrontApi(BASE)).presets();
    expect(lastRequest().url).toBe(`${BASE}/storefront/v1/presets`);
    expect(presets.map((preset) => preset.id)).toEqual(['arm-cm-000001']);
  });
});

describe('HttpReservationGateway', () => {
  const request = {
    lines: [{ sku: 'CPU-AMD-7600', quantity: 1, slot: 'cpu' as const }],
    contact: { name: 'Valentina Aguirre', phone: '+591 71234567' },
    notes: 'Paso el sabado por la manana',
    idempotencyKey: '4f6a0c2e-8d1b-4c3e-9a7f-5b2d1e0c9a8b',
  };

  it('crea la reserva con la cabecera Idempotency-Key y el cuerpo del contrato (201)', async () => {
    fetchMock.mockResolvedValue(json(RESERVATION_DTO, 201, { Location: '/storefront/v1/reservations/ARM-WEB-000004' }));
    const reservation = await new HttpReservationGateway(new StorefrontApi(BASE)).create(request);
    const { url, init } = lastRequest();
    expect(url).toBe(`${BASE}/storefront/v1/reservations`);
    expect(init.method).toBe('POST');
    expect(init.headers).toMatchObject({ 'Idempotency-Key': request.idempotencyKey, 'Content-Type': 'application/json' });
    expect(JSON.parse(String(init.body))).toEqual({
      lines: [{ sku: 'CPU-AMD-7600', quantity: 1, slot: 'cpu' }],
      contact: { name: 'Valentina Aguirre', phone: '+591 71234567' },
      notes: 'Paso el sabado por la manana',
    });
    expect(reservation.number).toBe('ARM-WEB-000004');
    expect(reservation.replayed).toBe(false);
  });

  it('reconoce la repetición idempotente (200 + Idempotent-Replayed: true)', async () => {
    fetchMock.mockResolvedValue(json(RESERVATION_DTO, 200, { 'Idempotent-Replayed': 'true' }));
    const reservation = await new HttpReservationGateway(new StorefrontApi(BASE)).create(request);
    expect(reservation.replayed).toBe(true);
  });

  it('409 sin stock: no se reservó nada y llegan las piezas afectadas con cuánto hay', async () => {
    fetchMock.mockResolvedValue(problem(INSUFFICIENT_STOCK_PROBLEM, 409));
    await expect(new HttpReservationGateway(new StorefrontApi(BASE)).create(request)).rejects.toMatchObject({
      kind: 'insufficient_stock',
      status: 409,
      code: 'storefront.insufficient_stock',
      shortages: [{ sku: 'CASE-COR-4000D', requested: 16, available: 4 }],
    });
  });

  it('422 del dominio y 400 de validación conservan el código y los mensajes', async () => {
    const gateway = new HttpReservationGateway(new StorefrontApi(BASE));
    fetchMock.mockResolvedValueOnce(problem({ title: 'domain', status: 422, code: 'pcbuild.contact_phone', detail: 'El teléfono debe tener 7 u 8 dígitos (Bolivia), con o sin +591.' }, 422));
    await expect(gateway.create(request)).rejects.toMatchObject({ kind: 'domain', code: 'pcbuild.contact_phone' });
    fetchMock.mockResolvedValueOnce(problem({ title: 'validation', status: 400, detail: 'Datos no válidos', errors: ['Agregue al menos una pieza al armado.'] }, 400));
    await expect(gateway.create(request)).rejects.toMatchObject({ kind: 'validation', errors: ['Agregue al menos una pieza al armado.'] });
  });

  it('429 en la reserva y red caída llegan como sus propias clases', async () => {
    const gateway = new HttpReservationGateway(new StorefrontApi(BASE));
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 429 }));
    await expect(gateway.create(request)).rejects.toMatchObject({ kind: 'rate_limited' });
    fetchMock.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    await expect(gateway.create(request)).rejects.toMatchObject({ kind: 'network' });
  });

  it('consulta con el número y el teléfono en la URL (el «+» codificado) y 404 si no coinciden', async () => {
    const gateway = new HttpReservationGateway(new StorefrontApi(BASE));
    fetchMock.mockResolvedValueOnce(json(RESERVATION_DTO));
    const reservation = await gateway.get('ARM-WEB-000004', '+591 71234567');
    expect(lastRequest().url).toBe(`${BASE}/storefront/v1/reservations/ARM-WEB-000004?phone=%2B591%2071234567`);
    expect(reservation.status).toBe('Reserved');
    fetchMock.mockResolvedValueOnce(problem({ title: 'not_found', status: 404, detail: 'La reserva ARM-WEB-000004 no existe o el teléfono no coincide.' }, 404));
    await expect(gateway.get('ARM-WEB-000004', '70000000')).rejects.toMatchObject({ kind: 'not_found' });
  });

  it('libera la reserva con POST …/cancel y el teléfono en el cuerpo; 422 pcbuild.state si ya no estaba reservada', async () => {
    const gateway = new HttpReservationGateway(new StorefrontApi(BASE));
    fetchMock.mockResolvedValueOnce(json({ ...RESERVATION_DTO, status: 'Cancelled', statusText: 'Cancelada', cancelReason: 'Cancelada por el cliente desde la tienda web' }));
    const cancelled = await gateway.cancel('ARM-WEB-000004', '71234567');
    const { url, init } = lastRequest();
    expect(url).toBe(`${BASE}/storefront/v1/reservations/ARM-WEB-000004/cancel`);
    expect(JSON.parse(String(init.body))).toEqual({ phone: '71234567' });
    expect(cancelled).toMatchObject({ status: 'Cancelled', cancelReason: 'Cancelada por el cliente desde la tienda web' });
    fetchMock.mockResolvedValueOnce(problem({ title: 'domain', status: 422, code: 'pcbuild.state', detail: 'El armado ARM-WEB-000004 está anulado: la reserva ya no se puede cancelar.' }, 422));
    await expect(gateway.cancel('ARM-WEB-000004', '71234567')).rejects.toMatchObject({ kind: 'domain', code: 'pcbuild.state' });
  });
});
