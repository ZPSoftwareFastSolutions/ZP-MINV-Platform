"""
M-INV · 18_PEDIDO: pedido sugerido de compra (proyección de lectura, imprimible).

Toma los productos AGOTADO / CRÍTICO / BAJO, calcula la cantidad para volver al máximo y el costo estimado, y los
agrupa por proveedor. El filtro de proveedor permite imprimir un pedido por proveedor.
"""
from __future__ import annotations

from .base import (C, FONT_SB, MAX_PROD, S_KPI, S_PEDIDO, Ctx, app_sheet, band, chip, check_msg, decimals_cf,
                   estado_cf, note, q, row_cf, sugerido_formula, toolbar_row)

PD_HDR = 14                         # fila Excel del encabezado de líneas
PD_FIRST = PD_HDR + 1
PD_LAST = PD_FIRST + MAX_PROD - 1
SPEC = [  # (encabezado, ancho, clave)
    ("#", 40, "k"), ("Proveedor", 232, "prov"), ("SKU", 84, "SKU"), ("Producto", 270, "Producto"),
    ("Unidad", 84, "Unidad"), ("Stock", 84, "StockActual"), ("Mínimo", 90, "StockMin"), ("Máximo", 90, "StockMax"),
    ("Prioridad", 124, "Estado"), ("A pedir", 104, "sug"), ("Costo unit.", 110, "CostoUnitario"),
    ("Subtotal", 124, "sub"),
]
WIDTHS = [16] + [s[1] for s in SPEC] + [16]


def build_pedido(ctx: Ctx):
    ws, st, wb = ctx.sheets[S_PEDIDO], ctx.st, ctx.wb
    app_sheet(ws, WIDTHS)
    band(ctx, ws, "Pedido sugerido de compra",
         "Capa de lectura · Qué comprar, cuánto y a quién · Filtre por proveedor e imprima (Ctrl + P)",
         "pedido", widths=WIDTHS)
    toolbar_row(ws, st)
    canvas, white = st(bg_color=C["canvas"]), st(bg_color=C["white"])
    for r in range(5, 13):
        ws.set_row_pixels(r, 26 if r > 5 else 12, canvas if r in (5, 12) else white)
    ws.set_row_pixels(12, 12, canvas)

    chip(ws, 16, 190, textlink=ctx.kref("txtPedidoLineas"), desc="Líneas del pedido")
    chip(ws, 216, 300, textlink=ctx.kref("txtPedidoTotal"), desc="Total estimado", color=C["brand_dk"])
    msg = ("Doble clic en una línea: formulario de reposición ya diligenciado." if ctx.plus else
           "Para registrar la compra al recibirla use Registrar (tipo ENTRADA).")
    note(ws, msg, 532, 460)

    # --- encabezado del documento (izquierda: empresa y filtro; derecha: datos del pedido)
    wbg = dict(bg_color=C["white"], formula=True)
    ws.merge_range(6, 1, 6, 4, "=cfgEmpresa", st(font_name=FONT_SB, font_size=14, indent=1, **wbg))
    ws.merge_range(7, 1, 7, 4, '="NIT "&cfgNIT&"  ·  "&cfgBodega', st(font_size=9, font_color=C["muted"], indent=1,
                                                                        **wbg))
    ws.merge_range(8, 1, 8, 4, "Sugerido por M-INV a partir de las alertas de stock (reposición hasta el máximo).",
                   st(font_size=8.5, font_color=C["muted"], italic=True, indent=1, bg_color=C["white"]))
    ws.merge_range(9, 1, 9, 2, "Filtrar por proveedor:", st(font_size=9, bold=True, align="right", indent=1,
                                                            bg_color=C["white"]))
    ws.merge_range(9, 3, 9, 4, "(Todos)", st.field(bold=True, font_size=10))
    wb.define_name("pdProveedor", f"={q(S_PEDIDO)}!$D$10")
    title, msg = "Filtrar por proveedor", "(Todos) o un proveedor. Imprima un pedido por proveedor."
    check_msg(title, msg)
    ws.data_validation(9, 3, 9, 3, {"validate": "list", "source": "=lstFiltroProv", "input_title": title,
                                    "input_message": msg, "error_title": "Proveedor no válido",
                                    "error_message": "Elija (Todos) o un proveedor de la lista."})
    ws.merge_range(10, 1, 10, 4, "=txtPedidoContacto", st(font_size=9, indent=1, **wbg))
    ws.merge_range(11, 1, 11, 4, "=txtPedidoEntrega", st(font_size=9, bold=True, font_color=C["teal"], indent=1,
                                                          **wbg))
    ws.merge_range(6, 9, 6, 12, "PEDIDO SUGERIDO", st(font_name=FONT_SB, font_size=16, align="right", indent=1,
                                                       font_color=C["ink"], bg_color=C["white"]))
    ws.merge_range(7, 9, 7, 12, "=YEAR(TODAY())*10000+MONTH(TODAY())*100+DAY(TODAY())",
                   st(font_size=10, bold=True, align="right", indent=1, num_format='"N.º PS-"0', **wbg))
    ws.merge_range(8, 9, 8, 12, "=TODAY()", st(font_size=9, align="right", indent=1,
                                               num_format='"Fecha: "dd/mm/yyyy', **wbg))
    ws.merge_range(9, 9, 9, 12, "=kpiPedidoTotal", st(font_name=FONT_SB, font_size=14, align="right", indent=1,
                                                      font_color=C["brand_dk"], num_format='"Total estimado: $ "#,##0',
                                                      **wbg))
    ws.merge_range(10, 9, 10, 12, "=txtPedidoResumen", st(font_size=9, align="right", indent=1,
                                                          font_color=C["muted"], **wbg))
    ws.merge_range(11, 9, 11, 12, "Elaboró: ______________    Aprobó: ______________",
                   st(font_size=9, align="right", indent=1, font_color=C["muted"], bg_color=C["white"]))

    # --- líneas
    h = st(bold=True, font_size=9, font_color=C["white"], bg_color=C["ink2"], align="center", text_wrap=True)
    for i, (label, w, _) in enumerate(SPEC):
        ws.write_string(PD_HDR - 1, 1 + i, label, h)
    ws.set_row_pixels(PD_HDR - 1, 32)
    kq = q(S_KPI)
    rk = ctx.layout["rank_pedido"]
    base = dict(font_size=9.5, formula=True)
    fm = {
        "k": st(align="center", font_color=C["faint"], **base),
        "prov": st(indent=1, **base),
        "SKU": st(align="center", **base),
        "Producto": st(indent=1, **base),
        "Unidad": st(align="center", **base),
        "StockActual": st(align="right", num_format="#,##0", indent=1, **base),
        "StockMin": st(align="right", num_format="#,##0", indent=1, font_color=C["muted"], **base),
        "StockMax": st(align="right", num_format="#,##0", indent=1, font_color=C["muted"], **base),
        "Estado": st(bold=True, indent=1, font_size=9, formula=True),
        "sug": st(align="right", num_format="#,##0", indent=1, bold=True, font_color=C["brand_dk"], font_size=10.5,
                  formula=True),
        "CostoUnitario": st(align="right", num_format="$ #,##0", indent=1, **base),
        "sub": st(align="right", num_format="$ #,##0", indent=1, bold=True, **base),
    }
    col = {key: chr(66 + i) for i, (_, _, key) in enumerate(SPEC)}
    for k in range(MAX_PROD):
        r = PD_FIRST + k
        idx = f"{kq}!${rk['fila']}{8 + k}"
        for i, (label, w, key) in enumerate(SPEC):
            if key == "k":
                f = f'=IF({idx}="","",{k + 1})'
            elif key == "prov":
                f = (f'=IF($B{r}="","",IF(INDEX(tblStock[Proveedor],{idx})="","(Sin proveedor)",'
                     f'INDEX(tblStock[Proveedor],{idx})))')
            elif key == "sug":
                refs = (f"${col['StockActual']}{r}", f"${col['StockMin']}{r}", f"${col['StockMax']}{r}")
                f = f'=IF($B{r}="","",{sugerido_formula(*refs)})'
            elif key == "sub":
                f = f'=IF($B{r}="","",${col["sug"]}{r}*${col["CostoUnitario"]}{r})'
            else:
                f = f'=IF($B{r}="","",INDEX(tblStock[{key}],{idx}))'
            ws.write_formula(r - 1, 1 + i, f, fm[key])
    first, last = PD_FIRST, PD_LAST
    estado_cf(ws, st, f"{col['Estado']}{first}:{col['Estado']}{last}", f"${col['Estado']}{first}")
    ws.conditional_format(f"B{first}:M{last}", {"type": "formula",
                                                "criteria": f'=AND($B{first}<>"",$C{first}<>$C{first - 1})',
                                                "format": st.cf(top=2, top_color=C["brand"])})
    ws.conditional_format(f"C{first}:C{last}", {"type": "formula",
                                                "criteria": f'=AND($B{first}<>"",$C{first}<>$C{first - 1})',
                                                "format": st.cf(bold=True, font_color=C["brand_dk"])})
    for key in ("StockActual", "StockMin", "StockMax", "sug"):
        decimals_cf(ws, st, f"{col[key]}{first}:{col[key]}{last}", f"{col[key]}{first}", "#,##0.00")
    row_cf(ws, st, f"B{first}:M{last}", f"$B{first}")
    ws.autofilter(PD_HDR - 1, 1, PD_LAST - 1, len(SPEC))
    ws.freeze_panes(PD_HDR, 0)
    ws.set_selection("D10")
    # impresión: encabezado del documento + líneas; se repite el encabezado de columnas
    ws.set_landscape()
    ws.set_paper(1)
    ws.fit_to_pages(1, 0)
    ws.set_margins(left=0.4, right=0.4, top=0.6, bottom=0.6)
    ws.repeat_rows(PD_HDR - 1)
    ws.set_footer('&L&"Segoe UI,Regular"&8M-INV · Pedido sugerido&R&"Segoe UI,Regular"&8Página &P de &N')
