// Módulo del panel (paquete M8): Facturación › Documentos fiscales (FiscalDocumentsView del escritorio). Facturas y notas
// crédito-débito del SIN: búsqueda por período, tipo, estado, sucursal, punto de venta y tipo de emisión; detalle con la
// bitácora del SIN y las entregas; ver e imprimir, PDF, correo, verificar en el SIN, anular (con «anular y devolver»),
// revertir la anulación, re-emitir y el XML exacto. Dirección: `?documento=<id>` abre un documento, `?venta=<número>`
// abre el documento vigente de esa venta y `&anular=1` empieza a anularlo; `?periodo=hoy` (tablero) muestra el día.
// Archivo LIVIANO: solo la definición, íconos y cargas diferidas.

import { CalendarDays, FileText, TriangleAlert } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const documentosFiscales = defineModule({
  key: 'documentos-fiscales',
  section: 'facturacion',
  title: 'Documentos fiscales',
  description: 'Facturas y notas crédito-débito del SIN: consulta, impresión, correo, anulación y re-emisión.',
  icon: FileText,
  order: 10,
  permissions: { all: ['billing.view'] },
  licenseModules: ['FISCAL_SIAT'],
  routes: [{ path: '', title: 'Documentos fiscales', element: lazyScreen(() => import('./FiscalDocumentsPage'), 'FiscalDocumentsPage') }],
  actions: [
    { key: 'hoy', label: 'Facturas de hoy', description: 'Lo emitido hoy ante el SIN, con su estado, filtros y exportación a Excel.', icon: CalendarDays, to: '?periodo=hoy' },
    {
      key: 'rechazadas',
      label: 'Revisar rechazadas',
      description: 'Documentos que el SIN rechazó u observó: vea sus mensajes y re-emítalos.',
      icon: TriangleAlert,
      to: '?estado=Rejected',
    },
  ],
  stats: [{ key: 'hoy', title: 'Facturación de hoy', component: lazyScreen(() => import('./TodayDocumentsStat'), 'TodayDocumentsStat') }],
});

export default documentosFiscales;
