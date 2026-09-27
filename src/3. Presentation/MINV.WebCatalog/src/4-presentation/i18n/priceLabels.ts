// Textos de precio de la presentación a partir de las reglas numéricas del dominio (money.ts).

import { INSTALLMENTS_MAX, installmentAmount, savingPercent } from '@/1-domain/catalog/money';
import { formatMoney, formatPercent } from '@/shared/format';

/** «-13 %» listo para una insignia de oferta, o cadena vacía si no hay ahorro. */
export function savingLabel(price: number, listPrice: number | null | undefined): string {
  const percent = savingPercent(price, listPrice);
  return percent > 0 ? `-${formatPercent(percent)}` : '';
}

/** «12 cuotas de Bs 266,58» (informativo, sin interés, con tarjetas de bancos asociados). */
export function installmentLabel(price: number, months = INSTALLMENTS_MAX): string {
  const amount = installmentAmount(price, months);
  return amount > 0 ? `${months} cuotas de ${formatMoney(amount)}` : '';
}
