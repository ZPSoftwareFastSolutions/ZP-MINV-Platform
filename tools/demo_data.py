"""
M-INV V1 · Datos maestros base y dataset de demostración.

- Datos BASE (release): tipos de movimiento, unidades, categorías genéricas.
- Datos DEMO (Core): "Distribuidora Demo S.A.S.", 34 productos y ~6 meses de
  movimientos simulados. La simulación es determinista (semilla fija) salvo por la
  fecha final, que por defecto es la fecha del build para que el tablero luzca vigente.

Invariantes que garantiza la simulación (y que tools/verify_minv.ps1 re-verifica en Excel):
  * El saldo de ningún producto es negativo en ningún momento.
  * Las cantidades respetan las unidades (enteras salvo KG, GL, LT, MT).
  * El estado final incluye todos los semáforos: AGOTADO, CRÍTICO, BAJO, ÓPTIMO,
    SOBRESTOCK e INACTIVO.
"""
from __future__ import annotations

import datetime as dt
import random
from dataclasses import dataclass, replace

# ---------------------------------------------------------------------------
# Catálogos base (aplican a Core y Release)
# ---------------------------------------------------------------------------
# (Tipo, FactorStock, Descripción)
TIPOS_MOVIMIENTO = [
    ("SALDO INICIAL", 1, "Carga del inventario existente al implementar el sistema (una vez por producto)."),
    ("ENTRADA", 1, "Compra, recepción de mercancía o devolución de un cliente."),
    ("SALIDA", -1, "Venta, despacho a cliente o consumo interno."),
    ("AJUSTE (+)", 1, "Sobrante encontrado en conteo físico. Exige observación."),
    ("AJUSTE (-)", -1, "Merma, daño, pérdida o faltante en conteo físico. Exige observación."),
]

# (Código, Unidad, Decimales SI/NO, Descripción)
UNIDADES = [
    ("UND", "Unidad", "NO", "Pieza individual"),
    ("CAJA", "Caja", "NO", "Caja cerrada del proveedor"),
    ("PAQ", "Paquete", "NO", "Paquete o bolsa cerrada"),
    ("PAR", "Par", "NO", "Par (guantes, botas)"),
    ("ROLLO", "Rollo", "NO", "Rollo completo"),
    ("KG", "Kilogramo", "SI", "Peso en kilogramos"),
    ("GL", "Galón", "SI", "Volumen en galones"),
    ("LT", "Litro", "SI", "Volumen en litros"),
    ("MT", "Metro", "SI", "Longitud en metros"),
]
UNIDADES_DECIMALES = {u[0] for u in UNIDADES if u[2] == "SI"}

# (Código, Categoría, Descripción)
CATEGORIAS_BASE = [
    ("MP", "MATERIA PRIMA", "Insumos que se transforman en producto"),
    ("PT", "PRODUCTO TERMINADO", "Artículos listos para la venta"),
    ("INS", "INSUMOS", "Materiales de consumo de la operación"),
    ("REP", "REPUESTOS", "Partes y repuestos de mantenimiento"),
    ("EMP", "EMPAQUES", "Material de empaque y embalaje"),
    ("GEN", "GENERAL", "Artículos sin categoría específica"),
]

# (Nombre, Cargo)
RESPONSABLES_BASE = [
    ("ADMINISTRADOR", "Administrador del sistema"),
    ("BODEGA", "Operador de bodega"),
    ("COMPRAS", "Responsable de compras"),
]

# ---------------------------------------------------------------------------
# Tenant de demostración
# ---------------------------------------------------------------------------
EMPRESA_DEMO = "Distribuidora Demo S.A.S."
NIT_DEMO = "900.123.456-7"
BODEGA_DEMO = "Bodega Principal"

CATEGORIAS_DEMO = [
    ("FER", "FERRETERÍA", "Tornillería, herrajes y herramienta manual"),
    ("ELE", "ELÉCTRICOS", "Cableado, iluminación y protecciones eléctricas"),
    ("PLO", "PLOMERÍA", "Tubería, accesorios y griferías"),
    ("PIN", "PINTURAS", "Pinturas, solventes y accesorios de aplicación"),
    ("SEG", "SEGURIDAD INDUSTRIAL", "Elementos de protección personal (EPP)"),
    ("ASE", "ASEO Y LIMPIEZA", "Químicos e implementos de limpieza"),
]

RESPONSABLES_DEMO = [
    ("Ana Gómez", "Jefe de bodega"),
    ("Carlos Ruiz", "Auxiliar de bodega"),
    ("Laura Méndez", "Analista de compras"),
]


# (Proveedor, NIT, Contacto, Teléfono, Correo, DíasEntrega) — datos ficticios (dominios .example reservados)
PROVEEDORES_DEMO = [
    ("Ferretería Mayorista del Valle", "900.456.123-1", "Jorge Salazar", "602 555 0101", "ventas@mayorista-valle.example", 5),
    ("Electro Suministros S.A.S.", "901.234.567-8", "Paula Rincón", "601 555 0142", "pedidos@electrosuministros.example", 3),
    ("Hidráulicos y PVC Ltda.", "800.765.432-5", "Andrés Mejía", "604 555 0177", "comercial@hidraulicos-pvc.example", 4),
    ("Pinturas Andinas S.A.", "860.111.222-3", "Marcela Ortiz", "601 555 0199", "servicio@pinturas-andinas.example", 7),
    ("Dotaciones Industriales Seguras", "900.888.999-0", "Luis Herrera", "602 555 0123", "ventas@dotaciones-seguras.example", 6),
    ("Químicos del Norte S.A.S.", "901.555.444-2", "Diana Castro", "605 555 0165", "pedidos@quimicos-norte.example", 4),
]


@dataclass(frozen=True)
class Producto:
    sku: str
    nombre: str
    categoria: str
    unidad: str
    minimo: float
    maximo: float
    costo: float
    ubicacion: str
    activo: bool = True
    proveedor: str = ""

    @property
    def etiqueta(self) -> str:
        """Texto que ve el operador en la lista desplegable (SKU · Nombre)."""
        return f"{self.sku} · {self.nombre}"


_CAT = {c[0]: c[1] for c in CATEGORIAS_DEMO}

_PRODUCTOS = [
    Producto("FER-001", 'Tornillo drywall 6x1" (caja x100)', _CAT["FER"], "CAJA", 20, 120, 9800, "A-01-01"),
    Producto("FER-002", 'Chazo plástico 1/4" (bolsa x100)', _CAT["FER"], "PAQ", 15, 80, 6500, "A-01-02"),
    Producto("FER-003", "Martillo uña 16 oz mango fibra", _CAT["FER"], "UND", 5, 30, 28000, "A-02-01"),
    Producto("FER-004", "Cinta métrica 5 m", _CAT["FER"], "UND", 8, 40, 14500, "A-02-02"),
    Producto("FER-005", 'Disco de corte metal 4-1/2"', _CAT["FER"], "UND", 25, 150, 3900, "A-02-03"),
    Producto("FER-006", "Candado de seguridad 40 mm", _CAT["FER"], "UND", 6, 36, 22000, "A-02-04"),
    Producto("FER-007", 'Serrucho 20" (descontinuado)', _CAT["FER"], "UND", 0, 0, 31000, "A-02-05", activo=False),
    Producto("ELE-001", "Cable THHN 12 AWG (rollo 100 m)", _CAT["ELE"], "ROLLO", 4, 20, 185000, "B-01-01"),
    Producto("ELE-002", "Bombillo LED 9 W luz blanca", _CAT["ELE"], "UND", 40, 250, 4200, "B-01-02"),
    Producto("ELE-003", "Toma doble con polo a tierra", _CAT["ELE"], "UND", 20, 120, 7800, "B-01-03"),
    Producto("ELE-004", "Breaker enchufable 1x20 A", _CAT["ELE"], "UND", 10, 60, 16500, "B-02-01"),
    Producto("ELE-005", 'Cinta aislante negra 3/4"', _CAT["ELE"], "UND", 30, 200, 3500, "B-02-02"),
    Producto("ELE-006", "Canaleta plástica 20x12 mm x 2 m", _CAT["ELE"], "UND", 20, 100, 6900, "B-02-03"),
    Producto("ELE-007", "Cable dúplex 2x14 AWG (por metro)", _CAT["ELE"], "MT", 100, 600, 2300, "B-03-01"),
    Producto("PLO-001", 'Tubo PVC presión 1/2" x 6 m', _CAT["PLO"], "UND", 15, 80, 17500, "C-01-01"),
    Producto("PLO-002", 'Codo PVC 1/2" x 90°', _CAT["PLO"], "UND", 50, 300, 900, "C-01-02"),
    Producto("PLO-003", 'Llave de paso 1/2" en bronce', _CAT["PLO"], "UND", 8, 40, 24000, "C-01-03"),
    Producto("PLO-004", "Soldadura líquida PVC 1/4 gal", _CAT["PLO"], "UND", 10, 50, 32000, "C-02-01"),
    Producto("PLO-005", 'Cinta teflón 1/2" x 10 m', _CAT["PLO"], "UND", 40, 250, 1200, "C-02-02"),
    Producto("PIN-001", "Vinilo tipo 1 blanco", _CAT["PIN"], "GL", 12, 60, 58000, "D-01-01"),
    Producto("PIN-002", "Esmalte sintético negro", _CAT["PIN"], "GL", 6, 30, 74000, "D-01-02"),
    Producto("PIN-003", "Thinner corriente", _CAT["PIN"], "GL", 8, 40, 26000, "D-01-03"),
    Producto("PIN-004", 'Rodillo de felpa 9"', _CAT["PIN"], "UND", 10, 60, 12500, "D-02-01"),
    Producto("PIN-005", 'Brocha 3" cerda natural', _CAT["PIN"], "UND", 12, 70, 5800, "D-02-02"),
    Producto("SEG-001", "Guantes de nitrilo", _CAT["SEG"], "PAR", 30, 200, 4500, "E-01-01"),
    Producto("SEG-002", "Casco de seguridad blanco", _CAT["SEG"], "UND", 6, 40, 24500, "E-01-02"),
    Producto("SEG-003", "Gafas de seguridad lente claro", _CAT["SEG"], "UND", 15, 90, 6200, "E-01-03"),
    Producto("SEG-004", "Botas de seguridad punta de acero", _CAT["SEG"], "PAR", 4, 24, 118000, "E-02-01"),
    Producto("SEG-005", "Tapabocas N95 (caja x20)", _CAT["SEG"], "CAJA", 8, 50, 38000, "E-02-02"),
    Producto("ASE-001", "Desengrasante industrial", _CAT["ASE"], "GL", 6, 40, 36000, "F-01-01"),
    Producto("ASE-002", "Hipoclorito de sodio 5%", _CAT["ASE"], "LT", 20, 120, 3800, "F-01-02"),
    Producto("ASE-003", "Bolsa de basura negra 90x110 (paq x10)", _CAT["ASE"], "PAQ", 25, 150, 7200, "F-01-03"),
    Producto("ASE-004", "Trapero industrial en algodón", _CAT["ASE"], "UND", 8, 50, 11500, "F-02-01"),
    Producto("ASE-005", "Detergente en polvo", _CAT["ASE"], "KG", 25, 150, 6800, "F-02-02"),
]

_PROV = [p[0] for p in PROVEEDORES_DEMO]
PROVEEDOR_POR_CATEGORIA = dict(zip([c[1] for c in CATEGORIAS_DEMO], _PROV))
PROVEEDOR_EXCEPCIONES = {"ELE-007": _PROV[0], "ASE-003": _PROV[4]}
PRODUCTOS_DEMO = [replace(p, proveedor=PROVEEDOR_EXCEPCIONES.get(p.sku, PROVEEDOR_POR_CATEGORIA[p.categoria]))
                  for p in _PRODUCTOS]

# Productos de baja rotación: solo se mueven en las primeras semanas (demuestran el KPI "sin rotación").
LENTOS = {"SEG-002", "FER-006"}

# Estado final deseado para la demo (el resto debe terminar ÓPTIMO).
OBJETIVOS = {
    "ELE-001": "AGOTADO", "SEG-004": "AGOTADO", "PIN-002": "AGOTADO",
    "FER-005": "CRÍTICO", "PLO-003": "CRÍTICO", "ASE-002": "CRÍTICO", "SEG-005": "CRÍTICO",
    "ELE-004": "BAJO", "PIN-004": "BAJO", "FER-001": "BAJO",
    "PLO-005": "SOBRESTOCK", "ASE-003": "SOBRESTOCK",
}
MARGEN_ALERTA = 0.20  # debe coincidir con cfgMargenAlerta (01_CONFIG)

CLIENTES = ["Constructora Andina", "Taller El Progreso", "Ferretería La 30", "Obras Civiles del Norte",
            "Mantenimientos Integrales", "Colegio San José"]


@dataclass
class Movimiento:
    fecha: dt.date
    tipo: str
    categoria: str
    producto: str      # etiqueta "SKU · Nombre" (como la elige el operador)
    cantidad: float    # siempre positiva; el signo lo aplica FactorStock
    documento: str
    responsable: str
    observaciones: str


FACTOR = {t[0]: t[1] for t in TIPOS_MOVIMIENTO}


def _redondear(p: Producto, q: float) -> float:
    if p.unidad in UNIDADES_DECIMALES:
        return max(0.5, round(q * 2) / 2)
    return float(max(1, round(q)))


def _es_habil(d: dt.date) -> bool:
    return d.weekday() < 6  # lunes a sábado


def generar_movimientos(fin: dt.date, semilla: int = 2026) -> tuple[list[Movimiento], dict[str, float]]:
    """Simula la operación desde el primer día de hace 5 meses hasta `fin` (inclusive)."""
    rnd = random.Random(semilla)
    mes = fin.month - 5
    anio = fin.year + (mes - 1) // 12
    mes = (mes - 1) % 12 + 1
    inicio = dt.date(anio, mes, 1)

    stock = {p.sku: 0.0 for p in PRODUCTOS_DEMO}
    movs: list[Movimiento] = []
    pendientes: dict[str, tuple[dt.date, float]] = {}
    seq = {"FC": 10230, "RM": 5540, "AJ": 0, "OC": 2210}
    por_sku = {p.sku: p for p in PRODUCTOS_DEMO}

    def registrar(fecha, tipo, p: Producto, cantidad, documento, responsable, obs=""):
        neto = cantidad * FACTOR[tipo]
        assert stock[p.sku] + neto >= -1e-9, f"saldo negativo {p.sku} {fecha}"
        stock[p.sku] = round(stock[p.sku] + neto, 2)
        movs.append(Movimiento(fecha, tipo, p.categoria, p.etiqueta, cantidad, documento, responsable, obs))

    def doc(prefijo):
        seq[prefijo] += 1
        return f"{prefijo}-{seq[prefijo]:03d}" if prefijo == "AJ" else f"{prefijo}-{seq[prefijo]}"

    # Día 0: saldo inicial (conteo físico de implementación)
    for p in PRODUCTOS_DEMO:
        q = 3.0 if not p.activo else _redondear(p, p.maximo * rnd.uniform(0.6, 0.9))
        registrar(inicio, "SALDO INICIAL", p, q, "INV-INICIAL", "Ana Gómez",
                  "Carga inicial (conteo físico de implementación)")

    activos = [p for p in PRODUCTOS_DEMO if p.activo]
    pesos = [p.maximo for p in activos]
    serrucho = por_sku["FER-007"]
    dia = inicio + dt.timedelta(days=1)
    dias_desde_ajuste = 0
    while dia < fin:
        if not _es_habil(dia):
            dia += dt.timedelta(days=1)
            continue
        # 1) Recepciones de órdenes de compra que llegan hoy
        for sku, (llegada, cant) in sorted(pendientes.items()):
            if llegada <= dia:
                p = por_sku[sku]
                registrar(dia, "ENTRADA", p, cant, doc("FC"), rnd.choice(["Ana Gómez", "Ana Gómez", "Laura Méndez"]),
                          f"Recepción {doc('OC')}")
                del pendientes[sku]
        # 2) Ventas / despachos del día (~2-3 por día)
        for _ in range(rnd.choice([1, 2, 2, 3, 3, 4])):
            p = rnd.choices(activos, weights=pesos)[0]
            if p.sku in LENTOS and (dia - inicio).days > 20:
                continue  # baja rotación: sin demanda después de las primeras semanas
            q = _redondear(p, rnd.gauss(0.12 * p.maximo, 0.04 * p.maximo))
            q = min(q, stock[p.sku])
            if q < (0.5 if p.unidad in UNIDADES_DECIMALES else 1):
                continue  # venta perdida por falta de stock
            cliente = rnd.choice(CLIENTES)
            obs = rnd.choice(["", "", f"Cliente: {cliente}", f"Cliente: {cliente}", "Venta mostrador",
                              "Consumo interno mantenimiento"])
            registrar(dia, "SALIDA", p, q, doc("RM"), rnd.choice(["Carlos Ruiz"] * 7 + ["Ana Gómez"] * 3), obs)
            # 3) Punto de reorden: se emite orden de compra que llega en 3-6 días
            if stock[p.sku] <= p.minimo and p.sku not in pendientes:
                cant = _redondear(p, p.maximo - stock[p.sku])
                pendientes[p.sku] = (dia + dt.timedelta(days=rnd.randint(3, 6)), cant)
        # 4) Producto descontinuado: se liquida en el primer mes
        if stock[serrucho.sku] > 0 and (dia - inicio).days in (6, 13, 20):
            registrar(dia, "SALIDA", serrucho, 1.0, doc("RM"), "Carlos Ruiz", "Liquidación producto descontinuado")
        # 5) Ajustes por conteo cíclico cada ~12 días hábiles
        dias_desde_ajuste += 1
        if dias_desde_ajuste >= 12:
            dias_desde_ajuste = 0
            p = rnd.choice([a for a in activos if a.sku not in LENTOS])
            if rnd.random() < 0.75 and stock[p.sku] >= 2:
                q = _redondear(p, rnd.uniform(1, 2))
                registrar(dia, "AJUSTE (-)", p, q, doc("AJ"), "Ana Gómez",
                          rnd.choice(["Merma: producto averiado en bodega", "Faltante en conteo cíclico",
                                      "Empaque dañado en recepción"]))
            else:
                registrar(dia, "AJUSTE (+)", p, _redondear(p, 1), doc("AJ"), "Ana Gómez",
                          rnd.choice(["Sobrante en conteo cíclico", "Devolución interna sin registrar"]))
        dia += dt.timedelta(days=1)

    # Cierre de la demo (fecha `fin`): se lleva cada producto a su semáforo objetivo.
    for p in activos:
        objetivo = OBJETIVOS.get(p.sku, "ÓPTIMO")
        actual = stock[p.sku]
        if objetivo == "AGOTADO":
            meta = 0.0
        elif objetivo == "CRÍTICO":
            meta = _redondear(p, p.minimo * rnd.uniform(0.4, 0.8))
        elif objetivo == "BAJO":
            meta = _redondear(p, p.minimo * (1 + MARGEN_ALERTA / 2))
            if meta <= p.minimo:
                meta = p.minimo + (0.5 if p.unidad in UNIDADES_DECIMALES else 1)
        elif objetivo == "SOBRESTOCK":
            meta = _redondear(p, p.maximo * 1.3)
        else:  # ÓPTIMO: solo se corrige si quedó en zona de alerta o por encima del máximo
            if p.minimo * (1 + MARGEN_ALERTA) < actual <= p.maximo:
                continue
            meta = _redondear(p, p.maximo * 0.75)
        delta = round(meta - actual, 2)
        if delta > 0:
            obs = "Compra por oportunidad de precio (volumen)" if objetivo == "SOBRESTOCK" else f"Recepción {doc('OC')}"
            registrar(fin, "ENTRADA", p, delta, doc("FC"), "Ana Gómez", obs)
        elif delta < 0:
            obs = f"Pedido especial cliente: {rnd.choice(CLIENTES)}"
            registrar(fin, "SALIDA", p, -delta, doc("RM"), "Carlos Ruiz", obs)

    return movs, stock


def estado_esperado(p: Producto, s: float) -> str:
    """Réplica en Python de la regla de semáforo de 15_STOCK (para verificación)."""
    if not p.activo:
        return "INACTIVO"
    if s < 0:
        return "INCONSISTENTE"
    if s == 0:
        return "AGOTADO"
    if s <= p.minimo:
        return "CRÍTICO"
    if s <= p.minimo * (1 + MARGEN_ALERTA):
        return "BAJO"
    if p.maximo > 0 and s > p.maximo:
        return "SOBRESTOCK"
    return "ÓPTIMO"


if __name__ == "__main__":
    movs, stock = generar_movimientos(dt.date.today())
    from collections import Counter
    print(f"{len(movs)} movimientos · tipos: {Counter(m.tipo for m in movs)}")
    print("estados:", Counter(estado_esperado(p, stock[p.sku]) for p in PRODUCTOS_DEMO))
