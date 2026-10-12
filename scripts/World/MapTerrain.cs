using Godot;

namespace MechSim;

/// <summary>
/// Generated ground for a map: a height-field of ridges (dunes, snow drifts) over broad hills, with an
/// optional river and lakes, plus scattered props. The node, its materials and the prop prefabs are set
/// up in the terrain scene (desert_terrain.tscn, winter_terrain.tscn, city_terrain.tscn); this builds
/// the mesh and collision from noise at start-up and instances the props. Change Seed (or any shape
/// parameter) in the inspector for a different map.
/// </summary>
public partial class MapTerrain : StaticBody3D
{
    public static MapTerrain Instance { get; private set; }

    [ExportGroup("Shape")]
    [Export] public int Seed = 1337;
    /// <summary>Side of the square map, metres.</summary>
    [Export] public float Size = 800f;
    /// <summary>Grid step, metres. Smaller = smoother, more triangles.</summary>
    [Export] public float CellSize = 4f;
    /// <summary>Height of the long ridges (dunes, drifts).</summary>
    [Export] public float RidgeHeight = 5f;
    [Export] public float RidgeLength = 95f;
    /// <summary>Height of the broad hills under the ridges.</summary>
    [Export] public float HillHeight = 12f;
    [Export] public float HillLength = 260f;
    /// <summary>Radius of level ground around the map centre (the player's start).</summary>
    [Export] public float FlatRadius = 28f;

    [ExportGroup("Water")]
    /// <summary>Fills everything below WaterLevel (lakes in the hollows) and cuts a river across the map.</summary>
    [Export] public bool HasWater;
    [Export] public float WaterLevel = -4.5f;
    /// <summary>Deepest the water gets; mechs wade, they don't swim.</summary>
    [Export] public float MaxDepth = 3f;
    [Export] public float RiverWidth = 36f;
    /// <summary>How far from the map centre the river runs.</summary>
    [Export] public float RiverOffset = 170f;
    /// <summary>How much the river wanders from side to side.</summary>
    [Export] public float RiverMeander = 70f;
    /// <summary>Everything with Z beyond this is open sea (the ground slopes away under water). 0 = no sea.</summary>
    [Export] public float SeaStart;
    /// <summary>Spawned where a foot comes down in water.</summary>
    [Export] public PackedScene SplashScene;

    [ExportGroup("Props")]
    /// <summary>Solid obstacles: rocks, boulders.</summary>
    [Export] public PackedScene[] SolidScenes = System.Array.Empty<PackedScene>();
    [Export] public int SolidCount = 36;
    /// <summary>Vegetation: cacti, trees.</summary>
    [Export] public PackedScene[] PlantScenes = System.Array.Empty<PackedScene>();
    [Export] public int PlantCount = 90;
    /// <summary>Things that move on their own (tumbleweeds). Optional.</summary>
    [Export] public PackedScene DrifterScene;
    [Export] public int DrifterCount = 16;

    /// <summary>Things that stand in the water (oil rigs).</summary>
    [Export] public PackedScene[] WaterPropScenes = System.Array.Empty<PackedScene>();
    [Export] public int WaterPropCount;

    [ExportGroup("Effects")]
    /// <summary>Mark a mech's foot leaves on this ground.</summary>
    [Export] public PackedScene FootprintScene;

    [ExportGroup("Radar map colours")]
    [Export] public Color MapLow = new(0.1f, 0.25f, 0.14f);
    [Export] public Color MapHigh = new(0.5f, 0.95f, 0.6f);
    [Export] public Color MapWater = new(0.1f, 0.3f, 0.6f);

    /// <summary>Top-down relief picture of the whole map for the radar (north = up).</summary>
    public ImageTexture MapTexture { get; private set; }

    FastNoiseLite _ridges, _hills;
    // Craters: how far each grid point has been dug down (or heaped up) since the map was made.
    float[] _dig;
    int _grid;
    bool _dirty;
    float _rebuildWait;

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Ready()
    {
        BuildSurface();
        BuildMapTexture();
        ScatterProps();

        var water = GetNodeOrNull<MeshInstance3D>("Water");
        if (water == null) return;
        water.Visible = HasWater;
        water.Mesh = new PlaneMesh { Size = new Vector2(Size, Size) };
        water.Position = new Vector3(0f, WaterLevel, 0f);
    }

    void EnsureNoise()
    {
        if (_ridges != null) return;
        _ridges = new FastNoiseLite { Seed = Seed, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 1f / Mathf.Max(RidgeLength, 1f) };
        _hills = new FastNoiseLite { Seed = Seed + 1, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 1f / Mathf.Max(HillLength, 1f) };
    }

    /// <summary>Ground height at a world position, craters included. Works before the mesh is built.</summary>
    public float HeightAt(float x, float z) => BaseHeightAt(x, z) + DigAt(x, z);

    /// <summary>How far the ground here has been dug away by explosions (negative) or heaped up (positive).</summary>
    float DigAt(float x, float z)
    {
        if (_dig == null) return 0f;
        float step = Size / (_grid - 1);
        float gx = Mathf.Clamp((x + Size * 0.5f) / step, 0f, _grid - 1.001f), gz = Mathf.Clamp((z + Size * 0.5f) / step, 0f, _grid - 1.001f);
        int i = (int)gx, j = (int)gz;
        float fx = gx - i, fz = gz - j;
        float a = _dig[j * _grid + i], b = _dig[j * _grid + i + 1], c = _dig[(j + 1) * _grid + i], d = _dig[(j + 1) * _grid + i + 1];
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
    }

    /// <summary>
    /// Digs a crater: a bowl of the given radius and depth with a low rim thrown up around it. The ground
    /// really changes shape — what stands on it sinks in, shots and feet follow the new surface.
    /// Only craters wider than a grid cell show; smaller hits leave a mark instead (see Impacts).
    /// </summary>
    public void AddCrater(Vector3 center, float radius, float depth)
    {
        if (_dig == null || radius < CellSize * 0.75f) return;
        float step = Size / (_grid - 1), half = Size * 0.5f, reach = radius * 1.5f;
        int i0 = Mathf.Max(0, Mathf.FloorToInt((center.X - reach + half) / step)), i1 = Mathf.Min(_grid - 1, Mathf.CeilToInt((center.X + reach + half) / step));
        int j0 = Mathf.Max(0, Mathf.FloorToInt((center.Z - reach + half) / step)), j1 = Mathf.Min(_grid - 1, Mathf.CeilToInt((center.Z + reach + half) / step));
        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
        {
            float d = new Vector2(-half + i * step - center.X, -half + j * step - center.Z).Length() / radius;
            if (d >= 1.5f) continue;
            // Bowl inside the radius, rim just outside it.
            float change = d < 1f ? -depth * (1f - d * d) : depth * 0.22f * Mathf.Sin((d - 1f) * Mathf.Tau);
            int k = j * _grid + i;
            // Not bottomless: repeated hits on one spot stop a few metres down.
            _dig[k] = Mathf.Max(_dig[k] + change, -6f);
        }
        _dirty = true;
    }

    public override void _Process(double delta)
    {
        // Reshaping the ground is costly, so a burst of explosions is folded into one rebuild.
        _rebuildWait -= (float)delta;
        if (!_dirty || _rebuildWait > 0f) return;
        _dirty = false;
        _rebuildWait = 0.35f;
        BuildSurface();
    }

    float BaseHeightAt(float x, float z)
    {
        EnsureNoise();
        float hills = _hills.GetNoise2D(x, z) * HillHeight;
        // Ridged noise, stretched across the wind, reads as dune crests.
        float ridge = 1f - Mathf.Abs(_ridges.GetNoise2D(x * 0.45f, z));
        float ridges = ridge * ridge * RidgeHeight;
        // Level pad around the start so the mech doesn't spawn on a slope.
        float flat = Mathf.SmoothStep(FlatRadius, FlatRadius * 2.5f, new Vector2(x, z).Length());
        float h = (hills + ridges) * flat;
        if (!HasWater) return h;

        // The river runs roughly along X, wandering; its bed is cut below the water with sloping banks.
        float centre = RiverOffset + RiverMeander * (Mathf.Sin(x / 140f + Seed) * 0.7f + Mathf.Sin(x / 57f + Seed * 2f) * 0.3f);
        float bank = Mathf.SmoothStep(RiverWidth * 0.5f, RiverWidth * 1.5f, Mathf.Abs(z - centre));
        h = Mathf.Lerp(Mathf.Min(h, WaterLevel - MaxDepth * 0.8f), h, bank);
        // The shore: land runs down a beach into the sea.
        if (SeaStart != 0f)
            h = Mathf.Lerp(h, WaterLevel - MaxDepth, Mathf.SmoothStep(SeaStart - 70f, SeaStart + 30f, z));
        return Mathf.Max(h, WaterLevel - MaxDepth);
    }

    /// <summary>Depth of water over the ground at a position; 0 on dry land.</summary>
    public float WaterDepthAt(float x, float z) => HasWater ? Mathf.Max(0f, WaterLevel - HeightAt(x, z)) : 0f;

    readonly RandomNumberGenerator _spotRng = new();

    /// <summary>True if there is at least this much water here and all around within the given radius.</summary>
    public bool IsOpenWater(float x, float z, float minDepth, float radius)
    {
        if (!HasWater || WaterDepthAt(x, z) < minDepth) return false;
        for (int i = 0; i < 8 && radius > 0f; i++)
        {
            float a = i * Mathf.Tau / 8f;
            if (WaterDepthAt(x + Mathf.Cos(a) * radius, z + Mathf.Sin(a) * radius) < minDepth) return false;
        }
        return true;
    }

    public bool HasSea => HasWater && SeaStart != 0f;

    /// <summary>A random place out at sea with room around it, at the water's surface; null on maps with no sea.</summary>
    public Vector3? RandomSeaSpot(float clearRadius)
    {
        if (!HasSea) return null;
        float half = Size * 0.5f - 30f - clearRadius;
        for (int tries = 0; tries < 300; tries++)
        {
            float x = _spotRng.RandfRange(-half, half), z = _spotRng.RandfRange(SeaStart + 40f, half);
            if (IsOpenWater(x, z, MaxDepth * 0.6f, clearRadius)) return new Vector3(x, WaterLevel, z);
        }
        return null;
    }

    /// <summary>
    /// A random place with at least this much water over it and around it, at the water's surface;
    /// null if none turns up.
    /// </summary>
    public Vector3? RandomWaterSpot(float minDepth, float clearRadius = 0f)
    {
        if (!HasWater) return null;
        float half = Size * 0.5f - 30f - clearRadius;
        for (int tries = 0; tries < 300; tries++)
        {
            float x = _spotRng.RandfRange(-half, half), z = _spotRng.RandfRange(-half, half);
            if (IsOpenWater(x, z, minDepth, clearRadius)) return new Vector3(x, WaterLevel, z);
        }
        return null;
    }

    float[] _base;      // ground heights at the grid points as generated, before any craters
    int[] _indices;
    HeightMapShape3D _heights;

    /// <summary>
    /// Builds (or, after a crater, rebuilds) the visible mesh and the collision from the grid of heights.
    /// The noise is only sampled the first time; a rebuild just re-reads the grid with the craters applied.
    /// </summary>
    void BuildSurface()
    {
        int n = Mathf.Max(2, Mathf.RoundToInt(Size / CellSize) + 1);
        float step = Size / (n - 1);
        float half = Size * 0.5f;
        if (_dig == null)
        {
            _grid = n;
            _dig = new float[n * n];
            _base = new float[n * n];
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                _base[j * n + i] = BaseHeightAt(-half + i * step, -half + j * step);

            _indices = new int[(n - 1) * (n - 1) * 6];
            int k = 0;
            for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                // Clockwise seen from above = front face up.
                _indices[k++] = a; _indices[k++] = b; _indices[k++] = d;
                _indices[k++] = a; _indices[k++] = d; _indices[k++] = c;
            }

            // Collision is a height field: cheap to update when the ground changes. Its samples are one
            // unit apart, so the shape node is stretched to the grid step.
            _heights = new HeightMapShape3D { MapWidth = n, MapDepth = n };
            var collision = GetNode<CollisionShape3D>("Collision");
            collision.Shape = _heights;
            collision.Scale = new Vector3(step, 1f, step);
        }

        var heights = new float[n * n];
        var verts = new Vector3[n * n];
        var normals = new Vector3[n * n];
        for (int k = 0; k < heights.Length; k++) heights[k] = _base[k] + _dig[k];
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            int k = j * n + i;
            verts[k] = new Vector3(-half + i * step, heights[k], -half + j * step);
            // Slope from the neighbouring grid points.
            float dx = heights[j * n + Mathf.Min(i + 1, n - 1)] - heights[j * n + Mathf.Max(i - 1, 0)];
            float dz = heights[Mathf.Min(j + 1, n - 1) * n + i] - heights[Mathf.Max(j - 1, 0) * n + i];
            normals[k] = new Vector3(-dx, 2f * step, -dz).Normalized();
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = _indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        GetNode<MeshInstance3D>("Mesh").Mesh = mesh;
        _heights.MapData = heights;
    }

    void BuildMapTexture()
    {
        const int n = 256;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgb8);
        float range = Mathf.Max(1f, HillHeight + RidgeHeight);
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            float x = (i + 0.5f) / n * Size - Size * 0.5f, z = (j + 0.5f) / n * Size - Size * 0.5f;
            float h = HeightAt(x, z);
            if (HasWater && h < WaterLevel)
            {
                img.SetPixel(i, j, MapWater);
                continue;
            }
            // Height tint plus a little shading from the slope, as if lit from the north-west.
            float t = Mathf.Clamp(h / range * 0.5f + 0.5f, 0f, 1f);
            float slope = (HeightAt(x + 3f, z + 3f) - h) * 0.12f;
            Color c = MapLow.Lerp(MapHigh, t) * Mathf.Clamp(1f - slope, 0.55f, 1.35f);
            img.SetPixel(i, j, new Color(c.R, c.G, c.B));
        }
        MapTexture = ImageTexture.CreateFromImage(img);
    }

    void ScatterProps()
    {
        var parent = GetNode<Node3D>("Props");
        var rng = new RandomNumberGenerator { Seed = (ulong)Seed };
        float half = Size * 0.5f - 15f;

        Vector3 RandomSpot(float clear)
        {
            for (int tries = 0; tries < 30; tries++)
            {
                float x = rng.RandfRange(-half, half), z = rng.RandfRange(-half, half);
                // Not on the start pad and not in the water.
                if (new Vector2(x, z).Length() > clear && WaterDepthAt(x, z) <= 0f)
                    return new Vector3(x, HeightAt(x, z), z);
            }
            return new Vector3(half, HeightAt(half, half), half);
        }

        void Place(PackedScene scene, Vector3 pos, float tilt)
        {
            var node = scene.Instantiate<Node3D>();
            Basis basis = new Basis(Vector3.Up, rng.RandfRange(0f, Mathf.Tau))
                        * new Basis(Vector3.Right, rng.RandfRange(-tilt, tilt));
            // Placed before it enters the tree, so the prop knows where it stands when it wakes up.
            node.Transform = new Transform3D(basis, pos);
            parent.AddChild(node);
        }

        if (SolidScenes.Length > 0)
            for (int i = 0; i < SolidCount; i++)
                Place(SolidScenes[rng.RandiRange(0, SolidScenes.Length - 1)], RandomSpot(FlatRadius * 1.5f), 0.2f);

        if (PlantScenes.Length > 0)
            for (int i = 0; i < PlantCount; i++)
                Place(PlantScenes[rng.RandiRange(0, PlantScenes.Length - 1)], RandomSpot(FlatRadius * 0.6f), 0.04f);

        if (DrifterScene != null)
            for (int i = 0; i < DrifterCount; i++)
                Place(DrifterScene, RandomSpot(0f), 0f);

        if (WaterPropScenes.Length > 0)
        {
            _spotRng.Seed = (ulong)Seed + 7;
            for (int i = 0; i < WaterPropCount; i++)
            {
                Vector3? spot = RandomWaterSpot(MaxDepth * 0.9f);
                if (spot != null) Place(WaterPropScenes[rng.RandiRange(0, WaterPropScenes.Length - 1)], spot.Value, 0f);
            }
        }
    }
}
