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
/// Blocks don't just vanish: a block that has been hit shows ragged holes (the wall shader eats them
/// out as its damage grows), the blocks round a gap are cracked, what breaks off flies or falls as
/// irregular lumps of masonry, and a section left without support comes down as heavy pieces.
/// The walls are dressed at start-up with whatever the Fittings list offers — a door, air conditioners
/// under the windows, things on a flat roof — each hung on one block and gone when that block goes.
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
    /// <summary>What the lumps that break off are made of; empty = the wall material.</summary>
    [Export] public Material DebrisMaterial;
    /// <summary>Whether a mech walking into it breaks through.</summary>
    [Export] public bool Trample = true;
    /// <summary>
    /// Gives every block a collision shape of its own instead of one box round the lot, so that things
    /// fall through where blocks are missing. For bridges; too many shapes for a tower.
    /// </summary>
    [Export] public bool BlockCollision;

    [ExportGroup("Fittings")]
    /// <summary>A way in on the ground floor. Fitting prefabs have their origin on the wall, +Z pointing out of it.</summary>
    [Export] public PackedScene DoorScene;
    /// <summary>How many doors.</summary>
    [Export] public int Doors = 1;
    /// <summary>Things hung under windows (air conditioners, dishes); one is picked for each window that gets one.</summary>
    [Export] public PackedScene[] WallFittings = System.Array.Empty<PackedScene>();
    /// <summary>Share of windows with something under them.</summary>
    [Export(PropertyHint.Range, "0,1")] public float WallShare = 0.1f;
    /// <summary>Storeys up to which the walls are dressed (higher up nobody would see it).</summary>
    [Export] public int DressedStoreys = 8;
    /// <summary>Things that stand on a flat roof (their origin on the roof, any way round).</summary>
    [Export] public PackedScene[] RoofFittings = System.Array.Empty<PackedScene>();
    [Export] public int RoofCount = 3;

    public static readonly List<ChunkedBuilding> All = new();
    /// <summary>Blocks still standing.</summary>
    public int AliveCount => _aliveCount;

    // Effects per frame are capped: a collapsing tower would otherwise spawn hundreds at once.
    const int FxPerFrame = 5;
    /// <summary>Least time between two blocks' own breaking sounds, ms.</summary>
    const ulong BreakGapMs = 160;
    static ulong _lastBreak;
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
    readonly Dictionary<int, List<Node3D>> _fittings = new();
    // Per block: which faces are on the outside of the building (a bit each), and how broken it looks (0..1).
    CollisionShape3D[] _shapes;
    byte[] _outside;
    float[] _damage;
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
        _outside = new byte[count];
        _damage = new float[count];
        _aliveCount = count;
        _groundAlive = Cells.X * Cells.Z;
        _origin = new Vector3(-Cells.X * CellSize * 0.5f, 0f, -Cells.Z * CellSize * 0.5f);

        var chunks = GetNode<MultiMeshInstance3D>("Chunks");
        _mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            // Each block tells the wall shader where in the building it is.
            UseCustomData = true,
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
            _outside[i] = (byte)((x == Cells.X - 1 ? 1 : 0) | (x == 0 ? 2 : 0) | (y == Cells.Y - 1 ? 4 : 0) | (y == 0 ? 8 : 0) | (z == Cells.Z - 1 ? 16 : 0) | (z == 0 ? 32 : 0));
            TellShader(i);
        }
        chunks.Multimesh = _mesh;
        chunks.SetInstanceShaderParameter("cell_size", CellSize);
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
        if (BlockCollision)
        {
            if (GetNodeOrNull<CollisionShape3D>("Collision") is { } whole) whole.Disabled = true;
            var box = new BoxShape3D { Size = Vector3.One * CellSize };
            _shapes = new CollisionShape3D[count];
            for (int i = 0; i < count; i++)
            {
                _shapes[i] = new CollisionShape3D { Shape = box, Position = CellCenter(i % Cells.X, i / (Cells.X * Cells.Z), i / Cells.X % Cells.Z) };
                AddChild(_shapes[i]);
            }
        }
        Dress();
        // Nothing to do each frame until a block goes and something may be left hanging.
        SetProcess(false);
    }

    /// <summary>Hangs the fittings on the walls and stands them on the roof, each tied to the block behind it.</summary>
    void Dress()
    {
        if (DoorScene == null && WallFittings.Length == 0 && RoofFittings.Length == 0) return;
        Vector3 p = GlobalPosition;
        var rng = new RandomNumberGenerator { Seed = (ulong)(Mathf.Abs(Mathf.RoundToInt(p.X * 13f + p.Z * 7f)) + Cells.X * 31 + Cells.Y) };
        // Where the windows are is the wall material's business.
        float bay = CellSize, storey = CellSize, sill = 0.25f;
        if (_wall is ShaderMaterial wall)
        {
            bay = (float)wall.GetShaderParameter("bay_width");
            storey = (float)wall.GetShaderParameter("floor_height");
            sill = (float)wall.GetShaderParameter("sill");
            if (bay <= 0f || storey <= 0f) return;
        }
        int baysPerCell = Mathf.Max(1, Mathf.RoundToInt(CellSize / bay));
        int storeys = Mathf.Min(Mathf.RoundToInt(Cells.Y * CellSize / storey), DressedStoreys);
        float width = Cells.X * CellSize, depth = Cells.Z * CellSize;

        // The four walls: outward direction, and how many bays run along each.
        var sides = new (Vector3 n, int bays)[] { (Vector3.Back, Cells.X * baysPerCell), (Vector3.Forward, Cells.X * baysPerCell), (Vector3.Right, Cells.Z * baysPerCell), (Vector3.Left, Cells.Z * baysPerCell) };
        var doors = new HashSet<(int side, int bay)>();
        for (int i = 0; i < Doors && DoorScene != null; i++)
        {
            int side = rng.RandiRange(0, 3);
            int at = Mathf.Clamp(sides[side].bays / 2 + rng.RandiRange(-1, 1), 0, sides[side].bays - 1);
            if (doors.Add((side, at))) Hang(DoorScene, sides[side].n, at, 0f, bay, width, depth);
        }
        for (int side = 0; side < 4 && WallFittings.Length > 0; side++)
        for (int at = 0; at < sides[side].bays; at++)
        for (int floor = 0; floor < storeys; floor++)
        {
            if (rng.Randf() > WallShare || (floor == 0 && doors.Contains((side, at)))) continue;
            Hang(WallFittings[rng.RandiRange(0, WallFittings.Length - 1)], sides[side].n, at, floor * storey + sill * storey - 0.02f, bay, width, depth);
        }

        // Only a flat roof has anything standing on it.
        if (_roof is MeshInstance3D { Mesh: PrismMesh }) return;
        for (int i = 0; i < RoofCount && RoofFittings.Length > 0; i++)
        {
            int x = rng.RandiRange(0, Cells.X - 1), z = rng.RandiRange(0, Cells.Z - 1);
            var node = RoofFittings[rng.RandiRange(0, RoofFittings.Length - 1)].Instantiate<Node3D>();
            Vector3 c = CellCenter(x, Cells.Y - 1, z);
            node.Transform = new Transform3D(new Basis(Vector3.Up, rng.RandiRange(0, 3) * Mathf.Pi / 2f), new Vector3(c.X, Cells.Y * CellSize + 0.4f, c.Z));
            Attach(node, Index(x, Cells.Y - 1, z));
        }
    }

    void Hang(PackedScene scene, Vector3 normal, int bayIndex, float height, float bay, float width, float depth)
    {
        // Along the wall, left to right as seen from outside.
        Vector3 along = new(normal.Z, 0f, -normal.X);
        float run = Mathf.Abs(normal.X) > 0.5f ? depth : width, reach = Mathf.Abs(normal.X) > 0.5f ? width : depth;
        Vector3 at = along * (-run * 0.5f + (bayIndex + 0.5f) * bay) + normal * (reach * 0.5f) + Vector3.Up * height;
        var node = scene.Instantiate<Node3D>();
        node.Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(normal.X, normal.Z)), at);
        // The block right behind it.
        Vector3 inside = (at - normal * (CellSize * 0.5f) - _origin) / CellSize;
        int x = Mathf.Clamp(Mathf.FloorToInt(inside.X), 0, Cells.X - 1), y = Mathf.Clamp(Mathf.FloorToInt(inside.Y + 0.1f), 0, Cells.Y - 1), z = Mathf.Clamp(Mathf.FloorToInt(inside.Z), 0, Cells.Z - 1);
        Attach(node, Index(x, y, z));
    }

    /// <summary>Fixes something to the block at these grid coordinates: it goes when the block goes.</summary>
    public void Attach(Node3D fitting, int x, int y, int z) => Attach(fitting, Index(Mathf.Clamp(x, 0, Cells.X - 1), Mathf.Clamp(y, 0, Cells.Y - 1), Mathf.Clamp(z, 0, Cells.Z - 1)));

    /// <summary>Middle of a block, in the building's own axes.</summary>
    public Vector3 BlockCentre(int x, int y, int z) => CellCenter(x, y, z);

    void Attach(Node3D fitting, int cell)
    {
        Lod.Apply(fitting, 45f, 110f, 500f);
        AddChild(fitting);
        if (!_fittings.TryGetValue(cell, out var list)) _fittings[cell] = list = new List<Node3D>();
        list.Add(fitting);
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
        if (_hp[cell] <= 0f)
        {
            Remove(cell, true);
            // Burnt through by a beam: the hole goes on burning (not a bridge deck — there's nothing left to burn).
            if (Burning.Heat && Trample)
            {
                int x = cell % Cells.X, z = cell / Cells.X % Cells.Z, y = cell / (Cells.X * Cells.Z);
                Burning.Ignite(GetParent(), GlobalTransform * (CellCenter(x, y, z) - new Vector3(0f, CellSize * 0.4f, 0f)), CellSize / 3f);
            }
        }
        else Wound(cell, (1f - _hp[cell] / CellHp) * 0.8f);
    }

    /// <summary>Where the block is in the building, which of its faces are outside, how broken it is: all the wall shader needs.</summary>
    void TellShader(int cell)
    {
        int x = cell % Cells.X, z = cell / Cells.X % Cells.Z, y = cell / (Cells.X * Cells.Z);
        Vector3 c = CellCenter(x, y, z);
        _mesh.SetInstanceCustomData(cell, new Color(c.X, c.Y, c.Z, 64f + _outside[cell] + Mathf.Clamp(_damage[cell], 0f, 1f) * 0.9f));
    }

    /// <summary>Makes a block look at least this broken.</summary>
    void Wound(int cell, float damage)
    {
        if (!_alive[cell] || damage <= _damage[cell]) return;
        _damage[cell] = Mathf.Min(damage, 1f);
        TellShader(cell);
    }

    void WoundAt(int x, int y, int z, float more)
    {
        if (!Alive(x, y, z)) return;
        int i = Index(x, y, z);
        Wound(i, _damage[i] + more);
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

    /// <summary>
    /// The front of a great explosion passing through: only the blocks it has reached since the last call
    /// (those between <paramref name="inner"/> and <paramref name="outer"/> from the centre) are hit, so a
    /// whole town isn't brought down in a single frame. <paramref name="damageAt"/> gives the damage at a distance.
    /// </summary>
    public void BlastShell(Vector3 center, float inner, float outer, System.Func<float, float> damageAt)
    {
        if (_aliveCount == 0) return;
        Vector3 local = GlobalTransform.AffineInverse() * center;
        ForEachCellIn(local - Vector3.One * outer, local + Vector3.One * outer, (x, y, z) =>
        {
            Vector3 away = CellCenter(x, y, z) - local;
            float dist = away.Length();
            if (dist < inner || dist >= outer) return;
            DamageCell(Index(x, y, z), damageAt(dist), GlobalTransform.Basis * away.Normalized() * 9f);
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

    /// <summary>
    /// The top of a deck that carries traffic (a bridge: a building that isn't trampled) over this spot,
    /// if there is one no higher than <paramref name="below"/>. For things that otherwise ask the terrain.
    /// </summary>
    public static bool DeckAt(float x, float z, float below, out float height)
    {
        height = 0f;
        bool found = false;
        var spot = new Vector2(x, z);
        foreach (var b in All)
        {
            if (b.Trample || b._aliveCount == 0 || !b._area.HasPoint(spot)) continue;
            Transform3D t = b.GlobalTransform;
            Vector3 local = t.AffineInverse() * new Vector3(x, t.Origin.Y, z);
            int cx = Mathf.FloorToInt((local.X - b._origin.X) / b.CellSize), cz = Mathf.FloorToInt((local.Z - b._origin.Z) / b.CellSize);
            for (int cy = b.Cells.Y - 1; cy >= 0; cy--)
            {
                if (!b.Alive(cx, cy, cz)) continue;
                float top = (t * new Vector3(local.X, b._origin.Y + (cy + 1) * b.CellSize, local.Z)).Y;
                if (top <= below && (!found || top > height)) { height = top; found = true; }
                break;
            }
        }
        return found;
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

    /// <summary>A block the host says is gone.</summary>
    public void NetRemove(int cell)
    {
        if (cell >= 0 && cell < _alive.Length) Remove(cell, true);
    }

    void Remove(int cell, bool withFx)
    {
        if (!_alive[cell]) return;
        NetLink.I?.World(0, All.IndexOf(this), cell, default, 0f);
        _alive[cell] = false;
        _aliveCount--;
        int x = cell % Cells.X, z = cell / Cells.X % Cells.Z, y = cell / (Cells.X * Cells.Z);
        // A block that's gone is a block squashed to nothing…
        _mesh.SetInstanceTransform(cell, Gone(x, y, z));
        // …and the ones behind it come into view.
        Reveal(x + 1, y, z); Reveal(x - 1, y, z); Reveal(x, y + 1, z); Reveal(x, y - 1, z); Reveal(x, y, z + 1); Reveal(x, y, z - 1);

        // The sound of it is one roar for the lot, fed by each block — not a knock apiece.
        if (withFx) CollapseRumble.Feed(GetTree().CurrentScene, GlobalTransform * CellCenter(x, y, z));
        if (withFx && AllowFx())
        {
            Node scene = GetTree().CurrentScene;
            Vector3 at = GlobalTransform * CellCenter(x, y, z);
            if (ChunkFx != null)
            {
                var fx = ChunkFx.Instantiate<Node3D>();
                // Only now and then does one block's own break stand out of the roar.
                ulong now = Time.GetTicksMsec();
                if (now - _lastBreak < BreakGapMs && fx.GetNodeOrNull<AudioStreamPlayer3D>("Sound") is { } sound) sound.Autoplay = false;
                else _lastBreak = now;
                scene.AddChild(fx);
                fx.GlobalPosition = at;
            }
            Material lumps = DebrisMaterial ?? _wall;
            if (_push == default)
            {
                // Nothing under it: it comes down in a couple of heavy pieces, shedding smaller ones.
                Debris.Chunk(scene, at, Vector3.One * CellSize * 0.8f, lumps, new Vector3((float)GD.RandRange(-1.0, 1.0), -1f, (float)GD.RandRange(-1.0, 1.0)));
                Debris.Burst(scene, at, 2, CellSize * 0.45f, lumps, default, 2.5f);
            }
            else
            {
                // Shot out: masonry thrown the way the blow went.
                Debris.Burst(scene, at, 4, CellSize * 0.5f, lumps, _push, 5f);
            }
        }
        // The blocks round the gap are left cracked and holed.
        WoundAt(x + 1, y, z, 0.4f); WoundAt(x - 1, y, z, 0.4f); WoundAt(x, y + 1, z, 0.5f); WoundAt(x, y - 1, z, 0.3f); WoundAt(x, y, z + 1, 0.4f); WoundAt(x, y, z - 1, 0.4f);

        _shapes?[cell].SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
        if (_fittings.Remove(cell, out var hung))
            foreach (Node3D fitting in hung) fitting.QueueFree();
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
