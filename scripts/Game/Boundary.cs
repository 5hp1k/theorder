using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Who is beyond the edge of the battlefield and for how long (see <see cref="MapTerrain.OutOfBounds"/>).
/// A machine that stays out longer than the map allows blows up; the HUD counts the player's seconds down.
/// </summary>
public static class Boundary
{
    static readonly Dictionary<ITarget, float> Out = new();

    /// <summary>Called by a machine every tick it is alive. True once its time out there has run out.</summary>
    public static bool Track(ITarget unit, Vector3 position, float dt)
    {
        var terrain = MapTerrain.Instance;
        if (terrain == null || !terrain.OutOfBounds(position))
        {
            Out.Remove(unit);
            return false;
        }
        float spent = Out.GetValueOrDefault(unit) + dt;
        Out[unit] = spent;
        return spent >= terrain.OutOfBoundsTime;
    }

    /// <summary>Seconds left before the machine is destroyed; negative if it is inside the battlefield.</summary>
    public static float TimeLeft(ITarget unit)
    {
        if (unit == null || MapTerrain.Instance is not { } terrain || !Out.TryGetValue(unit, out float spent)) return -1f;
        return Mathf.Max(0f, terrain.OutOfBoundsTime - spent);
    }

    /// <summary>Starts it afresh (destroyed, respawned, a new battle).</summary>
    public static void Forget(ITarget unit) => Out.Remove(unit);

    public static void Reset() => Out.Clear();
}
