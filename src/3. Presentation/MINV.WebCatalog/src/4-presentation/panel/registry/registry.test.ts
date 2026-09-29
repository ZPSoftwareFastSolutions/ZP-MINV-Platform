// Registro de módulos del panel: valida cada definición (un módulo con errores no se carga y el problema queda dicho en
// palabras), ordena por sección, `order` y título, y responde qué ve cada sesión (permisos `any`/`all`): menú, botones
// y estadísticas del tablero, buscador de pantallas, migas y acceso a una pantalla. También revisa los módulos REALES
// del proyecto: si un módulo nuevo tiene un error, esta prueba falla con el motivo.

import { Activity, Boxes, History } from 'lucide-react';
import { describe, expect, it } from 'vitest';
import { PANEL_REGISTRY } from './discovery';
import {
  allows,
  buildRegistry,
  canSeeModule,
  dashboardActions,
  dashboardStats,
  locate,
  menuSections,
  missingFor,
  missingText,
  modulePath,
  panelScreens,
  screenAccess,
  searchScreens,
  type PanelModuleDefinition,
  type RegistryEntry,
} from './index';

const Screen = () => null;

function definition(overrides: Partial<PanelModuleDefinition> & Pick<PanelModuleDefinition, 'key'>): PanelModuleDefinition {
  return {
    section: 'ventas',
    title: `Módulo ${overrides.key}`,
    description: 'Para las pruebas.',
    icon: Boxes,
    order: 10,
    permissions: {},
    routes: [{ path: '', title: `Pantalla ${overrides.key}`, element: Screen }],
    ...overrides,
  };
}

function entry(module: PanelModuleDefinition | unknown, folder?: string): RegistryEntry {
  const key = folder ?? (module as { key?: string })?.key ?? 'sin-clave';
  return { source: `../modules/${key}/module.tsx`, definition: module };
}

const INICIO = definition({ key: 'inicio', section: 'general', title: 'Inicio', order: 0 });
const CAJA = definition({
  key: 'caja',
  title: 'Caja',
  order: 10,
  permissions: { all: ['sales.pos.operate'] },
  actions: [{ key: 'abrir', label: 'Abrir caja', description: 'Cobrar ventas.', icon: Activity, to: '' }],
});
const VENTAS = definition({
  key: 'ventas',
  title: 'Ventas',
  order: 20,
  permissions: { any: ['sales.view', 'reports.view'] },
  routes: [
    { path: '', title: 'Ventas', element: Screen },
    { path: 'nueva', title: 'Nueva venta', element: Screen, permissions: { all: ['sales.pos.operate'] } },
    { path: ':numero', title: 'Detalle de la venta', element: Screen },
  ],
  actions: [
    { key: 'ver', label: 'Ver las ventas', description: 'Historial.', icon: Activity, to: '?estado=pagada' },
    { key: 'nueva', label: 'Nueva venta', description: 'Cobrar.', icon: Activity, to: 'nueva', permissions: { all: ['sales.pos.operate'] } },
  ],
  stats: [
    { key: 'hoy', title: 'Ventas de hoy', component: Screen },
    { key: 'margen', title: 'Margen', component: Screen, permissions: { all: ['accounting.manage'] } },
  ],
});
const ARMADOR = definition({ key: 'armador', section: 'tecnologia', title: 'Armador de PC', order: 10, permissions: { all: ['sales.view', 'inventory.stock.view'] } });
const ACTIVIDAD = definition({
  key: 'actividad',
  section: 'administracion',
  title: 'Actividad',
  order: 40,
  icon: History,
  description: 'Auditoría: quién hizo qué.',
  permissions: { all: ['iam.audit.view'] },
});

const REGISTRY = buildRegistry([ACTIVIDAD, VENTAS, INICIO, ARMADOR, CAJA].map((module) => entry(module)));

describe('registro · validación', () => {
  it('carga los módulos válidos, sin problemas, y ubica el tablero en /panel', () => {
    expect(REGISTRY.problems).toEqual([]);
    expect(REGISTRY.home?.key).toBe('inicio');
    expect(REGISTRY.home?.basePath).toBe('/panel');
    expect(REGISTRY.modules.find((module) => module.key === 'caja')?.basePath).toBe('/panel/caja');
  });

  it('ordena por sección (la del diseño), después por `order` y después por título', () => {
    expect(REGISTRY.modules.map((module) => module.key)).toEqual(['inicio', 'caja', 'ventas', 'armador', 'actividad']);
    const tied = buildRegistry([definition({ key: 'zeta', title: 'Zeta', order: 5 }), definition({ key: 'alfa', title: 'Alfa', order: 5 }), definition({ key: 'primero', title: 'Último', order: 1 })].map((module) => entry(module)));
    expect(tied.modules.map((module) => module.key)).toEqual(['primero', 'alfa', 'zeta']);
  });

  it('un módulo con errores no se carga y el problema queda en palabras (los demás siguen)', () => {
    const registry = buildRegistry([
      entry(CAJA),
      entry(definition({ key: 'Mal Nombre' }), 'mal-nombre'),
      entry(definition({ key: 'otra-clave' }), 'carpeta'),
      entry(definition({ key: 'seccion', section: 'inventada' as PanelModuleDefinition['section'] })),
      entry(definition({ key: 'permiso', permissions: { all: ['iam.audit.veiw'] } })),
      entry(definition({ key: 'sin-principal', routes: [{ path: 'lista', title: 'Lista', element: Screen }] })),
      entry(definition({ key: 'ruta', routes: [{ path: '/absoluta', title: 'Mal', element: Screen }] })),
      entry(definition({ key: 'boton', actions: [{ key: 'x', label: 'Ir', description: 'Lejos.', icon: Activity, to: '/panel/otra' }] })),
      entry(undefined, 'vacio'),
    ]);
    expect(registry.modules.map((module) => module.key)).toEqual(['caja']);
    const text = registry.problems.join('\n');
    expect(text).toContain('Módulo «mal-nombre»');
    expect(text).toContain('debe ir en minúsculas y con guiones');
    expect(text).toContain('la clave «otra-clave» debe ser igual al nombre de su carpeta («carpeta»)');
    expect(text).toContain('la sección «inventada» no existe');
    expect(text).toContain('el permiso «iam.audit.veiw» no existe en el contrato');
    expect(text).toContain('falta la pantalla principal');
    expect(text).toContain('«path» es relativa al módulo');
    expect(text).toContain('«to» es relativa al módulo');
    expect(text).toContain('Módulo «vacio» (modules/vacio/module.tsx): no exporta por defecto su definición');
  });

  it('dos módulos con la misma clave: se carga el primero y se avisa', () => {
    const registry = buildRegistry([entry(CAJA), { source: '../modules/caja/module.tsx', definition: { ...CAJA, title: 'Otra caja' } }]);
    expect(registry.modules).toHaveLength(1);
    expect(registry.problems.join('\n')).toContain('hay dos módulos con la misma clave');
  });
});

describe('registro · permisos any / all', () => {
  it('`all` exige todos, `any` basta uno, los dos juntos exigen ambas cosas y sin regla alcanza la sesión', () => {
    expect(allows({ all: ['a', 'b'] }, ['a', 'b', 'c'])).toBe(true);
    expect(allows({ all: ['a', 'b'] }, ['a'])).toBe(false);
    expect(allows({ any: ['a', 'b'] }, ['b'])).toBe(true);
    expect(allows({ any: ['a', 'b'] }, ['c'])).toBe(false);
    expect(allows({ all: ['a'], any: ['b', 'c'] }, ['a', 'c'])).toBe(true);
    expect(allows({ all: ['a'], any: ['b', 'c'] }, ['b', 'c'])).toBe(false);
    expect(allows({}, [])).toBe(true);
    expect(allows(undefined, [])).toBe(true);
  });

  it('un módulo sin acceso no aparece en el menú, y una sección sin módulos visibles tampoco', () => {
    const cajero = ['sales.pos.operate', 'sales.view', 'inventory.stock.view'];
    expect(menuSections(REGISTRY, cajero).map((group) => [group.section.title, group.modules.map((module) => module.title)])).toEqual([
      ['General', ['Inicio']],
      ['Ventas', ['Caja', 'Ventas']],
      ['Tecnología', ['Armador de PC']],
    ]);
    const consulta = ['reports.view'];
    expect(menuSections(REGISTRY, consulta).map((group) => group.modules.map((module) => module.key))).toEqual([['inicio'], ['ventas']]);
    expect(canSeeModule(ACTIVIDAD, consulta)).toBe(false);
    expect(canSeeModule(ACTIVIDAD, ['iam.audit.view'])).toBe(true);
  });

  it('con los módulos comerciales informados, un módulo sin licencia no se ve (sin datos de licencia no se filtra)', () => {
    const licensed = definition({ key: 'transferencias', permissions: {}, licenseModules: ['MULTI_BRANCH'] });
    expect(canSeeModule(licensed, [], null)).toBe(true);
    expect(canSeeModule(licensed, [], ['MULTI_BRANCH'])).toBe(true);
    expect(canSeeModule(licensed, [], ['GLOBAL_AUDIT'])).toBe(false);
  });

  it('lo que falta se dice en palabras con los nombres del contrato', () => {
    expect(missingText(missingFor([{ all: ['iam.audit.view'] }], []))).toBe(
      'Falta el permiso «Consultar la auditoría y la actividad». Pida al administrador que se lo asigne.',
    );
    expect(missingText(missingFor([{ any: ['inventory.movements.register.warehouse', 'inventory.movements.register.sales'] }], []))).toBe(
      'Necesita al menos uno de estos permisos: «Registrar entradas, saldo inicial y ajustes» o «Registrar salidas». Pida al administrador que se lo asigne.',
    );
    expect(missingText(missingFor([{ all: ['sales.view', 'inventory.stock.view'] }, { all: ['sales.pos.operate'] }], ['sales.view']))).toBe(
      'Faltan los permisos «Consultar stock, alertas y pedido sugerido» y «Abrir y cerrar caja, vender y cobrar». Pida al administrador que se los asigne.',
    );
  });
});

describe('registro · tablero, buscador y direcciones', () => {
  it('botones del tablero por sección, con los permisos del módulo y del botón y la dirección completa', () => {
    const groups = dashboardActions(REGISTRY, ['sales.view']);
    expect(groups.map((group) => group.section.title)).toEqual(['Ventas']);
    expect(groups[0].actions.map((item) => [item.action.label, item.to])).toEqual([['Ver las ventas', '/panel/ventas?estado=pagada']]);
    const cajero = dashboardActions(REGISTRY, ['sales.view', 'sales.pos.operate']);
    expect(cajero[0].actions.map((item) => [item.action.label, item.to])).toEqual([
      ['Abrir caja', '/panel/caja'],
      ['Ver las ventas', '/panel/ventas?estado=pagada'],
      ['Nueva venta', '/panel/ventas/nueva'],
    ]);
    expect(dashboardActions(REGISTRY, [])).toEqual([]);
  });

  it('estadísticas del tablero solo de módulos visibles y con su permiso', () => {
    expect(dashboardStats(REGISTRY, ['sales.view']).map((item) => item.stat.title)).toEqual(['Ventas de hoy']);
    expect(dashboardStats(REGISTRY, ['sales.view', 'accounting.manage']).map((item) => item.stat.title)).toEqual(['Ventas de hoy', 'Margen']);
    expect(dashboardStats(REGISTRY, ['iam.audit.view'])).toEqual([]);
  });

  it('el buscador encuentra módulos y pantallas sin acentos, y omite las que piden un dato o un permiso', () => {
    const screens = panelScreens(REGISTRY, ['sales.view', 'sales.pos.operate', 'iam.audit.view']);
    expect(screens.map((screen) => screen.to)).toEqual(['/panel', '/panel/caja', '/panel/ventas', '/panel/ventas/nueva', '/panel/actividad']);
    expect(searchScreens(screens, 'auditoria').map((screen) => screen.title)).toEqual(['Actividad']);
    expect(searchScreens(screens, 'NUEVA venta').map((screen) => [screen.title, screen.context])).toEqual([['Nueva venta', 'Ventas › Ventas']]);
    expect(searchScreens(screens, '   ')).toEqual([]);
    expect(panelScreens(REGISTRY, ['sales.view']).map((screen) => screen.to)).not.toContain('/panel/ventas/nueva');
  });

  it('ubica una dirección en su módulo y pantalla, y dice si la sesión puede abrirla', () => {
    expect(locate(REGISTRY, '/panel')?.module.key).toBe('inicio');
    expect(locate(REGISTRY, '/panel/ventas/F-CM-000123')).toMatchObject({ module: { key: 'ventas' }, route: { path: ':numero' }, params: { numero: 'F-CM-000123' } });
    expect(locate(REGISTRY, '/panel/ventas/nueva')?.route.title).toBe('Nueva venta');
    expect(locate(REGISTRY, '/panel/no-existe')).toBeNull();

    const nueva = locate(REGISTRY, '/panel/ventas/nueva')!;
    expect(screenAccess(nueva.module, nueva.route, ['sales.view']).allowed).toBe(false);
    expect(screenAccess(nueva.module, nueva.route, ['sales.view']).missing).toEqual({ all: ['sales.pos.operate'], any: [] });
    expect(screenAccess(nueva.module, nueva.route, ['sales.view', 'sales.pos.operate']).allowed).toBe(true);
    expect(modulePath({ basePath: '/panel/ventas' }, '?estado=x')).toBe('/panel/ventas?estado=x');
    expect(modulePath({ basePath: '/panel/ventas' }, 'nueva')).toBe('/panel/ventas/nueva');
  });
});

describe('registro · módulos del proyecto', () => {
  it('todos los módulos de `panel/modules` son válidos (si esta prueba falla, el mensaje dice qué corregir)', () => {
    expect(PANEL_REGISTRY.problems).toEqual([]);
    expect(PANEL_REGISTRY.home?.key).toBe('inicio');
    expect(PANEL_REGISTRY.modules.map((module) => module.key)).toEqual(expect.arrayContaining(['inicio', 'actividad']));
  });

  it('«Actividad» está en Administración, pide `iam.audit.view` y ofrece botones y una estadística', () => {
    const actividad = PANEL_REGISTRY.modules.find((module) => module.key === 'actividad')!;
    expect(actividad).toMatchObject({ section: 'administracion', title: 'Actividad', basePath: '/panel/actividad', permissions: { all: ['iam.audit.view'] } });
    expect(dashboardActions(PANEL_REGISTRY, ['iam.audit.view']).flatMap((group) => group.actions.map((item) => item.to))).toEqual([
      '/panel/actividad',
      '/panel/actividad?resultado=Rejected',
    ]);
    expect(dashboardStats(PANEL_REGISTRY, ['iam.audit.view']).map((item) => item.stat.title)).toEqual(['Actividad de hoy']);
    expect(menuSections(PANEL_REGISTRY, ['sales.view']).flatMap((group) => group.modules.map((module) => module.key))).not.toContain('actividad');
  });
});
