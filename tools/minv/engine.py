"""
M-INV · Motor interno (capa oculta):
  90_LISTAS  listas en cascada, listas de búsqueda por texto (formulario y consulta) y listas de proveedores.
  91_KPIS    indicadores, textos dinámicos, asistente «próximo paso», series de gráficos, rankings y ayudantes.
También define los nombres de navegación dinámica (ir*).
"""
from __future__ import annotations

from xlsxwriter.utility import xl_col_to_name

from .base import (A_ALERTAS, A_REPONER, C, EST, FIRST, HDR, LAST_MOV, LAST_PROD, LAST_PROV, MAX_PROD, MAX_PROV,
                   S_CONTEO, S_KARDEX, S_KPI, S_LISTAS, S_MOV, S_PEDIDO, S_PROD, S_REG, S_STOCK, Ctx,
                   estado_formula, q)
from .config_sheets import config_band
from .kardex import KX_FIRST, KX_LAST
from .pedido import PD_FIRST, PD_LAST

LAYOUT = {
    "rank_alertas": {"k": "O", "key": "P", "fila": "Q"},
    "rank_pedido": {"k": "R", "key": "S", "fila": "T"},
    "kardex_chart": ("W", "X", FIRST, FIRST + 99),
}


def fecha_txt(d: str) -> str:
    """Fecha como texto dd/mm/aaaa sin TEXTO() (independiente del idioma de Excel)."""
    return f'RIGHT("0"&DAY({d}),2)&"/"&RIGHT("0"&MONTH({d}),2)&"/"&YEAR({d})'


# ---------------------------------------------------------------------------
# Indicadores (nombres kpi*/txt*/frm*/kx*/pd*) — una fila por nombre en 91_KPIS, columna C
# ---------------------------------------------------------------------------
def kpi_specs(ctx: Ctx) -> list[tuple[str, str, str, str]]:
    PB = f"{q(S_PEDIDO)}!$B${PD_FIRST}:$B${PD_LAST}"
    PC = f"{q(S_PEDIDO)}!$C${PD_FIRST}:$C${PD_LAST}"
    PM = f"{q(S_PEDIDO)}!$M${PD_FIRST}:$M${PD_LAST}"
    KXID = f"{q(S_KARDEX)}!$B${KX_FIRST}:$B${KX_LAST}"
    mes0 = "DATE(YEAR(TODAY()),MONTH(TODAY()),1)"
    mes1 = "DATE(YEAR(TODAY()),MONTH(TODAY())+1,1)"
    en_mes = f'tblMovimientos[Fecha],">="&{mes0},tblMovimientos[Fecha],"<"&{mes1}'
    rot = 'tblStock[DiasSinMov],">"&cfgDiasSinRotacion,tblStock[Activo],"SI",tblStock[StockActual],">0"'
    conteo_msg = ("genere los ajustes para cerrarlo" if ctx.plus else
                  "registre los ajustes sugeridos y borre el conteo")
    s = [
        # --- inventario
        ("kpiCatalogo", "Productos en catálogo", '=COUNTIF(tblStock[SKU],"?*")', "#,##0"),
        ("kpiActivos", "Productos activos", '=COUNTIFS(tblStock[SKU],"?*",tblStock[Activo],"SI")', "#,##0"),
        ("kpiInconsistentes", "Inconsistentes", '=COUNTIF(tblStock[Estado],"INCONSISTENTE")', "#,##0"),
        ("kpiAgotados", "Agotados", '=COUNTIF(tblStock[Estado],"AGOTADO")', "#,##0"),
        ("kpiCriticos", "Críticos", '=COUNTIF(tblStock[Estado],"CRÍTICO")', "#,##0"),
        ("kpiBajos", "Bajos (preventivos)", '=COUNTIF(tblStock[Estado],"BAJO")', "#,##0"),
        ("kpiOptimos", "Óptimos", '=COUNTIF(tblStock[Estado],"ÓPTIMO")', "#,##0"),
        ("kpiSobrestock", "Sobrestock", '=COUNTIF(tblStock[Estado],"SOBRESTOCK")', "#,##0"),
        ("kpiInactivos", "Inactivos", '=COUNTIF(tblStock[Estado],"INACTIVO")', "#,##0"),
        ("kpiEnAlerta", "En alerta (requieren acción)",
         '=kpiInconsistentes+kpiAgotados+kpiCriticos+kpiBajos', "#,##0"),
        ("kpiValor", "Valor del inventario", "=SUM(tblStock[ValorInventario])", "$ #,##0"),
        ("kpiSinRotacion", "Productos sin rotación", f"=COUNTIFS({rot})", "#,##0"),
        ("kpiValorSinRot", "Valor inmovilizado", f"=SUMIFS(tblStock[ValorInventario],{rot})", "$ #,##0"),
        ("kpiProveedores", "Proveedores registrados", '=COUNTIF(tblProveedores[Proveedor],"?*")', "#,##0"),
        ("kpiCategorias", "Categorías", '=COUNTIF(tblCategorias[Categoría],"?*")', "#,##0"),
        # --- bitácora
        ("kpiRegistros", "Registros en la bitácora", "=COUNT(tblMovimientos[ID])", "#,##0"),
        ("kpiCapacidad", "Capacidad de la bitácora", "=ROWS(tblMovimientos[ID])", "#,##0"),
        ("kpiUltimoID", "Último ID registrado", "=MAX(tblMovimientos[ID])", "#,##0"),
        ("kpiFilaLibreMov", "Fila libre en bitácora",
         "=ROW(tblMovimientos[#Headers])+MIN(kpiUltimoID+1,kpiCapacidad)", "0"),
        ("kpiFilaLibreProd", "Fila libre en catálogo",
         '=ROW(tblProductos[#Headers])+MIN(IFERROR(LOOKUP(2,1/(tblProductos[SKU]<>""),'
         'ROW(tblProductos[SKU])-ROW(tblProductos[#Headers])),0)+1,ROWS(tblProductos[SKU]))', "0"),
        ("kpiFilaLibreProv", "Fila libre en proveedores",
         '=ROW(tblProveedores[#Headers])+MIN(IFERROR(LOOKUP(2,1/(tblProveedores[Proveedor]<>""),'
         'ROW(tblProveedores[Proveedor])-ROW(tblProveedores[#Headers])),0)+1,ROWS(tblProveedores[Proveedor]))', "0"),
        ("kpiMovMes", "Movimientos del mes", f"=COUNTIFS({en_mes})", "#,##0"),
        ("kpiIngMes", "Ingresos del mes (sin saldo inicial)",
         f'=COUNTIFS({en_mes},tblMovimientos[FactorStock],1,tblMovimientos[Tipo],"<>SALDO INICIAL")', "#,##0"),
        ("kpiEgrMes", "Egresos del mes", f"=COUNTIFS({en_mes},tblMovimientos[FactorStock],-1)", "#,##0"),
        ("kpiUltFecha", "Fecha del último movimiento",
         '=IF(COUNT(tblMovimientos[Fecha])=0,"",MAX(tblMovimientos[Fecha]))', "dd/mm/yyyy"),
        ("kpiErrores", "Registros con error (✖)", '=COUNTIF(tblMovimientos[Estado],"✖*")', "#,##0"),
        ("kpiAdvertencias", "Registros por revisar (⚠)", '=COUNTIF(tblMovimientos[Estado],"⚠*")', "#,##0"),
        ("kpiConSaldoInicial", "Productos con SALDO INICIAL",
         '=SUMPRODUCT((tblStock[SKU]<>"")*(tblStock[Activo]="SI")*(COUNTIFS(tblMovimientos[SKU],tblStock[SKU]&"",'
         'tblMovimientos[Tipo],"SALDO INICIAL")>0))', "#,##0"),
        ("kpiOperativos", "Movimientos operativos (sin saldo inicial)",
         '=COUNTIFS(tblMovimientos[ID],">0",tblMovimientos[Tipo],"<>SALDO INICIAL")', "#,##0"),
        # --- toma física
        ("kpiConteoContados", "Productos contados", "=COUNT(tblConteo[Conteo])", "#,##0"),
        ("kpiConteoDif", "Productos con diferencia",
         '=COUNTIF(tblConteo[Diferencia],">0")+COUNTIF(tblConteo[Diferencia],"<0")', "#,##0"),
        ("kpiConteoValor", "Valor neto de diferencias", "=SUM(tblConteo[ValorDiferencia])", "$ #,##0"),
        # --- pedido sugerido
        ("pdProvFila", "Fila del proveedor filtrado",
         '=IFERROR(MATCH(pdProveedor,tblProveedores[Proveedor],0),"")', "0"),
        ("kpiPedidoLineas", "Líneas del pedido", f"=COUNT({PB})", "#,##0"),
        ("kpiPedidoTotal", "Total estimado del pedido", f"=SUM({PM})", "$ #,##0"),
        ("kpiPedidoProv", "Proveedores en el pedido", f'=SUMPRODUCT(({PC}<>"")/COUNTIF({PC},{PC}&""))', "#,##0"),
        # --- consulta (kardex)
        ("kxSKU", "SKU consultado",
         '=IF(kxProducto="","",TRIM(LEFT(kxProducto,FIND(" · ",kxProducto&" · ")-1)))', "@"),
        ("kxFila", "Fila del SKU en 15_STOCK", '=IF(kxSKU="","",IFERROR(MATCH(kxSKU,tblStock[SKU],0),""))', "0"),
        ("kxExiste", "¿Existe el SKU?", "=ISNUMBER(kxFila)", "General"),
        ("kxN", "Movimientos del SKU", '=IF(kxSKU="",0,COUNTIF(tblMovimientos[SKU],kxSKU))', "#,##0"),
        ("kxStock", "Stock del SKU (para formatos condicionales)",
         "=IF(kxExiste,N(INDEX(tblStock[StockActual],kxFila)),0)", "#,##0.##"),
        ("kxMostrados", "Movimientos mostrados", f"=COUNT({KXID})", "#,##0"),
        ("kxCoinciden", "Productos que coinciden", f"=COUNT({q(S_LISTAS)}!$O${FIRST}:$O${LAST_PROD})", "#,##0"),
        # --- asistente «próximo paso»
        ("kpiPaso", "Próximo paso (código)", "=" + _nested([
            ("kpiErrores>0", 1), ('cfgEmpresa="NOMBRE DE SU EMPRESA"', 2), ("kpiCatalogo=0", 3),
            ("kpiRegistros=0", 4), ("kpiAdvertencias>0", 5), ("kpiConteoContados>0", 6), ("kpiAgotados>0", 7),
            ("kpiEnAlerta>0", 8), ("kpiSinRotacion>0", 9)], 10), "0"),
        ("kpiPasoNivel", "Nivel (1 rojo, 2 ámbar, 3 azul, 4 verde)", "=CHOOSE(kpiPaso,1,3,3,3,2,2,1,2,3,4)", "0"),
        ("txtPaso", "Mensaje del próximo paso", "=CHOOSE(kpiPaso," + ",".join([
            '"✖  "&kpiErrores&" registro(s) con error en la bitácora: corríjalos con un AJUSTE justificado."',
            '"①  Primeros pasos: el administrador debe personalizar el nombre de la empresa y las categorías."',
            '"②  Registre sus productos en el Catálogo (SKU, nombre, categoría, unidad, mínimos y proveedor)."',
            '"③  Cargue el SALDO INICIAL de cada producto (conteo físico de arranque) para activar el tablero."',
            '"⚠  "&kpiAdvertencias&" registro(s) incompletos en la bitácora: complételos."',
            f'"⚠  Hay un conteo físico en curso ("&kpiConteoContados&" productos): {conteo_msg}."',
            '"●  "&kpiAgotados&" producto(s) agotado(s): prepare hoy el pedido sugerido."',
            '"●  "&kpiEnAlerta&" producto(s) por reponer: revise el pedido sugerido."',
            '"●  "&kpiSinRotacion&" producto(s) sin movimiento hace más de "&cfgDiasSinRotacion&" días: '
            'evalúe promociones o devolución."',
            '"✔  Todo en orden: inventario saludable y bitácora íntegra."']) + ")", "@"),
        ("txtPasoBoton", "Texto del botón del próximo paso", "=CHOOSE(kpiPaso," + ",".join(
            f'"{t}"' for t in ("Ver errores  ➜", "Ver guía  ➜", "Ir al catálogo  ➜", "Registrar  ➜",
                               "Completar  ➜", "Ir al conteo  ➜", "Ver pedido  ➜", "Ver pedido  ➜",
                               "Ver stock  ➜", "Ver stock  ➜")) + ")", "@"),
        # --- textos del tablero y de las barras de herramientas
        ("txtActivosNota", "Nota tarjeta activos", "=kpiCatalogo", '"de "#,##0" en el catálogo"'),
        ("txtAgotNota", "Nota tarjeta agotados", "=IF(kpiActivos=0,0,kpiAgotados/kpiActivos)",
         '0%" del catálogo activo"'),
        ("txtCritNota", "Nota tarjeta críticos", '="en o por debajo del stock mínimo"', "@"),
        ("txtBajosNota", "Nota tarjeta preventivos", "=cfgMargenAlerta", '"hasta "0%" por encima del mínimo"'),
        ("txtOptNota", "Nota tarjeta óptimos", "=IF(kpiActivos=0,0,kpiOptimos/kpiActivos)",
         '0%" del catálogo activo"'),
        ("txtValorNota", "Nota tarjeta valor", '="a costo unitario de catálogo"', "@"),
        ("txtMovNota", "Nota tarjeta movimientos", '="▲ "&kpiIngMes&" ingresos    ▼ "&kpiEgrMes&" egresos"', "@"),
        ("txtSinRotNota", "Nota tarjeta sin rotación",
         '="+"&cfgDiasSinRotacion&" días sin movimiento · $ "&FIXED(kpiValorSinRot,0)', "@"),
        ("txtIntegridad", "Integridad de la bitácora",
         '=IF(kpiErrores>0,"✖ "&kpiErrores&" registro(s) con error en la bitácora",IF(kpiAdvertencias>0,'
         '"⚠ "&kpiAdvertencias&" registro(s) por completar","✔ Bitácora íntegra · sin errores"))', "@"),
        ("txtAlertasBtn", "Texto mosaico alertas", '="ALERTAS ("&kpiEnAlerta&")"', "@"),
        ("txtRegistros", "Chip registros", '="Registros: "&FIXED(kpiRegistros,0)&" de "&FIXED(kpiCapacidad,0)', "@"),
        ("txtProductos", "Chip productos",
         '="Productos: "&FIXED(kpiCatalogo,0)&" de "&FIXED(ROWS(tblProductos[SKU]),0)', "@"),
        ("txtProveedores", "Chip proveedores",
         '="Proveedores: "&FIXED(kpiProveedores,0)&" de "&FIXED(ROWS(tblProveedores[Proveedor]),0)', "@"),
        ("txtValor", "Chip valor", '="Valor del inventario: $ "&FIXED(kpiValor,0)', "@"),
        ("txtEnAlerta", "Chip en alerta", '="Requieren acción: "&kpiEnAlerta', "@"),
        ("txtChipAgot", "Chip agotados", '="● Agotados: "&kpiAgotados', "@"),
        ("txtChipCrit", "Chip críticos", '="● Críticos: "&kpiCriticos', "@"),
        ("txtChipBajo", "Chip preventivos", '="● Preventivos: "&kpiBajos', "@"),
        ("txtChipSobre", "Chip sobrestock", '="● Sobrestock: "&kpiSobrestock', "@"),
        ("txtChipIncons", "Chip inconsistentes", '="● Inconsistentes: "&kpiInconsistentes', "@"),
        ("txtBitacora", "Pie de portada",
         '="Bitácora: "&FIXED(kpiRegistros,0)&" de "&FIXED(kpiCapacidad,0)&" registros · "&'
         'FIXED(IF(kpiCapacidad=0,0,kpiRegistros/kpiCapacidad*100),0)&"% de capacidad"', "@"),
        ("txtEmpresa", "Subtítulo portada", '=cfgEmpresa&"   ·   "&cfgBodega', "@"),
        ("txtUltimoRegistro", "Último registro",
         '=IF(kpiUltimoID<1,"Aún no hay movimientos registrados","Último registro: #"&kpiUltimoID&"  ·  "&'
         'INDEX(tblMovimientos[Tipo],kpiUltimoID)&"  "&IF(N(INDEX(tblMovimientos[CantidadNeta],kpiUltimoID))>0,"+","")&'
         'INDEX(tblMovimientos[CantidadNeta],kpiUltimoID)&" "&INDEX(tblMovimientos[Unidad],kpiUltimoID)&"  ·  "&'
         'INDEX(tblMovimientos[SKU],kpiUltimoID))', "@"),
        ("txtConteoContados", "Chip contados", '="Contados: "&kpiConteoContados&" de "&kpiActivos', "@"),
        ("txtConteoDif", "Chip diferencias", '="Con diferencia: "&kpiConteoDif', "@"),
        ("txtConteoValor", "Chip valor diferencias", '="Valor neto: $ "&FIXED(kpiConteoValor,0)', "@"),
        ("txtPedidoLineas", "Chip líneas pedido", '="Líneas: "&kpiPedidoLineas', "@"),
        ("txtPedidoTotal", "Chip total pedido", '="Total estimado: $ "&FIXED(kpiPedidoTotal,0)', "@"),
        ("txtPedidoResumen", "Resumen pedido",
         '=kpiPedidoLineas&" línea(s) · "&kpiPedidoProv&" proveedor(es)"', "@"),
        ("txtPedidoContacto", "Contacto del proveedor filtrado",
         '=IF(ISNUMBER(pdProvFila),"Contacto: "&INDEX(tblProveedores[Contacto],pdProvFila)&"  ·  Tel. "&'
         'INDEX(tblProveedores[Teléfono],pdProvFila)&"  ·  "&INDEX(tblProveedores[Correo],pdProvFila),'
         '"Todos los proveedores. Para enviar un pedido, elija el proveedor e imprima.")', "@"),
        ("txtPedidoEntrega", "Entrega estimada",
         f'=IF(ISNUMBER(pdProvFila),"Entrega estimada: "&{fecha_txt("TODAY()+N(INDEX(tblProveedores[DiasEntrega],pdProvFila))")}'
         f'&"  ("&N(INDEX(tblProveedores[DiasEntrega],pdProvFila))&" días)","")', "@"),
        ("txtKardexResumen", "Chip consulta",
         '=IF(kxExiste,"Consultando "&kxSKU&"  ·  "&kxN&" movimiento(s)","Elija un producto para consultarlo")', "@"),
        ("txtKardexCoinciden", "Coincidencias de búsqueda",
         '=IF(AND(kxCategoria="",kxBuscar=""),kxCoinciden&" producto(s) en el catálogo · use Buscar para filtrar",'
         'kxCoinciden&" producto(s) coinciden con el filtro")', "@"),
        ("txtKardexMeta", "Ficha: categoría, proveedor, ubicación",
         '=IF(kxExiste,INDEX(tblStock[Categoría],kxFila)&"  ·  "&IF(INDEX(tblStock[Proveedor],kxFila)="",'
         '"Sin proveedor",INDEX(tblStock[Proveedor],kxFila))&"  ·  Ubicación "&INDEX(tblProductos[Ubicación],'
         'MATCH(kxSKU,tblProductos[SKU],0))&"  ·  "&INDEX(tblStock[Unidad],kxFila),"")', "@"),
        ("txtKardexTotales", "Ficha: totales",
         '=IF(kxExiste,"Entradas: "&INDEX(tblStock[Entradas],kxFila)&"    ·    Salidas: "&'
         'INDEX(tblStock[Salidas],kxFila)&"    ·    Movimientos: "&kxN,"")', "@"),
        ("txtKardexMostrando", "Historial: mostrando",
         '=IF(kxExiste,"Mostrando "&kxMostrados&" de "&kxN&" movimiento(s)","")', "@"),
    ]
    if ctx.plus:
        s += form_specs()
    return s


def form_specs() -> list[tuple[str, str, str, str]]:
    """Cálculos del formulario 12_REGISTRO (edición Plus)."""
    return [
        ("frmSKU", "SKU elegido", '=IF(frmProducto="","",TRIM(LEFT(frmProducto,FIND(" · ",frmProducto&" · ")-1)))',
         "@"),
        ("frmFila", "Fila del SKU en 15_STOCK", '=IF(frmSKU="","",IFERROR(MATCH(frmSKU,tblStock[SKU],0),""))', "0"),
        ("frmExiste", "¿Existe?", "=ISNUMBER(frmFila)", "General"),
        ("frmFactor", "FactorStock del tipo",
         "=IFERROR(INDEX(tblTiposMov[FactorStock],MATCH(frmTipo,tblTiposMov[Tipo],0)),0)", "0"),
        ("frmUnidad", "Unidad", '=IF(frmExiste,INDEX(tblStock[Unidad],frmFila),"")', "@"),
        ("frmDecimales", "¿Admite decimales?",
         '=IF(frmUnidad="","SI",IFERROR(INDEX(lstUniDec,MATCH(frmUnidad,lstUniCod,0)),"SI"))', "@"),
        ("frmActual", "Stock actual", '=IF(frmExiste,N(INDEX(tblStock[StockActual],frmFila)),"")', "#,##0.##"),
        ("frmMin", "Stock mínimo", "=IF(frmExiste,N(INDEX(tblStock[StockMin],frmFila)),0)", "#,##0.##"),
        ("frmMax", "Stock máximo", "=IF(frmExiste,N(INDEX(tblStock[StockMax],frmFila)),0)", "#,##0.##"),
        ("frmActivo", "Activo", '=IF(frmExiste,INDEX(tblStock[Activo],frmFila),"")', "@"),
        ("frmCantOK", "¿Cantidad válida?",
         '=IF(ISNUMBER(frmCantidad),AND(frmCantidad>0,OR(frmDecimales="SI",frmCantidad=INT(frmCantidad))),FALSE)',
         "General"),
        ("frmDespues", "Stock después",
         '=IF(AND(frmExiste,frmCantOK,frmFactor<>0),frmActual+frmCantidad*frmFactor,"")', "#,##0.##"),
        ("frmEstadoAct", "Estado actual", '=IF(frmExiste,INDEX(tblStock[Estado],frmFila),"")', "@"),
        ("frmEstadoDesp", "Estado después",
         f'=IF(frmDespues="","",{estado_formula("frmDespues", "frmMin", "frmMax", "frmActivo")})', "@"),
        ("frmCoinciden", "Productos en la lista", f"=COUNT({q(S_LISTAS)}!$L${FIRST}:$L${LAST_PROD})", "#,##0"),
        ("frmMeta", "Detalle del producto",
         '=IF(frmExiste,frmSKU&"  ·  "&frmUnidad&"  ·  Ubic. "&INDEX(tblProductos[Ubicación],MATCH(frmSKU,'
         'tblProductos[SKU],0))&"  ·  "&IF(INDEX(tblStock[Proveedor],frmFila)="","Sin proveedor",'
         'INDEX(tblStock[Proveedor],frmFila)),frmCoinciden&" producto(s) en la lista · use Buscar o Categoría '
         'para filtrar")', "@"),
        ("frmChk1", "Validación: tipo",
         '=IF(frmTipo="","○ Elija el tipo de movimiento",IF(frmFactor=0,"✖ Tipo no válido","✔ "&frmTipo&'
         'IF(frmFactor>0,": suma al inventario",": resta del inventario")))', "@"),
        ("frmChk2", "Validación: producto",
         '=IF(frmProducto="","○ Elija el producto (Buscar filtra la lista)",IF(NOT(frmExiste),'
         '"✖ El producto no existe en el catálogo",IF(frmActivo="NO","⚠ Producto inactivo (descontinuado)",'
         '"✔ Producto "&frmSKU&" en "&frmUnidad)))', "@"),
        ("frmChk3", "Validación: cantidad",
         '=IF(frmCantidad="","○ Escriba la cantidad"&IF(frmUnidad="",""," en "&frmUnidad),IF(frmCantOK,'
         '"✔ Cantidad: "&frmCantidad&" "&frmUnidad,"✖ Cantidad no válida: debe ser mayor que 0"&'
         'IF(frmDecimales="NO"," y sin decimales","")))', "@"),
        ("frmChk4", "Validación: stock",
         '=IF(frmDespues="","○ El stock resultante se calcula al completar tipo, producto y cantidad",'
         'IF(frmDespues<0,"✖ Stock insuficiente: hay "&frmActual&" "&frmUnidad&" disponibles",'
         '"✔ Quedarán "&frmDespues&" "&frmUnidad&" en stock"))', "@"),
        ("frmChk5", "Validación: responsable",
         '=IF(frmResponsable="","○ Indique el responsable",IF(ISNA(MATCH(frmResponsable,lstResponsables,0)),'
         '"✖ Responsable no válido","✔ Responsable: "&frmResponsable))', "@"),
        ("frmChk6", "Validación: observación",
         '=IF(LEFT(frmTipo,6)<>"AJUSTE","✔ Observaciones opcionales",IF(frmObservaciones="",'
         '"✖ Los ajustes exigen una observación (motivo)","✔ Ajuste justificado"))', "@"),
        ("frmChk7", "Validación: fecha",
         '=IF(frmFecha="","✔ Fecha: hoy",IF(AND(ISNUMBER(frmFecha),frmFecha>=cfgFechaMin,frmFecha<=TODAY()),'
         '"✔ Fecha válida","✖ Fecha no válida: no puede ser futura ni anterior a la mínima"))', "@"),
        ("frmValido", "¿Listo para registrar?",
         '=AND(LEFT(frmChk1,1)="✔",OR(LEFT(frmChk2,1)="✔",LEFT(frmChk2,1)="⚠"),LEFT(frmChk3,1)="✔",'
         'LEFT(frmChk4,1)="✔",LEFT(frmChk5,1)="✔",LEFT(frmChk6,1)="✔",LEFT(frmChk7,1)="✔")', "General"),
        ("frmResumen", "Resumen de validación",
         '=IF(frmValido,"✔  Listo: pulse REGISTRAR MOVIMIENTO","○  Complete los campos marcados con ○ o ✖")', "@"),
    ]


def _nested(pairs, default) -> str:
    expr = str(default)
    for cond, val in reversed(pairs):
        expr = f"IF({cond},{val},{expr})"
    return expr


def allocate(ctx: Ctx):
    """Asigna una fila de 91_KPIS a cada indicador (antes de construir las hojas que los referencian)."""
    ctx.layout.update(LAYOUT)
    for i, spec in enumerate(kpi_specs(ctx)):
        ctx.kpi[spec[0]] = FIRST + i


# ---------------------------------------------------------------------------
# 90_LISTAS
# ---------------------------------------------------------------------------
def build_listas(ctx: Ctx):
    ws, st, wb = ctx.sheets[S_LISTAS], ctx.st, ctx.wb
    P = {c.name: ctx.col("tblProductos", c.name) for c in ctx.tables["tblProductos"]}
    config_band(ctx, ws, "Motor de listas",
                "Motor interno (oculto) · Listas en cascada, búsqueda por texto y proveedores",
                [24, 24, 170, 300, 120, 24, 60, 120, 170, 300, 24, 60, 300, 24, 60, 300, 24, 200, 60, 220, 24])
    hdr = st.hdr("calc")
    heads = {"C": "Categoría válida", "D": "Etiqueta", "E": "Clave de orden", "G": "k", "H": "Clave k-ésima",
             "I": "Categoría ordenada", "J": "Etiqueta ordenada", "L": "k formulario", "M": "Lista formulario",
             "O": "k consulta", "P": "Lista consulta", "R": "Filtro proveedor", "S": "k proveedor",
             "T": "Proveedores"}
    for c, h in heads.items():
        ws.write_string(HDR - 1, ord(c) - 65, h, hdr)
    msg = st(bg_color=C["ink"], font_color=C["white"])
    ws.write_string(3, 9, "— Sin productos activos en esta categoría —", msg)
    ws.write_string(3, 12, "— Ningún producto coincide con la búsqueda —", msg)
    ws.write_string(3, 15, "— Ningún producto coincide con la búsqueda —", msg)
    ws.write_string(3, 19, "— Registre proveedores en 04_PROVEEDORES —", msg)
    sp = q(S_PROD)
    cell = st(font_size=9, formula=True)
    for i in range(MAX_PROD):
        r = FIRST + i
        # lista en cascada de la bitácora (productos activos ordenados por categoría y etiqueta)
        ws.write_formula(r - 1, 2, f'=IF(AND({sp}!${P["SKU"]}{r}<>"",{sp}!${P["Producto"]}{r}<>"",'
                                   f'{sp}!${P["Activo"]}{r}<>"NO",ISNUMBER(MATCH({sp}!${P["Categoría"]}{r},'
                                   f'lstCategorias,0))),{sp}!${P["Categoría"]}{r},"")', cell)
        ws.write_formula(r - 1, 3, f'=IF($C{r}="","",{sp}!${P["Etiqueta"]}{r})', cell)
        ws.write_formula(r - 1, 4, f'=IF($C{r}="","",MATCH($C{r},lstCategorias,0)*1000+'
                                   f'COUNTIFS($C${FIRST}:$C${LAST_PROD},$C{r},$D${FIRST}:$D${LAST_PROD},"<"&$D{r})+1'
                                   f'+ROW()/100000)', cell)
        ws.write_number(r - 1, 6, i + 1, st(font_size=9, align="center"))
        ws.write_formula(r - 1, 7, f'=IFERROR(SMALL($E${FIRST}:$E${LAST_PROD},$G{r}),"")', cell)
        ws.write_formula(r - 1, 8, f'=IF($H{r}="","",INDEX($C${FIRST}:$C${LAST_PROD},'
                                   f'MATCH($H{r},$E${FIRST}:$E${LAST_PROD},0)))', cell)
        ws.write_formula(r - 1, 9, f'=IF($H{r}="","",INDEX($D${FIRST}:$D${LAST_PROD},'
                                   f'MATCH($H{r},$E${FIRST}:$E${LAST_PROD},0)))', cell)
        # búsqueda del formulario (edición Plus): categoría + texto sobre la lista ordenada de activos
        if ctx.plus:
            ws.write_formula(r - 1, 11, f'=IF($J{r}="","",IF(AND(OR(frmCategoria="",$I{r}=frmCategoria),'
                                        f'OR(frmBuscar="",ISNUMBER(SEARCH(frmBuscar,$J{r})))),ROW()-{FIRST - 1},""))',
                             cell)
            ws.write_formula(r - 1, 12, f'=IFERROR(INDEX($J${FIRST}:$J${LAST_PROD},SMALL($L${FIRST}:$L${LAST_PROD},'
                                        f'ROW()-{FIRST - 1})),"")', cell)
        # búsqueda de la consulta: todo el catálogo (incluye descontinuados)
        lab = f"{sp}!${P['Etiqueta']}{r}"
        ws.write_formula(r - 1, 14, f'=IF({lab}="","",IF(AND(OR(kxCategoria="",{sp}!${P["Categoría"]}{r}=kxCategoria),'
                                    f'OR(kxBuscar="",ISNUMBER(SEARCH(kxBuscar,{lab})))),ROW()-{FIRST - 1},""))', cell)
        ws.write_formula(r - 1, 15, f'=IFERROR(INDEX({sp}!${P["Etiqueta"]}${FIRST}:${P["Etiqueta"]}${LAST_PROD},'
                                    f'SMALL($O${FIRST}:$O${LAST_PROD},ROW()-{FIRST - 1})),"")', cell)
    # proveedores: lista compacta (sin huecos) y lista de filtro con «(Todos)»
    ws.write_string(FIRST - 1, 17, "(Todos)", st(font_size=9, bold=True))
    for i in range(MAX_PROV):
        r = FIRST + i
        ws.write_formula(r - 1, 18, f'=IF(INDEX(tblProveedores[Proveedor],{i + 1})="","",{i + 1})', cell)
        ws.write_formula(r - 1, 19, f'=IFERROR(INDEX(tblProveedores[Proveedor],SMALL($S${FIRST}:$S${LAST_PROV},'
                                    f'{i + 1})),"")', cell)
        ws.write_formula(r, 17, f"=$T{r}", cell)
    L = q(S_LISTAS)
    n_prov = f'COUNTIF({L}!$T${FIRST}:$T${LAST_PROV},"?*")'
    names = {
        "lpCat": f"={L}!$I${FIRST}:$I${LAST_PROD}",
        "lpBase": f"={L}!$J${HDR}",
        "lpVacio": f"={L}!$J$4",
        "lpTodos": f'=IF(COUNTIF({L}!$J${FIRST}:$J${LAST_PROD},"?*")=0,{L}!$J$4,'
                   f'{L}!$J${FIRST}:INDEX({L}!$J${FIRST}:$J${LAST_PROD},COUNTIF({L}!$J${FIRST}:$J${LAST_PROD},"?*")))',
        "lfKardex": f"=IF(COUNT({L}!$O${FIRST}:$O${LAST_PROD})=0,{L}!$P$4,"
                    f"{L}!$P${FIRST}:INDEX({L}!$P${FIRST}:$P${LAST_PROD},COUNT({L}!$O${FIRST}:$O${LAST_PROD})))",
        "lstProveedores": f"=IF({n_prov}=0,{L}!$T$4,{L}!$T${FIRST}:INDEX({L}!$T${FIRST}:$T${LAST_PROV},{n_prov}))",
        "lstFiltroProv": f"={L}!$R${FIRST}:INDEX({L}!$R${FIRST}:$R${LAST_PROV + 1},1+{n_prov})",
    }
    if ctx.plus:
        names["lfForm"] = (f"=IF(COUNT({L}!$L${FIRST}:$L${LAST_PROD})=0,{L}!$M$4,"
                           f"{L}!$M${FIRST}:INDEX({L}!$M${FIRST}:$M${LAST_PROD},COUNT({L}!$L${FIRST}:$L${LAST_PROD})))")
    for name, ref in names.items():
        wb.define_name(name, ref)


# ---------------------------------------------------------------------------
# 91_KPIS
# ---------------------------------------------------------------------------
def build_kpis(ctx: Ctx, n_cat: int):
    ws, st, wb = ctx.sheets[S_KPI], ctx.st, ctx.wb
    S = {c.name: ctx.col("tblStock", c.name) for c in ctx.tables["tblStock"]}
    config_band(ctx, ws, "Motor de indicadores",
                "Motor interno (oculto) · KPIs, asistente, series de gráficos, rankings y cálculos del formulario",
                [24, 230, 150, 260, 24, 110, 90, 90, 150, 110, 110, 150, 150, 150, 60, 150, 70, 60, 150, 70, 24,
                 50, 110, 110, 24])
    sec = st(bold=True, font_color=C["brand"], font_size=10.5)
    lbl = st(font_size=9, indent=1, border=1, border_color=C["border"])
    note = st(font_size=8.5, font_color=C["muted"], indent=1, border=1, border_color=C["border"])
    n0 = st(font_size=9, bold=True, align="right", num_format="#,##0", border=1, border_color=C["border"],
            formula=True)
    hdr = st.hdr("calc")
    for (r, c), text in {(4, 1): "INDICADORES (nombres definidos)", (4, 5): "ACTIVIDAD MENSUAL (6 meses)",
                         (15, 5): "TOP 8 POR VALOR", (27, 5): "SALUD DEL INVENTARIO",
                         (37, 5): "ESTADO POR CATEGORÍA", (4, 11): "CLAVES POR PRODUCTO",
                         (4, 14): "RANKING DE ALERTAS", (4, 17): "RANKING DEL PEDIDO",
                         (4, 21): "SERIE DE LA CONSULTA"}.items():
        ws.write_string(r, c, text, sec)
    for c, h in zip("BCD", ["Indicador", "Valor", "Nombre definido"]):
        ws.write_string(HDR - 1, ord(c) - 65, h, hdr)

    for name, label, formula, nf in kpi_specs(ctx):
        r = ctx.kpi[name]
        ws.write_string(r - 1, 1, label, lbl)
        ws.write_formula(r - 1, 2, formula, st(font_size=9, bold=True, align="right", num_format=nf, border=1,
                                               border_color=C["border"], formula=True))
        ws.write_string(r - 1, 3, name, note)
        wb.define_name(name, f"={q(S_KPI)}!$C${r}")

    # Actividad mensual (F..I, filas 8..13)
    for c, h in zip("FGHI", ["Inicio de mes", "Mes", "Ingresos", "Egresos"]):
        ws.write_string(HDR - 1, ord(c) - 65, h, hdr)
    for i in range(6):
        r = 8 + i
        start = "=DATE(YEAR(TODAY()),MONTH(TODAY())-5,1)" if i == 0 else f"=DATE(YEAR(F{r - 1}),MONTH(F{r - 1})+1,1)"
        ws.write_formula(r - 1, 5, start, st(font_size=9, num_format="dd/mm/yyyy", formula=True))
        ws.write_formula(r - 1, 6, f'=CHOOSE(MONTH(F{r}),"Ene","Feb","Mar","Abr","May","Jun","Jul","Ago","Sep",'
                                   f'"Oct","Nov","Dic")&" "&RIGHT(YEAR(F{r}),2)', st(font_size=9, formula=True))
        for c, factor in ((7, 1), (8, -1)):
            extra = ',tblMovimientos[Tipo],"<>SALDO INICIAL"' if factor == 1 else ""
            ws.write_formula(r - 1, c, f'=COUNTIFS(tblMovimientos[Fecha],">="&F{r},tblMovimientos[Fecha],'
                                       f'"<"&DATE(YEAR(F{r}),MONTH(F{r})+1,1),tblMovimientos[FactorStock],{factor}'
                                       f'{extra})', n0)
    # Top 8 por valor (F..J, filas 18..25)
    for c, h in zip("FGHIJ", ["k", "Clave", "Fila", "Producto", "Valor"]):
        ws.write_string(16, ord(c) - 65, h, hdr)
    for k in range(8):
        r = 18 + k
        ws.write_number(r - 1, 5, k + 1, st(font_size=9, align="center"))
        ws.write_formula(r - 1, 6, f'=IFERROR(LARGE($L${FIRST}:$L${LAST_PROD},F{r}),"")', st(font_size=9, formula=True))
        ws.write_formula(r - 1, 7, f'=IF(G{r}="","",MATCH(G{r},$L${FIRST}:$L${LAST_PROD},0))',
                         st(font_size=9, formula=True))
        ws.write_formula(r - 1, 8, f'=IF(H{r}="","",LEFT(INDEX(tblStock[Producto],H{r}),34))',
                         st(font_size=9, formula=True))
        ws.write_formula(r - 1, 9, f'=IF(H{r}="","",INDEX(tblStock[ValorInventario],H{r}))',
                         st(font_size=9, num_format="$ #,##0", formula=True))
    # Salud del inventario (F..H, filas 30..35)
    for c, h in zip("FGH", ["Estado", "Productos", "Color"]):
        ws.write_string(28, ord(c) - 65, h, hdr)
    salud = [("Agotado", "kpiAgotados", "AGOTADO"), ("Crítico", "kpiCriticos", "CRÍTICO"),
             ("Preventivo", "kpiBajos", "BAJO"), ("Óptimo", "kpiOptimos", "ÓPTIMO"),
             ("Sobrestock", "kpiSobrestock", "SOBRESTOCK"), ("Inconsistente", "kpiInconsistentes", "INCONSISTENTE")]
    for i, (label, name, est) in enumerate(salud):
        r = 30 + i
        ws.write_string(r - 1, 5, label, st(font_size=9))
        ws.write_formula(r - 1, 6, f"={name}", n0)
        ws.write_string(r - 1, 7, EST[est][5], st(font_size=9, font_color=EST[est][5]))
    # Estado por categoría (F..K, filas 39..)
    etiquetas = ["Agotado", "Crítico", "Preventivo", "Óptimo", "Sobrestock"]
    claves = ["AGOTADO", "CRÍTICO", "BAJO", "ÓPTIMO", "SOBRESTOCK"]
    ws.write_string(38, 5, "Categoría", hdr)
    for j, (et, cl) in enumerate(zip(etiquetas, claves)):
        ws.write_string(38, 6 + j, et, hdr)
        ws.write_string(39, 6 + j, cl, st(font_size=8, font_color=C["muted"], align="center"))
    for k in range(max(n_cat, 1)):
        r = 41 + k
        ws.write_formula(r - 1, 5, f'=IFERROR(INDEX(tblCategorias[Categoría],{k + 1}),"")',
                         st(font_size=9, formula=True))
        for j in range(5):
            cl = xl_col_to_name(6 + j)
            ws.write_formula(r - 1, 6 + j, f'=IF($F{r}="","",COUNTIFS(tblStock[Categoría],$F{r},'
                                           f'tblStock[Estado],{cl}$40))', n0)

    # Claves por producto (L valor, M alerta, N pedido) alineadas con 15_STOCK + rankings
    for c, h in zip("LMNOPQRSTVWX", ["Clave valor", "Clave alerta", "Clave pedido", "k", "Clave k", "Fila stock",
                                     "k", "Clave k", "Fila stock", "j", "Fecha", "Saldo"]):
        ws.write_string(HDR - 1, ord(c) - 65, h, hdr)
    sq = q(S_STOCK)
    col = lambda n: f"{sq}!${S[n]}"  # noqa: E731
    small = st(font_size=8.5, formula=True)
    ra, rp = LAYOUT["rank_alertas"], LAYOUT["rank_pedido"]
    for i in range(MAX_PROD):
        r = FIRST + i
        v, e = f"{col('ValorInventario')}{r}", f"{col('Estado')}{r}"
        ws.write_formula(r - 1, 11, f'=IF(AND(ISNUMBER({v}),{v}>0),{v}+ROW()/1000000,"")', small)
        ws.write_formula(r - 1, 12, f'=IFERROR(MATCH({e},{A_ALERTAS},0)*100000+MIN(99,INT(IF(N({col("StockMin")}{r})>0,'
                                    f'MAX(0,N({col("StockActual")}{r}))/{col("StockMin")}{r},0)*10))*1000+ROW(),"")',
                         small)
        prov = f"{col('Proveedor')}{r}"
        ws.write_formula(r - 1, 13, f'=IF(AND(ISNUMBER(MATCH({e},{A_REPONER},0)),{col("Activo")}{r}="SI",'
                                    f'OR(pdProveedor="(Todos)",pdProveedor="",{prov}=pdProveedor)),'
                                    f'IFERROR(MATCH({prov},tblProveedores[Proveedor],0),999)*1000000+'
                                    f'MATCH({e},{A_REPONER},0)*10000+ROW(),"")', small)
        for rank, keycol in ((ra, "M"), (rp, "N")):
            k, key, fila = (ord(rank[x]) - 65 for x in ("k", "key", "fila"))
            ws.write_number(r - 1, k, i + 1, st(font_size=8.5, align="center"))
            ws.write_formula(r - 1, key, f'=IFERROR(SMALL(${keycol}${FIRST}:${keycol}${LAST_PROD},'
                                         f'{rank["k"]}{r}),"")', small)
            ws.write_formula(r - 1, fila, f'=IF({rank["key"]}{r}="","",MATCH({rank["key"]}{r},'
                                          f'${keycol}${FIRST}:${keycol}${LAST_PROD},0))', small)
    # Serie del gráfico de la consulta (cronológica; #N/A intencional = punto no trazado)
    xcol, ycol, r0, r1 = LAYOUT["kardex_chart"]
    kx = q(S_KARDEX)
    for j in range(1, r1 - r0 + 2):
        r = r0 + j - 1
        ws.write_number(r - 1, 21, j, st(font_size=8.5, align="center"))
        ws.write_formula(r - 1, 22, f"=IF({j}>kxMostrados,NA(),INDEX({kx}!$C${KX_FIRST}:$C${KX_LAST},"
                                    f"kxMostrados-{j}+1))", st(font_size=8.5, num_format="dd/mm/yyyy", formula=True))
        ws.write_formula(r - 1, 23, f"=IF({j}>kxMostrados,NA(),INDEX({kx}!$G${KX_FIRST}:$G${KX_LAST},"
                                    f"kxMostrados-{j}+1))", small)


# ---------------------------------------------------------------------------
# Nombres de navegación dinámica
# ---------------------------------------------------------------------------
def define_navigation(ctx: Ctx):
    wb = ctx.wb
    mov, prod = q(S_MOV), q(S_PROD)
    wb.define_name("irBitacora", f"=INDEX({mov}!$C:$C,MIN(kpiFilaLibreMov,{LAST_MOV}))")
    wb.define_name("irRegistrar", f"={q(S_REG)}!$D$8" if ctx.plus else "=irBitacora")
    wb.define_name("irConsultar", f"={q(S_KARDEX)}!$H$8")
    wb.define_name("irFilaLibreProd", f"=INDEX({prod}!$B:$B,MIN(kpiFilaLibreProd,{LAST_PROD}))")
    wb.define_name("irFilaLibreProv", f"=INDEX({q('04_PROVEEDORES')}!$B:$B,MIN(kpiFilaLibreProv,{LAST_PROV}))")
    wb.define_name("irPrimerError", '=INDEX(tblMovimientos[Fecha],MATCH("✖*",tblMovimientos[Estado],0))')
    wb.define_name("irPrimerAdvertencia", '=INDEX(tblMovimientos[Fecha],MATCH("⚠*",tblMovimientos[Estado],0))')
    wb.define_name("irSiguientePaso",
                   f"=CHOOSE(kpiPaso,irPrimerError,irPrimerosPasos,irFilaLibreProd,irRegistrar,irPrimerAdvertencia,"
                   f"{q(S_CONTEO)}!$I${FIRST},{q(S_PEDIDO)}!$D$10,{q(S_PEDIDO)}!$D$10,{q(S_STOCK)}!$A$1,"
                   f"{q(S_STOCK)}!$A$1)")
