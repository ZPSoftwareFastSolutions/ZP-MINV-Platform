// useTableState: filtros, orden, página y filas por página en la DIRECCIÓN de la página (nada en el navegador). Lee y
// escribe los parámetros, omite los valores por defecto, vuelve a la página 1 al filtrar, cuenta un rango de fechas
// como un filtro y admite un prefijo para dos listas en la misma pantalla.

import { fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderPanel, type RenderPanelResult } from '@/test-utils';
import { useTableState } from './useTableState';

function TableProbe({ prefix }: { prefix?: string }) {
  const table = useTableState({ filters: { q: '', estado: '', desde: '', hasta: '' }, sort: { column: 'fecha', direction: 'desc' }, prefix });
  const actions: [string, () => void][] = [
    ['Filtrar pagadas', () => table.setFilter('estado', 'pagada')],
    ['Buscar monitor', () => table.setFilter('q', 'monitor')],
    ['Quitar estado', () => table.setFilter('estado', '')],
    ['Este mes', () => table.setDateRange({ from: '2026-09-01', to: '2026-09-28' })],
    ['Ordenar por total', () => table.setSort({ column: 'total', direction: 'asc' })],
    ['Sin orden', () => table.setSort(null)],
    ['Orden por defecto', () => table.setSort({ column: 'fecha', direction: 'desc' })],
    ['Página 3', () => table.setPage(3)],
    ['50 filas', () => table.setPageSize(50)],
    ['Limpiar filtros', () => table.clearFilters()],
  ];
  return (
    <div>
      <p data-testid="estado">
        {JSON.stringify({ filters: table.filters, sort: table.sort, page: table.page, pageSize: table.pageSize, active: table.activeFilterCount, range: table.dateRange() })}
      </p>
      {actions.map(([label, action]) => (
        <button key={label} type="button" onClick={action}>
          {label}
        </button>
      ))}
    </div>
  );
}

interface ProbeState {
  filters: Record<string, string>;
  sort: { column: string; direction: string } | null;
  page: number;
  pageSize: number;
  active: number;
  range: { from: string | null; to: string | null };
}

function state(): ProbeState {
  return JSON.parse(screen.getByTestId('estado').textContent ?? '{}') as ProbeState;
}

/** Pulsa una acción y espera a que la dirección sea la esperada. */
async function act(view: RenderPanelResult, name: string, location: string) {
  fireEvent.click(screen.getByRole('button', { name }));
  await waitFor(() => expect(view.location()).toBe(location));
}

async function expectState(expected: Partial<ProbeState>) {
  await waitFor(() => expect(state()).toMatchObject(expected));
}

describe('useTableState', () => {
  it('lee filtros, orden, página y filas por página de la dirección', async () => {
    await renderPanel(<TableProbe />, { route: '/panel/ventas?estado=anulada&q=ssd&orden=total&sentido=asc&pagina=2&filas=50&otro=1' });
    expect(state()).toEqual({
      filters: { q: 'ssd', estado: 'anulada', desde: '', hasta: '' },
      sort: { column: 'total', direction: 'asc' },
      page: 2,
      pageSize: 50,
      active: 2,
      range: { from: null, to: null },
    });
  });

  it('valores raros en la dirección vuelven a los de por defecto', async () => {
    await renderPanel(<TableProbe />, { route: '/panel/ventas?pagina=abc&filas=7&orden=total&sentido=raro' });
    expect(state()).toMatchObject({ page: 1, pageSize: 25, sort: { column: 'total', direction: 'asc' } });
  });

  it('un filtro se escribe en la dirección, vuelve a la página 1 y conserva lo demás; el valor por defecto se borra', async () => {
    const view = await renderPanel(<TableProbe />, { route: '/panel/ventas?pagina=4&otro=1' });
    await act(view, 'Filtrar pagadas', '/panel/ventas?otro=1&estado=pagada');
    await expectState({ page: 1, active: 1, filters: { q: '', estado: 'pagada', desde: '', hasta: '' } });
    await act(view, 'Buscar monitor', '/panel/ventas?otro=1&estado=pagada&q=monitor');
    await expectState({ active: 2 });
    await act(view, 'Quitar estado', '/panel/ventas?otro=1&q=monitor');
    await expectState({ active: 1 });
  });

  it('el rango de fechas son dos parámetros que cuentan como UN filtro; «Limpiar filtros» conserva el orden y las filas', async () => {
    const view = await renderPanel(<TableProbe />, { route: '/panel/ventas?orden=total&sentido=asc&filas=50' });
    await act(view, 'Este mes', '/panel/ventas?orden=total&sentido=asc&filas=50&desde=2026-09-01&hasta=2026-09-28');
    await expectState({ active: 1, range: { from: '2026-09-01', to: '2026-09-28' } });
    await act(view, 'Buscar monitor', '/panel/ventas?orden=total&sentido=asc&filas=50&desde=2026-09-01&hasta=2026-09-28&q=monitor');
    await expectState({ active: 2 });
    await act(view, 'Limpiar filtros', '/panel/ventas?orden=total&sentido=asc&filas=50');
    await expectState({ active: 0, range: { from: null, to: null }, sort: { column: 'total', direction: 'asc' }, pageSize: 50 });
  });

  it('orden: distinto del de por defecto se escribe; «sin orden» queda explícito; el de por defecto se borra', async () => {
    const view = await renderPanel(<TableProbe />, { route: '/panel/ventas?pagina=2' });
    expect(state().sort).toEqual({ column: 'fecha', direction: 'desc' });
    await act(view, 'Ordenar por total', '/panel/ventas?orden=total&sentido=asc');
    await expectState({ page: 1, sort: { column: 'total', direction: 'asc' } });
    await act(view, 'Sin orden', '/panel/ventas?orden=');
    await waitFor(() => expect(state().sort).toBeNull());
    await act(view, 'Orden por defecto', '/panel/ventas');
    await expectState({ sort: { column: 'fecha', direction: 'desc' } });
  });

  it('página y filas por página', async () => {
    const view = await renderPanel(<TableProbe />, { route: '/panel/ventas' });
    await act(view, 'Página 3', '/panel/ventas?pagina=3');
    await expectState({ page: 3 });
    await act(view, '50 filas', '/panel/ventas?filas=50');
    await expectState({ page: 1, pageSize: 50 });
  });

  it('con prefijo, dos listas no se pisan', async () => {
    const view = await renderPanel(<TableProbe prefix="v_" />, { route: '/panel/ventas?estado=pagada' });
    expect(state().filters.estado).toBe('');
    await act(view, 'Filtrar pagadas', '/panel/ventas?estado=pagada&v_estado=pagada');
    await act(view, 'Página 3', '/panel/ventas?estado=pagada&v_estado=pagada&v_pagina=3');
    await expectState({ page: 3, filters: { q: '', estado: 'pagada', desde: '', hasta: '' } });
  });
});
