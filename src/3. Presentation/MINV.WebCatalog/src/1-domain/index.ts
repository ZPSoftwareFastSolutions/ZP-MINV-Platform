// Fachada del dominio: tipos y reglas puras (sin React, sin infraestructura).

export type * from './catalog/types';
export type * from './builder/types';
export type { ICatalogRepository } from './ports/ICatalogRepository';

export * from './catalog/money';
export * from './catalog/categories';
export * from './catalog/products';
export * from './catalog/stock';
export * from './builder/slots';
export * from './builder/build';
