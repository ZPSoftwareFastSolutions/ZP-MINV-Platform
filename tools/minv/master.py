"""M-INV · Datos maestros: 04_PROVEEDORES y 05_PRODUCTOS."""
from __future__ import annotations

import demo_data as D

from .base import (A_ACCIONABLES, C, FIRST, HDR, LAST_PROD, LAST_PROV, MAX_PROD, MAX_PROV, S_PROD, S_PROV, Col, Ctx,
                   action_button, app_sheet, band, build_table, chip, decimals_cf, legend_pills, nested_if, note,
                   print_setup, q, status_cf, this_row, toolbar_row)


def next_row_cf(ws, st, rng, key_cols, prev_ref):
    """Resalta la siguiente fila libre (guía visual para el operador)."""
    empty = ",".join(f'{c}=""' for c in key_cols)
    ws.conditional_format(rng, {"type": "formula", "criteria": f'=AND({empty},{prev_ref}<>"")',
                                "format": st.cf(bg_color=C["brand_lt"])})


# ---------------------------------------------------------------------------
# 04_PROVEEDORES
# ---------------------------------------------------------------------------
def proveedores_cols() -> list[Col]:
    r = this_row("tblProveedores")
    valid = nested_if([
        (f'AND({r("Proveedor")}="",{r("NIT")}="",{r("Contacto")}="",{r("Correo")}="")', '""'),
        (f'{r("Proveedor")}=""', '"⚠ Falta el nombre"'),
        (f'COUNTIF(tblProveedores[Proveedor],{r("Proveedor")})>1', '"✖ Proveedor duplicado"'),
        (f'AND({r("Correo")}<>"",ISERROR(FIND("@",{r("Correo")})))', '"⚠ Correo no válido"'),
    ], '"✔ OK"')
    return [
        Col("Proveedor", "in", 240, "text", "Razón social o nombre comercial (único). Aparece en el catálogo y en el "
                                           "pedido sugerido."),
        Col("NIT", "in", 130, "center", "Identificación tributaria (opcional)."),
        Col("Contacto", "in", 170, "text", "Persona de contacto para pedidos."),
        Col("Teléfono", "in", 124, "center", "Teléfono o celular del contacto."),
        Col("Correo", "in", 250, "text", "Correo para enviar el pedido sugerido."),
        Col("DiasEntrega", "in", 104, "qty", "Días que tarda en entregar desde el pedido (lead time). Se usa para la "
                                             "fecha estimada de llegada."),
        Col("Productos", "calc", 100, "center", "Productos del catálogo asignados a este proveedor.",
            formula=f'=IF({r("Proveedor")}="","",COUNTIF(tblProductos[Proveedor],{r("Proveedor")}))'),
        Col("EnAlerta", "calc", 100, "center", "Productos de este proveedor que requieren reposición.",
            formula=f'=IF({r("Proveedor")}="","",SUMPRODUCT(COUNTIFS(tblStock[Proveedor],{r("Proveedor")},'
                    f'tblStock[Estado],{A_ACCIONABLES})))'),
        Col("Validación", "calc", 190, "status", "Control de calidad del registro.", formula="=" + valid),
    ]


def build_proveedores(ctx: Ctx):
    ws, st = ctx.sheets[S_PROV], ctx.st
    cols = proveedores_cols()
    widths = [16] + [c.width for c in cols] + [16]
    app_sheet(ws, widths, zoom=90)
    band(ctx, ws, "Proveedores", "Datos maestros · Quién le vende cada producto y en cuántos días entrega",
         None, widths=widths)
    toolbar_row(ws, st)
    build_table(ctx, ws, "tblProveedores", cols, MAX_PROV)
    L = {c.name: ctx.col("tblProveedores", c.name) for c in cols}
    rng = lambda n: f"{L[n]}{FIRST}:{L[n]}{LAST_PROV}"  # noqa: E731
    b = L["Proveedor"]
    ws.data_validation(rng("Proveedor"), {
        "validate": "custom",
        "value": f'=AND(LEN({b}{FIRST})>=3,LEN({b}{FIRST})<=60,COUNTIF(${b}${FIRST}:${b}${LAST_PROV},{b}{FIRST})=1)',
        "input_title": "Proveedor", "input_message": "Nombre único de 3 a 60 caracteres.",
        "error_title": "Proveedor no válido",
        "error_message": "El nombre debe tener entre 3 y 60 caracteres y no puede repetirse."})
    for n, mx in (("NIT", 20), ("Contacto", 60), ("Teléfono", 20)):
        ws.data_validation(rng(n), {"validate": "length", "criteria": "<=", "value": mx,
                                    "input_title": n, "input_message": f"Máximo {mx} caracteres.",
                                    "error_title": "Texto muy largo", "error_message": f"Máximo {mx} caracteres."})
    e = L["Correo"]
    ws.data_validation(rng("Correo"), {
        "validate": "custom",
        "value": f'=AND(ISNUMBER(FIND("@",{e}{FIRST})),ISERROR(FIND(" ",{e}{FIRST})),LEN({e}{FIRST})<=80)',
        "input_title": "Correo", "input_message": "Ej: pedidos@proveedor.com",
        "error_title": "Correo no válido", "error_message": "Escriba un correo válido, sin espacios."})
    ws.data_validation(rng("DiasEntrega"), {
        "validate": "integer", "criteria": "between", "minimum": 0, "maximum": 365,
        "input_title": "Días de entrega", "input_message": "Número entero de días (0 a 365).",
        "error_title": "Valor no válido", "error_message": "Escriba un número entero entre 0 y 365."})
    next_row_cf(ws, st, f"{L['Proveedor']}{FIRST}:{L['DiasEntrega']}{LAST_PROV}",
                [f"${L['Proveedor']}{FIRST}"], f"${L['Proveedor']}{FIRST - 1}")
    status_cf(ws, st, rng("Validación"), f"${L['Validación']}{FIRST}")
    ws.conditional_format(rng("EnAlerta"), {"type": "cell", "criteria": ">", "value": 0,
                                            "format": st.cf(font_color=C["red"], bold=True)})

    chip(ws, 16, 190, textlink=ctx.kref("txtProveedores"), desc="Contador de proveedores")
    action_button(ws, "＋  Registrar nuevo proveedor", 216, 240, url="internal:irFilaLibreProv",
                  tip="Ir a la siguiente fila libre")
    x = legend_pills(ws, 472)
    note(ws, "Asigne un proveedor a cada producto en 05_PRODUCTOS para agrupar el pedido sugerido.", x + 16, 560)

    if ctx.demo:
        fmts = {c.name: ctx.st.cell("in", c.fmt) for c in cols if c.kind == "in"}
        for i, (nombre, nit, contacto, tel, correo, dias) in enumerate(D.PROVEEDORES_DEMO):
            r = FIRST - 1 + i
            ws.write_string(r, 1, nombre, fmts["Proveedor"])
            ws.write_string(r, 2, nit, fmts["NIT"])
            ws.write_string(r, 3, contacto, fmts["Contacto"])
            ws.write_string(r, 4, tel, fmts["Teléfono"])
            ws.write_string(r, 5, correo, fmts["Correo"])
            ws.write_number(r, 6, dias, fmts["DiasEntrega"])
    n = len(D.PROVEEDORES_DEMO) if ctx.demo else 0
    ws.freeze_panes(HDR, 0)
    ws.set_selection(FIRST - 1 + n, 1, FIRST - 1 + n, 1)
    print_setup(ws, "Proveedores")


# ---------------------------------------------------------------------------
# 05_PRODUCTOS
# ---------------------------------------------------------------------------
def productos_cols() -> list[Col]:
    r = this_row("tblProductos")
    valid = nested_if([
        (f'AND({r("SKU")}="",{r("Producto")}="")', '""'),
        (f'OR({r("SKU")}="",{r("Producto")}="",{r("Categoría")}="",{r("Unidad")}="")', '"⚠ Faltan datos"'),
        (f'COUNTIF(tblProductos[SKU],{r("SKU")})>1', '"✖ SKU duplicado"'),
        (f'ISNUMBER(FIND(" ",{r("SKU")}))', '"✖ SKU con espacios"'),
        (f'ISNA(MATCH({r("Categoría")},lstCategorias,0))', '"✖ Categoría no existe"'),
        (f'ISNA(MATCH({r("Unidad")},lstUniCod,0))', '"✖ Unidad no existe"'),
        (f'AND({r("Proveedor")}<>"",ISNA(MATCH({r("Proveedor")},tblProveedores[Proveedor],0)))',
         '"✖ Proveedor no existe"'),
        (f'AND(N({r("StockMax")})>0,N({r("StockMin")})>N({r("StockMax")}))', '"⚠ Mínimo mayor que máximo"'),
        (f'{r("Activo")}="NO"', '"● Inactivo"'),
    ], '"✔ OK"')
    return [
        Col("SKU", "in", 92, "text", "Código único del producto, sin espacios (ej: FER-001). No lo cambie si ya "
                                     "tiene movimientos: cree uno nuevo y desactive el anterior."),
        Col("Producto", "in", 290, "text", "Nombre descriptivo (3 a 60 caracteres). Es lo que el operador verá en "
                                          "la lista junto al SKU."),
        Col("Categoría", "in", 168, "text", "Seleccione la categoría (Alt + ↓). Las categorías se administran en "
                                           "03_CATEGORIAS."),
        Col("Unidad", "in", 84, "center", "Unidad de medida (UND, CAJA, KG...). Define si se admiten decimales."),
        Col("StockMin", "in", 96, "qty", "Punto de reorden. En o por debajo de este valor el producto queda "
                                        "CRÍTICO."),
        Col("StockMax", "in", 96, "qty", "Nivel máximo deseado. Por encima: SOBRESTOCK. 0 = sin máximo."),
        Col("CostoUnitario", "in", 120, "money", "Costo por unidad (moneda local). Se usa para valorizar el "
                                                "inventario y el pedido sugerido."),
        Col("Proveedor", "in", 210, "text", "Proveedor habitual (lista de 04_PROVEEDORES). Agrupa el pedido "
                                           "sugerido."),
        Col("Ubicación", "in", 96, "center", "Ubicación física en bodega (opcional). Ej: A-01-03."),
        Col("Activo", "in", 66, "center", "Escriba NO para descontinuar: desaparece de las listas pero conserva su "
                                         "historial. Vacío = SI."),
        Col("Etiqueta", "calc", 300, "text", "Texto de las listas desplegables: SKU · Producto.",
            formula=f'=IF(OR({r("SKU")}="",{r("Producto")}=""),"",{r("SKU")}&" · "&{r("Producto")})'),
        Col("Movimientos", "calc", 112, "center", "Cantidad de registros de este SKU en la bitácora.",
            formula=f'=IF({r("SKU")}="","",COUNTIF(tblMovimientos[SKU],{r("SKU")}))'),
        Col("Validación", "calc", 180, "status", "Control de calidad del registro maestro.", formula="=" + valid),
    ]


def build_productos(ctx: Ctx):
    ws, st = ctx.sheets[S_PROD], ctx.st
    cols = productos_cols()
    widths = [16] + [c.width for c in cols] + [16]
    app_sheet(ws, widths, zoom=90)
    band(ctx, ws, "Catálogo de productos",
         "Datos maestros · Cada producto se registra una sola vez · Nunca borre filas (use Activo = NO)",
         None, widths=widths)
    toolbar_row(ws, st)
    build_table(ctx, ws, "tblProductos", cols, MAX_PROD)
    L = {c.name: ctx.col("tblProductos", c.name) for c in cols}
    rng = lambda n: f"{L[n]}{FIRST}:{L[n]}{LAST_PROD}"  # noqa: E731

    s = L["SKU"]
    ws.data_validation(rng("SKU"), {
        "validate": "custom",
        "value": f'=AND(LEN({s}{FIRST})>=2,LEN({s}{FIRST})<=20,ISERROR(FIND(" ",{s}{FIRST})),'
                 f'COUNTIF(${s}${FIRST}:${s}${LAST_PROD},{s}{FIRST})=1)',
        "input_title": "SKU (código único)", "input_message": "2 a 20 caracteres, sin espacios, sin repetir. Ej: FER-001.",
        "error_title": "SKU no válido", "error_message": "El SKU debe tener 2 a 20 caracteres, no llevar espacios y no "
                                                        "existir ya en el catálogo."})
    ws.data_validation(rng("Producto"), {
        "validate": "length", "criteria": "between", "minimum": 3, "maximum": 60,
        "input_title": "Nombre del producto", "input_message": "Descripción clara, de 3 a 60 caracteres.",
        "error_title": "Nombre no válido", "error_message": "El nombre debe tener entre 3 y 60 caracteres."})
    ws.data_validation(rng("Categoría"), {
        "validate": "list", "source": "=lstCategorias",
        "input_title": "Categoría", "input_message": "Seleccione de la lista (Alt + ↓).",
        "error_title": "Categoría no válida", "error_message": "Elija una categoría de la lista. Para crear una nueva "
                                                             "contacte al administrador."})
    ws.data_validation(rng("Unidad"), {
        "validate": "list", "source": "=lstUniCod",
        "input_title": "Unidad de medida", "input_message": "Seleccione de la lista (Alt + ↓).",
        "error_title": "Unidad no válida", "error_message": "Elija una unidad de la lista."})
    for n in ("StockMin", "StockMax", "CostoUnitario"):
        ws.data_validation(rng(n), {
            "validate": "decimal", "criteria": ">=", "value": 0,
            "input_title": n, "input_message": "Número mayor o igual a 0.",
            "error_title": "Valor no válido", "error_message": "Escriba un número mayor o igual a 0."})
    ws.data_validation(rng("Proveedor"), {
        "validate": "list", "source": "=lstProveedores",
        "input_title": "Proveedor", "input_message": "Seleccione de la lista (Alt + ↓). Se administran en "
                                                     "04_PROVEEDORES.",
        "error_title": "Proveedor no válido", "error_message": "Elija un proveedor de la lista o regístrelo primero "
                                                              "en 04_PROVEEDORES."})
    ws.data_validation(rng("Ubicación"), {
        "validate": "length", "criteria": "<=", "value": 20,
        "input_title": "Ubicación (opcional)", "input_message": "Pasillo-estante-nivel. Máximo 20 caracteres.",
        "error_title": "Texto muy largo", "error_message": "Máximo 20 caracteres."})
    ws.data_validation(rng("Activo"), {
        "validate": "list", "source": ["SI", "NO"],
        "input_title": "¿Producto activo?", "input_message": "NO = descontinuado (sale de las listas). Vacío = SI.",
        "error_title": "Valor no válido", "error_message": "Escriba SI o NO."})

    next_row_cf(ws, st, f"{L['SKU']}{FIRST}:{L['Activo']}{LAST_PROD}",
                [f"${L['SKU']}{FIRST}", f"${L['Producto']}{FIRST}"], f"${L['SKU']}{FIRST - 1}")
    ws.conditional_format(f"{L['SKU']}{FIRST}:{L['Etiqueta']}{LAST_PROD}", {
        "type": "formula", "criteria": f'=${L["Activo"]}{FIRST}="NO"',
        "format": st.cf(font_color=C["faint"], italic=True)})
    status_cf(ws, st, rng("Validación"), f"${L['Validación']}{FIRST}")
    for n in ("StockMin", "StockMax"):
        decimals_cf(ws, st, rng(n), f"{L[n]}{FIRST}", "#,##0.00")

    chip(ws, 16, 210, textlink=ctx.kref("txtProductos"), desc="Contador de productos")
    action_button(ws, "＋  Registrar nuevo producto", 236, 230, url="internal:irFilaLibreProd",
                  tip="Ir a la siguiente fila libre del catálogo")
    action_button(ws, "Proveedores  ➜", 476, 150, url=f"internal:{q(S_PROV)}!A1", fill=C["slate"],
                  tip="Administrar proveedores")
    x = legend_pills(ws, 642)
    note(ws, "Para descontinuar escriba NO en Activo. Nunca borre filas ni cambie un SKU con movimientos.",
         x + 16, 560)

    if ctx.demo:
        fmts = {c.name: st.cell("in", c.fmt) for c in cols if c.kind == "in"}
        for i, p in enumerate(D.PRODUCTOS_DEMO):
            r = FIRST - 1 + i
            ws.write_string(r, 1, p.sku, fmts["SKU"])
            ws.write_string(r, 2, p.nombre, fmts["Producto"])
            ws.write_string(r, 3, p.categoria, fmts["Categoría"])
            ws.write_string(r, 4, p.unidad, fmts["Unidad"])
            ws.write_number(r, 5, p.minimo, fmts["StockMin"])
            ws.write_number(r, 6, p.maximo, fmts["StockMax"])
            ws.write_number(r, 7, p.costo, fmts["CostoUnitario"])
            ws.write_string(r, 8, p.proveedor, fmts["Proveedor"])
            ws.write_string(r, 9, p.ubicacion, fmts["Ubicación"])
            ws.write_string(r, 10, "SI" if p.activo else "NO", fmts["Activo"])
    n = len(D.PRODUCTOS_DEMO) if ctx.demo else 0
    ws.freeze_panes(HDR, 0)
    ws.set_selection(FIRST - 1 + n, 1, FIRST - 1 + n, 1)
    print_setup(ws, "Catálogo de productos")
