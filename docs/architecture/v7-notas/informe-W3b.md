# Informe del paquete W3b · registro de módulos, esqueleto por rol, «Inicio» y el módulo de ejemplo «Actividad» (V7)

## Resumen

W3b (tareas 10 y 12) terminado y todo en verde. Al empezar, `git log` mostró el commit de W3a (7c38afe) como último y
`git status` de la web estaba limpio: no había avance previo de W3b (no existían `registry/`, `shell/` ni `modules/`).
No hice commit ni ejecuté comandos de git que cambien algo ni `dotnet`; solo toqué la carpeta de la web (más este
informe). Sin dependencias nuevas de npm. El trabajo se cortó una vez por el límite de uso de la API y se retomó donde
estaba (limpieza de temporales, verificación final e informe).

Qué hay en `src/4-presentation/panel/`:

1. **`registry/`** · el registro de módulos. Cada módulo es `modules/<clave>/module.tsx` con `export default` de
   `defineModule({ key, section, title, description, icon, order, permissions: { any?, all? }, licenseModules?, routes,
   actions?, stats? })`. `discovery.ts` los descubre con `import.meta.glob('../modules/*/module.tsx', { eager: true,
   import: 'default' })`, los VALIDA (un módulo con errores no se carga y el motivo queda en palabras) y los ordena por
   sección (General, Ventas, Tecnología, Inventario, Compras, Sucursales, Facturación, Análisis, Administración),
   `order` y título. Funciones puras para el menú, el tablero (botones y estadísticas), el buscador, las migas y el
   acceso a una pantalla (permisos `any`/`all` del módulo y de la ruta; el permiso que falta dicho en palabras con los
   nombres del contrato).
2. **`shell/`** · el esqueleto (reemplaza el `PanelRoot.tsx` provisional, mismo archivo y exportación, carga diferida):
   menú lateral por secciones plegables con buscador de pantallas (cajón en el teléfono), barra superior con migas de
   pan, lista de SUCURSAL ACTIVA (si hay más de una opción; cambia con `SelectBranchCommand`, vuelve a leer la sesión y
   cada `useRpcQuery` repite su consulta) y el usuario (nombre y rol, con «Cambiar contraseña», «Ir a la tienda» y
   «Cerrar sesión»). Una ruta sin permiso muestra «No tiene acceso a esta pantalla» con el permiso que falta; una
   dirección sin pantalla, «No encontramos esta pantalla». Límite de errores por pantalla, foco al contenido al navegar.
3. **`modules/inicio`** (General › Inicio, en `/panel`): saludo con el nombre, el rol y la sucursal activa; «¿Qué quiere
   hacer?» con los `ActionButton` que ofrecen los módulos visibles, agrupados por sección; debajo, «Ver estadísticas»
   PLEGADO: al entrar no se ve ni se carga nada; al abrir, cada estadística se descarga y consulta por separado con su
   propio estado y su límite de errores. Sin otros módulos se ve bien igual (estado vacío).
4. **`modules/actividad`** (Administración › Actividad, `iam.audit.view`), el EJEMPLO completo y real:
   `GetActivityQuery { take }` → lista con filtros en la dirección (búsqueda, usuario y resultado con listas
   desplegables, rango de fechas y «Registros a revisar»), tabla ordenable y paginada, acciones por fila, detalle
   lateral con los datos registrados, exportar CSV, un comando (`ResetUserPasswordCommand`: contraseña temporal y
   desbloqueo, solo para quien administra usuarios) y la estadística plegada «Actividad de hoy» para el tablero. Los
   textos de las acciones y el resumen del detalle son los del escritorio (`Formats.Action`, `ActivityItem.Summarize`).
5. **`panel/README.md`** · la guía para los diez ingenieros de los módulos (15 secciones): estructura, `module.tsx`
   mínimo y completo, campos, claves/secciones/orden sugeridos (diseño §7), qué pone el esqueleto, cómo usar una
   operación y sus tipos A TRAVÉS DEL ADAPTADOR para que funcione igual con el provisional y con el generado, qué hacer si
   la operación todavía no está, consultas, comandos, permisos, la pantalla de lista y el comando COPIADOS del ejemplo,
   el tablero, direcciones, estilo, pruebas (copiadas del ejemplo), qué NO hacer, lista de entrega, usuarios de muestra y
   preguntas frecuentes. Comprobé que los bloques copiados son idénticos a los archivos y que los dos ejemplos que no son
   archivos del repositorio compilan, pasan el linter y la prueba corre en verde (temporales ya borrados).

Fuera del panel (dentro de la web):

- **El catálogo ya no bloquea el sitio** (tarea 3 del encargo). `CatalogProvider` se separó en `CatalogStateProvider`
  (carga y refresca sin bloquear; publica el estado) y `CatalogGate` (espera el catálogo). `App.tsx` usa el primero y
  la tabla de rutas pone `CatalogGate` SOLO alrededor de las páginas de la tienda. `/ingresar`, `/registrarse`,
  `/cambiar-contrasena`, `/mi-cuenta` y `/panel` funcionan con la tienda caída; mientras el catálogo no está, la
  estructura de la tienda muestra una cabecera liviana (logotipo e «Ingresar») y el `<main>` queda en el mismo lugar del
  árbol (un formulario de ingreso a medio escribir no se reinicia cuando llega el catálogo). «Mi armado» y el carrito
  siguen arriba del enrutador (no se pierden al navegar) y toleran la falta de catálogo.
- **Contrato provisional**: agregué `GetActivityQuery`, `ActivityRow`, `AuditOutcome` y `ResetUserPasswordCommand`
  (copiados del C#) con sus entradas en `RpcOperations` y `RPC_META`. `contract.ts` no cambió.
- **Modo mock** (`VITE_API_URL=mock`): un usuario del personal por rol con la matriz del encargo (`ROLE_PERMISSIONS`,
  `ROLE_SAMPLE_USERS`), sucursales por rol, `SelectBranchCommand` con las reglas del servidor, `GetActivityQuery`
  (actividad de muestra más lo que pasa en la pestaña, sin contraseñas) y `ResetUserPasswordCommand`.
- **Prueba de arquitectura** con dos reglas NUEVAS (más estricta, no más laxa): qué puede importar un módulo y que no
  declare a mano tipos del servidor. Comprobado que detectan violaciones con un archivo temporal (ya borrado).
- `package.json` y `package-lock.json`: versión **7.0.0-alpha.1** (solo el número).
- README de la web: sección «V7 · Esqueleto del panel y módulos (paquete W3b)».

Verificado en un navegador real (servidores de desarrollo en 127.0.0.1, ya detenidos): ver «Pruebas».

## Contratos (nombres exactos)

### Registro · `@/4-presentation/panel/registry` (lo que importa un módulo)

- `defineModule(definition: PanelModuleDefinition): PanelModuleDefinition` (solo tipa).
- `lazyScreen(load, name)`: `lazyScreen<M extends Record<K, ComponentType>, K extends string>(load: () => Promise<M>,
  name: K): LazyExoticComponent<ComponentType>` · carga diferida de una exportación CON NOMBRE
  (`lazyScreen(() => import('./ActivityPage'), 'ActivityPage')`).
- Tipos:
  - `PanelModuleDefinition { key: string; section: PanelSectionKey; title: string; description: string; icon: LucideIcon; order: number; permissions: PermissionRule; licenseModules?: readonly string[]; routes: readonly ModuleRoute[]; actions?: readonly ModuleAction[]; stats?: readonly ModuleStat[] }`.
  - `ModuleRoute { path: string; element: PanelComponent; title: string; permissions?: PermissionRule }` · `path` relativa
    al módulo, sin «/» inicial: `''` principal (obligatoria), `'nueva'`, `':numero'`.
  - `ModuleAction { key; label; description; icon: LucideIcon; to: string; permissions? }` · `to` relativa al módulo
    (`''`, `'nueva'`, `'?resultado=Rejected'`).
  - `ModuleStat { key; title; permissions?; component: PanelComponent }`.
  - `PanelComponent = ComponentType | LazyExoticComponent<ComponentType>`.
- Secciones: `PANEL_SECTIONS` (en orden: `general` General, `ventas` Ventas, `tecnologia` Tecnología, `inventario`
  Inventario, `compras` Compras, `sucursales` Sucursales, `facturacion` Facturación, `analisis` Análisis,
  `administracion` Administración; cada una con `icon`), `PanelSection`, `PanelSectionKey`, `isSectionKey`,
  `sectionOf(key)`, `sectionOrder(key)`.
- Acceso: `PermissionRule { any?: readonly string[]; all?: readonly string[] }` · `allows(regla, permisos)` (sin regla o
  `{}` = cualquier sesión; `all` todos; `any` al menos uno; los dos a la vez: ambas cosas) · `missingFor(reglas, permisos):
  MissingPermissions { all: string[]; any: string[] }` (una sola alternativa cuenta como obligatoria) · `hasMissing` ·
  `missingText(faltantes)`: «Falta el permiso «…». Pida al administrador que se lo asigne.», «Faltan los permisos «…» y
  «…». … se los asigne.» y «Necesita al menos uno de estos permisos: «…» o «…». …».
- Registro: `PANEL_BASE = '/panel'`, `HOME_MODULE_KEY = 'inicio'` (su pantalla es `/panel`), `RegistryEntry { source;
  definition }`, `PanelModule` (= definición + `basePath`), `PanelRegistry { modules; problems; home }`,
  `LicensedModules = readonly string[] | null` (null = sin datos de licencia: no filtra), `buildRegistry(entradas)`,
  `basePathOf(key)`, `canSeeModule(módulo, permisos, licencias?)`, `modulePath(módulo, to?)`, `visibleModules`,
  `menuSections → MenuSection { section; modules }[]` (sin secciones vacías), `dashboardActions →
  DashboardActionGroup { section; actions: DashboardAction { module; action; to }[] }[]`, `dashboardStats →
  DashboardStat { module; stat }[]`, `panelScreens → PanelScreen { id; title; context; to; module }[]` (módulos y
  rutas con título propio que no piden un dato), `searchScreens(pantallas, texto)` (sin acentos, todas las palabras),
  `locate(registro, pathname) → PanelLocation { module; route; params } | null`, `screenAccess(módulo, ruta, permisos,
  licencias?) → { allowed; missing; unlicensed }`.
- Visibilidad: acciones, estadísticas y rutas exigen los permisos del MÓDULO y los suyos.
- Contexto: `PanelRegistryContext`, `usePanelRegistry()` (lanza fuera de `PanelApp`).
- Validación (`buildRegistry`): clave en minúsculas con guiones e IGUAL a la carpeta; sección existente; `title`,
  `description`, `icon` y `order` válidos; `permissions` presente; códigos de permiso existentes en `PERMISSION_LIST`;
  `routes` con la principal `''`, rutas relativas sin repetir, `element` componente y `title`; el tablero (`inicio`) con
  una sola ruta; acciones y estadísticas con clave única, textos, ícono o componente y `to` relativo; clave repetida →
  se carga la primera. Mensajes: «Módulo «<carpeta>» (modules/<carpeta>/module.tsx): …».
- **NO exportado en el índice**: `registry/discovery.ts` → `PANEL_REGISTRY` (lo importa solo `PanelRoot.tsx` y las
  pruebas; así los módulos pueden importar el índice sin importaciones circulares).

### Esqueleto · `panel/shell/` (no lo importa un módulo)

- `PanelApp({ registry })`: proveedor del registro + `<Routes>` relativas a `/panel`: índice = `inicio`; cada módulo en
  `/<clave>` con sus rutas; `*` = `PanelNotFound`. Se registran TODAS las pantallas (sin permiso → `NoAccess`).
- `PanelRoot()` = `<PanelApp registry={PANEL_REGISTRY} />` (el archivo y la exportación de siempre).
- `PanelLayout`, `SideNav({ collapsed, onToggleSection })`, `PanelTopBar({ menuOpen, onOpenMenu })`, `PanelBreadcrumbs`,
  `crumbsFor(registro, pathname): PanelCrumb[]` (`PanelCrumb { label; to? }`: Inicio › Sección (sin enlace) › Módulo ›
  Pantalla), `BranchSwitcher`, `COOKIE_SESSION_ID = '00000000-0000-0000-0000-000000000000'` (el `sessionId` que viaja
  en `SelectBranchCommand`: por la ruta web el servidor lo reemplaza por el de la cookie), `PanelUserMenu`,
  `ModuleScreen({ module, route })`, `NoAccess`, `PanelNotFound`, `ScreenBoundary`, `PanelBrand`.
- Nombres accesibles: navegación «Menú del panel»; buscador «Buscar una pantalla» (Enter abre el primer resultado, ↓
  pasa a la lista, Escape borra) con la lista «Pantallas encontradas»; navegación «Migas de pan»; botón «Abrir el menú
  del panel» → cajón «Menú del panel» (cerrar: «Cerrar el menú»); lista «Sucursal activa» (opción «Todas las
  sucursales» solo con `access.allBranches`; si no, marcador «Elija una sucursal»); botón del usuario «Cuenta de
  <nombre> (<roles>)» con los ítems «Cambiar contraseña» (`/cambiar-contrasena?volver=<aquí>`), «Ir a la tienda» (`/`) y
  «Cerrar sesión».
- Avisos: «Sucursal activa cambiada» (con la sucursal) y, si falla, «No se pudo cambiar la sucursal».
- Textos: «No tiene acceso a esta pantalla», «No encontramos esta pantalla», «No se pudo mostrar esta pantalla» y, solo
  en desarrollo, «Hay módulos del panel que no se cargaron (aviso de desarrollo)» con la lista de problemas. Al pie del
  menú, en desarrollo, el enlace «Muestra de componentes (desarrollo)».
- `data-testid`: `panel-esqueleto`, `sin-acceso`, `pantalla-no-encontrada`, `pantalla-con-error`, `sucursal-unica`,
  `usuario-nombre`, `usuario-rol`.

### Módulos

- `inicio`: `section: 'general'`, `order: 0`, `permissions: {}`, ruta `''` → `InicioPage` (título «Hola, <nombre>»,
  `data-testid="saludo"` con «Rol: …» y «Sucursal activa: …» —o «Todas las sucursales (vista consolidada)»—, h2 «¿Qué
  quiere hacer?», grupos `data-testid="grupo-de-acciones"` con un h3 por sección, estado vacío «Todavía no hay funciones
  para su rol», plegable «Ver estadísticas» / «Ocultar estadísticas» con tarjetas `data-testid="estadistica"`).
- `actividad`: `section: 'administracion'`, `order: 40`, `permissions: { all: ['iam.audit.view'] }`, ruta `''` →
  `ActivityPage`; `actions`: `ver` («Ver la actividad», `to: ''`) y `rechazos` («Revisar rechazos», `to:
  '?resultado=Rejected'`); `stats`: `hoy` («Actividad de hoy» → `ActivityTodayStat`, `GetActivityQuery { take: 1000 }`,
  `data-testid="actividad-de-hoy"`, enlace «Ver la actividad de hoy» → `/panel/actividad?desde=<hoy>&hasta=<hoy>`).
- `ActivityPage`: filtros en la dirección `q`, `usuario` (correo o `_sistema`), `resultado` (`Succeeded`, `Rejected`,
  `Failed`), `desde`, `hasta`, `registros` (`200`, `500` por defecto, `1000`, `5000` → `take`); orden por defecto
  `fecha` descendente; tabla «Actividad del sistema» (Fecha y hora, Usuario, Acción —columna principal—, Resultado,
  Detalle); acciones por fila «Ver detalle», «Ver solo este usuario» y «Asignar contraseña temporal» (solo con
  `canRun('ResetUserPasswordCommand')`); resumen `data-testid="actividad-resumen"`; CSV «actividad-AAAA-MM-DD.csv» con
  Fecha y hora; Usuario; Correo; Acción; Código de la acción; Resultado; Detalle.
- `ResetPasswordDialog({ target: ResetTarget | null, onClose })`, `ResetTarget { email; name }`: diálogo «Asignar una
  contraseña temporal», campo «Contraseña temporal», «Generar una contraseña», casilla «Pedir que la cambie al
  ingresar» (marcada), aviso de éxito «Contraseña temporal asignada a <correo>», error dentro del diálogo.
- `modules/actividad/activity.ts`: `ActivityRecord` (= `RpcResponseOf<'GetActivityQuery'>[number]`), `TAKE_OPTIONS`,
  `DEFAULT_TAKE`, `takeOf`, `OUTCOMES` (Correcto, Rechazado, Con error), `ACTIVITY_FILTERS`, `ActivityFilters`,
  `SYSTEM_USER = '_sistema'`, `actionText`, `DetailField`, `detailFields`, `summarizeDetails`, `ActivityItem`,
  `toActivityItems`, `userOptions`, `filterActivity`, `outcomeLabel`, `CSV_COLUMNS`, `TodaySummary`,
  `todaySummary(filas, ahora, pedidas)`, `temporaryPasswordProblem`, `generateTemporaryPassword`.

### Contrato provisional (`3-infrastructure/http/contract.generated.ts`)

- `AuditOutcome = 'Succeeded' | 'Rejected' | 'Failed'`.
- `GetActivityQuery { take?: number }` (C#: `GetActivityQuery(int Take = 200)`, el servidor acota entre 1 y 5000).
- `ActivityRow { occurredAt: string; userEmail: string | null; userName: string | null; action: string; outcome: AuditOutcome; details: string | null }`.
- `ResetUserPasswordCommand { email: string; newPassword: string; mustChange?: boolean }`.
- `RpcOperations`: `GetActivityQuery: { request: GetActivityQuery; response: ActivityRow[] }`,
  `ResetUserPasswordCommand: { request: ResetUserPasswordCommand; response: boolean }`.
- `RPC_META`: `GetActivityQuery` → `type: 'MINV.Application.Iam.GetActivityQuery'`, `command: false`, `permissions:
  ['iam.audit.view']`; `ResetUserPasswordCommand` → `type: 'MINV.Application.Iam.ResetUserPasswordCommand'`, `command:
  true`, `permissions: ['iam.users.manage']` (ambas `modules: []`, `customer: false`).
- Cómo lo usa un módulo (README §5): por el NOMBRE de la operación y con tipos DERIVADOS del adaptador
  (`RpcResponseOf<'GetActivityQuery'>[number]`, `RpcRequestOf<'…'>`), enviando SIEMPRE todos los parámetros; así
  funciona igual con el provisional y con el generado.

### Catálogo de la tienda (independencia)

- `state/CatalogStatusContext.ts`: `CatalogStatusContext`, `CatalogStatus { status: 'loading' | 'error' | 'ready';
  error: StorefrontError | null; retrying: boolean; retry(): void }`.
- `state/CatalogProvider.tsx`: `CatalogStateProvider(props)` (no bloquea: publica el estado y, con instantánea,
  `ServicesContext`), `CatalogGate({ children?, variant?: 'screen' | 'body' })` (sin hijos dibuja `<Outlet />`;
  `body` por defecto), `CatalogProvider(props)` (= los dos, `variant="screen"`, como la V6), `CATALOG_REFRESH_MS`,
  `CATALOG_STALE_MS`.
- `components/feedback/CatalogScreens.tsx`: además de `CatalogLoadingScreen` y `CatalogErrorScreen` (sin cambios
  visibles), `CatalogLoadingBody` y `CatalogErrorBody` (sin cabecera propia; mismos `data-testid` `catalogo-cargando` y
  `catalogo-error`).
- Hooks: `useOptionalServices(): Services | null` (`hooks/useServices.ts`), `useOptionalStore(): StoreInfo | undefined`
  (`hooks/useStore.ts`).
- `AppShell`: sin catálogo, cabecera liviana `data-testid="cabecera-liviana"` (logotipo y `UserMenu`), sin barra
  superior, pie ni cajón del armado; `ScrollToHashOnLoad` se monta con el catálogo listo.
- `routeTable.tsx`: dentro de `AppShell`, `ingresar`, `registrarse`, `cambiar-contrasena` y `mi-cuenta/:seccion?` sin
  espera; el resto de la tienda (índice, catálogo, producto, armador, reserva, carrito, reservar y `*`) dentro de `{
  element: <CatalogGate /> }`. El panel sigue aparte (`/panel/*`, sin catálogo).
- `BuilderProvider` y `CartProvider` usan `useOptionalServices()`: sin catálogo no sincronizan ni cargan armados
  sugeridos y `adjust` no toca el carrito. `ReservationsSection` usa `useOptionalStore()` (sin catálogo, la sucursal se
  muestra con su código).

### Modo mock (`3-infrastructure/data/mockWeb.ts`) y utilidades de prueba

- `DemoUser` suma `role?: StaffRole` y `branches?: readonly string[]`; `StaffRole = 'ADMIN' | 'BODEGA' | 'VENTAS' |
  'CAJERO' | 'GERENCIA' | 'CONSULTA'`; `ROLE_PERMISSIONS` (la matriz del encargo; ADMIN = todos los permisos del personal);
  `ROLE_SAMPLE_USERS` (contraseña `Demo1234`): Bruno Mamani `bodega@` (CM), Carla Rojas `ventas@` (CM y CB), Diego Flores
  `cajero@` (CB), Elena Vargas `gerencia@` (todas, sin activa), Fernando Choque `consulta@` (SC), todos
  `@techzone.example`. `DEMO_USERS` no cambió (la pantalla de ingreso sigue ofreciendo solo administrador y cliente); en
  modo mock, `createWebServices` crea el servidor en memoria con `DEMO_USERS` + `ROLE_SAMPLE_USERS`.
- Sucursales: `allBranches` solo con `corporate.branches.all`; activa: ADMIN CM, GERENCIA null, el resto la primera
  asignada. `SelectBranchCommand`: fuera de sus sucursales → 403 «La sucursal elegida no está entre las suyas.»; `null`
  sin ser gerencia global → 403 «Elija una de sus sucursales (la vista de todas es solo para la gerencia).».
- `GetActivityQuery`: `take` 200 si falta, acotado 1..5000; lo más reciente primero (a la misma hora, lo registrado
  último); muestra con semilla fija (hoy: 10 operaciones cada 17 min hacia atrás; 12 días anteriores con 8 a 15 por día)
  de los usuarios que existen; más la actividad de la pestaña: ingresos (correctos y rechazados, con el correo intentado)
  y comandos ejecutados (Succeeded / Rejected / Failed), detalle `{ request, result, error }` SIN contraseñas.
- `ResetUserPasswordCommand`: 400 si la contraseña no cumple, 404 si el usuario no existe; asigna, pide cambiarla
  (`mustChange ?? true`) y desbloquea.
- `src/test-utils.tsx`: `failingSources()`, `preloadPanel()`, `roleUser(rol)`, `signedInAs(rol, opciones?)`, reexporta
  `ROLE_PERMISSIONS`, `ROLE_SAMPLE_USERS` y `StaffRole`; `renderRoutes({ …, waitForCatalog = true })` ahora arma la
  aplicación como `App.tsx` (sin bloquear el enrutador), espera la marca `catalogo-listo` (con el catálogo listo) y que
  el enrutador termine su carga inicial; `renderWithApp` sigue igual (con espera).

### Prueba de arquitectura (reglas nuevas)

- «un módulo del panel importa SOLO el conjunto del panel, el contrato y su propia carpeta (regla P-09, W3b)»: permitido
  `@/4-presentation/panel/{kit,hooks,lib,registry}`, `@/4-presentation/app/contract`, `@/4-presentation/app/routes`,
  `react`, `react-dom`, `react-router`, `react-router-dom`, `lucide-react`, `clsx` y rutas relativas que NO salgan de la
  carpeta del módulo.
- «un módulo del panel no declara a mano tipos del servidor (regla P-07, W3b)»: ningún `interface`/`type` con el nombre
  de un tipo de `contract.generated.ts` ni terminado en `Query` o `Command`.

## Desviaciones del diseño

- **Español neutro en dos textos del encargo.** El encargo cita «No tenés acceso a esta pantalla» y «¿Qué querés
  hacer?» con voseo, pero la regla del mismo encargo dice «en el panel del personal, español neutro». Quedaron «No tiene
  acceso a esta pantalla» y «¿Qué quiere hacer?» (coherentes con el kit: «Pida al administrador…»). Si se prefiere el
  texto literal, son dos cadenas en `shell/PanelStates.tsx` y `modules/inicio/InicioPage.tsx` (y sus pruebas).
- **Descubrimiento inmediato.** `import.meta.glob('../modules/*/module.tsx', { eager: true, import: 'default' })`: el
  menú necesita todas las definiciones al abrir el panel y `module.tsx` es liviano; las pantallas y estadísticas son
  diferidas (fragmentos propios). `discovery.ts` va aparte del índice del registro para evitar importaciones circulares.
- **Formas concretas de la definición.** `routes[].element` y `stats[].component` son COMPONENTES diferidos (no elementos
  JSX), `icon` es el componente de lucide (no `<Icon />`), y se agregó `lazyScreen(load, nombre)` para las exportaciones
  con nombre del proyecto. `actions[].to` es relativa al módulo (no una dirección absoluta).
- **El tablero es `/panel`, no `/panel/inicio`.** El módulo `inicio` se monta en el índice: `homeFor('staff')`, los
  enlaces «Ir al panel» y las pruebas existentes siguen apuntando a `/panel`.
- **Permisos del módulo más los propios** para rutas, botones y estadísticas (un botón nunca lleva a un módulo que la
  sesión no ve).
- **`licenseModules` es informativo por ahora.** La sesión web no trae los módulos comerciales activos; el registro ya
  los acepta (`LicensedModules`) y el esqueleto pasa `null` (no filtra). El servidor rechaza igual y el aviso lo explica.
- **Migas en la barra superior, automáticas.** Se arman con el registro (títulos de las rutas); los módulos NO pasan
  `breadcrumbs` a `Page`. En el teléfono solo «‹ anterior»: el camino completo se OCULTA (no «solo para lectores»),
  porque sus enlaces recibían el foco sin verse. La sección se omite por debajo de 1280 px para que entren en una línea.
- **El ejemplo incluye un comando.** El encargo no lo pedía para «Actividad», pero el README debía copiar «un comando»
  del módulo de ejemplo: se usó `ResetUserPasswordCommand` (contraseña temporal y desbloqueo desde el detalle de un
  ingreso rechazado), solo visible con `iam.users.manage`. Por eso el contrato provisional recibió dos operaciones.
- **«Registros a revisar».** `GetActivityQuery` no filtra en el servidor: la lista pide los últimos N (200, 500, 1000 o
  5000; por defecto 500, un filtro más de la dirección) y filtra en la página. El aviso de la lista lo dice.
- **Resultado «Con error».** El escritorio muestra «Falló» en la fila y «Con error» en el filtro; en la web el estado
  `Failed` se llama «Con error» en los dos lugares.
- **Pantallas de carga y error de la tienda.** Ahora se ven DENTRO de la estructura de la tienda con la cabecera liviana
  (así se puede ir a «Ingresar» con la tienda caída). Las versiones a pantalla completa siguen para `CatalogProvider`.
- **Pruebas existentes tocadas (sin cambiar lo que comprueban).** `app/routeTable.test.tsx`: el marcador del panel
  provisional (`panel-en-construccion`) se reemplazó por el del esqueleto (`panel-esqueleto`) y, en `/panel/ventas/caja`
  (todavía sin módulo), por `pantalla-no-encontrada` dentro del esqueleto; «Cerrar sesión» se pulsa desde el menú del
  usuario; `beforeAll(preloadPanel)`. `renderRoutes` compone ahora como `App.tsx` y espera la carga inicial del
  enrutador (con la suite en paralelo, la primera descarga de una ruta diferida superaba el segundo de espera).
- **`test-utils.tsx` desactiva `react/only-export-components`** (comentario `oxlint-disable` con la explicación): es un
  archivo solo de pruebas y ahora tiene un componente propio (`CatalogReadyMark`).
- **El mock valida la sucursal como el servidor** (antes aceptaba cualquiera y `null` para todos; el mensaje de rechazo
  pasó a ser el del servidor).
- **Usuario en la barra superior:** nombre y rol a la vista desde 1024 px; por debajo, las iniciales (el nombre
  accesible del botón lleva nombre y rol).
- **Dos límites de errores parecidos** (`shell/ScreenBoundary.tsx` y `modules/inicio/StatBoundary.tsx`): un módulo no
  puede importar `shell/`; se podría mover uno común al kit.

## Pendientes

- **Contrato generado.** Al reemplazar `contract.generated.ts`, correr `npm run typecheck`. Revisar en especial las dos
  operaciones de W3b: si el generado tipa `outcome` como `string` o `take` como obligatorio, el módulo sigue compilando
  (usa tipos derivados y siempre envía `take`); si nombra distinto `ActivityRow` o `AuditOutcome`, no importa (nadie los
  importa por nombre fuera del provisional y del mock: el mock sí los importa de `../http/contract`, habría que
  ajustarlo ahí).
- **Diez ingenieros editando el provisional a la vez**: el README pide un bloque por módulo para reducir conflictos; lo
  ideal es que el generado llegue antes.
- **Servidor:**
  - Que la sesión web informe los módulos comerciales activos (para `licenseModules`).
  - Un código estable para la falta de permiso (heredado de W3a).
  - Confirmar contra el servidor real que `SelectBranchCommand` con `sessionId` = UUID nulo se lee bien antes de
    `BindSession` (es un `Guid` no nulo: un texto vacío no se podría leer).
  - `GetActivityQuery` sin filtros en el servidor (usuario, resultado, fechas, páginas): con auditorías grandes conviene
    agregarlos; los filtros del módulo pasarían al pedido.
- **Textos de las acciones duplicados** del escritorio (`ACTIONS` en `activity.ts` = `Formats.Action` + V6/V7): mantener
  la paridad o que el servidor mande el texto.
- **Kit:** un límite de errores común; `Page` conserva `breadcrumbs` (ya no lo usan los módulos); `ServicesProvider.tsx`
  quedó sin uso (el proveedor usa el contexto directo).
- **Cajón del teléfono:** al elegir una pantalla, el foco vuelve al botón de menú (lo hace la capa modal compartida), no
  al contenido.
- **Verificación pendiente:** todo se probó con puertos simulados, en modo mock y con una API inexistente; falta el
  recorrido contra el servidor real en Docker (P-14).
- **Documentación fuera de la web sin actualizar** (fuera de mi carpeta): `docs/product/plan-v7.md` (tareas 10 y 12),
  `CHANGELOG.md`, `CLAUDE.md` y el diseño §7 (menciona `2-application/panel/`; el registro y el esqueleto viven en
  `4-presentation/panel/registry` y `shell`).
- **Sin commit** (prohibido): todos los cambios quedan sin confirmar en la carpeta de la web de la rama Inventario-V7.

## Pruebas

Ejecutado desde `src/3. Presentation/MINV.WebCatalog` con PowerShell; la salida de vitest se redirigió a un archivo y se
leyó el final.

- **Línea base antes de empezar:** `npx vitest run` → exit 0, «Test Files 54 passed (54)», «Tests 662 passed (662)»,
  Duration 21.38s.
- **Por carpeta durante el trabajo:** registro y esqueleto 35/35; `inicio` 10/10; `actividad` 24/24 (10 + 14);
  `app/catalogIndependence.test.tsx` 6/6; `3-infrastructure/data` 32/32 (4 archivos); `architecture.test.ts` 16/16.
  Prueba NEGATIVA de las reglas nuevas de arquitectura con un módulo temporal que importaba `../../shell/PanelLayout` y
  `@/4-presentation/components/ui/Alert` y declaraba `interface ActivityRow` y `type GetSalesQuery`: las dos reglas
  fallaron con exactamente esas 4 líneas; el temporal se borró. Los dos ejemplos del README que no son archivos del
  repositorio se extrajeron a temporales: `npx tsc -b --noEmit` exit 0, `oxlint` exit 0 y la prueba 1/1; se borraron.
- **Final:**
  - `npm run typecheck` (`tsc -b --noEmit`): exit 0, sin errores.
  - `npm run lint` (`oxlint`): exit 0; con `--format=default`: «Found 0 warnings and 0 errors. Finished in 86ms on 374
    files with 116 rules using 12 threads.».
  - `npx vitest run`: exit 0, «Test Files 61 passed (61)», «Tests 747 passed (747)», Duration 26.60s, sin advertencias
    de React ni salida de error. Son 85 pruebas nuevas: `registry/registry.test.ts` 14, `shell/PanelShell.test.tsx` 21
    (el menú de los 6 roles con la matriz y los módulos del diseño §7, marcado del activo, secciones plegables,
    buscador, sin acceso con `all` y con `any`, no encontrada, migas, foco, cajón del teléfono, aviso de desarrollo,
    usuario, cambio de sucursal con `SelectBranchCommand` + relectura de sesión y datos, sucursal única, gerencia
    «Todas», rechazo), `modules/inicio/InicioPage.test.tsx` 10 (saludo, botones por sección y por rol, vacío, NADA
    visible ni consultado al entrar, cada estadística por separado y una rota no tapa a las demás, con los módulos
    reales), `modules/actividad/activity.test.ts` 10, `modules/actividad/ActivityPage.test.tsx` 14 (lista, filtros en la
    dirección, fechas, búsqueda, `take` al servidor, respuesta simulada, error y «Reintentar», detalle, CSV, comando
    completo con desbloqueo y auditoría sin contraseña, gerencia sin el comando, ventas sin acceso, estadística y su
    error), `app/catalogIndependence.test.tsx` 6 (`/ingresar` y `/panel` con la tienda caída —y el ingreso hasta el
    panel—, la tienda con el error y «Ingresar», «Mi cuenta» sin catálogo, el formulario no se reinicia al llegar el
    catálogo, «Mi armado» y el carrito al navegar), `3-infrastructure/data/mockWebPanel.test.ts` 8, y 2 reglas nuevas en
    `architecture.test.ts` (16 en total). Las 662 anteriores pasan; en `routeTable.test.tsx` (26/26) solo cambió cómo
    se reconoce el panel real (ver «Desviaciones»).
  - `npm run build` (`tsc -b && vite build`): exit 0, «✓ 2179 modules transformed», «✓ built in 741ms». Fragmentos del
    panel: `PanelRoot` 29.03 kB, `kit` 54.00 kB, `InicioPage` 4.10 kB, `ActivityPage` 7.30 kB, `ActivityTodayStat` 1.56
    kB, `activity` 11.84 kB (compartido por la pantalla y la estadística); `index` 241.11 kB. Búsqueda de
    «_componentes» y «Componentes del panel» en `dist`: 0. «No tiene acceso a esta pantalla» solo en `PanelRoot-*.js`.
- **Navegador** (servidores de desarrollo en 127.0.0.1, ya detenidos):
  - Modo mock (`VITE_API_URL=mock`, puerto 5194): ingreso del administrador → `/panel` con saludo, botones de
    «Administración» y «Ver estadísticas» cerrado; al abrir, «Actividad de hoy» cargó sola; cambio de sucursal CM → CB:
    aviso, saludo y estadística vueltos a leer; «Actividad»: filtros, detalle lateral, contraseña temporal a
    `bodega@…` (aviso y fila «Restableció la contraseña de un usuario · Correo: bodega@techzone.example · Debe
    cambiarla: sí»; la contraseña no aparece en la página); «Cerrar sesión» desde el menú → `/`; ingreso de `ventas@…`:
    menú solo con «Inicio», tablero con el estado vacío y `/panel/actividad` con «No tiene acceso a esta pantalla» y el
    permiso que falta. Consola sin errores.
  - Tamaños: a 360 px sin desplazamiento horizontal (`scrollWidth` 360), cajón del menú que se cierra al elegir, lista
    en tarjetas y filtros plegados; a 768, 1024 y 1280 px sin desplazamiento horizontal. Todos los controles visibles de
    44 px o más después de corregir lo que encontré: logotipo compacto (40 px de ancho), enlace «Inicio» de las migas
    (42 px), migas partidas en dos líneas a 1024 px y recortadas a 768 px, y enlaces de las migas enfocables sin verse
    en el teléfono.
  - Con la tienda caída (`VITE_API_URL=http://127.0.0.1:9`, puerto 5195): `/` muestra la cabecera liviana con
    «Ingresar» y «No pudimos cargar el catálogo» con «Reintentar»; «Ingresar» abre el formulario de ingreso.
