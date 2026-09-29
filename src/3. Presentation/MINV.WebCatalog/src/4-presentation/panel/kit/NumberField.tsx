// USO · Campos numéricos del panel. Trabajan con un número (o null si está vacío o mal escrito):
//
//   <NumberField label="Cantidad" value={cantidad} onChange={setCantidad} unit="u." required />
//   <NumberField label="Peso" value={peso} onChange={setPeso} decimals={3} unit="kg" />
//   <MoneyField label="Precio de venta" value={precio} onChange={setPrecio} hint="Con IVA incluido." />
//
// Aceptan el formato de Bolivia («1.234,50») y también el punto decimal («1234.5»). Al salir del campo el número se
// muestra con separadores («1.234,50»). Un texto que no es un número, un negativo (sin `allowNegative`) o más decimales
// de los permitidos se marca en el propio campo y el valor que llega a `onChange` es null.

import clsx from 'clsx';
import { useState, type Ref } from 'react';
import { formatNumber } from '@/shared/format';
import { parseLocaleNumber, roundTo } from '../lib/numbers';
import { Field, type FieldFrameProps } from './Field';
import { CONTROL } from './styles';

export interface NumberFieldProps extends FieldFrameProps {
  value: number | null;
  onChange: (value: number | null) => void;
  /** Decimales permitidos (0 = solo enteros). */
  decimals?: number;
  /** Admite negativos (por defecto no). */
  allowNegative?: boolean;
  /** Unidad a la derecha, dentro del campo («u.», «kg», «%»). */
  unit?: string;
  /** Texto fijo a la izquierda, dentro del campo («Bs»). */
  prefix?: string;
  /** Muestra siempre todos los decimales al salir del campo («1.234,50»). */
  fixedDecimals?: boolean;
  placeholder?: string;
  disabled?: boolean;
  readOnly?: boolean;
  name?: string;
  autoFocus?: boolean;
  onBlur?: () => void;
  ref?: Ref<HTMLInputElement>;
}

interface Reading {
  /** Valor que representa el texto (null si está vacío o no sirve). */
  value: number | null;
  /** Mensaje si el texto no sirve. */
  problem: string | null;
}

function read(text: string, decimals: number, allowNegative: boolean): Reading {
  const parsed = parseLocaleNumber(text);
  if (parsed === null) return { value: null, problem: null };
  const example = decimals > 0 ? '1.234,50' : '1.500';
  if (Number.isNaN(parsed)) return { value: null, problem: `Escriba un número, por ejemplo ${example}.` };
  if (parsed < 0 && !allowNegative) return { value: null, problem: 'Escriba un número positivo.' };
  if (roundTo(parsed, decimals) !== parsed) {
    return { value: null, problem: decimals === 0 ? 'Escriba un número entero.' : `Use como máximo ${decimals} decimales.` };
  }
  return { value: parsed, problem: null };
}

function display(value: number | null, decimals: number, fixed: boolean): string {
  if (value === null || !Number.isFinite(value)) return '';
  return formatNumber(value, fixed ? { decimals } : { maxDecimals: decimals });
}

export function NumberField({
  label,
  hint,
  error,
  required,
  optional,
  hideLabel,
  className,
  value,
  onChange,
  decimals = 0,
  allowNegative = false,
  unit,
  prefix,
  fixedDecimals = false,
  onBlur,
  ref,
  ...input
}: NumberFieldProps) {
  const [text, setText] = useState(() => display(value, decimals, fixedDecimals));
  const [lastValue, setLastValue] = useState(value);
  // Si el valor cambia desde afuera (limpiar el formulario, cargar otro registro) el texto lo sigue; si el cambio vino
  // de lo que se está escribiendo, el texto queda como está («1,» mientras se escribe «1,5»).
  if (!Object.is(lastValue, value)) {
    setLastValue(value);
    if (read(text, decimals, allowNegative).value !== value) setText(display(value, decimals, fixedDecimals));
  }
  const reading = read(text, decimals, allowNegative);

  return (
    <Field label={label} hint={hint} error={error ?? reading.problem} required={required} optional={optional} hideLabel={hideLabel} className={className}>
      {(control) => (
        <div className="relative">
          {prefix && (
            <span aria-hidden="true" className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3 text-sm font-medium text-text-muted">
              {prefix}
            </span>
          )}
          <input
            ref={ref}
            type="text"
            inputMode={decimals > 0 ? 'decimal' : 'numeric'}
            autoComplete="off"
            value={text}
            onChange={(event) => {
              const next = event.target.value;
              setText(next);
              const nextValue = read(next, decimals, allowNegative).value;
              if (!Object.is(nextValue, value)) onChange(nextValue);
            }}
            onBlur={() => {
              if (reading.value !== null) setText(display(reading.value, decimals, fixedDecimals));
              onBlur?.();
            }}
            className={clsx(CONTROL, 'tabular-nums', prefix && 'pl-10', unit && 'pr-12')}
            {...control}
            {...input}
          />
          {unit && (
            <span aria-hidden="true" className="pointer-events-none absolute inset-y-0 right-0 flex items-center pr-3 text-sm text-text-muted">
              {unit}
            </span>
          )}
        </div>
      )}
    </Field>
  );
}

export type MoneyFieldProps = Omit<NumberFieldProps, 'decimals' | 'prefix' | 'fixedDecimals' | 'unit'>;

/** Importe en bolivianos: «Bs» a la izquierda, dos decimales, «1.234,50» al salir del campo. */
export function MoneyField(props: MoneyFieldProps) {
  return <NumberField {...props} decimals={2} prefix="Bs" fixedDecimals />;
}
