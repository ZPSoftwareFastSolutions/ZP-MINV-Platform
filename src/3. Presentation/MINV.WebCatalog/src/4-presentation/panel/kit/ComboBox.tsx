// USO · Lista con búsqueda para listas largas (productos, clientes, proveedores). Patrón «combobox» de ARIA 1.2: se
// escribe para filtrar, flechas ↑ ↓ para moverse, Enter elige, Escape cierra (y otra vez deshace lo escrito).
//
//   Lista fija (se filtra en la página, sin acentos ni mayúsculas, por nombre, descripción y valor):
//   <ComboBox label="Sucursal" value={sucursal} onChange={setSucursal}
//     options={sucursales.map((s) => ({ value: s.id, label: s.name, description: s.code }))} />
//
//   Carga asíncrona (busca en el servidor a medida que se escribe; espera 250 ms y descarta respuestas viejas):
//   <ComboBox label="Producto" value={producto} onChange={setProducto} placeholder="Nombre o SKU"
//     loadOptions={async (texto, signal) => (await rpc.send('SearchProductsQuery', { text: texto }, { signal }))
//       .map((p) => ({ value: p.sku, label: p.name, description: `${p.sku} · ${formatMoney(p.price)}`, data: p }))} />
//
// `value` es la opción elegida (o null): conserva `data` para no volver a buscarla. Si se borra el texto y se sale del
// campo, la selección se quita (salvo que sea obligatorio).

import clsx from 'clsx';
import { Check, ChevronDown, LoaderCircle, RotateCcw, X } from 'lucide-react';
import { useEffect, useId, useRef, useState, type KeyboardEvent, type Ref } from 'react';
import { normalizeText, tokenize } from '@/shared/text';
import { isAbort } from '../lib/rpc';
import { Field, type FieldFrameProps } from './Field';
import { CONTROL, FIELD_ICON_BUTTON, OPTION, POPOVER } from './styles';

export interface ComboOption<T = unknown> {
  /** Identificador (SKU, id, código). */
  value: string;
  /** Texto que se ve y que queda en el campo al elegirla. */
  label: string;
  /** Segunda línea (SKU, precio, NIT, stock). */
  description?: string;
  disabled?: boolean;
  /** El objeto completo (producto, cliente), para usarlo sin volver a buscarlo. */
  data?: T;
}

export interface ComboBoxProps<T = unknown> extends FieldFrameProps {
  value: ComboOption<T> | null;
  onChange: (option: ComboOption<T> | null) => void;
  /** Lista fija. */
  options?: readonly ComboOption<T>[];
  /** Búsqueda en el servidor: recibe el texto escrito y una señal que se cancela si la persona sigue escribiendo. */
  loadOptions?: (query: string, signal: AbortSignal) => Promise<readonly ComboOption<T>[]>;
  /** Caracteres mínimos para buscar (2 con carga asíncrona, 0 con lista fija). */
  minChars?: number;
  /** Espera después de la última tecla antes de buscar en el servidor (250 ms). */
  delay?: number;
  placeholder?: string;
  /** Texto sin resultados («Sin resultados»). */
  emptyText?: string;
  /** Máximo de opciones visibles (50): con más, se pide seguir escribiendo. */
  maxResults?: number;
  /** Muestra la «×» para quitar la selección (por defecto, si no es obligatorio). */
  clearable?: boolean;
  disabled?: boolean;
  autoFocus?: boolean;
  ref?: Ref<HTMLInputElement>;
}

interface RemoteResult<T> {
  query: string;
  status: 'ready' | 'error';
  options: readonly ComboOption<T>[];
}

function filterOptions<T>(options: readonly ComboOption<T>[], query: string): readonly ComboOption<T>[] {
  const words = tokenize(query);
  if (words.length === 0) return options;
  return options.filter((option) => {
    const haystack = normalizeText(`${option.label} ${option.description ?? ''} ${option.value}`);
    return words.every((word) => haystack.includes(word));
  });
}

function countText(count: number): string {
  return count === 1 ? '1 resultado' : `${count} resultados`;
}

export function ComboBox<T = unknown>({
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
  loadOptions,
  minChars,
  delay = 250,
  placeholder,
  emptyText = 'Sin resultados',
  maxResults = 50,
  clearable,
  disabled = false,
  autoFocus,
  ref,
}: ComboBoxProps<T>) {
  const listId = useId();
  const optionId = (index: number) => `${listId}-opcion-${index}`;
  const inputRef = useRef<HTMLInputElement | null>(null);
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState(false);
  const [query, setQuery] = useState('');
  const [active, setActive] = useState(-1);
  const [remote, setRemote] = useState<RemoteResult<T> | null>(null);
  const [retries, setRetries] = useState(0);

  const isAsync = typeof loadOptions === 'function';
  const min = minChars ?? (isAsync ? 2 : 0);
  const searchText = editing ? query.trim() : '';
  const tooShort = isAsync && searchText.length < min;
  const canClear = (clearable ?? !required) && value !== null && !disabled;

  let results: readonly ComboOption<T>[] = [];
  let total = 0;
  let status: 'idle' | 'loading' | 'ready' | 'error' = 'ready';
  if (isAsync) {
    if (tooShort) status = 'idle';
    else if (remote && remote.query === searchText) {
      status = remote.status;
      total = remote.options.length;
      results = remote.options.slice(0, maxResults);
    } else status = 'loading';
  } else {
    const matches = filterOptions(options ?? [], searchText);
    total = matches.length;
    results = matches.slice(0, maxResults);
  }

  // Búsqueda en el servidor: espera a que la persona deje de escribir y cancela la anterior (las respuestas viejas se
  // descartan).
  const loadRef = useRef(loadOptions);
  useEffect(() => {
    loadRef.current = loadOptions;
  });
  useEffect(() => {
    if (!open || !isAsync || tooShort) return;
    const controller = new AbortController();
    const timer = setTimeout(() => {
      loadRef.current?.(searchText, controller.signal).then(
        (list) => {
          if (!controller.signal.aborted) setRemote({ query: searchText, status: 'ready', options: list });
        },
        (failure: unknown) => {
          if (!controller.signal.aborted && !isAbort(failure)) setRemote({ query: searchText, status: 'error', options: [] });
        },
      );
    }, delay);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [open, isAsync, tooShort, searchText, delay, retries]);

  const enabledIndexes = results.map((option, index) => (option.disabled ? -1 : index)).filter((index) => index >= 0);
  const selectedIndex = value ? results.findIndex((option) => option.value === value.value) : -1;
  // Opción activa efectiva: si la marcada está deshabilitada o ya no está (cambiaron los resultados), la habilitada
  // siguiente (o la última).
  const current =
    active < 0 ? -1 : enabledIndexes.includes(active) ? active : (enabledIndexes.find((index) => index > active) ?? enabledIndexes[enabledIndexes.length - 1] ?? -1);
  const activeId = open && current >= 0 ? optionId(current) : undefined;

  // La opción activa siempre a la vista dentro de la lista.
  useEffect(() => {
    if (!open || current < 0) return;
    document.getElementById(`${listId}-opcion-${current}`)?.scrollIntoView?.({ block: 'nearest' });
  }, [open, current, listId]);

  const openList = (index: number) => {
    setOpen(true);
    setActive(index);
  };

  const move = (delta: 1 | -1) => {
    if (enabledIndexes.length === 0) return;
    const position = enabledIndexes.indexOf(current);
    const next = position < 0 ? (delta > 0 ? 0 : enabledIndexes.length - 1) : (position + delta + enabledIndexes.length) % enabledIndexes.length;
    setActive(enabledIndexes[next]);
  };

  const select = (option: ComboOption<T>) => {
    if (option.disabled) return;
    onChange(option);
    setEditing(false);
    setQuery('');
    setOpen(false);
    setActive(-1);
  };

  /** Cierra la lista al salir del campo: lo escrito sin elegir se deshace; el texto borrado quita la selección. */
  const leave = () => {
    if (editing && query.trim() === '' && value !== null && (clearable ?? !required)) onChange(null);
    setEditing(false);
    setQuery('');
    setOpen(false);
    setActive(-1);
  };

  const clear = () => {
    onChange(null);
    setEditing(false);
    setQuery('');
    setOpen(false);
    inputRef.current?.focus();
  };

  /** Vuelve a buscar después de una falla (botón «Reintentar» o Enter). */
  const retry = () => {
    setRemote(null);
    setRetries((count) => count + 1);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (!open) openList(event.altKey ? -1 : selectedIndex >= 0 ? selectedIndex : (enabledIndexes[0] ?? -1));
        else move(1);
        break;
      case 'ArrowUp':
        event.preventDefault();
        if (!open) openList(enabledIndexes[enabledIndexes.length - 1] ?? -1);
        else move(-1);
        break;
      case 'Enter':
        if (open && status === 'error') {
          event.preventDefault();
          retry();
        } else if (open && current >= 0) {
          event.preventDefault();
          select(results[current]);
        }
        break;
      case 'Escape':
        if (open) {
          event.preventDefault();
          event.stopPropagation();
          setOpen(false);
          setActive(-1);
        } else if (editing) {
          event.preventDefault();
          event.stopPropagation();
          setEditing(false);
          setQuery('');
        }
        break;
      case 'Tab':
        if (open) leave();
        break;
      default:
        break;
    }
  };

  let message: string | null = null;
  if (status === 'idle') message = `Escriba al menos ${min} ${min === 1 ? 'carácter' : 'caracteres'} para buscar.`;
  else if (status === 'loading') message = 'Buscando…';
  else if (status === 'error') message = 'No se pudo buscar.';
  else if (results.length === 0) message = emptyText;
  const more = status === 'ready' && total > results.length ? `Mostrando ${results.length} de ${total}: siga escribiendo para acotar.` : null;
  const announcement = !open ? '' : status === 'error' ? 'No se pudo buscar. Pulse Enter para reintentar.' : (message ?? countText(total));

  return (
    <Field label={label} hint={hint} error={error} required={required} optional={optional} hideLabel={hideLabel} className={className}>
      {(control) => (
        <div
          className="relative"
          onBlur={(event) => {
            if (!event.currentTarget.contains(event.relatedTarget as Node | null)) leave();
          }}
        >
          <input
            ref={(element) => {
              inputRef.current = element;
              if (typeof ref === 'function') ref(element);
              else if (ref) ref.current = element;
            }}
            type="text"
            role="combobox"
            aria-expanded={open}
            aria-controls={listId}
            aria-autocomplete="list"
            aria-activedescendant={activeId}
            autoComplete="off"
            spellCheck={false}
            autoFocus={autoFocus}
            disabled={disabled}
            placeholder={placeholder}
            value={editing ? query : (value?.label ?? '')}
            onChange={(event) => {
              setQuery(event.target.value);
              setEditing(true);
              setOpen(true);
              setActive(event.target.value.trim() ? 0 : -1);
            }}
            onClick={() => {
              if (!open) openList(selectedIndex);
            }}
            onKeyDown={onKeyDown}
            className={clsx(CONTROL, canClear ? 'pr-22' : 'pr-12')}
            {...control}
          />
          <div className="absolute inset-y-0 right-0 flex items-center">
            {canClear && (
              <button type="button" aria-label={`Quitar ${label.toLowerCase()}`} title="Quitar la selección" className={FIELD_ICON_BUTTON} onClick={clear}>
                <X aria-hidden="true" />
              </button>
            )}
            <button
              type="button"
              tabIndex={-1}
              aria-label={open ? 'Ocultar opciones' : 'Mostrar opciones'}
              disabled={disabled}
              className={FIELD_ICON_BUTTON}
              onMouseDown={(event) => event.preventDefault()}
              onClick={() => {
                if (open) setOpen(false);
                else openList(selectedIndex);
                inputRef.current?.focus();
              }}
            >
              <ChevronDown aria-hidden="true" className={clsx('transition-transform duration-200', open && 'rotate-180')} />
            </button>
          </div>

          <div hidden={!open} className={clsx(POPOVER, 'absolute inset-x-0 top-full z-30 mt-1')} onMouseDown={(event) => event.preventDefault()}>
            <ul id={listId} role="listbox" aria-label={label} className="max-h-72 overflow-y-auto overscroll-contain">
              {results.map((option, index) => {
                const selected = value?.value === option.value;
                return (
                  <li
                    key={option.value}
                    id={optionId(index)}
                    role="option"
                    aria-selected={selected}
                    aria-disabled={option.disabled || undefined}
                    className={clsx(OPTION, index === current && 'bg-surface-3', option.disabled && 'cursor-not-allowed opacity-50')}
                    onClick={() => select(option)}
                    onMouseMove={() => {
                      if (active !== index && !option.disabled) setActive(index);
                    }}
                  >
                    <span className="min-w-0 flex-1">
                      <span className="block truncate font-medium">{option.label}</span>
                      {option.description && <span className="block truncate text-xs text-text-muted">{option.description}</span>}
                    </span>
                    {selected && <Check aria-hidden="true" className="text-accent" />}
                  </li>
                );
              })}
            </ul>
            {(message || more) && (
              <div className="flex flex-wrap items-center gap-2 px-3 py-2 text-sm text-text-muted">
                {status === 'loading' && <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />}
                <span data-testid="combobox-mensaje">{message ?? more}</span>
                {status === 'error' && (
                  <button
                    type="button"
                    tabIndex={-1}
                    className="inline-flex min-h-11 cursor-pointer items-center gap-1 font-semibold text-accent hover:text-accent-hover"
                    onClick={retry}
                  >
                    <RotateCcw aria-hidden="true" className="size-4" />
                    Reintentar
                  </button>
                )}
              </div>
            )}
          </div>
          <p role="status" aria-live="polite" className="sr-only">
            {announcement}
          </p>
        </div>
      )}
    </Field>
  );
}
