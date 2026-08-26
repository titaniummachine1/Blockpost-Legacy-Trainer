#!/usr/bin/env python3
"""Remove stale alias entries from sdk_aliases.json.

Usage:
    python Tools/prune_aliases.py

Reads `Tools/sdk_aliases.json` and `Tools/analysis/type_database.json`,
compares every field/method/property alias to the actual dump data, and
deletes entries whose target does not exist. Classes that are no longer in
the dump are also removed.
"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ALIASES = ROOT / "Tools" / "sdk_aliases.json"
SDK_DIR = ROOT / "Sdk" / "Generated"

sys.path.insert(0, str(ROOT / "Tools"))
from generate_sdk import csharp_identifier


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def exists_in_database(class_info: dict, name: str, kind: str) -> bool:
    """Return True if `name` (after C# sanitization) exists in the dump class."""
    target = csharp_identifier(name)
    key = {"field": "fields", "method": "methods", "property": "properties"}[kind]
    for item in class_info.get(key, []):
        if csharp_identifier(item["name"]) == target:
            return True
    return False


def exists_in_sdk(safe_class: str, name: str, kind: str) -> bool:
    """Check against the generated SDK file for this class."""
    file = SDK_DIR / f"{safe_class}.cs"
    if not file.exists():
        return False
    text = file.read_text(encoding="utf-8")
    c = csharp_identifier(name)
    if kind == "field":
        return f"public const int {c}" in text
    if kind == "method":
        return f"public const uint {c}" in text
    return f"public const string {c}" in text


def main() -> int:
    aliases = load_json(ALIASES)
    db = load_json(ROOT / "Tools" / "analysis" / "type_database.json")
    pruned_classes = []
    pruned_targets = 0
    result = {}

    for orig, mapping in aliases.items():
        if orig not in db:
            pruned_classes.append(orig)
            continue

        info = db[orig]
        safe = csharp_identifier(orig)

        fields = mapping.get("Fields", {})
        methods = mapping.get("Methods", {})
        props = mapping.get("Properties", {})
        notes = mapping.get("Notes", {})

        new_fields = {}
        new_methods = {}
        new_props = {}

        for h, o in fields.items():
            # Prefer the generated SDK when it exists, because short names like
            # `Console` and `Image` can map to multiple dump classes.
            if (SDK_DIR / f"{safe}.cs").exists():
                ok = exists_in_sdk(safe, o, "field")
            else:
                ok = exists_in_database(info, o, "field")
            if ok:
                new_fields[h] = o
            else:
                pruned_targets += 1

        for h, o in methods.items():
            if (SDK_DIR / f"{safe}.cs").exists():
                ok = exists_in_sdk(safe, o, "method")
            else:
                ok = exists_in_database(info, o, "method")
            if ok:
                new_methods[h] = o
            else:
                pruned_targets += 1

        for h, o in props.items():
            if (SDK_DIR / f"{safe}.cs").exists():
                ok = exists_in_sdk(safe, o, "property")
            else:
                ok = exists_in_database(info, o, "property")
            if ok:
                new_props[h] = o
            else:
                pruned_targets += 1

        # Notes that refer to removed aliases are dropped as well.
        kept_humans = set(new_fields) | set(new_methods) | set(new_props)
        new_notes = {h: v for h, v in notes.items() if h in kept_humans}

        result[orig] = {
            "HumanClass": mapping.get("HumanClass", orig),
            "Fields": new_fields,
            "Methods": new_methods,
            "Properties": new_props,
            "Notes": new_notes,
        }

    ALIASES.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    print(f"Pruned {len(pruned_classes)} classes, {pruned_targets} field/method/property aliases.")
    print(f"Remaining {len(result)} classes.")
    if pruned_classes:
        for c in pruned_classes[:20]:
            print(f"  - {c}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
