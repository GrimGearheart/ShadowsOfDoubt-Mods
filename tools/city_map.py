"""Draws a street map of a generated Shadows of Doubt city.

Usage: python tools/city_map.py <city.cit|city.citb | latest> <out.png>
"""
import collections
import glob
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(__file__))
from city_names import load  # noqa: E402

CITIES = os.path.expandvars(r"%LOCALAPPDATA%\..\LocalLow\ColePowered Games\Shadows of Doubt\Cities")
FONT = "C:/Windows/Fonts/bahnschrift.ttf"
FONT_BOLD = "C:/Windows/Fonts/segoeuib.ttf"

PAPER = (24, 26, 33)
WATER = (22, 38, 56)
WATER_LINE = (40, 66, 92)
ROAD = (86, 88, 96)
BACK = (66, 68, 76)
ALLEY = (52, 53, 60)
PARK = (58, 104, 70)
PARK_EDGE = (82, 138, 96)
INK = (236, 232, 222)
MUTED = (160, 160, 168)
DOOR = (246, 214, 120)
DISTRICT = {  # by planned name, falls back by type
    "The Heights": (176, 74, 66), "Park Terrace": (196, 150, 64), "Downtown": (120, 94, 186),
    "Chinatown": (150, 120, 92), "Red Light": (190, 60, 110), "The Bricks": (168, 88, 140),
    "West Stacks": (70, 112, 170), "East Stacks": (70, 112, 170),
}
TYPE_COLOUR = {"Residential": (150, 110, 90), "CBD": (120, 94, 186), "Chinatown": (150, 120, 92),
               "RedLight": (190, 60, 110), "Industrial": (70, 112, 170)}
LANDMARKS = {"CityHall", "Hotel", "EdenTower", "AmericanDiner", "ChemicalPlant", "BrandyNetherland", "OneFIfthAve"}


def font(size, bold=False):
    try:
        f = ImageFont.truetype(FONT, size)
        if bold:
            f.set_variation_by_name("Bold")
        return f
    except Exception:
        return ImageFont.truetype(FONT_BOLD, size)


def main(src, out):
    if src == "latest":
        src = max(glob.glob(os.path.join(CITIES, "*.cit*")), key=os.path.getmtime)
    city = load(src)
    W, H = int(city["citySize"]["x"]), int(city["citySize"]["y"])
    districts = {d["districtID"]: d for d in city["districts"]}

    # Ground-floor footprint of every building, in global tiles
    foot, entrances = {}, []
    tiles_all = []
    for t in city["cityTiles"]:
        b = t["building"]
        ground = [f for f in b.get("floors", []) if f.get("floor", 0) == 0] or b.get("floors", [])[:1]
        cells = []
        for f in ground:
            for tt in f.get("tiles", []):
                g = tt["globalTileCoord"]
                cells.append((g["x"], g["y"]))
                if tt.get("isMainEntrance"):
                    entrances.append((g["x"], g["y"]))
        foot[(t["cityCoord"]["x"], t["cityCoord"]["y"])] = (t, cells)
        tiles_all += cells
    street_cells = {}
    for s in city["streets"]:
        kind = "alley" if s.get("isAlley") else "back" if s.get("isBackstreet") else "road"
        for tt in s["tiles"]:
            street_cells[(tt["x"], tt["y"])] = kind
            tiles_all.append((tt["x"], tt["y"]))
    ox = min(x for x, _ in tiles_all) - 2
    oy = min(y for _, y in tiles_all) - 2
    gx = max(x for x, _ in tiles_all) + 3 - ox
    gy = max(y for _, y in tiles_all) + 3 - oy

    cs = 18
    margin, top, side = 40, 120, 330
    img = Image.new("RGB", (margin * 2 + gx * cs + side, top + gy * cs + margin), PAPER)
    d = ImageDraw.Draw(img, "RGBA")

    def box(x, y, inset=0):
        px = margin + (x - ox) * cs
        py = top + (gy - 1 - (y - oy)) * cs   # north (high y) at the top
        return [px + inset, py + inset, px + cs - 1 - inset, py + cs - 1 - inset]

    # Water everywhere, then the land
    d.rectangle([margin, top, margin + gx * cs - 1, top + gy * cs - 1], fill=WATER)
    for i in range(0, gx * cs, 26):
        d.line([margin + i, top, margin + i + 40, top + gy * cs - 1], fill=WATER_LINE + (60,), width=1)

    # Streets
    for (x, y), kind in street_cells.items():
        d.rectangle(box(x, y), fill={"road": ROAD, "back": BACK, "alley": ALLEY}[kind])

    # Buildings
    label_spots = []
    for (cx, cy), (t, cells) in foot.items():
        b = t["building"]
        preset = b.get("preset", "")
        dist = districts.get(t.get("districtID"), {})
        if "Boundary" in preset:
            for x, y in cells:
                d.rectangle(box(x, y), fill=(38, 44, 52))
            continue
        if preset == "Park":
            colour = PARK
        else:
            colour = DISTRICT.get(dist.get("name"), TYPE_COLOUR.get(dist.get("preset"), (120, 120, 120)))
        for x, y in cells:
            d.rectangle(box(x, y), fill=colour)
        if cells:
            xs = [x for x, _ in cells]
            ys = [y for _, y in cells]
            outline = [margin + (min(xs) - ox) * cs, top + (gy - 1 - (max(ys) - oy)) * cs,
                       margin + (max(xs) + 1 - ox) * cs - 1, top + (gy - (min(ys) - oy)) * cs - 1]
            d.rectangle(outline, outline=PARK_EDGE if preset == "Park" else (20, 20, 26), width=2)
            label_spots.append((preset, b["name"], outline))

    # Front doors
    for x, y in entrances:
        bx = box(x, y, 4)
        d.ellipse(bx, fill=DOOR)

    # Building labels: landmarks in bold, other names small
    for preset, name, (x0, y0, x1, y1) in label_spots:
        if preset == "Park" and name == "Park":
            continue
        big = preset in LANDMARKS
        f = font(15 if big else 12, bold=big)
        words = name.split()
        lines, line = [], ""
        for w_ in words:
            trial = (line + " " + w_).strip()
            if d.textlength(trial, font=f) > (x1 - x0 - 10) and line:
                lines.append(line)
                line = w_
            else:
                line = trial
        lines.append(line)
        cy_ = (y0 + y1) / 2 - (len(lines) - 1) * (f.size + 2) / 2
        for i, ln in enumerate(lines):
            d.text(((x0 + x1) / 2, cy_ + i * (f.size + 2)), ln, font=f, fill=INK, anchor="mm",
                   stroke_width=3, stroke_fill=(16, 16, 22))

    # Street names on proper roads, along their length
    sf = font(12)
    for s in city["streets"]:
        if s.get("isAlley") or s.get("isBackstreet") or len(s["tiles"]) < 10:
            continue
        xs = [t["x"] for t in s["tiles"]]
        ys = [t["y"] for t in s["tiles"]]
        horizontal = (max(xs) - min(xs)) >= (max(ys) - min(ys))
        cx_, cy_ = sum(xs) / len(xs), sum(ys) / len(ys)
        tx, ty = min(((t["x"], t["y"]) for t in s["tiles"]), key=lambda p: (p[0] - cx_) ** 2 + (p[1] - cy_) ** 2)
        px, py = margin + (tx - ox + .5) * cs, top + (gy - 1 - (ty - oy) + .5) * cs
        tw = int(d.textlength(s["name"], font=sf)) + 8
        layer = Image.new("RGBA", (tw, 18), (0, 0, 0, 0))
        ImageDraw.Draw(layer).text((tw / 2, 9), s["name"], font=sf, fill=(230, 230, 236), anchor="mm")
        if not horizontal:
            layer = layer.rotate(90, expand=True)
        img.paste(layer, (int(px - layer.width / 2), int(py - layer.height / 2)), layer)
    d = ImageDraw.Draw(img, "RGBA")

    # District names, large and faint, over each district's centre
    by_district = collections.defaultdict(list)
    for (cx, cy), (t, cells) in foot.items():
        if "Boundary" not in t["building"].get("preset", "") and t["building"].get("preset") != "Park":
            by_district[t.get("districtID")] += cells
    df = font(17, bold=True)
    for did, cells in by_district.items():
        if not cells:
            continue
        top_row = max(y for _, y in cells)
        xs = [x for x, y in cells if y == top_row]
        name = districts.get(did, {}).get("name", "?").upper()
        px = margin + (min(xs) - ox) * cs
        py = top + (gy - 1 - (top_row - oy)) * cs - 4
        d.text((px, py), name, font=df, fill=(255, 255, 255, 215), anchor="ls", stroke_width=3, stroke_fill=(16, 16, 22))

    # Title, compass, legend
    d.text((margin, 40), city["cityName"].upper(), font=font(48, bold=True), fill=INK, anchor="lm")
    d.text((margin, 82), f'{int(city["citySize"]["x"])} × {int(city["citySize"]["y"])} blocks  ·  {len(city["citizens"]):,} residents  ·  {len(city["streets"])} streets',
           font=font(16), fill=MUTED, anchor="lm")
    lx = margin * 2 + gx * cs
    nx, ny = lx + 40, top + 30
    d.polygon([(nx, ny - 22), (nx - 10, ny + 8), (nx, ny), (nx + 10, ny + 8)], fill=INK)
    d.text((nx, ny + 22), "N", font=font(16, bold=True), fill=INK, anchor="mm")
    ly = top + 80
    d.text((lx, ly), "DISTRICTS", font=font(16, bold=True), fill=INK)
    ly += 30
    for dist in city["districts"]:
        col = DISTRICT.get(dist["name"], TYPE_COLOUR.get(dist["preset"], (120, 120, 120)))
        d.rectangle([lx, ly, lx + 22, ly + 16], fill=col)
        d.text((lx + 32, ly + 8), dist["name"], font=font(15), fill=INK, anchor="lm")
        ly += 26
    ly += 14
    d.text((lx, ly), "KEY", font=font(16, bold=True), fill=INK)
    ly += 30
    for col, label in [(PARK, "Park"), (ROAD, "Street"), (BACK, "Back street"), (ALLEY, "Alley"), (WATER, "Water")]:
        d.rectangle([lx, ly, lx + 22, ly + 16], fill=col)
        d.text((lx + 32, ly + 8), label, font=font(15), fill=INK, anchor="lm")
        ly += 26
    d.ellipse([lx + 6, ly + 3, lx + 16, ly + 13], fill=DOOR)
    d.text((lx + 32, ly + 8), "Main entrance", font=font(15), fill=INK, anchor="lm")
    img.save(out)
    print("wrote", out, img.size)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
