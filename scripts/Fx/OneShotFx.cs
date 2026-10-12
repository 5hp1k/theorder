using Godot;

namespace MechSim;

/// <summary>
/// A burst effect (explosion, impact): everything in the scene plays once, then the node frees itself.
/// An OmniLight3D child named "Flash" is faded out over FlashTime.
/// </summary>
public partial class OneShotFx : Node3D
{
    [Export] public float Lifetime = 4f;
    [Export] public float FlashTime = 0.35f;

    public override void _Ready()
    {
        var flash = GetNodeOrNull<OmniLight3D>("Flash");
        if (flash != null)
            CreateTween().TweenProperty(flash, "light_energy", 0f, FlashTime);
        GetTree().CreateTimer(Lifetime, false).Timeout += QueueFree;
    }
}
