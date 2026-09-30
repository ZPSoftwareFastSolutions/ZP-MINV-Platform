# Informe M4 · módulos «Series» y «Garantías» del panel web (tarea 11 de la V7)

Carpetas: `src/3. Presentation/MINV.WebCatalog/src/4-presentation/panel/modules/series/` y `…/modules/garantias/`. No se
tocó nada fuera de esas dos carpetas (ni `shell/`, `kit/`, registro, contrato, otros módulos ni `package.json`), salvo este
informe. Reglas aplicadas: P-01, P-07 a P-10 y T-02, T-04 y T-05 (la garantía la calcula el servidor; las transiciones del
caso las decide el servidor: la web solo muestra `nextStatuses`).

## Resumen

### Tecnología › Series (`/panel/series`, permiso `inventory.serials.view`)

Equivale a `SerialsView.xaml` + `SerialsViewModel.cs` (y sus diálogos `RegisterSerialsDialog` y `DisposeSerialDialog`).

| Escritorio | Web |
|---|---|
| Búsqueda por serie, IMEI o SKU (al servidor) y estado (al servidor), sucursal (en la lista) | Búsqueda (serie, IMEI o SKU) y **estado** al servidor; **producto** (lista con búsqueda de los productos que llevan serie o IMEI, al servidor como `sku`); **sucursal**, **tipo** (serie / IMEI), **garantía** (vigente / vencida / sin garantía en curso) y **fecha de ingreso** en la página; «Series a revisar» (500 · 1.000 · 2.000 · 5.000 más recientes, 1.000 por defecto como el escritorio). Todo en la dirección; «Limpiar filtros». |
| 4 tarjetas a la vista (en stock, vendidas con garantía vigente, en RMA o devueltas, bajas) sobre TODAS las series | Plegadas en «Ver resumen de las unidades ^» (no consultan hasta abrirse; `GetSerialSummaryQuery`), con barras por estado. Es la misma estadística que se ofrece al tablero. |
| Tabla: serie/IMEI, producto, venta, garantía, estado | Serie o IMEI (con su tipo), producto y SKU, estado, dónde está (sucursal · almacén), venta (fecha · factura · cliente), garantía («Hasta …» / «Venció el …»), ingreso. Ordenable, paginada, tarjetas bajo 640 px, menú por fila. Resumen «N de M series · las más recientes: afine la búsqueda». |
| Panel derecho: garantía derivada, origen (proveedor · recepción · ingreso), venta, casos RMA, trazabilidad | Detalle lateral en pestañas: **Resumen** (recuadro de garantía con los textos del escritorio, producto, tipo, estado, dónde está, ingreso, origen, venta, cliente, fin de garantía), **Trazabilidad** (línea de tiempo: hecho, fecha, sucursal · documento · usuario, nota) y **Garantías** (casos de la unidad, también cuando fue el reemplazo, con «Ver el caso»). |
| «Abrir RMA» (vendida o devuelta, sin caso abierto, `service.rma.open`) | «Abrir caso de garantía» → Garantías con la serie ya escrita; si ya tiene caso abierto (lo dice `GetWarrantyStatusQuery.openClaim`), «Ver el caso RMA-…». También en el menú de la fila. |
| «Dar destino» (devuelta o en RMA, `inventory.serials.manage`): al proveedor o de baja, con motivo | Igual, con **confirmación** (no se puede deshacer): destino (radio), motivo (los sugeridos del escritorio u «Otro motivo»), error del servidor dentro de la confirmación, aviso de éxito y recarga. |
| «Registrar series de stock» (`inventory.serials.manage`) | Igual: producto (lista con búsqueda: los que llevan serie o tienen stock), series una por línea (el lector de códigos agrega cada una con Enter; también se puede pegar una lista), nota («Inventario inicial de series»). Marca antes de enviar las repetidas, con espacios, demasiado largas o el IMEI mal escrito (15 dígitos + Luhn, comodidad: el servidor valida igual). |
| «Ficha» del producto | «Ver solo las series de este producto» (filtra la lista) y «Ver las ventas de <cliente>» (`/panel/ventas?cliente=<código>`, con `sales.view`). No hay enlace a una ficha de producto: ver Pendientes. |
| — | «Consultar una serie» (botón de la pantalla y del tablero): se escanea o escribe la serie y el servidor responde su garantía vigente (`GetWarrantyStatusQuery`): producto, estado, venta, cliente, meses, fin, caso abierto; desde ahí «Ver la trazabilidad» o «Abrir caso de garantía». Exportar CSV; estados de carga, vacío y error con «Reintentar». |

Tablero: `actions` «Consultar una serie» (`/panel/series?consultar=1`); `stats` «Unidades por estado».

### Tecnología › Garantías (`/panel/garantias`, ver con `inventory.serials.view`)

Equivale a `WarrantyClaimsView.xaml` + `WarrantyClaimsViewModel.cs` (y `OpenClaimDialog`, `MoveClaimDialog`,
`ReplaceClaimDialog`).

| Escritorio | Web |
|---|---|
| Chips de estado: «Abiertos» (al entrar), «Todos» y cada estado | Lista desplegable **Estado** («Abiertos (sin entregar)» al entrar, «Todos los casos» o un estado), que va al servidor (`status` / `onlyOpen`); además **sucursal**, **cobertura** (en garantía / con cargo), **tiempo en el taller** (más de 7, 15 o 30 días), **fecha de recepción** y búsqueda (número RMA, serie, cliente, producto, SKU, falla, proveedor o reemplazo). Todo en la dirección. |
| 4 tarjetas a la vista (abiertos y el más antiguo, en diagnóstico o proveedor, con cargo, entregados y con reemplazo) | Plegadas en «Ver resumen de los casos ^»: consultan TODOS los casos recién al abrirse, con barras por estado. |
| Tabla: caso, equipo y cliente, falla, cobertura, estado | Caso (y sucursal), recibido, equipo y cliente (producto, SKU · serie, cliente), falla, cobertura, estado, tiempo («5 días abierto» / «Cerrado el …»). Menú por fila: ver detalle, imprimir la orden de servicio, ver la serie, agregar nota. |
| Detalle: garantía, venta, resolución, falla, acciones según `NextStatuses`, «Agregar nota», «Ver la serie», bitácora | Detalle lateral (`GetWarrantyClaimQuery`): cobertura y garantía, falla reportada, cliente, sucursal, serie, producto, recibido, tiempo, venta, proveedor, reemplazo, resolución, «Ver la serie» (`/panel/series?q=…&producto=…&ver=…`), «Ver las ventas del cliente»; pestaña **Bitácora**. Al pie, SOLO los pasos que el servidor ofrece (`nextStatuses`) y solo con `service.rma.manage`; «Orden de servicio»; «Agregar nota» (caso abierto, `service.rma.open`). |
| Avanzar (diagnóstico, proveedor con su lista, reparado y rechazado con resolución sugerida, entregar) | Cada paso con **confirmación** («¿Enviar el equipo del caso … al proveedor?»): ayuda del escritorio, proveedor (`GetCatalogOptionsQuery`; vacío = el preferido del producto), resolución obligatoria al reparar, reemplazar o rechazar (sugeridas u «Otra»), nota opcional; error del servidor dentro de la confirmación; aviso de éxito; recarga lista y detalle. Rechazar va en rojo. |
| «Reemplazar con otra unidad»: elegir o escanear una unidad disponible del mismo producto; resolución | Igual, con confirmación (sale del stock con su asiento: no se puede deshacer): unidades de `GetAvailableSerialsQuery` (nunca la del caso) en una lista con búsqueda o, si no hay, la serie escrita; resolución («Reemplazo por falla de fábrica»). Si el caso ya tiene la unidad de reemplazo registrada, «Reemplazar» es un paso normal con resolución. |
| Abrir caso: serie (escaneada) → garantía al instante; falla (sugeridas); fuera de garantía, «reparación con cargo»; cliente si la venta es de otra sucursal | Igual: la serie se consulta al salir del campo, con Enter o «Consultar» (`GetWarrantyStatusQuery`); tarjeta con producto, estado, venta, cliente, garantía y aviso si ya tiene caso abierto o no está vendida; falla con sugerencias de un clic; «Reparación con cargo» obligatoria si está fuera de garantía; **cliente del caso** (lista con búsqueda de `GetCustomersQuery`, o el código escrito) si la venta no está a la vista. Llega con la serie desde Series (`?abrir=1&serie=…&sku=…`). Al abrir, se ve el detalle del caso nuevo. |
| — (el escritorio no imprime la orden) | **Orden de servicio imprimible**: vista previa y «Imprimir» (solo la orden, con la impresora del navegador; negro sobre blanco): empresa, sucursal, número, recepción, estado, cobertura, cliente, equipo con su serie, garantía y venta, falla, resolución, bitácora, firmas del cliente y de quien recibe. Exportar CSV; estados de carga, vacío y error con «Reintentar». |

Tablero: `actions` «Abrir un caso de garantía» (`/panel/garantias?abrir=1`, con `service.rma.open`).

Decisiones:

- La garantía NUNCA se calcula en la web (T-04): se muestran `warrantyUntil` / `inWarranty` del servidor; la lista de
  series solo compara la fecha de fin con hoy (La Paz) para el color y el filtro «Garantía», como el escritorio.
- Las transiciones NUNCA se deciden en la web (T-05): los botones salen de `nextStatuses`. La web solo sabe qué pide cada
  paso (resolución, proveedor, reemplazo) para armar el formulario; el servidor vuelve a validar todo.
- `?ver=<serie|número>` queda en la dirección mientras el detalle está abierto y se quita al cerrarlo: el panel lateral
  del kit se cierra al navegar, así que quitarlo al abrir lo cerraba enseguida.

## Rutas y parámetros

- `/panel/series` (única pantalla del módulo `series`).
  - Al servidor: `q` (serie, IMEI o SKU), `estado` (`InStock` · `Sold` · `InTransit` · `Returned` · `InRma` ·
    `ReturnedToSupplier` · `Scrapped`), `producto` (SKU), `registros` (`500` · `1000` · `2000` · `5000`).
  - En la página: `sucursal` (código), `tipo` (`Serial` · `Imei`), `garantia` (`vigente` · `vencida` · `sin`), `desde`,
    `hasta` (fecha de ingreso). Tabla: `orden`, `sentido`, `pagina`, `filas`.
  - `consultar=1` abre «Consultar una serie» (tablero; se quita de la dirección al abrirse).
  - `ver=<serie>` abre el detalle de esa unidad (lo usa Garantías con `q` y `producto`; se quita al cerrar el detalle).
- `/panel/garantias` (única pantalla del módulo `garantias`).
  - Al servidor: `estado` (`abiertos` por defecto · vacío = todos · `Received` · `Diagnosing` · `SentToSupplier` ·
    `Repaired` · `Replaced` · `Rejected` · `Delivered`).
  - En la página: `q`, `sucursal`, `cobertura` (`garantia` · `cargo`), `dias` (`7` · `15` · `30`), `desde`, `hasta`
    (fecha de recepción). Tabla: `orden`, `sentido`, `pagina`, `filas`.
  - `abrir=1` abre «Abrir un caso de garantía» (tablero), con `serie=<serie>` y `sku=<SKU>` ya escritos (Series); se
    quitan de la dirección al abrirse. Sin `service.rma.open` no abre nada.
  - `ver=<número>` abre el detalle del caso (Series; se quita al cerrar el detalle).
- Enlaces a otros módulos (solo rutas): entre Series y Garantías (los de arriba) y `/panel/ventas?cliente=<código>`
  («Ver las ventas…», parámetro definido por M2, solo con `sales.view`).

## Casos de uso usados

| Operación | Pedido exacto | Para qué | Permisos (contrato) |
|---|---|---|---|
| `SearchSerialsQuery` | `{ text, status, sku, max }` (sin filtro = `null`; `max` 1000 por defecto) | lista de Series | `inventory.serials.view` |
| `GetSerialSummaryQuery` | `{}` | resumen plegado y estadística «Unidades por estado» | `inventory.serials.view` |
| `GetSerialTraceQuery` | `{ serial, sku }` | detalle: trazabilidad, origen, casos | `inventory.serials.view` |
| `GetWarrantyStatusQuery` | `{ serial, sku }` (`sku: null` al consultar o abrir sin SKU) | garantía vigente, cliente, caso abierto (detalle, «Consultar una serie», «Abrir un caso») | `inventory.serials.view` |
| `RegisterStockSerialsCommand` | `{ sku, serials, note }` | registrar series de unidades en stock | `inventory.serials.manage` |
| `DisposeSerialCommand` | `{ serial, disposal: 'ReturnToSupplier' \| 'Scrap', reason, sku }` | dar destino | `inventory.serials.manage` |
| `GetWarrantyClaimsQuery` | `{ status, onlyOpen }` | lista de Garantías (y resumen: `{ status: null, onlyOpen: false }`) | `inventory.serials.view` |
| `GetWarrantyClaimQuery` | `{ number }` | detalle, pasos siguientes y orden de servicio | `inventory.serials.view` |
| `OpenWarrantyClaimCommand` | `{ serial, issue, chargeableRepair, customerCode, sku }` | abrir un caso | `service.rma.open` |
| `MoveWarrantyClaimCommand` | `{ number, next, resolution, supplierCode, note }` | avanzar el estado | `service.rma.manage` |
| `AddWarrantyClaimNoteCommand` | `{ number, note }` | nota en la bitácora | `service.rma.open` |
| `IssueWarrantyReplacementCommand` | `{ number, replacementSerial, resolution }` | entregar la unidad de reemplazo | `service.rma.manage` |

Además, porque el escritorio las usa en esas mismas pantallas (con respaldo si la sesión no tiene el permiso):

| Operación | Pedido | Para qué | Permisos | Sin el permiso |
|---|---|---|---|---|
| `SearchTechProductsQuery` | `{ text: null, categoryCode: null, platform: null, filters: null, onlyInStock: false, max: 2000 }` | productos del filtro y de «Registrar series» | `inventory.stock.view` | el filtro usa los productos de la lista; el registro pide el SKU escrito |
| `GetAvailableSerialsQuery` | `{ sku, warehouseCode: null }` | unidades para el reemplazo | `inventory.serials.view` | — |
| `GetCatalogOptionsQuery` | `{}` | proveedores al enviar al proveedor | `inventory.stock.view` | se usa el proveedor preferido |
| `GetCustomersQuery` | `{}` | cliente del caso (venta de otra sucursal) | `inventory.stock.view` | se escribe el código |

Todos los tipos salen del contrato generado por el nombre de la operación (`RpcRequestOf` / `RpcResponseOf`); los botones
se muestran con `canRun(operación)` y el servidor vuelve a decidir (P-01).

## Pendientes

1. **Kit — área imprimible.** Igual que M2: no hay una forma común de imprimir solo una parte de la página. Está resuelto
   DENTRO del módulo con una copia de la solución de Ventas (`garantias/PrintArea.tsx`, clase `print:` en el `<body>`).
   Sugerencia: llevar `PrintArea` a `kit/` (lo usan Ventas, Garantías y seguramente Caja y Documentos).
2. **Kit — error de un comando con su lista.** `ServerError` (mensaje + `error.errors`) está copiado en `series/` y
   `garantias/` (como `ErrorDetails` de Reservas). Sugerencia: que `Form`/`ConfirmDialog` acepten el `WebApiError` y
   muestren la lista.
3. **Kit — panel lateral y dirección.** `SidePanel` (el cajón de la tienda) se cierra con CUALQUIER cambio de la
   dirección, también un `replace` que solo quita un parámetro. Por eso `?ver=` se deja hasta cerrar el detalle. Además,
   el enrutador aplica la navegación en una transición: si se abre el panel justo mientras llega una navegación (p. ej.
   la limpieza de `?abrir=1`), el panel se cierra al llegar. Sugerencia: cerrar solo si cambia la ruta (`pathname`), no
   la búsqueda.
4. **Servidor — reemplazo en otra sucursal.** `GetAvailableSerialsQuery` lista las unidades de la sucursal ACTIVA, pero
   `IssueWarrantyReplacementCommand` las saca de la sucursal DEL CASO. Si la sesión tiene activa otra sucursal, la lista
   no sirve (la web ofrece escribir la serie). Sugerido: `GetAvailableSerialsQuery(Sku, WarehouseCode, BranchCode?)` o
   una consulta de unidades para el reemplazo de un caso.
5. **Servidor — `SearchSerialsQuery`.** Busca por serie, IMEI o SKU (no por nombre de producto) y no filtra por sucursal
   ni por fechas: sucursal, tipo, garantía y fechas se filtran en la página sobre las N más recientes (hasta 5.000).
   Sugerido: `BranchCode`, `Kind`, `From`/`To` y paginación.
6. **Servidor — `GetWarrantyClaimsQuery`.** Solo filtra por estado; tope fijo de 2.000 casos. Sugerido: sucursal,
   fechas, texto y paginación.
7. **Contrato — códigos de cliente.** `SerialRow` y `WarrantyClaimRow` traen el NOMBRE del cliente, no su código: «Ver las
   ventas del cliente» usa `WarrantyStatusView.customerCode` (detalle) y no puede ofrecerse desde la fila.
8. **Dar destino con caso abierto.** El servidor rechaza dar destino a una unidad en un caso sin cerrar (salvo
   reemplazado); la web no lo sabe de antemano (no hay dato para eso sin duplicar la regla), así que ofrece «Dar destino»
   a toda unidad devuelta o en RMA y muestra el motivo del rechazo dentro de la confirmación.
9. **Ficha del producto.** El escritorio abre la ficha («Ficha»). El paquete no define parámetros de Stock ni de Catálogo;
   no se enlazó. Sugerido: `/panel/stock?q=<SKU>` o una ficha con dirección propia.
10. **Modo mock (`VITE_API_URL=mock`).** El servidor en memoria no atiende series ni garantías: en `npm run dev` con mock
    las dos pantallas muestran el estado de error con «Reintentar». Las pruebas simulan el servidor dentro de cada prueba.
11. **Pruebas ajenas que cambiarán con la integración.** `modules/inicio/InicioPage.test.tsx` › «un rol sin «Actividad» no
    ve sus botones ni su estadística» usa el CAJERO con el registro real y espera «Todavía no hay funciones para su rol»:
    con Series y Garantías el cajero (`inventory.serials.view`, `service.rma.open`) tiene «Consultar una serie», «Abrir un
    caso de garantía» y la estadística «Unidades por estado». Hay que ajustarla al integrar (no la toqué).
12. **Registro real roto por otros módulos en construcción.** Hoy `registry/registry.test.ts` falla al cargar
    `modules/siat/module.tsx` (`./SiatStatusStat` no existe) y antes `modules/toma-fisica/module.tsx`
    (`./PhysicalCountPage`). Por eso mis pruebas de pantalla arman un registro propio con `buildRegistry([...])` (como
    Clientes); ahí mis dos módulos se cargan sin problemas.

## Pruebas

Desde `src/3. Presentation/MINV.WebCatalog`:

- `npx vitest run src/4-presentation/panel/modules/series src/4-presentation/panel/modules/garantias` →
  **Test Files 4 passed (4) · Tests 66 passed (66)** (≈ 9 s), sin avisos en la salida; repetido 3 veces seguidas junto con
  `src/architecture.test.ts` (82 pruebas) y una más sola, siempre en verde.
  - `series/serials.test.ts` (18): pedido exacto (sin filtro, con filtros, valores raros), estados del filtro, garantía
    frente a hoy, filtros de la página (fechas de La Paz), sucursales con nombre, productos (catálogo y respaldo),
    lectura de series e IMEI (Luhn, repetidas, espacios, largo), recuadro de garantía, línea de tiempo, origen, caso
    abierto, reglas de los botones, enlaces codificados, motivo, CSV y barras.
  - `series/SerialsPage.test.tsx` (18): lista con pedidos exactos y resumen plegado que consulta recién al abrirse;
    estado, búsqueda, producto y cantidad al servidor y en la dirección; sucursal, tipo y garantía en la página sin
    volver a consultar; error con «Reintentar»; CSV; detalle (pedidos exactos, garantía, «Abrir caso de garantía», «Ver
    las ventas…», línea de tiempo, casos, «Dar destino», «Ver solo…»); `?ver=` (queda abierto y se quita al cerrar);
    registrar series (validación, IMEI mal escrito, comando exacto, aviso, recarga; error del servidor en el diálogo);
    dar destino (motivo obligatorio, confirmación, comando exacto, recarga; rechazo del servidor en la confirmación);
    «Consultar una serie» por `?consultar=1` (validación, serie inexistente, resultado, trazabilidad); roles (CONSULTA
    sin registrar/destino/abrir casos, VENTAS abre casos sin registrar ni dar destino); tablero y estadística (datos y
    error con «Reintentar»).
  - `garantias/claims.test.ts` (11): pedido por estado, filtros de la página, pasos solo de `nextStatuses`, qué pide
    cada paso, textos de la bitácora, garantía, venta y tiempo, avisos al abrir, resumen, CSV, opciones de clientes y de
    reemplazo, enlaces.
  - `garantias/ClaimsPage.test.tsx` (19): abiertos al entrar con el pedido exacto y resumen plegado; estado al servidor y
    en la dirección; filtros de la página; error con «Reintentar»; detalle con SOLO los pasos del servidor, bitácora y
    enlaces; enviar al proveedor (proveedores, comando exacto, recarga de lista y detalle); marcar reparado (resolución
    obligatoria, «Otra»); rechazo del servidor dentro de la confirmación; reemplazar (unidades sin la del caso, comando
    exacto); nota (VENTAS sin pasos, comando exacto); `?ver=`; caso entregado sin pasos ni notas; abrir desde Series
    (consulta con SKU, falla obligatoria, «Reparación con cargo» obligatoria, comando exacto, detalle del caso nuevo);
    cliente de otra sucursal (lista de clientes, comando exacto); rechazo del servidor al abrir; orden de servicio
    (contenido, área imprimible, `window.print`); CONSULTA sin abrir casos ni pasos ni notas; botones del tablero por rol.
- `npx vitest run src/architecture.test.ts` → **16 passed (16)** (importaciones permitidas, sin tipos del servidor
  declarados a mano, sin red ni almacenamiento, sin HTML inyectado).
- `npx tsc -b --noEmit` → **sin errores en `series/` ni `garantias/`**. Quedan 2 errores de OTRO módulo en construcción:
  `modules/siat/module.tsx` (no encuentra sus pantallas); en corridas anteriores, de `catalogo/`, `compras/` y
  `documentos-fiscales/`.
- `npx oxlint src/4-presentation/panel/modules/series src/4-presentation/panel/modules/garantias` → sin avisos ni
  errores (código de salida 0).
- `npx vitest run src/4-presentation/panel/registry` → falla SOLO por `modules/siat/module.tsx` (otro módulo, ver
  Pendientes 12).
- `npm run build` no se ejecutó (lo hace la integración final, según las reglas del paquete).
