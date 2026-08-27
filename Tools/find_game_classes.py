"""Find game-specific readable classes that aren't yet aliased.
Filter out Unity engine, System, Steamworks, and third-party library classes."""
import re
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DUMP = ROOT / ".tools" / "Il2CppDumper" / "dump.cs"
ALIASES = ROOT / "Tools" / "sdk_aliases.json"

with open(DUMP, encoding="utf-8") as f:
    text = f.read()
with open(ALIASES, encoding="utf-8") as f:
    aliased = json.load(f)

# Skip these prefixes/names — they're engine/library code, not game code
SKIP_NAMES = {
    # Unity engine
    'VisualElement', 'VisualElement.Hierarchy', 'ScrollView', 'ScrollRect',
    'InputField', 'TextGenerator', 'TextEditor', 'FontAsset', 'GUISkin',
    'CanvasUpdateRegistry', 'AnimationTriggers', 'PanelSettings',
    'UIDocument', 'BaseVerticalCollectionView', 'ComputedStyle',
    'ComponentFactory', 'Object', 'Mesh', 'Font', 'Material', 'Texture',
    'Shader', 'Camera', 'Light', 'Renderer', 'Collider', 'Rigidbody',
    'Transform', 'GameObject', 'Component', 'MonoBehaviour', 'Behaviour',
    'ScriptableObject', 'TextAsset', 'Sprite', 'Canvas', 'CanvasRenderer',
    'Graphic', 'GraphicRaycaster', 'Image', 'RawImage', 'Button',
    'Toggle', 'Slider', 'Scrollbar', 'Dropdown', 'ScrollRect',
    'Selectable', 'Interactable', 'Navigation', 'ColorBlock',
    'SpriteState', 'AnimationTriggers', 'InputField',
    'TMP_Text', 'TMP_FontAsset', 'TMP_Settings', 'TMP_SpriteAsset',
    'TextMeshPro', 'TextMeshProUGUI',
    # System
    'String', 'Array', 'Type', 'RuntimeType', 'Convert', 'DateTime',
    'DateTimeFormatInfo', 'CultureInfo', 'TimeZoneInfo', 'Number',
    'Uri', 'Task', 'Regex', 'RegexParser', 'DeflateManager',
    'Socket', 'WebSocket', 'Thread', 'ThreadPool', 'Mutex',
    'Hashtable', 'HashSet', 'Dictionary', 'List', 'Queue', 'Stack',
    'Environment', 'Version', 'VersionInfo', 'Enum', 'Boolean',
    'Int32', 'Int64', 'UInt32', 'UInt64', 'Single', 'Double',
    'Byte', 'SByte', 'Char', 'Object', 'Exception', 'ArgumentException',
    'InvalidOperationException', 'NullReferenceException',
    'SerializationInfo', 'SafeSerializationManager',
    'LogicalCallContext', 'StackTrace', 'TypeConverter',
    'X509BasicConstraintsExtension', 'X509KeyUsageExtension',
    'X509SubjectKeyIdentifierExtension',
    # Steamworks
    'NativeMethods', 'Constants', 'Version', 'SteamInventoryTest',
    'SteamAppsTest', 'GPGSIds',
    # Third-party
    'AmplifyMotionEffectBase', 'AmplifyMotionPostProcess', 'AmplifyOcclusionBase',
    'LTDescr', 'LeanTween', 'LeanTweenExt', 'LTSpline', 'LTRect',
    'LeanTester', 'PathSplinePerformance', 'PathSplineEndless',
    'PathSplineTrack', 'DelayTransparent', 'GeneralSimpleUI',
    'GeneralEasingTypes', 'GeneralBasic', 'ThreadMap', 'ThreadPoolTest',
    'TestingZLegacy',
    # VK
    'VKContants', 'VKSettings', 'VKRequestKeys',
}

class_pattern = re.compile(
    r'(?:(public|internal|private|protected)\s+)?'
    r'(?:sealed\s+|static\s+|abstract\s+)*'
    r'(class|struct)\s+(\S+)'
    r'(?:\s*:\s*([^{]+?))?'
    r'\s*//\s*TypeDefIndex:\s*(\d+)',
    re.MULTILINE,
)

def is_readable(name):
    if len(name) < 3:
        return False
    if name == name.upper():
        return False
    has_lower = any(c.islower() for c in name)
    if not has_lower:
        return False
    # Skip names with dots that are namespace-qualified
    if "." in name and not name.startswith("ICIDCKCOICB"):
        return False
    return True

candidates = []
for m in class_pattern.finditer(text):
    cls_name = m.group(3)
    tdi = int(m.group(5))

    if cls_name in aliased or cls_name in SKIP_NAMES:
        continue
    if not is_readable(cls_name):
        continue

    block_start = text.find("{", m.end())
    if block_start < 0:
        continue
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

    field_count = len(re.findall(r'^\s+\w[\w\[\]<>, ]*\s+\w+;\s*//\s*0x', block, re.MULTILINE))
    method_count = len(re.findall(r'// RVA:', block))

    if field_count + method_count > 2:
        candidates.append((cls_name, tdi, field_count, method_count))

candidates.sort(key=lambda x: -(x[2] + x[3]))

print(f"Found {len(candidates)} game-specific readable unaliased classes")
print(f"\nTop 80 candidates:")
for cls, tdi, fields, methods in candidates[:80]:
    print(f"  {cls}: tdi={tdi}, {fields}f, {methods}m")

output_path = ROOT / "Tools" / "game_class_candidates.json"
with open(output_path, "w", encoding="utf-8") as f:
    json.dump(
        [{"class": c, "tdi": t, "fields": f, "methods": m}
         for c, t, f, m in candidates],
        f, indent=2,
    )
print(f"\nFull list: {output_path}")
