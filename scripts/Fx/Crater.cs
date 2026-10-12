using Godot;

namespace MechSim;

/// <summary>Scorch mark or crater on the ground: stays for a while, then fades out and frees itself.</summary>
public partial class Crater : Node3D
{
    [Export] public float Lifetime = 90f;
    [Export] public float FadeTime = 10f;

    public override void _Ready()
    {
        var tween = CreateTween();
        tween.TweenInterval(Mathf.Max(0.1f, Lifetime - FadeTime));
        tween.SetParallel();
        foreach (Node child in GetChildren())
            if (child is GeometryInstance3D g) tween.TweenProperty(g, "transparency", 1f, FadeTime);
        tween.SetParallel(false);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
