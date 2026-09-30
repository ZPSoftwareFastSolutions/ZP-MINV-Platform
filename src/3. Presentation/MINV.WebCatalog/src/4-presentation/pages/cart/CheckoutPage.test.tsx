// Página de reserva (`/reservar`) con la tabla de rutas REAL: un artículo suelto («Reservar ahora») no toca el carrito;
// el carrito se reserva y se vacía de lo reservado; validación por campo al salir y al enviar; notas en una línea; 409 con
// «Ajustar a lo disponible» sin perder lo escrito; la misma llave al reintentar por red y otra al cambiar algo; 400 por
// campo y 429; el cliente con sesión reserva por RPC sin volver a escribir sus datos; y la confirmación con y sin el
// correo en cola. Catálogo, reservas, sesión y carrito en memoria: nada toca la red.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { CartItem } from '@/1-domain/cart/types';
import type { Product } from '@/1-domain/catalog/types';
import type { IReservationGateway } from '@/1-domain/ports/IReservationGateway';
import { StorefrontError } from '@/1-domain/storefront/errors';
import type { ReservationRequest } from '@/1-domain/storefront/types';
import type { Sources, WebServices } from '@/4-presentation/app/container';
import { InMemoryReservationGateway, MOCK_CATALOG } from '@/3-infrastructure/data/mockCatalog';
import { isUuid } from '@/shared/ids';
import { mockCart, mockSources, renderRoutes, signedInWeb } from '@/test-utils';

function productOf(sku: string): Product {
  const product = MOCK_CATALOG.products.find((item) => item.sku === sku);
  if (!product) throw new Error(`El mock no tiene el producto ${sku}.`);
  return product;
}

const CONSOLE = productOf('CON-NIN-SWOLED'); // 2 disponibles
const GAME = productOf('JUE-NS2-DKBANANZA'); // 5 disponibles
const CPU = productOf('CPU-AMD-5600'); // 7 disponibles

interface OpenOptions {
  route?: string;
  items?: CartItem[];
  sources?: Sources;
  web?: WebServices;
}

async function openCheckout({ route = '/reservar', items = [], sources = mockSources(), web }: OpenOptions = {}) {
  const cart = mockCart(items);
  const app = await renderRoutes({ route, cart: cart.cart, sources, ...(web ? { web } : {}) });
  await screen.findByRole('heading', { level: 1, name: 'Reservar' });
  await waitFor(() => expect(screen.getByTestId('reserva-disponibilidad')).not.toHaveAttribute('data-freshness', 'checking'));
  return { app, sources, ...cart };
}

/** Pasarela que registra cada pedido y responde con lo que diga `respond` (o con la pasarela real). */
function recordingGateway(real: IReservationGateway, respond?: (request: ReservationRequest, call: number) => Promise<never> | null) {
  const requests: ReservationRequest[] = [];
  const gateway: IReservationGateway = {
    create: (request) => {
      requests.push(request);
      return respond?.(request, requests.length) ?? real.create(request);
    },
    get: (number, phone) => real.get(number, phone),
    findByPhone: (phone) => real.findByPhone(phone),
    cancel: (number, phone) => real.cancel(number, phone),
  };
  return { gateway, requests };
}

async function fillGuest({ name = 'Valentina Aguirre', phone = '71234567', email = '' }: { name?: string; phone?: string; email?: string } = {}) {
  fireEvent.change(await screen.findByLabelText(/^Nombre y apellido/), { target: { value: name } });
  fireEvent.change(screen.getByLabelText(/^Teléfono o WhatsApp/), { target: { value: phone } });
  fireEvent.change(screen.getByLabelText(/^Correo/), { target: { value: email } });
}

function confirm() {
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar reserva' }));
}

function lineOf(sku: string): HTMLElement {
  const found = screen.getAllByTestId('reserva-linea').find((item) => item.getAttribute('data-sku') === sku);
  if (!found) throw new Error(`No hay una línea de la reserva para ${sku}.`);
  return found;
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('artículo suelto («Reservar ahora»)', () => {
  it('reserva ESE producto con su cantidad y NO toca el carrito', async () => {
    const { store, sources } = await openCheckout({ route: `/reservar?sku=${GAME.sku}&cantidad=2`, items: [{ sku: CPU.sku, quantity: 1 }] });
    expect(screen.getByRole('heading', { level: 2, name: 'Lo que reservás' })).toBeInTheDocument();
    expect(screen.getAllByTestId('reserva-linea').map((item) => item.getAttribute('data-sku'))).toEqual([GAME.sku]);
    expect(screen.getByTestId('reserva-total')).toHaveTextContent('Bs 1.658,00');

    await fillGuest({ email: 'valentina@correo.example' });
    confirm();
    const done = await screen.findByTestId('reserva-confirmada');
    expect(within(done).getByTestId('confirmacion-numero')).toHaveTextContent('RES-WEB-000001');
    // El carrito sigue exactamente igual.
    expect(store.load()).toEqual([{ sku: CPU.sku, quantity: 1 }]);
    expect(sources.gateway instanceof InMemoryReservationGateway && sources.gateway.product(GAME.sku)).toMatchObject({ reserved: 2, stock: GAME.stock - 2 });
  });

  it('la cantidad se cambia en la misma página (acotada a lo disponible)', async () => {
    const { app } = await openCheckout({ route: `/reservar?sku=${CONSOLE.sku}&cantidad=1` });
    fireEvent.click(within(lineOf(CONSOLE.sku)).getByRole('button', { name: 'Agregar una unidad' }));
    await waitFor(() => expect(app.location()).toBe(`/reservar?sku=${CONSOLE.sku}&cantidad=2`));
    expect(screen.getByTestId('reserva-total')).toHaveTextContent('Bs 8.798,00');
  });

  it('un SKU que no existe muestra que no encontramos el producto', async () => {
    await renderRoutes({ route: '/reservar?sku=NO-EXISTE-99', cart: mockCart().cart });
    expect(await screen.findByText('No encontramos ese producto')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'Reservar' })).toBeInTheDocument();
  });
});

describe('carrito', () => {
  it('con el carrito vacío invita al catálogo', async () => {
    await renderRoutes({ route: '/reservar', cart: mockCart().cart });
    expect(await screen.findByText('Tu carrito está vacío')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver el catálogo' })).toHaveAttribute('href', '/catalogo');
  });

  it('reserva el carrito, lo vacía de lo reservado y confirma el envío del correo', async () => {
    const { store } = await openCheckout({ items: [{ sku: CPU.sku, quantity: 2 }, { sku: GAME.sku, quantity: 1 }] });
    expect(screen.getByRole('heading', { level: 2, name: 'Tu carrito' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Editar carrito' })).toHaveAttribute('href', '/carrito');
    expect(screen.getByTestId('reserva-invitacion')).toHaveTextContent('¿Tenés cuenta? Ingresá y seguí tus reservas.');
    expect(within(screen.getByTestId('reserva-invitacion')).getByRole('link', { name: 'Ingresar' })).toHaveAttribute('href', '/ingresar?volver=%2Freservar');

    await fillGuest({ email: 'valentina@correo.example' });
    fireEvent.change(screen.getByLabelText(/^¿Cuándo pasás a recogerlo\?/), { target: { value: '3' } });
    confirm();

    const done = await screen.findByTestId('reserva-confirmada');
    expect(screen.getByRole('heading', { level: 1, name: 'Tu reserva quedó registrada' })).toHaveFocus();
    expect(within(done).getByTestId('confirmacion-numero')).toHaveTextContent('RES-WEB-000001');
    expect(within(done).getByTestId('tipo-reserva')).toHaveTextContent('Compra');
    expect(done).toHaveTextContent('Reservada');
    expect(within(done).getByTestId('confirmacion-vence')).toHaveTextContent('72 h');
    expect(done).toHaveTextContent('Casa matriz La Paz');
    expect(within(done).getByTestId('confirmacion-total')).toHaveTextContent('Bs 3.027,00');
    expect(done).toHaveTextContent('Te enviamos un correo a valentina@correo.example');
    expect(within(done).getByRole('link', { name: 'Mi reserva' })).toHaveAttribute('href', '/reserva/RES-WEB-000001');
    expect(within(done).getByRole('link', { name: 'Seguir en el catálogo' })).toHaveAttribute('href', '/catalogo');
    expect(store.load()).toEqual([]);
  });

  it('sin correo no dice que lo envió: pide guardar el número, y «Mi reserva» la muestra como compra', async () => {
    const { app } = await openCheckout({ items: [{ sku: GAME.sku, quantity: 1 }] });
    await fillGuest();
    confirm();
    const done = await screen.findByTestId('reserva-confirmada');
    expect(done).not.toHaveTextContent('Te enviamos un correo');
    expect(done).toHaveTextContent('Guardá este número');

    fireEvent.click(within(done).getByRole('link', { name: 'Mi reserva' }));
    await waitFor(() => expect(app.location()).toBe('/reserva/RES-WEB-000001'));
    fireEvent.change(screen.getByLabelText('Número de celular'), { target: { value: '71234567' } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar reserva' }));
    const summary = await screen.findByTestId('resumen-reserva');
    expect(within(summary).getByTestId('tipo-reserva')).toHaveTextContent('Compra');
    expect(summary).toHaveTextContent('se guarda 48 h');
    expect(within(summary).getByRole('table')).toHaveTextContent('Producto');
  });

  it('«Copiar» copia el número de la reserva', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    await openCheckout({ items: [{ sku: GAME.sku, quantity: 1 }] });
    await fillGuest();
    confirm();
    await screen.findByTestId('reserva-confirmada');
    fireEvent.click(screen.getByRole('button', { name: 'Copiar el número RES-WEB-000001' }));
    expect(await screen.findByText('Número copiado.')).toBeInTheDocument();
    expect(writeText).toHaveBeenCalledWith('RES-WEB-000001');
  });
});

describe('validación', () => {
  it('valida cada campo al salir de él y todos al enviar (el foco va al primero con error)', async () => {
    const sources = mockSources();
    const { gateway, requests } = recordingGateway(sources.gateway);
    await openCheckout({ items: [{ sku: GAME.sku, quantity: 1 }], sources: { ...sources, gateway } });
    const name = await screen.findByLabelText(/^Nombre y apellido/);
    // Nada se marca antes de visitar el campo.
    expect(screen.queryByText('Indicá tu nombre y apellido.')).not.toBeInTheDocument();
    fireEvent.blur(name);
    expect(await screen.findByText('Indicá tu nombre y apellido.')).toBeInTheDocument();
    expect(name).toHaveAttribute('aria-invalid', 'true');

    const email = screen.getByLabelText(/^Correo/);
    fireEvent.change(email, { target: { value: 'valentina@' } });
    fireEvent.blur(email);
    expect(await screen.findByText('Revisá el correo: debe tener la forma nombre@dominio.')).toBeInTheDocument();

    confirm();
    expect(await screen.findByText('Indicá un teléfono o WhatsApp.')).toBeInTheDocument();
    await waitFor(() => expect(name).toHaveFocus());
    expect(requests).toHaveLength(0);

    // Al corregir, el error se va mientras escribe.
    fireEvent.change(name, { target: { value: 'Valentina Aguirre' } });
    expect(screen.queryByText('Indicá tu nombre y apellido.')).not.toBeInTheDocument();
  });

  it('los datos para la factura están plegados; si tienen errores al enviar, la sección se abre y marca el campo', async () => {
    const sources = mockSources();
    const { gateway, requests } = recordingGateway(sources.gateway);
    await openCheckout({ items: [{ sku: GAME.sku, quantity: 1 }], sources: { ...sources, gateway } });
    await fillGuest();
    const toggle = screen.getByRole('button', { name: /Datos para tu factura/ });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    fireEvent.change(screen.getByLabelText('Tipo de documento'), { target: { value: '5' } });
    const number = screen.getByLabelText(/^Número de documento/);
    fireEvent.change(number, { target: { value: '12AB' } });
    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(toggle).toHaveTextContent('NIT 12AB');

    confirm();
    expect(await screen.findByText('Con NIT el número de documento solo admite dígitos.')).toBeInTheDocument();
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    await waitFor(() => expect(screen.getByLabelText(/^Número de documento/)).toHaveFocus());
    expect(requests).toHaveLength(0);

    fireEvent.change(screen.getByLabelText(/^Número de documento/), { target: { value: '1020304050' } });
    fireEvent.change(screen.getByLabelText(/^Nombre o razón social/), { target: { value: 'Tech Zone Clientes SRL' } });
    confirm();
    await screen.findByTestId('reserva-confirmada');
    expect(requests[0]).toMatchObject({ kind: 'cart', buyer: { documentType: 5, documentNumber: '1020304050', name: 'Tech Zone Clientes SRL' } });
  });

  it('las notas con saltos de línea se envían en UNA línea', async () => {
    const sources = mockSources();
    const { gateway, requests } = recordingGateway(sources.gateway);
    await openCheckout({ items: [{ sku: GAME.sku, quantity: 1 }], sources: { ...sources, gateway } });
    await fillGuest();
    fireEvent.change(screen.getByLabelText(/^Notas para la tienda/), { target: { value: 'Paso el sábado\npor la mañana\n\ncon mi hermano' } });
    confirm();
    const done = await screen.findByTestId('reserva-confirmada');
    expect(requests[0].notes).toBe('Paso el sábado por la mañana con mi hermano');
    expect(requests[0].lines).toEqual([{ sku: GAME.sku, quantity: 1 }]);
    expect(requests[0].kind).toBe('cart');
    expect(done).toHaveTextContent('Paso el sábado por la mañana con mi hermano');
  });
});

describe('fallas al reservar', () => {
  it('409: marca el producto y cuánto hay, «Ajustar a lo disponible» baja la cantidad y NO pierde lo escrito', async () => {
    const sources = mockSources();
    const { store } = await openCheckout({ items: [{ sku: CONSOLE.sku, quantity: 2 }], sources });
    await fillGuest({ email: 'valentina@correo.example' });
    fireEvent.change(screen.getByLabelText(/^Notas para la tienda/), { target: { value: 'Paso el sábado' } });
    // Mientras tanto, otra persona reservó una de las dos consolas (el catálogo de esta página todavía no lo sabe).
    await act(async () => {
      await sources.gateway.create({ lines: [{ sku: CONSOLE.sku, quantity: 1 }], contact: { name: 'Otra persona', phone: '70000001' }, idempotencyKey: 'otra', kind: 'cart' });
    });
    confirm();

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('No alcanzó el stock para reservar todo');
    expect(within(lineOf(CONSOLE.sku)).getByTestId('reserva-linea-aviso')).toHaveTextContent('Pediste 2; hay 1 disponible.');
    // Lo escrito sigue ahí.
    expect(screen.getByLabelText(/^Nombre y apellido/)).toHaveValue('Valentina Aguirre');
    expect(screen.getByLabelText(/^Correo/)).toHaveValue('valentina@correo.example');
    expect(screen.getByLabelText(/^Notas para la tienda/)).toHaveValue('Paso el sábado');

    fireEvent.click(within(alert).getByRole('button', { name: 'Ajustar a lo disponible' }));
    expect(store.load()).toEqual([{ sku: CONSOLE.sku, quantity: 1 }]);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Confirmar reserva' })).toBeEnabled());
    confirm();
    const done = await screen.findByTestId('reserva-confirmada');
    expect(within(done).getByTestId('confirmacion-numero')).toHaveTextContent('RES-WEB-000002');
    expect(done).toHaveTextContent('Paso el sábado');
    expect(store.load()).toEqual([]);
  });

  it('red caída: el reintento viaja con la MISMA llave; si cambia algo, con OTRA', async () => {
    const sources = mockSources();
    const { gateway, requests } = recordingGateway(sources.gateway, (_request, call) =>
      call <= 2 ? Promise.reject(new StorefrontError({ kind: 'network', status: 0, detail: 'sin red' })) : null,
    );
    await openCheckout({ items: [{ sku: GAME.sku, quantity: 1 }], sources: { ...sources, gateway } });
    await fillGuest();
    confirm();
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('No pudimos conectarnos con la tienda');
    expect(screen.getByLabelText(/^Nombre y apellido/)).toHaveValue('Valentina Aguirre');

    fireEvent.click(within(alert).getByRole('button', { name: 'Reintentar' }));
    await waitFor(() => expect(requests).toHaveLength(2));
    await screen.findByText('No pudimos conectarnos con la tienda');
    expect(requests[1].idempotencyKey).toBe(requests[0].idempotencyKey);

    // Cambió algo (las notas): otra llave.
    fireEvent.change(screen.getByLabelText(/^Notas para la tienda/), { target: { value: 'Paso mañana' } });
    confirm();
    await screen.findByTestId('reserva-confirmada');
    expect(requests).toHaveLength(3);
    expect(requests[2].idempotencyKey).not.toBe(requests[0].idempotencyKey);
    expect(isUuid(requests[0].idempotencyKey)).toBe(true);
  });

  it('400 marca el campo que rechazó el servidor y 429 pide esperar, sin perder lo escrito', async () => {
    const sources = mockSources();
    const { gateway } = recordingGateway(sources.gateway, (_request, call) => {
      if (call === 1) return Promise.reject(new StorefrontError({ kind: 'rate_limited', status: 429, detail: 'Límite' }));
      if (call === 2) {
        return Promise.reject(
          new StorefrontError({ kind: 'validation', status: 400, detail: 'Datos no válidos', errors: ['Indique un teléfono o WhatsApp para confirmar la reserva.'] }),
        );
      }
      return null;
    });
    await openCheckout({ items: [{ sku: GAME.sku, quantity: 1 }], sources: { ...sources, gateway } });
    await fillGuest();
    confirm();
    expect(await screen.findByText('Hiciste demasiados intentos seguidos')).toBeInTheDocument();
    expect(screen.getByLabelText(/^Teléfono o WhatsApp/)).toHaveValue('71234567');

    confirm();
    expect(await screen.findByText('Indique un teléfono o WhatsApp para confirmar la reserva.')).toBeInTheDocument();
    const phone = screen.getByLabelText(/^Teléfono o WhatsApp/);
    expect(phone).toHaveAttribute('aria-invalid', 'true');
    await waitFor(() => expect(phone).toHaveFocus());
    // Al cambiar el campo, el error del servidor se va.
    fireEvent.change(phone, { target: { value: '71234568' } });
    expect(screen.queryByText('Indique un teléfono o WhatsApp para confirmar la reserva.')).not.toBeInTheDocument();
  });
});

describe('cliente con sesión', () => {
  it('usa los datos de su cuenta (solo lectura), reserva por RPC y ve la reserva en «Mis reservas»', async () => {
    const sources = mockSources();
    const shared = sources.gateway as InMemoryReservationGateway;
    const web = await signedInWeb('customer', { stock: shared });
    const publicCreate = vi.spyOn(sources.gateway, 'create');
    const rpcCall = vi.spyOn(web.backend.rpc, 'call');
    const { store, app } = await openCheckout({ items: [{ sku: CPU.sku, quantity: 2 }], sources, web: web.services });

    const data = await screen.findByTestId('reserva-datos-cuenta');
    expect(data).toHaveTextContent('Valentina Aguirre');
    expect(data).toHaveTextContent('cliente@techzone.example');
    expect(within(data).getByRole('link', { name: 'Cambiar mis datos' })).toHaveAttribute('href', '/mi-cuenta/datos');
    expect(screen.queryByLabelText(/^Nombre y apellido/)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/^Teléfono o WhatsApp/)).not.toBeInTheDocument();
    expect(screen.queryByTestId('reserva-invitacion')).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/^¿Cuándo pasás a recogerlo\?/), { target: { value: '1' } });
    fireEvent.change(screen.getByLabelText(/^Notas para la tienda/), { target: { value: 'Paso\nmañana' } });
    confirm();

    const done = await screen.findByTestId('reserva-confirmada');
    expect(publicCreate).not.toHaveBeenCalled();
    const call = rpcCall.mock.calls.find(([operation]) => operation === 'CreateMyReservationCommand');
    expect(call?.[1]).toEqual({ lines: [{ sku: CPU.sku, quantity: 2 }], kind: 'cart', holdDays: 1, notes: 'Paso mañana' });
    expect(isUuid(call?.[2]?.requestId ?? '')).toBe(true);
    expect(done).toHaveTextContent('Te enviamos un correo a cliente@techzone.example');
    expect(store.load()).toEqual([]);
    // Descontó el mismo stock que ve la tienda.
    expect(shared.product(CPU.sku)).toMatchObject({ reserved: 2 });

    fireEvent.click(within(done).getByRole('link', { name: 'Mi reserva' }));
    await waitFor(() => expect(app.location()).toBe('/mi-cuenta/reservas'));
    const [first] = await screen.findAllByTestId('reserva');
    expect(within(first).getByTestId('tipo-reserva')).toHaveTextContent('Compra');
    expect(within(first).getByTestId('reserva-horas')).toHaveTextContent('Se guarda 24 h');
  });

  it('por RPC, el faltante de stock también se marca y se ajusta', async () => {
    const sources = mockSources();
    const shared = sources.gateway as InMemoryReservationGateway;
    const web = await signedInWeb('customer', { stock: shared });
    const { store } = await openCheckout({ items: [{ sku: CONSOLE.sku, quantity: 2 }], sources, web: web.services });
    await screen.findByTestId('reserva-datos-cuenta');
    await act(async () => {
      await shared.create({ lines: [{ sku: CONSOLE.sku, quantity: 2 }], contact: { name: 'Otra persona', phone: '70000001' }, idempotencyKey: 'otra', kind: 'cart' });
    });
    confirm();
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('No alcanzó el stock para reservar todo');
    expect(within(lineOf(CONSOLE.sku)).getByTestId('reserva-linea-aviso')).toHaveTextContent('Pediste 2; ya no queda ninguno disponible.');
    fireEvent.click(within(alert).getByRole('button', { name: 'Ajustar a lo disponible' }));
    expect(store.load()).toEqual([]);
  });
});
