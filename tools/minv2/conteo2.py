"""
M-INV V2.1 · 13_CONTEO: toma física colaborativa.

La fila n refleja el producto n del catálogo. Varias personas cuentan a la vez repartiéndose zonas (Ubicación o
Categoría, cada una en su Vista de hoja) y escriben solo en la columna Conteo. El Office Script
GenerarAjustesConteo.ts (BODEGA/ADMIN, con «SI» en Confirmar) calcula la diferencia contra el stock EXACTO de ese
momento y registra en 10A un AJUSTE (+/-) por producto, o el SALDO INICIAL si el producto aún no tiene movimientos.
"""
from __future__ import annotations

from minv.base import C, MAX_PROD, Col, Ctx, nested_if, q, this_row

from .base2 import S_CONTEO, barra, celda, chip, con_margen, franja, lienzo, marcador_script, msg, tabla

HDR_K, FIRST_K = 8, 9
LAST_K = FIRST_K + MAX_PROD - 1
CONTEO_COLS = ["SKU", "Producto", "Categoría", "Ubicación", "Unidad", "Activo", "Sistema", "Conteo", "Diferencia",
               "ValorDiferencia", "Resultado", "AjusteSugerido"]
CONTEO_W = [84, 270, 160, 112, 84, 78, 120, 104, 110, 146, 150, 230]


def conteo_cols() -> list[Col]:
    t = "tblConteo"
    r = this_row(t)
    k = f"ROW()-ROW({t}[#Headers])"
    cat = lambda c: f"INDEX(tblProductos[{c}],{k})"  # noqa: E731
    vacio = f'{r("SKU")}=""'
    en_stock = lambda c: f'INDEX(tblStock[{c}],MATCH({r("SKU")},tblStock[SKU],0))'  # noqa: E731
    dec = f'IFERROR(INDEX(lstUniDec,MATCH({r("Unidad")},lstUniCod,0)),"SI")'
    resultado = nested_if([
        (f'OR({vacio},{r("Conteo")}="")', '""'),
        (f'OR(NOT(ISNUMBER({r("Conteo")})),N({r("Conteo")})<0)', '"✖ Conteo no válido"'),
        (f'AND({r("Conteo")}<>INT({r("Conteo")}),{dec}="NO")', f'"✖ "&{r("Unidad")}&" no admite decimales"'),
        (f'IFERROR({en_stock("UltimoMov")}="",TRUE)', '"● Saldo inicial"'),
        (f'{r("Diferencia")}=0', '"✔ Cuadra"'),
        (f'{r("Diferencia")}>0', '"▲ Sobrante"'),
    ], '"▼ Faltante"')
    ajuste = nested_if([
        (f'OR({r("Conteo")}="",LEFT({r("Resultado")},1)="✖")', '""'),
        (f'LEFT({r("Resultado")},1)="●"',
         f'IF(N({r("Conteo")})>0,"SALDO INICIAL "&{r("Conteo")}&" "&{r("Unidad")},"Sin movimiento (conteo en 0)")'),
        (f'{r("Diferencia")}=0', '""'),
    ], f'IF({r("Diferencia")}>0,"AJUSTE (+) ","AJUSTE (-) ")&ABS({r("Diferencia")})&" "&{r("Unidad")}')
    spec = {
        "SKU": Col("SKU", "calc", 0, "center", "Producto n del catálogo (05_PRODUCTOS).",
                   formula=f'=IF({cat("SKU")}="","",{cat("SKU")})'),
        "Producto": Col("Producto", "calc", 0, "text", "", formula=f'=IF({vacio},"",{cat("Producto")})'),
        "Categoría": Col("Categoría", "calc", 0, "text", "Filtre por categoría para repartir el conteo.",
                         formula=f'=IF({vacio},"",{cat("Categoría")}&"")'),
        "Ubicación": Col("Ubicación", "calc", 0, "center", "Filtre por ubicación para recorrer la bodega en orden.",
                         formula=f'=IF({vacio},"",{cat("Ubicación")}&"")'),
        "Unidad": Col("Unidad", "calc", 0, "center", "", formula=f'=IF({vacio},"",{cat("Unidad")}&"")'),
        "Activo": Col("Activo", "calc", 0, "center", "", formula=f'=IF({vacio},"",IF({cat("Activo")}="NO","NO","SI"))'),
        "Sistema": Col("Sistema", "calc", 0, "strong",
                       "Stock según la instantánea (15_STOCK). El ajuste final usa el stock EXACTO al generar.",
                       formula=f'=IF({vacio},"",IFERROR(N({en_stock("StockActual")}),0))'),
        "Conteo": Col("Conteo", "in", 0, "qty", "Cantidad contada físicamente. Deje vacío lo que no contó: solo se "
                                                "ajustan los productos con conteo."),
        "Diferencia": Col("Diferencia", "calc", 0, "signed", "Conteo − sistema (vista previa con la instantánea).",
                          formula=f'=IF(OR({vacio},{r("Conteo")}=""),"",IF(ISNUMBER({r("Conteo")}),'
                                  f'{r("Conteo")}-N({r("Sistema")}),""))'),
        "ValorDiferencia": Col("ValorDiferencia", "calc", 0, "money_signed", "Diferencia × costo unitario.",
                               formula=f'=IF({r("Diferencia")}="","",{r("Diferencia")}*N({cat("CostoUnitario")}))'),
        "Resultado": Col("Resultado", "calc", 0, "status", "✔ cuadra · ▲ sobrante · ▼ faltante · ● saldo inicial.",
                         formula="=" + resultado),
        "AjusteSugerido": Col("AjusteSugerido", "calc", 0, "text", "Movimiento que registrará el script.",
                              formula="=" + ajuste),
    }
    cols = [spec[x] for x in CONTEO_COLS]
    for c, w in zip(cols, CONTEO_W):
        c.width = w
    return cols


def build_conteo2(ctx: Ctx, conteos: dict[int, float]):
    """conteos: {n.º de fila: cantidad contada} (ejemplo de conteo en curso en la demo)."""
    ws, st, wb = ctx.sheets[S_CONTEO], ctx.st, ctx.wb
    cols = conteo_cols()
    widths = con_margen([16] + CONTEO_W + [16])
    lienzo(ws, widths, zoom=90)
    franja(ctx, ws, widths, "Toma física · conteo colaborativo",
           "Repartan zonas (Ubicación o Categoría) · Cada persona escribe solo el Conteo de su zona · "
           "El script registra los ajustes con el stock exacto del momento", "conteo")
    barra(ctx, ws)
    canvas = st(bg_color=C["canvas"])
    ws.set_row_pixels(5, 34, canvas)
    ws.set_row_pixels(6, 30, canvas)
    chip(ctx, ws, 5, 1, 2, '="Contados: "&kpiConteoContados&" de "&kpiActivos')
    chip(ctx, ws, 5, 3, 4, '="Con diferencia: "&kpiConteoDif')
    chip(ctx, ws, 5, 5, 7, '="Valor neto de diferencias: $ "&FIXED(kpiConteoValor,0)')
    marcador_script(ctx, ws, 5, 9, 12, "Generar ajustes del conteo", "GenerarAjustesConteo")
    # Fila 7: fecha (D7), confirmación (H7) y último resultado del script (I7:M7)
    lbl = st(bold=True, font_size=9.5, align="right", valign="vcenter", bg_color=C["canvas"])
    celda(ws, st, 6, 1, 2, "Fecha del conteo (vacía = hoy):", lbl)
    ws.write_blank(6, 3, None, st.cell("in", "date"))
    celda(ws, st, 6, 4, 6, "Confirmar (escriba SI):", lbl)
    ws.write_blank(6, 7, None, st.cell("in", "center"))
    res = st(font_size=9, bold=True, indent=1, valign="vcenter", bg_color=C["white"], border=1,
             border_color=C["border"], text_wrap=True)
    celda(ws, st, 6, 8, 12, "", res)
    wb.define_name("ctFecha", f"={q(S_CONTEO)}!$D$7")
    wb.define_name("ctConfirmar", f"={q(S_CONTEO)}!$H$7")
    wb.define_name("ctResultado", f"={q(S_CONTEO)}!$I$7")
    ws.data_validation(6, 3, 6, 3, {"validate": "date", "criteria": "between", "minimum": "=cfgFechaMin",
                                    "maximum": "=TODAY()", **msg("Fecha del conteo", "Opcional. Vacía = hoy."),
                                    "error_title": "Fecha no válida", "error_message": "Use dd/mm/aaaa, sin fechas futuras."})
    ws.data_validation(6, 7, 6, 7, {"validate": "list", "source": ["SI"],
                                    **msg("Confirmar", "Escriba SI justo antes de pulsar «Generar ajustes». El script lo "
                                                       "borra al terminar (evita ejecutarlo por accidente)."),
                                    "error_title": "Valor no válido", "error_message": "Escriba SI o deje vacío."})
    for crit, fg, bg in (('=LEFT($I$7,1)="✔"', C["green"], C["green_lt"]), ('=LEFT($I$7,1)="✖"', C["red"], C["red_lt"])):
        ws.conditional_format("I7:M7", {"type": "formula", "criteria": crit, "format": st.cf(font_color=fg, bg_color=bg)})

    tabla(ctx, ws, "tblConteo", cols, HDR_K, MAX_PROD, badges=False)
    fin = st.cell("in", "qty")
    for k in range(MAX_PROD):
        v = conteos.get(k + 1)
        r0 = FIRST_K - 1 + k
        if isinstance(v, (int, float)):
            ws.write_number(r0, 1 + CONTEO_COLS.index("Conteo"), v, fin)
        else:
            ws.write_blank(r0, 1 + CONTEO_COLS.index("Conteo"), None, fin)
    L = {x: chr(66 + i) for i, x in enumerate(CONTEO_COLS)}
    rng = lambda x: f"{L[x]}{FIRST_K}:{L[x]}{LAST_K}"  # noqa: E731
    ws.data_validation(rng("Conteo"), {"validate": "decimal", "criteria": ">=", "value": 0,
                                       **msg("Conteo físico", "Cantidad contada (0 o más). Deje vacío lo que no contó."),
                                       "error_title": "Conteo no válido", "error_message": "Escriba un número mayor o "
                                                                                          "igual a 0."})
    pwd = ctx.layout.get("pwd_bodega")
    ws.unprotect_range(rng("Conteo"), "Conteo", pwd)
    ws.unprotect_range("D7", "Fecha del conteo", pwd)
    ws.unprotect_range("H7", "Confirmar conteo", pwd)
    res_ref = f"${L['Resultado']}{FIRST_K}"
    for sym, fg, bg in (("✔", C["green"], None), ("✖", C["red"], C["red_lt"]), ("▲", C["brand"], None),
                        ("▼", C["amber"], C["amber_lt"]), ("●", C["purple"], None)):
        ws.conditional_format(rng("Resultado"), {"type": "formula", "criteria": f'=LEFT({res_ref},1)="{sym}"',
                                                 "format": st.cf(font_color=fg, bold=True,
                                                                 **({"bg_color": bg} if bg else {}))})
    dif = f"${L['Diferencia']}{FIRST_K}"
    ws.conditional_format(rng("Diferencia"), {"type": "formula", "criteria": f"=AND(ISNUMBER({dif}),{dif}>0)",
                                              "format": st.cf(font_color=C["brand"], bold=True)})
    ws.conditional_format(rng("Diferencia"), {"type": "formula", "criteria": f"=AND(ISNUMBER({dif}),{dif}<0)",
                                              "format": st.cf(font_color=C["red"], bold=True)})
    ws.conditional_format(f"B{FIRST_K}:{L['AjusteSugerido']}{LAST_K}", {
        "type": "formula", "criteria": f'=${L["Activo"]}{FIRST_K}="NO"', "format": st.cf(font_color=C["faint"],
                                                                                       italic=True)})
    for x in ("Sistema", "Conteo", "Diferencia"):
        ref = f"{L[x]}{FIRST_K}"
        ws.conditional_format(rng(x), {"type": "formula", "criteria": f"=AND(ISNUMBER({ref}),{ref}<>INT({ref}))",
                                       "format": st.cf(num_format="#,##0.00")})
    ws.freeze_panes(HDR_K, 3)
    ws.set_selection(f"{L['Conteo']}{FIRST_K}")
    ws.set_landscape()
    ws.fit_to_pages(1, 0)
    ws.repeat_rows(HDR_K - 1)
