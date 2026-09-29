// USO · Búsqueda de una lista, con espera al escribir: avisa `onChange` cuando la persona deja de escribir (300 ms), con
// Enter al instante, y Escape o la «×» la borran.
//
//   <SearchField label="Buscar" placeholder="Número, cliente o NIT" value={tabla.filters.q} onChange={(q) => tabla.setFilter('q', q)} />
//
// `value` es el texto YA aplicado (el de la dirección de la página): si cambia desde afuera («Limpiar filtros»), el
// campo lo sigue. El texto se entrega sin espacios en los extremos.

import clsx from 'clsx';
import { Search, X } from 'lucide-react';
import { useEffect, useRef, useState, type Ref } from 'react';
import { Field, type FieldFrameProps } from './Field';
import { CONTROL, FIELD_ICON_BUTTON } from './styles';

export interface SearchFieldProps extends Omit<FieldFrameProps, 'required' | 'optional'> {
  /** Texto aplicado. */
  value: string;
  /** Se llama con el texto nuevo cuando la persona deja de escribir. */
  onChange: (value: string) => void;
  placeholder?: string;
  /** Milisegundos de espera después de la última tecla (300 por defecto). */
  delay?: number;
  autoFocus?: boolean;
  disabled?: boolean;
  ref?: Ref<HTMLInputElement>;
}

export const SEARCH_DELAY_MS = 300;

export function SearchField({ label, hint, error, hideLabel, className, value, onChange, placeholder, delay = SEARCH_DELAY_MS, ref, ...input }: SearchFieldProps) {
  const [text, setText] = useState(value);
  const [applied, setApplied] = useState(value);
  // El valor cambió desde afuera (por ejemplo «Limpiar filtros»): el campo lo sigue.
  if (applied !== value) {
    setApplied(value);
    if (text.trim() !== value) setText(value);
  }

  const onChangeRef = useRef(onChange);
  useEffect(() => {
    onChangeRef.current = onChange;
  });

  // Espera a que la persona deje de escribir.
  useEffect(() => {
    const next = text.trim();
    if (next === value) return;
    const timer = setTimeout(() => onChangeRef.current(next), delay);
    return () => clearTimeout(timer);
  }, [text, value, delay]);

  const commit = (next: string) => {
    setText(next);
    if (next.trim() !== value) onChange(next.trim());
  };

  return (
    <Field label={label} hint={hint} error={error} hideLabel={hideLabel} className={className}>
      {(control) => (
        <div className="relative">
          <Search aria-hidden="true" className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-text-muted" />
          <input
            ref={ref}
            type="search"
            role="searchbox"
            autoComplete="off"
            spellCheck={false}
            enterKeyHint="search"
            placeholder={placeholder}
            value={text}
            onChange={(event) => setText(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter') {
                event.preventDefault();
                commit(text);
              } else if (event.key === 'Escape' && text.length > 0) {
                // Borra la búsqueda sin cerrar el diálogo o el panel que la contiene.
                event.preventDefault();
                event.stopPropagation();
                commit('');
              }
            }}
            className={clsx(CONTROL, 'pl-10', text && 'pr-12', '[&::-webkit-search-cancel-button]:appearance-none')}
            {...control}
            {...input}
          />
          {text && (
            <div className="absolute inset-y-0 right-0 flex items-center">
              <button type="button" aria-label="Borrar la búsqueda" title="Borrar la búsqueda" className={FIELD_ICON_BUTTON} onClick={() => commit('')}>
                <X aria-hidden="true" />
              </button>
            </div>
          )}
        </div>
      )}
    </Field>
  );
}
