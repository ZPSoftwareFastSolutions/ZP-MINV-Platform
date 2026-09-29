// Campos del formulario de reserva: datos de quien reserva (o los de su cuenta, solo lectura), «¿Cuándo pasás a
// recogerlo?», la sección plegable «Datos para tu factura (opcional)» y las notas. Cada campo tiene etiqueta visible,
// ayuda y error enlazados, y avisa al salir de él (`onBlur`) para validarlo.

import { ChevronDown, ReceiptText, UserRound } from 'lucide-react';
import { useId } from 'react';
import { documentType, DOCUMENT_LIMITS, parseDocumentType } from '@/1-domain/account/documents';
import type { CustomerAccount } from '@/1-domain/account/types';
import type { CheckoutField } from '@/1-domain/storefront/checkoutForm';
import { RESERVATION_LIMITS, type ReservationPolicy } from '@/1-domain/storefront/types';
import { ROUTES } from '@/4-presentation/app/routes';
import { Button } from '@/4-presentation/components/ui/Button';
import { FIELD_CLASSES, SelectField, TextField } from '@/4-presentation/components/ui/TextField';
import { buyerSummary, DOCUMENT_OPTIONS, holdDayOptions } from './checkoutText';
import type { CheckoutFormState } from './useCheckoutForm';

/** Registra el control de un campo para llevarle el foco cuando tiene un error. */
export type FieldRef = (field: CheckoutField) => (element: HTMLElement | null) => void;

interface FieldsProps {
  form: CheckoutFormState;
  fieldRef: FieldRef;
  disabled?: boolean;
}

/** Datos de quien reserva sin cuenta: nombre, teléfono o WhatsApp y correo. */
export function GuestContactFields({ form, fieldRef, disabled }: FieldsProps) {
  const { values, errorOf, change, blur } = form;
  return (
    <div className="grid gap-4 sm:grid-cols-2">
      <TextField
        ref={fieldRef('name')}
        className="sm:col-span-2"
        label="Nombre y apellido"
        required
        autoComplete="name"
        maxLength={RESERVATION_LIMITS.nameMaxLength}
        value={values.name}
        disabled={disabled}
        onChange={(event) => change('name', event.target.value)}
        onBlur={() => blur('name')}
        error={errorOf('name')}
      />
      <TextField
        ref={fieldRef('phone')}
        label="Teléfono o WhatsApp"
        required
        type="tel"
        inputMode="tel"
        autoComplete="tel"
        placeholder="+591 71234567"
        maxLength={RESERVATION_LIMITS.phoneMaxLength}
        value={values.phone}
        disabled={disabled}
        onChange={(event) => change('phone', event.target.value)}
        onBlur={() => blur('phone')}
        hint="7 u 8 dígitos de Bolivia, con o sin +591. Con él consultás o liberás tu reserva."
        error={errorOf('phone')}
      />
      <TextField
        ref={fieldRef('email')}
        label="Correo"
        optional
        type="email"
        inputMode="email"
        autoComplete="email"
        maxLength={RESERVATION_LIMITS.emailMaxLength}
        value={values.email}
        disabled={disabled}
        onChange={(event) => change('email', event.target.value)}
        onBlur={() => blur('email')}
        hint="Te enviamos el código y el detalle de tu reserva a este correo."
        error={errorOf('email')}
      />
    </div>
  );
}

/** Datos de la cuenta del cliente: se muestran (solo lectura) y se cambian en «Mi cuenta». */
export function AccountContact({ account }: { account: CustomerAccount }) {
  const type = documentType(account.documentType);
  return (
    <div className="rounded-xl border border-border bg-surface-2 p-4" data-testid="reserva-datos-cuenta">
      <p className="flex items-center gap-2 text-sm font-semibold text-text">
        <UserRound aria-hidden="true" className="size-4 text-accent" />
        Reservás con tu cuenta
      </p>
      <dl className="mt-3 grid gap-x-6 gap-y-2 text-sm sm:grid-cols-2">
        <div className="min-w-0">
          <dt className="text-xs text-text-faint">Nombre</dt>
          <dd className="font-medium break-words text-text">{account.name}</dd>
        </div>
        <div className="min-w-0">
          <dt className="text-xs text-text-faint">Teléfono o WhatsApp</dt>
          <dd className="font-medium text-text">{account.phone || '—'}</dd>
        </div>
        <div className="min-w-0 sm:col-span-2">
          <dt className="text-xs text-text-faint">Correo</dt>
          <dd className="font-medium break-all text-text">{account.email}</dd>
        </div>
        <div className="min-w-0 sm:col-span-2">
          <dt className="text-xs text-text-faint">Documento para la factura</dt>
          <dd className="font-medium text-text">
            {type && account.documentNumber ? `${type.short} ${account.documentNumber}${account.complement ? `-${account.complement}` : ''}` : 'Sin documento (lo pedís al pagar)'}
          </dd>
        </div>
      </dl>
      <p className="mt-3 text-xs text-text-muted">Te enviamos el código y el detalle de tu reserva a tu correo. Para cambiar estos datos, andá a «Mis datos».</p>
      <div className="mt-2">
        <Button to={ROUTES.accountSection('datos')} size="sm" variant="outline">
          Cambiar mis datos
        </Button>
      </div>
    </div>
  );
}

/** «¿Cuándo pasás a recogerlo?»: mañana, en 2 o en 3 días, según lo que permite la tienda. */
export function HoldDaysField({ form, fieldRef, disabled, policy, branchName }: FieldsProps & { policy: ReservationPolicy; branchName: string }) {
  const { values, errorOf, change, blur } = form;
  return (
    <SelectField
      ref={fieldRef('holdDays')}
      label="¿Cuándo pasás a recogerlo?"
      required
      options={holdDayOptions(policy)}
      value={values.holdDays}
      disabled={disabled}
      onChange={(event) => change('holdDays', event.target.value)}
      onBlur={() => blur('holdDays')}
      hint={`Te lo guardamos en ${branchName} hasta ese momento; después vuelve a estar disponible para otros clientes.`}
      error={errorOf('holdDays')}
    />
  );
}

/** «Datos para tu factura (opcional)»: plegable; si tiene errores al enviar, se abre sola. */
export function BuyerSection({ form, fieldRef, disabled, open, onToggle }: FieldsProps & { open: boolean; onToggle: () => void }) {
  const { values, errorOf, change, blur } = form;
  const regionId = useId();
  const type = documentType(parseDocumentType(values.documentType));
  const summary = buyerSummary(values);
  const hasError = Boolean(errorOf('documentType') || errorOf('documentNumber') || errorOf('complement') || errorOf('buyerName'));
  return (
    <section className="rounded-xl border border-border" aria-label="Datos para tu factura">
      <button
        type="button"
        className="flex min-h-11 w-full cursor-pointer items-center gap-3 rounded-xl px-4 py-3 text-left transition-colors duration-200 hover:bg-surface-2"
        aria-expanded={open}
        aria-controls={regionId}
        onClick={onToggle}
      >
        <ReceiptText aria-hidden="true" className="size-5 shrink-0 text-accent" />
        <span className="min-w-0 flex-1">
          <span className="block text-sm font-semibold text-text">
            Datos para tu factura <span className="font-normal text-text-faint">(opcional)</span>
          </span>
          {!open && (summary || hasError) && (
            <span className={hasError ? 'block text-xs text-danger-text' : 'block truncate text-xs text-text-muted'}>{hasError ? 'Hay datos por corregir' : summary}</span>
          )}
        </span>
        <ChevronDown aria-hidden="true" className={open ? 'size-5 shrink-0 rotate-180 text-text-muted transition-transform duration-200' : 'size-5 shrink-0 text-text-muted transition-transform duration-200'} />
      </button>
      <div id={regionId} hidden={!open} className="border-t border-border px-4 pt-3 pb-4">
        <p className="text-xs text-text-muted">Si querés la factura a tu nombre o al de tu empresa, dejá tu CI o NIT. Si no, podés darlo al pagar.</p>
        <div className="mt-3 grid gap-4 sm:grid-cols-2">
          <SelectField
            ref={fieldRef('documentType')}
            label="Tipo de documento"
            options={DOCUMENT_OPTIONS}
            value={values.documentType}
            disabled={disabled}
            onChange={(event) => {
              change('documentType', event.target.value);
              // El complemento solo existe con cédula de identidad: al cambiar de tipo, se borra.
              if (parseDocumentType(event.target.value) !== 1 && values.complement) change('complement', '');
            }}
            onBlur={() => blur('documentType')}
            error={errorOf('documentType')}
          />
          <TextField
            ref={fieldRef('documentNumber')}
            label="Número de documento"
            required={Boolean(type)}
            inputMode={type?.numeric ? 'numeric' : 'text'}
            autoComplete="off"
            maxLength={DOCUMENT_LIMITS.numberMaxLength}
            value={values.documentNumber}
            disabled={disabled}
            onChange={(event) => change('documentNumber', event.target.value)}
            onBlur={() => blur('documentNumber')}
            hint={type?.numeric ? `Solo dígitos (${type.short}).` : undefined}
            error={errorOf('documentNumber')}
          />
          {type?.complement && (
            <TextField
              ref={fieldRef('complement')}
              label="Complemento"
              optional
              autoComplete="off"
              autoCapitalize="characters"
              maxLength={DOCUMENT_LIMITS.complementMaxLength}
              value={values.complement}
              disabled={disabled}
              onChange={(event) => change('complement', event.target.value)}
              onBlur={() => blur('complement')}
              hint="Solo si tu CI lo tiene (por ejemplo 1A). Se usa solo con cédula de identidad."
              error={errorOf('complement')}
            />
          )}
          <TextField
            ref={fieldRef('buyerName')}
            className={type?.complement ? undefined : 'sm:col-span-2'}
            label="Nombre o razón social"
            optional
            autoComplete="organization"
            maxLength={RESERVATION_LIMITS.buyerNameMaxLength}
            value={values.buyerName}
            disabled={disabled}
            onChange={(event) => change('buyerName', event.target.value)}
            onBlur={() => blur('buyerName')}
            error={errorOf('buyerName')}
          />
        </div>
      </div>
    </section>
  );
}

/** Notas para la tienda: se pueden escribir en varias líneas, pero viajan en una (el servidor no admite saltos). */
export function NotesField({ form, fieldRef, disabled }: FieldsProps) {
  const { values, errorOf, change, blur } = form;
  const id = useId();
  const error = errorOf('notes');
  return (
    <div className="min-w-0">
      <label htmlFor={id} className="mb-1 block text-sm font-medium text-text">
        Notas para la tienda <span className="font-normal text-text-faint">(opcional)</span>
      </label>
      <textarea
        ref={fieldRef('notes')}
        id={id}
        rows={3}
        maxLength={RESERVATION_LIMITS.notesMaxLength + 50}
        placeholder="Por ejemplo: paso a recogerlo el sábado por la mañana."
        value={values.notes}
        disabled={disabled}
        onChange={(event) => change('notes', event.target.value)}
        onBlur={() => blur('notes')}
        aria-invalid={error ? true : undefined}
        aria-describedby={`${id}-ayuda${error ? ` ${id}-error` : ''}`}
        className={`${FIELD_CLASSES} h-auto resize-y py-2`}
      />
      <p id={`${id}-ayuda`} className="mt-1 text-xs text-text-faint">
        Hasta {RESERVATION_LIMITS.notesMaxLength} caracteres. Si escribís varias líneas, las unimos en una al enviar.
      </p>
      {error && (
        <p id={`${id}-error`} className="mt-1 text-sm text-danger-text">
          {error}
        </p>
      )}
    </div>
  );
}
