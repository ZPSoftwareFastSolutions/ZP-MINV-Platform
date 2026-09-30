// Mapeo del contrato (DTO) al dominio y casos de uso de reservas (sin red: pasarela simulada).

import { describe, expect, it, vi } from 'vitest';
import type { IReservationGateway } from '@/1-domain/ports/IReservationGateway';
import { StorefrontError } from '@/1-domain/storefront/errors';
import type { Reservation } from '@/1-domain/storefront/types';
import { MOCK_CATALOG } from '@/3-infrastructure/data/mockCatalog';
import { CASE_DTO, CATALOG_DTO, CPU_DTO, INSUFFICIENT_STOCK_PROBLEM, RESERVATION_DTO } from './fixtures';
import {
  absoluteImageUrl,
  toCatalogSnapshot,
  toCategory,
  toPreset,
  toProduct,
  toReservation,
  toReservationRequestDto,
  toStorefrontError,
} from './mappers';
import { createReservationUseCases, newIdempotencyKey, validateReservationLines } from './reservations';

const API = 'http://localhost:5291';

describe('mapeo DTO → dominio', () => {
  it('convierte un producto: disponible → stock, imagen absoluta hacia la API, etiquetas y condición válidas', () => {
    const product = toProduct(CASE_DTO, API);
    expect(product.stock).toBe(4);
    expect(product.reserved).toBe(0);
    expect(product.image).toBe('http://localhost:5291/storefront/v1/products/CASE-COR-4000D/image');
    expect(product.listPrice).toBeNull();
    expect(product.condition).toBe('Nuevo');
    expect(product.tags).toEqual(['nuevo']);
    expect(product.specs[1].value).toEqual(['ATX', 'Micro-ATX', 'Mini-ITX']);
    expect(product.specs[2]).toMatchObject({ value: 360, text: '360 mm', unit: 'mm', filterable: false });
  });

  it('un producto reservado queda sin disponible pero con `reserved`; las etiquetas desconocidas se descartan', () => {
    const cpu = toProduct(CPU_DTO, API);
    expect(cpu.stock).toBe(0);
    expect(cpu.reserved).toBe(2);
    expect(cpu.image).toBe('');
    expect(cpu.listPrice).toBe(2199);
    expect(cpu.tags).toEqual(['destacado', 'oferta']);
    expect(cpu.popularity).toBe(10);
  });

  it('absoluteImageUrl respeta las URL absolutas y une la base sin duplicar barras', () => {
    expect(absoluteImageUrl('/storefront/v1/products/X/image', 'http://api/')).toBe('http://api/storefront/v1/products/X/image');
    expect(absoluteImageUrl('storefront/v1/products/X/image', 'http://api')).toBe('http://api/storefront/v1/products/X/image');
    expect(absoluteImageUrl('https://cdn.example/img.png', 'http://api')).toBe('https://cdn.example/img.png');
    expect(absoluteImageUrl(null, 'http://api')).toBe('');
  });

  it('convierte categorías y armados (las líneas con ranura desconocida se omiten; el nivel desconocido cae en «media»)', () => {
    expect(toCategory(CATALOG_DTO.categories[1])).toMatchObject({ code: 'CPU', parent: 'COMP', icon: 'Cpu', productCount: 1 });
    const preset = toPreset(CATALOG_DTO.presets[0]);
    expect(preset.id).toBe('arm-cm-000001');
    expect(preset.tier).toBe('entrada');
    expect(preset.lines.map((line) => line.slot)).toEqual(['cpu', 'case']);
    expect(toPreset({ ...CATALOG_DTO.presets[0], tier: 'otro' }).tier).toBe('media');
  });

  it('arma la instantánea completa con la empresa, la sucursal y la fecha', () => {
    const snapshot = toCatalogSnapshot(CATALOG_DTO, API);
    expect(snapshot.store.company.code).toBe('TECHZONE');
    expect(snapshot.store.branch.name).toContain('Casa matriz');
    expect(snapshot.store.company.branches).toHaveLength(3);
    expect(snapshot.categories).toHaveLength(3);
    expect(snapshot.brands).toHaveLength(2);
    expect(snapshot.products.map((product) => product.sku)).toEqual(['CASE-COR-4000D', 'CPU-AMD-7600']);
    expect(snapshot.presets).toHaveLength(1);
    expect(snapshot.generatedAt.getUTCFullYear()).toBe(2026);
    expect(toCatalogSnapshot({ ...CATALOG_DTO, generatedAt: 'no-es-fecha' }, API).generatedAt.getTime()).not.toBeNaN();
  });

  it('convierte una reserva y marca si fue una repetición idempotente', () => {
    const reservation = toReservation(RESERVATION_DTO);
    expect(reservation).toMatchObject({ number: 'ARM-WEB-000004', status: 'Reserved', statusText: 'Reservada', total: 3348, branch: 'CM', replayed: false });
    expect(reservation.reservedUntil.getTime() - reservation.createdAt.getTime()).toBe(48 * 3_600_000);
    expect(reservation.lines[1].subtotal).toBe(1299);
    expect(toReservation(RESERVATION_DTO, true).replayed).toBe(true);
    const raro = toReservation({ ...RESERVATION_DTO, status: 'Otro', statusText: '' });
    expect(raro.status).toBe('Cancelled');
    expect(raro.statusText).toBe('Cancelada');
  });

  it('arma el cuerpo de la petición sin la llave (viaja en la cabecera) y sin campos vacíos', () => {
    const body = toReservationRequestDto({
      lines: [{ sku: 'CPU-AMD-7600', quantity: 1, slot: 'cpu' }],
      contact: { name: 'Valentina Aguirre', phone: '+591 71234567' },
      notes: '  ',
      idempotencyKey: 'abc',
    });
    expect(body).toEqual({ lines: [{ sku: 'CPU-AMD-7600', quantity: 1, slot: 'cpu' }], contact: { name: 'Valentina Aguirre', phone: '+591 71234567' } });
    expect('idempotencyKey' in body).toBe(false);
  });

  it('traduce los errores del contrato a StorefrontError con su clase, detalle y faltantes', () => {
    const conflict = toStorefrontError(409, INSUFFICIENT_STOCK_PROBLEM);
    expect(conflict).toBeInstanceOf(StorefrontError);
    expect(conflict.kind).toBe('insufficient_stock');
    expect(conflict.code).toBe('storefront.insufficient_stock');
    expect(conflict.shortages).toEqual([{ sku: 'CASE-COR-4000D', name: 'Gabinete Corsair 4000D Airflow negro', requested: 16, available: 4 }]);
    expect(conflict.detail).toContain('disponible 4');

    expect(toStorefrontError(400, { title: 'validation', detail: 'Datos no válidos', errors: ['Indique el nombre de quien reserva.'] })).toMatchObject({ kind: 'validation', errors: ['Indique el nombre de quien reserva.'] });
    expect(toStorefrontError(404, { title: 'not_found', detail: 'La reserva no existe o el teléfono no coincide.' }).kind).toBe('not_found');
    expect(toStorefrontError(422, { title: 'domain', code: 'pcbuild.contact_phone', detail: 'El teléfono debe tener 7 u 8 dígitos (Bolivia), con o sin +591.' })).toMatchObject({ kind: 'domain', code: 'pcbuild.contact_phone' });
    expect(toStorefrontError(422, { title: 'idempotency', detail: 'La llave de idempotencia ya se usó para otra reserva: use una llave nueva.' }).kind).toBe('idempotency');
    expect(toStorefrontError(429)).toMatchObject({ kind: 'rate_limited', status: 429 });
    expect(toStorefrontError(429).detail.length).toBeGreaterThan(0);
    expect(toStorefrontError(503, { title: 'Tienda web no disponible' }).kind).toBe('unavailable');
    expect(toStorefrontError(500).kind).toBe('unknown');
  });
});

describe('casos de uso de reservas', () => {
  const cpu = MOCK_CATALOG.products.find((product) => product.sku === 'CPU-AMD-7600')!;
  const ssd = MOCK_CATALOG.products.find((product) => product.sku === 'SSD-KNG-NV3-1TB')!;

  it('genera llaves de idempotencia distintas por intento', () => {
    const keys = new Set(Array.from({ length: 50 }, () => newIdempotencyKey()));
    expect(keys.size).toBe(50);
    for (const key of keys) expect(key.length).toBeLessThanOrEqual(100);
  });

  it('valida los límites del contrato antes de viajar al servidor (1-20 líneas, 1-16 unidades)', () => {
    expect(validateReservationLines([])).toBe('Agregue al menos una pieza al armado.');
    expect(validateReservationLines([{ slot: 'storage', product: ssd, quantity: 17 }])).toContain('entre 1 y 16');
    expect(validateReservationLines([{ slot: 'cpu', product: cpu, quantity: 1 }])).toBeNull();
    const muchas = Array.from({ length: 21 }, (_, index) => ({ slot: 'software' as const, product: { ...ssd, sku: `SKU-${index}` }, quantity: 1 }));
    expect(validateReservationLines(muchas)).toContain('20');
  });

  it('reserve traduce las líneas del armado a la petición y delega en la pasarela; lookup y release normalizan el número', async () => {
    const created: Reservation = { ...(await import('./mappers')).toReservation(RESERVATION_DTO) };
    const gateway: IReservationGateway = {
      create: vi.fn().mockResolvedValue(created),
      get: vi.fn().mockResolvedValue(created),
      findByPhone: vi.fn().mockResolvedValue([created]),
      cancel: vi.fn().mockResolvedValue({ ...created, status: 'Cancelled' }),
    };
    const useCases = createReservationUseCases(gateway);
    const reservation = await useCases.reserve({
      lines: [
        { slot: 'cpu', product: cpu, quantity: 1 },
        { slot: 'storage', product: ssd, quantity: 2 },
      ],
      contact: { name: 'Valentina Aguirre', phone: '+591 71234567', email: 'v@correo.example' },
      notes: 'Paso el sábado',
      idempotencyKey: 'llave-1',
    });
    expect(reservation.number).toBe('ARM-WEB-000004');
    expect(gateway.create).toHaveBeenCalledWith({
      lines: [
        { sku: 'CPU-AMD-7600', quantity: 1, slot: 'cpu' },
        { sku: 'SSD-KNG-NV3-1TB', quantity: 2, slot: 'storage' },
      ],
      contact: { name: 'Valentina Aguirre', phone: '+591 71234567', email: 'v@correo.example' },
      notes: 'Paso el sábado',
      name: undefined,
      idempotencyKey: 'llave-1',
    });

    await expect(useCases.reserve({ lines: [], contact: { name: 'A', phone: '71234567' }, idempotencyKey: 'k' })).rejects.toMatchObject({ kind: 'validation', status: 400 });
    expect(gateway.create).toHaveBeenCalledTimes(1);

    await useCases.lookup(' arm-web-000004 ', ' 71234567 ');
    expect(gateway.get).toHaveBeenCalledWith('ARM-WEB-000004', '71234567');
    // V7 · Con el código solo (o un celular en blanco) la pasarela consulta sin teléfono; con el celular solo, la lista
    await useCases.lookup('res-web-000001');
    expect(gateway.get).toHaveBeenLastCalledWith('RES-WEB-000001');
    await useCases.lookup('res-web-000002', '   ');
    expect(gateway.get).toHaveBeenLastCalledWith('RES-WEB-000002');
    expect(await useCases.lookupByPhone(' +591 71234567 ')).toEqual([created]);
    expect(gateway.findByPhone).toHaveBeenCalledWith('+591 71234567');
    const released = await useCases.release('arm-web-000004', '71234567');
    expect(released.status).toBe('Cancelled');
    expect(gateway.cancel).toHaveBeenCalledWith('ARM-WEB-000004', '71234567');
  });
});
