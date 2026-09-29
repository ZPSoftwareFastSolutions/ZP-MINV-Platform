# Informe del paquete W2b reservar (V7)

## Resumen

W2b (pantalla de reserva, tarea 9 segunda parte) terminado y todo en verde. Al empezar, `git log` mostró que el commit dbe3569 no traía avance de W2b. El único cambio sin confirmar era `CheckoutPage.tsx`, que era el texto provisional de W2a; lo reemplacé. No hice commit y no toqué nada fuera de `src/3. Presentation/MINV.WebCatalog`.

Qué se construyó:

1. **Dominio y adaptador de reservas.**
   - `Reservation` suma `kind` (build/cart) y `mailQueued`. `ReservationRequest` suma `kind`, `holdDays` y `buyer`. Las líneas del pedido admiten `slot` nulo o ausente.
   - El catálogo trae `reservationPolicy` (`reservationHours` y `maxHoldDays`; 48 y 3 si no vienen). Se eliminó la constante fija `RESERVATION_HOURS`; ahora se usan las horas del catálogo en «Armá tu PC», el resumen del armado y el pie de página.
   - El cuerpo HTTP manda `kind: "cart"`, `holdDays` y `buyer`, y las líneas de carrito sin `slot`. Un armado sin los campos nuevos manda el mismo cuerpo de la V6.
   - Nombre, notas y razón social viajan en una línea (`toSingleLine`). Esto también corrige el formulario «Reservar armado» de «Armá tu PC».

2. **`/reservar`: la misma pantalla para un artículo suelto (`?sku=&cantidad=`) y para el carrito.**
   - Resumen de lo que se reserva, con disponibilidad recién consultada; el artículo suelto permite cambiar la cantidad ahí mismo.
   - Formulario sin cuenta: nombre, teléfono o WhatsApp, correo con la ayuda «Te enviamos el código…», «¿Cuándo pasás a recogerlo?» (24, 48 o 72 h acotado por `maxHoldDays`), la sección plegable «Datos para tu factura (opcional)» (tipo, número, complemento solo con CI, nombre o razón social) y notas.
   - Validación de cada campo al salir de él y de todos al enviar. El foco va al primer error y la sección de factura se abre sola si el error está ahí.
   - Invitación «¿Tenés cuenta? Ingresá y seguí tus reservas», sin obligar.
   - Con sesión de cliente: los datos de la cuenta se muestran solo lectura, con «Cambiar mis datos», y la reserva va por RPC `CreateMyReservationCommand` con `kind: 'cart'`.
   - Sin sesión: tienda pública con una `Idempotency-Key` por intento (`IdempotentAttempt`). Es la misma llave si se reintenta por red y otra si cambia algo.
   - Manejo de fallas:
     - 409, o por RPC 422 con código `storefront.insufficient_stock`: marca cada producto con «Pediste X; hay Y», ofrece «Ajustar a lo disponible» y conserva lo escrito.
     - 400/422: el error queda en su campo cuando se puede ubicar.
     - 429 y red caída: aviso con «Reintentar».

3. **Confirmación.**
   - Número grande con «Copiar», tipo «Compra», estado, hasta cuándo se guarda y cuántas horas, sucursal de retiro, detalle y total.
   - «Te enviamos un correo a …» SOLO si `mailQueued` es verdadero; si no, «Guardá este número».
   - Enlaces «Mi reserva» (`/reserva/NUM`, o «Mis reservas» con cuenta) y «Seguir en el catálogo».
   - Se quitan del carrito los productos reservados; un artículo suelto no toca el carrito.

4. **«Mi reserva» y «Mis reservas».** «Mi reserva» acepta RES-WEB-… y ARM-WEB-…; las dos pantallas muestran el tipo (Armado / Compra) y las horas reales que se guarda cada reserva.

5. **Modo mock.**
   - Carritos numerados RES-WEB-n, sin ranura, con los días pedidos.
   - La factura se valida con las reglas del SIN y los caracteres de control dan 400, como en el servidor.
   - `mailQueued` es verdadero solo si hay correo.
   - La cuenta del cliente del mock reserva sobre el MISMO stock que la tienda.
   - Se recorre todo el flujo sin servidor.

6. **Pruebas.** Dominio, aplicación, HTTP y RPC, mock, y la página con la tabla de rutas real (15 casos): cubren todos los que pedía el encargo.

**Otros cambios en archivos existentes:**
- `CartPage` adelanta la descarga de la pantalla de reserva, para que «Reservar» abra al instante.
- Se actualizaron el README de la web (sección nueva «V7 · Reserva») y dos comentarios que decían «provisional».
- En `auth.test.ts` solo completé el objeto de prueba con los campos nuevos (`kind`, `mailQueued`, `createReservation`); ninguna comprobación cambió.

**Revisión en el navegador** (servidor de desarrollo en modo mock en 127.0.0.1:5186, ya detenido):
- Reserva de un artículo con NIT y notas en dos líneas: quedó RES-WEB-000001 con las notas unidas en una línea.
- Ingreso con el cliente de muestra: reserva por RPC RES-WEB-900001, el carrito quedó vacío y «Mis reservas» la muestra como Compra con «Se guarda 48 h».
- Sin desplazamiento horizontal a 360, 480, 768 y 1280 px. Todos los controles de la página miden 44 px o más; los únicos por debajo son los enlaces de la cabecera y el pie, que ya existían.
- La consola quedó sin errores.

## Contratos (nombres exactos)

- DOMINIO '@/1-domain/storefront/types': ReservationKind = 'build' | 'cart' · ReservationPolicy { reservationHours, maxHoldDays } · DEFAULT_RESERVATION_POLICY = { reservationHours: 48, maxHoldDays: 3 } · HOLD_DAYS_LIMIT = 3 · ReservationBuyer { documentType: DocumentTypeCode; documentNumber; complement?; name? } · RESERVATION_LIMITS agrega emailMaxLength 254 y buyerNameMaxLength 150 · SE ELIMINÓ RESERVATION_HOURS (las horas salen del catálogo)
- DOMINIO · ReservationRequest agrega kind?, holdDays? y buyer? · ReservationRequestLine.slot?: SlotKey | null (carrito sin ranura) · Reservation agrega kind: ReservationKind y mailQueued: boolean · ReservationLine.slot sigue siendo string ('' = sin ranura; el servidor manda null) · CatalogSnapshot.reservationPolicy (obligatorio) · InMemoryCatalogData.reservationPolicy? (opcional)
- DOMINIO '@/1-domain/storefront/policy': normalizeReservationPolicy(raw), hoursForHoldDays(d), holdDayChoices(policy) → [1..maxHoldDays], defaultHoldDays(policy) (48 h → 2), isHoldDays, reservationHeldHours(r) (horas reales de createdAt a reservedUntil), reservationKindOf(number) (RES- = cart), parseReservationKind(value, number), isReservationNumber, RESERVATION_NUMBER_PATTERN (ARM|RES-…)
- DOMINIO '@/1-domain/storefront/contact': toSingleLine(text) une las líneas con un espacio y quita los caracteres de control (se usa en notas, nombre y razón social antes de enviar)
- DOMINIO '@/1-domain/storefront/checkoutForm': CheckoutFormInput { name, phone, email, holdDays, documentType, documentNumber, complement, buyerName, notes } (texto) · CheckoutField · CheckoutFormErrors · CheckoutMode 'guest' | 'account' · CheckoutRules { mode, policy } · CHECKOUT_FIELDS, BUYER_FIELDS, checkoutFields(mode) (con cuenta: holdDays y notes) · emptyCheckoutForm(policy), checkoutFieldError(field, input, rules), validateCheckoutForm, firstInvalidField, toCheckoutContact, toHoldDays, toCheckoutNotes, toReservationBuyer
- DOMINIO '@/1-domain/account/types': AccountReservationLine { sku, quantity, slot? } · AccountReservationRequest { lines, kind: 'build' | 'cart', holdDays?, notes?, name? }
- PUERTOS: IAccountGateway.createReservation(request: AccountReservationRequest, options?) → Reservation · ICatalogRepository.getReservationPolicy()
- APLICACIÓN '@/2-application': CatalogUseCases.getReservationPolicy() · ReservationUseCases.reserveCart(input: ReserveCartInput { items: CartItem[], contact, holdDays?, buyer?, notes?, idempotencyKey }) envía kind 'cart' sin ranura y con las notas en una línea · validateCartReservationItems, validateHoldDays · AccountUseCases.reserve(input: AccountReserveInput { items, holdDays?, notes? }, { requestId }) → CreateMyReservationCommand con kind 'cart'
- APLICACIÓN '@/2-application/checkout': IdempotentAttempt (take(fingerprint), settle(error?), run(fingerprint, action), current()): la misma llave tras una falla de red, otra si cambia la huella, y se cierra cuando el servidor respondió · contentFingerprint(value) · isNetworkFailure · toCheckoutFailure(error) → CheckoutFailure { kind: 'insufficient_stock'|'invalid'|'rate_limited'|'network'|'unavailable'|'session'|'retry'|'unknown', title, message, fieldErrors, messages, shortages } · restrictToFields(failure, fields) · fieldOfServerMessage(msg, code) · parseShortages(msg) · INSUFFICIENT_STOCK_CODE = 'storefront.insufficient_stock' · checkoutAdjustments(review, shortages) → CheckoutAdjustment { sku, name, from, to } · shortagesBySku
- DTO '@/2-application/storefront': StorefrontReservationRequestDto agrega kind? ('cart'), holdDays? y buyer? (StorefrontReservationBuyerDto { documentType, documentNumber, complement?, name? }) · StorefrontReservationViewDto agrega kind? y mailQueued? · StorefrontCatalogDto agrega reservationHours? y maxHoldDays? · toReservationBuyerDto · toReservationRequestDto solo envía kind si es 'cart' (un armado manda el cuerpo de la V6)
- INFRAESTRUCTURA: RpcAccountGateway.createReservation → rpc.send('CreateMyReservationCommand', { lines: [{ sku, quantity }], kind: 'cart', holdDays?, notes?, name? }, { requestId }) · webMappers.toCreateReservationPayload · toAccountReservation lee kind y mailQueued · por RPC la falta de stock llega como WebApiError 'domain' con code 'storefront.insufficient_stock' y el detalle «SKU (pedido N, disponible M)» solo en el mensaje
- MODO MOCK: InMemoryReservationGateway numera aparte RES-WEB-n (cart) y ARM-WEB-n (build), aplica holdDays×24 h o reservationHours, valida la factura y los caracteres de control como el servidor y da mailQueued = hay correo · hold(lines)/release(lines)/shortagesOf(lines), StockHoldLine · InMemoryWebBackend acepta la opción stock: InMemoryStock y responde 422 storefront.insufficient_stock · container: createMockSources(data, mock, gateway?) y, con VITE_API_URL=mock, la tienda y la cuenta comparten UNA pasarela
- PRESENTACIÓN: useReservationPolicy() en '@/4-presentation/hooks/useReservationPolicy' · useOptionalAccount() en '@/4-presentation/hooks/useRpc' (AccountUseCases | null; no lanza en rutas públicas) · ReservationKindBadge en components/reservation/ReservationSummary · reservationKindLabel (Armado/Compra), heldHoursText, reservationItemsLabel en reservationText · CheckoutPage conserva su exportación y la ruta /reservar con carga diferida · componentes en pages/cart/checkout/ (CheckoutItems, CheckoutFields: GuestContactFields, AccountContact, HoldDaysField, BuyerSection, NotesField; CheckoutConfirmation; useCheckoutForm; checkoutText)
- DATA-TESTID: reserva-linea (data-sku, data-status ok|reduced|sold_out|unavailable|shortage), reserva-linea-aviso, reserva-total, reserva-disponibilidad (data-freshness), reserva-invitacion, reserva-datos-cuenta, reserva-confirmada, confirmacion-numero, confirmacion-vence, confirmacion-total, tipo-reserva (data-kind), reserva-horas (Mis reservas)
- TEXTOS ESTABLES: botones «Confirmar reserva», «Ajustar a lo disponible», «Reintentar», «Copiar», «Mi reserva», «Seguir en el catálogo», «Cambiar mis datos»; etiquetas «Nombre y apellido», «Teléfono o WhatsApp», «Correo», «¿Cuándo pasás a recogerlo?», «Tipo de documento», «Número de documento», «Complemento», «Nombre o razón social», «Notas para la tienda»; la sección «Datos para tu factura (opcional)» es un botón con aria-expanded

## Supuestos

- CreateMyReservationCommand recibe kind como texto 'cart' en minúsculas. El RPC del servidor usa JsonStringEnumConverter para PcBuildKind, que al leer no distingue mayúsculas. Si el contrato generado exige 'Cart' o un número, se corrige solo toCreateReservationPayload en webMappers.ts.
- Por el RPC, la falta de stock llega como 422 'domain' con code 'storefront.insufficient_stock' (StorefrontStockException es un DomainException) y los faltantes solo en el texto del mensaje. La web los lee con el patrón «SKU (pedido N, disponible M)».
- Con una cuenta de cliente no se envían datos para la factura: el CreateMyReservationCommand del servidor no los acepta y la caja usa el documento del cliente al cobrar. La página muestra el documento de la cuenta en solo lectura.
- El correo es opcional, igual que en el contrato. «Te enviamos un correo a …» aparece solo si la respuesta trae mailQueued y la web conoce el correo (el del formulario o el de la cuenta). Hoy B1 devuelve mailQueued siempre falso, así que contra ese servidor se verá «Guardá este número».
- La web siempre envía holdDays. Por defecto elige la opción que equivale a reservationHours (48 h → 2 días) o, si ninguna coincide, el máximo permitido.
- En el dominio, ReservationLine.slot se mantiene como texto: queda '' cuando el servidor manda null. Una prueba existente de W1 comprueba slot: ''. El valor nulo se admite en el pedido (ReservationRequestLine.slot) y en los DTO.
- Una sesión del personal reserva por la tienda pública, con el formulario completo y sin la invitación a ingresar.
- «Horas reales» = redondeo de reservedUntil − createdAt en horas.
- Ubicar en su campo un error 400/422 del servidor se hace por su código (pcbuild.contact_phone, buyer.*, storefront.hold_days…) o por palabras del texto. Lo que no se puede ubicar se muestra arriba. Con cuenta, los errores de campos que no se ven pasan a los mensajes generales (restrictToFields).
- Al confirmar un carrito se descuenta de cada línea lo que se reservó, y la línea se quita si llega a 0. Si otra pestaña cambió el carrito mientras tanto, se conserva la diferencia.

## Pendientes

- Regla P-14: falta el recorrido en un navegador real contra el servidor en Docker (agregar al carrito → reservar → llega el correo al buzón de prueba). Todo lo verifiqué solo con puertos simulados y en modo mock.
- contract.generated.ts sigue PROVISIONAL. Cuando lo reemplace el generado por `minv contrato-web`, hay que revisar la forma de CreateMyReservationCommand (kind y lines) y ajustar solo contract.ts o toCreateReservationPayload si difiere.
- El servidor B1 devuelve mailQueued siempre falso hasta que B3 encole el correo. Recién entonces la confirmación dirá «Te enviamos un correo a …» contra el servidor real.
- Un 429 de la tienda pública no lee la cabecera Retry-After (api.ts no expone las cabeceras en los errores). El mensaje dice «Esperá un minuto».
- Si el dueño quiere que un cliente con cuenta deje datos de factura distintos por reserva, el comando del servidor necesita un Buyer. Hoy se usa el documento de la cuenta.
- Documentación fuera de mi carpeta sin actualizar: CHANGELOG.md, docs/product/plan-v7.md (tarea 9) y docs/product/catalogo-web-v5.md. Actualicé solo el README de la web.
- Pendientes heredados de W2a sin tocar: TopBar y PurchaseBox siguen diciendo «Envíos a todo el país», y el enlace de inicio de las migas de pan mide 40×44 px.
- No hice commit (estaba prohibido). Todos los cambios quedan sin confirmar en la carpeta de la web de la rama Inventario-V7.

## Pruebas

Ejecutado desde la carpeta de la web al final, después del último cambio de código.

- **Línea base** antes de empezar: Test Files 36 passed (36), Tests 492 passed (492).
- **`npm run typecheck`** (tsc -b --noEmit, PowerShell): exit 0, sin errores.
- **`npm run lint`** (oxlint, PowerShell): exit 0, sin advertencias ni errores. Dos avisos react(set-state-in-effect) que salieron durante el trabajo quedaron corregidos.
- **`npx vitest run`**, salida redirigida a un archivo: Test Files 41 passed (41), Tests 537 passed (537). Se corrió dos veces seguidas con el código final y las dos dieron verde.
  - Una corrida anterior, antes de agregar la descarga adelantada en `CartPage`, falló una vez: la prueba existente «Seguir comprando vuelve al catálogo y Reservar lleva a la reserva del carrito» no encontró el título «Reservar». Esperó más de 1 s a que bajara el fragmento de /reservar mientras el equipo estaba cargado.
  - La corrida siguiente, sin cambios, pasó.
  - Desde que `CartPage` adelanta la descarga de `CheckoutPage` no volvió a fallar.
- **Carpetas nuevas por separado**:
  - `npx vitest run src/1-domain/storefront src/2-application/checkout src/3-infrastructure`: 10 files, 153 passed.
  - `CheckoutPage.test.tsx`: 15 passed, sin avisos de act.
  - La prueba de arquitectura sigue pasando sin cambios.
- **`npm run build`** (PowerShell): exit 0. «✓ 2105 modules transformed», «✓ built in 3.63s». Fragmento `CheckoutPage-*.js` 37.52 kB (gzip 11.50 kB).
- **Navegador**, modo mock, servidor de desarrollo en 127.0.0.1:5186, ya detenido:
  - Sin desplazamiento horizontal a 360, 480, 768 y 1280 px.
  - Controles de la página de 44 px o más.
  - Recorrido sin cuenta y con la cuenta de muestra completo.
  - Consola sin errores.
