"""
M-INV · Núcleo compartido del generador: constantes, paleta, estilos, contexto de build y
componentes visuales reutilizables (franja superior, navegación, chips, tablas, formatos).
"""
from __future__ import annotations

import datetime as dt
from dataclasses import dataclass, field
from pathlib import Path

import xlsxwriter
from xlsxwriter.utility import xl_col_to_name
from xlsxwriter.worksheet import Worksheet

ROOT = Path(__file__).resolve().parents[2]
VERSION = "1.2.0"
DEV_PASSWORD = "minv-dev"
ICONS = ROOT / "assets" / "icons"
BRAND = ROOT / "assets" / "branding"

MAX_PROD = 500      # capacidad del catálogo (filas pre-asignadas)
MAX_MOV = 5000      # capacidad de la bitácora
MAX_PROV = 100      # capacidad del maestro de proveedores
MAX_KARDEX = 100    # movimientos visibles en la consulta de producto
HDR = 7             # fila Excel del encabezado de tablas
FIRST = 8           # primera fila Excel de datos
LAST_PROD = FIRST + MAX_PROD - 1
LAST_MOV = FIRST + MAX_MOV - 1
LAST_PROV = FIRST + MAX_PROV - 1

FONT = "Segoe UI"
FONT_SB = "Segoe UI Semibold"

S_PORTADA, S_CONFIG, S_CAT, S_PROV, S_PROD, S_UNI = (
    "00_PORTADA", "01_CONFIG", "03_CATEGORIAS", "04_PROVEEDORES", "05_PRODUCTOS", "06_UNIDADES")
S_MOV, S_REG, S_CONTEO = "10_MOVIMIENTOS", "12_REGISTRO", "13_CONTEO"
S_STOCK, S_ALERT, S_KARDEX, S_PEDIDO = "15_STOCK", "16_ALERTAS", "17_KARDEX", "18_PEDIDO"
S_LISTAS, S_KPI, S_AYUDA = "90_LISTAS", "91_KPIS", "99_AYUDA"
HIDDEN_SHEETS = (S_CONFIG, S_CAT, S_UNI, S_LISTAS, S_KPI)

C = {
    "ink": "#0B1F33", "ink2": "#16324F", "nav": "#1C3A5A", "nav_text": "#DCE7F3", "band_sub": "#9FB3C8",
    "brand": "#1565C0", "brand_dk": "#0D47A1", "brand_lt": "#EAF3FE", "canvas": "#F3F5F8", "white": "#FFFFFF",
    "border": "#DDE3EA", "text": "#1F2937", "muted": "#5F6B7A", "faint": "#5F6E84",
    "input_border": "#8FB3DE", "calc_fill": "#F1F4F8", "calc_text": "#334155", "calc_border": "#E2E8F0",
    "hdr_in": "#1565C0", "hdr_calc": "#475569", "row_line": "#E5E7EB", "zebra": "#F8FAFC",
    "green": "#2E7D32", "green_lt": "#E8F5E9", "red": "#C62828", "red_lt": "#FDECEA",
    "amber": "#B45309", "amber_lt": "#FEF3C7", "amber_mid": "#F59E0B", "teal": "#00796B", "purple": "#5E35B1",
    "slate": "#455A64", "blue_lt": "#E3F2FD",
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
ACCIONABLES = ("INCONSISTENTE", "AGOTADO", "CRÍTICO", "BAJO")          # cuentan en kpiEnAlerta
REPONER = ("AGOTADO", "CRÍTICO", "BAJO")                                # entran al pedido sugerido
A_ACCIONABLES = '{"INCONSISTENTE","AGOTADO","CRÍTICO","BAJO"}'
A_REPONER = '{"AGOTADO","CRÍTICO","BAJO"}'
A_ALERTAS = '{"INCONSISTENTE","AGOTADO","CRÍTICO","BAJO","SOBRESTOCK"}'

FMT = {
    "text": {"align": "left", "indent": 1},
    "center": {"align": "center"},
    "date": {"align": "center", "num_format": "dd/mm/yyyy"},
    "qty": {"align": "right", "num_format": "#,##0", "indent": 1},
    "signed": {"align": "right", "num_format": "+#,##0;-#,##0;0", "indent": 1},
    "factor": {"align": "center", "num_format": "+0;-0;0", "bold": True},
    "money": {"align": "right", "num_format": "$ #,##0", "indent": 1},
    "money_signed": {"align": "right", "num_format": "$ #,##0;-$ #,##0;$ 0", "indent": 1},
    "pct": {"align": "right", "num_format": "0%", "indent": 1},
    "estado": {"align": "left", "bold": True, "indent": 1},  # el punto "● " lo añade el formato condicional
    "status": {"align": "left", "bold": True, "indent": 1},
    "id": {"align": "center", "font_color": C["faint"]},
    "strong": {"align": "right", "num_format": "#,##0", "bold": True, "indent": 1},
    "days": {"align": "center", "num_format": '0" d"'},
}


# ---------------------------------------------------------------------------
# Utilidades
# ---------------------------------------------------------------------------
class Styles:
    """Fábrica de formatos con caché. En Release oculta las fórmulas (Protección > Oculta)."""

    def __init__(self, wb: xlsxwriter.Workbook, hide_formulas: bool):
        self.wb, self.hide, self._cache, self._cf = wb, hide_formulas, {}, {}
        self.lock_inputs = False   # V2: los maestros y usuarios solo los edita el ADMIN (celdas ✎ bloqueadas)

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

    def hdr(self, kind: str):
        bg = C["hdr_in"] if kind == "in" else C["hdr_calc"]
        return self(bold=True, font_color=C["white"], bg_color=bg, align="center", text_wrap=True,
                    font_size=9.5, border=1, border_color=C["white"])

    def cell(self, kind: str, fmt: str, view: bool = False):
        p = dict(FMT[fmt])
        if kind == "in":
            p.update(bg_color=C["white"], border=1, border_color=C["input_border"], locked=self.lock_inputs)
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

    def field(self, **p):
        """Campo de formulario grande (✎)."""
        base = dict(bg_color=C["white"], border=1, border_color=C["input_border"], locked=False, indent=1,
                    font_size=11)
        base.update(p)
        return self(**base)


@dataclass
class Col:
    name: str                    # encabezado = nombre técnico (referencias estructuradas)
    kind: str                    # "in" (usuario) | "calc" (fórmula de tabla) | "mirror" (fórmula por fila)
    width: int                   # píxeles
    fmt: str = "text"
    help: str = ""               # mensaje de entrada del encabezado (ayuda contextual)
    formula: str | None = None   # fórmula de columna calculada
    mirror: str | None = None    # plantilla por fila con {r}


@dataclass
class Ctx:
    """Contexto de un build: libro, estilos, edición y registro de tablas/indicadores."""
    wb: xlsxwriter.Workbook
    st: Styles
    edition: str                 # "estandar" (.xlsx sin macros) | "plus" (base del .xlsm)
    demo: bool
    fin: dt.date
    password: str
    sheets: dict = field(default_factory=dict)
    tables: dict = field(default_factory=dict)   # nombre de tabla -> list[Col]
    kpi: dict = field(default_factory=dict)      # nombre definido -> fila Excel en 91_KPIS
    layout: dict = field(default_factory=dict)   # posiciones de bloques del motor (columnas de 91_KPIS/90_LISTAS)

    @property
    def plus(self) -> bool:
        return self.edition == "plus"

    def col(self, table: str, name: str) -> str:
        return letter(self.tables[table], name)

    def kref(self, name: str) -> str:
        return f"={q(S_KPI)}!$C${self.kpi[name]}"


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
    return sum(widths[:idx])


def legacy_hash(password: str) -> str:
    return Worksheet._encode_password(None, password)


def q(sheet: str) -> str:
    return f"'{sheet}'"


def check_msg(title: str, message: str):
    assert len(title) <= 32 and len(message) <= 255, (title, len(message))


# ---------------------------------------------------------------------------
# Componentes visuales
# ---------------------------------------------------------------------------
NAV = [  # clave, etiqueta, icono, destino, ayuda
    ("inicio", "Inicio", "inicio", f"internal:{q(S_PORTADA)}!A1", "Volver a la portada"),
    ("registrar", "Registrar", "movimiento", "internal:irRegistrar", "Registrar un movimiento"),
    ("bitacora", "Bitácora", "bitacora", "internal:irBitacora", "Ver la bitácora de movimientos"),
    ("stock", "Stock", "stock", f"internal:{q(S_STOCK)}!A1", "Ver el stock actual"),
    ("alertas", "Alertas", "alertas", f"internal:{q(S_ALERT)}!A1", "Productos que requieren acción"),
    ("consultar", "Consultar", "buscar", "internal:irConsultar", "Consultar un producto (kardex)"),
    ("pedido", "Pedido", "pedido", f"internal:{q(S_PEDIDO)}!A1", "Pedido sugerido de compra"),
    ("guia", "Guía", "ayuda", f"internal:{q(S_AYUDA)}!A1", "Guía rápida de uso"),
]
NAV_W, NAV_GAP = 104, 6


def textbox(ws, text, x, y, w, h, *, tag="txt", desc="", fill=None, line=None, font=None,
            halign="center", valign="middle", url=None, tip=None, textlink=None, anchor=(0, 0), action=None):
    """Cuadro de texto/forma. `tag` guía el post-proceso (esquinas, sombra, capas); `action` asigna una macro."""
    label = f"{tag}|{action}" if action else tag
    opts = {
        "x_offset": x, "y_offset": y, "width": w, "height": h, "object_position": 2,
        "fill": {"color": fill} if fill else {"none": True},
        "line": line or {"none": True},
        "font": {"name": FONT, "size": 10, "color": C["text"], **(font or {})},
        "align": {"vertical": valign, "horizontal": halign, "text": halign},
        "description": f"[{label}] {desc or text}".strip(),
    }
    if url:
        opts["url"] = url
    if tip:
        opts["tip"] = tip
    if textlink:
        opts["textlink"] = textlink
    ws.insert_textbox(anchor[0], anchor[1], text, opts)


def icon(ws, name, variant, x, y, size, *, anchor=(0, 0), url=None, tip=None):
    """Icono PNG @2x (64 px) superpuesto; el post-proceso lo lleva al frente."""
    opts = {"x_offset": x, "y_offset": y, "x_scale": size / 64, "y_scale": size / 64, "object_position": 2,
            "description": f"[icon] Icono {name}"}
    if url:
        opts["url"] = url
    if tip:
        opts["tip"] = tip
    ws.insert_image(anchor[0], anchor[1], str(ICONS / f"{name}-{variant}@2x.png"), opts)


def band(ctx: Ctx, ws, title: str, subtitle: str, active: str | None, widths=None, nav: bool = True):
    """Franja superior (filas 1-4): título, navegación con iconos a su derecha y subtítulo debajo."""
    st = ctx.st
    ink = st(bg_color=C["ink"])
    for r, h in ((0, 8), (1, 32), (2, 20), (3, 10)):
        ws.set_row_pixels(r, h, ink)
    ws.write_string(1, 1, title, st(bg_color=C["ink"], font_color=C["white"], font_name=FONT_SB, font_size=16))
    ws.write_string(2, 1, subtitle, st(bg_color=C["ink"], font_color=C["band_sub"], font_size=9))
    if not nav:
        return
    x0 = max(300, int(16 + len(title) * 10.5 + 28))
    if widths:
        assert x0 + len(NAV) * (NAV_W + NAV_GAP) <= sum(widths), f"navegación desborda la hoja '{title}'"
    for i, (key, label, ico, url, tip) in enumerate(NAV):
        on = key == active
        x = x0 + i * (NAV_W + NAV_GAP)
        textbox(ws, label, x, 11, NAV_W, 26, tag="pill", desc=f"Navegación: {tip}", halign="left",
                fill=C["white"] if on else C["nav"],
                font={"name": FONT_SB, "size": 9, "color": C["ink"] if on else C["nav_text"]}, url=url, tip=tip)
        icon(ws, ico, "tinta" if on else "blanco", x + 9, 16, 16, url=url, tip=tip)


def toolbar_row(ws, st: Styles, height: int = 34):
    canvas = st(bg_color=C["canvas"])
    ws.set_row_pixels(4, height, canvas)
    ws.set_row_pixels(5, 16, canvas)


def header_tip(ws, row, col, title, message):
    check_msg(title, message)
    ws.data_validation(row, col, row, col, {"validate": "any", "input_title": title, "input_message": message})


def chip(ws, x, w, *, text="", textlink=None, fill=None, color=None, bold=True, desc="Contador", tip=None,
         size=9, line=True):
    textbox(ws, text, x, 5, w, 24, tag="chip", desc=desc, fill=fill or C["white"],
            line={"color": C["border"], "width": 1} if line and not fill else None,
            font={"size": size, "bold": bold, "color": color or C["text"]}, textlink=textlink, tip=tip,
            anchor=(4, 0))


def action_button(ws, text, x, w, *, url=None, action=None, tip=None, fill=None, y=3, h=28, anchor=(4, 0),
                  desc=None, size=9.5):
    textbox(ws, text, x, y, w, h, tag="btn", desc=desc or text, fill=fill or C["brand"],
            font={"name": FONT_SB, "size": size, "color": C["white"]}, url=url, tip=tip, anchor=anchor,
            action=action)


def note(ws, text, x, w, *, y=5, desc="Nota", anchor=(4, 0)):
    textbox(ws, text, x, y, w, 24, tag="txt", desc=desc, halign="left",
            font={"size": 8.5, "color": C["muted"], "italic": True}, anchor=anchor)


def legend_pills(ws, x, y=5):
    """Leyenda de celdas de ingreso vs. cálculo (fila de herramientas)."""
    textbox(ws, "✎  Usted diligencia", x, y, 150, 24, tag="chip", desc="Leyenda: celda de ingreso",
            fill=C["white"], line={"color": C["input_border"], "width": 1},
            font={"size": 8.5, "bold": True, "color": C["brand"]}, anchor=(4, 0))
    textbox(ws, "ƒx  Cálculo automático", x + 158, y, 170, 24, tag="chip", desc="Leyenda: celda calculada",
            fill=C["calc_fill"], line={"color": C["calc_border"], "width": 1},
            font={"size": 8.5, "bold": True, "color": C["calc_text"]}, anchor=(4, 0))
    return x + 158 + 170


def build_table(ctx: Ctx, ws, name: str, cols: list[Col], nrows: int, view: bool = False, tips=True):
    st = ctx.st
    ctx.tables[name] = cols
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
            glyph = "✎" if c.kind == "in" else "ƒx"
            header_tip(ws, hdr0, col, f"{glyph} {c.name}"[:32], c.help)
        if c.kind == "in":
            f = st.cell("in", c.fmt)
            for r in range(nrows):
                ws.write_blank(hdr0 + 1 + r, col, None, f)
        elif c.mirror:
            f = st.cell(c.kind, c.fmt, view)
            for r in range(nrows):
                ws.write_formula(hdr0 + 1 + r, col, c.mirror.format(r=FIRST + r), f)


def app_sheet(ws, widths_px: list[int], zoom=100, headers=False):
    """Lienzo tipo aplicación: sin cuadrícula ni encabezados, columnas sobrantes ocultas."""
    ws.hide_gridlines(2)
    if not headers:
        ws.hide_row_col_headers()
    ws.set_zoom(zoom)
    for i, w in enumerate(widths_px):
        ws.set_column_pixels(i, i, w)
    ws.set_column(len(widths_px), 16383, None, None, {"hidden": True})
    ws.set_default_row(hide_unused_rows=True)


def print_setup(ws, title: str, repeat=True, landscape=True):
    if landscape:
        ws.set_landscape()
    ws.set_paper(1)
    ws.fit_to_pages(1, 0)
    ws.set_margins(left=0.4, right=0.4, top=0.7, bottom=0.6)
    if repeat:
        ws.repeat_rows(HDR - 1)
    ws.set_header(f'&L&"Segoe UI,Bold"&9M-INV · {title}&R&"Segoe UI,Regular"&8&D')
    ws.set_footer('&L&"Segoe UI,Regular"&8Z&&P Software Fast Solutions&R&"Segoe UI,Regular"&8Página &P de &N')


def section_label(ws, st: Styles, row: int, col: int, text: str, bg=None):
    bg = bg or C["canvas"]
    ws.write_rich_string(row, col, st(font_color=C["brand"], bold=True, font_size=10, bg_color=bg), "▍ ",
                         st(font_color=C["muted"], bold=True, font_size=9, bg_color=bg), text, st(bg_color=bg))


# ---------------------------------------------------------------------------
# Formatos condicionales reutilizables
# ---------------------------------------------------------------------------
def status_cf(ws, st, rng, ref):
    ws.conditional_format(rng, {"type": "formula", "criteria": f'=LEFT({ref},1)="✖"',
                                "format": st.cf(font_color=C["red"], bg_color=C["red_lt"], bold=True)})
    ws.conditional_format(rng, {"type": "formula", "criteria": f'=LEFT({ref},1)="⚠"',
                                "format": st.cf(font_color=C["amber"], bg_color=C["amber_lt"], bold=True)})
    ws.conditional_format(rng, {"type": "formula", "criteria": f'=LEFT({ref},1)="✔"',
                                "format": st.cf(font_color=C["green"])})
    ws.conditional_format(rng, {"type": "formula", "criteria": f'=LEFT({ref},1)="●"',
                                "format": st.cf(font_color=C["faint"])})
    ws.conditional_format(rng, {"type": "formula", "criteria": f'=LEFT({ref},1)="○"',
                                "format": st.cf(font_color=C["muted"])})


def decimals_cf(ws, st, rng, ref, num_format):
    ws.conditional_format(rng, {"type": "formula", "criteria": f"=AND(ISNUMBER({ref}),{ref}<>INT({ref}))",
                                "format": st.cf(num_format=num_format)})


def estado_cf(ws, st, rng, ref, solid=False):
    for e in ESTADOS:
        name, _, _, fg, bg = e[:5]
        solid_bg = bg if fg == "#FFFFFF" else fg
        fmt = st.cf(font_color="#FFFFFF", bg_color=solid_bg, bold=True) if solid else \
            st.cf(font_color=fg, bg_color=bg, bold=True, num_format='"● "@')
        ws.conditional_format(rng, {"type": "formula", "criteria": f'={ref}="{name}"', "format": fmt})


def row_cf(ws, st, rng, key):
    ws.conditional_format(rng, {"type": "formula", "criteria": f'=AND({key}<>"",MOD(ROW(),2)=0)',
                                "format": st.cf(bg_color=C["zebra"])})
    ws.conditional_format(rng, {"type": "formula", "criteria": f'={key}<>""',
                                "format": st.cf(bottom=1, bottom_color=C["row_line"])})


def estado_formula(stock: str, smin: str, smax: str, activo: str | None = None) -> str:
    """Regla del semáforo (idéntica en 15_STOCK, formulario y kardex)."""
    pairs = []
    if activo:
        pairs.append((f'{activo}="NO"', '"INACTIVO"'))
    pairs += [
        (f"{stock}<0", '"INCONSISTENTE"'),
        (f"{stock}=0", '"AGOTADO"'),
        (f"{stock}<={smin}", '"CRÍTICO"'),
        (f"{stock}<={smin}*(1+cfgMargenAlerta)", '"BAJO"'),
        (f"AND({smax}>0,{stock}>{smax})", '"SOBRESTOCK"'),
    ]
    return nested_if(pairs, '"ÓPTIMO"')


def sugerido_formula(stock: str, smin: str, smax: str) -> str:
    """Cantidad sugerida para reponer: hasta el máximo (o 2 × mínimo si no hay máximo)."""
    return f"MAX(0,IF({smax}>0,{smax},2*{smin})-MAX(0,{stock}))"
