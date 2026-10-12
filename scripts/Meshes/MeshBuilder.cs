using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Collects flat-shaded quads and triangles into the arrays a mesh is made from. Every vertex carries a
/// colour that the armour shader (materials/armor.gdshader) reads as a mask: red = how intact the
/// paint is (0 on sharp edges, where it wears through to metal), green = how exposed the spot is
/// (0 down in seams and recesses, where dirt gathers). Plain white is an ordinary painted surface.
/// </summary>
public class MeshBuilder
{
    public static readonly Color Paint = new(1f, 1f, 1f);
    public static readonly Color Edge = new(0f, 1f, 1f);
    public static readonly Color SoftEdge = new(0.45f, 0.8f, 1f);
    public static readonly Color Recess = new(1f, 0f, 1f);
    public static readonly Color Frame = new(1f, 0.55f, 1f);

    readonly List<Vector3> _verts = new(), _normals = new();
    readonly List<Color> _colors = new();
    readonly List<int> _indices = new();

    public int Add(Vector3 position, Vector3 normal, Color color)
    {
        _verts.Add(position);
        _normals.Add(normal);
        _colors.Add(color);
        return _verts.Count - 1;
    }

    /// <summary>A triangle of existing vertices, turned to face along the given direction.</summary>
    public void Face(int a, int b, int c, Vector3 facing)
    {
        // Godot draws the side on which the corners run clockwise.
        bool counter = (_verts[b] - _verts[a]).Cross(_verts[c] - _verts[a]).Dot(facing) > 0f;
        _indices.Add(a);
        _indices.Add(counter ? c : b);
        _indices.Add(counter ? b : c);
    }

    public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Color color)
    {
        Face(Add(a, normal, color), Add(b, normal, color), Add(c, normal, color), normal);
    }

    /// <summary>A flat quad; the corners go round it in either direction.</summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color color)
    {
        int i = Add(a, normal, color);
        Add(b, normal, color);
        Add(c, normal, color);
        Add(d, normal, color);
        Face(i, i + 1, i + 2, normal);
        Face(i, i + 2, i + 3, normal);
    }

    /// <summary>A quad whose normal is worked out from its corners, facing away from a point inside the shape.</summary>
    public void QuadFacingFrom(Vector3 inside, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        Vector3 n = (b - a).Cross(d - a);
        if (n.LengthSquared() < 1e-12f) n = (c - b).Cross(d - b);
        n = n.Normalized();
        if (n.Dot((a + c) * 0.5f - inside) < 0f) n = -n;
        Quad(a, b, c, d, n, color);
    }

    /// <summary>
    /// Moves every point (to slant or taper the finished shape) and sets the normals afresh from the
    /// moved triangles. Only for flat-shaded shapes: each face gets one normal.
    /// </summary>
    public void Reshape(System.Func<Vector3, Vector3> move)
    {
        for (int i = 0; i < _verts.Count; i++) _verts[i] = move(_verts[i]);
        for (int i = 0; i + 2 < _indices.Count; i += 3)
        {
            int a = _indices[i], b = _indices[i + 1], c = _indices[i + 2];
            // Clockwise is the front, so the normal is the reverse of the usual cross product.
            Vector3 n = (_verts[c] - _verts[a]).Cross(_verts[b] - _verts[a]);
            if (n.LengthSquared() < 1e-14f) continue;
            n = n.Normalized();
            _normals[a] = n;
            _normals[b] = n;
            _normals[c] = n;
        }
    }

    public Godot.Collections.Array ToArrays()
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = _colors.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = _indices.ToArray();
        return arrays;
    }
}
