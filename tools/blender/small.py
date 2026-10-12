"""The smaller things: the foot soldier, the power armour, the Cleaver's axe, missile, bomb, tumbleweed,
the oil rig.
    blender -b --python tools/blender/small.py -- <output dir> <name|all> [preview]
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(__file__))
from meshkit import *  # noqa: E402
from walker import PANELS, SMALL_PANELS, barrel, piston  # noqa: E402

ARGS = sys.argv[sys.argv.index("--") + 1:]
FINE = dict(margin=0.02, width=0.006, depth=0.006, min_area=0.02)


def limb(name, rows, kind=PAINT, seams=None, bevel=0.012):
    p = Part(name)
    p.stations("y", rows, kind=kind, seams=seams, bevel=bevel)
    return p


def soldier():
    m = Model("soldier", "scenes/infantry/soldier.tscn")
    U, SKIN = "soldier_uniform", "soldier_skin"
    t = Part("Torso")
    t.stations("y", [(0.86, 0.34, 0.2, 0, 0, 0.05), (1.08, 0.33, 0.2, 0, 0, 0.05), (1.36, 0.44, 0.24, 0, 0, 0.07), (1.47, 0.3, 0.2, 0, 0, 0.06)], bevel=0.01)
    t.box((0, 1.2, -0.12), (0.34, 0.34, 0.06), PAINT, 0.012, FINE)  # plate carrier
    for i in range(3):
        t.box((-0.11 + i * 0.11, 1.03, -0.14), (0.09, 0.14, 0.06), PAINT, 0.01)  # magazine pouches
    t.box((0, 1.2, 0.17), (0.32, 0.42, 0.14), PAINT, 0.02, FINE)  # pack
    t.cyl((0, 1.46, 0), (0, 1.54, 0), 0.06, segs=8, kind=PAINT, bevel=0)
    t.box((0, 0.9, 0), (0.37, 0.06, 0.23), FRAME, 0.008)  # belt
    m.add(t, "Body", "Torso", U)
    f = Part("Face")
    f.lathe((0, 1.48, 0), (0, 1.72, 0), [(0, 0.06), (0.25, 0.095), (0.6, 0.105), (0.9, 0.09), (1, 0.04)], segs=10)
    f.box((0, 1.59, -0.1), (0.03, 0.04, 0.03), PAINT, 0.006)
    m.add(f, "Body", "Face", SKIN)
    h = Part("Helmet")
    h.lathe((0, 1.63, 0.01), (0, 1.8, 0.01), [(0, 0.125), (0.1, 0.13), (0.6, 0.12), (0.9, 0.07), (1, 0.01)], segs=12)
    h.box((0, 1.66, -0.13), (0.16, 0.05, 0.03), FRAME, 0.006)  # goggles
    m.add(h, "Body", "Helmet", JOINT)
    leg = limb("LegL", [(0.0, 0.17, 0.2, 0, 0, 0.05), (-0.4, 0.14, 0.16, 0, 0, 0.04), (-0.44, 0.12, 0.14, 0, 0.01, 0.03), (-0.78, 0.11, 0.12, 0, 0.01, 0.03)])
    leg.box((0, -0.81, -0.04), (0.11, 0.09, 0.26), FRAME, 0.02)  # boot
    leg.box((0, -0.43, -0.07), (0.1, 0.1, 0.04), FRAME, 0.01)  # knee pad
    m.both(leg, "Body/LegL", "Mesh", U)
    arm = limb("ArmL", [(0.04, 0.13, 0.14, 0, 0, 0.04), (-0.28, 0.1, 0.11, 0, 0, 0.03), (-0.54, 0.08, 0.09, 0, 0, 0.025)])
    arm.box((0, -0.6, 0), (0.07, 0.1, 0.09), FRAME, 0.02)  # glove
    m.both(arm, "Body/ArmL", "Mesh", U)
    g = Part("Rifle")
    g.box((0, 0, 0.12), (0.045, 0.12, 0.3), FRAME, 0.008)  # stock
    g.box((0, 0.01, -0.1), (0.05, 0.09, 0.3), FRAME, 0.008)  # receiver
    g.box((0, -0.11, -0.08), (0.035, 0.16, 0.06), FRAME, 0.006, None, Matrix.Rotation(0.25, 3, "X"))  # magazine
    g.box((0, -0.08, 0.05), (0.035, 0.1, 0.05), FRAME, 0.006)  # grip
    g.cyl((0, 0.025, -0.2), (0, 0.025, -0.62), 0.014, segs=6, kind=FRAME, bevel=0)
    g.box((0, 0.02, -0.34), (0.05, 0.06, 0.22), FRAME, 0.006)  # handguard
    g.box((0, 0.085, -0.1), (0.03, 0.04, 0.14), FRAME, 0.005)  # sight
    m.add(g, "Head", "Gun", JOINT, at=(0.25, -0.24, -0.45))
    return m


def power_armor():
    m = Model("power_armor", "scenes/infantry/power_armor.tscn")
    C = "camo_forest"
    t = Part("Torso")
    t.stations("y", [(1.22, 0.95, 0.75, 0, 0, 0.15), (1.7, 1.2, 0.9, 0, 0, 0.2), (2.3, 1.5, 0.98, 0, -0.02, 0.22), (2.55, 1.1, 0.8, 0, 0, 0.18)], seams=SMALL_PANELS, cuts=[((0, 1.8, 0), (0, 1, 0))])
    t.plate(None, (0, 2.05, -0.5), (0, 0.1, -1), (0.8, 0.5), 0.06)
    t.slats((0.3, 1.5, -0.44), (0, 0, -1), (1, 0, 0), (0.3, 0.26), 4, 0.03)
    m.add(t, "Body", "Torso", C)
    s = Part("Shoulders")
    for sx in (-1, 1):
        s.stations("x", [(sx * 0.62, 0.8, 0.4, 0, 2.45, 0.12), (sx * 1.02, 0.95, 0.56, 0, 2.44, 0.18), (sx * 1.3, 0.7, 0.36, 0, 2.36, 0.12)], seams=SMALL_PANELS)
    m.add(s, "Body", "Shoulders", C)
    d = Part("Dome")
    d.lathe((0, 2.5, -0.08), (0, 3.02, -0.08), [(0, 0.3), (0.3, 0.33), (0.75, 0.28), (1, 0.08)], segs=12, kind=FRAME)
    d.cyl((0, 2.42, -0.08), (0, 2.52, -0.08), 0.36, segs=12, kind=SOFT)
    m.add(d, "Body", "Dome", JOINT)
    v = Part("Visor")
    v.stations("z", [(-0.44, 0.34, 0.1, 0, 2.79, 0.03), (-0.36, 0.5, 0.16, 0, 2.79, 0.05), (-0.26, 0.56, 0.16, 0, 2.79, 0.05)], bevel=0.006)
    m.add(v, "Body", "Visor", GLASS)
    p = Part("MissilePack")
    p.stations("z", [(0.42, 1.0, 0.9, 0, 2.38, 0.12), (0.98, 0.95, 0.86, 0, 2.4, 0.14)], kind=FRAME, seams=SMALL_PANELS)
    for sx in (-1, 1):
        p.cyl((sx * 0.27, 2.86, 0.7), (sx * 0.27, 3.05, 0.6), 0.16, segs=10, kind=FRAME)
        p.cyl((sx * 0.27, 3.04, 0.605), (sx * 0.27, 3.06, 0.595), 0.11, segs=10, kind=RECESS, bevel=0)
        p.cyl((sx * 0.3, 1.9, 0.92), (sx * 0.3, 1.6, 0.98), 0.14, 0.18, segs=10, kind=FRAME)  # jump jets
    m.add(p, "Body", "MissilePack", JOINT)
    leg = limb("LegL", [(0.06, 0.56, 0.64, 0, 0, 0.14), (-0.5, 0.46, 0.52, 0, 0, 0.12), (-0.58, 0.4, 0.44, 0, 0.02, 0.1), (-1.08, 0.42, 0.5, 0, 0.02, 0.1)], seams=SMALL_PANELS)
    leg.stations("z", [(0.24, 0.4, 0.18, 0, -1.18, 0.05), (-0.2, 0.46, 0.22, 0, -1.16, 0.06), (-0.42, 0.36, 0.14, 0, -1.2, 0.04)], kind=PAINT)  # boot
    leg.plate(None, (0, -0.54, -0.28), (0, 0, -1), (0.3, 0.3), 0.06)  # knee
    m.both(leg, "Body/LegL", "Mesh", C)
    arm = limb("ArmL", [(0.06, 0.46, 0.5, 0, 0, 0.12), (-0.5, 0.4, 0.44, 0, 0, 0.1), (-0.58, 0.36, 0.4, 0, -0.04, 0.09), (-0.95, 0.42, 0.46, 0, -0.1, 0.1)], seams=SMALL_PANELS)
    m.both(arm, "Body/ArmL", "Mesh", C)
    g = Part("GauntletL")
    g.stations("z", [(0.12, 0.5, 0.44, 0, -1.16, 0.1), (-0.5, 0.52, 0.46, 0, -1.16, 0.1)], kind=FRAME, seams=SMALL_PANELS)
    barrel(g, (0.07, -1.1, -0.5), 0.5, 0.07, "laser")
    g.cyl((-0.1, -1.24, -0.5), (-0.1, -1.24, -0.82), 0.035, segs=6, kind=FRAME, bevel=0)
    m.both(g, "Body/ArmL", "Gauntlet", JOINT)
    return m


def hatchet():
    m = Model("hatchet", "scenes/weapons/hatchet.tscn")
    f = Part("Grip")
    f.stations("z", [(0.35, 0.3, 0.3, 0, 0, 0.06), (-0.3, 0.3, 0.3, 0, 0, 0.06)], kind=FRAME)
    m.add(f, ".", "Fist", JOINT)
    h = Part("Haft")
    h.cyl((0, 0, 0.3), (0, 0, -4.25), 0.12, segs=10, kind=FRAME)
    for z in (-0.6, -1.0, -1.4):
        h.cyl((0, 0, z), (0, 0, z - 0.1), 0.15, segs=10, kind=SOFT, bevel=0.008)
    h.stations("z", [(-3.2, 0.3, 0.6, 0, -0.05, 0.08), (-4.2, 0.3, 0.7, 0, -0.05, 0.08)], kind=FRAME, seams=SMALL_PANELS)  # head socket
    m.add(h, ".", "Handle", JOINT)
    b = Part("Blade")
    # the bit: thick at the socket, drawn out to a long curved edge
    edge = [(-3.05, -0.4), (-2.85, -1.2), (-3.2, -1.75), (-3.8, -1.95), (-4.4, -1.72), (-4.7, -1.2), (-4.5, -0.4)]
    back = [(-3.25, -0.35), (-3.3, -0.36), (-3.5, -0.37), (-3.75, -0.38), (-4.0, -0.37), (-4.2, -0.36), (-4.3, -0.35)]
    b.loft([[(x, y, z) for z, y in pts] for x, pts in ((-0.1, back), (-0.012, edge), (0.012, edge), (0.1, back))], bevel=0.0)
    m.add(b, ".", "Blade", "street_steel")
    s = Part("Spike")
    s.stations("y", [(0.25, 0.22, 0.5, 0, -3.7, 0.05), (0.75, 0.1, 0.24, 0, -3.75, 0.03), (1.15, 0.02, 0.04, 0, -3.8, 0.005)], kind=FRAME)
    m.add(s, ".", "Spike", JOINT)
    return m


def missile():
    m = Model("missile", "scenes/fx/missile.tscn")
    b = Part("Body")
    b.lathe((0, 0, -0.75), (0, 0, 0.65), [(0, 0.005), (0.08, 0.06), (0.2, 0.105), (0.3, 0.11), (0.92, 0.11), (1.0, 0.07)], segs=12)
    for i in range(4):
        r = Matrix.Rotation(i * math.pi / 2 + math.pi / 4, 3, "Z")
        b.loft([[tuple(r @ Vector((x, y, z))) for z, y in [(0.25, 0.1), (0.62, 0.1), (0.62, 0.3), (0.45, 0.3)]] for x in (-0.012, 0.012)], bevel=0)
        b.loft([[tuple(r @ Vector((x, y, z))) for z, y in [(-0.3, 0.1), (-0.12, 0.1), (-0.14, 0.2), (-0.24, 0.2)]] for x in (-0.01, 0.01)], bevel=0)
    b.cyl((0, 0, 0.64), (0, 0, 0.66), 0.06, segs=10, kind=RECESS, bevel=0)
    m.add(b, ".", "Body", "missile_body")
    return m


def bomb():
    m = Model("bomb", "scenes/fx/bomb.tscn")
    b = Part("Body")
    b.lathe((0, 0, -0.95), (0, 0, 0.75), [(0, 0.01), (0.07, 0.16), (0.2, 0.27), (0.35, 0.29), (0.6, 0.28), (0.85, 0.16), (1.0, 0.1)], segs=14, kind=FRAME)
    for i in range(4):
        r = Matrix.Rotation(i * math.pi / 2 + math.pi / 4, 3, "Z")
        b.loft([[tuple(r @ Vector((x, y, z))) for z, y in [(0.45, 0.12), (1.1, 0.1), (1.1, 0.42), (0.75, 0.42)]] for x in (-0.015, 0.015)], kind=FRAME, bevel=0)
    b.cyl((0, 0, 0.9), (0, 0, 1.1), 0.43, segs=14, kind=FRAME, caps=False, bevel=0)
    for z in (-0.3, 0.1):
        b.cyl((0, 0, z), (0, 0, z + 0.05), 0.3, segs=14, kind=SOFT, bevel=0)
    m.add(b, ".", "Body", JOINT)
    m.removes += ["FinA", "FinB"]
    return m


def tumbleweed():
    m = Model("tumbleweed", "scenes/props/tumbleweed.tscn")
    rng = random.Random(4)
    t = Part("Twigs")
    for i in range(70):
        # a twig: part of a circle on a ball, wavering in and out
        axis = Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1))).normalized()
        side = axis.cross(Vector((0, 1, 0)))
        if side.length < 0.1:
            side = axis.cross(Vector((1, 0, 0)))
        side.normalize()
        up = axis.cross(side)
        r = rng.uniform(0.45, 0.9)
        a0 = rng.uniform(0, math.tau)
        span = rng.uniform(1.2, 3.0)
        segs = 7
        pts = []
        for k in range(segs + 1):
            a = a0 + span * k / segs
            rr = r * (1 + 0.12 * math.sin(k * 2.1 + i))
            pts.append(side * math.cos(a) * rr + up * math.sin(a) * rr + axis * rng.uniform(-0.06, 0.06))
        w = rng.uniform(0.008, 0.02)
        for a, b in zip(pts, pts[1:]):
            t.cyl(tuple(a), tuple(b), w, segs=3, kind=PAINT, bevel=0, caps=False)
    m.add(t, ".", "Ring1", "tumbleweed")
    m.removes += ["Ring%d" % i for i in range(2, 7)]
    return m


def truss(p, a, b, w0, w1, bays, r=0.12, kind=PAINT):
    """A four-cornered lattice from a to b, w0 wide at a and w1 at b."""
    a, b = Vector(a), Vector(b)
    axis = (b - a).normalized()
    s = axis.cross(Vector((1, 0, 0)))
    if s.length < 0.1:
        s = axis.cross(Vector((0, 0, 1)))
    s.normalize()
    u = axis.cross(s)
    corner = lambda t, i: a + (b - a) * t + (s * (1 if i in (0, 1) else -1) + u * (1 if i in (0, 3) else -1)) * (w0 + (w1 - w0) * t) / 2
    for i in range(4):
        p.cyl(tuple(corner(0, i)), tuple(corner(1, i)), r, segs=5, kind=kind, bevel=0)
        for k in range(bays):
            t0, t1 = k / bays, (k + 1) / bays
            j = (i + 1) % 4
            p.cyl(tuple(corner(t0, i)), tuple(corner(t1, j)), r * 0.6, segs=4, kind=kind, bevel=0, caps=False)
            p.cyl(tuple(corner(t1, i)), tuple(corner(t1, j)), r * 0.6, segs=4, kind=kind, bevel=0, caps=False)


def oil_rig():
    m = Model("oil_rig", "scenes/props/oil_rig.tscn")
    legs = Part("Legs")
    for x, z in ((-9, -9), (9, -9), (-9, 9), (9, 9)):
        legs.cyl((x, -6, z), (x, 14, z), 1.1, segs=14, kind=PAINT)
        legs.cyl((x, 2.6, z), (x, 3.4, z), 1.35, segs=14, kind=SOFT)  # boat fender
    for y0, y1 in ((0.5, 7), (7, 13.5)):
        for (xa, za), (xb, zb) in (((-9, -9), (9, -9)), ((9, -9), (9, 9)), ((9, 9), (-9, 9)), ((-9, 9), (-9, -9))):
            legs.cyl((xa, y0, za), (xb, y1, zb), 0.28, segs=6, kind=PAINT, bevel=0)
            legs.cyl((xa, y1, za), (xb, y1, zb), 0.28, segs=6, kind=PAINT, bevel=0)
    m.add(legs, "Platform", "Leg1", "rig_steel")
    m.removes += ["Platform/Leg2", "Platform/Leg3", "Platform/Leg4"]

    deck = Part("Deck")
    deck.stations("y", [(14.0, 23.0, 23.0, 0, 0, 0.6), (14.6, 24.0, 24.0, 0, 0, 0.6), (16.0, 24.0, 24.0, 0, 0, 0.3)], seams=dict(margin=0.3, width=0.04, depth=0.04, min_area=8.0),
                  cuts=[((0, 0, 0), (1, 0, 0)), ((0, 0, 0), (0, 0, 1))])
    for i in range(-2, 3):
        deck.box((i * 5.0, 13.6, 0), (0.5, 0.9, 22.0), FRAME, 0.04)  # girders under the deck
    for x, z in ((3, 9), (-9, 0), (4, -9)):
        deck.box((x, 16.5, z), (2.4, 1.0, 1.6), PAINT, 0.05, SMALL_PANELS)  # machinery houses
    deck.cyl((-2, 16.0, 3), (5, 16.4, 3), 0.25, segs=8, kind=PAINT)  # pipe runs
    deck.cyl((5, 16.4, 3), (5, 16.4, -6), 0.25, segs=8, kind=PAINT)
    m.add(deck, "Platform", "Deck", "rig_steel")

    rail = Part("Rails")
    rail.stations("y", [(15.95, 24.6, 24.6, 0, 0, 0.3), (16.2, 24.6, 24.6, 0, 0, 0.3)], bevel=0.03)
    for side in range(4):
        r = Matrix.Rotation(side * math.pi / 2, 3, "Y")
        for i in range(13):
            rail.cyl(tuple(r @ Vector((-12 + i * 2, 16.2, 12.1))), tuple(r @ Vector((-12 + i * 2, 17.4, 12.1))), 0.05, segs=5, kind=PAINT, bevel=0)
        for y in (16.8, 17.4):
            rail.cyl(tuple(r @ Vector((-12, y, 12.1))), tuple(r @ Vector((12, y, 12.1))), 0.04, segs=5, kind=PAINT, bevel=0)
    m.add(rail, "Platform", "DeckEdge", "rig_paint")

    pad = Part("Helipad")
    pad.loft([[(7 + x, y, 7 + z) for x, z in ring(8, 4.8, phase=math.pi / 8)] for y in (16.3, 16.8)], bevel=0.05)
    pad.cyl((7, 14.5, 7), (7, 16.3, 7), 1.2, segs=10, kind=FRAME)
    m.add(pad, "Helipad", "Mesh", "asphalt")

    tower = Part("Derrick")
    truss(tower, (0, 16, -2), (0, 39.8, -2), 4.4, 1.6, 7, r=0.16)
    tower.cyl((0, 16, -2), (0, 36, -2), 0.22, segs=8, kind=FRAME, bevel=0)  # drill string
    tower.box((0, 17.0, -2), (3.0, 2.0, 3.0), PAINT, 0.08, SMALL_PANELS)  # drill floor housing
    m.add(tower, "Derrick", "Tower", "rig_steel")
    top = Part("Crown")
    top.stations("y", [(39.8, 3.2, 3.2, 0, -2, 0.3), (40.6, 3.6, 3.6, 0, -2, 0.3), (41.0, 2.0, 2.0, 0, -2, 0.2)], bevel=0.04)
    for sx in (-1, 1):
        top.cyl((sx * 1.8, 40.6, -3.8), (sx * 1.8, 41.6, -3.8), 0.04, segs=4, kind=PAINT, bevel=0)
    m.add(top, "Derrick", "Top", "rig_paint")

    crane = Part("Crane")  # in the axes of its node: the jib runs along Z
    truss(crane, (0, 0, 7), (0, 0, -7), 1.3, 0.5, 9, r=0.07)
    crane.box((0, -0.4, 6.4), (1.8, 2.2, 2.2), PAINT, 0.06, SMALL_PANELS)  # cab
    crane.cyl((0, -3.4, 6.4), (0, -1.5, 6.4), 0.7, segs=10, kind=FRAME)  # pedestal
    crane.cyl((0, 0, -6.8), (0, -3.0, -6.8), 0.03, segs=4, kind=FRAME, bevel=0)  # hook line
    crane.box((0, -3.2, -6.8), (0.3, 0.4, 0.3), FRAME, 0.04)
    m.add(crane, "Crane", "Mesh", "rig_paint", own_place=True)

    tank = Part("Tank")
    tank.lathe((0, -2.0, 0), (0, 2.3, 0), [(0, 1.6), (0.88, 1.6), (0.95, 1.2), (1.0, 0.2)], segs=16)
    for y in (-1.2, 0.2, 1.4):
        tank.cyl((0, y, 0), (0, y + 0.1, 0), 1.64, segs=16, kind=SOFT, bevel=0)
    for i in range(10):
        tank.box((0, -1.8 + i * 0.4, 1.66), (0.4, 0.04, 0.04), FRAME, 0)  # ladder
    for sx in (-1, 1):
        tank.box((sx * 0.2, 0.0, 1.66), (0.04, 4.0, 0.04), FRAME, 0)
    m.add(tank, "Tank1", "Mesh", "car_red", own_place=True)
    m.add(tank, "Tank2", "Mesh", "car_red", own_place=True)
    return m


ALL = {"soldier": soldier, "power_armor": power_armor, "hatchet": hatchet, "missile": missile, "bomb": bomb, "tumbleweed": tumbleweed, "oil_rig": oil_rig}
for name, make in ALL.items():
    if ARGS[1] in ("all", name):
        make().finish([ARGS[0]] + ARGS[2:])
