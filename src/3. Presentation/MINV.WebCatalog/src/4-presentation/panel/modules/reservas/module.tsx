// Módulo Ventas › Reservas: todas las reservas de la tienda web y del mostrador, de compra (carritos RES-…) y de armado
// (ARM-…): cuándo vencen, quién las recoge, el estado de su correo, y las acciones Vender en caja, Liberar, Reenviar
// correo, Copiar teléfono y Abrir WhatsApp; más la reserva en mostrador (`ReserveCartCommand`, `?nueva=1`).
// Este archivo es LIVIANO: solo la definición, íconos y cargas diferidas.
//
// Permisos: las acciones son de `sales.pcbuild.manage` (como el diseño) y la lista (`GetPcBuildsQuery`) exige además
// `sales.view`, así que el módulo pide los dos. La reserva en mostrador busca los productos con `GetSellableProductsQuery`
// (`inventory.stock.view`).

import { CalendarClock, Hourglass, ShoppingBag } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const reservas = defineModule({
  key: 'reservas',
  section: 'ventas',
  title: 'Reservas',
  description: 'Reservas de la tienda web y del mostrador: vencimientos, correo, liberar y vender en caja.',
  icon: CalendarClock,
  order: 40,
  permissions: { all: ['sales.pcbuild.manage', 'sales.view'] },
  routes: [{ path: '', title: 'Reservas', element: lazyScreen(() => import('./ReservationsPage'), 'ReservationsPage') }],
  actions: [
    {
      key: 'nueva',
      label: 'Nueva reserva en mostrador',
      description: 'Reserve productos para un cliente: nombre, teléfono y días para recoger.',
      icon: ShoppingBag,
      to: '?nueva=1',
      permissions: { all: ['inventory.stock.view'] },
    },
    {
      key: 'vencen-hoy',
      label: 'Reservas que vencen hoy',
      description: 'Las reservas vigentes que hay que cobrar o liberar hoy, la más urgente primero.',
      icon: Hourglass,
      to: '?vence=hoy&orden=plazo&sentido=asc',
    },
  ],
  stats: [{ key: 'activas', title: 'Reservas activas y su valor', component: lazyScreen(() => import('./ActiveReservationsStat'), 'ActiveReservationsStat') }],
});

export default reservas;
