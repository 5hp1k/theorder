using Godot;

namespace MechSim;

/// <summary>
/// A machined cylinder: gun barrels, pistons, axles, wheels. Stands in for CylinderMesh (same
/// TopRadius, BottomRadius and Height, upright along Y). The rims are cut off, long ones get
/// reinforcing bands near the ends, and the ends are bored out a little, so a barrel has a muzzle.
/// </summary>
[Tool, GlobalClass]
public partial class ArmorTubeMesh : PrimitiveMesh
{
    float _top = 0.5f, _bottom = 0.5f, _height = 2f;
    int _segments = 18;
    bool _bands = true, _bore = true;

    [Export] public float TopRadius { get => _top; set { _top = value; RequestUpdate(); } }
    [Export] public float BottomRadius { get => _bottom; set { _bottom = value; RequestUpdate(); } }
    [Export] public float Height { get => _height; set { _height = value; RequestUpdate(); } }
    [Export(PropertyHint.Range, "6,48")] public int RadialSegments { get => _segments; set { _segments = value; RequestUpdate(); } }
    /// <summary>Raised bands near the ends of long tubes.</summary>
    [Export] public bool Bands { get => _bands; set { _bands = value; RequestUpdate(); } }
    /// <summary>Ends sunk in, like the mouth of a barrel.</summary>
    [Export] public bool Bore { get => _bore; set { _bore = value; RequestUpdate(); } }

    public override Godot.Collections.Array _CreateMeshArray()
    {
        var b = new MeshBuilder();
        float half = _height * 0.5f;
        float radius = Mathf.Max(_top, _bottom);
        float cut = Mathf.Min(radius * 0.12f, Mathf.Min(0.05f, _height * 0.2f));
        bool slender = _bands && _height > radius * 5f && Mathf.Abs(_top - _bottom) < radius * 0.2f;

        // Profile from the bottom rim to the top rim: height, radius, mask colour.
        var profile = new System.Collections.Generic.List<(float y, float r, Color c)>();
        float R(float y) => Mathf.Lerp(_bottom, _top, (y + half) / _height);
        void Point(float y, float grow, Color c) => profile.Add((y, Mathf.Max(R(y) + grow, 0f), c));

        if (_bottom > 0.001f) Point(-half, -cut, MeshBuilder.Edge);
        Point(-half + cut, 0f, MeshBuilder.Edge);
        if (slender)
        {
            float band = radius * 0.18f, at = _height * 0.07f, width = Mathf.Min(_height * 0.07f, radius * 1.6f);
            foreach (float sign in new[] { -1f, 1f })
            {
                float y0 = sign < 0f ? -half + at : half - at - width;
                Point(y0 - band * 0.01f, 0f, MeshBuilder.Paint);
                Point(y0 + band * 0.5f, band, MeshBuilder.SoftEdge);
                Point(y0 + width - band * 0.5f, band, MeshBuilder.SoftEdge);
                Point(y0 + width, 0f, MeshBuilder.Paint);
            }
        }
        if (_top > 0.001f)
        {
            Point(half - cut, 0f, MeshBuilder.Edge);
            Point(half, -cut, MeshBuilder.Edge);
        }
        else Point(half, 0f, MeshBuilder.Edge);

        int n = Mathf.Max(_segments, 6);
        // Sides: smooth round the tube, a crease at every change of profile.
        for (int i = 0; i < profile.Count - 1; i++)
        {
            var (y0, r0, c0) = profile[i];
            var (y1, r1, c1) = profile[i + 1];
            float lean = Mathf.Atan2(r0 - r1, y1 - y0);
            int first = -1;
            for (int k = 0; k <= n; k++)
            {
                float a = k * Mathf.Tau / n;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 normal = (dir * Mathf.Cos(lean) + Vector3.Up * Mathf.Sin(lean)).Normalized();
                int lo = b.Add(dir * r0 + Vector3.Up * y0, normal, c0);
                b.Add(dir * r1 + Vector3.Up * y1, normal, c1);
                if (first < 0) first = lo;
            }
            for (int k = 0; k < n; k++)
            {
                int lo = first + k * 2;
                var mid = new Vector3(Mathf.Cos((k + 0.5f) * Mathf.Tau / n), 0f, Mathf.Sin((k + 0.5f) * Mathf.Tau / n));
                Vector3 facing = mid * Mathf.Cos(lean) + Vector3.Up * Mathf.Sin(lean);
                b.Face(lo, lo + 1, lo + 3, facing);
                b.Face(lo, lo + 3, lo + 2, facing);
            }
        }

        // Ends.
        foreach (float sign in new[] { -1f, 1f })
        {
            float rim = (sign < 0f ? _bottom : _top) - cut;
            if (rim <= 0.001f) continue;
            float y = half * sign;
            Vector3 n3 = Vector3.Up * sign;
            float hole = _bore ? rim * 0.62f : 0f, sink = _bore ? Mathf.Min(rim * 0.6f, 0.25f) : 0f;
            for (int k = 0; k < n; k++)
            {
                float a0 = k * Mathf.Tau / n, a1 = (k + 1) * Mathf.Tau / n;
                Vector3 d0 = new(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 top = Vector3.Up * y, deep = Vector3.Up * (y - sink * sign);
                if (_bore)
                {
                    b.Quad(top + d0 * rim, top + d1 * rim, top + d1 * hole, top + d0 * hole, n3, MeshBuilder.SoftEdge);
                    Vector3 wall = -((d0 + d1) * 0.5f).Normalized();
                    b.Quad(top + d0 * hole, top + d1 * hole, deep + d1 * hole, deep + d0 * hole, wall, MeshBuilder.Recess);
                    b.Triangle(deep + d0 * hole, deep + d1 * hole, deep, n3, MeshBuilder.Recess);
                }
                else b.Triangle(top + d0 * rim, top + d1 * rim, top, n3, MeshBuilder.Paint);
            }
        }
        return b.ToArrays();
    }
}
