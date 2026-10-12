using Godot;

namespace MechSim;

/// <summary>Foot mark left on the ground: stays for a while, then fades out and frees itself.</summary>
public partial class Footprint : Node3D
{
	[Export] public float Lifetime = 30f;
	[Export] public float FadeTime = 6f;

	public override void _Ready()
	{
		// A tween instead of per-frame script work: there can be hundreds of these.
		var tween = CreateTween();
		tween.TweenInterval(Mathf.Max(0.1f, Lifetime - FadeTime));
		tween.SetParallel();
		foreach (Node child in GetChildren())
			if (child is GeometryInstance3D g) tween.TweenProperty(g, "transparency", 1f, FadeTime);
		tween.SetParallel(false);
		tween.TweenCallback(Callable.From(QueueFree));
	}
}
