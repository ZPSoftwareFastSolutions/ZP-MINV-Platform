// USO · Marco de un campo: etiqueta visible, ayuda y error enlazados al control (`aria-describedby`, `aria-invalid`).
// Todos los campos del conjunto lo usan; úselo también para un control propio:
//
//   <Field label="Producto" hint="Busque por nombre o SKU" error={errores.producto} required>
//     {(control) => <MiControl {...control} />}          ← recibe id, aria-describedby, aria-invalid y required
//   </Field>
//
// `FieldGroup` es lo mismo para un grupo de controles (opciones, rango de fechas): usa <fieldset> y <legend>.

import clsx from 'clsx';
import { useId, type ReactNode } from 'react';
import { describedBy } from './aria';
import { ERROR, HINT, LABEL } from './styles';

/** Lo que el control recibe del marco. */
export interface FieldControlProps {
  id: string;
  'aria-describedby'?: string;
  'aria-invalid'?: true;
  required?: boolean;
}

export interface FieldFrameProps {
  /** Etiqueta visible (también el nombre accesible del control). */
  label: string;
  /** Ayuda debajo del control. */
  hint?: ReactNode;
  /** Error debajo del control (lo marca inválido). */
  error?: ReactNode;
  /** Muestra « *» en la etiqueta. */
  required?: boolean;
  /** Muestra «(opcional)» en la etiqueta. */
  optional?: boolean;
  /** La etiqueta queda solo para lectores de pantalla (barras compactas). Úselo poco: la etiqueta visible ayuda. */
  hideLabel?: boolean;
  /** Clases del contenedor (ubicación en una grilla). */
  className?: string;
}

export interface FieldProps extends FieldFrameProps {
  /** Algo a la derecha de la etiqueta (contador de caracteres). */
  labelExtra?: ReactNode;
  children: (control: FieldControlProps) => ReactNode;
}

function LabelText({ label, required, optional }: Pick<FieldFrameProps, 'label' | 'required' | 'optional'>) {
  return (
    <>
      {label}
      {required && (
        <span aria-hidden="true" className="text-danger-text">
          {' '}
          *
        </span>
      )}
      {optional && <span className="font-normal text-text-faint"> (opcional)</span>}
    </>
  );
}

function Messages({ id, hint, error }: { id: string; hint?: ReactNode; error?: ReactNode }) {
  return (
    <>
      {hint && (
        <p id={`${id}-ayuda`} className={HINT}>
          {hint}
        </p>
      )}
      {error && (
        <p id={`${id}-error`} className={ERROR}>
          {error}
        </p>
      )}
    </>
  );
}

export function Field({ label, hint, error, required, optional, hideLabel, labelExtra, className, children }: FieldProps) {
  const id = useId();
  return (
    <div className={clsx('min-w-0', className)}>
      <div className={clsx('mb-1 flex items-baseline justify-between gap-2', hideLabel && !labelExtra && 'sr-only')}>
        <label htmlFor={id} className={clsx(LABEL, hideLabel && 'sr-only')}>
          <LabelText label={label} required={required} optional={optional} />
        </label>
        {labelExtra}
      </div>
      {children({ id, 'aria-describedby': describedBy(id, hint, error), 'aria-invalid': error ? true : undefined, required })}
      <Messages id={id} hint={hint} error={error} />
    </div>
  );
}

export interface FieldGroupProps extends FieldFrameProps {
  children: ReactNode;
  /** Clases del contenedor de los controles. */
  bodyClassName?: string;
}

/** Grupo de controles con <fieldset> y <legend> (opciones, rangos). La ayuda y el error describen al grupo. */
export function FieldGroup({ label, hint, error, required, optional, hideLabel, className, bodyClassName, children }: FieldGroupProps) {
  const id = useId();
  return (
    <fieldset className={clsx('min-w-0', className)} aria-describedby={describedBy(id, hint, error)}>
      <legend className={clsx(LABEL, 'mb-1', hideLabel && 'sr-only')}>
        <LabelText label={label} required={required} optional={optional} />
      </legend>
      <div className={bodyClassName}>{children}</div>
      <Messages id={id} hint={hint} error={error} />
    </fieldset>
  );
}
