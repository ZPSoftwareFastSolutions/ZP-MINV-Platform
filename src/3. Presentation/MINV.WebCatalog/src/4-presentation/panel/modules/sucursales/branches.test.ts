// Módulo «Sucursales» · pruebas de las funciones puras: filtros, rango del comparativo, consolidado, formularios de alta
// y edición (con sus pedidos exactos) y los cambios de usuarios por sucursal (nadie queda sin sucursales).

import { describe, expect, it } from 'vitest';
import {
  BRANCH_FILTERS,
  branchProblems,
  consolidatedCsv,
  createBranchPayload,
  defaultReportRange,
  editProblem,
  emptyBranchDraft,
  filterBranches,
  filterConsolidated,
  membershipChanges,
  orderedCodes,
  orphaned,
  proposedWarehouseCode,
  proposedWarehouseName,
  reportRequest,
  staffUsers,
  toBranchItems,
  transfersLink,
  updateBranchPayload,
  type BranchRecord,
  type ConsolidatedLine,
  type UserRecord,
} from './branches';

function branch(overrides: Partial<BranchRecord>): BranchRecord {
  return { id: 'b1', code: 'CM', name: 'Casa matriz', isActive: true, isVisible: true, stockValue: 1000, transfersIn: 0, transfersOut: 0, users: 2, warehouses: ['ALMCM'], ...overrides };
}

const BRANCHES = [branch({}), branch({ id: 'b2', code: 'CB', name: 'Cochabamba', isVisible: false, stockValue: null, warehouses: ['ALMCB'] }), branch({ id: 'b3', code: 'SC', name: 'Santa Cruz', isActive: false, warehouses: ['ALMSC'] })];

function user(overrides: Partial<UserRecord>): UserRecord {
  return {
    email: 'a@techzone.example',
    name: 'Ana',
    roles: ['BODEGA'],
    branchCodes: ['CM'],
    isActive: true,
    isLocked: false,
    hasPassword: true,
    mustChangePassword: false,
    failedAttempts: 0,
    lastAccess: null,
    ...overrides,
  };
}

describe('Sucursales · lista y comparativo', () => {
  it('filtra por estado, acceso y búsqueda (también por almacén)', () => {
    const items = toBranchItems(BRANCHES);
    const keys = (filters: Partial<typeof BRANCH_FILTERS>) => filterBranches(items, { ...BRANCH_FILTERS, ...filters }).map((item) => item.key);
    expect(keys({ estado: 'inactiva' })).toEqual(['SC']);
    expect(keys({ acceso: 'otras' })).toEqual(['CB']);
    expect(keys({ acceso: 'mias' })).toEqual(['CM', 'SC']);
    expect(keys({ q: 'almcb' })).toEqual(['CB']);
    expect(transfersLink('origen', 'CM')).toBe('/panel/transferencias?origen=CM');
  });

  it('el comparativo pide siempre las dos fechas (por defecto los últimos 30 días)', () => {
    expect(defaultReportRange('2026-09-29')).toEqual({ from: '2026-08-31', to: '2026-09-29' });
    expect(reportRequest({ from: '2026-09-01', to: null }, '2026-09-29')).toEqual({ from: '2026-09-01', to: '2026-09-29' });
    expect(reportRequest({ from: null, to: null }, '2026-09-29')).toEqual({ from: '2026-09-29', to: '2026-09-29' });
    expect(reportRequest({ from: '2026-09-10', to: '2026-09-01' }, '2026-09-29')).toBeNull();
  });

  it('el consolidado filtra por categoría y tránsito, y exporta una columna por sucursal', () => {
    const rows: ConsolidatedLine[] = [
      { sku: 'A', name: 'Mouse', category: 'Periféricos', unit: 'UND', byBranch: [2, 1], inTransit: 1, total: 4, value: 400 },
      { sku: 'B', name: 'Monitor', category: 'Monitores', unit: 'UND', byBranch: [0, 3], inTransit: 0, total: 3, value: 3000 },
    ];
    expect(filterConsolidated(rows, { q: '', categoria: '', transito: 'con' }).map((row) => row.sku)).toEqual(['A']);
    expect(filterConsolidated(rows, { q: '', categoria: 'Monitores', transito: '' }).map((row) => row.sku)).toEqual(['B']);
    const columns = consolidatedCsv([
      { id: '1', code: 'CM', name: 'Casa matriz' },
      { id: '2', code: 'CB', name: 'Cochabamba' },
    ]);
    expect(columns.map((column) => column.header)).toEqual(['SKU', 'Producto', 'Categoría', 'Unidad', 'CM', 'CB', 'En tránsito', 'Total', 'Valor']);
    expect(columns.map((column) => column.value(rows[0]))).toEqual(['A', 'Mouse', 'Periféricos', 'UND', 2, 1, 1, 4, 400]);
  });
});

describe('Sucursales · alta y edición', () => {
  it('propone el almacén como el escritorio y valida códigos, nombres y repetidos', () => {
    expect(proposedWarehouseCode('cb')).toBe('ALMCB');
    expect(proposedWarehouseName('Sucursal Tarija')).toBe('Almacén Tarija');
    expect(branchProblems(emptyBranchDraft(), BRANCHES)).toEqual({
      code: 'Indique el código de la sucursal.',
      name: 'Indique el nombre de la sucursal.',
      warehouseCode: 'Indique el código del almacén.',
      warehouseName: 'Indique el nombre del almacén.',
    });
    expect(branchProblems({ code: 'cm', name: 'X', warehouseCode: 'ALM CB', warehouseName: 'Y', createPosRegister: true }, BRANCHES)).toEqual({
      code: 'Ya existe la sucursal CM.',
      warehouseCode: 'El código del almacén: letras y números, sin espacios (hasta 12).',
    });
    expect(branchProblems({ code: 'TJ', name: 'Tarija', warehouseCode: 'almcb', warehouseName: 'Y', createPosRegister: true }, BRANCHES)).toEqual({ warehouseCode: 'Ya existe el almacén ALMCB.' });
  });

  it('arma los pedidos exactos de crear y editar; no deja desactivar la única activa', () => {
    expect(createBranchPayload({ code: ' tj ', name: ' Sucursal Tarija ', warehouseCode: 'almtj', warehouseName: ' Almacén Tarija ', createPosRegister: false })).toEqual({
      code: 'TJ',
      name: 'Sucursal Tarija',
      warehouseCode: 'ALMTJ',
      warehouseName: 'Almacén Tarija',
      createPosRegister: false,
    });
    expect(updateBranchPayload(BRANCHES[0], ' Casa matriz La Paz ', true)).toEqual({ code: 'CM', name: 'Casa matriz La Paz', isActive: true });
    expect(editProblem('X', false, BRANCHES[0], [BRANCHES[0]])).toEqual({ isActive: 'No puede desactivar la única sucursal activa.' });
    expect(editProblem('X', false, BRANCHES[0], BRANCHES)).toEqual({});
  });
});

describe('Sucursales · usuarios por sucursal', () => {
  const users = [
    user({}),
    user({ email: 'b@techzone.example', name: 'Bruno', branchCodes: ['CB', 'CM'] }),
    user({ email: 'c@techzone.example', name: 'Carla', branchCodes: ['CB'] }),
    user({ email: 'd@techzone.example', name: 'Diego', isActive: false }),
    user({ email: 'v@correo.example', name: 'Valentina', roles: ['CLIENTE'], branchCodes: [] }),
  ];

  it('solo el personal activo, y los códigos en el orden del directorio', () => {
    expect(staffUsers(users).map((item) => item.name)).toEqual(['Ana', 'Bruno', 'Carla']);
    expect(orderedCodes(['SC', 'CM', 'XX', 'CM'], BRANCHES)).toEqual(['CM', 'SC', 'XX']);
  });

  it('a cada persona que cambia se le envían TODAS sus sucursales; nadie queda sin ninguna', () => {
    const staff = staffUsers(users);
    const changes = membershipChanges(staff, 'CM', new Set(['b@techzone.example', 'c@techzone.example']), BRANCHES);
    expect(changes.map((change) => [change.user.name, change.joins, change.payload])).toEqual([
      ['Ana', false, { email: 'a@techzone.example', branchCodes: [] }],
      ['Carla', true, { email: 'c@techzone.example', branchCodes: ['CM', 'CB'] }],
    ]);
    expect(orphaned(changes).map((item) => item.name)).toEqual(['Ana']);
  });
});
