// USO · Rango de fechas con atajos (Todas, Hoy, Ayer, Últimos 7 días, Este mes, Mes anterior) y dos fechas «Desde» y
// «Hasta» para elegir a mano. Los días son los de La Paz y los dos extremos se incluyen.
//
//   <DateRangeField label="Fechas" value={tabla.dateRange()} onChange={(rango) => tabla.setDateRange(rango)} />
//
// Pulsar otra vez el atajo marcado quita el filtro. Si «Desde» queda después de «Hasta» se avisa en el campo. Ocupa dos
// columnas de la barra de filtros desde 640 px (cámbielo con `className`).

import clsx from 'clsx';
import { useId, type ReactNode } from 'react';
import { Chip } from '@/4-presentation/components/ui/Chip';
import { DATE_SHORTCUTS, EMPTY_RANGE, isRangeActive, matchingShortcut, rangeError, shortcutRange, type DateRange, type DateShortcutId } from '../lib/dates';
import { describedBy } from './aria';
import { CONTROL, ERROR, HINT, LABEL } from './styles';

export interface DateRangeFieldProps {
  label: string;
  value: DateRange;
  onChange: (range: DateRange) => void;
  /** Atajos a mostrar (por defecto todos); false = sin atajos. */
  shortcuts?: readonly DateShortcutId[] | false;
  hint?: ReactNode;
  /** Error propio de la pantalla (el de un rango al revés lo pone el campo). */
  error?: ReactNode;
  /** Fecha de hoy para los atajos (pruebas). */
  now?: Date;
  disabled?: boolean;
  className?: string;
}

export function DateRangeField({ label, value, onChange, shortcuts, hint, error, now, disabled = false, className }: DateRangeFieldProps) {
  const id = useId();
  const shown = shortcuts === false ? [] : DATE_SHORTCUTS.filter((shortcut) => !shortcuts || shortcuts.includes(shortcut.id));
  const active = matchingShortcut(value, now);
  const problem = error ?? rangeError(value);
  const describe = describedBy(id, hint, problem);

  return (
    <fieldset className={clsx('min-w-0 sm:col-span-2', className)} aria-describedby={describe} disabled={disabled}>
      <legend className={clsx(LABEL, 'mb-1')}>{label}</legend>
      {shown.length > 0 && (
        <div className="mb-2 flex flex-wrap gap-2">
          <Chip selected={!isRangeActive(value)} onClick={() => onChange(EMPTY_RANGE)}>
            Todas
          </Chip>
          {shown.map((shortcut) => (
            <Chip key={shortcut.id} selected={active === shortcut.id} onClick={() => onChange(active === shortcut.id ? EMPTY_RANGE : shortcutRange(shortcut.id, now))}>
              {shortcut.label}
            </Chip>
          ))}
        </div>
      )}
      {/* Las dos fechas lado a lado desde 400 px; en un teléfono angosto, una debajo de la otra (se leen completas). */}
      <div className="grid grid-cols-1 gap-2 min-[25rem]:grid-cols-2">
        <div className="min-w-0">
          <label htmlFor={`${id}-desde`} className="mb-1 block text-xs text-text-muted">
            Desde
          </label>
          <input
            id={`${id}-desde`}
            type="date"
            value={value.from ?? ''}
            max={value.to ?? undefined}
            aria-invalid={problem ? true : undefined}
            onChange={(event) => onChange({ from: event.target.value || null, to: value.to })}
            className={clsx(CONTROL, 'px-2 [color-scheme:dark]')}
          />
        </div>
        <div className="min-w-0">
          <label htmlFor={`${id}-hasta`} className="mb-1 block text-xs text-text-muted">
            Hasta
          </label>
          <input
            id={`${id}-hasta`}
            type="date"
            value={value.to ?? ''}
            min={value.from ?? undefined}
            aria-invalid={problem ? true : undefined}
            onChange={(event) => onChange({ from: value.from, to: event.target.value || null })}
            className={clsx(CONTROL, 'px-2 [color-scheme:dark]')}
          />
        </div>
      </div>
      {hint && (
        <p id={`${id}-ayuda`} className={HINT}>
          {hint}
        </p>
      )}
      {problem && (
        <p id={`${id}-error`} className={ERROR}>
          {problem}
        </p>
      )}
    </fieldset>
  );
}
