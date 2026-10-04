"""Builds a code mod in Release and zips its Thunderstore package.

Each mod keeps its package files in <project>/thunderstore/:
    manifest.json, icon.png (256x256), README.md, CHANGELOG.md, and optionally files/ (extra files to ship)
The zip is written to <project>/thunderstore/dist/<name>-<version>.zip

Usage: python tools/package_thunderstore.py CodeMods/<ModName>
"""
import json
import os
import re
import subprocess
import sys
import zipfile

from PIL import Image

if len(sys.argv) != 2:
    sys.exit(__doc__)

project = os.path.abspath(sys.argv[1])
ts = os.path.join(project, "thunderstore")
manifest = json.load(open(os.path.join(ts, "manifest.json"), encoding="utf-8"))

# Thunderstore validation rules, checked up front so an upload doesn't bounce.
if not re.fullmatch(r"[A-Za-z0-9_]+", manifest["name"]):
    sys.exit("manifest name may only contain letters, numbers and underscores")
if not re.fullmatch(r"\d+\.\d+\.\d+", manifest["version_number"]):
    sys.exit("version_number must be Major.Minor.Patch")
if len(manifest["description"]) > 250:
    sys.exit("manifest description must be 250 characters or fewer")
if Image.open(os.path.join(ts, "icon.png")).size != (256, 256):
    sys.exit("icon.png must be 256x256")

# Keep the plugin's own version in step with the manifest.
csproj = next(f for f in os.listdir(project) if f.endswith(".csproj"))
version = re.search(r"<Version>(.*?)</Version>", open(os.path.join(project, csproj), encoding="utf-8").read())
if not version or version.group(1) != manifest["version_number"]:
    sys.exit(f"{csproj} <Version> does not match manifest version {manifest['version_number']}")

subprocess.run(["dotnet", "build", "-c", "Release", project], check=True)
assembly = os.path.splitext(csproj)[0] + ".dll"
dll = os.path.join(project, "bin", "Release", "net6.0", assembly)

dist = os.path.join(ts, "dist")
os.makedirs(dist, exist_ok=True)
out = os.path.join(dist, f"{manifest['name']}-{manifest['version_number']}.zip")
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for name in ("manifest.json", "icon.png", "README.md", "CHANGELOG.md"):
        z.write(os.path.join(ts, name), name)
    z.write(dll, assembly)
    # Optional extra files (e.g. bundled cities) under thunderstore/files/, kept at their relative paths.
    extra = os.path.join(ts, "files")
    for root, _, files in os.walk(extra):
        for f in files:
            full = os.path.join(root, f)
            z.write(full, os.path.relpath(full, extra).replace(os.sep, "/"))
print("Packaged", out)
