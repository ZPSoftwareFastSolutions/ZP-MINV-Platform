# Informe M7 · módulos «Órdenes de compra», «Proveedores» (Compras), «Sucursales» y «Transferencias» (Sucursales) del panel web (tarea 11 de la V7)

Carpetas: `src/3. Presentation/MINV.WebCatalog/src/4-presentation/panel/modules/compras/`, `…/proveedores/`,
`…/sucursales/` y `…/transferencias/` (46 archivos). No se tocó nada fuera de esas cuatro carpetas (ni `shell/`, `kit/`,
`hooks/`, `lib/`, registro, contrato, otros módulos ni `package.json`), salvo este informe. Sin commits (el coordinador
guardó el avance parcial de `compras/purchasing.ts` en el wip `a47202f` cuando el límite de uso cortó la sesión).

Claves: `compras`, `proveedores`, `sucursales` y `transferencias` (las del paquete M7; la tabla §3.4 de la guía sugería
`ordenes-compra` para las órdenes, prevalece el paquete: `modules/pedido` ya enlaza a `/panel/compras`). Secciones y
orden: Compras 20 y 30, Sucursales 10 y 20.

## Resumen

Las cuatro pantallas siguen el patrón del panel: título y descripción, botones de acción arriba, barra de filtros con
listas desplegables + búsqueda (+ fechas donde aplica) + «Limpiar filtros», filtros/orden/página en la dirección
(`useTableState`), tabla ordenable y paginada (tarjetas por debajo de 640 px) con acciones por fila («⋯»), detalle en
panel lateral, formularios en diálogos con validación por campo y el mensaje del servidor DENTRO del diálogo,
confirmación antes de todo lo irreversible (anular, desactivar, despachar), exportar CSV (en el orden de la tabla),
estados de carga, vacío y error con «Reintentar», aviso de éxito tras cada comando, y estadísticas SOLO plegadas
(`Collapsible` «Ver …» cerrado al entrar que no consulta nada hasta abrirse, y `stats` del tablero). Cada botón y acción
se ofrece solo a quien puede ejecutar su comando (`canRun` con los permisos de `RPC_OPERATIONS`); el servidor decide igual
(P-01). El clic en una opción del menú «⋯» NO abre el detalle de la fila: cada módulo tiene su guarda (`rowGuard.ts`,
ver «Pendientes» 1).

### Compras › Órdenes de compra (`/panel/compras`, `purchasing.manage` + `inventory.stock.view`)

Dos pestañas para no mezclar: **Órdenes** y **Facturas de proveedores** (`?pestana=facturas`).

| Escritorio (PurchaseOrdersView + PurchasingViewModels) | Web |
|---|---|
| Combos de estado y proveedor | Listas desplegables: **Estado** (Borrador, Aprobada, Recibida en parte, Recibida, Anulada y además «Por recibir» y «Abiertas»), **Proveedor** (directorio + los de las órdenes; `?proveedor=<código>` desde Proveedores y el pedido sugerido), **Sucursal** (del número `OC-CM-…`, ver «Pendientes» 3), **Fecha de la orden** (rango) y búsqueda (número, proveedor, notas) |
| Lista: orden, proveedor, entrega, estado, total, recibido | Tabla: Orden, Proveedor, Fecha, Entrega (con «Vencida» si está por recibir y pasó la fecha), Estado, Líneas, Total (con total al pie), Recibido % |
| 4 tarjetas a la vista (borradores, por recibir, recibido del mes, proveedores) | Plegadas en «Ver resumen de las compras» y como estadística del tablero «Compras en curso» |
| Detalle con líneas y recepciones | Panel lateral: datos, aviso de entrega vencida, líneas con pedido/recibido/**pendiente**, recepciones con «Factura del proveedor» (→ pestaña de facturas filtrada) y botones Recibir / Aprobar / Ver solo este proveedor / Ver el proveedor (→ Proveedores) / Anular |
| «Nueva orden» (editor con proveedor, entrega, notas, productos con cantidad y costo, «Solo productos de este proveedor») | Diálogo «Nueva orden de compra»: proveedor (solo activos), entrega esperada (opcional: sin fecha, el servidor usa el plazo del proveedor, que se muestra), notas (250), producto con búsqueda (`GetCatalogQuery`) y el interruptor «Solo productos de este proveedor»; cantidad propuesta (máx. − mín., entera si la unidad no admite decimales) y costo del catálogo editables, subtotal y total. Al crearla se abre su detalle. `?nueva=1` (tablero) y `?nueva=1&proveedor=P002` (Proveedores) la abren con el proveedor elegido |
| «Desde el pedido sugerido» (confirmación) | Igual (`CreateSuggestedPurchaseOrdersCommand` con confirmación y el enlace «Revisar antes el pedido sugerido» → `/panel/pedido`); al terminar muestra los borradores |
| Aprobar (confirmación) | Igual, con confirmación |
| Recibir: documento del proveedor y series de los serializados | Diálogo «Recibir …»: lo que entra (pendiente × costo y valor), «Factura o remisión del proveedor» (40) y, por cada producto serializado pendiente (`SearchTechProductsQuery`), un campo de series o IMEI (una por unidad; cantidad exacta, sin repetidas, IMEI de 15 dígitos). Recibe TODO lo pendiente (ver «Pendientes» 2) |
| Anular (confirmación) | Igual, con confirmación; solo borradores y aprobadas |
| Exportar | Exportar CSV |
| Facturas de proveedores (en Facturación › Libros del escritorio) | Pestaña **Facturas de proveedores**: «Recepciones sin factura» (`GetReceiptsWithoutInvoiceQuery`) con «Registrar factura» y «Facturas registradas» (`GetSupplierInvoicesQuery`, por defecto el mes en curso; con importe y crédito fiscal y totales), filtros de proveedor, búsqueda (factura, recepción, NIT, CUF) y fechas, dos CSV. Diálogo «Factura del proveedor»: número (40), CUF o autorización (100), fecha (no posterior a hoy), importe total (se PROPONE recepción ÷ 0,87, como el escritorio), descuentos, no sujeto a crédito fiscal, tipo de compra (1 a 5) y código de control (17); el crédito fiscal y el asiento los calcula el servidor y su mensaje se muestra al terminar |

Acciones del tablero: **«Nueva orden de compra»** (`?nueva=1`) y **«Recibir mercadería»** (`?estado=por-recibir`, solo
con `inventory.movements.register.warehouse`, el permiso extra de la recepción). Estadística: **«Compras en curso»**.

### Compras › Proveedores (`/panel/proveedores`, `purchasing.manage` + `inventory.stock.view`)

| Escritorio (SuppliersView) | Web |
|---|---|
| Búsqueda por nombre o contacto | Búsqueda por nombre, código, NIT, contacto, teléfono o correo (`?q=P002` desde Órdenes de compra) y listas desplegables: Estado, Órdenes abiertas (con/sin) y Plazo de entrega (hasta 3 días, 4 a 7, más de 7) |
| Tabla: proveedor, entrega, productos, órdenes abiertas, comprado | Igual + NIT, contacto y estado; totales al pie |
| 4 tarjetas a la vista | Plegadas en «Ver resumen de los proveedores» (activos, órdenes abiertas, comprado con el principal, entrega promedio) |
| «Órdenes de compra» | Detalle lateral con **«Ver sus órdenes de compra»** (`/panel/compras?proveedor=<código>`) y **«Nueva orden de compra»** (`/panel/compras?nueva=1&proveedor=<código>`); también en el menú de la fila |
| Editor: razón social, NIT, días de entrega (combo), contacto, teléfono, correo, activo | Diálogo con lo mismo; días de entrega en lista desplegable con los plazos habituales y «Otro plazo…» (0 a 365); correo validado; largos de la base (150/30/120/40/254) |
| — | Activar (directo) y Desactivar (con confirmación), con los mismos datos del proveedor |

`?nuevo=1` abre «Nuevo proveedor». Exportar CSV.

### Sucursales › Sucursales (`/panel/sucursales`; ver: `inventory.stock.view` + uno de `reports.view` / `corporate.branches.all` / `corporate.branches.manage`)

Dos pestañas: **Sucursales** y **Stock consolidado** (`?pestana=stock`).

| Escritorio (BranchesView + BranchesViewModel) | Web |
|---|---|
| Tarjetas por sucursal (ventas del período, ticket, stock, almacén, usuarios, transferencias) | Tabla del directorio (`GetBranchesQuery`): Sucursal («No es una de sus sucursales» si no está en el alcance), Almacenes, Usuarios, Stock valorizado («Sin acceso» fuera del alcance), Salen, Llegan, Estado; filtros Estado, Acceso (mías/otras) y búsqueda (código, nombre, almacén). Detalle lateral con «Transferencias que salen/llegan» (→ Transferencias `?origen=` / `?destino=`), «Usuarios de la sucursal» y «Editar» |
| Período de ventas + tarjetas + ventas por sucursal | Plegado en **«Ver comparativo por sucursal»** (`GetBranchReportQuery`, solo con `reports.view`): rango (por defecto los últimos 30 días; atajos), ventas, stock y en tránsito, barras de ventas por sucursal, tabla (tickets, ventas, IVA, ticket promedio, participación, stock) con totales, **de cuándo son los datos** (modelo de lectura, regla B-14), «Ver lo que está en tránsito» (→ `/panel/transferencias?estado=Dispatched`) y CSV |
| Stock consolidado con búsqueda | Pestaña **Stock consolidado** (`ConsolidatedStockQuery`, búsqueda en el servidor): una columna por sucursal visible, en tránsito (contado una vez), total y valor; filtros Categoría y «En tránsito»; «Ver valor por sucursal» plegado; CSV |
| Nueva sucursal (código, nombre, almacén propuesto, «Crear también su caja») | Diálogo igual: se proponen «ALM» + código y «Almacén » + nombre mientras no se escriban a mano; valida códigos (letras y números, 12), nombres (100) y repetidos |
| — (el escritorio no edita) | **Editar** (`UpdateBranchCommand`: nombre y activa) y **Activar / Desactivar** (con confirmación; no deja desactivar la única activa) |
| «Asignar usuarios» (persona → sucursales con casillas) | Igual («Asignar sucursales a un usuario») y además **«Usuarios de la sucursal»** (por sucursal, una casilla por persona del personal activo): a cada persona que cambia se le envía la lista completa de sus sucursales; nadie puede quedar sin sucursales. Solo con `corporate.branches.manage` + `iam.users.manage` (lo exige `GetUsersQuery`) |

### Sucursales › Transferencias (`/panel/transferencias`, `inventory.transfers.manage` + `inventory.stock.view`; licencia `MULTI_BRANCH`)

| Escritorio (TransfersView + TransfersViewModel) | Web |
|---|---|
| Combo de estado | Listas desplegables: **Estado** (Pendiente, Despachada, Recibida, Anulada), **Origen**, **Destino**, **Pendiente en mis sucursales** (por despachar / por recibir, con los `canDispatch`/`canReceive` que calcula el servidor), cuántas revisar (300/1.000/2.000), **Fecha de la solicitud** y búsqueda (número, sucursal, almacén, notas) |
| Lista: número, ruta, estado, valor | Tabla: Número, Ruta (`CM → CB` con almacenes y destino), Estado (+ «Por despachar aquí» / «Por recibir aquí»), Solicitada, Productos, Cantidad, Valor (— si pendiente; total al pie), Faltante |
| 4 tarjetas a la vista | Plegadas en «Ver resumen de las transferencias» y como estadística del tablero «Transferencias en curso» |
| Detalle con líneas, series y bitácora | Panel lateral: ruta, almacenes, fechas, valor al costo del origen, faltante, notas, líneas (despachado, recibido, faltante y su motivo, costo, lotes, series) y la **Bitácora**; botones según el lado (regla B-03) |
| Nueva transferencia: sale de (almacén de su sucursal), llega a, productos, series | Diálogo: **Sale de** = almacenes de la sucursal ACTIVA (en la vista de todas, las visibles), **Llega a** en lista desplegable (otras sucursales activas), productos con búsqueda y su stock (`SearchTechProductsQuery`), cantidades y, en los serializados, las series que viajan: casillas de las disponibles en el almacén de origen (`GetAvailableSerialsQuery`) o, sin ese permiso, escritas una por renglón; notas (250). Al solicitarla se abre su detalle |
| Despachar (confirmación) | Confirmación con lo que sale y las series que viajan (`GetTransferQuery`); el error del servidor (p. ej. stock insuficiente) queda dentro |
| Recibir: lo que llegó y el motivo del faltante; series que no llegaron | Diálogo: «Llegó» por producto (por defecto todo; nunca más de lo despachado), motivo del faltante en lista desplegable u «Otro motivo…», y las unidades que NO llegaron marcadas entre las series que viajan |
| Anular con motivo sugerido | Confirmación con motivo en lista desplegable u «Otro motivo…» (200) |

Acciones del tablero: **«Nueva transferencia»** (`?nueva=1`) y **«Recibir transferencia»** (`?pendiente=recibir`).
Estadística: **«Transferencias en curso»**. Exportar CSV.

## Rutas y parámetros

| Ruta | Parámetros (en la dirección) |
|---|---|
| `/panel/compras` | Filtros `q`, `estado` (`Draft`, `Approved`, `PartiallyReceived`, `Received`, `Cancelled`, `por-recibir`, `abiertas`), `proveedor` (código), `sucursal` (código), `desde`, `hasta`; **`nueva=1`** abre la orden nueva (con `proveedor` elegido si viene) y se quita; `pestana=facturas` con `f_q`, `f_proveedor`, `f_desde`, `f_hasta` (por defecto el mes en curso) y `r_*` (orden/página de las recepciones) |
| `/panel/proveedores` | `q` (también el código), `estado` (`activo`, `inactivo`), `ordenes` (`con`, `sin`), `entrega` (`rapido`, `semana`, `mas`); `nuevo=1` abre «Nuevo proveedor» |
| `/panel/sucursales` | `q`, `estado` (`activa`, `inactiva`), `acceso` (`mias`, `otras`); `pestana=stock` con `s_q` (búsqueda del servidor), `s_categoria`, `s_transito` (`con`, `sin`) |
| `/panel/transferencias` | `q`, `estado` (`Pending`, `Dispatched`, `Received`, `Cancelled`), `origen`, `destino` (códigos de sucursal), `pendiente` (`despachar`, `recibir`), `desde`, `hasta`, `registros` (300/1000/2000); `nueva=1` abre la transferencia nueva |

Enlaces entre módulos (solo direcciones, P-09): Proveedores → `/panel/compras?proveedor=<código>` y
`/panel/compras?nueva=1&proveedor=<código>`; Órdenes → `/panel/proveedores?q=<código>` y `/panel/pedido`; Sucursales →
`/panel/transferencias?origen=<código>`, `?destino=<código>` y `?estado=Dispatched`. Lo que ya usaba otro paquete:
`/panel/compras?proveedor=<código>` (M5, pedido sugerido) funciona.

## Casos de uso usados

Todos por `useRpcQuery` / `useRpcCommand`, con los tipos derivados del contrato y TODOS los parámetros:

| Operación | Dónde | Pedido que se envía |
|---|---|---|
| `GetPurchaseOrdersQuery` | Órdenes, estadística | `{ status: null }` |
| `GetPurchaseOrderQuery` | Detalle, recepción | `{ id }` |
| `GetSuppliersQuery` | Órdenes (filtro y orden nueva), Proveedores | `{}` |
| `GetCatalogQuery` / `GetCatalogOptionsQuery` | Orden nueva (productos; decimales de la unidad) | `{ categoryCode: null, specFilters: null }` / `{}` |
| `SearchTechProductsQuery` | Recepción (¿lleva serie?), transferencia nueva (productos, stock, serie) | `{ text: null, categoryCode: null, platform: null, filters: null, onlyInStock: false, max: 2000 }` |
| `CreatePurchaseOrderCommand` | Orden nueva | `{ supplierCode, expectedDate: 'AAAA-MM-DD' \| null, notes \| null, lines: [{ sku, quantity, unitCost }] }` |
| `CreateSuggestedPurchaseOrdersCommand` | «Desde el pedido sugerido» | `{}` |
| `ApprovePurchaseOrderCommand` / `CancelPurchaseOrderCommand` | Aprobar / Anular | `{ id }` |
| `ReceivePurchaseOrderCommand` | Recibir | `{ id, supplierDocument \| null, serials: [{ sku, serials }] \| null }` |
| `GetReceiptsWithoutInvoiceQuery` | Facturas | `{}` |
| `GetSupplierInvoicesQuery` | Facturas | `{ from, to }` (las dos fechas siempre; «Todas» = desde 2000-01-01) |
| `RegisterSupplierInvoiceCommand` | Registrar factura | `{ receiptNumber, invoiceNumber, authorizationCode, invoiceDate, totalAmount, discounts, notSubjectToVat, purchaseType, controlCode \| null }` |
| `SaveSupplierCommand` | Nuevo, editar, activar, desactivar | `{ code \| null, name, taxId \| null, leadTimeDays, contactName \| null, phone \| null, email \| null, isActive }` |
| `GetBranchesQuery` | Sucursales, transferencias (almacenes y filtros) | `{}` |
| `GetBranchReportQuery` | Comparativo plegado | `{ from, to }` |
| `ConsolidatedStockQuery` | Stock consolidado | `{ search: null \| '<texto>' }` |
| `CreateBranchCommand` | Nueva sucursal | `{ code, name, warehouseCode, warehouseName, createPosRegister }` |
| `UpdateBranchCommand` | Editar, activar, desactivar | `{ code, name, isActive }` |
| `GetUsersQuery` | Usuarios por sucursal y por usuario | `{}` |
| `AssignUserBranchesCommand` | Ídem (uno por persona que cambia) | `{ email, branchCodes }` (lista completa, en el orden del directorio) |
| `GetTransfersQuery` | Transferencias, estadística | `{ status: null, take: 300 \| 1000 \| 2000 }` |
| `GetTransferQuery` | Detalle, despacho, recepción | `{ id }` |
| `GetAvailableSerialsQuery` | Transferencia nueva (series que viajan) | `{ sku, warehouseCode: '<almacén de origen>' }` |
| `CreateTransferCommand` | Nueva transferencia | `{ toWarehouseCode, lines: [{ sku, quantity, serials \| null }], notes \| null, fromWarehouseCode }` |
| `DispatchTransferCommand` | Despachar | `{ id }` |
| `ReceiveTransferCommand` | Recibir | `{ id, lines: [{ sku, receivedQuantity, shortageReason \| null, missingSerials \| null }] }` (todas las líneas) |
| `CancelTransferCommand` | Anular | `{ id, reason }` |

Permisos confirmados con `RPC_OPERATIONS`: las consultas de órdenes, proveedores, sucursales, catálogo y transferencias
exigen `inventory.stock.view` (por eso los módulos lo piden además del permiso del paquete); `GetSupplierInvoicesQuery`,
`GetReceiptsWithoutInvoiceQuery` y los comandos de compras, `purchasing.manage`; `ReceivePurchaseOrderCommand`, además
`inventory.movements.register.warehouse`; los de sucursales, `corporate.branches.manage` (+ `MULTI_BRANCH`);
`GetBranchReportQuery`, `reports.view` (+ `GLOBAL_AUDIT`); `GetUsersQuery`, `iam.users.manage`; los de transferencias,
`inventory.transfers.manage` (+ `MULTI_BRANCH`); `GetAvailableSerialsQuery`, `inventory.serials.view`.

## Pendientes

1. **Kit — el clic de una opción del menú «⋯» también abre el detalle de la fila** (el aviso de los otros ingenieros,
   ya descrito en «Pendientes» 1 del informe M5). `RowActions` dibuja el menú en un portal y React propaga el clic al
   `<tr>`; `fromInteractive` (`kit/aria.ts`) solo mira el DOM. Resuelto DENTRO de cada uno de mis módulos con
   `rowGuard.ts` (`fromMenu` envuelve cada acción del menú y `guardOpen` la apertura de la fila) y probado en las cuatro
   pantallas («… no abre el detalle»). Arreglo para todos: `event.stopPropagation()` en el `onClick` de cada opción del
   menú, o que `fromInteractive` reconozca `[role="menu"], [role="menuitem"]` aunque no estén dentro de la fila.
2. **Servidor — recepción parcial y lotes.** `ReceivePurchaseOrderCommand(Id, SupplierDocument?, Serials?)` recibe
   SIEMPRE todo lo pendiente (`RegisterReceipt(fullyReceived: true)`) y con el lote por defecto: no hay cantidades por
   línea ni lote/vencimiento. El paquete pedía «parcial o total, con series y lotes». La pantalla lo dice («Entra al stock
   TODO lo pendiente»). Sugerido: `Lines: IReadOnlyList<PurchaseReceiptLineInput>? = null` con `{ LineId, Quantity,
   LotNumber?, ExpiresOn?, Serials? }` (null = todo lo pendiente, como hoy) y que el estado quede `PartiallyReceived`
   cuando corresponda.
3. **Servidor — `PurchaseOrderRow` sin sucursal y `GetPurchaseOrdersQuery` sin filtros.** La sucursal de cada orden se
   saca del número (`OC-CM-000012`, regla B-02); la consulta trae TODAS las órdenes (solo admite `Status`) y proveedor,
   fechas y búsqueda se filtran en la página. Sugerido: `BranchCode` en `PurchaseOrderRow` y `SupplierCode`, `From`,
   `To`, `Take` opcionales en `GetPurchaseOrdersQuery`. Del mismo modo `PurchaseOrderDetail.Receipts` es solo la lista de
   números (sin fecha, total ni si ya tiene factura).
4. **Servidor — despacho «con series».** `DispatchTransferCommand(Id)` no recibe series: se eligen al SOLICITAR
   (`TransferLineInput.Serials`) y el despacho sale con esas (si falta una, pide anular y solicitar de nuevo). La web elige
   las series en «Nueva transferencia» y las muestra en la confirmación del despacho. Si se quiere cambiar series al
   despachar haría falta `DispatchTransferCommand(Id, IReadOnlyList<SkuSerials>? Serials = null)`.
5. **Servidor — `GetTransfersQuery` solo con `Status` y `Take`.** Origen, destino, fechas y búsqueda se filtran en la
   página sobre las últimas N (la pantalla lo dice). Sugerido: `FromBranchCode`, `ToBranchCode`, `From`, `To` opcionales.
6. **Servidor — «Recibir mercadería» para la gerencia.** GERENCIA tiene `purchasing.manage` sin
   `inventory.movements.register.warehouse`: aprueba y anula pero no recibe (la pantalla no se lo ofrece). Es la matriz
   de permisos, no un defecto; lo anoto porque el escritorio tampoco se lo ofrece.
7. **Servidor — asignación de usuarios por sucursal.** No hay un comando «personas de una sucursal»: se envía un
   `AssignUserBranchesCommand` por persona que cambia (en orden); si uno falla, el diálogo dice cuáles se guardaron y
   cuál no. Sugerido: `AssignBranchUsersCommand(string BranchCode, IReadOnlyList<string> Emails)` en una transacción.
   Además `GetUsersQuery` exige `iam.users.manage`: quien solo administra sucursales no ve la opción.
8. **Servidor — `TechProductRow.Stock` es de la sucursal ACTIVA.** En la vista de todas las sucursales (gerencia), el
   stock que se muestra al solicitar una transferencia no es el del almacén de origen elegido. Sugerido: `WarehouseCode`
   opcional en `SearchTechProductsQuery` (como `GetStockProjectionQuery`).
9. **Propuesta de importe de la factura.** Se propone recepción ÷ 0,87 (`FiscalRules.InvoiceForNetCost` del escritorio)
   como valor inicial editable; el crédito fiscal y el asiento solo los calcula el servidor (se muestra su mensaje). Si
   se prefiere no tener esa cuenta en la web, haría falta `ProposedTotal` en `PendingSupplierInvoiceRow`.
10. **Modo mock.** No atiende ninguna de estas operaciones: con `VITE_API_URL=mock` las cuatro pantallas muestran el
    estado de error con «Reintentar». Por eso no hubo recorrido en el navegador; se probaron con el servidor simulado en
    cada prueba (con un registro que tiene solo el módulo probado).
11. **Pruebas ajenas.** En la corrida completa fallan 2 pruebas de `modules/inicio/InicioPage.test.tsx` («con los
    módulos del proyecto»): esperan que el administrador vea en Administración SOLO los botones de «Actividad» y que el
    cajero no tenga «Ver estadísticas»; con los módulos de otros paquetes (usuarios, integraciones, configuración, caja,
    ventas…) eso ya no es así. Ninguna de las dos depende de mis módulos (no ofrecen nada al cajero ni a Administración).
12. **Lib — ayudas repetidas.** `plainMessage`, la guarda del menú, la pestaña en la dirección, `parseSerials` y los
    motivos «uno sugerido u otro» están repetidos en mis cuatro carpetas (y en otros módulos) porque un módulo no importa
    de otro (P-09). Sugerido: llevarlos a `panel/lib` / `panel/hooks`.

## Pruebas

Desde `src/3. Presentation/MINV.WebCatalog` (salida redirigida a archivo, como pide la regla):

- `npx vitest run src/4-presentation/panel/modules/compras src/4-presentation/panel/modules/proveedores src/4-presentation/panel/modules/sucursales src/4-presentation/panel/modules/transferencias`
  → **Test Files 8 passed (8) · Tests 83 passed (83)**:
  - `compras/purchasing.test.ts` 15 y `compras/PurchaseOrdersPage.test.tsx` 15 (lista con entrega vencida y resumen
    plegado; filtros en la dirección y `?proveedor=`; detalle con pendiente y enlaces; «Reintentar»; orden nueva con
    validación y el pedido EXACTO; aprobar desde el menú sin abrir el detalle; anular con el error del servidor dentro;
    recibir con IMEI, validación y error del servidor; desde el pedido sugerido; `?nueva=1&proveedor=`; facturas de
    proveedores con el pedido exacto; gerencia sin «Recibir»; ventas sin acceso; acciones y estadística del tablero).
  - `proveedores/suppliers.test.ts` 5 y `proveedores/SuppliersPage.test.tsx` 10 (lista y resumen plegado; filtros y
    `?q=`; detalle con enlaces a Órdenes; CSV; «Reintentar»; nuevo con validación, error del servidor y pedido exacto;
    editar; desactivar con confirmación sin abrir el detalle y activar; bodega con acciones; cajero sin acceso).
  - `sucursales/branches.test.ts` 7 y `sucursales/BranchesPage.test.tsx` 11 (directorio; comparativo plegado que consulta
    recién al abrirse, con la frescura de los datos; filtros y detalle con enlaces; «Reintentar»; nueva sucursal con
    almacén propuesto y pedido exacto; editar desde el menú; desactivar con error del servidor y activar; usuarios por
    sucursal —nadie sin sucursales— y por usuario; stock consolidado con una columna por sucursal y búsqueda en el
    servidor; consulta sin acciones; cajero sin acceso).
  - `transferencias/transfers.test.ts` 7 y `transferencias/TransfersPage.test.tsx` 13 (lista con el lado de la sesión y
    resumen plegado; filtros y `?pendiente=recibir`, «a revisar» vuelve a consultar; detalle con lotes, series y
    bitácora; «Reintentar»; solicitar con series elegidas y el pedido exacto; despachar con error del servidor sin abrir
    el detalle; recibir con faltantes, motivo y series que no llegaron; anular con motivo; `?nueva=1`; ventas sin
    acceso; acciones del tablero; estadística y su error).
- `npx vitest run src/architecture.test.ts src/4-presentation/panel/registry` → **2 archivos, 30 pruebas, todas pasan**
  (módulos válidos; sin importaciones entre módulos; solo el conjunto del panel; sin tipos del servidor a mano).
- `npx oxlint src/4-presentation/panel/modules/{compras,proveedores,sucursales,transferencias}` → **0 diagnósticos en
  46 archivos**; `npx oxlint` (todo el proyecto) → sin diagnósticos.
- `npx tsc -b --noEmit` → **0 errores** (todo el proyecto).
- `npx vitest run` (todo el proyecto, con los módulos de los demás paquetes tal como estaban al terminar) → **114 de 115
  archivos y 1.466 de 1.468 pruebas pasan**; las 2 que fallan son de `modules/inicio` (ver «Pendientes» 11), ajenas a
  este paquete.
