// Módulo del panel (paquete M10): Administración › Configuración. Datos de la empresa y semáforo (con `iam.users.manage`),
// facturación SIAT y correo de la empresa (con `billing.configure`). Este archivo es LIVIANO a propósito: solo la
// definición, íconos y cargas diferidas.

import { Receipt, Settings } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const configuracion = defineModule({
  key: 'configuracion',
  section: 'administracion',
  title: 'Configuración',
  description: 'Datos de la empresa y semáforo de stock, facturación con el SIN (SIAT) y el correo de la empresa.',
  icon: Settings,
  order: 30,
  permissions: { any: ['iam.users.manage', 'billing.configure'] },
  routes: [{ path: '', title: 'Configuración', element: lazyScreen(() => import('./SettingsPage'), 'SettingsPage') }],
  actions: [
    {
      key: 'facturacion',
      label: 'Configurar la facturación',
      description: 'NIT y ambiente, conexión con el SIN, sucursales del Padrón, puntos de venta y el correo de envío.',
      icon: Receipt,
      to: '?pestana=facturacion',
      permissions: { all: ['billing.configure'] },
    },
  ],
});

export default configuracion;
