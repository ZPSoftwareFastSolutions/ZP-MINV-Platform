// Mapeo del contrato (dto.ts) al dominio y viceversa. Funciones puras: sin React ni red. Tolerantes con lo que la API
// pueda agregar (campos extra) y estrictas con lo que el dominio exige (etiquetas, condición, ranuras y niveles válidos).

import { BUILD_SLOTS } from '@/1-domain/builder/slots';
import type { BuildLine, BuildPreset, PresetTier, SlotKey } from '@/1-domain/builder/types';
import type { Brand, Category, Condition, Product, ProductTag, Spec, SpecValue } from '@/1-domain/catalog/types';
import { toSingleLine } from '@/1-domain/storefront/contact';
import { StorefrontError, type StorefrontErrorKind } from '@/1-domain/storefront/errors';
import { normalizeReservationPolicy, parseReservationKind } from '@/1-domain/storefront/policy';
import type {
  CatalogSnapshot,
  Reservation,
  ReservationBuyer,
  ReservationRequest,
  ReservationStatus,
  StockShortage,
  StoreInfo,
} from '@/1-domain/storefront/types';
import type {
  StorefrontBrandDto,
  StorefrontReservationBuyerDto,
  StorefrontCatalogDto,
  StorefrontCategoryDto,
  StorefrontPresetDto,
  StorefrontProblemDto,
  StorefrontProductDto,
  StorefrontReservationRequestDto,
  StorefrontReservationViewDto,
  StorefrontSpecDto,
} from './dto';

const CONDITIONS: readonly Condition[] = ['Nuevo', 'Reacondicionado', 'Usado'];
const TAGS: readonly ProductTag[] = ['destacado', 'oferta', 'nuevo'];
const TIERS: readonly PresetTier[] = ['entrada', 'media', 'alta', 'entusiasta', 'oficina', 'creador'];
const STATUSES: readonly ReservationStatus[] = ['Reserved', 'Sold', 'Cancelled', 'Expired'];
const SLOT_KEYS = new Set<string>(BUILD_SLOTS.map((slot) => slot.key));

function isCondition(value: string): value is Condition {
  return (CONDITIONS as readonly string[]).includes(value);
}

function isTag(value: string): value is ProductTag {
  return (TAGS as readonly string[]).includes(value);
}

function isTier(value: string): value is PresetTier {
  return (TIERS as readonly string[]).includes(value);
}

function isSlotKey(value: string): value is SlotKey {
  return SLOT_KEYS.has(value);
}

function isStatus(value: string): value is ReservationStatus {
  return (STATUSES as readonly string[]).includes(value);
}

function nonNegative(value: number | null | undefined): number {
  return Number.isFinite(value) && (value as number) > 0 ? (value as number) : 0;
}

/** Une la base de la API con la ruta relativa de la imagen (`/storefront/v1/products/SKU/image`). */
export function absoluteImageUrl(image: string | null | undefined, apiBase: string): string {
  if (!image) return '';
  if (/^(https?:)?\/\//i.test(image) || image.startsWith('data:')) return image;
  const base = apiBase.replace(/\/+$/, '');
  return image.startsWith('/') ? `${base}${image}` : `${base}/${image}`;
}

function toSpecValue(value: StorefrontSpecDto['value'], text: string): SpecValue {
  if (Array.isArray(value)) return value.map(String);
  if (typeof value === 'number' || typeof value === 'string') return value;
  return text;
}

export function toSpec(dto: StorefrontSpecDto): Spec {
  return {
    key: dto.key,
    label: dto.label,
    value: toSpecValue(dto.value, dto.text),
    text: dto.text,
    unit: dto.unit ?? null,
    filterable: Boolean(dto.filterable),
  };
}

/** DTO de producto → `Product` del dominio (`stock` = disponible; la imagen queda absoluta hacia la API). */
export function toProduct(dto: StorefrontProductDto, apiBase: string): Product {
  return {
    sku: dto.sku,
    slug: dto.slug || dto.sku.toLowerCase(),
    name: dto.name,
    shortName: dto.shortName || dto.name,
    category: dto.category,
    categoryName: dto.categoryName,
    categoryPath: dto.categoryPath,
    brand: dto.brand,
    price: dto.price,
    listPrice: dto.listPrice != null && dto.listPrice > dto.price ? dto.listPrice : null,
    image: absoluteImageUrl(dto.image, apiBase),
    stock: nonNegative(dto.available),
    reserved: nonNegative(dto.reserved),
    condition: isCondition(dto.condition) ? dto.condition : 'Nuevo',
    warrantyMonths: nonNegative(dto.warrantyMonths),
    serialized: Boolean(dto.serialized),
    popularity: Math.min(10, Math.max(1, Math.trunc(dto.popularity || 1))),
    tags: (dto.tags ?? []).filter(isTag),
    description: dto.description ?? '',
    highlights: dto.highlights ?? [],
    specs: (dto.specs ?? []).map(toSpec),
  };
}

export function toCategory(dto: StorefrontCategoryDto): Category {
  return {
    code: dto.code,
    name: dto.name,
    slug: dto.slug,
    parent: dto.parent ?? null,
    icon: dto.icon || 'Tag',
    description: dto.description ?? '',
    productCount: nonNegative(dto.productCount),
  };
}

export function toBrand(dto: StorefrontBrandDto): Brand {
  return { code: dto.code, name: dto.name, productCount: nonNegative(dto.productCount) };
}

/**
 * DTO de armado publicado → `BuildPreset`. Las líneas con una ranura desconocida se omiten (el dominio solo arma con las
 * ranuras de BUILD_SLOTS); el nivel desconocido cae en «media».
 */
export function toPreset(dto: StorefrontPresetDto): BuildPreset {
  return {
    id: dto.id || dto.number.toLowerCase(),
    name: dto.name,
    tier: isTier(dto.tier) ? dto.tier : 'media',
    lines: dto.lines
      .filter((line) => isSlotKey(line.slot))
      .map((line) => ({ slot: line.slot as SlotKey, sku: line.sku, quantity: Math.max(1, Math.trunc(line.quantity || 1)) })),
  };
}

export function toStoreInfo(dto: Pick<StorefrontCatalogDto, 'company' | 'branch'>): StoreInfo {
  return {
    company: {
      code: dto.company.code,
      name: dto.company.name,
      branches: (dto.company.branches ?? []).map((branch) => ({ code: branch.code, name: branch.name })),
    },
    branch: { code: dto.branch.code, name: dto.branch.name },
  };
}

/** Instantánea completa. `apiBase` es la URL del gateway (para las imágenes). */
export function toCatalogSnapshot(dto: StorefrontCatalogDto, apiBase: string): CatalogSnapshot {
  const generatedAt = new Date(dto.generatedAt);
  return {
    store: toStoreInfo(dto),
    categories: dto.categories.map(toCategory),
    brands: dto.brands.map(toBrand),
    products: dto.products.map((product) => toProduct(product, apiBase)),
    presets: dto.presets.map(toPreset),
    // V7: los plazos de la reserva los fija el servidor; un servidor de la V6 no los manda (48 h y hasta 3 días).
    reservationPolicy: normalizeReservationPolicy(dto),
    generatedAt: Number.isNaN(generatedAt.getTime()) ? new Date() : generatedAt,
  };
}

/** Datos para la factura → JSON (número recortado, complemento solo con CI, razón social en una línea). */
export function toReservationBuyerDto(buyer: ReservationBuyer): StorefrontReservationBuyerDto {
  const complement = buyer.complement?.trim().toUpperCase();
  const name = buyer.name ? toSingleLine(buyer.name) : '';
  return {
    documentType: buyer.documentType,
    documentNumber: buyer.documentNumber.trim(),
    ...(buyer.documentType === 1 && complement ? { complement } : {}),
    ...(name ? { name } : {}),
  };
}

/**
 * Petición del dominio → cuerpo JSON (la llave de idempotencia viaja en la cabecera). Nombre y notas viajan en UNA línea:
 * el servidor rechaza los saltos de línea en medio (400). V7: las líneas sin ranura (carrito) van sin `slot`, y `kind`,
 * `holdDays` y `buyer` solo se envían si vienen (un armado sin ellos manda el mismo cuerpo de la V6).
 */
export function toReservationRequestDto(request: ReservationRequest): StorefrontReservationRequestDto {
  const notes = request.notes ? toSingleLine(request.notes) : '';
  const name = request.name ? toSingleLine(request.name) : '';
  return {
    lines: request.lines.map((line) => ({ sku: line.sku, quantity: line.quantity, ...(line.slot ? { slot: line.slot } : {}) })),
    contact: {
      name: request.contact.name,
      phone: request.contact.phone,
      ...(request.contact.email ? { email: request.contact.email } : {}),
    },
    ...(notes ? { notes } : {}),
    ...(name ? { name } : {}),
    ...(request.kind === 'cart' ? { kind: 'cart' as const } : {}),
    ...(request.holdDays !== undefined ? { holdDays: request.holdDays } : {}),
    ...(request.buyer ? { buyer: toReservationBuyerDto(request.buyer) } : {}),
  };
}

/** Líneas del armado → líneas de la petición (una por SKU; la ranura ya está resuelta en el armado). */
export function toReservationLines(lines: readonly BuildLine[]): ReservationRequest['lines'] {
  return lines.map((line) => ({ sku: line.product.sku, quantity: line.quantity, slot: line.slot }));
}

const STATUS_TEXT: Record<ReservationStatus, string> = {
  Reserved: 'Reservada',
  Sold: 'Vendida',
  Cancelled: 'Cancelada',
  Expired: 'Vencida',
};

export function toReservation(dto: StorefrontReservationViewDto, replayed = false): Reservation {
  const status = isStatus(dto.status) ? dto.status : 'Cancelled';
  return {
    number: dto.number,
    kind: parseReservationKind(dto.kind, dto.number),
    status,
    statusText: dto.statusText || STATUS_TEXT[status],
    createdAt: new Date(dto.createdAt),
    reservedUntil: new Date(dto.reservedUntil),
    total: dto.total,
    contactName: dto.contactName,
    branch: dto.branch,
    notes: dto.notes ?? null,
    hasCompatibilityWarnings: Boolean(dto.hasCompatibilityWarnings),
    lines: dto.lines.map((line) => ({
      slot: line.slot ?? '',
      sku: line.sku,
      name: line.name,
      quantity: line.quantity,
      unitPrice: line.unitPrice,
      subtotal: line.subtotal,
    })),
    cancelReason: dto.cancelReason ?? null,
    replayed,
    mailQueued: dto.mailQueued === true,
    // V7 · Solo en una consulta (regla S-06): el contacto enmascarado; las demás respuestas no los traen.
    ...(typeof dto.maskedPhone === 'string' ? { maskedPhone: dto.maskedPhone } : {}),
    ...(typeof dto.maskedEmail === 'string' ? { maskedEmail: dto.maskedEmail } : {}),
    ...(dto.masked === true ? { masked: true } : {}),
  };
}

function kindFor(status: number, problem: StorefrontProblemDto | undefined): StorefrontErrorKind {
  if (problem?.code === 'storefront.insufficient_stock' || status === 409) return 'insufficient_stock';
  switch (status) {
    case 400:
      return 'validation';
    case 404:
      return 'not_found';
    case 422:
      return problem?.title === 'idempotency' ? 'idempotency' : 'domain';
    case 429:
      return 'rate_limited';
    case 503:
      return 'unavailable';
    default:
      return 'unknown';
  }
}

const DEFAULT_DETAIL: Partial<Record<StorefrontErrorKind, string>> = {
  validation: 'Los datos enviados no son válidos.',
  not_found: 'No encontramos lo que buscabas.',
  insufficient_stock: 'No hay stock suficiente para una o más piezas.',
  domain: 'La tienda no pudo procesar la solicitud.',
  idempotency: 'La llave de idempotencia ya se usó para otra reserva: use una llave nueva.',
  rate_limited: 'Límite de solicitudes superado. Esperá un minuto e intentá de nuevo.',
  unavailable: 'Tienda web no disponible.',
  unknown: 'Ocurrió un error inesperado en la tienda.',
};

export function toShortages(problem: StorefrontProblemDto | undefined): StockShortage[] {
  return (problem?.shortages ?? []).map((item) => ({
    sku: item.sku,
    name: item.name,
    requested: item.requested,
    available: nonNegative(item.available),
  }));
}

/** Respuesta de error HTTP (código + `problem+json` si lo hubo) → StorefrontError del dominio. */
export function toStorefrontError(status: number, problem?: StorefrontProblemDto): StorefrontError {
  const kind = kindFor(status, problem);
  return new StorefrontError({
    kind,
    status,
    detail: problem?.detail?.trim() || DEFAULT_DETAIL[kind] || `Error ${status}`,
    code: problem?.code ?? null,
    errors: problem?.errors ?? [],
    shortages: toShortages(problem),
  });
}
