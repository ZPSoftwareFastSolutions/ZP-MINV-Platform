# Informe M6 · módulos «Catálogo» y «Toma física» (Inventario) del panel web (tarea 11 de la V7)

Carpetas: `src/3. Presentation/MINV.WebCatalog/src/4-presentation/panel/modules/catalogo/` (22 archivos) y
`…/modules/toma-fisica/` (10 archivos). No se tocó nada fuera de esas dos carpetas (ni `shell/`, `kit/`, `hooks/`, `lib/`,
registro, contrato, otros módulos ni `package.json`), salvo este informe. Sin commits.

Claves `catalogo` y `toma-fisica`, sección Inventario, orden 20 y 40 (los de la guía §3.4). Permisos del módulo:

- Catálogo: `{ all: ['inventory.stock.view'] }` (el paquete: «ver con `inventory.stock.view`»; la tabla de la guía decía
  `catalog.manage`, prevalece el paquete). Crear y modificar productos y categorías: `catalog.manage`; fichas técnicas y
  especificaciones: `catalog.specs.manage`.
- Toma física: `{ all: ['inventory.counts.record', 'inventory.stock.view'] }` (contar exige su permiso; buscar productos,
  posiciones y la ficha, ver el stock). Generar ajustes y anular: `inventory.counts.post`.

## Resumen

Las pantallas siguen el patrón del panel: título y descripción, botones de acción arriba, barra de filtros con listas
desplegables + búsqueda + «Limpiar filtros», filtros/orden/página en la dirección (`useTableState`), tabla ordenable y
paginada con acciones por fila («⋯»), detalle en panel lateral, formularios en diálogos con validación por campo y el
error del servidor DENTRO del diálogo, confirmación antes de lo irreversible, exportar CSV, estados de carga, vacío y error
con «Reintentar», aviso de éxito tras cada comando, y los indicadores SOLO plegados (`Collapsible` «Ver …» cerrado al
entrar). Lo que el rol no puede hacer no se muestra (`canRun` con los permisos del contrato); el servidor decide igual.

### Inventario › Catálogo (`/panel/catalogo`)

Tres pestañas (`?pestana=`): **Productos**, **Categorías** y **Especificaciones**. Las consultas comunes
(`GetCatalogOptionsQuery`, `GetCatalogQuery` y `GetSpecDefinitionsQuery` de todas las categorías) se hacen una vez en la
página y se comparten.

| Escritorio (CatalogView, CatalogViewModel, CatalogTech) | Web |
|---|---|
| Galería con imágenes o lista | **Lista** (tabla: Producto con SKU y distintivos «Serie»/«IMEI» y «sin imagen», Categoría, Marca, Precio, Margen, Disponible, Estado) o **Galería** (`?vista=galeria`: tarjetas con imagen, precio, marca, garantía, plataformas, especificaciones clave y disponible, «Ordenar por», paginación). La galería pide las imágenes SOLO de la página a la vista (`GetProductImagesQuery` con esos `variantIds`), no todo el catálogo |
| Combo de categoría (con sus subcategorías) + chips de estado + orden | Listas desplegables: **Categoría** (con sus subcategorías, por el servidor), **Marca** (con «Sin marca»), **Estado** (activo/inactivo), **Imagen** (con/sin), **Precio** (con / sin precio / margen bajo < 15 %), **Serie o IMEI** (serie, IMEI, sin serie), **Plataforma** y **Condición** (opciones de las especificaciones «plataforma(s)» y «condicion», regla T-07, nunca listas fijas) y búsqueda (nombre, SKU, código de barras, marca, proveedor, categoría o descripción) |
| Facetas de la categoría elegida | Una lista desplegable por especificación filtrable de la categoría (`GetSpecFacetsQuery`, con la cantidad de productos por valor y la unidad); lo elegido va a la dirección (`?espec=código:valor|…`) y lo filtra el SERVIDOR (`GetCatalogQuery` con `categoryCode` y `specFilters`), igual que la condición |
| 4 tarjetas de indicadores a la vista | Plegadas en «Ver resumen del catálogo»: activos, con imagen, precio promedio, margen promedio y productos por categoría |
| Editor lateral (General / Ficha técnica) | Diálogo por pestañas: **Datos** (SKU —fijo al editar—, nombre, categoría o «Crear una categoría nueva» sin salir del formulario, unidad —fija al editar—, proveedor preferido, posición del almacén de trabajo, código de barras con «Generar un código interno» EAN-13, descripción, activo) · **Precios** (costo, precio con IVA, «Aplicar un margen» 20–50 % y el margen y el precio sin IVA como guía) · **Imágenes** (elegir, cambiar, quitar; las fotos grandes se reducen en el navegador con un lienzo a 800 px JPEG hasta ≤ 1 MB, como `ImageFiles.Pick`) · **Ficha técnica** (lleva serie o IMEI, tipo, meses de garantía y cada especificación con su control: número con unidad, texto, una opción o varias) · **Stock mínimo** (mínimo y máximo del almacén de trabajo). Una pestaña con errores dice «(revisar)» y el guardar lleva a ella |
| Guardar: producto → imagen → ficha técnica | Igual y en ese orden. Si el producto se guardó pero la imagen o la ficha no, el diálogo lo dice, queda en esa pestaña y el reintento corrige ESE producto (`originalSku`), no crea otro |
| Sin `catalog.manage` el editor no abre | Mejora: con `catalog.specs.manage` sin `catalog.manage` (bodega, gerencia) se ofrece «Editar ficha técnica» (diálogo solo con la ficha, `SaveProductTechCommand`) |
| «Ver ficha y kardex» | Detalle lateral (`?producto=SKU`): imagen, datos, precio con y sin IVA, costo, margen, mínimo/máximo, posición, disponible, descripción y la ficha técnica completa (`GetProductTechQuery`); botones «Editar producto» o «Editar ficha técnica», «Ver ficha y kardex» (→ `/panel/stock?ficha=SKU`), «Activar» / «Desactivar» (con confirmación) |
| Exportar CSV | Exportar CSV en el orden de la tabla (las columnas del escritorio + marca, disponible, serie, garantía y plataformas) |
| «Especificaciones» (SpecsAdminDialog) | Pestaña **Especificaciones**: todas (cada una en su categoría) o las de una categoría CON las heredadas de sus madres; filtros Categoría, Tipo, Uso (obligatorias, filtrables, armador, heredadas) y búsqueda; detalle lateral; «Nueva especificación» y «Modificar» (o «Modificar en <madre>» para una heredada): nombre, código sugerido del nombre, tipo, varios valores, unidad, opciones (una por renglón), filtrable, obligatoria, clave del armador y orden (el tipo, el multivalor, la categoría y el código quedan fijos al editar) |
| «Nueva categoría» / «Nueva subcategoría» (con prompt) | Pestaña **Categorías**: productos propios, activos y especificaciones propias de cada una; filtros; detalle lateral; «Nueva categoría» (con madre opcional), «Nueva subcategoría», «Cambiar el nombre» (código sugerido como el escritorio: PER, PER2…), «Ver sus productos» y «Ver sus especificaciones» (llevan a la otra pestaña ya filtrada) |

Tablero: acciones **«Nuevo producto»** (`?nuevo=producto`, con `catalog.manage`) y **«Buscar en el catálogo»**; estadística
plegada **«Estado del catálogo»** (activos, sin imagen, activos sin precio, margen bajo, con accesos al catálogo filtrado).

### Inventario › Toma física (`/panel/toma-fisica`)

| Escritorio (PhysicalCountView, PhysicalCountViewModel) | Web |
|---|---|
| «No hay una toma física en curso» + observación + «Iniciar toma física» (almacén de trabajo, hoy) | Igual, con **Fecha del conteo** (hoy o anterior; la futura se avisa) y, si la sucursal activa tiene más de un almacén, lista desplegable **Almacén** (`?almacen=`; cada almacén tiene su toma) |
| Producto (buscador) + escáner + posición + cantidad + «En el sistema» y diferencia | «Registrar un conteo»: campo **Código de barras o SKU** para el LECTOR (escribe y Enter: elige el producto y pasa el foco a la cantidad; un código desconocido se avisa), **Producto** (lista con búsqueda), **Posición** (del almacén de la toma), **Cantidad contada** (sin decimales si la unidad no los admite), **En el sistema** y la diferencia como guía (`GetProductCardQuery`; solo si la toma es del almacén de trabajo) y «Ya contado: … registrar de nuevo lo corrige» |
| Lista de lo contado con diferencia | Pestaña **Contados**: Producto, Posición, Sistema, Contado, **Diferencia resaltada** (sobrante, faltante, cuadra), Contó y hora; filtros **Categoría**, **Ubicación**, **Diferencia**, **Contó** y búsqueda; acciones «Corregir conteo» (carga el producto y la posición en el formulario), «Ver ficha y kardex», «Quitar conteo» (con confirmación) |
| — | Mejoras: pestaña **Pendientes de contar** (lo que falta del alcance elegido, con «Contar»), **progreso** («Contados X de Y productos · Z %», con barra, según la categoría y la ubicación elegidas), **Imprimir planilla** (vista imprimible del navegador: productos del alcance por posición, columna vacía para anotar y firmas; conteo a ciegas) y **Exportar CSV** |
| Tarjetas de contados, sobrantes, faltantes y cuadran | Plegadas en «Ver resumen de diferencias» |
| «Generar ajustes» con confirmación y detalle | Confirmación con el **resumen de diferencias** (conteos, sobrantes → AJUSTE (+), faltantes → AJUSTE (−), cuadran y las 10 diferencias más grandes) y el aviso de productos con serie; `PostPhysicalCountCommand` con `confirmed: true`; el resultado queda a la vista con «Ver los movimientos». Un rechazo del servidor queda dentro de la confirmación |
| «Anular toma» con confirmación | Igual (`CancelPhysicalCountCommand`) |

Tablero: acción **«Iniciar toma física»** (abre la toma en curso o el formulario para iniciarla).

### El defecto del menú «⋯» (aviso de otro ingeniero)

Confirmado: `RowActions` dibuja el menú en un portal y React propaga el clic de una opción hasta el `<tr>`;
`fromInteractive` (`kit/aria.ts`) solo mira el DOM, así que la fila llamaría a `onRowOpen` después de la acción. Guarda en
cada tabla del catálogo que abre un detalle (Productos, Categorías, Especificaciones): `pickingAction`/`fromMenu`, como
M5. Probado: «Editar» desde el menú abre el diálogo SIN abrir el detalle ni tocar la dirección. En «Toma física» las filas
no abren detalle (todo está a la vista), así que no hay a dónde propagarse. Ver «Pendientes» 1.

## Rutas y parámetros

| Dirección | Qué hace |
|---|---|
| `/panel/catalogo` | Productos (lista). Filtros: `q`, `categoria`, `marca` (`_sin` = sin marca), `estado` (`activo`/`inactivo`), `imagen` (`con`/`sin`), `precio` (`con`/`sin`/`margen-bajo`), `serie` (`serie`/`imei`/`no`), `plataforma`, `condicion`, `espec` (`código:valor|…`, codificado); orden y página (`orden`, `sentido`, `pagina`, `filas`) |
| `?vista=galeria` | Vista de galería |
| `?producto=SKU` | Abre el detalle del producto |
| `?nuevo=producto` (o `?nuevo=1`) | Abre «Nuevo producto» (tablero) y se quita de la dirección |
| `?editar=SKU` | Abre la edición de ese producto (o su ficha técnica, según el permiso) y se quita |
| `?pestana=categorias` · `?nuevo=categoria` | Categorías (filtros con prefijo `c_`: `c_q`, `c_productos`, `c_especificaciones`) · abre «Nueva categoría» |
| `?pestana=especificaciones` · `?nuevo=especificacion` | Especificaciones (prefijo `e_`: `e_categoria`, `e_q`, `e_tipo`, `e_uso`) · abre «Nueva especificación» |
| `/panel/toma-fisica` | Toma física. Filtros: `q`, `categoria` (nombre), `ubicacion` (posición), `diferencia` (`con`/`sobrante`/`faltante`/`cuadra`), `contador`; `vista=pendientes`; `almacen=<código>` |

Enlaces a otros módulos (solo direcciones): `/panel/stock?ficha=SKU` (ficha y kardex, M5) y `/panel/movimientos` (después
de contabilizar). El módulo Stock (M5) enlaza «Editar en el catálogo» con `/panel/catalogo?q=SKU`: funciona (filtra por el
SKU); `?editar=SKU` abriría directamente el editor (ver «Pendientes» 7).

## Casos de uso usados

Catálogo — consultas: `GetCatalogOptionsQuery {}`, `GetCatalogQuery { categoryCode, specFilters }` (todo: ambos `null`;
filtrado: categoría y/o `[{ code, values: [valor], min: null, max: null }]`), `SearchTechProductsQuery { text: null,
categoryCode: null, platform: null, filters: null, onlyInStock: false, max: 2000 }` (marca, plataformas, serie, garantía,
disponible), `GetSpecDefinitionsQuery { categoryCode }`, `GetSpecFacetsQuery { categoryCode }`, `GetProductImagesQuery
{ variantIds }` (solo los de la página o del producto abierto), `GetProductTechQuery { sku }`, `GetWorkspaceQuery {}` y
`GetBinsQuery { warehouseCode }` (posiciones del almacén de trabajo). Comandos: `SaveProductCommand` (todos los
parámetros; también para activar/desactivar), `SetProductImageCommand { sku, content (base64), contentType, fileName }`,
`RemoveProductImageCommand { sku }`, `SaveProductTechCommand { sku, trackSerials, serialKind, warrantyMonths, specs }`,
`SaveCategoryCommand { code, name, parentCode }`, `SaveSpecDefinitionCommand` (todos los parámetros).

Toma física — consultas: `GetOpenPhysicalCountQuery { warehouseCode }` (null = almacén de trabajo), `GetWorkspaceQuery {}`,
`GetBranchesQuery {}` (almacenes de la sucursal activa), `GetProductLookupQuery { includeInactive: false }`, `GetBinsQuery
{ warehouseCode }`, `GetProductCardQuery { skuOrBarcode, take: 1 }`. Comandos: `OpenPhysicalCountCommand { warehouseCode,
countDate, notes }`, `RecordCountCommand { physicalCountId, sku, binCode, countedQuantity, lotNumber: null }`,
`RemoveCountCommand { physicalCountId, sku, binCode, lotNumber }` (null para el lote por defecto `SIN-LOTE`, como el
escritorio), `PostPhysicalCountCommand { physicalCountId, confirmed: true }`, `CancelPhysicalCountCommand { physicalCountId }`.

## Pendientes

1. **Kit — el clic de una opción del menú «⋯» llega también a la fila** (el aviso recibido; ya anotado por M5). Resuelto
   dentro del módulo con la guarda `pickingAction`. Arreglo para todos: en `fromInteractive` (`kit/aria.ts`) devolver
   `true` si el objetivo está dentro de `[role="menu"]` aunque no esté en el contenedor, o `event.stopPropagation()` en el
   `onClick` de cada opción de `RowActions`.
2. **Servidor — la categoría madre no llega a la web.** `GetCatalogOptionsQuery` devuelve las categorías sin su madre, así
   que la pestaña «Categorías» no puede mostrar el árbol ni los productos de las subcategorías (los cuenta la categoría con
   sus subcategorías solo al FILTRAR, por el servidor). Sugerido: `ParentCode` (y `Depth`) en `OptionItem` de las
   categorías, o `GetCategoryTreeQuery` (`inventory.stock.view`) → `[{ code, name, parentCode, products, specs }]`.
3. **Servidor — no hay una operación que liste las claves del armador** (`CompatibilityKeys.All`). La lista desplegable
   «Clave del armador de PC» ofrece las claves que YA usa alguna especificación (y la de la que se edita), con su nombre
   en español; una clave nueva todavía sin usar no se puede elegir desde la web. Sugerido: `GetCompatibilityKeysQuery`
   (`catalog.specs.manage`) → `string[]`, o las claves dentro de `GetSpecDefinitionsQuery`.
4. **Servidor — la ficha resumida es solo de productos activos.** `SearchTechProductsQuery` filtra `IsActive`: los
   inactivos no traen marca, plataformas, serie ni disponible (la lista muestra «—» y los filtros de marca, serie y
   plataforma no los incluyen). Sugerido: `IncludeInactive` en `SearchTechProductsQuery`, o `Brand`, `TrackSerials`,
   `SerialKind` y `WarrantyMonths` en `CatalogItem`.
5. **Servidor — el margen se calcula en la página (guía).** Como el escritorio (`CatalogProduct.Margin`, `VatRules.NetOf`
   con la tasa y la convención que manda el servidor en `GetCatalogOptionsQuery`), la web calcula el precio sin IVA y el
   margen para mostrarlos y para «Aplicar un margen»; el servidor no los usa. Sugerido para no repetir la regla:
   `NetPrice` y `Margin` en `CatalogItem`.
6. **Servidor — `GetProductCardQuery` sin almacén** (ya anotado por M5): «En el sistema» solo se muestra si la toma es del
   almacén de trabajo; si se cuenta otro almacén, la diferencia se ve al registrar (la calcula el servidor en la planilla).
7. **Otro módulo — enlace de Stock al catálogo.** `modules/stock/StockDetailPanel.tsx` usa `/panel/catalogo?q=SKU`
   («Editar en el catálogo»): filtra por el SKU; con `?editar=SKU` abriría el editor directamente (o la ficha técnica,
   según el permiso). Cambio sugerido al dueño de M5.
8. **Pruebas ajenas — `InicioPage.test.tsx` («con los módulos del proyecto»).** Espera que el ADMIN vea en Administración
   solo los botones de Actividad y que el CAJERO no tenga funciones ni estadísticas: con los módulos de todos los
   paquetes ya no es así (en esta sesión fallaron por Usuarios, Integraciones, Configuración y las estadísticas de los
   módulos de Inventario —incluida «Estado del catálogo», que el CAJERO ve porque consulta el stock—). La integración final
   debería actualizar esas expectativas.
9. **Modo mock.** No atiende estas operaciones: con `VITE_API_URL=mock` las pantallas muestran el estado de error. Por
   eso no hubo recorrido en el navegador; las pantallas se probaron con el servidor simulado de cada prueba.
10. **Toma física — productos con serie.** El servidor rechaza generar ajustes si un producto serializado tiene
    diferencia (regla T-02: se ajusta con «Movimientos» indicando las series). La web lo avisa en la confirmación y muestra
    el rechazo; no sabe de antemano cuáles llevan serie (`GetProductLookupQuery` no lo trae). Sugerido: `TrackSerials` en
    `ProductLookupItem` o en `PhysicalCountSheetLine`, para marcarlos en la planilla.

## Pruebas

Desde `src/3. Presentation/MINV.WebCatalog` (salida redirigida a archivo, como pide la regla):

- `npx vitest run src/4-presentation/panel/modules/catalogo src/4-presentation/panel/modules/toma-fisica` →
  **Test Files 7 passed (7) · Tests 78 passed (78)**:
  - `catalogo/catalog.test.ts` 18, `catalogo/specs.test.ts` 8 y `catalogo/image.test.ts` 4 (funciones puras: margen con la
    convención de Bolivia y «Aplicar margen», cruce con la ficha resumida, cada filtro de la página, opciones de marca,
    categoría, plataforma y condición salidas de las especificaciones, `?espec=` codificado, `specFilters` exactos,
    facetas, validación y contenido EXACTO de `SaveProductCommand`, activar/desactivar, EAN-13, ficha técnica por tipo,
    obligatorias y números, `SaveProductTechCommand`, `SaveSpecDefinitionCommand`, categorías y `SaveCategoryCommand`,
    imagen en base64, reducción a JPEG hasta 1 MB y sus errores).
  - `catalogo/CatalogPage.test.tsx` 14 (lista con pedidos exactos y resumen plegado; filtros de la página en la dirección;
    categoría, especificaciones y condición por el servidor; galería que pide solo las imágenes de la página y ordena;
    detalle con ficha técnica e imagen y enlace a Stock; la GUARDA del menú «⋯»; error con «Reintentar»; CSV; alta
    completa producto → imagen → ficha con los contenidos exactos; edición desde `?editar=` con el reintento que corrige el
    mismo producto; quitar la imagen; desactivar con confirmación y activar; CONSULTA sin crear ni editar; BODEGA solo la
    ficha técnica).
  - `catalogo/CatalogAdmin.test.tsx` 10 (categorías: lista y filtros con prefijo, nueva/subcategoría/cambio de nombre
    exactos, código repetido y rechazo del servidor en el diálogo, «Ver sus productos»; especificaciones: con heredadas,
    filtros, crear y modificar exactos, quién ve cada botón; la estadística; la definición del módulo y sus acciones por
    permiso).
  - `toma-fisica/count.test.ts` 8 y `toma-fisica/PhysicalCountPage.test.tsx` 16 (iniciar con fecha validada y contenido
    exacto; elegir almacén; planilla con diferencias, progreso y filtros en la dirección; LECTOR de códigos con Enter, guía
    y `RecordCountCommand` exacto; validación y rechazo del servidor en el formulario; corregir y quitar con confirmación;
    pendientes y «Contar»; generar ajustes con el resumen y `confirmed: true`, y su rechazo; anular; imprimir la planilla;
    CSV; error con «Reintentar»; VENTAS sin acceso; BODEGA con todo; definición del módulo).
- `npx vitest run src/architecture.test.ts` → **pasa** (sin importaciones entre módulos, solo el conjunto del panel, sin
  tipos del servidor a mano, sin HTML inyectado ni recursos de otros dominios).
- `npx oxlint src/4-presentation/panel/modules/catalogo src/4-presentation/panel/modules/toma-fisica` y `npx oxlint` →
  **0 diagnósticos** (salida vacía, código 0).
- `npx tsc -b --noEmit` → **0 errores en todo el proyecto** (última corrida; en una intermedia hubo 2 errores AJENOS en
  `modules/libros/module.tsx`, paquete que estaba en construcción).
- `npx vitest run` (toda la web, última corrida) → **Test Files 117 passed · 2 failed (119) · Tests 1500 passed · 3 failed
  (1503)**. Mis 7 archivos pasan. Los 3 que fallan son AJENOS: `app/routeTable.test.tsx` («el personal entra al panel»:
  espera 1 s el título del tablero, que con la suite completa en paralelo tarda más) y los 2 de `InicioPage.test.tsx` de
  «Pendientes» 8 (expectativas escritas cuando no había módulos). Con la suite completa en paralelo la primera pantalla
  tarda: mis pruebas de pantalla llevan 30 s por prueba y 5 s por espera.
