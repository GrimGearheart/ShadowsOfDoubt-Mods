"""Builds the Release DLL and zips a Thunderstore package into ./dist.

Usage: python package.py
"""
import json
import os
import subprocess
import sys
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
DLL = os.path.join(PROJECT, "bin", "Release", "net6.0", "ScrollThrottle.dll")

manifest = json.load(open(os.path.join(HERE, "manifest.json"), encoding="utf-8"))
if len(manifest["description"]) > 250:
    sys.exit("manifest description must be 250 characters or fewer")

subprocess.run(["dotnet", "build", "-c", "Release", PROJECT], check=True)

dist = os.path.join(HERE, "dist")
os.makedirs(dist, exist_ok=True)
out = os.path.join(dist, f"{manifest['name']}-{manifest['version_number']}.zip")
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for name in ("manifest.json", "icon.png", "README.md", "CHANGELOG.md"):
        z.write(os.path.join(HERE, name), name)
    z.write(DLL, "ScrollThrottle.dll")
print("Packaged", out)
