"""
M-INV V2 · Portadas por rol: 00_PORTADA_BODEGA y 00_PORTADA_VENTAS.

Solo celdas (vínculos, tarjetas, listas) y gráficos nativos: funcionan igual en Excel para la web. Cada portada muestra
únicamente las acciones de su rol, la frescura de la instantánea de stock y el lugar del botón «Recalcular stock».
"""
from __future__ import annotations

from minv.base import C, ESTADOS, FONT_SB, S_LISTAS, Ctx, estado_cf, q

from .base2 import (CAP_FIRST, S_ALERT, S_ENT, S_PB, S_PV, S_SAL, S_STOCK, VERSION2, boton, ccol, celda, franja,
                    lienzo, marcador_script, seccion)

GRID = [24] + [94] * 12 + [24]
HEIGHTS = {5: 10, 6: 40, 7: 8, 8: 34, 9: 10, 10: 22, 11: 36, 12: 36, 13: 12, 14: 22, 15: 20, 16: 42, 17: 20,
           18: 12, 19: 22, 20: 24, 31: 12, 32: 22, 33: 24, 42: 12, 43: 26}
for _r in range(21, 31):
    HEIGHTS[_r] = 22
for _r in range(34, 42):
    HEIGHTS[_r] = 22
CRIT_ROWS = range(21, 31)      # 10 filas de la lista de la instantánea
LAST_ROWS = range(34, 42)      # 8 últimos movimientos


def _base(ctx: Ctx, sheet: str, titulo: str, activo: str):
    ws, st = ctx.sheets[sheet], ctx.st
    lienzo(ws, GRID)
    franja(ctx, ws, GRID, titulo, "", activo)
    ink = C["ink"]
    ws.write_formula(2, 1, "=txtEmpresa", st(bg_color=ink, font_color=C["band_sub"], font_size=9, formula=True))
    ws.merge_range(1, 9, 1, 12, "", st(bg_color=ink))
    ws.write_formula(1, 9, "=txtHoy", st(bg_color=ink, font_color=C["white"], align="right", font_size=10,
                                         bold=True, formula=True))
    ws.merge_range(2, 9, 2, 12, f"Edición colaborativa · M-INV V{VERSION2}",
                   st(bg_color=ink, font_color=C["band_sub"], align="right", font_size=8.5))
    canvas = st(bg_color=C["canvas"])
    for r, h in HEIGHTS.items():
        ws.set_row_pixels(r, h, canvas)
    # Frescura de la instantánea + lugar del botón del script
    fb = st(font_name=FONT_SB, font_size=10, indent=1, bg_color=C["white"], border=1, border_color=C["border"],
            formula=True)
    celda(ws, st, 6, 1, 8, '=txtFrescura&"      ·      "&txtStockActualizado', fb, formula=True)
    for crit, fg, bg in (("=N(stkActualizado)=0", C["amber"], C["amber_lt"]),
                         ("=kpiNuevosDesdeCalculo>0", C["amber"], C["amber_lt"]),
                         ("=kpiNuevosDesdeCalculo=0", C["green"], C["green_lt"])):
        ws.conditional_format(6, 1, 6, 8, {"type": "formula", "criteria": crit, "stop_if_true": True,
                                           "format": st.cf(font_color=fg, bg_color=bg)})
    marcador_script(ctx, ws, 6, 9, 12, "Recalcular stock", "RecalcularStock")
    return ws, st


def _paso(ctx: Ctx, ws, st, nombre: str):
    f = st(font_name=FONT_SB, font_size=11, indent=1, bg_color=C["blue_lt"], font_color=C["brand_dk"],
           border=1, border_color=C["border"], formula=True)
    celda(ws, st, 8, 1, 12, f"={nombre}", f, formula=True)
    for crit, fg, bg in (('=LEFT({0},1)="✖"', C["red"], C["red_lt"]), ('=LEFT({0},1)="●"', C["amber"], C["amber_lt"]),
                         ('=LEFT({0},1)="✔"', C["green"], C["green_lt"])):
        ws.conditional_format(8, 1, 8, 12, {"type": "formula", "criteria": crit.format(nombre),
                                            "format": st.cf(font_color=fg, bg_color=bg)})


def _tarjetas(ctx: Ctx, ws, st, cards):
    seccion(ctx, ws, 14, 1, "INDICADORES")
    for i, (label, formula, nf, color, note) in enumerate(cards):
        c0, c1 = 1 + 3 * i, 3 + 3 * i
        edge = dict(bg_color=C["white"], left=1, left_color=C["border"], right=1, right_color=C["border"])
        ws.merge_range(15, c0, 15, c1, label, st(font_size=8.5, bold=True, font_color=C["muted"], indent=1,
                                                 top=5, top_color=color, **edge))
        ws.merge_range(16, c0, 16, c1, "", st(**edge))
        ws.write_formula(16, c0, formula, st(font_name=FONT_SB, font_size=22, font_color=color, indent=1,
                                             num_format=nf, formula=True, **edge))
        ws.merge_range(17, c0, 17, c1, "", st(**edge))
        ws.write_formula(17, c0, note, st(font_size=8.5, font_color=C["muted"], indent=1, bottom=1,
                                          bottom_color=C["border"], formula=True, **edge))


def _lista_alertas(ctx: Ctx, ws, st, titulo: str, filtro: tuple[str, ...] | None, cols):
    """Lista de la instantánea de alertas (16_ALERTAS) en las filas 21-30, columnas B..I."""
    seccion(ctx, ws, 19, 1, titulo)
    hdr = st(bold=True, font_size=9, font_color=C["white"], bg_color=C["hdr_calc"], align="center")
    for label, c0, c1, _ in cols:
        celda(ws, st, 20, c0, c1, label, hdr)
    txt = st(font_size=9, indent=1, bg_color=C["white"], bottom=1, bottom_color=C["row_line"], formula=True)
    num = st(font_size=9, align="right", indent=1, num_format="#,##0;-#,##0;0", bg_color=C["white"],
             bottom=1, bottom_color=C["row_line"], formula=True)
    fdate = st(font_size=9, align="center", num_format="dd/mm/yyyy", bg_color=C["white"], bottom=1,
               bottom_color=C["row_line"], formula=True)
    for k, r in enumerate(CRIT_ROWS, start=1):
        est = f"INDEX(tblAlertas[Estado],{k})"
        cond = (f'OR({est}={{' + ",".join(f'"{x}"' for x in filtro) + '})') if filtro else f'{est}<>""'
        for label, c0, c1, expr in cols:
            f = num if label in ("Stock", "Mínimo", "Sugerido") else fdate if label == "Últ. mov." else txt
            celda(ws, st, r, c0, c1, f'=IF({cond},{expr.format(k=k)},"")', f, formula=True)
    estado_cf(ws, st, f"B{CRIT_ROWS[0] + 1}:B{CRIT_ROWS[-1] + 1}", f"$B{CRIT_ROWS[0] + 1}", solid=True)
    _decimales(ws, st, f"G{CRIT_ROWS[0] + 1}:I{CRIT_ROWS[-1] + 1}", f"G{CRIT_ROWS[0] + 1}")


def _ultimos(ctx: Ctx, ws, st, tabla: str, titulo: str):
    seccion(ctx, ws, 32, 1, titulo)
    hdr = st(bold=True, font_size=9, font_color=C["white"], bg_color=C["hdr_calc"], align="center")
    cols = [("Fecha", 1, 1, "Fecha"), ("Tipo", 2, 2, "Tipo"), ("Producto", 3, 6, "Producto"),
            ("Cantidad", 7, 7, "Cantidad"), ("Registró", 8, 9, "Registró"), ("Estado", 10, 11, "Estado"),
            ("Documento", 12, 12, "Documento")]
    for label, c0, c1, _ in cols:
        celda(ws, st, 33, c0, c1, label, hdr)
    base = dict(font_size=9, bg_color=C["white"], bottom=1, bottom_color=C["row_line"], formula=True)
    fmts = {"Fecha": st(align="center", num_format="dd/mm/yyyy", **base),
            "Cantidad": st(align="right", indent=1, num_format="#,##0;-#,##0;0", **base),
            "Tipo": st(align="center", bold=True, **base)}
    for k, r in enumerate(LAST_ROWS):
        idx = f"ROWS({tabla}[ID])-{k}"
        for label, c0, c1, col in cols:
            expr = (f'=IFERROR(IF(INDEX({tabla}[ID],{idx})="","",INDEX({tabla}[{col}],{idx})&""),"")'
                    if label not in ("Fecha", "Cantidad") else
                    f'=IFERROR(IF(INDEX({tabla}[ID],{idx})="","",INDEX({tabla}[{col}],{idx})),"")')
            celda(ws, st, r, c0, c1, expr, fmts.get(label, st(indent=1, **base)), formula=True)
    first, last = LAST_ROWS[0] + 1, LAST_ROWS[-1] + 1
    _decimales(ws, st, f"H{first}:H{last}", f"H{first}")
    ws.conditional_format(f"K{first}:L{last}", {"type": "formula", "criteria": f'=LEFT($K{first},1)="✖"',
                                                "format": st.cf(font_color=C["red"], bold=True)})
    ws.conditional_format(f"K{first}:L{last}", {"type": "formula", "criteria": f'=LEFT($K{first},1)="✔"',
                                                "format": st.cf(font_color=C["green"])})


def _decimales(ws, st, rng: str, ref: str):
    ws.conditional_format(rng, {"type": "formula", "criteria": f"=AND(ISNUMBER({ref}),{ref}<>INT({ref}))",
                                "format": st.cf(num_format="#,##0.00")})


def _pie(ctx: Ctx, ws, st):
    f = st(font_size=8.5, font_color=C["muted"], bg_color=C["canvas"], indent=1, formula=True)
    celda(ws, st, 43, 1, 7, "=txtBitacoras", f, formula=True)
    celda(ws, st, 43, 8, 12, "M-INV V2 · Colaborativo en Microsoft 365 · Z&P Software Fast Solutions",
          st(font_size=8.5, font_color=C["muted"], bg_color=C["canvas"], align="right"))


def build_portada_bodega(ctx: Ctx):
    ws, st = _base(ctx, S_PB, "M-INV · Bodega", "bodega")
    _paso(ctx, ws, st, "txtPasoBodega")
    seccion(ctx, ws, 10, 1, "¿QUÉ NECESITA HACER?")
    boton(ctx, ws, 11, 1, 4, "⇩   REGISTRAR ENTRADA", S_ENT, f"{ccol('Tipo')}{CAP_FIRST}", fill=C["green"], rows=2,
          size=12, tip="Ir a su fila de captura en 10A_ENTRADAS (tipo ENTRADA o SALDO INICIAL)")
    boton(ctx, ws, 11, 5, 8, "±   REGISTRAR AJUSTE", S_ENT, f"{ccol('Tipo')}{CAP_FIRST}", fill=C["amber"], rows=2,
          size=12, tip="Ir a su fila de captura en 10A_ENTRADAS y elegir AJUSTE (+) o AJUSTE (-)")
    f = st(font_name=FONT_SB, font_size=12, align="center", valign="vcenter", font_color=C["white"],
           bg_color=C["red"], border=2, border_color=C["white"], formula=True)
    ws.merge_range(11, 9, 12, 12, "", f)
    ws.write_formula(11, 9, f'=HYPERLINK("#{q(S_ALERT)}!A1",txtAlertasBtn)', f)
    _tarjetas(ctx, ws, st, [
        ("ENTRADAS DE HOY", "=kpiEntradasHoy", "#,##0", C["green"], '="Ajustes del mes: "&kpiAjustesMes'),
        ("AGOTADOS", "=kpiAgotados", "#,##0", C["red"], '="sin stock en la instantánea"'),
        ("CRÍTICOS Y PREVENTIVOS", "=kpiCriticos+kpiBajos", "#,##0", C["amber"], '="en o cerca del mínimo"'),
        ("VALOR DEL INVENTARIO", "=kpiValor", "$ #,##0", C["brand"], '="a costo unitario de catálogo"'),
    ])
    _lista_alertas(ctx, ws, st, "PRODUCTOS QUE REQUIEREN ACCIÓN  (instantánea de 16_ALERTAS)", None, [
        ("Estado", 1, 1, "INDEX(tblAlertas[Estado],{k})"),
        ("Producto", 2, 5, 'INDEX(tblAlertas[SKU],{k})&" · "&INDEX(tblAlertas[Producto],{k})'),
        ("Stock", 6, 6, "INDEX(tblAlertas[Stock],{k})"),
        ("Mínimo", 7, 7, "INDEX(tblAlertas[Mínimo],{k})"),
        ("Sugerido", 8, 8, "INDEX(tblAlertas[SugeridoPedir],{k})"),
    ])
    _grafico_salud(ctx, ws)
    _ultimos(ctx, ws, st, "tblEntradas", "ÚLTIMAS ENTRADAS Y AJUSTES  (bitácora oficial 10A)")
    _pie(ctx, ws, st)
    ws.set_selection("B12")


def build_portada_ventas(ctx: Ctx):
    ws, st = _base(ctx, S_PV, "M-INV · Ventas", "ventas")
    _paso(ctx, ws, st, "txtPasoVentas")
    seccion(ctx, ws, 10, 1, "¿QUÉ NECESITA HACER?")
    boton(ctx, ws, 11, 1, 6, "⇧   REGISTRAR SALIDA", S_SAL, f"{ccol('Producto')}{CAP_FIRST}", fill=C["brand"],
          rows=2, size=12, tip="Ir a su fila de captura en 10B_SALIDAS")
    boton(ctx, ws, 11, 7, 12, "▦   CONSULTAR DISPONIBILIDAD", S_STOCK, "B8", fill=C["teal"], rows=2, size=12,
          tip="Stock por producto (instantánea). Para el disponible exacto, elija el producto en su fila de 10B")
    _tarjetas(ctx, ws, st, [
        ("SALIDAS DE HOY", "=kpiSalidasHoy", "#,##0", C["brand"], '="Unidades despachadas: "&kpiUnidadesHoy'),
        ("SALIDAS DEL MES", "=kpiSalidasMes", "#,##0", C["teal"], '="registros consolidados en 10B"'),
        ("PRODUCTOS CON STOCK", "=kpiDisponibles", "#,##0", C["green"], '="de "&kpiActivos&" activos"'),
        ("AGOTADOS · NO OFRECER", "=kpiAgotados", "#,##0", C["red"], '="confirme con Bodega"'),
    ])
    _lista_alertas(ctx, ws, st, "AGOTADOS Y CRÍTICOS  —  confirme con Bodega antes de ofrecer",
                   ("INCONSISTENTE", "AGOTADO", "CRÍTICO"), [
                       ("Estado", 1, 1, "INDEX(tblAlertas[Estado],{k})"),
                       ("Producto", 2, 5, 'INDEX(tblAlertas[SKU],{k})&" · "&INDEX(tblAlertas[Producto],{k})'),
                       ("Stock", 6, 6, "INDEX(tblAlertas[Stock],{k})"),
                       ("Unidad", 7, 7, "INDEX(tblAlertas[Unidad],{k})"),
                       ("Últ. mov.", 8, 8, "INDEX(tblAlertas[UltimoMov],{k})"),
                   ])
    _grafico_salidas(ctx, ws)
    _ultimos(ctx, ws, st, "tblSalidas", "ÚLTIMAS SALIDAS  (bitácora oficial 10B)")
    _pie(ctx, ws, st)
    ws.set_selection("B12")


# ---------------------------------------------------------------------------
# Gráficos (series en 90_LISTAS, columnas F:H)
# ---------------------------------------------------------------------------
def series_graficos(ctx: Ctx):
    ws, st = ctx.sheets[S_LISTAS], ctx.st
    hdr = st.hdr("calc")
    ws.set_column_pixels(5, 5, 130)
    ws.set_column_pixels(6, 6, 90)
    ws.write_string(6, 5, "Estado", hdr)
    ws.write_string(6, 6, "Productos", hdr)
    f = st(indent=1, border=1, border_color=C["border"])
    n = st(border=1, border_color=C["border"], formula=True)
    for i, e in enumerate(ESTADOS[:6]):
        ws.write_string(7 + i, 5, e[0], f)
        ws.write_formula(7 + i, 6, f"=COUNTIF(tblStock[Estado],F{8 + i})", n)
    ws.write_string(15, 5, "Día", hdr)
    ws.write_string(15, 6, "Unidades", hdr)
    for d in range(7):
        ws.write_formula(16 + d, 5, f"=TODAY()-{6 - d}", st(num_format="dd/mm", border=1, border_color=C["border"],
                                                           formula=True))
        ws.write_formula(16 + d, 6, f'=-SUMIFS(tblSalidas[CantidadNeta],tblSalidas[Fecha],F{17 + d},'
                                    f'tblSalidas[Estado],"✔*")', n)


def _grafico_salud(ctx: Ctx, ws):
    ch = ctx.wb.add_chart({"type": "doughnut"})
    ch.add_series({"name": "Productos", "categories": f"={q(S_LISTAS)}!$F$8:$F$13",
                   "values": f"={q(S_LISTAS)}!$G$8:$G$13",
                   "points": [{"fill": {"color": e[5]}, "border": {"color": "#FFFFFF"}} for e in ESTADOS[:6]],
                   "data_labels": {"value": True, "font": {"color": "#FFFFFF", "bold": True, "size": 9}}})
    ch.set_hole_size(55)
    ch.set_title({"name": "Salud del inventario", "name_font": {"size": 10, "bold": True, "color": C["text"]}})
    ch.set_legend({"position": "right", "font": {"size": 8}})
    ch.set_chartarea({"border": {"color": C["border"]}, "fill": {"color": "#FFFFFF"}})
    ch.set_size({"width": 372, "height": 262})
    ws.insert_chart(19, 9, ch, {"x_offset": 4, "y_offset": 0, "object_position": 1,
                                "description": "Gráfico: productos por estado (instantánea)"})


def _grafico_salidas(ctx: Ctx, ws):
    ch = ctx.wb.add_chart({"type": "column"})
    ch.add_series({"name": "Unidades despachadas", "categories": f"={q(S_LISTAS)}!$F$17:$F$23",
                   "values": f"={q(S_LISTAS)}!$G$17:$G$23", "fill": {"color": C["brand"]}, "gap": 60,
                   "data_labels": {"value": True, "num_format": "#,##0", "font": {"size": 8}}})
    ch.set_title({"name": "Unidades despachadas · últimos 7 días",
                  "name_font": {"size": 10, "bold": True, "color": C["text"]}})
    ch.set_legend({"none": True})
    ch.set_y_axis({"visible": False, "major_gridlines": {"visible": False}})
    ch.set_x_axis({"num_format": "dd/mm", "num_font": {"size": 8}})
    ch.set_chartarea({"border": {"color": C["border"]}, "fill": {"color": "#FFFFFF"}})
    ch.set_size({"width": 372, "height": 262})
    ws.insert_chart(19, 9, ch, {"x_offset": 4, "y_offset": 0, "object_position": 1,
                                "description": "Gráfico: unidades despachadas por día"})

