// Bloque de compra de la ficha: «Agregar al carrito» y «Reservar ahora» para TODO producto con disponibilidad, con la
// cantidad acotada a lo disponible; «Reservar ahora» lleva a la reserva de ese solo artículo con su cantidad; el armador
// y la consulta por WhatsApp quedan como opciones secundarias; lo agotado no se puede agregar ni reservar.

import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { useLocation } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import type { Product } from '@/1-domain/catalog/types';
import { useBuildState } from '@/4-presentation/hooks/useBuilder';
import { MOCK_CATALOG } from '@/3-infrastructure/data/mockCatalog';
import { mockCart, mockSources, renderWithApp, type MockCart } from '@/test-utils';
import { STORE } from '@/shared/constants';
import { PurchaseBox } from './PurchaseBox';

function productOf(sku: string): Product {
  const product = MOCK_CATALOG.products.find((item) => item.sku === sku);
  if (!product) throw new Error(`El mock no tiene el producto ${sku}.`);
  return product;
}

const CONSOLE = productOf('CON-NIN-SWOLED'); // consola · 2 disponibles
const GAME = productOf('JUE-NS2-DKBANANZA'); // videojuego · 5 disponibles
const SD_CARD = productOf('ALMC-SDK-MSDEX-256'); // accesorio de consola · 20 disponibles
const LAST_LAPTOP = productOf('LAP-HP-OMEN16-5060'); // portátil · 1 disponible
const SSD = productOf('SSD-KNG-NV3-1TB'); // pieza del armador (ranura múltiple) · 3 disponibles
const CPU = productOf('CPU-AMD-5600'); // pieza del armador (ranura única) · 7 disponibles
const SOLD_OUT = productOf('LAP-LEN-LEGIONPRO7-5080'); // agotada

function Probe() {
  const location = useLocation();
  const { count } = useBuildState();
  return (
    <>
      <p data-testid="ruta">{`${location.pathname}${location.search}`}</p>
      <p data-testid="piezas-armado">{count}</p>
    </>
  );
}

async function renderBox(product: Product, cart: MockCart = mockCart(), sources = mockSources()): Promise<MockCart> {
  await renderWithApp(
    <>
      <PurchaseBox product={product} categoryHref="/catalogo/de-prueba" />
      <Probe />
    </>,
    { cart: cart.cart, sources },
  );
  // La ficha consulta la disponibilidad fresca al abrirse.
  await waitFor(() => expect(screen.getByTestId('disponibilidad-ficha')).toHaveAttribute('data-fresh', 'true'));
  return cart;
}

function stepper(product: Product): HTMLElement {
  return screen.getByRole('group', { name: `Cantidad de ${product.shortName}` });
}

describe('PurchaseBox', () => {
  it.each([
    ['una consola', CONSOLE],
    ['un videojuego', GAME],
    ['un accesorio de consola', SD_CARD],
  ])('%s (antes solo «Consultar por WhatsApp») ahora se agrega al carrito y se reserva', async (_name, product) => {
    await renderBox(product);
    const actions = screen.getByTestId('acciones-ficha');
    expect(within(actions).getByRole('button', { name: 'Agregar al carrito' })).toBeEnabled();
    expect(within(actions).getByRole('link', { name: 'Reservar ahora' })).toHaveAttribute('href', `/reservar?sku=${product.sku}&cantidad=1`);
    // WhatsApp se conserva como opción secundaria.
    expect(within(actions).getByRole('link', { name: 'Consultar por WhatsApp' })).toHaveAttribute('href', STORE.whatsappUrl);
    expect(within(actions).getByRole('link', { name: `Ver más de ${product.categoryName}` })).toHaveAttribute('href', '/catalogo/de-prueba');
    // Y no se lo manda al armador.
    expect(within(actions).queryByRole('button', { name: /armado/i })).not.toBeInTheDocument();
  });

  it('agrega al carrito la cantidad elegida y avisa', async () => {
    const { store } = await renderBox(GAME);
    fireEvent.click(within(stepper(GAME)).getByRole('button', { name: 'Agregar una unidad' }));
    fireEvent.click(within(stepper(GAME)).getByRole('button', { name: 'Agregar una unidad' }));
    fireEvent.click(screen.getByRole('button', { name: 'Agregar al carrito' }));

    expect(store.load()).toEqual([{ sku: GAME.sku, quantity: 3 }]);
    expect(screen.getByText('Agregado al carrito')).toBeInTheDocument();
    expect(screen.getByTestId('en-carrito-ficha')).toHaveTextContent('Tenés 3 unidades en tu carrito.');
    expect(within(screen.getByTestId('en-carrito-ficha')).getByRole('link', { name: 'Ver carrito' })).toHaveAttribute('href', '/carrito');
    expect(screen.getByTestId('piezas-armado')).toHaveTextContent('0');
  });

  it('la cantidad nunca pasa de lo disponible', async () => {
    const { store } = await renderBox(CONSOLE);
    const plus = within(stepper(CONSOLE)).getByRole('button', { name: 'Agregar una unidad' });
    for (let click = 0; click < 5; click += 1) fireEvent.click(plus);
    expect(within(stepper(CONSOLE)).getByRole('spinbutton')).toHaveValue(2);
    fireEvent.click(screen.getByRole('button', { name: 'Agregar al carrito' }));
    expect(store.load()).toEqual([{ sku: CONSOLE.sku, quantity: 2 }]);

    // Ya tiene las dos que hay: otra vez no suma.
    fireEvent.click(screen.getByRole('button', { name: 'Agregar al carrito' }));
    expect(store.load()).toEqual([{ sku: CONSOLE.sku, quantity: 2 }]);
    expect(screen.getByText('Ya tenés todo lo disponible')).toBeInTheDocument();
  });

  it('con mucho stock la cantidad llega hasta 16 por producto', async () => {
    await renderBox(SD_CARD);
    expect(within(stepper(SD_CARD)).getByRole('spinbutton')).toHaveAttribute('max', '16');
    fireEvent.change(within(stepper(SD_CARD)).getByRole('spinbutton'), { target: { value: '40' } });
    expect(within(stepper(SD_CARD)).getByRole('spinbutton')).toHaveValue(16);
    expect(screen.getByRole('link', { name: 'Reservar ahora' })).toHaveAttribute('href', `/reservar?sku=${SD_CARD.sku}&cantidad=16`);
  });

  it('con una sola unidad disponible no pide cantidad', async () => {
    await renderBox(LAST_LAPTOP);
    expect(screen.queryByRole('group', { name: /^Cantidad de/ })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Reservar ahora' })).toHaveAttribute('href', `/reservar?sku=${LAST_LAPTOP.sku}&cantidad=1`);
  });

  it('«Reservar ahora» lleva a la reserva de ESE artículo con su cantidad, sin tocar el carrito ni el armado', async () => {
    const { store } = await renderBox(GAME, mockCart([{ sku: CONSOLE.sku, quantity: 1 }]));
    fireEvent.click(within(stepper(GAME)).getByRole('button', { name: 'Agregar una unidad' }));
    fireEvent.click(screen.getByRole('link', { name: 'Reservar ahora' }));
    expect(screen.getByTestId('ruta')).toHaveTextContent(`/reservar?sku=${GAME.sku}&cantidad=2`);
    expect(store.load()).toEqual([{ sku: CONSOLE.sku, quantity: 1 }]);
    expect(screen.getByTestId('piezas-armado')).toHaveTextContent('0');
  });

  it('una pieza del armador tiene el carrito y la reserva, y conserva «Agregar al armado» y «Ver mi armado»', async () => {
    const { store } = await renderBox(SSD);
    const actions = screen.getByTestId('acciones-ficha');
    expect(within(actions).getByRole('link', { name: 'Reservar ahora' })).toHaveAttribute('href', `/reservar?sku=${SSD.sku}&cantidad=1`);
    expect(within(actions).queryByRole('link', { name: 'Consultar por WhatsApp' })).not.toBeInTheDocument();

    fireEvent.click(within(stepper(SSD)).getByRole('button', { name: 'Agregar una unidad' }));
    fireEvent.click(within(actions).getByRole('button', { name: 'Agregar al armado' }));
    expect(screen.getByTestId('piezas-armado')).toHaveTextContent('2');
    expect(store.load()).toEqual([]);
    expect(within(actions).getByRole('button', { name: 'Ver mi armado (2)' })).toBeInTheDocument();

    fireEvent.click(within(actions).getByRole('button', { name: 'Agregar al carrito' }));
    expect(store.load()).toEqual([{ sku: SSD.sku, quantity: 2 }]);
    expect(screen.getByTestId('piezas-armado')).toHaveTextContent('2');
  });

  it('en una ranura de una sola pieza el armado recibe una unidad aunque se pidan más', async () => {
    await renderBox(CPU);
    fireEvent.click(within(stepper(CPU)).getByRole('button', { name: 'Agregar una unidad' }));
    fireEvent.click(screen.getByRole('button', { name: 'Agregar al armado' }));
    expect(screen.getByTestId('piezas-armado')).toHaveTextContent('1');
    expect(screen.getByRole('button', { name: 'En tu armado' })).toBeInTheDocument();
  });

  it('lo agotado no se puede agregar ni reservar, y ofrece consultar por WhatsApp', async () => {
    await renderBox(SOLD_OUT);
    const actions = screen.getByTestId('acciones-ficha');
    expect(within(actions).getByRole('button', { name: 'Agotado' })).toBeDisabled();
    expect(within(actions).queryByRole('button', { name: 'Agregar al carrito' })).not.toBeInTheDocument();
    expect(within(actions).queryByRole('link', { name: 'Reservar ahora' })).not.toBeInTheDocument();
    expect(within(actions).getByRole('link', { name: 'Consultanos por WhatsApp' })).toBeInTheDocument();
    expect(within(actions).getByRole('link', { name: 'Consultar por WhatsApp' })).toBeInTheDocument();
  });

  it('usa la disponibilidad FRESCA: si otro cliente reservó la última, ya no se puede agregar', async () => {
    const sources = mockSources();
    await sources.gateway.create({
      lines: [{ sku: LAST_LAPTOP.sku, quantity: 1, slot: 'peripherals' }],
      contact: { name: 'Otra persona', phone: '70000001' },
      idempotencyKey: 'prueba-ficha-1',
    });
    // La ficha recibe el producto de la instantánea (con 1 disponible) y consulta el fresco (0 disponibles, 1 reservada).
    await renderBox(LAST_LAPTOP, mockCart(), sources);
    const actions = screen.getByTestId('acciones-ficha');
    expect(within(actions).getByRole('button', { name: 'Reservado' })).toBeDisabled();
    expect(within(actions).queryByRole('button', { name: 'Agregar al carrito' })).not.toBeInTheDocument();
  });
});
