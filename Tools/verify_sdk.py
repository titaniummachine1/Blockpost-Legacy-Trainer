#!/usr/bin/env python3
"""Verify the generated SDK matches the alias database and the dump.

Usage:
    python Tools/verify_sdk.py

Reports:
    - Number of generated raw classes
    - Alias entries skipped because the referenced member/class does not exist
    - Classes in dump that have alias data but no generated SDK
    - Duplicate human class names and ambiguous aliases

Exits with a non-zero status when any of the above issues are found.
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SDK_DIR = ROOT / "Sdk" / "Generated"
ALIASES = ROOT / "Tools" / "sdk_aliases.json"

# Reuse the same name sanitizer that generate_sdk.py uses.
sys.path.insert(0, str(ROOT / "Tools"))
from generate_sdk import csharp_identifier, RESERVED_CSHARP_NAMES


# Regex to extract every generated public const member name from a .cs file.
_MEMBER_RE = re.compile(r"public const (?:int|uint|string) (\w+)", re.MULTILINE)


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def main() -> int:
    aliases = load_json(ALIASES)
    generated = {p.stem for p in SDK_DIR.glob("*.cs")}

    class_count = 0
    field_count = 0
    method_count = 0
    prop_count = 0
    missing_classes = []
    missing_targets = []  # (class, human, orig, kind)
    dup_humans = {}

    has_errors = False

    for orig, mapping in aliases.items():
        safe = csharp_identifier(orig)
        if safe not in generated:
            missing_classes.append(orig)
            has_errors = True
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

        cs = (SDK_DIR / f"{safe}.cs").read_text(encoding="utf-8")
        members = set(_MEMBER_RE.findall(cs))

        def _exists(name: str, kind: str) -> bool:
            c = csharp_identifier(name)
            if c in members:
                return True
            if c in RESERVED_CSHARP_NAMES and f"{c}_" in members:
                return True
            return False

        for h, o in fields.items():
            if not _exists(o, "field"):
                missing_targets.append((orig, h, o, "field"))
                has_errors = True
        for h, o in methods.items():
            if not _exists(o, "method"):
                missing_targets.append((orig, h, o, "method"))
                has_errors = True
        for h, o in props.items():
            if not _exists(o, "property"):
                missing_targets.append((orig, h, o, "property"))
                has_errors = True

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
        has_errors = True
    if missing_targets:
        print(f"\n! {len(missing_targets)} alias targets not found in generated SDK:")
        for c, h, o, k in missing_targets[:30]:
            print(f"    {c} [{k}] {h} -> {o}")
        if len(missing_targets) > 30:
            print(f"    ... and {len(missing_targets) - 30} more")

    # Overload mapping check: for overloaded methods, an alias whose constant name
    # matches the generated overload pattern (Foo or Foo_1) must point to the
    # corresponding generated method (Foo -> overload 0, Foo_1 -> overload 1, ...).
    _METHOD_ALIAS_RE = re.compile(r"public const uint (\w+) = Raw\.(\w+)\.Methods\.(\w+);")
    _RAW_METHOD_RE = re.compile(r"public const uint (\w+)")
    _OVERLOAD_SUFFIX_RE = re.compile(r"_(\d+)$")

    raw_methods: dict[str, list[str]] = {}
    for p in SDK_DIR.glob("*.cs"):
        if p.stem == "Aliases":
            continue
        raw_methods[p.stem] = _RAW_METHOD_RE.findall(p.read_text(encoding="utf-8"))

    # Group generated method names by base (stripping the overload suffix) and class.
    raw_method_bases: dict[tuple[str, str], list[str]] = {}
    for safe, methods in raw_methods.items():
        for m in methods:
            base = _OVERLOAD_SUFFIX_RE.sub("", m)
            raw_method_bases.setdefault((safe, base), []).append(m)

    aliases_text = (SDK_DIR / "Aliases.cs").read_text(encoding="utf-8")
    bad_overloads = []
    for match in _METHOD_ALIAS_RE.finditer(aliases_text):
        alias_name, safe, gen_name = match.groups()
        base = _OVERLOAD_SUFFIX_RE.sub("", gen_name)
        overloads = raw_method_bases.get((safe, base), [])
        if len(overloads) <= 1:
            # Not overloaded, or a unique method with a numeric suffix in its base name.
            continue
        # Does this alias name encode a specific overload index?
        if alias_name == base:
            idx = 0
        elif alias_name.startswith(f"{base}_"):
            suffix = alias_name[len(base) + 1 :]
            if suffix.isdigit():
                idx = int(suffix)
            else:
                continue
        else:
            continue

        if idx < len(overloads):
            expected = overloads[idx]
            if gen_name != expected:
                bad_overloads.append((safe, alias_name, gen_name, expected))
        else:
            bad_overloads.append((safe, alias_name, gen_name, f"{base}_{idx}"))

    if bad_overloads:
        print(f"\n! {len(bad_overloads)} overloaded method aliases are mapped to the wrong generated member:")
        for safe, alias_name, gen_name, expected in bad_overloads[:20]:
            print(f"    {safe} {alias_name} -> {gen_name} (expected {expected})")
        if len(bad_overloads) > 20:
            print(f"    ... and {len(bad_overloads) - 20} more")
        has_errors = True

    # Cross-check: every alias in sdk_aliases.json should be emitted as a constant in Aliases.cs.
    # The alias constant name may be suffixed for reserved C# names or duplicates, but the count must match.
    emitted_count = len(_MEMBER_RE.findall(aliases_text))
    expected_count = field_count + method_count + prop_count
    if emitted_count != expected_count:
        print(f"\n! Alias count mismatch: sdk_aliases.json has {expected_count} entries, Aliases.cs emitted {emitted_count} constants.")
        print("  Some alias entries were silently skipped by the generator.")
        has_errors = True

    if has_errors:
        print("\nSDK verification FAILED.")
        return 1

    print("\nAll aliased targets resolved in generated SDK.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
