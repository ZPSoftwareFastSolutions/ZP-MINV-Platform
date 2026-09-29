# MINV.WebCatalog · Tienda web de Tech Zone Gaming (V6 · V7 en construcción)

Catálogo web de **Tech Zone Gaming S.R.L.** (tienda boliviana de tecnología: componentes de PC, computadoras, monitores,
periféricos, consolas, videojuegos, accesorios, redes y software) con la experiencia **«Armá tu PC»**: el visitante
recorre las categorías, elige una pieza por ranura, ve el total al instante y **reserva el armado en la tienda**.

Desde la V6 la web está **conectada a la base de datos en la nube del escritorio** a través de la API pública de tienda
del API Gateway (`/storefront/v1`, contrato en `docs/integration/storefront-api-v1.md`): categorías, marcas, productos,
fichas, precios, imágenes, **disponibilidad = existencias − reservado** y armados sugeridos salen de ahí. «Reservar
armado» crea una cotización `ARM-WEB-000001` en la base con el stock de cada pieza reservado 48 horas; el cliente la
consulta o la libera con su teléfono, y la tienda la confirma y cobra en persona. No hay pagos en línea ni validación de
compatibilidad en la web (la revisa un técnico en el escritorio).

La **V7** agrega la **sesión web** (botón «Ingresar», registro de clientes, «Mi cuenta» y el punto de entrada del panel
del personal) contra el servidor en la nube por `/api/v1/web`: ver la sección [V7 · Sesión web](#v7--sesión-web-acceso-y-cuenta-del-cliente).

## Cómo correrlo

```powershell
cd "src\3. Presentation\MINV.WebCatalog"
npm install            # una sola vez
npm run dev            # http://localhost:5173 contra el gateway de VITE_API_URL (por defecto http://localhost:5090)
npm run build          # sitio estático en dist/ (tsc -b + vite build) con VITE_API_URL de .env.production
npm run preview        # sirve dist/
npm test               # vitest: dominio, aplicación, adaptador HTTP con fetch simulado y presentación (sobre el mock)
npm run typecheck      # tsc -b --noEmit
npm run lint           # oxlint
npm run generar-catalogo   # regenera el mock de la V5 desde catalogo-tecnologia.json (Python 3)
```

Para verla con datos reales hace falta el gateway con la tienda configurada (empresa `TECHZONE`, sucursal `CM`, CORS
para este origen), por ejemplo `tools\servidores_locales.ps1 -Accion iniciar` sobre la base local, o a mano:

```powershell
$env:Minv__Storefront__TenantCode = 'TECHZONE'; $env:Minv__Storefront__AllowedOrigins__0 = 'http://localhost:5173'
dotnet run --project "src/3. Presentation/MINV.ApiGateway" -- --urls http://localhost:5090   # MINV_DB con el rol minv_server
```

### Variables de entorno (`.env.example`)

| Variable | Valor | Qué hace |
|---|---|---|
| `VITE_API_URL` | `http://localhost:5090` (por defecto si falta o está vacía) | Base del API Gateway; la web llama a `<base>/storefront/v1/*` y pide las imágenes a `<base>/storefront/v1/products/{sku}/image`. En producción, la URL pública del gateway (TLS), autorizada en `Minv:Storefront:AllowedOrigins`. |
| `VITE_API_URL` | `mock` | **Modo mock**: usa `3-infrastructure/data/*.data.ts` (generados de la V5) y una pasarela de reservas EN MEMORIA que respeta el contrato (todo o nada, `ARM-WEB-n`, consulta y liberación con el teléfono). Sin red; al recargar se pierde todo. Es lo que usan las pruebas (`src/test-setup.ts`). |

`.env.production` fija el valor de `npm run build`; la variable de entorno del proceso tiene prioridad (así
`deploy/Dockerfile.webcatalog` la recibe como `ARG VITE_API_URL` y `docker-compose.yml` la pasa desde
`MINV_WEB_API_URL`). Para desarrollo local con otro puerto, cree `.env.local` (no se versiona).

Pila: Vite 8 · React 19 · TypeScript 6 · Tailwind CSS 4 (tokens en `src/index.css`) · react-router-dom 7 · lucide-react ·
clsx · vitest + Testing Library. Alias `@/` → `src/`.

## Arquitectura (limpia, dependencias hacia adentro)

```text
4-presentation  React: app/ (enrutador, CatalogProvider, container.ts), pages/, components/, hooks/, state/
      │ usa los casos de uso por useServices() (catálogo síncrono sobre la instantánea + reservas + ficha fresca)
2-application   casos de uso puros: consultas del catálogo, armador, reservas; storefront/ = contrato (DTO) y mapeo
      │ usa
1-domain        tipos y reglas puras sin React: dinero, categorías, productos, stock (disponible/reservado), ranuras,
                reductor del armado, contacto de la reserva (teléfono boliviano), errores de la tienda
      ▲ implementa los puertos ICatalogRepository · ICatalogSource · IReservationGateway
3-infrastructure http/ (StorefrontApi: ÚNICO fetch · HttpCatalogSource · HttpReservationGateway) · InMemoryCatalogRepository
                 data/ (mock GENERADO de la V5 + MockCatalogSource + InMemoryReservationGateway para VITE_API_URL=mock)
shared          formato es-BO (Bs 2.049,00), texto sin acentos, constantes de contacto
```

- `4-presentation/app/container.ts` es el **único** archivo de la presentación que importa `3-infrastructure`:
  `createSources()` elige API o mock según `VITE_API_URL` (el mock se descarga en un fragmento aparte solo en ese modo)
  y `createServices()` arma los casos de uso sobre una instantánea ya cargada.
- La red vive **solo** en `3-infrastructure/http/api.ts` (regla S-07): el resto de la web no usa `fetch`, `localStorage`
  ni ningún almacenamiento. `src/architecture.test.ts` lo comprueba leyendo los `import` y el código de todo `src/`.
- La infraestructura conoce de la aplicación únicamente `2-application/storefront` (el contrato JSON y su mapeo).
- Idioma de la interfaz, comentarios y documentación: español (Bolivia). Moneda: bolivianos con IVA incluido (13 %,
  informativo).

## Flujo de datos

1. **Carga.** `CatalogProvider` pide `GET /storefront/v1/catalog` (`ICatalogSource.load()`), muestra un esqueleto con el
   logotipo mientras llega y una pantalla de error con «Reintentar» si falla (red caída, 429, 503). Con la instantánea
   hidrata `InMemoryCatalogRepository` y entrega los casos de uso síncronos a las páginas.
2. **Frescura.** La instantánea se refresca cada 60 s con la pestaña visible y al volver a la pestaña (si tiene más de
   15 s), sin parpadeos: la anterior sigue en pantalla hasta que llega la nueva; si un refresco falla se conserva. Las
   piezas del armado se sincronizan con su versión fresca (precio y disponibilidad). La ficha del producto consulta
   `GET /products/{slug}` al abrirse (`useFreshProduct`) y muestra disponible y reservado.
3. **Disponibilidad.** `stock` = disponible (existencias − reservado). Estados: «Disponible (n)», «Últimas n unidades»,
   «Reservado» (0 disponible pero hay unidades reservadas que pueden volver) y «Agotado». Tarjetas, filas, ficha,
   candidatos del armador y resumen los muestran; «Agregar al armado» se deshabilita sin disponible y la cantidad máxima
   de una pieza en el armado es lo disponible (tope 10).
4. **Reserva.** «Reservar armado» abre el formulario (nombre, teléfono o WhatsApp de Bolivia con validación, correo
   opcional, notas), muestra las piezas y el aviso «te lo guardamos 48 h; se confirma y paga en la tienda», y envía
   `POST /reservations` con una cabecera `Idempotency-Key` (UUID por intento). Éxito: número, vencimiento, líneas a
   precios congelados, total, «Consultar mi reserva» y WhatsApp; el armado se vacía y el catálogo se refresca. Errores
   del contrato: 409 `insufficient_stock` marca las piezas afectadas con cuánto hay y ofrece «Ajustar a lo disponible»;
   400/422 muestran el `detail` del servidor; 429 y red caída, un texto propio.
5. **Consulta y liberación.** `/reserva` y `/reserva/:numero`: número + teléfono → estado (Reservada / Vendida /
   Cancelada / Vencida) con piezas, total y sucursal de retiro; «Liberar mi reserva» con confirmación
   (`POST /reservations/{number}/cancel`). Si el teléfono no coincide la API responde 404 y la web dice «no encontramos
   esa reserva» (regla S-06).
6. **Armados sugeridos.** Los publicados desde el escritorio (`catalog.presets`); si no hay, la portada muestra un estado
   vacío discreto y el armador un aviso sin galería.

## Estructura de carpetas

```text
src/
  1-domain/
    catalog/     types.ts (Product con stock = disponible y reserved) · money.ts · categories.ts · products.ts · stock.ts
    builder/     types.ts · slots.ts (BUILD_SLOTS) · build.ts (reductor, tope por disponible, total, progreso, faltantes)
    storefront/  types.ts (StoreInfo, CatalogSnapshot, Reservation…) · contact.ts (teléfono boliviano, formulario) · errors.ts
    ports/       ICatalogRepository.ts · ICatalogSource.ts · IReservationGateway.ts
  2-application/
    catalog/     types.ts · queries.ts · filterTree.ts        builder/  queries.ts (ranuras, candidatos, armados)
    storefront/  dto.ts (contrato JSON) · mappers.ts (DTO ↔ dominio, problem+json → StorefrontError) · reservations.ts
    index.ts     createCatalogUseCases(repo) · createReservationUseCases(gateway)
  3-infrastructure/
    http/        api.ts (StorefrontApi, único fetch) · HttpCatalogSource.ts · HttpReservationGateway.ts
    data/        catalog.data.ts (159) · categories.data.ts (35) · brands.data.ts (40) · presets.data.ts (6) · mockCatalog.ts
    InMemoryCatalogRepository.ts
  4-presentation/
    app/         App.tsx · router.tsx · routes.ts · container.ts · ServicesProvider.tsx
    state/       CatalogProvider.tsx (carga, error, refresco) · BuilderProvider.tsx (armado en memoria)
    hooks/       useServices · useStore · useFreshProduct · useBuilder · useToast · useDocumentTitle · useMediaQuery
    components/  ui/ · product/ (StockIndicator…) · reservation/ (ReservationSummary) · layout/ · feedback/ (CatalogScreens)
    pages/       home/ · catalog/ · product/ · builder/ (ReserveDialog) · reservation/ (ReservationPage) · NotFoundPage
  shared/        format.ts · text.ts · constants.ts
  test-utils.tsx renderWithApp() y orígenes en memoria para las pruebas de la presentación
public/images/   logo-256.png · products/*.png (el mock; con la API las imágenes vienen del gateway)
tools/           generar_catalogo_web.py
```

## Rutas

| Ruta | Página |
|---|---|
| `/` | Inicio (hero, destacados, ofertas, categorías, armados publicados) |
| `/catalogo` · `/catalogo/:categoria` | Catálogo con filtros en cliente (`?q=`, `?tags=oferta`, marcas, precio) |
| `/producto/:slug` | Ficha del producto con disponibilidad fresca (disponible y reservado), agregar al carrito, reservar ahora y agregar al armado |
| `/arma-tu-pc` (`#armados`) | Armá tu PC paso a paso, armados sugeridos y **Reservar armado** |
| `/reserva` · `/reserva/:numero` | Consultar mi reserva (número + teléfono), liberar |
| `/carrito` | Carrito de compras (V7) |
| `*` | Página no encontrada |

## Pruebas

`npm test` corre con `VITE_API_URL=mock` (`src/test-setup.ts`): dominio y aplicación de la V5 sobre el mock, mapeo DTO →
dominio (`2-application/storefront/storefront.test.ts`), adaptador HTTP con `fetch` simulado (éxito, 404, 409 con
faltantes, 422, 429 sin cuerpo, red caída, `Idempotency-Key` e `Idempotent-Replayed`), pasarela en memoria,
`CatalogProvider` (cargando, error y reintento, refresco sin parpadeo), formulario de reserva (validación, envío, 409 y
ajuste) y la página de consulta (404, estado y liberación). `src/architecture.test.ts` vigila las capas y el único `fetch`.

## Decisiones

- **Una sola verdad.** Todo lo que se muestra sale de la API de tienda (regla S-01); el mock queda para las pruebas y
  para revisar el diseño sin servidor (`VITE_API_URL=mock`).
- **Repositorio síncrono sobre una instantánea.** La red se concentra en una carga (y refrescos en segundo plano); las
  páginas calculan con `useMemo` sin estados de carga. Solo la ficha y las reservas vuelven a la red.
- **Estado en memoria.** El armado, la última reserva mostrada y los formularios viven en React. Lo que persiste es la
  reserva en la base de datos de la tienda. Desde la V7 el navegador guarda UNA cosa: el carrito (solo SKU y cantidad).
- **Idempotencia.** Cada intento de envío genera un UUID nuevo (`Idempotency-Key`); si la API devuelve la misma reserva
  (`Idempotent-Replayed`), la confirmación lo dice.
- **Fragmentos de producción.** `vite.config.ts` separa el mock (`catalogo`) y las bibliotecas (`vendor`) del código de
  la aplicación; el mock solo se descarga en modo mock.
- **Diseño.** Tema oscuro gaming con tokens (`bg-bg`, `bg-surface`, `text-primary-text`, `bg-tile`…), tipografía Space
  Grotesk + Inter, contraste AA, foco visible, objetivos táctiles ≥ 44 px, animaciones solo con transform/opacity y
  respeto de `prefers-reduced-motion`. Íconos de Lucide (sin emojis). Avisos, cajones y diálogos se montan en portales
  sobre `<body>`.

## V7 · Sesión web, acceso y cuenta del cliente

Reglas: `.claude/v7-web-platform-rules.md` (P-01 a P-14) · diseño: `docs/architecture/plataforma-web-v7.md`.

### Qué hay

| Ruta | Quién entra | Pantalla |
|---|---|---|
| `/ingresar` | todos | Correo y contraseña (mostrar u ocultar), mensaje único del servidor, aviso de cuenta bloqueada, enlace a registrarse. Con credenciales incorrectas NO se sale de la pantalla |
| `/registrarse` | todos | Nombre, correo, teléfono, contraseña y repetirla; indicador de requisitos; errores por campo; aviso si el correo ya tiene cuenta. Crea SIEMPRE una cuenta de cliente |
| `/cambiar-contrasena` | cualquier sesión | Cambio de contraseña; obligatoria cuando la sesión llega con `mustChangePassword` |
| `/mi-cuenta` · `/mi-cuenta/reservas` · `/datos` · `/contrasena` | sesión de **cliente** | «Mis reservas» (filtro por estado, detalle, liberar), «Mis datos» (nombre, teléfono, documento para la factura) y «Cambiar contraseña» |
| `/panel/*` | sesión del **personal** | Punto de montaje del panel: `4-presentation/panel/PanelRoot.tsx` (provisional «Panel en construcción»; lo reemplaza el paquete W3) |
| `/carrito` | todos | El carrito de compras (ver «V7 · Carrito de compras») |
| `/reservar` | todos | Provisional (`pages/cart/CheckoutPage.tsx`; la construye el paquete siguiente). Recibe el carrito o un artículo suelto (`?sku=…&cantidad=…`) |

Después de ingresar: el personal va a `/panel` y el cliente a `/mi-cuenta`, o a `volver` si es una ruta interna que su
tipo de sesión puede ver. Las pantallas de la V7 se descargan solo al visitarlas (carga diferida en
`4-presentation/app/routeTable.tsx`).

### Seguridad de la sesión

- El token viaja SOLO en una cookie `HttpOnly` que pone y borra el servidor: el JavaScript no la ve. La sesión vive en
  la memoria de React (`SessionProvider`); **nada de la sesión se guarda en el navegador** y al recargar se vuelve a
  preguntar con `GET /api/v1/web/session`.
- Toda petición a `/api/v1/web/*` va al **mismo origen** (`mode` y `credentials: 'same-origin'`) con la cabecera
  `X-MINV-Client-Version: 7.0.0`. En producción el nginx del catálogo reenvía esa ruta al servidor en la nube; en
  desarrollo lo hace el proxy de Vite (`vite.config.ts`, destino `MINV_CLOUD_URL`, por defecto `http://localhost:5080`).
- `volver` solo acepta rutas internas que empiezan con una sola «/»: `//evil.example`, `https://…`, `/\evil` o
  `javascript:…` se ignoran (`2-application/auth/navigation.ts`, `safeReturnPath`).
- Un 401 en cualquier pedido RPC limpia la sesión y manda a ingresar conservando a dónde iba la persona. El cambio de
  contraseña responde 401 cuando la contraseña ACTUAL es incorrecta: antes de dar la sesión por vencida se comprueba
  con el servidor (`2-application/auth/watchedRpc.ts`).
- Las guardas de ruta y los botones ocultos son comodidad: permisos, módulo, sucursal y validación los decide el
  servidor en cada pedido (regla P-01).

### Cómo se usa desde otra pantalla

```tsx
import { useSession } from '@/4-presentation/hooks/useSession';
import { useRpc, useAccount } from '@/4-presentation/hooks/useRpc';
import type { RpcResponseOf } from '@/4-presentation/app/contract';

const { session, can, login, logout } = useSession();          // session: Session | null
const rpc = useRpc();                                           // IRpcGateway<RpcOperations>, vigilado (401 → ingresar)
const filas: RpcResponseOf<'GetMyReservationsQuery'> = await rpc.send('GetMyReservationsQuery', {});
```

- **Idempotencia.** `rpc.send(operación, payload, { requestId })`: un UUID nuevo por intento de la persona y el MISMO si
  se reintenta por red caída. `AttemptKey` (`2-application/auth/requestId.ts`) lo lleva por usted:
  `attempt.run((requestId) => rpc.send('…', payload, { requestId }))`.
- **Errores.** Toda falla llega como `WebApiError` (`1-domain/auth/errors.ts`) con `kind`, `message`, `errors`, `code`,
  `status` y `requestId`; `describeWebApiError(error, 'store' | 'panel')` da el texto para mostrar.
- **Contrato.** `3-infrastructure/http/contract.generated.ts` lo genera `minv contrato-web` (hoy es un archivo
  PROVISIONAL con la misma forma). Solo lo importa el adaptador `3-infrastructure/http/contract.ts`; la presentación
  toma los tipos de `@/4-presentation/app/contract`.

### Modo mock (`VITE_API_URL=mock`)

Sesión y RPC en memoria (`3-infrastructure/data/mockWeb.ts`) con dos usuarios de muestra que la pantalla de ingreso
ofrece con un botón: uno del personal (administrador) y un cliente con reservas en distintos estados. Se comporta como
el servidor (mensaje único, bloqueo a los 5 intentos, registro siempre como cliente, idempotencia por `requestId`). Todo
se pierde al recargar la página.

### Pruebas de la V7

Funciones puras (`1-domain/auth`, `1-domain/account`, `2-application/auth`), adaptador HTTP con `fetch` simulado
(`3-infrastructure/http/webApi.test.ts`: cabecera propia, `credentials`, las dos formas de respuesta, errores, 429 y
401), servidor en memoria, `SessionProvider`, tabla de rutas real con sus guardas (`app/routeTable.test.tsx`: `volver`
malicioso, cambio de contraseña obligatorio, sesión vencida) y las pantallas. `src/architecture.test.ts` vigila el único
adaptador del contrato, los dos únicos archivos con `fetch`, que `localStorage` exista solo en
`3-infrastructure/storage/*` (el carrito) y que nada de la sesión toque el almacenamiento.

## V7 · Carrito de compras

Pedido del cliente: la tienda funciona como **carrito de compras**; quien quiere un solo monitor lo reserva sin pasar por
«Armá tu PC», que sigue funcionando igual.

### Qué hay

- **En cada tarjeta, fila y ficha de producto** con disponibilidad (también consolas, juegos, portátiles y lo que antes
  solo ofrecía «Consultar por WhatsApp»): **«Agregar al carrito»** y **«Reservar ahora»**. Las piezas del armador
  conservan «Agregar al armado» y el resto conserva «Consultar por WhatsApp», las dos como opciones secundarias.
- **«Reservar ahora»** lleva directo a `/reservar?sku=<SKU>&cantidad=<n>` con ESE solo artículo: no pasa por el carrito
  ni lo modifica.
- **Aviso al agregar**: «Agregado al carrito» con «Ver carrito» (o por qué no entró más: todo lo disponible, el máximo
  por producto o el carrito lleno).
- **Ícono del carrito** con las unidades en la cabecera (escritorio y móvil) y en el menú móvil.
- **`/carrito`**: lista con imagen, nombre, precio, cantidad (más y menos), subtotal y «Quitar»; total; «Seguir
  comprando»; «Vaciar carrito» con confirmación; «Reservar» → `/reservar`. Al abrirse vuelve a consultar el catálogo a la
  tienda: marca lo que **se agotó**, lo que **bajó** por debajo de lo pedido y lo que ya no está publicado, y ofrece
  ajustar (línea por línea o todo junto). El carrito nunca se corrige solo y no deja reservar mientras haya algo por
  ajustar.

### Reglas

| Regla | Dónde |
|---|---|
| Una línea por SKU; 16 unidades por producto y 20 productos distintos (los topes de la reserva, regla S-05) | `1-domain/cart/cart.ts` (`CART_LIMITS`) |
| La cantidad nunca supera lo disponible del producto | `addToCart`, `setCartQuantity`, `maxCartQuantity` |
| Total con aritmética exacta de centavos (3 × Bs 0,10 = Bs 0,30) | `1-domain/cart/totals.ts` |
| Cruce con el catálogo fresco: `ok`, `reduced` (bajó), `sold_out` (se agotó) y `unavailable` (ya no está) | `2-application/cart/review.ts` |
| Lo agotado y lo que ya no está no entran al total | `reviewCart` (`counted`) |

### Qué se guarda en el navegador

Una sola entrada, **`minv.carrito`**, con la versión del formato y el SKU y la cantidad de cada línea:

```json
{"v":1,"items":[{"sku":"MON-AOC-24G4","quantity":1}]}
```

Nada más: ni precios, ni nombres, ni datos de la persona, ni nada de la sesión. `3-infrastructure/storage/cartStorage.ts`
es el ÚNICO archivo de la web que usa el almacenamiento del navegador (lo vigila `src/architecture.test.ts`). Tolera un
almacenamiento lleno, bloqueado o con datos que no sirven (texto roto, otra versión del formato): leer devuelve un
carrito vacío, guardar devuelve `false` y el carrito sigue funcionando en memoria (la página avisa que no se está
guardando). Entre pestañas se sincroniza con el evento `storage`. Si el navegador no ofrece almacenamiento, se usa
`memoryCartStore.ts` (el mismo puerto, en memoria), que también usan las pruebas.

### Cómo se usa desde otra pantalla

```tsx
import { useCart } from '@/4-presentation/hooks/useCart';
import { useAddToCart } from '@/4-presentation/hooks/useAddToCart';
import { checkoutSelection } from '@/2-application';
import { ROUTES } from '@/4-presentation/app/routes';

const { lines, review, count, total, persistent, clear } = useCart();   // líneas ya cruzadas con el catálogo
const addToCart = useAddToCart();                                        // agrega y muestra el aviso
<Link to={ROUTES.checkoutItem(product.sku, 2)}>Reservar ahora</Link>     // /reservar?sku=…&cantidad=2

// En /reservar: qué se reserva (el artículo de la dirección o el carrito), ya cruzado con el catálogo.
const { source, items, review } = checkoutSelection(location.search, { items: cart.items }, (sku) => catalog.getProductBySku(sku));
// Al confirmar la reserva, el carrito se vacía SOLO si `source === 'cart'`.
```

### Cambios visibles en la cabecera

Para que el carrito entre sin desplazamiento horizontal: el botón «Armá tu PC» de la cabecera aparece desde 1024 px
(antes, desde 640 px; por debajo está en el menú) y, por debajo de 480 px, el ícono «Mi armado» aparece solo cuando hay
un armado en curso.

### Pruebas del carrito

Dominio (`1-domain/cart/cart.test.ts`), casos de uso con un almacén simulado (`2-application/cart/cart.test.ts`),
almacenamiento (`3-infrastructure/storage/cartStorage.test.ts`: datos rotos, versión vieja, almacenamiento que lanza,
aviso entre pestañas), proveedor (`state/CartProvider.test.tsx`), botones de la tarjeta y la fila
(`components/product/ProductActions.test.tsx`), de la ficha (`pages/product/PurchaseBox.test.tsx`) y la página del
carrito con la tabla de rutas real (`pages/cart/CartPage.test.tsx`).
