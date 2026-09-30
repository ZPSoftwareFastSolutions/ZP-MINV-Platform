# Informe del paquete D1 · escritorio WPF de la V7 (tarea 14)

## Resumen

Paquete D1 hecho en la carpeta `ZP-MINV-V7-escritorio`, rama `v7-escritorio`, con commits intermedios (`e4e9bad`, `c248186`,
`a6427dd`, `9fdc3fc`) y el último con este informe. No se tocó la web (`MINV.WebCatalog`), ni la base `minv`, ni PostgreSQL, ni
Docker; no se hizo push. Las capturas salen de la demostración en memoria (sin base local).

1. **Ventas › Reservas** (pantalla nueva, permiso `sales.pcbuild.manage`): carritos `RES-…` y armados `ARM-…` reservados de la web y
   del mostrador, con filtros en listas, detalle (productos, datos de factura, notas, bitácora y correos) y las acciones Vender en
   caja, Liberar (con motivo), Reenviar correo (a otro correo si hace falta), Copiar teléfono, Nueva reserva en mostrador y Exportar
   CSV. Todo con casos de uso existentes: `GetPcBuildsQuery`, `GetPcBuildQuery`, `ReleasePcBuildReservationCommand`,
   `ResendReservationMailCommand`, `ReserveCartCommand`, `GetOutgoingMailsQuery`, `GetSellableProductsQuery`, `GetCustomersQuery`.
2. **Caja**: al cobrar una reserva precarga el comprador con los datos de factura de la reserva si el cajero no escribió otro (y lo
   limpia si se quita la reserva sin haberlo tocado). **Armador › Cotizaciones** lista solo armados (`Kind = Build`).
3. **Inicio simplificado**: «¿Qué querés hacer?» con botones grandes por permisos, agrupados como el menú, y atajos (nueva reserva en
   mostrador, registrar entrada, registrar salida). Los indicadores, gráficos, listas, actividad y el tablero Tecnología están en
   secciones plegables «Ver …» CERRADAS que no leen nada hasta abrirse.
4. **Filtros y exportación** en Stock, Catálogo, Ventas, Clientes, Proveedores, Órdenes de compra, Transferencias, Series,
   Garantías, Documentos fiscales, Actividad y Usuarios: listas desplegables para los atributos, «Limpiar filtros» y «Exportar CSV»
   con un servicio compartido (`Services/CsvExport.cs`: UTF-8 con BOM, «;», neutralización de `= + - @`, tabulador, retorno y
   variantes de ancho completo).
5. **Administración › Correos** (pantalla nueva): la cola de correos de confirmación con filtros por estado, tipo, sucursal y fecha,
   Reenviar, Abrir la reserva y Exportar CSV.
6. **Usuarios**: filtro Tipo de cuenta (Personal por defecto / Clientes web / cuenta técnica de la tienda), estado y sucursal;
   columnas Tipo y Sucursales; el alta no ofrece «Cliente web» ni «Tienda web»; solo el personal se edita. El escritorio no deja
   entrar con una cuenta de cliente web (local, demostración y nube).
7. **Arreglos** de botones sin efecto, detalles que actuaban sobre la fila anterior, filtros que no filtraban, horas UTC, textos
   técnicos o en inglés y avisos que faltaban (sección «Arreglos»).
8. **Documentación**: `docs/product/escritorio-v7.md` (pantalla por pantalla, botones y filtros) y capturas 103 a 110 en
   `docs/product/capturas/v7`.

**Cambio en MINV.Application** (único fuera del escritorio): `Tech/TechDashboard.cs`: «Armados cotizados» (`QuotesOpen`,
`QuotesValue`) y «armados vendidos» (`BuildsSold`, `BuildsSoldValue`) cuentan solo `Kind == Build` (antes sumaban los carritos
`RES-…`). No hay caso de uso nuevo ni permiso nuevo. Prueba: `V7ScreenTests.V7_el_tablero_Tecnologia_cuenta_como_cotizaciones_solo_los_armados`
y siguen pasando las del tablero en `MINV.Infrastructure.Tests` (sección «Pruebas»).

## Pantallas y botones

| Pantalla | Filtros | Botones y acciones |
|---|---|---|
| Inicio | — | Un botón por pantalla del rol (agrupados por sección) + Nueva reserva en mostrador, Registrar entrada, Registrar salida; secciones «Ver indicadores del inventario», «Ver entradas, salidas y semáforo», «Ver alertas urgentes, más vendidos y últimos movimientos», «Ver actividad reciente» (auditoría), «Ver tablero Tecnología» (reportes): Ver/Ocultar y Reintentar |
| Ventas › Reservas (nueva) | tipo, canal, estado, vence (hoy/mañana/vencidas), sucursal, fecha, búsqueda (número, cliente, teléfono, correo, documento) | Vender en caja, Liberar (motivo), Reenviar correo (correo opcional distinto), Copiar teléfono, Nueva reserva en mostrador (cliente, teléfono, correo, productos con buscador, días para recoger, datos de factura), Exportar CSV, Limpiar filtros, Actualizar |
| Administración › Correos (nueva) | estado, tipo de reserva, sucursal, fecha, búsqueda | Reenviar, Abrir la reserva, Exportar CSV, Limpiar filtros |
| Stock | categoría, proveedor, chips de estado y reservas, búsqueda | Exportar CSV, Limpiar filtros; estado vacío también en la galería |
| Catálogo | categoría, estado, proveedor, marca, alerta | Exportar CSV (con Marca, Reservado, Disponible), Limpiar filtros |
| Ventas | período, cliente, medio de pago, cajero, estado, estado fiscal | Exportar CSV (ventas o devoluciones), Limpiar filtros; la búsqueda filtra también Devoluciones |
| Clientes | estado, tipo de comprador | Exportar CSV, Limpiar filtros |
| Proveedores | estado, órdenes abiertas, búsqueda | Exportar CSV, Limpiar filtros; columna Estado; estado vacío |
| Órdenes de compra | estado, proveedor, fecha de la orden, búsqueda | Exportar CSV, Limpiar filtros |
| Transferencias | estado, sucursal de origen, sucursal de destino, fecha, búsqueda | Exportar CSV, Limpiar filtros |
| Series e IMEI | estado, sucursal, garantía (vigente/vencida/sin venta), búsqueda | Exportar CSV, Limpiar filtros (ahora a la vista) |
| Garantías y RMA | chips de estado, sucursal, cobertura, búsqueda | Exportar CSV, Limpiar filtros («Ver todos los casos») |
| Documentos fiscales | período, estado, tipo, sucursal, punto de venta, tipo de emisión, búsqueda | Exportar CSV, Limpiar filtros |
| Actividad | chips de resultado, usuario, acción (en español), fecha, búsqueda | Exportar CSV, Limpiar filtros |
| Usuarios | tipo de cuenta, rol, estado, sucursal, búsqueda | Exportar CSV (sin contraseñas), Limpiar filtros; Editar solo personal |
| Caja | — | «Reserva o armado» (carritos y armados), comprador precargado con los datos de la reserva |
| Armador › Cotizaciones | (los de la V6) | solo armados; bloque «Reserva y contacto» en toda reserva |

Detalle con capturas: `docs/product/escritorio-v7.md`.

## Arreglos

- **Transferencias**: `IsCreating` no se avisaba: el panel «Nueva transferencia» nunca aparecía. «Recibir» usaba el detalle de la
  transferencia anterior mientras llegaba el nuevo (se limpia y se ignora una respuesta vieja). La bitácora mostraba la hora UTC
  (convertidor `LocalTime` nuevo). Anular sin motivo no hacía nada: ahora avisa. «N productos» no se actualizaba al quitar una línea.
- **Garantías y RMA**: las acciones (diagnóstico, reemplazo, entrega…) se aplicaban al caso anterior mientras cargaba el nuevo. Agregar
  una nota vacía no hacía nada y una nota agregada no confirmaba. «Sin garantía» salía en rojo como si hubiera vencido.
- **Series**: al llegar desde un caso RMA o la caja con una serie, los filtros de estado y sucursal podían ocultarla; la trazabilidad
  anterior decidía «Abrir caso» y «Destino» mientras cargaba la nueva; «Limpiar filtros» solo existía en el estado vacío.
- **Documentos fiscales**: «Ver documento» y «Anular» desde Ventas no hacían nada si el documento no estaba en la lista cargada (recién
  emitido o filtrado): ahora recarga y, si no está en la sucursal, avisa. El detalle anterior se quita mientras llega el nuevo.
  Rollo y PDF: mensajes para personas en vez del texto de la excepción.
- **Órdenes de compra**: el detalle de otra orden podía quedar a la vista (respuesta vieja); KPI renombrado; los datos compartidos se
  invalidan al crear, aprobar, anular o sugerir.
- **Ventas**: una venta con factura rechazada por el SIN no se podía anular; «desc. 0 %» en las líneas; columna de reembolso sin orden.
  **Clientes**: el alta con error de facturación perdía el código y el mensaje era técnico.
- **Reservas**: con el detalle abierto las columnas se comprimían hasta cortar el número («RES-…»); ahora se ocultan tipo, canal,
  teléfono y correo mientras el detalle está abierto (`BindingProxy` nuevo en `Views/Converters.cs`).
- **Stock**: el estado vacío no se veía en la galería (la vista por defecto); navegar con un estado antes de la primera carga no filtraba.
- **Inicio**: el estado vacío de las listas no aparecía en la primera carga (`OnPropertyChanged(string.Empty)` tras la primera carga).
- **Registrar movimiento**: al llegar con un producto (Alertas, ficha) la recarga volvía a poner el producto anterior; ahora muestra
  también lo reservado para clientes.
- **Alertas**: «Registrar entrada» se veía deshabilitado para Ventas sin explicación: ahora solo lo ve quien puede registrar.
- **Actividad**: 12 acciones de la V6/V7 (reservas, carritos, cuentas de cliente, correo) salían en inglés partido; ahora en español.
- **Sucursales**: la asignación de usuarios mostraba códigos de rol («CAJERO») e incluía clientes web y la cuenta técnica; el aviso de
  ventas no disponibles mostraba el texto técnico.
- **Armador**: la proforma en PDF con el archivo abierto lanzaba una excepción sin aviso; la impresora mostraba el texto técnico.
  Cotizaciones mezclaba carritos; «Publicar» no debe ofrecerse para carritos; el bloque de reserva no se veía en reservas del mostrador.
- **Caja**: textos «armado» en carritos; al cerrar la sesión con una reserva cargada quedaba colgada; chips de categoría.
- **Sesión**: cancelar el cambio de contraseña obligatorio dejaba la sesión abierta; una falla del buscador global al arrancar
  impedía iniciar el trabajo de facturación.

## Pendientes

- Reportes: el porcentaje de participación se calcula sobre la lista truncada; «Sin movimiento» fijo en 30 días.
- Contabilidad: filtro de origen por texto, el borrador no se reinicia al cambiar de plantilla, sangría por nivel.
- Usuarios › Empresa: los combos de margen y días pueden sobrescribir un valor que no está en la lista.
- Facturación SIAT: «Ver PDF» registra una entrega al comprador; largo máximo 250 frente a 150 en un campo; textos de token y ambiente.
- Catálogo: el alta con imagen cuando falla la imagen; campos del editor que no se envían.
- Configuración: texto del modo nube y mensaje de la impresora.
- Revisar en pantalla (no automatizable aquí) el tema claro y oscuro de las pantallas nuevas con la base local de datos de prueba.

## Pruebas

Resultados exactos (29/09/2026, rama `v7-escritorio`, antes del commit final):

- `dotnet build MINV.sln`: **Compilación correcta · 0 Advertencia(s) · 0 Errores** (advertencias como errores).
- `dotnet test tests/MINV.DesktopClient.Tests`: **Correctas · Con error: 0, Superado: 65, Omitido: 0, Total: 65** (7 m 30 s).
  Nuevas: `V7ScreenTests` (7 pruebas: inicio con botones por rol y secciones cerradas que no leen datos; Reservas con filtros, detalle,
  reenviar a otro correo, liberar con y sin motivo y exportar; nueva reserva en mostrador cobrada en la caja con el comprador
  precargado y la reserva consumida una sola vez; cola de correos con filtro, abrir la reserva y exportar/cancelar; Usuarios con
  personal, clientes web y cuenta técnica; filtros y exportación de Stock, Transferencias —incluido el panel de alta—, Series,
  Garantías, Documentos fiscales y Actividad; tablero Tecnología que cuenta solo armados) y en `FormatTests` 11 casos del CSV
  (neutralización de `= + - @`, tabulador y ancho completo, comillas, números, fechas, «Sí/No», BOM). Ajustadas al inicio plegable y a
  Reservas: `ScreenTests` (menú con «reservas» y «correos», tablero, registrar), `StorefrontScreenTests` (el armador lista solo armados;
  la tarjeta del inicio abre Reservas con el canal Web), `TechScreenTests` (tablero Tecnología al abrir su sección).
- `dotnet test tests/MINV.Application.Tests` (se tocó `TechDashboard.cs`): **Correctas · Con error: 0, Superado: 100, Omitido: 0, Total: 100**.
- `dotnet test tests/MINV.Infrastructure.Tests --filter "V42Tech|StorefrontFlowTests|DemoWorkspace"` (las que usan el tablero y la
  demostración): **Con error: 1, Superado: 19, Omitido: 8 (PostgreSQL), Total: 28**. La que falla,
  `DemoWorkspaceTests.La_demostracion_es_Tech_Zone_Gaming_y_permite_ingresar_con_cada_rol`, espera más de 1000 series en la
  demostración y hay 943: depende de los datos generados (semilla y días), no de este paquete (D1 no cambió nada de Infrastructure,
  Domain ni del sembrador; el único cambio fuera del escritorio es el filtro por tipo del tablero). Queda para el paquete de datos.
- Capturas: `M-INV.exe --capturas <temporal>` con la demostración: **✔ 95 capturas**; las 8 nuevas (103 a 110) se revisaron y se copiaron a
  `docs/product/capturas/v7`.
