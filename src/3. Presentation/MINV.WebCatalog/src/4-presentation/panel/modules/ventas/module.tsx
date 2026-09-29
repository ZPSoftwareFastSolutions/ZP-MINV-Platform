// Módulo del panel: Ventas › Ventas (el historial de ventas del escritorio, SalesView). Facturas de la sucursal con
// filtros, detalle con líneas, series, pago, factura del SIN y devoluciones; reimprimir, enviar la factura por correo,
// devolver productos (normal o por falla, con series) y anular. Pestaña «Devoluciones». Acepta `?cliente=<código>`.
// Archivo LIVIANO: solo la definición, íconos y cargas diferidas.

import { CalendarDays, Receipt, Undo2 } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const ventas = defineModule({
  key: 'ventas',
  section: 'ventas',
  title: 'Ventas',
  description: 'Historial de ventas: detalle, factura del SIN, reimpresión, devoluciones y anulaciones.',
  icon: Receipt,
  order: 20,
  permissions: { all: ['sales.view'] },
  routes: [{ path: '', title: 'Ventas', element: lazyScreen(() => import('./SalesPage'), 'SalesPage') }],
  actions: [
    { key: 'hoy', label: 'Ventas de hoy', description: 'Lo vendido hoy, con su factura del SIN, filtros y exportación a Excel.', icon: CalendarDays, to: '' },
    {
      key: 'devolucion',
      label: 'Registrar una devolución',
      description: 'El cliente devuelve productos: vuelve el stock, se reembolsa y se emite la nota.',
      icon: Undo2,
      to: '?devolver=1',
      permissions: { all: ['billing.void', 'sales.pos.operate'] },
    },
  ],
  stats: [
    { key: 'por-dia', title: 'Ventas por día (7 días)', component: lazyScreen(() => import('./SalesByDayStat'), 'SalesByDayStat') },
    { key: 'medios-de-pago', title: 'Medios de pago', component: lazyScreen(() => import('./PaymentMethodsStat'), 'PaymentMethodsStat') },
  ],
});

export default ventas;
