// Módulo del panel: Análisis › Contabilidad (en el escritorio: AccountingView). Estado de resultados, libro diario con
// detalle Debe/Haber, plan de cuentas en árbol, asiento manual (Debe = Haber en vivo) y cuentas nuevas. Ofrece al tablero
// «Libro diario», «Estado de resultados» y «Registrar un asiento». Archivo LIVIANO: solo la definición, íconos y cargas
// diferidas.

import { BookOpenText, FilePlus2, NotebookText, Scale } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const contabilidad = defineModule({
  key: 'contabilidad',
  section: 'analisis',
  title: 'Contabilidad',
  description: 'Estado de resultados, libro diario, plan de cuentas y asientos manuales (gastos, pagos y depósitos).',
  icon: BookOpenText,
  order: 20,
  permissions: { all: ['accounting.manage'] },
  routes: [{ path: '', title: 'Contabilidad', element: lazyScreen(() => import('./AccountingPage'), 'AccountingPage') }],
  actions: [
    { key: 'diario', label: 'Libro diario', description: 'Los asientos del mes con su detalle Debe y Haber, filtros por cuenta y origen.', icon: NotebookText, to: '?vista=diario' },
    { key: 'resultados', label: 'Estado de resultados', description: 'Ingresos, costo de ventas, gastos y utilidad neta del período.', icon: Scale, to: '' },
    { key: 'asiento', label: 'Registrar un asiento', description: 'Gastos, pagos a proveedores, depósitos o aportes, con plantillas.', icon: FilePlus2, to: '?nuevo=asiento' },
  ],
});

export default contabilidad;
