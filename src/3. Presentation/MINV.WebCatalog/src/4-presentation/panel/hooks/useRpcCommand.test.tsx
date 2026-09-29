// useRpcCommand: estado de envío, aviso de éxito, `requestId` estable al reintentar lo mismo y nuevo tras un éxito o si
// cambia el contenido, aviso de error con el mensaje del servidor, permiso que falta en palabras, sin doble envío y sin
// aviso ante un 401. Permisos para mostrar u ocultar (usePermissions, PermissionGate, AccessDenied). RPC simulado.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import { permissionName } from '@/4-presentation/app/contract';
import type { RpcSendOptions } from '@/1-domain/ports/IRpcGateway';
import type { WebRpc } from '@/4-presentation/app/container';
import { renderPanel, signedInWeb, webWith } from '@/test-utils';
import { AccessDenied, PermissionGate } from '../kit/PermissionGate';
import { useRpcCommand, type RpcCommandOptions, type RpcCommandOutcome } from './useRpcCommand';
import { usePermissions } from './usePermissions';

type Answer = unknown | Error;

/** RPC simulado que responde en orden lo que la prueba prepara (un Error = falla). */
function scriptedRpc(answers: Answer[]) {
  const requestIds: string[] = [];
  const pending: { release: () => void }[] = [];
  let hold = false;
  const call = vi.fn(async (_operation: string, _payload: unknown, options: RpcSendOptions = {}) => {
    requestIds.push(options.requestId ?? '');
    if (hold) await new Promise<void>((release) => pending.push({ release }));
    const next = answers.shift();
    if (next instanceof Error) throw next;
    return { result: next, replayed: false, requestId: options.requestId ?? '' };
  });
  const rpc = { call, send: vi.fn() } as unknown as WebRpc;
  return {
    rpc,
    call,
    requestIds,
    holdNext() {
      hold = true;
    },
    releaseAll() {
      hold = false;
      for (const item of pending.splice(0)) item.release();
    },
  };
}

async function servicesWith(rpc: WebRpc) {
  const web = await signedInWeb('staff');
  return webWith({ session: web.backend.session, rpc });
}

const outcomes: RpcCommandOutcome<unknown>[] = [];

function CommandProbe({ options, operation = 'CancelMyReservationCommand' }: { options?: RpcCommandOptions<'CancelMyReservationCommand'>; operation?: 'CancelMyReservationCommand' | 'CreateMyReservationCommand' }) {
  const cancel = useRpcCommand('CancelMyReservationCommand', options);
  const create = useRpcCommand('CreateMyReservationCommand', { notifyError: true });
  const [number, setNumber] = useState('RES-1');
  const run = async () => {
    const outcome = operation === 'CancelMyReservationCommand' ? await cancel.run({ number }) : await create.run({ lines: [{ sku: 'SSD-1T', quantity: 1 }], kind: 'cart' });
    outcomes.push(outcome);
  };
  const command = operation === 'CancelMyReservationCommand' ? cancel : create;
  return (
    <div>
      <button type="button" onClick={() => void run()}>
        Enviar
      </button>
      <button type="button" onClick={() => setNumber('RES-2')}>
        Cambiar contenido
      </button>
      <p data-testid="enviando">{command.sending ? 'sí' : 'no'}</p>
      <p data-testid="error">{command.errorText ?? 'sin error'}</p>
      <p data-testid="id">{command.requestId ?? 'ninguno'}</p>
      <button type="button" onClick={command.reset}>
        Olvidar error
      </button>
    </div>
  );
}

const text = (id: string) => screen.getByTestId(id).textContent;
const network = () => new WebApiError({ kind: 'network', message: 'Sin respuesta.' });

describe('useRpcCommand', () => {
  it('envía, muestra «enviando» y avisa el éxito', async () => {
    const script = scriptedRpc([{ number: 'RES-1', status: 'Cancelled' }]);
    const onSuccess = vi.fn();
    outcomes.length = 0;
    await renderPanel(<CommandProbe options={{ success: (result) => `Reserva ${(result as { number: string }).number} liberada`, onSuccess }} />, {
      web: await servicesWith(script.rpc),
    });
    script.holdNext();
    fireEvent.click(screen.getByRole('button', { name: 'Enviar' }));
    await waitFor(() => expect(text('enviando')).toBe('sí'));
    expect(script.call).toHaveBeenCalledWith('CancelMyReservationCommand', { number: 'RES-1' }, { requestId: expect.any(String) });
    script.releaseAll();
    expect(await screen.findByText('Reserva RES-1 liberada')).toBeInTheDocument();
    expect(text('enviando')).toBe('no');
    expect(onSuccess).toHaveBeenCalledWith({ number: 'RES-1', status: 'Cancelled' }, { number: 'RES-1' });
    expect(outcomes).toEqual([{ ok: true, result: { number: 'RES-1', status: 'Cancelled' }, replayed: false }]);
  });

  it('el mismo contenido reintenta con el MISMO requestId; tras un éxito o con otro contenido, uno nuevo', async () => {
    const script = scriptedRpc([network(), true, true, true]);
    await renderPanel(<CommandProbe options={{ notifyError: false }} />, { web: await servicesWith(script.rpc) });
    /** Envía y espera a que termine. */
    const send = async (count: number) => {
      fireEvent.click(screen.getByRole('button', { name: 'Enviar' }));
      await waitFor(() => expect(script.requestIds).toHaveLength(count));
      await waitFor(() => expect(text('enviando')).toBe('no'));
    };

    await send(1);
    await waitFor(() => expect(text('error')).toMatch(/No se pudo conectar/));
    expect(text('id')).toBe(script.requestIds[0]);
    await send(2); // Reintentar lo mismo: mismo id.
    expect(script.requestIds[1]).toBe(script.requestIds[0]);
    await waitFor(() => expect(text('id')).toBe('ninguno'));

    await send(3); // Después de un éxito, el mismo contenido es OTRO intento.
    expect(script.requestIds[2]).not.toBe(script.requestIds[1]);

    fireEvent.click(screen.getByRole('button', { name: 'Cambiar contenido' }));
    await send(4);
    expect(new Set(script.requestIds.slice(1)).size).toBe(3);
  });

  it('también cambia el id si cambia el contenido después de una falla', async () => {
    const script = scriptedRpc([network(), true]);
    await renderPanel(<CommandProbe options={{ notifyError: false }} />, { web: await servicesWith(script.rpc) });
    fireEvent.click(screen.getByRole('button', { name: 'Enviar' }));
    await waitFor(() => expect(text('error')).not.toBe('sin error'));
    fireEvent.click(screen.getByRole('button', { name: 'Cambiar contenido' }));
    fireEvent.click(screen.getByRole('button', { name: 'Enviar' }));
    await waitFor(() => expect(script.requestIds).toHaveLength(2));
    expect(script.requestIds[1]).not.toBe(script.requestIds[0]);
  });

  it('avisa el error con el mensaje del servidor', async () => {
    const script = scriptedRpc([new WebApiError({ kind: 'domain', status: 422, code: 'pcbuild.state', message: 'La reserva RES-1 ya no está activa: no se puede liberar.' })]);
    await renderPanel(<CommandProbe options={{ errorTitle: 'No se pudo liberar' }} />, { web: await servicesWith(script.rpc) });
    fireEvent.click(screen.getByRole('button', { name: 'Enviar' }));
    expect(await screen.findByText('No se pudo liberar')).toBeInTheDocument();
    expect(screen.getAllByText('La reserva RES-1 ya no está activa: no se puede liberar.').length).toBeGreaterThan(0);
    fireEvent.click(screen.getByRole('button', { name: 'Olvidar error' }));
    expect(text('error')).toBe('sin error');
  });

  it('si el servidor rechaza por permisos, dice en palabras cuál falta (el que nombra el servidor)', async () => {
    const script = scriptedRpc([new WebApiError({ kind: 'access_denied', status: 403, message: 'Su rol no tiene el permiso sales.pcbuild.manage.' })]);
    await renderPanel(<CommandProbe />, { web: await servicesWith(script.rpc) });
    fireEvent.click(screen.getByRole('button', { name: 'Enviar' }));
    expect(await screen.findByText('No tiene permiso para esta operación')).toBeInTheDocument();
    expect(screen.getByText(`Falta el permiso «${permissionName('sales.pcbuild.manage')}». Pida al administrador que se lo asigne.`)).toBeInTheDocument();
    expect(text('error')).toContain(`«${permissionName('sales.pcbuild.manage')}»`);
  });

  it('si el servidor no lo nombra, usa los permisos que el contrato declara para la operación', async () => {
    const script = scriptedRpc([new WebApiError({ kind: 'access_denied', status: 403, message: 'Acceso denegado.' })]);
    await renderPanel(<CommandProbe operation="CreateMyReservationCommand" />, { web: await servicesWith(script.rpc) });
    fireEvent.click(screen.getByRole('button', { name: 'Enviar' }));
    expect(await screen.findByText(`Falta el permiso «${permissionName('account.reserve')}». Pida al administrador que se lo asigne.`)).toBeInTheDocument();
    expect(text('error')).toBe(
      `No tiene permiso para esta operación. Falta el permiso «${permissionName('account.reserve')}». Pida al administrador que se lo asigne.`,
    );
  });

  it('no envía dos veces por un doble clic', async () => {
    const script = scriptedRpc([true]);
    await renderPanel(<CommandProbe />, { web: await servicesWith(script.rpc) });
    script.holdNext();
    fireEvent.click(screen.getByRole('button', { name: 'Enviar' }));
    fireEvent.click(screen.getByRole('button', { name: 'Enviar' }));
    await waitFor(() => expect(text('enviando')).toBe('sí'));
    await act(async () => script.releaseAll());
    await waitFor(() => expect(text('enviando')).toBe('no'));
    expect(script.call).toHaveBeenCalledTimes(1);
  });

  it('un 401 (sesión vencida) no muestra aviso: la página manda a ingresar', async () => {
    const script = scriptedRpc([new WebApiError({ kind: 'authentication', status: 401, message: 'La sesión venció.' })]);
    const view = await renderPanel(<CommandProbe options={{ errorTitle: 'No se pudo liberar' }} />, { web: await servicesWith(script.rpc) });
    fireEvent.click(screen.getByRole('button', { name: 'Enviar' }));
    expect(await screen.findByTestId('ingresar')).toBeInTheDocument();
    expect(view.location()).toMatch(/^\/ingresar\?volver=/);
    expect(screen.queryByText('No se pudo liberar')).not.toBeInTheDocument();
  });
});

function PermissionsProbe() {
  const { can, canAny, canAll, canRun, missing } = usePermissions();
  return (
    <div>
      <p data-testid="permisos">
        {[can('sales.view'), can('account.manage'), canAny(['account.manage', 'sales.view']), canAll(['account.manage', 'sales.view']), canRun('SelectBranchCommand'), canRun('GetMyAccountQuery')].join(',')}
      </p>
      <p data-testid="faltan">{missing(['sales.view', 'account.manage']).join(',')}</p>
      <PermissionGate permission="sales.view">
        <p>Visible con sales.view</p>
      </PermissionGate>
      <PermissionGate permission="account.manage" fallback={<AccessDenied permissions={['account.manage']} />}>
        <p>Nunca para el personal de muestra</p>
      </PermissionGate>
      <PermissionGate permission={['account.manage', 'sales.view']} mode="any">
        <p>Visible con uno de los dos</p>
      </PermissionGate>
      <PermissionGate operation="GetMyAccountQuery">
        <p>Oculto por la operación</p>
      </PermissionGate>
    </div>
  );
}

describe('permisos para mostrar u ocultar', () => {
  it('usePermissions, PermissionGate y AccessDenied con la sesión del personal de muestra', async () => {
    await renderPanel(<PermissionsProbe />);
    // El personal de muestra tiene todos los permisos menos los de la cuenta de cliente (account.*).
    expect(text('permisos')).toBe('true,false,true,false,true,false');
    expect(text('faltan')).toBe('account.manage');
    expect(screen.getByText('Visible con sales.view')).toBeInTheDocument();
    expect(screen.queryByText('Nunca para el personal de muestra')).not.toBeInTheDocument();
    const denied = screen.getByRole('alert');
    expect(within(denied).getByText('No tiene permiso para ver esto')).toBeInTheDocument();
    expect(denied).toHaveTextContent(`Falta el permiso «${permissionName('account.manage')}».`);
    expect(screen.getByText('Visible con uno de los dos')).toBeInTheDocument();
    expect(screen.queryByText('Oculto por la operación')).not.toBeInTheDocument();
  });
});
