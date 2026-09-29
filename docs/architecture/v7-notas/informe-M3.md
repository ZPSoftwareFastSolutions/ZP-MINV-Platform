# Informe M3 · módulos «Reservas» (Ventas) y «Armador de PC» (Tecnología) del panel web (tarea 11 de la V7)

> Escrito por quien construyó el paquete, en `src/3. Presentation/MINV.WebCatalog` (árbol de trabajo de la rama
> `Inventario-V7`). Sin commits: el avance parcial de `modules/reservas/` quedó en el commit wip `5563b40` del coordinador;
> después se terminó Reservas y se construyó Armador completo. Solo se tocaron `panel/modules/reservas/` y
> `panel/modules/armador/`.

## Resumen

Dos módulos nuevos, con la estructura del ejemplo `actividad` (definición liviana, funciones puras con sus pruebas,
pantallas con el conjunto del panel, pruebas de pantalla con el servidor simulado). Todos los tipos salen del contrato
generado; nada de red, almacenamiento ni HTML inyectado. La compatibilidad y todas las reglas las decide el servidor: la
página muestra, filtra y valida por comodidad.

### Ventas › Reservas (`/panel/reservas`)

Pantalla nueva de la V7 (diseño §8: el escritorio todavía no la tiene en este árbol; el escritorio mostraba las reservas
web dentro de Armador › Cotizaciones con el interruptor «Reservas web»). Reúne TODAS las reservas: de compra (carritos
`RES-WEB-…` de la tienda y `RES-<suc>-…` del mostrador) y de armado (`ARM-WEB-…` y cotizaciones `ARM-<suc>-…` que se
reservaron). Una reserva = un `PcBuild` que tuvo plazo (`reservedUntil`); las cotizaciones nunca reservadas quedan en el
Armador.

- **Filtros** (en la dirección, con listas desplegables): Tipo (Compra/Armado → servidor), Canal (Web/Mostrador →
  servidor), Estado (Reservada/Vencida/Vendida/Liberada; al servidor cuando es un solo estado), Vence (Hoy, Mañana, Ya
  vencidas, días de La Paz), Sucursal (aparece si hay más de una, p. ej. gerencia), Fecha de creación (rango con atajos) y
  búsqueda por número, nombre o teléfono (con o sin +591, con espacios). «Limpiar filtros» y contador.
- **Tabla** ordenable y paginada (tarjetas en el teléfono): número (con el nombre), tipo, canal, cliente, teléfono,
  total (con total al pie), reservado hasta (**resaltado** en ámbar con «vence en 2 h 30 min» si quedan menos de 6 h y en
  rojo «Venció el …» si se cumplió sin liberar; se actualiza solo cada minuto), estado y **estado del correo** (último
  correo de la reserva: Enviado, Pendiente, No se pudo enviar, Cancelado; o «Sin correo» / «No se envió»). Resumen en la
  barra: cantidad, suma y cuántas vencen en menos de 6 h. Exportar CSV (16 columnas; el teléfono en dígitos locales para
  que Excel no lo tome como fórmula).
- **Detalle lateral** (`GetPcBuildQuery` + `GetOutgoingMailsQuery` por número) con pestañas: Resumen (tipo, canal,
  sucursal, cliente, quién la recoge, teléfono con **Copiar teléfono** y **Abrir WhatsApp**, correo, fechas, plazo, precios
  vigentes, total, unidades reservadas, venta, motivo del cierre, notas del cliente y **datos para la factura**),
  Productos (con la disponibilidad: «Reservada · 3 más disponibles», «Sin stock»…), Bitácora y Correo (cada envío con
  estado, intentos, fechas y último error).
- **Acciones** (por fila, en el detalle y solo si la sesión puede): **Vender en caja** (enlace a
  `/panel/caja?reserva=<NÚMERO>`, solo reservada con precios vigentes y con permiso de caja), **Liberar** (motivo de la
  lista o escrito, confirmación de acción irreversible, error del servidor dentro del diálogo), **Reenviar correo** (al
  de la reserva o a otro correo validado; sin correo de contacto lo pide), **Copiar teléfono**, **Abrir WhatsApp**
  (`https://wa.me/591<dígitos>`, armado SOLO con los dígitos locales), **Nueva reserva en mostrador**
  (`ReserveCartCommand`): buscador de productos por nombre, SKU o código de barras con precio y disponible, cantidades
  (1 a 16, hasta 20 productos), total, cliente registrado opcional (completa nombre, teléfono y correo), nombre,
  teléfono boliviano, correo, días para recoger (1 a 3), notas, nombre de la reserva y datos para la factura (tipo de
  documento en lista desplegable, número, complemento solo con CI, razón social) con las reglas del SIN. Si falta stock,
  el mensaje del servidor con el detalle queda dentro del formulario. Al crearla se abre su detalle.
- **Estadística** «Reservas activas y su valor» (tablero, plegada): activas, valor reservado, vencen en menos de 6 h,
  vencidas sin liberar y barras por tipo y canal; la misma vista dentro de la pantalla en «Ver reservas activas y su
  valor» (cerrado al entrar, consulta al abrir).

### Tecnología › Armador de PC (`/panel/armador`, `/panel/armador/nuevo`, `/panel/armador/<NÚMERO>`)

Todo lo del escritorio (`PcBuilderView` + `PcBuilderViewModel`), repartido en dos pantallas:

- **Cotizaciones** (`/panel/armador`, la pestaña «Cotizaciones» del escritorio): solo armados de PC (los carritos son de
  Reservas). Filtros: búsqueda (número, armado, cliente o venta), Estado (al servidor), Vigencia (vigentes/vencidas),
  Publicado en la web, Canal y Fecha de creación. Columnas: número, armado (cliente · piezas), canal, compatibilidad
  (Compatible / Con errores aceptados / Con errores), vigencia o venta, estado (Borrador, Cotizado, Cotización vencida,
  Reservado, Reserva vencida, Vendido, Anulado), publicado y total. Acciones por fila según el estado: Abrir en el
  armador, Ver detalle, Vender en caja, Reservar stock, Publicar/Quitar de la web, Liberar reserva y Anular. Detalle
  lateral (resumen con la nota del estado y el contacto de la reserva web, piezas a sus precios cotizados, compatibilidad
  del servidor, bitácora) con «Abrir en el armador» y «Vender en caja». CSV. Las tarjetas del escritorio (cotizaciones
  vigentes, reservas web activas, vendidos, borradores) quedan plegadas en «Ver resumen de los armados».
- **Armador paso a paso** (`/panel/armador/nuevo`): 12 ranuras (procesador, placa, memoria, gráfica, almacenamiento,
  fuente, gabinete, refrigeración y los extras monitor, periféricos, software y servicios) con «Obligatoria · elija una
  pieza» / «Opcional» / cuántas piezas; «Paso N de 12» con Anterior/Siguiente y, como el escritorio, al elegir una pieza
  de una ranura única pasa sola a la siguiente obligatoria vacía. Candidatos del servidor (compatibles primero, los
  incompatibles atenuados con el motivo) con búsqueda, **categoría** en los extras (se propone la que se parece al nombre
  de la ranura y cada ranura recuerda la suya), **marca**, **precio desde/hasta**, «solo con stock» y «solo compatibles»,
  de a 24 con «Mostrar más». Compatibilidad **en vivo** (título que se anuncia, errores y avisos, consumo, fuente
  recomendada, potencia y carga de la fuente). Piezas elegidas con cantidades ± en las ranuras de varias piezas y quitar;
  total a los precios de lista vigentes; nombre (propuesto si se deja vacío), cliente y vigencia (3/7/15/30 días);
  **Guardar borrador** y **Guardar cotización** (con errores de compatibilidad pide confirmación y envía
  `acceptIncompatible: true`, regla T-06). Al guardar uno nuevo la dirección pasa a la del armado.
- **Armado guardado** (`/panel/armador/<NÚMERO>`): un borrador se sigue editando; cotizado, reservado, vendido o anulado
  queda **congelado**: piezas a los precios cotizados con su disponibilidad, contacto de la reserva web, bitácora y las
  acciones de su estado: **Vender en caja**, **Reservar stock** (24/48/72/168 h u otra cantidad de 1 a 720),
  **Publicar/Quitar de la web** (solo armados del mostrador), **Imprimir cotización** (vista imprimible del navegador:
  empresa, sucursal, número, fechas, cliente, piezas, total con IVA, notas de compatibilidad, quién atendió; en el
  navegador se puede guardar como PDF), **Liberar reserva** y **Anular** (motivo opcional). Un número que es un carrito
  avisa y enlaza a Reservas; uno que no existe muestra el error del servidor con «Reintentar».
- Con solo `sales.view` + `inventory.stock.view` se puede armar y revisar la compatibilidad; guardar y los comandos
  piden `sales.pcbuild.manage` (se avisa qué permiso falta, sin botones).

**Tablero** (`actions`): «Nueva reserva en mostrador» (`/panel/reservas?nueva=1`), «Reservas que vencen hoy»
(`/panel/reservas?vence=hoy&orden=plazo&sentido=asc`) y «Armar una PC» (`/panel/armador/nuevo`). **Estadística**:
«Reservas activas y su valor».

**Diferencias con el escritorio** (a propósito o por falta de datos): la proforma en rollo ESC/POS y el PDF propio se
reemplazan por la impresión del navegador; las miniaturas de producto, las insignias de plataforma (T-07) de los
candidatos y los meses de garantía de la proforma no se muestran (ver «Pendientes»).

## Rutas y parámetros

| Ruta | Parámetros de la dirección |
|---|---|
| `/panel/reservas` | `q` · `tipo` (`Cart`, `Build`) · `canal` (`Web`, `Desktop`) · `estado` (`reservada`, `vencida`, `vendida`, `liberada`) · `vence` (`hoy`, `manana`, `vencidas`) · `sucursal` (código) · `desde`/`hasta` (fecha de creación, días de La Paz) · `orden` (`numero`, `tipo`, `canal`, `cliente`, `telefono`, `total`, `plazo`, `estado`, `correo`; por defecto `plazo` desc) · `sentido` · `pagina` · `filas` · `nueva=1` (abre la reserva en mostrador y se quita de la dirección) |
| `/panel/armador` | `q` · `estado` (`Draft`, `Quoted`, `Reserved`, `Sold`, `Cancelled`) · `vigencia` (`vigente`, `vencida`) · `publicada` (`si`, `no`) · `canal` · `desde`/`hasta` · `orden` (`numero`, `armado`, `canal`, `compatibilidad`, `vigencia`, `estado`, `web`, `total`; por defecto el orden del servidor, más nuevos primero) · `sentido` · `pagina` · `filas` |
| `/panel/armador/nuevo` | — (armado nuevo) |
| `/panel/armador/:numero` | el número del armado (`ARM-CM-000012`) |

Enlaces a otros módulos (solo direcciones): `/panel/caja?reserva=<NÚMERO>` (M1, desde reservas y armados),
`/panel/reservas?q=<NÚMERO>` (desde el armador, para un carrito), `/panel/armador/nuevo` (botón «Armar una PC» de
Reservas), `/panel/reservas?estado=reservada` (desde la estadística).

Permisos de los módulos: Reservas `{ all: ['sales.pcbuild.manage', 'sales.view'] }` (la guía pide solo
`sales.pcbuild.manage`, pero `GetPcBuildsQuery` y `GetPcBuildQuery` exigen `sales.view`: se agregó para no mostrar un
módulo que no puede cargar; en la matriz de roles todos los que tienen el primero tienen el segundo). Botón «Nueva
reserva en mostrador»: además `inventory.stock.view` (busca productos). Armador `{ all: ['sales.view',
'inventory.stock.view'] }`.

## Casos de uso usados

| Operación | Permiso (contrato) | Dónde | Pedido (siempre con TODOS sus parámetros) |
|---|---|---|---|
| `GetPcBuildsQuery` | `sales.view` | Lista de reservas, lista de cotizaciones, estadística y resumen | `{ status, channel, kind }` (null = sin filtro; el armador siempre `kind: 'Build'`) |
| `GetPcBuildQuery` | `sales.view` | Detalle lateral de ambos módulos, armado guardado | `{ number }` |
| `GetOutgoingMailsQuery` | `sales.pcbuild.manage` | Estado del correo (lista, `take: 500`) y correos de una reserva (`take: 50`) | `{ status: null, number, take }` |
| `ReleasePcBuildReservationCommand` | `sales.pcbuild.manage` | Liberar (Reservas y Armador) | `{ number, reason }` |
| `ResendReservationMailCommand` | `sales.pcbuild.manage` | Reenviar correo | `{ number, email }` (`null` = el de la reserva) |
| `ReserveCartCommand` | `sales.pcbuild.manage` | Nueva reserva en mostrador | `{ items: [{ sku, quantity }], contactName, contactPhone, contactEmail, notes, holdDays, buyer: { documentType, documentNumber, complement, name } \| null, customerCode, name }` |
| `GetSellableProductsQuery` | `inventory.stock.view` | Buscador de productos de la reserva en mostrador | `{}` |
| `GetCustomersQuery` | `inventory.stock.view` | Cliente registrado (reserva y armado) | `{}` |
| `GetCatalogOptionsQuery` | `inventory.stock.view` | Categorías de los extras del armador | `{}` |
| `GetPcBuildCandidatesQuery` | `inventory.stock.view` | Candidatos de cada ranura | `{ slot, current: [{ slot, sku, quantity }], text, onlyInStock, categoryCode }` |
| `CheckPcBuildQuery` | `inventory.stock.view` | Compatibilidad en vivo | `{ items: [{ slot, sku, quantity }] }` |
| `SavePcBuildCommand` | `sales.pcbuild.manage` | Guardar borrador / cotización | `{ id, name, customerCode, items, quote, validDays, acceptIncompatible, kind: 'Build' }` |
| `ReservePcBuildCommand` | `sales.pcbuild.manage` | Reservar stock | `{ number, hours }` |
| `PublishPcBuildCommand` | `sales.pcbuild.manage` | Publicar / quitar de la web | `{ number, published }` |
| `CancelPcBuildCommand` | `sales.pcbuild.manage` | Anular | `{ number, reason }` (`null` sin motivo) |
| `SellPcBuildCommand` | `sales.pos.operate` + `inventory.movements.register.sales` | NO se envía aquí: solo decide si se muestra «Vender en caja» (lo cobra el módulo Caja) | — |

## Pendientes

1. **Contrato/servidor · `PcBuildRow` sin el código del cliente.** Trae `Customer` (el NOMBRE). Para editar un borrador,
   el armador busca el código por nombre en `GetCustomersQuery` (como el escritorio): si dos clientes se llaman igual
   puede elegir el que no es. Propuesta: agregar `CustomerCode` a `PcBuildRow`.
2. **Servidor · metadatos de las ranuras.** No hay operación que diga qué ranuras son obligatorias, de varias piezas o
   por categoría ni la categoría sugerida: `armador/builder.ts` (`SLOTS`) refleja `PcCompatibility.Required` y
   `PcBuild.MultiSlots` como guía de la pantalla (el servidor valida igual al guardar). Propuesta:
   `GetPcBuilderSlotsQuery` → `IReadOnlyList<PcSlotInfo(Slot, Label, Required, Multi, ByCategory, DefaultCategoryCode)>`
   con `inventory.stock.view`.
3. **Servidor · `GetPcBuildsQuery` acotada.** Devuelve los 500 más recientes sin fechas, texto ni «solo reservas»: las
   fechas, la búsqueda y «se reservó alguna vez» se filtran en la página (una tienda con muchas cotizaciones puede no ver
   reservas viejas). Propuesta: `From`, `To`, `Text`, `Take` y `ReservedOnly`, y `ReservedAt` en `PcBuildRow` (hoy la
   fecha de la lista es la de creación: para una cotización que se reservó después no coincide con el día de la reserva).
4. **Servidor · proforma y candidatos.** `PcBuildItemView`/`PcBuildCandidate` no traen los meses de garantía ni las
   plataformas (T-07) que el escritorio saca de su catálogo técnico, y el panel no tiene miniaturas de producto
   (`imageId` sin una operación liviana de imagen para el panel). Propuesta: `WarrantyMonths` y `Platforms` en esas
   filas y una operación de miniatura por `imageId`.
5. **Módulo Caja (M1).** «Vender en caja» lleva a `/panel/caja?reserva=<NÚMERO>` para reservas de compra, de armado y
   cotizaciones vigentes: depende de que Caja cargue la reserva con `GetPcBuildQuery` y cobre con `SellPcBuildCommand`.
6. **Kit · acciones de fila con enlace.** `RowActionItem` solo tiene `onSelect`: «Vender en caja», «Abrir en el armador»
   y «Abrir WhatsApp» del menú usan `navigate`/`window.open`. Propuesta: `to` y `href` en `RowActionItem`.
7. **Kit · piezas repetidas en los dos módulos** (los módulos no se importan entre sí): el diálogo de liberar con motivo
   (`ReleaseDialog`), `ErrorDetails` (el mensaje del servidor con su lista `errors`, que `Form`/`ConfirmDialog` no
   muestran solos) y `useNow` (hora que se renueva cada minuto). Propuesta: `ReasonConfirmDialog`, que `Form` muestre
   `error.errors` y un `useNow` en `hooks`.
8. **Arquitectura · enlace de WhatsApp.** `src/architecture.test.ts` rechaza cualquier `https://` en el código del panel
   (la regla habla de recursos: scripts, estilos, fuentes, imágenes). El enlace a `wa.me` es navegación en otra pestaña,
   así que se arma por partes (`['https:', '', 'wa.me'].join('/')`, comentado en `reservations.ts`). Propuesta: un
   `whatsappLink(teléfono)` en `lib` o permitir `wa.me` en la prueba.
9. **Kit · vista imprimible.** No existe: el armador trae `armador/quotePrint.css` (reglas `@media print` con
   `html[data-print='cotizacion']`, el mismo mecanismo que `pages/builder/builder.css` de la tienda) y un portal
   (`QuotePrint.tsx`). Caja y Ventas necesitan lo mismo: convendría un `PrintView` en el kit.
10. **Modo mock.** `VITE_API_URL=mock` no atiende ninguna de estas operaciones: con `npm run dev` en modo mock las dos
    pantallas muestran el error con «Reintentar». Las pruebas simulan el servidor. Falta la revisión visual en un
    navegador contra el servidor real (con datos de prueba) y a 360 px.
11. **Cola de correos completa.** Aquí se ve el correo de cada reserva; la cola entera por estado (`GetOutgoingMailsQuery`
    con `Status`) va en Administración según el diseño §8 (otro paquete).
12. **Typecheck del proyecto:** `npx tsc -b --noEmit` tiene 1 error y es de otro paquete:
    `src/4-presentation/panel/modules/caja/PosPage.test.tsx(360,32)` (M1, en construcción). Ninguno en `reservas/` ni
    `armador/`.

## Pruebas

Desde `src/3. Presentation/MINV.WebCatalog` (la salida de vitest a un archivo):

- `npx vitest run src/4-presentation/panel/modules/reservas` → **Test Files 2 passed (2) · Tests 40 passed (40)**
  (≈ 19 s), sin avisos en la salida.
  - `reservas/reservations.test.ts` (20): estado de la reserva (vigente, vencida sin cerrar o cerrada por vencimiento,
    vendida, liberada), plazo resaltado y tiempo restante, vender solo reservada y vigente, estado del correo (último
    correo, sin correo, sin datos), solo lo que se reservó alguna vez, pedido al servidor por tipo/canal/estado/vence,
    filtros (tipo, canal, estado, sucursal, fechas de La Paz, vence hoy/mañana/vencidas, búsqueda por número, nombre y
    teléfono con espacios o +591), resumen, CSV exacto, estadística, disponibilidad, WhatsApp solo con dígitos, motivo,
    carrito (sumar, tope 16, total), validación como el servidor (teléfono boliviano, correo, cantidades, una línea,
    documentos del SIN) y el pedido EXACTO de `ReserveCartCommand`, clientes ofrecidos.
  - `reservas/ReservationsPage.test.tsx` (20): lista con plazo resaltado, estado y correo y pedidos exactos; filtros en la
    dirección y al servidor; «vencen hoy» desde el tablero con orden; búsqueda por teléfono y nombre; error con
    «Reintentar»; la cola de correos que falla no tumba la lista; CSV; detalle (factura, notas, productos con
    disponibilidad, bitácora, correos, acciones al pie); Liberar (motivo obligatorio, otro motivo, pedido exacto, lista
    actualizada); rechazo del servidor dentro del diálogo y reintento; Reenviar (vacío = `email: null`, correo inválido,
    reserva sin correo); Copiar teléfono, Abrir WhatsApp y Vender en caja; nueva reserva (validaciones, producto por
    código de barras, cantidad, cliente que completa el contacto, días, NIT solo dígitos, pedido EXACTO, se abre la
    reserva creada); falta de stock con el detalle del servidor; `?nueva=1`; gerencia sin «Vender en caja»; bodega sin
    acceso; botones y estadística del tablero; estadística con su error.
- `npx vitest run src/4-presentation/panel/modules/armador` → **Test Files 2 passed (2) · Tests 37 passed (37)**
  (≈ 16 s), sin avisos en la salida.
  - `armador/builder.test.ts` (19): reemplazar/sumar piezas, tope de 16 y de 20 líneas, orden de las ranuras, carga de un
    armado guardado y precios vivos, nombre propuesto y siguiente obligatoria, candidatos por marca/precio/compatibles,
    categoría propuesta, resumen de compatibilidad y carga de la fuente, disponibilidad, estados y vigencia, acciones por
    estado, nota del armado, horas de reserva, pedido EXACTO de `SavePcBuildCommand`, clientes, lista (solo armados,
    pedido al servidor, filtros), CSV y resumen.
  - `armador/Armador.test.tsx` (18): lista (columnas, pedido `kind: 'Build'`, filtros en la dirección, acciones de cada
    estado, reservar con otra cantidad de horas, publicar, anular con motivo, liberar con el rechazo en el diálogo,
    detalle lateral con enlaces, error y CSV); armado paso a paso con la compatibilidad en vivo, candidato incompatible
    con su motivo, cotizar con errores solo tras confirmar (pedido EXACTO) y paso a la dirección del armado congelado;
    borrador con nombre, cliente, vigencia y cantidades, reabierto editable y vuelto a guardar sobre el mismo; extras por
    categoría y filtros de candidatos; armado congelado con «Vender en caja», publicar e imprimir (`window.print` con la
    cotización imprimible); carrito que lleva a Reservas; armado inexistente; sin `sales.pcbuild.manage` (lista sin
    comandos y armador con el aviso); bodega sin acceso; botón del tablero.
- `npx vitest run src/architecture.test.ts src/4-presentation/panel/registry` → **Test Files 2 passed (2) · Tests 30
  passed (30)** (importaciones permitidas, sin tipos del servidor a mano, sin red ni almacenamiento, registro válido con
  todos los módulos del proyecto).
- `npx tsc -b --noEmit` → 0 errores en `reservas/` y `armador/` (el único error del proyecto es de `caja/`, ver
  Pendientes 12).
- `npx oxlint src/4-presentation/panel/modules/reservas` y `…/armador` → sin avisos ni errores.

Las pruebas de pantalla montan `PanelApp` con un registro que tiene SOLO el módulo probado (`buildRegistry`), no
`preloadPanel()`/`PANEL_REGISTRY`: durante el trabajo, un módulo en construcción de otro paquete rompía el
descubrimiento de módulos y con eso cualquier prueba que lo cargara. La hora queda fija (29/09/2026 10:00 de La Paz) con
`vi.useFakeTimers({ toFake: ['Date'] })`.
