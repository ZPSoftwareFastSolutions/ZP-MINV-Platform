// «Reservar armado» (V6): formulario accesible (nombre, teléfono/WhatsApp boliviano, correo opcional y notas), resumen
// de las piezas, aviso de vigencia, envío con `Idempotency-Key` (un UUID por intento), errores del contrato (409 marca
// las piezas afectadas y cuánto hay) y la confirmación con número, vencimiento, líneas y total. Estado en memoria.

import { BadgeCheck, CalendarClock, CircleAlert, LoaderCircle, Printer, Wand2 } from 'lucide-react';
import { useEffect, useId, useState, type FormEvent } from 'react';
import type { BuildSummary } from '@/1-domain/builder/build';
import { ivaBreakdown } from '@/1-domain/catalog/money';
import { toReservationContact, validateReservationForm, type ReservationFormErrors, type ReservationFormInput } from '@/1-domain/storefront/contact';
import { asStorefrontError, describeStorefrontError, type StorefrontError } from '@/1-domain/storefront/errors';
import { RESERVATION_HOURS, RESERVATION_LIMITS, type Reservation, type StockShortage } from '@/1-domain/storefront/types';
import { newIdempotencyKey } from '@/2-application';
import { ROUTES } from '@/4-presentation/app/routes';
import { ReservationSummary } from '@/4-presentation/components/reservation/ReservationSummary';
import { Button } from '@/4-presentation/components/ui/Button';
import { ProductImage } from '@/4-presentation/components/ui/ProductImage';
import { useServices } from '@/4-presentation/hooks/useServices';
import { useStore } from '@/4-presentation/hooks/useStore';
import { formatMoney, pluralize } from '@/shared/format';
import { BuilderDialog } from './BuilderDialog';

export interface ReserveDialogProps {
  open: boolean;
  onClose: () => void;
  summary: BuildSummary;
  /** La tienda aceptó la reserva: el armador vacía las piezas y guarda el número. */
  onReserved: (reservation: Reservation) => void;
  /** Ajusta el armado a lo disponible tras un 409 (baja cantidades o quita piezas sin stock). */
  onAdjust: (shortages: readonly StockShortage[]) => void;
  onPrint: () => void;
  fallbackFocus?: () => HTMLElement | null | undefined;
}

const EMPTY_FORM: ReservationFormInput = { name: '', phone: '', email: '', notes: '' };

const FIELD =
  'h-11 w-full rounded-xl border border-border-control bg-surface px-3 text-sm text-text placeholder:text-text-faint transition-colors duration-200 hover:border-border-strong focus:border-accent focus:outline-none aria-invalid:border-danger';

type Phase = { kind: 'form' } | { kind: 'sending' } | { kind: 'done'; reservation: Reservation };

export function ReserveDialog({ open, onClose, summary, onReserved, onAdjust, onPrint, fallbackFocus }: ReserveDialogProps) {
  const { reservations } = useServices();
  const store = useStore();
  const ids = { name: useId(), phone: useId(), email: useId(), notes: useId(), error: useId() };
  const [form, setForm] = useState<ReservationFormInput>(EMPTY_FORM);
  const [errors, setErrors] = useState<ReservationFormErrors>({});
  const [touched, setTouched] = useState(false);
  const [serverError, setServerError] = useState<StorefrontError | null>(null);
  const [phase, setPhase] = useState<Phase>({ kind: 'form' });

  // Al cerrar después de reservar, el diálogo vuelve a empezar limpio (el armado ya quedó vacío).
  useEffect(() => {
    if (open || phase.kind !== 'done') return;
    const timer = setTimeout(() => {
      setPhase({ kind: 'form' });
      setForm(EMPTY_FORM);
      setErrors({});
      setTouched(false);
      setServerError(null);
    }, 300);
    return () => clearTimeout(timer);
  }, [open, phase.kind]);

  const filledSlots = summary.slots.filter((entry) => entry.lines.length > 0);
  const shortagesBySku = new Map((serverError?.shortages ?? []).map((item) => [item.sku, item]));
  const breakdown = ivaBreakdown(summary.total);
  const sending = phase.kind === 'sending';

  const update = (field: keyof ReservationFormInput, value: string) => {
    setForm((current) => ({ ...current, [field]: value }));
    if (touched) setErrors(validateReservationForm({ ...form, [field]: value }));
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const validation = validateReservationForm(form);
    setTouched(true);
    setErrors(validation);
    if (Object.keys(validation).length > 0) return;
    setServerError(null);
    setPhase({ kind: 'sending' });
    try {
      const reservation = await reservations.reserve({
        lines: summary.lines,
        contact: toReservationContact(form),
        notes: form.notes.trim() || undefined,
        idempotencyKey: newIdempotencyKey(),
      });
      setPhase({ kind: 'done', reservation });
      onReserved(reservation);
    } catch (error) {
      setServerError(asStorefrontError(error));
      setPhase({ kind: 'form' });
    }
  };

  if (phase.kind === 'done') {
    return (
      <BuilderDialog
        open={open}
        onClose={onClose}
        fallbackFocus={fallbackFocus}
        size="lg"
        title="Tu armado quedó reservado"
        description={`${phase.reservation.number} · te lo guardamos ${RESERVATION_HOURS} horas en ${store.branch.name}`}
        footer={
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" leftIcon={<Printer />} onClick={onPrint}>
              Imprimir
            </Button>
            <Button to={ROUTES.catalog} variant="brand" className="flex-1" onClick={onClose}>
              Seguir en el catálogo
            </Button>
          </div>
        }
      >
        <div className="space-y-5">
          <div className="flex items-start gap-3 rounded-xl border border-success/40 bg-success-soft p-3" role="status">
            <BadgeCheck aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-success" />
            <div className="text-sm">
              <p className="font-semibold text-success-text">
                {phase.reservation.replayed ? 'Esta reserva ya estaba registrada con los mismos datos.' : 'Listo: las piezas quedaron reservadas a tu nombre.'}
              </p>
              <p className="mt-0.5 text-text-muted">
                Pasá por la tienda antes del vencimiento para confirmar y pagar. Con tu número y tu teléfono podés consultar o liberar la reserva cuando quieras.
              </p>
            </div>
          </div>
          <ReservationSummary reservation={phase.reservation} store={store} withLinks />
        </div>
      </BuilderDialog>
    );
  }

  return (
    <BuilderDialog
      open={open}
      onClose={onClose}
      fallbackFocus={fallbackFocus}
      size="lg"
      title="Reservar armado"
      description={`${pluralize(summary.count, 'pieza', 'piezas')} · ${formatMoney(summary.total)} · IVA incluido ${formatMoney(breakdown.iva)}`}
      footer={
        <div className="flex flex-col gap-3">
          <p className="flex items-start gap-2 text-sm text-text-muted">
            <CalendarClock aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-accent" />
            <span>
              Te lo guardamos <span className="font-semibold text-text">{RESERVATION_HOURS} horas</span> en {store.branch.name}. La reserva se confirma y paga en la tienda; no hay pagos en línea.
            </span>
          </p>
          <div className="flex flex-wrap gap-2">
            <Button variant="ghost" onClick={onClose} disabled={sending}>
              Volver
            </Button>
            <Button type="submit" form={`${ids.name}-form`} variant="brand" className="flex-1" loading={sending} disabled={summary.count === 0}>
              {sending ? 'Reservando…' : 'Confirmar reserva'}
            </Button>
          </div>
        </div>
      }
    >
      <form id={`${ids.name}-form`} onSubmit={submit} noValidate aria-busy={sending || undefined} className="space-y-6">
        {serverError && (
          <div id={ids.error} role="alert" className="flex items-start gap-3 rounded-xl border border-danger/40 bg-danger-soft p-3 text-sm">
            <CircleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-danger-text" />
            <div className="min-w-0 flex-1">
              <p className="font-semibold text-danger-text">
                {serverError.kind === 'insufficient_stock' ? 'No alcanzó el stock para reservar todo el armado' : 'No pudimos registrar la reserva'}
              </p>
              <p className="mt-0.5 text-text">{describeStorefrontError(serverError)}</p>
              {serverError.errors.length > 1 && (
                <ul className="mt-1 list-disc pl-5 text-text-muted">
                  {serverError.errors.map((message) => (
                    <li key={message}>{message}</li>
                  ))}
                </ul>
              )}
              {serverError.kind === 'insufficient_stock' && serverError.shortages.length > 0 && (
                <div className="mt-2 flex flex-wrap items-center gap-2">
                  <p className="text-text-muted">No se reservó nada. Las piezas afectadas están marcadas abajo.</p>
                  <Button
                    size="sm"
                    variant="outline"
                    leftIcon={<Wand2 />}
                    onClick={() => {
                      onAdjust(serverError.shortages);
                      setServerError(null);
                    }}
                  >
                    Ajustar a lo disponible
                  </Button>
                </div>
              )}
            </div>
          </div>
        )}

        <fieldset className="space-y-4" disabled={sending}>
          <legend className="text-xs font-semibold uppercase tracking-wide text-accent">Tus datos</legend>
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="sm:col-span-2">
              <label htmlFor={ids.name} className="mb-1 block text-sm font-medium text-text">
                Nombre y apellido <span aria-hidden="true">*</span>
              </label>
              <input
                id={ids.name}
                data-autofocus
                type="text"
                autoComplete="name"
                required
                maxLength={RESERVATION_LIMITS.nameMaxLength}
                value={form.name}
                onChange={(event) => update('name', event.target.value)}
                aria-invalid={errors.name ? true : undefined}
                aria-describedby={errors.name ? `${ids.name}-error` : undefined}
                className={FIELD}
              />
              {errors.name && (
                <p id={`${ids.name}-error`} className="mt-1 text-sm text-danger-text">
                  {errors.name}
                </p>
              )}
            </div>
            <div>
              <label htmlFor={ids.phone} className="mb-1 block text-sm font-medium text-text">
                Teléfono o WhatsApp <span aria-hidden="true">*</span>
              </label>
              <input
                id={ids.phone}
                type="tel"
                inputMode="tel"
                autoComplete="tel"
                required
                placeholder="+591 71234567"
                maxLength={RESERVATION_LIMITS.phoneMaxLength}
                value={form.phone}
                onChange={(event) => update('phone', event.target.value)}
                aria-invalid={errors.phone ? true : undefined}
                aria-describedby={`${ids.phone}-hint${errors.phone ? ` ${ids.phone}-error` : ''}`}
                className={FIELD}
              />
              <p id={`${ids.phone}-hint`} className="mt-1 text-xs text-text-faint">
                7 u 8 dígitos de Bolivia, con o sin +591. Con él consultás o liberás tu reserva.
              </p>
              {errors.phone && (
                <p id={`${ids.phone}-error`} className="mt-1 text-sm text-danger-text">
                  {errors.phone}
                </p>
              )}
            </div>
            <div>
              <label htmlFor={ids.email} className="mb-1 block text-sm font-medium text-text">
                Correo <span className="font-normal text-text-faint">(opcional)</span>
              </label>
              <input
                id={ids.email}
                type="email"
                inputMode="email"
                autoComplete="email"
                value={form.email}
                onChange={(event) => update('email', event.target.value)}
                aria-invalid={errors.email ? true : undefined}
                aria-describedby={errors.email ? `${ids.email}-error` : undefined}
                className={FIELD}
              />
              {errors.email && (
                <p id={`${ids.email}-error`} className="mt-1 text-sm text-danger-text">
                  {errors.email}
                </p>
              )}
            </div>
            <div className="sm:col-span-2">
              <label htmlFor={ids.notes} className="mb-1 block text-sm font-medium text-text">
                Notas para la tienda <span className="font-normal text-text-faint">(opcional)</span>
              </label>
              <textarea
                id={ids.notes}
                rows={2}
                maxLength={RESERVATION_LIMITS.notesMaxLength}
                placeholder="Cuándo pasás a retirar, si querés el servicio de ensamble…"
                value={form.notes}
                onChange={(event) => update('notes', event.target.value)}
                aria-invalid={errors.notes ? true : undefined}
                aria-describedby={errors.notes ? `${ids.notes}-error` : undefined}
                className={`${FIELD} h-auto resize-y py-2`}
              />
              {errors.notes && (
                <p id={`${ids.notes}-error`} className="mt-1 text-sm text-danger-text">
                  {errors.notes}
                </p>
              )}
            </div>
          </div>
        </fieldset>

        <section aria-labelledby={`${ids.notes}-piezas`}>
          <h3 id={`${ids.notes}-piezas`} className="text-xs font-semibold uppercase tracking-wide text-accent">
            Piezas a reservar
          </h3>
          <table className="mt-2 w-full text-sm">
            <caption className="sr-only">Piezas del armado</caption>
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
                lines.map((line) => {
                  const shortage = shortagesBySku.get(line.product.sku);
                  return (
                    <tr key={line.product.sku} className={shortage ? 'bg-danger-soft/60' : undefined} data-shortage={shortage ? 'true' : undefined}>
                      <td className="py-2.5 pr-3">
                        <div className="flex items-center gap-3">
                          <ProductImage src={line.product.image} alt="" padding="sm" className="w-10 shrink-0" />
                          <div className="min-w-0">
                            <p className="text-[0.6875rem] font-semibold uppercase tracking-wide text-text-faint">{slot.label}</p>
                            <p className="line-clamp-2 leading-snug text-text">{line.product.shortName}</p>
                            {shortage && (
                              <p className="mt-0.5 text-xs font-semibold text-danger-text">
                                Pediste {shortage.requested}; {shortage.available > 0 ? `hay ${pluralize(shortage.available, 'unidad', 'unidades')} disponible${shortage.available === 1 ? '' : 's'}` : 'no queda disponible'}.
                              </p>
                            )}
                          </div>
                        </div>
                      </td>
                      <td className="py-2.5 text-center text-text-muted tabular-nums">{line.quantity}</td>
                      <td className="py-2.5 text-right font-medium text-text tabular-nums">{formatMoney(line.product.price * line.quantity)}</td>
                    </tr>
                  );
                }),
              )}
            </tbody>
          </table>
          {summary.missing.length > 0 && (
            <p className="mt-3 text-sm text-warning-text">Piezas esenciales pendientes: {summary.missing.map((slot) => slot.label).join(', ')}. Podés reservar igual y completarlas en la tienda.</p>
          )}
        </section>

        {sending && (
          <p role="status" className="flex items-center gap-2 text-sm text-text-muted">
            <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
            Enviando la reserva a la tienda…
          </p>
        )}
      </form>
    </BuilderDialog>
  );
}
