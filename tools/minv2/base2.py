"""
M-INV V2 · Núcleo del generador colaborativo (Excel para la web + Office Scripts).

Diferencias de fondo con la V1 (ver .claude/v2-concurrency-rules.md):
  - Escritura fragmentada (sharding): 10A_ENTRADAS (Bodega) y 10B_SALIDAS (Ventas). En cada hoja, una zona de
    CAPTURA con una fila por usuario (nadie escribe en la celda de otro) y la BITÁCORA OFICIAL, que solo escriben los
    Office Scripts (agregar fila = operación atómica del servidor) con Usuario_O365 y Timestamp ocultos.
  - Lectura a demanda: 15_STOCK y 16_ALERTAS son una instantánea (valores) que reconstruye RecalcularStock.ts.
  - Interfaz solo de celdas: en Excel para la web los vínculos de formas y los cuadros vinculados no son fiables;
    los botones y la navegación son celdas con hipervínculo y los botones de script los agrega Excel.
"""
from __future__ import annotations

from xlsxwriter.utility import xl_col_to_name

from minv.base import (C, FONT_SB, S_ALERT, S_AYUDA, S_CAT, S_CONFIG, S_KPI, S_LISTAS, S_PROD, S_PROV,  # noqa: F401
                       S_STOCK, S_UNI, Col, Ctx, Styles, check_msg, q)

VERSION2 = "2.1.0"
EDICION2 = "Colaborativa (Microsoft 365)"

S_PB, S_PV, S_PG = "00_PORTADA_BODEGA", "00_PORTADA_VENTAS", "00_PORTADA_GERENCIA"
S_USR = "02_USUARIOS"
S_ENT, S_SAL = "10A_ENTRADAS", "10B_SALIDAS"
S_CONTEO, S_ACT = "13_CONTEO", "14_ACTIVIDAD"
S_CONS, S_PED = "17_CONSULTA", "18_PEDIDO"
S_SES = "92_SESION"
HIDDEN2 = (S_CONFIG, S_CAT, S_UNI, S_LISTAS, S_KPI, S_SES)

# Capacidades
MAX_USR = 50          # usuarios autorizados (02_USUARIOS)
MAX_CAPT = 15         # filas de captura por hoja de escritura (una por usuario del rol)
MAX_CONS = 20         # filas de consulta (una por usuario operativo)

# Filas Excel (1-based) de las hojas de escritura: captura arriba, bitácora oficial debajo
CAP_HDR = 8
CAP_FIRST = CAP_HDR + 1
CAP_LAST = CAP_FIRST + MAX_CAPT - 1
LED_HDR = CAP_LAST + 4
LED_FIRST = LED_HDR + 1

T_CAPT = {S_ENT: "tblCapturaEntradas", S_SAL: "tblCapturaSalidas"}
T_LED = {S_ENT: "tblEntradas", S_SAL: "tblSalidas"}
PREFIJO = {S_ENT: "E", S_SAL: "S"}

ROLES = ("ADMIN", "BODEGA", "VENTAS", "CONSULTA")
ROLES_DOMINIO = {S_ENT: ("BODEGA", "ADMIN"), S_SAL: ("VENTAS", "ADMIN")}
ROLES_CONSULTA = ("ADMIN", "BODEGA", "VENTAS")          # CONSULTA es solo lectura en SharePoint
TIPOS_BODEGA = ("ENTRADA", "SALDO INICIAL", "AJUSTE (+)", "AJUSTE (-)")
ESTADO_OK = "✔ Consolidado"

ROJO_SANGRE = "#8A0303"

# ---------------------------------------------------------------------------
# Columnas de las hojas de escritura. Captura y bitácora comparten las columnas de hoja B..P: las técnicas
# (N, O, P) se ocultan para ambas tablas, así ocultar Usuario_O365/Timestamp no oculta nada de la captura.
# ---------------------------------------------------------------------------
TX_WIDTHS = [16, 168, 112, 92, 270, 96, 118, 180, 120, 240, 240, 74, 106, 230, 90, 140, 16]
TX_HIDDEN = (13, 14, 15)          # N, O, P (0-based)

LEDGER_COLS = ["ID", "Tipo", "Fecha", "Producto", "Cantidad", "Documento", "Observaciones", "CantidadNeta",
               "Estado", "Registró", "Unidad", "FactorStock", "Usuario_O365", "SKU", "Timestamp"]
CAPTURE_COLS = ["Usuario", "Tipo", "Fecha", "Producto", "Cantidad", "Documento", "Observaciones", "Disponible",
                "Validación", "Resultado", "Unidad", "Factor", "Correo", "SKU", "Queda"]
CAPTURE_INPUTS = {S_ENT: ("Tipo", "Fecha", "Producto", "Cantidad", "Documento", "Observaciones"),
                  S_SAL: ("Fecha", "Producto", "Cantidad", "Documento", "Observaciones")}


def cl(idx0: int) -> str:
    """Letra de columna (índice 0-based de hoja)."""
    return xl_col_to_name(idx0)


def ccol(name: str) -> str:
    """Letra de columna de un campo de la captura (B = primera)."""
    return cl(1 + CAPTURE_COLS.index(name))


def lcol(name: str) -> str:
    """Letra de columna de un campo de la bitácora (B = primera)."""
    return cl(1 + LEDGER_COLS.index(name))


# ---------------------------------------------------------------------------
# Navegación (celdas con hipervínculo: funcionan en Excel para la web y en el escritorio)
# ---------------------------------------------------------------------------
NAV2 = [  # clave, etiqueta, hoja destino, celda destino, ayuda
    ("bodega", "⌂ Bodega", S_PB, "A1", "Portada de Bodega"),
    ("ventas", "⌂ Ventas", S_PV, "A1", "Portada de Ventas"),
    ("gerencia", "◈ Gerencia", S_PG, "A1", "Portada de Gerencia: indicadores, pedido y actividad"),
    ("entradas", "⇩ Entradas", S_ENT, f"{ccol('Tipo')}{CAP_FIRST}", "Registrar entradas y ajustes (Bodega)"),
    ("salidas", "⇧ Salidas", S_SAL, f"{ccol('Producto')}{CAP_FIRST}", "Registrar salidas (Ventas)"),
    ("consulta", "⌕ Consulta", S_CONS, "C9", "Consultar un producto en su fila"),
    ("stock", "▦ Stock", S_STOCK, "A1", "Stock (instantánea recalculada a demanda)"),
    ("pedido", "✚ Pedido", S_PED, "A1", "Pedido sugerido por proveedor"),
    ("conteo", "✓ Conteo", S_CONTEO, "I9", "Toma física (conteo) colaborativa"),
    ("guia", "? Guía", S_AYUDA, "A1", "Guía de uso, acceso e instalación"),
]


def link(sheet: str, cell: str = "A1") -> str:
    return f"internal:{q(sheet)}!{cell}"


def pill_spans(widths: list[int], n: int, first: int = 1, min_w: int = 80) -> list[tuple[int, int]]:
    """Agrupa columnas consecutivas para que cada píldora de navegación mida al menos `min_w` píxeles."""
    spans, c = [], first
    for _ in range(n):
        start, acc = c, 0
        while acc < min_w:
            if c >= len(widths) - 1:
                raise ValueError(f"la navegación no cabe en la hoja ({n} píldoras desde la columna {first})")
            acc += widths[c]
            c += 1
        spans.append((start, c - 1))
    return spans


def con_margen(widths: list[int], start: int = 1, ancho: int = 96) -> list[int]:
    """Agrega columnas de margen (antes del borde derecho) hasta que quepa la barra de navegación."""
    w = list(widths)
    while True:
        try:
            pill_spans(w, len(NAV2), start)
            return w
        except ValueError:
            w.insert(len(w) - 1, ancho)


def lienzo(ws, widths: list[int], zoom: int = 100, hidden_cols=()):
    """Hoja tipo aplicación: sin cuadrícula ni encabezados; columnas y filas sobrantes ocultas."""
    ws.hide_gridlines(2)
    ws.hide_row_col_headers()
    ws.set_zoom(zoom)
    for i, w in enumerate(widths):
        ws.set_column_pixels(i, i, w, None, {"hidden": True} if i in hidden_cols else None)
    ws.set_column(len(widths), 16383, None, None, {"hidden": True})
    ws.set_default_row(hide_unused_rows=True)


def franja(ctx: Ctx, ws, widths: list[int], titulo: str, subtitulo: str, activo: str | None, nav: bool = True,
           nav_start: int = 1):
    """Filas 1-4: título y subtítulo sobre fondo tinta. Fila 5: barra de navegación de celdas."""
    st = ctx.st
    ink = st(bg_color=C["ink"])
    for r, h in ((0, 6), (1, 30), (2, 20), (3, 6)):
        ws.set_row_pixels(r, h, ink)
    ws.write_string(1, 1, titulo, st(bg_color=C["ink"], font_color=C["white"], font_name=FONT_SB, font_size=16))
    ws.write_string(2, 1, subtitulo, st(bg_color=C["ink"], font_color=C["band_sub"], font_size=9))
    if not nav:
        return
    ws.set_row_pixels(4, 28, st(bg_color=C["ink2"]))
    spans = pill_spans(widths, len(NAV2), nav_start)
    for (key, label, sheet, cell, tip), (c0, c1) in zip(NAV2, spans):
        on = key == activo
        f = st(font_name=FONT_SB, font_size=9, align="center", bg_color=C["white"] if on else C["ink2"],
               font_color=C["ink"] if on else C["nav_text"], **({"border": 2, "border_color": C["ink2"]} if on else {}))
        if c1 > c0:
            ws.merge_range(4, c0, 4, c1, "", f)
        ws.write_url(4, c0, link(sheet, cell), f, string=label, tip=tip)


def barra(ctx: Ctx, ws, row0: int = 5, height: int = 30):
    ws.set_row_pixels(row0, height, ctx.st(bg_color=C["canvas"]))


def celda(ws, st: Styles, row0: int, c0: int, c1: int, value, fmt, formula: bool = False):
    """Escribe texto o fórmula en una celda o rango combinado."""
    if c1 > c0:
        ws.merge_range(row0, c0, row0, c1, "", fmt)
    if formula:
        ws.write_formula(row0, c0, value, fmt)
    else:
        ws.write_string(row0, c0, value, fmt)


def chip(ctx: Ctx, ws, row0: int, c0: int, c1: int, formula: str, color=None):
    """Contador (texto calculado) sobre la barra de herramientas."""
    f = ctx.st(font_size=9, bold=True, align="center", font_color=color or C["text"], bg_color=C["white"],
               border=1, border_color=C["border"], formula=True)
    celda(ws, ctx.st, row0, c0, c1, formula, f, formula=True)


def boton(ctx: Ctx, ws, row0: int, c0: int, c1: int, texto: str, sheet: str, cell: str = "A1", fill=None,
          tip: str | None = None, rows: int = 1, size: float = 10):
    """Botón de navegación: celda (o rango) con hipervínculo interno."""
    f = ctx.st(font_name=FONT_SB, font_size=size, align="center", valign="vcenter", text_wrap=True,
               font_color=C["white"], bg_color=fill or C["brand"], border=2, border_color=C["white"])
    if c1 > c0 or rows > 1:
        ws.merge_range(row0, c0, row0 + rows - 1, c1, "", f)
    ws.write_url(row0, c0, link(sheet, cell), f, string=texto, tip=tip or texto)


def marcador_script(ctx: Ctx, ws, row0: int, c0: int, c1: int, etiqueta: str, script: str, rows: int = 1):
    """Lugar reservado para el botón de un Office Script (lo agrega Excel: Automatizar › script › Agregar en el libro).

    El botón real flota sobre este rango; si aún no se instaló, el texto explica qué falta.
    """
    f = ctx.st(font_size=8.5, bold=True, align="center", valign="vcenter", text_wrap=True, font_color=C["amber"],
               bg_color=C["amber_lt"], border=8, border_color=C["amber_mid"])
    txt = f"⚙ Botón «{etiqueta}» · Office Script {script}.ts (si no lo ve, instálelo: Guía › Instalación)"
    if c1 > c0 or rows > 1:
        ws.merge_range(row0, c0, row0 + rows - 1, c1, "", f)
    ws.write_string(row0, c0, txt, f)


def seccion(ctx: Ctx, ws, row0: int, col: int, texto: str, bg=None, height: int = 22):
    st = ctx.st
    bg = bg or C["canvas"]
    ws.set_row_pixels(row0, height, st(bg_color=bg))
    ws.write_rich_string(row0, col, st(font_color=C["brand"], bold=True, font_size=10, bg_color=bg), "▍ ",
                         st(font_color=C["muted"], bold=True, font_size=9, bg_color=bg), texto, st(bg_color=bg))


def tabla(ctx: Ctx, ws, name: str, cols: list[Col], hdr_row: int, nrows: int, badges: bool = True,
          autofilter: bool = True, header_tips: bool = True):
    """Tabla de Excel con encabezado en la fila `hdr_row` (1-based) y `nrows` filas de datos (mínimo 1)."""
    from minv.base import header_tip
    st = ctx.st
    ctx.tables[name] = cols
    h0 = hdr_row - 1
    columns = []
    for c in cols:
        spec = {"header": c.name, "header_format": st.hdr("in" if c.kind == "in" else "calc")}
        if c.formula:
            spec["formula"] = c.formula
            spec["format"] = st.cell(c.kind if c.kind == "in" else "calc", c.fmt)
        columns.append(spec)
    ws.add_table(h0, 1, h0 + max(1, nrows), len(cols), {"name": name, "columns": columns, "style": None,
                                                       "autofilter": autofilter})
    ws.set_row_pixels(h0, 32)
    for i, c in enumerate(cols):
        if badges:
            ws.write_string(h0 - 1, 1 + i, "✎" if c.kind == "in" else "ƒx", st.badge("in" if c.kind == "in" else "calc"))
        if header_tips and c.help:
            glyph = "✎" if c.kind == "in" else "ƒx"
            header_tip(ws, h0, 1 + i, f"{glyph} {c.name}"[:32], c.help)


def fecha_txt(x: str) -> str:
    """dd/mm/aaaa como texto sin TEXTO() (independiente del idioma de Excel, regla R-08)."""
    return f'RIGHT("0"&DAY({x}),2)&"/"&RIGHT("0"&MONTH({x}),2)&"/"&YEAR({x})'


def hora_txt(x: str) -> str:
    return f'RIGHT("0"&HOUR({x}),2)&":"&RIGHT("0"&MINUTE({x}),2)'


def rojo_sangre(st: Styles):
    """Poka-yoke: rojo sangre con texto blanco tachado (movimiento que no puede consolidarse)."""
    return st.cf(bg_color=ROJO_SANGRE, font_color="#FFFFFF", font_strikeout=True, bold=True)


def msg(title: str, message: str) -> dict:
    check_msg(title, message)
    return {"input_title": title, "input_message": message}
