# Reglas de la edición Tecnología · M-INV V4.2 (PC, componentes, periféricos y consolas)

> **Documento normativo** para personas y agentes que trabajen en la rama `Inventario-V4.2` (sobre `Inventario-V4.1`).
> **DEBE** = obligatorio · **NO DEBE** = prohibido · **PUEDE** = permitido. Complementa las reglas de la V3 (A-01…A-13),
> de la V4 (B-01…B-17) y de la facturación V4.1 (F-01…F-17); donde hablen de lo mismo (series, garantías, fichas técnicas,
> armado de PC, tema visual), **prevalece esta**. Diseño: `docs/architecture/edicion-tecnologia-v4.2.md`.

## 0. Principio rector

La V4.2 especializa M-INV para tiendas de tecnología y gaming **con datos, no con código a medida**: las fichas técnicas,
las plataformas y las reglas de compatibilidad son configuración (especificaciones por categoría con claves de
compatibilidad), y el motor sigue siendo el mismo (CQRS, append-only, multi-sucursal, nube, API y SIAT). Cada unidad
serializada es un hecho trazable de punta a punta: entra con su serie, se mueve con su serie, se vende con su serie
(y la factura del SIN la lleva) y vuelve por garantía con su serie.

## 1. Reglas

### T-01 · Fichas técnicas tipadas
- Una especificación (`SpecDefinition`) pertenece a una categoría y la heredan sus subcategorías. Tipos: texto, número
  (con unidad) y opción (valores de `SpecOption`); PUEDE ser multivalor (varias filas en `product_spec_values`).
- Un valor DEBE respetar el tipo de su especificación: el número va en la columna numérica, la opción referencia una
  `SpecOption` de ESA especificación (FK compuesta) y el texto no está vacío. NO DEBE guardarse un número como texto.
- Las especificaciones «filtrables» son las únicas que alimentan filtros y facetas; las que tienen clave de
  compatibilidad DEBEN usar una constante de `CompatibilityKeys`.
- Los valores de especificación NO DEBEN duplicar datos que ya existen en otras tablas (marca, precio, unidad, stock).

### T-02 · Productos serializados
- Un producto lleva serie si su modo de trazabilidad es `TrackingMode.Serial` (`catalog.products.tracking_mode`); su
  `ProductTechProfile.SerialKind` dice si la serie es un número de serie del fabricante o un IMEI (15 dígitos con dígito
  de Luhn) y cuántos meses de garantía tiene. La cantidad de una línea de un producto serializado DEBE ser entera y DEBE tener exactamente tantas series como
  unidades.
- Toda entrada (recepción de compra, saldo inicial, ajuste positivo), venta, devolución y transferencia de un producto
  serializado DEBE indicar las series; la operación se rechaza si falta, sobra o se repite una, o si la serie no está en
  el estado y la sucursal esperados (`serial.*`).
- La serie es única por (empresa, variante, serie). Su estado cambia SOLO con los métodos de `SerialNumber` y cada
  cambio DEBE dejar una fila en `inventory.serial_events` (append-only) con el documento y el usuario.
- Estados: `InStock` (en una sucursal) → `InTransit` (transferencia despachada) → `InStock`; `InStock` → `Sold` →
  `Returned`/`InRma` → `InStock`/`ReturnedToSupplier`/`Scrapped` (el ciclo completo, con sus métodos y la acción de
  bitácora de cada uno, está en el diseño §4). Una serie `Sold`, `Returned`, `InTransit`, `InRma`, `ReturnedToSupplier`
  o `Scrapped` NO DEBE contar como stock disponible ni venderse.
- Las existencias siguen saliendo de los movimientos (A-05): las series son trazabilidad, NO sustituyen al kardex. En
  cada sucursal, las series `InStock` de una variante serializada DEBEN coincidir con su stock.

### T-03 · Series en la factura del SIN
- La factura Compra Venta DEBE llevar en cada línea `numeroSerie` o `numeroImei` con las series vendidas de esa línea
  (separadas por «, » y dentro de los 1500 caracteres del XSD; si no caben, la línea se divide por unidades con el mismo
  precio y el descuento prorrateado). La nota crédito-débito guarda las series devueltas en sus líneas «devuelto» dentro
  de M-INV; su XSD (sector 24) no tiene `numeroSerie`, así que NO viajan en su XML. Nada de esto cambia los montos (F-06).

### T-04 · Garantía derivada
- La garantía vigente de una unidad = fecha de la venta + meses de garantía del producto (`ProductTechProfile`),
  calculada al consultar. NO DEBE guardarse una fecha de vencimiento de garantía. Una unidad entregada como reposición
  cuenta su garantía desde la fecha de la reposición (su último hecho «vendida» es `ReplacementIssued`).

### T-05 · Garantías y RMA
- Un caso RMA (`WarrantyClaim`) es de una sucursal (`IBranchScoped`), lleva número `RMA-<sucursal>-000001` y solo cambia
  de estado por `MoveTo` según la tabla de transiciones del agregado; cada cambio DEBE dejar su fila en
  `warranty_claim_events` (append-only).
- Recibir un equipo en garantía pasa la serie a `InRma` (sin entrada de stock vendible). La reposición con otra unidad
  (`IssueWarrantyReplacementCommand`) DEBE ser un movimiento de salida real (tipo `REPOSICION_GARANTIA`, «REPOSICIÓN POR
  GARANTÍA») con su serie y su asiento (Debe 5.1.10 Costo de garantías / Haber 1.1.05 Inventario al costo promedio); lo
  que el proveedor devuelva o acredite entra como hecho nuevo.
- La devolución por falla (`CreateSalesReturnCommand(…, Defective: true)`) reembolsa sin reingreso al stock: las series
  quedan en `InRma` hasta darles destino (`DisposeSerialCommand`: al proveedor o de baja).
- Fuera de garantía el caso PUEDE abrirse como reparación con cargo, marcado explícitamente.

### T-06 · Armador de PC
- La compatibilidad DEBE evaluarse SOLO con `PcCompatibility.Check` (dominio puro), a partir de las especificaciones con
  clave de compatibilidad; NO DEBEN escribirse reglas de compatibilidad en la interfaz ni en los casos de uso.
- Una cotización (`PcBuild`, `ARM-<sucursal>-000001`) congela el precio de cada línea al cotizar (redundancia comercial
  documentada: es la oferta hecha al cliente) y tiene vigencia; al pasar a la caja se vende con los casos de uso normales
  de venta (poka-yoke, series, factura) y la cotización queda `Sold` con la venta vinculada.
- Un armado con errores de compatibilidad NO DEBE cotizarse sin que el usuario lo confirme explícitamente (queda marcado).

### T-07 · Plataformas y condición
- «Plataforma» (PC, PS4, PS5, Xbox Series X, Xbox Series S, Nintendo Switch, Nintendo Switch 2) y «Condición» (Nuevo,
  Reacondicionado, Usado) son especificaciones de tipo opción; los filtros rápidos de la caja salen de sus opciones, no
  de listas fijas en la vista.

### T-08 · Tema gaming
- El tema oscuro es el predeterminado; claro y oscuro DEBEN tener las MISMAS claves de `Theme/Palette.*.xaml` y cumplir
  contraste WCAG AA en textos. Sigue vigente A-09: ningún color fijo en las vistas fuera del degradado de marca.

### T-09 · Datos de prueba y marcas
- La empresa de prueba es **Tech Zone Gaming S.R.L.** (`TECHZONE`, sucursales CM, CB y SC) y se carga con los casos de
  uso (A-13), desde `catalogo-tecnologia.json` embebido. Proveedores, clientes y correos son ficticios (`.example`).
  Excepción parcial documentada en `LocalDataSeeder.SeedCatalogAsync`: lo que todavía no tiene caso de uso (marcas y
  modelos, la unidad `SERV`, las categorías de cliente, las cajas extra y la topología del almacén) se registra directo en
  el contexto de la sesión del administrador, como el aprovisionamiento. Los servicios (`SERV`) llevan un cupo de
  existencias porque el dominio no tiene artículos sin stock.
- La demostración en memoria (A-12) es la MISMA empresa, generada con el mismo `LocalDataSeeder` (menos días, facturación
  con el simulador del SIN en memoria, sin los escenarios de contingencia); el importador de la V2.1 sigue disponible
  (`minv import-v21`) pero ya no es la fuente de la demostración. Prevalece sobre lo que A-12 dice de la V2.1.
- Los nombres comerciales de fabricantes PUEDEN aparecer en la descripción de un producto (uso nominativo); las imágenes
  DEBEN ser dibujos propios sin logotipos ni marcas registradas.

### T-10 · Definición de terminado (DoD) de la V4.2
Además de B-17 y F-17: pasan las pruebas de `PcCompatibility` (armados compatibles e incompatibles del catálogo), del
ciclo de vida de una serie (recepción → venta con factura → devolución → RMA → reposición) en memoria y, con
`MINV_TEST_PG`, en PostgreSQL; `v_serial_breaches` (series en stock ≠ stock) está vacía; las capturas de la V4.2 se
revisaron en tema oscuro y claro.

## 2. Checklist para agentes

- [ ] ¿Una línea de producto serializado sin series, con series de otra sucursal o repetidas? → rechazar (T-02).
- [ ] ¿Un cambio de estado de serie o de RMA sin su fila de bitácora? → corregir (T-02, T-05).
- [ ] ¿Una fecha de fin de garantía guardada? → derivarla (T-04).
- [ ] ¿Una regla de compatibilidad fuera de `PcCompatibility`? → moverla (T-06).
- [ ] ¿Una lista fija de plataformas o categorías en una vista? → especificaciones (T-07).
- [ ] ¿Un color fijo o una clave de paleta solo en un tema? → paleta (T-08).
- [ ] ¿Una imagen con logotipo de marca? → dibujo propio (T-09).
