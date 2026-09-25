"""
M-INV V2.1 · 17_CONSULTA: consulta de producto por usuario (colaborativa).

Cada usuario operativo (ADMIN, BODEGA, VENTAS) tiene su propia fila (asignada por su posición en 02_USUARIOS):
elige un producto y la fila muestra al instante su ficha, calculada con las dos bitácoras oficiales (no depende de
«Recalcular stock»). Nadie comparte el selector: no hay colisiones de coautoría (regla C-02).
"""
from __future__ import annotations

from minv.base import C, Col, Ctx, estado_cf, nested_if, this_row

from .base2 import (MAX_CONS, S_CONS, barra, celda, chip, con_margen, fecha_txt, franja, lienzo, msg, seccion, tabla)

HDR_C, FIRST_C = 8, 9
LAST_C = FIRST_C + MAX_CONS - 1
OK = '"✔*"'

CONS_COLS = ["Usuario", "Producto", "Disponible", "Estado", "Mínimo", "Máximo", "Sugerido", "Proveedor", "Ubicación",
             "UltimoMovimiento", "Entradas30d", "Salidas30d", "CoberturaDias", "Valor", "Correo", "SKU", "TsE", "TsS"]
CONS_W = [168, 290, 104, 130, 90, 90, 96, 210, 112, 330, 112, 112, 134, 120, 220, 84, 110, 110]
HIDDEN_NAMES = ("Correo", "SKU", "TsE", "TsS")


def consulta_cols() -> list[Col]:
    t = "tblConsulta"
    r = this_row(t)
    n = f"ROW()-ROW({t}[#Headers])"
    pos = f'MATCH({r("SKU")},tblProductos[SKU],0)'
    prod = lambda c: f"INDEX(tblProductos[{c}],{pos})"  # noqa: E731
    vacio = f'{r("SKU")}=""'

    def ultimo(tb: str, ts: str) -> str:
        m = f"MATCH({ts},{tb}[Timestamp],0)"
        return (f'{fecha_txt(f"INDEX({tb}[Fecha],{m})")}&"  ·  "&INDEX({tb}[Tipo],{m})&" "&INDEX({tb}[Cantidad],{m})&'
                f'"  ·  "&INDEX({tb}[Registró],{m})')

    def ts(tb: str) -> str:
        return (f'=IF({vacio},0,IFERROR(_xlfn.AGGREGATE(14,6,{tb}[Timestamp]/(({tb}[SKU]={r("SKU")})*'
                f'(LEFT({tb}[Estado],1)="✔")),1),0))')

    estado = nested_if([
        (f'OR({vacio},{r("Disponible")}="")', '""'),
        (f"ISNA({pos})", '"NO EXISTE"'),
        (f'{prod("Activo")}="NO"', '"INACTIVO"'),
        (f'{r("Disponible")}<0', '"INCONSISTENTE"'),
        (f'{r("Disponible")}=0', '"AGOTADO"'),
        (f'{r("Disponible")}<=N({r("Mínimo")})', '"CRÍTICO"'),
        (f'{r("Disponible")}<=N({r("Mínimo")})*(1+cfgMargenAlerta)', '"BAJO"'),
        (f'AND(N({r("Máximo")})>0,{r("Disponible")}>N({r("Máximo")}))', '"SOBRESTOCK"'),
    ], '"ÓPTIMO"')
    spec = {
        "Usuario": Col("Usuario", "calc", 0, "text", "Persona dueña de esta fila (según 02_USUARIOS). Use solo SU fila.",
                       formula=f'=IFERROR(INDEX(tblUsuarios[Nombre],MATCH({n},tblUsuarios[OrdenConsulta],0)),"")'),
        "Producto": Col("Producto", "in", 0, "text", "Elija un producto de la lista (escriba parte del nombre o del SKU "
                                                     "para filtrarla). La ficha se calcula al instante."),
        "Disponible": Col("Disponible", "calc", 0, "strong", "Stock exacto ahora: suma de las dos bitácoras oficiales.",
                          formula=f'=IF({vacio},"",SUMIFS(tblEntradas[CantidadNeta],tblEntradas[SKU],{r("SKU")},'
                                  f'tblEntradas[Estado],{OK})+SUMIFS(tblSalidas[CantidadNeta],tblSalidas[SKU],'
                                  f'{r("SKU")},tblSalidas[Estado],{OK}))'),
        "Estado": Col("Estado", "calc", 0, "estado", "Semáforo con el disponible exacto (mismas reglas de 15_STOCK).",
                      formula="=" + estado),
        "Mínimo": Col("Mínimo", "calc", 0, "qty", "Stock mínimo del catálogo.",
                      formula=f'=IF({vacio},"",IFERROR(N({prod("StockMin")}),""))'),
        "Máximo": Col("Máximo", "calc", 0, "qty", "Stock máximo del catálogo (0 = sin máximo).",
                      formula=f'=IF({vacio},"",IFERROR(N({prod("StockMax")}),""))'),
        "Sugerido": Col("Sugerido", "calc", 0, "qty", "Cantidad a pedir para volver al máximo (o a 2 × mínimo) si "
                                                      "está agotado, crítico o bajo; 0 si no requiere reposición "
                                                      "(misma regla de 18_PEDIDO).",
                        formula=f'=IF(OR({vacio},{r("Disponible")}=""),"",IF(OR({r("Estado")}="AGOTADO",'
                                f'{r("Estado")}="CRÍTICO",{r("Estado")}="BAJO"),MAX(0,IF(N({r("Máximo")})>0,'
                                f'{r("Máximo")},2*N({r("Mínimo")}))-MAX(0,{r("Disponible")})),0))'),
        "Proveedor": Col("Proveedor", "calc", 0, "text", "Proveedor habitual del producto.",
                         formula=f'=IF({vacio},"",IFERROR({prod("Proveedor")}&"",""))'),
        "Ubicación": Col("Ubicación", "calc", 0, "center", "Ubicación en bodega.",
                         formula=f'=IF({vacio},"",IFERROR({prod("Ubicación")}&"",""))'),
        "UltimoMovimiento": Col("UltimoMovimiento", "calc", 0, "text",
                                "Fecha, tipo, cantidad y quién hizo el último movimiento consolidado.",
                                formula=f'=IF({vacio},"",IF(MAX({r("TsE")},{r("TsS")})=0,"Sin movimientos",'
                                        f'IF({r("TsE")}>={r("TsS")},{ultimo("tblEntradas", r("TsE"))},'
                                        f'{ultimo("tblSalidas", r("TsS"))})))'),
        "Entradas30d": Col("Entradas30d", "calc", 0, "qty", "Unidades que entraron en los últimos 30 días.",
                           formula=f'=IF({vacio},"",SUMIFS(tblEntradas[CantidadNeta],tblEntradas[SKU],{r("SKU")},'
                                   f'tblEntradas[Estado],{OK},tblEntradas[Fecha],">="&(TODAY()-29),'
                                   f'tblEntradas[CantidadNeta],">0"))'),
        "Salidas30d": Col("Salidas30d", "calc", 0, "qty", "Unidades vendidas o despachadas en los últimos 30 días.",
                          formula=f'=IF({vacio},"",-SUMIFS(tblSalidas[CantidadNeta],tblSalidas[SKU],{r("SKU")},'
                                  f'tblSalidas[Estado],{OK},tblSalidas[Fecha],">="&(TODAY()-29)))'),
        "CoberturaDias": Col("CoberturaDias", "calc", 0, "days",
                             "Días que alcanza el stock al ritmo de salidas de los últimos 30 días.",
                             formula=f'=IF(OR({vacio},N({r("Salidas30d")})<=0),"",INT(MAX(0,{r("Disponible")})/'
                                     f'({r("Salidas30d")}/30)))'),
        "Valor": Col("Valor", "calc", 0, "money", "Disponible × costo unitario del catálogo.",
                     formula=f'=IF({vacio},"",MAX(0,N({r("Disponible")}))*IFERROR(N({prod("CostoUnitario")}),0))'),
        "Correo": Col("Correo", "calc", 0, "text", "Técnica (oculta): correo del dueño de la fila.",
                      formula=f'=IFERROR(INDEX(tblUsuarios[Correo],MATCH({n},tblUsuarios[OrdenConsulta],0)),"")'),
        "SKU": Col("SKU", "calc", 0, "center", "Técnica (oculta): código del producto.",
                   formula=f'=IF({r("Producto")}="","",TRIM(LEFT({r("Producto")},FIND(" · ",{r("Producto")}&" · ")-1)))'),
        "TsE": Col("TsE", "calc", 0, "qty", "Técnica (oculta): último registro en 10A.", formula=ts("tblEntradas")),
        "TsS": Col("TsS", "calc", 0, "qty", "Técnica (oculta): último registro en 10B.", formula=ts("tblSalidas")),
    }
    cols = [spec[x] for x in CONS_COLS]
    for c, w in zip(cols, CONS_W):
        c.width = w
    return cols


def build_consulta(ctx: Ctx, inputs: dict[int, str]):
    """inputs: {n.º de fila: etiqueta del producto} (consultas de ejemplo en la demo)."""
    ws, st = ctx.sheets[S_CONS], ctx.st
    cols = consulta_cols()
    widths = con_margen([16] + CONS_W + [16])
    hidden = tuple(1 + CONS_COLS.index(x) for x in HIDDEN_NAMES)
    lienzo(ws, widths, zoom=85, hidden_cols=hidden)
    franja(ctx, ws, widths, "Consulta de producto",
           "Cada persona usa SU fila · Elija un producto y vea su ficha al instante (stock exacto, estado, cobertura y "
           "último movimiento) · No depende de «Recalcular stock»", "consulta")
    barra(ctx, ws)
    canvas = st(bg_color=C["canvas"])
    chip(ctx, ws, 5, 1, 1, '="Consultas abiertas: "&COUNTIF(tblConsulta[Producto],"?*")')
    tip = st(font_size=8.5, italic=True, font_color=C["muted"], bg_color=C["canvas"], indent=1, text_wrap=True)
    celda(ws, st, 5, 2, 6, "Elija el producto en la columna Producto de SU fila (Alt + ↓ o escriba para filtrar). "
                           "Para ver el historial completo use 10A/10B con una Vista de hoja (vea abajo).", tip)
    seccion(ctx, ws, 6, 1, "CONSULTA — una fila por persona (nadie comparte el selector)")
    tabla(ctx, ws, "tblConsulta", cols, HDR_C, MAX_CONS, badges=False)
    fin = st.cell("in", "text")
    for k in range(MAX_CONS):
        r0 = FIRST_C - 1 + k
        ws.set_row_pixels(r0, 24)
        v = inputs.get(k + 1)
        if v:
            ws.write_string(r0, 2, v, fin)
        else:
            ws.write_blank(r0, 2, None, fin)
    L = {x: chr(66 + i) for i, x in enumerate(CONS_COLS)}
    rng = lambda x: f"{L[x]}{FIRST_C}:{L[x]}{LAST_C}"  # noqa: E731
    ws.data_validation(rng("Producto"), {"validate": "list", "source": "=lstProductos",
                                         **msg("Producto a consultar", "Elija de la lista. Escriba parte del nombre o "
                                                                       "del SKU para filtrarla."),
                                         "error_title": "Producto no válido",
                                         "error_message": "Elija un producto de la lista del catálogo."})
    ws.unprotect_range(rng("Producto"), "Consulta", None)
    estado_cf(ws, st, rng("Estado"), f"${L['Estado']}{FIRST_C}")
    cob = f"${L['CoberturaDias']}{FIRST_C}"
    for crit, fg, bg in ((f"=AND(ISNUMBER({cob}),{cob}<3)", C["red"], C["red_lt"]),
                         (f"=AND(ISNUMBER({cob}),{cob}<7)", C["amber"], C["amber_lt"])):
        ws.conditional_format(rng("CoberturaDias"), {"type": "formula", "criteria": crit, "stop_if_true": True,
                                                     "format": st.cf(font_color=fg, bg_color=bg, bold=True)})
    correo = f"${L['Correo']}{FIRST_C}"
    ws.conditional_format(f"B{FIRST_C}:{L['Valor']}{LAST_C}", {"type": "formula", "criteria": f'={correo}=""',
                                                              "format": st.cf(bg_color="#F8FAFC",
                                                                              font_color=C["faint"])})
    ws.conditional_format(rng("Usuario"), {"type": "formula", "criteria": f'={correo}=""',
                                           "format": st.cf(num_format=';;;"(sin asignar)"')})
    ws.conditional_format(rng("Usuario"), {"type": "formula", "criteria": f'={correo}<>""',
                                           "format": st.cf(bold=True, font_color=C["brand_dk"])})
    for x in ("Disponible", "Mínimo", "Máximo", "Sugerido", "Entradas30d", "Salidas30d"):
        ref = f"{L[x]}{FIRST_C}"
        ws.conditional_format(rng(x), {"type": "formula", "criteria": f"=AND(ISNUMBER({ref}),{ref}<>INT({ref}))",
                                       "format": st.cf(num_format="#,##0.00")})
    # Guía para el historial completo (Vista de hoja: filtra sin afectar a los demás)
    r = LAST_C + 1
    ws.set_row_pixels(r, 14, canvas)
    seccion(ctx, ws, r + 1, 1, "HISTORIAL COMPLETO DE UN PRODUCTO (sin afectar a los demás)")
    pasos = [
        "1. Abra 10A_ENTRADAS (entradas y ajustes) o 10B_SALIDAS (salidas).",
        "2. Pestaña Vista › Vista de hoja › Nueva: su filtro solo lo ve usted (no cambia la vista de los demás).",
        "3. En la bitácora oficial, filtre la columna Producto por el producto y ordene por Fecha.",
        "4. Al terminar: Vista › Vista de hoja › Salir (o Predeterminada).",
    ]
    txt = st(font_size=9.5, indent=1, bg_color=C["white"], border=1, border_color=C["border"])
    for i, paso in enumerate(pasos):
        celda(ws, st, r + 2 + i, 1, 6, paso, txt)
        ws.set_row_pixels(r + 2 + i, 22)
    ws.freeze_panes(HDR_C, 3)
    ws.set_selection(f"C{FIRST_C}")
    ws.set_landscape()
    ws.fit_to_pages(1, 0)
