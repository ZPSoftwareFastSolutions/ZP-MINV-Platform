// Acceso de la cabecera: «Ingresar» sin sesión; con sesión, el nombre con su menú (personal: «Ir al panel»; cliente:
// «Mi cuenta» y «Mis reservas»; ambos: «Cambiar contraseña» y «Cerrar sesión»), manejable con el teclado.

import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { demoUser, renderRoutes, renderWithApp, signedInWeb } from '@/test-utils';
import { Header } from './Header';
import { MobileMenu } from './MobileMenu';

const CUSTOMER = demoUser('customer');
const STAFF = demoUser('staff');

async function openMenu(name: string) {
  const button = await screen.findByRole('button', { name: `Cuenta de ${name}` });
  fireEvent.click(button);
  return { button, menu: screen.getByRole('menu') };
}

describe('cabecera · acceso', () => {
  it('sin sesión muestra el botón «Ingresar» que lleva a /ingresar', async () => {
    await renderWithApp(<Header />);
    const link = await screen.findByRole('link', { name: 'Ingresar' });
    expect(link).toHaveAttribute('href', '/ingresar');
    expect(screen.queryByRole('button', { name: /^Cuenta de/ })).not.toBeInTheDocument();
    // Visible también en móvil: no se oculta en ningún ancho.
    expect(link.className).not.toMatch(/(^|\s)(hidden|max-sm:hidden|max-md:hidden|max-lg:hidden|sm:hidden|md:hidden|lg:hidden)(\s|$)/);
  });

  it('el cliente ve su nombre y un menú con «Mi cuenta», «Mis reservas», «Cambiar contraseña» y «Cerrar sesión»', async () => {
    const web = await signedInWeb('customer');
    await renderWithApp(<Header />, { web: web.services });
    expect(await screen.findByTestId('usuario-nombre')).toHaveTextContent(CUSTOMER.name);
    expect(screen.queryByRole('link', { name: 'Ingresar' })).not.toBeInTheDocument();

    const { button, menu } = await openMenu(CUSTOMER.name);
    expect(button).toHaveAttribute('aria-haspopup', 'menu');
    expect(button).toHaveAttribute('aria-expanded', 'true');
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['Mi cuenta', 'Mis reservas', 'Cambiar contraseña', 'Cerrar sesión']);
    expect(within(menu).getByRole('menuitem', { name: 'Mi cuenta' })).toHaveAttribute('href', '/mi-cuenta/datos');
    expect(within(menu).getByRole('menuitem', { name: 'Mis reservas' })).toHaveAttribute('href', '/mi-cuenta/reservas');
    expect(within(menu).getByRole('menuitem', { name: 'Cambiar contraseña' })).toHaveAttribute('href', '/mi-cuenta/contrasena');
    expect(menu).toHaveTextContent(CUSTOMER.email);
    expect(within(menu).queryByRole('menuitem', { name: 'Ir al panel' })).not.toBeInTheDocument();
  });

  it('el personal ve «Ir al panel», «Cambiar contraseña» y «Cerrar sesión»', async () => {
    const web = await signedInWeb('staff');
    await renderWithApp(<Header />, { web: web.services });
    const { menu } = await openMenu(STAFF.name);
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['Ir al panel', 'Cambiar contraseña', 'Cerrar sesión']);
    expect(within(menu).getByRole('menuitem', { name: 'Ir al panel' })).toHaveAttribute('href', '/panel');
    expect(within(menu).getByRole('menuitem', { name: 'Cambiar contraseña' })).toHaveAttribute('href', '/cambiar-contrasena');
  });

  it('se maneja con el teclado: flechas, Inicio y Fin, y Escape devuelve el foco al botón', async () => {
    const web = await signedInWeb('customer');
    await renderWithApp(<Header />, { web: web.services });
    const { button, menu } = await openMenu(CUSTOMER.name);
    const items = within(menu).getAllByRole('menuitem');
    await waitFor(() => expect(items[0]).toHaveFocus());

    fireEvent.keyDown(menu, { key: 'ArrowDown' });
    expect(items[1]).toHaveFocus();
    fireEvent.keyDown(menu, { key: 'End' });
    expect(items[3]).toHaveFocus();
    fireEvent.keyDown(menu, { key: 'ArrowDown' });
    expect(items[0]).toHaveFocus();
    fireEvent.keyDown(menu, { key: 'ArrowUp' });
    expect(items[3]).toHaveFocus();
    fireEvent.keyDown(menu, { key: 'Home' });
    expect(items[0]).toHaveFocus();

    fireEvent.keyDown(menu, { key: 'Escape' });
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(button).toHaveFocus();
    expect(button).toHaveAttribute('aria-expanded', 'false');

    // La flecha hacia abajo sobre el botón abre el menú; un clic afuera lo cierra.
    fireEvent.keyDown(button, { key: 'ArrowDown' });
    expect(await screen.findByRole('menu')).toBeInTheDocument();
    fireEvent.pointerDown(document.body);
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
  });

  it('«Cerrar sesión» cierra la sesión y vuelve a mostrar «Ingresar»', async () => {
    const web = await signedInWeb('customer');
    await renderWithApp(<Header />, { web: web.services });
    const { menu } = await openMenu(CUSTOMER.name);
    fireEvent.click(within(menu).getByRole('menuitem', { name: 'Cerrar sesión' }));
    expect(await screen.findByRole('link', { name: 'Ingresar' })).toBeInTheDocument();
    expect(await screen.findByText('Sesión cerrada')).toBeInTheDocument();
    expect(await web.backend.session.current()).toBeNull();
  });

  it('desde «Mi cuenta», cerrar sesión lleva a la tienda', async () => {
    const web = await signedInWeb('customer');
    const app = await renderRoutes({ web: web.services, route: '/mi-cuenta/datos' });
    const { menu } = await openMenu(CUSTOMER.name);
    fireEvent.click(within(menu).getByRole('menuitem', { name: 'Cerrar sesión' }));
    await waitFor(() => expect(app.location()).toBe('/'));
    expect(await screen.findByRole('link', { name: 'Ingresar' })).toBeInTheDocument();
  });

  it('al elegir una opción navega y el menú se cierra', async () => {
    const web = await signedInWeb('customer');
    const app = await renderRoutes({ web: web.services, route: '/' });
    const { menu } = await openMenu(CUSTOMER.name);
    fireEvent.click(within(menu).getByRole('menuitem', { name: 'Mis reservas' }));
    await waitFor(() => expect(app.location()).toBe('/mi-cuenta/reservas'));
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(await screen.findAllByTestId('reserva')).toHaveLength(3);
  });
});

describe('menú móvil · acceso', () => {
  it('sin sesión ofrece «Ingresar» y «Crear cuenta»', async () => {
    await renderWithApp(<MobileMenu open onClose={() => undefined} />);
    const dialog = await screen.findByRole('dialog', { name: 'Menú' });
    expect(within(dialog).getByRole('link', { name: 'Ingresar' })).toHaveAttribute('href', '/ingresar');
    expect(within(dialog).getByRole('link', { name: 'Crear cuenta' })).toHaveAttribute('href', '/registrarse');
  });

  it('con sesión muestra el nombre y las opciones de la cuenta', async () => {
    const web = await signedInWeb('customer');
    await renderWithApp(<MobileMenu open onClose={() => undefined} />, { web: web.services });
    const account = await screen.findByRole('navigation', { name: 'Tu cuenta' });
    expect(account).toHaveTextContent(CUSTOMER.name);
    expect(within(account).getAllByRole('link').map((link) => link.textContent)).toEqual(['Mi cuenta', 'Mis reservas', 'Cambiar contraseña']);
    fireEvent.click(within(account).getByRole('button', { name: 'Cerrar sesión' }));
    await waitFor(async () => expect(await web.backend.session.current()).toBeNull());
  });
});
