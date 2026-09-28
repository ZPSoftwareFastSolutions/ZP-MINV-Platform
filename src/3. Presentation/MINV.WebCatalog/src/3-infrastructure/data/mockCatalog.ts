// Mock de la V5 como fuente de la V6 (`VITE_API_URL=mock` y pruebas): la misma instantánea que daría la API, pero desde
// los *.data.ts generados, y una pasarela de reservas EN MEMORIA que respeta el contrato (todo o nada, número
// ARM-WEB-n, consulta y cancelación con el teléfono). Sin red ni almacenamiento: al recargar se pierde todo.

import type { BuildPreset } from '@/1-domain/builder/types';
import type { Product } from '@/1-domain/catalog/types';
import type { ICatalogSource } from '@/1-domain/ports/ICatalogSource';
import type { IReservationGateway } from '@/1-domain/ports/IReservationGateway';
import { normalizeBolivianPhone } from '@/1-domain/storefront/contact';
import { StorefrontError } from '@/1-domain/storefront/errors';
import {
  RESERVATION_HOURS,
  type CatalogSnapshot,
  type Reservation,
  type ReservationRequest,
  type StockShortage,
  type StoreInfo,
} from '@/1-domain/storefront/types';
import { roundMoney, sumMoney } from '@/1-domain/catalog/money';
import type { InMemoryCatalogData } from '../InMemoryCatalogRepository';
import { BRANDS } from './brands.data';
import { PRODUCTS } from './catalog.data';
import { CATEGORIES } from './categories.data';
import { PRESETS } from './presets.data';

/** Empresa de prueba del mock (la misma que carga `minv datos-prueba`). */
export const MOCK_STORE: StoreInfo = {
  company: {
    code: 'TECHZONE',
    name: 'Tech Zone Gaming S.R.L.',
    branches: [
      { code: 'CM', name: 'Casa matriz La Paz · Av. 16 de Julio (El Prado)' },
      { code: 'CB', name: 'Sucursal Cochabamba' },
      { code: 'SC', name: 'Sucursal Santa Cruz' },
    ],
  },
  branch: { code: 'CM', name: 'Casa matriz La Paz · Av. 16 de Julio (El Prado)' },
};

/** Datos del mock listos para el repositorio en memoria (los productos sin reservas). */
export const MOCK_CATALOG: InMemoryCatalogData = {
  store: MOCK_STORE,
  categories: CATEGORIES,
  brands: BRANDS,
  products: PRODUCTS.map((product): Product => ({ ...product, reserved: 0 })),
  presets: PRESETS,
};

/** Instantánea equivalente a `GET /catalog` sobre el mock. */
export function mockSnapshot(data: InMemoryCatalogData = MOCK_CATALOG): CatalogSnapshot {
  return {
    store: data.store,
    categories: [...data.categories],
    brands: [...data.brands],
    products: [...data.products],
    presets: [...data.presets],
    generatedAt: new Date(),
  };
}

/** Fuente del catálogo sobre el mock: responde en el acto (sin red). Comparte productos con la pasarela en memoria. */
export class MockCatalogSource implements ICatalogSource {
  private readonly data: InMemoryCatalogData;

  constructor(data: InMemoryCatalogData = MOCK_CATALOG) {
    this.data = data;
  }

  load(): Promise<CatalogSnapshot> {
    return Promise.resolve(mockSnapshot(this.data));
  }

  product(slug: string): Promise<Product | undefined> {
    const key = slug.toLowerCase();
    return Promise.resolve(this.data.products.find((product) => product.slug === key || product.sku.toLowerCase() === key));
  }

  presets(): Promise<BuildPreset[]> {
    return Promise.resolve([...this.data.presets]);
  }
}

interface StoredReservation {
  reservation: Reservation;
  phone: string;
  key: string;
  fingerprint: string;
}

const STATUS_TEXT = { Reserved: 'Reservada', Sold: 'Vendida', Cancelled: 'Cancelada', Expired: 'Vencida' } as const;

/**
 * Pasarela de reservas en memoria: descuenta lo disponible del producto (y suma `reserved`) mientras la reserva está
 * activa, y lo devuelve al cancelar. Sirve al modo mock y a las pruebas de la presentación.
 */
export class InMemoryReservationGateway implements IReservationGateway {
  private readonly products: Map<string, Product>;
  private readonly store: StoreInfo;
  private readonly reservations = new Map<string, StoredReservation>();
  private readonly byKey = new Map<string, string>();
  private sequence = 0;
  private readonly now: () => Date;

  constructor(data: InMemoryCatalogData = MOCK_CATALOG, now: () => Date = () => new Date()) {
    this.products = new Map(data.products.map((product) => [product.sku, product]));
    this.store = data.store;
    this.now = now;
  }

  /** Producto con la disponibilidad actual (para que el mock refleje lo reservado). */
  product(sku: string): Product | undefined {
    return this.products.get(sku);
  }

  async create(request: ReservationRequest): Promise<Reservation> {
    const fingerprint = JSON.stringify({ lines: request.lines, contact: request.contact, notes: request.notes ?? '', name: request.name ?? '' });
    const previous = this.byKey.get(request.idempotencyKey);
    if (previous) {
      const stored = this.reservations.get(previous);
      if (stored && stored.fingerprint === fingerprint) return { ...stored.reservation, replayed: true };
      throw new StorefrontError({ kind: 'idempotency', status: 422, detail: 'La llave de idempotencia ya se usó para otra reserva: use una llave nueva.' });
    }
    const phone = normalizeBolivianPhone(request.contact.phone);
    if (!phone) {
      throw new StorefrontError({ kind: 'domain', status: 422, code: 'pcbuild.contact_phone', detail: 'El teléfono debe tener 7 u 8 dígitos (Bolivia), con o sin +591.' });
    }
    if (request.lines.length === 0 || !request.contact.name.trim()) {
      const errors = [
        ...(request.lines.length === 0 ? ['Agregue al menos una pieza al armado.'] : []),
        ...(request.contact.name.trim() ? [] : ['Indique el nombre de quien reserva.']),
      ];
      throw new StorefrontError({ kind: 'validation', status: 400, detail: `Datos no válidos: ${errors.join(' · ')}`, errors });
    }
    const shortages: StockShortage[] = [];
    for (const line of request.lines) {
      const product = this.products.get(line.sku);
      if (!product) throw new StorefrontError({ kind: 'domain', status: 422, code: 'product.inactive', detail: `El producto ${line.sku} no está en el catálogo.` });
      if (product.stock < line.quantity) shortages.push({ sku: product.sku, name: product.name, requested: line.quantity, available: product.stock });
    }
    if (shortages.length > 0) {
      const detail = shortages.map((item) => `${item.sku} (pedido ${item.requested}, disponible ${item.available})`).join('; ');
      throw new StorefrontError({
        kind: 'insufficient_stock',
        status: 409,
        code: 'storefront.insufficient_stock',
        detail: `No hay stock suficiente para ${shortages.length} pieza(s): ${detail}`,
        shortages,
      });
    }
    for (const line of request.lines) {
      const product = this.products.get(line.sku) as Product;
      this.products.set(line.sku, { ...product, stock: product.stock - line.quantity, reserved: product.reserved + line.quantity });
    }
    const createdAt = this.now();
    this.sequence += 1;
    const number = `ARM-WEB-${String(this.sequence).padStart(6, '0')}`;
    const lines = request.lines.map((line) => {
      const product = this.products.get(line.sku) as Product;
      return { slot: line.slot, sku: product.sku, name: product.name, quantity: line.quantity, unitPrice: product.price, subtotal: roundMoney(product.price * line.quantity) };
    });
    const reservation: Reservation = {
      number,
      status: 'Reserved',
      statusText: STATUS_TEXT.Reserved,
      createdAt,
      reservedUntil: new Date(createdAt.getTime() + RESERVATION_HOURS * 3_600_000),
      total: sumMoney(lines.map((line) => line.subtotal)),
      contactName: request.contact.name.trim(),
      branch: this.store.branch.code,
      notes: request.notes?.trim() || null,
      hasCompatibilityWarnings: false,
      lines,
      cancelReason: null,
      replayed: false,
    };
    this.reservations.set(number, { reservation, phone, key: request.idempotencyKey, fingerprint });
    this.byKey.set(request.idempotencyKey, number);
    return reservation;
  }

  private find(number: string, phone: string): StoredReservation {
    const stored = this.reservations.get(number.trim().toUpperCase());
    if (!stored || stored.phone !== normalizeBolivianPhone(phone)) {
      throw new StorefrontError({ kind: 'not_found', status: 404, detail: `La reserva ${number} no existe o el teléfono no coincide.` });
    }
    return stored;
  }

  async get(number: string, phone: string): Promise<Reservation> {
    const stored = this.find(number, phone);
    if (stored.reservation.status === 'Reserved' && stored.reservation.reservedUntil.getTime() <= this.now().getTime()) {
      stored.reservation = { ...stored.reservation, status: 'Expired', statusText: STATUS_TEXT.Expired, cancelReason: 'Vencida' };
    }
    return stored.reservation;
  }

  async cancel(number: string, phone: string): Promise<Reservation> {
    const stored = this.find(number, phone);
    if (stored.reservation.status !== 'Reserved') {
      throw new StorefrontError({ kind: 'domain', status: 422, code: 'pcbuild.state', detail: `El armado ${stored.reservation.number} está anulado: la reserva ya no se puede cancelar.` });
    }
    for (const line of stored.reservation.lines) {
      const product = this.products.get(line.sku);
      if (product) this.products.set(line.sku, { ...product, stock: product.stock + line.quantity, reserved: Math.max(0, product.reserved - line.quantity) });
    }
    stored.reservation = { ...stored.reservation, status: 'Cancelled', statusText: STATUS_TEXT.Cancelled, cancelReason: 'Cancelada por el cliente desde la tienda web' };
    return stored.reservation;
  }
}
