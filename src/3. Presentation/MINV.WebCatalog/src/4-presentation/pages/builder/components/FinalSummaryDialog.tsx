// Modal de resumen final: número de armado ficticio, lista completa por ranura, total con IVA y el aviso de demostración.
// No hay compra ni envío: es la pantalla de cierre visual del flujo «Armá tu PC».

import { BadgeCheck, Printer } from 'lucide-react';
import type { BuildSummary } from '@/1-domain/builder/build';
import { ivaBreakdown } from '@/1-domain/catalog/money';
import { ROUTES } from '@/4-presentation/app/routes';
import { Button } from '@/4-presentation/components/ui/Button';
import { ProductImage } from '@/4-presentation/components/ui/ProductImage';
import { STORE } from '@/shared/constants';
import { formatMoney, pluralize } from '@/shared/format';
import { formatLongDate } from '../builderSteps';
import { BuilderDialog } from './BuilderDialog';

export interface FinalSummaryDialogProps {
  open: boolean;
  onClose: () => void;
  summary: BuildSummary;
  /** Número ficticio generado en memoria al abrir («ARM-WEB-000123»). */
  buildNumber: string;
  issuedAt: Date;
  onPrint: () => void;
  /** Foco de reserva al cerrar (cuando se abrió desde la hoja de móvil). */
  fallbackFocus?: () => HTMLElement | null | undefined;
}

export function FinalSummaryDialog({ open, onClose, summary, buildNumber, issuedAt, onPrint, fallbackFocus }: FinalSummaryDialogProps) {
  const breakdown = ivaBreakdown(summary.total);
  const filledSlots = summary.slots.filter((entry) => entry.lines.length > 0);

  return (
    <BuilderDialog
      open={open}
      onClose={onClose}
      fallbackFocus={fallbackFocus}
      size="lg"
      title="Resumen de tu armado"
      description={`${buildNumber} · ${formatLongDate(issuedAt)}`}
      footer={
        <div className="flex flex-col gap-3">
          <div className="flex items-end justify-between gap-3">
            <div>
              <p className="text-sm text-text-muted">Total ({pluralize(summary.count, 'pieza', 'piezas')})</p>
              <p className="text-xs text-text-faint">
                IVA incluido: {formatMoney(breakdown.iva)} · Neto: {formatMoney(breakdown.net)}
              </p>
            </div>
            <p className="font-display text-3xl font-semibold leading-none text-text tabular-nums">{formatMoney(summary.total)}</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" leftIcon={<Printer />} onClick={onPrint}>
              Imprimir
            </Button>
            <Button to={ROUTES.catalog} variant="brand" className="flex-1">
              Seguir en el catálogo
            </Button>
          </div>
        </div>
      }
    >
      <div className="space-y-5">
        <div className="flex items-start gap-3 rounded-xl border border-success/40 bg-success-soft p-3">
          <BadgeCheck aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-success" />
          <div className="text-sm">
            <p className="font-semibold text-success-text">{summary.progress.complete ? 'Tu armado tiene todas las piezas esenciales.' : 'Guardamos este resumen solo en esta pantalla.'}</p>
            <p className="mt-0.5 text-text-muted">
              Este es un sitio de demostración: no se realizan compras. Para cotizar de verdad, escribinos a {STORE.email} o visitanos en {STORE.city}.
            </p>
          </div>
        </div>

        <table className="w-full text-sm">
          <caption className="sr-only">Piezas del armado {buildNumber}</caption>
          <thead>
            <tr className="text-left text-[0.6875rem] font-semibold uppercase tracking-wide text-text-faint">
              <th scope="col" className="pb-2 font-semibold">
                Pieza
              </th>
              <th scope="col" className="pb-2 text-center font-semibold">
                Cant.
              </th>
              <th scope="col" className="pb-2 text-right font-semibold">
                Subtotal
              </th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {filledSlots.flatMap(({ slot, lines }) =>
              lines.map((line) => (
                <tr key={line.product.sku}>
                  <td className="py-2.5 pr-3">
                    <div className="flex items-center gap-3">
                      <ProductImage src={line.product.image} alt="" padding="sm" className="w-10 shrink-0" />
                      <div className="min-w-0">
                        <p className="text-[0.6875rem] font-semibold uppercase tracking-wide text-text-faint">{slot.label}</p>
                        <p className="line-clamp-2 leading-snug text-text">{line.product.shortName}</p>
                      </div>
                    </div>
                  </td>
                  <td className="py-2.5 text-center text-text-muted tabular-nums">{line.quantity}</td>
                  <td className="py-2.5 text-right font-medium text-text tabular-nums">{formatMoney(line.product.price * line.quantity)}</td>
                </tr>
              )),
            )}
          </tbody>
        </table>

        {summary.missing.length > 0 && (
          <p className="text-sm text-warning-text">
            Piezas esenciales pendientes: {summary.missing.map((slot) => slot.label).join(', ')}.
          </p>
        )}
      </div>
    </BuilderDialog>
  );
}
