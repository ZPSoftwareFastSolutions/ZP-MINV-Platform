// Módulo del panel (paquete M10): Administración › Usuarios. Personal y clientes web, roles y permisos de solo lectura,
// sucursales, contraseñas temporales y activación. Este archivo es LIVIANO a propósito: solo la definición, íconos y
// cargas diferidas.

import { KeyRound, UserPlus, Users } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const usuarios = defineModule({
  key: 'usuarios',
  section: 'administracion',
  title: 'Usuarios',
  description: 'Cuentas del personal y de los clientes web: roles, sucursales, contraseñas y lo que puede hacer cada rol.',
  icon: Users,
  order: 10,
  permissions: { all: ['iam.users.manage'] },
  routes: [{ path: '', title: 'Usuarios', element: lazyScreen(() => import('./UsersPage'), 'UsersPage') }],
  actions: [
    {
      key: 'nuevo',
      label: 'Nuevo usuario',
      description: 'Cree la cuenta de una persona del personal con su rol, sus sucursales y una contraseña inicial.',
      icon: UserPlus,
      to: '?accion=nuevo',
    },
    {
      key: 'restablecer',
      label: 'Restablecer una contraseña',
      description: 'Asigne una contraseña temporal y desbloquee la cuenta de una persona.',
      icon: KeyRound,
      to: '?accion=restablecer',
    },
  ],
  stats: [{ key: 'resumen', title: 'Usuarios del sistema', component: lazyScreen(() => import('./UsersStat'), 'UsersStat') }],
});

export default usuarios;
