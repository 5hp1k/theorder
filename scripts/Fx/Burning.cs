using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// A fire left where a beam weapon destroyed something (scenes/fx/laser_fire.tscn): burns for BurnTime,
/// dies down over DieTime — the particles stop, the light and the crackle fade — then frees itself.
/// </summary>
public partial class Burning : Node3D
{
	/// <summary>Seconds it burns, from catching to the last smoke.</summary>
	[Export] public float BurnTime = 120f;
	/// <summary>Of those, the seconds it takes to die down at the end.</summary>
	[Export] public float DieTime = 12f;

	/// <summary>Most fires burning at once; the oldest goes out when another catches.</summary>
	public const int MaxFires = 18;
	/// <summary>No two fires closer than this: a beam swept along a wall lights a row, not a blaze per block.</summary>
	public const float Spacing = 3.5f;

	/// <summary>Set while a beam is dealing its damage, so whatever it destroys knows to catch fire.</summary>
	public static bool Heat;

	static PackedScene _scene;
	static readonly List<Burning> Fires = new();

	/// <summary>Sets fire to the spot, if a beam did the damage and nothing is burning there already.</summary>
	public static void Ignite(Node parent, Vector3 at, float size = 1f)
	{
		if (!Heat || parent == null) return;
		var terrain = MapTerrain.Instance;
		if (terrain != null && terrain.HasWater && at.Y < terrain.WaterLevel) return;
		Fires.RemoveAll(f => !IsInstanceValid(f));
		foreach (var f in Fires)
			if (f.GlobalPosition.DistanceSquaredTo(at) < Spacing * Spacing) return;
		if (Fires.Count >= MaxFires)
		{
			// (When everything is set alight at once, the fires already going out aren't hurried out by yet more.)
			if (_dyingCount >= MaxFires) return;
			Fires[0].Die();
		}

		_scene ??= GD.Load<PackedScene>("res://scenes/fx/laser_fire.tscn");
		var fire = _scene.Instantiate<Burning>();
		fire.Transform = new Transform3D(Basis.Identity.Scaled(Vector3.One * Mathf.Clamp(size, 0.5f, 2f)), at);
		parent.AddChild(fire);
		Fires.Add(fire);
	}

	public static void Reset()
	{
		Fires.Clear();
		_dyingCount = 0;
	}

	/// <summary>Fires burning now.</summary>
	public static int Count => Fires.Count;
	static int _dyingCount;

	bool _dying;

	public override void _Ready()
	{
		GetTree().CreateTimer(Mathf.Max(0.1f, BurnTime - DieTime), false).Timeout += Die;
	}

	public override void _ExitTree()
	{
		Fires.Remove(this);
		if (_dying) _dyingCount = Mathf.Max(0, _dyingCount - 1);
	}

	void Die()
	{
		if (_dying || !IsInsideTree()) return;
		_dying = true;
		_dyingCount++;
		Fires.Remove(this);
		float longest = 0f;
		var tween = CreateTween().SetParallel();
		foreach (Node child in GetChildren())
		{
			switch (child)
			{
				case GpuParticles3D p:
					// Flames go first, the smoke thins after them.
					longest = Mathf.Max(longest, (float)p.Lifetime);
					tween.TweenProperty(p, "amount_ratio", 0f, DieTime * (p.Lifetime > 2.0 ? 1f : 0.6f));
					break;
				case OmniLight3D light:
					light.SetProcess(false);
					tween.TweenProperty(light, "light_energy", 0f, DieTime * 0.6f);
					break;
				case AudioStreamPlayer3D sound:
					tween.TweenProperty(sound, "volume_db", -60f, DieTime * 0.8f);
					break;
			}
		}
		tween.Chain().TweenInterval(longest);
		tween.Chain().TweenCallback(Callable.From(QueueFree));
	}
}
