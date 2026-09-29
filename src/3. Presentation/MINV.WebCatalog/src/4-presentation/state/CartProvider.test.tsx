// Proveedor del carrito: entrega el carrito cruzado con el catálogo, guarda por el almacén después de cada cambio, se
// pone al día con otra pestaña y con cada instantánea nueva del catálogo. Almacén en memoria: nada toca el navegador.

import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { Product } from '@/1-domain/catalog/types';
import { useCart } from '@/4-presentation/hooks/useCart';
import { useServices } from '@/4-presentation/hooks/useServices';
import { MOCK_CATALOG } from '@/3-infrastructure/data/mockCatalog';
import { mockCart, mockSources, renderWithApp } from '@/test-utils';
import { formatMoney } from '@/shared/format';

const CONSOLE = 'CON-NIN-SWOLED'; // 2 disponibles · Bs 4.399
const GAME = 'JUE-NS2-DKBANANZA'; // 5 disponibles · Bs 829
const SD_CARD = 'ALMC-SDK-MSDEX-256'; // 20 disponibles · Bs 729

function productOf(sku: string): Product {
  const product = MOCK_CATALOG.products.find((item) => item.sku === sku);
  if (!product) throw new Error(`El mock no tiene el producto ${sku}.`);
  return product;
}

function Harness() {
  const cart = useCart();
  const { catalog, refresh } = useServices();
  const get = (sku: string) => catalog.getProductBySku(sku)!;
  return (
    <div>
      <p data-testid="unidades">{cart.count}</p>
      <p data-testid="total">{formatMoney(cart.total)}</p>
      <p data-testid="guardado">{cart.persistent ? 'sí' : 'no'}</p>
      <p data-testid="reservable">{cart.review.reservable ? 'sí' : 'no'}</p>
      <p data-testid="tiene-consola">{cart.has(CONSOLE) ? `sí (${cart.quantityOf(CONSOLE)})` : 'no'}</p>
      <ul>
        {cart.lines.map((line) => (
          <li key={line.sku} data-testid="linea">
            {line.sku} × {line.quantity} · {line.status} · máx {line.max} · {formatMoney(line.subtotal)}
          </li>
        ))}
      </ul>
      <button type="button" onClick={() => cart.add(get(CONSOLE))}>
        agregar consola
      </button>
      <button type="button" onClick={() => cart.add(get(GAME), 3)}>
        agregar 3 juegos
      </button>
      <button type="button" onClick={() => cart.setQuantity(GAME, 99)}>
        pedir 99 juegos
      </button>
      <button type="button" onClick={() => cart.setQuantity(SD_CARD, 99)}>
        pedir 99 tarjetas
      </button>
      <button type="button" onClick={() => cart.remove(GAME)}>
        quitar juego
      </button>
      <button type="button" onClick={() => cart.adjust()}>
        ajustar
      </button>
      <button type="button" onClick={() => cart.clear()}>
        vaciar
      </button>
      <button type="button" onClick={() => void refresh()}>
        refrescar catálogo
      </button>
    </div>
  );
}

function lines(): string[] {
  return screen.queryAllByTestId('linea').map((item) => item.textContent ?? '');
}

describe('CartProvider', () => {
  it('empieza vacío y no se puede reservar', async () => {
    await renderWithApp(<Harness />);
    expect(screen.getByTestId('unidades')).toHaveTextContent('0');
    expect(screen.getByTestId('total')).toHaveTextContent('Bs 0,00');
    expect(screen.getByTestId('reservable')).toHaveTextContent('no');
    expect(lines()).toEqual([]);
  });

  it('muestra lo guardado, cruzado con el catálogo (nombre, precio y disponible de hoy)', async () => {
    const { cart } = mockCart([
      { sku: CONSOLE, quantity: 1 },
      { sku: GAME, quantity: 2 },
    ]);
    await renderWithApp(<Harness />, { cart });
    expect(screen.getByTestId('unidades')).toHaveTextContent('3');
    expect(screen.getByTestId('total')).toHaveTextContent(formatMoney(4399 + 829 * 2));
    expect(screen.getByTestId('tiene-consola')).toHaveTextContent('sí (1)');
    expect(screen.getByTestId('reservable')).toHaveTextContent('sí');
    expect(lines()).toEqual([`${CONSOLE} × 1 · ok · máx 2 · Bs 4.399,00`, `${GAME} × 2 · ok · máx 5 · Bs 1.658,00`]);
  });

  it('agrega, cambia la cantidad, quita y vacía; cada cambio queda guardado (solo SKU y cantidad)', async () => {
    const { cart, store } = mockCart();
    await renderWithApp(<Harness />, { cart });

    fireEvent.click(screen.getByRole('button', { name: 'agregar consola' }));
    fireEvent.click(screen.getByRole('button', { name: 'agregar 3 juegos' }));
    expect(screen.getByTestId('unidades')).toHaveTextContent('4');
    expect(store.load()).toEqual([
      { sku: CONSOLE, quantity: 1 },
      { sku: GAME, quantity: 3 },
    ]);

    // Nunca más de lo disponible: quedan 5 juegos.
    fireEvent.click(screen.getByRole('button', { name: 'pedir 99 juegos' }));
    expect(store.load()).toContainEqual({ sku: GAME, quantity: 5 });

    fireEvent.click(screen.getByRole('button', { name: 'quitar juego' }));
    expect(lines()).toEqual([`${CONSOLE} × 1 · ok · máx 2 · Bs 4.399,00`]);
    expect(store.load()).toEqual([{ sku: CONSOLE, quantity: 1 }]);

    fireEvent.click(screen.getByRole('button', { name: 'vaciar' }));
    expect(screen.getByTestId('unidades')).toHaveTextContent('0');
    expect(store.load()).toEqual([]);
  });

  it('no deja pasar de lo disponible ni de 16 por producto', async () => {
    const { cart, store } = mockCart([
      { sku: CONSOLE, quantity: 2 },
      { sku: SD_CARD, quantity: 1 },
    ]);
    await renderWithApp(<Harness />, { cart });
    fireEvent.click(screen.getByRole('button', { name: 'agregar consola' }));
    expect(screen.getByTestId('tiene-consola')).toHaveTextContent('sí (2)');
    fireEvent.click(screen.getByRole('button', { name: 'pedir 99 tarjetas' }));
    expect(store.load()).toEqual([
      { sku: CONSOLE, quantity: 2 },
      { sku: SD_CARD, quantity: 16 },
    ]);
  });

  it('se pone al día cuando otra pestaña cambia el carrito', async () => {
    const { cart, store } = mockCart([{ sku: CONSOLE, quantity: 1 }]);
    await renderWithApp(<Harness />, { cart });
    act(() => store.changeFromElsewhere([{ sku: GAME, quantity: 4 }]));
    expect(lines()).toEqual([`${GAME} × 4 · ok · máx 5 · Bs 3.316,00`]);
    expect(screen.getByTestId('tiene-consola')).toHaveTextContent('no');
    act(() => store.changeFromElsewhere([]));
    expect(screen.getByTestId('unidades')).toHaveTextContent('0');
  });

  it('deja de escuchar al almacén al desmontarse', async () => {
    const { cart, store } = mockCart([{ sku: CONSOLE, quantity: 1 }]);
    const app = await renderWithApp(<Harness />, { cart });
    app.unmount();
    expect(() => store.changeFromElsewhere([{ sku: GAME, quantity: 1 }])).not.toThrow();
    expect(cart.current().items).toEqual([{ sku: CONSOLE, quantity: 1 }]);
  });

  it('avisa cuando el carrito vive solo en memoria', async () => {
    const { cart } = mockCart([], { persistent: false });
    await renderWithApp(<Harness />, { cart });
    expect(screen.getByTestId('guardado')).toHaveTextContent('no');
  });

  it('con cada instantánea nueva del catálogo marca lo que bajó o se agotó, sin corregir el carrito solo', async () => {
    const sources = mockSources();
    const { cart, store } = mockCart([
      { sku: CONSOLE, quantity: 2 },
      { sku: GAME, quantity: 1 },
    ]);
    await renderWithApp(<Harness />, { cart, sources });
    expect(screen.getByTestId('reservable')).toHaveTextContent('sí');

    // Otro cliente reserva una de las dos consolas y los cinco juegos.
    await sources.gateway.create({
      lines: [
        { sku: CONSOLE, quantity: 1, slot: 'peripherals' },
        { sku: GAME, quantity: 5, slot: 'peripherals' },
      ],
      contact: { name: 'Otra persona', phone: '70000001' },
      idempotencyKey: 'prueba-carrito-1',
    });
    fireEvent.click(screen.getByRole('button', { name: 'refrescar catálogo' }));

    await waitFor(() => expect(lines()).toEqual([`${CONSOLE} × 2 · reduced · máx 1 · Bs 8.798,00`, `${GAME} × 1 · sold_out · máx 0 · Bs 829,00`]));
    expect(screen.getByTestId('reservable')).toHaveTextContent('no');
    // Lo agotado no entra al total; el carrito guardado sigue como lo dejó la persona.
    expect(screen.getByTestId('total')).toHaveTextContent('Bs 8.798,00');
    expect(store.load()).toEqual([
      { sku: CONSOLE, quantity: 2 },
      { sku: GAME, quantity: 1 },
    ]);

    fireEvent.click(screen.getByRole('button', { name: 'ajustar' }));
    expect(lines()).toEqual([`${CONSOLE} × 1 · ok · máx 1 · Bs 4.399,00`]);
    expect(screen.getByTestId('reservable')).toHaveTextContent('sí');
    expect(store.load()).toEqual([{ sku: CONSOLE, quantity: 1 }]);
  });

  it('un producto que ya no está publicado queda marcado y no entra al total', async () => {
    const { cart } = mockCart([
      { sku: 'YA-NO-EXISTE', quantity: 2 },
      { sku: GAME, quantity: 1 },
    ]);
    await renderWithApp(<Harness />, { cart });
    expect(lines()).toEqual(['YA-NO-EXISTE × 2 · unavailable · máx 0 · Bs 0,00', `${GAME} × 1 · ok · máx 5 · Bs 829,00`]);
    expect(screen.getByTestId('total')).toHaveTextContent('Bs 829,00');
    expect(screen.getByTestId('reservable')).toHaveTextContent('no');
  });

  it('fuera del proveedor, useCart avisa con un error claro', () => {
    const silence = console.error;
    console.error = () => undefined;
    try {
      expect(() => render(<Harness />)).toThrow(/CartProvider|ServicesProvider/);
    } finally {
      console.error = silence;
    }
  });

  it('los datos de prueba tienen lo que estas pruebas suponen', () => {
    expect(productOf(CONSOLE)).toMatchObject({ stock: 2, price: 4399 });
    expect(productOf(GAME)).toMatchObject({ stock: 5, price: 829 });
    expect(productOf(SD_CARD)).toMatchObject({ stock: 20, price: 729 });
  });
});
