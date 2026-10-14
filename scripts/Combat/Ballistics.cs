using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Gunnery for shells that fly on an arc (a howitzer): how to lay the gun to drop a shell on a point,
/// and the path the shell then takes. The gun's fire control, the shell itself and the arc shown to
/// the player all use this, so the shell comes down where the arc says.
/// </summary>
public static class Ballistics
{
    /// <summary>
    /// Velocity to launch at so the shell lands on the target: the flat arc if the ground leaves it
    /// clear, otherwise the high one, over the hill. Out of reach, the gun is laid for its longest
    /// throw towards the target and the shell falls short.
    /// </summary>
    public static Vector3 Lay(Vector3 from, Vector3 target, float speed, float gravity, out bool reachable)
    {
        Vector3 flat = target - from;
        float rise = flat.Y;
        flat.Y = 0f;
        float dist = flat.Length();
        Vector3 dir = dist > 1e-3f ? flat / dist : Vector3.Forward;
        float v2 = speed * speed;
        float disc = v2 * v2 - gravity * (gravity * dist * dist + 2f * rise * v2);
        reachable = disc >= 0f && dist > 1f;
        if (!reachable) return Aim(dir, dist > 1f ? Mathf.Pi / 4f : 0f, speed);

        float root = Mathf.Sqrt(disc);
        Vector3 low = Aim(dir, Mathf.Atan2(v2 - root, gravity * dist), speed);
        if (Clear(from, low, gravity, dist)) return low;
        Vector3 high = Aim(dir, Mathf.Atan2(v2 + root, gravity * dist), speed);
        return Clear(from, high, gravity, dist) ? high : low;
    }

    static Vector3 Aim(Vector3 dir, float elevation, float speed)
        => (dir * Mathf.Cos(elevation) + Vector3.Up * Mathf.Sin(elevation)) * speed;

    /// <summary>Whether the shell gets most of the way to the target without flying into the ground.</summary>
    static bool Clear(Vector3 from, Vector3 velocity, float gravity, float dist)
    {
        var terrain = MapTerrain.Instance;
        if (terrain == null) return true;
        float along = new Vector2(velocity.X, velocity.Z).Length();
        if (along < 1e-3f) return true;
        float time = dist / along;
        for (int i = 1; i <= 10; i++)
        {
            float t = time * 0.88f * i / 10f;
            Vector3 p = At(from, velocity, gravity, t);
            if (p.Y < terrain.HeightAt(p.X, p.Z) - 0.5f) return false;
        }
        return true;
    }

    public static Vector3 At(Vector3 from, Vector3 velocity, float gravity, float t)
        => from + velocity * t + Vector3.Down * (0.5f * gravity * t * t);

    /// <summary>
    /// The shell's path from the muzzle until it meets the ground or the water, as points a fixed time
    /// apart; the last one is where it lands.
    /// </summary>
    public static void Path(Vector3 from, Vector3 velocity, float gravity, List<Vector3> points, float step = 0.12f, float longest = 30f)
    {
        points.Clear();
        points.Add(from);
        var terrain = MapTerrain.Instance;
        Vector3 last = from;
        for (float t = step; t < longest; t += step)
        {
            Vector3 p = At(from, velocity, gravity, t);
            float floor = terrain == null ? 0f : terrain.HeightAt(p.X, p.Z);
            if (terrain is { HasWater: true }) floor = Mathf.Max(floor, terrain.WaterLevel);
            if (p.Y <= floor)
            {
                // Where between the last two points it went in.
                float lastFloor = terrain == null ? 0f : terrain.HeightAt(last.X, last.Z);
                if (terrain is { HasWater: true }) lastFloor = Mathf.Max(lastFloor, terrain.WaterLevel);
                float above = last.Y - lastFloor, below = floor - p.Y;
                Vector3 hit = last.Lerp(p, above + below > 1e-4f ? Mathf.Clamp(above / (above + below), 0f, 1f) : 1f);
                points.Add(hit);
                return;
            }
            points.Add(p);
            last = p;
        }
    }
}
