# Guía UX/UI · M-INV V1 ("App-like Excel")

Objetivo: que un operador sin conocimientos técnicos perciba M-INV como **software nativo**, no como una hoja de
cálculo, y que le resulte **imposible romper el sistema por accidente**. Todo lo descrito aquí está implementado en
`tools/build_minv.py`; si cambia un token, cambie ambos.

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
héroe (82 px) → mosaicos (92 px) → 8 tarjetas KPI (2 × 100 px) → 4 gráficos (2 × 290 px) → últimos 8 movimientos → pie.

**Hojas de datos** (`05`, `10`, `15`, `16`):

| Fila | Alto | Contenido |
|---|---|---|
| 1–4 | 68 px | Franja `ink`: título, subtítulo y navegación (píldoras) |
| 5 | 34 px | Barra de herramientas: contador, botón de acción, leyenda ✎/ƒx, regla de la hoja |
| 6 | 16 px | Distintivos por columna: `✎` (ingreso) o `ƒx` (cálculo) |
| 7 | 34 px | Encabezados de tabla (con filtro) |
| 8+ | 20 px | Datos |

Filas 1–7 inmovilizadas: la navegación y los encabezados siempre están a la vista.

## 5. Navegación

```text
                 ┌────────────── 00_PORTADA ──────────────┐
                 │ [NUEVO MOVIMIENTO] [VER STOCK] [ALERTAS (n)] [CATÁLOGO] [GUÍA RÁPIDA] │
                 └───┬──────────────┬───────────┬──────────┬──────────┬────┘
        fila libre ▼        ▼           ▼          ▼          ▼
            10_MOVIMIENTOS   15_STOCK    16_ALERTAS  05_PRODUCTOS  99_AYUDA
            (irFilaLibreMov)                          (irFilaLibreProd)
```

- Todas las hojas visibles comparten la barra de píldoras `⌂ Inicio · + Movimientos · ▦ Stock · ⚠ Alertas ·
  ☰ Catálogo · ? Guía`; la píldora activa es blanca.
- **Navegación dinámica:** "Nuevo movimiento" y "Registrar nuevo producto" apuntan a nombres definidos
  (`irFilaLibreMov`, `irFilaLibreProd`) que se recalculan: el cursor aterriza en la siguiente fila libre, ya resaltada.
- Las tarjetas KPI también son botones (llevan a Stock, Alertas o la bitácora).
- Hacer clic en celdas de la portada no hace nada (selección deshabilitada): solo los botones responden.

## 6. Componentes

| Componente | Implementación | Especificación |
|---|---|---|
| Mosaico (tile) | Forma redondeada + icono PNG superpuesto | 212 × 92 px, color de sección, icono blanco 32 px, texto 10,5 pt abajo, sombra |
| Tarjeta KPI | Forma blanca + barra de acento + 3 cuadros de texto vinculados | 270 × 100 px; título 8,5 pt `muted`; valor 22 pt color semántico; nota 8,5 pt |
| Píldora de navegación | Cuadro de texto con hipervínculo | 118 × 28 px, radio completo |
| Botón de acción | Forma `brand` con hipervínculo | alto 28 px, texto blanco semibold |
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
| Ocultar barra de fórmulas | Es una opción de **aplicación**, no se guarda en `.xlsx` | Módulo opcional `src/macros/` (requiere `.xlsm`); en `.xlsx` las fórmulas quedan **ocultas** en Release |
| Botones con color dinámico | Las formas no admiten formato condicional | Colores semánticos fijos por tarjeta; el valor sí es dinámico (texto vinculado) |
| Tablas en hojas protegidas | No se expanden solas | Capacidad pre-asignada (500 productos / 5.000 movimientos) |
| Inmutabilidad de filas | Excel no puede bloquear una fila "al guardarla" sin VBA | Protección + columna `Estado` + regla de corrección por AJUSTE; sellado VBA en backlog V1.1 |
