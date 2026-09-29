// Fachada del dominio: tipos y reglas puras (sin React, sin infraestructura).

export type * from './catalog/types';
export type * from './builder/types';
export type * from './storefront/types';
export type { ICatalogRepository } from './ports/ICatalogRepository';
export type { ICatalogSource } from './ports/ICatalogSource';
export type { IReservationGateway } from './ports/IReservationGateway';
export type * from './auth/types';
export type * from './account/types';
export type { ISessionGateway } from './ports/ISessionGateway';
export type { IRpcGateway, RpcOperationMap, RpcOperationShape, RpcOutcome, RpcSendOptions } from './ports/IRpcGateway';
export type { IAccountGateway } from './ports/IAccountGateway';

export * from './catalog/money';
export * from './catalog/categories';
export * from './catalog/products';
export * from './catalog/stock';
export * from './builder/slots';
export * from './builder/build';
export { RESERVATION_HOURS, RESERVATION_LIMITS, isReservationActive } from './storefront/types';
export * from './storefront/errors';
export * from './storefront/contact';
export * from './auth/permissions';
export * from './auth/errors';
export * from './auth/validation';
export * from './account/documents';
export * from './account/validation';
