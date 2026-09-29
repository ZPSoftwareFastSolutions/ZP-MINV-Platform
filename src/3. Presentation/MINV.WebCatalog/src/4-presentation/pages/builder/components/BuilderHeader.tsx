// Cabecera del armador: título, progreso de las piezas esenciales y acciones (armado sugerido, vaciar con confirmación
// inline e imprimir el resumen). El foco acompaña a la confirmación: al pedir «Vaciar» pasa a «No» (la opción segura)
// y al resolverla vuelve a «Vaciar» o, si el armado quedó vacío, al botón de armados sugeridos.
// V6: explica que el armado se RESERVA en la tienda (las horas que publica el servidor) y, después de reservar, recuerda
// el número con su enlace.

import { Printer, Sparkles, TicketCheck, Trash2 } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import type { BuildSummary } from '@/1-domain/builder/build';
import type { SlotKey } from '@/1-domain/builder/types';
import type { Reservation } from '@/1-domain/storefront/types';
import { ROUTES } from '@/4-presentation/app/routes';
import { Button } from '@/4-presentation/components/ui/Button';
import { useReservationPolicy } from '@/4-presentation/hooks/useReservationPolicy';
import { formatMoney, pluralize } from '@/shared/format';
import { BuildProgress } from './BuildProgress';

export interface BuilderHeaderProps {
  summary: BuildSummary;
  /** Sucursal de la tienda donde se guarda y retira la reserva (de la instantánea). */
  branchName: string;
  /** Sin armados publicados, el botón «Empezar desde un armado sugerido» no se ofrece. */
  hasPresets: boolean;
  /** Hay algo que imprimir: piezas en el armado o la última reserva aceptada. */
  canPrint: boolean;
  /** Última reserva aceptada por la tienda en esta visita (el armado ya quedó vacío). */
  lastReservation: Reservation | null;
  onPickPreset: () => void;
  onClear: () => void;
  onPrint: () => void;
  onGoToStep: (slot: SlotKey) => void;
}

export function BuilderHeader({ summary, branchName, hasPresets, canPrint, lastReservation, onPickPreset, onClear, onPrint, onGoToStep }: BuilderHeaderProps) {
  const [confirmClear, setConfirmClear] = useState(false);
  const presetRef = useRef<HTMLButtonElement>(null);
  const clearRef = useRef<HTMLButtonElement>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);
  const wasConfirming = useRef(false);
  const { reservationHours } = useReservationPolicy();
  const empty = summary.lines.length === 0;
  if (confirmClear && empty) setConfirmClear(false);

  useEffect(() => {
    if (confirmClear) {
      cancelRef.current?.focus();
    } else if (wasConfirming.current) {
      const clear = clearRef.current;
      (clear && !clear.disabled ? clear : presetRef.current)?.focus();
    }
    wasConfirming.current = confirmClear;
  }, [confirmClear]);

  return (
    <header className="animate-fade-up">
      <p className="mb-2 text-xs font-semibold uppercase tracking-[0.2em] text-accent">Paso a paso</p>
      <div className="flex flex-col gap-6 lg:flex-row lg:items-end lg:justify-between">
        <div className="max-w-2xl lg:max-w-xl">
          <h1 className="text-3xl sm:text-4xl">
            Armá tu <span className="text-gradient-brand">PC</span>
          </h1>
          <p className="mt-3 text-base text-text-muted">
            Elegí una pieza por paso y mirá el total al instante. Podés empezar desde cero o cargar un armado sugerido y ajustarlo a tu gusto. Cuando
            esté listo, <span className="font-semibold text-text">reservalo</span>: te lo guardamos {reservationHours} horas en {branchName} y lo
            confirmás y pagás en la tienda.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2 lg:justify-end">
          {hasPresets && (
            <Button ref={presetRef} variant="brand" leftIcon={<Sparkles aria-hidden="true" />} onClick={onPickPreset}>
              Empezar desde un armado sugerido
            </Button>
          )}
          <Button variant="outline" leftIcon={<Printer />} disabled={!canPrint} onClick={onPrint} title="Abre el diálogo de impresión del navegador">
            Imprimir resumen
          </Button>
          {confirmClear ? (
            <div role="alert" className="flex flex-wrap items-center gap-2 rounded-xl border border-danger/40 bg-danger-soft px-3 py-1.5 text-sm text-text">
              <span>¿Vaciar el armado?</span>
              <Button ref={cancelRef} variant="ghost" onClick={() => setConfirmClear(false)}>
                No
              </Button>
              <Button
                variant="cta"
                onClick={() => {
                  onClear();
                  setConfirmClear(false);
                }}
              >
                Sí, vaciar
              </Button>
            </div>
          ) : (
            <Button ref={clearRef} variant="ghost" leftIcon={<Trash2 aria-hidden="true" />} disabled={empty} onClick={() => setConfirmClear(true)}>
              Vaciar
            </Button>
          )}
        </div>
      </div>

      {lastReservation && empty && (
        <div className="mt-6 flex flex-col gap-3 rounded-card border border-success/40 bg-success-soft p-4 text-sm sm:flex-row sm:items-center sm:justify-between" role="status">
          <p className="flex items-start gap-2 text-text">
            <TicketCheck aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-success" />
            <span>
              Tu último armado quedó reservado como <span className="font-semibold">{lastReservation.number}</span>. Podés armar otro o consultar su estado
              cuando quieras.
            </span>
          </p>
          <Button to={ROUTES.reservation(lastReservation.number)} variant="outline" className="shrink-0">
            Consultar mi reserva
          </Button>
        </div>
      )}

      <div className="mt-8 rounded-card border border-border bg-surface p-4 shadow-card sm:p-5">
        <div className="grid gap-4 md:grid-cols-[minmax(0,1fr)_auto] md:items-center">
          <BuildProgress progress={summary.progress} />
          <dl className="flex items-center gap-6 text-sm md:border-l md:border-border md:pl-6">
            <div>
              <dt className="text-text-muted">Piezas</dt>
              <dd className="font-display text-lg font-semibold text-text tabular-nums">{pluralize(summary.count, 'pieza', 'piezas')}</dd>
            </div>
            <div>
              <dt className="text-text-muted">Total</dt>
              <dd className="font-display text-lg font-semibold text-text tabular-nums">{formatMoney(summary.total)}</dd>
            </div>
          </dl>
        </div>
        {summary.missing.length > 0 ? (
          <div className="mt-4 flex flex-wrap items-center gap-2 text-sm">
            <span className="text-text-muted">Te falta elegir:</span>
            <ul className="flex flex-wrap gap-2" aria-label="Piezas esenciales pendientes">
              {summary.missing.map((slot) => (
                <li key={slot.key}>
                  <button
                    type="button"
                    onClick={() => onGoToStep(slot.key)}
                    aria-label={`${slot.label}: ir al paso`}
                    className="inline-flex h-11 cursor-pointer items-center rounded-full border border-warning/40 bg-warning-soft px-3 text-sm font-medium text-warning-text transition-colors duration-200 hover:border-warning"
                  >
                    {slot.label}
                  </button>
                </li>
              ))}
            </ul>
          </div>
        ) : (
          <p className="mt-4 text-sm font-medium text-success-text">Tu armado tiene todas las piezas esenciales. Sumá extras o reservalo cuando quieras.</p>
        )}
      </div>
    </header>
  );
}
