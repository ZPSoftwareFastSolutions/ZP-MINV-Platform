// PUNTO DE MONTAJE DEL PANEL (`/panel/*`). El enrutador lo carga con carga diferida (todo el panel va en un fragmento
// aparte: quien solo visita la tienda no lo descarga) y ya lo protege con la guarda (solo sesión del personal).
//
// Paquete W3b: el panel real. `registry/discovery.ts` descubre los módulos (`modules/<clave>/module.tsx`) y `PanelApp`
// arma con ellos el esqueleto (menú por secciones según los permisos, barra superior con la sucursal activa, el usuario
// y las migas) y una ruta por pantalla. La guía para escribir un módulo está en `panel/README.md`.

import { PANEL_REGISTRY } from './registry/discovery';
import { PanelApp } from './shell/PanelApp';

export function PanelRoot() {
  return <PanelApp registry={PANEL_REGISTRY} />;
}
