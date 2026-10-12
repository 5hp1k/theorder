using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// A building made of blocks. Each block has its own strength: shots knock out the blocks they hit,
/// explosions take a bite, a mech walking through carves a tunnel, and whatever is left hanging without
/// support comes down after it. The rest keeps standing and keeps stopping shots — and a shot through a
/// hole goes through.
/// Scene layout: "Chunks" (MultiMeshInstance3D, carries the wall material; the blocks are created here
/// from the Cells settings), "Collision" (one box around the whole building), optional "Preview"
/// (a plain box so the building shows in the editor; hidden in play), "Roof" (removed with the top
/// floor) and "Rubble" (shown once the ground floor is mostly gone).
/// </summary>
public partial class ChunkedBuilding : StaticBody3D, Phys.IPenetrable, IDamageable
{
    /// <summary>Blocks along X, Y (floors) and Z.</summary>
    [Export] public Vector3I Cells = new(5, 4, 5);
    /// <summary>Side of one block, metres.</summary>
    [Export] public float CellSize = 4f;
    /// <summary>Damage one block takes before it goes.</summary>
    [Export] public float CellHp = 12f;
    /// <summary>Effect spawned where a block is knocked out (dust, debris).</summary>
    [Export] public PackedScene ChunkFx;
    /// <summary>Whether a mech walking into it breaks through.</summary>
    [Export] public bool Trample = true;

    public static readonly List<ChunkedBuilding> All = new();
    /// <summary>Blocks still standing.</summary>
    public int AliveCount => _aliveCount;

    // Effects per frame are capped: a collapsing tower would otherwise spawn hundreds at once.
    const int FxPerFrame = 5;
    static ulong _fxFrame;
    static int _fxCount;

    MultiMesh _mesh;
    float[] _hp;
    bool[] _alive;
    int _aliveCount, _groundAlive;
    Vector3 _origin; // local position of the corner of block (0,0,0)
    Rect2 _area;
    Node3D _roof, _rubble;
    Material _wall;
    bool[] _shown;
    /// <summary>Which way the last blow came from, so the masonry flies the right way.</summary>
    Vector3 _push;
    readonly Queue<(int cell, double due)> _pending = new();
    double _time;

    int Index(int x, int y, int z) => (y * Cells.Z + z) * Cells.X + x;
    bool Inside(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < Cells.X && y < Cells.Y && z < Cells.Z;
    bool Alive(int x, int y, int z) => Inside(x, y, z) && _alive[Index(x, y, z)];

    public override void _Ready()
    {
        int count = Cells.X * Cells.Y * Cells.Z;
        _hp = new float[count];
        _alive = new bool[count];
        _shown = new bool[count];
        _aliveCount = count;
        _groundAlive = Cells.X * Cells.Z;
        _origin = new Vector3(-Cells.X * CellSize * 0.5f, 0f, -Cells.Z * CellSize * 0.5f);

        var chunks = GetNode<MultiMeshInstance3D>("Chunks");
        _mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new BoxMesh { Size = Vector3.One * CellSize },
            InstanceCount = count,
        };
        for (int y = 0; y < Cells.Y; y++)
        for (int z = 0; z < Cells.Z; z++)
        for (int x = 0; x < Cells.X; x++)
        {
            int i = Index(x, y, z);
            _hp[i] = CellHp;
            _alive[i] = true;
            // Blocks buried inside can't be seen: they stay out of the picture until a neighbour goes.
            bool outer = x == 0 || z == 0 || y == Cells.Y - 1 || x == Cells.X - 1 || z == Cells.Z - 1;
            _shown[i] = outer;
            _mesh.SetInstanceTransform(i, outer ? new Transform3D(Basis.Identity, CellCenter(x, y, z)) : Gone(x, y, z));
        }
        chunks.Multimesh = _mesh;
        _wall = chunks.MaterialOverride;

        GetNodeOrNull<Node3D>("Preview")?.Hide();
        _roof = GetNodeOrNull<Node3D>("Roof");
        _rubble = GetNodeOrNull<Node3D>("Rubble");

        // Footprint on the ground, for mechs walking in (buildings stand square to the axes).
        Vector3 p = GlobalPosition;
        bool turned = Mathf.Abs(GlobalTransform.Basis.X.Z) > 0.7f;
        var half = new Vector2((turned ? Cells.Z : Cells.X) * CellSize, (turned ? Cells.X : Cells.Z) * CellSize) * 0.5f;
        _area = new Rect2(new Vector2(p.X, p.Z) - half, half * 2f);
        All.Add(this);
        // Nothing to do each frame until a block goes and something may be left hanging.
        SetProcess(false);
    }

    public override void _ExitTree() => All.Remove(this);

    Vector3 CellCenter(int x, int y, int z) => _origin + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * CellSize;

    // ── Being hit ───────────────────────────────────────────────

    /// <summary>
    /// The collision shape is one box; a ray that reaches it is walked block by block through the grid
    /// to find the first block still standing. Through a hole, it finds none and carries on.
    /// </summary>
    public bool Refine(Vector3 from, Vector3 to, ref Phys.Hit hit)
    {
        Transform3D toLocal = GlobalTransform.AffineInverse();
        Vector3 a = (toLocal * from - _origin) / CellSize;
        Vector3 d = (toLocal * to - _origin) / CellSize - a;

        // Clip the ray to the grid.
        float t0 = 0f, t1 = 1f;
        for (int axis = 0; axis < 3; axis++)
        {
            float size = Cells[axis];
            if (Mathf.Abs(d[axis]) < 1e-6f)
            {
                if (a[axis] < 0f || a[axis] > size) return false;
                continue;
            }
            float u = -a[axis] / d[axis], v = (size - a[axis]) / d[axis];
            if (u > v) (u, v) = (v, u);
            t0 = Mathf.Max(t0, u);
            t1 = Mathf.Min(t1, v);
            if (t0 > t1) return false;
        }

        Vector3 p = a + d * (t0 + 1e-4f);
        var cell = new Vector3I(Mathf.Clamp(Mathf.FloorToInt(p.X), 0, Cells.X - 1), Mathf.Clamp(Mathf.FloorToInt(p.Y), 0, Cells.Y - 1), Mathf.Clamp(Mathf.FloorToInt(p.Z), 0, Cells.Z - 1));
        var step = new Vector3I(d.X > 0 ? 1 : -1, d.Y > 0 ? 1 : -1, d.Z > 0 ? 1 : -1);
        Vector3 tMax = default, tDelta = default;
        for (int axis = 0; axis < 3; axis++)
        {
            if (Mathf.Abs(d[axis]) < 1e-6f)
            {
                tMax[axis] = float.MaxValue;
                tDelta[axis] = float.MaxValue;
                continue;
            }
            float next = cell[axis] + (step[axis] > 0 ? 1 : 0);
            tMax[axis] = (next - a[axis]) / d[axis];
            tDelta[axis] = Mathf.Abs(1f / d[axis]);
        }

        float t = t0;
        int entered = -1; // axis through which the current block was entered
        while (Inside(cell.X, cell.Y, cell.Z) && t <= t1)
        {
            int i = Index(cell.X, cell.Y, cell.Z);
            if (_alive[i])
            {
                hit.Position = from + (to - from) * t;
                hit.Part = i;
                if (entered >= 0)
                {
                    Vector3 n = Vector3.Zero;
                    n[entered] = -step[entered];
                    hit.Normal = (GlobalTransform.Basis * n).Normalized();
                }
                return true;
            }
            int axis = tMax.X < tMax.Y ? (tMax.X < tMax.Z ? 0 : 2) : (tMax.Y < tMax.Z ? 1 : 2);
            t = tMax[axis];
            tMax[axis] += tDelta[axis];
            cell[axis] += step[axis];
            entered = axis;
        }
        return false;
    }

    Transform3D Gone(int x, int y, int z) => new(Basis.Identity.Scaled(Vector3.Zero), CellCenter(x, y, z));

    public void DamageCell(int cell, float amount, Vector3 direction = default)
    {
        if (cell < 0 || cell >= _alive.Length || !_alive[cell]) return;
        _hp[cell] -= amount;
        _push = direction;
        if (_hp[cell] <= 0f) Remove(cell, true);
    }

    /// <summary>A hit with no block attached (a hand weapon): the block nearest to the point.</summary>
    public void TakeDamage(float amount, Vector3 point)
    {
        Vector3 g = (GlobalTransform.AffineInverse() * point - _origin) / CellSize;
        int x = Mathf.Clamp(Mathf.FloorToInt(g.X), 0, Cells.X - 1), y = Mathf.Clamp(Mathf.FloorToInt(g.Y), 0, Cells.Y - 1), z = Mathf.Clamp(Mathf.FloorToInt(g.Z), 0, Cells.Z - 1);
        DamageCell(Index(x, y, z), amount);
    }

    /// <summary>An explosion: every block within the radius takes damage, less towards the edge.</summary>
    public void Blast(Vector3 center, float radius, float damage)
    {
        Vector3 local = GlobalTransform.AffineInverse() * center;
        ForEachCellIn(local - Vector3.One * radius, local + Vector3.One * radius, (x, y, z) =>
        {
            Vector3 away = CellCenter(x, y, z) - local;
            float dist = away.Length();
            if (dist > radius + CellSize * 0.5f) return;
            DamageCell(Index(x, y, z), damage * Mathf.Lerp(1f, 0.25f, Mathf.Clamp(dist / radius, 0f, 1f)),
                       GlobalTransform.Basis * away.Normalized() * 9f);
        });
    }

    /// <summary>Mechs walking through buildings: knocks out every block a body of this size overlaps.</summary>
    public static void TrampleAround(Vector3 position, float radius, float height)
    {
        var body = new Rect2(position.X - radius, position.Z - radius, radius * 2f, radius * 2f);
        foreach (var b in All)
        {
            if (!b.Trample || b._aliveCount == 0 || !b._area.Intersects(body)) continue;
            Vector3 local = b.GlobalTransform.AffineInverse() * position;
            b._push = default;
            b.ForEachCellIn(local - new Vector3(radius, 0.5f, radius), local + new Vector3(radius, height, radius),
                            (x, y, z) => b.Remove(b.Index(x, y, z), true));
        }
    }

    void ForEachCellIn(Vector3 min, Vector3 max, System.Action<int, int, int> action)
    {
        Vector3 a = (min - _origin) / CellSize, b = (max - _origin) / CellSize;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.X, b.X))), x1 = Mathf.Min(Cells.X - 1, Mathf.FloorToInt(Mathf.Max(a.X, b.X)));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.Y, b.Y))), y1 = Mathf.Min(Cells.Y - 1, Mathf.FloorToInt(Mathf.Max(a.Y, b.Y)));
        int z0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.Z, b.Z))), z1 = Mathf.Min(Cells.Z - 1, Mathf.FloorToInt(Mathf.Max(a.Z, b.Z)));
        for (int y = y0; y <= y1; y++)
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
            if (_alive[Index(x, y, z)]) action(x, y, z);
    }

    // ── Blocks going ────────────────────────────────────────────

    void Remove(int cell, bool withFx)
    {
        if (!_alive[cell]) return;
        _alive[cell] = false;
        _aliveCount--;
        int x = cell % Cells.X, z = cell / Cells.X % Cells.Z, y = cell / (Cells.X * Cells.Z);
        // A block that's gone is a block squashed to nothing…
        _mesh.SetInstanceTransform(cell, Gone(x, y, z));
        // …and the ones behind it come into view.
        Reveal(x + 1, y, z); Reveal(x - 1, y, z); Reveal(x, y + 1, z); Reveal(x, y - 1, z); Reveal(x, y, z + 1); Reveal(x, y, z - 1);

        if (withFx && AllowFx())
        {
            Node scene = GetTree().CurrentScene;
            Vector3 at = GlobalTransform * CellCenter(x, y, z);
            if (ChunkFx != null)
            {
                var fx = ChunkFx.Instantiate<Node3D>();
                scene.AddChild(fx);
                fx.GlobalPosition = at;
            }
            // The masonry itself: a few pieces thrown the way the blow went, or just dropping.
            Debris.Burst(scene, at, 2, CellSize * 0.5f, _wall, _push, _push == default ? 2.5f : 5f);
        }

        if (y == Cells.Y - 1 && _roof != null) _roof.Visible = false;
        if (y == 0 && --_groundAlive < Cells.X * Cells.Z / 2 && _rubble != null) _rubble.Visible = true;
        if (_aliveCount == 0) CollisionLayer = 0;

        // Whatever rested on this block gets checked shortly: the block above, and the neighbours that
        // were hanging off it (and off the one above).
        double due = _time + 0.12;
        Queue(x, y + 1, z, due);
        foreach (var (dx, dz) in Sides)
        {
            Queue(x + dx, y, z + dz, due);
            Queue(x + dx, y + 1, z + dz, due);
        }
    }

    void Reveal(int x, int y, int z)
    {
        if (!Alive(x, y, z)) return;
        int i = Index(x, y, z);
        if (_shown[i]) return;
        _shown[i] = true;
        _mesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, CellCenter(x, y, z)));
    }

    static readonly (int, int)[] Sides = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    void Queue(int x, int y, int z, double due)
    {
        if (!Alive(x, y, z)) return;
        _pending.Enqueue((Index(x, y, z), due));
        SetProcess(true);
    }

    /// <summary>
    /// A block stays up if it stands on the ground or on another block, or hangs off a neighbour that
    /// does: one block of overhang holds, more than that falls.
    /// </summary>
    bool Supported(int x, int y, int z)
    {
        if (y == 0 || Alive(x, y - 1, z)) return true;
        foreach (var (dx, dz) in Sides)
            if (Alive(x + dx, y, z + dz) && Alive(x + dx, y - 1, z + dz)) return true;
        return false;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        while (_pending.Count > 0 && _pending.Peek().due <= _time)
        {
            int cell = _pending.Dequeue().cell;
            if (!_alive[cell]) continue;
            int x = cell % Cells.X, z = cell / Cells.X % Cells.Z, y = cell / (Cells.X * Cells.Z);
            if (Supported(x, y, z)) continue;
            _push = default;
            Remove(cell, true);
        }
        if (_pending.Count == 0) SetProcess(false);
    }

    static bool AllowFx()
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame != _fxFrame)
        {
            _fxFrame = frame;
            _fxCount = 0;
        }
        return _fxCount++ < FxPerFrame;
    }
}
