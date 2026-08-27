"""Map raw obfuscated field aliases to semantic names based on field type."""
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

# Build field type map: { class_name: { field_name: field_type } }
class_fields = {}
for m in re.finditer(
    r"((?:public |internal |private |protected )?(?:sealed |static |abstract )?(?:class|struct|enum) (\S+)(?:\s*:.+?)?)\s*//\s*TypeDefIndex:\s*(\d+)\s*\{",
    text,
):
    cls_name = m.group(2)
    if cls_name in class_fields:
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
    # Match field declarations: type name; // offset
    fields = re.findall(r"^\s+(?:public |internal |private |protected )?(?:static |readonly |const )*(\w[\w\[\]<>, ]*)\s+(\w+);.*?//\s*(0x\w+)", block, re.MULTILINE)
    class_fields[cls_name] = {name: ftype.strip() for ftype, name, offset in fields}

total_mapped = 0
for cls_name, info in data.items():
    if not isinstance(info, dict):
        continue
    fields = info.get("Fields", {})
    if not fields:
        continue

    dump_fields = class_fields.get(cls_name, {})

    to_update = []
    for sem_name, obf_name in fields.items():
        # Only map raw obf names (key == value, all uppercase, len > 6)
        if sem_name != obf_name:
            continue
        if not (sem_name == sem_name.upper() and len(sem_name) > 6):
            continue

        ftype = dump_fields.get(obf_name, "")
        ftype_clean = ftype.strip()

        # Build semantic name from field type
        if ftype_clean == "int":
            new_name = f"IntField_{obf_name[:4]}"
        elif ftype_clean == "bool":
            new_name = f"BoolField_{obf_name[:4]}"
        elif ftype_clean == "float":
            new_name = f"FloatField_{obf_name[:4]}"
        elif ftype_clean == "string":
            new_name = f"StringField_{obf_name[:4]}"
        elif ftype_clean == "byte":
            new_name = f"ByteField_{obf_name[:4]}"
        elif ftype_clean == "long":
            new_name = f"LongField_{obf_name[:4]}"
        elif ftype_clean == "uint":
            new_name = f"UintField_{obf_name[:4]}"
        elif ftype_clean == "ulong":
            new_name = f"UlongField_{obf_name[:4]}"
        elif ftype_clean == "Vector3":
            new_name = f"Vector3Field_{obf_name[:4]}"
        elif ftype_clean == "Color":
            new_name = f"ColorField_{obf_name[:4]}"
        elif ftype_clean == "GameObject":
            new_name = f"GameObjectField_{obf_name[:4]}"
        elif ftype_clean == "Transform":
            new_name = f"TransformField_{obf_name[:4]}"
        elif ftype_clean == "Camera":
            new_name = f"CameraField_{obf_name[:4]}"
        elif ftype_clean == "AudioSource":
            new_name = f"AudioField_{obf_name[:4]}"
        elif ftype_clean == "Texture":
            new_name = f"TextureField_{obf_name[:4]}"
        elif ftype_clean == "Material":
            new_name = f"MaterialField_{obf_name[:4]}"
        elif ftype_clean == "Mesh":
            new_name = f"MeshField_{obf_name[:4]}"
        elif "KBBBHJDINCB" in ftype_clean:
            new_name = f"PlayerField_{obf_name[:4]}"
        elif "CGJPBNDDPIN" in ftype_clean:
            new_name = f"WeaponField_{obf_name[:4]}"
        elif "NAHLLMJMOED" in ftype_clean:
            new_name = f"WeaponDataField_{obf_name[:4]}"
        elif "FPNENMKEFBB" in ftype_clean:
            new_name = f"LoadoutField_{obf_name[:4]}"
        elif "List" in ftype_clean:
            new_name = f"ListField_{obf_name[:4]}"
        elif "Dictionary" in ftype_clean:
            new_name = f"DictField_{obf_name[:4]}"
        elif "[]" in ftype_clean:
            new_name = f"ArrayField_{obf_name[:4]}"
        elif ftype_clean == "Quaternion":
            new_name = f"QuaternionField_{obf_name[:4]}"
        elif ftype_clean == "Rigidbody":
            new_name = f"RigidbodyField_{obf_name[:4]}"
        elif ftype_clean == "Collider":
            new_name = f"ColliderField_{obf_name[:4]}"
        else:
            # Use type name prefix
            type_short = ftype_clean.replace("<", "").replace(">", "").replace(",", "_").replace(" ", "").replace("[]", "Arr")[:15]
            new_name = f"{type_short}Field_{obf_name[:4]}"

        to_update.append((sem_name, new_name, obf_name))

    for old_name, new_name, obf_name in to_update:
        del fields[old_name]
        fields[new_name] = obf_name
        total_mapped += 1

with open(ALIASES, "w", encoding="utf-8") as f:
    json.dump(data, f, indent=2, ensure_ascii=False)
    f.write("\n")
print(f"Total raw obf fields refined: {total_mapped}")
