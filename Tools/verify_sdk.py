#!/usr/bin/env python3
"""Verify the generated SDK matches the alias database and the dump.

Usage:
    python Tools/verify_sdk.py

Reports:
    - Number of generated raw classes
    - Alias entries skipped because the referenced member/class does not exist
    - Classes in dump that have alias data but no generated SDK
    - Duplicate human class names and ambiguous aliases
"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SDK_DIR = ROOT / "Sdk" / "Generated"
ALIASES = ROOT / "Tools" / "sdk_aliases.json"

# Reuse the same name sanitizer that generate_sdk.py uses.
sys.path.insert(0, str(ROOT / "Tools"))
from generate_sdk import csharp_identifier


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def main() -> int:
    aliases = load_json(ALIASES)
    generated = {p.stem for p in SDK_DIR.glob("*.cs")}

    # Map safe class name to original
    safe_to_orig = {orig.replace(".", "_").replace("<", "_").replace(">", "_"): orig for orig in aliases}

    # Count alias targets
    class_count = 0
    field_count = 0
    method_count = 0
    prop_count = 0
    missing_classes = []
    missing_targets = []  # (class, human, orig, kind)
    dup_humans = {}

    for orig, mapping in aliases.items():
        safe = orig.replace(".", "_").replace("<", "_").replace(">", "_")
        if safe not in generated:
            missing_classes.append(orig)
            continue
        class_count += 1

        human = mapping.get("HumanClass", orig)
        dup_humans[human] = dup_humans.get(human, 0) + 1

        fields = mapping.get("Fields", {})
        methods = mapping.get("Methods", {})
        props = mapping.get("Properties", {})

        field_count += len(fields)
        method_count += len(methods)
        prop_count += len(props)

        # Resolve missing file members at runtime by loading generated file as text.
        cs = (SDK_DIR / f"{safe}.cs").read_text(encoding="utf-8")

        def _exists(name: str, kind: str) -> bool:
            c = csharp_identifier(name)
            if kind == "field":
                return f"public const int {c}" in cs
            if kind == "method":
                return f"public const uint {c}" in cs
            return f"public const string {c}" in cs

        for h, o in fields.items():
            if not _exists(o, "field"):
                missing_targets.append((orig, h, o, "field"))
        for h, o in methods.items():
            if not _exists(o, "method"):
                missing_targets.append((orig, h, o, "method"))
        for h, o in props.items():
            if not _exists(o, "property"):
                missing_targets.append((orig, h, o, "property"))

    print("=== SDK Verification ===")
    print(f"Generated .cs files: {len(generated)}")
    print(f"Aliased classes in sdk_aliases.json: {len(aliases)}")
    print(f"Classes with a generated SDK: {class_count}")
    print(f"  Fields: {field_count}")
    print(f"  Methods: {method_count}")
    print(f"  Properties: {prop_count}")
    if missing_classes:
        print(f"\n! {len(missing_classes)} classes in sdk_aliases.json have no generated SDK:")
        for c in missing_classes[:20]:
            print(f"    {c}")
        if len(missing_classes) > 20:
            print(f"    ... and {len(missing_classes) - 20} more")
    duplicate_humans = {h: n for h, n in dup_humans.items() if n > 1}
    if duplicate_humans:
        print(f"\n! Duplicate HumanClass names (resolved with _N suffix in Aliases.cs): {len(duplicate_humans)}")
        for h, n in duplicate_humans.items():
            print(f"    {h}: {n}")
    if missing_targets:
        print(f"\n! {len(missing_targets)} alias targets not found in generated SDK:")
        for c, h, o, k in missing_targets[:30]:
            print(f"    {c} [{k}] {h} -> {o}")
        if len(missing_targets) > 30:
            print(f"    ... and {len(missing_targets) - 30} more")
    else:
        print("\nAll aliased targets resolved in generated SDK.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
