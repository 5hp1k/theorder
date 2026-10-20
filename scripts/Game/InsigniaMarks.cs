using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace MechSim;

/// <summary>
/// One emblem put on a machine by hand: on which of its meshes (path from the machine's root), where
/// on it and which way the surface faces there (in that mesh's own axes, so it moves with the part),
/// which way is up on the picture, and how big it is in metres.
/// </summary>
public readonly record struct InsigniaMark(string Part, Vector3 Position, Vector3 Normal, Vector3 Up, float Size)
{
    static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "{0:0.####},{1:0.####},{2:0.####}", v.X, v.Y, v.Z);

    static Vector3 P(string s)
    {
        string[] p = s.Split(',');
        return new Vector3(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture));
    }

    public string Pack() => string.Join(";", Part, V(Position), V(Normal), V(Up), Size.ToString("0.###", CultureInfo.InvariantCulture));

    public static InsigniaMark? Unpack(string s)
    {
        string[] p = (s ?? "").Split(';');
        if (p.Length != 5) return null;
        try { return new InsigniaMark(p[0], P(p[1]), P(p[2]), P(p[3]), float.Parse(p[4], CultureInfo.InvariantCulture)); }
        catch (System.FormatException) { return null; }
    }
}

/// <summary>
/// Emblems put anywhere on a machine (the paint tab of the hangar: click on the machine). Each is a
/// <see cref="Decal"/> hung on the part it lies on, so it moves with it and goes when the part is
/// shot off. Decals throw their picture only onto the machines' own render layer (<see cref="Layer"/>),
/// not onto the ground beneath. Kept per kind of machine (GameConfig saves them); the player's machine
/// wears them, and those of the lance that are of the same kind.
/// </summary>
public static class InsigniaMarks
{
    /// <summary>The render layer the machines that wear emblems are put on, and the only one the emblems are drawn onto.</summary>
    public const int Layer = 12;
    public const int MaxPerUnit = 16;
    /// <summary>How far in front of and behind the surface an emblem reaches, in all, metres.</summary>
    const float Depth = 0.5f;
    const string NodePrefix = "InsigniaMark";

    public static readonly Dictionary<string, List<InsigniaMark>> ByUnit = new();

    public static List<InsigniaMark> For(string unitId)
    {
        if (!ByUnit.TryGetValue(unitId, out var list)) ByUnit[unitId] = list = new List<InsigniaMark>();
        return list;
    }

    /// <summary>Puts a machine's emblems on (taking off any it had). Works before the machine is on the field.</summary>
    public static void Apply(Node root, Texture2D picture, IReadOnlyList<InsigniaMark> marks)
    {
        foreach (var old in Geo.Collect<Decal>(root))
            if (old.Name.ToString().StartsWith(NodePrefix))
            {
                old.GetParent().RemoveChild(old);
                old.QueueFree();
            }
        if (picture == null) return;
        // (on the emblems' layer even with none yet: the one being placed in the hangar shows on it)
        foreach (var geometry in Geo.Collect<GeometryInstance3D>(root)) geometry.SetLayerMaskValue(Layer, true);
        if (marks == null) return;
        int i = 0;
        foreach (var mark in marks)
        {
            if (root.GetNodeOrNull<Node3D>(mark.Part) is not { } part) continue;
            Transform3D partRel = RelativeTo(part, root);
            Vector3 point = partRel * mark.Position;
            Vector3 normal = (partRel.Basis * mark.Normal).Normalized();
            Vector3 up = (partRel.Basis * mark.Up).Normalized();
            var decal = Make(picture, mark.Size, false);
            decal.Name = NodePrefix + i++;
            // (set relative to the part, undoing whatever scale it has)
            decal.Transform = partRel.AffineInverse() * Placement(point, normal, up);
            part.AddChild(decal);
        }
    }

    /// <summary>A decal of the emblem; a ghost is the half-seen one that follows the cursor while placing.</summary>
    public static Decal Make(Texture2D picture, float size, bool ghost) => new()
    {
        TextureAlbedo = picture,
        Size = new Vector3(size, Depth, size),
        CullMask = 1u << (Layer - 1),
        NormalFade = 0.35f,
        UpperFade = 0.05f,
        LowerFade = 0.05f,
        Modulate = new Color(1f, 1f, 1f, ghost ? 0.55f : 1f),
        DistanceFadeEnabled = true,
        DistanceFadeBegin = 140f,
        DistanceFadeLength = 30f,
    };

    /// <summary>Where an emblem lies: its centre on the surface, facing out of it, the picture's top towards <paramref name="up"/>.</summary>
    public static Transform3D Placement(Vector3 point, Vector3 normal, Vector3 up)
    {
        Vector3 y = normal.Normalized();
        Vector3 z = -(up - y * up.Dot(y));
        if (z.LengthSquared() < 1e-6f) z = y.Cross(Vector3.Right);
        z = z.Normalized();
        Vector3 x = y.Cross(z).Normalized();
        return new Transform3D(new Basis(x, y, z), point);
    }

    /// <summary>A mark for a place on the machine (all in world space; the machine is on the field or on the stand).</summary>
    public static InsigniaMark MarkAt(Node3D root, MeshInstance3D part, Vector3 point, Vector3 normal, Vector3 up, float size)
    {
        Transform3D partInverse = part.GlobalTransform.AffineInverse();
        return new InsigniaMark(root.GetPathTo(part), partInverse * point,
            (partInverse.Basis * normal).Normalized(), (partInverse.Basis * up).Normalized(), size);
    }

    static readonly Dictionary<Mesh, TriangleMesh> Shapes = new();

    /// <summary>The nearest point of the machine's visible surface along a ray (world space), and the part it is on.</summary>
    public static bool Pick(Node root, Vector3 from, Vector3 dir, out MeshInstance3D part, out Vector3 point, out Vector3 normal)
    {
        part = null;
        point = normal = default;
        float best = float.MaxValue;
        foreach (var mesh in Geo.Collect<MeshInstance3D>(root))
        {
            if (mesh.Mesh == null || !mesh.IsVisibleInTree() || mesh.Name.ToString().StartsWith("Insignia")) continue;
            if (!Shapes.TryGetValue(mesh.Mesh, out var shape)) Shapes[mesh.Mesh] = shape = mesh.Mesh.GenerateTriangleMesh();
            if (shape == null) continue;
            Transform3D inverse = mesh.GlobalTransform.AffineInverse();
            var hit = shape.IntersectRay(inverse * from, inverse.Basis * dir);
            if (hit.Count == 0) continue;
            Vector3 at = mesh.GlobalTransform * (Vector3)hit["position"];
            float d = at.DistanceSquaredTo(from);
            if (d >= best) continue;
            best = d;
            part = mesh;
            point = at;
            // a normal goes through the inverse transpose; and it is turned to face the eye
            normal = (inverse.Basis.Transposed() * (Vector3)hit["normal"]).Normalized();
            if (normal.Dot(dir) > 0f) normal = -normal;
        }
        return part != null;
    }

    static Transform3D RelativeTo(Node3D node, Node root)
    {
        Transform3D t = Transform3D.Identity;
        for (Node n = node; n != null && n != root; n = n.GetParent())
            if (n is Node3D spatial) t = spatial.Transform * t;
        return t;
    }

    public static string[] Pack(List<InsigniaMark> marks) => marks.ConvertAll(m => m.Pack()).ToArray();

    public static void Unpack(string unit, string[] packed)
    {
        var list = For(unit);
        list.Clear();
        foreach (string s in packed)
            if (InsigniaMark.Unpack(s) is { } mark) list.Add(mark);
    }
}
