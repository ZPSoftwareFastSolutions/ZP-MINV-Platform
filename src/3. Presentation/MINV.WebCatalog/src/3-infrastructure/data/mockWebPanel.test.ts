// V7 · W3b · servidor en memoria del modo mock para el panel: un usuario de muestra por rol con la matriz de permisos y
// sus sucursales, las reglas de SelectBranchCommand, la actividad (`GetActivityQuery`: muestra de los últimos días más lo
// que pasa en la pestaña, sin contraseñas) y `ResetUserPasswordCommand` (contraseña temporal y desbloqueo).

import { describe, expect, it } from 'vitest';
import { MOCK_CATALOG } from './mockCatalog';
import { DEMO_USERS, InMemoryWebBackend, ROLE_PERMISSIONS, ROLE_SAMPLE_USERS, type StaffRole } from './mockWeb';

const NOW = new Date('2026-09-29T16:00:00Z');
const CM = '5d0c1f6e-0a51-4d0e-9d11-000000000001';
const CB = '5d0c1f6e-0a51-4d0e-9d11-000000000002';
const SC = '5d0c1f6e-0a51-4d0e-9d11-000000000003';

function backend(now: () => Date = () => NOW): InMemoryWebBackend {
  return new InMemoryWebBackend({ products: MOCK_CATALOG.products, now, users: [...DEMO_USERS, ...ROLE_SAMPLE_USERS] });
}

function emailOf(role: StaffRole): string {
  return role === 'ADMIN' ? DEMO_USERS[0].email : ROLE_SAMPLE_USERS.find((user) => user.role === role)!.email;
}

async function signedIn(role: StaffRole, web = backend()) {
  await web.session.login({ email: emailOf(role), password: 'Demo1234' });
  return web;
}

describe('usuarios de muestra por rol', () => {
  it('hay uno por rol (además del administrador), con la matriz de permisos', async () => {
    expect(ROLE_SAMPLE_USERS.map((user) => user.role)).toEqual(['BODEGA', 'VENTAS', 'CAJERO', 'GERENCIA', 'CONSULTA']);
    for (const role of Object.keys(ROLE_PERMISSIONS) as StaffRole[]) {
      const web = backend();
      const session = await web.session.login({ email: emailOf(role), password: 'Demo1234' });
      expect(session).toMatchObject({ kind: 'staff', roles: [role] });
      expect([...session.permissions].sort()).toEqual([...ROLE_PERMISSIONS[role]].sort());
    }
    expect(ROLE_PERMISSIONS.ADMIN).toContain('iam.audit.view');
    expect(ROLE_PERMISSIONS.ADMIN.some((code) => code.startsWith('account.'))).toBe(false);
  });

  it('cada rol ve sus sucursales: la gerencia global todas (sin activa: vista consolidada), el resto las asignadas', async () => {
    const access = async (role: StaffRole) => (await (await signedIn(role)).session.current())!.access;
    expect(await access('ADMIN')).toMatchObject({ allBranches: true, activeBranchId: CM });
    expect(await access('GERENCIA')).toMatchObject({ allBranches: true, activeBranchId: null });
    expect((await access('GERENCIA')).branches.map((branch) => branch.code)).toEqual(['CM', 'CB', 'SC']);
    expect(await access('BODEGA')).toMatchObject({ allBranches: false, activeBranchId: CM, branches: [{ code: 'CM' }] });
    expect((await access('VENTAS')).branches.map((branch) => branch.code)).toEqual(['CM', 'CB']);
    expect(await access('CAJERO')).toMatchObject({ activeBranchId: CB });
    expect(await access('CONSULTA')).toMatchObject({ activeBranchId: SC });
  });

  it('SelectBranchCommand: una de sus sucursales; «todas» solo la gerencia global (como el servidor)', async () => {
    const ventas = await signedIn('VENTAS');
    await expect(ventas.rpc.send('SelectBranchCommand', { sessionId: 'x', branchId: CB })).resolves.toMatchObject({ activeBranchId: CB });
    await expect(ventas.rpc.send('SelectBranchCommand', { sessionId: 'x', branchId: SC })).rejects.toMatchObject({ kind: 'access_denied', message: 'La sucursal elegida no está entre las suyas.' });
    await expect(ventas.rpc.send('SelectBranchCommand', { sessionId: 'x', branchId: null })).rejects.toMatchObject({ kind: 'access_denied' });
    const gerencia = await signedIn('GERENCIA');
    await expect(gerencia.rpc.send('SelectBranchCommand', { sessionId: 'x', branchId: SC })).resolves.toMatchObject({ activeBranchId: SC });
    await expect(gerencia.rpc.send('SelectBranchCommand', { sessionId: 'x', branchId: null })).resolves.toMatchObject({ activeBranchId: null });
  });
});

describe('actividad en memoria', () => {
  it('GetActivityQuery exige `iam.audit.view`', async () => {
    const ventas = await signedIn('VENTAS');
    await expect(ventas.rpc.send('GetActivityQuery', { take: 10 })).rejects.toMatchObject({ kind: 'access_denied', message: expect.stringContaining('iam.audit.view') });
  });

  it('devuelve lo más reciente primero, acota `take` entre 1 y 5000 y trae actividad de hoy y de días anteriores', async () => {
    const web = await signedIn('ADMIN');
    const all = await web.rpc.send('GetActivityQuery', { take: 5000 });
    expect(all.length).toBeGreaterThan(80);
    expect(all.length).toBeLessThanOrEqual(5000);
    expect(all.map((row) => row.occurredAt)).toEqual([...all.map((row) => row.occurredAt)].sort().reverse());
    expect((await web.rpc.send('GetActivityQuery', { take: 3 })).length).toBe(3);
    expect((await web.rpc.send('GetActivityQuery', { take: 0 })).length).toBe(1);
    expect((await web.rpc.send('GetActivityQuery', {})).length).toBe(Math.min(200, all.length));
    expect(all.filter((row) => row.occurredAt.startsWith('2026-09-29')).length).toBeGreaterThanOrEqual(10);
    expect(new Set(all.map((row) => row.outcome))).toEqual(new Set(['Succeeded', 'Rejected', 'Failed']));
    expect(all.some((row) => row.userEmail === null && row.userName === null)).toBe(true);
    // Todo lo de muestra es de usuarios de muestra que existen (o del sistema).
    const known = new Set([...DEMO_USERS, ...ROLE_SAMPLE_USERS].map((user) => user.email));
    expect(all.every((row) => row.userEmail === null || known.has(row.userEmail))).toBe(true);
  });

  it('registra los ingresos (también los rechazados, con el correo intentado) y los comandos, sin contraseñas', async () => {
    const web = backend();
    await web.session.login({ email: 'nadie@techzone.example', password: 'Clave1234' }).catch(() => undefined);
    await web.session.login({ email: emailOf('BODEGA'), password: 'mala123' }).catch(() => undefined);
    await web.session.login({ email: emailOf('ADMIN'), password: 'Demo1234' });
    await web.rpc.send('SelectBranchCommand', { sessionId: 'x', branchId: CB });
    const latest = await web.rpc.send('GetActivityQuery', { take: 4 });
    expect(latest.map((row) => [row.action, row.outcome, row.userEmail])).toEqual([
      ['SelectBranch', 'Succeeded', 'admin@techzone.example'],
      ['Login', 'Succeeded', 'admin@techzone.example'],
      ['Login', 'Rejected', 'bodega@techzone.example'],
      ['Login', 'Rejected', 'nadie@techzone.example'],
    ]);
    expect(JSON.parse(latest[2].details!)).toEqual({ request: { Email: 'bodega@techzone.example' }, result: null, error: 'Correo o contraseña incorrectos.' });
    expect(latest.some((row) => row.details?.includes('mala123') || row.details?.includes('Clave1234'))).toBe(false);
  });
});

describe('contraseña temporal (ResetUserPasswordCommand)', () => {
  it('exige `iam.users.manage`, valida la contraseña y el usuario', async () => {
    const gerencia = await signedIn('GERENCIA');
    await expect(gerencia.rpc.send('ResetUserPasswordCommand', { email: emailOf('BODEGA'), newPassword: 'Temporal2026', mustChange: true })).rejects.toMatchObject({
      kind: 'access_denied',
      message: expect.stringContaining('iam.users.manage'),
    });
    const admin = await signedIn('ADMIN');
    await expect(admin.rpc.send('ResetUserPasswordCommand', { email: emailOf('BODEGA'), newPassword: 'corta', mustChange: true })).rejects.toMatchObject({ kind: 'validation' });
    await expect(admin.rpc.send('ResetUserPasswordCommand', { email: 'nadie@techzone.example', newPassword: 'Temporal2026', mustChange: true })).rejects.toMatchObject({
      kind: 'not_found',
    });
  });

  it('asigna la temporal, desbloquea la cuenta, pide cambiarla y queda en la actividad sin la contraseña', async () => {
    let now = NOW;
    const web = backend(() => now);
    for (let attempt = 1; attempt <= 5; attempt += 1) await web.session.login({ email: emailOf('BODEGA'), password: 'mala123' }).catch(() => undefined);
    await expect(web.session.login({ email: emailOf('BODEGA'), password: 'Demo1234' })).rejects.toMatchObject({ message: expect.stringContaining('Cuenta bloqueada') });

    await web.session.login({ email: emailOf('ADMIN'), password: 'Demo1234' });
    now = new Date(NOW.getTime() + 60_000);
    await expect(web.rpc.send('ResetUserPasswordCommand', { email: 'BODEGA@techzone.example', newPassword: 'Temporal2026', mustChange: true })).resolves.toBe(true);
    const [reset] = await web.rpc.send('GetActivityQuery', { take: 1 });
    expect(reset).toMatchObject({ action: 'ResetUserPassword', outcome: 'Succeeded', userEmail: 'admin@techzone.example' });
    expect(JSON.parse(reset.details!)).toEqual({ request: { Email: 'bodega@techzone.example', MustChange: true }, result: null, error: null });

    await web.session.logout();
    const session = await web.session.login({ email: emailOf('BODEGA'), password: 'Temporal2026' });
    expect(session.mustChangePassword).toBe(true);
  });
});
