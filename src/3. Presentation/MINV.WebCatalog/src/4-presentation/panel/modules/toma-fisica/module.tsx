// Módulo del panel: Inventario › Toma física (PhysicalCountView del escritorio): abrir una toma en el almacén, contar
// producto por producto (a mano o con el lector de códigos), ver la diferencia contra el stock exacto y generar los
// ajustes en una sola operación confirmada. Contar exige `inventory.counts.record`; generar los ajustes o anular la toma,
// `inventory.counts.post` (el servidor decide igual). Archivo LIVIANO: solo la definición, íconos y cargas diferidas.

import { ClipboardCheck } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const tomaFisica = defineModule({
  key: 'toma-fisica',
  section: 'inventario',
  title: 'Toma física',
  description: 'Conteo del almacén con el lector de códigos, diferencias contra el sistema y ajustes automáticos.',
  icon: ClipboardCheck,
  order: 40,
  // Contar exige su permiso; la búsqueda de productos, las posiciones y la ficha exigen ver el stock.
  permissions: { all: ['inventory.counts.record', 'inventory.stock.view'] },
  routes: [{ path: '', title: 'Toma física', element: lazyScreen(() => import('./PhysicalCountPage'), 'PhysicalCountPage') }],
  actions: [
    {
      key: 'iniciar',
      label: 'Iniciar toma física',
      description: 'Abra una toma (o siga la que está en curso) y cuente con el lector de códigos.',
      icon: ClipboardCheck,
      to: '',
    },
  ],
});

export default tomaFisica;
