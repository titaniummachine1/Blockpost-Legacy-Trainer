"""Find unaliased obfuscated classes that reference key game types
(Controll, KBBBHJDINCB, PLH, Client, etc.) and map them by their
reference patterns."""
import json
import re
from pathlib import Path
from collections import defaultdict

ROOT = Path(__file__).resolve().parent.parent
GRAPH = ROOT / "Tools" / "call_graph.json"
ALIASES = ROOT / "Tools" / "sdk_aliases.json"
DUMP = ROOT / ".tools" / "Il2CppDumper" / "dump.cs"

with open(GRAPH, encoding="utf-8") as f:
    graph = json.load(f)
with open(ALIASES, encoding="utf-8") as f:
    aliases = json.load(f)

# Key game types
key_types = {"Controll", "KBBBHJDINCB", "PLH", "Client", "MasterClient", "NET",
             "DMHBMAAFCFJ", "NAHLLMJMOED", "FPNENMKEFBB", "Movement", "MouseLook",
             "UIAmmo", "GUIOptions", "GUIInv", "HUD", "MChar"}

# Find obfuscated classes (all caps + mixed case, not in aliases)
aliased_classes = set(aliases.keys())
references = graph.get("references", {})

# Find classes that reference key game types
game_related = {}
for cls_name, refs in references.items():
    # Check if this class references any key game types
    game_refs = {k: v for k, v in refs.items() if k in key_types}
    if game_refs and cls_name not in aliased_classes:
        # Check if it's obfuscated (no readable name)
        is_obf = bool(re.match(r'^[A-Z]{5,}[A-Z]+$', cls_name))
        game_related[cls_name] = {
            "refs": game_refs,
            "total_refs": sum(game_refs.values()),
            "is_obf": is_obf,
            "method_count": graph["classes"].get(cls_name, {}).get("method_count", 0),
            "field_count": graph["classes"].get(cls_name, {}).get("field_count", 0),
        }

# Sort by total references to game types
sorted_related = sorted(game_related.items(), key=lambda x: -x[1]["total_refs"])

print(f"Found {len(sorted_related)} unaliased classes referencing key game types:")
print()
for name, info in sorted_related[:30]:
    obf_tag = " [OBF]" if info["is_obf"] else ""
    ref_str = ", ".join(f"{k}={v}" for k, v in info["refs"].items())
    print(f"  {name}{obf_tag} ({info['method_count']}m, {info['field_count']}f) -> {ref_str}")

# Also find classes that are referenced BY key game types
reverse_refs = defaultdict(list)
for cls_name, refs in references.items():
    for target in refs:
        if target in key_types:
            reverse_refs[target].append(cls_name)

print("\n\nClasses referenced BY key game types:")
for key_type in ["Controll", "KBBBHJDINCB", "PLH", "Client"]:
    refs_to = reverse_refs.get(key_type, [])
    unaliased_refs = [r for r in refs_to if r not in aliased_classes]
    if unaliased_refs:
        print(f"\n  {key_type} is referenced by {len(unaliased_refs)} unaliased classes:")
        for r in unaliased_refs[:10]:
            print(f"    {r}")
