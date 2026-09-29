// Sesión web y RPC en memoria (modo mock): se comportan como el servidor (mensaje único, bloqueo, registro siempre como
// cliente, una sesión de cliente solo ejecuta operaciones de cliente, idempotencia por requestId).

import { describe, expect, it } from 'vitest';
import { MOCK_CATALOG } from './mockCatalog';
import { DEMO_USERS, InMemoryWebBackend } from './mockWeb';

const STAFF = DEMO_USERS.find((user) => user.kind === 'staff')!;
const CUSTOMER = DEMO_USERS.find((user) => user.kind === 'customer')!;

function backend(now: () => Date = () => new Date('2026-09-28T12:00:00Z')): InMemoryWebBackend {
  return new InMemoryWebBackend({ products: MOCK_CATALOG.products, now });
}

describe('sesión en memoria', () => {
  it('trae dos usuarios de muestra: uno del personal y un cliente', async () => {
    expect(DEMO_USERS.map((user) => user.kind).sort()).toEqual(['customer', 'staff']);
    const web = backend();
    expect(await web.session.current()).toBeNull();

    const staff = await web.session.login({ email: STAFF.email, password: STAFF.password });
    expect(staff).toMatchObject({ kind: 'staff', roles: ['ADMIN'], mustChangePassword: false });
    expect(staff.permissions).toContain('sales.pos.operate');
    expect(staff.permissions).not.toContain('account.manage');
    expect(staff.access.allBranches).toBe(true);
    expect(staff.access.branches.map((branch) => branch.code)).toEqual(['CM', 'CB', 'SC']);
    expect((await web.session.current())?.email).toBe(STAFF.email);

    await web.session.logout();
    expect(await web.session.current()).toBeNull();

    const customer = await web.session.login({ email: CUSTOMER.email.toUpperCase(), password: CUSTOMER.password });
    expect(customer).toMatchObject({ kind: 'customer', roles: ['CLIENTE'], permissions: ['account.manage', 'account.reserve'] });
  });

  it('el mismo mensaje si el correo no existe o si la contraseña no coincide', async () => {
    const web = backend();
    const unknown = await web.session.login({ email: 'nadie@correo.example', password: 'Clave123' }).catch((error: unknown) => error);
    const wrong = await web.session.login({ email: CUSTOMER.email, password: 'Otra12345' }).catch((error: unknown) => error);
    expect(unknown).toMatchObject({ kind: 'authentication', status: 401, message: 'Correo o contraseña incorrectos.' });
    expect(wrong).toMatchObject({ kind: 'authentication', status: 401, message: 'Correo o contraseña incorrectos.' });
    expect(await web.session.current()).toBeNull();
  });

  it('bloquea la cuenta a los 5 intentos fallidos durante 15 minutos', async () => {
    let now = new Date('2026-09-28T12:00:00Z');
    const web = backend(() => now);
    for (let attempt = 1; attempt <= 4; attempt += 1) {
      await expect(web.session.login({ email: CUSTOMER.email, password: 'mala' })).rejects.toMatchObject({ message: 'Correo o contraseña incorrectos.' });
    }
    await expect(web.session.login({ email: CUSTOMER.email, password: 'mala' })).rejects.toMatchObject({ kind: 'authentication', message: expect.stringContaining('Cuenta bloqueada') });
    // Bloqueada: ni con la contraseña correcta.
    await expect(web.session.login({ email: CUSTOMER.email, password: CUSTOMER.password })).rejects.toMatchObject({ message: expect.stringContaining('Cuenta bloqueada') });
    now = new Date('2026-09-28T12:16:00Z');
    await expect(web.session.login({ email: CUSTOMER.email, password: CUSTOMER.password })).resolves.toMatchObject({ kind: 'customer' });
  });

  it('registrarse crea SIEMPRE una cuenta de cliente y deja la sesión iniciada', async () => {
    const web = backend();
    const intruder = { name: 'Nueva Persona', email: 'nueva@correo.example', phone: '71112233', password: 'Clave123', role: 'ADMIN', permissions: ['iam.users.manage'] };
    const session = await web.session.register(intruder);
    expect(session).toMatchObject({ kind: 'customer', roles: ['CLIENTE'], permissions: ['account.manage', 'account.reserve'], displayName: 'Nueva Persona' });
    expect((await web.session.current())?.email).toBe('nueva@correo.example');
    expect(await web.rpc.send('GetMyAccountQuery', {})).toMatchObject({ name: 'Nueva Persona', phone: '+591 71112233', documentType: null });
    expect(await web.rpc.send('GetMyReservationsQuery', {})).toEqual([]);
  });

  it('rechaza el correo repetido y los datos inválidos', async () => {
    const web = backend();
    await expect(web.session.register({ name: 'Otra', email: CUSTOMER.email, phone: '71112233', password: 'Clave123' })).rejects.toMatchObject({
      kind: 'domain',
      status: 422,
      code: 'account.email_taken',
    });
    await expect(web.session.register({ name: '', email: 'mal', phone: '1', password: 'debil' })).rejects.toMatchObject({ kind: 'validation', status: 400 });
    expect(await web.session.current()).toBeNull();
  });
});

describe('RPC en memoria', () => {
  it('sin sesión responde 401', async () => {
    await expect(backend().rpc.send('GetMyAccountQuery', {})).rejects.toMatchObject({ kind: 'authentication', status: 401 });
  });

  it('cuando la sesión vence, el siguiente pedido responde 401', async () => {
    const web = backend();
    await web.session.login({ email: CUSTOMER.email, password: CUSTOMER.password });
    await expect(web.rpc.send('GetMyAccountQuery', {})).resolves.toMatchObject({ email: CUSTOMER.email });
    web.expire();
    await expect(web.rpc.send('GetMyAccountQuery', {})).rejects.toMatchObject({ kind: 'authentication' });
  });

  it('una sesión de cliente no ejecuta operaciones del personal, y el personal no tiene los permisos de cuenta', async () => {
    const web = backend();
    await web.session.login({ email: CUSTOMER.email, password: CUSTOMER.password });
    await expect(web.rpc.send('SelectBranchCommand', { sessionId: 'x', branchId: null })).rejects.toMatchObject({ kind: 'access_denied', status: 403 });
    await web.session.login({ email: STAFF.email, password: STAFF.password });
    await expect(web.rpc.send('GetMyAccountQuery', {})).rejects.toMatchObject({ kind: 'access_denied', message: expect.stringContaining('account.manage') });
    const access = await web.rpc.send('SelectBranchCommand', { sessionId: 'x', branchId: '5d0c1f6e-0a51-4d0e-9d11-000000000002' });
    expect(access.activeBranchId).toBe('5d0c1f6e-0a51-4d0e-9d11-000000000002');
    expect((await web.session.current())?.access.activeBranchId).toBe('5d0c1f6e-0a51-4d0e-9d11-000000000002');
  });

  it('el cliente de muestra tiene reservas en distintos estados y puede liberar la activa', async () => {
    const web = backend();
    await web.session.login({ email: CUSTOMER.email, password: CUSTOMER.password });
    const reservations = await web.rpc.send('GetMyReservationsQuery', {});
    expect(reservations.map((item) => item.status).sort()).toEqual(['Expired', 'Reserved', 'Sold']);
    const active = reservations.find((item) => item.status === 'Reserved')!;
    expect(active.lines.length).toBeGreaterThan(0);
    expect(active.total).toBeGreaterThan(0);

    const cancelled = await web.rpc.send('CancelMyReservationCommand', { number: active.number });
    expect(cancelled).toMatchObject({ number: active.number, status: 'Cancelled', statusText: 'Cancelada' });
    await expect(web.rpc.send('CancelMyReservationCommand', { number: active.number })).rejects.toMatchObject({ kind: 'domain', code: 'pcbuild.state' });
    await expect(web.rpc.send('CancelMyReservationCommand', { number: 'RES-WEB-999999' })).rejects.toMatchObject({ kind: 'not_found' });
  });

  it('una reserva activa cuyo plazo pasó se muestra vencida', async () => {
    let now = new Date('2026-09-28T12:00:00Z');
    const web = backend(() => now);
    await web.session.login({ email: CUSTOMER.email, password: CUSTOMER.password });
    now = new Date('2026-10-05T12:00:00Z');
    const reservations = await web.rpc.send('GetMyReservationsQuery', {});
    expect(reservations.some((item) => item.status === 'Reserved')).toBe(false);
  });

  it('los comandos son idempotentes por requestId', async () => {
    const web = backend();
    await web.session.login({ email: CUSTOMER.email, password: CUSTOMER.password });
    const active = (await web.rpc.send('GetMyReservationsQuery', {})).find((item) => item.status === 'Reserved')!;
    const requestId = '4f6a0c2e-8d1b-4c3e-9a7f-5b2d1e0c9a8b';
    const first = await web.rpc.call('CancelMyReservationCommand', { number: active.number }, { requestId });
    const again = await web.rpc.call('CancelMyReservationCommand', { number: active.number }, { requestId });
    expect(first).toMatchObject({ replayed: false, requestId });
    expect(again).toMatchObject({ replayed: true, requestId });
    expect(again.result).toEqual(first.result);
    await expect(web.rpc.call('CancelMyReservationCommand', { number: 'OTRA' }, { requestId })).rejects.toMatchObject({ kind: 'idempotency', status: 422, requestId });
  });

  it('actualiza los datos de la cuenta con las reglas del documento', async () => {
    const web = backend();
    await web.session.login({ email: CUSTOMER.email, password: CUSTOMER.password });
    const saved = await web.rpc.send('UpdateMyAccountCommand', { name: ' Valentina A. ', phone: '7123 4567', documentType: 1, documentNumber: '1234567', complement: '1a' });
    expect(saved).toEqual({ name: 'Valentina A.', email: CUSTOMER.email, phone: '+591 71234567', documentType: 1, documentNumber: '1234567', complement: '1A' });
    expect((await web.session.current())?.displayName).toBe('Valentina A.');
    await expect(web.rpc.send('UpdateMyAccountCommand', { name: 'V', phone: '71234567', documentType: 5, documentNumber: 'ABC' })).rejects.toMatchObject({ kind: 'domain', code: 'buyer.doc_numeric' });
    await expect(web.rpc.send('UpdateMyAccountCommand', { name: 'V', phone: '71234567', documentType: 5, documentNumber: '123', complement: '1A' })).rejects.toMatchObject({ code: 'buyer.complement' });
    await expect(web.rpc.send('UpdateMyAccountCommand', { name: '', phone: '1' })).rejects.toMatchObject({ kind: 'validation' });
  });

  it('cambia la contraseña: la actual incorrecta responde 401 SIN cerrar la sesión', async () => {
    const web = backend();
    await web.session.login({ email: CUSTOMER.email, password: CUSTOMER.password });
    web.requirePasswordChange(CUSTOMER.email);
    expect((await web.session.current())?.mustChangePassword).toBe(true);

    await expect(web.rpc.send('ChangePasswordCommand', { currentPassword: 'mala', newPassword: 'Nueva4567' })).rejects.toMatchObject({
      kind: 'authentication',
      message: 'La contraseña actual no es correcta.',
    });
    expect(await web.session.current()).not.toBeNull();
    await expect(web.rpc.send('ChangePasswordCommand', { currentPassword: CUSTOMER.password, newPassword: 'debil' })).rejects.toMatchObject({ kind: 'validation' });

    await expect(web.rpc.send('ChangePasswordCommand', { currentPassword: CUSTOMER.password, newPassword: 'Nueva4567' })).resolves.toBe(true);
    expect((await web.session.current())?.mustChangePassword).toBe(false);
    await web.session.logout();
    await expect(web.session.login({ email: CUSTOMER.email, password: CUSTOMER.password })).rejects.toMatchObject({ kind: 'authentication' });
    await expect(web.session.login({ email: CUSTOMER.email, password: 'Nueva4567' })).resolves.toMatchObject({ kind: 'customer' });
  });

  it('reserva con los datos de la cuenta (carrito sin ranuras, plazo de 1 a 3 días)', async () => {
    const web = backend();
    await web.session.login({ email: CUSTOMER.email, password: CUSTOMER.password });
    const product = MOCK_CATALOG.products.find((item) => item.stock > 0)!;
    const reservation = await web.rpc.send('CreateMyReservationCommand', { lines: [{ sku: product.sku, quantity: 2 }], kind: 'cart', holdDays: 1, notes: ' Paso mañana ' });
    expect(reservation).toMatchObject({ status: 'Reserved', kind: 'cart', contactName: CUSTOMER.name, notes: 'Paso mañana', mailQueued: true });
    expect(reservation.number).toMatch(/^RES-WEB-\d{6}$/);
    expect(reservation.lines).toEqual([{ slot: null, sku: product.sku, name: product.name, quantity: 2, unitPrice: product.price, subtotal: Math.round(product.price * 2 * 100) / 100 }]);
    expect(new Date(reservation.reservedUntil).getTime() - new Date(reservation.createdAt).getTime()).toBe(24 * 3_600_000);
    expect((await web.rpc.send('GetMyReservationsQuery', {}))[0].number).toBe(reservation.number);

    await expect(web.rpc.send('CreateMyReservationCommand', { lines: [], kind: 'cart' })).rejects.toMatchObject({ kind: 'validation' });
    await expect(web.rpc.send('CreateMyReservationCommand', { lines: [{ sku: product.sku, quantity: 1 }], kind: 'cart', holdDays: 4 })).rejects.toMatchObject({ kind: 'validation' });
    await expect(web.rpc.send('CreateMyReservationCommand', { lines: [{ sku: 'NO-EXISTE', quantity: 1 }], kind: 'cart' })).rejects.toMatchObject({ code: 'product.inactive' });
  });
});
