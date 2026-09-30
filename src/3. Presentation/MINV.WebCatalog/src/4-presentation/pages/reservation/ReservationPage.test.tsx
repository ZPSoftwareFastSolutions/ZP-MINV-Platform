// «Consultar mi reserva» (V7: con el código O el celular): código desde la ruta, validación «al menos uno», 404 cuando el
// celular no coincide, consulta con el código solo (contacto enmascarado y el celular pedido al liberar), consulta con el
// celular solo (lista para abrir una) y la vista completa con los dos; «Liberar mi reserva» con confirmación.

import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { Sources } from '@/4-presentation/app/container';
import { mockSources, renderWithApp } from '@/test-utils';
import { ReservationPage } from './ReservationPage';

const CPU = 'CPU-AMD-7600';
/** Otros procesadores con stock en el mock (el de CPU tiene una sola unidad). */
const OTHER = ['CPU-AMD-7800X3D', 'CPU-INT-14400F'];
const CODE = 'Código de reserva';
const PHONE = 'Número de celular';

async function withReservation(sources: Sources = mockSources(), phone = '+591 71234567', sku = CPU): Promise<{ sources: Sources; number: string }> {
  const reservation = await sources.gateway.create({
    lines: [{ sku, quantity: 1, slot: 'cpu' }],
    contact: { name: 'Valentina Aguirre', phone, email: 'v@correo.example' },
    notes: 'Paso el sábado',
    idempotencyKey: `prueba-${Math.random()}`,
  });
  return { sources, number: reservation.number };
}

function search() {
  fireEvent.click(screen.getByRole('button', { name: 'Buscar reserva' }));
}

describe('ReservationPage', () => {
  it('toma el código de la ruta, pide al menos uno de los dos y valida la forma del que venga', async () => {
    const { sources, number } = await withReservation();
    await renderWithApp(<ReservationPage />, { sources, route: `/reserva/${number.toLowerCase()}`, path: '/reserva/:numero' });
    expect(screen.getByRole('heading', { level: 1, name: 'Consultar mi reserva' })).toBeInTheDocument();
    expect(screen.getByLabelText(CODE)).toHaveValue(number);
    expect(screen.getByText('Complete al menos uno')).toBeInTheDocument();
    expect(screen.getByLabelText(CODE)).toHaveAccessibleDescription(expect.stringContaining('Complete al menos uno'));
    expect(screen.getByLabelText(PHONE)).toHaveAccessibleDescription(expect.stringContaining('Complete al menos uno'));

    fireEvent.change(screen.getByLabelText(CODE), { target: { value: '' } });
    search();
    expect(await screen.findByText('Completá el código de reserva o el número de celular (al menos uno).')).toBeInTheDocument();
    expect(screen.queryByTestId('resumen-reserva')).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(PHONE), { target: { value: '12' } });
    search();
    expect(await screen.findByText('El celular debe tener 7 u 8 dígitos (Bolivia), con o sin +591.')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(PHONE), { target: { value: '' } });
    fireEvent.change(screen.getByLabelText(CODE), { target: { value: 'PEDIDO-1' } });
    search();
    expect(await screen.findByText(/El código tiene la forma RES-WEB-000001/)).toBeInTheDocument();
  });

  it('con el código y otro celular responde «no encontramos esa reserva» (no revela si existe)', async () => {
    const { sources, number } = await withReservation();
    await renderWithApp(<ReservationPage />, { sources, route: '/reserva', path: '/reserva' });
    fireEvent.change(screen.getByLabelText(CODE), { target: { value: number } });
    fireEvent.change(screen.getByLabelText(PHONE), { target: { value: '70000000' } });
    search();
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('No encontramos esa reserva');
    expect(screen.queryByTestId('resumen-reserva')).not.toBeInTheDocument();
  });

  it('con el código y el celular muestra la reserva completa y la libera con confirmación', async () => {
    const { sources, number } = await withReservation();
    await renderWithApp(<ReservationPage />, { sources, route: `/reserva/${number}`, path: '/reserva/:numero' });
    fireEvent.change(screen.getByLabelText(PHONE), { target: { value: '591 7123 4567' } });
    search();

    const summary = await screen.findByTestId('resumen-reserva');
    expect(within(summary).getByTestId('numero-reserva')).toHaveTextContent(number);
    expect(summary).toHaveTextContent('Reservada');
    expect(summary).toHaveTextContent('Valentina Aguirre');
    expect(summary).toHaveTextContent('Paso el sábado');
    expect(summary).toHaveTextContent('Casa matriz La Paz');
    expect(within(summary).getByRole('table')).toHaveTextContent(CPU);
    expect(screen.getByTestId('contacto-enmascarado')).toHaveTextContent('•••••567');
    expect(screen.getByTestId('contacto-enmascarado')).toHaveTextContent('v•••@correo.example');
    expect(screen.queryByText(/ocultamos parte del nombre y las notas/)).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Coordinar retiro por WhatsApp' })).toHaveAttribute('href', expect.stringContaining(number));

    fireEvent.click(screen.getByRole('button', { name: 'Liberar mi reserva' }));
    expect(await screen.findByText(/¿Liberar la reserva/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Número de celular con el que reservaste')).not.toBeInTheDocument();   // ya se comprobó
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

  it('con el código solo muestra la reserva con el contacto enmascarado y pide el celular para liberarla', async () => {
    const { sources, number } = await withReservation();
    await renderWithApp(<ReservationPage />, { sources, route: `/reserva/${number}`, path: '/reserva/:numero' });
    search();

    const summary = await screen.findByTestId('resumen-reserva');
    expect(within(summary).getByTestId('numero-reserva')).toHaveTextContent(number);
    expect(summary).toHaveTextContent('Reservada');
    expect(summary).toHaveTextContent('V••• A•••');
    expect(summary).not.toHaveTextContent('Valentina');
    expect(summary).not.toHaveTextContent('Paso el sábado');
    const contact = screen.getByTestId('contacto-enmascarado');
    expect(contact).toHaveTextContent('•••••567');
    expect(contact).not.toHaveTextContent('71234567');
    expect(contact).toHaveTextContent('Por tu privacidad ocultamos parte del nombre y las notas');

    // Liberar pide el celular: vacío, mal formado y el que no coincide no liberan
    fireEvent.click(screen.getByRole('button', { name: 'Liberar mi reserva' }));
    const phoneInput = await screen.findByLabelText('Número de celular con el que reservaste');
    await waitFor(() => expect(phoneInput).toHaveFocus());
    fireEvent.click(screen.getByRole('button', { name: 'Sí, liberar' }));
    expect(await screen.findByText('Indicá el número de celular con el que hiciste la reserva.')).toBeInTheDocument();
    fireEvent.change(phoneInput, { target: { value: '12' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sí, liberar' }));
    expect(await screen.findByText('El celular debe tener 7 u 8 dígitos (Bolivia), con o sin +591.')).toBeInTheDocument();
    fireEvent.change(phoneInput, { target: { value: '70000000' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sí, liberar' }));
    expect(await screen.findByText('Ese celular no coincide con el de la reserva. Revisalo e intentá de nuevo.')).toBeInTheDocument();
    expect(screen.getByTestId('resumen-reserva')).toHaveTextContent('Reservada');

    fireEvent.change(phoneInput, { target: { value: '+591 71234567' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sí, liberar' }));
    await waitFor(() => expect(screen.getByTestId('resumen-reserva')).toHaveTextContent('Cancelada'));
    expect((await sources.source.product(CPU.toLowerCase()))?.reserved).toBe(0);
  });

  it('con el celular solo lista sus reservas (las más nuevas primero), abre una y la libera sin volver a pedirlo', async () => {
    const { sources, number: first } = await withReservation();
    const { number: second } = await withReservation(sources, '71234567', OTHER[0]);
    await withReservation(sources, '76543210', OTHER[1]);   // de otro celular: no sale
    await renderWithApp(<ReservationPage />, { sources, route: '/reserva', path: '/reserva' });
    fireEvent.change(screen.getByLabelText(PHONE), { target: { value: '7123-4567' } });
    search();

    const list = await screen.findByTestId('lista-reservas');
    expect(list).toHaveTextContent('Reservas con el celular +591 71234567');
    const items = within(list).getAllByRole('listitem');
    expect(items).toHaveLength(2);
    expect(items[0]).toHaveTextContent(second);
    expect(items[1]).toHaveTextContent(first);

    fireEvent.click(within(list).getByRole('button', { name: `Ver la reserva ${first}` }));
    const summary = await screen.findByTestId('resumen-reserva');
    expect(within(summary).getByTestId('numero-reserva')).toHaveTextContent(first);
    expect(summary).toHaveTextContent('V••• A•••');
    expect(summary).not.toHaveTextContent('Paso el sábado');

    fireEvent.click(screen.getByRole('button', { name: 'Liberar mi reserva' }));
    expect(screen.queryByLabelText('Número de celular con el que reservaste')).not.toBeInTheDocument();
    fireEvent.click(await screen.findByRole('button', { name: 'Sí, liberar' }));
    await waitFor(() => expect(screen.getByTestId('resumen-reserva')).toHaveTextContent('Cancelada'));

    fireEvent.click(screen.getByRole('button', { name: 'Volver a la lista' }));
    expect(await screen.findByTestId('lista-reservas')).toBeInTheDocument();
  });

  it('con un celular sin reservas lo dice sin revelar nada más', async () => {
    const { sources } = await withReservation();
    await renderWithApp(<ReservationPage />, { sources, route: '/reserva', path: '/reserva' });
    fireEvent.change(screen.getByLabelText(PHONE), { target: { value: '79999999' } });
    search();
    const list = await screen.findByTestId('lista-reservas');
    expect(within(list).getByRole('status')).toHaveTextContent('No encontramos reservas con ese celular');
    expect(within(list).queryAllByRole('listitem')).toHaveLength(0);
  });
});
