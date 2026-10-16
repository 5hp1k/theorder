using Godot;

namespace MechSim;

/// <summary>
/// One loose piece of wreckage (see <see cref="Debris"/>): a rigid block that tumbles, lies about for a
/// while, then shrinks away. The scene holds a unit mesh and shape; Setup sizes and colours them and
/// swaps the mesh for one of a few irregular lumps, so no two pieces look like the same brick.
/// </summary>
public partial class DebrisChunk : RigidBody3D
{
    [Export] public float Lifetime = 9f;
    /// <summary>kg per cubic metre, so big chunks fall like big chunks.</summary>
    [Export] public float Density = 600f;

    /// <summary>Seconds it takes to shrink away at the end.</summary>
    const float ShrinkTime = 0.6f;

    MeshInstance3D _mesh;
    BoxShape3D _shape;
    Vector3 _size;
    float _left;

    /// <summary>Lying about no more: it can be thrown out again (see <see cref="Debris"/>).</summary>
    public bool Spent { get; private set; }

    public void Setup(Vector3 size, Material material)
    {
        _mesh ??= GetNode<MeshInstance3D>("Mesh");
        if (_shape == null)
        {
            // Its own shape, resized for every throw (the scene's is shared by all of them).
            _shape = new BoxShape3D();
            GetNode<CollisionShape3D>("Shape").Shape = _shape;
        }
        _size = size;
        _mesh.Mesh = Debris.Lump();
        _mesh.Scale = size;
        _mesh.MaterialOverride = material;
        _shape.Size = size;
        Mass = Mathf.Max(5f, size.X * size.Y * size.Z * Density);
        _left = Lifetime * (float)GD.RandRange(0.7, 1.0);
        Spent = false;
        Visible = true;
        Freeze = false;
        CollisionLayer = Phys.LayerWreck;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        _left -= (float)delta;
        if (_left > 0f) return;
        // Shrinks away: the picture only. (Scaling the body itself would make the physics engine
        // rebuild its shape every frame.)
        float k = Mathf.Clamp(1f + _left / ShrinkTime, 0f, 1f);
        _mesh.Scale = _size * Mathf.Max(k, 0.01f);
        if (k > 0f) return;
        // Put away, to be thrown again.
        Spent = true;
        Visible = false;
        Freeze = true;
        CollisionLayer = 0;
        SetProcess(false);
    }
}
