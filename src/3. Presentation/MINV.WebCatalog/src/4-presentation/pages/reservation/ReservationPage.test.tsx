// «Consultar mi reserva»: número desde la ruta, validación, 404 cuando el teléfono no coincide, estado con las piezas y
// «Liberar mi reserva» con confirmación (las piezas vuelven a estar disponibles).

import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { Sources } from '@/4-presentation/app/container';
import { mockSources, renderWithApp } from '@/test-utils';
import { ReservationPage } from './ReservationPage';

const CPU = 'CPU-AMD-7600';

async function withReservation(): Promise<{ sources: Sources; number: string }> {
  const sources = mockSources();
  const reservation = await sources.gateway.create({
    lines: [{ sku: CPU, quantity: 1, slot: 'cpu' }],
    contact: { name: 'Valentina Aguirre', phone: '+591 71234567', email: 'v@correo.example' },
    notes: 'Paso el sábado',
    idempotencyKey: 'prueba-1',
  });
  return { sources, number: reservation.number };
}

describe('ReservationPage', () => {
  it('toma el número de la ruta y valida el teléfono antes de consultar', async () => {
    const { sources, number } = await withReservation();
    await renderWithApp(<ReservationPage />, { sources, route: `/reserva/${number.toLowerCase()}`, path: '/reserva/:numero' });
    expect(screen.getByRole('heading', { level: 1, name: 'Consultar mi reserva' })).toBeInTheDocument();
    expect(screen.getByLabelText('Número de reserva')).toHaveValue(number);
    fireEvent.click(screen.getByRole('button', { name: 'Ver estado' }));
    expect(await screen.findByText('Indicá el teléfono con el que hiciste la reserva.')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText(/Teléfono con el que reservaste/), { target: { value: '12' } });
    fireEvent.click(screen.getByRole('button', { name: 'Ver estado' }));
    expect(await screen.findByText('El teléfono debe tener 7 u 8 dígitos (Bolivia), con o sin +591.')).toBeInTheDocument();
  });

  it('con otro teléfono responde «no encontramos esa reserva» (no revela si existe)', async () => {
    const { sources, number } = await withReservation();
    await renderWithApp(<ReservationPage />, { sources, route: '/reserva', path: '/reserva' });
    fireEvent.change(screen.getByLabelText('Número de reserva'), { target: { value: number } });
    fireEvent.change(screen.getByLabelText(/Teléfono con el que reservaste/), { target: { value: '70000000' } });
    fireEvent.click(screen.getByRole('button', { name: 'Ver estado' }));
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('No encontramos esa reserva');
    expect(screen.queryByTestId('resumen-reserva')).not.toBeInTheDocument();
  });

  it('muestra el estado con las piezas y libera la reserva con confirmación', async () => {
    const { sources, number } = await withReservation();
    await renderWithApp(<ReservationPage />, { sources, route: `/reserva/${number}`, path: '/reserva/:numero' });
    fireEvent.change(screen.getByLabelText(/Teléfono con el que reservaste/), { target: { value: '591 7123 4567' } });
    fireEvent.click(screen.getByRole('button', { name: 'Ver estado' }));

    const summary = await screen.findByTestId('resumen-reserva');
    expect(within(summary).getByTestId('numero-reserva')).toHaveTextContent(number);
    expect(summary).toHaveTextContent('Reservada');
    expect(summary).toHaveTextContent('Valentina Aguirre');
    expect(summary).toHaveTextContent('Paso el sábado');
    expect(summary).toHaveTextContent('Casa matriz La Paz');
    expect(within(summary).getByRole('table')).toHaveTextContent(CPU);
    expect(screen.getByRole('link', { name: 'Coordinar retiro por WhatsApp' })).toHaveAttribute('href', expect.stringContaining(number));

    fireEvent.click(screen.getByRole('button', { name: 'Liberar mi reserva' }));
    expect(await screen.findByText(/¿Liberar la reserva/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'No, conservarla' }));
    await waitFor(() => expect(screen.queryByText(/¿Liberar la reserva/)).not.toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: 'Liberar mi reserva' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Sí, liberar' }));
    await waitFor(() => expect(screen.getByTestId('resumen-reserva')).toHaveTextContent('Cancelada'));
    expect(screen.getByTestId('resumen-reserva')).toHaveTextContent('Cancelada por el cliente desde la tienda web');
    expect(screen.queryByRole('button', { name: 'Liberar mi reserva' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Armar otra PC' })).toBeInTheDocument();
    expect((await sources.source.product(CPU.toLowerCase()))?.reserved).toBe(0);
  });
});
