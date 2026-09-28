// «Reservar armado»: validación del formulario (nombre, teléfono boliviano, correo), envío a la tienda con la confirmación
// (número, vencimiento, líneas y total), 409 con las piezas afectadas marcadas y «Ajustar a lo disponible».

import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { useEffect, useRef } from 'react';
import { describe, expect, it, vi } from 'vitest';
import type { Reservation, StockShortage } from '@/1-domain/storefront/types';
import { useBuilder } from '@/4-presentation/hooks/useBuilder';
import { useServices } from '@/4-presentation/hooks/useServices';
import { MOCK_CATALOG } from '@/3-infrastructure/data/mockCatalog';
import { mockSources, renderWithApp } from '@/test-utils';
import { ReserveDialog } from './ReserveDialog';

interface HarnessProps {
  /** SKU y cantidad de las piezas que entran al armado antes de abrir el diálogo. */
  pieces: [string, number][];
  onReserved: (reservation: Reservation) => void;
  onAdjust?: (shortages: readonly StockShortage[]) => void;
}

function Harness({ pieces, onReserved, onAdjust = () => {} }: HarnessProps) {
  const builder = useBuilder();
  const { catalog } = useServices();
  const loaded = useRef(false);
  useEffect(() => {
    if (loaded.current) return;
    loaded.current = true;
    for (const [sku, quantity] of pieces) builder.add(catalog.getProductBySku(sku)!, undefined, quantity, { silent: true });
  }, [builder, catalog, pieces]);
  const adjust = (shortages: readonly StockShortage[]) => {
    for (const shortage of shortages) {
      if (shortage.available <= 0) builder.remove(shortage.sku);
      else builder.setQuantity(shortage.sku, shortage.available);
    }
    onAdjust(shortages);
  };
  return (
    <>
      <p data-testid="piezas">{builder.count}</p>
      <ReserveDialog open onClose={() => {}} summary={builder.summary} onReserved={onReserved} onAdjust={adjust} onPrint={() => {}} />
    </>
  );
}

function fill(name: string, phone: string, email = '') {
  fireEvent.change(screen.getByLabelText(/Nombre y apellido/), { target: { value: name } });
  fireEvent.change(screen.getByLabelText(/Teléfono o WhatsApp/), { target: { value: phone } });
  if (email) fireEvent.change(screen.getByLabelText(/Correo/), { target: { value: email } });
}

const CPU = 'CPU-AMD-7600';
const SSD = 'SSD-KNG-NV3-1TB';

describe('ReserveDialog', () => {
  it('muestra las piezas, el aviso de vigencia y no envía nada con el formulario inválido', async () => {
    const onReserved = vi.fn();
    await renderWithApp(<Harness pieces={[[CPU, 1]]} onReserved={onReserved} />);
    const dialog = await screen.findByRole('dialog', { name: 'Reservar armado' });
    expect(dialog).toHaveTextContent(/48 horas/);
    expect(dialog).toHaveTextContent(/se confirma y paga en la tienda/i);
    expect(within(dialog).getByRole('table', { name: 'Piezas del armado' })).toHaveTextContent('Procesador AMD Ryzen 5 7600');

    fill('', '123', 'sin-arroba');
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar reserva' }));
    expect(await screen.findByText(/Indicá tu nombre/)).toBeInTheDocument();
    expect(screen.getByText('El teléfono debe tener 7 u 8 dígitos (Bolivia), con o sin +591.')).toBeInTheDocument();
    expect(screen.getByText(/Revisá el correo/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Nombre y apellido/)).toHaveAttribute('aria-invalid', 'true');
    expect(onReserved).not.toHaveBeenCalled();
  });

  it('reserva el armado en la tienda y muestra el número, el vencimiento, las líneas y el total', async () => {
    const onReserved = vi.fn();
    const sources = mockSources();
    await renderWithApp(<Harness pieces={[[CPU, 1], [SSD, 2]]} onReserved={onReserved} />, { sources });
    await screen.findByRole('dialog', { name: 'Reservar armado' });
    fill('Valentina Aguirre', '7123-4567', 'valentina@correo.example');
    fireEvent.change(screen.getByLabelText(/Notas para la tienda/), { target: { value: 'Paso el sábado' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar reserva' }));

    const done = await screen.findByRole('dialog', { name: 'Tu armado quedó reservado' });
    expect(within(done).getByTestId('numero-reserva')).toHaveTextContent('ARM-WEB-000001');
    expect(done).toHaveTextContent('Reservada');
    expect(done).toHaveTextContent(/Vence:/);
    expect(done).toHaveTextContent('Casa matriz La Paz');
    expect(done).toHaveTextContent('Paso el sábado');
    expect(within(done).getByRole('link', { name: 'Consultar mi reserva' })).toHaveAttribute('href', '/reserva/ARM-WEB-000001');
    expect(within(done).getByRole('link', { name: 'Avisar por WhatsApp' })).toHaveAttribute('href', expect.stringContaining('ARM-WEB-000001'));

    const cpu = MOCK_CATALOG.products.find((product) => product.sku === CPU)!;
    const ssd = MOCK_CATALOG.products.find((product) => product.sku === SSD)!;
    expect(onReserved).toHaveBeenCalledTimes(1);
    const reservation: Reservation = onReserved.mock.calls[0][0];
    expect(reservation.lines.map((line) => [line.sku, line.quantity])).toEqual([[CPU, 1], [SSD, 2]]);
    expect(reservation.total).toBe(cpu.price + ssd.price * 2);
    expect(reservation.contactName).toBe('Valentina Aguirre');
    // La pasarela descontó lo disponible: la ficha fresca lo refleja.
    expect((await sources.source.product('cpu-amd-7600'))?.stock).toBe(cpu.stock - 1);
  });

  it('con stock insuficiente (409) marca las piezas afectadas, dice cuánto hay y permite ajustar a lo disponible', async () => {
    const onReserved = vi.fn();
    const onAdjust = vi.fn();
    const cpu = MOCK_CATALOG.products.find((product) => product.sku === CPU)!;
    // El CPU con 3 disponibles: el armado pide las 3 y, antes de enviar, otro cliente reserva 2 (la pasarela ve 1).
    const data = { ...MOCK_CATALOG, products: MOCK_CATALOG.products.map((product) => (product.sku === CPU ? { ...product, stock: 3 } : product)) };
    const sources = mockSources(data);
    await renderWithApp(<Harness pieces={[[CPU, 3], [SSD, 1]]} onReserved={onReserved} onAdjust={onAdjust} />, { sources });
    const dialog = await screen.findByRole('dialog', { name: 'Reservar armado' });
    expect(screen.getByTestId('piezas')).toHaveTextContent('4');
    await sources.gateway.create({ lines: [{ sku: CPU, quantity: 2, slot: 'cpu' }], contact: { name: 'Otro cliente', phone: '70000000' }, idempotencyKey: 'otro' });

    fill('Ana Pérez', '71234567');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Confirmar reserva' }));

    const alert = await within(dialog).findByRole('alert');
    expect(alert).toHaveTextContent('No alcanzó el stock para reservar todo el armado');
    expect(alert).toHaveTextContent(/No se reservó nada/);
    const row = within(dialog).getByRole('table', { name: 'Piezas del armado' }).querySelector('tr[data-shortage="true"]');
    expect(row).not.toBeNull();
    expect(row).toHaveTextContent(cpu.shortName);
    expect(row).toHaveTextContent(/Pediste 3; hay 1 unidad disponible/);
    expect(within(dialog).getByRole('table', { name: 'Piezas del armado' }).querySelectorAll('tr[data-shortage="true"]')).toHaveLength(1);
    expect(onReserved).not.toHaveBeenCalled();
    // El SSD no se reservó (todo o nada).
    expect((await sources.source.product(SSD.toLowerCase()))?.reserved).toBe(0);

    fireEvent.click(within(dialog).getByRole('button', { name: 'Ajustar a lo disponible' }));
    expect(onAdjust).toHaveBeenCalledWith([expect.objectContaining({ sku: CPU, requested: 3, available: 1 })]);
    await waitFor(() => expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument());
    await waitFor(() => expect(screen.getByTestId('piezas')).toHaveTextContent('2'));

    // Con el armado ajustado, la reserva sale.
    fireEvent.click(within(dialog).getByRole('button', { name: 'Confirmar reserva' }));
    const done = await screen.findByRole('dialog', { name: 'Tu armado quedó reservado' });
    expect(within(done).getByTestId('numero-reserva')).toHaveTextContent('ARM-WEB-000002');
    expect(onReserved).toHaveBeenCalledTimes(1);
  });
});
