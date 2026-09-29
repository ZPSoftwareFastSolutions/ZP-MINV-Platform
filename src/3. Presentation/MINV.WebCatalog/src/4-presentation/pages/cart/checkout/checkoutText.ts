// Textos de la pantalla de reserva (sin React), en la voz de la tienda (voseo suave): la lista «¿Cuándo pasás a
// recogerlo?», el aviso del correo y cómo se enuncia un faltante o un ajuste.

import { DOCUMENT_TYPES, documentType, parseDocumentType } from '@/1-domain/account/documents';
import { holdDayChoices, hoursForHoldDays } from '@/1-domain/storefront/policy';
import type { ReservationPolicy, StockShortage } from '@/1-domain/storefront/types';
import type { CheckoutAdjustment } from '@/2-application';
import type { SelectOption } from '@/4-presentation/components/ui/TextField';
import { pluralize } from '@/shared/format';

/** «Mañana (te lo guardamos 24 h)» · «En 2 días (48 h)» · «En 3 días (72 h)». */
export function holdDayLabel(days: number): string {
  const hours = hoursForHoldDays(days);
  return days === 1 ? `Mañana (te lo guardamos ${hours} h)` : `En ${days} días (${hours} h)`;
}

/** Opciones de la lista desplegable, acotadas por los días que permite la tienda. */
export function holdDayOptions(policy: ReservationPolicy): SelectOption[] {
  return holdDayChoices(policy).map((days) => ({ value: String(days), label: holdDayLabel(days) }));
}

/** Tipos de documento para la factura, con la opción de no pedirla. */
export const DOCUMENT_OPTIONS: readonly SelectOption[] = [
  { value: '', label: 'Sin datos para la factura' },
  ...DOCUMENT_TYPES.map((type) => ({ value: String(type.code), label: type.label })),
];

/** Resumen de los datos para la factura cuando la sección está plegada («NIT 1020304050 · Tech SRL»). */
export function buyerSummary(input: { documentType: string; documentNumber: string; buyerName: string }): string {
  const type = documentType(parseDocumentType(input.documentType));
  if (!type) return '';
  return [`${type.short} ${input.documentNumber.trim()}`.trim(), input.buyerName.trim()].filter(Boolean).join(' · ');
}

/** «Pediste 3; hay 1 disponible» · «Pediste 2; no queda ninguno disponible». */
export function shortageText(shortage: Pick<StockShortage, 'requested' | 'available'>): string {
  if (shortage.available <= 0) return `Pediste ${shortage.requested}; ya no queda ninguno disponible.`;
  return `Pediste ${shortage.requested}; ${shortage.available === 1 ? 'hay 1 disponible' : `hay ${shortage.available} disponibles`}.`;
}

/** Aviso después de ajustar: «Monitor LG: de 3 a 1 · SSD Kingston: lo quitamos». */
export function checkoutAdjustmentText(changes: readonly CheckoutAdjustment[]): string {
  return changes.map((change) => (change.to === 0 ? `${change.name}: lo quitamos` : `${change.name}: de ${change.from} a ${change.to}`)).join(' · ');
}

/** «1 producto» · «3 productos». */
export function productsLabel(count: number): string {
  return pluralize(count, 'producto', 'productos');
}
