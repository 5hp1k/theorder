"""Hangs the meshes of an imported model on the nodes of a unit's scene.
    python3 tools/blender/apply_model.py <manifest.json> <scene.tscn> <res://models/...>
Each part of the manifest names the node it belongs to (created if missing) and its material.
The primitive meshes that stood in for the model on the same parents are removed."""
import json, os, sys
sys.path.insert(0, os.path.dirname(__file__))
from tscn import *

manifest_path, scene_path, models = sys.argv[1:4]
data = json.load(open(manifest_path))
sc = Scene(scene_path)
ext = {x.attr('path'): x.attr('id') for x in sc.sections if x.kind == 'ext_resource'}
MATERIALS = {'camo': 'res://materials/camo_desert.tres', 'joint': 'res://materials/mech_joint.tres', 'tyre': 'res://materials/car_tyre.tres', 'panel': 'res://materials/cockpit_panel.tres', 'grey': 'res://materials/ship_grey.tres',
             'accent': 'res://materials/mech_accent.tres', 'glass': 'res://materials/mech_canopy.tres'}
wanted = {}
for part in data['parts']:
    wanted.setdefault(part['parent'], []).append(part)

for parent, group in sorted(wanted.items(), key=lambda kv: kv[0].count('/')):
    names = {p['node'] for p in group}
    # what the model replaces
    for n in sc.children(parent):
        if n.attr('type') == 'MeshInstance3D' and n.attr('name') not in names and not n.attr('name').startswith('Insignia'):
            for child in [c for c in sc.nodes() if (c.attr('parent') or '').startswith(n.path)]:
                sc.remove(child)
            sc.remove(n)
    anchor = sc.node(parent)
    for part in group:
        path = part['node'] if parent == '.' else parent + '/' + part['node']
        n = sc.node(path)
        if n is None:
            n = sc.add_node(part['node'], 'MeshInstance3D', parent, [], after=anchor)
        anchor = n
        if part.get('origin') is not None:
            n.set('transform', fmt_xf(np.eye(3), np.array(part['origin'])))
        elif not part.get('keep_place'):
            n.remove('transform')
        n.set('mesh', f'ExtResource("{sc.ext(models + "/" + part["mesh"] + ".res", "ArrayMesh")}")')
        n.set('material_override', f'ExtResource("{sc.ext(MATERIALS.get(part["material"], "res://materials/" + part["material"] + ".tres"), "Material")}")')
for path, (rows, origin) in data.get('frames', {}).items():
    n = sc.node(path)
    if n is not None: n.set('transform', fmt_xf(np.array(rows), np.array(origin)))
for path, script in data.get('scripts', {}).items():
    n = sc.node(path)
    if n is not None: n.set('script', f'ExtResource("{sc.ext(script, "Script")}")')
for path in data.get('remove', []):
    n = sc.node(path)
    if n is not None: sc.remove(n)
for path, origin in data.get('move', {}).items():
    n = sc.node(path)
    if n is None: continue
    m, _ = parse_xf(n.get('transform'))
    n.set('transform', fmt_xf(m, np.array(origin)))
root = sc.nodes()[0]
for prop, mesh in data.get('root', {}).items():
    root.set(prop, f'ExtResource("{sc.ext(models + "/" + mesh + ".res", "ArrayMesh")}")')
sc.prune()
sc.save()
print('applied', len(data['parts']), 'parts to', scene_path)
