// Textos y enlaces de una reserva (sin React): fecha y hora en español, nombre de la sucursal de retiro y el enlace de
// WhatsApp con el número ya escrito. Los comparten la confirmación del armador y «Consultar mi reserva».

import type { Reservation, StoreInfo } from '@/1-domain/storefront/types';
import { STORE } from '@/shared/constants';

/** Fecha y hora en español de Bolivia («29 de septiembre de 2026, 14:39»). */
export function formatDateTime(date: Date): string {
  if (Number.isNaN(date.getTime())) return '—';
  return new Intl.DateTimeFormat('es-BO', { dateStyle: 'long', timeStyle: 'short' }).format(date);
}

/** Nombre de la sucursal de retiro (la de la tienda); si el código no coincide, el código tal cual. */
export function branchName(reservation: Pick<Reservation, 'branch'>, store?: StoreInfo): string {
  if (!store) return reservation.branch;
  if (store.branch.code === reservation.branch) return store.branch.name;
  return store.company.branches.find((branch) => branch.code === reservation.branch)?.name ?? reservation.branch;
}

/** Enlace de WhatsApp a la tienda con el número de la reserva ya escrito. */
export function whatsappReservationUrl(reservation: Pick<Reservation, 'number'>): string {
  const text = `Hola, hice la reserva ${reservation.number} desde la tienda web y quiero coordinar el retiro.`;
  return `${STORE.whatsappUrl}?text=${encodeURIComponent(text)}`;
}
