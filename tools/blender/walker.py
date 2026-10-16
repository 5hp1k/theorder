"""Shared pieces of the walking machines: legs, hips, joints. Everything is modelled at the Ostrich's
size and then stretched to the proportions asked for, so the same details serve a light scout and a
heavy missile carrier."""
import math
from meshkit import *

PANELS = dict(margin=0.07, width=0.016, depth=0.018, min_area=0.3)
SMALL_PANELS = dict(margin=0.05, width=0.012, depth=0.014, min_area=0.12)


def piston(p, a, b, r, split=0.55):
    """Hydraulic ram from a to b: a fat sleeve, then a bright rod."""
    a, b = Vector(a), Vector(b)
    mid = a + (b - a) * split
    d = (b - a).normalized()
    p.cyl(tuple(a), tuple(mid), r, segs=10, kind=FRAME, bevel=0.01)
    p.cyl(tuple(mid), tuple(b), r * 0.55, segs=8, kind=EDGE, bevel=0)
    p.cyl(tuple(mid - d * 0.04), tuple(mid + d * 0.02), r * 1.25, segs=10, kind=SOFT, bevel=0)


def myomer(p, a, b, r, strands=3, spread=None):
    """An artificial muscle from a to b: a bundle of ribbed strands, thick in the belly and drawn in
    to a clamp at each end, the way a muscle runs into its tendon."""
    a, b = Vector(a), Vector(b)
    d = (b - a).normalized()
    side = d.cross(Vector((1, 0, 0)))
    if side.length < 1e-3:
        side = d.cross(Vector((0, 0, 1)))
    side.normalize()
    across = d.cross(side)
    spread = r * 1.5 if spread is None else spread
    length = (b - a).length
    for i in range(strands):
        ang = i * math.tau / strands + 0.5
        off = (side * math.cos(ang) + across * math.sin(ang)) * spread * (0.0 if strands == 1 else 1.0)
        # belly: fattest in the middle
        p.lathe(tuple(a + off * 0.35), tuple(b + off * 0.35), [(0.0, r * 0.45), (0.12, r * 0.8), (0.5, r), (0.88, r * 0.8), (1.0, r * 0.45)], segs=8, kind=SOFT)
        # the weave: rings along it
        n = max(3, int(length / (r * 2.2)))
        for j in range(1, n):
            # each strand's rings a little off its neighbours', so no two ring faces share a plane
            t = (j + (i / strands - 0.5) * 0.4) / n
            c = a + (b - a) * t + off * 0.35
            fat = r * (0.8 + 0.2 * math.sin(t * math.pi)) * 1.06
            p.cyl(tuple(c - d * r * 0.16), tuple(c + d * r * 0.16), fat, segs=8, kind=RECESS, bevel=0)
    for end in (a, b):
        p.cyl(tuple(end - d * r * 0.5), tuple(end + d * r * 0.5), r * 1.9 if strands > 1 else r * 1.2, segs=10, kind=FRAME, bevel=0.01)


# How much heavier than drawn the legs are built: machines of this sort stand on massive lower legs
# and broad feet. (Width and depth of the bones; the lengths, which the walk depends on, are untouched.)
HEAVY_THIGH = 1.3
HEAVY_SHIN = 1.42
HEAVY_META = 1.3
HEAVY_FOOT = (1.24, 1.15, 1.12)
HEAVY_JOINT = 1.22


def joint(name, r, half):
    """A hinge drum lying across the leg, with bolted caps."""
    j = Part(name)
    j.cyl((-half, 0, 0), (half, 0, 0), r, segs=18, kind=FRAME)
    for s in (-1, 1):
        j.cyl((s * half, 0, 0), (s * (half + 0.06), 0, 0), r * 0.62, segs=14, kind=SOFT)
        j.bolts([(s * half, math.cos(i * math.tau / 6) * r * 0.8, math.sin(i * math.tau / 6) * r * 0.8) for i in range(6)], (s, 0, 0), r=r * 0.07, h=0.04)
    j.cyl((-half * 0.35, 0, 0), (half * 0.35, 0, 0), r * 1.08, segs=18, kind=SOFT)
    return j


def legs(model, thigh, shin, meta, foot, joints, spur=True, foot_shift=0.0, foot_style=None):
    """Both legs. thigh / shin / meta: (length, width, depth) of the bone as in the scene;
    foot: (width, height, length) of the sole; joints: radii of hip, knee and heel.
    Bone axes: +Y towards the foot, +Z the front of the limb; left leg, outside = -X.
    foot_style "cross": four toes — fore, aft and one to each side — as on the Agni;
               "box": a flat oblong sole, square at both ends, as on the Executioner."""
    man = meta[0] <= 0.9
    # Thigh ------------------------------------------------------------------------------------------
    t = Part("ThighL_Armor")
    t.stations("y", [(-1.25, 0.95, 1.15, 0, 0, 0.2), (0.1, 0.9, 1.05, 0, 0.03, 0.2), (1.2, 0.66, 0.78, 0, 0.05, 0.15)], seams=PANELS, cuts=[((0, -0.25, 0), (0, 1, 0))])
    # knee guard: a thick slab hung over the joint, with its hinge lugs
    t.stations("y", [(0.62, 0.78, 0.26, 0, 0.5, 0.06), (1.4, 0.74, 0.4, 0, 0.58, 0.08), (1.86, 0.56, 0.26, 0, 0.48, 0.05)], seams=SMALL_PANELS)
    t.bolts([(-0.26, 0.9, 0.67), (0.26, 0.9, 0.67), (-0.26, 1.5, 0.74), (0.26, 1.5, 0.74)], (0, 0, 1), r=0.04)
    # hip actuator housing on the outside, and an inspection hatch
    t.box((-0.5, -0.98, 0.0), (0.16, 0.5, 0.7), PAINT, 0.03, SMALL_PANELS)
    t.slats((-0.585, -0.98, 0.0), (-1, 0, 0), (0, 0, 1), (0.36, 0.5), 4, 0.03)
    k = (thigh[1] / 0.95 * HEAVY_THIGH, thigh[0] / 2.7, thigh[2] / 1.15 * HEAVY_THIGH)
    model.both(t.scale(k), "LegL/Thigh/Hit", "Mesh", CAMO)
    model.moves["LegL/Thigh/Insignia"] = (-0.535 * k[0] - 0.005, -0.35 * k[1], 0.0)
    model.moves["LegR/Thigh/Insignia"] = (0.535 * k[0] + 0.005, -0.35 * k[1], 0.0)
    model.shapes["LegL/Thigh/Hit/Shape"] = model.shapes["LegR/Thigh/Hit/Shape"] = (thigh[1] * HEAVY_THIGH, thigh[0], thigh[2] * HEAVY_THIGH)

    f = Part("ThighL_Frame")
    # the muscles of the thigh: a heavy bundle behind the bone, a lighter pair in front of it
    myomer(f, (0, -1.08, -0.58), (0, 1.22, -0.4), 0.15, strands=3)
    for s in (-1, 1):
        myomer(f, (s * 0.24, -0.6, 0.5), (s * 0.2, 0.62, 0.46), 0.075, strands=1)
    f.box((0, -1.3, 0), (0.56, 0.3, 0.8), FRAME)
    f.box((0, 1.28, 0), (0.46, 0.3, 0.6), FRAME)
    model.both(f.scale(k), "LegL/Thigh/Hit", "Frame", JOINT)

    # Shin: the heaviest part of the leg — a boxed greave over a calf full of muscle ---------------------
    s_ = Part("ShinL_Armor")
    s_.stations("y", [(-1.35, 0.72, 0.88, 0, 0, 0.16), (0.3, 0.74, 0.86, 0, 0.02, 0.15), (1.3, 0.62, 0.66, 0, 0, 0.1)], seams=PANELS, cuts=[((0, -0.3, 0), (0, 1, 0))])
    s_.slats((-0.365, -0.95, 0.0), (-1, 0, 0), (0, 0, 1), (0.5, 0.36), 4, 0.04)
    # ankle skirt
    s_.stations("y", [(0.95, 0.8, 0.84, 0, 0.02, 0.1), (1.42, 0.86, 0.9, 0, 0.02, 0.1)], seams=SMALL_PANELS)
    k = (shin[1] / 0.75 * HEAVY_SHIN, shin[0] / 2.9, shin[2] / 0.85 * HEAVY_SHIN)
    model.both(s_.scale(k), "LegL/Shin/Hit", "Mesh", CAMO)
    model.shapes["LegL/Shin/Hit/Shape"] = model.shapes["LegR/Shin/Hit/Shape"] = (shin[1] * HEAVY_SHIN, shin[0], shin[2] * HEAVY_SHIN)

    f = Part("ShinL_Frame")
    myomer(f, (0, -1.2, -0.5), (0, 1.15, -0.34), 0.13, strands=3)
    f.box((0, -1.38, 0), (0.44, 0.26, 0.6), FRAME)
    f.box((0, 1.36, 0), (0.36, 0.24, 0.42), FRAME)
    # cable trunk down the inside of the leg
    f.cyl((0.34, -1.2, 0.1), (0.3, 1.2, 0.08), 0.045, segs=6, kind=SOFT, bevel=0)
    model.both(f.scale(k), "LegL/Shin/Hit", "Frame", JOINT)

    # Metatarsal: a strut with muscle on a bird leg, just an ankle block on a man-like one ------------
    m = Part("MetaL")
    if not man:
        m.stations("y", [(-0.8, 0.5, 0.55, 0, 0, 0.1), (0.8, 0.44, 0.46, 0, 0, 0.08)], kind=FRAME, seams=SMALL_PANELS)
        for s in (-1, 1):
            myomer(m, (s * 0.15, -0.7, 0.34), (s * 0.13, 0.72, 0.27), 0.07, strands=1)
        m.box((0, 0.0, -0.3), (0.4, 0.9, 0.1), FRAME, 0.02)
        m.scale((meta[1] / 0.55 * HEAVY_META, meta[0] / 1.6, meta[2] / 0.6 * HEAVY_META))
    else:
        w, d = meta[1] * HEAVY_META, meta[2] * HEAVY_META
        m.stations("y", [(-meta[0] / 2, w, d, 0, 0, 0.08), (meta[0] / 2, w * 0.9, d * 0.9, 0, 0, 0.08)], kind=FRAME)
    model.both(m, "LegL/Meta", "Mesh", JOINT)

    # Foot (level with the ground; toes point to -Z). Modelled round the Ostrich's sole: 1.3 x 0.35 x 2.3
    G = -0.175
    kf = (foot[0] / 1.3 * HEAVY_FOOT[0], foot[1] / 0.35 * HEAVY_FOOT[1], foot[2] / 2.3 * HEAVY_FOOT[2])
    if foot_style == "cross":
        print_ = cross_foot(model, kf, G)
        # (its ground point is the middle of the cross)
        model.props["LegL"] = model.props["LegR"] = {"ToeReach": 1.02 * kf[2], "HeelReach": 1.02 * kf[2]}
    elif foot_style == "box":
        p = Part("FootL_Plate")
        # the sole: an oblong slab, and a deck on it carrying the ankle
        p.stations("z", [(1.1, 1.3, 0.34, 0, G + 0.17, 0.04), (-1.72, 1.3, 0.34, 0, G + 0.17, 0.04)], seams=SMALL_PANELS)
        p.stations("z", [(0.75, 0.92, 0.24, 0, G + 0.44, 0.06), (-1.0, 0.92, 0.24, 0, G + 0.44, 0.06)], seams=SMALL_PANELS)
        p.bolts([(sx * 0.52, G + 0.35, z) for sx in (-1, 1) for z in (-1.4, -0.4, 0.6)], (0, 1, 0), r=0.035)
        model.both(p.scale(kf).move((0, 0, foot_shift)), "LegL/Foot", "Plate", CAMO)
        print_ = p.sole("Footprint")
        toes = Part("FootL_Toes")
        toes.cyl((-0.5, 0.2, 0.0), (0.5, 0.2, 0.0), 0.27, segs=14, kind=FRAME)  # ankle
        # a bumper across the toe and a heel block, bare frame
        toes.box((0, G + 0.19, -1.74), (1.34, 0.26, 0.12), FRAME, 0.02)  # its top just under the sole's
        toes.box((0, G + 0.2, 1.12), (1.1, 0.26, 0.12), FRAME, 0.02)
        model.both(toes.scale(kf).move((0, 0, foot_shift)), "LegL/Foot", "Toes", JOINT)
        model.removes += ["LegL/Foot/Spur", "LegR/Foot/Spur"]
        model.props["LegL"] = model.props["LegR"] = {"ToeReach": (1.72 * kf[2] - foot_shift) * 0.92, "HeelReach": (1.1 * kf[2] + foot_shift) * 0.92}
    else:
        p = Part("FootL_Plate")
        p.stations("z", [(0.8, 0.7, 0.3, 0, G + 0.15, 0.08), (0.15, 1.08, 0.52, 0, G + 0.26, 0.14), (-0.85, 1.22, 0.34, 0, G + 0.17, 0.1)], seams=SMALL_PANELS)
        model.both(p.scale(kf).move((0, 0, foot_shift)), "LegL/Foot", "Plate", CAMO)
        print_ = p.sole("Footprint")

        toes = Part("FootL_Toes")
        for s in (-1, 1):
            toes.stations("z", [(-1.72, 0.3, 0.14, s * 0.46, G + 0.07, 0.03), (-1.3, 0.46, 0.26, s * 0.42, G + 0.13, 0.06), (-0.7, 0.5, 0.34, s * 0.36, G + 0.17, 0.07)], kind=FRAME)
            toes.cyl((s * 0.36 - 0.28, G + 0.2, -0.72), (s * 0.36 + 0.28, G + 0.2, -0.72), 0.17, segs=10, kind=SOFT)
        toes.cyl((-0.5, 0.2, 0.0), (0.5, 0.2, 0.0), 0.27, segs=14, kind=FRAME)  # ankle
        if not spur:
            # a man-like foot has a heel instead of a spur
            toes.stations("z", [(0.6, 0.72, 0.32, 0, G + 0.16, 0.07), (1.1, 0.6, 0.24, 0, G + 0.12, 0.06)], kind=FRAME)
        model.both(toes.scale(kf).move((0, 0, foot_shift)), "LegL/Foot", "Toes", JOINT)
        print_.merge(toes.sole("t"))

        if spur:
            sp = Part("FootL_Spur")
            sp.stations("z", [(0.6, 0.46, 0.3, 0, G + 0.15, 0.06), (1.3, 0.38, 0.22, 0, G + 0.11, 0.05), (1.8, 0.24, 0.12, 0, G + 0.06, 0.03)], kind=FRAME)
            model.both(sp.scale(kf).move((0, 0, foot_shift)), "LegL/Foot", "Spur", JOINT)
            print_.merge(sp.sole("s"))
        else:
            model.removes += ["LegL/Foot/Spur", "LegR/Foot/Spur"]
        # Where the foot rolls over: the tips of its toes and the end of its heel, from its ground point.
        toe, heel = (1.72 * kf[2] - foot_shift) * 0.92, ((1.8 if spur else 1.1) * kf[2] + foot_shift) * 0.92
        if man:
            model.props["LegL"] = model.props["LegR"] = {"ToeReach": toe, "HeelReach": heel}
        else:
            # A bird's foot is all toes: the leg's own heel is high up the metatarsal, and does the rolling.
            model.props["LegL"] = model.props["LegR"] = {"ToeReach": toe, "HeelReach": heel, "HeelOffAngle": 0.3, "HeelStrikeAngle": 0.14, "ToeHang": 0.5}

    # What the foot leaves on the ground: the outline of its sole.
    model.loose.append(print_)
    model.root["FootprintMesh"] = "Footprint"

    for name, r in zip(("HipJoint", "KneeJoint", "HeelJoint"), joints):
        r *= HEAVY_JOINT
        j = joint(name, r * 0.86, r * 0.83)
        model.add(j, "LegL", name, JOINT, own_place=True)
        model.add(j, "LegR", name, JOINT, own_place=True)
    return kf


def cross_foot(model, kf, G):
    """A foot of four toes set as a cross — one fore, one aft, one to each side — round a domed ankle
    block. Returns its print."""
    def toe(part, ang, length, kind):
        rot = Matrix.Rotation(ang, 3, "Y")
        # a toe pointing to -Z, then turned: a wedge rising to its knuckle
        secs = []
        for z, w, h in ((-length, 0.36, 0.12), (-length * 0.72, 0.5, 0.24), (-0.5, 0.56, 0.36), (-0.28, 0.5, 0.36)):
            pts = octagon(w, h, 0.05)
            secs.append([tuple(rot @ Vector((u, G + h / 2 + v, z))) for u, v in pts])
        part.loft(secs, kind, 0.02, SMALL_PANELS if kind == PAINT else None)
        # knuckle hinge
        a, b = rot @ Vector((-0.3, G + 0.22, -0.46)), rot @ Vector((0.3, G + 0.22, -0.46))
        part.cyl(tuple(a), tuple(b), 0.16, segs=10, kind=SOFT)  # (standing proud of the toe's back)

    plate = Part("FootL_Plate")
    # fore and aft toes are armoured, and the block they hinge on
    toe(plate, 0.0, 1.02, PAINT)
    toe(plate, math.pi, 1.02, PAINT)
    plate.stations("y", [(G + 0.02, 0.84, 0.84, 0, 0, 0.2), (G + 0.4, 0.76, 0.76, 0, 0, 0.2), (G + 0.56, 0.5, 0.5, 0, 0, 0.14)], seams=SMALL_PANELS)
    model.both(plate.scale(kf), "LegL/Foot", "Plate", CAMO)
    print_ = plate.sole("Footprint")

    toes = Part("FootL_Toes")
    # the side toes are bare frame; they are set by the sole's length, so the cross is even
    side = kf[2] / kf[0]
    toe(toes, math.pi / 2, 1.02 * side, FRAME)
    toe(toes, -math.pi / 2, 1.02 * side, FRAME)
    toes.cyl((-0.42, 0.2, 0.0), (0.42, 0.2, 0.0), 0.27, segs=14, kind=FRAME)  # ankle
    model.both(toes.scale(kf), "LegL/Foot", "Toes", JOINT)
    print_.merge(toes.sole("t"))
    model.removes += ["LegL/Foot/Spur", "LegR/Foot/Spur"]
    return print_


def hips(model, k=1.0, jets=None, spread=0.0):
    """Pelvis with the hip axles and the turret ring, at the Ostrich's size times k.
    jets: X positions (already in scene units) of jump-jet nozzles on the back, or None.
    spread: how much further out each hip is (before k), for a machine with its legs set wide."""
    p = Part("Pelvis")
    p.stations("y", [(-0.55, 1.0, 0.9, 0, 0, 0.15), (-0.1, 1.5, 1.3, 0, 0, 0.22), (0.45, 1.5, 1.4, 0, 0, 0.22)], kind=FRAME, seams=SMALL_PANELS)
    for s in (-1, 1):
        p.cyl((s * 0.55, -0.2, 0), (s * (1.12 + spread), -0.2, 0), 0.5, segs=16, kind=FRAME)
        p.cyl((s * (0.75 + spread), -0.2, 0), (s * (0.85 + spread), -0.2, 0), 0.58, segs=16, kind=SOFT)
    p.cyl((0, 0.4, 0), (0, 0.64, 0), 0.86, segs=20, kind=FRAME)
    p.scale(k)
    if jets:
        for x, y, z in jets:
            p.cyl((x, y + 0.45 * k, z - 0.25 * k), (x, y - 0.1 * k, z), 0.22 * k, 0.3 * k, segs=14, kind=FRAME)
            p.cyl((x, y - 0.1 * k, z), (x, y - 0.13 * k, z + 0.01), 0.24 * k, segs=14, kind=RECESS, bevel=0)
    model.add(p, "Hips", "Pelvis", JOINT)

    a = Part("PelvisArmor")
    tilt = Matrix.Rotation(math.radians(-18), 3, "X")
    # groin and tail plates, thick, with a bolted strip along the top edge
    a.box((0, -0.16, -0.82), (1.5, 1.0, 0.2), PAINT, 0.035, SMALL_PANELS, tilt)
    a.box((0, -0.16, 0.82), (1.5, 1.0, 0.2), PAINT, 0.035, SMALL_PANELS, tilt.inverted())
    a.bolts([(x, 0.2, -0.82) for x in (-0.55, -0.18, 0.18, 0.55)], (0, 0.3, -1), r=0.045)
    for s in (-1, 1):
        a.box((s * (0.9 + spread / 2), 0.38, 0), (0.7 + spread, 0.16, 1.3), PAINT, 0.03, SMALL_PANELS)
        # hip guards: slabs hung over the top of each thigh
        a.box((s * (1.5 + spread), -0.1, 0), (0.14, 0.9, 1.2), PAINT, 0.03, SMALL_PANELS, Matrix.Rotation(s * math.radians(-9), 3, "Z"))
        a.bolts([(s * (1.58 + spread), 0.2, -0.36), (s * (1.58 + spread), 0.2, 0.36)], (s, 0, 0), r=0.045)
    model.add(a.scale(k), "Hips", "PelvisArmor", CAMO)


def barrel(p, at, length, r, kind="laser"):
    """A gun pointing to -Z with its breech at 'at'."""
    x, y, z = at
    if kind == "laser":
        # (the core stops just short of the muzzle collar's face, so the two faces don't share a plane)
        p.cyl((x, y, z), (x, y, z - length + 0.01), r * 0.62, segs=12, kind=FRAME, bevel=0)
        p.cyl((x, y, z - length * 0.08), (x, y, z - length * 0.62), r, segs=12, kind=FRAME)
        n = max(3, int(length * 0.5 / (r * 1.1)))
        for i in range(n):
            zz = z - length * 0.12 - i * (length * 0.46 / n)
            p.cyl((x, y, zz), (x, y, zz - r * 0.36), r * 1.25, segs=12, kind=SOFT, bevel=0.008)
        p.cyl((x, y, z - length * 0.88), (x, y, z - length), r * 0.95, segs=12, kind=FRAME)
        p.cyl((x, y, z - length + 0.01), (x, y, z - length - 0.02), r * 0.52, segs=12, kind=RECESS, bevel=0)
    else:
        p.cyl((x, y, z), (x, y, z - length * 0.85), r, segs=12, kind=FRAME)
        p.cyl((x, y, z - length * 0.1), (x, y, z - length * 0.38), r * 1.35, segs=12, kind=FRAME)
        b = r * 2.5
        # (the muzzle block stands a little taller than the barrel, so their tops are not level)
        p.box((x, y, z - length * 0.9), (b, b * 0.84, length * 0.26), FRAME)
        p.cyl((x, y, z - length * 1.02), (x, y, z - length * 1.045), r * 0.7, segs=12, kind=RECESS, bevel=0)
        for s in (-1, 1):
            p.slats((x + s * b * 0.505, y, z - length * 0.9), (s, 0, 0), (0, 1, 0), (b * 0.45, length * 0.2), 3, 0.02)


def cockpit(model):
    """The inside of the cab, as seen in first-person view. Axes of the "Cockpit" node: the pilot's eye
    is the origin, looking along -Z. The instrument panel is on the "Dash" node, tilted towards the
    eye; the three screens on it are made by CockpitDisplays, which is put on the Cockpit node."""
    C = "Hips/Torso/CockpitCam/Cockpit"
    a = math.radians(-28)
    model.frames[C + "/Dash"] = ([[1, 0, 0], [0, math.cos(a), -math.sin(a)], [0, math.sin(a), math.cos(a)]], [0, -0.43, -0.98])
    model.scripts[C] = "res://scripts/UI/CockpitDisplays.cs"

    d = Part("CockpitDash")
    d.box((0, 0, -0.06), (1.86, 0.64, 0.1), FRAME, 0.02)
    for cx in (-0.56, 0, 0.56):
        for sx, sy, w, h in ((0, 1, 0.52, 0.035), (0, -1, 0.52, 0.035), (1, 0, 0.035, 0.46), (-1, 0, 0.035, 0.46)):
            d.box((cx + sx * 0.2475, 0.02 + sy * 0.2475, 0.008), (w, h, 0.04), PAINT, 0.008)  # bezel
        for i in range(5):
            d.box((cx - 0.18 + i * 0.09, -0.275, 0.0), (0.05, 0.028, 0.02), SOFT, 0.004)  # soft keys
    for sx in (-1, 1):
        d.cyl((sx * 0.87, 0.2, -0.01), (sx * 0.87, 0.2, 0.035), 0.03, segs=10, kind=SOFT)
        d.cyl((sx * 0.87, 0.08, -0.01), (sx * 0.87, 0.08, 0.035), 0.03, segs=10, kind=SOFT)
        for i in range(4):
            d.box((sx * 0.87, -0.05 - i * 0.05, 0.0), (0.035, 0.022, 0.03), EDGE, 0.003)  # toggle switches
    d.box((0, 0.37, 0.07), (1.96, 0.05, 0.34), FRAME, 0.015)  # glare shield
    d.stations("y", [(-0.34, 0.5, 0.2, 0, 0.02, 0.04), (-0.7, 0.4, 0.4, 0, 0.12, 0.05)], kind=FRAME, seams=dict(margin=0.03, width=0.008, depth=0.008, min_area=0.03))  # pedestal
    model.add(d, C + "/Dash", "Console", PANEL)

    f = Part("CockpitFrame")
    for s in (-1, 1):
        # canopy bows and rails
        f.loft([[(s * x + u, y + v, z) for u, v in ((-0.04, -0.03), (0.04, -0.03), (0.04, 0.03), (-0.04, 0.03))] for x, y, z in ((1.0, -0.3, -1.25), (0.82, 0.62, -0.85), (0.7, 0.98, -0.3))], kind=FRAME, bevel=0.008)
        f.box((s * 1.0, -0.32, -0.29), (0.07, 0.07, 1.88), FRAME, 0.01)
        f.box((s * 0.7, 0.98, 0.35), (0.07, 0.06, 1.3), FRAME, 0.01)
        # side consoles with rows of switches
        f.stations("z", [(-0.7, 0.36, 0.28, s * 0.9, -0.66, 0.05), (0.6, 0.36, 0.28, s * 0.9, -0.66, 0.05)], kind=FRAME, seams=dict(margin=0.03, width=0.008, depth=0.008, min_area=0.06))
        for i in range(6):
            f.box((s * 0.9 + 0.08, -0.515, -0.55 + i * 0.09), (0.035, 0.03, 0.022), EDGE, 0.003)
            f.box((s * 0.9 - 0.08, -0.515, -0.55 + i * 0.09), (0.05, 0.02, 0.03), SOFT, 0.003)
        f.box((s * 0.25, -0.98, -1.05), (0.16, 0.22, 0.05), FRAME, 0.01, None, Matrix.Rotation(math.radians(-25), 3, "X"))  # pedals
    f.box((0, 0.98, -0.3), (1.44, 0.05, 0.07), FRAME, 0.01)  # windscreen bow
    f.box((0, 0.95, 0.1), (0.5, 0.05, 0.5), FRAME, 0.01)  # overhead panel
    for i in range(5):
        f.box((-0.16 + i * 0.08, 0.915, 0.0), (0.03, 0.03, 0.02), EDGE, 0.003)
    f.box((0, -1.08, -0.3), (2.2, 0.06, 2.4), FRAME, 0)  # floor
    f.box((0, 0.2, 0.75), (2.1, 2.6, 0.06), FRAME, 0)  # bulkhead behind the seat
    # throttle on the left console, stick on the right
    f.box((-0.84, -0.44, -0.35), (0.12, 0.04, 0.3), FRAME, 0.01)
    f.cyl((-0.84, -0.44, -0.4), (-0.84, -0.26, -0.47), 0.018, segs=6, kind=EDGE, bevel=0)
    f.box((-0.84, -0.24, -0.48), (0.09, 0.07, 0.06), SOFT, 0.012)
    f.cyl((0.84, -0.46, -0.45), (0.84, -0.2, -0.42), 0.022, segs=6, kind=EDGE, bevel=0)
    f.stations("y", [(-0.2, 0.06, 0.07, 0.84, -0.42, 0.015), (-0.04, 0.07, 0.08, 0.84, -0.43, 0.02)], kind=SOFT, bevel=0.01)
    model.add(f, C, "Frame", PANEL)
