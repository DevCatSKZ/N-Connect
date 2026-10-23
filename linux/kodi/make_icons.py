# -*- coding: utf-8 -*-
"""Erzeugt die Controller-Icons für das Add-on-Menü und das Kodi-Widget (service.skzconnect/resources/icons/).
Einmalig bzw. bei Design-Änderungen ausführen: python linux/kodi/make_icons.py – die PNGs werden eingecheckt.

Die Zeichnung läuft auf 512 px mit weichen Kanten und wird am Ende auf 128 px herunterskaliert
(4×-Supersampling → saubere, runde Formen ohne Pixelkanten)."""
import math
import pathlib
import struct
import zlib

S = 512          # Render-Auflösung
OUT_S = 128      # Ausgabe-Größe
OUT = pathlib.Path(__file__).parent / 'service.skzconnect' / 'resources' / 'icons'

# Controller-Farben (an die echten Gehäuse angelehnt)
BLUE = (86, 180, 233)      # Joy-Con links (Neon-Blau)
CORAL = (255, 102, 96)     # Joy-Con rechts (Neon-Rot)
SLATE = (70, 72, 82)       # Pro Controller
PURPLE = (110, 90, 210)    # GameCube
GRAY = (130, 133, 145)     # generisch
WHITE = (238, 238, 243)    # Xbox
PSBLUE = (72, 100, 224)    # PlayStation
DARK = (30, 32, 38)
LIGHT = (245, 245, 250)


def new_image():
    return bytearray(S * S * 4)


def blend(img, x, y, c, alpha=255):
    if 0 <= x < S and 0 <= y < S:
        i = (y * S + x) * 4
        a = alpha / 255.0
        img[i] = int(c[0] * a + img[i] * (1 - a))
        img[i + 1] = int(c[1] * a + img[i + 1] * (1 - a))
        img[i + 2] = int(c[2] * a + img[i + 2] * (1 - a))
        img[i + 3] = min(255, img[i + 3] + alpha)


def shade(c, f):
    """Farbe aufhellen (f > 1) oder abdunkeln (f < 1)."""
    return tuple(max(0, min(255, int(v * f))) for v in c)


def disc(img, cx, cy, r, c, alpha=255):
    for y in range(int(cy - r - 2), int(cy + r + 2)):
        for x in range(int(cx - r - 2), int(cx + r + 2)):
            d = math.hypot(x - cx, y - cy)
            if d <= r - 1.5:
                blend(img, x, y, c, alpha)
            elif d <= r:
                blend(img, x, y, c, int(alpha * (r - d) / 1.5))


def rrect(img, x0, y0, x1, y1, r, c, alpha=255):
    for y in range(y0, y1):
        for x in range(x0, x1):
            dx = max(x0 + r - x, 0, x - (x1 - r - 1))
            dy = max(y0 + r - y, 0, y - (y1 - r - 1))
            d = math.hypot(dx, dy)
            if d <= r - 1.5:
                blend(img, x, y, c, alpha)
            elif d <= r:
                blend(img, x, y, c, int(alpha * (r - d) / 1.5))


def ellipse(img, cx, cy, rx, ry, c, alpha=255, rot=0.0):
    """Gefüllte, optional rotierte Ellipse (rot in Grad)."""
    co, si = math.cos(math.radians(rot)), math.sin(math.radians(rot))
    for y in range(int(cy - max(rx, ry) - 2), int(cy + max(rx, ry) + 2)):
        for x in range(int(cx - max(rx, ry) - 2), int(cx + max(rx, ry) + 2)):
            lx = (x - cx) * co + (y - cy) * si
            ly = -(x - cx) * si + (y - cy) * co
            d = math.hypot(lx / rx, ly / ry) * max(rx, ry) / max(rx, ry)
            v = (lx / rx) ** 2 + (ly / ry) ** 2
            edge = (1.0 - math.sqrt(v)) * min(rx, ry)
            if edge >= 1.5:
                blend(img, x, y, c, alpha)
            elif edge >= 0:
                blend(img, x, y, c, int(alpha * edge / 1.5))


def stick(img, cx, cy, r):
    """Analog-Stick: dunkler Ring, heller Kappenrand, dunkle Mitte mit Mini-Highlight."""
    disc(img, cx, cy, r, shade(DARK, 0.9))
    disc(img, cx, cy, r * 0.78, (60, 62, 72))
    disc(img, cx, cy, r * 0.62, DARK)
    disc(img, cx - r * 0.18, cy - r * 0.22, r * 0.16, (80, 84, 96))


def face_buttons(img, cx, cy, r, gap, color=LIGHT):
    """Vier Aktionstasten im Kreuz (A/B/X/Y bzw. Steuerkreuz)."""
    for dx, dy in ((0, -gap), (0, gap), (-gap, 0), (gap, 0)):
        disc(img, cx + dx, cy + dy, r, color)


def joycon(img, cx, color, left):
    """Ein aufrechter Joy-Con: farbige Kapsel, innere Führungsschiene, Stick oben (außen),
    Tasten-Kreuz, kleine +/- und seitliche SL/SR-Andeutung."""
    w, top, bottom = 96, 60, 460
    rail_x = cx + (w // 2 - 12) * (-1 if left else 1)   # Schiene auf der Innenseite
    outer = 1 if left else -1

    # Gehäuse
    rrect(img, cx - w // 2, top, cx + w // 2, bottom, 46, color)
    # leichte Vertiefung unten (Volumen)
    rrect(img, cx - w // 2 + 6, bottom - 90, cx + w // 2 - 6, bottom - 6, 34, shade(color, 0.82), 90)
    # Schiene
    rrect(img, rail_x - 4, top + 20, rail_x + 4, bottom - 20, 4, shade(color, 0.72))
    # SL/SR-Andeutung an der Schiene
    rrect(img, rail_x - 7, top + 60, rail_x + 7, top + 90, 6, shade(color, 0.6))
    rrect(img, rail_x - 7, top + 98, rail_x + 7, top + 128, 6, shade(color, 0.6))

    bx = cx + 10 * outer   # Bedienfeld leicht zur Außenseite versetzt
    if left:
        stick(img, bx + 6, top + 128, 34)
        face_buttons(img, bx - 2, top + 258, 11, 26)          # Steuerkreuz-Tasten
        disc(img, cx + w // 2 - 18, top + 44, 9, shade(color, 0.55))   # Minus
        ellipse(img, bx - 2, bottom - 78, 26, 14, shade(color, 0.7))   # Screenshot-Taste
    else:
        face_buttons(img, bx - 2, top + 128, 11, 26)          # A B X Y
        stick(img, bx + 6, top + 258, 34)
        # Plus
        rrect(img, cx - w // 2 + 9, top + 40, cx - w // 2 + 27, top + 48, 3, shade(color, 0.55))
        rrect(img, cx - w // 2 + 15, top + 34, cx - w // 2 + 21, top + 54, 3, shade(color, 0.55))
        disc(img, bx - 2, bottom - 78, 16, shade(color, 0.7))          # Home


def pad(img, color, xbox=False):
    """Gamepad-Silhouette: Mittelstück + zwei angewinkelte Griffe, Sticks, Tasten, D-Pad."""
    # Griffe zuerst (liegen unter dem Korpus)
    ellipse(img, 128, 330, 74, 118, shade(color, 0.92), rot=-32)
    ellipse(img, 384, 330, 74, 118, shade(color, 0.92), rot=32)
    # Mittelstück
    rrect(img, 60, 118, 452, 322, 78, color)
    # Schulter-Andeutung
    rrect(img, 96, 92, 196, 132, 18, shade(color, 0.8))
    rrect(img, 316, 92, 416, 132, 18, shade(color, 0.8))
    if xbox:
        stick(img, 150, 190, 40)                    # linker Stick oben
        face_buttons(img, 372, 190, 12, 30)         # ABXY
        stick(img, 226, 268, 36)                    # rechter Stick unten-links
        # D-Pad unten-links als Kreuz
        rrect(img, 340, 244, 384, 288, 10, shade(color, 0.55))
        rrect(img, 352, 232, 372, 300, 10, shade(color, 0.55))
    else:
        stick(img, 168, 230, 40)
        stick(img, 344, 230, 40)
        face_buttons(img, 416, 170, 12, 30)
        rrect(img, 96, 158, 140, 202, 10, shade(color, 0.55))   # D-Pad
        rrect(img, 108, 146, 128, 214, 10, shade(color, 0.55))
    disc(img, 256, 158, 12, shade(color, 0.55))     # Home-LED/Taste


def pair(img):
    """Joy-Con-Paar nebeneinander – leicht ausgerichtet wie in einer Halterung."""
    joycon(img, 152, BLUE, left=True)
    joycon(img, 360, CORAL, left=False)
    rrect(img, 236, 120, 276, 400, 12, (52, 54, 62))   # Mittelsteg (Grip)


def save(name, img):
    """4×-Supersampling: je 4×4 Pixel mitteln (vorvermultipliziert)."""
    small = bytearray(OUT_S * OUT_S * 4)
    f = S // OUT_S
    for y in range(OUT_S):
        for x in range(OUT_S):
            acc = [0, 0, 0, 0]
            for sy in range(y * f, y * f + f):
                for sx in range(x * f, x * f + f):
                    i = (sy * S + sx) * 4
                    for k in range(4):
                        acc[k] += img[i + k]
            j = (y * OUT_S + x) * 4
            for k in range(4):
                small[j + k] = acc[k] // (f * f)

    raw = b''.join(b'\x00' + bytes(small[y * OUT_S * 4:(y + 1) * OUT_S * 4]) for y in range(OUT_S))

    def chunk(tag, data):
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data))

    png = (b'\x89PNG\r\n\x1a\n'
           + chunk(b'IHDR', struct.pack('>IIBBBBB', OUT_S, OUT_S, 8, 6, 0, 0, 0))
           + chunk(b'IDAT', zlib.compress(raw, 9))
           + chunk(b'IEND', b''))
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / name).write_bytes(png)
    print(name)
    save_off(name, small)


def save_off(name, small):
    """Ausgegraute Variante („nicht verbunden“): Graustufen, deutlich durchsichtig – <name>_off.png."""
    off = bytearray(small)
    for i in range(0, len(off), 4):
        gray = int(off[i] * 0.30 + off[i + 1] * 0.59 + off[i + 2] * 0.11)
        off[i] = off[i + 1] = off[i + 2] = gray
        off[i + 3] = off[i + 3] * 40 // 100
    raw = b''.join(b'\x00' + bytes(off[y * OUT_S * 4:(y + 1) * OUT_S * 4]) for y in range(OUT_S))

    def chunk(tag, data):
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data))

    png = (b'\x89PNG\r\n\x1a\n'
           + chunk(b'IHDR', struct.pack('>IIBBBBB', OUT_S, OUT_S, 8, 6, 0, 0, 0))
           + chunk(b'IDAT', zlib.compress(raw, 9))
           + chunk(b'IEND', b''))
    (OUT / name.replace('.png', '_off.png')).write_bytes(png)


def main():
    img = new_image(); joycon(img, 256, BLUE, left=True); save('joycon_l.png', img)
    img = new_image(); joycon(img, 256, CORAL, left=False); save('joycon_r.png', img)
    img = new_image(); pair(img); save('joycon_pair.png', img)
    for name, color, xbox in (('pro.png', SLATE, False), ('gamecube.png', PURPLE, False),
                              ('xbox.png', WHITE, True), ('playstation.png', PSBLUE, False),
                              ('gamepad.png', GRAY, True)):
        img = new_image(); pad(img, color, xbox); save(name, img)


if __name__ == '__main__':
    main()
