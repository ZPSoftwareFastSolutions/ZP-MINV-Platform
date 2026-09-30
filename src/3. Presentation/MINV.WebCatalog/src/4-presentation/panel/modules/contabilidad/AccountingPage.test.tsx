// Módulo «Contabilidad» dentro del panel real con un registro que solo tiene los módulos de este paquete (Reportes y
// Contabilidad): el estado de resultados (pedido exacto, cuentas que llevan al libro, gráfico plegado), el libro diario
// (filtros, detalle Debe/Haber, mayor de la cuenta, CSV, «Usar como plantilla» sin abrir el detalle), el plan de cuentas
// (árbol, filtros, «Crear una subcuenta»), «Nueva cuenta» (validación, pedido exacto, error del servidor) y «Nuevo
// asiento» (plantilla, Debe = Haber en vivo, validación, confirmación, pedido exacto y error del servidor), los permisos
// y el tablero. El servidor se simula en la prueba: nada toca la red.

import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import type { RpcOperationName, RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';
import { ROLE_PERMISSIONS, renderPanel, signedInAs, type StaffRole } from '@/test-utils';
import { laPazToday } from '../../lib';
import { buildRegistry, dashboardActions } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import reportes from '../reportes/module';
import contabilidad from './module';
import { periodRange } from './period';

const REGISTRY = buildRegistry([
  { source: '../modules/reportes/module.tsx', definition: reportes },
  { source: '../modules/contabilidad/module.tsx', definition: contabilidad },
]);

beforeAll(async () => {
  await import('../../shell/PanelApp');
  await import('./AccountingPage');
}, 30_000);

afterEach(() => {
  vi.restoreAllMocks();
});

type Account = RpcResponseOf<'GetChartOfAccountsQuery'>[number];
type Entry = RpcResponseOf<'GetJournalQuery'>[number];

function account(code: string, name: string, type: Account['type'], patch: Partial<Account> = {}): Account {
  const level = code.split('.').length - 1;
  return { code, name, type, level, isPostable: level >= 2, parentCode: level === 0 ? null : code.slice(0, code.lastIndexOf('.')), debit: 0, credit: 0, balance: 0, ...patch };
}

const CHART: Account[] = [
  account('1', 'ACTIVO', 'Asset', { debit: 1116, credit: 200, balance: 916 }),
  account('1.1', 'Activo corriente', 'Asset', { debit: 1116, credit: 200, balance: 916 }),
  account('1.1.01', 'Caja', 'Asset', { debit: 1116, credit: 200, balance: 916 }),
  account('1.1.02', 'Bancos', 'Asset'),
  account('4', 'INGRESOS', 'Revenue', { credit: 1116, balance: 1116 }),
  account('4.1', 'Ingresos operativos', 'Revenue', { credit: 1116, balance: 1116 }),
  account('4.1.01', 'Ventas', 'Revenue', { credit: 1116, balance: 1116 }),
  account('6', 'GASTOS', 'Expense', { debit: 200, balance: 200 }),
  account('6.1', 'Gastos de operación', 'Expense', { debit: 200, balance: 200 }),
  account('6.1.01', 'Sueldos y salarios', 'Expense', { debit: 200, balance: 200 }),
  account('6.1.04', 'Gastos administrativos', 'Expense'),
];

const JOURNAL: Entry[] = [
  { number: 'AS-CM-000011', date: '2026-09-21', description: 'Pago de sueldos del mes', status: 'Posted', total: 200, lines: [{ accountCode: '6.1.01', accountName: 'Sueldos y salarios', debit: 200, credit: 0, memo: 'Septiembre' }, { accountCode: '1.1.01', accountName: 'Caja', debit: 0, credit: 200, memo: null }] },
  { number: 'AS-CM-000010', date: '2026-09-20', description: 'Venta F-CM-000100 · Juan Pérez · Efectivo', status: 'Posted', total: 1116, lines: [{ accountCode: '1.1.01', accountName: 'Caja', debit: 1116, credit: 0, memo: null }, { accountCode: '4.1.01', accountName: 'Ventas', debit: 0, credit: 1116, memo: null }] },
];

interface ServerOptions {
  entryFailsOnce?: string;
  accountFailsOnce?: string;
}

function fakeServer({ entryFailsOnce, accountFailsOnce }: ServerOptions = {}) {
  let entryFailure = entryFailsOnce;
  let accountFailure = accountFailsOnce;
  const handlers: Partial<Record<RpcOperationName, (payload: never) => unknown>> = {
    GetChartOfAccountsQuery: () => CHART,
    GetJournalQuery: () => JOURNAL,
    GetIncomeStatementQuery: (payload: { from: string; to: string }) => ({
      from: payload.from,
      to: payload.to,
      revenue: [CHART[6]],
      costs: [],
      expenses: [CHART[9]],
      totalRevenue: 1116,
      totalCosts: 0,
      grossProfit: 1116,
      totalExpenses: 200,
      netIncome: 916,
    }),
    CreateJournalEntryCommand: () => {
      if (entryFailure) {
        const message = entryFailure;
        entryFailure = undefined;
        throw new WebApiError({ kind: 'domain', status: 422, message });
      }
      return 'AS-CM-000012';
    },
    CreateAccountCommand: (payload: RpcRequestOf<'CreateAccountCommand'>) => {
      if (accountFailure) {
        const message = accountFailure;
        accountFailure = undefined;
        throw new WebApiError({ kind: 'domain', status: 422, message });
      }
      return payload.code;
    },
  };
  return handlers;
}

async function openAccounting(role: StaffRole = 'GERENCIA', query = '', options: ServerOptions = {}) {
  const web = await signedInAs(role);
  const handlers = fakeServer(options);
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  const call = vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, sendOptions) => {
    const handler = handlers[operation];
    if (!handler) return real(operation, payload, sendOptions);
    return { result: handler(payload as never) as never, replayed: false, requestId: sendOptions?.requestId ?? 'prueba' };
  });
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/contabilidad${query}`, path: '/panel/*' });
  return { call, location: view.location };
}

function requests(call: { mock: { calls: unknown[][] } }, operation: RpcOperationName): unknown[] {
  return call.mock.calls.filter((args) => args[0] === operation).map((args) => args[1]);
}

async function table(name: string): Promise<HTMLElement> {
  const region = await screen.findByRole('region', { name }, { timeout: 5000 });
  await waitFor(() => expect(within(region).queryAllByRole('row').length).toBeGreaterThan(1));
  return region;
}

function dataRows(region: HTMLElement): HTMLElement[] {
  return within(region)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'));
}

describe('Contabilidad · estado de resultados', () => {
  it('pide «Este mes», muestra las secciones y lleva de una cuenta a sus asientos; el gráfico está plegado', async () => {
    const { call, location } = await openAccounting();
    const statement = await screen.findByTestId('estado-resultados', undefined, { timeout: 5000 });
    expect(requests(call, 'GetIncomeStatementQuery')).toEqual([periodRange('esteMes')]);
    expect(within(statement).getByTestId('linea-total-ingresos')).toHaveTextContent('Total ingresosBs 1.116,00100,0 %');
    expect(within(statement).getByTestId('linea-utilidad-neta')).toHaveTextContent('Utilidad neta del períodoBs 916,00');
    expect(screen.getByRole('button', { name: /Ver gráfico/ })).toHaveAttribute('aria-expanded', 'false');

    fireEvent.click(within(statement).getByRole('button', { name: /6\.1\.01/ }));
    await waitFor(() => expect(location()).toBe('/panel/contabilidad?vista=diario&cuenta=6.1.01'));
    const grid = await table('Libro diario');
    expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['AS-CM-000011']);
    expect(screen.getByTestId('diario-mayor')).toHaveTextContent('6.1.01 · Sueldos y salarios · Debe Bs 200,00 · Haber Bs 0,00 · en 1 asientos');
  });
});

describe('Contabilidad · libro diario', () => {
  it('filtra por origen, despliega el detalle Debe/Haber y exporta una fila por línea', async () => {
    const created: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      created.push(blob as Blob);
      return 'blob:prueba';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const { call, location } = await openAccounting('GERENCIA', '?vista=diario');
    const grid = await table('Libro diario');
    expect(requests(call, 'GetJournalQuery')).toEqual([periodRange('esteMes')]);
    expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['AS-CM-000011', 'AS-CM-000010']);
    expect(dataRows(grid)[1]).toHaveTextContent('Venta');

    fireEvent.change(screen.getByRole('combobox', { name: 'Origen' }), { target: { value: 'venta' } });
    await waitFor(() => expect(location()).toBe('/panel/contabilidad?vista=diario&origen=venta'));
    await waitFor(() => expect(dataRows(grid)).toHaveLength(1));
    fireEvent.click(within(dataRows(grid)[0]).getByRole('button', { name: 'Detalle de el asiento AS-CM-000010' }));
    expect(await screen.findByTestId('lineas-AS-CM-000010')).toHaveTextContent('1.1.01 CajaBs 1.116,00');

    fireEvent.click(screen.getByRole('button', { name: 'Exportar CSV' }));
    expect(await screen.findByText(/^Se descargó libro-diario-.+\.csv \(1 asientos\)\.$/)).toBeInTheDocument();
    const lines = (await created[0].text()).replace(/^﻿/, '').trim().split('\r\n');
    expect(lines[0]).toBe('"Asiento";"Fecha";"Origen";"Estado";"Descripción";"Cuenta";"Nombre de la cuenta";"Debe";"Haber";"Glosa"');
    expect(lines.slice(1)).toEqual([
      '"AS-CM-000010";"2026-09-20";"Venta";"Contabilizado";"Venta F-CM-000100 · Juan Pérez · Efectivo";"1.1.01";"Caja";1116;0;',
      '"AS-CM-000010";"2026-09-20";"Venta";"Contabilizado";"Venta F-CM-000100 · Juan Pérez · Efectivo";"4.1.01";"Ventas";0;1116;',
    ]);
  });

  it('«Usar como plantilla» del menú abre un asiento nuevo con las mismas líneas (y no abre el detalle)', async () => {
    await openAccounting('GERENCIA', '?vista=diario');
    const grid = await table('Libro diario');
    fireEvent.click(within(dataRows(grid)[0]).getByRole('button', { name: /^Acciones de / }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Usar como plantilla de un asiento nuevo' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo asiento' });
    expect(within(dialog).getByRole('textbox', { name: /^Descripción/ })).toHaveValue('Pago de sueldos del mes');
    expect(within(within(dialog).getByRole('group', { name: 'Línea 1' })).getByRole('textbox', { name: /^Debe/ })).toHaveValue('200,00');
    expect(within(dialog).getByText('Cuadrado: el debe es igual al haber.')).toBeInTheDocument();
    expect(screen.queryByTestId('asiento-detalle')).not.toBeInTheDocument();
  });
});

describe('Contabilidad · plan de cuentas y cuentas nuevas', () => {
  it('muestra el árbol, filtra por tipo y crea una subcuenta con el código sugerido (pedido exacto)', async () => {
    const { call, location } = await openAccounting('GERENCIA', '?vista=cuentas');
    const grid = await table('Plan de cuentas');
    expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(CHART.map((item) => item.code));
    fireEvent.change(screen.getByRole('combobox', { name: 'Tipo' }), { target: { value: 'Expense' } });
    await waitFor(() => expect(location()).toBe('/panel/contabilidad?vista=cuentas&tipo=Expense'));
    await waitFor(() => expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['6', '6.1', '6.1.01', '6.1.04']));

    const group = dataRows(grid).find((row) => row.getAttribute('data-row-key') === '6.1')!;
    fireEvent.click(within(group).getByRole('button', { name: /^Acciones de / }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Crear una subcuenta' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nueva cuenta' });
    expect(within(dialog).getByRole('textbox', { name: /^Código/ })).toHaveValue('6.1.05');
    expect(dialog).toHaveTextContent('Tipo de la cuenta nuevaCosto o gasto');

    fireEvent.click(within(dialog).getByRole('button', { name: 'Crear cuenta' }));
    expect(await within(dialog).findByText('Escriba el nombre de la cuenta.')).toBeInTheDocument();
    expect(requests(call, 'CreateAccountCommand')).toEqual([]);

    fireEvent.change(within(dialog).getByRole('textbox', { name: /^Nombre/ }), { target: { value: ' Publicidad ' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Crear cuenta' }));
    expect(await screen.findByText('Cuenta 6.1.05 · Publicidad creada')).toBeInTheDocument();
    expect(requests(call, 'CreateAccountCommand')).toEqual([{ code: '6.1.05', name: 'Publicidad', parentCode: '6.1' }]);
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nueva cuenta' })).not.toBeInTheDocument());
    // El plan se vuelve a leer para mostrar la cuenta nueva.
    expect(requests(call, 'GetChartOfAccountsQuery').length).toBeGreaterThanOrEqual(2);
  });

  it('el error del servidor se muestra dentro del diálogo, sin cerrarlo', async () => {
    await openAccounting('GERENCIA', '?vista=cuentas', { accountFailsOnce: 'Ya existe la cuenta 6.1.05.' });
    await table('Plan de cuentas');
    fireEvent.click(screen.getByRole('button', { name: 'Nueva cuenta' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nueva cuenta' });
    fireEvent.change(within(dialog).getByRole('textbox', { name: /^Nombre/ }), { target: { value: 'Publicidad' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Crear cuenta' }));
    expect(await within(dialog).findByText('Ya existe la cuenta 6.1.05.')).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: 'Nueva cuenta' })).toBeInTheDocument();
  });
});

describe('Contabilidad · asiento manual', () => {
  it('plantilla, Debe = Haber en vivo, validación, confirmación y el pedido exacto; al terminar muestra el libro', async () => {
    const { call, location } = await openAccounting('ADMIN');
    await screen.findByTestId('estado-resultados', undefined, { timeout: 5000 });
    fireEvent.click(screen.getByRole('button', { name: 'Nuevo asiento' }));
    await waitFor(() => expect(location()).toBe('/panel/contabilidad?nuevo=asiento'));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo asiento' });

    // Vacío: se valida en la página y no se envía nada.
    fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar asiento' }));
    expect(await within(dialog).findByText('Un asiento necesita al menos dos líneas con monto.')).toBeInTheDocument();
    expect(within(dialog).getByText('Escriba la descripción del asiento (qué registra).')).toBeInTheDocument();

    fireEvent.change(within(dialog).getByRole('combobox', { name: /^Plantilla/ }), { target: { value: 'sueldos' } });
    expect(within(dialog).getByRole('textbox', { name: /^Descripción/ })).toHaveValue('Pago de sueldos del mes');
    const first = within(dialog).getByRole('group', { name: 'Línea 1' });
    const second = within(dialog).getByRole('group', { name: 'Línea 2' });
    expect(within(first).getByRole('combobox', { name: /^Cuenta/ })).toHaveValue('6.1.01 · Sueldos y salarios');
    expect(within(second).getByRole('combobox', { name: /^Cuenta/ })).toHaveValue('1.1.02 · Bancos');

    fireEvent.change(within(first).getByRole('textbox', { name: /^Debe/ }), { target: { value: '1.500' } });
    expect(within(dialog).getByText('No cuadra: la diferencia es Bs 1.500,00.')).toBeInTheDocument();
    fireEvent.change(within(second).getByRole('textbox', { name: /^Haber/ }), { target: { value: '1500' } });
    expect(within(dialog).getByText('Cuadrado: el debe es igual al haber.')).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar asiento' }));
    const confirm = await screen.findByRole('alertdialog', { name: '¿Registrar el asiento?' });
    expect(confirm).toHaveTextContent('Un asiento registrado no se borra ni se edita');
    expect(requests(call, 'CreateJournalEntryCommand')).toEqual([]);
    fireEvent.click(within(confirm).getByRole('button', { name: 'Registrar asiento' }));

    expect(await screen.findByText('Asiento AS-CM-000012 registrado · Pago de sueldos del mes')).toBeInTheDocument();
    expect(requests(call, 'CreateJournalEntryCommand')).toEqual([
      {
        date: laPazToday(),
        description: 'Pago de sueldos del mes',
        lines: [
          { accountCode: '6.1.01', debit: 1500, credit: 0, memo: null },
          { accountCode: '1.1.02', debit: 0, credit: 1500, memo: null },
        ],
        branchId: expect.any(String),
      },
    ]);
    await waitFor(() => expect(location()).toBe('/panel/contabilidad?vista=diario'));
    await table('Libro diario');
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nuevo asiento' })).not.toBeInTheDocument());
  });

  it('el rechazo del servidor queda a la vista en la confirmación y en el formulario, para corregir', async () => {
    const { call } = await openAccounting('ADMIN', '?nuevo=asiento', { entryFailsOnce: 'El período contable de esa fecha está cerrado.' });
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo asiento' }, { timeout: 5000 });
    fireEvent.change(within(dialog).getByRole('textbox', { name: /^Descripción/ }), { target: { value: 'Depósito del día' } });
    const first = within(dialog).getByRole('group', { name: 'Línea 1' });
    const second = within(dialog).getByRole('group', { name: 'Línea 2' });
    const pick = async (group: HTMLElement, text: string, option: string) => {
      const box = within(group).getByRole('combobox', { name: /^Cuenta/ });
      fireEvent.change(box, { target: { value: text } });
      fireEvent.click(await screen.findByRole('option', { name: new RegExp(option) }));
    };
    await pick(first, 'bancos', '1.1.02 · Bancos');
    await pick(second, 'caja', '1.1.01 · Caja');
    fireEvent.change(within(first).getByRole('textbox', { name: /^Debe/ }), { target: { value: '300' } });
    fireEvent.change(within(second).getByRole('textbox', { name: /^Haber/ }), { target: { value: '300' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar asiento' }));
    const confirm = await screen.findByRole('alertdialog', { name: '¿Registrar el asiento?' });
    fireEvent.click(within(confirm).getByRole('button', { name: 'Registrar asiento' }));
    expect(await within(confirm).findByText('El período contable de esa fecha está cerrado.')).toBeInTheDocument();
    expect(requests(call, 'CreateJournalEntryCommand')).toHaveLength(1);
    fireEvent.click(within(confirm).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    expect(within(dialog).getByText('El período contable de esa fecha está cerrado.')).toBeInTheDocument();
  });
});

describe('Contabilidad · permisos y tablero', () => {
  it('un rol sin `accounting.manage` ve «No tiene acceso a esta pantalla»', async () => {
    await openAccounting('VENTAS');
    expect(await screen.findByTestId('sin-acceso', undefined, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Nuevo asiento' })).not.toBeInTheDocument();
  });

  it('ofrece al tablero «Libro diario», «Estado de resultados» y «Registrar un asiento» solo a quien lleva la contabilidad', () => {
    const labels = (role: StaffRole) => dashboardActions(REGISTRY, ROLE_PERMISSIONS[role]).flatMap((item) => item.actions.filter((entry) => entry.module.key === 'contabilidad').map((entry) => `${entry.action.label} → ${entry.to}`));
    expect(labels('GERENCIA')).toEqual(['Libro diario → /panel/contabilidad?vista=diario', 'Estado de resultados → /panel/contabilidad', 'Registrar un asiento → /panel/contabilidad?nuevo=asiento']);
    expect(labels('CONSULTA')).toEqual([]);
  });
});
