# Informe del paquete W2a carrito (V7)

## Resumen

W2a (el carrito, tarea 9 primera parte) está terminado y todo pasa. Al empezar, `git log` mostró que un intento anterior del paquete ya había dejado casi todo guardado en el commit dbe3569. `git status` de la carpeta de la web estaba limpio, así que no había cambios sin guardar. Revisé ese avance punto por punto contra el pedido, sin rehacerlo, y lo comprobé con las pruebas y en un navegador real.

Qué hay:
1. **Dominio puro** en `1-domain/cart`:
   - Una línea por SKU. Se puede agregar, cambiar la cantidad, quitar y vaciar.
   - Topes: 16 unidades por producto y 20 productos distintos (los mismos que la reserva, regla S-05).
   - La cantidad nunca supera lo disponible del producto.
   - El total se calcula en centavos exactos.
   - El puerto `ICartStore` está en `1-domain/ports`.
2. **Casos de uso** en `2-application/cart`:
   - Casos de uso sobre el puerto (`createCartUseCases`).
   - Cruce con el catálogo fresco: cada línea queda como ok, bajó, se agotó o ya no está publicada, y se ofrece ajustar (una línea o todas). El carrito nunca se corrige solo.
   - El contrato de la dirección de `/reservar`.
3. **Infraestructura**:
   - `cartStorage.ts` es el único lugar con `localStorage`. Guarda una sola entrada, `minv.carrito`, con la versión y solo SKU y cantidad.
   - Tolera almacenamiento lleno, bloqueado o con datos rotos o de otra versión: la página no se rompe.
   - Se sincroniza entre pestañas con el evento `storage`.
   - `memoryCartStore.ts` es la versión en memoria, para las pruebas y para cuando el navegador no deja guardar.
4. **Presentación**:
   - `CartProvider`, `useCart`, `useCartState`, `useCartActions` y `useAddToCart`. Al agregar sale el aviso «Agregado al carrito» con «Ver carrito».
   - Ícono del carrito con las unidades en la cabecera (escritorio y móvil) y en el menú móvil.
   - `ProductActions` está en cada tarjeta y en cada fila del catálogo. `PurchaseBox` (la ficha) tiene cantidad, «Agregar al carrito» y «Reservar ahora» para todo producto con disponibilidad, también consolas, juegos y portátiles.
   - «Reservar ahora» lleva a `/reservar?sku=…&cantidad=…` sin tocar el carrito ni el armado.
   - Como opciones secundarias quedan «Agregar al armado» (piezas del armador) y «Consultar por WhatsApp» (el resto).
   - «Armá tu PC» sigue igual.
5. **`/carrito`**:
   - Cada línea muestra imagen, nombre, precio, cantidad con más y menos, subtotal, «Quitar» y la disponibilidad recién consultada (se vuelve a pedir al abrir la página y con «Actualizar»).
   - Marca lo que se agotó, lo que bajó y lo que ya no está publicado. No deja reservar hasta ajustar.
   - Además: total y ahorro, «Seguir comprando», «Vaciar carrito» con confirmación, «Reservar» → `/reservar`, carrito vacío con invitación al catálogo, y aviso si el carrito no se está guardando.
6. **Pruebas**: dominio, almacenamiento (datos rotos, versión vieja y futura, almacenamiento que lanza, aviso entre pestañas), casos de uso, proveedor, botones de la tarjeta, de la fila y de la ficha, y la página del carrito con la tabla de rutas real. Todas usan puertos simulados y ninguna toca la red.

Lo único que cambié en esta sesión: el texto de la página provisional `CheckoutPage.tsx` (misma exportación, misma ruta). Antes decía «La reserva del carrito está en construcción» también cuando se llegaba por «Reservar ahora» con un solo artículo. Ahora distingue las dos entradas con `isSingleItemCheckout`, y el comentario de cabecera documenta el contrato para el paquete siguiente.

Prueba en navegador con el servidor de desarrollo en modo mock (127.0.0.1:5183, ya detenido):
- No hay desplazamiento horizontal a 360, 480, 768 y 1280 px.
- Los botones miden 44 px o más (51–53 px en las tarjetas de dos columnas a 360 px).
- El almacenamiento solo contiene `minv.carrito` con `{"v":1,"items":[{"sku","quantity"}]}`.
- «Reservar ahora» abrió `/reservar?sku=JUE-PS5-FC26&cantidad=1` y el carrito quedó igual.
- Con un monitor pedido por encima de lo disponible y un SKU inexistente, las líneas quedaron marcadas como «bajó» y «ya no está». «Ajustar carrito» llevó el monitor de 5 a 3 y quitó el otro.
- Un cambio simulado desde otra pestaña se aplicó en el momento, y los datos rotos dejaron el carrito vacío sin romper nada.
- La consola quedó sin errores.

No hice commit (estaba prohibido) ni toqué nada fuera de la carpeta de la web.

## Contratos (nombres exactos)

- DOMINIO '@/1-domain/cart/cart': CART_LIMITS = { maxLines: 20, maxQuantityPerLine: 16, skuMaxLength: 40 } (sale de RESERVATION_LIMITS). Funciones: EMPTY_CART, normalizeSku(v) → string|null (mayúsculas, ^[A-Z0-9][A-Z0-9_-]*$), normalizeQuantity(v) → 0..16, sanitizeCartItems(raw), cartOf(items), maxCartQuantity(product) = min(16, stock), quantityInCart, isInCart, cartCount (suma de unidades), sameCart, addToCart(cart, {sku, stock}, qty=1) → CartAddResult, setCartQuantity(cart, sku, qty, product?), removeFromCart, clearCart
- TIPOS '@/1-domain/cart/types': CartItem { sku, quantity } · Cart { items: readonly CartItem[] } · CartAddResult { cart, added, quantity, limit: 'unavailable'|'cart_full'|'stock'|'line_limit'|'invalid'|null }
- DINERO '@/1-domain/cart/totals': toCents(bs), fromCents(cents), lineCents(unitPrice, qty), lineSubtotal, sumCents. Todo total del carrito se suma en centavos enteros
- PUERTO '@/1-domain/ports/ICartStore': ICartStore { readonly persistent: boolean; load(): CartItem[]; save(items): boolean; subscribe(listener: (items) => void): () => void }. Síncrono y NUNCA lanza
- APLICACIÓN '@/2-application' (2-application/cart). createCartUseCases(store) → CartUseCases { current(), subscribe(l), isPersistent(), add(product, qty?) → CartAddResult, setQuantity(sku, qty, product?), remove(sku), clear(), review(lookup), adjust(lookup, sku?) → CartAdjustment }
- CRUCE CON EL CATÁLOGO: reviewCart(cart, lookup) → CartReview { lines, count, total, totalCents, savings, issues, reservable }. CartLine { sku, quantity, product|null, status: 'ok'|'reduced'|'sold_out'|'unavailable', available, max, suggestedQuantity, unitPrice, subtotal, counted }. adjustCart(cart, lookup, onlySku?) → { cart, changes: { sku, name, from, to, reason }[] }. ProductLookup = (sku) => Product|undefined (en la web: catalog.getProductBySku)
- CÓMO RECIBE /reservar UN ARTÍCULO SUELTO: la dirección es `/reservar?sku=<SKU>&cantidad=<n>`, armada con ROUTES.checkoutItem(sku, qty) o checkoutItemPath (parámetros CHECKOUT_PARAMS = { sku: 'sku', quantity: 'cantidad' }). isSingleItemCheckout(search) dice si trae `sku`, sirva o no. parseCheckoutItem(search) → CartItem|null: valida el SKU; sin cantidad o con una inválida vale 1; más de 16 se acota a 16. «Reservar ahora» NO toca el carrito ni el armado
- CÓMO LEE /reservar EL CARRITO: `/reservar` sin `sku` es el carrito completo. checkoutSelection(location.search, { items: cart.items }, sku => catalog.getProductBySku(sku)) → { source: 'item'|'cart', items: CartItem[], review: CartReview }. Al confirmar la reserva, el carrito se vacía SOLO si source === 'cart' (useCart().clear()). Si review.reservable es falso (algo se agotó o bajó), no se debe enviar la reserva
- INFRAESTRUCTURA '3-infrastructure/storage/cartStorage.ts' (ÚNICO archivo con localStorage): CART_STORAGE_KEY = 'minv.carrito', CART_STORAGE_VERSION = 1, formato {"v":1,"items":[{"sku":"…","quantity":n}]}. Exporta encodeCart, decodeCart(text) (null = descartar), LocalCartStore(area, events = window), browserStorageArea(), createCartStore(area?) (si el almacenamiento no está disponible devuelve InMemoryCartStore). Tipos CartStorageArea y CartStorageEvents
- MEMORIA '3-infrastructure/storage/memoryCartStore.ts': InMemoryCartStore(initial = [], { persistent?: boolean }) con changeFromElsewhere(items), que simula otra pestaña
- CONTENEDOR '@/4-presentation/app/container': createCartServices(store = createCartStore()) → CartUseCases. App.tsx crea UNO solo y lo monta como <SessionProvider><CatalogProvider><ToastProvider><BuilderProvider><CartProvider cart={cart}><RouterProvider/>
- PRESENTACIÓN: CartProvider({ cart: CartUseCases, children }) en '@/4-presentation/state/CartProvider'. Contextos CartStateContext y CartActionsContext en state/CartContext.ts
- HOOKS '@/4-presentation/hooks/useCart': useCart() = CartStateApi & CartActionsApi. useCartState() → { items, lines, review, count, total, persistent, has(sku), quantityOf(sku) }. useCartActions() → { add(product, qty?) → CartAddResult, setQuantity(sku, qty) (acota a lo disponible hoy), remove(sku), clear(), adjust(sku?) → CartAdjustment }. Fuera del proveedor lanzan un error
- AVISO AL AGREGAR: useAddToCart() de '@/4-presentation/hooks/useAddToCart' → (product, qty?) => CartAddResult. Muestra «Agregado al carrito» con la acción «Ver carrito» (grupo de avisos 'carrito:agregar'). La página del carrito usa el grupo 'carrito:cambio'. Textos en '@/4-presentation/components/cart/cartText': addToCartNotice, lineIssueText, adjustmentText, unitsLabel, cartCountLabel
- COMPONENTES: ProductActions({ product, className? }) en components/product/ProductActions.tsx, usado por ProductCard y ProductRow. PurchaseBox (ficha) con QuantityStepper y los botones «Agregar al carrito» y «Reservar ahora». CartLineRow en pages/cart
- RUTAS '@/4-presentation/app/routes': ROUTES.cart = '/carrito', ROUTES.checkout = '/reservar', ROUTES.checkoutItem(sku, qty = 1). Constantes CART_PATH y CHECKOUT_PATH en '@/2-application'
- PUNTO DE MONTAJE PARA EL PAQUETE SIGUIENTE: reemplazar el CONTENIDO de `src/4-presentation/pages/cart/CheckoutPage.tsx` conservando la exportación `CheckoutPage`. La ruta pública 'reservar' tiene carga diferida en routeTable.tsx, dentro de AppShell
- CAMBIO DE FIRMA: Services.refresh() (useServices().refresh) devuelve Promise<boolean>: true si llegó una instantánea nueva, false si falló y se conserva la anterior. CatalogLoader.load devuelve Promise<boolean>
- PRUEBAS '@/test-utils': mockCart(items = [], { persistent = true }) → { store: InMemoryCartStore, cart: CartUseCases }. renderWithApp(ui, { cart }) y renderRoutes({ cart }) aceptan el carrito; por defecto estrenan uno vacío en memoria
- DATA-TESTID: linea-carrito (con data-sku y data-status), linea-aviso, linea-subtotal, carrito-resumen, carrito-disponibilidad (data-freshness = checking|fresh|stale), carrito-total-importe, carrito-vaciar-confirmar, acciones-producto, en-carrito, acciones-ficha, en-carrito-ficha
- CABECERA: IconButton a /carrito con aria-label 'Carrito' o 'Carrito, N productos' (N = unidades) e insignia visual. El menú móvil tiene el botón «Carrito» con su número. «Armá tu PC» aparece en la cabecera desde 1024 px; por debajo de 480 px, «Mi armado» solo se muestra con un armado en curso

## Supuestos

- Los topes del carrito (16 unidades por producto y 20 productos distintos) son los de la reserva de la tienda: RESERVATION_LIMITS en la web, y PcBuild.MaxQuantity y el límite de líneas del servidor (S-05, informe B1). Así no se puede armar un carrito que el servidor rechace
- Los SKU del catálogo van en mayúsculas, con letras, números, guion y guion bajo, y miden hasta 40 caracteres. Lo guardado o recibido por la dirección con otra forma se descarta
- Lo disponible de una línea es `product.stock` de la instantánea (existencias − reservado, S-03). La tienda vuelve a comprobarlo al reservar
- Al abrir /carrito se vuelve a pedir la instantánea completa del catálogo (GET /storefront/v1/catalog) para tener la disponibilidad fresca. En modo API supuse que ese costo es aceptable. Si falla, se muestra la última disponibilidad conocida con el aviso y el botón «Actualizar»
- Una línea que «bajó» entra al total con la cantidad pedida. Las agotadas y las que ya no están publicadas no entran. En los dos casos «Reservar» queda bloqueado hasta ajustar, así que el total nunca llega mal a la reserva
- El número del ícono del carrito son UNIDADES (suma de cantidades) e incluye las líneas que ya no están publicadas hasta que se ajusten
- Un aviso `storage` con key null (otra pestaña vació todo el almacenamiento del sitio) también pone al día el carrito. Un carrito recibido de otra pestaña no se vuelve a guardar, para evitar un rebote entre pestañas
- El aviso «Tu carrito no se está guardando» aparece cuando el almacén es de memoria o cuando falló el último guardado. Si un guardado posterior funciona, vuelve a contar como guardado

## Pendientes

- Paquete siguiente (página /reservar): construirla con checkoutSelection. Debe enviar `kind: 'cart'`, `holdDays` (1 a 3) y `buyer`, con líneas SIN ranura (`slot` nulo). Debe leer `reservationHours` y `maxHoldDays` del catálogo en vez de la constante de 48 h. Debe unir en una sola línea las notas del textarea, porque el servidor ahora rechaza saltos de línea en medio (riesgo del informe B1). Y debe vaciar el carrito solo si source === 'cart'
- Paquete siguiente: HttpReservationGateway, los DTO de la tienda y la pasarela en memoria del modo mock (InMemoryReservationGateway) hoy piden la ranura de cada línea. Hay que admitir líneas de carrito sin ranura y el tipo `kind`. El tipo `Reservation` del dominio todavía no tiene `kind` ni `mailQueued` (pendiente heredado de W1)
- La página /reservar sigue PROVISIONAL (solo cambié su texto para distinguir el artículo suelto del carrito)
- Texto existente de la V5 que choca con el diseño V7 §11 (sin envío a domicilio ni pago en línea): PurchaseBox y TopBar dicen «Envíos a todo el país» y «Pagá con QR, tarjeta o transferencia». No lo cambié porque es una decisión comercial; conviene que el dueño la confirme
- El enlace del inicio en las migas de pan (componente existente Breadcrumbs) mide 40×44 px: cumple el mínimo AA de 24 px, pero no los 44 px de ancho
- Verificado solo con puertos simulados y en modo mock. Falta el recorrido de la regla P-14 contra el servidor real en Docker (agregar al carrito → reservar → correo)
- No hice commit (estaba prohibido). El único cambio sin confirmar de esta sesión es `src/4-presentation/pages/cart/CheckoutPage.tsx`; el resto del paquete ya estaba en el commit dbe3569
- Documentación fuera de mi carpeta sin actualizar: CHANGELOG.md, docs/product/plan-v7.md (tarea 9) y docs/product/catalogo-web-v5.md. El README de la web ya tiene la sección «V7 · Carrito de compras»

## Pruebas

Ejecutado desde la carpeta de la web con PowerShell (node_modules ya existía; en Git Bash `npm run build` falla sin imprimir nada, como ya advirtió W1). La salida de vitest se redirigió a un archivo y se leyó el final.

- `npx vitest run` solo con las carpetas del carrito (1-domain/cart, 2-application/cart, 3-infrastructure/storage, CartProvider, pages/cart, pages/product, components/product, components/cart): Test Files 9 passed (9) · Tests 145 passed (145).
- `npm run typecheck` (tsc -b --noEmit): exit 0, sin errores.
- `npm run lint` (oxlint): exit 0, sin advertencias ni errores.
- `npx vitest run` completo: exit 0 · Test Files 36 passed (36) · Tests 492 passed (492) · Duration 14.64s. Ninguna prueba existente cambió lo que comprueba; `src/architecture.test.ts` sigue igual y pasa.
- `npm run build` (tsc -b && vite build): exit 0 · 2093 módulos · built in 812ms · CartPage 10,45 kB (gzip 3,60 kB) y CheckoutPage 0,99 kB en fragmentos diferidos.

Verificación manual en navegador (servidor de desarrollo temporal con VITE_API_URL=mock en 127.0.0.1:5183, ya detenido):
- Sin desplazamiento horizontal: scrollWidth = ancho de la ventana a 360 y 480 px; a 768 y 1280 px es 15 px menor por la barra vertical.
- Botones de compra de 44 a 53 px de alto.
- Consola sin errores.
- localStorage solo con la clave `minv.carrito`.
