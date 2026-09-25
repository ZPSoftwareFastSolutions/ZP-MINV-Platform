"""
M-INV V2 · Datos de demostración y fixture de pruebas de los Office Scripts.

Parte de la misma simulación de la V1.2 (demo_data.generar_movimientos) y la reparte en los dos fragmentos:
SALIDA → 10B_SALIDAS (usuarios de Ventas); el resto → 10A_ENTRADAS (usuarios de Bodega). Cada fila lleva el ID sin
coordinación, el correo de Microsoft 365 y el Timestamp que estamparía el script.
"""
from __future__ import annotations

import datetime as dt
import random
from dataclasses import dataclass, field

import demo_data as D

from .base2 import (CAPTURE_COLS, ESTADO_OK, LEDGER_COLS, MAX_CAPT, PREFIJO, ROLES_DOMINIO, S_ENT, S_SAL)
from .config2 import USUARIOS_DEMO
from .lectura import proyectar, serial

UNIDAD = {p.sku: p.unidad for p in D.PRODUCTOS_DEMO}
NOMBRE = {p.sku: p.nombre for p in D.PRODUCTOS_DEMO}
POR_NOMBRE = {u[1]: u for u in USUARIOS_DEMO}
BODEGA = [u for u in USUARIOS_DEMO if u[2] == "BODEGA"]
VENTAS = [u for u in USUARIOS_DEMO if u[2] == "VENTAS"]
ADMIN = USUARIOS_DEMO[0]
MOVS_TRAS_CALCULO = 3     # movimientos registrados después de la instantánea (la demo muestra «recalcule»)


@dataclass
class DatosV2:
    entradas: list[dict] = field(default_factory=list)
    salidas: list[dict] = field(default_factory=list)
    captura: dict = field(default_factory=dict)          # hoja -> {n.º de fila: valores}
    stock: list[list] = field(default_factory=list)      # instantánea precargada
    alertas: list[list] = field(default_factory=list)
    stock_completo: list[list] = field(default_factory=list)   # lo que debe producir RecalcularStock.ts
    alertas_completas: list[list] = field(default_factory=list)
    meta: dict = field(default_factory=dict)             # stkActualizado, stkActualizadoPor...


def productos_dict() -> list[dict]:
    return [{"SKU": p.sku, "Producto": p.nombre, "Categoría": p.categoria, "Unidad": p.unidad,
             "StockMin": p.minimo, "StockMax": p.maximo, "CostoUnitario": p.costo, "Proveedor": p.proveedor,
             "Ubicación": p.ubicacion, "Activo": "SI" if p.activo else "NO"} for p in D.PRODUCTOS_DEMO]


def orden_captura(hoja: str) -> list[tuple]:
    """Usuarios del dominio en el orden de 02_USUARIOS (define su fila de captura)."""
    return [u for u in USUARIOS_DEMO if u[2] in ROLES_DOMINIO[hoja]]


def _id(prefijo: str, ts: dt.datetime, rnd: random.Random) -> str:
    return f"{prefijo}-{ts:%Y%m%d}-{ts:%H%M%S}-{rnd.randrange(16 ** 4):04X}"


def generar(fin: dt.date) -> DatosV2:
    movs, _ = D.generar_movimientos(fin)
    rnd = random.Random(2027)
    datos = DatosV2()
    # horas crecientes dentro de cada día (orden de la simulación)
    por_dia: dict[dt.date, int] = {}
    for m in movs:
        por_dia[m.fecha] = por_dia.get(m.fecha, 0) + 1
    usados: dict[dt.date, list[int]] = {}
    for d, n in por_dia.items():
        segs = sorted(rnd.sample(range(7 * 3600 + 30 * 60, 17 * 3600 + 30 * 60), n))
        usados[d] = segs
    idx: dict[dt.date, int] = {}
    for m in movs:
        k = idx.get(m.fecha, 0)
        idx[m.fecha] = k + 1
        ts = dt.datetime.combine(m.fecha, dt.time()) + dt.timedelta(seconds=usados[m.fecha][k])
        sku = m.producto.split(" · ")[0].strip()
        factor = D.FACTOR[m.tipo]
        hoja = S_SAL if m.tipo == "SALIDA" else S_ENT
        if hoja == S_SAL:
            u = VENTAS[rnd.randrange(len(VENTAS))]
        else:
            u = POR_NOMBRE.get(m.responsable)
            if not u or u[2] != "BODEGA":
                u = BODEGA[rnd.randrange(len(BODEGA))]
        fila = {
            "ID": _id(PREFIJO[hoja], ts, rnd), "Tipo": m.tipo, "Fecha": m.fecha,
            "Producto": f"{sku} · {NOMBRE[sku]}", "Cantidad": m.cantidad, "Documento": m.documento,
            "Observaciones": m.observaciones, "CantidadNeta": m.cantidad * factor, "Estado": ESTADO_OK,
            "Registró": u[1], "Unidad": UNIDAD[sku], "FactorStock": factor, "Usuario_O365": u[0], "SKU": sku,
            "Timestamp": ts,
        }
        (datos.salidas if hoja == S_SAL else datos.entradas).append(fila)

    # Instantánea precargada: calculada antes de los últimos movimientos (la portada invita a recalcular)
    todos = sorted(datos.entradas + datos.salidas, key=lambda f: f["Timestamp"])
    corte = todos[-MOVS_TRAS_CALCULO - 1]["Timestamp"] + dt.timedelta(minutes=1)
    prods = productos_dict()
    margen = D.MARGEN_ALERTA

    def a_proy(filas):
        return [{"SKU": f["SKU"], "CantidadNeta": f["CantidadNeta"], "Fecha": serial(f["Fecha"]),
                 "Estado": f["Estado"]} for f in filas]

    datos.stock, datos.alertas, n = proyectar(prods, a_proy([f for f in todos if f["Timestamp"] <= corte]), margen,
                                              fin)
    datos.stock_completo, datos.alertas_completas, _ = proyectar(prods, a_proy(todos), margen, fin)
    datos.meta = {"stkActualizado": corte, "stkActualizadoPor": ADMIN[0], "stkActualizadoNombre": ADMIN[1],
                  "stkMovimientos": n}

    # Capturas de ejemplo (pendientes): muestran «✔ Lista» y el poka-yoke en rojo sangre
    final = {r[0]: r for r in datos.stock_completo}
    agotado = next(r[0] for r in datos.stock_completo if r[12] == "AGOTADO")
    con_stock = next(r[0] for r in datos.stock_completo if r[12] == "ÓPTIMO" and r[8] >= 5
                     and UNIDAD[r[0]] not in D.UNIDADES_DECIMALES)
    ent = orden_captura(S_ENT)
    sal = orden_captura(S_SAL)
    datos.captura = {
        S_ENT: {1 + ent.index(POR_NOMBRE["Ana Gómez"]): {
            "Tipo": "ENTRADA", "Producto": f"{agotado} · {NOMBRE[agotado]}", "Cantidad": 20.0,
            "Documento": "FC-10290", "Observaciones": "Recepción OC-2251"}},
        S_SAL: {
            1 + sal.index(POR_NOMBRE["Carlos Ruiz"]): {
                "Producto": f"{agotado} · {NOMBRE[agotado]}", "Cantidad": 3.0, "Documento": "Constructora Andina"},
            1 + sal.index(POR_NOMBRE["Sofía López"]): {
                "Producto": f"{con_stock} · {NOMBRE[con_stock]}", "Cantidad": 2.0,
                "Documento": "Taller El Progreso"}},
    }
    datos.meta["ejemplo_agotado"] = agotado
    datos.meta["ejemplo_con_stock"] = con_stock
    datos.meta["stock_con_stock"] = final[con_stock][8]
    return datos


# ---------------------------------------------------------------------------
# Fixture para las pruebas de los Office Scripts (tests/office-scripts)
# ---------------------------------------------------------------------------
def _v(x):
    if isinstance(x, dt.datetime) or isinstance(x, dt.date):
        return serial(x)
    return x


def fixture(datos: DatosV2, password: str, fin: dt.date, cfg: dict, tablas_extra: dict) -> dict:
    """Estado del Core como lo verían los scripts (valores ya calculados de las columnas con fórmula)."""
    def captura(hoja):
        usuarios = orden_captura(hoja)
        filas = []
        for n in range(1, MAX_CAPT + 1):
            u = usuarios[n - 1] if n <= len(usuarios) else None
            vals = datos.captura.get(hoja, {}).get(n, {})
            fila = {c: "" for c in CAPTURE_COLS}
            fila.update({k: _v(v) for k, v in vals.items()})
            fila["Usuario"] = u[1] if u else ""
            fila["Correo"] = u[0] if u else ""
            fila["Tipo"] = vals.get("Tipo", "SALIDA" if (hoja == S_SAL and u) else "")
            prod = vals.get("Producto", "")
            fila["SKU"] = prod.split(" · ")[0].strip() if prod else ""
            filas.append([fila[c] for c in CAPTURE_COLS])
        return filas

    def bitacora(filas):
        return [[_v(f[c]) for c in LEDGER_COLS] for f in filas] or [["" for _ in LEDGER_COLS]]

    return {
        "password": password,
        "hoy": fin.isoformat(),
        "tablas": {
            "tblCapturaEntradas": {"hoja": S_ENT, "encabezados": CAPTURE_COLS, "filas": captura(S_ENT)},
            "tblCapturaSalidas": {"hoja": S_SAL, "encabezados": CAPTURE_COLS, "filas": captura(S_SAL)},
            "tblEntradas": {"hoja": S_ENT, "encabezados": LEDGER_COLS, "filas": bitacora(datos.entradas)},
            "tblSalidas": {"hoja": S_SAL, "encabezados": LEDGER_COLS, "filas": bitacora(datos.salidas)},
            **tablas_extra,
        },
        "nombres": {k: {"hoja": "01_CONFIG", "valor": _v(v)} for k, v in cfg.items()},
        "esperado": {"stock": [[_v(x) for x in r] for r in datos.stock_completo],
                     "alertas": [[_v(x) for x in r] for r in datos.alertas_completas],
                     "ejemplo_agotado": datos.meta["ejemplo_agotado"],
                     "ejemplo_con_stock": datos.meta["ejemplo_con_stock"],
                     "stock_con_stock": datos.meta["stock_con_stock"]},
    }
