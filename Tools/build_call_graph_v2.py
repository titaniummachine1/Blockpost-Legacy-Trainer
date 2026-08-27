"""Improved call graph builder that captures ALL class definitions from dump.cs
using a more robust regex that handles all class declaration formats."""
import re
import json
from pathlib import Path
from collections import defaultdict

ROOT = Path(__file__).resolve().parent.parent
DUMP = ROOT / ".tools" / "Il2CppDumper" / "dump.cs"
OUTPUT = ROOT / "Tools" / "call_graph_v2.json"

with open(DUMP, encoding="utf-8") as f:
    text = f.read()

# More robust class matching: handles sealed, abstract, static, interfaces, structs
class_pattern = re.compile(
    r'(?:public |internal |private |protected )?'
    r'(?:sealed |abstract |static )?'
    r'(?:class |struct |interface )'
    r'(\w+)\s*'
    r'(?::\s*([^{\n]+?))?'
    r'(?:\s*//[^\n]*)?'
    r'\s*\{',
    re.MULTILINE
)

classes = {}
for m in class_pattern.finditer(text):
    name = m.group(1)
    parent = (m.group(2) or "").strip()

    # Get the class block by counting braces
    brace_start = m.end() - 1
    depth = 0
    i = brace_start
    while i < len(text):
        if text[i] == '{':
            depth += 1
        elif text[i] == '}':
            depth -= 1
            if depth == 0:
                break
        i += 1
    block = text[m.start():i+1]

    # Extract methods (with RVA comments)
    methods = re.findall(
        r'// RVA:.*?Offset:.*?VA:.*?\n\s+(?:internal |public |private |protected )?(?:static )?(\w[\w<>\[\], ]*)\s+(\w+)\(([^)]*)\)',
        block
    )
    # Extract fields
    fields = re.findall(r'(\w[\w<>\[\], ]*)\s+(\w+);\s*// (0x\w+)', block)

    classes[name] = {
        "parent": parent,
        "methods": [{"return": r.strip(), "name": n, "params": p.strip()} for r, n, p in methods],
        "fields": [{"type": t.strip(), "name": n, "offset": o} for t, n, o in fields],
        "method_count": len(methods),
        "field_count": len(fields),
    }

print(f"Parsed {len(classes)} classes (v2)")

# Build reference graph
references = defaultdict(lambda: defaultdict(int))

for cls_name, cls_data in classes.items():
    for field in cls_data["fields"]:
        ref_class = re.match(r'(\w+)', field["type"])
        if ref_class and ref_class.group(1) in classes and ref_class.group(1) != cls_name:
            references[cls_name][ref_class.group(1)] += 1

    for method in cls_data["methods"]:
        for param_type in re.findall(r'(\w+)', method["params"]):
            if param_type in classes and param_type != cls_name:
                references[cls_name][param_type] += 1
        ret_type = re.match(r'(\w+)', method["return"])
        if ret_type and ret_type.group(1) in classes and ret_type.group(1) != cls_name:
            references[cls_name][ret_type.group(1)] += 1

# Key game types
key_types = {"Controll", "KBBBHJDINCB", "PLH", "Client", "MasterClient", "NET",
             "DMHBMAAFCFJ", "NAHLLMJMOED", "FPNENMKEFBB", "Movement", "MouseLook",
             "UIAmmo", "GUIOptions", "GUIInv", "HUD", "MChar", "Spectator",
             "DemoRec", "HUDKiller", "UIScores", "UIDeathMessages"}

# Find classes referencing key game types
game_related = {}
for cls_name, refs in references.items():
    game_refs = {k: v for k, v in refs.items() if k in key_types}
    if game_refs:
        game_related[cls_name] = {
            "refs": game_refs,
            "total_refs": sum(game_refs.values()),
            "method_count": classes[cls_name]["method_count"],
            "field_count": classes[cls_name]["field_count"],
        }

# Sort by total references
sorted_related = sorted(game_related.items(), key=lambda x: -x[1]["total_refs"])

print(f"\nFound {len(sorted_related)} classes referencing key game types:")
for name, info in sorted_related[:40]:
    ref_str = ", ".join(f"{k}={v}" for k, v in info["refs"].items())
    print(f"  {name} ({info['method_count']}m, {info['field_count']}f) -> {ref_str}")

# Find reverse references (who references key types)
reverse_refs = defaultdict(list)
for cls_name, refs in references.items():
    for target in refs:
        if target in key_types:
            reverse_refs[target].append(cls_name)

print("\n\nClasses referenced BY key game types:")
for key_type in sorted(key_types):
    refs_to = reverse_refs.get(key_type, [])
    if refs_to:
        print(f"\n  {key_type} referenced by {len(refs_to)} classes:")
        for r in refs_to[:5]:
            print(f"    {r}")

# Save
output = {
    "class_count": len(classes),
    "classes": {name: {"method_count": d["method_count"], "field_count": d["field_count"], "parent": d["parent"]} for name, d in classes.items()},
    "references": {cls: dict(refs) for cls, refs in references.items()},
    "game_related": {name: info for name, info in sorted_related},
}
with open(OUTPUT, "w", encoding="utf-8") as f:
    json.dump(output, f, indent=2, ensure_ascii=False)
    f.write("\n")
print(f"\nSaved to {OUTPUT}")
