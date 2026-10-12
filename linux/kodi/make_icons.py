# -*- coding: utf-8 -*-
"""Erzeugt die Controller-Icons für das Add-on-Menü und das Kodi-Widget (service.nconnect/resources/icons/).
Einmalig bzw. bei Design-Änderungen ausführen: python linux/kodi/make_icons.py – die PNGs werden eingecheckt."""
import math
import pathlib
import struct
import zlib

S = 128  # Kantenlänge in Pixeln
OUT = pathlib.Path(__file__).parent / 'service.nconnect' / 'resources' / 'icons'

# Controller-Farben (an die echten Gehäuse angelehnt)
BLUE = (86, 180, 233)     # Joy-Con 2 links
CORAL = (255, 102, 96)    # Joy-Con 2 rechts
SLATE = (86, 88, 100)     # Pro Controller 2
PURPLE = (110, 90, 210)   # GameCube (Switch 2)
GRAY = (125, 128, 140)    # generisch
WHITE = (235, 235, 240)   # Xbox
PSBLUE = (70, 100, 220)   # PlayStation


def new_image():
    return bytearray(S * S * 4)  # RGBA, transparent


def blend(img, x, y, c, alpha=255):
    if 0 <= x < S and 0 <= y < S:
        i = (y * S + x) * 4
        a = alpha / 255.0
        img[i] = int(c[0] * a + img[i] * (1 - a))
        img[i + 1] = int(c[1] * a + img[i + 1] * (1 - a))
        img[i + 2] = int(c[2] * a + img[i + 2] * (1 - a))
        img[i + 3] = min(255, img[i + 3] + alpha)


def disc(img, cx, cy, r, c, alpha=255):
    """Gefüllter Kreis mit 1,5 px weicher Kante."""
    for y in range(int(cy - r - 2), int(cy + r + 2)):
        for x in range(int(cx - r - 2), int(cx + r + 2)):
            d = math.hypot(x - cx, y - cy)
            if d <= r - 1.5:
                blend(img, x, y, c, alpha)
            elif d <= r:
                blend(img, x, y, c, int(alpha * (r - d) / 1.5))


def rrect(img, x0, y0, x1, y1, r, c, alpha=255):
    """Abgerundetes Rechteck mit 1,5 px weicher Kante."""
    for y in range(y0, y1):
        for x in range(x0, x1):
            dx = max(x0 + r - x, 0, x - (x1 - r - 1))
            dy = max(y0 + r - y, 0, y - (y1 - r - 1))
            d = math.hypot(dx, dy)
            if d <= r - 1.5:
                blend(img, x, y, c, alpha)
            elif d <= r:
                blend(img, x, y, c, int(alpha * (r - d) / 1.5))


def stick(img, cx, cy, r):
    """Analog-Stick: heller Ring mit dunklem Kern."""
    disc(img, cx, cy, r, (245, 245, 250))
    disc(img, cx, cy, r * 0.6, (40, 40, 48))


def buttons(img, cx, cy, r, gap):
    """Vier Aktionstasten als helle Punkte."""
    disc(img, cx, cy - gap, r, (245, 245, 250))
    disc(img, cx, cy + gap, r, (245, 245, 250))
    disc(img, cx - gap, cy, r, (245, 245, 250))
    disc(img, cx + gap, cy, r, (245, 245, 250))


def draw_joycon(img, cx, color, mirror=False):
    """Aufrechter Joy-Con: Gehäuse, Stick oben, Tasten darunter (gespiegelt für L/R)."""
    rrect(img, cx - 20, 20, cx + 20, 108, 20, color)
    stick(img, cx + (8 if mirror else -8), 46, 10)
    buttons(img, cx - (8 if mirror else -8), 76, 3.2, 8)


def draw_pad(img, color):
    """Gamepad-Silhouette: Korpus + zwei Griffe, Sticks und Tasten."""
    rrect(img, 16, 34, 112, 80, 22, color)
    disc(img, 38, 80, 21, color)
    disc(img, 90, 80, 21, color)
    stick(img, 44, 56, 9)
    buttons(img, 84, 56, 3.2, 9)
    disc(img, 64, 48, 4, (245, 245, 250))  # Home-Taste


def draw_pair(img):
    """Joy-Con-Paar: links blau, rechts koralle."""
    draw_joycon(img, 36, BLUE, mirror=False)
    draw_joycon(img, 92, CORAL, mirror=True)


def save(name, img):
    raw = b''.join(b'\x00' + bytes(img[y * S * 4:(y + 1) * S * 4]) for y in range(S))

    def chunk(tag, data):
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data))

    png = (b'\x89PNG\r\n\x1a\n'
           + chunk(b'IHDR', struct.pack('>IIBBBBB', S, S, 8, 6, 0, 0, 0))
           + chunk(b'IDAT', zlib.compress(raw, 9))
           + chunk(b'IEND', b''))
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / name).write_bytes(png)
    print(name)


def main():
    img = new_image(); draw_joycon(img, 64, BLUE); save('joycon_l.png', img)
    img = new_image(); draw_joycon(img, 64, CORAL, mirror=True); save('joycon_r.png', img)
    img = new_image(); draw_pair(img); save('joycon_pair.png', img)
    for name, color in (('pro.png', SLATE), ('gamecube.png', PURPLE), ('xbox.png', WHITE),
                        ('playstation.png', PSBLUE), ('gamepad.png', GRAY)):
        img = new_image(); draw_pad(img, color); save(name, img)


if __name__ == '__main__':
    main()
