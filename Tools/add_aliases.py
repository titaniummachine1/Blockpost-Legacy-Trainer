#!/usr/bin/env python3
"""Add or update curated class aliases in sdk_aliases.json.

Usage:
    python Tools/add_aliases.py

This script merges high-confidence, manually verified aliases into
Tools/sdk_aliases.json. Existing entries are preserved unless a known
override is provided here. Run this *before* Tools/auto_alias.py so
auto-generated aliases do not clobber verified mappings.
"""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ALIASES_FILE = ROOT / "Tools" / "sdk_aliases.json"

# Curated aliases. These override any auto-generated or stale entries.
# Only add mappings that are explicitly verified from the dump or AGENTS.md.
KNOWN_OVERRIDES = {
    "Client": {
        "HumanClass": "Network",
        "Methods": {
            "SendHitReport": "AHLDAPJEJNC",
            "ProcessPacket": "FPIDGCHIEMJ",
            "Flush": "HKOFHOANEJD",
        },
        "Notes": {
            "SendHitReport": "internal void AHLDAPJEJNC(Vector3 origin, uint seq, List<Hit> hits) - send hit report",
            "ProcessPacket": "internal void FPIDGCHIEMJ(byte[] receiveBuffer, int receiveLength) - process received packet",
            "Flush": "internal void HKOFHOANEJD() - flush/send packet",
        },
    },
    "NET": {
        "HumanClass": "Net",
    },
    "MasterClient": {
        "HumanClass": "MasterServer",
    },
    "Controll.NJPOPGGFJIH": {
        "HumanClass": "MovementFlags",
    },
    "MouseLook.NLJBDGBDDLP": {
        "HumanClass": "MouseLookAxis",
    },
}

# Optional user-editable file for extra curated aliases.
EXTRA_ALIASES_FILE = ROOT / "Tools" / "known_aliases.json"


def deep_update(base: dict, override: dict) -> dict:
    """Recursively update `base` with `override`. Lists are replaced."""
    for key, value in override.items():
        if isinstance(value, dict) and key in base and isinstance(base[key], dict):
            base[key] = deep_update(base[key], value)
        else:
            base[key] = value
    return base


def main() -> int:
    data = json.loads(ALIASES_FILE.read_text(encoding="utf-8"))

    extra = {}
    if EXTRA_ALIASES_FILE.exists():
        extra = json.loads(EXTRA_ALIASES_FILE.read_text(encoding="utf-8"))

    all_overrides = deep_update(dict(KNOWN_OVERRIDES), extra)

    updated = 0
    added = 0
    for class_name, mapping in all_overrides.items():
        if class_name not in data:
            data[class_name] = {}
            added += 1
        else:
            updated += 1
        data[class_name] = deep_update(data[class_name], mapping)
        print(f"  {'+' if class_name not in data else '='} {class_name}")

    ALIASES_FILE.write_text(
        json.dumps(data, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    print(f"Done. Added {added}, updated {updated}. Total: {len(data)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
