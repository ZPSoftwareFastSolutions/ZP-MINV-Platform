// Paso numerado del armador (acordeón): cabecera con estado y pieza elegida; al expandir, el consejo de la ranura, las
// piezas elegidas (con cantidad y quitar) y los candidatos filtrables. «Omitir este paso» solo en las opcionales.

import clsx from 'clsx';
import { Check, ChevronDown, Lightbulb, Minus, RotateCcw, SkipForward } from 'lucide-react';
import { useId } from 'react';
import type { BuildLine, BuildSlot } from '@/1-domain/builder/types';
import type { Product } from '@/1-domain/catalog/types';
import { ProductCardCompact } from '@/4-presentation/components/product/ProductCardCompact';
import { Badge } from '@/4-presentation/components/ui/Badge';
import { Button } from '@/4-presentation/components/ui/Button';
import { CategoryIcon } from '@/4-presentation/components/ui/CategoryIcon';
import { ProductImage } from '@/4-presentation/components/ui/ProductImage';
import { formatMoney, pluralize } from '@/shared/format';
import type { StepStatus } from '../builderSteps';
import { SlotCandidates } from './SlotCandidates';

export interface SlotStepProps {
  slot: BuildSlot;
  lines: readonly BuildLine[];
  subtotal: number;
  status: StepStatus;
  expanded: boolean;
  onToggle: () => void;
  onSkip: () => void;
  /** Deshace «Omitir este paso»: la ranura vuelve a estar pendiente. */
  onUnskip: () => void;
  onChoose: (product: Product) => void;
  onRemove: (sku: string) => void;
  onQuantity: (sku: string, quantity: number) => void;
  /** Referencia al botón de la cabecera para mover el foco entre pasos. */
  headerRef: (element: HTMLButtonElement | null) => void;
}

const STATUS_TEXT: Record<StepStatus, string> = { elegido: 'Elegida', omitido: 'Omitido', pendiente: 'Sin elegir' };

export function SlotStep({ slot, lines, subtotal, status, expanded, onToggle, onSkip, onUnskip, onChoose, onRemove, onQuantity, headerRef }: SlotStepProps) {
  const panelId = useId();
  const headerId = useId();
  const first = lines[0];
  const count = lines.reduce((acc, line) => acc + line.quantity, 0);
  const summaryText =
    status === 'elegido' && first
      ? lines.length === 1
        ? `${first.product.shortName}${first.quantity > 1 ? ` × ${first.quantity}` : ''}`
        : `${pluralize(count, 'pieza', 'piezas')}: ${lines.map((line) => line.product.shortName).join(', ')}`
      : STATUS_TEXT[status];

  return (
    <li
      id={`paso-${slot.key}`}
      className={clsx(
        'scroll-mt-32 rounded-card border bg-surface shadow-card transition-[border-color,box-shadow] duration-200',
        expanded ? 'border-border-strong shadow-glow' : status === 'elegido' ? 'border-accent/40' : 'border-border',
      )}
    >
      <h3 className="m-0">
        <button
          ref={headerRef}
          type="button"
          id={headerId}
          aria-expanded={expanded}
          aria-controls={panelId}
          onClick={onToggle}
          className="flex w-full cursor-pointer items-center gap-3 rounded-card p-3 text-left transition-colors duration-200 hover:bg-surface-2/60 sm:gap-4 sm:p-4"
        >
          <span
            aria-hidden="true"
            className={clsx(
              'flex size-11 shrink-0 items-center justify-center rounded-xl font-display text-base font-bold tabular-nums transition-colors duration-200',
              status === 'elegido' && 'bg-linear-to-br from-primary to-accent text-white',
              status === 'pendiente' && (expanded ? 'bg-primary-soft text-primary-text ring-1 ring-primary/60' : 'bg-surface-2 text-text-muted ring-1 ring-border'),
              status === 'omitido' && 'bg-surface-2 text-text-faint ring-1 ring-border',
            )}
          >
            {status === 'elegido' ? <Check className="size-5" /> : status === 'omitido' ? <Minus className="size-5" /> : slot.order}
          </span>

          <span className="min-w-0 flex-1">
            <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
              <span className="font-display text-base font-semibold text-text sm:text-lg">{slot.label}</span>
              <Badge size="sm" tone={slot.required ? 'destacado' : 'neutral'} variant="outline">
                {slot.required ? 'Esencial' : 'Opcional'}
              </Badge>
              {slot.multiple && (
                <span className="text-xs text-text-faint">
                  <span className="sr-only">Admite </span>varias piezas
                </span>
              )}
            </span>
            <span
              className={clsx(
                'mt-0.5 block truncate text-sm',
                status === 'elegido' ? 'text-text-muted' : status === 'omitido' ? 'text-text-faint italic' : 'text-text-faint',
              )}
            >
              <span className="sr-only">Estado: </span>
              {summaryText}
            </span>
          </span>

          {status === 'elegido' && first && (
            <span className="hidden items-center gap-3 sm:flex">
              <ProductImage src={first.product.image} alt="" padding="sm" className="w-12 shrink-0" />
              <span className="font-display text-base font-semibold text-text tabular-nums">{formatMoney(subtotal)}</span>
            </span>
          )}
          {status === 'elegido' && first && <span className="font-display text-sm font-semibold text-text tabular-nums sm:hidden">{formatMoney(subtotal)}</span>}
          <ChevronDown aria-hidden="true" className={clsx('size-5 shrink-0 text-text-muted transition-transform duration-200', expanded && 'rotate-180')} />
        </button>
      </h3>

      {expanded && (
        <div id={panelId} role="region" aria-labelledby={headerId} className="border-t border-border p-3 sm:p-4">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <p className="flex items-start gap-2 text-sm text-text-muted">
              <Lightbulb aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-warning" />
              <span>{slot.hint}</span>
            </p>
            {!slot.required && lines.length === 0 && status !== 'omitido' && (
              <Button variant="ghost" leftIcon={<SkipForward />} onClick={onSkip} className="shrink-0 self-start">
                Omitir este paso
              </Button>
            )}
            {status === 'omitido' && (
              <Button variant="ghost" leftIcon={<RotateCcw />} onClick={onUnskip} className="shrink-0 self-start">
                Volver a considerar
              </Button>
            )}
          </div>

          {lines.length > 0 && (
            <div className="mt-4">
              <p className="mb-2 flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-accent">
                <CategoryIcon name={slot.icon} className="size-4" />
                {slot.multiple ? 'En tu armado' : 'Tu elección'}
              </p>
              <ul className="space-y-2">
                {lines.map((line) => (
                  <li key={line.product.sku}>
                    <ProductCardCompact
                      product={line.product}
                      selected
                      quantity={line.quantity}
                      onQuantityChange={slot.multiple ? (quantity) => onQuantity(line.product.sku, quantity) : undefined}
                      onRemove={() => onRemove(line.product.sku)}
                      showLineTotal
                    />
                  </li>
                ))}
              </ul>
            </div>
          )}

          <div className="mt-5">
            <p className="mb-3 text-xs font-semibold uppercase tracking-wide text-text-faint">
              {lines.length > 0 ? (slot.multiple ? 'Sumá más opciones' : 'Cambiar por otra opción') : 'Elegí una opción'}
            </p>
            <SlotCandidates slot={slot} lines={lines} onChoose={onChoose} onRemove={onRemove} onQuantity={onQuantity} />
          </div>
        </div>
      )}
    </li>
  );
}
