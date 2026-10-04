"""Draws the 256x256 Thunderstore icon: a noir-toned throttle gauge with a mouse wheel."""
import math
import os
from PIL import Image, ImageDraw, ImageFont

S = 1024  # draw large, downsample for smooth edges
BG, RING, DIM, ACCENT, TEXT = "#15121c", "#3a3348", "#6b6280", "#e8b04b", "#f2ece0"

img = Image.new("RGB", (S, S), BG)
d = ImageDraw.Draw(img)

# Vignette-ish rounded panel
d.rounded_rectangle([40, 40, S - 40, S - 40], radius=120, outline=RING, width=18)

cx, cy, r = S // 2, int(S * 0.56), int(S * 0.36)
start, end = 200, 340  # gauge sweep in degrees (PIL: 0 = east, clockwise)
d.arc([cx - r, cy - r, cx + r, cy + r], start - 20, end + 20, fill=RING, width=46)
d.arc([cx - r, cy - r, cx + r, cy + r], start - 20, 300, fill=ACCENT, width=46)

# Tick marks
for i in range(9):
    a = math.radians(start - 20 + i * (end - start + 40) / 8)
    r1, r2 = r - 70, r - 120 if i % 2 == 0 else r - 95
    d.line([cx + r1 * math.cos(a), cy + r1 * math.sin(a), cx + r2 * math.cos(a), cy + r2 * math.sin(a)],
           fill=DIM, width=16)

# Needle
a = math.radians(300)
d.line([cx, cy, cx + (r - 90) * math.cos(a), cy + (r - 90) * math.sin(a)], fill=TEXT, width=26)
d.ellipse([cx - 44, cy - 44, cx + 44, cy + 44], fill=TEXT)

# Mouse with wheel, lower centre
mx, my, mw, mh = cx, int(S * 0.80), 150, 210
d.rounded_rectangle([mx - mw // 2, my - mh // 2, mx + mw // 2, my + mh // 2], radius=75, outline=TEXT, width=16)
d.line([mx, my - mh // 2, mx, my - 20], fill=TEXT, width=12)
d.rounded_rectangle([mx - 18, my - 75, mx + 18, my - 15], radius=18, fill=ACCENT)

font = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", 120)
d.text((cx, int(S * 0.16)), "THROTTLE", font=font, fill=TEXT, anchor="mm")

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "icon.png")
img.resize((256, 256), Image.LANCZOS).save(out)
print("wrote", out)
