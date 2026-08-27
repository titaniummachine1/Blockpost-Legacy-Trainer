import json
with open('Tools/sdk_aliases.json', encoding='utf-8') as f:
    data = json.load(f)
new_classes = ['HUDNames', 'VWGen', 'VCGen', 'CharAnimator', 'GUICraft', 'GUIGameExit', 'HUDMessage', 'HUDTab', 'GUICase', 'GUIGameSet']
for cls in new_classes:
    present = cls in data
    status = "aliased" if present else "NOT aliased"
    print(f"{cls}: {status}")
