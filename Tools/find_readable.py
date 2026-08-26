#!/usr/bin/env python3
"""Find all classes with human-readable field names (non-obfuscated).

The game has a mix of obfuscated (e.g. KBBBHJDINCB) and clear names (e.g. _ammo).
Classes with clear names are usually UI or Unity-engine-facing code that
wasn't run through the obfuscator. These are gold for reverse engineering
because they tell us what fields/methods actually do.
"""
import re
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DUMP_CS = ROOT / ".tools" / "Il2CppDumper" / "dump.cs"
OUT = ROOT / "Tools" / "analysis" / "readable_classes.json"

# Read dump
text = DUMP_CS.read_text(encoding="utf-8", errors="ignore")

# Find all class/struct/enum definitions
TYPE_DEF_RE = re.compile(
    r"^(?P<attrs>(?:\[[^\]]+\]\s*)*)"
    r"(?P<mods>(?:internal|public|private|protected|sealed|abstract|static|partial)\s+)*"
    r"(?P<kind>class|enum|struct|interface)\s+"
    r"(?P<name>[\w.<>]+)"
    r"(?:\s*:\s*(?P<bases>[\w\.,\s<>]+?))?"
    r"\s*(?://.*)?$",
    re.MULTILINE,
)

FIELD_RE = re.compile(
    r"^\s*(?P<attrs>(?:\[[^\]]+\]\s*)*)"
    r"(?P<modifiers>(?:internal|private|public|protected|static|readonly|const|volatile)\s+)*"
    r"(?P<type>[\w\[\]<>.,\s]+?)\s+"
    r"(?P<name>[\w<>.]+)\s*(?:=\s*(?P<default>[^;]+))?;\s*"
    r"(?://\s*(?P<offset>0x[0-9A-Fa-f]+))?\s*$",
    re.MULTILINE,
)

# A name is "readable" if it contains lowercase letters and isn't all-caps gibberish
def is_readable_name(name: str) -> bool:
    if not name:
        return False
    # Obfuscated names are typically all uppercase, 8+ chars, no underscores
    if name.isupper() and len(name) >= 6 and "_" not in name:
        return False
    # Unity-generated backing fields
    if name.startswith("<") and name.endswith(">k__BackingField"):
        return False
    # Has lowercase = probably readable
    if any(c.islower() for c in name):
        return True
    # Short all-caps names like "HUD", "NET", "PLH" - check if they're known
    return False


def brace_count(text: str) -> tuple[str | None, int]:
    depth = 1
    i = 0
    while i < len(text) and depth > 0:
        ch = text[i]
        if ch == "'" or ch == '"':
            quote = ch
            i += 1
            while i < len(text):
                if text[i] == "\\":
                    i += 2
                    continue
                if text[i] == quote:
                    i += 1
                    break
                i += 1
            continue
        if ch == "/" and i + 1 < len(text) and text[i + 1] == "/":
            end = text.find("\n", i)
            if end == -1:
                break
            i = end + 1
            continue
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
        i += 1
    if depth == 0:
        return text[:i - 1], i
    return None, 0


readable_classes = []

for m in TYPE_DEF_RE.finditer(text):
    name = m.group("name").strip()
    kind = m.group("kind")

    after = text[m.end():]
    brace_match = re.search(r"^\s*\{\s*$", after, re.MULTILINE)
    if not brace_match:
        continue
    body_start = m.end() + brace_match.end()
    body, _ = brace_count(text[body_start:])
    if body is None:
        continue

    # Parse fields
    fields = []
    for fm in FIELD_RE.finditer(body):
        fname = fm.group("name").strip()
        ftype = fm.group("type").strip()
        foffset = fm.group("offset")
        mods = (fm.group("modifiers") or "").split()
        fields.append({
            "name": fname,
            "type": ftype,
            "offset": foffset,
            "static": "static" in mods,
            "readable": is_readable_name(fname),
        })

    # Check if class has readable fields
    readable_fields = [f for f in fields if f["readable"]]
    if not readable_fields:
        continue

    # Also check if class name itself is readable
    class_readable = is_readable_name(name) or name in (
        "Controll", "Movement", "MouseLook", "Shooter", "Spectator",
        "Crosshair", "Radar", "Following", "FreeFlyCamera", "HUD",
        "Client", "MasterClient", "NET", "DropClient", "Main",
        "MainManager", "UIManager", "SteamManager", "MapLoader",
        "MapGenerator", "MapAutoload", "MapEvent", "MapPrefab",
        "MapCulling", "VoxelMap", "VoxelBattleMap", "VoxelMapLight",
        "FXBloodSplat", "FXTracer", "GeneralCameraShake", "OutlineSystem",
        "MChar", "MCharAnimator", "Util", "Util2", "UtilHash", "UtilChar",
        "Log", "DevDraw", "dbgNet", "DemoRec", "ConsoleBase", "Console",
        "Lang", "LangWeapon", "VInput", "InputHelper", "ControllTouch",
        "FileSender", "HitData", "GUIInv", "GUIMap", "GUIPlay", "GUIM",
        "GUIMMain", "GUIMPlay", "GUIAdmin", "GUIAdminMaplist",
        "GUIAdminUpload", "GUIAdminSettings", "GUIAdminPlayers",
        "GUIChar", "GUICharEditor", "GUISkinEditor", "GUIGameSet",
        "GUIGameMenu", "GUIGameSquad", "GUICraft", "GUICase", "GUIClan",
        "GUIIcon", "GUIShop", "GUIOptions", "GUIObj", "GUIName",
        "GUIFX", "GUIProfile", "GUIRank", "GUIGold", "GUIBonus",
        "GUI3D", "VoxelPaletteGUI", "UIChatMessage", "UIHUD",
        "UIMPlay", "UIMMainmenu", "UIMInventory", "UIMPlaymode",
        "UIMShop", "UIMTasks", "UIMReward", "UIDrop", "UIDropButton",
        "UIDropButtonExit", "UIElementBase", "UIColors", "UIPalette",
        "UIPaletteColorPreview", "HUDMessage", "UIDeathMessage",
        "HUDBuild", "HUDGameEnd", "HUDTab", "HUDNames", "HUDKiller",
        "HUDIndicator", "MainConfig", "MainBack", "ParticleManager",
        "CharAnimator", "SoundLoader", "FXScale", "FXScaleCase",
        "FadeLight", "VoxelMapSceneItem", "VoxelMultList", "VoxelGreedy",
        "VoxelPalette", "VoxelAtlas", "VoxelRadiusUpdate", "VCGen",
        "VWGen", "VWGen2", "VWIK", "VWPos", "VDestr", "MapOcc",
        "BPPMap", "TMap", "HMap", "OMap", "MapLayerGenerator",
        "GrassGenerator", "TreeGenerator", "MapLightBaker", "MapUpload",
        "SaveMap",
    )

    readable_classes.append({
        "name": name,
        "kind": kind,
        "class_readable": class_readable,
        "readable_field_count": len(readable_fields),
        "total_field_count": len(fields),
        "readable_fields": [
            {"name": f["name"], "type": f["type"], "offset": f["offset"], "static": f["static"]}
            for f in readable_fields
        ],
    })

# Sort by number of readable fields
readable_classes.sort(key=lambda c: c["readable_field_count"], reverse=True)

print(f"Found {len(readable_classes)} classes with readable field names")
print(f"\n=== Top 30 classes by readable field count ===")
for c in readable_classes[:30]:
    print(f"  {c['name']:40s} ({c['readable_field_count']:3d}/{c['total_field_count']:3d} readable)")
    for f in c["readable_fields"][:5]:
        print(f"    {f['offset'] or '    ':6s} {f['type']:25s} {f['name']}")
    if len(c["readable_fields"]) > 5:
        print(f"    ... and {len(c['readable_fields']) - 5} more")

OUT.write_text(json.dumps(readable_classes, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
print(f"\nSaved to {OUT}")
