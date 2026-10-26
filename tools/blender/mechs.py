"""The four mechs.
    blender -b --python tools/blender/mechs.py -- <output dir> <ostrich|hound|ballista|panther|hades|agni|executioner|mantis|mauler|direwolf> [preview]
Writes <name>.glb (one mesh per part, each in the axes of the scene node it hangs on) and <name>.json
(which node each mesh belongs to); with "preview", also studio renders of the assembled machine.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from meshkit import *  # noqa: E402
from walker import *  # noqa: E402

ARGS = sys.argv[sys.argv.index("--") + 1:]


# ── The house style ─────────────────────────────────────────────────────────────────────────────
# Slab-sided and square, like a machine welded up out of plate: every lofted section of the hull and
# the limbs has its corners cut only slightly, and tapers far less towards its ends than it was drawn.
SQUARE = 0.3      # share of the drawn corner cut that is kept
BLOCKY = 0.6      # how far each section is pushed out towards the biggest section of its part


def lifting_eyes(p, points, r=0.09):
    """Rings welded to the roof to sling the machine from."""
    for x, y, z in points:
        with p.detail((x, y, z), (0, 1, 0)):
            p.box((x, y + r * 0.5, z), (0.05, r * 1.6, r * 2.4), FRAME, 0.012)
            p.cyl((x - 0.03, y + r * 0.8, z), (x + 0.03, y + r * 0.8, z), r * 0.5, segs=8, kind=RECESS, bevel=0)
_stations = Part.stations


def _slab_stations(self, axis, rows, kind=PAINT, bevel=0.025, seams=None, cuts=(), chamfer=0.1):
    if self.name.startswith(("Head_", "Cockpit", "Antenna")) or kind == GLASS:
        return _stations(self, axis, rows, kind, bevel, seams, cuts, chamfer)
    wide, high = max(r[1] for r in rows), max(r[2] for r in rows)
    out = []
    for r in rows:
        r = list(r)
        # thin things (struts, blades, lips) stay as they are; bodies are squared up
        if min(wide, high) > 0.3:
            r[1] += (wide - r[1]) * BLOCKY
            r[2] += (high - r[2]) * BLOCKY
        if len(r) > 5:
            r[5] *= SQUARE
        if len(r) > 6:
            r[6] *= SQUARE
        out.append(tuple(r))
    return _stations(self, axis, out, kind, bevel, seams, cuts, chamfer * SQUARE)


Part.stations = _slab_stations


def bird_torso(model, k, nose="blunt", stacks=True, guns=(), heavy=False):
    """Body of a chicken-walker in the axes of Hips/Torso (turret ring at the origin, nose to -Z),
    at the Ostrich's size times k. guns: (x, y, z of the breech, length, radius) of fixed nose lasers,
    at the Ostrich's size."""
    T = "Hips/Torso"
    p = Part("CenterTorso_Armor")
    # the chin: a blunt block under the canopy, drawn square as it is (so the glass stays clear of it)
    if nose == "sharp":
        rows = [(-3.3, 0.95, 0.55, 0, 0.98, 0.05), (-2.6, 1.4, 0.95, 0, 1.1, 0.06), (-1.7, 1.85, 1.5, 0, 1.2, 0.08), (-1.15, 1.6, 1.9, 0, 1.1, 0.08)]
    else:
        rows = [(-3.05, 1.45, 0.72, 0, 0.98, 0.05), (-2.55, 1.72, 1.02, 0, 1.08, 0.06), (-1.7, 1.95, 1.5, 0, 1.2, 0.08), (-1.15, 1.6, 1.9, 0, 1.1, 0.08)]
    _stations(p, "z", rows, seams=PANELS, cuts=[((0, 0, -2.1), (0, 0, 1))])
    p.stations("z", [(-1.15, 1.42, 1.95, 0, 1.1, 0.2), (1.75, 1.42, 1.95, 0, 1.1, 0.2)], seams=PANELS, cuts=[((0, 0, 0.3), (0, 0, 1))])
    p.stations("z", [(-1.05, 0.5, 0.2, 0, 2.1, 0.06), (-0.6, 0.82, 0.34, 0, 2.17, 0.1), (1.3, 0.82, 0.34, 0, 2.17, 0.1), (1.7, 0.6, 0.2, 0, 2.1, 0.06)], seams=SMALL_PANELS)
    p.stations("z", [(-2.0, 0.9, 0.2, 0, 0.5, 0.06), (-1.0, 1.2, 0.3, 0, 0.22, 0.1), (1.3, 1.2, 0.3, 0, 0.22, 0.1), (1.7, 1.0, 0.2, 0, 0.3, 0.06)])
    model.add(p.scale(k), T + "/CenterTorso", "Armor", CAMO)

    f = Part("CenterTorso_Frame")
    f.stations("z", [(1.7, 2.3, 1.5, 0, 1.35, 0.2), (2.6, 2.2, 1.4, 0, 1.4, 0.28), (2.95, 1.7, 1.0, 0, 1.4, 0.2)], kind=FRAME, seams=SMALL_PANELS)
    f.slats((0, 1.4, 2.96), (0, 0, 1), (1, 0, 0), (1.3, 0.75), 6, 0.09)
    for s in (-1, 1):
        if stacks:
            # square exhaust stacks
            f.box((s * 0.75, 2.3, 2.4), (0.36, 0.8, 0.46), FRAME, 0.02)
            f.box((s * 0.75, 2.705, 2.4), (0.26, 0.02, 0.36), RECESS, 0)
        # a grab rail along the roof and a stowage bin behind it (both seated on the roof: see Part.detail)
        with f.detail((s * 0.6, 2.35, 0.35), (0, 1, 0)):
            f.box((s * 0.6, 2.5, 0.35), (0.04, 0.04, 1.5), FRAME, 0.008)
            for z in (-0.35, 0.35, 1.05):
                f.box((s * 0.6, 2.42, z), (0.032, 0.14, 0.032), FRAME, 0.006)
        with f.detail((s * 0.42, 2.32, 1.45), (0, 1, 0)):
            f.box((s * 0.42, 2.42, 1.45), (0.5, 0.2, 0.36), FRAME, 0.02)
        f.slats((s * 1.16, 1.4, 2.45), (s, 0, 0), (0, 1, 0), (0.7, 0.7), 5, 0.06)  # behind the side torsos
    # the turret ring, and its seal (a little above the pelvis's ring, whose underside would be flush with it)
    f.cyl((0, -0.62, 0), (0, 0.2, 0), 0.78, segs=20, kind=FRAME)
    f.cyl((0, -0.28, 0), (0, -0.12, 0), 0.95, segs=20, kind=SOFT)
    f.box((0, 0.6, -2.4), (0.5, 0.3, 0.6), FRAME)
    f.cyl((0, 0.6, -2.7), (0, 0.6, -2.8), 0.11, segs=10, kind=RECESS, bevel=0)
    # a bumper bar across under the chin on two brackets, with towing shackles (the brackets' tops are
    # let a few centimetres up into the underside of the chin, which is higher under the sharp nose)
    up = 0.07 if nose == "sharp" else 0.0
    f.box((0, 0.42 + up, -2.92), (1.7, 0.2, 0.16), FRAME, 0.03)
    for s in (-1, 1):
        f.box((s * 0.6, 0.5 + up, -2.68), (0.12, 0.24, 0.5), FRAME, 0.02)
        f.cyl((s * 0.72, 0.3 + up, -2.95), (s * 0.72, 0.3 + up, -3.05), 0.07, segs=8, kind=SOFT)
    lifting_eyes(f, [(sx * 0.95, 2.16, z) for sx in (-1, 1) for z in (-0.9, 1.5)])
    # radiator fins stacked down the back of the engine housing
    for i in range(5):
        f.box((0, 0.75 + i * 0.07, 2.99), (1.5, 0.03, 0.12), FRAME, 0)
    # mounts of the nose guns (the guns themselves are the weapons' own models, see weapons.py)
    for x, y, z, length, r in guns:
        f.box((x, y, z + 0.2), (r * 3.2, r * 3.0, 0.5), FRAME)
    model.add(f.scale(k), T + "/CenterTorso", "Frame", JOINT)

    h = Part("Head_Armor")
    # (running a little way into the body, so its back is not flush with the chin's)
    h.stations("z", [(-2.0, 1.36, 0.22, 0, 2.08, 0.08), (-1.55, 1.5, 0.36, 0, 2.04, 0.12), (-1.1, 1.3, 0.25, 0, 2.02, 0.08)], seams=SMALL_PANELS)
    tip = -3.2 if nose == "sharp" else -2.97
    h.stations("z", [(tip, 0.09, 0.06, 0, 1.56 if nose != "sharp" else 1.4, 0.01), (-2.35, 0.09, 0.06, 0, 1.985, 0.01), (-1.95, 0.09, 0.06, 0, 2.14, 0.01)], bevel=0.008)
    for s in (-1, 1):
        h.stations("z", [(tip, 0.07, 0.07, s * 0.22, 1.47 if nose != "sharp" else 1.3, 0.01), (-2.35, 0.07, 0.07, s * 0.58, 1.6, 0.01), (-1.9, 0.07, 0.07, s * 0.66, 1.66, 0.01)], bevel=0.008)
    model.add(h.scale(k), T + "/Head", "Armor", CAMO)

    g = Part("Head_Glass")
    if nose == "sharp":
        g.stations("z", [(-3.2, 0.42, 0.1, 0, 1.34, 0.04), (-2.35, 1.12, 0.5, 0, 1.72, 0.16), (-1.85, 1.3, 0.62, 0, 1.82, 0.2)], bevel=0.012)
    else:
        g.stations("z", [(-2.95, 0.62, 0.1, 0, 1.5, 0.04), (-2.35, 1.15, 0.5, 0, 1.72, 0.16), (-1.85, 1.3, 0.62, 0, 1.82, 0.2)], bevel=0.012)
    model.add(g.scale(k), T + "/Head", "Canopy", GLASS)

    left = Part("LeftTorso_Armor")
    left.stations("z", [(-1.8, 0.7, 1.0, -1.12, 1.2, 0.15), (-1.25, 1.12, 1.85, -1.27, 1.12, 0.26), (1.35, 1.12, 2.0, -1.27, 1.1, 0.26), (1.95, 0.85, 1.45, -1.2, 1.2, 0.2)],
                  seams=PANELS, cuts=[((0, 0, 0.1), (0, 0, 1))])
    left.slats((-1.2, 1.22, -1.57), (0, 0.25, -1), (1, 0, 0), (0.5, 0.55), 4, 0.07)
    # a skirt along the lower edge, a stowage locker behind the shoulder
    left.box((-1.72, 0.3, 0.2), (0.14, 0.5, 2.6), PAINT, 0.03, SMALL_PANELS)
    left.box((-1.4, 2.2, 1.45), (0.7, 0.26, 0.7), PAINT, 0.03, SMALL_PANELS)
    model.both(left.scale(k), T + "/LeftTorso", "Armor", CAMO)

    model.removes += [T + "/Stripe"]

    a = Part("Antenna")
    # (in the middle of the locker's lid, well in from its edges)
    with a.detail((-1.4, 2.09, 1.45), (0, 1, 0)):
        a.box((-1.4, 2.18, 1.45), (0.26, 0.18, 0.26), FRAME)
        a.cyl((-1.4, 2.25, 1.45), (-1.4, 3.7, 1.45), 0.035, 0.012, segs=6, kind=FRAME, bevel=0)
    # (clear of the lifting eye beside it)
    with a.detail((-0.82, 2.09, 1.6), (0, 1, 0)):
        a.cyl((-0.82, 2.2, 1.6), (-0.82, 2.9, 1.6), 0.025, 0.01, segs=6, kind=FRAME, bevel=0)
    model.add(a.scale(k), T, "Antenna", JOINT)


def gun_pods(model, k, reach=0.0):
    """Arms that are gun pods: a laser over an autocannon. Shoulder axes, left arm (body on its +X).
    reach: how much further out the arms are than the scene has them (the shoulder drum is longer by as
    much), metres."""
    T = "Hips/Torso"
    lf = Part("LeftTorso_Frame")
    lf.cyl((-1.7, 1.0, -0.3), (-2.02 - reach / k, 1.0, -0.3), 0.5, segs=16, kind=FRAME)
    lf.cyl((-1.8, 1.0, -0.3), (-1.9, 1.0, -0.3), 0.6, segs=16, kind=SOFT)
    model.both(lf.scale(k), T + "/LeftTorso", "Frame", JOINT)

    arm = Part("ArmL_Armor")
    arm.stations("z", [(0.8, 0.6, 0.7, 0, 0, 0.12), (0.35, 1.0, 1.1, 0, 0, 0.2), (-1.5, 1.0, 1.1, 0, 0, 0.2), (-2.1, 0.82, 0.9, 0, 0, 0.18)],
                 seams=PANELS, cuts=[((0, 0, -0.6), (0, 0, 1))])
    arm.slats((-0.51, 0.12, 0.05), (-1, 0, 0), (0, 0, 1), (0.5, 0.5), 5, 0.05)
    model.both(arm.scale((k * 1.14, k * 1.12, k)), T + "/ArmL/Hit", "Armor", CAMO)

    af = Part("ArmL_Frame")
    af.cyl((0.3, 0, 0), (0.64, 0, 0), 0.4, segs=16, kind=FRAME)
    af.box((0, 0, -2.12), (0.72, 0.82, 0.12), FRAME)                 # the plate the guns stand on
    af.cyl((-0.36, -0.62, -0.5), (0.36, -0.62, -0.5), 0.3, segs=14, kind=FRAME)  # ammunition drum
    af.box((0, -0.5, -1.25), (0.3, 0.2, 1.1), FRAME)
    model.both(af.scale((k * 1.14, k * 1.12, k)), T + "/ArmL/Hit", "Frame", JOINT)


def missile_racks(model):
    """Arms that are box launchers on yokes. Shoulder axes in scene units, left arm (body on its +X)."""
    T = "Hips/Torso"
    r = Part("ArmL_Armor")
    r.stations("z", [(1.23, 1.5, 1.3, 0, 0, 0.15), (0.9, 1.9, 1.68, 0, 0, 0.22), (-1.9, 1.9, 1.68, 0, 0, 0.22), (-2.13, 1.76, 1.54, 0, 0, 0.18)],
               seams=PANELS, cuts=[((0, 0, -0.5), (0, 0, 1))])
    r.slats((0, 0, 1.24), (0, 0, 1), (1, 0, 0), (1.0, 0.8), 5, 0.07)
    model.both(r, T + "/ArmL/Hit", "Armor", CAMO)
    model.moves["Hips/Torso/ArmL/Insignia"] = (-0.975, 0.0, 0.45)
    model.moves["Hips/Torso/ArmR/Insignia"] = (0.975, 0.0, 0.45)

    f = Part("ArmL_Frame")
    # the face of the launcher (its tubes are the fitted launcher's own model)
    f.box((0, 0, -2.15), (1.62, 1.4, 0.1), FRAME, 0.02)
    # yoke down to the shoulder of the hull
    f.stations("x", [(0.5, 1.0, 0.5, 0.34, -0.62, 0.1), (1.6, 0.8, 0.7, 0.34, -0.9, 0.12)], kind=FRAME, seams=SMALL_PANELS)
    f.cyl((0.4, -0.62, 0.34), (1.0, -0.62, 0.34), 0.42, segs=16, kind=FRAME)
    for s in (-1, 1):
        piston(f, (1.35, -1.0, 0.34 + s * 0.3), (0.5, -0.5, -0.9 + s * 0.3), 0.08)
    model.both(f, T + "/ArmL/Hit", "Frame", JOINT)


def ostrich():
    m = Model("ostrich", "scenes/mech/ostrich.tscn")
    bird_torso(m, 1.0)
    gun_pods(m, 1.0)
    hips(m, 0.95)
    cockpit(m)
    legs(m, (2.7, 0.95, 1.15), (2.9, 0.75, 0.85), (1.6, 0.55, 0.6), (1.3, 0.35, 2.3), (0.7, 0.6, 0.5))
    return m


def hound():
    k = 0.8
    m = Model("hound", "scenes/mech/hound.tscn")
    bird_torso(m, k, nose="sharp", stacks=False, guns=[(0, 0.9, -2.7, 1.05, 0.15)])
    # (the arms a little further out than the Ostrich's: their guns came down into the thighs as it walked)
    gun_pods(m, k, reach=0.2)
    m.moves["Hips/Torso/ArmL"] = (-2.08, 0.8, -0.24)
    m.moves["Hips/Torso/ArmR"] = (2.08, 0.8, -0.24)
    hips(m, k, jets=[(-0.72, -0.32, 0.88), (0.72, -0.32, 0.88)])
    cockpit(m)
    legs(m, (2.484, 0.76, 0.92), (2.668, 0.6, 0.68), (1.472, 0.44, 0.48), (1.04, 0.28, 1.84), (0.56, 0.48, 0.4))
    return m


def shoulder_racks(model, k, lift=0.0, back=0.0, pedestal=0.0):
    """Box missile launchers carried high on the shoulders, over the side torsos (axes of Hips/Torso).
    pedestal: how much taller the pedestals are, down from under the boxes."""
    T = "Hips/Torso"
    r = Part("LeftTorso_Rack")
    # the box, a little nose-down, on a pedestal
    r.stations("z", [(1.25, 1.25, 1.0, -1.3, 2.75, 0.08), (-1.55, 1.25, 1.0, -1.3, 2.6, 0.08)], seams=PANELS)
    model.both(r.move((0, lift, back)).scale(k), T + "/LeftTorso", "Rack", CAMO)
    f = Part("LeftTorso_RackFace")
    f.box((-1.3, 2.6, -1.58), (1.1, 0.86, 0.08), FRAME, 0.02)       # face (the tubes: the launcher's model)
    f.box((-1.3, 2.1 - pedestal / 2, 0.1), (0.7, 0.34 + pedestal, 1.2), FRAME, 0.03)          # pedestal
    f.slats((-1.3, 2.68, 1.26), (0, 0, 1), (1, 0, 0), (0.9, 0.7), 5, 0.06)
    model.both(f.move((0, lift, back)).scale(k), T + "/LeftTorso", "RackFace", JOINT)
    # the launchers' tubes (their own models) stand on the middle of the faces
    model.moves[T + "/HardpointRackL"] = (-1.3 * k, (2.6 + lift) * k, (-1.59 + back) * k)
    model.moves[T + "/HardpointRackR"] = (1.3 * k, (2.6 + lift) * k, (-1.59 + back) * k)


def panther():
    """A heavy in the manner of the Mad Cat: bird legs, a low cab thrust forward, each arm a long
    weapon pod carried at the elbow, a missile box on each shoulder. Built on the Ostrich's frame."""
    m = Model("panther", "scenes/mech/panther.tscn")
    bird_torso(m, 1.0, heavy=True, guns=[(-0.6, 0.85, -2.75, 0.95, 0.13), (0.6, 0.85, -2.75, 0.95, 0.13)])
    T = "Hips/Torso"
    lf = Part("LeftTorso_Frame")
    # shoulder joint and the upper arm hanging from it, down to the elbow the pod pivots on
    lf.cyl((-1.7, 1.0, -0.3), (-2.1, 1.0, -0.3), 0.55, segs=16, kind=FRAME)
    lf.cyl((-1.82, 1.0, -0.3), (-1.92, 1.0, -0.3), 0.66, segs=16, kind=SOFT)
    m.both(lf, T + "/LeftTorso", "Frame", JOINT)
    arm = Part("ArmL_Armor")
    # the pod: a long slab-sided box, deeper than it is wide, with a chin under the muzzles
    arm.stations("z", [(1.0, 0.95, 1.5, 0, -0.1, 0.1), (-2.3, 0.95, 1.5, 0, -0.1, 0.1), (-2.7, 0.85, 1.3, 0, -0.1, 0.1)], seams=PANELS, cuts=[((0, 0, -0.7), (0, 0, 1))])
    arm.slats((0, -0.1, 1.01), (0, 0, 1), (1, 0, 0), (0.7, 1.0), 5, 0.06)
    m.both(arm, T + "/ArmL/Hit", "Armor", CAMO)
    af = Part("ArmL_Frame")
    af.cyl((0.3, 0, 0), (0.7, 0, 0), 0.46, segs=16, kind=FRAME)   # elbow trunnion
    af.box((0, -0.1, -2.72), (0.8, 1.26, 0.1), FRAME)             # the plate the guns stand on
    m.both(af, T + "/ArmL/Hit", "Frame", JOINT)
    m.moves[T + "/ArmL/Insignia"] = (-0.535, 0.0, -0.4)
    m.moves[T + "/ArmR/Insignia"] = (0.535, 0.0, -0.4)
    shoulder_racks(m, 1.0)
    hips(m, 0.92)                      # (its axles a little thinner than the hip drums' bores: not flush with them)
    cockpit(m)
    legs(m, (2.7, 1.05, 1.25), (2.9, 0.82, 0.92), (1.6, 0.6, 0.65), (1.4, 0.38, 2.4), (0.76, 0.66, 0.55))
    return m


def ballista():
    k = 1.12
    m = Model("ballista", "scenes/mech/ballista.tscn")
    bird_torso(m, k, heavy=True, guns=[(-0.65, 0.9, -2.75, 1.05, 0.15), (0.65, 0.9, -2.75, 1.05, 0.15)])
    missile_racks(m)
    hips(m, k)
    cockpit(m)
    legs(m, (2.835, 1.064, 1.288), (3.045, 0.84, 0.952), (1.68, 0.616, 0.672), (1.456, 0.392, 2.576), (0.784, 0.672, 0.56))
    return m


def cleaver(kind="cleaver"):
    """The man-shaped frame: upright torso, a head, arms with elbows and fists. Four machines stand on it:
    cleaver   — the brawler with an axe in the right hand and a gun in the left forearm;
    hades     — a gun platform in the manner of the Thanatos: a weapon pod for a right forearm, a fist
                and a gun on the left, missile boxes on broad shoulders;
    panther   — in the manner of the Mad Cat, but with arms and hands: a laser in each forearm, missile
                boxes on the shoulders;
    gladiator — a fast brawler: two bare fists, a shield on the left forearm, a mace in the right hand."""
    heavy = kind == "hades"
    m = Model(kind, f"scenes/mech/{kind}.tscn")
    T = "Hips/Torso"
    p = Part("CenterTorso_Armor")
    # abdomen and chest
    p.stations("y", [(0.05, 1.15, 1.25, 0, 0, 0.18), (1.1, 1.45, 1.75, 0, 0, 0.25), (2.2, 1.5, 2.05, 0, -0.05, 0.28), (2.78, 1.3, 1.6, 0, 0, 0.25)], seams=PANELS, cuts=[((0, 1.5, 0), (0, 1, 0))])
    # breastplate
    p.stations("z", [(-1.78, 1.2, 0.7, 0, 1.85, 0.16), (-1.35, 1.82, 1.16, 0, 1.8, 0.24), (-0.95, 1.7, 1.3, 0, 1.75, 0.2)], seams=SMALL_PANELS)
    # collar round the neck
    p.stations("y", [(2.7, 1.0, 1.1, 0, -0.1, 0.2), (2.95, 0.8, 0.9, 0, -0.1, 0.15)])
    if kind == "gladiator":
        # a studded belt (the middle of the chest is the chest gun's)
        p.stations("y", [(0.25, 1.75, 1.55, 0, 0, 0.12), (0.55, 1.75, 1.55, 0, 0, 0.12)], kind=FRAME, seams=SMALL_PANELS)
        p.bolts([(x, 0.4, -0.79) for x in (-0.6, -0.2, 0.2, 0.6)], (0, 0, -1), r=0.07, h=0.06)
    m.add(p, T + "/CenterTorso", "Armor", CAMO)

    f = Part("CenterTorso_Frame")
    # reactor pack on the back
    f.stations("z", [(0.95, 1.6, 1.6, 0, 1.62, 0.2), (1.65, 1.72, 1.72, 0, 1.62, 0.28), (1.92, 1.3, 1.25, 0, 1.62, 0.2)], kind=FRAME, seams=SMALL_PANELS)
    f.slats((0, 1.62, 1.93), (0, 0, 1), (1, 0, 0), (0.95, 0.85), 6, 0.08)
    for s in (-1, 1):
        f.cyl((s * 0.55, 2.3, 1.3), (s * 0.6, 2.85, 1.6), 0.16, segs=12, kind=FRAME)
        f.cyl((s * 0.598, 2.83, 1.59), (s * 0.602, 2.87, 1.61), 0.11, segs=12, kind=RECESS, bevel=0)
    # radiator fins down the back of the pack, lifting eyes on its top
    for i in range(6):
        f.box((0, 1.0 + i * 0.07, 1.96), (1.3, 0.03, 0.12), FRAME, 0)
    lifting_eyes(f, [(sx * 0.6, 2.47, 1.1) for sx in (-1, 1)])
    # waist
    f.cyl((0, -0.6, 0), (0, 0.15, 0), 0.72, segs=20, kind=FRAME)
    f.cyl((0, -0.28, 0), (0, -0.12, 0), 0.88, segs=20, kind=SOFT)
    # neck
    f.cyl((0, 2.68, -0.15), (0, 3.0, -0.15), 0.32, segs=14, kind=FRAME)
    m.add(f, T + "/CenterTorso", "Frame", JOINT)

    h = Part("Head_Armor")
    # helmet: brow, crown, jaw
    h.stations("z", [(-0.76, 0.7, 0.5, 0, 3.12, 0.12), (-0.45, 0.95, 0.86, 0, 3.18, 0.2), (0.2, 0.95, 0.86, 0, 3.18, 0.22), (0.34, 0.7, 0.6, 0, 3.2, 0.15)], seams=SMALL_PANELS)
    h.stations("z", [(-0.82, 0.5, 0.2, 0, 2.92, 0.05), (-0.5, 0.7, 0.26, 0, 2.86, 0.07), (-0.1, 0.6, 0.2, 0, 2.86, 0.05)])
    for s in (-1, 1):
        # (let into the helmet's sides, which the house style pushes out to 0.475)
        h.cyl((s * 0.44, 3.15, -0.2), (s * 0.56, 3.15, -0.2), 0.2, segs=10, kind=SOFT)
    h.cyl((0.3, 3.55, 0.1), (0.3, 3.95, 0.14), 0.02, 0.008, segs=6, kind=FRAME, bevel=0)
    if kind == "gladiator":
        # a tall crest fore and aft over the helmet, cheek guards, and a grille for a face
        h.loft([[(x, y, z) for z, y in [(-0.7, 3.5), (-0.5, 4.25), (0.1, 4.45), (0.6, 4.1), (0.5, 3.5)]] for x in (-0.07, 0.07)], bevel=0.012)
        for s in (-1, 1):
            h.box((s * 0.5, 2.98, -0.45), (0.1, 0.5, 0.55), PAINT, 0.02)
        h.slats((0, 3.0, -0.84), (0, 0, -1), (1, 0, 0), (0.6, 0.3), 5, 0.05)
    m.add(h, T + "/Head", "Skull", CAMO)
    v = Part("Head_Visor")
    v.stations("z", [(-0.8, 0.6, 0.16, 0, 3.24, 0.04), (-0.7, 0.86, 0.24, 0, 3.24, 0.06), (-0.5, 0.92, 0.24, 0, 3.24, 0.06)], bevel=0.008)
    m.add(v, T + "/Head", "Visor", GLASS)

    left = Part("LeftTorso_Armor")
    left.stations("y", [(0.2, 0.7, 1.3, -1.0, 0, 0.15), (1.2, 0.95, 1.9, -1.19, 0, 0.22), (2.3, 1.0, 2.1, -1.2, 0, 0.25), (2.66, 0.8, 1.7, -1.15, 0, 0.2)], seams=PANELS, cuts=[((0, 1.4, 0), (0, 1, 0))])
    left.slats((-1.2, 2.0, -1.06), (0, 0.1, -1), (1, 0, 0), (0.5, 0.6), 4, 0.06)
    # pauldron: a heavy box over the shoulder, with a plate hung down its outside
    left.stations("x", [(-2.3, 1.3, 0.5, 0, 2.6, 0.08), (-1.5, 1.7, 0.74, 0, 2.66, 0.18), (-1.0, 1.5, 0.56, 0, 2.7, 0.14)], seams=SMALL_PANELS)
    left.box((-2.52, 2.3, 0.0), (0.14, 0.9, 1.4), PAINT, 0.03, SMALL_PANELS)
    left.bolts([(-2.6, 2.6, -0.5), (-2.6, 2.6, 0.5), (-2.6, 2.0, -0.5), (-2.6, 2.0, 0.5)], (-1, 0, 0), r=0.045)
    if kind == "gladiator":
        # layered round shoulder guards, and tassets hanging over the hips
        # (each one sitting on the one under it: a guard is 0.26 tall)
        for i, (r, y) in enumerate(((1.15, 2.75), (0.95, 2.99), (0.7, 3.23))):
            left.cyl((-1.55, y, 0.0), (-1.55, y + 0.26, 0.0), r, r * 0.8, segs=12, kind=PAINT, bevel=0.02)
        left.box((-0.95, 0.0, -0.95), (0.8, 1.1, 0.12), PAINT, 0.03, SMALL_PANELS, Matrix.Rotation(math.radians(-14), 3, "X"))
        left.box((-1.5, 0.05, 0.0), (0.12, 1.0, 1.3), PAINT, 0.03, SMALL_PANELS)
    m.both(left, T + "/LeftTorso", "Hull", CAMO)
    m.removes += [T + "/LeftTorso/Shoulder", T + "/RightTorso/Shoulder"]

    m.removes += [T + "/Stripe"]
    a = Part("Antenna")
    with a.detail((-0.86, 2.76, 0.57), (0, 1, 0)):
        a.box((-0.86, 2.83, 0.57), (0.2, 0.14, 0.2), FRAME)
        a.cyl((-0.86, 2.88, 0.57), (-0.86, 3.93, 0.57), 0.03, 0.01, segs=6, kind=FRAME, bevel=0)
    m.add(a, T, "Antenna", JOINT)

    # Upper arm: hangs from the shoulder (the node is at its middle; the elbow is 0.76 below). It stops
    # short of the elbow, so the forearm, swinging up to aim, doesn't cut through its lower end.
    u = Part("UpperArmL")
    u.stations("y", [(0.8, 0.96, 1.0, 0, 0, 0.16), (0.1, 0.92, 0.96, 0, 0, 0.14), (-0.3, 0.8, 0.82, 0, 0, 0.1)], seams=SMALL_PANELS)
    u.cyl((0.2, 0.62, 0), (0.75, 0.62, 0), 0.36, segs=14, kind=SOFT)
    m.both(u, T, "UpperArmL", CAMO, own_place=True)

    def pod(name):
        """A forearm that is a weapon pod: a long box with a heavy barrel and a lighter one under it."""
        fa = Part(name)
        # elbow (its ends short of the armour's sides, which are 0.496 out)
        fa.cyl((-0.46, 0, 0), (0.46, 0, 0), 0.47, segs=16, kind=FRAME)
        for s in (-1, 1):
            fa.cyl((s * 0.46, 0, 0), (s * 0.52, 0, 0), 0.28, segs=12, kind=SOFT)
        # (no grille along its side: that is under the forearm's armour, which would sit flat on it)
        fa.stations("z", [(-0.4, 0.9, 1.0, 0, -0.05, 0.1), (-2.5, 0.9, 1.0, 0, -0.05, 0.1), (-2.75, 0.8, 0.9, 0, -0.05, 0.1)], kind=FRAME, seams=SMALL_PANELS)
        return fa

    def forearm(name, gun):
        fa = Part(name)
        fa.cyl((-0.46, 0, 0), (0.46, 0, 0), 0.45, segs=16, kind=FRAME)  # elbow
        for s in (-1, 1):
            fa.cyl((s * 0.46, 0, 0), (s * 0.52, 0, 0), 0.28, segs=12, kind=SOFT)
        # fist
        fa.stations("z", [(-2.28, 0.8, 0.8, 0, 0, 0.14), (-2.94, 0.72, 0.7, 0, 0, 0.14)], kind=FRAME)
        if gun:
            # no fingers: a plate across the end that the guns stand on (see gun_hand)
            fa.box((0, 0, -2.98), (0.7, 0.68, 0.08), FRAME, 0.015)
        else:
            for i in range(4):
                fa.box(((i - 1.5) * 0.19, 0.12, -2.98), (0.16, 0.5, 0.12), FRAME, 0.015)
        return fa

    def gun_hand(side):
        """The guns on a forearm's end plate, side by side and clear of each other: the main one on the
        inboard part of the plate, the light one outboard and low."""
        out = -1 if side == "L" else 1
        A = f"{T}/Arm{side}/HardpointAutocannon"
        M = f"{T}/Arm{side}/HardpointMachineGun"
        m.moves[A] = (-out * 0.11, 0.0, -3.0)
        m.moves[M] = (out * 0.24, -0.2, -3.0)
        m.props[A] = {"Area": (0.44, 0.64)}
        m.props[M] = {"Area": (0.18, 0.24)}

    arm = Part("ArmL_Armor")
    arm.stations("z", [(-0.35, 0.74, 0.74, 0, 0, 0.1), (-0.8, 0.98, 1.0, 0, 0, 0.16), (-2.0, 0.94, 0.94, 0, 0, 0.14), (-2.32, 0.76, 0.76, 0, 0, 0.1)], seams=SMALL_PANELS)
    m.both(arm, T + "/ArmL/Hit", "Forearm", CAMO)
    if heavy:
        # the left arm keeps its fist, to hit with, the guns on its end
        m.add(forearm("ArmL_Frame", True), T + "/ArmL/Hit", "Hand", JOINT)
        gun_hand("L")
        m.add(pod("ArmR_Frame_").mirrored("ArmR_Frame"), T + "/ArmR/Hit", "Hand", JOINT)
        # (high enough that the launchers' tubes clear the pauldrons under them)
        shoulder_racks(m, 0.85, lift=1.35, back=0.9, pedestal=0.3)
    elif kind == "panther":
        m.add(forearm("ArmL_Frame", True), T + "/ArmL/Hit", "Hand", JOINT)
        m.add(forearm("ArmR_Frame_", True).mirrored("ArmR_Frame"), T + "/ArmR/Hit", "Hand", JOINT)
        shoulder_racks(m, 0.95, lift=0.75, back=0.7)
    elif kind == "gladiator":
        left = forearm("ArmL_Frame", False)
        # a slab of shield bolted along the outside of the forearm
        # (let 1.5 cm into the side of the forearm's armour)
        left.box((-0.55, 0.1, -1.5), (0.14, 1.5, 2.3), FRAME, 0.03, SMALL_PANELS)
        left.bolts([(-0.7, 0.6, -0.7), (-0.7, -0.4, -0.7), (-0.7, 0.6, -2.3), (-0.7, -0.4, -2.3)], (-1, 0, 0), r=0.05)
        m.moves[T + "/HardpointShoulder"] = (-1.255, 2.935, -1.08)  # (the launcher in front of the shoulder guards, not through them)
        m.add(left, T + "/ArmL/Hit", "Hand", JOINT)
        m.add(forearm("ArmR_Frame_", False).mirrored("ArmR_Frame"), T + "/ArmR/Hit", "Hand", JOINT)
    else:
        m.add(forearm("ArmL_Frame", True), T + "/ArmL/Hit", "Hand", JOINT)
        gun_hand("L")
        m.add(forearm("ArmR_Frame_", False).mirrored("ArmR_Frame"), T + "/ArmR/Hit", "Hand", JOINT)
    for side in "LR":
        m.removes += [f"{T}/Arm{side}/Hit/Elbow", f"{T}/Arm{side}/Hit/Barrel"]
    m.shapes[T + "/ArmL/Hit/Shape"] = m.shapes[T + "/ArmR/Hit/Shape"] = (1.0, 1.0, 3.23)
    m.moves[T + "/ArmL/Insignia"] = (-0.565, 0.0, -1.4)
    m.moves[T + "/ArmR/Insignia"] = (0.565, 0.0, -1.4)

    hips(m, 0.95, jets=[(-0.86, -0.38, 1.04), (0.86, -0.38, 1.04)])
    cockpit(m)
    legs(m, (2.744, 0.9025, 1.0925), (2.744, 0.7125, 0.8075), (0.441, 0.5225, 0.57), (1.14, 0.3325, 2.09), (0.665, 0.57, 0.475), spur=False, foot_shift=-0.43)
    return m


# ── The Agni's cab ───────────────────────────────────────────────────────────────────────────────
# Let into the chest under the hood: an armoured windscreen of three panes — a broad one ahead and a
# narrower one either side, folded back to the pillars — leaning back under the brow of the hood, in a
# heavy frame.
AGNI_GLASS_LOW, AGNI_GLASS_HIGH = 1.6, 2.38        # heights of the bottom and the top of the panes
# Bottom and top edge of the panes, left to right: outer corner, fold, fold, outer corner (x, z).
AGNI_PANE_LOW = [(-0.6, -1.64), (-0.34, -1.88), (0.34, -1.88), (0.6, -1.64)]
AGNI_PANE_HIGH = [(-0.56, -1.44), (-0.31, -1.66), (0.31, -1.66), (0.56, -1.44)]


def bar(p, a, b, w, h, normal, kind=FRAME, bevel=0.01):
    """A straight bar from a to b, w across and h deep (along normal)."""
    a, b = Vector(a), Vector(b)
    d = (b - a).normalized()
    n = Vector(normal)
    n = (n - d * n.dot(d)).normalized()
    s = d.cross(n)
    sec = lambda c: [tuple(c + s * u * w / 2 + n * v * h / 2) for u, v in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    return p.loft([sec(a), sec(b)], kind, bevel)


def pane(p, corners, thick, kind=PAINT, shift=(0, 0, 0)):
    """A flat slab of glass through four corners (going round), thick across."""
    c = [Vector(q) + Vector(shift) for q in corners]
    n = (c[1] - c[0]).cross(c[3] - c[0]).normalized()
    return p.loft([[tuple(q - n * thick / 2) for q in c], [tuple(q + n * thick / 2) for q in c]], kind, 0.004)


def agni_panes(shift=(0, 0, 0)):
    """The three panes, as corners in the torso's axes: left, middle, right."""
    lo = [(x, AGNI_GLASS_LOW, z) for x, z in AGNI_PANE_LOW]
    hi = [(x, AGNI_GLASS_HIGH, z) for x, z in AGNI_PANE_HIGH]
    return [[lo[i], lo[i + 1], hi[i + 1], hi[i]] for i in range(3)]


def agni_cab_frame(h, paint, frame, grow=0.0, shift=(0, 0, 0)):
    """The cab's armour round the panes: their frame, the sill, the brow, the cheeks (grow and shift:
    the same a little bigger, or moved)."""
    g = grow
    sh = Vector(shift)
    lo = [Vector((x, AGNI_GLASS_LOW, z)) + sh for x, z in AGNI_PANE_LOW]
    hi = [Vector((x, AGNI_GLASS_HIGH, z)) + sh for x, z in AGNI_PANE_HIGH]
    at = lambda q: tuple(Vector(q) + sh)
    # a heavy rail under the panes and another along their top, posts where they fold
    for i in range(3):
        out = (lo[i + 1] - lo[i]).cross(Vector((0, 1, 0))).normalized() * -1
        if out.z > 0:
            out = -out
        # (the middle rail a shade thicker than its neighbours, so where they overlap at the corners
        # their faces are not in one plane)
        fat = 0.012 if i == 1 else 0.0
        for row, w, d in ((lo, 0.1, 0.09), (hi, 0.08, 0.08)):
            along = (row[i + 1] - row[i]).normalized()
            bar(h, row[i] - along * (0.04 + g), row[i + 1] + along * (0.04 + g), w + 2 * g + fat, d + 2 * g + fat, out, frame, 0.012)
    for i in range(4):
        side = Vector((1, 0, 0)) if i < 2 else Vector((-1, 0, 0))
        normal = (Vector((0, 0, -1)) + side * (0.0 if i in (1, 2) else 0.8)).normalized()
        bar(h, lo[i] - Vector((0, g, 0)), hi[i] + Vector((0, g, 0)), (0.075 if i in (1, 2) else 0.1) + 2 * g, 0.08 + 2 * g, normal, frame, 0.01)
    # the sill the frame stands on: a sloping ledge of armour
    h.stations("x", [(-0.66 - g, 0.16 + 2 * g, 0.34 + 2 * g, -1.74 + sh.z, 1.5 + sh.y, 0.04), (-0.36, 0.16 + 2 * g, 0.38 + 2 * g, -1.88 + sh.z, 1.5 + sh.y, 0.04),
                     (0.36, 0.16 + 2 * g, 0.38 + 2 * g, -1.88 + sh.z, 1.5 + sh.y, 0.04), (0.66 + g, 0.16 + 2 * g, 0.34 + 2 * g, -1.74 + sh.z, 1.5 + sh.y, 0.04)], kind=paint)
    # the brow under the front of the hood, shading the panes, and its rain lip
    h.box(at((0, 2.43, -1.72)), (1.34 + 2 * g, 0.08 + 2 * g, 0.34 + 2 * g), paint, 0.02)
    h.box(at((0, 2.395, -1.88)), (1.2 + 2 * g, 0.03 + 2 * g, 0.04 + 2 * g), frame, 0.006)
    for sx in (-1, 1):
        # cheeks closing the cab between the pillars and the chest
        h.box(at((sx * 0.64, 1.99, -1.3)), (0.06 + 2 * g, 0.9 + 2 * g, 0.62 + 2 * g), paint, 0.015)
        if g > 0:
            # (the pillars of the hood are the hull's own: only their lining is the cab's)
            h.box(at((sx * 0.66, 1.98, -1.66)), (0.14 + 2 * g, 1.0 + 2 * g, 0.3 + 2 * g), paint, 0.03)


def agni_cab(m):
    T = "Hips/Torso"
    h = Part("Head_Armor")
    agni_cab_frame(h, PAINT, FRAME)
    h.bolts([(x, 1.5, -2.07) for x in (-0.28, -0.1, 0.1, 0.28)], (0, 0, -1), r=0.025, h=0.02)
    for sx in (-1, 1):
        # a washer jet over each side pane
        h.cyl((sx * 0.47, 2.36, -1.6), (sx * 0.47, 2.36, -1.66), 0.018, segs=8, kind=EDGE, bevel=0)
    m.add(h, T + "/Head", "Skull", CAMO)
    v = Part("Head_Visor")
    for corners in agni_panes():
        pane(v, corners, 0.035)
    m.add(v, T + "/Head", "Visor", GLASS)


def agni():
    """A light fire-raiser: a broad box of a hull with the cab let into the middle of its chest under
    an armoured hood, a framed radiator on the roof, a drum of a shoulder either side, each arm ending
    in a block of four barrels with a two-barrelled pod outboard of it, and feet of four toes set as a
    cross. It stands on the man-shaped frame (the scene is laid out as the Gladiator's)."""
    m = Model("agni", "scenes/mech/agni.tscn")
    T = "Hips/Torso"
    p = Part("CenterTorso_Armor")
    # the hull, waist to shoulders
    p.stations("y", [(0.05, 1.2, 1.35, 0, 0, 0.18), (0.95, 1.5, 2.0, 0, 0, 0.25), (2.35, 1.5, 2.2, 0, -0.05, 0.28), (2.78, 1.4, 1.8, 0, 0, 0.25)], seams=PANELS, cuts=[((0, 1.5, 0), (0, 1, 0))])
    # the prow under the cab: a wedge of plate jutting forward, with a bolted glacis
    p.stations("z", [(-2.0, 1.0, 0.5, 0, 0.98, 0.12), (-1.55, 1.46, 0.9, 0, 1.02, 0.2), (-0.9, 1.5, 1.1, 0, 1.08, 0.2)], seams=SMALL_PANELS)
    # the hood over the cab, and the posts that carry it
    p.stations("z", [(-1.86, 1.36, 0.22, 0, 2.52, 0.06), (-1.3, 1.5, 0.36, 0, 2.58, 0.1), (-0.6, 1.5, 0.36, 0, 2.62, 0.1)], seams=SMALL_PANELS)
    for s in (-1, 1):
        p.box((s * 0.66, 1.98, -1.66), (0.14, 1.0, 0.3), PAINT, 0.03)
    m.add(p, T + "/CenterTorso", "Armor", CAMO)

    f = Part("CenterTorso_Frame")
    # reactor pack on the back, with its fins
    f.stations("z", [(0.95, 1.6, 1.7, 0, 1.6, 0.2), (1.65, 1.72, 1.8, 0, 1.6, 0.28), (1.92, 1.3, 1.3, 0, 1.6, 0.2)], kind=FRAME, seams=SMALL_PANELS)
    f.slats((0, 1.62, 1.93), (0, 0, 1), (1, 0, 0), (0.95, 0.85), 6, 0.08)
    for i in range(6):
        f.box((0, 0.95 + i * 0.07, 1.96), (1.3, 0.03, 0.12), FRAME, 0)
    # the radiator on the roof: a framed panel of louvres standing behind the hood
    f.box((0, 3.12, 0.25), (2.0, 0.62, 0.9), FRAME, 0.03, SMALL_PANELS)
    f.slats((0, 3.12, -0.21), (0, 0, -1), (1, 0, 0), (1.7, 0.46), 6, 0.05)
    f.slats((0, 3.44, 0.25), (0, 1, 0), (1, 0, 0), (1.5, 0.7), 6, 0.04)
    for s in (-1, 1):
        # its feet, standing on the roof (which is 1.46 across)
        f.box((s * 0.55, 2.8, 0.25), (0.12, 0.12, 0.5), FRAME, 0.02)
        # fuel lines for the flamers, from the pack over each shoulder
        f.cyl((s * 0.7, 2.5, 1.2), (s * 1.5, 2.9, 0.6), 0.07, segs=8, kind=SOFT, bevel=0)
        f.cyl((s * 1.5, 2.9, 0.6), (s * 1.9, 2.75, 0.1), 0.07, segs=8, kind=SOFT, bevel=0)
    # waist
    f.cyl((0, -0.6, 0), (0, 0.15, 0), 0.72, segs=20, kind=FRAME)
    f.cyl((0, -0.28, 0), (0, -0.12, 0), 0.88, segs=20, kind=SOFT)
    m.add(f, T + "/CenterTorso", "Frame", JOINT)

    agni_cab(m)

    left = Part("LeftTorso_Armor")
    left.stations("y", [(0.25, 0.75, 1.4, -1.02, 0, 0.15), (1.15, 1.0, 1.95, -1.2, 0, 0.22), (2.35, 1.05, 2.15, -1.22, 0, 0.25), (2.72, 0.9, 1.75, -1.17, 0, 0.2)], seams=PANELS, cuts=[((0, 1.4, 0), (0, 1, 0))])
    # a square lamp housing on the corner of the chest
    left.box((-1.2, 2.2, -1.1), (0.46, 0.4, 0.2), FRAME, 0.03)
    left.box((-1.2, 2.2, -1.205), (0.32, 0.26, 0.02), RECESS, 0)
    # the shoulder: a drum lying across, capped and bolted, with a sensor box on top
    left.cyl((-1.55, 2.15, 0.0), (-2.78, 2.15, 0.0), 0.98, segs=22, kind=PAINT, bevel=0.03)
    left.cyl((-2.78, 2.15, 0.0), (-2.9, 2.15, 0.0), 0.7, segs=18, kind=FRAME)
    left.bolts([(-2.9, 2.15 + math.cos(i * math.tau / 8) * 0.52, math.sin(i * math.tau / 8) * 0.52) for i in range(8)], (-1, 0, 0), r=0.05)
    left.cyl((-2.2, 2.15, 0.0), (-2.3, 2.15, 0.0), 1.03, segs=22, kind=RECESS, bevel=0)
    left.box((-2.1, 3.2, 0.0), (0.7, 0.34, 0.8), PAINT, 0.03, SMALL_PANELS)
    left.box((-2.1, 3.2, -0.41), (0.4, 0.18, 0.02), RECESS, 0)
    m.both(left, T + "/LeftTorso", "Hull", CAMO)
    m.removes += [T + "/LeftTorso/Shoulder", T + "/RightTorso/Shoulder", T + "/Stripe"]

    a = Part("Antenna")
    # (on the radiator's lid, outboard of its louvres)
    with a.detail((0.88, 3.43, 0.5), (0, 1, 0)):
        a.box((0.88, 3.5, 0.5), (0.16, 0.14, 0.2), FRAME)
        a.cyl((0.88, 3.56, 0.5), (0.88, 4.7, 0.5), 0.03, 0.01, segs=6, kind=FRAME, bevel=0)
    m.add(a, T, "Antenna", JOINT)

    # Upper arm: a short link under the drum (the node is at its middle; the elbow is 0.76 below)
    u = Part("UpperArmL")
    u.stations("y", [(0.3, 0.9, 0.96, 0, 0, 0.16), (-0.2, 0.84, 0.9, 0, 0, 0.14), (-0.76, 0.7, 0.74, 0, 0, 0.1)], seams=SMALL_PANELS)
    m.both(u, T, "UpperArmL", CAMO, own_place=True)

    # Forearm: a block of a gun housing, a pod on its outside
    arm = Part("ArmL_Armor")
    arm.stations("z", [(0.45, 0.9, 1.0, 0, 0, 0.12), (-0.1, 1.16, 1.3, 0, 0, 0.18), (-2.0, 1.16, 1.3, 0, 0, 0.18), (-2.3, 1.02, 1.12, 0, 0, 0.14)], seams=PANELS, cuts=[((0, 0, -1.0), (0, 0, 1))])
    arm.stations("z", [(-0.5, 0.5, 0.74, -0.82, -0.1, 0.1), (-2.0, 0.5, 0.74, -0.82, -0.1, 0.1), (-2.16, 0.42, 0.62, -0.82, -0.1, 0.08)], seams=SMALL_PANELS)
    arm.slats((0.585, 0.0, -1.0), (1, 0, 0), (0, 0, 1), (0.6, 0.9), 5, 0.05)
    m.both(arm, T + "/ArmL/Hit", "Forearm", CAMO)
    hand = Part("ArmL_Frame")
    hand.cyl((-0.5, 0, 0), (0.5, 0, 0), 0.5, segs=16, kind=FRAME)   # elbow
    hand.box((0, 0, -2.32), (0.98, 1.08, 0.1), FRAME, 0.02)        # the plate the guns stand on
    m.both(hand, T + "/ArmL/Hit", "Hand", JOINT)
    for side in "LR":
        m.removes += [f"{T}/Arm{side}/Hit/Elbow", f"{T}/Arm{side}/Hit/Barrel"]
    m.moves[T + "/ArmL/Insignia"] = (-1.08, -0.1, -1.25)
    m.moves[T + "/ArmR/Insignia"] = (1.08, -0.1, -1.25)
    m.shapes[T + "/ArmL/Hit/Shape"] = m.shapes[T + "/ArmR/Hit/Shape"] = (1.3, 1.3, 3.4)

    hips(m, 0.95, jets=[(-0.86, -0.38, 1.04), (0.86, -0.38, 1.04)])
    cockpit(m)
    legs(m, (2.744, 0.86, 1.04), (2.744, 0.68, 0.77), (0.441, 0.5, 0.55), (1.14, 0.3325, 2.09), (0.665, 0.57, 0.475), spur=False, foot_style="cross")
    return m


def executioner():
    """A heavy in the manner of the old box-bodied assault machines: a square torso with the cab jutting
    out of the top of the chest like a crate (the pilot's eye is just in front of its glass, so the crate
    is behind the view), side torsos as broad as the middle one, a gauss rifle for a
    left forearm and a pod of two lasers for a right one, and a single missile box on the left shoulder.
    Stands on the man-shaped legs and arms of the Hades (the arm nodes are where the Hades has them)."""
    m = Model("executioner", "scenes/mech/executioner.tscn")
    T = "Hips/Torso"

    p = Part("CenterTorso_Armor")
    # the body: a box, a little wider and deeper at the chest than at the waist
    p.stations("y", [(0.05, 1.3, 1.5, 0, 0.1, 0.16), (0.9, 1.9, 2.25, 0, 0.0, 0.2), (2.6, 2.0, 2.4, 0, 0.05, 0.22), (2.95, 1.8, 2.1, 0, 0.1, 0.18)], seams=PANELS, cuts=[((0, 1.6, 0), (0, 1, 0))])
    # the roof: a raised hatch and a row of lifting eyes
    p.box((0, 2.99, 0.45), (1.0, 0.1, 0.9), PAINT, 0.02, SMALL_PANELS)
    m.add(p, T + "/CenterTorso", "Armor", CAMO)

    f = Part("CenterTorso_Frame")
    # reactor housing on the back with its grilles, exhausts either side
    f.box((0, 1.6, 1.3), (1.5, 1.7, 0.4), FRAME, 0.03, SMALL_PANELS)
    f.slats((0, 1.6, 1.51), (0, 0, 1), (1, 0, 0), (1.2, 1.3), 7, 0.07)
    for sx in (-1, 1):
        f.cyl((sx * 0.55, 2.55, 1.15), (sx * 0.55, 3.25, 1.3), 0.15, segs=12, kind=FRAME)
        f.cyl((sx * 0.55, 3.23, 1.3), (sx * 0.55, 3.27, 1.31), 0.1, segs=12, kind=RECESS, bevel=0)
    lifting_eyes(f, [(sx * 0.6, 3.0, -0.3) for sx in (-1, 1)])
    # waist
    f.cyl((0, -0.6, 0), (0, 0.15, 0), 0.75, segs=20, kind=FRAME)
    f.cyl((0, -0.28, 0), (0, -0.12, 0), 0.92, segs=20, kind=SOFT)
    # a medium laser low in the chest, right of the middle
    f.box((0.62, 0.9, -1.12), (0.42, 0.42, 0.2), FRAME, 0.02)       # its mount
    m.moves[T + "/HardpointChestLaser"] = (0.005, 1.32, -1.15)       # (its back let into the chest)
    m.add(f, T + "/CenterTorso", "Frame", JOINT)

    # The cab: a crate thrust forward out of the top of the chest, the glass a slot across its face.
    h = Part("Head_Armor")
    # (its face stops 4 cm behind the cockpit's back bulkhead, which would otherwise lie flush with it)
    h.stations("z", [(-0.3, 1.5, 1.05, 0, 2.48, 0.12), (-1.4, 1.5, 1.05, 0, 2.48, 0.12), (-2.0, 1.36, 0.9, 0, 2.5, 0.12), (-2.11, 1.16, 0.72, 0, 2.52, 0.1)], seams=SMALL_PANELS)
    # a brow over the glass, cheeks down its sides, a chin under it
    h.box((0, 2.98, -1.75), (1.56, 0.1, 0.9), PAINT, 0.02)
    for sx in (-1, 1):
        h.box((sx * 0.78, 2.45, -1.6), (0.1, 0.9, 1.0), PAINT, 0.02, SMALL_PANELS)
        h.bolts([(sx * 0.84, y, z) for y in (2.15, 2.75) for z in (-1.25, -1.95)], (sx, 0, 0), r=0.035)
    h.stations("z", [(-1.6, 1.2, 0.3, 0, 2.03, 0.08), (-2.1, 1.0, 0.24, 0, 2.06, 0.06)])
    h.slats((0, 2.03, -2.12), (0, 0, -1), (1, 0, 0), (0.7, 0.18), 4, 0.04)
    h.cyl((0.55, 2.97, -1.0), (0.55, 3.6, -0.98), 0.025, 0.01, segs=6, kind=FRAME, bevel=0)  # (from inside the roof)
    m.add(h, T + "/Head", "Skull", CAMO)
    v = Part("Head_Visor")
    # (let 2 cm into the face of the cab, which is at z = -2.11)
    v.stations("z", [(-2.09, 1.12, 0.26, 0, 2.66, 0.04), (-2.2, 1.04, 0.22, 0, 2.66, 0.04)], bevel=0.008)
    m.add(v, T + "/Head", "Visor", GLASS)

    # Side torsos: blocks as deep as the middle one, ending at the shoulders.
    left = Part("LeftTorso_Armor")
    left.stations("x", [(-1.0, 2.3, 2.3, 0.0, 1.55, 0.18), (-1.9, 2.2, 2.2, 0.0, 1.6, 0.2), (-2.05, 1.8, 1.8, 0.0, 1.65, 0.16)], seams=PANELS, cuts=[((-1.5, 0, 0), (1, 0, 0))])
    left.slats((-1.5, 1.2, -1.16), (0, 0, -1), (1, 0, 0), (0.6, 0.7), 5, 0.06)
    m.both(left, T + "/LeftTorso", "Hull", CAMO)

    # One missile box, on the left shoulder.
    r = Part("LeftTorso_Rack")
    r.stations("z", [(1.0, 1.15, 1.0, -1.55, 3.5, 0.08), (-1.3, 1.15, 1.0, -1.55, 3.45, 0.08)], seams=PANELS)
    m.add(r, T + "/LeftTorso", "Rack", CAMO)
    rf = Part("LeftTorso_RackFace")
    rf.box((-1.55, 3.45, -1.33), (1.0, 0.86, 0.08), FRAME, 0.02)     # face (the tubes: the launcher's model)
    rf.box((-1.55, 2.85, 0.0), (0.7, 0.4, 1.2), FRAME, 0.03)          # pedestal
    rf.slats((-1.55, 3.5, 1.01), (0, 0, 1), (1, 0, 0), (0.9, 0.7), 5, 0.06)
    m.add(rf, T + "/LeftTorso", "RackFace", JOINT)
    m.removes += [T + "/RightTorso/Rack", T + "/RightTorso/RackFace"]

    a = Part("Antenna")
    with a.detail((1.5, 2.72, 0.8), (0, 1, 0)):
        a.box((1.5, 2.79, 0.8), (0.2, 0.14, 0.2), FRAME)
        a.cyl((1.5, 2.84, 0.8), (1.5, 4.24, 0.85), 0.03, 0.01, segs=6, kind=FRAME, bevel=0)
    m.add(a, T, "Antenna", JOINT)

    # Shoulder pivots and upper arms (the nodes stand where the Hades has them).
    j = Part("ShoulderJointL")
    j.cyl((0.5, 0, 0), (-0.45, 0, 0), 0.5, segs=18, kind=FRAME)
    j.cyl((-0.45, 0, 0), (-0.55, 0, 0), 0.32, segs=14, kind=SOFT)
    m.both(j, T, "ShoulderJointL", JOINT, own_place=True)
    u = Part("UpperArmL")
    u.stations("y", [(0.85, 0.9, 1.0, 0, 0, 0.12), (0.2, 0.94, 1.0, 0, 0, 0.12), (-0.75, 0.84, 0.88, 0, 0, 0.1)], seams=SMALL_PANELS)
    m.both(u, T, "UpperArmL", CAMO, own_place=True)

    # Forearms: square housings; the left one carries the gauss rifle, the right one the lasers.
    arm = Part("ArmL_Armor")
    arm.stations("z", [(0.35, 0.9, 0.95, 0, 0, 0.1), (-0.2, 1.1, 1.2, 0, 0, 0.14), (-2.75, 1.1, 1.2, 0, 0, 0.14), (-3.0, 0.94, 1.02, 0, 0, 0.1)], seams=PANELS, cuts=[((0, 0, -1.5), (0, 0, 1))])
    arm.slats((0, 0.61, -1.5), (0, 1, 0), (1, 0, 0), (0.6, 1.4), 8, 0.05)
    m.both(arm, T + "/ArmL/Hit", "Forearm", CAMO)

    g = Part("ArmL_Frame")
    g.cyl((-0.5, 0, 0), (0.5, 0, 0), 0.5, segs=16, kind=FRAME, hub=False)      # elbow (its ends inside the housing's walls)
    g.box((0, 0.05, -3.06), (0.86, 0.9, 0.14), FRAME, 0.02)          # the plate the gun stands on, sunk a little into the housing
    m.add(g, T + "/ArmL/Hit", "Hand", JOINT)

    lz = Part("ArmR_Frame_")
    lz.cyl((-0.5, 0, 0), (0.5, 0, 0), 0.5, segs=16, kind=FRAME, hub=False)     # elbow
    lz.box((0, 0, -3.06), (0.9, 1.0, 0.14), FRAME, 0.02)
    m.add(lz.mirrored("ArmR_Frame"), T + "/ArmR/Hit", "Hand", JOINT)

    for side in "LR":
        m.removes += [f"{T}/Arm{side}/Hit/Elbow", f"{T}/Arm{side}/Hit/Barrel"]
    m.shapes[T + "/ArmL/Hit/Shape"] = m.shapes[T + "/ArmR/Hit/Shape"] = (1.1, 1.2, 3.65)
    m.moves[T + "/ArmL/Insignia"] = (-0.62, 0.0, -1.5)
    m.moves[T + "/ArmR/Insignia"] = (0.62, 0.0, -1.5)

    # heavy legs — thighs and shins a third thicker than the Hades' — on the same flat oblong feet
    hips(m, 0.95, jets=[(-0.86, -0.38, 1.04), (0.86, -0.38, 1.04)])
    cockpit(m)
    legs(m, (2.744, 1.05, 1.3), (2.744, 0.86, 1.02), (0.441, 0.5, 0.55), (1.3, 0.3325, 2.2), (0.7, 0.62, 0.44), spur=False, foot_shift=-0.3, foot_style="box")
    return m



# ── Two assault machines after the old BattleTech ones ───────────────────────────────────────────
# Both stand on the man-shaped frame of the Executioner (the same arm and leg nodes), but every piece of
# their armour is their own.

def assault_arm(m, T, length, w, h, sleeve=None, fins=0):
    """A forearm that is a weapon housing: a long chamfered box from the elbow, its weapons standing on the
    plate at its end. sleeve: (width, height, side x) of a feed run down its outside; fins: cooling fins on
    top. Mirrored for the right arm."""
    arm = Part("ArmL_Armor")
    arm.stations("z", [(0.4, w * 0.82, h * 0.78, 0, 0.05, 0.1), (-0.2, w, h, 0, 0, 0.14), (-length + 0.35, w, h, 0, 0, 0.14), (-length, w * 0.86, h * 0.86, 0, 0, 0.1)],
                 seams=PANELS, cuts=[((0, 0, -length * 0.48), (0, 0, 1))])
    if sleeve:
        sw, sh, sx = sleeve
        arm.stations("z", [(-0.5, sw, sh, sx, -h * 0.12, 0.06), (-length + 0.7, sw, sh, sx, -h * 0.12, 0.06)], seams=SMALL_PANELS)
    if fins:
        arm.slats((0, h * 0.5 + 0.01, -length * 0.5), (0, 1, 0), (1, 0, 0), (w * 0.6, length * 0.55), fins, 0.06)
    m.both(arm, T + "/ArmL/Hit", "Forearm", CAMO)
    g = Part("ArmL_Frame")
    g.cyl((-0.5, 0, 0), (0.5, 0, 0), 0.5, segs=16, kind=FRAME, hub=False)                     # elbow (its ends inside the housing)
    g.box((0, 0, -length - 0.06), (w * 0.8, h * 0.82, 0.14), FRAME, 0.02)                      # the plate the weapons stand on
    m.both(g, T + "/ArmL/Hit", "Hand", JOINT)
    for side in "LR":
        m.removes += [f"{T}/Arm{side}/Hit/Elbow", f"{T}/Arm{side}/Hit/Barrel"]
        m.moves[f"{T}/Arm{side}/HardpointArm"] = (0.0, 0.0, -length - 0.125)                  # (5 mm into the plate)
        m.props[f"{T}/Arm{side}/HardpointArm"] = {"Area": (w * 0.78, h * 0.8)}
        m.moves[f"{T}/Arm{side}/Insignia"] = ((-1 if side == "L" else 1) * (w / 2 + 0.02), 0.0, -length * 0.5)
    m.shapes[T + "/ArmL/Hit/Shape"] = m.shapes[T + "/ArmR/Hit/Shape"] = (w, h, length + 0.6)


def shoulder_pod(m, T, centre, size, pedestal, slope=0.0, cheeks=False):
    """A missile box on top of a side torso (both sides): its tubes are the launcher's model, on the
    hardpoint at its face. size: (width, height, length) at the face; pedestal: (bottom y, height) of the
    block it stands on; slope: how much lower its back is than its face (a wedge, its top raked back)."""
    x, y, z = centre
    w, h, ln = size
    r = Part("LeftTorso_Rack")
    hb = h - slope                                                                              # height at the back
    r.stations("z", [(z + ln / 2, w * 0.92, hb * 0.9, x, y - slope / 2 - 0.03, 0.1), (z + ln / 2 - 0.25, w, hb, x, y - slope / 2, 0.1),
                     (z - ln / 2 + 0.2, w, h, x, y, 0.1), (z - ln / 2, w * 0.94, h * 0.94, x, y, 0.08)], seams=PANELS)
    r.slats((x - w / 2 - 0.01, y, z + 0.2), (-1, 0, 0), (0, 0, 1), (h * 0.5, ln * 0.45), 5, 0.05)
    if cheeks:
        # armoured cheeks and a brow standing out round the tube face, as on the old box launchers
        for sx in (-1, 1):
            r.box((x + sx * (w / 2 - 0.02), y, z - ln / 2 - 0.12), (0.12, h * 1.04, 0.42), PAINT, 0.02)
        r.box((x, y + h / 2 - 0.02, z - ln / 2 - 0.12), (w * 1.02, 0.12, 0.42), PAINT, 0.02)
    m.both(r, T + "/LeftTorso", "Rack", CAMO)
    rf = Part("LeftTorso_RackFace")
    rf.box((x, y, z - ln / 2 - 0.02), (w * 0.84, h * 0.82, 0.08), FRAME, 0.02)               # face (the tubes: the launcher's model)
    rf.box((x, pedestal[0] + pedestal[1] / 2, z + 0.1), (w * 0.55, pedestal[1], ln * 0.5), FRAME, 0.03)   # pedestal
    m.both(rf, T + "/LeftTorso", "RackFace", JOINT)
    m.moves[T + "/HardpointRackL"] = (x, y, z - ln / 2 - 0.055)
    m.moves[T + "/HardpointRackR"] = (-x, y, z - ln / 2 - 0.055)
    m.props[T + "/HardpointRackL"] = m.props[T + "/HardpointRackR"] = {"Area": (w * 0.8, h * 0.78)}


def mech_proportions(m, hip_height, hip_x, home_x, thigh, shin, meta):
    """A machine's own build: how high its hips ride, how far apart the legs stand, how long their bones
    are (the leg nodes are told the same lengths the leg meshes are drawn to)."""
    m.moves["Hips"] = (0.0, hip_height, 0.0)
    m.moves["Sounds"] = (0.0, hip_height, 0.0)
    m.moves["Hips/HipL"], m.moves["Hips/HipR"] = (-hip_x, -0.19, 0.0), (hip_x, -0.19, 0.0)
    m.moves["HomeL"], m.moves["HomeR"] = (-home_x, 0.0, 0.05), (home_x, 0.0, 0.05)
    m.props["."] = {"HipHeight": hip_height}
    for leg in ("LegL", "LegR"):
        m.props.setdefault(leg, {})
        m.props[leg] = dict(m.props[leg], ThighLength=thigh, ShinLength=shin, MetaLength=meta)


def side_loft(part, xs, prof, kind=PAINT, bevel=0.05, seams=PANELS):
    """A side torso drawn as a profile (z, y points, round one way) swept out along X through the given
    stations (x, how much the profile shrinks towards its own middle there[, how far its front is drawn
    back there — a front that recedes towards the outside]). The front is the part of the profile ahead
    of its middle."""
    cz = sum(z for z, y in prof) / len(prof)
    cy = sum(y for z, y in prof) / len(prof)
    secs = []
    for st in xs:
        x, k = st[:2]
        back = st[2] if len(st) > 2 else 0.0
        secs.append([(x, cy + (y - cy) * k, cz + (z - cz) * k + (back if z < cz else 0.0)) for z, y in prof])
    part.loft(secs, kind, bevel, seams)


def arms_at(m, T, x):
    """Where the shoulders, upper arms and forearms stand on a broad machine (the other side mirrored)."""
    for side, sx in (("L", -1), ("R", 1)):
        m.moves[f"{T}/ShoulderJoint{side}"] = (sx * (x - 0.28), 2.3725, 0.0)
        m.moves[f"{T}/UpperArm{side}"] = (sx * (x - 0.04), 1.5, 0.0)
        m.moves[f"{T}/Arm{side}"] = (sx * x, 0.55, 0.0)


def mauler_thigh(t):
    # a heavy trapezoid, broadest over the hip, its outer face a raked slab, a pointed cap over the knee
    t.stations("y", [(-1.28, 1.02, 1.2, 0, 0.0, 0.22), (-0.3, 1.06, 1.12, 0, 0.0, 0.22), (0.75, 0.84, 1.0, 0, 0.06, 0.18), (1.18, 0.66, 0.8, 0, 0.06, 0.13)],
               seams=PANELS, cuts=[((0, -0.3, 0), (0, 1, 0))])
    t.box((-0.55, -0.55, 0.06), (0.12, 1.3, 1.0), PAINT, 0.03, SMALL_PANELS, Matrix.Rotation(math.radians(-6), 3, "Z"))
    secs = []
    for y, w, d, z in ((0.7, 0.62, 0.3, 0.5), (1.25, 0.8, 0.5, 0.6), (1.8, 0.56, 0.3, 0.48)):
        secs.append([(-w / 2, y, z - d / 2), (w / 2, y, z - d / 2), (w / 2, y, z + d * 0.25), (0, y, z + d / 2), (-w / 2, y, z + d * 0.25)])
    t.loft(secs, PAINT, 0.03, SMALL_PANELS)
    t.bolts([(-0.28, 1.0, 0.66), (0.28, 1.0, 0.66)], (0, 0, 1), r=0.04)


def mauler_shin(s):
    # a greave flaring towards the ankle, a ridge down its front, vents in its sides
    s.stations("y", [(-1.38, 0.7, 0.84, 0, 0.0, 0.14), (0.0, 0.76, 0.9, 0, 0.03, 0.15), (1.05, 0.9, 1.0, 0, 0.05, 0.14), (1.42, 0.88, 0.94, 0, 0.04, 0.1)],
               seams=PANELS, cuts=[((0, -0.2, 0), (0, 1, 0))])
    s.stations("y", [(-1.1, 0.22, 0.2, 0, 0.47, 0.05), (1.2, 0.26, 0.24, 0, 0.56, 0.06)], seams=SMALL_PANELS)
    for sx in (-1, 1):
        s.slats((sx * 0.39, 0.1, 0.0), (sx, 0, 0), (0, 0, 1), (0.5, 0.7), 4, 0.04)


def mauler():
    """An assault machine after the BattleTech Mauler: a body like a beak — five-sided, its prow a ridge
    leaning back to the top — with a low hood of a cab on the front of the roof and a slit of glass across
    it; great missile boxes high on both shoulders; heavy forearms, each with a pair of autocannons; short
    thick legs set wide, pointed knee caps, three-toed feet. Squatter and broader than anything else."""
    m = Model("mauler", "scenes/mech/mauler.tscn")
    T = "Hips/Torso"

    p = Part("CenterTorso_Armor")
    secs = []
    for y, w, zb, zs, zt in ((0.05, 0.75, 0.85, -0.6, -1.2), (0.9, 1.05, 1.15, -0.8, -1.85), (2.0, 1.1, 1.1, -0.7, -1.5), (2.85, 0.95, 0.95, -0.45, -0.9)):
        secs.append([(-w, y, zb), (w, y, zb), (w, y, zs), (0, y, zt), (-w, y, zs)])
    p.loft(secs, PAINT, 0.05, PANELS)
    m.add(p, T + "/CenterTorso", "Armor", CAMO)

    f = Part("CenterTorso_Frame")
    f.box((0, 1.5, 1.32), (1.6, 1.8, 0.45), FRAME, 0.03, SMALL_PANELS)                         # reactor on the back
    f.slats((0, 1.5, 1.555), (0, 0, 1), (1, 0, 0), (1.3, 1.4), 8, 0.07)
    for sx in (-1, 1):
        f.cyl((sx * 0.62, 2.35, 1.0), (sx * 0.62, 3.15, 1.24), 0.16, segs=12, kind=FRAME)        # exhausts
        f.cyl((sx * 0.62, 3.13, 1.24), (sx * 0.62, 3.17, 1.25), 0.11, segs=12, kind=RECESS, bevel=0)
        f.box((sx * 0.6, 0.62, -1.25), (0.36, 0.34, 0.4), FRAME, 0.02)                          # the chest lasers' mounts, either side of the prow
    f.cyl((0, -0.6, 0), (0, 0.15, 0), 0.75, segs=20, kind=FRAME)                                # waist
    f.cyl((0, -0.28, 0), (0, -0.12, 0), 0.92, segs=20, kind=SOFT)
    m.moves[T + "/HardpointChestLaser"] = (0.0, 0.62, -1.445)
    # (no face to fit them to: one on each mount, a prow apart)
    m.props[T + "/HardpointChestLaser"] = {"Area": (0, 0), "Spacing": (1.2, 0, 0)}
    m.add(f, T + "/CenterTorso", "Frame", JOINT)

    # the cab: a low hood on the front of the roof, a slit of glass across its face, cheek vents
    h = Part("Head_Armor")
    h.stations("z", [(0.0, 1.25, 0.7, 0, 3.15, 0.12), (-0.6, 1.25, 0.7, 0, 3.15, 0.12), (-1.0, 1.05, 0.46, 0, 3.04, 0.1), (-1.2, 0.86, 0.26, 0, 2.97, 0.08)], seams=SMALL_PANELS)
    h.box((0, 3.52, -0.45), (1.0, 0.1, 0.9), PAINT, 0.02)                                       # a hatch on top
    for sx in (-1, 1):
        h.slats((sx * 0.62, 3.1, -0.55), (sx, 0, 0), (0, 0, 1), (0.3, 0.6), 4, 0.03)
    h.cyl((0.42, 3.4, -0.25), (0.42, 4.2, -0.22), 0.025, 0.01, segs=6, kind=FRAME, bevel=0)      # aerial (from inside the hood)
    m.add(h, T + "/Head", "Skull", CAMO)
    v = Part("Head_Visor")
    # a raked canopy across the hood's sloping front
    v.loft([[(-0.5, 3.04, -1.07), (0.5, 3.04, -1.07), (0.5, 3.34, -0.66), (-0.5, 3.34, -0.66)],
            [(-0.48, 3.08, -1.09), (0.48, 3.08, -1.09), (0.48, 3.37, -0.69), (-0.48, 3.37, -0.69)]], PAINT, 0.006)
    m.add(v, T + "/Head", "Visor", GLASS)
    m.moves[T + "/CockpitCam"] = (0.0, 3.2, -1.22)
    for side, sx in (("L", -1), ("R", 1)):
        m.moves[f"{T}/Headlight{side}"] = (sx * 0.55, 2.35, -1.3)
    m.shapes[T + "/Head/Shape"] = (1.3, 0.8, 1.3)

    # side torsos: their fronts bulge low and rake back above, like the prow
    left = Part("LeftTorso_Armor")
    side_loft(left, [(-0.95, 1.0, 0.0), (-2.05, 1.0, 0.35), (-2.22, 0.9, 0.4)], [(1.0, 0.2), (1.0, 2.8), (-0.75, 2.8), (-1.25, 1.3), (-1.0, 0.2)])
    left.slats((-1.55, 0.75, -1.08), (0, 0, -1), (1, 0, 0), (0.7, 0.8), 6, 0.05)
    m.both(left, T + "/LeftTorso", "Hull", CAMO)
    shoulder_pod(m, T, (-1.75, 3.85, -0.15), (1.55, 1.35, 2.6), (2.7, 0.6), slope=0.25, cheeks=True)
    m.shapes[T + "/LeftTorso/Shape"] = m.shapes[T + "/RightTorso/Shape"] = (1.25, 2.6, 2.2)

    arms_at(m, T, 2.85)
    j = Part("ShoulderJointL")
    j.cyl((0.5, 0, 0), (-0.45, 0, 0), 0.58, segs=18, kind=FRAME)
    j.cyl((-0.45, 0, 0), (-0.55, 0, 0), 0.36, segs=14, kind=SOFT)
    m.both(j, T, "ShoulderJointL", JOINT, own_place=True)
    u = Part("UpperArmL")
    u.stations("y", [(0.85, 1.05, 1.15, 0, 0, 0.12), (0.2, 1.1, 1.15, 0, 0, 0.12), (-0.75, 0.94, 1.0, 0, 0, 0.1)], seams=SMALL_PANELS)
    u.box((-0.58, 0.35, 0), (0.12, 0.9, 1.0), PAINT, 0.03, SMALL_PANELS)                        # outer plate
    m.both(u, T, "UpperArmL", CAMO, own_place=True)
    assault_arm(m, T, 3.0, 1.12, 1.28, sleeve=(0.32, 0.55, -0.65))

    hips(m, 0.95, spread=0.3)
    cockpit(m)
    legs(m, (2.4, 1.15, 1.38), (2.4, 0.95, 1.12), (0.441, 0.5, 0.55), (1.45, 0.3325, 2.3), (0.72, 0.64, 0.44), spur=False, foot_style="talon",
         thigh_armor=mauler_thigh, shin_armor=mauler_shin)
    mech_proportions(m, 5.1, 1.45, 1.72, 2.4, 2.4, 0.441)
    return m


def direwolf_thigh(t):
    # a broad block with two stepped plates down its front, side plates, a square shield over the knee
    t.stations("y", [(-1.28, 1.08, 1.22, 0, 0.0, 0.16), (0.1, 1.1, 1.24, 0, 0.03, 0.16), (1.15, 0.8, 0.92, 0, 0.05, 0.12)], seams=PANELS, cuts=[((0, -0.2, 0), (0, 1, 0))])
    t.box((0, -0.65, 0.66), (0.86, 0.9, 0.14), PAINT, 0.03, SMALL_PANELS)
    t.box((0, 0.25, 0.64), (0.76, 0.7, 0.12), PAINT, 0.03, SMALL_PANELS)
    t.box((0, 1.3, 0.6), (0.9, 0.95, 0.2), PAINT, 0.04, SMALL_PANELS, Matrix.Rotation(math.radians(10), 3, "X"))
    for sx in (-1, 1):
        t.box((sx * 0.56, -0.7, 0.0), (0.1, 0.9, 0.8), PAINT, 0.03, SMALL_PANELS)


def direwolf_shin(s):
    # a massive boot: thick over the calf, waisted, flaring again over the ankle, a plate down its front
    s.stations("y", [(-1.38, 0.8, 0.92, 0, 0.0, 0.14), (-0.6, 0.86, 0.98, 0, 0.03, 0.15), (0.5, 0.74, 0.86, 0, 0.03, 0.13), (1.1, 0.92, 1.02, 0, 0.04, 0.14),
                     (1.42, 0.94, 1.0, 0, 0.04, 0.1)], seams=PANELS, cuts=[((0, 0.0, 0), (0, 1, 0))])
    s.box((0, -0.6, 0.5), (0.6, 0.8, 0.12), PAINT, 0.03, SMALL_PANELS)


def direwolf():
    """An assault machine after the BattleTech Dire Wolf: a broad body that widens to the top, its side
    torsos rising higher than the cab, so the cab sits down between them like a head between hunched
    shoulders; missile launchers let into the upper fronts of the side torsos; angular pauldrons over the
    shoulders; massive square forearms, each a battery of lasers with cooling fins along its top; thick
    legs in boot-like greaves on square-toed feet."""
    m = Model("direwolf", "scenes/mech/direwolf.tscn")
    T = "Hips/Torso"

    p = Part("CenterTorso_Armor")
    p.stations("y", [(0.05, 1.7, 1.7, 0, 0.1, 0.16), (0.8, 2.5, 2.3, 0, 0.0, 0.22), (2.4, 2.7, 2.3, 0, 0.08, 0.22), (2.95, 2.4, 2.0, 0, 0.2, 0.18)], seams=PANELS, cuts=[((0, 1.5, 0), (0, 1, 0))])
    for sx in (-1, 1):
        # the chest slabs, angled into a shallow V
        p.box((sx * 0.62, 1.85, -1.12), (1.0, 1.4, 0.18), PAINT, 0.03, SMALL_PANELS, Matrix.Rotation(sx * math.radians(14), 3, "Y"))
    m.add(p, T + "/CenterTorso", "Armor", CAMO)

    f = Part("CenterTorso_Frame")
    f.box((0, 1.6, 1.24), (2.0, 1.9, 0.45), FRAME, 0.03, SMALL_PANELS)                         # reactor on the back
    f.slats((0, 1.6, 1.475), (0, 0, 1), (1, 0, 0), (1.6, 1.5), 9, 0.07)
    for sx in (-1, 1):
        f.cyl((sx * 0.8, 2.4, 0.95), (sx * 0.8, 3.4, 1.15), 0.18, segs=12, kind=FRAME)
        f.cyl((sx * 0.8, 3.38, 1.15), (sx * 0.8, 3.42, 1.16), 0.12, segs=12, kind=RECESS, bevel=0)
    f.cyl((0, -0.6, 0), (0, 0.15, 0), 0.75, segs=20, kind=FRAME)                                # waist
    f.cyl((0, -0.28, 0), (0, -0.12, 0), 0.92, segs=20, kind=SOFT)
    f.box((0, 0.55, -1.0), (1.1, 0.38, 0.24), FRAME, 0.02)                                        # the chest guns' mount, low on the chest
    f.slats((0, 1.0, -1.03), (0, 0, -1), (1, 0, 0), (1.2, 0.4), 4, 0.05)
    m.moves[T + "/HardpointChestLaser"] = (0.0, 0.55, -1.115)
    m.props[T + "/HardpointChestLaser"] = {"Area": (1.0, 0.34), "Spacing": (0.55, 0, 0)}
    m.add(f, T + "/CenterTorso", "Frame", JOINT)

    # the cab: down between the side torsos, a heavy brow over a wide band of glass
    h = Part("Head_Armor")
    h.stations("z", [(0.3, 1.3, 0.6, 0, 3.2, 0.1), (-0.7, 1.3, 0.6, 0, 3.2, 0.1), (-1.1, 1.1, 0.5, 0, 3.15, 0.1), (-1.22, 0.94, 0.4, 0, 3.12, 0.08)], seams=SMALL_PANELS)
    h.box((0, 3.5, -0.95), (1.2, 0.1, 0.6), PAINT, 0.02)                                         # brow
    m.add(h, T + "/Head", "Skull", CAMO)
    v = Part("Head_Visor")
    v.loft([[(-0.52, 3.0, -1.2), (0.52, 3.0, -1.2), (0.46, 3.38, -0.98), (-0.46, 3.38, -0.98)],
            [(-0.5, 3.03, -1.23), (0.5, 3.03, -1.23), (0.44, 3.4, -1.01), (-0.44, 3.4, -1.01)]], PAINT, 0.006)
    m.add(v, T + "/Head", "Visor", GLASS)
    m.moves[T + "/CockpitCam"] = (0.0, 3.22, -1.36)
    for side, sx in (("L", -1), ("R", 1)):
        m.moves[f"{T}/Headlight{side}"] = (sx * 0.62, 0.95, -1.14)
    m.shapes[T + "/Head/Shape"] = (1.3, 0.7, 1.5)

    # side torsos: tall, rising over the cab, fronts raked back above the launchers; pauldrons over the shoulders
    left = Part("LeftTorso_Armor")
    side_loft(left, [(-1.15, 1.0, 0.0), (-2.05, 1.0, 0.25), (-2.22, 0.92, 0.3)], [(1.0, 0.2), (1.0, 3.5), (-0.65, 3.5), (-1.15, 1.6), (-1.05, 0.2)])
    left.box((-2.55, 3.1, 0.0), (1.05, 0.2, 1.6), PAINT, 0.03, SMALL_PANELS, Matrix.Rotation(math.radians(18), 3, "Z"))   # pauldron
    m.both(left, T + "/LeftTorso", "Hull", CAMO)
    r = Part("LeftTorso_Rack")
    r.box((-1.67, 2.75, -1.0), (0.95, 1.0, 0.55), PAINT, 0.03, SMALL_PANELS)                     # the launcher's housing, out of the raked front
    m.both(r, T + "/LeftTorso", "Rack", CAMO)
    rf = Part("LeftTorso_RackFace")
    rf.box((-1.67, 2.75, -1.285), (0.8, 0.85, 0.06), FRAME, 0.02)
    m.both(rf, T + "/LeftTorso", "RackFace", JOINT)
    m.moves[T + "/HardpointRackL"], m.moves[T + "/HardpointRackR"] = (-1.67, 2.75, -1.31), (1.67, 2.75, -1.31)
    m.props[T + "/HardpointRackL"] = m.props[T + "/HardpointRackR"] = {"Area": (0.76, 0.8)}
    m.shapes[T + "/LeftTorso/Shape"] = m.shapes[T + "/RightTorso/Shape"] = (1.1, 3.3, 2.2)

    arms_at(m, T, 3.05)
    j = Part("ShoulderJointL")
    j.cyl((0.5, 0, 0), (-0.45, 0, 0), 0.62, segs=18, kind=FRAME)
    j.cyl((-0.45, 0, 0), (-0.55, 0, 0), 0.38, segs=14, kind=SOFT)
    m.both(j, T, "ShoulderJointL", JOINT, own_place=True)
    u = Part("UpperArmL")
    u.stations("y", [(0.85, 1.1, 1.2, 0, 0, 0.12), (0.2, 1.16, 1.2, 0, 0, 0.12), (-0.75, 1.0, 1.05, 0, 0, 0.1)], seams=SMALL_PANELS)
    m.both(u, T, "UpperArmL", CAMO, own_place=True)
    assault_arm(m, T, 3.2, 1.45, 1.55, fins=8)

    hips(m, 0.95, spread=0.41)
    cockpit(m)
    legs(m, (2.5, 1.3, 1.5), (2.5, 1.1, 1.25), (0.441, 0.5, 0.55), (1.6, 0.3325, 2.4), (0.72, 0.64, 0.44), spur=False, foot_style="boot",
         thigh_armor=direwolf_thigh, shin_armor=direwolf_shin)
    mech_proportions(m, 5.35, 1.55, 1.85, 2.5, 2.5, 0.441)
    return m

# ── The Mantis ───────────────────────────────────────────────────────────────────────────────────
# Drawn from its own pictures, not from any machine above: a long low box of a body with a raked
# glacis — the cab's slit windows on its left, a honeycomb of missile tubes on its right, hazard
# stripes down its edges — gun pods slung either side like a mantis's forelegs, each with one heavy
# barrel; long straight legs with great domed shin guards, and flat four-clawed feet.
BRONZE, HAZARD, HAZARD_DARK = "mech_bronze", "hazard_yellow", "hazard_black"


def mantis_leg_parts(m, thigh_len, shin_len):
    """Both legs of the Mantis in their bones' axes (+Y to the foot, +Z to the front, left leg: -X out)."""
    th, sh = thigh_len / 2, shin_len / 2
    # Thigh: a box girder narrowing to the knee, armour faced in front with a raised, bolted glacis plate,
    # recessed side panels, and two rams working the knee down its back.
    t = Part("ThighL_Armor")
    t.stations("y", [(-th - 0.1, 0.8, 0.96, 0, 0.0, 0.16), (-th + 0.25, 0.96, 1.18, 0, 0.03, 0.2), (-th + 0.9, 1.0, 1.24, 0, 0.05, 0.2),
                     (th - 0.9, 0.9, 1.06, 0, 0.06, 0.18), (th - 0.35, 0.8, 0.9, 0, 0.04, 0.15), (th - 0.05, 0.68, 0.76, 0, 0.02, 0.12)],
               seams=SMALL_PANELS, cuts=[((0, 0, 0), (0, 1, 0))])
    t.stations("y", [(-th + 0.2, 0.7, 0.12, 0, 0.64, 0.04), (-th + 0.45, 0.86, 0.2, 0, 0.68, 0.06), (th - 0.75, 0.8, 0.2, 0, 0.62, 0.06), (th - 0.45, 0.62, 0.12, 0, 0.58, 0.04)],
               seams=SMALL_PANELS)                                                                            # glacis plate
    t.bolts([(sx, y, 0.78) for sx in (-0.3, 0.3) for y in (-th + 0.55, 0.0, th - 0.8)], (0, 0, 1), r=0.04)
    for sx in (-1, 1):
        t.plate(None, (sx * 0.49, 0.05, 0.02), (sx, 0, 0), (0.62, 1.3), 0.025, kind=RECESS)                    # side panel let in
        t.bolts([(sx * 0.515, y, z) for y in (-0.55, 0.65) for z in (-0.22, 0.26)], (sx, 0, 0), r=0.03)
    m.both(t, "LegL/Thigh/Hit", "Mesh", CAMO)
    tf = Part("ThighL_Frame")
    for sx in (-0.22, 0.22):
        mt_ram(tf, (sx, -th + 0.35, -0.66), (sx, th - 0.4, -0.58), 0.11)
        mt_pipe(tf, [(sx, -th + 0.62, -0.78), (sx * 1.3, -th + 0.3, -0.62), (sx * 1.3, -th + 0.1, -0.38)], 0.025, SOFT, 8)
    for y, w in ((-th + 0.35, 0.68), (th - 0.4, 0.62)):                                                    # lugs the rams hang on
        tf.prism([(0.0, 0.12), (-0.3, 0.08), (-0.34, -0.06), (-0.12, -0.14), (0.0, -0.12)], "x", -w / 2, w / 2, FRAME, 0.015, centre=(-0.42, y))
    m.both(tf, "LegL/Thigh/Hit", "Frame", JOINT)

    # Shin: a strut of octagonal section, a knee plate, the great faceted guard over its lower front, and a
    # bronze-cased ram behind working the ankle.
    s_ = Part("ShinL_Armor")
    s_.stations("y", [(-sh - 0.05, 0.6, 0.68, 0, 0, 0.12), (-sh + 0.55, 0.68, 0.78, 0, -0.04, 0.14), (0.0, 0.64, 0.72, 0, -0.05, 0.13), (sh - 0.3, 0.56, 0.62, 0, -0.04, 0.1),
                      (sh - 0.05, 0.5, 0.54, 0, -0.02, 0.09)], seams=SMALL_PANELS)
    s_.stations("y", [(-sh + 0.15, 0.6, 0.12, 0, 0.42, 0.04), (-sh + 0.4, 0.76, 0.2, 0, 0.46, 0.06), (-sh + 0.95, 0.7, 0.18, 0, 0.44, 0.05), (-sh + 1.15, 0.56, 0.1, 0, 0.42, 0.03)],
                seams=SMALL_PANELS)                                                                           # knee plate
    for sx in (-1, 1):
        s_.stations("y", [(-sh + 0.7, 0.08, 0.5, sx * 0.355, -0.08, 0.02), (sh - 0.5, 0.08, 0.42, sx * 0.325, -0.08, 0.02)], kind=FRAME)   # side rails
    m.both(s_, "LegL/Shin/Hit", "Mesh", CAMO)
    sf = Part("ShinL_Frame")
    y0, y1 = -sh + 1.15, sh - 0.12
    rows = []
    for k, (wk, dk) in enumerate(((0.62, 0.3), (0.86, 0.62), (0.98, 0.84), (1.0, 0.92), (0.96, 0.86), (0.84, 0.66), (0.62, 0.34))):
        rows.append((y0 + (y1 - y0) * k / 6, 0.95 * wk, 0.62 * dk + 0.1, 0.12, 0.13 * wk))
    mt_shell(sf, rows, PAINT, SMALL_PANELS)                                                                  # the dome
    sf.stations("y", [(y0 + 0.08, 0.6, 0.08, 0, 0.27, 0.02), (y0 + 0.16, 0.66, 0.08, 0, 0.3, 0.02)], kind=FRAME)       # its upper lip
    sf.bolts([(sx * 0.3, y0 + 0.35, 0.7) for sx in (-1, 1)] + [(sx * 0.3, y1 - 0.4, 0.68) for sx in (-1, 1)], (0, 0, 1), r=0.035)
    mt_ram(sf, (0, -sh + 0.95, -0.52), (0, sh - 0.42, -0.44), 0.15)
    sf.prism([(0.0, 0.14), (-0.22, 0.1), (-0.26, -0.08), (0.0, -0.14)], "x", -0.2, 0.2, FRAME, 0.015, centre=(-0.3, -sh + 0.95))   # lug for it
    mt_pipe(sf, [(0.12, -sh + 1.3, -0.52), (0.19, -sh + 1.4, -0.49), (0.23, -sh + 1.6, -0.44), (0.24, -sh + 1.85, -0.36)], 0.025, SOFT, 8)
    m.both(sf, "LegL/Shin/Hit", "Frame", BRONZE)

    # Ankle: a housing narrowing down to the foot's joint, with a link on its back.
    a = Part("MetaL")
    a.stations("y", [(-0.3, 0.48, 0.54, 0, 0, 0.1), (-0.05, 0.54, 0.6, 0, 0.02, 0.12), (0.25, 0.44, 0.48, 0, 0.0, 0.09)], kind=FRAME, seams=SMALL_PANELS)
    mt_eye(a, (0, -0.22, -0.38), (0, 1, 0), (1, 0, 0), 0.1, 0.16, 0.3)
    m.both(a, "LegL/Meta", "Mesh", JOINT)

    # Foot: four claws set as a cross round a hub, each a ridged wedge thinning to a blunt point.
    G = -0.175
    plate = Part("FootL_Plate")
    toes = Part("FootL_Toes")
    for ang, part, kind in ((0.0, plate, PAINT), (math.pi, plate, PAINT), (math.pi / 2, toes, FRAME), (-math.pi / 2, toes, FRAME)):
        rot = Matrix.Rotation(ang, 3, "Y")
        secs = []
        for z, w, h in ((-1.25, 0.28, 0.14), (-1.05, 0.58, 0.24), (-0.6, 0.66, 0.36), (-0.25, 0.6, 0.42)):
            outline = [(w / 2, 0), (w / 2, h * 0.62), (w * 0.14, h), (-w * 0.14, h), (-w / 2, h * 0.62), (-w / 2, 0)]
            # (the side claws' soles a hair higher than the others', so the two don't lie in one plane where they cross)
            secs.append([tuple(rot @ Vector((u, G + v + (0.012 if part is toes else 0.0), z))) for u, v in outline])
        part.loft(secs, kind, 0.02, SMALL_PANELS if kind == PAINT else None)
        part.bolts([tuple(rot @ Vector((sx * 0.2, G + 0.27, -0.85))) for sx in (-1, 1)], (0, 1, 0), r=0.03)
    plate.stations("y", [(G + 0.02, 0.78, 0.78, 0, 0, 0.18), (G + 0.42, 0.7, 0.7, 0, 0, 0.16), (G + 0.6, 0.46, 0.46, 0, 0, 0.12)], seams=SMALL_PANELS)
    m.both(plate, "LegL/Foot", "Plate", CAMO)
    toes.lathe((-0.32, 0.22, 0.0), (0.32, 0.22, 0.0), [(0, 0.17), (0.05, 0.22), (0.12, 0.26), (0.88, 0.26), (0.95, 0.22), (1, 0.17)], segs=16, kind=FRAME)   # ankle pin
    m.both(toes, "LegL/Foot", "Toes", JOINT)
    print_ = plate.sole("Footprint")
    print_.merge(toes.sole("t"))
    m.loose.append(print_)
    m.removes += ["LegL/Foot/Spur", "LegR/Foot/Spur"]
    m.props["LegL"] = m.props["LegR"] = {"ToeReach": 1.2, "HeelReach": 1.2}

    # Joints: grooved drums with bolted flanges and capped hubs; the hip and the knee under armoured hoods.
    for name, r, half, hood in (("HipJoint", 0.62, 0.5, (0.35, 2.4)), ("KneeJoint", 0.56, 0.44, (-0.2, 1.9)), ("HeelJoint", 0.42, 0.36, None)):
        j = Part(name)
        mt_hub(j, half, r)
        if hood:
            mt_cowl(j, -half + 0.04, half - 0.04, r + 0.02, r + 0.13, hood[0], hood[1], PAINT)
        m.add(j, "LegL", name, JOINT, own_place=True)
        m.add(j, "LegR", name, JOINT, own_place=True)


def mantis():
    m = Model("mantis", "scenes/mech/mantis.tscn")
    T = "Hips/Torso"

    # The body: a long box, its glacis raked back from the chin to the roof, the rear stepped down.
    p = Part("CenterTorso_Armor")
    p.stations("z", [(-2.05, 2.4, 0.9, 0, 0.55, 0.12), (-1.85, 2.5, 1.6, 0, 0.95, 0.16), (-1.0, 2.6, 2.0, 0, 1.05, 0.2),
                     (1.0, 2.6, 2.0, 0, 1.05, 0.2), (1.6, 2.3, 1.5, 0, 1.2, 0.16)], seams=PANELS, cuts=[((0, 0, -0.2), (0, 0, 1)), ((0, 0, 0.9), (0, 0, 1))])
    # the roof: a raised spine with vents along it
    p.box((0, 2.1, 0.1), (1.0, 0.16, 2.2), PAINT, 0.03, SMALL_PANELS)
    for s in (-1, 1):
        p.slats((s * 0.75, 2.05, 0.6), (0, 1, 0), (1, 0, 0), (0.4, 1.2), 6, 0.04)
    m.add(p, T + "/CenterTorso", "Armor", CAMO)

    f = Part("CenterTorso_Frame")
    # the waist under it, the reactor's exhaust grilles in the stepped-down rear, and its hazard stripes
    f.lathe((0, -0.62, 0), (0, 0.12, 0), [(0, 0.64), (0.05, 0.72), (0.3, 0.72), (0.32, 0.88), (0.52, 0.88), (0.54, 0.74), (0.7, 0.74), (0.72, 0.68),
                                          (0.78, 0.68), (0.8, 0.72), (0.96, 0.72), (1, 0.66)], segs=24, kind=FRAME)                    # waist ring
    f.bolts([(math.cos(i * math.tau / 12) * 0.8, -0.14, math.sin(i * math.tau / 12) * 0.8) for i in range(12)], (0, 1, 0), r=0.035)
    f.slats((0, 1.1, 1.62), (0, 0, 1), (1, 0, 0), (1.6, 0.9), 7, 0.08)
    for s in (-1, 1):
        f.lathe((s * 0.85, 1.95, 1.0), (s * 0.85, 2.35, 1.25), [(0, 0.15), (0.1, 0.17), (0.2, 0.17), (0.24, 0.13), (0.78, 0.13), (0.82, 0.16), (0.94, 0.16), (1, 0.14)],
                segs=12, kind=FRAME)                                                                                       # exhaust stacks
        f.lathe((s * 0.85, 2.33, 1.24), (s * 0.85, 2.37, 1.26), [(0, 0.1), (1, 0.1)], segs=12, kind=RECESS)
    lifting_eyes(f, [(sx * 0.95, 2.05, -0.6) for sx in (-1, 1)])
    m.add(f, T + "/CenterTorso", "Frame", JOINT)
    hz = Part("Hazard")
    # yellow chevrons down the glacis's outer edges, outboard of the cab and the missiles (each seated on
    # the plate under it, which leans back above the chin)
    for s in (-1, 1):
        for i in range(4):
            y = 0.55 + i * 0.3
            with hz.detail((s * 1.1, y, -1.9), (0, 0.26, -0.97)):
                hz.box((s * 1.1, y, -2.1), (0.26, 0.12, 0.05), PAINT, 0.005, None, Matrix.Rotation(s * 0.6, 3, "Z"))
    m.add(hz, T + "/CenterTorso", "Hazard", HAZARD)

    # The cab: a raised armoured frame on the glacis's left with three slit windows in it.
    h = Part("Head_Armor")
    h.box((-0.5, 1.2, -1.97), (0.85, 1.05, 0.22), PAINT, 0.03)
    for x in (-0.78, -0.5, -0.22):
        h.box((x, 1.25, -2.1), (0.08, 0.86, 0.06), FRAME, 0.01)                                         # mullions
    h.box((-0.5, 1.73, -2.1), (0.9, 0.1, 0.12), FRAME, 0.01)                                            # brow
    m.add(h, T + "/Head", "Skull", CAMO)
    v = Part("Head_Visor")
    for x in (-0.64, -0.36):
        v.box((x, 1.25, -2.09), (0.2, 0.8, 0.04), PAINT, 0.005)
    m.add(v, T + "/Head", "Visor", GLASS)
    # the missile honeycomb on the glacis's right: a recessed frame the launcher's face stands in
    m.moves[T + "/HardpointChest"] = (0.55, 1.2, -2.05)
    m.props[T + "/HardpointChest"] = {"Area": (0.85, 0.95)}
    rf = Part("Rack")
    rf.box((0.55, 1.2, -1.98), (1.0, 1.05, 0.12), FRAME, 0.02)
    m.add(rf, T + "/CenterTorso", "Rack", JOINT)

    # Side torsos: the shoulders the gun pods hang from.
    left = Part("LeftTorso_Hull")
    left.stations("x", [(-1.25, 2.4, 1.5, -0.1, 1.0, 0.16), (-1.75, 2.1, 1.3, -0.1, 1.05, 0.16)], seams=SMALL_PANELS)
    left.slats((-1.5, 1.72, -0.1), (0, 1, 0), (1, 0, 0), (0.3, 1.2), 5, 0.04)
    m.both(left, T + "/LeftTorso", "Hull", CAMO)
    m.removes += [T + "/LeftTorso/Shoulder", T + "/RightTorso/Shoulder", T + "/Stripe"]
    # (on the side torsos' faces, which stand 1.24 ahead)
    m.moves[T + "/HardpointLeftTorso"] = (-1.5, 1.05, -1.24)
    m.moves[T + "/HardpointRightTorso"] = (1.5, 1.05, -1.24)
    m.props[T + "/HardpointLeftTorso"] = {"Area": (0.45, 0.6)}
    m.props[T + "/HardpointRightTorso"] = {"Area": (0.45, 0.6)}

    a = Part("Antenna")
    with a.detail((0.0, 2.18, 1.0), (0, 1, 0)):
        a.lathe((0.0, 2.17, 1.0), (0.0, 2.36, 1.0), [(0, 0.1), (0.25, 0.1), (0.3, 0.07), (0.7, 0.06), (0.75, 0.045), (1, 0.035)], segs=12, kind=FRAME)   # mount
        a.lathe((0.0, 2.34, 1.0), (0.0, 3.6, 1.05), [(0, 0.026), (0.3, 0.02), (0.31, 0.024), (0.33, 0.017), (0.65, 0.013), (0.66, 0.017), (0.68, 0.011), (1, 0.007)], segs=8, kind=FRAME)
    m.add(a, T, "Antenna", JOINT)

    # Shoulder pivots: a drum from the side torso out to the pod.
    u = Part("UpperArmL")
    u.lathe((0.75, 0, 0), (-0.1, 0, 0), [(0, 0.36), (0.04, 0.42), (0.4, 0.42), (0.42, 0.5), (0.58, 0.5), (0.6, 0.42), (0.8, 0.42), (0.82, 0.38), (0.86, 0.38),
                                         (0.88, 0.42), (0.96, 0.42), (1, 0.36)], segs=20, kind=FRAME)
    u.bolts([(0.24, math.cos(i * math.tau / 8) * 0.46, math.sin(i * math.tau / 8) * 0.46) for i in range(8)], (-1, 0, 0), r=0.03)
    m.both(u, T, "UpperArmL", JOINT, own_place=True)
    m.moves[T + "/UpperArmL"] = (-2.05, 1.05, -0.1)
    m.moves[T + "/UpperArmR"] = (2.05, 1.05, -0.1)
    m.moves[T + "/ArmL"] = (-2.6, 1.05, -0.1)
    m.moves[T + "/ArmR"] = (2.6, 1.05, -0.1)

    # Gun pods: a long box, chamfered, its top raked down to the back, the barrel's mount in its face.
    arm = Part("ArmL_Armor")
    arm.stations("z", [(1.3, 0.8, 0.7, 0, -0.15, 0.1), (0.8, 1.0, 1.1, 0, 0, 0.14), (-1.9, 1.0, 1.15, 0, 0, 0.14), (-2.15, 0.88, 1.0, 0, 0, 0.12)],
                 seams=PANELS, cuts=[((0, 0, -0.6), (0, 0, 1))])
    arm.slats((-0.51, 0.0, -0.4), (-1, 0, 0), (0, 0, 1), (1.4, 0.7), 5, 0.05)
    m.both(arm, T + "/ArmL/Hit", "Forearm", CAMO)
    hand = Part("ArmL_Frame")
    hand.box((0, 0, -2.18), (0.8, 0.92, 0.1), FRAME, 0.02)                                            # face the guns stand on
    hand.lathe((0.55, 0, 0), (0.42, 0, 0), [(0, 0.3), (0.2, 0.36), (0.8, 0.36), (1, 0.3)], segs=16, kind=FRAME)                    # pivot boss
    m.both(hand, T + "/ArmL/Hit", "Hand", JOINT)
    for side in "LR":
        m.removes += [f"{T}/Arm{side}/Hit/Elbow", f"{T}/Arm{side}/Hit/Barrel"]
        m.moves[f"{T}/Arm{side}/HardpointArm"] = (0.0, 0.0, -2.2)
        m.props[f"{T}/Arm{side}/HardpointArm"] = {"Area": (0.78, 0.88)}
    m.shapes[T + "/ArmL/Hit/Shape"] = m.shapes[T + "/ArmR/Hit/Shape"] = (1.0, 1.15, 3.45)
    m.moves[T + "/ArmL/Insignia"] = (-0.52, 0.1, -1.2)
    m.moves[T + "/ArmR/Insignia"] = (0.52, 0.1, -1.2)

    # Pelvis: a narrow keel under the waist with the hip axles out to either side, a plate fore and aft.
    pv = Part("Pelvis")
    pv.stations("y", [(-0.62, 0.9, 1.0, 0, 0, 0.14), (-0.1, 1.4, 1.3, 0, 0, 0.2), (0.4, 1.4, 1.36, 0, 0, 0.2)], kind=FRAME, seams=SMALL_PANELS)
    for sx in (-1, 1):
        pv.lathe((sx * 0.5, -0.19, 0), (sx * 1.25, -0.19, 0), [(0, 0.5), (0.1, 0.5), (0.14, 0.44), (0.5, 0.44), (0.53, 0.4), (0.62, 0.4), (0.65, 0.44), (0.94, 0.44), (1, 0.4)],
                 segs=18, kind=FRAME)                                                                                   # hip axles
    pv.lathe((0, 0.38, 0), (0, 0.66, 0), [(0, 0.78), (0.12, 0.84), (0.5, 0.84), (0.56, 0.8), (0.9, 0.8), (1, 0.74)], segs=24, kind=FRAME)          # swivel
    m.add(pv, "Hips", "Pelvis", JOINT)
    pa = Part("PelvisArmor")
    for sz in (-1, 1):
        pa.box((0, -0.25, sz * 0.78), (1.3, 0.9, 0.16), PAINT, 0.03, SMALL_PANELS, Matrix.Rotation(sz * math.radians(14), 3, "X"))
    m.add(pa, "Hips", "PelvisArmor", CAMO)

    # the pilot's eye just behind the slit windows
    m.moves[T + "/CockpitCam"] = (-0.55, 1.3, -2.0)
    cockpit(m)
    mantis_leg_parts(m, 2.744, 2.744)
    return m

{"agni": agni, "ostrich": ostrich, "hound": hound, "ballista": ballista, "panther": panther, "hades": lambda: cleaver("hades"), "executioner": executioner, "mantis": mantis, "mauler": mauler, "direwolf": direwolf}[ARGS[1]]().finish([ARGS[0]] + ARGS[2:])
