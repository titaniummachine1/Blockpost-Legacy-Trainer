"""Map Client handler methods to network opcodes by analyzing the packet
processing method (FPKEAECEOPE) which switches on opcodes and calls handlers.
Since dump.cs doesn't show method bodies, we use signature analysis to infer
which methods handle which opcodes based on the BACKLOG.md protocol notes."""
import re
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DUMP = ROOT / ".tools" / "Il2CppDumper" / "dump.cs"
ALIASES = ROOT / "Tools" / "sdk_aliases.json"

with open(DUMP, encoding="utf-8") as f:
    text = f.read()
with open(ALIASES, encoding="utf-8") as f:
    data = json.load(f)

# Find Client class block
idx = text.find("public class Client ")
if idx < 0:
    for m in re.finditer(r"(?:public |internal )?class (Client)\s*[:{]", text):
        idx = m.start()
        break

brace_start = text.find("{", idx)
depth = 0
i = brace_start
while i < len(text):
    if text[i] == "{": depth += 1
    elif text[i] == "}":
        depth -= 1
        if depth == 0: break
    i += 1
block = text[idx:i+1]

# Extract all methods with full info
methods = re.findall(
    r'// RVA:.*?Offset:.*?VA:.*?\n\s+(?:internal |public |private )?(?:static )?(\w[\w\[\]<>, ]*)\s+(\w+)\(([^)]*)\)',
    block
)

# Known protocol info from BACKLOG.md and PROTOCOL.md:
# 0x01 = join/spawn (39-byte initial server packet)
# 0x03 = snapshot (player positions)
# 0x04 = hit event (target, value, u16)
# 0x06 = shot reply (3 shorts)
# 0x07 = state/7
# 0x08 = weapondata
# 0x09 = loadout
# 0x0A = state/10
# 0x0B = state/11
# 0x0C = state/12
# 0x0D = chat
# 0x0E = weapon replication
# 0x0F = slot switch
# 0x13 = state/19
# 0x14 = state/20
# 0x15 = state/21
# 0x2D = move reply
# 0x34 = replication/52

# Map by signature analysis:
# - Position handlers (Vector3 params) → 0x03 snapshot, 0x2D move reply
# - Hit report (Vector3, uint, List<Hit>) → 0x04 hit event (already known: AHLDAPJEJNC)
# - Shot (Vector3, Vector3, float) → 0x06 shot, 0x16 throw, 0x17 impact
# - String handlers → 0x0D chat, 0x1B string
# - Loadout (List<FPNENMKEFBB>) → 0x09 loadout
# - WeaponData (NAHLLMJMOED) → 0x08 weapondata
# - Slot (int) → 0x0F slot switch
# - Team (int) → team change
# - Simple void() → state events (0x07, 0x0A, 0x0B, 0x0C, 0x13, 0x14, 0x15)

# Build semantic name mappings
mappings = {}

for ret, name, params in methods:
    params_clean = params.strip()
    ret_clean = ret.strip()
    
    # AHLDAPJEJNC = already mapped as hit report sender (tx), but also used for rx
    # Position handlers with 6 floats + int = move/snapshot reply
    if "Vector3" in params_clean and "float" in params_clean and "int" in params_clean:
        if "GOALAGKACHO" in params_clean:  # has spread parameter
            mappings[name] = f"Client_RecvPlayerState_{name[:4]}"
        elif "NKFBPNNOCJB" in params_clean:
            mappings[name] = f"Client_RecvPlayerState2_{name[:4]}"
    
    # Hit report: Vector3, uint, List<Hit>
    elif "Vector3" in params_clean and "uint" in params_clean and "List" in params_clean:
        mappings[name] = f"Client_RecvHitReport_{name[:4]}"
    
    # Shot/throw/impact: Vector3, Vector3, float
    elif params_clean.count("Vector3") == 2 and "float" in params_clean:
        mappings[name] = f"Client_RecvShot_{name[:4]}"
    
    # Position + int (teleport/spawn)
    elif "Vector3" in params_clean and "int" in params_clean and "float" not in params_clean:
        mappings[name] = f"Client_RecvTeleport_{name[:4]}"
    
    # Loadout: List<FPNENMKEFBB>
    elif "List<FPNENMKEFBB>" in params_clean:
        mappings[name] = f"Client_RecvLoadout_{name[:4]}"
    
    # WeaponData: NAHLLMJMOED
    elif "NAHLLMJMOED" in params_clean:
        mappings[name] = f"Client_RecvWeaponData_{name[:4]}"
    
    # Chat: int, string, int
    elif "int" in params_clean and "string" in params_clean:
        mappings[name] = f"Client_RecvChat_{name[:4]}"
    
    # String only
    elif params_clean == "string":
        mappings[name] = f"Client_RecvString_{name[:4]}"
    
    # Single int (slot, team, id)
    elif params_clean == "int":
        mappings[name] = f"Client_RecvInt_{name[:4]}"
    
    # No params (state events)
    elif params_clean == "":
        mappings[name] = f"Client_RecvState_{name[:4]}"
    
    # 3 ints (coordinates)
    elif params_clean.count("int") == 3:
        mappings[name] = f"Client_RecvInt3_{name[:4]}"
    
    # 4 floats + byte + uint (movement)
    elif "float" in params_clean and "byte" in params_clean and "uint" in params_clean:
        mappings[name] = f"Client_RecvMove_{name[:4]}"
    
    # 3 ints (position)
    elif params_clean.count("int") >= 2 and "float" not in params_clean:
        mappings[name] = f"Client_RecvInts_{name[:4]}"

# Apply mappings to aliases
client = data.get("Client", {})
methods_dict = client.get("Methods", {})
added = 0
for obf_name, sem_name in mappings.items():
    # Find the existing alias key that maps to this obf name
    for k, v in list(methods_dict.items()):
        if v == obf_name and k.startswith("Action_"):
            del methods_dict[k]
            methods_dict[sem_name] = obf_name
            added += 1
            break

with open(ALIASES, "w", encoding="utf-8") as f:
    json.dump(data, f, indent=2, ensure_ascii=False)
    f.write("\n")

print(f"Mapped {added} Client handler methods to semantic names")
print(f"\nMappings applied:")
for obf, sem in sorted(mappings.items()):
    print(f"  {obf} -> {sem}")
