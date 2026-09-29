// USO · Lista desplegable del panel (la nativa del navegador: accesible con teclado, lector de pantalla y cómoda en el
// teléfono). Como filtro trae la opción «Todos», que vale '' y no filtra:
//
//   <SelectField label="Estado" value={tabla.filters.estado} onChange={(v) => tabla.setFilter('estado', v)}
//     options={[{ value: 'pagada', label: 'Pagada' }, { value: 'anulada', label: 'Anulada' }]} />
//   <SelectField label="Sucursal" allLabel="Todas las sucursales" … />
//
// En un formulario (sin «Todos»): `allLabel={false}` y `placeholder="Elija una sucursal"` (esa opción no se puede volver
// a elegir). Para listas largas con búsqueda (productos, clientes) use ComboBox.

import clsx from 'clsx';
import { ChevronDown } from 'lucide-react';
import type { Ref } from 'react';
import { Field, type FieldFrameProps } from './Field';
import { CONTROL } from './styles';

export interface SelectOption<T extends string = string> {
  value: T;
  label: string;
  disabled?: boolean;
}

export interface SelectFieldProps<T extends string = string> extends FieldFrameProps {
  /** Valor elegido; '' = «Todos» (o ninguno, en un formulario). */
  value: T | '';
  onChange: (value: T | '') => void;
  options: readonly SelectOption<T>[];
  /** Texto de la opción que no filtra («Todos», «Todas las sucursales»); false = sin esa opción. Por defecto «Todos». */
  allLabel?: string | false;
  /** Sin «Todos»: texto de la opción vacía («Elija una sucursal»). */
  placeholder?: string;
  disabled?: boolean;
  name?: string;
  autoFocus?: boolean;
  ref?: Ref<HTMLSelectElement>;
}

export function SelectField<T extends string = string>({
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
  allLabel = 'Todos',
  placeholder,
  ref,
  ...select
}: SelectFieldProps<T>) {
  const known = value === '' || options.some((option) => option.value === value);
  return (
    <Field label={label} hint={hint} error={error} required={required} optional={optional} hideLabel={hideLabel} className={className}>
      {(control) => (
        <div className="relative">
          <select
            ref={ref}
            value={value}
            onChange={(event) => onChange(event.target.value as T | '')}
            className={clsx(CONTROL, 'cursor-pointer appearance-none pr-10', value === '' && allLabel === false && 'text-text-muted')}
            {...control}
            {...select}
          >
            {allLabel !== false ? (
              <option value="">{allLabel}</option>
            ) : (
              <option value="" disabled>
                {placeholder ?? 'Elija una opción'}
              </option>
            )}
            {/* Un valor que ya no está en la lista (de la dirección o de un registro viejo) se sigue viendo. */}
            {!known && <option value={value}>{value}</option>}
            {options.map((option) => (
              <option key={option.value} value={option.value} disabled={option.disabled}>
                {option.label}
              </option>
            ))}
          </select>
          <ChevronDown aria-hidden="true" className="pointer-events-none absolute top-1/2 right-3 size-4 -translate-y-1/2 text-text-muted" />
        </div>
      )}
    </Field>
  );
}
