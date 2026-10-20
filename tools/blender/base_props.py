"""Things of the military base: fuel tanks, shipping containers, a watchtower, a radar, an army truck,
concrete barriers and sandbags.
    blender -b --python tools/blender/base_props.py -- <output dir> [all | name ...] [preview]
    python3 tools/blender/make_props.py <output dir>/props [godot binary] base
Writes one <name>.glb and <name>.json per prop; make_props.py with "base" turns them into scenes in
scenes/base/ (meshes in models/base/). Axes: X right, Y up, forward is -Z; a prop stands on Y = 0.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from meshkit import *  # noqa: E402
from vehicles_parts import tyre, hub  # noqa: E402

ARGS = sys.argv[sys.argv.index("--") + 1:]
SEAMS = dict(margin=0.05, width=0.012, depth=0.014, min_area=0.4)
ALL = []
OLIVE, CANVAS, WHITE = "base_olive", "base_canvas", "base_white"


def prop(name, kind, **spec):
    m = Model(name, None)
    m.spec = dict(kind=kind, **spec)
    ALL.append(m)
    return m


def ladder(p, x, z, y0, y1, out=(0, 0, -1), width=0.45, kind=FRAME):
    """Two rails and rungs up a face; out: which way the face looks."""
    ox, _, oz = out
    sx, sz = -oz, ox   # across the face
    for s in (-1, 1):
        p.cyl((x + sx * s * width / 2, y0, z + sz * s * width / 2), (x + sx * s * width / 2, y1, z + sz * s * width / 2), 0.03, segs=6, kind=kind, bevel=0)
    n = int((y1 - y0) / 0.32)
    for i in range(1, n):
        y = y0 + i * (y1 - y0) / n
        p.cyl((x - sx * width / 2, y, z - sz * width / 2), (x + sx * width / 2, y, z + sz * width / 2), 0.018, segs=5, kind=kind, bevel=0)


# ── Fuel tank ─────────────────────────────────────────────────────────────────────────────────────

m = prop("fuel_tank", "car", hp=30, mode=2, footprint=(6.6, 6.6), height=7.4, shape=(6.2, 7.0, 6.2), fx="explosion")
t = Part("Tank")
R = 3.0
# the shell on its ring footing, a low cone of a roof
t.cyl((0, 0, 0), (0, 0.35, 0), R + 0.25, segs=32, kind=FRAME)
t.cyl((0, 0.3, 0), (0, 6.3, 0), R, segs=32, kind=PAINT)
t.lathe((0, 6.25, 0), (0, 7.0, 0), [(0, R + 0.06), (0.25, R * 0.8), (0.8, 0.5), (1, 0.3)], segs=32, kind=PAINT)
# welded bands round the shell
for y in (1.8, 3.3, 4.8):
    t.cyl((0, y - 0.03, 0), (0, y + 0.03, 0), R + 0.015, segs=32, kind=EDGE, bevel=0)
m.add(t, ".", "Shell", WHITE)
f = Part("Fittings")
# a ladder up the side to a railed landing on the roof's edge, a pipe and valve at the foot
ladder(f, 0, -(R + 0.25), 0.35, 6.4, out=(0, 0, -1))
f.box((0, 6.38, -(R - 0.2)), (1.4, 0.08, 1.0), FRAME, 0.01)
for s in (-1, 1):
    f.cyl((s * 0.68, 6.4, -(R - 0.6)), (s * 0.68, 7.4, -(R - 0.6)), 0.03, segs=6, kind=FRAME, bevel=0)
    f.cyl((s * 0.68, 6.4, -(R + 0.25)), (s * 0.68, 7.4, -(R + 0.25)), 0.03, segs=6, kind=FRAME, bevel=0)
    f.cyl((s * 0.68, 7.4, -(R + 0.25)), (s * 0.68, 7.4, -(R - 0.6)), 0.03, segs=6, kind=FRAME, bevel=0)
f.cyl((R - 0.05, 0.7, 0), (R + 1.4, 0.7, 0), 0.16, segs=12, kind=FRAME)
f.cyl((R + 1.4, 0.7, 0), (R + 1.4, 0.0, 0), 0.16, segs=12, kind=FRAME)
f.cyl((R + 0.6, 0.7, -0.25), (R + 0.6, 0.7, 0.25), 0.26, segs=12, kind=SOFT)
f.cyl((R + 0.6, 0.96, 0), (R + 0.6, 1.2, 0), 0.03, segs=6, kind=EDGE, bevel=0)
f.cyl((R + 0.6, 1.2, -0.2), (R + 0.6, 1.2, 0.2), 0.03, segs=6, kind=EDGE, bevel=0)
m.add(f, ".", "Fittings", STEEL)


# ── Shipping containers ───────────────────────────────────────────────────────────────────────────

def container(name, paint):
    L, W, H = 6.06, 2.44, 2.59
    m = prop(name, "car", hp=22, mode=3, footprint=(W, L), height=H, shape=(W, H, L))
    b = Part("Box")
    b.box((0, H / 2, 0), (W - 0.1, H - 0.1, L - 0.12), PAINT, 0.01)
    # corrugated sides: ribs pressed in and out along the length
    n = 34
    for s in (-1, 1):
        for i in range(n):
            z = -L / 2 + 0.25 + (i + 0.5) * (L - 0.5) / n
            b.box((s * (W / 2 - 0.035), H / 2, z), (0.04, H - 0.32, 0.07), PAINT, 0)
    for i in range(14):
        x = -W / 2 + 0.3 + (i + 0.5) * (W - 0.6) / 14
        b.box((x, H / 2, -(L / 2 - 0.045)), (0.07, H - 0.32, 0.04), PAINT, 0)
    m.add(b, ".", "Box", paint)
    fr = Part("Frame")
    # corner posts and castings, top and bottom rails
    for sx in (-1, 1):
        for sz in (-1, 1):
            fr.box((sx * (W / 2 - 0.08), H / 2, sz * (L / 2 - 0.08)), (0.16, H, 0.16), FRAME, 0.01)
            for y in (0.09, H - 0.09):
                fr.box((sx * (W / 2 - 0.09), y, sz * (L / 2 - 0.09)), (0.2, 0.18, 0.2), FRAME, 0.01)
        for y in (0.08, H - 0.08):
            fr.box((sx * (W / 2 - 0.07), y, 0), (0.14, 0.16, L - 0.2), FRAME, 0.01)
    for sz in (-1, 1):
        for y in (0.08, H - 0.08):
            fr.box((0, y, sz * (L / 2 - 0.07)), (W - 0.2, 0.16, 0.14), FRAME, 0.01)
    # the doors at the back: four lock bars with their handles
    for x in (-0.85, -0.35, 0.35, 0.85):
        fr.cyl((x, 0.2, L / 2 - 0.01), (x, H - 0.2, L / 2 - 0.01), 0.025, segs=6, kind=EDGE, bevel=0)
        fr.box((x + 0.08, 1.2, L / 2 + 0.02), (0.16, 0.05, 0.04), EDGE, 0.005)
    fr.box((0, H / 2, L / 2 - 0.02), (0.04, H - 0.3, 0.03), RECESS, 0)
    m.add(fr, ".", "Frame", paint)
    return m


container("container_red", "container_red")
container("container_blue", "container_blue")
container("container_green", "container_green")


# ── Watchtower ────────────────────────────────────────────────────────────────────────────────────

m = prop("watchtower", "street", hp=12, mode=0, footprint=(3.2, 3.2), height=11.0)
w = Part("Frame")
top = 7.6
for sx in (-1, 1):
    for sz in (-1, 1):
        w.cyl((sx * 1.55, 0, sz * 1.55), (sx * 1.15, top, sz * 1.15), 0.09, segs=8, kind=FRAME)
        w.box((sx * 1.55, 0.15, sz * 1.55), (0.5, 0.3, 0.5), FRAME, 0.02)   # footings
# cross braces on each face, two storeys of them
for y0, y1, k0, k1 in ((0.4, 3.8, 1.53, 1.35), (3.8, 7.2, 1.35, 1.17)):
    for s in (-1, 1):
        w.cyl((-k0, y0, s * k0), (k1, y1, s * k1), 0.045, segs=6, kind=FRAME, bevel=0)
        w.cyl((k0, y0, s * k0), (-k1, y1, s * k1), 0.045, segs=6, kind=FRAME, bevel=0)
        w.cyl((s * k0, y0, -k0), (s * k1, y1, k1), 0.045, segs=6, kind=FRAME, bevel=0)
        w.cyl((s * k0, y0, k0), (s * k1, y1, -k1), 0.045, segs=6, kind=FRAME, bevel=0)
ladder(w, 0.0, 1.6, 0.0, top, out=(0, 0, 1), width=0.5)
m.add(w, ".", "Frame", STEEL)
c = Part("Cabin")
# the floor, a waist-high wall round it, corner posts and a pyramid roof
c.box((0, top + 0.08, 0), (3.0, 0.16, 3.0), PAINT, 0.02)
for s in (-1, 1):
    c.box((s * 1.45, top + 0.7, 0), (0.08, 1.1, 3.0), PAINT, 0.01, SEAMS)
    c.box((0, top + 0.7, s * 1.45), (2.84, 1.1, 0.08), PAINT, 0.01, SEAMS)
    for t2 in (-1, 1):
        c.box((s * 1.42, top + 1.9, t2 * 1.42), (0.12, 1.4, 0.12), PAINT, 0.01)
c.loft([[(x * k, top + 2.6 + y, z * k) for x, z in ((-1, -1), (1, -1), (1, 1), (-1, 1))] for k, y in ((1.75, 0.0), (1.75, 0.12), (0.15, 1.0))], kind=PAINT, bevel=0.01)
m.add(c, ".", "Cabin", OLIVE)
l = Part("Lamp")
l.cyl((1.3, top + 1.3, -1.3), (1.5, top + 1.3, -1.6), 0.18, 0.22, segs=10, kind=FRAME)
m.add(l, ".", "Searchlight", STEEL)


# ── Radar ─────────────────────────────────────────────────────────────────────────────────────────

m = prop("radar", "car", hp=18, mode=2, footprint=(3.6, 3.0), height=8.5, shape=(3.4, 3.0, 2.6), fx="explosion_medium")
r = Part("Station")
# the equipment cabin, a lattice mast off its roof, the drive head
r.box((0, 1.4, 0), (3.4, 2.6, 2.6), PAINT, 0.04, SEAMS)
r.box((0, 0.08, 0), (3.6, 0.16, 2.8), FRAME, 0.02)
r.slats((1.71, 1.6, 0), (1, 0, 0), (0, 0, 1), (1.4, 0.9), 6, 0.04)
r.box((-0.6, 1.2, -1.31), (0.9, 1.9, 0.04), RECESS, 0)    # door
m.add(r, ".", "Cabin", OLIVE)
mast = Part("Mast")
for sx in (-1, 1):
    for sz in (-1, 1):
        mast.cyl((sx * 0.45, 2.7, sz * 0.45), (sx * 0.18, 5.6, sz * 0.18), 0.05, segs=6, kind=FRAME, bevel=0)
for y in (3.4, 4.2, 5.0):
    k = 0.45 - (y - 2.7) / 2.9 * 0.27
    for s in (-1, 1):
        mast.cyl((-k, y, s * k), (k, y, s * k), 0.03, segs=5, kind=FRAME, bevel=0)
        mast.cyl((s * k, y, -k), (s * k, y, k), 0.03, segs=5, kind=FRAME, bevel=0)
mast.cyl((0, 5.55, 0), (0, 6.1, 0), 0.32, segs=14, kind=FRAME)
m.add(mast, ".", "Mast", STEEL)
d = Part("Dish")
# a shallow mesh dish, tipped up, on its yoke, with the feed horn on struts in front of it
tilt = Matrix.Rotation(math.radians(-20), 3, "X")
centre = Vector((0, 6.9, 0.2))
rim = []
for i in range(24):
    a = i * math.tau / 24
    rim.append((math.cos(a) * 2.2, math.sin(a) * 1.0))
d.loft([[tuple(centre + tilt @ Vector((x * k, y * k, dz))) for x, y in rim] for k, dz in ((1.0, 0.0), (0.97, -0.06), (0.55, -0.4), (0.12, -0.55))], kind=PAINT, bevel=0, caps=True)
d.cyl(tuple(centre + tilt @ Vector((0, 0, -0.5))), (0, 6.05, 0), 0.12, segs=8, kind=FRAME, bevel=0)
horn = centre + tilt @ Vector((0, 0, 1.3))
for x, y in ((-1.4, 0), (1.4, 0), (0, 0.8)):
    d.cyl(tuple(centre + tilt @ Vector((x, y, -0.05))), tuple(horn), 0.025, segs=5, kind=FRAME, bevel=0)
d.cyl(tuple(horn), tuple(horn + tilt @ Vector((0, 0, -0.25))), 0.1, 0.16, segs=10, kind=SOFT)
m.add(d, ".", "Dish", WHITE)


# ── Army truck ────────────────────────────────────────────────────────────────────────────────────

m = prop("army_truck", "car", hp=10, mode=3, footprint=(2.6, 8.0), height=3.3, shape=(2.5, 2.6, 7.8))
L, W = 7.8, 2.45
b = Part("Body")
# the cab, square with a flat windscreen, and the load bed behind it
b.box((0, 1.0, -L / 2 + 0.55), (W, 0.7, 1.1), PAINT, 0.04)                       # bonnet
b.stations("z", [(-L / 2 + 1.0, W, 1.5, 0, 1.9, 0.06), (-L / 2 + 2.6, W, 1.5, 0, 1.9, 0.06)], seams=SEAMS)
b.box((0, 0.95, 0.9), (W, 0.25, L - 2.6), FRAME, 0.02)                           # bed floor
for s in (-1, 1):
    b.box((s * (W / 2 - 0.03), 1.4, 0.9), (0.06, 0.65, L - 2.6), PAINT, 0.01, SEAMS)   # drop sides
b.box((0, 1.4, L / 2 - 0.03), (W, 0.65, 0.06), PAINT, 0.01)
m.add(b, ".", "Body", OLIVE)
cv = Part("Cover")
# the canvas tilt over the bed on its hoops
arch = [(-1.2, 0.0), (-1.18, 0.9), (-0.9, 1.35), (0.0, 1.5), (0.9, 1.35), (1.18, 0.9), (1.2, 0.0)]
cv.loft([[(x, 1.72 + y, z) for x, y in arch + [(1.12, 0.0), (1.1, 0.88), (0.85, 1.28), (0.0, 1.42), (-0.85, 1.28), (-1.1, 0.88), (-1.12, 0.0)]] for z in (-L / 2 + 2.62, L / 2 - 0.05)], kind=PAINT, bevel=0)
m.add(cv, ".", "Cover", CANVAS)
g = Part("Glass")
g.box((0, 2.15, -L / 2 + 0.98), (W - 0.3, 0.7, 0.04), PAINT, 0)
for s in (-1, 1):
    g.box((s * (W / 2 + 0.005), 2.15, -L / 2 + 1.7), (0.02, 0.6, 0.9), PAINT, 0)
m.add(g, ".", "Glass", GLASS)
tr = Part("Trim")
tr.box((0, 0.75, -L / 2 + 0.02), (W + 0.1, 0.25, 0.14), FRAME, 0.02)            # bumper
tr.box((0, 1.05, -L / 2 + 0.005), (1.2, 0.5, 0.03), RECESS, 0)                   # grille
for s in (-1, 1):
    tr.box((s * 0.95, 1.25, -L / 2 - 0.01), (0.22, 0.16, 0.04), SOFT, 0.01)      # lamps
    tr.box((s * (W / 2 + 0.12), 2.2, -L / 2 + 1.15), (0.06, 0.3, 0.18), FRAME, 0.01)   # mirrors
    tr.box((s * 0.9, 0.7, -L / 2 + 2.9), (0.6, 0.5, 1.0), FRAME, 0.02)           # tool box / tank
    for z in (-L / 2 + 1.3, L / 2 - 2.4, L / 2 - 1.1):
        tr.merge(hub("h", 0.55, 0.36, out=s).move((s * (W / 2 - 0.15), 0.55, z)))
tr.box((0, 0.55, 0.2), (0.3, 0.25, L - 1.0), FRAME, 0.01)                         # chassis
m.add(tr, ".", "Trim", JOINT)
wh = Part("Wheels")
for s in (-1, 1):
    for z in (-L / 2 + 1.3, L / 2 - 2.4, L / 2 - 1.1):
        wh.merge(tyre("t", 0.55, 0.38).move((s * (W / 2 - 0.15), 0.55, z)))
m.add(wh, ".", "Wheels", TYRE)


# ── Barriers ──────────────────────────────────────────────────────────────────────────────────────

m = prop("jersey_barrier", "street", hp=14, mode=2, footprint=(0.7, 3.0), height=0.85)
j = Part("Block")
profile = [(-0.3, 0.0), (0.3, 0.0), (0.3, 0.08), (0.2, 0.3), (0.09, 0.82), (-0.09, 0.82), (-0.2, 0.3), (-0.3, 0.08)]
j.loft([[(x, y, z) for x, y in profile] for z in (-1.5, 1.5)], kind=PAINT, bevel=0.02)
for z in (-1.0, 1.0):
    j.box((0, 0.05, z), (0.62, 0.1, 0.25), RECESS, 0)    # lifting slots at the foot
m.add(j, ".", "Block", CONCRETE)

m = prop("sandbags", "street", hp=10, mode=2, footprint=(0.9, 3.2), height=1.1)
sb = Part("Bags")
# three courses of bags laid like bricks, each a little soft-cornered pillow
for row in range(3):
    n = 5 if row % 2 == 0 else 4
    for i in range(n):
        z = -1.2 + (i + (0 if row % 2 == 0 else 0.5)) * 0.6
        y = 0.16 + row * 0.3
        sb.stations("z", [(z - 0.28, 0.5, 0.18, 0, y, 0.07), (z - 0.22, 0.6, 0.3, 0, y, 0.11), (z + 0.22, 0.6, 0.3, 0, y, 0.11), (z + 0.28, 0.5, 0.18, 0, y, 0.07)], bevel=0.02)
m.add(sb, ".", "Bags", CANVAS)


for m in ALL:
    if len(ARGS) < 2 or ARGS[1] == "all" or m.name in ARGS[1:]:
        m.finish([os.path.join(ARGS[0], "props")] + (["preview"] if "preview" in ARGS else []))
