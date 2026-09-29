// Esqueleto del panel sobre un registro de MUESTRA con los módulos del diseño §7 (secciones y permisos): el menú de cada
// rol (matriz de la empresa de prueba) muestra solo lo que sus permisos permiten; una pantalla sin permiso dice qué falta
// en palabras; una dirección sin pantalla se avisa dentro del panel; migas, secciones plegables, buscador, cajón del
// teléfono, menú del usuario y cambio de sucursal (SelectBranchCommand → la sesión y los datos de la pantalla se vuelven
// a leer). Sesión y RPC en memoria: ninguna prueba toca la red.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { Boxes } from 'lucide-react';
import { describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import { useSession } from '@/4-presentation/hooks/useSession';
import { renderPanel, signedInAs, type StaffRole } from '@/test-utils';
import { useRpcQuery } from '../hooks';
import { buildRegistry, type PanelModuleDefinition, type PanelSectionKey, type PermissionRule } from '../registry';
import { PanelApp } from './PanelApp';

const CM = '5d0c1f6e-0a51-4d0e-9d11-000000000001';
const CB = '5d0c1f6e-0a51-4d0e-9d11-000000000002';

function titled(title: string) {
  return function FakeScreen() {
    return <h1>{title}</h1>;
  };
}

/** Pantalla que consulta el servidor y muestra la sucursal activa (para ver que se vuelve a leer al cambiarla). */
function BranchProbe() {
  const { session } = useSession();
  const activity = useRpcQuery('GetActivityQuery', { take: 3 });
  const active = session?.access.branches.find((branch) => branch.id === session.access.activeBranchId);
  return (
    <>
      <h1>Sonda</h1>
      <p data-testid="sonda-sucursal">{active?.code ?? 'todas'}</p>
      <p data-testid="sonda-filas">{activity.data ? activity.data.length : '…'}</p>
    </>
  );
}

type Row = [key: string, section: PanelSectionKey, title: string, permissions: PermissionRule];

/** Los módulos del diseño §7 (mismas secciones y permisos), con pantallas de muestra. */
const MODULES: Row[] = [
  ['inicio', 'general', 'Inicio', {}],
  ['caja', 'ventas', 'Caja', { all: ['sales.pos.operate'] }],
  ['ventas', 'ventas', 'Ventas', { all: ['sales.view'] }],
  ['clientes', 'ventas', 'Clientes', { all: ['sales.customers.manage'] }],
  ['reservas', 'ventas', 'Reservas', { all: ['sales.pcbuild.manage'] }],
  ['armador', 'tecnologia', 'Armador de PC', { all: ['sales.view', 'inventory.stock.view'] }],
  ['series', 'tecnologia', 'Series', { all: ['inventory.serials.view'] }],
  ['garantias', 'tecnologia', 'Garantías', { all: ['inventory.serials.view'] }],
  ['stock', 'inventario', 'Stock', { all: ['inventory.stock.view'] }],
  ['catalogo', 'inventario', 'Catálogo', { all: ['catalog.manage'] }],
  ['movimientos', 'inventario', 'Movimientos', { any: ['inventory.movements.register.warehouse', 'inventory.movements.register.sales'] }],
  ['toma-fisica', 'inventario', 'Toma física', { all: ['inventory.counts.record'] }],
  ['alertas', 'inventario', 'Alertas', { all: ['inventory.stock.view'] }],
  ['pedido-sugerido', 'compras', 'Pedido sugerido', { all: ['inventory.stock.view'] }],
  ['ordenes-compra', 'compras', 'Órdenes de compra', { all: ['purchasing.manage'] }],
  ['proveedores', 'compras', 'Proveedores', { all: ['purchasing.manage'] }],
  ['sucursales', 'sucursales', 'Sucursales', { any: ['reports.view', 'corporate.branches.all', 'corporate.branches.manage'] }],
  ['transferencias', 'sucursales', 'Transferencias', { all: ['inventory.transfers.manage'] }],
  ['documentos', 'facturacion', 'Documentos', { all: ['billing.view'] }],
  ['siat', 'facturacion', 'Estado del SIAT', { all: ['billing.view'] }],
  ['homologacion', 'facturacion', 'Homologación', { all: ['billing.view'] }],
  ['libros', 'facturacion', 'Libros', { all: ['billing.view'] }],
  ['reportes', 'analisis', 'Reportes', { all: ['reports.view'] }],
  ['contabilidad', 'analisis', 'Contabilidad', { all: ['accounting.manage'] }],
  ['usuarios', 'administracion', 'Usuarios', { all: ['iam.users.manage'] }],
  ['integraciones', 'administracion', 'Integraciones', { all: ['integration.manage'] }],
  ['configuracion', 'administracion', 'Configuración', { all: ['billing.configure'] }],
  ['actividad', 'administracion', 'Actividad', { all: ['iam.audit.view'] }],
  ['sonda', 'general', 'Sonda', {}],
];

const REGISTRY = buildRegistry(
  MODULES.map(([key, section, title, permissions], index): { source: string; definition: PanelModuleDefinition } => ({
    source: `../modules/${key}/module.tsx`,
    definition: {
      key,
      section,
      title,
      description: `Pantalla de muestra de ${title}.`,
      icon: Boxes,
      order: index,
      permissions,
      routes: [{ path: '', title, element: key === 'sonda' ? BranchProbe : titled(title) }],
    },
  })),
);

async function renderShell(role: StaffRole, route = '/panel') {
  const web = await signedInAs(role);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route, path: '/panel/*' });
  await screen.findByTestId('panel-esqueleto');
  return { ...view, web };
}

/** El menú tal como se ve: cada sección con sus módulos. */
function menu(): [string, string[]][] {
  const nav = screen.getAllByRole('navigation', { name: 'Menú del panel' })[0];
  return Array.from(nav.querySelectorAll(':scope > ul > li')).map((item) => [
    item.querySelector('button')?.textContent ?? '',
    Array.from(item.querySelectorAll('a')).map((link) => link.textContent ?? ''),
  ]);
}

const EXPECTED_MENUS: Record<StaffRole, [string, string[]][]> = {
  ADMIN: [
    ['General', ['Inicio', 'Sonda']],
    ['Ventas', ['Caja', 'Ventas', 'Clientes', 'Reservas']],
    ['Tecnología', ['Armador de PC', 'Series', 'Garantías']],
    ['Inventario', ['Stock', 'Catálogo', 'Movimientos', 'Toma física', 'Alertas']],
    ['Compras', ['Pedido sugerido', 'Órdenes de compra', 'Proveedores']],
    ['Sucursales', ['Sucursales', 'Transferencias']],
    ['Facturación', ['Documentos', 'Estado del SIAT', 'Homologación', 'Libros']],
    ['Análisis', ['Reportes', 'Contabilidad']],
    ['Administración', ['Usuarios', 'Integraciones', 'Configuración', 'Actividad']],
  ],
  BODEGA: [
    ['General', ['Inicio', 'Sonda']],
    ['Tecnología', ['Series', 'Garantías']],
    ['Inventario', ['Stock', 'Movimientos', 'Toma física', 'Alertas']],
    ['Compras', ['Pedido sugerido', 'Órdenes de compra', 'Proveedores']],
    ['Sucursales', ['Sucursales', 'Transferencias']],
    ['Análisis', ['Reportes']],
  ],
  VENTAS: [
    ['General', ['Inicio', 'Sonda']],
    ['Ventas', ['Caja', 'Ventas', 'Clientes', 'Reservas']],
    ['Tecnología', ['Armador de PC', 'Series', 'Garantías']],
    ['Inventario', ['Stock', 'Movimientos', 'Alertas']],
    ['Compras', ['Pedido sugerido']],
    ['Sucursales', ['Sucursales']],
    ['Facturación', ['Documentos', 'Estado del SIAT', 'Homologación', 'Libros']],
    ['Análisis', ['Reportes']],
  ],
  CAJERO: [
    ['General', ['Inicio', 'Sonda']],
    ['Ventas', ['Caja', 'Ventas', 'Clientes', 'Reservas']],
    ['Tecnología', ['Armador de PC', 'Series', 'Garantías']],
    ['Inventario', ['Stock', 'Movimientos', 'Alertas']],
    ['Compras', ['Pedido sugerido']],
    ['Facturación', ['Documentos', 'Estado del SIAT', 'Homologación', 'Libros']],
  ],
  GERENCIA: [
    ['General', ['Inicio', 'Sonda']],
    ['Ventas', ['Ventas', 'Reservas']],
    ['Tecnología', ['Armador de PC', 'Series', 'Garantías']],
    ['Inventario', ['Stock', 'Alertas']],
    ['Compras', ['Pedido sugerido', 'Órdenes de compra', 'Proveedores']],
    ['Sucursales', ['Sucursales', 'Transferencias']],
    ['Facturación', ['Documentos', 'Estado del SIAT', 'Homologación', 'Libros']],
    ['Análisis', ['Reportes', 'Contabilidad']],
    ['Administración', ['Actividad']],
  ],
  CONSULTA: [
    ['General', ['Inicio', 'Sonda']],
    ['Tecnología', ['Series', 'Garantías']],
    ['Inventario', ['Stock', 'Alertas']],
    ['Compras', ['Pedido sugerido']],
    ['Sucursales', ['Sucursales']],
    ['Facturación', ['Documentos', 'Estado del SIAT', 'Homologación', 'Libros']],
    ['Análisis', ['Reportes']],
  ],
};

describe('esqueleto del panel · menú por rol', () => {
  it.each(Object.keys(EXPECTED_MENUS) as StaffRole[])('el menú de %s muestra solo lo que sus permisos permiten', async (role) => {
    await renderShell(role);
    expect(menu()).toEqual(EXPECTED_MENUS[role]);
  });

  it('el módulo abierto queda marcado en el menú y el tablero lo marca solo en /panel', async () => {
    await renderShell('ADMIN', '/panel/actividad');
    const nav = screen.getByRole('navigation', { name: 'Menú del panel' });
    expect(within(nav).getByRole('link', { name: 'Actividad' })).toHaveAttribute('aria-current', 'page');
    expect(within(nav).getByRole('link', { name: 'Inicio' })).not.toHaveAttribute('aria-current');
  });

  it('las secciones se pliegan y se despliegan (aria-expanded)', async () => {
    await renderShell('VENTAS');
    const nav = screen.getByRole('navigation', { name: 'Menú del panel' });
    const section = within(nav).getByRole('button', { name: 'Ventas' });
    expect(section).toHaveAttribute('aria-expanded', 'true');
    fireEvent.click(section);
    expect(section).toHaveAttribute('aria-expanded', 'false');
    expect(within(nav).queryByRole('link', { name: 'Caja' })).not.toBeInTheDocument();
    fireEvent.click(section);
    expect(within(nav).getByRole('link', { name: 'Caja' })).toBeInTheDocument();
  });

  it('el buscador de pantallas filtra sin acentos y Enter abre la primera', async () => {
    const view = await renderShell('BODEGA');
    const search = screen.getByRole('searchbox', { name: 'Buscar una pantalla' });
    fireEvent.change(search, { target: { value: 'toma fisica' } });
    const results = screen.getByRole('list', { name: 'Pantallas encontradas' });
    expect(within(results).getAllByRole('link')).toHaveLength(1);
    expect(within(results).getByRole('link')).toHaveTextContent('Toma física');
    expect(screen.getByText('1 pantalla encontrada')).toBeInTheDocument();
    fireEvent.keyDown(search, { key: 'Enter' });
    await waitFor(() => expect(view.location()).toBe('/panel/toma-fisica'));
    expect(search).toHaveValue('');
    // Lo que el rol no ve tampoco aparece en el buscador.
    fireEvent.change(search, { target: { value: 'caja' } });
    expect(screen.getByText('Sin resultados para «caja».')).toBeInTheDocument();
  });
});

describe('esqueleto del panel · acceso y direcciones', () => {
  it('una pantalla sin permiso muestra «No tiene acceso a esta pantalla» con el permiso que falta en palabras', async () => {
    await renderShell('VENTAS', '/panel/actividad');
    const denied = await screen.findByTestId('sin-acceso');
    expect(within(denied).getByRole('heading', { level: 1 })).toHaveTextContent('No tiene acceso a esta pantalla');
    expect(denied).toHaveTextContent('Administración › Actividad');
    expect(denied).toHaveTextContent('Falta el permiso «Consultar la auditoría y la actividad». Pida al administrador que se lo asigne.');
    expect(within(denied).getByRole('link', { name: 'Volver al inicio del panel' })).toHaveAttribute('href', '/panel');
  });

  it('si basta uno de varios permisos, lo dice así', async () => {
    await renderShell('CONSULTA', '/panel/movimientos');
    expect(await screen.findByTestId('sin-acceso')).toHaveTextContent(
      'Necesita al menos uno de estos permisos: «Registrar entradas, saldo inicial y ajustes» o «Registrar salidas».',
    );
  });

  it('una dirección que no es de ninguna pantalla se avisa dentro del panel', async () => {
    const view = await renderShell('ADMIN', '/panel/no-existe/tampoco');
    expect(await screen.findByTestId('pantalla-no-encontrada')).toHaveTextContent('No encontramos esta pantalla');
    expect(screen.getByRole('navigation', { name: 'Menú del panel' })).toBeInTheDocument();
    expect(view.location()).toBe('/panel/no-existe/tampoco');
  });

  it('migas de pan: Inicio › Sección › Módulo (la sección no es un enlace)', async () => {
    await renderShell('ADMIN', '/panel/actividad');
    const crumbs = screen.getByRole('navigation', { name: 'Migas de pan' });
    expect(within(crumbs).getAllByRole('link', { name: 'Inicio' })[0]).toHaveAttribute('href', '/panel');
    expect(within(crumbs).getByText('Administración').tagName).toBe('SPAN');
    expect(within(crumbs).getByText('Actividad')).toHaveAttribute('aria-current', 'page');
  });

  it('al cambiar de pantalla el foco pasa al contenido', async () => {
    const view = await renderShell('ADMIN');
    fireEvent.click(within(screen.getByRole('navigation', { name: 'Menú del panel' })).getByRole('link', { name: 'Stock' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Stock' })).toBeInTheDocument();
    expect(view.location()).toBe('/panel/stock');
    await waitFor(() => expect(document.activeElement).toBe(document.getElementById('contenido')));
  });

  it('en el teléfono el menú es un cajón que se cierra al elegir una pantalla', async () => {
    const view = await renderShell('CAJERO');
    const button = screen.getByRole('button', { name: 'Abrir el menú del panel' });
    fireEvent.click(button);
    const drawer = await screen.findByRole('dialog', { name: 'Menú del panel' });
    fireEvent.click(within(drawer).getByRole('link', { name: 'Clientes' }));
    await waitFor(() => expect(view.location()).toBe('/panel/clientes'));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Menú del panel' })).not.toBeInTheDocument());
  });

  it('en desarrollo avisa arriba los módulos que no se pudieron cargar', async () => {
    const broken = buildRegistry([
      { source: '../modules/inicio/module.tsx', definition: { key: 'inicio', section: 'general', title: 'Inicio', description: 'x', icon: Boxes, order: 0, permissions: {}, routes: [{ path: '', title: 'Inicio', element: titled('Inicio') }] } },
      { source: '../modules/roto/module.tsx', definition: { key: 'roto' } },
    ]);
    await renderPanel(<PanelApp registry={broken} />, { path: '/panel/*' });
    expect(await screen.findByText('Hay módulos del panel que no se cargaron (aviso de desarrollo)')).toBeInTheDocument();
    expect(screen.getByText(/Módulo «roto» \(modules\/roto\/module\.tsx\): falta «title»/)).toBeInTheDocument();
  });
});

describe('esqueleto del panel · usuario y sucursal', () => {
  it('muestra el nombre y el rol con el menú: cambiar contraseña, ir a la tienda y cerrar sesión', async () => {
    await renderShell('BODEGA', '/panel/stock');
    const button = screen.getByRole('button', { name: 'Cuenta de Bruno Mamani (Bodega)' });
    expect(within(button).getByTestId('usuario-nombre')).toHaveTextContent('Bruno Mamani');
    expect(within(button).getByTestId('usuario-rol')).toHaveTextContent('Bodega');
    fireEvent.click(button);
    const menuBox = screen.getByRole('menu');
    expect(within(menuBox).getByRole('menuitem', { name: 'Cambiar contraseña' })).toHaveAttribute('href', '/cambiar-contrasena?volver=%2Fpanel%2Fstock');
    expect(within(menuBox).getByRole('menuitem', { name: 'Ir a la tienda' })).toHaveAttribute('href', '/');
    expect(within(menuBox).getByRole('menuitem', { name: 'Cerrar sesión' })).toBeInTheDocument();
    expect(document.activeElement).toBe(within(menuBox).getAllByRole('menuitem')[0]);
    fireEvent.keyDown(menuBox, { key: 'Escape' });
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(document.activeElement).toBe(button);
  });

  it('con más de una sucursal, cambiarla envía SelectBranchCommand, vuelve a leer la sesión y los datos de la pantalla', async () => {
    const { web } = await renderShell('ADMIN', '/panel/sonda');
    const call = vi.spyOn(web.backend.rpc, 'call');
    const select = screen.getByRole('combobox', { name: 'Sucursal activa' });
    expect(select).toHaveValue(CM);
    expect(within(select).getAllByRole('option').map((option) => option.textContent)).toEqual([
      'Todas las sucursales',
      'CM · Casa matriz La Paz · Av. 16 de Julio (El Prado)',
      'CB · Sucursal Cochabamba',
      'SC · Sucursal Santa Cruz',
    ]);
    await waitFor(() => expect(screen.getByTestId('sonda-filas')).not.toHaveTextContent('…'));
    expect(screen.getByTestId('sonda-sucursal')).toHaveTextContent('CM');

    await act(async () => {
      fireEvent.change(select, { target: { value: CB } });
    });
    await waitFor(() => expect(screen.getByTestId('sonda-sucursal')).toHaveTextContent('CB'));
    expect(select).toHaveValue(CB);
    const selectCall = call.mock.calls.find(([operation]) => operation === 'SelectBranchCommand');
    expect(selectCall?.[1]).toEqual({ sessionId: '00000000-0000-0000-0000-000000000000', branchId: CB });
    expect(selectCall?.[2]).toMatchObject({ requestId: expect.any(String) });
    // La pantalla volvió a consultar con la sucursal nueva.
    await waitFor(() => expect(call.mock.calls.filter(([operation]) => operation === 'GetActivityQuery').length).toBeGreaterThanOrEqual(1));
    expect(await screen.findByText('Sucursal activa cambiada')).toBeInTheDocument();
    expect((await web.backend.session.current())?.access.activeBranchId).toBe(CB);
  });

  it('con una sola sucursal se muestra como texto (sin lista)', async () => {
    await renderShell('BODEGA');
    expect(screen.getByTestId('sucursal-unica')).toHaveTextContent('Sucursal: CM');
    expect(screen.queryByRole('combobox', { name: 'Sucursal activa' })).not.toBeInTheDocument();
  });

  it('la gerencia global puede ver todas las sucursales juntas', async () => {
    await renderShell('GERENCIA');
    const select = screen.getByRole('combobox', { name: 'Sucursal activa' });
    expect(select).toHaveValue('');
    expect(within(select).getByRole('option', { selected: true })).toHaveTextContent('Todas las sucursales');
  });

  it('si el servidor rechaza el cambio, se avisa y la sucursal no cambia', async () => {
    const { web } = await renderShell('VENTAS');
    const select = screen.getByRole('combobox', { name: 'Sucursal activa' });
    expect(within(select).getAllByRole('option').map((option) => option.textContent)).toEqual(['Elija una sucursal', 'CM · Casa matriz La Paz · Av. 16 de Julio (El Prado)', 'CB · Sucursal Cochabamba']);
    vi.spyOn(web.backend.rpc, 'call').mockRejectedValueOnce(new WebApiError({ kind: 'access_denied', status: 403, message: 'La sucursal elegida no está entre las suyas.' }));
    await act(async () => {
      fireEvent.change(select, { target: { value: CB } });
    });
    expect(await screen.findByText('No se pudo cambiar la sucursal')).toBeInTheDocument();
    expect(select).toHaveValue(CM);
  });
});
