// Rutas del sitio en un solo lugar: las páginas y componentes construyen enlaces con estas funciones.

export const ROUTES = {
  home: '/',
  catalog: '/catalogo',
  category: (slug: string) => `/catalogo/${slug}`,
  product: (slug: string) => `/producto/${slug}`,
  builder: '/arma-tu-pc',
  presets: '/arma-tu-pc#armados',
  offers: '/catalogo?tags=oferta',
  newArrivals: '/catalogo?tags=nuevo',
  search: (q: string) => (q.trim() ? `/catalogo?q=${encodeURIComponent(q.trim())}` : '/catalogo'),
} as const;

/** Nombre del parámetro de búsqueda en /catalogo. */
export const SEARCH_PARAM = 'q';
