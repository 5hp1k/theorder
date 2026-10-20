"""Checks a unit as the game puts it together — every visible mesh where it stands, weapons on their hard
points, legs on the ground — for the two faults a model script can't see by itself:
  * loose pieces: solids (or groups of solids) that touch nothing of the rest of the machine — they hang
    in the air beside it;
  * fighting faces: two solids with faces in one plane, facing the same way, over the same patch (they
    flicker in the game), between parts that come from different places (a gun and the plate it is on);
  * clipping weapons: a weapon's model running into another solid (the machine, or the weapon beside it)
    further forward than its back end, which is meant to sit on the mount (needs the "# weapon" lines
    the export writes before each weapon's mesh: its back end and which way it points).
Input is an OBJ written by the game (tools/export_unit.tscn; tools/export_units.sh exports and checks
every unit), in game axes, one object per mesh node:
    blender -b --python tools/blender/check_assembled.py -- unit.obj [unit2.obj ...]
or, without one, the model a script has just stood up for its preview (no weapons, legs as in the scene):
    blender -b --python tools/blender/mechs.py --python tools/blender/check_assembled.py -- /tmp/out hound preview
Prints one line per fault: the meshes (node paths), where (game axes, metres) and how big.
"""
import sys
from collections import defaultdict

import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree

TOUCH = 0.004      # solids closer than this touch
GAP = 0.004        # faces closer than this to one plane fight
MIN_AREA = 0.0004  # m²; smaller overlaps are not seen
MIN_LOOSE = 0.02   # m; loose crumbs smaller than this across are not reported
CLIP_FROM = 0.08   # m; a weapon may be let into its mount this far from its back end
CLIP_DEPTH = 0.005 # m; a weapon's point deeper than this inside something else is clipping


WEAPONS = {}  # mesh -> (back end, forward), from the "# weapon" lines


def read_obj(path):
    objects, verts, name = {}, [], None
    weapon = None
    WEAPONS.clear()
    for line in open(path):
        if line.startswith("# weapon "):
            n = list(map(float, line.split()[2:8]))
            weapon = (Vector(n[:3]), Vector(n[3:]))
        elif line.startswith("o "):
            name = line[2:].strip()
            objects[name] = []
            if weapon:
                WEAPONS[name] = weapon
            weapon = None
        elif line.startswith("v "):
            verts.append(Vector(map(float, line.split()[1:4])))
        elif line.startswith("f "):
            objects[name].append(tuple(int(i.split("/")[0]) - 1 for i in line.split()[1:4]))
    return objects, verts


class Solid:
    def __init__(self, mesh, index, faces):
        self.mesh, self.index = mesh, index
        self.faces = faces  # lists of points
        pts = [p for f in faces for p in f]
        self.lo = Vector(map(min, *pts)) if len(pts) > 1 else pts[0].copy()
        self.hi = Vector(map(max, *pts)) if len(pts) > 1 else pts[0].copy()
        verts, polys, ids = [], [], {}
        for f in faces:
            poly = []
            for p in f:
                key = (round(p.x, 5), round(p.y, 5), round(p.z, 5))
                if key not in ids:
                    ids[key] = len(verts)
                    verts.append(p)
                poly.append(ids[key])
            polys.append(poly)
        self.verts = verts
        self.bvh = BVHTree.FromPolygons(verts, polys)

    @property
    def size(self):
        return (self.hi - self.lo).length

    def near(self, other, margin):
        return all(self.lo[i] - margin <= other.hi[i] and other.lo[i] - margin <= self.hi[i] for i in range(3))

    def touches(self, other):
        if self.bvh.overlap(other.bvh):
            return True
        for a, b in ((self, other), (other, self)):
            step = max(1, len(a.verts) // 400)
            for v in a.verts[::step]:
                hit = b.bvh.find_nearest(v, TOUCH)
                if hit[0] is not None:
                    return True
        return False


def solids_of(objects, verts):
    out = []
    for name, tris in objects.items():
        bm = bmesh.new()
        vs = [bm.verts.new(verts[i]) for i in sorted({i for t in tris for i in t})]
        index = {i: v for i, v in zip(sorted({i for t in tris for i in t}), vs)}
        for t in tris:
            try:
                bm.faces.new([index[i] for i in t])
            except ValueError:
                pass
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        seen, n = {}, 0
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
        groups = defaultdict(list)
        for f in bm.faces:
            groups[seen[f]].append([v.co.copy() for v in f.verts])
        for k, faces in groups.items():
            out.append(Solid(name, k, faces))
        bm.free()
    return out


def loose(solids):
    """Groups of solids that touch one another but nothing of the biggest group: hanging in the air."""
    parent = list(range(len(solids)))

    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    order = sorted(range(len(solids)), key=lambda i: solids[i].lo.x)
    for a_i, a in enumerate(order):
        sa = solids[a]
        for b in order[a_i + 1:]:
            sb = solids[b]
            if sb.lo.x - TOUCH > sa.hi.x:
                break
            if root(a) != root(b) and sa.near(sb, TOUCH) and sa.touches(sb):
                parent[root(a)] = root(b)
    groups = defaultdict(list)
    for i in range(len(solids)):
        groups[root(i)].append(solids[i])
    main = max(groups.values(), key=lambda g: sum(s.size for s in g))
    out = []
    for g in groups.values():
        if g is main:
            continue
        lo = Vector(map(min, *[s.lo for s in g])) if len(g) > 1 else g[0].lo
        hi = Vector(map(max, *[s.hi for s in g])) if len(g) > 1 else g[0].hi
        if (hi - lo).length < MIN_LOOSE:
            continue
        # how far from the rest of the machine
        gap = 1e9
        centre = (lo + hi) / 2
        for s in main:
            for piece in g:
                if not s.near(piece, 1.0):
                    continue
                for v in piece.verts[:: max(1, len(piece.verts) // 200)]:
                    hit = s.bvh.find_nearest(v, 1.0)
                    if hit[0] is not None:
                        gap = min(gap, hit[3])
        out.append((sorted({s.mesh for s in g}), centre, (hi - lo).length, gap))
    return out


def fights(solids):
    """Faces of two different meshes in one plane, facing one way, over one patch."""
    sys.path.insert(0, __file__.rsplit("/", 1)[0])
    from check_overlaps import flat, overlap
    faces = []
    for si, s in enumerate(solids):
        for f in s.faces:
            if len(f) < 3:
                continue
            n = (f[1] - f[0]).cross(f[2] - f[0])
            area = n.length / 2
            if area < MIN_AREA / 4:
                continue
            n.normalize()
            faces.append((si, n, n.dot(f[0]), f, area))
    buckets = defaultdict(list)
    for i, (_, n, d, _, _) in enumerate(faces):
        buckets[(round(n.x * 40), round(n.y * 40), round(n.z * 40), round(d / GAP))].append(i)
    shared = defaultdict(float)
    where = {}
    for key, idx in buckets.items():
        near = list(idx)
        for dk in (-1, 1):
            near += buckets.get(key[:3] + (key[3] + dk,), [])
        for a in idx:
            sa, na, da, fa, _ = faces[a]
            for b in near:
                if b <= a and b in idx:
                    continue
                sb, nb, db, fb, _ = faces[b]
                if solids[sa].mesh == solids[sb].mesh or na.dot(nb) < 0.999 or abs(da - db) > GAP:
                    continue
                # (each one's middle in the other's plane: two nearly parallel faces far from the origin
                # can have their planes' distances from it agree and still stand apart)
                ca_, cb_ = sum(fa, Vector()) / len(fa), sum(fb, Vector()) / len(fb)
                if abs(na.dot(cb_) - da) > GAP or abs(nb.dot(ca_) - db) > GAP:
                    continue
                o = overlap(flat(fa, na), flat(fb, na))
                if o > 0:
                    pair = tuple(sorted((solids[sa].mesh, solids[sb].mesh)))
                    shared[pair] += o
                    where[pair] = sum(fa, Vector()) / len(fa)
    return [(pair, area, where[pair]) for pair, area in shared.items() if area >= MIN_AREA]


def inside(solid, v):
    """How deep the point is inside the closed solid (None: outside, or less than CLIP_DEPTH in)."""
    if not all(solid.lo[i] <= v[i] <= solid.hi[i] for i in range(3)):
        return None
    hit, normal, _, d = solid.bvh.find_nearest(v, 1.0)
    # (the faces' winding from the game makes their normals point inwards)
    if hit is None or d < CLIP_DEPTH or (v - hit).dot(normal) <= 0:
        return None
    # inside for sure: a ray out crosses its surface an odd number of times
    count, start = 0, v.copy()
    for _ in range(64):
        h = solid.bvh.ray_cast(start, Vector((0.0, 1.0, 0.0)), 100.0)
        if h[0] is None:
            break
        count += 1
        start = h[0] + Vector((0.0, 1e-4, 0.0))
    return d if count % 2 else None


def clips(solids):
    """Weapons and other solids running into each other ahead of the weapons' back ends (either one's
    points inside the other): (weapon mesh, other mesh, deepest, where)."""
    out = {}
    for w in solids:
        # (a hand weapon's grip and haft go through the fist that holds them)
        if w.mesh not in WEAPONS or w.mesh.rsplit("/", 1)[-1] in ("Fist", "Handle", "Grip"):
            continue
        back, fwd = WEAPONS[w.mesh]
        own = w.mesh.rsplit("/", 1)[0] + "/"
        for o in solids:
            # (not the other pieces of the same weapon: a mace's handle runs into its head)
            if o.mesh.startswith(own) or not o.near(w, 0.0):
                continue
            for a, b in ((w, o), (o, w)):
                for v in a.verts:
                    if (v - back).dot(fwd) <= CLIP_FROM:
                        continue
                    d = inside(b, v)
                    if d is None:
                        continue
                    key = (w.mesh, o.mesh)
                    if key not in out or d > out[key][0]:
                        out[key] = (d, v.copy())
    return [(a, b, d, at) for (a, b), (d, at) in out.items()]


def scene_objects():
    """The meshes of the open scene, as read_obj gives them (game axes)."""
    import bpy
    objects, verts = {}, []
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH":
            continue
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bm.transform(obj.matrix_world)
        bmesh.ops.triangulate(bm, faces=bm.faces)
        base = len(verts)
        verts += [Vector((v.co.x, v.co.z, -v.co.y)) for v in bm.verts]
        bm.verts.index_update()
        objects[obj.name] = [tuple(base + v.index for v in f.verts) for f in bm.faces]
        bm.free()
    return objects, verts


def main():
    args = [a for a in sys.argv[sys.argv.index("--") + 1:] if a.endswith(".obj")]
    for path in args or [None]:
        objects, verts = read_obj(path) if path else scene_objects()
        path = path or sys.argv[sys.argv.index("--") + 2] + ".obj"
        solids = solids_of(objects, verts)
        name = path.rsplit("/", 1)[-1].rsplit(".", 1)[0]
        bad = 0
        for meshes, centre, size, gap in loose(solids):
            bad += 1
            print(f"{name}: LOOSE {', '.join(meshes)} at ({centre.x:.2f}, {centre.y:.2f}, {centre.z:.2f}), {size:.2f} m across, {gap:.3f} m off")
        for (a, b), area, at in fights(solids):
            bad += 1
            print(f"{name}: FIGHT {a} | {b}: {area * 1e4:.0f} cm² at ({at.x:.2f}, {at.y:.2f}, {at.z:.2f})")
        for a, b, d, at in clips(solids):
            bad += 1
            print(f"{name}: CLIP {a} into {b}: {d * 100:.1f} cm deep at ({at.x:.2f}, {at.y:.2f}, {at.z:.2f})")
        print(f"{name}: {len(solids)} solids, {bad} faults")


if __name__ == "__main__":
    main()
