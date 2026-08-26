#!/usr/bin/env python3
"""Auto-generate aliases for ALL TARGET_CLASSES based on static analysis.

Uses the type database to infer field/method purpose from:
  - Field types (Camera, Rigidbody, GameObject, etc.)
  - Method signatures (parameter types, return types)
  - Readable field names in partially-obfuscated classes
  - Cross-references between classes
  - Known patterns from the existing aliases

Updates sdk_aliases.json with new entries (preserves existing ones).
"""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TYPE_DB = ROOT / "Tools" / "analysis" / "type_database.json"
ALIASES_FILE = ROOT / "Tools" / "sdk_aliases.json"

# Load
db = json.loads(TYPE_DB.read_text(encoding="utf-8"))
aliases = json.loads(ALIASES_FILE.read_text(encoding="utf-8"))

# Type-based field name inference
TYPE_INFERRED_NAMES = {
    "Camera": ["Camera", "MainCamera", "RadarCamera", "OverviewCamera"],
    "Rigidbody": ["Rigidbody", "PlayerRigidbody", "BodyRigidbody"],
    "GameObject": ["GameObject", "RootObject", "Head", "Body", "Backpack", "ArmHelp",
                    "LeftArm", "RightArm", "WaterSplash", "Arrow", "Prefab", "Effect"],
    "Transform": ["Transform", "CameraTransform", "ControllerTransform"],
    "AudioSource": ["AudioSource", "FireSound", "ReloadSound", "FootstepSound"],
    "AudioClip": ["AudioClip", "FireClip", "ReloadClip", "FootstepClip"],
    "Texture2D": ["Texture", "Icon", "IconAlt", "Skin", "SkinAlt"],
    "Material": ["Material", "SkinMaterial", "BodyMaterial"],
    "Animation": ["Animation", "WeaponAnimation"],
    "AnimationClip": ["AnimationClip", "FireAnim", "ReloadAnim", "IdleAnim"],
    "BoxCollider": ["BoxCollider", "HeadCollider", "BodyCollider"],
    "CharacterJoint": ["CharacterJoint", "Joint"],
    "Vector3": ["Position", "Forward", "CameraForward", "MuzzleForward", "Velocity",
                 "SpreadVector", "SpawnPoint", "TargetPoint"],
    "Vector2": ["Vector2", "IconOffset", "UVOffset"],
    "Color": ["Color", "TeamColor", "IndicatorColor"],
    "Rect": ["Rect", "ScreenRect", "UIRect"],
    "Plane[]": ["Planes", "FrustumPlanes"],
    "List<DMHBMAAFCFJ>": ["HitList", "Hits"],
    "List<CGJPBNDDPIN>": ["WeaponList", "Weapons"],
    "List<Controll.LLECAFPENFN>": ["PlayerList", "Players"],
    "KBBBHJDINCB": ["Player", "MainPlayer", "TargetPlayer"],
    "CGJPBNDDPIN": ["WeaponItem", "ActiveWeapon", "Weapon"],
    "NAHLLMJMOED": ["WeaponData", "WeaponDefinition"],
    "FPNENMKEFBB[]": ["Loadout", "LoadoutEntries"],
    "FPNENMKEFBB": ["LoadoutEntry", "SelectedLoadout"],
    "GOMBJHAKIFE[]": ["BlockValidators", "Blocks"],
    "BIMFEOACIDM[]": ["BlockData", "BlockDataArray"],
    "CFMGCCJAFCD[]": ["Attachments", "WeaponAttachments"],
    "HELILPACLAM": ["BoneRig", "BoneRig1"],
    "IIMNEEFAPBC": ["IKTarget", "IKTarget1"],
    "int": ["Value", "Count", "Id", "Index", "Health", "Ammo", "TeamId",
             "PlayerId", "KillCount", "DeathCount", "Score", "Level", "Slot"],
    "int[]": ["IntArray", "AmmoPerSlot", "StatsArray", "ScoreHistory"],
    "float": ["Timer", "Speed", "Sensitivity", "Rate", "Factor", "Distance",
               "Yaw", "Pitch", "Spread", "Recoil", "FireRate", "ReloadTime"],
    "float[]": ["FloatArray", "RecoilPattern", "SpreadPattern"],
    "bool": ["Flag", "IsActive", "IsReady", "IsDead", "IsFiring", "IsReloading",
              "IsGrounded", "IsSprinting", "IsCrouching", "IsJumping", "IsAiming"],
    "string": ["Name", "DisplayName", "Codename", "Hash", "AuthKey", "PlayerName"],
    "string[]": ["StringArray", "WeaponNames", "Options"],
    "uint": ["Sequence", "InputState", "Flags", "PlayerId"],
    "ulong": ["UniqueId", "SteamId"],
    "byte": ["Byte", "Slot", "TeamByte"],
    "byte[]": ["Buffer", "ReceiveBuffer", "SendBuffer", "Data"],
    "short": ["Short", "Port", "Count16"],
    "TcpClient": ["TcpClient", "Connection"],
}

# High-confidence exact overrides. These are applied before generic inference.
METHOD_OVERRIDES = {
    "Client": {
        "AHLDAPJEJNC": "SendHitReport",
        "FPIDGCHIEMJ": "ProcessPacket",
        "HKOFHOANEJD": "Flush",
    },
    "PLH": {
        "CDEGJOBLOFO": "Fire",
        "MFHJFPPOHLC": "FireAlt",
    },
}

FIELD_OVERRIDES = {
    "Client": {
        "PEGEIKDNHLL": "ReceiveBuffer",
        "FKEHEHGFNBD": "ReceiveLength",
        "HPDGDLFMEKI": "TcpClient",
    },
}


def infer_field_name(field: dict, class_name: str, all_fields: list) -> str:
    """Infer a human-readable name for a field based on its type and context."""
    ftype = field["type"]
    fname = field["name"]
    offset = field["offset"]

    # Exact known overrides first
    if class_name in FIELD_OVERRIDES and fname in FIELD_OVERRIDES[class_name]:
        return FIELD_OVERRIDES[class_name][fname]

    # If the field name is already readable, use it
    if any(c.islower() for c in fname) and not fname.startswith("<"):
        return fname

    # Use type-based inference
    if ftype in TYPE_INFERRED_NAMES:
        candidates = TYPE_INFERRED_NAMES[ftype]
        # Use offset to pick a unique name
        idx = 0
        for f in all_fields:
            if f["type"] == ftype and f["name"] != fname:
                idx += 1
        return candidates[min(idx, len(candidates) - 1)]

    # Special cases
    if ftype == "Controll":
        return "Instance"
    if ftype == "Client":
        return "Instance"
    if ftype == "MasterClient":
        return "Instance"

    # Default: use type name
    return f"{ftype}_{fname[:4]}"


def infer_method_name(method: dict, class_name: str) -> str:
    """Infer a human-readable name for a method based on its signature."""
    name = method["name"]
    ret = method.get("return_type", "void")
    args = method.get("parsed_args", [])

    # Exact known overrides first
    if class_name in METHOD_OVERRIDES and name in METHOD_OVERRIDES[class_name]:
        return METHOD_OVERRIDES[class_name][name]

    # Skip constructors and special methods
    if name.startswith(".ctor") or name.startswith("get_") or name.startswith("set_"):
        return name

    # If the name is already readable, use it
    if any(c.islower() for c in name):
        return name

    # Infer from signature
    if ret == "void" and len(args) == 0:
        return f"Action_{name[:4]}"
    if ret == "void" and len(args) == 1:
        arg_type = args[0]["type"] if args else ""
        if arg_type == "float":
            return f"WriteFloat_{name[:4]}"
        if arg_type == "int":
            return f"WriteInt_{name[:4]}"
        if arg_type == "string":
            return f"WriteString_{name[:4]}"
        if arg_type == "bool":
            return f"SetFlag_{name[:4]}"
        if arg_type == "Vector3":
            return f"SetVector3_{name[:4]}"
        if arg_type == "KBBBHJDINCB":
            return f"SetPlayer_{name[:4]}"
        if arg_type == "CGJPBNDDPIN":
            return f"SetWeapon_{name[:4]}"
        return f"Write_{name[:4]}"
    if ret != "void" and len(args) == 0:
        if ret == "int":
            return f"GetInt_{name[:4]}"
        if ret == "float":
            return f"GetFloat_{name[:4]}"
        if ret == "string":
            return f"GetString_{name[:4]}"
        if ret == "bool":
            return f"GetBool_{name[:4]}"
        if ret == "Vector3":
            return f"GetPosition_{name[:4]}"
        if ret == "CGJPBNDDPIN":
            return f"GetWeapon_{name[:4]}"
        if ret == "FPNENMKEFBB":
            return f"GetLoadout_{name[:4]}"
        if ret == "Texture2D":
            return f"GetTexture_{name[:4]}"
        return f"Get_{name[:4]}"
    if ret == "bool" and len(args) == 1 and args[0]["type"] == "KBBBHJDINCB":
        return f"CheckPlayer_{name[:4]}"

    return f"Method_{name[:4]}"


def generate_aliases_for_class(class_name: str) -> dict:
    """Generate alias entries for a class based on static analysis."""
    if class_name not in db:
        return {}

    info = db[class_name]
    is_enum = info["kind"] == "enum"

    result = {
        "HumanClass": aliases.get(class_name, {}).get("HumanClass", class_name),
        "Fields": {},
        "Methods": {},
        "Properties": {},
        "Notes": {},
    }

    # Preserve existing aliases
    existing = aliases.get(class_name, {})
    if "HumanClass" in existing:
        result["HumanClass"] = existing["HumanClass"]
    if "Fields" in existing:
        result["Fields"].update(existing["Fields"])
    if "Methods" in existing:
        result["Methods"].update(existing["Methods"])
    if "Properties" in existing:
        result["Properties"].update(existing["Properties"])
    if "Notes" in existing:
        result["Notes"].update(existing["Notes"])

    # Generate field aliases for fields not yet aliased
    # Only alias fields that have an offset (instance fields with 0xNN offset)
    # Const/static fields without offsets are not in the Raw.Offsets class
    for f in info["fields"]:
        if f["name"] in result["Fields"].values():
            continue
        if f["name"].startswith("<"):
            continue
        # Skip fields without offsets (const fields, static fields without offset)
        # These won't be in the Raw.Offsets class and would cause build errors
        if not is_enum and f["offset"] is None:
            continue
        # Skip if already aliased
        already = False
        for alias_name, orig_name in result["Fields"].items():
            if orig_name == f["name"]:
                already = True
                break
        if already:
            continue

        inferred = infer_field_name(f, class_name, info["fields"])
        # Make unique
        base = inferred
        idx = 1
        while inferred in result["Fields"]:
            inferred = f"{base}_{idx}"
            idx += 1
        result["Fields"][inferred] = f["name"]

    # Generate method aliases for methods not yet aliased
    for m in info["methods"]:
        if m["name"] in result["Methods"].values():
            continue
        if m["name"].startswith("."):
            continue
        # Skip if already aliased
        already = False
        for alias_name, orig_name in result["Methods"].items():
            if orig_name == m["name"]:
                already = True
                break
        if already:
            continue

        inferred = infer_method_name(m, class_name)
        # Make unique
        base = inferred
        idx = 1
        while inferred in result["Methods"]:
            inferred = f"{base}_{idx}"
            idx += 1
        result["Methods"][inferred] = m["name"]

    # Generate property aliases
    for p in info["properties"]:
        if p["name"] in result["Properties"].values():
            continue
        if p["name"].startswith("<"):
            continue
        already = False
        for alias_name, orig_name in result["Properties"].items():
            if orig_name == p["name"]:
                already = True
                break
        if already:
            continue
        # Use the property name if readable, else infer
        if any(c.islower() for c in p["name"]):
            result["Properties"][p["name"]] = p["name"]
        else:
            inferred = f"Prop_{p['name'][:4]}"
            base = inferred
            idx = 1
            while inferred in result["Properties"]:
                inferred = f"{base}_{idx}"
                idx += 1
            result["Properties"][inferred] = p["name"]

    return result


# Import TARGET_CLASSES from generate_sdk.py
import importlib.util
spec = importlib.util.spec_from_file_location("generate_sdk", ROOT / "Tools" / "generate_sdk.py")
generate_sdk = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generate_sdk)
TARGET_CLASSES = generate_sdk.TARGET_CLASSES

print(f"Generating aliases for {len(TARGET_CLASSES)} target classes ...")

new_count = 0
updated_count = 0

for cls in TARGET_CLASSES:
    if cls not in db:
        print(f"  SKIP {cls} (not in dump)")
        continue

    old_entry = aliases.get(cls, {})
    new_entry = generate_aliases_for_class(cls)

    if cls not in aliases:
        new_count += 1
    elif len(new_entry.get("Fields", {})) > len(old_entry.get("Fields", {})):
        updated_count += 1

    aliases[cls] = new_entry

# Also add aliases for classes referenced by TARGET_CLASSES
# (e.g. types used as fields/methods in target classes)
referenced_types = set()
for cls in TARGET_CLASSES:
    if cls not in db:
        continue
    for f in db[cls]["fields"]:
        # Extract type names
        for m in re.finditer(r"\b([A-Z][A-Za-z0-9_]{4,})\b", f["type"]):
            ref = m.group(1)
            if ref in db and ref not in aliases and ref not in TARGET_CLASSES:
                referenced_types.add(ref)

print(f"\nFound {len(referenced_types)} referenced types not yet aliased")
# Add aliases for the most-referenced types
ref_count = {}
for ref in referenced_types:
    count = 0
    for cls in TARGET_CLASSES:
        if cls not in db:
            continue
        for f in db[cls]["fields"]:
            if ref in f["type"]:
                count += 1
    ref_count[ref] = count

# Add all referenced types that are not already aliased
for ref, count in sorted(ref_count.items(), key=lambda x: x[1], reverse=True):
    aliases[ref] = generate_aliases_for_class(ref)
    new_count += 1
    print(f"  + {ref} (referenced {count} times)")

# Save
ALIASES_FILE.write_text(
    json.dumps(aliases, indent=2, ensure_ascii=False) + "\n",
    encoding="utf-8"
)

print(f"\n=== Summary ===")
print(f"New classes: {new_count}")
print(f"Updated classes: {updated_count}")
print(f"Total aliased classes: {len(aliases)}")
print(f"Saved to {ALIASES_FILE}")
