"""Models for the scenes that used to stand on Godot's primitive meshes: each is built round its own
middle in the very size of the box (or cylinder) it replaces, so the scenes keep their nodes, their
transforms and their scripts — the field repair bay, a lamp's lens, a track's link, the menu's turntable,
the rubble a building leaves, the round roof of a hangar, the pads of the base.
    blender -b --python tools/blender/kit.py -- <out dir>
    godot --headless --path . -s tools/blender/import_parts.gd -- <out>/kit.glb res://models/kit
Game axes, metres."""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bmesh  # noqa: E402
from mathutils import Vector  # noqa: E402
from meshkit import *  # noqa: E402

FINE = dict(margin=0.05, width=0.012, depth=0.012, min_area=0.1)


def corrugated(p, axis, length, width, height, pitch=0.3, depth=0.07, centre=(0, 0, 0)):
    """A sheet of corrugated steel standing up: its waves run up it, across 'width', 'length' along axis
    ('z' — the sheet faces ±X; 'x' — it faces ±Z); thickness 'depth'."""
    n = max(4, int(width / pitch))
    outline = []
    for i in range(n + 1):
        u = -width / 2 + width * i / n
        outline.append((u, depth / 2 * (1 if i % 2 == 0 else -1)))
    back = [(u, v - 0.02) for u, v in reversed(outline)]
    poly = outline + back
    cx, cy, cz = centre
    secs = []
    for y in (-height / 2, height / 2):
        if axis == "z":
            secs.append([(cx + v, cy + y, cz + u) for u, v in poly])
        else:
            secs.append([(cx + u, cy + y, cz + v) for u, v in poly])
    p.loft(secs, PAINT, 0.0)


def repair_bay():
    out = []
    # the pad: a concrete slab with chamfered edges, a drainage channel with its grate, tie-down rings
    pad = Part("bay_pad")
    pad.stations("y", [(-0.25, 17.8, 17.8, 0, 0, 0.12), (0.25, 18.0, 18.0, 0, 0, 0.12)], seams=dict(margin=0.6, width=0.03, depth=0.02, min_area=4.0))
    pad.slats((0, 0.25, 0), (0, 1, 0), (1, 0, 0), (14.0, 1.0), 14, 0.025)
    for x in (-6, 6):
        for z in (-6, 6):
            with pad.detail((x, 0.25, z), (0, 1, 0)):
                pad.lathe((x, 0.24, z), (x, 0.3, z), [(0, 0.22), (0.5, 0.22), (1, 0.18)], segs=12, kind=FRAME)
                pad.lathe((x - 0.14, 0.33, z), (x + 0.14, 0.33, z), [(0, 0.03), (0.5, 0.045), (1, 0.03)], segs=8, kind=EDGE)
    out.append(pad)
    stripe = Part("bay_stripe")
    stripe.stations("x", [(-8.2, 0.48, 0.06, 0, 0, 0.012), (8.2, 0.48, 0.06, 0, 0, 0.012)])
    out.append(stripe)
    # walls: corrugated sheeting on a frame of steel columns and rails
    side = Part("bay_side")
    corrugated(side, "z", 19.0, 18.6, 14.6)
    for z in (-9.3, -3.1, 3.1, 9.3):
        side.stations("y", [(-7.5, 0.5, 0.4, 0.05, z, 0.05), (7.5, 0.5, 0.4, 0.05, z, 0.05)], kind=FRAME)
    for y in (-7.2, 0.0, 7.2):
        side.stations("z", [(-9.5, 0.45, 0.3, 0.1, y, 0.04), (9.5, 0.45, 0.3, 0.1, y, 0.04)], kind=FRAME)
    out.append(side)
    back = Part("bay_back")
    corrugated(back, "x", 19.8, 19.4, 14.6)
    for x in (-9.6, -3.2, 3.2, 9.6):
        back.stations("y", [(-7.5, 0.4, 0.5, x, 0.05, 0.05), (7.5, 0.4, 0.5, x, 0.05, 0.05)], kind=FRAME)
    for y in (-7.2, 0.0, 7.2):
        back.stations("x", [(-9.9, 0.3, 0.45, 0.1, y, 0.04), (9.9, 0.3, 0.45, 0.1, y, 0.04)], kind=FRAME)
    out.append(back)
    roof = Part("bay_roof")
    for k in range(-5, 6):                                                  # ribbed roof sheets, a fascia round them
        roof.stations("z", [(-9.9, 1.6, 0.18, k * 1.85, 0.05, 0.05), (9.9, 1.6, 0.18, k * 1.85, 0.05, 0.05)], seams=FINE)
        roof.stations("z", [(-9.9, 0.12, 0.12, k * 1.85 + 0.85, 0.18, 0.03), (9.9, 0.12, 0.12, k * 1.85 + 0.85, 0.18, 0.03)], kind=FRAME)
    for s in (-1, 1):
        roof.stations("x", [(-10.3, 0.25, 0.6, s * 9.8, 0, 0.04), (10.3, 0.25, 0.6, s * 9.8, 0, 0.04)], kind=FRAME)
        roof.stations("z", [(-9.9, 0.6, 0.25, s * 10.2, 0, 0.04), (9.9, 0.6, 0.25, s * 10.2, 0, 0.04)], kind=FRAME)
    out.append(roof)
    door = Part("bay_door")
    door.stations("z", [(-0.2, 9.5, 14.4, 0, 0, 0.08), (0.2, 9.5, 14.4, 0, 0, 0.08)], kind=FRAME)
    for col in range(3):
        for row in range(4):
            cx, cy = -3.1 + col * 3.1, -5.4 + row * 3.6
            door.stations("z", [(0.18, 2.8, 3.3, cx, cy, 0.06), (0.26, 2.6, 3.1, cx, cy, 0.06)], seams=FINE)
    for s in (-1, 1):
        door.stations("z", [(0.18, 0.2, 14.0, s * 4.6, 0, 0.03), (0.3, 0.2, 14.0, s * 4.6, 0, 0.03)], kind=FRAME)
        door.lathe((s * 3.5, 7.0, -0.25), (s * 3.5, 7.0, 0.25), [(0, 0.24), (0.15, 0.3), (0.85, 0.3), (1, 0.24)], segs=14, kind=EDGE)   # rollers
    out.append(door)
    crate = Part("bay_crate")
    crate.stations("y", [(-0.9, 2.3, 3.5, 0, 0, 0.06), (0.75, 2.3, 3.5, 0, 0, 0.06)], seams=FINE)
    crate.stations("y", [(0.72, 2.4, 3.6, 0, 0, 0.08), (0.9, 2.36, 3.56, 0, 0, 0.08)], kind=FRAME)          # lid
    for sx in (-1, 1):
        for sz in (-1, 1):
            crate.stations("y", [(-0.9, 0.16, 0.16, sx * 1.12, sz * 1.72, 0.03), (0.85, 0.16, 0.16, sx * 1.12, sz * 1.72, 0.03)], kind=FRAME)
        for z in (-0.6, 0.6):
            crate.stations("y", [(-0.85, 0.08, 0.14, sx * 1.16, z, 0.02), (0.7, 0.08, 0.14, sx * 1.16, z, 0.02)], kind=FRAME)
            crate.lathe((sx * 1.2, 0.3, z - 0.18), (sx * 1.2, 0.3, z + 0.18), [(0, 0.03), (0.5, 0.04), (1, 0.03)], segs=6, kind=EDGE)   # handles
    out.append(crate)
    lamp = Part("bay_lamp")
    lamp.lathe((0, -0.15, 0), (0, 0.15, 0), [(0, 0.45), (0.2, 0.45), (0.3, 0.38), (0.6, 0.34), (0.85, 0.24), (1, 0.08)], segs=16, kind=PAINT)
    for i in range(4):
        a = i * math.pi / 2
        lamp.cyl((math.cos(a) * 0.4, -0.1, math.sin(a) * 0.4), (math.cos(a) * 0.2, 0.15, math.sin(a) * 0.2), 0.018, segs=6, kind=FRAME)   # cage
    out.append(lamp)
    post = Part("bay_post")
    post.stations("y", [(-6.5, 0.7, 0.7, 0, 0, 0.08), (6.5, 0.66, 0.66, 0, 0, 0.08)], kind=FRAME, seams=FINE)
    out.append(post)
    beam_ = Part("bay_beam")
    beam_.stations("x", [(-9.7, 0.8, 0.8, 0, 0, 0.08), (9.7, 0.8, 0.8, 0, 0, 0.08)], kind=FRAME, seams=FINE)
    out.append(beam_)
    return out


def small_bits():
    out = []
    lens = Part("headlight_lens")
    # (a cylinder 0.16 round and 0.05 deep, lying along Y in the scene)
    lens.lathe((0, -0.025, 0), (0, 0.025, 0), [(0, 0.16), (0.3, 0.16), (0.35, 0.135), (0.7, 0.13), (1, 0.09)], segs=16, kind=PAINT)
    out.append(lens)
    link = Part("track_link")
    # a track shoe 0.6 wide, 0.07 thick, 0.25 long: the plate, a grouser across it, hinge lugs at its ends
    link.stations("x", [(-0.3, 0.22, 0.045, 0, -0.012, 0.012), (0.3, 0.22, 0.045, 0, -0.012, 0.012)])
    link.stations("x", [(-0.29, 0.05, 0.035, 0, 0.022, 0.012), (0.29, 0.04, 0.035, 0, 0.022, 0.01)], kind=FRAME)
    for x in (-0.22, -0.08, 0.08, 0.22):
        link.lathe((x - 0.05, -0.012, 0.11), (x + 0.05, -0.012, 0.11), [(0, 0.022), (0.2, 0.028), (0.8, 0.028), (1, 0.022)], segs=8, kind=EDGE)
    out.append(link)
    ped = Part("menu_pedestal")
    # the hangar's turntable, 7–7.5 m round and 0.3 deep
    ped.lathe((0, -0.15, 0), (0, 0.15, 0), [(0, 7.5), (0.4, 7.45), (0.5, 7.3), (0.62, 7.3), (0.7, 7.15), (0.9, 7.05), (1, 6.9)], segs=72, kind=FRAME)
    ped.lathe((0, 0.149, 0), (0, 0.16, 0), [(0, 6.2), (1, 6.2)], segs=72, kind=PAINT)
    ped.bolts([(math.cos(i * math.tau / 24) * 6.6, 0.15, math.sin(i * math.tau / 24) * 6.6) for i in range(24)], (0, 1, 0), r=0.06, h=0.03)
    out.append(ped)
    return out


def rubble():
    """Unit-sized heaps and slabs (the scenes scale them to the size of the box they replace)."""
    out = []
    rng = random.Random(7)
    heap = Part("rubble_heap")
    # a low mound of broken masonry: a lumpy dome, with lumps sitting on it
    rings = 8
    segs = 20
    secs = []
    for r_ in range(rings + 1):
        t = r_ / rings
        rad = 0.5 * math.cos(t * math.pi / 2) + 0.001
        y = -0.5 + math.sin(t * math.pi / 2)
        sec = []
        for i in range(segs):
            a = i * math.tau / segs
            j = 1.0 + (rng.random() - 0.5) * 0.25 * (1.0 if 0 < r_ < rings else 0.0)
            sec.append((math.cos(a) * rad * j, y * (1.0 + (rng.random() - 0.5) * 0.12 * (1.0 if 0 < r_ else 0.0)), math.sin(a) * rad * j))
        secs.append(sec)
    heap.loft(secs, PAINT, 0.0)
    for _ in range(22):
        a, rr = rng.random() * math.tau, rng.random() * 0.4
        cx, cz = math.cos(a) * rr, math.sin(a) * rr
        cy = -0.5 + math.cos(rr / 0.5 * math.pi / 2) * 0.85
        s = 0.06 + rng.random() * 0.1
        pts = [(cx + (rng.random() - 0.5) * s * 2, cy + (rng.random() - 0.3) * s, cz + (rng.random() - 0.5) * s * 2) for _ in range(4)]
        heap.loft([[pts[0], pts[1], pts[2]], [pts[3], pts[3], pts[3]]], PAINT, 0.0)
    out.append(heap)
    slab = Part("rubble_slab")
    # a broken slab: a ragged outline, its top cracked across, ends of rebar sticking out of a broken edge
    outline = []
    n = 14
    for i in range(n):
        a = i * math.tau / n
        k = 0.82 + rng.random() * 0.18
        outline.append((max(-0.5, min(0.5, math.cos(a) * 0.62 * k)), max(-0.5, min(0.5, math.sin(a) * 0.62 * k))))
    slab.loft([[(u, -0.5, v) for u, v in outline], [(u * 0.97, 0.5, v * 0.97) for u, v in outline]], PAINT, 0.0)
    for i in range(4):
        z = -0.3 + i * 0.2
        slab.cyl((0.45, 0.0, z), (0.62 + rng.random() * 0.1, 0.05 + rng.random() * 0.2, z + 0.03), 0.012, segs=5, kind=EDGE)
    out.append(slab)
    return out


def hangar_roof():
    """The round roof of a hangar, as a unit: an arch 1 across (radius 0.5) and 1 long, its sheets ribbed."""
    roof = Part("hangar_roof")
    segs = 32
    outer, inner = 0.5, 0.485
    poly = []
    for i in range(segs + 1):
        a = math.pi * i / segs
        k = 1.0 + 0.008 * (1 if i % 2 == 0 else -1)                   # the corrugations, along it
        poly.append((math.cos(a) * outer * k, math.sin(a) * outer * k))
    for i in range(segs, -1, -1):
        a = math.pi * i / segs
        poly.append((math.cos(a) * inner, math.sin(a) * inner))
    roof.loft([[(u, v, z) for u, v in poly] for z in (-0.5, 0.5)], PAINT, 0.0)
    for z in (-0.5, -0.25, 0.0, 0.25, 0.5):                            # ribs over it
        rib = []
        for i in range(segs + 1):
            a = math.pi * i / segs
            rib.append((math.cos(a) * (outer + 0.01), math.sin(a) * (outer + 0.01)))
        for i in range(segs, -1, -1):
            a = math.pi * i / segs
            rib.append((math.cos(a) * outer * 0.99, math.sin(a) * outer * 0.99))
        zz = max(-0.5, min(0.49, z))
        roof.loft([[(u, v, zz) for u, v in rib], [(u, v, zz + 0.01) for u, v in rib]], FRAME, 0.0)
    return [roof]


def arena_cover():
    """Cover for the arena floor, standing on its origin: a bulwark 12 long, 8 high and 2 thick, and a
    pillar 6 across and 6 high."""
    out = []
    w = Part("arena_bulwark")
    # the wall: battered faces, a capping beam, buttress fins either side, recessed panels
    w.stations("x", [(-6.0, 2.0, 8.0, 0, 4.0, 0.12, 0.05), (6.0, 2.0, 8.0, 0, 4.0, 0.12, 0.05)], seams=dict(margin=0.35, width=0.03, depth=0.03, min_area=2.0))
    w.stations("y", [(-0.2, 12.0, 2.6, 0, 0, 0.1), (0.6, 12.0, 2.4, 0, 0, 0.1)])                                  # footing
    for x in (-4.0, 0.0, 4.0):
        for sz in (-1, 1):
            w.stations("y", [(0.4, 0.7, 1.6, x, sz * 1.6, 0.08), (6.8, 0.6, 0.3, x, sz * 1.05, 0.06)], kind=PAINT)   # fins
    w.stations("x", [(-6.05, 2.3, 0.45, 0, 8.1, 0.06), (6.05, 2.3, 0.45, 0, 8.1, 0.06)], kind=FRAME)             # steel cap
    for x in (-5.0, -1.7, 1.7, 5.0):
        with w.detail((x, 8.33, 0), (0, 1, 0)):
            w.lathe((x - 0.18, 8.45, 0), (x + 0.18, 8.45, 0), [(0, 0.06), (0.5, 0.1), (1, 0.06)], segs=8, kind=EDGE)   # lifting eyes
    out.append(w)
    hz = Part("arena_bulwark_ends")
    for sx in (-1, 1):
        hz.stations("x", [(sx * 6.0 - sx * 0.02, 2.06, 7.6, 0, 3.9, 0.1), (sx * 6.06, 2.06, 7.6, 0, 3.9, 0.1)], kind=FRAME)
    out.append(hz)
    pl = Part("arena_pillar")
    # an octagonal pillar, battered at the foot, banded with steel, its top capped
    pl.stations("y", [(0.0, 6.0, 6.0, 0, 0, 1.6), (0.8, 5.8, 5.8, 0, 0, 1.55), (5.4, 5.2, 5.2, 0, 0, 1.4), (6.0, 5.0, 5.0, 0, 0, 1.35)], seams=dict(margin=0.3, width=0.03, depth=0.03, min_area=1.5))
    for y in (1.6, 4.4):
        k = 5.8 - (y - 0.8) / 4.6 * 0.6
        pl.stations("y", [(y - 0.18, k + 0.12, k + 0.12, 0, 0, 1.6 * k / 6 + 0.02), (y + 0.18, k + 0.12, k + 0.12, 0, 0, 1.6 * k / 6 + 0.02)], kind=FRAME)
    pl.stations("y", [(5.9, 5.3, 5.3, 0, 0, 1.45), (6.3, 4.6, 4.6, 0, 0, 1.25)], kind=FRAME)
    out.append(pl)
    return out


def fluted(part, base, height, r, flutes=16, kind=PAINT, broken=0.0, rng=None):
    """A fluted column shaft standing on base (with a slight entasis), optionally broken off raggedly at the top."""
    rows = []
    for k in range(7):
        t = k / 6
        rr = r * (1.0 - 0.12 * t + 0.04 * math.sin(t * math.pi))
        y = base[1] + height * t
        sec = []
        for i in range(flutes * 2):
            a = i * math.pi / flutes
            q = rr * (1.0 if i % 2 == 0 else 0.93)
            yy = y
            if broken and k == 6:
                yy -= broken * (0.5 + 0.5 * math.sin(a * 3.0 + 1.3)) * (0.6 + 0.4 * (rng.random() if rng else 0.5))
            sec.append((base[0] + math.cos(a) * q, yy, base[2] + math.sin(a) * q))
        rows.append(sec)
    part.loft(rows, kind, 0.0)


def arena_obstacles():
    """The arena's great obstacles, standing on their origins (Steiner's Coliseum's pylons and rising walls):
    a pylon, a rampart with arched openings, a bastion, a group of broken columns."""
    out = []
    rng = random.Random(11)
    # ── The pylon: a stepped plinth, a panelled tapering shaft with corner pilasters, a capital, a crown ──
    py = Part("arena_pylon")
    py.stations("y", [(0.0, 11.0, 11.0, 0, 0, 0.3), (1.2, 11.0, 11.0, 0, 0, 0.3)], seams=dict(margin=0.4, width=0.04, depth=0.04, min_area=3.0))
    py.stations("y", [(1.2, 9.0, 9.0, 0, 0, 0.25), (2.4, 9.0, 9.0, 0, 0, 0.25)])
    py.stations("y", [(2.4, 7.2, 7.2, 0, 0, 0.2), (3.4, 7.0, 7.0, 0, 0, 0.2)])
    py.stations("y", [(3.4, 6.0, 6.0, 0, 0, 0.6), (17.5, 4.6, 4.6, 0, 0, 0.45)], seams=dict(margin=0.35, width=0.05, depth=0.06, min_area=2.0))
    for sx in (-1, 1):
        for sz in (-1, 1):
            py.stations("y", [(3.4, 0.9, 0.9, sx * 2.75, sz * 2.75, 0.12), (17.5, 0.7, 0.7, sx * 2.1, sz * 2.1, 0.1)], kind=PAINT)       # pilasters
    py.stations("y", [(17.5, 5.0, 5.0, 0, 0, 0.4), (18.2, 5.6, 5.6, 0, 0, 0.4), (18.2, 6.4, 6.4, 0, 0, 0.45), (19.0, 6.6, 6.6, 0, 0, 0.45), (19.6, 5.4, 5.4, 0, 0, 0.35)], kind=PAINT)   # capital
    py.lathe((0, 19.5, 0), (0, 21.0, 0), [(0, 2.2), (0.2, 2.2), (0.3, 1.9), (0.8, 1.8), (1, 1.5)], segs=24, kind=FRAME)                   # lamp crown
    py.stations("y", [(21.0, 3.0, 3.0, 0, 0, 0.3), (24.0, 0.2, 0.2, 0, 0, 0.02)], kind=PAINT)                                          # cap
    for i in range(4):
        a = i * math.pi / 2
        with py.detail((math.cos(a) * 2.6, 10.0, math.sin(a) * 2.6), (math.cos(a), 0, math.sin(a))):
            py.stations("y", [(8.0, 1.6, 0.25, math.cos(a) * 2.75, math.sin(a) * 2.75, 0.05), (12.0, 1.6, 0.25, math.cos(a) * 2.75, math.sin(a) * 2.75, 0.05)] if i % 2 == 0 else
                        [(8.0, 0.25, 1.6, math.cos(a) * 2.75, math.sin(a) * 2.75, 0.05), (12.0, 0.25, 1.6, math.cos(a) * 2.75, math.sin(a) * 2.75, 0.05)], kind=FRAME)   # bronze shields
    out.append(py)
    glow = Part("arena_pylon_glow")
    glow.lathe((0, 19.9, 0), (0, 20.6, 0), [(0, 2.25), (1, 2.25)], segs=24, kind=PAINT)
    out.append(glow)

    # ── The rampart: 28 m of wall 3 thick and 10 high, three arched openings, buttresses, merlons ──
    rp = Part("arena_rampart")
    L, H, T = 28.0, 10.0, 3.0
    openings = [(-8.5, 2.6), (0.0, 2.6), (8.5, 2.6)]         # centre and half-width of each arch
    def underside(x):
        for cx, hw in openings:
            if abs(x - cx) < hw:
                s = (x - cx) / hw
                return 5.2 + 2.2 * math.sqrt(max(0.0, 1.0 - s * s))
        return 0.0
    xs = [-L / 2 + L * i / 112 for i in range(113)]
    for cx, hw in openings:
        xs += [cx - hw + 0.001, cx + hw - 0.001]
    xs = sorted(set(round(x, 4) for x in xs))
    secs = [[(x, underside(x), -T / 2), (x, H, -T / 2 + 0.25), (x, H, T / 2 - 0.25), (x, underside(x), T / 2)] for x in xs]
    rp.loft(secs, PAINT, 0.0)
    for cx, hw in openings:                                     # the archivolts round the openings, each side
        for sz in (-1, 1):
            arc = []
            for i in range(17):
                a = math.pi * i / 16
                arc.append((cx + math.cos(a) * hw, 5.2 + math.sin(a) * 2.2))
            outer = [(cx + (x - cx) * 1.18, 5.2 + (y - 5.2) * 1.25 + 0.3) for x, y in reversed(arc)]
            poly = arc + outer
            rp.loft([[(x, y, sz * (T / 2 - 0.05)) for x, y in poly], [(x, y, sz * (T / 2 + 0.35)) for x, y in poly]], PAINT, 0.0)
    for x in (-L / 2 + 0.8, -4.25, 4.25, L / 2 - 0.8):           # buttresses
        for sz in (-1, 1):
            rp.stations("y", [(0.0, 1.4, 2.6, x, sz * 2.4, 0.12), (6.5, 1.1, 0.5, x, sz * 1.5, 0.08)], kind=PAINT)
    rp.stations("x", [(-L / 2 - 0.3, T + 0.5, 0.7, 0, H + 0.2, 0.12), (L / 2 + 0.3, T + 0.5, 0.7, 0, H + 0.2, 0.12)], kind=PAINT)          # string course
    for i in range(10):                                         # merlons
        x = -L / 2 + 1.4 + i * (L - 2.8) / 9
        rp.stations("y", [(H + 0.5, 1.8, T + 0.2, x, 0, 0.1), (H + 2.4, 1.6, T, x, 0, 0.1)], kind=PAINT)
    out.append(rp)
    lift = Part("arena_rampart_lift")
    # the steel housing it rose out of, along its foot
    lift.stations("x", [(-L / 2 - 0.8, T + 1.6, 0.6, 0, 0.2, 0.08), (L / 2 + 0.8, T + 1.6, 0.6, 0, 0.2, 0.08)], kind=FRAME)
    for x in (-L / 2 - 0.4, L / 2 + 0.4):
        lift.stations("y", [(0.0, 0.8, T + 1.4, x, 0, 0.08), (11.0, 0.6, T + 1.0, x, 0, 0.08)], kind=FRAME)       # guide rails at its ends
    out.append(lift)

    # ── The bastion: an angle of wall with sloped faces, a parapet with embrasures ──
    bs = Part("arena_bastion")
    for axis in ("x", "z"):
        rows = [(0.0, 18.0, 4.0, 9.0 if axis == "x" else 0.0, 0.0), ]
        if axis == "x":
            bs.stations("x", [(-1.0, 4.5, 8.0, 0, 4.0, 0.3, 0.05), (18.0, 4.5, 8.0, 0, 4.0, 0.3, 0.05)], seams=dict(margin=0.4, width=0.04, depth=0.04, min_area=3.0))
            bs.stations("x", [(-1.0, 6.5, 1.6, 0, 0.8, 0.2), (18.5, 6.5, 1.6, 0, 0.8, 0.2)])                                                     # glacis foot
        else:
            bs.stations("z", [(-1.0, 4.5, 8.0, 0, 4.0, 0.3, 0.05), (18.0, 4.5, 8.0, 0, 4.0, 0.3, 0.05)], seams=dict(margin=0.4, width=0.04, depth=0.04, min_area=3.0))
            bs.stations("z", [(-1.0, 6.5, 1.6, 0, 0.8, 0.2), (18.5, 6.5, 1.6, 0, 0.8, 0.2)])
    for k in range(5):                                          # merlons with embrasures between them
        for axis, sign in (("x", 1), ("z", 1)):
            t = 1.0 + k * 3.8
            if axis == "x":
                bs.stations("y", [(8.0, 2.4, 4.8, t, 0, 0.12), (10.0, 2.2, 4.4, t, 0, 0.12)], kind=PAINT)
            else:
                bs.stations("y", [(8.0, 4.8, 2.4, 0, t, 0.12), (10.0, 4.4, 2.2, 0, t, 0.12)], kind=PAINT)
    bs.lathe((0, 7.9, 0), (0, 11.5, 0), [(0, 3.0), (0.1, 3.2), (0.3, 3.0), (0.9, 2.8), (1, 2.4)], segs=20, kind=PAINT)          # the corner tower
    bs.stations("y", [(11.5, 3.0, 3.0, 0, 0, 0.6), (13.5, 0.4, 0.4, 0, 0, 0.05)], kind=FRAME)
    out.append(bs)

    # ── Ruins: broken fluted columns on a cracked stylobate, a fallen drum, a toppled piece of entablature ──
    ru = Part("arena_ruins")
    ru.stations("y", [(0.0, 22.0, 9.0, 0, 0, 0.3), (1.5, 21.0, 8.4, 0, 0, 0.25)], seams=dict(margin=0.5, width=0.05, depth=0.05, min_area=3.0))
    for x, h, brk in ((-8.0, 16.0, 1.5), (-2.6, 9.0, 2.2), (2.8, 13.0, 1.8), (8.2, 4.5, 1.2)):
        ru.stations("y", [(1.5, 3.4, 3.4, x, 0, 0.15), (2.3, 3.2, 3.2, x, 0, 0.15)], kind=PAINT)       # plinth
        ru.lathe((x, 2.3, 0), (x, 3.0, 0), [(0, 1.5), (0.5, 1.55), (1, 1.35)], segs=20, kind=PAINT)     # torus base
        fluted(ru, (x, 3.0, 0), h, 1.3, 16, PAINT, brk, rng)
    for i, (cx, cz, rot) in enumerate(((-1.0, 7.0, 0.4), (5.5, -6.5, -0.9))):
        d = Vector((math.cos(rot), 0, math.sin(rot)))
        ru.lathe(tuple(Vector((cx, 1.25, cz)) - d * 1.4), tuple(Vector((cx, 1.25, cz)) + d * 1.4), [(0, 1.15), (0.5, 1.2), (1, 1.15)], segs=16, kind=PAINT)   # fallen drums
    ru.stations("x", [(-14.0, 3.0, 2.2, 6.5, 1.1, 0.15), (-6.0, 3.0, 2.2, 6.8, 1.2, 0.15)], kind=PAINT)                        # a toppled block of entablature
    out.append(ru)
    return out


def main():
    out = sys.argv[sys.argv.index("--") + 1]
    os.makedirs(out, exist_ok=True)
    parts = repair_bay() + small_bits() + rubble() + hangar_roof() + arena_cover() + arena_obstacles()
    scene = reset_scene()
    objs = []
    for p in parts:
        bmesh.ops.recalc_face_normals(p.bm, faces=list(p.bm.faces))
        objs.append(p.to_object(scene.collection))
    export_glb(os.path.join(out, "kit.glb"), objs)
    print("KIT", len(objs), [p.name for p in parts])


main()
