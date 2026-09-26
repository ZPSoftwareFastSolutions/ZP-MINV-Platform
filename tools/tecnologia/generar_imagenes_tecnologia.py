r"""Genera las ilustraciones de productos de M-INV V4.2 · Edición Tecnología y Gaming (datos de prueba y demostración).

Dibujos propios, sin textos, sin logotipos ni marcas (sin imágenes de terceros): un tipo de artículo por archivo PNG de
360 px, con la técnica de las ilustraciones de la edición ferretería (lienzo 4x con suavizado, coordenadas en 0..360)
pero con fondo oscuro gaming (degradado azul noche, halo y rejilla de piso) y acentos neón violeta, cian y magenta.
Los trazos marcados como «neón» se dibujan también en una capa de brillo que se difumina debajo del dibujo.

Uso:    .venv\Scripts\python tools\tecnologia\generar_imagenes_tecnologia.py [carpeta_salida] [--muestrario]
Salida: src/2. Infrastructure/MINV.Infrastructure/Seeding/Tecnologia/Imagenes/<tipo>.png (recursos incrustados del seeder)
        --muestrario agrega _muestrario.png, una cuadrícula para revisar los dibujos: NO debe quedar en la carpeta
        del seeder (el proyecto incrusta Seeding\Tecnologia\Imagenes\*.png).
"""
import math
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageColor, ImageDraw, ImageFilter, ImageFont

S = 1440          # lienzo de trabajo (4x) para bordes suaves
OUT = 360
K = S / 360       # escala: las coordenadas se escriben en 0..360

# ------------------------------------------------------------------------------------------------ paleta gaming
FONDO_ARRIBA, FONDO_ABAJO = "#182038", "#090C15"
D0, D1, D2, D3, D4, D5 = "#07090F", "#10151F", "#19202E", "#242D3F", "#334058", "#4D5B78"
M1, M2, M3, W, WW = "#7B879C", "#A7B1C2", "#CBD3DF", "#E7ECF3", "#F7F9FC"
CIAN, VIOLETA, VIOLETA_OSC, LILA, MAGENTA, ROSA = "#22D3EE", "#8B5CF6", "#6D28D9", "#C4B5FD", "#E879F9", "#F472B6"
VERDE, ROJO, AMBAR, AZUL = "#4ADE80", "#FF4557", "#FBBF24", "#3B82F6"
ORO, ORO_OSC = "#E2A93B", "#A8741C"
RGB = (VIOLETA, CIAN, MAGENTA)


# ------------------------------------------------------------------------------------------------ utilidades
def box(x0, y0, x1, y1):
    return [x0 * K, y0 * K, x1 * K, y1 * K]


def _xy(pts):
    return [(pts[i] * K, pts[i + 1] * K) for i in range(0, len(pts), 2)]


def _w(w):
    return max(1, int(round(w * K))) if w else 0


def _px(b):
    x0, y0, x1, y1 = (int(round(v * K)) for v in b)
    return x0, y0, max(x0 + 1, x1), max(y0 + 1, y1)


def _rgba(color, alpha=None):
    r, g, b, a = ImageColor.getcolor(color, "RGBA")
    return (r, g, b, a if alpha is None else alpha)


def mezcla(c1, c2, t):
    a, b = _rgba(c1), _rgba(c2)
    return "#%02X%02X%02X" % tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def color_en(stops, t):
    t = min(1.0, max(0.0, t)) * (len(stops) - 1)
    k = min(int(t), len(stops) - 2)
    return mezcla(stops[k], stops[k + 1], t - k)


def curva(puntos, pasos=10):
    """Catmull-Rom por los puntos dados; devuelve la lista plana x0, y0, x1, y1…"""
    p = [puntos[0]] + list(puntos) + [puntos[-1]]
    res = []
    for i in range(1, len(p) - 2):
        p0, p1, p2, p3 = p[i - 1], p[i], p[i + 1], p[i + 2]
        for s in range(pasos):
            t = s / pasos
            t2, t3 = t * t, t * t * t
            for j in range(2):
                res.append(0.5 * (2 * p1[j] + (-p0[j] + p2[j]) * t + (2 * p0[j] - 5 * p1[j] + 4 * p2[j] - p3[j]) * t2
                                  + (-p0[j] + 3 * p1[j] - 3 * p2[j] + p3[j]) * t3))
    res += list(puntos[-1])
    return res


class Lienzo:
    """Capa del dibujo + capa de brillo (lo que se dibuja con neon=True se difumina debajo)."""

    def __init__(self):
        self.img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        self.brillo = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)
        self.b = ImageDraw.Draw(self.brillo)

    def capas(self, neon):
        return (self.d, self.b) if neon else (self.d,)


def rrect(c, b, r, fill=None, outline=None, width=0, neon=False):
    for d in c.capas(neon):
        d.rounded_rectangle(box(*b), radius=r * K, fill=fill, outline=outline, width=_w(width) if outline else 0)


def poly(c, pts, fill=None, outline=None, width=0, neon=False):
    for d in c.capas(neon):
        d.polygon(_xy(pts), fill=fill, outline=outline, width=_w(width) if outline else 0)


def line(c, pts, fill, width, neon=False):
    for d in c.capas(neon):
        d.line(_xy(pts), fill=fill, width=_w(width), joint="curve")


def ell(c, b, fill=None, outline=None, width=0, neon=False):
    for d in c.capas(neon):
        d.ellipse(box(*b), fill=fill, outline=outline, width=_w(width) if outline else 0)


def circ(c, cx, cy, r, fill=None, outline=None, width=0, neon=False):
    ell(c, (cx - r, cy - r, cx + r, cy + r), fill, outline, width, neon)


def arc(c, b, a0, a1, fill, width, neon=False):
    for d in c.capas(neon):
        d.arc(box(*b), a0, a1, fill=fill, width=_w(width))


def trazo(c, pts, fill, width, neon=False):
    """Línea gruesa con extremos redondeados (cables, mangueras, brazos)."""
    line(c, pts, fill, width, neon)
    for x, y in ((pts[0], pts[1]), (pts[-2], pts[-1])):
        circ(c, x, y, width / 2, fill=fill, neon=neon)


# formas para máscaras: f(d, v) dibuja la figura con el valor v (255 = dentro)
def f_rrect(b, r):
    return lambda d, v=255: d.rounded_rectangle(box(*b), radius=r * K, fill=v)


def f_rect(b):
    return lambda d, v=255: d.rectangle(box(*b), fill=v)


def f_ell(b):
    return lambda d, v=255: d.ellipse(box(*b), fill=v)


def f_poly(pts):
    return lambda d, v=255: d.polygon(_xy(pts), fill=v)


def f_resta(a, b):
    def f(d, v=255):
        a(d, v)
        b(d, 0)
    return f


def _mascara(forma):
    m = Image.new("L", (S, S), 0)
    forma(ImageDraw.Draw(m))
    return m


def _tira(stops, w, h, vertical=False):
    cols = [_rgba(s) for s in stops]
    n = 256
    tira = Image.new("RGBA", (n, 1))
    px = tira.load()
    for i in range(n):
        t = i / (n - 1) * (len(cols) - 1)
        k = min(int(t), len(cols) - 2)
        f = t - k
        px[i, 0] = tuple(int(round(cols[k][j] + (cols[k + 1][j] - cols[k][j]) * f)) for j in range(4))
    if vertical:
        tira = tira.transpose(Image.Transpose.ROTATE_270)
    return tira.resize((max(1, w), max(1, h)), Image.BILINEAR)


def _pegar_degradado(dst, bbox, stops, vertical=False, forma=None):
    x0, y0, x1, y1 = _px(bbox)
    g = _tira(stops, x1 - x0, y1 - y0, vertical)
    if forma is not None:
        m = _mascara(forma).crop((x0, y0, x1, y1))
        g.putalpha(ImageChops.multiply(g.getchannel("A"), m))
    dst.alpha_composite(g, (x0, y0))


def degradado(c, bbox, stops, forma, vertical=False, neon=False, solo_brillo=False):
    if not solo_brillo:
        _pegar_degradado(c.img, bbox, stops, vertical, forma)
    if neon or solo_brillo:
        _pegar_degradado(c.brillo, bbox, stops, vertical, forma)


def capa_recortada(c, forma, dibujar, neon=False):
    """Dibuja en una capa aparte (dibujar(draw, capa)), la recorta con la forma y la mezcla (admite transparencias)."""
    capa = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    dibujar(ImageDraw.Draw(capa), capa)
    if forma is not None:
        capa.putalpha(ImageChops.multiply(capa.getchannel("A"), _mascara(forma)))
    c.img.alpha_composite(capa)
    if neon:
        c.brillo.alpha_composite(capa)


def cortar(c, forma):
    """Vuelve transparente la figura (ranuras, agujeros, muescas)."""
    inv = ImageChops.invert(_mascara(forma))
    for capa in (c.img, c.brillo):
        capa.putalpha(ImageChops.multiply(capa.getchannel("A"), inv))


def anillo(c, cx, cy, r, grosor, stops, neon=True):
    forma = f_resta(f_ell((cx - r, cy - r, cx + r, cy + r)), f_ell((cx - r + grosor, cy - r + grosor, cx + r - grosor, cy + r - grosor)))
    degradado(c, (cx - r, cy - r, cx + r, cy + r), stops if len(stops) > 1 else stops * 2, forma, neon=neon)


def ventilador(c, cx, cy, r, aro=(VIOLETA, CIAN), aspas=9):
    circ(c, cx, cy, r, fill=D0)
    paso = 2 * math.pi / aspas
    for i in range(aspas):
        a0 = i * paso
        ida, vuelta = [], []
        for s in range(7):
            t = s / 6
            rr = r * (0.3 + 0.6 * t)
            ida += [cx + rr * math.cos(a0 + 0.75 * t), cy + rr * math.sin(a0 + 0.75 * t)]
            a1 = a0 + paso * 0.62 + 0.75 * t
            vuelta = [cx + rr * math.cos(a1), cy + rr * math.sin(a1)] + vuelta
        poly(c, ida + vuelta, fill=D3, outline=D4, width=1)
    circ(c, cx, cy, r * 0.3, fill=D2, outline=D4, width=1.5)
    circ(c, cx, cy, r * 0.11, fill=aro[0], neon=True)
    anillo(c, cx, cy, r, max(2.5, r * 0.075), aro)


def pantalla(c, forma, bbox):
    """Fondo de pantalla «synthwave» (cielo, sol a franjas, montañas y rejilla), recortado a la forma de la pantalla."""
    x0, y0, x1, y1 = bbox
    w, h = x1 - x0, y1 - y0
    hz = y0 + h * 0.64
    cielo = ("#150A33", "#3B1170", "#8E2A96")

    def dib(d, capa):
        _pegar_degradado(capa, (x0, y0, x1, hz), cielo, vertical=True)
        _pegar_degradado(capa, (x0, hz, x1, y1), ("#1A0B3D", "#0A0620"), vertical=True)
        r = h * 0.27
        sx, sy = x0 + w * 0.5, hz - r * 0.3
        sol = f_resta(f_ell((sx - r, sy - r, sx + r, sy + r)), f_rect((x0 - 5, hz, x1 + 5, y1 + 5)))
        _pegar_degradado(capa, (sx - r, sy - r, sx + r, sy + r), ("#FDE68A", "#F472B6", "#C026D3"), vertical=True, forma=sol)
        for i in range(4):
            yy, hh = sy + r * (0.08 + i * 0.2), 0.9 + i * 0.9
            if yy + hh < hz:
                d.rectangle(box(sx - r - 1, yy, sx + r + 1, yy + hh), fill=color_en(cielo, (yy - y0) / (hz - y0)))
        d.polygon(_xy((x0, hz, x0 + w * 0.1, hz - h * 0.13, x0 + w * 0.2, hz - h * 0.05, x0 + w * 0.29, hz - h * 0.1,
                       x0 + w * 0.38, hz)), fill="#1E0B45")
        d.polygon(_xy((x1 - w * 0.4, hz, x1 - w * 0.3, hz - h * 0.08, x1 - w * 0.2, hz - h * 0.15, x1 - w * 0.1, hz - h * 0.06,
                       x1, hz - h * 0.1, x1, hz)), fill="#1E0B45")
        for i in range(-12, 13):
            d.line(_xy((sx + i * w * 0.03, hz, sx + i * w * 0.17, y1 + 2)), fill="#C13BD6", width=_w(0.8))
        for k in range(1, 6):
            yy = hz + (y1 - hz) * (k / 5) ** 1.8
            d.line(_xy((x0, yy, x1, yy)), fill="#C13BD6", width=_w(0.8))
        d.line(_xy((x0, hz, x1, hz)), fill=CIAN, width=_w(1.2))

    capa_recortada(c, forma, dib)
    degradado(c, bbox, ("#6D28D980", "#C026D380"), forma, vertical=True, solo_brillo=True)


def arte_portada(c, bbox, stops, acento, planeta):
    """Portada abstracta de videojuego (sin texto): cielo, planeta con anillo, sierra y un tajo de neón."""
    x0, y0, x1, y1 = bbox
    w, h = x1 - x0, y1 - y0

    def dib(d, capa):
        _pegar_degradado(capa, bbox, stops, vertical=True)
        px, py, pr = x0 + w * 0.6, y0 + h * 0.34, w * 0.26
        _pegar_degradado(capa, (px - pr, py - pr, px + pr, py + pr), planeta, vertical=True,
                         forma=f_ell((px - pr, py - pr, px + pr, py + pr)))
        d.arc(box(px - pr * 1.7, py - pr * 0.45, px + pr * 1.7, py + pr * 0.45), 160, 380, fill=_rgba(W, 200), width=_w(2))
        for sx, sy in ((0.12, 0.12), (0.3, 0.26), (0.2, 0.5), (0.86, 0.1), (0.9, 0.62), (0.4, 0.08)):
            d.ellipse(box(x0 + w * sx - 1.2, y0 + h * sy - 1.2, x0 + w * sx + 1.2, y0 + h * sy + 1.2), fill=WW)
        d.polygon(_xy((x0, y1, x0, y0 + h * 0.7, x0 + w * 0.18, y0 + h * 0.58, x0 + w * 0.34, y0 + h * 0.72, x0 + w * 0.5,
                       y0 + h * 0.6, x0 + w * 0.72, y0 + h * 0.76, x0 + w * 0.88, y0 + h * 0.66, x1, y0 + h * 0.74, x1, y1)),
                  fill="#0B0717")
        d.line(_xy((x0 + w * 0.08, y0 + h * 0.94, x0 + w * 0.92, y0 + h * 0.8)), fill=acento, width=_w(3))

    capa_recortada(c, f_rect(bbox), dib)
    line(c, (x0 + w * 0.08, y0 + h * 0.94, x0 + w * 0.92, y0 + h * 0.8), acento, 2, neon=True)


# ------------------------------------------------------------------------------------------------ componentes de PC
def gpu(c):
    rrect(c, (28, 102, 46, 250), 3, fill=M1, outline=M2, width=1)                    # soporte metálico
    for y in (116, 146, 176, 206):
        rrect(c, (32, y, 42, y + 20), 2, fill=D0)
    rrect(c, (98, 226, 262, 248), 3, fill=ORO)                                       # conector PCIe
    for x in range(102, 260, 5):
        line(c, (x, 231, x, 246), ORO_OSC, 1.4)
    cortar(c, f_rect((134, 234, 140, 249)))
    rrect(c, (240, 96, 274, 112), 3, fill=D0, outline=D4, width=1)                   # conector de energía
    rrect(c, (44, 106, 328, 232), 16, fill=D2, outline=D5, width=2)
    poly(c, (58, 122, 314, 122, 320, 130, 320, 220, 58, 220, 52, 212), fill=D3)
    degradado(c, (66, 111, 306, 117), RGB, f_rrect((66, 111, 306, 117), 3), neon=True)
    for x in (60, 70):
        line(c, (x, 214, x + 12, 196), VIOLETA, 2.5, neon=True)
        line(c, (x + 250, 214, x + 262, 196), CIAN, 2.5, neon=True)
    for cx, aro in ((101, (VIOLETA, MAGENTA)), (186, (MAGENTA, CIAN)), (271, (CIAN, VIOLETA))):
        ventilador(c, cx, 171, 40, aro=aro)


def cpu(c):
    rrect(c, (78, 78, 282, 282), 12, fill="#123524", outline="#2B6A48", width=2)
    cortar(c, f_ell((70, 112, 86, 128)))
    cortar(c, f_ell((274, 112, 290, 128)))
    for i in range(12):
        v = 94 + i * 15.5
        for x0, y0 in ((v, 86), (v, 268), (86, v), (268, v)):
            rrect(c, (x0 - 2.5, y0 - 2.5, x0 + 2.5, y0 + 2.5), 1, fill=ORO)
    for x, y in ((100, 100), (108, 100), (252, 258), (260, 258), (100, 250), (252, 100)):
        rrect(c, (x - 3, y - 1.5, x + 3, y + 1.5), 0.5, fill=M2)
    poly(c, (86, 274, 86, 262, 98, 274), fill=ORO)
    degradado(c, (108, 108, 252, 252), (WW, M3, M2, M1), f_rrect((108, 108, 252, 252), 18), vertical=True)
    rrect(c, (108, 108, 252, 252), 18, outline=M1, width=2)
    capa_recortada(c, f_rrect((108, 108, 252, 252), 18),
                   lambda d, capa: d.polygon(_xy((108, 180, 180, 108, 214, 108, 108, 214)), fill=(255, 255, 255, 60)))
    for y, x1 in ((206, 226), (218, 196), (230, 212)):
        line(c, (134, y, x1, y), M1, 2)
    rrect(c, (132, 132, 162, 146), 3, fill=None, outline=M1, width=1.5)
    rrect(c, (72, 72, 288, 288), 16, outline=CIAN, width=2.5, neon=True)


def motherboard(c):
    rrect(c, (58, 48, 302, 312), 8, fill=D1, outline=D5, width=2)
    for pts in ((130, 170, 130, 196, 210, 196), (200, 200, 270, 200, 270, 220), (100, 256, 100, 290, 180, 290),
                (206, 150, 206, 186), (74, 190, 74, 250), (230, 300, 290, 300)):
        line(c, pts, D3, 1.5)
    for x, y in ((68, 58), (292, 58), (68, 302), (292, 302), (206, 302)):
        circ(c, x, y, 4, fill=M1)
    rrect(c, (64, 58, 96, 176), 6, fill=D3, outline=D4, width=1)                    # tapa de puertos
    degradado(c, (87, 66, 92, 168), (VIOLETA, CIAN), f_rrect((87, 66, 92, 168), 2), vertical=True, neon=True)
    rrect(c, (100, 60, 124, 170), 5, fill=D4)                                         # disipadores VRM
    rrect(c, (100, 56, 206, 82), 5, fill=D4)
    for y in range(90, 166, 8):
        line(c, (104, y, 120, y), D3, 2)
    for x in range(130, 204, 8):
        line(c, (x, 60, x, 78), D3, 2)
    rrect(c, (130, 90, 200, 160), 4, fill=D3, outline=M1, width=2)                    # zócalo
    rrect(c, (140, 100, 190, 150), 3, fill=M2)
    rrect(c, (148, 108, 182, 142), 2, fill=M3)
    line(c, (204, 94, 204, 158), M2, 3)
    for x in (214, 227, 240, 253):                                                   # ranuras de memoria
        rrect(c, (x, 68, x + 8, 190), 2, fill=D0, outline=D4, width=1)
        rrect(c, (x - 1, 62, x + 9, 70), 1.5, fill=M1)
        rrect(c, (x - 1, 188, x + 9, 196), 1.5, fill=M1)
    rrect(c, (280, 86, 294, 164), 2, fill=D0, outline=D4, width=1)                   # conector de 24 pines
    rrect(c, (84, 204, 264, 216), 2, fill=D0, outline=M1, width=2)                    # ranuras PCIe
    rrect(c, (84, 262, 264, 272), 2, fill=D0, outline=D4, width=1)
    rrect(c, (96, 226, 198, 252), 4, fill=D4)                                         # disipador M.2
    line(c, (104, 239, 190, 239), CIAN, 2, neon=True)
    rrect(c, (214, 226, 288, 298), 8, fill=D3, outline=D4, width=1)                   # chipset
    circ(c, 251, 262, 18, fill=D2)
    anillo(c, 251, 262, 18, 3, (VIOLETA, MAGENTA))
    circ(c, 124, 292, 10, fill=M2, outline=M1, width=1.5)                             # pila
    for x in (74, 86):
        circ(c, x, 286, 5, fill=ORO)


def _modulo_ram(c, x0, y0, oscuro=False):
    w = 270
    cuerpo, faceta = (D2, D1) if oscuro else (D3, D2)
    rrect(c, (x0 + 4, y0 + 80, x0 + w - 4, y0 + 112), 3, fill="#123524")
    rrect(c, (x0 + 10, y0 + 98, x0 + w - 10, y0 + 112), 1, fill=ORO)
    for x in range(int(x0) + 12, int(x0 + w) - 10, 4):
        line(c, (x, y0 + 100, x, y0 + 111), ORO_OSC, 1)
    cortar(c, f_rect((x0 + 110, y0 + 100, x0 + 116, y0 + 113)))
    poly(c, (x0, y0 + 20, x0 + 16, y0 + 6, x0 + w - 16, y0 + 6, x0 + w, y0 + 20, x0 + w, y0 + 92, x0, y0 + 92),
         fill=cuerpo, outline=D5, width=1.5)
    poly(c, (x0 + 2, y0 + 62, x0 + 150, y0 + 62, x0 + 170, y0 + 44, x0 + w - 2, y0 + 44, x0 + w - 2, y0 + 90, x0 + 2, y0 + 90),
         fill=faceta)
    line(c, (x0 + 150, y0 + 62, x0 + 170, y0 + 44), MAGENTA if not oscuro else VIOLETA, 2, neon=True)
    degradado(c, (x0 + 4, y0 - 8, x0 + w - 4, y0 + 10), RGB, f_rrect((x0 + 4, y0 - 8, x0 + w - 4, y0 + 10), 6), neon=True)


def ram(c):
    _modulo_ram(c, 56, 92, oscuro=True)
    _modulo_ram(c, 40, 142)


def ssd(c):
    rrect(c, (30, 138, 330, 222), 8, fill=D1, outline=D5, width=2)
    rrect(c, (30, 146, 58, 214), 3, fill=ORO)
    for y in range(150, 212, 4):
        line(c, (32, y, 56, y), ORO_OSC, 1.2)
    cortar(c, f_rect((26, 186, 48, 192)))
    cortar(c, f_ell((318, 168, 342, 192)))
    rrect(c, (72, 154, 116, 206), 4, fill=D0, outline=D4, width=1)                   # controlador
    rrect(c, (126, 160, 152, 200), 3, fill=D0, outline=D4, width=1)                  # DRAM
    for x in (164, 244):                                                             # memorias NAND
        rrect(c, (x, 150, x + 66, 210), 4, fill=D0, outline=D4, width=1)
        line(c, (x + 6, 156, x + 60, 156), D3, 1.5)
    degradado(c, (164, 176, 310, 184), RGB, f_rrect((164, 176, 310, 184), 3), neon=True)
    for x in (80, 88, 96, 104):
        rrect(c, (x, 214, x + 4, 218), 0.5, fill=M1)
    rrect(c, (24, 132, 336, 228), 12, outline=CIAN, width=2, neon=True)


def hdd(c):
    rrect(c, (82, 44, 278, 316), 14, fill=M1, outline=M3, width=2)
    rrect(c, (94, 56, 266, 304), 10, fill=D3)
    degradado(c, (98, 62, 262, 226), (WW, M2, M3, M1), f_ell((98, 62, 262, 226)), vertical=True)
    ell(c, (98, 62, 262, 226), outline=M1, width=2)

    def reflejos(d, capa):
        d.arc(box(112, 76, 248, 212), 195, 250, fill=(255, 255, 255, 170), width=_w(3))
        d.arc(box(126, 90, 234, 198), 15, 70, fill=(255, 255, 255, 120), width=_w(3))
        d.arc(box(104, 68, 256, 220), 110, 160, fill=_rgba(VIOLETA, 150), width=_w(3))

    capa_recortada(c, None, reflejos)
    circ(c, 180, 144, 24, fill=M2, outline=M1, width=2)
    circ(c, 180, 144, 10, fill=M1)
    for a in range(0, 360, 60):
        circ(c, 180 + 17 * math.cos(math.radians(a)), 144 + 17 * math.sin(math.radians(a)), 2.2, fill=D4)
    poly(c, (210, 282, 264, 282, 264, 300, 210, 300), fill=D1)                       # imán
    poly(c, (226, 250, 250, 266, 184, 114, 176, 118), fill=D4)                       # brazo
    poly(c, (168, 104, 188, 110, 186, 124, 166, 118), fill=D2)                       # cabezal
    circ(c, 238, 258, 20, fill=D2, outline=D4, width=2)
    circ(c, 238, 258, 8, fill=M1)
    for x, y in ((92, 54), (268, 54), (92, 306), (268, 306)):
        circ(c, x, y, 5, fill=M2, outline=M1, width=1)
    circ(c, 112, 288, 5, fill=CIAN, neon=True)


def psu(c):
    def cara(u, v):
        return (252 + u * 44, 96 - u * 34 + v * 200)

    poly(c, (52, 96, 96, 62, 296, 62, 252, 96), fill=D3, outline=D5, width=1.5)     # tapa
    poly(c, (120, 86, 136, 72, 250, 72, 234, 86), fill=D4)
    poly(c, (252, 96, 296, 62, 296, 262, 252, 296), fill=D1, outline=D5, width=1.5)  # lateral de conectores
    for u0 in (0.18, 0.58):
        for v0 in (0.12, 0.34, 0.56):
            poly(c, cara(u0, v0) + cara(u0 + 0.28, v0) + cara(u0 + 0.28, v0 + 0.14) + cara(u0, v0 + 0.14), fill=D0)
    poly(c, cara(0.25, 0.76) + cara(0.75, 0.76) + cara(0.75, 0.92) + cara(0.25, 0.92), fill=D0, outline=D4, width=1)
    rrect(c, (52, 96, 252, 296), 6, fill=D2, outline=D5, width=2)                    # cara del ventilador
    ventilador(c, 152, 196, 82, aro=(CIAN, VIOLETA), aspas=11)
    for r in (36, 58):
        circ(c, 152, 196, r, outline=_rgba(M1, 170), width=1.5)
    for a in range(0, 180, 45):
        dx, dy = 82 * math.cos(math.radians(a)), 82 * math.sin(math.radians(a))
        line(c, (152 - dx, 196 - dy, 152 + dx, 196 + dy), _rgba(M1, 170), 1.5)
    for x, y in ((64, 108), (240, 108), (64, 284), (240, 284)):
        circ(c, x, y, 4, fill=M1)
    line(c, (54, 96, 250, 96), CIAN, 2, neon=True)


def case(c):
    poly(c, (66, 44, 110, 24, 286, 24, 242, 44), fill=D3, outline=D5, width=1.5)     # techo
    for x in range(120, 240, 12):
        line(c, (x, 40, x + 30, 27), D2, 2)
    poly(c, (242, 44, 286, 24, 286, 296, 242, 316), fill=D2, outline=D5, width=1.5)  # frente con ventiladores
    for cy in (92, 164, 236):
        cyc = cy - 12
        ell(c, (250, cyc - 30, 280, cyc + 30), fill=D0)
        ell(c, (250, cyc - 30, 280, cyc + 30), outline=CIAN, width=3, neon=True)
        ell(c, (259, cyc - 10, 271, cyc + 10), fill=D3)
    rrect(c, (66, 44, 242, 316), 8, fill=D1, outline=D5, width=2)                    # lateral de vidrio
    rrect(c, (76, 54, 232, 306), 5, fill="#0C111C")
    rrect(c, (90, 64, 200, 212), 3, fill=D2, outline=D3, width=1)                    # placa madre
    rrect(c, (92, 58, 200, 63), 2, fill=D3)
    line(c, (98, 60, 194, 60), VIOLETA, 2, neon=True)
    circ(c, 136, 112, 26, fill=D3)
    anillo(c, 136, 112, 26, 3.5, (VIOLETA, MAGENTA))
    circ(c, 136, 112, 8, fill=D4)
    degradado(c, (172, 76, 178, 146), RGB, f_rrect((172, 76, 178, 146), 2), vertical=True, neon=True)
    degradado(c, (182, 76, 188, 146), RGB, f_rrect((182, 76, 188, 146), 2), vertical=True, neon=True)
    rrect(c, (86, 176, 226, 202), 4, fill=D3, outline=D4, width=1)                   # tarjeta de video
    degradado(c, (94, 178, 218, 182), RGB, f_rrect((94, 178, 218, 182), 2), neon=True)
    trazo(c, curva([(206, 150), (214, 190), (212, 236)]), D0, 5)
    rrect(c, (76, 240, 232, 306), 3, fill=D2)                                         # cubierta de la fuente
    for x in range(92, 218, 10):
        line(c, (x, 256, x, 290), D3, 3)

    def reflejo(d, capa):
        d.polygon(_xy((160, 54, 196, 54, 104, 306, 76, 306, 76, 290)), fill=(255, 255, 255, 18))

    capa_recortada(c, f_rrect((76, 54, 232, 306), 5), reflejo)
    rrect(c, (80, 316, 110, 324), 2, fill=D3)
    rrect(c, (200, 316, 230, 324), 2, fill=D3)


def cooler_aio(c):
    trazo(c, curva([(76, 138), (72, 200), (112, 246), (152, 258)]), D0, 14)            # mangueras
    trazo(c, curva([(76, 138), (72, 200), (112, 246), (152, 258)]), D3, 5)
    trazo(c, curva([(102, 138), (104, 186), (136, 226), (160, 234)]), D0, 14)
    trazo(c, curva([(102, 138), (104, 186), (136, 226), (160, 234)]), D3, 5)
    rrect(c, (40, 48, 320, 140), 8, fill=D2, outline=D5, width=2)                     # radiador
    for x in range(46, 316, 5):
        line(c, (x, 52, x, 136), D3, 1.2)
    for cx, aro in ((110, (VIOLETA, MAGENTA)), (250, (CIAN, VIOLETA))):
        rrect(c, (cx - 46, 50, cx + 46, 138), 10, fill=D1, outline=D4, width=1.5)
        ventilador(c, cx, 94, 40, aro=aro)
    circ(c, 208, 250, 56, fill=D2, outline=D5, width=2)                               # bomba
    circ(c, 208, 250, 44, fill=D0)
    anillo(c, 208, 250, 44, 5, (VIOLETA, CIAN))
    anillo(c, 208, 250, 32, 3, (CIAN, MAGENTA))
    anillo(c, 208, 250, 20, 2.5, (MAGENTA, VIOLETA))
    circ(c, 208, 250, 8, fill=D3)


def cooler_air(c):
    for x in (124, 146, 214, 236):                                                   # heatpipes
        rrect(c, (x - 7, 34, x + 7, 70), 7, fill=M2, outline=M1, width=1)
    rrect(c, (104, 58, 256, 300), 6, fill=M1)                                         # aletas
    for y in range(64, 298, 6):
        line(c, (106, y, 254, y), D4, 1.5)
    rrect(c, (98, 52, 262, 74), 8, fill=D3, outline=D5, width=1.5)
    degradado(c, (112, 60, 248, 66), RGB, f_rrect((112, 60, 248, 66), 3), neon=True)
    rrect(c, (80, 104, 280, 288), 24, fill=D2, outline=D5, width=2)
    ventilador(c, 180, 196, 84, aro=(VIOLETA, CIAN), aspas=9)
    rrect(c, (134, 300, 226, 314), 3, fill="#C77B3B")


# ------------------------------------------------------------------------------------------------ equipos
def laptop(c):
    poly(c, (78, 44, 282, 44, 292, 206, 68, 206), fill=D1, outline=D5, width=2)      # tapa
    pantalla(c, f_poly((88, 56, 272, 56, 280, 196, 80, 196)), (80, 56, 280, 196))
    poly(c, (26, 284, 334, 284, 330, 296, 30, 296), fill=D0)                          # canto
    poly(c, (68, 206, 292, 206, 334, 284, 26, 284), fill=D2, outline=D5, width=2)    # base
    degradado(c, (112, 207, 248, 212), RGB, f_rrect((112, 207, 248, 212), 2), neon=True)

    def borde(y, lado):
        k = (y - 206) * 42 / 78
        return (68 - k + 16) if lado < 0 else (292 + k - 16)

    for r in range(4):
        yt, yb = 218 + r * 12, 227 + r * 12
        n = 14
        for k in range(n):
            f0, f1 = (k + 0.08) / n, (k + 0.92) / n
            lt, rt, lb, rb = borde(yt, -1), borde(yt, 1), borde(yb, -1), borde(yb, 1)
            pts = (lt + (rt - lt) * f0, yt, lt + (rt - lt) * f1, yt, lb + (rb - lb) * f1, yb, lb + (rb - lb) * f0, yb)
            tono = color_en(RGB, k / (n - 1))
            poly(c, pts, fill=mezcla(D3, tono, 0.35))
            c.b.polygon(_xy(pts), fill=tono)
    poly(c, (144, 268, 216, 268, 220, 281, 140, 281), fill=D3, outline=D4, width=1)


def desktop(c):
    rrect(c, (22, 56, 236, 196), 8, fill=D0, outline=D5, width=2)                    # monitor
    pantalla(c, f_rrect((30, 64, 228, 188), 4), (30, 64, 228, 188))
    poly(c, (118, 196, 140, 196, 146, 238, 112, 238), fill=D3, outline=D4, width=1)
    rrect(c, (84, 236, 174, 246), 4, fill=D3, outline=D4, width=1)
    rrect(c, (252, 58, 334, 290), 8, fill=D1, outline=D5, width=2)                   # torre
    degradado(c, (258, 68, 264, 280), RGB, f_rrect((258, 68, 264, 280), 3), vertical=True, neon=True)
    for cy, aro in ((110, (VIOLETA, CIAN)), (172, (CIAN, MAGENTA)), (234, (MAGENTA, VIOLETA))):
        circ(c, 298, cy, 24, fill=D0)
        anillo(c, 298, cy, 24, 3, aro)
        circ(c, 298, cy, 7, fill=D3)
    circ(c, 322, 72, 3.5, fill=CIAN, neon=True)
    poly(c, (40, 262, 226, 262, 236, 296, 30, 296), fill=D2, outline=D5, width=1.5)  # teclado
    for r in range(3):
        yt, yb = 268 + r * 9, 274 + r * 9
        for k in range(16):
            f0, f1 = (k + 0.1) / 16, (k + 0.9) / 16
            lt = 44 - (yt - 262) * 10 / 34
            rt = 222 + (yt - 262) * 10 / 34
            lb = 44 - (yb - 262) * 10 / 34
            rb = 222 + (yb - 262) * 10 / 34
            pts = (lt + (rt - lt) * f0, yt, lt + (rt - lt) * f1, yt, lb + (rb - lb) * f1, yb, lb + (rb - lb) * f0, yb)
            tono = color_en(RGB, k / 15)
            poly(c, pts, fill=mezcla(D3, tono, 0.35))
            c.b.polygon(_xy(pts), fill=tono)


def monitor(c):
    rrect(c, (20, 44, 340, 238), 10, fill=D0, outline=D5, width=2)
    pantalla(c, f_rrect((28, 52, 332, 228), 4), (28, 52, 332, 228))
    degradado(c, (110, 238, 250, 242), RGB, f_rrect((110, 238, 250, 242), 2), neon=True)
    poly(c, (166, 238, 194, 238, 200, 292, 160, 292), fill=D3, outline=D4, width=1)
    poly(c, (180, 284, 266, 306, 260, 316, 180, 298, 100, 316, 94, 306), fill=D3, outline=D4, width=1)


# ------------------------------------------------------------------------------------------------ periféricos
def keyboard(c):
    trazo(c, curva([(180, 134), (176, 102), (198, 72), (192, 38)]), D0, 7)
    degradado(c, (22, 128, 338, 254), RGB, f_rrect((22, 128, 338, 254), 16), solo_brillo=True)
    rrect(c, (26, 132, 334, 250), 14, fill=D1, outline=D5, width=2)
    rrect(c, (34, 140, 326, 242), 8, fill=D0)
    filas = ([1] * 15, [1.5] + [1] * 12 + [1.5], [1.75] + [1] * 11 + [2.25], [2.25] + [1] * 10 + [2.75],
             [1.25, 1.25, 1.25, 6.25, 1.25, 1.25, 1.25, 1.25])
    u = 290 / 15
    for r, fila in enumerate(filas):
        y0, y1 = 142 + r * 20, 142 + r * 20 + 17.5
        acc = 0
        for wu in fila:
            x0, x1 = 36 + acc * u + 1.3, 36 + (acc + wu) * u - 1.3
            acc += wu
            tono = color_en(RGB, ((x0 + x1) / 2 - 36) / 290)
            c.b.rounded_rectangle(box(x0, y0, x1, y1), radius=3 * K, fill=tono)
            rrect(c, (x0, y0, x1, y1), 3, fill=D3)
            rrect(c, (x0 + 2, y0 + 1.5, x1 - 2, y1 - 4), 2.5, fill=D4)
            line(c, (x0 + 3, y1 - 0.8, x1 - 3, y1 - 0.8), tono, 1.2)


def mouse(c):
    trazo(c, curva([(180, 70), (182, 46), (204, 28), (236, 20)]), D0, 6)
    for b, v in (((126, 64, 234, 220), 0), ((112, 120, 248, 312), 0)):
        ell(c, (b[0] - 2.5, b[1] - 2.5, b[2] + 2.5, b[3] + 2.5), fill=D5)
    ell(c, (126, 64, 234, 220), fill=D2)
    ell(c, (112, 120, 248, 312), fill=D2)
    arc(c, (116, 124, 244, 308), 140, 215, VIOLETA, 4, neon=True)
    arc(c, (116, 124, 244, 308), 325, 40, CIAN, 4, neon=True)
    line(c, (180, 68, 180, 168), D0, 3)
    trazo(c, curva([(130, 158), (180, 172), (230, 158)]), D0, 3)
    rrect(c, (170, 90, 190, 140), 9, fill=D0)
    rrect(c, (175, 97, 185, 133), 5, fill=CIAN, neon=True)
    rrect(c, (175, 146, 185, 155), 2, fill=D4)
    rrect(c, (112, 170, 124, 196), 4, fill=D4)
    rrect(c, (112, 202, 124, 228), 4, fill=D4)
    degradado(c, (158, 258, 202, 263), (VIOLETA, MAGENTA), f_rrect((158, 258, 202, 263), 2.5), neon=True)


def headset(c):
    arc(c, (74, 38, 286, 250), 180, 360, D3, 20)
    arc(c, (86, 50, 274, 238), 195, 345, D1, 7)
    arc(c, (69, 33, 291, 255), 215, 325, VIOLETA, 3, neon=True)
    rrect(c, (68, 138, 92, 178), 4, fill=M1)
    rrect(c, (268, 138, 292, 178), 4, fill=M1)
    trazo(c, curva([(70, 268), (86, 300), (130, 312), (160, 302)]), D3, 7)            # micrófono
    circ(c, 164, 300, 9, fill=D0)
    circ(c, 164, 300, 4, fill=MAGENTA, neon=True)
    for x0, x1, cx, cojin in ((50, 122, 86, (110, 128)), (238, 310, 274, (232, 250))):
        rrect(c, (cojin[0], 178, cojin[1], 272), 8, fill=D0)
        rrect(c, (x0, 164, x1, 286), 30, fill=D2, outline=D5, width=2)
        circ(c, cx, 225, 24, fill=D3)
        anillo(c, cx, 225, 24, 4, (CIAN, VIOLETA))
        circ(c, cx, 225, 9, fill=D4)


def mousepad(c):
    poly(c, (24, 282, 336, 282, 336, 292, 24, 292), fill=D0)
    exterior = (54, 150, 306, 150, 336, 282, 24, 282)
    interior = (60, 156, 300, 156, 327, 276, 33, 276)
    poly(c, exterior, fill=D1)

    def trama(d, capa):
        for i in range(-20, 40):
            x = i * 12
            d.line(_xy((x, 150, x + 60, 290)), fill=_rgba(D3, 255), width=_w(1))

    capa_recortada(c, f_poly(interior), trama)
    degradado(c, (24, 150, 336, 282), RGB, f_resta(f_poly(exterior), f_poly(interior)), neon=True)
    ell(c, (212, 172, 262, 252), fill=D3, outline=D5, width=1.5)                      # ratón
    line(c, (237, 174, 237, 204), D0, 2)
    rrect(c, (233, 182, 241, 196), 3, fill=CIAN, neon=True)
    arc(c, (214, 174, 260, 250), 120, 200, VIOLETA, 2.5, neon=True)


def webcam(c):
    rrect(c, (30, 214, 330, 330), 10, fill=D0, outline=D5, width=2)                   # borde superior del monitor
    pantalla(c, f_rrect((40, 224, 320, 330), 3), (40, 224, 320, 330))

    def oscurecer(d, capa):
        _pegar_degradado(capa, (28, 214, 332, 332), ("#090C1500", "#090C15FF"), vertical=True)

    capa_recortada(c, None, oscurecer)
    rrect(c, (168, 168, 192, 196), 4, fill=D3)                                        # cuello y pinza
    rrect(c, (116, 192, 244, 212), 5, fill=D3, outline=D4, width=1)
    rrect(c, (116, 206, 132, 234), 4, fill=D3, outline=D4, width=1)
    circ(c, 180, 184, 6, fill=M1)
    rrect(c, (70, 88, 290, 172), 42, fill=D2, outline=D5, width=2)
    rrect(c, (80, 98, 280, 162), 32, fill=D1)
    circ(c, 180, 130, 40, fill=D0, outline=M1, width=3)
    anillo(c, 180, 130, 44, 2.5, (VIOLETA, CIAN))
    degradado(c, (152, 102, 208, 158), ("#1E1B4B", "#4C1D95", "#0E7490"), f_ell((152, 102, 208, 158)), vertical=True)
    circ(c, 180, 130, 13, fill=D0)

    def brillos(d, capa):
        d.ellipse(box(163, 112, 173, 122), fill=(255, 255, 255, 200))
        d.arc(box(158, 108, 202, 152), 20, 80, fill=(255, 255, 255, 110), width=_w(2.5))

    capa_recortada(c, None, brillos)
    circ(c, 240, 130, 5, fill=CIAN, neon=True)
    for i in range(4):
        circ(c, 102 + i * 8, 130, 2, fill=D4)


def chair(c):
    for x, y in ((74, 296), (116, 318), (244, 318), (286, 296), (180, 310)):
        line(c, (180, 286, x, y), D3, 10)
        circ(c, x, y + 6, 9, fill=D0, outline=D4, width=1.5)
    rrect(c, (168, 262, 192, 292), 4, fill=D3)
    rrect(c, (173, 232, 187, 270), 3, fill=M1)
    rrect(c, (72, 184, 84, 234), 2, fill=D1)
    rrect(c, (276, 184, 288, 234), 2, fill=D1)
    poly(c, (92, 200, 268, 200, 282, 236, 78, 236), fill=D3, outline=D5, width=2)     # asiento
    poly(c, (78, 236, 282, 236, 276, 250, 84, 250), fill=D1)
    rrect(c, (60, 176, 96, 190), 5, fill=D1, outline=D4, width=1)                    # apoyabrazos
    rrect(c, (264, 176, 300, 190), 5, fill=D1, outline=D4, width=1)
    respaldo = (124, 34, 236, 34, 256, 62, 252, 122, 240, 150, 250, 200, 110, 200, 120, 150, 108, 122, 104, 62)
    poly(c, respaldo, fill=D2, outline=D5, width=2)
    for signo in (1, -1):
        pts = [(180 + signo * (180 - x), y) for x, y in ((130, 42), (116, 66), (116, 120), (128, 150), (120, 196))]
        trazo(c, curva(pts), MAGENTA, 4, neon=True)
    rrect(c, (148, 48, 212, 80), 12, fill=D1, outline=D4, width=1)
    rrect(c, (140, 146, 220, 176), 12, fill=D1, outline=D4, width=1)
    rrect(c, (132, 92, 146, 114), 4, fill=D0)
    rrect(c, (214, 92, 228, 114), 4, fill=D0)
    line(c, (180, 84, 180, 142), D3, 2)


# ------------------------------------------------------------------------------------------------ consolas
def ps5(c):
    ell(c, (126, 300, 234, 324), fill=D1, outline=D4, width=1)
    rrect(c, (154, 44, 206, 306), 10, fill=D1, outline=D4, width=1)
    for x in (158, 202):
        line(c, (x, 56, x, 296), "#60A5FA", 2, neon=True)
    ys = list(range(24, 321, 6))

    def exterior(y):
        return 156 - (15 + 36 * ((y - 172) / 148) ** 2)

    izq = [v for y in ys for v in (exterior(y), y)] + [v for y in reversed(ys) for v in (156, y)]
    der = [v for i in range(0, len(izq), 2) for v in (360 - izq[i], izq[i + 1])]
    degradado(c, (100, 24, 157, 321), (M2, WW, W), f_poly(izq))
    degradado(c, (203, 24, 260, 321), (W, WW, M3), f_poly(der))
    poly(c, izq, outline=M1, width=1)
    poly(c, der, outline=M1, width=1)
    rrect(c, (172, 262, 188, 266), 1.5, fill=D0)
    circ(c, 180, 276, 3, fill=D3)


def ps4(c):
    A, B, C, D = (28, 196), (236, 146), (334, 192), (126, 244)
    t = 28

    def lerp(p, q, f):
        return (p[0] + (q[0] - p[0]) * f, p[1] + (q[1] - p[1]) * f)

    poly(c, A + D + (D[0], D[1] + t) + (A[0], A[1] + t), fill=D1, outline=D4, width=1)
    poly(c, D + C + (C[0], C[1] + t) + (D[0], D[1] + t), fill=D2, outline=D4, width=1)
    line(c, (D[0], D[1] + t * 0.5, C[0], C[1] + t * 0.5), D0, 3)
    line(c, (A[0], A[1] + t * 0.5, D[0], D[1] + t * 0.5), D0, 3)
    poly(c, A + B + C + D, fill=D3, outline=D5, width=1.5)
    G1, G2 = lerp(A, B, 0.64), lerp(D, C, 0.64)
    poly(c, G1 + B + C + G2, fill=D4)

    def reflejo(d, capa):
        p = lerp(G1, B, 0.3) + lerp(G1, B, 0.62) + lerp(G2, C, 0.62) + lerp(G2, C, 0.3)
        d.polygon(_xy(p), fill=(255, 255, 255, 26))
        p = lerp(A, G1, 0.2) + lerp(A, G1, 0.45) + lerp(D, G2, 0.45) + lerp(D, G2, 0.2)
        d.polygon(_xy(p), fill=(255, 255, 255, 12))

    capa_recortada(c, None, reflejo)
    line(c, G1 + G2, "#60A5FA", 3.5, neon=True)
    line(c, lerp(D, C, 0.06) + lerp(D, C, 0.46), D0, 2)


def xbox_series_x(c):
    poly(c, (130, 94, 172, 68, 254, 68, 212, 94), fill=D3, outline=D5, width=1.5)
    ell(c, (152, 72, 232, 90), fill=D0)
    ell(c, (158, 74, 226, 88), outline=VERDE, width=2, neon=True)
    for i in range(-3, 4):
        for j in (-1, 0, 1):
            circ(c, 192 + i * 7 + j * 3, 81 + j * 3.5, 1.1, fill=D4)
    poly(c, (212, 94, 254, 68, 254, 288, 212, 314), fill=D0, outline=D4, width=1.5)
    rrect(c, (130, 94, 212, 314), 3, fill=D1, outline=D5, width=1.5)
    line(c, (142, 118, 142, 252), D0, 3)
    circ(c, 198, 110, 5, fill=D3)
    circ(c, 198, 110, 5, outline=W, width=1.5, neon=True)
    rrect(c, (190, 294, 204, 299), 1, fill=D0)


def xbox_series_s(c):
    poly(c, (60, 168, 110, 132, 296, 132, 246, 168), fill=WW, outline=M2, width=1.5)
    ell(c, (178, 138, 262, 162), fill=D0)
    for r in (8, 16, 24, 32):
        ell(c, (220 - r * 1.25, 150 - r * 0.35, 220 + r * 1.25, 150 + r * 0.35), outline=D3, width=1)
    poly(c, (246, 168, 296, 132, 296, 226, 246, 262), fill=M3, outline=M2, width=1.5)
    rrect(c, (60, 168, 246, 262), 3, fill=W, outline=M2, width=1.5)
    circ(c, 80, 184, 4.5, fill=M2)
    circ(c, 80, 184, 4.5, outline=CIAN, width=1.2, neon=True)
    rrect(c, (80, 242, 96, 247), 1, fill=D1)
    circ(c, 226, 244, 3, fill=M2)


def _switch(c, x0, x1, y0, y1, izq, der, oscuro, ancho, r):
    pantalla_bbox = (x0 + ancho + 4, y0 + 8, x1 - ancho - 4, y1 - 8)
    for bx0, bx1, col in ((x0, x0 + ancho + 20, izq), (x1 - ancho - 20, x1, der)):
        rrect(c, (bx0, y0, bx1, y1), r, fill=D2 if oscuro else col, outline=D5 if oscuro else None, width=2 if oscuro else 0,
              neon=not oscuro)
    rrect(c, (x0 + ancho - 4, y0, x1 - ancho + 4, y1), 6, fill=D0, outline=D4, width=1)
    if oscuro:
        line(c, (x0 + ancho - 8, y0 + 12, x0 + ancho - 8, y1 - 12), izq, 3, neon=True)
        line(c, (x1 - ancho + 8, y0 + 12, x1 - ancho + 8, y1 - 12), der, 3, neon=True)
    pantalla(c, f_rrect(pantalla_bbox, 3), pantalla_bbox)
    ci, cd = x0 + (ancho - 4) / 2 + 2, x1 - (ancho - 4) / 2 - 2
    alto = y1 - y0
    for cx, cy_stick, cy_bot, col in ((ci, y0 + alto * 0.27, y0 + alto * 0.62, izq), (cd, y0 + alto * 0.62, y0 + alto * 0.27, der)):
        circ(c, cx, cy_stick, 15, fill=D0)
        circ(c, cx, cy_stick, 10, fill=D3)
        if oscuro:
            circ(c, cx, cy_stick, 16, outline=col, width=2, neon=True)
        for dx, dy in ((0, -12), (0, 12), (-12, 0), (12, 0)):
            circ(c, cx + dx, cy_bot + dy, 5.5, fill=D0 if not oscuro else D4)
    rrect(c, (ci - 6, y1 - 24, ci + 6, y1 - 14), 2, fill=D0)
    circ(c, cd - 6, y1 - 19, 5, fill=D3, outline=M1, width=1)


def switch(c):
    _switch(c, 34, 326, 122, 238, CIAN, ROJO, oscuro=False, ancho=58, r=30)


def switch2(c):
    _switch(c, 14, 346, 112, 248, CIAN, MAGENTA, oscuro=True, ancho=62, r=32)


def _silueta_mando(c, color, borde, cuerpo, grip):
    """Une elipse del cuerpo + dos empuñaduras con un borde común (primero el borde grueso, luego el relleno)."""
    grip_d = [360 - v if i % 2 == 0 else v for i, v in enumerate(grip)]
    formas = [("e", cuerpo), ("p", grip), ("p", grip_d), ("e", (grip[6] - 26, grip[7] - 42, grip[6] + 28, grip[7] + 4)),
              ("e", (360 - grip[6] - 28, grip[7] - 42, 360 - grip[6] + 26, grip[7] + 4))]
    for pasada, (f, o, w) in enumerate(((borde, borde, 5), (color, None, 0))):
        for tipo, g in formas:
            if tipo == "e":
                ell(c, g, fill=f, outline=o, width=w)
            else:
                poly(c, g, fill=f, outline=o, width=w)


def controller_ps(c):
    rrect(c, (80, 94, 142, 118), 10, fill=D3)
    rrect(c, (218, 94, 280, 118), 10, fill=D3)
    _silueta_mando(c, W, M1, (60, 104, 300, 214), (70, 150, 146, 176, 134, 262, 106, 290, 60, 256, 54, 196))
    poly(c, (118, 162, 242, 162, 256, 204, 226, 224, 134, 224, 104, 204), fill=D1)
    rrect(c, (128, 104, 232, 162), 12, fill=WW, outline=M2, width=1.5)
    line(c, (130, 116, 130, 150), CIAN, 2.5, neon=True)
    line(c, (230, 116, 230, 150), CIAN, 2.5, neon=True)
    for cx in (144, 216):
        circ(c, cx, 196, 17, fill=D0)
        circ(c, cx, 196, 11, fill=D3)
    for dx, dy in ((0, -12), (0, 12), (-12, 0), (12, 0)):
        rrect(c, (98 + dx - 5, 160 + dy - 5, 98 + dx + 5, 160 + dy + 5), 2, fill=D3)
        circ(c, 262 + dx, 160 + dy, 6, fill=D3)
    circ(c, 180, 202, 6, fill=D0, outline=M1, width=1)
    rrect(c, (170, 214, 190, 218), 2, fill=D3)
    rrect(c, (112, 124, 118, 136), 2, fill=D3)
    rrect(c, (242, 124, 248, 136), 2, fill=D3)


def controller_xbox(c):
    poly(c, (74, 112, 140, 96, 148, 112, 82, 128), fill=D1, outline=D4, width=1)
    poly(c, (286, 112, 220, 96, 212, 112, 278, 128), fill=D1, outline=D4, width=1)
    _silueta_mando(c, D3, D5, (58, 100, 302, 214), (62, 150, 142, 180, 130, 262, 104, 294, 56, 252, 50, 192))
    circ(c, 180, 128, 13, fill=D3)
    circ(c, 180, 128, 13, outline=W, width=2, neon=True)
    for cx, cy in ((112, 160), (214, 204)):
        circ(c, cx, cy, 18, fill=D0)
        circ(c, cx, cy, 12, fill=D3)
    rrect(c, (140, 200, 156, 212), 2, fill=D0)
    rrect(c, (142, 198, 154, 214), 2, fill=D0)
    rrect(c, (134, 200, 162, 212), 2, fill=D0)
    for (dx, dy), col in (((0, -14), AMBAR), ((-14, 0), AZUL), ((14, 0), ROJO), ((0, 14), VERDE)):
        circ(c, 250 + dx, 160 + dy, 7, fill=col, neon=True)
    for cx in (160, 200):
        circ(c, cx, 160, 4, fill=D4)


def joycon(c):
    for x0, x1, col, lado in ((84, 172, CIAN, -1), (188, 276, ROJO, 1)):
        rrect(c, (x0, 44, x1, 312), 44, fill=col, neon=True)
        recto = (x1 - 44, 44, x1, 312) if lado < 0 else (x0, 44, x0 + 44, 312)
        rrect(c, recto, 3, fill=col)
        riel = (x1 - 8, 58, x1 + 2, 298) if lado < 0 else (x0 - 2, 58, x0 + 8, 298)
        rrect(c, riel, 3, fill=D1)
    for cx, cy_stick, cy_bot in ((128, 100, 182), (232, 182, 100)):
        circ(c, cx, cy_stick, 20, fill=D0)
        circ(c, cx, cy_stick, 13, fill=D3)
        for dx, dy in ((0, -17), (0, 17), (-17, 0), (17, 0)):
            circ(c, cx + dx, cy_bot + dy, 8, fill=D0)
    rrect(c, (144, 62, 156, 67), 1.5, fill=D0)
    rrect(c, (120, 238, 136, 254), 3, fill=D0)
    circ(c, 232, 246, 8, fill=D3, outline=M1, width=1.5)
    rrect(c, (202, 62, 214, 67), 1.5, fill=D0)
    rrect(c, (205.5, 58.5, 210.5, 70.5), 1.5, fill=D0)


# ------------------------------------------------------------------------------------------------ juegos y accesorios
def _disco(c, cx, cy, r):
    degradado(c, (cx - r, cy - r, cx + r, cy + r), (M3, WW, M2, M3), f_ell((cx - r, cy - r, cx + r, cy + r)), vertical=True)

    def irisado(d, capa):
        d.arc(box(cx - r * 0.8, cy - r * 0.8, cx + r * 0.8, cy + r * 0.8), 200, 260, fill=_rgba(VIOLETA, 150), width=_w(6))
        d.arc(box(cx - r * 0.6, cy - r * 0.6, cx + r * 0.6, cy + r * 0.6), 20, 80, fill=_rgba(CIAN, 150), width=_w(6))

    capa_recortada(c, None, irisado)
    circ(c, cx, cy, r * 0.3, fill=M1)
    cortar(c, f_ell((cx - r * 0.14, cy - r * 0.14, cx + r * 0.14, cy + r * 0.14)))


def _estuche(c, b, banda, arte, acento, planeta):
    x0, y0, x1, y1 = b
    rrect(c, b, 8, fill=D0, outline=D5, width=2)
    rrect(c, (x0, y0, x1, y0 + 30), 8, fill=banda)
    rrect(c, (x0, y0 + 16, x1, y0 + 30), 0, fill=banda)
    rrect(c, (x0 + 14, y0 + 12, x0 + 60, y0 + 17), 2, fill=_rgba(WW, 230))
    arte_portada(c, (x0 + 8, y0 + 34, x1 - 8, y1 - 8), arte, acento, planeta)
    rrect(c, (x0, y0, x0 + 8, y1), 3, fill=mezcla(D0, banda, 0.25))


def game_ps(c):
    _disco(c, 240, 150, 76)
    _estuche(c, (72, 40, 236, 314), "#1D4ED8", ("#0B1A4A", "#3B1170", "#0E7490"), CIAN, (CIAN, VIOLETA_OSC))


def game_xbox(c):
    _disco(c, 240, 150, 76)
    _estuche(c, (72, 40, 236, 314), "#15803D", ("#07261A", "#0F3D3A", "#4C1D95"), VERDE, (VERDE, "#0E7490"))


def game_switch(c):
    _estuche(c, (78, 52, 222, 290), "#DC2626", ("#2A0A2E", "#6B1650", "#F59E0B"), MAGENTA, (AMBAR, "#DB2777"))
    tarjeta = (206, 196, 286, 196, 286, 300, 196, 300, 196, 206)
    poly(c, tarjeta, fill=D1, outline=D5, width=2)
    arte_portada(c, (206, 206, 276, 262), ("#1E0B45", "#C026D3"), CIAN, (CIAN, VIOLETA))
    for x in range(208, 278, 8):
        rrect(c, (x, 284, x + 4, 296), 1, fill=ORO)


def microsd(c):
    pts = (100, 56, 262, 56, 262, 304, 94, 304, 94, 196, 108, 182, 108, 122, 100, 114)
    poly(c, pts, fill=D1, outline=D5, width=2)
    for i in range(8):
        x = 118 + i * 17.5
        largo = 116 if i % 2 == 0 else 104
        rrect(c, (x, 66, x + 11, largo), 2, fill=ORO)
    degradado(c, (116, 190, 250, 292), (VIOLETA_OSC, VIOLETA, CIAN), f_rrect((116, 190, 250, 292), 8), vertical=True)
    for k in range(3):
        line(c, (130 + k * 26, 280, 176 + k * 26, 204), _rgba(WW, 120), 5)
    poly(c, pts, outline=CIAN, width=2, neon=True)


def router(c):
    for bx, tx, col in ((96, 70, VIOLETA), (146, 132, MAGENTA), (214, 228, MAGENTA), (264, 290, CIAN)):
        trazo(c, (bx, 192, tx, 62), D3, 16)
        trazo(c, (bx, 192, tx, 62), D4, 5)
        circ(c, tx, 62, 5, fill=col, neon=True)
    poly(c, (74, 184, 286, 184, 318, 216, 42, 216), fill=D2, outline=D5, width=1.5)
    poly(c, (42, 216, 318, 216, 318, 250, 42, 250), fill=D1, outline=D4, width=1.5)
    line(c, (140, 192, 180, 208, 220, 192), MAGENTA, 3, neon=True)
    for i in range(8):
        circ(c, 96 + i * 24, 233, 3.5, fill=CIAN, neon=True)
    rrect(c, (56, 250, 80, 256), 2, fill=D0)
    rrect(c, (280, 250, 304, 256), 2, fill=D0)


def cable(c):
    camino = curva([(64, 250), (80, 292), (150, 304), (212, 262), (196, 200), (130, 186), (116, 138), (156, 102), (222, 92)])
    trazo(c, camino, D0, 14)
    trazo(c, camino, D3, 6)
    line(c, camino, VIOLETA, 1.6, neon=True)
    rrect(c, (212, 82, 232, 102), 4, fill=D3)                                         # extremo HDMI
    rrect(c, (228, 70, 286, 114), 8, fill=D2, outline=D5, width=2)
    line(c, (236, 108, 278, 108), CIAN, 2, neon=True)
    rrect(c, (284, 78, 322, 106), 3, fill=M2, outline=M1, width=1)
    poly(c, (290, 84, 316, 84, 316, 96, 312, 100, 294, 100, 290, 96), fill=D0)
    rrect(c, (50, 204, 78, 252), 8, fill=D2, outline=D5, width=2)                     # extremo USB-C
    line(c, (54, 244, 74, 244), MAGENTA, 2, neon=True)
    rrect(c, (54, 180, 74, 206), 8, fill=M2, outline=M1, width=1)
    rrect(c, (59, 186, 69, 200), 4, fill=D0)


def license(c):
    rrect(c, (56, 74, 304, 286), 16, fill=D2, outline=D5, width=2)
    banda = f_resta(f_rrect((56, 74, 304, 140), 16), f_rect((50, 124, 310, 150)))
    degradado(c, (56, 74, 304, 124), (VIOLETA_OSC, VIOLETA, CIAN), banda)
    for x in (80, 96, 112):
        circ(c, x, 99, 5, fill=_rgba(WW, 220))
    poly(c, (258, 86, 282, 86, 282, 102, 270, 114, 258, 102), fill=WW)
    line(c, (263, 99, 268, 104, 277, 93), VIOLETA_OSC, 2.5)
    ell(c, (84, 150, 144, 210), outline=CIAN, width=10, neon=True)                    # llave
    line(c, (142, 180, 258, 180), CIAN, 10, neon=True)
    rrect(c, (218, 180, 230, 206), 2, fill=CIAN, neon=True)
    rrect(c, (240, 180, 252, 200), 2, fill=CIAN, neon=True)
    for g in range(4):
        x = 76 + g * 55
        rrect(c, (x, 236, x + 44, 256), 5, fill=D3)
        for k in range(4):
            circ(c, x + 8 + k * 9.5, 246, 2.2, fill=M1)


def service(c):
    cx, cy, dientes = 158, 170, 10
    pts = []
    for i in range(dientes):
        base = 2 * math.pi * i / dientes
        for off, r in ((-0.2, 84), (-0.12, 104), (0.12, 104), (0.2, 84)):
            a = base + off
            pts += [cx + r * math.cos(a), cy + r * math.sin(a)]
    poly(c, pts, fill=D3, outline=D5, width=2)
    circ(c, cx, cy, 62, fill=D2, outline=D4, width=2)
    anillo(c, cx, cy, 62, 3, (CIAN, VIOLETA))
    rrect(c, (cx - 28, cy - 28, cx + 28, cy + 28), 6, fill=D0, outline=M1, width=1.5)
    for k in range(5):
        v = -20 + k * 10
        for x0, y0, x1, y1 in ((cx + v, cy - 36, cx + v, cy - 29), (cx + v, cy + 29, cx + v, cy + 36),
                               (cx - 36, cy + v, cx - 29, cy + v), (cx + 29, cy + v, cx + 36, cy + v)):
            line(c, (x0, y0, x1, y1), ORO, 2.5)
    degradado(c, (cx - 14, cy - 14, cx + 14, cy + 14), (VIOLETA, CIAN), f_rrect((cx - 14, cy - 14, cx + 14, cy + 14), 3), neon=True)
    trazo(c, (206, 302, 290, 218), M1, 28)                                           # llave inglesa
    trazo(c, (206, 302, 290, 218), M2, 22)
    circ(c, 300, 208, 32, fill=M2, outline=M1, width=2)
    circ(c, 206, 302, 17, fill=M2, outline=M1, width=2)
    u, v = (0.7071, -0.7071), (0.7071, 0.7071)
    boca = (300 + v[0] * 11, 208 + v[1] * 11, 300 + v[0] * 11 + u[0] * 44, 208 + v[1] * 11 + u[1] * 44,
            300 - v[0] * 11 + u[0] * 44, 208 - v[1] * 11 + u[1] * 44, 300 - v[0] * 11, 208 - v[1] * 11)
    cortar(c, f_poly(boca))
    cortar(c, f_ell((199, 295, 213, 309)))
    line(c, (220, 288, 276, 232), CIAN, 3, neon=True)


# ------------------------------------------------------------------------------------------------ catálogo de dibujos
SOMBRA_PLANA = (50, 262, 310, 298)
DIBUJOS = {
    "gpu": (gpu, VIOLETA, (40, 244, 320, 276)),
    "cpu": (cpu, CIAN, None),
    "motherboard": (motherboard, VIOLETA, None),
    "ram": (ram, MAGENTA, (50, 254, 318, 286)),
    "ssd": (ssd, CIAN, (40, 222, 320, 252)),
    "hdd": (hdd, VIOLETA, None),
    "psu": (psu, CIAN, None),
    "case": (case, VIOLETA, None),
    "cooler_aio": (cooler_aio, CIAN, None),
    "cooler_air": (cooler_air, VIOLETA, None),
    "laptop": (laptop, MAGENTA, None),
    "desktop": (desktop, CIAN, None),
    "monitor": (monitor, VIOLETA, None),
    "keyboard": (keyboard, MAGENTA, (30, 244, 330, 276)),
    "mouse": (mouse, CIAN, None),
    "headset": (headset, VIOLETA, None),
    "mousepad": (mousepad, MAGENTA, (20, 280, 340, 306)),
    "webcam": (webcam, CIAN, (40, 318, 320, 340)),
    "chair": (chair, MAGENTA, None),
    "ps5": (ps5, CIAN, None),
    "ps4": (ps4, VIOLETA, (30, 250, 340, 290)),
    "xbox_series_x": (xbox_series_x, VIOLETA, None),
    "xbox_series_s": (xbox_series_s, CIAN, (50, 246, 310, 280)),
    "switch": (switch, MAGENTA, SOMBRA_PLANA),
    "switch2": (switch2, CIAN, (30, 256, 330, 290)),
    "controller_ps": (controller_ps, CIAN, None),
    "controller_xbox": (controller_xbox, VIOLETA, None),
    "joycon": (joycon, MAGENTA, None),
    "game_ps": (game_ps, CIAN, None),
    "game_xbox": (game_xbox, VIOLETA, None),
    "game_switch": (game_switch, MAGENTA, None),
    "microsd": (microsd, CIAN, None),
    "router": (router, VIOLETA, (40, 248, 320, 280)),
    "cable": (cable, CIAN, None),
    "license": (license, VIOLETA, None),
    "service": (service, CIAN, None),
}


# ------------------------------------------------------------------------------------------------ fondo y render
def fondo(acento):
    base = _tira((FONDO_ARRIBA, FONDO_ABAJO), 360, 360, vertical=True)
    halo = Image.new("RGBA", (360, 360), (0, 0, 0, 0))
    ImageDraw.Draw(halo).ellipse((60, 36, 300, 276), fill=_rgba(acento, 78))
    base.alpha_composite(halo.filter(ImageFilter.GaussianBlur(50)))
    img = base.resize((S, S), Image.BICUBIC)
    rejilla = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    rd = ImageDraw.Draw(rejilla)
    hz, color = 258, _rgba(acento, 90)
    for i in range(-10, 11):
        rd.line(_xy((180 + i * 7, hz, 180 + i * 46, 362)), fill=color, width=_w(0.9))
    for k in range(1, 8):
        y = hz + (362 - hz) * (k / 7) ** 1.7
        rd.line(_xy((0, y, 360, y)), fill=color, width=_w(0.9))
    desvanecer = Image.new("L", (S, S), 0)
    desvanecer.paste(Image.linear_gradient("L").resize((S, S - int(hz * K))), (0, int(hz * K)))
    rejilla.putalpha(ImageChops.multiply(rejilla.getchannel("A"), desvanecer))
    img.alpha_composite(rejilla)
    return img


def _sombra(img, e):
    capa = Image.new("RGBA", (360, 360), (0, 0, 0, 0))
    ImageDraw.Draw(capa).ellipse(e, fill=(0, 0, 0, 170))
    img.alpha_composite(capa.filter(ImageFilter.GaussianBlur(9)).resize((S, S), Image.BICUBIC))


def _resplandor(brillo):
    chico = brillo.convert("RGBa").resize((360, 360), Image.BOX)
    cerca = chico.filter(ImageFilter.GaussianBlur(2.2)).resize((S, S), Image.BILINEAR).convert("RGBA")
    lejos = chico.filter(ImageFilter.GaussianBlur(8)).resize((S, S), Image.BILINEAR).convert("RGBA")
    return cerca, lejos


def render(funcion, acento, sombra=None):
    img = fondo(acento)
    _sombra(img, sombra or (60, 298, 300, 334))
    c = Lienzo()
    funcion(c)
    cerca, lejos = _resplandor(c.brillo)
    img.alpha_composite(lejos)
    img.alpha_composite(lejos)
    img.alpha_composite(cerca)
    img.alpha_composite(c.img)
    return img.resize((OUT, OUT), Image.LANCZOS).convert("RGB")


def muestrario(imagenes, destino):
    cols, celda, alto = 6, 200, 226
    filas = math.ceil(len(imagenes) / cols)
    hoja = Image.new("RGB", (cols * celda + 20, filas * alto + 20), "#05070C")
    d = ImageDraw.Draw(hoja)
    try:
        fuente = ImageFont.truetype("segoeui.ttf", 15)
    except OSError:
        fuente = ImageFont.load_default()
    for i, (nombre, im) in enumerate(imagenes):
        x, y = 10 + (i % cols) * celda, 10 + (i // cols) * alto
        hoja.paste(im.resize((190, 190), Image.LANCZOS), (x + 5, y + 5))
        d.text((x + 100, y + 208), nombre, fill="#C5CBDC", font=fuente, anchor="mm")
    hoja.save(destino, optimize=True)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    out = Path(args[0]) if args else Path(__file__).resolve().parents[2] / "src" / "2. Infrastructure" / "MINV.Infrastructure" / "Seeding" / "Tecnologia" / "Imagenes"
    out.mkdir(parents=True, exist_ok=True)
    solo = {a.split("=", 1)[1] for a in sys.argv[1:] if a.startswith("--solo=")}
    hechas = []
    for nombre, (funcion, acento, sombra) in DIBUJOS.items():
        if solo and nombre not in solo:
            continue
        im = render(funcion, acento, sombra)
        im.save(out / f"{nombre}.png", optimize=True)
        hechas.append((nombre, im))
    if "--muestrario" in sys.argv:
        muestrario(hechas, out / "_muestrario.png")
    print(f"ok {len(hechas)} imágenes en {out}")


if __name__ == "__main__":
    main()
