// Utilidades de texto puras: normalización sin acentos (para buscar) y separación en palabras.

/** «Refrigeración Líquida» → «refrigeracion liquida». Minúsculas, sin diacríticos, espacios colapsados. */
export function normalizeText(text: string): string {
  return text
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLowerCase()
    .replace(/\s+/g, ' ')
    .trim();
}

/** Palabras normalizadas de una búsqueda, sin vacíos: «RTX  5070 » → ['rtx', '5070']. */
export function tokenize(text: string): string[] {
  return normalizeText(text)
    .split(' ')
    .filter((token) => token.length > 0);
}
