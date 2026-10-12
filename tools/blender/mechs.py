"""The four mechs.
    blender -b --python tools/blender/mechs.py -- <output dir> <ostrich|hound|ballista|cleaver> [preview]
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


def bird_torso(model, k, nose="blunt", stacks=True, guns=(), heavy=False):
    """Body of a chicken-walker in the axes of Hips/Torso (turret ring at the origin, nose to -Z),
    at the Ostrich's size times k. guns: (x, y, z of the breech, length, radius) of fixed nose lasers,
    at the Ostrich's size."""
    T = "Hips/Torso"
    p = Part("CenterTorso_Armor")
    if nose == "sharp":
        rows = [(-3.3, 0.45, 0.26, 0, 1.08, 0.07), (-2.6, 1.25, 0.8, 0, 1.18, 0.2), (-1.7, 1.85, 1.45, 0, 1.2, 0.32), (-1.15, 1.5, 1.9, 0, 1.1, 0.25)]
    else:
        rows = [(-3.05, 0.8, 0.42, 0, 1.12, 0.12), (-2.55, 1.5, 0.95, 0, 1.2, 0.24), (-1.7, 1.95, 1.5, 0, 1.2, 0.32), (-1.15, 1.5, 1.9, 0, 1.1, 0.25)]
    p.stations("z", rows, seams=PANELS, cuts=[((0, 0, -2.1), (0, 0, 1))])
    p.stations("z", [(-1.15, 1.42, 1.95, 0, 1.1, 0.2), (1.75, 1.42, 1.95, 0, 1.1, 0.2)], seams=PANELS, cuts=[((0, 0, 0.3), (0, 0, 1))])
    p.stations("z", [(-1.05, 0.5, 0.2, 0, 2.1, 0.06), (-0.6, 0.82, 0.34, 0, 2.17, 0.1), (1.3, 0.82, 0.34, 0, 2.17, 0.1), (1.7, 0.6, 0.2, 0, 2.1, 0.06)], seams=SMALL_PANELS)
    p.stations("z", [(-2.0, 0.9, 0.2, 0, 0.5, 0.06), (-1.0, 1.2, 0.3, 0, 0.22, 0.1), (1.3, 1.2, 0.3, 0, 0.22, 0.1), (1.7, 1.0, 0.2, 0, 0.3, 0.06)])
    for s in (-1, 1):
        p.plate(None, (s * 0.86, 1.2, -1.95), (s, 0.15, -0.35), (0.7, 0.75), 0.09 if heavy else 0.05, along=(0, 0, 1))
        p.bolts([(s * 0.9, 1.45, -1.75), (s * 0.9, 0.95, -1.75)], (s, 0.1, -0.2))
    if heavy:
        # extra glacis over the nose
        p.plate(None, (0, 1.72, -2.35), (0, 1, -0.55), (1.1, 0.5), 0.08, along=(0, 0, 1))
    model.add(p.scale(k), T + "/CenterTorso", "Armor", CAMO)

    f = Part("CenterTorso_Frame")
    f.stations("z", [(1.7, 2.3, 1.5, 0, 1.35, 0.2), (2.6, 2.2, 1.4, 0, 1.4, 0.28), (2.95, 1.7, 1.0, 0, 1.4, 0.2)], kind=FRAME, seams=SMALL_PANELS)
    f.slats((0, 1.4, 2.96), (0, 0, 1), (1, 0, 0), (1.3, 0.75), 6, 0.09)
    for s in (-1, 1):
        if stacks:
            f.cyl((s * 0.72, 1.95, 2.15), (s * 0.78, 2.5, 2.8), 0.2, segs=12, kind=FRAME)
            f.cyl((s * 0.778, 2.48, 2.78), (s * 0.782, 2.515, 2.815), 0.14, segs=12, kind=RECESS, bevel=0)
        f.slats((s * 1.16, 1.4, 2.2), (s, 0, 0), (0, 1, 0), (0.7, 0.7), 5, 0.06)
    f.cyl((0, -0.62, 0), (0, 0.2, 0), 0.78, segs=20, kind=FRAME)
    f.cyl((0, -0.3, 0), (0, -0.12, 0), 0.95, segs=20, kind=SOFT)
    f.box((0, 0.6, -2.4), (0.5, 0.3, 0.6), FRAME)
    f.cyl((0, 0.6, -2.7), (0, 0.6, -2.8), 0.11, segs=10, kind=RECESS, bevel=0)
    for x, y, z, length, r in guns:
        barrel(f, (x, y, z), length, r)
        f.box((x, y, z + 0.2), (r * 3.2, r * 3.0, 0.5), FRAME)
    model.add(f.scale(k), T + "/CenterTorso", "Frame", JOINT)

    h = Part("Head_Armor")
    h.stations("z", [(-2.0, 1.36, 0.22, 0, 2.08, 0.08), (-1.55, 1.5, 0.36, 0, 2.04, 0.12), (-1.15, 1.3, 0.25, 0, 2.02, 0.08)], seams=SMALL_PANELS)
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
    left.plate(None, (-1.27, 2.1, 0.75), (0, 1, 0), (0.7, 1.0), 0.05, along=(0, 0, 1))
    left.bolts([(-1.52, 2.15, 0.35), (-1.02, 2.15, 0.35), (-1.52, 2.15, 1.15), (-1.02, 2.15, 1.15)], (0, 1, 0))
    left.plate(None, (-1.83, 1.05, 1.0), (-1, 0, 0), (1.2, 1.3), 0.09 if heavy else 0.05, along=(0, 0, 1))
    model.both(left.scale(k), T + "/LeftTorso", "Armor", CAMO)

    s = Part("Stripe")
    s.stations("x", [(-1.76, 0.5, 0.1, -0.85, 2.09, 0.03), (-0.72, 0.62, 0.12, -0.85, 2.12, 0.03), (0.72, 0.62, 0.12, -0.85, 2.12, 0.03), (1.76, 0.5, 0.1, -0.85, 2.09, 0.03)], bevel=0.012)
    model.add(s.scale(k), T, "Stripe", ACCENT)

    a = Part("Antenna")
    a.box((-1.2, 2.18, 1.5), (0.26, 0.18, 0.26), FRAME)
    a.cyl((-1.2, 2.25, 1.5), (-1.2, 3.7, 1.5), 0.035, 0.012, segs=6, kind=FRAME, bevel=0)
    a.cyl((-0.95, 2.2, 1.6), (-0.95, 2.9, 1.6), 0.025, 0.01, segs=6, kind=FRAME, bevel=0)
    model.add(a.scale(k), T, "Antenna", JOINT)


def gun_pods(model, k):
    """Arms that are gun pods: a laser over an autocannon. Shoulder axes, left arm (body on its +X)."""
    T = "Hips/Torso"
    lf = Part("LeftTorso_Frame")
    lf.cyl((-1.7, 1.0, -0.3), (-2.02, 1.0, -0.3), 0.5, segs=16, kind=FRAME)
    lf.cyl((-1.8, 1.0, -0.3), (-1.9, 1.0, -0.3), 0.6, segs=16, kind=SOFT)
    model.both(lf.scale(k), T + "/LeftTorso", "Frame", JOINT)

    arm = Part("ArmL_Armor")
    arm.stations("z", [(0.8, 0.6, 0.7, 0, 0, 0.12), (0.35, 1.0, 1.1, 0, 0, 0.2), (-1.5, 1.0, 1.1, 0, 0, 0.2), (-2.1, 0.82, 0.9, 0, 0, 0.18)],
                 seams=PANELS, cuts=[((0, 0, -0.6), (0, 0, 1))])
    arm.plate(None, (0, 0.55, -0.6), (0, 1, 0), (0.6, 1.4), 0.06, along=(0, 0, 1))
    arm.bolts([(-0.22, 0.62, -1.2), (0.22, 0.62, -1.2), (-0.22, 0.62, 0.0), (0.22, 0.62, 0.0)], (0, 1, 0))
    arm.slats((-0.51, 0.12, 0.05), (-1, 0, 0), (0, 0, 1), (0.5, 0.5), 5, 0.05)
    model.both(arm.scale(k), T + "/ArmL/Hit", "Armor", CAMO)

    af = Part("ArmL_Frame")
    af.cyl((0.3, 0, 0), (0.64, 0, 0), 0.42, segs=16, kind=FRAME)
    af.box((0, 0, -2.12), (0.72, 0.82, 0.12), FRAME)
    barrel(af, (0, 0.25, -2.0), 1.62, 0.16, "laser")
    barrel(af, (0, -0.25, -2.0), 1.3, 0.17, "cannon")
    af.cyl((-0.36, -0.62, -0.5), (0.36, -0.62, -0.5), 0.3, segs=14, kind=FRAME)  # ammunition drum
    af.box((0, -0.5, -1.25), (0.3, 0.2, 1.1), FRAME)
    model.both(af.scale(k), T + "/ArmL/Hit", "Frame", JOINT)


def missile_racks(model):
    """Arms that are box launchers on yokes. Shoulder axes in scene units, left arm (body on its +X)."""
    T = "Hips/Torso"
    r = Part("ArmL_Armor")
    r.stations("z", [(1.23, 1.5, 1.3, 0, 0, 0.15), (0.9, 1.9, 1.68, 0, 0, 0.22), (-1.9, 1.9, 1.68, 0, 0, 0.22), (-2.13, 1.76, 1.54, 0, 0, 0.18)],
               seams=PANELS, cuts=[((0, 0, -0.5), (0, 0, 1))])
    r.plate(None, (0, 0.84, -0.5), (0, 1, 0), (1.2, 1.9), 0.07, along=(0, 0, 1))
    r.bolts([(-0.5, 0.92, -1.3), (0.5, 0.92, -1.3), (-0.5, 0.92, 0.3), (0.5, 0.92, 0.3)], (0, 1, 0), r=0.045)
    r.slats((0, 0, 1.24), (0, 0, 1), (1, 0, 0), (1.0, 0.8), 5, 0.07)
    model.both(r, T + "/ArmL/Hit", "Armor", CAMO)
    model.moves["Hips/Torso/ArmL/Insignia"] = (-0.975, 0.0, 0.45)
    model.moves["Hips/Torso/ArmR/Insignia"] = (0.975, 0.0, 0.45)

    f = Part("ArmL_Frame")
    # the face of the launcher: five by four tubes
    f.box((0, 0, -2.15), (1.62, 1.4, 0.1), FRAME, 0.02)
    for i in range(5):
        for j in range(4):
            x, y = (i - 2) * 0.3, (j - 1.5) * 0.31
            f.cyl((x, y, -2.19), (x, y, -2.215), 0.115, segs=10, kind=RECESS, bevel=0)
            f.cyl((x, y, -2.18), (x, y, -2.225), 0.135, 0.125, segs=10, kind=SOFT, bevel=0, caps=False)
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
    hips(m, 1.0)
    cockpit(m)
    legs(m, (2.7, 0.95, 1.15), (2.9, 0.75, 0.85), (1.6, 0.55, 0.6), (1.3, 0.35, 2.3), (0.7, 0.6, 0.5))
    return m


def hound():
    k = 0.8
    m = Model("hound", "scenes/mech/hound.tscn")
    bird_torso(m, k, nose="sharp", stacks=False, guns=[(0, 0.9, -2.7, 1.05, 0.15)])
    gun_pods(m, k)
    hips(m, k, jets=[(-0.72, -0.32, 0.88), (0.72, -0.32, 0.88)])
    cockpit(m)
    legs(m, (2.484, 0.76, 0.92), (2.668, 0.6, 0.68), (1.472, 0.44, 0.48), (1.04, 0.28, 1.84), (0.56, 0.48, 0.4))
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


def cleaver():
    """A man-shaped brawler: upright torso, a head, arms with elbows; an axe in the right hand."""
    m = Model("cleaver", "scenes/mech/cleaver.tscn")
    T = "Hips/Torso"
    p = Part("CenterTorso_Armor")
    # abdomen and chest
    p.stations("y", [(0.05, 1.15, 1.25, 0, 0, 0.18), (1.1, 1.45, 1.75, 0, 0, 0.25), (2.2, 1.5, 2.05, 0, -0.05, 0.28), (2.78, 1.3, 1.6, 0, 0, 0.25)], seams=PANELS, cuts=[((0, 1.5, 0), (0, 1, 0))])
    # breastplate
    p.stations("z", [(-1.78, 1.2, 0.7, 0, 1.85, 0.16), (-1.35, 1.82, 1.16, 0, 1.8, 0.24), (-0.95, 1.7, 1.3, 0, 1.75, 0.2)], seams=SMALL_PANELS)
    p.plate(None, (-0.4, 1.8, -1.8), (0, 0.1, -1), (0.5, 0.5), 0.05)
    # collar round the neck
    p.stations("y", [(2.7, 1.0, 1.1, 0, -0.1, 0.2), (2.95, 0.8, 0.9, 0, -0.1, 0.15)])
    m.add(p, T + "/CenterTorso", "Armor", CAMO)

    f = Part("CenterTorso_Frame")
    # reactor pack on the back
    f.stations("z", [(0.95, 1.6, 1.6, 0, 1.62, 0.2), (1.65, 1.72, 1.72, 0, 1.62, 0.28), (1.92, 1.3, 1.25, 0, 1.62, 0.2)], kind=FRAME, seams=SMALL_PANELS)
    f.slats((0, 1.62, 1.93), (0, 0, 1), (1, 0, 0), (0.95, 0.85), 6, 0.08)
    for s in (-1, 1):
        f.cyl((s * 0.55, 2.3, 1.3), (s * 0.6, 2.85, 1.6), 0.16, segs=12, kind=FRAME)
        f.cyl((s * 0.598, 2.83, 1.59), (s * 0.602, 2.87, 1.61), 0.11, segs=12, kind=RECESS, bevel=0)
    # waist
    f.cyl((0, -0.6, 0), (0, 0.15, 0), 0.72, segs=20, kind=FRAME)
    f.cyl((0, -0.28, 0), (0, -0.12, 0), 0.88, segs=20, kind=SOFT)
    # neck
    f.cyl((0, 2.7, -0.15), (0, 3.0, -0.15), 0.32, segs=14, kind=FRAME)
    barrel(f, (0.52, 1.8, -1.5), 0.82, 0.14, "laser")
    m.add(f, T + "/CenterTorso", "Frame", JOINT)

    h = Part("Head_Armor")
    # helmet: brow, crown, jaw
    h.stations("z", [(-0.76, 0.7, 0.5, 0, 3.12, 0.12), (-0.45, 0.95, 0.86, 0, 3.18, 0.2), (0.2, 0.95, 0.86, 0, 3.18, 0.22), (0.34, 0.7, 0.6, 0, 3.2, 0.15)], seams=SMALL_PANELS)
    h.stations("z", [(-0.82, 0.5, 0.2, 0, 2.92, 0.05), (-0.5, 0.7, 0.26, 0, 2.86, 0.07), (-0.1, 0.6, 0.2, 0, 2.86, 0.05)])
    for s in (-1, 1):
        h.cyl((s * 0.48, 3.15, -0.2), (s * 0.56, 3.15, -0.2), 0.2, segs=10, kind=SOFT)
    h.cyl((0.3, 3.55, 0.1), (0.3, 3.95, 0.14), 0.02, 0.008, segs=6, kind=FRAME, bevel=0)
    m.add(h, T + "/Head", "Skull", CAMO)
    v = Part("Head_Visor")
    v.stations("z", [(-0.8, 0.6, 0.16, 0, 3.24, 0.04), (-0.7, 0.86, 0.24, 0, 3.24, 0.06), (-0.5, 0.92, 0.24, 0, 3.24, 0.06)], bevel=0.008)
    m.add(v, T + "/Head", "Visor", GLASS)

    left = Part("LeftTorso_Armor")
    left.stations("y", [(0.2, 0.7, 1.3, -1.0, 0, 0.15), (1.2, 0.95, 1.9, -1.19, 0, 0.22), (2.3, 1.0, 2.1, -1.2, 0, 0.25), (2.66, 0.8, 1.7, -1.15, 0, 0.2)], seams=PANELS, cuts=[((0, 1.4, 0), (0, 1, 0))])
    left.slats((-1.2, 2.0, -1.06), (0, 0.1, -1), (1, 0, 0), (0.5, 0.6), 4, 0.06)
    # pauldron
    left.stations("x", [(-2.0, 1.1, 0.3, 0, 2.62, 0.08), (-1.5, 1.6, 0.62, 0, 2.66, 0.18), (-1.0, 1.5, 0.5, 0, 2.7, 0.14)], seams=SMALL_PANELS)
    m.both(left, T + "/LeftTorso", "Hull", CAMO)
    m.removes += [T + "/LeftTorso/Shoulder", T + "/RightTorso/Shoulder"]

    s = Part("Stripe")
    s.stations("x", [(-1.66, 0.45, 0.1, -0.86, 2.74, 0.03), (-0.7, 0.5, 0.12, -1.0, 2.5, 0.03), (0.7, 0.5, 0.12, -1.0, 2.5, 0.03), (1.66, 0.45, 0.1, -0.86, 2.74, 0.03)], bevel=0.012)
    m.add(s, T, "Stripe", ACCENT)
    a = Part("Antenna")
    a.box((-0.86, 2.9, 0.57), (0.2, 0.14, 0.2), FRAME)
    a.cyl((-0.86, 2.95, 0.57), (-0.86, 4.0, 0.57), 0.03, 0.01, segs=6, kind=FRAME, bevel=0)
    m.add(a, T, "Antenna", JOINT)

    # Upper arm: hangs from the shoulder (the node is at its middle; the elbow is 0.76 below)
    u = Part("UpperArmL")
    u.stations("y", [(0.8, 0.8, 0.86, 0, 0, 0.16), (0.1, 0.76, 0.8, 0, 0, 0.14), (-0.75, 0.6, 0.62, 0, 0, 0.1)], seams=SMALL_PANELS)
    u.cyl((0.2, 0.62, 0), (0.75, 0.62, 0), 0.36, segs=14, kind=SOFT)
    m.both(u, T, "UpperArmL", CAMO, own_place=True)

    def forearm(name, gun):
        fa = Part(name)
        fa.cyl((-0.46, 0, 0), (0.46, 0, 0), 0.45, segs=16, kind=FRAME)  # elbow
        for s in (-1, 1):
            fa.cyl((s * 0.46, 0, 0), (s * 0.52, 0, 0), 0.28, segs=12, kind=SOFT)
        # fist
        fa.stations("z", [(-2.28, 0.8, 0.8, 0, 0, 0.14), (-2.94, 0.72, 0.7, 0, 0, 0.14)], kind=FRAME)
        for i in range(4):
            fa.box(((i - 1.5) * 0.19, 0.12, -2.98), (0.16, 0.5, 0.12), FRAME, 0.015)
        if gun:
            barrel(fa, (0, 0, -2.9), 0.9, 0.19, "cannon")
            fa.cyl((0.28, -0.28, -2.3), (0.28, -0.28, -2.94), 0.06, segs=8, kind=FRAME, bevel=0)
        return fa

    arm = Part("ArmL_Armor")
    arm.stations("z", [(-0.35, 0.6, 0.6, 0, 0, 0.1), (-0.8, 0.8, 0.82, 0, 0, 0.16), (-2.0, 0.76, 0.76, 0, 0, 0.14), (-2.32, 0.6, 0.6, 0, 0, 0.1)], seams=SMALL_PANELS)
    arm.plate(None, (0, 0.4, -1.4), (0, 1, 0), (0.46, 1.0), 0.06, along=(0, 0, 1))
    m.both(arm, T + "/ArmL/Hit", "Forearm", CAMO)
    m.add(forearm("ArmL_Frame", True), T + "/ArmL/Hit", "Hand", JOINT)
    m.add(forearm("ArmR_Frame_", False).mirrored("ArmR_Frame"), T + "/ArmR/Hit", "Hand", JOINT)
    for side in "LR":
        m.removes += [f"{T}/Arm{side}/Hit/Elbow", f"{T}/Arm{side}/Hit/Barrel"]
    m.moves[T + "/ArmL/Insignia"] = (-0.41, 0.0, -1.4)
    m.moves[T + "/ArmR/Insignia"] = (0.41, 0.0, -1.4)

    hips(m, 0.95, jets=[(-0.86, -0.38, 1.04), (0.86, -0.38, 1.04)])
    cockpit(m)
    legs(m, (2.744, 0.9025, 1.0925), (2.744, 0.7125, 0.8075), (0.441, 0.5225, 0.57), (1.14, 0.3325, 2.09), (0.665, 0.57, 0.475), spur=False, foot_shift=-0.43)
    return m


{"ostrich": ostrich, "hound": hound, "ballista": ballista, "cleaver": cleaver}[ARGS[1]]().finish([ARGS[0]] + ARGS[2:])
