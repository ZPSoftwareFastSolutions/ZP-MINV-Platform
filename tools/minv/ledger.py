"""M-INV · Capa de escritura: 10_MOVIMIENTOS (bitácora append-only)."""
from __future__ import annotations

import datetime as dt

from .base import (C, FIRST, HDR, LAST_MOV, MAX_MOV, S_MOV, Col, Ctx, action_button, app_sheet, band, build_table,
                   chip, decimals_cf, legend_pills, nested_if, note, print_setup, status_cf, this_row, toolbar_row)

INPUTS = ("Fecha", "Tipo", "Categoría", "Producto", "Cantidad", "Documento", "Responsable", "Observaciones")


def movimientos_cols() -> list[Col]:
    r = this_row("tblMovimientos")
    llena = f'({r("Fecha")}&{r("Tipo")}&{r("Producto")}&{r("Cantidad")})=""'
    estado = nested_if([
        (llena, '""'),
        (f'OR({r("Fecha")}="",{r("Tipo")}="",{r("Producto")}="",{r("Cantidad")}="",{r("Responsable")}="")',
         '"⚠ Incompleto"'),
        (f'{r("Unidad")}="?"', '"✖ Producto no existe"'),
        (f'{r("FactorStock")}=0', '"✖ Tipo no válido"'),
        (f'NOT(ISNUMBER({r("Cantidad")}))', '"✖ Cantidad inválida"'),
        (f'{r("Cantidad")}<=0', '"✖ Cantidad inválida"'),
        (f'AND({r("Categoría")}<>"",{r("Categoría")}<>INDEX(tblProductos[Categoría],'
         f'MATCH({r("SKU")},tblProductos[SKU],0)))', '"⚠ Categoría no coincide"'),
        (f'{r("Saldo")}<0', '"✖ Stock insuficiente"'),
        (f'AND(LEFT({r("Tipo")},6)="AJUSTE",{r("Observaciones")}="")', '"⚠ Justifique el ajuste"'),
    ], '"✔ Registrado"')
    return [
        Col("ID", "calc", 48, "id", "Consecutivo automático del registro (auditoría). Aparece al diligenciar la fila.",
            formula=f'=IF({llena},"",ROW()-ROW(tblMovimientos[#Headers]))'),
        Col("Fecha", "in", 94, "date", "Fecha del movimiento (dd/mm/aaaa). Atajo: Ctrl + ; escribe la fecha de hoy."),
        Col("Tipo", "in", 118, "text", "ENTRADA, SALIDA, AJUSTE (+), AJUSTE (-) o SALDO INICIAL. El sistema asigna "
                                      "el FactorStock."),
        Col("Categoría", "in", 172, "text", "Paso 1 de la cascada: elija la categoría para filtrar los productos."),
        Col("Producto", "in", 280, "text", "Paso 2: elija el producto de la lista filtrada. Nunca lo escriba a mano."),
        Col("SKU", "calc", 80, "center", "Código extraído del producto elegido (clave de la proyección de stock).",
            formula=f'=IF({r("Producto")}="","",TRIM(LEFT({r("Producto")},FIND(" · ",{r("Producto")}&" · ")-1)))'),
        Col("Unidad", "calc", 78, "center", "Unidad de medida del producto (desde 05_PRODUCTOS). ? = no existe.",
            formula=f'=IF({r("SKU")}="","",IFERROR(INDEX(tblProductos[Unidad],MATCH({r("SKU")},tblProductos[SKU],0)),'
                    f'"?"))'),
        Col("Cantidad", "in", 86, "qty", "Cantidad siempre positiva, en la unidad indicada. El signo lo pone el "
                                        "FactorStock."),
        Col("FactorStock", "calc", 104, "factor", "+1 suma al inventario, -1 resta. Lo define el Tipo (tabla "
                                                  "tblTiposMov en 01_CONFIG).",
            formula=f'=IF({r("Tipo")}="","",IFERROR(INDEX(tblTiposMov[FactorStock],'
                    f'MATCH({r("Tipo")},tblTiposMov[Tipo],0)),0))'),
        Col("CantidadNeta", "calc", 114, "signed", "Cantidad × FactorStock. Es el único valor que suma la proyección "
                                                   "15_STOCK.",
            formula=f'=IF(OR({r("Cantidad")}="",{r("FactorStock")}=""),"",IF(ISNUMBER({r("Cantidad")}),'
                    f'{r("Cantidad")}*{r("FactorStock")},""))'),
        Col("Saldo", "calc", 82, "strong", "Saldo del producto después de este movimiento (kardex acumulado).",
            formula=f'=IF({r("CantidadNeta")}="","",SUMIFS(INDEX(tblMovimientos[CantidadNeta],1):{r("CantidadNeta")},'
                    f'INDEX(tblMovimientos[SKU],1):{r("SKU")},{r("SKU")}))'),
        Col("Estado", "calc", 172, "status", "Control automático del registro: ✔ correcto, ⚠ revisar, ✖ error que "
                                             "debe corregirse.", formula="=" + estado),
        Col("Documento", "in", 112, "text", "Soporte: factura, remisión u orden (opcional). Ej: FC-10234."),
        Col("Responsable", "in", 140, "text", "Quién registra el movimiento (lista de 01_CONFIG)."),
        Col("Observaciones", "in", 250, "text", "Detalle adicional. Obligatorio en AJUSTES: explique el motivo."),
    ]


def build_movimientos(ctx: Ctx, movs):
    ws, st = ctx.sheets[S_MOV], ctx.st
    cols = ctx.tables["tblMovimientos"]
    widths = [16] + [c.width for c in cols] + [16]
    app_sheet(ws, widths, zoom=90)
    band(ctx, ws, "Bitácora de movimientos",
         "Capa de escritura · Registro inmutable (append-only) de entradas, salidas y ajustes",
         "bitacora", widths=widths)
    toolbar_row(ws, st)
    build_table(ctx, ws, "tblMovimientos", cols, MAX_MOV)
    L = {c.name: ctx.col("tblMovimientos", c.name) for c in cols}
    rng = lambda n: f"{L[n]}{FIRST}:{L[n]}{LAST_MOV}"  # noqa: E731

    ws.data_validation(rng("Fecha"), {
        "validate": "date", "criteria": "between", "minimum": "=cfgFechaMin", "maximum": "=TODAY()",
        "input_title": "Fecha del movimiento",
        "input_message": "Escriba la fecha (dd/mm/aaaa). Atajo: Ctrl + ; inserta la fecha de hoy. No se admiten "
                         "fechas futuras.",
        "error_title": "Fecha no válida",
        "error_message": "Use el formato dd/mm/aaaa. No se permiten fechas futuras ni anteriores a la fecha mínima "
                         "configurada."})
    ws.data_validation(rng("Tipo"), {
        "validate": "list", "source": "=lstTiposMov",
        "input_title": "Tipo de movimiento",
        "input_message": "ENTRADA (+) · SALIDA (-) · AJUSTE (+/-) · SALDO INICIAL (+). El FactorStock se asigna solo.",
        "error_title": "Tipo no válido", "error_message": "Seleccione un tipo de la lista desplegable (Alt + ↓)."})
    ws.data_validation(rng("Categoría"), {
        "validate": "list", "source": "=lstCategorias",
        "input_title": "Paso 1 · Categoría",
        "input_message": "Elija la categoría. La lista de Producto se filtrará con esta selección.",
        "error_title": "Categoría no válida", "error_message": "Seleccione una categoría de la lista (Alt + ↓)."})
    cat = f"${L['Categoría']}{FIRST}"
    ws.data_validation(rng("Producto"), {
        "validate": "list",
        "source": f'=IF({cat}="",lpTodos,IF(COUNTIF(lpCat,{cat})=0,lpVacio,'
                  f'OFFSET(lpBase,MATCH({cat},lpCat,0),0,COUNTIF(lpCat,{cat}),1)))',
        "input_title": "Paso 2 · Producto",
        "input_message": "Elija el producto de la lista (filtrada por la categoría). Formato: SKU · Nombre. Nunca lo "
                         "escriba a mano.",
        "error_title": "Producto no válido",
        "error_message": "Seleccione un producto de la lista. Si no existe, pida al administrador crearlo en "
                         "05_PRODUCTOS."})
    qc, uc = f"{L['Cantidad']}{FIRST}", f"${L['Unidad']}{FIRST}"
    ws.data_validation(rng("Cantidad"), {
        "validate": "custom",
        "value": f'=AND(ISNUMBER({qc}),{qc}>0,IFERROR(OR(INDEX(lstUniDec,MATCH({uc},lstUniCod,0))="SI",'
                 f'{qc}=INT({qc})),TRUE))',
        "input_title": "Cantidad",
        "input_message": "Número mayor que 0, en la unidad indicada. UND, CAJA, PAQ, PAR y ROLLO solo admiten "
                         "enteros.",
        "error_title": "Cantidad no válida",
        "error_message": "La cantidad debe ser mayor que 0. Esta unidad no admite decimales o el valor no es un "
                         "número."})
    ws.data_validation(rng("Documento"), {
        "validate": "length", "criteria": "<=", "value": 30,
        "input_title": "Documento soporte (opcional)", "input_message": "Factura, remisión u orden. Ej: FC-10234.",
        "error_title": "Texto muy largo", "error_message": "Máximo 30 caracteres."})
    ws.data_validation(rng("Responsable"), {
        "validate": "list", "source": "=lstResponsables",
        "input_title": "Responsable", "input_message": "Seleccione quién registra el movimiento.",
        "error_title": "Responsable no válido", "error_message": "Seleccione un nombre de la lista (Alt + ↓)."})
    ws.data_validation(rng("Observaciones"), {
        "validate": "length", "criteria": "<=", "value": 250,
        "input_title": "Observaciones", "input_message": "Obligatorio en AJUSTES: explique el motivo (merma, conteo...).",
        "error_title": "Texto muy largo", "error_message": "Máximo 250 caracteres."})

    # Formato condicional
    b = FIRST
    ins = " ".join(f"{L[n]}{b}:{L[n]}{LAST_MOV}" for n in INPUTS)
    first_in = f"{L['Fecha']}{b}:{L['Fecha']}{LAST_MOV}"
    # Filas selladas por la edición Plus: se ven como celdas de solo lectura (gris)
    ws.conditional_format(first_in, {
        "type": "formula",
        "criteria": f'=AND(ISNUMBER(${L["ID"]}{b}),${L["ID"]}{b}<=cfgSelladoHasta,LEFT(${L["Estado"]}{b},1)="✔")',
        "format": st.cf(bg_color=C["calc_fill"], border=1, border_color=C["calc_border"]), "multi_range": ins})
    empty = (f'AND(${L["Fecha"]}{b}="",${L["Tipo"]}{b}="",${L["Producto"]}{b}="",${L["Cantidad"]}{b}="",'
             f'${L["ID"]}{b - 1}<>"")')
    ws.conditional_format(first_in, {"type": "formula", "criteria": "=" + empty,
                                     "format": st.cf(bg_color=C["brand_lt"]), "multi_range": ins})
    fac = f"${L['FactorStock']}{b}"
    for n in ("Tipo", "FactorStock", "CantidadNeta"):
        ws.conditional_format(rng(n), {"type": "formula", "criteria": f"={fac}=1",
                                       "format": st.cf(font_color=C["green"])})
        ws.conditional_format(rng(n), {"type": "formula", "criteria": f"={fac}=-1",
                                       "format": st.cf(font_color=C["red"])})
    decimals_cf(ws, st, rng("Cantidad"), f"{L['Cantidad']}{b}", "#,##0.00")
    decimals_cf(ws, st, rng("Saldo"), f"{L['Saldo']}{b}", "#,##0.00")
    decimals_cf(ws, st, rng("CantidadNeta"), f"{L['CantidadNeta']}{b}", "+#,##0.00;-#,##0.00;0")
    ws.conditional_format(rng("Saldo"), {"type": "formula",
                                         "criteria": f"=AND(ISNUMBER(${L['Saldo']}{b}),${L['Saldo']}{b}<0)",
                                         "format": st.cf(font_color=C["red"], bold=True)})
    status_cf(ws, st, rng("Estado"), f"${L['Estado']}{b}")

    # Barra de herramientas
    chip(ws, 16, 240, textlink=ctx.kref("txtRegistros"), desc="Contador de registros")
    label = "＋  Registrar con el formulario" if ctx.plus else "＋  Registrar nuevo movimiento"
    action_button(ws, label, 266, 250, url="internal:irRegistrar",
                  tip="Abrir el formulario guiado" if ctx.plus else "Ir a la siguiente fila libre de la bitácora")
    x = legend_pills(ws, 532)
    msg = ("Filas en gris: selladas (no editables). Doble clic en una fila: consulta del producto. Los errores se "
           "corrigen con un AJUSTE." if ctx.plus else
           "Registro inmutable: nunca borre filas. Los errores se corrigen con un AJUSTE y su observación.")
    note(ws, msg, x + 16, 660)

    fmts = {c.name: st.cell("in", c.fmt) for c in cols if c.kind == "in"}
    idx = {c.name: 1 + i for i, c in enumerate(cols)}
    for i, m in enumerate(movs):
        r = FIRST - 1 + i
        ws.write_datetime(r, idx["Fecha"], dt.datetime.combine(m.fecha, dt.time()), fmts["Fecha"])
        ws.write_string(r, idx["Tipo"], m.tipo, fmts["Tipo"])
        ws.write_string(r, idx["Categoría"], m.categoria, fmts["Categoría"])
        ws.write_string(r, idx["Producto"], m.producto, fmts["Producto"])
        ws.write_number(r, idx["Cantidad"], m.cantidad, fmts["Cantidad"])
        if m.documento:
            ws.write_string(r, idx["Documento"], m.documento, fmts["Documento"])
        ws.write_string(r, idx["Responsable"], m.responsable, fmts["Responsable"])
        if m.observaciones:
            ws.write_string(r, idx["Observaciones"], m.observaciones, fmts["Observaciones"])
    nxt = FIRST - 1 + len(movs)
    ws.freeze_panes(HDR, 0, max(HDR, nxt - 14), 0)
    ws.set_selection(nxt, 2, nxt, 2)
    print_setup(ws, "Bitácora de movimientos")
