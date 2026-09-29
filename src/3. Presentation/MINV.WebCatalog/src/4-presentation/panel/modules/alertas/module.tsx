// Módulo del panel: Inventario › Alertas (productos que necesitan atención, priorizados, con la acción sugerida).
// LIVIANO a propósito: solo la definición, íconos y cargas diferidas.

import { TriangleAlert } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const alertas = defineModule({
  key: 'alertas',
  section: 'inventario',
  title: 'Alertas',
  description: 'Productos sin stock, críticos, bajos o con exceso, por urgencia y con la acción sugerida.',
  icon: TriangleAlert,
  order: 50,
  permissions: { all: ['inventory.stock.view'] },
  routes: [{ path: '', title: 'Alertas', element: lazyScreen(() => import('./AlertsPage'), 'AlertsPage') }],
  actions: [{ key: 'ver', label: 'Ver alertas', description: 'Qué reponer o revisar primero, con la acción sugerida.', icon: TriangleAlert, to: '' }],
});

export default alertas;
