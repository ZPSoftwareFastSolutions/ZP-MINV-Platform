// Módulo del panel: Tecnología › Series (la pantalla «Series e IMEI» del escritorio, SerialsView). Cada unidad con serie o
// IMEI: dónde está, a quién se vendió, su garantía (derivada de la venta, regla T-04), su trazabilidad y sus casos de
// garantía; registrar las series de unidades que ya estaban en stock y dar destino a las devueltas o en garantía (al
// proveedor o de baja). Archivo LIVIANO: solo la definición, íconos y cargas diferidas.
//
// Permisos: se VE con `inventory.serials.view`; registrar series y dar destino piden `inventory.serials.manage` (los
// botones solo aparecen con ese permiso). «Abrir caso de garantía» lleva a Garantías (`service.rma.open`).

import { ScanSearch, ScanBarcode } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const series = defineModule({
  key: 'series',
  section: 'tecnologia',
  title: 'Series',
  description: 'Series e IMEI: dónde está cada unidad, a quién se vendió, su garantía y su trazabilidad.',
  icon: ScanBarcode,
  order: 20,
  permissions: { all: ['inventory.serials.view'] },
  routes: [{ path: '', title: 'Series', element: lazyScreen(() => import('./SerialsPage'), 'SerialsPage') }],
  actions: [
    {
      key: 'consultar',
      label: 'Consultar una serie',
      description: 'Escanee o escriba la serie o el IMEI: dónde está, a quién se vendió y su garantía.',
      icon: ScanSearch,
      to: '?consultar=1',
    },
  ],
  stats: [{ key: 'por-estado', title: 'Unidades por estado', component: lazyScreen(() => import('./SerialsByStatusStat'), 'SerialsByStatusStat') }],
});

export default series;
