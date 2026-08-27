#!/usr/bin/env python3
"""
Fabrique src/DofusOrganizer.App/appicon.ico à partir de assets/icon.<ext>.

Pourquoi un script et non un .ico déposé à la main : une icône Windows n'est pas
une image, c'est un conteneur de plusieurs tailles, et le résultat dépend
entièrement de la façon dont on descend l'illustration. Régénérer doit donc être
reproductible plutôt que refait au jugé.

    pip install Pillow && python3 tools/build-icon.py

Deux partis pris, expliqués là où ils s'appliquent dans le code :
  - les petites tailles sont réaccentuées après réduction ;
  - les entrées jusqu'à 128 sont en BMP, la 256 en PNG.
"""

import struct
from io import BytesIO
from pathlib import Path

from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"
TARGET = ROOT / "src" / "DofusOrganizer.App" / "appicon.ico"

# 20 et 40 ne sont pas décoratives : ce sont les tailles que Windows demande à
# 125 % et 150 % d'échelle. Sans elles, le système prend la taille du dessus et
# la réduit lui-même, moins bien.
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

# Au-delà, la réduction efface les traits fins de l'illustration — le contour de
# l'œuf, les cases de la liste. Un léger renforcement les remet debout sans
# créer de halo.
SHARPEN_BELOW = 64

# L'illustration ne touche pas les bords de son cadre : recadrer sur ce qui est
# réellement peint gagne un bon dixième de surface visible, ce qui compte à 16 px.
ALPHA_THRESHOLD = 32
MARGIN = 0.02


def square(image: Image.Image) -> Image.Image:
    """Recadre sur le dessin, puis le centre dans un carré transparent."""
    alpha = image.getchannel("A")
    box = alpha.point(lambda v: 255 if v > ALPHA_THRESHOLD else 0).getbbox()
    if box is None:
        raise SystemExit("L'illustration source est entièrement transparente.")

    drawing = image.crop(box)
    side = round(max(drawing.size) * (1 + MARGIN))

    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(drawing, ((side - drawing.width) // 2, (side - drawing.height) // 2))
    return canvas


def scaled(image: Image.Image, size: int) -> Image.Image:
    small = image.resize((size, size), Image.LANCZOS)
    if size < SHARPEN_BELOW:
        small = small.filter(ImageFilter.UnsharpMask(radius=1.0, percent=70, threshold=2))
    return small


def encode(frames: dict[int, Image.Image]) -> bytes:
    """
    Assemble le conteneur .ico.

    Les entrées jusqu'à 128 sont en BMP et la 256 en PNG : c'est la convention que
    Windows suit lui-même. Un PNG dans les petites tailles est accepté par
    l'explorateur moderne mais pas partout, et un BMP en 256 pèse 256 Kio à lui
    seul. Pillow ne sait écrire qu'un seul format par fichier, d'où l'assemblage
    ici de deux passes dont on ne garde que la charge utile.
    """
    payloads: dict[int, bytes] = {}

    for size, frame in frames.items():
        buffer = BytesIO()
        if size == 256:
            frame.save(buffer, "png")
            payloads[size] = buffer.getvalue()
            continue

        # Une entrée BMP d'icône déclare une hauteur double : le format prévoit un
        # masque de transparence sous l'image. En 32 bits il est inutile — l'alpha
        # est dans les pixels — mais la hauteur doit malgré tout être doublée.
        frame.save(buffer, "dib")
        raw = buffer.getvalue()
        payloads[size] = raw[:8] + struct.pack("<I", size * 2) + raw[12:]

    header = struct.pack("<HHH", 0, 1, len(payloads))
    offset = len(header) + 16 * len(payloads)

    directory, body = b"", b""
    for size in sorted(payloads):
        data = payloads[size]
        directory += struct.pack(
            "<BBBBHHII",
            size if size < 256 else 0,  # largeur, 0 signifie 256
            size if size < 256 else 0,  # hauteur
            0,                          # palette : aucune
            0,                          # réservé
            1,                          # plans
            32,                         # bits par pixel
            len(data),
            offset,
        )
        body += data
        offset += len(data)

    return header + directory + body


def find_source() -> Path:
    """
    L'illustration est conservée telle qu'elle a été fournie, sans réencodage :
    la réencoder en PNG « pour faire propre » ne ferait que gonfler un fichier
    déjà compressé avec perte, sans rien lui rendre. D'où la recherche par
    extension plutôt qu'un nom figé.
    """
    candidates = sorted(p for p in ASSETS.glob("icon.*") if p.suffix.lower() != ".ico")
    if not candidates:
        raise SystemExit(f"Aucune illustration assets/icon.* dans {ASSETS}.")
    return candidates[0]


def main() -> int:
    source = find_source()
    art = square(Image.open(source).convert("RGBA"))
    frames = {size: scaled(art, size) for size in SIZES if size <= max(art.size)}

    TARGET.write_bytes(encode(frames))
    print(f"{source.relative_to(ROOT)} -> {TARGET.relative_to(ROOT)} — "
          f"{', '.join(str(s) for s in sorted(frames))} px, {TARGET.stat().st_size // 1024} Kio")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
