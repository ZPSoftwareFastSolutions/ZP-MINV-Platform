#!/usr/bin/env python3
"""
M-INV V2 · Herramienta de los Office Scripts (src/office-scripts)

Office Scripts no permite importar módulos: cada script debe ser un solo archivo. El código compartido vive en
src/office-scripts/lib/comun.ts y esta herramienta lo copia entre los marcadores de cada script.

    python tools/office_scripts.py sync                     copia lib/comun.ts dentro de cada script
    python tools/office_scripts.py check                    falla si algún script no está sincronizado
    python tools/office_scripts.py deploy --edicion core    versión instalable con la contraseña del Core
    python tools/office_scripts.py deploy --edicion release versión instalable con la del Release

La versión instalable (build/office-scripts/<edición>/, fuera de Git) reemplaza el marcador __MINV_PASSWORD__ por la
contraseña de las hojas (MINV_PASSWORD / MINV_RELEASE_PASSWORD, como el generador): es la que se pega en Excel.
"""
from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "office-scripts"
LIB = SRC / "lib" / "comun.ts"
OUT = ROOT / "build" / "office-scripts"
BEGIN = "// >>> M-INV · BLOQUE COMÚN"
END = "// <<< M-INV · FIN DEL BLOQUE COMÚN"
MARCADOR = "__MINV_PASSWORD__"
AVISO = ("// VERSIÓN INSTALABLE generada por tools/office_scripts.py: contiene la contraseña de protección del libro.\n"
         "// Péguela en Excel (Automatizar › Nuevo script). No la suba al repositorio ni la comparta fuera del equipo.\n")


def scripts() -> list[Path]:
    return sorted(p for p in SRC.glob("*.ts"))


def armar(path: Path) -> str:
    t = path.read_text(encoding="utf-8").replace("\r\n", "\n")
    if BEGIN not in t or END not in t:
        raise SystemExit(f"{path.name}: faltan los marcadores del bloque común")
    head, rest = t.split(BEGIN, 1)
    _, tail = rest.split(END, 1)
    comun = LIB.read_text(encoding="utf-8").replace("\r\n", "\n").strip("\n")
    return (f"{head}{BEGIN} (generado desde lib/comun.ts con tools/office_scripts.py: no editar aquí)\n"
            f"{comun}\n{END}{tail.split(chr(10), 1)[1] if chr(10) in tail else ''}").rstrip("\n") + "\n"


def sync() -> int:
    for p in scripts():
        nuevo = armar(p)
        if p.read_text(encoding="utf-8").replace("\r\n", "\n") != nuevo:
            p.write_text(nuevo, encoding="utf-8", newline="\n")
            print(f"[sync] {p.relative_to(ROOT)}")
    print(f"[sync] {len(scripts())} scripts sincronizados con {LIB.relative_to(ROOT)}")
    return 0


def check() -> int:
    malos = [p.name for p in scripts() if p.read_text(encoding="utf-8").replace("\r\n", "\n") != armar(p)]
    if malos:
        print("[check] desincronizados (ejecute: python tools/office_scripts.py sync): " + ", ".join(malos))
        return 1
    print(f"[check] {len(scripts())} scripts sincronizados con el bloque común")
    return 0


def deploy(edicion: str) -> int:
    dev = os.environ.get("MINV_PASSWORD", "minv-dev")
    clave = dev if edicion == "core" else os.environ.get("MINV_RELEASE_PASSWORD", dev)
    if edicion == "release" and "MINV_RELEASE_PASSWORD" not in os.environ:
        print("[aviso]   MINV_RELEASE_PASSWORD no definida: el release usa la contraseña de desarrollo.")
    if check():
        return 1
    destino = OUT / edicion
    destino.mkdir(parents=True, exist_ok=True)
    literal = clave.replace("\\", "\\\\").replace('"', '\\"')
    for p in scripts():
        t = p.read_text(encoding="utf-8")
        assert t.count(f'"{MARCADOR}"') == 1, p.name
        (destino / p.name).write_text(AVISO + t.replace(f'"{MARCADOR}"', f'"{literal}"'), encoding="utf-8",
                                      newline="\n")
    print(f"[deploy] {len(scripts())} scripts instalables en {destino.relative_to(ROOT)}")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description="Sincroniza y empaqueta los Office Scripts de M-INV V2.")
    ap.add_argument("accion", choices=("sync", "check", "deploy"))
    ap.add_argument("--edicion", choices=("core", "release"), default="core")
    a = ap.parse_args()
    return {"sync": sync, "check": check}.get(a.accion, lambda: deploy(a.edicion))()


if __name__ == "__main__":
    sys.exit(main())
