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
    j.cyl((-half, 0, 0), (half, 0, 0), r, segs=20, kind=FRAME, hub=False)   # (its own caps and bolts below)
    for s in (-1, 1):
        j.cyl((s * half, 0, 0), (s * (half + 0.06), 0, 0), r * 0.62, segs=14, kind=SOFT)
        j.bolts([(s * half, math.cos(i * math.tau / 6) * r * 0.8, math.sin(i * math.tau / 6) * r * 0.8) for i in range(6)], (s, 0, 0), r=r * 0.07, h=0.04)
    j.cyl((-half * 0.32, 0, 0), (half * 0.32, 0, 0), r * 1.08, segs=18, kind=SOFT, hub=False)
    return j


def legs(model, thigh, shin, meta, foot, joints, spur=True, foot_shift=0.0, foot_style=None, thigh_armor=None, shin_armor=None):
    """Both legs. thigh / shin / meta: (length, width, depth) of the bone as in the scene;
    foot: (width, height, length) of the sole; joints: radii of hip, knee and heel.
    Bone axes: +Y towards the foot, +Z the front of the limb; left leg, outside = -X.
    foot_style "cross": four toes — fore, aft and one to each side — as on the Agni;
               "box": a flat oblong sole, square at both ends, as on the Executioner;
               "talon": a broad deck on three splayed front toes and a heel claw (an assault machine's);
               "boot": a massive square-toed sole with a split toe cap and a raised ankle collar.
    thigh_armor / shin_armor: a machine's own armour for the thigh and the shin, drawn into the part given
    at the standard size (thigh 2.7 long, 0.95 wide, 1.15 deep; shin 2.9 x 0.75 x 0.85), in place of the
    standard plates; it is then scaled to the bone like them."""
    man = meta[0] <= 0.9
    # Thigh ------------------------------------------------------------------------------------------
    t = Part("ThighL_Armor")
    if thigh_armor:
        thigh_armor(t)
    else:
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
    # a hinge drum across the top, round the pelvis's hip axle (a flat block there lay flat against one
    # face or another of the axle, depending on how the leg stands)
    f.cyl((-0.28, -1.36, 0), (0.28, -1.36, 0), 0.38, segs=16, kind=FRAME)
    f.box((0, 1.28, 0), (0.46, 0.3, 0.6), FRAME)
    model.both(f.scale(k), "LegL/Thigh/Hit", "Frame", JOINT)

    # Shin: the heaviest part of the leg — a boxed greave over a calf full of muscle ---------------------
    s_ = Part("ShinL_Armor")
    if shin_armor:
        shin_armor(s_)
    else:
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
    elif foot_style in ("talon", "boot"):
        p = Part("FootL_Plate")
        toes = Part("FootL_Toes")
        if foot_style == "talon":
            # a broad deck over the ankle, and three toes splayed out in front of it, a claw behind
            p.stations("z", [(0.7, 1.0, 0.3, 0, G + 0.3, 0.06), (-0.3, 1.24, 0.4, 0, G + 0.35, 0.08), (-0.75, 1.1, 0.32, 0, G + 0.3, 0.06)], seams=SMALL_PANELS)
            for ang in (-0.32, 0.0, 0.32):
                rot = Matrix.Rotation(ang, 3, "Y")
                secs = []
                for z, w, h in ((-1.85, 0.3, 0.12), (-1.45, 0.42, 0.26), (-0.85, 0.46, 0.34), (-0.55, 0.42, 0.34)):
                    secs.append([tuple(rot @ Vector((u, G + h / 2 + v, z))) for u, v in octagon(w, h, 0.05)])
                p.loft(secs, PAINT, 0.02, SMALL_PANELS)
                a_, b_ = rot @ Vector((-0.28, G + 0.22, -0.82)), rot @ Vector((0.28, G + 0.22, -0.82))
                toes.cyl(tuple(a_), tuple(b_), 0.19, segs=10, kind=SOFT)                     # knuckles, standing proud of the toe
            toes.stations("z", [(0.6, 0.6, 0.3, 0, G + 0.15, 0.06), (1.2, 0.44, 0.2, 0, G + 0.1, 0.05), (1.5, 0.24, 0.1, 0, G + 0.05, 0.03)], kind=FRAME)   # heel claw
            reach_toe, reach_heel = 1.85, 1.5
        else:
            # a square-toed sole, its toe cap split in two, a heel block and a raised collar round the ankle
            p.stations("z", [(1.15, 1.32, 0.34, 0, G + 0.17, 0.05), (-1.2, 1.4, 0.36, 0, G + 0.18, 0.05)], seams=SMALL_PANELS)
            p.stations("y", [(G + 0.3, 1.0, 1.1, 0, 0.0, 0.12), (G + 0.62, 0.84, 0.92, 0, 0.0, 0.1)], seams=SMALL_PANELS)     # ankle collar
            for sx in (-1, 1):
                p.stations("z", [(-1.15, 0.6, 0.36, sx * 0.36, G + 0.2, 0.06), (-1.8, 0.56, 0.24, sx * 0.36, G + 0.13, 0.05)], seams=SMALL_PANELS)  # toe caps
            toes.box((0, G + 0.2, 1.2), (1.2, 0.3, 0.14), FRAME, 0.02)                                                          # heel block
            reach_toe, reach_heel = 1.8, 1.25
        toes.cyl((-0.5, 0.2, 0.0), (0.5, 0.2, 0.0), 0.27, segs=14, kind=FRAME)  # ankle
        model.both(p.scale(kf).move((0, 0, foot_shift)), "LegL/Foot", "Plate", CAMO)
        model.both(toes.scale(kf).move((0, 0, foot_shift)), "LegL/Foot", "Toes", JOINT)
        print_ = p.sole("Footprint")
        print_.merge(toes.sole("t"))
        model.removes += ["LegL/Foot/Spur", "LegR/Foot/Spur"]
        model.props["LegL"] = model.props["LegR"] = {"ToeReach": (reach_toe * kf[2] - foot_shift) * 0.92, "HeelReach": (reach_heel * kf[2] + foot_shift) * 0.92}
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
            # (their outer sides 2.5 cm in from the sole's, which they would otherwise lie flat against as they bend)
            toes.stations("z", [(-1.72, 0.3, 0.14, s * 0.435, G + 0.07, 0.03), (-1.3, 0.46, 0.26, s * 0.395, G + 0.13, 0.06), (-0.7, 0.5, 0.34, s * 0.335, G + 0.17, 0.07)], kind=FRAME)
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

    # What the foot leaves on the ground: the outline of its sole (tools/footprint_textures.gd draws it).
    model.loose.append(print_)

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
        p.cyl((s * 0.55, -0.2, 0), (s * (1.12 + spread), -0.2, 0), 0.5, segs=16, kind=FRAME, hub=False)   # (into the hip joint's drum)
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


# The controls of the cab the pilot can work with the mouse (see CockpitControls): node name → where it
# stands in the Cockpit node's axes. "Sw_" are toggle switches (their lever flips), "Btn_" push buttons;
# the rest of the name is the game's input action they press.
# Soft keys: a row of up to five under each of the three panel screens, in the "Dash" node's axes; what each
# does is written on its screen just above it (CockpitDisplays), lit while it is on.
# Grouped by the screen they belong with: power and heat under the damage-and-heat screen, the sensors
# under the radar, the walk under the weapons and throttle; the targeting keys under the target pod, the
# cab's own lamp and heater under the other pod.
CAB_SOFTKEYS = [["Btn_reactor", "Btn_engine", "Btn_coolant", "Btn_full_stop"],
                ["Btn_radar_mode", "Btn_toggle_lights", "Btn_night_vision", "Btn_thermal_vision"],
                ["Btn_counter_rotate", "Btn_kneel"]]
CAB_POD_KEYS = {"PodL": ["Btn_target_nearest", "Btn_target_next", "Btn_target_friendly"],
                "PodR": ["Btn_squad_attack", "Btn_squad_follow", "Btn_squad_goto", "Btn_squad_hold", "Btn_squad_fire"]}
# The cab's own lamp and the glass heater: toggle switches of their own on the glare shield, either side.
CAB_SHIELD_SWITCHES = [("Sw_cab_light", -0.68), ("Sw_glass_heat", 0.68)]
# The pods: up on stalks at the front of the consoles, at the height of the panel and turned square to the
# eye, just outside the panel's screens (centre, which way the screen faces).
CAB_PODS = {"PodL": ((-0.72, -0.17, -0.64), (0.72, 0.17, 0.64)), "PodR": ((0.72, -0.17, -0.64), (-0.72, 0.17, 0.64))}
POD_FACE = (0.2, -0.215, 0.14)       # the housing's half width, its bottom and top (round the screen and its keys)
CAB_STICK, CAB_THROTTLE = (0.72, -0.49, -0.16), (-0.7, -0.49, -0.22)
CAB_PEDALS = {"PedalL": (-0.22, -0.9, -0.92), "PedalR": (0.22, -0.9, -0.92)}
SEAT, GRIP = "cockpit_seat", "cockpit_grip"
# The combiner glass of the head-up display, hung from the roof: its corners (bottom left, bottom right,
# top right, top left), leaning back a little at the top. The sight, at eye level, lies across its lower part.
COMBINER = ((-0.31, -0.05, -0.64), (0.31, -0.05, -0.64), (0.31, 0.26, -0.6), (-0.31, 0.26, -0.6))
# The compass's own strip of a screen, hung between the combiner's struts just over the glass, turned to
# the eye: its centre, the way it faces, its width and height.
COMPASS = ((0.0, 0.32, -0.61), (0.0, -0.45, 0.89), 0.3, 0.06)


def softkey_x(i, n, run=0.4):
    """Where the i-th of n keys under a panel screen sits across it (spread wider the fewer there are)."""
    pitch = min(0.17, run / n)
    return (i - (n - 1) / 2) * pitch


def cockpit(model):
    """The inside of the cab, as seen in first-person view: a closed armoured tub with a windscreen and two
    side windows, an instrument panel, side consoles with the throttle and the side-stick, soft keys under
    the panel's screens, the head-up display on the windscreen, an ejection seat and the pedals. Axes of the "Cockpit" node: the
    pilot's eye is the origin, looking along -Z. The instrument panel is on the "Dash" node, tilted
    towards the eye; the screens on it and on the two console pods are made by CockpitDisplays, the
    controls are moved and worked by CockpitControls. Everything of the machine itself that pokes into
    the tub is cut away while the pilot sits in it (Mech.DrawOver), so only the windows look out."""
    C = "Hips/Torso/CockpitCam/Cockpit"
    a = math.radians(-28)
    model.frames[C + "/Dash"] = ([[1, 0, 0], [0, math.cos(a), -math.sin(a)], [0, math.sin(a), math.cos(a)]], [0, -0.43, -0.98])
    model.scripts[C] = "res://scripts/UI/CockpitDisplays.cs"
    model.frames[C] = ([[1, 0, 0], [0, 1, 0], [0, 0, 1]], [0, 0, 0])      # the eye is the origin
    fine = dict(margin=0.03, width=0.007, depth=0.007, min_area=0.03)
    up, fwd = (0, 1, 0), (0, 0, -1)

    # ── The instrument panel ──
    d = Part("CockpitDash")
    d.stations("z", [(-0.12, 1.84, 0.62, 0, 0.0, 0.05), (-0.01, 1.86, 0.64, 0, 0.0, 0.05)], kind=FRAME, seams=fine)
    for cx in (-0.56, 0, 0.56):
        bezel(d, (cx, 0.02, -0.01), (0, 0, 1), up, 0.46, 0.46, 0.032, 0.026)
        row = CAB_SOFTKEYS[round(cx / 0.56) + 1]
        for i in range(len(row)):
            keycap(d, (cx + softkey_x(i, len(row)), -0.268, -0.01), (0, 0, 1), up, 0.09, 0.044, 0.006, FRAME)   # the keys' collars
    # (from under the panel, whose lower edge is at y = -0.32, down to the floor)
    d.stations("y", [(-0.28, 0.5, 0.2, 0, -0.04, 0.04), (-0.7, 0.42, 0.42, 0, 0.08, 0.05), (-0.9, 0.44, 0.46, 0, 0.1, 0.05)], kind=FRAME, seams=fine)   # pedestal
    model.add(d, C + "/Dash", "Console", PANEL)
    for column, row in enumerate(CAB_SOFTKEYS):
        for i, name in enumerate(row):
            b_ = Part("Cab_" + name)
            keycap(b_, (0, 0, -0.002), (0, 0, 1), up, 0.074, 0.032, 0.016, SOFT)
            red = name in ("Btn_full_stop", "Btn_reactor")                     # (the ones that stop it all: red)
            x = softkey_x(i, len(row))
            model.add(b_, C + "/Dash", name, "cockpit_red" if red else GRIP, at=((column - 1) * 0.56 + x, -0.268, -0.006))
            # (which screen names it, and where along that screen's bottom the key is)
            model.props[C + "/Dash/" + name] = {"metadata/push": (0, 0, 1), "metadata/softkey": True,
                                                "metadata/screen": "Screen" + "LCR"[column], "metadata/at": 0.5 + x / 0.46}

    # ── The tub ──
    f = Part("CockpitFrame")
    f.stations("z", [(-1.24, 2.04, 0.1, 0, -1.1, 0.01), (0.84, 2.04, 0.1, 0, -1.1, 0.01)], kind=FRAME, seams=PANELS)          # floor
    f.slats((0, -1.05, -0.45), up, (1, 0, 0), (0.7, 0.6), 7, 0.012)                                                        # tread plate
    f.stations("z", [(-0.92, 2.04, 0.1, 0, 0.95, 0.01), (0.84, 2.04, 0.1, 0, 0.95, 0.01)], kind=FRAME, seams=PANELS)        # roof
    f.stations("z", [(0.74, 2.04, 2.2, 0, -0.08, 0.03), (0.86, 2.04, 2.2, 0, -0.08, 0.03)], kind=FRAME, seams=PANELS)       # bulkhead behind the seat
    for sx in (-1, 1):
        f.stations("z", [(-1.22, 0.1, 1.06, sx * 1.0, -0.62, 0.02), (0.82, 0.1, 1.06, sx * 1.0, -0.62, 0.02)], kind=FRAME, seams=PANELS)   # side wall, low
        f.stations("z", [(-0.34, 0.1, 1.1, sx * 1.0, 0.42, 0.02), (0.82, 0.1, 1.1, sx * 1.0, 0.42, 0.02)], kind=FRAME, seams=PANELS)     # side wall behind the window
        f.stations("z", [(-1.04, 0.1, 0.4, sx * 1.0, 0.76, 0.02), (-0.3, 0.1, 0.4, sx * 1.0, 0.76, 0.02)], kind=FRAME)                  # over the window
        f.stations("y", [(-0.2, 0.22, 0.12, sx * 0.91, -1.13, 0.02), (0.95, 0.22, 0.12, sx * 0.91, -0.83, 0.02)], kind=FRAME)           # windscreen pillar
        # side console, with its pod (a screen turned to the pilot) at the front
        f.stations("z", [(-0.76, 0.38, 0.62, sx * 0.8, -0.8, 0.045), (0.44, 0.38, 0.62, sx * 0.8, -0.8, 0.045)], kind=FRAME, seams=fine)
        name = "PodL" if sx < 0 else "PodR"
        centre, normal = CAB_PODS[name]
        s_, u_, n_ = frame_at(normal, up)
        hw, lo, hi = POD_FACE
        face = [Vector(centre) + s_ * a_ + u_ * b_ for a_, b_ in ((hw, lo), (hw, hi), (-hw, hi), (-hw, lo))]
        back = [p_ - n_ * 0.07 for p_ in face]
        f.loft([[tuple(p_) for p_ in back], [tuple(p_) for p_ in face]], FRAME, 0.012)                                   # the housing
        foot = Vector((centre[0], -0.49, centre[2])) - n_ * 0.05
        f.stations("y", [(-0.49, 0.09, 0.09, foot.x, foot.z, 0.02), (centre[1] + lo + 0.02, 0.07, 0.07, foot.x, foot.z, 0.015)], kind=FRAME)   # its stalk
        bezel(f, tuple(Vector(centre) + n_ * 0.002), normal, up, 0.32, 0.22, 0.025, 0.02)
        keys = CAB_POD_KEYS[name]
        # (all under the screen, which is 0.3 wide, so each key has its name right over it)
        pitch_ = min(0.085, 0.27 / len(keys))
        for i, key in enumerate(keys):
            a_ = (i - (len(keys) - 1) / 2) * pitch_
            at_ = Vector(centre) + s_ * a_ + u_ * (lo + 0.04) + n_ * 0.002
            keycap(f, tuple(at_), normal, up, pitch_ * 0.86, 0.036, 0.005, FRAME)                                          # collar
            b_ = Part("Cab_" + key)
            keycap(b_, (0, 0, 0), normal, up, pitch_ * 0.68, 0.026, 0.014, SOFT)
            model.add(b_, C, key, GRIP, at=tuple(at_ + n_ * 0.003))
            model.props[C + "/" + key] = {"metadata/push": tuple(n_), "metadata/softkey": True, "metadata/screen": name, "metadata/at": 0.5 + a_ / 0.3}
    for sx in (-1, 1):
        for z in (-0.24, 0.22):                                                                                              # frames of the side walls
            f.stations("y", [(-1.07, 0.06, 0.07, sx * 0.94, z, 0.015), (0.92, 0.06, 0.07, sx * 0.94, z, 0.015)], kind=FRAME)
        mt_pipe(f, [(sx * 0.93, 0.7, 0.72), (sx * 0.93, 0.7, 0.3), (sx * 0.93, 0.68, -0.25), (sx * 0.93, 0.6, -0.34)], 0.022, SOFT, 8)     # cable runs
        mt_pipe(f, [(sx * 0.925, 0.64, 0.72), (sx * 0.925, 0.64, -0.3)], 0.014, EDGE, 8)
        mt_pipe(f, [(sx * 0.62, 0.89, -0.62), (sx * 0.62, 0.8, -0.58), (sx * 0.62, 0.8, -0.38), (sx * 0.62, 0.89, -0.34)], 0.014, EDGE, 8)  # grab handles in the roof
    for x in (-0.5, 0.5):                                                                                                    # frames of the bulkhead
        f.stations("y", [(-1.07, 0.07, 0.06, x, 0.72, 0.015), (0.92, 0.07, 0.06, x, 0.72, 0.015)], kind=FRAME)
    f.stations("x", [(-1.02, 0.26, 0.36, -0.87, 0.77, 0.03), (1.02, 0.26, 0.36, -0.87, 0.77, 0.03)], kind=FRAME, seams=fine)      # header over the windscreen
    f.stations("x", [(-0.9, 0.24, 0.08, -1.1, -0.15, 0.02), (0.9, 0.24, 0.08, -1.1, -0.15, 0.02)], kind=FRAME)                    # glare shield
    f.stations("y", [(-1.12, 2.04, 0.1, 0, -1.245, 0.02), (-0.13, 2.04, 0.1, 0, -1.245, 0.02)], kind=FRAME, seams=PANELS)           # front wall, under the windscreen
    f.stations("z", [(-1.2, 1.2, 0.06, 0, -1.02, 0.01), (-0.82, 1.2, 0.06, 0, -1.02, 0.01)], kind=FRAME)                         # footwell floor plate
    f.stations("y", [(-1.0, 1.0, 0.05, 0, -1.19, 0.01), (-0.75, 0.9, 0.05, 0, -1.12, 0.01)], kind=FRAME)                          # kick plate under the panel
    f.stations("z", [(-0.62, 0.52, 0.1, 0, 0.87, 0.03), (0.3, 0.52, 0.1, 0, 0.87, 0.03)], kind=FRAME, seams=fine)                # overhead panel
    f.slats((0, 0.82, -0.16), (0, -1, 0), (1, 0, 0), (0.4, 0.8), 9, 0.01)
    # the equipment on the bulkhead: a life-support pack with its grille, an extinguisher in its bracket
    f.stations("y", [(-0.6, 0.5, 0.16, -0.6, 0.66, 0.03), (0.1, 0.5, 0.16, -0.6, 0.66, 0.03)], kind=FRAME, seams=fine)
    f.slats((-0.6, -0.25, 0.58), (0, 0, -1), (1, 0, 0), (0.36, 0.4), 6, 0.012)
    f.lathe((0.62, -0.72, 0.64), (0.62, -0.18, 0.64), [(0, 0.05), (0.06, 0.07), (0.85, 0.07), (0.93, 0.05), (1, 0.025)], segs=14, kind=PAINT)
    f.stations("y", [(-0.6, 0.18, 0.04, 0.62, 0.71, 0.01), (-0.54, 0.18, 0.04, 0.62, 0.71, 0.01)], kind=FRAME)
    # the throttle's quadrant: a slotted plate with its detents
    tx, ty, tz = CAB_THROTTLE
    for sx in (-1, 1):
        f.stations("z", [(tz - 0.22, 0.025, 0.03, tx + sx * 0.03, ty + 0.012, 0.006), (tz + 0.2, 0.025, 0.03, tx + sx * 0.03, ty + 0.012, 0.006)], kind=SOFT)
    for k in range(5):
        f.stations("x", [(tx - 0.05, 0.008, 0.01, tz - 0.18 + k * 0.09, ty + 0.03, 0.002), (tx - 0.044, 0.008, 0.01, tz - 0.18 + k * 0.09, ty + 0.03, 0.002)], kind=EDGE)
    # the cab lamp's and the glass heater's switches, on top of the glare shield, each with its lamp
    for name, x in CAB_SHIELD_SWITCHES:
        at = (x, -0.11, -1.04)
        switch_nut(f, at, up, fwd, r=0.014)
        lv = Part("Cab_" + name)
        lever_switch(lv, up, fwd, length=0.05)
        model.add(lv, C, name, GRIP, at=(at[0], at[1] + 0.006, at[2]))
        model.props[C + "/" + name] = {"metadata/axis": (1, 0, 0), "metadata/placard": "front"}
        lx = x + (0.05 if x < 0 else -0.05)
        switch_nut(f, (lx, at[1], at[2]), up, fwd, r=0.009)
        led = Part("Cab_Led_" + name[3:])
        led.lathe((0, -0.004, 0), (0, 0.016, 0), [(0, 0.0065), (0.4, 0.0065), (0.6, 0.006), (0.85, 0.0045), (1, 0.002)], segs=12, kind=PAINT)
        model.add(led, C, "Led_" + name[3:], "cockpit_led", at=(lx, at[1] + 0.007, at[2]))
    # the head-up display's combiner: a framed glass hung from the roof on two struts, in front of the eye
    cl, cr = Vector(COMBINER[0]), Vector(COMBINER[2])
    cw = (cr.x - cl.x) / 2
    ccen = (Vector(COMBINER[0]) + Vector(COMBINER[2])) / 2
    cup = (Vector(COMBINER[3]) - Vector(COMBINER[0])).normalized()
    cn = Vector((1, 0, 0)).cross(cup).normalized()
    if cn.z < 0:
        cn = -cn
    ch = (Vector(COMBINER[3]) - Vector(COMBINER[0])).length / 2
    bezel(f, tuple(ccen - cn * 0.004), tuple(cn), tuple(cup), cw * 2, ch * 2, 0.018, 0.016)
    for sx in (-1, 1):
        top = ccen + cup * (ch + 0.018) + Vector((sx * (cw - 0.04), 0, 0))
        f.stations("y", [(top.y - 0.005, 0.026, 0.03, top.x, top.z, 0.006), (0.905, 0.026, 0.03, top.x, top.z - 0.02, 0.006)], kind=FRAME)          # struts
        f.stations("y", [(0.86, 0.07, 0.06, top.x, top.z - 0.02, 0.012), (0.905, 0.07, 0.06, top.x, top.z - 0.02, 0.012)], kind=FRAME)           # their roof shoes
    # the compass's strip of a screen over the combiner, its housing fixed to the struts
    cc, cnorm, cw_, ch_ = COMPASS
    s_, u_, n_ = frame_at(cnorm, up)
    # a slim case just round the screen, on two thin brackets out to the struts
    m_ = 0.012
    face = [Vector(cc) + s_ * a_ + u_ * b_ for a_, b_ in ((cw_ / 2 + m_, -ch_ / 2 - m_), (cw_ / 2 + m_, ch_ / 2 + m_), (-cw_ / 2 - m_, ch_ / 2 + m_), (-cw_ / 2 - m_, -ch_ / 2 - m_))]
    f.loft([[tuple(p_ - n_ * 0.025) for p_ in face], [tuple(p_) for p_ in face]], FRAME, 0.005)
    strut_x = COMBINER[2][0] - 0.04
    for sx in (-1, 1):
        a0 = Vector(cc) - n_ * 0.012
        x0, x1 = cw_ / 2 + m_ - 0.01, strut_x + 0.006
        f.stations("x", [(sx * x0, 0.014, 0.012, a0.z, a0.y, 0.003), (sx * x1, 0.014, 0.012, a0.z, a0.y, 0.003)], kind=FRAME)
    # the side-stick's well
    sx_, sy_, sz_ = CAB_STICK
    f.lathe((sx_, sy_ - 0.01, sz_), (sx_, sy_ + 0.012, sz_), [(0, 0.075), (0.5, 0.075), (1, 0.065)], segs=18, kind=SOFT)
    # the consoles' tops, beside the stick and the throttle: cooling grilles
    for sx in (-1, 1):
        f.slats((sx * 0.9, -0.49, -0.14), up, (0, 0, 1), (0.1, 0.42), 7, 0.008)
    model.add(f, C, "Frame", PANEL)

    # Pods: empty nodes the console screens go on (their housings are part of the frame). The compass's
    # strip is put on the same way.
    for name, (centre, normal) in list(CAB_PODS.items()) + [("CompassMount", COMPASS[:2])]:
        pod = Part("Cab_" + name)
        pw, ph = (COMPASS[2], COMPASS[3]) if name == "CompassMount" else (0.3, 0.2)
        bezel(pod, (0, 0, 0), normal, up, pw, ph, 0.004, 0.004, FRAME)
        model.add(pod, C, name, PANEL, at=centre)
        model.props[C + "/" + name] = {"metadata/face": normal}

    # ── The seat ──
    st = Part("CockpitSeat")
    st.stations("z", [(-0.31, 0.42, 0.05, 0, -0.85, 0.022), (-0.29, 0.47, 0.09, 0, -0.84, 0.04), (-0.22, 0.5, 0.11, 0, -0.835, 0.05), (0.12, 0.5, 0.1, 0, -0.84, 0.045),
                      (0.2, 0.48, 0.09, 0, -0.845, 0.04), (0.24, 0.44, 0.06, 0, -0.85, 0.025)], kind=PAINT, seams=fine)                                                        # cushion
    st.stations("y", [(-0.84, 0.44, 0.06, 0, 0.32, 0.025), (-0.8, 0.48, 0.1, 0, 0.315, 0.045), (-0.62, 0.48, 0.11, 0, 0.32, 0.05), (-0.3, 0.46, 0.1, 0, 0.345, 0.045),
                      (-0.12, 0.42, 0.09, 0, 0.36, 0.04), (-0.08, 0.38, 0.06, 0, 0.365, 0.025)], kind=PAINT, seams=fine)                                                        # back
    for sx in (-1, 1):
        st.stations("y", [(-0.74, 0.04, 0.09, sx * 0.25, 0.3, 0.015), (-0.66, 0.05, 0.12, sx * 0.255, 0.295, 0.02), (-0.4, 0.05, 0.11, sx * 0.255, 0.31, 0.02), (-0.3, 0.035, 0.07, sx * 0.25, 0.325, 0.012)], kind=PAINT)   # bolsters
        st.stations("z", [(-0.25, 0.045, 0.05, sx * 0.27, -0.8, 0.015), (-0.2, 0.05, 0.07, sx * 0.27, -0.795, 0.02), (0.14, 0.05, 0.07, sx * 0.27, -0.795, 0.02), (0.2, 0.045, 0.05, sx * 0.27, -0.8, 0.015)], kind=PAINT)   # thigh rolls
    st.stations("y", [(-0.03, 0.24, 0.06, 0, 0.4, 0.025), (0.0, 0.29, 0.1, 0, 0.395, 0.045), (0.14, 0.29, 0.1, 0, 0.395, 0.045), (0.19, 0.25, 0.06, 0, 0.4, 0.025)], kind=PAINT, seams=fine)   # headrest
    model.add(st, C, "Seat", SEAT)
    sb = Part("CockpitSeatFrame")
    sb.stations("y", [(-0.9, 0.58, 0.07, 0, 0.42, 0.02), (-0.2, 0.56, 0.07, 0, 0.45, 0.02), (0.24, 0.38, 0.06, 0, 0.48, 0.02)], kind=FRAME)     # shell
    sb.stations("z", [(-0.32, 0.58, 0.05, 0, -0.915, 0.015), (0.4, 0.58, 0.05, 0, -0.915, 0.015)], kind=FRAME)                               # pan
    for sx in (-1, 1):
        sb.stations("y", [(-1.07, 0.05, 0.06, sx * 0.21, 0.52, 0.012), (0.3, 0.05, 0.06, sx * 0.21, 0.53, 0.012)], kind=SOFT)                 # ejection rails
        sb.stations("z", [(-0.3, 0.03, 0.18, sx * 0.3, -0.84, 0.008), (0.3, 0.03, 0.2, sx * 0.3, -0.84, 0.008)], kind=FRAME)                  # side plates
    sb.stations("y", [(-1.07, 0.42, 0.42, 0, 0.06, 0.06), (-0.93, 0.38, 0.38, 0, 0.06, 0.05)], kind=FRAME)                                  # pedestal
    mt_pipe(sb, [(-0.09, -0.9, -0.32), (-0.09, -0.86, -0.38), (-0.05, -0.83, -0.4), (0.05, -0.83, -0.4), (0.09, -0.86, -0.38), (0.09, -0.9, -0.32)], 0.012, EDGE, 8)   # ejection handle
    model.add(sb, C, "SeatFrame", PANEL)

    # ── The side-stick (right) and the throttle (left), each on a node at its pivot ──
    sk = Part("CockpitStick")
    sk.lathe((0, -0.005, 0), (0, 0.07, 0), [(0, 0.05), (0.18, 0.044), (0.3, 0.05), (0.45, 0.038), (0.58, 0.043), (0.72, 0.031), (0.84, 0.034), (1, 0.017)], segs=16, kind=SOFT)   # boot
    sk.lathe((0, 0.06, 0), (0, 0.14, 0), [(0, 0.016), (0.12, 0.016), (0.18, 0.011), (1, 0.011)], segs=10, kind=EDGE)
    sk.stations("y", [(0.13, 0.048, 0.054, 0, 0.0, 0.018), (0.17, 0.056, 0.062, 0, -0.004, 0.022), (0.22, 0.05, 0.058, 0, 0.0, 0.02), (0.265, 0.054, 0.066, 0, 0.008, 0.022), (0.29, 0.044, 0.054, 0, 0.01, 0.018)], kind=PAINT)
    sk.stations("y", [(0.185, 0.018, 0.02, 0, -0.038, 0.005), (0.225, 0.016, 0.018, 0, -0.042, 0.005)], kind=SOFT)                          # trigger
    model.add(sk, C, "Stick", GRIP, at=CAB_STICK)
    th = Part("CockpitThrottle")
    th.stations("y", [(0.0, 0.03, 0.036, 0, 0, 0.008), (0.17, 0.026, 0.03, 0, -0.03, 0.007)], kind=EDGE)
    th.stations("x", [(-0.022, 0.07, 0.05, -0.036, 0.18, 0.015), (0.0, 0.08, 0.06, -0.036, 0.186, 0.02), (0.06, 0.076, 0.056, -0.036, 0.186, 0.02), (0.078, 0.06, 0.044, -0.036, 0.18, 0.015)], kind=PAINT)
    model.add(th, C, "Throttle", GRIP, at=CAB_THROTTLE)

    # ── The pedals, each on a node at its footplate ──
    for name, at in CAB_PEDALS.items():
        pd = Part("Cab_" + name)
        n_ = Vector((0, 0.55, 0.84)).normalized()
        s_, u_, _ = frame_at(n_, up)
        secs = [[tuple(n_ * t + s_ * a_ * k + u_ * b_ * k) for a_, b_ in octagon(0.13, 0.22, 0.03)] for t, k in ((-0.02, 1.0), (0.004, 1.0), (0.012, 0.94))]
        pd.loft(secs, FRAME, 0.004)
        for r_ in range(4):
            keycap(pd, tuple(n_ * 0.01 + u_ * (-0.075 + r_ * 0.05)), n_, up, 0.1, 0.012, 0.008, SOFT)                                         # grip ribs
        pd.stations("y", [(-0.12, 0.05, 0.04, 0, 0.0, 0.01), (-0.02, 0.05, 0.04, 0, 0.04, 0.01)], kind=FRAME)                                 # its arm
        pd.stations("y", [(-0.17, 0.09, 0.1, 0, -0.01, 0.02), (-0.1, 0.08, 0.09, 0, -0.01, 0.02)], kind=SOFT)                                  # and pivot block
        model.add(pd, C, name, PANEL, at=at)

    # The head-up display is projected on the windscreen itself: a sheet just inside it (the picture is put
    # on by CockpitDisplays).
    cb = Part("CockpitCombiner")
    vs = [cb.bm.verts.new(gd(Vector(p_))) for p_ in COMBINER]
    cb.bm.faces.new(vs)[cb.kind] = PAINT
    model.add(cb, C, "HeadUp", "cockpit_glass")
    model.props[C + "/HeadUp"] = {"cast_shadow": 0}

    # The glass: windscreen and side windows, single sheets laid just outside their frames (CockpitGlass
    # cracks it, dirties it, puts the weather on it; materials/cockpit_glass.gdshader).
    g = Part("CockpitGlass")

    def sheet(*corners):
        vs = []
        for c in corners:
            vs.append(g.bm.verts.new(gd(Vector(c))))
        g.bm.faces.new(vs)[g.kind] = PAINT

    sheet((-0.85, -0.17, -1.2), (0.85, -0.17, -1.2), (0.85, 0.62, -0.91), (-0.85, 0.62, -0.91))
    for sx in (-1, 1):
        sheet((sx * 1.06, -0.13, -1.08), (sx * 1.06, -0.13, -0.32), (sx * 1.06, 0.58, -0.32), (sx * 1.06, 0.58, -0.92))
    model.add(g, C, "Glass", "cockpit_glass")
    model.scripts[C + "/Glass"] = "res://scripts/Fx/CockpitGlass.cs"
    model.props[C + "/Glass"] = {"cast_shadow": 0}
