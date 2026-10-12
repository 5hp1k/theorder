"""Turns the props exported by props.py into scenes.
    python3 tools/blender/make_props.py <dir with the .glb/.json pairs> [godot binary]
Meshes go to models/city/<name>/, scenes to scenes/city/<name>.tscn:
  street  - a Destructible with no collision of its own (a mech walks through it and it falls);
  car     - a Destructible with a box round it, blown up into a wreck;
  fitting - plain meshes, hung on a building by ChunkedBuilding.
"""
import glob, json, os, subprocess, sys

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
src = sys.argv[1]
godot = sys.argv[2] if len(sys.argv) > 2 else 'godot'
MATERIALS = {'joint': 'mech_joint', 'glass': 'car_glass', 'tyre': 'car_tyre', 'steel': 'street_steel', 'concrete': 'concrete',
             'wood': 'wood_paint', 'glow': 'lamp_glow'}


def material(key):
    return 'res://materials/%s.tres' % MATERIALS.get(key, key)


for path in sorted(glob.glob(os.path.join(src, '*.json'))):
    data = json.load(open(path))
    name = os.path.basename(path)[:-5]
    spec = data['spec']
    models = 'res://models/city/' + name
    subprocess.run([godot, '--headless', '--path', ROOT, '-s', 'tools/blender/import_parts.gd', '--', path[:-5] + '.glb', models],
                   check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    ext, nodes, ids = [], [], {}

    def res(kind, p):
        if p not in ids:
            ids[p] = str(len(ids) + 1)
            ext.append('[ext_resource type="%s" path="%s" id="%s"]' % (kind, p, ids[p]))
        return 'ExtResource("%s")' % ids[p]

    title = ''.join(w.title() for w in name.split('_'))
    kind = spec['kind']
    if kind == 'fitting':
        head = ['[node name="%s" type="Node3D"]' % title]
    else:
        fx = 'res://scenes/fx/explosion_small.tscn' if kind == 'car' else 'res://scenes/fx/dust_puff.tscn'
        head = ['[node name="%s" type="StaticBody3D"]' % title, 'collision_layer = %d' % (32 if kind == 'car' else 0), 'collision_mask = 0',
                'script = ' + res('Script', 'res://scripts/World/Destructible.cs'), 'Hp = %g' % spec['hp'], 'How = %d' % spec['mode'],
                'Footprint = Vector2(%g, %g)' % tuple(spec['footprint']), 'Height = %g' % spec['height'], 'DestroyFx = ' + res('PackedScene', fx)]
        if kind == 'car':
            head.append('CharredMaterial = ' + res('Material', 'res://materials/mech_charred.tres'))
    subs = []
    for part in data['parts']:
        nodes.append('[node name="%s" type="MeshInstance3D" parent="."]\nmesh = %s\nmaterial_override = %s' % (
            part['node'], res('ArrayMesh', '%s/%s.res' % (models, part['mesh'])), res('Material', material(part['material']))))
        if part['material'] in ('glow',) or part['material'].startswith('light_'):
            nodes[-1] += '\ncast_shadow = 0'
    if kind == 'car':
        w, h, l = spec['shape']
        subs.append('[sub_resource type="BoxShape3D" id="shape"]\nsize = Vector3(%g, %g, %g)' % (w, h, l))
        nodes.append('[node name="Collision" type="CollisionShape3D" parent="."]\ntransform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, %g, 0)\nshape = SubResource("shape")' % (spec['height'] - h / 2))
    text = '[gd_scene format=3]\n\n' + '\n'.join(ext) + '\n\n' + ''.join(s + '\n\n' for s in subs) + '\n'.join(head) + '\n\n' + '\n\n'.join(nodes) + '\n'
    open(os.path.join(ROOT, 'scenes', 'city', name + '.tscn'), 'w').write(text)
    print(name, kind, len(data['parts']))
