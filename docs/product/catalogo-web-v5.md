# Catálogo web M-INV · guía del producto (V5) y su conexión a la base de datos (V6)

> **Nota de la V6 (`Inventario-V6`).** Lo que describe esta guía nació en la V5 como un sitio **solo de presentación**: el
> catálogo salía de un archivo generado (mock) y «Finalizar armado» solo mostraba un resumen. La **V6 lo conecta a la misma
> base de datos que usa el escritorio** a través de la API pública de tienda del API Gateway (`/storefront/v1`): productos,
> precios, imágenes, disponibilidad y armados sugeridos salen de la base, y el visitante **reserva** su armado con nombre y
> teléfono (el stock queda reservado 48 h y el vendedor lo vende en caja o lo libera). El mock queda solo para pruebas y
> para `VITE_API_URL=mock`. Paso a paso: [`docs/deployment/inicio-rapido-v6.md`](../deployment/inicio-rapido-v6.md) ·
> diseño: [`docs/architecture/tienda-web-conectada-v6.md`](../architecture/tienda-web-conectada-v6.md) · contrato:
> [`docs/integration/storefront-api-v1.md`](../integration/storefront-api-v1.md).

## 1. Qué es

El **catálogo web de Tech Zone Gaming S.R.L.** (`src/3. Presentation/MINV.WebCatalog`): un sitio responsivo para los
clientes finales de la tienda de tecnología (componentes de PC, computadoras, monitores, periféricos, consolas, videojuegos,
accesorios, redes y software) con la experiencia **«Armá tu PC»**: el visitante recorre las categorías, elige una pieza por
ranura y ve el total acumulado al instante. Idioma: español de Bolivia; moneda: bolivianos con IVA incluido.

Pila: Vite 8 · React 19 · TypeScript · Tailwind CSS 4 · react-router-dom 7 · lucide-react · vitest + Testing Library. Se
publica como sitio estático (`npm run build` → `dist/`) o en un contenedor (`deploy/Dockerfile.webcatalog`).

## 2. Páginas

| Ruta | Página | Qué hace |
|---|---|---|
| `/` | **Inicio** | Hero con tres productos protagonistas, cifras del catálogo (productos, marcas, armados), carrusel de campañas y secciones de categorías, destacados, ofertas, novedades, PC armadas, consolas, marcas y boletín |
| `/catalogo` · `/catalogo/:categoria` | **Catálogo** | Migas de pan, chips de categorías con conteos, panel de filtros (marcas, precio, etiquetas; en móvil bajo el botón «Filtros»), buscador (`?q=`), orden, vista de grilla o lista y paginación de 12 |
| `/producto/:slug` | **Ficha del producto** | Ilustración grande, insignias (oferta, destacado, serie o IMEI), precio con «Antes» tachado y ahorro, disponibilidad y garantía, ficha técnica, «Va en: …» (la ranura del armador) y «Agregar al armado» / «Ver mi armado» |
| `/arma-tu-pc` (`#armados`) | **Armá tu PC** | Una ranura por tipo de pieza (procesador, placa, memoria, tarjeta de video, almacenamiento, fuente, gabinete, refrigeración y extras), candidatos por ranura, total y progreso, y los **armados sugeridos** para partir de uno |
| `/reserva/:numero` | **Mi reserva** (V6) | Con el número y el teléfono: estado (Reservada, Vendida, Cancelada, Vencida), líneas y total; «Liberar mi reserva» con confirmación |
| `*` | No encontrada | |

En la V5, «Finalizar armado» mostraba un resumen y avisaba que nada se guardaba ni se compraba. En la V6 el botón es
**«Reservar armado»**: formulario con nombre, teléfono o WhatsApp de Bolivia, correo opcional y notas; aviso
de que la tienda guarda el armado 48 horas y se confirma y paga en persona; respuesta con el número `ARM-WEB-…`, el
vencimiento, las líneas y el total, y los enlaces «Consultar mi reserva» y WhatsApp.

## 3. Disponibilidad

| La web dice | Significa |
|---|---|
| **Disponible (n)** | Hay *n* unidades libres en la sucursal de la tienda (disponible = existencias − reservado) |
| **Últimas n** | Quedan pocas |
| **Reservado** (V6) | No queda ninguna libre, pero hay unidades reservadas por otros clientes o por el escritorio: si una reserva se libera o vence, vuelven a estar disponibles |
| **Agotado** | No hay existencias; no se puede agregar al armado |

En la V5 la disponibilidad venía del mock (las piezas de los armados sugeridos nunca quedaban agotadas, a propósito). En la
V6 es la de la base de datos, se refresca sola al volver a la pestaña y cada 60 s, y la ficha consulta la disponibilidad
fresca al abrirse.

## 4. Cómo correrlo

```powershell
cd "src\3. Presentation\MINV.WebCatalog"
npm install                # una sola vez
npm run dev                # http://localhost:5173
npm run build              # sitio estático en dist/ (tsc -b + vite build)
npm run preview            # sirve dist/
npm test                   # pruebas (vitest)
npm run typecheck          # tsc -b --noEmit
npm run lint               # oxlint
npm run generar-catalogo   # regenera el mock desde catalogo-tecnologia.json (Python 3)
```

| Versión | De dónde salen los datos |
|---|---|
| V5 (`Inventario-V5`) | Del mock generado (`3-infrastructure/data/*.data.ts`, 159 productos, 35 categorías, 40 marcas, 6 armados). No necesita servidores |
| V6 (`Inventario-V6`) | De la API pública de tienda: `VITE_API_URL` (por defecto `http://localhost:5090`, el API Gateway de `tools\servidores_locales.ps1`). Con `VITE_API_URL=mock` vuelve al mock (pruebas y demostraciones sin servidor) |

## 5. Arquitectura (limpia, dependencias hacia adentro)

```text
4-presentation  React: app/ (enrutador, proveedores, container.ts), pages/, components/, hooks/, state/
      │ usa los casos de uso por useServices()
2-application   casos de uso y consultas puras sobre los puertos (searchCatalog, getPresets, getSlotCandidates…)
      │ usa
1-domain        tipos y reglas puras sin React: dinero, categorías, productos, stock, ranuras y reductor del armado;
                puertos ICatalogRepository y (V6) ICatalogSource e IReservationGateway
      ▲ implementan los puertos
3-infrastructure InMemoryCatalogRepository (V5: sobre data/*.data.ts; V6: hidratado con la instantánea de la API)
                 (V6) http/: HttpCatalogSource, HttpReservationGateway, api.ts — el ÚNICO lugar con fetch
shared          formato es-BO (Bs 2.049,00), texto sin acentos, constantes
```

- `4-presentation/app/container.ts` es el único archivo de la presentación que importa `3-infrastructure`.
- `src/architecture.test.ts` lee los `import` de todo `src/` y falla si una capa apunta hacia afuera o si algún archivo
  usa `localStorage` o red; en la V6 la única excepción es `3-infrastructure/http` (regla S-07).
- El armado vive en la memoria de la pestaña (`Context + useReducer` con el reductor puro del dominio) y se pierde al
  recargar, a propósito: lo que persiste es la **reserva** en la base de datos (V6).

## 6. Diseño

Tema oscuro gaming con tokens de Tailwind (`bg-bg`, `bg-surface`, `text-primary-text`, `bg-tile`…), tipografía Space
Grotesk + Inter, contraste AA, foco visible, objetivos táctiles de 44 px, animaciones solo con transform y opacity y
respeto de `prefers-reduced-motion`. Íconos de Lucide (sin emojis); las imágenes de los productos son dibujos propios sin
logotipos (en la V6 las sirve la API desde `catalog.product_images`). Los avisos y los cajones se montan en portales fuera
de `#root`. Guía visual general: [`ux-ui-guidelines.md`](ux-ui-guidelines.md).

## 7. Qué no hace

- Sin pagos en línea, sin cuentas de cliente, sin carrito persistente (V5 y V6).
- V5: sin backend, sin base de datos, sin reservas y sin validación de compatibilidad.
- V6: la compatibilidad se informa y no bloquea; la reserva es por cantidad (las series se eligen al vender en caja) y
  muestra el stock de **una** sucursal (la configurada en el gateway). Detalle: [`inicio-rapido-v6.md`](../deployment/inicio-rapido-v6.md) §7.

Documentación del proyecto (variables, modo mock, flujo de reserva, pruebas): `src/3. Presentation/MINV.WebCatalog/README.md`.
