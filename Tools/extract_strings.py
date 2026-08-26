#!/usr/bin/env python3
"""Extract game-specific string literals from dump.cs.

Looks for strings used in:
  - PlayerPrefs.GetInt/SetInt/GetFloat/SetFloat/GetString/SetString calls
  - String literal assignments
  - Debug.Log messages
  - HTTP URLs
  - Config keys
"""
import re
import json
from pathlib import Path
from collections import defaultdict

ROOT = Path(__file__).resolve().parent.parent
DUMP_CS = ROOT / ".tools" / "Il2CppDumper" / "dump.cs"
OUT = ROOT / "Tools" / "analysis" / "game_strings.json"

# Patterns to find string usage in the dump
# Note: dump.cs has empty method bodies, so we can only find strings in:
#   - Attribute arguments: [Header("...")], [Tooltip("...")], [Obsolete("...")]
#   - Default parameter values: string x = "..."
#   - Field initializers (rare in dump)
# But the MOST useful source is string literals in the actual compiled code
# which we can't see. However, we can find:
#   - PlayerPrefs key patterns in attribute names
#   - SerializeField/SerializeField tooltips that reveal field purpose
#   - Header/Tooltip attributes that describe fields

HEADER_RE = re.compile(r'\[Header\("([^"]+)"\)\]')
TOOLTIP_RE = re.compile(r'\[Tooltip\("([^"]+)"\)\]')
RANGE_RE = re.compile(r'\[Range\(([^)]+)\)\]')
OBSOLETE_RE = re.compile(r'\[Obsolete\("([^"]+)"\)')
URL_RE = re.compile(r'"(https?://[^"]+)"')
PP_KEY_RE = re.compile(r'PlayerPrefs\.\w+\("([^"]+)"')
DEFAULT_STRING_RE = re.compile(r'=\s*"([^"]{3,})"')


def find_attribute_strings(text: str) -> dict:
    """Find strings in attributes that describe fields."""
    results = {
        "headers": [],
        "tooltips": [],
        "ranges": [],
        "obsolete": [],
        "urls": [],
        "playerprefs_keys": [],
        "default_strings": [],
    }

    for m in HEADER_RE.finditer(text):
        val = m.group(1)
        line = text[:m.start()].count("\n") + 1
        results["headers"].append({"value": val, "line": line})

    for m in TOOLTIP_RE.finditer(text):
        val = m.group(1)
        line = text[:m.start()].count("\n") + 1
        results["tooltips"].append({"value": val, "line": line})

    for m in RANGE_RE.finditer(text):
        val = m.group(1)
        line = text[:m.start()].count("\n") + 1
        results["ranges"].append({"value": val, "line": line})

    for m in OBSOLETE_RE.finditer(text):
        val = m.group(1)
        line = text[:m.start()].count("\n") + 1
        results["obsolete"].append({"value": val, "line": line})

    for m in URL_RE.finditer(text):
        val = m.group(1)
        line = text[:m.start()].count("\n") + 1
        results["urls"].append({"value": val, "line": line})

    for m in PP_KEY_RE.finditer(text):
        val = m.group(1)
        line = text[:m.start()].count("\n") + 1
        results["playerprefs_keys"].append({"value": val, "line": line})

    for m in DEFAULT_STRING_RE.finditer(text):
        val = m.group(1)
        line = text[:m.start()].count("\n") + 1
        results["default_strings"].append({"value": val, "line": line})

    return results


def find_field_descriptions(text: str) -> list[dict]:
    """Find fields with Header/Tooltip attributes that describe their purpose.
    These are the MOST useful for reverse engineering as they tell us what fields do.
    """
    results = []
    # Pattern: [Header("...")] or [Tooltip("...")] followed by field declaration
    # The attribute is on the line before the field
    lines = text.split("\n")
    i = 0
    while i < len(lines):
        line = lines[i]
        # Check for Header/Tooltip attribute
        header_m = HEADER_RE.search(line)
        tooltip_m = TOOLTIP_RE.search(line)
        if header_m or tooltip_m:
            desc = ""
            if header_m:
                desc = header_m.group(1)
            if tooltip_m:
                desc = desc + " | " + tooltip_m.group(1) if desc else tooltip_m.group(1)

            # Look at next few lines for the field declaration
            for j in range(i + 1, min(i + 5, len(lines))):
                field_line = lines[j].strip()
                if field_line.startswith("public ") or field_line.startswith("private ") or field_line.startswith("internal "):
                    # Parse field
                    field_m = re.match(
                        r'(?:public|private|internal|protected)\s+(?:static\s+)?(?:readonly\s+)?'
                        r'([\w\[\]<>.,\s]+?)\s+(\w+)\s*;\s*(?://\s*(0x[0-9A-Fa-f]+))?',
                        field_line,
                    )
                    if field_m:
                        results.append({
                            "description": desc,
                            "type": field_m.group(1).strip(),
                            "name": field_m.group(2),
                            "offset": field_m.group(3),
                            "line": i + 1,
                        })
                    break
        i += 1
    return results


def main() -> int:
    print(f"Reading {DUMP_CS} ...")
    text = DUMP_CS.read_text(encoding="utf-8", errors="ignore")

    print("Finding attribute strings ...")
    attrs = find_attribute_strings(text)
    print(f"  Headers: {len(attrs['headers'])}")
    print(f"  Tooltips: {len(attrs['tooltips'])}")
    print(f"  Ranges: {len(attrs['ranges'])}")
    print(f"  Obsolete: {len(attrs['obsolete'])}")
    print(f"  URLs: {len(attrs['urls'])}")
    print(f"  PlayerPrefs keys: {len(attrs['playerprefs_keys'])}")
    print(f"  Default strings: {len(attrs['default_strings'])}")

    print("Finding field descriptions ...")
    field_descs = find_field_descriptions(text)
    print(f"  {len(field_descs)} fields with descriptions")

    # Show some interesting field descriptions
    print("\n=== Interesting field descriptions ===")
    for fd in field_descs:
        if any(kw in fd["description"].lower() for kw in
               ["speed", "health", "ammo", "damage", "fire", "reload", "weapon",
                "player", "team", "kill", "death", "move", "jump", "sprint",
                "crouch", "aim", "sens", "fov", "camera", "recoil", "spread",
                "bullet", "projectile", "hit", "collision", "gravity", "mass"]):
            print(f"  L{fd['line']:6d} {fd['type']:20s} {fd['name']:30s} // {fd['description']}")

    print("\n=== URLs found ===")
    for u in attrs["urls"][:20]:
        print(f"  L{u['line']:6d} {u['value']}")

    print("\n=== PlayerPrefs keys ===")
    for pp in attrs["playerprefs_keys"][:20]:
        print(f"  L{pp['line']:6d} {pp['value']}")

    # Save
    output = {
        "attributes": attrs,
        "field_descriptions": field_descs,
    }
    OUT.write_text(json.dumps(output, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"\nSaved to {OUT}")
    return 0


if __name__ == "__main__":
    import sys
    sys.exit(main())
