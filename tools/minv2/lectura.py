"""
M-INV V2 · Capa de lectura a demanda (lazy): 15_STOCK y 16_ALERTAS son instantáneas de VALORES.

Las reconstruye el Office Script RecalcularStock.ts leyendo las dos bitácoras oficiales. `proyectar()` implementa
aquí el mismo algoritmo (misma aritmética IEEE que JavaScript) para precargar la demo y para contrastar el script en
las pruebas automáticas: si una regla cambia, debe cambiar en ambos lados.
"""
from __future__ import annotations

import datetime as dt
import math

from minv.base import C, EST, FIRST, HDR, LAST_PROD, MAX_PROD, Col, Ctx, estado_cf

from .base2 import barra, chip, franja, lienzo, marcador_script, tabla

STOCK_COLS = ["SKU", "Producto", "Categoría", "Proveedor", "Unidad", "Activo", "Entradas", "Salidas", "StockActual",
              "StockMin", "StockMax", "Nivel", "Estado", "CostoUnitario", "ValorInventario", "UltimoMov", "DiasSinMov",
              "Salidas30d", "CoberturaDias", "RankSalidas30d"]
STOCK_W = [84, 270, 160, 200, 84, 78, 96, 96, 118, 104, 104, 104, 130, 130, 146, 118, 118, 116, 134, 146]
STOCK_FMT = ["center", "text", "text", "text", "center", "center", "qty", "qty", "strong", "qty", "qty", "pct",
             "estado", "money", "money", "date", "days", "qty", "days", "center"]
ALERT_COLS = ["#", "Estado", "SKU", "Producto", "Categoría", "Proveedor", "Stock", "Mínimo", "Máximo", "Unidad",
              "Faltante", "SugeridoPedir", "AcciónSugerida", "UltimoMov"]
ALERT_W = [48, 130, 84, 270, 160, 200, 90, 96, 96, 84, 96, 136, 250, 118]
ALERT_FMT = ["id", "estado", "center", "text", "text", "text", "strong", "qty", "qty", "center", "qty", "qty", "text",
             "date"]
PEDIDO_COLS = ["Proveedor", "SKU", "Producto", "Unidad", "Estado", "Stock", "Mínimo", "Máximo", "APedir",
               "CostoUnitario", "Subtotal", "DiasEntrega", "EntregaEstimada", "Contacto", "Teléfono", "Correo"]
PEDIDO_W = [220, 84, 270, 84, 124, 84, 96, 96, 100, 130, 124, 120, 146, 170, 120, 240]
PEDIDO_FMT = ["text", "center", "text", "center", "estado", "strong", "qty", "qty", "strong", "money", "money", "days",
              "date", "text", "center", "text"]
ALERTAS = ("INCONSISTENTE", "AGOTADO", "CRÍTICO", "BAJO", "SOBRESTOCK")
REPONER = ("AGOTADO", "CRÍTICO", "BAJO")
SIN_PROVEEDOR = "(Sin proveedor)"
VENTANA_DIAS = 30
EPOCH = dt.date(1899, 12, 30)


def serial(d: dt.date | dt.datetime) -> float:
    if isinstance(d, dt.datetime):
        return (d - dt.datetime(1899, 12, 30)).total_seconds() / 86400.0
    return float((d - EPOCH).days)


def r6(x: float) -> float:
    """Redondeo a 6 decimales idéntico al del script: Math.round(x * 1e6) / 1e6."""
    return math.floor(x * 1e6 + 0.5) / 1e6


def estado_de(stock: float, smin: float, smax: float, activo: bool, margen: float) -> str:
    if not activo:
        return "INACTIVO"
    if stock < 0:
        return "INCONSISTENTE"
    if stock == 0:
        return "AGOTADO"
    if stock <= smin:
        return "CRÍTICO"
    if stock <= smin * (1 + margen):
        return "BAJO"
    if smax > 0 and stock > smax:
        return "SOBRESTOCK"
    return "ÓPTIMO"


def _num(v) -> float:
    return float(v) if isinstance(v, (int, float)) and not isinstance(v, bool) else 0.0


def proyectar(productos: list[dict], movs: list[dict], margen: float, hoy: dt.date,
              proveedores: list[dict] | None = None) -> dict:
    """Instantánea de lectura a partir de los movimientos consolidados (misma regla que RecalcularStock.ts).

    productos: dicts con SKU, Producto, Categoría, Proveedor, Unidad, StockMin, StockMax, CostoUnitario, Activo.
    movs: dicts con SKU, CantidadNeta, Fecha (serial), Estado y Tipo. proveedores: dicts con Proveedor, Contacto,
    Teléfono, Correo y DiasEntrega (orden de 04_PROVEEDORES). Devuelve {"stock", "alertas", "pedido", "n"}.
    """
    ent, sal, ult, s30 = {}, {}, {}, {}
    n = 0
    hoy_s = serial(hoy)
    desde = math.floor(hoy_s) - (VENTANA_DIAS - 1)
    for m in movs:
        if not str(m.get("Estado", "")).startswith("✔"):
            continue
        sku = str(m.get("SKU", "")).strip()
        neta = _num(m.get("CantidadNeta"))
        if not sku:
            continue
        n += 1
        if neta >= 0:
            ent[sku] = ent.get(sku, 0.0) + neta
        else:
            sal[sku] = sal.get(sku, 0.0) - neta
        f = m.get("Fecha")
        if isinstance(f, (int, float)) and f > ult.get(sku, 0):
            ult[sku] = f
        if str(m.get("Tipo", "")) == "SALIDA" and isinstance(f, (int, float)) and f >= desde:
            s30[sku] = s30.get(sku, 0.0) - neta
    provs = proveedores or []
    pos_prov = {}
    for j, pv in enumerate(provs):
        nombre = str(pv.get("Proveedor", "")).strip()
        if nombre and nombre not in pos_prov:
            pos_prov[nombre] = j
    stock_rows, cand, ped, rank = [], [], [], []
    for i, p in enumerate(productos):
        sku = str(p.get("SKU", "")).strip()
        if not sku:
            continue
        activo = str(p.get("Activo", "")).strip().upper() != "NO"
        e, s = r6(ent.get(sku, 0.0)), r6(sal.get(sku, 0.0))
        stock = r6(e - s)
        smin, smax = _num(p.get("StockMin")), _num(p.get("StockMax"))
        costo = _num(p.get("CostoUnitario"))
        est = estado_de(stock, smin, smax, activo, margen)
        nivel = r6(max(0.0, stock) / smax) if smax > 0 else ""
        u = ult.get(sku, "")
        dias = int(math.floor(hoy_s) - math.floor(u)) if u != "" else ""
        v30 = r6(s30.get(sku, 0.0))
        cobertura = int(math.floor(max(0.0, stock) / (v30 / VENTANA_DIAS))) if v30 > 0 else ""
        fila = [sku, p.get("Producto", ""), p.get("Categoría", ""), p.get("Proveedor", ""), p.get("Unidad", ""),
                "SI" if activo else "NO", e, s, stock, smin, smax, nivel, est, costo, r6(max(0.0, stock) * costo), u,
                dias, v30, cobertura, ""]
        stock_rows.append(fila)
        if v30 > 0:
            rank.append((-v30, i, fila))
        tope = smax if smax > 0 else 2 * smin
        if activo and est in ALERTAS:
            cob = (max(0.0, stock) / smin) if smin > 0 else 0.0
            sug = 0.0 if est in ("SOBRESTOCK", "INCONSISTENTE") else r6(max(0.0, tope - max(0.0, stock)))
            cand.append(((ALERTAS.index(est), min(99, int(math.floor(cob * 10))), i),
                         [est, sku, p.get("Producto", ""), p.get("Categoría", ""), p.get("Proveedor", ""), stock,
                          smin, smax, p.get("Unidad", ""), r6(max(0.0, smin - stock)), sug, EST[est][7], u]))
        if activo and est in REPONER:
            apedir = r6(max(0.0, tope - max(0.0, stock)))
            if apedir > 0:
                nombre = str(p.get("Proveedor", "")).strip()
                j = pos_prov.get(nombre)
                pv = provs[j] if j is not None else {}
                de = pv.get("DiasEntrega")
                de = de if isinstance(de, (int, float)) and not isinstance(de, bool) else ""
                ped.append(((j if j is not None else 999, REPONER.index(est), i),
                            [nombre or SIN_PROVEEDOR, sku, p.get("Producto", ""), p.get("Unidad", ""), est, stock,
                             smin, smax, apedir, costo, r6(apedir * costo), de,
                             math.floor(hoy_s) + de if de != "" else "", pv.get("Contacto", ""),
                             pv.get("Teléfono", ""), pv.get("Correo", "")]))
    rank.sort(key=lambda x: (x[0], x[1]))
    for k, (_, _, fila) in enumerate(rank, start=1):
        fila[-1] = k
    cand.sort(key=lambda x: x[0])
    ped.sort(key=lambda x: x[0])
    return {"stock": stock_rows, "alertas": [[k + 1] + row for k, (_, row) in enumerate(cand)],
            "pedido": [row for _, row in ped], "n": n}


def _write_rows(ws, st, rows: list[list], fmts: list[str], widths_first_col: int = 1):
    fcache = [st.cell("calc", f, view=True) for f in fmts]
    date_f = st(num_format="dd/mm/yyyy", align="center", formula=True)
    for k in range(MAX_PROD):
        r0 = FIRST - 1 + k
        row = rows[k] if k < len(rows) else None
        for j, f in enumerate(fcache):
            v = row[j] if row else ""
            fmt = date_f if fmts[j] == "date" else f
            if isinstance(v, (int, float)) and not isinstance(v, bool) and v != "":
                ws.write_number(r0, widths_first_col + j, v, fmt)
            elif v:
                ws.write_string(r0, widths_first_col + j, str(v), fmt)
            else:
                ws.write_blank(r0, widths_first_col + j, None, fmt)


def build_stock2(ctx: Ctx, rows: list[list]):
    ws, st = ctx.sheets["15_STOCK"], ctx.st
    widths = [16] + STOCK_W + [16]
    lienzo(ws, widths, zoom=90)
    franja(ctx, ws, widths, "Stock · instantánea",
           "Capa de lectura a demanda · No se recalcula mientras la gente trabaja · Pulse «Recalcular stock» para "
           "actualizarla con las dos bitácoras", "stock", nav_start=7)
    barra(ctx, ws)
    cols = [Col(n, "calc", w, f) for n, w, f in zip(STOCK_COLS, STOCK_W, STOCK_FMT)]
    tabla(ctx, ws, "tblStock", cols, HDR, MAX_PROD, badges=False, header_tips=False)
    ws.set_row_pixels(HDR - 2, 16, st(bg_color=C["canvas"]))
    _write_rows(ws, st, rows, STOCK_FMT)
    L = {n: chr(66 + i) if i < 25 else "?" for i, n in enumerate(STOCK_COLS)}
    rng = lambda n: f"{L[n]}{FIRST}:{L[n]}{LAST_PROD}"  # noqa: E731
    estado_cf(ws, st, rng("Estado"), f"${L['Estado']}{FIRST}")
    ws.conditional_format(rng("Nivel"), {"type": "data_bar", "bar_color": "#90CAF9", "bar_solid": True,
                                         "min_type": "num", "min_value": 0, "max_type": "num", "max_value": 1})
    ws.conditional_format(rng("DiasSinMov"), {
        "type": "formula", "criteria": f'=AND(ISNUMBER(${L["DiasSinMov"]}{FIRST}),${L["DiasSinMov"]}{FIRST}>'
                                       f'cfgDiasSinRotacion,${L["StockActual"]}{FIRST}>0)',
        "format": st.cf(font_color=C["amber"], bold=True, bg_color=C["amber_lt"])})
    for n in ("Entradas", "Salidas", "StockActual", "StockMin", "StockMax"):
        ref = f"{L[n]}{FIRST}"
        ws.conditional_format(rng(n), {"type": "formula", "criteria": f"=AND(ISNUMBER({ref}),{ref}<>INT({ref}))",
                                       "format": st.cf(num_format="#,##0.00")})
    cob = f"${L['CoberturaDias']}{FIRST}"
    for crit, fg, bg in ((f"=AND(ISNUMBER({cob}),{cob}<3)", C["red"], C["red_lt"]),
                         (f"=AND(ISNUMBER({cob}),{cob}<7)", C["amber"], C["amber_lt"])):
        ws.conditional_format(rng("CoberturaDias"), {"type": "formula", "criteria": crit, "stop_if_true": True,
                                                     "format": st.cf(font_color=fg, bg_color=bg, bold=True)})
    rk = f"${L['RankSalidas30d']}{FIRST}"
    ws.conditional_format(rng("RankSalidas30d"), {"type": "formula", "criteria": f"=AND(ISNUMBER({rk}),{rk}<=10)",
                                                  "format": st.cf(font_color=C["brand_dk"], bold=True)})
    ws.conditional_format(f"B{FIRST}:{L[STOCK_COLS[-1]]}{LAST_PROD}", {
        "type": "formula", "criteria": f'=AND($B{FIRST}<>"",MOD(ROW(),2)=0)', "format": st.cf(bg_color=C["zebra"])})
    _barra_instantanea(ctx, ws, (1, 2), (3, 5), (6, 10))
    ws.freeze_panes(HDR, 2)
    ws.set_selection("B8")
    ws.set_landscape()
    ws.fit_to_pages(1, 0)
    ws.repeat_rows(HDR - 1)


def build_alertas2(ctx: Ctx, rows: list[list]):
    ws, st = ctx.sheets["16_ALERTAS"], ctx.st
    widths = [16] + ALERT_W + [16]
    lienzo(ws, widths, zoom=90)
    franja(ctx, ws, widths, "Alertas de stock · instantánea",
           "Priorizadas: inconsistentes, agotados, críticos, bajos y sobrestock · Cantidad sugerida hasta el máximo · "
           "Se actualiza con «Recalcular stock»", "alertas")
    barra(ctx, ws)
    cols = [Col(n, "calc", w, f) for n, w, f in zip(ALERT_COLS, ALERT_W, ALERT_FMT)]
    tabla(ctx, ws, "tblAlertas", cols, HDR, MAX_PROD, badges=False, header_tips=False)
    ws.set_row_pixels(HDR - 2, 16, st(bg_color=C["canvas"]))
    _write_rows(ws, st, rows, ALERT_FMT)
    L = {n: chr(66 + i) for i, n in enumerate(ALERT_COLS)}
    estado_cf(ws, st, f"{L['Estado']}{FIRST}:{L['Estado']}{LAST_PROD}", f"${L['Estado']}{FIRST}", solid=True)
    for n in ("Stock", "Faltante", "SugeridoPedir"):
        ref = f"{L[n]}{FIRST}"
        ws.conditional_format(f"{L[n]}{FIRST}:{L[n]}{LAST_PROD}", {
            "type": "formula", "criteria": f"=AND(ISNUMBER({ref}),{ref}<>INT({ref}))",
            "format": st.cf(num_format="#,##0.00")})
    ws.conditional_format(f"{L['SugeridoPedir']}{FIRST}:{L['SugeridoPedir']}{LAST_PROD}", {
        "type": "cell", "criteria": ">", "value": 0, "format": st.cf(font_color=C["brand_dk"], bold=True)})
    ws.conditional_format(f"B{FIRST}:O{LAST_PROD}", {
        "type": "formula", "criteria": f'=AND($B{FIRST}<>"",MOD(ROW(),2)=0)', "format": st.cf(bg_color=C["zebra"])})
    _barra_instantanea(ctx, ws, (1, 4), (5, 6), (7, 12))
    ws.freeze_panes(HDR, 0)
    ws.set_selection("B8")
    ws.set_landscape()
    ws.fit_to_pages(1, 0)
    ws.repeat_rows(HDR - 1)


def _barra_instantanea(ctx: Ctx, ws, calc: tuple[int, int], fresca: tuple[int, int], boton: tuple[int, int]):
    """Fila 6: cuándo y quién calculó la instantánea, frescura y el lugar del botón RecalcularStock."""
    st = ctx.st
    ws.set_row_pixels(5, 36, st(bg_color=C["canvas"]))
    chip(ctx, ws, 5, calc[0], calc[1], "=txtStockActualizado")
    chip(ctx, ws, 5, fresca[0], fresca[1], "=txtFrescuraCorta")
    for color, crit in ((("#FFFFFF", C["green"]), "=kpiNuevosDesdeCalculo=0"),
                        ((C["amber"], C["amber_lt"]), "=kpiNuevosDesdeCalculo>0")):
        ws.conditional_format(5, fresca[0], 5, fresca[1], {"type": "formula", "criteria": crit,
                                                           "format": st.cf(font_color=color[0], bg_color=color[1])})
    marcador_script(ctx, ws, 5, boton[0], boton[1], "Recalcular stock", "RecalcularStock")


def build_pedido2(ctx: Ctx, rows: list[list]):
    """18_PEDIDO: pedido sugerido por proveedor (instantánea que escribe RecalcularStock.ts). Sin celdas de ingreso:
    cada persona filtra por proveedor en su propia Vista de hoja e imprime (no afecta a los demás)."""
    ws, st = ctx.sheets["18_PEDIDO"], ctx.st
    widths = [16] + PEDIDO_W + [16]
    lienzo(ws, widths, zoom=90)
    franja(ctx, ws, widths, "Pedido sugerido por proveedor · instantánea",
           "Productos agotados, críticos y preventivos · Cantidad hasta el máximo · Filtre Proveedor en su Vista de "
           "hoja e imprima (Ctrl + P) · Se actualiza con «Recalcular stock»", "pedido")
    barra(ctx, ws)
    cols = [Col(n, "calc", w, f) for n, w, f in zip(PEDIDO_COLS, PEDIDO_W, PEDIDO_FMT)]
    tabla(ctx, ws, "tblPedido", cols, HDR, MAX_PROD, badges=False, header_tips=False)
    ws.set_row_pixels(HDR - 2, 16, st(bg_color=C["canvas"]))
    _write_rows(ws, st, rows, PEDIDO_FMT)
    L = {n: chr(66 + i) for i, n in enumerate(PEDIDO_COLS)}
    rng = lambda n: f"{L[n]}{FIRST}:{L[n]}{LAST_PROD}"  # noqa: E731
    estado_cf(ws, st, rng("Estado"), f"${L['Estado']}{FIRST}")
    for n in ("Stock", "Mínimo", "Máximo", "APedir"):
        ref = f"{L[n]}{FIRST}"
        ws.conditional_format(rng(n), {"type": "formula", "criteria": f"=AND(ISNUMBER({ref}),{ref}<>INT({ref}))",
                                       "format": st.cf(num_format="#,##0.00")})
    # Separador visual entre proveedores (primera línea de cada uno en negrita con borde superior)
    prov = f"$B{FIRST}"
    ws.conditional_format(f"B{FIRST}:{L[PEDIDO_COLS[-1]]}{LAST_PROD}", {
        "type": "formula", "criteria": f'=AND({prov}<>"",{prov}<>$B{FIRST - 1})',
        "format": st.cf(top=2, top_color=C["brand"])})
    ws.conditional_format(rng("Proveedor"), {"type": "formula", "criteria": f'=AND({prov}<>"",{prov}<>$B{FIRST - 1})',
                                             "format": st.cf(bold=True, font_color=C["brand_dk"])})
    ws.conditional_format(f"B{FIRST}:{L[PEDIDO_COLS[-1]]}{LAST_PROD}", {
        "type": "formula", "criteria": f'=AND($B{FIRST}<>"",MOD(ROW(),2)=0)', "format": st.cf(bg_color=C["zebra"])})
    chip(ctx, ws, 5, 1, 1, '="Líneas: "&kpiPedidoLineas')
    chip(ctx, ws, 5, 2, 3, '="Proveedores: "&kpiPedidoProv&"   ·   Total estimado: $ "&FIXED(kpiPedidoTotal,0)',
         color=C["brand_dk"])
    chip(ctx, ws, 5, 4, 6, "=txtFrescuraCorta")
    marcador_script(ctx, ws, 5, 8, 12, "Recalcular stock", "RecalcularStock")
    ws.set_row_pixels(5, 36, st(bg_color=C["canvas"]))
    ws.freeze_panes(HDR, 0)
    ws.set_selection("B8")
    ws.set_landscape()
    ws.set_paper(1)
    ws.fit_to_pages(1, 0)
    ws.repeat_rows(HDR - 1)
    ws.set_header('&L&"Segoe UI,Bold"&9Pedido sugerido · M-INV&R&"Segoe UI,Regular"&8&D')

