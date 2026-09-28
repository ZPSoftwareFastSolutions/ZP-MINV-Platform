// Forma de los productos del mock GENERADO de la V5 (tools/generar_catalogo_web.py): igual que `Product` pero sin
// `reserved` (el mock no conoce reservas; mockCatalog.ts lo completa con 0 al hidratar).

import type { Product } from '@/1-domain/catalog/types';

export type MockProduct = Omit<Product, 'reserved'>;
