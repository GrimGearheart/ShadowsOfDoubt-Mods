"""Draws 256x256 Thunderstore icons in the shared noir style used by these mods.

Usage: python tools/make_icon.py <squint|license> <output.png>
"""
import math
import sys
from PIL import Image, ImageDraw, ImageFont

S = 1024
BG, RING, DIM, ACCENT, TEXT = "#15121c", "#3a3348", "#6b6280", "#e8b04b", "#f2ece0"
FONT = "C:/Windows/Fonts/segoeuib.ttf"


def base(title):
    img = Image.new("RGB", (S, S), BG)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([40, 40, S - 40, S - 40], radius=120, outline=RING, width=18)
    d.text((S // 2, int(S * 0.16)), title, font=ImageFont.truetype(FONT, 120), fill=TEXT, anchor="mm")
    return img, d


def squint():
    img, d = base("SQUINT")
    cx, cy = S // 2, int(S * 0.58)
    # Narrowed eye: two arcs meeting at the corners
    w, h = 330, 150
    d.arc([cx - w, cy - h * 2, cx + w, cy + h * 2 - 120], 20, 160, fill=TEXT, width=24)
    d.arc([cx - w, cy - h * 2 + 120, cx + w, cy + h * 2], 200, 340, fill=TEXT, width=24)
    d.ellipse([cx - 85, cy - 85, cx + 85, cy + 85], fill=ACCENT)
    d.ellipse([cx - 32, cy - 32, cx + 32, cy + 32], fill=BG)
    # Focus brackets
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = cx + sx * 400, cy + sy * 250
            d.line([x, y, x - sx * 90, y], fill=DIM, width=20)
            d.line([x, y, x, y - sy * 90], fill=DIM, width=20)
    return img


def license_():
    img, d = base("LICENSED")
    # ID card
    x0, y0, x1, y1 = 170, 330, S - 170, 820
    d.rounded_rectangle([x0, y0, x1, y1], radius=50, outline=TEXT, width=20)
    d.rectangle([x0 + 10, y0 + 10, x1 - 10, y0 + 110], fill=RING)
    # Photo box
    d.rounded_rectangle([x0 + 60, y0 + 160, x0 + 290, y0 + 430], radius=20, outline=DIM, width=14)
    d.ellipse([x0 + 125, y0 + 200, x0 + 225, y0 + 300], fill=DIM)
    d.pieslice([x0 + 95, y0 + 310, x0 + 255, y0 + 470], 180, 360, fill=DIM)
    # Text lines
    for i, w in enumerate((300, 240, 280)):
        y = y0 + 190 + i * 80
        d.rounded_rectangle([x0 + 340, y, x0 + 340 + w, y + 34], radius=17, fill=DIM)
    # Star badge
    cx, cy, r = x1 - 120, y1 - 120, 95
    pts = []
    for i in range(10):
        a = math.radians(-90 + i * 36)
        rr = r if i % 2 == 0 else r * 0.45
        pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
    d.polygon(pts, fill=ACCENT)
    return img


def mantle():
    img, d = base("MANTLE")
    # A tall wall; the figure hangs off its top edge, hauling itself up.
    wx, wy = 470, 430
    d.rectangle([wx, wy, S - 130, S - 130], fill=RING)
    d.line([wx, wy, S - 130, wy], fill=ACCENT, width=22)
    # Head just above the edge, both forearms planted on top
    hx, hy = 445, 365
    d.ellipse([hx - 55, hy - 55, hx + 55, hy + 55], fill=TEXT)
    d.line([hx + 20, hy + 55, wx + 150, wy - 12], fill=TEXT, width=32)
    d.line([hx - 5, hy + 60, wx + 70, wy - 12], fill=TEXT, width=32)
    # Body hanging down the wall face
    hip = (wx - 45, 640)
    d.line([hx - 10, hy + 55, *hip], fill=TEXT, width=44)
    # One knee driving up against the wall, one leg dangling
    d.line([*hip, wx - 5, 700], fill=TEXT, width=34)
    d.line([wx - 5, 700, wx - 10, 800], fill=TEXT, width=34)
    d.line([*hip, wx - 90, 840], fill=TEXT, width=34)
    # Upward effort lines
    for x in (300, 350):
        d.line([x, 760, x, 640], fill=DIM, width=14)
    return img


def prone():
    img, d = base("PRONE")
    floor = 800
    d.line([90, floor, S - 90, floor], fill=DIM, width=14)
    # Desk on the right
    d.rectangle([560, 470, S - 130, 510], fill=RING)
    d.rectangle([570, 510, 600, floor], fill=RING)
    d.rectangle([S - 170, 510, S - 140, floor], fill=RING)
    # Figure lying on the floor, head raised, looking under the desk
    hx, hy = 470, floor - 95
    d.ellipse([hx - 52, hy - 52, hx + 52, hy + 52], fill=TEXT)
    d.line([hx - 40, hy + 40, 170, floor - 40], fill=TEXT, width=46)    # body along the floor
    d.line([170, floor - 40, 110, floor - 25], fill=TEXT, width=34)     # legs
    d.line([hx - 70, hy + 45, hx + 10, floor - 20], fill=TEXT, width=30)  # forearm propping up
    # Line of sight under the desk
    for x in range(545, 760, 55):
        d.line([x, hy + 5, x + 30, hy + 5], fill=ACCENT, width=12)
    return img


def sit():
    img, d = base("SIT ANYWHERE")
    # Rooftop ledge on the left, the drop and the city below on the right
    top = 600
    d.rectangle([90, top, 520, top + 40], fill=RING)            # ledge top
    d.rectangle([90, top + 40, 470, S - 90], fill=RING)         # building wall
    for i, (x, h) in enumerate([(620, 170), (720, 250), (830, 130)]):
        d.rectangle([x, S - 90 - h, x + 80, S - 90], fill=DIM)
        for wy in range(S - 90 - h + 30, S - 110, 50):
            d.rectangle([x + 22, wy, x + 36, wy + 18], fill=ACCENT if (wy + i) % 3 else BG)
    # Moon
    d.ellipse([760, 250, 880, 370], fill=ACCENT)
    d.ellipse([730, 235, 850, 355], fill=BG)
    # Figure sitting on the edge, legs over the drop, looking out
    hx, hy = 455, top - 230
    d.ellipse([hx - 50, hy - 50, hx + 50, hy + 50], fill=TEXT)
    d.rounded_rectangle([hx - 95, hy - 42, hx + 95, hy - 24], radius=9, fill=TEXT)   # fedora brim
    d.rounded_rectangle([hx - 52, hy - 105, hx + 52, hy - 30], radius=22, fill=TEXT)  # crown
    d.rectangle([hx - 52, hy - 52, hx + 52, hy - 40], fill=ACCENT)                   # hat band
    d.line([hx - 10, hy + 50, hx - 40, top - 10], fill=TEXT, width=56)         # body
    d.line([hx - 40, top - 15, 545, top - 15], fill=TEXT, width=40)            # thigh along the ledge
    d.line([545, top - 15, 575, top + 150], fill=TEXT, width=36)               # shin over the drop
    d.line([hx - 5, hy + 90, 520, top - 60], fill=TEXT, width=26)              # arm resting on knee
    return img


def sanitation():
    img, d = base("SANITATION")
    floor = 820
    d.line([90, floor, S - 90, floor], fill=DIM, width=14)
    # Dustbin: tapered body, lid with handle, ribs
    d.polygon([(250, 430), (530, 430), (500, floor), (280, floor)], fill=RING, outline=TEXT, width=14)
    d.rounded_rectangle([225, 395, 555, 440], radius=14, fill=TEXT)
    d.rounded_rectangle([350, 355, 430, 395], radius=12, outline=TEXT, width=12)
    for x in (320, 390, 460):
        d.line([x, 470, x - 6 if x < 390 else (x + 6 if x > 390 else x), floor - 30], fill=DIM, width=12)
    # Lumpy tied trash bag beside it: two overlapping lobes, a neck and two knot ears
    d.ellipse([555, 620, 745, floor + 6], fill=TEXT)
    d.ellipse([640, 600, 820, floor + 6], fill=TEXT)
    d.polygon([(660, 615), (720, 615), (705, 560), (675, 560)], fill=TEXT)
    d.polygon([(690, 565), (640, 505), (675, 545)], fill=TEXT)
    d.polygon([(690, 565), (745, 510), (705, 548)], fill=TEXT)
    d.line([668, 568, 712, 568], fill=ACCENT, width=12)
    # A four-point sparkle: it's clean
    cx, cy, r, w = 770, 370, 75, 18
    d.polygon([(cx, cy - r), (cx + w, cy - w), (cx + r, cy), (cx + w, cy + w),
               (cx, cy + r), (cx - w, cy + w), (cx - r, cy), (cx - w, cy - w)], fill=ACCENT)
    return img


def city():
    img, d = base("MARGIN CITY")
    water = 800
    # Water with a few reflections
    d.rectangle([60, water, S - 60, S - 60], fill="#1d2a3a")
    for y in range(water + 30, S - 80, 34):
        d.line([170, y, S - 170, y], fill=RING, width=6)
    def tower(cx, w, top, spires=True, fill=RING):
        d.rectangle([cx - w // 2, top, cx + w // 2, water], fill=fill, outline=TEXT, width=8)
        for yy in range(top + 40, water - 20, 46):
            d.line([cx - w // 2 + 22, yy, cx + w // 2 - 22, yy], fill=DIM, width=6)
        if spires:
            for sx in (cx - w // 2 + 16, cx, cx + w // 2 - 16):
                h = 120 if sx == cx else 80
                d.polygon([(sx - 16, top), (sx + 16, top), (sx, top - h)], fill=TEXT)
    # Back row of low blocks
    for x0, h in ((120, 600), (240, 650), (760, 640), (860, 610)):
        d.rectangle([x0, h, x0 + 110, water], fill=BG, outline=DIM, width=6)
    tower(330, 150, 360)                      # Credit Tower
    tower(S - 330, 150, 360)                  # Debit Tower
    # The Ledger: a low civic block with a dome, between the towers
    d.rectangle([430, 620, S - 430, water], fill=RING, outline=TEXT, width=8)
    d.pieslice([450, 520, S - 450, 720], 180, 360, fill=ACCENT)
    d.line([S // 2, 520, S // 2, 470], fill=ACCENT, width=12)
    for x in range(470, S - 450, 40):
        d.line([x, 660, x, water - 20], fill=DIM, width=10)
    return img


def management():
    img = Image.new("RGB", (S, S), BG)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([40, 40, S - 40, S - 40], radius=120, outline=RING, width=18)
    # Striped awning over a shop window
    x0, x1, top = 130, S - 130, 150
    stripes = 8
    w = (x1 - x0) / stripes
    for i in range(stripes):
        d.polygon([(x0 + i * w, top), (x0 + (i + 1) * w, top), (x0 + (i + 1) * w + 6, top + 120), (x0 + i * w - 6, top + 120)],
                  fill=ACCENT if i % 2 == 0 else RING)
    for i in range(stripes):
        cx = x0 + (i + 0.5) * w
        d.pieslice([cx - w / 2, top + 80, cx + w / 2, top + 160], 0, 180, fill=ACCENT if i % 2 == 0 else RING)
    # Window frame and glass
    d.rectangle([x0 + 20, 300, x1 - 20, S - 120], fill="#1d2a3a", outline=TEXT, width=14)
    d.line([S // 2, 300, S // 2, S - 120], fill=TEXT, width=10)
    # Hanging sign on a string
    sx0, sx1, sy0, sy1 = 180, S - 180, 460, 730
    d.line([S // 2, 330, sx0 + 60, sy0], fill=DIM, width=8)
    d.line([S // 2, 330, sx1 - 60, sy0], fill=DIM, width=8)
    d.ellipse([S // 2 - 16, 314, S // 2 + 16, 346], fill=DIM)
    d.rounded_rectangle([sx0, sy0, sx1, sy1], radius=24, fill=TEXT, outline=ACCENT, width=12)
    small = ImageFont.truetype(FONT, 80)
    big = ImageFont.truetype(FONT, 84)
    d.text((S // 2, sy0 + 85), "UNDER NEW", font=small, fill=BG, anchor="mm")
    d.text((S // 2, sy0 + 185), "MANAGEMENT", font=big, fill=BG, anchor="mm")
    # A coin in the corner of the window
    cx, cy, r = x1 - 110, S - 195, 58
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=ACCENT, outline=TEXT, width=8)
    d.text((cx, cy + 4), "¢", font=ImageFont.truetype(FONT, 90), fill=BG, anchor="mm")
    return img


if __name__ == "__main__":
    kind, out = sys.argv[1], sys.argv[2]
    img = {"squint": squint, "license": license_, "mantle": mantle, "prone": prone, "sanitation": sanitation, "city": city,
           "management": management, "sit": sit}[kind]()
    img.resize((256, 256), Image.LANCZOS).save(out)
    print("wrote", out)
