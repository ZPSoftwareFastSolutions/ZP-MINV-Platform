// Módulo del panel (paquete M8): Facturación › Libros fiscales (FiscalBooksView del escritorio). Libro de ventas IVA (facturas
// y notas del SIN), libro de compras (facturas de los proveedores y las recepciones que todavía no la tienen) y el resumen
// IVA / IT del mes para el contador; descarga del libro en CSV (plantilla del SIN) o Excel. Filtros por mes y sucursal,
// totales al pie. Dirección: `?mes=2026-09`, `?sucursal=CM` y `?pestana=ventas|compras|resumen|recepciones`. Archivo
// LIVIANO: solo la definición, íconos y cargas diferidas.

import { BookOpen, Calculator } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const libros = defineModule({
  key: 'libros',
  section: 'facturacion',
  title: 'Libros fiscales',
  description: 'Libro de ventas IVA, libro de compras y resumen IVA / IT del mes, con descarga para el SIN.',
  icon: BookOpen,
  order: 40,
  permissions: { all: ['billing.view'] },
  licenseModules: ['FISCAL_SIAT'],
  routes: [{ path: '', title: 'Libros fiscales', element: lazyScreen(() => import('./BooksPage'), 'BooksPage') }],
  actions: [
    { key: 'ventas', label: 'Libro de ventas del mes', description: 'Facturas y notas del mes con sus totales, listo para descargar en CSV o Excel.', icon: BookOpen, to: '' },
    { key: 'resumen', label: 'Resumen IVA e IT del mes', description: 'Débito, crédito, IVA a pagar e IT, con la explicación de cada línea.', icon: Calculator, to: '?pestana=resumen' },
  ],
  stats: [{ key: 'iva', title: 'IVA del mes', component: lazyScreen(() => import('./TaxStat'), 'TaxStat') }],
});

export default libros;
