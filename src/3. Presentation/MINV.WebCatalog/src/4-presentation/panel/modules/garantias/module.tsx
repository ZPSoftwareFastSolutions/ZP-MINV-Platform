// Módulo del panel: Tecnología › Garantías (la pantalla «Garantías y RMA» del escritorio, WarrantyClaimsView). Recibir un
// equipo en garantía (o como reparación con cargo), diagnosticarlo, enviarlo al proveedor, repararlo o reemplazarlo con
// otra unidad, rechazar la garantía y entregarlo, con su bitácora y la orden de servicio imprimible (reglas T-04 y T-05).
// Archivo LIVIANO: solo la definición, íconos y cargas diferidas.
//
// Permisos: se VE con `inventory.serials.view` (la lista y el detalle); abrir casos y agregar notas piden
// `service.rma.open`; avanzar el estado y entregar el reemplazo piden `service.rma.manage` (los botones solo aparecen con
// esos permisos).

import { ShieldCheck, ShieldPlus } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const garantias = defineModule({
  key: 'garantias',
  section: 'tecnologia',
  title: 'Garantías',
  description: 'Casos de garantía (RMA): recibir, diagnosticar, reparar o reemplazar y entregar equipos, con su orden de servicio.',
  icon: ShieldCheck,
  order: 30,
  permissions: { all: ['inventory.serials.view'] },
  routes: [{ path: '', title: 'Garantías', element: lazyScreen(() => import('./ClaimsPage'), 'ClaimsPage') }],
  actions: [
    {
      key: 'abrir',
      label: 'Abrir un caso de garantía',
      description: 'Reciba un equipo con falla: busque su serie, vea su garantía y registre la falla.',
      icon: ShieldPlus,
      to: '?abrir=1',
      permissions: { all: ['service.rma.open'] },
    },
  ],
});

export default garantias;
