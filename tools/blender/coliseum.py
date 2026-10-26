"""The Arena map's coliseum, after Steiner's Coliseum on Solaris VII as MechWarrior 4 shows it: a
neoclassical bowl round a floor of sand. The arena wall is a blind arcade — engaged columns, arches with
keystones, a cornice and a parapet — and over it runs the ferroglass shield of the box seats, bronze
mullions, the great screens behind it. Three tiers of cushioned seats rise above, parted by walkways and
the dark mouths of the vomitoria; a colonnade under an entablature crowns the top, obelisks on it. At
either end of the floor a triumphal arch; the east one over the ramp that runs down to the 'Mech bays, the
west one choked with fallen masonry. The bowl is broken unevenly: a great collapse to the north-west that
has taken the top tier and, in its middle, the second, a lesser one, gaps in the parapet.
    blender -b --python tools/blender/coliseum.py -- <out dir>
    godot --headless --path . -s tools/blender/import_parts.gd -- <out>/coliseum.glb res://models/arena
The scene (scenes/arena/coliseum.tscn) hangs the meshes on a StaticBody3D whose collision is made from
them (MeshCollision). Game axes, metres; the middle of the arena floor is the origin. The ramps
themselves are cut into the ground by the map's terrain (MapTerrain.Ramps): RAMP below says where."""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bmesh  # noqa: E402
from mathutils import Vector  # noqa: E402
from meshkit import *  # noqa: E402

A, B = 280.0, 220.0      # semi-axes of the arena wall's face (x, z)
BAYS = 64
BASE = -12.0
WALL = 22.0              # top of the arena wall's cornice (a mech stands 12–15 m)
RAMP_START, RAMP_DEPTH, RAMP_HALF = A - 70.0, 8.0, 16.0     # the ramps: from 70 m in to the wall, 8 m down, 32 m wide
GATE = 19.0 / A          # half the angle the gate arches take
MEAN = (A + B) / 2
# Where the bowl has come down: the top tier, the colonnade and its entablature are gone, rubble lies on
# the seats below (angle of the middle, half its width in angle). The first is the great collapse: in its
# middle the second tier has gone too. Not opposite one another: the place is not a mirror.
BROKEN = [(2.4, 0.26), (4.62, 0.07)]
DEEP = 0.55              # how far into a collapse the second tier is gone as well
PARAPET_GAPS = [(4.05, 0.035), (0.92, 0.022), (5.6, 0.05)]
# The west gate (at pi) is sealed: its arch stands, but the ramp behind is filled with fallen masonry.
OPEN_GATES = (0.0,)


def broken(t, pad=0.0):
    """How far into a broken stretch t is: 0 outside, 1 in its middle."""
    for c, w in BROKEN:
        d = abs(math.atan2(math.sin(t - c), math.cos(t - c)))
        if d < w + pad:
            return 1.0 - d / (w + pad)
    return 0.0


def at(t, d):
    return math.cos(t) * (A + d), math.sin(t) * (B + d)


def P(t, d, y):
    x, z = at(t, d)
    return Vector((x, y, z))


def ring(part, profile, t0, t1, n, kind=PAINT, bevel=0.0, caps=True):
    secs = []
    for i in range(n + 1):
        t = t0 + (t1 - t0) * i / n
        secs.append([tuple(P(t, d, y)) for d, y in profile])
    return part.loft(secs, kind, bevel, None, (), caps)


def varying(part, profile_at, t0, t1, n, kind=PAINT, bevel=0.0):
    secs = []
    for i in range(n + 1):
        t = t0 + (t1 - t0) * i / n
        secs.append([tuple(P(t, d, y)) for d, y in profile_at(i / n)])
    return part.loft(secs, kind, bevel)


def gate_t(t, extra=0.0):
    return any(abs(math.atan2(math.sin(t - c), math.cos(t - c))) < GATE + extra for c in (0.0, math.pi))


def seating(d0, y0, rows, riser, tread, under=3.0):
    pts = [(d0, y0 - under), (d0, y0)]
    d, y = d0, y0
    for _ in range(rows):
        y += riser
        pts += [(d, y), (d + tread - 0.18, y), (d + tread - 0.14, y + 0.44), (d + tread - 0.05, y + 0.46), (d + tread - 0.04, y)]
        d += tread
    pts.append((d, y - under))
    return pts, d, y


def flight(d0, y0, rows, riser, tread, under=3.2):
    pts = [(d0, y0 - under), (d0, y0)]
    d, y = d0, y0
    for _ in range(rows * 2):
        y += riser / 2
        pts.append((d, y))
        d += tread / 2
        pts.append((d, y))
    pts.append((d, y - under))
    return pts


def runs():
    return ((GATE, math.pi - GATE), (math.pi + GATE, math.tau - GATE))


def build():
    parts = {}

    def part(name):
        if name not in parts:
            parts[name] = Part(name)
        return parts[name]

    bay = math.tau / BAYS

    # ── The arena wall: plinth, a blind arcade (engaged columns, arches, keystones), cornice, parapet ──
    wall = [(0.0, BASE), (0.0, WALL - 2.4), (4.5, WALL - 2.4), (4.5, BASE)]
    plinth = [(-0.9, BASE), (-0.9, 1.2), (-0.5, 1.8), (0.05, 1.8), (0.05, BASE)]
    cornice = [(0.05, WALL - 2.6), (-0.4, WALL - 2.3), (-0.4, WALL - 1.8), (-1.1, WALL - 1.3), (-1.1, WALL - 0.6), (-0.6, WALL), (4.5, WALL), (4.5, WALL - 2.6)]
    parapet = [(-0.5, WALL - 0.05), (-0.5, WALL + 1.6), (-0.8, WALL + 1.8), (-0.8, WALL + 2.2), (0.6, WALL + 2.2), (0.6, WALL + 1.8), (0.3, WALL + 1.6), (0.3, WALL - 0.05)]
    for t0, t1 in runs():
        n = max(2, int((t1 - t0) / bay * 3))
        for prof, name in ((wall, "Wall"), (plinth, "Wall"), (cornice, "Wall")):
            ring(part(name), prof, t0, t1, n)
        # the parapet, with its knocked-off stretches left out
        cuts = sorted((c - w, c + w) for c, w in PARAPET_GAPS if t0 < c < t1)
        a0 = t0
        for g0, g1 in cuts + [(t1, t1)]:
            if g0 > a0:
                ring(part("Wall"), parapet, a0, g0, max(2, int((g0 - a0) / bay * 3)))
            a0 = g1
    col = [(0, 0.95), (0.03, 0.95), (0.06, 0.82), (0.1, 0.78), (0.5, 0.74), (0.88, 0.7), (0.9, 0.82), (0.95, 1.0), (1.0, 1.05)]
    for i in range(BAYS):
        t = i * bay
        if not gate_t(t, 0.02):
            # an engaged column between the arches: its shaft half sunk in the wall, base and capital
            x, z = at(t, -0.1)
            part("Columns").lathe((x, 1.8, z), (x, WALL - 2.6, z), col, segs=12, kind=PAINT)
        tm = t + bay / 2
        if gate_t(tm, 0.03):
            continue
        hw = bay * 0.34
        # the arch: a recessed panel with a round head, its archivolt standing proud, a keystone at its crown
        top = WALL - 6.5
        spring = top - (A + B) / 2 * hw
        def recess(k, hw=hw, top=top, spring=spring):
            s = (k - 0.5) * 2.0
            yb = spring + (top - spring) * math.sqrt(max(0.0, 1.0 - s * s))
            return [(-0.02, 1.8), (-0.02, yb), (0.25, yb), (0.25, 1.8)]
        varying(part("Arches"), recess, tm - hw, tm + hw, 12)
        def archivolt(k, hw=hw, top=top, spring=spring):
            s = (k - 0.5) * 2.0
            c = math.sqrt(max(0.0, 1.0 - s * s))
            yb = spring + (top - spring) * c
            return [(-0.45, yb), (-0.45, yb + 1.0), (0.05, yb + 1.0), (0.05, yb)]
        varying(part("Wall"), archivolt, tm - hw * 1.12, tm + hw * 1.12, 14)
        ring(part("Wall"), [(-0.7, top + 0.6), (-0.7, top + 2.4), (0.05, top + 2.4), (0.05, top + 0.6)], tm - bay * 0.03, tm + bay * 0.03, 1, PAINT, 0.03)   # keystone
        ring(part("Wall"), [(-0.6, spring - 0.6), (-0.6, spring), (0.05, spring), (0.05, spring - 0.6)], tm - hw * 1.15, tm - hw * 0.95, 1, PAINT, 0.02)    # imposts
        ring(part("Wall"), [(-0.6, spring - 0.6), (-0.6, spring), (0.05, spring), (0.05, spring - 0.6)], tm + hw * 0.95, tm + hw * 1.15, 1, PAINT, 0.02)

    # ── The ferroglass shield of the box seats, bronze mullions, the screens behind it ──
    glass = [(0.5, WALL + 2.1), (2.6, WALL + 9.0), (2.9, WALL + 9.0), (0.8, WALL + 2.1)]
    rooms = [(3.4, WALL + 2.0), (3.4, WALL + 9.4), (9.0, WALL + 9.4), (9.0, WALL + 2.0)]
    lid = [(2.0, WALL + 8.9), (2.0, WALL + 9.6), (1.4, WALL + 10.0), (1.4, WALL + 10.8), (11.0, WALL + 10.8), (11.0, WALL + 8.9)]
    for t0, t1 in runs():
        n = max(2, int((t1 - t0) / bay * 3))
        ring(part("Glass"), glass, t0, t1, n)
        ring(part("Boxes"), rooms, t0, t1, n)
        ring(part("Wall"), lid, t0, t1, n)
    for i in range(BAYS * 2):
        t = i * bay / 2
        if gate_t(t, 0.01):
            continue
        w = 0.2 / MEAN
        ring(part("Bronze"), [(0.35, WALL + 2.1), (2.45, WALL + 9.0), (3.0, WALL + 9.0), (0.9, WALL + 2.1)], t - w, t + w, 1, FRAME)
    for c, size in ((math.pi * 0.47, 9.0), (math.pi * 1.52, 11.0), (math.pi * 0.21, 7.0), (math.pi * 0.83, 9.0), (math.pi * 1.71, 6.0)):
        w = size / MEAN
        ring(part("Screens"), [(3.2, WALL + 3.0), (3.2, WALL + 8.4), (3.35, WALL + 8.4), (3.35, WALL + 3.0)], c - w, c + w, 6)
        ring(part("Bronze"), [(3.0, WALL + 2.6), (3.0, WALL + 8.8), (3.3, WALL + 8.8), (3.3, WALL + 2.6)], c - w * 1.04, c - w * 0.98, 1, FRAME)
        ring(part("Bronze"), [(3.0, WALL + 2.6), (3.0, WALL + 8.8), (3.3, WALL + 8.8), (3.3, WALL + 2.6)], c + w * 0.98, c + w * 1.04, 1, FRAME)

    # ── The triumphal arches over the ramps: the opening, columns either side, an attic over it ──
    for c in (0.0, math.pi):
        t0, t1 = c - GATE, c + GATE
        opening_top = 16.0
        def soffit(k, c=c):
            s = (k - 0.5) * 2.0
            yb = 4.0 + (opening_top - 4.0) * math.sqrt(max(0.0, 1.0 - s * s))
            return [(-2.0, yb), (-2.0, WALL + 10.8), (9.0, WALL + 10.8), (9.0, yb)]
        varying(part("Wall"), soffit, t0, t1, 24)
        sealed = c not in OPEN_GATES
        low = -1.0 if sealed else -RAMP_DEPTH - 1.0
        ring(part("Tunnel"), [(9.0, low), (9.0, opening_top + 1.0), (11.0, opening_top + 1.0), (11.0, low)], t0, t1, 6)   # its dark back
        for side in (-1, 1):
            j = c + side * GATE
            j2 = c + side * (GATE + 9.0 / A)
            ring(part("Wall"), [(-3.0, -RAMP_DEPTH - 1.0), (-3.0, WALL + 10.8), (9.0, WALL + 10.8), (9.0, -RAMP_DEPTH - 1.0)], min(j, j2), max(j, j2), 2, PAINT, 0.04)  # piers
            for k in (0.25, 0.75):
                tt = j + (j2 - j) * k
                x, z = at(tt, -3.6)
                part("Columns").lathe((x, -0.2, z), (x, WALL + 3.0, z), col, segs=12, kind=PAINT)          # columns flanking it
            for y in (-0.6, WALL + 3.0):
                ring(part("Wall"), [(-4.8, y), (-4.8, y + 1.0), (-2.9, y + 1.0), (-2.9, y)], min(j, j2), max(j, j2), 2, PAINT, 0.03)
        ring(part("Wall"), [(-3.6, WALL + 4.0), (-3.6, WALL + 12.5), (9.0, WALL + 12.5), (9.0, WALL + 4.0)], c - GATE - 9.0 / A, c + GATE + 9.0 / A, 8, PAINT, 0.04)  # the attic
        ring(part("Bronze"), [(-3.9, WALL + 6.0), (-3.9, WALL + 10.5), (-3.55, WALL + 10.5), (-3.55, WALL + 6.0)], c - GATE * 0.8, c + GATE * 0.8, 8, FRAME)   # its inscription panel
        if sealed:
            # the opening choked to half its height with fallen blocks, spilling out onto the floor
            import random as _r
            rr = _r.Random(11)
            for _ in range(70):
                t = c + rr.uniform(-GATE * 1.05, GATE * 1.05)
                d = rr.uniform(-16.0, 9.0)
                y = max(0.0, 9.0 * (1.0 - max(0.0, -d) / 16.0)) * rr.uniform(0.5, 1.0) - 0.4
                x, z = at(t, d)
                r = rr.uniform(1.0, 3.2)
                pts = [(x + rr.uniform(-r, r), y + rr.uniform(-0.4, 0.2), z + rr.uniform(-r, r)) for _ in range(3)]
                part("Rubble").loft([pts, [(x + rr.uniform(-0.5, 0.5), y + r * rr.uniform(0.6, 1.1), z + rr.uniform(-0.5, 0.5))] * 3], PAINT, 0.0)
            continue
        # the walls along the ramp, from the floor down to the arch
        for side in (-1, 1):
            secs = []
            for k in range(13):
                x0 = math.copysign(RAMP_START + (A + 2.0 - RAMP_START) * k / 12, math.cos(c))
                t = k / 12
                bottom = -RAMP_DEPTH * min(1.0, t * (A - RAMP_START + 2) / (A - RAMP_START)) - 1.0
                z0 = side * (RAMP_HALF + 1.0)
                secs.append([(x0, bottom, z0 - 0.8), (x0, 1.4, z0 - 0.8), (x0, 1.4, z0 + 0.8), (x0, bottom, z0 + 0.8)])
            part("Ramps").loft(secs, PAINT, 0.03)

    # ── The stands: three tiers of cushioned seats between stairs, walkways and baltei between ──
    stair = 0.9 / MEAN
    tiers = [(11.0, WALL + 10.8, 26, 0.5, 0.85), (37.6, WALL + 10.8 + 13.0 + 3.0, 24, 0.55, 0.85), (62.0, WALL + 10.8 + 13.0 + 3.0 + 13.2 + 3.0, 16, 0.62, 0.85)]
    top_d = top_y = 0.0
    ends = []
    for k, (d0, y0, rows, riser, tread) in enumerate(tiers):
        seats, d_end, y_end = seating(d0, y0, rows, riser, tread)
        steps_ = flight(d0, y0, rows, riser, tread)
        for i in range(BAYS):
            t = i * bay
            gone = broken(t + bay / 2)
            if (k == 2 and gone > 0.0) or (k == 1 and gone > DEEP):
                continue
            ring(part(f"Seats{i * 8 // BAYS}"), seats, t + stair, t + bay - stair, 2)
            ring(part("Stairs"), steps_, t - stair, t + stair, 1, caps=False)
        if k < 2:
            walk = [(d_end, y_end - 3.0), (d_end, y_end), (d_end + 3.6, y_end), (d_end + 3.6, y_end - 3.0)]
            front = [(d_end + 3.6, y_end - 3.0), (d_end + 3.6, y_end + 3.0), (d_end + 4.6, y_end + 3.0), (d_end + 4.6, y_end - 3.0)]
            coping = [(d_end + 3.45, y_end + 2.9), (d_end + 3.45, y_end + 3.2), (d_end + 4.75, y_end + 3.2), (d_end + 4.75, y_end + 2.9)]
            if k == 0:
                for prof in (walk, front, coping):
                    ring(part("Walks"), prof, 0, math.tau, BAYS * 2, PAINT, 0.0, caps=False)
            else:
                # (round the deep collapse it is broken off)
                c0, w0 = BROKEN[0]
                ga, gb = c0 - w0 * (1.0 - DEEP), c0 + w0 * (1.0 - DEEP)
                n2 = int(BAYS * 2 * (math.tau - (gb - ga)) / math.tau)
                for prof in (walk, front, coping):
                    ring(part("Walks"), prof, gb, ga + math.tau, n2, PAINT, 0.0, caps=True)
            for i in range(0, BAYS, 2):
                t = (i + 0.5) * bay
                hw = 1.4 / (MEAN + d_end)
                ring(part("Doors"), [(d_end + 3.56, y_end), (d_end + 3.56, y_end + 2.4), (d_end + 3.8, y_end + 2.4), (d_end + 3.8, y_end)], t - hw, t + hw, 2)
                ring(part("Walks"), [(d_end + 3.35, y_end + 2.4), (d_end + 3.35, y_end + 2.75), (d_end + 3.7, y_end + 2.75), (d_end + 3.7, y_end + 2.4)], t - hw * 1.3, t + hw * 1.3, 2, PAINT, 0.02)
        top_d, top_y = d_end, y_end
        ends.append((d_end, y_end))

    # ── The colonnade along the top under its entablature, obelisks on it, the outer wall ──
    floor = [(top_d, top_y - 3.0), (top_d, top_y), (top_d + 7.0, top_y), (top_d + 7.0, top_y - 3.0)]
    def whole(prof, name, n=BAYS * 2, drop=None):
        """A ring round the top, left out (or brought down to 'drop') where the bowl has come down."""
        step = math.tau / n
        start = None
        for i in range(n + 1):
            t = i * step
            gone = i < n and broken(t + step / 2) > 0.0
            if not gone and start is None and i < n:
                start = t
            if (gone or i == n) and start is not None:
                ring(part(name), prof, start, t, max(1, int(round((t - start) / step))), PAINT, 0.0, caps=True)
                start = None
    whole(floor, "Gallery")
    roof_y = top_y + 10.0
    entab = [(top_d - 0.3, roof_y), (top_d - 0.3, roof_y + 1.0), (top_d - 0.5, roof_y + 1.0), (top_d - 0.5, roof_y + 2.2), (top_d - 1.2, roof_y + 2.6), (top_d - 1.2, roof_y + 3.1),
             (top_d + 8.0, roof_y + 3.1), (top_d + 8.0, roof_y)]
    whole(entab, "Gallery")
    outer = [(top_d + 7.0, BASE), (top_d + 7.0, roof_y + 3.0), (top_d + 9.5, roof_y + 3.0), (top_d + 9.5, BASE)]
    whole(outer, "Outer")
    low_outer = [(top_d + 7.0, BASE), (top_d + 7.0, top_y - 14.0), (top_d + 9.5, top_y - 14.0), (top_d + 9.5, BASE)]
    for c, w in BROKEN:
        ring(part("Outer"), low_outer, c - w, c + w, 8)
    tall = [(0, 0.72), (0.03, 0.72), (0.05, 0.6), (0.08, 0.56), (0.5, 0.5), (0.88, 0.45), (0.9, 0.56), (0.95, 0.74), (1.0, 0.78)]
    for i in range(BAYS):
        for k in (0.25, 0.75):
            t = (i + k) * bay
            if broken(t, 0.02) > 0.0:
                continue
            x, z = at(t, top_d + 1.4)
            part("Columns").lathe((x, top_y - 0.02, z), (x, roof_y + 0.02, z), tall, segs=10, kind=PAINT)
        # obelisks, not every fourth bay like a fence: some fallen, the rest of uneven height
        if i % 4 == 0 and broken(i * bay, 0.03) == 0.0 and (i * 7) % 5 != 1:
            t = i * bay
            tall_ = 1.0 + ((i * 13) % 7) / 10.0
            x, z = at(t, top_d + 3.5)
            # an obelisk on a stepped base, on the entablature
            part("Obelisks").stations("y", [(roof_y + 3.0, 3.0, 3.0, x, z, 0.2), (roof_y + 3.8, 3.0, 3.0, x, z, 0.2), (roof_y + 3.8, 2.4, 2.4, x, z, 0.15),
                                            (roof_y + 4.6, 2.4, 2.4, x, z, 0.15), (roof_y + 4.6, 1.6, 1.6, x, z, 0.1), (roof_y + 4.6 + 8.4 * tall_, 1.0, 1.0, x, z, 0.06),
                                            (roof_y + 6.0 + 8.4 * tall_, 0.05, 0.05, x, z, 0.01)], kind=PAINT, bevel=0.0)
    import random
    rng = random.Random(5)
    # The great collapse's slope of fallen masonry: from the parapet of the first tier's walkway up to the
    # broken edge of the top, solid down to the ground (under the stands there is nothing else to stand on).
    c0, w0 = BROKEN[0]
    (d_e0, y_e0) = ends[0]
    slope_d0, slope_d1 = d_e0 + 4.6, top_d + 7.0
    y_lo, y_hi = y_e0 + 3.0, top_y - 7.0
    def slope_y(d):
        k = max(0.0, min(1.0, (d - slope_d0) / (slope_d1 - slope_d0)))
        return y_lo + (y_hi - y_lo) * (k * 0.8 + k * k * 0.2)
    sa, sb = c0 - w0 * (1.0 - DEEP) * 1.08, c0 + w0 * (1.0 - DEEP) * 1.08
    prof = [(slope_d0, BASE), (slope_d0, y_lo)] + [(slope_d0 + (slope_d1 - slope_d0) * i / 6, slope_y(slope_d0 + (slope_d1 - slope_d0) * i / 6)) for i in range(1, 7)] + [(slope_d1, BASE)]
    ring(part("Rubble"), prof, sa, sb, 10)
    for c, w in BROKEN:
        deep = (c, w) == BROKEN[0]
        for _ in range(int(w * (420 if deep else 260))):
            t = c + rng.uniform(-w, w)
            far = abs(math.atan2(math.sin(t - c), math.cos(t - c))) < w * (1.0 - DEEP)
            if deep and far:
                # where the second tier is gone, its stones lie on the slumped slope over the void
                d = rng.uniform(slope_d0, slope_d1)
                y = slope_y(d)
            else:
                # on the seats of the second tier, below where the top one stood
                d = rng.uniform(tiers[1][0] + 4.0, tiers[2][0] - 1.0)
                y = WALL + 10.8 + 13.0 + 3.0 + (d - tiers[1][0]) * 0.62
            x, z = at(t, d)
            r = rng.uniform(1.2, 3.6)
            pts = [(x + rng.uniform(-r, r), y + rng.uniform(-0.3, 0.2), z + rng.uniform(-r, r)) for _ in range(3)]
            top = (x + rng.uniform(-0.4, 0.4), y + r * rng.uniform(0.6, 1.1), z + rng.uniform(-0.4, 0.4))
            part("Rubble").loft([pts, [top, top, top]], PAINT, 0.0)
        for _ in range(6):
            t = c + rng.uniform(-w * 0.8, w * 0.8)
            d = rng.uniform(tiers[1][0] + 8.0, top_d)
            y = WALL + 10.8 + 13.0 + 3.0 + (d - tiers[1][0]) * 0.62 + 0.8
            x, z = at(t, d)
            a = rng.uniform(0, math.tau)
            dv = Vector((math.cos(a), rng.uniform(-0.15, 0.15), math.sin(a)))
            part("Rubble").lathe(tuple(Vector((x, y, z)) - dv * 1.0), tuple(Vector((x, y, z)) + dv * 1.0), [(0, 0.72), (0.5, 0.75), (1, 0.72)], segs=10, kind=PAINT)
    # the parapet's knocked-off stones at the foot of the wall
    for _ in range(24):
        gc, gw = PARAPET_GAPS[rng.randrange(len(PARAPET_GAPS))]
        t = gc + rng.uniform(-gw * 1.5, gw * 1.5)
        x, z = at(t, rng.uniform(-14.0, -2.0))
        s_ = rng.uniform(0.5, 1.2)
        pts = [(x + rng.uniform(-s_, s_), -0.3, z + rng.uniform(-s_, s_)) for _ in range(3)]
        part("Rubble").loft([pts, [(x, s_ * 1.2, z)] * 3], PAINT, 0.0)
    return parts


def main():
    out = sys.argv[sys.argv.index("--") + 1]
    os.makedirs(out, exist_ok=True)
    parts = build()
    scene = reset_scene()
    objs, tris = [], 0
    for name, p in parts.items():
        bmesh.ops.recalc_face_normals(p.bm, faces=list(p.bm.faces))
        n = sum(max(len(f.verts) - 2, 1) for f in p.bm.faces)
        tris += n
        print("PART", name, n)
        objs.append(p.to_object(scene.collection))
    export_glb(os.path.join(out, "coliseum.glb"), objs)
    print("COLISEUM PARTS", len(objs), "TRIS", tris)


main()
