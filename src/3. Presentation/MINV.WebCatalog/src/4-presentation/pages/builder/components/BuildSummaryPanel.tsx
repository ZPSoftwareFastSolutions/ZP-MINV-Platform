// Resumen del armado: líneas por ranura (miniatura, nombre corto, cantidad, subtotal, quitar), faltantes esenciales con
// enlace al paso, datos de referencia del catálogo, total con IVA incluido y las acciones de cierre.
// Lo comparten el panel fijo de escritorio y la hoja inferior de móvil.

import { ArrowRight, CircleAlert, Info, PcCase, Sparkles, Trash2 } from 'lucide-react';
import { Link } from 'react-router-dom';
import type { BuildSummary } from '@/1-domain/builder/build';
import type { SlotKey } from '@/1-domain/builder/types';
import { ivaBreakdown } from '@/1-domain/catalog/money';
import { ROUTES } from '@/4-presentation/app/routes';
import { Badge } from '@/4-presentation/components/ui/Badge';
import { Button } from '@/4-presentation/components/ui/Button';
import { EmptyState } from '@/4-presentation/components/ui/EmptyState';
import { IconButton } from '@/4-presentation/components/ui/IconButton';
import { ProductImage } from '@/4-presentation/components/ui/ProductImage';
import { formatMoney, pluralize } from '@/shared/format';
import { referenceData } from '../builderSteps';
import { BuildProgress } from './BuildProgress';

export interface BuildSummaryPanelProps {
  summary: BuildSummary;
  onRemove: (sku: string) => void;
  onGoToStep: (slot: SlotKey) => void;
  onFinish: () => void;
  onPickPreset: () => void;
  /** Muestra el título «Tu armado» (el panel de escritorio; la hoja de móvil ya tiene título propio). */
  withHeading?: boolean;
  /** Muestra el bloque de total y los botones (en la hoja de móvil van en el pie fijo). */
  withTotals?: boolean;
}

export function BuildSummaryPanel({ summary, onRemove, onGoToStep, onFinish, onPickPreset, withHeading = true, withTotals = true }: BuildSummaryPanelProps) {
  const { lines, count, total, savings, progress, missing } = summary;
  const filledSlots = summary.slots.filter((entry) => entry.lines.length > 0);
  const references = referenceData(lines);

  return (
    <div className="flex flex-col gap-5">
      {withHeading && (
        <div className="flex items-center justify-between gap-3">
          <h2 className="text-2xl">Tu armado</h2>
          {count > 0 && <Badge tone="nuevo">{pluralize(count, 'pieza', 'piezas')}</Badge>}
        </div>
      )}

      {lines.length === 0 ? (
        <EmptyState size="sm" icon={<PcCase />} title="Todavía no elegiste piezas" description="Abrí un paso y elegí una opción, o empezá desde un armado sugerido.">
          <Button variant="accent" leftIcon={<Sparkles />} onClick={onPickPreset}>
            Ver armados sugeridos
          </Button>
        </EmptyState>
      ) : (
        <>
          <BuildProgress progress={progress} size="sm" className="-mt-1" />
          <ul className="divide-y divide-border" aria-label="Piezas del armado">
            {filledSlots.map(({ slot, lines: slotLines, subtotal }) => (
              <li key={slot.key} className="py-1.5 first:pt-0 last:pb-0">
                <div className="mb-1 flex min-h-11 items-center justify-between gap-3 text-[0.6875rem] font-semibold uppercase tracking-wide text-text-faint">
                  <button
                    type="button"
                    onClick={() => onGoToStep(slot.key)}
                    aria-label={`${slot.order}. ${slot.label}: ir al paso`}
                    className="inline-flex min-h-11 cursor-pointer items-center rounded-sm text-left transition-colors duration-200 hover:text-accent-hover"
                  >
                    {slot.order}. {slot.label}
                  </button>
                  {slotLines.length > 1 && <span className="tabular-nums">{formatMoney(subtotal)}</span>}
                </div>
                <ul className="space-y-2">
                  {slotLines.map((line) => (
                    <li key={line.product.sku} className="flex items-center gap-3">
                      <ProductImage src={line.product.image} alt="" padding="sm" className="w-11 shrink-0" />
                      <div className="min-w-0 flex-1">
                        <p className="line-clamp-2 text-sm leading-snug text-text">
                          <Link to={ROUTES.product(line.product.slug)} className="rounded-sm transition-colors duration-200 hover:text-accent-hover">
                            {line.product.shortName}
                          </Link>
                        </p>
                        <p className="mt-0.5 text-xs text-text-muted tabular-nums">
                          {line.quantity > 1 && <span>{line.quantity} × {formatMoney(line.product.price)} · </span>}
                          <span className="font-semibold text-text">{formatMoney(line.product.price * line.quantity)}</span>
                        </p>
                      </div>
                      <IconButton label={`Quitar ${line.product.shortName} del armado`} icon={<Trash2 />} onClick={() => onRemove(line.product.sku)} />
                    </li>
                  ))}
                </ul>
              </li>
            ))}
          </ul>

          {missing.length > 0 && (
            <div className="rounded-xl border border-warning/40 bg-warning-soft p-3" role="status">
              <p className="flex items-center gap-2 text-sm font-semibold text-warning-text">
                <CircleAlert aria-hidden="true" className="size-4" />
                {pluralize(missing.length, 'pieza esencial pendiente', 'piezas esenciales pendientes')}
              </p>
              <ul className="mt-2 flex flex-wrap gap-2">
                {missing.map((slot) => (
                  <li key={slot.key}>
                    <button
                      type="button"
                      onClick={() => onGoToStep(slot.key)}
                      className="inline-flex h-11 cursor-pointer items-center gap-1 rounded-full border border-warning/50 bg-surface px-3 text-sm font-medium text-text transition-colors duration-200 hover:border-warning hover:text-warning-text"
                    >
                      {slot.label}
                      <ArrowRight aria-hidden="true" className="size-3.5" />
                    </button>
                  </li>
                ))}
              </ul>
            </div>
          )}

          {references.length > 0 && (
            <div className="rounded-xl border border-border bg-surface-2 p-3">
              <p className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-text-muted">
                <Info aria-hidden="true" className="size-4 text-accent" />
                Datos de referencia
              </p>
              <dl className="mt-2 space-y-1.5">
                {references.map((item) => (
                  <div key={item.key} className="flex items-baseline justify-between gap-3 text-sm">
                    <dt className="text-text-muted">{item.label}</dt>
                    <dd className="shrink-0 font-medium text-text tabular-nums">{item.value}</dd>
                  </div>
                ))}
              </dl>
              <p className="mt-2 text-xs text-text-faint">Tomados de la ficha de cada pieza. Este sitio no valida la compatibilidad entre componentes.</p>
            </div>
          )}
        </>
      )}

      {withTotals && lines.length > 0 && <SummaryTotals total={total} savings={savings} count={count} onFinish={onFinish} />}
    </div>
  );
}

export interface SummaryTotalsProps {
  total: number;
  savings: number;
  count: number;
  onFinish: () => void;
  /** Botones en fila (pie de la hoja de móvil). */
  compact?: boolean;
}

/** Total en Bs, IVA incluido, «Finalizar armado» y «Seguir en el catálogo». */
export function SummaryTotals({ total, savings, count, onFinish, compact = false }: SummaryTotalsProps) {
  const breakdown = ivaBreakdown(total);
  return (
    <div className={compact ? 'flex flex-col gap-3' : 'space-y-4 border-t border-border pt-4'}>
      <div className="flex items-end justify-between gap-3">
        <div>
          <p className="text-sm text-text-muted">Total ({pluralize(count, 'pieza', 'piezas')})</p>
          <p className="text-xs text-text-faint">IVA incluido ({formatMoney(breakdown.iva)})</p>
          {savings > 0 && <p className="mt-0.5 text-xs font-semibold text-cta-hover">Ahorrás {formatMoney(savings)} frente al precio de lista</p>}
        </div>
        <p className="font-display text-3xl font-semibold leading-none text-text tabular-nums">
          <span className="sr-only">Total: {formatMoney(total)}</span>
          <span aria-hidden="true">
            <span className="mr-1 text-base font-medium text-text-muted">Bs</span>
            {formatMoney(total, { symbol: '' }).trim()}
          </span>
        </p>
      </div>
      <div className={compact ? 'flex gap-2' : 'flex flex-col gap-2'}>
        <Button variant="brand" fullWidth={!compact} className={compact ? 'flex-1' : undefined} rightIcon={<ArrowRight />} disabled={count === 0} onClick={onFinish}>
          Finalizar armado
        </Button>
        <Button to={ROUTES.catalog} variant="outline" fullWidth={!compact} className={compact ? 'max-sm:hidden' : undefined}>
          Seguir en el catálogo
        </Button>
      </div>
    </div>
  );
}
