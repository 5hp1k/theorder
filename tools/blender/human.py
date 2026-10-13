"""The foot soldier: a realistic human made with MakeHuman's generator (the MPFB extension for Blender),
dressed, kitted out, rigged and animated here, and exported as models/infantry/soldier.glb.
    blender -b --python tools/blender/human.py -- <output dir> <dir with the unpacked MakeHuman system assets> [preview]
Needs the MPFB extension installed in Blender (extensions.blender.org/add-ons/mpfb) and the
"makehuman_system_assets_cc0" pack from files.makehumancommunity.org (both CC0).
The body, skin, coveralls and boots are MakeHuman's; the helmet, vest, pouches and rifle are built here.
The animation clips (Idle, Run, Air, Death) are keyed here too, pose by pose: see CLIPS at the end.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

ARGS = sys.argv[sys.argv.index("--") + 1:]
OUT, ASSETS = ARGS[0], ARGS[1]
PREVIEW = "preview" in ARGS

from bl_ext.user_default.mpfb.services.humanservice import HumanService  # noqa: E402
from bl_ext.user_default.mpfb.services.targetservice import TargetService  # noqa: E402

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.user_default.mpfb")
scene = bpy.context.scene


def asset(*parts):
    return os.path.join(ASSETS, *parts)


# ── The man ─────────────────────────────────────────────────────────────────────────────────────
macro = TargetService.get_default_macro_info_dict()
macro.update(gender=1.0, age=0.5, muscle=0.78, weight=0.52, height=0.6, proportions=0.6)
macro["race"] = {"asian": 0.1, "caucasian": 0.8, "african": 0.1}
body = HumanService.create_human(scale=0.1, macro_detail_dict=macro)
body.name = "Body"
HumanService.set_character_skin(asset("skins", "young_caucasian_male", "young_caucasian_male.mhmat"), body, skin_type="GAMEENGINE")
rig = HumanService.add_builtin_rig(body, "game_engine", import_weights=True)
rig.name = "Soldier"
for kind, path in (("Eyes", ("eyes", "low-poly", "low-poly.mhclo")), ("Eyebrows", ("eyebrows", "eyebrow001", "eyebrow001.mhclo")),
                   ("Clothes", ("clothes", "male_worksuit01", "male_worksuit01.mhclo")), ("Clothes", ("clothes", "shoes04", "shoes04.mhclo"))):
    HumanService.add_mhclo_asset(asset(*path), body, asset_type=kind, subdiv_levels=0, material_type="GAMEENGINE")
TargetService.bake_targets(body)
bpy.context.view_layer.update()


def bone(name):
    """Head of a bone in the armature's axes: X the man's left, -Y forward, Z up."""
    return rig.data.bones[name].head_local.copy()


# ── Fatigues: the coveralls dyed into a camouflage, the boots blackened ──────────────────────────
import numpy as np  # noqa: E402


def repaint(obj, fn, size=1024):
    for slot in obj.material_slots:
        for node in slot.material.node_tree.nodes:
            if node.type != "TEX_IMAGE" or node.image is None:
                continue
            img = node.image
            if max(img.size) > size:
                img.scale(size, size)
            colour = any(l.to_socket.name == "Base Color" for l in node.outputs[0].links)
            if colour and fn is not None:
                w, h = img.size
                px = np.empty(w * h * 4, np.float32)
                img.pixels.foreach_get(px)
                px = px.reshape(h, w, 4)
                px[..., :3] = fn(px[..., :3], w, h)
                img.pixels.foreach_set(px.ravel())
            img.pack()


def camo(rgb, w, h):
    """Keeps the cloth's light and shade, swaps its colour for blotches of three greens and a brown."""
    rng = np.random.default_rng(3)
    shade = rgb.mean(axis=2, keepdims=True)
    shade = np.clip(shade / max(float(shade.mean()), 1e-3), 0.35, 1.7)
    field = np.zeros((h, w))
    yy, xx = np.mgrid[0:h, 0:w]
    for _ in range(26):  # a sum of slow waves reads as blotches
        fx, fy = rng.integers(1, 9, 2)
        field += np.sin(xx * fx * 2 * np.pi / w + rng.uniform(0, 6.28)) * np.sin(yy * fy * 2 * np.pi / h + rng.uniform(0, 6.28)) / (fx + fy) ** 0.5
    field = (field - field.min()) / (field.max() - field.min())
    tones = np.array([(0.10, 0.12, 0.07), (0.20, 0.23, 0.12), (0.28, 0.26, 0.16), (0.16, 0.13, 0.09)])
    pick = np.digitize(field, [0.38, 0.5, 0.63])
    return np.clip(tones[pick] * shade * 1.15, 0, 1)


for o in scene.objects:
    if o.type != "MESH":
        continue
    if "worksuit" in o.name:
        repaint(o, camo)
    elif "shoes" in o.name:
        repaint(o, lambda rgb, w, h: rgb.mean(axis=2, keepdims=True) * np.array([0.3, 0.27, 0.24]))
    else:
        repaint(o, None)

# ── Kit: helmet, vest with pouches, pack, rifle ──────────────────────────────────────────────────


def material(name, colour, rough=0.8, metal=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*colour, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    m.diffuse_color = (*colour, 1)
    return m


CLOTH = material("Kit_Cloth", (0.13, 0.14, 0.09))
HELMET = material("Kit_Helmet", (0.15, 0.17, 0.1), 0.65)
STRAP = material("Kit_Strap", (0.07, 0.07, 0.06))
STEEL = material("Kit_Steel", (0.05, 0.05, 0.055), 0.45, 0.8)
WOOD = material("Kit_Wood", (0.22, 0.12, 0.06), 0.6)


class Kit:
    """Pieces of kit gathered into one mesh that rides on one bone."""

    def __init__(self, name, bone_name):
        self.name, self.bone_name, self.bm, self.mats = name, bone_name, bmesh.new(), []

    def _slot(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def _add(self, made, mat, matrix):
        verts = made["verts"]
        bmesh.ops.transform(self.bm, matrix=matrix, verts=verts)
        slot = self._slot(mat)
        for f in {f for v in verts for f in v.link_faces}:
            f.material_index = slot
            f.smooth = True

    def box(self, centre, size, mat, rot=None, bevel=0.006):
        made = bmesh.ops.create_cube(self.bm, size=1.0)
        m = Matrix.Translation(centre) @ (rot.to_4x4() if rot else Matrix.Identity(4)) @ Matrix.Diagonal((*size, 1))
        self._add(made, mat, m)
        if bevel:
            edges = list({e for v in made["verts"] for e in v.link_edges})
            bmesh.ops.bevel(self.bm, geom=edges, offset=bevel, segments=1, affect="EDGES")

    def tube(self, a, b, r, mat, r2=None, segs=12):
        a, b = Vector(a), Vector(b)
        made = bmesh.ops.create_cone(self.bm, cap_ends=True, segments=segs, radius1=r, radius2=r if r2 is None else r2, depth=(b - a).length)
        self._add(made, mat, Matrix.Translation((a + b) / 2) @ (b - a).to_track_quat("Z", "Y").to_matrix().to_4x4())

    def dome(self, centre, radii, mat, cut=-0.25, tilt=0.0):
        """The top of an ellipsoid, cut off below 'cut' (in radii): a helmet shell."""
        made = bmesh.ops.create_uvsphere(self.bm, u_segments=20, v_segments=12, radius=1.0)
        gone = [v for v in made["verts"] if v.co.z < cut - 0.12 * v.co.y]  # lower at the back of the neck
        bmesh.ops.delete(self.bm, geom=gone, context="VERTS")
        keep = [v for v in made["verts"] if v.is_valid]
        m = Matrix.Translation(centre) @ Matrix.Rotation(tilt, 4, "X") @ Matrix.Diagonal((*radii, 1))
        bmesh.ops.transform(self.bm, matrix=m, verts=keep)
        slot = self._slot(mat)
        for f in {f for v in keep for f in v.link_faces}:
            f.material_index = slot
            f.smooth = True

    def finish(self):
        mesh = bpy.data.meshes.new(self.name)
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        self.bm.to_mesh(mesh)
        for mat in self.mats:
            mesh.materials.append(mat)
        obj = bpy.data.objects.new(self.name, mesh)
        scene.collection.objects.link(obj)
        # Rigid on its bone: every vertex weighted to it alone.
        obj.vertex_groups.new(name=self.bone_name).add(range(len(mesh.vertices)), 1.0, "REPLACE")
        mod = obj.modifiers.new("Armature", "ARMATURE")
        mod.object = rig
        obj.parent = rig
        if any(m is HELMET for m in self.mats):
            sol = obj.modifiers.new("Shell", "SOLIDIFY")
            sol.thickness = 0.012
            obj.modifiers.move(len(obj.modifiers) - 1, 0)
        return obj


head, neck, chest, waist = bone("head"), bone("neck_01"), bone("spine_03"), bone("spine_01")
top = max((body.matrix_world @ v.co).z for v in body.data.vertices)
print("HEIGHTS head %s neck %s chest %s waist %s top %.3f hand_r %s" % (tuple(round(c, 3) for c in head), tuple(round(c, 3) for c in neck), tuple(round(c, 3) for c in chest), tuple(round(c, 3) for c in waist), top, tuple(round(c, 3) for c in bone("hand_r"))))

k = Kit("Helmet", "head")
skull = Vector((0, head.y - 0.012, (head.z + top) / 2 - 0.004))
k.dome(skull, (0.11, 0.13, 0.116), HELMET, cut=-0.06)
for s in (-1, 1):
    k.box(skull + Vector((s * 0.092, 0.02, -0.075)), (0.006, 0.018, 0.13), STRAP, bevel=0)           # chin straps
    k.box(skull + Vector((s * 0.113, 0.0, -0.01)), (0.012, 0.11, 0.03), STEEL, bevel=0.003)         # side rails
k.box(skull + Vector((0, -0.13, 0.03)), (0.05, 0.016, 0.04), STEEL, bevel=0.003)                     # mount on the brow
k.finish()

k = Kit("Vest", "spine_03")
mid = Vector((0, chest.y, chest.z + 0.06))
k.box(mid + Vector((0, -0.125, 0.02)), (0.3, 0.05, 0.34), CLOTH, bevel=0.015)                        # front plate
k.box(mid + Vector((0, 0.115, 0.03)), (0.3, 0.05, 0.36), CLOTH, bevel=0.015)                         # back plate
for s in (-1, 1):
    k.box(mid + Vector((s * 0.165, -0.005, -0.06)), (0.04, 0.22, 0.16), CLOTH, bevel=0.012)          # cummerbund
    k.box(mid + Vector((s * 0.105, -0.005, 0.215)), (0.055, 0.25, 0.03), STRAP, bevel=0.008)         # shoulder straps
for i in (-1, 0, 1):
    k.box(mid + Vector((i * 0.092, -0.165, -0.07)), (0.08, 0.04, 0.15), CLOTH, bevel=0.008)          # magazine pouches
    k.box(mid + Vector((i * 0.092, -0.17, 0.0)), (0.082, 0.035, 0.03), STRAP, bevel=0.004)
k.box(mid + Vector((0.08, -0.16, 0.12)), (0.09, 0.03, 0.07), CLOTH, bevel=0.006)                     # radio pouch
k.tube(mid + Vector((0.11, -0.16, 0.15)), mid + Vector((0.11, -0.15, 0.3)), 0.004, STEEL, segs=6)    # aerial
k.box(mid + Vector((0, 0.19, 0.02)), (0.26, 0.11, 0.3), CLOTH, bevel=0.02)                           # pack
k.box(mid + Vector((0, 0.25, -0.05)), (0.2, 0.03, 0.12), CLOTH, bevel=0.01)
k.box(mid + Vector((0, -0.03, -0.185)), (0.345, 0.26, 0.05), STRAP, bevel=0.01)                      # belt
k.finish()

# The rifle rides on the chest too, held low across the body, muzzle forward; the hands are posed onto it.
k = Kit("Rifle", "spine_03")
GRIP = mid + Vector((-0.07, -0.22, -0.1))          # where the right hand closes
aim = Matrix.Rotation(math.radians(9), 3, "X") @ Matrix.Rotation(math.radians(10), 3, "Z")


def gun(offset, size, mat, bevel=0.004):
    k.box(GRIP + aim @ Vector(offset), size, mat, rot=aim, bevel=bevel)


gun((0, -0.02, 0.07), (0.045, 0.3, 0.07), STEEL)                 # receiver
gun((0, 0.03, 0.0), (0.035, 0.045, 0.1), STRAP)                  # pistol grip
gun((0, -0.1, -0.04), (0.03, 0.07, 0.17), STEEL)                 # magazine
gun((0, 0.27, 0.055), (0.04, 0.26, 0.06), WOOD, 0.008)           # stock
gun((0, 0.405, 0.045), (0.042, 0.02, 0.1), STRAP)                # butt plate
gun((0, -0.3, 0.07), (0.05, 0.26, 0.055), WOOD, 0.008)           # handguard
gun((0, -0.05, 0.125), (0.02, 0.12, 0.03), STEEL)                # sight rail
k.tube(GRIP + aim @ Vector((0, -0.42, 0.075)), GRIP + aim @ Vector((0, -0.72, 0.075)), 0.011, STEEL)   # barrel
k.tube(GRIP + aim @ Vector((0, -0.72, 0.075)), GRIP + aim @ Vector((0, -0.78, 0.075)), 0.016, STEEL)   # muzzle brake
k.tube(GRIP + aim @ Vector((0, -0.42, 0.105)), GRIP + aim @ Vector((0, -0.6, 0.105)), 0.007, STEEL)    # gas tube
gun((0, -0.62, 0.11), (0.012, 0.012, 0.05), STEEL, 0)            # front sight
k.finish()
HAND_R = GRIP + aim @ Vector((0, 0.055, -0.01))                   # where the wrists go: behind the grip…
HAND_L = GRIP + aim @ Vector((0, -0.17, 0.015))                   # …and under the handguard

# ── Poses and clips ──────────────────────────────────────────────────────────────────────────────
ORDER = [b.name for b in rig.data.bones]   # parents come before their children
for pb in rig.pose.bones:
    pb.rotation_mode = "QUATERNION"


REST = {b.name: b.matrix_local.copy() for b in rig.data.bones}
NEXT = {"upperarm_l": "lowerarm_l", "lowerarm_l": "hand_l", "upperarm_r": "lowerarm_r", "lowerarm_r": "hand_r"}


def set_pose(turns, shift=(0, 0, 0)):
    """Poses the rig from rest. turns: bone → list of steps applied in order about the bone's joint, in
    the armature's own axes (X the man's left, -Y forward, Z up):
      (axis, degrees)            a turn about that axis;
      ("reach", point, pole)     on an upper arm: swing it so that the hand can get to the point, the
                                 elbow kept towards the pole direction;
      ("point", point)           on a forearm: aim it at the point.
    shift moves the pelvis."""
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    for name in ORDER:
        pb = rig.pose.bones[name]
        m = pb.matrix.copy()
        if name == "pelvis" and any(shift):
            m = Matrix.Translation(shift) @ m

        def swing(m, towards):
            """Turns the bone about its joint so that the next joint lies towards the given point."""
            at = m.to_translation()
            now = (m @ (REST[name].inverted() @ REST[NEXT[name]].to_translation())) - at
            q = now.rotation_difference(Vector(towards) - at)
            return Matrix.Translation(at) @ q.to_matrix().to_4x4() @ Matrix.Translation(-at) @ m

        for step in turns.get(name, ()):
            at = m.to_translation()
            if step[0] == "reach":
                target, pole = Vector(step[1]), Vector(step[2])
                l1 = (REST[NEXT[name]].to_translation() - REST[name].to_translation()).length
                l2 = (REST[NEXT[NEXT[name]]].to_translation() - REST[NEXT[name]].to_translation()).length
                d = target - at
                dist = min(d.length, (l1 + l2) * 0.995)
                along = d.normalized()
                a = (l1 * l1 - l2 * l2 + dist * dist) / (2 * dist)
                h = math.sqrt(max(l1 * l1 - a * a, 0.0))
                side = (pole - along * pole.dot(along)).normalized()
                m = swing(m, at + along * a + side * h)
            elif step[0] == "point":
                m = swing(m, step[1])
            else:
                m = Matrix.Translation(at) @ Matrix.Rotation(math.radians(step[1]), 4, step[0]) @ Matrix.Translation(-at) @ m
        if name in turns or name == "pelvis":
            pb.matrix = m
            bpy.context.view_layer.update()


def key(frame):
    for pb in rig.pose.bones:
        pb.keyframe_insert("rotation_quaternion", frame=frame)
    rig.pose.bones["pelvis"].keyframe_insert("location", frame=frame)


def merged(*poses):
    out = {}
    for p in poses:
        for name, turns in p.items():
            out[name] = out.get(name, []) + list(turns)
    return out


# Arms on the rifle: right hand at the grip by the ribs, left hand forward under the handguard. The
# rifle rides on the chest bone, so the hands are sent to where it is in the pose at hand.
def hold(pose=None, shift=(0, 0, 0)):
    """The pose, with the arms brought onto the rifle wherever the chest has carried it."""
    pose = dict(pose or {})
    for name in ("upperarm_l", "lowerarm_l", "upperarm_r", "lowerarm_r", "hand_l", "hand_r"):
        pose.pop(name, None)
    set_pose(pose, shift)
    carry = rig.pose.bones["spine_03"].matrix @ REST["spine_03"].inverted()
    right, left = carry @ HAND_R, carry @ HAND_L
    back, down = carry.to_3x3() @ Vector((0, 1, 0)), carry.to_3x3() @ Vector((0, 0, -1))
    pose["upperarm_r"] = [("reach", right, down + back * 0.9 + carry.to_3x3() @ Vector((-0.5, 0, 0)))]
    pose["lowerarm_r"] = [("point", right)]
    pose["upperarm_l"] = [("reach", left, down + carry.to_3x3() @ Vector((0.55, 0, 0)))]
    pose["lowerarm_l"] = [("point", left)]
    return pose


def run_pose(t):
    """One instant of the run; t goes 0..1 over a full stride (left foot forward first)."""
    turns = {}
    for side, ph in (("l", 0.0), ("r", 0.5)):
        a = 2 * math.pi * (t + ph)
        swing = math.sin(a)
        turns[f"thigh_{side}"] = [("X", -6 - 36 * swing)]
        # the knee folds as the leg comes through, opens to reach, gives a little on landing
        turns[f"calf_{side}"] = [("X", 18 + 62 * max(0.0, math.sin(a - 2.3)) ** 1.2 + 10 * max(0.0, math.sin(a + 0.5)))]
        turns[f"foot_{side}"] = [("X", -12 * swing)]
    twist = 7 * math.sin(2 * math.pi * t)
    turns["pelvis"] = [("Z", -twist * 0.6)]
    turns["spine_01"] = [("X", 9), ("Z", twist)]
    turns["spine_03"] = [("Z", twist * 0.6)]
    turns["head"] = [("X", -5), ("Z", -twist * 0.9)]
    lift = (0, 0, 0.028 * math.cos(4 * math.pi * t) - 0.035)
    return hold(merged({"clavicle_r": [("Z", 6)], "clavicle_l": [("Z", -10)]}, turns), lift), lift


CLIPS = {}


def clip(name, frames):
    """frames: list of (frame, turns, pelvis shift)."""
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    rig.animation_data_create()
    rig.animation_data.action = action
    for frame, turns, shift in frames:
        set_pose(turns, shift)
        key(frame)
    CLIPS[name] = action
    return action


SHOULDERS = {"clavicle_r": [("Z", 6)], "clavicle_l": [("Z", -10)], "head": [("X", 4)]}
breathe = {"spine_02": [("X", -1.5)], "spine_03": [("X", -1.0)]}
stance = merged(SHOULDERS, {"thigh_l": [("X", -5), ("Y", -3)], "thigh_r": [("X", 4), ("Y", 3)], "calf_l": [("X", 7)], "calf_r": [("X", 5)], "foot_l": [("X", -2)], "foot_r": [("X", -9)]})
clip("Idle", [(0, hold(stance, (0, 0, -0.008)), (0, 0, -0.008)), (40, hold(merged(stance, breathe), (0, 0, -0.004)), (0, 0, -0.004)), (80, hold(stance, (0, 0, -0.008)), (0, 0, -0.008))])
clip("Run", [(i, *run_pose(i / 16)) for i in range(17)])
tuck = merged(SHOULDERS, {"thigh_l": [("X", -34)], "calf_l": [("X", 58)], "thigh_r": [("X", 6)], "calf_r": [("X", 34)], "foot_l": [("X", 18)], "foot_r": [("X", 24)], "spine_01": [("X", 6)]})
clip("Air", [(0, hold(tuck), (0, 0, 0)), (12, hold(merged(tuck, {"thigh_l": [("X", -4)], "calf_r": [("X", 6)]})), (0, 0, 0)), (24, hold(tuck), (0, 0, 0))])
hip = bone("pelvis").z
# Shot: the knees give, he goes over backwards, the arms come off the rifle and fall wide.
buckle = merged(SHOULDERS, {"thigh_l": [("X", -30)], "thigh_r": [("X", -18)], "calf_l": [("X", 64)], "calf_r": [("X", 52)], "spine_01": [("X", -10)], "head": [("X", -14)]})
falling = {"pelvis": [("X", -50)], "thigh_l": [("X", 20)], "thigh_r": [("X", 34)], "calf_l": [("X", 70)], "calf_r": [("X", 48)], "spine_02": [("X", -8)], "head": [("X", 16)],
           "upperarm_l": [("Y", 10), ("X", -30)], "upperarm_r": [("Y", -10), ("X", -30)]}
down = {"pelvis": [("X", -86)], "thigh_l": [("X", -4), ("Y", -14)], "thigh_r": [("X", 4), ("Y", 12)], "calf_l": [("X", 22)], "calf_r": [("X", 6)],
        "spine_02": [("X", -5)], "head": [("X", -6), ("Z", 24)], "upperarm_l": [("Y", -8), ("X", 30)], "upperarm_r": [("Y", 4), ("X", 26)],
        "lowerarm_l": [("Z", -30)], "lowerarm_r": [("Z", 40)]}
clip("Death", [(0, hold(stance), (0, 0, 0)), (7, hold(buckle, (0, 0.06, -0.2)), (0, 0.06, -0.2)), (15, falling, (0, 0.28, -hip * 0.55)),
               (22, down, (0, 0.45, -hip + 0.14)), (26, down, (0, 0.45, -hip + 0.125))])
rig.animation_data.action = CLIPS["Idle"]
scene.frame_set(0)


def shots(tag, views, target=(0, 0.95, 0)):
    """Preview renders with the textures on. Views are in game axes (Y up, the man faces +Z)."""
    scene.render.engine = "BLENDER_WORKBENCH"
    sh = scene.display.shading
    sh.light, sh.color_type, sh.show_cavity, sh.show_shadows = "STUDIO", "TEXTURE", True, True
    scene.render.resolution_x, scene.render.resolution_y = 700, 900
    if scene.world is None:
        scene.world = bpy.data.worlds.new("w")
        scene.world.color = (0.32, 0.34, 0.37)
    cam = bpy.data.objects.get("cam")
    if cam is None:
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
        cam.data.lens = 40
        scene.collection.objects.link(cam)
    scene.camera = cam
    gd = lambda p: Vector((p[0], -p[2], p[1]))
    for i, eye in enumerate(views):
        cam.location = gd(eye)
        cam.rotation_euler = (gd(target) - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(OUT, f"{tag}_{i}.png")
        bpy.ops.render.render(write_still=True)


os.makedirs(OUT, exist_ok=True)
if PREVIEW:
    AROUND = [(1.6, 1.5, 2.6), (-2.6, 1.2, 0.3), (0.0, 1.3, 3.0), (2.4, 1.2, -1.6)]
    for name, frames in (("Idle", (0,)), ("Run", (0, 4, 8, 12)), ("Air", (0,)), ("Death", (7, 15, 26))):
        rig.animation_data.action = CLIPS[name]
        for f in frames:
            scene.frame_set(f)
            shots(f"{name}{f:02d}", AROUND if name == "Idle" else [(-3.0, 1.1, 0.4), (1.8, 1.3, 2.6)], (0, 0.85, 0))
    rig.animation_data.action = CLIPS["Idle"]
    scene.frame_set(0)

# ── Out ──────────────────────────────────────────────────────────────────────────────────────────
for o in scene.objects:
    o.select_set(o.type in ("MESH", "ARMATURE"))
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, "soldier.glb"), export_format="GLB", use_selection=True, export_apply=True,
                          export_animations=True, export_animation_mode="ACTIONS", export_force_sampling=True, export_frame_range=False,
                          export_image_format="JPEG", export_jpeg_quality=85, export_yup=True, export_skins=True, export_def_bones=False,
                          export_optimize_animation_size=True)
print("EXPORTED", os.path.getsize(os.path.join(OUT, "soldier.glb")) // 1024, "KiB")
