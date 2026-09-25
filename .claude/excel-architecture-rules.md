# Reglas de arquitectura Excel · M-INV V1 (CQRS)

> **Documento normativo.** Aplica a personas y agentes (Claude Code) que trabajen en `ZP-MINV-Platform`.
> **DEBE** = obligatorio · **NO DEBE** = prohibido · **PUEDE** = permitido.
> Si una solicitud contradice estas reglas, el agente DEBE señalarlo antes de ejecutar.

## 0. Principio rector

M-INV V1 es una **aplicación transaccional inmutable que corre sobre Excel**, no una hoja de cálculo.
El inventario solo cambia registrando hechos (movimientos); todo lo demás (stock, alertas, tablero) es
una **proyección calculada** de esos hechos. Este es el patrón CQRS:

```text
   COMANDOS (escritura)                          CONSULTAS (lectura)
┌──────────────────────┐   SUMAR.SI.CONJUNTO   ┌──────────────────────┐   ┌──────────────┐
│ 10_MOVIMIENTOS       │ ────────────────────► │ 15_STOCK             │──►│ 00_PORTADA   │
│ bitácora append-only │   Σ CantidadNeta/SKU  │ 16_ALERTAS           │   │ (tablero)    │
└──────────▲───────────┘                       └──────────▲───────────┘   └──────────────┘
           │ listas validadas                             │ reglas de semáforo
┌──────────┴───────────┐                       ┌──────────┴───────────┐
│ 05_PRODUCTOS (maestro)│                      │ 01_CONFIG · 03 · 06  │
└───────────────────────┘                      │ (configuración)      │
                                               └──────────────────────┘
```

## 1. Capas y hojas

| Capa | Hoja | Rol | Core (dev) | Release (cliente) | Quién escribe |
|---|---|---|---|---|---|
| Presentación | `00_PORTADA` | Tablero, navegación, KPIs | visible | visible | nadie (solo lectura) |
| Configuración | `01_CONFIG` | Parámetros del tenant, tipos de movimiento, semáforo, responsables | oculta | **muy oculta** | administrador (con contraseña) |
| Configuración | `03_CATEGORIAS` | Categorías (nivel 1 de la cascada) | oculta | **muy oculta** | administrador |
| Maestro | `05_PRODUCTOS` | Catálogo de productos | visible | visible | administrador del cliente (celdas ✎) |
| Configuración | `06_UNIDADES` | Unidades y regla de decimales | oculta | **muy oculta** | administrador |
| Escritura | `10_MOVIMIENTOS` | Bitácora inmutable (append-only) | visible | visible | operador (celdas ✎) |
| Lectura | `15_STOCK` | Proyección de stock por SKU | visible | visible | nadie (100 % fórmulas) |
| Lectura | `16_ALERTAS` | Proyección priorizada de alertas | visible | visible | nadie (100 % fórmulas) |
| Motor | `90_LISTAS` | Listas en cascada ordenadas | oculta | **muy oculta** | nadie |
| Motor | `91_KPIS` | Indicadores, series de gráficos, ranking de alertas | oculta | **muy oculta** | nadie |
| Presentación | `99_AYUDA` | Guía didáctica | visible | visible | nadie |

Numeración reservada: `02` bodegas, `04` proveedores/terceros, `07`-`09` maestros futuros,
`11`-`14` otros comandos (p. ej. traslados), `17`-`19` otras proyecciones (p. ej. kardex), `9x` motor.

## 2. Reglas

### R-01 · Excel-as-code
- La fuente de verdad es `tools/build_minv.py` (+ `tools/demo_data.py`). Los `.xlsx` son **artefactos generados**.
- NO DEBE editarse a mano `src/M-INV_V1_Core.xlsx` ni `releases/M-INV_V1_Produccion_Bloqueado.xlsx`.
  Cualquier cambio se hace en el generador, se regenera y se verifica (R-12).
- La única excepción es el ajuste de datos del tenant durante una implementación en el archivo del cliente
  (nunca en el repositorio).

### R-02 · Bitácora append-only
- `10_MOVIMIENTOS` es la **única** hoja donde cambia el inventario.
- Cada cambio es una **fila nueva**. NO DEBE borrarse, ordenarse, reemplazarse ni "corregirse" una fila registrada.
- Los errores se corrigen con un movimiento compensatorio (`AJUSTE (+)` / `AJUSTE (-)`) cuya columna
  `Observaciones` es obligatoria (la valida la columna `Estado`).
- La protección de hoja impide borrar/insertar filas y ordenar. Sellar filas ya registradas exige VBA (backlog V1.1).

### R-03 · FactorStock
- Todo tipo de movimiento vive en `tblTiposMov` (`01_CONFIG`) con `FactorStock` ∈ {+1, −1}.
- `Cantidad` es **siempre positiva**; `CantidadNeta = Cantidad × FactorStock` es el único valor que suman las proyecciones.
- Agregar un tipo = agregar una fila a `tblTiposMov` (sin tocar fórmulas). NO DEBE existir un tipo con factor 0.

### R-04 · El stock nunca se escribe
- `15_STOCK` y `16_ALERTAS` son proyecciones 100 % calculadas. NO DEBEN tener celdas de ingreso,
  valores pegados ni ajustes manuales.
- `StockActual = SUMAR.SI.CONJUNTO(tblMovimientos[CantidadNeta]; tblMovimientos[SKU]; [@SKU])`.
- Invariante de conservación: `Σ StockActual = Σ CantidadNeta` (lo comprueba `verify_minv.ps1`).

### R-05 · Integridad referencial sin tipeo
- El operador NO DEBE escribir IDs, SKUs ni nombres de producto: todo se elige de listas (validación de datos).
- Cascada: `Categoría` (lista `lstCategorias`) → `Producto` (lista filtrada `lpCat/lpBase` de `90_LISTAS`).
- El producto se guarda como etiqueta `SKU · Nombre`; el `SKU` se **deriva** del texto y `Unidad` / `FactorStock`
  se buscan con `INDICE + COINCIDIR` (equivalente robusto de `BUSCARV`, independiente del orden de columnas).
- La columna `Estado` re-valida cada fila (pegar valores salta la validación de datos): `✔` correcto,
  `⚠` revisar, `✖` error.
- NO DEBE modificarse el SKU de un producto con movimientos: se crea uno nuevo y el anterior pasa a `Activo = NO`.

### R-06 · Proyecciones alineadas por fila
- La fila *n* de `15_STOCK` refleja la fila *n* de `05_PRODUCTOS` (y de `90_LISTAS` y `91_KPIS`). Las capacidades
  `MAX_PROD` (500) y `MAX_MOV` (5.000) DEBEN ser iguales en todos los rangos dependientes (constantes únicas en el generador).

### R-07 · Protección por capas
- Fórmulas: celda **bloqueada**. Ingresos: celda **desbloqueada** con estilo ✎ (fondo blanco, borde azul suave).
- Todas las hojas se protegen con contraseña. En Release además: fórmulas **ocultas**, capas de configuración y
  motor en **muy oculto** y **estructura del libro bloqueada**.
- La portada no permite seleccionar celdas (solo botones). Las hojas de datos permiten filtrar y ajustar anchos.
- La contraseña de hoja es una barrera anti-accidentes, **no** un control de seguridad. Nunca contiene datos sensibles.

### R-08 · Compatibilidad (línea base: Excel 2016+ en Windows)
- PUEDE usarse: `SUMAR.SI.CONJUNTO`, `CONTAR.SI(.CONJUNTO)`, `INDICE`, `COINCIDIR`, `SI.ERROR`, `K.ESIMO.MENOR/MAYOR`,
  `SUMAPRODUCTO`, `AGREGAR` (`_xlfn.AGGREGATE`), `DECIMAL` (`FIXED`), `ELEGIR`, `FECHA`, `HOY`.
- NO DEBE usarse: `BUSCARX`/`XLOOKUP`, `FILTRAR`, `ORDENAR`, `UNICOS`, `LET`, `LAMBDA` (exigen 365/2021) ni
  `INDIRECTO` (volátil).
- `DESREF` (`OFFSET`) solo dentro de validaciones de datos (no recalcula la hoja).
- NO DEBE usarse `TEXTO()` con códigos de fecha/número: los códigos cambian con el idioma de Excel
  (`aaaa` vs `yyyy`). Use formatos de número de celda o `DECIMAL()`.

### R-09 · Nomenclatura
| Objeto | Convención | Ejemplo |
|---|---|---|
| Hojas | `NN_NOMBRE` en mayúsculas | `10_MOVIMIENTOS` |
| Tablas | `tbl` + PascalCase | `tblMovimientos` |
| Columnas | PascalCase sin espacios (legibles en `[@Columna]`) | `FactorStock`, `CantidadNeta` |
| Listas de validación | `lst*` (simples), `lp*` (cascada de productos) | `lstCategorias`, `lpCat` |
| Parámetros | `cfg*` | `cfgMargenAlerta` |
| Indicadores / textos del tablero | `kpi*` / `txt*` | `kpiEnAlerta`, `txtAlertasBtn` |
| Destinos de navegación dinámica | `ir*` | `irFilaLibreMov` |

### R-10 · Tablas y capacidad
- Excel no expande tablas en hojas protegidas: `tblProductos` y `tblMovimientos` están **pre-asignadas**
  (500 / 5.000 filas). La fila libre siguiente se resalta en azul y los botones `ir*` saltan a ella.
- Las tablas de configuración (`tblTiposMov`, `tblEstados`, `tblResponsables`, `tblCategorias`, `tblUnidades`) son
  normales: el administrador desprotege, agrega la fila y la tabla (y las listas) crecen solas.
- Al acercarse a 5.000 registros se ejecuta el **cierre de período**: archivo histórico + libro nuevo con
  `SALDO INICIAL` por producto.

### R-11 · Datos
- Los datos de demostración viven solo en el Core. El Release sale limpio (catálogo y bitácora vacíos).
- NO DEBEN versionarse datos reales de clientes en este repositorio.

### R-12 · Definición de terminado (DoD)
Un cambio al libro está terminado solo si:
1. `python tools/build_minv.py` genera ambos libros sin errores.
2. `tools/verify_minv.ps1` pasa **sin fallas** en Core y Release (0 errores de fórmula, conservación CQRS,
   cascada, navegación, 16_ALERTAS consistente).
3. Las vistas previas se revisaron visualmente y `docs/` refleja el cambio (diccionario de datos y guía UX).

## 3. Mapa de migración a SQL/.NET (V2)

| Excel (V1) | SQL (V2) | Nota |
|---|---|---|
| `tblProductos` | `producto` | `sku` UNIQUE, FK `categoria_id`, `unidad_id` |
| `tblMovimientos` | `movimiento` | tabla append-only (sin UPDATE/DELETE; permisos + trigger) |
| `tblTiposMov.FactorStock` | `tipo_movimiento.factor_stock` | `CHECK (factor_stock IN (-1, 1))` |
| `15_STOCK` | vista `v_stock` / vista materializada | `SUM(cantidad * factor_stock) GROUP BY producto_id` |
| `16_ALERTAS` | vista `v_alertas` | mismas reglas de `tblEstados` |
| `91_KPIS` | consultas del tablero / API | |
| Validación de datos + `Estado` | restricciones FK/CHECK + validación de dominio en .NET | |

El detalle campo a campo está en `docs/architecture/data-dictionary.md`.

## 4. Checklist para agentes

- [ ] ¿El cambio escribe stock fuera de `10_MOVIMIENTOS`? → **rechazar**.
- [ ] ¿Se agregó una columna? → actualizar generador, diccionario de datos y verificador.
- [ ] ¿Se usó una función de la lista prohibida (R-08)? → reemplazar.
- [ ] ¿Se regeneraron **ambos** libros y pasó `verify_minv.ps1`? → adjuntar el resultado en el PR.
