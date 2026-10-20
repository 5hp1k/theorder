using Godot;

namespace MechSim;

/// <summary>
/// Stands its children on the map's ground as the map comes up: for things laid out by hand on rolling
/// terrain (whose height is known only to <see cref="MapTerrain"/>). Put the props under it at any height;
/// only their X and Z count. The terrain must come before this node in the scene.
/// </summary>
public partial class GroundSnap : Node3D
{
    /// <summary>Raised (or sunk, negative) this far off the ground.</summary>
    [Export] public float Offset;

    bool _done;

    public override void _EnterTree()
    {
        // (Before the children's _Ready, so that what they work out from their place is right.)
        if (_done || MapTerrain.Instance is not { } terrain) return;
        _done = true;
        foreach (Node child in GetChildren())
        {
            if (child is not Node3D node) continue;
            var at = Transform * node.Position;
            node.Position = new Vector3(node.Position.X, terrain.HeightAt(at.X, at.Z) + Offset - Position.Y, node.Position.Z);
        }
    }
}
