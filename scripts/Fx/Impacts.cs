using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>What made the mark: each has a prefab of its own, scenes/fx/mark_*.tscn.</summary>
public enum Mark
{
    /// <summary>Bullets and blades: a pock with chips round it.</summary>
    Bullet,
    /// <summary>Shells, missiles, bombs: char, streaks of soot, thrown earth.</summary>
    Blast,
    /// <summary>Beams: a glowing burn.</summary>
    Burn,
}

/// <summary>
/// Marks that weapons leave on the world: craters and scorches on the ground, splashes and ripples on
/// water. The looks are in scenes/fx/mark_*.tscn and the map's splash scene.
/// </summary>
public static class Impacts
{
    /// <summary>Most marks kept at once; the oldest goes when a new one arrives.</summary>
    public const int MaxCraters = 140;

    static readonly PackedScene[] Scenes = new PackedScene[3];
    static readonly Queue<Node3D> Craters = new();

    /// <summary>Leaves a mark if the hit landed on the ground — and a hole, if it was big enough.</summary>
    public static void Ground(Node parent, in Phys.Hit hit, float radius, Mark kind = Mark.Blast)
    {
        if (radius <= 0f || hit.Collider is not MapTerrain terrain) return;
        // Big blasts move earth as well as scorching it.
        terrain.AddCrater(hit.Position, radius, radius * 0.3f);
        // Nothing is painted on a bed under water: the mark would show through the surface.
        if (terrain.WaterDepthAt(hit.Position.X, hit.Position.Z) > 0.02f) return;
        Scenes[(int)kind] ??= GD.Load<PackedScene>(kind switch
        {
            Mark.Bullet => "res://scenes/fx/mark_bullet.tscn",
            Mark.Burn => "res://scenes/fx/mark_burn.tscn",
            _ => "res://scenes/fx/mark_blast.tscn",
        });
        var crater = Scenes[(int)kind].Instantiate<Node3D>();
        // Flat on the map (it drapes itself over the ground), turned at random so no two look the same.
        var basis = new Basis(Vector3.Up, GD.Randf() * Mathf.Tau).Scaled(Vector3.One * radius * (float)GD.RandRange(0.85, 1.15));
        crater.Transform = new Transform3D(basis, hit.Position);
        parent.AddChild(crater);

        Craters.Enqueue(crater);
        while (Craters.Count > MaxCraters)
        {
            var old = Craters.Dequeue();
            if (GodotObject.IsInstanceValid(old)) old.QueueFree();
        }
    }

    /// <summary>
    /// A shot from one point to another: if it went into water on the way, throws up a splash and sends
    /// out ripples where it broke the surface.
    /// </summary>
    public static void Water(Node parent, Vector3 from, Vector3 to, float strength)
    {
        var terrain = MapTerrain.Instance;
        if (terrain == null || !terrain.HasWater) return;
        float level = terrain.WaterLevel;
        if (from.Y <= level || to.Y >= level) return;
        Vector3 at = from.Lerp(to, (from.Y - level) / (from.Y - to.Y));
        if (terrain.WaterDepthAt(at.X, at.Z) <= 0.05f) return;
        Splash(parent, at, strength);
    }

    /// <summary>Splash and ripples at a point on the water.</summary>
    public static void Splash(Node parent, Vector3 at, float strength)
    {
        var terrain = MapTerrain.Instance;
        if (terrain == null) return;
        at.Y = terrain.WaterLevel;
        WaterSurface.AddRipple(at, strength);
        if (terrain.SplashScene == null) return;
        var splash = terrain.SplashScene.Instantiate<Node3D>();
        splash.Transform = new Transform3D(Basis.Identity.Scaled(Vector3.One * Mathf.Clamp(strength, 0.4f, 3f)), at);
        parent.AddChild(splash);
    }

    /// <summary>Forget everything (a new battle).</summary>
    public static void Reset()
    {
        Craters.Clear();
        Debris.Reset();
        Burning.Reset();
    }
}
