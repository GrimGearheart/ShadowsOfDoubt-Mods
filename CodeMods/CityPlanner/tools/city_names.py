"""Naming pass for a generated Shadows of Doubt city.

  python CodeMods/CityPlanner/tools/city_names.py export <city.citb> <names.csv>
      Writes every nameable thing (city, districts, streets, buildings, businesses) to a spreadsheet with an
      empty "new_name" column.

  python CodeMods/CityPlanner/tools/city_names.py apply <city.citb> <names.csv>
      Reads the spreadsheet and writes a renamed copy of the city next to the original (the original is not
      touched). Rows with an empty new_name are left alone. Renaming the city changes its share code and file
      name, so saves made in the old city keep using the old file.

A building's name is repeated in its flats ("1001 Coleman Suites"), rooms and some business names; renaming a
building updates all of them. City files are brotli-compressed JSON with the uncompressed size appended as a
4-byte little-endian integer.
"""
import collections
import csv
import json
import os
import sys

import brotli

FIELDS = ["kind", "id", "district", "type", "detail", "name", "new_name"]


def load(path):
    raw = open(path, "rb").read()
    data = brotli.decompress(raw[:-4]) if path.lower().endswith(".citb") else raw
    return json.loads(data.decode("utf-8"))


def save(city, path):
    text = json.dumps(city, ensure_ascii=False, separators=(",", ":"), allow_nan=True).encode("utf-8")
    if path.lower().endswith(".citb"):
        packed = brotli.compress(text, quality=9, lgwin=22)
        open(path, "wb").write(packed + len(text).to_bytes(4, "little"))
    else:
        open(path, "wb").write(text)


def district_names(city):
    return {d["districtID"]: d["name"] for d in city["districts"]}


def street_district(street, tile_district, per_tile):
    counts = collections.Counter()
    for t in street.get("tiles", []):
        counts[tile_district.get((int(t["x"] // per_tile), int(t["y"] // per_tile)), "?")] += 1
    return counts.most_common(1)[0][0] if counts else "?"


def companies(building):
    for f in building.get("floors", []):
        for a in f.get("addresses", []):
            co = a.get("company")
            if isinstance(co, dict) and co.get("preset"):
                yield a, co


def export(city, out):
    dn = district_names(city)
    tile_district = {(t["cityCoord"]["x"], t["cityCoord"]["y"]): dn.get(t["districtID"], "?") for t in city["cityTiles"]}
    w = int(city["citySize"]["x"])
    per_tile = (max(tt["x"] for s in city["streets"] for tt in s["tiles"]) + 1) / w
    rows = [dict(kind="city", id="", district="", type="", detail="", name=city["cityName"])]
    for d in city["districts"]:
        rows.append(dict(kind="district", id=d["districtID"], district=d["name"], type=d["preset"], detail="", name=d["name"]))
    for s in sorted(city["streets"], key=lambda s: (street_district(s, tile_district, per_tile), s["name"])):
        kind = "alley" if s.get("isAlley") else "back street" if s.get("isBackstreet") else "street"
        rows.append(dict(kind="street", id=s["streetID"], district=street_district(s, tile_district, per_tile), type=kind,
                         detail=f'{len(s.get("tiles", []))} tiles', name=s["name"]))
    for t in city["cityTiles"]:
        b = t["building"]
        if "Boundary" in b.get("preset", ""):
            continue
        x, y = t["cityCoord"]["x"], t["cityCoord"]["y"]
        dist = dn.get(t["districtID"], "?")
        floors = [f.get("floor", 0) for f in b.get("floors", [])]
        rows.append(dict(kind="building", id=f"{x},{y}", district=dist, type=b["preset"],
                         detail=f"{max(floors) + 1 if floors else 0} floors", name=b["name"]))
        for a, co in companies(b):
            rows.append(dict(kind="business", id=a["id"], district=dist, type=co["preset"], detail=f'in {b["name"]}', name=a["name"]))
    with open(out, "w", newline="", encoding="utf-8-sig") as fh:
        wr = csv.DictWriter(fh, FIELDS)
        wr.writeheader()
        for r in rows:
            r["new_name"] = ""
            wr.writerow(r)
    counts = collections.Counter(r["kind"] for r in rows)
    print(f"wrote {out}: " + ", ".join(f"{v} {k}s" for k, v in counts.items()))


def replace_strings(node, old, new, keys=("name", "shortName")):
    """Replace old with new in the given string fields anywhere under node."""
    n = 0
    if isinstance(node, dict):
        for k, v in node.items():
            if k in keys and isinstance(v, str) and old in v:
                node[k] = v.replace(old, new)
                n += 1
            elif isinstance(v, (dict, list)):
                n += replace_strings(v, old, new, keys)
    elif isinstance(node, list):
        for v in node:
            n += replace_strings(v, old, new, keys)
    return n


def apply(city, path, sheet):
    rows = [r for r in csv.DictReader(open(sheet, encoding="utf-8-sig")) if r.get("new_name", "").strip()]
    by_kind = collections.defaultdict(list)
    for r in rows:
        by_kind[r["kind"]].append(r)
    changes = 0

    for r in by_kind["district"]:
        for d in city["districts"]:
            if str(d["districtID"]) == r["id"]:
                d["name"] = r["new_name"].strip()
                changes += 1
    tiles = {f'{t["cityCoord"]["x"]},{t["cityCoord"]["y"]}': t for t in city["cityTiles"]}
    # Businesses before buildings, so a business named after its building is matched by its old name first.
    addresses = {}
    for t in city["cityTiles"]:
        for a, co in companies(t["building"]):
            addresses[str(a["id"])] = (a, co)
    for r in by_kind["business"]:
        a, co = addresses.get(r["id"], (None, None))
        if not a:
            continue
        old, new = a["name"], r["new_name"].strip()
        replace_strings(a.get("rooms", []), old, new)
        a["name"] = new
        co["shortName"] = new
        changes += 1
    for r in by_kind["building"]:
        t = tiles.get(r["id"])
        if not t:
            continue
        b = t["building"]
        old, new = b["name"], r["new_name"].strip()
        n = replace_strings(b.get("floors", []), old, new)
        b["name"] = new
        changes += 1
        print(f"  building {old!r} -> {new!r}: {n} flat/room/business names updated")

    # Streets last: businesses and buildings are matched by their original names first.
    streets = {str(s["streetID"]): s for s in city["streets"]}
    for r in by_kind["street"]:
        s = streets.get(r["id"])
        if s:
            old, new = s["name"], r["new_name"].strip()
            s["name"] = new
            changes += 1
            # Businesses and addresses named after the street ("Johansen Street Sync Clinic") follow it.
            n = sum(replace_strings(t["building"].get("floors", []), old, new) for t in city["cityTiles"])
            if n:
                print(f"  street {old!r} -> {new!r}: {n} business/address names updated")
    folder = os.path.dirname(path)
    stem = os.path.basename(path).rsplit(".", 1)[0]
    parts = stem.split(".")
    ext = os.path.splitext(path)[1]
    city_rows = by_kind["city"]
    if city_rows:
        city["cityName"] = city_rows[0]["new_name"].strip()
        changes += 1
    parts[0] = city["cityName"]
    share = ".".join(parts)
    out = os.path.join(folder, share + ext)
    if os.path.abspath(out) == os.path.abspath(path):
        raise SystemExit("Refusing to overwrite the original city: rename the city in the sheet, or copy the file first.")
    save(city, out)
    info = json.load(open(os.path.join(folder, stem + ".txt"), encoding="utf-8"))
    info["cityName"] = city["cityName"]
    info["shareCode"] = share
    json.dump(info, open(os.path.join(folder, share + ".txt"), "w", encoding="utf-8"), indent=4)
    print(f"applied {changes} renames -> {out}")


if __name__ == "__main__":
    mode, src, sheet = sys.argv[1], sys.argv[2], sys.argv[3]
    city = load(src)
    if mode == "export":
        export(city, sheet)
    elif mode == "apply":
        apply(city, src, sheet)
    else:
        raise SystemExit(__doc__)
