r"""Genera las ilustraciones de productos de M-INV (datos de prueba y demostración).

Dibujos propios, planos y sin textos (sin imágenes de terceros): un tipo de artículo por archivo PNG de 360 px.
Uso:    .venv\Scripts\python tools\generar_imagenes_productos.py
Salida: src/2. Infrastructure/MINV.Infrastructure/Seeding/Imagenes/<tipo>.png (recursos incrustados del seeder)
"""
import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

S = 1440          # lienzo de trabajo (4x) para bordes suaves
OUT = 360
K = S / 360       # escala: las coordenadas se escriben en 0..360


def P(*xy):
    return [v * K for v in xy]


def box(x0, y0, x1, y1):
    return [x0 * K, y0 * K, x1 * K, y1 * K]


def hexrgb(h, a=255):
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


def background(top, bottom):
    img = Image.new("RGBA", (S, S))
    t, b = hexrgb(top), hexrgb(bottom)
    px = img.load()
    for y in range(S):
        f = y / S
        c = tuple(int(t[i] + (b[i] - t[i]) * f) for i in range(3)) + (255,)
        for x in range(S):
            px[x, y] = c
    return img


def shadow(img, ellipse):
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).ellipse(box(*ellipse), fill=(15, 23, 42, 70))
    layer = layer.filter(ImageFilter.GaussianBlur(10 * K))
    img.alpha_composite(layer)


def poly(d, pts, fill, outline=None, width=0):
    d.polygon([(pts[i] * K, pts[i + 1] * K) for i in range(0, len(pts), 2)], fill=fill, outline=outline, width=int(width * K) if width else 0)


def rrect(d, b, r, fill, outline=None, width=0):
    d.rounded_rectangle(box(*b), radius=r * K, fill=fill, outline=outline, width=int(width * K) if width else 0)


def line(d, pts, fill, width):
    d.line([(pts[i] * K, pts[i + 1] * K) for i in range(0, len(pts), 2)], fill=fill, width=int(width * K), joint="curve")


def ell(d, b, fill, outline=None, width=0):
    d.ellipse(box(*b), fill=fill, outline=outline, width=int(width * K) if width else 0)


# ------------------------------------------------------------------------------------------------ dibujos
def tornillo(d):
    rrect(d, (120, 70, 240, 110), 14, "#9AA7B8")
    rrect(d, (120, 70, 240, 88), 10, "#C3CCD8")
    line(d, (180, 78, 180, 102), "#5B6778", 8)
    poly(d, (160, 110, 200, 110, 196, 250, 180, 300, 164, 250), "#B4BFCD")
    for y in range(125, 250, 22):
        line(d, (158, y, 202, y + 10), "#7D8A9C", 7)


def martillo(d):
    poly(d, (168, 130, 192, 130, 200, 310, 160, 310), "#C98A4B")
    poly(d, (172, 130, 180, 130, 176, 310, 164, 310), "#E0A566")
    rrect(d, (90, 70, 270, 130), 16, "#5B6778")
    rrect(d, (90, 70, 270, 92), 12, "#8391A5")
    poly(d, (270, 72, 305, 88, 305, 112, 270, 128), "#46505F")


def cinta_metrica(d):
    rrect(d, (80, 110, 240, 270), 40, "#F2C230")
    ell(d, (120, 150, 200, 230), "#E0A800")
    ell(d, (145, 175, 175, 205), "#3C4450")
    rrect(d, (225, 220, 320, 250), 4, "#FFE680")
    for x in range(235, 320, 14):
        line(d, (x, 222, x, 236), "#6B5A00", 3)
    rrect(d, (80, 110, 240, 140), 30, "#FFD84D")


def disco(d):
    ell(d, (70, 70, 290, 290), "#3C4450")
    ell(d, (80, 80, 280, 280), "#5B6778")
    ell(d, (95, 95, 265, 265), "#8391A5")
    ell(d, (150, 150, 210, 210), "#2B3240")
    ell(d, (168, 168, 192, 192), "#DDE3EA")
    for a in range(0, 360, 30):
        r0, r1 = 105, 128
        line(d, (180 + r0 * math.cos(math.radians(a)), 180 + r0 * math.sin(math.radians(a)),
                 180 + r1 * math.cos(math.radians(a)), 180 + r1 * math.sin(math.radians(a))), "#AAB5C4", 4)


def candado(d):
    d.arc(box(120, 60, 240, 190), 180, 360, fill="#9AA7B8", width=int(22 * K))
    line(d, (131, 125, 131, 160), "#9AA7B8", 22)
    line(d, (229, 125, 229, 160), "#9AA7B8", 22)
    rrect(d, (95, 150, 265, 300), 22, "#E0A800")
    rrect(d, (95, 150, 265, 180), 18, "#F2C230")
    ell(d, (166, 200, 194, 228), "#5A4700")
    poly(d, (174, 220, 186, 220, 190, 262, 170, 262), "#5A4700")


def serrucho(d):
    poly(d, (60, 150, 250, 120, 250, 205, 60, 230), "#C3CCD8")
    for x in range(64, 250, 12):
        poly(d, (x, 229 - (x - 60) * 0.13, x + 6, 244 - (x - 60) * 0.13, x + 12, 227 - (x - 60) * 0.13), "#9AA7B8")
    rrect(d, (240, 105, 320, 225), 26, "#1565C0")
    rrect(d, (262, 135, 298, 195), 16, "#E8F1FD")


def destornillador(d):
    rrect(d, (70, 150, 180, 210), 28, "#E53935")
    rrect(d, (70, 150, 180, 170), 20, "#EF6C6C")
    for x in (95, 120, 145):
        line(d, (x, 158, x, 202), "#B71C1C", 5)
    rrect(d, (175, 170, 300, 190), 6, "#9AA7B8")
    poly(d, (300, 168, 320, 176, 320, 184, 300, 192), "#7D8A9C")


def llave(d):
    rrect(d, (95, 160, 265, 200), 20, "#8391A5")
    ell(d, (40, 125, 140, 235), "#8391A5")
    ell(d, (65, 155, 105, 205), "#EEF2F7")
    poly(d, (40, 170, 80, 170, 80, 190, 40, 190), "#EEF2F7")
    ell(d, (230, 135, 320, 225), "#8391A5")
    ell(d, (255, 160, 295, 200), "#EEF2F7")


def taladro(d):
    rrect(d, (70, 100, 250, 180), 30, "#1565C0")
    rrect(d, (70, 100, 250, 125), 24, "#3B82F6")
    poly(d, (140, 170, 200, 170, 215, 300, 135, 300), "#0D47A1")
    rrect(d, (125, 280, 225, 320), 10, "#263238")
    rrect(d, (250, 125, 290, 155), 6, "#37474F")
    rrect(d, (288, 134, 330, 146), 4, "#9AA7B8")
    rrect(d, (95, 125, 130, 150), 8, "#FFB300")


def cable(d):
    ell(d, (70, 80, 290, 300), "#E53935")
    ell(d, (95, 105, 265, 275), "#C62828")
    ell(d, (115, 125, 245, 255), "#E53935")
    ell(d, (140, 150, 220, 230), "#EEF2F7")
    for a in range(0, 360, 20):
        line(d, (180 + 90 * math.cos(math.radians(a)), 190 + 90 * math.sin(math.radians(a)),
                 180 + 106 * math.cos(math.radians(a)), 190 + 106 * math.sin(math.radians(a))), "#B71C1C", 3)
    line(d, (260, 150, 320, 110), "#E53935", 12)
    line(d, (318, 111, 334, 100), "#E0A800", 6)


def foco(d):
    ell(d, (95, 55, 265, 225), "#FFE58A")
    ell(d, (115, 70, 215, 170), "#FFF3C4")
    poly(d, (135, 205, 225, 205, 215, 245, 145, 245), "#FFE58A")
    rrect(d, (140, 240, 220, 300), 10, "#9AA7B8")
    for y in (252, 268, 284):
        line(d, (140, y, 220, y), "#7D8A9C", 5)
    poly(d, (160, 300, 200, 300, 190, 318, 170, 318), "#3C4450")


def enchufe(d):
    rrect(d, (80, 70, 280, 300), 30, "#F5F7FA", outline="#CBD5E1", width=4)
    rrect(d, (110, 100, 250, 180), 20, "#E3E8EF")
    rrect(d, (110, 195, 250, 275), 20, "#E3E8EF")
    for y in (125, 220):
        rrect(d, (145, y, 157, y + 28), 4, "#3C4450")
        rrect(d, (203, y, 215, y + 28), 4, "#3C4450")


def cinta_aislante(d):
    ell(d, (70, 90, 290, 310), "#263238")
    ell(d, (90, 110, 270, 290), "#37474F")
    ell(d, (135, 155, 225, 245), "#CFD8DC")
    ell(d, (150, 170, 210, 230), "#EEF2F7")
    poly(d, (270, 200, 330, 215, 325, 245, 262, 232), "#263238")


def tubo(d):
    rrect(d, (50, 150, 300, 210), 10, "#E3E8EF", outline="#B0BAC7", width=4)
    rrect(d, (50, 150, 300, 168), 8, "#F8FAFC")
    ell(d, (280, 145, 320, 215), "#CBD5E1")
    ell(d, (290, 160, 310, 200), "#8391A5")
    rrect(d, (40, 140, 90, 220), 10, "#F8FAFC", outline="#B0BAC7", width=4)


def llave_paso(d):
    rrect(d, (60, 170, 300, 220), 10, "#E0A800")
    rrect(d, (130, 140, 230, 250), 18, "#F2C230")
    rrect(d, (170, 90, 190, 145), 4, "#B08400")
    rrect(d, (120, 70, 240, 100), 14, "#E53935")
    rrect(d, (60, 170, 300, 185), 8, "#FFD84D")


def grifo(d):
    rrect(d, (70, 250, 290, 290), 12, "#AAB5C4")
    rrect(d, (150, 150, 200, 255), 12, "#C3CCD8")
    d.arc(box(150, 70, 290, 210), 180, 270, fill="#C3CCD8", width=int(34 * K))
    line(d, (220, 87, 262, 87), "#C3CCD8", 34)
    rrect(d, (255, 88, 285, 140), 10, "#AAB5C4")
    rrect(d, (130, 120, 220, 150), 12, "#1565C0")
    ell(d, (262, 150, 278, 172), "#60A5FA")


def pintura(d, color="#1E88E5"):
    rrect(d, (85, 105, 275, 300), 18, "#DDE3EA")
    ell(d, (85, 85, 275, 125), "#C3CCD8")
    ell(d, (100, 92, 260, 118), "#AAB5C4")
    rrect(d, (85, 150, 275, 250), 4, color)
    poly(d, (95, 150, 140, 150, 125, 185, 110, 175), color)
    d.arc(box(95, 40, 265, 160), 200, 340, fill="#5B6778", width=int(8 * K))


def pintura_roja(d):
    pintura(d, "#E53935")


def pintura_blanca(d):
    pintura(d, "#F5F7FA")
    rrect(d, (85, 150, 275, 250), 4, None, outline="#CBD5E1", width=3)


def brocha(d):
    rrect(d, (155, 40, 205, 160), 20, "#C98A4B")
    rrect(d, (140, 150, 220, 190), 6, "#9AA7B8")
    poly(d, (138, 188, 222, 188, 232, 300, 128, 300), "#8D6E4B")
    for x in range(140, 222, 12):
        line(d, (x, 200, x + 2, 296), "#6D5237", 3)


def rodillo(d):
    rrect(d, (80, 90, 280, 160), 34, "#1E88E5")
    rrect(d, (80, 90, 280, 112), 26, "#60A5FA")
    line(d, (280, 125, 300, 125, 300, 190, 190, 190, 190, 230), "#8391A5", 10)
    rrect(d, (172, 225, 208, 320), 14, "#E53935")


def casco(d):
    d.pieslice(box(70, 70, 290, 290), 180, 360, fill="#F2C230")
    d.pieslice(box(95, 85, 265, 255), 190, 350, fill="#FFD84D")
    rrect(d, (50, 172, 310, 200), 12, "#E0A800")
    rrect(d, (165, 70, 195, 175), 8, "#E0A800")


def guantes(d):
    rrect(d, (95, 140, 255, 300), 40, "#26A69A")
    for i, x in enumerate((100, 138, 176, 214)):
        rrect(d, (x, 60 + (i % 2) * 18, x + 34, 170), 17, "#26A69A")
    rrect(d, (225, 150, 300, 200), 24, "#26A69A")
    rrect(d, (95, 260, 255, 310), 18, "#00796B")


def botas(d):
    poly(d, (120, 60, 200, 60, 205, 220, 300, 240, 305, 300, 110, 300), "#6D4C41")
    poly(d, (120, 60, 200, 60, 202, 110, 118, 110), "#8D6E63")
    rrect(d, (100, 290, 315, 318), 10, "#263238")
    ell(d, (240, 235, 300, 295), "#9AA7B8")
    for y in (130, 160, 190):
        line(d, (130, y, 195, y), "#3E2723", 5)


def gafas(d):
    rrect(d, (50, 140, 310, 210), 30, "#90CAF9")
    rrect(d, (50, 140, 310, 160), 26, "#BBDEFB")
    line(d, (180, 150, 180, 200), "#64B5F6", 6)
    rrect(d, (50, 134, 310, 146), 6, "#263238")
    line(d, (50, 150, 20, 190), "#263238", 8)
    line(d, (310, 150, 340, 190), "#263238", 8)


def mascarilla(d):
    rrect(d, (80, 120, 280, 260), 60, "#F5F7FA", outline="#CBD5E1", width=4)
    for y in (160, 190, 220):
        d.arc(box(100, y - 20, 260, y + 20), 20, 160, fill="#CBD5E1", width=int(4 * K))
    line(d, (80, 170, 40, 150), "#90A4AE", 6)
    line(d, (280, 170, 320, 150), "#90A4AE", 6)
    ell(d, (160, 175, 200, 215), "#E3E8EF")


def botella(d, color="#1E88E5"):
    rrect(d, (115, 110, 245, 310), 30, color)
    rrect(d, (115, 110, 245, 140), 26, "#DDE3EA")
    rrect(d, (150, 60, 210, 115), 10, "#EEF2F7")
    rrect(d, (140, 50, 220, 70), 8, "#E53935")
    rrect(d, (135, 175, 225, 255), 10, "#F5F7FA")
    line(d, (150, 200, 210, 200), "#90A4AE", 6)
    line(d, (150, 222, 195, 222), "#90A4AE", 6)


def botella_verde(d):
    botella(d, "#43A047")


def bolsa(d):
    poly(d, (90, 120, 270, 120, 300, 310, 60, 310), "#263238")
    poly(d, (130, 70, 180, 120, 230, 70, 250, 120, 110, 120), "#37474F")
    line(d, (100, 180, 280, 180), "#455A64", 4)
    line(d, (80, 250, 290, 250), "#455A64", 4)


def escoba(d):
    rrect(d, (170, 30, 190, 210), 8, "#C98A4B")
    rrect(d, (110, 200, 250, 240), 10, "#E53935")
    poly(d, (105, 238, 255, 238, 280, 320, 80, 320), "#FFB300")
    for x in range(95, 270, 14):
        line(d, (x, 250, x - 4 + (x - 180) * 0.1, 316), "#E08E00", 4)


def cemento(d):
    rrect(d, (80, 90, 280, 300), 24, "#BCAAA4")
    rrect(d, (80, 90, 280, 120), 18, "#D7CCC8")
    rrect(d, (110, 150, 250, 230), 10, "#8D6E63")
    rrect(d, (125, 165, 235, 185), 6, "#F5F7FA")
    rrect(d, (125, 195, 200, 212), 6, "#F5F7FA")


def extintor(d):
    rrect(d, (125, 110, 235, 310), 40, "#E53935")
    rrect(d, (125, 110, 235, 150), 30, "#EF6C6C")
    rrect(d, (160, 70, 200, 115), 8, "#3C4450")
    line(d, (200, 80, 265, 70, 280, 130), "#263238", 10)
    rrect(d, (145, 180, 215, 250), 10, "#F5F7FA")
    rrect(d, (130, 60, 230, 76), 6, "#263238")


def escalera(d):
    line(d, (120, 40, 80, 320), "#AAB5C4", 18)
    line(d, (240, 40, 280, 320), "#AAB5C4", 18)
    for i, y in enumerate(range(80, 310, 45)):
        dx = (y - 40) * (40 / 280)
        line(d, (120 - dx + 6, y, 240 + dx - 6, y), "#8391A5", 14)


def chaleco(d):
    poly(d, (100, 70, 150, 70, 180, 120, 210, 70, 260, 70, 290, 310, 70, 310), "#FF9800")
    rrect(d, (80, 180, 280, 205), 4, "#E0E0E0")
    rrect(d, (76, 250, 284, 275), 4, "#E0E0E0")
    line(d, (180, 120, 180, 310), "#E65100", 5)


def caja(d):
    poly(d, (180, 70, 300, 130, 180, 190, 60, 130), "#E3C08D")
    poly(d, (60, 130, 180, 190, 180, 320, 60, 260), "#C99A5B")
    poly(d, (300, 130, 180, 190, 180, 320, 300, 260), "#B5844A")
    poly(d, (120, 100, 240, 160, 250, 155, 130, 95), "#F1D9B0")


def nivel(d):
    rrect(d, (40, 140, 320, 220), 14, "#F2C230")
    rrect(d, (40, 140, 320, 160), 10, "#FFD84D")
    rrect(d, (150, 160, 210, 200), 20, "#A5D6A7")
    ell(d, (170, 168, 192, 192), "#E8F5E9")
    rrect(d, (70, 170, 100, 190), 8, "#A5D6A7")
    rrect(d, (260, 170, 290, 190), 8, "#A5D6A7")


def pala(d):
    rrect(d, (170, 30, 190, 200), 8, "#C98A4B")
    rrect(d, (150, 25, 210, 45), 10, "#263238")
    poly(d, (180, 190, 250, 215, 240, 300, 180, 330, 120, 300, 110, 215), "#8391A5")
    poly(d, (180, 195, 215, 208, 205, 290, 180, 305), "#AAB5C4")


def carretilla(d):
    poly(d, (60, 130, 300, 130, 260, 230, 110, 230), "#1E88E5")
    poly(d, (60, 130, 300, 130, 290, 150, 70, 150), "#60A5FA")
    ell(d, (100, 230, 170, 300), "#263238")
    ell(d, (122, 252, 148, 278), "#9AA7B8")
    line(d, (260, 230, 320, 280), "#5B6778", 10)
    line(d, (240, 230, 250, 300), "#5B6778", 8)


DIBUJOS = {
    "tornillo": (tornillo, "#EEF2F7", "#D6DEE8"),
    "martillo": (martillo, "#FFF3E0", "#FFE0B2"),
    "cinta_metrica": (cinta_metrica, "#FFFDE7", "#FFF59D"),
    "disco": (disco, "#ECEFF1", "#CFD8DC"),
    "candado": (candado, "#FFF8E1", "#FFECB3"),
    "serrucho": (serrucho, "#E3F2FD", "#BBDEFB"),
    "destornillador": (destornillador, "#FFEBEE", "#FFCDD2"),
    "llave": (llave, "#ECEFF1", "#CFD8DC"),
    "taladro": (taladro, "#E3F2FD", "#BBDEFB"),
    "cable": (cable, "#FFEBEE", "#FFCDD2"),
    "foco": (foco, "#FFFDE7", "#FFF59D"),
    "enchufe": (enchufe, "#EEF2F7", "#D6DEE8"),
    "cinta_aislante": (cinta_aislante, "#ECEFF1", "#CFD8DC"),
    "tubo": (tubo, "#E0F2F1", "#B2DFDB"),
    "llave_paso": (llave_paso, "#FFF8E1", "#FFECB3"),
    "grifo": (grifo, "#E3F2FD", "#BBDEFB"),
    "pintura": (pintura, "#E3F2FD", "#BBDEFB"),
    "pintura_roja": (pintura_roja, "#FFEBEE", "#FFCDD2"),
    "pintura_blanca": (pintura_blanca, "#ECEFF1", "#CFD8DC"),
    "brocha": (brocha, "#FFF3E0", "#FFE0B2"),
    "rodillo": (rodillo, "#E3F2FD", "#BBDEFB"),
    "casco": (casco, "#FFFDE7", "#FFF59D"),
    "guantes": (guantes, "#E0F2F1", "#B2DFDB"),
    "botas": (botas, "#EFEBE9", "#D7CCC8"),
    "gafas": (gafas, "#E3F2FD", "#BBDEFB"),
    "mascarilla": (mascarilla, "#E0F7FA", "#B2EBF2"),
    "botella": (botella, "#E3F2FD", "#BBDEFB"),
    "botella_verde": (botella_verde, "#E8F5E9", "#C8E6C9"),
    "bolsa": (bolsa, "#ECEFF1", "#CFD8DC"),
    "escoba": (escoba, "#FFF8E1", "#FFECB3"),
    "cemento": (cemento, "#EFEBE9", "#D7CCC8"),
    "extintor": (extintor, "#FFEBEE", "#FFCDD2"),
    "escalera": (escalera, "#ECEFF1", "#CFD8DC"),
    "chaleco": (chaleco, "#FFF3E0", "#FFE0B2"),
    "caja": (caja, "#FFF8E1", "#FFECB3"),
    "nivel": (nivel, "#FFFDE7", "#FFF59D"),
    "pala": (pala, "#ECEFF1", "#CFD8DC"),
    "carretilla": (carretilla, "#E3F2FD", "#BBDEFB"),
}


def render(nombre, funcion, top, bottom):
    img = background(top, bottom)
    shadow(img, (70, 300, 290, 335))
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    funcion(ImageDraw.Draw(layer))
    img.alpha_composite(layer)
    return img.resize((OUT, OUT), Image.LANCZOS).convert("RGB")


def main():
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[1] / "src" / "2. Infrastructure" / "MINV.Infrastructure" / "Seeding" / "Imagenes"
    out.mkdir(parents=True, exist_ok=True)
    for nombre, (funcion, top, bottom) in DIBUJOS.items():
        render(nombre, funcion, top, bottom).save(out / f"{nombre}.png", optimize=True)
    print(f"ok {len(DIBUJOS)} imágenes en {out}")


if __name__ == "__main__":
    main()
