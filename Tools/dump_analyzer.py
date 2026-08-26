#!/usr/bin/env python3
"""Comprehensive dump.cs analyzer.

Extracts from Il2CppDumper's dump.cs:
  - All classes/enums/structs with their fields, properties, methods (full signatures)
  - String literals (PlayerPrefs keys, log messages, config keys)
  - Cross-references: which classes reference which types in field/method signatures
  - Method parameter types and return types
  - Enum values
  - Inheritance hierarchy

Outputs JSON databases to Tools/analysis/ for downstream tooling.
"""
import json
import re
import sys
from pathlib import Path
from collections import defaultdict

ROOT = Path(__file__).resolve().parent.parent
DUMP_CS = ROOT / ".tools" / "Il2CppDumper" / "dump.cs"
OUT_DIR = ROOT / "Tools" / "analysis"

# ── Parsing ────────────────────────────────────────────────────────────────

TYPE_DEF_RE = re.compile(
    r"^(?P<attrs>(?:\[[^\]]+\]\s*)*)"
    r"(?P<mods>(?:internal|public|private|protected|sealed|abstract|static|partial)\s+)*"
    r"(?P<kind>class|enum|struct|interface)\s+"
    r"(?P<name>[\w.<>]+)"
    r"(?P<basepart>\s*:\s*[^/]+?)?"
    r"\s*(?://.*)?$",
    re.MULTILINE,
)

FIELD_RE = re.compile(
    r"^\s*(?P<attrs>(?:\[[^\]]+\]\s*)*)"
    r"(?P<modifiers>(?:internal|private|public|protected|static|readonly|const|volatile)\s+)*"
    r"(?P<type>[\w\[\]<>.,\s]+?)\s+"
    r"(?P<name>[\w<>.]+)\s*(?:=\s*(?P<default>[^;]+))?;\s*"
    r"(?://\s*(?P<offset>0x[0-9A-Fa-f]+))?\s*$",
    re.MULTILINE,
)

PROP_RE = re.compile(
    r"^\s*(?P<attrs>(?:\[[^\]]+\]\s*)*)"
    r"(?P<modifiers>(?:internal|private|public|protected|static|readonly|const)\s+)*"
    r"(?P<type>[\w\[\]<>.,\s]+?)\s+"
    r"(?P<name>[\w<>.]+)\s*\{\s*"
    r"(?P<getter>get)?\s*;?\s*(?P<setter>set)?\s*;?\s*"
    r"\}\s*$",
    re.MULTILINE,
)

RVA_RE = re.compile(
    r"^\s*//\s*RVA:\s*0x(?P<rva>[0-9A-Fa-f]+)\s+Offset:\s*0x(?P<offset>[0-9A-Fa-f]+)\s+VA:\s*0x(?P<va>[0-9A-Fa-f]+)\s*$",
    re.MULTILINE,
)

STRING_LITERAL_RE = re.compile(r'"([^"]{2,})"')


def brace_count(text: str) -> tuple[str | None, int]:
    """Given text starting at the first character inside a {, return the substring
    up to the matching } and the number of bytes consumed."""
    depth = 1
    i = 0
    while i < len(text) and depth > 0:
        ch = text[i]
        if ch == "'" or ch == '"':
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


def find_all_type_defs(text: str) -> list[dict]:
    """Find all top-level type definitions (class/enum/struct/interface).

    If the same simple type name appears multiple times (e.g. the game's
    `Console` vs `System.Console`), keep the first occurrence to stay
    consistent with generate_sdk.py's find_class_block.
    """
    results = []
    seen = set()
    for m in TYPE_DEF_RE.finditer(text):
        kind = m.group("kind")
        name = m.group("name").strip()
        if name in seen:
            continue
        seen.add(name)
        basepart = m.group("basepart")
        base_list = []
        if basepart:
            # Strip the leading ':' and any trailing generic where-clause.
            basepart = basepart.strip().lstrip(":").strip()
            where_idx = basepart.find(" where ")
            if where_idx != -1:
                basepart = basepart[:where_idx]
            base_list = [b.strip() for b in basepart.split(",") if b.strip()]

        after = text[m.end():]
        brace_match = re.search(r"^\s*\{\s*$", after, re.MULTILINE)
        if not brace_match:
            continue
        body_start = m.end() + brace_match.end()
        body, consumed = brace_count(text[body_start:])
        if body is None:
            continue
        line_no = text[:body_start].count("\n") + 1
        results.append({
            "name": name,
            "kind": kind,
            "bases": base_list,
            "body": body,
            "line": line_no,
            "attrs": m.group("attrs").strip() if m.group("attrs") else "",
        })
    return results


def parse_section(body: str, section_name: str) -> str:
    """Extract a // Section body between section markers."""
    start_re = re.compile(rf"\n\s*//\s*{section_name}\s*\n", re.IGNORECASE)
    m = start_re.search(body)
    if not m:
        return ""
    start = m.end()
    # Stop at next // Section or end
    next_section = re.search(r"\n\s*//\s*(?:Fields|Properties|Methods|Nested)\s*\n", body[start:])
    if next_section:
        return body[start : start + next_section.start()]
    return body[start:]


def parse_fields_section(body: str, is_enum: bool = False) -> list[dict]:
    """Parse the // Fields section."""
    fields = []
    field_text = parse_section(body, "Fields")

    # Standard field with offset: internal static int NAME; // 0xNN
    for m in FIELD_RE.finditer(field_text):
        mods = (m.group("modifiers") or "").split()
        is_static = "static" in mods
        ftype = m.group("type").strip()
        name = m.group("name").strip()
        offset_str = m.group("offset")
        offset = int(offset_str, 16) if offset_str else None
        fields.append({
            "name": name,
            "type": ftype,
            "offset": offset,
            "static": is_static,
            "modifiers": mods,
            "default": m.group("default").strip() if m.group("default") else None,
        })

    # Enum values
    if is_enum:
        enum_re = re.compile(
            r"^\s*public\s+const\s+(?P<type>[\w.<>\[\]]+)\s+"
            r"(?P<name>\w+)\s*=\s*(?P<value>-?\d+)\s*;\s*$",
            re.MULTILINE,
        )
        for m in enum_re.finditer(field_text):
            name = m.group("name").strip()
            if any(f["name"] == name for f in fields):
                continue
            fields.append({
                "name": name,
                "type": m.group("type").strip(),
                "offset": int(m.group("value")),
                "static": True,
                "modifiers": ["public", "const"],
                "default": None,
            })
        bare_re = re.compile(r"^\s*(?P<name>\w+)\s*=\s*(?P<value>-?\d+)\s*;\s*$", re.MULTILINE)
        for m in bare_re.finditer(field_text):
            name = m.group("name").strip()
            if any(f["name"] == name for f in fields):
                continue
            fields.append({
                "name": name,
                "type": "int",
                "offset": int(m.group("value")),
                "static": True,
                "modifiers": [],
                "default": None,
            })
    return fields


def parse_properties_section(body: str) -> list[dict]:
    """Parse the // Properties section."""
    props = []
    prop_text = parse_section(body, "Properties")
    for m in PROP_RE.finditer(prop_text):
        mods = (m.group("modifiers") or "").split()
        props.append({
            "name": m.group("name").strip(),
            "type": m.group("type").strip(),
            "get": bool(m.group("getter")),
            "set": bool(m.group("setter")),
            "static": "static" in mods,
            "modifiers": mods,
        })
    return props


def parse_methods_section(body: str) -> list[dict]:
    """Parse the // Methods section with full signatures."""
    methods = []
    method_text = parse_section(body, "Methods")

    # Find all RVA comments and their following method signatures
    for rva_m in RVA_RE.finditer(method_text):
        rva = int(rva_m.group("rva"), 16)
        offset = int(rva_m.group("offset"), 16)
        va = int(rva_m.group("va"), 16)

        # Find the method signature after this RVA comment
        rest = method_text[rva_m.end():]
        sig = None
        for line in rest.splitlines():
            line = line.strip()
            if not line or line.startswith("//"):
                continue
            if "{" in line:
                sig = line.rstrip("{").strip()
                break
        if not sig:
            continue

        # Parse: modifiers return_type Name(args) {
        paren = sig.find("(")
        if paren == -1:
            continue
        header = sig[:paren].strip()
        args_str = sig[paren + 1 : sig.rfind(")")].strip()

        tokens = header.split()
        if len(tokens) < 2:
            continue

        method_name = tokens[-1]
        ret_and_mods = tokens[:-1]

        modifiers = []
        ret_type = ""
        for t in ret_and_mods:
            if t in ("internal", "private", "public", "protected", "static",
                      "virtual", "override", "abstract", "sealed", "extern",
                      "new", "async"):
                modifiers.append(t)
            else:
                ret_type = (ret_type + " " + t).strip() if ret_type else t

        # Parse args
        args = []
        if args_str:
            for arg in split_args(args_str):
                arg = arg.strip()
                if not arg:
                    continue
                # Strip default values: "float X = 1024" -> "float X"
                eq_idx = arg.find("=")
                if eq_idx != -1:
                    arg = arg[:eq_idx].strip()
                # Handle ref/out/in params
                arg_tokens = arg.split()
                if len(arg_tokens) >= 2:
                    arg_type = arg_tokens[-2]
                    arg_name = arg_tokens[-1]
                    arg_mods = [m for m in arg_tokens[:-2] if m in ("ref", "out", "in", "params")]
                    args.append({"type": arg_type, "name": arg_name, "modifiers": arg_mods})
                else:
                    args.append({"type": arg, "name": "", "modifiers": []})

        methods.append({
            "name": method_name,
            "return_type": ret_type,
            "modifiers": modifiers,
            "args": args,
            "rva": rva,
            "offset": offset,
            "va": va,
            "signature": sig,
        })
    return methods


def split_args(s: str) -> list[str]:
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


def extract_string_literals(text: str) -> list[dict]:
    """Extract all string literals with their line numbers and enclosing class."""
    literals = []
    # Find all type defs to know class boundaries
    type_defs = find_all_type_defs(text)

    for m in STRING_LITERAL_RE.finditer(text):
        val = m.group(1)
        # Skip short strings and code-like strings
        if len(val) < 3:
            continue
        # Skip if it's in a comment
        line_start = text.rfind("\n", 0, m.start()) + 1
        line = text[line_start : text.find("\n", m.end())]
        stripped = line.lstrip()
        if stripped.startswith("//"):
            continue
        line_no = text[:m.start()].count("\n") + 1
        # Find enclosing class
        enclosing = ""
        for td in type_defs:
            if td["line"] <= line_no:
                enclosing = td["name"]
            else:
                break
        literals.append({
            "value": val,
            "line": line_no,
            "class": enclosing,
        })
    return literals


def extract_type_refs(type_str: str) -> list[str]:
    """Extract referenced type names from a type string like 'List<KBBBHJDINCB>'."""
    if not type_str:
        return []
    # Remove array brackets and generic args, extract identifiers
    refs = set()
    # Match identifiers that look like obfuscated names or known types
    for m in re.finditer(r"\b([A-Z][A-Za-z0-9_]{4,}|[A-Z][A-Za-z0-9_]+)\b", type_str):
        ref = m.group(1)
        # Skip primitive types and keywords
        if ref in ("Vector3", "Vector2", "Vector4", "Color", "Rect", "Quaternion",
                    "Matrix4x4", "Transform", "GameObject", "Camera", "Component",
                    "MonoBehaviour", "Rigidbody", "Collider", "AudioClip", "AudioSource",
                    "Material", "Texture", "Texture2D", "Mesh", "MeshRenderer",
                    "MeshFilter", "SkinnedMeshRenderer", "Animator", "Animation",
                    "AnimationClip", "ParticleSystem", "Light", "RectTransform",
                    "Canvas", "Image", "Text", "Button", "Slider", "Toggle",
                    "Scrollbar", "ScrollRect", "InputField", "Dropdown",
                    "String", "Int32", "Int64", "UInt32", "Boolean", "Single",
                    "Double", "Byte", "SByte", "Int16", "UInt16", "UInt64",
                    "Object", "List", "Dictionary", "Array", "IEnumerable",
                    "IEnumerator", "Action", "Func", "Predicate", "Queue",
                    "Stack", "HashSet", "KeyValuePair", "Tuple",
                    "Plane", "Ray", "RaycastHit", "Bounds", "LayerMask",
                    "Input", "Physics", "Time", "Screen", "Application",
                    "PlayerPrefs", "Cursor", "QualitySettings", "RenderSettings",
                    "Shader", "ShaderUtil", "Graphics", "GL", "AsyncOperation",
                    "WaitForSeconds", "WaitForEndOfFrame", "Coroutine",
                    "UnityWebRequest", "WWW", "Socket", "TcpClient",
                    "IPEndPoint", "IPAddress", "Thread", "Mutex", "Monitor",
                    "Exception", "Debug", "Mathf", "Random", "Path", "File",
                    "Directory", "Stream", "BinaryReader", "BinaryWriter",
                    "StreamReader", "StreamWriter", "MemoryStream", "Encoding",
                    "Convert", "BitConverter", "GC", "Console", "Math",
                    "DateTime", "TimeSpan", "Stopwatch", "Task", "Thread",
                    "StringComparison", "StringSplitOptions", "StringBuilder",
                    "Regex", "Match", "Group", "Capture", "Json",
                    "MonoBehaviour", "ScriptableObject", "Behaviour",
                    "Renderer", "LineRenderer", "TrailRenderer", "ParticleSystemRenderer",
                    "Sprite", "SpriteRenderer", "CanvasRenderer", "CanvasGroup",
                    "CanvasScaler", "GraphicRaycaster", "ContentSizeFitter",
                    "HorizontalLayoutGroup", "VerticalLayoutGroup", "GridLayoutGroup",
                    "LayoutElement", "Outline", "Shadow", "PositionAsUV1",
                    "AspectRatioFitter", "Button", "Scrollbar", "ScrollRect",
                    "Selectable", "PointerEventData", "BaseEventData",
                    "EventSystem", "EventTrigger", "InputModule",
                    "StandaloneInputModule", "TouchInputModule",
                    "JsonUtility", "PlayerData", "NetworkBehaviour",
                    "NetworkClient", "NetworkServer", "NetworkConnection",
                    "NetworkMessage", "MessageBase", "SyncList", "SyncVar",
                    "Command", "ClientRpc", "TargetRpc", "ServerCallback",
                    "ClientCallback", "Server", "Client", "LocalPlayer",
                    "NetworkReader", "NetworkWriter", "NetworkReaderPool",
                    "NetworkWriterPool", "NetworkSerializer", "NetworkDeserializer",
                    "float", "int", "bool", "string", "byte", "char",
                    "double", "long", "short", "uint", "ulong", "ushort",
                    "object", "void", "sbyte", "decimal"):
            continue
        refs.add(ref)
    return list(refs)


def build_cross_references(type_defs: list[dict]) -> dict:
    """Build a cross-reference database: class -> referenced classes."""
    xrefs = defaultdict(lambda: {"fields": set(), "methods": set(), "properties": set(), "bases": set(), "derived_by": set()})

    for td in type_defs:
        name = td["name"]
        # Bases
        for base in td["bases"]:
            xrefs[name]["bases"].add(base)
            xrefs[base]["derived_by"].add(name)

        # Fields
        fields = parse_fields_section(td["body"], is_enum=(td["kind"] == "enum"))
        for f in fields:
            for ref in extract_type_refs(f["type"]):
                xrefs[name]["fields"].add(ref)

        # Properties
        props = parse_properties_section(td["body"])
        for p in props:
            for ref in extract_type_refs(p["type"]):
                xrefs[name]["properties"].add(ref)

        # Methods
        methods = parse_methods_section(td["body"])
        for m in methods:
            for ref in extract_type_refs(m["return_type"]):
                xrefs[name]["methods"].add(ref)
            for arg in m["args"]:
                for ref in extract_type_refs(arg["type"]):
                    xrefs[name]["methods"].add(ref)

    # Convert sets to sorted lists
    result = {}
    for name, refs in xrefs.items():
        result[name] = {
            "fields": sorted(refs["fields"]) if "fields" in refs else [],
            "methods": sorted(refs["methods"]) if "methods" in refs else [],
            "properties": sorted(refs["properties"]) if "properties" in refs else [],
            "bases": sorted(refs["bases"]) if "bases" in refs else [],
            "derived_by": sorted(refs["derived_by"]) if "derived_by" in refs else [],
        }
    return result


# ── Main ───────────────────────────────────────────────────────────────────

def main() -> int:
    print(f"Reading {DUMP_CS} ...")
    text = DUMP_CS.read_text(encoding="utf-8", errors="ignore")
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    print("Finding all type definitions ...")
    type_defs = find_all_type_defs(text)
    print(f"  Found {len(type_defs)} type definitions")

    # Parse all types
    all_types = {}
    enum_count = 0
    class_count = 0
    for td in type_defs:
        is_enum = td["kind"] == "enum"
        fields = parse_fields_section(td["body"], is_enum=is_enum)
        properties = parse_properties_section(td["body"])
        methods = parse_methods_section(td["body"])
        all_types[td["name"]] = {
            "name": td["name"],
            "kind": td["kind"],
            "bases": td["bases"],
            "line": td["line"],
            "attrs": td["attrs"],
            "fields": fields,
            "properties": properties,
            "methods": methods,
            "field_count": len(fields),
            "property_count": len(properties),
            "method_count": len(methods),
        }
        if is_enum:
            enum_count += 1
        else:
            class_count += 1

    print(f"  {class_count} classes/structs, {enum_count} enums")

    # Save full type database
    print("Writing type_database.json ...")
    (OUT_DIR / "type_database.json").write_text(
        json.dumps(all_types, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )

    # Build cross-references
    print("Building cross-references ...")
    xrefs = build_cross_references(type_defs)
    (OUT_DIR / "cross_references.json").write_text(
        json.dumps(xrefs, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )

    # Extract string literals
    print("Extracting string literals ...")
    literals = extract_string_literals(text)
    # Filter to interesting strings (PlayerPrefs keys, log messages, config)
    interesting = [l for l in literals if
                    any(kw in l["value"].lower() for kw in [
                        "player", "weapon", "ammo", "fire", "reload", "health",
                        "armor", "skin", "loadout", "slot", "team", "kill",
                        "death", "spawn", "spectator", "chat", "menu", "hud",
                        "server", "client", "connect", "packet", "login",
                        "auth", "token", "session", "config", "setting",
                        "pref", "key", "save", "load", "map", "voxel",
                        "block", "build", "mode", "rank", "level", "xp",
                        "coin", "gold", "case", "crate", "shop", "buy",
                        "sell", "trade", "inventory", "item", "reward",
                        "achievement", "quest", "task", "mission", "battle",
                        "royale", "classic", "deathmatch", "flag", "bomb",
                        "point", "zone", "area", "safe", "danger", "storm",
                        "circle", "blue", "red", "green", "yellow",
                        "error", "warn", "info", "debug", "log",
                        "http", "url", "api", "endpoint",
                        "version", "build", "patch", "update",
                        "fps", "ping", "latency", "tick",
                    ]) or
                    l["value"].startswith("pp_") or
                    l["value"].startswith("playerpref") or
                    "_" in l["value"] and len(l["value"]) > 5
                   ]
    print(f"  {len(literals)} total string literals, {len(interesting)} interesting")
    (OUT_DIR / "string_literals.json").write_text(
        json.dumps({"all": literals[:5000], "interesting": interesting[:2000]}, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )

    # Summary stats
    print("\n=== Summary ===")
    print(f"Total types: {len(all_types)}")
    print(f"  Classes/structs: {class_count}")
    print(f"  Enums: {enum_count}")
    total_fields = sum(t["field_count"] for t in all_types.values())
    total_props = sum(t["property_count"] for t in all_types.values())
    total_methods = sum(t["method_count"] for t in all_types.values())
    print(f"Total fields: {total_fields}")
    print(f"Total properties: {total_props}")
    print(f"Total methods: {total_methods}")
    print(f"Total string literals: {len(literals)}")
    print(f"Output: {OUT_DIR}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
