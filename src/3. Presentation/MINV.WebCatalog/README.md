# MINV.WebCatalog · Tienda web de Tech Zone Gaming (V6)

Catálogo web de **Tech Zone Gaming S.R.L.** (tienda boliviana de tecnología: componentes de PC, computadoras, monitores,
periféricos, consolas, videojuegos, accesorios, redes y software) con la experiencia **«Armá tu PC»**: el visitante
recorre las categorías, elige una pieza por ranura, ve el total al instante y **reserva el armado en la tienda**.

Desde la V6 la web está **conectada a la base de datos en la nube del escritorio** a través de la API pública de tienda
del API Gateway (`/storefront/v1`, contrato en `docs/integration/storefront-api-v1.md`): categorías, marcas, productos,
fichas, precios, imágenes, **disponibilidad = existencias − reservado** y armados sugeridos salen de ahí. «Reservar
armado» crea una cotización `ARM-WEB-000001` en la base con el stock de cada pieza reservado 48 horas; el cliente la
consulta o la libera con su teléfono, y la tienda la confirma y cobra en persona. No hay pagos en línea, sesión,
carrito persistente ni validación de compatibilidad en la web (la revisa un técnico en el escritorio).

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
| `/producto/:slug` | Ficha del producto con disponibilidad fresca (disponible y reservado), agregar al armado |
| `/arma-tu-pc` (`#armados`) | Armá tu PC paso a paso, armados sugeridos y **Reservar armado** |
| `/reserva` · `/reserva/:numero` | Consultar mi reserva (número + teléfono), liberar |
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
- **Estado en memoria.** El armado, la última reserva mostrada y los formularios viven en React; nada se guarda en el
  navegador. Lo que persiste es la reserva en la base de datos de la tienda.
- **Idempotencia.** Cada intento de envío genera un UUID nuevo (`Idempotency-Key`); si la API devuelve la misma reserva
  (`Idempotent-Replayed`), la confirmación lo dice.
- **Fragmentos de producción.** `vite.config.ts` separa el mock (`catalogo`) y las bibliotecas (`vendor`) del código de
  la aplicación; el mock solo se descarga en modo mock.
- **Diseño.** Tema oscuro gaming con tokens (`bg-bg`, `bg-surface`, `text-primary-text`, `bg-tile`…), tipografía Space
  Grotesk + Inter, contraste AA, foco visible, objetivos táctiles ≥ 44 px, animaciones solo con transform/opacity y
  respeto de `prefers-reduced-motion`. Íconos de Lucide (sin emojis). Avisos, cajones y diálogos se montan en portales
  sobre `<body>`.
