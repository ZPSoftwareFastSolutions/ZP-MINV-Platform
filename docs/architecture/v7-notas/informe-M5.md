# Informe M5 · módulos «Stock», «Alertas», «Movimientos» (Inventario) y «Pedido sugerido» (Compras) del panel web (tarea 11 de la V7)

Carpetas: `src/3. Presentation/MINV.WebCatalog/src/4-presentation/panel/modules/stock/`, `…/alertas/`, `…/movimientos/` y
`…/pedido/`. No se tocó nada fuera de esas cuatro carpetas (ni `shell/`, `kit/`, `hooks/`, `lib/`, registro, contrato,
otros módulos ni `package.json`), salvo este informe. Sin commits (el coordinador guardó el avance de `stock/` en el wip
`5563b40` cuando el límite de uso cortó la sesión; lo demás se escribió después).

Claves: `stock`, `alertas`, `movimientos` y `pedido` (las del paquete M5; la tabla §3.4 de la guía sugería
`pedido-sugerido`, prevalece el paquete). Secciones y orden de la guía: Inventario 10 / 30 / 50 y Compras 10.

## Resumen

Todas las pantallas siguen el patrón del panel: título y descripción, botones de acción arriba, barra de filtros con
listas desplegables + búsqueda (+ fechas donde aplica) + «Limpiar filtros», filtros/orden/página en la dirección
(`useTableState`), tabla ordenable y paginada con acciones por fila («⋯») y fila desplegable donde hay más datos,
detalle en panel lateral, exportar CSV (en el orden en que se ve la tabla), estados de carga, vacío y error con
«Reintentar», avisos de éxito, confirmación antes de lo irreversible y estadísticas SOLO plegadas (`Collapsible` «Ver …»
cerrado al entrar, que no consulta nada nuevo hasta abrirse). Botones y acciones se muestran solo a quien tiene el
permiso (el servidor decide igual).

### Inventario › Stock (`/panel/stock`, `inventory.stock.view`)

| Escritorio (StockView, ProductDetailView) | Web |
|---|---|
| Stock del almacén de trabajo, con reservado y disponible (V6) | Igual, y además cualquier otra sucursal VISIBLE (filtro «Sucursal» → `warehouseCode`) y «Todas las sucursales (consolidado)» (`ConsolidatedStockQuery`, solo si la sesión ve más de una), con una columna por sucursal, en tránsito, total, valor y totales al pie |
| Chips de estado + combo de categoría + búsqueda | Listas desplegables: Sucursal, Categoría, **Marca**, Semáforo (sin stock, crítico, bajo, normal, exceso, inconsistente, inactivo), **Reservas** (solo con reservas), **Rotación** (sin rotación: la regla de la V1 con los días de la empresa de `GetWorkspaceQuery`) y búsqueda (SKU, nombre, categoría, marca o proveedor) |
| Galería / tabla | Tabla (tarjetas en el teléfono): Producto, Semáforo, Existencias, Reservado, Disponible, Mínimo, Cobertura; la fila se despliega con mín./máx., valor, costo, salidas de 30 días, ranking, último movimiento, proveedor, marca y rotación |
| Tarjetas de resumen a la vista | Plegadas en «Ver resumen del inventario»: valor, productos activos, en alerta, con reservas y valor por categoría (en el consolidado: valor total, en tránsito y valor por sucursal) |
| Ficha con kardex (ventana aparte) | Panel lateral con pestañas: **Ficha** (existencias, disponible, reservado, mín./máx., costo promedio, valor, proveedor, posición principal, códigos de barras, existencias por posición y lote, «Copiar SKU»), **Kardex** (movimientos con saldo, tabla ordenable, «Exportar kardex»), **Reservas** (reservado por posición y lote + «Ver quién reservó» → Reservas) y **Por sucursal** (consulta al abrirla). Se abre también con `?ficha=SKU` desde otros módulos |
| «Registrar entrada/salida» (ficha) y «+» de la fila | «Registrar entrada», «Registrar salida», «Registrar ajuste» (según permiso) → Movimientos con el producto elegido; «Editar en el catálogo» (con `catalog.manage`) |
| Exportar a Excel | Exportar CSV (por almacén o consolidado) |

Estadísticas del tablero (`stats`, plegadas en «Inicio › Ver estadísticas»): **«Valor del inventario»** (valor,
productos con stock, valor por categoría, «Consultar el stock») y **«Productos en alerta»** (sin stock, críticos, bajos,
exceso o inconsistentes, alertas por tipo, «Ver las N alertas»). Acción del tablero: **«Consultar stock»**.

### Inventario › Alertas (`/panel/alertas`, `inventory.stock.view`)

| Escritorio (AlertsView) | Web |
|---|---|
| Tarjetas priorizadas con la acción sugerida | Tabla priorizada (orden del servidor): Prioridad, Producto, Tipo, Existencias (con el mínimo), Faltan, Sugerido, Acción sugerida (la de la V2.1); la fila se despliega con proveedor, máximo y último movimiento |
| Chips por estado | Listas desplegables: Tipo, Categoría, **Proveedor** («Sin proveedor» incluido), **Sucursal** (otra sucursal visible) y búsqueda |
| 4 tarjetas de conteo a la vista | Plegadas en «Ver resumen de alertas» (una tarjeta por tipo con su acción) |
| «Registrar entrada», «Ver ficha», «Ver pedido sugerido» | Detalle lateral + acciones: «Registrar entrada» (sin stock, crítico, bajo) o «Registrar ajuste» (exceso, inconsistente) solo con permiso de bodega, «Ver en el pedido sugerido», «Ver ficha y kardex» (→ Stock), «Ver solo este proveedor» |
| «¡Todo en orden!» | Igual, en el estado vacío |

Acción del tablero: **«Ver alertas»**. Exportar CSV.

### Inventario › Movimientos (`/panel/movimientos`, `inventory.stock.view` + uno de `inventory.movements.register.warehouse` / `.sales`)

| Escritorio (MovementView) | Web |
|---|---|
| Registro en tres pasos (tipo → producto → cantidad) con vista previa | Diálogo «Registrar un movimiento»: **Tipo** (solo los manuales que el rol puede usar, en el orden del escritorio: bodega → entrada, ajustes, saldo inicial; ventas → salida, devolución de cliente, venta), **Producto** (lista con búsqueda por nombre, SKU o código de barras: sirve el escáner), **Posición** (del almacén de trabajo; se abre en la principal o la que más tiene), **Cantidad** (sin decimales si la unidad no los admite), Documento, Observaciones (obligatorias en ajustes y devoluciones) |
| Poka-yoke rojo sangre y botón bloqueado | **Poka-yoke visible**: si la salida dejaría la posición en negativo, el campo «Cantidad» se marca en rojo con el motivo, la vista previa («En CM-A-01: 7 u. disponibles → quedarán −2 u.») se pone en rojo y el botón se deshabilita. La validación que manda es la del servidor |
| Series (T-02) con el diálogo de series | Si el producto lleva serie o IMEI (`GetProductTechQuery`): al entrar se escriben (una por línea, sirve el escáner); al salir se ELIGEN de las disponibles de la sucursal (`GetAvailableSerialsQuery`, con `inventory.serials.view`; si no, se escriben); aviso para venta y devolución (tienen su propio documento) |
| «Registrados en esta sesión» | Lista de últimos movimientos (`GetRecentMovementsQuery`) con filtros: Tipo, Entradas o salidas, Usuario, **Producto**, **Fechas**, cuántos revisar (50–500) y búsqueda; detalle lateral con «Registrar otro movimiento de este producto», «Ver ficha y kardex» y «Ver solo este producto»; exportar CSV |
| Gráfico de tendencia del tablero | Plegado en «Ver tendencia de movimientos» (`GetMovementTrendQuery`, período 7/14/30/90 días): entradas, salidas, movimientos, saldo inicial aparte y barras por día |

Acciones del tablero (solo bodega): **«Registrar entrada»** (`?registrar=ENTRADA`) y **«Registrar ajuste»**
(`?registrar=ajuste`). En la pantalla, además: «Registrar salida» (ventas) y «Registrar movimiento».

### Compras › Pedido sugerido (`/panel/pedido`, ver `inventory.stock.view`; crear órdenes `purchasing.manage`)

| Escritorio (OrderView + «Desde el pedido sugerido» de PurchaseOrdersView) | Web |
|---|---|
| Tarjeta por proveedor con sus líneas, total, contacto y entrega | Dos pestañas: **Productos** (tabla con la cantidad **EDITABLE**, costo, subtotal y total al pie; «Volver a la cantidad sugerida», «No pedir este producto») y **Proveedores** (total, entrega estimada, **órdenes abiertas**, contacto; detalle lateral con sus líneas) |
| — | Filtros: Proveedor, Categoría, Semáforo y búsqueda; «Volver a las cantidades sugeridas» |
| «Copiar» / «Copiar todo» (correo o WhatsApp) | «Copiar pedido» por proveedor (mismo texto del escritorio) |
| «Desde el pedido sugerido» (confirmación) | **«Generar órdenes de compra»** → confirmación (dice qué proveedores se omitirán por tener una orden abierta y avisa si hay cantidades revisadas, que esta opción no usa) → `CreateSuggestedPurchaseOrdersCommand` → aviso con los números creados y enlace **«Ver órdenes de compra» (`/panel/compras`)** |
| — | **«Crear orden de compra»** de UN proveedor con las cantidades revisadas (`CreatePurchaseOrderCommand`): diálogo con las líneas, total estimado, entrega esperada (prellenada) y observación; enlace a `/panel/compras?proveedor=<código>` |
| Total a la vista | Plegado en «Ver resumen del pedido» (total, productos, proveedores y total por proveedor) |

Acción del tablero: **«Generar pedido»** (solo con `purchasing.manage`). Exportar CSV (productos o proveedores).

## Rutas y parámetros

| Ruta | Parámetros (en la dirección) |
|---|---|
| `/panel/stock` | Filtros `q`, `sucursal` (código de almacén o `todas`), `categoria`, `marca` (`_sin-marca` = sin marca), `estado` (`OutOfStock`, `Critical`, `Low`, `Optimal`, `Overstock`, `Inconsistent`, `Inactive`), `reservas=con`, `rotacion=sin`; **`ficha=<SKU>` abre la ficha lateral** (no es filtro) |
| `/panel/alertas` | `q`, `tipo` (`Inconsistent`, `OutOfStock`, `Critical`, `Low`, `Overstock`), `categoria`, `proveedor` (nombre; «Sin proveedor»), `sucursal` (código de almacén) |
| `/panel/movimientos` | `q`, `tipo` (código), `flujo` (`entradas` \| `salidas`), `usuario` (nombre; `_sistema`), `producto` (SKU), `desde`, `hasta`, `registros` (50/100/200/500); **`registrar=<CÓDIGO>` \| `ajuste` \| `1` abre el registro y `sku=<SKU>` elige el producto** (no son filtros; se borran al cerrar) |
| `/panel/pedido` | `q`, `proveedor` (nombre), `categoria`, `estado` (`OutOfStock`, `Critical`, `Low`) |

Enlaces que ofrecen estos módulos: Stock → `/panel/movimientos?registrar=ENTRADA|SALIDA|ajuste|1&sku=…`,
`/panel/alertas`, `/panel/pedido`, `/panel/reservas?q=<SKU>` (con `sales.pcbuild.manage`),
`/panel/catalogo?q=<SKU>` (con `catalog.manage`); Alertas → `/panel/stock?ficha=…`, `/panel/movimientos?registrar=…`,
`/panel/pedido?q=<SKU>`; Movimientos → `/panel/stock?ficha=…`; Pedido → `/panel/stock?ficha=…`, `/panel/compras`,
`/panel/compras?proveedor=<código>` (con `purchasing.manage`).

## Casos de uso usados

Todos por `useRpcQuery` / `useRpcCommand`, con los tipos derivados del contrato y TODOS los parámetros:

| Operación | Dónde | Pedido que se envía |
|---|---|---|
| `GetStockProjectionQuery` | Stock, Alertas, Pedido, 2 estadísticas | `{ warehouseCode: null }` (almacén de trabajo) o `{ warehouseCode: 'CB-01' }` |
| `GetStockReservationsQuery` | Stock | `{ warehouseCode }` (igual que la proyección) |
| `ConsolidatedStockQuery` | Stock (consolidado y pestaña «Por sucursal») | `{ search: null }` / `{ search: '<SKU>' }` |
| `GetBranchesQuery` | Stock, Alertas (filtro «Sucursal»: solo `isVisible` y activas) | `{}` |
| `GetWorkspaceQuery` | Stock (días sin rotación, almacén de trabajo), registro de movimientos | `{}` |
| `SearchTechProductsQuery` | Stock (marca de cada producto) | `{ text: null, categoryCode: null, platform: null, filters: null, onlyInStock: false, max: 2000 }` |
| `GetProductCardQuery` | Ficha (kardex), registro (existencias por posición) | `{ skuOrBarcode, take: 300 }` / `{ skuOrBarcode, take: 1 }` |
| `GetMovementTypesQuery` | Movimientos | `{}` |
| `GetRecentMovementsQuery` | Movimientos | `{ take: 50 \| 100 \| 200 \| 500 }` |
| `GetMovementTrendQuery` | Movimientos (plegado) | `{ days: 7 \| 14 \| 30 \| 90 }` |
| `GetProductLookupQuery` | Registro | `{ includeInactive: false }` |
| `GetBinsQuery` | Registro | `{ warehouseCode: '<almacén de trabajo>' }` |
| `GetProductTechQuery` | Registro (¿lleva serie?) | `{ sku }` |
| `GetAvailableSerialsQuery` | Registro (salidas de productos con serie) | `{ sku, warehouseCode }` |
| `RegisterMovementCommand` | Registro | `{ sku, binCode, movementTypeCode, quantity, businessDate: null, documentReference, notes, lotNumber: null, adjustmentReasonCode: null, serials }` |
| `GetSuppliersQuery` | Pedido (código, activo y órdenes abiertas de cada proveedor) | `{}` |
| `CreateSuggestedPurchaseOrdersCommand` | Pedido («Generar órdenes de compra») | `{}` |
| `CreatePurchaseOrderCommand` | Pedido (orden de un proveedor con cantidades revisadas) | `{ supplierCode, expectedDate, notes, lines: [{ sku, quantity, unitCost }] }` |

Permisos confirmados con `RPC_META`: todas las consultas exigen `inventory.stock.view` (salvo `GetAvailableSerialsQuery`:
`inventory.serials.view`); `RegisterMovementCommand` no declara permisos en el contrato (los decide el dominio del tipo:
bodega o ventas), por eso los tipos se ofrecen según `inventory.movements.register.warehouse` / `.sales`; los dos
comandos de compras exigen `purchasing.manage`.

## Pendientes

1. **Kit — el clic de una acción del menú «⋯» también abre el detalle de la fila.** `RowActions` dibuja el menú en un
   portal y React propaga el clic al `<tr>`; `fromInteractive` (`kit/aria.ts`) solo mira el DOM
   (`container.contains(...)`), así que la fila llama a `onRowOpen` DESPUÉS de la acción (en Stock, «Registrar entrada»
   navegaba y la ficha la pisaba). Resuelto DENTRO de cada módulo con una guarda (`fromMenu`/`pickingAction`). Arreglo
   sugerido para todos: en `fromInteractive`, `return target.closest('[role="menu"], [role="menuitem"]') !== null || …`,
   o `event.stopPropagation()` en el `onClick` de cada opción del menú. Por el mismo mecanismo debería afectar también a
   `modules/actividad` («Ver solo este usuario» cerraría el detalle y la fila lo volvería a abrir); no lo verifiqué ahí.
2. **Lib — semáforo y sucursales repetidos.** Los estados del semáforo (`StockStatusCode` → texto y color), la acción
   sugerida y las opciones de «Sucursal» (sucursales visibles → almacenes) están repetidos en `stock`, `alertas` y
   `pedido` porque un módulo no importa de otro (P-09). Sugerido: `panel/lib/stock.ts` (o `kit`) con `STOCK_STATUSES`,
   `statusPriority` y `warehouseOptions`.
3. **Pruebas — `preloadPanel()` depende de TODOS los módulos.** Importa `PanelRoot` → el descubrimiento de todos los
   `module.tsx`; un módulo de otro paquete a medio hacer (hoy pasó con `caja/module.tsx` sin `PosPage`) rompe la
   transformación de cualquier suite que lo use. Mis pruebas arman su propio registro (`buildRegistry` con solo su
   módulo) y precargan solo `shell/PanelApp` y sus pantallas: no dependen de los demás paquetes.
4. **Servidor — `CreateSuggestedPurchaseOrdersCommand` no recibe cantidades.** Es un comando sin parámetros: recalcula el
   pedido con las cantidades del sistema y omite a los proveedores con una orden abierta. Las cantidades revisadas en la
   pantalla se usan con `CreatePurchaseOrderCommand` (una orden por proveedor, que el servidor NO omite si ya hay una
   abierta: la pantalla lo avisa con `SupplierRow.openOrders`). Sugerido:
   `CreateSuggestedPurchaseOrdersCommand(IReadOnlyList<PurchaseLineInput>? Lines = null)` (null = las sugeridas).
5. **Servidor — quién reservó.** `GetStockReservationsQuery` devuelve solo `{ sku, reserved }` y la ficha solo el
   reservado por posición y lote. Sugerido: `GetStockReservationDetailsQuery(string Sku, string? WarehouseCode = null)`
   (`inventory.stock.view`) → `[{ origin: 'Build' | 'Cart' | 'Pos' | 'SalesOrder', number, channel, holder (solo con
   `sales.pcbuild.manage`, regla S-06), quantity, binCode, lotNumber, reservedUntil }]`. Mientras, la pestaña «Reservas»
   enlaza a `/panel/reservas?q=<SKU>`.
6. **Servidor — `StockRow` sin marca, reservado ni disponible.** La marca se cruza con `SearchTechProductsQuery` (solo
   productos activos, máximo 2000, y es pesada: trae fichas y precios); el disponible se muestra como `max(0,
   existencias − reservado)`, igual que el escritorio (`StockItem.Available`). Sugerido: `Brand`, `Reserved` y
   `Available` en `StockRow` (y `Brand` en `ConsolidatedStockRow`).
7. **Servidor — `GetProductCardQuery` sin almacén.** La ficha y el kardex son SIEMPRE del almacén de trabajo; si la tabla
   muestra otra sucursal, la ficha lo avisa. Sugerido: `WarehouseCode` opcional, como `GetStockProjectionQuery`.
8. **Servidor — `GetRecentMovementsQuery` solo con `Take` (1–500).** Tipo, usuario, producto y fechas se filtran en la
   página sobre los últimos N (la pantalla lo dice: «Los más recientes; los filtros se aplican sobre ellos»). Sugerido:
   `From`, `To`, `Sku`, `TypeCode` y `UserEmail` opcionales (`GetMovementsReportQuery` exige `reports.view`: no sirve a
   bodega ni a ventas).
9. **Servidor — `SuggestedOrderLine` sin categoría, código de proveedor ni decimales de la unidad.** La categoría sale de
   las filas de stock de la MISMA respuesta; el código, de `GetSuppliersQuery` por nombre (`LegalName`, como hace
   `CreateSuggestedPurchaseOrdersHandler`); los decimales los valida el servidor al crear la orden (el campo admite 3).
10. **Modo mock.** No atiende ninguna de estas operaciones (responde «Operación desconocida»): con `VITE_API_URL=mock` las
    cuatro pantallas muestran el estado de error. Para verlas sin servidor habría que agregarlas al servidor en memoria
    (`3-infrastructure/data/mockWeb.ts`). Por eso no hubo recorrido en el navegador: las pantallas se probaron con el
    servidor simulado en cada prueba.
11. **Otros módulos.** Doy por hechos estos parámetros de otros paquetes: `/panel/reservas?q=` (M3),
    `/panel/catalogo?q=` (M6) y `/panel/compras?proveedor=<código>` (M7, que ya lo usa en «Proveedores»). Si un módulo no
    lee `q`, abre sin filtrar.
12. **Registro — fecha y lote.** Como en el escritorio, no se ofrecen la fecha del movimiento (`businessDate: null` =
    hoy) ni el lote (`lotNumber: null` = lote por defecto `SIN-LOTE`, que es el que protege el poka-yoke) ni el motivo de
    ajuste codificado (`adjustmentReasonCode: null`; el motivo va en «Observaciones»).
13. **Typecheck.** `npx tsc -b --noEmit` marca 1 error, en `modules/caja/PosPage.test.tsx` (paquete M1, en
    construcción); ninguno en mis carpetas ni fuera de los módulos.

## Pruebas

Desde `src/3. Presentation/MINV.WebCatalog` (salida redirigida a archivo, como pide la regla):

- `npx vitest run src/4-presentation/panel/modules/stock src/4-presentation/panel/modules/alertas src/4-presentation/panel/modules/movimientos src/4-presentation/panel/modules/pedido`
  → **Test Files 8 passed (8) · Tests 89 passed (89)**:
  - `stock/stock.test.ts` 12 y `stock/StockPage.test.tsx` 16 (lista con reservado/disponible/mínimo/cobertura/semáforo;
    filtros en la dirección; búsqueda; otra sucursal y consolidado con totales; quien ve una sola sucursal no tiene
    consolidado; error con «Reintentar»; ficha con kardex, reservas y por sucursal; `?ficha=`; botones por rol (admin,
    bodega, consulta); acciones de fila sin abrir la ficha; CSV; resumen plegado sin consultas nuevas; las dos
    estadísticas y su error; definición del módulo).
  - `alertas/alerts.test.ts` 6 y `alertas/AlertsPage.test.tsx` 11 (priorizada con acción sugerida; filtros; otra
    sucursal; «¡Todo en orden!» y error; detalle y accesos por rol; ajuste para el exceso; menú sin abrir el detalle;
    CSV; resumen plegado; definición).
  - `movimientos/movements.test.ts` 12 y `movimientos/MovementsPage.test.tsx` 13 (lista y filtros, «a revisar» vuelve a
    consultar; detalle y «registrar otro»; CSV; tendencia plegada que consulta al abrirse; consulta sin acceso; registro
    de una entrada con el contenido EXACTO de `RegisterMovementCommand`; validación por campo y observación obligatoria;
    POKA-YOKE: campo en rojo y botón deshabilitado; error del servidor dentro del diálogo; IMEI escritos al entrar y
    elegidos al salir; definición).
  - `pedido/order.test.ts` 7 y `pedido/OrderPage.test.tsx` 12 (cantidades editables con subtotal y total; filtros;
    sin nada que reponer; generar con confirmación y `{}`; aviso de cantidades revisadas y rechazo del servidor en la
    confirmación; orden de un proveedor con la forma exacta de `CreatePurchaseOrderCommand`; productos sin proveedor;
    «Copiar pedido»; ventas sin botones de compras; CSV; definición).
- `npx vitest run src/4-presentation/panel/registry src/architecture.test.ts` → **2 archivos, 30 pruebas, todas pasan**
  (módulos válidos; sin importaciones entre módulos; solo el conjunto del panel; sin tipos del servidor a mano).
- `npx oxlint src/4-presentation/panel/modules/{stock,alertas,movimientos,pedido}` → **0 diagnósticos en 27 archivos**.
- `npx tsc -b --noEmit` → **0 errores en mis carpetas**; 1 error ajeno en `modules/caja/PosPage.test.tsx` (ver
  «Pendientes» 13).
