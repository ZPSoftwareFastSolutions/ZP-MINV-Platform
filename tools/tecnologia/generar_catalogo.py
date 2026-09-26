# -*- coding: utf-8 -*-
"""
M-INV V4.2 · Edición Tecnología — generador y validador del catálogo de prueba del rubro.

Produce catalogo-tecnologia.json (UTF-8) junto a este script con:
  metadatos, unidades, categorias (árbol), especificaciones (heredables), marcas, productos (~155 reales a 2026),
  proveedores (8 mayoristas ficticios .example), categorias_cliente, clientes (30 ficticios .example) y armados de PC
  (6 compatibles + 2 incompatibles para pruebas) con el resultado de las reglas de compatibilidad del diseño §3.

Los datos se cargan igual que la tupla Products de LocalDataSeeder.cs (categoría, nombre, unidad, costo, mínimo,
máximo, popularidad) más la ficha técnica, el perfil de serie/garantía (catalog.product_tech_profiles), la
homologación SIN (SaveProductHomologationCommand: actividad + código de producto SIN) y la clave de ilustración
(como ProductImageLibrary.KindFor, pero explícita por producto).

Precios en bolivianos (Bs) con IVA incluido. Costo = precio de referencia internacional (USD, calle 2026) × tipo de
cambio de referencia de importación (incluye flete, arancel e IVA de importación). Precio = costo / (1 − margen) con
redondeo comercial. Margen bruto = (precio − costo) / precio, siempre entre 15 % y 35 %.

Uso:  python generar_catalogo.py        (genera, valida y escribe el JSON; código de salida 1 si algo falla)
"""
from __future__ import annotations

import csv
import hashlib
import json
import random
import re
import sys
from collections import Counter, OrderedDict
from pathlib import Path

AQUI = Path(__file__).resolve().parents[2] / "src" / "2. Infrastructure" / "MINV.Infrastructure" / "Seeding" / "Tecnologia"
SALIDA = AQUI / "catalogo-tecnologia.json"
CSV_SIN = AQUI / "catalogo-productos-sin-tecnologia.csv"

VERSION = "4.2.0-alpha.1"
TC_COSTO = 9.2          # Bs por USD: costo puesto en tienda (tipo de cambio de mercado + flete + arancel + IVA de importación)
MARGEN_MIN, MARGEN_MAX = 0.15, 0.35
IVA = 0.13

# ================================================================================================ catálogos base
SI_NO = ["Sí", "No"]
CONDICIONES = ["Nuevo", "Reacondicionado", "Usado"]
SOCKETS = ["AM4", "AM5", "LGA1700", "LGA1851"]
TIPOS_RAM = ["DDR4", "DDR5"]
FORMATOS_PLACA = ["E-ATX", "ATX", "Micro-ATX", "Mini-ITX"]
CHIPSETS = ["A520", "B550", "A620", "B650", "B650E", "X670E", "X870", "X870E", "B760", "Z790", "B860", "Z890"]
CERTIFICACIONES = ["80 PLUS", "80 PLUS Bronze", "80 PLUS Gold", "80 PLUS Platinum", "80 PLUS Titanium"]
INTERFACES_ALMAC = ["NVMe PCIe 3.0", "NVMe PCIe 4.0", "NVMe PCIe 5.0", "SATA"]
PANELES = ["IPS", "VA", "OLED", "QD-OLED", "TN"]
RESOLUCIONES = ["1920x1080 (Full HD)", "2560x1440 (QHD)", "3440x1440 (UWQHD)", "3840x2160 (4K UHD)"]
PLATAFORMAS = ["PS4", "PS5", "Xbox Series X", "Xbox Series S", "Xbox One", "Nintendo Switch", "Nintendo Switch 2", "PC"]
CONEXIONES = ["USB", "Inalámbrico 2,4 GHz", "Bluetooth", "Jack 3,5 mm", "USB-C"]
GENEROS = ["Deportes", "Acción", "Aventura", "Carreras", "Shooter", "Plataformas", "Rol (RPG)", "Lucha", "Mundo abierto",
           "Simulación", "Party", "Supervivencia", "Construcción"]
ESRB = ["E", "E10+", "T", "M", "RP"]

IMAGENES = ["gpu", "cpu", "motherboard", "ram", "ssd", "hdd", "psu", "case", "cooler_aio", "cooler_air", "laptop", "desktop",
            "monitor", "keyboard", "mouse", "headset", "mousepad", "webcam", "chair", "ps5", "ps4", "xbox_series_x",
            "xbox_series_s", "switch", "switch2", "controller_ps", "controller_xbox", "joycon", "game_ps", "game_xbox",
            "game_switch", "microsd", "router", "cable", "license", "service"]

CLAVES_COMPAT = ["cpu_socket", "ram_type", "ram_slots", "ram_max_gb", "form_factor", "case_form_factors", "gpu_length_mm",
                 "case_max_gpu_mm", "power_draw_w", "cpu_tdp_w", "psu_watts", "m2_slots", "cooler_sockets", "igpu",
                 "storage_interface", "ram_modules", "ram_capacity_gb"]

UNIDADES = [
    {"codigo": "UND", "nombre": "Unidad", "decimales": False, "descripcion": "Pieza individual (existe desde la V2.1)",
     "codigo_sin": 57, "descripcion_sin": "UNIDAD (BIENES)", "nueva": False},
    {"codigo": "SERV", "nombre": "Servicio", "decimales": False,
     "descripcion": "Servicio prestado en tienda (no maneja existencias)", "codigo_sin": 58,
     "descripcion_sin": "UNIDAD (SERVICIOS)", "nueva": True},
]

# ================================================================================================ categorías (árbol)
# (código, nombre, padre)
CATEGORIAS = [
    ("COMP", "Componentes", None),
    ("CPU", "Procesadores", "COMP"), ("GPU", "Tarjetas de video", "COMP"), ("MB", "Placas madre", "COMP"),
    ("RAM", "Memorias RAM", "COMP"), ("STO", "Almacenamiento", "COMP"), ("PSU", "Fuentes de poder", "COMP"),
    ("CASE", "Gabinetes", "COMP"), ("COOL", "Refrigeración", "COMP"),
    ("PC", "Computadoras", None),
    ("LAPG", "Laptops gamer", "PC"), ("LAPU", "Laptops y ultrabooks", "PC"), ("DESK", "PC de escritorio", "PC"),
    ("MON", "Monitores", None),
    ("PER", "Periféricos", None),
    ("KEY", "Teclados", "PER"), ("MOU", "Mouse", "PER"), ("AUD", "Audífonos y headsets", "PER"), ("PAD", "Mousepads", "PER"),
    ("CAM", "Webcams y micrófonos", "PER"), ("CHA", "Sillas gamer", "PER"),
    ("CON", "Consolas", None),
    ("CPS", "PlayStation", "CON"), ("CXB", "Xbox", "CON"), ("CNS", "Nintendo", "CON"),
    ("JUE", "Videojuegos", None),
    ("ACC", "Accesorios de consola", None),
    ("MAND", "Mandos", "ACC"), ("CARG", "Cargadores y energía", "ACC"), ("ALMC", "Almacenamiento para consola", "ACC"),
    ("RED", "Redes", None),
    ("CAB", "Cables y adaptadores", None),
    ("SOFT", "Software y servicios", None),
    ("LIC", "Licencias de software", "SOFT"), ("SRV", "Servicios técnicos", "SOFT"),
]
PADRE = {c: p for c, _, p in CATEGORIAS}
NOMBRE_CAT = {c: n for c, n, _ in CATEGORIAS}


def cadena(cat: str) -> list[str]:
    """Categoría y sus ancestros (de la raíz a la hoja)."""
    out = []
    while cat is not None:
        out.insert(0, cat)
        cat = PADRE[cat]
    return out


def raiz(cat: str) -> str:
    return cadena(cat)[0]


# ================================================================================================ especificaciones
def S(codigo, nombre, tipo, unidad=None, multi=False, filt=False, key=None, opts=None, req=False):
    return {"codigo": codigo, "nombre": nombre, "unidad": unidad, "tipo": tipo, "multivalor": multi, "filtrable": filt,
            "clave_compatibilidad": key, "obligatoria": req, "opciones": list(opts) if opts else []}


def _pantalla_laptop():
    return [
        S("pantalla", "Pantalla", "numero", "pulgadas", filt=True, req=True),
        S("resolucion_pantalla", "Resolución de pantalla", "texto", req=True),
        S("frecuencia_pantalla", "Frecuencia de pantalla", "numero", "Hz", filt=True, req=True),
        S("panel_pantalla", "Tipo de panel", "opcion", opts=PANELES, filt=True),
        S("peso", "Peso", "numero", "kg"),
        S("idioma_teclado", "Idioma del teclado", "opcion", opts=["Español (latino)", "Español (España)", "Inglés (US)"], filt=True),
    ]


ESPECIFICACIONES = {
    "COMP": [S("condicion", "Condición", "opcion", opts=CONDICIONES, filt=True, req=True)],
    "CPU": [
        S("socket", "Socket", "opcion", key="cpu_socket", opts=SOCKETS, filt=True, req=True),
        S("nucleos", "Núcleos", "numero", filt=True, req=True),
        S("hilos", "Hilos", "numero", req=True),
        S("frecuencia_base", "Frecuencia base", "numero", "GHz"),
        S("frecuencia_turbo", "Frecuencia turbo", "numero", "GHz", filt=True),
        S("cache_l3", "Caché L3", "numero", "MB"),
        S("tdp", "TDP / potencia base", "numero", "W", key="cpu_tdp_w", req=True),
        S("graficos_integrados", "Gráficos integrados", "opcion", key="igpu", opts=SI_NO, filt=True, req=True),
        S("modelo_graficos", "Modelo de gráficos integrados", "texto"),
        S("memoria_soportada", "Memoria soportada", "opcion", multi=True, opts=TIPOS_RAM, filt=True),
        S("disipador_incluido", "Disipador incluido", "opcion", opts=SI_NO, filt=True),
        S("arquitectura", "Arquitectura", "texto"),
    ],
    "GPU": [
        S("fabricante_chip", "Fabricante del chip", "opcion", opts=["NVIDIA", "AMD", "Intel"], filt=True, req=True),
        S("modelo_gpu", "GPU", "texto", filt=True, req=True),
        S("vram", "Memoria de video", "numero", "GB", filt=True, req=True),
        S("tipo_vram", "Tipo de memoria", "opcion", opts=["GDDR6", "GDDR6X", "GDDR7"], filt=True),
        S("bus_memoria", "Bus de memoria", "numero", "bits"),
        S("largo", "Largo de la tarjeta", "numero", "mm", key="gpu_length_mm", req=True),
        S("consumo", "Consumo de la tarjeta", "numero", "W", key="power_draw_w", req=True),
        S("fuente_recomendada", "Fuente recomendada por el fabricante", "numero", "W"),
        S("conectores_energia", "Conectores de energía", "texto"),
        S("ranuras_ocupadas", "Ranuras ocupadas", "numero", "ranuras"),
        S("salidas_video", "Salidas de video", "texto"),
        S("interfaz_pcie", "Interfaz", "opcion", opts=["PCIe 4.0 x8", "PCIe 4.0 x16", "PCIe 5.0 x8", "PCIe 5.0 x16"]),
    ],
    "MB": [
        S("socket", "Socket", "opcion", key="cpu_socket", opts=SOCKETS, filt=True, req=True),
        S("chipset", "Chipset", "opcion", opts=CHIPSETS, filt=True, req=True),
        S("formato", "Formato", "opcion", key="form_factor", opts=FORMATOS_PLACA, filt=True, req=True),
        S("tipo_ram", "Tipo de memoria", "opcion", key="ram_type", opts=TIPOS_RAM, filt=True, req=True),
        S("ranuras_ram", "Ranuras de memoria", "numero", key="ram_slots", req=True),
        S("ram_max", "Memoria máxima", "numero", "GB", key="ram_max_gb", req=True),
        S("ranuras_m2", "Ranuras M.2", "numero", key="m2_slots", filt=True, req=True),
        S("wifi", "Wi-Fi integrado", "opcion", opts=["No", "Wi-Fi 5", "Wi-Fi 6", "Wi-Fi 6E", "Wi-Fi 7"], filt=True),
        S("pcie_gpu", "Ranura para GPU", "opcion", opts=["PCIe 4.0 x16", "PCIe 5.0 x16"]),
    ],
    "RAM": [
        S("tipo_ram", "Tipo de memoria", "opcion", key="ram_type", opts=TIPOS_RAM, filt=True, req=True),
        S("capacidad_total", "Capacidad total del kit", "numero", "GB", key="ram_capacity_gb", filt=True, req=True),
        S("modulos", "Módulos del kit", "numero", key="ram_modules", req=True),
        S("velocidad", "Velocidad", "numero", "MT/s", filt=True, req=True),
        S("latencia_cl", "Latencia CAS (CL)", "numero"),
        S("perfil", "Perfil de overclock", "opcion", opts=["XMP 3.0", "EXPO", "XMP 3.0 y EXPO", "XMP 2.0", "JEDEC"]),
        S("formato_modulo", "Formato del módulo", "opcion", opts=["DIMM", "SO-DIMM"], filt=True, req=True),
        S("rgb", "Iluminación RGB", "opcion", opts=SI_NO, filt=True),
    ],
    "STO": [
        S("tecnologia", "Tecnología", "opcion", opts=["SSD", "HDD"], filt=True, req=True),
        S("interfaz", "Interfaz", "opcion", key="storage_interface", opts=INTERFACES_ALMAC, filt=True, req=True),
        S("formato_unidad", "Formato", "opcion", opts=["M.2 2280", "2,5\"", "3,5\""], filt=True, req=True),
        S("capacidad", "Capacidad", "numero", "GB", filt=True, req=True),
        S("lectura", "Lectura secuencial", "numero", "MB/s"),
        S("escritura", "Escritura secuencial", "numero", "MB/s"),
        S("rpm", "Velocidad de giro", "numero", "rpm"),
        S("tbw", "Resistencia (TBW)", "numero", "TB"),
        S("disipador", "Disipador incluido", "opcion", opts=SI_NO),
    ],
    "PSU": [
        S("potencia", "Potencia", "numero", "W", key="psu_watts", filt=True, req=True),
        S("certificacion", "Certificación", "opcion", opts=CERTIFICACIONES, filt=True, req=True),
        S("modularidad", "Cableado", "opcion", opts=["No modular", "Semimodular", "Modular"], filt=True),
        S("norma_atx", "Norma", "opcion", opts=["ATX 2.x", "ATX 3.0", "ATX 3.1"]),
        S("conector_12v2x6", "Conector 12V-2x6 (16 pines)", "opcion", opts=SI_NO, filt=True),
        S("formato_fuente", "Formato", "opcion", opts=["ATX", "SFX"], filt=True),
    ],
    "CASE": [
        S("formatos_placa", "Formatos de placa soportados", "opcion", multi=True, key="case_form_factors", opts=FORMATOS_PLACA,
          filt=True, req=True),
        S("largo_max_gpu", "Largo máximo de GPU", "numero", "mm", key="case_max_gpu_mm", req=True),
        S("altura_max_disipador", "Altura máxima del disipador", "numero", "mm"),
        S("radiador_max", "Radiador máximo", "numero", "mm"),
        S("tipo_gabinete", "Tipo", "opcion", opts=["Mini Tower", "Mid Tower", "Full Tower", "SFF (compacto)"], filt=True),
        S("ventiladores_incluidos", "Ventiladores incluidos", "numero"),
        S("panel_lateral", "Panel lateral", "opcion", opts=["Vidrio templado", "Malla", "Sólido"]),
        S("color", "Color", "opcion", opts=["Negro", "Blanco"], filt=True),
    ],
    "COOL": [
        S("tipo_refrigeracion", "Tipo", "opcion", opts=["Aire", "Líquida AIO"], filt=True, req=True),
        S("sockets_compatibles", "Sockets compatibles", "opcion", multi=True, key="cooler_sockets", opts=SOCKETS, filt=True,
          req=True),
        S("tdp_soportado", "TDP soportado", "numero", "W"),
        S("tamano_radiador", "Tamaño del radiador", "numero", "mm", filt=True),
        S("altura", "Altura", "numero", "mm"),
        S("rgb", "Iluminación RGB", "opcion", opts=SI_NO, filt=True),
    ],
    "PC": [
        S("condicion", "Condición", "opcion", opts=CONDICIONES, filt=True, req=True),
        S("procesador", "Procesador", "texto", filt=True, req=True),
        S("ram", "Memoria RAM", "numero", "GB", filt=True, req=True),
        S("almacenamiento", "Almacenamiento", "numero", "GB", filt=True, req=True),
        S("grafica", "Gráfica", "texto", filt=True, req=True),
        S("sistema_operativo", "Sistema operativo", "opcion",
          opts=["Windows 11 Home", "Windows 11 Pro", "macOS", "Sin sistema operativo"], filt=True, req=True),
    ],
    "LAPG": _pantalla_laptop(),
    "LAPU": _pantalla_laptop(),
    "DESK": [S("formato_equipo", "Formato", "opcion", opts=["Torre", "Mini PC", "Todo en uno"], filt=True, req=True)],
    "MON": [
        S("condicion", "Condición", "opcion", opts=CONDICIONES, filt=True, req=True),
        S("tamano", "Tamaño", "numero", "pulgadas", filt=True, req=True),
        S("resolucion", "Resolución", "opcion", opts=RESOLUCIONES, filt=True, req=True),
        S("frecuencia", "Frecuencia de actualización", "numero", "Hz", filt=True, req=True),
        S("tipo_panel", "Tipo de panel", "opcion", opts=PANELES, filt=True, req=True),
        S("tiempo_respuesta", "Tiempo de respuesta", "numero", "ms"),
        S("curvo", "Curvo", "opcion", opts=SI_NO, filt=True),
        S("sincronizacion", "Sincronización adaptativa", "opcion", multi=True,
          opts=["G-SYNC", "G-SYNC Compatible", "FreeSync", "FreeSync Premium", "FreeSync Premium Pro", "Adaptive-Sync"]),
        S("hdr", "HDR", "opcion", opts=["No", "HDR10", "DisplayHDR 400", "DisplayHDR 600", "DisplayHDR True Black 400"]),
        S("conexiones", "Conexiones", "texto"),
    ],
    "PER": [
        S("condicion", "Condición", "opcion", opts=CONDICIONES, filt=True, req=True),
        S("color", "Color", "texto", filt=True),
    ],
    "KEY": [
        S("conexion", "Conexión", "opcion", multi=True, opts=CONEXIONES, filt=True, req=True),
        S("tipo_teclado", "Tipo", "opcion", opts=["Mecánico", "Membrana", "Óptico", "Magnético (Hall)"], filt=True, req=True),
        S("switches", "Switches", "texto"),
        S("formato_teclado", "Formato", "opcion", opts=["Completo (100 %)", "TKL (80 %)", "75 %", "65 %", "60 %"], filt=True),
        S("idioma_teclado", "Idioma", "opcion", opts=["Español (latino)", "Español (España)", "Inglés (US)"], filt=True),
        S("rgb", "Iluminación RGB", "opcion", opts=SI_NO, filt=True),
    ],
    "MOU": [
        S("conexion", "Conexión", "opcion", multi=True, opts=CONEXIONES, filt=True, req=True),
        S("dpi_max", "DPI máximo", "numero", "DPI", filt=True),
        S("peso", "Peso", "numero", "g", filt=True),
        S("sensor", "Sensor", "texto"),
        S("botones", "Botones", "numero"),
        S("rgb", "Iluminación RGB", "opcion", opts=SI_NO, filt=True),
    ],
    "AUD": [
        S("conexion", "Conexión", "opcion", multi=True, opts=CONEXIONES, filt=True, req=True),
        S("plataformas", "Plataformas compatibles", "opcion", multi=True, opts=PLATAFORMAS, filt=True),
        S("tipo_sonido", "Sonido", "opcion", opts=["Estéreo", "Envolvente 7.1 virtual", "Audio espacial 3D"]),
        S("microfono", "Micrófono", "opcion", opts=["Desmontable", "Abatible", "Integrado", "Sin micrófono"]),
        S("bateria", "Autonomía de batería", "numero", "h"),
    ],
    "PAD": [
        S("tamano_mousepad", "Tamaño", "opcion", opts=["S", "M", "L", "XL", "XXL (extendido)"], filt=True, req=True),
        S("dimensiones", "Dimensiones", "texto"),
        S("superficie", "Superficie", "opcion", opts=["Tela (control)", "Tela (velocidad)", "Rígida"]),
    ],
    "CAM": [
        S("tipo_dispositivo", "Tipo", "opcion", opts=["Webcam", "Micrófono"], filt=True, req=True),
        S("conexion", "Conexión", "opcion", multi=True, opts=CONEXIONES, req=True),
        S("resolucion_video", "Resolución de video", "opcion", opts=["720p 30 fps", "1080p 30 fps", "1080p 60 fps", "4K 30 fps"],
          filt=True),
        S("patron_polar", "Patrón polar", "texto"),
    ],
    "CHA": [
        S("carga_maxima", "Carga máxima", "numero", "kg", req=True),
        S("tapiz", "Tapiz", "opcion", opts=["Cuero sintético (PU)", "Tela", "Malla"], filt=True),
        S("reclinacion", "Reclinación máxima", "numero", "°"),
        S("apoyabrazos", "Apoyabrazos", "opcion", opts=["2D", "3D", "4D"]),
    ],
    "CON": [
        S("condicion", "Condición", "opcion", opts=CONDICIONES, filt=True, req=True),
        S("plataforma", "Plataforma", "opcion", opts=PLATAFORMAS, filt=True, req=True),
        S("almacenamiento", "Almacenamiento", "numero", "GB", filt=True, req=True),
        S("edicion", "Edición", "opcion", opts=["Estándar", "Digital", "Pro", "OLED", "Lite", "Pack con juego"], filt=True, req=True),
        S("lector_discos", "Lector de discos", "opcion", opts=SI_NO, filt=True, req=True),
        S("resolucion_max", "Resolución máxima", "opcion", opts=["720p", "1080p", "1440p", "4K"], filt=True),
        S("color", "Color", "texto"),
        S("juego_incluido", "Juego incluido", "texto"),
    ],
    "JUE": [
        S("plataforma", "Plataforma", "opcion", opts=PLATAFORMAS, filt=True, req=True),
        S("formato", "Formato", "opcion", opts=["Físico", "Digital"], filt=True, req=True),
        S("edicion", "Edición", "opcion", opts=["Estándar", "Deluxe", "Gold", "Ultimate", "Coleccionista", "Nintendo Switch 2 Edition"],
          filt=True, req=True),
        S("clasificacion_esrb", "Clasificación ESRB", "opcion", opts=ESRB, filt=True, req=True),
        S("genero", "Género", "opcion", multi=True, opts=GENEROS, filt=True, req=True),
        S("idioma", "Idioma", "texto"),
        S("jugadores", "Jugadores", "texto"),
    ],
    "ACC": [
        S("condicion", "Condición", "opcion", opts=CONDICIONES, filt=True, req=True),
        S("plataformas", "Plataformas compatibles", "opcion", multi=True, opts=PLATAFORMAS, filt=True, req=True),
    ],
    "MAND": [
        S("conexion", "Conexión", "opcion", multi=True, opts=CONEXIONES, req=True),
        S("bateria", "Autonomía de batería", "numero", "h"),
        S("color", "Color", "texto", filt=True),
        S("funciones", "Funciones destacadas", "texto"),
    ],
    "CARG": [
        S("tipo_accesorio", "Tipo", "opcion", opts=["Estación de carga", "Batería recargable", "Adaptador de corriente"], filt=True,
          req=True),
        S("capacidad_carga", "Capacidad", "texto"),
    ],
    "ALMC": [
        S("tipo_almacenamiento", "Tipo", "opcion",
          opts=["microSD Express", "microSDXC UHS-I", "Tarjeta de expansión", "SSD NVMe PCIe 4.0 (M.2)"], filt=True, req=True),
        S("capacidad", "Capacidad", "numero", "GB", filt=True, req=True),
        S("lectura", "Lectura secuencial", "numero", "MB/s"),
        S("disipador", "Disipador incluido", "opcion", opts=SI_NO),
    ],
    "RED": [
        S("condicion", "Condición", "opcion", opts=CONDICIONES, filt=True, req=True),
        S("tipo_equipo", "Tipo de equipo", "opcion",
          opts=["Router", "Router 4G/LTE", "Hotspot móvil 4G", "Sistema mesh", "Adaptador USB", "Tarjeta PCIe"], filt=True, req=True),
        S("estandar_wifi", "Estándar Wi-Fi", "opcion", opts=["Wi-Fi 5", "Wi-Fi 6", "Wi-Fi 6E", "Wi-Fi 7"], filt=True, req=True),
        S("velocidad_inalambrica", "Velocidad inalámbrica total", "numero", "Mbps", filt=True),
        S("bandas", "Bandas", "opcion", opts=["Doble banda", "Triple banda"]),
        S("puertos_lan", "Puertos LAN", "numero"),
        S("puerto_wan", "Puerto WAN", "texto"),
        S("red_movil", "Red móvil", "opcion", opts=["No", "4G LTE", "4G+ LTE-A", "5G"], filt=True),
    ],
    "CAB": [
        S("tipo_cable", "Tipo", "opcion",
          opts=["HDMI 2.1", "DisplayPort 1.4", "DisplayPort 2.1", "USB-C a USB-C", "USB-C a HDMI", "Red Cat 6"], filt=True, req=True),
        S("largo", "Largo", "numero", "m", filt=True, req=True),
        S("capacidad_senal", "Capacidad de señal", "texto"),
        S("color", "Color", "texto"),
    ],
    "SOFT": [S("tipo_entrega", "Entrega", "opcion", opts=["Licencia digital (ESD)", "Código digital", "Servicio en tienda"], filt=True,
               req=True)],
    "LIC": [
        S("vigencia", "Vigencia", "opcion", opts=["Perpetua", "12 meses", "3 meses", "1 mes"], filt=True, req=True),
        S("dispositivos", "Dispositivos", "numero"),
        S("plataformas", "Plataformas", "opcion", multi=True, opts=PLATAFORMAS, filt=True),
        S("idioma", "Idioma", "texto"),
    ],
    "SRV": [
        S("duracion_estimada", "Duración estimada", "numero", "h"),
        S("incluye", "Incluye", "texto", req=True),
    ],
}


def specs_de(cat: str) -> "OrderedDict[str, dict]":
    """Especificaciones efectivas de una categoría (heredadas de la raíz a la hoja)."""
    out = OrderedDict()
    for c in cadena(cat):
        for s in ESPECIFICACIONES.get(c, []):
            out[s["codigo"]] = s
    return out


# ================================================================================================ homologación SIN
def cargar_sin() -> "OrderedDict[int, dict]":
    """Productos SIN de las actividades del rubro (código de producto → actividad y descripciones)."""
    out = OrderedDict()
    with CSV_SIN.open(encoding="utf-8-sig", newline="") as f:
        for fila in csv.DictReader(f, delimiter=";"):
            codigo = int(fila["codigo_producto_sin"])
            out[codigo] = OrderedDict(codigo_actividad=int(fila["codigo_actividad"]),
                                      descripcion_actividad=fila["descripcion_actividad"].strip(),
                                      codigo_producto=codigo, descripcion_producto=fila["descripcion_producto"].strip())
    return out


SIN = cargar_sin()

# ================================================================================================ precios
def _paso(p: float) -> int:
    """Escalón del redondeo comercial: decenas (< 1000), cincuentenas (< 5000) y centenas."""
    return 10 if p < 1000 else 50 if p < 5000 else 100


def redondeo_comercial(x: float) -> int:
    """Precio terminado en 9: 349, 1 249, 5 999…"""
    paso = _paso(x)
    return max(paso, int(round(x / paso)) * paso) - 1


def _subir(p: int) -> int:
    k = p + 1
    k += _paso(k)
    while k % _paso(k):
        k += 1
    return k - 1


def _bajar(p: int) -> int:
    k = p + 1
    k -= _paso(k - 1)
    while k % _paso(k):
        k -= 1
    return k - 1


def es_precio_comercial(p: int) -> bool:
    return isinstance(p, int) and (p + 1) % _paso(p + 1) == 0


def margen(costo: float, precio: float) -> float:
    return (precio - costo) / precio


def precio_de(costo: int, objetivo: float) -> int:
    """Precio = costo / (1 − margen objetivo), redondeado comercialmente y ajustado a la banda 15-35 %."""
    p = redondeo_comercial(costo / (1 - objetivo))
    for _ in range(200):
        m = margen(costo, p)
        if m < MARGEN_MIN:
            p = _subir(p)
        elif m > MARGEN_MAX:
            p = _bajar(p)
        else:
            break
    return p


def stock_por_defecto(precio: int, pop: int) -> tuple[int, int]:
    """Mínimo y máximo según el precio (equipos caros: poco stock) y la popularidad (1 a 10)."""
    base = (6, 30)
    for tope, b in ((15000, (1, 3)), (6000, (1, 5)), (2500, (2, 8)), (1000, (3, 12)), (400, (4, 18))):
        if precio >= tope:
            base = b
            break
    f = 0.6 + pop / 10
    mn = max(1, round(base[0] * f))
    return mn, max(mn + 2, round(base[1] * f))


# ================================================================================================ productos
# categoría: (imagen, producto SIN, lleva serie, garantía en meses, margen objetivo)
DEF_CAT = {
    "CPU": ("cpu", 1003586, True, 36, 0.16), "GPU": ("gpu", 1003586, True, 24, 0.16),
    "MB": ("motherboard", 1003586, True, 24, 0.20), "RAM": ("ram", 1003586, True, 36, 0.20),
    "STO": ("ssd", 1001969, True, 36, 0.22), "PSU": ("psu", 1003586, True, 36, 0.22),
    "CASE": ("case", 1003586, False, 12, 0.26), "COOL": ("cooler_air", 1003586, False, 12, 0.26),
    "LAPG": ("laptop", 1001967, True, 12, 0.16), "LAPU": ("laptop", 1001967, True, 12, 0.16),
    "DESK": ("desktop", 1001966, True, 12, 0.20), "MON": ("monitor", 1003591, True, 24, 0.20),
    "KEY": ("keyboard", 1001972, False, 12, 0.30), "MOU": ("mouse", 1001973, False, 12, 0.30),
    "AUD": ("headset", 1003586, False, 12, 0.28), "PAD": ("mousepad", 1003586, False, 6, 0.33),
    "CAM": ("webcam", 1003586, False, 12, 0.28), "CHA": ("chair", 1003586, False, 12, 0.28),
    "CPS": ("ps5", 1003590, True, 12, 0.16), "CXB": ("xbox_series_x", 1003590, True, 12, 0.16),
    "CNS": ("switch", 1003590, True, 12, 0.16), "JUE": ("game_ps", 1003590, False, 0, 0.22),
    "MAND": ("controller_ps", 1003590, True, 12, 0.25), "CARG": ("controller_ps", 1003590, False, 6, 0.30),
    "ALMC": ("microsd", 1001969, False, 24, 0.24), "RED": ("router", 1003589, True, 12, 0.26),
    "CAB": ("cable", 1003586, False, 12, 0.34), "LIC": ("license", 1003249, False, 0, 0.22),
    "SRV": ("service", 1003248, False, 3, 0.32),
}
GARANTIAS_VALIDAS = {0, 3, 6, 12, 24, 36, 60}
IMG_CONSOLA = {"PS5": "ps5", "PS4": "ps4", "Xbox Series X": "xbox_series_x", "Xbox Series S": "xbox_series_s",
               "Nintendo Switch": "switch", "Nintendo Switch 2": "switch2"}
IMG_JUEGO = {"PS5": "game_ps", "PS4": "game_ps", "Xbox Series X": "game_xbox", "Xbox Series S": "game_xbox",
             "Xbox One": "game_xbox", "Nintendo Switch": "game_switch", "Nintendo Switch 2": "game_switch"}

PRODUCTOS: list[dict] = []


def P(cat, sku, nombre, marca, spec, *, usd=None, bs=None, pop=5, img=None, sin=None, serie=None, imei=False, gar=None,
      margen_obj=None, stock=None):
    """Agrega un producto. `usd` = referencia internacional 2026 (el costo en Bs sale de TC_COSTO); `bs` = costo directo."""
    d_img, d_sin, d_serie, d_gar, d_margen = DEF_CAT[cat]
    efectivas = specs_de(cat)
    spec = dict(spec)
    if "condicion" in efectivas:
        spec.setdefault("condicion", "Nuevo")
    if cat == "SRV":
        spec.setdefault("tipo_entrega", "Servicio en tienda")
    valores = OrderedDict((c, spec[c]) for c in efectivas if c in spec)
    valores.update((c, v) for c, v in spec.items() if c not in valores)  # códigos desconocidos: los rechaza la validación
    if img is None:
        if cat == "JUE":
            img = IMG_JUEGO.get(spec.get("plataforma"), d_img)
        elif raiz(cat) == "CON":
            img = IMG_CONSOLA.get(spec.get("plataforma"), d_img)
        elif cat == "COOL":
            img = "cooler_aio" if spec.get("tipo_refrigeracion") == "Líquida AIO" else "cooler_air"
        elif cat == "STO":
            img = "hdd" if spec.get("tecnologia") == "HDD" else "ssd"
        else:
            img = d_img
    costo = int(bs) if bs is not None else int(round(usd * TC_COSTO))
    precio = precio_de(costo, margen_obj or d_margen)
    unidad = "SERV" if cat == "SRV" else "UND"
    lleva = True if imei else (d_serie if serie is None else serie)
    if unidad == "SERV":
        mn, mx = 0, 0
    else:
        mn, mx = stock or stock_por_defecto(precio, pop)
    codigo_sin = sin or d_sin
    fila_sin = SIN.get(codigo_sin, {})
    PRODUCTOS.append(OrderedDict(
        sku=sku, nombre=nombre, categoria=cat, categoria_nombre=NOMBRE_CAT[cat],
        ruta_categoria=" > ".join(NOMBRE_CAT[c] for c in cadena(cat)), marca=marca, unidad=unidad,
        costo=costo, precio=precio, margen_pct=round(margen(costo, precio) * 100, 1), minimo=mn, maximo=mx, popularidad=pop,
        lleva_serie=lleva, tipo_serie=("imei" if imei else "serie") if lleva else None,
        garantia_meses=d_gar if gar is None else gar, especificaciones=valores,
        producto_sin=OrderedDict(codigo_actividad=fila_sin.get("codigo_actividad"), codigo_producto=codigo_sin,
                                 descripcion=fila_sin.get("descripcion_producto")),
        imagen=img, referencia_usd=usd))


SI, NO = "Sí", "No"
FHD, QHD, UWQHD, UHD = RESOLUCIONES
PS_PC = ["PS5", "PC"]
XBOX_TODAS = ["Xbox Series X", "Xbox Series S", "Xbox One", "PC"]
MULTI = ["PC", "PS5", "PS4", "Xbox Series X", "Xbox Series S", "Nintendo Switch"]


def cpu(sku, nombre, marca, usd, pop, socket, nucleos, hilos, base, turbo, l3, tdp, igpu, memoria, disipador, arq):
    spec = {"socket": socket, "nucleos": nucleos, "hilos": hilos, "frecuencia_base": base, "frecuencia_turbo": turbo,
            "cache_l3": l3, "tdp": tdp, "graficos_integrados": SI if igpu else NO, "memoria_soportada": memoria,
            "disipador_incluido": SI if disipador else NO, "arquitectura": arq}
    if igpu:
        spec["modelo_graficos"] = igpu
    P("CPU", sku, nombre, marca, spec, usd=usd, pop=pop)


def gpu(sku, nombre, marca, usd, pop, modelo, vram, tipo, bus, largo, consumo, fuente, conectores, ranuras, salidas, pcie,
        gar=None):
    fabricante = "NVIDIA" if modelo.startswith("GeForce") else "AMD" if modelo.startswith("Radeon") else "Intel"
    P("GPU", sku, nombre, marca, {
        "fabricante_chip": fabricante, "modelo_gpu": modelo, "vram": vram, "tipo_vram": tipo, "bus_memoria": bus,
        "largo": largo, "consumo": consumo, "fuente_recomendada": fuente, "conectores_energia": conectores,
        "ranuras_ocupadas": ranuras, "salidas_video": salidas, "interfaz_pcie": pcie}, usd=usd, pop=pop, gar=gar)


def placa(sku, nombre, marca, usd, pop, socket, chipset, formato, ram, ranuras, ram_max, m2, wifi, pcie, gar=None):
    P("MB", sku, nombre, marca, {
        "socket": socket, "chipset": chipset, "formato": formato, "tipo_ram": ram, "ranuras_ram": ranuras,
        "ram_max": ram_max, "ranuras_m2": m2, "wifi": wifi, "pcie_gpu": pcie}, usd=usd, pop=pop, gar=gar)


def ram(sku, nombre, marca, usd, pop, tipo, total, modulos, velocidad, cl, perfil, formato, rgb):
    P("RAM", sku, nombre, marca, {
        "tipo_ram": tipo, "capacidad_total": total, "modulos": modulos, "velocidad": velocidad, "latencia_cl": cl,
        "perfil": perfil, "formato_modulo": formato, "rgb": SI if rgb else NO}, usd=usd, pop=pop)


def almacenamiento(sku, nombre, marca, usd, pop, tec, interfaz, formato, cap, lectura, escritura=None, tbw=None, rpm=None,
                   disipador=None, gar=None):
    spec = {"tecnologia": tec, "interfaz": interfaz, "formato_unidad": formato, "capacidad": cap, "lectura": lectura}
    for k, v in (("escritura", escritura), ("rpm", rpm), ("tbw", tbw), ("disipador", disipador)):
        if v is not None:
            spec[k] = v
    P("STO", sku, nombre, marca, spec, usd=usd, pop=pop, gar=gar)


def fuente(sku, nombre, marca, usd, pop, w, cert, modular, norma, c16, formato="ATX", gar=None):
    P("PSU", sku, nombre, marca, {
        "potencia": w, "certificacion": cert, "modularidad": modular, "norma_atx": norma,
        "conector_12v2x6": SI if c16 else NO, "formato_fuente": formato}, usd=usd, pop=pop, gar=gar)


def gabinete(sku, nombre, marca, usd, pop, formatos, gpu_max, disip_max, radiador, tipo, ventiladores, panel, color):
    P("CASE", sku, nombre, marca, {
        "formatos_placa": formatos, "largo_max_gpu": gpu_max, "altura_max_disipador": disip_max, "radiador_max": radiador,
        "tipo_gabinete": tipo, "ventiladores_incluidos": ventiladores, "panel_lateral": panel, "color": color},
      usd=usd, pop=pop)


def enfriador(sku, nombre, marca, usd, pop, tipo, sockets, tdp, radiador=None, altura=None, rgb=False, gar=None):
    spec = {"tipo_refrigeracion": tipo, "sockets_compatibles": sockets, "tdp_soportado": tdp, "rgb": SI if rgb else NO}
    if radiador:
        spec["tamano_radiador"] = radiador
    if altura:
        spec["altura"] = altura
    aio = tipo == "Líquida AIO"
    P("COOL", sku, nombre, marca, spec, usd=usd, pop=pop, serie=aio, gar=gar if gar is not None else (36 if aio else 12))


def laptop(cat, sku, nombre, marca, usd, pop, procesador, ram_gb, alm_gb, grafica, so, pulgadas, resolucion, hz, panel, peso,
           idioma):
    P(cat, sku, nombre, marca, {
        "procesador": procesador, "ram": ram_gb, "almacenamiento": alm_gb, "grafica": grafica, "sistema_operativo": so,
        "pantalla": pulgadas, "resolucion_pantalla": resolucion, "frecuencia_pantalla": hz, "panel_pantalla": panel,
        "peso": peso, "idioma_teclado": idioma}, usd=usd, pop=pop)


def monitor(sku, nombre, marca, usd, pop, tamano, resolucion, hz, panel, ms, curvo, sync, hdr, conexiones, gar=None):
    P("MON", sku, nombre, marca, {
        "tamano": tamano, "resolucion": resolucion, "frecuencia": hz, "tipo_panel": panel, "tiempo_respuesta": ms,
        "curvo": SI if curvo else NO, "sincronizacion": sync, "hdr": hdr, "conexiones": conexiones}, usd=usd, pop=pop, gar=gar)


def consola(cat, sku, nombre, marca, usd, pop, plataforma, gb, edicion, lector, resolucion, color, juego=None, condicion="Nuevo",
            gar=None):
    spec = {"condicion": condicion, "plataforma": plataforma, "almacenamiento": gb, "edicion": edicion,
            "lector_discos": SI if lector else NO, "resolucion_max": resolucion, "color": color}
    if juego:
        spec["juego_incluido"] = juego
    P(cat, sku, nombre, marca, spec, usd=usd, pop=pop, gar=gar)


def juego(sku, nombre, marca, usd, pop, plataforma, esrb, generos, jugadores, edicion="Estándar",
          idioma="Español (textos y voces)"):
    P("JUE", sku, nombre, marca, {
        "plataforma": plataforma, "formato": "Físico", "edicion": edicion, "clasificacion_esrb": esrb, "genero": generos,
        "idioma": idioma, "jugadores": jugadores}, usd=usd, pop=pop)


# ------------------------------------------------------------------------------------------------ procesadores
cpu("CPU-AMD-7600", "Procesador AMD Ryzen 5 7600 (AM5, 6 núcleos, 5,1 GHz, con disipador)", "AMD", 185, 9,
    "AM5", 6, 12, 3.8, 5.1, 32, 65, "AMD Radeon Graphics (2 CU)", ["DDR5"], True, "Zen 4 (Raphael)")
cpu("CPU-AMD-9700X", "Procesador AMD Ryzen 7 9700X (AM5, 8 núcleos, 5,5 GHz)", "AMD", 300, 6,
    "AM5", 8, 16, 3.8, 5.5, 32, 65, "AMD Radeon Graphics (2 CU)", ["DDR5"], False, "Zen 5 (Granite Ridge)")
cpu("CPU-AMD-7800X3D", "Procesador AMD Ryzen 7 7800X3D (AM5, 8 núcleos, 3D V-Cache 96 MB)", "AMD", 370, 8,
    "AM5", 8, 16, 4.2, 5.0, 96, 120, "AMD Radeon Graphics (2 CU)", ["DDR5"], False, "Zen 4 con 3D V-Cache")
cpu("CPU-AMD-9800X3D", "Procesador AMD Ryzen 7 9800X3D (AM5, 8 núcleos, 3D V-Cache 96 MB)", "AMD", 470, 9,
    "AM5", 8, 16, 4.7, 5.2, 96, 120, "AMD Radeon Graphics (2 CU)", ["DDR5"], False, "Zen 5 con 3D V-Cache (2.ª gen.)")
cpu("CPU-AMD-9950X3D", "Procesador AMD Ryzen 9 9950X3D (AM5, 16 núcleos, 3D V-Cache 128 MB)", "AMD", 680, 4,
    "AM5", 16, 32, 4.3, 5.7, 128, 170, "AMD Radeon Graphics (2 CU)", ["DDR5"], False, "Zen 5 con 3D V-Cache (2.ª gen.)")
cpu("CPU-AMD-8600G", "Procesador AMD Ryzen 5 8600G (AM5, 6 núcleos, Radeon 760M, con disipador)", "AMD", 190, 6,
    "AM5", 6, 12, 4.3, 5.0, 16, 65, "AMD Radeon 760M", ["DDR5"], True, "Zen 4 (Phoenix)")
cpu("CPU-AMD-5600", "Procesador AMD Ryzen 5 5600 (AM4, 6 núcleos, 4,4 GHz, con disipador)", "AMD", 100, 6,
    "AM4", 6, 12, 3.5, 4.4, 32, 65, None, ["DDR4"], True, "Zen 3 (Vermeer)")
cpu("CPU-INT-14400F", "Procesador Intel Core i5-14400F (LGA1700, 10 núcleos, 4,7 GHz, con disipador)", "Intel", 125, 9,
    "LGA1700", 10, 16, 2.5, 4.7, 20, 65, None, ["DDR4", "DDR5"], True, "Raptor Lake Refresh (6P + 4E)")
cpu("CPU-INT-14700K", "Procesador Intel Core i7-14700K (LGA1700, 20 núcleos, 5,6 GHz)", "Intel", 330, 6,
    "LGA1700", 20, 28, 3.4, 5.6, 33, 125, "Intel UHD Graphics 770", ["DDR4", "DDR5"], False, "Raptor Lake Refresh (8P + 12E)")
cpu("CPU-INT-265K", "Procesador Intel Core Ultra 7 265K (LGA1851, 20 núcleos, 5,5 GHz)", "Intel", 290, 5,
    "LGA1851", 20, 20, 3.9, 5.5, 30, 125, "Intel Graphics (4 núcleos Xe)", ["DDR5"], False, "Arrow Lake-S (8P + 12E)")

# ------------------------------------------------------------------------------------------------ tarjetas de video
NV50 = "1× HDMI 2.1b, 3× DisplayPort 2.1b"
ASUS50 = "2× HDMI 2.1b, 3× DisplayPort 2.1b"
C16 = "1× 16 pines (12V-2x6)"
gpu("GPU-MSI-4060-V2XB", "Tarjeta de video MSI GeForce RTX 4060 VENTUS 2X BLACK 8G OC", "MSI", 290, 9,
    "GeForce RTX 4060", 8, "GDDR6", 128, 199, 115, 550, "1× 8 pines", 2, "1× HDMI 2.1a, 3× DisplayPort 1.4a", "PCIe 4.0 x8")
gpu("GPU-ASU-5060TI-DUAL16", "Tarjeta de video ASUS Dual GeForce RTX 5060 Ti 16 GB GDDR7 OC Edition", "ASUS", 470, 8,
    "GeForce RTX 5060 Ti", 16, "GDDR7", 128, 229, 180, 550, "1× 8 pines", 2.5, NV50, "PCIe 5.0 x8", gar=36)
gpu("GPU-MSI-5070-GTRIO", "Tarjeta de video MSI GeForce RTX 5070 12G GAMING TRIO OC", "MSI", 610, 7,
    "GeForce RTX 5070", 12, "GDDR7", 192, 338, 250, 650, C16, 3, NV50, "PCIe 5.0 x16")
gpu("GPU-ZOT-5070-TWIN", "Tarjeta de video ZOTAC GAMING GeForce RTX 5070 Twin Edge OC 12 GB", "Zotac", 560, 7,
    "GeForce RTX 5070", 12, "GDDR7", 192, 231, 250, 650, C16, 2, NV50, "PCIe 5.0 x16")
gpu("GPU-ASU-5070TI-TUF", "Tarjeta de video ASUS TUF Gaming GeForce RTX 5070 Ti 16 GB GDDR7 OC Edition", "ASUS", 900, 6,
    "GeForce RTX 5070 Ti", 16, "GDDR7", 256, 329, 300, 750, C16, 3.1, ASUS50, "PCIe 5.0 x16", gar=36)
gpu("GPU-GIG-5070TI-GOC", "Tarjeta de video Gigabyte GeForce RTX 5070 Ti GAMING OC 16G", "Gigabyte", 830, 5,
    "GeForce RTX 5070 Ti", 16, "GDDR7", 256, 304, 300, 750, C16, 3, NV50, "PCIe 5.0 x16")
gpu("GPU-MSI-5080-GTRIO", "Tarjeta de video MSI GeForce RTX 5080 16G GAMING TRIO OC", "MSI", 1200, 4,
    "GeForce RTX 5080", 16, "GDDR7", 256, 338, 360, 850, C16, 3, NV50, "PCIe 5.0 x16")
gpu("GPU-ASU-5090-ASTRAL", "Tarjeta de video ASUS ROG Astral GeForce RTX 5090 32 GB GDDR7 OC Edition", "ASUS", 3300, 2,
    "GeForce RTX 5090", 32, "GDDR7", 512, 358, 600, 1000, C16, 3.8, ASUS50, "PCIe 5.0 x16", gar=36)
gpu("GPU-ZOT-5090-SOLID", "Tarjeta de video ZOTAC GAMING GeForce RTX 5090 SOLID OC 32 GB", "Zotac", 2600, 2,
    "GeForce RTX 5090", 32, "GDDR7", 512, 330, 575, 1000, C16, 3.5, NV50, "PCIe 5.0 x16")
gpu("GPU-GIG-RX7600-GOC", "Tarjeta de video Gigabyte Radeon RX 7600 GAMING OC 8G", "Gigabyte", 260, 6,
    "Radeon RX 7600", 8, "GDDR6", 128, 282, 165, 550, "1× 8 pines", 2, "2× HDMI 2.1, 2× DisplayPort 2.1", "PCIe 4.0 x8")
gpu("GPU-GIG-RX9060XT-GOC", "Tarjeta de video Gigabyte Radeon RX 9060 XT GAMING OC 16G", "Gigabyte", 380, 7,
    "Radeon RX 9060 XT", 16, "GDDR6", 128, 281, 160, 550, "1× 8 pines", 2.5, "2× HDMI 2.1b, 2× DisplayPort 2.1a",
    "PCIe 5.0 x16")
gpu("GPU-ASU-RX9070XT-TUF", "Tarjeta de video ASUS TUF Gaming Radeon RX 9070 XT OC Edition 16 GB", "ASUS", 760, 6,
    "Radeon RX 9070 XT", 16, "GDDR6", 256, 331, 340, 850, "3× 8 pines", 3.6, "1× HDMI 2.1b, 3× DisplayPort 2.1a",
    "PCIe 5.0 x16", gar=36)

# ------------------------------------------------------------------------------------------------ placas madre
placa("MB-MSI-B650-GPWIFI", "Placa madre MSI B650 GAMING PLUS WIFI (AM5, ATX, DDR5)", "MSI", 170, 8,
      "AM5", "B650", "ATX", "DDR5", 4, 192, 2, "Wi-Fi 6E", "PCIe 4.0 x16")
placa("MB-ASU-B650-TUFPW", "Placa madre ASUS TUF GAMING B650-PLUS WIFI (AM5, ATX, DDR5)", "ASUS", 190, 7,
      "AM5", "B650", "ATX", "DDR5", 4, 192, 3, "Wi-Fi 6", "PCIe 4.0 x16", gar=36)
placa("MB-GIG-B650M-DS3H", "Placa madre Gigabyte B650M DS3H (AM5, Micro-ATX, DDR5)", "Gigabyte", 125, 8,
      "AM5", "B650", "Micro-ATX", "DDR5", 4, 192, 2, "No", "PCIe 4.0 x16")
placa("MB-MSI-X870-TOMAHAWK", "Placa madre MSI MAG X870 TOMAHAWK WIFI (AM5, ATX, DDR5)", "MSI", 300, 6,
      "AM5", "X870", "ATX", "DDR5", 4, 256, 4, "Wi-Fi 7", "PCIe 5.0 x16")
placa("MB-ASU-X870E-STRIXE", "Placa madre ASUS ROG STRIX X870E-E GAMING WIFI (AM5, ATX, DDR5)", "ASUS", 500, 3,
      "AM5", "X870E", "ATX", "DDR5", 4, 256, 5, "Wi-Fi 7", "PCIe 5.0 x16", gar=36)
placa("MB-MSI-B550M-PVDH", "Placa madre MSI B550M PRO-VDH WIFI (AM4, Micro-ATX, DDR4)", "MSI", 105, 5,
      "AM4", "B550", "Micro-ATX", "DDR4", 4, 128, 2, "Wi-Fi 5", "PCIe 4.0 x16")
placa("MB-GIG-B760M-DS3HD4", "Placa madre Gigabyte B760M DS3H DDR4 (LGA1700, Micro-ATX)", "Gigabyte", 110, 8,
      "LGA1700", "B760", "Micro-ATX", "DDR4", 4, 128, 2, "No", "PCIe 4.0 x16")
placa("MB-ASU-Z790-TUFPW", "Placa madre ASUS TUF GAMING Z790-PLUS WIFI (LGA1700, ATX, DDR5)", "ASUS", 230, 5,
      "LGA1700", "Z790", "ATX", "DDR5", 4, 192, 4, "Wi-Fi 6", "PCIe 5.0 x16", gar=36)
placa("MB-ASU-B860-TUFPW", "Placa madre ASUS TUF GAMING B860-PLUS WIFI (LGA1851, ATX, DDR5)", "ASUS", 210, 5,
      "LGA1851", "B860", "ATX", "DDR5", 4, 256, 3, "Wi-Fi 7", "PCIe 5.0 x16", gar=36)

# ------------------------------------------------------------------------------------------------ memorias
ram("RAM-KNG-D5-16-5600", "Memoria Kingston FURY Beast DDR5 16 GB (2×8 GB) 5600 MT/s CL36", "Kingston", 95, 8,
    "DDR5", 16, 2, 5600, 36, "XMP 3.0", "DIMM", False)
ram("RAM-KNG-D5-32-6000", "Memoria Kingston FURY Beast RGB DDR5 32 GB (2×16 GB) 6000 MT/s CL30 EXPO", "Kingston", 220, 9,
    "DDR5", 32, 2, 6000, 30, "EXPO", "DIMM", True)
ram("RAM-COR-D5-32-6000", "Memoria Corsair Vengeance RGB DDR5 32 GB (2×16 GB) 6000 MT/s CL30", "Corsair", 230, 7,
    "DDR5", 32, 2, 6000, 30, "XMP 3.0", "DIMM", True)
ram("RAM-COR-D5-64-6000", "Memoria Corsair Vengeance DDR5 64 GB (2×32 GB) 6000 MT/s CL30", "Corsair", 430, 4,
    "DDR5", 64, 2, 6000, 30, "XMP 3.0", "DIMM", False)
ram("RAM-COR-D4-16-3200", "Memoria Corsair Vengeance LPX DDR4 16 GB (2×8 GB) 3200 MHz CL16", "Corsair", 65, 7,
    "DDR4", 16, 2, 3200, 16, "XMP 2.0", "DIMM", False)
ram("RAM-KNG-SO5-16-5600", "Memoria para laptop Kingston FURY Impact DDR5 SO-DIMM 16 GB 5600 MT/s", "Kingston", 75, 6,
    "DDR5", 16, 1, 5600, 40, "XMP 3.0", "SO-DIMM", False)

# ------------------------------------------------------------------------------------------------ almacenamiento
almacenamiento("SSD-SAM-990PRO-1TB", "SSD Samsung 990 PRO 1 TB NVMe PCIe 4.0 M.2", "Samsung", 125, 8,
               "SSD", "NVMe PCIe 4.0", "M.2 2280", 1000, 7450, 6900, tbw=600, disipador=NO, gar=60)
almacenamiento("SSD-SAM-990PRO-2TB", "SSD Samsung 990 PRO 2 TB NVMe PCIe 4.0 M.2", "Samsung", 210, 7,
               "SSD", "NVMe PCIe 4.0", "M.2 2280", 2000, 7450, 6900, tbw=1200, disipador=NO, gar=60)
almacenamiento("SSD-SAM-9100PRO-2TB", "SSD Samsung 9100 PRO 2 TB NVMe PCIe 5.0 M.2", "Samsung", 290, 4,
               "SSD", "NVMe PCIe 5.0", "M.2 2280", 2000, 14700, 13400, tbw=1200, disipador=NO, gar=60)
almacenamiento("SSD-WD-SN850X-2TB", "SSD WD_BLACK SN850X 2 TB NVMe PCIe 4.0 M.2", "Western Digital", 180, 6,
               "SSD", "NVMe PCIe 4.0", "M.2 2280", 2000, 7300, 6600, tbw=1200, disipador=NO, gar=60)
almacenamiento("SSD-KNG-NV3-1TB", "SSD Kingston NV3 1 TB NVMe PCIe 4.0 M.2", "Kingston", 75, 10,
               "SSD", "NVMe PCIe 4.0", "M.2 2280", 1000, 6000, 4000, tbw=320, disipador=NO)
almacenamiento("SSD-CRU-P310-1TB", "SSD Crucial P310 1 TB NVMe PCIe 4.0 M.2 2280", "Crucial", 85, 7,
               "SSD", "NVMe PCIe 4.0", "M.2 2280", 1000, 7100, 6000, tbw=220, disipador=NO)
almacenamiento("SSD-CRU-BX500-1TB", 'SSD Crucial BX500 1 TB SATA 2,5"', "Crucial", 75, 6,
               "SSD", "SATA", '2,5"', 1000, 540, 500, tbw=360)
almacenamiento("HDD-SEA-BARRACUDA-2TB", 'Disco duro Seagate BarraCuda 2 TB 7200 rpm SATA 3,5"', "Seagate", 70, 4,
               "HDD", "SATA", '3,5"', 2000, 220, rpm=7200, gar=24)

# ------------------------------------------------------------------------------------------------ fuentes de poder
fuente("PSU-MSI-A650BN", "Fuente de poder MSI MAG A650BN 650 W 80 PLUS Bronze", "MSI", 55, 8,
       650, "80 PLUS Bronze", "No modular", "ATX 2.x", False)
fuente("PSU-COR-RM750X", "Fuente de poder Corsair RM750x 750 W 80 PLUS Gold modular (ATX 3.1)", "Corsair", 110, 8,
       750, "80 PLUS Gold", "Modular", "ATX 3.1", True, gar=60)
fuente("PSU-MSI-A850GL", "Fuente de poder MSI MAG A850GL PCIE5 850 W 80 PLUS Gold modular", "MSI", 105, 7,
       850, "80 PLUS Gold", "Modular", "ATX 3.1", True)
fuente("PSU-COR-RM850X", "Fuente de poder Corsair RM850x 850 W 80 PLUS Gold modular (ATX 3.1)", "Corsair", 135, 6,
       850, "80 PLUS Gold", "Modular", "ATX 3.1", True, gar=60)
fuente("PSU-COR-RM1000X", "Fuente de poder Corsair RM1000x 1000 W 80 PLUS Gold modular (ATX 3.1)", "Corsair", 185, 4,
       1000, "80 PLUS Gold", "Modular", "ATX 3.1", True, gar=60)
fuente("PSU-COR-HX1200I", "Fuente de poder Corsair HX1200i 1200 W 80 PLUS Platinum (ATX 3.1)", "Corsair", 290, 2,
       1200, "80 PLUS Platinum", "Modular", "ATX 3.1", True, gar=60)

# ------------------------------------------------------------------------------------------------ gabinetes
TODAS_PLACAS = ["ATX", "Micro-ATX", "Mini-ITX"]
gabinete("CASE-NZXT-H5FLOW", "Gabinete NZXT H5 Flow (2024) ATX negro", "NZXT", 95, 8,
         TODAS_PLACAS, 400, 165, 360, "Mid Tower", 2, "Vidrio templado", "Negro")
gabinete("CASE-LL-O11DEVO", "Gabinete Lian Li O11 Dynamic EVO negro", "Lian Li", 165, 5,
         ["E-ATX"] + TODAS_PLACAS, 422, 167, 360, "Mid Tower", 0, "Vidrio templado", "Negro")
gabinete("CASE-COR-4000D", "Gabinete Corsair 4000D Airflow negro", "Corsair", 105, 7,
         TODAS_PLACAS, 360, 170, 360, "Mid Tower", 2, "Vidrio templado", "Negro")
gabinete("CASE-CM-NR200P", "Gabinete Cooler Master MasterBox NR200P Mini-ITX", "Cooler Master", 95, 3,
         ["Mini-ITX"], 330, 155, 280, "SFF (compacto)", 2, "Vidrio templado", "Negro")
gabinete("CASE-LL-A3MATX", "Gabinete Lian Li A3-mATX negro", "Lian Li", 75, 5,
         ["Micro-ATX", "Mini-ITX"], 415, 165, 360, "Mini Tower", 0, "Vidrio templado", "Negro")
gabinete("CASE-MSI-FORGE120A", "Gabinete MSI MAG FORGE 120A AIRFLOW (6 ventiladores ARGB)", "MSI", 60, 8,
         TODAS_PLACAS, 330, 160, 240, "Mid Tower", 6, "Vidrio templado", "Negro")

# ------------------------------------------------------------------------------------------------ refrigeración
SOCKETS_ACTUALES = ["AM4", "AM5", "LGA1700", "LGA1851"]
enfriador("COOL-DPC-AK400", "Disipador por aire DeepCool AK400 (120 mm)", "DeepCool", 35, 8,
          "Aire", SOCKETS_ACTUALES, 220, altura=155)
enfriador("COOL-TRT-PA120SE", "Disipador por aire Thermalright Peerless Assassin 120 SE (doble torre)", "Thermalright", 40, 8,
          "Aire", SOCKETS_ACTUALES, 245, altura=155)
enfriador("COOL-NOC-NHL9I17XX", "Disipador de perfil bajo Noctua NH-L9i-17xx chromax.black (solo Intel)", "Noctua", 55, 2,
          "Aire", ["LGA1700", "LGA1851"], 65, altura=37, gar=24)
enfriador("COOL-NZXT-KRAKEN240", "Refrigeración líquida NZXT Kraken 240 (AIO 240 mm, pantalla LCD)", "NZXT", 140, 5,
          "Líquida AIO", SOCKETS_ACTUALES, 250, radiador=240)
enfriador("COOL-COR-NAUTILUS360", "Refrigeración líquida Corsair NAUTILUS 360 RS ARGB (AIO 360 mm)", "Corsair", 110, 6,
          "Líquida AIO", SOCKETS_ACTUALES, 300, radiador=360, rgb=True, gar=60)
enfriador("COOL-MSI-A13-240", "Refrigeración líquida MSI MAG CORELIQUID A13 240 (AIO 240 mm)", "MSI", 75, 6,
          "Líquida AIO", SOCKETS_ACTUALES, 250, radiador=240, rgb=True)

# ------------------------------------------------------------------------------------------------ computadoras
W11H, W11P = "Windows 11 Home", "Windows 11 Pro"
laptop("LAPG", "LAP-ASU-TUFA15-4060", 'Laptop ASUS TUF Gaming A15 FA507NVR (Ryzen 7 7435HS, RTX 4060, 16 GB, 512 GB, 15,6" 144 Hz)',
       "ASUS", 950, 8, "AMD Ryzen 7 7435HS", 16, 512, "NVIDIA GeForce RTX 4060 Laptop 8 GB", W11H, 15.6, "1920x1080", 144,
       "IPS", 2.2, "Inglés (US)")
laptop("LAPG", "LAP-ASU-STRIXG16-5070TI",
       'Laptop ASUS ROG Strix G16 G615LR (Core Ultra 9 275HX, RTX 5070 Ti, 32 GB, 1 TB, 16" 240 Hz)', "ASUS", 2500, 4,
       "Intel Core Ultra 9 275HX", 32, 1000, "NVIDIA GeForce RTX 5070 Ti Laptop 12 GB", W11H, 16, "2560x1600", 240, "IPS",
       2.5, "Inglés (US)")
laptop("LAPG", "LAP-LEN-LOQ15-5060", 'Laptop Lenovo LOQ 15IRX10 (Core i7-13650HX, RTX 5060, 16 GB, 1 TB, 15,6" 144 Hz)',
       "Lenovo", 1150, 7, "Intel Core i7-13650HX", 16, 1000, "NVIDIA GeForce RTX 5060 Laptop 8 GB", W11H, 15.6, "1920x1080",
       144, "IPS", 2.4, "Español (latino)")
laptop("LAPG", "LAP-LEN-LEGIONPRO7-5080",
       'Laptop Lenovo Legion Pro 7i Gen 10 (Core Ultra 9 275HX, RTX 5080, 32 GB, 2 TB, 16" OLED 240 Hz)', "Lenovo", 3300, 2,
       "Intel Core Ultra 9 275HX", 32, 2000, "NVIDIA GeForce RTX 5080 Laptop 16 GB", W11H, 16, "2560x1600", 240, "OLED", 2.6,
       "Inglés (US)")
laptop("LAPG", "LAP-HP-VICTUS15-4050", 'Laptop HP Victus 15 (Ryzen 5 8645HS, RTX 4050, 16 GB, 512 GB, 15,6" 144 Hz)', "HP",
       850, 7, "AMD Ryzen 5 8645HS", 16, 512, "NVIDIA GeForce RTX 4050 Laptop 6 GB", W11H, 15.6, "1920x1080", 144, "IPS",
       2.3, "Español (latino)")
laptop("LAPG", "LAP-HP-OMEN16-5060", 'Laptop HP OMEN 16 (Core Ultra 7 255H, RTX 5060, 16 GB, 1 TB, 16" 165 Hz)', "HP", 1450, 5,
       "Intel Core Ultra 7 255H", 16, 1000, "NVIDIA GeForce RTX 5060 Laptop 8 GB", W11H, 16, "1920x1200", 165, "IPS", 2.4,
       "Español (latino)")
laptop("LAPG", "LAP-ACR-NITROV15-4050",
       'Laptop Acer Nitro V 15 ANV15-51 (Core i5-13420H, RTX 4050, 16 GB, 512 GB, 15,6" 144 Hz)', "Acer", 800, 8,
       "Intel Core i5-13420H", 16, 512, "NVIDIA GeForce RTX 4050 Laptop 6 GB", W11H, 15.6, "1920x1080", 144, "IPS", 2.1,
       "Español (latino)")
laptop("LAPU", "LAP-APL-MBA13-M4", 'Apple MacBook Air 13" M4 (16 GB, 256 GB, medianoche)', "Apple", 1000, 6,
       "Apple M4 (CPU de 10 núcleos)", 16, 256, "GPU Apple M4 de 8 núcleos", "macOS", 13.6, "2560x1664", 60, "IPS", 1.24,
       "Español (latino)")
laptop("LAPU", "LAP-ASU-VIVO15-I5", 'Laptop ASUS Vivobook 15 X1504VA (Core i5-1335U, 16 GB, 512 GB, 15,6")', "ASUS", 550, 7,
       "Intel Core i5-1335U", 16, 512, "Intel Iris Xe Graphics", W11H, 15.6, "1920x1080", 60, "IPS", 1.7, "Español (latino)")
laptop("LAPU", "LAP-LEN-IPS3-15", 'Laptop Lenovo IdeaPad Slim 3 15IRH8 (Core i5-13420H, 16 GB, 512 GB, 15,6")', "Lenovo", 600, 6,
       "Intel Core i5-13420H", 16, 512, "Intel UHD Graphics", W11H, 15.6, "1920x1080", 60, "IPS", 1.62, "Español (latino)")
P("DESK", "DSK-APL-MACMINI-M4", "Apple Mac mini M4 (16 GB, 256 GB)", "Apple", {
    "procesador": "Apple M4 (CPU de 10 núcleos)", "ram": 16, "almacenamiento": 256, "grafica": "GPU Apple M4 de 10 núcleos",
    "sistema_operativo": "macOS", "formato_equipo": "Mini PC"}, usd=600, pop=5)
P("DESK", "DSK-TZG-NOVA-5060TI", "PC Gamer Tech Zone Nova (Ryzen 5 7600, RTX 5060 Ti 16 GB, 32 GB DDR5, 1 TB)", "Tech Zone", {
    "procesador": "AMD Ryzen 5 7600", "ram": 32, "almacenamiento": 1000, "grafica": "NVIDIA GeForce RTX 5060 Ti 16 GB",
    "sistema_operativo": W11H, "formato_equipo": "Torre"}, usd=1400, pop=6)

# ------------------------------------------------------------------------------------------------ monitores
FS_GS = ["FreeSync Premium", "G-SYNC Compatible"]
OLED_SYNC = ["FreeSync Premium Pro", "G-SYNC Compatible"]
monitor("MON-LG-24GS60F", 'Monitor LG UltraGear 24GS60F-B 24" Full HD IPS 180 Hz', "LG", 150, 9,
        23.8, FHD, 180, "IPS", 1, False, FS_GS, "HDR10", "1× HDMI 2.0, 1× DisplayPort 1.4")
monitor("MON-LG-27GS75Q", 'Monitor LG UltraGear 27GS75Q-B 27" QHD IPS 180 Hz', "LG", 230, 8,
        27, QHD, 180, "IPS", 1, False, FS_GS, "HDR10", "2× HDMI 2.0, 1× DisplayPort 1.4")
monitor("MON-SAM-G5-27", 'Monitor curvo Samsung Odyssey G5 27" QHD VA 165 Hz (LS27CG552)', "Samsung", 220, 7,
        27, QHD, 165, "VA", 1, True, ["FreeSync"], "HDR10", "1× HDMI 2.0, 1× DisplayPort 1.2")
monitor("MON-SAM-G6-OLED27", 'Monitor Samsung Odyssey OLED G6 27" QHD 360 Hz (G60SD)', "Samsung", 700, 4,
        27, QHD, 360, "QD-OLED", 0.03, False, OLED_SYNC, "DisplayHDR True Black 400", "2× HDMI 2.1, 1× DisplayPort 1.4")
monitor("MON-ASU-VG249Q3A", 'Monitor ASUS TUF Gaming VG249Q3A 24" Full HD Fast IPS 180 Hz', "ASUS", 140, 8,
        23.8, FHD, 180, "IPS", 1, False, FS_GS, "No", "2× HDMI 2.0, 1× DisplayPort 1.2", gar=36)
monitor("MON-ASU-VG27AQ3A", 'Monitor ASUS TUF Gaming VG27AQ3A 27" QHD Fast IPS 180 Hz', "ASUS", 220, 6,
        27, QHD, 180, "IPS", 1, False, FS_GS, "HDR10", "2× HDMI 2.0, 1× DisplayPort 1.2", gar=36)
monitor("MON-ASU-XG27ACDNG", 'Monitor ASUS ROG Strix OLED XG27ACDNG 27" QHD 360 Hz', "ASUS", 750, 3,
        27, QHD, 360, "QD-OLED", 0.03, False, OLED_SYNC, "DisplayHDR True Black 400",
        "2× HDMI 2.1, 1× DisplayPort 1.4, 1× USB-C", gar=36)
monitor("MON-AOC-24G4", 'Monitor AOC 24G4 24" Full HD IPS 180 Hz', "AOC", 120, 8,
        23.8, FHD, 180, "IPS", 1, False, ["Adaptive-Sync", "G-SYNC Compatible"], "HDR10", "2× HDMI 2.0, 1× DisplayPort 1.4")
monitor("MON-AOC-CU34G2XP", 'Monitor curvo AOC CU34G2XP 34" UWQHD VA 180 Hz', "AOC", 320, 4,
        34, UWQHD, 180, "VA", 1, True, ["Adaptive-Sync"], "HDR10", "2× HDMI 2.0, 2× DisplayPort 1.4")

# ------------------------------------------------------------------------------------------------ periféricos
P("KEY", "KEY-RDG-K552RGB", "Teclado mecánico Redragon Kumara K552 RGB (TKL, Outemu Blue, español)", "Redragon", {
    "color": "Negro", "conexion": ["USB"], "tipo_teclado": "Mecánico", "switches": "Outemu Blue (clicky)",
    "formato_teclado": "TKL (80 %)", "idioma_teclado": "Español (latino)", "rgb": SI}, usd=35, pop=9)
P("KEY", "KEY-HYX-ALLOYOC", "Teclado mecánico HyperX Alloy Origins Core RGB (TKL, HyperX Red)", "HyperX", {
    "color": "Negro", "conexion": ["USB"], "tipo_teclado": "Mecánico", "switches": "HyperX Red (lineal)",
    "formato_teclado": "TKL (80 %)", "idioma_teclado": "Inglés (US)", "rgb": SI}, usd=80, pop=6)
P("KEY", "KEY-LOG-GPROXTKL", "Teclado Logitech G PRO X TKL LIGHTSPEED inalámbrico (GX Brown táctil)", "Logitech", {
    "color": "Negro", "conexion": ["Inalámbrico 2,4 GHz", "Bluetooth", "USB"], "tipo_teclado": "Mecánico",
    "switches": "GX Brown (táctil)", "formato_teclado": "TKL (80 %)", "idioma_teclado": "Inglés (US)", "rgb": SI},
  usd=200, pop=4, serie=True, gar=24)
P("KEY", "KEY-RZR-BWV4X", "Teclado mecánico Razer BlackWidow V4 X (Razer Green, completo)", "Razer", {
    "color": "Negro", "conexion": ["USB"], "tipo_teclado": "Mecánico", "switches": "Razer Green (clicky)",
    "formato_teclado": "Completo (100 %)", "idioma_teclado": "Inglés (US)", "rgb": SI}, usd=110, pop=5, gar=24)
P("KEY", "KEY-LOG-MK270", "Combo inalámbrico Logitech MK270 (teclado y mouse, español)", "Logitech", {
    "color": "Negro", "conexion": ["Inalámbrico 2,4 GHz"], "tipo_teclado": "Membrana", "formato_teclado": "Completo (100 %)",
    "idioma_teclado": "Español (latino)", "rgb": NO}, usd=28, pop=8)
P("MOU", "MOU-LOG-G203", "Mouse gamer Logitech G203 LIGHTSYNC", "Logitech", {
    "color": "Negro", "conexion": ["USB"], "dpi_max": 8000, "peso": 85, "sensor": "Óptico de 8000 DPI", "botones": 6,
    "rgb": SI}, usd=30, pop=10)
P("MOU", "MOU-LOG-GPROXSL2", "Mouse inalámbrico Logitech G PRO X SUPERLIGHT 2 LIGHTSPEED", "Logitech", {
    "color": "Negro", "conexion": ["Inalámbrico 2,4 GHz", "USB-C"], "dpi_max": 32000, "peso": 60, "sensor": "HERO 2",
    "botones": 5, "rgb": NO}, usd=150, pop=5, serie=True, gar=24)
P("MOU", "MOU-RZR-DAV3", "Mouse gamer Razer DeathAdder V3 (59 g)", "Razer", {
    "color": "Negro", "conexion": ["USB"], "dpi_max": 30000, "peso": 59, "sensor": "Razer Focus Pro 30K", "botones": 6,
    "rgb": NO}, usd=70, pop=6, gar=24)
P("MOU", "MOU-HYX-HASTE2", "Mouse gamer HyperX Pulsefire Haste 2 (53 g)", "HyperX", {
    "color": "Negro", "conexion": ["USB"], "dpi_max": 26000, "peso": 53, "sensor": "HyperX 26K", "botones": 6, "rgb": SI},
  usd=50, pop=6, gar=24)
P("MOU", "MOU-RDG-M711", "Mouse gamer Redragon Cobra M711 RGB", "Redragon", {
    "color": "Negro", "conexion": ["USB"], "dpi_max": 10000, "peso": 115, "sensor": "PixArt PMW3325", "botones": 7,
    "rgb": SI}, usd=20, pop=8)
P("AUD", "AUD-HYX-CLOUDII", "Audífonos gamer HyperX Cloud II (7.1 virtual, USB y 3,5 mm)", "HyperX", {
    "color": "Rojo y negro", "conexion": ["USB", "Jack 3,5 mm"], "plataformas": MULTI,
    "tipo_sonido": "Envolvente 7.1 virtual", "microfono": "Desmontable"}, usd=80, pop=8, gar=24)
P("AUD", "AUD-HYX-CLOUDIIIW", "Audífonos inalámbricos HyperX Cloud III Wireless", "HyperX", {
    "color": "Negro", "conexion": ["Inalámbrico 2,4 GHz"], "plataformas": ["PC", "PS5", "PS4", "Nintendo Switch"],
    "tipo_sonido": "Audio espacial 3D", "microfono": "Desmontable", "bateria": 120}, usd=150, pop=5, serie=True, gar=24)
P("AUD", "AUD-LOG-G435", "Audífonos inalámbricos Logitech G435 LIGHTSPEED y Bluetooth", "Logitech", {
    "color": "Negro", "conexion": ["Inalámbrico 2,4 GHz", "Bluetooth"],
    "plataformas": ["PC", "PS5", "PS4", "Nintendo Switch"], "tipo_sonido": "Estéreo", "microfono": "Integrado",
    "bateria": 18}, usd=60, pop=7)
P("AUD", "AUD-SNY-PULSEELITE", "Audífonos inalámbricos Sony PULSE Elite (PS5)", "Sony", {
    "color": "Blanco", "conexion": ["Inalámbrico 2,4 GHz", "Bluetooth", "Jack 3,5 mm"], "plataformas": PS_PC,
    "tipo_sonido": "Audio espacial 3D", "microfono": "Abatible", "bateria": 30}, usd=150, pop=5, serie=True)
P("PAD", "PAD-LOG-G240", "Mousepad Logitech G240 (tela, 340 × 280 mm)", "Logitech", {
    "color": "Negro", "tamano_mousepad": "M", "dimensiones": "340 × 280 × 1 mm", "superficie": "Tela (control)"},
  usd=20, pop=7)
P("PAD", "PAD-RZR-GIGV2XXL", "Mousepad Razer Gigantus V2 XXL (940 × 410 mm)", "Razer", {
    "color": "Negro", "tamano_mousepad": "XXL (extendido)", "dimensiones": "940 × 410 × 4 mm",
    "superficie": "Tela (control)"}, usd=30, pop=6)
P("CAM", "CAM-LOG-C920S", "Webcam Logitech C920s PRO HD 1080p", "Logitech", {
    "color": "Negro", "tipo_dispositivo": "Webcam", "conexion": ["USB"], "resolucion_video": "1080p 30 fps"}, usd=65, pop=7)
P("CAM", "CAM-HYX-QUADCASTS", "Micrófono HyperX QuadCast S RGB USB", "HyperX", {
    "color": "Negro", "tipo_dispositivo": "Micrófono", "conexion": ["USB-C"],
    "patron_polar": "Cardioide, bidireccional, omnidireccional y estéreo"}, usd=130, pop=4, serie=True, gar=24)
P("CHA", "CHA-CGR-ARMORONE", "Silla gamer Cougar Armor One (negro y naranja)", "Cougar", {
    "color": "Negro y naranja", "carga_maxima": 120, "tapiz": "Cuero sintético (PU)", "reclinacion": 180,
    "apoyabrazos": "2D"}, usd=170, pop=4)

# ------------------------------------------------------------------------------------------------ consolas
consola("CPS", "CON-SNY-PS5SLIM", "Consola PlayStation 5 Slim edición estándar (1 TB, con lector)", "Sony", 550, 10,
        "PS5", 1000, "Estándar", True, "4K", "Blanco")
consola("CPS", "CON-SNY-PS5SLIM-DIG", "Consola PlayStation 5 Slim Digital Edition (1 TB, sin lector)", "Sony", 500, 8,
        "PS5", 1000, "Digital", False, "4K", "Blanco")
consola("CPS", "CON-SNY-PS5PRO", "Consola PlayStation 5 Pro (2 TB, sin lector)", "Sony", 750, 5,
        "PS5", 2000, "Pro", False, "4K", "Blanco")
consola("CPS", "CON-SNY-PS4SLIM-1TB-R", "Consola PlayStation 4 Slim 1 TB (reacondicionada)", "Sony", 210, 5,
        "PS4", 1000, "Estándar", True, "1080p", "Negro", condicion="Reacondicionado", gar=6)
consola("CXB", "CON-MS-XSX-1TB", "Consola Xbox Series X 1 TB (con lector)", "Microsoft", 650, 6,
        "Xbox Series X", 1000, "Estándar", True, "4K", "Negro")
consola("CXB", "CON-MS-XSS-512", "Consola Xbox Series S 512 GB (blanca, digital)", "Microsoft", 400, 6,
        "Xbox Series S", 512, "Digital", False, "1440p", "Blanco")
consola("CXB", "CON-MS-XSS-1TB", "Consola Xbox Series S 1 TB (negro carbón, digital)", "Microsoft", 450, 4,
        "Xbox Series S", 1000, "Digital", False, "1440p", "Negro carbón")
consola("CNS", "CON-NIN-SWOLED", "Consola Nintendo Switch – Modelo OLED (64 GB, blanca)", "Nintendo", 400, 7,
        "Nintendo Switch", 64, "OLED", False, "1080p", "Blanco")
consola("CNS", "CON-NIN-SWLITE", "Consola Nintendo Switch Lite (32 GB, turquesa)", "Nintendo", 230, 6,
        "Nintendo Switch", 32, "Lite", False, "720p", "Turquesa")
consola("CNS", "CON-NIN-SW2", "Consola Nintendo Switch 2 (256 GB)", "Nintendo", 450, 9,
        "Nintendo Switch 2", 256, "Estándar", False, "4K", "Negro")
consola("CNS", "CON-NIN-SW2-MKW", "Consola Nintendo Switch 2 + Mario Kart World (pack)", "Nintendo", 500, 9,
        "Nintendo Switch 2", 256, "Pack con juego", False, "4K", "Negro", juego="Mario Kart World (descarga incluida)")

# ------------------------------------------------------------------------------------------------ videojuegos
ACCION_AV = ["Acción", "Aventura"]
juego("JUE-PS5-FC26", "EA SPORTS FC 26 (PS5)", "Electronic Arts", 70, 10, "PS5", "E", ["Deportes"],
      "1-4 local, hasta 22 en línea")
juego("JUE-PS5-SPIDERMAN2", "Marvel's Spider-Man 2 (PS5)", "PlayStation Studios", 50, 7, "PS5", "T",
      ACCION_AV + ["Mundo abierto"], "1 jugador")
juego("JUE-PS5-GOWR", "God of War Ragnarök (PS5)", "PlayStation Studios", 40, 6, "PS5", "M", ACCION_AV, "1 jugador")
juego("JUE-PS5-GT7", "Gran Turismo 7 (PS5)", "PlayStation Studios", 40, 6, "PS5", "E", ["Carreras", "Simulación"],
      "1-2 local, en línea")
juego("JUE-PS5-CODBO7", "Call of Duty: Black Ops 7 (PS5)", "Activision", 70, 8, "PS5", "M", ["Shooter", "Acción"],
      "1 local, multijugador en línea")
juego("JUE-PS4-FC26", "EA SPORTS FC 26 (PS4)", "Electronic Arts", 60, 6, "PS4", "E", ["Deportes"],
      "1-4 local, en línea")
juego("JUE-XSX-HALOINF", "Halo Infinite (Xbox Series X|S y Xbox One)", "Xbox Game Studios", 35, 4, "Xbox Series X", "T",
      ["Shooter", "Acción"], "1 jugador, multijugador en línea")
juego("JUE-XSX-FH5", "Forza Horizon 5 (Xbox Series X|S y Xbox One)", "Xbox Game Studios", 40, 5, "Xbox Series X", "E",
      ["Carreras", "Mundo abierto"], "1 jugador, en línea")
juego("JUE-XSX-CODBO7", "Call of Duty: Black Ops 7 (Xbox Series X|S)", "Activision", 70, 5, "Xbox Series X", "M",
      ["Shooter", "Acción"], "1 local, multijugador en línea")
juego("JUE-NSW-ZELDATOTK", "The Legend of Zelda: Tears of the Kingdom (Nintendo Switch)", "Nintendo", 70, 7,
      "Nintendo Switch", "E10+", ["Aventura", "Acción", "Mundo abierto"], "1 jugador")
juego("JUE-NSW-MK8D", "Mario Kart 8 Deluxe (Nintendo Switch)", "Nintendo", 60, 8, "Nintendo Switch", "E",
      ["Carreras", "Party"], "1-4 local, hasta 12 en línea")
juego("JUE-NSW-SMBWONDER", "Super Mario Bros. Wonder (Nintendo Switch)", "Nintendo", 60, 7, "Nintendo Switch", "E",
      ["Plataformas"], "1-4 local, en línea")
juego("JUE-NS2-MKWORLD", "Mario Kart World (Nintendo Switch 2)", "Nintendo", 80, 9, "Nintendo Switch 2", "E",
      ["Carreras", "Party"], "1-4 local, hasta 24 en línea")
juego("JUE-NS2-DKBANANZA", "Donkey Kong Bananza (Nintendo Switch 2)", "Nintendo", 70, 6, "Nintendo Switch 2", "E10+",
      ["Plataformas", "Aventura"], "1-2 local")
juego("JUE-NS2-ZELDATOTK", "The Legend of Zelda: Tears of the Kingdom – Nintendo Switch 2 Edition", "Nintendo", 80, 5,
      "Nintendo Switch 2", "E10+", ["Aventura", "Acción", "Mundo abierto"], "1 jugador", edicion="Nintendo Switch 2 Edition")

# ------------------------------------------------------------------------------------------------ accesorios de consola
P("MAND", "MAND-SNY-DUALSENSE", "Mando inalámbrico DualSense (blanco)", "Sony", {
    "plataformas": PS_PC, "conexion": ["Bluetooth", "USB-C"], "bateria": 8, "color": "Blanco",
    "funciones": "Retroalimentación háptica y gatillos adaptativos"}, usd=75, pop=10)
P("MAND", "MAND-SNY-DSEDGE", "Mando inalámbrico DualSense Edge", "Sony", {
    "plataformas": PS_PC, "conexion": ["Bluetooth", "USB-C"], "bateria": 6, "color": "Blanco",
    "funciones": "Botones traseros, sticks reemplazables y perfiles personalizados"}, usd=200, pop=4)
P("MAND", "MAND-MS-XBWC", "Control inalámbrico Xbox (Carbon Black)", "Microsoft", {
    "plataformas": XBOX_TODAS, "conexion": ["Inalámbrico 2,4 GHz", "Bluetooth", "USB-C"], "bateria": 40, "color": "Negro",
    "funciones": "Botón Compartir y cruceta híbrida (2 pilas AA)"}, usd=65, pop=7, img="controller_xbox")
P("MAND", "MAND-NIN-JOYCON", "Joy-Con (L)/(R) para Nintendo Switch (rojo neón y azul neón)", "Nintendo", {
    "plataformas": ["Nintendo Switch", "Nintendo Switch 2"], "conexion": ["Bluetooth"], "bateria": 20,
    "color": "Rojo neón y azul neón", "funciones": "Vibración HD y cámara IR de movimiento"}, usd=80, pop=6, img="joycon")
P("MAND", "MAND-NIN-JOYCON2", "Joy-Con 2 (L)/(R) para Nintendo Switch 2", "Nintendo", {
    "plataformas": ["Nintendo Switch 2"], "conexion": ["Bluetooth"], "bateria": 20, "color": "Gris con acentos azul y rojo",
    "funciones": "Función de mouse, botón C para GameChat y vibración HD 2"}, usd=95, pop=7, img="joycon")
P("MAND", "MAND-NIN-PROCON", "Nintendo Switch Pro Controller", "Nintendo", {
    "plataformas": ["Nintendo Switch", "Nintendo Switch 2"], "conexion": ["Bluetooth", "USB-C"], "bateria": 40,
    "color": "Negro", "funciones": "NFC para amiibo y vibración HD"}, usd=70, pop=5, img="controller_xbox")
P("MAND", "MAND-NIN-NS2PRO", "Nintendo Switch 2 Pro Controller", "Nintendo", {
    "plataformas": ["Nintendo Switch 2"], "conexion": ["Bluetooth", "USB-C"], "bateria": 40, "color": "Negro",
    "funciones": "Botones traseros GL/GR, botón C y conector de audio 3,5 mm"}, usd=85, pop=6, img="controller_xbox")
P("CARG", "CARG-SNY-DSCHARGE", "Estación de carga para mandos DualSense (PS5)", "Sony", {
    "plataformas": ["PS5"], "tipo_accesorio": "Estación de carga", "capacidad_carga": "2 mandos DualSense"}, usd=35, pop=5)
P("ALMC", "ALMC-SDK-MSDEX-256", "Tarjeta SanDisk microSD Express 256 GB para Nintendo Switch 2", "SanDisk", {
    "plataformas": ["Nintendo Switch 2"], "tipo_almacenamiento": "microSD Express", "capacidad": 256, "lectura": 880},
  usd=60, pop=8)
P("ALMC", "ALMC-SDK-MSDXC-256", "Tarjeta SanDisk microSDXC 256 GB para Nintendo Switch (UHS-I)", "SanDisk", {
    "plataformas": ["Nintendo Switch"], "tipo_almacenamiento": "microSDXC UHS-I", "capacidad": 256, "lectura": 100},
  usd=30, pop=6)
P("ALMC", "ALMC-SEA-XBEXP-1TB", "Tarjeta de expansión Seagate 1 TB para Xbox Series X|S", "Seagate", {
    "plataformas": ["Xbox Series X", "Xbox Series S"], "tipo_almacenamiento": "Tarjeta de expansión", "capacidad": 1000},
  usd=150, pop=4, img="ssd", serie=True)
P("ALMC", "ALMC-WD-SN850XH-2TB", "SSD WD_BLACK SN850X 2 TB con disipador (compatible con PS5)", "Western Digital", {
    "plataformas": ["PS5", "PC"], "tipo_almacenamiento": "SSD NVMe PCIe 4.0 (M.2)", "capacidad": 2000, "lectura": 7300,
    "disipador": SI}, usd=200, pop=5, img="ssd", serie=True, gar=60)

# ------------------------------------------------------------------------------------------------ redes
P("RED", "RED-TPL-AX55", "Router TP-Link Archer AX55 Wi-Fi 6 AX3000", "TP-Link", {
    "tipo_equipo": "Router", "estandar_wifi": "Wi-Fi 6", "velocidad_inalambrica": 3000, "bandas": "Doble banda",
    "puertos_lan": 4, "puerto_wan": "1× Gigabit", "red_movil": "No"}, usd=80, pop=7, gar=24)
P("RED", "RED-TPL-BE550", "Router TP-Link Archer BE550 Wi-Fi 7 BE9300 tribanda", "TP-Link", {
    "tipo_equipo": "Router", "estandar_wifi": "Wi-Fi 7", "velocidad_inalambrica": 9300, "bandas": "Triple banda",
    "puertos_lan": 4, "puerto_wan": "1× 2,5 Gbps", "red_movil": "No"}, usd=250, pop=3, gar=24)
P("RED", "RED-TPL-DECOX20-2P", "Sistema mesh TP-Link Deco X20 Wi-Fi 6 AX1800 (pack de 2)", "TP-Link", {
    "tipo_equipo": "Sistema mesh", "estandar_wifi": "Wi-Fi 6", "velocidad_inalambrica": 1800, "bandas": "Doble banda",
    "puertos_lan": 2, "puerto_wan": "Autodetección en cualquiera de los 2 puertos Gigabit", "red_movil": "No"},
  usd=120, pop=5, gar=24)
P("RED", "RED-TPL-MR600", "Router 4G+ TP-Link Archer MR600 (LTE Cat6, Wi-Fi AC1200)", "TP-Link", {
    "tipo_equipo": "Router 4G/LTE", "estandar_wifi": "Wi-Fi 5", "velocidad_inalambrica": 1200, "bandas": "Doble banda",
    "puertos_lan": 3, "puerto_wan": "1× Gigabit LAN/WAN", "red_movil": "4G+ LTE-A"}, usd=120, pop=5, imei=True)
P("RED", "RED-TPL-M7450", "Hotspot móvil 4G+ TP-Link M7450 (LTE Cat6, Wi-Fi doble banda)", "TP-Link", {
    "tipo_equipo": "Hotspot móvil 4G", "estandar_wifi": "Wi-Fi 5", "velocidad_inalambrica": 1200, "bandas": "Doble banda",
    "puertos_lan": 0, "red_movil": "4G+ LTE-A"}, usd=100, pop=4, imei=True)

# ------------------------------------------------------------------------------------------------ cables
P("CAB", "CAB-UGR-HDMI21-2M", "Cable UGREEN HDMI 2.1 8K de 2 m (48 Gbps)", "UGREEN", {
    "tipo_cable": "HDMI 2.1", "largo": 2, "capacidad_senal": "48 Gbps: 4K 120 Hz y 8K 60 Hz", "color": "Negro"},
  usd=12, pop=9)
P("CAB", "CAB-UGR-DP14-2M", "Cable UGREEN DisplayPort 1.4 de 2 m", "UGREEN", {
    "tipo_cable": "DisplayPort 1.4", "largo": 2, "capacidad_senal": "32,4 Gbps: 4K 144 Hz y 8K 60 Hz (DSC)",
    "color": "Negro"}, usd=12, pop=7)
P("CAB", "CAB-UGR-USBCHDMI-15", "Cable UGREEN USB-C a HDMI 4K 60 Hz de 1,5 m", "UGREEN", {
    "tipo_cable": "USB-C a HDMI", "largo": 1.5, "capacidad_senal": "4K 60 Hz (DisplayPort Alt Mode)", "color": "Gris"},
  usd=15, pop=6)
P("CAB", "CAB-UGR-CAT6-5M", "Cable de red UGREEN Cat 6 de 5 m", "UGREEN", {
    "tipo_cable": "Red Cat 6", "largo": 5, "capacidad_senal": "Hasta 1 Gbps (250 MHz)", "color": "Negro"}, usd=6, pop=7)

# ------------------------------------------------------------------------------------------------ software y servicios
P("LIC", "LIC-MS-W11HOME", "Windows 11 Home (licencia digital ESD)", "Microsoft", {
    "tipo_entrega": "Licencia digital (ESD)", "vigencia": "Perpetua", "dispositivos": 1, "plataformas": ["PC"],
    "idioma": "Multilenguaje (incluye español)"}, usd=110, pop=7, sin=1003249)
P("LIC", "LIC-MS-W11PRO", "Windows 11 Pro (licencia digital ESD)", "Microsoft", {
    "tipo_entrega": "Licencia digital (ESD)", "vigencia": "Perpetua", "dispositivos": 1, "plataformas": ["PC"],
    "idioma": "Multilenguaje (incluye español)"}, usd=150, pop=5, sin=1003249)
P("LIC", "LIC-MS-M365PER-12M", "Microsoft 365 Personal 12 meses (código digital)", "Microsoft", {
    "tipo_entrega": "Código digital", "vigencia": "12 meses", "dispositivos": 5, "plataformas": ["PC"],
    "idioma": "Multilenguaje (incluye español)"}, usd=100, pop=4, sin=1003250)
P("LIC", "LIC-SNY-PSPLUS-ESS12", "PlayStation Plus Essential 12 meses (código digital)", "Sony", {
    "tipo_entrega": "Código digital", "vigencia": "12 meses", "plataformas": ["PS5", "PS4"]}, usd=80, pop=7, sin=1003590)
P("LIC", "LIC-MS-GPULT-1M", "Xbox Game Pass Ultimate 1 mes (código digital)", "Microsoft", {
    "tipo_entrega": "Código digital", "vigencia": "1 mes", "plataformas": ["Xbox Series X", "Xbox Series S", "PC"]},
  usd=30, pop=6, sin=1003590)
P("SRV", "SRV-ENSAMBLE-PC", "Servicio de ensamblado de PC (armado, cableado y pruebas)", "Tech Zone", {
    "duracion_estimada": 3, "incluye": "Montaje de componentes, gestión de cables, actualización de BIOS, pruebas de estrés "
                                       "y de temperatura"}, bs=170, pop=8, sin=1001981)
P("SRV", "SRV-INSTAL-WIN", "Servicio de instalación de Windows y controladores", "Tech Zone", {
    "duracion_estimada": 1.5, "incluye": "Instalación de Windows 11, controladores, actualizaciones y activación de la "
                                         "licencia del cliente"}, bs=95, pop=7)
P("SRV", "SRV-MANT-PC", "Mantenimiento preventivo de PC gamer (limpieza y pasta térmica)", "Tech Zone", {
    "duracion_estimada": 2, "incluye": "Limpieza interna, cambio de pasta térmica, revisión de ventiladores y de "
                                       "temperaturas"}, bs=140, pop=6)
P("SRV", "SRV-MANT-CONSOLA", "Mantenimiento de consola (limpieza interna y pasta térmica)", "Tech Zone", {
    "duracion_estimada": 2, "incluye": "Apertura, limpieza de ventilador y disipador, pasta térmica según el modelo y "
                                       "prueba de temperatura"}, bs=170, pop=6)

POR_SKU = {p["sku"]: p for p in PRODUCTOS}

# ================================================================================================ marcas
MARCAS_PROPIAS = {"Tech Zone"}  # marca de la tienda de prueba (ficticia)


def slug(texto: str) -> str:
    tabla = str.maketrans("áéíóúüñÁÉÍÓÚÜÑ", "aeiouunAEIOUUN")
    return "".join(ch for ch in texto.translate(tabla).lower() if ch.isalnum())


def marcas() -> list[dict]:
    cuenta = Counter(p["marca"] for p in PRODUCTOS)
    return [OrderedDict(codigo=slug(m).upper()[:12], nombre=m, productos=n, propia=m in MARCAS_PROPIAS)
            for m, n in sorted(cuenta.items(), key=lambda kv: kv[0].lower())]


# ================================================================================================ proveedores (ficticios)
def _digitos(semilla: str, n: int) -> str:
    """Número determinista de n dígitos (no empieza con 0) a partir de un texto."""
    h = int(hashlib.sha256(semilla.encode("utf-8")).hexdigest(), 16)
    return str(10 ** (n - 1) + h % (9 * 10 ** (n - 1)))


# (razón social, dominio .example, contacto, ciudad, días de entrega, condición de pago, categorías)
PROVEEDORES_DEF = [
    ("Andes Cómputo Mayorista S.R.L.", "andescomputo.example", "Rodrigo Mamani Aguilar", "La Paz", 3, "Crédito 30 días",
     ["CPU", "MB", "RAM", "STO"]),
    ("GráficaPro Distribuciones S.A.", "graficapro.example", "Verónica Salvatierra Roca", "Santa Cruz", 5, "Crédito 15 días",
     ["GPU", "PSU"]),
    ("Chasis y Frío Importaciones Ltda.", "chasisyfrio.example", "Marco Antonio Lima Pinto", "Cochabamba", 4, "Contado",
     ["CASE", "COOL"]),
    ("Portátiles del Oriente S.R.L.", "portatilesoriente.example", "Carla Ribera Méndez", "Santa Cruz", 6, "Crédito 30 días",
     ["PC"]),
    ("Pantallas Illimani S.R.L.", "pantallasillimani.example", "Javier Tórrez Callisaya", "La Paz", 4, "Crédito 30 días",
     ["MON"]),
    ("Periféricos Tunari S.R.L.", "perifericostunari.example", "Paola Rocha Vargas", "Cochabamba", 3, "Contado", ["PER"]),
    ("Consolas y Juegos Pacífico S.A.", "consolaspacifico.example", "Hugo Saavedra Ortiz", "Santa Cruz", 7,
     "Crédito 15 días", ["CON", "JUE", "ACC", "LIC"]),
    ("Conecta Redes y Software S.R.L.", "conectaredes.example", "Natalia Pérez Arancibia", "La Paz", 2, "Contado",
     ["RED", "CAB", "LIC"]),
]


def _cubre(cats: list[str], cat: str) -> bool:
    return any(c in cats for c in cadena(cat))


def proveedores() -> list[dict]:
    out = []
    for i, (nombre, dominio, contacto, ciudad, dias, pago, cats) in enumerate(PROVEEDORES_DEF, start=1):
        marcas_p = sorted({p["marca"] for p in PRODUCTOS if _cubre(cats, p["categoria"]) and p["marca"] not in MARCAS_PROPIAS},
                          key=str.lower)
        out.append(OrderedDict(
            codigo=f"PROV-{i:02d}", razon_social=nombre, nit=_digitos("nit-prov-" + nombre, 10), contacto=contacto,
            telefono="7" + _digitos("tel-prov-" + nombre, 7), email=f"ventas@{dominio}", sitio_web=f"https://www.{dominio}",
            ciudad=ciudad, dias_entrega=dias, condicion_pago=pago, categorias=cats, marcas=marcas_p, ficticio=True))
    return out


# ================================================================================================ clientes (ficticios)
CATEGORIAS_CLIENTE = [
    OrderedDict(codigo="GENERAL", nombre="Consumidor final", descripcion="Venta de mostrador (existe desde la V2.1)",
                nueva=False),
    OrderedDict(codigo="GAMER", nombre="Gamer frecuente", descripcion="Personas con CI; acumulan compras y garantías",
                nueva=True),
    OrderedDict(codigo="EMPRESA", nombre="Empresa / corporativo", descripcion="Empresas con NIT: oficinas, estudios y "
                                                                              "cibercafés", nueva=True),
    OrderedDict(codigo="EDUCACION", nombre="Colegios e institutos", descripcion="Unidades educativas con NIT (laboratorios "
                                                                                "de computación)", nueva=True),
]
CIUDAD_SUC = {"La Paz": ("CM", "LP"), "Cochabamba": ("CB", "CB"), "Santa Cruz": ("SC", "SC")}

# (nombre, ciudad, plataformas de interés)
GAMERS = [
    ("Diego Alejandro Mamani Rojas", "La Paz", ["PC", "PS5"]), ("Valeria Choque Fernández", "La Paz", ["Nintendo Switch 2"]),
    ("Sebastián Quispe Villca", "La Paz", ["PC"]), ("Camila Andrea Gutiérrez Salazar", "La Paz", ["PS5"]),
    ("Mateo Condori Arce", "La Paz", ["Xbox Series X", "PC"]), ("Rodrigo Flores Ticona", "La Paz", ["PC"]),
    ("Luciana Vargas Cuéllar", "Santa Cruz", ["PS5", "Nintendo Switch"]), ("Joaquín Rivero Suárez", "Santa Cruz", ["PC"]),
    ("Daniela Añez Justiniano", "Santa Cruz", ["Nintendo Switch 2"]), ("Nicolás Paz Ribera", "Santa Cruz", ["PS5", "PC"]),
    ("Isabella Chávez Moreno", "Santa Cruz", ["Xbox Series S"]), ("Fernanda Soliz Terrazas", "Cochabamba", ["PC"]),
    ("Andrés Camacho Zurita", "Cochabamba", ["PS5"]), ("Gabriel Montaño Heredia", "Cochabamba", ["PC", "Nintendo Switch"]),
    ("Sofía Coca Almaraz", "Cochabamba", ["Nintendo Switch 2", "PS5"]), ("Tomás Guzmán Orellana", "Cochabamba", ["PS4"]),
]
# (razón social, dominio, ciudad, rubro)
EMPRESAS = [
    ("Ciber Nébula Play S.R.L.", "cibernebula.example", "La Paz", "Cibercafé gamer"),
    ("Estudio Creativo Píxel Andino S.R.L.", "pixelandino.example", "La Paz", "Diseño 3D y render"),
    ("Consultora Cóndor Analítica S.R.L.", "condoranalitica.example", "La Paz", "Consultoría (equipos de oficina)"),
    ("Contadores Integrales Chuquiago S.R.L.", "chuquiagocontadores.example", "La Paz", "Contabilidad"),
    ("Streaming House Kantuta Ltda.", "kantutastreaming.example", "Santa Cruz", "Creadores de contenido"),
    ("Colibrí Neón Agencia Digital S.R.L.", "colibrineon.example", "Santa Cruz", "Marketing digital"),
    ("Inmobiliaria Torre Guapurú S.A.", "torreguapuru.example", "Santa Cruz", "Inmobiliaria"),
    ("Taller de Arquitectura Yungas Digital S.R.L.", "yungasdigital.example", "Cochabamba", "Arquitectura (CAD/BIM)"),
    ("Llajta Píxel eSports S.R.L.", "llajtapixel.example", "Cochabamba", "Equipo de eSports"),
]
COLEGIOS = [
    ("Unidad Educativa Nuevo Horizonte Digital", "nuevohorizontedigital.example", "La Paz"),
    ("Colegio Técnico Cumbres del Sur", "cumbresdelsur.example", "La Paz"),
    ("Instituto Tecnológico Bit Andino", "bitandino.example", "Cochabamba"),
    ("Unidad Educativa Aurora del Valle Alto", "auroravallealto.example", "Cochabamba"),
    ("Colegio Bilingüe Kantuta Azul", "kantutaazul.example", "Santa Cruz"),
]


def clientes() -> list[dict]:
    out = []

    def add(tipo, nombre, doc_tipo, doc, ext, email, ciudad, categoria, extra):
        n = len(out) + 1
        suc, _ = CIUDAD_SUC[ciudad]
        telefono = ("6" if n % 3 == 0 else "7") + _digitos(f"tel-cli-{nombre}", 7)
        out.append(OrderedDict(codigo=f"CLI-{n:03d}", tipo=tipo, nombre=nombre, tipo_documento=doc_tipo,
                               numero_documento=doc, extension=ext, email=email, telefono=telefono, ciudad=ciudad,
                               sucursal=suc, categoria=categoria, **extra, ficticio=True))

    for i, (nombre, ciudad, plataformas) in enumerate(GAMERS, start=1):
        partes = nombre.split()
        usuario = slug(partes[0]) + "." + slug(partes[-2])
        add("persona", nombre, "CI", _digitos("ci-" + nombre, 7), CIUDAD_SUC[ciudad][1], f"{usuario}{i:02d}@correo.example",
            ciudad, "GAMER", {"plataformas_interes": plataformas})
    for nombre, dominio, ciudad, rubro in EMPRESAS:
        add("empresa", nombre, "NIT", _digitos("nit-cli-" + nombre, 10), None, f"compras@{dominio}", ciudad, "EMPRESA",
            {"rubro": rubro})
    for nombre, dominio, ciudad in COLEGIOS:
        add("institucion_educativa", nombre, "NIT", _digitos("nit-cli-" + nombre, 10), None, f"administracion@{dominio}",
            ciudad, "EDUCACION", {"rubro": "Educación (laboratorio de computación)"})
    return out


# ================================================================================================ armador de PC (§3)
RANURAS = OrderedDict([("cpu", "CPU"), ("placa", "MB"), ("ram", "RAM"), ("gpu", "GPU"), ("almacenamiento", "STO"),
                       ("fuente", "PSU"), ("gabinete", "CASE"), ("refrigeracion", "COOL"), ("extra", None)])
RANURAS_UNICAS = {"cpu", "placa", "fuente", "gabinete", "refrigeracion"}
OBLIGATORIAS = [("cpu", "procesador"), ("placa", "placa madre"), ("ram", "memoria RAM"),
                ("almacenamiento", "almacenamiento"), ("fuente", "fuente de poder"), ("gabinete", "gabinete")]
CONSUMO_RESTO_W = 75
FACTOR_FUENTE = 1.3

REGLAS = [
    ("SOCKET_CPU_PLACA", "error", "Socket de la CPU = socket de la placa"),
    ("TIPO_RAM", "error", "Tipo de RAM (DDR4/DDR5) = tipo de la placa"),
    ("RANURAS_RAM", "error", "Módulos de RAM ≤ ranuras de la placa"),
    ("CAPACIDAD_RAM", "error", "Capacidad total de RAM ≤ máxima de la placa"),
    ("FORMATO_GABINETE", "error", "Formato de la placa ∈ formatos del gabinete"),
    ("LARGO_GPU", "error", "Largo de la GPU ≤ largo máximo de GPU del gabinete"),
    ("POTENCIA_FUENTE", "error", "Potencia de la fuente ≥ consumo estimado (TDP CPU + consumo GPU + 75 W)"),
    ("POTENCIA_RECOMENDADA", "aviso", "Potencia de la fuente ≥ consumo estimado × 1,3"),
    ("RANURAS_M2", "error", "Unidades M.2 (NVMe) ≤ ranuras M.2 de la placa"),
    ("SOCKET_REFRIGERACION", "error", "Enfriador compatible con el socket de la CPU"),
    ("SIN_GRAFICOS", "aviso", "CPU sin gráficos integrados y sin GPU"),
    ("PIEZA_FALTANTE", "aviso", "Falta una pieza obligatoria (CPU, placa, RAM, almacenamiento, fuente, gabinete)"),
]


def valor_clave(prod: dict, clave: str):
    """Valor de la especificación marcada con esa clave de compatibilidad (como lo haría PcCompatibility)."""
    for s in specs_de(prod["categoria"]).values():
        if s["clave_compatibilidad"] == clave:
            return prod["especificaciones"].get(s["codigo"])
    return None


def evaluar_armado(lineas: list[tuple[str, dict, int]]) -> OrderedDict:
    errores, avisos = [], []

    def err(regla, msg):
        errores.append(OrderedDict(regla=regla, mensaje=msg))

    def av(regla, msg):
        avisos.append(OrderedDict(regla=regla, mensaje=msg))

    def una(ranura):
        return next((p for r, p, _ in lineas if r == ranura), None)

    def varias(ranura):
        return [(p, q) for r, p, q in lineas if r == ranura]

    cpu_p, placa_p, gab, psu, frio = una("cpu"), una("placa"), una("gabinete"), una("fuente"), una("refrigeracion")
    rams, gpus, unidades = varias("ram"), varias("gpu"), varias("almacenamiento")
    k = valor_clave

    if cpu_p and placa_p and k(cpu_p, "cpu_socket") != k(placa_p, "cpu_socket"):
        err("SOCKET_CPU_PLACA", f"La CPU es {k(cpu_p, 'cpu_socket')} y la placa es {k(placa_p, 'cpu_socket')}.")
    if placa_p:
        for p, _ in rams:
            if k(p, "ram_type") != k(placa_p, "ram_type"):
                err("TIPO_RAM", f"{p['sku']} es {k(p, 'ram_type')} y la placa admite {k(placa_p, 'ram_type')}.")
        modulos = sum(k(p, "ram_modules") * q for p, q in rams)
        if modulos > k(placa_p, "ram_slots"):
            err("RANURAS_RAM", f"{modulos} módulos de RAM y la placa tiene {k(placa_p, 'ram_slots')} ranuras.")
        total = sum(k(p, "ram_capacity_gb") * q for p, q in rams)
        if total > k(placa_p, "ram_max_gb"):
            err("CAPACIDAD_RAM", f"{total} GB de RAM y la placa admite hasta {k(placa_p, 'ram_max_gb')} GB.")
        m2 = sum(q for p, q in unidades if str(k(p, "storage_interface")).startswith("NVMe"))
        if m2 > k(placa_p, "m2_slots"):
            err("RANURAS_M2", f"{m2} unidades M.2 y la placa tiene {k(placa_p, 'm2_slots')} ranuras M.2.")
        if gab and k(placa_p, "form_factor") not in k(gab, "case_form_factors"):
            err("FORMATO_GABINETE", f"La placa es {k(placa_p, 'form_factor')} y el gabinete admite "
                                    f"{', '.join(k(gab, 'case_form_factors'))}.")
    if gab:
        for p, _ in gpus:
            if k(p, "gpu_length_mm") > k(gab, "case_max_gpu_mm"):
                err("LARGO_GPU", f"La GPU mide {k(p, 'gpu_length_mm')} mm y el gabinete admite {k(gab, 'case_max_gpu_mm')} mm.")
    consumo = (k(cpu_p, "cpu_tdp_w") if cpu_p else 0) + sum(k(p, "power_draw_w") * q for p, q in gpus) + CONSUMO_RESTO_W
    recomendado = int(-(-consumo * FACTOR_FUENTE // 1))
    if psu:
        w = k(psu, "psu_watts")
        if w < consumo:
            err("POTENCIA_FUENTE", f"La fuente es de {w} W y el consumo estimado es {consumo} W.")
        elif w < consumo * FACTOR_FUENTE:
            av("POTENCIA_RECOMENDADA", f"La fuente es de {w} W; se recomiendan {recomendado} W o más.")
    if frio and cpu_p and k(cpu_p, "cpu_socket") not in k(frio, "cooler_sockets"):
        err("SOCKET_REFRIGERACION", f"El enfriador admite {', '.join(k(frio, 'cooler_sockets'))} y la CPU es "
                                    f"{k(cpu_p, 'cpu_socket')}.")
    if cpu_p and k(cpu_p, "igpu") == NO and not gpus:
        av("SIN_GRAFICOS", "La CPU no tiene gráficos integrados y el armado no lleva tarjeta de video.")
    for ranura, nombre in OBLIGATORIAS:
        if not any(r == ranura for r, _, _ in lineas):
            av("PIEZA_FALTANTE", f"Falta la pieza obligatoria: {nombre}.")
    return OrderedDict(compatible=not errores, errores=errores, avisos=avisos, consumo_estimado_w=consumo,
                       potencia_recomendada_w=recomendado, potencia_fuente_w=k(psu, "psu_watts") if psu else None)


ARMADOS_DEF = [
    dict(numero="ARM-CM-000001", perfil="entrada", nombre="PC Gamer Entrada 1080p (Core i5-14400F + RTX 4060)",
         cliente="CLI-001", vigencia_dias=7, estado="Cotizada", esperado="compatible",
         lineas=[("cpu", "CPU-INT-14400F", 1), ("placa", "MB-GIG-B760M-DS3HD4", 1), ("ram", "RAM-COR-D4-16-3200", 1),
                 ("gpu", "GPU-MSI-4060-V2XB", 1), ("almacenamiento", "SSD-KNG-NV3-1TB", 1), ("fuente", "PSU-MSI-A650BN", 1),
                 ("gabinete", "CASE-MSI-FORGE120A", 1), ("extra", "LIC-MS-W11HOME", 1), ("extra", "SRV-ENSAMBLE-PC", 1)]),
    dict(numero="ARM-CM-000002", perfil="media", nombre="PC Gamer Media 1440p (Ryzen 5 7600 + RTX 5060 Ti 16 GB)",
         cliente="CLI-003", vigencia_dias=7, estado="Cotizada", esperado="compatible",
         lineas=[("cpu", "CPU-AMD-7600", 1), ("refrigeracion", "COOL-DPC-AK400", 1), ("placa", "MB-MSI-B650-GPWIFI", 1),
                 ("ram", "RAM-KNG-D5-32-6000", 1), ("gpu", "GPU-ASU-5060TI-DUAL16", 1),
                 ("almacenamiento", "SSD-SAM-990PRO-1TB", 1), ("fuente", "PSU-COR-RM750X", 1),
                 ("gabinete", "CASE-COR-4000D", 1), ("extra", "LIC-MS-W11HOME", 1), ("extra", "SRV-ENSAMBLE-PC", 1)]),
    dict(numero="ARM-CB-000001", perfil="alta", nombre="PC Gamer Alta 1440p/4K (Ryzen 7 9800X3D + RTX 5070 Ti)",
         cliente="CLI-012", vigencia_dias=10, estado="Aprobada", esperado="compatible",
         lineas=[("cpu", "CPU-AMD-9800X3D", 1), ("refrigeracion", "COOL-NZXT-KRAKEN240", 1),
                 ("placa", "MB-MSI-X870-TOMAHAWK", 1), ("ram", "RAM-COR-D5-32-6000", 1), ("gpu", "GPU-ASU-5070TI-TUF", 1),
                 ("almacenamiento", "SSD-SAM-990PRO-2TB", 1), ("fuente", "PSU-COR-RM850X", 1),
                 ("gabinete", "CASE-NZXT-H5FLOW", 1), ("extra", "MON-LG-27GS75Q", 1), ("extra", "LIC-MS-W11HOME", 1),
                 ("extra", "SRV-ENSAMBLE-PC", 1)]),
    dict(numero="ARM-CM-000003", perfil="entusiasta", nombre="PC Entusiasta 4K (Ryzen 9 9950X3D + RTX 5090)",
         cliente="CLI-018", vigencia_dias=15, estado="Cotizada", esperado="compatible",
         lineas=[("cpu", "CPU-AMD-9950X3D", 1), ("refrigeracion", "COOL-COR-NAUTILUS360", 1),
                 ("placa", "MB-ASU-X870E-STRIXE", 1), ("ram", "RAM-COR-D5-64-6000", 1), ("gpu", "GPU-ASU-5090-ASTRAL", 1),
                 ("almacenamiento", "SSD-SAM-9100PRO-2TB", 1), ("almacenamiento", "SSD-SAM-990PRO-2TB", 1),
                 ("fuente", "PSU-COR-HX1200I", 1), ("gabinete", "CASE-LL-O11DEVO", 1), ("extra", "MON-ASU-XG27ACDNG", 1),
                 ("extra", "LIC-MS-W11PRO", 1), ("extra", "SRV-ENSAMBLE-PC", 1)]),
    dict(numero="ARM-SC-000001", perfil="streaming", nombre="PC Streaming y creación (Core Ultra 7 265K + RTX 5070)",
         cliente="CLI-021", vigencia_dias=10, estado="Cotizada", esperado="compatible",
         lineas=[("cpu", "CPU-INT-265K", 1), ("refrigeracion", "COOL-MSI-A13-240", 1), ("placa", "MB-ASU-B860-TUFPW", 1),
                 ("ram", "RAM-COR-D5-64-6000", 1), ("gpu", "GPU-MSI-5070-GTRIO", 1),
                 ("almacenamiento", "SSD-WD-SN850X-2TB", 1), ("almacenamiento", "SSD-KNG-NV3-1TB", 1),
                 ("fuente", "PSU-MSI-A850GL", 1), ("gabinete", "CASE-COR-4000D", 1), ("extra", "CAM-HYX-QUADCASTS", 1),
                 ("extra", "CAM-LOG-C920S", 1), ("extra", "LIC-MS-W11PRO", 1), ("extra", "SRV-ENSAMBLE-PC", 1)]),
    dict(numero="ARM-SC-000002", perfil="oficina", nombre="PC de oficina (Ryzen 5 8600G con gráficos integrados)",
         cliente="CLI-023", vigencia_dias=15, estado="Convertida en venta", esperado="compatible",
         lineas=[("cpu", "CPU-AMD-8600G", 1), ("placa", "MB-GIG-B650M-DS3H", 1), ("ram", "RAM-KNG-D5-16-5600", 1),
                 ("almacenamiento", "SSD-KNG-NV3-1TB", 1), ("fuente", "PSU-MSI-A650BN", 1), ("gabinete", "CASE-LL-A3MATX", 1),
                 ("extra", "MON-AOC-24G4", 1), ("extra", "KEY-LOG-MK270", 1), ("extra", "LIC-MS-W11PRO", 1),
                 ("extra", "SRV-ENSAMBLE-PC", 1)]),
    dict(numero="ARM-CM-000004", perfil="prueba_incompatible", nombre="PRUEBA · Plataforma incompatible (AM4 en placa AM5)",
         cliente="CLI-005", vigencia_dias=3, estado="Borrador", esperado="incompatible",
         motivo="CPU AM4 en placa AM5 y RAM DDR4 en placa DDR5; sin almacenamiento ni gráficos",
         esperado_errores=["SOCKET_CPU_PLACA", "TIPO_RAM"], esperado_avisos=["SIN_GRAFICOS", "PIEZA_FALTANTE"],
         lineas=[("cpu", "CPU-AMD-5600", 1), ("refrigeracion", "COOL-DPC-AK400", 1), ("placa", "MB-ASU-B650-TUFPW", 1),
                 ("ram", "RAM-COR-D4-16-3200", 1), ("fuente", "PSU-COR-RM750X", 1), ("gabinete", "CASE-COR-4000D", 1)]),
    dict(numero="ARM-CM-000005", perfil="prueba_incompatible", nombre="PRUEBA · Espacio y energía (RTX 5090 en gabinete Mini-ITX)",
         cliente="CLI-006", vigencia_dias=3, estado="Borrador", esperado="incompatible",
         motivo="Demasiada RAM y M.2 para la placa, placa ATX y GPU larga en gabinete Mini-ITX, fuente corta y "
                "disipador solo Intel con CPU AM5",
         esperado_errores=["RANURAS_RAM", "CAPACIDAD_RAM", "RANURAS_M2", "FORMATO_GABINETE", "LARGO_GPU", "POTENCIA_FUENTE",
                           "SOCKET_REFRIGERACION"], esperado_avisos=[],
         lineas=[("cpu", "CPU-AMD-9950X3D", 1), ("refrigeracion", "COOL-NOC-NHL9I17XX", 1),
                 ("placa", "MB-MSI-B650-GPWIFI", 1), ("ram", "RAM-COR-D5-64-6000", 4), ("gpu", "GPU-ASU-5090-ASTRAL", 1),
                 ("almacenamiento", "SSD-SAM-990PRO-2TB", 2), ("almacenamiento", "SSD-SAM-9100PRO-2TB", 1),
                 ("fuente", "PSU-COR-RM750X", 1), ("gabinete", "CASE-CM-NR200P", 1)]),
]


def armados() -> list[dict]:
    out = []
    for d in ARMADOS_DEF:
        lineas_prod = [(r, POR_SKU[s], q) for r, s, q in d["lineas"] if s in POR_SKU]
        lineas = [OrderedDict(ranura=r, sku=s, nombre=POR_SKU[s]["nombre"] if s in POR_SKU else None, cantidad=q,
                              precio_cotizado=POR_SKU[s]["precio"] if s in POR_SKU else None,
                              subtotal=POR_SKU[s]["precio"] * q if s in POR_SKU else None) for r, s, q in d["lineas"]]
        resultado = evaluar_armado(lineas_prod)
        a = OrderedDict(numero=d["numero"], sucursal=d["numero"].split("-")[1], perfil=d["perfil"], nombre=d["nombre"],
                        cliente=d["cliente"], vigencia_dias=d["vigencia_dias"], estado=d["estado"],
                        marcado_incompatible=d["esperado"] == "incompatible")
        if d["esperado"] == "incompatible":
            a["motivo_prueba"] = d["motivo"]
            a["errores_esperados"] = d["esperado_errores"]
            a["avisos_esperados"] = d["esperado_avisos"]
        a["lineas"] = lineas
        a["total_bs"] = sum(l["subtotal"] or 0 for l in lineas)
        a["total_componentes_bs"] = sum(l["subtotal"] or 0 for l in lineas if l["ranura"] != "extra")
        a["compatibilidad"] = resultado
        out.append(a)
    return out


# ================================================================================================ validación
SKU_RE = re.compile(r"^[A-Z0-9]+(?:-[A-Z0-9]+)+$")
CHIPSET_SOCKET = {"A520": "AM4", "B550": "AM4", "A620": "AM5", "B650": "AM5", "B650E": "AM5", "X670E": "AM5", "X870": "AM5",
                  "X870E": "AM5", "B760": "LGA1700", "Z790": "LGA1700", "B860": "LGA1851", "Z890": "LGA1851"}


def validar(data: dict) -> tuple[list[str], list[str]]:
    errores: list[str] = []
    avisos: list[str] = []
    E = errores.append

    # ---- catálogos base
    codigos_cat = [c for c, _, _ in CATEGORIAS]
    if len(codigos_cat) != len(set(codigos_cat)):
        E("Códigos de categoría repetidos.")
    for c, _, p in CATEGORIAS:
        if p is not None and p not in PADRE:
            E(f"Categoría {c}: la madre {p} no existe.")
    hijas = {p for _, _, p in CATEGORIAS if p}
    for cat, lista in ESPECIFICACIONES.items():
        if cat not in PADRE:
            E(f"Especificaciones de una categoría inexistente: {cat}.")
            continue
        vistos = set()
        for s in lista:
            if s["codigo"] in vistos:
                E(f"{cat}: especificación repetida {s['codigo']}.")
            vistos.add(s["codigo"])
            if s["tipo"] not in ("texto", "numero", "opcion"):
                E(f"{cat}.{s['codigo']}: tipo inválido {s['tipo']}.")
            if s["tipo"] == "opcion" and not s["opciones"]:
                E(f"{cat}.{s['codigo']}: especificación de opción sin opciones.")
            if s["tipo"] != "opcion" and s["opciones"]:
                E(f"{cat}.{s['codigo']}: solo las de tipo opción llevan opciones.")
            if s["multivalor"] and s["tipo"] != "opcion":
                E(f"{cat}.{s['codigo']}: multivalor solo para opciones.")
            if len(s["opciones"]) != len(set(s["opciones"])):
                E(f"{cat}.{s['codigo']}: opciones repetidas.")
            if s["clave_compatibilidad"] and s["clave_compatibilidad"] not in CLAVES_COMPAT:
                E(f"{cat}.{s['codigo']}: clave de compatibilidad desconocida {s['clave_compatibilidad']}.")
        for anc in cadena(cat)[:-1]:
            repetidas = vistos & {s["codigo"] for s in ESPECIFICACIONES.get(anc, [])}
            if repetidas:
                E(f"{cat} redefine especificaciones heredadas de {anc}: {sorted(repetidas)}.")
    claves_usadas = {s["clave_compatibilidad"] for lista in ESPECIFICACIONES.values() for s in lista if s["clave_compatibilidad"]}
    if set(CLAVES_COMPAT) - claves_usadas:
        E(f"Claves de compatibilidad sin especificación: {sorted(set(CLAVES_COMPAT) - claves_usadas)}.")
    for img in IMAGENES:
        if not (AQUI / "Imagenes" / f"{img}.png").is_file():
            E(f"Falta la ilustración Imagenes/{img}.png.")

    # ---- productos
    prods = data["productos"]
    skus = Counter(p["sku"] for p in prods)
    for s, n in skus.items():
        if n > 1:
            E(f"SKU repetido: {s} ({n} veces).")
    nombres = Counter(p["nombre"] for p in prods)
    for s, n in nombres.items():
        if n > 1:
            E(f"Nombre de producto repetido: {s}.")
    if not 140 <= len(prods) <= 170:
        E(f"Se esperaban ~150 productos y hay {len(prods)}.")
    unidades = {u["codigo"] for u in UNIDADES}
    for p in prods:
        sku, cat, esp = p["sku"], p["categoria"], p["especificaciones"]
        pre = f"{sku}:"
        if not SKU_RE.match(sku) or len(sku) > 40:
            E(f"{pre} SKU con formato inválido (letras mayúsculas, números y guiones; máx. 40).")
        if not 2 <= len(p["nombre"]) <= 150:
            E(f"{pre} nombre fuera de 2-150 caracteres.")
        if cat not in PADRE:
            E(f"{pre} categoría inexistente {cat}.")
            continue
        if cat in hijas:
            E(f"{pre} la categoría {cat} no es hoja.")
        if p["unidad"] not in unidades:
            E(f"{pre} unidad inexistente {p['unidad']}.")
        if (p["unidad"] == "SERV") != (cat == "SRV"):
            E(f"{pre} la unidad SERV es exclusiva de los servicios.")
        if not (isinstance(p["costo"], int) and p["costo"] > 0):
            E(f"{pre} costo inválido.")
        if p["precio"] <= p["costo"]:
            E(f"{pre} el precio ({p['precio']}) no supera el costo ({p['costo']}).")
        m = margen(p["costo"], p["precio"])
        if not MARGEN_MIN - 1e-9 <= m <= MARGEN_MAX + 1e-9:
            E(f"{pre} margen {m:.1%} fuera de 15-35 %.")
        if not es_precio_comercial(p["precio"]):
            E(f"{pre} el precio {p['precio']} no tiene redondeo comercial.")
        if not (isinstance(p["popularidad"], int) and 1 <= p["popularidad"] <= 10):
            E(f"{pre} popularidad fuera de 1-10.")
        if p["unidad"] == "SERV":
            if p["minimo"] != 0 or p["maximo"] != 0:
                E(f"{pre} un servicio no maneja mínimo ni máximo.")
        elif not 0 < p["minimo"] < p["maximo"]:
            E(f"{pre} mínimo/máximo incoherentes ({p['minimo']}/{p['maximo']}).")
        if not isinstance(p["lleva_serie"], bool):
            E(f"{pre} lleva_serie debe ser booleano.")
        if p["lleva_serie"] and p["tipo_serie"] not in ("serie", "imei"):
            E(f"{pre} tipo de serie inválido.")
        if not p["lleva_serie"] and p["tipo_serie"] is not None:
            E(f"{pre} tipo de serie sin llevar serie.")
        if p["unidad"] == "SERV" and p["lleva_serie"]:
            E(f"{pre} un servicio no lleva serie.")
        if p["garantia_meses"] not in GARANTIAS_VALIDAS:
            E(f"{pre} garantía de {p['garantia_meses']} meses no admitida.")
        if p["imagen"] not in IMAGENES or not (AQUI / "Imagenes" / f"{p['imagen']}.png").is_file():
            E(f"{pre} imagen inexistente {p['imagen']}.")
        ps = p["producto_sin"]
        fila = SIN.get(ps["codigo_producto"])
        if fila is None:
            E(f"{pre} código de producto SIN {ps['codigo_producto']} inexistente en el CSV.")
        elif fila["codigo_actividad"] != ps["codigo_actividad"]:
            E(f"{pre} la actividad {ps['codigo_actividad']} no corresponde al producto SIN {ps['codigo_producto']}.")

        # especificaciones: existencia, tipo y opciones
        defs = specs_de(cat)
        for codigo, valor in esp.items():
            s = defs.get(codigo)
            if s is None:
                E(f"{pre} especificación {codigo} no definida para {cat}.")
                continue
            if s["tipo"] == "numero":
                if isinstance(valor, bool) or not isinstance(valor, (int, float)) or valor < 0:
                    E(f"{pre} {codigo} debe ser un número ≥ 0 ({valor!r}).")
            elif s["tipo"] == "texto":
                if not isinstance(valor, str) or not valor.strip():
                    E(f"{pre} {codigo} debe ser un texto no vacío.")
            elif s["multivalor"]:
                if not isinstance(valor, list) or not valor or len(valor) != len(set(valor)):
                    E(f"{pre} {codigo} debe ser una lista no vacía sin repetidos.")
                elif any(v not in s["opciones"] for v in valor):
                    E(f"{pre} {codigo} tiene valores fuera de las opciones: {[v for v in valor if v not in s['opciones']]}.")
            elif valor not in s["opciones"]:
                E(f"{pre} {codigo}={valor!r} no es una opción válida.")
        for codigo, s in defs.items():
            if s["obligatoria"] and codigo not in esp:
                E(f"{pre} falta la especificación obligatoria {codigo}.")
            if s["obligatoria"] and s["tipo"] == "numero" and codigo in esp and not esp[codigo] > 0:
                E(f"{pre} {codigo} obligatoria debe ser > 0.")
        completitud = len(esp) / len(defs)
        if completitud < 0.6:
            avisos.append(f"{pre} ficha técnica incompleta ({completitud:.0%}).")

        # coherencia por categoría
        g = esp.get
        if cat == "CPU":
            if g("hilos", 0) < g("nucleos", 0):
                E(f"{pre} menos hilos que núcleos.")
            if g("frecuencia_turbo", 0) < g("frecuencia_base", 0):
                E(f"{pre} turbo menor que la base.")
            if (g("graficos_integrados") == SI) != ("modelo_graficos" in esp):
                E(f"{pre} gráficos integrados y modelo de gráficos incoherentes.")
            soportada = set(g("memoria_soportada", []))
            if g("socket") == "AM4" and soportada != {"DDR4"} or g("socket") in ("AM5", "LGA1851") and soportada != {"DDR5"}:
                E(f"{pre} memoria soportada incoherente con el socket {g('socket')}.")
        elif cat == "GPU":
            pref = {"NVIDIA": "GeForce", "AMD": "Radeon", "Intel": "Arc"}[g("fabricante_chip")]
            if not g("modelo_gpu", "").startswith(pref):
                E(f"{pre} el modelo {g('modelo_gpu')} no corresponde a {g('fabricante_chip')}.")
            if g("modelo_gpu").split(" ", 1)[1] not in p["nombre"]:
                E(f"{pre} el nombre no menciona la GPU {g('modelo_gpu')}.")
            if g("fuente_recomendada", 0) < g("consumo", 0) * 1.5:
                E(f"{pre} fuente recomendada demasiado baja para {g('consumo')} W.")
            if g("tipo_vram") == "GDDR7" and g("fabricante_chip") != "NVIDIA":
                E(f"{pre} GDDR7 solo en la serie GeForce RTX 50.")
        elif cat == "MB":
            if CHIPSET_SOCKET.get(g("chipset")) != g("socket"):
                E(f"{pre} el chipset {g('chipset')} no corresponde al socket {g('socket')}.")
            if g("chipset") not in p["nombre"]:
                E(f"{pre} el nombre no menciona el chipset {g('chipset')}.")
            if g("socket") == "AM4" and g("tipo_ram") != "DDR4" or g("socket") in ("AM5", "LGA1851") and g("tipo_ram") != "DDR5":
                E(f"{pre} tipo de RAM incoherente con el socket.")
        elif cat == "RAM":
            if g("capacidad_total") % g("modulos"):
                E(f"{pre} la capacidad no se reparte en módulos iguales.")
            if (g("formato_modulo") == "SO-DIMM") != ("SO-DIMM" in p["nombre"]):
                E(f"{pre} formato de módulo no reflejado en el nombre.")
            if g("tipo_ram") not in p["nombre"]:
                E(f"{pre} el nombre no menciona {g('tipo_ram')}.")
        elif cat == "STO":
            if g("tecnologia") == "SSD" and ("rpm" in esp or "tbw" not in esp):
                E(f"{pre} un SSD lleva TBW y no rpm.")
            if g("tecnologia") == "HDD" and ("rpm" not in esp or g("interfaz") != "SATA" or "tbw" in esp):
                E(f"{pre} un HDD lleva rpm, interfaz SATA y no TBW.")
            if (g("formato_unidad") == "M.2 2280") != g("interfaz", "").startswith("NVMe"):
                E(f"{pre} formato e interfaz incoherentes.")
        elif cat == "PSU":
            if g("conector_12v2x6") == SI and not g("norma_atx", "").startswith("ATX 3"):
                E(f"{pre} el conector 12V-2x6 requiere ATX 3.x.")
            if str(g("potencia")) not in p["nombre"]:
                E(f"{pre} el nombre no menciona la potencia.")
        elif cat == "COOL":
            if g("tipo_refrigeracion") == "Líquida AIO" and "tamano_radiador" not in esp:
                E(f"{pre} una AIO lleva tamaño de radiador.")
            if g("tipo_refrigeracion") == "Aire" and "altura" not in esp:
                E(f"{pre} un disipador por aire lleva altura.")
            if (p["imagen"] == "cooler_aio") != (g("tipo_refrigeracion") == "Líquida AIO"):
                E(f"{pre} imagen incoherente con el tipo de refrigeración.")
        elif cat == "MON":
            if g("tipo_panel") in ("OLED", "QD-OLED") and g("tiempo_respuesta", 1) >= 0.1:
                E(f"{pre} un OLED tiene tiempo de respuesta < 0,1 ms.")
            if not 21 <= g("tamano", 0) <= 49:
                E(f"{pre} tamaño de monitor fuera de rango.")
        elif raiz(cat) == "PC":
            if g("sistema_operativo") == "macOS" and p["marca"] != "Apple":
                E(f"{pre} macOS solo en equipos Apple.")
        elif raiz(cat) == "CON":
            if IMG_CONSOLA.get(g("plataforma")) != p["imagen"]:
                E(f"{pre} imagen {p['imagen']} incoherente con la plataforma {g('plataforma')}.")
            sin_lector = g("edicion") in ("Digital", "Pro", "OLED", "Lite") or g("plataforma", "").startswith("Nintendo")
            if sin_lector and g("lector_discos") != NO:
                E(f"{pre} esta edición no tiene lector de discos.")
            if (g("edicion") == "Pack con juego") != ("juego_incluido" in esp):
                E(f"{pre} pack con juego y juego incluido incoherentes.")
            if g("plataforma") not in {"CPS": ("PS5", "PS4"), "CXB": ("Xbox Series X", "Xbox Series S"),
                                       "CNS": ("Nintendo Switch", "Nintendo Switch 2")}[cat]:
                E(f"{pre} plataforma {g('plataforma')} fuera de la subcategoría {cat}.")
            if not p["lleva_serie"]:
                E(f"{pre} una consola lleva número de serie.")
        elif cat == "JUE":
            if IMG_JUEGO.get(g("plataforma")) != p["imagen"]:
                E(f"{pre} imagen {p['imagen']} incoherente con la plataforma {g('plataforma')}.")
            if g("edicion") == "Nintendo Switch 2 Edition" and g("plataforma") != "Nintendo Switch 2":
                E(f"{pre} una Nintendo Switch 2 Edition es de Nintendo Switch 2.")
        elif cat == "RED":
            if (p["tipo_serie"] == "imei") != (g("red_movil", "No") != "No"):
                E(f"{pre} solo los equipos con red móvil se registran por IMEI.")
        if p["tipo_serie"] == "imei" and cat != "RED":
            E(f"{pre} IMEI solo para equipos de red móvil.")
        if p["producto_sin"]["codigo_producto"] == 1001981 and cat != "SRV":
            E(f"{pre} el ensamblado de computadores es un servicio.")

    # ---- proveedores
    provs = data["proveedores"]
    if len(provs) != 8:
        E(f"Se esperaban 8 proveedores y hay {len(provs)}.")
    for campo in ("codigo", "nit", "email", "razon_social"):
        c = Counter(pv[campo] for pv in provs)
        for v, n in c.items():
            if n > 1:
                E(f"Proveedores: {campo} repetido {v}.")
    for pv in provs:
        if not pv["email"].endswith(".example") or not pv["sitio_web"].endswith(".example"):
            E(f"{pv['codigo']}: correo y sitio deben ser .example (ficticios).")
        for c in pv["categorias"]:
            if c not in PADRE:
                E(f"{pv['codigo']}: categoría inexistente {c}.")
    for p in prods:
        if p["unidad"] != "SERV" and not any(_cubre(pv["categorias"], p["categoria"]) for pv in provs):
            E(f"{p['sku']}: ningún proveedor abastece la categoría {p['categoria']}.")

    # ---- clientes
    clis = data["clientes"]
    if len(clis) != 30:
        E(f"Se esperaban 30 clientes y hay {len(clis)}.")
    cats_cli = {c["codigo"] for c in data["categorias_cliente"]}
    for campo in ("codigo", "numero_documento", "email", "nombre"):
        c = Counter(x[campo] for x in clis)
        for v, n in c.items():
            if n > 1:
                E(f"Clientes: {campo} repetido {v}.")
    for x in clis:
        pre = f"{x['codigo']}:"
        if not x["email"].endswith(".example"):
            E(f"{pre} el correo debe ser .example.")
        if x["categoria"] not in cats_cli:
            E(f"{pre} categoría de cliente inexistente {x['categoria']}.")
        if x["tipo"] == "persona" and (x["tipo_documento"] != "CI" or len(x["numero_documento"]) != 7 or not x["extension"]):
            E(f"{pre} una persona se identifica con CI de 7 dígitos y extensión.")
        if x["tipo"] != "persona" and (x["tipo_documento"] != "NIT" or len(x["numero_documento"]) != 10):
            E(f"{pre} empresas y colegios se identifican con NIT.")
        if not (len(x["telefono"]) == 8 and x["telefono"][0] in "67"):
            E(f"{pre} teléfono móvil boliviano inválido.")
    tipos = Counter(x["tipo"] for x in clis)
    if not (tipos["persona"] and tipos["empresa"] and tipos["institucion_educativa"]):
        E("Faltan clientes de algún tipo (gamers, empresas, colegios).")

    # ---- armados
    arms = data["armados"]
    ids_cli = {x["codigo"] for x in clis}
    compatibles = [a for a in arms if not a["marcado_incompatible"]]
    incompatibles = [a for a in arms if a["marcado_incompatible"]]
    perfiles = sorted(a["perfil"] for a in compatibles)
    if perfiles != sorted(["entrada", "media", "alta", "entusiasta", "streaming", "oficina"]):
        E(f"Perfiles de armados compatibles inesperados: {perfiles}.")
    if len(incompatibles) != 2:
        E(f"Se esperaban 2 armados incompatibles y hay {len(incompatibles)}.")
    if len({a["numero"] for a in arms}) != len(arms):
        E("Números de armado repetidos.")
    for a in arms:
        pre = f"{a['numero']}:"
        if not re.match(r"^ARM-(CM|CB|SC)-\d{6}$", a["numero"]):
            E(f"{pre} número de armado con formato inválido.")
        if a["cliente"] not in ids_cli:
            E(f"{pre} cliente inexistente {a['cliente']}.")
        elif next(x["sucursal"] for x in clis if x["codigo"] == a["cliente"]) != a["sucursal"]:
            E(f"{pre} el cliente {a['cliente']} es de otra sucursal.")
        cuenta = Counter()
        for l in a["lineas"]:
            prod = POR_SKU.get(l["sku"])
            if prod is None:
                E(f"{pre} SKU inexistente {l['sku']}.")
                continue
            if l["ranura"] not in RANURAS:
                E(f"{pre} ranura desconocida {l['ranura']}.")
                continue
            esperada = RANURAS[l["ranura"]]
            if esperada and prod["categoria"] != esperada:
                E(f"{pre} {l['sku']} ({prod['categoria']}) no va en la ranura {l['ranura']}.")
            if esperada is None and prod["categoria"] in RANURAS.values():
                E(f"{pre} {l['sku']} es un componente y debe ir en su ranura.")
            if l["ranura"] == "ram" and prod["especificaciones"].get("formato_modulo") != "DIMM":
                E(f"{pre} {l['sku']} es SO-DIMM (memoria de laptop).")
            if not (isinstance(l["cantidad"], int) and l["cantidad"] >= 1):
                E(f"{pre} cantidad inválida en {l['sku']}.")
            if l["precio_cotizado"] != prod["precio"] or l["subtotal"] != prod["precio"] * l["cantidad"]:
                E(f"{pre} precio cotizado o subtotal incoherente en {l['sku']}.")
            cuenta[l["ranura"]] += l["cantidad"]
        for r in RANURAS_UNICAS:
            if cuenta[r] > 1:
                E(f"{pre} la ranura {r} admite una sola pieza.")
        if a["total_bs"] != sum(l["subtotal"] or 0 for l in a["lineas"]):
            E(f"{pre} total incoherente.")
        res = a["compatibilidad"]
        codigos_err = {e["regla"] for e in res["errores"]}
        codigos_av = {e["regla"] for e in res["avisos"]}
        if not a["marcado_incompatible"]:
            if res["errores"] or res["avisos"]:
                E(f"{pre} debía ser compatible y tiene {sorted(codigos_err | codigos_av)}.")
        else:
            if res["compatible"]:
                E(f"{pre} marcado incompatible pero no tiene errores.")
            if codigos_err != set(a["errores_esperados"]):
                E(f"{pre} errores {sorted(codigos_err)} ≠ esperados {sorted(a['errores_esperados'])}.")
            if codigos_av != set(a["avisos_esperados"]):
                E(f"{pre} avisos {sorted(codigos_av)} ≠ esperados {sorted(a['avisos_esperados'])}.")
    reglas_probadas = {e["regla"] for a in incompatibles for e in a["compatibilidad"]["errores"] + a["compatibilidad"]["avisos"]}
    faltan = [c for c, _, _ in REGLAS if c not in reglas_probadas and c != "POTENCIA_RECOMENDADA"]
    if faltan:
        avisos.append(f"Reglas sin caso de prueba incompatible: {faltan}.")
    return errores, avisos


# ================================================================================================ ensamblado y salida
def categorias_json() -> list[dict]:
    hijos = Counter(p for _, _, p in CATEGORIAS if p)
    return [OrderedDict(codigo=c, nombre=n, padre=p, nivel=len(cadena(c)) - 1, ruta=" > ".join(NOMBRE_CAT[x] for x in cadena(c)),
                        hoja=hijos[c] == 0, productos=sum(1 for pr in PRODUCTOS if c in cadena(pr["categoria"])))
            for c, n, p in CATEGORIAS]


def especificaciones_json() -> list[dict]:
    out = []
    for c, _, _ in CATEGORIAS:
        for orden, s in enumerate(ESPECIFICACIONES.get(c, []), start=1):
            out.append(OrderedDict(categoria=c, orden=orden, **s))
    return out


def resumen(data: dict) -> OrderedDict:
    prods = data["productos"]
    por_raiz = Counter(raiz(p["categoria"]) for p in prods)
    por_hoja = Counter(p["categoria"] for p in prods)
    barato = min(prods, key=lambda p: p["precio"])
    caro = max(prods, key=lambda p: p["precio"])
    margenes = [p["margen_pct"] for p in prods]
    return OrderedDict(
        productos=len(prods),
        por_categoria_raiz=OrderedDict((c, por_raiz[c]) for c, _, p in CATEGORIAS if p is None),
        por_categoria=OrderedDict((c, por_hoja[c]) for c, _, _ in CATEGORIAS if por_hoja[c]),
        precio_min=OrderedDict(sku=barato["sku"], precio=barato["precio"]),
        precio_max=OrderedDict(sku=caro["sku"], precio=caro["precio"]),
        margen_pct_min=min(margenes), margen_pct_max=max(margenes),
        margen_pct_promedio=round(sum(margenes) / len(margenes), 1),
        con_serie=sum(1 for p in prods if p["lleva_serie"]), con_imei=sum(1 for p in prods if p["tipo_serie"] == "imei"),
        servicios=sum(1 for p in prods if p["unidad"] == "SERV"),
        por_actividad_sin=OrderedDict(sorted(Counter(str(p["producto_sin"]["codigo_actividad"]) for p in prods).items())),
        marcas=len(data["marcas"]), proveedores=len(data["proveedores"]), clientes=len(data["clientes"]),
        clientes_por_tipo=OrderedDict(Counter(x["tipo"] for x in data["clientes"])),
        armados=len(data["armados"]),
        armados_compatibles=sum(1 for a in data["armados"] if a["compatibilidad"]["compatible"]),
        armados_incompatibles=sum(1 for a in data["armados"] if not a["compatibilidad"]["compatible"]))


def construir() -> OrderedDict:
    actividades = OrderedDict()
    for fila in SIN.values():
        actividades.setdefault(fila["codigo_actividad"], fila["descripcion_actividad"])
    return OrderedDict(
        metadatos=OrderedDict(
            version=VERSION, edicion="Tecnología y gaming", generado_por=Path(__file__).name,
            empresa=OrderedDict(nombre="Tech Zone Gaming S.R.L.", codigo="TECHZONE", ficticia=True, sucursales=[
                OrderedDict(codigo="CM", nombre="Casa matriz La Paz", ciudad="La Paz"),
                OrderedDict(codigo="CB", nombre="Sucursal Cochabamba", ciudad="Cochabamba"),
                OrderedDict(codigo="SC", nombre="Sucursal Santa Cruz", ciudad="Santa Cruz")]),
            moneda="BOB", precios_con_iva=True, iva=IVA, tipo_cambio_costo_bs_usd=TC_COSTO,
            margen_min=MARGEN_MIN, margen_max=MARGEN_MAX,
            nota_precios="Costo = referencia internacional 2026 (USD) × tipo de cambio de importación (flete, arancel e IVA "
                         "de importación incluidos); precio = costo / (1 − margen) con redondeo comercial terminado en 9.",
            nota_datos="Productos, marcas y especificaciones reales; proveedores, clientes, documentos y correos son "
                       "ficticios (dominio .example).",
            actividades_sin=[OrderedDict(codigo=k, descripcion=v) for k, v in actividades.items()]),
        unidades=UNIDADES,
        categorias=categorias_json(),
        especificaciones=especificaciones_json(),
        claves_compatibilidad=CLAVES_COMPAT,
        reglas_compatibilidad=[OrderedDict(codigo=c, nivel=n, descripcion=d) for c, n, d in REGLAS],
        marcas=marcas(),
        productos=PRODUCTOS,
        proveedores=proveedores(),
        categorias_cliente=CATEGORIAS_CLIENTE,
        clientes=clientes(),
        armados=armados(),
    )


def main() -> int:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except (AttributeError, ValueError):
        pass
    data = construir()
    errores, avisos = validar(data)
    data["resumen"] = resumen(data)
    data["validacion"] = OrderedDict(ok=not errores, errores=errores, avisos=avisos)
    r = data["resumen"]
    print(f"M-INV {VERSION} · catálogo de tecnología")
    print(f"  Productos: {r['productos']}  (serie: {r['con_serie']}, IMEI: {r['con_imei']}, servicios: {r['servicios']})")
    print("  Por categoría raíz: " + ", ".join(f"{NOMBRE_CAT[c]} {n}" for c, n in r["por_categoria_raiz"].items()))
    print("  Por categoría: " + ", ".join(f"{c} {n}" for c, n in r["por_categoria"].items()))
    print(f"  Precios: Bs {r['precio_min']['precio']:,} ({r['precio_min']['sku']}) a Bs {r['precio_max']['precio']:,} "
          f"({r['precio_max']['sku']}); margen {r['margen_pct_min']}-{r['margen_pct_max']} % (prom. {r['margen_pct_promedio']} %)")
    print(f"  Actividades SIN: {dict(r['por_actividad_sin'])}")
    print(f"  Marcas: {r['marcas']}  Proveedores: {r['proveedores']}  Clientes: {r['clientes']} {dict(r['clientes_por_tipo'])}")
    for a in data["armados"]:
        c = a["compatibilidad"]
        estado = "OK" if c["compatible"] and not c["avisos"] else "INCOMPATIBLE" if not c["compatible"] else "AVISOS"
        print(f"  {a['numero']} {a['perfil']:<20} Bs {a['total_bs']:>9,}  {c['consumo_estimado_w']:>4} W / fuente "
              f"{c['potencia_fuente_w']} W  {estado}  {[e['regla'] for e in c['errores']] or ''}"
              f"{[e['regla'] for e in c['avisos']] or ''}")
    for av in avisos:
        print(f"  AVISO: {av}")
    if errores:
        print(f"\nVALIDACIÓN FALLIDA: {len(errores)} errores")
        for e in errores:
            print(f"  - {e}")
        return 1
    SALIDA.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"\nVALIDACIÓN OK · escrito {SALIDA.name} ({SALIDA.stat().st_size:,} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
