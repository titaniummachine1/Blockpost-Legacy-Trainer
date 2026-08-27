"""Show OBJ class const strings for alias mapping."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
with open(ROOT / "Tools" / "string_crossref.json", encoding="utf-8") as f:
    data = json.load(f)

for cls, info in data["classes_with_strings"].items():
    if cls in ("OBJ", "Constants", "VKContants", "VKSettings", "Version", "NativeMethods", "GPGSIds"):
        print(f"\n{cls} const strings:")
        for s in info["const_strings"]:
            print(f'  {s["obf_field"]} = "{s["value"]}"')
