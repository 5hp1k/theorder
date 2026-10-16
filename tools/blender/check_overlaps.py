"""Finds surfaces that fight: places where two separate solids of a machine — two parts, or two pieces of
one part — have faces lying in the same plane, facing the same way and covering the same patch. In the
game such a patch flickers between the two (z-fighting). Within one solid, two faces covering the same
patch of one plane mean its surface is folded over itself (a cap whose outline crosses itself). Also finds faces turned inside out on a closed
solid: the game draws only the front of a face, so such a face is a hole to see through. Run it after a model script's preview, which
stands every part where it stands in the scene:
    blender -b --python tools/blender/mechs.py --python tools/blender/check_overlaps.py -- /tmp/out executioner preview
Prints one line per fighting pair: the two meshes, the area they share and where (game axes, metres).
"""
from collections import defaultdict

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

GAP = 0.004        # faces closer than this to one plane fight
MIN_AREA = 0.0004  # m²; smaller overlaps are not seen
BOXES = {}         # (mesh, solid) -> its bounds in game axes, to tell which solid it is


def islands(bm):
    """Index of the solid (connected piece) each face belongs to."""
    seen = {}
    n = 0
    for f in bm.faces:
        if f in seen:
            continue
        stack = [f]
        seen[f] = n
        while stack:
            g = stack.pop()
            for e in g.edges:
                for h in e.link_faces:
                    if h not in seen:
                        seen[h] = n
                        stack.append(h)
        n += 1
    return seen


def clip(subject, edge_a, edge_b):
    out = []
    for i, p in enumerate(subject):
        q = subject[i - 1]
        side_p = (edge_b - edge_a).cross(p - edge_a) >= 0
        side_q = (edge_b - edge_a).cross(q - edge_a) >= 0
        if side_p != side_q:
            d = (p - q)
            t = (edge_b - edge_a).cross(edge_a - q) / (edge_b - edge_a).cross(d) if (edge_b - edge_a).cross(d) else 0
            out.append(q + d * t)
        if side_p:
            out.append(p)
    return out


def area(poly):
    return abs(sum(poly[i - 1].cross(poly[i]) for i in range(len(poly)))) / 2 if len(poly) > 2 else 0.0


def overlap(a, b):
    """Area shared by two convex polygons (2D Vectors, counter-clockwise)."""
    out = a
    for i in range(len(b)):
        if not out:
            return 0.0
        out = clip(out, b[i - 1], b[i])
    return area(out)


def flipped(obj):
    """Closed solids with faces turned the wrong way, by solid: how many edges join two faces that run
    round it the same way (a patch turned over), or, when that is none, whether the whole solid is
    inside out (negative volume)."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.transform(obj.matrix_world)
    isl = islands(bm)
    by = defaultdict(list)
    for f in bm.faces:
        by[isl[f]].append(f)
    out = {}
    for ident, fs in by.items():
        edges = {e for f in fs for e in f.edges}
        if any(len(e.link_faces) != 2 for e in edges):
            continue  # an open shell: no inside to face
        wrong = sum(1 for e in edges if e.link_loops[0].vert == e.link_loops[1].vert)
        volume = 0.0
        for f in fs:
            vs = [v.co for v in f.verts]
            for k in range(1, len(vs) - 1):
                volume += vs[0].dot(vs[k].cross(vs[k + 1])) / 6
        if wrong or volume < 0:
            out[ident] = (wrong, volume)
    bm.free()
    return out


def faces_of(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.transform(obj.matrix_world)
    bm.normal_update()
    isl = islands(bm)
    for f in bm.faces:
        for v in f.verts:
            lo, hi = BOXES.setdefault((obj.name, isl[f]), [Vector((1e9,) * 3), Vector((-1e9,) * 3)])
            co = Vector((v.co.x, v.co.z, -v.co.y))
            BOXES[(obj.name, isl[f])] = [Vector(map(min, lo, co)), Vector(map(max, hi, co))]
    out = []
    for f in bm.faces:
        if f.calc_area() < MIN_AREA:
            continue
        n = f.normal.normalized()
        out.append((obj.name, isl[f], n, n.dot(f.verts[0].co), [v.co.copy() for v in f.verts], f.calc_center_median()))
    bm.free()
    return out


def flat(points, n):
    u = n.orthogonal().normalized()
    w = n.cross(u)
    pts = [Vector((p.dot(u), p.dot(w))) for p in points]
    # counter-clockwise
    if sum(pts[i - 1].cross(pts[i]) for i in range(len(pts))) < 0:
        pts.reverse()
    return pts


def main():
    faces = []
    for obj in bpy.context.scene.objects:
        if obj.type == "MESH":
            faces += faces_of(obj)
    buckets = defaultdict(list)
    for i, (_, _, n, d, _, _) in enumerate(faces):
        # a face and one turned the other way in the same plane land in the same bucket
        if max(n, key=abs) < 0:
            n, d = -n, -d
        buckets[(round(n.x * 40), round(n.y * 40), round(n.z * 40), round(d / GAP))].append(i)
    found = defaultdict(lambda: [0.0, None])
    for (nx, ny, nz, dk), members in buckets.items():
        nxt = buckets.get((nx, ny, nz, dk + 1), [])
        for x, i in enumerate(members):
            oa, ia, na, da, pa, ca = faces[i]
            for j in members[x + 1:] + nxt:
                ob, ib, nb, db, pb, cb = faces[j]
                same = (oa, ia) == (ob, ib)
                if na.dot(nb) < 0:
                    db, nb = -db, -nb
                if abs(da - db) > GAP:
                    continue
                flipped_pair = faces[j][2].dot(na) < -0.999
                if na.dot(nb) < 0.999 or (flipped_pair and not same):
                    continue
                if (ca - cb).length > 6:
                    continue
                shared = overlap(flat(pa, na), flat(pb, na))
                if shared < MIN_AREA:
                    continue
                key = tuple(sorted([(oa, ia), (ob, ib)]))
                found[key][0] += shared
                if found[key][1] is None:
                    found[key][1] = (ca, na)
    inside_out = []
    for obj in bpy.context.scene.objects:
        if obj.type == "MESH":
            inside_out += [(obj.name, ident, wrong, volume) for ident, (wrong, volume) in flipped(obj).items()]
    print("FLIPPED", len(inside_out))
    for name, ident, wrong, volume in inside_out:
        lo, hi = BOXES.get((name, ident), (Vector(), Vector()))
        print("INSIDE-OUT %s#%d  %d edges turned, volume %.3f  from (%.2f, %.2f, %.2f) to (%.2f, %.2f, %.2f)" % (name, ident, wrong, volume, lo.x, lo.y, lo.z, hi.x, hi.y, hi.z))
    print("OVERLAPS", len(found))
    for ((oa, ia), (ob, ib)), (shared, (at, n)) in sorted(found.items(), key=lambda kv: -kv[1][0]):
        print("FIGHT %s#%d  %s#%d  %.4f m2  at (%.2f, %.2f, %.2f)  facing (%.2f, %.2f, %.2f)" % (oa, ia, ob, ib, shared, at.x, at.z, -at.y, n.x, n.z, -n.y))
        for key in ((oa, ia), (ob, ib)):
            lo, hi = BOXES[key]
            print("      %s#%d  from (%.2f, %.2f, %.2f) to (%.2f, %.2f, %.2f)" % (key[0], key[1], lo.x, lo.y, lo.z, hi.x, hi.y, hi.z))


main()
