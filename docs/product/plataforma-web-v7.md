# Plataforma web M-INV · guía del producto (V7)

> Rama `Inventario-V7`, versión **7.0.0-alpha.1**. La web de Tech Zone Gaming (`src/3. Presentation/MINV.WebCatalog`) es a
> la vez la **tienda** para los clientes y el **panel del personal** (`/panel`). La tienda nació en la V5 (catálogo y «Armá tu
> PC»: [`catalogo-web-v5.md`](catalogo-web-v5.md)) y se conectó a la base de datos en la V6 (disponibilidad y reservas de
> armados); la V7 agrega las cuentas de cliente, el carrito, el correo de la reserva y el panel. Paso a paso:
> [`docs/deployment/inicio-rapido-v7.md`](../deployment/inicio-rapido-v7.md) · Docker:
> [`docs/deployment/tienda-publica-docker-v7.md`](../deployment/tienda-publica-docker-v7.md) · diseño:
> [`docs/architecture/plataforma-web-v7.md`](../architecture/plataforma-web-v7.md) · reglas P-01 a P-14:
> `.claude/v7-web-platform-rules.md` · el escritorio de la V7: [`escritorio-v7.md`](escritorio-v7.md).

## 1. Qué es

| Parte | Para quién | Dónde |
|---|---|---|
| **Tienda** | Cualquier visitante: catálogo, fichas, «Armá tu PC», carrito y reservas | `/` y sus páginas |
| **Cuenta de cliente** | Quien se registró en la tienda: sus reservas y sus datos | «Ingresar» → `/mi-cuenta` |
| **Panel del personal** | Las personas de la empresa, según su rol: caja, ventas, reservas, stock, compras, facturación, administración… | `/panel` |

Todo sale de la **misma base de datos** que el escritorio. El panel es **otro cliente del servidor en la nube**: envía los
mismos comandos y consultas que el escritorio, y el servidor decide en cada pedido qué puede hacer cada rol, en qué
sucursal, y lo audita (canal `web`). La web solo oculta lo que el rol no puede usar.

Idioma: la tienda habla con voseo («Reservá», «¿Cuándo pasás a recogerlo?»); el panel, en español neutro («Elija una
sucursal», «Pida al administrador»). Moneda: bolivianos con IVA incluido. Diseño oscuro gaming, objetivos táctiles de 44 px,
sin desplazamiento horizontal desde 360 px.

## 2. Páginas de la tienda

| Ruta | Página | Qué hace |
|---|---|---|
| `/` | **Inicio** | Productos protagonistas, campañas, categorías, destacados, ofertas, novedades, armados publicados y marcas (V5/V6) |
| `/catalogo` · `/catalogo/:categoria` | **Catálogo** | Categorías, filtros (marca, precio, etiquetas), búsqueda, orden, grilla o lista. V7: cada tarjeta y fila con disponibilidad tiene **«Agregar al carrito»** y **«Reservar ahora»** |
| `/producto/:slug` | **Ficha del producto** | Imagen, ficha técnica, precio, disponibilidad fresca y garantía. V7: cantidad, **«Agregar al carrito»** y **«Reservar ahora»**; como opciones secundarias «Agregar al armado» (piezas del armador) o «Consultar por WhatsApp» |
| `/arma-tu-pc` | **Armá tu PC** | Igual que la V6: una ranura por pieza, armados sugeridos y **«Reservar armado»** (`ARM-WEB-…`). Las horas que se guarda salen del catálogo |
| `/carrito` | **Carrito** (V7) | Ver §3.1 |
| `/reservar` | **Reservar** (V7) | La reserva del carrito o de un artículo suelto (`?sku=…&cantidad=…`): ver §3.2 |
| `/reserva` · `/reserva/:numero` | **Mi reserva** | Dos campos, «Código de reserva» y «Número de celular», con la pista «Complete al menos uno». Con el código (y, si se quiere, el celular): estado (Reservada, Vendida, Cancelada, Vencida), productos, total, dónde se retira y «Liberar mi reserva». Solo con el celular: la lista de sus reservas (las 10 más nuevas de los últimos 90 días) para abrir una. Sin los dos datos el nombre sale con iniciales, sin notas, y el celular y el correo enmascarados; para liberar se pide el celular si no se escribió. Acepta `RES-WEB-…` y `ARM-WEB-…` |
| `/ingresar` | **Ingresar** (V7) | Correo y contraseña (mostrar u ocultar). Ante cualquier falla, un solo mensaje: «Correo o contraseña incorrectos», y no se sale de la pantalla. A los 5 intentos fallidos la cuenta se bloquea 15 minutos y la página lo avisa. Enlace a «Crear cuenta». Olvidó la contraseña: se pide en la tienda (el Administrador asigna una temporal) |
| `/registrarse` | **Crear cuenta** (V7) | Nombre, correo, teléfono de Bolivia, contraseña y repetirla (8 a 128 caracteres con letras y números, con indicador de requisitos). Crea SIEMPRE una **cuenta de cliente**. Si el correo ya tiene cuenta: «Ese correo ya tiene una cuenta» con enlace a ingresar |
| `/cambiar-contrasena` | **Cambiar contraseña** (V7) | Para cualquier sesión; obligatoria si el Administrador asignó una temporal |
| `/mi-cuenta/*` | **Mi cuenta** (V7) | Solo clientes: ver §4 |
| `/panel/*` | **Panel** (V7) | Solo el personal: ver §5 |

**Cabecera.** Sin sesión, el botón **«Ingresar»** (también en el teléfono). Con sesión, el nombre de la persona con su menú:
el cliente ve «Mi cuenta» y «Mis reservas»; el personal, «Ir al panel»; los dos, «Cambiar contraseña» y «Cerrar sesión».
El ícono del **carrito** muestra cuántas unidades lleva. Después de ingresar, el personal va al panel y el cliente a «Mi
cuenta» (o a la página desde la que venía). Un cliente no entra al panel y el personal no entra a «Mi cuenta».

**Si la tienda no carga el catálogo** (el servidor de la tienda no responde), las páginas de la tienda muestran «No pudimos
cargar el catálogo» con «Reintentar», pero «Ingresar», «Crear cuenta», «Mi cuenta» y el panel siguen funcionando.

## 3. Carrito y reserva

### 3.1 El carrito

- **«Agregar al carrito»** suma el producto y avisa «Agregado al carrito» con «Ver carrito» (o por qué no entró más: ya
  está todo lo disponible, llegó al máximo por producto o el carrito está lleno).
- **`/carrito`**: cada producto con imagen, precio, cantidad (− y +), subtotal y «Quitar»; total y ahorro; «Seguir
  comprando»; «Vaciar carrito» (con confirmación); **«Reservar»**.
- Al abrirse vuelve a consultar la disponibilidad: marca lo que **se agotó**, lo que **bajó** por debajo de lo pedido y lo que
  **ya no está publicado**, y ofrece ajustar (una línea o todo). El carrito nunca se corrige solo y no deja reservar hasta
  ajustar.
- Topes: **16 unidades por producto** y **20 productos distintos** (los mismos de la reserva). La cantidad nunca supera lo
  disponible.
- Se guarda en el navegador (solo SKU y cantidad: nada de precios, nombres ni datos de la persona) y se sincroniza entre
  pestañas. Si el navegador no deja guardar, sigue funcionando y la página avisa que no se está guardando.
- **«Reservar ahora»** (en la tarjeta, la fila o la ficha) salta el carrito: reserva **ese solo producto** (el caso «quiero
  solo un monitor»), sin tocar el carrito ni el armado.

### 3.2 Reservar (`/reservar`)

| Parte | Qué pide o muestra |
|---|---|
| **Resumen** | Lo que se reserva (imagen, cantidad, precio, subtotal, total), con la disponibilidad recién consultada. Un artículo suelto deja cambiar la cantidad ahí mismo |
| **Datos del cliente (sin cuenta)** | Nombre y apellido, teléfono o WhatsApp de Bolivia y correo («Te enviamos el código y el detalle de tu reserva a este correo»). Invitación «¿Tenés cuenta? Ingresá y seguí tus reservas», sin obligar |
| **Con cuenta de cliente** | Los datos salen de su cuenta, solo para leer, con «Cambiar mis datos»; la factura usa el documento de la cuenta |
| **«¿Cuándo pasás a recogerlo?»** | Mañana (24 h), en 2 días (48 h) o en 3 días (72 h); por defecto, las horas que publica la tienda (48) |
| **«Datos para tu factura (opcional)»** | Sección plegable: tipo de documento (CI, CEX, PAS, OD o NIT), número, complemento (solo con CI) y nombre o razón social, con las reglas del SIN. La caja los propone al cobrar |
| **Notas para la tienda** | Se pueden escribir en varias líneas; viajan en una |

Cada campo se valida al salir de él y todos al enviar (el foco va al primero con error; si está en la factura, la sección se
abre sola). **«Confirmar reserva»** envía la reserva **una sola vez** aunque se corte la conexión y se reintente.

| Si el servidor responde | La página |
|---|---|
| Falta stock | Marca cada producto «Pediste X; hay Y» y ofrece **«Ajustar a lo disponible»** sin perder lo escrito. La reserva es todo o nada: no se reservó nada |
| Un dato no sirve | Marca el campo cuando puede ubicarlo; si no, lo muestra arriba |
| Demasiados intentos | «Hiciste demasiados intentos seguidos», con «Reintentar» |
| Sin conexión | «No pudimos conectarnos con la tienda», con «Reintentar» (el reintento no duplica la reserva) |

### 3.3 Confirmación y correo

- La confirmación muestra el **número** bien visible con «Copiar» (`RES-WEB-000001` para una compra de la web; los armados
  siguen siendo `ARM-WEB-…`), el tipo (**Compra**), el estado, **hasta cuándo se guarda** (y cuántas horas), **dónde se
  retira**, el detalle con el total y los enlaces «Mi reserva» (o «Mis reservas» con cuenta) y «Seguir en el catálogo». Los
  productos reservados salen del carrito; un artículo suelto no toca el carrito.
- **«Te enviamos un correo a …»** aparece solo si el servidor dejó la confirmación en la cola; si no, «Guardá este número».
- El **correo** sale de la cuenta de la empresa (`zapasoftwarefastsolutions@gmail.com`) con el asunto «Reserva `<número>` ·
  `<empresa>`»: saludo, **el código para presentar al recogerla**, cada producto con cantidad, precio y subtotal, el total
  («se paga al recoger»), hasta cuándo se guarda, dónde se recoge y el botón **«Ver mi reserva»**. No incluye las notas del
  cliente. Sale unos segundos después de reservar, por una cola con reintentos; como máximo 3 correos por destinatario cada
  24 h. En las pruebas, todos los correos quedan en el buzón de prueba (`http://127.0.0.1:8025`).
- La reserva queda con el stock **reservado**: la tienda y el escritorio muestran menos disponible hasta que se venda, se
  libere o venza.

### 3.4 Después de reservar

- **Consultar** en «Mi reserva» (`/reserva`, con el código O el número de celular; con uno solo, el contacto sale
  enmascarado) o, con cuenta, en «Mis reservas».
- El enlace «Ver mi reserva» del correo abre `/reserva/<código>` con el código ya escrito: basta con «Buscar reserva».
- **Liberar**: «Liberar mi reserva» (con confirmación; SIEMPRE con el código y el celular: si se buscó solo con el código,
  la confirmación pide el celular); el stock vuelve al instante.
- **Vencer**: pasado el plazo, la cierra sola el trabajo automático (cada 5 minutos); mientras tanto se muestra «Vencida».
- **Comprar**: se paga en la tienda. El personal la vende en caja (panel o escritorio) y la reserva pasa a **«Vendida»**.

## 4. «Mi cuenta»

Solo para cuentas de cliente (`/mi-cuenta`), con tres secciones:

| Sección | Qué hace |
|---|---|
| **Mis reservas** (`/mi-cuenta/reservas`) | Sus reservas, de la más nueva a la más antigua, con filtro por estado (Todas, Reservadas, Vendidas, Vencidas, Canceladas), tipo (Compra o Armado), las horas reales que se guarda cada una y el detalle con sus productos; **«Liberar mi reserva»** en las vigentes |
| **Mis datos** (`/mi-cuenta/datos`) | Nombre, teléfono y documento para la factura (tipo, número y complemento). El correo es el de ingreso |
| **Cambiar contraseña** (`/mi-cuenta/contrasena`) | Contraseña actual y la nueva, con los mismos requisitos del registro |

Una cuenta de cliente solo ve y cambia **sus** datos y **sus** reservas: el servidor no le deja ejecutar nada más. No sirve
para el escritorio ni para el panel.

## 5. El panel del personal (`/panel`)

### 5.1 Cómo se usa

- **Menú lateral** por secciones plegables (General, Ventas, Tecnología, Inventario, Compras, Sucursales, Facturación,
  Análisis, Administración) con un **buscador de pantallas**; en el teléfono se abre como cajón. Solo aparecen las
  secciones y los módulos que el rol puede usar.
- **Barra superior**: migas de pan, la lista **«Sucursal activa»** (si la persona tiene más de una; la gerencia puede elegir
  «Todas las sucursales») y el usuario (nombre y rol, «Cambiar contraseña», «Ir a la tienda», «Cerrar sesión»). Al cambiar la
  sucursal, cada lista se vuelve a consultar.
- **Inicio** (`/panel`): «Hola, …», el rol y la sucursal activa; **«¿Qué quiere hacer?»** con los **botones grandes** que
  ofrecen los módulos, agrupados por sección; debajo, **«Ver estadísticas ^»**, **cerrado** al entrar: al abrirlo, cada
  estadística se descarga y consulta por separado (una que falla no tapa a las demás).
- **Cada pantalla de lista**: título y botones de acción; **barra de filtros** con listas desplegables (y `ComboBox` con
  búsqueda en las listas largas, rango de fechas con atajos Hoy, Ayer, Últimos 7 días, Este mes y Mes anterior), búsqueda
  y «Limpiar filtros» con el contador de activos; tabla ordenable y paginada (tarjetas en el teléfono) con menú **«⋯»** por
  fila; **detalle al costado**; **«Exportar CSV»** (se abre bien en Excel: UTF-8, «;» y celdas protegidas contra fórmulas);
  estados de carga, vacío y error con **«Reintentar»**; los resúmenes con números, plegados en «Ver …». Los filtros quedan
  en la dirección, así un botón del tablero abre la lista ya filtrada.
- **Permisos**: un botón que el rol no puede usar no aparece. Si alguien abre por dirección una pantalla que no le
  corresponde, el panel dice «No tiene acceso a esta pantalla» y qué permiso falta. El servidor vuelve a decidir en cada
  pedido.

### 5.2 Los módulos

En cada tabla: **Lo ve** = permisos que muestran el módulo · **Tablero** = botones de «¿Qué quiere hacer?» y estadísticas
plegadas que ofrece (entre paréntesis, el permiso extra que piden).

#### General

| Módulo | Lo ve | Tablero | En la pantalla |
|---|---|---|---|
| **Inicio** `/panel` | Toda sesión del personal | Muestra los botones y las estadísticas de los demás módulos | Saludo, sucursal activa, «¿Qué quiere hacer?» y «Ver estadísticas» plegado |

#### Ventas

| Módulo | Lo ve | Tablero | En la pantalla |
|---|---|---|---|
| **Caja** `/panel/caja` | `sales.pos.operate` | «Ir a la caja» · «Vender una reserva» (`sales.view` + `inventory.movements.register.sales`) · estadística «Ventas de mi turno» | «Abrir caja» (caja y fondo inicial) y «Cerrar caja» con arqueo (efectivo contado y diferencia); buscador **«Buscar o escanear (F2)»** para el lector de códigos; productos con precio y disponible, «Agregar» o «Elegir IMEI/serie»; venta actual con cantidades, descuento, series obligatorias y avisos; **«Cobrar» (F4)** con cliente, datos de la factura (Verificar NIT), medio de pago, efectivo recibido y vuelto; resultado con la factura del SIN, «Imprimir comprobante», «Descargar PDF» y «Enviar por correo»; **«Vender una reserva»** (o `?reserva=<número>`) con precios congelados y los datos de factura de la reserva. Filtros: categoría, plataforma, condición, disponibilidad y búsqueda. Exportar CSV |
| **Ventas** `/panel/ventas` | `sales.view` | «Ventas de hoy» · «Registrar una devolución» (`billing.void` + `sales.pos.operate`) · estadísticas «Ventas por día (7 días)» y «Medios de pago» | Pestañas **Ventas** y **Devoluciones**. Detalle con productos y series, pago, factura del SIN y devoluciones; «Reimprimir la factura» (imprimir o PDF), «Enviar la factura por correo», **«Devolver productos»** (paso a paso: productos o series, motivo, «por falla», medio de reembolso y confirmación), «Anular» (con motivo), «Ver el cliente». Filtros: fechas (hoy por defecto), sucursal, cliente, cajero, medio de pago, estado y factura del SIN; en Devoluciones, sucursal, cliente, estado de la nota y fechas. Resumen plegado. Exportar CSV |
| **Clientes** `/panel/clientes` | `inventory.stock.view` y uno de `sales.customers.manage` / `sales.view` | «Nuevo cliente» (`sales.customers.manage`) | Alta y edición con los datos de factura del SIN y «Verificar el NIT en el Padrón»; activar y desactivar; detalle con «Ver sus ventas». Filtros: categoría, estado, datos de factura, origen (clientes web o de la tienda) y búsqueda. Resumen plegado. Exportar CSV |
| **Reservas** `/panel/reservas` | `sales.pcbuild.manage` + `sales.view` | «Nueva reserva en mostrador» (`inventory.stock.view`) · «Reservas que vencen hoy» · estadística «Reservas activas y su valor» | Todas las reservas: compras (`RES-…`) y armados (`ARM-…`), de la web y del mostrador, con «Reservado hasta» resaltado (ámbar a menos de 6 h, rojo si venció) y el **estado del correo**. Detalle con productos, datos para la factura, bitácora y correos. **«Vender en caja»**, **«Liberar»** (con motivo), **«Reenviar correo»** (a su correo o a otro), **«Copiar teléfono»**, **«Abrir WhatsApp»** y **«Nueva reserva en mostrador»** (productos, cliente, teléfono, días para recoger y datos para la factura). Filtros: tipo, canal, estado, vence (hoy, mañana, vencidas), sucursal, fecha de creación y búsqueda (número, nombre o teléfono). Exportar CSV |

#### Tecnología

| Módulo | Lo ve | Tablero | En la pantalla |
|---|---|---|---|
| **Armador de PC** `/panel/armador` | `sales.view` + `inventory.stock.view` | «Armar una PC» | **Cotizaciones** (solo armados) y el **armador paso a paso** (`/panel/armador/nuevo`): 12 ranuras, candidatos del servidor con la compatibilidad **en vivo**, filtros de categoría, marca, precio, «solo con stock» y «solo compatibles»; «Guardar borrador», «Guardar cotización», «Reservar stock», «Publicar / Quitar de la web», «Imprimir cotización», «Liberar reserva», «Anular» y «Vender en caja» (guardar y los comandos piden `sales.pcbuild.manage`). Filtros de la lista: estado, vigencia, publicado, canal, fecha y búsqueda. Resumen plegado. Exportar CSV |
| **Series** `/panel/series` | `inventory.serials.view` | «Consultar una serie» · estadística «Unidades por estado» | Cada unidad con serie o IMEI: dónde está, a quién se vendió, su garantía, su trazabilidad y sus casos. «Consultar una serie», «Registrar series de stock» y «Dar destino» (al proveedor o de baja) con `inventory.serials.manage`; «Abrir caso de garantía». Filtros: estado, producto, sucursal, tipo, garantía, fecha de ingreso, cuántas revisar y búsqueda. Resumen plegado. Exportar CSV |
| **Garantías** `/panel/garantias` | `inventory.serials.view` | «Abrir un caso de garantía» (`service.rma.open`) | Casos RMA: «Abrir un caso», los pasos según el estado («Pasar a diagnóstico», «Enviar al proveedor», «Marcar reparado», «Reemplazar con otra unidad», «Rechazar la garantía», «Entregar al cliente»; `service.rma.manage`), «Agregar nota», «Orden de servicio» imprimible y la bitácora. Filtros: estado (abiertos por defecto), sucursal, cobertura, tiempo en el taller, fecha de recepción y búsqueda. Resumen plegado. Exportar CSV |

#### Inventario

| Módulo | Lo ve | Tablero | En la pantalla |
|---|---|---|---|
| **Stock** `/panel/stock` | `inventory.stock.view` | «Consultar stock» · estadísticas «Valor del inventario» y «Productos en alerta» | Existencias, **reservado** y **disponible** por sucursal o **todas las sucursales (consolidado)**; ficha lateral con Ficha, **Kardex** (exportable), Reservas y Por sucursal; «Registrar entrada / salida / ajuste» según el permiso. Filtros: sucursal, categoría, marca, semáforo, con reservas, sin rotación y búsqueda. Resumen plegado. Exportar CSV |
| **Catálogo** `/panel/catalogo` | `inventory.stock.view` | «Nuevo producto» (`catalog.manage`) · «Buscar en el catálogo» · estadística «Estado del catálogo» | Pestañas **Productos** (lista o galería), **Categorías** y **Especificaciones**: alta y edición de productos con precio, imagen y ficha técnica, activar y desactivar, categorías y subcategorías, especificaciones por categoría (crear y modificar pide `catalog.manage`; fichas técnicas, `catalog.specs.manage`). Filtros: categoría, marca, estado, imagen, precio, serie o IMEI, plataforma, condición y búsqueda. Resumen plegado. Exportar CSV |
| **Movimientos** `/panel/movimientos` | `inventory.stock.view` y uno de `inventory.movements.register.warehouse` / `.sales` | «Registrar entrada» · «Registrar ajuste» (los dos, `inventory.movements.register.warehouse`) | Registrar un movimiento (tipos que el rol puede usar, producto con el lector de códigos, posición, cantidad, documento y observaciones) con **poka-yoke visible** (la salida que dejaría la posición en negativo se marca en rojo y no deja registrar) y series cuando el producto las lleva; lista de los últimos movimientos con detalle; tendencia plegada. Filtros: tipo, entradas o salidas, usuario, producto, fechas, cuántos revisar y búsqueda. Exportar CSV |
| **Toma física** `/panel/toma-fisica` | `inventory.counts.record` + `inventory.stock.view` | «Iniciar toma física» | «Iniciar toma física» (almacén, fecha y observación) o seguir la que está en curso; contar producto por producto a mano o con el lector de códigos («Registrar conteo», «Corregir conteo», «Quitar conteo») viendo la diferencia contra el stock exacto; «Imprimir planilla»; **«Generar ajustes»** con el resumen de diferencias y «Anular toma» (los dos, `inventory.counts.post`). Filtros: categoría, ubicación, diferencia, contados o pendientes y búsqueda. Resumen de diferencias plegado. Exportar CSV |
| **Alertas** `/panel/alertas` | `inventory.stock.view` | «Ver alertas» | Productos sin stock, críticos, bajos, con exceso o inconsistentes, por urgencia y con la acción sugerida; «Registrar entrada» o «Registrar ajuste» (con permiso de bodega), «Ver en el pedido sugerido», «Ver ficha y kardex». Filtros: tipo, categoría, proveedor, sucursal y búsqueda. Resumen plegado. Exportar CSV |

#### Compras

| Módulo | Lo ve | Tablero | En la pantalla |
|---|---|---|---|
| **Pedido sugerido** `/panel/pedido` | `inventory.stock.view` | «Generar pedido» (`purchasing.manage`) | Pestañas **Productos** (cantidad editable) y **Proveedores**; «Copiar pedido»; **«Generar órdenes de compra»** y «Crear orden de compra» de un proveedor (`purchasing.manage`). Filtros: proveedor, categoría, semáforo y búsqueda. Resumen plegado. Exportar CSV |
| **Órdenes de compra** `/panel/compras` | `purchasing.manage` + `inventory.stock.view` | «Nueva orden de compra» · «Recibir mercadería» (`inventory.movements.register.warehouse`) · estadística «Compras en curso» | Pestañas **Órdenes** y **Facturas de proveedores**: «Nueva orden», «Desde el pedido sugerido», «Aprobar», «Recibir mercadería» (con las series de lo serializado), «Anular» y registrar la factura del proveedor de lo recibido. Filtros: estado, proveedor, sucursal, fecha de la orden y búsqueda. Resumen plegado. Exportar CSV |
| **Proveedores** `/panel/proveedores` | `purchasing.manage` + `inventory.stock.view` | — | Contacto, NIT, días de entrega (los usa el pedido sugerido), productos asignados, órdenes abiertas y lo comprado; «Nuevo proveedor», «Editar», «Activar» / «Desactivar», «Ver sus órdenes de compra» y «Nueva orden de compra». Filtros: estado, órdenes abiertas, plazo de entrega y búsqueda. Resumen plegado. Exportar CSV |

#### Sucursales

| Módulo | Lo ve | Tablero | En la pantalla |
|---|---|---|---|
| **Sucursales** `/panel/sucursales` | `inventory.stock.view` y uno de `reports.view` / `corporate.branches.all` / `corporate.branches.manage` | — | Pestañas **Sucursales** (almacenes, usuarios, stock valorizado y transferencias abiertas de cada una; comparativo por sucursal plegado) y **Stock consolidado** (con lo que está en tránsito, contado una vez). Para quien administra sucursales: «Nueva sucursal», «Editar», «Activar» / «Desactivar» y «Asignar usuarios». «Transferencias que salen» / «que llegan». Filtros: estado, acceso y búsqueda. Exportar CSV |
| **Transferencias** `/panel/transferencias` | `inventory.transfers.manage` + `inventory.stock.view` (módulo comercial `MULTI_BRANCH`) | «Nueva transferencia» · «Recibir transferencia» · estadística «Transferencias en curso» | La sucursal de ORIGEN solicita («Nueva transferencia»), **«Despachar»** (la mercadería queda en tránsito) y «Anular»; la de DESTINO **«Recibir»**, con los faltantes y su motivo, y las series de lo serializado. Detalle con la bitácora. Filtros: estado, origen, destino, pendiente en mis sucursales, fecha de la solicitud, cuántas revisar y búsqueda. Resumen plegado. Exportar CSV |

#### Facturación

| Módulo | Lo ve | Tablero | En la pantalla |
|---|---|---|---|
| **Documentos fiscales** `/panel/documentos-fiscales` | `billing.view` (módulo comercial `FISCAL_SIAT`) | «Facturas de hoy» · «Revisar rechazadas» · estadística «Facturación de hoy» | Facturas y notas crédito-débito del SIN con su detalle y bitácora: «Ver e imprimir», «Enviar por correo», «Verificar en el SIN», «Re-emitir», «Copiar CUF», «Anular ante el SIN» (también «anular y devolver»), «Revertir la anulación» y el XML. Filtros: tipo, estado, tipo de emisión, sucursal, punto de venta, fechas de emisión y búsqueda. Resumen plegado. Exportar CSV |
| **Estado del SIAT** `/panel/siat` | `billing.view` (módulo comercial `FISCAL_SIAT`) | «Estado del SIAT» · «Transcribir factura manual» (`billing.contingency`) · estadística «Conexión con el SIN» | Conexión con el SIN y alertas con cuenta regresiva de los plazos; pestañas **Puntos de venta** (una tarjeta por punto con su modo, CUIS y CUFD: «Verificar comunicación», «Pedir CUFD nuevo», «Pasar a fuera de línea», «Contingencia manual», «Terminar contingencia», «Recuperar ahora»), **Eventos significativos**, **Paquetes** y **Talonarios CAFC** («Registrar talonario CAFC», «Transcribir factura manual»); «Procesar ahora»; indicadores y bitácora técnica del SIN plegados. Cada acción aparece solo si el rol puede ejecutarla. Filtros: sucursal y modo. Exportar CSV |

#### Análisis

| Módulo | Lo ve | Tablero | En la pantalla |
|---|---|---|---|
| **Reportes** `/panel/reportes` | `reports.view` | «Reporte de ventas» · «Reporte de compras» · estadística «Ventas del mes vs mes anterior» | Pestañas **Ventas** (ingresos, IVA, costo, utilidad y margen, agrupado por producto, categoría, cliente, cajero, medio de pago o día), **Compras**, **Movimientos**, **Inventario**, **Sucursales** y **Tecnología**, con **período** (atajos y fechas), filtros, totales, gráficos plegados («Ver gráfico»), **Exportar CSV** e **Imprimir** |
| **Contabilidad** `/panel/contabilidad` | `accounting.manage` | «Libro diario» · «Estado de resultados» · «Registrar un asiento» | Pestañas **Estado de resultados** (con gráfico plegado), **Libro diario** (asientos con su detalle Debe y Haber; filtros de cuenta, origen, estado y búsqueda) y **Plan de cuentas** (árbol; filtros de tipo, clase, movimiento y saldos); «Nuevo asiento» con Debe = Haber en vivo y plantillas, «Nueva cuenta» y «Crear una subcuenta». Exportar CSV |

#### Administración

| Módulo | Lo ve | Tablero | En la pantalla |
|---|---|---|---|
| **Usuarios** `/panel/usuarios` | `iam.users.manage` | «Nuevo usuario» · «Restablecer una contraseña» · estadística «Usuarios del sistema» | Pestañas **Usuarios** y **Roles y permisos** (solo lectura): nuevo usuario (rol, sucursales y contraseña inicial que se muestra una vez), editar, activar y desactivar, restablecer la contraseña (temporal, desbloquea), asignar sucursales, ver su actividad. Filtros: **tipo (Personal o Clientes web)**, rol, sucursal, estado y búsqueda. Resumen plegado. Exportar CSV |
| **Integraciones** `/panel/integraciones` | `integration.manage` | «Correos de reservas» (`sales.pcbuild.manage`) · estadísticas «Integraciones» y «Correos de reservas» (`sales.pcbuild.manage`) | Pestañas **API Keys** (nueva, revocar; la llave se muestra una vez), **Webhooks** (nuevo, rotar el secreto, desactivar), **Entregas** y **Correos de reservas** (la cola: pendiente, enviado, agotado o cancelado, intentos y último error; «Reenviar» y «Reenviar una confirmación»). Filtros en cada pestaña. Resumen plegado. Exportar CSV |
| **Configuración** `/panel/configuracion` | `iam.users.manage` o `billing.configure` | «Configurar la facturación» (`billing.configure`) | Pestañas **Empresa** (datos y semáforo de stock), **Facturación SIAT** (estado, «Probar conexión», «Preparar SIAT», «Sincronizar catálogos», datos del Padrón, conexión con el SIN, sucursales del Padrón, puntos de venta con CUIS y CUFD, actividades económicas) y **Correo de la empresa** («Configurar con Gmail»; la contraseña nunca se muestra) |
| **Actividad** `/panel/actividad` | `iam.audit.view` | «Ver la actividad» · «Revisar rechazos» · estadística «Actividad de hoy» | La auditoría: quién hizo qué, cuándo y con qué resultado; detalle con los datos registrados (sin contraseñas); «Ver solo este usuario» y «Asignar contraseña temporal» (`iam.users.manage`). Filtros: usuario, resultado, fechas, cuántos revisar y búsqueda. Exportar CSV |

<!-- V7-MODULOS: completar con los módulos que falten -->

Pantallas del diseño todavía sin módulo en la web: **Homologación** y **Libros** de ventas y compras (Facturación); siguen en
el escritorio.

## 6. Roles y permisos

### 6.1 Qué módulos ve cada rol

✔ = el módulo aparece en el menú. Dentro de cada módulo, algunos botones piden un permiso más (tablas de §5.2).

| Módulo | Administrador | Gerencia | Bodega | Ventas | Cajero | Consulta |
|---|---|---|---|---|---|---|
| Inicio | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Caja | ✔ | — | — | ✔ | ✔ | — |
| Ventas | ✔ | ✔ | — | ✔ | ✔ | — |
| Clientes | ✔ | ✔ | — | ✔ | ✔ | — |
| Reservas | ✔ | ✔ | — | ✔ | ✔ | — |
| Armador de PC | ✔ | ✔ | — | ✔ | ✔ | — |
| Series | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Garantías | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Stock | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Catálogo | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Movimientos | ✔ | — | ✔ | ✔ | ✔ | — |
| Toma física | ✔ | — | ✔ | — | — | — |
| Alertas | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Pedido sugerido | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Órdenes de compra | ✔ | ✔ | ✔ | — | — | — |
| Proveedores | ✔ | ✔ | ✔ | — | — | — |
| Sucursales | ✔ | ✔ | ✔ | ✔ | — | ✔ |
| Transferencias | ✔ | ✔ | ✔ | — | — | — |
| Documentos fiscales | ✔ | ✔ | — | ✔ | ✔ | ✔ |
| Estado del SIAT | ✔ | ✔ | — | ✔ | ✔ | ✔ |
| Reportes | ✔ | ✔ | ✔ | ✔ | — | ✔ |
| Contabilidad | ✔ | ✔ | — | — | — | — |
| Usuarios | ✔ | — | — | — | — | — |
| Integraciones | ✔ | — | — | — | — | — |
| Configuración | ✔ | — | — | — | — | — |
| Actividad | ✔ | ✔ | — | — | — | — |

El **Cliente web** no entra al panel (su página es «Mi cuenta») y la cuenta técnica **Tienda web** no es una persona.
Sucursales: la Gerencia y el Administrador ven todas; los demás, las asignadas (la sucursal activa decide dónde se opera).

### 6.2 Permisos de cada rol

La matriz por defecto de una empresa nueva (`PermissionCodes.ForRole`, la misma del escritorio). El Administrador tiene
todos; «Tienda web» es la cuenta técnica del API Gateway.

| Permiso | Administrador | Gerencia | Bodega | Ventas | Cajero | Consulta | Tienda web | Cliente web |
|---|---|---|---|---|---|---|---|---|
| `inventory.stock.view` Consultar stock, alertas y pedido sugerido | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | — |
| `catalog.manage` Productos, categorías, unidades y proveedores | ✔ | — | — | — | — | — | — | — |
| `catalog.specs.manage` Fichas técnicas y especificaciones | ✔ | ✔ | ✔ | — | — | — | — | — |
| `inventory.movements.register.warehouse` Entradas, saldo inicial y ajustes | ✔ | — | ✔ | — | — | — | — | — |
| `inventory.movements.register.sales` Salidas | ✔ | — | — | ✔ | ✔ | — | — | — |
| `inventory.counts.record` / `inventory.counts.post` Toma física | ✔ | — | ✔ | — | — | — | — | — |
| `inventory.transfers.manage` Transferencias entre sucursales | ✔ | ✔ | ✔ | — | — | — | — | — |
| `inventory.serials.view` Series e IMEI y casos RMA | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | — | — |
| `inventory.serials.manage` Registrar series y dar de baja | ✔ | ✔ | ✔ | — | — | — | — | — |
| `service.rma.open` Abrir casos de garantía | ✔ | ✔ | ✔ | ✔ | ✔ | — | — | — |
| `service.rma.manage` Avanzar, reponer y entregar casos | ✔ | ✔ | ✔ | — | — | — | — | — |
| `purchasing.manage` Órdenes de compra, recepciones y devoluciones | ✔ | ✔ | ✔ | — | — | — | — | — |
| `sales.pos.operate` Abrir y cerrar caja, vender y cobrar | ✔ | — | — | ✔ | ✔ | — | — | — |
| `sales.view` Historial de ventas y facturas | ✔ | ✔ | — | ✔ | ✔ | — | — | — |
| `sales.customers.manage` Crear y modificar clientes | ✔ | — | — | ✔ | ✔ | — | — | — |
| `sales.pcbuild.manage` Armador de PC y reservas | ✔ | ✔ | — | ✔ | ✔ | — | — | — |
| `billing.view` Documentos fiscales, SIAT y libros | ✔ | ✔ | — | ✔ | ✔ | ✔ | — | — |
| `billing.issue` Emitir y reenviar facturas | ✔ | — | — | ✔ | ✔ | — | — | — |
| `billing.void` Anular, revertir y notas crédito-débito | ✔ | ✔ | — | — | — | — | — | — |
| `billing.contingency` Eventos, paquetes y CAFC | ✔ | ✔ | — | — | — | — | — | — |
| `billing.configure` Configurar la facturación SIAT | ✔ | — | — | — | — | — | — | — |
| `reports.view` Reportes | ✔ | ✔ | ✔ | ✔ | — | ✔ | — | — |
| `accounting.manage` Asientos, períodos y costos | ✔ | ✔ | — | — | — | — | — | — |
| `corporate.branches.all` Todas las sucursales (gerencia global) | ✔ | ✔ | — | — | — | — | — | — |
| `corporate.branches.manage` Sucursales y asignación de usuarios | ✔ | — | — | — | — | — | — | — |
| `iam.users.manage` Usuarios, roles y permisos | ✔ | — | — | — | — | — | — | — |
| `iam.audit.view` Auditoría y actividad | ✔ | ✔ | — | — | — | — | — | — |
| `integration.manage` API Keys y webhooks | ✔ | — | — | — | — | — | — | — |
| `storefront.read` / `storefront.reserve` Tienda web pública | ✔ | ✔ | — | — | — | — | ✔ | — |
| `account.manage` / `account.reserve` Cuenta de cliente | ✔ | — | — | — | — | — | — | ✔ |

Reglas que no dependen de la tabla:

- **Registrarse** crea siempre una cuenta **Cliente web**; el personal lo crea el Administrador (panel › Usuarios). Nadie se
  registra con un rol del personal.
- Una sesión es **de cliente** cuando su ÚNICO rol es Cliente web: solo puede usar su cuenta (sus datos, sus reservas, cambiar
  su contraseña y cerrar sesión), en la web y en cualquier otra ruta del servidor. El Administrador tiene los permisos de la
  cuenta, pero su sesión es del personal.
- El **teléfono, el correo y los datos para la factura** de una reserva solo los ven quienes tienen `sales.pcbuild.manage`.
  En «Mi reserva» el celular y el correo salen siempre enmascarados, y el nombre y las notas solo completos si se buscó con el
  código Y el celular (regla S-06).
- Los botones ocultos son comodidad: el servidor comprueba permisos, módulo comercial y sucursal en cada pedido.

## 7. Qué no hace

- Sin pagos en línea ni envío a domicilio: la reserva se paga y se retira en la tienda (la casa matriz).
- Sin verificación del correo al registrarse ni recuperación de la contraseña por correo (la restablece el Administrador).
- La caja web imprime con el navegador: no maneja la impresora de tickets ni el cajón (eso sigue en el escritorio).
- El panel no filtra todavía por los módulos comerciales de la empresa: si un módulo no está contratado, el servidor rechaza y
  el aviso lo explica.
- Algunas listas filtran en la página porque la consulta del servidor solo recibe fechas o un tope de filas (por ejemplo,
  Ventas y Actividad): conviene acotar las fechas o los registros a revisar.

## 8. Para quien programa

La web vive en `src/3. Presentation/MINV.WebCatalog` (Vite + React + TypeScript + Tailwind). Cómo correrla, su arquitectura y
sus pruebas: `src/3. Presentation/MINV.WebCatalog/README.md`. Guía para escribir un módulo del panel:
`src/3. Presentation/MINV.WebCatalog/src/4-presentation/panel/README.md`. Los tipos del servidor salen del contrato generado
(`minv contrato-web`, regla P-07).
