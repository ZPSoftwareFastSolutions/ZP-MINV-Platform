"""
M-INV · 13_CONTEO: toma física de inventario (conteo vs. sistema).

El conteo es un COMANDO: sus diferencias se convierten en movimientos AJUSTE (+)/(-) de la bitácora.
- Edición Plus: el botón «Generar ajustes» (VBA, modConteo.bas) los registra, los sella y limpia el conteo.
- Edición Estándar: la columna AjusteSugerido indica qué registrar a mano en la bitácora.
Las filas están alineadas con 05_PRODUCTOS y 15_STOCK (regla R-06).
"""
from __future__ import annotations

from xlsxwriter.utility import xl_col_to_name

from .base import (C, FIRST, HDR, LAST_PROD, MAX_PROD, S_CONTEO, S_PROD, S_STOCK, Col, Ctx, action_button, app_sheet,
                   band, build_table, chip, decimals_cf, note, print_setup, q, this_row, toolbar_row)

NAMES = ["SKU", "Producto", "Categoría", "Ubicación", "Unidad", "Activo", "StockSistema", "Conteo", "Diferencia",
         "ValorDiferencia", "Resultado", "AjusteSugerido"]
LT = {n: xl_col_to_name(1 + i) for i, n in enumerate(NAMES)}


def conteo_cols(ctx: Ctx) -> list[Col]:
    r = this_row("tblConteo")
    sp, ss = q(S_PROD), q(S_STOCK)
    P = {n: ctx.col("tblProductos", n) for n in ("SKU", "Producto", "Categoría", "Ubicación", "Unidad", "Activo")}
    stock_act, costo = ctx.col("tblStock", "StockActual"), ctx.col("tblStock", "CostoUnitario")
    src = lambda n: f"{sp}!${P[n]}{{r}}"  # noqa: E731
    sku = "$B{r}"
    dif = f"${LT['Diferencia']}{{r}}"
    cols = [
        Col("SKU", "mirror", 84, "center", "Espejo de 05_PRODUCTOS (misma fila).",
            mirror=f'=IF({src("SKU")}="","",{src("SKU")})'),
        Col("Producto", "mirror", 270, "text", "", mirror=f'=IF({sku}="","",{src("Producto")})'),
        Col("Categoría", "mirror", 160, "text", "", mirror=f'=IF({sku}="","",{src("Categoría")})'),
        Col("Ubicación", "mirror", 96, "center", "Ubicación en bodega: filtre por ella para recorrer la bodega en orden.",
            mirror=f'=IF({sku}="","",{src("Ubicación")}&"")'),
        Col("Unidad", "mirror", 74, "center", "", mirror=f'=IF({sku}="","",{src("Unidad")})'),
        Col("Activo", "mirror", 64, "center", "", mirror=f'=IF({sku}="","",IF({src("Activo")}="NO","NO","SI"))'),
        Col("StockSistema", "mirror", 122, "strong", "Stock actual según la bitácora (15_STOCK). Para un conteo "
                                                     "ciego, oculte esta columna antes de imprimir.",
            mirror=f'=IF({sku}="","",N({ss}!${stock_act}{{r}}))'),
        Col("Conteo", "in", 104, "qty", "Cantidad contada físicamente. Deje vacío lo que no contó: solo se ajustan "
                                       "los productos con conteo."),
        Col("Diferencia", "calc", 104, "signed", "Conteo − stock del sistema. Positiva = sobrante; negativa = "
                                                 "faltante.",
            formula=f'=IF(OR({r("SKU")}="",{r("Conteo")}=""),"",{r("Conteo")}-{r("StockSistema")})'),
        Col("ValorDiferencia", "mirror", 142, "money_signed", "Diferencia × costo unitario del catálogo.",
            mirror=f'=IF({dif}="","",{dif}*N({ss}!${costo}{{r}}))'),
        Col("Resultado", "calc", 150, "status", "✔ cuadra · ▲ sobrante · ▼ faltante.",
            formula=f'=IF({r("Diferencia")}="","",IF({r("Diferencia")}=0,"✔ Cuadra",'
                    f'IF({r("Diferencia")}>0,"▲ Sobrante","▼ Faltante")))'),
        Col("AjusteSugerido", "calc", 190, "text", "Movimiento que corrige la diferencia en la bitácora.",
            formula=f'=IF(OR({r("Diferencia")}="",{r("Diferencia")}=0),"",IF({r("Diferencia")}>0,"AJUSTE (+) ",'
                    f'"AJUSTE (-) ")&ABS({r("Diferencia")})&" "&{r("Unidad")})'),
    ]
    assert [c.name for c in cols] == NAMES
    return cols


def build_conteo(ctx: Ctx):
    ws, st, wb = ctx.sheets[S_CONTEO], ctx.st, ctx.wb
    cols = ctx.tables["tblConteo"]
    widths = [16] + [c.width for c in cols] + [16]
    app_sheet(ws, widths, zoom=90)
    band(ctx, ws, "Toma física de inventario",
         "Comando · Compare el conteo con el sistema y convierta las diferencias en ajustes de la bitácora",
         None, widths=widths)
    toolbar_row(ws, st)
    build_table(ctx, ws, "tblConteo", cols, MAX_PROD)
    rng = lambda n: f"{LT[n]}{FIRST}:{LT[n]}{LAST_PROD}"  # noqa: E731

    c, u = f"{LT['Conteo']}{FIRST}", f"${LT['Unidad']}{FIRST}"
    ws.data_validation(rng("Conteo"), {
        "validate": "custom",
        "value": f'=AND(ISNUMBER({c}),{c}>=0,IFERROR(OR(INDEX(lstUniDec,MATCH({u},lstUniCod,0))="SI",{c}=INT({c})),'
                 f'TRUE))',
        "input_title": "Conteo físico",
        "input_message": "Cantidad encontrada en bodega (0 o más). Deje vacío si no contó este producto.",
        "error_title": "Conteo no válido",
        "error_message": "Escriba un número mayor o igual a 0. Esta unidad no admite decimales."})

    ws.conditional_format(rng("Conteo"), {"type": "formula", "criteria": f'=${LT["Conteo"]}{FIRST}<>""',
                                          "format": st.cf(bg_color=C["brand_lt"], bold=True)})
    res = f"${LT['Resultado']}{FIRST}"
    ws.conditional_format(rng("Resultado"), {"type": "formula", "criteria": f'=LEFT({res},1)="✔"',
                                             "format": st.cf(font_color=C["green"])})
    ws.conditional_format(rng("Resultado"), {"type": "formula", "criteria": f'=LEFT({res},1)="▲"',
                                             "format": st.cf(font_color=C["brand"], bg_color=C["blue_lt"])})
    ws.conditional_format(rng("Resultado"), {"type": "formula", "criteria": f'=LEFT({res},1)="▼"',
                                             "format": st.cf(font_color=C["red"], bg_color=C["red_lt"])})
    for n in ("Diferencia", "ValorDiferencia", "AjusteSugerido"):
        ref = f"${LT['Diferencia']}{FIRST}"
        ws.conditional_format(rng(n), {"type": "formula", "criteria": f"=AND(ISNUMBER({ref}),{ref}>0)",
                                       "format": st.cf(font_color=C["brand"])})
        ws.conditional_format(rng(n), {"type": "formula", "criteria": f"=AND(ISNUMBER({ref}),{ref}<0)",
                                       "format": st.cf(font_color=C["red"])})
    ws.conditional_format(f"B{FIRST}:{LT['AjusteSugerido']}{LAST_PROD}", {
        "type": "formula", "criteria": f'=${LT["Activo"]}{FIRST}="NO"',
        "format": st.cf(font_color=C["faint"], italic=True)})
    for n in ("StockSistema", "Conteo", "Diferencia"):
        decimals_cf(ws, st, rng(n), f"{LT[n]}{FIRST}", "#,##0.00")

    # Barra de herramientas + datos del conteo (fecha y responsable)
    chip(ws, 16, 170, textlink=ctx.kref("txtConteoContados"), desc="Productos contados")
    chip(ws, 194, 170, textlink=ctx.kref("txtConteoDif"), desc="Productos con diferencia")
    chip(ws, 372, 210, textlink=ctx.kref("txtConteoValor"), desc="Valor neto de las diferencias",
         color=C["brand_dk"])
    if ctx.plus:
        action_button(ws, "✔  Generar ajustes", 594, 200, action="GenerarAjustesConteo", fill=C["green"],
                      tip="Registrar en la bitácora un AJUSTE por cada diferencia y limpiar el conteo",
                      desc="Acción: generar ajustes del conteo")
    else:
        note(ws, "Registre en la Bitácora el AjusteSugerido de cada fila y luego borre los conteos.", 594, 370)
    lbl = st(bold=True, font_size=9, bg_color=C["canvas"], align="right", indent=1)
    ws.write_string(4, 9, "Fecha:", lbl)
    ws.write_blank(4, 10, None, st.field(font_size=10, num_format="dd/mm/yyyy", align="center"))
    ws.write_string(4, 11, "Responsable:", lbl)
    ws.write_blank(4, 12, None, st.field(font_size=10))
    wb.define_name("ctFecha", f"={q(S_CONTEO)}!$K$5")
    wb.define_name("ctResponsable", f"={q(S_CONTEO)}!$M$5")
    ws.data_validation(4, 10, 4, 10, {"validate": "date", "criteria": "between", "minimum": "=cfgFechaMin",
                                      "maximum": "=TODAY()", "input_title": "Fecha del conteo",
                                      "input_message": "Vacía = hoy. Es la fecha que llevarán los ajustes.",
                                      "error_title": "Fecha no válida",
                                      "error_message": "No se permiten fechas futuras ni anteriores a la mínima."})
    ws.data_validation(4, 12, 4, 12, {"validate": "list", "source": "=lstResponsables",
                                      "input_title": "Responsable del conteo",
                                      "input_message": "Quién realizó el conteo (se registra en los ajustes).",
                                      "error_title": "Responsable no válido",
                                      "error_message": "Seleccione un nombre de la lista."})
    ws.freeze_panes(HDR, 0)
    ws.set_selection(FIRST - 1, 8, FIRST - 1, 8)
    print_setup(ws, "Toma física de inventario")
