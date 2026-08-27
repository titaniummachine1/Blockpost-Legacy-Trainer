"""Extract string literals from dump.cs and associate them with the class/method
that references them. This reveals the true purpose of obfuscated methods."""
import re
import json
from pathlib import Path
from collections import defaultdict

ROOT = Path(__file__).resolve().parent.parent
DUMP = ROOT / ".tools" / "Il2CppDumper" / "dump.cs"
OUTPUT = ROOT / "Tools" / "string_crossref.json"

with open(DUMP, encoding="utf-8") as f:
    text = f.read()

# Find all class blocks and extract string literals within each
# String literals appear as: const string NAME = "value";
# Or as method body references (but dump.cs doesn't show bodies, only signatures)
# The const string fields are the main source of readable strings

results = defaultdict(lambda: {"const_strings": [], "methods_with_string_params": []})

# Pattern 1: const string fields within classes
# Format: private/public const string OBF_NAME = "readable value";
const_string_pattern = re.compile(
    r'(?:private |public |internal )?const string (\w+) = "([^"]+)";'
)

# Find all classes and their blocks
class_pattern = re.compile(
    r'((?:public |internal |private |protected )?(?:sealed |static |abstract )?(?:class|struct) (\S+)(?:\s*:.+?)?)\s*//\s*TypeDefIndex:\s*(\d+)\s*\{',
    re.MULTILINE,
)

classes_found = 0
for m in class_pattern.finditer(text):
    cls_name = m.group(2)
    block_start = m.end() - 1
    depth = 0
    i = block_start
    while i < len(text):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                break
        i += 1
    block = text[m.start() : i + 1]

    # Extract const strings
    for sm in const_string_pattern.finditer(block):
        obf_name, value = sm.group(1), sm.group(2)
        if len(value) > 1:  # Skip single-char strings
            results[cls_name]["const_strings"].append(
                {"obf_field": obf_name, "value": value}
            )

    classes_found += 1

# Also extract PlayerPrefs keys and URL strings
playerprefs_keys = set()
urls = set()
for sm in re.finditer(r'"([^"]*PlayerPrefs[^"]*)"', text):
    playerprefs_keys.add(sm.group(1))
for sm in re.finditer(r'"(https?://[^"]+)"', text):
    urls.add(sm.group(1))

# Filter to only classes that have const strings
filtered = {
    cls: data
    for cls, data in results.items()
    if data["const_strings"]
}

output = {
    "classes_with_strings": dict(filtered),
    "playerprefs_keys": sorted(playerprefs_keys),
    "urls": sorted(urls),
    "total_classes_with_strings": len(filtered),
    "total_const_strings": sum(
        len(d["const_strings"]) for d in filtered.values()
    ),
}

with open(OUTPUT, "w", encoding="utf-8") as f:
    json.dump(output, f, indent=2, ensure_ascii=False)
    f.write("\n")

print(f"Scanned {classes_found} classes")
print(f"Found {len(filtered)} classes with const strings")
print(f"Total const strings: {output['total_const_strings']}")
print(f"PlayerPrefs keys: {len(playerprefs_keys)}")
print(f"URLs: {len(urls)}")
print(f"Output: {OUTPUT}")
