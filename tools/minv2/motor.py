"""
M-INV V2 · Motor oculto liviano: 90_LISTAS, 91_KPIS y 92_SESION.

En la V2 no hay proyecciones pesadas en vivo: los indicadores leen la instantánea (15/16, valores) o hacen conteos
simples sobre las bitácoras (valores). Nada de rankings de 500 filas recalculándose mientras la gente trabaja.
"""
from __future__ import annotations

from minv.base import C, FIRST, S_KPI, S_LISTAS, Ctx, q
from minv.config_sheets import config_band

from .base2 import ROLES, S_SES, TIPOS_BODEGA, fecha_txt, hora_txt

MES = 'DATE(YEAR(TODAY()),MONTH(TODAY()),1)'
OK = '"✔*"'


def kpi_specs2() -> list[tuple[str, str, str, str]]:
    ult = lambda t, c: f'IFERROR(LOOKUP(2,1/({t}[{c}]<>""),ROW({t}[{c}])-ROW({t}[#Headers])),0)'  # noqa: E731
    s = [
        # --- maestros
        ("kpiCatalogo", "Productos en el catálogo", '=COUNTIF(tblProductos[SKU],"?*")', "#,##0"),
        ("kpiActivos", "Productos activos", '=COUNTIFS(tblProductos[SKU],"?*",tblProductos[Activo],"<>NO")', "#,##0"),
        ("kpiProveedores", "Proveedores", '=COUNTIF(tblProveedores[Proveedor],"?*")', "#,##0"),
        ("kpiUsuarios", "Usuarios activos", '=COUNTIFS(tblUsuarios[Correo],"?*",tblUsuarios[Activo],"<>NO")', "#,##0"),
        ("kpiAdmins", "Administradores activos",
         '=COUNTIFS(tblUsuarios[Correo],"?*",tblUsuarios[Activo],"<>NO",tblUsuarios[Rol],"ADMIN")', "#,##0"),
        ("kpiUltProd", "Última fila usada del catálogo", "=" + ult("tblProductos", "SKU"), "0"),
        ("kpiUltProv", "Última fila usada de proveedores", "=" + ult("tblProveedores", "Proveedor"), "0"),
        # --- instantánea (15_STOCK)
        ("kpiInconsistentes", "Inconsistentes", '=COUNTIF(tblStock[Estado],"INCONSISTENTE")', "#,##0"),
        ("kpiAgotados", "Agotados", '=COUNTIF(tblStock[Estado],"AGOTADO")', "#,##0"),
        ("kpiCriticos", "Críticos", '=COUNTIF(tblStock[Estado],"CRÍTICO")', "#,##0"),
        ("kpiBajos", "Preventivos", '=COUNTIF(tblStock[Estado],"BAJO")', "#,##0"),
        ("kpiOptimos", "Óptimos", '=COUNTIF(tblStock[Estado],"ÓPTIMO")', "#,##0"),
        ("kpiSobrestock", "Sobrestock", '=COUNTIF(tblStock[Estado],"SOBRESTOCK")', "#,##0"),
        ("kpiInactivos", "Inactivos", '=COUNTIF(tblStock[Estado],"INACTIVO")', "#,##0"),
        ("kpiEnAlerta", "Requieren acción", "=kpiInconsistentes+kpiAgotados+kpiCriticos+kpiBajos", "#,##0"),
        ("kpiValor", "Valor del inventario", "=SUM(tblStock[ValorInventario])", "$ #,##0"),
        ("kpiDisponibles", "Productos con stock", '=COUNTIFS(tblStock[StockActual],">0",tblStock[Activo],"SI")', "#,##0"),
        ("kpiSinRotacion", "Sin rotación",
         '=COUNTIFS(tblStock[DiasSinMov],">"&cfgDiasSinRotacion,tblStock[StockActual],">0",tblStock[Activo],"SI")',
         "#,##0"),
        # --- bitácoras oficiales (valores: conteos livianos)
        ("kpiRegEnt", "Registros consolidados en 10A", f"=COUNTIF(tblEntradas[Estado],{OK})", "#,##0"),
        ("kpiRegSal", "Registros consolidados en 10B", f"=COUNTIF(tblSalidas[Estado],{OK})", "#,##0"),
        ("kpiRechEnt", "Rechazados en 10A", '=COUNTIF(tblEntradas[Estado],"✖*")', "#,##0"),
        ("kpiRechSal", "Rechazados en 10B", '=COUNTIF(tblSalidas[Estado],"✖*")', "#,##0"),
        ("kpiPendEnt", "Filas con datos en la captura 10A", '=COUNTIF(tblCapturaEntradas[Validación],"?*")', "#,##0"),
        ("kpiPendSal", "Filas con datos en la captura 10B", '=COUNTIF(tblCapturaSalidas[Validación],"?*")', "#,##0"),
        ("kpiEntradasHoy", "Entradas de hoy",
         f'=COUNTIFS(tblEntradas[Fecha],TODAY(),tblEntradas[Tipo],"ENTRADA",tblEntradas[Estado],{OK})', "#,##0"),
        ("kpiAjustesMes", "Ajustes del mes",
         f'=COUNTIFS(tblEntradas[Fecha],">="&{MES},tblEntradas[Tipo],"AJUSTE*",tblEntradas[Estado],{OK})', "#,##0"),
        ("kpiSaldosIni", "Productos con SALDO INICIAL",
         f'=COUNTIFS(tblEntradas[Tipo],"SALDO INICIAL",tblEntradas[Estado],{OK})', "#,##0"),
        ("kpiSalidasHoy", "Salidas de hoy", f"=COUNTIFS(tblSalidas[Fecha],TODAY(),tblSalidas[Estado],{OK})", "#,##0"),
        ("kpiUnidadesHoy", "Unidades despachadas hoy",
         f"=-SUMIFS(tblSalidas[CantidadNeta],tblSalidas[Fecha],TODAY(),tblSalidas[Estado],{OK})", "#,##0.##"),
        ("kpiSalidasMes", "Salidas del mes",
         f'=COUNTIFS(tblSalidas[Fecha],">="&{MES},tblSalidas[Estado],{OK})', "#,##0"),
        ("kpiNuevosDesdeCalculo", "Movimientos posteriores a la instantánea",
         '=COUNTIF(tblEntradas[Timestamp],">"&N(stkActualizado))+COUNTIF(tblSalidas[Timestamp],">"&N(stkActualizado))',
         "#,##0"),
        ("kpiRechazos", "Rechazos", "=kpiRechEnt+kpiRechSal", "#,##0"),
        # --- textos
        ("txtHoy", "Fecha de hoy", f'="Hoy · "&{fecha_txt("TODAY()")}', "@"),
        ("txtEmpresa", "Empresa y bodega", '=cfgEmpresa&"   ·   "&cfgBodega', "@"),
        ("txtStockActualizado", "Cuándo se calculó la instantánea",
         f'=IF(N(stkActualizado)=0,"Stock aún no calculado",'
         f'"Calculado el "&{fecha_txt("stkActualizado")}&" "&{hora_txt("stkActualizado")}&" · "&'
         f'IF(stkActualizadoNombre="",stkActualizadoPor,stkActualizadoNombre))', "@"),
        ("txtFrescura", "Frescura de la instantánea",
         '=IF(N(stkActualizado)=0,"⚠ Pulse «Recalcular stock» para calcular el stock",'
         'IF(kpiNuevosDesdeCalculo>0,"⚠ "&kpiNuevosDesdeCalculo&" movimiento(s) nuevo(s) desde el cálculo: recalcule",'
         '"✔ Stock al día con las bitácoras"))', "@"),
        ("txtFrescuraCorta", "Frescura (chip)",
         '=IF(N(stkActualizado)=0,"⚠ Sin calcular",IF(kpiNuevosDesdeCalculo>0,"⚠ "&kpiNuevosDesdeCalculo&'
         '" mov. nuevo(s): recalcule","✔ Al día con las bitácoras"))', "@"),
        ("txtAlertasBtn", "Botón de alertas", '="⚠  ALERTAS DE STOCK CRÍTICO ("&kpiEnAlerta&")"', "@"),
        ("txtPasoBodega", "Próximo paso (Bodega)",
         '=IF(kpiUsuarios=0,"① El administrador debe registrar los usuarios en 02_USUARIOS",'
         'IF(kpiCatalogo=0,"② Registre los productos en el catálogo (05_PRODUCTOS)",'
         'IF(kpiSaldosIni=0,"③ Cargue el SALDO INICIAL de cada producto en 10A_ENTRADAS",'
         'IF(kpiRechEnt>0,"✖ Hay "&kpiRechEnt&" registro(s) rechazados en 10A: revíselos",'
         'IF(kpiAgotados+kpiCriticos>0,"● "&(kpiAgotados+kpiCriticos)&" producto(s) agotados o críticos: gestione la '
         'reposición y registre las entradas al recibir","✔ Inventario saludable")))))', "@"),
        ("txtPasoVentas", "Próximo paso (Ventas)",
         '=IF(kpiUsuarios=0,"El administrador debe registrar los usuarios en 02_USUARIOS",'
         'IF(kpiRechSal>0,"✖ Hay "&kpiRechSal&" salida(s) rechazadas en 10B: revíselas",'
         'IF(kpiAgotados>0,"● "&kpiAgotados&" producto(s) agotado(s): no los ofrezca sin confirmar con Bodega",'
         '"✔ Todos los productos activos tienen stock")))', "@"),
        ("txtBitacoras", "Pie de portada",
         '="Bitácoras oficiales: "&FIXED(kpiRegEnt,0)&" entradas/ajustes · "&FIXED(kpiRegSal,0)&" salidas"', "@"),
    ]
    return s


def build_kpis2(ctx: Ctx):
    wb, ws, st = ctx.wb, ctx.sheets[S_KPI], ctx.st
    config_band(ctx, ws, "Motor de indicadores",
                "Capa de motor (oculta) · Conteos livianos sobre la instantánea y las bitácoras · No editar",
                [24, 320, 150, 110, 24])
    ws.write_string(6, 1, "Indicador", st.hdr("calc"))
    ws.write_string(6, 2, "Valor", st.hdr("calc"))
    ws.write_string(6, 3, "Formato", st.hdr("calc"))
    lbl = st(indent=1, border=1, border_color=C["border"])
    for i, (name, label, formula, nf) in enumerate(kpi_specs2()):
        r = 7 + i
        ws.write_string(r, 1, f"{name} · {label}", lbl)
        ws.write_formula(r, 2, formula, st(num_format=nf, border=1, border_color=C["border"], formula=True,
                                           align="right"))
        ws.write_string(r, 3, nf, lbl)
        wb.define_name(name, f"={q(S_KPI)}!$C${r + 1}")
        ctx.kpi[name] = r + 1


def build_listas2(ctx: Ctx):
    wb, ws, st = ctx.wb, ctx.sheets[S_LISTAS], ctx.st
    config_band(ctx, ws, "Motor de listas",
                "Capa de motor (oculta) · Listas fijas de validación · Productos y proveedores se leen del catálogo",
                [24, 150, 24, 120, 24])
    hdr = st.hdr("calc")
    ws.write_string(6, 1, "Tipos de Bodega", hdr)
    ws.write_string(6, 3, "Roles", hdr)
    f = st(indent=1, border=1, border_color=C["border"])
    for i, t in enumerate(TIPOS_BODEGA):
        ws.write_string(7 + i, 1, t, f)
    for i, r in enumerate(ROLES):
        ws.write_string(7 + i, 3, r, f)
    wb.define_name("lstTiposBodega", f"={q(S_LISTAS)}!$B$8:$B${7 + len(TIPOS_BODEGA)}")
    wb.define_name("lstRoles", f"={q(S_LISTAS)}!$D$8:$D${7 + len(ROLES)}")


def define_names2(ctx: Ctx, etiqueta_col: str):
    """Listas dinámicas del catálogo (DESREF solo dentro de validaciones, regla R-08)."""
    wb = ctx.wb
    wb.define_name("lstProductos", f"=OFFSET({q('05_PRODUCTOS')}!${etiqueta_col}${FIRST},0,0,MAX(1,kpiUltProd),1)")
    wb.define_name("lstProveedores", f"=OFFSET({q('04_PROVEEDORES')}!$B${FIRST},0,0,MAX(1,kpiUltProv),1)")


def build_sesion(ctx: Ctx):
    """Hoja técnica sin proteger: los scripts crean y borran en ella un comentario para leer el correo de Microsoft 365
    de quien los ejecuta (Comment.getAuthorEmail). No guarda datos."""
    ws, st = ctx.sheets[S_SES], ctx.st
    ws.set_column_pixels(0, 0, 900)
    ws.write_string(0, 0, "Hoja técnica de M-INV: los Office Scripts la usan para identificar al usuario de "
                          "Microsoft 365 (crean y borran un comentario). No la modifique ni la proteja.",
                    st(bold=True, font_color=C["muted"], text_wrap=True))
    ws.set_row_pixels(0, 40)
