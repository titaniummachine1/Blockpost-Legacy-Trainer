"""Find obfuscated classes with many fields that aren't yet aliased.
These are likely game data structures worth mapping."""
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

class_pattern = re.compile(
    r'(?:(public|internal|private|protected)\s+)?'
    r'(?:sealed\s+|static\s+|abstract\s+)*'
    r'(class|struct)\s+(\S+)'
    r'(?:\s*:\s*([^{]+?))?'
    r'\s*//\s*TypeDefIndex:\s*(\d+)',
    re.MULTILINE,
)

# Obfuscated name: all uppercase, length >= 8, no lowercase
def is_obfuscated(name):
    if len(name) < 8:
        return False
    return name == name.upper() and any(c.isalpha() for c in name)

candidates = []
for m in class_pattern.finditer(text):
    cls_name = m.group(3)
    tdi = int(m.group(5))
    base = (m.group(4) or "").strip()

    if cls_name in aliased:
        continue
    if not is_obfuscated(cls_name):
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

    field_count = len(re.findall(r'^\s+\w[\w\[\]<>, ]*\s+\w+;\s*//\s*0x', block, re.MULTILINE))
    method_count = len(re.findall(r'// RVA:', block))

    if field_count >= 5:  # Only classes with 5+ fields
        # Extract field types for classification
        fields_info = re.findall(r'^\s+(\w[\w\[\]<>, ]*)\s+(\w+);\s*//\s*(0x\w+)', block, re.MULTILINE)
        field_types = set(f[0].strip() for f in fields_info)
        candidates.append((cls_name, tdi, field_count, method_count, base[:50], sorted(field_types)[:5]))

candidates.sort(key=lambda x: -x[2])

print(f"Found {len(candidates)} obfuscated unaliased classes with 5+ fields")
print(f"\nTop 60 by field count:")
for cls, tdi, fields, methods, base, ftypes in candidates[:60]:
    print(f"  {cls} (tdi={tdi}): {fields}f, {methods}m, base={base}")
    print(f"    field types: {', '.join(ftypes)}")

output_path = ROOT / "Tools" / "obf_class_candidates.json"
with open(output_path, "w", encoding="utf-8") as f:
    json.dump(
        [{"class": c, "tdi": t, "fields": f, "methods": m, "base": b, "field_types": ft}
         for c, t, f, m, b, ft in candidates],
        f, indent=2,
    )
print(f"\nFull list: {output_path}")
