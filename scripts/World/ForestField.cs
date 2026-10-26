using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// A forest of thousands of trees, too many to be a scene apiece: a child of a <see cref="MapTerrain"/>
/// that grows woods over it (thick where a slow noise says, with glades between) out of the tree prop
/// scenes it is given (<see cref="TreeScenes"/>, ordinary <see cref="Destructible"/> trees).
///
/// Standing trees are drawn by the square of ground (<see cref="ChunkSize"/>) — one multimesh per piece
/// of each kind of tree per square, which the engine culls, thins out with distance (<see cref="DrawDistance"/>)
/// and simplifies as a whole — and they are solid as one static body per square, made of the trees'
/// own collision shapes. The moment a tree is hit or walked into, it becomes its own prop scene where it
/// stood and takes the blow as such: a crown shot off, the trunk felled, splinters, fire. A nuclear
/// blast flattens the woods round it outright.
/// </summary>
public partial class ForestField : Node3D
{
    [Export] public PackedScene[] TreeScenes = System.Array.Empty<PackedScene>();
    [Export] public int Seed = 5;
    /// <summary>Distance between trees where the wood is thick, metres (they are jittered off a grid this fine).</summary>
    [Export] public float Spacing = 9f;
    /// <summary>The same out beyond the battlefield, where the woods are only seen.</summary>
    [Export] public float OuterSpacing = 16f;
    /// <summary>Share of the ground under woods (0–1); the rest are glades and open country.</summary>
    [Export(PropertyHint.Range, "0,1")] public float Cover = 0.5f;
    /// <summary>Size of a wood: how slowly the noise that marks them changes, metres.</summary>
    [Export] public float WoodSize = 220f;
    /// <summary>Trees are kept this far from the middle of the map (the start).</summary>
    [Export] public float ClearRadius = 45f;
    /// <summary>Each tree is drawn this much bigger or smaller, at random.</summary>
    [Export(PropertyHint.Range, "0,0.6")] public float SizeVariety = 0.3f;
    [Export] public float ChunkSize = 120f;
    /// <summary>How far away a square of woods is still drawn, metres.</summary>
    [Export] public float DrawDistance = 900f;
    /// <summary>Within this distance the trees cast shadows.</summary>
    [Export] public float ShadowDistance = 320f;

    /// <summary>The forests of the map being played.</summary>
    public static readonly List<ForestField> All = new();

    // One kind of tree: its scene, the pieces it is drawn from and its collision shapes.
    sealed class Kind
    {
        public PackedScene Scene;
        public readonly List<(Mesh mesh, Transform3D at)> Pieces = new();
        public readonly List<(Shape3D shape, Transform3D at)> Shapes = new();
        public float Reach = 2f;
    }

    // A square of ground: its multimeshes (per kind, per piece), its body, and which trees it holds.
    sealed class Chunk
    {
        public readonly Dictionary<int, MultiMesh[]> Meshes = new();
        public ForestChunkBody Body;
        public readonly List<int> Trees = new();
    }

    struct Tree
    {
        public int Kind, Chunk, Slot, FirstShape, ShapeCount;
        public Transform3D At;
        public bool Gone;
    }

    readonly List<Kind> _kinds = new();
    readonly List<Chunk> _chunks = new();
    readonly List<Tree> _trees = new();
    MapTerrain _terrain;

    public override void _Ready()
    {
        All.Add(this);
        _terrain = GetParent() as MapTerrain ?? MapTerrain.Instance;
        // (After the ground has been shaped: the terrain makes it in its own _Ready, which comes after this one.)
        Callable.From(Grow).CallDeferred();
    }

    public override void _ExitTree() => All.Remove(this);

    // ── Growing the woods ───────────────────────────────────────

    void Grow()
    {
        if (_terrain == null || TreeScenes.Length == 0) return;
        foreach (var scene in TreeScenes)
            if (scene != null) _kinds.Add(Describe(scene));
        if (_kinds.Count == 0) return;

        var rng = new RandomNumberGenerator { Seed = (ulong)Seed };
        var woods = new FastNoiseLite { Seed = Seed, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 1f / Mathf.Max(WoodSize, 10f), FractalOctaves = 3 };
        var sorts = new FastNoiseLite { Seed = Seed + 31, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 1f / Mathf.Max(WoodSize * 0.7f, 10f) };
        float half = _terrain.Size * 0.5f - 10f;
        float inner = Mathf.Min(_terrain.PlayHalf + 100f, half);
        // The noise is spread about its middle; the threshold that leaves Cover of the ground wooded.
        float threshold = Mathf.Lerp(0.35f, -0.35f, Mathf.Clamp(Cover, 0f, 1f));
        int across = Mathf.CeilToInt(half * 2f / ChunkSize);
        for (int i = 0; i < across * across; i++) _chunks.Add(new Chunk());

        void Plant(float x, float z, float spacing)
        {
            float wood = woods.GetNoise2D(x, z);
            // Thick inside a wood, thinning towards its edge; a lone tree now and then in the open.
            float thick = Mathf.SmoothStep(threshold, threshold + 0.18f, wood);
            if (rng.Randf() > Mathf.Max(thick, 0.025f)) return;
            if (new Vector2(x, z).Length() < ClearRadius || _terrain.WaterDepthAt(x, z) > 0f || _terrain.OnTrack(x, z, 3f)) return;
            // Too steep, or right at the water's edge: nothing grows.
            float y = _terrain.HeightAt(x, z);
            if (_terrain.HasWater && y < _terrain.WaterLevel + 0.6f) return;
            if (Mathf.Abs(_terrain.HeightAt(x + 2f, z) - y) > 2.2f || Mathf.Abs(_terrain.HeightAt(x, z + 2f) - y) > 2.2f) return;
            // Each wood favours one or two kinds.
            float sort = sorts.GetNoise2D(x, z) * 0.5f + 0.5f;
            int kind = rng.Randf() < 0.7f ? Mathf.Clamp((int)(sort * _kinds.Count), 0, _kinds.Count - 1) : rng.RandiRange(0, _kinds.Count - 1);
            float size = 1f + rng.RandfRange(-SizeVariety, SizeVariety);
            var basis = new Basis(Vector3.Up, rng.RandfRange(0f, Mathf.Tau)) * new Basis(Vector3.Right, rng.RandfRange(-0.04f, 0.04f));
            // (A little into the ground, so no root shows on a slope.)
            var at = new Transform3D(basis.Scaled(Vector3.One * size), new Vector3(x, y - 0.15f, z));
            int cx = Mathf.Clamp((int)((x + half) / ChunkSize), 0, across - 1), cz = Mathf.Clamp((int)((z + half) / ChunkSize), 0, across - 1);
            int chunk = cz * across + cx;
            _trees.Add(new Tree { Kind = kind, Chunk = chunk, At = at });
            _chunks[chunk].Trees.Add(_trees.Count - 1);
        }

        for (float x = -half; x < half; x += Spacing)
        for (float z = -half; z < half; z += Spacing)
        {
            bool outer = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) > inner;
            // Out beyond the battlefield only some of the cells, as if the grid were coarser there.
            if (outer && rng.Randf() > (Spacing * Spacing) / (OuterSpacing * OuterSpacing)) continue;
            Plant(x + rng.RandfRange(-0.45f, 0.45f) * Spacing, z + rng.RandfRange(-0.45f, 0.45f) * Spacing, Spacing);
        }

        for (int c = 0; c < _chunks.Count; c++)
            if (_chunks[c].Trees.Count > 0) Build(c, across, half);
    }

    /// <summary>What a tree scene is drawn from and solid with, in the axes of its root.</summary>
    static Kind Describe(PackedScene scene)
    {
        var kind = new Kind { Scene = scene };
        var root = scene.Instantiate<Node3D>();
        void Walk(Node node, Transform3D at)
        {
            foreach (Node child in node.GetChildren())
            {
                if (child is not Node3D n) continue;
                Transform3D here = at * n.Transform;
                if (n is MeshInstance3D { Mesh: not null } mi && mi.Name != "Rubble")
                {
                    // The materials the scene gives its surfaces, baked into a copy of the mesh: one multimesh draws it.
                    var mesh = (Mesh)mi.Mesh.Duplicate();
                    for (int s = 0; s < mesh.GetSurfaceCount(); s++)
                    {
                        var m = mi.MaterialOverride ?? mi.GetSurfaceOverrideMaterial(s) ?? mi.Mesh.SurfaceGetMaterial(s);
                        if (m != null && mesh is ArrayMesh am) am.SurfaceSetMaterial(s, m);
                    }
                    kind.Pieces.Add((mesh, here));
                }
                if (n is CollisionShape3D { Shape: not null } cs)
                {
                    kind.Shapes.Add((cs.Shape, here));
                    kind.Reach = Mathf.Max(kind.Reach, new Vector2(here.Origin.X, here.Origin.Z).Length() + 1.5f);
                }
                Walk(n, here);
            }
        }
        Walk(root, Transform3D.Identity);
        root.Free();
        return kind;
    }

    void Build(int index, int across, float half)
    {
        var chunk = _chunks[index];
        var holder = new Node3D { Name = "Chunk" + index };
        AddChild(holder);
        float cx = -half + (index % across + 0.5f) * ChunkSize, cz = -half + (index / across + 0.5f) * ChunkSize;
        Vector3 middle = new(cx, _terrain.HeightAt(cx, cz), cz);

        // Which trees of each kind, in the order of their slots in the multimeshes.
        var byKind = new Dictionary<int, List<int>>();
        foreach (int t in chunk.Trees)
        {
            var tree = _trees[t];
            if (!byKind.TryGetValue(tree.Kind, out var list)) byKind[tree.Kind] = list = new List<int>();
            tree.Slot = list.Count;
            _trees[t] = tree;
            list.Add(t);
        }
        foreach (var (k, list) in byKind)
        {
            var kind = _kinds[k];
            var meshes = new MultiMesh[kind.Pieces.Count];
            for (int p = 0; p < kind.Pieces.Count; p++)
            {
                var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = kind.Pieces[p].mesh, InstanceCount = list.Count };
                for (int i = 0; i < list.Count; i++)
                    mm.SetInstanceTransform(i, Local(_trees[list[i]].At, middle) * kind.Pieces[p].at);
                meshes[p] = mm;
                holder.AddChild(new MultiMeshInstance3D
                {
                    Multimesh = mm,
                    Position = middle,
                    VisibilityRangeEnd = DrawDistance,
                    VisibilityRangeEndMargin = 40f,
                    VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
                });
            }
            chunk.Meshes[k] = meshes;
        }
        // Shadows only from the squares near enough for them to be seen.
        var shadows = new Node3D { Name = "Shadows" };
        foreach (Node child in holder.GetChildren())
            if (child is MultiMeshInstance3D mmi)
            {
                var caster = (MultiMeshInstance3D)mmi.Duplicate();
                caster.CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
                caster.VisibilityRangeEnd = ShadowDistance;
                caster.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
                mmi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                shadows.AddChild(caster);
            }
        holder.AddChild(shadows);

        // Solid: every shape of every tree, in one body.
        chunk.Body = new ForestChunkBody { Name = "Body", Forest = this, CollisionLayer = Phys.LayerProps, CollisionMask = 0 };
        holder.AddChild(chunk.Body);
        foreach (int t in chunk.Trees)
        {
            var tree = _trees[t];
            var kind = _kinds[tree.Kind];
            tree.FirstShape = chunk.Body.GetChildCount();
            tree.ShapeCount = kind.Shapes.Count;
            _trees[t] = tree;
            foreach (var (shape, at) in kind.Shapes)
                chunk.Body.AddChild(new CollisionShape3D { Shape = shape, Transform = tree.At * at });
        }
        chunk.Body.Trees = chunk.Trees;
    }

    static Transform3D Local(Transform3D at, Vector3 middle) => new(at.Basis, at.Origin - middle);

    // ── Trees coming down ───────────────────────────────────────

    /// <summary>
    /// The tree becomes its own prop scene where it stood (and is no longer drawn or solid here); the
    /// scene is returned, ready to take a blow. Every computer of a network game does it in the same order.
    /// </summary>
    public Destructible Materialize(int index)
    {
        if (index < 0 || index >= _trees.Count || _trees[index].Gone) return null;
        Hide(index);
        NetLink.I?.World(4, All.IndexOf(this), index, Vector3.Zero, 0f);
        var tree = _trees[index];
        var node = _kinds[tree.Kind].Scene.Instantiate<Destructible>();
        node.Transform = tree.At;
        Lod.Apply(node, 70f, 260f, 900f);
        AddChild(node);
        return node;
    }

    void Hide(int index)
    {
        var tree = _trees[index];
        tree.Gone = true;
        _trees[index] = tree;
        var chunk = _chunks[tree.Chunk];
        if (chunk.Meshes.TryGetValue(tree.Kind, out var meshes))
            foreach (var mm in meshes) mm.SetInstanceTransform(tree.Slot, new Transform3D(new Basis(Vector3.Right, Vector3.Zero, Vector3.Zero), Vector3.Zero));
        for (int s = 0; s < tree.ShapeCount; s++)
            if (chunk.Body.GetChild(tree.FirstShape + s) is CollisionShape3D cs) cs.Disabled = true;
    }

    /// <summary>Network game: another computer has turned this tree into its scene.</summary>
    public void NetMaterialize(int index) => Materialize(index);

    /// <summary>The standing tree of this square nearest to a point (within its crown's reach), or -1.</summary>
    int Nearest(List<int> trees, Vector3 point)
    {
        int best = -1;
        float bestD = float.MaxValue;
        foreach (int t in trees)
        {
            var tree = _trees[t];
            if (tree.Gone) continue;
            Vector3 d = point - tree.At.Origin;
            float flat = new Vector2(d.X, d.Z).Length();
            if (flat > _kinds[tree.Kind].Reach * tree.At.Basis.Scale.X || flat >= bestD) continue;
            bestD = flat;
            best = t;
        }
        return best;
    }

    /// <summary>A blow to a square of woods: the tree it landed on takes it, as its own scene.</summary>
    public void Strike(List<int> trees, float amount, Vector3 point)
    {
        int t = Nearest(trees, point);
        if (t < 0) return;
        var prop = Materialize(t);
        if (prop == null) return;
        // The piece of it that was hit: the nearest of its parts (a crown, the trunk), or the tree as a whole.
        IDamageable target = prop;
        float best = float.MaxValue;
        foreach (Node child in prop.GetChildren())
            if (child is PropPart part && part.GlobalPosition.DistanceSquaredTo(point) < best)
            {
                best = part.GlobalPosition.DistanceSquaredTo(point);
                target = part;
            }
        target.TakeDamage(amount, point);
    }

    /// <summary>Every tree a body of this radius walks into goes down; how many did.</summary>
    public static int TrampleAround(Vector3 position, float radius, Vector3 direction)
    {
        int down = 0;
        foreach (var forest in All) down += forest.Trample(position, radius, direction);
        return down;
    }

    int Trample(Vector3 position, float radius, Vector3 direction)
    {
        if (_chunks.Count == 0) return 0;
        float half = _terrain.Size * 0.5f - 10f;
        int across = Mathf.CeilToInt(half * 2f / ChunkSize);
        int down = 0;
        // The squares the body can reach into.
        int x0 = Mathf.Clamp((int)((position.X - radius - 3f + half) / ChunkSize), 0, across - 1), x1 = Mathf.Clamp((int)((position.X + radius + 3f + half) / ChunkSize), 0, across - 1);
        int z0 = Mathf.Clamp((int)((position.Z - radius - 3f + half) / ChunkSize), 0, across - 1), z1 = Mathf.Clamp((int)((position.Z + radius + 3f + half) / ChunkSize), 0, across - 1);
        for (int cx = x0; cx <= x1; cx++)
        for (int cz = z0; cz <= z1; cz++)
            foreach (int t in _chunks[cz * across + cx].Trees)
            {
                var tree = _trees[t];
                if (tree.Gone) continue;
                Vector3 d = tree.At.Origin - position;
                // (the trunk's girth, roughly)
                if (new Vector2(d.X, d.Z).Length() > radius + 0.8f * tree.At.Basis.Scale.X) continue;
                Materialize(t)?.DestroyWhole(direction);
                down++;
            }
        return down;
    }

    /// <summary>A nuclear blast: every tree within the radius is flattened (gone without a trace of its scene).</summary>
    public static void Flatten(Vector3 center, float radius)
    {
        NetLink.I?.World(5, 0, 0, center, radius);
        foreach (var forest in All)
            for (int t = 0; t < forest._trees.Count; t++)
            {
                var tree = forest._trees[t];
                if (tree.Gone) continue;
                Vector3 d = tree.At.Origin - center;
                if (d.X * d.X + d.Z * d.Z < radius * radius) forest.Hide(t);
            }
    }
}
