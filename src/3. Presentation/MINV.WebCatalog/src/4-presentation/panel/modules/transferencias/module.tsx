// Módulo del panel (paquete M7): Sucursales › Transferencias (TransfersView del escritorio). Mercadería entre sucursales:
// el ORIGEN solicita, despacha (sale y queda en tránsito) y anula; el DESTINO recibe, con faltantes y su motivo (reglas
// B-03 y B-04). LIVIANO a propósito: solo la definición, íconos y cargas diferidas.
//
// Dirección: `?nueva=1` (tablero «Nueva transferencia»), `?pendiente=recibir` (tablero «Recibir transferencia»),
// `?estado=Dispatched`, `?origen=CM` y `?destino=CB` (desde Sucursales).

import { ArrowRightLeft, PackageCheck, Plus } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const transferencias = defineModule({
  key: 'transferencias',
  section: 'sucursales',
  title: 'Transferencias',
  description: 'Mercadería entre sucursales: solicitar, despachar, recibir con faltantes y anular.',
  icon: ArrowRightLeft,
  order: 20,
  // La lista la sirve `GetTransfersQuery` (consultar existencias); los comandos exigen `inventory.transfers.manage`.
  permissions: { all: ['inventory.transfers.manage', 'inventory.stock.view'] },
  licenseModules: ['MULTI_BRANCH'],
  routes: [{ path: '', title: 'Transferencias', element: lazyScreen(() => import('./TransfersPage'), 'TransfersPage') }],
  actions: [
    {
      key: 'nueva',
      label: 'Nueva transferencia',
      description: 'Envíe mercadería de su sucursal a otra.',
      icon: Plus,
      to: '?nueva=1',
    },
    {
      key: 'recibir',
      label: 'Recibir transferencia',
      description: 'Lo que llega a sus sucursales: cuente lo recibido y registre los faltantes.',
      icon: PackageCheck,
      to: '?pendiente=recibir',
    },
  ],
  stats: [{ key: 'en-curso', title: 'Transferencias en curso', component: lazyScreen(() => import('./TransfersStat'), 'TransfersStat') }],
});

export default transferencias;
