using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Marks that weapons leave on the world: craters and scorches on the ground, splashes and ripples on
/// water. The looks are in scenes/fx/crater.tscn and the map's splash scene.
/// </summary>
public static class Impacts
{
    /// <summary>Most craters kept at once; the oldest goes when a new one arrives.</summary>
    public const int MaxCraters = 140;

    static PackedScene _crater;
    static readonly Queue<Node3D> Craters = new();

    /// <summary>Leaves a crater if the hit landed on the ground.</summary>
    public static void Ground(Node parent, in Phys.Hit hit, float radius)
    {
        if (radius <= 0f || hit.Collider is not MapTerrain terrain) return;
        // Big blasts move earth as well as scorching it.
        terrain.AddCrater(hit.Position, radius, radius * 0.3f);
        // A hole that size needs no painted mark on top of it.
        if (radius >= terrain.CellSize * 0.75f) return;
        _crater ??= GD.Load<PackedScene>("res://scenes/fx/crater.tscn");
        var crater = _crater.Instantiate<Node3D>();
        // Lying on the slope it hit, turned at random so they don't all look the same.
        Basis basis = Geo.BasisFromUpForward(hit.Normal, Vector3.Forward.Rotated(Vector3.Up, GD.Randf() * Mathf.Tau));
        crater.Transform = new Transform3D(basis.Scaled(Vector3.One * radius), hit.Position);
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
    }
}
