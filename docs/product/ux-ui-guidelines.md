# Guía UX/UI · M-INV V1.2 ("App-like Excel")

Objetivo: que un operador sin conocimientos técnicos perciba M-INV como **software nativo**, no como una hoja de
cálculo, y que le resulte **imposible romper el sistema por accidente**. Todo lo descrito aquí está implementado en
`tools/build_minv.py` (paquete `tools/minv/`, tokens en `base.py`); si cambia un token, cambie ambos.

## 1. Principios

1. **App, no hoja.** Sin cuadrícula, sin encabezados de fila/columna, columnas sobrantes ocultas, navegación con botones.
2. **El error no debe ser posible; si ocurre, debe ser visible.** Listas en lugar de tipeo, validación en cada celda
   de ingreso y una columna `Estado` que re-valida cada fila (`✔` / `⚠` / `✖`).
3. **Se enseña en el lugar.** Mensajes de entrada en cada columna y encabezado, leyendas en la barra de herramientas
   y la hoja `99_AYUDA`.
4. **Ingreso ≠ cálculo, siempre.** Lo que se escribe es blanco con borde azul (✎); lo que calcula el sistema es gris y
   está bloqueado (ƒx).
5. **Color con significado.** El semáforo se reserva para el estado del stock y siempre va acompañado de texto
   (nunca solo color).
6. **Siempre hay un siguiente paso.** La portada dice qué hacer ahora y lleva al lugar exacto; cada hoja tiene su
   acción principal a un clic y la guía se organiza por perfil (Bodega, Compras, Gerencia, Administración).
7. **Ver antes de confirmar.** El formulario muestra el stock antes → después y el semáforo resultante antes de
   registrar; las acciones irreversibles (ajustes del conteo) piden confirmación con «No» como opción por defecto.

## 2. Paleta (tokens)

| Token | Hex | Uso |
|---|---|---|
| `ink` | `#0B1F33` | Franja superior (héroe), encabezados de mini-tablas |
| `ink2` | `#16324F` | Encabezados secundarios |
| `nav` / `nav_text` | `#1C3A5A` / `#DCE7F3` | Píldoras de navegación inactivas |
| `brand` | `#1565C0` | Acción primaria, encabezados de columnas ✎ |
| `brand_dk` | `#0D47A1` | Valores monetarios, énfasis |
| `brand_lt` | `#EAF3FE` | Resaltado de la **siguiente fila libre** |
| `canvas` | `#F3F5F8` | Fondo de la aplicación |
| `white` | `#FFFFFF` | Tarjetas, celdas de ingreso |
| `text` / `muted` / `faint` | `#1F2937` / `#5F6B7A` / `#5F6E84` | Texto principal / secundario / terciario |
| `input_border` | `#8FB3DE` | Borde de celda de ingreso ✎ |
| `calc_fill` / `calc_text` | `#F1F4F8` / `#334155` | Celda calculada ƒx |
| `hdr_calc` | `#475569` | Encabezado de columna ƒx |
| `teal` | `#00796B` | Stock / movimientos del mes |
| `purple` | `#5E35B1` | Catálogo / último movimiento |

### Semáforo

| Estado | Texto | Fondo | Gráfico | Significado |
|---|---|---|---|---|
| AGOTADO | `#FFFFFF` | `#B71C1C` | `#9B1C1C` | Stock en cero (sólido: máxima urgencia) |
| CRÍTICO | `#C62828` | `#FDECEA` | `#D32F2F` | En o bajo el mínimo (**rojo**) |
| BAJO | `#B45309` | `#FEF3C7` | `#F59E0B` | Dentro del margen de alerta (**ámbar**) |
| ÓPTIMO | `#2E7D32` | `#E8F5E9` | `#2E7D32` | Nivel sano (**verde**) |
| SOBRESTOCK | `#1565C0` | `#E3F2FD` | `#1565C0` | Sobre el máximo (azul, informativo) |
| INCONSISTENTE | `#6A1B9A` | `#F3E5F5` | `#8E24AA` | Stock negativo: error de datos |
| INACTIVO | `#555F6D` | `#F3F4F6` | `#9CA3AF` | Descontinuado |

Contraste: todas las combinaciones de texto (semáforo, botones, textos secundarios `muted`/`faint` sobre
blanco, gris de cálculo y lienzo) superan 4,5:1 (WCAG 2.1 AA, texto normal), calculado con la fórmula de
luminancia relativa. Mínimos: BAJO 4,51 · ÓPTIMO 4,56 · `faint` sobre gris de cálculo 4,70. En los gráficos las
etiquetas son blancas sobre los tonos de la columna *Gráfico* (≥ 4,98:1) y tinta sobre el ámbar de BAJO.

## 3. Tipografía

| Rol | Fuente | Tamaño |
|---|---|---|
| Título del héroe | Segoe UI Semibold | 18 pt |
| Título de hoja | Segoe UI Semibold | 16 pt |
| Valor de tarjeta KPI | Segoe UI Semibold | 22 pt |
| Cuerpo / celdas | Segoe UI | 10 pt |
| Encabezado de columna | Segoe UI **negrita** | 9,5 pt |
| Etiquetas, notas, chips | Segoe UI | 8,5–9 pt |

Segoe UI es la fuente de sistema de Windows (sensación nativa). En macOS Excel la sustituye automáticamente.

## 4. Retícula y anatomía

**Portada** (`00_PORTADA`): margen 24 px + 12 columnas × 94 px (1.176 px útiles) + margen 24 px, a 100 % de zoom
(cabe en pantallas de 1.366 px). Filas en píxeles fijos. Orden vertical:
héroe (82 px) → banda **Próximo paso** (44 px) → 8 mosaicos (86 px) → 8 tarjetas KPI (2 × 100 px) → 4 gráficos
(2 × 290 px) → últimos 8 movimientos → pie.

**Hojas de datos** (`04`, `05`, `10`, `13`, `15`, `16`):

| Fila | Alto | Contenido |
|---|---|---|
| 1–4 | 70 px | Franja `ink`: título, subtítulo y navegación (8 píldoras con icono) |
| 5 | 34 px | Barra de herramientas: contadores (chips), botón de acción, leyenda ✎/ƒx, regla de la hoja |
| 6 | 16 px | Distintivos por columna: `✎` (ingreso) o `ƒx` (cálculo) |
| 7 | 34 px | Encabezados de tabla (con filtro) |
| 8+ | 20 px | Datos |

**Hojas de trabajo** (`12_REGISTRO`, `17_KARDEX`, `18_PEDIDO`): misma franja y barra; debajo, un lienzo en dos zonas
(datos a la izquierda, resultado o vista previa a la derecha) y, si aplica, una tabla de resultados con encabezado
inmovilizado. `18_PEDIDO` tiene además un encabezado imprimible (empresa, N.º de pedido, fecha, total, firmas).

Filas 1–7 inmovilizadas: la navegación y los encabezados siempre están a la vista.

## 5. Navegación

```text
┌──────────────────────────────────── 00_PORTADA ─────────────────────────────────────┐
│ PRÓXIMO PASO: «3 producto(s) agotado(s): prepare hoy el pedido sugerido»  [Ver pedido ➜] │
│ [REGISTRAR] [CONSULTAR] [STOCK] [ALERTAS (n)] [PEDIDO] [CONTEO] [CATÁLOGO] [GUÍA]        │
└────┬────────────┬─────────┬────────┬────────────┬────────┬─────────┬──────────┬───────┘
     ▼            ▼         ▼        ▼            ▼        ▼         ▼          ▼
 12_REGISTRO   17_KARDEX 15_STOCK 16_ALERTAS  18_PEDIDO 13_CONTEO 05_PRODUCTOS 99_AYUDA
 (Plus) o fila
 libre de 10_MOVIMIENTOS (Estándar)
```

- Todas las hojas visibles comparten la barra de 8 píldoras con icono
  `Inicio · Registrar · Bitácora · Stock · Alertas · Consultar · Pedido · Guía`; la píldora activa es blanca.
- **Próximo paso:** la banda de la portada muestra la acción más urgente (errores → configuración → catálogo →
  saldo inicial → registros incompletos → conteo en curso → agotados → reposición → sin rotación → todo en orden),
  con color de severidad y un botón que lleva al lugar exacto (`irSiguientePaso`).
- **Navegación dinámica** (nombres `ir*` que se recalculan): `irRegistrar` abre el formulario (Plus) o la siguiente
  fila libre de la bitácora (Estándar); `irBitacora`, `irFilaLibreProd` y `irFilaLibreProv` aterrizan en la siguiente
  fila libre, ya resaltada; `irPrimerError` lleva al primer registro con `✖`.
- **Guía por perfil:** `99_AYUDA` abre con «¿Qué necesita hacer hoy?» (Bodega, Compras, Gerencia, Administración),
  cada perfil con sus dos accesos directos, y una lista de **primeros pasos** que se marca sola (✔ / ○).
- **Doble clic (Plus):** en `16_ALERTAS` o `18_PEDIDO` abre el formulario listo para la reposición (ENTRADA con la
  cantidad sugerida); en `15_STOCK` o en un registro de `10_MOVIMIENTOS`, la consulta del producto.
- Las tarjetas KPI también son botones. Hacer clic en celdas de la portada no hace nada (selección deshabilitada).

## 6. Componentes

| Componente | Implementación | Especificación |
|---|---|---|
| Mosaico (tile) | Forma redondeada + icono PNG superpuesto | 130 × 86 px, color de sección, icono blanco 30 px, texto 9,5 pt abajo, sombra |
| Banda «Próximo paso» | Celdas combinadas con formato condicional + botón blanco | Fondo/texto por severidad (rojo, ámbar, azul, verde); botón con texto vinculado (`txtPasoBoton`) |
| Tarjeta KPI | Forma blanca + barra de acento + 3 cuadros de texto vinculados | 270 × 100 px; título 8,5 pt `muted`; valor 22 pt color semántico; nota 8,5 pt |
| Píldora de navegación | Cuadro de texto con hipervínculo + icono PNG | 104 × 26 px, radio completo, icono 16 px a la izquierda |
| Botón de acción | Forma `brand` con hipervínculo o macro (Plus) | alto 28 px, texto blanco semibold; en el formulario 48 px (REGISTRAR verde) |
| Campo de formulario | Celdas combinadas ✎ con validación y mensaje de entrada | etiqueta a la derecha (`*` rojo = obligatorio, «(opcional)» gris); borde rojo si hay error, ámbar si falta la observación de un ajuste |
| Vista previa | Tarjeta blanca de celdas con fórmulas | stock actual → después (24 pt), semáforo antes/después, 7 validaciones `✔ ○ ✖` y resumen en verde/ámbar |
| Aviso «sin macros» (Plus) | Forma ámbar | visible solo si las macros están deshabilitadas (el VBA la oculta al abrir) |
| Chip / leyenda | Cuadro de texto (estático o vinculado a celda) | alto 20–24 px, radio completo |
| Marco de gráfico | Forma blanca con sombra detrás del gráfico | radio 4 %, sin borde |
| Gráficos | Nativos de Excel | Sin borde; etiquetas de datos en lugar de ejes de valores |
| Tabla | Tabla de Excel sin estilo + formatos propios | Encabezados azul (✎) / pizarra (ƒx), filtro activo |

Las formas redondeadas, sombras y el orden de capas (marcos detrás de gráficos, iconos encima de los mosaicos) se
aplican en el post-proceso OOXML del generador (etiquetas `[tile]`, `[card]`, `[bg]`, `[icon]`, etc.).

## 7. Celdas de ingreso vs. cálculo

| | Ingreso ✎ | Cálculo ƒx |
|---|---|---|
| Fondo | `#FFFFFF` | `#F1F4F8` |
| Borde | `#8FB3DE` (azul suave) | `#E2E8F0` |
| Encabezado | `#1565C0` | `#475569` |
| Protección | Desbloqueada | Bloqueada (y oculta en Release) |
| Teclado | `Tab` salta entre celdas ✎ y omite las ƒx | — |

La **siguiente fila libre** se pinta con `brand_lt` (`#EAF3FE`) para guiar al operador.

## 8. Microcopy

- Español neutro, **imperativo y corto**: "Seleccione…", "Escriba…", "Revise…".
- Mensajes de entrada: título ≤ 32 caracteres, cuerpo ≤ 255 (límite de Excel). Explican *qué* y *un ejemplo*.
- Mensajes de error: dicen *qué está mal* y *cómo corregirlo*; nunca culpan al usuario.
- Estados de registro: `✔ Registrado`, `⚠ Incompleto`, `⚠ Justifique el ajuste`, `✖ Stock insuficiente`…
  (el símbolo inicial permite filtrar y colorear).
- Números: formato de celda (`#.##0`, `$ #.##0`, `dd/mm/aaaa`). Nunca `TEXTO()` con códigos (dependen del idioma).

## 9. Accesibilidad

- El estado nunca se comunica solo con color: siempre hay texto (`AGOTADO`, `✖ …`).
- Contraste AA (≥ 4,5:1) en semáforo, botones y textos secundarios (ver sección 2).
- Formas e imágenes con texto alternativo (descripción) para lectores de pantalla.
- Tamaño mínimo de texto: 8,5 pt.

## 10. Activos gráficos

| Ruta | Uso |
|---|---|
| `assets/icons/*.svg` | Iconos fuente (rejilla 24 × 24, trazo 2 px, `currentColor`) |
| `assets/icons/*-blanco@2x.png` / `*-tinta@2x.png` | Iconos rasterizados para Excel (sobre color / sobre claro) |
| `assets/branding/zp-*.png/.svg` | Marca Z&P (insignia y logotipo claro/oscuro) |
| `assets/branding/tenant-logo-placeholder@2x.png` | Espacio del logo del cliente en la portada (480 × 144 px) |

**Personalizar el logo del cliente:** reemplace `tenant-logo-placeholder@2x.png` por el logo real (PNG transparente
de 480 × 144 px) y regenere, o en el archivo del cliente use *Cambiar imagen* sobre el placeholder (con la hoja
desprotegida). Regenerar los activos: `python tools/make_assets.py`.

## 11. Limitaciones conocidas de Excel (y cómo se resuelven)

| Mandato | Limitación | Solución V1 |
|---|---|---|
| Ocultar barra de fórmulas | Es una opción de **aplicación**, no se guarda en `.xlsx` | Edición Plus (`.xlsm`, `modAppMode`); en `.xlsx` las fórmulas quedan **ocultas** en Release |
| Botones con color dinámico | Las formas no admiten formato condicional | Colores semánticos fijos por tarjeta; el valor sí es dinámico (texto vinculado) |
| Tablas en hojas protegidas | No se expanden solas | Capacidad pre-asignada (500 productos / 100 proveedores / 5.000 movimientos) |
| Inmutabilidad de filas | Excel no puede bloquear una fila "al guardarla" sin VBA | Protección + columna `Estado` + regla de corrección por AJUSTE; en Plus, **sellado** VBA (filas registradas bloqueadas y en gris) |
| Formulario de captura | Un formulario de celdas no puede "enviar" sin VBA | Edición Plus: botón REGISTRAR (VBA) que escribe en la bitácora y comprueba su `Estado`; en Estándar se registra en la bitácora |
| Búsqueda en listas desplegables | La validación de datos no filtra mientras se escribe | Campo `Buscar` que filtra la lista (`lfForm`, `lfKardex`) al pulsar Enter |
| Referencias a tablas en formatos condicionales | Excel rechaza el libro completo | Nombres definidos; el generador bloquea el build si aparece una (regla R-08) |

## 12. Libro colaborativo (V2) en Excel para la web

La V2 conserva la paleta, la tipografía y los principios de esta guía, con estas diferencias para la web y la coautoría
(detalle normativo en `.claude/v2-concurrency-rules.md`):

| Tema | V1.2 (escritorio) | V2 (Microsoft 365) |
|---|---|---|
| Botones y navegación | Formas con hipervínculo e iconos PNG | **Celdas** con hipervínculo interno (o `HIPERVINCULO`); iconos como caracteres (⌂ ⇩ ⇧ ▦ ⚠ ☰) |
| Tarjetas e indicadores | Cuadros de texto vinculados a celdas | Celdas combinadas con fórmula (formato grande y barra de color superior) |
| Botones de acción | VBA (edición Plus) | Botones de **Office Script** que agrega Excel sobre los recuadros amarillos punteados `⚙` |
| Portadas | Una portada general | **Por rol**: Bodega (entradas, ajustes, alertas críticas) y Ventas (salidas, disponibilidad) |
| Registro | Siguiente fila libre o formulario | **Fila de captura propia** por usuario (su nombre en la primera columna) y columna *Resultado* que escribe el script |
| Error de stock | Validación `✖ Stock insuficiente` | Además: **rojo sangre `#8A0303` con texto blanco tachado** en la cantidad (poka-yoke) |
| Stock | Proyección en vivo | Instantánea con barra de frescura: «Calculado el … por …» y «⚠ N movimiento(s) nuevo(s): recalcule» |
| Consulta (2.1) | `17_KARDEX` con un selector | `17_CONSULTA`: **una fila por persona** con su selector; la ficha ocupa la fila (disponible exacto, semáforo, cobertura, último movimiento) |
| Toma física (2.1) | Formulario de conteo | `13_CONTEO`: columna *Conteo* azul (✎) por producto, *Resultado* con ▲ sobrante (azul), ▼ faltante (ámbar), ● saldo inicial (morado), ✔ cuadra (verde), ✖ (rojo); confirmación `SI` antes del botón |
| Gerencia (2.1) | Tablero único | `00_PORTADA_GERENCIA`: accesos, dos filas de tarjetas, dos gráficos nativos, top 10 y actividad por usuario (bloqueos en rojo) |
| Auditoría (2.1) | — | `14_ACTIVIDAD`: ✔ en verde, ✖ en rojo, chip de bloqueos del mes en rojo si hay alguno |

- Barra de navegación: fila 5, fondo `ink2`; la hoja activa se marca en blanco. Las píldoras ocupan columnas completas
  (mínimo ~80 px) porque una celda no puede partirse. Desde la 2.1 son 10 destinos (Bodega, Ventas, Gerencia, Entradas,
  Salidas, Consulta, Stock, Pedido, Conteo, Guía); si una hoja es angosta, el generador agrega columnas de margen
  (`con_margen`) en lugar de encimar píldoras.
- Encabezados con filtro: el ancho de columna deja ~20 px para el botón de filtro (el texto no debe cortarse).
- Los recuadros `⚙` indican dónde va cada botón de script y explican qué hacer si aún no está instalado.
- Nada de formas con vínculo, imágenes clicables ni cuadros vinculados: en la web no son fiables. Los gráficos nativos
  sí se usan (dona de salud del inventario, columnas de unidades despachadas).

