// General › Inicio: el tablero del panel (su dirección es `/panel`). No ofrece botones ni estadísticas propias: muestra
// las que ofrecen los demás módulos a los que la sesión tiene acceso.

import { LayoutDashboard } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const inicio = defineModule({
  key: 'inicio',
  section: 'general',
  title: 'Inicio',
  description: 'Tablero con las funciones de su rol y las estadísticas plegadas.',
  icon: LayoutDashboard,
  order: 0,
  permissions: {},
  routes: [{ path: '', title: 'Inicio', element: lazyScreen(() => import('./InicioPage'), 'InicioPage') }],
});

export default inicio;
