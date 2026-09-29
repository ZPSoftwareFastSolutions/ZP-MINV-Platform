// V7 · W3b: el catálogo de la tienda ya NO bloquea el sitio. Antes, `CatalogProvider` envolvía al enrutador y no dibujaba
// nada hasta cargar el catálogo: con la tienda caída tampoco se podía ingresar ni abrir el panel. Ahora solo las páginas
// de la tienda lo esperan (con el error y «Reintentar» dentro de la tienda) y la sesión, «Mi cuenta» y el panel funcionan
// igual; «Mi armado» y el carrito siguen arriba del enrutador y no se pierden al navegar. Todo en memoria (sin red).

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { beforeAll, describe, expect, it } from 'vitest';
import type { CatalogSnapshot } from '@/1-domain/storefront/types';
import type { ICatalogSource } from '@/1-domain/ports/ICatalogSource';
import { MOCK_CATALOG } from '@/3-infrastructure/data/mockCatalog';
import { demoUser, failingSources, mockCart, mockSources, preloadPanel, renderRoutes, signedInWeb } from '@/test-utils';

const STAFF = demoUser('staff');

beforeAll(preloadPanel, 30_000);

async function signIn(email: string, password: string) {
  fireEvent.change(await screen.findByLabelText(/^Correo/), { target: { value: email } });
  fireEvent.change(screen.getByLabelText(/^Contraseña/), { target: { value: password } });
  fireEvent.click(screen.getByRole('button', { name: 'Ingresar' }));
}

describe('con la tienda caída', () => {
  it('/ingresar funciona (con la cabecera liviana) y el personal llega al panel', async () => {
    const app = await renderRoutes({ sources: failingSources(), route: '/ingresar', waitForCatalog: false });
    expect(await screen.findByRole('heading', { level: 1, name: 'Ingresar' })).toBeInTheDocument();
    expect(screen.getByTestId('cabecera-liviana')).toBeInTheDocument();
    // La cabecera completa de la tienda (con su menú y categorías) necesita el catálogo: no está.
    expect(screen.queryByRole('button', { name: 'Abrir menú' })).not.toBeInTheDocument();

    await signIn(STAFF.email, STAFF.password);
    expect(await screen.findByTestId('panel-esqueleto')).toBeInTheDocument();
    expect(await screen.findByRole('heading', { level: 1, name: `Hola, ${STAFF.name}` }, { timeout: 5000 })).toBeInTheDocument();
    expect(app.location()).toBe('/panel');
  });

  it('/panel funciona con la sesión vigente', async () => {
    const web = await signedInWeb('staff');
    await renderRoutes({ web: web.services, sources: failingSources(), route: '/panel/actividad', waitForCatalog: false });
    expect(await screen.findByTestId('panel-esqueleto')).toBeInTheDocument();
    expect(await screen.findByRole('heading', { level: 1, name: 'Actividad' }, { timeout: 5000 })).toBeInTheDocument();
  });

  it('las páginas de la tienda muestran el error con «Reintentar» dentro de la tienda, con «Ingresar» a mano', async () => {
    await renderRoutes({ sources: failingSources(), route: '/', waitForCatalog: false });
    const error = await screen.findByTestId('catalogo-error');
    expect(error).toHaveTextContent('No pudimos cargar el catálogo');
    expect(within(error).getByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
    expect(within(screen.getByTestId('cabecera-liviana')).getByRole('link', { name: 'Ingresar' })).toHaveAttribute('href', '/ingresar');
  });

  it('«Mi cuenta» del cliente tampoco depende del catálogo', async () => {
    const web = await signedInWeb('customer');
    await renderRoutes({ web: web.services, sources: failingSources(), route: '/mi-cuenta', waitForCatalog: false });
    expect(await screen.findAllByTestId('reserva', undefined, { timeout: 5000 })).toHaveLength(3);
  });
});

describe('mientras llega el catálogo', () => {
  it('el formulario de ingreso no se reinicia cuando aparece la cabecera completa', async () => {
    const base = mockSources();
    let release: () => void = () => undefined;
    const source: ICatalogSource = {
      load: () =>
        new Promise<CatalogSnapshot>((resolve, reject) => {
          release = () => void base.source.load().then(resolve, reject);
        }),
      product: (slug) => base.source.product(slug),
      presets: () => base.source.presets(),
    };
    await renderRoutes({ sources: { ...base, source }, route: '/ingresar', waitForCatalog: false });
    const email = await screen.findByLabelText(/^Correo/);
    fireEvent.change(email, { target: { value: 'persona@techzone.example' } });
    expect(screen.getByTestId('cabecera-liviana')).toBeInTheDocument();

    await act(async () => release());
    expect(await screen.findByRole('button', { name: 'Abrir menú' })).toBeInTheDocument();
    expect(screen.queryByTestId('cabecera-liviana')).not.toBeInTheDocument();
    expect(screen.getByLabelText(/^Correo/)).toBe(email);
    expect(email).toHaveValue('persona@techzone.example');
  });
});

describe('la tienda con el catálogo', () => {
  it('«Mi armado» y el carrito se conservan al navegar por la tienda y al pasar por las pantallas de la sesión', async () => {
    const cpu = MOCK_CATALOG.products.find((product) => product.sku === 'CPU-AMD-5600')!;
    const cart = mockCart([{ sku: 'JUE-NS2-DKBANANZA', quantity: 2 }]);
    const app = await renderRoutes({ cart: cart.cart, route: `/producto/${cpu.slug}` });
    await waitFor(() => expect(screen.getByTestId('disponibilidad-ficha')).toHaveAttribute('data-fresh', 'true'));
    fireEvent.click(screen.getByRole('button', { name: 'Agregar al armado' }));
    expect(await screen.findByRole('button', { name: 'Mi armado, 1 pieza' })).toBeInTheDocument();

    for (const route of ['/catalogo', '/ingresar', '/arma-tu-pc']) {
      await act(async () => {
        await app.router.navigate(route);
      });
      expect(await screen.findByRole('button', { name: 'Mi armado, 1 pieza' })).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'Carrito, 2 productos' })).toBeInTheDocument();
    }
    expect(cart.cart.current().items).toEqual([{ sku: 'JUE-NS2-DKBANANZA', quantity: 2 }]);
  });
});
