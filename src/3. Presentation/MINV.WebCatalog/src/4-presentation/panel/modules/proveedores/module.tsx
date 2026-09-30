// Módulo del panel (paquete M7): Compras › Proveedores (SuppliersView del escritorio). Datos de contacto, NIT, plazo de
// entrega (lo usa el pedido sugerido), productos, órdenes abiertas y lo comprado; alta, edición y activación, y el
// enlace a sus órdenes de compra (`/panel/compras?proveedor=<código>`). LIVIANO: definición, íconos y cargas diferidas.

import { Truck } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const proveedores = defineModule({
  key: 'proveedores',
  section: 'compras',
  title: 'Proveedores',
  description: 'Contactos, NIT, plazos de entrega, órdenes abiertas y lo comprado a cada proveedor.',
  icon: Truck,
  order: 30,
  // La lista la sirve `GetSuppliersQuery` (consultar existencias); guardar exige `purchasing.manage`.
  permissions: { all: ['purchasing.manage', 'inventory.stock.view'] },
  routes: [{ path: '', title: 'Proveedores', element: lazyScreen(() => import('./SuppliersPage'), 'SuppliersPage') }],
});

export default proveedores;
