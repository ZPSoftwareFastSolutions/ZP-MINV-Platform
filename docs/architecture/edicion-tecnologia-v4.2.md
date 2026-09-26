# M-INV V4.2 · Edición Tecnología (PC, componentes, periféricos y consolas)

> Rama `Inventario-V4.2` (desde `Inventario-V4.1`). Versión **4.2.0-alpha.1**. Mismo motor (CQRS y append-only,
> multi-sucursal, nube, API y facturación SIAT) **especializado para tiendas de tecnología y gaming**: componentes de PC,
> computadoras, monitores, periféricos, consolas (PS4 y PS5, Xbox Series X y Series S, Nintendo Switch y Switch 2),
> videojuegos, accesorios, redes, cables y software. Estilo visual gaming (oscuro con acentos neón) y empresa de prueba
> del rubro, **Tech Zone Gaming S.R.L.**
>
> Reglas normativas T-01 a T-10: [`.claude/v42-tech-rules.md`](../../.claude/v42-tech-rules.md) · tablas y restricciones:
> [`docs/database/ERD-MINV-V3.md`](../database/ERD-MINV-V3.md) §9 · migración `V42TechRetail`:
> [`.claude/database-migration-guide.md`](../../.claude/database-migration-guide.md) §9 · interfaz:
> [`docs/product/escritorio-v4.2.md`](../product/escritorio-v4.2.md) · paso a paso para probarla:
> [`docs/deployment/inicio-rapido-v4.2.md`](../deployment/inicio-rapido-v4.2.md).

## 0. Principio: datos, no código a medida

La V4.2 no escribe una tienda de tecnología «a mano» dentro del programa: las **fichas técnicas**, las **plataformas**, la
**condición** del equipo y las **reglas de compatibilidad** del armador salen de configuración (especificaciones por
categoría, con una clave de compatibilidad cuando el armador las necesita). El motor es el mismo de la V4.1: el stock
cambia solo con movimientos inmutables, cada sucursal ve lo suyo, todo pasa por la tubería de MediatR (validación →
permisos y módulo → alcance por sucursal → caso de uso → auditoría) y cada venta se factura al SIN.

Lo nuevo de verdad es la **unidad serializada**: cada equipo con número de serie o IMEI es un hecho trazable de punta a
punta. Entra con su serie, viaja con su serie, se vende con su serie (la factura del SIN la lleva) y vuelve por garantía
con su serie. La serie es **trazabilidad**: no reemplaza al kardex (las existencias siguen saliendo de los movimientos) y
en cada sucursal las series en stock deben coincidir con el stock (`inventory.v_serial_breaches` vacía).

## 1. Qué necesita una tienda de tecnología y cómo lo resuelve la V4.2

| Necesidad del rubro | Solución V4.2 |
|---|---|
| Cada equipo tiene **número de serie o IMEI** (garantía, robo, devoluciones, reclamos al proveedor) | Productos serializados (`catalog.products.tracking_mode = 'Serial'`): una serie por unidad en la recepción de compras, el saldo inicial, los ajustes, la venta (escaneo en la caja), las transferencias y las devoluciones; bitácora de cada serie; la factura del SIN lleva `numeroSerie` o `numeroImei` por línea (§7) |
| **Garantía** por producto (3, 6, 12, 24, 36 o 60 meses) y **RMA** con el proveedor | Meses de garantía en el perfil técnico del producto; garantía vigente **derivada** al consultar (§5); casos RMA con estados y bitácora; reposición con otra unidad como salida real de stock con su asiento (§5) |
| **Fichas técnicas** (socket, VRAM, DDR5, Hz, plataforma…) y búsqueda por especificación | Especificaciones tipadas por categoría (texto, número con unidad, opción, multivalor), heredadas por las subcategorías; facetas y filtros en el catálogo, el stock y la caja (§3) |
| **Armado de PC** con compatibilidad y cotización | Armador de PC por ranuras con las reglas de `PcCompatibility` (socket, RAM, ranuras, formato, largo de GPU, fuente, M.2, refrigeración), consumo estimado, fuente recomendada y cotización con precios congelados y vigencia que se cobra en la caja (§6) |
| **Plataformas** de consolas, juegos y accesorios | Especificación de tipo opción «Plataforma» (PS4, PS5, Xbox Series X, Xbox Series S, Xbox One, Nintendo Switch, Nintendo Switch 2, PC) con chips en la caja y en el catálogo (regla T-07) |
| **Condición** del equipo | Especificación «Condición» (Nuevo, Reacondicionado, Usado) |
| **Facturación** del rubro | Actividades del SIN 4741100 (computadoras, periféricos y programas), 4741200 (consolas de videojuegos y sus juegos) y 4742100 (monitores); cada producto del catálogo de prueba viene homologado con su producto SIN (1001966 ordenadores de escritorio, 1001967 portátiles, 1001971 impresoras y monitores, 1001972 teclados, 1001973 ratones y palancas de mando, 1001969 almacenamiento, 1003590 consolas y juegos, 1003591 monitores, 1001981 ensamblado de computadores, 1003248 instalación y reparación, 1003587 programas informáticos…) |
| **Servicios** de la tienda (ensamblado, instalación de Windows, mantenimiento) | Unidad `SERV` (homologada a «UNIDAD (SERVICIOS)»); ver la decisión D-04 (§11) |

## 2. Modelo de datos nuevo (5FN) · migración `V42TechRetail`

La migración `20260926082719_V42TechRetail` lleva la base de **140 tablas en 9 esquemas** (V4.1) a **152 tablas en 10
esquemas**: 12 tablas nuevas y el esquema nuevo **`service`**.

| Tabla | Qué guarda | Tipo |
|---|---|---|
| `catalog.spec_definitions` | Especificación de una categoría: código, nombre, unidad, tipo (`Text`, `Number`, `Option`), multivalor (solo opciones), filtrable, obligatoria, clave de compatibilidad (constante de `CompatibilityKeys`) y orden. La heredan las subcategorías | catálogo (de la empresa) |
| `catalog.spec_options` | Opciones de una especificación de tipo opción (AM5, LGA1700, DDR5, ATX, PS5, Nuevo…); única por (especificación, valor) | catálogo |
| `catalog.product_spec_values` | Valor de una especificación para un producto: número, texto u opción (arco exclusivo); las multivalor tienen una fila por opción. La opción es de ESA especificación (FK compuesta) | catálogo |
| `catalog.product_tech_profiles` | Subtipo 1:1 del producto: tipo de identificador por unidad (`Serial` o `Imei`) y meses de garantía (0 a 120). Que el producto lleve serie lo dice `products.tracking_mode = 'Serial'` | catálogo |
| `inventory.serial_events` | Bitácora de cada serie o IMEI (15 acciones: ingreso, venta, devolución, despacho y recepción de transferencia, RMA, envío al proveedor, reparada, reemplazada, reposición entregada, devuelta al proveedor, baja, ajuste, reingreso al stock, entregada al cliente) con sucursal, documento y usuario | **append-only** |
| `inventory.stock_transfer_line_serials` | Series que viajan en cada línea de transferencia (las ven origen y destino) | **append-only**, **entre sucursales** |
| `sales.sales_order_line_serials` | Series vendidas en cada línea de venta (N:M línea ↔ serie): van al ticket, a la factura y a la garantía | **append-only**, **por sucursal** |
| `sales.sales_return_line_serials` | Series devueltas en cada línea de devolución | **append-only**, **por sucursal** |
| `sales.pc_builds` | Armado o cotización `ARM-<sucursal>-000001`: nombre, cliente, estado (`Draft`, `Quoted`, `Sold`, `Cancelled`), vigencia, fecha de cotización, marca de «cotizado con errores de compatibilidad confirmados» y la venta que lo cobró. El total no se guarda | **por sucursal**, OCC `xmin` |
| `sales.pc_build_lines` | Pieza por ranura (12 ranuras) con variante, cantidad (1 a 16) y **precio cotizado** | **por sucursal** |
| `service.warranty_claims` | Caso RMA `RMA-<sucursal>-000001`: serie, cliente, venta original (de la misma sucursal), falla, en garantía o con cargo, estado, proveedor, resolución, serie de reemplazo; un solo caso abierto por serie | **por sucursal**, OCC `xmin` |
| `service.warranty_claim_events` | Bitácora del caso (apertura, cambio de estado, nota, reposición entregada, cierre) con el estado resultante y el usuario | **append-only**, **por sucursal** |

Además, `inventory.serial_numbers` (existente desde la V3) gana **variante**, **tipo** (`Serial` o `Imei`) y **fecha de
ingreso**; la serie pasa a ser única por **(empresa, variante, serie)** y solo las series en stock (o reservadas, estado de
la V3) ocupan una existencia. Nuevos estados: `InTransit`, `InRma` y `ReturnedToSupplier`.

La parte propia de PostgreSQL (`V42TechRetail.Sql.cs`, regla B-15):

1. **Guardia** antes de cualquier cambio: las series existentes deben poder cumplir las reglas nuevas (una serie por
   variante, sin espacios ni separadores, existencia solo si está en stock).
2. **Relleno** de `serial_numbers.variant_id` desde su lote (y `kind = 'Serial'`, `received_at` = alta de la fila) con
   verificación de que ninguna fila quedó sin valor.
3. **Append-only** (`trg_append_only` y privilegios revocados) en las 5 bitácoras nuevas: **29 libros inmutables** en total.
4. **RLS**: `tenant_isolation` en las 12 tablas nuevas (**150** tablas con empresa) y `branch_isolation` RESTRICTIVA en
   las 6 tablas de sucursal y en la de entre sucursales (**62** en total).
5. Trigger **`catalog.minv_spec_value_matches`**: el valor usa la columna del tipo de su especificación y una
   especificación de un solo valor tiene una sola fila por producto (regla T-01).
6. Vista de control **`inventory.v_serial_breaches`** (`security_invoker`): por sucursal y variante serializada, las
   series en stock que no coinciden con el stock. Debe estar **vacía**; `minv verify --codigo <empresa>` la revisa.
7. **Datos** para las empresas que ya existían (las nuevas los reciben del aprovisionamiento): los 6 permisos y su matriz
   por rol (§9), el tipo de movimiento `REPOSICION_GARANTIA` («REPOSICIÓN POR GARANTÍA») y la cuenta **5.1.10 Costo de
   garantías**.
8. Privilegios de `minv_app` y `minv_server` sobre las tablas nuevas (solo si esos roles existen).

## 3. Fichas técnicas, plataformas y facetas (reglas T-01 y T-07)

- Una especificación pertenece a una **categoría** y la heredan sus **subcategorías** (`GetSpecDefinitionsQuery` devuelve
  las propias y las heredadas con `IsInherited`). Un código no se repite entre una madre y sus hijas; el tipo de una
  especificación no cambia; una opción en uso no se puede quitar.
- Tipos: **texto**, **número** con unidad (núcleos, GB, W, mm, Hz…) y **opción** (lista cerrada), que puede ser
  **multivalor** (por ejemplo, los formatos de placa que admite un gabinete o los sockets de un enfriador).
- Solo las especificaciones **filtrables** alimentan las facetas (`GetSpecFacetsQuery`) y los filtros del catálogo
  (`GetCatalogQuery(SpecFilters, CategoryCode)`, `SearchTechProductsQuery`). Una categoría madre muestra también los
  productos de sus subcategorías.
- Las que usa el armador llevan una **clave de compatibilidad**, siempre una constante de `CompatibilityKeys` (17 claves:
  `cpu_socket`, `ram_type`, `ram_slots`, `ram_max_gb`, `form_factor`, `case_form_factors`, `gpu_length_mm`,
  `case_max_gpu_mm`, `power_draw_w`, `cpu_tdp_w`, `psu_watts`, `m2_slots`, `cooler_sockets`, `igpu`,
  `storage_interface`, `ram_modules`, `ram_capacity_gb`). Así las reglas no dependen del nombre visible.
- Una ficha técnica **no duplica** lo que ya está en otras tablas (marca, precio, unidad, stock).
- **Plataforma** y **Condición** son especificaciones de tipo opción: los chips de la caja y del catálogo salen de sus
  opciones, no de listas escritas en las vistas.
- El perfil técnico (`SaveProductTechCommand`) fija si el producto **lleva serie o IMEI**, el tipo y los **meses de
  garantía**. Pasar a «lleva serie» un producto con existencias exige que cada unidad en stock ya tenga su serie
  (**Series e IMEI › Registrar series de stock**, `RegisterStockSerialsCommand`) y alcance sobre todas las sucursales.

## 4. Ciclo de vida de una serie (regla T-02)

```text
                  recepción de compra · saldo inicial · ajuste (+) · registrar series de stock
   (serie nueva) ───────────────────────────────────────────────────────────────┐
                                                                                ▼
                  despacho de transferencia              ┌────────────── EN STOCK (InStock) ◄──────── reingreso (ajuste + o
          ┌──────────────────────────────────────────────┤   en una existencia de una sucursal          recepción) de una unidad
          ▼                                              │                                              devuelta, en RMA sin caso
   EN TRÁNSITO (InTransit) ── recepción en el destino ──►┘                                              abierto o del proveedor
          │ faltante («no llegó»)                        │ venta (caja, API, armado, CAFC)                ▲
          ▼                                              │ o reposición por garantía                      │
   DADA DE BAJA (Scrapped)                               ▼                                                │
          ▲                                       VENDIDA (Sold) ── devolución normal (vuelve a la existencia de la venta) ─┤
          │ ajuste (−) desde el stock                    │                                                │
          │ o «Dar destino › baja»                       │ devolución por falla (sin reingreso)           │
          │                                              ▼                                                │
          │                                   DEVUELTA (Returned) ──┐                                     │
          │                                              │          │ abrir un caso RMA                   │
          │                                              ▼          ▼                                     │
          └──────────────────────────────────── EN GARANTÍA (InRma) ── al proveedor / reparada / reemplazada (sigue InRma)
                                                         │                     │
                         entrega al cerrar el RMA ───────┘                     │ «Dar destino › al proveedor»
                         (reparada o rechazada): vuelve a VENDIDA              ▼ o el proveedor se queda con ella
                                                                DEVUELTA AL PROVEEDOR (ReturnedToSupplier)
```

| Método de `SerialNumber` | Desde | Hacia | Fila de `serial_events` | Quién lo dispara |
|---|---|---|---|---|
| `Receive` | (nueva) | `InStock` | `Received` | recepción de compra, saldo inicial, ajuste positivo, registrar series de stock |
| `Sell` | `InStock` / `Reserved` | `Sold` | `Sold` | caja, pedido del API, venta de un armado, factura manual CAFC transcrita |
| `IssueAsReplacement` | `InStock` | `Sold` | `ReplacementIssued` | reposición por garantía (`IssueWarrantyReplacementCommand`) |
| `Return` | `Sold` | `InStock` (normal) o `Returned` (por falla) | `Returned` | devolución de la venta, anulación con devolución de mercadería |
| `SendToRma` | `Sold` / `Returned` | `InRma` | `RmaReceived` | abrir un caso RMA; devolución por falla |
| `SendToSupplier` · `MarkRepaired` · `MarkReplaced` | `InRma` | `InRma` | `SentToSupplier` · `Repaired` · `Replaced` | pasos del caso RMA |
| `ReturnFromRma` | `InRma` | `Sold` | `ReturnedToCustomer` | entregar un caso reparado o rechazado |
| `Restock` | `Returned` / `InRma` / `ReturnedToSupplier` | `InStock` | `Restocked` | ajuste positivo o recepción de una unidad que vuelve (sin caso abierto) |
| `TransferOut` · `TransferIn` | `InStock` → `InTransit` → `InStock` | | `TransferDispatched` · `TransferReceived` | despacho y recepción de una transferencia |
| `ReturnToSupplier` | `InStock` / `Reserved` / `Returned` / `InRma` | `ReturnedToSupplier` | `ReturnedToSupplier` | devolución a proveedor, «Dar destino», reemplazo hecho por el proveedor |
| `Scrap` | `InStock` / `Reserved` / `InTransit` / `Returned` / `InRma` | `Scrapped` | `Scrapped` | ajuste negativo, faltante de una transferencia, «Dar destino › baja» |

Reglas que valida el dominio (código estable entre paréntesis, `SerialErrorCodes`): la línea de un producto serializado
tiene cantidad **entera** (`serial.quantity`) y **exactamente** una serie por unidad (`serial.required`, `serial.count`),
sin repetidas (`serial.duplicate`); la serie existe (`serial.not_found`), está disponible (`serial.not_available`), en la
sucursal (`serial.wrong_branch`) y en el almacén o la posición esperados (`serial.wrong_location`); un IMEI tiene 15
dígitos con dígito de Luhn (`serial.imei_invalid`); no se piden series a un producto sin control (`serial.not_tracked`);
la serie es del documento (`serial.not_in_document`), no es ambigua entre variantes (`serial.ambiguous`), no tiene un caso
abierto (`serial.in_claim`) y la venta, la devolución, la transferencia y la reposición usan su propio documento
(`serial.use_document`). La toma física de un producto serializado con diferencia se rechaza (`serial.count_adjustment`):
la diferencia se registra con un ajuste que lleva sus series.

Una serie `Sold`, `Returned`, `InTransit`, `InRma`, `ReturnedToSupplier` o `Scrapped` **no** es stock vendible. La
unicidad y los estados también los garantiza PostgreSQL (`CHECK` de estados y tipos, IMEI de 15 dígitos, existencia solo
en stock) y la concurrencia optimista (`xmin`): dos cajas no pueden vender la misma unidad.

## 5. Garantía y RMA (reglas T-04 y T-05)

**Garantía derivada.** La garantía vigente de una unidad es la **fecha de su última venta + los meses de garantía del
producto**, calculada al consultar (`GetWarrantyStatusQuery`, `SerialLedger.SalesAsync`). No se guarda ninguna fecha de
vencimiento. La unidad entregada como **reposición** cuenta su garantía desde la **fecha de la reposición** (su último hecho
«vendida» es `ReplacementIssued`). El ticket, el rollo fiscal y el PDF de la factura imprimen «Garantía hasta dd/mm/aaaa»
con la fecha de la **venta** (no la de la emisión fiscal).

**Casos RMA** (`WarrantyClaim`, `RMA-<sucursal>-000001`, de la sucursal donde se abre):

```text
   Recibido ──► En diagnóstico ──► En el proveedor ──┬──► Reparado ────┐
      │               │                              ├──► Reemplazado ─┼──► Entregado (cierre)
      │               ├──► Reparado / Reemplazado ───┘                 │
      └───────────────┴──► Rechazado (con la resolución) ──────────────┘
```

- **Abrir** (`OpenWarrantyClaimCommand`): la serie pasa a `InRma`, sin entrada al stock vendible. Fuera de garantía el caso
  solo se abre como **reparación con cargo** (`ChargeableRepair`), marcado así. Un solo caso abierto por serie.
- **Avanzar** (`MoveWarrantyClaimCommand`) solo por la tabla de transiciones del agregado; reparado, reemplazado y
  rechazado exigen una **resolución**; «en el proveedor» exige el proveedor. Cada paso deja su fila en
  `warranty_claim_events` y el hecho correspondiente en la bitácora de la serie. **Notas** con `AddWarrantyClaimNoteCommand`.
- **Reponer con otra unidad** (`IssueWarrantyReplacementCommand`, desde diagnóstico o proveedor, una sola vez): la unidad
  nueva sale del stock con un movimiento real **REPOSICIÓN POR GARANTÍA** (su serie, su costo) y su asiento **Debe 5.1.10
  Costo de garantías / Haber 1.1.05 Inventario** al costo promedio. La defectuosa sigue en `InRma` para devolverla al
  proveedor o darla de baja (**Dar destino**, `DisposeSerialCommand`). Si el reemplazo lo hizo el proveedor (el caso estaba
  «en el proveedor»), la defectuosa queda devuelta al proveedor. Lo que el proveedor devuelva o acredite entra como un hecho
  nuevo (recepción o ajuste), nunca editando lo registrado.
- **Entregar** cierra el caso: una unidad reparada o con la garantía rechazada vuelve a su dueño (`Sold`).
- **Devolución por falla** (`CreateSalesReturnCommand(…, Defective: true)`): se reembolsa al cliente y se emite la nota
  crédito-débito, pero la mercadería **no vuelve al stock vendible** (sin movimiento; el costo sigue en el costo de ventas
  hasta que el proveedor lo reconozca): las series quedan devueltas y en garantía, listas para **Dar destino**.

## 6. Armador de PC (regla T-06)

**Ranuras** (`PcSlot`): procesador, placa madre, memoria RAM, tarjeta de video, almacenamiento, fuente de poder, gabinete
y refrigeración, más los **extras** monitor, periférico, software y servicio. Obligatorias: procesador, placa, RAM,
almacenamiento, fuente y gabinete. La ranura de un candidato se reconoce por su clave de compatibilidad (los extras, por
categoría) y los candidatos incompatibles se muestran atenuados con el motivo (`GetPcBuildCandidatesQuery`: solo cuentan
los errores **nuevos** que agregaría esa pieza).

**Reglas** (dominio puro `PcCompatibility.Check`, las mismas del generador del catálogo de prueba):

| Código | Regla | Tipo |
|---|---|---|
| `SOCKET_CPU_PLACA` | Socket del procesador = socket de la placa | error |
| `TIPO_RAM` | Tipo de RAM (DDR4/DDR5) = el de la placa | error |
| `RANURAS_RAM` | Módulos de RAM ≤ ranuras de la placa | error |
| `CAPACIDAD_RAM` | Capacidad total ≤ máxima de la placa | error |
| `RANURAS_M2` | Unidades NVMe ≤ ranuras M.2 de la placa | error |
| `FORMATO_GABINETE` | Formato de la placa (ATX, Micro-ATX, Mini-ITX) ∈ formatos que admite el gabinete | error |
| `LARGO_GPU` | Largo de la tarjeta de video ≤ largo máximo del gabinete | error |
| `SOCKET_REFRIGERACION` | El enfriador admite el socket del procesador (multivalor) | error |
| `POTENCIA_FUENTE` | Fuente ≥ consumo estimado (TDP del procesador + consumo de la GPU + 75 W del resto) | error |
| `POTENCIA_RECOMENDADA` | Fuente ≥ consumo × 1,3 | aviso |
| `SIN_GRAFICOS` | Procesador sin gráficos integrados y sin tarjeta de video | aviso |
| `PIEZA_FALTANTE` | Falta una pieza obligatoria | aviso |

**Estados de un armado** (`PcBuild`): **Borrador** (se guarda sin congelar precios) → **Cotizado** (congela el precio de
cada línea y la vigencia: 3, 7, 15 o 30 días en la pantalla) → **Vendido** (con la venta que lo cobró) o **Anulado**. Un
cotizado cuya vigencia pasó se muestra **Vencido** (derivado de la fecha, no es un estado guardado). Un armado con errores
de compatibilidad **no se cotiza** sin la confirmación explícita del usuario (`AcceptIncompatible`) y queda marcado
(`quoted_with_errors`). El precio cotizado por línea es una **redundancia comercial documentada**: es la oferta hecha al
cliente y vale mientras la cotización esté vigente aunque cambie la lista de precios.

**Cobro** (`SellPcBuildCommand`, permisos `sales.pos.operate` y `inventory.movements.register.sales`): la cotización
vigente pasa a la caja con una línea fija por pieza a su precio cotizado; se eligen las series de las piezas serializadas y
se cobra con los casos de uso normales de venta (poka-yoke, series, factura del SIN). La venta y el paso del armado a
**Vendido** ocurren en la **misma transacción**. La **proforma** (texto de ticket, rollo o PDF) lleva cada pieza con su
precio cotizado y su garantía en meses, y la leyenda «Documento sin valor fiscal: no es una factura».

## 7. Series en la factura del SIN (regla T-03)

- La **factura Compra Venta** (sector 1) lleva en cada línea de un producto serializado `numeroSerie` (número de serie) o
  `numeroImei` (IMEI) con las series vendidas en esa línea, unidas con «, ».
- Si no caben en los **1500 caracteres** del XSD, la línea se **divide por unidades** con el mismo precio unitario y el
  descuento prorrateado (el último tramo lleva el resto): la suma de los subtotales es el importe de la línea y el total
  fiscal no cambia (F-06).
- La **nota crédito-débito** (sector 24) guarda las series devueltas en sus líneas «devuelto» **dentro de M-INV**, pero su
  XSD **no tiene** `numeroSerie`: las series no viajan en el XML de la nota (decisión D-06).
- Nada de esto cambia los montos ni el CUF; el XML se sigue validando contra el XSD antes de guardarse (F-05).

## 8. Tablero · sección Tecnología

`GetTechDashboardQuery` (permiso `reports.view`) lee el **modelo de escritura**, acotado a los últimos **30 días** y a las
sucursales visibles de la sesión: unidades con serie en stock (por categoría), casos RMA abiertos (y cuántos fuera de
garantía), armados cotizados vigentes con su valor por cobrar, armados vendidos, **ventas por categoría** (categorías
principales con sus subcategorías) y **por plataforma** (según la especificación «Plataforma»), casos RMA por estado,
cotizado frente a vendido, **tarjetas de video** y **consolas** más vendidas, y un aviso si hay existencias de productos
serializados sin todas sus series. Se lee del modelo de escritura porque son pocas filas por empresa y ventana; si el
volumen crece, es candidato a una vista del modelo de lectura (regla B-14).

## 9. Casos de uso, permisos y roles

Todo pasa por la tubería de MediatR y los `IRequest` públicos se exponen solos por RPC en el servidor en la nube.
Reintento optimista ×3 donde se tocan existencias, números de documento o series (B-07).

| Tema | Contratos (`MINV.Application/Tech/TechContracts.cs` y los de siempre) |
|---|---|
| Fichas técnicas (T-01) | `GetSpecDefinitionsQuery`, `SaveSpecDefinitionCommand`, `GetProductTechQuery`, `SaveProductTechCommand`, `SearchTechProductsQuery`, `GetSpecFacetsQuery`, `GetCatalogQuery(SpecFilters, CategoryCode)`; `SaveCategoryCommand(…, ParentCode)` crea subcategorías |
| Series en las operaciones (T-02) | parámetro `Serials` en `SaleLineInput` (caja, API, CAFC), `ReturnLineInput`, `TransferLineInput` y `RegisterMovementCommand`; `ReceivePurchaseOrderCommand(…, Serials: SkuSerials[])`; `TransferReceiptInput(…, MissingSerials)`; `CreateSalesReturnCommand(…, Defective)`; `GetSaleLinesQuery` devuelve las series de cada línea |
| Series e IMEI | `GetAvailableSerialsQuery`, `SearchSerialsQuery`, `GetSerialTraceQuery`, `GetWarrantyStatusQuery`, `RegisterStockSerialsCommand`, `DisposeSerialCommand` |
| Garantías y RMA (T-04, T-05) | `OpenWarrantyClaimCommand`, `MoveWarrantyClaimCommand`, `AddWarrantyClaimNoteCommand`, `IssueWarrantyReplacementCommand`, `GetWarrantyClaimsQuery`, `GetWarrantyClaimQuery` |
| Armador de PC (T-06) | `CheckPcBuildQuery`, `GetPcBuildCandidatesQuery`, `SavePcBuildCommand(…, Quote, ValidDays, AcceptIncompatible)`, `GetPcBuildsQuery`, `GetPcBuildQuery`, `CancelPcBuildCommand`, `SellPcBuildCommand` |
| Tablero | `GetTechDashboardQuery` |
| API Gateway | `GET /v1/products/{sku}/specs` (alcance `catalog:read`); los pedidos (`POST /v1/orders`) aceptan `serials` por línea |

**Permisos nuevos** (`PermissionCodes`, copiados en la migración):

| Permiso | Qué permite |
|---|---|
| `catalog.specs.manage` | Especificaciones por categoría y ficha técnica de cada producto (valores, garantía, control por serie o IMEI) |
| `inventory.serials.view` | Consultar series e IMEI, su trazabilidad, la garantía de una unidad y los casos RMA |
| `inventory.serials.manage` | Registrar series de unidades ya en stock y dar destino a unidades serializadas (proveedor o baja) |
| `service.rma.open` | Abrir casos de garantía al recibir un equipo y agregarles notas |
| `service.rma.manage` | Diagnosticar, enviar al proveedor, reparar, reponer con otra unidad, rechazar y entregar |
| `sales.pcbuild.manage` | Armar, cotizar y anular armados de PC |

**Matriz por rol** (`PermissionCodes.ForRole`; las pantallas se muestran según estos permisos y la tubería los vuelve a
comprobar en cada caso de uso):

| Rol | Fichas técnicas | Series e IMEI | Garantías y RMA | Armador de PC |
|---|---|---|---|---|
| Administrador | sí | consulta, registra y da destino | abre y avanza | arma, cotiza, anula y cobra |
| Gerencia | sí | consulta, registra y da destino | abre y avanza | arma, cotiza y anula (no tiene caja: no cobra) |
| Bodega | sí | consulta, registra y da destino | abre y avanza | — |
| Ventas | — | consulta | abre y agrega notas | arma, cotiza, anula y cobra |
| Cajero | — | consulta | abre y agrega notas | arma, cotiza, anula y cobra |
| Consulta | — | consulta | consulta | — |

Otras operaciones con series siguen los permisos de la V4 y la V4.1: recibir compras exige `purchasing.manage` e
`inventory.movements.register.warehouse` (Bodega y Administrador); transferencias, `inventory.transfers.manage` (Bodega,
Gerencia y Administrador); **devoluciones** (normal o por falla, con su nota crédito-débito) exigen `sales.pos.operate` y
`billing.void` a la vez: con los roles predeterminados, solo el **Administrador**.

## 10. Interfaz y tema

- Sección nueva del menú **Tecnología**: **Armador de PC**, **Series e IMEI** y **Garantías y RMA**. **Catálogo** con la
  pestaña **Ficha técnica**, insignias **Serie** / **IMEI** y **Garantía**, chips de plataforma, facetas y administración de
  **Especificaciones**; **Punto de venta** con chips de categoría y plataforma, elección de la unidad por serie o IMEI y
  **Desde armado**; formulario de series en **Compras**, **Registrar movimiento**, **Transferencias** y **Devoluciones**.
  Guía de cada pantalla: [`docs/product/escritorio-v4.2.md`](../product/escritorio-v4.2.md).
- **Tema gaming** (regla T-08): oscuro por defecto y claro a un clic, con las mismas claves en `Theme/Palette.Dark.xaml` y
  `Theme/Palette.Light.xaml` y contraste WCAG AA; degradado de marca violeta → cian, foco del teclado con anillo neón,
  tarjetas de indicador con acento, insignias `Badge` y `PlatformBadge`, títulos y cifras en Bahnschrift, ícono y logotipo
  propios y textos de marca en `Services/Brand.cs`. Detalle: [`docs/product/ux-ui-guidelines.md`](../product/ux-ui-guidelines.md) §13.

## 11. Decisiones y límites conocidos

| # | Decisión o límite | Por qué |
|---|---|---|
| D-01 | La serie es **trazabilidad**, no inventario: las existencias siguen saliendo de los movimientos y `v_serial_breaches` vigila que coincidan | Conserva el kardex, la conservación (`v_conservation_breaches`) y la contabilidad de la V4.1 sin una segunda fuente de verdad |
| D-02 | Garantía **derivada** (venta + meses); la de una reposición cuenta desde la fecha de la reposición | Cambiar los meses de un producto no deja fechas guardadas desactualizadas (T-04); el cliente que recibe una unidad nueva tiene la garantía completa |
| D-03 | Precio **congelado** en `pc_build_lines` | Es la oferta hecha al cliente: redundancia comercial documentada (A-06), no un derivado |
| D-04 | Los **servicios** (unidad `SERV`: ensamblado, instalación de Windows, mantenimiento) llevan un **cupo** de existencias (90 en la casa matriz y al menos 10 por sucursal en los datos de prueba) | El dominio no tiene artículos sin stock: toda venta descuenta existencias; el cupo se repone como cualquier producto |
| D-05 | **Demostración = Tech Zone Gaming** generada en memoria con el MISMO `LocalDataSeeder` (8 días, 6 facturados, 40 % del volumen, sin los escenarios de contingencia ni el pedido sugerido completo; saldo inicial de la casa matriz del 60 al 105 % del máximo en vez del 90 al 160 % de la base local, y primera distribución de unos 90 productos con pocas unidades por sucursal en vez de 70 con el 35 % del máximo), no con el libro de la V2.1 | Una sola empresa de prueba para todo; T-09 prevalece sobre lo que A-12 decía de la V2.1 (el importador sigue en `minv import-v21`). Abre en unos **17 s en frío**; con menos ventas, el saldo más bajo evita que el tablero arranque en sobrestock. Límite: en la demostración, Cochabamba y Santa Cruz tienen unos 60 a 70 productos agotados (en memoria cada línea de transferencia cuesta unos 25 ms: abastecerlas por completo llevaba la apertura a más de 20 s) |
| D-06 | La **nota crédito-débito** no lleva `numeroSerie` en el XML | El XSD del sector 24 no tiene el campo; las series devueltas quedan en las líneas del documento en M-INV y en `sales_return_line_serials` |
| D-07 | **Excepción parcial a A-13** en los datos de prueba: marcas, modelos, la unidad `SERV`, las categorías de cliente, las cajas extra y la topología del almacén (zonas, pasillos, estanterías y posiciones) se escriben directo en el contexto de la sesión del administrador | No tienen casos de uso todavía (como el aprovisionamiento); todo lo demás (categorías, especificaciones, productos, fichas, imágenes, movimientos, ventas, RMA, armados, facturas) pasa por la tubería completa. Está documentado en `LocalDataSeeder.SeedCatalogAsync` |
| D-08 | Arranque en frío: `TieredPGO=false` en el escritorio, la CLI y las pruebas, y `TieredCompilation.CallCountingDelayMs=0` en todos (`Directory.Build.props`) | La demostración y la CLI son procesos cortos: sin la instrumentación de la PGO dinámica y sin la espera de 100 ms, los métodos se optimizan antes (~25 % menos de tiempo en la demostración). Los servidores conservan la PGO (corren por días) |
| D-09 | El **simulador del SIN** tiene un padrón de actividades por NIT (`SiatSimulatorCatalogs.Rows(catálogo, nit)`): el NIT de Tech Zone Gaming (1023456029) solo sincroniza sus tres actividades de tecnología (4741100, 4741200 y 4742100) con sus productos SIN; cualquier otro NIT conserva además las dos de ferretería de la V4.1 | Así la empresa de prueba solo ve productos SIN de su rubro, sin romper las pruebas de homologación de la V4.1 (NIT del ejemplo oficial). Son datos de simulación: en producción manda lo que devuelve el SIN (F-16) |
| D-10 | **IVA boliviano sobre el importe facturado** (`VatRules`, `Pricing.VatOnInvoicedAmountAsync`): el asiento de la venta separa el IVA como el 13 % de lo cobrado (Ventas = 87 %; el IVA del total de la factura, redondeado una vez y repartido entre las líneas con `VatRules.Allocate`), la devolución igual, el costo neto de una compra es el 87 % de su factura y el catálogo mide el margen contra el 87 % del precio | Es la regla del SIN (Ley 843, arts. 5 y 15) y la de los libros de ventas y compras (`FiscalRules.Vat`): así 2.1.02 IVA débito fiscal coincide con el débito del libro de ventas, el margen del estado de resultados con el del catálogo, y la factura del proveedor de una compra al costo neto no toca el inventario. Hasta el cierre de la V4.2 la venta separaba importe × 13/113 (11,5 %), la convención de la V3; una empresa de otro país (tasa del 19 %, según el país de la dirección de sus sucursales) la conserva |
| L-01 | Las **ventas anteriores** a volver serializado un producto no tienen series y no se piden series retroactivas: esas unidades vendidas no aparecen en Series e IMEI ni tienen garantía por serie | Límite conocido; el inventario inicial en stock sí se registra con **Registrar series de stock** |
| L-02 | Con los roles predeterminados, **solo el Administrador** hace devoluciones (normales o por falla) | `CreateSalesReturnCommand` exige `sales.pos.operate` y `billing.void`; se puede ampliar en Usuarios y roles |
| L-03 | La compatibilidad solo evalúa las especificaciones que tienen **clave de compatibilidad**; un producto sin su ficha completa no genera errores por lo que le falta | Las reglas se basan en datos (T-06); la ficha de los productos del catálogo de prueba está completa |
| L-04 | El tablero Tecnología lee el modelo de escritura | Ver §8 |
| L-05 | Los movimientos que se registran a mano sin documento ni precio (SALDO INICIAL, ENTRADA, SALIDA, VENTA POS y DEVOLUCIÓN DE CLIENTE de «Registrar movimiento»; el SALDO INICIAL que genera la toma física en una existencia sin movimientos) **no generan asiento**: su contrapartida (apertura, proveedor, costo de ventas) la registra el contador con un asiento manual | Son el kardex de la V2.1: M-INV no sabe contra qué cuenta van. Los **AJUSTE (±)** (mermas, daños, pérdidas, sobrantes y faltantes de la toma física) sí se contabilizan al costo promedio desde el cierre de la V4.2 (5.1.09 / 4.1.02, `InventoryAdjustments`). La consulta de control de §12 muestra si 1.1.05 se apartó del valor del stock |
| L-06 | El **costo promedio no se recalcula** con la factura del proveedor (límite de la V4.1) | Con la factura que corresponde a la recepción (recepción / 0,87 al costo neto) el asiento no toca 1.1.05; si la factura dice otro importe (o la recepción entró al costo con IVA), la diferencia queda en 1.1.05 y el mayor se aparta del valor del stock en ese importe. El diálogo lo advierte antes de guardar |

## 12. Empresa de prueba y demostración

**Tech Zone Gaming S.R.L.** (código de empresa `TECHZONE`, NIT de simulación 1023456029), sucursales **CM** La Paz (casa
matriz, 3 cajas: CAJA01, CAJA02 y CAJA03 de servicio técnico), **CB** Cochabamba y **SC** Santa Cruz (una caja cada una);
12 usuarios de los 6 roles. Se genera desde `Seeding/Tecnologia/catalogo-tecnologia.json` (embebido): **35 categorías** (10
principales), **176 especificaciones**, **40 marcas**, **159 productos** con ficha técnica, imagen propia (sin logotipos) y
producto del SIN (105 con serie, 2 de ellos con IMEI: los routers 4G), **8 proveedores** mayoristas y **30 clientes**
ficticios (personas con CI, empresas e instituciones educativas con NIT, dominio `.example`) y **8 armados** (6 compatibles
y 2 incompatibles de prueba).

**Precio y costo del JSON.** El JSON trae `precio` y `costo` con el **IVA incluido** (13 %; `precios_con_iva`, `iva`) y
`margen_pct` = (precio − costo) / precio. En Bolivia el IVA forma parte del precio facturado y se calcula sobre el
**importe** (Ley 843, arts. 5 y 15; decisión D-10): el débito fiscal de una venta es el 13 % de lo cobrado, el crédito
fiscal de una compra es el 13 % de la factura del proveedor y el neto contable es el **87 %** del importe. Por eso los datos
de prueba compran al **costo neto de IVA** y el lector (`TechSeedCatalog.NetCost`) carga el costo como `costo × 0,87`
redondeado a 2 decimales (mitad hacia arriba, como los demás importes) para el producto, el saldo inicial y las compras.
El catálogo mide el margen con el costo promedio contra el **precio neto** (precio × 0,87, `VatRules.NetOf`): con el mismo
factor en el precio y en el costo, el margen es exactamente el `margen_pct` del JSON (el lector lo valida), del **15,2 al
34,9 %** (**22,1 %** en promedio) y ningún producto con «margen bajo» (menos del 15 %); el editor calcula el precio de un
margen como costo / (1 − margen) / 0,87 y muestra «Sin IVA» y «IVA» con la misma regla. Ejemplo: Ryzen 5 7600, precio
Bs 2.049 con IVA (Bs 1.782,63 neto), costo del JSON Bs 1.702 → costo en M-INV Bs 1.480,74 → margen 16,9 %. Hasta el cierre
de la V4.2 el costo neto era `costo / 1,13` (un 1,69 % más alto que el 87 %) y el catálogo dividía el precio por 1,13: el
margen mostrado era el mismo, pero la factura del proveedor no cuadraba con la recepción (ver abajo).

**Factura del proveedor de una compra al costo neto.** La recepción contabiliza Debe 1.1.05 Inventario / Haber 2.1.01
Proveedores por su valor (neto). La factura del proveedor es el importe definitivo de la compra: su asiento lleva el
crédito fiscal (13 % de la base) al Debe de 1.1.04, **suma a 2.1.01 la diferencia** entre el importe de la factura (menos
descuentos) y el valor de la recepción, y deja en 1.1.05 el resto (`RegisterSupplierInvoiceHandler`). Para una recepción al
costo neto, el importe que corresponde es **recepción / 0,87** (`FiscalRules.InvoiceForNetCost`; en los datos de prueba, el
costo con IVA del JSON): su crédito fiscal es exactamente el IVA que la factura suma a la deuda (también con el redondeo: el
13 % de un error de medio centavo no llega a mover el crédito; lo prueba `FiscalRulesTests` con 20.000 importes), así que el
asiento es Debe 1.1.04 / Haber 2.1.01 y **el inventario no cambia**: 1.1.05 sigue siendo el valor del stock. Ejemplo:
recepción neta de Bs 1.000,00 → factura de Bs 1.149,43 → Debe 1.1.04 149,43 / Haber 2.1.01 149,43. El diálogo **Registrar
factura del proveedor** propone ese importe y muestra el asiento que dejará lo que se escriba. Si la factura dice otro
importe, el inventario absorbe la diferencia (con Bs 1.130 = recepción × 1,13: Debe 1.1.04 146,90 / Haber 2.1.01 130,00 y
Haber 1.1.05 16,90) y, como el costo promedio no se recalcula con la factura, el mayor se aparta del valor del stock en ese
importe (límite L-06); si la recepción entró al costo con IVA (factura = recepción), el asiento es el de la V4.1: Debe
1.1.04 / Haber 1.1.05.

**Inventario del mayor = valor del stock.** En cada sucursal, el saldo de 1.1.05 Inventario debe ser Σ existencias × costo
promedio vigente de la variante en su almacén (el tránsito entre sucursales está en 1.1.06 − 2.1.04, regla B-05). Al
cierre de la V4.2, la carga de 60 días (datos de prueba con facturación, semilla 2026) dejaba **+452,79** en la casa matriz
(Cochabamba y Santa Cruz en 0,00). Descompuesto por tipo de movimiento contra los asientos de cada documento:

| Tipo de movimiento o documento (CM) | Variación del valor del stock | Asientos en 1.1.05 | Diferencia (1.1.05 − stock) |
|---|---:|---:|---:|
| SALDO INICIAL / asiento de apertura | 3.793.538,99 | 3.793.538,99 | 0,00 |
| RECEPCIÓN DE COMPRA / compras | 3.644.546,11 | 3.644.546,11 | 0,00 |
| VENTA POS / ventas (costo de ventas) | −2.327.512,54 | −2.327.512,54 | 0,00 |
| TRASLADO (SALIDA) / transferencias despachadas | −3.528.020,99 | −3.528.020,99 | 0,00 |
| DEVOLUCIÓN DE CLIENTE / devoluciones y anulación | 9.607,09 | 7.083,19 + 2.523,90 | 0,00 |
| REPOSICIÓN POR GARANTÍA / asiento 5.1.10 | −895,58 | −895,58 | 0,00 |
| **AJUSTE (−)**: 9 mermas y 1 faltante de la toma física | −3.134,52 | sin asiento | **+3.134,52** |
| **AJUSTE (+)**: 1 sobrante de la toma física | 488,50 | sin asiento | **−488,50** |
| **Facturas de 4 proveedores** (recepción × 1,13 con costo neto / 1,13) | 0,00 | −2.193,23 | **−2.193,23** |
| **Total** | | | **+452,79** |

No hay redondeo: con las compras siempre al mismo costo, el costo promedio de cada variante no cambia (2 decimales) y el
costo de ventas es exacto; la devolución por falla no mueve stock ni 1.1.05, y la baja de su serie (al proveedor) tampoco.
Las dos causas eran de lógica y se corrigieron en su origen. Los **AJUSTE (±)** de «Registrar movimiento» y de la toma
física cambiaban el stock sin asiento; ahora se contabilizan al costo promedio del almacén (`InventoryAdjustments`): la
merma o el faltante, Debe 5.1.09 Mermas y ajustes de inventario / Haber 1.1.05; el sobrante, Debe 1.1.05 / Haber 4.1.02
Sobrantes de inventario (cuentas del plan que ya existían y no se usaban). Y la factura del proveedor calculada como
recepción × 1,13 sobre un costo neto de «importe / 1,13» dejaba un crédito fiscal un 1,69 % de la recepción mayor que el
IVA sumado a la deuda; con el costo neto al 87 % y la factura = recepción / 0,87 cuadran exacto. Después de las dos
correcciones la misma carga deja **0,00** en las tres sucursales (lo vigilan `LocalDataSeederTests` en memoria y
`Los_datos_de_prueba_respetan_todas_las_restricciones_de_PostgreSQL` con `MINV_TEST_PG`). Consulta de control:

```sql
WITH cur AS (SELECT DISTINCT ON (variant_id, warehouse_id) variant_id, branch_id, average_cost
             FROM accounting.average_cost_history ORDER BY variant_id, warehouse_id, sequence DESC),
     val AS (SELECT s.branch_id, sum(s.quantity_on_hand * coalesce(cur.average_cost, 0)) AS valor
             FROM inventory.stock_levels s JOIN inventory.batches b ON b.id = s.batch_id
             LEFT JOIN cur ON cur.variant_id = b.variant_id AND cur.branch_id = s.branch_id GROUP BY 1),
     led AS (SELECT e.branch_id, sum(l.debit - l.credit) AS saldo FROM accounting.journal_lines l
             JOIN accounting.accounts a ON a.id = l.account_id JOIN accounting.journal_entries e ON e.id = l.journal_entry_id
             WHERE a.code = '1.1.05' GROUP BY 1)
SELECT b.code, led.saldo AS mayor_1105, round(val.valor, 2) AS valor_stock, round(led.saldo - val.valor, 2) AS diferencia
FROM warehouse.branches b LEFT JOIN led ON led.branch_id = b.id LEFT JOIN val ON val.branch_id = b.id ORDER BY 1;
```

La **base de prueba** (`tools\bd_local.ps1 -Accion recrear`, `minv datos-prueba`): **60 días** de operación en unos
**122 s** de carga, sobre **152 tablas en 10 esquemas**: unas **2.722 series e IMEI**, **7 casos RMA** en todos sus
estados, los **8 armados** (dos cobrados en la caja de Cochabamba y de Santa Cruz, uno incompatible en borrador y otro
cotizado con la confirmación explícita), una devolución por falla, transferencias con sus series (algunas con faltantes),
pedidos de la tienda en línea y unos **518 documentos fiscales** de los últimos 25 días con los escenarios de la V4.1. El
detalle de los escenarios está en [`inicio-rapido-v4.2.md`](../deployment/inicio-rapido-v4.2.md) §2. La
**demostración** usa la misma empresa en memoria (decisión D-05).
