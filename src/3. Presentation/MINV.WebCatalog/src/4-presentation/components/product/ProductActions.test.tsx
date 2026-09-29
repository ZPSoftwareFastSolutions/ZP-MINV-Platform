// Botones de la tarjeta y de la fila de producto: TODO producto con disponibilidad (consolas, juegos y portátiles
// incluidos) ofrece «Agregar al carrito» y «Reservar ahora»; «Reservar ahora» lleva directo a la reserva de ese solo
// artículo; las piezas del armador conservan «Agregar al armado»; lo agotado no se puede agregar.

import { fireEvent, screen, within } from '@testing-library/react';
import { useLocation } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import type { Product } from '@/1-domain/catalog/types';
import { useBuildState } from '@/4-presentation/hooks/useBuilder';
import { ProductRow } from '@/4-presentation/pages/catalog/ProductRow';
import { MOCK_CATALOG } from '@/3-infrastructure/data/mockCatalog';
import { mockCart, renderWithApp } from '@/test-utils';
import { ProductCard } from './ProductCard';

function productOf(sku: string): Product {
  const product = MOCK_CATALOG.products.find((item) => item.sku === sku);
  if (!product) throw new Error(`El mock no tiene el producto ${sku}.`);
  return product;
}

const CONSOLE = productOf('CON-NIN-SWOLED'); // consola · 2 disponibles
const GAME = productOf('JUE-NS2-DKBANANZA'); // videojuego · 5 disponibles
const LAPTOP = productOf('LAP-HP-OMEN16-5060'); // portátil · 1 disponible
const CPU = productOf('CPU-AMD-5600'); // pieza del armador · 7 disponibles
const SOLD_OUT = productOf('LAP-LEN-LEGIONPRO7-5080'); // portátil agotada
const SOLD_OUT_PART = productOf('CPU-INT-14700K'); // pieza del armador agotada

/** Dónde está el enrutador y cuántas piezas tiene el armado (para comprobar que el carrito no lo toca). */
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

function renderCard(product: Product, cart = mockCart()) {
  return renderWithApp(
    <>
      <ProductCard product={product} />
      <Probe />
    </>,
    { cart: cart.cart },
  ).then(() => cart);
}

describe('acciones de compra de la tarjeta', () => {
  it.each([
    ['una consola', CONSOLE],
    ['un videojuego', GAME],
    ['una portátil', LAPTOP],
  ])('%s (no es pieza del armador) ofrece «Agregar al carrito» y «Reservar ahora»', async (_name, product) => {
    await renderCard(product);
    const actions = screen.getByTestId('acciones-producto');
    expect(within(actions).getByRole('button', { name: `Agregar al carrito: ${product.shortName}` })).toBeEnabled();
    expect(within(actions).getByRole('link', { name: `Reservar ahora: ${product.shortName}` })).toHaveAttribute('href', `/reservar?sku=${product.sku}&cantidad=1`);
    // No pasa por «Armá tu PC»: no hay botón del armador ni el viejo «Ver producto».
    expect(within(actions).queryByRole('button', { name: /armado/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Ver producto' })).not.toBeInTheDocument();
  });

  it('una pieza del armador ofrece el carrito, la reserva Y «Agregar al armado»', async () => {
    await renderCard(CPU);
    const actions = screen.getByTestId('acciones-producto');
    expect(within(actions).getByRole('button', { name: `Agregar al carrito: ${CPU.shortName}` })).toBeEnabled();
    expect(within(actions).getByRole('link', { name: `Reservar ahora: ${CPU.shortName}` })).toHaveAttribute('href', `/reservar?sku=${CPU.sku}&cantidad=1`);
    fireEvent.click(within(actions).getByRole('button', { name: `Agregar al armado: ${CPU.shortName}` }));
    expect(screen.getByTestId('piezas-armado')).toHaveTextContent('1');
    expect(within(actions).getByRole('button', { name: `En tu armado, agregar otra vez: ${CPU.shortName}` })).toHaveTextContent('En tu armado');
  });

  it('«Agregar al carrito» suma al carrito, avisa y no toca el armado', async () => {
    const { store } = await renderCard(CONSOLE);
    fireEvent.click(screen.getByRole('button', { name: `Agregar al carrito: ${CONSOLE.shortName}` }));

    expect(store.load()).toEqual([{ sku: CONSOLE.sku, quantity: 1 }]);
    expect(screen.getByTestId('piezas-armado')).toHaveTextContent('0');
    // El aviso sale en la región viva de avisos (se anuncia sin interrumpir) y ofrece «Ver carrito».
    const toasts = document.querySelector<HTMLElement>('[data-toasts]')!;
    expect(toasts).toHaveAttribute('aria-live', 'polite');
    expect(within(toasts).getByText('Agregado al carrito')).toBeInTheDocument();
    expect(within(toasts).getByText(`${CONSOLE.shortName} · 1 unidad en tu carrito`)).toBeInTheDocument();
    expect(within(toasts).getByRole('button', { name: 'Ver carrito' })).toBeInTheDocument();
    // El botón recuerda cuántas hay en el carrito.
    expect(screen.getByRole('button', { name: `Agregar al carrito: ${CONSOLE.shortName} (1 unidad en tu carrito)` })).toBeInTheDocument();
    expect(screen.getByTestId('en-carrito')).toHaveTextContent('1');
    expect(screen.getByTestId('ruta')).toHaveTextContent('/');
  });

  it('«Ver carrito» del aviso lleva al carrito', async () => {
    await renderCard(GAME);
    fireEvent.click(screen.getByRole('button', { name: `Agregar al carrito: ${GAME.shortName}` }));
    fireEvent.click(screen.getByRole('button', { name: 'Ver carrito' }));
    expect(screen.getByTestId('ruta')).toHaveTextContent('/carrito');
  });

  it('varios productos seguidos muestran un solo aviso', async () => {
    await renderCard(GAME);
    const add = screen.getByRole('button', { name: /^Agregar al carrito/ });
    fireEvent.click(add);
    fireEvent.click(add);
    fireEvent.click(add);
    expect(screen.getAllByText('Agregado al carrito')).toHaveLength(1);
    expect(screen.getByText(`${GAME.shortName} · 3 unidades en tu carrito`)).toBeInTheDocument();
  });

  it('no deja pasar de lo disponible: avisa y el carrito queda igual', async () => {
    const { store } = await renderCard(CONSOLE, mockCart([{ sku: CONSOLE.sku, quantity: 2 }]));
    fireEvent.click(screen.getByRole('button', { name: /^Agregar al carrito/ }));
    expect(screen.getByText('Ya tenés todo lo disponible')).toBeInTheDocument();
    expect(screen.queryByText('Agregado al carrito')).not.toBeInTheDocument();
    expect(store.load()).toEqual([{ sku: CONSOLE.sku, quantity: 2 }]);
  });

  it('«Reservar ahora» va directo a la reserva de ESE artículo, sin pasar por el carrito ni por el armado', async () => {
    const { store } = await renderCard(CONSOLE);
    fireEvent.click(screen.getByRole('link', { name: `Reservar ahora: ${CONSOLE.shortName}` }));
    expect(screen.getByTestId('ruta')).toHaveTextContent(`/reservar?sku=${CONSOLE.sku}&cantidad=1`);
    expect(store.load()).toEqual([]);
    expect(screen.getByTestId('piezas-armado')).toHaveTextContent('0');
  });

  it.each([
    ['un producto agotado', SOLD_OUT],
    ['una pieza del armador agotada', SOLD_OUT_PART],
  ])('%s no se puede agregar ni reservar', async (_name, product) => {
    await renderCard(product);
    const actions = screen.getByTestId('acciones-producto');
    expect(within(actions).getByRole('button', { name: `Agotado: ${product.shortName}` })).toBeDisabled();
    expect(within(actions).queryByRole('button', { name: /Agregar al carrito/ })).not.toBeInTheDocument();
    expect(within(actions).queryByRole('link', { name: /Reservar ahora/ })).not.toBeInTheDocument();
    expect(within(actions).queryByRole('button', { name: /armado/i })).not.toBeInTheDocument();
  });

  it('la tarjeta sigue enlazando a la ficha', async () => {
    await renderCard(CONSOLE);
    expect(screen.getByRole('link', { name: CONSOLE.shortName })).toHaveAttribute('href', `/producto/${CONSOLE.slug}`);
  });
});

describe('acciones de compra de la fila (vista en lista)', () => {
  it('ofrece las mismas acciones que la tarjeta', async () => {
    const cart = mockCart();
    await renderWithApp(<ProductRow product={GAME} />, { cart: cart.cart });
    const actions = screen.getByTestId('acciones-producto');
    expect(within(actions).getByRole('link', { name: `Reservar ahora: ${GAME.shortName}` })).toHaveAttribute('href', `/reservar?sku=${GAME.sku}&cantidad=1`);
    fireEvent.click(within(actions).getByRole('button', { name: `Agregar al carrito: ${GAME.shortName}` }));
    expect(cart.store.load()).toEqual([{ sku: GAME.sku, quantity: 1 }]);
    expect(screen.getByText('Agregado al carrito')).toBeInTheDocument();
  });

  it('una pieza del armador conserva «Agregar al armado»', async () => {
    await renderWithApp(<ProductRow product={CPU} />);
    expect(screen.getByRole('button', { name: `Agregar al armado: ${CPU.shortName}` })).toBeEnabled();
    expect(screen.getByRole('button', { name: `Agregar al carrito: ${CPU.shortName}` })).toBeEnabled();
  });
});
