// Módulo del panel (paquete M7): Sucursales › Sucursales (BranchesView del escritorio). El directorio de sucursales con
// sus almacenes, usuarios, stock valorizado y transferencias abiertas; el comparativo por sucursal (plegado); el stock
// consolidado con lo que está en tránsito (contado una vez); y, para quien administra sucursales, alta, edición,
// activación y los usuarios de cada sucursal. LIVIANO a propósito: solo la definición, íconos y cargas diferidas.

import { Building2 } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const sucursales = defineModule({
  key: 'sucursales',
  section: 'sucursales',
  title: 'Sucursales',
  description: 'Ventas y stock por sucursal, stock consolidado con lo que está en tránsito, y alta de sucursales y sus usuarios.',
  icon: Building2,
  order: 10,
  // Lo ve quien consulta reportes o la gerencia global, o quien administra sucursales; el directorio lo sirve
  // `GetBranchesQuery` (consultar existencias).
  permissions: { all: ['inventory.stock.view'], any: ['reports.view', 'corporate.branches.all', 'corporate.branches.manage'] },
  routes: [{ path: '', title: 'Sucursales', element: lazyScreen(() => import('./BranchesPage'), 'BranchesPage') }],
});

export default sucursales;
