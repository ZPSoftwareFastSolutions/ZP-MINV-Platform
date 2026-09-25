"""
M-INV · 17_KARDEX: consulta de producto (proyección de lectura).

Buscador (categoría + texto) → ficha del producto → gráfico de evolución del stock → historial de los últimos
100 movimientos con su saldo. Todo son fórmulas sobre la bitácora; nada se escribe aquí salvo el selector.
"""
from __future__ import annotations

from .base import (C, ESTADOS, FONT_SB, MAX_KARDEX, S_KARDEX, S_KPI, Ctx, action_button, app_sheet, band, chip,
                   check_msg, decimals_cf, estado_cf, note, q, row_cf, section_label, textbox, toolbar_row)

GRID = [24] + [94] * 12 + [120]
KX_HDR = 21                       # fila Excel del encabezado del historial
KX_FIRST = KX_HDR + 1             # primera fila del historial
KX_LAST = KX_FIRST + MAX_KARDEX - 1


def build_kardex(ctx: Ctx):
    ws, st, wb = ctx.sheets[S_KARDEX], ctx.st, ctx.wb
    app_sheet(ws, GRID)
    band(ctx, ws, "Consulta de producto",
         "Capa de lectura · Busque un producto y vea su ficha, su evolución y su historial (kardex)",
         "consultar", widths=GRID)
    toolbar_row(ws, st)
    canvas = st(bg_color=C["canvas"])
    heights = {5: 12, 6: 20, 7: 34, 8: 20, 9: 14}
    for r in range(10, 18):
        heights[r] = 30
    heights.update({18: 18, 19: 30, KX_HDR - 1: 28})
    for r, h in heights.items():
        ws.set_row_pixels(r, h, canvas)

    # --- barra de herramientas
    chip(ws, 16, 330, textlink=ctx.kref("txtKardexResumen"), desc="Resumen de la consulta")
    if ctx.plus:
        action_button(ws, "＋  Registrar movimiento de este producto", 356, 320, action="RegistrarDesdeKardex",
                      tip="Abrir el formulario con este producto ya elegido",
                      desc="Acción: registrar desde la consulta")
    else:
        action_button(ws, "＋  Registrar movimiento", 356, 220, url="internal:irRegistrar",
                      tip="Ir a la siguiente fila libre de la bitácora")
    note(ws, "Tip: en Buscar escriba parte del nombre o del SKU; en Producto elija con Alt + ↓.", 692, 480)

    # --- selector
    lbl = st(font_size=8.5, bold=True, font_color=C["muted"], bg_color=C["canvas"], indent=1)
    ws.merge_range(6, 1, 6, 3, "CATEGORÍA  (opcional)", lbl)
    ws.merge_range(6, 4, 6, 6, "BUSCAR  (nombre o SKU)", lbl)
    ws.merge_range(6, 7, 6, 12, "PRODUCTO", lbl)
    ws.merge_range(7, 1, 7, 3, "", st.field())
    ws.merge_range(7, 4, 7, 6, "", st.field())
    ws.merge_range(7, 7, 7, 12, ctx.layout.get("kardex_default", ""), st.field(bold=True))
    wb.define_name("kxCategoria", f"={q(S_KARDEX)}!$B$8")
    wb.define_name("kxBuscar", f"={q(S_KARDEX)}!$E$8")
    wb.define_name("kxProducto", f"={q(S_KARDEX)}!$H$8")
    for col, opts, title, msg in (
        (1, {"validate": "list", "source": "=lstCategorias"}, "Categoría (opcional)",
         "Filtra la lista de productos."),
        (4, {"validate": "length", "criteria": "<=", "value": 40}, "Buscar",
         "Escriba parte del nombre o del SKU y pulse Enter; luego elija en Producto."),
        (7, {"validate": "list", "source": "=lfKardex", "error_title": "Producto no válido",
             "error_message": "Elija un producto de la lista (Alt + ↓)."}, "Producto",
         "Elija el producto a consultar (incluye descontinuados)."),
    ):
        check_msg(title, msg)
        ws.data_validation(7, col, 7, col, {**opts, "input_title": title, "input_message": msg})
    ws.merge_range(8, 7, 8, 12, "=txtKardexCoinciden", st(font_size=8.5, font_color=C["muted"], bg_color=C["canvas"],
                                                          indent=1, italic=True, formula=True))

    # --- ficha (izquierda)
    card = dict(bg_color=C["white"], formula=True)
    edge_l = dict(left=1, left_color=C["border"])
    edge_r = dict(right=1, right_color=C["border"])
    ws.merge_range(10, 1, 10, 6, '=IF(kxExiste,kxProducto,"Elija un producto para ver su ficha")',
                   st(font_name=FONT_SB, font_size=13, indent=1, top=1, top_color=C["border"], **edge_l, **edge_r,
                      **card))
    ws.merge_range(11, 1, 11, 6, "=txtKardexMeta", st(font_size=9, font_color=C["muted"], indent=1, **edge_l,
                                                       **edge_r, **card))
    small = dict(font_size=8, bold=True, font_color=C["muted"], align="center", **card)
    ws.merge_range(12, 1, 12, 2, "STOCK ACTUAL", st(**edge_l, **small))
    ws.merge_range(12, 3, 12, 4, "ESTADO", st(**small))
    ws.merge_range(12, 5, 12, 6, "VALOR", st(**edge_r, **small))
    ws.merge_range(13, 1, 14, 2, '=IF(kxExiste,INDEX(tblStock[StockActual],kxFila),"—")',
                   st(font_name=FONT_SB, font_size=24, align="center", num_format="#,##0;-#,##0;0", **edge_l,
                      **card))
    ws.merge_range(13, 3, 14, 4, '=IF(kxExiste,INDEX(tblStock[Estado],kxFila),"")',
                   st(bold=True, align="center", font_size=10, **card))
    ws.merge_range(13, 5, 14, 6, '=IF(kxExiste,INDEX(tblStock[ValorInventario],kxFila),"")',
                   st(font_name=FONT_SB, font_size=15, align="center", num_format="$ #,##0", font_color=C["brand_dk"],
                      **edge_r, **card))
    ws.write_formula(15, 1, '=IF(kxExiste,"MÍN "&INDEX(tblStock[StockMin],kxFila),"")', st(**edge_l, **small))
    ws.write_formula(15, 2, '=IF(kxExiste,"MÁX "&INDEX(tblStock[StockMax],kxFila),"")', st(**small))
    ws.merge_range(15, 3, 15, 4, "ÚLTIMO MOVIMIENTO", st(**small))
    ws.merge_range(15, 5, 15, 6, "DÍAS SIN MOVIMIENTO", st(**edge_r, **small))
    ws.merge_range(16, 1, 16, 2, '=IF(kxExiste,INDEX(tblStock[Unidad],kxFila),"")',
                   st(align="center", font_size=10, bold=True, font_color=C["muted"], **edge_l, **card))
    ws.merge_range(16, 3, 16, 4, '=IF(kxExiste,INDEX(tblStock[UltimoMov],kxFila),"")',
                   st(align="center", font_size=11, bold=True, num_format="dd/mm/yyyy", **card))
    ws.merge_range(16, 5, 16, 6, '=IF(kxExiste,INDEX(tblStock[DiasSinMov],kxFila),"")',
                   st(align="center", font_size=11, bold=True, num_format='[=1]0" día";0" días"', **edge_r, **card))
    ws.merge_range(17, 1, 17, 6, "=txtKardexTotales", st(font_size=9, font_color=C["muted"], align="center",
                                                          bottom=1, bottom_color=C["border"], **edge_l, **edge_r,
                                                          **card))
    estado_cf(ws, st, "D14:E15", "$D$14")
    for e in ESTADOS:
        color = e[4] if e[3] == "#FFFFFF" else e[3]
        ws.conditional_format("B14:C15", {"type": "formula", "criteria": f'=$D$14="{e[0]}"',
                                          "format": st.cf(font_color=color)})
    decimals_cf(ws, st, "B14:C15", "$B$14", "#,##0.00")
    ws.conditional_format("F17:G17", {"type": "formula",
                                      "criteria": '=AND(ISNUMBER($F$17),$F$17>cfgDiasSinRotacion,kxStock>0)',
                                      "format": st.cf(font_color=C["amber"], bg_color=C["amber_lt"])})

    # --- gráfico de evolución (derecha)
    textbox(ws, "", 0, 0, 564, 240, tag="bg", desc="Marco gráfico evolución", fill=C["white"], anchor=(10, 7))
    kq = q(S_KPI)
    xcol, ycol, r0, r1 = ctx.layout["kardex_chart"]
    ch = wb.add_chart({"type": "scatter", "subtype": "straight_with_markers"})
    ch.add_series({"name": "Saldo", "categories": f"={kq}!${xcol}${r0}:${xcol}${r1}",
                   "values": f"={kq}!${ycol}${r0}:${ycol}${r1}",
                   "line": {"color": C["brand"], "width": 2.25},
                   "marker": {"type": "circle", "size": 5, "fill": {"color": C["white"]},
                              "border": {"color": C["brand"], "width": 1.5}}})
    ch.set_title({"name": "Evolución del stock (saldo después de cada movimiento)", "overlay": False,
                  "name_font": {"name": "Segoe UI", "size": 10, "bold": True, "color": C["text"]}})
    ch.set_legend({"none": True})
    ch.set_x_axis({"num_format": "dd/mm", "num_font": {"name": "Segoe UI", "size": 8, "color": C["muted"]},
                   "line": {"color": "#CBD5E1"}, "major_gridlines": {"visible": False}})
    ch.set_y_axis({"num_font": {"name": "Segoe UI", "size": 8, "color": C["muted"]}, "line": {"none": True},
                   "major_gridlines": {"visible": True, "line": {"color": "#E5E7EB"}}, "min": 0})
    ch.set_chartarea({"border": {"none": True}, "fill": {"none": True}})
    ch.set_plotarea({"border": {"none": True}, "fill": {"none": True}})
    ch.set_size({"width": 548, "height": 228})
    ws.insert_chart(10, 7, ch, {"x_offset": 8, "y_offset": 6, "object_position": 2,
                                "description": "Gráfico de evolución del stock del producto"})

    # --- historial
    section_label(ws, st, 19, 1, "HISTORIAL DE MOVIMIENTOS  (del más reciente al más antiguo)")
    ws.merge_range(19, 9, 19, 12, "=txtKardexMostrando", st(font_size=9, font_color=C["muted"], bg_color=C["canvas"],
                                                            align="right", formula=True))
    h = st(bold=True, font_size=9, font_color=C["white"], bg_color=C["ink2"], align="center")
    hl = st(bold=True, font_size=9, font_color=C["white"], bg_color=C["ink2"], indent=1)
    layout = [("ID", 1, 1, h), ("Fecha", 2, 2, h), ("Tipo", 3, 3, h), ("Entrada (+)", 4, 4, h),
              ("Salida (-)", 5, 5, h), ("Saldo", 6, 6, h), ("Documento", 7, 7, h), ("Responsable", 8, 9, hl),
              ("Observaciones", 10, 12, hl)]
    for label, c0, c1, f in layout:
        if c0 == c1:
            ws.write_string(KX_HDR - 1, c0, label, f)
        else:
            ws.merge_range(KX_HDR - 1, c0, KX_HDR - 1, c1, label, f)
    base = dict(font_size=9, formula=True)
    fmts = {
        "ID": st(align="center", font_color=C["faint"], **base),
        "Fecha": st(align="center", num_format="dd/mm/yyyy", **base),
        "Tipo": st(align="center", bold=True, **base),
        "Entrada": st(align="right", num_format="#,##0", indent=1, font_color=C["green"], bold=True, **base),
        "Salida": st(align="right", num_format="#,##0", indent=1, font_color=C["red"], bold=True, **base),
        "Saldo": st(align="right", num_format="#,##0", indent=1, bold=True, **base),
        "txt": st(indent=1, **base),
        "doc": st(align="center", font_color=C["muted"], **base),
    }
    for k in range(MAX_KARDEX):
        r = KX_FIRST + k
        rr = r - 1
        idc = f"$B{r}"
        net = f"INDEX(tblMovimientos[CantidadNeta],{idc})"
        ws.write_formula(rr, 1, f'=IF(kxSKU="","",IFERROR(_xlfn.AGGREGATE(14,6,tblMovimientos[ID]/'
                                f'(tblMovimientos[SKU]=kxSKU),{k + 1}),""))', fmts["ID"])
        ws.write_formula(rr, 2, f'=IF({idc}="","",INDEX(tblMovimientos[Fecha],{idc}))', fmts["Fecha"])
        ws.write_formula(rr, 3, f'=IF({idc}="","",INDEX(tblMovimientos[Tipo],{idc}))', fmts["Tipo"])
        ws.write_formula(rr, 4, f'=IF({idc}="","",IF({net}>0,{net},""))', fmts["Entrada"])
        ws.write_formula(rr, 5, f'=IF({idc}="","",IF({net}<0,-{net},""))', fmts["Salida"])
        ws.write_formula(rr, 6, f'=IF({idc}="","",INDEX(tblMovimientos[Saldo],{idc}))', fmts["Saldo"])
        ws.write_formula(rr, 7, f'=IF({idc}="","",INDEX(tblMovimientos[Documento],{idc})&"")', fmts["doc"])
        ws.merge_range(rr, 8, rr, 9, f'=IF({idc}="","",INDEX(tblMovimientos[Responsable],{idc})&"")', fmts["txt"])
        ws.merge_range(rr, 10, rr, 12, f'=IF({idc}="","",INDEX(tblMovimientos[Observaciones],{idc})&"")',
                       fmts["txt"])
        ws.set_row_pixels(rr, 22)
    first, last = KX_FIRST, KX_LAST
    ws.conditional_format(f"D{first}:D{last}", {"type": "formula", "criteria": f'=ISNUMBER($E{first})',
                                                "format": st.cf(font_color=C["green"])})
    ws.conditional_format(f"D{first}:D{last}", {"type": "formula", "criteria": f'=ISNUMBER($F{first})',
                                                "format": st.cf(font_color=C["red"])})
    row_cf(ws, st, f"B{first}:M{last}", f"$B{first}")
    for c in "EFG":  # enteros sin separador decimal; decimales con dos cifras (como la bitácora)
        decimals_cf(ws, st, f"{c}{first}:{c}{last}", f"{c}{first}", "#,##0.00")
    ws.set_row_pixels(KX_LAST, 16, canvas)
    ws.set_selection("H8")
