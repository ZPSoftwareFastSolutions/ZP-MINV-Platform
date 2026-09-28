# M-INV V6 · Tienda web conectada (catálogo web sobre la base de datos en la nube)

> Rama `Inventario-V6` (sobre `Inventario-V5`). Versión 6.0.0-alpha.1. La V5 dejó el catálogo web con un mock; la V6 lo
> conecta a la **misma base de datos en la nube** que usa la aplicación de escritorio (Tech Zone Gaming, V4.2): lo que se
> vende en el escritorio deja de estar disponible en la web al instante, y lo que se **reserva** desde el armador de la web
> (o desde el escritorio) queda **EN RESERVA** para ambos. Este documento describe lo que el código hace de verdad: la
> fase A (dominio, aplicación, migración, API pública, datos de prueba) y la fase B (pantallas de la web y del escritorio)
> están fusionadas en la rama y verificadas de punta a punta (§8). Paso a paso para probarla:
> `docs/deployment/inicio-rapido-v6.md`.

## 1. Qué cambia

| Antes (V5) | Ahora (V6) |
|---|---|
| Productos, categorías, fichas y armados sugeridos desde un JSON embebido | Desde la base de datos en la nube, a través de una **API pública de tienda** (`/storefront/v1`) del API Gateway |
| Imágenes propias copiadas al sitio | Servidas por la API desde `catalog.product_images` (con `ETag` y caché de 1 h) |
| «Finalizar armado» solo mostraba un resumen | **Reserva** el armado: crea una cotización `ARM-WEB-000001` en la base, con los datos de contacto, y **reserva el stock** de cada pieza por 48 h |
| El escritorio no sabía nada de la web | Armador de PC del escritorio: ve las reservas web (contacto, vence), las **vende en caja** (consumiendo la reserva) o las **libera**; puede **reservar** sus propias cotizaciones y **publicar** armados sugeridos en la web |
| Stock: disponible = existencias | Disponible = existencias − **reservado** (reservas web, de escritorio y de caja) en la sucursal de la tienda |

## 2. Arquitectura

```text
Catálogo web (React, V5)  ──HTTP──▶  MINV.ApiGateway  /storefront/v1/*  (anónimo, tenant fijo, límite por IP, CORS)
   3-infrastructure/http: HttpCatalogSource (instantánea del catálogo) + HttpReservationGateway
   InMemoryCatalogRepository hidratado con la instantánea (el resto de la V5 no cambia)
                                        │ MediatR (misma tubería: validación → permisos → auditoría, canal «storefront»)
                                        ▼
                      MINV.Application/Storefront (consultas públicas + reservas)  ── MINV.Domain (PcBuild, StockLevel)
                                        │
                                        ▼
                      PostgreSQL en la nube (la misma base del escritorio)  ◀── MINV.DesktopClient (Armador de PC, Stock, Caja)
```

- **Principal de tienda** (`StorefrontAuthenticator`, esquema de autenticación `Storefront`): las rutas `/storefront/v1` no
  llevan API Key. En cada petición el gateway busca la empresa de `Minv:Storefront:TenantCode`, su usuario con el rol
  **`TIENDA_WEB`** («Tienda web»: `tienda-web@<dominio>`), recalcula sus permisos desde la base (`storefront.read`,
  `storefront.reserve`, `inventory.stock.view`) y fija el alcance a **una** sucursal: `Minv:Storefront:BranchCode` o, si no
  está, la del almacén principal de la empresa (casa matriz) o la primera activa. El canal de la auditoría es `storefront`.
  Nunca ve costos, clientes ni ventas. Si falta la empresa, el usuario técnico o la sucursal, toda ruta responde **503
  «Tienda web no disponible»**.
- **Configuración** (`Minv:Storefront`, `StorefrontSettings`): `Enabled` (true), `TenantCode`, `BranchCode`, `AllowedOrigins[]`
  (CORS: `GET`, `POST`, `OPTIONS`, cualquier cabecera; expone `ETag`, `Idempotent-Replayed`, `Location`, `Cache-Control`),
  `ReservationHours` (48; `StorefrontOptions` la acota a 1…720), `ReadsPerMinute` (300), `ReservationsPerMinute` (10) y
  `ExpiryMinutes` (5). En `appsettings.json` del gateway viene TECHZONE, CM y `http://localhost:5173`;
  `tools/servidores_locales.ps1` y `deploy/docker-compose.yml` las pasan por variables `Minv__Storefront__*`.
- **Contrato**: `docs/integration/storefront-api-v1.md` (JSON exacto, copiado de una ejecución real). Lo escribe el backend
  y lo consume la web.
- **Instantánea** (`GetStorefrontCatalogQuery`, `StorefrontCatalogReader`): la MISMA forma que el mock de la V5
  (`tools/generar_catalogo_web.py`): categorías en recorrido en profundidad con ícono de Lucide y conteo (incluidas las
  subcategorías), marcas, productos activos **con precio en la lista por defecto** (sin precio no salen), `slug` = SKU en
  minúsculas, ficha técnica ordenada de la categoría madre a la hoja, condición, garantía, serie, `listPrice` tachado solo
  si hay una lista de precios vigente no predeterminada llamada «web», «oferta» o «lista … tienda», popularidad 1-10 por
  las ventas facturadas de 90 días, etiquetas `destacado` (≥ 8), `oferta`, `nuevo` (alta en 30 días), descripción generada
  si el producto no tiene, y armados publicados. Disponibilidad de la sucursal de la tienda: `available` = existencias −
  reservado (nunca negativo), `reserved`, `onHand`.
- **Reserva** (`CreateStorefrontReservationCommand`) = `PcBuild` con `Channel = Web`, `Status = Reserved`, `ReservedUntil`,
  contacto (nombre y teléfono boliviano normalizado obligatorios, correo y notas opcionales) y una `StockReservation` por
  línea (origen nuevo `PcBuildLineId`; el arco de origen pasa a caja | línea de pedido | línea de armado), repartida entre
  las posiciones de la sucursal con más disponible. Numeración `ARM-WEB-000001` por empresa. La ranura de cada pieza la
  manda la web o se deduce de la ficha técnica (clave de compatibilidad) o de la categoría; la compatibilidad se evalúa
  con `PcCompatibility.Check` y **se informa** (`hasCompatibilityWarnings`), nunca bloquea. Todo en UNA transacción con
  reintento optimista (3); si falta stock de cualquier pieza no se reserva nada y la respuesta es 409
  `storefront.insufficient_stock` con `shortages[]` (qué piezas y cuánto hay).
- **Idempotencia** (regla S-05): cabecera `Idempotency-Key` obligatoria (o `idempotencyKey` en el cuerpo). El registro va
  en `iam.processed_requests` (la misma tabla del RPC del servidor en la nube) con un id determinista (16 bytes del SHA-256
  de `storefront-reservation:<llave>`), el hash del contenido normalizado y la respuesta guardada: repetir igual devuelve
  la misma reserva con `200` e `Idempotent-Replayed: true`; la misma llave con otro contenido, `422 idempotency`.
- **Consulta y cancelación** (`GetStorefrontReservationQuery`, `CancelStorefrontReservationCommand`): solo con el número Y
  el teléfono con que se hizo (`PcBuild.MatchesPhone`); si no coinciden, 404 sin revelar si existe (regla S-06). Cancelar
  pasa el armado a `Cancelled` con motivo «Cancelada por el cliente desde la tienda web» y devuelve el stock; una reserva
  que ya no está `Reserved` responde `422 pcbuild.state`.
- **Vencimiento**: `StorefrontReservationExpiryService` (gateway, cada `ExpiryMinutes`) corre como el principal técnico y
  ejecuta `ExpirePcBuildReservationsCommand`: los armados `Reserved` con `ReservedUntil` pasado quedan `Cancelled` con motivo
  «Vencida» y el stock vuelve. La API muestra `Expired` tanto a la reserva ya cerrada por el trabajo como a la vencida que
  todavía espera su pasada (regla S-04: nunca se cierra «al leer»).
- **Venta**: `SellPcBuildCommand` de un armado reservado **consume** las reservas (`StockLevel.Fulfill`: no descuenta dos
  veces); `CancelPcBuildCommand` de uno reservado las libera.
- **Eventos**: `pcbuild.reserved`, `pcbuild.released` (con `expired`), `pcbuild.sold` (con `wasReserved`) en el outbox
  (webhooks B2B, `docs/integration/api-gateway-v1.md` §7.2).
- **Bitácora**: `sales.pc_build_events` (append-only): `Created`, `Quoted`, `Reserved`, `Released`, `Expired`, `Sold`,
  `Cancelled`, `Published`, `Unpublished`; la escribe solo `PcBuild`.
- **Privacidad**: el teléfono y el correo van enmascarados a la auditoría (`AuditDetails`), la API pública nunca los
  devuelve y el escritorio los muestra solo a quien tiene `sales.pcbuild.manage` (Ventas, Cajero, Gerencia y Administrador).

## 3. Modelo de datos (migración `V6Storefront`)

| Tabla / columna | Qué guarda |
|---|---|
| `sales.pc_builds.channel` | `Desktop` \| `Web` (los existentes al migrar quedan `Desktop`) |
| `sales.pc_builds.contact_name / contact_phone / contact_email / notes` | Contacto de la reserva (nombre y teléfono obligatorios en canal Web; teléfono normalizado, 7 u 8 dígitos con `+591` si vino) |
| `sales.pc_builds.reserved_at`, `reserved_until`, `cancel_reason` | Vigencia de la reserva y motivo del cierre («Vencida», el del cliente o el del vendedor) |
| `sales.pc_builds.published_to_web` | Armado sugerido visible en la web (solo armados del escritorio cotizados, reservados o vendidos) |
| `sales.pc_build_events` | Bitácora append-only del armado (por sucursal); al migrar se reconstruye una fila `Created` (y `Quoted`, `Sold` o `Cancelled`) por armado existente |
| `inventory.stock_reservations.pc_build_line_id` | Origen «línea de armado» (arco `num_nonnulls(pos_session_id, sales_order_line_id, pc_build_line_id) <= 1`) |
| `iam` | Permisos `storefront.read` y `storefront.reserve` (ADMIN y GERENCIA los reciben); rol `TIENDA_WEB` con ambos más `inventory.stock.view`; usuario técnico `tienda-web@<dominio>` por empresa, en la casa matriz, con credencial aleatoria inutilizable (`TenantProvisioner` lo crea en las empresas nuevas) |
| `iam.processed_requests` | Idempotencia de `POST /reservations` por `Idempotency-Key` |
| `iam.audit_logs.channel` | Canal nuevo `storefront` |

Nuevo estado `PcBuildStatus.Reserved`: `Draft → Quoted → Reserved → Sold | Cancelled`; `Quoted → Sold | Cancelled`. Límites
del dominio: `PcBuild.MaxQuantity` = 16 unidades por línea, `PcBuild.MaxLines` = 20 líneas. Resultado: **153 tablas en 10
esquemas**, 151 políticas `tenant_isolation`, 63 `branch_isolation`, 30 libros append-only (`docs/database/ERD-MINV-V3.md`
§10; `scripts/db_init.sql` regenerado).

## 4. API pública de tienda (resumen; el contrato completo está en `docs/integration/storefront-api-v1.md`)

| Método y ruta | Qué devuelve |
|---|---|
| `GET /storefront/v1/catalog` | Instantánea (`Cache-Control: public, max-age=30`): empresa y sucursales, sucursal de la tienda, categorías (árbol con conteos), marcas, productos (sku, slug, nombre, categoría, marca, precio, precio de lista, disponibilidad, condición, garantía, serie, popularidad, etiquetas, descripción, destacados, ficha técnica), armados publicados, `generatedAt` |
| `GET /storefront/v1/products/{slug}` | El producto con disponibilidad fresca (`available`, `reserved`, `onHand`; `no-cache`) |
| `GET /storefront/v1/products/{sku}/image` | Imagen (PNG/JPEG) con `ETag` y caché de 1 h; `304` con `If-None-Match` |
| `GET /storefront/v1/presets` | Armados publicados (ordenados por precio) con sus líneas, precios cotizados y si todas sus piezas están disponibles |
| `POST /storefront/v1/reservations` | Crea la reserva (`Idempotency-Key`); `201` + `Location` con número, `reservedUntil`, líneas y total; `200` + `Idempotent-Replayed` si se repite; `409 storefront.insufficient_stock` con `shortages[]`; `422 pcbuild.contact_phone` / `pcbuild.slot` / `idempotency`; `400 validation` |
| `GET /storefront/v1/reservations/{number}?phone=` | Estado de una reserva (`Reserved`, `Sold`, `Cancelled`, `Expired`, con `statusText` en español) |
| `POST /storefront/v1/reservations/{number}/cancel` | El cliente libera su reserva (`{ "phone": … }`) |

Límites: 300 lecturas/min por IP (también el limitador global del gateway para este prefijo); 10 reservas o cancelaciones/min
por IP; `429` sin cuerpo. CORS solo para `Minv:Storefront:AllowedOrigins`. Errores en `application/problem+json` con `code`
y `errors`. Documentación viva en `/docs` (etiqueta «Tienda web») y `/docs/v1/openapi.json`.

## 5. Escritorio

Casos de uso listos en `MINV.Application/Tech` (fase A): `GetPcBuildsQuery(Status?, Channel?)` con `PcBuildRow` (canal,
contacto, «reservado hasta», publicado, motivo del cierre, notas, unidades reservadas), `GetPcBuildQuery` con la bitácora
(`History`), `ReservePcBuildCommand(Number, Hours = 48)` (cotización vigente del escritorio → reserva de stock por línea,
todo o nada), `ReleasePcBuildReservationCommand(Number, Reason)`, `PublishPcBuildCommand(Number, Published)`,
`SellPcBuildCommand` (consume la reserva), `CancelPcBuildCommand` (libera) y `GetTechDashboardQuery` con
`WebReservationsActive` y `WebReservationsValue`. Reservar, liberar, publicar y anular exigen `sales.pcbuild.manage`; listar
y ver el detalle, `sales.view`; vender, la caja (`sales.pos.operate` y el registro de salidas).

Pantallas (guía: `docs/product/escritorio-v6.md`):

- **Armador de PC › Cotizaciones**: columnas Canal (insignia «Web»), Contacto (nombre y teléfono; correo en el detalle),
  «Reservado hasta» (resaltado si vence en menos de 6 h o ya venció) y Publicado; filtro rápido «Reservas web» y KPI «Reservas
  web activas»; acciones según estado y permiso: «Reservar stock» (pide las horas, 48 por defecto), «Liberar reserva» (motivo),
  «Vender en caja» (el aviso dice que consume la reserva), «Publicar en la web» / «Quitar de la web». Detalle de una reserva
  web: contacto con «Copiar teléfono», notas del cliente, líneas con disponibilidad y bitácora.
- **Stock y Catálogo**: «Reservado: n» en la lista de stock y en la ficha del producto; disponible = existencias − reservado.
- **Caja**: «Disponible n (reservado m)» en los productos con reservas; no deja vender más que lo disponible (el dominio ya
  lo rechaza: se muestra como aviso claro).
- **Inicio › Tecnología**: tarjeta «Reservas web activas» (cantidad y Bs) con enlace al armador filtrado.
- Capturas nuevas en `docs/product/capturas/v6` (`ScreenshotRunner`: cotización web reservada, lista y detalle, y stock con
  reservado).

## 6. Web

Fase A dejó el contrato; la web (`src/3. Presentation/MINV.WebCatalog/README.md`):

- Puertos `1-domain/ports/ICatalogSource.ts` (`load()`, `product(slug)`, `presets()`) e `IReservationGateway.ts` (`create`,
  `get(number, phone)`, `cancel(number, phone)`); tipos del contrato en `2-application/storefront/*`; único lugar con `fetch`:
  `3-infrastructure/http/{HttpCatalogSource, HttpReservationGateway, api.ts}` (regla S-07; `src/architecture.test.ts` lo
  vigila). `InMemoryCatalogRepository` se hidrata con la instantánea (DTO → `Product`, `Category`, `Brand`, `BuildPreset`;
  `image` absoluta hacia la API).
- `VITE_API_URL` (por defecto `http://localhost:5090`; `.env.example`, `.env.production` para `npm run build`;
  `deploy/Dockerfile.webcatalog` la recibe como `ARG`); `VITE_API_URL=mock` usa los `*.data.ts` de la V5 (pruebas y
  demostraciones sin servidor).
- `CatalogProvider`: cargando (pantalla con skeleton y logotipo), error («Reintentar»), listo; refresco al volver a la
  pestaña (`visibilitychange`) y cada 60 s sin parpadeos; la ficha consulta `GET /products/{slug}` al abrirse.
- Disponibilidad (`1-domain/catalog/stock.ts`): «Disponible (n)», «Últimas n», «Reservado» (disponible 0 y reservado > 0),
  «Agotado»; «Agregar al armado» deshabilitado sin disponible; cantidad máxima = disponible.
- «Finalizar armado» → «Reservar armado»: formulario accesible (nombre, teléfono/WhatsApp con validación boliviana, correo
  opcional, notas), aviso de vigencia («te lo guardamos 48 h; se confirma y paga en la tienda»), envío con `Idempotency-Key`
  (UUID por intento), errores del contrato (409: marca las piezas y cuánto hay), éxito con número, vencimiento, líneas y
  total; vacía el armado; enlaces «Consultar mi reserva» y WhatsApp. Página `/reserva/:numero` (teléfono → estado; «Liberar
  mi reserva» con confirmación). Estado en memoria, nada de storage.
- Armados sugeridos desde la API (si no hay, la sección se oculta); textos: la reserva se guarda en la tienda y se confirma en
  persona; sucursal de retiro = la de la instantánea.

## 7. Límites conocidos

- Una reserva no elige series: las series se asignan al vender en caja (la reserva es por cantidad).
- La tienda muestra el stock de UNA sucursal (la configurada); no hay retiro en otra sucursal ni stock consolidado.
- Sin pagos en línea ni cuentas de cliente: la reserva se confirma y cobra en la tienda física; se consulta o libera con el
  número y el teléfono.
- La compatibilidad se informa, no bloquea (el vendedor la revisa en el escritorio).
- Una reserva vencida se cierra en la pasada siguiente del trabajo del gateway (hasta `ExpiryMinutes`); entre tanto la API la
  muestra `Expired` y el stock sigue reservado.
- La instantánea se arma en cada petición (caché HTTP de 30 s en el navegador): con catálogos muy grandes convendría una
  instantánea materializada.

## 8. Datos de prueba y verificación

- **Datos de prueba** (`LocalDataSeeder`, regla S-09): usuario técnico `tienda-web@techzone.example`; los armados sugeridos
  del catálogo cotizados y compatibles se publican con `PublishPcBuildCommand`; dos reservas web con el mismo caso de uso que
  la web y contactos ficticios `.example`: una vencida hace 2 días (la carga corre el vencimiento: queda «Vencida») y una
  activa de hoy; `usuarios-prueba.txt` menciona el usuario técnico (sin contraseña utilizable), los armados publicados y las
  reservas.
- **Pruebas**: dominio (`PcBuildReservationTests`, `StockTests`: contacto, transiciones, bitácora, arco, 20 líneas),
  infraestructura en memoria (`StorefrontFlowTests`: reservar → stock reservado → vender consume → liberar o vencer devuelve;
  insuficiente y contacto inválido; el escritorio reserva, publica, libera y anula) y PostgreSQL (`V6StorefrontPostgresTests`:
  usuario técnico del aprovisionamiento, CHECK y arco, append-only, RLS; `ModelTests`: datos de la migración = dominio),
  integración del gateway (`StorefrontApiTests`, 7 pruebas: catálogo público y CORS, producto e imagen con caché, reservar
  idempotente y consultar/cancelar con el teléfono, 409 sin reservar nada, vencimiento en segundo plano, venta en caja que
  consume la reserva, límites por IP).
- **Recorrido de punta a punta** (regla S-10): `docs/deployment/inicio-rapido-v6.md` §6 y `storefront-api-v1.md` §6. La fase C
  lo verificó sobre una base temporal (`minv datos-prueba`, 60 días) con la web (Playwright), el gateway y el servidor en la
  nube (RPC como un cliente de escritorio): reservar desde la web → `GetPcBuildsQuery(Reserved, Web)` la lista con contacto y
  vencimiento y la ficha del producto muestra lo reservado → `SellPcBuildCommand` factura y **consume** la reserva (existencias
  −1, reservado −1, disponible igual) → la web muestra la pieza como «Última unidad»/«Agotado» y la reserva **Vendida**;
  `ReservePcBuildCommand` desde el escritorio → «Reservado» en la web y `ReleasePcBuildReservationCommand` lo devuelve; 409
  con `shortages[]` sin reservar nada; vencimiento por el trabajo del gateway (nunca al leer); `Idempotent-Replayed`; CORS
  solo para el origen configurado; 429 al superar 10 reservas o 300 lecturas por minuto.
