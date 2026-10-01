#!/usr/bin/env bash
#
# Fish-Easily build + package.
#
#   ./build.sh                bump the patch version, build, verify, package
#   ./build.sh minor          bump minor   (1.2.3 -> 1.3.0)
#   ./build.sh major          bump major   (1.2.3 -> 2.0.0)
#   ./build.sh --set 2.1.0    set an explicit version
#   ./build.sh --no-bump      rebuild and repackage the current version
#   ./build.sh --dry-run      show what the bump would change, write nothing
#   ./build.sh --deploy       also install into the r2modman profile
#   ./build.sh --help
#
# The current version is read from whichever file declares it, in this order:
# Plugin.cs (public const string Version, or the [BepInPlugin] argument) then
# manifest.json. The new version is written back to every file that already
# carries one, so the zip can never disagree with the assembly inside it. A file
# that does not declare a version is left alone.
#
# Artefacts land in dist/ and are copied to ~/Downloads/.
#
# --deploy skips the r2modman import: it writes the DLL straight into the profile
# (both the plugins copy and the package cache) and updates the version r2modman
# records, so the next game launch picks it up. Plain ./build.sh leaves the
# profile alone.
#
set -euo pipefail

HERE="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$HERE"

PROFILE="${FISHEASILY_PROFILE:-$HOME/.config/r2modmanPlus-local/Valheim/profiles/Default}"
R2MM="${FISHEASILY_R2MM:-$HOME/.config/r2modmanPlus-local/Valheim}"
# r2modman names a local import "<author>-FishEasily"; match whatever author was used.
PLUGIN_DIR="$(compgen -G "$PROFILE/BepInEx/plugins/*-FishEasily" | head -1 || true)"
PKG_DIR_NAME="$(basename "${PLUGIN_DIR:-unknown-FishEasily}")"
CACHE_DIR="$R2MM/cache/$PKG_DIR_NAME"
MODS_YML="$PROFILE/mods.yml"
DOWNLOADS="${FISHEASILY_DOWNLOADS:-$HOME/Downloads}"
MANAGED="${FISHEASILY_MANAGED:-$HOME/Data/SteamLibrary/steamapps/common/Valheim/valheim_Data/Managed}"
# exported so package.py reads the same directory
export FISHEASILY_REFS="${FISHEASILY_REFS:-/tmp/valheim-src}"
REFS_DIR="$FISHEASILY_REFS"

usage() {
    # print the header comment block: skip the shebang, stop at the first real line
    awk 'NR > 1 { if ($0 ~ /^#/) { sub(/^#+ ?/, ""); print; next } else { exit } }' "${BASH_SOURCE[0]}"
}

BUMP="patch"
EXPLICIT=""
DEPLOY=0
DRY=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        major|minor|patch) BUMP="$1"; shift ;;
        --no-bump)         BUMP="none"; shift ;;
        --deploy)          DEPLOY=1; shift ;;
        --dry-run)         DRY=1; shift ;;
        --set)
            EXPLICIT="${2:-}"
            if [[ -z "$EXPLICIT" ]]; then
                echo "error: --set needs a version, e.g. --set 2.1.0" >&2
                exit 2
            fi
            shift 2
            ;;
        -h|--help) usage; exit 0 ;;
        *) echo "error: unknown argument '$1'" >&2; usage >&2; exit 2 ;;
    esac
done

# ---------------------------------------------------------------- current version
CUR=""
VSRC=""

if [[ -f Plugin.cs ]]; then
    CUR="$(sed -n 's/.*public const string Version = "\([^"]*\)".*/\1/p' Plugin.cs | head -1)"
    if [[ -n "$CUR" ]]; then
        VSRC="Plugin.cs (public const string Version)"
    else
        CUR="$(sed -n 's/.*\[BepInPlugin([^]]*,\s*"[^"]*"\s*,\s*"\([^"]*\)".*/\1/p' Plugin.cs | head -1)"
        if [[ -n "$CUR" ]]; then
            VSRC="Plugin.cs ([BepInPlugin] argument)"
        fi
    fi
fi

if [[ -z "$CUR" && -f manifest.json ]]; then
    CUR="$(python3 -c "import json;print(json.load(open('manifest.json'))['version_number'])" 2>/dev/null)"
    if [[ -n "$CUR" ]]; then
        VSRC="manifest.json"
    fi
fi

if [[ -z "$CUR" ]]; then
    echo "error: no version found in Plugin.cs or manifest.json" >&2
    exit 1
fi

# ---------------------------------------------------------------- new version
if [[ -n "$EXPLICIT" ]]; then
    if [[ ! "$EXPLICIT" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
        echo "error: '$EXPLICIT' is not x.y.z" >&2
        exit 2
    fi
    NEW="$EXPLICIT"
else
    IFS=. read -r MA MI PA <<< "$CUR"
    case "$BUMP" in
        major) MA=$((MA + 1)); MI=0; PA=0 ;;
        minor) MI=$((MI + 1)); PA=0 ;;
        patch) PA=$((PA + 1)) ;;
        none)  : ;;
    esac
    NEW="$MA.$MI.$PA"
fi

echo "== version: $CUR -> $NEW   (source: $VSRC)"

if [[ "$CUR" != "$NEW" ]]; then
    NEW="$NEW" DRY="$DRY" python3 - <<'PY'
import json, os, pathlib, re

new = os.environ["NEW"]
dry = os.environ["DRY"] == "1"
lines = []


def report(name, detail):
    lines.append(f"   {name:20s} {detail}")


def write(path, text):
    if not dry:
        pathlib.Path(path).write_text(text, encoding="utf-8")


# --- Plugin.cs -------------------------------------------------------------
p = pathlib.Path("Plugin.cs")
if p.exists():
    t = p.read_text(encoding="utf-8")

    m = re.search(r'public const string Version = "([^"]+)"', t)
    if m:
        write(p, t.replace(m.group(0), f'public const string Version = "{new}"'))
        report("Plugin.cs", f"{m.group(1)} -> {new}  (Version constant)")
    else:
        m = re.search(r"\[BepInPlugin\(([^\]]*)\)\]", t)
        quoted = re.findall(r'"[^"]*"', m.group(1)) if m else []
        # [BepInPlugin(guid, name, version)]: the version is the LAST quoted string.
        # guid/name may be identifier constants, so there can be as few as two.
        if len(quoted) >= 2:
            old = quoted[-1].strip('"')
            t2 = t.replace(quoted[-1], f'"{new}"', 1) if t.count(quoted[-1]) == 1 else None
            if t2 is None:
                # the literal is ambiguous; anchor on the whole attribute instead
                inner = m.group(1)
                new_inner = inner.replace(quoted[-1], f'"{new}"', 1)
                t2 = t.replace(inner, new_inner, 1)
            write(p, t2)
            report("Plugin.cs", f"{old} -> {new}  ([BepInPlugin] argument)")
        else:
            report("Plugin.cs", "no version declared, left alone")
else:
    report("Plugin.cs", "not found, skipped")

# --- FishEasily.csproj -----------------------------------------------------
p = pathlib.Path("FishEasily.csproj")
if p.exists():
    t = p.read_text(encoding="utf-8")
    m = re.search(r"<Version>([^<]+)</Version>", t)
    if m:
        write(p, t.replace(m.group(0), f"<Version>{new}</Version>"))
        report("FishEasily.csproj", f"{m.group(1)} -> {new}")
    else:
        report("FishEasily.csproj", "no <Version>, left alone")
else:
    report("FishEasily.csproj", "not found, skipped")

# --- manifest.json ---------------------------------------------------------
p = pathlib.Path("manifest.json")
if p.exists():
    d = json.loads(p.read_text(encoding="utf-8"))
    old = d.get("version_number")
    d["version_number"] = new
    write(p, json.dumps(d, indent=4) + "\n")
    report("manifest.json", f"{old} -> {new}")
else:
    report("manifest.json", "not found, skipped")

# --- CHANGELOG.md ----------------------------------------------------------
p = pathlib.Path("CHANGELOG.md")
if p.exists():
    t = p.read_text(encoding="utf-8")
    if f"## {new}" in t:
        report("CHANGELOG.md", "section already present")
    else:
        src = t.splitlines()
        idx = next((i for i, l in enumerate(src) if l.startswith("## ")), len(src))
        src[idx:idx] = [f"## {new}", "", "- (describe this release)", ""]
        write(p, "\n".join(src).rstrip() + "\n")
        report("CHANGELOG.md", "section added (placeholder - go edit it)")
else:
    report("CHANGELOG.md", "not found, skipped")

print("\n".join(lines))
if dry:
    print("   -- dry run, nothing written --")
PY
else
    echo "   no change (rebuilding $CUR)"
fi

if [[ "$DRY" -eq 1 ]]; then
    echo
    echo "== dry run: stopping before build"
    exit 0
fi

# ---------------------------------------------------------------- build
echo "== build"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet build FishEasily.csproj -c Release -v minimal

# ---------------------------------------------------------------- verify + package
echo "== verify and package"

# package.py asserts the plugin's reflection targets against decompiled game types
# and expects those dumps to be present; regenerate them if the scratch dir is gone.
NEED_REFS=0
for t in FishingFloat Fish; do
    if [[ ! -f "$REFS_DIR/$t.cs" ]]; then
        NEED_REFS=1
    fi
done

if [[ "$NEED_REFS" -eq 1 ]]; then
    ILSPY="$(command -v ilspycmd || true)"
    if [[ -z "$ILSPY" && -x "$HOME/.dotnet/tools/ilspycmd" ]]; then
        ILSPY="$HOME/.dotnet/tools/ilspycmd"
    fi
    if [[ -z "$ILSPY" ]]; then
        echo "error: ilspycmd not found (dotnet tool install -g ilspycmd)" >&2
        exit 1
    fi
    if [[ ! -f "$MANAGED/assembly_valheim.dll" ]]; then
        echo "error: $MANAGED/assembly_valheim.dll not found" >&2
        exit 1
    fi
    mkdir -p "$REFS_DIR"
    for t in FishingFloat Fish; do
        echo "   decompiling $t -> $REFS_DIR/$t.cs"
        "$ILSPY" -t "$t" "$MANAGED/assembly_valheim.dll" > "$REFS_DIR/$t.cs"
    done
fi

python3 package.py

ZIP="dist/FishEasily-$NEW.zip"
if [[ ! -f "$ZIP" ]]; then
    echo "error: expected $ZIP was not produced" >&2
    exit 1
fi

mkdir -p "$DOWNLOADS"
cp "$ZIP" "$DOWNLOADS/"

echo
echo "== artefact"
echo "   $HERE/$ZIP"
echo "   $DOWNLOADS/$(basename "$ZIP")"
sha256sum "$ZIP" | awk '{print "   sha256 " $1}'

# ---------------------------------------------------------------- optional deploy
if [[ "$DEPLOY" -eq 1 ]]; then
    echo
    echo "== deploy into the r2modman profile"
    DLL="bin/Release/FishEasily.dll"

    if [[ -z "$PLUGIN_DIR" ]]; then
        echo "   no *-FishEasily folder in $PROFILE/BepInEx/plugins" >&2
        echo "   import the zip in r2modman once, then --deploy can keep it updated" >&2
        exit 1
    fi

    # write to a temp name and mv: safe even while the game holds the old inode
    cp "$DLL" "$PLUGIN_DIR/FishEasily.dll.new"
    mv "$PLUGIN_DIR/FishEasily.dll.new" "$PLUGIN_DIR/FishEasily.dll"
    echo "   plugins/$PKG_DIR_NAME/FishEasily.dll"

    mkdir -p "$CACHE_DIR/$NEW"
    cp "$DLL" "$CACHE_DIR/$NEW/FishEasily.dll"
    echo "   cache/$PKG_DIR_NAME/$NEW/FishEasily.dll"

    if [[ -f "$MODS_YML" ]]; then
        MODS_YML="$MODS_YML" NEW="$NEW" PKG="$PKG_DIR_NAME" python3 - <<'PY'
import os, re, pathlib

major, minor, patch = os.environ["NEW"].split(".")
pkg = os.environ["PKG"]
p = pathlib.Path(os.environ["MODS_YML"])
text = p.read_text(encoding="utf-8")
pattern = re.compile(
    r"(name: " + re.escape(pkg) + r"\n(?:.*\n)*?  versionNumber:\n)"
    r"    major: \d+\n    minor: \d+\n    patch: \d+\n"
)
block = f"    major: {major}\n    minor: {minor}\n    patch: {patch}\n"
new_text, n = pattern.subn(lambda m: m.group(1) + block, text, count=1)
if n:
    p.write_text(new_text, encoding="utf-8")
    print(f"   mods.yml versionNumber -> {major}.{minor}.{patch}")
else:
    print(f"   note: no {pkg} versionNumber block in mods.yml; r2modman will "
          "still show the previous version")
PY
    fi

    echo "   restart Valheim to load it (a running session keeps the old assembly)"
fi
