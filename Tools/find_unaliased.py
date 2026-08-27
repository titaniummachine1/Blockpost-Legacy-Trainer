"""Find game-logic classes in the dump that aren't yet aliased.
Focus on classes with readable names (non-obfuscated) that have game-relevant
fields/methods."""
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

# Find all class/struct definitions
class_pattern = re.compile(
    r'(?:(public|internal|private|protected)\s+)?'
    r'(?:sealed\s+|static\s+|abstract\s+)*'
    r'(class|struct)\s+(\S+)'
    r'(?:\s*:\s*([^{]+?))?'
    r'\s*//\s*TypeDefIndex:\s*(\d+)',
    re.MULTILINE,
)

# Obfuscated name pattern: all uppercase letters, mixed case with no vowels, etc.
# A readable name has lowercase letters and follows C# naming conventions
def is_readable(name):
    if len(name) < 3:
        return False
    # All uppercase = likely obfuscated or const
    if name == name.upper():
        return False
    # Has lowercase letters and follows PascalCase or camelCase
    has_lower = any(c.islower() for c in name)
    has_upper = any(c.isupper() for c in name)
    if not has_lower:
        return False
    # Filter out Unity/System types
    skip_prefixes = ('Unity', 'System', 'UnityEngine', 'TMPro', 'Steamworks',
                     'Ampify', 'ReorderableList', 'UnityEngineInternal',
                     'Microsoft', 'Mono.', 'Mono.Security', 'X509',
                     'System.Net', 'System.Security', 'System.IO',
                     'System.Text', 'System.Collections', 'System.Reflection',
                     'System.Runtime', 'System.Globalization', 'System.Diagnostics',
                     'System.Threading', 'System.Resources')
    for p in skip_prefixes:
        if name.startswith(p):
            return False
    return True

# Game-relevant readable classes not yet aliased
candidates = []
for m in class_pattern.finditer(text):
    cls_name = m.group(3)
    tdi = int(m.group(5))

    if cls_name in aliased:
        continue
    if not is_readable(cls_name):
        continue

    # Get the class block to count fields/methods
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

    # Count fields and methods
    field_count = len(re.findall(r'^\s+\w[\w\[\]<>, ]*\s+\w+;\s*//\s*0x', block, re.MULTILINE))
    method_count = len(re.findall(r'// RVA:', block))

    if field_count + method_count > 3:  # Skip trivial classes
        candidates.append((cls_name, tdi, field_count, method_count))

# Sort by total members (fields + methods)
candidates.sort(key=lambda x: -(x[2] + x[3]))

print(f"Found {len(candidates)} readable unaliased classes with >3 members")
print(f"\nTop 50 candidates:")
for cls, tdi, fields, methods in candidates[:50]:
    print(f"  {cls}: tdi={tdi}, {fields} fields, {methods} methods")

# Save full list
output_path = ROOT / "Tools" / "unaliased_candidates.json"
with open(output_path, "w", encoding="utf-8") as f:
    json.dump(
        [{"class": c, "tdi": t, "fields": f, "methods": m}
         for c, t, f, m in candidates],
        f, indent=2,
    )
print(f"\nFull list saved to {output_path}")
