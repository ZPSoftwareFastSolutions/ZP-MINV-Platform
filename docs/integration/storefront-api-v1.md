# Contrato de la API pública de tienda · M-INV `/storefront/v1` (V6 · V7)

> Es lo que consume el catálogo web (`src/3. Presentation/MINV.WebCatalog`) y lo ÚNICO que la web necesita saber del
> servidor. Lo publica `MINV.ApiGateway` (`src/3. Presentation/MINV.ApiGateway/Endpoints/StorefrontEndpoints.cs`) sobre los
> casos de uso de `MINV.Application/Storefront`. Diseño: `docs/architecture/tienda-web-conectada-v6.md`; reglas S-01 a S-10:
> `.claude/v6-storefront-rules.md`. Los JSON de ejemplo se copiaron de una ejecución real contra una base temporal cargada con
> `minv datos-prueba` (empresa TECHZONE, sucursal CM): los ids, números y fechas cambian en cada carga.
>
> **V7 · carrito** (diseño `docs/architecture/plataforma-web-v7.md` §5, regla P-05 de `.claude/v7-web-platform-rules.md`): la
> misma ruta de reservas admite un **carrito** con cualquier producto (`kind = "cart"`), los **días para recogerlo**
> (`holdDays`) y los **datos para la factura** (`buyer`). Es un cambio **compatible**: solo se agregan campos opcionales a la
> petición y campos nuevos a las respuestas; una web de la V6 sigue funcionando sin tocar nada. Resumen en §7.

## 1. Conexión

| Qué | Valor |
|---|---|
| Base | `http://localhost:5090` en local (`tools\servidores_locales.ps1`); en producción, la URL pública del gateway detrás de TLS. La web la recibe en `VITE_API_URL`. |
| Prefijo | `/storefront/v1` |
| Autenticación | **Ninguna**: no hay API Key ni sesión. El gateway ejecuta cada petición como el usuario técnico `tienda-web` (rol `TIENDA_WEB`) de la empresa configurada (`Minv:Storefront:TenantCode`) en la sucursal de la tienda (`Minv:Storefront:BranchCode`, por defecto la casa matriz). Nada de lo que la API devuelve depende del navegador. |
| CORS | Solo los orígenes de `Minv:Storefront:AllowedOrigins` (en local `http://localhost:5173`); métodos `GET`, `POST`, `OPTIONS`; cualquier cabecera; se exponen `ETag`, `Idempotent-Replayed`, `Location` y `Cache-Control`. |
| Formato | JSON UTF-8 en camelCase; fechas ISO 8601 con zona (`2026-09-27T14:05:12.55+00:00`); montos en bolivianos (Bs) con IVA incluido; cantidades numéricas (`available`, `reserved`, `onHand` pueden traer decimales en unidades que los admiten). |
| Moneda y zona | Bs; las fechas de vigencia se calculan en la zona de la empresa (`America/La_Paz`). |
| Documentación viva | `GET /docs` (Swagger UI, etiqueta «Tienda web») y `GET /docs/v1/openapi.json`. |
| Si la tienda no está configurada | Toda ruta responde `503` con `{"title":"Tienda web no disponible", …}`. |

## 2. Límites y caché

| Regla | Valor |
|---|---|
| Lecturas por IP | 300 por minuto (`Minv:Storefront:ReadsPerMinute`) sobre todo `/storefront/v1`; al superarlo, `429` sin cuerpo. |
| Reservas por IP | 10 por minuto (`Minv:Storefront:ReservationsPerMinute`) para `POST /reservations` y `POST /reservations/{number}/cancel` (cuentan también las rechazadas). |
| Instantánea | `Cache-Control: public, max-age=30`: la web la refresca al volver a la pestaña y cada 60 s. |
| Producto | `Cache-Control: no-cache`: la ficha consulta la disponibilidad fresca al abrirse. |
| Imagen | `Cache-Control: public, max-age=3600` y `ETag` (el id de la imagen): con `If-None-Match` responde `304`. |
| Reserva | Vale los días que pide quien reserva (`holdDays` 1 a 3: 24, 48 o 72 h) o, sin ese campo, `Minv:Storefront:ReservationHours` horas (48). Ninguna reserva pasa de `Minv:Storefront:MaxReservationHours` (72): con un tope menor, `maxHoldDays` baja y las horas por defecto se acotan. Vencida, el gateway la cierra solo (cada 5 minutos) y el stock vuelve. |
| Tamaño de una reserva | 1 a 20 líneas; 1 a 16 unidades por línea; notas ≤ 500; nombre ≤ 120; teléfono ≤ 30; documento ≤ 20; complemento ≤ 5; razón social ≤ 150. |
| Textos de una línea (V7) | El nombre de contacto, las notas, el nombre de la reserva y la razón social **no admiten saltos de línea, tabuladores ni otros caracteres de control** (`400 validation`). Los espacios y saltos de los extremos se recortan; si la web usa un área de texto para las notas, debe unir las líneas antes de enviar. |

## 3. Errores

Los errores salen como `application/problem+json` (RFC 7807) con `status`, `title`, `detail` y, cuando corresponde, `code`
(código estable del dominio) y `errors` (lista de mensajes de validación). La web muestra `detail` tal cual (está en español).

| HTTP | `title` | Cuándo |
|---|---|---|
| 400 | `validation` | Cuerpo inválido o campos que no pasan la validación (`errors[]`): sin líneas, cantidad fuera de 1-16, sin nombre o teléfono, sin `Idempotency-Key`… V7: `kind` que no es `build` ni `cart`, `holdDays` fuera de 1 a 3, `buyer` mal formado (tipo fuera de 1 a 5, sin número, textos demasiado largos) o un texto con saltos de línea. |
| 404 | `not_found` | Producto o imagen inexistentes; reserva inexistente **o teléfono que no coincide** (no se distingue, regla S-06). |
| 409 | `insufficient_stock` | `code` = `storefront.insufficient_stock`: falta stock de una o más piezas; **no se reservó nada** y `shortages[]` dice qué falta y cuánto hay. |
| 422 | `domain` | Regla del dominio: `code` = `pcbuild.contact_phone` (teléfono no boliviano), `pcbuild.slot` (en un armado, dos piezas en una ranura única; en cualquier reserva, una ranura que no existe), `pcbuild.state` (la reserva ya no está reservada: no se cancela dos veces), `price.missing`, `product.inactive`… V7: `buyer.doc_type`, `buyer.doc_number`, `buyer.doc_numeric` (CI y NIT solo dígitos), `buyer.complement` (el complemento solo con CI, ≤ 5), `storefront.hold_days` (más días de los que admite el servidor, ver `maxHoldDays`), `guard.control_chars`. |
| 422 | `idempotency` | La misma `Idempotency-Key` con otro contenido. |
| 429 | — | Límite por IP superado. |
| 503 | `Tienda web no disponible` | La tienda no está configurada en el servidor. |

```json
{
  "type": "https://minv.example/errores/storefront.insufficient_stock",
  "title": "insufficient_stock",
  "status": 409,
  "detail": "No hay stock suficiente para 2 pieza(s): GPU-MSI-4060-V2XB (pedido 16, disponible 8); CASE-COR-4000D (pedido 16, disponible 4)",
  "code": "storefront.insufficient_stock",
  "shortages": [
    {
      "sku": "GPU-MSI-4060-V2XB",
      "name": "Tarjeta de video MSI GeForce RTX 4060 VENTUS 2X BLACK 8G OC",
      "requested": 16,
      "available": 8
    },
    {
      "sku": "CASE-COR-4000D",
      "name": "Gabinete Corsair 4000D Airflow negro",
      "requested": 16,
      "available": 4
    }
  ],
  "traceId": "00-2603809486bd5f7da4e79cdffaa494af-d26e11a8107fb49e-00"
}
```

## 4. Rutas

### 4.1 `GET /storefront/v1/catalog` · instantánea del catálogo

Todo lo que la web necesita para pintar el sitio en una sola llamada. Devuelve `StorefrontCatalogView`:

| Campo | Tipo | Significado |
|---|---|---|
| `company` | `{ code, name, branches[{ code, name }] }` | Empresa y sus sucursales activas. |
| `branch` | `{ code, name }` | Sucursal cuya disponibilidad se muestra (la de la tienda). |
| `categories[]` | `{ code, name, slug, parent, icon, description, productCount }` | Árbol de categorías (recorrido en profundidad: cada raíz seguida de sus hijas). `parent` es null en las raíces; `icon` es el nombre del componente de lucide-react (`Cpu`, `Gpu`, `Monitor`… o `Tag` si la categoría no tiene ícono conocido); `productCount` cuenta los productos de la categoría y de sus subcategorías. |
| `brands[]` | `{ code, name, productCount }` | Marcas con productos publicados (`code` = nombre en mayúsculas sin espacios ni acentos). |
| `products[]` | ver 4.2 | Productos activos con precio en la lista por defecto, ordenados por SKU. |
| `presets[]` | ver 4.4 | Armados sugeridos publicados desde el escritorio (de la sucursal de la tienda). |
| `generatedAt` | fecha | Cuándo se generó la instantánea. |
| `reservationHours` | entero | V7 · Horas que vale una reserva que no indica `holdDays` (48; `Minv:Storefront:ReservationHours` acotado por el tope). La web muestra ESTE valor: no lo supone. |
| `maxHoldDays` | entero | V7 · Días que puede pedir quien reserva en `holdDays`: de 1 a este número (3; los que caben en `Minv:Storefront:MaxReservationHours`, nunca más de 3). |

Ejemplo real (recortado a un producto, una categoría, una marca y un armado):

```json
{
  "company": {
    "code": "TECHZONE",
    "name": "Tech Zone Gaming S.R.L.",
    "branches": [
      {
        "code": "CB",
        "name": "Sucursal Cochabamba"
      },
      {
        "code": "CM",
        "name": "Casa matriz La Paz · Av. 16 de Julio (El Prado)"
      },
      {
        "code": "SC",
        "name": "Sucursal Santa Cruz"
      }
    ]
  },
  "branch": {
    "code": "CM",
    "name": "Casa matriz La Paz · Av. 16 de Julio (El Prado)"
  },
  "categories": [   // … 35 categorías
    {
      "code": "COMP",
      "name": "Componentes",
      "slug": "componentes",
      "parent": null,
      "icon": "Cpu",
      "description": "Componentes para armar o actualizar tu PC",
      "productCount": 63
    },
    {
      "code": "CASE",
      "name": "Gabinetes",
      "slug": "gabinetes",
      "parent": "COMP",
      "icon": "Box",
      "description": "Componentes > Gabinetes",
      "productCount": 6
    }
  ],
  "brands": [   // … 40 marcas
    {
      "code": "CORSAIR",
      "name": "Corsair",
      "productCount": 9
    }
  ],
  "products": [   // … 159 productos
    {
      "sku": "CASE-COR-4000D",
      "slug": "case-cor-4000d",
      "name": "Gabinete Corsair 4000D Airflow negro",
      "shortName": "Gabinete Corsair 4000D Airflow negro",
      "category": "CASE",
      "categoryName": "Gabinetes",
      "categoryPath": "Componentes > Gabinetes",
      "brand": "Corsair",
      "price": 1299.0,
      "listPrice": null,
      "image": "/storefront/v1/products/CASE-COR-4000D/image",
      "available": 4,
      "reserved": 0,
      "onHand": 4,
      "condition": "Nuevo",
      "warrantyMonths": 12,
      "serialized": false,
      "popularity": 7,
      "tags": [
        "nuevo"
      ],
      "description": "Gabinete Corsair 4000D Airflow negro. Marca Corsair · Componentes › Gabinetes.",
      "highlights": [
        "Formatos de placa soportados: ATX, Micro-ATX, Mini-ITX",
        "Tipo: Mid Tower",
        "Color: Negro"
      ],
      "specs": [
        {
          "key": "condicion",
          "label": "Condición",
          "value": "Nuevo",
          "text": "Nuevo",
          "unit": null,
          "filterable": true
        },
        {
          "key": "formatos_placa",
          "label": "Formatos de placa soportados",
          "value": [
            "ATX",
            "Micro-ATX",
            "Mini-ITX"
          ],
          "text": "ATX, Micro-ATX, Mini-ITX",
          "unit": null,
          "filterable": true
        },
        {
          "key": "largo_max_gpu",
          "label": "Largo máximo de GPU",
          "value": 360,
          "text": "360 mm",
          "unit": "mm",
          "filterable": false
        },
        {
          "key": "altura_max_disipador",
          "label": "Altura máxima del disipador",
          "value": 170,
          "text": "170 mm",
          "unit": "mm",
          "filterable": false
        },
        {
          "key": "radiador_max",
          "label": "Radiador máximo",
          "value": 360,
          "text": "360 mm",
          "unit": "mm",
          "filterable": false
        },
        {
          "key": "tipo_gabinete",
          "label": "Tipo",
          "value": "Mid Tower",
          "text": "Mid Tower",
          "unit": null,
          "filterable": true
        },
        {
          "key": "ventiladores_incluidos",
          "label": "Ventiladores incluidos",
          "value": 2,
          "text": "2",
          "unit": null,
          "filterable": false
        },
        {
          "key": "panel_lateral",
          "label": "Panel lateral",
          "value": "Vidrio templado",
          "text": "Vidrio templado",
          "unit": null,
          "filterable": false
        },
        {
          "key": "color",
          "label": "Color",
          "value": "Negro",
          "text": "Negro",
          "unit": null,
          "filterable": true
        }
      ]
    }
  ],
  "presets": [   // … 3 armados publicados
    {
      "id": "arm-cm-000001",
      "number": "ARM-CM-000001",
      "name": "PC Gamer Entrada 1080p (Core i5-14400F + RTX 4060)",
      "tier": "entrada",
      "total": 10421.0,
      "available": true,
      "lines": [
        {
          "slot": "cpu",
          "sku": "CPU-INT-14400F",
          "quantity": 1,
          "unitPrice": 1399.0
        },
        {
          "slot": "motherboard",
          "sku": "MB-GIG-B760M-DS3HD4",
          "quantity": 1,
          "unitPrice": 1249.0
        },
        {
          "slot": "ram",
          "sku": "RAM-COR-D4-16-3200",
          "quantity": 1,
          "unitPrice": 749.0
        },
        {
          "slot": "gpu",
          "sku": "GPU-MSI-4060-V2XB",
          "quantity": 1,
          "unitPrice": 3199.0
        },
        {
          "slot": "storage",
          "sku": "SSD-KNG-NV3-1TB",
          "quantity": 1,
          "unitPrice": 879.0
        },
        {
          "slot": "psu",
          "sku": "PSU-MSI-A650BN",
          "quantity": 1,
          "unitPrice": 649.0
        },
        {
          "slot": "case",
          "sku": "CASE-MSI-FORGE120A",
          "quantity": 1,
          "unitPrice": 749.0
        },
        {
          "slot": "software",
          "sku": "LIC-MS-W11HOME",
          "quantity": 1,
          "unitPrice": 1299.0
        },
        {
          "slot": "software",
          "sku": "SRV-ENSAMBLE-PC",
          "quantity": 1,
          "unitPrice": 249.0
        }
      ]
    }
  ],
  "generatedAt": "2026-09-27T18:39:56.0601584+00:00",
  "reservationHours": 48,
  "maxHoldDays": 3
}
```

### 4.2 `GET /storefront/v1/products/{slug}` · un producto con disponibilidad fresca

`slug` es el SKU en minúsculas (`case-cor-4000d`); también acepta el SKU tal cual. Devuelve un `StorefrontProduct`:

| Campo | Tipo | Significado |
|---|---|---|
| `sku`, `slug` | texto | Identificador; `slug` = SKU en minúsculas. |
| `name`, `shortName` | texto | Nombre completo y sin el paréntesis final de especificaciones. |
| `category`, `categoryName`, `categoryPath` | texto | Código, nombre y ruta («Componentes > Gabinetes»). |
| `brand` | texto | Marca (o «Sin marca»). |
| `price` | número | Precio de venta en Bs con IVA (lista de precios por defecto). |
| `listPrice` | número o null | Precio «de lista» tachado cuando la empresa tiene una lista de precios vigente NO predeterminada llamada como web u oferta con un precio mayor; si no maneja el concepto, null. |
| `image` | texto o null | Ruta relativa de la imagen (`/storefront/v1/products/{sku}/image`) o null si no tiene. |
| `available` | número | **Disponible = existencias − reservado** en la sucursal de la tienda (nunca negativo). |
| `reserved` | número | Unidades reservadas (reservas web y del escritorio, reservas de caja). |
| `onHand` | número | Existencias físicas. |
| `condition` | texto | «Nuevo», «Reacondicionado» o «Usado» (especificación `condicion`). |
| `warrantyMonths` | entero | Meses de garantía (0 si no tiene). |
| `serialized` | booleano | Se vende con número de serie o IMEI. |
| `popularity` | entero 1-10 | Ventas de los últimos 90 días normalizadas (10 = lo más vendido). |
| `tags[]` | texto | `destacado` (popularidad ≥ 8), `oferta` (con `listPrice`), `nuevo` (alta en los últimos 30 días). |
| `description` | texto | La del producto o, si no tiene, una generada como en la web de la V5. |
| `highlights[]` | texto | Hasta 3 «Etiqueta: valor» de las especificaciones filtrables. |
| `specs[]` | `{ key, label, value, text, unit, filterable }` | Ficha técnica en el orden de la categoría madre a la hoja. `value` es número, texto o lista de textos (multivalor); `text` viene formateado («5,1 GHz», «DDR5», «Nintendo Switch 2»). |

Ejemplo real:

```json
{
  "sku": "CASE-COR-4000D",
  "slug": "case-cor-4000d",
  "name": "Gabinete Corsair 4000D Airflow negro",
  "shortName": "Gabinete Corsair 4000D Airflow negro",
  "category": "CASE",
  "categoryName": "Gabinetes",
  "categoryPath": "Componentes > Gabinetes",
  "brand": "Corsair",
  "price": 1299.0,
  "listPrice": null,
  "image": "/storefront/v1/products/CASE-COR-4000D/image",
  "available": 4,
  "reserved": 0,
  "onHand": 4,
  "condition": "Nuevo",
  "warrantyMonths": 12,
  "serialized": false,
  "popularity": 10,
  "tags": [
    "destacado",
    "nuevo"
  ],
  "description": "Gabinete Corsair 4000D Airflow negro. Marca Corsair · Componentes › Gabinetes.",
  "highlights": [
    "Formatos de placa soportados: ATX, Micro-ATX, Mini-ITX",
    "Tipo: Mid Tower",
    "Color: Negro"
  ],
  "specs": [
    {
      "key": "condicion",
      "label": "Condición",
      "value": "Nuevo",
      "text": "Nuevo",
      "unit": null,
      "filterable": true
    },
    {
      "key": "formatos_placa",
      "label": "Formatos de placa soportados",
      "value": [
        "ATX",
        "Micro-ATX",
        "Mini-ITX"
      ],
      "text": "ATX, Micro-ATX, Mini-ITX",
      "unit": null,
      "filterable": true
    },
    {
      "key": "largo_max_gpu",
      "label": "Largo máximo de GPU",
      "value": 360,
      "text": "360 mm",
      "unit": "mm",
      "filterable": false
    },
    {
      "key": "altura_max_disipador",
      "label": "Altura máxima del disipador",
      "value": 170,
      "text": "170 mm",
      "unit": "mm",
      "filterable": false
    },
    {
      "key": "radiador_max",
      "label": "Radiador máximo",
      "value": 360,
      "text": "360 mm",
      "unit": "mm",
      "filterable": false
    },
    {
      "key": "tipo_gabinete",
      "label": "Tipo",
      "value": "Mid Tower",
      "text": "Mid Tower",
      "unit": null,
      "filterable": true
    },
    {
      "key": "ventiladores_incluidos",
      "label": "Ventiladores incluidos",
      "value": 2,
      "text": "2",
      "unit": null,
      "filterable": false
    },
    {
      "key": "panel_lateral",
      "label": "Panel lateral",
      "value": "Vidrio templado",
      "text": "Vidrio templado",
      "unit": null,
      "filterable": false
    },
    {
      "key": "color",
      "label": "Color",
      "value": "Negro",
      "text": "Negro",
      "unit": null,
      "filterable": true
    }
  ]
}
```

`404` si no existe o no está publicado:

```json
{
  "type": "https://minv.example/errores/not_found",
  "title": "not_found",
  "status": 404,
  "detail": "El producto no-existe no está en el catálogo.",
  "errors": null,
  "code": null,
  "traceId": "00-2f97f9ea996bf3c5c60678e46e7d6b41-3453a4e8d7351c2b-00"
}
```

### 4.3 `GET /storefront/v1/products/{sku}/image` · imagen

Responde el binario (`image/png` o `image/jpeg`) con `ETag` y `Cache-Control: public, max-age=3600`; `304 Not Modified` con
`If-None-Match`; `404` si el producto no tiene imagen. Ejemplo de cabeceras de una ejecución real: `200 image/png "01a0e425727c7301bcc06a52b74d15f0" public, max-age=3600 bytes=44309`.

### 4.4 `GET /storefront/v1/presets` · armados sugeridos

Los armados publicados desde el escritorio (`PublishPcBuildCommand`) de la sucursal de la tienda, ordenados por precio. Es
el mismo arreglo que `catalog.presets`. Cada `StorefrontPreset`:

| Campo | Tipo | Significado |
|---|---|---|
| `id` | texto | Número en minúsculas (`arm-cm-000001`). |
| `number` | texto | Número del armado en M-INV. |
| `name` | texto | Nombre del armado. |
| `tier` | texto | `entrada`, `media`, `alta`, `entusiasta`, `oficina` o `creador` (por el nombre y, si no, por el precio). |
| `total` | número | Suma de las líneas a los precios cotizados. |
| `available` | booleano | Todas sus piezas tienen disponible en la sucursal de la tienda. |
| `lines[]` | `{ slot, sku, quantity, unitPrice }` | `slot` es la ranura de la web: `cpu`, `motherboard`, `ram`, `gpu`, `storage`, `psu`, `case`, `cooler`, `monitor`, `peripherals`, `software`. |

```json
[
  {
    "id": "arm-cm-000001",
    "number": "ARM-CM-000001",
    "name": "PC Gamer Entrada 1080p (Core i5-14400F + RTX 4060)",
    "tier": "entrada",
    "total": 10421.0,
    "available": true,
    "lines": [
      {
        "slot": "cpu",
        "sku": "CPU-INT-14400F",
        "quantity": 1,
        "unitPrice": 1399.0
      },
      {
        "slot": "motherboard",
        "sku": "MB-GIG-B760M-DS3HD4",
        "quantity": 1,
        "unitPrice": 1249.0
      },
      {
        "slot": "ram",
        "sku": "RAM-COR-D4-16-3200",
        "quantity": 1,
        "unitPrice": 749.0
      },
      {
        "slot": "gpu",
        "sku": "GPU-MSI-4060-V2XB",
        "quantity": 1,
        "unitPrice": 3199.0
      },
      {
        "slot": "storage",
        "sku": "SSD-KNG-NV3-1TB",
        "quantity": 1,
        "unitPrice": 879.0
      },
      {
        "slot": "psu",
        "sku": "PSU-MSI-A650BN",
        "quantity": 1,
        "unitPrice": 649.0
      },
      {
        "slot": "case",
        "sku": "CASE-MSI-FORGE120A",
        "quantity": 1,
        "unitPrice": 749.0
      },
      {
        "slot": "software",
        "sku": "LIC-MS-W11HOME",
        "quantity": 1,
        "unitPrice": 1299.0
      },
      {
        "slot": "software",
        "sku": "SRV-ENSAMBLE-PC",
        "quantity": 1,
        "unitPrice": 249.0
      }
    ]
  },
  {
    "id": "arm-cm-000002",
    "number": "ARM-CM-000002",
    "name": "PC Gamer Media 1440p (Ryzen 5 7600 + RTX 5060 Ti 16 GB)",
    "tier": "media",
    "total": 17680.0,
    "available": true,
    "lines": [
      {
        "slot": "cpu",
        "sku": "CPU-AMD-7600",
        "quantity": 1,
        "unitPrice": 2049.0
      },
      {
        "slot": "motherboard",
        "sku": "MB-MSI-B650-GPWIFI",
        "quantity": 1,
        "unitPrice": 1949.0
      },
      {
        "slot": "ram",
        "sku": "RAM-KNG-D5-32-6000",
        "quantity": 1,
        "unitPrice": 2549.0
      },
      {
        "slot": "gpu",
        "sku": "GPU-ASU-5060TI-DUAL16",
        "quantity": 1,
        "unitPrice": 5099.0
      },
      {
        "slot": "storage",
        "sku": "SSD-SAM-990PRO-1TB",
        "quantity": 1,
        "unitPrice": 1449.0
      },
      {
        "slot": "psu",
        "sku": "PSU-COR-RM750X",
        "quantity": 1,
        "unitPrice": 1299.0
      },
      {
        "slot": "case",
        "sku": "CASE-COR-4000D",
        "quantity": 1,
        "unitPrice": 1299.0
      },
      {
        "slot": "cooler",
        "sku": "COOL-DPC-AK400",
        "quantity": 1,
        "unitPrice": 439.0
      },
      {
        "slot": "software",
        "sku": "LIC-MS-W11HOME",
        "quantity": 1,
        "unitPrice": 1299.0
      },
      {
        "slot": "software",
        "sku": "SRV-ENSAMBLE-PC",
        "quantity": 1,
        "unitPrice": 249.0
      }
    ]
  }
]
// … 3 armados en total
```

### 4.5 `POST /storefront/v1/reservations` · reservar un armado o un carrito

Crea la cotización `ARM-WEB-000001` (armado) o la reserva `RES-WEB-000001` (carrito, V7) en el canal Web con el stock de
cada línea reservado por 48 h (o por los días de `holdDays`), todo en una transacción (regla S-03). Cabecera
**obligatoria** `Idempotency-Key` (texto ≤ 100, único por reserva: la web genera un UUID por intento). Cuerpo
(`StorefrontReservationRequest`):

| Campo | Obligatorio | Significado |
|---|---|---|
| `lines[].sku` | sí | SKU de la pieza. |
| `lines[].quantity` | no (1) | 1 a 16. |
| `lines[].slot` | no | Ranura de la web (`cpu`, `motherboard`, `ram`, `gpu`, `storage`, `psu`, `case`, `cooler`, `monitor`, `peripherals`, `software`). **Armado:** si falta se deduce de la ficha técnica o de la categoría, y dos piezas en una ranura única (CPU, placa, fuente, gabinete, refrigeración) → `422 pcbuild.slot`. **Carrito:** si falta, la línea queda SIN ranura (`slot: null`); si viene, se conserva; no hay ranura única. Una ranura que no existe → `422 pcbuild.slot`. |
| `contact.name` | sí | Quien reserva (≤ 120, una línea). |
| `contact.phone` | sí | Teléfono o WhatsApp de Bolivia: 7 u 8 dígitos, con o sin `+591`, espacios o guiones (`+591 71234567`, `7123-4567`). Se guarda normalizado y es la «clave» para consultar o cancelar. |
| `contact.email` | no | Correo válido. V7: con correo, la reserva encola el correo de confirmación (`mailQueued`). |
| `notes` | no | Para la tienda (≤ 500, una línea). |
| `name` | no | Nombre de la reserva (≤ 150, una línea; por defecto «Armado web de <nombre>» o, en un carrito, «Reserva de <nombre>»). |
| `idempotencyKey` | no | Alternativa a la cabecera. |
| `kind` | no (`"build"`) | V7 · `"build"` = armado de PC · `"cart"` = carrito con cualquier producto (un solo monitor, una consola y dos juegos…). Otro valor → `400`. |
| `holdDays` | no | V7 · Días para recoger la reserva: 1, 2 o 3 (24, 48 o 72 h). Sin valor, `reservationHours` del catálogo. Fuera de 1 a 3 → `400`; más que `maxHoldDays` → `422 storefront.hold_days`. Vale para armados y carritos. |
| `buyer` | no | V7 · Datos para la factura (la caja los precarga al cobrar). Si viene, `documentType` y `documentNumber` son obligatorios. |
| `buyer.documentType` | con `buyer` | Tipo de documento del SIN: 1 CI · 2 CEX · 3 pasaporte · 4 otro documento · 5 NIT. |
| `buyer.documentNumber` | con `buyer` | Número (≤ 20). Con CI o NIT, solo dígitos (`422 buyer.doc_numeric`). |
| `buyer.complement` | no | Complemento del SEGIP: solo con CI, ≤ 5 (`422 buyer.complement`). Se guarda en mayúsculas. |
| `buyer.name` | no | Nombre o razón social para la factura (≤ 150, una línea). |

La compatibilidad de las piezas de un **armado** se evalúa e informa (`hasCompatibilityWarnings`), nunca bloquea: el
vendedor la revisa en el escritorio. En un **carrito** no se evalúa (`hasCompatibilityWarnings` es siempre `false`).
Los datos de `buyer` **nunca se devuelven** por esta API (ni en esta respuesta ni al consultar): son una instantánea de
quien reserva que solo ve el personal con `sales.pcbuild.manage` (reglas S-06 y P-05). Respuestas:

- `201 Created` + `Location: /storefront/v1/reservations/{number}` con `StorefrontReservationView`.
- `200 OK` + `Idempotent-Replayed: true` con la MISMA reserva si se repite la llave con el mismo contenido.
- `409` `storefront.insufficient_stock` con `shortages[]` (no se reservó nada).
- `422` `idempotency` (misma llave, otro contenido), `422` `domain` (`code`), `400` `validation` (`errors[]`).

`StorefrontReservationView`:

| Campo | Significado |
|---|---|
| `number` | `ARM-WEB-000001` (armado) o `RES-WEB-000001` (carrito); cada numeración es por empresa y no se mezclan. |
| `status` | `Reserved`, `Sold`, `Cancelled` o `Expired` (`Expired` también mientras la reserva vencida espera al trabajo que la cierra). |
| `statusText` | «Reservada», «Vendida», «Cancelada», «Vencida». |
| `createdAt`, `reservedUntil` | Cuándo se hizo y hasta cuándo vale. |
| `total` | Bs, suma de las líneas. |
| `contactName`, `branch`, `notes` | Nombre de quien reservó (nunca el teléfono, el correo ni los datos para la factura), sucursal donde se retira, notas. |
| `hasCompatibilityWarnings` | La ficha técnica encontró errores de compatibilidad entre las piezas (solo en armados). |
| `lines[]` | `{ slot, sku, name, quantity, unitPrice, subtotal }` a los precios cotizados (congelados). V7: `slot` es `null` en el producto sin ranura de un carrito. |
| `cancelReason` | Motivo del cierre («Vencida», «Cancelada por el cliente desde la tienda web», el del vendedor) o null. |
| `kind` | V7 · `"build"` o `"cart"`. |
| `mailQueued` | V7 · `true` si al reservar se encoló el correo de confirmación (sale después, en segundo plano). Por ahora siempre `false`: lo activa el correo de la reserva (diseño §6). En la consulta y la cancelación no se informa (`false`). |

Petición y respuesta reales de un armado:

```http
POST /storefront/v1/reservations
Content-Type: application/json
Idempotency-Key: 4f6a0c2e8d1b4c3e9a7f5b2d1e0c9a8b
```

```json
{
  "lines": [
    {
      "sku": "CPU-AMD-7600",
      "quantity": 1,
      "slot": "cpu"
    },
    {
      "sku": "RAM-COR-D5-32-6000",
      "quantity": 1,
      "slot": "ram"
    }
  ],
  "contact": {
    "name": "Valentina Aguirre",
    "phone": "+591 71234567",
    "email": "valentina.aguirre@correo.example"
  },
  "notes": "Paso el sabado por la manana",
  "name": "Mi PC gamer"
}
```

```json
{
  "number": "ARM-WEB-000004",
  "status": "Reserved",
  "statusText": "Reservada",
  "createdAt": "2026-09-27T18:39:56.7982846+00:00",
  "reservedUntil": "2026-09-29T18:39:56.7982846+00:00",
  "total": 4698.0,
  "contactName": "Valentina Aguirre",
  "branch": "CM",
  "notes": "Paso el sabado por la manana",
  "hasCompatibilityWarnings": false,
  "lines": [
    {
      "slot": "cpu",
      "sku": "CPU-AMD-7600",
      "name": "Procesador AMD Ryzen 5 7600 (AM5, 6 núcleos, 5,1 GHz, con disipador)",
      "quantity": 1,
      "unitPrice": 2049.0,
      "subtotal": 2049.0
    },
    {
      "slot": "ram",
      "sku": "RAM-COR-D5-32-6000",
      "name": "Memoria Corsair Vengeance RGB DDR5 32 GB (2×16 GB) 6000 MT/s CL30",
      "quantity": 1,
      "unitPrice": 2649.0,
      "subtotal": 2649.0
    }
  ],
  "cancelReason": null,
  "kind": "build",
  "mailQueued": false
}
```

Petición y respuesta reales de un **carrito** (V7): un monitor y dos juegos, sin ranura, 2 días para recogerlo y el CI para
la factura. La respuesta no trae nada de `buyer`.

```http
POST /storefront/v1/reservations
Content-Type: application/json
Idempotency-Key: 9c1e4b7a2f0d4e6b8a3c5d7e9f1a2b3c
```

```json
{
  "kind": "cart",
  "holdDays": 2,
  "lines": [
    {
      "sku": "MON-LG-24GS60F",
      "quantity": 1
    },
    {
      "sku": "JUE-PS5-FC26",
      "quantity": 2
    }
  ],
  "contact": {
    "name": "Valentina Aguirre",
    "phone": "+591 71234567",
    "email": "valentina.aguirre@correo.example"
  },
  "buyer": {
    "documentType": 1,
    "documentNumber": "4567890",
    "complement": "1A",
    "name": "Valentina Aguirre Rojas"
  },
  "notes": "Paso el sabado por la manana"
}
```

```json
{
  "number": "RES-WEB-000001",
  "status": "Reserved",
  "statusText": "Reservada",
  "createdAt": "2026-09-28T20:50:00.6416613+00:00",
  "reservedUntil": "2026-09-30T20:50:00.6416613+00:00",
  "total": 3357,
  "contactName": "Valentina Aguirre",
  "branch": "CM",
  "notes": "Paso el sabado por la manana",
  "hasCompatibilityWarnings": false,
  "lines": [
    {
      "slot": null,
      "sku": "MON-LG-24GS60F",
      "name": "Monitor LG UltraGear 24GS60F-B 24\" Full HD IPS 180 Hz",
      "quantity": 1,
      "unitPrice": 1699,
      "subtotal": 1699
    },
    {
      "slot": null,
      "sku": "JUE-PS5-FC26",
      "name": "EA SPORTS FC 26 (PS5)",
      "quantity": 2,
      "unitPrice": 829,
      "subtotal": 1658
    }
  ],
  "cancelReason": null,
  "kind": "cart",
  "mailQueued": false
}
```

La llave de idempotencia cubre también los campos nuevos: repetir con otro `kind`, otro `holdDays` u otros datos de
`buyer` es «otro contenido» (`422 idempotency`).

Repetida con la misma llave (`200`, `Idempotent-Replayed: true`): el mismo cuerpo. Misma llave con otro contenido:

```json
{
  "type": "https://minv.example/errores/idempotency",
  "title": "idempotency",
  "status": 422,
  "detail": "La llave de idempotencia ya se usó para otra reserva: use una llave nueva.",
  "errors": null,
  "code": null,
  "traceId": "00-63ab5e424f2e32d82ebf155228f2bca8-7bb7ae914fd44d69-00"
}
```

Validación (`400`) y teléfono inválido (`422`):

```json
{
  "type": "https://minv.example/errores/validation",
  "title": "validation",
  "status": 400,
  "detail": "Datos no válidos: Agregue al menos una pieza al armado. · Indique el nombre de quien reserva.",
  "errors": [
    "Agregue al menos una pieza al armado.",
    "Indique el nombre de quien reserva."
  ],
  "code": null,
  "traceId": "00-1d705d99b9ba40c4682f0bf8dadb58af-42c92fea0fdc7276-00"
}
```

```json
{
  "type": "https://minv.example/errores/domain",
  "title": "domain",
  "status": 422,
  "detail": "El teléfono debe tener 7 u 8 dígitos (Bolivia), con o sin +591.",
  "errors": null,
  "code": "pcbuild.contact_phone",
  "traceId": "00-be2b3a85e01568b083c63e2be68647a4-73df4e8d9497ea9c-00"
}
```

V7 · Días fuera de rango o notas con un salto de línea (`400`) y CI con letras (`422`):

```json
{
  "type": "https://minv.example/errores/validation",
  "title": "validation",
  "status": 400,
  "detail": "Datos no válidos: Los días para recoger la reserva van de 1 a 3.",
  "errors": [
    "Los días para recoger la reserva van de 1 a 3."
  ],
  "code": null,
  "traceId": "00-75d333c4e124e5276cacaef40004b388-962ab45c6d57eccd-00"
}
```

```json
{
  "type": "https://minv.example/errores/validation",
  "title": "validation",
  "status": 400,
  "detail": "Datos no válidos: Las notas van en una sola línea: no admite saltos de línea, tabuladores ni otros caracteres de control.",
  "errors": [
    "Las notas van en una sola línea: no admite saltos de línea, tabuladores ni otros caracteres de control."
  ],
  "code": null,
  "traceId": "00-2932019a15b868271a95af6caf85b55a-653c28b534469558-00"
}
```

```json
{
  "type": "https://minv.example/errores/domain",
  "title": "domain",
  "status": 422,
  "detail": "Con CI o NIT el número de documento solo admite dígitos.",
  "errors": null,
  "code": "buyer.doc_numeric",
  "traceId": "00-38a6b7e4c7ba2f797e82f9e0531c5f7e-8a5763fe3c1d3bd2-00"
}
```

### 4.6 `GET /storefront/v1/reservations/{number}?phone=…` · estado de una reserva

Solo con el número **y** el teléfono con que se hizo (con o sin `+591`; en la URL codifique el `+` como `%2B`). Si no
coinciden, `404` (no se revela si el número existe). Sirve igual para un armado (`ARM-WEB-…`) y para un carrito
(`RES-WEB-…`); las reservas hechas en el mostrador (`RES-CM-…`) no se consultan por aquí. Devuelve
`StorefrontReservationView` (con `kind` y, en esta ruta, `mailQueued` siempre `false`):

```json
{
  "number": "ARM-WEB-000004",
  "status": "Reserved",
  "statusText": "Reservada",
  "createdAt": "2026-09-27T18:39:57.237181+00:00",
  "reservedUntil": "2026-09-29T18:39:56.798284+00:00",
  "total": 4698.0,
  "contactName": "Valentina Aguirre",
  "branch": "CM",
  "notes": "Paso el sabado por la manana",
  "hasCompatibilityWarnings": false,
  "lines": [
    {
      "slot": "cpu",
      "sku": "CPU-AMD-7600",
      "name": "Procesador AMD Ryzen 5 7600 (AM5, 6 núcleos, 5,1 GHz, con disipador)",
      "quantity": 1,
      "unitPrice": 2049.0,
      "subtotal": 2049.0
    },
    {
      "slot": "ram",
      "sku": "RAM-COR-D5-32-6000",
      "name": "Memoria Corsair Vengeance RGB DDR5 32 GB (2×16 GB) 6000 MT/s CL30",
      "quantity": 1,
      "unitPrice": 2649.0,
      "subtotal": 2649.0
    }
  ],
  "cancelReason": null,
  "kind": "build",
  "mailQueued": false
}
```

```json
{
  "type": "https://minv.example/errores/not_found",
  "title": "not_found",
  "status": 404,
  "detail": "La reserva ARM-WEB-000004 no existe o el teléfono no coincide.",
  "errors": null,
  "code": null,
  "traceId": "00-0ddff429b50f79d94dc6867f4a17524d-a6eee3d04d570d7a-00"
}
```

### 4.7 `POST /storefront/v1/reservations/{number}/cancel` · el cliente libera su reserva

Cuerpo `{ "phone": "71234567" }`. Pasa la reserva a `Cancelled` (motivo «Cancelada por el cliente desde la tienda web») y
el stock vuelve a estar disponible en el acto. `404` si el teléfono no coincide; `422 pcbuild.state` si ya no estaba
reservada (vendida, cancelada o vencida).

```json
{
  "number": "ARM-WEB-000004",
  "status": "Cancelled",
  "statusText": "Cancelada",
  "createdAt": "2026-09-27T18:39:57.237181+00:00",
  "reservedUntil": "2026-09-29T18:39:56.798284+00:00",
  "total": 4698.0,
  "contactName": "Valentina Aguirre",
  "branch": "CM",
  "notes": "Paso el sabado por la manana",
  "hasCompatibilityWarnings": false,
  "lines": [
    {
      "slot": "cpu",
      "sku": "CPU-AMD-7600",
      "name": "Procesador AMD Ryzen 5 7600 (AM5, 6 núcleos, 5,1 GHz, con disipador)",
      "quantity": 1,
      "unitPrice": 2049.0,
      "subtotal": 2049.0
    },
    {
      "slot": "ram",
      "sku": "RAM-COR-D5-32-6000",
      "name": "Memoria Corsair Vengeance RGB DDR5 32 GB (2×16 GB) 6000 MT/s CL30",
      "quantity": 1,
      "unitPrice": 2649.0,
      "subtotal": 2649.0
    }
  ],
  "cancelReason": "Cancelada por el cliente desde la tienda web",
  "kind": "build",
  "mailQueued": false
}
```

```json
{
  "type": "https://minv.example/errores/domain",
  "title": "domain",
  "status": 422,
  "detail": "El armado ARM-WEB-000004 está anulado: la reserva ya no se puede cancelar.",
  "errors": null,
  "code": "pcbuild.state",
  "traceId": "00-1e27719d72338b2c278efe21c5867ebd-2aebd9e1307e74eb-00"
}
```

## 5. Qué ve el escritorio de todo esto

- Armador de PC › Cotizaciones: la reserva web aparece con canal **Web**, contacto (teléfono y correo solo para quien tiene
  `sales.pcbuild.manage`), «Reservado hasta» y las unidades reservadas; el vendedor la vende en caja (`SellPcBuildCommand`,
  que consume la reserva), la libera (`ReleasePcBuildReservationCommand`) o la deja vencer.
- V7 · Carritos: `GetPcBuildsQuery(Kind: Cart)` los lista aparte de los armados; la fila (`PcBuildRow`) lleva el tipo y, para
  quien tiene `sales.pcbuild.manage`, los datos para la factura (`BuyerDocumentType`, `BuyerDocumentNumber`,
  `BuyerComplement`, `BuyerName`). `SellPcBuildCommand` cobra el carrito a sus precios congelados y, si el cajero no captura
  comprador, factura con esos datos. El personal también reserva un carrito en el mostrador (`ReserveCartCommand`,
  `RES-<sucursal>-000001`).
- Stock, catálogo y caja: disponible = existencias − reservado. La web y el escritorio ven el MISMO número.
- Webhooks B2B: `pcbuild.reserved`, `pcbuild.released` y `pcbuild.sold` (`docs/integration/api-gateway-v1.md` §7.2). V7: llevan
  `kind` (`Build` o `Cart`) y la línea sin ranura de un carrito lleva `slot: null`.

## 6. Recorrido de referencia (regla S-10)

1. `GET /catalog` → el gabinete `CASE-COR-4000D` tiene `available` = *n*.
2. `POST /reservations` con ese gabinete → `201`, `ARM-WEB-000003`, `reservedUntil` = +48 h; `GET /products/case-cor-4000d`
   → `available` = *n − 1*, `reserved` = +1, `onHand` igual.
3. En el escritorio, `GetPcBuildsQuery(Status: Reserved, Channel: Web)` la lista; el cajero la cobra con
   `SellPcBuildCommand("ARM-WEB-000003", "EFECTIVO", …)`.
4. `GET /reservations/ARM-WEB-000003?phone=71234567` → `status` = `Sold`; `GET /products/case-cor-4000d` → `onHand` = *n − 1*,
   `reserved` volvió a lo de antes: la unidad se descontó UNA sola vez.

Las pruebas `tests/MINV.Integration.Tests/StorefrontApiTests.cs` recorren exactamente esto sobre el gateway real.

Con un carrito (V7) el recorrido es el mismo: `POST /reservations` con `"kind": "cart"` y los productos sin ranura →
`201`, `RES-WEB-000001`, `reservedUntil` según `holdDays`; el cajero lo cobra con `SellPcBuildCommand("RES-WEB-000001", …)`;
la consulta devuelve `status` = `Sold` y `kind` = `cart`. Lo recorren `tests/MINV.Integration.Tests/StorefrontCartApiTests.cs`
(gateway real) y `tests/MINV.Infrastructure.Tests/CartFlowTests.cs` (casos de uso en memoria, con la factura).

## 7. Qué cambió en la V7 (compatible con la V6)

| Dónde | Cambio |
|---|---|
| Petición de `POST /reservations` | Campos opcionales nuevos: `kind`, `holdDays`, `buyer { documentType, documentNumber, complement, name }`. Sin ellos, todo sigue como en la V6 (armado, 48 h, sin datos de factura). |
| Respuesta de reservas | Campos nuevos `kind` y `mailQueued`; `lines[].slot` puede ser `null` (solo en carritos). |
| Catálogo | Campos nuevos `reservationHours` y `maxHoldDays`. |
| Numeración | Carritos `RES-WEB-000001`; los armados conservan `ARM-WEB-000001`. |
| Validación | Nombre de contacto, notas, nombre de la reserva y razón social: una sola línea, sin caracteres de control. Es el ÚNICO cambio que puede rechazar una petición que la V6 aceptaba (unas notas con saltos de línea). |
| Configuración | `Minv:Storefront:MaxReservationHours` (72): tope de cualquier reserva de la tienda. |
| Idempotencia | El contenido incluye los campos nuevos; una petición sin ellos conserva el mismo contenido que en la V6. |
