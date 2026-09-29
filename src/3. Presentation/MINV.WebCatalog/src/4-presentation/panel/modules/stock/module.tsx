// Módulo del panel: Inventario › Stock (existencias, reservado y disponible por almacén, consolidado de sucursales y ficha
// con kardex). LIVIANO a propósito: solo la definición, íconos y cargas diferidas.

import { PackageSearch } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const stock = defineModule({
  key: 'stock',
  section: 'inventario',
  title: 'Stock',
  description: 'Existencias, reservas y disponible por sucursal, con la ficha y el kardex de cada producto.',
  icon: PackageSearch,
  order: 10,
  permissions: { all: ['inventory.stock.view'] },
  routes: [{ path: '', title: 'Stock', element: lazyScreen(() => import('./StockPage'), 'StockPage') }],
  actions: [
    { key: 'consultar', label: 'Consultar stock', description: 'Existencias, reservas y disponible, con filtros y exportación a Excel.', icon: PackageSearch, to: '' },
  ],
  stats: [
    { key: 'valor', title: 'Valor del inventario', component: lazyScreen(() => import('./StockValueStat'), 'StockValueStat') },
    { key: 'alertas', title: 'Productos en alerta', component: lazyScreen(() => import('./StockAlertsStat'), 'StockAlertsStat') },
  ],
});

export default stock;
