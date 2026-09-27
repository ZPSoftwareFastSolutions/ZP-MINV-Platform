// Filtro de precio: dos campos numéricos (mínimo y máximo) y un deslizador doble accesible (dos `<input type="range">`
// superpuestos, cada uno con su etiqueta). El valor se confirma al soltar el deslizador o al salir del campo, para no
// reescribir la URL en cada píxel arrastrado.

import clsx from 'clsx';
import { useId, useState, type KeyboardEvent } from 'react';
import type { PriceRange } from '@/1-domain/catalog/products';
import { formatMoney } from '@/shared/format';

export interface PriceSelection {
  min?: number;
  max?: number;
}

export interface PriceRangeFilterProps {
  /** Mínimo y máximo posibles (facetas sin el filtro de precio). */
  bounds: PriceRange;
  value: PriceSelection;
  onCommit: (next: PriceSelection) => void;
}

const THUMB =
  'pointer-events-none absolute inset-x-0 top-1/2 h-11 w-full -translate-y-1/2 appearance-none bg-transparent outline-none ' +
  '[&::-webkit-slider-runnable-track]:bg-transparent [&::-moz-range-track]:bg-transparent ' +
  '[&::-webkit-slider-thumb]:pointer-events-auto [&::-webkit-slider-thumb]:size-6 [&::-webkit-slider-thumb]:cursor-grab [&::-webkit-slider-thumb]:appearance-none ' +
  '[&::-webkit-slider-thumb]:rounded-full [&::-webkit-slider-thumb]:border-[3px] [&::-webkit-slider-thumb]:border-bg [&::-webkit-slider-thumb]:bg-accent [&::-webkit-slider-thumb]:shadow-glow-accent ' +
  '[&::-webkit-slider-thumb]:transition-[background-color] [&::-webkit-slider-thumb]:duration-200 [&:hover::-webkit-slider-thumb]:bg-accent-hover [&:active::-webkit-slider-thumb]:cursor-grabbing ' +
  '[&::-moz-range-thumb]:pointer-events-auto [&::-moz-range-thumb]:size-6 [&::-moz-range-thumb]:cursor-grab [&::-moz-range-thumb]:rounded-full ' +
  '[&::-moz-range-thumb]:border-[3px] [&::-moz-range-thumb]:border-bg [&::-moz-range-thumb]:bg-accent ' +
  'focus-visible:[&::-webkit-slider-thumb]:outline-2 focus-visible:[&::-webkit-slider-thumb]:outline-offset-2 focus-visible:[&::-webkit-slider-thumb]:outline-accent ' +
  'focus-visible:[&::-moz-range-thumb]:outline-2 focus-visible:[&::-moz-range-thumb]:outline-offset-2 focus-visible:[&::-moz-range-thumb]:outline-accent ' +
  'disabled:[&::-webkit-slider-thumb]:cursor-not-allowed disabled:opacity-40';

const INPUT =
  'h-11 w-full rounded-xl border border-border bg-surface-2 pl-9 pr-3 text-sm text-text tabular-nums transition-colors duration-200 ' +
  'hover:border-border-strong focus:border-accent focus:outline-none disabled:cursor-not-allowed disabled:opacity-50 ' +
  '[appearance:textfield] [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none';

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}

function parse(text: string): number | undefined {
  if (text.trim() === '') return undefined;
  const parsed = Number(text.replace(',', '.'));
  return Number.isFinite(parsed) ? parsed : undefined;
}

export function PriceRangeFilter({ bounds, value, onCommit }: PriceRangeFilterProps) {
  const id = useId();
  const lo = Math.floor(bounds.min);
  const hi = Math.ceil(bounds.max);
  const disabled = hi <= lo;
  const committedMin = clamp(value.min ?? lo, lo, hi);
  const committedMax = clamp(value.max ?? hi, lo, hi);

  // Los campos guardan texto (para poder borrar y escribir); el deslizador lee los números válidos de ese texto.
  const [minText, setMinText] = useState(String(committedMin));
  const [maxText, setMaxText] = useState(String(committedMax));
  const syncKey = `${lo}|${hi}|${committedMin}|${committedMax}`;
  const [syncedKey, setSyncedKey] = useState(syncKey);
  if (syncedKey !== syncKey) {
    setSyncedKey(syncKey);
    setMinText(String(committedMin));
    setMaxText(String(committedMax));
  }

  const sliderMin = clamp(parse(minText) ?? committedMin, lo, hi);
  const sliderMax = clamp(parse(maxText) ?? committedMax, lo, hi);
  const range = Math.max(1, hi - lo);
  const startPercent = ((Math.min(sliderMin, sliderMax) - lo) / range) * 100;
  const endPercent = ((Math.max(sliderMin, sliderMax) - lo) / range) * 100;

  const commit = (rawMin: number, rawMax: number) => {
    let nextMin = clamp(Math.round(rawMin), lo, hi);
    let nextMax = clamp(Math.round(rawMax), lo, hi);
    if (nextMin > nextMax) [nextMin, nextMax] = [nextMax, nextMin];
    setMinText(String(nextMin));
    setMaxText(String(nextMax));
    const next: PriceSelection = { min: nextMin > lo ? nextMin : undefined, max: nextMax < hi ? nextMax : undefined };
    if (next.min !== value.min || next.max !== value.max) onCommit(next);
  };

  const commitFields = () => commit(parse(minText) ?? lo, parse(maxText) ?? hi);
  const onFieldKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      event.preventDefault();
      commitFields();
    }
  };
  const commitSlider = () => commit(sliderMin, sliderMax);

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-3">
        <div>
          <label htmlFor={`${id}-min`} className="mb-1 block text-xs font-medium text-text-muted">
            Mínimo
          </label>
          <div className="relative">
            <span aria-hidden="true" className="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-xs font-semibold text-text-faint">
              Bs
            </span>
            <input
              id={`${id}-min`}
              type="number"
              inputMode="numeric"
              min={lo}
              max={hi}
              step={1}
              value={minText}
              disabled={disabled}
              onChange={(event) => setMinText(event.target.value)}
              onBlur={commitFields}
              onKeyDown={onFieldKeyDown}
              className={INPUT}
            />
          </div>
        </div>
        <div>
          <label htmlFor={`${id}-max`} className="mb-1 block text-xs font-medium text-text-muted">
            Máximo
          </label>
          <div className="relative">
            <span aria-hidden="true" className="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-xs font-semibold text-text-faint">
              Bs
            </span>
            <input
              id={`${id}-max`}
              type="number"
              inputMode="numeric"
              min={lo}
              max={hi}
              step={1}
              value={maxText}
              disabled={disabled}
              onChange={(event) => setMaxText(event.target.value)}
              onBlur={commitFields}
              onKeyDown={onFieldKeyDown}
              className={INPUT}
            />
          </div>
        </div>
      </div>

      <div role="group" aria-label="Rango de precio" className="relative h-11 px-3">
        <div aria-hidden="true" className="absolute inset-x-3 top-1/2 h-1.5 -translate-y-1/2 rounded-full bg-surface-3" />
        <div
          aria-hidden="true"
          className="absolute top-1/2 h-1.5 -translate-y-1/2 rounded-full bg-linear-to-r from-primary to-accent"
          style={{ left: `calc(0.75rem + ${startPercent}% * (100% - 1.5rem) / 100%)`, right: `calc(0.75rem + ${100 - endPercent}% * (100% - 1.5rem) / 100%)` }}
        />
        <input
          type="range"
          aria-label="Precio mínimo"
          aria-valuetext={formatMoney(sliderMin, { decimals: 0 })}
          min={lo}
          max={hi}
          step={1}
          value={sliderMin}
          disabled={disabled}
          onChange={(event) => setMinText(String(Math.min(Number(event.target.value), sliderMax)))}
          onPointerUp={commitSlider}
          onKeyUp={commitSlider}
          onBlur={commitSlider}
          className={clsx(THUMB, sliderMin > lo + range / 2 ? 'z-30' : 'z-10')}
        />
        <input
          type="range"
          aria-label="Precio máximo"
          aria-valuetext={formatMoney(sliderMax, { decimals: 0 })}
          min={lo}
          max={hi}
          step={1}
          value={sliderMax}
          disabled={disabled}
          onChange={(event) => setMaxText(String(Math.max(Number(event.target.value), sliderMin)))}
          onPointerUp={commitSlider}
          onKeyUp={commitSlider}
          onBlur={commitSlider}
          className={clsx(THUMB, 'z-20')}
        />
      </div>

      <p className="flex items-center justify-between text-xs text-text-muted tabular-nums">
        <span>{formatMoney(sliderMin, { decimals: 0 })}</span>
        <span>{formatMoney(sliderMax, { decimals: 0 })}</span>
      </p>
    </div>
  );
}
