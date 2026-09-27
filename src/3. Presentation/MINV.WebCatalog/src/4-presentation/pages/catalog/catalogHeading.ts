// Encabezado del catálogo según el contexto de la URL: categoría, búsqueda libre, etiquetas u «todo el catálogo».
// Función pura (sin React) para poder probarla.

import type { Category, ProductTag } from '@/1-domain/catalog/types';
import { pluralize } from '@/shared/format';
import type { CatalogFilters } from './catalogFilters';

export interface CatalogHeading {
  /** Etiqueta pequeña sobre el título («Componentes», «Búsqueda»). */
  eyebrow?: string;
  title: string;
  subtitle?: string;
}

export interface CatalogHeadingContext {
  /** Categoría de la ruta, si el slug existe. */
  category?: Category;
  /** Raíz de esa categoría (ella misma si ya es raíz). */
  root?: Category;
  /** Total de productos que cumplen los filtros. */
  total: number;
}

const TAG_TITLES: Record<ProductTag, string> = { oferta: 'Ofertas', nuevo: 'Novedades', destacado: 'Destacados' };

const TAG_SUBTITLES: Record<ProductTag, string> = {
  oferta: 'Precios rebajados frente al precio de lista, por tiempo limitado.',
  nuevo: 'Lo último que llegó a la tienda.',
  destacado: 'Los productos que más eligen nuestros clientes.',
};

function quoted(q: string): string {
  return `“${q}”`;
}

export function catalogHeading(filters: CatalogFilters, context: CatalogHeadingContext): CatalogHeading {
  const { category, root, total } = context;
  const firstTag = filters.tags[0];

  if (category) {
    const isRoot = category.parent === null;
    let subtitle: string | undefined;
    if (filters.q) {
      subtitle = total === 0 ? `Sin coincidencias para ${quoted(filters.q)} en ${category.name}.` : `${pluralize(total, 'resultado', 'resultados')} para ${quoted(filters.q)} en ${category.name}.`;
    } else if (firstTag) {
      subtitle = TAG_SUBTITLES[firstTag];
    } else {
      subtitle = isRoot ? category.description : root?.description;
    }
    return { eyebrow: isRoot || !root ? 'Catálogo' : root.name, title: category.name, subtitle };
  }

  if (filters.q) {
    return {
      eyebrow: 'Búsqueda',
      title: `Resultados para ${quoted(filters.q)}`,
      subtitle: total === 0 ? 'No encontramos coincidencias en el catálogo.' : `${pluralize(total, 'producto coincide', 'productos coinciden')} en todo el catálogo.`,
    };
  }

  if (firstTag) {
    return {
      eyebrow: 'Catálogo',
      title: filters.tags.map((tag) => TAG_TITLES[tag]).join(' y '),
      subtitle: TAG_SUBTITLES[firstTag],
    };
  }

  return {
    eyebrow: 'Tech Zone Gaming',
    title: 'Todo el catálogo',
    subtitle: `Componentes, computadoras, consolas, periféricos y más: ${pluralize(total, 'producto', 'productos')} con garantía oficial.`,
  };
}
