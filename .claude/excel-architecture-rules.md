# Reglas de arquitectura Excel · M-INV V1.x (CQRS)

> **Documento normativo.** Aplica a personas y agentes (Claude Code) que trabajen en `ZP-MINV-Platform`.
> **DEBE** = obligatorio · **NO DEBE** = prohibido · **PUEDE** = permitido.
> Si una solicitud contradice estas reglas, el agente DEBE señalarlo antes de ejecutar.

## 0. Principio rector

M-INV V1 es una **aplicación transaccional inmutable que corre sobre Excel**, no una hoja de cálculo.
El inventario solo cambia registrando hechos (movimientos); todo lo demás (stock, alertas, consulta, pedido, tablero)
es una **proyección calculada** de esos hechos. Este es el patrón CQRS:

```text
   COMANDOS (escritura)                          CONSULTAS (lectura)
┌──────────────────────┐   SUMAR.SI.CONJUNTO   ┌──────────────────────┐   ┌──────────────┐
│ 10_MOVIMIENTOS       │ ────────────────────► │ 15_STOCK · 16_ALERTAS│──►│ 00_PORTADA   │
│ bitácora append-only │   Σ CantidadNeta/SKU  │ 17_KARDEX · 18_PEDIDO│   │ (tablero)    │
└──▲────────────▲──────┘                       └──────────▲───────────┘   └──────────────┘
   │ escriben   │ listas validadas                        │ reglas de semáforo
┌──┴──────────┐ ┌┴──────────────────────────┐  ┌──────────┴───────────┐
│ 12_REGISTRO │ │ 04_PROVEEDORES            │  │ 01_CONFIG · 03 · 06  │
│ 13_CONTEO   │ │ 05_PRODUCTOS (maestros)   │  │ (configuración)      │
└─────────────┘ └───────────────────────────┘  └──────────────────────┘
```

## 1. Capas y hojas

| Capa | Hoja | Rol | Core (dev) | Release (cliente) | Quién escribe |
|---|---|---|---|---|---|
| Presentación | `00_PORTADA` | Tablero, próximo paso, navegación, KPIs | visible | visible | nadie (solo lectura) |
| Configuración | `01_CONFIG` | Parámetros del tenant, tipos de movimiento, semáforo, responsables | oculta | **muy oculta** | administrador (con contraseña) |
| Configuración | `03_CATEGORIAS` | Categorías (nivel 1 de la cascada) | oculta | **muy oculta** | administrador |
| Maestro | `04_PROVEEDORES` | Proveedores (contacto, días de entrega) | visible | visible | administrador del cliente (celdas ✎) |
| Maestro | `05_PRODUCTOS` | Catálogo de productos | visible | visible | administrador del cliente (celdas ✎) |
| Configuración | `06_UNIDADES` | Unidades y regla de decimales | oculta | **muy oculta** | administrador |
| Escritura | `10_MOVIMIENTOS` | Bitácora inmutable (append-only) | visible | visible | operador (celdas ✎) o comandos |
| Comando (Plus) | `12_REGISTRO` | Formulario guiado: valida y escribe en la bitácora (VBA) | visible | visible | operador (campos ✎) |
| Comando | `13_CONTEO` | Toma física: diferencias → ajustes en la bitácora | visible | visible | operador (columna `Conteo`) |
| Lectura | `15_STOCK` | Proyección de stock por SKU | visible | visible | nadie (100 % fórmulas) |
| Lectura | `16_ALERTAS` | Proyección priorizada de alertas | visible | visible | nadie (100 % fórmulas) |
| Lectura | `17_KARDEX` | Consulta de un producto (ficha, gráfico, historial) | visible | visible | solo los selectores ✎ |
| Lectura | `18_PEDIDO` | Pedido sugerido por proveedor | visible | visible | solo el filtro ✎ |
| Motor | `90_LISTAS` | Listas en cascada, búsquedas por texto, proveedores | oculta | **muy oculta** | nadie |
| Motor | `91_KPIS` | Indicadores, asistente, series, rankings, cálculos del formulario y la consulta | oculta | **muy oculta** | nadie |
| Presentación | `99_AYUDA` | Guía didáctica por perfil y primeros pasos | visible | visible | nadie |

Numeración reservada: `02` bodegas, `07`-`09` maestros futuros, `11` traslados, `14` otros comandos,
`19` otras proyecciones, `9x` motor.

**Ediciones.** El mismo generador produce la edición **Estándar** (`.xlsx`, sin macros) y la **Plus** (`.xlsm`,
con `12_REGISTRO` y automatizaciones VBA). La Plus con las macros deshabilitadas DEBE comportarse como la Estándar.

## 2. Reglas

### R-01 · Excel-as-code
- La fuente de verdad es `tools/build_minv.py` + el paquete `tools/minv/` (un módulo por capa) + `tools/demo_data.py`.
  Los `.xlsx`/`.xlsm` son **artefactos generados**: la edición Plus se construye con `tools/build_xlsm.ps1` a partir de
  la base que genera el script de Python y del VBA fuente de `src/macros/`.
- NO DEBE editarse a mano ningún libro de `src/` ni de `releases/`. Cualquier cambio se hace en el generador o en
  `src/macros/`, se regenera y se verifica (R-12).
- La única excepción es el ajuste de datos del tenant durante una implementación en el archivo del cliente
  (nunca en el repositorio).

### R-02 · Bitácora append-only
- `10_MOVIMIENTOS` es la **única** hoja donde cambia el inventario. Los comandos (`12_REGISTRO`, `13_CONTEO`)
  escriben filas en ella; NO DEBEN modificar stock ni proyecciones.
- Cada cambio es una **fila nueva**. NO DEBE borrarse, ordenarse, reemplazarse ni "corregirse" una fila registrada.
- Los errores se corrigen con un movimiento compensatorio (`AJUSTE (+)` / `AJUSTE (-)`) cuya columna
  `Observaciones` es obligatoria (la valida la columna `Estado`).
- La protección de hoja impide borrar/insertar filas y ordenar. En la edición Plus las filas con `Estado = ✔` se
  **sellan** (celdas bloqueadas, en gris, `cfgSelladoHasta`) al registrar desde el formulario, al generar ajustes y
  antes de cada guardado.

### R-03 · FactorStock
- Todo tipo de movimiento vive en `tblTiposMov` (`01_CONFIG`) con `FactorStock` ∈ {+1, −1}.
- `Cantidad` es **siempre positiva**; `CantidadNeta = Cantidad × FactorStock` es el único valor que suman las proyecciones.
- Agregar un tipo = agregar una fila a `tblTiposMov` (sin tocar fórmulas). NO DEBE existir un tipo con factor 0.

### R-04 · El stock nunca se escribe
- `15_STOCK`, `16_ALERTAS`, `17_KARDEX` y `18_PEDIDO` son proyecciones calculadas. NO DEBEN tener valores pegados
  ni ajustes manuales (solo los selectores y filtros de consulta son ✎).
- `StockActual = SUMAR.SI.CONJUNTO(tblMovimientos[CantidadNeta]; tblMovimientos[SKU]; [@SKU])`.
- Invariante de conservación: `Σ StockActual = Σ CantidadNeta` (lo comprueba `verify_minv.ps1`).

### R-05 · Integridad referencial sin tipeo
- El operador NO DEBE escribir IDs, SKUs ni nombres de producto: todo se elige de listas (validación de datos).
- Cascada: `Categoría` (lista `lstCategorias`) → `Producto` (lista filtrada `lpCat/lpBase` de `90_LISTAS`). La
  búsqueda por texto (`lfForm`, `lfKardex`) solo **filtra** la lista; el valor sigue saliendo de ella.
- El producto se guarda como etiqueta `SKU · Nombre`; el `SKU` se **deriva** del texto y `Unidad` / `FactorStock`
  se buscan con `INDICE + COINCIDIR` (equivalente robusto de `BUSCARV`, independiente del orden de columnas).
- La columna `Estado` re-valida cada fila (pegar valores salta la validación de datos): `✔` correcto,
  `⚠` revisar, `✖` error.
- NO DEBE modificarse el SKU de un producto con movimientos: se crea uno nuevo y el anterior pasa a `Activo = NO`.

### R-06 · Proyecciones alineadas por fila
- La fila *n* de `15_STOCK` y de `13_CONTEO` refleja la fila *n* de `05_PRODUCTOS` (y de `90_LISTAS` y `91_KPIS`). Las
  capacidades `MAX_PROD` (500), `MAX_PROV` (100), `MAX_MOV` (5.000) y `MAX_KARDEX` (100) DEBEN ser iguales en todos los
  rangos dependientes (constantes únicas en `tools/minv/base.py`).

### R-07 · Protección por capas
- Fórmulas: celda **bloqueada**. Ingresos: celda **desbloqueada** con estilo ✎ (fondo blanco, borde azul suave).
- Todas las hojas se protegen con contraseña. En Release además: fórmulas **ocultas**, capas de configuración y
  motor en **muy oculto** y **estructura del libro bloqueada**.
- La portada no permite seleccionar celdas (solo botones); el formulario solo permite seleccionar sus campos. Las hojas
  de datos permiten filtrar y ajustar anchos.
- La contraseña de hoja es una barrera anti-accidentes, **no** un control de seguridad. Nunca contiene datos sensibles.

### R-08 · Compatibilidad (línea base: Excel 2016+ en Windows)
- PUEDE usarse: `SUMAR.SI.CONJUNTO`, `CONTAR.SI(.CONJUNTO)`, `INDICE`, `COINCIDIR`, `SI.ERROR`, `K.ESIMO.MENOR/MAYOR`,
  `SUMAPRODUCTO`, `AGREGAR` (`_xlfn.AGGREGATE`), `DECIMAL` (`FIXED`), `ELEGIR`, `FECHA`, `HOY`.
- NO DEBE usarse: `BUSCARX`/`XLOOKUP`, `FILTRAR`, `ORDENAR`, `UNICOS`, `LET`, `LAMBDA` (exigen 365/2021) ni
  `INDIRECTO` (volátil).
- `DESREF` (`OFFSET`) solo dentro de validaciones de datos (no recalcula la hoja).
- NO DEBE usarse `TEXTO()` con códigos de fecha/número: los códigos cambian con el idioma de Excel
  (`aaaa` vs `yyyy`). Use formatos de número de celda o `DECIMAL()`.
- NO DEBEN usarse **referencias estructuradas** (`tblX[Columna]`) dentro de **formatos condicionales** ni de
  **validaciones de datos**: Excel rechaza el libro **completo** al abrirlo (ni siquiera lo repara). Use nombres
  definidos (`kxStock`, `lstCategorias`…). El post-proceso del generador (`_guard_rules`) aborta el build si aparece una.

### R-09 · Nomenclatura
| Objeto | Convención | Ejemplo |
|---|---|---|
| Hojas | `NN_NOMBRE` en mayúsculas | `10_MOVIMIENTOS` |
| Tablas | `tbl` + PascalCase | `tblMovimientos` |
| Columnas | PascalCase sin espacios (legibles en `[@Columna]`) | `FactorStock`, `CantidadNeta` |
| Listas de validación | `lst*` (simples), `lp*` (cascada), `lf*` (búsqueda por texto) | `lstCategorias`, `lpCat`, `lfForm` |
| Parámetros | `cfg*` | `cfgMargenAlerta` |
| Indicadores / textos del tablero | `kpi*` / `txt*` | `kpiEnAlerta`, `txtPaso` |
| Campos y cálculos por hoja | `frm*` formulario · `kx*` consulta · `pd*` pedido · `ct*` conteo | `frmValido`, `kxSKU` |
| Destinos de navegación dinámica | `ir*` | `irSiguientePaso` |
| Módulos VBA | `mod` + PascalCase; código de hoja en `Hoja_<NN_NOMBRE>.txt` | `modRegistro` |

### R-10 · Tablas y capacidad
- Excel no expande tablas en hojas protegidas: `tblProductos` (500), `tblProveedores` (100) y `tblMovimientos` (5.000)
  están **pre-asignadas**. La fila libre siguiente se resalta en azul y los botones `ir*` saltan a ella.
- Las tablas de configuración (`tblTiposMov`, `tblEstados`, `tblResponsables`, `tblCategorias`, `tblUnidades`) son
  normales: el administrador desprotege, agrega la fila y la tabla (y las listas) crecen solas.
- Al acercarse a 5.000 registros se ejecuta el **cierre de período**: archivo histórico + libro nuevo con
  `SALDO INICIAL` por producto.

### R-11 · Datos
- Los datos de demostración viven solo en el Core. El Release sale limpio (catálogo, proveedores y bitácora vacíos).
- NO DEBEN versionarse datos reales de clientes en este repositorio. Los proveedores demo usan dominios `.example`.

### R-12 · Definición de terminado (DoD)
Un cambio al libro está terminado solo si:
1. `tools/build_all.ps1` termina **sin fallas**: genera los 4 libros, construye la edición Plus con sus pruebas de VBA
   y pasa `verify_minv.ps1` en los 4 entregables (0 errores de fórmula, conservación CQRS, cascada y búsqueda,
   consulta, pedido, conteo, formulario, navegación, 16_ALERTAS consistente).
2. Las vistas previas se revisaron visualmente y `docs/` refleja el cambio (diccionario de datos, guía UX y
   `CHANGELOG.md`).

### R-13 · VBA (edición Plus)
- El VBA es una **capa de comandos y comodidad**: PUEDE escribir en `10_MOVIMIENTOS` (filas libres), en los campos del
  formulario, en `Conteo` y en `cfgSelladoHasta`. NO DEBE escribir en proyecciones, maestros ni configuración.
- Todo comando que escribe en la bitácora DEBE comprobar después el `Estado` que calcula la propia bitácora y deshacer
  su escritura (o informarla) si no es `✔`. Las validaciones viven en fórmulas, no solo en VBA.
- Toda función DEBE seguir disponible sin macros (Estándar): el VBA agrega atajos, nunca el único camino.
- Cada punto de entrada (botón, doble clic, evento) DEBE manejar errores y avisar con `Avisar`; los avisos respetan
  `ModoSilencioso` para las pruebas automáticas (nunca `MsgBox` directo).
- Fuentes en UTF-8 dentro de `src/macros/`, compatibles con Windows-1252: los símbolos ✔ ✖ ○ se generan con `ChrW`.
- La contraseña se inyecta al construir (marcador `__MINV_PASSWORD__`); NO DEBE versionarse una contraseña real.
  Antes de entregar un Release Plus, bloquee el proyecto VBA.
- `UserInterfaceOnly` no se guarda en el archivo: `Workbook_Open` reprotege las hojas que las macros modifican, con los
  mismos permisos del generador.
- Toda macro nueva DEBE tener prueba en `tools/build_xlsm.ps1`.

## 3. Mapa de migración a SQL/.NET (V2)

| Excel (V1) | SQL (V2) | Nota |
|---|---|---|
| `tblProductos` | `producto` | `sku` UNIQUE, FK `categoria_id`, `unidad_id`, `proveedor_id` |
| `tblProveedores` | `proveedor` | `nombre` UNIQUE, `dias_entrega` |
| `tblMovimientos` | `movimiento` | tabla append-only (sin UPDATE/DELETE; permisos + trigger) — equivale al sellado |
| `tblTiposMov.FactorStock` | `tipo_movimiento.factor_stock` | `CHECK (factor_stock IN (-1, 1))` |
| `15_STOCK` | vista `v_stock` / vista materializada | `SUM(cantidad * factor_stock) GROUP BY producto_id` |
| `16_ALERTAS` | vista `v_alertas` | mismas reglas de `tblEstados` |
| `17_KARDEX` | consulta `movimiento` por producto + `SUM() OVER (ORDER BY id)` | |
| `18_PEDIDO` | vista `v_pedido_sugerido` | agrupada por proveedor |
| `13_CONTEO` | `toma_fisica` + `toma_fisica_detalle` | al cerrar genera `movimiento` tipo AJUSTE |
| `12_REGISTRO` + VBA | caso de uso *RegistrarMovimiento* en .NET | mismas validaciones del `Estado` |
| `91_KPIS` | consultas del tablero / API | |
| Validación de datos + `Estado` | restricciones FK/CHECK + validación de dominio en .NET | |

El detalle campo a campo está en `docs/architecture/data-dictionary.md`.

## 4. Checklist para agentes

- [ ] ¿El cambio escribe stock fuera de `10_MOVIMIENTOS`? → **rechazar**.
- [ ] ¿Un formato condicional o una validación usa `tblX[...]`? → usar un nombre definido (R-08).
- [ ] ¿Se agregó una columna? → actualizar generador, diccionario de datos y verificador.
- [ ] ¿Se usó una función de la lista prohibida (R-08)? → reemplazar.
- [ ] ¿La función nueva exige macros? → debe existir también el camino sin macros (R-13).
- [ ] ¿Se agregó o cambió VBA? → prueba en `tools/build_xlsm.ps1`.
- [ ] ¿Pasó `tools/build_all.ps1` sin fallas? → adjuntar el resultado en el PR.
