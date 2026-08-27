"""Build a method call graph from dump.cs by analyzing method signatures,
field types, and cross-class references. This helps map obfuscated methods
to semantic names by understanding which methods call which."""
import re
import json
from pathlib import Path
from collections import defaultdict

ROOT = Path(__file__).resolve().parent.parent
DUMP = ROOT / ".tools" / "Il2CppDumper" / "dump.cs"
OUTPUT = ROOT / "Tools" / "call_graph.json"

with open(DUMP, encoding="utf-8") as f:
    text = f.read()

# Parse all classes and their methods
classes = {}
current_class = None
current_block = ""

for m in re.finditer(r'(?:public |internal |private |protected )?(?:sealed |abstract |static )?class (\w+)\s*(?::\s*([^{]+?))?\s*\{', text):
    name = m.group(1)
    parent = m.group(2) or ""
    # Get the class block
    brace_start = m.end() - 1
    depth = 0
    i = brace_start
    while i < len(text):
        if text[i] == '{':
            depth += 1
        elif text[i] == '}':
            depth -= 1
            if depth == 0:
                break
        i += 1
    block = text[m.start():i+1]

    # Extract methods
    methods = re.findall(
        r'// RVA:.*?Offset:.*?VA:.*?\n\s+(?:internal |public |private |protected )?(?:static )?(\w[\w<>\[\], ]*)\s+(\w+)\(([^)]*)\)',
        block
    )
    # Extract fields
    fields = re.findall(r'(\w[\w<>\[\], ]*)\s+(\w+);\s*// (0x\w+)', block)

    classes[name] = {
        "parent": parent.strip(),
        "methods": [{"return": r.strip(), "name": n, "params": p.strip()} for r, n, p in methods],
        "fields": [{"type": t.strip(), "name": n, "offset": o} for t, n, o in fields],
        "method_count": len(methods),
        "field_count": len(fields),
    }

print(f"Parsed {len(classes)} classes")

# Build call graph: which classes reference which other classes
# by looking at field types and method parameter types
references = defaultdict(lambda: defaultdict(int))

for cls_name, cls_data in classes.items():
    # Check field types
    for field in cls_data["fields"]:
        ftype = field["type"]
        # Extract class name from type (remove generics, arrays, etc.)
        ref_class = re.match(r'(\w+)', ftype)
        if ref_class and ref_class.group(1) in classes and ref_class.group(1) != cls_name:
            references[cls_name][ref_class.group(1)] += 1

    # Check method parameter types
    for method in cls_data["methods"]:
        for param_type in re.findall(r'(\w+)', method["params"]):
            if param_type in classes and param_type != cls_name:
                references[cls_name][param_type] += 1
        # Check return type
        ret_type = re.match(r'(\w+)', method["return"])
        if ret_type and ret_type.group(1) in classes and ret_type.group(1) != cls_name:
            references[cls_name][ret_type.group(1)] += 1

# Find the most referenced classes (hubs)
ref_count = defaultdict(int)
for cls_name, refs in references.items():
    for target, count in refs.items():
        ref_count[target] += count

# Top 30 most referenced classes
top_referenced = sorted(ref_count.items(), key=lambda x: -x[1])[:30]
print("\nTop 30 most referenced classes (hubs):")
for name, count in top_referenced:
    print(f"  {name}: {count} references")

# Find classes that reference many other classes (controllers)
outgoing = {cls: sum(refs.values()) for cls, refs in references.items()}
top_outgoing = sorted(outgoing.items(), key=lambda x: -x[1])[:30]
print("\nTop 30 classes with most outgoing references (controllers):")
for name, count in top_outgoing:
    print(f"  {name}: {count} outgoing refs to {len(references[name])} classes")

# Save the full graph
output = {
    "classes": {name: {"method_count": d["method_count"], "field_count": d["field_count"], "parent": d["parent"]} for name, d in classes.items()},
    "references": {cls: dict(refs) for cls, refs in references.items()},
    "top_referenced": dict(top_referenced),
    "top_outgoing": dict(top_outgoing),
}
with open(OUTPUT, "w", encoding="utf-8") as f:
    json.dump(output, f, indent=2, ensure_ascii=False)
    f.write("\n")
print(f"\nCall graph saved to {OUTPUT}")
