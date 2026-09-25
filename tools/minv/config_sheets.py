"""M-INV · Capa de configuración (oculta): 01_CONFIG, 03_CATEGORIAS, 06_UNIDADES."""
from __future__ import annotations

import datetime as dt

import demo_data as D

from .base import (C, ESTADOS, HDR, S_CONFIG, VERSION, Ctx, band, header_tip, q, toolbar_row)


def config_band(ctx: Ctx, ws, title, subtitle, widths):
    for i, w in enumerate(widths):
        ws.set_column_pixels(i, i, w)
    band(ctx, ws, title, subtitle, None, nav=False)
    toolbar_row(ws, ctx.st, 24)
    ws.hide_gridlines(2)
    ws.set_zoom(100)


def simple_table(ctx: Ctx, ws, first_row, name, headers, rows, formats, tips=None):
    """Tabla de configuración normal: crece sola al editarla con la hoja desprotegida."""
    st = ctx.st
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
    for c, tip in enumerate(tips or []):
        if tip:
            header_tip(ws, first_row, 1 + c, tip[0], tip[1])


def build_config(ctx: Ctx):
    wb, ws, st, demo = ctx.wb, ctx.sheets[S_CONFIG], ctx.st, ctx.demo
    config_band(ctx, ws, "Configuración del sistema",
                "Capa de configuración (oculta) · Solo administrador · Cambios aquí afectan todo el libro",
                [24, 250, 260, 520, 24])
    lbl = st(bold=True, font_color=C["text"], border=1, border_color=C["border"], indent=1)
    note = st(font_color=C["muted"], font_size=9, border=1, border_color=C["border"], indent=1, text_wrap=True)
    sec = st(bold=True, font_color=C["brand"], font_size=10.5)
    ws.write_string(6, 1, "PARÁMETROS DEL TENANT", sec)
    params = [
        ("cfgEmpresa", "Empresa (cliente)", D.EMPRESA_DEMO if demo else "NOMBRE DE SU EMPRESA", None,
         "Nombre que aparece en la portada y en los documentos."),
        ("cfgNIT", "NIT / Identificación", D.NIT_DEMO if demo else "", None, "Identificación tributaria del cliente."),
        ("cfgBodega", "Sede / Bodega", D.BODEGA_DEMO if demo else "Bodega principal", None,
         "Ubicación física que controla este libro (un libro por bodega en V1)."),
        ("cfgMoneda", "Moneda", "COP", None, "Informativo. Los formatos de valor usan el símbolo $."),
        ("cfgMargenAlerta", "Margen de alerta preventiva", D.MARGEN_ALERTA, "0%",
         "Un producto pasa a BAJO cuando su stock es ≤ Mínimo × (1 + margen)."),
        ("cfgDiasSinRotacion", "Días para considerar sin rotación", 60, '0" días"',
         "Productos con stock y sin movimientos en este plazo se marcan como inventario inmovilizado."),
        ("cfgFechaMin", "Fecha mínima permitida", dt.date(2020, 1, 1), "dd/mm/yyyy",
         "Fechas anteriores son rechazadas por la validación de la bitácora y del formulario."),
        ("cfgVersion", "Versión del sistema", VERSION, None, "Versión del motor M-INV."),
        ("cfgEdicion", "Edición", "Plus" if ctx.plus else "Estándar", None,
         "Estándar = .xlsx sin macros · Plus = .xlsm con formulario guiado y automatizaciones."),
        ("cfgFechaBuild", "Fecha de generación", ctx.fin, "dd/mm/yyyy", "Fecha en que se generó este libro."),
        ("cfgProveedor", "Implementado por", "Z&P Software Fast Solutions", None, "Proveedor del software."),
        ("cfgSelladoHasta", "Bitácora sellada hasta el ID", 0, "0",
         "Lo actualiza la edición Plus al sellar registros (no editar a mano)."),
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
        elif value:
            ws.write_string(r, 2, value, f)
        else:
            ws.write_blank(r, 2, None, f)
        ws.write_string(r, 3, desc, note)
        wb.define_name(name, f"={q(S_CONFIG)}!$C${r + 1}")
        if name == "cfgMargenAlerta":
            ws.data_validation(r, 2, r, 2, {"validate": "decimal", "criteria": "between", "minimum": 0,
                                            "maximum": 1, "input_title": "Margen de alerta",
                                            "input_message": "Valor entre 0% y 100%."})
        if name == "cfgDiasSinRotacion":
            ws.data_validation(r, 2, r, 2, {"validate": "integer", "criteria": "between", "minimum": 1,
                                            "maximum": 3650, "input_title": "Días sin rotación",
                                            "input_message": "Número entero de días (1 a 3.650)."})

    r0 = 8 + len(params) + 2
    ws.write_string(r0 - 1, 1, "TIPOS DE MOVIMIENTO  (definen el FactorStock de la bitácora)", sec)
    center = st(align="center", bold=True, border=1, border_color=C["border"])
    txt = st(indent=1, border=1, border_color=C["border"], text_wrap=True)
    simple_table(ctx, ws, r0, "tblTiposMov", ["Tipo", "FactorStock", "Descripción"],
                 [list(t) for t in D.TIPOS_MOVIMIENTO],
                 [st(bold=True, indent=1, border=1, border_color=C["border"]),
                  st(align="center", bold=True, num_format="+0;-0;0", border=1, border_color=C["border"]), txt])

    r1 = r0 + len(D.TIPOS_MOVIMIENTO) + 3
    ws.write_string(r1 - 1, 1, "SEMÁFORO DE STOCK  (reglas de 15_STOCK, 16_ALERTAS y 18_PEDIDO)", sec)
    ws.set_column_pixels(4, 4, 220)
    ws.set_column_pixels(5, 5, 24)
    simple_table(ctx, ws, r1, "tblEstados", ["Estado", "Prioridad", "Regla", "Acción"],
                 [[e[0], e[1], e[6], e[7]] for e in ESTADOS],
                 [st(bold=True, indent=1, border=1, border_color=C["border"]), center, txt, txt])
    for i, e in enumerate(ESTADOS):
        ws.write_string(r1 + 1 + i, 1, e[0], st(bold=True, indent=1, font_color=e[3], bg_color=e[4],
                                                 border=1, border_color=C["border"]))

    r2 = r1 + len(ESTADOS) + 3
    ws.write_string(r2 - 1, 1, "RESPONSABLES  (listas de la bitácora, el formulario y la toma física)", sec)
    gente = D.RESPONSABLES_DEMO if demo else D.RESPONSABLES_BASE
    simple_table(ctx, ws, r2, "tblResponsables", ["Nombre", "Cargo"], [list(p) for p in gente],
                 [st(bold=True, indent=1, border=1, border_color=C["border"]), txt])
    for r in range(len(gente)):
        ws.set_row_pixels(r2 + 1 + r, 22)


def build_categorias(ctx: Ctx, ws):
    st = ctx.st
    config_band(ctx, ws, "Categorías", "Capa de configuración (oculta) · Primer nivel de la lista en cascada",
                [24, 110, 240, 420, 24])
    cats = D.CATEGORIAS_DEMO if ctx.demo else D.CATEGORIAS_BASE
    txt = st(indent=1, border=1, border_color=C["border"])
    simple_table(ctx, ws, HDR - 1, "tblCategorias", ["Código", "Categoría", "Descripción"], [list(c) for c in cats],
                 [st(bold=True, align="center", border=1, border_color=C["border"]),
                  st(bold=True, indent=1, border=1, border_color=C["border"]), txt],
                 tips=[("Código", "Abreviatura interna (opcional)."),
                       ("Categoría", "Nombre visible en las listas. No lo cambie si ya tiene productos asociados."),
                       ("Descripción", "Texto de apoyo.")])


def build_unidades(ctx: Ctx, ws):
    st = ctx.st
    config_band(ctx, ws, "Unidades de medida",
                "Capa de configuración (oculta) · Define si la cantidad admite decimales",
                [24, 90, 160, 110, 320, 24])
    txt = st(indent=1, border=1, border_color=C["border"])
    simple_table(ctx, ws, HDR - 1, "tblUnidades", ["Código", "Unidad", "Decimales", "Descripción"],
                 [list(u) for u in D.UNIDADES],
                 [st(bold=True, align="center", border=1, border_color=C["border"]), txt,
                  st(align="center", border=1, border_color=C["border"]), txt],
                 tips=[("Código", "Abreviatura que ve el operador (UND, KG...)."),
                       ("Unidad", "Nombre completo de la unidad."),
                       ("Decimales", "SI = admite fracciones (KG, LT...). NO = solo enteros."),
                       ("Descripción", "Texto de apoyo.")])
