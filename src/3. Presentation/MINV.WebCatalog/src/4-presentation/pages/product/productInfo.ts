// Reglas de presentación de la ficha de producto (sin React): qué especificaciones hablan de compatibilidad y cómo se
// enuncia la garantía. Este catálogo NO valida compatibilidad: solo muestra los datos para que el cliente los compare.

import type { Product, Spec } from '@/1-domain/catalog/types';

/**
 * Claves de especificación que describen compatibilidad física, eléctrica o de plataforma, en el orden en que se
 * muestran. Solo se enseñan las que el producto realmente trae.
 */
export const COMPATIBILITY_KEYS: readonly string[] = [
  'socket',
  'sockets_compatibles',
  'chipset',
  'tipo_ram',
  'memoria_soportada',
  'formato_modulo',
  'formato',
  'formatos_placa',
  'formato_fuente',
  'formato_unidad',
  'interfaz',
  'interfaz_pcie',
  'pcie_gpu',
  'ranuras_ram',
  'ram_max',
  'ranuras_m2',
  'potencia',
  'fuente_recomendada',
  'consumo',
  'tdp',
  'tdp_soportado',
  'conector_12v2x6',
  'conectores_energia',
  'norma_atx',
  'largo',
  'largo_max_gpu',
  'altura',
  'altura_max_disipador',
  'tamano_radiador',
  'radiador_max',
  'ranuras_ocupadas',
  'disipador_incluido',
  'plataformas',
  'plataforma',
  'conexion',
  'conexiones',
  'salidas_video',
];

const ORDER = new Map(COMPATIBILITY_KEYS.map((key, index) => [key, index]));

/** Especificaciones de compatibilidad del producto, ordenadas como `COMPATIBILITY_KEYS`. Vacío si no tiene ninguna. */
export function compatibilitySpecs(product: Pick<Product, 'specs'>): Spec[] {
  return product.specs
    .filter((spec) => ORDER.has(spec.key))
    .sort((a, b) => (ORDER.get(a.key) ?? 0) - (ORDER.get(b.key) ?? 0));
}

/** «36 meses de garantía oficial» · «1 mes de garantía oficial» · «Sin garantía del fabricante». */
export function warrantyLabel(months: number): string {
  if (!Number.isFinite(months) || months <= 0) return 'Sin garantía del fabricante';
  if (months === 1) return '1 mes de garantía oficial';
  return `${months} meses de garantía oficial`;
}
