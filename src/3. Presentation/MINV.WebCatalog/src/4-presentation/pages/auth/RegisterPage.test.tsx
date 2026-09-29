// «Registrarse»: errores por campo, indicador de requisitos de la contraseña, contraseña débil, correo repetido con
// enlace a ingresar, y registro correcto (queda con sesión de cliente y va a su cuenta).

import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { ISessionGateway } from '@/1-domain/ports/ISessionGateway';
import { demoUser, mockWeb, renderRoutes, webWith } from '@/test-utils';

const CUSTOMER = demoUser('customer');

interface Values {
  name?: string;
  email?: string;
  phone?: string;
  password?: string;
  confirm?: string;
}

const VALID: Required<Values> = { name: 'Nueva Persona', email: 'nueva@correo.example', phone: '71112233', password: 'Clave1234', confirm: 'Clave1234' };

function fill(values: Values) {
  const set = (label: RegExp, value: string | undefined) => {
    if (value !== undefined) fireEvent.change(screen.getByLabelText(label), { target: { value } });
  };
  set(/^Nombre y apellido/, values.name);
  set(/^Correo/, values.email);
  set(/^Teléfono o WhatsApp/, values.phone);
  set(/^Contraseña/, values.password);
  set(/^Repetí la contraseña/, values.confirm);
}

async function openRegister(options: Parameters<typeof renderRoutes>[0] = {}) {
  const app = await renderRoutes({ route: '/registrarse', ...options });
  await screen.findByRole('heading', { level: 1, name: 'Crear cuenta' });
  return app;
}

const submit = () => fireEvent.click(screen.getByRole('button', { name: 'Crear cuenta' }));

describe('RegisterPage', () => {
  it('tiene los cinco campos con su etiqueta y el enlace a ingresar', async () => {
    await openRegister();
    expect(screen.getByLabelText(/^Nombre y apellido/)).toHaveAttribute('autocomplete', 'name');
    expect(screen.getByLabelText(/^Correo/)).toHaveAttribute('type', 'email');
    expect(screen.getByLabelText(/^Teléfono o WhatsApp/)).toHaveAttribute('type', 'tel');
    expect(screen.getByLabelText(/^Contraseña/)).toHaveAttribute('autocomplete', 'new-password');
    expect(screen.getByLabelText(/^Repetí la contraseña/)).toHaveAttribute('type', 'password');
    expect(screen.getByRole('link', { name: 'Ingresá' })).toHaveAttribute('href', '/ingresar');
  });

  it('marca cada campo con su error y no viaja al servidor', async () => {
    const register = vi.fn();
    const gateway: ISessionGateway = { login: vi.fn(), register, current: vi.fn().mockResolvedValue(null), logout: vi.fn() };
    await openRegister({ web: webWith({ session: gateway, rpc: { call: vi.fn(), send: vi.fn() } }) });
    submit();
    expect(await screen.findByText('Indicá tu nombre y apellido.')).toBeInTheDocument();
    expect(screen.getByText('Indicá tu correo.')).toBeInTheDocument();
    expect(screen.getByText('Indicá un teléfono o WhatsApp.')).toBeInTheDocument();
    expect(screen.getByText('Indicá tu contraseña.')).toBeInTheDocument();
    expect(screen.getByText('Repetí la contraseña.')).toBeInTheDocument();
    for (const label of [/^Nombre y apellido/, /^Correo/, /^Teléfono o WhatsApp/, /^Contraseña/, /^Repetí la contraseña/]) {
      expect(screen.getByLabelText(label)).toHaveAttribute('aria-invalid', 'true');
    }
    // El error queda enlazado al campo para los lectores de pantalla.
    const email = screen.getByLabelText(/^Correo/);
    const described = (email.getAttribute('aria-describedby') ?? '').split(' ').map((id) => document.getElementById(id)?.textContent);
    expect(described).toContain('Indicá tu correo.');

    fill({ name: 'Nueva Persona', email: 'mal', phone: '123' });
    expect(await screen.findByText('Revisá el correo: debe tener la forma nombre@dominio.')).toBeInTheDocument();
    expect(screen.getByText('El teléfono debe tener 7 u 8 dígitos (Bolivia), con o sin +591.')).toBeInTheDocument();
    expect(screen.queryByText('Indicá tu nombre y apellido.')).not.toBeInTheDocument();
    expect(register).not.toHaveBeenCalled();
  });

  it('el indicador marca cada requisito de la contraseña mientras se escribe', async () => {
    await openRegister();
    const list = screen.getByText('La contraseña debe tener').parentElement!;
    const state = () => within(list).getAllByRole('listitem').map((item) => item.getAttribute('data-met'));
    expect(state()).toEqual(['false', 'false', 'false']);
    fill({ password: 'abc' });
    expect(state()).toEqual(['false', 'true', 'false']);
    fill({ password: 'abc12345' });
    expect(state()).toEqual(['true', 'true', 'true']);
    expect(within(list).getAllByText(': cumplido')).toHaveLength(3);
    // El campo de la contraseña referencia la lista de requisitos.
    expect(screen.getByLabelText(/^Contraseña/).getAttribute('aria-describedby')).toContain(list.id);
  });

  it('rechaza una contraseña débil y una repetición distinta', async () => {
    const web = mockWeb();
    const app = await openRegister({ web: web.services });
    fill({ ...VALID, password: 'sololetras', confirm: 'sololetras' });
    submit();
    expect(await screen.findByText('La contraseña debe tener entre 8 y 128 caracteres y combinar letras y números.')).toBeInTheDocument();
    expect(screen.getByLabelText(/^Contraseña/)).toHaveAttribute('aria-invalid', 'true');

    fill({ password: 'corta1', confirm: 'corta1' });
    expect(screen.getByText('La contraseña debe tener entre 8 y 128 caracteres y combinar letras y números.')).toBeInTheDocument();

    fill({ password: 'Clave1234', confirm: 'Clave1235' });
    expect(await screen.findByText('Las contraseñas no coinciden.')).toBeInTheDocument();
    submit();
    expect(app.location()).toBe('/registrarse');
    expect(await web.backend.session.current()).toBeNull();
  });

  it('con un correo que ya tiene cuenta avisa y ofrece ingresar, sin salir de la pantalla', async () => {
    const web = mockWeb();
    const app = await openRegister({ web: web.services, route: '/registrarse?volver=%2Fcarrito' });
    fill({ ...VALID, email: CUSTOMER.email });
    submit();

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Ese correo ya tiene una cuenta');
    expect(within(alert).getByRole('link', { name: 'Ingresar con ese correo' })).toHaveAttribute('href', '/ingresar?volver=%2Fcarrito');
    expect(screen.getByLabelText(/^Correo/)).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByText('Este correo ya tiene una cuenta.')).toBeInTheDocument();
    expect(app.location()).toBe('/registrarse?volver=%2Fcarrito');
    expect(await web.backend.session.current()).toBeNull();

    // Al escribir otro correo el aviso se va.
    fill({ email: 'otra@correo.example' });
    await waitFor(() => expect(screen.queryByText('Ese correo ya tiene una cuenta')).not.toBeInTheDocument());
    expect(screen.getByLabelText(/^Correo/)).not.toHaveAttribute('aria-invalid');
  });

  it('registra una cuenta de cliente y la lleva a su cuenta con la sesión iniciada', async () => {
    const web = mockWeb();
    const app = await openRegister({ web: web.services });
    fill(VALID);
    submit();
    expect(await screen.findByRole('heading', { level: 1, name: 'Hola, Nueva Persona' })).toBeInTheDocument();
    expect(app.location()).toBe('/mi-cuenta');
    expect(await web.backend.session.current()).toMatchObject({ kind: 'customer', roles: ['CLIENTE'], email: 'nueva@correo.example' });
    // Una cuenta nueva todavía no tiene reservas.
    expect(await screen.findByText('Todavía no tenés reservas')).toBeInTheDocument();
  });

  it('envía solo nombre, correo, teléfono y contraseña: nunca un rol', async () => {
    const register = vi.fn().mockRejectedValue(new WebApiError({ kind: 'server', status: 500, message: 'El servidor no pudo completar la operación.' }));
    const gateway: ISessionGateway = { login: vi.fn(), register, current: vi.fn().mockResolvedValue(null), logout: vi.fn() };
    const app = await openRegister({ web: webWith({ session: gateway, rpc: { call: vi.fn(), send: vi.fn() } }) });
    fill({ ...VALID, name: '  Nueva Persona ', phone: '591 7111 2233' });
    submit();
    expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos crear la cuenta');
    expect(register).toHaveBeenCalledTimes(1);
    expect(register).toHaveBeenCalledWith({ name: 'Nueva Persona', email: 'nueva@correo.example', phone: '+591 71112233', password: 'Clave1234' });
    expect(app.location()).toBe('/registrarse');
  });

  it('muestra los errores de validación del servidor y el límite de registros', async () => {
    const register = vi
      .fn()
      .mockRejectedValueOnce(new WebApiError({ kind: 'validation', status: 400, message: 'Datos no válidos', errors: ['Indique un correo válido.', 'El teléfono no es de Bolivia.'] }))
      .mockRejectedValueOnce(new WebApiError({ kind: 'rate_limited', status: 429, message: 'Se superó el límite de solicitudes.' }));
    const gateway: ISessionGateway = { login: vi.fn(), register, current: vi.fn().mockResolvedValue(null), logout: vi.fn() };
    await openRegister({ web: webWith({ session: gateway, rpc: { call: vi.fn(), send: vi.fn() } }) });
    fill(VALID);
    submit();
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Datos no válidos');
    expect(within(alert).getAllByRole('listitem').map((item) => item.textContent)).toEqual(['Indique un correo válido.', 'El teléfono no es de Bolivia.']);

    submit();
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(/Demasiados intentos.*Esperá un minuto/));
  });
});
