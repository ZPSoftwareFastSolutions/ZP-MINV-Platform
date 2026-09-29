// Barra de filtros y campos del panel: contador y «Limpiar filtros», lista desplegable con «Todos», búsqueda con espera
// al escribir, rango de fechas con atajos, números en el formato de Bolivia, casillas, interruptor y opciones.

import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { EMPTY_RANGE, type DateRange } from '../lib/dates';
import { Checkbox, RadioGroup, Switch } from './Choice';
import { DateRangeField } from './DateRangeField';
import { FilterBar } from './FilterBar';
import { MoneyField, NumberField } from './NumberField';
import { SearchField } from './SearchField';
import { SelectField } from './SelectField';
import { TextArea, TextField } from './TextField';

afterEach(() => {
  vi.useRealTimers();
});

describe('FilterBar', () => {
  it('muestra cuántos filtros están activos y «Limpiar filtros» los borra', () => {
    const onClear = vi.fn();
    const { rerender } = render(
      <FilterBar activeCount={2} onClear={onClear}>
        <p>controles</p>
      </FilterBar>,
    );
    expect(screen.getByRole('region', { name: 'Filtros' })).toBeInTheDocument();
    expect(screen.getByTestId('filtros-activos')).toHaveTextContent('2 activos');
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    expect(onClear).toHaveBeenCalledTimes(1);

    rerender(
      <FilterBar activeCount={0} onClear={onClear}>
        <p>controles</p>
      </FilterBar>,
    );
    expect(screen.getByTestId('filtros-activos')).toHaveTextContent('Ninguno activo');
    const clear = screen.getByRole('button', { name: 'Limpiar filtros' });
    expect(clear).toHaveAttribute('aria-disabled', 'true');
    fireEvent.click(clear);
    expect(onClear).toHaveBeenCalledTimes(1);
  });

  it('en el teléfono se pliega con «Mostrar filtros» (aria-expanded)', () => {
    render(
      <FilterBar activeCount={1} onClear={vi.fn()}>
        <p>controles</p>
      </FilterBar>,
    );
    const toggle = screen.getByRole('button', { name: 'Ocultar filtros' });
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    fireEvent.click(toggle);
    expect(screen.getByRole('button', { name: 'Mostrar filtros' })).toHaveAttribute('aria-expanded', 'false');
  });
});

describe('SelectField', () => {
  const OPTIONS = [
    { value: 'pagada', label: 'Pagada' },
    { value: 'anulada', label: 'Anulada' },
  ] as const;

  it('trae «Todos» primero (vale "") y avisa el valor elegido', () => {
    const onChange = vi.fn();
    render(<SelectField label="Estado" value="" onChange={onChange} options={OPTIONS} />);
    const select = screen.getByLabelText('Estado') as HTMLSelectElement;
    expect([...select.options].map((option) => option.textContent)).toEqual(['Todos', 'Pagada', 'Anulada']);
    expect(select.value).toBe('');
    fireEvent.change(select, { target: { value: 'anulada' } });
    expect(onChange).toHaveBeenCalledWith('anulada');
  });

  it('en un formulario: sin «Todos», con la opción vacía deshabilitada, error enlazado y requerido', () => {
    render(<SelectField label="Sucursal" allLabel={false} placeholder="Elija una sucursal" required value="" onChange={vi.fn()} options={OPTIONS} error="Elija una sucursal." />);
    const select = screen.getByRole('combobox', { name: /Sucursal/ }) as HTMLSelectElement;
    expect(select.options[0]).toHaveTextContent('Elija una sucursal');
    expect(select.options[0].disabled).toBe(true);
    expect(select).toBeRequired();
    expect(select).toHaveAttribute('aria-invalid', 'true');
    expect(select).toHaveAccessibleDescription('Elija una sucursal.');
  });

  it('un valor que ya no está en la lista se sigue viendo', () => {
    render(<SelectField label="Estado" value={'vieja' as 'pagada'} onChange={vi.fn()} options={OPTIONS} allLabel="Todos los estados" />);
    expect((screen.getByLabelText('Estado') as HTMLSelectElement).value).toBe('vieja');
  });
});

function SearchHarness({ onApplied, delay }: { onApplied: (value: string) => void; delay?: number }) {
  const [value, setValue] = useState('');
  return (
    <>
      <SearchField
        label="Buscar"
        value={value}
        delay={delay}
        onChange={(next) => {
          setValue(next);
          onApplied(next);
        }}
      />
      <button type="button" onClick={() => setValue('')}>
        Limpiar desde afuera
      </button>
      <p data-testid="aplicado">{value}</p>
    </>
  );
}

describe('SearchField', () => {
  it('espera a que se deje de escribir antes de avisar', () => {
    vi.useFakeTimers();
    const onApplied = vi.fn();
    render(<SearchHarness onApplied={onApplied} />);
    const input = screen.getByRole('searchbox', { name: 'Buscar' });
    fireEvent.change(input, { target: { value: 'mon' } });
    act(() => vi.advanceTimersByTime(200));
    fireEvent.change(input, { target: { value: 'monitor ' } });
    act(() => vi.advanceTimersByTime(299));
    expect(onApplied).not.toHaveBeenCalled();
    act(() => vi.advanceTimersByTime(1));
    expect(onApplied).toHaveBeenCalledTimes(1);
    expect(onApplied).toHaveBeenCalledWith('monitor');
    expect(screen.getByTestId('aplicado')).toHaveTextContent('monitor');
  });

  it('Enter aplica al instante; Escape y la «×» borran', () => {
    vi.useFakeTimers();
    const onApplied = vi.fn();
    render(<SearchHarness onApplied={onApplied} />);
    const input = screen.getByRole('searchbox', { name: 'Buscar' });
    fireEvent.change(input, { target: { value: 'ssd' } });
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(onApplied).toHaveBeenLastCalledWith('ssd');
    fireEvent.click(screen.getByRole('button', { name: 'Borrar la búsqueda' }));
    expect(onApplied).toHaveBeenLastCalledWith('');
    expect(input).toHaveValue('');
    fireEvent.change(input, { target: { value: 'ram' } });
    fireEvent.keyDown(input, { key: 'Escape' });
    expect(input).toHaveValue('');
    act(() => vi.runAllTimers());
    expect(onApplied).toHaveBeenCalledTimes(2);
  });

  it('si el valor cambia desde afuera («Limpiar filtros»), el campo lo sigue', () => {
    vi.useFakeTimers();
    render(<SearchHarness onApplied={vi.fn()} />);
    const input = screen.getByRole('searchbox', { name: 'Buscar' });
    fireEvent.change(input, { target: { value: 'teclado' } });
    act(() => vi.runAllTimers());
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar desde afuera' }));
    expect(input).toHaveValue('');
  });
});

function RangeHarness({ now, onRange }: { now: Date; onRange?: (range: DateRange) => void }) {
  const [range, setRange] = useState<DateRange>(EMPTY_RANGE);
  return (
    <DateRangeField
      label="Fechas"
      now={now}
      value={range}
      onChange={(next) => {
        setRange(next);
        onRange?.(next);
      }}
    />
  );
}

describe('DateRangeField', () => {
  const now = new Date('2026-09-29T01:30:00Z'); // 28/09/2026 en La Paz

  it('los atajos llenan «Desde» y «Hasta» con los días de La Paz y quedan marcados', () => {
    const onRange = vi.fn();
    render(<RangeHarness now={now} onRange={onRange} />);
    const group = screen.getByRole('group', { name: 'Fechas' });
    expect(within(group).getByRole('button', { name: 'Todas' })).toHaveAttribute('aria-pressed', 'true');
    fireEvent.click(within(group).getByRole('button', { name: 'Este mes' }));
    expect(onRange).toHaveBeenLastCalledWith({ from: '2026-09-01', to: '2026-09-28' });
    expect(screen.getByLabelText('Desde')).toHaveValue('2026-09-01');
    expect(screen.getByLabelText('Hasta')).toHaveValue('2026-09-28');
    expect(within(group).getByRole('button', { name: 'Este mes' })).toHaveAttribute('aria-pressed', 'true');
    fireEvent.click(within(group).getByRole('button', { name: 'Mes anterior' }));
    expect(onRange).toHaveBeenLastCalledWith({ from: '2026-08-01', to: '2026-08-31' });
    fireEvent.click(within(group).getByRole('button', { name: 'Ayer' }));
    expect(onRange).toHaveBeenLastCalledWith({ from: '2026-09-27', to: '2026-09-27' });
    // Pulsar otra vez el atajo marcado quita el filtro.
    fireEvent.click(within(group).getByRole('button', { name: 'Ayer' }));
    expect(onRange).toHaveBeenLastCalledWith({ from: null, to: null });
  });

  it('se puede elegir a mano y avisa si «Desde» queda después de «Hasta»', () => {
    render(<RangeHarness now={now} />);
    fireEvent.change(screen.getByLabelText('Desde'), { target: { value: '2026-09-20' } });
    fireEvent.change(screen.getByLabelText('Hasta'), { target: { value: '2026-09-10' } });
    expect(screen.getByText('La fecha «Desde» no puede ser posterior a «Hasta».')).toBeInTheDocument();
    expect(screen.getByLabelText('Desde')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByRole('group', { name: 'Fechas' })).toHaveAccessibleDescription('La fecha «Desde» no puede ser posterior a «Hasta».');
  });
});

function NumberHarness({ money = false, decimals, onValue }: { money?: boolean; decimals?: number; onValue: (value: number | null) => void }) {
  const [value, setValue] = useState<number | null>(null);
  const change = (next: number | null) => {
    setValue(next);
    onValue(next);
  };
  return money ? <MoneyField label="Precio" value={value} onChange={change} /> : <NumberField label="Cantidad" unit="u." decimals={decimals} value={value} onChange={change} />;
}

describe('NumberField y MoneyField', () => {
  it('leen el formato de Bolivia y dan formato al salir del campo', () => {
    const onValue = vi.fn();
    render(<NumberHarness money onValue={onValue} />);
    const input = screen.getByLabelText('Precio');
    expect(input).toHaveAttribute('inputmode', 'decimal');
    fireEvent.change(input, { target: { value: '1234,5' } });
    expect(onValue).toHaveBeenLastCalledWith(1234.5);
    fireEvent.blur(input);
    expect(input).toHaveValue('1.234,50');
    fireEvent.change(input, { target: { value: '1.500' } });
    expect(onValue).toHaveBeenLastCalledWith(1500);
  });

  it('un texto que no es un número queda marcado y el valor es null', () => {
    const onValue = vi.fn();
    render(<NumberHarness onValue={onValue} />);
    const input = screen.getByLabelText('Cantidad');
    expect(input).toHaveAttribute('inputmode', 'numeric');
    fireEvent.change(input, { target: { value: '12' } });
    expect(onValue).toHaveBeenLastCalledWith(12);
    fireEvent.change(input, { target: { value: '12a' } });
    expect(onValue).toHaveBeenLastCalledWith(null);
    expect(input).toHaveValue('12a');
    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(input).toHaveAccessibleDescription('Escriba un número, por ejemplo 1.500.');
    fireEvent.change(input, { target: { value: '2,5' } });
    expect(screen.getByText('Escriba un número entero.')).toBeInTheDocument();
    fireEvent.change(input, { target: { value: '-3' } });
    expect(screen.getByText('Escriba un número positivo.')).toBeInTheDocument();
    fireEvent.change(input, { target: { value: '' } });
    expect(input).not.toHaveAttribute('aria-invalid');
  });

  it('respeta la cantidad de decimales', () => {
    const onValue = vi.fn();
    render(<NumberHarness decimals={2} onValue={onValue} />);
    fireEvent.change(screen.getByLabelText('Cantidad'), { target: { value: '1,25' } });
    expect(onValue).toHaveBeenLastCalledWith(1.25);
    fireEvent.change(screen.getByLabelText('Cantidad'), { target: { value: '1,255' } });
    expect(onValue).toHaveBeenLastCalledWith(null);
    expect(screen.getByText('Use como máximo 2 decimales.')).toBeInTheDocument();
  });

  it('si el valor cambia desde afuera, el campo lo muestra', () => {
    const { rerender } = render(<NumberField label="Cantidad" value={5} onChange={vi.fn()} />);
    expect(screen.getByLabelText('Cantidad')).toHaveValue('5');
    rerender(<NumberField label="Cantidad" value={1500} onChange={vi.fn()} />);
    expect(screen.getByLabelText('Cantidad')).toHaveValue('1.500');
    rerender(<NumberField label="Cantidad" value={null} onChange={vi.fn()} />);
    expect(screen.getByLabelText('Cantidad')).toHaveValue('');
  });
});

describe('texto, casillas, interruptor y opciones', () => {
  it('TextField y TextArea trabajan con el valor, con etiqueta, ayuda, error y contador', () => {
    const onName = vi.fn();
    render(
      <>
        <TextField label="Nombre" required value="" onChange={onName} error="Escriba el nombre." />
        <TextArea label="Motivo" value="Cliente arrepentido" onChange={vi.fn()} maxLength={200} hint="Queda en la bitácora." />
      </>,
    );
    const name = screen.getByRole('textbox', { name: /Nombre/ });
    fireEvent.change(name, { target: { value: 'Ana' } });
    expect(onName).toHaveBeenCalledWith('Ana');
    expect(name).toBeRequired();
    expect(name).toHaveAccessibleDescription('Escriba el nombre.');
    expect(screen.getByRole('textbox', { name: 'Motivo' })).toHaveAccessibleDescription('Queda en la bitácora.');
    expect(screen.getByText('19 / 200')).toBeInTheDocument();
  });

  it('Checkbox, Switch y RadioGroup', () => {
    const onCheck = vi.fn();
    const onSwitch = vi.fn();
    const onRadio = vi.fn();
    render(
      <>
        <Checkbox label="Incluir anuladas" description="Se muestran en gris." checked={false} onChange={onCheck} />
        <Switch label="Producto activo" checked onChange={onSwitch} />
        <RadioGroup
          label="Documento"
          value="factura"
          onChange={onRadio}
          options={[
            { value: 'factura', label: 'Factura' },
            { value: 'recibo', label: 'Recibo', description: 'Sin crédito fiscal' },
          ]}
        />
      </>,
    );
    const box = screen.getByRole('checkbox', { name: 'Incluir anuladas' });
    expect(box).toHaveAccessibleDescription('Se muestran en gris.');
    fireEvent.click(box);
    expect(onCheck).toHaveBeenCalledWith(true);

    const toggle = screen.getByRole('switch', { name: 'Producto activo' });
    expect(toggle).toHaveAttribute('aria-checked', 'true');
    fireEvent.click(toggle);
    expect(onSwitch).toHaveBeenCalledWith(false);

    const group = screen.getByRole('group', { name: 'Documento' });
    expect(within(group).getByRole('radio', { name: 'Factura' })).toBeChecked();
    fireEvent.click(within(group).getByRole('radio', { name: /Recibo/ }));
    expect(onRadio).toHaveBeenCalledWith('recibo');
  });
});
