"""Map remaining Action_ methods by deeper signature pattern analysis."""
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

classes_to_map = []
for cls, info in data.items():
    if not isinstance(info, dict):
        continue
    methods = info.get("Methods", {})
    if any(k.startswith("Action_") for k in methods):
        classes_to_map.append(cls)

print(f"Processing {len(classes_to_map)} classes...")
total_added = 0

for cls_name in classes_to_map:
    patterns = [
        f"internal class {cls_name} ",
        f"public class {cls_name} ",
        f"public sealed class {cls_name} ",
        f"internal sealed class {cls_name} ",
        f"public struct {cls_name} ",
        f"internal struct {cls_name} ",
    ]
    idx = -1
    for p in patterns:
        idx = text.find(p)
        if idx >= 0:
            break
    if idx < 0:
        continue

    brace_start = text.find("{", idx)
    if brace_start < 0:
        continue
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
    block = text[idx : i + 1]
    methods = re.findall(
        r"// RVA:.*?\n\s+(?:internal |public |private )?(?:static )?(\w[\w\[\]<>, ]*) (\w+)\((.*?)\)",
        block,
    )

    cls_data = data.get(cls_name, {})
    if not isinstance(cls_data, dict):
        cls_data = {}
        data[cls_name] = cls_data
    existing_methods = cls_data.get("Methods", {})
    if not existing_methods:
        cls_data["Methods"] = {}
        existing_methods = cls_data["Methods"]
    obf_to_sem = {v: k for k, v in existing_methods.items()}

    added = 0
    for ret, name, params in methods:
        sem = obf_to_sem.get(name, "?")
        if not sem.startswith("Action_"):
            continue

        p = params.strip()
        new_sem = None

        if "Vector3" in p and "float" in p:
            new_sem = f"{cls_name}_RecvTransform{added + 1}"
        elif "Vector3" in p and "int" in p:
            new_sem = f"{cls_name}_RecvPosEvent{added + 1}"
        elif p.count("int") >= 2 and "string" not in p:
            new_sem = f"{cls_name}_RecvMultiInt{added + 1}"
        elif "string" in p and "int" in p:
            new_sem = f"{cls_name}_RecvStringInt{added + 1}"
        elif "string" in p and "bool" in p:
            new_sem = f"{cls_name}_RecvStringBool{added + 1}"
        elif "KBBBHJDINCB" in p:
            new_sem = f"{cls_name}_PlayerAction{added + 1}"
        elif "CGJPBNDDPIN" in p:
            new_sem = f"{cls_name}_WeaponAction{added + 1}"
        elif "NAHLLMJMOED" in p:
            new_sem = f"{cls_name}_WeaponDataAction{added + 1}"
        elif "FPNENMKEFBB" in p:
            new_sem = f"{cls_name}_LoadoutAction{added + 1}"
        elif "byte[]" in p:
            new_sem = f"{cls_name}_RecvBytes{added + 1}"
        elif "Color" in p:
            new_sem = f"{cls_name}_SetColor{added + 1}"
        elif "GameObject" in p:
            new_sem = f"{cls_name}_GameObjectAction{added + 1}"
        elif "Transform" in p:
            new_sem = f"{cls_name}_TransformAction{added + 1}"
        elif p.count("float") >= 2:
            new_sem = f"{cls_name}_RecvMultiFloat{added + 1}"
        elif "ulong" in p:
            new_sem = f"{cls_name}_RecvUlong{added + 1}"
        elif "uint" in p:
            new_sem = f"{cls_name}_RecvUint{added + 1}"
        elif "Camera" in p:
            new_sem = f"{cls_name}_CameraAction{added + 1}"
        elif "AudioSource" in p:
            new_sem = f"{cls_name}_AudioAction{added + 1}"
        elif "Material" in p:
            new_sem = f"{cls_name}_MaterialAction{added + 1}"
        elif "Mesh" in p:
            new_sem = f"{cls_name}_MeshAction{added + 1}"
        elif "Texture" in p:
            new_sem = f"{cls_name}_TextureAction{added + 1}"
        else:
            new_sem = f"{cls_name}_ComplexAction{added + 1}"

        for k, v in list(existing_methods.items()):
            if v == name:
                del existing_methods[k]
                existing_methods[new_sem] = name
                added += 1
                break

    total_added += added

with open(ALIASES, "w", encoding="utf-8") as f:
    json.dump(data, f, indent=2, ensure_ascii=False)
    f.write("\n")
print(f"Total mapped: {total_added}")
