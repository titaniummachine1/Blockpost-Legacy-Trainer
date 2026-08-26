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
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ALIASES = ROOT / "Tools" / "sdk_aliases.json"
SDK_DIR = ROOT / "Sdk" / "Generated"

sys.path.insert(0, str(ROOT / "Tools"))
from generate_sdk import csharp_identifier, RESERVED_CSHARP_NAMES


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


def sdk_members(safe_class: str) -> dict[str, set[str]]:
    """Extract all public const member names from a generated .cs file."""
    file = SDK_DIR / f"{safe_class}.cs"
    if not file.exists():
        return {}
    text = file.read_text(encoding="utf-8")
    members = {
        "field": set(re.findall(r"public const int (\w+)", text)),
        "method": set(re.findall(r"public const uint (\w+)", text)),
        "property": set(re.findall(r"public const string (\w+)", text)),
    }
    return members


def exists_in_sdk(safe_class: str, name: str, kind: str) -> bool:
    """Check whether a target member exists in the generated SDK, accounting for reserved-name suffixes."""
    members = sdk_members(safe_class)
    c = csharp_identifier(name)
    if c in members.get(kind, set()):
        return True
    if c in RESERVED_CSHARP_NAMES and f"{c}_" in members.get(kind, set()):
        return True
    return False


def _overload_index(safe_h: str, safe_o: str) -> int | None:
    """If the alias name encodes an overload index, return it; otherwise None."""
    if safe_h == safe_o:
        return 0
    if safe_h.startswith(f"{safe_o}_"):
        suffix = safe_h[len(safe_o) + 1 :]
        if suffix.isdigit():
            return int(suffix)
    return None


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

        # Methods can be overloaded. For non-overloaded methods any matching alias
        # is fine. For overloaded methods we keep aliases whose human name encodes
        # a valid overload index (Foo -> 0, Foo_1 -> 1, ...) and then fill any
        # remaining overload slots with the other aliases in JSON order.
        method_overloads = {}
        method_plan = []  # (human, orig, safe_h, safe_o, overload_count)
        for h, o in methods.items():
            safe_h = csharp_identifier(h)
            safe_o = csharp_identifier(o)
            if (SDK_DIR / f"{safe}.cs").exists():
                ok = exists_in_sdk(safe, o, "method")
            else:
                ok = exists_in_database(info, o, "method")
            if not ok:
                pruned_targets += 1
                continue
            # Count overloads for this original method.
            overload_count = method_overloads.get(safe_o)
            if overload_count is None:
                overload_count = sum(
                    1 for m in info.get("methods", []) if csharp_identifier(m["name"]) == safe_o
                )
                method_overloads[safe_o] = overload_count

            idx = _overload_index(safe_h, safe_o) if overload_count > 1 else None
            method_plan.append((h, o, safe_h, safe_o, overload_count, idx))

        # Decide which aliases to keep per method target.
        kept: set[tuple[str, str]] = set()  # (human, safe_o)
        by_target: dict[str, list[tuple[str, str, str, int, int | None]]] = {}
        for h, o, safe_h, safe_o, overload_count, idx in method_plan:
            by_target.setdefault(safe_o, []).append((h, safe_h, overload_count, idx))

        for safe_o, items in by_target.items():
            overload_count = items[0][2]
            if overload_count == 1:
                for h, _, _, _ in items:
                    kept.add((h, safe_o))
                continue

            used_indices: set[int] = set()
            # First, claim slots for aliases that explicitly name a valid overload.
            for h, safe_h, _, idx in items:
                if idx is not None and idx < overload_count and idx not in used_indices:
                    kept.add((h, safe_o))
                    used_indices.add(idx)

            # Then fill any remaining overload slots with the remaining aliases.
            remaining = overload_count - len(used_indices)
            for h, safe_h, _, idx in items:
                if idx is None and remaining > 0 and (h, safe_o) not in kept:
                    kept.add((h, safe_o))
                    remaining -= 1

        for h, o, _, safe_o, _, _ in method_plan:
            if (h, safe_o) in kept:
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
