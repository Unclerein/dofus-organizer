#!/usr/bin/env python3
"""
Fabrique src/DofusOrganizer.App/appicon.ico à partir de assets/icon.<ext>.

Pourquoi un script et non un .ico déposé à la main : une icône Windows n'est pas
une image, c'est un conteneur de plusieurs tailles, et le résultat dépend
entièrement de la façon dont on descend l'illustration. Régénérer doit donc être
reproductible plutôt que refait au jugé.

    pip install Pillow && python3 tools/build-icon.py

Quatre partis pris, expliqués là où ils s'appliquent dans le code :
  - une illustration livrée sur fond uni est détourée ;
  - la réduction se fait en alpha prémultiplié ;
  - les petites tailles sont réaccentuées ;
  - les entrées jusqu'à 128 sont en BMP, la 256 en PNG.
"""

import struct
from io import BytesIO
from pathlib import Path

from PIL import Image, ImageChops, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"
TARGET = ROOT / "src" / "DofusOrganizer.App" / "appicon.ico"

# 20 et 40 ne sont pas décoratives : ce sont les tailles que Windows demande à
# 125 % et 150 % d'échelle. Sans elles, le système prend la taille du dessus et
# la réduit lui-même, moins bien.
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

# Au-delà, la réduction efface les traits fins d'une illustration détaillée. Un
# léger renforcement les remet debout sans créer de halo.
SHARPEN_BELOW = 64

# L'illustration ne touche pas forcément les bords de son cadre : recadrer sur ce
# qui est réellement peint gagne un bon dixième de surface visible, ce qui compte
# à 16 px.
ALPHA_THRESHOLD = 32
MARGIN = 0.02

# Écart admis autour de la couleur des coins pour reconnaître le fond uni.
BACKGROUND_TOLERANCE = 24


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


def detach_background(image: Image.Image) -> Image.Image:
    """
    Rend transparent le fond uni d'une illustration livrée sans couche alpha.

    Sans cela l'icône serait un carré blanc : les coins arrondis du dessin ne se
    verraient nulle part, et le fond trancherait sur toute barre des tâches
    sombre. Le remplissage part des bords plutôt que de tester chaque pixel, pour
    ne pas percer une zone claire enfermée à l'intérieur du dessin — une feuille
    de papier, un reflet.
    """
    width, height = image.size
    pixels = image.convert("RGB").tobytes()

    corners = [(0, 0), (width - 1, 0), (0, height - 1), (width - 1, height - 1)]
    def at(x: int, y: int) -> tuple[int, int, int]:
        o = (y * width + x) * 3
        return pixels[o], pixels[o + 1], pixels[o + 2]

    reference = at(*corners[0])
    for corner in corners[1:]:
        if max(abs(a - b) for a, b in zip(at(*corner), reference)) > BACKGROUND_TOLERANCE:
            return image  # les coins ne s'accordent pas : ce n'est pas un fond uni

    alpha = bytearray(b"\xff" * (width * height))
    pending: list[int] = []

    def visit(index: int) -> None:
        if not alpha[index]:
            return
        o = index * 3
        if (abs(pixels[o] - reference[0]) <= BACKGROUND_TOLERANCE
                and abs(pixels[o + 1] - reference[1]) <= BACKGROUND_TOLERANCE
                and abs(pixels[o + 2] - reference[2]) <= BACKGROUND_TOLERANCE):
            alpha[index] = 0
            pending.append(index)

    for x in range(width):
        visit(x)
        visit((height - 1) * width + x)
    for y in range(height):
        visit(y * width)
        visit(y * width + width - 1)

    while pending:
        index = pending.pop()
        x, y = index % width, index // width
        if x:              visit(index - 1)
        if x < width - 1:  visit(index + 1)
        if y:              visit(index - width)
        if y < height - 1: visit(index + width)

    # Le pourtour du dessin est adouci contre le fond : ces pixels-là sont un
    # mélange des deux et garderaient un liseré clair. Un pixel de rognage les
    # écarte, ce qui ne coûte rien sur une source de plus de mille pixels.
    mask = Image.frombytes("L", (width, height), bytes(alpha)).filter(ImageFilter.MinFilter(3))

    detached = image.copy()
    detached.putalpha(mask)
    return detached


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
    """
    Réduit en alpha prémultiplié.

    Réduire une image transparente telle quelle mélange la couleur des pixels
    invisibles à celle de leurs voisins : le fond écarté juste avant reviendrait
    par la bande, en halo clair sur le pourtour, d'autant plus visible que la
    taille est petite — c'est en 16 px qu'un pixel de sortie moyenne le plus de
    pixels d'entrée. Multiplier par l'alpha avant, diviser après, l'évite.
    """
    red, green, blue, alpha = image.split()
    premultiplied = Image.merge("RGBA", (
        ImageChops.multiply(red, alpha),
        ImageChops.multiply(green, alpha),
        ImageChops.multiply(blue, alpha),
        alpha,
    )).resize((size, size), Image.LANCZOS)

    raw = premultiplied.tobytes()
    out = bytearray(len(raw))
    for i in range(0, len(raw), 4):
        a = raw[i + 3]
        if not a:
            continue  # déjà à zéro
        out[i] = min(255, raw[i] * 255 // a)
        out[i + 1] = min(255, raw[i + 1] * 255 // a)
        out[i + 2] = min(255, raw[i + 2] * 255 // a)
        out[i + 3] = a

    small = Image.frombytes("RGBA", (size, size), bytes(out))
    return sharpened(small) if size < SHARPEN_BELOW else small


def sharpened(image: Image.Image) -> Image.Image:
    """
    Renforce les traits sans creuser le pourtour.

    Un pixel devenu transparent n'a plus de couleur — elle vaut zéro, c'est-à-dire
    noir. Accentuer directement tracerait donc un liseré sombre le long du bord,
    au contact de ce noir. Les pixels transparents reçoivent d'abord la couleur de
    leurs voisins visibles, le temps du calcul ; la transparence, elle, ne bouge
    pas et reprend sa place ensuite.
    """
    alpha = image.getchannel("A")
    filled = image.convert("RGB")

    for _ in range(2):
        spread = filled.filter(ImageFilter.BoxBlur(1))
        filled = Image.composite(filled, spread, alpha.point(lambda v: 255 if v else 0))

    result = filled.filter(ImageFilter.UnsharpMask(radius=1.0, percent=70, threshold=2)).convert("RGBA")
    result.putalpha(alpha)
    return result


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


def main() -> int:
    source = find_source()
    image = Image.open(source).convert("RGBA")

    opaque = image.getchannel("A").getextrema()[0] == 255
    if opaque:
        image = detach_background(image)
        if image.getchannel("A").getextrema()[0] == 255:
            print("Attention : aucun fond uni reconnu, l'icône restera opaque.")

    art = square(image)
    frames = {size: scaled(art, size) for size in SIZES if size <= max(art.size)}

    TARGET.write_bytes(encode(frames))
    print(f"{source.relative_to(ROOT)} -> {TARGET.relative_to(ROOT)} — "
          f"{'détourée, ' if opaque else ''}{', '.join(str(s) for s in sorted(frames))} px, "
          f"{TARGET.stat().st_size // 1024} Kio")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
