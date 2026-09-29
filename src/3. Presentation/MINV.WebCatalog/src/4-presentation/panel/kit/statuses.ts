// Estados con nombre y color para `StatusBadge`. Cada módulo declara los suyos UNA vez y los reutiliza en la tabla, las
// tarjetas, el detalle y las listas desplegables de filtros:
//
//   export const ESTADOS_VENTA = defineStatuses({
//     Paid: { label: 'Pagada', tone: 'success' },
//     Voided: { label: 'Anulada', tone: 'danger' },
//     Pending: { label: 'Pendiente', tone: 'warning' },
//   });
//   <StatusBadge status={venta.status} statuses={ESTADOS_VENTA} />
//   <SelectField label="Estado" options={statusOptions(ESTADOS_VENTA)} … />

/** Color de un estado: neutro (gris), información (cian), éxito (verde), aviso (ámbar), peligro (rojo), marca (violeta). */
export type StatusTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger' | 'accent';

export interface StatusDefinition {
  label: string;
  tone: StatusTone;
}

export type StatusMap<K extends string = string> = Readonly<Record<K, StatusDefinition>>;

/** Declara los estados de un módulo (solo tipa el objeto). */
export function defineStatuses<K extends string>(map: Record<K, StatusDefinition>): StatusMap<K> {
  return map;
}

/** El estado de un código; uno desconocido se muestra tal cual, en gris. */
export function statusOf(map: StatusMap, code: string | null | undefined): StatusDefinition {
  if (!code) return { label: '—', tone: 'neutral' };
  return Object.prototype.hasOwnProperty.call(map, code) ? map[code] : { label: code, tone: 'neutral' };
}

/** Opciones de una lista desplegable a partir de los estados (en el orden en que se declararon). */
export function statusOptions<K extends string>(map: StatusMap<K>): { value: K; label: string }[] {
  return (Object.keys(map) as K[]).map((value) => ({ value, label: map[value].label }));
}
