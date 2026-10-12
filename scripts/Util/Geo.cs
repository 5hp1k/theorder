using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>Transform helpers for parts that are placed between two points (leg bones, beams, tracers).</summary>
public static class Geo
{
    /// <summary>Stretches a unit-Y mesh between a and b. sideHint picks the local X axis.</summary>
    public static void PlaceSegment(Node3D node, Vector3 a, Vector3 b, Vector3 sideHint)
        => Place(node, a, b, sideHint, true);

    /// <summary>Like <see cref="PlaceSegment"/> but without stretching: for fixed-length parts such as leg bones.</summary>
    public static void PlaceRigid(Node3D node, Vector3 a, Vector3 b, Vector3 sideHint)
        => Place(node, a, b, sideHint, false);

    static void Place(Node3D node, Vector3 a, Vector3 b, Vector3 sideHint, bool stretch)
    {
        Vector3 d = b - a;
        float len = d.Length();
        if (len < 1e-4f) return;
        Vector3 y = d / len;
        Vector3 x = sideHint - y * sideHint.Dot(y);
        if (x.LengthSquared() < 1e-6f)
            x = Mathf.Abs(y.Dot(Vector3.Up)) < 0.9f ? Vector3.Up.Cross(y) : Vector3.Right.Cross(y);
        x = x.Normalized();
        Vector3 z = x.Cross(y);
        node.GlobalTransform = new Transform3D(new Basis(x, stretch ? y * len : y, z), (a + b) * 0.5f);
    }

    /// <summary>Orthonormal basis with given up and a forward hint (Godot forward = -Z).</summary>
    public static Basis BasisFromUpForward(Vector3 up, Vector3 forwardHint)
    {
        up = up.Normalized();
        Vector3 f = forwardHint - up * forwardHint.Dot(up);
        if (f.LengthSquared() < 1e-6f) f = Vector3.Forward;
        f = f.Normalized();
        Vector3 back = -f;
        Vector3 x = up.Cross(back);
        return new Basis(x, up, back);
    }

    /// <summary>Box around every visible mesh under root, in root's own space.</summary>
    public static Aabb LocalBounds(Node3D root)
    {
        Transform3D toLocal = root.GlobalTransform.AffineInverse();
        bool any = false;
        Aabb bounds = default;
        foreach (var mesh in Collect<MeshInstance3D>(root))
        {
            if (mesh.Mesh == null || !mesh.IsVisibleInTree()) continue;
            Aabb box = toLocal * mesh.GlobalTransform * mesh.Mesh.GetAabb();
            bounds = any ? bounds.Merge(box) : box;
            any = true;
        }
        return any ? bounds : new Aabb(Vector3.One * -0.5f, Vector3.One);
    }

    /// <summary>Every node of type T under root, at any depth (instanced sub-scenes included).</summary>
    public static List<T> Collect<T>(Node root, List<T> into = null) where T : class
    {
        into ??= new List<T>();
        foreach (Node child in root.GetChildren())
        {
            if (child is T t) into.Add(t);
            Collect(child, into);
        }
        return into;
    }
}
