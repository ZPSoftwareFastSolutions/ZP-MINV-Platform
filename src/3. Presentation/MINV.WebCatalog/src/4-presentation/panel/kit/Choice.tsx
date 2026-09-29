// USO · Casillas, interruptores y opciones del panel (toda la etiqueta es clicable: objetivo de 44 px).
//
//   <Checkbox label="Incluir anuladas" checked={incluir} onChange={setIncluir} />
//   <Switch label="Producto activo" description="Se vende en la caja y en la tienda." checked={activo} onChange={setActivo} />
//   <RadioGroup label="Tipo de documento" value={tipo} onChange={setTipo}
//     options={[{ value: 'factura', label: 'Factura' }, { value: 'recibo', label: 'Recibo', description: 'Sin crédito fiscal' }]} />
//
// Checkbox y RadioGroup usan los controles nativos (teclado y lectores de pantalla sin trucos: flechas entre opciones,
// Espacio para marcar). Switch es un botón con role="switch" y aria-checked.

import clsx from 'clsx';
import { useId, type InputHTMLAttributes, type ReactNode } from 'react';
import { describedBy } from './aria';
import { FieldGroup, type FieldFrameProps } from './Field';
import { ERROR, TICK } from './styles';

type NativeCheckbox = Omit<InputHTMLAttributes<HTMLInputElement>, 'type' | 'checked' | 'defaultChecked' | 'onChange' | 'className' | 'children' | 'id'>;

export interface CheckboxProps extends NativeCheckbox {
  label: ReactNode;
  /** Texto de apoyo debajo de la etiqueta. */
  description?: ReactNode;
  checked: boolean;
  onChange: (checked: boolean) => void;
  /** Marca parcial (algunas filas seleccionadas). */
  indeterminate?: boolean;
  error?: ReactNode;
  className?: string;
}

export function Checkbox({ label, description, checked, onChange, indeterminate = false, error, className, disabled, ...input }: CheckboxProps) {
  const id = useId();
  return (
    <div className={clsx('min-w-0', className)}>
      <label htmlFor={id} className={clsx('flex min-h-11 items-start gap-3 py-2.5', disabled ? 'cursor-not-allowed opacity-60' : 'cursor-pointer')}>
        <input
          ref={(element) => {
            if (element) element.indeterminate = indeterminate;
          }}
          id={id}
          type="checkbox"
          checked={checked}
          disabled={disabled}
          onChange={(event) => onChange(event.target.checked)}
          aria-labelledby={`${id}-etiqueta`}
          aria-describedby={describedBy(id, description, error)}
          aria-invalid={error ? true : undefined}
          className={TICK}
          {...input}
        />
        <span className="min-w-0 text-sm">
          <span id={`${id}-etiqueta`} className="font-medium text-text">
            {label}
          </span>
          {description && (
            <span id={`${id}-ayuda`} className="block text-text-muted">
              {description}
            </span>
          )}
        </span>
      </label>
      {error && (
        <p id={`${id}-error`} className={clsx(ERROR, 'mt-0 pl-8')}>
          {error}
        </p>
      )}
    </div>
  );
}

export interface SwitchProps {
  label: ReactNode;
  description?: ReactNode;
  checked: boolean;
  onChange: (checked: boolean) => void;
  disabled?: boolean;
  className?: string;
}

/** Interruptor encendido/apagado (el cambio se aplica al tocarlo; para confirmar después, use Checkbox). */
export function Switch({ label, description, checked, onChange, disabled = false, className }: SwitchProps) {
  const id = useId();
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-labelledby={`${id}-etiqueta`}
      aria-describedby={description ? `${id}-ayuda` : undefined}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={clsx(
        'flex min-h-11 w-full min-w-0 cursor-pointer items-start gap-3 rounded-xl py-2.5 text-left disabled:cursor-not-allowed disabled:opacity-60',
        className,
      )}
    >
      <span
        aria-hidden="true"
        className={clsx(
          'relative mt-0.5 inline-flex h-6 w-11 shrink-0 items-center rounded-full border transition-colors duration-200',
          checked ? 'border-primary bg-primary' : 'border-border-control bg-surface-3',
        )}
      >
        <span className={clsx('inline-block size-4 rounded-full bg-white shadow transition-transform duration-200', checked ? 'translate-x-6' : 'translate-x-1')} />
      </span>
      <span className="min-w-0 text-sm">
        <span id={`${id}-etiqueta`} className="font-medium text-text">
          {label}
        </span>
        {description && (
          <span id={`${id}-ayuda`} className="block text-text-muted">
            {description}
          </span>
        )}
      </span>
    </button>
  );
}

export interface RadioOption<T extends string = string> {
  value: T;
  label: string;
  description?: string;
  disabled?: boolean;
}

export interface RadioGroupProps<T extends string = string> extends FieldFrameProps {
  value: T | null;
  onChange: (value: T) => void;
  options: readonly RadioOption<T>[];
  /** En fila (se reparte en varias líneas si no entra) o en columna (por defecto). */
  orientation?: 'vertical' | 'horizontal';
  /** Nombre del grupo (por defecto uno único). */
  name?: string;
  disabled?: boolean;
}

export function RadioGroup<T extends string = string>({
  label,
  hint,
  error,
  required,
  optional,
  hideLabel,
  className,
  value,
  onChange,
  options,
  orientation = 'vertical',
  name,
  disabled = false,
}: RadioGroupProps<T>) {
  const ownName = useId();
  return (
    <FieldGroup
      label={label}
      hint={hint}
      error={error}
      required={required}
      optional={optional}
      hideLabel={hideLabel}
      className={className}
      bodyClassName={clsx(orientation === 'horizontal' ? 'flex flex-wrap gap-x-6' : 'flex flex-col')}
    >
      {options.map((option, index) => {
        const off = disabled || option.disabled;
        const optionId = `${ownName}-opcion-${index}`;
        return (
          <label key={option.value} className={clsx('flex min-h-11 items-start gap-3 py-2.5', off ? 'cursor-not-allowed opacity-60' : 'cursor-pointer')}>
            <input
              type="radio"
              name={name ?? ownName}
              value={option.value}
              checked={value === option.value}
              disabled={off}
              required={required}
              onChange={() => onChange(option.value)}
              aria-labelledby={`${optionId}-etiqueta`}
              aria-describedby={option.description ? `${optionId}-ayuda` : undefined}
              className={clsx(TICK, 'rounded-full')}
            />
            <span className="min-w-0 text-sm">
              <span id={`${optionId}-etiqueta`} className="font-medium text-text">
                {option.label}
              </span>
              {option.description && (
                <span id={`${optionId}-ayuda`} className="block text-text-muted">
                  {option.description}
                </span>
              )}
            </span>
          </label>
        );
      })}
    </FieldGroup>
  );
}
