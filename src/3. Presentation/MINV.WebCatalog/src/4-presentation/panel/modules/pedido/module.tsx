// Módulo del panel: Compras › Pedido sugerido (qué comprar y a quién, con cantidades revisables, y las órdenes de compra
// en borrador). LIVIANO a propósito: solo la definición, íconos y cargas diferidas.

import { ShoppingCart } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const pedido = defineModule({
  key: 'pedido',
  section: 'compras',
  title: 'Pedido sugerido',
  description: 'Qué comprar y a quién según mínimos y máximos; genera las órdenes de compra en borrador.',
  icon: ShoppingCart,
  order: 10,
  permissions: { all: ['inventory.stock.view'] },
  routes: [{ path: '', title: 'Pedido sugerido', element: lazyScreen(() => import('./OrderPage'), 'OrderPage') }],
  actions: [
    {
      key: 'generar',
      label: 'Generar pedido',
      description: 'Revise qué comprar a cada proveedor y cree las órdenes de compra.',
      icon: ShoppingCart,
      to: '',
      permissions: { all: ['purchasing.manage'] },
    },
  ],
});

export default pedido;
