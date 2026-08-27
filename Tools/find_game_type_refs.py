"""Find game-specific MonoBehaviour classes with game-relevant fields.
Look for classes that reference game types like KBBBHJDINCB, CGJPBNDDPIN, etc."""
import re
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DUMP = ROOT / ".tools" / "Il2CppDumper" / "dump.cs"
ALIASES = ROOT / "Tools" / "sdk_aliases.json"

with open(DUMP, encoding="utf-8") as f:
    text = f.read()
with open(ALIASES, encoding="utf-8") as f:
    aliased = json.load(f)

# Game type references that indicate a class is game-relevant
GAME_TYPES = {
    "KBBBHJDINCB", "CGJPBNDDPIN", "NAHLLMJMOED", "FPNENMKEFBB",
    "Controll", "PLH", "Client", "MasterClient", "NET",
    "GUIInv", "GUIOptions", "Movement", "MouseLook",
    "MChar", "MCharAnimator", "HUD", "UIHUD", "UIAmmo",
    "DMHBMAAFCFJ", "BIMFEOACIDM",
}

class_pattern = re.compile(
    r'(?:(public|internal|private|protected)\s+)?'
    r'(?:sealed\s+|static\s+|abstract\s+)*'
    r'(class|struct)\s+(\S+)'
    r'(?:\s*:\s*([^{]+?))?'
    r'\s*//\s*TypeDefIndex:\s*(\d+)',
    re.MULTILINE,
)

candidates = []
for m in class_pattern.finditer(text):
    cls_name = m.group(3)
    tdi = int(m.group(5))
    base = m.group(4) or ""

    if cls_name in aliased:
        continue

    block_start = text.find("{", m.end())
    if block_start < 0:
        continue
    depth = 0
    i = block_start
    while i < len(text):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                break
        i += 1
    block = text[m.start() : i + 1]

    # Check if this class references game types
    game_refs = sum(1 for gt in GAME_TYPES if gt in block)
    if game_refs == 0:
        continue

    # Count fields and methods
    field_count = len(re.findall(r'^\s+\w[\w\[\]<>, ]*\s+\w+;\s*//\s*0x', block, re.MULTILINE))
    method_count = len(re.findall(r'// RVA:', block))

    candidates.append((cls_name, tdi, field_count, method_count, game_refs, base.strip()[:60]))

candidates.sort(key=lambda x: (-x[4], -(x[2] + x[3])))

print(f"Found {len(candidates)} classes referencing game types")
print(f"\nTop 60:")
for cls, tdi, fields, methods, refs, base in candidates[:60]:
    print(f"  {cls} (tdi={tdi}): {fields}f, {methods}m, {refs} game refs, base={base}")

output_path = ROOT / "Tools" / "game_type_candidates.json"
with open(output_path, "w", encoding="utf-8") as f:
    json.dump(
        [{"class": c, "tdi": t, "fields": f, "methods": m, "game_refs": r, "base": b}
         for c, t, f, m, r, b in candidates],
        f, indent=2,
    )
print(f"\nFull list: {output_path}")
