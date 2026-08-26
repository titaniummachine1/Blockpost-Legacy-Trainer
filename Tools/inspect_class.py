#!/usr/bin/env python3
"""Quick inspection of key classes from the type database."""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
db = json.loads((ROOT / "Tools" / "analysis" / "type_database.json").read_text(encoding="utf-8"))

classes = sys.argv[1:] if len(sys.argv) > 1 else ["Controll", "KBBBHJDINCB", "PLH", "Client", "NET", "Movement", "MouseLook", "Shooter"]

for cls in classes:
    if cls not in db:
        print(f"\n=== {cls} NOT FOUND ===")
        continue
    info = db[cls]
    print(f"\n=== {cls} ({info['kind']}) ===")
    print(f"  Bases: {info['bases']}")
    print(f"  Fields: {info['field_count']}, Properties: {info['property_count']}, Methods: {info['method_count']}")

    print(f"  --- Fields ---")
    for f in info["fields"][:40]:
        offset = hex(f["offset"]) if f["offset"] is not None else "?"
        static = "static " if f["static"] else ""
        print(f"    {offset:8s} {static}{f['type']:30s} {f['name']}")
    if len(info["fields"]) > 40:
        print(f"    ... and {len(info['fields']) - 40} more")

    print(f"  --- Properties ---")
    for p in info["properties"][:20]:
        gs = "/".join(filter(None, ["get" if p["get"] else "", "set" if p["set"] else ""])) or "?"
        print(f"    {p['type']:30s} {p['name']} {{{gs}}}")

    print(f"  --- Methods (first 40) ---")
    for m in info["methods"][:40]:
        ret = m.get("return_type", "void")
        print(f"    {hex(m['va'])} {ret:20s} {m['name']}({m['args']})")
    if len(info["methods"]) > 40:
        print(f"    ... and {len(info['methods']) - 40} more")
