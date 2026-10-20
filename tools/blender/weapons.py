"""The weapons' own models: what a machine shows at a hardpoint is what is fitted there.
    blender -b --python tools/blender/weapons.py -- <output dir> [preview]
    godot --headless --path . -s tools/blender/import_parts.gd -- <output dir>/weapons.glb res://models/weapons
Writes weapons.glb, one mesh per weapon, named by its catalog id (models/weapons/<id>.res), each the
"Model" child of scenes/weapons/<id>.tscn. Every one is modelled with its muzzle at the origin and
its body running back along +Z (the weapon node is the muzzle, see Weapon.ModelLength): the
hardpoint sets it so that its back end sits on the machine (Hardpoint.Layout).
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from meshkit import *  # noqa: E402

ARGS = sys.argv[sys.argv.index("--") + 1:]
SEAMS = dict(margin=0.02, width=0.006, depth=0.006, min_area=0.03)


def laser(name, length, r):
    """A lens in a collar at the muzzle, a tube ringed with heat-sink fins, a squared housing behind."""
    p = Part(name)
    p.cyl((0, 0, -0.01), (0, 0, 0.012), r * 0.55, segs=14, kind=RECESS, bevel=0)                     # the lens
    p.cyl((0, 0, 0.0), (0, 0, 0.1), r * 0.95, segs=14, kind=FRAME)                                    # collar
    p.cyl((0, 0, 0.08), (0, 0, length * 0.62), r * 0.62, segs=12, kind=FRAME, bevel=0)                # tube
    n = max(3, int(length * 0.45 / (r * 1.1)))
    for i in range(n):
        z = length * 0.14 + i * (length * 0.4 / n)
        p.cyl((0, 0, z), (0, 0, z + r * 0.35), r * 1.22, segs=12, kind=SOFT, bevel=0.006)             # fins
    p.stations("z", [(length * 0.58, r * 2.0, r * 2.0, 0, 0, r * 0.25), (length, r * 2.5, r * 2.3, 0, 0, r * 0.3)], kind=FRAME, seams=SEAMS)
    p.slats((r * 1.27, 0, length * 0.8), (1, 0, 0), (0, 1, 0), (r * 1.4, length * 0.3), 4, 0.02)
    p.slats((-r * 1.27, 0, length * 0.8), (-1, 0, 0), (0, 1, 0), (r * 1.4, length * 0.3), 4, 0.02)
    return p


def pulse_laser(name, length, r):
    """A pulse laser: a squat emitter head of stacked focusing rings round a wide lens, a ribbed
    capacitor drum behind it (the bursts are stored there), and a slab-sided housing with cooling vents."""
    p = Part(name)
    p.cyl((0, 0, -0.01), (0, 0, 0.012), r * 0.75, segs=16, kind=RECESS, bevel=0)                     # the lens
    # the emitter tube, and the focusing rings round its mouth, each against the next
    p.cyl((0, 0, 0.0), (0, 0, length * 0.62), r * 0.85, segs=14, kind=FRAME, bevel=0)
    for i in range(4):
        z = i * r * 0.42
        p.cyl((0, 0, z), (0, 0, z + r * 0.42), r * (1.25 - i * 0.07), segs=16, kind=FRAME if i % 2 == 0 else SOFT)
    for i in range(6):
        z = length * 0.24 + i * length * 0.055
        p.cyl((0, 0, z), (0, 0, z + length * 0.03), r * 1.3, segs=14, kind=SOFT, bevel=0.004)          # capacitor ribs
    p.stations("z", [(length * 0.55, r * 2.4, r * 2.0, 0, 0, r * 0.3), (length, r * 2.6, r * 2.2, 0, 0, r * 0.35)], kind=FRAME, seams=SEAMS)
    for s in (-1, 1):
        p.slats((s * r * 1.32, 0, length * 0.78), (s, 0, 0), (0, 1, 0), (r * 1.3, length * 0.32), 5, 0.02)
        p.cyl((s * r * 0.8, r * 1.05, length * 0.56), (s * r * 0.6, r * 0.75, length * 0.3), r * 0.1, segs=6, kind=SOFT, bevel=0)  # leads
    return p


def ppc(name, length, r):
    """A particle cannon: a flared emitter, a barrel through three heavy field coils, the capacitor block."""
    p = Part(name)
    p.cyl((0, 0, -0.01), (0, 0, 0.012), r * 0.6, segs=16, kind=RECESS, bevel=0)
    p.cyl((0, 0, 0.0), (0, 0, 0.25), r * 1.15, r * 0.75, segs=16, kind=FRAME)                         # emitter flare
    p.cyl((0, 0, 0.2), (0, 0, length * 0.62), r * 0.7, segs=14, kind=FRAME, bevel=0)
    for t in (0.22, 0.36, 0.5):
        z = length * t
        p.cyl((0, 0, z), (0, 0, z + r * 0.7), r * 1.55, segs=16, kind=SOFT)                            # coils
        p.cyl((0, 0, z - 0.02), (0, 0, z + r * 0.7 + 0.02), r * 1.2, segs=16, kind=FRAME, bevel=0)
    p.stations("z", [(length * 0.58, r * 2.6, r * 2.4, 0, 0, r * 0.35), (length, r * 3.0, r * 2.7, 0, 0, r * 0.4)], kind=FRAME, seams=SEAMS)
    for s in (-1, 1):
        p.cyl((s * r * 0.9, r * 0.9, length * 0.62), (s * r * 0.75, r * 0.75, length * 0.3), r * 0.12, segs=6, kind=SOFT, bevel=0)  # leads
    return p


def gauss(name, length, w):
    """Two square rails held in a ring of coils, a heavy block at the muzzle, the capacitor bank behind."""
    p = Part(name)
    p.box((0, 0, 0.14), (w * 1.5, w * 1.5, 0.28), FRAME, 0.02)                                        # muzzle block
    p.box((0, 0, -0.005), (w * 0.55, w * 0.55, 0.02), RECESS, 0)
    p.box((0, 0, length * 0.36), (w, w, length * 0.62), FRAME, 0.015)                                 # rails
    for i in range(6):
        z = 0.42 + i * (length * 0.6 - 0.42) / 6
        p.box((0, 0, z), (w * 1.65, w * 1.65, 0.13), FRAME, 0.015)                                    # coils
        p.box((0, 0, z + 0.075), (w * 1.4, w * 1.4, 0.03), SOFT, 0)
    p.stations("z", [(length * 0.64, w * 2.3, w * 2.2, 0, 0, w * 0.2), (length, w * 2.6, w * 2.4, 0, 0, w * 0.25)], kind=FRAME, seams=SEAMS)
    for s in (-1, 1):
        p.cyl((s * w * 0.6, -w * 0.85, length * 0.66), (s * w * 0.5, -w * 0.6, length * 0.4), 0.04, segs=6, kind=SOFT, bevel=0)
    return p


def cannon(name, length, r, brake=True, extractor=False, receiver=1.0):
    """A gun: muzzle brake, a barrel (with a fume extractor on the big ones), a recoil sleeve, the breech."""
    p = Part(name)
    p.cyl((0, 0, -0.01), (0, 0, 0.012), r * 0.62, segs=12, kind=RECESS, bevel=0)                      # bore
    if brake:
        b = r * 2.6
        p.box((0, 0, 0.17), (b, b * 0.82, 0.34), FRAME, 0.02)
        for s in (-1, 1):
            p.slats((s * b * 0.505, 0, 0.17), (s, 0, 0), (0, 1, 0), (b * 0.45, 0.28), 3, 0.02)
    p.cyl((0, 0, 0.1 if brake else 0.0), (0, 0, length * 0.75), r, r * 1.1, segs=14, kind=FRAME)      # barrel
    if extractor:
        p.cyl((0, 0, length * 0.3), (0, 0, length * 0.42), r * 1.55, segs=14, kind=FRAME)
    p.cyl((0, 0, length * 0.6), (0, 0, length * 0.8), r * 1.5, segs=14, kind=FRAME)                    # recoil sleeve
    k = receiver
    p.stations("z", [(length * 0.78, r * 3.0 * k, r * 2.7 * k, 0, 0, r * 0.4), (length, r * 3.2 * k, r * 2.9 * k, 0, 0, r * 0.45)], kind=FRAME, seams=SEAMS)
    p.box((r * 1.9 * k, -r * 0.4, length * 0.9), (r * 0.8, r * 1.5, length * 0.16), FRAME, 0.01)      # feed chute
    return p


def machine_gun(name, length, r):
    """A slim barrel in a holed cooling jacket, a small receiver."""
    p = Part(name)
    p.cyl((0, 0, -0.01), (0, 0, 0.01), r * 0.5, segs=8, kind=RECESS, bevel=0)
    p.cyl((0, 0, 0.0), (0, 0, length * 0.7), r * 0.6, segs=8, kind=FRAME, bevel=0)
    p.cyl((0, 0, length * 0.1), (0, 0, length * 0.6), r * 1.3, segs=10, kind=FRAME)                   # jacket
    for i in range(5):
        z = length * (0.15 + i * 0.09)
        p.cyl((0, 0, z), (0, 0, z + 0.03), r * 1.38, segs=10, kind=RECESS, bevel=0)
    p.box((0, 0, length * 0.84), (r * 3.0, r * 3.4, length * 0.32), FRAME, 0.01)                      # receiver
    p.box((-r * 2.1, -r * 0.6, length * 0.82), (r * 1.2, r * 2.0, length * 0.18), FRAME, 0.008)        # box magazine
    return p


def flamer(name, length, r):
    """A nozzle with its igniter, a pipe, and the fuel tank slung under it."""
    p = Part(name)
    p.cyl((0, 0, -0.005), (0, 0, 0.01), r * 0.55, segs=10, kind=RECESS, bevel=0)
    p.cyl((0, 0, 0.0), (0, 0, 0.22), r * 0.8, r * 1.1, segs=12, kind=FRAME)                           # nozzle
    p.cyl((0, r * 1.05, 0.04), (0, r * 1.05, 0.3), r * 0.22, segs=6, kind=SOFT, bevel=0)               # igniter
    p.cyl((0, 0, 0.2), (0, 0, length * 0.7), r * 0.6, segs=10, kind=FRAME, bevel=0)
    p.lathe((0, -r * 1.5, length * 0.25), (0, -r * 1.5, length), [(0.0, r * 0.6), (0.08, r * 1.1), (0.92, r * 1.1), (1.0, r * 0.6)], segs=14, kind=PAINT)  # tank
    for t in (0.35, 0.75):
        p.cyl((-r * 1.2, -r * 1.5, length * t), (r * 1.2, -r * 1.5, length * t), 0.03, segs=6, kind=SOFT, bevel=0)  # straps
    p.box((0, -r * 0.55, length * 0.84), (r * 1.4, r * 1.2, length * 0.3), FRAME, 0.01)              # valve block (its back short of the tank's)
    return p


def missiles(name, cols, rows, pitch, depth):
    """A cassette of launch tubes, its face at the muzzle: it fronts a launcher box on the machine."""
    p = Part(name)
    w, h = cols * pitch + 0.12, rows * pitch + 0.12
    p.box((0, 0, depth / 2), (w, h, depth), FRAME, 0.02, SEAMS)
    for i in range(cols):
        for j in range(rows):
            x, y = (i - (cols - 1) / 2) * pitch, (j - (rows - 1) / 2) * pitch
            p.cyl((x, y, -0.012), (x, y, 0.004), pitch * 0.36, segs=10, kind=RECESS, bevel=0)        # tube mouths
            p.cyl((x, y, -0.03), (x, y, 0.0), pitch * 0.44, pitch * 0.42, segs=10, kind=SOFT, bevel=0, caps=False)
    return p


def build():
    m = Model("weapons", None)
    parts = [
        laser("small_laser", 0.9, 0.12),
        laser("medium_laser", 1.3, 0.16),
        laser("large_laser", 1.8, 0.21),
        pulse_laser("small_pulse_laser", 0.95, 0.12),
        pulse_laser("medium_pulse_laser", 1.35, 0.16),
        pulse_laser("large_pulse_laser", 1.85, 0.21),
        ppc("ppc", 2.0, 0.2),
        gauss("gauss", 2.6, 0.32),
        cannon("autocannon", 2.0, 0.12),
        cannon("heavy_cannon", 3.0, 0.17, extractor=True, receiver=1.1),
        cannon("howitzer", 5.5, 0.2, extractor=True, receiver=1.2),
        machine_gun("machine_gun", 1.1, 0.05),
        flamer("flamethrower", 1.0, 0.13),
        missiles("srm", 3, 2, 0.24, 0.45),
        missiles("lrm", 5, 2, 0.22, 0.55),
        missiles("rocket_battery", 4, 3, 0.26, 0.6),
    ]
    for p in parts:
        m.loose.append(p)
    return m


build().finish(ARGS)
