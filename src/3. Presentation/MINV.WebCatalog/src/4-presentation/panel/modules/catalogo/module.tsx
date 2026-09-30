// Módulo del panel: Inventario › Catálogo (productos, precios, imágenes, fichas técnicas, categorías y especificaciones;
// CatalogView del escritorio). Lo consulta quien ve el stock; crear y modificar productos y categorías exige
// `catalog.manage` y las fichas técnicas y especificaciones, `catalog.specs.manage` (el servidor decide igual).
// Archivo LIVIANO: solo la definición, íconos y cargas diferidas.

import { PackagePlus, Search, Tags } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const catalogo = defineModule({
  key: 'catalogo',
  section: 'inventario',
  title: 'Catálogo',
  description: 'Productos con precios, imágenes y ficha técnica; categorías y especificaciones por categoría.',
  icon: Tags,
  order: 20,
  // Todas sus consultas exigen ver el stock (`GetCatalogQuery`, `GetCatalogOptionsQuery`…); editar pide más permisos.
  permissions: { all: ['inventory.stock.view'] },
  routes: [{ path: '', title: 'Catálogo', element: lazyScreen(() => import('./CatalogPage'), 'CatalogPage') }],
  actions: [
    {
      key: 'nuevo',
      label: 'Nuevo producto',
      description: 'Dé de alta un producto con su precio, imagen y ficha técnica.',
      icon: PackagePlus,
      to: '?nuevo=producto',
      permissions: { all: ['catalog.manage'] },
    },
    { key: 'buscar', label: 'Buscar en el catálogo', description: 'Productos por categoría, marca, plataforma o especificación, en lista o en galería.', icon: Search, to: '' },
  ],
  stats: [{ key: 'estado', title: 'Estado del catálogo', component: lazyScreen(() => import('./CatalogStat'), 'CatalogStat') }],
});

export default catalogo;
