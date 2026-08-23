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
    "Controll",
    "Controll.LLECAFPENFN",
    "Client",
    "Client.AGAPNOPLKDB",
    "MasterClient",
    "MasterClient.DHCBFAKOCAA",
    "PLH",
    "KBBBHJDINCB",
    "NET",
    "DMHBMAAFCFJ",
    "GOMBJHAKIFE",
    "NAHLLMJMOED",
    "CGJPBNDDPIN",
    "BIMFEOACIDM",
    "FPNENMKEFBB",
    "GUIInv",
    "HUD",
    "GUIMap",
    "SoundLoader",
    "CharAnimator",
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


def load_dump_text() -> str:
    if not DUMP_CS.exists():
        raise FileNotFoundError(f"dump.cs not found at {DUMP_CS}")
    return DUMP_CS.read_text(encoding="utf-8", errors="ignore")


def find_class_block(text: str, class_name: str) -> tuple[str, int] | None:
    """Find a top-level class block by brace counting. Returns (body, start_line)."""
    needle = class_name.replace(".", "\\.")
    # Match the class declaration line, optionally with a base class and TypeDefIndex comment.
    pattern = re.compile(rf"^\s*internal\s+class\s+{needle}(?:\s*:\s*[\w\.]+)?\s*(?://.*)?$", re.MULTILINE)
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
            return body, line_no
    return None, 0


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


def parse_fields(body: str) -> list[dict]:
    """Parse // Fields section."""
    fields = []
    # Stop when we hit the methods section or the end of the body.
    field_re = re.compile(
        r"^\s*(?P<modifiers>(?:internal|private|public|protected)(?:\s+(?:static|readonly|const))*)\s+"
        r"(?P<type>[\w\[\]<>.,\s]+?)\s+"
        r"(?P<name>[\w<>.]+)\s*;\s*//\s*(?P<offset>0x[0-9A-Fa-f]+)\s*$",
        re.MULTILINE,
    )
    # Find the end of the Fields section: a line like "\t// Methods" or first method RVA.
    methods_idx = re.search(r"\n\s*//\s*Methods\s*\n", body)
    field_text = body[: methods_idx.start()] if methods_idx else body

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
    return fields


def parse_methods(body: str) -> list[dict]:
    """Parse // Methods section."""
    methods = []
    # Match RVA/Offset/VA line followed by the method header line.
    rva_re = re.compile(
        r"^\s*//\s*RVA:\s*0x(?P<rva>[0-9A-Fa-f]+)\s+Offset:\s*0x(?P<offset>[0-9A-Fa-f]+)\s+VA:\s*0x(?P<va>[0-9A-Fa-f]+)\s*$",
        re.MULTILINE,
    )
    for rva in rva_re.finditer(body):
        # The method signature is on one of the next few non-blank, non-comment lines.
        start = rva.end()
        # Find the next non-blank/non-comment line that starts a method or property.
        rest = body[start:]
        for line in rest.splitlines():
            line = line.strip()
            if not line or line.startswith("//"):
                continue
            if "(" in line:
                header = line
                break
            # Sometimes the header is on a blank line? skip
            continue
        else:
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
        # Last token before '(' is the method name; everything before is return type + modifiers.
        method_name = tokens[-1]
        ret_and_mods = " ".join(tokens[:-1])

        # Skip the static .cctor and .ctor noise if desired, but keep them for completeness.
        methods.append({
            "name": method_name,
            "ret_and_mods": ret_and_mods,
            "args": args_part.strip(),
            "rva": int(rva.group("rva"), 16),
            "offset": int(rva.group("offset"), 16),
            "va": int(rva.group("va"), 16),
        })
    return methods


def csharp_identifier(name: str) -> str:
    """Make a safe C# identifier from a class/method/field name."""
    # . in original class name becomes _ for generated SDK class name.
    return name.replace(".", "_").replace("<", "_").replace(">", "_").replace(" ", "_")


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


def write_class_sdk(class_name: str, fields: list[dict], methods: list[dict], out_dir: Path) -> None:
    safe_name = csharp_identifier(class_name)
    file = out_dir / f"{safe_name}.cs"
    sb = []
    sb.append("// Auto-generated by Tools/generate_sdk.py")
    sb.append("// Do not edit manually; regenerate after re-dumping the game.")
    sb.append("namespace BlockpostTrainer.Sdk.Raw")
    sb.append("{")
    sb.append(f"    internal static class {safe_name}")
    sb.append("    {")
    sb.append("        /// <summary>")
    sb.append(f"        /// Field and static-field offsets for {class_name}.")
    sb.append("        /// </summary>")
    sb.append("        public static class Offsets")
    sb.append("        {")

    instance_fields = [f for f in fields if not f["static"]]
    static_fields = [f for f in fields if f["static"]]

    if static_fields:
        sb.append("            // Static fields")
        for f in static_fields:
            cname = csharp_identifier(f["name"])
            comp = vector_suffixes(f["type"])
            if comp:
                sb.append(f"            public const int {cname} = {hex(f['offset'])}; // {f['type']} ({SIZE_HINTS.get(f['type'], 4)} bytes)")
                for comp_name, delta in comp:
                    sb.append(f"            public const int {cname}_{comp_name} = {hex(f['offset'] + delta)};")
            else:
                sb.append(f"            public const int {cname} = {hex(f['offset'])}; // {f['type']}")

    if instance_fields:
        if static_fields:
            sb.append("")
        sb.append("            // Instance fields")
        for f in instance_fields:
            cname = csharp_identifier(f["name"])
            comp = vector_suffixes(f["type"])
            if comp:
                sb.append(f"            public const int {cname} = {hex(f['offset'])}; // {f['type']} ({SIZE_HINTS.get(f['type'], 4)} bytes)")
                for comp_name, delta in comp:
                    sb.append(f"            public const int {cname}_{comp_name} = {hex(f['offset'] + delta)};")
            else:
                sb.append(f"            public const int {cname} = {hex(f['offset'])}; // {f['type']}")

    sb.append("        }")
    sb.append("")

    sb.append("        /// <summary>")
    sb.append(f"        /// Method virtual addresses (VAs) for {class_name}.")
    sb.append("        /// </summary>")
    sb.append("        public static class Methods")
    sb.append("        {")
    seen = {}
    for m in methods:
        base_mname = csharp_identifier(m["name"])
        if base_mname in ("get", "set", "add", "remove", "op_") or not base_mname:
            # skip broken/special names
            continue
        seen[base_mname] = seen.get(base_mname, 0) + 1
        mname = base_mname if seen[base_mname] == 1 else f"{base_mname}_{seen[base_mname]}"
        # Quote the original method name in the comment so it can still be found by string.
        sig = f"{m['ret_and_mods']} {m['name']}({m['args']})"
        sb.append(f"            public const uint {mname} = {hex(m['va'])}; // {sig}")
    sb.append("        }")
    sb.append("    }")
    sb.append("}")

    file.write_text("\n".join(sb), encoding="utf-8")


def write_aliases(aliases: dict, out_dir: Path) -> None:
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
    for orig_class, mapping in aliases.items():
        safe_orig = orig_class.replace(".", "_")
        human = mapping.get("HumanClass", orig_class.replace(".", ""))
        sb.append(f"        public static class {human}")
        sb.append("        {")
        fields = mapping.get("Fields", {})
        methods = mapping.get("Methods", {})
        for human_name, orig_name in fields.items():
            safe_h = csharp_identifier(human_name)
            safe_o = csharp_identifier(orig_name)
            sb.append(f"            public const int {safe_h} = Raw.{safe_orig}.Offsets.{safe_o};")
        for human_name, orig_name in methods.items():
            safe_h = csharp_identifier(human_name)
            safe_o = csharp_identifier(orig_name)
            sb.append(f"            public const uint {safe_h} = Raw.{safe_orig}.Methods.{safe_o};")
        sb.append("        }")
        sb.append("")
    sb.append("    }")
    sb.append("}")
    file.write_text("\n".join(sb), encoding="utf-8")


def load_aliases() -> dict:
    if not ALIASES_FILE.exists():
        return {}
    return json.loads(ALIASES_FILE.read_text(encoding="utf-8"))


def main() -> int:
    print(f"Reading {DUMP_CS} ...")
    text = load_dump_text()
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    aliases = load_aliases()

    for class_name in TARGET_CLASSES:
        body, line = find_class_block(text, class_name)
        if body is None:
            print(f"  ! {class_name}: not found")
            continue
        print(f"  + {class_name} at line {line}")
        fields = parse_fields(body)
        methods = parse_methods(body)
        write_class_sdk(class_name, fields, methods, OUT_DIR)

    write_aliases(aliases, OUT_DIR)
    print(f"Done. Output in {OUT_DIR}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
