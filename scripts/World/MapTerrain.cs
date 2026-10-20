using Godot;

namespace MechSim;

/// <summary>
/// Generated ground for a map: a height-field of ridges (dunes, snow drifts) over broad hills, with an
/// optional river and lakes, plus scattered props. The node, its materials and the prop prefabs are set
/// up in the terrain scene (desert_terrain.tscn, winter_terrain.tscn, city_terrain.tscn); this builds
/// the mesh and collision from noise at start-up and instances the props. Change Seed (or any shape
/// parameter) in the inspector for a different map.
/// Beyond the square the battle is fought on, the same ground runs on to the horizon as a coarse
/// backdrop with mountains (no collision: nothing can get there).
/// </summary>
public partial class MapTerrain : StaticBody3D
{
    public static MapTerrain Instance { get; private set; }

    /// <summary>Render layer the ground is on (besides the usual one): what decals on the ground project onto.</summary>
    public const int GroundLayer = 11;

    /// <summary>
    /// How far from the middle of the map the battlefield reaches (on either axis). Beyond it the ground
    /// goes on, but whoever stays out there longer than <see cref="OutOfBoundsTime"/> is destroyed.
    /// </summary>
    public float PlayHalf => Size * 0.5f - Border - 12f;
    /// <summary>How far anything can get at all: the end of the real ground (past it there is only scenery).</summary>
    public float HardHalf => Size * 0.5f - 25f;

    /// <summary>Whether a position is beyond the edge of the battlefield.</summary>
    public bool OutOfBounds(Vector3 p) => Mathf.Abs(p.X) > PlayHalf || Mathf.Abs(p.Z) > PlayHalf;

    /// <summary>The position, brought back to the end of the real ground if it was beyond it.</summary>
    public Vector3 Contain(Vector3 p, out bool wasOutside)
    {
        float limit = HardHalf;
        wasOutside = Mathf.Abs(p.X) > limit || Mathf.Abs(p.Z) > limit;
        return wasOutside ? new Vector3(Mathf.Clamp(p.X, -limit, limit), p.Y, Mathf.Clamp(p.Z, -limit, limit)) : p;
    }

    [ExportGroup("Shape")]
    [Export] public int Seed = 1337;
    /// <summary>Side of the square map, metres.</summary>
    [Export] public float Size = 800f;
    /// <summary>
    /// Width of the strip along the map's edge beyond the battlefield: the ground, the plants, the houses
    /// are all there and can be gone into, but not for long (see <see cref="OutOfBoundsTime"/>).
    /// </summary>
    [Export] public float Border = 120f;
    /// <summary>How thickly plants and rocks grow beyond the battlefield, as a share of how they grow on it.</summary>
    [Export(PropertyHint.Range, "0,1")] public float OuterPropShare = 0.5f;
    /// <summary>Seconds a machine may stay beyond the edge of the battlefield before it is destroyed.</summary>
    [Export] public float OutOfBoundsTime = 10f;
    /// <summary>Grid step, metres. Smaller = smoother, more triangles.</summary>
    [Export] public float CellSize = 4f;
    /// <summary>Height of the long ridges (dunes, drifts).</summary>
    [Export] public float RidgeHeight = 5f;
    [Export] public float RidgeLength = 95f;
    /// <summary>Height of the broad hills under the ridges.</summary>
    [Export] public float HillHeight = 12f;
    [Export] public float HillLength = 260f;
    /// <summary>Height of the small bumps and hollows that break up the smooth hills.</summary>
    [Export] public float DetailHeight = 1.4f;
    /// <summary>Radius of level ground around the map centre (the player's start).</summary>
    [Export] public float FlatRadius = 28f;

    [ExportGroup("Backdrop")]
    /// <summary>Side of the square of scenery around the map, metres. 0 = none.</summary>
    [Export] public float FarSize = 7000f;
    /// <summary>Grid step of the scenery.</summary>
    [Export] public float FarCell = 32f;
    /// <summary>Height of the mountains on the horizon.</summary>
    [Export] public float MountainHeight = 300f;
    /// <summary>Width of a mountain ridge.</summary>
    [Export] public float MountainLength = 2200f;
    /// <summary>How far past the map's edge the ground stays low before the mountains rise.</summary>
    [Export] public float MountainStart = 350f;

    [ExportGroup("Water")]
    /// <summary>Fills everything below WaterLevel (lakes in the hollows) and cuts a river across the map.</summary>
    [Export] public bool HasWater;
    /// <summary>Off: only the river (and the sea) hold water — the hollows are filled in to dry land.</summary>
    [Export] public bool Lakes = true;
    /// <summary>Off: no river across the map, only the lakes (and the sea).</summary>
    [Export] public bool River = true;
    [Export] public float WaterLevel = -4.5f;
    /// <summary>Deepest the water gets; mechs wade, they don't swim.</summary>
    [Export] public float MaxDepth = 3f;
    [Export] public float RiverWidth = 36f;
    /// <summary>How far from the map centre the river runs.</summary>
    [Export] public float RiverOffset = 170f;
    /// <summary>How much the river wanders from side to side.</summary>
    [Export] public float RiverMeander = 70f;
    /// <summary>Basins dug for water where the map is laid out by hand: X, Z of the middle and radius (Z of the vector).</summary>
    [Export] public Vector3[] Pools = System.Array.Empty<Vector3>();
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
    /// <summary>
    /// 0 = plants scattered evenly; towards 1 they gather into woods and thickets with open ground
    /// between (a slow noise decides where the woods are).
    /// </summary>
    [Export(PropertyHint.Range, "0,1")] public float PlantClumping = 0.7f;
    /// <summary>No plants within this distance of the middle (0: a little over the start pad), for a map with something built there.</summary>
    [Export] public float PlantClear;
    /// <summary>Plants and rocks come out between 1 − this and 1 + this of their size.</summary>
    [Export(PropertyHint.Range, "0,0.6")] public float SizeVariety = 0.3f;
    /// <summary>Things that move on their own (tumbleweeds). Optional.</summary>
    [Export] public PackedScene DrifterScene;
    [Export] public int DrifterCount = 16;

    /// <summary>Things that stand in the water (oil rigs).</summary>
    [Export] public PackedScene[] WaterPropScenes = System.Array.Empty<PackedScene>();
    [Export] public int WaterPropCount;

    /// <summary>Field repair bays: the prefab, and where on the map they stand (x, z).</summary>
    [Export] public PackedScene RepairBayScene;
    [Export] public Vector2[] RepairBaySpots = System.Array.Empty<Vector2>();

    [ExportGroup("Effects")]
    /// <summary>Mark a mech's foot leaves on this ground.</summary>
    [Export] public PackedScene FootprintScene;

    [ExportGroup("Radar map colours")]
    [Export] public Color MapLow = new(0.1f, 0.25f, 0.14f);
    [Export] public Color MapHigh = new(0.5f, 0.95f, 0.6f);
    [Export] public Color MapWater = new(0.1f, 0.3f, 0.6f);

    /// <summary>Top-down relief picture of the whole map for the radar (north = up).</summary>
    public ImageTexture MapTexture { get; private set; }

    FastNoiseLite _ridges, _hills, _detail, _mountains;
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
        // Footprints and the like are projected onto this layer only, not onto machines walking over them.
        GetNode<MeshInstance3D>("Mesh").SetLayerMaskValue(GroundLayer, true);
        BuildBackdrop();
        BuildMapTexture();
        ScatterProps();

        // The ground's shader needs to know where the water stands to draw the shore.
        if (GetNode<MeshInstance3D>("Mesh").MaterialOverride is ShaderMaterial ground && HasWater)
        {
            ground.SetShaderParameter("water_level", WaterLevel);
            ground.SetShaderParameter("shore_level", WaterLevel + 0.3f);
        }

        var water = GetNodeOrNull<MeshInstance3D>("Water");
        if (water == null) return;
        water.Visible = HasWater;
        float extent = Mathf.Max(Size, FarSize);
        water.Mesh = new PlaneMesh { Size = new Vector2(extent, extent) };
        water.Position = new Vector3(0f, WaterLevel, 0f);
    }

    void EnsureNoise()
    {
        if (_ridges != null) return;
        _ridges = new FastNoiseLite { Seed = Seed, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 1f / Mathf.Max(RidgeLength, 1f) };
        _hills = new FastNoiseLite { Seed = Seed + 1, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 1f / Mathf.Max(HillLength, 1f) };
        _detail = new FastNoiseLite
        {
            Seed = Seed + 2, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 1f / 48f,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 3, FractalGain = 0.38f,
        };
        _mountains = new FastNoiseLite
        {
            Seed = Seed + 3, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 1f / Mathf.Max(MountainLength, 1f),
            FractalType = FastNoiseLite.FractalTypeEnum.Ridged, FractalOctaves = 3, FractalGain = 0.38f,
        };
    }

    /// <summary>Ground height at a world position, craters included. Works before the mesh is built.</summary>
    public float HeightAt(float x, float z) => BaseHeightAt(x, z) + DigAt(x, z);

    /// <summary>How far the ground here has been dug away by explosions (negative) or heaped up (positive).</summary>
    /// <summary>
    /// Height of the drawn surface itself — the triangles of the ground mesh — for things that have to lie
    /// flush on it (HeightAt is the smooth land, which the mesh only meets at its grid points).
    /// </summary>
    public float MeshHeightAt(float x, float z)
    {
        if (_dig == null) return HeightAt(x, z);
        float step = Size / (_grid - 1);
        float gx = Mathf.Clamp((x + Size * 0.5f) / step, 0f, _grid - 1.001f), gz = Mathf.Clamp((z + Size * 0.5f) / step, 0f, _grid - 1.001f);
        int i = (int)gx, j = (int)gz, k = j * _grid + i;
        float fx = gx - i, fz = gz - j;
        float a = _base[k] + _dig[k], b = _base[k + 1] + _dig[k + 1], c = _base[k + _grid] + _dig[k + _grid], d = _base[k + _grid + 1] + _dig[k + _grid + 1];
        // Each cell is two triangles split along b–c, the way the physics engine splits its height field.
        return fx + fz <= 1f ? a + (b - a) * fx + (c - a) * fz : d + (c - d) * (1f - fx) + (b - d) * (1f - fz);
    }

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
    /// Only craters wider than a grid cell show; every hit leaves a mark as well (see Impacts).
    /// </summary>
    /// <summary>The ground has just changed shape (craters) within this area (x, z): whatever lies on it there should settle again.</summary>
    public static event System.Action<Rect2> Reshaped;

    /// <param name="fromHost">The host of a network game says so (on a client nothing digs by itself).</param>
    public void AddCrater(Vector3 center, float radius, float depth, bool fromHost = false)
    {
        if (_dig == null || radius < CellSize * 0.75f) return;
        if (!Net.Authority && !fromHost) return;
        NetLink.I?.World(3, 0, 0, center, radius, depth);
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
        if (!_dirty)
        {
            _dirtyI0 = i0; _dirtyI1 = i1; _dirtyJ0 = j0; _dirtyJ1 = j1;
        }
        else
        {
            _dirtyI0 = Mathf.Min(_dirtyI0, i0); _dirtyI1 = Mathf.Max(_dirtyI1, i1);
            _dirtyJ0 = Mathf.Min(_dirtyJ0, j0); _dirtyJ1 = Mathf.Max(_dirtyJ1, j1);
        }
        _dirty = true;
    }

    // The part of the grid dug since the last rebuild.
    int _dirtyI0, _dirtyI1, _dirtyJ0, _dirtyJ1;

    public override void _Process(double delta)
    {
        // Reshaping the ground is costly, so a burst of explosions is folded into one rebuild.
        _rebuildWait -= (float)delta;
        if (!_dirty || _rebuildWait > 0f) return;
        _dirty = false;
        _rebuildWait = 0.35f;
        long prof = Prof.Now();
        UpdateSurface(_dirtyI0, _dirtyI1, _dirtyJ0, _dirtyJ1);
        Prof.Add(3, prof);
        // The ground moved only here: marks elsewhere needn't look.
        float step = Size / (_grid - 1), half = Size * 0.5f;
        var area = new Rect2(-half + _dirtyI0 * step, -half + _dirtyJ0 * step, (_dirtyI1 - _dirtyI0) * step, (_dirtyJ1 - _dirtyJ0) * step);
        Reshaped?.Invoke(area);
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
        float h = (hills + ridges + _detail.GetNoise2D(x, z) * DetailHeight) * flat;
        if (!HasWater) return h;
        if (!Lakes)
        {
            // No standing water in the hollows: they bottom out, gently, a little above the water level.
            float dry = WaterLevel + 1f;
            h = dry + 0.8f * Mathf.Log(1f + Mathf.Exp(Mathf.Min((h - dry) / 0.8f, 30f)));
        }

        // The river runs roughly along X, wandering; its bed is cut below the water with sloping banks.
        if (River)
        {
            float centre = RiverOffset + RiverMeander * (Mathf.Sin(x / 140f + Seed) * 0.7f + Mathf.Sin(x / 57f + Seed * 2f) * 0.3f);
            float bank = Mathf.SmoothStep(RiverWidth * 0.5f, RiverWidth * 1.5f, Mathf.Abs(z - centre));
            h = Mathf.Lerp(Mathf.Min(h, WaterLevel - MaxDepth * 0.8f), h, bank);
        }
        foreach (var pool in Pools)
        {
            float shore = Mathf.SmoothStep(pool.Z * 0.75f, pool.Z * 1.2f, new Vector2(x - pool.X, z - pool.Y).Length());
            h = Mathf.Lerp(Mathf.Min(h, WaterLevel - MaxDepth * 0.8f), h, shore);
        }
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
        float half = PlayHalf - 18f - clearRadius;
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
    // The ground as last built, kept so that a crater only touches the part of it that changed.
    float[] _heightsNow;
    Vector3[] _verts, _normals;
    Color[] _colors;
    ArrayMesh _mesh;
    // The collision is cut into square tiles of a height field each: a crater rebuilds only the ones it touched.
    HeightMapShape3D[] _tiles;
    int _tileCells, _tilesAcross;
    // Rows of the ground are rewritten in place in the mesh's buffers: the engine keeps all the positions
    // (12 bytes each), then all the normals with their tangents (8 bytes each); the colours (4 bytes each)
    // in a buffer of their own. The new bytes are had by letting the engine pack just those rows into a
    // little mesh of their own. False = not laid out as expected: the mesh is built anew instead.
    bool _inPlace;
    int _normalStart;

    /// <summary>
    /// Builds the visible mesh and the collision from the grid of heights. The noise is only sampled
    /// here; a crater later rebuilds just its own part (see UpdateSurface).
    /// </summary>
    void BuildSurface()
    {
        int n = Mathf.Max(2, Mathf.RoundToInt(Size / CellSize) + 1);
        float step = Size / (n - 1);
        float half = Size * 0.5f;
        _grid = n;
        _dig = new float[n * n];
        _base = new float[n * n];
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
            _base[j * n + i] = BaseHeightAt(-half + i * step, -half + j * step);

        _indices = new int[(n - 1) * (n - 1) * 6];
        int q = 0;
        for (int j = 0; j < n - 1; j++)
        for (int i = 0; i < n - 1; i++)
        {
            int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
            // Clockwise seen from above = front face up. Split along b–c, as the physics engine splits
            // its height field: split the other way, the ground drawn and the ground walked on part by
            // up to a metre on the dunes, and feet and footprints sink out of sight.
            _indices[q++] = a; _indices[q++] = b; _indices[q++] = c;
            _indices[q++] = b; _indices[q++] = d; _indices[q++] = c;
        }

        _heightsNow = new float[n * n];
        _verts = new Vector3[n * n];
        _normals = new Vector3[n * n];
        _colors = new Color[n * n];
        Recompute(0, n - 1, 0, n - 1);

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _verts;
        arrays[(int)Mesh.ArrayType.Normal] = _normals;
        arrays[(int)Mesh.ArrayType.Color] = _colors;
        arrays[(int)Mesh.ArrayType.Index] = _indices;
        _mesh = new ArrayMesh();
        _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var ground = GetNode<MeshInstance3D>("Mesh");
        ground.Mesh = _mesh;
        // Room for craters below and heaps above, so the box needn't be worked out again as they come.
        Aabb box = _mesh.GetAabb();
        ground.CustomAabb = new Aabb(box.Position - new Vector3(0f, 8f, 0f), box.Size + new Vector3(0f, 12f, 0f));
        LearnLayout();

        BuildCollision(step);
    }

    /// <summary>Heights, positions, normals and colours of the grid points in the block (normals one point further round).</summary>
    void Recompute(int i0, int i1, int j0, int j1)
    {
        int n = _grid;
        float step = Size / (n - 1), half = Size * 0.5f;
        int ri0 = Mathf.Max(i0 - 1, 0), ri1 = Mathf.Min(i1 + 1, n - 1), rj0 = Mathf.Max(j0 - 1, 0), rj1 = Mathf.Min(j1 + 1, n - 1);
        for (int j = rj0; j <= rj1; j++)
        for (int i = ri0; i <= ri1; i++)
        {
            int k = j * n + i;
            _heightsNow[k] = _base[k] + _dig[k];
            // Red marks dug-up ground for the shader: craters come out dark and burnt.
            _colors[k] = new Color(Mathf.Clamp(Mathf.Abs(_dig[k]) * 0.8f, 0f, 1f), 0f, 0f);
            _verts[k] = new Vector3(-half + i * step, _heightsNow[k], -half + j * step);
        }
        for (int j = rj0; j <= rj1; j++)
        for (int i = ri0; i <= ri1; i++)
        {
            int k = j * n + i;
            // Slope from the neighbouring grid points.
            float dx = Height(i + 1, j) - Height(i - 1, j);
            float dz = Height(i, j + 1) - Height(i, j - 1);
            _normals[k] = new Vector3(-dx, 2f * step, -dz).Normalized();
        }
    }

    float Height(int i, int j)
    {
        int k = Mathf.Clamp(j, 0, _grid - 1) * _grid + Mathf.Clamp(i, 0, _grid - 1);
        return _base[k] + _dig[k];
    }

    /// <summary>After craters: the changed block of the grid is worked out again, and only it is sent to the mesh and the collision.</summary>
    void UpdateSurface(int i0, int i1, int j0, int j1)
    {
        int n = _grid;
        Recompute(i0, i1, j0, j1);
        // Whole rows, from one above to one below (their normals changed too).
        int r0 = Mathf.Max(j0 - 1, 0), r1 = Mathf.Min(j1 + 1, n - 1);
        if (_inPlace) Rewrite(r0, r1);
        else
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = _verts;
            arrays[(int)Mesh.ArrayType.Normal] = _normals;
            arrays[(int)Mesh.ArrayType.Color] = _colors;
            arrays[(int)Mesh.ArrayType.Index] = _indices;
            _mesh.ClearSurfaces();
            _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }
        UpdateCollision(i0 - 1, i1 + 1, j0 - 1, j1 + 1);
    }

    /// <summary>
    /// Finds out how the engine lays the ground's vertices out in its buffers, and checks it by writing
    /// the whole mesh the same way and comparing. If anything differs, rows are never rewritten in place.
    /// </summary>
    void LearnLayout()
    {
        _inPlace = false;
        var surface = RenderingServer.MeshGetSurface(_mesh.GetRid(), 0);
        var format = (RenderingServer.ArrayFormat)surface["format"].AsUInt64();
        int count = _verts.Length;
        int nOff = (int)RenderingServer.MeshSurfaceGetFormatOffset(format, count, (int)Mesh.ArrayType.Normal);
        byte[] vdata = surface["vertex_data"].AsByteArray(), adata = surface["attribute_data"].AsByteArray();
        if (nOff != count * 12 || vdata.Length != count * 20 || adata.Length != count * 4 || _grid < 24)
        {
            GD.Print($"Terrain: unexpected mesh layout ({vdata.Length / (float)count} + {adata.Length / (float)count} bytes a vertex, normals at {nOff}); craters rebuild the whole mesh.");
            return;
        }
        _normalStart = nOff;
        // Rows packed on their own must come out exactly as they are in the whole.
        var (p, nt, c) = Pack(10, 20);
        int from = 10 * _grid;
        for (int k = 0; k < p.Length; k++) if (p[k] != vdata[from * 12 + k]) { Mismatch("position", k); return; }
        for (int k = 0; k < nt.Length; k++) if (nt[k] != vdata[nOff + from * 8 + k]) { Mismatch("normal", k); return; }
        for (int k = 0; k < c.Length; k++) if (c[k] != adata[from * 4 + k]) { Mismatch("colour", k); return; }
        _inPlace = true;
    }

    static void Mismatch(string what, int at)
        => GD.Print($"Terrain: {what} data of rows packed apart differs at byte {at}; craters rebuild the whole mesh.");

    /// <summary>The engine's own packing of the grid rows r0..r1: positions, normals with tangents, colours.</summary>
    (byte[] positions, byte[] normals, byte[] colours) Pack(int r0, int r1)
    {
        int from = r0 * _grid, count = (r1 - r0 + 1) * _grid;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = System.MemoryExtensions.AsSpan(_verts, from, count).ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = System.MemoryExtensions.AsSpan(_normals, from, count).ToArray();
        arrays[(int)Mesh.ArrayType.Color] = System.MemoryExtensions.AsSpan(_colors, from, count).ToArray();
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, _grid };
        var piece = new ArrayMesh();
        piece.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var surface = RenderingServer.MeshGetSurface(piece.GetRid(), 0);
        byte[] v = surface["vertex_data"].AsByteArray(), a = surface["attribute_data"].AsByteArray();
        return (v[..(count * 12)], v[(count * 12)..], a);
    }

    void Rewrite(int r0, int r1)
    {
        Rid rid = _mesh.GetRid();
        int from = r0 * _grid;
        var (positions, normals, colours) = Pack(r0, r1);
        RenderingServer.MeshSurfaceUpdateVertexRegion(rid, 0, from * 12, positions);
        RenderingServer.MeshSurfaceUpdateVertexRegion(rid, 0, _normalStart + from * 8, normals);
        RenderingServer.MeshSurfaceUpdateAttributeRegion(rid, 0, from * 4, colours);
    }

    /// <summary>
    /// The collision: square tiles of height field over the grid, side by side (sharing their edge rows),
    /// all under the "Collision" node's body. A single field for the lot if the grid won't divide evenly.
    /// </summary>
    void BuildCollision(float step)
    {
        int cells = _grid - 1;
        _tileCells = cells;
        // About 32 cells a tile, as long as they come out even.
        for (int t = 32, d = 0; d <= 16 && _tileCells == cells; d++)
        {
            if (t + d <= cells && cells % (t + d) == 0) _tileCells = t + d;
            else if (t - d >= 8 && cells % (t - d) == 0) _tileCells = t - d;
        }
        _tilesAcross = cells / _tileCells;
        _tiles = new HeightMapShape3D[_tilesAcross * _tilesAcross];
        var first = GetNode<CollisionShape3D>("Collision");
        float half = Size * 0.5f;
        for (int tj = 0; tj < _tilesAcross; tj++)
        for (int ti = 0; ti < _tilesAcross; ti++)
        {
            var shape = new HeightMapShape3D { MapWidth = _tileCells + 1, MapDepth = _tileCells + 1 };
            _tiles[tj * _tilesAcross + ti] = shape;
            // Its samples are one unit apart, so the node is stretched to the grid step; the field is centred on it.
            var node = ti == 0 && tj == 0 ? first : new CollisionShape3D { Name = $"Collision{ti}_{tj}" };
            node.Shape = shape;
            node.Scale = new Vector3(step, 1f, step);
            node.Position = new Vector3(-half + (ti + 0.5f) * _tileCells * step, 0f, -half + (tj + 0.5f) * _tileCells * step);
            if (node != first) AddChild(node);
            FillTile(ti, tj);
        }
    }

    void FillTile(int ti, int tj)
    {
        int side = _tileCells + 1;
        var data = new float[side * side];
        for (int j = 0; j < side; j++)
            System.Array.Copy(_heightsNow, (tj * _tileCells + j) * _grid + ti * _tileCells, data, j * side, side);
        _tiles[tj * _tilesAcross + ti].MapData = data;
    }

    void UpdateCollision(int i0, int i1, int j0, int j1)
    {
        int last = _tilesAcross - 1;
        // A point on a tile's edge belongs to both tiles.
        int ti0 = Mathf.Clamp((Mathf.Max(i0, 0) - 1) / _tileCells, 0, last), ti1 = Mathf.Clamp(Mathf.Min(i1, _grid - 1) / _tileCells, 0, last);
        int tj0 = Mathf.Clamp((Mathf.Max(j0, 0) - 1) / _tileCells, 0, last), tj1 = Mathf.Clamp(Mathf.Min(j1, _grid - 1) / _tileCells, 0, last);
        for (int tj = tj0; tj <= tj1; tj++)
        for (int ti = ti0; ti <= ti1; ti++)
            FillTile(ti, tj);
    }

    /// <summary>Height of the scenery outside the map: the same ground, rising into mountains further out.</summary>
    float BackdropHeightAt(float x, float z)
    {
        float h = BaseHeightAt(x, z);
        float half = Size * 0.5f;
        float outside = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - half;
        if (outside <= 0f) return h;
        float rise = Mathf.SmoothStep(MountainStart, MountainStart + 1400f, outside);
        // Ridged noise is 1 on the crests; squared, the valleys between them open out.
        float ridge = _mountains.GetNoise2D(x, z) * 0.5f + 0.5f;
        float mountains = rise * (0.12f + ridge * Mathf.Sqrt(ridge)) * MountainHeight;
        // No mountains out at sea, and none standing in the river.
        if (HasSea) mountains *= 1f - Mathf.SmoothStep(SeaStart - 500f, SeaStart - 60f, z);
        if (HasWater && h < WaterLevel + 1f) mountains *= Mathf.SmoothStep(WaterLevel - 0.5f, WaterLevel + 1f, h);
        return h + mountains;
    }

    /// <summary>
    /// The scenery around the map: a coarse ring of ground out to the horizon, drawn with the map's own
    /// material. Its inner edge tucks in under the edge of the real ground, so no gap shows between them.
    /// </summary>
    void BuildBackdrop()
    {
        if (FarSize <= Size) return;
        float half = Size * 0.5f;
        // A whole number of cells to the map's edge, so a row of points lies exactly along it.
        int inner = Mathf.Max(1, Mathf.RoundToInt(half / FarCell));
        float step = half / inner;
        int outer = Mathf.CeilToInt(FarSize * 0.5f / step);
        int n = outer * 2 + 1;

        var heights = new float[n * n];
        var verts = new Vector3[n * n];
        var normals = new Vector3[n * n];
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            float x = (i - outer) * step, z = (j - outer) * step;
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - half;
            float h = BackdropHeightAt(x, z);
            // Under the real ground it sits lower, out of sight.
            if (edge < -0.01f) h -= 4f;
            else if (edge < 0.01f) h -= 0.35f;
            heights[j * n + i] = h;
            verts[j * n + i] = new Vector3(x, h, z);
        }
        // Away from the map the heights are averaged with their neighbours: a peak that is a single
        // point of so coarse a grid shows as a spike.
        var smooth = (float[])heights.Clone();
        for (int j = 1; j < n - 1; j++)
        for (int i = 1; i < n - 1; i++)
        {
            float edge = Mathf.Max(Mathf.Abs((i - outer) * step), Mathf.Abs((j - outer) * step)) - half;
            if (edge < MountainStart * 0.5f) continue;
            int k = j * n + i;
            smooth[k] = heights[k] * 0.4f + (heights[k - 1] + heights[k + 1] + heights[k - n] + heights[k + n]) * 0.15f;
            verts[k].Y = smooth[k];
        }
        heights = smooth;
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            float dx = heights[j * n + Mathf.Min(i + 1, n - 1)] - heights[j * n + Mathf.Max(i - 1, 0)];
            float dz = heights[Mathf.Min(j + 1, n - 1) * n + i] - heights[Mathf.Max(j - 1, 0) * n + i];
            normals[j * n + i] = new Vector3(-dx, 2f * step, -dz).Normalized();
        }

        var indices = new System.Collections.Generic.List<int>((n - 1) * (n - 1) * 6);
        for (int j = 0; j < n - 1; j++)
        for (int i = 0; i < n - 1; i++)
        {
            // Nothing is drawn under the map itself, apart from one row of cells along its edge.
            float cx = (i + 0.5f - outer) * step, cz = (j + 0.5f - outer) * step;
            if (Mathf.Max(Mathf.Abs(cx), Mathf.Abs(cz)) < half - step) continue;
            int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
            indices.Add(a); indices.Add(b); indices.Add(d);
            indices.Add(a); indices.Add(d); indices.Add(c);
        }

        long tA = System.Diagnostics.Stopwatch.GetTimestamp();
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        // Without colours the shader would read white, which marks scorched ground.
        arrays[(int)Mesh.ArrayType.Color] = new Color[n * n];
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var ground = GetNode<MeshInstance3D>("Mesh");
        AddChild(new MeshInstance3D
        {
            Name = "Backdrop",
            Mesh = mesh,
            MaterialOverride = ground.MaterialOverride,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = ground.Layers,
        });
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
        // The counts are for the battlefield and a strip round it; past that, out to the end of the
        // ground, the same things grow thinner (OuterPropShare as dense), to be seen rather than walked among.
        float inner = Mathf.Min(PlayHalf + 120f, half);
        float ring = (half * half - inner * inner) / (inner * inner) * OuterPropShare;
        float innerShare = 1f / (1f + ring);
        bool outer = false;

        // How many of a thing to place, the ring beyond included.
        int Total(int count) => Mathf.RoundToInt(count * (1f + ring));

        Vector3 RandomSpot(float clear)
        {
            for (int tries = 0; tries < 30; tries++)
            {
                float x, z;
                if (!outer)
                {
                    x = rng.RandfRange(-inner, inner);
                    z = rng.RandfRange(-inner, inner);
                }
                else
                {
                    // Anywhere in the ring: a point in the whole square, tried again until it is outside the inner one.
                    do
                    {
                        x = rng.RandfRange(-half, half);
                        z = rng.RandfRange(-half, half);
                    } while (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < inner);
                }
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
            float size = 1f + rng.RandfRange(-SizeVariety, SizeVariety);
            node.Transform = new Transform3D(basis.Scaled(Vector3.One * size), pos);
            // Plants and rocks are landmarks: they stay in sight further than street furniture does.
            Lod.Apply(node, 70f, 260f, 900f);
            parent.AddChild(node);
        }

        if (RepairBayScene != null)
            foreach (Vector2 spot in RepairBaySpots)
            {
                // Level, on the highest ground under the pad so that no corner is buried.
                float top = float.MinValue;
                for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    top = Mathf.Max(top, HeightAt(spot.X + i * 8f, spot.Y + j * 8f));
                var bay = RepairBayScene.Instantiate<Node3D>();
                bay.Position = new Vector3(spot.X, top, spot.Y);
                parent.AddChild(bay);
            }

        if (SolidScenes.Length > 0)
            for (int i = 0; i < Total(SolidCount); i++)
            {
                outer = rng.Randf() > innerShare;
                Place(SolidScenes[rng.RandiRange(0, SolidScenes.Length - 1)], RandomSpot(FlatRadius * 1.5f), 0.2f);
            }

        if (PlantScenes.Length > 0)
        {
            // Woods: a slow noise marks where they grow, and each wood favours one or two kinds of plant.
            var woods = new FastNoiseLite { Seed = Seed + 11, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 1f / 170f };
            for (int i = 0; i < Total(PlantCount); i++)
            {
                outer = rng.Randf() > innerShare;
                float clear = Mathf.Max(FlatRadius * 0.6f, PlantClear);
                Vector3 spot = RandomSpot(clear);
                for (int tries = 0; tries < 12; tries++)
                {
                    float density = woods.GetNoise2D(spot.X, spot.Z) * 0.5f + 0.5f;
                    if (rng.Randf() > PlantClumping * (1f - Mathf.SmoothStep(0.42f, 0.62f, density))) break;
                    spot = RandomSpot(clear);
                }
                float kind = woods.GetNoise2D(spot.X * 0.6f + 500f, spot.Z * 0.6f) * 0.5f + 0.5f;
                int pick = rng.Randf() < 0.65f ? Mathf.Clamp((int)(kind * PlantScenes.Length), 0, PlantScenes.Length - 1) : rng.RandiRange(0, PlantScenes.Length - 1);
                Place(PlantScenes[pick], spot, 0.04f);
            }
        }

        // (Tumbleweeds move about: only where the battle is.)
        outer = false;
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
