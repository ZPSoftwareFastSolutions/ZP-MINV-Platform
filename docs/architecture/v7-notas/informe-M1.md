# Informe M1 · módulo «Caja» (Ventas) del panel web (tarea 11 de la V7)

> Escrito por quien construyó el paquete, en `src/3. Presentation/MINV.WebCatalog` (árbol de trabajo de la rama
> `Inventario-V7`). Sin commits: el avance parcial quedó en el commit wip `5563b40` del coordinador y después se terminó
> el módulo. Solo se tocó `src/4-presentation/panel/modules/caja/` (y este informe).

## Resumen

Módulo nuevo `caja` (Ventas › Caja, `/panel/caja`, permiso `sales.pos.operate`, orden 10, módulo comercial
`POS_HARDWARE`), con la estructura del ejemplo `actividad`: definición liviana, funciones puras con sus pruebas,
pantalla y diálogos con el conjunto del panel, y pruebas de pantalla con el servidor simulado. Todos los tipos del
servidor se derivan del contrato generado (`types.ts`: nombres terminados en `Data`/`Payload`); nada de `fetch`,
almacenamiento, HTML inyectado ni direcciones de otros dominios. El servidor decide todo (P-01): la página guía, muestra
y envía los mismos casos de uso del escritorio.

Es el equivalente web de «Punto de venta» del escritorio (`PosView.xaml` + `PosViewModel.cs` + `PosFiscal.cs` +
`BuyerForm` de `BillingCommon.cs`). Tiene TODAS sus funciones y además las mejoras que pide el paquete:

| Escritorio | Web (`/panel/caja`) |
|---|---|
| Caja cerrada: combo de caja + fondo + «Abrir caja» | Tarjeta grande «La caja está cerrada» + «Abrir caja» → diálogo con la caja en lista desplegable (preseleccionada la **sugerida por el servidor**: la del turno propio o una libre de la sucursal activa), **fondo inicial** obligatorio (≥ 0) con montos frecuentes (Bs 0/100/200/500/1.000); error del servidor dentro del diálogo. |
| Caja abierta: «Caja X abierta · desde · ventas · esperado» + «Cerrar caja» (pregunta el contado) | Barra del turno con el mismo resumen y **«Cerrar caja» con arqueo**: fondo, ventas en efectivo, **efectivo esperado**, ventas del turno, **efectivo contado** (con «Contó lo esperado»), **diferencia en vivo** (exacto / sobrante / faltante) y **confirmación** (diálogo de alerta: el turno cerrado no se reabre). Al terminar, aviso con la diferencia que calcula el servidor. |
| Banda fiscal (en línea · fuera de línea · contingencia · no lista) | La misma banda (`GetPosFiscalStateQuery`), con los mismos títulos y tonos; aviso si hay productos sin homologar. |
| Buscador + escáner (`OnScanned`), chips de categoría y de plataforma | Buscador **«Buscar o escanear (F2)»**: el lector de códigos funciona como teclado (código + **Enter agrega**; también el SKU, y si el texto deja un solo producto, ese). Un código que no está a la venta se **explica** con `GetProductLookupQuery` (inactivo / sin precio / desconocido). Filtros con **listas desplegables** en la dirección: Categoría (de los productos del servidor, con cuántos hay), **Plataforma** (opciones de las especificaciones «plataforma»/«plataformas», solo las que tienen productos), **Condición** (opciones de «condicion»; la filtra el servidor con `SearchTechProductsQuery`) y Disponibilidad; «Limpiar filtros». |
| Cuadrícula de productos (precio, «Disponible n (reservado m)», agotado, insignia de serie/IMEI, plataformas) | **Tabla ordenable y paginada** (tarjetas en el teléfono) con producto (SKU, insignias Serie/IMEI, plataformas, «En la venta: n»), categoría, precio, **disponible = existencias − reservado** (con lo reservado), botón **«Agregar»** (o «Elegir IMEI/serie»; deshabilitado si está agotado, con la caja cerrada o con una reserva cargada), acciones por fila, **detalle lateral** (precio, disponible, reservado, marca, serie, garantía, plataformas, códigos, especificaciones) y **Exportar CSV**. Estados de carga, vacío y error con «Reintentar». |
| Carrito: cantidad ±, descuento 0–25 %, quitar, series («Elegir»), «supera lo disponible», vaciar | Panel «Venta actual» (fijo al costado desde 1280 px): cantidad con − / campo / + (enteras o con decimales según la unidad), **descuento** en lista desplegable (0/5/10/15/20/25 %), quitar, **series o IMEI obligatorios** («Cambiar o agregar unidades»), aviso **«Supera lo disponible: hay n (m reservadas para otras ventas)»**, «Vaciar» con confirmación, totales con descuentos e **IVA incluido** (13 % de lo facturado, como `VatRules`). Lo que impide cobrar (series que faltan, más de lo disponible) se lista antes de cobrar. |
| Selector de series (`SerialsDialog`) | Diálogo de unidades disponibles (`GetAvailableSerialsQuery`): **escanear la serie + Enter la marca**, lista con casillas (depósito y fecha de ingreso), búsqueda, «Elegidas n de m» en una reserva; no ofrece las series que ya usan otras líneas. |
| Cliente y medio de pago (combos), efectivo recibido con Exacto/50/100/200 y vuelto, referencia, tarjeta | **Diálogo «Cobrar la venta»** (F4): total grande con IVA y descuentos, **cliente con búsqueda** (consumidor final por defecto), **datos de la factura** si la caja factura, **medio de pago** en lista desplegable, **efectivo recibido** con «Exacto» y billetes redondos por encima del total y **vuelto / faltan** en vivo, **referencia** obligatoria para los medios que no abren el cajón, **número de tarjeta** (solo si factura; viaja al cobrar y no se guarda) y el botón **«Cobrar Bs X»** como confirmación con el total. El error del servidor queda en el diálogo para corregir y reintentar (mismo `requestId`). |
| `BuyerForm`: tipo de documento, número, complemento (CI), razón social, correo, NIT especiales, «¿ya compró antes?», «Verificar NIT», «Facturar aunque el NIT no sea válido» | Lo mismo: tipo de documento en **lista desplegable** del catálogo del SIN (o los 5 del SIN si no se sincronizó), guía de las reglas del SIN (CI/NIT solo dígitos, hasta 20, complemento solo con CI y hasta 5, correo válido), documento obligatorio a consumidor final, **NIT especiales** 99003/99002/99001, **autocompletado** al salir del número (`FindFiscalBuyerQuery`: nombre, correo y elige al cliente), **Verificar NIT** (`VerifyNitCommand`) y la excepción. |
| Tras cobrar: `DispatchFiscalDocumentsCommand` → documento definitivo, ticket con QR, imprimir otra vez, PDF, correo, «Ver el documento», «Siguiente venta» | Diálogo **«Venta F-… cobrada»**: total, **vuelto a entregar**, número de **venta** y de **pedido**, cliente, medio, IVA; la **factura enviada al SIN** (`DispatchFiscalDocumentsCommand` recién después de cobrar, como el escritorio: el documento DEFINITIVO si se re-emitió fuera de línea) con **número, CUF, estado** y qué significa, avisos del envío; **«Imprimir comprobante»** (vista imprimible del navegador con `GetFiscalPrintModelQuery`, o el ticket de la venta sin factura), **«Descargar PDF»** (`RenderFiscalDocumentQuery`), **«Enviar por correo»** (propone el correo escrito en la factura), «Ver el comprobante» (plegado), enlaces a Ventas y a Documentos, **«Siguiente venta»** (vuelve al buscador). |
| «Desde armado» (cotizaciones y reservas vigentes) y «Vender en caja» desde el armador | **`?reserva=<NÚMERO>`** (lo usan Reservas y Armador) y botón **«Vender una reserva»** (número escrito/escaneado o lista de las **vigentes** con filtros tipo/estado/canal, búsqueda y CSV): carga la reserva o cotización con `GetPcBuildQuery`, **bloquea sus líneas (precio congelado)**, **precarga los datos para la factura** que dejó quien reservó y su cliente, pide las **series** de las piezas y cobra con **`SellPcBuildCommand`** (consume la reserva). Si no se puede cobrar (vendida, anulada, vencida, borrador) se explica. Si había otra venta en curso, pide confirmación. |
| Ticket en pantalla / impreso | Comprobante dibujado en la página e impreso con `window.print()` (hoja `caja-impresion.css`: solo el comprobante, negro sobre blanco; «SIN VALOR LEGAL» en el ambiente de pruebas). |

Mejoras propias de la web: **atajos visibles** (F2 buscar o escanear, F4 cobrar, Esc cancelar: vuelve al buscador y
borra la búsqueda; en una ventana, Esc la cierra), botón **«Pantalla completa»** (API del navegador, solo si existe) y
diseño a pantalla completa (productos + venta lado a lado, «Ir al cobro · Bs X» en pantallas angostas), **la venta no se
pierde** (salir de la caja con productos pide confirmación y el navegador avisa al recargar), al **cambiar la sucursal
activa** la venta se vacía con un aviso, anuncios para lectores de pantalla («Se agregó …»), objetivos de 44 px y
etiquetas en todos los campos. Sin indicadores a la vista al entrar: el resumen del turno es texto (lo pide el paquete);
los números van en la estadística plegada.

**Tablero** (`actions`): «Ir a la caja» (`''`) y «Vender una reserva» (`?elegir=reserva`, además con `sales.view` e
`inventory.movements.register.sales`). **Estadística** (`stats`, plegada): «Ventas de mi turno» (ventas, total, en
efectivo, efectivo esperado en la caja, fondo; «Ir a la caja»; sin turno lo dice).

## Rutas y parámetros

| Ruta | Parámetros |
|---|---|
| `/panel/caja` | Lista de productos (`useTableState`): `q` (búsqueda), `categoria` (código), `plataforma` (opción), `condicion` (opción), `stock` (`con` · `sin`), `orden` (`producto`, `categoria`, `precio`, `disponible`), `sentido`, `pagina`, `filas`. |
| | **`reserva=<NÚMERO>`**: carga esa reserva o cotización para cobrarla (sin distinguir mayúsculas: `res-cb-000007` → `RES-CB-000007`). Se quita al vender, al «Quitar» o si se cancela la confirmación. |
| | **`elegir=reserva`**: abre «Vender una reserva o cotización» (el botón del tablero); se quita al cerrar o al elegir. |

Enlaces a otros módulos (solo direcciones): `/panel/reservas?q=<NÚMERO>` («Ver en Reservas» cuando una reserva no se
puede cobrar; M3 usa `q`), `/panel/ventas?q=<VENTA>` («Ver la venta en Ventas»; M2 usa `q`) y
`/panel/documentos-fiscales?q=<CUF>` («Ver en Facturación › Documentos»; ver Pendientes 6).

## Casos de uso usados

| Operación | Permisos (contrato) | Para qué |
|---|---|---|
| `GetPosStateQuery` | `sales.pos.operate` | Cajas, medios de pago, clientes, IVA, turno abierto, caja sugerida (pantalla y estadística). |
| `OpenPosSessionCommand` | `sales.pos.operate` (+ módulo `POS_HARDWARE`) | Abrir caja `{ registerCode, openingCash }`. |
| `ClosePosSessionCommand` | `sales.pos.operate` (+ `POS_HARDWARE`) | Cerrar con arqueo `{ sessionId, countedCash }`; muestra la diferencia que devuelve. |
| `GetSellableProductsQuery` | `inventory.stock.view` | Productos a la venta (precio, disponible = existencias − reservado, códigos de barras). |
| `SearchTechProductsQuery` | `inventory.stock.view` | Serie o IMEI, garantía, plataformas, marca (`max: 2000`) y el filtro «Condición» (`filters: [{ code: 'condicion', … }]`). |
| `GetStockReservationsQuery` | `inventory.stock.view` | Lo reservado por producto (informativo). |
| `GetSpecDefinitionsQuery` | `inventory.stock.view` | Opciones de «Plataforma» y «Condición» (regla T-07). |
| `GetProductLookupQuery` | `inventory.stock.view` | Explicar un código que no está a la venta (`includeInactive: true`, solo al primer código fallido). |
| `GetAvailableSerialsQuery` | `inventory.serials.view` | Series o IMEI disponibles `{ sku, warehouseCode: null }`. |
| `CheckoutCommand` | `sales.pos.operate` + `inventory.movements.register.sales` | Cobrar la venta (todos los parámetros; `null` lo que no aplica). |
| `GetPosFiscalStateQuery` | `sales.pos.operate` | Banda fiscal y tipos de documento del comprador. |
| `FindFiscalBuyerQuery` | `billing.issue` | «¿Ya compró antes?» al salir del número (se envía con `useRpcCommand`, sin avisos: es una consulta a pedido). |
| `VerifyNitCommand` | `billing.issue` (+ `FISCAL_SIAT`) | «Verificar NIT» `{ nit, customerCode }`. |
| `DispatchFiscalDocumentsCommand` | `billing.issue` (+ `FISCAL_SIAT`) | Enviar la factura al SIN después de cobrar `{ documentId, max: 50 }` y mostrar el documento definitivo. |
| `GetFiscalPrintModelQuery` | `billing.view` | El comprobante para imprimir (con factura). |
| `RenderFiscalDocumentQuery` | `billing.view` | «Descargar PDF» `{ documentId, format: 'Pdf', columns: 48 }` (con `useRpcCommand`: se pide al pulsar). |
| `SendFiscalDocumentEmailCommand` | `billing.issue` (+ `FISCAL_SIAT`) | «Enviar por correo» `{ documentId, email }`. |
| `GetPcBuildQuery` | `sales.view` | Cargar la reserva o cotización de `?reserva=`. |
| `GetPcBuildsQuery` | `sales.view` | Lista de reservas y cotizaciones vigentes de «Vender una reserva» (`{ status: null, channel: null, kind: null }`). |
| `SellPcBuildCommand` | `sales.pos.operate` + `inventory.movements.register.sales` | Cobrar la reserva con las series de sus piezas `{ number, paymentMethodCode, serials, cashReceived, paymentReference, buyer, cardNumber, customerCode }`. |

Cada botón o bloque se muestra solo si la sesión puede ejecutar su operación (`canRun`; `capabilities.ts`); el servidor
vuelve a decidir en cada pedido.

## Pendientes

1. **Prueba del tablero de `inicio` (no es de este módulo).** `modules/inicio/InicioPage.test.tsx › «Inicio · con los
   módulos del proyecto»` supone el tablero exacto de TODO el proyecto: «un rol sin «Actividad» no ve sus botones ni su
   estadística» espera que CAJERO no tenga ningún botón ni «Ver estadísticas», y ahora Caja le ofrece «Ir a la caja»,
   «Vender una reserva» y «Ventas de mi turno» (lo pide el paquete). La otra («el administrador ve los botones de
   «Actividad»…») falla por el módulo Usuarios (M10). La integración debe actualizar esas dos expectativas.
2. **Contrato · `PosOption` sin «exige referencia».** El servidor sabe qué medio exige referencia
   (`PaymentMethod.RequiresReference`), pero `PosState.paymentMethods` solo trae `code`, `name` y `opensCashDrawer`. La
   web hace lo mismo que el escritorio: pide la referencia a todo medio que no abre el cajón. Sugerido: agregar
   `RequiresReference` a `PosOption` (y la web la usaría en vez de la regla del escritorio).
3. **Contrato · cajas sin sucursal.** `PosState.registers` no dice de qué sucursal es cada caja; la web preselecciona la
   sugerida por el servidor (`suggestedRegister`: la libre de la sucursal activa) pero no puede ocultar las de otras
   sucursales visibles. Sugerido: `BranchCode` en `PosOption` de las cajas (o filtrarlas por la sucursal activa).
4. **Contrato · condición del producto.** `TechProductRow` trae `platforms` pero no la condición (Nuevo, Usado…): el
   filtro «Condición» pide al servidor otra vez `SearchTechProductsQuery` con el filtro. Sugerido: `Condition` en
   `TechProductRow` para filtrar en la página.
5. **Kit · código QR.** El escritorio dibuja el QR de verificación del SIN en el ticket; el kit no tiene un componente de
   QR (y no se agregan dependencias). El comprobante imprime la dirección de verificación en texto y «Descargar PDF» trae
   la representación gráfica oficial del servidor (con su QR). Sugerido: un `QrCode` en SVG en `kit/`.
6. **Clave del módulo de documentos fiscales.** El README (§3.4) sugiere `documentos` y el paquete M8 dice
   `documentos-fiscales` (M2 enlaza a `/panel/documentos`). La caja enlaza a `/panel/documentos-fiscales?q=<CUF>` (la
   del paquete M8); si M8 usa otra clave o no acepta `q`, hay que igualarlo.
7. **Re-emitir una factura rechazada.** El escritorio ofrece «Corregir y re-emitir» en el resultado del cobro; el
   paquete M1 no incluye `ReissueFiscalDocumentCommand` (es de M8, «Documentos»): la caja explica el rechazo y enlaza al
   documento.
8. **Impresión.** El comprobante se imprime con una hoja propia del módulo (`caja-impresion.css`, importada por
   `ReceiptSheet.tsx`, el mismo mecanismo que la cotización de «Armá tu PC»): marca `<html data-imprimir="caja">` solo
   mientras imprime. Si se prefiere, el kit podría ofrecer un «imprimir esta sección» común.
9. **Verificación visual.** No se abrió en el navegador: el modo mock (`VITE_API_URL=mock`) no atiende las operaciones de
   la caja (solo cuenta, sucursal y actividad) y crear `.claude/launch.json` queda fuera de la carpeta del módulo. El
   diseño a 360 px usa las tarjetas de `DataTable` y grillas que se pliegan; conviene revisarlo contra el servidor.

## Pruebas

Desde `src/3. Presentation/MINV.WebCatalog`:

- `npx vitest run src/4-presentation/panel/modules/caja` → **5 archivos, 52 pruebas, todas pasan** (estable en varias
  corridas seguidas; ~25 s):
  - `products.test.ts` (7): unión con la ficha técnica y lo reservado, filtros (categoría, plataforma, condición,
    disponibilidad, búsqueda sin acentos), lector de códigos, explicación de un código fuera de venta, opciones de las
    listas desde el servidor, disponible en palabras y CSV.
  - `cart.test.ts` (8): agregar/cantidades/decimales, series, descuentos y quitar, reserva con líneas fijas, ficha técnica
    de una pieza fuera de venta, importes con la aritmética del servidor e IVA incluido, avisos (series, disponible) y
    series agrupadas.
  - `payment.test.ts` (6): valores por defecto, efectivo/tarjeta, vuelto y billetes, la guía antes de cobrar y los pedidos
    EXACTOS de `CheckoutCommand` y `SellPcBuildCommand`.
  - `fiscal.test.ts` (11): banda fiscal, tipos de documento del SIN, comprador y NIT especiales, autocompletado y
    verificación, envío al SIN (documento definitivo, fuera de línea, falla), hora fiscal, reservas cobrables y su
    explicación, precarga de la factura, lista para elegir, arqueo y comprobante con y sin factura.
  - `PosPage.test.tsx` (20, panel real con el servidor simulado): abrir caja (validación y pedido exacto), cerrar con
    arqueo y confirmación, error de carga con «Reintentar», lista y filtros en la dirección, lector de códigos + F2,
    código fuera de venta, IMEI escaneado, agotado y «supera lo disponible», cobro en efectivo (pedido exacto, vuelto,
    siguiente venta), referencia obligatoria y error del servidor dentro del cobro, CSV, factura (documento obligatorio,
    «¿ya compró antes?», Verificar NIT, envío al SIN), imprimir/PDF/correo, sesión sin `billing.issue` (sin verificar ni
    enviar), vender una reserva de la dirección (precio congelado, factura precargada, series, `SellPcBuildCommand`
    exacto), reserva vendida, «Vender una reserva» con la lista, rol sin acceso (GERENCIA), estadística, botones del
    tablero por rol y «no se pierde la venta».
- `npx vitest run src/4-presentation/panel/registry src/architecture.test.ts` → **2 archivos, 30 pruebas, pasan** (el
  módulo es válido y respeta las reglas de arquitectura).
- `npx tsc -b --noEmit` → **0 errores** en todo el proyecto.
- `npx oxlint src/4-presentation/panel/modules/caja` → **0 avisos, 0 errores**.
- Fuera del módulo: `npx vitest run src/4-presentation/panel/modules/inicio` → 2 pruebas fallan por la suposición del
  tablero de todo el proyecto (Pendientes 1); no se tocaron.
