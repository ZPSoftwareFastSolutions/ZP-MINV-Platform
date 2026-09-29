// Módulo del panel: Inventario › Movimientos (últimos movimientos y registrar entradas, salidas y ajustes con poka-yoke).
// LIVIANO a propósito: solo la definición, íconos y cargas diferidas.

import { ArrowLeftRight, PackagePlus, Wrench } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const WAREHOUSE = { all: ['inventory.movements.register.warehouse'] };

const movimientos = defineModule({
  key: 'movimientos',
  section: 'inventario',
  title: 'Movimientos',
  description: 'Registrar entradas, salidas y ajustes, y ver los últimos movimientos del inventario.',
  icon: ArrowLeftRight,
  order: 30,
  // Todas sus consultas exigen ver el stock; registrar, el permiso del dominio del tipo (bodega o ventas).
  permissions: { all: ['inventory.stock.view'], any: ['inventory.movements.register.warehouse', 'inventory.movements.register.sales'] },
  routes: [{ path: '', title: 'Movimientos', element: lazyScreen(() => import('./MovementsPage'), 'MovementsPage') }],
  actions: [
    { key: 'entrada', label: 'Registrar entrada', description: 'Mercadería que llega: compra, remisión o devolución.', icon: PackagePlus, to: '?registrar=ENTRADA', permissions: WAREHOUSE },
    { key: 'ajuste', label: 'Registrar ajuste', description: 'Corregir el stock por merma, daño o sobrante, con su motivo.', icon: Wrench, to: '?registrar=ajuste', permissions: WAREHOUSE },
  ],
});

export default movimientos;
