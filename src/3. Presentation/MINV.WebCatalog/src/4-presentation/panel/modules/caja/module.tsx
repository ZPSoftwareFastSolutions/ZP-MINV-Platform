// Módulo del panel: Ventas › Caja (el punto de venta). Abrir y cerrar el turno con arqueo, vender con el buscador y el
// lector de códigos, series o IMEI, factura del SIN y comprobante, y vender una reserva o cotización
// (`/panel/caja?reserva=<NÚMERO>`, lo usan los módulos Reservas y Armador de PC). Archivo LIVIANO: solo la definición,
// íconos y cargas diferidas.

import { PackageCheck, ShoppingCart } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const caja = defineModule({
  key: 'caja',
  section: 'ventas',
  title: 'Caja',
  description: 'Punto de venta: abrir y cerrar caja, vender, cobrar con factura y vender reservas.',
  icon: ShoppingCart,
  order: 10,
  permissions: { all: ['sales.pos.operate'] },
  // Abrir y cerrar el turno exigen el módulo comercial de la caja (`[RequiresModule(POS_HARDWARE)]`).
  licenseModules: ['POS_HARDWARE'],
  routes: [{ path: '', title: 'Caja', element: lazyScreen(() => import('./PosPage'), 'PosPage') }],
  actions: [
    { key: 'caja', label: 'Ir a la caja', description: 'Abrir el turno, vender, cobrar y emitir la factura.', icon: ShoppingCart, to: '' },
    {
      key: 'reserva',
      label: 'Vender una reserva',
      description: 'Cobrar una reserva o cotización a sus precios congelados.',
      icon: PackageCheck,
      to: '?elegir=reserva',
      permissions: { all: ['sales.view', 'inventory.movements.register.sales'] },
    },
  ],
  stats: [{ key: 'turno', title: 'Ventas de mi turno', component: lazyScreen(() => import('./ShiftSalesStat'), 'ShiftSalesStat') }],
});

export default caja;
