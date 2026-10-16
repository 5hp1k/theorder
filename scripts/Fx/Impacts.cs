using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>What made the mark: each has a prefab of its own, scenes/fx/mark_*.tscn.</summary>
public enum Mark
{
    /// <summary>Bullets and blades: a pock with chips round it.</summary>
    Bullet,
    /// <summary>Shells, missiles, bombs: char, streaks of soot, thrown earth.</summary>
    Blast,
    /// <summary>Beams: a glowing burn.</summary>
    Burn,
}

/// <summary>
/// Marks that weapons leave on the world: craters and scorches on the ground, splashes and ripples on
/// water. The looks are in scenes/fx/mark_*.tscn and the map's splash scene.
/// </summary>
public static class Impacts
{
    /// <summary>Most marks kept at once; the oldest goes when a new one arrives.</summary>
    public const int MaxCraters = 140;

    static readonly PackedScene[] Scenes = new PackedScene[3];
    static readonly Queue<Node3D> Craters = new();

    /// <summary>Leaves a mark if the hit landed on the ground — and a hole, if it was big enough.</summary>
    public static void Ground(Node parent, in Phys.Hit hit, float radius, Mark kind = Mark.Blast, float depth = 0.3f)
    {
        if (radius <= 0f || hit.Collider is not MapTerrain terrain) return;
        // Big blasts move earth as well as scorching it, and spatter whatever stands near with it.
        terrain.AddCrater(hit.Position, radius, radius * depth);
        if (radius >= 1.5f) Grime.Splash(hit.Position, radius);
        // Nothing is painted on a bed under water: the mark would show through the surface.
        if (terrain.WaterDepthAt(hit.Position.X, hit.Position.Z) > 0.02f) return;
        Scenes[(int)kind] ??= GD.Load<PackedScene>(kind switch
        {
            Mark.Bullet => "res://scenes/fx/mark_bullet.tscn",
            Mark.Burn => "res://scenes/fx/mark_burn.tscn",
            _ => "res://scenes/fx/mark_blast.tscn",
        });
        var crater = Scenes[(int)kind].Instantiate<Node3D>();
        // Flat on the map (it drapes itself over the ground), turned at random so no two look the same.
        var basis = new Basis(Vector3.Up, GD.Randf() * Mathf.Tau).Scaled(Vector3.One * radius * (float)GD.RandRange(0.85, 1.15));
        crater.Transform = new Transform3D(basis, hit.Position);
        parent.AddChild(crater);

        Craters.Enqueue(crater);
        while (Craters.Count > MaxCraters)
        {
            var old = Craters.Dequeue();
            if (GodotObject.IsInstanceValid(old)) old.QueueFree();
        }
    }

    /// <summary>
    /// A shot from one point to another: if it went into water on the way, throws up a splash and sends
    /// out ripples where it broke the surface.
    /// </summary>
    public static void Water(Node parent, Vector3 from, Vector3 to, float strength)
    {
        var terrain = MapTerrain.Instance;
        if (terrain == null || !terrain.HasWater) return;
        float level = terrain.WaterLevel;
        if (from.Y <= level || to.Y >= level) return;
        Vector3 at = from.Lerp(to, (from.Y - level) / (from.Y - to.Y));
        if (terrain.WaterDepthAt(at.X, at.Z) <= 0.05f) return;
        Splash(parent, at, strength);
    }

    /// <summary>Splash and ripples at a point on the water.</summary>
    public static void Splash(Node parent, Vector3 at, float strength)
    {
        var terrain = MapTerrain.Instance;
        if (terrain == null) return;
        at.Y = terrain.WaterLevel;
        WaterSurface.AddRipple(at, strength);
        if (terrain.SplashScene == null) return;
        var splash = OneShotFx.Spawn(terrain.SplashScene, parent, at);
        if (splash != null) splash.Scale = Vector3.One * Mathf.Clamp(strength, 0.4f, 3f);
    }

    // ── On machines ─────────────────────────────────────────────

    /// <summary>Most marks one section (or one vehicle) carries; the oldest goes when another lands.</summary>
    public const int MaxUnitMarks = 14;
    const string MarkGroup = "hit_mark";
    static readonly PackedScene[] UnitScenes = new PackedScene[3];
    static PackedScene _sparks;

    /// <summary>
    /// A hit on a mech or a vehicle leaves its mark on the plate it struck: a pock for a bullet, a
    /// scorch for a shell or a missile, a glowing burn for a beam. The mark rides on the part it is on
    /// and is cleaned off when the machine is repaired.
    /// </summary>
    public static void Unit(in Phys.Hit hit, float radius, Mark kind)
    {
        if (radius <= 0f || hit.Collider is not Node3D body || (body is not HitBox && body is not Vehicle)) return;
        // A pock on a machine half a kilometre off is not a thing anyone sees.
        if (!Lod.Visible(hit.Position, 2f, GameConfig.FxRange * 1.5f)) return;
        // The shot stopped at the collision box, which is cruder than the machine: carry it on to the plating.
        Vector3 along = hit.Position - hit.From;
        along = along.LengthSquared() > 1e-6f ? along.Normalized() : -hit.Normal;
        Vector3 middle = body is HitBox box ? box.Center : body.GlobalTransform * Geo.LocalBounds(body).GetCenter();
        if (!OnModel(body, hit.Position - along * 0.4f, hit.Position + along * ModelReach, out var at, out var n, out var plate)
            // It would have gone through the box without touching anything: the mark goes where the box's middle is.
            && !OnModel(body, hit.Position - along * 0.4f, middle + (middle - hit.Position).Normalized() * 0.5f, out at, out n, out plate))
            return;
        if (n.Dot(along) > 0f) n = -n;
        var carrier = plate.GetParent<Node3D>();

        UnitScenes[(int)kind] ??= GD.Load<PackedScene>(kind switch
        {
            Mark.Bullet => "res://scenes/fx/hit_mark_bullet.tscn",
            Mark.Burn => "res://scenes/fx/hit_mark_burn.tscn",
            _ => "res://scenes/fx/hit_mark_blast.tscn",
        });
        // Not more than so many on one part.
        int count = 0;
        Node oldest = null;
        foreach (Node child in carrier.GetChildren())
            if (child.IsInGroup(MarkGroup) && !child.IsQueuedForDeletion())
            {
                oldest ??= child;
                count++;
            }
        if (count >= MaxUnitMarks) oldest.QueueFree();

        var mark = UnitScenes[(int)kind].Instantiate<MeshInstance3D>();
        mark.AddToGroup(MarkGroup);
        carrier.AddChild(mark);
        // Flat on the plate (the quad faces along its Z), turned at random, a hair off the surface.
        Vector3 side = n.Cross(Mathf.Abs(n.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized().Rotated(n, GD.Randf() * Mathf.Tau);
        float size = Mathf.Clamp(radius * 0.9f, 0.3f, 1.4f) * (float)GD.RandRange(0.85, 1.2);
        mark.GlobalTransform = new Transform3D(new Basis(side, n.Cross(side), n).Scaled(Vector3.One * size), at + n * 0.02f);
        mark.SetInstanceShaderParameter("seed", GD.Randf() * 100f);
        mark.SetInstanceShaderParameter("fade", 1f);
        mark.SetInstanceShaderParameter("heat", kind == Mark.Bullet ? 0f : 1f);
        if (kind != Mark.Bullet)
            mark.CreateTween().TweenMethod(Callable.From<float>(v => mark.SetInstanceShaderParameter("heat", v)), 1f, 0f, kind == Mark.Burn ? 9f : 5f);
    }

    /// <summary>How far past the collision box a shot is followed to find the plating behind it.</summary>
    const float ModelReach = 8f;
    static readonly Dictionary<Mesh, TriangleMesh> Solids = new();
    static readonly Dictionary<Mesh, Aabb> Bounds = new();
    // The meshes of each body, collected once (the marks hung on them later are not among them).
    static readonly Dictionary<Node3D, List<MeshInstance3D>> Plates = new();

    /// <summary>
    /// Where a shot from one point to another first meets the machine itself — the triangles of its
    /// meshes, not its collision boxes — and which mesh that is.
    /// </summary>
    static bool OnModel(Node3D body, Vector3 from, Vector3 to, out Vector3 position, out Vector3 normal, out MeshInstance3D plate)
    {
        position = normal = default;
        plate = null;
        float nearest = float.MaxValue;
        if (!Plates.TryGetValue(body, out var plates))
        {
            plates = Geo.Collect<MeshInstance3D>(body);
            plates.RemoveAll(m => m.IsInGroup(MarkGroup));
            Plates[body] = plates;
        }
        foreach (var mesh in plates)
        {
            if (!GodotObject.IsInstanceValid(mesh) || mesh.Mesh is not { } shape) continue;
            // Most of a machine's parts are nowhere near the shot: a look at the part's box first,
            // before asking the engine about its triangles.
            if (!Bounds.TryGetValue(shape, out var box)) Bounds[shape] = box = shape.GetAabb();
            Transform3D world = mesh.GlobalTransform, local = world.AffineInverse();
            Vector3 a = local * from, b = local * to;
            if (!SegmentTouches(box.Grow(0.05f), a, b)) continue;
            // (A part shot off and lying elsewhere is no longer on this body.)
            if (!mesh.IsVisibleInTree() || !body.IsAncestorOf(mesh)) continue;
            if (!Solids.TryGetValue(shape, out var solid)) Solids[shape] = solid = shape.GenerateTriangleMesh();
            if (solid == null) continue;
            var found = solid.IntersectSegment(a, b);
            if (found.Count == 0) continue;
            Vector3 p = world * found["position"].AsVector3();
            float d = p.DistanceSquaredTo(from);
            if (d >= nearest) continue;
            nearest = d;
            position = p;
            // (Normals go by the inverse transpose: the parts of a machine are not all scaled evenly.)
            normal = (local.Basis.Transposed() * found["normal"].AsVector3()).Normalized();
            plate = mesh;
        }
        return plate != null;
    }

    /// <summary>Works out ahead of time the shapes marks are laid on for every part of a machine (it takes a while the first time).</summary>
    public static void Prepare(Node unit)
    {
        foreach (var mesh in Geo.Collect<MeshInstance3D>(unit))
        {
            if (mesh.Mesh is not { } shape || mesh.IsInGroup(MarkGroup) || Solids.ContainsKey(shape)) continue;
            Solids[shape] = shape.GenerateTriangleMesh();
            Bounds[shape] = shape.GetAabb();
        }
    }

    /// <summary>Whether the segment from a to b passes through the box.</summary>
    static bool SegmentTouches(Aabb box, Vector3 a, Vector3 b)
    {
        Vector3 d = b - a, lo = box.Position, hi = box.End;
        float t0 = 0f, t1 = 1f;
        for (int i = 0; i < 3; i++)
        {
            if (Mathf.Abs(d[i]) < 1e-6f)
            {
                if (a[i] < lo[i] || a[i] > hi[i]) return false;
                continue;
            }
            float u = (lo[i] - a[i]) / d[i], v = (hi[i] - a[i]) / d[i];
            if (u > v) (u, v) = (v, u);
            t0 = Mathf.Max(t0, u);
            t1 = Mathf.Min(t1, v);
            if (t0 > t1) return false;
        }
        return true;
    }

    /// <summary>Takes the marks off a machine (it has been repaired).</summary>
    public static void CleanUnit(Node unit)
    {
        foreach (Node mark in unit.GetTree().GetNodesInGroup(MarkGroup))
            if (unit.IsAncestorOf(mark)) mark.QueueFree();
    }

    /// <summary>A shower of sparks where a shell or a bullet strikes metal.</summary>
    public static void Sparks(Node parent, in Phys.Hit hit)
    {
        if (hit.Collider is not HitBox && hit.Collider is not Vehicle) return;
        _sparks ??= GD.Load<PackedScene>("res://scenes/fx/sparks.tscn");
        Vector3 n = hit.Normal.Normalized();
        if (OneShotFx.Spawn(_sparks, parent, hit.Position + n * 0.1f, 0.6f) is not { } fx) return;
        // The shower flies off along the plate's normal (the emitter shoots along its +Z).
        Vector3 side = n.Cross(Mathf.Abs(n.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
        fx.GlobalBasis = new Basis(side, n.Cross(side), n);
    }

    /// <summary>Forget everything (a new battle).</summary>
    public static void Reset()
    {
        Craters.Clear();
        // (The shapes of the parts are kept: the meshes are the same in every battle, and working them
        // out again is what made the first hit on each part stall.)
        Plates.Clear();
        Debris.Reset();
        OneShotFx.Reset();
        Boundary.Reset();
        TimedFx.Reset();
        GaussTrail.Reset();
        Burning.Reset();
        Emp.Reset();
        CollapseRumble.Reset();
        Bomb.Falling.Clear();
        Grime.Reset();
        AiPilot.NukeSpent = false;
    }
}
