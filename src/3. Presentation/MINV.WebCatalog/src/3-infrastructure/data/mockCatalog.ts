// Mock de la V5 como fuente de la V6 (`VITE_API_URL=mock` y pruebas): la misma instantánea que daría la API, pero desde
// los *.data.ts generados, y una pasarela de reservas EN MEMORIA que respeta el contrato (todo o nada, número
// ARM-WEB-n, consulta y cancelación con el teléfono). Sin red ni almacenamiento: al recargar se pierde todo.
// V7: reservas de carrito (`kind = "cart"`, número RES-WEB-n, líneas sin ranura), días para recoger (1 a 3), datos para
// la factura con las reglas del SIN, texto libre sin caracteres de control (400) y `mailQueued` cuando hay correo.
// `hold`/`release` dejan que la cuenta del cliente del modo mock (mockWeb.ts) reserve sobre el MISMO stock.

import { documentType } from '@/1-domain/account/documents';
import type { BuildPreset } from '@/1-domain/builder/types';
import type { Product } from '@/1-domain/catalog/types';
import type { ICatalogSource } from '@/1-domain/ports/ICatalogSource';
import type { IReservationGateway } from '@/1-domain/ports/IReservationGateway';
import { normalizeBolivianPhone } from '@/1-domain/storefront/contact';
import { StorefrontError } from '@/1-domain/storefront/errors';
import { hoursForHoldDays } from '@/1-domain/storefront/policy';
import {
  DEFAULT_RESERVATION_POLICY,
  HOLD_DAYS_LIMIT,
  RESERVATION_LIMITS,
  type CatalogSnapshot,
  type Reservation,
  type ReservationBuyer,
  type ReservationPolicy,
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
  reservationPolicy: DEFAULT_RESERVATION_POLICY,
};

/** Instantánea equivalente a `GET /catalog` sobre el mock. */
export function mockSnapshot(data: InMemoryCatalogData = MOCK_CATALOG): CatalogSnapshot {
  return {
    store: data.store,
    categories: [...data.categories],
    brands: [...data.brands],
    products: [...data.products],
    presets: [...data.presets],
    reservationPolicy: data.reservationPolicy ?? DEFAULT_RESERVATION_POLICY,
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

/** Una línea que otra vía del modo mock (la cuenta del cliente) quiere reservar sobre el stock compartido. */
export interface StockHoldLine {
  sku: string;
  quantity: number;
}

const STATUS_TEXT = { Reserved: 'Reservada', Sold: 'Vendida', Cancelled: 'Cancelada', Expired: 'Vencida' } as const;

const CONTROL_CHARS = /[\p{Cc}\p{Zl}\p{Zp}]/u;
const NO_CONTROL = 'no admite saltos de línea, tabuladores ni otros caracteres de control.';

/** Como el servidor: los extremos se recortan (no son error); lo que queda en medio no puede tener caracteres de control. */
function hasControlChars(value: string | undefined): boolean {
  return value !== undefined && CONTROL_CHARS.test(value.trim());
}

function buyerErrors(buyer: ReservationBuyer): string[] {
  const errors: string[] = [];
  if (!documentType(buyer.documentType)) errors.push('El tipo de documento para la factura va de 1 (CI) a 5 (NIT).');
  const number = buyer.documentNumber.trim();
  if (!number) errors.push('Indique el número de documento (CI o NIT) para la factura.');
  else if (number.length > 20) errors.push('El número de documento supera 20 caracteres.');
  if ((buyer.complement?.trim().length ?? 0) > 5) errors.push('El complemento tiene como máximo 5 caracteres.');
  if ((buyer.name?.trim().length ?? 0) > RESERVATION_LIMITS.buyerNameMaxLength) errors.push('El nombre o razón social supera 150 caracteres.');
  else if (hasControlChars(buyer.name)) errors.push(`El nombre o razón social ${NO_CONTROL}`);
  return errors;
}

/** Mismas validaciones del servidor (400): límites, texto libre en una línea, días de 1 a 3 y datos para la factura. */
function validationErrors(request: ReservationRequest): string[] {
  const errors: string[] = [];
  if (request.lines.length === 0) errors.push('Agregue al menos una pieza al armado.');
  if (request.lines.length > RESERVATION_LIMITS.maxLines) errors.push(`Una reserva admite como máximo ${RESERVATION_LIMITS.maxLines} líneas.`);
  if (request.lines.some((line) => line.quantity < 1 || line.quantity > RESERVATION_LIMITS.maxQuantityPerLine)) {
    errors.push(`La cantidad de cada pieza va de 1 a ${RESERVATION_LIMITS.maxQuantityPerLine}.`);
  }
  if (!request.contact.name.trim()) errors.push('Indique el nombre de quien reserva.');
  else if (hasControlChars(request.contact.name)) errors.push(`El nombre de contacto ${NO_CONTROL}`);
  if ((request.notes?.trim().length ?? 0) > RESERVATION_LIMITS.notesMaxLength) errors.push('Las notas superan 500 caracteres.');
  else if (hasControlChars(request.notes)) errors.push(`Las notas van en una sola línea: ${NO_CONTROL}`);
  if (hasControlChars(request.name)) errors.push(`El nombre de la reserva ${NO_CONTROL}`);
  if (request.holdDays !== undefined && (!Number.isInteger(request.holdDays) || request.holdDays < 1 || request.holdDays > HOLD_DAYS_LIMIT)) {
    errors.push(`Los días para recoger la reserva van de 1 a ${HOLD_DAYS_LIMIT}.`);
  }
  if (request.buyer) errors.push(...buyerErrors(request.buyer));
  return errors;
}

/** Reglas del SIN que el servidor aplica en el dominio (422): CI y NIT solo dígitos, complemento solo con CI. */
function buyerDomainError(buyer: ReservationBuyer): StorefrontError | null {
  if ((buyer.documentType === 1 || buyer.documentType === 5) && !/^[0-9]+$/.test(buyer.documentNumber.trim())) {
    return new StorefrontError({ kind: 'domain', status: 422, code: 'buyer.doc_numeric', detail: 'Con CI o NIT el número de documento solo admite dígitos.' });
  }
  if (buyer.complement?.trim() && buyer.documentType !== 1) {
    return new StorefrontError({ kind: 'domain', status: 422, code: 'buyer.complement', detail: 'El complemento solo se usa con cédula de identidad.' });
  }
  return null;
}

/**
 * Pasarela de reservas en memoria: descuenta lo disponible del producto (y suma `reserved`) mientras la reserva está
 * activa, y lo devuelve al cancelar. Sirve al modo mock y a las pruebas de la presentación.
 */
export class InMemoryReservationGateway implements IReservationGateway {
  private readonly products: Map<string, Product>;
  private readonly store: StoreInfo;
  private readonly policy: ReservationPolicy;
  private readonly reservations = new Map<string, StoredReservation>();
  private readonly byKey = new Map<string, string>();
  /** Numeración propia de armados (ARM-WEB-n) y de carritos (RES-WEB-n), como el servidor. */
  private readonly sequences = { build: 0, cart: 0 };
  private readonly now: () => Date;

  constructor(data: InMemoryCatalogData = MOCK_CATALOG, now: () => Date = () => new Date()) {
    this.products = new Map(data.products.map((product) => [product.sku, product]));
    this.store = data.store;
    this.policy = data.reservationPolicy ?? DEFAULT_RESERVATION_POLICY;
    this.now = now;
  }

  /** Producto con la disponibilidad actual (para que el mock refleje lo reservado). */
  product(sku: string): Product | undefined {
    return this.products.get(sku);
  }

  /** Faltantes de unas líneas contra lo disponible (vacío si alcanza para todas). */
  shortagesOf(lines: readonly StockHoldLine[]): StockShortage[] {
    const shortages: StockShortage[] = [];
    for (const line of lines) {
      const product = this.products.get(line.sku);
      if (product && product.stock < line.quantity) shortages.push({ sku: product.sku, name: product.name, requested: line.quantity, available: product.stock });
    }
    return shortages;
  }

  /**
   * Reserva el stock de unas líneas para otra vía del modo mock (la cuenta del cliente), todo o nada. Devuelve los
   * faltantes: si hay alguno, no reservó nada.
   */
  hold(lines: readonly StockHoldLine[]): StockShortage[] {
    const shortages = this.shortagesOf(lines);
    if (shortages.length > 0) return shortages;
    for (const line of lines) this.move(line.sku, line.quantity);
    return [];
  }

  /** Devuelve al stock lo que se había reservado con `hold`. */
  release(lines: readonly StockHoldLine[]): void {
    for (const line of lines) this.move(line.sku, -line.quantity);
  }

  /** Pasa unidades de disponibles a reservadas (o al revés, con una cantidad negativa). */
  private move(sku: string, quantity: number): void {
    const product = this.products.get(sku);
    if (product) this.products.set(sku, { ...product, stock: product.stock - quantity, reserved: Math.max(0, product.reserved + quantity) });
  }

  async create(request: ReservationRequest): Promise<Reservation> {
    const kind = request.kind === 'cart' ? 'cart' : 'build';
    const fingerprint = JSON.stringify({
      lines: request.lines,
      contact: request.contact,
      notes: request.notes ?? '',
      name: request.name ?? '',
      kind,
      holdDays: request.holdDays ?? null,
      buyer: request.buyer ?? null,
    });
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
    const errors = validationErrors(request);
    if (errors.length > 0) {
      throw new StorefrontError({ kind: 'validation', status: 400, detail: `Datos no válidos: ${errors.join(' · ')}`, errors });
    }
    if (request.holdDays !== undefined && request.holdDays > this.policy.maxHoldDays) {
      const detail = this.policy.maxHoldDays === 1 ? 'La reserva se guarda 1 día.' : `La reserva se guarda de 1 a ${this.policy.maxHoldDays} días.`;
      throw new StorefrontError({ kind: 'domain', status: 422, code: 'storefront.hold_days', detail });
    }
    const buyerProblem = request.buyer ? buyerDomainError(request.buyer) : null;
    if (buyerProblem) throw buyerProblem;
    for (const line of request.lines) {
      if (!this.products.has(line.sku)) throw new StorefrontError({ kind: 'domain', status: 422, code: 'product.inactive', detail: `El producto ${line.sku} no está en el catálogo.` });
    }
    const shortages = this.shortagesOf(request.lines);
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
    for (const line of request.lines) this.move(line.sku, line.quantity);
    const createdAt = this.now();
    this.sequences[kind] += 1;
    const number = `${kind === 'cart' ? 'RES' : 'ARM'}-WEB-${String(this.sequences[kind]).padStart(6, '0')}`;
    const lines = request.lines.map((line) => {
      const product = this.products.get(line.sku) as Product;
      return {
        // En un carrito las líneas no tienen ranura (el servidor manda null; el dominio lo guarda como texto vacío).
        slot: kind === 'cart' ? '' : (line.slot ?? ''),
        sku: product.sku,
        name: product.name,
        quantity: line.quantity,
        unitPrice: product.price,
        subtotal: roundMoney(product.price * line.quantity),
      };
    });
    const hours = request.holdDays !== undefined ? hoursForHoldDays(request.holdDays) : this.policy.reservationHours;
    const reservation: Reservation = {
      number,
      kind,
      status: 'Reserved',
      statusText: STATUS_TEXT.Reserved,
      createdAt,
      reservedUntil: new Date(createdAt.getTime() + hours * 3_600_000),
      total: sumMoney(lines.map((line) => line.subtotal)),
      contactName: request.contact.name.trim(),
      branch: this.store.branch.code,
      notes: request.notes?.trim() || null,
      hasCompatibilityWarnings: false,
      lines,
      cancelReason: null,
      replayed: false,
      // Como el servidor: el correo con el código y el detalle se encola solo si la persona dejó uno.
      mailQueued: Boolean(request.contact.email?.trim()),
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
    return { ...stored.reservation, replayed: false };
  }

  async cancel(number: string, phone: string): Promise<Reservation> {
    const stored = this.find(number, phone);
    if (stored.reservation.status !== 'Reserved') {
      throw new StorefrontError({ kind: 'domain', status: 422, code: 'pcbuild.state', detail: `El armado ${stored.reservation.number} está anulado: la reserva ya no se puede cancelar.` });
    }
    for (const line of stored.reservation.lines) this.move(line.sku, -line.quantity);
    stored.reservation = { ...stored.reservation, status: 'Cancelled', statusText: STATUS_TEXT.Cancelled, cancelReason: 'Cancelada por el cliente desde la tienda web' };
    return { ...stored.reservation, replayed: false };
  }
}
