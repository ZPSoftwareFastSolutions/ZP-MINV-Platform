# Panel del personal (V7) · Guía para escribir un módulo

Esta guía es TODO lo que necesita para agregar una pantalla al panel (`/panel`) sin tocar nada más: caja, ventas,
reservas, stock, catálogo, compras, facturación, reportes, usuarios… Cada módulo es una carpeta que se registra sola.
El módulo de ejemplo **`modules/actividad`** (Administración › Actividad) está completo y probado: copie de él.

Reglas que mandan (`.claude/v7-web-platform-rules.md`): P-01 (el servidor decide: la web no tiene lógica de negocio),
P-07 (tipos del servidor solo del contrato generado), P-08 (nada de red ni de almacenamiento fuera de la
infraestructura), P-09 (un módulo no importa de otro ni toca `shell/` ni `kit/`) y P-10 (tablero con botones;
estadísticas plegadas; listas con filtros, búsqueda y estados). La prueba `src/architecture.test.ts` vigila varias.

---

## 1. En un minuto

1. Cree la carpeta `src/4-presentation/panel/modules/<clave>/` (clave en minúsculas y con guiones: `ordenes-compra`).
2. Escriba `module.tsx`: exporta por defecto `defineModule({ … })` con la sección, el título, los permisos y sus
   pantallas (con carga diferida). El registro la descubre solo: el menú, las rutas, las migas, el buscador y el tablero
   aparecen solos, **solo para quien tiene los permisos**.
3. Escriba sus pantallas con el conjunto del panel: `Page`, `FilterBar`, `DataTable`, `SidePanel`, `Dialog`… (`kit`),
   `useRpcQuery`, `useRpcCommand`, `usePermissions`, `useTableState` (`hooks`) y `formatMoney`, `exportCsv`… (`lib`).
4. Pida datos al servidor por el **nombre de la operación** (`'GetActivityQuery'`) y use sus tipos derivados del
   contrato (`RpcResponseOf<'GetActivityQuery'>`). Nunca escriba a mano un tipo del servidor.
5. Pruebe: `npx vitest run src/4-presentation/panel/modules/<clave>` y después `npm run typecheck`, `npm run lint`,
   `npx vitest run` y `npm run build`.

---

## 2. Cómo está armado el panel

```text
src/4-presentation/panel/
  PanelRoot.tsx        punto de montaje de /panel/* (fragmento aparte: quien solo mira la tienda no lo descarga)
  registry/            el REGISTRO de módulos: tipos (defineModule, lazyScreen), secciones, reglas de acceso,
                       menú, tablero, buscador y migas. discovery.ts descubre modules/*/module.tsx.
  shell/               el ESQUELETO: menú lateral por secciones (cajón en el teléfono), barra superior (migas,
                       sucursal activa, usuario), «No tiene acceso a esta pantalla», «pantalla no encontrada».
  kit/                 los componentes (W3a). Cada archivo empieza con su comentario de USO.
  hooks/               useRpcQuery, useRpcCommand, usePermissions, useTableState.
  lib/                 funciones puras: formato (Bs, fechas de La Paz), rangos de fechas, tablas, CSV, errores.
  showcase/            la muestra /panel/_componentes (solo en desarrollo): todos los componentes funcionando.
  modules/
    inicio/            General › Inicio (el tablero, en /panel)
    actividad/         Administración › Actividad (el EJEMPLO completo)
    <su-módulo>/       ← lo suyo va aquí, y SOLO aquí
```

| Carpeta | ¿Quién la cambia? |
|---|---|
| `modules/<su-módulo>/` | Usted. |
| `registry/`, `shell/`, `kit/`, `hooks/`, `lib/` | Nadie desde un módulo (P-09). Si le falta algo, pídalo: se agrega para todos. |
| `3-infrastructure/http/contract.generated.ts` | Nadie a mano: lo genera `minv contrato-web` desde el servidor (ver §5.3). |
| `app/routeTable.tsx`, `PanelRoot.tsx` | Nadie: los módulos se registran solos. |

Para ver los componentes funcionando: `npm run dev` con `VITE_API_URL=mock` (PowerShell:
`$env:VITE_API_URL='mock'; npm run dev`), ingrese con el usuario de muestra del personal y abra
`http://localhost:5173/panel/_componentes` (enlace «Muestra de componentes» al pie del menú lateral). La pantalla de
lista de la muestra (`showcase/SalesDemo.tsx`) también sirve de modelo.

---

## 3. Crear un módulo

### 3.1 La carpeta

```text
modules/actividad/
  module.tsx               la definición (LIVIANA: solo metadatos, íconos y cargas diferidas)
  ActivityPage.tsx         la pantalla principal (/panel/actividad)
  ResetPasswordDialog.tsx  el diálogo de un comando
  ActivityTodayStat.tsx    la estadística que ofrece al tablero (plegada)
  activity.ts              funciones puras del módulo (textos, filtros, columnas del CSV) — se prueban sin React
  activity.test.ts         pruebas de las funciones puras
  ActivityPage.test.tsx    pruebas de la pantalla (servidor en memoria)
```

Todo lo del módulo vive en su carpeta: sus componentes, sus funciones, sus estados (`defineStatuses`) y sus pruebas.
Nada de otro módulo se importa (si dos módulos necesitan lo mismo, se pide para `kit/` o `lib/`).

### 3.2 `module.tsx` mínimo

```tsx
import { History } from 'lucide-react';
import { defineModule, lazyScreen } from '@/4-presentation/panel/registry';

const actividad = defineModule({
  key: 'actividad', //                  = nombre de la carpeta = su dirección: /panel/actividad
  section: 'administracion', //         una de las 9 secciones (§3.4)
  title: 'Actividad', //                nombre en el menú
  description: 'Quién hizo qué, cuándo y con qué resultado (auditoría).', // una línea (buscador del menú)
  icon: History, //                     un ícono de lucide-react (el componente, no <History />)
  order: 40, //                         orden dentro de la sección (decenas: 10, 20, 30…)
  permissions: { all: ['iam.audit.view'] }, // quién ve el módulo (§3.3)
  routes: [{ path: '', title: 'Actividad', element: lazyScreen(() => import('./ActivityPage'), 'ActivityPage') }],
});

export default actividad;
```

- **Exporte por defecto una constante con nombre** (`const actividad = defineModule(…); export default actividad;`):
  `export default defineModule(…)` directo hace que el linter avise.
- **`module.tsx` NO importa sus pantallas de forma estática**: van con `lazyScreen(() => import('./X'), 'X')` (se
  descargan recién al abrirlas, cada una en su fragmento). `lazyScreen` sirve para exportaciones CON NOMBRE (la
  convención del proyecto: `export function ActivityPage()`).
- Si algo está mal (clave distinta de la carpeta, sección inexistente, permiso mal escrito, falta la pantalla
  principal, una ruta con «/» al inicio…), el módulo **no se carga**, la prueba `registry/registry.test.ts` falla con el
  motivo en palabras y, en `npm run dev`, el panel lo avisa arriba de la pantalla.

### 3.3 Los campos de la definición

| Campo | Tipo | Qué es |
|---|---|---|
| `key` | texto | Clave = carpeta = dirección `/panel/<clave>`. Minúsculas, números y guiones. `inicio` es el tablero (`/panel`). |
| `section` | clave de sección | `general`, `ventas`, `tecnologia`, `inventario`, `compras`, `sucursales`, `facturacion`, `analisis` o `administracion`. |
| `title` | texto | Nombre en el menú, en las migas y en el buscador. |
| `description` | texto | Una línea: para qué sirve (el buscador también busca aquí). |
| `icon` | ícono de lucide-react | El componente (`History`), no un elemento (`<History />`). |
| `order` | número | Orden dentro de la sección (empates: por título). Use la tabla de §3.4. |
| `permissions` | `{ any?: string[], all?: string[] }` | Quién VE el módulo: `all` = hacen falta todos; `any` = basta uno; los dos a la vez = todos los de `all` y al menos uno de `any`; `{}` = cualquier sesión del personal. Los códigos tienen que existir en el contrato (`PERMISSION_LIST`). |
| `licenseModules?` | `string[]` | Módulos comerciales que exige el servidor (`[RequiresModule]`: `MULTI_BRANCH`, `API_INTEGRATIONS`, `GLOBAL_AUDIT`…). Hoy la sesión web no informa la licencia: no filtra (el servidor rechaza y el aviso lo explica); cuando la sesión la traiga, el menú la usará sin cambiar su módulo. |
| `routes` | `{ path, title, element, permissions? }[]` | Sus pantallas. `path` es RELATIVA al módulo, sin «/» al inicio: `''` = la principal (obligatoria), `'nueva'` = `/panel/<clave>/nueva`, `':numero'` = `/panel/<clave>/:numero` (se lee con `useParams()`). `permissions` = permisos EXTRA de esa pantalla. |
| `actions?` | `{ key, label, description, icon, to, permissions? }[]` | Botones grandes que ofrece al tablero de «Inicio» (§8). `to` es RELATIVA al módulo: `''`, `'nueva'`, `'?estado=abierta'`. |
| `stats?` | `{ key, title, component, permissions? }[]` | Estadísticas que ofrece al tablero, PLEGADAS detrás de «Ver estadísticas» (§8). |

Las acciones, las estadísticas y las pantallas se ven solo si la sesión cumple los permisos del MÓDULO **y** los suyos.
Todas las pantallas se registran aunque la sesión no tenga permiso: abrir una por dirección muestra «No tiene acceso a
esta pantalla» con el permiso que falta en palabras (no «no encontrada»). Ocultar es comodidad: el servidor vuelve a
decidir en cada pedido (P-01).

### 3.4 Claves, secciones y orden sugeridos (diseño §7)

Úsenlos para no pisarse. Los permisos son los del diseño: confírmelos con los `[RequiresPermission]` de las
operaciones que usa (el contrato los trae en `RPC_OPERATIONS[operación].permissions`).

| Sección | Módulo | `key` | `order` | `permissions` |
|---|---|---|---|---|
| General | Inicio | `inicio` (existe) | 0 | `{}` |
| Ventas | Caja | `caja` | 10 | `{ all: ['sales.pos.operate'] }` |
| Ventas | Ventas | `ventas` | 20 | `{ all: ['sales.view'] }` |
| Ventas | Clientes | `clientes` | 30 | `{ all: ['sales.customers.manage'] }` |
| Ventas | Reservas | `reservas` | 40 | `{ all: ['sales.pcbuild.manage'] }` |
| Tecnología | Armador de PC | `armador` | 10 | `{ all: ['sales.view', 'inventory.stock.view'] }` |
| Tecnología | Series | `series` | 20 | `{ all: ['inventory.serials.view'] }` |
| Tecnología | Garantías | `garantias` | 30 | `{ all: ['inventory.serials.view'] }` |
| Inventario | Stock | `stock` | 10 | `{ all: ['inventory.stock.view'] }` |
| Inventario | Catálogo | `catalogo` | 20 | `{ all: ['catalog.manage'] }` |
| Inventario | Movimientos | `movimientos` | 30 | `{ any: ['inventory.movements.register.warehouse', 'inventory.movements.register.sales'] }` |
| Inventario | Toma física | `toma-fisica` | 40 | `{ all: ['inventory.counts.record'] }` |
| Inventario | Alertas | `alertas` | 50 | `{ all: ['inventory.stock.view'] }` |
| Compras | Pedido sugerido | `pedido-sugerido` | 10 | `{ all: ['inventory.stock.view'] }` |
| Compras | Órdenes de compra | `ordenes-compra` | 20 | `{ all: ['purchasing.manage'] }` |
| Compras | Proveedores | `proveedores` | 30 | `{ all: ['purchasing.manage'] }` |
| Sucursales | Sucursales | `sucursales` | 10 | `{ any: ['reports.view', 'corporate.branches.all', 'corporate.branches.manage'] }` |
| Sucursales | Transferencias | `transferencias` | 20 | `{ all: ['inventory.transfers.manage'] }` |
| Facturación | Documentos | `documentos` | 10 | `{ all: ['billing.view'] }` |
| Facturación | Estado del SIAT | `siat` | 20 | `{ all: ['billing.view'] }` |
| Facturación | Homologación | `homologacion` | 30 | `{ all: ['billing.view'] }` |
| Facturación | Libros | `libros` | 40 | `{ all: ['billing.view'] }` |
| Análisis | Reportes | `reportes` | 10 | `{ all: ['reports.view'] }` |
| Análisis | Contabilidad | `contabilidad` | 20 | `{ all: ['accounting.manage'] }` |
| Administración | Usuarios | `usuarios` | 10 | `{ all: ['iam.users.manage'] }` |
| Administración | Integraciones | `integraciones` | 20 | `{ all: ['integration.manage'] }` |
| Administración | Configuración | `configuracion` | 30 | `{ all: ['billing.configure'] }` |
| Administración | Actividad | `actividad` (existe) | 40 | `{ all: ['iam.audit.view'] }` |

### 3.5 La definición completa del ejemplo

```tsx
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
```

---

## 4. Qué pone el esqueleto (y usted NO)

- **Márgenes laterales** del contenido: `Page` no los agrega y usted tampoco.
- **Migas de pan** en la barra superior: `Inicio › Sección › Módulo › Pantalla`, armadas con el registro (el título de
  cada ruta). **No pase `breadcrumbs` a `Page`.**
- **Título de la pestaña**: `Page` lo fija con su `title` («Actividad · Panel · Tech Zone Gaming»).
- **Sucursal activa**: la lista de la barra superior envía `SelectBranchCommand` y vuelve a leer la sesión. Cada
  `useRpcQuery` de su pantalla se repite solo con la sucursal nueva (y no muestra datos de la otra mientras tanto).
  **No haga su propio selector de sucursal activa.** Un filtro «Sucursal» de un reporte es un filtro común (va en el
  pedido o en la página).
- **Usuario** (cambiar contraseña, ir a la tienda, cerrar sesión), **menú por permisos**, **buscador de pantallas**,
  **cajón en el teléfono**, «No tiene acceso a esta pantalla», «No encontramos esta pantalla» y el **foco**: al cambiar
  de pantalla la vista vuelve arriba y el foco pasa al contenido.
- **Carga diferida** de su pantalla (con esqueleto mientras llega) y un **límite de errores** propio: si su pantalla
  falla al dibujarse, el menú sigue funcionando y se ofrece «Reintentar».

---

## 5. El servidor: operaciones, tipos y el contrato generado

La web envía los MISMOS comandos y consultas de `MINV.Application` por `POST /api/v1/web/rpc` (P-01). Usted nunca usa
`fetch` ni el RPC a mano: usa `useRpcQuery` (consultas) y `useRpcCommand` (comandos).

### 5.1 El nombre de la operación y sus tipos

- El **nombre** de una operación es el nombre de la clase C# sin el espacio de nombres: `GetActivityQuery`,
  `ResetUserPasswordCommand`. Es la clave del mapa `RpcOperations` del contrato.
- Los **tipos** se DERIVAN del nombre, siempre desde el adaptador `@/4-presentation/app/contract` (nunca desde
  `contract.generated.ts`, que solo importa `3-infrastructure/http/contract.ts`):

```ts
import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';

/** Una fila de `GetActivityQuery` tal como la manda el servidor. */
export type ActivityRecord = RpcResponseOf<'GetActivityQuery'>[number];
/** Un campo de la fila (en el provisional: 'Succeeded' | 'Rejected' | 'Failed'). */
type Outcome = ActivityRecord['outcome'];
/** El pedido de un comando. */
type ResetPayload = RpcRequestOf<'ResetUserPasswordCommand'>;
```

Por qué derivarlos: el generado puede nombrar distinto los tipos internos (una clase anidada, un nombre repetido en
otro espacio de nombres), pero el mapa de operaciones sigue indexado por el nombre de la operación. Derivados del
nombre, sus tipos son los mismos con el provisional y con el generado. **No declare `interface` ni `type` con el nombre
de un tipo del servidor ni terminados en `Query` o `Command`: la prueba de arquitectura lo rechaza (P-07).** Sí puede
declarar tipos PROPIOS de la pantalla (una fila «lista para mostrar», los filtros), como `ActivityItem` en
`modules/actividad/activity.ts`.

También del adaptador: `permissionName(código)` (el nombre del permiso en español), `roleName(código)`,
`rpcOperation(nombre)` (tipo completo, si es comando, permisos y módulos que exige), `PERMISSION_LIST` y `ROLE_LIST`.

### 5.2 Cómo viajan los datos (C# → TypeScript)

| C# | En la página | Cómo mostrarlo |
|---|---|---|
| propiedades de un `record` | `camelCase` (`OccurredAt` → `occurredAt`) | — |
| `DateTimeOffset` | texto ISO 8601 con zona (`"2026-09-29T14:00:00Z"`) | `formatDateTime`, `formatDate`, `formatTime` (hora de La Paz) |
| `DateOnly` | `"2026-09-28"` | `formatDate` (nunca se corre de día) |
| `decimal`, `int` | `number` | `formatMoney`, `formatQuantity`, `formatNumber` |
| `Guid` | texto | — |
| `enum` | texto con el nombre del miembro (`"Rejected"`) | `defineStatuses` + `StatusBadge` |
| `T?` | `T \| null` | el kit muestra «—» si falta |
| `IReadOnlyList<T>` | `T[]` | — |

Al ENVIAR:

- **Envíe SIEMPRE todos los parámetros**, también los que en C# tienen un valor por defecto (`GetActivityQuery(int Take
  = 200)` → `{ take: 500 }`): así funciona tanto si el generado los marca opcionales como obligatorios.
- «Sin filtro» en un parámetro que admite nulo es `null` explícito.
- Fechas: un `DateOnly` va como `"2026-09-28"` (los rangos de `useTableState` ya están en ese formato); un
  `DateTimeOffset` va como `dayStart(dia).toISOString()` / `dayEnd(dia).toISOString()` (límites del día en La Paz, de
  `lib`).
- Una consulta sin parámetros se envía con `{}`.

Al LEER, escriba código que funcione aunque el generado sea más o menos estricto que el provisional:

- Compare con textos (`fila.outcome === 'Rejected'`) y use mapas de estados (`defineStatuses({ Rejected: … })`); no
  anote variables con un tipo más estrecho que el del contrato.
- Trate siempre el nulo (`fila.userName ?? fila.userEmail ?? 'Sistema'`).

### 5.3 El contrato es GENERADO (regla P-07)

`src/3-infrastructure/http/contract.generated.ts` lo genera `minv contrato-web` desde `RpcCatalog` del servidor: trae
TODAS las operaciones (192), sus tipos exactos, permisos, módulos comerciales y si una sesión de cliente puede usarlas.
Una prueba del servidor falla si queda desactualizado. Por eso:

- NO lo edite a mano (ni agregue operaciones): se regenera y se pierde.
- Use el nombre corto de la operación y los tipos derivados (§5.1) SOLO a través del adaptador.
- Si `typecheck` marca una diferencia (un campo que admite nulo, un parámetro obligatorio), corrija SU módulo.
- Los nombres visibles de los permisos salen de `PERMISSION_LIST` / `permissionName(code)`: nunca los escriba a mano en
  el código ni en las pruebas (cambian si cambia la descripción en el servidor).

### 5.4 Si una operación que necesita NO está en el contrato

Entonces no existe en el servidor (o no es pública). NO la invente en la web: anótela en «Pendientes» de su informe con
lo que haría falta (nombre sugerido, parámetros, respuesta, permiso) y resuelva la pantalla con las operaciones que sí
existen. El equipo del servidor la agrega y regenera el contrato.

### 5.5 Consultas: `useRpcQuery`

```ts
const actividad = useRpcQuery('GetActivityQuery', { take });            // carga al montar y cuando cambia el pedido
const filas = actividad.data ?? [];                                     // undefined hasta la primera respuesta
// actividad.loading   → primera carga (esqueleto)       actividad.fetching → hay un pedido en curso (recarga, filtro)
// actividad.error     → falla del último pedido          actividad.reload() → «Reintentar» / «Actualizar»
// actividad.setData(…) → cambiar los datos en la página después de un comando que ya devolvió la fila nueva
// useRpcQuery('X', pedido, { enabled: false })       → no consulta (hasta que se elija un filtro obligatorio)
```

- El pedido se compara por VALOR: un objeto nuevo en cada dibujo no repite la consulta; cambiar un filtro del pedido,
  sí (y la respuesta vieja se descarta).
- Se repite solo al cambiar la sucursal activa. Un 401 cierra la sesión solo.

### 5.6 Comandos: `useRpcCommand`

```ts
const reset = useRpcCommand('ResetUserPasswordCommand', {
  success: (_result, payload) => `Contraseña temporal asignada a ${payload.email}`, // aviso al terminar bien (o false)
  notifyError: false, //                                  el error se muestra en el formulario con reset.errorText
});
const outcome = await reset.run({ email, newPassword: password, mustChange }); // NUNCA lanza
if (outcome.ok) cerrar();                                                       // si no: outcome.error / outcome.message
// reset.sending → botón ocupado (un doble clic no envía dos veces) · reset.reset() → olvidar el error
```

- **Idempotencia** (B-09): cada intento viaja con un `requestId`. Reintentar LO MISMO (tras una falla de red) usa el
  mismo id: si el servidor ya lo había hecho, devuelve la respuesta guardada. Tras un éxito, o si cambia el contenido,
  el id es nuevo. No lo maneje a mano.
- Si el servidor rechaza por permisos, el aviso dice QUÉ permiso falta, en palabras.
- Con `ConfirmDialog`, pase directamente `onConfirm={() => comando.run(…)}`: se cierra solo si salió bien.
- Después de un comando que cambia la lista: `consulta.reload()` o `consulta.setData(…)` con lo que devolvió.

### 5.7 Permisos dentro de las pantallas

```ts
const { can, canAny, canRun, session } = usePermissions();
canRun('ResetUserPasswordCommand')   // todos los permisos que el contrato declara para ESA operación
can('sales.pos.operate')             // un permiso
```

- Oculte (o no ofrezca) lo que la sesión no puede hacer: botones con `canRun`, acciones de fila con
  `hidden: !canRun(…)` o `permission: '…'`, bloques con `<PermissionGate operation="…">`. Es comodidad: el servidor
  decide igual (P-01).
- Un rechazo que igual llegue se explica solo (avisos de `useRpcCommand`, `ErrorState` de la tabla).

---

## 6. Pantalla de lista (copiada del ejemplo)

Toda lista del panel sigue el mismo patrón (P-10): **filtros con listas desplegables y búsqueda (en la dirección de la
página) → barra de acciones (actualizar, exportar CSV) → tabla ordenable y paginada con acciones por fila → detalle en
un panel lateral → diálogos de los comandos**, con estados de carga, vacío y error con «Reintentar».

- `useTableState({ filters, sort })` guarda filtros, orden, página y filas por página en la DIRECCIÓN (se puede
  recargar o compartir el enlace). Nombres reservados: `orden`, `sentido`, `pagina`, `filas`. Un rango `desde` y `hasta`
  cuenta como UN filtro. Nada se guarda en el navegador.
- Lo que filtra el SERVIDOR va en el pedido de `useRpcQuery` (cambiarlo vuelve a consultar); lo que se filtra en la
  página va en un `useMemo`. `DataTable` recibe las filas YA filtradas (ordena y pagina sola).
- Columnas FUERA del componente (o en `useMemo`). `value` ordena y exporta; `cell` dibuja; `card: 'title'` es el título
  de la tarjeta en el teléfono y la columna que abre el detalle.
- Estados (`defineStatuses`) y funciones puras en un archivo aparte del módulo (`activity.ts`), con sus pruebas.

Las funciones puras del ejemplo (el principio de `modules/actividad/activity.ts`):

```ts
import type { RpcResponseOf } from '@/4-presentation/app/contract';
import { defineStatuses, statusOf } from '@/4-presentation/panel/kit';
import { formatDate, formatDateTime, formatMoney, inRange, laPazToday, matchesSearch, toDate, toIsoDate, type CsvColumn } from '@/4-presentation/panel/lib';

/** Una fila de `GetActivityQuery` tal como la manda el servidor. */
export type ActivityRecord = RpcResponseOf<'GetActivityQuery'>[number];

/** Cuántas filas se piden al servidor (el servidor acepta de 1 a 5000; la lista filtra en la página). */
export const TAKE_OPTIONS: readonly { value: string; label: string }[] = [
  { value: '200', label: 'Últimos 200' },
  { value: '500', label: 'Últimos 500' },
  { value: '1000', label: 'Últimos 1.000' },
  { value: '5000', label: 'Últimos 5.000' },
];
export const DEFAULT_TAKE = '500';

/** `?registros=` de la dirección → cantidad para el servidor (un valor raro vuelve al de siempre). */
export function takeOf(value: string): number {
  return Number(TAKE_OPTIONS.some((option) => option.value === value) ? value : DEFAULT_TAKE);
}

/** Resultado de la operación (enumeración `AuditOutcome` del servidor). */
export const OUTCOMES = defineStatuses({
  Succeeded: { label: 'Correcto', tone: 'success' },
  Rejected: { label: 'Rechazado', tone: 'warning' },
  Failed: { label: 'Con error', tone: 'danger' },
});

/** Filtros de la lista (en la dirección de la página). */
export const ACTIVITY_FILTERS = { q: '', usuario: '', resultado: '', desde: '', hasta: '', registros: DEFAULT_TAKE };
export type ActivityFilters = typeof ACTIVITY_FILTERS;
```

(El archivo sigue con `ActivityItem` —la fila lista para mostrar—, `toActivityItems`, `userOptions`, `filterActivity`,
`CSV_COLUMNS`, `todaySummary` y la contraseña temporal: léalo entero en `modules/actividad/activity.ts`.)

La pantalla completa (`modules/actividad/ActivityPage.tsx`):

```tsx
// Administración › Actividad (módulo de EJEMPLO del panel: el modelo a copiar). Quién hizo qué, cuándo y con qué
// resultado: la auditoría del servidor (`GetActivityQuery`), con filtros en la dirección (búsqueda, usuario, resultado,
// fechas y cuántos registros traer), tabla ordenable y paginada, detalle lateral, exportar CSV y un comando
// (`ResetUserPasswordCommand`: contraseña temporal y desbloqueo, solo para quien administra usuarios).
//
// Patrón de toda lista del panel:
//   1. `useTableState`  → filtros, orden y página en la dirección (se puede recargar o compartir el enlace).
//   2. `useRpcQuery`    → el pedido al servidor con lo que filtra el servidor (aquí, `take`); se repite solo si cambia.
//   3. `useMemo`        → lo que se filtra en la página (usuario, resultado, fechas y búsqueda).
//   4. `<Page>` → `<FilterBar>` → `<Toolbar>` → `<DataTable>` → `<SidePanel>` (y los diálogos de los comandos).

import { Download, Eye, Filter, History, KeyRound, RefreshCw } from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import { usePermissions, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Button,
  DataTable,
  DateRangeField,
  DetailList,
  FilterBar,
  Page,
  SearchField,
  SelectField,
  SidePanel,
  StatusBadge,
  Toolbar,
  statusOptions,
  useNotify,
  type DataTableColumn,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatDateLong, formatDateTime, formatNumber, formatTime } from '@/4-presentation/panel/lib';
import {
  ACTIVITY_FILTERS,
  CSV_COLUMNS,
  OUTCOMES,
  SYSTEM_USER,
  TAKE_OPTIONS,
  detailFields,
  filterActivity,
  outcomeLabel,
  takeOf,
  toActivityItems,
  userOptions,
  type ActivityItem,
} from './activity';
import { ResetPasswordDialog, type ResetTarget } from './ResetPasswordDialog';

/** Columnas de la tabla: fuera del componente (así no se vuelven a crear ni a ordenar en cada dibujo). */
const COLUMNS: DataTableColumn<ActivityItem>[] = [
  {
    id: 'fecha',
    header: 'Fecha y hora',
    value: (item) => new Date(item.record.occurredAt),
    cell: (item) => formatDateTime(item.record.occurredAt),
    className: 'whitespace-nowrap',
  },
  {
    id: 'usuario',
    header: 'Usuario',
    value: (item) => item.who,
    cell: (item) => (
      <span className="block min-w-36">
        <span className="block">{item.who}</span>
        {item.email && item.email !== item.who && <span className="block text-xs text-text-muted">{item.email}</span>}
      </span>
    ),
  },
  { id: 'accion', header: 'Acción', value: (item) => item.actionText, card: 'title', className: 'min-w-48' },
  {
    id: 'resultado',
    header: 'Resultado',
    value: (item) => outcomeLabel(item.record.outcome),
    cell: (item) => <StatusBadge status={item.record.outcome} statuses={OUTCOMES} />,
  },
  {
    id: 'detalle',
    header: 'Detalle',
    value: (item) => item.summary,
    sortable: false,
    cell: (item) => <span className="line-clamp-2 block max-w-md min-w-48 text-text-muted">{item.summary || '—'}</span>,
  },
];

export function ActivityPage() {
  const notify = useNotify();
  const { canRun } = usePermissions();
  const table = useTableState({ filters: ACTIVITY_FILTERS, sort: { column: 'fecha', direction: 'desc' } });
  const filters = table.filters;
  const take = takeOf(filters.registros);

  // Lo que filtra el SERVIDOR va en el pedido (cambiarlo vuelve a consultar); el resto se filtra en la página.
  const activity = useRpcQuery('GetActivityQuery', { take });
  const items = useMemo(() => toActivityItems(activity.data ?? []), [activity.data]);
  const rows = useMemo(() => filterActivity(items, filters), [items, filters]);
  const users = useMemo(() => userOptions(items), [items]);

  // Detalle abierto (se guarda la fila y si está abierto: al cerrar, el panel se desliza con su contenido).
  const [detail, setDetail] = useState<{ item: ActivityItem; open: boolean } | null>(null);
  const [resetting, setResetting] = useState<ResetTarget | null>(null);
  const canReset = canRun('ResetUserPasswordCommand');
  const recordedTitleId = useId();

  const openDetail = (item: ActivityItem) => setDetail({ item, open: true });
  const onlyThisUser = (item: ActivityItem) => {
    table.setFilter('usuario', item.userValue);
    setDetail((current) => (current ? { ...current, open: false } : current));
  };
  const askReset = (item: ActivityItem) => {
    if (item.email) setResetting({ email: item.email, name: item.who });
  };

  const exportRows = () => {
    const file = exportCsv('actividad', CSV_COLUMNS, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const rejected = rows.filter((row) => row.record.outcome !== 'Succeeded').length;
  const item = detail?.item;
  const recorded = item ? detailFields(item.record.details) : [];

  return (
    <Page title="Actividad" description="Quién hizo qué, cuándo y con qué resultado. Cada operación del sistema queda registrada y no se puede borrar.">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Acción, usuario o detalle" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Usuario" value={filters.usuario} onChange={(value) => table.setFilter('usuario', value)} options={users} />
        <SelectField label="Resultado" value={filters.resultado} onChange={(value) => table.setFilter('resultado', value)} options={statusOptions(OUTCOMES)} />
        <SelectField
          label="Registros a revisar"
          allLabel={false}
          value={filters.registros}
          onChange={(value) => table.setFilter('registros', value || ACTIVITY_FILTERS.registros)}
          options={TAKE_OPTIONS}
          hint="Los más recientes; los filtros se aplican sobre ellos."
        />
        <DateRangeField label="Fechas" value={table.dateRange()} onChange={(range) => table.setDateRange(range)} />
      </FilterBar>

      <Toolbar
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={activity.fetching && !activity.loading} onClick={activity.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {activity.data && (
          <span className="text-sm text-text-muted" data-testid="actividad-resumen">
            {formatNumber(rows.length)} de {formatNumber(items.length)} registros · {formatNumber(rejected)} rechazados o con error
          </span>
        )}
      </Toolbar>

      <DataTable
        caption="Actividad del sistema"
        columns={COLUMNS}
        rows={activity.data ? rows : undefined}
        rowKey={(row) => row.key}
        rowLabel={(row) => `la operación «${row.actionText}» de ${row.who}`}
        loading={activity.loading}
        refreshing={activity.fetching && !activity.loading}
        error={activity.error}
        onRetry={activity.reload}
        operation="GetActivityQuery"
        {...table.tableProps}
        onRowOpen={openDetail}
        activeRowKey={detail?.open ? detail.item.key : null}
        rowActions={(row) => [
          { label: 'Ver detalle', icon: <Eye />, onSelect: () => openDetail(row) },
          { label: 'Ver solo este usuario', icon: <Filter />, onSelect: () => onlyThisUser(row), hidden: filters.usuario === row.userValue },
          {
            label: 'Asignar contraseña temporal',
            icon: <KeyRound />,
            onSelect: () => askReset(row),
            hidden: !canReset || row.userValue === SYSTEM_USER,
          },
        ]}
        empty={{
          title: items.length === 0 ? 'Todavía no hay actividad registrada' : 'No hay actividad con estos filtros',
          description: items.length === 0 ? undefined : 'Pruebe con otras fechas, revise más registros o limpie los filtros.',
          icon: <History />,
          action:
            items.length === 0 ? undefined : (
              <Button variant="outline" onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
        }}
      />

      <SidePanel
        open={detail?.open ?? false}
        onClose={() => setDetail((current) => (current?.open ? { ...current, open: false } : current))}
        title={item ? item.actionText : 'Operación'}
        description={item ? formatDateTime(item.record.occurredAt) : undefined}
        headerExtra={item && <StatusBadge status={item.record.outcome} statuses={OUTCOMES} />}
        footer={
          item && (
            <div className="flex flex-col gap-2">
              {item.userValue !== filters.usuario && (
                <Button variant="outline" leftIcon={<Filter />} fullWidth onClick={() => onlyThisUser(item)}>
                  Ver solo la actividad de {item.who}
                </Button>
              )}
              {canReset && item.email && (
                <Button leftIcon={<KeyRound />} fullWidth onClick={() => askReset(item)}>
                  Asignar contraseña temporal
                </Button>
              )}
            </div>
          )
        }
      >
        {item && (
          <div className="space-y-5">
            <DetailList
              items={[
                { label: 'Fecha', value: formatDateLong(item.record.occurredAt) },
                { label: 'Hora', value: formatTime(item.record.occurredAt) },
                { label: 'Usuario', value: item.who },
                { label: 'Correo', value: item.email },
                { label: 'Resultado', value: <StatusBadge status={item.record.outcome} statuses={OUTCOMES} /> },
                { label: 'Código de la operación', value: item.record.action },
                { label: 'Detalle', value: item.summary, wide: true },
              ]}
            />
            {recorded.length > 0 && (
              <section aria-labelledby={recordedTitleId}>
                <h3 id={recordedTitleId} className="text-sm font-semibold text-text">
                  Datos registrados
                </h3>
                <DetailList className="mt-2" items={recorded} />
              </section>
            )}
          </div>
        )}
      </SidePanel>

      <ResetPasswordDialog target={resetting} onClose={() => setResetting(null)} />
    </Page>
  );
}
```

Notas:

- El detalle guarda la fila y si está abierto (`{ item, open }`): al cerrar, el panel se desliza sin perder su
  contenido. `onClose` puede ser una función nueva en cada dibujo.
- Si el detalle necesita más datos del servidor, pídalos con otra consulta habilitada solo con el panel abierto:
  `useRpcQuery('Get…Query', { number: abierta ?? '' }, { enabled: abierta !== null })` y pase `loading`, `error` y
  `onRetry` a `SidePanel`.
- Tablas anchas (8 columnas o más): deje los detalles en `renderExpanded` o en el panel lateral.
- No use `Button size="sm"` (mide 36 px en escritorio): los objetivos del panel son de 44 px.

---

## 7. Un comando con formulario (copiado del ejemplo)

`modules/actividad/ResetPasswordDialog.tsx`: el diálogo valida en la página (comodidad), envía el comando y muestra el
error del servidor DENTRO del diálogo, sin cerrarlo, para corregir y reintentar.

```tsx
// Módulo «Actividad» · el COMANDO de ejemplo: asignar una contraseña temporal a un usuario (`ResetUserPasswordCommand`),
// que además desbloquea su cuenta. Sirve cuando la actividad muestra ingresos rechazados o una cuenta bloqueada. Solo lo
// ofrece a quien puede ejecutarlo (`canRun`), y el servidor igual decide (regla P-01).
//
// Patrón de un comando con formulario:
//   - `useRpcCommand(operación, { success, notifyError: false })`: el aviso de éxito sale solo; el error se muestra DENTRO
//     del diálogo (`errorText`), sin cerrarlo, para corregir y reintentar (el reintento viaja con el mismo `requestId`).
//   - `run(contenido)` nunca lanza: se mira `ok`.
//   - La contraseña vive solo en el estado del diálogo y se borra al cerrarlo (nada se guarda ni se registra).

import { KeyRound, WandSparkles } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Button, Checkbox, Dialog, Form, TextField } from '@/4-presentation/panel/kit';
import { generateTemporaryPassword, temporaryPasswordProblem } from './activity';

/** A quién se le asigna la contraseña. */
export interface ResetTarget {
  email: string;
  name: string;
}

export interface ResetPasswordDialogProps {
  /** null = diálogo cerrado. */
  target: ResetTarget | null;
  onClose: () => void;
}

export function ResetPasswordDialog({ target, onClose }: ResetPasswordDialogProps) {
  const formId = useId();
  const [password, setPassword] = useState('');
  const [mustChange, setMustChange] = useState(true);
  const [touched, setTouched] = useState(false);
  const reset = useRpcCommand('ResetUserPasswordCommand', {
    success: (_result, payload) => `Contraseña temporal asignada a ${payload.email}`,
    notifyError: false,
  });
  // El último destinatario sigue a la vista mientras el diálogo se cierra (animación de salida).
  const [shown, setShown] = useState<ResetTarget | null>(target);
  if (target && target !== shown) setShown(target);

  const problem = temporaryPasswordProblem(password);

  const close = () => {
    setPassword('');
    setMustChange(true);
    setTouched(false);
    reset.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (!target || problem) return;
    const outcome = await reset.run({ email: target.email, newPassword: password, mustChange });
    if (outcome.ok) close();
  };

  return (
    <Dialog
      open={target !== null}
      onClose={close}
      dismissible={!reset.sending}
      title="Asignar una contraseña temporal"
      description={shown ? `${shown.name} · ${shown.email}` : undefined}
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={reset.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<KeyRound />} loading={reset.sending}>
            Asignar contraseña
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={reset.errorText} busy={reset.sending}>
        <p className="text-sm text-text-muted">
          La cuenta se desbloquea y la persona ingresa con esta contraseña. Comuníquesela por un medio seguro; no queda registrada en ningún lado.
        </p>
        <TextField
          label="Contraseña temporal"
          value={password}
          onChange={(value) => {
            setPassword(value);
            if (reset.error) reset.reset();
          }}
          error={touched && problem ? problem : undefined}
          hint="De 8 a 128 caracteres, con letras y números."
          autoComplete="off"
          autoCapitalize="none"
          spellCheck={false}
          maxLength={128}
          required
          data-autofocus
        />
        <Button variant="subtle" leftIcon={<WandSparkles />} onClick={() => setPassword(generateTemporaryPassword())}>
          Generar una contraseña
        </Button>
        <Checkbox
          label="Pedir que la cambie al ingresar"
          description="Recomendado: la temporal deja de servir en cuanto la persona elige la suya."
          checked={mustChange}
          onChange={setMustChange}
        />
      </Form>
    </Dialog>
  );
}
```

Un comando que solo pide confirmación (anular, liberar) no necesita formulario:

```tsx
<ConfirmDialog open={confirmar} onClose={() => setConfirmar(false)} tone="danger"
  title="¿Anular la venta F-CM-000123?" message="Se devuelve el stock y se anula la factura en el SIN."
  confirmLabel="Anular venta" onConfirm={() => anular.run({ number })} error={anular.errorText} />
```

Contraseñas y datos personales: solo en el estado del componente mientras hace falta; nunca en la dirección, en el
navegador ni en un aviso.

---

## 8. El tablero: botones y estadísticas plegadas

El cliente pidió un tablero simple: **las funciones como botones y las estadísticas PLEGADAS detrás de «Ver»** (P-10).
Un módulo no dibuja nada en «Inicio»: lo OFRECE en su definición y el tablero lo muestra solo a quien tiene acceso.

- **`actions`** → botones grandes en «¿Qué quiere hacer?», agrupados por sección. `label` con un verbo («Abrir caja»,
  «Ver la actividad»), `description` de una línea, `to` relativa al módulo (puede llevar filtros: `'?resultado=Rejected'`).
- **`stats`** → tarjetas dentro de «Ver estadísticas» (cerrado al entrar). Cada `component` se descarga y consulta
  recién al abrirlo, con su propio límite de errores: una que falla no tapa a las demás. El título lo pone el tablero.
- **Nada de indicadores ni gráficos a la vista al entrar**, tampoco en sus pantallas: si una pantalla quiere mostrar
  números o gráficos, van dentro de un `Collapsible` («Ver …») cerrado.

La estadística del ejemplo (`modules/actividad/ActivityTodayStat.tsx`):

```tsx
// Módulo «Actividad» · la ESTADÍSTICA que ofrece al tablero: «Actividad de hoy». Vive plegada detrás de «Ver
// estadísticas» (regla P-10): este componente se descarga y hace su consulta recién cuando se abre, con su propio estado
// de carga y de error. El título lo pone el tablero.

import { Activity, CircleX, History, ShieldAlert } from 'lucide-react';
import { useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { BarList, Button, ErrorState, StatCard } from '@/4-presentation/panel/kit';
import { formatNumber, formatTime } from '@/4-presentation/panel/lib';
import { todaySummary } from './activity';

/** Registros que se revisan para el resumen de hoy. */
const STAT_TAKE = 1000;

export function ActivityTodayStat() {
  const activity = useRpcQuery('GetActivityQuery', { take: STAT_TAKE });
  // «Hoy» se fija al abrir (una estadística abierta a medianoche no cambia de día sola).
  const [now] = useState(() => new Date());

  if (activity.error) return <ErrorState error={activity.error} operation="GetActivityQuery" onRetry={activity.reload} retrying={activity.fetching} />;

  const summary = activity.data ? todaySummary(activity.data, now, STAT_TAKE) : null;
  const loading = summary === null;
  const total = summary ? `${summary.capped ? 'Más de ' : ''}${formatNumber(summary.total)}` : '';
  return (
    <div className="space-y-4" data-testid="actividad-de-hoy">
      <div className="grid grid-cols-[repeat(auto-fit,minmax(9rem,1fr))] gap-3">
        <StatCard
          label="Operaciones"
          value={total}
          hint={summary?.lastAt ? `La última a las ${formatTime(summary.lastAt)}` : summary ? 'Ninguna todavía' : undefined}
          icon={<Activity />}
          loading={loading}
        />
        <StatCard
          label="Rechazadas"
          value={formatNumber(summary?.rejected ?? 0)}
          tone={summary && summary.rejected > 0 ? 'warning' : 'default'}
          icon={<ShieldAlert />}
          loading={loading}
        />
        <StatCard label="Con error" value={formatNumber(summary?.failed ?? 0)} tone={summary && summary.failed > 0 ? 'danger' : 'default'} icon={<CircleX />} loading={loading} />
      </div>
      {summary && summary.byUser.length > 0 && <BarList label="Quién operó hoy" items={summary.byUser.slice(0, 5)} format={(value) => formatNumber(value)} />}
      {summary && (
        <Button to={ROUTES.panelModule(`actividad?desde=${summary.day}&hasta=${summary.day}`)} variant="outline" leftIcon={<History />}>
          Ver la actividad de hoy
        </Button>
      )}
    </div>
  );
}
```

La tarjeta de la estadística es angosta (media columna): use grillas que se acomoden al ancho del contenedor
(`grid-cols-[repeat(auto-fit,minmax(9rem,1fr))]`), no las de la ventana (`sm:`, `lg:`).

---

## 9. Direcciones y navegación

- Su módulo vive en `/panel/<clave>`; sus pantallas, en `/panel/<clave>/<path>`. Para enlazar a una de SUS pantallas:
  `ROUTES.panelModule('<clave>/nueva')` o un `<Button to=…>`. Para leer un parámetro: `const { numero } = useParams()`.
- Enlazar a la pantalla de OTRO módulo es solo una dirección (`ROUTES.panelModule('ventas?cliente=CLI-0042')`), nunca
  una importación. Si ese módulo no existe todavía o la sesión no tiene permiso, el panel lo dice solo.
- Los filtros viven en la dirección (`useTableState`): un botón del tablero puede abrir la lista ya filtrada.

---

## 10. Estilo, textos y accesibilidad

- Solo componentes del kit (misma apariencia en todo el panel) y los tokens de color (`text-text-muted`,
  `bg-surface-2`…). Nada de colores fijos ni recursos de otros dominios.
- **Español neutro** en el panel («Pida al administrador», «Elija una sucursal»); la tienda usa voseo. Nada de
  términos técnicos a la vista: estados en palabras (`defineStatuses`), errores con `describePanelError` (ya lo hacen
  `ErrorState`, `useRpcCommand` y `useNotify().error`), fechas y montos con `lib` (hora de La Paz, «Bs 1.234,50»).
- Objetivos de 44 px, foco visible, todo con teclado, etiquetas en cada campo y sin desplazamiento horizontal a 360 px
  (la tabla pasa a tarjetas sola por debajo de 640 px; los filtros se pliegan).
- Estados de toda lista: carga (esqueleto), vacío (con qué hacer: «Limpiar filtros») y error con «Reintentar».

---

## 11. Pruebas (copiadas del ejemplo)

Pruebe las funciones puras sin React (`activity.test.ts`) y la pantalla dentro del panel real, con la sesión en memoria
de un ROL (`signedInAs('ADMIN' | 'BODEGA' | 'VENTAS' | 'CAJERO' | 'GERENCIA' | 'CONSULTA')`, con la matriz de permisos
de la empresa de prueba). Ninguna prueba toca la red.

```tsx
import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { RpcResponseOf } from '@/4-presentation/app/contract';
import { preloadPanel, renderPanel, signedInAs, type MockWeb } from '@/test-utils';
import { PANEL_REGISTRY } from '../../registry/discovery';
import { PanelApp } from '../../shell/PanelApp';

beforeAll(async () => {
  await preloadPanel(); //                  el panel es un fragmento grande: se descarga una vez antes
  await import('./ActivityPage');
}, 30_000);

afterEach(() => {
  vi.restoreAllMocks();
});

/** Abre la lista con una sesión ya preparada (para espiar o preparar el servidor antes). */
async function openActivityAgain(web: MockWeb, query = ''): Promise<{ location: () => string }> {
  const view = await renderPanel(<PanelApp registry={PANEL_REGISTRY} />, { web: web.services, route: `/panel/actividad${query}`, path: '/panel/*' });
  return { location: view.location };
}

async function table(): Promise<HTMLElement> {
  const found = await screen.findByTestId('tabla', undefined, { timeout: 5000 });
  await waitFor(() => expect(within(found).queryAllByRole('row').length).toBeGreaterThan(1));
  return found;
}

function dataRows(tableElement: HTMLElement): HTMLElement[] {
  return within(tableElement)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'));
}

describe('Actividad · lista', () => {
  it('muestra lo que responde el servidor (respuesta simulada en la prueba, sin tocar el modo mock)', async () => {
    const web = await signedInAs('GERENCIA');
    const rows: RpcResponseOf<'GetActivityQuery'> = [
      { occurredAt: '2026-09-29T14:00:00Z', userEmail: 'cajero@techzone.example', userName: 'Diego Flores', action: 'VoidSale', outcome: 'Rejected', details: '{"request":{},"result":null,"error":"La venta ya fue anulada."}' },
      { occurredAt: '2026-09-29T13:00:00Z', userEmail: null, userName: null, action: 'ExpirePcBuildReservations', outcome: 'Succeeded', details: null },
    ];
    vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, _payload, options) => {
      if (operation === 'GetActivityQuery') return { result: rows, replayed: false, requestId: options?.requestId ?? 'prueba' };
      throw new Error(`Operación no simulada en esta prueba: ${operation}`);
    });
    await openActivityAgain(web);
    const grid = await table();
    expect(dataRows(grid).map((row) => row.textContent)).toEqual([
      '29/09/2026 10:00Diego Florescajero@techzone.exampleAnuló una ventaRechazadoLa venta ya fue anulada.',
      '29/09/2026 09:00SistemaVenció reservas automáticamenteCorrecto—',
    ]);
  });
});
```

Recetas (ver `modules/actividad/ActivityPage.test.tsx`, `shell/PanelShell.test.tsx` y `modules/inicio/InicioPage.test.tsx`):

- **Su operación no está en el modo mock**: simúlela con `vi.spyOn(web.backend.rpc, 'call').mockImplementation(…)`
  como arriba (o `mockRejectedValueOnce(new WebApiError({ kind: 'network', message: '…' }))` para probar el error).
- **Qué pidió la pantalla**: `const call = vi.spyOn(web.backend.rpc, 'call')` y después
  `call.mock.calls.filter(([op]) => op === 'X').map(([, pedido]) => pedido)`.
- **Permisos**: la misma pantalla con `signedInAs('VENTAS')` debe mostrar «No tiene acceso a esta pantalla»
  (`findByTestId('sin-acceso')`) o no ofrecer el botón del comando.
- **Filtros en la dirección**: `const view = await renderPanel(…)` y `expect(view.location()).toBe('/panel/x?estado=y')`.
- **Tablero**: sus `actions` y `stats` se prueban con `renderRoutes({ web: web.services, route: '/panel' })` (el
  tablero real) o con un registro de muestra (`buildRegistry([...])`, ver `InicioPage.test.tsx`).
- `jsdom` no tiene `matchMedia`: la tabla se ve como tabla (no tarjetas). Para las tarjetas, ver `kit/DataTable.test.tsx`.

---

## 12. Qué NO hacer

| No | Por qué / qué hacer | ¿Lo vigila una prueba? |
|---|---|---|
| Importar de otro módulo | P-09: los módulos son independientes. Lo compartido se pide para `kit/` o `lib/`. | Sí (arquitectura) |
| Importar algo que no sea `panel/kit`, `panel/hooks`, `panel/lib`, `panel/registry`, `app/contract`, `app/routes`, su carpeta o `react`, `react-router-dom`, `lucide-react`, `clsx` | El kit ya reexporta `Button`, `Alert`, `EmptyState`… Si falta algo, se agrega al kit para todos. | Sí (arquitectura) |
| Tocar `shell/`, `kit/`, `registry/`, `hooks/`, `lib/`, `PanelRoot.tsx` o `routeTable.tsx` | P-09. Su módulo se registra solo. | No: revisión |
| Declarar a mano tipos del servidor (`interface ActivityRow`, `type GetSalesQuery`) | P-07: derívelos del contrato (§5.1). | Sí (arquitectura) |
| Importar `contract.generated.ts` o usar `fetch` / `useRpc` directo | Todo pasa por el adaptador y por `useRpcQuery` / `useRpcCommand`. | Sí (arquitectura) |
| Guardar algo en el navegador (`localStorage`, `sessionStorage`, cookies, IndexedDB) | P-02 y P-08. Los filtros van en la dirección (`useTableState`). | Sí (arquitectura) |
| Mostrar estadísticas, indicadores o gráficos a la vista al entrar | P-10: al tablero van como `stats`; en una pantalla, dentro de un `Collapsible` cerrado. | No: revisión |
| Lógica de negocio en la página (calcular stock, precios, permisos «de verdad») | P-01: el servidor decide; la página muestra, filtra y formatea. | No: revisión |
| Importar la pantalla en `module.tsx` sin `lazyScreen` | Todo el panel se descargaría junto. | No: revisión |
| Pasar `breadcrumbs` a `Page` o poner márgenes laterales | Los pone el esqueleto (§4). | No |
| Su propio selector de sucursal activa | Lo hace la barra superior; `useRpcQuery` se repite solo. | No |
| `dangerouslySetInnerHTML`, `eval`, recursos de otros dominios | P-11 (Content-Security-Policy de solo «self»). | Sí (arquitectura) |
| Textos en inglés o técnicos, voseo en el panel, `Button size="sm"` | §10. | No: revisión |
| Usar `orden`, `sentido`, `pagina` o `filas` como nombre de un filtro | Son de la tabla (`TABLE_PARAMS`). | No |

---

## 13. Antes de entregar

```powershell
cd "src\3. Presentation\MINV.WebCatalog"
npx vitest run src/4-presentation/panel/modules/<clave>                      # sus pruebas
npx vitest run src/4-presentation/panel/registry src/architecture.test.ts   # su módulo es válido y respeta las reglas
npm run typecheck
npm run lint
npx vitest run          # en esta consola vitest no imprime en vivo: redirija a un archivo y mire el final
npm run build
```

- [ ] `module.tsx` liviano, con la clave igual a la carpeta, la sección y los permisos correctos.
- [ ] Tipos derivados del contrato (nada declarado a mano) y operaciones por su nombre.
- [ ] Lista con filtros (listas desplegables y búsqueda) en la dirección, tabla, detalle, exportar CSV y los tres
      estados (carga, vacío, error con «Reintentar»).
- [ ] Botones y comandos solo para quien puede (`canRun`); el tablero recibe `actions` y, si aplica, `stats` plegadas.
- [ ] Probado con al menos dos roles (uno con acceso y uno sin), a 360 px y con teclado.

---

## 14. Usuarios de muestra del modo mock

Con `VITE_API_URL=mock` la sesión y el RPC viven en la memoria de la pestaña (se pierden al recargar). La pantalla de
ingreso ofrece el administrador y el cliente; los demás roles existen igual (contraseña pública de la demostración,
`Demo1234`). Sirven para ver el menú y el tablero de cada rol.

| Rol | Nombre | Correo | Sucursales |
|---|---|---|---|
| ADMIN | Andrea Quiroga | `admin@techzone.example` | todas (activa: CM) |
| BODEGA | Bruno Mamani | `bodega@techzone.example` | CM |
| VENTAS | Carla Rojas | `ventas@techzone.example` | CM y CB |
| CAJERO | Diego Flores | `cajero@techzone.example` | CB |
| GERENCIA | Elena Vargas | `gerencia@techzone.example` | todas (vista consolidada) |
| CONSULTA | Fernando Choque | `consulta@techzone.example` | SC |
| CLIENTE | Valentina Aguirre | `cliente@techzone.example` | (cuenta de cliente: no entra al panel) |

La matriz de permisos de cada rol está en `ROLE_PERMISSIONS` (`src/3-infrastructure/data/mockWeb.ts`). El modo mock
atiende hoy: la cuenta del cliente, `SelectBranchCommand`, `ChangePasswordCommand`, `GetActivityQuery` y
`ResetUserPasswordCommand`. Contra el servidor real, los usuarios de prueba y sus contraseñas están en
`usuarios-prueba.txt` del equipo (regla A-13), nunca en el repositorio.

---

## 15. Preguntas frecuentes

- **Mi módulo no aparece en el menú.** ¿La sesión tiene sus permisos? (pruebe con `admin@…`). ¿`module.tsx` exporta
  por defecto? ¿La clave es igual a la carpeta? Corra `npx vitest run src/4-presentation/panel/registry`: el mensaje
  dice qué corregir; en `npm run dev` el panel también lo avisa arriba.
- **«No tiene acceso a esta pantalla».** La sesión no cumple `permissions` del módulo o de esa ruta. El aviso nombra el
  permiso que falta.
- **`typecheck` dice que la operación no existe.** No está en el servidor: §5.4.
- **¿Filtro en el servidor o en la página?** Si la operación tiene el parámetro (fechas, sucursal, estado…), en el
  pedido de `useRpcQuery`; si no, en un `useMemo` sobre las filas. Una lista enorme se pide acotada (como `take`).
- **¿Dónde pongo los estados de mis documentos?** `defineStatuses({ … })` en un archivo del módulo; úselos en la tabla
  (`StatusBadge`), en el detalle y en el filtro (`statusOptions`).
- **¿Cómo muestro un error de un comando dentro de un formulario?** `useRpcCommand(op, { notifyError: false })` y
  `<Form error={comando.errorText}>`.
- **¿Puedo mostrar un gráfico en mi pantalla?** Sí, dentro de un `Collapsible` cerrado («Ver …»), nunca a la vista al
  entrar.
