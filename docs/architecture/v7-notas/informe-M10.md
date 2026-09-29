# Informe del paquete M10 · módulos de «Administración»: Usuarios, Integraciones y Configuración (V7, tarea 11)

## Resumen

Tres módulos nuevos del panel web, cada uno en su carpeta de `src/4-presentation/panel/modules/` (no toqué nada fuera de
ellas, salvo este informe). Sin dependencias nuevas, sin `fetch` ni almacenamiento del navegador, sin commits (el
coordinador guardó un avance intermedio en el commit wip `5563b40` cuando el límite de uso cortó el trabajo; lo retomé
desde ahí). Todo el servidor se usa con `useRpcQuery` / `useRpcCommand` y los tipos se derivan del contrato generado.

| Módulo | Carpeta | Sección · orden | Permiso que lo muestra |
|---|---|---|---|
| Usuarios | `modules/usuarios` | Administración · 10 | `{ all: ['iam.users.manage'] }` |
| Integraciones | `modules/integraciones` | Administración · 20 | `{ all: ['integration.manage'] }` |
| Configuración | `modules/configuracion` | Administración · 30 | `{ any: ['iam.users.manage', 'billing.configure'] }` (la empresa con `iam.users.manage`; facturación y correo con `billing.configure`, como pide el paquete) |

Todas las pantallas siguen el patrón del ejemplo `actividad`: título y descripción, botones arriba, barra de filtros con
listas desplegables + búsqueda (+ fechas donde aplica) + «Limpiar filtros», tabla ordenable y paginada con acciones por
fila (tarjetas por debajo de 640 px), detalle en panel lateral, formularios en diálogos con validación por campo y el
mensaje del servidor DENTRO del diálogo, confirmación antes de lo irreversible, «Exportar CSV» en toda lista, estados de
carga / vacío / error con «Reintentar» y aviso de éxito tras cada comando. Filtros, orden y página en la dirección
(`useTableState`). Nada de indicadores a la vista: los del escritorio van como `stats` (plegados en «Inicio») y dentro de
cada pantalla en un `Collapsible` «Ver resumen …» cerrado que no consulta hasta abrirse. Cada acción se ofrece solo a quien
puede ejecutar su comando (`canRun`); el servidor decide igual (P-01).

### Usuarios (escritorio: `UsersView` + `UsersViewModel.cs`)

Pestañas «Usuarios» y «Roles y permisos» (la tercera pestaña del escritorio, «Empresa», está en Configuración).

- **Lista** (`GetUsersQuery` + `GetRolesQuery` + `GetBranchesQuery`): usuario (nombre, correo, «(usted)»), rol, sucursales
  («Todas (gerencia global)» si su rol tiene `corporate.branches.all`), estado (Activo / Debe cambiar la contraseña /
  Bloqueado / Sin contraseña / Inactivo, con la precedencia del escritorio) y último ingreso («Nunca ingresó»).
  Filtros: **Tipo** (Personal / Clientes web con cuántos hay; por defecto el personal, así no se mezcla con los clientes:
  pedido del cliente), rol, sucursal, estado y búsqueda. CSV con 10 columnas. Detalle lateral con «Lo que puede hacer»
  (las funciones de su rol, agrupadas por área, con los nombres del servidor).
- **Nuevo usuario** (`SaveUserCommand`): nombre, correo, rol en lista desplegable (roles del personal, con qué hace cada
  uno; Ventas propuesto como el escritorio), activo, **sucursales con casillas** (la activa marcada), contraseña inicial
  generada (se puede escribir otra, «Generar otra») y «Pedir que la cambie al ingresar». Al crear, la contraseña se
  muestra **UNA vez con «Copiar»** y se borra al cerrar.
- **Editar** (mismo comando, con `originalEmail`; no toca contraseña ni sucursales). Nadie puede cambiarse el rol ni
  desactivarse a sí mismo (como el escritorio). Un cliente web conserva el rol «Cliente web».
- **Activar / Desactivar** con confirmación (el mismo `SaveUserCommand` con otro `isActive`).
- **Restablecer la contraseña** (`ResetUserPasswordCommand`): desde la fila o desde el botón del tablero (ahí se elige a la
  persona en una lista con búsqueda); genera la temporal al abrir (un reintento viaja igual: idempotencia), desbloquea
  la cuenta y la muestra **una vez con «Copiar»**.
- **Asignar sucursales** (`AssignUserBranchesCommand`): casillas, al menos una, las inactivas solo se pueden quitar.
- **Ver su actividad**: enlace a Actividad filtrada por esa persona (con `iam.audit.view`).
- **Roles y permisos** (solo lectura, `GetRolesQuery`): una tarjeta por rol con qué hace en palabras, «N de M funciones»,
  las funciones que tiene por área y, plegado, lo que no puede; filtros por rol, área y búsqueda («¿quién puede anular
  facturas?»), «Ver sus usuarios (N)» (abre la lista filtrada) y la matriz rol × función en CSV.
- **Resumen plegado** (los KPI del escritorio): personal activo, bloqueados, deben cambiar la contraseña, ingresaron hoy,
  personal por rol y clientes web.

### Integraciones (escritorio: `IntegrationsView` + `IntegrationsViewModel.cs`)

Pestañas «API Keys», «Webhooks», «Entregas» y «Correos de reservas» (esta última solo con `sales.pcbuild.manage`).

- **API Keys** (`GetApiKeysQuery`): llave con su prefijo `minv_xxxxxxxx_••••` (nunca el token), alcances en palabras,
  sucursal, último uso, creada, estado (Activa / Vencida / Revocada). Filtros por estado, alcance, sucursal y búsqueda.
  **Nueva API Key** (`CreateApiKeyCommand` + `GetIntegrationCatalogQuery`): para qué es, **alcances con casillas** (leer
  catálogo y stock marcados, como el escritorio), sucursal y vencimiento en listas; el token se muestra **UNA vez con
  «Copiar»** con los permisos efectivos. **Revocar** con confirmación (`RevokeApiKeyCommand`).
- **Webhooks** (`GetWebhooksQuery`): dirección, eventos, sucursal, entregas correctas/fallidas, último intento y error,
  estado. **Nuevo webhook** (`CreateWebhookCommand`): dirección **https** (http solo en este equipo, sin usuario ni
  contraseña: las reglas del dominio), descripción, **eventos con casillas** (Venta cobrada marcado) y sucursal; el
  secreto de firma se muestra **una vez**. **Rotar el secreto** con confirmación (`RotateWebhookSecretCommand`: el nuevo
  se muestra una vez) y **Desactivar** con confirmación (`DisableWebhookCommand`). «Ver sus entregas» abre la pestaña
  Entregas filtrada.
- **Entregas** (`GetWebhookDeliveriesQuery`): el webhook y cuántas revisar van al servidor; **resultado** (correcta /
  fallida), **evento**, fechas y búsqueda en la página. Detalle con el error y la duración.
- **Correos de reservas** (`GetOutgoingMailsQuery`): solicitado, reserva (tipo y sucursal), destinatario, estado
  (Pendiente / Enviado / Agotado / Cancelado), intentos «n de 5», último error y próximo intento. Estado, número de
  reserva y cuántos revisar van al servidor; sucursal, tipo de reserva, fechas y búsqueda en la página. **Reenviar**
  desde la fila o «Reenviar una confirmación» por número (`ResendReservationMailCommand`, a su correo o a otro), con el
  mensaje del servidor si la reserva ya no está vigente. «Ver la reserva» enlaza a Ventas › Reservas.
- **Resumen plegado** (KPI del escritorio): llaves activas y en uso, webhooks activos y suscripciones, entregas correctas
  y fallidas (con enlace a las fallidas).

### Configuración (escritorio: `BillingSettingsView` + `BillingSettingsViewModel.cs`, la pestaña «Empresa» de `UsersView`, las acciones de configuración de «Estado SIAT»; `SettingsView` es de la estación)

Pestañas «Empresa», «Facturación SIAT» y «Correo de la empresa», cada una según los permisos.

- **Empresa** (`GetCompanySettingsQuery`): razón social, código, NIT, zona horaria y fecha mínima; **semáforo de stock**
  con el margen de alerta y los días sin rotación en listas desplegables (las opciones del escritorio) que cambia quien
  administra usuarios (`UpdateCompanySettingsCommand`).
- **Facturación SIAT** (`GetSiatSettingsQuery`): «Estado de la facturación» (sin configurar / activa / desactivada,
  ambiente, **lo que falta para facturar** —datos del Padrón, token del ambiente, casa matriz con código 0, licencia—,
  hora del SIN) con **Probar conexión** (`CheckSiatCommunicationCommand`), **Preparar SIAT** (`PrepareSiatCommand`) y
  **Sincronizar catálogos** (`SyncSiatCatalogsCommand`), cuyo resultado queda a la vista. Secciones **plegables** (se
  cargan al abrirlas):
  - *Datos del Padrón y del sistema* (abierta al entrar; `SaveSiatSettingsCommand`): NIT, razón social, código de sistema,
    ambiente, leyendas (vacías = las oficiales) y «Facturación activa»; avisa qué falta si se activa; **pasar a
    PRODUCCIÓN pide una confirmación aparte** (como el escritorio).
  - *Conexión con el SIN* por ambiente (`SaveSiatProfileCommand`): las seis direcciones, namespace, QR, espera y el
    **token delegado, que solo se escribe** (el campo empieza vacío, «vacío = conservar el guardado»; se ve solo si hay
    uno, su vigencia y cuándo se cargó). «Completar las direcciones» arma las seis desde la dirección base.
  - *Sucursales del Padrón* (`SaveSiatBranchCommand`): código (0 = casa matriz), municipio y teléfono de cada sucursal.
  - *Puntos de venta, CUIS y CUFD* (`GetSiatStatusQuery`): **Registrar punto de venta** (`RegisterSiatPointOfSaleCommand`,
    solo sucursales con código del Padrón), **Solicitar CUIS / CUFD** (`RequestCuisCommand`, `RequestCufdCommand`),
    **Probar la comunicación** del punto, **Vincular una caja** (`LinkPointOfSaleRegisterCommand`; cajas de
    `GetPosStateQuery` si la sesión puede leerlas) y **Cerrar en el SIN** (`CloseSiatPointOfSaleCommand`, DEFINITIVO:
    doble confirmación con casilla «Entiendo…»). Filtros por sucursal, modo y abiertos/cerrados.
  - *Actividades económicas del SIN* (`GetSiatActivitiesQuery`): código, descripción, tipo, sectores, vigente.
  - Sin la licencia `FISCAL_SIAT` (`moduleActive`), se ve todo con un aviso y los botones que escriben quedan
    deshabilitados.
- **Correo de la empresa** (`SaveMailSettingsCommand`): servidor, puerto, STARTTLS/SSL, usuario, **contraseña que nunca
  se muestra** (con una guardada solo se ofrece «Cambiar la contraseña»: sin marcarla viaja `null`), remitente y activo;
  **«Configurar con Gmail»** (smtp.gmail.com, 587, STARTTLS) y la ayuda plegada con los pasos de la contraseña de
  aplicación.

Lo del escritorio que NO aplica a la web: impresora, escáner y tema de la estación (`SettingsView`); los datos de la
sesión y el cambio de contraseña ya están en el menú del usuario del panel. Las contingencias (fuera de línea, CAFC,
recuperación, bitácora del SIN) son de Facturación › Estado del SIAT (otro paquete).

## Rutas y parámetros

| Dirección | Parámetros |
|---|---|
| `/panel/usuarios` | `pestana=roles` (sin él, «Usuarios»); `accion=nuevo` · `accion=restablecer` (abren su diálogo y se quitan de la dirección). Usuarios: `q`, `tipo` (por defecto `personal`; `clientes`; `tipo=` vacío = todos), `rol` (código), `sucursal` (código), `estado` (`activo`, `cambiar`, `bloqueado`, `sin-clave`, `inactivo`), `orden`, `sentido`, `pagina`, `filas`. Roles: `rol`, `area` (`ventas`, `tecnologia`, `inventario`, `compras`, `sucursales`, `facturacion`, `analisis`, `administracion`, `tienda`, `otros`), `q`. |
| `/panel/integraciones` | `pestana=llaves` (por defecto) · `webhooks` · `entregas` · `correos`. Llaves: `q`, `estado` (`activa`, `vencida`, `revocada`), `alcance` (código), `sucursal` (código o `_todas`). Webhooks: `q`, `estado` (`activo`, `desactivado`), `evento`, `sucursal`. Entregas: `webhook` (id; va al servidor), `registros` (100/200/500/1000; servidor), `resultado` (`correcta`, `fallida`), `evento`, `q`, `desde`, `hasta`. Correos: `estado` (`Pending`, `Sent`, `Exhausted`, `Cancelled`; servidor), `reserva` (servidor), `registros` (50/100/200/500; servidor), `sucursal`, `tipo` (`Build`, `Cart`), `q`, `desde`, `hasta`. |
| `/panel/configuracion` | `pestana=empresa` · `facturacion` · `correo` (por defecto la primera visible). Sucursales del Padrón: `suc_q`, `suc_estado` (`matriz`, `sucursal`, `sin-codigo`). Puntos de venta: `pv_q`, `pv_sucursal`, `pv_modo` (`Online`, `Offline`, `Recovering`, `ManualContingency`), `pv_estado` (por defecto `abiertos`; `cerrados`; vacío = todos). Actividades: `act_q`, `act_vigente` (`si`, `no`). |

Cambiar de pestaña deja la dirección limpia (cada lista tiene sus filtros). Enlaces a OTROS módulos (solo direcciones):
`/panel/actividad?usuario=<correo>` (Usuarios) y `/panel/reservas?q=<número>` (Integraciones › Correos; supone que
Reservas busca con `q`, como el ejemplo).

Botones del tablero (`actions`): «Nuevo usuario» (`/panel/usuarios?accion=nuevo`), «Restablecer una contraseña»
(`?accion=restablecer`), «Correos de reservas» (`/panel/integraciones?pestana=correos`, permiso extra
`sales.pcbuild.manage`) y «Configurar la facturación» (`/panel/configuracion?pestana=facturacion`, permiso extra
`billing.configure`). Estadísticas plegadas (`stats`): «Usuarios del sistema», «Integraciones» y «Correos de reservas»
(permiso extra `sales.pcbuild.manage`).

## Casos de uso usados

| Operación | Permiso (contrato) | Dónde |
|---|---|---|
| `GetUsersQuery` | `iam.users.manage` | Usuarios (lista, restablecer, estadística) |
| `GetRolesQuery` | `iam.users.manage` | Usuarios (roles, formulario, sucursales globales, detalle) |
| `SaveUserCommand` | `iam.users.manage` | nuevo, editar, activar/desactivar |
| `ResetUserPasswordCommand` | `iam.users.manage` | restablecer; y tras crear sin «debe cambiarla» (ver Pendientes) |
| `AssignUserBranchesCommand` | `corporate.branches.manage` + `MULTI_BRANCH` | asignar sucursales |
| `GetBranchesQuery` | `inventory.stock.view` | sucursales de Usuarios e Integraciones (opcional: sin permiso se derivan) |
| `GetIntegrationCatalogQuery` | `integration.manage` | alcances y eventos |
| `GetApiKeysQuery` · `CreateApiKeyCommand` · `RevokeApiKeyCommand` | `integration.manage` (+ `API_INTEGRATIONS` al crear) | API Keys |
| `GetWebhooksQuery` · `CreateWebhookCommand` · `RotateWebhookSecretCommand` · `DisableWebhookCommand` | `integration.manage` (+ `API_INTEGRATIONS` al crear) | Webhooks |
| `GetWebhookDeliveriesQuery` | `integration.manage` | Entregas |
| `GetOutgoingMailsQuery` · `ResendReservationMailCommand` | `sales.pcbuild.manage` | Correos de reservas |
| `GetCompanySettingsQuery` · `UpdateCompanySettingsCommand` | `inventory.stock.view` · `iam.users.manage` | Empresa |
| `GetSiatSettingsQuery` | `billing.view` | Facturación y Correo |
| `SaveSiatSettingsCommand` · `SaveSiatProfileCommand` · `SaveSiatBranchCommand` · `SaveMailSettingsCommand` | `billing.configure` + `FISCAL_SIAT` | Facturación y Correo |
| `GetSiatStatusQuery` | `billing.view` | puntos de venta (no estaba en la lista del paquete: es la única que trae los puntos con su id) |
| `GetSiatActivitiesQuery` | `billing.view` | actividades |
| `PrepareSiatCommand` · `SyncSiatCatalogsCommand` · `RequestCuisCommand` · `RequestCufdCommand` · `RegisterSiatPointOfSaleCommand` · `CloseSiatPointOfSaleCommand` · `LinkPointOfSaleRegisterCommand` | `billing.configure` + `FISCAL_SIAT` | Facturación |
| `CheckSiatCommunicationCommand` | `billing.view` + `FISCAL_SIAT` | «Probar conexión» (el escritorio lo tiene en esta pantalla) |
| `GetPosStateQuery` | `sales.pos.operate` | cajas para registrar/vincular un punto (opcional: sin permiso se escribe el código) |

## Pendientes

1. **`SaveUserCommand` no recibe «debe cambiarla»** (siempre la pide). La web cumple el paquete enviando después
   `ResetUserPasswordCommand(email, misma contraseña, mustChange: false)` cuando se desmarca la casilla (si falla, avisa
   que el usuario deberá cambiarla). Sugerencia para el servidor: `SaveUserCommand(…, bool MustChange = true)`.
2. **Correos de reservas y permisos**: la cola exige `sales.pcbuild.manage` y el módulo Integraciones `integration.manage`
   (el paquete); en la empresa de prueba solo el ADMIN tiene los dos, así que Ventas y Caja no ven la cola desde aquí.
   Si se quiere que la vean, Reservas (M3) puede enlazar a esta pestaña cuando la sesión tenga los dos, o abrir el módulo
   con `{ any: [...] }` (decisión de producto).
3. **Direcciones del SIN sin escribir URL en el código**: la prueba de arquitectura prohíbe `http(s)://` en el código del
   panel (P-11), así que no hay «Usar el simulador local» ni «Usar el patrón del SIN» con direcciones fijas como en el
   escritorio: la persona escribe la dirección base (la ayuda nombra los anfitriones del piloto, producción y simulador)
   y «Completar las direcciones» arma las seis con los caminos de `SiatEndpointSet.ForBaseUrl`; el namespace se toma de
   otro ambiente ya guardado. Sugerencia: que `GetSiatSettingsQuery` (o una `GetSiatEndpointDefaultsQuery`) devuelva las
   direcciones base conocidas, el namespace y la URL del QR por ambiente.
4. **Texto de uso de la API Key**: por la misma prueba (prohíbe las palabras de la cabecera de autorización estándar en el
   código) la ayuda dice «cabecera X-Api-Key» (el gateway también la acepta, `api-gateway-v1.md` §2).
5. **Para `kit/` o `hooks/`** (hoy duplicado en los tres módulos por P-09): el secreto «se muestra una vez» con «Copiar»
   (`SecretDialog`/`SecretReveal` + `copyText` con el portapapeles) y la pestaña/acción en la dirección
   (`useTabParam`, `useActionParam`).
6. **Filtros que hoy se aplican en la página** sobre lo último que devuelve el servidor: entregas de webhooks (máx. 1000:
   resultado, evento, fechas) y cola de correos (máx. 500: sucursal, tipo, fechas). Sugerencia: `Succeeded`,
   `EventType`, `From`/`To` en `GetWebhookDeliveriesQuery` y `BranchCode`, `Kind`, `From`/`To` en `GetOutgoingMailsQuery`.
   `WebhookDeliveryRow` no trae identificador (la tabla arma una clave con hora, dirección, evento e intento).
7. **Cajas para vincular** sin `sales.pos.operate`: `GetPosStateQuery` es de la caja; quien solo configura la facturación
   escribe el código a mano. Sugerencia: una consulta liviana de cajas con `billing.configure`.
8. **Licencias**: la sesión web no informa los módulos comerciales; Configuración usa `SiatSettingsView.moduleActive`
   para `FISCAL_SIAT`, pero para `API_INTEGRATIONS` (crear llaves y webhooks) y `MULTI_BRANCH` (asignar sucursales) solo
   el rechazo del servidor lo explica.
9. **Modo mock** (`VITE_API_URL=mock`): el RPC en memoria no atiende estas operaciones, así que en `npm run dev` con mock
   las tres pantallas muestran el error con «Reintentar». Las pruebas simulan el servidor en cada archivo.
10. **Prueba de otro paquete a actualizar en la integración**: `modules/inicio/InicioPage.test.tsx › «Inicio · con los
    módulos del proyecto»` espera que el grupo «Administración» del tablero tenga solo los dos botones de Actividad; con
    M10 tiene seis (en orden: `/panel/usuarios?accion=nuevo`, `/panel/usuarios?accion=restablecer`,
    `/panel/integraciones?pestana=correos`, `/panel/configuracion?pestana=facturacion`, `/panel/actividad`,
    `/panel/actividad?resultado=Rejected`). La segunda prueba de ese bloque (el CAJERO sin estadísticas) falla por
    módulos de otros paquetes (Caja, Ventas, Stock, Reservas), no por M10. No lo toqué (no es mi carpeta).

## Pruebas

Desde `src/3. Presentation/MINV.WebCatalog`:

- `npx vitest run src/4-presentation/panel/modules/usuarios src/4-presentation/panel/modules/integraciones src/4-presentation/panel/modules/configuracion`
  → **Test Files 6 passed (6) · Tests 94 passed (94)** (9,8 s):
  - `usuarios/users.test.ts` 15 · `usuarios/UsersPage.test.tsx` 20
  - `integraciones/integrations.test.ts` 19 · `integraciones/IntegrationsPage.test.tsx` 14
  - `configuracion/settings.test.ts` 10 · `configuracion/SettingsPage.test.tsx` 16
- `npx tsc -b --noEmit` → **0 errores** en todo el proyecto al terminar (durante el trabajo hubo errores pasajeros solo en
  carpetas de otros módulos en construcción: `ventas`, `armador`).
- `npx oxlint --max-warnings=0 src/4-presentation/panel/modules/{usuarios,integraciones,configuracion}` → **0 errores,
  0 advertencias**.
- `npx vitest run src/4-presentation/panel/registry src/architecture.test.ts` → **30 passed** (los tres módulos son
  válidos y respetan las reglas de arquitectura).
- `npx vitest run src/4-presentation/panel/shell src/4-presentation/panel/modules/inicio src/4-presentation/panel/modules/actividad src/4-presentation/app`
  → 92 passed, **2 fallan en `inicio`** por lo del punto 10 de «Pendientes».

Las pruebas de pantalla montan el panel real (`PanelApp`) con un registro que tiene SOLO el módulo probado (así no
dependen de los módulos que otros construyen a la vez), la sesión en memoria de un rol (`signedInAs`) o una sesión con
permisos elegidos, y un servidor simulado con `vi.spyOn(web.backend.rpc, 'call')` que responde las operaciones del módulo
y anota cada pedido: ninguna toca la red. Verifican la carga y los filtros (en la dirección y los que van al servidor),
cada formulario (validación por campo y el pedido EXACTO del contrato), los secretos mostrados una vez con «Copiar»
(portapapeles simulado), las confirmaciones (incluida la doble del cierre de un punto y la de producción), los errores
del servidor dentro del diálogo, el error de carga con «Reintentar», el CSV, los resúmenes plegados que no consultan
hasta abrirse, los botones del tablero (`?accion=`, `?pestana=`) y que cada rol ve solo lo suyo (sin acceso al módulo,
pestañas ocultas y acciones ocultas sin su permiso).
