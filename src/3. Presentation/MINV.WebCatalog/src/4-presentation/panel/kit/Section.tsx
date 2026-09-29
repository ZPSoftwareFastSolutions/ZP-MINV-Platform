// USO · Bloques de contenido del panel.
//
// Section: tarjeta con título, descripción y botones (formularios, bloques de un detalle).
//   <Section title="Datos del cliente" description="Se usan en la factura." actions={<Button variant="outline">Editar</Button>}>…</Section>
//
// DetailList: pares «etiqueta: valor» de un registro (en el panel lateral o en una sección). Vacíos → «—».
//   <DetailList items={[{ label: 'Número', value: venta.number }, { label: 'Total', value: formatMoney(venta.total) },
//                       { label: 'Notas', value: venta.notes, wide: true }]} />

import clsx from 'clsx';
import { useId, type ReactNode } from 'react';
import { EMPTY_VALUE } from '../lib/format';

export interface SectionProps {
  title: string;
  description?: ReactNode;
  actions?: ReactNode;
  /** Nivel del título (2 dentro de una Page; 3 dentro de otra sección). */
  level?: 2 | 3;
  /** Id del bloque (para enlazarlo con `#id`). */
  id?: string;
  children?: ReactNode;
  className?: string;
}

export function Section({ title, description, actions, level = 2, id, children, className }: SectionProps) {
  const titleId = useId();
  const Heading = level === 2 ? 'h2' : 'h3';
  return (
    <section id={id} aria-labelledby={titleId} className={clsx('min-w-0 rounded-card border border-border bg-surface p-4 shadow-card sm:p-5', className)}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <Heading id={titleId} className="text-lg font-semibold">
            {title}
          </Heading>
          {description && <div className="mt-0.5 text-sm text-text-muted">{description}</div>}
        </div>
        {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
      </div>
      {children != null && <div className="mt-4">{children}</div>}
    </section>
  );
}

export interface DetailItem {
  label: string;
  value: ReactNode;
  /** Ocupa todo el ancho (textos largos). */
  wide?: boolean;
}

export interface DetailListProps {
  items: readonly DetailItem[];
  /** Columnas desde 640 px (1 o 2; por defecto 2). */
  columns?: 1 | 2;
  className?: string;
}

function isBlank(value: ReactNode): boolean {
  return value == null || value === false || (typeof value === 'string' && value.trim() === '');
}

export function DetailList({ items, columns = 2, className }: DetailListProps) {
  return (
    <dl className={clsx('grid grid-cols-1 gap-x-6 gap-y-3', columns === 2 && 'sm:grid-cols-2', className)}>
      {items.map((item) => (
        <div key={item.label} className={clsx('min-w-0', item.wide && 'sm:col-span-2')}>
          <dt className="text-xs font-semibold uppercase tracking-wide text-text-faint">{item.label}</dt>
          <dd className="mt-0.5 break-words text-sm text-text">{isBlank(item.value) ? EMPTY_VALUE : item.value}</dd>
        </div>
      ))}
    </dl>
  );
}
