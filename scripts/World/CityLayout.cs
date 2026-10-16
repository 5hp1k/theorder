using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Lays a city out over the map's ground, hills, river and all. The streets themselves are painted by
/// the ground's shader (this node tells it where they run); this places what stands on the blocks —
/// towers, houses, parks, car parks, squares — and along the streets: parked cars, lamps, traffic
/// lights, signs, and a bridge wherever a street meets water. Everything is a prefab listed here.
/// </summary>
public partial class CityLayout : Node3D
{
    [Export] public int Seed = 2024;
    /// <summary>Blocks per side.</summary>
    [Export] public int Blocks = 6;
    [Export] public float BlockSize = 84f;
    [Export] public float RoadWidth = 26f;
    /// <summary>Width of the pavement beside a street.</summary>
    [Export] public float Pavement = 4f;

    [ExportGroup("Buildings")]
    [Export] public PackedScene[] Towers = System.Array.Empty<PackedScene>();
    [Export] public PackedScene[] Houses = System.Array.Empty<PackedScene>();
    [Export] public PackedScene[] SmallHouses = System.Array.Empty<PackedScene>();
    /// <summary>A deck of blocks; its length is set to span the water.</summary>
    [Export] public PackedScene BridgeScene;
    /// <summary>One block's length of parapet for a bridge (origin on the deck's edge, +Z pointing off the bridge).</summary>
    [Export] public PackedScene BridgeRailScene;
    /// <summary>A pier under a bridge (origin at its top).</summary>
    [Export] public PackedScene BridgePierScene;
    /// <summary>Water shallower than this at its deepest is forded, not bridged.</summary>
    [Export] public float BridgeMinDepth = 1.4f;
    /// <summary>…and so is water narrower than this.</summary>
    [Export] public float BridgeMinSpan = 10f;
    /// <summary>What shows under a building where the ground falls away from it.</summary>
    [Export] public Material FoundationMaterial;

    [ExportGroup("Streets")]
    [Export] public PackedScene[] Cars = System.Array.Empty<PackedScene>();
    /// <summary>Police, ambulances, fire engines: one in a dozen of the cars.</summary>
    [Export] public PackedScene[] ServiceCars = System.Array.Empty<PackedScene>();
    [Export] public PackedScene LampScene;
    [Export] public PackedScene TrafficLightScene;
    [Export] public PackedScene[] Signs = System.Array.Empty<PackedScene>();
    /// <summary>Hydrants, bins, benches: odd things on the pavement.</summary>
    [Export] public PackedScene[] Furniture = System.Array.Empty<PackedScene>();
    [Export] public PackedScene[] Trees = System.Array.Empty<PackedScene>();

    /// <summary>
    /// Blocks per side laid out in full: past them (and so past the edge of the battlefield) the town
    /// goes on for the look of it, with houses bare of fittings and streets without cars, lamps or signs.
    /// </summary>
    [Export] public int DetailBlocks = 12;
    /// <summary>How far past the battlefield's edge things are still set out in full, metres.</summary>
    [Export] public float DetailMargin = 40f;

    [ExportGroup("Density")]
    /// <summary>Chance of a tower block in the very centre; falls off towards the edge.</summary>
    [Export(PropertyHint.Range, "0,1")] public float TowerChance = 0.6f;
    /// <summary>Share of small-house plots left empty.</summary>
    [Export(PropertyHint.Range, "0,1")] public float EmptyPlots = 0.2f;
    /// <summary>Most cars along one block of road.</summary>
    [Export] public int CarsPerBlock = 3;
    [Export(PropertyHint.Range, "0,0.5")] public float ParkShare = 0.1f;
    [Export(PropertyHint.Range, "0,0.5")] public float CarParkShare = 0.07f;
    [Export(PropertyHint.Range, "0,0.5")] public float SquareShare = 0.04f;

    enum Block { Yard, CarPark, Square }

    RandomNumberGenerator _rng;
    MapTerrain _terrain;
    float _pitch, _reach, _detailReach, _detailEdge;

    public override void _Ready()
    {
        _rng = new RandomNumberGenerator { Seed = (ulong)Seed };
        _terrain = MapTerrain.Instance;
        _pitch = BlockSize + RoadWidth;
        int half = Blocks / 2;
        _reach = half * _pitch;
        _detailReach = Mathf.Max(1, DetailBlocks / 2) * _pitch;
        _detailEdge = (_terrain?.PlayHalf ?? _reach) + DetailMargin;

        var kinds = Image.CreateEmpty(half * 2, half * 2, false, Image.Format.R8);
        for (int bx = -half; bx < half; bx++)
        for (int bz = -half; bz < half; bz++)
        {
            Block kind = FillBlock(new Vector3((bx + 0.5f) * _pitch, 0f, (bz + 0.5f) * _pitch));
            kinds.SetPixel(bx + half, bz + half, new Color((int)kind * 0.5f, 0f, 0f));
        }

        for (int k = -half; k <= half; k++)
        {
            Bridges(k * _pitch, false);
            Bridges(k * _pitch, true);
            for (int j = -half; j < half; j++)
            {
                float along = (j + 0.5f) * _pitch;
                Street(new Vector3(k * _pitch, 0f, along), false);
                Street(new Vector3(along, 0f, k * _pitch), true);
            }
            for (int j = -half; j <= half; j++) Crossing(new Vector3(k * _pitch, 0f, j * _pitch));
        }

        // The ground paints the streets; it has to know where they are.
        if (_terrain?.GetNodeOrNull<MeshInstance3D>("Mesh")?.MaterialOverride is ShaderMaterial ground)
        {
            ground.SetShaderParameter("street_pitch", _pitch);
            ground.SetShaderParameter("street_width", RoadWidth);
            ground.SetShaderParameter("street_reach", _reach);
            ground.SetShaderParameter("walk_width", Pavement);
            ground.SetShaderParameter("block_map", ImageTexture.CreateFromImage(kinds));
        }
    }

    // ── The lie of the land ─────────────────────────────────────

    float Ground(float x, float z) => _terrain?.HeightAt(x, z) ?? 0f;

    /// <summary>Under water, or so close to it that nothing should be built there.</summary>
    bool Wet(float x, float z) => _terrain != null && _terrain.HasWater && Ground(x, z) < _terrain.WaterLevel + 0.5f;

    bool AnyWet(Vector3 centre, float halfX, float halfZ)
    {
        for (int i = -1; i <= 1; i++)
        for (int j = -1; j <= 1; j++)
            if (Wet(centre.X + i * halfX, centre.Z + j * halfZ)) return true;
        return false;
    }

    Vector3 Slope(float x, float z)
    {
        const float d = 1.5f;
        return new Vector3(Ground(x - d, z) - Ground(x + d, z), 2f * d, Ground(x, z - d) - Ground(x, z + d)).Normalized();
    }

    // ── Blocks ──────────────────────────────────────────────────

    Block FillBlock(Vector3 centre)
    {
        float yard = BlockSize * 0.5f - Pavement;
        // Water in the block: it's a park round a pond, whatever else it might have been.
        if (AnyWet(centre, yard, yard))
        {
            Park(centre, yard);
            return Block.Yard;
        }

        float edge = Mathf.Clamp(centre.Length() / _detailReach, 0f, 1f);
        float roll = _rng.Randf();
        if (roll < ParkShare)
        {
            Park(centre, yard);
            return Block.Yard;
        }
        roll -= ParkShare;
        if (roll < CarParkShare)
        {
            CarPark(centre, yard);
            return Block.CarPark;
        }
        roll -= CarParkShare;
        if (roll < SquareShare)
        {
            Square(centre, yard);
            return Block.Square;
        }
        roll = _rng.Randf();

        if (roll < TowerChance * (1f - edge) && Towers.Length > 0)
        {
            Building(Pick(Towers), centre, QuarterTurn());
        }
        else if (roll < 0.45f + 0.4f * (1f - edge) && Houses.Length > 0)
        {
            // Four mid-rise houses, one per quarter of the block.
            float q = BlockSize * 0.25f;
            foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                Building(Pick(Houses), centre + new Vector3(sx * q, 0f, sz * q), QuarterTurn());
        }
        else if (SmallHouses.Length > 0)
        {
            // A three-by-three of small houses with gaps, and a tree in some of the gaps.
            float q = BlockSize / 3f;
            for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
            {
                Vector3 at = centre + new Vector3(i * q, 0f, j * q);
                if (_rng.Randf() < EmptyPlots)
                {
                    if (Trees.Length > 0 && _rng.Randf() < 0.7f) Stand(Pick(Trees), at, _rng.Randf() * Mathf.Tau);
                    continue;
                }
                Building(Pick(SmallHouses), at, QuarterTurn());
            }
        }
        return Block.Yard;
    }

    void Building(PackedScene scene, Vector3 at, float yaw)
    {
        var node = scene.Instantiate<Node3D>();
        float halfX = 4f, halfZ = 4f;
        if (node is ChunkedBuilding b)
        {
            bool turned = Mathf.Abs(Mathf.Sin(yaw)) > 0.7f;
            halfX = (turned ? b.Cells.Z : b.Cells.X) * b.CellSize * 0.5f;
            halfZ = (turned ? b.Cells.X : b.Cells.Z) * b.CellSize * 0.5f;
        }
        if (AnyWet(at, halfX + 2f, halfZ + 2f))
        {
            node.Free();
            return;
        }
        // It stands on the highest ground under it; a foundation fills in below where the ground drops away.
        float high = float.MinValue, low = float.MaxValue;
        for (int i = -1; i <= 1; i++)
        for (int j = -1; j <= 1; j++)
        {
            float h = Ground(at.X + i * halfX, at.Z + j * halfZ);
            high = Mathf.Max(high, h);
            low = Mathf.Min(low, h);
        }
        node.Transform = new Transform3D(new Basis(Vector3.Up, yaw), new Vector3(at.X, high + 0.05f, at.Z));
        if (Lite(at) && node is ChunkedBuilding bare)
        {
            bare.Doors = 0;
            bare.WallFittings = System.Array.Empty<PackedScene>();
            bare.RoofFittings = System.Array.Empty<PackedScene>();
        }
        float drop = high - low + 1f;
        bool quarter = Mathf.Abs(Mathf.Sin(yaw)) > 0.7f;
        node.AddChild(new MeshInstance3D
        {
            Name = "Foundation",
            Mesh = new BoxMesh { Size = new Vector3((quarter ? halfZ : halfX) * 2f + 0.5f, drop, (quarter ? halfX : halfZ) * 2f + 0.5f) },
            Position = new Vector3(0f, -drop * 0.5f + 0.02f, 0f),
            MaterialOverride = FoundationMaterial,
        });
        AddChild(node);
    }

    void Park(Vector3 centre, float yard)
    {
        int trees = Trees.Length > 0 ? _rng.RandiRange(12, 20) : 0;
        for (int i = 0; i < trees; i++)
        {
            var at = centre + new Vector3(_rng.RandfRange(-yard, yard), 0f, _rng.RandfRange(-yard, yard));
            if (!Wet(at.X, at.Z)) Stand(Pick(Trees), at, _rng.Randf() * Mathf.Tau);
        }
        // Benches and lamps round the edge.
        for (int i = 0; i < 6 && Furniture.Length > 0; i++)
        {
            float t = _rng.RandfRange(-yard * 0.8f, yard * 0.8f);
            bool onX = _rng.Randf() < 0.5f;
            float side = _rng.Randf() < 0.5f ? -1f : 1f;
            var at = centre + (onX ? new Vector3(t, 0f, side * (yard - 2f)) : new Vector3(side * (yard - 2f), 0f, t));
            if (!Wet(at.X, at.Z)) Stand(Pick(Furniture), at, onX ? (side > 0f ? Mathf.Pi : 0f) : side * -Mathf.Pi * 0.5f);
        }
    }

    void CarPark(Vector3 centre, float yard)
    {
        if (Cars.Length == 0) return;
        // Stalls 2.7 wide in bands along X, as the ground shader draws them: two bands to every 16 m.
        const float stall = 2.7f, band = 16f;
        float edge = yard;
        for (float row = -edge; row + band <= edge + 0.1f; row += band)
        for (float x = -edge + stall * 0.5f; x < edge - 1.5f; x += stall)
        foreach (float z in new[] { row + 2.5f, row + 13.5f })
        {
            if (_rng.Randf() > 0.34f) continue;
            Stand(PickCar(), centre + new Vector3(x, 0f, z), _rng.Randf() < 0.5f ? 0f : Mathf.Pi, 0.06f, true);
        }
        for (int i = 0; i < 4 && LampScene != null; i++)
            Stand(LampScene, centre + new Vector3((i % 2 * 2 - 1) * edge * 0.5f, 0f, (i / 2 * 2 - 1) * edge * 0.5f), _rng.Randf() * Mathf.Tau);
    }

    void Square(Vector3 centre, float yard)
    {
        for (int i = 0; i < 8 && Furniture.Length > 0; i++)
        {
            float a = i * Mathf.Tau / 8f;
            Stand(Pick(Furniture), centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * yard * 0.55f, -a + Mathf.Pi * 0.5f);
        }
        for (int i = 0; i < 4 && Trees.Length > 0; i++)
            Stand(Pick(Trees), centre + new Vector3((i % 2 * 2 - 1) * yard * 0.75f, 0f, (i / 2 * 2 - 1) * yard * 0.75f), _rng.Randf() * Mathf.Tau);
        for (int i = 0; i < 4 && LampScene != null; i++)
        {
            float a = i * Mathf.Tau / 4f + 0.4f;
            Stand(LampScene, centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * yard * 0.3f, -a);
        }
    }

    // ── Streets ─────────────────────────────────────────────────

    /// <summary>One block's length of street: cars at the kerb, lamps, the odd sign and hydrant.</summary>
    void Street(Vector3 centre, bool alongX)
    {
        Vector3 Along(float along, float across) => centre + (alongX ? new Vector3(along, 0f, across) : new Vector3(across, 0f, along));
        float yaw = alongX ? Mathf.Pi / 2f : 0f;
        float kerb = RoadWidth * 0.5f;

        int cars = Cars.Length > 0 ? _rng.RandiRange(0, CarsPerBlock) : 0;
        for (int i = 0; i < cars; i++)
        {
            float side = _rng.Randf() < 0.5f ? -1f : 1f;
            Vector3 at = Along(_rng.RandfRange(-BlockSize * 0.42f, BlockSize * 0.42f), side * (kerb - 2.2f));
            Stand(PickCar(), at, yaw + (_rng.Randf() < 0.5f ? Mathf.Pi : 0f), 0.06f, true);
        }

        // Lamps: one each side, staggered, arm over the road.
        if (LampScene != null)
        {
            Stand(LampScene, Along(-BlockSize * 0.22f, kerb + 1f), yaw + Mathf.Pi * 0.5f * (alongX ? -1f : 1f));
            Stand(LampScene, Along(BlockSize * 0.22f, -kerb - 1f), yaw - Mathf.Pi * 0.5f * (alongX ? -1f : 1f));
        }
        if (Signs.Length > 0 && _rng.Randf() < 0.45f)
        {
            float side = _rng.Randf() < 0.5f ? -1f : 1f;
            Stand(Pick(Signs), Along(side * BlockSize * 0.36f, side * (kerb + 1.1f)), yaw + (side > 0f ? 0f : Mathf.Pi));
        }
        if (Furniture.Length > 0 && _rng.Randf() < 0.6f)
        {
            float side = _rng.Randf() < 0.5f ? -1f : 1f;
            Stand(Pick(Furniture), Along(_rng.RandfRange(-BlockSize * 0.3f, BlockSize * 0.3f), side * (kerb + 2.4f)), yaw + side * Mathf.Pi * 0.5f * (alongX ? 1f : -1f));
        }
    }

    /// <summary>Traffic lights on two opposite corners of a crossroads.</summary>
    void Crossing(Vector3 centre)
    {
        if (TrafficLightScene == null) return;
        float corner = RoadWidth * 0.5f + 0.9f;
        Stand(TrafficLightScene, centre + new Vector3(corner, 0f, corner), Mathf.Pi);
        Stand(TrafficLightScene, centre + new Vector3(-corner, 0f, -corner), 0f);
    }

    /// <summary>
    /// Walks one street from end to end and throws a bridge over every stretch of water too deep to ford
    /// (a puddle in a dip is left as a puddle). The deck runs bank to bank, sloping if they differ.
    /// </summary>
    void Bridges(float line, bool alongX)
    {
        if (BridgeScene == null || _terrain == null || !_terrain.HasWater) return;
        const float step = 2f;
        float At(float s) => alongX ? Ground(s, line) : Ground(line, s);
        float start = float.NaN, deepest = 0f;
        for (float s = -_reach; s <= _reach + step; s += step)
        {
            float depth = s <= _reach ? _terrain.WaterLevel - At(s) : -1f;
            bool wet = depth > 0f;
            if (wet)
            {
                if (float.IsNaN(start)) { start = s; deepest = 0f; }
                deepest = Mathf.Max(deepest, depth);
                continue;
            }
            if (float.IsNaN(start)) continue;

            float from = start, to = s;
            start = float.NaN;
            if (deepest < BridgeMinDepth || to - from < BridgeMinSpan) continue;

            // Abutments a few metres back on dry ground each side.
            float a = Mathf.Max(from - 6f, -_reach), b = Mathf.Min(to + 4f, _reach);
            var bridge = BridgeScene.Instantiate<ChunkedBuilding>();
            int across = Mathf.Max(1, Mathf.RoundToInt(RoadWidth / bridge.CellSize));
            int length = Mathf.Max(2, Mathf.CeilToInt((b - a) / bridge.CellSize));
            bridge.Cells = new Vector3I(across, 1, length);
            var size = new Vector3(across, 1, length) * bridge.CellSize;
            b = a + size.Z;
            if (bridge.GetNodeOrNull<CollisionShape3D>("Collision") is { } shape)
            {
                shape.Shape = new BoxShape3D { Size = size };
                shape.Position = new Vector3(0f, size.Y * 0.5f, 0f);
            }
            // The roadway meets the ground at both ends, so wheels and feet go straight on.
            float ha = At(a) + 0.03f, hb = At(b) + 0.03f;
            var basis = new Basis(Vector3.Up, alongX ? Mathf.Pi / 2f : 0f) * new Basis(Vector3.Right, -Mathf.Atan2(hb - ha, size.Z));
            float mid = (a + b) * 0.5f;
            Vector3 top = alongX ? new Vector3(mid, (ha + hb) * 0.5f, line) : new Vector3(line, (ha + hb) * 0.5f, mid);
            bridge.Transform = new Transform3D(basis, top - basis.Y * size.Y);
            AddChild(bridge);

            // Parapets where there's a drop beside the deck (never across a side street),
            // a pair of piers every few blocks: each fixed to its block of the deck.
            float half = bridge.CellSize * 0.5f;
            for (int z = 0; z < length; z++)
            foreach (int x in new[] { 0, across - 1 })
            {
                float side = x == 0 ? -1f : 1f;
                Vector3 c = bridge.BlockCentre(x, 0, z);
                Vector3 edge = bridge.Transform * (c + new Vector3(side * (half + 1.5f), half, 0f));
                float along = alongX ? edge.X : edge.Z;
                bool sideStreet = Mathf.Abs(along - Mathf.Round(along / _pitch) * _pitch) < RoadWidth * 0.5f + Pavement;
                float drop = edge.Y - Ground(edge.X, edge.Z);
                if (BridgeRailScene != null && !sideStreet && drop > 0.8f)
                {
                    var rail = BridgeRailScene.Instantiate<Node3D>();
                    rail.Transform = new Transform3D(new Basis(Vector3.Up, side * Mathf.Pi / 2f), c + new Vector3(side * half, half, 0f));
                    bridge.Attach(rail, x, 0, z);
                }
                if (BridgePierScene != null && z % 6 == 3 && drop > 1.5f)
                {
                    var pier = BridgePierScene.Instantiate<Node3D>();
                    pier.Position = c + new Vector3(-side * half, -half, 0f);
                    bridge.Attach(pier, x, 0, z);
                    bridge.Anchor(x, 0, z);
                }
            }
        }
    }

    // ── Placing ─────────────────────────────────────────────────

    /// <summary>Whether this spot is far enough past the battlefield's edge to be set out only for the look of it.</summary>
    bool Lite(Vector3 at) => Mathf.Max(Mathf.Abs(at.X), Mathf.Abs(at.Z)) > _detailEdge;

    PackedScene Pick(PackedScene[] scenes) => scenes[_rng.RandiRange(0, scenes.Length - 1)];
    PackedScene PickCar() => ServiceCars.Length > 0 && _rng.Randf() < 0.08f ? Pick(ServiceCars) : Pick(Cars);
    float QuarterTurn() => _rng.RandiRange(0, 3) * Mathf.Pi / 2f;

    /// <summary>Stands a prop on the ground (not in water); with tilt, leaning with the slope like a parked car.</summary>
    void Stand(PackedScene scene, Vector3 at, float yaw, float lift = 0f, bool tilt = false)
    {
        if (scene == null || Wet(at.X, at.Z)) return;
        // Beyond the battlefield only the trees.
        if (Lite(at) && System.Array.IndexOf(Trees, scene) < 0) return;
        var node = scene.Instantiate<Node3D>();
        var basis = new Basis(Vector3.Up, yaw);
        if (tilt) basis = Geo.BasisFromUpForward(Slope(at.X, at.Z), -basis.Z);
        // Placed before it enters the tree, so the prop knows where it stands when it wakes up.
        node.Transform = new Transform3D(basis, new Vector3(at.X, Ground(at.X, at.Z) + lift, at.Z));
        Lod.Apply(node);
        AddChild(node);
    }
}
