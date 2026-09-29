// Módulo «Caja» · el turno de caja (funciones puras): el resumen del turno abierto, el arqueo al cerrar (efectivo
// contado frente al esperado) y el fondo inicial. El esperado y la diferencia que valen son los del servidor
// (`ClosePosSessionCommand` devuelve la diferencia): aquí solo se muestran antes de confirmar.

import { formatMoney, formatNumber, formatTime, roundTo } from '@/4-presentation/panel/lib';
import type { PosSessionData, PosStateData } from './types';

/** Montos sugeridos para el fondo inicial (los del escritorio). */
export const OPENING_SUGGESTIONS: readonly number[] = [0, 100, 200, 500, 1000];

/** «1 venta» · «12 ventas». */
export function plural(count: number, one: string, many: string): string {
  return `${formatNumber(count)} ${count === 1 ? one : many}`;
}

/** «CAJA-CB-01 · Caja 1 Cochabamba» (o solo el nombre si ya lleva el código). */
export function registerLabel(session: Pick<PosSessionData, 'registerCode' | 'registerName'>): string {
  return session.registerName.includes(session.registerCode) ? session.registerName : `${session.registerName} (${session.registerCode})`;
}

/** «Abierta a las 08:05 · 12 ventas por Bs 3.450,00 · efectivo esperado Bs 1.250,00». */
export function sessionSummary(session: PosSessionData): string {
  return `Abierta a las ${formatTime(session.openedAt)} · ${plural(session.tickets, 'venta', 'ventas')} por ${formatMoney(session.sales)} · efectivo esperado ${formatMoney(session.expectedCash)}`;
}

/** La caja que la pantalla preselecciona al abrir: la sugerida por el servidor (la libre de la sucursal activa). */
export function suggestedRegister(state: PosStateData | undefined): string {
  if (!state) return '';
  const known = (code: string | null | undefined) => (code && state.registers.some((register) => register.code === code) ? code : null);
  return known(state.session?.registerCode) ?? known(state.suggestedRegister) ?? state.registers[0]?.code ?? '';
}

export interface CashDifference {
  kind: 'exacto' | 'sobrante' | 'faltante';
  amount: number;
  text: string;
  tone: 'success' | 'warning' | 'danger';
}

/** Diferencia del arqueo: contado − esperado (positiva = sobra efectivo). */
export function cashDifference(counted: number, expected: number): CashDifference {
  const difference = roundTo(counted - expected, 2);
  if (difference === 0) return { kind: 'exacto', amount: 0, text: 'Arqueo exacto: el efectivo cuadra.', tone: 'success' };
  if (difference > 0) return { kind: 'sobrante', amount: difference, text: `Sobrante de ${formatMoney(difference)}.`, tone: 'warning' };
  return { kind: 'faltante', amount: -difference, text: `Faltante de ${formatMoney(-difference)}.`, tone: 'danger' };
}

/** Aviso después de cerrar, con la diferencia que calculó el servidor. */
export function closedNotice(difference: number): { tone: 'success' | 'warning'; description: string } {
  const result = cashDifference(difference, 0);
  return { tone: result.kind === 'exacto' ? 'success' : 'warning', description: result.text };
}
