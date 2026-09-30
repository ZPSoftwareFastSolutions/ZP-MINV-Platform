// Módulo del panel (paquete M8): Facturación › Homologación (HomologationView del escritorio). Cada producto, unidad y
// medio de pago de M-INV con su código del SIN (sin homologar no se factura): pestañas Productos / Unidades / Medios de
// pago, filtro «sin homologar», asignar con búsqueda en el catálogo del SIN y «Sugerir homologación» para aceptar en lote.
// Dirección: `?pestana=productos|unidades|medios` y `?estado=pendiente`. Archivo LIVIANO: solo la definición, íconos y
// cargas diferidas.

import { Tags } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const homologacion = defineModule({
  key: 'homologacion',
  section: 'facturacion',
  title: 'Homologación',
  description: 'Productos, unidades y medios de pago con sus códigos del SIN (sin homologar no se factura).',
  icon: Tags,
  order: 30,
  permissions: { all: ['billing.view'] },
  licenseModules: ['FISCAL_SIAT'],
  routes: [{ path: '', title: 'Homologación', element: lazyScreen(() => import('./HomologationPage'), 'HomologationPage') }],
  actions: [
    {
      key: 'productos',
      label: 'Homologar productos',
      description: 'Asigne a cada producto su código del SIN, con sugerencias que se aceptan con un clic.',
      icon: Tags,
      to: '?estado=pendiente',
      permissions: { all: ['billing.configure'] },
    },
  ],
  stats: [{ key: 'pendientes', title: 'Homologación pendiente', component: lazyScreen(() => import('./PendingStat'), 'PendingStat') }],
});

export default homologacion;
