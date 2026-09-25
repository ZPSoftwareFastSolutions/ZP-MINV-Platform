"""
M-INV V2.1 · 00_PORTADA_GERENCIA: tablero de gerencia (ADMIN y CONSULTA).

Solo lectura y solo celdas: indicadores de la instantánea y de las bitácoras, tendencia de ventas, valor por categoría,
los 10 productos más vendidos (ranking que calcula RecalcularStock.ts) y la actividad de cada usuario en el mes
(registros consolidados y bloqueos, desde 14_ACTIVIDAD).
"""
from __future__ import annotations

from minv.base import C, FONT_SB, S_LISTAS, Ctx, q

from .base2 import S_ACT, S_ALERT, S_PED, S_PG, S_STOCK, boton, celda, seccion
from .portadas import _base, _decimales, _paso, _pie, _tarjetas

HEIGHTS_G = {5: 10, 6: 40, 7: 8, 8: 34, 9: 10, 10: 22, 11: 36, 12: 36, 13: 12, 14: 22, 15: 20, 16: 42, 17: 20,
             18: 8, 19: 20, 20: 42, 21: 20, 22: 12, 23: 22, 37: 12, 38: 22, 39: 24, 50: 12, 51: 26}
for _r in range(24, 37):
    HEIGHTS_G[_r] = 22
for _r in range(40, 50):
    HEIGHTS_G[_r] = 22
TOP_ROWS = range(40, 50)
MES = "DATE(YEAR(TODAY()),MONTH(TODAY()),1)"


def build_portada_gerencia(ctx: Ctx):
    ws, st = _base(ctx, S_PG, "M-INV · Gerencia", "gerencia", HEIGHTS_G)
    _paso(ctx, ws, st, "txtPasoGerencia")
    seccion(ctx, ws, 10, 1, "ACCESOS")
    boton(ctx, ws, 11, 1, 3, "▦  STOCK Y COBERTURA", S_STOCK, "B8", fill=C["teal"], rows=2, size=11,
          tip="Instantánea de stock con cobertura en días y ranking de ventas")
    for c0, c1, color, dest, nombre in ((4, 6, C["red"], S_ALERT, "txtAlertasBtn"),
                                        (7, 9, C["brand"], S_PED, "txtPedidoBtn")):
        f = st(font_name=FONT_SB, font_size=11, align="center", valign="vcenter", text_wrap=True,
               font_color=C["white"], bg_color=color, border=2, border_color=C["white"], formula=True)
        ws.merge_range(11, c0, 12, c1, "", f)
        ws.write_formula(11, c0, f'=HYPERLINK("#{q(dest)}!A1",{nombre})', f)
    boton(ctx, ws, 11, 10, 12, "☰  ACTIVIDAD Y AUDITORÍA", S_ACT, "B8", fill=C["slate"], rows=2, size=11,
          tip="Quién registró, recalculó o fue bloqueado, y cuándo")
    _tarjetas(ctx, ws, st, [
        ("VALOR DEL INVENTARIO", "=kpiValor", "$ #,##0", C["brand"], '="a costo unitario de catálogo"'),
        ("REQUIEREN ACCIÓN", "=kpiEnAlerta", "#,##0", C["red"],
         '="agotados "&kpiAgotados&" · críticos "&kpiCriticos&" · bajos "&kpiBajos'),
        ("PEDIDO SUGERIDO", "=kpiPedidoTotal", "$ #,##0", C["amber"],
         '=kpiPedidoLineas&" línea(s) · "&kpiPedidoProv&" proveedor(es)"'),
        ("MOVIMIENTOS DEL MES", "=kpiMovMes", "#,##0", C["teal"], '="entradas, ajustes y salidas consolidadas"'),
    ], fila=15, titulo="INDICADORES")
    _tarjetas(ctx, ws, st, [
        ("UNIDADES VENDIDAS · 30 DÍAS", "=kpiUnid30", "#,##0", C["green"], '="salidas consolidadas de 10B"'),
        ("COBERTURA MEDIANA", '=IF(kpiCobMed="","—",kpiCobMed)', '0" días"', C["purple"],
         '="días de stock al ritmo de venta actual"'),
        ("BLOQUEOS DEL MES", "=kpiBloqMes", "#,##0", C["red"], '="intentos rechazados por los scripts"'),
        ("USUARIOS ACTIVOS", "=kpiUsuarios", "#,##0", C["slate"], '="administradores: "&kpiAdmins'),
    ], fila=19, titulo=None)

    seccion(ctx, ws, 23, 1, "TENDENCIA Y VALOR")
    _grafico_meses(ctx, ws)
    _grafico_categorias(ctx, ws)

    # Top 10 más vendidos (ranking de la instantánea) y actividad por usuario (mes en curso)
    seccion(ctx, ws, 38, 1, "MÁS VENDIDOS · 30 DÍAS  (instantánea)")
    seccion(ctx, ws, 38, 7, "ACTIVIDAD POR USUARIO · MES EN CURSO")
    hdr = st(bold=True, font_size=9, font_color=C["white"], bg_color=C["hdr_calc"], align="center")
    for label, c0, c1 in (("#", 1, 1), ("Producto", 2, 4), ("Vendido 30 d", 5, 5), ("Cobertura", 6, 6),
                          ("Usuario", 7, 8), ("Rol", 9, 9), ("Registros", 10, 10), ("Bloqueos", 11, 11),
                          ("Último registro", 12, 12)):
        celda(ws, st, 39, c0, c1, label, hdr)
    base = dict(font_size=9, bg_color=C["white"], bottom=1, bottom_color=C["row_line"], formula=True)
    txt = st(indent=1, **base)
    num = st(align="right", indent=1, num_format="#,##0;-#,##0;0", **base)
    cen = st(align="center", **base)
    dias = st(align="center", num_format='0" d"', **base)
    fecha = st(align="center", num_format="dd/mm hh:mm", **base)
    for k, r in enumerate(TOP_ROWS, start=1):
        pos = f"MATCH({k},tblStock[RankSalidas30d],0)"
        celda(ws, st, r, 1, 1, f'=IF(ISNA({pos}),"",{k})', cen, formula=True)
        celda(ws, st, r, 2, 4, f'=IFERROR(INDEX(tblStock[SKU],{pos})&" · "&INDEX(tblStock[Producto],{pos}),"")', txt,
              formula=True)
        celda(ws, st, r, 5, 5, f'=IFERROR(INDEX(tblStock[Salidas30d],{pos}),"")', num, formula=True)
        celda(ws, st, r, 6, 6, f'=IFERROR(INDEX(tblStock[CoberturaDias],{pos}),"")', dias, formula=True)
        correo = f"INDEX(tblUsuarios[Correo],{k})"
        vacio = f'{correo}=""'
        celda(ws, st, r, 7, 8, f'=IF({vacio},"",INDEX(tblUsuarios[Nombre],{k}))', txt, formula=True)
        celda(ws, st, r, 9, 9, f'=IF({vacio},"",INDEX(tblUsuarios[Rol],{k}))', cen, formula=True)
        celda(ws, st, r, 10, 10, f'=IF({vacio},"",COUNTIFS(tblEntradas[Usuario_O365],{correo},tblEntradas[Fecha],">="&{MES},'
                                 f'tblEntradas[Estado],"✔*")+COUNTIFS(tblSalidas[Usuario_O365],{correo},'
                                 f'tblSalidas[Fecha],">="&{MES},tblSalidas[Estado],"✔*"))', num, formula=True)
        celda(ws, st, r, 11, 11, f'=IF({vacio},"",COUNTIFS(tblActividad[Usuario_O365],{correo},tblActividad[Resultado],'
                                 f'"✖*",tblActividad[Timestamp],">="&{MES}))', num, formula=True)
        ult = (f'MAX(IFERROR(_xlfn.AGGREGATE(14,6,tblEntradas[Timestamp]/(tblEntradas[Usuario_O365]={correo}),1),0),'
               f'IFERROR(_xlfn.AGGREGATE(14,6,tblSalidas[Timestamp]/(tblSalidas[Usuario_O365]={correo}),1),0))')
        celda(ws, st, r, 12, 12, f'=IF({vacio},"",IF({ult}=0,"—",{ult}))', fecha, formula=True)
    first, last = TOP_ROWS[0] + 1, TOP_ROWS[-1] + 1
    _decimales(ws, st, f"F{first}:F{last}", f"F{first}")
    ws.conditional_format(f"L{first}:L{last}", {"type": "cell", "criteria": ">", "value": 0,
                                                "format": st.cf(font_color=C["red"], bold=True)})
    ws.conditional_format(f"G{first}:G{last}", {"type": "formula",
                                                "criteria": f"=AND(ISNUMBER($G{first}),$G{first}<7)",
                                                "format": st.cf(font_color=C["amber"], bold=True)})
    _pie(ctx, ws, st, fila=51)
    ws.set_selection("B12")


def _grafico_meses(ctx: Ctx, ws):
    ch = ctx.wb.add_chart({"type": "column"})
    ch.add_series({"name": "Unidades despachadas", "categories": f"={q(S_LISTAS)}!$I$8:$I$13",
                   "values": f"={q(S_LISTAS)}!$J$8:$J$13", "fill": {"color": C["teal"]}, "gap": 70,
                   "data_labels": {"value": True, "num_format": "#,##0", "font": {"size": 8}}})
    ch.set_title({"name": "Unidades despachadas por mes", "name_font": {"size": 10, "bold": True, "color": C["text"]}})
    ch.set_legend({"none": True})
    ch.set_y_axis({"visible": False, "major_gridlines": {"visible": False}})
    ch.set_x_axis({"num_format": "mmm yy", "num_font": {"size": 8}})
    ch.set_chartarea({"border": {"color": C["border"]}, "fill": {"color": "#FFFFFF"}})
    ch.set_size({"width": 560, "height": 282})
    ws.insert_chart(24, 1, ch, {"x_offset": 2, "y_offset": 2, "object_position": 1,
                                "description": "Gráfico: unidades despachadas por mes"})


def _grafico_categorias(ctx: Ctx, ws):
    n = ctx.layout.get("n_cats", 6)
    ch = ctx.wb.add_chart({"type": "bar"})
    ch.add_series({"name": "Valor del inventario", "categories": f"={q(S_LISTAS)}!$L$8:$L${7 + n}",
                   "values": f"={q(S_LISTAS)}!$M$8:$M${7 + n}", "fill": {"color": C["brand"]}, "gap": 60,
                   "data_labels": {"value": True, "num_format": '$ #,##0,,"M"', "font": {"size": 8}}})
    ch.set_title({"name": "Valor del inventario por categoría",
                  "name_font": {"size": 10, "bold": True, "color": C["text"]}})
    ch.set_legend({"none": True})
    ch.set_x_axis({"visible": False, "major_gridlines": {"visible": False}})
    ch.set_y_axis({"reverse": True, "num_font": {"size": 8}})
    ch.set_chartarea({"border": {"color": C["border"]}, "fill": {"color": "#FFFFFF"}})
    ch.set_size({"width": 560, "height": 282})
    ws.insert_chart(24, 7, ch, {"x_offset": 2, "y_offset": 2, "object_position": 1,
                                "description": "Gráfico: valor del inventario por categoría"})
