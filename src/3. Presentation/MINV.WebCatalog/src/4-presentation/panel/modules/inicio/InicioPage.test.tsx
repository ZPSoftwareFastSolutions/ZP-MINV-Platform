// Tablero «Inicio»: saludo con el nombre, el rol y la sucursal activa; los botones que ofrecen los módulos a los que la
// sesión tiene acceso, agrupados por sección; y «Ver estadísticas» PLEGADO: al entrar no se ve ni se carga ninguna
// estadística; al abrirlo, cada una se descarga y consulta por separado, con su propio estado (una que falla no tapa a las
// demás). Con el módulo «inicio» REAL y módulos de muestra; sesión y RPC en memoria.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { History, PackageSearch, ShoppingCart } from 'lucide-react';
import { useEffect } from 'react';
import { beforeAll, describe, expect, it, vi } from 'vitest';
import { preloadPanel, renderPanel, renderRoutes, signedInAs, type StaffRole } from '@/test-utils';
import { useRpcQuery } from '../../hooks';
import { buildRegistry, type PanelModuleDefinition } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import inicio from './module';

/** Avisa cada vez que se monta la estadística de muestra. */
const statMounted = vi.fn();

/** Estadística que consulta al servidor (avisa cuándo se monta). */
function SalesStat() {
  useEffect(() => statMounted(), []);
  const activity = useRpcQuery('GetActivityQuery', { take: 4 });
  return <p data-testid="ventas-hoy">{activity.data ? `${activity.data.length} filas` : 'Cargando…'}</p>;
}

/** Estadística que falla al dibujarse: no debe tapar a las demás. */
function BrokenStat(): never {
  throw new Error('Falla de prueba');
}

const Screen = () => <h1>Pantalla</h1>;

const MODULES: PanelModuleDefinition[] = [
  inicio,
  {
    key: 'ventas',
    section: 'ventas',
    title: 'Ventas',
    description: 'Historial de ventas.',
    icon: ShoppingCart,
    order: 20,
    permissions: { all: ['sales.view'] },
    routes: [
      { path: '', title: 'Ventas', element: Screen },
      { path: 'nueva', title: 'Nueva venta', element: Screen },
    ],
    actions: [
      { key: 'ver', label: 'Ver las ventas', description: 'Historial con filtros.', icon: ShoppingCart, to: '' },
      { key: 'nueva', label: 'Nueva venta', description: 'Cobrar en la caja.', icon: ShoppingCart, to: 'nueva', permissions: { all: ['sales.pos.operate'] } },
    ],
    stats: [{ key: 'hoy', title: 'Ventas de hoy', component: SalesStat }],
  },
  {
    key: 'stock',
    section: 'inventario',
    title: 'Stock',
    description: 'Existencias.',
    icon: PackageSearch,
    order: 10,
    permissions: { all: ['inventory.stock.view'] },
    routes: [{ path: '', title: 'Stock', element: Screen }],
    actions: [{ key: 'ver', label: 'Consultar stock', description: 'Existencias por sucursal.', icon: PackageSearch, to: '' }],
    stats: [{ key: 'bajo', title: 'Stock bajo', component: BrokenStat }],
  },
  {
    key: 'auditoria',
    section: 'administracion',
    title: 'Auditoría',
    description: 'Quién hizo qué.',
    icon: History,
    order: 10,
    permissions: { all: ['iam.audit.view'] },
    routes: [{ path: '', title: 'Auditoría', element: Screen }],
    actions: [{ key: 'ver', label: 'Ver la auditoría', description: 'Operaciones del día.', icon: History, to: '?resultado=Rejected' }],
  },
];

const REGISTRY = buildRegistry(MODULES.map((definition) => ({ source: `../modules/${definition.key}/module.tsx`, definition })));

async function renderHome(role: StaffRole, registry = REGISTRY) {
  statMounted.mockClear();
  const web = await signedInAs(role);
  const call = vi.spyOn(web.backend.rpc, 'call');
  await renderPanel(<PanelApp registry={registry} />, { web: web.services, route: '/panel', path: '/panel/*' });
  await screen.findByRole('heading', { level: 1, name: /^Hola, / }, { timeout: 5000 });
  return { web, call };
}

function actionGroups(): [string, [string, string | null][]][] {
  return screen.getAllByTestId('grupo-de-acciones').map((group) => [
    within(group).getByRole('heading', { level: 3 }).textContent ?? '',
    within(group)
      .getAllByRole('link')
      .map((link) => [link.getAttribute('aria-labelledby') ? (document.getElementById(link.getAttribute('aria-labelledby')!.split(' ')[0])?.textContent ?? '') : '', link.getAttribute('href')]),
  ]);
}

beforeAll(async () => {
  await preloadPanel();
  await import('./InicioPage');
}, 30_000);

describe('Inicio · saludo y botones', () => {
  it('saluda con el nombre, el rol y la sucursal activa', async () => {
    await renderHome('ADMIN');
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Hola, Andrea Quiroga');
    const greeting = screen.getByTestId('saludo');
    expect(greeting).toHaveTextContent('Rol: Administrador');
    expect(greeting).toHaveTextContent('Sucursal activa: CM · Casa matriz La Paz · Av. 16 de Julio (El Prado)');
  });

  it('la gerencia global sin sucursal elegida ve la vista consolidada', async () => {
    await renderHome('GERENCIA');
    expect(screen.getByTestId('saludo')).toHaveTextContent('Rol: Gerencia');
    expect(screen.getByTestId('saludo')).toHaveTextContent('Sucursal activa: Todas las sucursales (vista consolidada)');
  });

  it('«¿Qué quiere hacer?» muestra los botones de los módulos con acceso, agrupados por sección y en el orden del menú', async () => {
    await renderHome('ADMIN');
    expect(screen.getByRole('heading', { level: 2, name: '¿Qué quiere hacer?' })).toBeInTheDocument();
    expect(actionGroups()).toEqual([
      [
        'Ventas',
        [
          ['Ver las ventas', '/panel/ventas'],
          ['Nueva venta', '/panel/ventas/nueva'],
        ],
      ],
      ['Inventario', [['Consultar stock', '/panel/stock']]],
      ['Administración', [['Ver la auditoría', '/panel/auditoria?resultado=Rejected']]],
    ]);
  });

  it('cada rol ve solo sus botones (un botón con permiso propio también se oculta)', async () => {
    await renderHome('CONSULTA');
    expect(actionGroups()).toEqual([['Inventario', [['Consultar stock', '/panel/stock']]]]);
  });

  it('sin módulos que ofrezcan botones, el tablero se ve bien igual (y sin «Ver estadísticas»)', async () => {
    await renderHome('ADMIN', buildRegistry([{ source: '../modules/inicio/module.tsx', definition: inicio }]));
    expect(screen.getByText('Todavía no hay funciones para su rol')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Ver estadísticas/ })).not.toBeInTheDocument();
  });
});

describe('Inicio · estadísticas plegadas', () => {
  it('al entrar NO se ve ni se carga ninguna estadística', async () => {
    const { call } = await renderHome('ADMIN');
    const toggle = screen.getByRole('button', { name: /Ver estadísticas/ });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByTestId('plegable-simbolo')).toHaveClass('rotate-180');
    expect(screen.queryAllByTestId('estadistica')).toHaveLength(0);
    expect(statMounted).not.toHaveBeenCalled();
    expect(call.mock.calls.filter(([operation]) => operation === 'GetActivityQuery')).toHaveLength(0);
  });

  it('al abrir, cada estadística carga por separado y con su propio estado (una que falla no tapa a las otras)', async () => {
    const { call } = await renderHome('ADMIN');
    // React avisa en la consola del error de la estadística rota (lo atrapa su límite): se silencia en esta prueba.
    const quiet = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    try {
      await act(async () => {
        fireEvent.click(screen.getByRole('button', { name: /Ver estadísticas/ }));
      });
      expect(screen.getByRole('button', { name: /Ocultar estadísticas/ })).toHaveAttribute('aria-expanded', 'true');
      const panels = screen.getAllByTestId('estadistica');
      expect(panels.map((panel) => within(panel).getByRole('heading', { level: 3 }).textContent)).toEqual(['Ventas de hoy', 'Stock bajo']);
      expect(await within(panels[0]).findByTestId('ventas-hoy')).toHaveTextContent('4 filas');
      expect(within(panels[1]).getByRole('alert')).toHaveTextContent('No se pudo mostrar esta estadística.');
      expect(call.mock.calls.filter(([operation]) => operation === 'GetActivityQuery')).toHaveLength(1);
      // Plegar de nuevo desmonta las estadísticas.
      fireEvent.click(screen.getByRole('button', { name: /Ocultar estadísticas/ }));
      expect(screen.queryAllByTestId('estadistica')).toHaveLength(0);
    } finally {
      quiet.mockRestore();
    }
  });

  it('solo aparecen las estadísticas de los módulos a los que el rol tiene acceso', async () => {
    await renderHome('CONSULTA');
    const quiet = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    try {
      fireEvent.click(screen.getByRole('button', { name: /Ver estadísticas/ }));
      await waitFor(() => expect(screen.getAllByTestId('estadistica')).toHaveLength(1));
      expect(screen.getByRole('heading', { level: 3, name: 'Stock bajo' })).toBeInTheDocument();
    } finally {
      quiet.mockRestore();
    }
  });
});

describe('Inicio · con los módulos del proyecto', () => {
  it('el administrador ve los botones de «Actividad» y su estadística plegada, que consulta recién al abrirla', async () => {
    const web = await signedInAs('ADMIN');
    const call = vi.spyOn(web.backend.rpc, 'call');
    await renderRoutes({ web: web.services, route: '/panel' });
    expect(await screen.findByRole('heading', { level: 1, name: 'Hola, Andrea Quiroga' }, { timeout: 5000 })).toBeInTheDocument();
    const admin = screen.getAllByTestId('grupo-de-acciones').find((group) => within(group).getByRole('heading', { level: 3 }).textContent === 'Administración');
    expect(admin).toBeDefined();
    expect(within(admin!).getAllByRole('link').map((link) => link.getAttribute('href'))).toEqual([
      '/panel/usuarios?accion=nuevo',
      '/panel/usuarios?accion=restablecer',
      '/panel/integraciones?pestana=correos',
      '/panel/configuracion?pestana=facturacion',
      '/panel/actividad',
      '/panel/actividad?resultado=Rejected',
    ]);
    expect(call.mock.calls.some(([operation]) => operation === 'GetActivityQuery')).toBe(false);

    fireEvent.click(screen.getByRole('button', { name: /Ver estadísticas/ }));
    const stat = await screen.findByTestId('actividad-de-hoy', undefined, { timeout: 5000 });
    await waitFor(() => expect(within(stat).queryByText(/^Cargando/)).not.toBeInTheDocument());
    expect(call.mock.calls.filter(([operation]) => operation === 'GetActivityQuery').map(([, payload]) => payload)).toEqual([{ take: 1000 }]);
    expect(within(stat).getByRole('link', { name: 'Ver la actividad de hoy' }).getAttribute('href')).toMatch(/^\/panel\/actividad\?desde=\d{4}-\d{2}-\d{2}&hasta=\d{4}-\d{2}-\d{2}$/);
  });

  it('un rol sin «Actividad» no ve sus botones ni su estadística', async () => {
    const web = await signedInAs('CAJERO');
    await renderRoutes({ web: web.services, route: '/panel' });
    expect(await screen.findByRole('heading', { level: 1, name: 'Hola, Diego Flores' }, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Ver la actividad/ })).not.toBeInTheDocument();
    
    // CAJERO tiene botones ahora (Caja, Ventas), pero no los de Actividad
    const estadisticasToggle = screen.queryByRole('button', { name: /Ver estadísticas/ });
    if (estadisticasToggle) {
        // En algunos entornos el botón puede existir si tienen métricas. Abrimos.
        const { fireEvent, waitFor } = await import('@testing-library/react');
        fireEvent.click(estadisticasToggle);
        await waitFor(() => expect(screen.getAllByTestId('estadistica').length).toBeGreaterThan(0));
    }
    expect(screen.queryByTestId('actividad-de-hoy')).not.toBeInTheDocument();
  });
});
