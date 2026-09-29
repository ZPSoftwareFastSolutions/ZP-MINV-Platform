// Módulo de EJEMPLO del panel: Administración › Actividad (la auditoría: quién hizo qué, cuándo y con qué resultado).
// Este archivo es LIVIANO a propósito: solo la definición, íconos y cargas diferidas. Las pantallas y la estadística se
// descargan recién cuando se abren.

import { History, ShieldAlert } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const actividad = defineModule({
  key: 'actividad',
  section: 'administracion',
  title: 'Actividad',
  description: 'Quién hizo qué, cuándo y con qué resultado (auditoría).',
  icon: History,
  order: 40,
  permissions: { all: ['iam.audit.view'] },
  routes: [{ path: '', title: 'Actividad', element: lazyScreen(() => import('./ActivityPage'), 'ActivityPage') }],
  actions: [
    { key: 'ver', label: 'Ver la actividad', description: 'Quién hizo qué y cuándo, con filtros y exportación a Excel.', icon: History, to: '' },
    { key: 'rechazos', label: 'Revisar rechazos', description: 'Operaciones rechazadas: permisos, stock o datos no válidos.', icon: ShieldAlert, to: '?resultado=Rejected' },
  ],
  stats: [{ key: 'hoy', title: 'Actividad de hoy', component: lazyScreen(() => import('./ActivityTodayStat'), 'ActivityTodayStat') }],
});

export default actividad;
