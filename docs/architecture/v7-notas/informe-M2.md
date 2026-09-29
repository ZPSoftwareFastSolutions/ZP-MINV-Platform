# Informe M2 · módulos «Ventas» y «Clientes» del panel web (tarea 11 de la V7)

Carpetas: `src/3. Presentation/MINV.WebCatalog/src/4-presentation/panel/modules/ventas/` y `…/modules/clientes/`. No se tocó
nada fuera de esas dos carpetas (ni `shell/`, `kit/`, registro, contrato, otros módulos ni `package.json`), salvo este
informe.

## Resumen

### Ventas › Ventas (`/panel/ventas`, permiso `sales.view`)

Equivale a `SalesView.xaml` + `SalesViewModel` del escritorio, en dos pestañas («Ventas» y «Devoluciones», como el
selector del escritorio).

| Escritorio | Web |
|---|---|
| Período en un combo (7 días por defecto) | Rango de fechas con atajos (Hoy, Ayer, 7 días, Este mes, Mes anterior, Todas) y fechas a mano; **hoy por defecto** (pedido del paquete). Las fechas van al servidor. |
| Búsqueda (factura o cliente), medio de pago, cajero y estado (Todas/Emitidas/Anuladas) | Búsqueda (número de venta, pedido, cliente, código o N° de factura del SIN) + listas desplegables: **sucursal** (si la sesión ve más de una; sale del número `F-CM-…`, regla B-02), **cliente** (lista con búsqueda), cajero, medio de pago, **estado** (vigente / con devolución / anulada) y **factura del SIN** (cada estado del SIN o «Sin factura del SIN»). Todo en la dirección; «Limpiar filtros» vuelve a HOY. |
| 4 tarjetas a la vista (vendido, facturas, ticket promedio, anuladas) | Plegadas en «Ver resumen del período ^» (cerrado al entrar, regla P-10), más «Con devolución». A la vista solo una línea de resumen y los **totales al pie** de la tabla (sin anuladas). |
| Tabla: factura, fecha, cliente·cajero·pago, total, estado + factura del SIN | Igual, más el pedido y la marca «Cliente web» (códigos `WEB-…`). Ordenable, paginada, tarjetas bajo 640 px, menú de acciones por fila. |
| Panel derecho: líneas, factura del SIN («Ver»), motivo de anulación, «Devolución», «Anular esta venta» | Detalle lateral: pedido, sucursal, cliente, cajero; **pago** (medio, total, IVA, reembolsado); **factura del SIN** (N°, estado con su explicación, en línea / fuera de línea, plazo de anulación, CUF); **productos con sus series** (se piden al abrir el detalle); **devoluciones** de la venta con su nota. Botones según el rol: Reimprimir, Enviar por correo, Devolver productos, Ver solo este cliente, Ver el cliente, Anular. |
| Anular: motivo sugerido o escrito; con factura del SIN activa manda a Documentos fiscales | Igual: `ConfirmDialog` con motivo (lista de sugeridos del escritorio o «Otro motivo») y el error del servidor dentro del diálogo. Con factura del SIN activa lo explica y ofrece «Ir a Documentos fiscales» (`/panel/documentos`) a quien puede anular documentos, o nombra el permiso que falta. Una venta con devoluciones muestra por qué no se anula entera. |
| Devolución (`SalesReturnDialog`): lo que queda por línea, cantidades o series que siguen vendidas, motivo, «por falla», medio de reembolso, «Devolver todo»; después envía la nota al SIN | Igual, en un diálogo guiado de 3 pasos: (1) la venta (elegida o escrita: botón «Registrar devolución» y tablero), (2) productos (cantidades o series; las series que ya no están vendidas se ven deshabilitadas), motivo, por falla (se sugiere solo al elegir «Producto con falla»), medio de reembolso (aviso si mueve la caja) y reembolso estimado, (3) **confirmación** (no se puede deshacer). Después envía la nota crédito-débito al SIN (`DispatchFiscalDocumentsCommand`, como el escritorio) y abre «Devoluciones». |
| Reimprimir (en Documentos fiscales: rollo o PDF) | «Reimprimir la factura»: vista de la factura (`GetFiscalPrintModelQuery`, con «SIN VALOR LEGAL», «ANULADA», fuera de línea, series y garantía), **Imprimir** (solo la factura, con la impresora del navegador) y **Descargar PDF** (`RenderFiscalDocumentQuery`, con la entrega registrada como el escritorio). |
| Enviar por correo (en Documentos fiscales) | «Enviar la factura por correo» (`billing.issue`): correo opcional (vacío = el del comprador), validado; error del servidor en el diálogo. Oculto/deshabilitado para documentos que no se entregan. |
| Pestaña Devoluciones: devolución, fecha, venta·cliente·motivo, reembolso, nota y estado | Igual, con búsqueda, sucursal, cliente, **estado de la nota** y fechas; detalle lateral, «Ver la venta» y exportar CSV. |
| Exportar CSV | En las dos pestañas (más columnas que la tabla: IVA, motivo, reembolsado, CUF…). |
| — | Estados de carga, vacío (con «Limpiar filtros» / «Registrar devolución») y error con «Reintentar»; aviso de éxito tras cada comando. |

Tablero: `actions` «Ventas de hoy» (`/panel/ventas`) y «Registrar una devolución» (`/panel/ventas?devolver=1`, solo con
`billing.void` + `sales.pos.operate`); `stats` «Ventas por día (7 días)» (tarjetas + barras por día + mejor día + enlace a
la lista de esos días) y «Medios de pago» (barras por medio, sin anuladas). Ambas consultan recién al abrirse.

### Ventas › Clientes (`/panel/clientes`)

Equivale a `CustomersView.xaml` + `CustomersViewModel`/`CustomerEditor`.

| Escritorio | Web |
|---|---|
| Búsqueda (nombre, código, NIT, correo, teléfono), categoría con cantidades, orden | Búsqueda igual + listas: categoría (con cantidades), **estado** (activo/inactivo), **datos de factura** (con/sin), **origen** (clientes web / de la tienda). Orden por cualquier columna. Todo en la dirección. |
| 4 tarjetas a la vista (activos, con compras, vendido a clientes, mejor cliente) | Plegadas en «Ver resumen de la cartera ^». |
| Tabla: cliente (código, NIT), contacto, categoría, compras, total, última compra, editar | Igual + documento con su tipo («CI 4455667-1A», «NIT …», o «Sin tipo de documento»), estado y la marca **«Cliente web»**; totales al pie; menú por fila: Ver detalle, **Ver sus ventas**, Editar, Activar / Desactivar. |
| Editor lateral: nombre, NIT/CI, tipo de documento (+ complemento con CI), verificar NIT en el Padrón, categoría, correo, teléfono, activo | Diálogo de alta y edición con lo mismo, validación por campo con las reglas del SIN (CI y NIT solo dígitos, hasta 20; complemento solo con CI, hasta 5, en mayúsculas; correo; largos del servidor; el consumidor final no se desactiva) y el error del servidor dentro del diálogo. «Verificar el NIT en el Padrón» (`VerifyNitCommand`) con el resultado en línea. |
| — | Detalle lateral con los datos de factura, compras y «Ver sus ventas» (`/panel/ventas?cliente=<código>`, que muestra TODAS sus ventas). Desactivar pide confirmación; activar es directo. |

Visibilidad: `{ all: ['inventory.stock.view'], any: ['sales.customers.manage', 'sales.view'] }` — lo ve quien gestiona
clientes o quien ve ventas («ver con `sales.view`» del paquete), y exige además `inventory.stock.view`, que es lo que el
servidor pide para leer los clientes (`GetCustomersQuery` y `GetCustomerFiscalIdentitiesQuery`): así nadie abre una lista
que el servidor le rechazaría. Alta, edición y activar/desactivar solo con `sales.customers.manage`. Tablero: `actions`
«Nuevo cliente» (`/panel/clientes?nuevo=1`, con `sales.customers.manage`).

Decisión respecto del escritorio: al **editar**, se guardan primero los datos de factura y después el cliente (el
escritorio lo hace al revés); así un cambio de tipo de documento (de CI a pasaporte, por ejemplo) no choca con la regla
del tipo anterior que `Customer.Update` vuelve a validar. Al **crear**, primero el cliente (el servidor da el código) y
después sus datos de factura; si estos fallan, el diálogo recuerda el código y el reintento corrige ESE cliente en vez de
crear otro.

Si la empresa no tiene la facturación SIAT (`GetBillingAccessQuery.moduleActive = false`), ambos módulos ocultan lo del
SIN (columna y filtro de la factura, reimprimir y correo; tipo de documento, complemento y verificación del NIT), como el
escritorio con `HasBillingModule`.

## Rutas y parámetros

- `/panel/ventas` (única pantalla del módulo `ventas`).
  - Filtros (en la dirección, `useTableState`): `q`, `desde`, `hasta` (hoy por defecto; `desde=&hasta=` = todas),
    `sucursal` (código), `cliente` (código), `cajero`, `pago`, `estado` (`vigente` · `devolucion` · `anulada`), `fiscal`
    (`Valid` · `Pending` · `Offline` · `InPackage` · `NoResponse` · `Rejected` · `PackageRejected` · `DuplicateToVoid` ·
    `Voided` · `Discarded` · `SinFactura`), `nota` (los mismos estados · `SinNota`).
  - Tablas: `orden`, `sentido`, `pagina`, `filas` (ventas) y `d_orden`, `d_sentido`, `d_pagina`, `d_filas` (devoluciones).
  - `vista=devoluciones` (pestaña).
  - **`cliente=<código>`** (lo pide el paquete; lo usa «Ver sus ventas»): si llega SIN fechas, se muestran todas las
    ventas del cliente y la dirección queda `?cliente=X&desde=&hasta=`.
  - `devolver=1` abre «Registrar una devolución» pidiendo el número; `devolver=<número>` abre la devolución de esa venta.
    El parámetro se quita de la dirección al abrir.
- `/panel/clientes` (única pantalla del módulo `clientes`): `q`, `categoria`, `estado` (`activo` · `inactivo`),
  `factura` (`con` · `sin`), `origen` (`web` · `tienda`), `orden`, `sentido`, `pagina`, `filas`; `nuevo=1` abre «Nuevo
  cliente» (se quita de la dirección).
- Enlaces a otros módulos (solo rutas): `/panel/caja` («Nueva venta», con `sales.pos.operate`), `/panel/documentos`
  (anulación ante el SIN), `/panel/clientes?q=<código>` («Ver el cliente») y `/panel/ventas?cliente=<código>`.

## Casos de uso usados

| Operación | Para qué | Permisos (contrato) |
|---|---|---|
| `GetSalesQuery` `{ from, to }` | ventas del rango; también las dos estadísticas (últimos 7 días) | `sales.view` |
| `GetSalesFiscalStatusQuery` `{ from, to }` | factura del SIN vigente de cada venta | `sales.view` |
| `GetSalesReturnsQuery` `{ from, to: max(to, hoy) }` | devoluciones: marca «con devolución» y pestaña | `sales.view` |
| `GetSaleLinesQuery` `{ invoiceNumber }` | productos y series (detalle y devolución) | `sales.view` |
| `GetReturnableLinesQuery` `{ invoiceNumber }` | lo que queda por devolver | `sales.view` |
| `CreateSalesReturnCommand` `{ invoiceNumber, reason, refundPaymentMethodCode, lines: [{ sku, quantity, serials }], defective }` | devolución (normal o por falla, con series) | `billing.void` + `sales.pos.operate` |
| `VoidSaleCommand` `{ invoiceNumber, reason }` | anular | `sales.pos.operate` + `sales.view` |
| `GetFiscalPrintModelQuery` `{ documentId }` | reimprimir (vista e impresión) | `billing.view` |
| `RenderFiscalDocumentQuery` `{ documentId, format: 'Pdf', columns: 48 }` | reimprimir (PDF) | `billing.view` |
| `SendFiscalDocumentEmailCommand` `{ documentId, email }` | reenviar la factura | `billing.issue` (+ módulo FISCAL_SIAT) |
| `GetCustomersQuery` `{}` | clientes y categorías | `inventory.stock.view` |
| `SaveCustomerCommand` `{ code, name, taxId, email, phone, categoryCode, isActive }` | alta, edición, activar/desactivar | `sales.customers.manage` |
| `GetCustomerFiscalIdentitiesQuery` `{}` | tipo de documento y complemento | `inventory.stock.view` |
| `SaveCustomerFiscalIdentityCommand` `{ code, documentType, documentNumber, complement }` | datos de factura | `sales.customers.manage` |

Además, porque el escritorio las usa en esas mismas pantallas:

| Operación | Para qué | Permisos |
|---|---|---|
| `GetBillingAccessQuery` `{}` | ¿la empresa factura con el SIN? (la sesión web no trae la licencia) | sesión |
| `GetPosStateQuery` `{}` | medios de reembolso de la devolución | `sales.pos.operate` |
| `SearchSerialsQuery` `{ text: null, status: 'Sold', sku, max: 2000 }` | series de la venta que siguen vendidas (si falta el permiso, se ofrecen todas y el servidor valida) | `inventory.serials.view` |
| `DispatchFiscalDocumentsCommand` `{ documentId, max: 50 }` | enviar al SIN la nota de la devolución (si falla, la envía el trabajo automático) | `billing.issue` |
| `RecordFiscalDeliveryCommand` `{ documentId, channel: 'Pdf', recipient: null }` | evidencia de la entrega en PDF | `billing.view` |
| `VerifyNitCommand` `{ nit, customerCode }` | verificar el NIT del cliente en el Padrón | `billing.issue` (+ FISCAL_SIAT) |

Todos los tipos salen del contrato generado por el nombre de la operación (`RpcRequestOf`/`RpcResponseOf`); los botones
se muestran con `canRun(operación)`.

## Pendientes

1. **Kit — área imprimible.** No hay una forma común de imprimir solo una parte de la página. Está resuelto DENTRO del
   módulo (`ventas/PrintArea.tsx`: el contenido va al final del `<body>`, oculto en pantalla y visible solo al imprimir;
   una clase de Tailwind en el `<body>` — `print:[&>*:not([data-imprimible])]:hidden` — oculta lo demás al imprimir;
   verifiqué con el compilador de Tailwind 4.3.3 que genera la regla `@media print`). En papel usa negro sobre blanco
   (los tokens del tema oscuro no sirven impresos). Sugerencia: llevarlo a `kit/` para Caja (M1) y Documentos (M8).
2. **Kit — consulta a pedido.** `useRpcQuery` es declarativa; para «Descargar PDF» (una consulta que se pide al pulsar un
   botón) usé `useRpcCommand('RenderFiscalDocumentQuery')`. Es correcto (el servidor no guarda idempotencia de consultas),
   pero convendría un `useRpcFetch`/`run` para consultas.
3. **Servidor — `GetSalesQuery` sin límites ni filtros.** Solo recibe fechas: sucursal, cajero, cliente, medio y estado se
   filtran en la página, y «Todas» (desde `2000-01-01`) trae todas las ventas de la historia. Sugerido: paginación o `take`
   y filtros opcionales (`branchCode`, `customerCode`, `status`).
4. **Contrato — datos que faltan en las filas.** `SaleRow` no trae la sucursal (se deduce del número `F-XX-…`) ni el correo
   del comprador (el diálogo de correo no puede mostrarlo; vacío = el del comprador lo resuelve el servidor).
   `SalesReturnRow` no trae el código del cliente ni la sucursal: el filtro «Cliente» de la pestaña Devoluciones usa las
   ventas cargadas y el nombre.
5. **Enlace con Documentos (M8).** Para anular ante el SIN solo se enlaza `/panel/documentos` sin parámetros (el paquete no
   define otros); el diálogo dice el N° de factura a buscar. Sugerido: que Documentos acepte `?documento=<id>` o `?q=<N°>`
   para abrir la factura directo (el escritorio navega con `FiscalDocumentFocus(documentId, StartVoid: true)`).
6. **Servidor — permisos de lectura de clientes.** `GetCustomersQuery` y `GetCustomerFiscalIdentitiesQuery` piden
   `inventory.stock.view`, no `sales.view`/`sales.customers.manage`; el módulo lo exige también. Si se quiere que un rol
   de ventas sin «consultar existencias» vea clientes, hay que cambiar el permiso en el servidor.
7. **Tipos de documento del SIN.** Se usa la lista fija de los 5 del XSD (1 CI, 2 CEX, 3 PAS, 4 OD, 5 NIT), la misma que
   usa el escritorio cuando no hay catálogo y la única que acepta el dominio (1–5). El catálogo sincronizado
   (`GetSiatCatalogQuery`) exige `billing.view`, que quien gestiona clientes puede no tener.
8. **Licencia en la sesión web.** Como la sesión no informa los módulos comerciales, se usa `GetBillingAccessQuery`. Mientras
   responde se asume que la empresa factura, así que `GetSalesFiscalStatusQuery` / `GetCustomerFiscalIdentitiesQuery`
   pueden pedirse una vez aunque el módulo no esté activo (inofensivo).
9. **Modo mock (`VITE_API_URL=mock`).** El servidor en memoria no atiende ventas ni clientes: en `npm run dev` con mock las
   dos pantallas muestran el estado de error con «Reintentar». Las pruebas simulan el servidor dentro de cada prueba.
10. **Pruebas ajenas que cambiarán con la integración.** `modules/inicio/InicioPage.test.tsx` › «un rol sin «Actividad» no
    ve sus botones ni su estadística» usa el CAJERO y espera «Todavía no hay funciones para su rol»: con Ventas y Clientes
    (y Caja) registrados, el cajero tiene botones y estadísticas. Hay que ajustarla al integrar (no la toqué).
11. **Registro real roto por otro módulo.** `modules/armador/module.tsx` importa `./BuilderPage`, que todavía no existe:
    rompe `PANEL_REGISTRY` (y `preloadPanel`). Por eso mis pruebas de pantalla arman un registro propio con
    `buildRegistry([...])` (como `InicioPage.test.tsx`) en vez de usar el descubrimiento.
12. **Impresión y evidencia.** Solo la descarga del PDF se registra como entrega (`RecordFiscalDeliveryCommand`); la
    impresión del navegador no avisa si se imprimió de verdad, así que no se registra.

## Pruebas

Desde `src/3. Presentation/MINV.WebCatalog`:

- `npx vitest run src/4-presentation/panel/modules/ventas src/4-presentation/panel/modules/clientes` →
  **Test Files 4 passed (4) · Tests 60 passed (60)** (≈ 11 s), sin avisos en la salida.
  - `ventas/sales.test.ts` (17): número y sucursal, rango del servidor (hoy, «Todas», parciales, inválido), hora fiscal,
    estados, filtros, contador y limpieza, totales, acciones por rol, reglas SIN de anulación y correo, devoluciones,
    estadísticas, borrador de devolución (agrupar líneas, series, reembolso estimado, validaciones, pedido EXACTO) y
    descarga (base64, nombre seguro).
  - `ventas/SalesPage.test.tsx` (22): carga de HOY con pedidos exactos y totales al pie; filtros en la dirección (estado,
    cajero + pago, factura del SIN, sucursal, búsqueda); fechas al servidor («7 días», «Todas»); `?cliente=` = todas las
    ventas; error con «Reintentar»; resumen plegado; sin módulo SIAT; exportar CSV; detalle (series, pago, factura, plazo,
    devoluciones, «Ver el cliente»); anular (motivo obligatorio, error del servidor en el diálogo, pedido exacto, recarga);
    venta con factura activa → Documentos; devolución guiada (serie deshabilitada, validaciones, «por falla», reembolso,
    confirmación, `CreateSalesReturnCommand` exacto, envío de la nota, pestaña Devoluciones); `?devolver=1` (número,
    venta inexistente, «Buscar otra venta»); reimprimir (vista, área imprimible, PDF con su contenido y la entrega
    registrada, `window.print`, se limpia al cerrar); correo (validación, `email: null`); pestaña Devoluciones (nota,
    detalle, CSV, filtro); roles (GERENCIA, VENTAS, CONSULTA sin acceso); estadísticas; botones del tablero por rol.
  - `clientes/customers.test.ts` (7) y `clientes/CustomersPage.test.tsx` (14): lista con datos de factura y «Cliente web»,
    filtros en la dirección, detalle con «Ver sus ventas», CSV, error con «Reintentar», sin SIAT; alta (validaciones por
    campo, orden y forma exacta de los dos comandos), reintento que corrige el MISMO cliente, edición (datos de factura
    primero) y verificación del NIT, desactivar con confirmación, activar directo, consumidor final bloqueado, `?nuevo=1`;
    roles (GERENCIA solo lectura, BODEGA sin acceso con los dos permisos alternativos) y botón del tablero.
- `npx vitest run src/architecture.test.ts` → **16 passed (16)** (importaciones, tipos del servidor, sin red ni
  almacenamiento, sin HTML inyectado).
- `npx tsc -b --noEmit` → **sin errores en `ventas/` ni `clientes/`**. Quedan 2 errores de OTRO módulo en construcción:
  `modules/armador/module.tsx(22,78)` y `(23,74)`: `Cannot find module './BuilderPage'`.
- `npx oxlint src/4-presentation/panel/modules/ventas src/4-presentation/panel/modules/clientes` → **0 avisos, 0 errores**.
- `npm run build` no se ejecutó (lo hace la integración final, según las reglas del paquete).
