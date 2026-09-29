# Guía del integrador · M-INV API Gateway v1 (B2B)

Para tiendas en línea (Shopify, WooCommerce, desarrollos propios) y sistemas contables o ERP que necesitan leer el
catálogo y el stock de M-INV, registrar pedidos como ventas, operar transferencias entre sucursales y recibir eventos.
Versión 4.0.0-alpha.1 (rama `Inventario-V4.-BaseDeDatosNube`). Arquitectura:
[`docs/architecture/arquitectura-v4.md`](../architecture/arquitectura-v4.md).

> El gateway es **solo para integraciones de terceros**. El escritorio `M-INV.exe` nunca lo usa: en modo nube habla con
> el servidor M-INV (`/api/v1/rpc`), que es otro servicio.

```text
ALGORITMO DEL INTEGRADOR
 1. Pida al administrador de M-INV una API Key (escritorio › Integraciones › API Keys) con los alcances mínimos que
    necesita y, si corresponde, limitada a una sucursal. El token completo se muestra UNA sola vez: guárdelo en su bóveda.
 2. Pruebe:        curl -s "$MINV_API/v1/catalog?pageSize=5" -H "Authorization: Bearer $MINV_KEY"
 3. Lea el catálogo y el stock (paginados); registre pedidos con un externalId ÚNICO por pedido (idempotencia).
 4. Registre un webhook (alcance webhooks:manage), guarde su secreto y VERIFIQUE la firma X-MINV-Signature.
 5. Trate los errores por su estado HTTP y su «code»; reintente solo 409, 429 y 5xx (con el MISMO externalId).
```

## 1. Conexión

| Qué | Valor |
|---|---|
| URL base (producción) | la que publique su proveedor de M-INV, p. ej. `https://api.suempresa.com` (siempre https) |
| URL base (pruebas en un solo equipo) | `http://localhost:5090` (`tools\servidores_locales.ps1 -Accion iniciar`) |
| Documentación interactiva (OpenAPI / Swagger UI) | `GET /docs` · documento: `GET /docs/v1/openapi.json` |
| Estado | `GET /health` → `{"status":"ok","product":"M-INV API Gateway","version":"4.0.0-alpha.1"}` (sin llave) |
| Formato | JSON UTF-8 en camelCase; fechas ISO 8601 en UTC (`2026-09-25T15:04:05.123+00:00`); días `AAAA-MM-DD`; enumeraciones como texto (`"Dispatched"`) |
| Errores | ProblemDetails (RFC 7807), `Content-Type: application/problem+json` |

Los ejemplos usan bash (Linux, macOS, Git Bash):

```bash
export MINV_API='http://localhost:5090'
export MINV_KEY='minv_<prefijo>_<secreto>'      # nunca la escriba en el código ni en un repositorio
```

En PowerShell use `curl.exe` (no el alias `curl`), `$env:MINV_API = 'http://localhost:5090'`, `$env:MINV_KEY = '…'`,
y para los cuerpos JSON un archivo: `curl.exe … -H "Content-Type: application/json" --data "@pedido.json"`.

## 2. Autenticación y alcances

Envíe la llave en **una** de estas cabeceras:

```text
Authorization: Bearer minv_<prefijo 8>_<secreto 43>
X-Api-Key: minv_<prefijo 8>_<secreto 43>
```

- La llave se crea en el escritorio (Integraciones › API Keys › Nueva): nombre, alcances, sucursal opcional y
  vencimiento opcional (1 a 730 días). M-INV guarda solo el prefijo y el hash SHA-256: si pierde el token, revoque la
  llave y cree otra.
- **Actúa en nombre de su dueño** (el usuario que la creó): puede hacer lo que permiten sus alcances **y** el rol del
  dueño. Si el dueño pierde un permiso, deja de trabajar en la sucursal de la llave o se desactiva, la llave pierde lo
  mismo en la siguiente petición.
- **Sucursal**: una llave limitada a una sucursal solo ve y opera esa sucursal; sin límite, ve las del dueño (todas si es
  gerencia global). Los pedidos sin `warehouseCode` salen del almacén de esa sucursal.
- Llave ausente, mal formada, inexistente, vencida o revocada → `401` sin más detalle.

| Alcance | Rutas | Permisos del dueño que usa |
|---|---|---|
| `catalog:read` | `GET /v1/catalog`, `GET /v1/branches`, `GET /v1/products/{sku}/specs` (V4.2) | `inventory.stock.view` |
| `stock:read` | `GET /v1/stock`, `GET /v1/stock/consolidated` | `inventory.stock.view` |
| `orders:write` | `POST /v1/orders`, `GET /v1/orders/{externalId}` | `sales.pos.operate`, `inventory.movements.register.sales`, `sales.view` |
| `transfers:read` | `GET /v1/transfers`, `GET /v1/transfers/{id}` | `inventory.stock.view` |
| `transfers:write` | `POST /v1/transfers`, `…/dispatch`, `…/receive`, `…/cancel` | `inventory.transfers.manage`, `inventory.stock.view` |
| `webhooks:manage` | `GET/POST /v1/webhooks`, `…/rotate-secret`, `DELETE /v1/webhooks/{id}`, `GET /v1/webhooks/deliveries` (una llave limitada a una sucursal solo ve y administra los webhooks de esa sucursal) | `integration.manage` |
| `reports:read` | `GET /v1/reports/branches` | `reports.view` |
| (cualquier llave válida) | `GET /v1/events` | — |

Además de los alcances, algunas rutas exigen que la empresa tenga licenciado un módulo: `POST /v1/orders` y
`POST /v1/webhooks` (**API_INTEGRATIONS**), las escrituras de transferencias (**MULTI_BRANCH**) y
`GET /v1/reports/branches` (**GLOBAL_AUDIT**). Sin el módulo: `403 access_denied`.

## 3. Errores

```json
{
  "type": "https://minv.example/errores/domain",
  "title": "domain",
  "status": 422,
  "detail": "Stock insuficiente: disponible 3, se pidió 5.",
  "errors": null,
  "code": "stock.insufficient"
}
```

| HTTP | `title` | Cuándo | ¿Reintentar? |
|---|---|---|---|
| 400 | `validation` | datos inválidos o JSON mal formado; `errors` lista cada problema en español | no: corrija |
| 401 | — | sin llave, o llave inválida, vencida o revocada (`WWW-Authenticate: Bearer realm="M-INV", error="invalid_token"`) | no |
| 403 | — (sin cuerpo) | la llave no tiene el alcance de la ruta | no |
| 403 | `access_denied` | el dueño no tiene el permiso, la empresa no tiene el módulo o la sucursal no es de la llave | no |
| 404 | `not_found` | no existe o no es de sus sucursales (SKU, cliente, medio de pago, transferencia, webhook) | no |
| 409 | `concurrency` | otra operación simultánea ganó y los reintentos internos no alcanzaron | sí, con espera |
| 422 | `domain` | regla de negocio; `code` es estable (ver abajo) | no |
| 422 | `idempotency` | el mismo `externalId` ya se usó con otro contenido | no: use otro id |
| 429 | — (sin cuerpo) | superó el límite de peticiones | sí, espere unos segundos |
| 500 | `server` | falla técnica (sin detalles internos) | sí, con el mismo `externalId` |

Códigos frecuentes (`code`): `stock.insufficient` (poka-yoke: la venta dejaría el stock en negativo),
`quantity.decimals` (la unidad no admite decimales), `price.missing` (el producto no tiene precio en la lista por
defecto), `payment.reference` (QR, tarjeta y transferencia exigen referencia), `product.inactive`,
`transfer.same_warehouse`, `transfer.duplicate_line`, `transfer.invalid_state`, `transfer.insufficient_stock`,
`transfer.over_receipt`, `transfer.batch_over_receipt`, `transfer.shortage_reason`, `transfer.unknown_line`,
`warehouse.inactive`, `branch.inactive`, `webhook.url`, `webhook.https`, `webhook.userinfo`, `webhook.event`,
`webhook.limit`, `db.foreign_key`, `db.check`.

## 4. Límites de peticiones

- **Por dirección IP**: 120 peticiones por minuto (ventana fija), en todas las rutas, ANTES de autenticar (frena el
  barrido de llaves).
- **Por llave** (rutas `/v1`): una cubeta de 100 peticiones que se repone a 10 por segundo. La partición es el prefijo
  público de la llave (`minv_<prefijo>_…`), que se lee de la cabecera sin tocar la base.
- Al superarlos: `429`. Una integración que llama desde una sola IP no debe pasar de **120 peticiones por minuto**: use
  páginas grandes (`pageSize` hasta 500) y webhooks en lugar de consultar el stock en bucle.

## 5. Idempotencia de los pedidos

- Cada pedido lleva un `externalId` (el id del pedido en su sistema, ≤ 100 caracteres). Si no lo envía en el cuerpo,
  se usa la cabecera `Idempotency-Key`. Sin ninguno de los dos: `400`.
- El par **(API Key, externalId)** es único: repetir el MISMO pedido (mismo cliente, medio de pago, referencia,
  almacén y líneas; el orden de las líneas no importa) devuelve la venta original con `200` y la cabecera
  `Idempotent-Replayed: true`; no se vende dos veces.
- El mismo `externalId` con otro contenido → `422 idempotency` (la factura original se menciona en `detail`).
- Otra API Key con el mismo `externalId` crea otro pedido (el canal es la llave, no el cuerpo).
- Ante un corte de red, `5xx`, `409` o `429`, **reintente con el mismo `externalId`**: es seguro.

## 6. Endpoints

### 6.1 Catálogo

#### `GET /v1/catalog` · `catalog:read`

Productos (variantes) con el precio de la lista por defecto (IVA incluido) y sus códigos de barras, ordenados por SKU.
Parámetros: `page` (1), `pageSize` (100, máximo 500), `search` (SKU o parte del nombre, sin distinguir mayúsculas).
El catálogo es de toda la empresa.

```bash
curl -s "$MINV_API/v1/catalog?page=1&pageSize=100&search=4060" -H "Authorization: Bearer $MINV_KEY"
```

```json
{
  "pageNumber": 1,
  "pageSize": 100,
  "total": 1,
  "items": [
    {
      "sku": "GPU-MSI-4060-V2XB",
      "name": "Tarjeta de video MSI GeForce RTX 4060 VENTUS 2X BLACK 8G OC",
      "category": "Tarjetas de video",
      "unit": "UND",
      "price": 3199.0,
      "barcodes": ["7771234567897"],
      "isActive": true
    }
  ]
}
```

#### `GET /v1/products/{sku}/specs` · `catalog:read` (V4.2)

Ficha técnica de un producto de la edición Tecnología: especificaciones (código, nombre, unidad, tipo, valores y texto
para mostrar, clave de compatibilidad del armador), si lleva serie o IMEI (`trackSerials`, `serialKind`), meses de
garantía y cuántas unidades con serie hay en stock. `404` si el SKU no existe.

```bash
curl -s "$MINV_API/v1/products/GPU-MSI-4060-V2XB/specs" -H "Authorization: Bearer $MINV_KEY"
```

#### `GET /v1/branches` · `catalog:read`

Directorio de sucursales de la empresa. `isVisible` indica si la llave puede operar esa sucursal; el valor del stock
solo se informa de las visibles (`null` en las demás); `transfersOut`/`transfersIn` cuentan las transferencias abiertas
(pendientes o en tránsito) que la llave puede ver.

```bash
curl -s "$MINV_API/v1/branches" -H "Authorization: Bearer $MINV_KEY"
```

```json
[
  {
    "id": "01926b3e-7a10-7c2e-9d41-5b0f2a8e1c01",
    "code": "CM",
    "name": "Casa matriz La Paz · Av. 16 de Julio (El Prado)",
    "isActive": true,
    "warehouses": ["ALM01"],
    "users": 7,
    "isVisible": true,
    "stockValue": 1528340.55,
    "transfersOut": 2,
    "transfersIn": 0
  },
  {
    "id": "01926b3e-7a11-7f55-8a02-3c1d9e4f2b02",
    "code": "CB",
    "name": "Sucursal Cochabamba",
    "isActive": true,
    "warehouses": ["ALMCB"],
    "users": 4,
    "isVisible": false,
    "stockValue": null,
    "transfersOut": 0,
    "transfersIn": 1
  }
]
```

#### `GET /v1/events` · cualquier llave

Eventos que M-INV puede enviar por webhook (ver §7).

```bash
curl -s "$MINV_API/v1/events" -H "Authorization: Bearer $MINV_KEY"
```

```json
[
  { "code": "sale.completed", "description": "Venta cobrada (POS o e-commerce)" },
  { "code": "sale.voided", "description": "Venta anulada (el stock volvió)" },
  { "code": "purchase.received", "description": "Mercadería recibida de un proveedor" },
  { "code": "transfer.dispatched", "description": "Transferencia despachada: la mercadería sale del origen y queda en tránsito" },
  { "code": "transfer.received", "description": "Transferencia recibida en la sucursal destino" },
  { "code": "transfer.discrepancy", "description": "Faltante registrado al recibir una transferencia" },
  { "code": "sale.returned", "description": "Devolución de mercadería de un cliente (con nota crédito-débito si estaba facturada)" },
  { "code": "fiscal.document.validated", "description": "Factura o nota crédito-débito válida en el SIN (SIAT)" },
  { "code": "fiscal.document.voided", "description": "Factura o nota crédito-débito anulada en el SIN (SIAT)" },
  { "code": "pcbuild.reserved", "description": "Armado de PC reservado: el stock de sus piezas quedó reservado (tienda web o escritorio)" },
  { "code": "pcbuild.released", "description": "Reserva de un armado liberada o vencida: el stock volvió a estar disponible" },
  { "code": "pcbuild.sold", "description": "Armado de PC vendido en la caja (si estaba reservado, la reserva se consumió)" }
]
```

> V6 · La **API pública de tienda** (`/storefront/v1`: catálogo, disponibilidad, imágenes, armados sugeridos y reservas de
> armados, SIN API Key) tiene su propio contrato: `docs/integration/storefront-api-v1.md`.

### 6.2 Stock

#### `GET /v1/stock` · `stock:read`

Existencias por SKU, sucursal y almacén (solo las sucursales de la llave). Parámetros: `branch` (código, p. ej. `CM`),
`sku`, `page`, `pageSize` (máx. 500). `available` = `onHand` − `reserved`.

```bash
curl -s "$MINV_API/v1/stock?branch=CM&sku=JUE-PS5-GT7" -H "Authorization: Bearer $MINV_KEY"
```

```json
{
  "pageNumber": 1,
  "pageSize": 100,
  "total": 1,
  "items": [
    { "sku": "JUE-PS5-GT7", "branchCode": "CM", "warehouseCode": "ALM01", "onHand": 14, "reserved": 0, "available": 14 }
  ]
}
```

#### `GET /v1/stock/consolidated` · `stock:read`

Stock por sucursal visible más lo que está **en tránsito** entre sucursales (transferencias despachadas y aún no
recibidas, contado una sola vez). `total` = Σ `byBranch` + `inTransit`. Valorizado al costo promedio de cada almacén.
Parámetro: `search` (SKU o nombre). `byBranch` y `valueByBranch` siguen el orden de `branches`.

```bash
curl -s "$MINV_API/v1/stock/consolidated?search=JUE-PS5-GT7" -H "Authorization: Bearer $MINV_KEY"
```

```json
{
  "branches": [
    { "id": "01926b3e-7a10-7c2e-9d41-5b0f2a8e1c01", "code": "CM", "name": "Casa matriz La Paz · Av. 16 de Julio (El Prado)" }
  ],
  "rows": [
    {
      "sku": "JUE-PS5-GT7",
      "name": "Gran Turismo 7 (PS5)",
      "category": "Videojuegos",
      "unit": "UND",
      "byBranch": [14],
      "inTransit": 10,
      "total": 24,
      "value": 8832.00
    }
  ],
  "valueByBranch": [152340.55],
  "inTransitValue": 3120.40,
  "totalValue": 155460.95
}
```

### 6.3 Pedidos

#### `POST /v1/orders` · `orders:write`

Registra un pedido del e-commerce como **venta cobrada**: el mismo flujo que la caja (poka-yoke de stock, factura con
IVA incluido, pago, asiento contable y evento `sale.completed`), sin turno de caja. Idempotente (§5).

| Campo | Obligatorio | Descripción |
|---|---|---|
| `externalId` | sí (o cabecera `Idempotency-Key`) | id del pedido en su sistema (≤ 100) |
| `customerCode` | sí | código del cliente en M-INV (`CF` = consumidor final) |
| `paymentMethodCode` | sí | `EFECTIVO`, `QR`, `TARJETA` o `TRANSFERENCIA` |
| `paymentReference` | con QR, tarjeta y transferencia | número de operación o voucher |
| `lines[]` | sí (1 a 200) | `sku` (o código de barras), `quantity` > 0, `discountPercent` 0 a 100 (opcional) y, V4.2, `serials` (series o IMEI de las unidades de un producto serializado: una por unidad, en stock en el almacén que despacha; van en la factura del SIN y entran al hash de idempotencia solo si vienen) |
| `warehouseCode` | no | almacén que despacha (por defecto, el de la sucursal de la llave); debe ser de una sucursal de la llave |
| `buyer` | no (V4.1) | datos de facturación del comprador para la factura del SIAT (ver abajo). Si la empresa factura en el SIAT y el cliente no tiene datos de facturación, es obligatorio |
| `buyer.documentType` | sí, si hay `buyer` | tipo de documento del SIN: `1` CI, `2` CEX, `3` pasaporte, `4` otro documento, `5` NIT |
| `buyer.documentNumber` | sí, si hay `buyer` | número de documento (CI y NIT solo dígitos, hasta 20). NIT especiales: `99001` consulados, `99002` control tributario, `99003` ventas menores del día |
| `buyer.complement` | no | complemento del CI (hasta 5 caracteres; solo con CI) |
| `buyer.name` | no | nombre o razón social que va en la factura (si falta, el del cliente) |
| `buyer.email` | no | correo al que se entrega la factura (XML y PDF) |

**Facturación SIAT (V4.1).** Si la empresa emite documentos fiscales del SIAT (Bolivia), la venta del pedido lleva su
factura Compra Venta en la MISMA transacción (punto de venta 0 de la sucursal): en línea queda pendiente de envío y el
trabajo automático del servidor la envía al SIN en segundos; sin comunicación con el SIN se emite fuera de línea y se
envía en un paquete al recuperarla. Si el documento del comprador no es de un cliente existente, M-INV crea el cliente (código `CI-…`, `NIT-…`).
Un pedido sin `buyer` conserva el mismo hash de idempotencia que en la V4 (el campo solo se agregó). Errores posibles
(`422`, `code`): `fiscal.buyer_required` (falta el documento del comprador), `fiscal.not_homologated` (producto, unidad o
medio de pago sin homologar con el SIN), `fiscal.quantity_decimals` / `fiscal.price_decimals` (la factura admite 2
decimales), `siat.no_cufd`.

```bash
curl -s -i -X POST "$MINV_API/v1/orders" \
  -H "Authorization: Bearer $MINV_KEY" -H "Content-Type: application/json" \
  -d '{
        "externalId": "PED-1001",
        "customerCode": "CF",
        "paymentMethodCode": "QR",
        "paymentReference": "QR-88213344",
        "lines": [
          { "sku": "JUE-PS5-GT7", "quantity": 1 },
          { "sku": "PAD-LOG-G240", "quantity": 2, "discountPercent": 5 }
        ],
        "buyer": { "documentType": 5, "documentNumber": "1020703023", "name": "ESTUDIO PIXEL ANDINO S.R.L.", "email": "compras@pixelandino.example" }
      }'
```

`201 Created` (la primera vez):

```json
{
  "externalId": "PED-1001",
  "orderNumber": "PV-CM-000215",
  "invoiceNumber": "F-CM-000215",
  "branchCode": "CM",
  "total": 980.10,
  "tax": 127.41,
  "issuedAt": "2026-09-25T15:04:05.1234567+00:00",
  "replayed": false,
  "cuf": "4128CF31A2C8606A3A19E23EF34124CD…",
  "fiscalNumber": 215
}
```

V4.1: `cuf` y `fiscalNumber` son el CUF y el número del documento fiscal del SIAT de la venta (`null` si la empresa no
factura en el SIAT). Si el documento se re-emitió (rechazo o sin respuesta del SIN), `GET /v1/orders/{externalId}`
devuelve el vigente. V4.2: `tax` es el IVA incluido en `total`; en Bolivia, el 13 % del total facturado, redondeado una vez
(el débito fiscal de la factura en el libro de ventas); una empresa de otro país conserva importe × tasa / (100 + tasa).

`200 OK` con `Idempotent-Replayed: true` y `"replayed": true` si repite el mismo pedido; `422` con
`"title": "idempotency"` si repite el `externalId` con otro contenido:

```json
{
  "type": "https://minv.example/errores/idempotency",
  "title": "idempotency",
  "status": 422,
  "detail": "El pedido PED-1001 ya se registró con otro contenido (factura F-CM-000215): use otro externalId.",
  "errors": null,
  "code": null
}
```

Validación (`400`):

```json
{
  "type": "https://minv.example/errores/validation",
  "title": "validation",
  "status": 400,
  "detail": "Datos no válidos: El pedido no tiene productos.",
  "errors": ["El pedido no tiene productos."],
  "code": null
}
```

### 6.4 Transferencias entre sucursales

Estados: `Pending` (solicitada) → `Dispatched` (salió del origen: **en tránsito**) → `Received`; una `Pending` se puede
anular (`Cancelled`). El **origen** crea, despacha y anula; el **destino** recibe (una llave de gerencia global puede
ambas cosas). Números con el código de la sucursal de origen: `TR-CM-000031`.


#### `GET /v1/orders/{externalId}` · `orders:write`

Devuelve el pedido ya registrado por ESTA llave (el destino del encabezado `Location` del `POST`); `404` si no existe
para esa integración. La respuesta es la misma del `POST` con `"replayed": true`.

```bash
curl -s "$MINV_API/v1/orders/WEB-1001" -H "Authorization: Bearer $MINV_API_KEY"
```
#### `GET /v1/transfers` · `transfers:read`

Transferencias que salen de o llegan a las sucursales de la llave (las 300 más recientes). Parámetro opcional `status`
(`Pending`, `Dispatched`, `Received`, `Cancelled`). `canDispatch`, `canReceive` y `canCancel` dicen qué puede hacer la
llave con cada una.

```bash
curl -s "$MINV_API/v1/transfers?status=Dispatched" -H "Authorization: Bearer $MINV_KEY"
```

```json
[
  {
    "id": "01926c01-2b3d-7e8f-a1b2-c3d4e5f60718",
    "number": "TR-CM-000031",
    "fromBranch": "Casa matriz La Paz · Av. 16 de Julio (El Prado)",
    "fromWarehouse": "ALM01",
    "toBranch": "Sucursal Cochabamba",
    "toWarehouse": "ALMCB",
    "status": "Dispatched",
    "statusLabel": "despachada (en tránsito)",
    "requestedAt": "2026-09-22T19:00:00+00:00",
    "dispatchedAt": "2026-09-22T19:02:11+00:00",
    "receivedAt": null,
    "lines": 1,
    "quantity": 10,
    "value": 3680.00,
    "shortage": 0,
    "notes": "Reposición semanal de la sucursal",
    "canDispatch": false,
    "canReceive": true,
    "canCancel": false
  }
]
```

#### `GET /v1/transfers/{id}` · `transfers:read`

Cabecera (la misma fila de arriba), líneas con el **manifiesto por lote** y la **bitácora** de estados.

```bash
curl -s "$MINV_API/v1/transfers/01926c01-2b3d-7e8f-a1b2-c3d4e5f60718" -H "Authorization: Bearer $MINV_KEY"
```

```json
{
  "header": { "id": "01926c01-2b3d-7e8f-a1b2-c3d4e5f60718", "number": "TR-CM-000031", "status": "Received", "…": "…" },
  "lines": [
    {
      "lineId": "01926c01-2b3e-71aa-9c0d-11aa22bb33cc",
      "sku": "JUE-PS5-GT7",
      "name": "Gran Turismo 7 (PS5)",
      "unit": "UND",
      "quantity": 10,
      "unitCost": 368.00,
      "received": 9,
      "shortage": 1,
      "shortageReason": "Caja dañada en el camión",
      "lots": ["SIN-LOTE × 10"]
    }
  ],
  "history": [
    { "occurredAt": "2026-09-22T19:00:00+00:00", "statusLabel": "pendiente", "user": "Mariana Suárez", "detail": "Solicitada" },
    { "occurredAt": "2026-09-22T19:02:11+00:00", "statusLabel": "despachada (en tránsito)", "user": "Mariana Suárez", "detail": "Despachada: mercadería en tránsito" },
    { "occurredAt": "2026-09-23T12:40:00+00:00", "statusLabel": "recibida", "user": "Sergio Mamani", "detail": "Recibida con faltantes (1)" }
  ]
}
```

(`"…": "…"` abrevia los campos de la cabecera, iguales a los de `GET /v1/transfers`.)

#### `POST /v1/transfers` · `transfers:write`

Solicita una transferencia **pendiente** (no mueve stock). `fromWarehouseCode` es opcional: por defecto, el almacén de
la sucursal de la llave.

```bash
curl -s -X POST "$MINV_API/v1/transfers" \
  -H "Authorization: Bearer $MINV_KEY" -H "Content-Type: application/json" \
  -d '{ "toWarehouseCode": "ALMCB", "lines": [ { "sku": "JUE-PS5-GT7", "quantity": 10 } ], "notes": "Reposición de fin de semana" }'
```

`201 Created` (`Location: /v1/transfers/{id}`):

```json
{
  "id": "01926c01-2b3d-7e8f-a1b2-c3d4e5f60718",
  "number": "TR-CM-000031",
  "message": "✔ Transferencia TR-CM-000031 solicitada de ALM01 a ALMCB (1 productos)."
}
```

#### `POST /v1/transfers/{id}/dispatch` · `transfers:write` (origen)

Sale todo del origen en una transacción (salidas por lote, manifiesto, costo promedio, asiento
1.1.06 / 1.1.05) y queda **en tránsito**. Sin cuerpo.

```bash
curl -s -X POST "$MINV_API/v1/transfers/01926c01-2b3d-7e8f-a1b2-c3d4e5f60718/dispatch" -H "Authorization: Bearer $MINV_KEY"
```

```json
{
  "id": "01926c01-2b3d-7e8f-a1b2-c3d4e5f60718",
  "number": "TR-CM-000031",
  "message": "✔ Transferencia TR-CM-000031 despachada: la mercadería está en tránsito hacia Sucursal Cochabamba. Asiento AS-CM-000412 por 3680.00."
}
```

Sin stock suficiente: `422` con `"code": "transfer.insufficient_stock"`.

#### `POST /v1/transfers/{id}/receive` · `transfers:write` (destino)

Recibe todas las líneas de una vez. Cuerpo opcional: sin líneas (o `{}`) = llegó todo; por cada línea con diferencia,
lo recibido y el **motivo** del faltante (obligatorio si recibe menos). Nunca más de lo despachado. V4.2: las líneas de
productos serializados se solicitan con sus series (`serials` en `POST /v1/transfers`) y un faltante de esos productos
DEBE decir qué series no llegaron (`missingSerials`; `422` con `serial.required`, `serial.count` o
`serial.not_in_document` si no).

```bash
curl -s -X POST "$MINV_API/v1/transfers/01926c01-2b3d-7e8f-a1b2-c3d4e5f60718/receive" \
  -H "Authorization: Bearer $MINV_KEY" -H "Content-Type: application/json" \
  -d '{ "lines": [ { "sku": "JUE-PS5-GT7", "receivedQuantity": 9, "shortageReason": "Caja dañada en el camión" } ] }'
```

```json
{
  "id": "01926c01-2b3d-7e8f-a1b2-c3d4e5f60718",
  "number": "TR-CM-000031",
  "message": "✔ Transferencia TR-CM-000031 recibida con 1 faltante(s) registrados como merma en tránsito. Asiento AS-EA-000057."
}
```

#### `POST /v1/transfers/{id}/cancel` · `transfers:write` (origen)

Solo una pendiente. Motivo obligatorio (≤ 200).

```bash
curl -s -X POST "$MINV_API/v1/transfers/01926c01-2b3d-7e8f-a1b2-c3d4e5f60718/cancel" \
  -H "Authorization: Bearer $MINV_KEY" -H "Content-Type: application/json" -d '{ "reason": "Pedido duplicado" }'
```

```json
{ "id": "01926c01-2b3d-7e8f-a1b2-c3d4e5f60718", "number": "TR-CM-000031", "message": "✔ Transferencia TR-CM-000031 anulada." }
```

### 6.5 Webhooks

#### `GET /v1/webhooks` · `webhooks:manage`

```bash
curl -s "$MINV_API/v1/webhooks" -H "Authorization: Bearer $MINV_KEY"
```

```json
[
  {
    "id": "01926d10-0a0b-7c0d-8e0f-101112131415",
    "url": "https://tienda.suempresa.com/webhooks/minv",
    "description": "Tienda en línea",
    "events": ["sale.completed", "transfer.dispatched", "transfer.received"],
    "branch": null,
    "isActive": true,
    "createdAt": "2026-09-25T14:00:00+00:00",
    "secretVersion": 1,
    "delivered": 120,
    "failed": 3,
    "lastAttemptAt": "2026-09-25T15:04:06+00:00",
    "lastError": null
  }
]
```

#### `POST /v1/webhooks` · `webhooks:manage`

| Campo | Descripción |
|---|---|
| `url` | https (se admite `http://localhost` solo para pruebas locales), sin usuario ni contraseña en la URL, ≤ 500 |
| `events[]` | uno o más códigos de `GET /v1/events` |
| `description` | opcional (≤ 200) |
| `branchCode` | opcional: solo eventos de esa sucursal. Una llave limitada a una sucursal queda siempre limitada a la suya |

```bash
curl -s -X POST "$MINV_API/v1/webhooks" \
  -H "Authorization: Bearer $MINV_KEY" -H "Content-Type: application/json" \
  -d '{ "url": "https://tienda.suempresa.com/webhooks/minv", "events": ["sale.completed", "transfer.received"], "description": "Tienda en línea" }'
```

`201 Created`. **El secreto se muestra una sola vez**: guárdelo para verificar las firmas.

```json
{
  "id": "01926d10-0a0b-7c0d-8e0f-101112131415",
  "url": "https://tienda.suempresa.com/webhooks/minv",
  "secret": "whsec_<43 caracteres>",
  "message": "✔ Webhook registrado. Guarde el secreto: se muestra una sola vez y sirve para verificar la firma X-MINV-Signature."
}
```

Máximo 20 webhooks activos por empresa (`webhook.limit`).

#### `POST /v1/webhooks/{id}/rotate-secret` · `webhooks:manage`

Genera un secreto nuevo. Durante **24 horas** cada entrega lleva **dos** firmas (`v1=` del nuevo y del anterior):
actualice su receptor dentro de ese plazo sin cortes.

```bash
curl -s -X POST "$MINV_API/v1/webhooks/01926d10-0a0b-7c0d-8e0f-101112131415/rotate-secret" -H "Authorization: Bearer $MINV_KEY"
```

```json
{
  "id": "01926d10-0a0b-7c0d-8e0f-101112131415",
  "url": "https://tienda.suempresa.com/webhooks/minv",
  "secret": "whsec_<43 caracteres>",
  "message": "✔ Secreto rotado: durante 24 horas cada entrega lleva las dos firmas."
}
```

#### `DELETE /v1/webhooks/{id}` · `webhooks:manage`

Desactiva el webhook (deja de recibir eventos; el historial de entregas se conserva). `204 No Content`.

```bash
curl -s -X DELETE "$MINV_API/v1/webhooks/01926d10-0a0b-7c0d-8e0f-101112131415" -H "Authorization: Bearer $MINV_KEY"
```

#### `GET /v1/webhooks/deliveries` · `webhooks:manage`

Últimos intentos de entrega (más recientes primero). Parámetros: `endpointId` (opcional), `take` (100, máx. 1000).

```bash
curl -s "$MINV_API/v1/webhooks/deliveries?take=20" -H "Authorization: Bearer $MINV_KEY"
```

```json
[
  {
    "attemptedAt": "2026-09-25T15:04:06+00:00",
    "url": "https://tienda.suempresa.com/webhooks/minv",
    "eventType": "sale.completed",
    "attempt": 1,
    "statusCode": 204,
    "succeeded": true,
    "error": null,
    "durationMs": 184
  },
  {
    "attemptedAt": "2026-09-25T14:31:10+00:00",
    "url": "https://tienda.suempresa.com/webhooks/minv",
    "eventType": "transfer.received",
    "attempt": 2,
    "statusCode": 503,
    "succeeded": false,
    "error": "Service Unavailable",
    "durationMs": 97
  }
]
```

### 6.6 Reportes

#### `GET /v1/reports/branches?from=AAAA-MM-DD&to=AAAA-MM-DD` · `reports:read`

Tablero gerencial por sucursal visible: tickets, ingresos (IVA incluido), IVA, ticket promedio, participación y valor
del stock, ingresos por día y valor en tránsito. Sale del **modelo de lectura** (vistas materializadas refrescadas cada
5 minutos): `refreshedAt` dice de cuándo son los datos.

```bash
curl -s "$MINV_API/v1/reports/branches?from=2026-09-01&to=2026-09-25" -H "Authorization: Bearer $MINV_KEY"
```

```json
{
  "from": "2026-09-01",
  "to": "2026-09-25",
  "branches": [
    { "code": "CM", "name": "Casa matriz La Paz · Av. 16 de Julio (El Prado)", "tickets": 412, "revenue": 58210.40, "tax": 7567.35, "averageTicket": 141.29, "stockValue": 152340.55, "sharePercent": 61.4 },
    { "code": "CB", "name": "Sucursal Cochabamba", "tickets": 198, "revenue": 21330.10, "tax": 2772.91, "averageTicket": 107.73, "stockValue": 30115.20, "sharePercent": 22.5 },
    { "code": "SC", "name": "Sucursal Santa Cruz", "tickets": 150, "revenue": 15250.00, "tax": 1982.50, "averageTicket": 101.67, "stockValue": 27940.00, "sharePercent": 16.1 }
  ],
  "days": [
    { "day": "2026-09-01", "revenueByBranch": [2410.50, 980.00, 610.20] }
  ],
  "totalRevenue": 94790.50,
  "totalStockValue": 210395.75,
  "inTransitValue": 3120.40,
  "refreshedAt": "2026-09-25T15:00:02+00:00"
}
```

## 7. Webhooks: eventos, firma y reintentos

### 7.1 Qué recibe

M-INV hace `POST` a su URL con `Content-Type: application/json; charset=utf-8` y estas cabeceras:

| Cabecera | Ejemplo | Uso |
|---|---|---|
| `X-MINV-Signature` | `t=1790000000,v1=5f1c…e9` (dos `v1=` durante una rotación) | verificar autenticidad e integridad (§7.3) |
| `X-MINV-Event` | `transfer.received` | enrutar sin leer el cuerpo |
| `X-MINV-Delivery` | `01926e2a3b4c7d5e8f60718293a4b5c6-2` | id del evento (sin guiones) + número de intento (diagnóstico) |
| `User-Agent` | `M-INV-Webhooks/4.0` | — |

Cuerpo (sobre común):

```json
{
  "id": "01926e2a-3b4c-7d5e-8f60-718293a4b5c6",
  "type": "sale.completed",
  "occurredAt": "2026-09-25T15:04:05.1234567+00:00",
  "tenantId": "01926b3e-79ff-7a00-b1c2-d3e4f5a6b7c8",
  "branchId": "01926b3e-7a10-7c2e-9d41-5b0f2a8e1c01",
  "data": { }
}
```

- `id` es el identificador del **evento**, igual en todos los reintentos: úselo para no procesar dos veces.
- `data` trae los campos del evento en camelCase e incluye también `eventType`, `occurredAt` y `branchId`. El orden de
  los campos no está garantizado; ignore los que no conozca (pueden agregarse campos nuevos en la v1).
- Solo recibe eventos ocurridos **después** de registrar el webhook y, si lo registró con `branchCode`, solo los de esa
  sucursal. `transfer.dispatched` es de la sucursal de **origen**; `transfer.received` y `transfer.discrepancy`, de la
  de **destino**.
- Los eventos de transferencias identifican el producto por `variantId`: para obtener el SKU consulte
  `GET /v1/transfers/{transferId}` (alcance `transfers:read`).

### 7.2 Eventos y su `data`

**`sale.completed`** · venta cobrada (caja o pedido del API; `channel` = `pos` o `api`):

```json
{
  "invoiceNumber": "F-CM-000215",
  "orderNumber": "PV-CM-000215",
  "branchIdOfSale": "01926b3e-7a10-7c2e-9d41-5b0f2a8e1c01",
  "customerCode": "CF",
  "total": 980.10,
  "tax": 127.41,
  "channel": "api",
  "lines": [
    { "sku": "JUE-PS5-GT7", "quantity": 1, "unitPrice": 469.0, "discountPercent": 0 },
    { "sku": "PAD-LOG-G240", "quantity": 2, "unitPrice": 269.0, "discountPercent": 5 }
  ],
  "eventType": "sale.completed",
  "occurredAt": "2026-09-25T15:04:05.1234567+00:00",
  "branchId": "01926b3e-7a10-7c2e-9d41-5b0f2a8e1c01"
}
```

**`sale.voided`** · venta anulada (el stock volvió):

```json
{
  "invoiceNumber": "F-CM-000215",
  "branchIdOfSale": "01926b3e-7a10-7c2e-9d41-5b0f2a8e1c01",
  "total": 145.70,
  "reason": "Cliente desistió de la compra",
  "eventType": "sale.voided",
  "occurredAt": "2026-09-25T16:10:00+00:00",
  "branchId": "01926b3e-7a10-7c2e-9d41-5b0f2a8e1c01"
}
```

**`purchase.received`** · mercadería recibida de un proveedor:

```json
{
  "orderNumber": "OC-CM-000044",
  "receiptNumber": "RC-CM-000044",
  "branchIdOfReceipt": "01926b3e-7a10-7c2e-9d41-5b0f2a8e1c01",
  "supplierCode": "P001",
  "total": 1850.00,
  "eventType": "purchase.received",
  "occurredAt": "2026-09-25T12:40:00+00:00",
  "branchId": "01926b3e-7a10-7c2e-9d41-5b0f2a8e1c01"
}
```

**`transfer.dispatched`** · salió del origen, queda en tránsito (`totalCost` al costo promedio del origen):

```json
{
  "transferId": "01926c01-2b3d-7e8f-a1b2-c3d4e5f60718",
  "number": "TR-CM-000031",
  "fromBranchId": "01926b3e-7a10-7c2e-9d41-5b0f2a8e1c01",
  "toBranchId": "01926b3e-7a11-7f55-8a02-3c1d9e4f2b02",
  "lines": [ { "variantId": "01926b3f-0001-7000-8000-00000000f004", "quantity": 10, "unitCost": 368.00 } ],
  "totalCost": 3680.00,
  "eventType": "transfer.dispatched",
  "occurredAt": "2026-09-22T19:02:11+00:00",
  "branchId": "01926b3e-7a10-7c2e-9d41-5b0f2a8e1c01"
}
```

**`transfer.received`** · el destino recibió (`lines[].quantity` = lo recibido; `shortage` = faltante total):

```json
{
  "transferId": "01926c01-2b3d-7e8f-a1b2-c3d4e5f60718",
  "number": "TR-CM-000031",
  "fromBranchId": "01926b3e-7a10-7c2e-9d41-5b0f2a8e1c01",
  "toBranchId": "01926b3e-7a11-7f55-8a02-3c1d9e4f2b02",
  "lines": [ { "variantId": "01926b3f-0001-7000-8000-00000000f004", "quantity": 9, "unitCost": 368.00 } ],
  "shortage": 1,
  "eventType": "transfer.received",
  "occurredAt": "2026-09-23T12:40:00+00:00",
  "branchId": "01926b3e-7a11-7f55-8a02-3c1d9e4f2b02"
}
```

**`transfer.discrepancy`** · un faltante al recibir (uno por línea con diferencia):

```json
{
  "transferId": "01926c01-2b3d-7e8f-a1b2-c3d4e5f60718",
  "number": "TR-CM-000031",
  "toBranchId": "01926b3e-7a11-7f55-8a02-3c1d9e4f2b02",
  "variantId": "01926b3f-0001-7000-8000-00000000f004",
  "quantity": 1,
  "reason": "Caja dañada en el camión",
  "eventType": "transfer.discrepancy",
  "occurredAt": "2026-09-23T12:40:00+00:00",
  "branchId": "01926b3e-7a11-7f55-8a02-3c1d9e4f2b02"
}
```

**`pcbuild.reserved`** · V6 · un armado (cotización) reservó el stock de sus piezas; `channel` = `Web` (tienda web) o `Desktop`
(escritorio). Las líneas identifican la pieza por `variantId` (el SKU está en `GET /v1/catalog`); nunca viajan datos del
contacto del cliente ni sus datos para la factura.

V7 · Los tres eventos `pcbuild.*` llevan un campo nuevo, `kind`: `Build` (armado de PC, `ARM-…`) o `Cart` (carrito con
cualquier producto, `RES-WEB-…` de la tienda o `RES-<sucursal>-…` del mostrador). En un carrito, la línea de un producto sin
ranura lleva `"slot": null`. El significado de los eventos no cambia (regla B-08: solo se agregan campos); un consumidor que
no distingue el tipo puede ignorarlo.

```json
{
  "pcBuildId": "01927a10-4c2e-7b3f-9a1d-2e3f4a5b6c7d",
  "number": "ARM-WEB-000003",
  "branchIdOfBuild": "01926b3e-7a10-7c2e-9d41-5b0f2a8e1c01",
  "channel": "Web",
  "total": 6842.00,
  "reservedUntil": "2026-09-29T14:05:12.55+00:00",
  "lines": [ { "variantId": "01926b3f-0001-7000-8000-00000000f0a1", "slot": "Cpu", "quantity": 1, "quotedUnitPrice": 1399.00 } ],
  "kind": "Build",
  "eventType": "pcbuild.reserved",
  "occurredAt": "2026-09-27T14:05:12.55+00:00",
  "branchId": "01926b3e-7a10-7c2e-9d41-5b0f2a8e1c01"
}
```

**`pcbuild.released`** · V6 · la reserva se liberó (el cliente desistió, el vendedor la liberó o venció: `expired` = true) y el
stock volvió a estar disponible:

```json
{
  "pcBuildId": "01927a10-4c2e-7b3f-9a1d-2e3f4a5b6c7d",
  "number": "ARM-WEB-000003",
  "branchIdOfBuild": "01926b3e-7a10-7c2e-9d41-5b0f2a8e1c01",
  "channel": "Web",
  "total": 6842.00,
  "reason": "Vencida",
  "expired": true,
  "lines": [ { "variantId": "01926b3f-0001-7000-8000-00000000f0a1", "slot": "Cpu", "quantity": 1, "quotedUnitPrice": 1399.00 } ],
  "kind": "Build",
  "eventType": "pcbuild.released",
  "occurredAt": "2026-09-29T14:10:00+00:00",
  "branchId": "01926b3e-7a10-7c2e-9d41-5b0f2a8e1c01"
}
```

**`pcbuild.sold`** · V6 · el armado se vendió en la caja (`wasReserved` = true si consumió una reserva; `sale.completed` viaja
aparte con la venta):

```json
{
  "pcBuildId": "01927a10-4c2e-7b3f-9a1d-2e3f4a5b6c7d",
  "number": "ARM-WEB-000003",
  "branchIdOfBuild": "01926b3e-7a10-7c2e-9d41-5b0f2a8e1c01",
  "channel": "Web",
  "total": 6842.00,
  "invoiceId": "01927a11-0d2e-7f10-8a2b-3c4d5e6f7a8b",
  "wasReserved": true,
  "lines": [ { "variantId": "01926b3f-0001-7000-8000-00000000f0a1", "slot": "Cpu", "quantity": 1, "quotedUnitPrice": 1399.00 } ],
  "kind": "Build",
  "eventType": "pcbuild.sold",
  "occurredAt": "2026-09-28T16:20:31+00:00",
  "branchId": "01926b3e-7a10-7c2e-9d41-5b0f2a8e1c01"
}
```

V7 · El mismo `pcbuild.reserved` de un carrito de la tienda (un monitor sin ranura):

```json
{
  "pcBuildId": "01927f42-1b7c-7d20-8e3a-5f6a7b8c9d0e",
  "number": "RES-WEB-000001",
  "branchIdOfBuild": "01926b3e-7a10-7c2e-9d41-5b0f2a8e1c01",
  "channel": "Web",
  "total": 1699.00,
  "reservedUntil": "2026-09-29T20:50:00.64+00:00",
  "lines": [ { "variantId": "01926b3f-0001-7000-8000-00000000c3d2", "slot": null, "quantity": 1, "quotedUnitPrice": 1699.00 } ],
  "kind": "Cart",
  "eventType": "pcbuild.reserved",
  "occurredAt": "2026-09-28T20:50:00.64+00:00",
  "branchId": "01926b3e-7a10-7c2e-9d41-5b0f2a8e1c01"
}
```

(Los números de documento y montos de los ejemplos son ilustrativos.)

### 7.3 Verificar la firma

`X-MINV-Signature: t=<unix>,v1=<hex>[,v1=<hex>]`, donde cada `v1` es
`hex(HMAC-SHA256(clave = secreto en UTF-8 tal cual, incluido «whsec_», mensaje = "<t>." + cuerpo))` en minúsculas.

1. Lea el cuerpo **crudo** (los bytes tal como llegaron) ANTES de convertirlo a JSON: reserializar cambia los bytes.
2. Rechace si `t` difiere más de **5 minutos** de su reloj (evita repeticiones). Sincronice el reloj con NTP.
3. Acepte si **alguna** `v1` coincide, comparando en tiempo constante.
4. Responda **2xx en menos de 10 segundos** y procese en segundo plano; deduplique por `id`.

**C# (.NET 8, ASP.NET Core Minimal API)**

```csharp
using System.Security.Cryptography;
using System.Text;

static bool VerificarFirma(string secreto, string cabecera, string cuerpo, TimeSpan? tolerancia = null)
{
    var partes = cabecera.Split(',', StringSplitOptions.TrimEntries);
    var t = partes.FirstOrDefault(p => p.StartsWith("t=", StringComparison.Ordinal));
    if (t is null || !long.TryParse(t[2..], out var unix)) return false;
    var ahora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    if (Math.Abs(ahora - unix) > (tolerancia ?? TimeSpan.FromMinutes(5)).TotalSeconds) return false;
    var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secreto), Encoding.UTF8.GetBytes($"{unix}.{cuerpo}"));
    var esperada = Encoding.ASCII.GetBytes("v1=" + Convert.ToHexString(mac).ToLowerInvariant());
    return partes.Where(p => p.StartsWith("v1=", StringComparison.Ordinal))
                 .Any(p => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(p), esperada));
}

app.MapPost("/webhooks/minv", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body, Encoding.UTF8);
    var cuerpo = await reader.ReadToEndAsync();                       // crudo, antes de deserializar
    var secreto = Environment.GetEnvironmentVariable("MINV_WEBHOOK_SECRET")!;
    if (!VerificarFirma(secreto, request.Headers["X-MINV-Signature"].ToString(), cuerpo))
    {
        return Results.Unauthorized();
    }
    using var evento = System.Text.Json.JsonDocument.Parse(cuerpo);
    var id = evento.RootElement.GetProperty("id").GetGuid();           // deduplicar por id
    // … encolar el procesamiento …
    return Results.NoContent();
});
```

**Node.js (18+, Express)**

```js
const crypto = require('node:crypto');
const express = require('express');
const app = express();

function verificarFirma(secreto, cabecera, cuerpo /* Buffer crudo */, toleranciaSeg = 300) {
  const partes = (cabecera || '').split(',').map((p) => p.trim());
  const t = partes.find((p) => p.startsWith('t='));
  if (!t) return false;
  const unix = Number(t.slice(2));
  if (!Number.isInteger(unix) || Math.abs(Date.now() / 1000 - unix) > toleranciaSeg) return false;
  const esperada = Buffer.from(
    'v1=' + crypto.createHmac('sha256', secreto).update(`${unix}.`).update(cuerpo).digest('hex'));
  return partes.filter((p) => p.startsWith('v1=')).some((p) => {
    const recibida = Buffer.from(p);
    return recibida.length === esperada.length && crypto.timingSafeEqual(recibida, esperada);
  });
}

// express.raw: el cuerpo llega como Buffer, sin reserializar
app.post('/webhooks/minv', express.raw({ type: 'application/json' }), (req, res) => {
  if (!verificarFirma(process.env.MINV_WEBHOOK_SECRET, req.get('X-MINV-Signature'), req.body)) {
    return res.status(401).end();
  }
  const evento = JSON.parse(req.body.toString('utf8'));
  // deduplicar por evento.id y procesar en segundo plano
  res.status(204).end();
});

app.listen(3000);
```

**Python (3.10+, Flask)**

```python
import hashlib
import hmac
import json
import os
import time

from flask import Flask, abort, request

app = Flask(__name__)


def verificar_firma(secreto: str, cabecera: str | None, cuerpo: bytes, tolerancia: int = 300) -> bool:
    partes = [p.strip() for p in (cabecera or "").split(",")]
    t = next((p for p in partes if p.startswith("t=")), None)
    if t is None:
        return False
    try:
        unix = int(t[2:])
    except ValueError:
        return False
    if abs(time.time() - unix) > tolerancia:
        return False
    mac = hmac.new(secreto.encode("utf-8"), f"{unix}.".encode("utf-8") + cuerpo, hashlib.sha256).hexdigest()
    esperada = "v1=" + mac
    return any(hmac.compare_digest(p, esperada) for p in partes if p.startswith("v1="))


@app.post("/webhooks/minv")
def recibir():
    cuerpo = request.get_data()  # bytes crudos
    if not verificar_firma(os.environ["MINV_WEBHOOK_SECRET"], request.headers.get("X-MINV-Signature"), cuerpo):
        abort(401)
    evento = json.loads(cuerpo)
    # deduplicar por evento["id"] y procesar en segundo plano
    return "", 204
```

La función de referencia de M-INV es `ApiKeyTokens.Verify` (`src/1. Core/MINV.Application/Integration/ApiKeyTokens.cs`);
las pruebas de integración la usan contra entregas reales.

### 7.4 Entregas y reintentos

- **Éxito**: cualquier respuesta 2xx en menos de 10 s. Las redirecciones (3xx) **no** se siguen y cuentan como falla.
- **Reintentos**: hasta **8 intentos** por evento y destino. Espera antes de cada intento: 0 (inmediato), 1 min, 5 min,
  30 min, 2 h, 6 h, 12 h y 24 h (≈ 44,5 h en total). Después, el evento queda **agotado** y se ve en el escritorio
  (Integraciones › Entregas) y en `GET /v1/webhooks/deliveries`.
- **Al menos una vez**: puede recibir el mismo evento más de una vez (por ejemplo, si su servidor respondió tarde o si
  una réplica del gateway se reinició). Deduplique por `id`.
- **Orden**: no garantizado entre eventos distintos; use `occurredAt` y el estado actual (`GET /v1/transfers/{id}`) si
  el orden importa.
- Su URL debe ser **https** y resolver a una **IP pública**: por seguridad (SSRF), el gateway no se conecta a
  direcciones privadas, de loopback ni de metadatos de la nube.

## 8. Buenas prácticas

- Una llave por integración y por entorno, con los alcances mínimos; limítela a una sucursal cuando pueda; póngale
  vencimiento y rótela (cree la nueva, cámbiela en su sistema y revoque la anterior).
- Nunca registre la llave ni el secreto del webhook en los logs; guárdelos en una bóveda o en variables de entorno.
- `externalId` estable por pedido (el id de su plataforma), nunca aleatorio por intento.
- Prefiera webhooks a consultar el stock en bucle; para una sincronización completa, pagine con `pageSize=500`.
- Muestre al usuario el `detail` de los errores 4xx: están en español y dicen qué corregir.

## 9. Historial de la API

| Versión | Cambios |
|---|---|
| v1 (M-INV 4.0.0-alpha.1) | Primera versión: catálogo, sucursales, stock por sucursal y consolidado, pedidos idempotentes, transferencias, webhooks firmados con rotación, reportes por sucursal, OpenAPI |
