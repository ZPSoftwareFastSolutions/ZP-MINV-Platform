// Módulo del panel: Análisis › Reportes (en el escritorio: ReportsView). Reportes de ventas (con rentabilidad), compras,
// movimientos, inventario, sucursales y tecnología, con período, filtros, totales, exportar CSV e imprimir; gráficos
// plegados. Ofrece al tablero «Reporte de ventas» y «Reporte de compras», y la estadística plegada «Ventas del mes vs mes
// anterior». Archivo LIVIANO: solo la definición, íconos y cargas diferidas.

import { ChartColumnBig, Receipt, Truck } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const reportes = defineModule({
  key: 'reportes',
  section: 'analisis',
  title: 'Reportes',
  description: 'Ventas y rentabilidad, compras, movimientos, inventario, sucursales y tecnología por período; exportar e imprimir.',
  icon: ChartColumnBig,
  order: 10,
  permissions: { all: ['reports.view'] },
  routes: [{ path: '', title: 'Reportes', element: lazyScreen(() => import('./ReportsPage'), 'ReportsPage') }],
  actions: [
    { key: 'ventas', label: 'Reporte de ventas', description: 'Ventas, costo, utilidad y margen del período, por producto, categoría, cliente o día.', icon: Receipt, to: '' },
    { key: 'compras', label: 'Reporte de compras', description: 'Compras recibidas del período por proveedor o por día, y órdenes abiertas.', icon: Truck, to: '?reporte=compras' },
  ],
  stats: [{ key: 'mes', title: 'Ventas del mes vs mes anterior', component: lazyScreen(() => import('./MonthSalesStat'), 'MonthSalesStat') }],
});

export default reportes;
