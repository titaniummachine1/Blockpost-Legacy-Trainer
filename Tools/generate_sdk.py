#!/usr/bin/env python3
"""Generate a C# offset/method SDK from an Il2CppDumper dump.cs file.

Usage:
    python Tools/generate_sdk.py

It reads .tools/Il2CppDumper/dump.cs and Tools/sdk_aliases.json and writes
Sdk/Generated/<Class>.cs plus Sdk/Generated/Aliases.cs.
"""

import json
import re
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DUMP_CS = ROOT / ".tools" / "Il2CppDumper" / "dump.cs"
ALIASES_FILE = ROOT / "Tools" / "sdk_aliases.json"
OUT_DIR = ROOT / "Sdk" / "Generated"

# Classes we definitely want in the SDK. More can be added here or in the aliases.
TARGET_CLASSES = [
    # Core game logic
    "Controll",
    "Controll.LLECAFPENFN",
    "Movement",
    "MouseLook",
    "Shooter",
    "PLH",
    "Spectator",
    "Crosshair",
    "Radar",
    "Following",
    "FreeFlyCamera",
    # Player & weapon data structures
    "KBBBHJDINCB",
    "CGJPBNDDPIN",
    "NAHLLMJMOED",
    "FPNENMKEFBB",
    "DMHBMAAFCFJ",
    "GOMBJHAKIFE",
    "BIMFEOACIDM",
    "PJPKAJCOJLB",
    "PJIMMBGGOBM",
    "ACEDGBLFHDK",
    "EEEBDHNOPDI",
    "OJGPKMCPJDB",
    "LFLEFDINMDA",
    # Network
    "Client",
    "Client.AGAPNOPLKDB",
    "MasterClient",
    "MasterClient.DHCBFAKOCAA",
    "NET",
    "NEGGNDFJMAK",
    "NEGGNDFJMAK.GBPCAKEGOIJ",
    "NEGGNDFJMAK.FPEDLONKNMC",
    "NEGGNDFJMAK.BEONIPPEOGM",
    "DropClient",
    # UI - HUD
    "HUD",
    "HUDMessage",
    "HUDMessage.JCHHGBGPFGN",
    "HUDMessage.EKAOIFHCKNB",
    "HUDMessage.PNCCLCBPABA",
    "UIDeathMessage",
    "HUDBuild",
    "HUDGameEnd",
    "HUDTab",
    "HUDNames",
    "HUDKiller",
    "HUDIndicator",
    # UI - Menus
    "GUIInv",
    "GUIMap",
    "GUIPlay",
    "GUIM",
    "GUIMMain",
    "GUIMPlay",
    "GUIAdmin",
    "GUIAdminMaplist",
    "GUIAdminUpload",
    "GUIAdminSettings",
    "GUIAdminPlayers",
    "GUIChar",
    "GUICharEditor",
    "GUISkinEditor",
    "GUIGameSet",
    "GUIGameMenu",
    "GUIGameSquad",
    "GUICraft",
    "GUICase",
    "GUIClan",
    "GUIIcon",
    "GUIShop",
    "GUIOptions",
    "GUIObj",
    "GUIName",
    "GUIFX",
    "GUIProfile",
    "GUIRank",
    "GUIRank.MFHOMFNKDBG",
    "GUIGold",
    "GUIBonus",
    "GUI3D",
    "VoxelPaletteGUI",
    "UIChatMessage",
    "UIHUD",
    "UIMPlay",
    "UIMMainmenu",
    "UIMInventory",
    "UIMPlaymode",
    "UIMShop",
    "UIMTasks",
    "UIMReward",
    "UIDrop",
    "UIDropButton",
    "UIDropButtonExit",
    "UIElementBase",
    "UIColors",
    "UIPalette",
    "UIPaletteColorPreview",
    # Managers
    "Main",
    "MainManager",
    "MainConfig",
    "MainBack",
    "UIManager",
    "ParticleManager",
    "SteamManager",
    "MapLoader",
    "MapGenerator",
    "MapAutoload",
    "MapEvent",
    "MapPrefab",
    "MapCulling",
    # Voxel/map
    "VoxelMap",
    "VoxelBattleMap",
    "VoxelMapLight",
    "VoxelMapSceneItem",
    "VoxelMultList",
    "VoxelGreedy",
    "VoxelPalette",
    "VoxelAtlas",
    "VoxelRadiusUpdate",
    "VCGen",
    "VWGen",
    "VWGen2",
    "VWIK",
    "VWPos",
    "VDestr",
    "MapOcc",
    "BPPMap",
    "TMap",
    "HMap",
    "OMap",
    "MapLayerGenerator",
    "GrassGenerator",
    "TreeGenerator",
    "MapLightBaker",
    "MapUpload",
    "SaveMap",
    # Effects
    "FXBloodSplat",
    "FXScale",
    "FXScaleCase",
    "FXTracer",
    "GeneralCameraShake",
    "OutlineSystem",
    "FadeLight",
    # Animation & character
    "CharAnimator",
    "MChar",
    "MCharAnimator",
    "SoundLoader",
    # Utility & debug
    "Util",
    "Util2",
    "UtilHash",
    "UtilChar",
    "Log",
    "DevDraw",
    "dbgNet",
    "DemoRec",
    "ConsoleBase",
    "Console",
    "Lang",
    "LangWeapon",
    # Input
    "VInput",
    "InputHelper",
    "ControllTouch",
    # Data structures (obfuscated)
    "HOONFDNBMIM",
    "HHMFAGJJOMH",
    "MDADLLEFHKO",
    "EECOBMIMJEL",
    "CHPELPHDFJE",
    "ICNIFLJBPDA",
    "NMGFEEKOKDB",
    "IFALFNHBMFO",
    "AEKADIMKDIL",
    "PBFLCAFNKMG",
    "MLDGDBIFMEO",
    "GFENDPCMKFI",
    "JCPCKNLOIED",
    "CFMGCCJAFCD",
    "HELILPACLAM",
    "KJDJGJJLOBC",
    "IIMNEEFAPBC",
    "MFGOJNMLKGG",
    "LANMKMLNGOP",
    "EICNFHFLMOF",
    "IMMADJCIMNI",
    "IFGNGLDKNPA",
    "OIEJMJAPFGH",
    "IGMIAOIMNAJ",
    "KMGJMLHJHDD",
    "ABPEFGNFBBC",
    "MCCKEODPMDC",
    "FileSender",
    "HitData",
    # Enums (parsed as classes for offset generation - they have values)
    "Controll.NJPOPGGFJIH",
    "MouseLook.NLJBDGBDDLP",
    "PBMAFIFKGEH",
    "FGICCBAAPGC",
    "LIMCMHLKAPK",
    "PHMJFCEPJLH",
    "EDODLIKGBOC",
    "HECKHONLMLN",
    "IHFCHDIAMHJ",
    "DIKJFIAOHOI",
    "AKNKNGOIGMJ",
    "NDANMCKCENA",
    "JNPOJGEBDJJ",
]

# Map of simple types to a comment-friendly size hint
SIZE_HINTS = {
    "Vector3": 12,
    "Vector2": 8,
    "Color": 16,
    "Rect": 16,
    "Quaternion": 16,
    "Matrix4x4": 64,
    "Plane": 16,
    "string": -1,
}

# Names that would hide inherited System.Object members when used as constants.
RESERVED_CSHARP_NAMES = {
    "Equals", "GetHashCode", "GetType", "ToString", "Finalize",
    "MemberwiseClone", "ReferenceEquals",
}


def safe_member_name(name: str, seen: dict, check_reserved: bool = True) -> str:
    """Return a C# member name that is not reserved and is unique within `seen`.

    `seen` maps the base (sanitized) name to a counter. The returned name is
    added to `seen` and incremented if it already exists.
    """
    base = csharp_identifier(name)
    if check_reserved and base in RESERVED_CSHARP_NAMES:
        base = f"{base}_"
    seen[base] = seen.get(base, 0) + 1
    n = seen[base]
    if n == 1:
        return base
    # Use _1, _2, ... for duplicates and avoid double underscores if the
    # base already ends with one (e.g. reserved names like Equals_).
    sep = "" if base.endswith("_") else "_"
    return f"{base}{sep}{n - 1}"


def unique_name(name: str, seen: dict) -> str:
    """Return a unique sanitized C# member name, appending a counter for duplicates."""
    return safe_member_name(name, seen, check_reserved=False)


def offset_literal(value: int) -> str:
    """Return a C# int literal for an Il2Cpp field offset or enum value.

    Values with the high bit set are cast from a uint literal to avoid the
    `uint to int' compile error while keeping the same bit pattern.
    """
    if value >= 0x80000000:
        return f"unchecked((int){hex(value)}u)"
    return hex(value)


def load_dump_text() -> str:
    if not DUMP_CS.exists():
        raise FileNotFoundError(f"dump.cs not found at {DUMP_CS}")
    return DUMP_CS.read_text(encoding="utf-8", errors="ignore")


def find_class_block(text: str, class_name: str) -> tuple[str | None, int, str]:
    """Find a top-level class or enum block by brace counting.

    Returns (body, start_line, kind) where kind is "enum", "struct" or "class".
    On failure returns (None, 0, "").
    """
    needle = class_name.replace(".", "\\.")
    # Match the class/enum declaration line, optionally with a base class,
    # generic 'where' constraints, and a TypeDefIndex comment.
    pattern = re.compile(
        rf"^(?P<attrs>(?:\[[^\]]+\]\s*)*)"
        rf"\s*(?P<mods>(?:internal|public|private|protected)(?:\s+(?:sealed|abstract|static|readonly))*)\s+"
        rf'(?P<kind>class|enum|struct)\s+{needle}'
        rf'(?P<basepart>(?:\s*:\s*[^/]+?|\s+where\s+[^/]+?))?'
        rf"\s*(?://\s*TypeDefIndex:\s*(?P<tdi>\d+))?\s*$",
        re.MULTILINE,
    )
    for m in pattern.finditer(text):
        # Find the opening brace on a subsequent line.
        after = text[m.end():]
        brace_match = re.search(r"^\s*\{\s*$", after, re.MULTILINE)
        if not brace_match:
            continue
        body_start = m.end() + brace_match.end()
        body, _ = brace_count(text[body_start:])
        if body is not None:
            line_no = text[:body_start].count("\n") + 1
            tdi = int(m.group("tdi")) if m.group("tdi") else -1
            return body, line_no, m.group("kind"), tdi
    return None, 0, "", -1


def brace_count(text: str) -> tuple[str | None, int]:
    """Given text starting at the first character inside a {, return the substring
    up to the matching } and the number of bytes consumed."""
    depth = 1
    i = 0
    while i < len(text) and depth > 0:
        ch = text[i]
        if ch == "'" or ch == '"':
            # skip string/char literal
            quote = ch
            i += 1
            while i < len(text):
                if text[i] == "\\":
                    i += 2
                    continue
                if text[i] == quote:
                    i += 1
                    break
                i += 1
            continue
        if ch == "/" and i + 1 < len(text) and text[i + 1] == "/":
            # skip to end of line
            end = text.find("\n", i)
            if end == -1:
                break
            i = end + 1
            continue
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
        i += 1
    if depth == 0:
        return text[:i - 1], i
    return None, 0


def parse_fields(body: str, is_enum: bool = False) -> list[dict]:
    """Parse // Fields section. For enums, also parses named enum members."""
    fields = []
    # Find the end of the Fields section: a line like "\t// Methods" or first method RVA.
    methods_idx = re.search(r"\n\s*//\s*Methods\s*\n", body)
    field_text = body[: methods_idx.start()] if methods_idx else body

    # Standard field pattern: internal static int NAME; // 0xNN
    field_re = re.compile(
        r"^\s*(?P<modifiers>(?:internal|private|public|protected)(?:\s+(?:static|readonly|const))*)\s+"
        r"(?P<type>[\w\[\]<>.,\s]+?)\s+"
        r"(?P<name>[\w<>.]+)\s*(?:=\s*(?P<default>[^;]+))?\s*;\s*//\s*(?P<offset>0x[0-9A-Fa-f]+)\s*$",
        re.MULTILINE,
    )
    for m in field_re.finditer(field_text):
        mods = m.group("modifiers").split()
        is_static = "static" in mods
        ftype = m.group("type").strip()
        name = m.group("name").strip()
        offset = int(m.group("offset"), 16)
        fields.append({
            "name": name,
            "type": ftype,
            "offset": offset,
            "static": is_static,
        })

    # Enum value parsing — only for actual enums. Non-enum classes may have
    # "public const int X = 5;" constants that are NOT offsets; parsing them
    # here would misinterpret constant values as field offsets.
    if is_enum:
        # Il2CppDumper emits enum members as
        #   "public const <EnumType> <Name> = <Value>;"
        enum_re = re.compile(
            r"^\s*(?P<modifiers>(?:internal|private|public|protected)(?:\s+(?:static|readonly|const))*)\s+"
            r"(?P<type>[\w\[\]<>.,\s]+?)\s+"
            r"(?P<name>\w+)\s*=\s*(?P<value>-?\d+)\s*;\s*$",
            re.MULTILINE,
        )
        for m in enum_re.finditer(field_text):
            name = m.group("name").strip()
            value = int(m.group("value"))
            # Skip if already captured as a regular field (e.g. value__)
            if any(f["name"] == name for f in fields):
                continue
            fields.append({
                "name": name,
                "type": m.group("type").strip(),
                "offset": value,  # For enums, offset stores the value
                "static": True,
            })

        # Bare enum form: "Name = Value;" (no type, no offset comment)
        bare_enum_re = re.compile(
            r"^\s*(?P<name>\w+)\s*=\s*(?P<value>-?\d+)\s*;\s*$",
            re.MULTILINE,
        )
        for m in bare_enum_re.finditer(field_text):
            name = m.group("name").strip()
            value = int(m.group("value"))
            if any(f["name"] == name for f in fields):
                continue
            fields.append({
                "name": name,
                "type": "int",
                "offset": value,
                "static": True,
            })
    return fields


def parse_properties(body: str) -> list[dict]:
    """Parse // Properties section if it exists."""
    props = []
    # Find // Properties section, which sits between // Fields and // Methods.
    m = re.search(r"\n\s*//\s*Properties\s*\n", body)
    if not m:
        return props

    start = m.end()
    # Stop at // Methods or end of body
    methods_m = re.search(r"\n\s*//\s*Methods\s*\n", body[start:])
    prop_text = body[start : start + methods_m.start()] if methods_m else body[start:]

    prop_re = re.compile(
        r"^\s*(?:\[[^\]]+\]\s*)*"
        r"(?P<modifiers>(?:internal|private|public|protected)(?:\s+(?:static|readonly|const))*)\s+"
        r"(?P<type>[\w\[\]<>.,\s]+?)\s+"
        r"(?P<name>[\w<>.]+)\s*\{\s*"
        r"(?P<getter>get)?\s*;?\s*(?P<setter>set)?\s*;?\s*"
        r"\}\s*$",


        re.MULTILINE,
    )

    for m in prop_re.finditer(prop_text):
        mods = m.group("modifiers").split()
        props.append({
            "name": m.group("name").strip(),
            "type": m.group("type").strip(),
            "get": bool(m.group("getter")),
            "set": bool(m.group("setter")),
            "static": "static" in mods,
        })
    return props


def parse_methods(body: str) -> list[dict]:
    """Parse // Methods section with full signatures including parameter types."""
    methods = []
    # Match RVA/Offset/VA line followed by the method header line.
    rva_re = re.compile(
        r"^\s*//\s*RVA:\s*0x(?P<rva>[0-9A-Fa-f]+)\s+Offset:\s*0x(?P<offset>[0-9A-Fa-f]+)\s+VA:\s*0x(?P<va>[0-9A-Fa-f]+)\s*$",
        re.MULTILINE,
    )
    for rva in rva_re.finditer(body):
        # The method signature is on one of the next few non-blank, non-comment lines.
        start = rva.end()
        rest = body[start:]
        header = None
        for line in rest.splitlines():
            line = line.strip()
            if not line or line.startswith("//"):
                continue
            if "(" in line:
                header = line
                break
        if not header:
            continue

        # Extract return type and name from the part before '('.
        paren = header.find("(")
        if paren == -1:
            continue
        header_part = header[:paren].strip()
        args_part = header[paren + 1: header.rfind(")")]

        tokens = header_part.split()
        if len(tokens) < 2:
            continue
        method_name = tokens[-1]
        ret_and_mods = " ".join(tokens[:-1])

        # Separate return type from modifiers
        ret_type = ""
        modifiers = []
        for t in tokens[:-1]:
            if t in ("internal", "private", "public", "protected", "static",
                      "virtual", "override", "abstract", "sealed", "extern", "new"):
                modifiers.append(t)
            else:
                ret_type = (ret_type + " " + t).strip() if ret_type else t

        # Parse args into structured list
        parsed_args = []
        if args_part.strip():
            for arg in _split_args(args_part):
                arg = arg.strip()
                if not arg:
                    continue
                # Strip default values: "float X = 1024" -> "float X"
                eq_idx = arg.find("=")
                if eq_idx != -1:
                    arg = arg[:eq_idx].strip()
                arg_tokens = arg.split()
                if len(arg_tokens) >= 2:
                    parsed_args.append({
                        "type": arg_tokens[-2],
                        "name": arg_tokens[-1],
                        "modifiers": [m for m in arg_tokens[:-2] if m in ("ref", "out", "in", "params")],
                    })
                else:
                    parsed_args.append({"type": arg, "name": "", "modifiers": []})

        methods.append({
            "name": method_name,
            "ret_and_mods": ret_and_mods,
            "return_type": ret_type,
            "modifiers": modifiers,
            "args": args_part.strip(),
            "parsed_args": parsed_args,
            "rva": int(rva.group("rva"), 16),
            "offset": int(rva.group("offset"), 16),
            "va": int(rva.group("va"), 16),
        })
    return methods


def _split_args(s: str) -> list[str]:
    """Split method args on commas, respecting nested generics/arrays."""
    args = []
    depth = 0
    start = 0
    for i, ch in enumerate(s):
        if ch in "<[(":
            depth += 1
        elif ch in ">])":
            depth -= 1
        elif ch == "," and depth == 0:
            args.append(s[start:i])
            start = i + 1
    if start < len(s):
        args.append(s[start:])
    return args


def csharp_identifier(name: str) -> str:
    """Make a safe C# identifier from a class/method/field name."""
    # . in original class name becomes _ for generated SDK class name.
    # Also sanitize [], <>, spaces, and other invalid chars from auto-generated aliases.
    result = name.replace(".", "_").replace("<", "_").replace(">", "_").replace(" ", "_")
    result = result.replace("[", "_").replace("]", "_").replace(",", "_")
    result = result.replace("{", "_").replace("}", "_").replace("(", "_").replace(")", "_")
    result = result.replace("+", "_").replace("-", "_").replace("*", "_")
    result = result.replace("&", "_").replace("@", "_").replace(":", "_")
    result = result.replace("?", "_")
    # Collapse multiple underscores
    while "__" in result:
        result = result.replace("__", "_")
    # Remove leading/trailing underscores
    result = result.strip("_")
    # Ensure it starts with a letter or underscore
    if result and result[0].isdigit():
        result = "_" + result
    return result if result else "_"


def vector_suffixes(ftype: str) -> list[tuple[str, int]] | None:
    """Return (component, delta) pairs for vector-like value types."""
    if ftype in ("Vector3", "UnityEngine.Vector3"):
        return [("X", 0), ("Y", 4), ("Z", 8)]
    if ftype in ("Vector2", "UnityEngine.Vector2"):
        return [("X", 0), ("Y", 4)]
    if ftype in ("Color", "UnityEngine.Color"):
        return [("R", 0), ("G", 4), ("B", 8), ("A", 12)]
    if ftype in ("Rect", "UnityEngine.Rect"):
        return [("X", 0), ("Y", 4), ("W", 8), ("H", 12)]
    if ftype in ("Quaternion", "UnityEngine.Quaternion"):
        return [("X", 0), ("Y", 4), ("Z", 8), ("W", 12)]
    return None


def write_class_sdk(class_name: str, fields: list[dict], properties: list[dict], methods: list[dict],
                    out_dir: Path, is_enum: bool = False, typedef_index: int = -1) -> tuple[set[str], dict[str, list[str]], set[str]]:
    safe_name = csharp_identifier(class_name)
    file = out_dir / f"{safe_name}.cs"
    field_names: set[str] = set()
    method_names: dict[str, list[str]] = {}
    prop_names: set[str] = set()
    sb = []
    sb.append("// Auto-generated by Tools/generate_sdk.py")
    sb.append("// Do not edit manually; regenerate after re-dumping the game.")
    sb.append("namespace BlockpostTrainer.Sdk.Raw")
    sb.append("{")
    sb.append(f"    internal static class {safe_name}")
    sb.append("    {")
    if typedef_index >= 0:
        sb.append(f"        public const int TypeDefIndex = {typedef_index};")
    sb.append(f"        public const string OriginalName = \"{class_name}\";")
    sb.append("")
    sb.append("        /// <summary>")
    if is_enum:
        sb.append(f"        /// Enum values for {class_name}.")
    else:
        sb.append(f"        /// Field and static-field offsets for {class_name}.")
    sb.append("        /// </summary>")
    sb.append("        public static class Offsets")
    sb.append("        {")

    instance_fields = [f for f in fields if not f["static"]]
    static_fields = [f for f in fields if f["static"]]

    if is_enum:
        # For enums, emit value__ as the backing field and the named members as
        # integer constants. Enum members are static const in the dump.
        seen_enum = {}
        sb.append("            // Backing field")
        for f in instance_fields:
            cname = safe_member_name(f["name"], seen_enum)
            field_names.add(cname)
            sb.append(f"            public const int {cname} = {offset_literal(f['offset'])}; // {f['type']}")
        if static_fields:
            sb.append("")
            sb.append("            // Enum values")
            for f in static_fields:
                cname = safe_member_name(f["name"], seen_enum)
                field_names.add(cname)
                sb.append(f"            public const int {cname} = {offset_literal(f['offset'])}; // {f['type']}")
    else:
        # Use a single seen set for all offset constants so a static and
        # instance field with the same obfuscated name cannot collide.
        seen_offsets = {}
        if static_fields:
            sb.append("            // Static fields")
            for f in static_fields:
                cname = safe_member_name(f["name"], seen_offsets)
                comp = vector_suffixes(f["type"])
                if comp:
                    sb.append(f"            public const int {cname} = {offset_literal(f['offset'])}; // {f['type']} ({SIZE_HINTS.get(f['type'], 4)} bytes)")
                    field_names.add(cname)
                    for comp_name, delta in comp:
                        comp_cname = f"{cname}_{comp_name}"
                        field_names.add(comp_cname)
                        sb.append(f"            public const int {comp_cname} = {offset_literal(f['offset'] + delta)};")
                else:
                    sb.append(f"            public const int {cname} = {offset_literal(f['offset'])}; // {f['type']}")
                    field_names.add(cname)

        if instance_fields:
            if static_fields:
                sb.append("")
            sb.append("            // Instance fields")
            for f in instance_fields:
                cname = safe_member_name(f["name"], seen_offsets)
                comp = vector_suffixes(f["type"])
                if comp:
                    sb.append(f"            public const int {cname} = {offset_literal(f['offset'])}; // {f['type']} ({SIZE_HINTS.get(f['type'], 4)} bytes)")
                    field_names.add(cname)
                    for comp_name, delta in comp:
                        comp_cname = f"{cname}_{comp_name}"
                        field_names.add(comp_cname)
                        sb.append(f"            public const int {comp_cname} = {offset_literal(f['offset'] + delta)};")
                else:
                    sb.append(f"            public const int {cname} = {offset_literal(f['offset'])}; // {f['type']}")
                    field_names.add(cname)

    sb.append("        }")
    sb.append("")

    if properties:
        sb.append("        /// <summary>")
        sb.append(f"        /// Property names for {class_name}.")
        sb.append("        /// </summary>")
        sb.append("        public static class Properties")
        sb.append("        {")
        seen_props = {}
        for p in properties:
            cname = safe_member_name(p["name"], seen_props)
            prop_names.add(cname)
            gs = []
            if p["get"]:
                gs.append("get")
            if p["set"]:
                gs.append("set")
            gs_str = "/".join(gs) if gs else "?"
            sb.append(f"            public const string {cname} = \"{p['name']}\"; // {p['type']} {{ {gs_str} }}")
        sb.append("        }")
        sb.append("")

    sb.append("        /// <summary>")
    sb.append(f"        /// Method virtual addresses (VAs) for {class_name}.")
    sb.append("        /// </summary>")
    sb.append("        public static class Methods")
    sb.append("        {")
    seen_methods = {}
    for m in methods:
        base_mname = csharp_identifier(m["name"])
        if not base_mname or base_mname in ("get", "set", "add", "remove"):
            # skip broken/special names
            continue
        mname = safe_member_name(m["name"], seen_methods)
        method_names.setdefault(base_mname, []).append(mname)
        # Full signature in comment for reverse engineering.
        sig = f"{m['ret_and_mods']} {m['name']}({m['args']})"
        # Add param type summary for methods with non-trivial args
        param_types = []
        if "parsed_args" in m:
            for arg in m["parsed_args"]:
                if arg["modifiers"]:
                    param_types.append(f"{' '.join(arg['modifiers'])} {arg['type']} {arg['name']}")
                else:
                    param_types.append(f"{arg['type']} {arg['name']}")
        param_summary = ", ".join(param_types) if param_types else m["args"]
        ret_type = m.get("return_type", "")
        if ret_type and ret_type not in ("void", ""):
            sb.append(f"            /// <summary>{m['name']}({param_summary}) -> {ret_type}</summary>")
        elif param_types:
            sb.append(f"            /// <summary>{m['name']}({param_summary})</summary>")
        sb.append(f"            public const uint {mname} = {hex(m['va'])}; // {sig}")
    sb.append("        }")
    sb.append("    }")
    sb.append("}")

    file.write_text("\n".join(sb), encoding="utf-8")
    return field_names, method_names, prop_names


def write_aliases(aliases: dict, out_dir: Path, valid_fields: dict = None, valid_methods: dict = None, valid_props: dict = None) -> None:
    if not aliases:
        return
    file = out_dir / "Aliases.cs"
    sb = []
    sb.append("// Auto-generated by Tools/generate_sdk.py from Tools/sdk_aliases.json")
    sb.append("namespace BlockpostTrainer.Sdk")
    sb.append("{")
    sb.append("    /// <summary>")
    sb.append("    /// Human-readable aliases for the most important game classes/fields/methods.")
    sb.append("    /// </summary>")
    sb.append("    public static class Aliases")
    sb.append("    {")
    skipped = 0
    seen_humans = {}
    # Sort so a class whose original name matches its human name gets the un-suffixed alias.
    def _alias_sort_key(item):
        orig, mapping = item
        return (0 if csharp_identifier(orig) == csharp_identifier(mapping.get("HumanClass", orig)) else 1, orig)
    for orig_class, mapping in sorted(aliases.items(), key=_alias_sort_key):
        safe_orig = csharp_identifier(orig_class)
        human = mapping.get("HumanClass", orig_class)
        safe_human = unique_name(human, seen_humans)
        # Skip if the Raw class doesn't exist (not in TARGET_CLASSES and not generated).
        # A class is considered generated if it has fields, methods, or properties in the SDK.
        if valid_fields is not None and safe_orig not in valid_fields and safe_orig not in valid_methods and safe_orig not in valid_props:
            # Skip classes that don't have a generated Raw file
            # (e.g. Unity engine types like Texture2D, GameObject, etc.)
            skipped += 1
            continue
        sb.append(f"        public static class {safe_human}")
        sb.append("        {")
        seen_alias_members = {}

        def resolve_target(name: str, valid: set[str] | None) -> str | None:
            """Map an alias target to the generated C# member name, handling reserved names."""
            if not valid:
                return None
            c = csharp_identifier(name)
            if c in valid:
                return c
            if c in RESERVED_CSHARP_NAMES and f"{c}_" in valid:
                return f"{c}_"
            return None

        fields = mapping.get("Fields", {})
        methods = mapping.get("Methods", {})
        notes = mapping.get("Notes", {})
        class_valid_fields = valid_fields.get(safe_orig, set()) if valid_fields else None
        class_valid_methods = valid_methods.get(safe_orig, {}) if valid_methods else None

        # Resolve field aliases.
        for human_name, orig_name in fields.items():
            safe_h = safe_member_name(human_name, seen_alias_members)
            safe_o = resolve_target(orig_name, class_valid_fields)
            if not safe_o:
                skipped += 1
                continue
            note = notes.get(human_name)
            if note:
                sb.append(f"            /// <summary>{note}</summary>")
            sb.append(f"            public const int {safe_h} = Raw.{safe_orig}.Offsets.{safe_o};")

        # Resolve method aliases. We do this in two passes:
        #   1) aliases whose name matches the generated overload index (e.g. Foo or Foo_1)
        #      get the exact generated member they name;
        #   2) all other aliases fall back to the first unused generated member.
        # This makes overloaded method mapping deterministic regardless of JSON order.
        method_plan = []
        for human_name, orig_name in methods.items():
            safe_h = safe_member_name(human_name, seen_alias_members)
            safe_o = csharp_identifier(orig_name)
            method_plan.append((human_name, safe_h, safe_o))

        used_methods: dict[str, set[str]] = {}
        assigned_methods: list[tuple[str, str, str, str]] = []  # (human_name, safe_h, safe_o, gen_mname)
        assigned_set: set[tuple[str, str, str]] = set()

        def _method_index_for_alias(safe_h: str, safe_o: str) -> int | None:
            """If the alias name encodes an overload index (Foo -> 0, Foo_1 -> 1, ...), return it."""
            if safe_h == safe_o:
                return 0
            if safe_h.startswith(f"{safe_o}_"):
                suffix = safe_h[len(safe_o) + 1 :]
                if suffix.isdigit():
                    return int(suffix)
            return None

        def _assign_method(safe_h: str, safe_o: str) -> str | None:
            gen_list = class_valid_methods.get(safe_o, []) if class_valid_methods else []
            if not gen_list:
                return None
            used = used_methods.setdefault(safe_o, set())
            idx = _method_index_for_alias(safe_h, safe_o)
            if idx is not None and 0 <= idx < len(gen_list):
                gen_mname = gen_list[idx]
                if gen_mname not in used:
                    used.add(gen_mname)
                    return gen_mname
            # Fallback to the first generated member that hasn't been used yet.
            for gen_mname in gen_list:
                if gen_mname not in used:
                    used.add(gen_mname)
                    return gen_mname
            return None

        # First pass: index-matched aliases.
        for human_name, safe_h, safe_o in method_plan:
            if _method_index_for_alias(safe_h, safe_o) is not None:
                gen_mname = _assign_method(safe_h, safe_o)
                if gen_mname:
                    assigned_methods.append((human_name, safe_h, safe_o, gen_mname))
                    assigned_set.add((human_name, safe_h, safe_o))

        # Second pass: remaining aliases.
        for human_name, safe_h, safe_o in method_plan:
            if (human_name, safe_h, safe_o) in assigned_set:
                continue
            gen_mname = _assign_method(safe_h, safe_o)
            if gen_mname:
                assigned_methods.append((human_name, safe_h, safe_o, gen_mname))
                assigned_set.add((human_name, safe_h, safe_o))
            else:
                skipped += 1

        for human_name, safe_h, safe_o, gen_mname in assigned_methods:
            note = notes.get(human_name)
            if note:
                sb.append(f"            /// <summary>{note}</summary>")
            sb.append(f"            public const uint {safe_h} = Raw.{safe_orig}.Methods.{gen_mname};")
        # Properties are emitted as name strings, for reflection-based access.
        # Only emit if the Raw class has a Properties section.
        # We check by looking at whether the class has any properties in the dump.
        # Since we don't have that info here, we wrap in a check.
        class_valid_props = valid_props.get(safe_orig, set()) if valid_props else None
        for human_name, orig_name in mapping.get("Properties", {}).items():
            safe_h = safe_member_name(human_name, seen_alias_members)
            safe_o = resolve_target(orig_name, class_valid_props)
            if not safe_o:
                skipped += 1
                continue
            sb.append(f"            public const string {safe_h} = Raw.{safe_orig}.Properties.{safe_o};")
        sb.append("        }")
        sb.append("")
    sb.append("    }")
    sb.append("}")
    file.write_text("\n".join(sb), encoding="utf-8")
    if skipped:
        print(f"  (skipped {skipped} alias entries referencing non-existent fields/methods/classes)")


def load_aliases() -> dict:
    if not ALIASES_FILE.exists():
        return {}
    return json.loads(ALIASES_FILE.read_text(encoding="utf-8"))


def write_sdk_index(class_index: list[dict], out_dir: Path) -> None:
    """Write SdkIndex.cs with lookups for all generated raw classes."""
    file = out_dir / "SdkIndex.cs"
    sb = []
    sb.append("// Auto-generated by Tools/generate_sdk.py")
    sb.append("namespace BlockpostTrainer.Sdk")
    sb.append("{")
    sb.append("    /// <summary>")
    sb.append("    /// Index of every generated raw class, its original Il2Cpp name, human alias and TypeDefIndex.")
    sb.append("    /// </summary>")
    sb.append("    public static class SdkIndex")
    sb.append("    {")
    sb.append("        public static class ByOriginalName")
    sb.append("        {")
    for entry in class_index:
        safe = csharp_identifier(entry["original"])
        sb.append(f"            public const string {safe} = \"{entry['safe']}\";")
    sb.append("        }")
    sb.append("")
    sb.append("        public static class ByHumanName")
    sb.append("        {")
    seen_human = {}
    for entry in class_index:
        human = unique_name(entry["human"], seen_human)
        sb.append(f"            public const string {human} = \"{entry['safe']}\";")
    sb.append("        }")
    sb.append("")
    sb.append("        public static class ByTypeDefIndex")
    sb.append("        {")
    for entry in class_index:
        if entry["tdi"] >= 0:
            safe = csharp_identifier(entry["original"])
            sb.append(f"            public const string Tdi{entry['tdi']} = \"{entry['safe']}\"; // {entry['original']}")
    sb.append("        }")
    sb.append("    }")
    sb.append("}")
    file.write_text("\n".join(sb), encoding="utf-8")


def main() -> int:
    print(f"Reading {DUMP_CS} ...")
    text = load_dump_text()
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    aliases = load_aliases()

    # Generate SDK for target classes plus every class that has alias data.
    class_names = list(dict.fromkeys(TARGET_CLASSES + list(aliases.keys())))

    # Track valid field/method/property names per class for alias validation
    valid_fields: dict[str, set[str]] = {}
    valid_methods: dict[str, set[str]] = {}
    valid_props: dict[str, set[str]] = {}

    sdk_index = []
    for class_name in class_names:
        body, line, kind, tdi = find_class_block(text, class_name)
        if body is None:
            print(f"  ! {class_name}: not found")
            continue
        print(f"  + {class_name} at line {line} ({kind}) tdi={tdi}")
        fields = parse_fields(body, is_enum=(kind == "enum"))
        properties = parse_properties(body)
        methods = parse_methods(body)
        safe_name = csharp_identifier(class_name)
        class_fields, class_methods, class_props = write_class_sdk(
            class_name, fields, properties, methods, OUT_DIR,
            is_enum=(kind == "enum"), typedef_index=tdi)

        sdk_index.append({"original": class_name, "safe": safe_name, "tdi": tdi,
                          "human": aliases.get(class_name, {}).get("HumanClass", class_name)})

        # Record the exact generated member names so aliases can be bound to the
        # right overload and reserved names (Equals, etc.) resolve correctly.
        valid_fields[safe_name] = class_fields
        valid_methods[safe_name] = class_methods
        valid_props[safe_name] = class_props

    write_aliases(aliases, OUT_DIR, valid_fields, valid_methods, valid_props)
    write_sdk_index(sdk_index, OUT_DIR)
    print(f"Done. Generated {len(sdk_index)} classes in {OUT_DIR}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
