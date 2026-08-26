#!/usr/bin/env python3
"""Search the type database and alias map from the command line.

Usage:
    python Tools/sdk_search.py <query>

Searches are case-insensitive and match any part of the class, field, or
method name. Output is a concise table for reverse engineering.
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DB = ROOT / "Tools" / "analysis" / "type_database.json"
ALIASES = ROOT / "Tools" / "sdk_aliases.json"


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def print_match(class_name: str, db: dict, aliases: dict) -> None:
    info = db[class_name]
    alias = aliases.get(class_name, {})
    human = alias.get("HumanClass", class_name)
    print(f"\n[class] {class_name}  ->  {human}  (tdi={info.get('line', 0)} kind={info['kind']})")
    for f in info.get("fields", [])[:10]:
        if f["offset"] is None:
            continue
        print(f"  [field] {f['name']:<24} {hex(f['offset']):<12} {f['type']}")
    for m in info.get("methods", [])[:10]:
        args = ", ".join(f"{a['type']} {a['name']}" for a in m.get("parsed_args", []))
        print(f"  [method] {m['name']:<24} {m.get('return_type', 'void')} {m['name']}({args})")


def main() -> int:
    if len(sys.argv) < 2:
        print(f"Usage: python Tools/sdk_search.py <query>")
        return 1
    query = sys.argv[1].lower()
    db = load_json(DB)
    aliases = load_json(ALIASES)
    found = 0
    for class_name in sorted(db):
        if query in class_name.lower():
            print_match(class_name, db, aliases)
            found += 1
        else:
            # Check aliases/human names
            human = aliases.get(class_name, {}).get("HumanClass", "")
            if query in human.lower():
                print_match(class_name, db, aliases)
                found += 1
    if not found:
        print(f"No matches for '{query}'")
        return 1
    print(f"\n{found} class(es) matched.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
