// Disponibilidad fresca de una ficha (regla S-07): al abrir el producto se consulta `GET /products/{slug}` y, mientras
// llega, se muestra el de la instantánea. Si la consulta falla o el producto ya no está publicado, se conserva el de
// la instantánea (la ficha nunca se queda en blanco). El estado solo cambia cuando responde la consulta (nada de
// setState síncrono en el efecto): «refrescando» se deriva de si la respuesta guardada es la de este slug.

import { useEffect, useState } from 'react';
import type { Product } from '@/1-domain/catalog/types';
import { useServices } from './useServices';

export interface FreshProduct {
  product: Product;
  /** La consulta fresca está en curso. */
  refreshing: boolean;
  /** `product` ya trae la disponibilidad recién consultada. */
  fresh: boolean;
}

interface FreshResult {
  slug: string;
  /** null: la API no lo devolvió (404 o falla); se conserva el de la instantánea. */
  product: Product | null;
  /** Instantánea con la que se consultó: si llega otra, se vuelve a preguntar. */
  generatedAt: number;
}

export function useFreshProduct(product: Product): FreshProduct {
  const { live, generatedAt } = useServices();
  const snapshotAt = generatedAt.getTime();
  const [result, setResult] = useState<FreshResult | null>(null);

  useEffect(() => {
    let active = true;
    const slug = product.slug;
    live
      .product(slug)
      .then((fresh) => {
        if (active) setResult({ slug, product: fresh ?? null, generatedAt: snapshotAt });
      })
      .catch(() => {
        if (active) setResult({ slug, product: null, generatedAt: snapshotAt });
      });
    return () => {
      active = false;
    };
  }, [live, product.slug, snapshotAt]);

  const current = result && result.slug === product.slug && result.generatedAt === snapshotAt ? result : null;
  return { product: current?.product ?? product, refreshing: current === null, fresh: current?.product != null };
}
