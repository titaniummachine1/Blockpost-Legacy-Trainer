import json
with open('Tools/call_graph.json', encoding='utf-8') as f:
    g = json.load(f)
classes = g.get('classes', {})
for t in ['Controll', 'KBBBHJDINCB', 'PLH', 'Client', 'Movement']:
    present = t in classes
    mc = classes.get(t, {}).get('method_count', '?')
    print(f'{t}: present={present} methods={mc}')
print(f'Total classes in graph: {len(classes)}')

import re
with open('.tools/Il2CppDumper/dump.cs', encoding='utf-8') as f:
    text = f.read()
all_classes = re.findall(r'class (\w+)\s', text)
print(f'Total class definitions in dump: {len(all_classes)}')
print(f'Controll in dump: {"Controll" in all_classes}')
print(f'KBBBHJDINCB in dump: {"KBBBHJDINCB" in all_classes}')
