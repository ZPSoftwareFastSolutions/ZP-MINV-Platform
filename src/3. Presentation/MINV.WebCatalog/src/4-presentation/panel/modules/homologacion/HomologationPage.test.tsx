// Módulo «Homologación» dentro del panel real, con un registro que tiene SOLO este módulo. El servidor se simula en la
// prueba (el modo mock no atiende estas operaciones): lista con filtros en la dirección («sin homologar» del tablero),
// resumen plegado, asignar un código con búsqueda en el catálogo del SIN, sugerir y aceptar en lote, unidades y medios de
// pago, errores del servidor, permisos por rol, la estadística y el tablero. Ninguna prueba toca la red.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import { renderPanel, signedInAs, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry, dashboardActions, dashboardStats } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import type { HomologationData, SinProductData } from './homologation';
import homologacion from './module';
import { PendingStat } from './PendingStat';

const REGISTRY = buildRegistry([{ source: '../modules/homologacion/module.tsx', definition: homologacion }]);

beforeAll(async () => {
  await import('../../shell/PanelLayout');
  await import('./HomologationPage');
}, 30_000);

afterEach(() => {
  vi.restoreAllMocks();
});

// ---------------------------------------------------------------------------------------------------- servidor simulado

function view(): HomologationData {
  return {
    activities: [
      { activityType: 'S', code: '620000', description: 'SERVICIOS DE PROGRAMACION', isCurrent: true, sectors: [2] },
      { activityType: 'P', code: '461000', description: 'VENTA AL POR MAYOR DE EQUIPOS', isCurrent: true, sectors: [1, 24] },
    ],
    paymentMethods: [
      { code: 'EFECTIVO', name: 'Efectivo', paymentMethodId: 'pm-1', sinCode: 1, sinDescription: 'EFECTIVO' },
      { code: 'QR', name: 'Pago QR', paymentMethodId: 'pm-2', sinCode: null, sinDescription: null },
    ],
    pendingProducts: 2,
    products: [
      { activityCode: '461000', category: 'Periféricos', isActive: true, name: 'Mouse gamer', productId: 'p-1', sinProductCode: 83141, sinProductDescription: 'MOUSE', sku: 'MOU-01' },
      { activityCode: null, category: 'Video', isActive: true, name: 'Tarjeta de video RTX', productId: 'p-2', sinProductCode: null, sinProductDescription: null, sku: 'GPU-01' },
      { activityCode: null, category: 'Periféricos', isActive: true, name: 'Teclado mecánico', productId: 'p-3', sinProductCode: null, sinProductDescription: null, sku: 'TEC-01' },
      { activityCode: null, category: 'Otros', isActive: false, name: 'Producto viejo', productId: 'p-4', sinProductCode: null, sinProductDescription: null, sku: 'OLD-01' },
    ],
    sinPaymentMethods: [
      { catalog: 'TIPO_METODO_PAGO', code: 1, description: 'EFECTIVO', isCurrent: true },
      { catalog: 'TIPO_METODO_PAGO', code: 7, description: 'TRANSFERENCIA BANCARIA', isCurrent: true },
      { catalog: 'TIPO_METODO_PAGO', code: 33, description: 'PAGO ONLINE', isCurrent: true },
    ],
    sinUnits: [
      { catalog: 'UNIDAD_MEDIDA', code: 58, description: 'UNIDAD (BIENES)', isCurrent: true },
      { catalog: 'UNIDAD_MEDIDA', code: 47, description: 'JUEGO', isCurrent: true },
    ],
    units: [
      { code: 'UND', name: 'Unidad', sinUnitCode: 58, sinUnitDescription: 'UNIDAD (BIENES)', unitId: 'u-1' },
      { code: 'KIT', name: 'Kit', sinUnitCode: null, sinUnitDescription: null, unitId: 'u-2' },
    ],
  };
}

const SIN_PRODUCTS: SinProductData[] = [
  { activityCode: '461000', productCode: 83141, description: 'MOUSE', isCurrent: true },
  { activityCode: '461000', productCode: 83142, description: 'TARJETA DE VIDEO', isCurrent: true },
  { activityCode: '461000', productCode: 83143, description: 'TECLADO', isCurrent: true },
  { activityCode: '461000', productCode: 83144, description: 'TARJETA MADRE', isCurrent: true },
];

type Payload = Record<string, unknown>;

class HomologationServer {
  view = view();
  suggestions = [
    { sku: 'GPU-01', activityCode: '461000', sinProductCode: 83142 },
    { sku: 'TEC-01', activityCode: '461000', sinProductCode: 83143 },
  ];
  calls: { operation: string; payload: Payload }[] = [];
  failures = new Map<string, WebApiError>();

  payloads(operation: string): Payload[] {
    return this.calls.filter((call) => call.operation === operation).map((call) => call.payload);
  }

  handle(operation: string, payload: Payload): { handled: true; result: unknown } | { handled: false } {
    const known = ['GetHomologationQuery', 'SearchSiatProductsQuery', 'SuggestProductHomologationQuery', 'SaveProductHomologationCommand', 'SaveUnitHomologationCommand', 'SavePaymentMethodHomologationCommand'];
    if (!known.includes(operation)) return { handled: false };
    this.calls.push({ operation, payload });
    const failure = this.failures.get(operation);
    if (failure) {
      this.failures.delete(operation);
      throw failure;
    }
    return { handled: true, result: this.execute(operation, payload) };
  }

  private execute(operation: string, payload: Payload): unknown {
    switch (operation) {
      case 'GetHomologationQuery':
        return this.view;
      case 'SearchSiatProductsQuery': {
        const text = typeof payload.text === 'string' ? payload.text.toUpperCase() : null;
        return SIN_PRODUCTS.filter((item) => (payload.activityCode == null || item.activityCode === payload.activityCode) && (text === null || item.description.includes(text)));
      }
      case 'SuggestProductHomologationQuery':
        return this.suggestions;
      case 'SaveProductHomologationCommand': {
        const items = payload.items as { sku: string; activityCode: string; sinProductCode: number }[];
        this.view = {
          ...this.view,
          pendingProducts: this.view.pendingProducts - items.length,
          products: this.view.products.map((row) => {
            const item = items.find((candidate) => candidate.sku === row.sku);
            return item ? { ...row, activityCode: item.activityCode, sinProductCode: item.sinProductCode, sinProductDescription: SIN_PRODUCTS.find((sin) => sin.productCode === item.sinProductCode)?.description ?? null } : row;
          }),
        };
        return `✔ ${items.length} productos homologados.`;
      }
      case 'SaveUnitHomologationCommand':
        return '✔ Unidad KIT homologada con 47.';
      case 'SavePaymentMethodHomologationCommand':
        return '✔ Medio de pago QR homologado con 33.';
      default:
        throw new Error(`Operación no simulada: ${operation}`);
    }
  }
}

function serve(web: MockWeb, server: HomologationServer) {
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, options) => {
    const answer = server.handle(operation, payload as Payload);
    if (answer.handled) return { result: answer.result as never, replayed: false, requestId: options?.requestId ?? 'prueba' };
    return real(operation, payload, options);
  });
}

async function openHomologation(role: StaffRole = 'ADMIN', query = '', server = new HomologationServer()) {
  const web = await signedInAs(role);
  serve(web, server);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/homologacion${query}`, path: '/panel/*' });
  return { web, server, location: view.location };
}

async function table(): Promise<HTMLElement> {
  const found = await screen.findByTestId('tabla', undefined, { timeout: 5000 });
  await waitFor(() => expect(within(found).queryAllByRole('row').length).toBeGreaterThan(1));
  return found;
}

function dataRows(tableElement: HTMLElement): HTMLElement[] {
  return within(tableElement)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'));
}

async function rowAction(tableElement: HTMLElement, key: string, action: string) {
  const row = dataRows(tableElement).find((candidate) => candidate.getAttribute('data-row-key') === key);
  if (!row) throw new Error(`No está la fila ${key}`);
  fireEvent.click(within(row).getByRole('button', { name: /^Acciones de / }));
  fireEvent.click(await screen.findByRole('menuitem', { name: action }));
}

// ---------------------------------------------------------------------------------------------------- productos

describe('Homologación · productos', () => {
  it('lista los productos con su código del SIN, avisa los pendientes y el resumen está plegado', async () => {
    const { server } = await openHomologation();
    expect(await screen.findByRole('heading', { level: 1, name: 'Homologación' }, { timeout: 5000 })).toBeInTheDocument();
    const grid = await table();
    expect(server.payloads('GetHomologationQuery')).toEqual([{}]);
    expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['p-1', 'p-4', 'p-2', 'p-3']);
    expect(dataRows(grid)[0]).toHaveTextContent('83141 · Mouse');
    expect(screen.getByText('2 productos activos sin homologar')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Ver resumen de la homologación/ })).toHaveAttribute('aria-expanded', 'false');
    fireEvent.click(screen.getByRole('button', { name: /Ver resumen de la homologación/ }));
    expect(await screen.findByTestId('resumen-homologacion')).toHaveTextContent('Productos homologados1de 3 productos activos');
  });

  it('«Homologar productos» del tablero (?estado=pendiente) muestra solo los que faltan; filtros en la dirección', async () => {
    const { location } = await openHomologation('ADMIN', '?estado=pendiente');
    const grid = await table();
    expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['p-2', 'p-3']);
    fireEvent.change(screen.getByRole('combobox', { name: 'Categoría' }), { target: { value: 'Video' } });
    await waitFor(() => expect(location()).toBe('/panel/homologacion?estado=pendiente&categoria=Video'));
    await waitFor(() => expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['p-2']));
  });

  it('asignar el código desde el menú «⋯»: no abre el detalle, busca en el catálogo del SIN y guarda la forma exacta del contrato', async () => {
    const { server } = await openHomologation();
    const grid = await table();
    await rowAction(grid, 'p-2', 'Asignar código del SIN');
    const dialog = await screen.findByRole('dialog', { name: 'Homologar GPU-01' });
    expect(screen.queryByRole('dialog', { name: /Tarjeta de video RTX$/ })).not.toBeInTheDocument();
    await waitFor(() => expect(server.payloads('SearchSiatProductsQuery')).toEqual([{ activityCode: '461000', text: 'Tarjeta', max: 100 }]));
    const choice = await within(dialog).findByRole('radio', { name: '83142 · Tarjeta de video' });
    expect(within(dialog).getByRole('radio', { name: '83144 · Tarjeta madre' })).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar homologación' }));
    expect(await within(dialog).findByText('Elija el producto del SIN.')).toBeInTheDocument();
    fireEvent.click(choice);
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar homologación' }));
    });
    expect(server.payloads('SaveProductHomologationCommand')).toEqual([{ items: [{ sku: 'GPU-01', activityCode: '461000', sinProductCode: 83142 }] }]);
    expect(await screen.findByText('Producto homologado')).toBeInTheDocument();
    await waitFor(() => expect(dataRows(grid).find((row) => row.getAttribute('data-row-key') === 'p-2')).toHaveTextContent('83142 · Tarjeta de video'));
  });

  it('el detalle lateral de un producto ofrece «Asignar código del SIN»; buscar otra palabra consulta el catálogo', async () => {
    const { server } = await openHomologation();
    const grid = await table();
    fireEvent.click(within(dataRows(grid).find((row) => row.getAttribute('data-row-key') === 'p-3')!).getAllByRole('button')[0]);
    const panel = await screen.findByRole('dialog', { name: 'TEC-01 · Teclado mecánico' });
    expect(panel).toHaveTextContent('Sin homologar no se factura');
    fireEvent.click(within(panel).getByRole('button', { name: 'Asignar código del SIN' }));
    const dialog = await screen.findByRole('dialog', { name: 'Homologar TEC-01' });
    const search = within(dialog).getByRole('searchbox', { name: 'Buscar en el catálogo del SIN' });
    fireEvent.change(search, { target: { value: 'mouse' } });
    fireEvent.keyDown(search, { key: 'Enter' });
    await waitFor(() => expect(server.payloads('SearchSiatProductsQuery').at(-1)).toEqual({ activityCode: '461000', text: 'mouse', max: 100 }));
    // Un solo resultado queda elegido.
    expect(await within(dialog).findByTestId('eleccion-sin')).toHaveTextContent('83141 · Mouse');
  });

  it('sugerir la homologación de la actividad y aceptar solo las marcadas', async () => {
    const { server } = await openHomologation();
    await table();
    expect(screen.getByRole('combobox', { name: /^Actividad para sugerir/ })).toHaveValue('461000');
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Sugerir homologación' }));
    });
    expect(server.payloads('SuggestProductHomologationQuery')).toEqual([{ activityCode: '461000' }]);
    expect(server.payloads('SearchSiatProductsQuery')).toEqual([{ activityCode: '461000', text: null, max: 1000 }]);
    const dialog = await screen.findByRole('dialog', { name: 'Sugerencias de homologación · actividad 461000' });
    const suggestions = within(dialog).getByTestId('sugerencias');
    expect(suggestions).toHaveTextContent('GPU-01 · Tarjeta de video RTX');
    expect(suggestions).toHaveTextContent('→ 83143 · Teclado');
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'TEC-01 · Teclado mecánico' }));
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Aceptar las marcadas (1)' }));
    });
    expect(server.payloads('SaveProductHomologationCommand')).toEqual([{ items: [{ sku: 'GPU-01', activityCode: '461000', sinProductCode: 83142 }] }]);
    expect(await screen.findByText('Homologación guardada')).toBeInTheDocument();
  });

  it('sin sugerencias lo avisa; el error del servidor al guardar se ve en el diálogo', async () => {
    const server = new HomologationServer();
    server.suggestions = [];
    server.failures.set('SaveProductHomologationCommand', new WebApiError({ kind: 'domain', status: 422, code: 'billing.sin_product', message: 'El código 83142 no es de la actividad 461000.' }));
    await openHomologation('ADMIN', '', server);
    const grid = await table();
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Sugerir homologación' }));
    });
    expect(await screen.findByText('Sin sugerencias')).toBeInTheDocument();
    await rowAction(grid, 'p-2', 'Asignar código del SIN');
    const dialog = await screen.findByRole('dialog', { name: 'Homologar GPU-01' });
    fireEvent.click(await within(dialog).findByRole('radio', { name: '83142 · Tarjeta de video' }));
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar homologación' }));
    });
    expect(await within(dialog).findByText('El código 83142 no es de la actividad 461000.')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------------------------------- unidades y medios de pago

describe('Homologación · unidades y medios de pago', () => {
  it('unidades: filtra las sin homologar y cambia la unidad del SIN con el catálogo sincronizado', async () => {
    const { server, location } = await openHomologation('ADMIN', '?pestana=unidades');
    const grid = await table();
    expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['u-2', 'u-1']);
    fireEvent.change(screen.getByRole('combobox', { name: 'Homologación de la unidad' }), { target: { value: 'pendiente' } });
    await waitFor(() => expect(location()).toBe('/panel/homologacion?pestana=unidades&u_estado=pendiente'));
    await waitFor(() => expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['u-2']));
    await rowAction(grid, 'u-2', 'Cambiar la unidad del SIN');
    const dialog = await screen.findByRole('dialog', { name: 'Unidad del SIN · KIT' });
    fireEvent.change(within(dialog).getByRole('combobox', { name: /^Unidad del SIN/ }), { target: { value: '47' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));
    });
    expect(server.payloads('SaveUnitHomologationCommand')).toEqual([{ unitCode: 'KIT', sinUnitCode: 47 }]);
    expect(await screen.findByText('Unidad homologada')).toBeInTheDocument();
  });

  it('medios de pago: el error del servidor queda en el diálogo; al reintentar se guarda', async () => {
    const server = new HomologationServer();
    server.failures.set('SavePaymentMethodHomologationCommand', new WebApiError({ kind: 'domain', status: 422, code: 'billing.catalog', message: 'El código 33 no está vigente en el catálogo del SIN.' }));
    await openHomologation('ADMIN', '?pestana=medios', server);
    const grid = await table();
    await rowAction(grid, 'pm-2', 'Cambiar el método del SIN');
    const dialog = await screen.findByRole('dialog', { name: 'Método de pago del SIN · Pago QR' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));
    expect(await within(dialog).findByText('Elija el código del SIN.')).toBeInTheDocument();
    fireEvent.change(within(dialog).getByRole('combobox', { name: /^Método de pago del SIN/ }), { target: { value: '33' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));
    });
    expect(await within(dialog).findByText('El código 33 no está vigente en el catálogo del SIN.')).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));
    });
    expect(server.payloads('SavePaymentMethodHomologationCommand')).toEqual([
      { paymentMethodCode: 'QR', sinCode: 33 },
      { paymentMethodCode: 'QR', sinCode: 33 },
    ]);
    expect(await screen.findByText('Medio de pago homologado')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------------------------------- permisos y tablero

describe('Homologación · permisos, estadística y tablero', () => {
  it('quien solo ve la facturación (consulta) ve todo sin asignar ni sugerir', async () => {
    await openHomologation('CONSULTA');
    const grid = await table();
    expect(screen.queryByRole('button', { name: 'Sugerir homologación' })).not.toBeInTheDocument();
    const row = dataRows(grid)[0];
    fireEvent.click(within(row).getByRole('button', { name: /^Acciones de / }));
    const menu = await screen.findByTestId('menu-acciones');
    expect(within(menu).queryByRole('menuitem', { name: 'Asignar código del SIN' })).not.toBeInTheDocument();
    expect(within(menu).getByRole('menuitem', { name: 'Ver detalle' })).toBeInTheDocument();
  });

  it('un rol sin `billing.view` (bodega) ve «No tiene acceso a esta pantalla»', async () => {
    await openHomologation('BODEGA');
    expect(await screen.findByTestId('sin-acceso', undefined, { timeout: 5000 })).toHaveTextContent('Falta el permiso');
  });

  it('si la consulta falla muestra el error con «Reintentar» y se recupera', async () => {
    const server = new HomologationServer();
    server.failures.set('GetHomologationQuery', new WebApiError({ kind: 'network', message: 'Sin conexión' }));
    await openHomologation('ADMIN', '', server);
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    await table();
  });

  it('la estadística «Homologación pendiente» y el botón del tablero (solo para quien configura)', async () => {
    const web = await signedInAs('ADMIN');
    serve(web, new HomologationServer());
    await renderPanel(<PendingStat />, { web: web.services });
    const stat = await screen.findByTestId('homologacion-pendiente');
    await waitFor(() => expect(within(stat).getByRole('link', { name: 'Ver los productos sin homologar' })).toHaveAttribute('href', '/panel/homologacion?estado=pendiente'));
    expect(stat).toHaveTextContent('Productos sin homologar2de 3 activos');
    expect(REGISTRY.problems).toEqual([]);
    expect(dashboardActions(REGISTRY, ['billing.view'])).toEqual([]);
    expect(dashboardActions(REGISTRY, ['billing.view', 'billing.configure']).flatMap((group) => group.actions.map((item) => [item.action.label, item.to]))).toEqual([
      ['Homologar productos', '/panel/homologacion?estado=pendiente'],
    ]);
    expect(dashboardStats(REGISTRY, ['billing.view']).map((item) => item.stat.title)).toEqual(['Homologación pendiente']);
  });
});
