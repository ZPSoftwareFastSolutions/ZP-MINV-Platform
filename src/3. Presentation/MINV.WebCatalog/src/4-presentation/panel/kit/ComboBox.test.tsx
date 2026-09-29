// ComboBox (patrón combobox de ARIA 1.2): filtrar escribiendo, flechas, Enter, Escape, Tab, opciones deshabilitadas,
// quitar la selección y carga asíncrona con espera, «Buscando…», respuestas viejas descartadas y «Reintentar».

import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { ComboBox, type ComboBoxProps, type ComboOption } from './ComboBox';

const OPTIONS: ComboOption[] = [
  { value: 'MON-27', label: 'Monitor 27" QHD', description: 'MON-27 · stock 12' },
  { value: 'MON-24', label: 'Monitor 24" Full HD', description: 'MON-24 · stock 20' },
  { value: 'SSD-1T', label: 'SSD NVMe 1 TB', description: 'SSD-1T' },
  { value: 'TEC-TKL', label: 'Teclado mecánico TKL', description: 'Agotado', disabled: true },
  { value: 'RAT-26K', label: 'Mouse gamer', description: 'RAT-26K' },
];

function Harness(props: Partial<ComboBoxProps> & { initial?: ComboOption | null; onPick?: (option: ComboOption | null) => void }) {
  const { initial = null, onPick, ...rest } = props;
  const [value, setValue] = useState<ComboOption | null>(initial);
  return (
    <>
      <ComboBox
        label="Producto"
        options={OPTIONS}
        {...rest}
        value={value}
        onChange={(option) => {
          setValue(option);
          onPick?.(option);
        }}
      />
      <p data-testid="elegido">{value?.value ?? 'ninguno'}</p>
      <button type="button">Otro control</button>
    </>
  );
}

function input() {
  return screen.getByRole('combobox', { name: 'Producto' });
}

function visibleOptions(): string[] {
  return within(screen.getByRole('listbox', { hidden: true })).queryAllByRole('option', { hidden: true }).map((option) => option.textContent ?? '');
}

function activeOption(): HTMLElement | null {
  const id = input().getAttribute('aria-activedescendant');
  return id ? document.getElementById(id) : null;
}

describe('ComboBox con lista fija', () => {
  it('escribir filtra; las flechas mueven la opción activa y Enter elige', () => {
    const onPick = vi.fn();
    render(<Harness onPick={onPick} />);
    expect(input()).toHaveAttribute('aria-expanded', 'false');
    fireEvent.change(input(), { target: { value: 'moni' } });
    expect(input()).toHaveAttribute('aria-expanded', 'true');
    expect(visibleOptions()).toEqual(['Monitor 27" QHDMON-27 · stock 12', 'Monitor 24" Full HDMON-24 · stock 20']);
    expect(activeOption()).toHaveTextContent('Monitor 27" QHD');
    expect(screen.getByRole('status')).toHaveTextContent('2 resultados');

    fireEvent.keyDown(input(), { key: 'ArrowDown' });
    expect(activeOption()).toHaveTextContent('Monitor 24" Full HD');
    fireEvent.keyDown(input(), { key: 'ArrowDown' });
    expect(activeOption()).toHaveTextContent('Monitor 27" QHD');
    fireEvent.keyDown(input(), { key: 'ArrowUp' });
    expect(activeOption()).toHaveTextContent('Monitor 24" Full HD');

    fireEvent.keyDown(input(), { key: 'Enter' });
    expect(onPick).toHaveBeenCalledWith(OPTIONS[1]);
    expect(input()).toHaveValue('Monitor 24" Full HD');
    expect(input()).toHaveAttribute('aria-expanded', 'false');
    expect(input()).not.toHaveAttribute('aria-activedescendant');
    expect(screen.getByTestId('elegido')).toHaveTextContent('MON-24');
  });

  it('busca sin acentos ni mayúsculas, por nombre, descripción o valor, y salta las opciones deshabilitadas', () => {
    const onPick = vi.fn();
    render(<Harness onPick={onPick} />);
    fireEvent.change(input(), { target: { value: 'NVME' } });
    expect(visibleOptions()).toEqual(['SSD NVMe 1 TBSSD-1T']);
    fireEvent.change(input(), { target: { value: 'rat-26' } });
    expect(visibleOptions()).toEqual(['Mouse gamerRAT-26K']);
    fireEvent.change(input(), { target: { value: 'MECANICO' } });
    const disabled = screen.getByRole('option', { name: /Teclado mecánico/ });
    expect(disabled).toHaveAttribute('aria-disabled', 'true');
    // La única opción está deshabilitada: no queda activa y Enter no la elige.
    expect(input()).not.toHaveAttribute('aria-activedescendant');
    fireEvent.keyDown(input(), { key: 'Enter' });
    fireEvent.click(disabled);
    expect(onPick).not.toHaveBeenCalled();
    fireEvent.change(input(), { target: { value: 'zzz' } });
    expect(visibleOptions()).toEqual([]);
    expect(screen.getByTestId('combobox-mensaje')).toHaveTextContent('Sin resultados');
    expect(screen.getByRole('status')).toHaveTextContent('Sin resultados');
  });

  it('↓ con la lista cerrada la abre en la opción elegida; Escape cierra y otra vez deshace lo escrito', () => {
    render(<Harness initial={OPTIONS[2]} />);
    expect(input()).toHaveValue('SSD NVMe 1 TB');
    fireEvent.keyDown(input(), { key: 'ArrowDown' });
    expect(input()).toHaveAttribute('aria-expanded', 'true');
    expect(activeOption()).toHaveTextContent('SSD NVMe 1 TB');
    expect(screen.getByRole('option', { name: /SSD NVMe/ })).toHaveAttribute('aria-selected', 'true');

    fireEvent.change(input(), { target: { value: 'mou' } });
    fireEvent.keyDown(input(), { key: 'Escape' });
    expect(input()).toHaveAttribute('aria-expanded', 'false');
    expect(input()).toHaveValue('mou');
    fireEvent.keyDown(input(), { key: 'Escape' });
    expect(input()).toHaveValue('SSD NVMe 1 TB');
    expect(screen.getByTestId('elegido')).toHaveTextContent('SSD-1T');
  });

  it('al salir sin elegir vuelve la selección; borrar el texto y salir la quita; la «×» también', () => {
    render(<Harness initial={OPTIONS[0]} />);
    fireEvent.change(input(), { target: { value: 'ssd' } });
    fireEvent.blur(input(), { relatedTarget: screen.getByRole('button', { name: 'Otro control' }) });
    expect(input()).toHaveValue('Monitor 27" QHD');
    expect(screen.getByTestId('elegido')).toHaveTextContent('MON-27');

    fireEvent.change(input(), { target: { value: '' } });
    fireEvent.blur(input(), { relatedTarget: null });
    expect(screen.getByTestId('elegido')).toHaveTextContent('ninguno');
    expect(input()).toHaveValue('');

    fireEvent.change(input(), { target: { value: 'mouse' } });
    fireEvent.keyDown(input(), { key: 'Enter' });
    expect(screen.getByTestId('elegido')).toHaveTextContent('RAT-26K');
    fireEvent.click(screen.getByRole('button', { name: 'Quitar producto' }));
    expect(screen.getByTestId('elegido')).toHaveTextContent('ninguno');
    expect(input()).toHaveFocus();
  });

  it('un clic en una opción la elige sin perder el foco del campo', () => {
    render(<Harness />);
    fireEvent.click(input());
    expect(input()).toHaveAttribute('aria-expanded', 'true');
    expect(visibleOptions()).toHaveLength(5);
    fireEvent.click(screen.getByRole('option', { name: /Mouse gamer/ }));
    expect(screen.getByTestId('elegido')).toHaveTextContent('RAT-26K');
  });

  it('obligatoria: sin «×» y requerida', () => {
    render(<Harness initial={OPTIONS[0]} required />);
    expect(input()).toBeRequired();
    expect(screen.queryByRole('button', { name: 'Quitar producto' })).not.toBeInTheDocument();
  });
});

interface Deferred<T> {
  promise: Promise<T>;
  resolve(value: T): void;
  reject(error: unknown): void;
}

function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((ok, fail) => {
    resolve = ok;
    reject = fail;
  });
  return { promise, resolve, reject };
}

describe('ComboBox con carga asíncrona', () => {
  it('pide al menos 2 caracteres, espera, muestra «Buscando…» y los resultados', async () => {
    const pending = deferred<readonly ComboOption[]>();
    const loadOptions = vi.fn(() => pending.promise);
    render(<Harness options={undefined} loadOptions={loadOptions} delay={5} />);
    fireEvent.change(input(), { target: { value: 'm' } });
    expect(screen.getByTestId('combobox-mensaje')).toHaveTextContent('Escriba al menos 2 caracteres para buscar.');
    expect(loadOptions).not.toHaveBeenCalled();
    fireEvent.change(input(), { target: { value: 'mo' } });
    expect(screen.getByTestId('combobox-mensaje')).toHaveTextContent('Buscando…');
    await waitFor(() => expect(loadOptions).toHaveBeenCalledTimes(1));
    expect(loadOptions).toHaveBeenCalledWith('mo', expect.any(AbortSignal));
    pending.resolve([OPTIONS[0], OPTIONS[4]]);
    expect(await screen.findByRole('option', { name: /Monitor 27/ })).toBeInTheDocument();
    expect(visibleOptions()).toHaveLength(2);
    fireEvent.keyDown(input(), { key: 'Enter' });
    expect(screen.getByTestId('elegido')).toHaveTextContent('MON-27');
  });

  it('descarta la respuesta vieja si la persona siguió escribiendo (y la cancela)', async () => {
    const first = deferred<readonly ComboOption[]>();
    const second = deferred<readonly ComboOption[]>();
    const signals: AbortSignal[] = [];
    const loadOptions = vi.fn((query: string, signal: AbortSignal) => {
      signals.push(signal);
      return query === 'mon' ? first.promise : second.promise;
    });
    render(<Harness options={undefined} loadOptions={loadOptions} delay={5} />);
    fireEvent.change(input(), { target: { value: 'mon' } });
    await waitFor(() => expect(loadOptions).toHaveBeenCalledTimes(1));
    fireEvent.change(input(), { target: { value: 'moni' } });
    expect(signals[0].aborted).toBe(true);
    await waitFor(() => expect(loadOptions).toHaveBeenCalledTimes(2));
    second.resolve([OPTIONS[1]]);
    expect(await screen.findByRole('option', { name: /Monitor 24/ })).toBeInTheDocument();
    first.resolve([OPTIONS[2]]);
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(visibleOptions()).toEqual(['Monitor 24" Full HDMON-24 · stock 20']);
  });

  it('si la búsqueda falla, ofrece «Reintentar» (también con Enter)', async () => {
    let calls = 0;
    const loadOptions = vi.fn(async () => {
      calls += 1;
      if (calls === 1) throw new Error('sin conexión');
      return [OPTIONS[2]];
    });
    render(<Harness options={undefined} loadOptions={loadOptions} delay={5} />);
    fireEvent.change(input(), { target: { value: 'ssd' } });
    await waitFor(() => expect(screen.getByTestId('combobox-mensaje')).toHaveTextContent('No se pudo buscar.'));
    expect(screen.getByRole('button', { name: 'Reintentar', hidden: true })).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Pulse Enter para reintentar');
    fireEvent.keyDown(input(), { key: 'Enter' });
    expect(await screen.findByRole('option', { name: /SSD NVMe/ })).toBeInTheDocument();
    expect(loadOptions).toHaveBeenCalledTimes(2);
  });
});
