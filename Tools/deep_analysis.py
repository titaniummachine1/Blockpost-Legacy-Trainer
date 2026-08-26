#!/usr/bin/env python3
"""Deep static analysis of the dump to answer key reverse-engineering questions.

Reads Tools/analysis/type_database.json and produces:
  - Tools/analysis/ammo_analysis.json: trace of ammo-related fields/methods
  - Tools/analysis/network_analysis.json: network protocol mapping
  - Tools/analysis/feature_analysis.json: speed/fly/noclip/esp opportunities
  - Tools/analysis/class_map.json: full class hierarchy with human names
"""
import json
import re
from pathlib import Path
from collections import defaultdict

ROOT = Path(__file__).resolve().parent.parent
ANALYSIS_DIR = ROOT / "Tools" / "analysis"
TYPE_DB = ANALYSIS_DIR / "type_database.json"

# Known human-readable names from sdk_aliases.json
KNOWN_NAMES = {
    "Controll": "Game",
    "KBBBHJDINCB": "Player",
    "CGJPBNDDPIN": "WeaponItem",
    "PLH": "Weapon",
    "Client": "Network",
    "MasterClient": "MasterServer",
    "NET": "Net",
    "DMHBMAAFCFJ": "Hit",
    "NAHLLMJMOED": "WeaponData",
    "FPNENMKEFBB": "LoadoutEntry",
    "Movement": "Movement",
    "HUD": "HUD",
    "GUIInv": "Inventory",
    "GOMBJHAKIFE": "BlockValidator",
    "BIMFEOACIDM": "BlockData",
    "PJPKAJCOJLB": "WeaponDefinition",
    "PJIMMBGGOBM": "WeaponVariant",
    "ACEDGBLFHDK": "WeaponCollection",
    "EEEBDHNOPDI": "InventoryEntry",
    "OJGPKMCPJDB": "ShopItem",
    "LFLEFDINMDA": "Achievement",
    "HOONFDNBMIM": "GameModeConfig",
    "HHMFAGJJOMH": "RoomConfig",
    "MDADLLEFHKO": "IconData",
    "AEKADIMKDIL": "ExtendedIconData",
    "NMGFEEKOKDB": "ParticleEffect",
    "IFALFNHBMFO": "MapMetadata",
    "CFMGCCJAFCD": "MapPreview",
    "LANMKMLNGOP": "ServerBrowserEntry",
    "EICNFHFLMOF": "ChunkCoord",
    "PBFLCAFNKMG": "CursorData",
    "Controll.NJPOPGGFJIH": "MovementFlags",
    "MouseLook.NLJBDGBDDLP": "MouseLookAxis",
    "PBMAFIFKGEH": "TeamColor",
    "FGICCBAAPGC": "GameMode",
    "LIMCMHLKAPK": "MaterialType",
    "PHMJFCEPJLH": "GraphicsTier",
    "EDODLIKGBOC": "MotionBlurType",
    "HECKHONLMLN": "ChunkVisibility",
    "IHFCHDIAMHJ": "DataStructureType",
    "DIKJFIAOHOI": "CoordinateSpace",
    "AKNKNGOIGMJ": "PlatformMode",
    "NDANMCKCENA": "AxisConstraint",
    "JNPOJGEBDJJ": "TransformOp",
}


def load_db() -> dict:
    return json.loads(TYPE_DB.read_text(encoding="utf-8"))


def find_methods_taking_type(db: dict, type_name: str) -> list[dict]:
    """Find all methods across all classes that take a parameter of the given type."""
    results = []
    for class_name, info in db.items():
        for m in info["methods"]:
            for arg in m.get("parsed_args", []):
                if type_name in arg["type"]:
                    results.append({
                        "class": class_name,
                        "method": m["name"],
                        "return_type": m.get("return_type", ""),
                        "args": m["args"],
                        "va": hex(m["va"]),
                        "arg_type": arg["type"],
                        "arg_name": arg["name"],
                    })
    return results


def find_methods_returning_type(db: dict, type_name: str) -> list[dict]:
    """Find all methods that return the given type."""
    results = []
    for class_name, info in db.items():
        for m in info["methods"]:
            ret = m.get("return_type", "")
            if type_name in ret:
                results.append({
                    "class": class_name,
                    "method": m["name"],
                    "return_type": ret,
                    "args": m["args"],
                    "va": hex(m["va"]),
                })
    return results


def find_fields_of_type(db: dict, type_name: str) -> list[dict]:
    """Find all fields of the given type across all classes."""
    results = []
    for class_name, info in db.items():
        for f in info["fields"]:
            if type_name in f["type"]:
                results.append({
                    "class": class_name,
                    "field": f["name"],
                    "type": f["type"],
                    "offset": hex(f["offset"]) if f["offset"] is not None else None,
                    "static": f["static"],
                })
    return results


def get_class_methods(db: dict, class_name: str) -> list[dict]:
    """Get all methods of a class with full info."""
    if class_name not in db:
        return []
    return db[class_name]["methods"]


def get_class_fields(db: dict, class_name: str) -> list[dict]:
    if class_name not in db:
        return []
    return db[class_name]["fields"]


def analyze_ammo(db: dict) -> dict:
    """Deep analysis of ammo-related fields and methods."""
    analysis = {
        "candidates": [],
        "fire_methods": [],
        "reload_methods": [],
        "ammo_modifying_methods": [],
        "loadout_methods": [],
        "weapon_data_fields": [],
        "conclusions": [],
    }

    # All known ammo candidates from AGENTS.md
    ammo_candidates = {
        "FGGKANNFBDH": {"class": "Controll", "offset": "0xC0", "desc": "AmmoInMag candidate"},
        "ILFOFIOFBAM": {"class": "Controll", "offset": "0xC8", "desc": "MaxAmmo candidate (-1=no weapon)"},
        "KJOMABGHAIJ": {"class": "Controll", "offset": "0xCC", "desc": "ReserveAmmo candidate"},
        "PELNEJDOBKH": {"class": "KBBBHJDINCB", "offset": "0xCC", "desc": "Ammo candidate (from demo recording)"},
        "GEDMGLAMGMD": {"class": "KBBBHJDINCB", "offset": "0x180", "desc": "Ammo candidate (from weapon data)"},
        "MHCOJFIAGLP": {"class": "KBBBHJDINCB", "offset": "0x184", "desc": "Ammo candidate (paired with above)"},
        "JHGGICCFNFJ": {"class": "KBBBHJDINCB", "offset": "?", "desc": "AmmoCandidate5"},
        "CNHNFDDJMJO": {"class": "KBBBHJDINCB", "offset": "?", "desc": "AmmoCandidate6"},
        "GDEMINMDJAC": {"class": "KBBBHJDINCB", "offset": "0xA8", "desc": "AmmoPerSlot int[] (PRIME candidate)"},
    }

    for field, info in ammo_candidates.items():
        methods_using = []
        # Find methods that reference this field name in their class
        if info["class"] in db:
            for m in db[info["class"]]["methods"]:
                # We can't see field access in method bodies (dump has empty bodies)
                # But we can check if the method name suggests ammo interaction
                pass
        analysis["candidates"].append({
            "field": field,
            "class": info["class"],
            "offset": info["offset"],
            "description": info["desc"],
        })

    # Find fire-related methods
    fire_keywords = ["Fire", "Shoot", "Bullet", "ShootBullet", "FireBullet", "CDEGJOBLOFO", "MFHJFPPOHLC"]
    for class_name in ["PLH", "Controll", "Shooter", "KBBBHJDINCB"]:
        if class_name not in db:
            continue
        for m in db[class_name]["methods"]:
            if any(kw.lower() in m["name"].lower() for kw in fire_keywords) or \
               any(kw in m["name"] for kw in fire_keywords):
                analysis["fire_methods"].append({
                    "class": class_name,
                    "method": m["name"],
                    "args": m["args"],
                    "return_type": m.get("return_type", ""),
                    "va": hex(m["va"]),
                })

    # Find reload-related methods
    reload_keywords = ["Reload", "ReloadMinigame", "ReloadEnd", "ReloadStart", "ReloadRequest"]
    for class_name in ["Controll", "PLH", "KBBBHJDINCB", "CGJPBNDDPIN"]:
        if class_name not in db:
            continue
        for m in db[class_name]["methods"]:
            if any(kw.lower() in m["name"].lower() for kw in reload_keywords):
                analysis["reload_methods"].append({
                    "class": class_name,
                    "method": m["name"],
                    "args": m["args"],
                    "return_type": m.get("return_type", ""),
                    "va": hex(m["va"]),
                })

    # Find methods that take int parameters and could be ammo-related
    # (methods on Player/Controll/WeaponItem that take/return int)
    for class_name in ["Controll", "KBBBHJDINCB", "CGJPBNDDPIN", "PLH"]:
        if class_name not in db:
            continue
        for m in db[class_name]["methods"]:
            ret = m.get("return_type", "")
            if ret == "int" or ret == "void":
                # Check if method has int params (could be ammo setters)
                has_int_param = any(arg["type"] == "int" for arg in m.get("parsed_args", []))
                if has_int_param and m["name"] not in [x["method"] for x in analysis["fire_methods"] + analysis["reload_methods"]]:
                    analysis["ammo_modifying_methods"].append({
                        "class": class_name,
                        "method": m["name"],
                        "args": m["args"],
                        "return_type": ret,
                        "va": hex(m["va"]),
                    })

    # Find loadout/inventory methods
    loadout_keywords = ["Loadout", "Slot", "Weapon", "Select", "Switch", "Equip"]
    for class_name in ["GUIInv", "FPNENMKEFBB", "KBBBHJDINCB", "Controll"]:
        if class_name not in db:
            continue
        for m in db[class_name]["methods"]:
            if any(kw.lower() in m["name"].lower() for kw in loadout_keywords):
                analysis["loadout_methods"].append({
                    "class": class_name,
                    "method": m["name"],
                    "args": m["args"],
                    "return_type": m.get("return_type", ""),
                    "va": hex(m["va"]),
                })

    # WeaponData fields (NAHLLMJMOED)
    if "NAHLLMJMOED" in db:
        for f in db["NAHLLMJMOED"]["fields"]:
            analysis["weapon_data_fields"].append({
                "field": f["name"],
                "type": f["type"],
                "offset": hex(f["offset"]) if f["offset"] is not None else None,
            })

    # Find methods that take KBBBHJDINCB (Player) as parameter - these likely modify player state
    player_methods = find_methods_taking_type(db, "KBBBHJDINCB")
    analysis["methods_taking_player"] = [
        {"class": m["class"], "method": m["method"], "args": m["args"], "va": m["va"]}
        for m in player_methods[:50]  # Top 50
    ]

    # Find methods that take CGJPBNDDPIN (WeaponItem) as parameter
    weapon_methods = find_methods_taking_type(db, "CGJPBNDDPIN")
    analysis["methods_taking_weapon"] = [
        {"class": m["class"], "method": m["method"], "args": m["args"], "va": m["va"]}
        for m in weapon_methods[:50]
    ]

    return analysis


def analyze_network(db: dict) -> dict:
    """Map the network protocol from method signatures."""
    analysis = {
        "client_methods": [],
        "master_client_methods": [],
        "net_primitives": [],
        "packet_handlers": [],
        "send_methods": [],
        "connection_methods": [],
        "conclusions": [],
    }

    # Client class methods
    if "Client" in db:
        for m in db["Client"]["methods"]:
            entry = {
                "method": m["name"],
                "args": m["args"],
                "return_type": m.get("return_type", ""),
                "va": hex(m["va"]),
            }
            analysis["client_methods"].append(entry)
            # Categorize
            name_lower = m["name"].lower()
            if "send" in name_lower or "flush" in name_lower or "write" in name_lower:
                analysis["send_methods"].append(entry)
            if "connect" in name_lower or "disconnect" in name_lower or "close" in name_lower:
                analysis["connection_methods"].append(entry)
            if "process" in name_lower or "handle" in name_lower or "receive" in name_lower or "parse" in name_lower:
                analysis["packet_handlers"].append(entry)

    # MasterClient methods
    if "MasterClient" in db:
        for m in db["MasterClient"]["methods"]:
            entry = {
                "method": m["name"],
                "args": m["args"],
                "return_type": m.get("return_type", ""),
                "va": hex(m["va"]),
            }
            analysis["master_client_methods"].append(entry)
            name_lower = m["name"].lower()
            if "send" in name_lower or "flush" in name_lower or "write" in name_lower:
                analysis["send_methods"].append(entry)
            if "connect" in name_lower or "disconnect" in name_lower:
                analysis["connection_methods"].append(entry)
            if "process" in name_lower or "handle" in name_lower or "receive" in name_lower:
                analysis["packet_handlers"].append(entry)

    # NET primitives
    if "NET" in db:
        for m in db["NET"]["methods"]:
            analysis["net_primitives"].append({
                "method": m["name"],
                "args": m["args"],
                "return_type": m.get("return_type", ""),
                "va": hex(m["va"]),
            })

    # Find all classes with "Packet" in name or methods with "Packet" in name
    for class_name, info in db.items():
        if "packet" in class_name.lower():
            analysis["packet_classes"] = analysis.get("packet_classes", [])
            analysis["packet_classes"].append({
                "class": class_name,
                "method_count": info["method_count"],
                "field_count": info["field_count"],
            })

    # Find methods that take byte[] (likely packet data)
    byte_array_methods = find_methods_taking_type(db, "byte[]")
    analysis["byte_array_methods"] = [
        {"class": m["class"], "method": m["method"], "args": m["args"], "va": m["va"]}
        for m in byte_array_methods[:30]
    ]

    return analysis


def analyze_features(db: dict) -> dict:
    """Analyze classes for cheat feature opportunities."""
    analysis = {
        "speed_hack": {"targets": [], "methods": []},
        "fly_hack": {"targets": [], "methods": []},
        "noclip": {"targets": [], "methods": []},
        "esp": {"targets": [], "methods": []},
        "chams": {"targets": [], "methods": []},
        "triggerbot": {"targets": [], "methods": []},
        "third_person": {"targets": [], "methods": []},
        "infinite_ammo": {"targets": [], "methods": []},
    }

    # Speed hack: look for movement speed fields
    for class_name in ["Movement", "Controll", "KBBBHJDINCB"]:
        if class_name not in db:
            continue
        for f in db[class_name]["fields"]:
            if f["type"] in ("float", "int") and any(kw in f["name"].lower() for kw in
                ["speed", "velocity", "move", "walk", "run", "sprint", "accel", "force"]):
                analysis["speed_hack"]["targets"].append({
                    "class": class_name,
                    "field": f["name"],
                    "type": f["type"],
                    "offset": hex(f["offset"]) if f["offset"] is not None else None,
                })

    # Find Movement methods
    if "Movement" in db:
        for m in db["Movement"]["methods"]:
            if any(kw in m["name"].lower() for kw in ["move", "ground", "air", "accel", "jump", "speed", "velocity"]):
                analysis["speed_hack"]["methods"].append({
                    "method": m["name"],
                    "args": m["args"],
                    "return_type": m.get("return_type", ""),
                    "va": hex(m["va"]),
                })

    # Fly hack: look for gravity/rigidbody fields
    for class_name in ["KBBBHJDINCB", "Controll", "Movement"]:
        if class_name not in db:
            continue
        for f in db[class_name]["fields"]:
            if any(kw in f["name"].lower() for kw in ["gravity", "rigidbody", "mass", "drag", "usegravity"]):
                analysis["fly_hack"]["targets"].append({
                    "class": class_name,
                    "field": f["name"],
                    "type": f["type"],
                    "offset": hex(f["offset"]) if f["offset"] is not None else None,
                })

    # Noclip: look for collider fields
    for class_name in ["KBBBHJDINCB", "Controll", "Movement"]:
        if class_name not in db:
            continue
        for f in db[class_name]["fields"]:
            if any(kw in f["type"].lower() for kw in ["collider", "charactercontroller", "capsulecollider"]):
                analysis["noclip"]["targets"].append({
                    "class": class_name,
                    "field": f["name"],
                    "type": f["type"],
                    "offset": hex(f["offset"]) if f["offset"] is not None else None,
                })

    # ESP: look for camera/rendering methods
    for class_name in ["Controll", "HUD", "HUDNames"]:
        if class_name not in db:
            continue
        for m in db[class_name]["methods"]:
            if any(kw in m["name"].lower() for kw in ["worldtoscreen", "screentoworld", "draw", "render", "name", "tag"]):
                analysis["esp"]["methods"].append({
                    "class": class_name,
                    "method": m["name"],
                    "args": m["args"],
                    "va": hex(m["va"]),
                })

    # Chams: look for material/renderer fields on player
    if "KBBBHJDINCB" in db:
        for f in db["KBBBHJDINCB"]["fields"]:
            if any(kw in f["type"].lower() for kw in ["renderer", "material", "mesh", "skinned"]):
                analysis["chams"]["targets"].append({
                    "field": f["name"],
                    "type": f["type"],
                    "offset": hex(f["offset"]) if f["offset"] is not None else None,
                })

    # Triggerbot: look for raycast methods
    for class_name, info in db.items():
        for m in info["methods"]:
            if "raycast" in m["name"].lower() or "raycast" in m.get("return_type", "").lower():
                analysis["triggerbot"]["methods"].append({
                    "class": class_name,
                    "method": m["name"],
                    "args": m["args"],
                    "return_type": m.get("return_type", ""),
                    "va": hex(m["va"]),
                })

    # Third person: look for camera offset/position fields
    for class_name in ["Controll", "MouseLook", "Following"]:
        if class_name not in db:
            continue
        for f in db[class_name]["fields"]:
            if any(kw in f["name"].lower() for kw in ["camera", "offset", "distance", "zoom", "fov"]):
                analysis["third_person"]["targets"].append({
                    "class": class_name,
                    "field": f["name"],
                    "type": f["type"],
                    "offset": hex(f["offset"]) if f["offset"] is not None else None,
                })

    # Infinite ammo: all int fields on weapon-related classes
    for class_name in ["Controll", "KBBBHJDINCB", "CGJPBNDDPIN", "PLH"]:
        if class_name not in db:
            continue
        for f in db[class_name]["fields"]:
            if f["type"] == "int" and f["offset"] is not None:
                analysis["infinite_ammo"]["targets"].append({
                    "class": class_name,
                    "field": f["name"],
                    "offset": hex(f["offset"]),
                })

    return analysis


def build_class_map(db: dict) -> dict:
    """Build a comprehensive class map with human names where known."""
    class_map = {}

    for class_name, info in db.items():
        human = KNOWN_NAMES.get(class_name, "")
        entry = {
            "obfuscated": class_name,
            "human": human,
            "kind": info["kind"],
            "bases": info["bases"],
            "line": info["line"],
            "field_count": info["field_count"],
            "property_count": info["property_count"],
            "method_count": info["method_count"],
            "key_fields": [],
            "key_methods": [],
        }

        # Pick out key fields (non-trivial types, interesting names)
        for f in info["fields"]:
            if f["offset"] is not None and not f["static"]:
                entry["key_fields"].append({
                    "name": f["name"],
                    "type": f["type"],
                    "offset": hex(f["offset"]),
                })

        # Pick out key methods (non-trivial signatures)
        for m in info["methods"]:
            if m["args"] or m.get("return_type", "") not in ("void", ""):
                entry["key_methods"].append({
                    "name": m["name"],
                    "args": m["args"],
                    "return_type": m.get("return_type", ""),
                    "va": hex(m["va"]),
                })

        class_map[class_name] = entry

    return class_map


def main() -> int:
    print("Loading type database ...")
    db = load_db()
    print(f"  {len(db)} types loaded")

    print("Analyzing ammo ...")
    ammo = analyze_ammo(db)
    (ANALYSIS_DIR / "ammo_analysis.json").write_text(
        json.dumps(ammo, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    print(f"  {len(ammo['candidates'])} candidates, {len(ammo['fire_methods'])} fire methods, "
          f"{len(ammo['reload_methods'])} reload methods")

    print("Analyzing network ...")
    net = analyze_network(db)
    (ANALYSIS_DIR / "network_analysis.json").write_text(
        json.dumps(net, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    print(f"  {len(net['client_methods'])} client methods, {len(net['master_client_methods'])} master methods, "
          f"{len(net['net_primitives'])} net primitives")

    print("Analyzing features ...")
    feats = analyze_features(db)
    (ANALYSIS_DIR / "feature_analysis.json").write_text(
        json.dumps(feats, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    for feat, data in feats.items():
        targets = len(data.get("targets", []))
        methods = len(data.get("methods", []))
        print(f"  {feat}: {targets} targets, {methods} methods")

    print("Building class map ...")
    cmap = build_class_map(db)
    (ANALYSIS_DIR / "class_map.json").write_text(
        json.dumps(cmap, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    print(f"  {len(cmap)} classes mapped")

    print(f"Done. Output in {ANALYSIS_DIR}")
    return 0


if __name__ == "__main__":
    import sys
    sys.exit(main())
