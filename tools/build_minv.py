#!/usr/bin/env python3
"""
M-INV V1 · Generador del libro Excel (Excel-as-code)
Z&P Software Fast Solutions

Este script ES la fuente de verdad del producto V1. El .xlsx se genera, nunca se
edita a mano (ver .claude/excel-architecture-rules.md, regla R-01).

Salidas:
    src/M-INV_V1_Core.xlsx                        maestro de desarrollo (datos demo)
    releases/M-INV_V1_Produccion_Bloqueado.xlsx   plantilla limpia y blindada para el cliente

Uso:
    .venv\\Scripts\\python tools\\build_minv.py                # Core + Release
    .venv\\Scripts\\python tools\\build_minv.py --solo core
    .venv\\Scripts\\python tools\\build_minv.py --fin-demo 2026-09-25

Variables de entorno:
    MINV_PASSWORD           contraseña de protección del Core (por defecto: "minv-dev")
    MINV_RELEASE_PASSWORD   contraseña del Release (si falta, usa la del Core y avisa)

Arquitectura (CQRS sobre Excel):
    Configuración (oculta)   01_CONFIG · 03_CATEGORIAS · 06_UNIDADES
    Maestro                  05_PRODUCTOS
    Escritura (append-only)  10_MOVIMIENTOS  → cada cambio es un registro con FactorStock (+1/-1)
    Lectura (proyecciones)   15_STOCK · 16_ALERTAS  → SUMAR.SI.CONJUNTO sobre la bitácora
    Presentación             00_PORTADA · 99_AYUDA
    Motor interno (oculto)   90_LISTAS (listas en cascada) · 91_KPIS (indicadores y series)
"""
from __future__ import annotations

import argparse
import datetime as dt
import os
import re
import sys
import zipfile
from dataclasses import dataclass
from pathlib import Path

import xlsxwriter
from xlsxwriter.utility import xl_col_to_name
from xlsxwriter.worksheet import Worksheet

sys.path.insert(0, str(Path(__file__).resolve().parent))
import demo_data as D  # noqa: E402

# ---------------------------------------------------------------------------
# Constantes del producto
# ---------------------------------------------------------------------------
ROOT = Path(__file__).resolve().parents[1]
VERSION = "1.0.0"
DEV_PASSWORD = "minv-dev"
OUT_CORE = ROOT / "src" / "M-INV_V1_Core.xlsx"
OUT_RELEASE = ROOT / "releases" / "M-INV_V1_Produccion_Bloqueado.xlsx"
ICONS = ROOT / "assets" / "icons"
BRAND = ROOT / "assets" / "branding"

MAX_PROD = 500     # capacidad del catálogo (filas pre-asignadas)
MAX_MOV = 5000     # capacidad de la bitácora (filas pre-asignadas)
HDR = 7            # fila Excel del encabezado de las tablas
FIRST = 8          # primera fila Excel de datos
LAST_PROD = FIRST + MAX_PROD - 1
LAST_MOV = FIRST + MAX_MOV - 1

FONT = "Segoe UI"
FONT_SB = "Segoe UI Semibold"

S_PORTADA, S_CONFIG, S_CAT, S_PROD, S_UNI = "00_PORTADA", "01_CONFIG", "03_CATEGORIAS", "05_PRODUCTOS", "06_UNIDADES"
S_MOV, S_STOCK, S_ALERT = "10_MOVIMIENTOS", "15_STOCK", "16_ALERTAS"
S_LISTAS, S_KPI, S_AYUDA = "90_LISTAS", "91_KPIS", "99_AYUDA"

C = {
    "ink": "#0B1F33", "ink2": "#16324F", "nav": "#1C3A5A", "nav_text": "#DCE7F3", "band_sub": "#9FB3C8",
    "brand": "#1565C0", "brand_dk": "#0D47A1", "brand_lt": "#EAF3FE", "canvas": "#F3F5F8", "white": "#FFFFFF",
    "border": "#DDE3EA", "text": "#1F2937", "muted": "#5F6B7A", "faint": "#5F6E84",
    "input_border": "#8FB3DE", "calc_fill": "#F1F4F8", "calc_text": "#334155", "calc_border": "#E2E8F0",
    "hdr_in": "#1565C0", "hdr_calc": "#475569", "row_line": "#E5E7EB", "zebra": "#F8FAFC",
    "green": "#2E7D32", "green_lt": "#E8F5E9", "red": "#C62828", "red_lt": "#FDECEA",
    "amber": "#B45309", "amber_lt": "#FEF3C7", "amber_mid": "#F59E0B", "teal": "#00796B", "purple": "#5E35B1",
}

# Semáforo: (Estado, Prioridad, EsAlerta, color texto, color fondo, color gráfico, regla, acción)
ESTADOS = [
    ("INCONSISTENTE", 1, "SI", "#6A1B9A", "#F3E5F5", "#8E24AA",
     "Stock negativo: se registraron salidas mayores a lo disponible.", "Auditar la bitácora y registrar un AJUSTE"),
    ("AGOTADO", 2, "SI", "#FFFFFF", "#B71C1C", "#9B1C1C",
     "Stock en cero.", "Reabastecer de inmediato"),
    ("CRÍTICO", 3, "SI", "#C62828", "#FDECEA", "#D32F2F",
     "Stock en o por debajo del mínimo.", "Emitir orden de compra"),
    ("BAJO", 4, "SI", "#B45309", "#FEF3C7", "#F59E0B",
     "Stock cercano al mínimo (dentro del margen de alerta).", "Programar reposición"),
    ("ÓPTIMO", 5, "NO", "#2E7D32", "#E8F5E9", "#2E7D32",
     "Stock entre el margen de alerta y el máximo.", "Sin acción"),
    ("SOBRESTOCK", 6, "SI", "#1565C0", "#E3F2FD", "#1565C0",
     "Stock por encima del máximo.", "Frenar compras / rotar inventario"),
    ("INACTIVO", 7, "NO", "#555F6D", "#F3F4F6", "#9CA3AF",
     "Producto descontinuado (Activo = NO).", "Sin acción"),
]
EST = {e[0]: e for e in ESTADOS}

# Formatos de celda por tipo de dato
FMT = {
    "text": {"align": "left", "indent": 1},
    "center": {"align": "center"},
    "date": {"align": "center", "num_format": "dd/mm/yyyy"},
    "qty": {"align": "right", "num_format": "#,##0", "indent": 1},
    "signed": {"align": "right", "num_format": "+#,##0;-#,##0;0", "indent": 1},
    "factor": {"align": "center", "num_format": "+0;-0;0", "bold": True},
    "money": {"align": "right", "num_format": "$ #,##0", "indent": 1},
    "pct": {"align": "right", "num_format": "0%", "indent": 1},
    "estado": {"align": "left", "bold": True, "indent": 1},  # el punto "● " lo añade el formato condicional
    "status": {"align": "left", "bold": True, "indent": 1},
    "id": {"align": "center", "font_color": C["faint"]},
    "strong": {"align": "right", "num_format": "#,##0", "bold": True, "indent": 1},
}


# ---------------------------------------------------------------------------
# Utilidades
# ---------------------------------------------------------------------------
class Styles:
    """Fábrica de formatos con caché. En modo release oculta las fórmulas (Protección > Oculta)."""

    def __init__(self, wb: xlsxwriter.Workbook, hide_formulas: bool):
        self.wb, self.hide, self._cache, self._cf = wb, hide_formulas, {}, {}

    def __call__(self, **p):
        props = {"font_name": FONT, "font_size": 10, "font_color": C["text"], "valign": "vcenter"}
        props.update(p)
        if props.pop("formula", False) and self.hide:
            props["hidden"] = True
        key = tuple(sorted(props.items()))
        if key not in self._cache:
            self._cache[key] = self.wb.add_format(props)
        return self._cache[key]

    def cf(self, **p):
        key = tuple(sorted(p.items()))
        if key not in self._cf:
            self._cf[key] = self.wb.add_format(p)
        return self._cf[key]

    # --- estilos de tabla
    def hdr(self, kind: str):
        bg = C["hdr_in"] if kind == "in" else C["hdr_calc"]
        return self(bold=True, font_color=C["white"], bg_color=bg, align="center", text_wrap=True,
                    font_size=9.5, border=1, border_color=C["white"])

    def cell(self, kind: str, fmt: str, view: bool = False):
        p = dict(FMT[fmt])
        if kind == "in":
            p.update(bg_color=C["white"], border=1, border_color=C["input_border"], locked=False)
        elif view:
            p.update(formula=True)
        else:
            p.setdefault("font_color", C["calc_text"])
            p.update(bg_color=C["calc_fill"], border=1, border_color=C["calc_border"], formula=True)
        return self(**p)

    def badge(self, kind: str):
        if kind == "in":
            return self(font_size=9, bold=True, font_color=C["brand"], bg_color=C["canvas"], align="center")
        return self(font_size=9, font_color=C["faint"], bg_color=C["canvas"], align="center", italic=True)


@dataclass
class Col:
    name: str                    # encabezado = nombre técnico (referencias estructuradas)
    kind: str                    # "in" (usuario) | "calc" (fórmula de tabla) | "mirror" (fórmula por fila)
    width: int                   # píxeles
    fmt: str = "text"
    help: str = ""               # mensaje de entrada del encabezado (ayuda contextual)
    formula: str | None = None   # fórmula de columna calculada
    mirror: str | None = None    # plantilla por fila con {r}


def nested_if(pairs: list[tuple[str, str]], default: str) -> str:
    expr = default
    for cond, val in reversed(pairs):
        expr = f"IF({cond},{val},{expr})"
    return expr


def this_row(table: str):
    return lambda col: f"{table}[[#This Row],[{col}]]"


def letter(cols: list[Col], name: str) -> str:
    return xl_col_to_name(1 + [c.name for c in cols].index(name))


def col_x(widths: list[int], idx: int) -> int:
    """Posición x (px) del borde izquierdo de la columna idx (0 = A)."""
    return sum(widths[:idx])


def legacy_hash(password: str) -> str:
    return Worksheet._encode_password(None, password)


def q(sheet: str) -> str:
    return f"'{sheet}'"


# ---------------------------------------------------------------------------
# Componentes visuales
# ---------------------------------------------------------------------------
NAV = [
    ("inicio", "⌂ Inicio", f"internal:{q(S_PORTADA)}!A1", "Volver a la portada"),
    ("mov", "+ Movimientos", "internal:irFilaLibreMov", "Registrar un movimiento (siguiente fila libre)"),
    ("stock", "▦ Stock", f"internal:{q(S_STOCK)}!A1", "Ver el stock actual"),
    ("alertas", "⚠ Alertas", f"internal:{q(S_ALERT)}!A1", "Ver productos que requieren acción"),
    ("catalogo", "☰ Catálogo", f"internal:{q(S_PROD)}!A1", "Ver o crear productos"),
    ("ayuda", "? Guía", f"internal:{q(S_AYUDA)}!A1", "Guía rápida de uso"),
]


def textbox(ws, text, x, y, w, h, *, tag="txt", desc="", fill=None, line=None, font=None,
            halign="center", valign="middle", url=None, tip=None, textlink=None, anchor=(0, 0)):
    opts = {
        "x_offset": x, "y_offset": y, "width": w, "height": h, "object_position": 2,
        "fill": {"color": fill} if fill else {"none": True},
        "line": line or {"none": True},
        "font": {"name": FONT, "size": 10, "color": C["text"], **(font or {})},
        "align": {"vertical": valign, "horizontal": halign, "text": halign},
        "description": f"[{tag}] {desc or text}".strip(),
    }
    if url:
        opts["url"] = url
    if tip:
        opts["tip"] = tip
    if textlink:
        opts["textlink"] = textlink
    ws.insert_textbox(anchor[0], anchor[1], text, opts)


def band(ws, st: Styles, title: str, subtitle: str, active: str | None, nav: bool = True, widths=None):
    """Franja superior de la hoja (filas 1-4) + navegación tipo app."""
    ink = st(bg_color=C["ink"])
    for r, h in ((0, 8), (1, 30), (2, 20), (3, 10)):
        ws.set_row_pixels(r, h, ink)
    ws.write_string(1, 1, title, st(bg_color=C["ink"], font_color=C["white"], font_name=FONT_SB, font_size=16))
    ws.write_string(2, 1, subtitle, st(bg_color=C["ink"], font_color=C["band_sub"], font_size=9))
    if not nav:
        return
    w, gap = 118, 6
    x0 = max(520, int(16 + max(len(title) * 10, len(subtitle) * 5.6) + 40))
    if widths:
        assert x0 + len(NAV) * (w + gap) <= sum(widths), f"navegación desborda la hoja '{title}'"
    for i, (key, label, url, tip) in enumerate(NAV):
        on = key == active
        textbox(ws, label, x0 + i * (w + gap), 17, w, 28, tag="pill", desc=f"Navegación: {tip}",
                fill=C["white"] if on else C["nav"],
                font={"name": FONT_SB, "size": 9, "color": C["ink"] if on else C["nav_text"]},
                url=url, tip=tip)


def toolbar_row(ws, st: Styles, height: int = 34):
    canvas = st(bg_color=C["canvas"])
    ws.set_row_pixels(4, height, canvas)
    ws.set_row_pixels(5, 16, canvas)


def header_tip(ws, row, col, title, message):
    assert len(title) <= 32 and len(message) <= 255, (title, len(message))
    ws.data_validation(row, col, row, col, {"validate": "any", "input_title": title, "input_message": message})


def legend_pills(ws, x, y=5):
    """Leyenda de celdas de ingreso vs. cálculo (fila de herramientas)."""
    textbox(ws, "✎  Usted diligencia", x, y, 150, 24, tag="chip", desc="Leyenda: celda de ingreso",
            fill=C["white"], line={"color": C["input_border"], "width": 1},
            font={"size": 8.5, "bold": True, "color": C["brand"]}, anchor=(4, 0))
    textbox(ws, "ƒx  Cálculo automático", x + 158, y, 170, 24, tag="chip", desc="Leyenda: celda calculada",
            fill=C["calc_fill"], line={"color": C["calc_border"], "width": 1},
            font={"size": 8.5, "bold": True, "color": C["calc_text"]}, anchor=(4, 0))
    return x + 158 + 170


def build_table(ws, st: Styles, name: str, cols: list[Col], nrows: int, view: bool = False, tips=True):
    hdr0 = HDR - 1
    columns = []
    for c in cols:
        spec = {"header": c.name, "header_format": st.hdr(c.kind)}
        if c.formula:
            spec["formula"] = c.formula
            spec["format"] = st.cell(c.kind, c.fmt, view)
        columns.append(spec)
    ws.add_table(hdr0, 1, hdr0 + nrows, len(cols), {"name": name, "columns": columns, "style": None,
                                                    "autofilter": True})
    ws.set_row_pixels(hdr0, 34)
    for i, c in enumerate(cols):
        col = 1 + i
        ws.set_column_pixels(col, col, c.width)
        ws.write_string(hdr0 - 1, col, "✎" if c.kind == "in" else "ƒx", st.badge(c.kind))
        if tips and c.help:
            icon = "✎" if c.kind == "in" else "ƒx"
            header_tip(ws, hdr0, col, f"{icon} {c.name}"[:32], c.help)
        if c.kind == "in":
            f = st.cell("in", c.fmt)
            for r in range(nrows):
                ws.write_blank(hdr0 + 1 + r, col, None, f)
        elif c.mirror:
            f = st.cell(c.kind, c.fmt, view)
            for r in range(nrows):
                ws.write_formula(hdr0 + 1 + r, col, c.mirror.format(r=FIRST + r), f)


def app_sheet(ws, widths_px: list[int], zoom=100, headers=False):
    """Lienzo tipo aplicación: sin cuadrícula, sin encabezados, columnas sobrantes ocultas."""
    ws.hide_gridlines(2)
    if not headers:
        ws.hide_row_col_headers()
    ws.set_zoom(zoom)
    ws.set_column_pixels(0, 0, widths_px[0])
    last = len(widths_px) - 1
    ws.set_column_pixels(last, last, widths_px[-1])
    ws.set_column(last + 1, 16383, None, None, {"hidden": True})
    ws.set_default_row(hide_unused_rows=True)


def print_setup(ws, title: str, repeat=True):
    ws.set_landscape()
    ws.set_paper(1)
    ws.fit_to_pages(1, 0)
    ws.set_margins(left=0.4, right=0.4, top=0.7, bottom=0.6)
    if repeat:
        ws.repeat_rows(HDR - 1)
    ws.set_header(f'&L&"Segoe UI,Bold"&9M-INV · {title}&R&"Segoe UI,Regular"&8&D')
    ws.set_footer('&L&"Segoe UI,Regular"&8Z&&P Software Fast Solutions&R&"Segoe UI,Regular"&8Página &P de &N')


# ---------------------------------------------------------------------------
# Hojas de configuración (capa oculta)
# ---------------------------------------------------------------------------
def config_band(ws, st, title, subtitle, widths):
    for i, w in enumerate(widths):
        ws.set_column_pixels(i, i, w)
    band(ws, st, title, subtitle, None, nav=False)
    toolbar_row(ws, st, 24)
    ws.hide_gridlines(2)
    ws.set_zoom(100)


def simple_table(ws, st, first_row, name, headers, rows, formats, tips=None):
    """Tabla de configuración normal (se expande sola al editar con la hoja desprotegida)."""
    cols = [{"header": h, "header_format": st.hdr("calc")} for h in headers]
    ws.add_table(first_row, 1, first_row + max(1, len(rows)), len(headers),
                 {"name": name, "columns": cols, "style": None, "autofilter": False})
    ws.set_row_pixels(first_row, 30)
    for r, row in enumerate(rows):
        for c, val in enumerate(row):
            f = formats[c]
            if isinstance(val, (int, float)):
                ws.write_number(first_row + 1 + r, 1 + c, val, f)
            else:
                ws.write_string(first_row + 1 + r, 1 + c, val, f)
    if tips:
        for c, (t, m) in enumerate(tips):
            if t:
                header_tip(ws, first_row, 1 + c, t, m)


def build_config(wb, ws, st, demo: bool, fin: dt.date):
    config_band(ws, st, "Configuración del sistema",
                "Capa de configuración (oculta) · Solo administrador · Cambios aquí afectan todo el libro",
                [24, 250, 260, 520, 24])
    lbl = st(bold=True, font_color=C["text"], border=1, border_color=C["border"], indent=1)
    val = st(bg_color=C["white"], border=1, border_color=C["input_border"], locked=False, indent=1,
             font_color=C["brand_dk"], bold=True)
    note = st(font_color=C["muted"], font_size=9, border=1, border_color=C["border"], indent=1, text_wrap=True)
    sec = st(bold=True, font_color=C["brand"], font_size=10.5)
    ws.write_string(6, 1, "PARÁMETROS DEL TENANT", sec)
    params = [
        ("cfgEmpresa", "Empresa (cliente)", D.EMPRESA_DEMO if demo else "NOMBRE DE SU EMPRESA", None,
         "Nombre que aparece en la portada."),
        ("cfgNIT", "NIT / Identificación", D.NIT_DEMO if demo else "", None, "Identificación tributaria del cliente."),
        ("cfgBodega", "Sede / Bodega", D.BODEGA_DEMO if demo else "Bodega principal", None,
         "Ubicación física que controla este libro (un libro por bodega en V1)."),
        ("cfgMoneda", "Moneda", "COP", None, "Informativo. Los formatos de valor usan el símbolo $."),
        ("cfgMargenAlerta", "Margen de alerta preventiva", D.MARGEN_ALERTA, "0%",
         "Un producto pasa a BAJO cuando su stock es ≤ Mínimo × (1 + margen)."),
        ("cfgFechaMin", "Fecha mínima permitida", dt.date(2020, 1, 1), "dd/mm/yyyy",
         "Fechas anteriores son rechazadas por la validación de 10_MOVIMIENTOS."),
        ("cfgVersion", "Versión del sistema", VERSION, None, "Versión del motor M-INV."),
        ("cfgFechaBuild", "Fecha de generación", fin, "dd/mm/yyyy", "Fecha en que se generó este libro."),
        ("cfgProveedor", "Implementado por", "Z&P Software Fast Solutions", None, "Proveedor del software."),
    ]
    ws.write_string(7, 1, "Parámetro", st.hdr("calc"))
    ws.write_string(7, 2, "Valor", st.hdr("in"))
    ws.write_string(7, 3, "Descripción", st.hdr("calc"))
    for i, (name, label, value, nf, desc) in enumerate(params):
        r = 8 + i
        f = st(bg_color=C["white"], border=1, border_color=C["input_border"], locked=False, indent=1,
               font_color=C["brand_dk"], bold=True, **({"num_format": nf} if nf else {}))
        ws.write_string(r, 1, label, lbl)
        if isinstance(value, dt.date):
            ws.write_datetime(r, 2, dt.datetime.combine(value, dt.time()), f)
        elif isinstance(value, (int, float)):
            ws.write_number(r, 2, value, f)
        else:
            ws.write_string(r, 2, value, f if value else val)
        ws.write_string(r, 3, desc, note)
        wb.define_name(name, f"={q(S_CONFIG)}!$C${r + 1}")
    ws.data_validation(12, 2, 12, 2, {"validate": "decimal", "criteria": "between", "minimum": 0, "maximum": 1,
                                      "input_title": "Margen de alerta", "input_message": "Valor entre 0% y 100%."})

    r0 = 8 + len(params) + 2
    ws.write_string(r0 - 1, 1, "TIPOS DE MOVIMIENTO  (definen el FactorStock de la bitácora)", sec)
    center = st(align="center", bold=True, border=1, border_color=C["border"])
    txt = st(indent=1, border=1, border_color=C["border"], text_wrap=True)
    simple_table(ws, st, r0, "tblTiposMov", ["Tipo", "FactorStock", "Descripción"],
                 [list(t) for t in D.TIPOS_MOVIMIENTO],
                 [st(bold=True, indent=1, border=1, border_color=C["border"]),
                  st(align="center", bold=True, num_format="+0;-0;0", border=1, border_color=C["border"]), txt])

    r1 = r0 + len(D.TIPOS_MOVIMIENTO) + 3
    ws.write_string(r1 - 1, 1, "SEMÁFORO DE STOCK  (reglas de 15_STOCK y 16_ALERTAS)", sec)
    ws.set_column_pixels(4, 4, 220)
    ws.set_column_pixels(5, 5, 24)
    simple_table(ws, st, r1, "tblEstados", ["Estado", "Prioridad", "Regla", "Acción"],
                 [[e[0], e[1], e[6], e[7]] for e in ESTADOS],
                 [st(bold=True, indent=1, border=1, border_color=C["border"]), center, txt, txt])
    for i, e in enumerate(ESTADOS):
        ws.write_string(r1 + 1 + i, 1, e[0], st(bold=True, indent=1, font_color=e[3], bg_color=e[4],
                                                 border=1, border_color=C["border"]))

    r2 = r1 + len(ESTADOS) + 3
    ws.write_string(r2 - 1, 1, "RESPONSABLES  (lista desplegable de 10_MOVIMIENTOS)", sec)
    gente = D.RESPONSABLES_DEMO if demo else D.RESPONSABLES_BASE
    simple_table(ws, st, r2, "tblResponsables", ["Nombre", "Cargo"], [list(p) for p in gente],
                 [st(bold=True, indent=1, border=1, border_color=C["border"]), txt])
    for r in range(len(gente)):
        ws.set_row_pixels(r2 + 1 + r, 22)


def build_categorias(ws, st, demo: bool):
    config_band(ws, st, "Categorías", "Capa de configuración (oculta) · Primer nivel de la lista en cascada",
                [24, 110, 240, 420, 24])
    cats = D.CATEGORIAS_DEMO if demo else D.CATEGORIAS_BASE
    txt = st(indent=1, border=1, border_color=C["border"])
    simple_table(ws, st, HDR - 1, "tblCategorias", ["Código", "Categoría", "Descripción"], [list(c) for c in cats],
                 [st(bold=True, align="center", border=1, border_color=C["border"]),
                  st(bold=True, indent=1, border=1, border_color=C["border"]), txt],
                 tips=[("Código", "Abreviatura interna (opcional)."),
                       ("Categoría", "Nombre visible en las listas. No lo cambie si ya tiene productos asociados."),
                       ("Descripción", "Texto de apoyo.")])


def build_unidades(ws, st):
    config_band(ws, st, "Unidades de medida", "Capa de configuración (oculta) · Define si la cantidad admite decimales",
                [24, 90, 160, 110, 320, 24])
    txt = st(indent=1, border=1, border_color=C["border"])
    simple_table(ws, st, HDR - 1, "tblUnidades", ["Código", "Unidad", "Decimales", "Descripción"],
                 [list(u) for u in D.UNIDADES],
                 [st(bold=True, align="center", border=1, border_color=C["border"]), txt,
                  st(align="center", border=1, border_color=C["border"]), txt],
                 tips=[("Código", "Abreviatura que ve el operador (UND, KG...)."),
                       ("Unidad", "Nombre completo de la unidad."),
                       ("Decimales", "SI = admite fracciones (KG, LT...). NO = solo enteros."),
                       ("Descripción", "Texto de apoyo.")])


# ---------------------------------------------------------------------------
# 05_PRODUCTOS · Maestro
# ---------------------------------------------------------------------------
def productos_cols() -> list[Col]:
    r = this_row("tblProductos")
    valid = nested_if([
        (f'AND({r("SKU")}="",{r("Producto")}="")', '""'),
        (f'OR({r("SKU")}="",{r("Producto")}="",{r("Categoría")}="",{r("Unidad")}="")', '"⚠ Faltan datos"'),
        (f'COUNTIF(tblProductos[SKU],{r("SKU")})>1', '"✖ SKU duplicado"'),
        (f'ISNUMBER(FIND(" ",{r("SKU")}))', '"✖ SKU con espacios"'),
        (f'ISNA(MATCH({r("Categoría")},lstCategorias,0))', '"✖ Categoría no existe"'),
        (f'ISNA(MATCH({r("Unidad")},lstUniCod,0))', '"✖ Unidad no existe"'),
        (f'AND(N({r("StockMax")})>0,N({r("StockMin")})>N({r("StockMax")}))', '"⚠ Mínimo mayor que máximo"'),
        (f'{r("Activo")}="NO"', '"● Inactivo"'),
    ], '"✔ OK"')
    return [
        Col("SKU", "in", 92, "text", "Código único del producto, sin espacios (ej: FER-001). No lo cambie si ya "
                                     "tiene movimientos: cree uno nuevo y desactive el anterior."),
        Col("Producto", "in", 290, "text", "Nombre descriptivo (3 a 60 caracteres). Es lo que el operador verá en "
                                          "la lista junto al SKU."),
        Col("Categoría", "in", 168, "text", "Seleccione la categoría (Alt + ↓). Las categorías se administran en "
                                           "03_CATEGORIAS."),
        Col("Unidad", "in", 84, "center", "Unidad de medida (UND, CAJA, KG...). Define si se admiten decimales."),
        Col("StockMin", "in", 96, "qty", "Punto de reorden. En o por debajo de este valor el producto queda "
                                        "CRÍTICO."),
        Col("StockMax", "in", 96, "qty", "Nivel máximo deseado. Por encima: SOBRESTOCK. 0 = sin máximo."),
        Col("CostoUnitario", "in", 120, "money", "Costo por unidad (moneda local). Se usa para valorizar el "
                                                "inventario."),
        Col("Ubicación", "in", 96, "center", "Ubicación física en bodega (opcional). Ej: A-01-03."),
        Col("Activo", "in", 66, "center", "Escriba NO para descontinuar: desaparece de las listas pero conserva su "
                                         "historial. Vacío = SI."),
        Col("Etiqueta", "calc", 300, "text", "Texto de la lista desplegable de 10_MOVIMIENTOS: SKU · Producto.",
            formula=f'=IF(OR({r("SKU")}="",{r("Producto")}=""),"",{r("SKU")}&" · "&{r("Producto")})'),
        Col("Movimientos", "calc", 112, "center", "Cantidad de registros de este SKU en la bitácora.",
            formula=f'=IF({r("SKU")}="","",COUNTIF(tblMovimientos[SKU],{r("SKU")}))'),
        Col("Validación", "calc", 176, "status", "Control de calidad del registro maestro.", formula="=" + valid),
    ]


def build_productos(wb, ws, st, demo: bool):
    cols = productos_cols()
    widths = [16] + [c.width for c in cols] + [16]
    app_sheet(ws, widths, zoom=90)
    band(ws, st, "Catálogo de productos",
         "Datos maestros · Cada producto se registra una sola vez · Nunca borre filas (use Activo = NO)",
         "catalogo", widths=widths)
    toolbar_row(ws, st)
    build_table(ws, st, "tblProductos", cols, MAX_PROD)
    L = {c.name: letter(cols, c.name) for c in cols}

    # Validaciones de datos (integridad del maestro)
    rng = lambda n: f"{L[n]}{FIRST}:{L[n]}{LAST_PROD}"  # noqa: E731
    ws.data_validation(rng("SKU"), {
        "validate": "custom",
        "value": f'=AND(LEN({L["SKU"]}{FIRST})>=2,LEN({L["SKU"]}{FIRST})<=20,ISERROR(FIND(" ",{L["SKU"]}{FIRST})),'
                 f'COUNTIF(${L["SKU"]}${FIRST}:${L["SKU"]}${LAST_PROD},{L["SKU"]}{FIRST})=1)',
        "input_title": "SKU (código único)", "input_message": "2 a 20 caracteres, sin espacios, sin repetir. Ej: FER-001.",
        "error_title": "SKU no válido", "error_message": "El SKU debe tener 2 a 20 caracteres, no llevar espacios y no "
                                                        "existir ya en el catálogo."})
    ws.data_validation(rng("Producto"), {
        "validate": "length", "criteria": "between", "minimum": 3, "maximum": 60,
        "input_title": "Nombre del producto", "input_message": "Descripción clara, de 3 a 60 caracteres.",
        "error_title": "Nombre no válido", "error_message": "El nombre debe tener entre 3 y 60 caracteres."})
    ws.data_validation(rng("Categoría"), {
        "validate": "list", "source": "=lstCategorias",
        "input_title": "Categoría", "input_message": "Seleccione de la lista (Alt + ↓).",
        "error_title": "Categoría no válida", "error_message": "Elija una categoría de la lista. Para crear una nueva "
                                                             "contacte al administrador."})
    ws.data_validation(rng("Unidad"), {
        "validate": "list", "source": "=lstUniCod",
        "input_title": "Unidad de medida", "input_message": "Seleccione de la lista (Alt + ↓).",
        "error_title": "Unidad no válida", "error_message": "Elija una unidad de la lista."})
    for n in ("StockMin", "StockMax", "CostoUnitario"):
        ws.data_validation(rng(n), {
            "validate": "decimal", "criteria": ">=", "value": 0,
            "input_title": n, "input_message": "Número mayor o igual a 0.",
            "error_title": "Valor no válido", "error_message": "Escriba un número mayor o igual a 0."})
    ws.data_validation(rng("Ubicación"), {
        "validate": "length", "criteria": "<=", "value": 20,
        "input_title": "Ubicación (opcional)", "input_message": "Pasillo-estante-nivel. Máximo 20 caracteres.",
        "error_title": "Texto muy largo", "error_message": "Máximo 20 caracteres."})
    ws.data_validation(rng("Activo"), {
        "validate": "list", "source": ["SI", "NO"],
        "input_title": "¿Producto activo?", "input_message": "NO = descontinuado (sale de las listas). Vacío = SI.",
        "error_title": "Valor no válido", "error_message": "Escriba SI o NO."})

    # Formato condicional
    inputs = f"{L['SKU']}{FIRST}:{L['Activo']}{LAST_PROD}"
    ws.conditional_format(inputs, {"type": "formula",
                                   "criteria": f'=AND(${L["SKU"]}{FIRST}="",${L["Producto"]}{FIRST}="",'
                                               f'${L["SKU"]}{FIRST - 1}<>"")',
                                   "format": st.cf(bg_color=C["brand_lt"])})
    ws.conditional_format(f"{L['SKU']}{FIRST}:{L['Etiqueta']}{LAST_PROD}", {
        "type": "formula", "criteria": f'=${L["Activo"]}{FIRST}="NO"', "format": st.cf(font_color=C["faint"], italic=True)})
    v = f"{L['Validación']}{FIRST}:{L['Validación']}{LAST_PROD}"
    status_cf(ws, st, v, f"${L['Validación']}{FIRST}")
    for n in ("StockMin", "StockMax"):
        decimals_cf(ws, st, f"{L[n]}{FIRST}:{L[n]}{LAST_PROD}", f"{L[n]}{FIRST}", "#,##0.00")

    # Barra de herramientas
    textbox(ws, "", 16, 5, 220, 24, tag="chip", desc="Contador de productos", fill=C["white"],
            line={"color": C["border"], "width": 1}, font={"size": 9, "bold": True},
            textlink=f"={q(S_KPI)}!$C$42", anchor=(4, 0))
    textbox(ws, "＋  Registrar nuevo producto", 246, 3, 230, 28, tag="btn", desc="Ir a la siguiente fila libre",
            fill=C["brand"], font={"name": FONT_SB, "size": 9.5, "color": C["white"]},
            url="internal:irFilaLibreProd", tip="Ir a la siguiente fila libre del catálogo", anchor=(4, 0))
    x = legend_pills(ws, 492)
    textbox(ws, "Para descontinuar escriba NO en Activo. Nunca borre filas ni cambie un SKU con movimientos.",
            x + 16, 5, 560, 24, tag="txt", desc="Regla del catálogo", halign="left",
            font={"size": 8.5, "color": C["muted"], "italic": True}, anchor=(4, 0))

    if demo:
        fmts = {c.name: st.cell("in", c.fmt) for c in cols if c.kind == "in"}
        for i, p in enumerate(D.PRODUCTOS_DEMO):
            r = FIRST - 1 + i
            ws.write_string(r, 1, p.sku, fmts["SKU"])
            ws.write_string(r, 2, p.nombre, fmts["Producto"])
            ws.write_string(r, 3, p.categoria, fmts["Categoría"])
            ws.write_string(r, 4, p.unidad, fmts["Unidad"])
            ws.write_number(r, 5, p.minimo, fmts["StockMin"])
            ws.write_number(r, 6, p.maximo, fmts["StockMax"])
            ws.write_number(r, 7, p.costo, fmts["CostoUnitario"])
            ws.write_string(r, 8, p.ubicacion, fmts["Ubicación"])
            ws.write_string(r, 9, "SI" if p.activo else "NO", fmts["Activo"])
    n = len(D.PRODUCTOS_DEMO) if demo else 0
    ws.freeze_panes(HDR, 0)
    ws.set_selection(FIRST - 1 + n, 1, FIRST - 1 + n, 1)
    print_setup(ws, "Catálogo de productos")
    return cols


def status_cf(ws, st, rng, ref):
    ws.conditional_format(rng, {"type": "formula", "criteria": f'=LEFT({ref},1)="✖"',
                                "format": st.cf(font_color=C["red"], bg_color=C["red_lt"], bold=True)})
    ws.conditional_format(rng, {"type": "formula", "criteria": f'=LEFT({ref},1)="⚠"',
                                "format": st.cf(font_color=C["amber"], bg_color=C["amber_lt"], bold=True)})
    ws.conditional_format(rng, {"type": "formula", "criteria": f'=LEFT({ref},1)="✔"',
                                "format": st.cf(font_color=C["green"])})
    ws.conditional_format(rng, {"type": "formula", "criteria": f'=LEFT({ref},1)="●"',
                                "format": st.cf(font_color=C["faint"])})


def decimals_cf(ws, st, rng, ref, num_format):
    ws.conditional_format(rng, {"type": "formula", "criteria": f"=AND(ISNUMBER({ref}),{ref}<>INT({ref}))",
                                "format": st.cf(num_format=num_format)})


# ---------------------------------------------------------------------------
# 10_MOVIMIENTOS · Capa de escritura (append-only)
# ---------------------------------------------------------------------------
def movimientos_cols() -> list[Col]:
    r = this_row("tblMovimientos")
    llena = f'({r("Fecha")}&{r("Tipo")}&{r("Producto")}&{r("Cantidad")})=""'
    estado = nested_if([
        (llena, '""'),
        (f'OR({r("Fecha")}="",{r("Tipo")}="",{r("Producto")}="",{r("Cantidad")}="",{r("Responsable")}="")',
         '"⚠ Incompleto"'),
        (f'{r("Unidad")}="?"', '"✖ Producto no existe"'),
        (f'{r("FactorStock")}=0', '"✖ Tipo no válido"'),
        (f'NOT(ISNUMBER({r("Cantidad")}))', '"✖ Cantidad inválida"'),
        (f'{r("Cantidad")}<=0', '"✖ Cantidad inválida"'),
        (f'AND({r("Categoría")}<>"",{r("Categoría")}<>INDEX(tblProductos[Categoría],'
         f'MATCH({r("SKU")},tblProductos[SKU],0)))', '"⚠ Categoría no coincide"'),
        (f'{r("Saldo")}<0', '"✖ Stock insuficiente"'),
        (f'AND(LEFT({r("Tipo")},6)="AJUSTE",{r("Observaciones")}="")', '"⚠ Justifique el ajuste"'),
    ], '"✔ Registrado"')
    return [
        Col("ID", "calc", 48, "id", "Consecutivo automático del registro (auditoría). Aparece al diligenciar la fila.",
            formula=f'=IF({llena},"",ROW()-ROW(tblMovimientos[#Headers]))'),
        Col("Fecha", "in", 94, "date", "Fecha del movimiento (dd/mm/aaaa). Atajo: Ctrl + ; escribe la fecha de hoy."),
        Col("Tipo", "in", 118, "text", "ENTRADA, SALIDA, AJUSTE (+), AJUSTE (-) o SALDO INICIAL. El sistema asigna "
                                      "el FactorStock."),
        Col("Categoría", "in", 172, "text", "Paso 1 de la cascada: elija la categoría para filtrar los productos."),
        Col("Producto", "in", 280, "text", "Paso 2: elija el producto de la lista filtrada. Nunca lo escriba a mano."),
        Col("SKU", "calc", 80, "center", "Código extraído del producto elegido (clave de la proyección de stock).",
            formula=f'=IF({r("Producto")}="","",TRIM(LEFT({r("Producto")},FIND(" · ",{r("Producto")}&" · ")-1)))'),
        Col("Unidad", "calc", 78, "center", "Unidad de medida del producto (desde 05_PRODUCTOS). ? = no existe.",
            formula=f'=IF({r("SKU")}="","",IFERROR(INDEX(tblProductos[Unidad],MATCH({r("SKU")},tblProductos[SKU],0)),'
                    f'"?"))'),
        Col("Cantidad", "in", 86, "qty", "Cantidad siempre positiva, en la unidad indicada. El signo lo pone el "
                                        "FactorStock."),
        Col("FactorStock", "calc", 104, "factor", "+1 suma al inventario, -1 resta. Lo define el Tipo (tabla "
                                                 "tblTiposMov en 01_CONFIG).",
            formula=f'=IF({r("Tipo")}="","",IFERROR(INDEX(tblTiposMov[FactorStock],'
                    f'MATCH({r("Tipo")},tblTiposMov[Tipo],0)),0))'),
        Col("CantidadNeta", "calc", 114, "signed", "Cantidad × FactorStock. Es el único valor que suma la proyección "
                                                   "15_STOCK.",
            formula=f'=IF(OR({r("Cantidad")}="",{r("FactorStock")}=""),"",IF(ISNUMBER({r("Cantidad")}),'
                    f'{r("Cantidad")}*{r("FactorStock")},""))'),
        Col("Saldo", "calc", 82, "strong", "Saldo del producto después de este movimiento (kardex acumulado).",
            formula=f'=IF({r("CantidadNeta")}="","",SUMIFS(INDEX(tblMovimientos[CantidadNeta],1):{r("CantidadNeta")},'
                    f'INDEX(tblMovimientos[SKU],1):{r("SKU")},{r("SKU")}))'),
        Col("Estado", "calc", 172, "status", "Control automático del registro: ✔ correcto, ⚠ revisar, ✖ error que "
                                             "debe corregirse.", formula="=" + estado),
        Col("Documento", "in", 112, "text", "Soporte: factura, remisión u orden (opcional). Ej: FC-10234."),
        Col("Responsable", "in", 140, "text", "Quién registra el movimiento (lista de 01_CONFIG)."),
        Col("Observaciones", "in", 250, "text", "Detalle adicional. Obligatorio en AJUSTES: explique el motivo."),
    ]


def build_movimientos(wb, ws, st, demo: bool, movs):
    cols = movimientos_cols()
    widths = [16] + [c.width for c in cols] + [16]
    app_sheet(ws, widths, zoom=90)
    band(ws, st, "Movimientos de inventario",
         "Capa de escritura · Registro inmutable (append-only) de entradas, salidas y ajustes",
         "mov", widths=widths)
    toolbar_row(ws, st)
    build_table(ws, st, "tblMovimientos", cols, MAX_MOV)
    L = {c.name: letter(cols, c.name) for c in cols}
    rng = lambda n: f"{L[n]}{FIRST}:{L[n]}{LAST_MOV}"  # noqa: E731

    ws.data_validation(rng("Fecha"), {
        "validate": "date", "criteria": "between", "minimum": "=cfgFechaMin", "maximum": "=TODAY()",
        "input_title": "Fecha del movimiento",
        "input_message": "Escriba la fecha (dd/mm/aaaa). Atajo: Ctrl + ; inserta la fecha de hoy. No se admiten "
                         "fechas futuras.",
        "error_title": "Fecha no válida",
        "error_message": "Use el formato dd/mm/aaaa. No se permiten fechas futuras ni anteriores a la fecha mínima "
                         "configurada."})
    ws.data_validation(rng("Tipo"), {
        "validate": "list", "source": "=lstTiposMov",
        "input_title": "Tipo de movimiento",
        "input_message": "ENTRADA (+) · SALIDA (-) · AJUSTE (+/-) · SALDO INICIAL (+). El FactorStock se asigna solo.",
        "error_title": "Tipo no válido", "error_message": "Seleccione un tipo de la lista desplegable (Alt + ↓)."})
    ws.data_validation(rng("Categoría"), {
        "validate": "list", "source": "=lstCategorias",
        "input_title": "Paso 1 · Categoría",
        "input_message": "Elija la categoría. La lista de Producto se filtrará con esta selección.",
        "error_title": "Categoría no válida", "error_message": "Seleccione una categoría de la lista (Alt + ↓)."})
    cat = f"${L['Categoría']}{FIRST}"
    ws.data_validation(rng("Producto"), {
        "validate": "list",
        "source": f'=IF({cat}="",lpTodos,IF(COUNTIF(lpCat,{cat})=0,lpVacio,'
                  f'OFFSET(lpBase,MATCH({cat},lpCat,0),0,COUNTIF(lpCat,{cat}),1)))',
        "input_title": "Paso 2 · Producto",
        "input_message": "Elija el producto de la lista (filtrada por la categoría). Formato: SKU · Nombre. Nunca lo "
                         "escriba a mano.",
        "error_title": "Producto no válido",
        "error_message": "Seleccione un producto de la lista. Si no existe, pida al administrador crearlo en "
                         "05_PRODUCTOS."})
    qc, uc = f"{L['Cantidad']}{FIRST}", f"${L['Unidad']}{FIRST}"
    ws.data_validation(rng("Cantidad"), {
        "validate": "custom",
        "value": f'=AND(ISNUMBER({qc}),{qc}>0,IFERROR(OR(INDEX(lstUniDec,MATCH({uc},lstUniCod,0))="SI",'
                 f'{qc}=INT({qc})),TRUE))',
        "input_title": "Cantidad",
        "input_message": "Número mayor que 0, en la unidad indicada. UND, CAJA, PAQ, PAR y ROLLO solo admiten "
                         "enteros.",
        "error_title": "Cantidad no válida",
        "error_message": "La cantidad debe ser mayor que 0. Esta unidad no admite decimales o el valor no es un "
                         "número."})
    ws.data_validation(rng("Documento"), {
        "validate": "length", "criteria": "<=", "value": 30,
        "input_title": "Documento soporte (opcional)", "input_message": "Factura, remisión u orden. Ej: FC-10234.",
        "error_title": "Texto muy largo", "error_message": "Máximo 30 caracteres."})
    ws.data_validation(rng("Responsable"), {
        "validate": "list", "source": "=lstResponsables",
        "input_title": "Responsable", "input_message": "Seleccione quién registra el movimiento.",
        "error_title": "Responsable no válido", "error_message": "Seleccione un nombre de la lista (Alt + ↓)."})
    ws.data_validation(rng("Observaciones"), {
        "validate": "length", "criteria": "<=", "value": 250,
        "input_title": "Observaciones", "input_message": "Obligatorio en AJUSTES: explique el motivo (merma, conteo...).",
        "error_title": "Texto muy largo", "error_message": "Máximo 250 caracteres."})

    # Formato condicional
    b = FIRST
    empty = (f'AND(${L["Fecha"]}{b}="",${L["Tipo"]}{b}="",${L["Producto"]}{b}="",${L["Cantidad"]}{b}="",'
             f'${L["ID"]}{b - 1}<>"")')
    ins = " ".join(f"{L[n]}{b}:{L[n]}{LAST_MOV}" for n in ("Fecha", "Tipo", "Categoría", "Producto", "Cantidad",
                                                           "Documento", "Responsable", "Observaciones"))
    ws.conditional_format(f"{L['Fecha']}{b}:{L['Fecha']}{LAST_MOV}",
                          {"type": "formula", "criteria": "=" + empty, "format": st.cf(bg_color=C["brand_lt"]),
                           "multi_range": ins})
    fac = f"${L['FactorStock']}{b}"
    for n in ("Tipo", "FactorStock", "CantidadNeta"):
        ws.conditional_format(rng(n), {"type": "formula", "criteria": f"={fac}=1",
                                       "format": st.cf(font_color=C["green"])})
        ws.conditional_format(rng(n), {"type": "formula", "criteria": f"={fac}=-1",
                                       "format": st.cf(font_color=C["red"])})
    decimals_cf(ws, st, rng("Cantidad"), f"{L['Cantidad']}{b}", "#,##0.00")
    decimals_cf(ws, st, rng("Saldo"), f"{L['Saldo']}{b}", "#,##0.00")
    decimals_cf(ws, st, rng("CantidadNeta"), f"{L['CantidadNeta']}{b}", "+#,##0.00;-#,##0.00;0")
    ws.conditional_format(rng("Saldo"), {"type": "formula",
                                         "criteria": f"=AND(ISNUMBER(${L['Saldo']}{b}),${L['Saldo']}{b}<0)",
                                         "format": st.cf(font_color=C["red"], bold=True)})
    status_cf(ws, st, rng("Estado"), f"${L['Estado']}{b}")

    # Barra de herramientas
    textbox(ws, "", 16, 5, 240, 24, tag="chip", desc="Contador de registros", fill=C["white"],
            line={"color": C["border"], "width": 1}, font={"size": 9, "bold": True},
            textlink=f"={q(S_KPI)}!$C$41", anchor=(4, 0))
    textbox(ws, "＋  Registrar nuevo movimiento", 266, 3, 250, 28, tag="btn", desc="Ir a la siguiente fila libre",
            fill=C["brand"], font={"name": FONT_SB, "size": 9.5, "color": C["white"]},
            url="internal:irFilaLibreMov", tip="Ir a la siguiente fila libre de la bitácora", anchor=(4, 0))
    x = legend_pills(ws, 532)
    textbox(ws, "Registro inmutable: nunca borre filas. Los errores se corrigen con un AJUSTE y su observación.",
            x + 16, 5, 600, 24, tag="txt", desc="Regla de inmutabilidad", halign="left",
            font={"size": 8.5, "color": C["muted"], "italic": True}, anchor=(4, 0))

    fmts = {c.name: st.cell("in", c.fmt) for c in cols if c.kind == "in"}
    for i, m in enumerate(movs):
        r = FIRST - 1 + i
        ws.write_datetime(r, 2, dt.datetime.combine(m.fecha, dt.time()), fmts["Fecha"])
        ws.write_string(r, 3, m.tipo, fmts["Tipo"])
        ws.write_string(r, 4, m.categoria, fmts["Categoría"])
        ws.write_string(r, 5, m.producto, fmts["Producto"])
        ws.write_number(r, 8, m.cantidad, fmts["Cantidad"])
        if m.documento:
            ws.write_string(r, 13, m.documento, fmts["Documento"])
        ws.write_string(r, 14, m.responsable, fmts["Responsable"])
        if m.observaciones:
            ws.write_string(r, 15, m.observaciones, fmts["Observaciones"])
    nxt = FIRST - 1 + len(movs)
    ws.freeze_panes(HDR, 0, max(HDR, nxt - 14), 0)
    ws.set_selection(nxt, 2, nxt, 2)
    print_setup(ws, "Movimientos de inventario")
    return cols


# ---------------------------------------------------------------------------
# 15_STOCK · Capa de lectura (proyección)
# ---------------------------------------------------------------------------
def stock_cols(prod: list[Col]) -> list[Col]:
    r = this_row("tblStock")
    P = {c.name: letter(prod, c.name) for c in prod}
    sp = q(S_PROD)
    src = lambda n: f"{sp}!${P[n]}{{r}}"  # noqa: E731
    estado = nested_if([
        (f'{r("SKU")}=""', '""'),
        (f'{r("Activo")}="NO"', '"INACTIVO"'),
        (f'{r("StockActual")}<0', '"INCONSISTENTE"'),
        (f'{r("StockActual")}=0', '"AGOTADO"'),
        (f'{r("StockActual")}<={r("StockMin")}', '"CRÍTICO"'),
        (f'{r("StockActual")}<={r("StockMin")}*(1+cfgMargenAlerta)', '"BAJO"'),
        (f'AND({r("StockMax")}>0,{r("StockActual")}>{r("StockMax")})', '"SOBRESTOCK"'),
    ], '"ÓPTIMO"')
    sku_ref = "$B{r}"
    return [
        Col("SKU", "mirror", 84, "center", "Espejo de 05_PRODUCTOS (misma fila).",
            mirror=f'=IF({src("SKU")}="","",{src("SKU")})'),
        Col("Producto", "mirror", 270, "text", "", mirror=f'=IF({sku_ref}="","",{src("Producto")})'),
        Col("Categoría", "mirror", 172, "text", "", mirror=f'=IF({sku_ref}="","",{src("Categoría")})'),
        Col("Unidad", "mirror", 78, "center", "", mirror=f'=IF({sku_ref}="","",{src("Unidad")})'),
        Col("Activo", "mirror", 74, "center", "", mirror=f'=IF({sku_ref}="","",IF({src("Activo")}="NO","NO","SI"))'),
        Col("Entradas", "calc", 88, "qty", "Suma de movimientos con FactorStock +1 (saldo inicial, entradas, "
                                           "ajustes +).",
            formula=f'=IF({r("SKU")}="","",SUMIFS(tblMovimientos[CantidadNeta],tblMovimientos[SKU],{r("SKU")},'
                    f'tblMovimientos[FactorStock],1))'),
        Col("Salidas", "calc", 88, "qty", "Suma de movimientos con FactorStock -1 (salidas y ajustes -).",
            formula=f'=IF({r("SKU")}="","",-SUMIFS(tblMovimientos[CantidadNeta],tblMovimientos[SKU],{r("SKU")},'
                    f'tblMovimientos[FactorStock],-1))'),
        Col("StockActual", "calc", 112, "strong", "SUMAR.SI.CONJUNTO de CantidadNeta por SKU. Nunca se escribe a "
                                                  "mano: cambia solo con movimientos.",
            formula=f'=IF({r("SKU")}="","",SUMIFS(tblMovimientos[CantidadNeta],tblMovimientos[SKU],{r("SKU")}))'),
        Col("StockMin", "mirror", 94, "qty", "", mirror=f'=IF({sku_ref}="","",N({src("StockMin")}))'),
        Col("StockMax", "mirror", 94, "qty", "", mirror=f'=IF({sku_ref}="","",N({src("StockMax")}))'),
        Col("Nivel", "calc", 116, "pct", "Stock actual ÷ stock máximo (barra de nivel).",
            formula=f'=IF(OR({r("SKU")}="",N({r("StockMax")})=0),"",MAX(0,{r("StockActual")})/{r("StockMax")})'),
        Col("Estado", "calc", 138, "estado", "Semáforo según reglas de tblEstados (01_CONFIG) y el margen de alerta.",
            formula="=" + estado),
        Col("CostoUnitario", "mirror", 120, "money", "", mirror=f'=IF({sku_ref}="","",N({src("CostoUnitario")}))'),
        Col("ValorInventario", "calc", 136, "money", "Stock actual (no negativo) × costo unitario.",
            formula=f'=IF({r("SKU")}="","",MAX(0,{r("StockActual")})*{r("CostoUnitario")})'),
        Col("UltimoMov", "calc", 112, "date", "Fecha del último movimiento del SKU (detecta inventario inmovilizado).",
            formula=f'=IF({r("SKU")}="","",IFERROR(_xlfn.AGGREGATE(14,6,tblMovimientos[Fecha]/'
                    f'(tblMovimientos[SKU]={r("SKU")}),1),""))'),
    ]


def estado_cf(ws, st, rng, ref, solid=False):
    for e in ESTADOS:
        name, _, _, fg, bg, strong = e[:6]
        solid_bg = bg if fg == "#FFFFFF" else fg
        fmt = st.cf(font_color="#FFFFFF", bg_color=solid_bg, bold=True) if solid else \
            st.cf(font_color=fg, bg_color=bg, bold=True, num_format='"● "@')
        ws.conditional_format(rng, {"type": "formula", "criteria": f'={ref}="{name}"', "format": fmt})


def row_cf(ws, st, rng, key):
    ws.conditional_format(rng, {"type": "formula", "criteria": f'=AND({key}<>"",MOD(ROW(),2)=0)',
                                "format": st.cf(bg_color=C["zebra"])})
    ws.conditional_format(rng, {"type": "formula", "criteria": f'={key}<>""',
                                "format": st.cf(bottom=1, bottom_color=C["row_line"])})


def build_stock(wb, ws, st, prod_cols):
    cols = stock_cols(prod_cols)
    widths = [16] + [c.width for c in cols] + [16]
    app_sheet(ws, widths, zoom=90)
    band(ws, st, "Stock actual",
         "Capa de lectura · Proyección calculada con SUMAR.SI.CONJUNTO sobre 10_MOVIMIENTOS (no editable)",
         "stock", widths=widths)
    toolbar_row(ws, st)
    build_table(ws, st, "tblStock", cols, MAX_PROD, view=True)
    L = {c.name: letter(cols, c.name) for c in cols}
    rng = lambda n: f"{L[n]}{FIRST}:{L[n]}{LAST_PROD}"  # noqa: E731
    key = f"$B{FIRST}"
    estado_cf(ws, st, rng("Estado"), f"${L['Estado']}{FIRST}")
    for name, color in (("AGOTADO", C["red"]), ("CRÍTICO", C["red"]), ("INCONSISTENTE", "#6A1B9A"),
                        ("BAJO", C["amber"]), ("ÓPTIMO", C["green"]), ("SOBRESTOCK", C["brand"])):
        ws.conditional_format(rng("StockActual"), {"type": "formula",
                                                   "criteria": f'=${L["Estado"]}{FIRST}="{name}"',
                                                   "format": st.cf(font_color=color)})
    ws.conditional_format(rng("Nivel"), {"type": "data_bar", "bar_solid": True, "bar_color": "#90CAF9",
                                         "bar_border_color": "#64B5F6", "min_type": "num", "min_value": 0,
                                         "max_type": "num", "max_value": 1, "data_bar_2010": True})
    for n in ("Entradas", "Salidas", "StockActual", "StockMin", "StockMax"):
        decimals_cf(ws, st, rng(n), f"{L[n]}{FIRST}", "#,##0.00")
    row_cf(ws, st, f"B{FIRST}:{L['UltimoMov']}{LAST_PROD}", key)

    textbox(ws, "", 16, 5, 200, 24, tag="chip", desc="Contador de productos", fill=C["white"],
            line={"color": C["border"], "width": 1}, font={"size": 9, "bold": True},
            textlink=f"={q(S_KPI)}!$C$42", anchor=(4, 0))
    textbox(ws, "", 226, 5, 290, 24, tag="chip", desc="Valor del inventario", fill=C["white"],
            line={"color": C["border"], "width": 1}, font={"size": 9, "bold": True, "color": C["brand_dk"]},
            textlink=f"={q(S_KPI)}!$C$43", anchor=(4, 0))
    x = 532
    for e in ESTADOS:
        w = 36 + int(7.5 * len(e[0]))
        textbox(ws, e[0], x, 7, w, 20, tag="chip", desc=f"Leyenda semáforo: {e[0]}", fill=e[4],
                font={"size": 8, "bold": True, "color": e[3]}, tip=e[6], anchor=(4, 0))
        x += w + 6
    ws.freeze_panes(HDR, 0)
    ws.set_selection(FIRST - 1, 1, FIRST - 1, 1)
    print_setup(ws, "Stock actual")
    return cols


# ---------------------------------------------------------------------------
# 16_ALERTAS · Capa de lectura (priorizada)
# ---------------------------------------------------------------------------
def build_alertas(wb, ws, st, stock):
    S = {c.name: letter(stock, c.name) for c in stock}
    spec = [  # (encabezado, ancho, formato, columna de tblStock o fórmula especial)
        ("#", 42, "id", None), ("Nivel", 138, "estado", "Estado"), ("SKU", 84, "center", "SKU"),
        ("Producto", 270, "text", "Producto"), ("Categoría", 172, "text", "Categoría"),
        ("Stock", 86, "strong", "StockActual"), ("Mínimo", 92, "qty", "StockMin"), ("Máximo", 92, "qty", "StockMax"),
        ("Unidad", 76, "center", "Unidad"), ("Faltante", 88, "qty", "*faltante"),
        ("SugeridoPedir", 132, "qty", "*sugerido"), ("AcciónSugerida", 250, "text", "*accion"),
        ("UltimoMov", 112, "date", "UltimoMov"),
    ]
    widths = [16] + [s[1] for s in spec] + [16]
    app_sheet(ws, widths, zoom=100)
    band(ws, st, "Alertas de inventario",
         "Capa de lectura · Productos que requieren acción, ordenados por prioridad (se actualiza sola)",
         "alertas", widths=widths)
    toolbar_row(ws, st)
    hdr0 = HDR - 1
    ws.set_row_pixels(hdr0, 34)
    tips = {
        "#": "Orden de prioridad: primero inconsistencias, luego agotados, críticos, preventivos y sobrestock.",
        "Faltante": "Unidades que faltan para llegar al stock mínimo.",
        "SugeridoPedir": "Cantidad sugerida para reponer hasta el máximo (o 2 × mínimo si no hay máximo).",
        "AcciónSugerida": "Acción recomendada según el semáforo (tblEstados en 01_CONFIG).",
    }
    L = {}
    for i, (name, w, fmt, _) in enumerate(spec):
        col = 1 + i
        L[name] = xl_col_to_name(col)
        ws.set_column_pixels(col, col, w)
        ws.write_string(hdr0, col, name, st.hdr("calc"))
        ws.write_string(hdr0 - 1, col, "ƒx", st.badge("calc"))
        if name in tips:
            header_tip(ws, hdr0, col, f"ƒx {name}", tips[name])
    kq = q(S_KPI)
    for k in range(MAX_PROD):
        r = FIRST + k
        idx = f"{kq}!$Q{r}"
        for i, (name, w, fmt, src) in enumerate(spec):
            f = st.cell("calc", fmt, view=True)
            if name == "#":
                formula = f'=IF({idx}="","",{kq}!$O{r})'
            elif src == "*faltante":
                formula = f'=IF($B{r}="","",MAX(0,${L["Mínimo"]}{r}-${L["Stock"]}{r}))'
            elif src == "*sugerido":
                formula = (f'=IF($B{r}="","",IF(OR(${L["Nivel"]}{r}="SOBRESTOCK",${L["Nivel"]}{r}="INCONSISTENTE"),0,'
                           f'MAX(0,IF(${L["Máximo"]}{r}>0,${L["Máximo"]}{r},2*${L["Mínimo"]}{r})'
                           f'-MAX(0,${L["Stock"]}{r}))))')
            elif src == "*accion":
                formula = (f'=IF($B{r}="","",IFERROR(INDEX(tblEstados[Acción],MATCH(${L["Nivel"]}{r},'
                           f'tblEstados[Estado],0)),""))')
            else:
                formula = f'=IF($B{r}="","",INDEX(tblStock[{src}],{idx}))'
            ws.write_formula(r - 1, 1 + i, formula, f)
    last = LAST_PROD
    ws.autofilter(hdr0, 1, last - 1, len(spec))
    estado_cf(ws, st, f"{L['Nivel']}{FIRST}:{L['Nivel']}{last}", f"${L['Nivel']}{FIRST}")
    estado_cf(ws, st, f"B{FIRST}:B{last}", f"${L['Nivel']}{FIRST}", solid=True)
    for n in ("Stock", "Mínimo", "Máximo", "Faltante", "SugeridoPedir"):
        decimals_cf(ws, st, f"{L[n]}{FIRST}:{L[n]}{last}", f"{L[n]}{FIRST}", "#,##0.00")
    ws.conditional_format(f"{L['SugeridoPedir']}{FIRST}:{L['SugeridoPedir']}{last}",
                          {"type": "formula", "criteria": f'=AND(ISNUMBER(${L["SugeridoPedir"]}{FIRST}),'
                                                          f'${L["SugeridoPedir"]}{FIRST}>0)',
                           "format": st.cf(bold=True, font_color=C["brand_dk"])})
    row_cf(ws, st, f"B{FIRST}:{L['UltimoMov']}{last}", f"$B{FIRST}")

    textbox(ws, "", 16, 5, 200, 24, tag="chip", desc="Productos en alerta", fill=C["white"],
            line={"color": C["border"], "width": 1}, font={"size": 9, "bold": True},
            textlink=f"={kq}!$C$44", anchor=(4, 0))
    x = 226
    for name, cell in (("AGOTADO", 45), ("CRÍTICO", 46), ("BAJO", 47), ("SOBRESTOCK", 48), ("INCONSISTENTE", 49)):
        e = EST[name]
        textbox(ws, "", x, 5, 158, 24, tag="chip", desc=f"Contador {name}", fill=e[4],
                font={"size": 8.5, "bold": True, "color": e[3]}, textlink=f"={kq}!$C${cell}", tip=e[6],
                anchor=(4, 0))
        x += 164
    ws.freeze_panes(HDR, 0)
    ws.set_selection(FIRST - 1, 1, FIRST - 1, 1)
    print_setup(ws, "Alertas de inventario")


# ---------------------------------------------------------------------------
# 90_LISTAS · Motor de listas en cascada (oculto)
# ---------------------------------------------------------------------------
def build_listas(wb, ws, st, prod):
    P = {c.name: letter(prod, c.name) for c in prod}
    config_band(ws, st, "Motor de listas", "Motor interno (oculto) · Productos activos ordenados por categoría para la "
                                           "lista en cascada de 10_MOVIMIENTOS",
                [24, 24, 170, 300, 120, 24, 60, 120, 170, 300, 24])
    hdr = st.hdr("calc")
    for c, h in zip("CDEGHIJ", ["Categoría válida", "Etiqueta", "Clave de orden", "k", "Clave k-ésima",
                                "Categoría ordenada", "Etiqueta ordenada"]):
        ws.write_string(HDR - 1, ord(c) - 65, h, hdr)
    ws.write_string(3, 9, "— Sin productos activos en esta categoría —", st(bg_color=C["ink"], font_color=C["white"]))
    sp = q(S_PROD)
    cell = st(font_size=9, formula=True)
    for i in range(MAX_PROD):
        r = FIRST + i
        ws.write_formula(r - 1, 2, f'=IF(AND({sp}!${P["SKU"]}{r}<>"",{sp}!${P["Producto"]}{r}<>"",'
                                   f'{sp}!${P["Activo"]}{r}<>"NO",ISNUMBER(MATCH({sp}!${P["Categoría"]}{r},'
                                   f'lstCategorias,0))),{sp}!${P["Categoría"]}{r},"")', cell)
        ws.write_formula(r - 1, 3, f'=IF($C{r}="","",{sp}!${P["Etiqueta"]}{r})', cell)
        ws.write_formula(r - 1, 4, f'=IF($C{r}="","",MATCH($C{r},lstCategorias,0)*1000+'
                                   f'COUNTIFS($C${FIRST}:$C${LAST_PROD},$C{r},$D${FIRST}:$D${LAST_PROD},"<"&$D{r})+1'
                                   f'+ROW()/100000)', cell)
        ws.write_number(r - 1, 6, i + 1, st(font_size=9, align="center"))
        ws.write_formula(r - 1, 7, f'=IFERROR(SMALL($E${FIRST}:$E${LAST_PROD},$G{r}),"")', cell)
        ws.write_formula(r - 1, 8, f'=IF($H{r}="","",INDEX($C${FIRST}:$C${LAST_PROD},'
                                   f'MATCH($H{r},$E${FIRST}:$E${LAST_PROD},0)))', cell)
        ws.write_formula(r - 1, 9, f'=IF($H{r}="","",INDEX($D${FIRST}:$D${LAST_PROD},'
                                   f'MATCH($H{r},$E${FIRST}:$E${LAST_PROD},0)))', cell)
    L = q(S_LISTAS)
    wb.define_name("lpCat", f"={L}!$I${FIRST}:$I${LAST_PROD}")
    wb.define_name("lpBase", f"={L}!$J${HDR}")
    wb.define_name("lpVacio", f"={L}!$J$4")
    wb.define_name("lpTodos", f'=IF(COUNTIF({L}!$J${FIRST}:$J${LAST_PROD},"?*")=0,{L}!$J$4,'
                              f'{L}!$J${FIRST}:INDEX({L}!$J${FIRST}:$J${LAST_PROD},'
                              f'COUNTIF({L}!$J${FIRST}:$J${LAST_PROD},"?*")))')


# ---------------------------------------------------------------------------
# 91_KPIS · Motor de indicadores (oculto)
# ---------------------------------------------------------------------------
def build_kpis(wb, ws, st, stock, n_cat: int):
    S = {c.name: letter(stock, c.name) for c in stock}
    config_band(ws, st, "Motor de indicadores", "Motor interno (oculto) · KPIs, series de gráficos y ranking de alertas",
                [24, 230, 150, 300, 24, 110, 90, 90, 150, 110, 110, 150, 150, 24, 60, 150, 70, 24])
    sec = st(bold=True, font_color=C["brand"], font_size=10.5)
    lbl = st(font_size=9, indent=1, border=1, border_color=C["border"])
    note = st(font_size=8.5, font_color=C["muted"], indent=1, border=1, border_color=C["border"])
    n0 = st(font_size=9, bold=True, align="right", num_format="#,##0", border=1, border_color=C["border"],
            formula=True)
    ws.write_string(4, 1, "INDICADORES (nombres definidos kpi* / txt*)", sec)
    ws.write_string(4, 5, "ACTIVIDAD MENSUAL (6 meses)", sec)
    ws.write_string(15, 5, "TOP 8 POR VALOR", sec)
    ws.write_string(27, 5, "SALUD DEL INVENTARIO", sec)
    ws.write_string(37, 5, "ESTADO POR CATEGORÍA", sec)
    ws.write_string(4, 11, "CLAVES POR PRODUCTO", sec)
    ws.write_string(4, 14, "RANKING DE ALERTAS", sec)
    for c, h in zip("BCD", ["Indicador", "Valor", "Nombre definido / nota"]):
        ws.write_string(HDR - 1, ord(c) - 65, h, st.hdr("calc"))

    alert_list = '{"INCONSISTENTE","AGOTADO","CRÍTICO","BAJO"}'
    rows = [  # fila Excel, nombre, etiqueta, fórmula, formato de número, nota
        (8, "kpiCatalogo", "Productos en catálogo", '=COUNTIF(tblStock[SKU],"?*")', "#,##0", ""),
        (9, "kpiActivos", "Productos activos", '=COUNTIFS(tblStock[SKU],"?*",tblStock[Activo],"SI")', "#,##0", ""),
        (10, "kpiInconsistentes", "Inconsistentes", '=COUNTIF(tblStock[Estado],"INCONSISTENTE")', "#,##0", ""),
        (11, "kpiAgotados", "Agotados", '=COUNTIF(tblStock[Estado],"AGOTADO")', "#,##0", ""),
        (12, "kpiCriticos", "Críticos", '=COUNTIF(tblStock[Estado],"CRÍTICO")', "#,##0", ""),
        (13, "kpiBajos", "Bajos (preventivos)", '=COUNTIF(tblStock[Estado],"BAJO")', "#,##0", ""),
        (14, "kpiOptimos", "Óptimos", '=COUNTIF(tblStock[Estado],"ÓPTIMO")', "#,##0", ""),
        (15, "kpiSobrestock", "Sobrestock", '=COUNTIF(tblStock[Estado],"SOBRESTOCK")', "#,##0", ""),
        (16, "kpiInactivos", "Inactivos", '=COUNTIF(tblStock[Estado],"INACTIVO")', "#,##0", ""),
        (17, "kpiEnAlerta", "En alerta (requieren acción)",
         f"=SUMPRODUCT(COUNTIF(tblStock[Estado],{alert_list}))", "#,##0", "Inconsistente + agotado + crítico + bajo"),
        (18, "kpiValor", "Valor del inventario", "=SUM(tblStock[ValorInventario])", "$ #,##0", ""),
        (19, "kpiRegistros", "Registros en la bitácora", "=COUNT(tblMovimientos[ID])", "#,##0", ""),
        (20, "kpiCapacidad", "Capacidad de la bitácora", "=ROWS(tblMovimientos[ID])", "#,##0", ""),
        (21, "kpiUltimoID", "Último ID registrado", "=MAX(tblMovimientos[ID])", "#,##0", ""),
        (22, "kpiFilaLibreMov", "Fila libre en bitácora",
         "=ROW(tblMovimientos[#Headers])+MIN(kpiUltimoID+1,kpiCapacidad)", "0", "Destino de irFilaLibreMov"),
        (23, "kpiFilaLibreProd", "Fila libre en catálogo",
         '=ROW(tblProductos[#Headers])+MIN(IFERROR(LOOKUP(2,1/(tblProductos[SKU]<>""),'
         'ROW(tblProductos[SKU])-ROW(tblProductos[#Headers])),0)+1,ROWS(tblProductos[SKU]))', "0",
         "Destino de irFilaLibreProd"),
        (24, "kpiMovMes", "Movimientos del mes",
         '=COUNTIFS(tblMovimientos[Fecha],">="&DATE(YEAR(TODAY()),MONTH(TODAY()),1),'
         'tblMovimientos[Fecha],"<"&DATE(YEAR(TODAY()),MONTH(TODAY())+1,1))', "#,##0", ""),
        (25, "kpiIngMes", "Ingresos del mes (sin saldo inicial)",
         '=COUNTIFS(tblMovimientos[Fecha],">="&DATE(YEAR(TODAY()),MONTH(TODAY()),1),'
         'tblMovimientos[Fecha],"<"&DATE(YEAR(TODAY()),MONTH(TODAY())+1,1),tblMovimientos[FactorStock],1,'
         'tblMovimientos[Tipo],"<>SALDO INICIAL")', "#,##0", ""),
        (26, "kpiEgrMes", "Egresos del mes",
         '=COUNTIFS(tblMovimientos[Fecha],">="&DATE(YEAR(TODAY()),MONTH(TODAY()),1),'
         'tblMovimientos[Fecha],"<"&DATE(YEAR(TODAY()),MONTH(TODAY())+1,1),tblMovimientos[FactorStock],-1)',
         "#,##0", ""),
        (27, "kpiUltFecha", "Fecha del último movimiento",
         '=IF(COUNT(tblMovimientos[Fecha])=0,"",MAX(tblMovimientos[Fecha]))', "dd/mm/yyyy", ""),
        (28, "kpiErrores", "Registros con error (✖)", '=COUNTIF(tblMovimientos[Estado],"✖*")', "#,##0", ""),
        (29, "kpiAdvertencias", "Registros por revisar (⚠)", '=COUNTIF(tblMovimientos[Estado],"⚠*")', "#,##0", ""),
        (30, "txtActivosNota", "Nota tarjeta activos", "=kpiCatalogo", '"de "#,##0" en el catálogo"', ""),
        (31, "txtAgotNota", "Nota tarjeta agotados", "=IF(kpiActivos=0,0,kpiAgotados/kpiActivos)",
         '0%" del catálogo activo"', ""),
        (32, "txtCritNota", "Nota tarjeta críticos", '="en o por debajo del stock mínimo"', "@", ""),
        (33, "txtBajosNota", "Nota tarjeta preventivos", "=cfgMargenAlerta", '"hasta "0%" por encima del mínimo"', ""),
        (34, "txtOptNota", "Nota tarjeta óptimos", "=IF(kpiActivos=0,0,kpiOptimos/kpiActivos)",
         '0%" del catálogo activo"', ""),
        (35, "txtValorNota", "Nota tarjeta valor", '="a costo unitario de catálogo"', "@", ""),
        (36, "txtMovNota", "Nota tarjeta movimientos",
         '="▲ "&kpiIngMes&" ingresos    ▼ "&kpiEgrMes&" egresos"', "@", ""),
        (37, "txtUltFecha", "Tarjeta último movimiento", '=IF(kpiUltFecha="","Sin registros",kpiUltFecha)',
         "dd/mm/yyyy", ""),
        (38, "txtUltNota", "Nota último movimiento",
         '=IF(kpiUltFecha="","registre el primer movimiento",IF(TODAY()-kpiUltFecha<=0,"registrado hoy",'
         'IF(TODAY()-kpiUltFecha=1,"hace 1 día","hace "&(TODAY()-kpiUltFecha)&" días")))', "@", ""),
        (39, "txtIntegridad", "Integridad de la bitácora",
         '=IF(kpiErrores>0,"✖ "&kpiErrores&" registro(s) con error en la bitácora",IF(kpiAdvertencias>0,'
         '"⚠ "&kpiAdvertencias&" registro(s) por completar","✔ Bitácora íntegra · sin errores"))', "@", ""),
        (40, "txtAlertasBtn", "Texto botón alertas", '="ALERTAS ("&kpiEnAlerta&")"', "@", ""),
        (41, "txtRegistros", "Chip registros",
         '="Registros: "&FIXED(kpiRegistros,0)&" de "&FIXED(kpiCapacidad,0)', "@", ""),
        (42, "txtProductos", "Chip productos",
         '="Productos: "&FIXED(kpiCatalogo,0)&" de "&FIXED(ROWS(tblProductos[SKU]),0)', "@", ""),
        (43, "txtValor", "Chip valor", '="Valor del inventario: $ "&FIXED(kpiValor,0)', "@", ""),
        (44, "txtEnAlerta", "Chip en alerta", '="Requieren acción: "&kpiEnAlerta', "@", ""),
        (45, "txtChipAgot", "Chip agotados", '="● Agotados: "&kpiAgotados', "@", ""),
        (46, "txtChipCrit", "Chip críticos", '="● Críticos: "&kpiCriticos', "@", ""),
        (47, "txtChipBajo", "Chip preventivos", '="● Preventivos: "&kpiBajos', "@", ""),
        (48, "txtChipSobre", "Chip sobrestock", '="● Sobrestock: "&kpiSobrestock', "@", ""),
        (49, "txtChipIncons", "Chip inconsistentes", '="● Inconsistentes: "&kpiInconsistentes', "@", ""),
        (50, "txtBitacora", "Pie de portada",
         '="Bitácora: "&FIXED(kpiRegistros,0)&" de "&FIXED(kpiCapacidad,0)&" registros · "&'
         'FIXED(IF(kpiCapacidad=0,0,kpiRegistros/kpiCapacidad*100),0)&"% de capacidad"', "@", ""),
        (51, "txtEmpresa", "Subtítulo portada", '=cfgEmpresa&"   ·   "&cfgBodega', "@", ""),
    ]
    for r, name, label, formula, nf, nota in rows:
        ws.write_string(r - 1, 1, label, lbl)
        ws.write_formula(r - 1, 2, formula, st(font_size=9, bold=True, align="right", num_format=nf, border=1,
                                               border_color=C["border"], formula=True))
        ws.write_string(r - 1, 3, nota or name, note)
        wb.define_name(name, f"={q(S_KPI)}!$C${r}")

    hdr = st.hdr("calc")
    # Actividad mensual (F..I, filas 8..13)
    for c, h in zip("FGHI", ["Inicio de mes", "Mes", "Ingresos", "Egresos"]):
        ws.write_string(HDR - 1, ord(c) - 65, h, hdr)
    for i in range(6):
        r = 8 + i
        start = "=DATE(YEAR(TODAY()),MONTH(TODAY())-5,1)" if i == 0 else f"=DATE(YEAR(F{r - 1}),MONTH(F{r - 1})+1,1)"
        ws.write_formula(r - 1, 5, start, st(font_size=9, num_format="dd/mm/yyyy", formula=True))
        ws.write_formula(r - 1, 6, f'=CHOOSE(MONTH(F{r}),"Ene","Feb","Mar","Abr","May","Jun","Jul","Ago","Sep",'
                                   f'"Oct","Nov","Dic")&" "&RIGHT(YEAR(F{r}),2)', st(font_size=9, formula=True))
        for c, factor in ((7, 1), (8, -1)):
            extra = ',tblMovimientos[Tipo],"<>SALDO INICIAL"' if factor == 1 else ""
            ws.write_formula(r - 1, c, f'=COUNTIFS(tblMovimientos[Fecha],">="&F{r},tblMovimientos[Fecha],'
                                       f'"<"&DATE(YEAR(F{r}),MONTH(F{r})+1,1),tblMovimientos[FactorStock],{factor}'
                                       f'{extra})', n0)
    # Top 8 por valor (F..J, filas 18..25)
    for c, h in zip("FGHIJ", ["k", "Clave", "Fila", "Producto", "Valor"]):
        ws.write_string(16, ord(c) - 65, h, hdr)
    for k in range(8):
        r = 18 + k
        ws.write_number(r - 1, 5, k + 1, st(font_size=9, align="center"))
        ws.write_formula(r - 1, 6, f'=IFERROR(LARGE($L${FIRST}:$L${LAST_PROD},F{r}),"")', st(font_size=9, formula=True))
        ws.write_formula(r - 1, 7, f'=IF(G{r}="","",MATCH(G{r},$L${FIRST}:$L${LAST_PROD},0))',
                         st(font_size=9, formula=True))
        ws.write_formula(r - 1, 8, f'=IF(H{r}="","",LEFT(INDEX(tblStock[Producto],H{r}),34))',
                         st(font_size=9, formula=True))
        ws.write_formula(r - 1, 9, f'=IF(H{r}="","",INDEX(tblStock[ValorInventario],H{r}))',
                         st(font_size=9, num_format="$ #,##0", formula=True))
    # Salud del inventario (F..H, filas 30..35)
    for c, h in zip("FGH", ["Estado", "Productos", "Color"]):
        ws.write_string(28, ord(c) - 65, h, hdr)
    salud = [("Agotado", "kpiAgotados", "AGOTADO"), ("Crítico", "kpiCriticos", "CRÍTICO"),
             ("Preventivo", "kpiBajos", "BAJO"), ("Óptimo", "kpiOptimos", "ÓPTIMO"),
             ("Sobrestock", "kpiSobrestock", "SOBRESTOCK"), ("Inconsistente", "kpiInconsistentes", "INCONSISTENTE")]
    for i, (label, name, est) in enumerate(salud):
        r = 30 + i
        ws.write_string(r - 1, 5, label, st(font_size=9))
        ws.write_formula(r - 1, 6, f"={name}", n0)
        ws.write_string(r - 1, 7, EST[est][5], st(font_size=9, font_color=EST[est][5]))
    # Estado por categoría (F..K, filas 39..50)
    etiquetas = ["Agotado", "Crítico", "Preventivo", "Óptimo", "Sobrestock"]
    claves = ["AGOTADO", "CRÍTICO", "BAJO", "ÓPTIMO", "SOBRESTOCK"]
    ws.write_string(38, 5, "Categoría", hdr)
    for j, (et, cl) in enumerate(zip(etiquetas, claves)):
        ws.write_string(38, 6 + j, et, hdr)
        ws.write_string(39, 6 + j, cl, st(font_size=8, font_color=C["muted"], align="center"))
    for k in range(max(n_cat, 1)):
        r = 41 + k
        ws.write_formula(r - 1, 5, f'=IFERROR(INDEX(tblCategorias[Categoría],{k + 1}),"")',
                         st(font_size=9, formula=True))
        for j in range(5):
            cl = xl_col_to_name(6 + j)
            ws.write_formula(r - 1, 6 + j, f'=IF($F{r}="","",COUNTIFS(tblStock[Categoría],$F{r},'
                                           f'tblStock[Estado],{cl}$40))', n0)
    # Claves por producto (L..M) y ranking de alertas (O..Q), alineadas con las filas de 15_STOCK
    for c, h in zip("LMOPQ", ["Clave valor", "Clave alerta", "k", "Clave k", "Fila stock"]):
        ws.write_string(HDR - 1, ord(c) - 65, h, hdr)
    sq = q(S_STOCK)
    prio = '{"INCONSISTENTE","AGOTADO","CRÍTICO","BAJO","SOBRESTOCK"}'
    small = st(font_size=8.5, formula=True)
    for i in range(MAX_PROD):
        r = FIRST + i
        ws.write_formula(r - 1, 11, f'=IF(AND(ISNUMBER({sq}!${S["ValorInventario"]}{r}),'
                                    f'{sq}!${S["ValorInventario"]}{r}>0),{sq}!${S["ValorInventario"]}{r}'
                                    f'+ROW()/1000000,"")', small)
        ws.write_formula(r - 1, 12, f'=IFERROR(MATCH({sq}!${S["Estado"]}{r},{prio},0)*100000+'
                                    f'MIN(99,INT(IF(N({sq}!${S["StockMin"]}{r})>0,MAX(0,N({sq}!${S["StockActual"]}{r}))'
                                    f'/{sq}!${S["StockMin"]}{r},0)*10))*1000+ROW(),"")', small)
        ws.write_number(r - 1, 14, i + 1, st(font_size=8.5, align="center"))
        ws.write_formula(r - 1, 15, f'=IFERROR(SMALL($M${FIRST}:$M${LAST_PROD},O{r}),"")', small)
        ws.write_formula(r - 1, 16, f'=IF(P{r}="","",MATCH(P{r},$M${FIRST}:$M${LAST_PROD},0))', small)
    wb.define_name("irFilaLibreMov", f"=INDEX({q(S_MOV)}!$C:$C,MIN(kpiFilaLibreMov,{LAST_MOV}))")
    wb.define_name("irFilaLibreProd", f"=INDEX({q(S_PROD)}!$B:$B,MIN(kpiFilaLibreProd,{LAST_PROD}))")


# ---------------------------------------------------------------------------
# 00_PORTADA · Tablero tipo aplicación
# ---------------------------------------------------------------------------
GRID = [24] + [94] * 12 + [24]   # A | B..M | N  (1.176 px de contenido)


def chart_frame(ws, x, y, w, h, row, desc):
    textbox(ws, "", x, y, w, h, tag="bg", desc=desc, fill=C["white"], anchor=(row, 1))


def base_chart(chart, title):
    chart.set_title({"name": title, "overlay": False,
                     "name_font": {"name": FONT, "size": 10.5, "bold": True, "color": C["text"]}})
    chart.set_chartarea({"border": {"none": True}, "fill": {"none": True}})
    chart.set_plotarea({"border": {"none": True}, "fill": {"none": True}})


def axis_font(color=None):
    return {"name": FONT, "size": 8.5, "color": color or C["muted"]}


def build_portada(wb, ws, st, demo: bool, n_cat: int):
    for i, w in enumerate(GRID):
        ws.set_column_pixels(i, i, w)
    ws.set_column(len(GRID), 16383, None, None, {"hidden": True})
    ws.hide_gridlines(2)
    ws.hide_row_col_headers()
    ws.set_default_row(hide_unused_rows=True)
    ws.set_zoom(100)
    canvas, ink = st(bg_color=C["canvas"]), st(bg_color=C["ink"])
    heights = {0: 12, 1: 34, 2: 22, 3: 14, 4: 18, 5: 92, 6: 20, 7: 24, 8: 100, 9: 16, 10: 100, 11: 22, 12: 24,
               13: 290, 14: 16, 15: 290, 16: 34, 17: 26}
    for r in range(18, 26):
        heights[r] = 26
    heights.update({26: 22, 27: 40, 28: 14})
    for r, h in heights.items():
        ws.set_row_pixels(r, h, ink if r <= 3 else canvas)
    kq = q(S_KPI)

    # --- Héroe
    ws.insert_image(0, 1, str(BRAND / "tenant-logo-placeholder@2x.png"),
                    {"x_offset": 0, "y_offset": 17, "x_scale": 0.3125, "y_scale": 0.3125, "object_position": 2,
                     "description": "[logo] Logo del cliente (reemplazable)"})
    ws.write_string(1, 3, "M-INV · Sistema de Inventarios", st(bg_color=C["ink"], font_color=C["white"],
                                                               font_name=FONT_SB, font_size=18))
    ws.write_formula(2, 3, "=txtEmpresa", st(bg_color=C["ink"], font_color=C["band_sub"], font_size=10,
                                             formula=True))
    ws.merge_range(1, 9, 1, 12, "=TODAY()", st(bg_color=C["ink"], font_color=C["nav_text"], font_size=10, align="right",
                                           num_format='"Hoy · "dd/mm/yyyy', formula=True))
    ws.merge_range(2, 9, 2, 12, f"M-INV V{VERSION}", st(bg_color=C["ink"], font_color=C["band_sub"], font_size=8.5,
                                                   align="right"))
    if demo:
        textbox(ws, "DATOS DE DEMOSTRACIÓN", 760, 27, 176, 26, tag="pill", desc="Aviso: libro con datos demo",
                fill="#F59E0B", font={"name": FONT_SB, "size": 8.5, "color": C["ink"]},
                tip="Este libro Core contiene datos ficticios para demostración")

    # --- Mosaicos de navegación
    tiles = [
        ("NUEVO MOVIMIENTO", "movimiento", C["brand"], "internal:irFilaLibreMov",
         "Registrar entrada, salida o ajuste", None),
        ("VER STOCK", "stock", C["teal"], f"internal:{q(S_STOCK)}!A1", "Consultar el stock actual", None),
        ("", "alertas", "#C62828", f"internal:{q(S_ALERT)}!A1", "Productos que requieren acción",
         f"={kq}!$C$40"),
        ("CATÁLOGO", "catalogo", C["purple"], f"internal:{q(S_PROD)}!A1", "Crear o consultar productos", None),
        ("GUÍA RÁPIDA", "ayuda", "#455A64", f"internal:{q(S_AYUDA)}!A1", "Aprenda a usar M-INV", None),
    ]
    tw, tg = 212, 17
    for i, (label, icon, color, url, tip, link) in enumerate(tiles):
        x = i * (tw + tg)
        textbox(ws, label, x, 0, tw, 92, tag="tile", desc=f"Botón {label or 'ALERTAS'}", fill=color,
                font={"name": FONT_SB, "size": 10.5, "color": C["white"]}, valign="bottom",
                url=url, tip=tip, textlink=link, anchor=(5, 1))
        ws.insert_image(5, 1, str(ICONS / f"{icon}-blanco@2x.png"),
                        {"x_offset": x + (tw - 32) // 2, "y_offset": 14, "x_scale": 0.5, "y_scale": 0.5,
                         "object_position": 2, "url": url, "tip": tip, "description": f"[icon] Icono {icon}"})

    # --- Tarjetas KPI
    def section(row, text):
        ws.write_rich_string(row, 1, st(font_color=C["brand"], bold=True, font_size=10, bg_color=C["canvas"]), "▍ ",
                             st(font_color=C["muted"], bold=True, font_size=9, bg_color=C["canvas"]), text,
                             st(bg_color=C["canvas"]))

    section(7, "INDICADORES CLAVE")
    ws.write_formula(7, 12, "=txtIntegridad", st(bg_color=C["canvas"], font_size=9, bold=True, align="right",
                                                 font_color=C["green"], formula=True))
    ws.conditional_format(7, 12, 7, 12, {"type": "formula", "criteria": '=LEFT($M$8,1)="✖"',
                                         "format": st.cf(font_color=C["red"])})
    ws.conditional_format(7, 12, 7, 12, {"type": "formula", "criteria": '=LEFT($M$8,1)="⚠"',
                                         "format": st.cf(font_color=C["amber"])})
    cards = [
        ("PRODUCTOS ACTIVOS", 9, 30, C["brand"], C["ink"], f"{q(S_STOCK)}!A1"),
        ("AGOTADOS", 11, 31, "#B71C1C", "#B71C1C", f"{q(S_ALERT)}!A1"),
        ("STOCK CRÍTICO", 12, 32, "#E53935", "#D32F2F", f"{q(S_ALERT)}!A1"),
        ("ALERTA PREVENTIVA", 13, 33, "#F59E0B", C["amber"], f"{q(S_ALERT)}!A1"),
        ("STOCK ÓPTIMO", 14, 34, "#43A047", C["green"], f"{q(S_STOCK)}!A1"),
        ("VALOR DEL INVENTARIO", 18, 35, C["brand_dk"], C["brand_dk"], f"{q(S_STOCK)}!A1"),
        ("MOVIMIENTOS DEL MES", 24, 36, C["teal"], C["teal"], "irFilaLibreMov"),
        ("ÚLTIMO MOVIMIENTO", 37, 38, C["purple"], C["purple"], "irFilaLibreMov"),
    ]
    cw, cg = 270, 16
    for i, (title, vrow, frow, accent, vcolor, link) in enumerate(cards):
        row = 8 if i < 4 else 10
        x = (i % 4) * (cw + cg)
        url = f"internal:{link}"
        tip = f"Ver detalle: {title.lower()}"
        textbox(ws, "", x, 0, cw, 100, tag="card", desc=f"Tarjeta {title}", fill=C["white"], url=url, tip=tip,
                anchor=(row, 1))
        textbox(ws, "", x + 14, 22, 5, 56, tag="bar", desc=f"Acento {title}", fill=accent, anchor=(row, 1))
        textbox(ws, title, x + 30, 13, cw - 44, 18, tag="txt", desc=f"Título {title}", halign="left",
                font={"size": 8.5, "bold": True, "color": C["muted"]}, url=url, tip=tip, anchor=(row, 1))
        textbox(ws, "", x + 30, 32, cw - 44, 40, tag="txt", desc=f"Valor {title}", halign="left",
                font={"name": FONT_SB, "size": 22, "color": vcolor}, textlink=f"={kq}!$C${vrow}", url=url, tip=tip,
                anchor=(row, 1))
        textbox(ws, "", x + 30, 72, cw - 44, 18, tag="txt", desc=f"Nota {title}", halign="left",
                font={"size": 8.5, "color": C["muted"]}, textlink=f"={kq}!$C${frow}", url=url, tip=tip,
                anchor=(row, 1))

    # --- Gráficos
    section(12, "ANÁLISIS DEL INVENTARIO")
    chart_frame(ws, 0, 0, 372, 290, 13, "Marco gráfico salud")
    chart_frame(ws, 388, 0, 740, 290, 13, "Marco gráfico actividad")
    chart_frame(ws, 0, 0, 556, 290, 15, "Marco gráfico top valor")
    chart_frame(ws, 572, 0, 556, 290, 15, "Marco gráfico categorías")

    salud = wb.add_chart({"type": "doughnut"})
    salud.add_series({
        "name": "Salud del inventario",
        "categories": f"={kq}!$F$30:$F$35", "values": f"={kq}!$G$30:$G$35",
        "points": [{"fill": {"color": EST[k][5]}, "border": {"color": "#FFFFFF", "width": 1.5}}
                   for k in ("AGOTADO", "CRÍTICO", "BAJO", "ÓPTIMO", "SOBRESTOCK", "INCONSISTENTE")],
        "data_labels": {"value": True, "num_format": "#,##0;;;",
                        "font": {"name": FONT, "size": 9, "bold": True, "color": "#FFFFFF"},
                        "custom": [{"font": {"name": FONT, "size": 9, "bold": True,
                                             "color": C["ink"] if k == "BAJO" else "#FFFFFF"}}
                                   for k in ("AGOTADO", "CRÍTICO", "BAJO", "ÓPTIMO", "SOBRESTOCK", "INCONSISTENTE")]},
    })
    salud.set_hole_size(58)
    base_chart(salud, "Salud del inventario (productos por semáforo)")
    salud.set_legend({"position": "right", "font": axis_font(C["text"])})
    salud.set_size({"width": 356, "height": 278})
    ws.insert_chart(13, 1, salud, {"x_offset": 8, "y_offset": 6, "object_position": 2,
                                   "description": "Gráfico de salud del inventario"})

    act = wb.add_chart({"type": "column"})
    for name, col, color in (("Ingresos (+)", "H", C["green"]), ("Egresos (-)", "I", C["red"])):
        act.add_series({"name": name, "categories": f"={kq}!$G$8:$G$13", "values": f"={kq}!${col}$8:${col}$13",
                        "fill": {"color": color}, "border": {"none": True}, "gap": 70, "overlap": -6,
                        "data_labels": {"value": True, "num_format": "#,##0;;;", "font": axis_font(C["text"])}})
    base_chart(act, "Actividad: movimientos por mes (últimos 6 meses)")
    act.set_legend({"position": "top", "font": axis_font(C["text"])})
    act.set_y_axis({"major_gridlines": {"visible": True, "line": {"color": "#E5E7EB"}}, "line": {"none": True},
                    "label_position": "none", "min": 0})
    act.set_x_axis({"line": {"color": "#CBD5E1"}, "num_font": axis_font(C["text"])})
    act.set_size({"width": 724, "height": 278})
    ws.insert_chart(13, 1, act, {"x_offset": 396, "y_offset": 6, "object_position": 2,
                                 "description": "Gráfico de actividad mensual"})

    top = wb.add_chart({"type": "bar"})
    top.add_series({"name": "Valor", "categories": f"={kq}!$I$18:$I$25", "values": f"={kq}!$J$18:$J$25",
                    "fill": {"color": "#1E88E5"}, "border": {"none": True}, "gap": 45,
                    "data_labels": {"value": True, "num_format": '$ #,##0.0,," M";;;', "font": axis_font(C["text"])}})
    base_chart(top, "Top 8 productos por valor de inventario")
    top.set_legend({"none": True})
    top.set_y_axis({"reverse": True, "line": {"color": "#CBD5E1"}, "num_font": axis_font(C["text"])})
    top.set_x_axis({"major_gridlines": {"visible": True, "line": {"color": "#E5E7EB"}}, "line": {"none": True},
                    "label_position": "none", "min": 0})
    top.set_size({"width": 540, "height": 278})
    ws.insert_chart(15, 1, top, {"x_offset": 8, "y_offset": 6, "object_position": 2,
                                 "description": "Gráfico top 8 productos por valor"})

    cat = wb.add_chart({"type": "bar", "subtype": "stacked"})
    last = 40 + max(n_cat, 1)
    for j, k in enumerate(("AGOTADO", "CRÍTICO", "BAJO", "ÓPTIMO", "SOBRESTOCK")):
        cl = xl_col_to_name(6 + j)
        cat.add_series({"name": f"={kq}!${cl}$39", "categories": f"={kq}!$F$41:$F${last}",
                        "values": f"={kq}!${cl}$41:${cl}${last}", "fill": {"color": EST[k][5]},
                        "border": {"color": "#FFFFFF", "width": 0.75}, "gap": 55,
                        "data_labels": {"value": True, "num_format": "#,##0;;;",
                                        "font": {"name": FONT, "size": 8, "bold": True,
                                                 "color": C["ink"] if k == "BAJO" else "#FFFFFF"}}})
    base_chart(cat, "Semáforo por categoría (n.º de productos)")
    cat.set_legend({"position": "bottom", "font": axis_font(C["text"])})
    cat.set_y_axis({"reverse": True, "line": {"color": "#CBD5E1"}, "num_font": axis_font(C["text"])})
    cat.set_x_axis({"major_gridlines": {"visible": True, "line": {"color": "#E5E7EB"}}, "line": {"none": True},
                    "label_position": "none", "min": 0})
    cat.set_size({"width": 540, "height": 278})
    ws.insert_chart(15, 1, cat, {"x_offset": 580, "y_offset": 6, "object_position": 2,
                                 "description": "Gráfico semáforo por categoría"})

    # --- Últimos movimientos
    section(16, "ÚLTIMOS MOVIMIENTOS")
    textbox(ws, "Ver bitácora completa  ➜", 950, 1, 178, 22, tag="txt", desc="Ir a la bitácora", halign="right",
            font={"size": 9, "bold": True, "color": C["brand"]}, url="internal:irFilaLibreMov",
            tip="Abrir 10_MOVIMIENTOS en la siguiente fila libre", anchor=(16, 1))
    h = st(bold=True, font_size=9, font_color=C["white"], bg_color=C["ink2"], align="center")
    hl = st(bold=True, font_size=9, font_color=C["white"], bg_color=C["ink2"], indent=1)
    layout = [("Fecha", 1, 1, h), ("Tipo", 2, 2, h), ("Producto", 3, 7, hl), ("Cantidad", 8, 8, h),
              ("Unidad", 9, 9, h), ("Responsable", 10, 11, hl), ("Documento", 12, 12, h)]
    for label, c0, c1, f in layout:
        if c0 == c1:
            ws.write_string(17, c0, label, f)
        else:
            ws.merge_range(17, c0, 17, c1, label, f)
    cell = dict(font_size=9, bg_color=C["white"], bottom=1, bottom_color=C["row_line"], formula=True)
    fmts = {
        "Fecha": st(align="center", num_format="dd/mm/yyyy", **cell),
        "Tipo": st(align="center", bold=True, **cell),
        "Producto": st(indent=1, **cell),
        "Cantidad": st(align="right", num_format="+#,##0;-#,##0;0", bold=True, indent=1, **cell),
        "Unidad": st(align="center", **cell),
        "Responsable": st(indent=1, **cell),
        "Documento": st(align="center", font_color=C["muted"], **cell),
    }
    src = {"Fecha": "Fecha", "Tipo": "Tipo", "Producto": "Producto", "Cantidad": "CantidadNeta",
           "Unidad": "Unidad", "Responsable": "Responsable", "Documento": "Documento"}
    for j in range(8):
        r = 18 + j
        idx = f"kpiUltimoID-{j}"
        for label, c0, c1, _ in layout:
            formula = f'=IF({idx}<1,"",INDEX(tblMovimientos[{src[label]}],{idx})&"")'
            if label in ("Fecha", "Cantidad"):
                formula = f'=IF({idx}<1,"",INDEX(tblMovimientos[{src[label]}],{idx}))'
            if c0 == c1:
                ws.write_formula(r, c0, formula, fmts[label])
            else:
                ws.merge_range(r, c0, r, c1, formula, fmts[label])
    lastrow = 18 + 7 + 1
    ws.conditional_format(f"C19:C{lastrow}", {"type": "formula", "criteria": "=AND(ISNUMBER($I19),$I19>0)",
                                              "format": st.cf(font_color=C["green"])})
    ws.conditional_format(f"C19:C{lastrow}", {"type": "formula", "criteria": "=AND(ISNUMBER($I19),$I19<0)",
                                              "format": st.cf(font_color=C["red"])})
    ws.conditional_format(f"I19:I{lastrow}", {"type": "formula", "criteria": "=AND(ISNUMBER($I19),$I19>0)",
                                              "format": st.cf(font_color=C["green"])})
    ws.conditional_format(f"I19:I{lastrow}", {"type": "formula", "criteria": "=AND(ISNUMBER($I19),$I19<0)",
                                              "format": st.cf(font_color=C["red"])})
    decimals_cf(ws, st, f"I19:I{lastrow}", "I19", "+#,##0.00;-#,##0.00;0")

    # --- Pie
    ws.insert_image(27, 1, str(BRAND / "zp-insignia@2x.png"),
                    {"x_offset": 0, "y_offset": 6, "x_scale": 0.16, "y_scale": 0.16, "object_position": 2,
                     "description": "[logo] Z&P Software Fast Solutions"})
    ws.merge_range(27, 1, 27, 8, "", st(bg_color=C["canvas"]))
    ws.write_rich_string(27, 1,
                         st(bg_color=C["canvas"], font_size=8.5, bold=True, font_color=C["text"]),
                         f"            M-INV V{VERSION}",
                         st(bg_color=C["canvas"], font_size=8.5, font_color=C["muted"]),
                         "  ·  CQRS: escritura en 10_MOVIMIENTOS → lectura en 15_STOCK y 16_ALERTAS  ·  "
                         "Z&P Software Fast Solutions",
                         st(bg_color=C["canvas"], font_size=8.5, font_color=C["muted"]))
    ws.merge_range(27, 9, 27, 12, "=txtBitacora", st(bg_color=C["canvas"], font_size=8.5, font_color=C["muted"],
                                                     align="right", formula=True))
    ws.set_selection(0, 0, 0, 0)


# ---------------------------------------------------------------------------
# 99_AYUDA · Guía didáctica
# ---------------------------------------------------------------------------
def build_ayuda(wb, ws, st, demo: bool):
    widths = [24] + [94] * 12 + [120]
    app_sheet(ws, widths)
    for i, w in enumerate(widths):
        ws.set_column_pixels(i, i, w)
    band(ws, st, "Guía rápida", "Aprenda a operar M-INV en 5 minutos · Reglas, pasos y código de colores",
         "ayuda", widths=widths)
    canvas = st(bg_color=C["canvas"])
    row_h = {4: 18}
    r = 5
    sec = st(bold=True, font_color=C["brand"], font_size=11, bg_color=C["canvas"])
    para = st(font_size=10, font_color=C["text"], bg_color=C["canvas"], text_wrap=True, valign="top")
    muted = st(font_size=9, font_color=C["muted"], bg_color=C["canvas"], text_wrap=True, valign="top")

    def section(title):
        nonlocal r
        row_h[r] = 30
        ws.merge_range(r, 1, r, 12, title, sec)
        r += 1

    def paragraph(text, h=36, fmt=None):
        nonlocal r
        row_h[r] = h
        ws.merge_range(r, 1, r, 12, text, fmt or para)
        r += 1

    def gap(h=14):
        nonlocal r
        row_h[r] = h
        r += 1

    section("1 · ¿CÓMO FUNCIONA M-INV?")
    paragraph("M-INV separa ESCRIBIR de LEER (arquitectura CQRS). Usted solo escribe en la bitácora de movimientos; el "
              "stock, las alertas y el tablero se calculan solos. Así el inventario siempre es trazable y nadie puede "
              "\"inventar\" un saldo.")
    row_h[r] = 96
    flow = [
        ("①  CATÁLOGO\n05_PRODUCTOS\nDatos maestros", C["purple"], f"internal:{q(S_PROD)}!A1"),
        ("②  MOVIMIENTOS\n10_MOVIMIENTOS\nEscritura · append-only", C["brand"], "internal:irFilaLibreMov"),
        ("③  STOCK\n15_STOCK\nLectura · proyección", C["teal"], f"internal:{q(S_STOCK)}!A1"),
        ("④  ALERTAS\n16_ALERTAS\nLectura · prioridad", "#C62828", f"internal:{q(S_ALERT)}!A1"),
    ]
    bw, gapx = 236, 61
    for i, (text, color, url) in enumerate(flow):
        x = i * (bw + gapx)
        textbox(ws, text, x, 8, bw, 80, tag="tile", desc=f"Diagrama: {text.splitlines()[0]}", fill=color,
                font={"name": FONT_SB, "size": 10, "color": C["white"]}, url=url, tip="Ir a la hoja", anchor=(r, 1))
        if i < 3:
            textbox(ws, "➜", x + bw + 8, 30, gapx - 16, 36, tag="txt", desc="Flecha del flujo",
                    font={"size": 20, "bold": True, "color": C["faint"]}, anchor=(r, 1))
    r += 1
    paragraph("Regla de oro: el stock NUNCA se escribe a mano. Cada cambio es un registro nuevo en 10_MOVIMIENTOS con "
              "su FactorStock (+1 suma, -1 resta); 15_STOCK suma esos registros con SUMAR.SI.CONJUNTO.", 36, muted)
    gap()

    section("2 · REGISTRAR UN MOVIMIENTO EN 7 PASOS")
    pasos = [
        "Pulse «Nuevo movimiento» en la portada (o «Registrar nuevo movimiento» en la bitácora): el cursor queda en "
        "la siguiente fila libre, resaltada en azul claro.",
        "Fecha: escríbala (dd/mm/aaaa) o presione Ctrl + ; para la fecha de hoy.",
        "Tipo: ENTRADA, SALIDA, AJUSTE (+), AJUSTE (-) o SALDO INICIAL. El sistema asigna el FactorStock.",
        "Categoría y luego Producto: la lista de productos se filtra por la categoría elegida (Alt + ↓ abre la lista).",
        "Cantidad: siempre positiva y en la unidad que muestra la columna Unidad. El signo lo pone el sistema.",
        "Documento (opcional), Responsable y Observaciones (obligatorias en los ajustes).",
        "Revise la columna Estado: debe decir ✔ Registrado. Si ve ✖ o ⚠, lea el mensaje y corrija antes de seguir.",
    ]
    num = st(bold=True, font_color=C["white"], bg_color=C["brand"], align="center", font_size=11)
    txt = st(font_size=10, bg_color=C["white"], text_wrap=True, indent=1, border=1, border_color=C["border"])
    for i, p in enumerate(pasos, 1):
        row_h[r] = 34
        ws.write_number(r, 1, i, num)
        ws.merge_range(r, 2, r, 12, p, txt)
        r += 1
    gap()

    section("3 · CÓDIGO DE COLORES")
    row_h[r] = 30
    ws.write_string(r, 1, "Texto", st(bg_color=C["white"], border=1, border_color=C["input_border"], indent=1))
    ws.merge_range(r, 2, r, 6, "  ✎  Celda de ingreso: usted escribe o selecciona.", para)
    ws.write_string(r, 7, "Cálculo", st(bg_color=C["calc_fill"], font_color=C["calc_text"], border=1,
                                         border_color=C["calc_border"], indent=1))
    ws.merge_range(r, 8, r, 12, "  ƒx  Celda calculada: bloqueada, la llena el sistema.", para)
    r += 1
    gap(8)
    for i, e in enumerate(ESTADOS):
        row_h[r] = 26
        ws.merge_range(r, 1, r, 2, e[0], st(bold=True, font_size=9, font_color=e[3], bg_color=e[4], align="center",
                                            num_format='"● "@'))
        ws.merge_range(r, 3, r, 12, f'=INDEX(tblEstados[Regla],{i + 1})&"  →  "&INDEX(tblEstados[Acción],{i + 1})',
                       st(font_size=9.5, bg_color=C["canvas"], indent=1, formula=True))
        r += 1
    paragraph('=" Margen de alerta preventiva configurado: "&FIXED(cfgMargenAlerta*100,0)&"% por encima del mínimo."',
              22, st(font_size=9, font_color=C["muted"], bg_color=C["canvas"], italic=True, formula=True))
    gap()

    section("4 · TIPOS DE MOVIMIENTO")
    h = st(bold=True, font_size=9, font_color=C["white"], bg_color=C["ink2"], align="center")
    row_h[r] = 26
    ws.merge_range(r, 1, r, 2, "Tipo", h)
    ws.write_string(r, 3, "FactorStock", h)
    ws.merge_range(r, 4, r, 12, "Uso", h)
    r += 1
    for i in range(len(D.TIPOS_MOVIMIENTO)):
        row_h[r] = 26
        base = dict(font_size=9.5, bg_color=C["white"], bottom=1, bottom_color=C["row_line"], formula=True)
        ws.merge_range(r, 1, r, 2, f"=INDEX(tblTiposMov[Tipo],{i + 1})", st(bold=True, indent=1, **base))
        ws.write_formula(r, 3, f"=INDEX(tblTiposMov[FactorStock],{i + 1})",
                         st(bold=True, align="center", num_format="+0;-0;0", **base))
        ws.merge_range(r, 4, r, 12, f"=INDEX(tblTiposMov[Descripción],{i + 1})", st(indent=1, **base))
        r += 1
    ws.conditional_format(r - len(D.TIPOS_MOVIMIENTO), 3, r - 1, 3,
                          {"type": "cell", "criteria": ">", "value": 0, "format": st.cf(font_color=C["green"])})
    ws.conditional_format(r - len(D.TIPOS_MOVIMIENTO), 3, r - 1, 3,
                          {"type": "cell", "criteria": "<", "value": 0, "format": st.cf(font_color=C["red"])})
    gap()

    section("5 · ¿ME EQUIVOQUÉ? ASÍ SE CORRIGE")
    paragraph("La bitácora es inmutable: nunca borre ni sobrescriba un movimiento ya registrado. Corrija con un "
              "movimiento compensatorio y deje la explicación en Observaciones.", 36)
    paragraph("Ejemplo: se registró una SALIDA de 10 UND pero eran 8 → registre un AJUSTE (+) de 2 UND con la "
              "observación «Corrección del registro ID 245».", 36, muted)
    gap()

    section("6 · EJEMPLO DE FILA BIEN DILIGENCIADA")
    ej = [("Fecha", "in", "24/09/2026"), ("Tipo", "in", "SALIDA"), ("Categoría", "in", "FERRETERÍA"),
          ("Producto", "in", "FER-001 · Tornillo drywall 6x1\""), ("Cantidad", "in", "4"),
          ("FactorStock", "calc", "-1"), ("Estado", "calc", "✔ Registrado")]
    spans = [(1, 1), (2, 2), (3, 4), (5, 8), (9, 9), (10, 10), (11, 12)]
    row_h[r], row_h[r + 1] = 28, 28
    for (name, kind, value), (c0, c1) in zip(ej, spans):
        hf = st(bold=True, font_color=C["white"], bg_color=C["hdr_in"] if kind == "in" else C["hdr_calc"],
                align="center", font_size=9, border=1, border_color=C["white"])
        vf = (st(bg_color=C["white"], border=1, border_color=C["input_border"], indent=1, font_size=9.5)
              if kind == "in" else
              st(bg_color=C["calc_fill"], font_color=C["green"] if name == "Estado" else C["red"], bold=True,
                 border=1, border_color=C["calc_border"], indent=1, font_size=9.5))
        for rr, val, f in ((r, name, hf), (r + 1, value, vf)):
            if c0 == c1:
                ws.write_string(rr, c0, val, f)
            else:
                ws.merge_range(rr, c0, rr, c1, val, f)
    r += 2
    paragraph("Azul = usted diligencia · Gris = el sistema calcula (SKU, Unidad, FactorStock, Saldo y Estado se "
              "llenan solos).", 22, muted)
    gap()

    section("7 · ATAJOS DE TECLADO")
    atajos = [("Ctrl + ;", "Inserta la fecha de hoy en la celda."),
              ("Alt + ↓", "Abre la lista desplegable de la celda seleccionada."),
              ("Tab", "Salta al siguiente campo editable (las columnas calculadas se omiten solas)."),
              ("Ctrl + Z", "Deshace el último cambio (antes de guardar)."),
              ("Ctrl + Inicio", "Vuelve al inicio de la hoja.")]
    key = st(bold=True, font_size=9.5, align="center", bg_color=C["white"], border=2, border_color="#CBD5E1")
    for k, desc in atajos:
        row_h[r] = 28
        ws.merge_range(r, 1, r, 2, k, key)
        ws.merge_range(r, 3, r, 12, "  " + desc, para)
        r += 1
    gap()

    section("8 · PREGUNTAS FRECUENTES")
    faqs = [
        ("¿Por qué no puedo escribir en algunas columnas?",
         "Son cálculos protegidos (ƒx). El bloqueo evita que una fórmula se borre por accidente."),
        ("¿Por qué aparece «✖ Stock insuficiente»?",
         "La salida supera el saldo disponible del producto. Verifique la cantidad o registre primero la entrada "
         "pendiente."),
        ("¿Cómo agrego un producto?",
         "En 05_PRODUCTOS pulse «Registrar nuevo producto». El SKU debe ser único y sin espacios."),
        ("¿Cómo descontinúo un producto?",
         "Escriba NO en la columna Activo: sale de las listas pero conserva todo su historial."),
        ("¿Qué pasa cuando la bitácora se llena?",
         f"La capacidad es de {MAX_MOV:,} registros".replace(",", ".") +
         ". Antes del límite, Z&P realiza el cierre de período: archivo histórico + saldos iniciales en un libro "
         "nuevo."),
    ]
    qf = st(bold=True, font_size=10, bg_color=C["canvas"], font_color=C["ink"])
    for question, answer in faqs:
        row_h[r] = 22
        ws.merge_range(r, 1, r, 12, "▸ " + question, qf)
        r += 1
        paragraph("   " + answer, 24, muted)
    gap(20)
    row_h[r] = 30
    ws.merge_range(r, 1, r, 12, f"M-INV V{VERSION} · Z&P Software Fast Solutions · Documentación técnica en el "
                                f"repositorio ZP-MINV-Platform (docs/)",
                   st(font_size=8.5, font_color=C["faint"], bg_color=C["canvas"], align="center"))
    r += 1
    row_h[r] = 16
    for rr in range(4, r + 1):
        ws.set_row_pixels(rr, row_h.get(rr, 20), canvas)
    ws.set_selection(0, 0, 0, 0)


# ---------------------------------------------------------------------------
# Post-proceso del paquete OOXML (lo que XlsxWriter no expone)
# ---------------------------------------------------------------------------
ANCHOR_RE = re.compile(r"<xdr:(twoCellAnchor|oneCellAnchor|absoluteAnchor)\b.*?</xdr:\1>", re.S)
TAG_RE = re.compile(r'descr="\[(\w+)\]\s*([^"]*)"')
RADIUS = {"btn": 22000, "tile": 9000, "card": 6500, "bg": 4000, "pill": 50000, "chip": 50000, "bar": 50000}
SHADOW = {
    "soft": '<a:effectLst><a:outerShdw blurRad="63500" dist="12700" dir="5400000" algn="t" rotWithShape="0">'
            '<a:srgbClr val="0B1F33"><a:alpha val="14000"/></a:srgbClr></a:outerShdw></a:effectLst>',
    "lift": '<a:effectLst><a:outerShdw blurRad="50800" dist="19050" dir="5400000" algn="t" rotWithShape="0">'
            '<a:srgbClr val="0B1F33"><a:alpha val="26000"/></a:srgbClr></a:outerShdw></a:effectLst>',
}
INSETS = {"txt": (0, 0, 0, 0), "pill": (76200, 0, 76200, 0), "chip": (76200, 0, 76200, 0),
          "tile": (76200, 45720, 76200, 133350), "btn": (76200, 0, 76200, 0)}


def _style_anchor(a: str) -> tuple[str, str | None]:
    m = TAG_RE.search(a)
    if not m:
        return a, None
    tag = m.group(1)
    a = a.replace(m.group(0), f'descr="{m.group(2)}"', 1)
    if tag in RADIUS:
        a = a.replace('<a:prstGeom prst="rect"><a:avLst/></a:prstGeom>',
                      f'<a:prstGeom prst="roundRect"><a:avLst><a:gd name="adj" fmla="val {RADIUS[tag]}"/></a:avLst>'
                      f'</a:prstGeom>', 1)
    if tag in ("card", "bg"):
        a = re.sub(r"(</a:ln>)(</xdr:spPr>)", lambda mm: mm.group(1) + SHADOW["soft"] + mm.group(2), a, count=1)
    elif tag in ("tile", "btn"):
        a = re.sub(r"(</a:ln>)(</xdr:spPr>)", lambda mm: mm.group(1) + SHADOW["lift"] + mm.group(2), a, count=1)
    if tag in INSETS:
        l, t, r, b = INSETS[tag]
        a = a.replace("<a:bodyPr ", f'<a:bodyPr lIns="{l}" tIns="{t}" rIns="{r}" bIns="{b}" ', 1)
    return a, tag


def _fix_drawing(xml: str) -> str:
    anchors = list(ANCHOR_RE.finditer(xml))
    if not anchors:
        return xml
    head, tail = xml[:anchors[0].start()], xml[anchors[-1].end():]
    back, middle, front = [], [], []
    for m in anchors:
        a, tag = _style_anchor(m.group(0))
        (back if tag == "bg" else front if tag == "icon" else middle).append(a)
    return head + "".join(back + middle + front) + tail


def postprocess(src: Path, dst: Path, lock_password: str | None):
    with zipfile.ZipFile(src) as zin, zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED) as zout:
        for item in zin.infolist():
            data = zin.read(item.filename)
            if item.filename.startswith("xl/drawings/drawing") and item.filename.endswith(".xml"):
                data = _fix_drawing(data.decode("utf-8")).encode("utf-8")
            elif item.filename == "xl/workbook.xml" and lock_password:
                xml = data.decode("utf-8")
                prot = f'<workbookProtection workbookPassword="{legacy_hash(lock_password)}" lockStructure="1"/>'
                xml = xml.replace("<bookViews>", prot + "<bookViews>", 1)
                data = xml.encode("utf-8")
            zout.writestr(item, data)
    src.unlink()


# ---------------------------------------------------------------------------
# Ensamble
# ---------------------------------------------------------------------------
def build(mode: str, out: Path, password: str, fin: dt.date) -> dict:
    demo = mode == "core"
    out.parent.mkdir(parents=True, exist_ok=True)
    tmp = out.with_name(out.stem + ".tmp.xlsx")
    wb = xlsxwriter.Workbook(str(tmp), {"strings_to_numbers": False})
    wb.set_properties({
        "title": "M-INV V1 · Sistema de Inventarios",
        "subject": "Inventario B2B transaccional (CQRS sobre Excel)",
        "author": "Z&P Software Fast Solutions",
        "company": "Z&P Software Fast Solutions",
        "category": "Inventarios",
        "keywords": "M-INV, inventario, CQRS, kardex",
        "comments": "Generado por tools/build_minv.py. No editar a mano: regenerar desde el repositorio.",
        "status": "Desarrollo (datos demo)" if demo else "Producción",
    })
    wb.set_custom_property("M-INV Version", VERSION)
    wb.set_custom_property("M-INV Build", "core" if demo else "release")
    st = Styles(wb, hide_formulas=not demo)

    sheets = {name: wb.add_worksheet(name) for name in
              (S_PORTADA, S_CONFIG, S_CAT, S_PROD, S_UNI, S_MOV, S_STOCK, S_ALERT, S_LISTAS, S_KPI, S_AYUDA)}
    movs, stock_final = D.generar_movimientos(fin) if demo else ([], {})
    cats = D.CATEGORIAS_DEMO if demo else D.CATEGORIAS_BASE

    wb.define_name("lstTiposMov", "=tblTiposMov[Tipo]")
    wb.define_name("lstCategorias", "=tblCategorias[Categoría]")
    wb.define_name("lstUniCod", "=tblUnidades[Código]")
    wb.define_name("lstUniDec", "=tblUnidades[Decimales]")
    wb.define_name("lstResponsables", "=tblResponsables[Nombre]")

    build_config(wb, sheets[S_CONFIG], st, demo, fin)
    build_categorias(sheets[S_CAT], st, demo)
    build_unidades(sheets[S_UNI], st)
    prod = build_productos(wb, sheets[S_PROD], st, demo)
    build_movimientos(wb, sheets[S_MOV], st, demo, movs)
    stock = build_stock(wb, sheets[S_STOCK], st, prod)
    build_alertas(wb, sheets[S_ALERT], st, stock)
    build_listas(wb, sheets[S_LISTAS], st, prod)
    build_kpis(wb, sheets[S_KPI], st, stock, len(cats))
    build_portada(wb, sheets[S_PORTADA], st, demo, len(cats))
    build_ayuda(wb, sheets[S_AYUDA], st, demo)

    # Protección y visibilidad por capa
    allow_data = {"autofilter": True, "format_columns": True, "select_locked_cells": True,
                  "select_unlocked_cells": True}
    for name in (S_PROD, S_MOV, S_STOCK, S_ALERT):
        sheets[name].protect(password, allow_data)
    sheets[S_PORTADA].protect(password, {"select_locked_cells": False, "select_unlocked_cells": False})
    sheets[S_AYUDA].protect(password, {"select_locked_cells": True})
    for name in (S_CONFIG, S_CAT, S_UNI, S_LISTAS, S_KPI):
        sheets[name].protect(password, {"select_locked_cells": True, "select_unlocked_cells": True})
        if demo:
            sheets[name].hide()
        else:
            sheets[name].very_hidden()
    tabs = {S_PORTADA: C["brand"], S_PROD: C["purple"], S_MOV: C["brand_dk"], S_STOCK: C["teal"],
            S_ALERT: "#C62828", S_AYUDA: "#455A64"}
    for name, color in tabs.items():
        sheets[name].set_tab_color(color)
    for name in (S_CONFIG, S_CAT, S_UNI, S_LISTAS, S_KPI):
        sheets[name].set_tab_color("#94A3B8")
    sheets[S_PORTADA].activate()
    sheets[S_PORTADA].set_first_sheet()
    wb.close()
    postprocess(tmp, out, None if demo else password)
    return {"movimientos": len(movs), "stock_final": stock_final}


def main():
    ap = argparse.ArgumentParser(description="Genera los libros Excel de M-INV V1.")
    ap.add_argument("--solo", choices=("core", "release"), help="Generar solo una variante.")
    ap.add_argument("--fin-demo", type=dt.date.fromisoformat, default=dt.date.today(),
                    help="Fecha final de la simulación demo (AAAA-MM-DD). Por defecto: hoy.")
    args = ap.parse_args()
    pwd = os.environ.get("MINV_PASSWORD", DEV_PASSWORD)
    rel_pwd = os.environ.get("MINV_RELEASE_PASSWORD")
    if not args.solo or args.solo == "core":
        info = build("core", OUT_CORE, pwd, args.fin_demo)
        print(f"[core]    {OUT_CORE.relative_to(ROOT)}  ({info['movimientos']} movimientos demo)")
    if not args.solo or args.solo == "release":
        if not rel_pwd:
            print("[aviso]   MINV_RELEASE_PASSWORD no definida: el release usa la contraseña de desarrollo.")
        build("release", OUT_RELEASE, rel_pwd or pwd, args.fin_demo)
        print(f"[release] {OUT_RELEASE.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
