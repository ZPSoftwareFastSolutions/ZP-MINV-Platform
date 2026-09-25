# Historial de cambios · M-INV

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/). Versionado semántico.

## [3.1.0-alpha.1 · base de datos local] · 2026-09-25 · rama `Inventario-V3.-BaseDeDatosLocal`

Tema: **todo funcionando con una base de datos PostgreSQL LOCAL** (97 tablas en 5FN), **datos de prueba** con usuarios
de cada rol, **muchas más funciones por rol** (punto de venta, ventas, clientes, compras, proveedores, reportes,
contabilidad, usuarios) e **imágenes de cada producto**. Misma versión 3.1.0-alpha.1, construida sobre `Inventario-V3.1`.

### Agregado

- **`tools/bd_local.ps1`**: PostgreSQL 16 portátil en `%LOCALAPPDATA%\M-INV` sin permisos de administrador (`instalar`,
  `iniciar`, `detener`, `estado`, `recrear`, `-Autoiniciar`, `-SinDatos`): clúster UTF-8 con `scram-sha-256`, roles
  `minv_owner` (clave aleatoria) y `minv_app`, base `minv`, migraciones y datos de prueba. Claves solo en el equipo.
- **`minv datos-prueba`** (`LocalDataSeeder`): empresa **MINV · Ferretería El Constructor S.R.L.**, 9 usuarios de los 6
  roles con contraseñas aleatorias (archivo `usuarios-prueba.txt`, nunca versionado), 8 categorías con 24 posiciones,
  8 proveedores, 29 clientes, 61 productos con imagen, precio, costo, mínimo, máximo y código EAN-13, y 60 días de
  operación simulada con los casos de uso reales: dos cajas (apertura, ventas en 4 medios de pago, arqueo y cierre),
  anulaciones, mermas, pedido sugerido → aprobación → recepción, depósitos, gastos del mes y pagos a proveedores.
- **Imágenes de productos**: tabla `catalog.product_images` (bytea PNG/JPEG ≤ 1 MB, RLS, única por variante; migración
  `ProductImages`), 38 ilustraciones propias (`tools/generar_imagenes_productos.py`, recursos incrustados) asignadas por
  nombre a los datos de prueba y a la demostración; `SetProductImageCommand`, `RemoveProductImageCommand`,
  `GetProductImagesQuery`.
- **Casos de uso nuevos** (con permisos, validación y auditoría): catálogo (`GetCatalogQuery`, `GetCatalogOptionsQuery`,
  `SaveProductCommand` con posición, `SaveCategoryCommand`); clientes y proveedores (`Get/Save…`); compras
  (`CreatePurchaseOrderCommand`, `CreateSuggestedPurchaseOrdersCommand`, `Approve…`, `Cancel…`, `ReceivePurchaseOrderCommand`
  con costo promedio ponderado y asiento); ventas (`GetPosStateQuery`, `GetSellableProductsQuery`, `CheckoutCommand` con
  factura, IVA incluido, pago y asiento, `GetSalesQuery`, `GetSaleLinesQuery`, `VoidSaleCommand` con devolución y asiento
  inverso); reportes (`GetSalesReportQuery`, `GetPurchasesReportQuery`, `GetMovementsReportQuery`); contabilidad
  automática con plan de cuentas jerárquico (`ChartOfAccounts`, `JournalPoster`, `GetChartOfAccountsQuery`,
  `GetJournalQuery`, `GetIncomeStatementQuery`, `CreateJournalEntryCommand`, `CreateAccountCommand`); administración
  (`GetUsersQuery`, `SaveUserCommand`, `ResetUserPasswordCommand`, `GetRolesQuery`, `Get/UpdateCompanySettings…`).
- **Pantallas nuevas del escritorio** (todo lo que viene de una lista, en combos): **Catálogo** en galería con imágenes o
  lista y editor lateral (imagen, categoría + nueva, unidad, posición, proveedor, costo, precio con combo de margen,
  mínimo, máximo, código de barras); **Punto de venta** (caja, tarjetas con imagen, carrito con descuentos, cliente y
  medio de pago, vuelto, referencia, ticket en pantalla o ESC/POS, arqueo); **Ventas** (período, filtros, detalle y
  anulación con motivo); **Clientes**; **Órdenes de compra** (desde el pedido sugerido, aprobar, recibir, anular);
  **Proveedores**; **Reportes** (ventas, compras, movimientos, inventario, con gráficos y ranking agrupable);
  **Contabilidad** (estado de resultados, libro diario, plan de cuentas, asientos con plantillas); **Usuarios y roles**
  (alta, rol, contraseña temporal, restablecer, matriz de funciones, parámetros de la empresa).
- **Stock en galería** (tarjeta con la imagen de cada producto) además de la tabla; la ficha del producto muestra la
  imagen y lleva al editor del catálogo.
- Menú por secciones (General, Ventas, Inventario, Compras y reposición, Análisis, Administración) según el rol; cuadros
  con dato a completar (motivo, efectivo contado, documento) y con contraseña para copiar.
- Pruebas: datos de prueba en memoria (coherencia contable, stock, ventas, compras, permisos por rol), pantallas de
  negocio con la demostración (POS → cobro → anulación, compras sugeridas → aprobar → recibir, catálogo, reportes,
  contabilidad, usuarios) y datos de prueba contra PostgreSQL real.

### Cambiado

- Matriz de permisos: nuevos `reports.view`, `sales.customers.manage` y `sales.view`; Gerencia gana contabilidad y
  compras, Bodega compras y reportes, Ventas y Cajero el punto de venta, clientes y ventas, Consulta los reportes.
- Cada empresa nueva recibe el plan de cuentas completo (activo, pasivo, patrimonio, ingresos, costos y gastos).
- La venta de caja se asocia a la sesión y la recepción a su orden (arcos exclusivos `ck_sales_orders_origen` y
  `ck_goods_receipts_origen` que PostgreSQL exige y la memoria no validaba).
- La demostración completa el catálogo de módulos licenciados y agrega precios de venta: el punto de venta funciona
  también sin base de datos.
- `appsettings.json` propone la empresa `MINV` (la de la base local).

### Corregido

- **Clientes** no cargaba con PostgreSQL («Numeric value does not fit in a System.Decimal»): el total por cliente se
  calculaba en SQL como `cantidad × precio × (1 − descuento / 100)` y la división dejaba numéricos de 34 cifras (el
  máximo de .NET es 28). Ahora suma los pagos de las facturas emitidas. La prueba contra PostgreSQL recorre todas las
  consultas de las pantallas con 20 días de datos para que no vuelva a pasar.

### Verificado

- `tools/build_v3.ps1 -Capturas -Publicar` con `MINV_TEST_PG`: 135 pruebas (7 contra PostgreSQL real), migraciones al
  día, `scripts/db_init.sql` con 97 tablas, 46 capturas (claro y oscuro) y `M-INV.exe` publicado.
- `minv verify --codigo MINV`: 97 tablas, 7 libros append-only, RLS en 95 tablas, conservación sin descuadres; partida
  doble cuadrada en los 900+ asientos de los datos de prueba.

## [3.1.0-alpha.1] · 2026-09-25 · rama `Inventario-V3.1`

Tema de la versión: **cliente de escritorio completo, bonito e intuitivo**. La interfaz de la V3 (tablas tipo Excel)
se reemplaza por una aplicación de escritorio con sistema visual propio, pensada para el trabajo diario de bodega,
ventas y gerencia. El modelo de datos, las reglas y la paridad con la V2.1 no cambian.

### Agregado

- **`M-INV.exe`** con ícono propio, **pantalla de carga** (preferencias, tema y comprobación de PostgreSQL en segundos)
  e **inicio de sesión** rediseñado (estado de la base con «Reintentar», Bloq Mayús, recordar empresa y correo).
- **Modo demostración** sin base de datos: el libro de la V2.1 se migra a una base en memoria con el mismo importador
  (reglas, poka-yoke y paridad) y se entra con cualquiera de sus usuarios y roles. Contraseña aleatoria por ejecución;
  el reloj de la demostración se ubica en el día de los datos de la V2.1.
- **Ventana principal**: menú lateral por secciones según los permisos del rol (contraíble), búsqueda global de
  productos con autocompletado sin tildes (`Ctrl+K`), campana de alertas, avisos flotantes, confirmaciones dentro de la
  ventana, menú de la cuenta, tema **claro/oscuro/según Windows** y atajos de teclado.
- **Pantallas**: Inicio (indicadores, próximo paso según el rol, entradas y salidas de 14 días, semáforo en dona, alertas
  urgentes, más vendidos, últimos movimientos, actividad); Stock (chips por estado, categoría, orden, barra de nivel,
  exportación a Excel); Registrar movimiento (tipo → producto → cantidad, vista previa y **poka-yoke rojo sangre** de la
  V2.1, Enter para registrar, historial de la sesión); **Toma física** (iniciar, contar, quitar, anular y generar ajustes
  con confirmación); Alertas; Pedido sugerido por proveedor (copiar para correo o WhatsApp, exportar); **Ficha del
  producto** con gráfico del saldo y kardex; Actividad con filtros; Configuración (tema, impresora ESC/POS con **página
  de prueba**, prueba del escáner, sesión, permisos, conexión) y Ayuda (guías, atajos, semáforo).
- **Cambio de contraseña** (obligatorio si el administrador la asignó) y **cierre de sesión** que vuelve al inicio.
- Lector de códigos en modo teclado en todas las pantallas (elige el producto o abre su ficha) sin ensuciar los campos.
- Casos de uso nuevos: `GetWorkspaceQuery`, `GetProductLookupQuery`, `GetBinsQuery`, `GetMovementTypesQuery`,
  `GetProductCardQuery` (ficha y kardex con saldo acumulado), `GetRecentMovementsQuery`, `GetMovementTrendQuery`,
  `GetOpenPhysicalCountQuery`, `RemoveCountCommand`, `CancelPhysicalCountCommand`, `ChangePasswordCommand` y
  `LogoutCommand` (auditados los que escriben).
- `DatabaseProbe` (comprobación rápida de PostgreSQL sin mostrar la contraseña), `DemoWorkspace` / `DemoClock` y
  `AddMinvDemoInfrastructure` (EF Core InMemory 8.0.31), `ReceiptPrinters` y `ReceiptRenderer.RenderTestPage`.
- **`M-INV.exe --capturas <carpeta>`**: recorre todas las pantallas (claro, oscuro y dos roles) y guarda imágenes;
  `tools/build_v3.ps1 -Capturas` las deja en `docs/product/capturas/v3.1` y `-Publicar` genera el ejecutable con
  `tools/publicar_escritorio.ps1` (`dist/`, dependiente del runtime o `-Autocontenido`).
- Guía de la interfaz `docs/product/escritorio-v3.1.md`; generador del ícono `tools/generar_icono_escritorio.py`.

### Cambiado

- El ejecutable se llama `M-INV.exe` (antes `MINV.DesktopClient.exe`); versión 3.1.0-alpha.1.
- Cada acción de la interfaz es una unidad de trabajo: el contexto de datos descarta lo rastreado y las consultas se
  envían de a una (`SerialMediator`), así nunca se decide con existencias leídas antes de que otra caja las cambiara.
- La auditoría clasifica como *Rechazado* (no *Falló*) una credencial incorrecta.
- `docs/deployment/inicio-rapido-v3.md`: algoritmo rápido de 3 pasos con la demostración y distribución del ejecutable.

### Verificado

- `tools/build_v3.ps1 -Capturas -Publicar` sin fallas: compilación Release sin advertencias; **128 pruebas** (dominio
  47, aplicación 10, hardware 10, infraestructura 44 con 5 que requieren PostgreSQL, **cliente de escritorio 17** con la
  demostración real); migraciones al día; `db_init.sql` sin cambios; 24 capturas revisadas; `M-INV.exe` publicado
  (11,3 MB) y probado hasta el inicio de sesión.
- Pendiente: probar contra un PostgreSQL real (triggers, RLS y vistas) con `MINV_TEST_PG`.

## [3.0.0-alpha.1] · 2026-09-25 · rama `Inventario-V3`

Tema de la versión: **fundación de M-INV V3** (escritorio + PostgreSQL). Transición desde Excel (V2.1) a una
arquitectura cliente-servidor para punto de venta y bodegas de alta concurrencia. Es una versión *alpha*: la base de
datos, el dominio, los casos de uso de inventario, la migración desde la V2.1 y la infraestructura están completos y
probados; las pantallas de compras, ventas POS completas y contabilidad llegan en las siguientes iteraciones.

### Agregado

- **Solución .NET 8** `MINV.sln` en Clean Architecture: `MINV.Domain` (sin dependencias), `MINV.Application`
  (MediatR 12.5, FluentValidation), `MINV.Infrastructure` (EF Core 8 + Npgsql 8), `MINV.Hardware`,
  `MINV.DesktopClient` (WPF/MVVM) y `MINV.Cli` (`minv`). Versiones centralizadas (`Directory.Packages.props`) y
  advertencias como errores.
- **Base de datos PostgreSQL de 96 tablas en 7 esquemas** (iam 14, catalog 18, warehouse 10, inventory 14,
  purchasing 11, sales 19, accounting 10), normalizada hasta 5FN: árbol de categorías con tabla de clausura, variantes
  y atributos, códigos de barras múltiples, conversiones de unidades, topología sucursal › almacén › zona › pasillo ›
  estantería › nivel › posición, lotes con caducidad, series, reservas, tomas físicas, traslados, compras, ventas/POS
  con direcciones normalizadas (país › estado › ciudad › código postal), pagos, costos promedio, impuestos con vigencia
  y contabilidad de partida doble.
- **Multi-tenant**: `TenantId` en toda entidad, filtros globales, FK compuestas `(tenant_id, x_id)` y Row Level
  Security. **Concurrencia optimista** con `xmin`. **Append-only** en movimientos, auditoría, accesos, caja, pagos,
  tipos de cambio y costo promedio (EF Core + triggers). **Auditoría** de cada comando con su resultado.
- **Dominio rico**: `StockLevel` (movimientos, poka-yoke, reservas, ajuste por conteo), `Product`/`ProductVariant`
  (variantes, EAN con dígito de control, empaques, impuestos), `PhysicalCount` (CF-AAAAMMDD, todo o nada),
  `PosSession`, `JournalEntry` (cuadre), `UserCredential` (bloqueo por intentos), `StockRules` y `StockProjection`
  (semáforo, alertas, cobertura, ranking y pedido: tercera implementación de las reglas de la V2.1).
- **Importador V2.1 → V3** (`minv import-v21`): lector .xlsx sin dependencias, plan de migración a través del
  dominio, rechazos y actividad a la auditoría, toma física en curso, y **verificación de paridad** con la instantánea
  de la V2.1 (idéntica en el libro de demostración).
- **Licencias por módulo comercial** (`iam.modules` sembrado con la matriz de valor: motor de datos Bs 8.500, cliente
  de escritorio Bs 6.000, POS y hardware Bs 4.500, RBAC Bs 3.000, SLA Bs 800/mes) y `[RequiresModule]`.
- **Login cifrado** (PBKDF2-SHA256, 600.000 iteraciones), sesiones, equipos autorizados y registro de accesos.
- **Hardware POS**: documentos ESC/POS, comprobante de venta, impresoras serie, red y USB (cola de Windows en RAW),
  lector serie y detector de escáner en modo teclado.
- **Migraciones EF Core** (`InitialCreate`, `GuardsRlsAndViews`) y `scripts/db_init.sql` generado (idempotente).
- **Pruebas**: 102 (dominio 47, aplicación 10, hardware 8, infraestructura 37 de las cuales 5 requieren PostgreSQL
  real vía `MINV_TEST_PG`).
- Documentación: `docs/database/ERD-MINV-V3.md`, `.claude/v3-architecture-rules.md`,
  `.claude/database-migration-guide.md`, `docs/deployment/inicio-rapido-v3.md`, `tools/build_v3.ps1`.

### Decisiones

- **Marco .NET 8** como pidió la especificación. Aviso: .NET 8 deja de tener soporte el 10 de noviembre de 2026; el
  marco está centralizado en `Directory.Build.props` para pasar a .NET 10 LTS cambiando una línea (y las versiones de
  EF Core/Npgsql).
- **MediatR 12.5.0** (última versión con licencia Apache 2.0; la 13 exige licencia comercial).
- **`RowVersion` = `xmin`** de PostgreSQL (equivalente al `rowversion` de SQL Server, sin columna extra).
- **Redundancias controladas y documentadas**: `tenant_id` (aislamiento) y el estado materializado de
  `stock_levels` (control de concurrencia), verificado por la vista `v_conservation_breaches`.

## [2.1.0] · 2026-09-25 · rama `Inventario-V2.1`

Tema de la versión: **M-INV colaborativo completo**. Sobre la base de la 2.0 (captura por usuario, bitácoras
fragmentadas, instantánea a demanda) se agregan las funciones que faltaban para operar el día a día en Microsoft 365
sin salir del libro, todas con la misma regla de oro: nadie escribe en la celda de otro.

### Agregado

- **Portada de Gerencia** (`00_PORTADA_GERENCIA`): próximo paso, accesos (stock y cobertura, alertas, pedido sugerido,
  actividad), 8 indicadores (valor, requieren acción, pedido, movimientos del mes, unidades vendidas en 30 días,
  cobertura mediana, bloqueos del mes, usuarios), gráficos nativos (unidades despachadas por mes, valor por categoría),
  los 10 más vendidos en 30 días y la actividad de cada usuario en el mes.
- **Consulta por usuario** (`17_CONSULTA`): una fila por persona (ADMIN, BODEGA, VENTAS) con su propio selector de
  producto: disponible exacto en vivo, semáforo, mínimo/máximo, sugerido, proveedor, ubicación, último movimiento
  (fecha, tipo y quién), entradas y salidas de 30 días, cobertura y valor. Nuevo `OrdenConsulta` en `02_USUARIOS`.
- **Toma física colaborativa** (`13_CONTEO`): cada producto tiene su celda de conteo; vista previa de la diferencia y
  del ajuste; fecha y confirmación `SI`. Nuevo script **`GenerarAjustesConteo.ts`**: valida todo, compara contra el
  stock exacto, registra AJUSTE (±) o SALDO INICIAL en una sola inserción (`addRows`), vuelve a verificar los ajustes
  negativos y limpia solo los conteos procesados.
- **Pedido sugerido por proveedor** (`18_PEDIDO`): instantánea que escribe `RecalcularStock` con cantidad a pedir,
  subtotal, días de entrega, fecha estimada y contacto del proveedor, lista para filtrar e imprimir.
- **Registro de actividad** (`14_ACTIVIDAD`): cada ejecución de un script (registros, bloqueos, rechazos, recálculos,
  conteos, diagnósticos) con ID, correo, nombre, script, resultado y detalle. Indicadores `kpiEjecuciones`,
  `kpiBloqMes` y `txtUltimaEjecucion`.
- **Resumen diario** `ResumenDiario.ts` (solo lectura) para un flujo programado de Power Automate: asunto, HTML, texto e
  indicadores del día calculados con el stock exacto.
- **15_STOCK**: columnas `Salidas30d`, `CoberturaDias` y `RankSalidas30d` (misma regla en Python y TypeScript).
- **Portadas**: Bodega con *Toma física*; Ventas con *Consultar producto* y *Stock completo*; barra de navegación de 10
  destinos en todas las hojas; próximo paso de Bodega con el conteo en curso.
- **Guía de acceso paso a paso** `docs/deployment/inicio-rapido.md` (demo local, demo en la nube, producción, uso diario
  por rol, Power Automate y solución de problemas) y `99_AYUDA` ampliada (cómo entrar, consulta, toma física, pedido,
  actividad, gerencia, resumen diario).
- **Verificación de tipos** opcional en `tools/build_v2.ps1`: cada script se compila con TypeScript estricto contra
  `tests/office-scripts/excelscript-tipos.d.ts` si hay `tsc` (PATH o `MINV_TSC`).
- **Pruebas**: de 12 a 22 (actividad, toma física, resumen diario, paridad del pedido y de las columnas nuevas);
  el simulador de `ExcelScript` admite `Table.addRows`.

### Cambiado

- `lib/comun.ts`: `registrarActividad`, `agregarFilas` (lote atómico), `saldos` (stock exacto de todos los productos en
  una pasada), `estadoDe` (compartido), `fechaTexto`/`fechaCompacta`, `nombreVisible`; `registrar` audita también los
  intentos sin fila de captura.
- `RecalcularStock.ts` escribe tres instantáneas (stock de 20 columnas, alertas y pedido) y deja su ejecución en
  `14_ACTIVIDAD`; `DiagnosticoInstalacion.ts` revisa las 16 hojas, 16 tablas y 9 nombres de la 2.1, la fila de consulta
  y la contraseña de cada hoja que escriben los scripts.
- `tools/verify_minv_v2.ps1`: 21 hojas; nuevas comprobaciones independientes de cobertura, ranking, pedido, conteo,
  consulta, actividad y Gerencia, con pruebas en vivo (escribir y recalcular).
- Anchos de columna revisados para que los encabezados con filtro no se corten.

### Corregido

- Verificador: `[math]::Max(0, $x)` de PowerShell redondeaba a entero un stock decimal (usaba la sobrecarga `Int32`);
  ahora `[math]::Max(0.0, $x)`.

## [2.0.0] · 2026-09-25 · rama `Inventario-V2`

Tema de la versión: **M-INV colaborativo en Microsoft 365**. Un solo libro en SharePoint/OneDrive que Bodega y Ventas
usan a la vez desde Excel para la web, sin colisiones de coautoría, con auditoría por correo y Office Scripts.

### Agregado

- **Libro colaborativo** `src/M-INV_V2_Colaborativo.xlsx` (Core con demo) y
  `releases/M-INV_V2_Colaborativo_Produccion.xlsx` (Release), generados por `tools/build_minv_v2.py` (`tools/minv2/`).
- **Escritura fragmentada**: `10A_ENTRADAS` (Bodega: entrada, saldo inicial, ajustes) y `10B_SALIDAS` (Ventas). En cada
  una, **captura por usuario** (una fila por persona según `02_USUARIOS`, con validación y disponible exacto en vivo) y
  **bitácora oficial** que solo escriben los scripts (`Table.addRow`, ID sin contador compartido, `Usuario_O365` y
  `Timestamp` en columnas ocultas).
- **Poka-yoke**: una salida (o ajuste negativo) mayor que el disponible se tiñe de rojo sangre con texto blanco tachado
  y el script bloquea la consolidación; si otra persona se adelanta, el registro propio queda «✖ Rechazado» y no suma.
- **Lectura a demanda**: `15_STOCK` y `16_ALERTAS` son instantáneas de valores que reconstruye `RecalcularStock.ts`;
  las portadas muestran quién y cuándo calculó y cuántos movimientos hay después.
- **Portadas por rol**: `00_PORTADA_BODEGA` (registrar entrada, registrar ajuste, alertas de stock crítico) y
  `00_PORTADA_VENTAS` (registrar salida, consultar disponibilidad), con indicadores, gráficos y últimos movimientos.
- **Control de acceso**: `02_USUARIOS` (correo de Microsoft 365 y rol ADMIN/BODEGA/VENTAS/CONSULTA) validado por los
  scripts; «Permitir editar rangos» en las capturas (contraseña opcional por rol); guía de SharePoint.
- **Office Scripts** (`src/office-scripts/`): `RegistrarEntrada`, `RegistrarSalida`, `RecalcularStock` y
  `DiagnosticoInstalacion`, con bloque común (`lib/comun.ts`) sincronizado por `tools/office_scripts.py`, que también
  genera las versiones instalables con la contraseña (`build/office-scripts/`).
- **Pruebas sin Excel**: simulador de la API `ExcelScript` y 12 pruebas que ejecutan los scripts reales
  (`tests/office-scripts/`), incluida la paridad de `RecalcularStock` con el generador.
- **Verificación en Excel** `tools/verify_minv_v2.ps1` y ciclo completo `tools/build_v2.ps1`.
- Documentación: `.claude/v2-concurrency-rules.md`, `docs/deployment/sharepoint-rbac-policies.md`,
  `docs/architecture/data-dictionary-v2.md`, `src/office-scripts/README.md`.

### Cambiado

- `tools/minv/master.py`: la tabla de catálogo y proveedores se separa de su decoración para reutilizarla en la V2.
- `tools/minv/postprocess.py`: nueva guarda que aborta el build si una fórmula tiene paréntesis o llaves desbalanceados
  (Excel rechazaba el libro completo).

### Decisiones

- La especificación pedía cálculo **Manual** para 15_STOCK: en Excel el modo de cálculo es por libro y por sesión (no por
  hoja) y congelaría el disponible y el poka-yoke. Se implementó la instantánea a demanda, que cumple el objetivo sin ese
  costo; `RecalcularStock` además dispara un recálculo completo.
- «Permitir editar rangos» no admite correos en Excel para la web: el correo lo valida el script contra `02_USUARIOS` y
  los rangos admiten contraseña por rol.

## [1.2.0] · 2026-09-25 · rama `Inventario-V1.2`

Tema de la versión: **más funciones y un uso más intuitivo para todos los perfiles** (bodega, compras, gerencia y
administración), sin romper el modelo CQRS.

### Agregado

- **Dos ediciones** generadas desde el mismo código:
  - **Estándar** (`.xlsx`, sin macros): todo lo nuevo de lectura y consulta.
  - **Plus** (`.xlsm`): formulario guiado, sellado de la bitácora, ajustes automáticos del conteo y atajos de doble clic.
- **Portada renovada**: asistente **«Próximo paso»** que indica qué hacer ahora (errores, catálogo vacío, saldo
  inicial, conteo en curso, agotados, reposición, inventario sin rotación) con un botón que lleva al lugar exacto;
  8 mosaicos con iconos (Registrar, Consultar, Stock, Alertas, Pedido, Conteo, Catálogo, Guía); tarjeta
  **Sin rotación**; indicador de integridad de la bitácora.
- **Barra de navegación** con iconos en todas las hojas visibles (la hoja activa resaltada).
- **`04_PROVEEDORES`** (maestro nuevo): contacto, teléfono, correo, días de entrega; productos y alertas por proveedor.
  `05_PRODUCTOS` gana la columna `Proveedor` (lista validada).
- **`12_REGISTRO`** (Plus): formulario guiado con búsqueda por texto, vista previa del stock antes → después, semáforo
  antes → después y 7 validaciones en vivo. El botón **REGISTRAR** escribe en la bitácora, comprueba el `Estado` y
  sella la fila; conserva tipo y responsable para registrar varios movimientos seguidos.
- **`13_CONTEO`** (toma física): conteo contra sistema, diferencia en unidades y en dinero, resultado
  (cuadra / sobrante / faltante) y ajuste sugerido. En Plus, **Generar ajustes** registra y sella los `AJUSTE (+/-)`.
- **`17_KARDEX`** (consulta de producto): búsqueda por texto, ficha (stock, estado, valor, último movimiento, días sin
  movimiento), gráfico de evolución del saldo e historial de los últimos 100 movimientos.
- **`18_PEDIDO`** (pedido sugerido de compra): agrupado por proveedor y priorizado, cantidad a pedir hasta el máximo,
  costo y subtotal; filtro por proveedor con contacto y fecha estimada de entrega; listo para imprimir.
- **`99_AYUDA`**: «¿Qué necesita hacer hoy?» por perfil y lista de **primeros pasos** que se marca sola.
- `15_STOCK`: columnas `Proveedor` y `DiasSinMov` (inventario inmovilizado en ámbar); `16_ALERTAS`: columna `Proveedor`.
- Parámetros `cfgDiasSinRotacion` (60 días), `cfgEdicion` y `cfgSelladoHasta`.
- Edición Plus: **sellado** de la bitácora (las filas registradas quedan bloqueadas y en gris al registrar desde el
  formulario, al generar ajustes y al guardar), **doble clic** (alerta o pedido → formulario de reposición con la
  cantidad sugerida; stock o bitácora → consulta del producto) y aviso visible cuando las macros están deshabilitadas.
- Herramientas: generador modular (`tools/minv/`), `tools/build_xlsm.ps1` generalizado (Core y Release, con 20+
  pruebas automáticas del VBA en Excel), `tools/build_all.ps1` (ciclo completo) y guarda del post-proceso que impide
  referencias estructuradas en formatos condicionales y validaciones.

### Cambiado

- `src/M-INV_V1_Core.xlsm` pasa de variante opcional (V1.1, solo modo app) a **edición Plus** completa; se agrega
  `releases/M-INV_V1_Produccion_Bloqueado.xlsm`.
- Los fuentes VBA (`src/macros/`) pasan a UTF-8; el script los instala con `AddFromString`.
- Datos demo: 6 proveedores ficticios y 2 productos de baja rotación.

### Corregido

- Excel rechazaba el libro completo cuando un formato condicional usaba referencias estructuradas (`tblStock[...]`):
  se reemplazaron por nombres definidos y el generador ahora lo impide (regla R-08).

## [1.1.0] · rama `Inventario-V1`

- Variante `.xlsm` con modo app: la barra de fórmulas se oculta en la portada y se restaura al salir (probado en Excel).

## [1.0.0] · rama `Inventario-V1`

- Primera versión: libro CQRS (bitácora append-only → stock → alertas → tablero), Core con datos demo y Release
  blindado, generador, verificador en Excel real y documentación.
