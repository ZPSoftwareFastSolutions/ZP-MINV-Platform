// Campo de formulario con etiqueta visible, ayuda y error enlazados (`aria-describedby`, `aria-invalid`). Alto de 44 px
// (objetivo táctil) y el borde de control del tema (contraste ≥ 3:1). El foco usa el contorno global `:focus-visible`.

import clsx from 'clsx';
import { useId, type InputHTMLAttributes, type ReactNode, type Ref, type SelectHTMLAttributes } from 'react';

/** Clases de un control de texto (también para `<select>` y `<textarea>`). */
export const FIELD_CLASSES =
  'h-11 w-full min-w-0 rounded-xl border border-border-control bg-surface px-3 text-sm text-text placeholder:text-text-faint transition-colors duration-200 hover:border-border-strong focus:border-accent aria-invalid:border-danger disabled:cursor-not-allowed disabled:opacity-60 read-only:bg-surface-2 read-only:text-text-muted';

interface FieldFrameProps {
  id: string;
  label: string;
  required?: boolean;
  /** Muestra «(opcional)» junto a la etiqueta. */
  optional?: boolean;
  hint?: ReactNode;
  error?: ReactNode;
  className?: string;
  children: ReactNode;
}

function describedBy(id: string, hint: ReactNode, error: ReactNode, extra?: string): string | undefined {
  const ids = [hint ? `${id}-ayuda` : null, error ? `${id}-error` : null, extra ?? null].filter(Boolean);
  return ids.length > 0 ? ids.join(' ') : undefined;
}

function FieldFrame({ id, label, required, optional, hint, error, className, children }: FieldFrameProps) {
  return (
    <div className={clsx('min-w-0', className)}>
      <label htmlFor={id} className="mb-1 block text-sm font-medium text-text">
        {label}
        {required && (
          <span aria-hidden="true" className="text-danger-text">
            {' '}
            *
          </span>
        )}
        {optional && <span className="font-normal text-text-faint"> (opcional)</span>}
      </label>
      {children}
      {hint && (
        <p id={`${id}-ayuda`} className="mt-1 text-xs text-text-faint">
          {hint}
        </p>
      )}
      {error && (
        <p id={`${id}-error`} className="mt-1 text-sm text-danger-text">
          {error}
        </p>
      )}
    </div>
  );
}

export interface TextFieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'className' | 'id'> {
  label: string;
  error?: ReactNode;
  hint?: ReactNode;
  optional?: boolean;
  /** Clases del contenedor (para ubicarlo en una grilla). */
  className?: string;
  inputClassName?: string;
  /** Control superpuesto a la derecha del campo (mostrar u ocultar la contraseña). */
  trailing?: ReactNode;
  /** Id de otro elemento que también describe el campo (la lista de requisitos de la contraseña). */
  describedBy?: string;
  ref?: Ref<HTMLInputElement>;
}

export function TextField({ label, error, hint, optional, className, inputClassName, trailing, describedBy: extra, required, ref, ...input }: TextFieldProps) {
  const id = useId();
  return (
    <FieldFrame id={id} label={label} required={required} optional={optional} hint={hint} error={error} className={className}>
      <div className="relative">
        <input
          ref={ref}
          id={id}
          required={required}
          aria-invalid={error ? true : undefined}
          aria-describedby={describedBy(id, hint, error, extra)}
          className={clsx(FIELD_CLASSES, trailing && 'pr-12', inputClassName)}
          {...input}
        />
        {trailing && <div className="absolute inset-y-0 right-0 flex items-center">{trailing}</div>}
      </div>
    </FieldFrame>
  );
}

export interface SelectOption {
  value: string;
  label: string;
}

export interface SelectFieldProps extends Omit<SelectHTMLAttributes<HTMLSelectElement>, 'className' | 'id' | 'children'> {
  label: string;
  options: readonly SelectOption[];
  error?: ReactNode;
  hint?: ReactNode;
  optional?: boolean;
  className?: string;
  ref?: Ref<HTMLSelectElement>;
}

/** Lista desplegable nativa (accesible con teclado y lectores de pantalla, y cómoda en el teléfono). */
export function SelectField({ label, options, error, hint, optional, className, required, ref, ...select }: SelectFieldProps) {
  const id = useId();
  return (
    <FieldFrame id={id} label={label} required={required} optional={optional} hint={hint} error={error} className={className}>
      <select
        ref={ref}
        id={id}
        required={required}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy(id, hint, error)}
        className={clsx(FIELD_CLASSES, 'cursor-pointer pr-8')}
        {...select}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </FieldFrame>
  );
}
