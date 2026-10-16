"""Coconut palms for the coast map.
    blender -b --python tools/blender/palm.py -- <output dir>
Writes <variant>.glb for each variant below, with two meshes: "Trunk" (UV-mapped for the palm bark
texture: U round the trunk, V up it, one repeat of the texture per 1.4 m) and "Crown" (fronds, dead
fronds hanging under them and a bunch of coconuts; coloured by vertex colour, no texture). Turn them
into mesh resources with tools/blender/import_parts.gd, as for the other models.

The frond is built the way a coconut palm's is: a stiff midrib that rises from the crown and arches
over under its own weight, and along both sides of it some forty narrow leaflets, longest a third of
the way out, each folded along its middle, angled forward and drooping into a shallow V.
Axes: the game's (X right, Y up, Z back), metres; the palm stands on its origin.
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
from meshkit import gd, reset_scene, export_glb  # noqa: E402

ARGS = sys.argv[sys.argv.index("--") + 1:]
OUT = ARGS[0] if ARGS else "/tmp/palms"

# name: height, lean (m the top stands off the base), lean bend (how much of it is in the upper part), fronds, seed
VARIANTS = {
    "palm_tall": dict(height=11.5, lean=1.6, bend=0.7, fronds=17, seed=3),
    "palm_medium": dict(height=8.0, lean=0.9, bend=0.5, fronds=15, seed=11),
    "palm_leaning": dict(height=9.0, lean=3.8, bend=0.35, fronds=16, seed=23),
}

BARK_REPEAT = 1.4  # metres of trunk per repeat of the bark texture


class Builder:
    """A bmesh with a vertex-colour layer and a UV layer, filled in game axes."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.col = self.bm.loops.layers.float_color.new("Color")
        self.uv = self.bm.loops.layers.uv.new("UVMap")

    def vert(self, p):
        return self.bm.verts.new(gd(p))

    def face(self, verts, colour, uvs=None):
        try:
            f = self.bm.faces.new(verts)
        except ValueError:
            return None
        for i, loop in enumerate(f.loops):
            loop[self.col] = colour
            if uvs:
                loop[self.uv].uv = uvs[i]
        f.smooth = True
        return f

    def finish(self):
        mesh = bpy.data.meshes.new(self.name)
        self.bm.normal_update()
        self.bm.to_mesh(mesh)
        self.bm.free()
        mesh.color_attributes.active_color = mesh.color_attributes["Color"]
        mesh.color_attributes.render_color_index = 0
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        return obj


def trunk_axis(t, height, lean, bend, yaw):
    """Point on the trunk's centre line at t (0 = foot, 1 = top): it rises, curving over to one side."""
    side = Vector((math.cos(yaw), 0.0, math.sin(yaw)))
    # The curve: mostly straight low down, leaning more and more towards the top (bend: how late).
    offset = lean * (t ** (1.0 + bend * 2.0))
    return Vector((0.0, t * height, 0.0)) + side * offset


def build_trunk(b, height, lean, bend, yaw, rng):
    sides = 16
    rings = max(12, int(height / 0.14))
    rows = []
    vs = []
    for k in range(rings + 1):
        t = k / rings
        c = trunk_axis(t, height, lean, bend, yaw)
        # The trunk's own direction here, to set the ring square to it.
        d = (trunk_axis(min(t + 0.01, 1.0), height, lean, bend, yaw) - trunk_axis(max(t - 0.01, 0.0), height, lean, bend, yaw)).normalized()
        x = d.cross(Vector((0.0, 0.0, 1.0)))
        if x.length < 1e-4:
            x = Vector((1.0, 0.0, 0.0))
        x.normalize()
        z = x.cross(d).normalized()
        # Thick at the foot (a swollen bole over the roots), slimming up the trunk, a little wider under the crown.
        r = 0.17 + 0.05 * (1.0 - t) + 0.14 * math.exp(-t * height / 0.45) + 0.03 * max(0.0, (t - 0.9) / 0.1)
        # The leaf scars: a ridge every ring of the old leaf bases.
        r *= 1.0 + 0.035 * (0.5 + 0.5 * math.cos(t * height / 0.13 * math.tau))
        row = []
        for i in range(sides + 1):
            a = i / sides * math.tau
            wobble = 1.0 + 0.02 * math.sin(a * 3 + k * 0.7)
            row.append(b.vert(c + (x * math.cos(a) + z * math.sin(a)) * r * wobble))
        rows.append((row, t * height / BARK_REPEAT))
    white = (1.0, 1.0, 1.0, 1.0)
    for (ra, va), (rb, vb) in zip(rows, rows[1:]):
        for i in range(sides):
            u0, u1 = i / sides, (i + 1) / sides
            b.face([ra[i], ra[i + 1], rb[i + 1], rb[i]], white, [(u0, va), (u1, va), (u1, vb), (u0, vb)])
    # Cap the top (hidden in the crown) and the foot (in the ground).
    top = rows[-1][0][:-1]
    b.face(list(reversed(top)), white, [(0.5, 0.5)] * len(top))
    return trunk_axis(1.0, height, lean, bend, yaw)


def green(rng, age):
    """Leaf colour: young fronds a fresh green, old ones yellowing."""
    g = (0.16 + rng.uniform(-0.02, 0.02), 0.30 + rng.uniform(-0.03, 0.03), 0.07)
    y = (0.42, 0.40, 0.14)
    k = max(0.0, age - 0.55) / 0.45
    return tuple(g[i] * (1 - k) + y[i] * k for i in range(3)) + (1.0,)


def brown(rng):
    v = rng.uniform(0.85, 1.1)
    return (0.30 * v, 0.21 * v, 0.11 * v, 1.0)


def build_frond(b, base, yaw, elevation, length, colour, rng, dead=False):
    """One frond from the crown at base, heading out at yaw, leaving at the elevation (radians above level)."""
    out = Vector((math.cos(yaw), 0.0, math.sin(yaw)))
    up = Vector((0.0, 1.0, 0.0))
    droop = 0.55 if not dead else 0.3

    def rib(s):
        # Thrown up and out, then bending over under its weight.
        h = length * s
        return base + out * (h * math.cos(elevation)) + up * (h * math.sin(elevation) - droop * length * s * s)

    def tangent(s):
        return (rib(min(s + 0.01, 1.0)) - rib(max(s - 0.01, 0.0))).normalized()

    # The midrib: a thin tapering strip seen edge-on from above, as a narrow box.
    steps = 14
    prev = None
    for k in range(steps + 1):
        s = k / steps
        c = rib(s)
        t = tangent(s)
        side = t.cross(up)
        if side.length < 1e-4:
            side = out.cross(up)
        side.normalize()
        normal = side.cross(t).normalized()
        w = 0.045 * (1.0 - s) + 0.008
        quad = [b.vert(c + side * w + normal * w * 0.6), b.vert(c - side * w + normal * w * 0.6),
                b.vert(c - side * w * 0.6 - normal * w * 0.5), b.vert(c + side * w * 0.6 - normal * w * 0.5)]
        if prev:
            for i in range(4):
                j = (i + 1) % 4
                b.face([prev[i], prev[j], quad[j], quad[i]], colour)
        prev = quad

    # The leaflets.
    pairs = 38 if not dead else 26
    for side_sign in (-1.0, 1.0):
        for n in range(pairs):
            s = 0.1 + 0.88 * n / (pairs - 1)
            if rng.random() < (0.04 if not dead else 0.25):
                continue  # torn off
            c = rib(s)
            t = tangent(s)
            side = t.cross(up)
            if side.length < 1e-4:
                side = out.cross(up)
            side.normalize()
            # Longest a third of the way out, short at the base and towards the tip.
            l = length * (0.07 + 0.17 * math.sin(math.pi * min(1.0, s / 0.92) ** 0.75)) * rng.uniform(0.9, 1.08)
            # Pointing forward along the rib, out to the side and down into a V; dead ones hang.
            fwd = 0.55 + rng.uniform(-0.08, 0.08)
            dip = (0.45 if not dead else 0.7) + rng.uniform(-0.1, 0.1)
            d0 = (side * side_sign + t * fwd + Vector((0.0, -dip, 0.0))).normalized()
            width = 0.055 * rng.uniform(0.85, 1.15) * (0.75 if dead else 1.0)
            segs = 3
            pts = []
            for k in range(segs + 1):
                u = k / segs
                # The leaflet droops more towards its tip.
                d = (d0 + Vector((0.0, -0.45 * u * u, 0.0))).normalized()
                pts.append(c + d * (l * u) if k == 0 else pts[-1] + d * (l / segs))
            leaf_normal = d0.cross(t).normalized() * side_sign
            if leaf_normal.y < 0:
                leaf_normal = -leaf_normal
            across = d0.cross(leaf_normal).normalized()
            tip_colour = tuple(min(1.0, ch * 1.12) for ch in colour[:3]) + (1.0,)
            rows = []
            for k, p in enumerate(pts):
                u = k / segs
                w = width * (math.sin(math.pi * (0.15 + 0.85 * u)) if k < segs else 0.15)
                fold = leaf_normal * w * 0.35  # folded up along its middle
                rows.append([b.vert(p + across * w), b.vert(p + fold), b.vert(p - across * w)])
            for k in range(segs):
                col = colour if k < segs - 1 else tip_colour
                b.face([rows[k][0], rows[k][1], rows[k + 1][1], rows[k + 1][0]], col)
                b.face([rows[k][1], rows[k][2], rows[k + 1][2], rows[k + 1][1]], col)


def build_coconuts(b, top, rng):
    count = rng.randint(4, 7)
    for i in range(count):
        a = rng.uniform(0, math.tau)
        c = top + Vector((math.cos(a) * 0.32, -0.35 - rng.uniform(0, 0.25), math.sin(a) * 0.32))
        r = rng.uniform(0.11, 0.14)
        col = (0.33, 0.36, 0.10, 1.0) if rng.random() < 0.6 else (0.38, 0.27, 0.12, 1.0)
        rows = []
        n, m = 8, 6
        for j in range(m + 1):
            phi = math.pi * j / m
            row = []
            for k in range(n):
                th = math.tau * k / n
                row.append(b.vert(c + Vector((math.sin(phi) * math.cos(th) * r, math.cos(phi) * r * 1.15, math.sin(phi) * math.sin(th) * r))))
            rows.append(row)
        for j in range(m):
            for k in range(n):
                kk = (k + 1) % n
                b.face([rows[j][k], rows[j][kk], rows[j + 1][kk], rows[j + 1][k]], col)


def build(name, height, lean, bend, fronds, seed):
    rng = random.Random(seed)
    reset_scene()
    trunk = Builder("Trunk")
    top = build_trunk(trunk, height, lean, bend, rng.uniform(0, math.tau), rng)
    crown = Builder("Crown")
    # Leaf bases round the top of the trunk.
    base = top + Vector((0.0, 0.15, 0.0))
    golden = math.pi * (3.0 - math.sqrt(5.0))
    for i in range(fronds):
        age = i / max(fronds - 1, 1)  # 0 = the newest, in the middle on top; 1 = the oldest, lowest
        yaw = i * golden + rng.uniform(-0.15, 0.15)
        elevation = math.radians(70 - 95 * age + rng.uniform(-6, 6))
        length = rng.uniform(4.3, 5.4) * (0.75 + 0.25 * min(1.0, age * 2.0))
        build_frond(crown, base - Vector((0.0, 0.12 * age, 0.0)), yaw, elevation, length, green(rng, age), rng)
    # Two or three dead fronds hanging against the trunk.
    for i in range(rng.randint(2, 3)):
        yaw = rng.uniform(0, math.tau)
        build_frond(crown, base - Vector((0.0, 0.25, 0.0)), yaw, math.radians(-68 + rng.uniform(-8, 8)), rng.uniform(2.3, 2.9), brown(rng), rng, dead=True)
    build_coconuts(crown, top, rng)
    objs = [trunk.finish(), crown.finish()]
    os.makedirs(OUT, exist_ok=True)
    export_glb(os.path.join(OUT, name + ".glb"), objs)
    print(f"{name}: crown top at {tuple(round(v, 2) for v in top)}")


for name, spec in VARIANTS.items():
    build(name, **spec)
