// Página del carrito (`/carrito`) con la tabla de rutas REAL: carrito vacío, lista con cantidades y subtotales, quitar,
// vaciar con confirmación, total, disponibilidad fresca (marca lo que se agotó o bajó y ofrece ajustar), «Seguir
// comprando», «Reservar» y el ícono de la cabecera con las unidades. Catálogo y carrito en memoria: nada toca la red.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { CartItem } from '@/1-domain/cart/types';
import type { Product } from '@/1-domain/catalog/types';
import { StorefrontError } from '@/1-domain/storefront/errors';
import type { Sources } from '@/4-presentation/app/container';
import { MOCK_CATALOG } from '@/3-infrastructure/data/mockCatalog';
import { mockCart, mockSources, renderRoutes, type MockCart } from '@/test-utils';

function productOf(sku: string): Product {
  const product = MOCK_CATALOG.products.find((item) => item.sku === sku);
  if (!product) throw new Error(`El mock no tiene el producto ${sku}.`);
  return product;
}

const CONSOLE = productOf('CON-NIN-SWOLED'); // 2 disponibles · Bs 4.399
const GAME = productOf('JUE-NS2-DKBANANZA'); // 5 disponibles · Bs 829
const CPU = productOf('CPU-AMD-5600'); // 7 disponibles · Bs 1.099 (lista Bs 1.263,85)

interface Opened extends MockCart {
  app: Awaited<ReturnType<typeof renderRoutes>>;
  sources: Sources;
}

async function openCart(items: CartItem[] = [], options: { sources?: Sources; persistent?: boolean; wait?: boolean } = {}): Promise<Opened> {
  const sources = options.sources ?? mockSources();
  const cart = mockCart(items, { persistent: options.persistent });
  const app = await renderRoutes({ route: '/carrito', cart: cart.cart, sources });
  await screen.findByRole('heading', { level: 1, name: 'Carrito' });
  // Al abrirse vuelve a consultar la disponibilidad: se espera a que termine.
  if (items.length > 0 && options.wait !== false) await waitFor(() => expect(screen.getByTestId('carrito-disponibilidad')).not.toHaveAttribute('data-freshness', 'checking'));
  return { ...cart, app, sources };
}

function line(product: Pick<Product, 'sku'>): HTMLElement {
  const found = screen.getAllByTestId('linea-carrito').find((item) => item.getAttribute('data-sku') === product.sku);
  if (!found) throw new Error(`No hay una línea del carrito para ${product.sku}.`);
  return found;
}

function skus(): (string | null)[] {
  return screen.queryAllByTestId('linea-carrito').map((item) => item.getAttribute('data-sku'));
}

/** Otro cliente reserva unidades: baja lo disponible en la tienda. */
function reserveElsewhere(sources: Sources, sku: string, quantity: number) {
  return sources.gateway.create({
    lines: [{ sku, quantity, slot: 'peripherals' }],
    contact: { name: 'Otra persona', phone: '70000001' },
    idempotencyKey: `prueba-${sku}-${quantity}`,
  });
}

describe('carrito vacío', () => {
  it('invita a ver el catálogo', async () => {
    await openCart();
    expect(screen.getByText('Tu carrito está vacío')).toBeInTheDocument();
    expect(screen.getByTestId('carrito-resumen')).toHaveTextContent('Todavía no agregaste productos.');
    expect(screen.getByRole('link', { name: 'Ver el catálogo' })).toHaveAttribute('href', '/catalogo');
    expect(screen.queryByRole('link', { name: 'Reservar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Vaciar carrito' })).not.toBeInTheDocument();
    // El ícono de la cabecera no lleva número.
    expect(screen.getByRole('link', { name: 'Carrito' })).toHaveAttribute('href', '/carrito');
  });
});

describe('lista del carrito', () => {
  const ITEMS: CartItem[] = [
    { sku: CONSOLE.sku, quantity: 1 },
    { sku: GAME.sku, quantity: 2 },
    { sku: CPU.sku, quantity: 1 },
  ];

  it('muestra cada producto con su nombre, precio, disponibilidad, cantidad y subtotal, y el total', async () => {
    await openCart(ITEMS);
    expect(skus()).toEqual([CONSOLE.sku, GAME.sku, CPU.sku]);

    const game = line(GAME);
    expect(within(game).getByRole('link', { name: GAME.shortName })).toHaveAttribute('href', `/producto/${GAME.slug}`);
    expect(game).toHaveTextContent('Precio: Bs 829,00');
    expect(game).toHaveTextContent('Disponible (5)');
    expect(within(game).getByRole('spinbutton', { name: `Cantidad de ${GAME.shortName}` })).toHaveValue(2);
    expect(within(game).getByTestId('linea-subtotal')).toHaveTextContent('Bs 1.658,00');
    expect(game.querySelector('img')).toHaveAttribute('src', GAME.image);

    expect(screen.getByTestId('carrito-resumen')).toHaveTextContent('4 productos');
    const summary = screen.getByRole('region', { name: 'Resumen' });
    expect(within(summary).getByText('4 unidades')).toBeInTheDocument();
    expect(within(summary).getByTestId('carrito-total-importe')).toHaveTextContent('Bs 7.156,00');
    // El CPU está en oferta: ahorro frente al precio de lista.
    expect(within(summary).getByText('Bs 164,85')).toBeInTheDocument();
  });

  it('el ícono de la cabecera muestra las unidades y se actualiza', async () => {
    await openCart(ITEMS);
    expect(screen.getByRole('link', { name: 'Carrito, 4 productos' })).toHaveAttribute('href', '/carrito');
    fireEvent.click(within(line(GAME)).getByRole('button', { name: 'Agregar una unidad' }));
    expect(screen.getByRole('link', { name: 'Carrito, 5 productos' })).toBeInTheDocument();
  });

  it('el menú móvil también lleva al carrito con sus unidades', async () => {
    await openCart(ITEMS);
    fireEvent.click(screen.getByRole('button', { name: 'Abrir menú' }));
    const menu = await screen.findByRole('dialog', { name: 'Menú' });
    const link = within(menu).getByRole('link', { name: /^Carrito/ });
    expect(link).toHaveAttribute('href', '/carrito');
    expect(link).toHaveTextContent('4 productos en tu carrito');
  });

  it('más y menos cambian la cantidad, el subtotal y el total, y lo guardan', async () => {
    const { store } = await openCart(ITEMS);
    const game = line(GAME);
    fireEvent.click(within(game).getByRole('button', { name: 'Agregar una unidad' }));
    expect(within(game).getByRole('spinbutton')).toHaveValue(3);
    expect(within(game).getByTestId('linea-subtotal')).toHaveTextContent('Bs 2.487,00');
    expect(screen.getByTestId('carrito-total-importe')).toHaveTextContent('Bs 7.985,00');

    fireEvent.click(within(game).getByRole('button', { name: 'Quitar una unidad' }));
    fireEvent.click(within(game).getByRole('button', { name: 'Quitar una unidad' }));
    expect(within(game).getByRole('spinbutton')).toHaveValue(1);
    expect(store.load()).toContainEqual({ sku: GAME.sku, quantity: 1 });

    // En 1 el botón de menos no quita la línea (para eso está «Quitar»).
    fireEvent.click(within(game).getByRole('button', { name: 'Quitar una unidad' }));
    expect(skus()).toContain(GAME.sku);
    expect(within(game).getByRole('spinbutton')).toHaveValue(1);
  });

  it('la cantidad nunca pasa de lo disponible', async () => {
    const { store } = await openCart(ITEMS);
    const console_ = line(CONSOLE);
    const plus = within(console_).getByRole('button', { name: 'Agregar una unidad' });
    for (let click = 0; click < 4; click += 1) fireEvent.click(plus);
    expect(within(console_).getByRole('spinbutton')).toHaveValue(2);
    fireEvent.change(within(console_).getByRole('spinbutton'), { target: { value: '15' } });
    expect(within(console_).getByRole('spinbutton')).toHaveValue(2);
    expect(store.load()).toContainEqual({ sku: CONSOLE.sku, quantity: 2 });
  });

  it('«Quitar» saca el producto y avisa', async () => {
    const { store } = await openCart(ITEMS);
    fireEvent.click(screen.getByRole('button', { name: `Quitar ${GAME.shortName} del carrito` }));
    expect(skus()).toEqual([CONSOLE.sku, CPU.sku]);
    expect(store.load()).toEqual([
      { sku: CONSOLE.sku, quantity: 1 },
      { sku: CPU.sku, quantity: 1 },
    ]);
    expect(screen.getByText('Quitado del carrito')).toBeInTheDocument();
    expect(screen.getByTestId('carrito-total-importe')).toHaveTextContent('Bs 5.498,00');
  });

  it('quitar el último producto deja el carrito vacío', async () => {
    await openCart([{ sku: GAME.sku, quantity: 1 }]);
    fireEvent.click(screen.getByRole('button', { name: `Quitar ${GAME.shortName} del carrito` }));
    expect(screen.getByText('Tu carrito está vacío')).toBeInTheDocument();
  });

  it('«Vaciar carrito» pide confirmación: «No» lo conserva y «Sí» lo vacía', async () => {
    const { store } = await openCart(ITEMS);
    fireEvent.click(screen.getByRole('button', { name: 'Vaciar carrito' }));
    const confirm = screen.getByRole('alertdialog', { name: '¿Vaciar el carrito?' });
    expect(confirm).toHaveTextContent('Se quitan los 4 productos que elegiste.');
    expect(store.load()).toHaveLength(3);

    fireEvent.click(within(confirm).getByRole('button', { name: 'No, conservar' }));
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(skus()).toHaveLength(3);
    expect(screen.getByRole('button', { name: 'Vaciar carrito' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Vaciar carrito' }));
    fireEvent.click(screen.getByRole('button', { name: 'Sí, vaciar' }));
    expect(screen.getByText('Tu carrito está vacío')).toBeInTheDocument();
    expect(store.load()).toEqual([]);
    expect(screen.getByText('Vaciaste el carrito')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Carrito' })).toBeInTheDocument();
  });

  it('«Seguir comprando» vuelve al catálogo y «Reservar» lleva a la reserva del carrito', async () => {
    const { app, store } = await openCart(ITEMS);
    expect(screen.getByRole('link', { name: 'Seguir comprando' })).toHaveAttribute('href', '/catalogo');
    const reserve = screen.getByRole('link', { name: 'Reservar' });
    expect(reserve).toHaveAttribute('href', '/reservar');
    expect(reserve).not.toHaveAttribute('aria-disabled');
    fireEvent.click(reserve);
    expect(await screen.findByRole('heading', { level: 1, name: 'Reservar' })).toBeInTheDocument();
    expect(app.location()).toBe('/reservar');
    // Pasar a reservar no vacía el carrito.
    expect(store.load()).toHaveLength(3);
  });

  it('se pone al día si otra pestaña cambia el carrito', async () => {
    const { store } = await openCart(ITEMS);
    act(() => store.changeFromElsewhere([{ sku: CPU.sku, quantity: 3 }]));
    expect(skus()).toEqual([CPU.sku]);
    expect(screen.getByTestId('carrito-total-importe')).toHaveTextContent('Bs 3.297,00');
  });
});

describe('disponibilidad fresca', () => {
  it('al abrirse vuelve a consultar el catálogo a la tienda', async () => {
    const sources = mockSources();
    const load = vi.spyOn(sources.source, 'load');
    await openCart([{ sku: GAME.sku, quantity: 1 }], { sources });
    expect(load).toHaveBeenCalledTimes(2);
    expect(screen.getByTestId('carrito-disponibilidad')).toHaveAttribute('data-freshness', 'fresh');
    expect(screen.getByText('Disponibilidad recién consultada')).toBeInTheDocument();
    expect(screen.queryByText(/cambi(ó|aron) de disponibilidad/)).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Actualizar' }));
    await waitFor(() => expect(load).toHaveBeenCalledTimes(3));
    await waitFor(() => expect(screen.getByTestId('carrito-disponibilidad')).toHaveAttribute('data-freshness', 'fresh'));
  });

  it('marca lo que bajó y lo que se agotó, no deja reservar y ofrece ajustar todo', async () => {
    const sources = mockSources();
    await reserveElsewhere(sources, CONSOLE.sku, 1); // quedan 1 de 2
    await reserveElsewhere(sources, GAME.sku, 5); // no queda ninguno: reservados
    const { store } = await openCart(
      [
        { sku: CONSOLE.sku, quantity: 2 },
        { sku: GAME.sku, quantity: 1 },
        { sku: CPU.sku, quantity: 1 },
      ],
      { sources },
    );

    const console_ = line(CONSOLE);
    expect(console_).toHaveAttribute('data-status', 'reduced');
    expect(within(console_).getByTestId('linea-aviso')).toHaveTextContent('Pediste 2 y queda 1 sola unidad.');
    const game = line(GAME);
    expect(game).toHaveAttribute('data-status', 'sold_out');
    expect(within(game).getByTestId('linea-aviso')).toHaveTextContent('Las unidades que quedan están reservadas por otros clientes.');
    expect(within(game).getByTestId('linea-subtotal')).toHaveTextContent('No entra en el total');
    expect(within(game).queryByRole('spinbutton')).not.toBeInTheDocument();
    expect(line(CPU)).toHaveAttribute('data-status', 'ok');
    expect(within(line(CPU)).queryByTestId('linea-aviso')).not.toBeInTheDocument();

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('2 productos cambiaron de disponibilidad');
    expect(screen.getByRole('link', { name: 'Reservar' })).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByText('Ajustá el carrito a lo disponible para poder reservar.')).toBeInTheDocument();
    // Nada se corrigió solo.
    expect(store.load()).toHaveLength(3);

    fireEvent.click(within(alert).getByRole('button', { name: 'Ajustar carrito' }));
    expect(skus()).toEqual([CONSOLE.sku, CPU.sku]);
    expect(store.load()).toEqual([
      { sku: CONSOLE.sku, quantity: 1 },
      { sku: CPU.sku, quantity: 1 },
    ]);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Reservar' })).not.toHaveAttribute('aria-disabled');
    expect(screen.getByText('Carrito ajustado a lo disponible')).toBeInTheDocument();
    expect(screen.getByText(`${CONSOLE.shortName}: de 2 a 1 · ${GAME.shortName}: lo quitamos`)).toBeInTheDocument();
  });

  it('un clic en «Reservar» bloqueado no lleva a la reserva', async () => {
    const sources = mockSources();
    await reserveElsewhere(sources, CONSOLE.sku, 1);
    const { app } = await openCart([{ sku: CONSOLE.sku, quantity: 2 }], { sources });
    fireEvent.click(screen.getByRole('link', { name: 'Reservar' }));
    expect(app.location()).toBe('/carrito');
  });

  it('cada línea ofrece su propio ajuste', async () => {
    const sources = mockSources();
    await reserveElsewhere(sources, CONSOLE.sku, 1);
    await reserveElsewhere(sources, GAME.sku, 5);
    const { store } = await openCart(
      [
        { sku: CONSOLE.sku, quantity: 2 },
        { sku: GAME.sku, quantity: 1 },
      ],
      { sources },
    );
    fireEvent.click(screen.getByRole('button', { name: `Ajustar ${CONSOLE.shortName} a 1 unidad` }));
    expect(store.load()).toEqual([
      { sku: CONSOLE.sku, quantity: 1 },
      { sku: GAME.sku, quantity: 1 },
    ]);
    expect(screen.getByRole('alert')).toHaveTextContent('Un producto cambió de disponibilidad');

    // Lo agotado se quita desde su aviso (o con el botón «Quitar» de la línea: los dos dicen lo mismo).
    expect(within(line(GAME)).getAllByRole('button', { name: `Quitar ${GAME.shortName} del carrito` })).toHaveLength(2);
    fireEvent.click(within(within(line(GAME)).getByTestId('linea-aviso')).getByRole('button', { name: `Quitar ${GAME.shortName} del carrito` }));
    expect(skus()).toEqual([CONSOLE.sku]);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('un producto que ya no está publicado se marca y se puede quitar', async () => {
    const { store } = await openCart([
      { sku: 'YA-NO-EXISTE', quantity: 2 },
      { sku: GAME.sku, quantity: 1 },
    ]);
    const gone = line({ sku: 'YA-NO-EXISTE' });
    expect(gone).toHaveAttribute('data-status', 'unavailable');
    expect(gone).toHaveTextContent('Este producto ya no está publicado en el catálogo.');
    expect(gone).toHaveTextContent('Pediste 2 unidades');
    expect(screen.getByTestId('carrito-total-importe')).toHaveTextContent('Bs 829,00');
    fireEvent.click(within(within(gone).getByTestId('linea-aviso')).getByRole('button', { name: 'Quitar YA-NO-EXISTE del carrito' }));
    expect(store.load()).toEqual([{ sku: GAME.sku, quantity: 1 }]);
  });

  it('si no puede consultar la tienda avisa, muestra la última disponibilidad conocida y deja reintentar', async () => {
    const sources = mockSources();
    const original = sources.source.load.bind(sources.source);
    const load = vi
      .spyOn(sources.source, 'load')
      .mockImplementationOnce(original)
      .mockRejectedValueOnce(new StorefrontError({ kind: 'network', status: 0, detail: 'sin conexión' }));
    await openCart([{ sku: GAME.sku, quantity: 2 }], { sources });

    expect(screen.getByTestId('carrito-disponibilidad')).toHaveAttribute('data-freshness', 'stale');
    expect(screen.getByRole('alert')).toHaveTextContent('No pudimos consultar la disponibilidad');
    // El carrito sigue en pantalla con lo último que se sabe.
    expect(within(line(GAME)).getByTestId('linea-subtotal')).toHaveTextContent('Bs 1.658,00');
    expect(screen.getByRole('link', { name: 'Reservar' })).not.toHaveAttribute('aria-disabled');

    fireEvent.click(screen.getByRole('button', { name: 'Actualizar' }));
    await waitFor(() => expect(screen.getByTestId('carrito-disponibilidad')).toHaveAttribute('data-freshness', 'fresh'));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(load).toHaveBeenCalledTimes(3);
  });
});

describe('carrito sin guardar', () => {
  it('avisa cuando el navegador no deja guardar el carrito', async () => {
    await openCart([{ sku: GAME.sku, quantity: 1 }], { persistent: false });
    expect(screen.getByText('Tu carrito no se está guardando')).toBeInTheDocument();
    // Y funciona igual.
    fireEvent.click(within(line(GAME)).getByRole('button', { name: 'Agregar una unidad' }));
    expect(within(line(GAME)).getByRole('spinbutton')).toHaveValue(2);
  });

  it('no muestra el aviso cuando el carrito se guarda', async () => {
    await openCart([{ sku: GAME.sku, quantity: 1 }]);
    expect(screen.queryByText('Tu carrito no se está guardando')).not.toBeInTheDocument();
  });
});
