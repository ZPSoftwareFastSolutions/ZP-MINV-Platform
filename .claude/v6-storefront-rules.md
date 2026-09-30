# Reglas de la tienda web conectada · M-INV V6

> **Documento normativo** para la rama `Inventario-V6`. **DEBE** = obligatorio · **NO DEBE** = prohibido · **PUEDE** =
> permitido. Complementa A-xx (V3), B-xx (V4), F-xx (V4.1), T-xx (V4.2) y W-xx (V5); donde hablen de lo mismo (API pública,
> reservas, armados), **prevalece esta**. Diseño: `docs/architecture/tienda-web-conectada-v6.md`.

### S-01 · Una sola verdad: la base de datos en la nube
- Todo lo que la web muestra (categorías, productos, fichas, precios, disponibilidad, imágenes, armados sugeridos) DEBE
  salir de la base de datos por la API de tienda. El mock de la V5 solo PUEDE usarse en pruebas y con `VITE_API_URL=mock`.

### S-02 · La API de tienda es pública, acotada y por principal técnico
- `/storefront/v1` NO DEBE exigir API Key. Cada petición DEBE ejecutarse como el usuario técnico `tienda-web` del tenant
  configurado, con el rol `TIENDA_WEB` (`storefront.read`, `storefront.reserve`, `inventory.stock.view`) y la sucursal de
  la tienda. NO DEBE exponer costos, clientes, ventas, usuarios ni nada fuera del contrato de `storefront-api-v1.md`.
- Toda ruta DEBE pasar por MediatR (misma tubería de validación, permisos y auditoría). Límite por IP obligatorio; CORS
  solo para los orígenes configurados.

### S-03 · Reserva = armado + reservas de stock, en UNA transacción
- Una reserva web DEBE crear el `PcBuild` (canal Web, contacto, `ReservedUntil`) y una `StockReservation` por línea en la
  MISMA transacción, con reintento optimista (B-07). Si falta stock, NO DEBE reservarse nada (todo o nada) y la respuesta
  DEBE decir qué piezas y cuánto hay.
- Disponible = existencias − reservado (`StockLevel.Available`), en la web, en el stock del escritorio y en la caja.

### S-04 · Estados del armado
- `Draft → Quoted → Reserved → Sold | Cancelled` y `Quoted → Sold | Cancelled`; solo por los métodos de `PcBuild`, y cada
  cambio DEBE dejar su fila en `sales.pc_build_events` (append-only).
- Vender un armado reservado DEBE consumir sus reservas (nunca descontar dos veces). Liberar o vencer DEBE devolver el
  stock. Una reserva vencida la cierra el trabajo en segundo plano; NO DEBE cerrarse «al leer».

### S-05 · Idempotencia y abuso
- `POST /storefront/v1/reservations` DEBE ser idempotente por `Idempotency-Key` (B-09). Reservas por IP limitadas; una
  reserva PUEDE tener como máximo 16 unidades por línea (`PcBuild.MaxQuantity`) y 20 líneas.

### S-06 · Privacidad del contacto
- El teléfono y el correo del cliente web solo se muestran completos en el escritorio (y en el panel) a quien tiene
  `sales.pcbuild.manage`. La API pública NUNCA devuelve el teléfono, el correo ni los datos para la factura completos.
- V7 · La API pública consulta una reserva web con el código O el teléfono (al menos uno). Con el código Y el teléfono con
  que se hizo devuelve la vista completa (nombre y notas); con uno solo DEBE enmascarar el contacto: nombre con las
  iniciales (`V••• A•••`), sin notas, teléfono con los 3 últimos dígitos (`•••••567`) y correo con la primera letra y el
  dominio (`v•••@correo.example`). Estado, líneas, totales, vencimiento y sucursal PUEDEN mostrarse siempre.
- La consulta por teléfono devuelve solo las reservas del canal Web de la empresa de la tienda, de los últimos 90 días, las
  más nuevas primero y como máximo 10. Un código inexistente y un teléfono que no coincide DEBEN responder el MISMO 404 (no se
  revela si el código existe); un teléfono sin reservas, la lista vacía.
- Cancelar DEBE exigir siempre el código Y el teléfono. Las consultas tienen su propio límite por IP, más bajo que el de las
  lecturas (`Minv:Storefront:LookupsPerMinute`, 20).

### S-07 · Web: infraestructura HTTP aislada
- Solo `src/3-infrastructure/http/*` PUEDE usar `fetch`; el resto de la web sigue sin red ni storage (prueba de
  arquitectura). Los puertos `ICatalogSource` e `IReservationGateway` viven en `1-domain/ports`.
- La instantánea DEBE cargarse con estado de carga y de error visibles (reintentar); la disponibilidad de una ficha DEBE
  refrescarse al abrirla.

### S-08 · Escritorio
- El Armador de PC DEBE mostrar canal, contacto y vencimiento de cada reserva y ofrecer reservar, liberar, vender y
  publicar. Stock, catálogo y caja DEBEN mostrar lo reservado.

### S-09 · Datos de prueba
- La empresa de prueba DEBE traer el usuario técnico, armados publicados y al menos una reserva web activa y una vencida.

### S-10 · Definición de terminado
Además de B-17, T-10 y W-xx: pasan las pruebas de integración del gateway (`/storefront/v1`), las de la web contra un
servidor de prueba, y un recorrido de punta a punta en una base temporal: reservar desde la web → la cotización aparece
en el escritorio con el stock reservado → vender en caja → la web muestra la unidad vendida como no disponible.

## Checklist para agentes
- [ ] ¿Una ruta de tienda sin pasar por MediatR o que devuelve datos fuera del contrato? → rechazar (S-02).
- [ ] ¿Reserva sin `StockReservation` o fuera de la transacción del armado? → rechazar (S-03).
- [ ] ¿Un cambio de estado de armado sin fila en `pc_build_events`? → corregir (S-04).
- [ ] ¿`fetch` fuera de `3-infrastructure/http`? → rechazar (S-07).
