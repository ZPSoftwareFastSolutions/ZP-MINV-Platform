export type PageToken = number | 'gap';

/** Páginas a mostrar con elipsis: 1 … 4 5 6 … 12. */
export function paginationRange(page: number, pageCount: number, siblingCount = 1): PageToken[] {
  const total = siblingCount * 2 + 5;
  if (pageCount <= total) return Array.from({ length: pageCount }, (_, index) => index + 1);
  const left = Math.max(2, page - siblingCount);
  const right = Math.min(pageCount - 1, page + siblingCount);
  const tokens: PageToken[] = [1];
  if (left > 2) tokens.push('gap');
  for (let current = left; current <= right; current += 1) tokens.push(current);
  if (right < pageCount - 1) tokens.push('gap');
  tokens.push(pageCount);
  return tokens;
}
