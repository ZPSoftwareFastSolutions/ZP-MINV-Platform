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
  /** «Consultar mi reserva» (V6): sin número, o con el número ya escrito (`/reserva/ARM-WEB-000001`). */
  reservations: '/reserva',
  reservation: (number: string) => `/reserva/${encodeURIComponent(number)}`,
} as const;

/** Nombre del parámetro de búsqueda en /catalogo. */
export const SEARCH_PARAM = 'q';
