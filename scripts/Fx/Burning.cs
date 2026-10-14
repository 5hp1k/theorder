using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// A fire left where a beam weapon destroyed something or a flamethrower played (scenes/fx/laser_fire.tscn):
/// burns for BurnTime, dies down over DieTime — the particles stop, the light and the crackle fade —
/// then frees itself. The same script puts out the fire of a wreck (wreck_fire.tscn) and the smoke of a
/// shot-away section (damage_smoke.tscn) in their time: nothing burns for ever.
/// </summary>
public partial class Burning : Node3D
{
	/// <summary>Seconds it burns, from catching to the last smoke.</summary>
	[Export] public float BurnTime = 120f;
	/// <summary>Of those, the seconds it takes to die down at the end.</summary>
	[Export] public float DieTime = 12f;

	/// <summary>What it does to the trees, cars and the like standing within <see cref="SpreadRadius"/>, per second: they burn down in their turn.</summary>
	[Export] public float SpreadDamage;
	[Export] public float SpreadRadius = 4.5f;

	/// <summary>Most fires burning at once; the oldest goes out when another catches.</summary>
	public const int MaxFires = 24;
	/// <summary>Most brush fires (the small ones without a light, lit by the dozen: scenes/fx/ground_fire.tscn), and how close two may be.</summary>
	public const int MaxBrushFires = 70;
	public const float BrushSpacing = 8f;
	/// <summary>No two fires closer than this: a beam swept along a wall lights a row, not a blaze per block.</summary>
	public const float Spacing = 3.5f;

	/// <summary>Set while a beam is dealing its damage, so whatever it destroys knows to catch fire.</summary>
	public static bool Heat;

	static PackedScene _scene;
	static readonly List<Burning> Fires = new();

	/// <summary>Sets fire to the spot, if a beam did the damage and nothing is burning there already.</summary>
	public static void Ignite(Node parent, Vector3 at, float size = 1f)
	{
		if (Heat) Kindle(parent, at, size);
	}

	/// <summary>Sets fire to the spot whatever did it (a flamethrower playing on the ground), if nothing is burning there already.</summary>
	public static void Kindle(Node parent, Vector3 at, float size = 1f)
	{
		if (parent == null) return;
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

	static PackedScene _brushScene;
	static readonly List<Burning> Brushes = new();

	/// <summary>
	/// A brush fire: a small one with no light of its own, for when a whole stretch of country is set
	/// alight at once (a nuclear blast). Refused, not swapped for an older one, once there are enough.
	/// </summary>
	public static void Brush(Node parent, Vector3 at, float size = 1f)
	{
		if (parent == null) return;
		var terrain = MapTerrain.Instance;
		if (terrain != null && terrain.HasWater && at.Y < terrain.WaterLevel + 0.05f) return;
		Brushes.RemoveAll(f => !IsInstanceValid(f));
		if (Brushes.Count >= MaxBrushFires) return;
		foreach (var f in Brushes)
			if (f.GlobalPosition.DistanceSquaredTo(at) < BrushSpacing * BrushSpacing) return;
		_brushScene ??= GD.Load<PackedScene>("res://scenes/fx/ground_fire.tscn");
		var fire = _brushScene.Instantiate<Burning>();
		// (Not all the same, and not all dying down in the same second.)
		fire.BurnTime *= (float)GD.RandRange(0.7, 1.3);
		fire.Transform = new Transform3D(Basis.Identity.Scaled(Vector3.One * Mathf.Clamp(size, 0.6f, 2.4f)), at);
		parent.AddChild(fire);
		Brushes.Add(fire);
	}

	public static void Reset()
	{
		Brushes.Clear();
		Fires.Clear();
		_dyingCount = 0;
	}

	/// <summary>Fires burning now.</summary>
	public static int Count => Fires.Count;
	static int _dyingCount;

	bool _dying, _counted;

	public override void _Ready()
	{
		GetTree().CreateTimer(Mathf.Max(0.1f, BurnTime - DieTime), false).Timeout += Die;
		_spreadIn = (float)GD.RandRange(1.0, 2.0);
	}

	const float SpreadEvery = 1.5f;
	float _spreadIn;

	/// <summary>While it burns well, whatever can burn next to it is being burnt.</summary>
	public override void _PhysicsProcess(double delta)
	{
		if (SpreadDamage <= 0f || _dying || !Net.Authority) return;
		_spreadIn -= (float)delta;
		if (_spreadIn > 0f) return;
		_spreadIn = SpreadEvery;
		var query = new PhysicsShapeQueryParameters3D
		{
			Shape = new SphereShape3D { Radius = SpreadRadius * Scale.X },
			Transform = new Transform3D(Basis.Identity, GlobalPosition + Vector3.Up),
			CollisionMask = Phys.LayerProps,
		};
		Heat = true;
		foreach (var result in GetWorld3D().DirectSpaceState.IntersectShape(query, 8))
			if (result["collider"].AsGodotObject() is IDamageable and (Destructible or PropPart) and IDamageable fuel)
				fuel.TakeDamage(SpreadDamage * SpreadEvery, GlobalPosition);
		Heat = false;
	}

	public override void _ExitTree()
	{
		Fires.Remove(this);
		Brushes.Remove(this);
		if (_counted) _dyingCount = Mathf.Max(0, _dyingCount - 1);
	}

	void Die()
	{
		if (_dying || !IsInsideTree()) return;
		_dying = true;
		// (Only the fires that count towards the limit are counted as going out.)
		_counted = Fires.Remove(this);
		if (_counted) _dyingCount++;
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
