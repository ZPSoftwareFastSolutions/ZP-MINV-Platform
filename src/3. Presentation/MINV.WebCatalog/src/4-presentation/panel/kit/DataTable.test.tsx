// DataTable: orden por columna (y su aria-sort), páginas con filas por página, selección (casillas y «todas las de la
// página»), totales con TODAS las filas, estados de carga, vacío y error con «Reintentar», fila desplegable, acciones por
// fila, apertura del detalle y vista de tarjetas por debajo de 640 px. Sin red.

import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import { formatMoney } from '../lib/format';
import type { SortState } from '../lib/table';
import { DataTable, type DataTableProps } from './DataTable';
import type { DataTableColumn } from './tableColumns';

interface Sale {
  number: string;
  customer: string;
  total: number;
}

const SALES: Sale[] = Array.from({ length: 23 }, (_, index) => ({
  number: `F-${String(index + 1).padStart(3, '0')}`,
  customer: index % 2 === 0 ? 'Carlos Quispe' : 'Ana Rojas',
  // Totales desordenados a propósito para ver el orden.
  total: ((index * 7) % 23) * 10 + 10,
}));

const COLUMNS: DataTableColumn<Sale>[] = [
  { id: 'numero', header: 'Número', value: (sale) => sale.number },
  { id: 'cliente', header: 'Cliente', value: (sale) => sale.customer },
  {
    id: 'total',
    header: 'Total',
    align: 'end',
    value: (sale) => sale.total,
    cell: (sale) => formatMoney(sale.total),
    footer: (rows) => formatMoney(rows.reduce((sum, sale) => sum + sale.total, 0)),
  },
];

function renderTable(props: Partial<DataTableProps<Sale>> = {}) {
  return render(<DataTable caption="Ventas" columns={COLUMNS} rows={SALES} rowKey={(sale) => sale.number} {...props} />);
}

/** Números de las filas de datos a la vista, en orden. */
function shownNumbers(): string[] {
  return screen
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'))
    .map((row) => row.getAttribute('data-row-key') ?? '');
}

function header(name: RegExp) {
  return screen.getByRole('columnheader', { name });
}

const originalMatchMedia = window.matchMedia;

afterEach(() => {
  window.matchMedia = originalMatchMedia;
});

describe('DataTable · orden', () => {
  it('ordena al pulsar el encabezado: ascendente → descendente → sin orden, con aria-sort', () => {
    renderTable({ paginate: false });
    expect(header(/Total/)).not.toHaveAttribute('aria-sort');
    const button = within(header(/Total/)).getByRole('button');

    fireEvent.click(button);
    expect(header(/Total/)).toHaveAttribute('aria-sort', 'ascending');
    const ascending = shownNumbers().map((number) => SALES.find((sale) => sale.number === number)!.total);
    expect(ascending).toEqual([...ascending].sort((a, b) => a - b));

    fireEvent.click(button);
    expect(header(/Total/)).toHaveAttribute('aria-sort', 'descending');
    const descending = shownNumbers().map((number) => SALES.find((sale) => sale.number === number)!.total);
    expect(descending).toEqual([...descending].sort((a, b) => b - a));

    fireEvent.click(button);
    expect(header(/Total/)).not.toHaveAttribute('aria-sort');
    expect(shownNumbers()).toEqual(SALES.map((sale) => sale.number));
  });

  it('con el orden controlado solo avisa (el estado lo tiene la pantalla, por ejemplo en la dirección)', () => {
    const onSortChange = vi.fn();
    renderTable({ paginate: false, sort: { column: 'numero', direction: 'desc' }, onSortChange });
    expect(header(/Número/)).toHaveAttribute('aria-sort', 'descending');
    expect(shownNumbers()[0]).toBe('F-023');
    fireEvent.click(within(header(/Cliente/)).getByRole('button'));
    expect(onSortChange).toHaveBeenCalledWith({ column: 'cliente', direction: 'asc' });
    expect(header(/Número/)).toHaveAttribute('aria-sort', 'descending');
  });

  it('una columna sin valor no se puede ordenar', () => {
    render(
      <DataTable
        caption="Ventas"
        columns={[...COLUMNS, { id: 'marca', header: 'Marca', cell: () => '★' }]}
        rows={SALES.slice(0, 2)}
        rowKey={(sale) => sale.number}
      />,
    );
    expect(within(header(/Marca/)).queryByRole('button')).not.toBeInTheDocument();
  });
});

describe('DataTable · páginas', () => {
  it('pagina en la página con filas por página y dice qué filas muestra', () => {
    renderTable();
    expect(screen.getByText('Mostrando 1–23 de 23')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Filas por página'), { target: { value: '10' } });
    expect(screen.getByText('Mostrando 1–10 de 23')).toBeInTheDocument();
    expect(shownNumbers()).toHaveLength(10);

    fireEvent.click(screen.getByRole('button', { name: 'Página siguiente' }));
    expect(screen.getByText('Mostrando 11–20 de 23')).toBeInTheDocument();
    expect(shownNumbers()[0]).toBe('F-011');
    fireEvent.click(screen.getByRole('button', { name: 'Página 3' }));
    expect(shownNumbers()).toEqual(['F-021', 'F-022', 'F-023']);
    expect(screen.getByRole('button', { name: 'Página 3' })).toHaveAttribute('aria-current', 'page');
  });

  it('controlada: avisa los cambios y una página fuera de rango muestra la última', () => {
    const onPageChange = vi.fn();
    const onPageSizeChange = vi.fn();
    renderTable({ page: 9, pageSize: 10, onPageChange, onPageSizeChange });
    expect(screen.getByText('Mostrando 21–23 de 23')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Página 1' }));
    expect(onPageChange).toHaveBeenCalledWith(1);
    fireEvent.change(screen.getByLabelText('Filas por página'), { target: { value: '50' } });
    expect(onPageSizeChange).toHaveBeenCalledWith(50);
  });
});

function SelectableTable() {
  const [selection, setSelection] = useState<Set<string>>(() => new Set());
  return <DataTable caption="Ventas" columns={COLUMNS} rows={SALES} rowKey={(sale) => sale.number} pageSize={10} selection={selection} onSelectionChange={setSelection} />;
}

describe('DataTable · selección', () => {
  it('marca filas sueltas, todas las de la página y las quita', () => {
    render(<SelectableTable />);
    const all = screen.getByRole('checkbox', { name: 'Seleccionar todas las filas de esta página' }) as HTMLInputElement;
    fireEvent.click(screen.getByRole('checkbox', { name: 'Seleccionar F-001' }));
    expect(screen.getByRole('checkbox', { name: 'Seleccionar F-001' })).toBeChecked();
    expect(screen.getByText('1 seleccionada')).toBeInTheDocument();
    expect(all.indeterminate).toBe(true);
    expect(all).not.toBeChecked();

    fireEvent.click(all);
    expect(screen.getByText('10 seleccionadas')).toBeInTheDocument();
    expect(all).toBeChecked();
    expect(all.indeterminate).toBe(false);

    fireEvent.click(all);
    expect(screen.queryByText(/seleccionadas?$/)).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('checkbox', { name: 'Seleccionar F-002' }));
    fireEvent.click(screen.getByRole('button', { name: 'Quitar selección' }));
    expect(screen.getByRole('checkbox', { name: 'Seleccionar F-002' })).not.toBeChecked();
  });
});

describe('DataTable · totales', () => {
  it('el pie suma TODAS las filas (no solo la página) y lo dice', () => {
    renderTable({ pageSize: 10 });
    const expected = formatMoney(SALES.reduce((sum, sale) => sum + sale.total, 0));
    const totals = screen.getByTestId('tabla-totales');
    expect(within(totals).getByText(expected)).toBeInTheDocument();
    expect(within(totals).getByText('Totales (las 23 filas)')).toBeInTheDocument();
  });

  it('con una sola página el pie dice solo «Totales»', () => {
    renderTable();
    expect(within(screen.getByTestId('tabla-totales')).getByText('Totales')).toBeInTheDocument();
  });
});

describe('DataTable · estados', () => {
  it('primera carga: esqueleto y aviso para lectores de pantalla, sin filas', () => {
    renderTable({ rows: undefined, loading: true });
    expect(screen.getByRole('status')).toHaveTextContent('Cargando ventas…');
    expect(screen.getByTestId('tabla')).toHaveAttribute('aria-busy', 'true');
    expect(shownNumbers()).toEqual([]);
  });

  it('vacía: el estado vacío con su acción', () => {
    const clear = vi.fn();
    renderTable({ rows: [], empty: { title: 'No hay ventas', description: 'Pruebe con otras fechas.', action: <button onClick={clear}>Limpiar filtros</button> } });
    expect(screen.getByText('No hay ventas')).toBeInTheDocument();
    expect(screen.getByText('Pruebe con otras fechas.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    expect(clear).toHaveBeenCalled();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('error sin filas: el mensaje y «Reintentar»', () => {
    const onRetry = vi.fn();
    renderTable({ rows: undefined, error: new WebApiError({ kind: 'network', message: 'x' }), onRetry });
    const alert = screen.getByTestId('estado-error');
    expect(alert).toHaveTextContent('No se pudo conectar con el servidor');
    fireEvent.click(within(alert).getByRole('button', { name: 'Reintentar' }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it('sin permiso: dice cuál falta y no ofrece reintentar', () => {
    renderTable({ rows: undefined, error: new WebApiError({ kind: 'access_denied', status: 403, message: 'Su rol no tiene el permiso sales.view.' }), onRetry: vi.fn() });
    const alert = screen.getByTestId('estado-error');
    expect(alert).toHaveTextContent('No tiene permiso para ver esto');
    expect(alert).toHaveTextContent('«Consultar el historial de ventas y facturas»');
    expect(within(alert).queryByRole('button', { name: 'Reintentar' })).not.toBeInTheDocument();
  });

  it('error al actualizar con filas a la vista: las filas siguen y aparece el aviso con «Reintentar»', () => {
    const onRetry = vi.fn();
    renderTable({ error: new WebApiError({ kind: 'server', status: 500, message: 'detalle' }), onRetry });
    expect(shownNumbers()).toHaveLength(23);
    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('No se pudo actualizar la lista');
    fireEvent.click(within(alert).getByRole('button', { name: 'Reintentar' }));
    expect(onRetry).toHaveBeenCalled();
  });

  it('recarga con filas: sin esqueleto, con el aviso de actualización', () => {
    renderTable({ refreshing: true });
    expect(shownNumbers()).toHaveLength(23);
    expect(screen.getByRole('status')).toHaveTextContent('Actualizando ventas…');
  });
});

describe('DataTable · detalle, acciones y apertura', () => {
  it('la fila desplegable muestra y oculta su contenido', () => {
    renderTable({ paginate: false, renderExpanded: (sale) => <p>Líneas de {sale.number}</p> });
    const toggle = screen.getByRole('button', { name: 'Detalle de F-001' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    const detail = screen.getByText('Líneas de F-001');
    expect(toggle.getAttribute('aria-controls')).toBe(detail.closest('tr')?.id);
    fireEvent.click(toggle);
    expect(screen.queryByText('Líneas de F-001')).not.toBeInTheDocument();
  });

  it('el menú de acciones de la fila: se abre, se recorre con el teclado y ejecuta la acción', async () => {
    const annul = vi.fn();
    const reprint = vi.fn();
    renderTable({
      paginate: false,
      rowActions: (sale) => [
        { label: 'Anular', tone: 'danger', onSelect: () => annul(sale.number), disabled: sale.number === 'F-002', disabledReason: 'Ya está anulada.' },
        { label: 'Reimprimir', onSelect: () => reprint(sale.number) },
      ],
    });
    const button = screen.getByRole('button', { name: 'Acciones de F-001' });
    fireEvent.click(button);
    const menu = await screen.findByRole('menu');
    const items = within(menu).getAllByRole('menuitem');
    // Las peligrosas van al final, separadas.
    expect(items.map((item) => item.textContent)).toEqual(['Reimprimir', 'Anular']);
    expect(within(menu).getByRole('separator')).toBeInTheDocument();
    await waitFor(() => expect(items[0]).toHaveFocus());
    fireEvent.keyDown(items[0], { key: 'ArrowDown' });
    expect(items[1]).toHaveFocus();
    fireEvent.click(items[1]);
    expect(annul).toHaveBeenCalledWith('F-001');
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(button).toHaveFocus();

    // Escape cierra y devuelve el foco; una acción deshabilitada no se ejecuta y dice por qué.
    fireEvent.click(screen.getByRole('button', { name: 'Acciones de F-002' }));
    const second = await screen.findByRole('menu');
    const disabled = within(second).getByRole('menuitem', { name: /Anular/ });
    expect(disabled).toHaveAttribute('aria-disabled', 'true');
    expect(disabled).toHaveAccessibleDescription('Ya está anulada.');
    fireEvent.click(disabled);
    expect(annul).toHaveBeenCalledTimes(1);
    fireEvent.keyDown(within(second).getByRole('menuitem', { name: 'Reimprimir' }), { key: 'Escape' });
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Acciones de F-002' })).toHaveFocus();
  });

  it('abre el detalle con el botón de la columna principal o con un clic en la fila (no en sus controles)', () => {
    const onRowOpen = vi.fn();
    const onSelectionChange = vi.fn();
    renderTable({ paginate: false, onRowOpen, selection: new Set(), onSelectionChange });
    fireEvent.click(screen.getByRole('button', { name: 'F-003' }));
    expect(onRowOpen).toHaveBeenLastCalledWith(SALES[2]);
    fireEvent.click(screen.getAllByText('Ana Rojas')[0]);
    expect(onRowOpen).toHaveBeenLastCalledWith(SALES[1]);
    const calls = onRowOpen.mock.calls.length;
    fireEvent.click(screen.getByRole('checkbox', { name: 'Seleccionar F-005' }));
    expect(onSelectionChange).toHaveBeenCalled();
    expect(onRowOpen).toHaveBeenCalledTimes(calls);
  });
});

describe('DataTable · tarjetas por debajo de 640 px', () => {
  function stubNarrowScreen() {
    window.matchMedia = vi.fn().mockImplementation((query: string) => ({
      matches: query.includes('max-width: 639.98px'),
      media: query,
      onchange: null,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })) as unknown as typeof window.matchMedia;
  }

  it('muestra una tarjeta por fila con «etiqueta: valor», totales y un paginador compacto', () => {
    stubNarrowScreen();
    renderTable({ pageSize: 10 });
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    const list = screen.getByRole('list', { name: 'Ventas' });
    const cards = within(list).getAllByRole('listitem');
    expect(cards).toHaveLength(10);
    expect(within(cards[0]).getByText('F-001')).toBeInTheDocument();
    expect(within(cards[0]).getByText('Cliente')).toBeInTheDocument();
    expect(within(cards[0]).getByText('Carlos Quispe')).toBeInTheDocument();
    expect(within(screen.getByTestId('tabla-totales')).getByText(formatMoney(SALES.reduce((sum, sale) => sum + sale.total, 0)))).toBeInTheDocument();
    expect(screen.getByText('Página 1 de 3')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Siguiente/ }));
    expect(screen.getByText('Página 2 de 3')).toBeInTheDocument();
  });

  it('el orden se elige con una lista desplegable', () => {
    stubNarrowScreen();
    const onSortChange = vi.fn<(sort: SortState | null) => void>();
    renderTable({ sort: null, onSortChange });
    fireEvent.change(screen.getByLabelText('Ordenar por'), { target: { value: 'total' } });
    expect(onSortChange).toHaveBeenCalledWith({ column: 'total', direction: 'asc' });
  });
});

describe('DataTable · cambios de datos', () => {
  it('al llegar menos filas, la página se acota sin romperse', () => {
    const { rerender } = render(<DataTable caption="Ventas" columns={COLUMNS} rows={SALES} rowKey={(sale) => sale.number} pageSize={10} page={3} onPageChange={vi.fn()} />);
    expect(shownNumbers()).toHaveLength(3);
    act(() => {
      rerender(<DataTable caption="Ventas" columns={COLUMNS} rows={SALES.slice(0, 5)} rowKey={(sale) => sale.number} pageSize={10} page={3} onPageChange={vi.fn()} />);
    });
    expect(shownNumbers()).toHaveLength(5);
    expect(screen.getByText('Mostrando 1–5 de 5')).toBeInTheDocument();
  });
});
