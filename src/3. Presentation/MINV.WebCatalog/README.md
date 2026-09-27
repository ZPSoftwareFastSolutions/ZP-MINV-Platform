# MINV.WebCatalog · Catálogo web de Tech Zone Gaming (V5)

Catálogo web de **Tech Zone Gaming S.R.L.** (tienda boliviana de tecnología: componentes de PC, computadoras, monitores,
periféricos, consolas, videojuegos, accesorios, redes y software) con la experiencia **«Armá tu PC»**: el visitante
recorre las categorías, elige una pieza por ranura y ve el total acumulado al instante.

Es **solo frontend de presentación**: no hay backend, base de datos, autenticación, carrito persistente, pagos ni
validación de compatibilidad. Los productos son un mock local generado desde el catálogo de tecnología de la V4.2 y el
armado vive en memoria de React (se pierde al recargar la página, a propósito).

## Cómo correrlo

```powershell
cd "src\3. Presentation\MINV.WebCatalog"
npm install            # una sola vez
npm run dev            # http://localhost:5173
npm run build          # sitio estático en dist/ (tsc -b + vite build)
npm run preview        # sirve dist/
npm test               # pruebas de dominio y aplicación (vitest)
npm run typecheck      # tsc -b --noEmit
npm run lint           # oxlint
npm run generar-catalogo   # regenera el mock desde catalogo-tecnologia.json (Python 3)
```

Pila: Vite 8 · React 19 · TypeScript 6 · Tailwind CSS 4 (tokens en `src/index.css`) · react-router-dom 7 · lucide-react ·
clsx · vitest + Testing Library. Alias `@/` → `src/`.

## Arquitectura (limpia, dependencias hacia adentro)

```text
4-presentation  React: app/ (enrutador, proveedores, container.ts), pages/, components/, hooks/, state/
      │ usa los casos de uso por useServices()
2-application   casos de uso / consultas puras sobre el puerto (searchCatalog, getPresets, getSlotCandidates…)
      │ usa
1-domain        tipos y reglas puras sin React: dinero, categorías, productos, stock, ranuras y reductor del armado
      ▲ implementa el puerto ICatalogRepository
3-infrastructure InMemoryCatalogRepository sobre data/*.data.ts (GENERADOS: no editar a mano)
shared          formato es-BO (Bs 2.049,00), texto sin acentos, constantes
```

- `4-presentation/app/container.ts` es el **único** archivo de la presentación que importa `3-infrastructure`.
- El dominio no conoce React ni la infraestructura; los casos de uso reciben el repositorio por el puerto.
- `src/architecture.test.ts` comprueba estas reglas leyendo los `import` de todo `src/` (sin las pruebas) y falla si
  una capa apunta hacia afuera o si algún archivo usa `localStorage`, `fetch` u otro acceso a red o almacenamiento.
- Idioma de la interfaz, comentarios y documentación: español (Bolivia). Moneda: bolivianos con IVA incluido (13 %,
  informativo).

## Estructura de carpetas

```text
src/
  1-domain/
    catalog/   types.ts · money.ts · categories.ts · products.ts · stock.ts
    builder/   types.ts · slots.ts (BUILD_SLOTS) · build.ts (reductor, total, progreso, faltantes)
    ports/     ICatalogRepository.ts
  2-application/
    catalog/   types.ts (SearchCatalogQuery/Result, facetas) · queries.ts
    builder/   queries.ts (ranuras, candidatos, armados sugeridos)
    index.ts   createCatalogUseCases(repo)
  3-infrastructure/
    data/      catalog.data.ts (159 productos) · categories.data.ts (35) · brands.data.ts (40) · presets.data.ts (6)
    InMemoryCatalogRepository.ts
  4-presentation/
    app/         App.tsx · router.tsx · routes.ts · container.ts · ServicesProvider.tsx
    state/       BuilderProvider.tsx (armado en memoria)
    hooks/       useServices · useBuilder · useToast · useDocumentTitle · useMediaQuery
    components/  ui/ (kit) · product/ (tarjetas, ficha técnica) · layout/ (cabecera, navegación, cajón, pie) · feedback/
    pages/       home/ · catalog/ · product/ · builder/ · NotFoundPage · RouteErrorPage
  shared/        format.ts · text.ts · constants.ts
public/images/   logo-256.png · products/*.png (dibujos propios, 360×360)
tools/           generar_catalogo_web.py
```

## Rutas

| Ruta | Página |
|---|---|
| `/` | Inicio (hero, destacados, ofertas, categorías) |
| `/catalogo` · `/catalogo/:categoria` | Catálogo con filtros en cliente (`?q=`, `?tags=oferta`, marcas, precio) |
| `/producto/:slug` | Ficha del producto (imagen, descripción, especificaciones, agregar al armado) |
| `/arma-tu-pc` (`#armados`) | Armá tu PC paso a paso y armados sugeridos |
| `*` | Página no encontrada |

## Decisiones

- **Solo frontend.** Sin rutas de servidor, servicios externos ni almacenamiento (ni `localStorage`): el armado es
  estado en memoria (`Context + useReducer` con el reductor puro del dominio).
- **Mock generado.** `npm run generar-catalogo` reconstruye `data/*.data.ts` desde
  `src/2. Infrastructure/MINV.Infrastructure/Seeding/Tecnologia/catalogo-tecnologia.json` de forma determinista
  (las piezas de los armados sugeridos nunca quedan agotadas).
- **Fragmentos de producción.** `vite.config.ts` separa el mock (`catalogo`) y las bibliotecas (`vendor`) del código de
  la aplicación, así el navegador guarda en caché lo que no cambia entre versiones.
- **Repositorio síncrono.** El puerto devuelve datos en memoria sin promesas: las páginas calculan con `useMemo` y no
  necesitan estados de carga (los esqueletos quedan para imágenes y suspensión futura).
- **Diseño.** Tema oscuro gaming con tokens (`bg-bg`, `bg-surface`, `text-primary-text`, `bg-tile`…), tipografía Space
  Grotesk + Inter, contraste AA (`text-muted` ≥ 7:1 y `text-faint` ≥ 4,5:1 sobre todas las superficies), foco visible,
  objetivos táctiles ≥ 44 px, animaciones solo con transform/opacity y respeto de `prefers-reduced-motion`. Íconos de
  Lucide (sin emojis). Los avisos (toasts) y los cajones se montan en portales sobre `<body>`, fuera de `#root`, para
  que sigan siendo usables mientras un cajón deja el resto de la página inerte.
