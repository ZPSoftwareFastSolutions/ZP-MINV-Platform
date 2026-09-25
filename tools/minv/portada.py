"""M-INV · 00_PORTADA: tablero tipo aplicación (asistente, mosaicos, KPIs, gráficos y últimos movimientos)."""
from __future__ import annotations

from xlsxwriter.utility import xl_col_to_name

from .base import (BRAND, C, EST, FONT, FONT_SB, S_ALERT, S_AYUDA, S_CONTEO, S_KPI, S_PEDIDO, S_PROD, S_STOCK,
                   VERSION, Ctx, decimals_cf, icon, q, section_label, textbox)

GRID = [24] + [94] * 12 + [24]   # A | B..M | N  (1.176 px de contenido)
ROWS = {0: 12, 1: 34, 2: 22, 3: 14, 4: 16, 5: 44, 6: 14, 7: 86, 8: 18, 9: 24, 10: 100, 11: 16, 12: 100, 13: 22,
        14: 24, 15: 290, 16: 16, 17: 290, 18: 34, 19: 26, 28: 22, 29: 40, 30: 14}
for _r in range(20, 28):
    ROWS[_r] = 26
R_BANNER, R_TILES, R_SEC1, R_CARD1, R_CARD2, R_SEC2, R_CH1, R_CH2, R_SEC3, R_THEAD, R_FOOT = (
    5, 7, 9, 10, 12, 14, 15, 17, 18, 19, 29)

TILES = [  # etiqueta, icono, color, destino, ayuda, texto vinculado
    ("REGISTRAR", "movimiento", "#1565C0", "internal:irRegistrar", "Registrar entrada, salida o ajuste", None),
    ("CONSULTAR", "buscar", "#283593", "internal:irConsultar", "Consultar un producto (kardex)", None),
    ("STOCK", "stock", "#00796B", f"internal:{q(S_STOCK)}!A1", "Ver el stock actual", None),
    ("", "alertas", "#C62828", f"internal:{q(S_ALERT)}!A1", "Productos que requieren acción", "txtAlertasBtn"),
    ("PEDIDO", "pedido", "#BF360C", f"internal:{q(S_PEDIDO)}!A1", "Pedido sugerido de compra", None),
    ("CONTEO", "conteo", "#2E7D32", f"internal:{q(S_CONTEO)}!A1", "Toma física de inventario", None),
    ("CATÁLOGO", "catalogo", "#5E35B1", f"internal:{q(S_PROD)}!A1", "Productos y proveedores", None),
    ("GUÍA", "ayuda", "#455A64", f"internal:{q(S_AYUDA)}!A1", "Aprenda a usar M-INV", None),
]
CARDS = [  # título, valor, nota, acento, color del valor, destino
    ("PRODUCTOS ACTIVOS", "kpiActivos", "txtActivosNota", C["brand"], C["ink"], f"{q(S_STOCK)}!A1"),
    ("AGOTADOS", "kpiAgotados", "txtAgotNota", "#B71C1C", "#B71C1C", f"{q(S_ALERT)}!A1"),
    ("STOCK CRÍTICO", "kpiCriticos", "txtCritNota", "#E53935", "#D32F2F", f"{q(S_ALERT)}!A1"),
    ("ALERTA PREVENTIVA", "kpiBajos", "txtBajosNota", "#F59E0B", C["amber"], f"{q(S_ALERT)}!A1"),
    ("STOCK ÓPTIMO", "kpiOptimos", "txtOptNota", "#43A047", C["green"], f"{q(S_STOCK)}!A1"),
    ("VALOR DEL INVENTARIO", "kpiValor", "txtValorNota", C["brand_dk"], C["brand_dk"], f"{q(S_STOCK)}!A1"),
    ("MOVIMIENTOS DEL MES", "kpiMovMes", "txtMovNota", C["teal"], C["teal"], "irBitacora"),
    ("SIN ROTACIÓN", "kpiSinRotacion", "txtSinRotNota", "#8D6E63", "#6D4C41", f"{q(S_STOCK)}!A1"),
]


def chart_frame(ws, x, y, w, h, row, desc):
    textbox(ws, "", x, y, w, h, tag="bg", desc=desc, fill=C["white"], anchor=(row, 1))


def base_chart(chart, title):
    chart.set_title({"name": title, "overlay": False,
                     "name_font": {"name": FONT, "size": 10.5, "bold": True, "color": C["text"]}})
    chart.set_chartarea({"border": {"none": True}, "fill": {"none": True}})
    chart.set_plotarea({"border": {"none": True}, "fill": {"none": True}})


def axis_font(color=None):
    return {"name": FONT, "size": 8.5, "color": color or C["muted"]}


def build_portada(ctx: Ctx, n_cat: int):
    ws, st, wb = ctx.sheets["00_PORTADA"], ctx.st, ctx.wb
    for i, w in enumerate(GRID):
        ws.set_column_pixels(i, i, w)
    ws.set_column(len(GRID), 16383, None, None, {"hidden": True})
    ws.hide_gridlines(2)
    ws.hide_row_col_headers()
    ws.set_default_row(hide_unused_rows=True)
    ws.set_zoom(100)
    canvas, ink = st(bg_color=C["canvas"]), st(bg_color=C["ink"])
    for r, h in ROWS.items():
        ws.set_row_pixels(r, h, ink if r <= 3 else canvas)
    kq = q(S_KPI)

    # --- héroe
    ws.insert_image(0, 1, str(BRAND / "tenant-logo-placeholder@2x.png"),
                    {"x_offset": 0, "y_offset": 17, "x_scale": 0.3125, "y_scale": 0.3125, "object_position": 2,
                     "description": "[logo] Logo del cliente (reemplazable)"})
    ws.write_string(1, 3, "M-INV · Sistema de Inventarios", st(bg_color=C["ink"], font_color=C["white"],
                                                               font_name=FONT_SB, font_size=18))
    ws.write_formula(2, 3, "=txtEmpresa", st(bg_color=C["ink"], font_color=C["band_sub"], font_size=10,
                                             formula=True))
    ws.merge_range(1, 9, 1, 12, "=TODAY()", st(bg_color=C["ink"], font_color=C["nav_text"], font_size=10,
                                               align="right", num_format='"Hoy · "dd/mm/yyyy', formula=True))
    ws.merge_range(2, 9, 2, 12, f"M-INV V{VERSION}", st(bg_color=C["ink"], font_color=C["band_sub"], font_size=8.5,
                                                        align="right"))
    edition = "EDICIÓN PLUS" if ctx.plus else "EDICIÓN ESTÁNDAR"
    textbox(ws, edition, 884, 27, 140, 26, tag="chip", desc=f"Edición del libro: {edition}",
            fill=C["teal"] if ctx.plus else C["nav"], font={"name": FONT_SB, "size": 8.5, "color": C["white"]},
            tip="Plus = formulario guiado y automatizaciones (.xlsm) · Estándar = sin macros (.xlsx)")
    if ctx.demo:
        textbox(ws, "DATOS DE DEMOSTRACIÓN", 700, 27, 176, 26, tag="chip", desc="Aviso: libro con datos demo",
                fill="#F59E0B", font={"name": FONT_SB, "size": 8.5, "color": C["ink"]},
                tip="Este libro Core contiene datos ficticios para demostración")

    # --- asistente «próximo paso»
    r = R_BANNER
    ws.write_string(r, 1, "PRÓXIMO PASO", st(bold=True, font_size=8.5, align="center", bg_color=C["blue_lt"],
                                             font_color=C["brand"]))
    ws.merge_range(r, 2, r, 12, "=txtPaso", st(bold=True, font_size=10.5, indent=1, bg_color=C["blue_lt"],
                                              font_color=C["brand"], formula=True))
    colors = {1: (C["red_lt"], C["red"]), 2: (C["amber_lt"], C["amber"]), 3: (C["blue_lt"], C["brand"]),
              4: (C["green_lt"], C["green"])}
    for level, (bg, fg) in colors.items():
        ws.conditional_format(r, 1, r, 12, {"type": "formula", "criteria": f"=kpiPasoNivel={level}",
                                            "format": st.cf(bg_color=bg, font_color=fg)})
    textbox(ws, "", 10, 7, 168, 30, tag="btn", desc="Ir al próximo paso", fill=C["white"],
            line={"color": C["border"], "width": 1}, font={"name": FONT_SB, "size": 9.5, "color": C["brand_dk"]},
            textlink=ctx.kref("txtPasoBoton"), url="internal:irSiguientePaso",
            tip="Ir directamente a donde se resuelve este paso", anchor=(r, 11))

    # --- mosaicos de navegación
    tw, tg = 130, 12
    for i, (label, ico, color, url, tip, link) in enumerate(TILES):
        x = i * (tw + tg)
        textbox(ws, label, x, 0, tw, 86, tag="tile", desc=f"Botón {label or 'ALERTAS'}", fill=color,
                font={"name": FONT_SB, "size": 9.5, "color": C["white"]}, valign="bottom", url=url, tip=tip,
                textlink=ctx.kref(link) if link else None, anchor=(R_TILES, 1))
        icon(ws, ico, "blanco", x + (tw - 30) // 2, 14, 30, anchor=(R_TILES, 1), url=url, tip=tip)

    # --- tarjetas KPI
    section_label(ws, st, R_SEC1, 1, "INDICADORES CLAVE")
    ws.merge_range(R_SEC1, 9, R_SEC1, 12, "=txtIntegridad", st(bg_color=C["canvas"], font_size=9, bold=True,
                                                               align="right", font_color=C["green"], formula=True))
    ref = f"$J${R_SEC1 + 1}"
    ws.conditional_format(R_SEC1, 9, R_SEC1, 12, {"type": "formula", "criteria": f'=LEFT({ref},1)="✖"',
                                                  "format": st.cf(font_color=C["red"])})
    ws.conditional_format(R_SEC1, 9, R_SEC1, 12, {"type": "formula", "criteria": f'=LEFT({ref},1)="⚠"',
                                                  "format": st.cf(font_color=C["amber"])})
    cw, cg = 270, 16
    for i, (title, vname, fname, accent, vcolor, link) in enumerate(CARDS):
        row = R_CARD1 if i < 4 else R_CARD2
        x = (i % 4) * (cw + cg)
        url, tip = f"internal:{link}", f"Ver detalle: {title.lower()}"
        textbox(ws, "", x, 0, cw, 100, tag="card", desc=f"Tarjeta {title}", fill=C["white"], url=url, tip=tip,
                anchor=(row, 1))
        textbox(ws, "", x + 14, 22, 5, 56, tag="bar", desc=f"Acento {title}", fill=accent, anchor=(row, 1))
        textbox(ws, title, x + 30, 13, cw - 44, 18, tag="txt", desc=f"Título {title}", halign="left",
                font={"size": 8.5, "bold": True, "color": C["muted"]}, url=url, tip=tip, anchor=(row, 1))
        textbox(ws, "", x + 30, 32, cw - 44, 40, tag="txt", desc=f"Valor {title}", halign="left",
                font={"name": FONT_SB, "size": 22, "color": vcolor}, textlink=ctx.kref(vname), url=url, tip=tip,
                anchor=(row, 1))
        textbox(ws, "", x + 30, 72, cw - 44, 18, tag="txt", desc=f"Nota {title}", halign="left",
                font={"size": 8.5, "color": C["muted"]}, textlink=ctx.kref(fname), url=url, tip=tip,
                anchor=(row, 1))

    # --- gráficos
    section_label(ws, st, R_SEC2, 1, "ANÁLISIS DEL INVENTARIO")
    chart_frame(ws, 0, 0, 372, 290, R_CH1, "Marco gráfico salud")
    chart_frame(ws, 388, 0, 740, 290, R_CH1, "Marco gráfico actividad")
    chart_frame(ws, 0, 0, 556, 290, R_CH2, "Marco gráfico top valor")
    chart_frame(ws, 572, 0, 556, 290, R_CH2, "Marco gráfico categorías")

    salud = wb.add_chart({"type": "doughnut"})
    keys = ("AGOTADO", "CRÍTICO", "BAJO", "ÓPTIMO", "SOBRESTOCK", "INCONSISTENTE")
    salud.add_series({
        "name": "Salud del inventario",
        "categories": f"={kq}!$F$30:$F$35", "values": f"={kq}!$G$30:$G$35",
        "points": [{"fill": {"color": EST[k][5]}, "border": {"color": "#FFFFFF", "width": 1.5}} for k in keys],
        "data_labels": {"value": True, "num_format": "#,##0;;;",
                        "font": {"name": FONT, "size": 9, "bold": True, "color": "#FFFFFF"},
                        "custom": [{"font": {"name": FONT, "size": 9, "bold": True,
                                             "color": C["ink"] if k == "BAJO" else "#FFFFFF"}} for k in keys]},
    })
    salud.set_hole_size(58)
    base_chart(salud, "Salud del inventario (productos por semáforo)")
    salud.set_legend({"position": "right", "font": axis_font(C["text"])})
    salud.set_size({"width": 356, "height": 278})
    ws.insert_chart(R_CH1, 1, salud, {"x_offset": 8, "y_offset": 6, "object_position": 2,
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
    ws.insert_chart(R_CH1, 1, act, {"x_offset": 396, "y_offset": 6, "object_position": 2,
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
    ws.insert_chart(R_CH2, 1, top, {"x_offset": 8, "y_offset": 6, "object_position": 2,
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
    ws.insert_chart(R_CH2, 1, cat, {"x_offset": 580, "y_offset": 6, "object_position": 2,
                                    "description": "Gráfico semáforo por categoría"})

    # --- últimos movimientos
    section_label(ws, st, R_SEC3, 1, "ÚLTIMOS MOVIMIENTOS")
    textbox(ws, "Ver bitácora completa  ➜", 950, 6, 178, 22, tag="txt", desc="Ir a la bitácora", halign="right",
            font={"size": 9, "bold": True, "color": C["brand"]}, url="internal:irBitacora",
            tip="Abrir 10_MOVIMIENTOS en la siguiente fila libre", anchor=(R_SEC3, 1))
    h = st(bold=True, font_size=9, font_color=C["white"], bg_color=C["ink2"], align="center")
    hl = st(bold=True, font_size=9, font_color=C["white"], bg_color=C["ink2"], indent=1)
    layout = [("Fecha", 1, 1, h), ("Tipo", 2, 2, h), ("Producto", 3, 7, hl), ("Cantidad", 8, 8, h),
              ("Unidad", 9, 9, h), ("Responsable", 10, 11, hl), ("Documento", 12, 12, h)]
    for label, c0, c1, f in layout:
        if c0 == c1:
            ws.write_string(R_THEAD, c0, label, f)
        else:
            ws.merge_range(R_THEAD, c0, R_THEAD, c1, label, f)
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
        r = R_THEAD + 1 + j
        idx = f"kpiUltimoID-{j}"
        for label, c0, c1, _ in layout:
            formula = f'=IF({idx}<1,"",INDEX(tblMovimientos[{src[label]}],{idx})&"")'
            if label in ("Fecha", "Cantidad"):
                formula = f'=IF({idx}<1,"",INDEX(tblMovimientos[{src[label]}],{idx}))'
            if c0 == c1:
                ws.write_formula(r, c0, formula, fmts[label])
            else:
                ws.merge_range(r, c0, r, c1, formula, fmts[label])
    t0, t1 = R_THEAD + 2, R_THEAD + 9          # filas Excel de las 8 líneas
    for rng in (f"C{t0}:C{t1}", f"I{t0}:I{t1}"):
        ws.conditional_format(rng, {"type": "formula", "criteria": f"=AND(ISNUMBER($I{t0}),$I{t0}>0)",
                                    "format": st.cf(font_color=C["green"])})
        ws.conditional_format(rng, {"type": "formula", "criteria": f"=AND(ISNUMBER($I{t0}),$I{t0}<0)",
                                    "format": st.cf(font_color=C["red"])})
    decimals_cf(ws, st, f"I{t0}:I{t1}", f"I{t0}", "+#,##0.00;-#,##0.00;0")

    # --- pie
    ws.insert_image(R_FOOT, 1, str(BRAND / "zp-insignia@2x.png"),
                    {"x_offset": 0, "y_offset": 6, "x_scale": 0.16, "y_scale": 0.16, "object_position": 2,
                     "description": "[logo] Z&P Software Fast Solutions"})
    ws.merge_range(R_FOOT, 1, R_FOOT, 8, "", st(bg_color=C["canvas"]))
    ws.write_rich_string(R_FOOT, 1,
                         st(bg_color=C["canvas"], font_size=8.5, bold=True, font_color=C["text"]),
                         f"            M-INV V{VERSION}",
                         st(bg_color=C["canvas"], font_size=8.5, font_color=C["muted"]),
                         "  ·  CQRS: escritura en 10_MOVIMIENTOS → lectura en 15_STOCK y 16_ALERTAS  ·  "
                         "Z&P Software Fast Solutions",
                         st(bg_color=C["canvas"], font_size=8.5, font_color=C["muted"]))
    ws.merge_range(R_FOOT, 9, R_FOOT, 12, "=txtBitacora", st(bg_color=C["canvas"], font_size=8.5,
                                                             font_color=C["muted"], align="right", formula=True))
    ws.set_selection(0, 0, 0, 0)
