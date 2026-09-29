// Módulo del panel (paquete M10): Administración › Integraciones. API Keys, webhooks y sus entregas para la tienda en
// línea o el ERP, y la cola de correos de las reservas. Este archivo es LIVIANO a propósito: solo la definición, íconos y
// cargas diferidas.

import { MailCheck, Plug } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const integraciones = defineModule({
  key: 'integraciones',
  section: 'administracion',
  title: 'Integraciones',
  description: 'API Keys, webhooks y sus entregas para la tienda en línea o el ERP, y la cola de correos de las reservas.',
  icon: Plug,
  order: 20,
  permissions: { all: ['integration.manage'] },
  routes: [{ path: '', title: 'Integraciones', element: lazyScreen(() => import('./IntegrationsPage'), 'IntegrationsPage') }],
  actions: [
    {
      key: 'correos',
      label: 'Correos de reservas',
      description: 'Vea en qué quedó cada confirmación de reserva (enviada, pendiente o con error) y reenvíela.',
      icon: MailCheck,
      to: '?pestana=correos',
      permissions: { all: ['sales.pcbuild.manage'] },
    },
  ],
  stats: [
    { key: 'resumen', title: 'Integraciones', component: lazyScreen(() => import('./IntegrationsStat'), 'IntegrationsStat') },
    { key: 'correos', title: 'Correos de reservas', permissions: { all: ['sales.pcbuild.manage'] }, component: lazyScreen(() => import('./MailsStat'), 'MailsStat') },
  ],
});

export default integraciones;
