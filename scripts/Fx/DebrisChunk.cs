using Godot;

namespace MechSim;

/// <summary>
/// One loose piece of wreckage (see <see cref="Debris"/>): a rigid block that tumbles, lies about for a
/// while, then shrinks away. The scene holds a unit box mesh and shape; Setup sizes and colours them.
/// </summary>
public partial class DebrisChunk : RigidBody3D
{
    [Export] public float Lifetime = 9f;
    /// <summary>kg per cubic metre, so big chunks fall like big chunks.</summary>
    [Export] public float Density = 600f;

    public void Setup(Vector3 size, Material material)
    {
        var mesh = GetNode<MeshInstance3D>("Mesh");
        mesh.Scale = size;
        mesh.MaterialOverride = material;
        GetNode<CollisionShape3D>("Shape").Shape = new BoxShape3D { Size = size };
        Mass = Mathf.Max(5f, size.X * size.Y * size.Z * Density);
    }

    public override void _Ready()
    {
        var tween = CreateTween();
        tween.TweenInterval(Lifetime * (float)GD.RandRange(0.7, 1.0));
        tween.TweenProperty(this, "scale", Vector3.One * 0.01f, 0.6f);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
