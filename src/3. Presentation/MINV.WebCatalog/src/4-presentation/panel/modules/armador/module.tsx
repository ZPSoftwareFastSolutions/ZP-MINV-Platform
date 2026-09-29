// Módulo Tecnología › Armador de PC: la lista de cotizaciones (armados de PC, del mostrador y de la tienda web) y el
// armador paso a paso por ranura con la compatibilidad revisada en vivo por el servidor, la cotización para el cliente, la
// reserva de stock, la publicación en la tienda web, la anulación, «Vender en caja» y la cotización imprimible.
// Este archivo es LIVIANO: solo la definición, íconos y cargas diferidas.
//
// Permisos: se VE con `sales.view` + `inventory.stock.view` (lista, candidatos y compatibilidad); guardar, cotizar,
// reservar, liberar, publicar y anular piden `sales.pcbuild.manage` (los botones solo aparecen con ese permiso).

import { Cpu, Wrench } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const armador = defineModule({
  key: 'armador',
  section: 'tecnologia',
  title: 'Armador de PC',
  description: 'Arme PCs compatibles, cotícelas, reserve el stock, publíquelas en la web y cóbrelas en la caja.',
  icon: Cpu,
  order: 10,
  permissions: { all: ['sales.view', 'inventory.stock.view'] },
  routes: [
    { path: '', title: 'Armador de PC', element: lazyScreen(() => import('./BuildsPage'), 'BuildsPage') },
    { path: 'nuevo', title: 'Armar una PC', element: lazyScreen(() => import('./BuilderPage'), 'BuilderPage') },
    { path: ':numero', title: 'Armado', element: lazyScreen(() => import('./BuilderPage'), 'BuilderPage') },
  ],
  actions: [
    {
      key: 'armar',
      label: 'Armar una PC',
      description: 'Paso a paso por ranura, con la compatibilidad revisada en vivo y la cotización para el cliente.',
      icon: Wrench,
      to: 'nuevo',
    },
  ],
});

export default armador;
