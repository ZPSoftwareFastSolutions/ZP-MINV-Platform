// Módulo «Pedido sugerido» dentro del panel real con un registro que solo tiene este módulo: líneas con la cantidad
// editable (subtotal y total al instante), filtros en la dirección, «Generar órdenes de compra» con confirmación
// (`CreateSuggestedPurchaseOrdersCommand` con `{}`), el error del servidor dentro de la confirmación, la orden de un
// proveedor con las cantidades revisadas (`CreatePurchaseOrderCommand` con la forma exacta del contrato), «Copiar
// pedido», exportar CSV y los botones solo para quien puede crear órdenes. El servidor se simula: nada toca la red.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { RpcOperationName, RpcResponseOf } from '@/4-presentation/app/contract';
import { renderPanel, signedInAs, type StaffRole } from '@/test-utils';
import { buildRegistry } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import pedidoModule from './module';
import { REVIEWED_ORDER_NOTE } from './order';

const REGISTRY = buildRegistry([{ source: '../modules/pedido/module.tsx', definition: pedidoModule }]);

beforeAll(async () => {
  await import('../../shell/PanelApp');
  await import('./OrderPage');
  await import('./SupplierOrderDialog');
}, 30_000);

afterEach(() => {
  vi.restoreAllMocks();
});

type OrderLine = RpcResponseOf<'GetStockProjectionQuery'>['result']['order'][number];
type StockRecord = RpcResponseOf<'GetStockProjectionQuery'>['result']['stock'][number];

const ANDINA = 'Distribuidora Andina S.R.L.';
const SUR = 'Importadora Sur Ltda.';

function orderLine(sku: string, name: string, supplier: string, patch: Partial<OrderLine> = {}): OrderLine {
  return {
    supplier,
    sku,
    name,
    unit: 'u.',
    status: 'Low',
    stock: 3,
    minimum: 5,
    maximum: 20,
    quantityToOrder: 17,
    unitCost: 250,
    subtotal: 4250,
    leadTimeDays: 5,
    estimatedDelivery: '2026-10-04',
    contact: 'Marcela Rojas',
    phone: '+591 2 2440011',
    email: 'ventas@andina.example',
    ...patch,
  };
}

const ORDER: OrderLine[] = [
  orderLine('TEC-RZ-BW', 'Teclado Razer BlackWidow', ANDINA, { status: 'OutOfStock', stock: 0, quantityToOrder: 20, unitCost: 350, subtotal: 7000 }),
  orderLine('MOU-LOG-G502', 'Mouse Logitech G502', ANDINA, { status: 'Critical' }),
  orderLine('SSD-KIN-480', 'SSD Kingston 480 GB', SUR, { stock: 6, maximum: 15, quantityToOrder: 9, unitCost: 180, subtotal: 1620, leadTimeDays: 10, estimatedDelivery: '2026-10-09', contact: '', phone: '', email: '' }),
  orderLine('CAB-GEN-2M', 'Cable genérico 2 m', '(Sin proveedor)', { quantityToOrder: 10, unitCost: 5, subtotal: 50, leadTimeDays: null, estimatedDelivery: null, contact: '', phone: '', email: '' }),
];

const CATEGORIES: Record<string, string> = { 'TEC-RZ-BW': 'Teclados', 'MOU-LOG-G502': 'Periféricos', 'SSD-KIN-480': 'Almacenamiento', 'CAB-GEN-2M': 'Cables' };

interface FakeOptions {
  order?: OrderLine[];
  generateFails?: boolean;
}

function fakeServer({ order = ORDER, generateFails = false }: FakeOptions = {}) {
  const handlers: Partial<Record<RpcOperationName, (payload: never) => unknown>> = {
    GetStockProjectionQuery: () => ({
      warehouseCode: 'CM-01',
      today: '2026-09-29',
      result: { stock: order.map((line) => ({ sku: line.sku, category: CATEGORIES[line.sku] }) as StockRecord), alerts: [], order, movements: 40 },
    }),
    GetSuppliersQuery: () => [
      { code: 'PROV-AND', name: ANDINA, taxId: null, leadTimeDays: 5, contact: 'Marcela Rojas', phone: null, email: null, isActive: true, products: 12, openOrders: 0, purchased: 0 },
      { code: 'PROV-SUR', name: SUR, taxId: null, leadTimeDays: 10, contact: 'Luis Soto', phone: '+591 4 4250000', email: 'compras@sur.example', isActive: true, products: 4, openOrders: 1, purchased: 0 },
    ],
    CreateSuggestedPurchaseOrdersCommand: () => {
      if (generateFails) {
        throw new WebApiError({ kind: 'domain', status: 422, code: 'purchase.nothing', message: 'No hay órdenes nuevas que crear: el pedido sugerido está vacío o sus proveedores ya tienen una orden abierta.' });
      }
      return ['OC-CM-000045'];
    },
    CreatePurchaseOrderCommand: (payload: { supplierCode: string; lines: unknown[] }) => ({
      id: 'po-1',
      number: 'OC-CM-000046',
      supplierCode: payload.supplierCode,
      supplier: ANDINA,
      orderDate: '2026-09-29',
      expectedDate: '2026-10-04',
      status: 'Draft',
      lines: payload.lines.length,
      total: 9500,
      receivedPercent: 0,
      notes: null,
    }),
  };
  return {
    handles: (operation: string) => operation in handlers,
    handle: (operation: RpcOperationName, payload: unknown) => handlers[operation]?.(payload as never),
  };
}

async function openOrder(role: StaffRole = 'ADMIN', query = '', server = fakeServer()) {
  const web = await signedInAs(role);
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  const call = vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, options) => {
    if (!server.handles(operation)) return real(operation, payload, options);
    return { result: server.handle(operation, payload) as never, replayed: false, requestId: options?.requestId ?? 'prueba' };
  });
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/pedido${query}`, path: '/panel/*' });
  return { call, location: view.location };
}

async function table(caption = 'Productos del pedido sugerido'): Promise<HTMLElement> {
  const region = await screen.findByRole('region', { name: caption }, { timeout: 5000 });
  await waitFor(() => expect(within(region).queryAllByRole('row').length).toBeGreaterThan(1));
  return region;
}

function dataRows(region: HTMLElement): HTMLElement[] {
  return within(region)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'));
}

const keys = (region: HTMLElement) => dataRows(region).map((row) => row.getAttribute('data-row-key'));

function payloadsOf(call: { mock: { calls: unknown[][] } }, operation: RpcOperationName): unknown[] {
  return call.mock.calls.filter(([name]) => name === operation).map(([, payload]) => payload);
}

describe('Pedido sugerido · líneas y cantidades', () => {
  it('muestra qué pedir a quién con la cantidad sugerida, el subtotal y el total', async () => {
    await openOrder();
    expect(await screen.findByRole('heading', { level: 1, name: 'Pedido sugerido' }, { timeout: 5000 })).toBeInTheDocument();
    const grid = await table();
    expect(keys(grid)).toEqual(['TEC-RZ-BW', 'MOU-LOG-G502', 'SSD-KIN-480', 'CAB-GEN-2M']);
    const first = dataRows(grid)[0];
    expect(first).toHaveTextContent('Teclado Razer BlackWidowTEC-RZ-BW · Teclados');
    expect(first).toHaveTextContent('Sin stock');
    expect(within(first).getByRole('textbox', { name: 'Cantidad a pedir de TEC-RZ-BW' })).toHaveValue('20');
    expect(first).toHaveTextContent('Bs 7.000,00');
    expect(screen.getByTestId('pedido-resumen')).toHaveTextContent('4 productos · 3 proveedores · total Bs 12.920,00');
    expect(within(grid).getByTestId('tabla-totales')).toHaveTextContent('Bs 12.920,00');
    expect(screen.getByText(/almacén CM-01, al 29\/09\/2026/)).toBeInTheDocument();
  });

  it('revisar una cantidad recalcula el subtotal y el total; «No pedir» y «Volver a las cantidades sugeridas»', async () => {
    await openOrder();
    const grid = await table();
    const mouse = within(dataRows(grid)[1]).getByRole('textbox', { name: 'Cantidad a pedir de MOU-LOG-G502' });
    fireEvent.change(mouse, { target: { value: '10' } });
    await waitFor(() => expect(dataRows(grid)[1]).toHaveTextContent('Bs 2.500,00'));
    expect(dataRows(grid)[1]).toHaveTextContent('Sugerido: 17');
    expect(screen.getByTestId('pedido-resumen')).toHaveTextContent('total Bs 11.170,00 · 1 cantidad revisada');

    fireEvent.click(within(dataRows(grid)[3]).getByRole('button', { name: /^Acciones de / }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'No pedir este producto' }));
    await waitFor(() => expect(screen.getByTestId('pedido-resumen')).toHaveTextContent('total Bs 11.120,00 · 2 cantidades revisadas'));

    fireEvent.click(screen.getByRole('button', { name: 'Volver a las cantidades sugeridas' }));
    await waitFor(() => expect(screen.getByTestId('pedido-resumen')).toHaveTextContent('total Bs 12.920,00'));
    expect(mouse).toHaveValue('17');
  });

  it('filtra por proveedor, categoría y semáforo (en la dirección)', async () => {
    const { location } = await openOrder();
    const grid = await table();
    fireEvent.change(screen.getByRole('combobox', { name: 'Proveedor' }), { target: { value: ANDINA } });
    await waitFor(() => expect(keys(grid)).toEqual(['TEC-RZ-BW', 'MOU-LOG-G502']));
    expect(location()).toBe(`/panel/pedido?proveedor=${encodeURIComponent(ANDINA).replace(/%20/g, '+')}`);
    fireEvent.change(screen.getByRole('combobox', { name: 'Semáforo' }), { target: { value: 'Critical' } });
    await waitFor(() => expect(keys(grid)).toEqual(['MOU-LOG-G502']));
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Categoría' }), { target: { value: 'Almacenamiento' } });
    await waitFor(() => expect(keys(grid)).toEqual(['SSD-KIN-480']));
  });

  it('sin nada que reponer lo dice y no ofrece generar órdenes', async () => {
    await openOrder('ADMIN', '', fakeServer({ order: [] }));
    expect(await screen.findByText('No hay nada que reponer', undefined, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Generar órdenes de compra' })).toBeDisabled();
  });
});

describe('Pedido sugerido · generar órdenes de compra', () => {
  it('confirma, envía `CreateSuggestedPurchaseOrdersCommand` con `{}` y enlaza a «Órdenes de compra»', async () => {
    const { call } = await openOrder();
    await table();
    fireEvent.click(screen.getByRole('button', { name: 'Generar órdenes de compra' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Generar las órdenes de compra?' });
    expect(dialog).toHaveTextContent('Los proveedores que ya tienen una orden abierta se omiten.');
    expect(within(dialog).getByText(`${SUR} ya tiene una orden abierta.`)).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Generar órdenes' }));
    });
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    expect(payloadsOf(call, 'CreateSuggestedPurchaseOrdersCommand')).toEqual([{}]);
    expect(await screen.findByText('1 orden creada en borrador')).toBeInTheDocument();
    expect(screen.getByTestId('ordenes-creadas')).toHaveTextContent('OC-CM-000045');
    const banner = screen.getByTestId('ordenes-creadas').closest('[role="status"]') as HTMLElement;
    expect(within(banner).getByRole('link', { name: 'Ver órdenes de compra' })).toHaveAttribute('href', '/panel/compras');
    // Se vuelve a leer el pedido después de crear las órdenes.
    expect(payloadsOf(call, 'GetStockProjectionQuery').length).toBeGreaterThan(1);
  });

  it('con cantidades revisadas avisa que la generación usa las sugeridas; el rechazo del servidor queda en el diálogo', async () => {
    await openOrder('ADMIN', '', fakeServer({ generateFails: true }));
    const grid = await table();
    fireEvent.change(within(dataRows(grid)[0]).getByRole('textbox', { name: 'Cantidad a pedir de TEC-RZ-BW' }), { target: { value: '5' } });
    fireEvent.click(screen.getByRole('button', { name: 'Generar órdenes de compra' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Generar las órdenes de compra?' });
    expect(dialog).toHaveTextContent('Revisó 1 cantidad: esta opción no las usa.');
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Generar órdenes' }));
    });
    expect(await within(dialog).findByText(/No hay órdenes nuevas que crear/)).toBeInTheDocument();
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
  });
});

describe('Pedido sugerido · por proveedor', () => {
  it('crea la orden de un proveedor con las cantidades revisadas (forma exacta del contrato)', async () => {
    const { call } = await openOrder();
    const grid = await table();
    fireEvent.change(within(dataRows(grid)[1]).getByRole('textbox', { name: 'Cantidad a pedir de MOU-LOG-G502' }), { target: { value: '10' } });
    fireEvent.click(screen.getByRole('tab', { name: /^Proveedores/ }));
    const suppliers = await table('Pedido por proveedor');
    expect(keys(suppliers)).toEqual([ANDINA, SUR, '(Sin proveedor)']);
    fireEvent.click(within(dataRows(suppliers)[0]).getByRole('button', { name: /^Distribuidora Andina/ }));
    const panel = await screen.findByRole('dialog', { name: ANDINA });
    expect(within(panel).getByTestId('detalle-proveedor')).toHaveTextContent('Mouse Logitech G502MOU-LOG-G502 · 10 u. (sugerido 17)');
    fireEvent.click(within(panel).getByRole('button', { name: 'Crear orden de compra' }));

    const dialog = await screen.findByRole('dialog', { name: `Orden de compra para ${ANDINA}` });
    expect(within(dialog).getByTestId('lineas-de-la-orden')).toHaveTextContent('Total estimadoBs 9.500,00');
    expect(within(dialog).getByLabelText(/^Entrega esperada/)).toHaveValue('2026-10-04');
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Crear orden en borrador' }));
    });
    expect(await screen.findByText('Orden OC-CM-000046 creada en borrador')).toBeInTheDocument();
    expect(payloadsOf(call, 'CreatePurchaseOrderCommand')).toEqual([
      {
        supplierCode: 'PROV-AND',
        expectedDate: '2026-10-04',
        notes: REVIEWED_ORDER_NOTE,
        lines: [
          { sku: 'TEC-RZ-BW', quantity: 20, unitCost: 350 },
          { sku: 'MOU-LOG-G502', quantity: 10, unitCost: 250 },
        ],
      },
    ]);
    const banner = screen.getByTestId('ordenes-creadas').closest('[role="status"]') as HTMLElement;
    expect(within(banner).getByRole('link', { name: 'Ver órdenes de compra' })).toHaveAttribute('href', '/panel/compras?proveedor=PROV-AND');
  });

  it('los productos sin proveedor no pueden pedirse desde aquí (se explica por qué)', async () => {
    await openOrder();
    await table();
    fireEvent.click(screen.getByRole('tab', { name: /^Proveedores/ }));
    const suppliers = await table('Pedido por proveedor');
    fireEvent.click(within(dataRows(suppliers)[2]).getByRole('button', { name: /^Acciones de / }));
    const item = await screen.findByRole('menuitem', { name: /Crear orden de compra/ });
    expect(item).toHaveAttribute('aria-disabled', 'true');
    expect(item).toHaveTextContent('Estos productos no tienen proveedor preferido: asígnelo en el catálogo.');
  });

  it('«Copiar pedido» deja el texto listo para un correo o WhatsApp', async () => {
    const writeText = vi.fn(async () => undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    await openOrder();
    await table();
    fireEvent.click(screen.getByRole('tab', { name: /^Proveedores/ }));
    const suppliers = await table('Pedido por proveedor');
    fireEvent.click(within(dataRows(suppliers)[1]).getByRole('button', { name: /^Acciones de / }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Copiar pedido' }));
    expect(await screen.findByText('Pedido copiado')).toBeInTheDocument();
    expect(writeText).toHaveBeenCalledWith(
      ['Pedido sugerido · 29/09/2026', `Proveedor: ${SUR} · +591 4 4250000 · compras@sur.example`, '  SSD-KIN-480  SSD Kingston 480 GB  →  9 u.  (Bs 1.620,00)', 'Total: Bs 1.620,00'].join('\n'),
    );
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    Reflect.deleteProperty(navigator, 'clipboard');
  });

  it('ventas ve el pedido pero no genera ni crea órdenes', async () => {
    await openOrder('VENTAS');
    await table();
    expect(screen.queryByRole('button', { name: 'Generar órdenes de compra' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Ver órdenes de compra' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: /^Proveedores/ }));
    const suppliers = await table('Pedido por proveedor');
    fireEvent.click(within(dataRows(suppliers)[0]).getByRole('button', { name: /^Acciones de / }));
    const menu = await screen.findByTestId('menu-acciones');
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['Ver detalle', 'Copiar pedido', 'Ver sus productos']);
  });
});

describe('Pedido sugerido · exportar y tablero', () => {
  it('exporta los productos con la cantidad revisada', async () => {
    const created: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      created.push(blob as Blob);
      return 'blob:prueba';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await openOrder();
    const grid = await table();
    fireEvent.change(within(dataRows(grid)[0]).getByRole('textbox', { name: 'Cantidad a pedir de TEC-RZ-BW' }), { target: { value: '8' } });
    fireEvent.click(screen.getByRole('button', { name: 'Exportar CSV' }));
    expect(await screen.findByText(/^Se descargó pedido-sugerido-\d{4}-\d{2}-\d{2}\.csv \(4 filas\)\.$/)).toBeInTheDocument();
    const lines = (await created[0].text()).replace(/^﻿/, '').trim().split('\r\n');
    expect(lines[1]).toContain('"TEC-RZ-BW";"Teclado Razer BlackWidow";"Teclados";"u.";"Sin stock";0;5;20;20;8;350;2800');
  });

  it('el botón del tablero «Generar pedido» exige poder crear órdenes', () => {
    expect(pedidoModule).toMatchObject({ key: 'pedido', section: 'compras', order: 10, permissions: { all: ['inventory.stock.view'] } });
    expect(pedidoModule.actions).toEqual([expect.objectContaining({ label: 'Generar pedido', to: '', permissions: { all: ['purchasing.manage'] } })]);
    expect(REGISTRY.problems).toEqual([]);
  });
});
