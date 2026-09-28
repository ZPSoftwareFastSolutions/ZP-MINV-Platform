// Contrato de la tienda conectada (DTO + mapeo) y casos de uso de reservas. Es el único módulo de la aplicación que
// la infraestructura puede importar (el adaptador HTTP traduce el JSON de la API con estos mapeos).

export type * from './dto';
export { IDEMPOTENCY_HEADER, IDEMPOTENT_REPLAYED_HEADER, STOREFRONT_PREFIX } from './dto';
export * from './mappers';
export * from './reservations';
