// USO · Gráficos simples del panel (sin bibliotecas: CSS y SVG propios, con los colores del tema). Siempre con texto
// alternativo. Regla P-10: van dentro de un Collapsible «Ver …».
//
// BarList: barras horizontales con su etiqueta y su valor (productos más vendidos, ventas por sucursal). Los números
// están escritos al lado de cada barra: la barra solo ayuda a comparar.
//   <BarList label="Productos más vendidos (unidades)" items={top.map((p) => ({ label: p.name, value: p.units }))} />
//   <BarList label="Ventas por sucursal" format={formatMoney} tone="accent" items={…} />
//
// MiniBars: columnas pequeñas en SVG para una serie en el tiempo (ventas de los últimos días). `label` es el texto
// alternativo obligatorio; el resumen (máximo, mínimo y total) y la tabla de valores quedan para lectores de pantalla.
//   <MiniBars label="Ventas de los últimos 7 días" format={formatMoney}
//     points={dias.map((d) => ({ label: formatDate(d.day), value: d.total }))} showAxis />

import clsx from 'clsx';
import { useId } from 'react';
import { formatNumber } from '../lib/format';

export type ChartTone = 'primary' | 'accent' | 'success' | 'warning' | 'danger';

const BAR_TONES: Record<ChartTone, string> = {
  primary: 'bg-primary',
  accent: 'bg-accent',
  success: 'bg-success',
  warning: 'bg-warning',
  danger: 'bg-danger',
};

const FILL_TONES: Record<ChartTone, string> = {
  primary: 'fill-primary',
  accent: 'fill-accent',
  success: 'fill-success',
  warning: 'fill-warning',
  danger: 'fill-danger',
};

const defaultFormat = (value: number) => formatNumber(value, { maxDecimals: 2 });

export interface BarListItem {
  label: string;
  value: number;
  /** Segunda línea (SKU, cantidad de ventas). */
  hint?: string;
}

export interface BarListProps {
  /** Título del gráfico (visible). */
  label: string;
  items: readonly BarListItem[];
  /** Formato del valor (por defecto número con separadores). */
  format?: (value: number) => string;
  /** Valor de la barra llena (por defecto el mayor). */
  max?: number;
  tone?: ChartTone;
  emptyText?: string;
  className?: string;
}

export function BarList({ label, items, format = defaultFormat, max, tone = 'primary', emptyText = 'Sin datos para mostrar.', className }: BarListProps) {
  const titleId = useId();
  const top = max ?? Math.max(0, ...items.map((item) => item.value));
  return (
    <figure aria-labelledby={titleId} className={clsx('min-w-0', className)}>
      <figcaption id={titleId} className="mb-3 text-sm font-semibold text-text">
        {label}
      </figcaption>
      {items.length === 0 ? (
        <p className="text-sm text-text-muted">{emptyText}</p>
      ) : (
        <ul className="space-y-3">
          {items.map((item, index) => {
            const percent = top > 0 ? Math.max(0, Math.min(100, (item.value / top) * 100)) : 0;
            return (
              <li key={`${item.label}-${index}`} className="min-w-0">
                <div className="flex items-baseline justify-between gap-3 text-sm">
                  <span className="min-w-0 truncate text-text" title={item.label}>
                    {item.label}
                    {item.hint && <span className="ml-2 text-xs text-text-faint">{item.hint}</span>}
                  </span>
                  <span className="shrink-0 font-semibold text-text tabular-nums">{format(item.value)}</span>
                </div>
                <div aria-hidden="true" className="mt-1 h-2 overflow-hidden rounded-full bg-surface-3">
                  <div className={clsx('h-full rounded-full', BAR_TONES[tone])} style={{ width: `${percent}%` }} />
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </figure>
  );
}

export interface MiniBarsPoint {
  /** Etiqueta del punto («28/09»). */
  label: string;
  value: number;
}

export interface MiniBarsProps {
  /** Texto alternativo obligatorio: qué muestra («Ventas de los últimos 7 días»). */
  label: string;
  points: readonly MiniBarsPoint[];
  format?: (value: number) => string;
  /** Alto en píxeles (64 por defecto). */
  height?: number;
  tone?: ChartTone;
  /** Muestra la primera y la última etiqueta debajo. */
  showAxis?: boolean;
  className?: string;
}

/** «Máximo 5.000 (25/09) · mínimo 800 (22/09) · total 15.000». */
function summaryOf(points: readonly MiniBarsPoint[], format: (value: number) => string): string {
  if (points.length === 0) return 'Sin datos.';
  const highest = points.reduce((best, point) => (point.value > best.value ? point : best));
  const lowest = points.reduce((best, point) => (point.value < best.value ? point : best));
  const total = points.reduce((sum, point) => sum + point.value, 0);
  return `Máximo ${format(highest.value)} (${highest.label}) · mínimo ${format(lowest.value)} (${lowest.label}) · total ${format(total)}.`;
}

export function MiniBars({ label, points, format = defaultFormat, height = 64, tone = 'accent', showAxis = false, className }: MiniBarsProps) {
  const titleId = useId();
  const descriptionId = useId();
  const top = Math.max(0, ...points.map((point) => point.value));
  const width = Math.max(1, points.length) * 10;
  return (
    <figure className={clsx('min-w-0', className)}>
      <svg
        role="img"
        aria-labelledby={`${titleId} ${descriptionId}`}
        viewBox={`0 0 ${width} ${height}`}
        preserveAspectRatio="none"
        className="block w-full"
        style={{ height }}
      >
        <title id={titleId}>{label}</title>
        <desc id={descriptionId}>{summaryOf(points, format)}</desc>
        {points.map((point, index) => {
          const barHeight = top > 0 ? Math.max(point.value > 0 ? 1.5 : 0, (Math.max(0, point.value) / top) * (height - 2)) : 0;
          return (
            <rect key={`${point.label}-${index}`} x={index * 10 + 1.5} width={7} y={height - barHeight} height={barHeight} rx={1.5} className={FILL_TONES[tone]}>
              <title>{`${point.label}: ${format(point.value)}`}</title>
            </rect>
          );
        })}
      </svg>
      {showAxis && points.length > 0 && (
        <div aria-hidden="true" className="mt-1 flex justify-between text-xs text-text-faint">
          <span>{points[0].label}</span>
          {points.length > 1 && <span>{points[points.length - 1].label}</span>}
        </div>
      )}
      <table className="sr-only">
        <caption>{label}</caption>
        <tbody>
          {points.map((point, index) => (
            <tr key={`${point.label}-${index}`}>
              <th scope="row">{point.label}</th>
              <td>{format(point.value)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </figure>
  );
}
