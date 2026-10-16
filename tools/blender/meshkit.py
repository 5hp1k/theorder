"""Hard-surface building blocks for the game's machines, on top of Blender's bmesh.

Everything here is written in the game's axes (Godot: X right, Y up, Z back, so forward is -Z) and in
metres; points are turned into Blender's Z-up axes as they go into the mesh, and the glTF export
turns them back.

A Part is one mesh of the finished model: the armour of a section, or its bare frame. It is put
together from closed solids (lofts, boxes, cylinders) that may simply overlap. Each solid gets its
sharp edges cut and, if asked, panel seams sunk into its larger faces. Faces are tagged by kind, and
the kinds end up as vertex colours that materials/armor.gdshader reads: red = paint intact (0 on worn
edges), green = exposed (0 down in seams).
"""
import math
from contextlib import contextmanager
import bmesh
import bpy
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

PAINT, EDGE, SOFT, RECESS, FRAME = range(5)
COLORS = {PAINT: (1, 1, 1, 1), EDGE: (0, 1, 1, 1), SOFT: (0.45, 0.8, 1, 1), RECESS: (1, 0, 1, 1), FRAME: (1, 0.55, 1, 1)}


def gd(p):
    """Game axes to Blender axes."""
    return Vector((p[0], -p[2], p[1]))


def octagon(w, h, chamfer=0.1, top=None, bottom=None):
    """Rectangle w by h with its corners cut, as 2D points going round counter-clockwise.
    top / bottom override the size of the cut on the upper / lower pair of corners."""
    ct = chamfer if top is None else top
    cb = chamfer if bottom is None else bottom
    ct = min(ct, w * 0.49, h * 0.49)
    cb = min(cb, w * 0.49, h * 0.49)
    x, y = w / 2, h / 2
    return [(x - cb, -y), (x, -y + cb), (x, y - ct), (x - ct, y), (-x + ct, y), (-x, y - ct), (-x, -y + cb), (-x + cb, -y)]


def ring(n, r, r2=None, phase=0.0):
    r2 = r if r2 is None else r2
    return [(math.cos(phase + i * math.tau / n) * r, math.sin(phase + i * math.tau / n) * r2) for i in range(n)]


class Part:
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.kind = self.bm.faces.layers.int.new("kind")
        self._layers()

    def _layers(self):
        """Per-vertex marks of the small fittings (see detail()): which fitting a vertex belongs to (0 =
        the body), and the two loose points that say where the fitting is fixed and which way is out."""
        lay = self.bm.verts.layers.int
        self.detail_id = lay.get("detail") or lay.new("detail")
        self.marker = lay.get("marker") or lay.new("marker")

    # ── solids ──────────────────────────────────────────────────────────────

    def loft(self, sections, kind=PAINT, bevel=0.025, seams=None, cuts=(), caps=True):
        """Skin through a row of cross-sections (lists of 3D points, all the same length, each going
        round the same way). Returns the faces of the new solid."""
        bm = self.bm
        rows = [[bm.verts.new(gd(p)) for p in s] for s in sections]
        faces = []
        n = len(rows[0])
        for a, b in zip(rows, rows[1:]):
            for i in range(n):
                j = (i + 1) % n
                if (a[i].co - a[j].co).length < 1e-6 and (b[i].co - b[j].co).length < 1e-6:
                    continue
                vs = [a[i], a[j], b[j], b[i]]
                # a section squeezed to a point or a line leaves triangles
                uniq = []
                for v in vs:
                    if all((v.co - u.co).length > 1e-6 for u in uniq):
                        uniq.append(v)
                if len(uniq) >= 3:
                    try:
                        faces.append(bm.faces.new(uniq))
                    except ValueError:
                        pass
        if caps:
            for row in (rows[0], rows[-1]):
                uniq = []
                for v in row:
                    if all((v.co - u.co).length > 1e-6 for u in uniq):
                        uniq.append(v)
                if len(uniq) >= 3:
                    try:
                        faces.append(bm.faces.new(uniq))
                    except ValueError:
                        pass
        bmesh.ops.remove_doubles(bm, verts=[v for r in rows for v in r if v.is_valid], dist=1e-5)
        faces = [f for f in faces if f.is_valid]
        return self._dress(faces, kind, bevel, seams, cuts)

    def prism(self, outline, axis, a, b, kind=PAINT, bevel=0.025, seams=None, cuts=(), scale_b=1.0, shift_b=(0, 0), centre=(0, 0)):
        """A 2D outline pushed along an axis from a to b. axis 'z': outline is (x, y); 'y': (x, z);
        'x': (z, y). scale_b / shift_b shrink and slide the far end."""
        def place(p, t, s, sh):
            u, v = centre[0] + p[0] * s + sh[0], centre[1] + p[1] * s + sh[1]
            return {"z": (u, v, t), "y": (u, t, v), "x": (t, v, u)}[axis]
        s0 = [place(p, a, 1.0, (0, 0)) for p in outline]
        s1 = [place(p, b, scale_b, shift_b) for p in outline]
        return self.loft([s0, s1], kind, bevel, seams, cuts)

    def stations(self, axis, rows, kind=PAINT, bevel=0.025, seams=None, cuts=(), chamfer=0.1):
        """Loft through chamfered rectangles. rows: (position along axis, width, height, centre u,
        centre v[, chamfer[, chamfer of the lower corners]]) with u, v as in prism()."""
        sections = []
        for row in rows:
            t, w, h, cu, cv = row[:5]
            c = row[5] if len(row) > 5 else chamfer
            pts = octagon(w, h, c, bottom=row[6] if len(row) > 6 else None)
            sections.append([{"z": (cu + u, cv + v, t), "y": (cu + u, t, cv + v), "x": (t, cv + v, cu + u)}[axis] for u, v in pts])
        return self.loft(sections, kind, bevel, seams, cuts)

    def box(self, centre, size, kind=PAINT, bevel=0.02, seams=None, rot=None):
        x, y, z = (s / 2 for s in size)
        corners = [(-x, -y), (x, -y), (x, y), (-x, y)]
        s0 = [Vector((u, v, -z)) for u, v in corners]
        s1 = [Vector((u, v, z)) for u, v in corners]
        m = rot if rot is not None else Matrix.Identity(3)
        c = Vector(centre)
        return self.loft([[tuple(c + m @ p) for p in s0], [tuple(c + m @ p) for p in s1]], kind, bevel, seams)

    def cyl(self, a, b, r, r2=None, segs=16, kind=PAINT, bevel=0.015, caps=True, squash=1.0):
        """Cylinder (or cone) from point a to point b."""
        a, b = Vector(a), Vector(b)
        axis = (b - a).normalized()
        side = axis.cross(Vector((0, 1, 0)))
        if side.length < 1e-4:
            side = axis.cross(Vector((1, 0, 0)))
        side.normalize()
        up = side.cross(axis)
        r2 = r if r2 is None else r2
        s0 = [tuple(a + side * u + up * v * squash) for u, v in ring(segs, r)]
        s1 = [tuple(b + side * u + up * v * squash) for u, v in ring(segs, r2)]
        return self.loft([s0, s1], kind, bevel if segs <= 24 else 0, None, (), caps)

    def band(self, path, x0, x1, thick, kind=FRAME, bevel=0.012):
        """A belt (a tank track): a strip from x0 to x1 wide and 'thick' deep, running round a closed
        path of (z, y) points; the path is its outer face."""
        n = len(path)
        secs = []
        for i in range(n):
            a, b, c = Vector(path[i - 1]), Vector(path[i]), Vector(path[(i + 1) % n])
            d0, d1 = (b - a).normalized(), (c - b).normalized()
            # inward normal of the path at this corner (the path runs clockwise seen from +X... or not: found below)
            t = (d0 + d1).normalized()
            inward = Vector((-t.y, t.x)) * self._turn(path)
            k = thick / max(0.3, inward.dot(Vector((-d0.y, d0.x)) * self._turn(path)))
            inner = b + inward * k
            secs.append([(x0, b.y, b.x), (x1, b.y, b.x), (x1, inner.y, inner.x), (x0, inner.y, inner.x)])
        secs.append(secs[0])
        return self.loft(secs, kind, bevel, None, (), caps=False)

    @staticmethod
    def _turn(path):
        area = sum(path[i - 1][0] * path[i][1] - path[i][0] * path[i - 1][1] for i in range(len(path)))
        return 1.0 if area > 0 else -1.0

    def lathe(self, a, b, profile, segs=18, kind=PAINT, bevel=0.0):
        """Body of revolution along a..b; profile is a list of (distance along 0..1, radius)."""
        a, b = Vector(a), Vector(b)
        axis = b - a
        n = axis.normalized()
        side = n.cross(Vector((0, 1, 0)))
        if side.length < 1e-4:
            side = n.cross(Vector((1, 0, 0)))
        side.normalize()
        up = side.cross(n)
        secs = [[tuple(a + axis * t + side * u + up * v) for u, v in ring(segs, max(r, 1e-4))] for t, r in profile]
        return self.loft(secs, kind, bevel)

    # ── detail ──────────────────────────────────────────────────────────────

    @contextmanager
    def detail(self, anchor, normal):
        """A small fitting (bolts, a grille, a lifting eye) built inside the with-block, meant to sit on the
        surface at anchor, facing out along normal. The body is often pushed out or in after it was drawn
        (the house style squares sections up), so when the part is finished every fitting is slid along
        its normal onto the outer surface actually there (settle()): none is left buried in the plating,
        flush with it or hanging off it."""
        before = set(self.bm.verts)
        yield
        ident = 1 + max((v[self.detail_id] for v in self.bm.verts), default=0)
        for v in self.bm.verts:
            if v not in before:
                v[self.detail_id] = ident
        a = gd(anchor)
        for role, at in ((1, a), (2, a + gd(Vector(normal).normalized()) * 0.1)):
            v = self.bm.verts.new(at)
            v[self.detail_id] = ident
            v[self.marker] = role

    def settle(self, reach=0.4):
        """Slides every fitting onto the outer surface of the body under it (see detail()). A fitting with
        no body within reach along its normal is left where it is (it sits on another part)."""
        bm = self.bm
        groups = {}
        for v in bm.verts:
            if v[self.detail_id]:
                groups.setdefault(v[self.detail_id], []).append(v)
        if not groups:
            return
        body = [f for f in bm.faces if all(not v[self.detail_id] for v in f.verts)]
        if body:
            index = {}
            verts = []
            for f in body:
                for v in f.verts:
                    if v not in index:
                        index[v] = len(verts)
                        verts.append(v.co.copy())
            tree = BVHTree.FromPolygons(verts, [[index[v] for v in f.verts] for f in body])
            for ident, vs in groups.items():
                marks = {v[self.marker]: v.co for v in vs if v[self.marker]}
                if 1 not in marks or 2 not in marks:
                    continue
                a, n = marks[1].copy(), (marks[2] - marks[1]).normalized()
                hit, _, _, _ = tree.ray_cast(a + n * 2.0, -n, 2.0 + reach)
                if hit is None:
                    continue
                shift = (hit - a).dot(n)
                if abs(shift) > reach or abs(shift) < 1e-4:
                    continue
                bmesh.ops.translate(bm, vec=n * shift, verts=[v for v in vs if not v[self.marker]])
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v[self.marker]], context="VERTS")
        for v in bm.verts:
            v[self.detail_id] = 0

    def _dress(self, faces, kind, bevel, seams, cuts):
        bm = self.bm
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        for f in faces:
            f[self.kind] = kind
        # Cut the big faces into panels before anything else touches them.
        for co, no in cuts:
            geom = list({e for f in faces if f.is_valid for e in f.edges}) + [f for f in faces if f.is_valid]
            before = set(bm.faces)
            bmesh.ops.bisect_plane(bm, geom=geom + list({v for f in faces if f.is_valid for v in f.verts}), plane_co=gd(co), plane_no=gd(no).normalized(), dist=1e-4)
            faces = [f for f in faces if f.is_valid] + [f for f in bm.faces if f not in before]
            for f in faces:
                f[self.kind] = kind
        if bevel and bevel > 0:
            edges = set()
            for f in faces:
                for e in f.edges:
                    if len(e.link_faces) == 2 and e.is_convex and e.calc_face_angle(0.0) > math.radians(28):
                        edges.add(e)
            if edges:
                before = set(bm.faces)
                bmesh.ops.bevel(bm, geom=list(edges), offset=bevel, offset_type="OFFSET", segments=1, profile=0.5, affect="EDGES", clamp_overlap=True)
                new = [f for f in bm.faces if f not in before]
                keep = {f for f in faces if f.is_valid}
                for f in new:
                    # bevel re-creates the faces it trims as well: only the thin strips are the cut itself
                    if f.calc_area() < 0.0001 or self._thin(f, bevel):
                        f[self.kind] = EDGE if kind != RECESS else RECESS
                    else:
                        f[self.kind] = kind
                faces = list(keep) + new
        if seams:
            self.seams([f for f in faces if f.is_valid and f[self.kind] == kind], **seams)
        return [f for f in faces if f.is_valid]

    @staticmethod
    def _thin(face, width):
        # A strip: its area is about its longest edge times the bevel width.
        longest = max(e.calc_length() for e in face.edges)
        return face.calc_area() < longest * width * 2.2

    def seams(self, faces, margin=0.06, width=0.018, depth=0.02, min_area=0.25, facing=None):
        """Sinks a V-groove round the inside of every large face: the gap between armour plates.
        On a face with short edges (the cut corners of a squared-up section) a groove of the full size
        would pull those edges inside out and fold the face over itself; there it is drawn narrower,
        and left off a face too cramped even for that."""
        bm = self.bm
        whole = margin + 2 * width
        for f in faces:
            if not f.is_valid or f.calc_area() < min_area:
                continue
            if facing is not None and f.normal.dot(gd(facing).normalized()) < 0.5:
                continue
            k = min(1.0, self._room(f) / whole)
            if k < 0.35:
                continue
            bmesh.ops.inset_individual(bm, faces=[f], thickness=margin * k, depth=0.0, use_even_offset=True)
            down = bmesh.ops.inset_individual(bm, faces=[f], thickness=width * k, depth=-depth, use_even_offset=True)["faces"]
            up = bmesh.ops.inset_individual(bm, faces=[f], thickness=width * k, depth=depth, use_even_offset=True)["faces"]
            for g in down + up:
                g[self.kind] = RECESS

    @staticmethod
    def _room(face):
        """How far in the face's outline can be drawn before one of its edges shrinks away: an inset by t
        shortens an edge by t·(cot(a/2) + cot(b/2)), a and b being the corners at its ends."""
        face.normal_update()
        n = face.normal
        vs = [v.co for v in face.verts]
        count = len(vs)

        def cot_half(i):
            to_prev, to_next = (vs[i - 1] - vs[i]).normalized(), (vs[(i + 1) % count] - vs[i]).normalized()
            angle = to_prev.angle(to_next, 0.0)
            # a corner that turns the other way (reflex) opens up as the outline is drawn in
            if (vs[i] - vs[i - 1]).cross(vs[(i + 1) % count] - vs[i]).dot(n) < 0:
                angle = math.tau - angle
            return 1.0 / math.tan(max(angle, 1e-3) / 2)

        room = float("inf")
        for i in range(count):
            shrink = cot_half(i) + cot_half((i + 1) % count)
            if shrink > 1e-6:
                room = min(room, 0.85 * (vs[(i + 1) % count] - vs[i]).length / shrink)
        return room

    def plate(self, faces_or_none, centre, normal, size, height=0.03, along=(0, 1, 0), kind=PAINT, bevel=0.012):
        """A raised slab lying on a surface: centre on the surface, normal out of it, size (across, along)."""
        n = Vector(normal).normalized()
        a = Vector(along)
        a = (a - n * a.dot(n)).normalized()
        s = n.cross(a)
        m = Matrix((s, n, a)).transposed()
        c = Vector(centre) + n * height / 2
        return self.box(tuple(c), (size[0], height, size[1]), kind, bevel, None, m)

    def bolts(self, centres, normal, r=0.035, h=0.03):
        n = Vector(normal).normalized()
        for c in centres:
            c = Vector(c)
            with self.detail(c, n):
                self.cyl(tuple(c - n * 0.01), tuple(c + n * h), r, segs=6, kind=SOFT, bevel=0)

    def slats(self, centre, normal, across, size, count=5, depth=0.05):
        """A grille: dark bars side by side. size = (length of a bar, total run)."""
        n = Vector(normal).normalized()
        a = Vector(across).normalized()
        run = n.cross(a)
        m = Matrix((a, n, run)).transposed()
        pitch = size[1] / count
        with self.detail(centre, n):
            for i in range(count):
                c = Vector(centre) + run * ((i + 0.5) * pitch - size[1] / 2) + n * depth / 2
                self.box(tuple(c), (size[0], depth, pitch * 0.5), RECESS, 0, None, m)

    def sole(self, name, lift=0.03):
        """The part's underside as a flat shape lying just above the ground: the print it leaves."""
        flat = Part(name)
        low = min(v.co.z for v in self.bm.verts if not v[self.marker])
        high = max(v.co.z for v in self.bm.verts if not v[self.marker])
        for f in self.bm.faces:
            # Everything of the underside that faces the ground, pressed flat: a sole that is not quite
            # level (its sections pushed out to the house style) still leaves its whole outline.
            f.normal_update()
            if f.normal.z > -0.3 or f.calc_center_median().z > low + (high - low) * 0.45 or f.calc_area() < 1e-5:
                continue
            vs = [flat.bm.verts.new((v.co.x, v.co.y, lift)) for v in f.verts]
            face = flat.bm.faces.new(vs)
            face.normal_update()
            if face.normal.z < 0:
                face.normal_flip()
        return flat

    def merge(self, other):
        """Takes over the faces of another part."""
        mesh = bpy.data.meshes.new("tmp")
        other.bm.to_mesh(mesh)
        kinds = [f[other.kind] for f in other.bm.faces]
        details = [(v[other.detail_id], v[other.marker]) for v in other.bm.verts]
        start, vstart = len(self.bm.faces), len(self.bm.verts)
        offset = max((v[self.detail_id] for v in self.bm.verts), default=0)
        self.bm.from_mesh(mesh)
        self._layers()
        self.bm.faces.ensure_lookup_table()
        self.bm.verts.ensure_lookup_table()
        for i, k in enumerate(kinds):
            self.bm.faces[start + i][self.kind] = k
        for i, (ident, role) in enumerate(details):
            v = self.bm.verts[vstart + i]
            v[self.detail_id] = ident + offset if ident else 0
            v[self.marker] = role
        bpy.data.meshes.remove(mesh)
        return self

    def scale(self, k):
        """Resizes the whole part about its origin; k is a number or (x, y, z) in game axes."""
        v = (k, k, k) if isinstance(k, (int, float)) else (k[0], k[2], k[1])
        bmesh.ops.scale(self.bm, vec=v, verts=self.bm.verts)
        return self

    def move(self, offset):
        bmesh.ops.translate(self.bm, vec=gd(offset), verts=self.bm.verts)
        return self

    # ── output ──────────────────────────────────────────────────────────────

    def mirrored(self, name):
        """A copy flipped left to right (for the other arm or leg)."""
        other = Part(name)
        other.bm.free()
        other.bm = self.bm.copy()
        other.kind = other.bm.faces.layers.int["kind"]
        other._layers()
        bmesh.ops.scale(other.bm, vec=(-1, 1, 1), verts=other.bm.verts)
        bmesh.ops.reverse_faces(other.bm, faces=other.bm.faces)
        return other

    def to_object(self, collection):
        self.settle()
        bm = self.bm
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
        mesh = bpy.data.meshes.new(self.name)
        colour = bm.loops.layers.float_color.new("Color")
        for f in bm.faces:
            f.smooth = True
            c = COLORS[f[self.kind]]
            for loop in f.loops:
                loop[colour] = c
        bm.normal_update()
        bm.to_mesh(mesh)
        mesh.set_sharp_from_angle(angle=math.radians(32))
        mesh.color_attributes.active_color = mesh.color_attributes["Color"]
        mesh.color_attributes.render_color_index = 0
        obj = bpy.data.objects.new(self.name, mesh)
        collection.objects.link(obj)
        mod = obj.modifiers.new("WeightedNormal", "WEIGHTED_NORMAL")
        mod.keep_sharp = True
        mod.weight = 60
        return obj


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    return bpy.context.scene


def export_glb(path, objects):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
                              export_normals=True, export_tangents=False, export_materials="NONE",
                              export_vertex_color="ACTIVE", export_active_vertex_color_when_no_material=True,
                              export_all_vertex_colors=False, export_animations=False, export_skins=False, export_morph=False)


def render_views(path_prefix, views, target, size=(1400, 1000)):
    """Quick studio renders of whatever is in the scene."""
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    sh = scene.display.shading
    sh.light = "STUDIO"
    sh.color_type = "MATERIAL"
    sh.show_cavity = True
    sh.cavity_type = "BOTH"
    sh.curvature_ridge_factor = 1.2
    sh.curvature_valley_factor = 1.2
    sh.show_shadows = True
    sh.shadow_intensity = 0.35
    sh.show_object_outline = False
    scene.display.render_aa = "8"
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("w")
    world.color = (0.32, 0.34, 0.37)
    scene.world = world
    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens = 34
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    for i, eye in enumerate(views):
        cam.location = gd(eye)
        direction = gd(target) - cam.location
        cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = f"{path_prefix}_{i}.png"
        bpy.ops.render.render(write_still=True)


# ── A whole model ───────────────────────────────────────────────────────────────────────────────

CAMO, JOINT, ACCENT, GLASS, TYRE, GREY = "camo", "joint", "accent", "glass", "tyre", "grey"
STEEL, CONCRETE, WOOD, GLOW, PANEL = "steel", "concrete", "wood", "glow", "panel"
PREVIEW_SHADES = {CAMO: (0.62, 0.52, 0.36, 1), JOINT: (0.16, 0.16, 0.17, 1), ACCENT: (0.3, 0.36, 0.18, 1),
                  GLASS: (0.04, 0.09, 0.12, 1), TYRE: (0.05, 0.05, 0.05, 1), GREY: (0.4, 0.43, 0.46, 1),
                  PANEL: (0.08, 0.085, 0.09, 1), STEEL: (0.5, 0.52, 0.54, 1), CONCRETE: (0.6, 0.6, 0.58, 1), WOOD: (0.4, 0.26, 0.15, 1), GLOW: (1.0, 0.9, 0.6, 1)}


class Model:
    """The parts of one machine and the scene nodes they hang on.

    Every part is modelled in the axes of its parent node in the unit's scene; parts on nodes that the
    game moves about by themselves (joints, wheels, turrets) are modelled round that node's own origin
    and added with own_place=True, which also keeps the node's transform as it is in the scene.
    """

    def __init__(self, name, scene):
        self.name = name
        self.scene = scene  # path of the unit's .tscn, relative to the project
        self.parts = []
        self.moves = {}
        self.removes = []
        self.places = {}
        self.frames = {}     # node path -> (basis rows, origin): transforms the model sets on scene nodes
        self.scripts = {}    # node path -> script to put on that node
        self.spec = {}       # for props: what kind of scene to make of it (see make_props.py)
        self.loose = []      # meshes that hang on no node of their own
        self.root = {}       # properties of the scene's root that take one of them, e.g. FootprintMesh
        self.shapes = {}     # CollisionShape3D node path -> size of its box: hit boxes that follow the model
        self.props = {}      # node path -> {property: number} to set on that node

    def add(self, part, parent, node, material, own_place=False, at=None):
        """at: where the node goes in its parent (for nodes the model brings with it, such as wheels);
        the part is then modelled round that point, like an own_place part."""
        self.parts.append((part, parent, node, material, own_place or at is not None))
        if at is not None:
            self.places[(node if parent == "." else parent + "/" + node)] = (parent, tuple(at))
        return part

    def both(self, part, parent, node, material, own_place=False, at=None, left="L", right="R"):
        """Adds a left-hand part and its mirror image; the node paths differ by the L / R in them."""
        self.add(part, parent, node, material, own_place, at)
        other = part.mirrored(swap_side(part.name, left, right))
        self.add(other, swap_side(parent, left, right), swap_side(node, left, right), material, own_place, None if at is None else (-at[0], at[1], at[2]))
        return part

    def finish(self, args):
        import json
        import os
        import sys
        out = args[0]
        scene = reset_scene()
        built, export, manifest, tris = {}, [], [], 0
        for part, parent, node, material, own_place in self.parts:
            if part.name not in built:
                tris += sum(max(len(f.verts) - 2, 1) for f in part.bm.faces)
                built[part.name] = part.to_object(scene.collection)
                export.append(built[part.name])
            path = node if parent == "." else parent + "/" + node
            manifest.append({"mesh": part.name, "parent": parent, "node": node, "material": material, "keep_place": own_place,
                             "origin": self.places[path][1] if path in self.places else None})
        for part in self.loose:
            export.append(part.to_object(scene.collection))
        os.makedirs(out, exist_ok=True)
        export_glb(os.path.join(out, self.name + ".glb"), export)
        for part in self.loose:
            bpy.data.objects.remove(bpy.data.objects[part.name])
        json.dump({"parts": manifest, "move": self.moves, "remove": self.removes, "root": self.root, "spec": self.spec,
                   "frames": self.frames, "scripts": self.scripts, "shapes": self.shapes, "props": self.props}, open(os.path.join(out, self.name + ".json"), "w"), indent=1)
        print("MODEL", self.name, "PARTS", len(export), "TRIS", tris)
        if "preview" not in args:
            return
        # Stand every part where its node stands in the scene.
        here = os.path.dirname(os.path.abspath(__file__))
        sys.path.insert(0, here)
        import numpy as np
        import tscn
        sc = tscn.Scene(os.path.join(here, "..", "..", self.scene)) if self.scene else None
        cache = {}

        def world(path):
            if path in (".", None):
                return np.eye(3), np.zeros(3)
            if path in self.frames:
                pm, po = world(path.rsplit("/", 1)[0] if "/" in path else ".")
                rows, origin = self.frames[path]
                return pm @ np.array(rows), po + pm @ np.array(origin)
            if path in self.places:
                pm, po = world(self.places[path][0])
                return pm, po + pm @ np.array(self.places[path][1])
            if sc is None:
                return np.eye(3), np.zeros(3)
            if path not in cache:
                n = sc.node(path)
                if n is None:
                    cache[path] = world(path.rsplit("/", 1)[0] if "/" in path else ".")
                else:
                    m, o = tscn.parse_xf(n.get("transform"))
                    pm, po = world(n.attr("parent"))
                    cache[path] = (pm @ m, po + pm @ o)
            return cache[path]

        conv = Matrix(((1, 0, 0), (0, 0, -1), (0, 1, 0)))
        shades = {}
        for key, colour in PREVIEW_SHADES.items():
            m = bpy.data.materials.new(key)
            m.diffuse_color = colour
            m.roughness = 0.15 if key == GLASS else 0.6
            m.metallic = 0.6 if key == JOINT else 0.0
            shades[key] = m
        lo, hi = Vector((1e9, 1e9, 1e9)), Vector((-1e9, -1e9, -1e9))
        placed = set()
        for part, parent, node, material, own_place in self.parts:
            src = built[part.name]
            obj = src if part.name not in placed else src.copy()
            if obj is not src:
                scene.collection.objects.link(obj)
            placed.add(part.name)
            path = (node if parent == "." else parent + "/" + node) if own_place else parent
            m, o = world(path)
            mat = (conv @ Matrix(m.tolist()) @ conv.inverted()).to_4x4()
            mat.translation = conv @ Vector(o.tolist())
            obj.matrix_world = mat
            obj.data = obj.data.copy()
            obj.data.materials.clear()
            if material not in shades:
                tone = {"red": (0.7, 0.08, 0.06, 1), "blue": (0.08, 0.2, 0.6, 1), "white": (0.85, 0.85, 0.83, 1), "yellow": (0.85, 0.65, 0.1, 1),
                        "black": (0.04, 0.04, 0.045, 1), "silver": (0.6, 0.62, 0.64, 1), "green": (0.12, 0.3, 0.16, 1)}
                key = next((k for k in tone if k in material), None)
                shades[material] = bpy.data.materials.new(material)
                shades[material].diffuse_color = tone.get(key, (0.5, 0.5, 0.5, 1))
            obj.data.materials.append(shades[material])
            for corner in obj.bound_box:
                w = mat @ Vector(corner)
                lo = Vector(map(min, lo, w))
                hi = Vector(map(max, hi, w))
        centre = (lo + hi) / 2
        c = (centre.x, centre.z, -centre.y)  # back to game axes
        r = (hi - lo).length * 0.95
        views = [(c[0] + r * 0.55, c[1] + r * 0.3, c[2] - r * 0.8), (c[0] - r * 0.8, c[1] + r * 0.12, c[2] - r * 0.55),
                 (c[0] + r * 0.6, c[1] + r * 0.25, c[2] + r * 0.75), (c[0] + r, c[1] + r * 0.05, c[2])]
        render_views(os.path.join(out, self.name), views, c)


def swap_side(text, left="L", right="R"):
    """ArmL -> ArmR, LeftTorso -> RightTorso, WheelL2 -> WheelR2."""
    import re
    text = text.replace("Left", "\0").replace("Right", "Left").replace("\0", "Right")
    return re.sub(r"(?<=[a-z0-9])%s(?=$|[0-9/_])" % left, right, text)
