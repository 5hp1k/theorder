using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Dirt that a machine picks up in battle, part by part: the feet, legs, wheels and tracks soonest,
/// all of it faster on the move, and in a burst near an explosion. A part that goes under water is
/// washed clean (as much of it as was under) and comes out wet, then dries. Repairs clean it all.
/// The look is the armour shader's (grime_level, wetness instance parameters); how fast is set by the
/// numbers below, the same for every machine.
/// </summary>
public sealed class Grime
{
    /// <summary>Dirt gathered per second standing, and per second at full speed, near the ground.</summary>
    public static float Settle = 0.0015f, Kick = 0.012f;
    /// <summary>Up to this height above the ground a part counts as near it, metres.</summary>
    public static float SplashHeight = 2.5f;
    /// <summary>Dirt washed off per second when wholly under water; how fast a wet part dries.</summary>
    public static float Wash = 0.6f, Dry = 0.05f;

    static readonly List<Grime> All = new();

    sealed class Part
    {
        public MeshInstance3D Mesh;
        public float Dirt, Wet, ShownDirt = -1f, ShownWet = -1f;
    }

    readonly List<Part> _parts = new();
    readonly Node3D _unit;
    float _clock;
    int _next;

    public Grime(Node3D unit)
    {
        _unit = unit;
        foreach (var mesh in Geo.Collect<MeshInstance3D>(unit))
            if (mesh.MaterialOverride is ShaderMaterial { Shader: { } shader } && shader.ResourcePath.EndsWith("armor.gdshader") && !mesh.Name.ToString().StartsWith("Insignia"))
                _parts.Add(new Part { Mesh = mesh });
        All.Add(this);
    }

    public void Forget() => All.Remove(this);

    /// <summary>Called every tick; looks at a few parts each time.</summary>
    public void Update(float dt, float speedShare)
    {
        _clock += dt;
        if (_parts.Count == 0 || _clock < 0.05f) return;
        float step = _clock;
        _clock = 0f;
        var terrain = MapTerrain.Instance;
        // A quarter of the parts each time round: every part is seen about five times a second.
        int count = Mathf.Max(1, _parts.Count / 4);
        for (int k = 0; k < count; k++)
        {
            var part = _parts[_next = (_next + 1) % _parts.Count];
            if (!GodotObject.IsInstanceValid(part.Mesh) || part.Mesh.Mesh == null || !part.Mesh.IsInsideTree()) continue;
            float span = step * 4f;
            Aabb box = part.Mesh.GlobalTransform * part.Mesh.Mesh.GetAabb();
            Vector3 middle = box.GetCenter();
            float ground = terrain?.HeightAt(middle.X, middle.Z) ?? 0f;
            float low = 1f - Mathf.Clamp((box.Position.Y - ground) / SplashHeight, 0f, 1f);
            part.Dirt += span * (Settle + Kick * speedShare * (0.25f + 0.75f * low));
            float under = 0f;
            if (terrain is { HasWater: true } && box.Position.Y < terrain.WaterLevel && terrain.WaterDepthAt(middle.X, middle.Z) > 0.05f)
                under = Mathf.Clamp((terrain.WaterLevel - box.Position.Y) / Mathf.Max(box.Size.Y, 0.2f), 0f, 1f);
            if (under > 0f)
            {
                part.Dirt -= span * Wash * under;
                part.Wet = Mathf.Max(part.Wet, under);
            }
            else part.Wet -= span * Dry;
            Show(part);
        }
    }

    void Show(Part part)
    {
        part.Dirt = Mathf.Clamp(part.Dirt, 0f, 1f);
        part.Wet = Mathf.Clamp(part.Wet, 0f, 1f);
        if (Mathf.Abs(part.Dirt - part.ShownDirt) > 0.01f)
        {
            part.ShownDirt = part.Dirt;
            part.Mesh.SetInstanceShaderParameter("grime_level", part.Dirt);
        }
        if (Mathf.Abs(part.Wet - part.ShownWet) > 0.01f)
        {
            part.ShownWet = part.Wet;
            part.Mesh.SetInstanceShaderParameter("wetness", part.Wet);
        }
    }

    /// <summary>A blast threw up earth here (and this far about): for whatever else gets dirty, such as cockpit glass.</summary>
    public static event System.Action<Vector3, float> Splashed;

    /// <summary>Spattered: everything this close to a blast takes some of the earth it threw up, the near side most.</summary>
    public static void Splash(Vector3 at, float radius)
    {
        Splashed?.Invoke(at, radius);
        float reach = radius * 3f + 4f;
        foreach (var grime in All)
        {
            if (!GodotObject.IsInstanceValid(grime._unit) || grime._unit.GlobalPosition.DistanceSquaredTo(at) > (reach + 15f) * (reach + 15f)) continue;
            foreach (var part in grime._parts)
            {
                if (!GodotObject.IsInstanceValid(part.Mesh)) continue;
                float d = part.Mesh.GlobalPosition.DistanceTo(at);
                if (d > reach) continue;
                part.Dirt += 0.25f * (1f - d / reach);
                grime.Show(part);
            }
        }
    }

    /// <summary>Washed down in the repair bay.</summary>
    public void Clean()
    {
        foreach (var part in _parts)
        {
            part.Dirt = part.Wet = 0f;
            if (GodotObject.IsInstanceValid(part.Mesh)) Show(part);
        }
    }

    public static void Reset() => All.Clear();
}
