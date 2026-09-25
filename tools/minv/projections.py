"""M-INV · Capa de lectura: 15_STOCK (proyección) y 16_ALERTAS (proyección priorizada)."""
from __future__ import annotations

from xlsxwriter.utility import xl_col_to_name

from .base import (C, EST, ESTADOS, FIRST, HDR, LAST_PROD, MAX_PROD, S_ALERT, S_KPI, S_PROD, S_STOCK, Col, Ctx,
                   app_sheet, band, build_table, chip, decimals_cf, estado_cf, estado_formula, header_tip, note,
                   print_setup, q, row_cf, sugerido_formula, textbox, this_row, toolbar_row)


# ---------------------------------------------------------------------------
# 15_STOCK
# ---------------------------------------------------------------------------
def stock_cols(ctx: Ctx) -> list[Col]:
    r = this_row("tblStock")
    sp = q(S_PROD)
    P = {c.name: ctx.col("tblProductos", c.name) for c in ctx.tables["tblProductos"]}
    src = lambda n: f"{sp}!${P[n]}{{r}}"  # noqa: E731
    sku = "$B{r}"
    return [
        Col("SKU", "mirror", 84, "center", "Espejo de 05_PRODUCTOS (misma fila).",
            mirror=f'=IF({src("SKU")}="","",{src("SKU")})'),
        Col("Producto", "mirror", 270, "text", "", mirror=f'=IF({sku}="","",{src("Producto")})'),
        Col("Categoría", "mirror", 168, "text", "", mirror=f'=IF({sku}="","",{src("Categoría")})'),
        Col("Proveedor", "mirror", 200, "text", "", mirror=f'=IF({sku}="","",{src("Proveedor")}&"")'),
        Col("Unidad", "mirror", 78, "center", "", mirror=f'=IF({sku}="","",{src("Unidad")})'),
        Col("Activo", "mirror", 74, "center", "", mirror=f'=IF({sku}="","",IF({src("Activo")}="NO","NO","SI"))'),
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
        Col("StockMin", "mirror", 94, "qty", "", mirror=f'=IF({sku}="","",N({src("StockMin")}))'),
        Col("StockMax", "mirror", 94, "qty", "", mirror=f'=IF({sku}="","",N({src("StockMax")}))'),
        Col("Nivel", "calc", 116, "pct", "Stock actual ÷ stock máximo (barra de nivel).",
            formula=f'=IF(OR({r("SKU")}="",N({r("StockMax")})=0),"",MAX(0,{r("StockActual")})/{r("StockMax")})'),
        Col("Estado", "calc", 138, "estado", "Semáforo según reglas de tblEstados (01_CONFIG) y el margen de alerta.",
            formula=f'=IF({r("SKU")}="","",'
                    f'{estado_formula(r("StockActual"), r("StockMin"), r("StockMax"), r("Activo"))})'),
        Col("CostoUnitario", "mirror", 120, "money", "", mirror=f'=IF({sku}="","",N({src("CostoUnitario")}))'),
        Col("ValorInventario", "calc", 136, "money", "Stock actual (no negativo) × costo unitario.",
            formula=f'=IF({r("SKU")}="","",MAX(0,{r("StockActual")})*{r("CostoUnitario")})'),
        Col("UltimoMov", "calc", 112, "date", "Fecha del último movimiento del SKU.",
            formula=f'=IF({r("SKU")}="","",IFERROR(_xlfn.AGGREGATE(14,6,tblMovimientos[Fecha]/'
                    f'(tblMovimientos[SKU]={r("SKU")}),1),""))'),
        Col("DiasSinMov", "calc", 114, "days", "Días desde el último movimiento. En ámbar si supera el plazo de "
                                               "rotación (cfgDiasSinRotacion) y aún hay stock.",
            formula=f'=IF(OR({r("SKU")}="",{r("UltimoMov")}=""),"",TODAY()-{r("UltimoMov")})'),
    ]


def build_stock(ctx: Ctx):
    ws, st = ctx.sheets[S_STOCK], ctx.st
    cols = ctx.tables["tblStock"]
    widths = [16] + [c.width for c in cols] + [16]
    app_sheet(ws, widths, zoom=90)
    band(ctx, ws, "Stock actual",
         "Capa de lectura · Proyección calculada con SUMAR.SI.CONJUNTO sobre la bitácora (no editable)",
         "stock", widths=widths)
    toolbar_row(ws, st)
    build_table(ctx, ws, "tblStock", cols, MAX_PROD, view=True)
    L = {c.name: ctx.col("tblStock", c.name) for c in cols}
    rng = lambda n: f"{L[n]}{FIRST}:{L[n]}{LAST_PROD}"  # noqa: E731
    estado_cf(ws, st, rng("Estado"), f"${L['Estado']}{FIRST}")
    for name, color in (("AGOTADO", C["red"]), ("CRÍTICO", C["red"]), ("INCONSISTENTE", "#6A1B9A"),
                        ("BAJO", C["amber"]), ("ÓPTIMO", C["green"]), ("SOBRESTOCK", C["brand"])):
        ws.conditional_format(rng("StockActual"), {"type": "formula", "criteria": f'=${L["Estado"]}{FIRST}="{name}"',
                                                   "format": st.cf(font_color=color)})
    ws.conditional_format(rng("Nivel"), {"type": "data_bar", "bar_solid": True, "bar_color": "#90CAF9",
                                         "bar_border_color": "#64B5F6", "min_type": "num", "min_value": 0,
                                         "max_type": "num", "max_value": 1, "data_bar_2010": True})
    d, s = f"${L['DiasSinMov']}{FIRST}", f"${L['StockActual']}{FIRST}"
    ws.conditional_format(rng("DiasSinMov"), {
        "type": "formula", "criteria": f'=AND(ISNUMBER({d}),{d}>cfgDiasSinRotacion,N({s})>0)',
        "format": st.cf(font_color=C["amber"], bg_color=C["amber_lt"], bold=True)})
    for n in ("Entradas", "Salidas", "StockActual", "StockMin", "StockMax"):
        decimals_cf(ws, st, rng(n), f"{L[n]}{FIRST}", "#,##0.00")
    row_cf(ws, st, f"B{FIRST}:{L['DiasSinMov']}{LAST_PROD}", f"$B{FIRST}")

    chip(ws, 16, 200, textlink=ctx.kref("txtProductos"), desc="Contador de productos")
    chip(ws, 226, 290, textlink=ctx.kref("txtValor"), desc="Valor del inventario", color=C["brand_dk"])
    x = 532
    for e in ESTADOS:
        w = 36 + int(7.5 * len(e[0]))
        textbox(ws, e[0], x, 7, w, 20, tag="chip", desc=f"Leyenda semáforo: {e[0]}", fill=e[4],
                font={"size": 8, "bold": True, "color": e[3]}, tip=e[6], anchor=(4, 0))
        x += w + 6
    if ctx.plus:
        note(ws, "Doble clic en un producto: abre su consulta (kardex).", x + 10, 330)
    ws.freeze_panes(HDR, 0)
    ws.set_selection(FIRST - 1, 1, FIRST - 1, 1)
    print_setup(ws, "Stock actual")


# ---------------------------------------------------------------------------
# 16_ALERTAS
# ---------------------------------------------------------------------------
ALERT_SPEC = [  # (encabezado, ancho, formato, columna de tblStock o fórmula especial)
    ("#", 42, "id", None), ("Nivel", 138, "estado", "Estado"), ("SKU", 84, "center", "SKU"),
    ("Producto", 270, "text", "Producto"), ("Categoría", 168, "text", "Categoría"),
    ("Proveedor", 200, "text", "Proveedor"),
    ("Stock", 86, "strong", "StockActual"), ("Mínimo", 92, "qty", "StockMin"), ("Máximo", 92, "qty", "StockMax"),
    ("Unidad", 76, "center", "Unidad"), ("Faltante", 88, "qty", "*faltante"),
    ("SugeridoPedir", 132, "qty", "*sugerido"), ("AcciónSugerida", 250, "text", "*accion"),
    ("UltimoMov", 112, "date", "UltimoMov"),
]


def build_alertas(ctx: Ctx):
    ws, st = ctx.sheets[S_ALERT], ctx.st
    spec = ALERT_SPEC
    widths = [16] + [s[1] for s in spec] + [16]
    app_sheet(ws, widths, zoom=100)
    band(ctx, ws, "Alertas de inventario",
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
    L = {name: xl_col_to_name(1 + i) for i, (name, *_rest) in enumerate(spec)}
    for i, (name, w, fmt, _) in enumerate(spec):
        ws.write_string(hdr0, 1 + i, name, st.hdr("calc"))
        ws.write_string(hdr0 - 1, 1 + i, "ƒx", st.badge("calc"))
        if name in tips:
            header_tip(ws, hdr0, 1 + i, f"ƒx {name}", tips[name])
    kq = q(S_KPI)
    rk = ctx.layout["rank_alertas"]  # columnas del ranking de alertas en 91_KPIS
    for k in range(MAX_PROD):
        r = FIRST + k
        idx = f"{kq}!${rk['fila']}{r}"
        for i, (name, w, fmt, src) in enumerate(spec):
            f = st.cell("calc", fmt, view=True)
            if name == "#":
                formula = f'=IF({idx}="","",{kq}!${rk["k"]}{r})'
            elif src == "*faltante":
                formula = f'=IF($B{r}="","",MAX(0,${L["Mínimo"]}{r}-${L["Stock"]}{r}))'
            elif src == "*sugerido":
                stock_ref, min_ref, max_ref = f"${L['Stock']}{r}", f"${L['Mínimo']}{r}", f"${L['Máximo']}{r}"
                formula = (f'=IF($B{r}="","",IF(OR(${L["Nivel"]}{r}="SOBRESTOCK",${L["Nivel"]}{r}="INCONSISTENTE"),0,'
                           f'{sugerido_formula(stock_ref, min_ref, max_ref)}))')
            elif src == "*accion":
                formula = (f'=IF($B{r}="","",IFERROR(INDEX(tblEstados[Acción],MATCH(${L["Nivel"]}{r},'
                           f'tblEstados[Estado],0)),""))')
            else:
                formula = f'=IF($B{r}="","",INDEX(tblStock[{src}],{idx})&"")' if src == "Proveedor" else \
                    f'=IF($B{r}="","",INDEX(tblStock[{src}],{idx}))'
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

    chip(ws, 16, 200, textlink=ctx.kref("txtEnAlerta"), desc="Productos en alerta")
    x = 226
    for name, key in (("AGOTADO", "txtChipAgot"), ("CRÍTICO", "txtChipCrit"), ("BAJO", "txtChipBajo"),
                      ("SOBRESTOCK", "txtChipSobre"), ("INCONSISTENTE", "txtChipIncons")):
        e = EST[name]
        chip(ws, x, 158, textlink=ctx.kref(key), fill=e[4], color=e[3], size=8.5, desc=f"Contador {name}", tip=e[6])
        x += 164
    if ctx.plus:
        note(ws, "Doble clic en una alerta: abre el formulario de reposición ya diligenciado.", x + 10, 420)
    ws.freeze_panes(HDR, 0)
    ws.set_selection(FIRST - 1, 1, FIRST - 1, 1)
    print_setup(ws, "Alertas de inventario")
