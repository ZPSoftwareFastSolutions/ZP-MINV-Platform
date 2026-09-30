// Módulo del panel (paquete M8): Facturación › Estado del SIAT (SiatStatusView del escritorio). Conexión con el SIN,
// alertas con cuenta regresiva de los plazos, una tarjeta por punto de venta con su modo, CUIS y CUFD vigentes y sus
// acciones (verificar, pedir CUFD, fuera de línea, contingencia manual, fin de la contingencia, recuperar), eventos
// significativos, paquetes, talonarios CAFC, transcripción de facturas manuales y la bitácora técnica del SIN (plegada).
// Dirección: `?pestana=puntos|eventos|paquetes|talonarios`. Archivo LIVIANO: solo la definición, íconos y cargas diferidas.

import { FileText, RadioTower } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const siat = defineModule({
  key: 'siat',
  section: 'facturacion',
  title: 'Estado del SIAT',
  description: 'Conexión con el SIN, puntos de venta, plazos, contingencias, paquetes y talonarios CAFC.',
  icon: RadioTower,
  order: 20,
  permissions: { all: ['billing.view'] },
  licenseModules: ['FISCAL_SIAT'],
  routes: [{ path: '', title: 'Estado del SIAT', element: lazyScreen(() => import('./SiatPage'), 'SiatPage') }],
  actions: [
    { key: 'estado', label: 'Estado del SIAT', description: 'Conexión con el SIN, plazos de CUFD y CUIS, contingencias y paquetes.', icon: RadioTower, to: '' },
    {
      key: 'transcribir',
      label: 'Transcribir factura manual',
      description: 'Facturas del talonario CAFC emitidas durante una contingencia manual.',
      icon: FileText,
      to: '?pestana=eventos&transcribir=1',
      permissions: { all: ['billing.contingency'] },
    },
  ],
  stats: [{ key: 'conexion', title: 'Conexión con el SIN', component: lazyScreen(() => import('./SiatStatusStat'), 'SiatStatusStat') }],
});

export default siat;
