// SidePanel: detalle lateral con estados de carga y error, y un cierre que se llama UNA vez por cierre aunque la pantalla
// pase una función nueva en cada dibujo (antes, navegar con el panel abierto armaba un ciclo sin fin).

import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { useState } from 'react';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import { SidePanel } from './SidePanel';

function Harness() {
  const [state, setState] = useState({ open: true, closes: 0 });
  const navigate = useNavigate();
  return (
    <>
      <button type="button" onClick={() => navigate('/otra')}>
        Ir a otra pantalla
      </button>
      <p data-testid="cierres">{state.closes}</p>
      {/* Función nueva en cada dibujo y estado nuevo en cada cierre: lo más común en una pantalla. */}
      <SidePanel open={state.open} onClose={() => setState((current) => ({ open: false, closes: current.closes + 1 }))} title="Venta F-CM-000123">
        <p>Detalle de la venta</p>
      </SidePanel>
    </>
  );
}

function renderHarness() {
  return render(
    <MemoryRouter initialEntries={['/panel/ventas']}>
      <Routes>
        <Route path="*" element={<Harness />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('SidePanel', () => {
  it('es un diálogo con su título y su contenido', () => {
    renderHarness();
    const panel = screen.getByRole('dialog', { name: 'Venta F-CM-000123' });
    expect(within(panel).getByText('Detalle de la venta')).toBeInTheDocument();
  });

  it('al navegar con el panel abierto se cierra UNA sola vez (sin ciclo)', async () => {
    renderHarness();
    fireEvent.click(screen.getByRole('button', { name: 'Ir a otra pantalla', hidden: true }));
    await waitFor(() => expect(screen.getByTestId('cierres')).toHaveTextContent('1'));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.getByTestId('cierres')).toHaveTextContent('1');
  });

  it('Escape lo cierra una vez', async () => {
    renderHarness();
    fireEvent.keyDown(document.body, { key: 'Escape' });
    await waitFor(() => expect(screen.getByTestId('cierres')).toHaveTextContent('1'));
    fireEvent.keyDown(document.body, { key: 'Escape' });
    expect(screen.getByTestId('cierres')).toHaveTextContent('1');
  });

  it('muestra el esqueleto mientras carga y el error con «Reintentar»', () => {
    const onRetry = vi.fn();
    const { rerender } = render(
      <SidePanel open onClose={vi.fn()} title="Cliente" loading>
        <p>Datos del cliente</p>
      </SidePanel>,
    );
    expect(screen.getByRole('status')).toHaveTextContent('Cargando cliente…');
    expect(screen.queryByText('Datos del cliente')).not.toBeInTheDocument();
    rerender(
      <SidePanel open onClose={vi.fn()} title="Cliente" error={new WebApiError({ kind: 'not_found', status: 404, message: 'El cliente no existe.' })} onRetry={onRetry}>
        <p>Datos del cliente</p>
      </SidePanel>,
    );
    const alert = screen.getByTestId('estado-error');
    expect(alert).toHaveTextContent('El cliente no existe.');
    fireEvent.click(within(alert).getByRole('button', { name: 'Reintentar' }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });
});
