r"""Genera el ícono del cliente de escritorio M-INV (cubo isométrico blanco sobre un cuadrado redondeado azul).

Uso:  .venv\Scripts\python tools\generar_icono_escritorio.py
Salida: src/3. Presentation/MINV.DesktopClient/Assets/minv.ico (16 a 256 px) y minv-256.png.
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw

S = 1024
def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))

def icon():
    top, bottom = (14, 60, 140), (14, 165, 233)
    grad = Image.new("RGB", (S, S))
    px = grad.load()
    for y in range(S):
        for x in range(S):
            t = min(1.0, max(0.0, (x * 0.35 + y * 0.65) / S))
            px[x, y] = lerp(top, bottom, t)
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle((32, 32, S - 32, S - 32), radius=230, fill=255)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    img.paste(grad, (0, 0), mask)
    d = ImageDraw.Draw(img)
    cx, cy, r = S / 2, S / 2 + 20, 300
    h = r * 0.5
    top_face = [(cx, cy - r), (cx + r * 0.866, cy - h), (cx, cy), (cx - r * 0.866, cy - h)]
    left_face = [(cx - r * 0.866, cy - h), (cx, cy), (cx, cy + r), (cx - r * 0.866, cy + h)]
    right_face = [(cx + r * 0.866, cy - h), (cx + r * 0.866, cy + h), (cx, cy + r), (cx, cy)]
    d.polygon(top_face, fill=(255, 255, 255, 255))
    d.polygon(left_face, fill=(226, 238, 255, 255))
    d.polygon(right_face, fill=(186, 214, 250, 255))
    # cinta del paquete
    w = 44
    d.polygon([(cx - r * 0.433 - w, cy - r * 0.75 + w * 0.5), (cx - r * 0.433 + w, cy - r * 0.75 - w * 0.5),
               (cx + r * 0.433 + w, cy - r * 0.25 - w * 0.5), (cx + r * 0.433 - w, cy - r * 0.25 + w * 0.5)], fill=(14, 116, 200, 255))
    return img

img = icon()
out = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[1] / "src" / "3. Presentation" / "MINV.DesktopClient" / "Assets"
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
img.resize((256, 256), Image.LANCZOS).save(out / "minv.ico", sizes=[(s, s) for s in sizes])
img.resize((256, 256), Image.LANCZOS).save(out / "minv-256.png")
print("ok")
