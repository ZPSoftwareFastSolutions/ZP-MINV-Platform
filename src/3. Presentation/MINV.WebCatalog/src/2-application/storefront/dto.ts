// Contrato de la API pública de tienda `/storefront/v1` tal como viaja en el JSON (camelCase, fechas ISO 8601).
// Es la ÚNICA descripción del contrato en la web: docs/integration/storefront-api-v1.md. El mapeo al dominio está en
// mappers.ts; la red, en 3-infrastructure/http.

export interface StorefrontBranchDto {
  code: string;
  name: string;
}

export interface StorefrontCompanyDto {
  code: string;
  name: string;
  branches: StorefrontBranchDto[];
}

export interface StorefrontCategoryDto {
  code: string;
  name: string;
  slug: string;
  parent: string | null;
  icon: string;
  description: string;
  productCount: number;
}

export interface StorefrontBrandDto {
  code: string;
  name: string;
  productCount: number;
}

export interface StorefrontSpecDto {
  key: string;
  label: string;
  value: string | number | string[] | null;
  text: string;
  unit: string | null;
  filterable: boolean;
}

export interface StorefrontProductDto {
  sku: string;
  slug: string;
  name: string;
  shortName: string;
  category: string;
  categoryName: string;
  categoryPath: string;
  brand: string;
  price: number;
  listPrice: number | null;
  /** Ruta relativa (`/storefront/v1/products/{sku}/image`) o null. */
  image: string | null;
  available: number;
  reserved: number;
  onHand: number;
  condition: string;
  warrantyMonths: number;
  serialized: boolean;
  popularity: number;
  tags: string[];
  description: string;
  highlights: string[];
  specs: StorefrontSpecDto[];
}

export interface StorefrontPresetLineDto {
  slot: string;
  sku: string;
  quantity: number;
  unitPrice: number;
}

export interface StorefrontPresetDto {
  id: string;
  number: string;
  name: string;
  tier: string;
  total: number;
  available: boolean;
  lines: StorefrontPresetLineDto[];
}

/** `GET /storefront/v1/catalog`. */
export interface StorefrontCatalogDto {
  company: StorefrontCompanyDto;
  branch: StorefrontBranchDto;
  categories: StorefrontCategoryDto[];
  brands: StorefrontBrandDto[];
  products: StorefrontProductDto[];
  presets: StorefrontPresetDto[];
  generatedAt: string;
  /** V7: horas que se guarda una reserva sin días indicados (48 si no viene). */
  reservationHours?: number;
  /** V7: días que puede pedir quien reserva para recogerla (1 a 3; 3 si no viene). */
  maxHoldDays?: number;
}

export interface StorefrontReservationLineRequestDto {
  sku: string;
  quantity: number;
  /** Ranura del armado; en un carrito las líneas van sin ranura (V7). */
  slot?: string;
}

export interface StorefrontReservationContactDto {
  name: string;
  phone: string;
  email?: string;
}

/** V7: datos para la factura (opcionales). `documentType`: 1 CI, 2 CEX, 3 PAS, 4 OD, 5 NIT. */
export interface StorefrontReservationBuyerDto {
  documentType: number;
  documentNumber: string;
  complement?: string;
  name?: string;
}

/** Tipo de reserva del contrato (V7): `build` (armado, por defecto) o `cart` (carrito). */
export type StorefrontReservationKindDto = 'build' | 'cart';

/**
 * Cuerpo de `POST /storefront/v1/reservations` (la llave va en la cabecera `Idempotency-Key`). V7: `kind`, `holdDays`
 * (1 a 3) y `buyer` son opcionales; sin ellos el cuerpo es el mismo de la V6. Nombre, notas y razón social van en UNA
 * línea (el servidor rechaza los caracteres de control).
 */
export interface StorefrontReservationRequestDto {
  lines: StorefrontReservationLineRequestDto[];
  contact: StorefrontReservationContactDto;
  notes?: string;
  name?: string;
  kind?: StorefrontReservationKindDto;
  holdDays?: number;
  buyer?: StorefrontReservationBuyerDto;
}

export interface StorefrontReservationLineDto {
  /** Ranura del armado; null en las líneas de un carrito (V7). */
  slot: string | null;
  sku: string;
  name: string;
  quantity: number;
  unitPrice: number;
  subtotal: number;
}

/** `StorefrontReservationView`: respuesta de crear, consultar y cancelar (V7: la consulta por teléfono devuelve una lista). */
export interface StorefrontReservationViewDto {
  number: string;
  status: string;
  statusText: string;
  createdAt: string;
  reservedUntil: string;
  total: number;
  contactName: string;
  branch: string;
  notes: string | null;
  hasCompatibilityWarnings: boolean;
  lines: StorefrontReservationLineDto[];
  cancelReason: string | null;
  /** V7: `build` o `cart` (si falta, se deduce del número). */
  kind?: string;
  /** V7: el servidor encoló el correo de confirmación. */
  mailQueued?: boolean;
  /** V7 · Solo en una consulta: el celular enmascarado (`•••••567`). */
  maskedPhone?: string | null;
  /** V7 · Solo en una consulta: el correo enmascarado (`v•••@correo.example`) o null. */
  maskedEmail?: string | null;
  /** V7 · La consulta no se hizo con el código Y el celular: nombre enmascarado y sin notas. */
  masked?: boolean;
}

export interface StorefrontShortageDto {
  sku: string;
  name: string;
  requested: number;
  available: number;
}

/** Errores `application/problem+json` (RFC 7807) con las extensiones del gateway. */
export interface StorefrontProblemDto {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string | null;
  errors?: string[] | null;
  shortages?: StorefrontShortageDto[] | null;
  traceId?: string;
}

/** Cabecera obligatoria de `POST /reservations`. */
export const IDEMPOTENCY_HEADER = 'Idempotency-Key';
/** Cabecera de respuesta cuando la API devolvió una reserva ya creada con la misma llave. */
export const IDEMPOTENT_REPLAYED_HEADER = 'Idempotent-Replayed';
/** Prefijo de todas las rutas de la tienda. */
export const STOREFRONT_PREFIX = '/storefront/v1';
