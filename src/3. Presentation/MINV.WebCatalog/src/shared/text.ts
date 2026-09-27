// Utilidades de texto puras: normalización sin acentos (para buscar) y slugs (para rutas).

/** «Refrigeración Líquida» → «refrigeracion liquida». Minúsculas, sin diacríticos, espacios colapsados. */
export function normalizeText(text: string): string {
  return text
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLowerCase()
    .replace(/\s+/g, ' ')
    .trim();
}

/** «Tarjetas de video» → «tarjetas-de-video». */
export function slugify(text: string): string {
  return normalizeText(text)
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}

/** Palabras normalizadas de una búsqueda, sin vacíos: «RTX  5070 » → ['rtx', '5070']. */
export function tokenize(text: string): string[] {
  return normalizeText(text)
    .split(' ')
    .filter((token) => token.length > 0);
}

/** Recorta un texto largo agregando «…» (para títulos en listas compactas). */
export function truncate(text: string, maxLength: number): string {
  if (text.length <= maxLength) return text;
  return `${text.slice(0, Math.max(0, maxLength - 1)).trimEnd()}…`;
}
