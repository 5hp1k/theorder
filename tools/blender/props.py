"""Things of the city: street furniture, fittings for buildings, civilian cars.
    blender -b --python tools/blender/props.py -- <output dir> [preview]
Writes one <name>.glb and <name>.json per prop; tools/blender/make_props.py turns them into scenes.
Axes: X right, Y up, forward is -Z. A street prop stands on Y = 0 and faces -Z... a fitting for a wall
has its origin on the wall and +Z pointing out of it; a car's nose points to -Z.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from meshkit import *  # noqa: E402
from vehicles_parts import tyre, hub  # noqa: E402

ARGS = sys.argv[sys.argv.index("--") + 1:]
SEAMS = dict(margin=0.04, width=0.01, depth=0.012, min_area=0.08)
ALL = []


def prop(name, kind, **spec):
    m = Model(name, None)
    m.spec = dict(kind=kind, **spec)
    ALL.append(m)
    return m


# ── Street furniture ─────────────────────────────────────────────────────────────────────────────

m = prop("street_lamp", "street", hp=6, mode=0, footprint=(0.6, 0.6), height=8.0)
p = Part("Post")
p.cyl((0, 0, 0), (0, 0.5, 0), 0.2, 0.14, segs=8, kind=FRAME)
p.cyl((0, 0.5, 0), (0, 7.4, 0), 0.1, 0.07, segs=8, kind=PAINT, bevel=0)
p.cyl((0, 7.3, 0), (0, 7.75, -1.0), 0.06, segs=6, kind=PAINT, bevel=0)
p.cyl((0, 7.75, -1.0), (0, 7.8, -2.4), 0.055, segs=6, kind=PAINT, bevel=0)
p.stations("z", [(-1.9, 0.3, 0.1, 0, 7.78, 0.03), (-3.0, 0.42, 0.16, 0, 7.8, 0.05)], kind=PAINT, bevel=0.015)
m.add(p, ".", "Post", STEEL)
g = Part("Glow")
g.box((0, 7.7, -2.5), (0.32, 0.04, 0.8), PAINT, 0)
m.add(g, ".", "Lamp", GLOW)

m = prop("traffic_light", "street", hp=6, mode=0, footprint=(0.6, 0.6), height=6.0)
p = Part("Post")
p.cyl((0, 0, 0), (0, 0.4, 0), 0.22, 0.14, segs=8, kind=FRAME)
p.cyl((0, 0.4, 0), (0, 5.6, 0), 0.09, segs=8, kind=PAINT, bevel=0)
p.cyl((0, 5.5, 0), (0, 5.6, -4.2), 0.07, segs=6, kind=PAINT, bevel=0)
for z, y in ((-4.0, 5.1), (0.0, 3.2)):
    p.box((0, y, z - 0.02), (0.36, 1.05, 0.3), FRAME, 0.02)
    for i in range(3):
        p.box((0, y + 0.33 - i * 0.33 + 0.13, z - 0.24), (0.3, 0.03, 0.2), FRAME, 0)  # hoods
m.add(p, ".", "Post", JOINT)
for name, i in (("Red", 0), ("Amber", 1), ("Green", 2)):
    l = Part("Lens" + name)
    for z, y in ((-4.0, 5.1), (0.0, 3.2)):
        l.cyl((0, y + 0.33 - i * 0.33, z - 0.165), (0, y + 0.33 - i * 0.33, z - 0.18), 0.11, segs=10, kind=PAINT, bevel=0)
    m.add(l, ".", name, "light_" + name.lower())


def sign(name, board, colour, back="white"):
    m = prop(name, "street", hp=3, mode=0, footprint=(0.5, 0.5), height=3.0)
    p = Part("Post")
    p.cyl((0, 0, 0), (0, 3.0, 0), 0.04, segs=6, kind=PAINT, bevel=0)
    m.add(p, ".", "Post", STEEL)
    b = Part("Board")
    board(b)
    m.add(b, ".", "Board", colour)
    return m


def octo(b):
    b.loft([[(x, 2.5 + y, z) for x, y in ring(8, 0.42, phase=math.pi / 8)] for z in (-0.05, -0.08)], bevel=0.006)


def disc(b):
    b.cyl((0, 2.5, -0.05), (0, 2.5, -0.08), 0.4, segs=16, kind=PAINT, bevel=0.005)


def plate(b):
    b.box((0, 2.5, -0.065), (1.1, 0.5, 0.03), PAINT, 0.006)


def triangle(b):
    b.loft([[(x, 2.4 + y, z) for x, y in ring(3, 0.5, phase=math.pi / 2)] for z in (-0.05, -0.08)], bevel=0.006)


sign("sign_stop", octo, "sign_red")
sign("sign_limit", disc, "sign_white")
sign("sign_way", plate, "sign_blue")
sign("sign_warn", triangle, "sign_yellow")

m = prop("hydrant", "street", hp=4, mode=2, footprint=(0.5, 0.5), height=1.0)
p = Part("Body")
p.lathe((0, 0, 0), (0, 0.9, 0), [(0, 0.16), (0.08, 0.16), (0.1, 0.12), (0.7, 0.12), (0.74, 0.15), (0.84, 0.14), (1.0, 0.03)], segs=10)
for s in (-1, 1):
    p.cyl((s * 0.1, 0.55, 0), (s * 0.22, 0.55, 0), 0.06, segs=8, kind=SOFT)
p.cyl((0, 0.5, -0.1), (0, 0.5, -0.24), 0.08, segs=8, kind=SOFT)
m.add(p, ".", "Body", "sign_red")

m = prop("bin", "street", hp=3, mode=2, footprint=(0.6, 0.6), height=1.1)
p = Part("Body")
p.lathe((0, 0.05, 0), (0, 1.0, 0), [(0, 0.24), (0.9, 0.3), (0.92, 0.33), (1.0, 0.33)], segs=10)
p.cyl((0, 1.0, 0), (0, 1.03, 0), 0.26, segs=10, kind=RECESS, bevel=0)
m.add(p, ".", "Body", STEEL)

m = prop("bench", "street", hp=4, mode=2, footprint=(2.0, 0.8), height=1.0)
p = Part("Frame")
for s in (-1, 1):
    p.box((s * 0.8, 0.25, 0), (0.08, 0.5, 0.5), FRAME, 0.01)
    p.box((s * 0.8, 0.75, 0.24), (0.08, 0.55, 0.06), FRAME, 0.01)
m.add(p, ".", "Frame", JOINT)
w = Part("Slats")
for i in range(4):
    w.box((0, 0.5, -0.18 + i * 0.13), (1.9, 0.04, 0.1), PAINT, 0.008)
for i in range(3):
    w.box((0, 0.68 + i * 0.14, 0.27), (1.9, 0.1, 0.04), PAINT, 0.008)
m.add(w, ".", "Slats", WOOD)

m = prop("bus_stop", "street", hp=8, mode=1, footprint=(3.4, 1.6), height=2.6)
p = Part("Frame")
for s in (-1, 1):
    p.box((s * 1.6, 1.25, 0.6), (0.08, 2.5, 0.08), PAINT, 0.01)
    p.box((s * 1.6, 1.25, -0.6), (0.08, 2.5, 0.08), PAINT, 0.01)
p.box((0, 2.52, 0), (3.5, 0.08, 1.6), PAINT, 0.02)
p.box((0, 0.48, 0.45), (2.6, 0.06, 0.4), PAINT, 0.01)
m.add(p, ".", "Frame", STEEL)
g = Part("Panes")
g.box((0, 1.3, 0.62), (3.1, 2.2, 0.03), PAINT, 0)
g.box((1.62, 1.3, 0), (0.03, 2.2, 1.1), PAINT, 0)
m.add(g, ".", "Glass", GLASS)

# ── Fittings for buildings (origin on the wall, +Z out of it) ─────────────────────────────────────

m = prop("door", "fitting")
p = Part("Steps")
p.box((0, 0.1, 0.5), (1.9, 0.2, 1.0), PAINT, 0.02)
p.box((0, 0.3, 0.3), (1.6, 0.2, 0.6), PAINT, 0.02)
p.box((0, 2.42, 0.45), (2.0, 0.1, 1.0), PAINT, 0.02)  # canopy
for s in (-1, 1):
    p.cyl((s * 0.9, 0.4, 0.85), (s * 0.9, 2.4, 0.85), 0.04, segs=6, kind=FRAME, bevel=0)
m.add(p, ".", "Steps", CONCRETE)
d = Part("Leaf")
d.box((0, 1.38, 0.03), (1.16, 2.04, 0.08), FRAME, 0.015)  # frame
d.box((0, 1.36, 0.06), (0.98, 1.92, 0.06), PAINT, 0.012, dict(margin=0.1, width=0.014, depth=0.016, min_area=0.5))
d.cyl((0.36, 1.3, 0.09), (0.36, 1.3, 0.14), 0.035, segs=8, kind=EDGE, bevel=0)
m.add(d, ".", "Leaf", WOOD)
g = Part("Lamp")
g.box((0.8, 2.1, 0.08), (0.14, 0.22, 0.12), PAINT, 0.01)
m.add(g, ".", "Lamp", GLOW)

m = prop("ac_unit", "fitting")
p = Part("Box")
p.box((0, 0.0, 0.2), (0.8, 0.52, 0.36), PAINT, 0.02, SEAMS)
p.cyl((0.05, 0.0, 0.38), (0.05, 0.0, 0.4), 0.2, segs=12, kind=RECESS, bevel=0)
for i in range(4):
    a = i * math.pi / 4
    p.box((0.05, 0.0, 0.405), (0.4, 0.02, 0.01), FRAME, 0, None, Matrix.Rotation(a, 3, "Z"))
for s in (-1, 1):
    p.box((s * 0.3, -0.32, 0.18), (0.04, 0.12, 0.36), FRAME, 0)
m.add(p, ".", "Box", STEEL)

m = prop("sat_dish", "fitting")
p = Part("Dish")
p.cyl((0, 0, 0), (0, 0.1, 0.3), 0.025, segs=6, kind=FRAME, bevel=0)
p.lathe((0, 0.1, 0.3), (0.0, 0.2, 0.42), [(0, 0.04), (0.5, 0.26), (1.0, 0.34)], segs=12)
p.cyl((0, 0.2, 0.42), (0, 0.36, 0.62), 0.012, segs=5, kind=FRAME, bevel=0)
m.add(p, ".", "Dish", STEEL)

# things for a flat roof (origin on the roof)
m = prop("roof_vent", "fitting")
p = Part("Box")
p.box((0, 0.5, 0), (2.4, 1.0, 1.6), PAINT, 0.03, SEAMS)
p.slats((0, 0.5, 0.81), (0, 0, 1), (1, 0, 0), (2.0, 0.7), 5, 0.05)
p.cyl((0.6, 1.0, 0), (0.6, 1.25, 0), 0.45, segs=12, kind=FRAME)
p.cyl((0.6, 1.25, 0), (0.6, 1.27, 0), 0.38, segs=12, kind=RECESS, bevel=0)
p.cyl((-0.8, 1.0, 0.3), (-0.8, 1.6, 0.3), 0.12, segs=8, kind=FRAME)
m.add(p, ".", "Box", STEEL)

m = prop("roof_tank", "fitting")
p = Part("Tank")
for x, z in ((-0.8, -0.8), (0.8, -0.8), (-0.8, 0.8), (0.8, 0.8)):
    p.cyl((x, 0, z), (x * 0.9, 1.5, z * 0.9), 0.06, segs=6, kind=FRAME, bevel=0)
p.lathe((0, 1.4, 0), (0, 4.0, 0), [(0, 1.15), (0.02, 1.2), (0.85, 1.2), (0.9, 1.1), (1.0, 0.1)], segs=14, kind=PAINT)
for y in (1.9, 2.7):
    p.cyl((0, y, 0), (0, y + 0.08, 0), 1.23, segs=14, kind=FRAME, bevel=0)
m.add(p, ".", "Tank", STEEL)

m = prop("roof_mast", "fitting")
p = Part("Mast")
p.box((0, 0.2, 0), (0.6, 0.4, 0.6), FRAME, 0.02)
p.cyl((0, 0.4, 0), (0, 6.5, 0), 0.06, 0.03, segs=6, kind=FRAME, bevel=0)
for y in (3.5, 4.5, 5.5):
    p.box((0, y, 0), (1.4 - (y - 3.5) * 0.3, 0.04, 0.04), FRAME, 0)
p.lathe((0.3, 2.4, 0), (0.3, 2.5, 0.3), [(0, 0.05), (0.5, 0.3), (1, 0.4)], segs=10, kind=PAINT)
m.add(p, ".", "Mast", STEEL)

# parts of a bridge
m = prop("bridge_rail", "fitting")
p = Part("Rail")
p.box((0, 0.25, -0.2), (2.0, 0.5, 0.3), PAINT, 0.03)  # kerb block (origin on the deck's edge, +Z off the bridge)
for x in (-0.66, 0, 0.66):
    p.box((x, 0.85, -0.2), (0.1, 0.7, 0.1), PAINT, 0.01)
p.box((0, 1.22, -0.2), (2.0, 0.1, 0.16), PAINT, 0.015)
p.box((0, 0.85, -0.2), (2.0, 0.06, 0.06), PAINT, 0.01)
m.add(p, ".", "Rail", CONCRETE)

m = prop("bridge_pier", "fitting")
p = Part("Pier")
p.stations("y", [(0.0, 2.4, 1.6, 0, 0, 0.2), (-0.8, 1.4, 1.2, 0, 0, 0.2), (-9.0, 1.6, 1.4, 0, 0, 0.25)], seams=dict(margin=0.1, width=0.02, depth=0.02, min_area=1.5))
m.add(p, ".", "Pier", CONCRETE)

# ── Cars ─────────────────────────────────────────────────────────────────────────────────────────


def car(name, paint, L=4.4, W=1.8, belt=0.95, roof=1.45, cab=(-0.55, 1.35), clear=0.28, wheel=(0.34, 1.3), box=None, bed=False, bar=False, livery=None, hp=5):
    """A road vehicle. cab = where the glasshouse starts and ends (Z); wheel = (radius, distance of
    the axles from the middle); box = (start, end, height) of a tall van body behind the cab;
    bed = an open load bed instead of a boot; bar = a light bar on the roof;
    livery = (material, kind) for a second colour."""
    m = prop(name, "car", hp=hp, mode=3, footprint=(W + 0.1, L), height=(box[2] if box else roof) + 0.05, shape=(W, (box[2] if box else roof) - clear, L))
    h = L / 2
    b = Part("Body")
    mid = (belt + clear) / 2
    b.stations("z", [(-h, W * 0.82, (belt - clear) * 0.6, 0, mid - 0.04, 0.08), (-h + 0.3, W, belt - clear, 0, mid, 0.12, 0.2),
                     (h - 0.28, W, belt - clear, 0, mid, 0.12, 0.2), (h, W * 0.86, (belt - clear) * 0.7, 0, mid + 0.02, 0.08)],
               seams=dict(margin=0.05, width=0.01, depth=0.012, min_area=0.5), cuts=[((0, 0, cab[0] + 0.5), (0, 0, 1)), ((0, 0, cab[1] - 0.5), (0, 0, 1))])
    # roof
    if not box:
        b.stations("z", [(cab[0] + 0.62, W * 0.78, 0.06, 0, roof - 0.03, 0.02), (cab[1] - 0.5, W * 0.78, 0.06, 0, roof - 0.03, 0.02)], bevel=0.012)
    else:
        b.stations("z", [(cab[0] + 0.5, W * 0.84, 0.06, 0, roof - 0.03, 0.02), (box[0], W * 0.84, 0.06, 0, roof - 0.03, 0.02)], bevel=0.012)
        b.stations("z", [(box[0], W, box[2] - belt + 0.05, 0, (box[2] + belt) / 2, 0.08), (box[1], W, box[2] - belt + 0.05, 0, (box[2] + belt) / 2, 0.08)],
                   seams=dict(margin=0.06, width=0.01, depth=0.012, min_area=0.6), cuts=[((0, 0, (box[0] + box[1]) / 2), (0, 0, 1))])
    if bed:
        for s in (-1, 1):
            b.box((s * (W / 2 - 0.04), belt + 0.16, (cab[1] + h) / 2), (0.06, 0.34, h - cab[1] - 0.05), PAINT, 0.01)
        b.box((0, belt + 0.16, h - 0.04), (W - 0.1, 0.34, 0.06), PAINT, 0.01)
    m.add(b, ".", "Body", paint)

    g = Part("Glass")
    end = box[0] if box else cab[1]
    g.stations("z", [(cab[0], W * 0.84, 0.04, 0, belt, 0.02), (cab[0] + 0.62, W * 0.8, roof - belt - 0.04, 0, (roof + belt) / 2 - 0.02, 0.1),
                     (end - 0.5, W * 0.8, roof - belt - 0.04, 0, (roof + belt) / 2 - 0.02, 0.1), (end, W * 0.84, 0.04 if not box else roof - belt - 0.04, 0, belt if not box else (roof + belt) / 2 - 0.02, 0.02)], bevel=0.01)
    m.add(g, ".", "Glass", GLASS)

    t = Part("Trim")
    for s in (-1, 1):
        t.box((0, clear + 0.14, s * (h + 0.03)), (W * 0.96, 0.2, 0.14), FRAME, 0.03)  # bumpers
        t.box((s * W * 0.34, belt - 0.2, -h - 0.005), (W * 0.2, 0.12, 0.03), EDGE, 0.01)  # head lamps
        t.box((s * W * 0.36, belt - 0.18, h + 0.005), (W * 0.16, 0.1, 0.03), SOFT, 0.01)
        for z in (-wheel[1], wheel[1]):
            w = hub("h", wheel[0], 0.22, out=s).move((s * (W / 2 - 0.1), wheel[0], z))
            t.merge(w)
        t.box((s * (W / 2 + 0.07), belt + 0.08, cab[0] + 0.5), (0.12, 0.08, 0.06), FRAME, 0.01)  # mirrors
    t.box((0, belt - 0.22, -h - 0.01), (W * 0.4, 0.14, 0.03), RECESS, 0)  # grille
    m.add(t, ".", "Trim", JOINT)

    wh = Part("Wheels")
    for s in (-1, 1):
        for z in (-wheel[1], wheel[1]):
            wh.merge(tyre("t", wheel[0], 0.24, tread=wheel[0] > 0.45).move((s * (W / 2 - 0.1), wheel[0], z)))
    m.add(wh, ".", "Wheels", TYRE)

    top = box[2] if box else roof
    if bar:
        for s, key in ((-1, "light_red"), (1, "light_blue")):
            l = Part("Bar" + key[6:].title())
            l.box((s * 0.3, top + 0.09, cab[0] + 1.0), (0.56, 0.14, 0.26), PAINT, 0.02)
            m.add(l, ".", "Bar" + key[6:].title(), key)
    if livery:
        lv = Part("Livery")
        colour, what = livery
        if what == "doors":
            for s in (-1, 1):
                lv.box((s * (W / 2 + 0.006), mid + 0.03, (cab[0] + cab[1]) / 2 + 0.15), (0.02, belt - clear - 0.16, cab[1] - cab[0] - 0.5), PAINT, 0)
        else:  # a stripe right round the tall body
            for s in (-1, 1):
                lv.box((s * (W / 2 + 0.006), belt + 0.25, (box[0] + box[1]) / 2), (0.02, 0.3, box[1] - box[0] - 0.1), PAINT, 0)
            lv.box((0, belt + 0.25, box[1] + 0.006), (W - 0.1, 0.3, 0.02), PAINT, 0)
        m.add(lv, ".", "Livery", colour)
    return m


car("car_sedan_red", "car_red")
car("car_sedan_blue", "car_blue")
car("car_sedan_black", "car_black")
car("car_sedan_silver", "car_silver")
car("car_hatch_white", "car_white", L=3.9, cab=(-0.5, 1.75), roof=1.48)
car("car_hatch_yellow", "car_yellow", L=3.9, cab=(-0.5, 1.75), roof=1.48)
car("car_hatch_green", "car_green", L=3.9, cab=(-0.5, 1.75), roof=1.48)
car("car_suv_black", "car_black", L=4.7, W=1.95, belt=1.1, roof=1.75, cab=(-0.7, 2.1), clear=0.36, wheel=(0.4, 1.45))
car("car_suv_silver", "car_silver", L=4.7, W=1.95, belt=1.1, roof=1.75, cab=(-0.7, 2.1), clear=0.36, wheel=(0.4, 1.45))
car("car_pickup_blue", "car_blue", L=5.2, W=1.95, belt=1.05, roof=1.75, cab=(-0.9, 0.7), clear=0.38, wheel=(0.4, 1.6), bed=True)
car("car_van_white", "car_white", L=5.2, W=2.0, belt=1.1, roof=1.9, cab=(-1.9, -0.6), clear=0.34, wheel=(0.38, 1.6), box=(-0.75, 2.55, 2.3))
car("car_bus", "car_yellow", L=10.5, W=2.5, belt=1.3, roof=2.9, cab=(-4.9, 4.9), clear=0.4, wheel=(0.5, 3.4), hp=10)
car("car_police", "car_white", bar=True, livery=("car_black", "doors"))
car("car_ambulance", "car_white", L=5.6, W=2.1, belt=1.1, roof=1.95, cab=(-2.1, -0.75), clear=0.36, wheel=(0.4, 1.75), box=(-0.9, 2.75, 2.6), bar=True, livery=("sign_red", "stripe"))
f = car("car_fire", "sign_red", L=8.2, W=2.5, belt=1.3, roof=2.5, cab=(-3.9, -2.2), clear=0.45, wheel=(0.52, 2.7), box=(-2.3, 4.05, 2.9), bar=True, livery=("sign_white", "stripe"), hp=10)
ld = Part("Ladder")
for s in (-1, 1):
    ld.box((s * 0.3, 3.05, 0.9), (0.06, 0.1, 6.2), PAINT, 0.01)
for i in range(14):
    ld.cyl((-0.3, 3.05, -2.0 + i * 0.44), (0.3, 3.05, -2.0 + i * 0.44), 0.025, segs=5, kind=PAINT, bevel=0)
ld.cyl((0, 2.9, 3.4), (0, 3.02, 3.4), 0.5, segs=12, kind=FRAME)
f.add(ld, ".", "Ladder", STEEL)

for m in ALL:
    if len(ARGS) < 2 or ARGS[1] == "all" or m.name in ARGS[1:]:
        m.finish([os.path.join(ARGS[0], "props")] + (["preview"] if "preview" in ARGS else []))
