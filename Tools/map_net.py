#!/usr/bin/env python3
"""Map NET class methods to their protocol primitives by signature analysis.

The NET class is the packet building primitive layer. By analyzing method
signatures (parameter types and return types), we can infer what each method
does:
  - void method(byte[], int, int) = copy bytes to buffer
  - void method(float) = write float to packet
  - void method(int) = write int to packet
  - void method(short) = write short to packet
  - void method(byte) = write byte to packet
  - void method(ulong) = write ulong to packet
  - void method(string) = write string to packet
  - float method() = read float from packet
  - int method() = read int from packet
  - string method() = read string from packet
  - byte[] method() = get send buffer
  - byte[] method(float/int/short/ulong) = convert to bytes
  - ulong method(byte[], int) = read ulong from buffer
"""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
db = json.loads((ROOT / "Tools" / "analysis" / "type_database.json").read_text(encoding="utf-8"))

net_methods = db["NET"]["methods"]

# Categorize by signature
categories = {
    "write_float": [],      # void(float)
    "write_int": [],        # void(int)
    "write_short": [],      # void(short)
    "write_byte": [],       # void(byte)
    "write_ulong": [],      # void(ulong)
    "write_string": [],     # void(string)
    "write_bytes": [],      # void(byte[], int, int)
    "read_float": [],       # float()
    "read_int": [],         # int()
    "read_string": [],      # string()
    "read_ulong": [],       # ulong()
    "read_uint": [],        # uint()
    "get_buffer": [],       # byte[]()
    "convert_to_bytes": [], # byte[](primitive)
    "read_from_bytes": [],  # primitive(byte[], int)
    "copy_to_buffer": [],   # void(byte[], int)
    "other": [],
}

for m in net_methods:
    name = m["name"]
    ret = m.get("return_type", "void")
    args = m.get("parsed_args", [])
    va = hex(m["va"])
    sig = f"{ret} {name}({m['args']})"

    entry = {"name": name, "va": va, "signature": sig, "args": m["args"]}

    if ret == "void" and len(args) == 1:
        arg_type = args[0]["type"]
        if arg_type == "float":
            categories["write_float"].append(entry)
        elif arg_type == "int":
            categories["write_int"].append(entry)
        elif arg_type == "short":
            categories["write_short"].append(entry)
        elif arg_type == "byte":
            categories["write_byte"].append(entry)
        elif arg_type == "ulong":
            categories["write_ulong"].append(entry)
        elif arg_type == "string":
            categories["write_string"].append(entry)
        elif arg_type == "byte[]":
            categories["copy_to_buffer"].append(entry)
        else:
            categories["other"].append(entry)
    elif ret == "void" and len(args) == 2:
        if args[0]["type"] == "byte" and args[1]["type"] == "byte":
            categories["write_byte"].append(entry)  # write 2 bytes
        else:
            categories["other"].append(entry)
    elif ret == "void" and len(args) == 3:
        if args[0]["type"] == "byte[]" and args[1]["type"] == "int" and args[2]["type"] == "int":
            categories["write_bytes"].append(entry)
        else:
            categories["other"].append(entry)
    elif ret == "void" and len(args) == 0:
        categories["other"].append({"name": name, "va": va, "signature": sig, "args": "", "note": "void() - likely flush/reset"})
    elif ret == "float" and len(args) == 0:
        categories["read_float"].append(entry)
    elif ret == "int" and len(args) == 0:
        categories["read_int"].append(entry)
    elif ret == "string" and len(args) == 0:
        categories["read_string"].append(entry)
    elif ret == "ulong" and len(args) == 0:
        categories["read_ulong"].append(entry)
    elif ret == "uint" and len(args) == 0:
        categories["read_uint"].append(entry)
    elif ret == "byte[]" and len(args) == 0:
        categories["get_buffer"].append(entry)
    elif ret == "byte[]" and len(args) == 1:
        categories["convert_to_bytes"].append(entry)
    elif ret in ("float", "int", "ulong", "uint") and len(args) == 2:
        if args[0]["type"] == "byte[]" and args[1]["type"] == "int":
            categories["read_from_bytes"].append(entry)
        else:
            categories["other"].append(entry)
    else:
        categories["other"].append(entry)

print("=== NET Protocol Primitives ===\n")
for cat, methods in categories.items():
    if not methods:
        continue
    print(f"--- {cat} ({len(methods)}) ---")
    for m in methods:
        print(f"  {m['va']}  {m['signature']}")
    print()

# Save
out = ROOT / "Tools" / "analysis" / "net_protocol_map.json"
out.write_text(json.dumps(categories, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
print(f"Saved to {out}")
