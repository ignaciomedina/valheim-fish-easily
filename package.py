#!/usr/bin/env python3
"""Verify Fish-Easily's reflection targets exist in the decompiled Valheim source,
then package the Thunderstore zip.

Run from the project directory:
    python3 package.py

The decompiled source in /tmp/valheim-src does not survive a reboot. Regenerate it with:
    mkdir -p /tmp/valheim-src
    for t in FishingFloat Fish; do
        ilspycmd -t $t <Valheim>/valheim_Data/Managed/assembly_valheim.dll > /tmp/valheim-src/$t.cs
    done

Step 1 asserts that every member Fish-Easily reaches by name is really declared in
assembly_valheim.dll (checked against ilspycmd output, so it is evidence and not a
recollection). Step 2 builds the Thunderstore-format zip r2modman imports.
"""
import json
import os
import re
import subprocess
import sys
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
REFS = os.environ.get("FISHEASILY_REFS", "/tmp/valheim-src")  # decompiled FishingFloat.cs, Fish.cs
DLL = os.path.join(HERE, "bin", "Release", "FishEasily.dll")

# member -> declaration pattern expected in the decompiled source
EXPECTED = {
    "FishingFloat.cs": {
        "m_nibbler": r"private\s+Fish\s+m_nibbler;",
        "m_nibbleTime": r"private\s+float\s+m_nibbleTime;",
        "m_baitConsumed": r"private\s+bool\s+m_baitConsumed;",
        "m_nview": r"private\s+ZNetView\s+m_nview;",
        "SetCatch": r"private\s+void\s+SetCatch\(Fish\s+fish\)",
        "ReturnBait": r"private\s+void\s+ReturnBait\(\)",
        "GetOwner": r"private\s+Character\s+GetOwner\(\)",
        "FixedUpdate": r"void\s+FixedUpdate\(\)",
        "Awake": r"void\s+Awake\(\)",
        "Catch": r"public\s+static\s+string\s+Catch\(Fish\s+fish,\s*Character\s+owner\)",
        "GetCatch": r"public\s+Fish\s+GetCatch\(\)",
        "m_pullStaminaUse": r"public\s+float\s+m_pullStaminaUse\s*=",
        "m_breakDistance": r"public\s+float\s+m_breakDistance\s*=",
        "m_maxDistance": r"public\s+float\s+m_maxDistance\s*=",
    },
    "Fish.cs": {
        "m_failedBait": r"private\s+FishingFloat\s+m_failedBait;",
        "TestBate": r"private\s+bool\s+TestBate\(FishingFloat\s+ff\)",
        "FindFloat": r"private\s+FishingFloat\s+FindFloat\(\)",
        "Escape": r"private\s+void\s+Escape\(\)",
        "GetStaminaUse": r"public\s+float\s+GetStaminaUse\(\)",
        "OnHooked": r"public\s+void\s+OnHooked\(FishingFloat\s+ff\)",
    },
}

failures = []

print("== 1. reflection targets vs decompiled Valheim source")
for filename, members in EXPECTED.items():
    path = os.path.join(REFS, filename)
    if not os.path.exists(path):
        failures.append(f"{filename} missing (run the ilspycmd dump in the docstring)")
        continue
    src = open(path, encoding="utf-8").read()
    for member, pattern in members.items():
        ok = re.search(pattern, src) is not None
        print(f"   {'OK  ' if ok else 'MISS'} {filename:18s} {member}")
        if not ok:
            failures.append(f"{filename}: {member}")

print("\n== 2. strings baked into the built assembly")
blob = open(DLL, "rb").read()
for needle in ["TestBate", "FindFloat", "Escape", "GetStaminaUse", "FixedUpdate",
               "Awake", "m_nibbler", "m_nibbleTime", "m_baitConsumed", "m_nview",
               "m_failedBait", "SetCatch", "ReturnBait", "GetOwner",
               "Fish-Easily", "valheim.fisheasily"]:
    # attribute arguments land in the blob heap as UTF-8, string literals in the
    # user-string heap as UTF-16LE; check both before calling a name missing.
    ok = needle.encode("utf-8") in blob or needle.encode("utf-16-le") in blob
    print(f"   {'OK  ' if ok else 'MISS'} {needle}")
    if not ok:
        failures.append(f"binary: {needle}")

print("\n== 3. no game assemblies leaked into the build output")
strays = [f for f in os.listdir(os.path.join(HERE, "bin", "Release"))
          if f.lower() not in ("fisheasily.dll",)]
for f in strays:
    print(f"   LEAK {f}")
    failures.append(f"stray output: {f}")
if not strays:
    print("   OK   only FishEasily.dll")

print("\n== 4. package files present")
contents = ["manifest.json", "icon.png", "README.md", "CHANGELOG.md"]
for c in contents:
    ok = os.path.exists(os.path.join(HERE, c))
    print(f"   {'OK  ' if ok else 'MISS'} {c}")
    if not ok:
        failures.append(f"package file: {c}")

if failures:
    print("\nFAILED:")
    for f in failures:
        print("  -", f)
    sys.exit(1)

print("\n== 5. packaging")
manifest = json.load(open(os.path.join(HERE, "manifest.json"), encoding="utf-8"))
version = manifest["version_number"]
dist = os.path.join(HERE, "dist")
os.makedirs(dist, exist_ok=True)
out = os.path.join(dist, f"{manifest['name']}-{version}.zip")

with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for c in contents:
        z.write(os.path.join(HERE, c), c)
    z.write(DLL, "FishEasily.dll")

with zipfile.ZipFile(out) as z:
    print(f"   wrote {out}")
    for i in z.infolist():
        print(f"     {i.filename:20s} {i.file_size} bytes")
print("\nOK")
