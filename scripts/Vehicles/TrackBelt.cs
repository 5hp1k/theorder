using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// A caterpillar track made of separate links (scenes/vehicles/track_belt.tscn; the link is the mesh
/// of its MultiMesh). Every frame the vehicle hands it the wheels of its side as they stand — road
/// wheels riding on their suspension, idler and sprocket — and how far that track has run; the belt is
/// drawn taut round them, so it follows each wheel over the bumps, and its links travel round with
/// the track's own speed (the two tracks of a turning tank run at different speeds).
/// </summary>
public partial class TrackBelt : MultiMeshInstance3D
{
    /// <summary>Distance from one link to the next, metres.</summary>
    [Export] public float LinkLength = 0.3f;
    /// <summary>Thickness of the belt: its middle runs this far over half from the wheels.</summary>
    [Export] public float Thickness = 0.09f;
    /// <summary>How far the upper run hangs down in the middle.</summary>
    [Export] public float Sag = 0.06f;

    readonly List<Vector2> _points = new(), _hull = new(), _path = new();
    int _links;
    // Every link's transform, handed to the MultiMesh in one go (12 numbers a link: the basis by rows, the origin down the side).
    float[] _buffer;

    public override void _Ready()
    {
        // Every belt needs its own set of link positions.
        if (Multimesh != null) Multimesh = (MultiMesh)Multimesh.Duplicate();
    }

    /// <summary>
    /// Lays the belt. wheels: centre (X = along the vehicle, its local Z; Y = height) and radius of
    /// every wheel of this side, in the axes of this node's parent; run: metres the track has travelled.
    /// </summary>
    public void Lay(IReadOnlyList<Vector3> wheels, float run)
    {
        if (Multimesh == null || wheels.Count < 2) return;
        // Points round every wheel, then the tight outline round the lot.
        _points.Clear();
        const int steps = 14;
        foreach (Vector3 w in wheels)
            for (int i = 0; i < steps; i++)
            {
                float a = i * Mathf.Tau / steps;
                _points.Add(new Vector2(w.X + Mathf.Cos(a) * (w.Z + Thickness * 0.5f), w.Y + Mathf.Sin(a) * (w.Z + Thickness * 0.5f)));
            }
        Outline();
        if (_hull.Count < 3) return;

        // The upper run hangs a little: more points along the long top edge, dropped in the middle.
        _path.Clear();
        for (int i = 0; i < _hull.Count; i++)
        {
            Vector2 a = _hull[i], b = _hull[(i + 1) % _hull.Count];
            _path.Add(a);
            // Only the long edge along the top.
            if (Mathf.Abs(b.X - a.X) < LinkLength * 3f || Mathf.Min(a.Y, b.Y) < _top - 0.25f) continue;
            const int cuts = 6;
            for (int k = 1; k < cuts; k++)
            {
                float t = k / (float)cuts;
                _path.Add(a.Lerp(b, t) - new Vector2(0f, Sag * Mathf.Sin(t * Mathf.Pi)));
            }
        }

        float length = 0f;
        for (int i = 0; i < _path.Count; i++) length += _path[i].DistanceTo(_path[(i + 1) % _path.Count]);
        // The number of links is fixed once; their spacing gives as the wheels move.
        if (_links == 0)
        {
            _links = Mathf.Max(4, Mathf.RoundToInt(length / LinkLength));
            Multimesh.InstanceCount = _links;
            _buffer = new float[_links * 12];
        }
        float pitch = length / _links;
        float at = Mathf.PosMod(run, pitch);
        int seg = 0;
        float segStart = 0f, segLength = _path[0].DistanceTo(_path[1]);
        for (int i = 0; i < _links; i++)
        {
            float d = at + i * pitch;
            while (d > segStart + segLength && seg < _path.Count - 1)
            {
                segStart += segLength;
                seg++;
                segLength = _path[seg].DistanceTo(_path[(seg + 1) % _path.Count]);
            }
            Vector2 a = _path[seg], b = _path[(seg + 1) % _path.Count];
            Vector2 p = a.Lerp(b, segLength > 1e-5f ? Mathf.Clamp((d - segStart) / segLength, 0f, 1f) : 0f);
            Vector2 along = (b - a).Normalized();
            // The link lies along the belt: its Z down the run, its Y out of the loop.
            // Columns: X = right, Y = z × right, Z = z. Written out by rows.
            var z = new Vector3(0f, along.Y, along.X);
            var y = z.Cross(Vector3.Right);
            int o = i * 12;
            _buffer[o] = 1f; _buffer[o + 1] = y.X; _buffer[o + 2] = z.X; _buffer[o + 3] = 0f;
            _buffer[o + 4] = 0f; _buffer[o + 5] = y.Y; _buffer[o + 6] = z.Y; _buffer[o + 7] = p.Y;
            _buffer[o + 8] = 0f; _buffer[o + 9] = y.Z; _buffer[o + 10] = z.Z; _buffer[o + 11] = p.X;
        }
        Multimesh.Buffer = _buffer;
    }

    float _top;

    /// <summary>Convex outline of the points, going round the way the track runs when driving forward.</summary>
    void Outline()
    {
        _points.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        _hull.Clear();
        static float Turn(Vector2 o, Vector2 a, Vector2 b) => (a - o).Cross(b - o);
        // Lower edge left to right, then the upper edge back.
        foreach (Vector2 p in _points)
        {
            while (_hull.Count >= 2 && Turn(_hull[^2], _hull[^1], p) <= 0f) _hull.RemoveAt(_hull.Count - 1);
            _hull.Add(p);
        }
        int lower = _hull.Count + 1;
        for (int i = _points.Count - 2; i >= 0; i--)
        {
            Vector2 p = _points[i];
            while (_hull.Count >= lower && Turn(_hull[^2], _hull[^1], p) <= 0f) _hull.RemoveAt(_hull.Count - 1);
            _hull.Add(p);
        }
        _hull.RemoveAt(_hull.Count - 1);
        _top = float.MinValue;
        foreach (Vector2 p in _hull) _top = Mathf.Max(_top, p.Y);
    }
}
