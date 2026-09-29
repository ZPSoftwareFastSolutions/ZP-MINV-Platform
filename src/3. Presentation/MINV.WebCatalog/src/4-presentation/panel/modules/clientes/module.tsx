// Módulo del panel: Ventas › Clientes (la cartera del escritorio, CustomersView). Lista con filtros, alta y edición en un
// diálogo con los datos de factura del SIN, activar y desactivar, y el detalle con «Ver sus ventas». Lo ve quien gestiona
// clientes o quien consulta las ventas (solo lectura); la lista exige además «consultar existencias», el permiso que el
// servidor pide para leer los clientes (`GetCustomersQuery`). Archivo LIVIANO: definición, íconos y cargas diferidas.

import { UserPlus, Users } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const clientes = defineModule({
  key: 'clientes',
  section: 'ventas',
  title: 'Clientes',
  description: 'Cartera de clientes: contacto, datos de factura del SIN, compras y sus ventas.',
  icon: Users,
  order: 30,
  permissions: { all: ['inventory.stock.view'], any: ['sales.customers.manage', 'sales.view'] },
  routes: [{ path: '', title: 'Clientes', element: lazyScreen(() => import('./CustomersPage'), 'CustomersPage') }],
  actions: [
    {
      key: 'nuevo',
      label: 'Nuevo cliente',
      description: 'Registre un cliente con su NIT o CI para facturarle a su nombre.',
      icon: UserPlus,
      to: '?nuevo=1',
      permissions: { all: ['sales.customers.manage'] },
    },
  ],
});

export default clientes;
