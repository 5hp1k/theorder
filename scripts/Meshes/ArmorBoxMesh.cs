using Godot;

namespace MechSim;

/// <summary>
/// A block of armour: a box with its edges cut off and raised plates on its larger faces, some of them
/// bolted down. Stands in for BoxMesh wherever a machine is built out of blocks; set Size as you would
/// there. The plates are laid out from Seed and the size, so equal blocks look alike.
/// </summary>
[Tool, GlobalClass]
public partial class ArmorBoxMesh : PrimitiveMesh
{
    Vector3 _size = Vector3.One;
    float _bevel = 0.07f;
    float _plate = 0.03f;
    int _seed;
    bool _plates = true;

    [Export] public Vector3 Size { get => _size; set { _size = value; RequestUpdate(); } }
    /// <summary>Width of the cut along each edge, as a share of the shortest side (never more than 8 cm).</summary>
    [Export(PropertyHint.Range, "0,0.3")] public float Bevel { get => _bevel; set { _bevel = value; RequestUpdate(); } }
    /// <summary>How far the plates stand proud of the block, metres.</summary>
    [Export(PropertyHint.Range, "0,0.2")] public float PlateHeight { get => _plate; set { _plate = value; RequestUpdate(); } }
    /// <summary>Plates on the faces; off = a plain block with cut edges.</summary>
    [Export] public bool Plates { get => _plates; set { _plates = value; RequestUpdate(); } }
    /// <summary>Changes the layout of the plates.</summary>
    [Export] public int Seed { get => _seed; set { _seed = value; RequestUpdate(); } }

    public enum Side { Up, Down, Front, Back, Left, Right }

    Side _taperTowards = Side.Up;
    Vector2 _taper = Vector2.One;
    Vector2 _slant;

    /// <summary>The face that Taper narrows and Slant shifts.</summary>
    [Export] public Side TaperTowards { get => _taperTowards; set { _taperTowards = value; RequestUpdate(); } }
    /// <summary>
    /// Size of that face relative to the opposite one, across its two directions (for Up and Down: X and
    /// Z; for Front and Back: X and Y; for Left and Right: Y and Z). 1 = a plain box, less = a wedge.
    /// </summary>
    [Export] public Vector2 Taper { get => _taper; set { _taper = value; RequestUpdate(); } }
    /// <summary>How far that face is pushed sideways, metres, in the same two directions.</summary>
    [Export] public Vector2 Slant { get => _slant; set { _slant = value; RequestUpdate(); } }

    static readonly Vector3[] Axes = { Vector3.Right, Vector3.Up, Vector3.Back };

    public override Godot.Collections.Array _CreateMeshArray()
    {
        var b = new MeshBuilder();
        Vector3 h = _size.Abs() * 0.5f;
        float shortest = Mathf.Min(h.X, Mathf.Min(h.Y, h.Z)) * 2f;
        float cut = Mathf.Min(shortest * _bevel, 0.08f);
        cut = Mathf.Min(cut, shortest * 0.45f);

        var rng = new RandomNumberGenerator
        {
            Seed = (ulong)(_seed * 7919 + Mathf.RoundToInt(_size.X * 100f) * 31 + Mathf.RoundToInt(_size.Y * 100f) * 17 + Mathf.RoundToInt(_size.Z * 100f) + 1),
        };

        // Faces.
        for (int axis = 0; axis < 3; axis++)
        {
            int ua = (axis + 1) % 3, va = (axis + 2) % 3;
            Vector3 u = Axes[ua], v = Axes[va];
            float hu = h[ua] - cut, hv = h[va] - cut;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector3 n = Axes[axis] * sign;
                Vector3 c = n * h[axis];
                bool plated = _plates && _plate > 0f && Mathf.Min(hu, hv) * 2f >= 0.4f;
                b.Quad(c - u * hu - v * hv, c + u * hu - v * hv, c + u * hu + v * hv, c - u * hu + v * hv, n, plated ? MeshBuilder.Frame : MeshBuilder.Paint);
                if (plated) AddPlates(b, rng, c, n, u, v, hu, hv);
            }
        }

        // Cut edges: one strip between each pair of neighbouring faces.
        for (int axis = 0; axis < 3; axis++)
        {
            int ua = (axis + 1) % 3, va = (axis + 2) % 3;
            Vector3 along = Axes[axis] * (h[axis] - cut);
            for (int su = -1; su <= 1; su += 2)
            for (int sv = -1; sv <= 1; sv += 2)
            {
                Vector3 onU = Axes[ua] * (su * h[ua]) + Axes[va] * (sv * (h[va] - cut));
                Vector3 onV = Axes[ua] * (su * (h[ua] - cut)) + Axes[va] * (sv * h[va]);
                Vector3 n = (Axes[ua] * su + Axes[va] * sv).Normalized();
                b.Quad(onU - along, onU + along, onV + along, onV - along, n, MeshBuilder.Edge);
            }
        }

        // Cut corners.
        for (int sx = -1; sx <= 1; sx += 2)
        for (int sy = -1; sy <= 1; sy += 2)
        for (int sz = -1; sz <= 1; sz += 2)
        {
            var s = new Vector3(sx, sy, sz);
            Vector3 full = h * s, inner = (h - Vector3.One * cut) * s;
            b.Triangle(new Vector3(full.X, inner.Y, inner.Z), new Vector3(inner.X, full.Y, inner.Z), new Vector3(inner.X, inner.Y, full.Z), s.Normalized(), MeshBuilder.Edge);
        }
        if (_taper != Vector2.One || _slant != Vector2.Zero)
        {
            // Along which axis, towards which end, and which two axes it squeezes.
            int along = _taperTowards is Side.Up or Side.Down ? 1 : _taperTowards is Side.Front or Side.Back ? 2 : 0;
            float end = _taperTowards is Side.Up or Side.Back or Side.Right ? 1f : -1f;
            int first = along == 0 ? 1 : 0, second = along == 2 ? 1 : 2;
            float reach = Mathf.Max(h[along], 1e-4f);
            b.Reshape(p =>
            {
                float t = Mathf.Clamp((p[along] * end / reach + 1f) * 0.5f, 0f, 1f);
                p[first] = p[first] * Mathf.Lerp(1f, _taper.X, t) + _slant.X * t;
                p[second] = p[second] * Mathf.Lerp(1f, _taper.Y, t) + _slant.Y * t;
                return p;
            });
        }
        return b.ToArrays();
    }

    /// <summary>Covers a face with one to three raised plates, side by side along its longer direction.</summary>
    void AddPlates(MeshBuilder b, RandomNumberGenerator rng, Vector3 centre, Vector3 n, Vector3 u, Vector3 v, float hu, float hv)
    {
        // Work along the longer side.
        if (hv > hu)
        {
            (u, v) = (v, u);
            (hu, hv) = (hv, hu);
        }
        float margin = Mathf.Clamp(hv * 0.14f, 0.03f, 0.12f);
        float gap = Mathf.Clamp(hv * 0.07f, 0.02f, 0.06f);
        float ratio = hu / hv;
        int count = ratio > 3.2f ? 3 : ratio > 1.5f ? 2 : 1;
        if (count == 1 && hv > 0.9f && rng.Randf() < 0.5f) count = 2;

        float length = (hu - margin) * 2f;
        // Uneven split: plates of different widths look less like a grid.
        var cuts = new float[count + 1];
        cuts[0] = 0f;
        cuts[count] = 1f;
        for (int i = 1; i < count; i++) cuts[i] = (i + rng.RandfRange(-0.22f, 0.22f)) / count;

        for (int i = 0; i < count; i++)
        {
            float u0 = -hu + margin + cuts[i] * length + (i > 0 ? gap * 0.5f : 0f);
            float u1 = -hu + margin + cuts[i + 1] * length - (i < count - 1 ? gap * 0.5f : 0f);
            float v0 = -hv + margin, v1 = hv - margin;
            if (u1 - u0 < 0.12f || v1 - v0 < 0.12f) continue;
            float height = _plate * rng.RandfRange(0.7f, 1.3f);
            Plate(b, centre, n, u, v, u0, u1, v0, v1, height, MeshBuilder.Paint);

            float w = u1 - u0, t = v1 - v0;
            float roll = rng.Randf();
            if (Mathf.Min(w, t) > 0.7f && roll < 0.4f)
            {
                // A hatch: a second, smaller plate on top.
                float iw = w * rng.RandfRange(0.45f, 0.65f), it = t * rng.RandfRange(0.45f, 0.65f);
                float ou = rng.RandfRange(-0.5f, 0.5f) * (w - iw) * 0.6f, ov = rng.RandfRange(-0.5f, 0.5f) * (t - it) * 0.6f;
                float cu = (u0 + u1) * 0.5f + ou, cv = (v0 + v1) * 0.5f + ov;
                Plate(b, centre + n * height, n, u, v, cu - iw * 0.5f, cu + iw * 0.5f, cv - it * 0.5f, cv + it * 0.5f, height * 0.7f, MeshBuilder.Paint);
            }
            else if (Mathf.Min(w, t) > 0.5f && roll < 0.8f)
            {
                // Bolts in the corners.
                float inset = Mathf.Clamp(Mathf.Min(w, t) * 0.12f, 0.05f, 0.14f), stud = inset * 0.3f;
                for (int su = 0; su < 2; su++)
                for (int sv = 0; sv < 2; sv++)
                {
                    float bu = su == 0 ? u0 + inset : u1 - inset, bv = sv == 0 ? v0 + inset : v1 - inset;
                    Plate(b, centre + n * height, n, u, v, bu - stud, bu + stud, bv - stud, bv + stud, stud * 0.8f, MeshBuilder.SoftEdge);
                }
            }
            else if (Mathf.Min(w, t) > 0.6f)
            {
                // A grille: slats across the shorter side.
                bool acrossU = w < t;
                float span = acrossU ? t : w, depth = height * 0.9f;
                int slats = Mathf.Clamp(Mathf.RoundToInt(span * 0.45f / 0.09f), 3, 8);
                float pitch = span * 0.5f / slats, start = (acrossU ? v0 : u0) + span * 0.25f;
                float side0 = (acrossU ? u0 : v0) + (acrossU ? w : t) * 0.2f, side1 = (acrossU ? u1 : v1) - (acrossU ? w : t) * 0.2f;
                for (int k = 0; k < slats; k++)
                {
                    float a0 = start + k * pitch, a1 = a0 + pitch * 0.55f;
                    if (acrossU) Plate(b, centre + n * height, n, u, v, side0, side1, a0, a1, depth, MeshBuilder.Recess);
                    else Plate(b, centre + n * height, n, u, v, a0, a1, side0, side1, depth, MeshBuilder.Recess);
                }
            }
        }
    }

    /// <summary>A raised rectangle with sloping sides.</summary>
    static void Plate(MeshBuilder b, Vector3 centre, Vector3 n, Vector3 u, Vector3 v, float u0, float u1, float v0, float v1, float height, Color top)
    {
        float slope = Mathf.Min(height, Mathf.Min(u1 - u0, v1 - v0) * 0.3f);
        Vector3 P(float x, float y, float z) => centre + u * x + v * y + n * z;
        Vector3 a0 = P(u0, v0, 0f), b0 = P(u1, v0, 0f), c0 = P(u1, v1, 0f), d0 = P(u0, v1, 0f);
        Vector3 a1 = P(u0 + slope, v0 + slope, height), b1 = P(u1 - slope, v0 + slope, height), c1 = P(u1 - slope, v1 - slope, height), d1 = P(u0 + slope, v1 - slope, height);
        b.Quad(a1, b1, c1, d1, n, top);
        Vector3 inside = (a0 + c0) * 0.5f;
        b.QuadFacingFrom(inside, a0, b0, b1, a1, MeshBuilder.SoftEdge);
        b.QuadFacingFrom(inside, b0, c0, c1, b1, MeshBuilder.SoftEdge);
        b.QuadFacingFrom(inside, c0, d0, d1, c1, MeshBuilder.SoftEdge);
        b.QuadFacingFrom(inside, d0, a0, a1, d1, MeshBuilder.SoftEdge);
    }
}
