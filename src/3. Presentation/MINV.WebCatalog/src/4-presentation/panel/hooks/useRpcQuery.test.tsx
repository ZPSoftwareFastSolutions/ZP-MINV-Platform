// useRpcQuery: carga, error y «Recargar», descarta respuestas viejas, cancela al desmontar, no repite por un objeto
// igual, `enabled`, datos anteriores mientras llega el filtro nuevo y nueva consulta al cambiar la sucursal activa.
// El RPC es un puerto simulado (sin red). El contrato provisional solo trae operaciones de la cuenta: cualquiera sirve
// para probar el mecanismo.

import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { useEffect, useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { RpcSendOptions } from '@/1-domain/ports/IRpcGateway';
import type { WebRpc } from '@/4-presentation/app/container';
import { useSession } from '@/4-presentation/hooks/useSession';
import { renderPanel, signedInWeb, webWith } from '@/test-utils';
import { Collapsible } from '../kit/Collapsible';
import { useRpcQuery, type RpcQueryOptions } from './useRpcQuery';

interface Deferred<T> {
  promise: Promise<T>;
  resolve(value: T): void;
  reject(error: unknown): void;
}

function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((ok, fail) => {
    resolve = ok;
    reject = fail;
  });
  return { promise, resolve, reject };
}

interface Call {
  operation: string;
  payload: unknown;
  options: RpcSendOptions;
  answer: Deferred<unknown>;
}

/** RPC simulado: cada pedido queda pendiente hasta que la prueba lo responde. */
function pendingRpc() {
  const calls: Call[] = [];
  const call = vi.fn((operation: string, payload: unknown, options: RpcSendOptions = {}) => {
    const answer = deferred<unknown>();
    calls.push({ operation, payload, options, answer });
    return answer.promise.then((result) => ({ result, replayed: false, requestId: options.requestId ?? 'id' }));
  });
  const rpc = { call, send: vi.fn(async (operation: string, payload: unknown, options?: RpcSendOptions) => (await call(operation, payload, options)).result) } as unknown as WebRpc;
  return { rpc, calls };
}

async function panelWith(rpc: WebRpc) {
  const web = await signedInWeb('staff');
  return { backend: web.backend, services: webWith({ session: web.backend.session, rpc }) };
}

interface ProbeProps {
  number: string;
  options?: RpcQueryOptions;
}

/** Cambia las props de la sonda sin sacarla de sus proveedores (lo guarda ProbeHost en un efecto). */
let changeProbe: (props: ProbeProps) => void = () => undefined;

function ProbeHost(initial: ProbeProps) {
  const [props, setProps] = useState(initial);
  useEffect(() => {
    changeProbe = setProps;
  }, []);
  return <Probe {...props} />;
}

/** Vuelve a dibujar la sonda con otras props (un objeto nuevo en cada llamada). */
function reprobe(props: ProbeProps) {
  act(() => changeProbe({ ...props }));
}

function Probe({ number, options }: ProbeProps) {
  const query = useRpcQuery('CancelMyReservationCommand', { number }, options);
  const { refresh } = useSession();
  return (
    <div>
      <p data-testid="estado">
        {query.status}|{query.loading ? 'cargando' : '-'}|{query.fetching ? 'pidiendo' : '-'}
      </p>
      <p data-testid="datos">{query.data ? JSON.stringify(query.data) : 'sin datos'}</p>
      <p data-testid="error">{query.error?.message ?? 'sin error'}</p>
      <button type="button" onClick={query.reload}>
        Recargar
      </button>
      <button type="button" onClick={() => query.setData({ number: 'CAMBIADA' } as never)}>
        Cambiar en la página
      </button>
      <button type="button" onClick={() => void refresh()}>
        Refrescar sesión
      </button>
    </div>
  );
}

const text = (id: string) => screen.getByTestId(id).textContent;

/** Responde un pedido y espera a que la pantalla lo muestre. */
async function answer(call: Call, value: unknown) {
  await act(async () => {
    call.answer.resolve(value);
    await call.answer.promise;
  });
}

describe('useRpcQuery', () => {
  it('carga al montar: «cargando», luego los datos', async () => {
    const { rpc, calls } = pendingRpc();
    const { services } = await panelWith(rpc);
    await renderPanel(<Probe number="RES-1" />, { web: services });
    expect(text('estado')).toBe('loading|cargando|pidiendo');
    await waitFor(() => expect(calls).toHaveLength(1));
    expect(calls[0]).toMatchObject({ operation: 'CancelMyReservationCommand', payload: { number: 'RES-1' } });
    expect(calls[0].options.signal).toBeInstanceOf(AbortSignal);
    await answer(calls[0], { number: 'RES-1' });
    await waitFor(() => expect(text('estado')).toBe('success|-|-'));
    expect(text('datos')).toBe('{"number":"RES-1"}');
  });

  it('error y «Recargar»: vuelve a consultar y muestra los datos', async () => {
    const { rpc, calls } = pendingRpc();
    const { services } = await panelWith(rpc);
    await renderPanel(<Probe number="RES-1" />, { web: services });
    await waitFor(() => expect(calls).toHaveLength(1));
    await act(async () => {
      calls[0].answer.reject(new WebApiError({ kind: 'network', message: 'Sin respuesta.' }));
      await calls[0].answer.promise.catch(() => undefined);
    });
    await waitFor(() => expect(text('estado')).toBe('error|-|-'));
    expect(text('error')).toBe('Sin respuesta.');
    fireEvent.click(screen.getByRole('button', { name: 'Recargar' }));
    expect(text('estado')).toBe('loading|cargando|pidiendo');
    await waitFor(() => expect(calls).toHaveLength(2));
    await answer(calls[1], { number: 'RES-1' });
    await waitFor(() => expect(text('estado')).toBe('success|-|-'));
    expect(text('error')).toBe('sin error');
  });

  it('un contenido nuevo descarta la respuesta vieja (y cancela su pedido); mientras tanto se ven los datos anteriores', async () => {
    const { rpc, calls } = pendingRpc();
    const { services } = await panelWith(rpc);
    await renderPanel(<ProbeHost number="A" />, { web: services });
    await waitFor(() => expect(calls).toHaveLength(1));
    await answer(calls[0], { number: 'A' });
    await waitFor(() => expect(text('datos')).toBe('{"number":"A"}'));

    reprobe({ number: 'B' });
    await waitFor(() => expect(calls).toHaveLength(2));
    // Datos anteriores a la vista, sin esqueleto, con el pedido en curso.
    expect(text('estado')).toBe('success|-|pidiendo');
    expect(text('datos')).toBe('{"number":"A"}');

    reprobe({ number: 'C' });
    await waitFor(() => expect(calls).toHaveLength(3));
    expect(calls[1].options.signal?.aborted).toBe(true);
    await answer(calls[2], { number: 'C' });
    await answer(calls[1], { number: 'B' });
    await waitFor(() => expect(text('estado')).toBe('success|-|-'));
    expect(text('datos')).toBe('{"number":"C"}');
  });

  it('sin `keepPreviousData` un contenido nuevo vuelve a «cargando»', async () => {
    const { rpc, calls } = pendingRpc();
    const { services } = await panelWith(rpc);
    await renderPanel(<ProbeHost number="A" options={{ keepPreviousData: false }} />, { web: services });
    await waitFor(() => expect(calls).toHaveLength(1));
    await answer(calls[0], { number: 'A' });
    await waitFor(() => expect(text('estado')).toBe('success|-|-'));
    reprobe({ number: 'B', options: { keepPreviousData: false } });
    expect(text('estado')).toBe('loading|cargando|pidiendo');
    expect(text('datos')).toBe('sin datos');
  });

  it('un objeto nuevo con el mismo contenido no repite el pedido', async () => {
    const { rpc, calls } = pendingRpc();
    const { services } = await panelWith(rpc);
    await renderPanel(<ProbeHost number="A" />, { web: services });
    await waitFor(() => expect(calls).toHaveLength(1));
    reprobe({ number: 'A' });
    reprobe({ number: 'A', options: {} });
    await answer(calls[0], { number: 'A' });
    await waitFor(() => expect(text('estado')).toBe('success|-|-'));
    expect(calls).toHaveLength(1);
  });

  it('cancela al desmontar y descarta la respuesta tardía', async () => {
    const { rpc, calls } = pendingRpc();
    const { services } = await panelWith(rpc);
    const view = await renderPanel(<Probe number="A" />, { web: services });
    await waitFor(() => expect(calls).toHaveLength(1));
    view.unmount();
    expect(calls[0].options.signal?.aborted).toBe(true);
    await answer(calls[0], { number: 'A' });
  });

  it('con `enabled: false` no consulta', async () => {
    const { rpc, calls } = pendingRpc();
    const { services } = await panelWith(rpc);
    await renderPanel(<ProbeHost number="A" options={{ enabled: false }} />, { web: services });
    expect(text('estado')).toBe('idle|-|-');
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(calls).toHaveLength(0);
    reprobe({ number: 'A', options: { enabled: true } });
    await waitFor(() => expect(calls).toHaveLength(1));
  });

  it('`setData` cambia los datos sin volver al servidor', async () => {
    const { rpc, calls } = pendingRpc();
    const { services } = await panelWith(rpc);
    await renderPanel(<Probe number="A" />, { web: services });
    await waitFor(() => expect(calls).toHaveLength(1));
    await answer(calls[0], { number: 'A' });
    await waitFor(() => expect(text('estado')).toBe('success|-|-'));
    fireEvent.click(screen.getByRole('button', { name: 'Cambiar en la página' }));
    expect(text('datos')).toBe('{"number":"CAMBIADA"}');
    expect(calls).toHaveLength(1);
  });

  it('vuelve a consultar al cambiar la sucursal activa y no muestra los datos de la otra sucursal', async () => {
    const { rpc, calls } = pendingRpc();
    const { backend, services } = await panelWith(rpc);
    await renderPanel(<Probe number="A" />, { web: services });
    await waitFor(() => expect(calls).toHaveLength(1));
    await answer(calls[0], { number: 'A', sucursal: 'CM' });
    await waitFor(() => expect(text('datos')).toContain('CM'));

    // Otra sucursal activa en el servidor (lo que hace SelectBranchCommand) y el esqueleto refresca la sesión.
    await backend.rpc.send('SelectBranchCommand', { sessionId: '', branchId: '5d0c1f6e-0a51-4d0e-9d11-000000000002' });
    fireEvent.click(screen.getByRole('button', { name: 'Refrescar sesión' }));
    await waitFor(() => expect(calls).toHaveLength(2));
    expect(text('datos')).toBe('sin datos');
    expect(text('estado')).toBe('loading|cargando|pidiendo');
    await answer(calls[1], { number: 'A', sucursal: 'CB' });
    await waitFor(() => expect(text('datos')).toContain('CB'));
  });
});

function Lazy() {
  const query = useRpcQuery('GetMyReservationsQuery', {});
  return <p>{query.loading ? 'Cargando estadísticas…' : `Reservas: ${Array.isArray(query.data) ? query.data.length : 0}`}</p>;
}

describe('useRpcQuery dentro de un Collapsible', () => {
  it('no consulta nada hasta abrir «Ver …»', async () => {
    const { rpc, calls } = pendingRpc();
    const { services } = await panelWith(rpc);
    await renderPanel(
      <Collapsible label="Ver estadísticas">
        <Lazy />
      </Collapsible>,
      { web: services },
    );
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(calls).toHaveLength(0);
    fireEvent.click(screen.getByRole('button', { name: 'Ver estadísticas' }));
    expect(screen.getByText('Cargando estadísticas…')).toBeInTheDocument();
    await waitFor(() => expect(calls).toHaveLength(1));
    await answer(calls[0], [{}, {}]);
    expect(await screen.findByText('Reservas: 2')).toBeInTheDocument();
  });
});
