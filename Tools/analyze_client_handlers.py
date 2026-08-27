"""Analyze remaining network opcodes by finding their handler methods in Client.
The Client class processes inbound packets by switching on opcode and calling
handler methods. We need to find the switch statement and map opcodes to handlers."""
import re
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DUMP = ROOT / ".tools" / "Il2CppDumper" / "dump.cs"

with open(DUMP, encoding="utf-8") as f:
    text = f.read()

# Find the Client class block
idx = text.find("public class Client ")
if idx < 0:
    idx = text.find("internal class Client ")
if idx < 0:
    # Try just finding "class Client "
    for m in re.finditer(r"(?:public |internal )?class (Client)\s*[:{]", text):
        idx = m.start()
        break

print(f"Client class found at offset {idx}")

# Get the full Client block
brace_start = text.find("{", idx)
depth = 0
i = brace_start
while i < len(text):
    if text[i] == "{":
        depth += 1
    elif text[i] == "}":
        depth -= 1
        if depth == 0:
            break
    i += 1
block = text[idx:i+1]
print(f"Client block size: {len(block)} chars")

# Find all methods with their signatures
methods = re.findall(
    r'// RVA:.*?\n\s+(?:internal |public |private )?(?:static )?(\w[\w\[\]<>, ]*)\s+(\w+)\(([^)]*)\)',
    block
)

# Find methods that take byte parameters (likely packet handlers)
# Also find methods that reference specific opcodes
print(f"\nTotal Client methods: {len(methods)}")

# Look for methods with specific patterns:
# - Methods taking (byte[], int) - packet receivers
# - Methods taking single int/byte - opcode handlers
# - Methods with Vector3 parameters - position handlers
# - Methods taking KBBBHJDINCB (Player) - player action handlers

handlers = []
for ret, name, params in methods:
    params_clean = params.strip()
    ret_clean = ret.strip()
    
    # Categorize by signature
    if "byte[]" in params_clean or "Il2CppStructArray<byte>" in params_clean:
        cat = "packet_receiver"
    elif "KBBBHJDINCB" in params_clean:
        cat = "player_handler"
    elif "Vector3" in params_clean:
        cat = "position_handler"
    elif params_clean == "" or params_clean == "int":
        cat = "simple_handler"
    elif "string" in params_clean.lower():
        cat = "string_handler"
    elif "bool" in params_clean:
        cat = "bool_handler"
    else:
        cat = "other"
    
    handlers.append({
        "name": name,
        "return": ret_clean,
        "params": params_clean,
        "category": cat,
    })

# Print categorized handlers
for cat in ["packet_receiver", "player_handler", "position_handler", "string_handler", "bool_handler", "simple_handler", "other"]:
    cat_handlers = [h for h in handlers if h["category"] == cat]
    if cat_handlers:
        print(f"\n=== {cat} ({len(cat_handlers)}) ===")
        for h in cat_handlers[:20]:
            print(f"  {h['return']} {h['name']}({h['params']})")
        if len(cat_handlers) > 20:
            print(f"  ... +{len(cat_handlers)-20} more")

# Save full list
output_path = ROOT / "Tools" / "client_handlers.json"
with open(output_path, "w", encoding="utf-8") as f:
    json.dump(handlers, f, indent=2)
print(f"\nFull list: {output_path}")
