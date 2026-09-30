// Módulo «Reportes» · los campos del PERÍODO dentro de la barra de filtros de cada reporte: la lista «Período» (atajos) y
// las fechas «Desde» y «Hasta». Elegir un atajo borra las fechas a mano; escribir una fecha pasa a «Personalizado».

import { DateRangeField, SelectField } from '@/4-presentation/panel/kit';
import { CUSTOM_PERIOD, PERIOD_SELECT_OPTIONS, filtersForDates, filtersForPeriodChoice, rangeText, type PeriodFilters, type ResolvedPeriod } from './period';

export interface PeriodFieldsProps {
  period: ResolvedPeriod;
  onChange: (values: Partial<PeriodFilters>) => void;
}

export function PeriodFields({ period, onChange }: PeriodFieldsProps) {
  return (
    <>
      <SelectField
        label="Período"
        allLabel={false}
        value={period.id}
        onChange={(value) => onChange(filtersForPeriodChoice(value, period))}
        options={PERIOD_SELECT_OPTIONS}
        hint={period.problem ? undefined : period.id === CUSTOM_PERIOD ? `Fechas elegidas: ${rangeText(period)}.` : `Días de La Paz: ${rangeText(period)}.`}
      />
      <DateRangeField label="Fechas" shortcuts={false} value={{ from: period.from, to: period.to }} onChange={(range) => onChange(filtersForDates(range))} error={period.problem ?? undefined} />
    </>
  );
}
