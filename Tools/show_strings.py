"""Show extracted string cross-reference data."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
with open(ROOT / "Tools" / "string_crossref.json", encoding="utf-8") as f:
    data = json.load(f)

print("=== URLs ===")
for u in data["urls"]:
    print(f"  {u}")

print("\n=== PlayerPrefs keys ===")
for k in data["playerprefs_keys"]:
    print(f"  {k}")

print(f"\n=== Classes with const strings ({data['total_classes_with_strings']}) ===")
for cls, info in sorted(data["classes_with_strings"].items()):
    strings = info["const_strings"]
    print(f"\n{cls} ({len(strings)} strings):")
    for s in strings[:8]:
        print(f'  {s["obf_field"]} = "{s["value"]}"')
    if len(strings) > 8:
        print(f"  ... +{len(strings)-8} more")
