"""Tanks, the armoured car, the ship and the aircraft.
    blender -b --python tools/blender/vehicles.py -- <output dir> <tank|amphibian|apc|ship|helicopter|bomber> [preview]
Axes of the scenes: X right, Y up, forward is -Z; ground vehicles stand on Y = 0.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from meshkit import *  # noqa: E402
from walker import PANELS, SMALL_PANELS, barrel, piston  # noqa: E402
from vehicles_parts import tyre, hub  # noqa: E402

ARGS = sys.argv[sys.argv.index("--") + 1:]


def road_wheel(name, r, w):
    h = Part(name)
    h.cyl((-w / 2, 0, 0), (w / 2, 0, 0), r, segs=16, kind=FRAME)
    h.cyl((-w / 2 - 0.02, 0, 0), (-w / 2, 0, 0), r * 0.78, segs=16, kind=RECESS, bevel=0)
    h.cyl((-w / 2 - 0.05, 0, 0), (-w / 2, 0, 0), r * 0.36, segs=10, kind=SOFT)
    h.bolts([(-w / 2 - 0.02, math.cos(i * math.tau / 5) * r * 0.56, math.sin(i * math.tau / 5) * r * 0.56) for i in range(5)], (-1, 0, 0), r=r * 0.07, h=0.03)
    return h


def tracked(m, length, width, deck, hull_h, turret_at, turret=(2.3, 0.85, 2.7), gun=(3.8, 0.14), boat=False):
    """A tank. length / width of the hull, deck = height of its top, hull_h = how deep the upper hull is;
    turret = (width, height, length); gun = (barrel length, radius)."""
    L, W = length / 2, width / 2
    body = Part("Body")
    floor = 0.42
    # lower tub between the tracks
    body.stations("z", [(-L + 0.1, W * 1.3, 0.3, 0, floor + 0.45), (-L + 0.7, W * 1.36, deck - hull_h - floor + 0.1, 0, (deck - hull_h + floor) / 2),
                        (L - 0.4, W * 1.36, deck - hull_h - floor + 0.1, 0, (deck - hull_h + floor) / 2), (L - 0.05, W * 1.3, 0.4, 0, floor + 0.5)], kind=FRAME, bevel=0.03)
    # upper hull: sloped glacis (or a boat bow), flat deck, engine at the back
    top = deck - hull_h / 2
    if boat:
        rows = [(-L - 0.85, W * 1.3, 0.2, 0, deck - 0.2, 0.06), (-L - 0.1, W * 2.0, hull_h * 1.25, 0, top - hull_h * 0.12, 0.12, 0.5),
                (-L + 1.3, W * 2.0, hull_h, 0, top, 0.14), (L - 0.3, W * 2.0, hull_h, 0, top, 0.14), (L, W * 1.94, hull_h * 0.7, 0, top + 0.05, 0.12)]
    else:
        rows = [(-L - 0.15, W * 1.9, 0.14, 0, deck - hull_h + 0.1, 0.04), (-L + 1.4, W * 2.0, hull_h, 0, top, 0.14),
                (L - 0.3, W * 2.0, hull_h, 0, top, 0.14), (L, W * 1.94, hull_h * 0.6, 0, top + 0.05, 0.12)]
    body.stations("z", rows, seams=PANELS, cuts=[((0, 0, -0.6), (0, 0, 1)), ((0, 0, L * 0.45), (0, 0, 1))])
    # engine deck grilles, hatches, lamps, exhausts, stowage
    for s in (-1, 1):
        body.slats((s * W * 0.5, deck, L * 0.7), (0, 1, 0), (1, 0, 0), (W * 0.7, L * 0.42), 6, 0.04)
        body.plate(None, (s * W * 0.45, deck, -L * 0.42), (0, 1, 0), (0.6, 0.7), 0.05, along=(0, 0, 1))
        body.cyl((s * W * 0.72, deck - hull_h * 0.2, -L - 0.02 + (0.5 if not boat else 0.0)), (s * W * 0.72, deck - hull_h * 0.2, -L - 0.12 + (0.5 if not boat else 0.0)), 0.11, segs=10, kind=SOFT)
        body.cyl((s * W * 0.8, deck - hull_h * 0.45, L - 0.1), (s * W * 0.8, deck - hull_h * 0.3, L + 0.35), 0.09, segs=8, kind=FRAME)
        body.box((s * (W - 0.05), deck + 0.14, L * 0.15), (0.5, 0.26, 1.2), PAINT, 0.02, SMALL_PANELS)
        # side skirts over the upper run of the track
        body.box((s * (W + 0.36), deck - hull_h - 0.12, 0), (0.06, 0.6, length * 0.94), PAINT, 0.015, dict(margin=0.04, width=0.012, depth=0.012, min_area=0.4))
        body.box((s * (W + 0.02), deck - hull_h + 0.14, 0), (0.78, 0.06, length * 1.0), PAINT, 0.015)  # fender
    body.cyl((0, deck - 0.02, turret_at[2]), (0, deck + 0.06, turret_at[2]), turret[0] * 0.44, segs=24, kind=FRAME)
    for i in range(2):
        body.cyl((-W * 0.9 + i * 0.45, deck + 0.2, L - 0.5), (W * 0.1 - W + i * 0.45 + 0.9, deck + 0.2, L - 0.5), 0.2, segs=12, kind=PAINT) if False else None
    body.cyl((-W * 0.75, deck + 0.24, L - 0.25), (W * 0.75, deck + 0.24, L - 0.25), 0.22, segs=12, kind=PAINT)  # fuel drum
    m.add(body, "Hull", "Body", CAMO)

    # Tracks: a belt round sprocket, road wheels and idler
    zf, zr = -L - 0.12, L + 0.12
    path = [(zf + 0.55, 0.0), (zr - 0.6, 0.0), (zr - 0.05, 0.42), (zr - 0.05, 0.72), (zr - 0.3, 0.98), (zf + 0.3, 0.98), (zf + 0.05, 0.74), (zf + 0.05, 0.45)]
    tr = Part("TrackL")
    x0, x1 = -(W + 0.42), -(W - 0.3)
    tr.band(path, x0, x1, 0.09)
    # Track links: a cleat every 30 cm along the upper and lower runs. They are parts of their own,
    # which the game slides back and forth by one link's length as the vehicle moves.
    n = int((zr - zf - 1.2) / 0.3)
    upper, lower = Part("TreadTopL"), Part("TreadBottomL")
    for i in range(n):
        z = zf + 0.7 + i * 0.3
        lower.box(((x0 + x1) / 2, -0.005, z), (x1 - x0 + 0.02, 0.05, 0.16), FRAME, 0)
        upper.box(((x0 + x1) / 2, 0.985, z), (x1 - x0 + 0.02, 0.05, 0.16), FRAME, 0)
    # the dark gap behind the wheels
    tr.box((x1 - 0.06, 0.5, 0), (0.1, 0.74, zr - zf - 0.9), RECESS, 0)
    m.both(tr, "Hull", "TrackL", JOINT)
    m.both(upper, "Hull/TrackL", "TreadTop", JOINT)
    m.both(lower, "Hull/TrackL", "TreadBottom", JOINT)

    count = max(5, int(length / 1.05))
    spots = [(zf + 0.5, 0.62, 0.33)] + [(zf + 1.25 + i * (zr - zf - 2.5) / (count - 1), 0.44, 0.36) for i in range(count)] + [(zr - 0.5, 0.56, 0.33)]
    for i, (z, y, r) in enumerate(spots):
        w = road_wheel("RoadWheelL%d" % (0 if 0 < i < len(spots) - 1 else 1), r, 0.34)
        m.both(w, "Hull", "WheelL%d" % (i + 1), JOINT, at=(-(W + 0.2), y, z))

    # Turret
    tw, th, tl = turret
    t = Part("Turret")
    t.stations("z", [(-tl * 0.56, tw * 0.5, th * 0.6, 0, th * 0.42, 0.12), (-tl * 0.28, tw * 0.95, th * 0.95, 0, th * 0.5, 0.2, 0.3),
                     (tl * 0.3, tw, th, 0, th * 0.5, 0.2, 0.3), (tl * 0.62, tw * 0.8, th * 0.75, 0, th * 0.55, 0.16)], seams=PANELS)
    t.cyl((tw * 0.22, th - 0.02, tl * 0.1), (tw * 0.22, th + 0.2, tl * 0.1), tw * 0.16, segs=14, kind=PAINT)  # cupola
    t.cyl((tw * 0.22, th + 0.2, tl * 0.1), (tw * 0.22, th + 0.24, tl * 0.1), tw * 0.13, segs=14, kind=SOFT)
    t.plate(None, (-tw * 0.22, th, tl * 0.12), (0, 1, 0), (tw * 0.3, tl * 0.25), 0.04, along=(0, 0, 1))  # loader's hatch
    t.box((0, th * 0.55, tl * 0.74), (tw * 0.75, th * 0.5, tl * 0.2), PAINT, 0.02, SMALL_PANELS)  # stowage bin
    for s in (-1, 1):
        for i in range(3):  # smoke dischargers
            a = (s * tw * 0.5, th * 0.62, -tl * 0.05 + i * 0.16)
            t.cyl(a, (a[0] + s * 0.16, a[1] + 0.12, a[2] - 0.1), 0.05, segs=8, kind=FRAME, bevel=0)
    t.cyl((-tw * 0.36, th, tl * 0.5), (-tw * 0.36, th + 1.6, tl * 0.5), 0.02, 0.008, segs=6, kind=FRAME, bevel=0)
    t.box((tw * 0.22, th + 0.3, -tl * 0.05), (0.14, 0.14, 0.4), FRAME, 0.01)  # machine gun on the cupola
    t.cyl((tw * 0.22, th + 0.3, -tl * 0.2), (tw * 0.22, th + 0.3, -tl * 0.2 - 0.7), 0.025, segs=6, kind=FRAME, bevel=0)
    m.add(t, "Turret", "Body", CAMO)

    g = Part("Gun")
    glen, gr = gun
    g.stations("z", [(0.1, tw * 0.34, th * 0.75, 0, 0, 0.1), (-0.4, tw * 0.28, th * 0.6, 0, 0, 0.1), (-0.62, tw * 0.16, th * 0.36, 0, 0, 0.06)], kind=FRAME)
    g.cyl((0, 0, -0.5), (0, 0, -glen), gr, segs=14, kind=FRAME)
    g.cyl((0, 0, -glen * 0.52), (0, 0, -glen * 0.68), gr * 1.5, segs=14, kind=FRAME)  # fume extractor
    g.cyl((0, 0, -0.6), (0, 0, -1.1), gr * 1.35, segs=14, kind=FRAME)
    g.box((0, 0, -glen + 0.16), (gr * 3.2, gr * 2.4, 0.34), FRAME)  # muzzle brake
    g.cyl((0, 0, -glen - 0.01), (0, 0, -glen - 0.03), gr * 0.7, segs=12, kind=RECESS, bevel=0)
    for s in (-1, 1):
        g.slats((s * gr * 1.61, 0, -glen + 0.16), (s, 0, 0), (0, 1, 0), (gr * 1.4, 0.24), 2, 0.02)
    g.cyl((tw * 0.24, th * 0.18, -0.4), (tw * 0.24, th * 0.18, -1.35), 0.03, segs=6, kind=FRAME, bevel=0)  # coaxial machine gun
    m.add(g, "Turret/Gun", "Mantlet", JOINT)
    m.removes += ["Hull/Glacis", "Hull/Bow", "Turret/Gun/Barrel"]


def tank():
    m = Model("tank", "scenes/vehicles/tank.tscn")
    tracked(m, 6.4, 3.4, 1.55, 0.62, (0, 1.55, 0.3))
    return m


def amphibian():
    m = Model("amphibian", "scenes/vehicles/amphibian.tscn")
    tracked(m, 7.2, 3.3, 1.85, 0.95, (0, 1.85, 0.2), turret=(1.9, 0.7, 2.2), gun=(3.0, 0.1), boat=True)
    return m


def apc():
    """Eight-wheeled armoured car with a small autocannon turret."""
    m = Model("apc", "scenes/vehicles/apc.tscn")
    b = Part("Body")
    b.stations("z", [(-3.75, 2.0, 0.3, 0, 1.55, 0.06), (-2.7, 2.8, 1.45, 0, 1.66, 0.3, 0.55), (-1.6, 2.8, 1.5, 0, 1.65, 0.3, 0.6),
                     (3.15, 2.8, 1.5, 0, 1.65, 0.3, 0.6), (3.4, 2.7, 1.2, 0, 1.75, 0.25, 0.4)], seams=PANELS, cuts=[((0, 0, -1.2), (0, 0, 1)), ((0, 0, 1.2), (0, 0, 1))])
    # belly and axles
    b.stations("z", [(-3.0, 1.5, 0.5, 0, 0.82, 0.1), (3.0, 1.5, 0.5, 0, 0.82, 0.1)], kind=FRAME)
    for z in (-2.38, -0.79, 0.79, 2.38):
        b.cyl((-1.3, 0.6, z), (1.3, 0.6, z), 0.12, segs=8, kind=FRAME, bevel=0)
    for s in (-1, 1):
        b.plate(None, (s * 0.7, 2.4, -1.9), (0, 1, 0), (0.6, 0.6), 0.05, along=(0, 0, 1))  # driver / commander hatches
        b.box((s * 0.7, 2.47, -2.3), (0.4, 0.12, 0.1), FRAME, 0.01)  # periscopes
        b.cyl((s * 1.05, 1.95, -3.2), (s * 1.05, 1.95, -3.3), 0.13, segs=10, kind=SOFT)  # headlamps
        b.box((s * 1.43, 1.9, 1.2), (0.12, 0.5, 2.6), PAINT, 0.02, SMALL_PANELS)  # stowage
        b.plate(None, (s * 0.6, 1.7, 3.41), (0, 0.1, 1), (0.9, 1.2), 0.04, along=(0, 1, 0))  # rear doors
        b.slats((s * 1.41, 2.05, -1.4), (s, 0.2, 0), (0, 0, 1), (0.9, 0.4), 4, 0.04)
    b.box((0, 1.52, -3.7), (2.0, 0.5, 0.05), PAINT, 0.012, None, Matrix.Rotation(math.radians(38), 3, "X"))  # trim vane
    b.cyl((1.5, 2.1, 2.2), (1.5, 2.1, 3.3), 0.1, segs=8, kind=FRAME)  # exhaust
    b.cyl((0, 2.38, -0.6), (0, 2.45, -0.6), 0.72, segs=20, kind=FRAME)
    b.cyl((-1.0, 2.4, 2.9), (-1.0, 4.0, 2.9), 0.02, 0.008, segs=6, kind=FRAME, bevel=0)
    m.add(b, "Hull", "Body", CAMO)
    t, h = tyre("Tyre", 0.6, 0.45), hub("WheelHubL", 0.6, 0.45)
    hr = h.mirrored("WheelHubR")
    for i, z in enumerate((-2.38, -0.79, 0.79, 2.38)):
        for side, s, hh in (("L", -1, h), ("R", 1, hr)):
            m.add(t, "Hull", f"Wheel{i + 1}{side}", TYRE, at=(s * 1.45, 0.6, z))
            m.add(hh, f"Hull/Wheel{i + 1}{side}", "Hub", JOINT)

    tu = Part("Turret")
    tu.stations("z", [(-0.95, 0.7, 0.36, 0, 0.26, 0.08), (-0.4, 1.4, 0.56, 0, 0.3, 0.14, 0.2), (0.5, 1.5, 0.58, 0, 0.3, 0.14, 0.2), (0.95, 1.1, 0.42, 0, 0.32, 0.1)], seams=SMALL_PANELS)
    tu.plate(None, (0.3, 0.59, 0.25), (0, 1, 0), (0.5, 0.5), 0.04, along=(0, 0, 1))
    tu.box((-0.45, 0.68, 0.0), (0.26, 0.2, 0.3), FRAME, 0.02)  # sight
    for s in (-1, 1):
        for i in range(2):
            a = (s * 0.75, 0.4, 0.1 + i * 0.16)
            tu.cyl(a, (a[0] + s * 0.14, a[1] + 0.1, a[2] - 0.08), 0.045, segs=8, kind=FRAME, bevel=0)
    m.add(tu, "Turret", "Body", CAMO)
    g = Part("Gun")
    g.stations("z", [(0.1, 0.5, 0.42, 0, 0, 0.08), (-0.34, 0.36, 0.3, 0, 0, 0.06)], kind=FRAME)
    g.cyl((0, 0, -0.3), (0, 0, -2.2), 0.055, segs=10, kind=FRAME, bevel=0)
    g.cyl((0, 0, -0.3), (0, 0, -1.0), 0.09, segs=10, kind=FRAME)
    g.cyl((0, 0, -2.0), (0, 0, -2.26), 0.085, segs=10, kind=SOFT)
    g.cyl((0, 0, -2.25), (0, 0, -2.28), 0.04, segs=8, kind=RECESS, bevel=0)
    g.cyl((0.4, 0.1, -0.2), (0.4, 0.1, -0.95), 0.025, segs=6, kind=FRAME, bevel=0)
    m.add(g, "Turret/Gun", "Mantlet", JOINT)
    m.removes += ["Hull/Glacis", "Turret/Gun/Barrel"]
    return m


def hull_section(b, keel, deck=3.4, flare=1.0):
    """Cross-section of a ship's hull: half-breadth b at the deck, keel at height 'keel'."""
    return [(0.02, keel), (b * 0.55, keel + (deck - keel) * 0.22), (b * 0.92, keel + (deck - keel) * 0.6), (b * flare, deck),
            (-b * flare, deck), (-b * 0.92, keel + (deck - keel) * 0.6), (-b * 0.55, keel + (deck - keel) * 0.22), (-0.02, keel)]


def ship():
    """A patrol ship: raked bow, superstructure with a bridge, mast, funnel, a gun forward, missiles aft."""
    m = Model("ship", "scenes/vehicles/ship.tscn")
    rows = [(-17.6, 0.06, 2.3, 1.12), (-15.5, 1.3, 1.0, 1.1), (-11.5, 2.7, 0.25, 1.06), (-5.0, 3.4, 0.0, 1.03), (6.0, 3.5, 0.0, 1.0), (13.0, 3.3, 0.25, 1.0), (16.0, 3.0, 0.9, 1.0)]
    b = Part("Body")
    b.loft([[(x, y, z) for x, y in hull_section(br, keel, 3.4, fl)] for z, br, keel, fl in rows], seams=dict(margin=0.12, width=0.02, depth=0.02, min_area=6.0), bevel=0.04)
    # bulwark round the bow, rubbing strake, anchors
    for s in (-1, 1):
        b.box((s * 3.52, 2.5, 3.0), (0.08, 0.16, 18.0), FRAME, 0.02)
        b.box((s * 1.7, 2.6, -14.6), (0.25, 0.5, 0.35), FRAME, 0.03, None, Matrix.Rotation(s * math.radians(-24), 3, "Y"))
    m.add(b, "Hull", "Body", GREY)

    d = Part("Deck")
    d.loft([[(x * 0.97, 3.36 + (0.14 if y > 3 else 0.0), z) for x, y in [(br, 3.4), (-br, 3.4), (-br, 0), (br, 0)]] for z, br, keel, fl in rows[1:]], kind=FRAME, bevel=0.0)
    for z in (-13.0, -4.5, 13.5):
        for s in (-1, 1):
            d.cyl((s * 2.0, 3.5, z), (s * 2.0, 3.75, z), 0.12, segs=8, kind=FRAME)  # bollards
    d.box((0, 3.62, -12.0), (0.9, 0.25, 1.2), FRAME, 0.03)  # windlass
    # guard rails: posts and two wires down each side
    for s in (-1, 1):
        for z in range(-10, 15, 2):
            d.cyl((s * 3.2, 3.5, z), (s * 3.2, 4.4, z), 0.025, segs=5, kind=FRAME, bevel=0)
        for y in (3.95, 4.4):
            d.cyl((s * 3.2, y, -10), (s * 3.2, y, 14), 0.015, segs=4, kind=FRAME, bevel=0)
    m.add(d, "Hull", "Deck", JOINT)

    s_ = Part("Bridge")
    s_.stations("z", [(-0.6, 4.4, 2.0, 0, 4.5, 0.2), (0.2, 5.2, 2.2, 0, 4.6, 0.25), (9.5, 5.2, 2.2, 0, 4.6, 0.25), (10.5, 4.4, 1.6, 0, 4.4, 0.2)], seams=dict(margin=0.1, width=0.02, depth=0.02, min_area=1.2), cuts=[((0, 0, 3.2), (0, 0, 1)), ((0, 0, 6.4), (0, 0, 1))])
    s_.stations("z", [(-0.2, 3.6, 0.5, 0, 5.9, 0.1), (0.5, 4.4, 1.7, 0, 6.5, 0.25), (4.6, 4.4, 1.7, 0, 6.5, 0.25), (5.4, 3.8, 1.2, 0, 6.3, 0.2)], seams=dict(margin=0.1, width=0.02, depth=0.02, min_area=1.0))
    for s in (-1, 1):
        s_.box((s * 3.0, 6.0, 1.6), (1.4, 0.1, 1.6), PAINT, 0.02)  # bridge wings
        s_.cyl((s * 2.3, 5.9, 8.0), (s * 2.3, 6.4, 8.0), 0.45, segs=12, kind=PAINT)  # life rafts
        for z in (1.5, 4.0, 7.0):
            s_.plate(None, (s * 2.61, 4.6, z), (s, 0, 0), (0.7, 1.7), 0.05, along=(0, 1, 0))  # doors
    s_.box((0, 7.5, 2.5), (2.2, 0.3, 2.2), PAINT, 0.04)  # compass platform
    m.add(s_, "Hull", "Bridge", CAMO)

    w = Part("Windows")
    w.stations("z", [(0.42, 4.46, 0.62, 0, 6.72, 0.04), (0.62, 4.46, 0.62, 0, 6.72, 0.04)], bevel=0.01)
    for s in (-1, 1):
        w.box((s * 2.21, 6.72, 2.4), (0.04, 0.6, 3.6), PAINT, 0.01)
    m.add(w, "Hull", "Windows", GLASS)

    ma = Part("Mast")
    for s in (-1, 1):
        ma.cyl((s * 0.7, 7.3, 4.2), (s * 0.12, 12.4, 5.0), 0.09, segs=8, kind=FRAME, bevel=0)
    ma.cyl((0, 7.3, 6.2), (0, 12.4, 5.0), 0.09, segs=8, kind=FRAME, bevel=0)
    ma.box((0, 10.6, 5.0), (2.6, 0.12, 0.3), FRAME, 0.02)  # yard
    ma.box((0, 11.4, 4.6), (2.0, 0.5, 0.18), FRAME, 0.03)  # radar aerial
    ma.cyl((0, 9.2, 4.9), (0, 9.4, 4.9), 0.8, segs=14, kind=FRAME)
    ma.cyl((0, 12.4, 5.0), (0, 14.2, 5.0), 0.03, 0.01, segs=6, kind=FRAME, bevel=0)
    ma.lathe((0, 9.4, 4.9), (0, 10.1, 4.9), [(0, 0.5), (0.5, 0.48), (1, 0.1)], segs=12, kind=PAINT)  # radome
    m.add(ma, "Hull", "Mast", JOINT)

    f = Part("Funnel")
    f.stations("y", [(6.6, 1.9, 2.6, 0, 7.3, 0.4), (8.4, 1.5, 2.2, 0, 7.8, 0.35), (9.3, 1.3, 1.9, 0, 8.0, 0.3)], kind=FRAME, seams=SMALL_PANELS)
    f.stations("y", [(9.25, 1.0, 1.5, 0, 8.0, 0.25), (9.36, 1.0, 1.5, 0, 8.0, 0.25)], kind=RECESS, bevel=0)
    m.add(f, "Hull", "Funnel", JOINT)

    la = Part("Launcher")
    tilt = Matrix.Rotation(math.radians(-28), 3, "X")
    for s in (-1, 1):
        la.box((s * 0.75, 4.35, 12.0), (1.1, 1.0, 3.0), PAINT, 0.04, SMALL_PANELS, tilt)
        for i in range(2):
            for j in range(2):
                c = Vector((s * 0.75 + (i - 0.5) * 0.48, 4.35 + (j - 0.5) * 0.44, 12.0)) + tilt @ Vector((0, 0, -1.52))
                la.cyl(tuple(c), tuple(c + tilt @ Vector((0, 0, -0.03))), 0.17, segs=10, kind=RECESS, bevel=0)
    la.box((0, 3.7, 12.3), (2.4, 0.4, 2.2), FRAME, 0.03)
    m.add(la, "Hull", "Launcher", CAMO)

    t = Part("Turret")
    t.cyl((0, 0, 0), (0, 0.2, 0), 1.5, segs=20, kind=FRAME)
    t.stations("z", [(-1.7, 1.2, 0.8, 0, 0.75, 0.2), (-0.9, 2.4, 1.4, 0, 0.9, 0.35), (0.9, 2.6, 1.5, 0, 0.95, 0.35), (1.6, 2.0, 1.1, 0, 0.85, 0.3)], seams=PANELS)
    t.plate(None, (0.6, 1.7, 0.5), (0, 1, 0), (0.6, 0.6), 0.05, along=(0, 0, 1))
    m.add(t, "Turret", "Body", CAMO)
    g = Part("Gun")
    g.stations("z", [(0.3, 0.8, 0.9, 0, 0, 0.14), (-0.5, 0.6, 0.6, 0, 0, 0.12), (-0.9, 0.4, 0.4, 0, 0, 0.08)], kind=FRAME)
    g.cyl((0, 0, -0.8), (0, 0, -4.4), 0.13, segs=14, kind=FRAME)
    g.cyl((0, 0, -0.9), (0, 0, -1.7), 0.2, segs=14, kind=FRAME)
    g.cyl((0, 0, -4.1), (0, 0, -4.46), 0.18, segs=14, kind=SOFT)
    g.cyl((0, 0, -4.45), (0, 0, -4.48), 0.09, segs=12, kind=RECESS, bevel=0)
    m.add(g, "Turret/Gun", "Mantlet", JOINT)
    m.removes += ["Hull/Bow", "Turret/Gun/Barrel"]
    return m


def blade(p, length, chord, root=0.6, angle=0.0, thick=0.05):
    """A rotor blade lying flat, running out from the hub along the direction 'angle' (about Y)."""
    rot = Matrix.Rotation(angle, 3, "Y")
    secs = []
    for r, c, t in ((root, chord * 0.5, thick), (root + 0.5, chord, thick), (length - 0.25, chord, thick * 0.8), (length, chord * 0.55, thick * 0.5)):
        pts = [(c * 0.5, 0), (c * 0.25, t / 2), (-c * 0.3, t / 2), (-c * 0.5, 0), (-c * 0.3, -t / 2), (c * 0.25, -t / 2)]
        secs.append([tuple(rot @ Vector((r, v, u))) for u, v in pts])
    p.loft(secs, kind=FRAME, bevel=0)


def helicopter():
    """A gunship: narrow fuselage with a stepped tandem canopy, engines either side of the mast,
    stub wings with rocket pods, a chin gun."""
    m = Model("helicopter", "scenes/vehicles/helicopter.tscn")
    f = Part("Fuselage")
    f.stations("z", [(-5.0, 0.34, 0.4, 0, -0.62, 0.1), (-4.2, 0.9, 0.9, 0, -0.5, 0.2), (-2.9, 1.25, 1.5, 0, -0.3, 0.3), (-1.2, 1.4, 2.0, 0, -0.05, 0.35),
                     (1.4, 1.4, 2.1, 0, 0.0, 0.35), (2.6, 1.1, 1.5, 0, 0.25, 0.3), (3.4, 0.62, 0.8, 0, 0.48, 0.18)], seams=PANELS, cuts=[((0, 0, 0.2), (0, 0, 1))])
    # engines either side of the mast, with intakes and upturned exhausts
    for s in (-1, 1):
        f.stations("z", [(-1.5, 0.5, 0.5, s * 0.95, 0.72, 0.14), (-0.9, 0.72, 0.72, s * 0.98, 0.72, 0.2), (1.6, 0.72, 0.72, s * 0.98, 0.72, 0.2), (2.3, 0.5, 0.5, s * 0.95, 0.76, 0.14)], seams=SMALL_PANELS)
        f.cyl((s * 0.95, 0.72, -1.5), (s * 0.95, 0.72, -1.53), 0.17, segs=10, kind=RECESS, bevel=0)
        f.cyl((s * 1.0, 0.8, 2.1), (s * 1.3, 1.0, 2.7), 0.2, segs=10, kind=FRAME)
        f.cyl((s * 1.29, 0.99, 2.68), (s * 1.31, 1.01, 2.72), 0.14, segs=10, kind=RECESS, bevel=0)
        f.plate(None, (s * 0.71, -0.2, -0.4), (s, 0, 0), (1.0, 1.4), 0.04, along=(0, 0, 1))
    f.stations("y", [(0.95, 0.9, 1.6, 0, 0.2, 0.3), (1.32, 0.5, 0.9, 0, 0.1, 0.2)], seams=SMALL_PANELS)  # mast fairing
    f.cyl((0, 1.3, 0), (0, 1.52, 0), 0.14, segs=10, kind=FRAME)
    # canopy frame
    for z, y in ((-3.0, 0.52), (-1.95, 1.02)):
        f.box((0, y, z), (1.02, 0.07, 0.07), FRAME, 0.01)
    f.cyl((0, -0.72, -4.9), (0, -0.72, -5.25), 0.13, segs=10, kind=FRAME)  # nose sensor
    m.add(f, "Hull", "Fuselage", CAMO)

    c = Part("Canopy")
    c.stations("z", [(-4.1, 0.5, 0.1, 0, -0.02, 0.04), (-3.05, 0.92, 0.72, 0, 0.3, 0.2), (-2.95, 0.96, 0.9, 0, 0.42, 0.22), (-1.95, 1.02, 1.0, 0, 0.62, 0.25), (-1.3, 0.9, 0.6, 0, 0.72, 0.2)], bevel=0.012)
    m.add(c, "Hull", "Canopy", GLASS)

    tb = Part("TailBoom")
    tb.stations("z", [(3.0, 0.7, 0.85, 0, 0.45, 0.2), (8.4, 0.34, 0.44, 0, 0.72, 0.1), (9.4, 0.2, 0.3, 0, 0.8, 0.06)], seams=SMALL_PANELS)
    tb.box((0, 0.2, 7.4), (0.08, 0.5, 0.7), FRAME, 0.02)  # tail skid
    m.add(tb, "Hull", "TailBoom", CAMO)

    fin = Part("Fin")
    fin.loft([[(x, y, z) for z, y in [(7.9, 0.75), (9.3, 0.7), (10.0, 2.8), (9.2, 2.8)]] for x in (-0.07, 0.07)], seams=SMALL_PANELS)
    fin.loft([[(x, y, z) for z, y in [(8.2, 0.9), (9.0, 0.9), (9.15, 0.96), (8.35, 0.96)]] for x in (-1.3, 1.3)])  # tailplane
    fin.cyl((0.05, 1.0, 8.9), (0.38, 1.0, 8.9), 0.12, segs=10, kind=FRAME)
    m.add(fin, "Hull", "Fin", CAMO)

    wg = Part("Wings")
    for s in (-1, 1):
        wg.loft([[(s * x, -0.3 + v * t - (x - 0.7) * 0.08, 0.5 + u * c) for u, v in [(-0.5, 0), (-0.2, 0.5), (0.3, 0.5), (0.5, 0), (0.3, -0.5), (-0.2, -0.5)]]
                 for x, c, t in ((0.6, 1.5, 0.22), (2.95, 1.0, 0.12))], kind=PAINT)
        wg.box((s * 2.7, -0.52, 0.45), (0.1, 0.22, 0.8), FRAME, 0.02)  # pylons
        wg.box((s * 1.7, -0.5, 0.45), (0.1, 0.22, 0.8), FRAME, 0.02)
        # missiles on the inner pylons
        wg.cyl((s * 1.7, -0.72, -0.45), (s * 1.7, -0.72, 1.2), 0.09, segs=8, kind=FRAME)
        wg.cyl((s * 1.7, -0.72, -0.45), (s * 1.7, -0.72, -0.7), 0.09, 0.02, segs=8, kind=SOFT)
    m.add(wg, "Hull", "Wings", CAMO)

    pod = Part("PodL")
    pod.cyl((0, 0, -0.9), (0, 0, 0.9), 0.34, segs=14, kind=FRAME)
    pod.cyl((0, 0, 0.9), (0, 0, 1.2), 0.34, 0.12, segs=14, kind=FRAME)
    for i in range(7):
        a = i * math.tau / 6
        x, y = (0, 0) if i == 6 else (math.cos(a) * 0.2, math.sin(a) * 0.2)
        pod.cyl((x, y, -0.9), (x, y, -0.92), 0.07, segs=8, kind=RECESS, bevel=0)
    m.both(pod, "Hull", "PodL", JOINT, at=(-2.7, -0.6, 0.4))

    sk = Part("SkidL")
    sk.cyl((0, 0, -2.0), (0, 0, 2.3), 0.07, segs=8, kind=FRAME)
    sk.cyl((0, 0, -2.0), (0, 0.35, -2.5), 0.07, segs=8, kind=FRAME)
    for z in (-1.0, 1.3):
        sk.cyl((0, 0, z), (0.55, 0.85, z), 0.055, segs=8, kind=FRAME)
    m.both(sk, "Hull", "SkidL", JOINT, at=(-1.22, -1.7, -0.3))

    r = Part("MainRotor")
    r.cyl((0, -0.25, 0), (0, 0.12, 0), 0.26, segs=12, kind=FRAME)
    r.cyl((0, 0.1, 0), (0, 0.3, 0), 0.12, segs=10, kind=SOFT)
    for i in range(4):
        blade(r, 6.6, 0.48, angle=i * math.tau / 4)
        a = i * math.tau / 4
        r.box((math.cos(a) * 0.45, 0, -math.sin(a) * 0.45), (0.5, 0.12, 0.16), FRAME, 0.02, None, Matrix.Rotation(a, 3, "Y"))
    m.add(r, "Hull/RotorMain", "Blades", JOINT)
    tr = Part("TailRotor")
    tr.cyl((0, -0.12, 0), (0, 0.1, 0), 0.1, segs=10, kind=FRAME)
    for i in range(4):
        blade(tr, 1.25, 0.2, root=0.12, angle=i * math.tau / 4 + 0.4, thick=0.03)
    m.add(tr, "Hull/RotorTail", "Blades", JOINT)

    t = Part("Turret")
    t.cyl((0, 0.45, 0), (0, 0.62, 0), 0.36, segs=14, kind=FRAME)
    t.stations("z", [(-0.4, 0.4, 0.34, 0, 0.3, 0.08), (0.4, 0.5, 0.4, 0, 0.3, 0.1)], kind=PAINT)
    m.add(t, "Turret", "Body", CAMO)
    g = Part("Gun")
    g.box((0, 0, -0.1), (0.24, 0.26, 0.5), FRAME, 0.02)
    g.cyl((0, 0, -0.3), (0, 0, -1.45), 0.045, segs=8, kind=FRAME, bevel=0)
    g.cyl((0, 0, -0.3), (0, 0, -0.8), 0.075, segs=8, kind=FRAME)
    g.cyl((0, 0, -1.3), (0, 0, -1.5), 0.07, segs=8, kind=SOFT)
    m.add(g, "Turret/Gun", "Mantlet", JOINT)
    m.removes += ["Turret/Gun/Barrel"]
    return m


def bomber():
    """A flying wing in the manner of the American stealth bombers: no tail, no fuselage to speak of,
    a straight swept leading edge and a saw-toothed trailing edge."""
    m = Model("bomber", "scenes/vehicles/bomber.tscn")
    span, nose = 13.0, -10.5

    def lead(x):
        return nose + abs(x) * (12.6 / span)

    def trail(x):
        pts = [(0, 5.6), (3.0, 7.6), (5.6, 4.8), (8.6, 7.3), (13.0, 3.7)]
        x = abs(x)
        for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
            if x <= x1:
                return z0 + (z1 - z0) * (x - x0) / (x1 - x0)
        return pts[-1][1]

    def thick(x):
        x = abs(x)
        body = 2.5 * max(0.0, 1 - (x / 3.4) ** 2) ** 1.5
        return max(body, 0.95 * (1 - x / span) + 0.12)

    def section(x):
        le, te, t = lead(x), trail(x), thick(x)
        c = te - le
        up = 0.62 if abs(x) < 3.4 else 0.5  # the middle bulges upwards for the crew and the bomb bay
        return [(x, 0.0, le), (x, t * up * 0.75, le + c * 0.12), (x, t * up, le + c * 0.34), (x, t * up * 0.55, le + c * 0.7), (x, 0.02, te),
                (x, -t * (1 - up) * 0.6, le + c * 0.7), (x, -t * (1 - up), le + c * 0.36), (x, -t * (1 - up) * 0.7, le + c * 0.12)]

    xs = [-13.0, -8.6, -5.6, -3.4, -1.7, 0.0, 1.7, 3.4, 5.6, 8.6, 13.0]
    f = Part("Fuselage")
    f.loft([section(x) for x in xs], seams=dict(margin=0.14, width=0.02, depth=0.02, min_area=5.0), bevel=0.03)
    # bomb bay doors and wheel wells on the belly
    for s in (-1, 1):
        f.plate(None, (s * 0.9, -thick(0.9) * 0.38 - 0.02, 0.4), (0, -1, 0), (1.5, 5.0), 0.03, along=(0, 0, 1))
        f.plate(None, (s * 4.4, -0.3, 1.2), (0, -1, 0), (1.4, 2.2), 0.03, along=(0, 0, 1))
    # A stealth aircraft is not repainted with the player's camouflage: it wears its own dark grey.
    m.add(f, "Hull", "Fuselage", "stealth_grey")

    c = Part("Canopy")
    c.loft([[(x, y, z) for x, y in [(w, 0.0), (w * 0.8, h * 0.8), (w * 0.3, h), (-w * 0.3, h), (-w * 0.8, h * 0.8), (-w, 0.0)]]
            for z, w, h, base in ((-8.9, 0.5, 0.3, 0.55), (-7.9, 1.25, 0.72, 0.98), (-7.0, 1.4, 0.5, 1.4))] if False else
           [[(x, base + y, z) for x, y in [(w, 0.0), (w * 0.8, h * 0.8), (w * 0.3, h), (-w * 0.3, h), (-w * 0.8, h * 0.8), (-w, 0.0)]]
            for z, w, h, base in ((-8.7, 0.5, 0.12, 0.38), (-7.8, 1.2, 0.42, 0.72), (-6.9, 1.35, 0.3, 1.08))], bevel=0.012)
    m.add(c, "Hull", "Canopy", GLASS)

    # Engines buried in the wing: a scoop on top in front, a trough behind
    e = Part("EngineL")
    e.loft([[(-3.3 + u, y0 + v, z) for u, v in [(1.0, 0), (0.85, h), (-0.85, h), (-1.0, 0)]] for z, y0, h in ((-4.6, 0.95, 0.02), (-3.3, 1.0, 0.62), (-0.5, 0.9, 0.62), (2.2, 0.55, 0.3), (3.6, 0.35, 0.05))],
           kind=PAINT, seams=SMALL_PANELS)
    e.box((-3.3, 1.28, -3.42), (1.5, 0.42, 0.1), RECESS, 0)
    e.box((-3.3, 0.5, 3.4), (1.5, 0.12, 1.2), RECESS, 0)
    m.both(e, "Hull", "EngineL", "stealth_grey")
    m.removes += ["Hull/Nose", "Hull/Wings", "Hull/Tailplane", "Hull/Fin"]
    return m


{"tank": tank, "amphibian": amphibian, "apc": apc, "ship": ship, "helicopter": helicopter, "bomber": bomber}[ARGS[1]]().finish([ARGS[0]] + ARGS[2:])
