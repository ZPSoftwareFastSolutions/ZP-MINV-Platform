// Módulo del panel (paquete M7): Compras › Órdenes de compra (PurchaseOrdersView del escritorio). Pedir, aprobar,
// recibir (con las series de los productos serializados) y anular órdenes de compra, y registrar las facturas de los
// proveedores de lo recibido. LIVIANO a propósito: solo la definición, íconos y cargas diferidas.
//
// Dirección: `?proveedor=<código>` (lo usan Proveedores y el pedido sugerido), `?estado=por-recibir` (tablero «Recibir
// mercadería»), `?nueva=1` (tablero «Nueva orden de compra») y `?pestana=facturas` (facturas de proveedores).

import { ClipboardList, FilePlus2, PackageCheck } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const compras = defineModule({
  key: 'compras',
  section: 'compras',
  title: 'Órdenes de compra',
  description: 'Pedir, aprobar y recibir mercadería de los proveedores, y registrar sus facturas.',
  icon: ClipboardList,
  order: 20,
  // La lista la sirve `GetPurchaseOrdersQuery` (consultar existencias); las acciones, `purchasing.manage`.
  permissions: { all: ['purchasing.manage', 'inventory.stock.view'] },
  routes: [{ path: '', title: 'Órdenes de compra', element: lazyScreen(() => import('./PurchaseOrdersPage'), 'PurchaseOrdersPage') }],
  actions: [
    {
      key: 'nueva',
      label: 'Nueva orden de compra',
      description: 'Pida mercadería a un proveedor: productos, cantidades y costos.',
      icon: FilePlus2,
      to: '?nueva=1',
    },
    {
      key: 'recibir',
      label: 'Recibir mercadería',
      description: 'Órdenes aprobadas por recibir: entra el stock y se registra el asiento.',
      icon: PackageCheck,
      to: '?estado=por-recibir',
      permissions: { all: ['inventory.movements.register.warehouse'] },
    },
  ],
  stats: [{ key: 'resumen', title: 'Compras en curso', component: lazyScreen(() => import('./PurchasesStat'), 'PurchasesStat') }],
});

export default compras;
