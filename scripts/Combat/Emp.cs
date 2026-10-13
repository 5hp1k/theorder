using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Electromagnetic pulse (a nuclear blast gives one off). Every machine caught in it loses its electronics
/// for a while: no radar, no compass, no target data, no missile lock; computer pilots see only as far as
/// their own eyes. Missiles flying through the pulse, or guided by a machine that was caught, go blind.
/// </summary>
public static class Emp
{
    /// <summary>Seconds the air where a pulse went off stays too charged for a seeker to work.</summary>
    public const float ZoneTime = 6f;

    static readonly Dictionary<ulong, float> Until = new();
    static readonly List<(Vector3 center, float radius, float until)> Zones = new();

    /// <summary>Battle time in seconds; stands still while the game is paused.</summary>
    static float Now => Engine.GetPhysicsFrames() / (float)Engine.PhysicsTicksPerSecond;

    /// <summary>A pulse: everything within the radius is knocked out, for less time the further away it is.</summary>
    public static void Pulse(Vector3 center, float radius, float seconds)
    {
        float now = Now;
        Zones.RemoveAll(z => z.until < now);
        Zones.Add((center, radius, now + ZoneTime));
        foreach (var unit in Units.All)
        {
            if (unit is not GodotObject thing || !GodotObject.IsInstanceValid(thing)) continue;
            float d = unit.GlobalPosition.DistanceTo(center);
            if (d > radius) continue;
            float until = now + seconds * Mathf.Lerp(1f, 0.35f, d / radius);
            ulong id = thing.GetInstanceId();
            if (!Until.TryGetValue(id, out float old) || old < until) Until[id] = until;
        }
    }

    /// <summary>Seconds until the unit's electronics come back; 0 if they are working.</summary>
    public static float Left(object unit)
        => unit is GodotObject thing && Until.TryGetValue(thing.GetInstanceId(), out float until) ? Mathf.Max(0f, until - Now) : 0f;

    public static bool Jammed(object unit) => Left(unit) > 0f;

    /// <summary>Whether a pulse has just gone off around the point.</summary>
    public static bool Charged(Vector3 at)
    {
        float now = Now;
        foreach (var (center, radius, until) in Zones)
            if (until > now && at.DistanceSquaredTo(center) < radius * radius) return true;
        return false;
    }

    /// <summary>Forget everything (a new battle).</summary>
    public static void Reset()
    {
        Until.Clear();
        Zones.Clear();
    }
}
