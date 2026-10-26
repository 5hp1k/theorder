using Godot;

namespace MechSim;

/// <summary>
/// A static body whose collision is the very shape of its meshes: on start every MeshInstance3D child
/// (but those named in <see cref="Skip"/>) gets a trimesh collision shape made from its mesh. For big
/// hand-modelled structures (the colosseum), where boxes would not follow benches and arches.
/// </summary>
public partial class MeshCollision : StaticBody3D
{
    /// <summary>Meshes that need no collision (fine decoration nothing should stand on).</summary>
    [Export] public string[] Skip = System.Array.Empty<string>();

    public override void _Ready()
    {
        foreach (Node child in GetChildren())
        {
            if (child is not MeshInstance3D { Mesh: { } mesh } part || System.Array.IndexOf(Skip, part.Name.ToString()) >= 0) continue;
            AddChild(new CollisionShape3D { Name = part.Name + "Shape", Shape = mesh.CreateTrimeshShape(), Transform = part.Transform });
        }
    }
}
