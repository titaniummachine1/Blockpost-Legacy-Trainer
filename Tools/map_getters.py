"""Map Get_* method aliases to semantic names based on return type analysis."""
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

# Build a map of all classes and their method signatures
# Format: { obf_class_name: { obf_method_name: (return_type, params) } }
class_methods = {}
for m in re.finditer(
    r"((?:public |internal |private |protected )?(?:sealed |static |abstract )?(?:class|struct|enum) (\S+)(?:\s*:.+?)?)\s*//\s*TypeDefIndex:\s*(\d+)\s*\{",
    text,
):
    cls_name = m.group(2)
    if cls_name in class_methods:
        continue
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
    methods = re.findall(
        r"// RVA:.*?\n\s+(?:internal |public |private |protected )?(?:static )?(\w[\w\[\]<>, ]*) (\w+)\((.*?)\)",
        block,
    )
    class_methods[cls_name] = {name: (ret, params) for ret, name, params in methods}

total_mapped = 0
for cls_name, info in data.items():
    if not isinstance(info, dict):
        continue
    methods = info.get("Methods", {})
    if not methods:
        continue

    # Get the obfuscated class name for this alias entry
    # The alias key might be the human name; we need to find the obfuscated name
    # Actually, the alias structure is: data[obfuscated_name] = { HumanClass: ..., Methods: { semantic: obf_method } }
    # So cls_name IS the obfuscated name
    dump_methods = class_methods.get(cls_name, {})

    to_update = []
    for sem_name, obf_name in methods.items():
        if not sem_name.startswith("Get_"):
            continue
        ret_type, params = dump_methods.get(obf_name, ("", ""))
        ret_clean = ret_type.strip()

        # Build semantic name from return type
        if ret_clean == "string":
            new_name = sem_name.replace("Get_", "GetString_")
        elif ret_clean == "int":
            new_name = sem_name.replace("Get_", "GetInt_")
        elif ret_clean == "bool":
            new_name = sem_name.replace("Get_", "GetBool_")
        elif ret_clean == "float":
            new_name = sem_name.replace("Get_", "GetFloat_")
        elif ret_clean == "byte":
            new_name = sem_name.replace("Get_", "GetByte_")
        elif ret_clean == "long":
            new_name = sem_name.replace("Get_", "GetLong_")
        elif ret_clean == "uint":
            new_name = sem_name.replace("Get_", "GetUint_")
        elif ret_clean == "ulong":
            new_name = sem_name.replace("Get_", "GetUlong_")
        elif ret_clean == "Vector3":
            new_name = sem_name.replace("Get_", "GetVector3_")
        elif ret_clean == "Color":
            new_name = sem_name.replace("Get_", "GetColor_")
        elif ret_clean == "GameObject":
            new_name = sem_name.replace("Get_", "GetGameObject_")
        elif ret_clean == "Transform":
            new_name = sem_name.replace("Get_", "GetTransform_")
        elif ret_clean == "Camera":
            new_name = sem_name.replace("Get_", "GetCamera_")
        elif ret_clean == "Texture":
            new_name = sem_name.replace("Get_", "GetTexture_")
        elif ret_clean == "Material":
            new_name = sem_name.replace("Get_", "GetMaterial_")
        elif ret_clean == "Mesh":
            new_name = sem_name.replace("Get_", "GetMesh_")
        elif "KBBBHJDINCB" in ret_clean:
            new_name = sem_name.replace("Get_", "GetPlayer_")
        elif "CGJPBNDDPIN" in ret_clean:
            new_name = sem_name.replace("Get_", "GetWeapon_")
        elif "NAHLLMJMOED" in ret_clean:
            new_name = sem_name.replace("Get_", "GetWeaponData_")
        elif "FPNENMKEFBB" in ret_clean:
            new_name = sem_name.replace("Get_", "GetLoadout_")
        elif "List" in ret_clean:
            new_name = sem_name.replace("Get_", "GetList_")
        elif "Dictionary" in ret_clean:
            new_name = sem_name.replace("Get_", "GetDict_")
        elif "Array" in ret_clean or "[]" in ret_clean:
            new_name = sem_name.replace("Get_", "GetArray_")
        else:
            # Keep the Get_ prefix but add the type
            type_name = ret_clean.replace("<", "").replace(">", "").replace(",", "_").replace(" ", "")[:20]
            new_name = sem_name.replace("Get_", f"Get{type_name}_")

        if new_name != sem_name:
            to_update.append((sem_name, new_name, obf_name))

    for old_name, new_name, obf_name in to_update:
        del methods[old_name]
        methods[new_name] = obf_name
        total_mapped += 1

with open(ALIASES, "w", encoding="utf-8") as f:
    json.dump(data, f, indent=2, ensure_ascii=False)
    f.write("\n")
print(f"Total Get_* methods refined: {total_mapped}")
