"""Draws an overview map of a generated city and reports wealth and street decoration by district.

Usage: python tools/city_overview.py <city.cit|city.citb | latest> <out.png>
  "latest" picks the newest city in the game's Cities folder.
"""
import collections
import glob
import json
import os
import re
import sys

import brotli
from PIL import Image, ImageDraw, ImageFont

BUILDINGS = [  # code, preset, label
    ("CH", "CityHall", "City Hall"),
    ("HO", "Hotel", "Hotel"),
    ("ET", "EdenTower", "Commercial Tower"),
    ("OF", "OneFIfthAve", "Apartment Tower (One Fifth)"),
    ("BN", "BrandyNetherland", "Apartment Tower (Netherland)"),
    ("TH", "Townhouse", "Town-house"),
    ("TS", "TownhouseShops", "Town-house with shopfront"),
    ("SH", "ShantyTown", "Fathoms"),
    ("PK", "Park", "Park"),
    ("DI", "AmericanDiner", "Diner"),
    ("MI", "MixedIndustrial", "Mixed Industrial"),
    ("CP", "ChemicalPlant", "Chemical Plant"),
    ("~", None, "Coastline"),
    ("▬", None, "Front of the building"),
]
CODES = {preset: code for code, preset, _ in BUILDINGS if preset}
COLOURS = ["#5b8def", "#9b6bd6", "#e8b04b", "#d9534f", "#e377c2", "#8c7a6b", "#6b6b6b", "#3aa17e", "#c9a227", "#5f9ea0", "#bc8f8f"]
LAND = ["veryLow", "low", "medium", "high", "veryHigh"]
GRIT = ["Shanty", "BurningBarrel", "StreetJunk", "StaticLitter"]
CITIES = os.path.expandvars(r"%LOCALAPPDATA%\..\LocalLow\ColePowered Games\Shadows of Doubt\Cities")


def load(path):
    data = open(path, "rb").read()
    if path.lower().endswith(".citb"):
        for candidate in (data, data[:-4], data[4:], data[8:], data[:-8]):
            try:
                data = brotli.decompress(candidate)
                break
            except brotli.error:
                continue
        else:
            raise SystemExit("couldn't decompress " + path)
    return json.loads(data.decode("utf-8"))


def main(src, out):
    if src == "latest":
        src = max(glob.glob(os.path.join(CITIES, "*.cit*")), key=os.path.getmtime)
    print("City:", os.path.basename(src))
    city = load(src)
    w, h = int(city["citySize"]["x"]), int(city["citySize"]["y"])
    districts = {d["districtID"]: d for d in city["districts"]}
    colour = {did: COLOURS[i % len(COLOURS)] for i, did in enumerate(districts)}

    cell, left, right = 96, 300, 300
    img = Image.new("RGB", (left + w * cell + right, max(h * cell + 40, 560)), "#15121c")
    d = ImageDraw.Draw(img)
    font = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", 22)
    bold = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", 15)
    small = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 14)

    # Building key on the left
    d.text((20, 24), "BUILDINGS", font=bold, fill="white")
    for i, (code, _, label) in enumerate(BUILDINGS):
        y = 58 + i * 32
        d.rectangle([20, y - 12, 56, y + 12], fill="#2a2536")
        d.text((38, y), code, font=bold, fill="white", anchor="mm")
        d.text((68, y), label, font=small, fill="white", anchor="lm")

    wealth = collections.defaultdict(list)
    for t in city["cityTiles"]:
        x, y = t["cityCoord"]["x"], t["cityCoord"]["y"]
        px, py = left + x * cell, 20 + (h - 1 - y) * cell   # north (high y) at the top
        b = t["building"]
        preset = b.get("preset", "")
        boundary = "Boundary" in preset
        code = "~" if boundary else CODES.get(preset, preset[:3])
        did = t.get("districtID")
        d.rectangle([px + 2, py + 2, px + cell - 2, py + cell - 2], fill="#1d3557" if boundary else colour.get(did, "#333"))
        d.text((px + cell / 2, py + cell / 2 - 10), code, font=font, fill="white", anchor="mm")
        lv = t.get("landValue")
        d.text((px + cell / 2, py + cell - 18), f"LV {LAND[lv] if isinstance(lv, int) else lv}", font=small, fill="white", anchor="mm")
        # Front of the building: a bar on the side its front door is on (north = up). The game's facing value
        # names the opposite side: North (0) means the front is on the south side, East (1) on the west.
        facing = b.get("facing")
        if isinstance(facing, int) and not boundary and preset != "Park":
            bar = {2: [px + 14, py + 4, px + cell - 14, py + 9],          # front on the north side
                   3: [px + cell - 9, py + 14, px + cell - 4, py + cell - 14],  # front on the east side
                   0: [px + 14, py + cell - 9, px + cell - 14, py + cell - 4],  # front on the south side
                   1: [px + 4, py + 14, px + 9, py + cell - 14]}.get(facing)    # front on the west side
            if bar:
                d.rectangle(bar, fill="#f2ece0")
        floors = [f.get("floor", 0) for f in b.get("floors", [])]
        if floors and not boundary:
            d.text((px + cell - 8, py + 8), f"{max(floors) + 1}F", font=small, fill="#f2ece0", anchor="ra")
        if not boundary:
            for f in b.get("floors", []):
                for a in f.get("addresses", []):
                    r = a.get("residence")
                    if isinstance(r, dict) and r.get("preset"):
                        wealth[districts.get(did, {}).get("name", "?")].append(float(a.get("landValue", 0)))

    # District legend on the right
    rx = left + w * cell + 30
    d.text((rx, 24), "DISTRICTS", font=bold, fill="white")
    for i, (did, dist) in enumerate(districts.items()):
        y = 58 + i * 32
        d.rectangle([rx, y - 10, rx + 20, y + 10], fill=colour[did])
        d.text((rx + 30, y), f'{dist["name"]} ({dist["preset"]})', font=small, fill="white", anchor="lm")
    img.save(out)
    print("wrote", out)

    print("\nHome wealth by district (0-1, residences only):")
    for name, vals in sorted(wealth.items(), key=lambda kv: -sum(kv[1]) / len(kv[1])):
        print(f"  {name:15} {len(vals):4} homes  avg {sum(vals) / len(vals):.2f}  range {min(vals):.2f}-{max(vals):.2f}")

    # Street decoration by district
    tile_district = {(t["cityCoord"]["x"], t["cityCoord"]["y"]): districts.get(t["districtID"], {}).get("name", "?")
                     for t in city["cityTiles"]}
    placed, most = [], 0
    for st in city["streets"]:
        for room in st["rooms"]:
            for fc in room.get("f", []):
                a = fc["anchorNode"]
                most = max(most, a["x"], a["y"])
                placed.append((fc["cluster"], a["x"], a["y"]))
    per_tile = (most + 1) / w
    grit = collections.Counter()
    homes = collections.Counter(tile_district.values())
    for name, x, y in placed:
        if any(g in name for g in GRIT):
            grit[tile_district.get((int(x // per_tile), int(y // per_tile)), "?")] += 1
    # Dead-end walls: the game splits some alleys in two with a wall; each split pair shares ground.
    streets = {st["streetID"]: st for st in city["streets"]}
    tile_max = max(tt["x"] for st in city["streets"] for tt in st["tiles"]) + 1
    per_city_tile = tile_max / w
    walls = collections.Counter()
    seen = set()
    for sid, st in streets.items():
        if not st.get("isAlley"):
            continue
        for other in st.get("sharedGround", []):
            o = streets.get(other)
            pair = frozenset((sid, other))
            if not o or not o.get("isAlley") or pair in seen:
                continue
            seen.add(pair)
            half = min((st, o), key=lambda z: len(z["tiles"]))
            tt = half["tiles"][0]
            walls[tile_district.get((int(tt["x"] // per_city_tile), int(tt["y"] // per_city_tile)), "?")] += 1

    # District-themed decoration, using the list City Planner writes to the BepInEx log
    themed = {}
    log = r"F:\SteamLibrary\steamapps\common\Shadows of Doubt\BepInEx\LogOutput.log"
    if os.path.exists(log):
        for line in open(log, encoding="utf-8", errors="ignore"):
            m = re.search(r"Themed decoration (\S+): only ([\w+]+)", line)
            if m:
                themed[m.group(1)] = set(m.group(2).split("+"))
    district_type = {dd["name"]: dd["preset"] for dd in districts.values()}
    theme_in, theme_out = collections.Counter(), collections.Counter()
    for name, x, y in placed:
        if name in themed:
            where = tile_district.get((int(x // per_tile), int(y // per_tile)), "?")
            (theme_in if district_type.get(where) in themed[name] else theme_out)[where] += 1

    # Businesses and homes
    biz, res = collections.Counter(), collections.Counter()
    for t in city["cityTiles"]:
        dn = tile_district.get((t["cityCoord"]["x"], t["cityCoord"]["y"]), "?")
        for f in t["building"].get("floors", []):
            for a in f.get("addresses", []):
                if isinstance(a.get("company"), dict) and a["company"].get("preset"):
                    biz[dn] += 1
                elif isinstance(a.get("residence"), dict) and a["residence"].get("preset"):
                    res[dn] += 1

    def avg_wealth(n):
        v = wealth.get(n)
        return sum(v) / len(v) if v else 0

    print("\nScorecard by district (grit = shacks/barrel fires/junk/litter on its streets):")
    print(f"  {'district':15} {'homes':>6} {'biz':>5} {'grit':>5} {'dead ends':>10} {'themed':>7}")
    for name in sorted(homes, key=lambda n: -avg_wealth(n)):
        print(f"  {name:15} {res[name]:6} {biz[name]:5} {grit[name]:5} {walls[name]:10} {theme_in[name]:7}")
    print(f"  dead-end walls in total: {sum(walls.values())}" +
          ("" if themed else "  (themed counts need a City Planner log that lists themed decorations)"))
    if theme_out:
        print("  themed decoration outside its district: " + ", ".join(f"{k} {v}" for k, v in theme_out.items()))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
