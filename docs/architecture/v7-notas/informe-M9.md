# Informe M9 · módulos «Reportes» y «Contabilidad» (Análisis) del panel web (tarea 11 de la V7)

Carpetas: `src/3. Presentation/MINV.WebCatalog/src/4-presentation/panel/modules/reportes/` y `…/contabilidad/`. No se
tocó nada fuera de esas dos carpetas (ni `shell/`, `kit/`, `hooks/`, `lib/`, registro, contrato, otros módulos ni
`package.json`), salvo este informe. Sin commits ni operaciones de git.

Claves, secciones y orden de la guía (§3.4): `reportes` (Análisis 10, `{ all: ['reports.view'] }`) y `contabilidad`
(Análisis 20, `{ all: ['accounting.manage'] }`). Un módulo no importa del otro: el período (`period.ts`,
`PeriodFields.tsx`) y la guarda del menú «⋯» (`useRowMenuGuard.ts`) están copiados en cada carpeta.

## Resumen

Las dos pantallas siguen el patrón del panel: título y descripción, botones de acción arriba, pestañas guardadas en la
dirección, barra de filtros con listas desplegables + búsqueda + fechas + «Limpiar filtros» (filtros, orden y página en
la dirección con `useTableState`), tabla ordenable y paginada con pie de totales y acciones por fila («⋯»), detalle en
panel lateral, exportar CSV (en el orden en que se ve la tabla), estados de carga, vacío y error con «Reintentar»,
avisos de éxito, formularios en diálogos con validación por campo y el error del servidor dentro del diálogo, y
confirmación antes de lo irreversible (un asiento no se borra). Los gráficos (del kit: `MiniBars`, `BarList`) van SOLO
dentro de un `Collapsible` «Ver gráfico ^» cerrado al entrar; al tablero va la estadística plegada. Botones y acciones
se muestran solo a quien puede (`canRun`); el servidor decide igual.

**Período con atajos** (los dos módulos): lista «Período» (Hoy, Ayer, Últimos 7 días, Últimos 30 días, Este mes, Mes
anterior, Últimos 90 días, Este año, Últimos 365 días, Personalizado) + fechas «Desde»/«Hasta» (días de La Paz). En la
dirección: `?periodo=esteMes` o `?desde=…&hasta=…` (con fechas a mano pasa a «Personalizado»). Fechas al revés,
inválidas o más de 366 días: aviso en el campo y en lugar de la tabla, y NO se consulta. Al cambiar de pestaña se
conserva el período y se olvidan los filtros, el orden y la página de la anterior. Reportes abre en «Últimos 30 días» y
Contabilidad en «Este mes» (como el escritorio).

### Análisis › Reportes (`/panel/reportes`, `reports.view`)

Pestañas (`?reporte=`): Ventas · Compras · Movimientos · Inventario (solo con `inventory.stock.view`) · Sucursales (solo
si la sesión ve varias sucursales) · Tecnología. Solo se monta la pestaña elegida. Un distintivo junto al título dice el
alcance: «Sucursal CM · La Paz» o «Todas las sucursales (vista consolidada)». Toda pestaña tiene «Actualizar»,
«Exportar CSV» e **«Imprimir»**: la hoja impresa (empresa, reporte, período, alcance, filtros, totales y la tabla
COMPLETA con su pie, no solo la página visible, «Impreso el … por …») se dibuja al final del `<body>`, oculta en la
pantalla, y al imprimir se oculta todo lo demás (clases `print:` de Tailwind; se quita al terminar).

| Escritorio (ReportsView + ReportsViewModel) | Web |
|---|---|
| Ventas: 4 tarjetas (ingresos con IVA, utilidad y margen, ventas y anuladas, ticket promedio) a la vista | «Totales del período» en texto (sin tarjetas): ingresos con IVA, IVA, sin IVA, costo de lo vendido, utilidad bruta, margen, ventas, ticket promedio, unidades y anuladas, tal como los calcula el servidor (rentabilidad) |
| Ventas por día (columnas) y medios de pago (dona) a la vista | Plegados en «Ver gráfico»: ventas por día (por semana si pasa de 62 días), medios de pago y los 10 primeros de la agrupación |
| Ranking de los 50 primeros por producto, categoría, cajero, cliente o medio de pago (combo) | Lista «Agrupar» (producto, categoría, cliente, cajero, medio de pago **y día**) + búsqueda; tabla completa paginada con puesto, cantidad, ventas, utilidad (con IVA), operaciones y participación; pie con totales; detalle lateral (puesto, participación, margen de la fila, promedio por operación) con enlaces a Stock (ficha y kardex), Movimientos, Ventas del cliente / cajero / medio de pago / día |
| Compras: 3 tarjetas + columnas por día + por proveedor | Totales en texto (recibido, recepciones, promedio, proveedores, órdenes abiertas y por recibir de HOY); agrupar por proveedor o por día; gráfico plegado; detalle con enlaces a Órdenes de compra y Proveedores |
| Movimientos: 3 tarjetas + combo de tipo + grilla | Tipo al servidor (`typeCode`, lista de `GetMovementTypesQuery`; sin ese permiso, los tipos que trae el reporte) + entradas o salidas, usuario y búsqueda en la página; totales (movimientos, entradas y salidas con unidades, productos, usuarios); detalle con observaciones; «Ver solo este producto», «Ver solo este tipo», «Ver ficha y kardex»; aviso si el servidor cortó en 20.000 |
| Inventario: 3 tarjetas + dona por categoría + ranking | Totales de HOY (valor, activos, en alerta, agotados, críticos, sin movimiento > 30 días); tabla por categoría (productos, unidades, valor, participación, en alerta, sin movimiento) con pie; filtro **Sucursal** (almacén, `warehouseCode`); detalle con los 10 productos de más valor; gráfico plegado |
| — (el escritorio lo tiene en «Sucursales») | **Sucursales** (`GetBranchReportQuery`, modelo de lectura): por sucursal (ventas, ingresos, IVA, ticket promedio, valor del stock, participación, filtro Sucursal y búsqueda) o por día (una columna por sucursal y total); totales con lo que está en tránsito y **de cuándo son los datos** (regla B-14) |
| — (el escritorio lo tiene en el tablero Tecnología) | **Tecnología** (`GetTechDashboardQuery`): período propio (últimos 7/30/90/180/365 días: el servidor mide hasta hoy); listas: ventas por categoría, por plataforma, tarjetas de video y consolas más vendidas, garantías abiertas por estado, unidades con serie en stock; totales (series, garantías y fuera de garantía, cotizaciones, armados vendidos, reservas web, series que no cuadran); enlaces a Garantías y Series |
| Período (combo de atajos) | Período con atajos + fechas a mano (arriba) |
| Exportar | Exportar CSV + Imprimir en cada pestaña |

Tablero: acciones **«Reporte de ventas»** (`/panel/reportes`) y **«Reporte de compras»** (`?reporte=compras`); estadística
plegada **«Ventas del mes vs mes anterior»** (`MonthSalesStat`): el mes en curso contra el MISMO tramo del mes anterior
(del 1 al mismo día), con la flecha y el porcentaje, el mes anterior completo como referencia y «Ver el reporte de
ventas del mes».

### Análisis › Contabilidad (`/panel/contabilidad`, `accounting.manage`)

Pestañas (`?vista=`): Estado de resultados · Libro diario · Plan de cuentas. Botones arriba: **«Nueva cuenta»** y
**«Nuevo asiento»** (también con `?nuevo=cuenta` / `?nuevo=asiento`, así los abre el tablero). El plan de cuentas se carga
una vez en la pantalla (lo usan el libro, el plan y los dos diálogos). Después de registrar un asiento todo se vuelve a
consultar y se muestra el libro diario.

| Escritorio (AccountingView + AccountingViewModel) | Web |
|---|---|
| Estado de resultados: 5 tarjetas + dona a la vista | Tabla del estado (secciones Ingresos, Costo de ventas y Gastos de operación con cada cuenta, totales, utilidad bruta y utilidad o pérdida neta) con la **parte de los ingresos** de cada fila; cada cuenta lleva a sus asientos del libro; «¿A dónde va cada boliviano vendido?» plegado; exportar CSV |
| Libro diario: búsqueda + combo de origen (ventas, compras, anulaciones, manuales) | Filtros: período (servidor), **Cuenta** (lista con búsqueda), Origen (venta, devolución, anulación, compra, transferencia, inventario, garantía, manual), **Estado** y búsqueda (número, descripción, cuenta o glosa); fila desplegable con el detalle Debe/Haber y totales; detalle lateral; con una cuenta elegida, **el mayor de la cuenta** (Debe, Haber y asientos); pie con Debe y Haber; «Usar como plantilla de un asiento nuevo»; CSV una fila por línea (como el escritorio); aviso si el servidor cortó en 5.000 |
| Plan de cuentas: combo de tipo + grilla con sangría | Árbol (sangría por nivel, grupos en negrita) con Debe, Haber y saldo; filtros Tipo, Clase (grupos / imputables), Movimiento y búsqueda; **saldos de todo el historial o del período elegido**; detalle lateral (grupo, nivel, saldos, subcuentas); «Ver los asientos de la cuenta» y «Crear una subcuenta» por fila; CSV |
| Panel «Nueva cuenta» (grupo, código sugerido, nombre) | Diálogo con el grupo (lista con búsqueda), el tipo que hereda, el código sugerido (la siguiente subcuenta con las cifras de sus hermanas: `6.1.04` → `6.1.05`) y el nombre; validación por campo (grupo, prefijo, repetida, largo) y el error del servidor dentro del diálogo |
| Nuevo asiento: plantillas, fecha (combo de 31 días), descripción, líneas con cuenta/debe/haber/glosa, totales en vivo | Diálogo con las mismas 7 plantillas, fecha (hasta hoy), **sucursal** (si se ven varias; sin sucursal activa es obligatoria), descripción y líneas (agregar / quitar, mínimo dos); **Debe = Haber en vivo** («Cuadrado» / «No cuadra: la diferencia es Bs …», anunciado a lectores de pantalla); validación por línea (cuenta, un solo lado, monto) y general; **confirmación** («Un asiento registrado no se borra ni se edita…»); el error del servidor queda en la confirmación y en el formulario |

Tablero: acciones **«Libro diario»** (`?vista=diario`), **«Estado de resultados»** y **«Registrar un asiento»**
(`?nuevo=asiento`). Sin estadística (el paquete no pide una para Contabilidad).

### Aviso del menú «⋯» (defecto del kit)

El menú de una fila se dibuja en un portal y React igual le pasa el clic a la fila, que abriría el detalle encima de la
acción elegida (`kit/aria.ts` › `fromInteractive` solo mira el DOM). Se evita con `useRowMenuGuard` en cada módulo:
mientras se atiende una opción del menú, el clic de la fila no hace nada. Probado («Ver solo este producto» en
Movimientos y «Usar como plantilla» en el libro diario no abren el detalle).

## Rutas y parámetros

| Ruta | Parámetros de la dirección |
|---|---|
| `/panel/reportes` | `reporte` (`compras`, `movimientos`, `inventario`, `sucursales`, `tecnologia`; sin él, ventas) · período `periodo`, `desde`, `hasta` · Ventas: `agrupar` (`producto`, `categoria`, `cliente`, `cajero`, `pago`, `dia`), `q` · Compras: `agrupar` (`proveedor`, `dia`), `q` · Movimientos: `tipo`, `flujo` (`entradas`, `salidas`), `usuario`, `q` · Inventario: `sucursal` (código de almacén), `q` · Sucursales: `agrupar` (`sucursal`, `dia`), `sucursal`, `q` · Tecnología: `dias` (7, 30, 90, 180, 365), `lista` (`categorias`, `plataformas`, `gpu`, `consolas`, `garantias`, `series`), `q` · tabla: `orden`, `sentido`, `pagina`, `filas` |
| `/panel/contabilidad` | `vista` (`diario`, `cuentas`; sin él, estado de resultados) · `nuevo` (`asiento`, `cuenta`) · período `periodo`, `desde`, `hasta` · Libro: `cuenta` (código imputable), `origen`, `estado` (`Posted`, `Draft`), `q` · Plan: `tipo` (`Asset`, `Liability`, `Equity`, `Revenue`, `Expense`), `clase` (`grupos`, `imputables`), `movimiento` (`con`, `sin`), `saldos` (`periodo`), `q` · tabla: `orden`, `sentido`, `pagina`, `filas` |

Enlaces que salen de estos módulos (solo direcciones): `/panel/stock?ficha=<SKU>`, `/panel/stock?categoria=<nombre>`,
`/panel/movimientos?producto=<SKU>&desde=…&hasta=…`, `/panel/ventas?cliente=<código>|cajero=<nombre>|pago=<nombre>&desde=…&hasta=…`,
`/panel/ventas?desde=<día>&hasta=<día>`, `/panel/ordenes-compra?q=<código>`, `/panel/proveedores?q=<código>`,
`/panel/garantias`, `/panel/series`. Entran: `/panel/reportes?periodo=esteMes` (estadística del mes) y los botones del
tablero.

## Casos de uso usados

| Operación | Permiso (contrato) | Dónde | Pedido que envía |
|---|---|---|---|
| `GetSalesReportQuery` | `reports.view` | Reportes › Ventas; estadística del mes | `{ from, to }` |
| `GetPurchasesReportQuery` | `reports.view` | Reportes › Compras | `{ from, to }` |
| `GetMovementsReportQuery` | `reports.view` | Reportes › Movimientos | `{ from, to, typeCode }` (`null` = todos) |
| `GetMovementTypesQuery` | `inventory.stock.view` | lista «Tipo» (si el rol puede) | `{}` |
| `GetStockProjectionQuery` | `inventory.stock.view` | Reportes › Inventario | `{ warehouseCode }` (`null` = sucursal activa) |
| `GetBranchesQuery` | `inventory.stock.view` | filtro «Sucursal» del inventario | `{}` |
| `GetBranchReportQuery` | `reports.view` + módulo `GLOBAL_AUDIT` | Reportes › Sucursales | `{ from, to }` |
| `GetTechDashboardQuery` | `reports.view` | Reportes › Tecnología | `{ days, gpuCategoryCode: 'GPU', consoleCategoryCode: 'CON' }` |
| `GetIncomeStatementQuery` | `accounting.manage` | Contabilidad › Estado de resultados | `{ from, to }` |
| `GetJournalQuery` | `accounting.manage` | Contabilidad › Libro diario | `{ from, to }` |
| `GetChartOfAccountsQuery` | `accounting.manage` | plan, lista «Cuenta», diálogos | `{ from: null, to: null }` o `{ from, to }` (saldos del período) |
| `CreateAccountCommand` | `accounting.manage` | «Nueva cuenta» | `{ code, name, parentCode }` |
| `CreateJournalEntryCommand` | `accounting.manage` | «Nuevo asiento» | `{ date, description, lines: [{ accountCode, debit, credit, memo }], branchId }` (solo líneas con monto; glosa vacía = `null`; `branchId` = la sucursal elegida o `null` con una sola sucursal) |

Todos los tipos se derivan del contrato (`RpcResponseOf` / `RpcRequestOf`); ningún tipo del servidor declarado a mano.

## Pendientes

Kit (no se tocó):

1. **Menú «⋯» y clic de la fila** (`kit/DataTable.tsx` + `kit/aria.ts`): el clic en una opción del menú (portal) llega a
   `onClick` de la `<tr>` y abre el detalle. Sugerencia: en `DataTable`, ignorar el clic si
   `!event.currentTarget.contains(event.target as Node)` (lo que viene de un portal no está en el DOM de la fila), o
   `stopPropagation` en `RowActions`. Hasta entonces cada módulo usa su guarda (`useRowMenuGuard`).
2. `lib` no reexporta `normalizeText` (de `@/shared/text`): el origen de los asientos quita acentos con una función propia
   del módulo.
3. **Período de un reporte**: `lib/dates` tiene los atajos de un FILTRO (con «Todas» = sin límite), pero un reporte
   necesita siempre un rango (más atajos: 30 y 90 días, este año, 365 días) y un tope de un año. `period.ts` +
   `PeriodFields.tsx` y `useRowMenuGuard.ts` están copiados en `reportes/` y `contabilidad/` (P-09). Sugerido: llevarlos
   a `panel/lib` y `panel/kit` (`ReportPeriodField`), y la hoja impresa (`usePrint` + `PrintSheet`) al kit si otros
   módulos quieren imprimir listas.

Servidor / contrato:

4. **Filtro de sucursal en los reportes**: `GetSalesReportQuery`, `GetPurchasesReportQuery`, `GetMovementsReportQuery`,
   `GetIncomeStatementQuery`, `GetJournalQuery` y `GetChartOfAccountsQuery` cubren la sucursal ACTIVA (o todas en la
   vista consolidada) y no reciben sucursal. Sugerencia: `BranchCode string? = null` validado contra el alcance
   (`BranchScope`). Mientras tanto la pantalla muestra el alcance y se cambia con la sucursal activa de la barra; el
   inventario sí filtra por almacén y «Sucursales» trae una fila por sucursal.
5. **Origen del asiento**: `JournalEntryRow` no dice de dónde viene: se deduce de la descripción («Venta …», «Anulación…»,
   «Compra…», «Transferencia…», «Toma física…», «Reposición por garantía…»), como el escritorio; frágil si cambia el
   texto. Sugerencia: `Origin` (enum: Sale, Void, Return, Purchase, SupplierInvoice, Transfer, Inventory, Warranty,
   Manual) y `BranchCode` en `JournalEntryRow`; filtros `AccountCode` y `Origin` y paginación en `GetJournalQuery`.
6. **Topes sin aviso**: `GetJournalQuery` corta en 5.000 asientos y `GetMovementsReportQuery` en 20.000 filas sin decirlo
   (la pantalla avisa si llega justo al tope). Sugerencia: `Truncated: bool` o paginación.
7. **Utilidad por fila vs utilidad bruta**: en `SalesReport`, la utilidad de cada grupo es monto CON IVA − costo, y la
   utilidad bruta del total es monto SIN IVA − costo: la suma de las filas no coincide con el total. La columna se
   rotula «Utilidad (con IVA)» para no confundir. Sugerencia: calcular la de los grupos sobre el neto.
8. `SalesReport.ByCustomer` trae solo los 15 clientes que más compraron («Por cliente (los 15 que más compraron)»).
   Sugerencia: la lista completa o un parámetro `Top`.
9. `GetTechDashboardQuery` mide solo «los últimos N días hasta hoy» (sin `From`/`To`): la pestaña Tecnología tiene su propio
   período. Sugerencia: `From`/`To` como los demás reportes.
10. `GetBranchReportQuery` exige el módulo comercial `GLOBAL_AUDIT` y la sesión web no informa la licencia: la pestaña se
   ofrece a quien ve varias sucursales y, si la empresa no tiene el módulo, el servidor lo rechaza y el error lo explica.
11. Las plantillas de asiento usan códigos fijos del plan de la empresa de prueba (`1.1.01`, `1.1.02`, `2.1.01`, `3.1.01`,
    `6.1.01`…`6.1.04`), igual que el escritorio; si una cuenta no existe, la línea queda sin cuenta. Sugerencia:
    `GetJournalTemplatesQuery` (o los `AccountCodes` en el contrato).
12. No hay casos de uso para renombrar o desactivar una cuenta, ni para cerrar un período contable (tampoco en el escritorio).

Integración:

13. Los enlaces a otros módulos usan sus filtros tal como están hoy: `stock?ficha=`, `stock?categoria=` (nombre),
    `movimientos?producto=`, `ventas?cliente=` (código), `ventas?cajero=` y `ventas?pago=` (nombre visible),
    `ordenes-compra?q=` y `proveedores?q=` (esos dos módulos se construyen en paralelo: verificar el nombre del filtro).

## Pruebas

Desde `src/3. Presentation/MINV.WebCatalog` (Git Bash, salida a archivo):

- `npx vitest run src/4-presentation/panel/modules/reportes src/4-presentation/panel/modules/contabilidad` →
  **4 archivos, 56 pruebas, todas pasan** (reportes: 17 de funciones puras + 18 de pantalla; contabilidad: 12 de
  funciones puras + 9 de pantalla), sin avisos en la consola. Cubren: pedidos exactos de cada consulta y de los dos
  comandos, totales, agrupaciones, búsqueda y filtros en la dirección, período (atajo, fechas al revés sin consulta,
  fechas a mano), CSV (encabezados y filas), impresión de TODAS las filas, detalle lateral y enlaces, guarda del menú
  «⋯», gráficos plegados al entrar, error con «Reintentar», pestañas según permisos (Inventario, Sucursales),
  validaciones y errores del servidor dentro de los diálogos, confirmación del asiento, Debe = Haber en vivo, acceso
  denegado (CAJERO en Reportes, VENTAS en Contabilidad), acciones y estadística del tablero por rol y la estadística del
  mes. Las pantallas se prueban con un registro que solo tiene estos dos módulos.
- `npx tsc -b --noEmit` → **0 errores** en todo el proyecto (en una corrida anterior había 2 en
  `modules/homologacion/module.tsx`, de otro paquete en construcción; ya no están).
- `npx oxlint src/4-presentation/panel/modules/reportes src/4-presentation/panel/modules/contabilidad` → 0 avisos, 0 errores.
- `npx vitest run src/architecture.test.ts` → 15 pruebas, pasan (capas, importaciones del módulo, tipos del servidor, sin
  HTML inyectado). `registry/registry.test.ts` no llega a correr por módulos ajenos en construcción (al final:
  `modules/libros/module.tsx` importa `./TaxStat`, que todavía no existe); la validez de `reportes` y `contabilidad` se
  comprueba en `ReportsPage.test.tsx` (`REGISTRY.problems` vacío, sección Análisis, orden 10 y 20).
- `npx vitest run` (todo el proyecto, con los módulos de los demás paquetes a medio construir) → **115 archivos, 1.468
  pruebas: 1.461 pasan y 7 fallan, ninguna de este paquete**: `modules/inicio/InicioPage.test.tsx` (2: esperan que
  Administración tenga solo los botones de Actividad y que el cajero no tenga «Ver estadísticas»; los agregan Usuarios,
  Integraciones, Configuración, Caja, Ventas… — el cajero no ve nada de Reportes ni de Contabilidad), `app/routeTable.test.tsx`
  (esperan el título del tablero con la espera de 1 s de la búsqueda mientras se descarga el panel completo; siguen
  fallando al correr ese archivo solo), y `pages/auth/LoginPage.test.tsx`, `pages/cart/CartPage.test.tsx` y
  `modules/armador/Armador.test.tsx` (1 cada una, por tiempo con toda la suite en paralelo: al volver a correrlos solos,
  pasan). No toqué esas carpetas.
