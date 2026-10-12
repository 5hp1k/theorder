using Godot;

namespace MechSim;

/// <summary>
/// Lays out a city on flat ground: a grid of wide roads with the map centre on a crossroads, and each
/// block filled with towers, mid-rise houses or small houses (taller towards the centre), plus cars
/// parked along the roads. Everything it places is a prefab listed on this node.
/// </summary>
public partial class CityLayout : Node3D
{
    [Export] public int Seed = 2024;
    /// <summary>Blocks per side.</summary>
    [Export] public int Blocks = 6;
    [Export] public float BlockSize = 84f;
    [Export] public float RoadWidth = 26f;

    [ExportGroup("Prefabs")]
    /// <summary>One block-length of road running along Z (it's turned for the cross streets).</summary>
    [Export] public PackedScene RoadScene;
    [Export] public PackedScene[] Towers = System.Array.Empty<PackedScene>();
    [Export] public PackedScene[] Houses = System.Array.Empty<PackedScene>();
    [Export] public PackedScene[] SmallHouses = System.Array.Empty<PackedScene>();
    [Export] public PackedScene[] Cars = System.Array.Empty<PackedScene>();

    [ExportGroup("Density")]
    /// <summary>Chance of a tower block in the very centre; falls off towards the edge.</summary>
    [Export(PropertyHint.Range, "0,1")] public float TowerChance = 0.6f;
    /// <summary>Share of small-house plots left empty.</summary>
    [Export(PropertyHint.Range, "0,1")] public float EmptyPlots = 0.2f;
    /// <summary>Most cars along one block of road.</summary>
    [Export] public int CarsPerBlock = 3;

    RandomNumberGenerator _rng;

    public override void _Ready()
    {
        _rng = new RandomNumberGenerator { Seed = (ulong)Seed };
        float pitch = BlockSize + RoadWidth;
        int half = Blocks / 2;

        // Roads: lines at k * pitch, one prefab per block length. The two directions sit at slightly
        // different heights so the crossings don't flicker.
        for (int k = -half; k <= half; k++)
        for (int j = -half; j < half; j++)
        {
            float along = (j + 0.5f) * pitch;
            Place(RoadScene, new Vector3(k * pitch, 0.04f, along), 0f);
            Place(RoadScene, new Vector3(along, 0.07f, k * pitch), Mathf.Pi / 2f);
            ParkCars(new Vector3(k * pitch, 0f, along), false);
            ParkCars(new Vector3(along, 0f, k * pitch), true);
        }

        float reach = half * pitch;
        for (int bx = -half; bx < half; bx++)
        for (int bz = -half; bz < half; bz++)
        {
            var centre = new Vector3((bx + 0.5f) * pitch, 0f, (bz + 0.5f) * pitch);
            float edge = Mathf.Clamp(centre.Length() / reach, 0f, 1f);
            float roll = _rng.Randf();
            if (roll < TowerChance * (1f - edge) && Towers.Length > 0)
            {
                Place(Pick(Towers), centre, QuarterTurn());
            }
            else if (roll < 0.45f + 0.4f * (1f - edge) && Houses.Length > 0)
            {
                // Four mid-rise houses, one per quarter of the block.
                float q = BlockSize * 0.25f;
                foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    Place(Pick(Houses), centre + new Vector3(sx * q, 0f, sz * q), QuarterTurn());
            }
            else if (SmallHouses.Length > 0)
            {
                // A three-by-three of small houses with gaps.
                float q = BlockSize / 3f;
                for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    if (_rng.Randf() < EmptyPlots) continue;
                    Place(Pick(SmallHouses), centre + new Vector3(i * q, 0f, j * q), QuarterTurn());
                }
            }
        }
    }

    void ParkCars(Vector3 roadCentre, bool alongX)
    {
        if (Cars.Length == 0) return;
        int count = _rng.RandiRange(0, CarsPerBlock);
        for (int i = 0; i < count; i++)
        {
            float along = _rng.RandfRange(-BlockSize * 0.45f, BlockSize * 0.45f);
            // Kerb side, either direction.
            float side = (_rng.Randf() < 0.5f ? -1f : 1f) * (RoadWidth * 0.5f - 2.2f);
            Vector3 offset = alongX ? new Vector3(along, 0f, side) : new Vector3(side, 0f, along);
            float yaw = (alongX ? Mathf.Pi / 2f : 0f) + (_rng.Randf() < 0.5f ? Mathf.Pi : 0f);
            Place(Pick(Cars), roadCentre + offset + Vector3.Up * 0.1f, yaw);
        }
    }

    PackedScene Pick(PackedScene[] scenes) => scenes[_rng.RandiRange(0, scenes.Length - 1)];
    float QuarterTurn() => _rng.RandiRange(0, 3) * Mathf.Pi / 2f;

    void Place(PackedScene scene, Vector3 position, float yaw)
    {
        if (scene == null) return;
        var node = scene.Instantiate<Node3D>();
        // Placed before it enters the tree, so the prop knows where it stands when it wakes up.
        node.Transform = new Transform3D(new Basis(Vector3.Up, yaw), position);
        AddChild(node);
    }
}
